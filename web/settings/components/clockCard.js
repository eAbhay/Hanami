// Screensaver "Clock" card: a monitor-shaped placement preview (drag the clock, or use the anchor
// buttons / arrow keys), a size slider, a font picker (bundled · imported · installed) and the date
// options. Edits go to updateSettings({screensaver:{clock}}) debounced 150 ms. Contract: docs/CONTRACTS.md.
import { h, svg, slider, toggle, debounce } from "../util.js";
import { card, row } from "./layout.js";

const DEFAULT_CLOCK = { x: 0.5, y: 0.31, size: 1, font: null, dateFont: "system", showDate: true };
const BUNDLED_FAMILY = "Hanami Clock Preview";   // Tilt Warp, declared in style.css
const SNAP = 0.02;                               // drag snaps to the centre lines within 2%
const ANCHORS = [                                // [label, x, y]
  ["Top left", 0.15, 0.13], ["Top", 0.5, 0.13], ["Top right", 0.85, 0.13],
  ["Left", 0.15, 0.5], ["Centre", 0.5, 0.5], ["Right", 0.85, 0.5],
  ["Bottom left", 0.15, 0.87], ["Bottom", 0.5, 0.87], ["Bottom right", 0.85, 0.87],
];
const clamp = (v, lo, hi) => (lo > hi ? (lo + hi) / 2 : Math.min(hi, Math.max(lo, v)));
const q = (s) => `"${String(s).replace(/["\\\x00-\x1f]/g, "")}"`;

export function createClockCard(ctx) {
  let clock = { ...DEFAULT_CLOCK };
  let pending = {};
  let fonts = { bundled: [], system: [], imported: [] };
  let fontsLoaded = false;
  const faces = new Map();   // imported font id → CSS family name (once its FontFace is added)

  const send = debounce(() => {
    const patch = pending;
    if (!Object.keys(patch).length) return;
    ctx.mutate("updateSettings", { screensaver: { clock: patch } })
      .then(() => { for (const k of Object.keys(patch)) if (pending[k] === patch[k]) delete pending[k]; })
      .catch(() => { pending = {}; sync(); });
  }, 150);

  function change(partial, immediate = false) {
    pending = { ...pending, ...partial };
    clock = { ...clock, ...partial };
    sync();
    send();
    if (immediate) send.flush();
  }

  // ------------------------------------------------------------------ placement preview
  const bg = h("div.preview-bg");
  const timeEl = h("div.clock-time");
  const dateEl = h("div.clock-date");
  const handle = h("div.clock-handle", {
    tabindex: "0", role: "slider", "aria-label": "Clock position (arrow keys move it)", title: "Drag to place the clock",
  }, timeEl, dateEl);
  const guideV = h("div.clock-guide.v");
  const guideH = h("div.clock-guide.h");
  const stage = h("div.preview.clock-stage", bg, guideV, guideH, handle);

  function timeText() {
    return new Intl.DateTimeFormat(undefined, { hour: "numeric", minute: "2-digit" }).formatToParts(new Date())
      .filter((p) => p.type !== "dayPeriod").map((p) => p.value).join("").trim();
  }
  const dateText = () => new Intl.DateTimeFormat(undefined, { weekday: "long", day: "numeric", month: "long" }).format(new Date());

  /** Same on-screen clamp as the wallpaper page (clock.js layout()), against the preview's own size. */
  function placed() {
    const W = stage.clientWidth, H = stage.clientHeight;
    if (!W || !H) return { cx: clock.x * (W || 1), cy: clock.y * (H || 1), W, H };
    const halfW = Math.max(timeEl.offsetWidth, clock.showDate ? dateEl.offsetWidth : 0) / 2 + 0.02 * W;
    const above = timeEl.offsetHeight / 2 + 0.02 * H;
    const below = 0.26 * H * clock.size / 2 + (clock.showDate ? dateEl.offsetHeight : 0) + 0.02 * H;
    return { cx: clamp(clock.x * W, halfW, W - halfW), cy: clamp(clock.y * H, above, H - below), W, H };
  }

  function paintHandle() {
    const H = stage.clientHeight;
    if (!H) return;
    timeEl.style.fontSize = `${H * 0.22 * clock.size}px`;
    dateEl.style.fontSize = `${Math.max(7, H * 0.026 * clock.size)}px`;
    dateEl.hidden = !clock.showDate;
    const { cx, cy } = placed();
    handle.style.left = `${cx}px`;
    handle.style.top = `${cy}px`;
    handle.setAttribute("aria-valuetext", `${Math.round(clock.x * 100)}% across, ${Math.round(clock.y * 100)}% down`);
    guideV.classList.toggle("on", Math.abs(clock.x - 0.5) < 0.001);
    guideH.classList.toggle("on", Math.abs(clock.y - 0.5) < 0.001 || Math.abs(clock.y - DEFAULT_CLOCK.y) < 0.001);
    guideH.style.top = `${(Math.abs(clock.y - 0.5) < 0.001 ? 0.5 : DEFAULT_CLOCK.y) * 100}%`;
  }

  let drag = null;
  handle.addEventListener("pointerdown", (e) => {
    if (e.button !== 0) return;
    e.preventDefault();
    handle.setPointerCapture(e.pointerId);
    const r = stage.getBoundingClientRect(), p = placed();
    drag = { dx: e.clientX - r.left - p.cx, dy: e.clientY - r.top - p.cy };
    handle.classList.add("dragging");
    handle.focus({ preventScroll: true });
  });
  handle.addEventListener("pointermove", (e) => {
    if (!drag) return;
    const r = stage.getBoundingClientRect();
    let x = (e.clientX - r.left - drag.dx) / r.width, y = (e.clientY - r.top - drag.dy) / r.height;
    if (Math.abs(x - 0.5) < SNAP) x = 0.5;
    if (Math.abs(y - 0.5) < SNAP) y = 0.5;
    else if (Math.abs(y - DEFAULT_CLOCK.y) < SNAP) y = DEFAULT_CLOCK.y;
    // keep what the user sees: clamp like the page does, then store that
    const keep = { x: clock.x, y: clock.y };
    clock = { ...clock, x, y };
    const p = placed();
    clock = { ...clock, ...keep };
    change({ x: round(p.cx / p.W), y: round(p.cy / p.H) });
  });
  const endDrag = () => { drag = null; handle.classList.remove("dragging"); send.flush(); };
  handle.addEventListener("pointerup", endDrag);
  handle.addEventListener("pointercancel", endDrag);
  handle.addEventListener("keydown", (e) => {
    const step = e.shiftKey ? 0.05 : 0.01;
    const d = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[e.key];
    if (!d) return;
    e.preventDefault();
    change({ x: round(clamp(clock.x + d[0], 0, 1)), y: round(clamp(clock.y + d[1], 0, 1)) });
  });
  new ResizeObserver(() => paintHandle()).observe(stage);

  const round = (v) => Math.round(v * 1000) / 1000;

  const anchors = h("div.clock-anchors", { role: "group", "aria-label": "Clock position presets" },
    ANCHORS.map(([label, x, y]) => h("button.anchor", {
      type: "button", title: label, "aria-label": label, "data-x": x, "data-y": y,
      onclick: () => change({ x, y }, true),
    }, h("span.anchor-dot"))));
  const resetPos = h("button.btn.subtle.small", { type: "button", title: "Back to the default spot",
    onclick: () => change({ x: DEFAULT_CLOCK.x, y: DEFAULT_CLOCK.y }, true) }, svg("reset"), h("span", "Default spot"));

  // ------------------------------------------------------------------ size, font, date
  const size = slider({ label: "Size", min: 0.5, max: 1.6, step: 0.05, value: 1, format: (v) => `${Math.round(v * 100)}%`,
    onInput: (v) => change({ size: v }) });

  const fontSelect = h("select.text-input.font-select", { "aria-label": "Clock font",
    onchange: () => change({ font: fontSelect.value || null }, true) });
  const importBtn = h("button.btn.small", { type: "button", onclick: importFont }, svg("plus"), h("span", "Import font…"));
  const removeBtn = h("button.btn.subtle.small", { type: "button", hidden: true, title: "Delete this imported font", onclick: removeFont },
    svg("x"), h("span", "Remove"));
  const fontNote = h("p.field-hint.font-note", { hidden: true });

  const dateSame = toggle(false, (v) => change({ dateFont: v ? "same" : "system" }, true), "Use the clock font for the date");
  const showDate = toggle(true, (v) => change({ showDate: v }, true), "Show date");

  async function loadFonts() {
    try {
      fonts = await ctx.call("getFonts");
      fontsLoaded = true;
      for (const f of fonts.imported ?? []) addFace(f);
      rebuildFonts();
    } catch (err) { console.error("[clock] getFonts failed", err); }
  }

  function addFace(f) {
    if (faces.has(f.id) || !f.src) return;
    const name = `Hanami Imported ${f.id}`;
    faces.set(f.id, name);
    try {
      const face = new FontFace(name, `url(${q(f.src)})`);
      document.fonts.add(face);
      face.load().then(paintFont).catch(() => {});   // unreadable file: the option just shows the default face
    } catch { /* preview only */ }
  }

  function familyFor(setting) {
    if (setting?.startsWith("system:")) return `${q(setting.slice(7))}, ${q(BUNDLED_FAMILY)}`;
    if (setting?.startsWith("library:")) { const n = faces.get(setting.slice(8)); return n ? `${q(n)}, ${q(BUNDLED_FAMILY)}` : q(BUNDLED_FAMILY); }
    return q(BUNDLED_FAMILY);
  }

  function rebuildFonts() {
    const opt = (value, label, family) => h("option", { value, style: { fontFamily: family } }, label);
    const imported = fonts.imported ?? [];
    const sel = clock.font ?? "";
    fontSelect.replaceChildren(
      h("optgroup", { label: "Bundled" }, opt("", "Tilt Warp (default)", q(BUNDLED_FAMILY))),
      imported.length ? h("optgroup", { label: "Imported" }, imported.map((f) => opt(`library:${f.id}`, f.name, familyFor(`library:${f.id}`)))) : null,
      (fonts.system ?? []).length ? h("optgroup", { label: "Installed" }, fonts.system.map((n) => opt(`system:${n}`, n, q(n)))) : null);
    // a selected font that is not in the lists (removed file, uninstalled family) stays visible
    if (sel && ![...fontSelect.options].some((o) => o.value === sel)) {
      const missing = sel.startsWith("library:") ? sel.slice(8) : sel.slice(7);
      fontSelect.prepend(h("option", { value: sel }, `${missing} (not found — showing Tilt Warp)`));
    }
    fontSelect.value = sel;
    paintFont();
  }

  async function importFont() {
    importBtn.disabled = true;
    try {
      const r = await ctx.call("importFont");
      if (r?.imported) {
        await loadFonts();
        change({ font: `library:${r.imported.id}` }, true);
        ctx.toast(`Imported ${r.imported.name}`, "success");
      }
    } catch (err) { ctx.toastError(err); }
    finally { importBtn.disabled = false; }
  }

  async function removeFont() {
    const id = clock.font?.startsWith("library:") ? clock.font.slice(8) : null;
    if (!id) return;
    try {
      pending = { ...pending }; delete pending.font;
      await ctx.mutate("deleteFont", { id });
      await loadFonts();
    } catch { /* toasted by mutate */ }
  }

  function paintFont() {
    const f = clock.font ?? "";
    const family = familyFor(f);
    timeEl.style.fontFamily = family;
    dateEl.style.fontFamily = clock.dateFont === "same" ? family : "";
    fontSelect.value = f;
    removeBtn.hidden = !f.startsWith("library:");
    paintHandle();
  }

  // ------------------------------------------------------------------ sync
  function sync() {
    size.set(clock.size);
    showDate.set(clock.showDate);
    dateSame.set(clock.dateFont === "same");
    dateSame.input.disabled = !clock.showDate;
    timeEl.textContent = timeText();
    dateEl.textContent = dateText();
    paintFont();
    for (const b of anchors.children) {
      const on = Math.abs(clock.x - Number(b.dataset.x)) < 0.001 && Math.abs(clock.y - Number(b.dataset.y)) < 0.001;
      b.classList.toggle("on", on);
      b.setAttribute("aria-pressed", on ? "true" : "false");
    }
  }

  const el = card("Clock", "Shown on the screensaver",
    h("div.clock-layout",
      h("div.clock-place",
        stage,
        h("div.clock-place-tools", anchors, resetPos)),
      h("div.clock-opts",
        size,
        h("div.field",
          h("label.field-label", "Font"),
          h("div.font-row", fontSelect, importBtn, removeBtn),
          fontNote),
        h("div.divider"),
        row(null, "Use the clock font for the date", null, dateSame),
        row(null, "Show date", null, showDate))));

  let lastWallpaper = null;
  function paintBackground(s) {
    const cfg = s.settings.screensaver;
    const id = cfg.wallpaperId ?? ctx.assignmentFor(ctx.scopeKey()).wallpaperId;
    const w = ctx.wallpaper(id);
    let sig = "scene:twilight";
    if (w?.builtin || w?.type === "builtin") sig = `scene:${w.id.split(":")[1]}`;
    else if (w?.thumbnail) sig = `img:${w.thumbnail}`;
    if (sig === lastWallpaper) return;
    lastWallpaper = sig;
    bg.className = "preview-bg";
    bg.replaceChildren();
    if (sig.startsWith("scene:")) bg.classList.add("scene", `scene-${sig.slice(6)}`);
    else bg.append(h("img", { src: w.thumbnail, alt: "", draggable: false }));
  }

  function update(s) {
    const c = s.settings.screensaver?.clock;
    if (!c) { el.hidden = true; return; }   // host without clock settings
    el.hidden = false;
    if (!fontsLoaded) { fontsLoaded = "pending"; loadFonts(); }
    clock = { ...DEFAULT_CLOCK, ...c, ...pending };
    paintBackground(s);
    sync();
    if (fontsLoaded === true && fontSelect.value !== (clock.font ?? "")) rebuildFonts();   // e.g. a font removed on the host
  }
  setInterval(() => { timeEl.textContent = timeText(); dateEl.textContent = dateText(); }, 30_000);

  return { el, update };
}
