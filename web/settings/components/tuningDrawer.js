// "<Effect> settings" drawer: a live preview running the real engine, the sliders, cursor
// interaction and click burst. A right-side drawer, a bottom sheet on narrow windows. Edits go to
// setEffectTuning (shared by every place that shows that effect), debounced 150 ms.
import { h, svg, slider, segmented, toggle, debounce } from "../util.js";
import { loadEffects } from "../effects-module.js";
import { FALLBACK_EFFECT } from "../defaults.js";
import { card, row } from "./layout.js";

const INTERACTION_HINT = {
  none: "Particles ignore the cursor.",
  repel: "Particles drift away from the cursor.",
  attract: "Particles gather around the cursor.",
  wind: "Moving the cursor blows particles along with it.",
};
const pct = (v) => `${Math.round(v * 100)}%`;
const mult = (v) => `${v.toFixed(1)}×`;
const windFmt = (v) => (Math.abs(v) < 0.025 ? "Calm" : v < 0 ? `← ${Math.abs(v).toFixed(2)}` : `${v.toFixed(2)} →`);

export function createTuningDrawer(ctx) {
  let preset = null;        // the effect being edited; null = closed
  let name = "";
  let wallpaperId = null;   // what the preview sits over
  let current = { ...FALLBACK_EFFECT };
  let pending = {};         // edits the host hasn't acknowledged yet
  let engine = null;
  let mod = null;
  let opener = null;

  const send = debounce(() => {
    const patch = pending, target = preset;
    if (!target || !Object.keys(patch).length) return;
    ctx.mutate("setEffectTuning", { preset: target, tuning: patch })
      .then(() => { for (const k of Object.keys(patch)) if (pending[k] === patch[k]) delete pending[k]; update(ctx.state); })
      .catch(() => { pending = {}; update(ctx.state); });
  }, 150);

  function change(partial, immediate = false) {
    pending = { ...pending, ...partial };
    current = { ...current, ...partial };
    engine?.setConfig(partial);
    syncControls(current);
    send();
    if (immediate) send.flush();
  }

  // ------------------------------------------------------------------ controls
  const sliders = {
    density: slider({ label: "Density", min: 0.1, max: 3, step: 0.1, value: 1, format: mult, onInput: (v) => change({ density: v }) }),
    speed: slider({ label: "Speed", min: 0.2, max: 3, step: 0.1, value: 1, format: mult, onInput: (v) => change({ speed: v }) }),
    size: slider({ label: "Size", min: 0.5, max: 2, step: 0.05, value: 1, format: mult, onInput: (v) => change({ size: v }) }),
    opacity: slider({ label: "Opacity", min: 0, max: 1, step: 0.05, value: 1, format: pct, onInput: (v) => change({ opacity: v }) }),
    wind: slider({ label: "Wind", min: -1, max: 1, step: 0.05, value: 0, format: windFmt, onInput: (v) => change({ wind: v }), hint: "Constant sideways drift" }),
    interactionStrength: slider({ label: "Strength", min: 0, max: 2, step: 0.1, value: 1, format: mult, onInput: (v) => change({ interactionStrength: v }) }),
    interactionRadius: slider({ label: "Radius", min: 40, max: 400, step: 10, value: 160, format: (v) => `${v} px`, onInput: (v) => change({ interactionRadius: v }) }),
  };
  const interaction = segmented(
    [{ value: "none", label: "Off" }, { value: "repel", label: "Repel" }, { value: "attract", label: "Attract" }, { value: "wind", label: "Wind" }],
    "repel", (v) => change({ interaction: v }, true), { ariaLabel: "Cursor interaction" });
  const interactionHint = h("p.field-hint");
  const burst = toggle(true, (v) => change({ clickBurst: v }, true), "Click burst");
  const resetBtn = h("button.btn.subtle.small", { type: "button", title: "Reset this effect's settings to its defaults", onclick: reset },
    svg("reset"), h("span", "Reset to defaults"));

  function reset() {
    pending = {};
    current = { ...FALLBACK_EFFECT, ...(mod?.PRESET_DEFAULTS?.[preset] ?? {}), preset };
    engine?.setConfig({ ...current });
    syncControls(current);
    ctx.mutate("setEffectTuning", { preset, tuning: null }).catch(() => {});
  }

  function syncControls(e) {
    for (const [k, s] of Object.entries(sliders)) s.set(e[k]);
    interaction.set(e.interaction);
    interactionHint.textContent = INTERACTION_HINT[e.interaction] ?? "";
    const off = e.interaction === "none";
    for (const k of ["interactionStrength", "interactionRadius"]) {
      sliders[k].classList.toggle("disabled", off);
      sliders[k].input.disabled = off;
    }
    burst.set(e.clickBurst);
  }

  // ------------------------------------------------------------------ preview
  const canvas = h("canvas.preview-canvas");
  const bg = h("div.preview-bg");
  const previewLabel = h("span.preview-chip");
  const previewError = h("div.preview-error", { hidden: true });
  const preview = h("div.preview", bg, canvas,
    h("div.preview-top", previewLabel, h("span.preview-chip.live", h("span.live-dot"), "Live preview")),
    h("div.preview-hint", svg("pointer"), h("span", "Move the mouse here · click for a burst")),
    previewError);
  const local = (e) => { const r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
  preview.addEventListener("pointermove", (e) => { preview.classList.add("touched"); engine?.setPointer(...local(e), true); });
  preview.addEventListener("pointerleave", (e) => engine?.setPointer(...local(e), false));
  preview.addEventListener("pointerdown", (e) => { if (e.button === 0) engine?.click(...local(e)); });
  new ResizeObserver(() => engine?.resize()).observe(canvas);
  document.addEventListener("visibilitychange", () => {
    if (!engine || !preset) return;
    if (document.hidden) engine.stop(); else engine.start();
  });

  loadEffects().then((m) => { mod = m; }).catch((err) => {
    console.error("[effects] could not load the effects engine", err);
    previewError.hidden = false;
    previewError.textContent = "The effects engine couldn't be loaded, so the preview is unavailable. Your settings still apply to the wallpaper.";
  });

  function startEngine() {
    if (!mod) { requestAnimationFrame(() => preset && startEngine()); return; }
    try {
      if (!engine) engine = new mod.EffectsEngine(canvas, { ...current });
      else engine.setConfig({ ...current });
      engine.setFpsCap(ctx.state?.settings.performance.fpsCap ?? 60);
      engine.resize();
      engine.start();
    } catch (err) {
      console.error("[effects] preview failed", err);
      previewError.hidden = false;
      previewError.textContent = `Preview error: ${err.message}`;
    }
  }

  function paintBackground() {
    const w = ctx.wallpaper(wallpaperId);
    let sig = "none";
    if (w?.builtin || w?.type === "builtin") sig = `scene:${w.id.split(":")[1]}`;
    else if (w?.thumbnail) sig = `img:${w.thumbnail}`;
    if (bg._sig === sig) return;
    bg._sig = sig;
    bg.className = "preview-bg";
    bg.replaceChildren();
    if (sig.startsWith("scene:")) bg.classList.add("scene", `scene-${sig.slice(6)}`);
    else if (sig.startsWith("img:")) bg.append(h("img", { src: w.thumbnail, alt: "", draggable: false }));
    else bg.classList.add("scene", "scene-twilight");
  }

  // ------------------------------------------------------------------ shell
  const title = h("h2#tuning-title");
  const closeBtn = h("button.icon-btn", { type: "button", title: "Close", "aria-label": "Close", onclick: () => close() }, svg("x"));
  const note = h("p.drawer-note");
  const panel = h("aside.drawer", { role: "dialog", "aria-modal": "true", "aria-labelledby": "tuning-title" },
    h("header.drawer-head", title, closeBtn),
    h("div.drawer-body",
      preview,
      card("Look", resetBtn, h("div.slider-grid", sliders.density, sliders.speed, sliders.size, sliders.opacity, sliders.wind)),
      card("Cursor interaction", null,
        interaction,
        interactionHint,
        h("div.slider-grid", sliders.interactionStrength, sliders.interactionRadius),
        h("div.divider"),
        row(null, "Click burst", "Clicking the desktop sends out a little puff of particles.", burst)),
      note));
  const backdrop = h("div.drawer-backdrop", { onclick: () => close() });
  const root = h("div.drawer-root", { hidden: true }, backdrop, panel);
  document.body.append(root);

  root.addEventListener("keydown", (e) => {
    if (e.key === "Escape") { e.stopPropagation(); close(); return; }
    if (e.key !== "Tab") return;
    const f = [...panel.querySelectorAll("button, input, [tabindex='0']")].filter((x) => !x.disabled && x.offsetParent !== null);
    if (!f.length) return;
    const first = f[0], last = f[f.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  });

  /** Opens the drawer for an effect. wallpaperId = what the preview sits over; from = element to refocus on close. */
  function open(id, { wallpaperId: wid = null, from = document.activeElement } = {}) {
    if (preset === id && !root.hidden) return;
    send.flush();
    preset = id;
    wallpaperId = wid;
    opener = from;
    pending = {};
    current = ctx.effectFor(id);
    name = (mod?.PRESETS ?? []).find((p) => p.id === id)?.name ?? id;
    title.textContent = `${name} settings`;
    note.textContent = `Used wherever ${name} is selected.`;
    previewLabel.textContent = name;
    preview.classList.remove("touched");
    syncControls(current);
    paintBackground();
    root.hidden = false;
    void root.offsetWidth;   // lay out the closed state first so the open transition runs
    root.classList.add("open");
    startEngine();
    closeBtn.focus({ preventScroll: true });
  }

  function close() {
    if (root.hidden) return;
    send.flush();
    preset = null;
    root.classList.remove("open");
    engine?.stop();
    setTimeout(() => { if (!preset) root.hidden = true; }, 200);
    opener?.focus?.({ preventScroll: true });
    opener = null;
  }

  function update(s) {
    if (!preset || !s) return;
    const next = { ...ctx.effectFor(preset), ...pending };
    const diff = {};
    for (const k of Object.keys(next)) if (next[k] !== current[k]) diff[k] = next[k];
    current = next;
    if (Object.keys(diff).length) { engine?.setConfig(diff); syncControls(current); }
    engine?.setFpsCap(s.settings.performance.fpsCap);
    paintBackground();
  }

  return { open, close, update };
}
