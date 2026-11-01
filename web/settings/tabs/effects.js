// Effects tab — preset tiles, sliders, interaction, and a live preview running the real engine.
import { h, svg, slider, segmented, toggle, debounce } from "../util.js";
import { scopeSelector } from "../scope.js";
import { loadEffects } from "../effects-module.js";
import { FALLBACK_EFFECT } from "../defaults.js";

// Tile artwork per preset id. Each glyph is repeated at a few positions for a "scene" feel.
const GLYPH = {
  none: '<circle cx="12" cy="12" r="7.5" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M6.8 17.2 17.2 6.8" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>',
  sakura: '<g fill="currentColor">' + [0, 72, 144, 216, 288].map((a) => `<path transform="rotate(${a} 12 12)" d="M12 11c-2.6-1.6-3.4-4.8-1.7-7 .7-.8 1.3-.3 1.7.6.4-.9 1-1.4 1.7-.6 1.7 2.2.9 5.4-1.7 7Z"/>`).join("") + '</g><circle cx="12" cy="12" r="1.3" fill="#fff6"/>',
  snow: '<g stroke="currentColor" stroke-width="1.5" stroke-linecap="round" fill="none"><path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9"/><path d="m9.8 4.8 2.2 2 2.2-2M9.8 19.2l2.2-2 2.2 2"/></g>',
  leaves: '<path fill="currentColor" d="M5 19C5 11 10 5.5 19.5 4.5 19 14 13.5 19 5 19Z"/><path d="M5 19 13 11" stroke="#0003" stroke-width="1.2" stroke-linecap="round"/>',
  fireflies: '<circle cx="12" cy="12" r="6" fill="currentColor" opacity=".25"/><circle cx="12" cy="12" r="3" fill="currentColor"/>',
  rain: '<g stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><path d="M9 4 6.5 11M15 6l-2.5 7M11 13l-2.5 7M17.5 14 15 21"/></g>',
  sparkles: '<path fill="currentColor" d="M12 2.5c.6 4.6 2.9 6.9 7.5 7.5v.1c-4.6.6-6.9 2.9-7.5 7.5h-.1c-.6-4.6-2.9-6.9-7.4-7.5v-.1c4.5-.6 6.8-2.9 7.4-7.5Z"/>',
};
const SPOTS = [[18, 26, 1, -12], [58, 18, 0.7, 20], [76, 54, 0.85, 40], [30, 66, 0.6, 70], [52, 78, 0.5, 5]];

const INTERACTION_HINT = {
  none: "Particles ignore the cursor.",
  repel: "Particles drift away from the cursor.",
  attract: "Particles gather around the cursor.",
  wind: "Moving the cursor blows particles along with it.",
};

const pct = (v) => `${Math.round(v * 100)}%`;
const mult = (v) => `${v.toFixed(1)}×`;
const windFmt = (v) => (Math.abs(v) < 0.025 ? "Calm" : v < 0 ? `← ${Math.abs(v).toFixed(2)}` : `${v.toFixed(2)} →`);

export function createEffectsTab(ctx) {
  const scope = scopeSelector(ctx);
  const tiles = h("div.preset-grid", { role: "radiogroup", "aria-label": "Effect preset" });
  const presetDesc = h("p.card-sub");

  // pending edits not yet acknowledged by the host, and the key they belong to
  let pending = {};
  let pendingKey = "*";
  let lastScope = null;
  let engine = null;
  let mod = null;
  let visible = false;
  let current = { ...FALLBACK_EFFECT };

  const send = debounce(() => {
    const patch = pending, key = pendingKey;
    if (!Object.keys(patch).length) return;
    ctx.assign({ effect: patch }, key)
      .then(() => { for (const k of Object.keys(patch)) if (pending[k] === patch[k]) delete pending[k]; update(ctx.state); })
      .catch(() => { pending = {}; update(ctx.state); });
  }, 150);

  function change(partial, immediate = false) {
    const key = ctx.scopeKey();
    if (key !== pendingKey && Object.keys(pending).length) send.flush();
    pendingKey = key;
    Object.assign(pending, partial);
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
  const resetBtn = h("button.btn.subtle.small", { type: "button", title: "Reset sliders and interaction to the defaults (keeps the preset)",
    onclick: () => change({ ...(mod?.DEFAULT_EFFECT ?? FALLBACK_EFFECT), preset: current.preset }, true) }, svg("reset"), h("span", "Reset"));

  // ------------------------------------------------------------------ preview
  const canvas = h("canvas.preview-canvas");
  const bg = h("div.preview-bg");
  const previewLabel = h("span.preview-chip");
  const previewError = h("div.preview-error", { hidden: true });
  const preview = h("div.preview",
    bg, canvas,
    h("div.preview-top", previewLabel, h("span.preview-chip.live", h("span.live-dot"), "Live preview")),
    h("div.preview-hint", svg("pointer"), h("span", "Move the mouse here · click for a burst")),
    previewError);

  const local = (e) => { const r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
  preview.addEventListener("pointermove", (e) => { preview.classList.add("touched"); engine?.setPointer(...local(e), true); });
  preview.addEventListener("pointerleave", (e) => engine?.setPointer(...local(e), false));
  preview.addEventListener("pointerdown", (e) => { if (e.button === 0) engine?.click(...local(e)); });
  new ResizeObserver(() => engine?.resize()).observe(canvas);
  document.addEventListener("visibilitychange", () => {
    if (!engine) return;
    if (document.hidden) engine.stop(); else if (visible) engine.start();
  });

  loadEffects().then((m) => {
    mod = m;
    buildTiles(m.PRESETS);
    syncControls(current);
    if (visible) startEngine();
  }).catch((err) => {
    console.error("[effects] could not load the effects engine", err);
    previewError.hidden = false;
    previewError.textContent = "The effects engine couldn't be loaded, so the preview is unavailable. Your settings still apply to the wallpaper.";
    buildTiles(Object.keys(GLYPH).map((id) => ({ id, name: id[0].toUpperCase() + id.slice(1), description: "" })));
  });

  function startEngine() {
    if (!mod) return;
    try {
      if (!engine) {
        engine = new mod.EffectsEngine(canvas, { ...current });
        engine.setFpsCap(ctx.state?.settings.performance.fpsCap ?? 60);
      }
      engine.resize();
      engine.start();
    } catch (err) {
      console.error("[effects] preview failed", err);
      previewError.hidden = false;
      previewError.textContent = `Preview error: ${err.message}`;
    }
  }

  // ------------------------------------------------------------------ tiles
  let tileButtons = [];
  let presetList = [];
  function buildTiles(list) {
    presetList = list;
    tileButtons = list.map((p) => {
      const art = h(`div.tile-art.art-${p.id}`, { html: (p.id === "none" ? [[50, 50, 1.2, 0]] : SPOTS)
        .map(([x, y, s, r]) => `<svg viewBox="0 0 24 24" style="left:${x}%;top:${y}%;--s:${s};--r:${r}deg">${GLYPH[p.id] ?? GLYPH.sparkles}</svg>`).join("") });
      return h("button.tile", { type: "button", role: "radio", "data-id": p.id, title: p.description || p.name,
        onclick: () => change({ preset: p.id }, true) }, art, h("span.tile-name", p.name));
    });
    tiles.replaceChildren(...tileButtons);
  }

  function syncControls(e) {
    for (const [k, s] of Object.entries(sliders)) s.set(e[k]);
    interaction.set(e.interaction);
    interactionHint.textContent = INTERACTION_HINT[e.interaction] ?? "";
    const off = e.interaction === "none";
    sliders.interactionStrength.classList.toggle("disabled", off);
    sliders.interactionRadius.classList.toggle("disabled", off);
    sliders.interactionStrength.input.disabled = off;
    sliders.interactionRadius.input.disabled = off;
    burst.set(e.clickBurst);
    for (const b of tileButtons) {
      const on = b.dataset.id === e.preset;
      b.classList.toggle("on", on);
      b.setAttribute("aria-checked", on ? "true" : "false");
    }
    const p = presetList.find((x) => x.id === e.preset);
    presetDesc.textContent = p?.description || "";
    const noParticles = e.preset === "none";
    for (const k of ["density", "speed", "size", "opacity", "wind"]) sliders[k].classList.toggle("muted-ctl", noParticles);
    previewLabel.textContent = p?.name ?? e.preset;
  }

  function paintBackground(s, key) {
    const w = ctx.wallpaper(ctx.assignmentFor(key).wallpaperId);
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

  // ------------------------------------------------------------------ layout
  const row = (title, sub, control) => h("div.row", h("div.row-text", h("div.row-title", title), sub ? h("div.row-sub", sub) : null), control);

  const el = h("section",
    h("header.page-head",
      h("div", h("h1", "Effects"), h("p.page-sub", "Particles drawn over your wallpaper. Changes apply instantly."))),
    scope,
    h("div.effects-layout",
      h("div.effects-side",
        preview,
        h("div.card.pad",
          h("div.card-head", h("h2", "Effect")),
          tiles,
          presetDesc)),
      h("div.effects-main",
        h("div.card.pad",
          h("div.card-head", h("h2", "Look"), resetBtn),
          h("div.slider-grid", sliders.density, sliders.speed, sliders.size, sliders.opacity, sliders.wind)),
        h("div.card.pad",
          h("div.card-head", h("h2", "Cursor interaction")),
          interaction,
          interactionHint,
          h("div.slider-grid", sliders.interactionStrength, sliders.interactionRadius),
          h("div.divider"),
          row("Click burst", "Clicking the desktop sends out a little puff of particles.", burst)))));

  function update(s) {
    if (!s) return;
    scope.update(s);
    const key = ctx.scopeKey();
    if (lastScope !== null && key !== lastScope && Object.keys(pending).length) { send.flush(); }
    const scopeChanged = key !== lastScope;
    lastScope = key;
    const base = ctx.assignmentFor(key).effect;
    const next = { ...base, ...(key === pendingKey ? pending : {}) };
    const diff = {};
    for (const k of Object.keys(next)) if (next[k] !== current[k]) diff[k] = next[k];
    current = next;
    if (Object.keys(diff).length || scopeChanged) { engine?.setConfig(diff); syncControls(current); }
    engine?.setFpsCap(s.settings.performance.fpsCap);
    paintBackground(s, key);
  }

  return {
    el, update,
    show() { visible = true; startEngine(); requestAnimationFrame(() => interaction.set(current.interaction)); },
    hide() { visible = false; engine?.stop(); send.flush(); },
  };
}
