// Screensaver clock: glass time (position, size and font from the host's `config.clock`), small
// date (with weekday) underneath.
//
// The glass is three layers stacked in the same box:
//  1. .frost — a backdrop-filter blur of whatever is behind, clipped to the glyph shapes via an
//     SVG clipPath, so the digits look like frosted glass over the moving wallpaper;
//  2. the SVG <text> itself, filled with a faint tint and run through #glass-edge, which adds a
//     bright rim on the top-left edges, a soft inner shadow on the bottom-right edges and a drop
//     shadow outside — the light-from-top-left look of a floating glass slab;
//  3. the date: plain small text with a soft shadow for legibility on bright scenes.
// Time and date follow the Windows locale (12/24 h); the AM/PM marker is dropped, like the
// Windows lock screen.
//
// mountClock(parent, {onFontError}) → {set(clockConfig), destroy()}. The filter's pixel offsets and
// blurs are tuned for size 1, so layout() scales them with `size`.

const DEFAULTS = { x: 0.5, y: 0.31, size: 1, font: null, dateFont: "system", showDate: true, tint: "#fff4fc", opacity: 0.16, frost: 18 };
const HEX = /^#[0-9a-fA-F]{6}$/;
const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
const FALLBACK_FAMILY = "Hanami Clock";   // the bundled Tilt Warp (style.css)
const cssString = (s) => `"${String(s).replace(/["\\\x00-\x1f]/g, "")}"`;

export function mountClock(parent, { onFontError } = {}) {
  const root = document.createElement("div");
  root.id = "clock";
  root.innerHTML = `
    <div class="time-wrap">
      <div class="frost"></div>
      <svg class="glass" aria-hidden="true">
        <defs>
          <clipPath id="clock-clip" clipPathUnits="userSpaceOnUse"><text class="glyphs"></text></clipPath>
          <filter id="glass-edge" x="-15%" y="-25%" width="130%" height="150%" color-interpolation-filters="sRGB">
            <!-- the glyph fill is only faintly opaque (the tint), so build a solid mask of the
                 glyph shapes first; every edge effect below is cut from this, not SourceAlpha -->
            <feComponentTransfer in="SourceAlpha" result="shape">
              <feFuncA type="linear" slope="12"/>
            </feComponentTransfer>
            <!-- bright rim: the part of each glyph not covered by a copy shifted down-right -->
            <feOffset in="shape" dx="3.5" dy="3.5" result="shiftDR"/>
            <feComposite in="shape" in2="shiftDR" operator="out" result="rimBand"/>
            <feGaussianBlur in="rimBand" stdDeviation="0.9" result="rimSoft"/>
            <feFlood flood-color="#ffffff" flood-opacity="1"/>
            <feComposite in2="rimSoft" operator="in" result="rim"/>
            <!-- inner shadow: the band left uncovered by a copy shifted up-left -->
            <feOffset in="shape" dx="-6" dy="-7" result="shiftUL"/>
            <feComposite in="shape" in2="shiftUL" operator="out" result="shadeBand"/>
            <feGaussianBlur in="shadeBand" stdDeviation="3" result="shadeSoft"/>
            <feFlood flood-color="#0a0614" flood-opacity="0.55"/>
            <feComposite in2="shadeSoft" operator="in" result="shadeFill"/>
            <feComposite in="shadeFill" in2="shape" operator="in" result="innerShade"/>
            <!-- gentle sheen across the face, strongest top-left -->
            <feGaussianBlur in="shape" stdDeviation="4" result="bump"/>
            <feSpecularLighting in="bump" surfaceScale="3" specularConstant="0.45" specularExponent="24"
                                lighting-color="#ffffff" result="spec">
              <feDistantLight azimuth="225" elevation="48"/>
            </feSpecularLighting>
            <feComposite in="spec" in2="shape" operator="in" result="sheen"/>
            <!-- floating: drop shadow below-right, kept outside the glyphs -->
            <feGaussianBlur in="shape" stdDeviation="10" result="dropBlur"/>
            <feOffset in="dropBlur" dx="8" dy="14" result="dropOff"/>
            <feFlood flood-color="#05030c" flood-opacity="0.4"/>
            <feComposite in2="dropOff" operator="in" result="dropFill"/>
            <feComposite in="dropFill" in2="shape" operator="out" result="drop"/>
            <feMerge>
              <feMergeNode in="drop"/>
              <feMergeNode in="SourceGraphic"/>
              <feMergeNode in="innerShade"/>
              <feMergeNode in="sheen"/>
              <feMergeNode in="rim"/>
            </feMerge>
          </filter>
        </defs>
        <text class="glyphs face" filter="url(#glass-edge)"></text>
      </svg>
    </div>
    <div class="date"></div>`;
  parent.appendChild(root);

  const wrap = root.querySelector(".time-wrap");
  const svg = root.querySelector("svg");
  const texts = [...root.querySelectorAll("text.glyphs")];
  const dateEl = root.querySelector(".date");
  const maskSlope = root.querySelector("feFuncA");
  let cfg = { ...DEFAULTS };

  // Every filter number that is a pixel distance, with its value at size 1.
  const scaled = [];
  for (const el of root.querySelectorAll("feOffset, feGaussianBlur, feSpecularLighting")) {
    for (const attr of ["dx", "dy", "stdDeviation", "surfaceScale"]) {
      if (el.hasAttribute(attr)) scaled.push([el, attr, Number(el.getAttribute(attr))]);
    }
  }

  const timeFmt = new Intl.DateTimeFormat(undefined, { hour: "numeric", minute: "2-digit" });
  const dateFmt = new Intl.DateTimeFormat(undefined, { weekday: "long", day: "numeric", month: "long" });

  function layout() {
    // clipPath (userSpaceOnUse) and the visible text share the wrap's pixel space.
    const w = wrap.clientWidth;
    const h = wrap.clientHeight;
    svg.setAttribute("width", w);
    svg.setAttribute("height", h);
    for (const t of texts) {
      t.setAttribute("x", w / 2);
      t.setAttribute("y", h * 0.5);
    }
    for (const [el, attr, base] of scaled) el.setAttribute(attr, base * cfg.size);

    // Put the time's centre at (x, y), nudged so the visible glyphs and the date stay on screen.
    const W = innerWidth, H = innerHeight;
    const box = texts[0].getBBox();
    const dateW = cfg.showDate ? dateEl.offsetWidth : 0;
    const dateH = cfg.showDate ? dateEl.offsetHeight + 0.004 * H : 0;
    const halfW = Math.max(box.width, dateW) / 2 + 0.02 * W;
    const above = Math.max(box.height, 0) / 2 + 0.02 * H;
    const below = h / 2 + dateH + 0.02 * H;
    const fit = (v, lo, hi) => (lo > hi ? (lo + hi) / 2 : Math.min(hi, Math.max(lo, v)));
    const cx = fit(cfg.x * W, halfW, W - halfW);
    const cy = fit(cfg.y * H, above, H - below);
    root.style.left = `${cx}px`;
    root.style.top = `${cy - h / 2}px`;
  }

  let lastText = "";
  function render() {
    const now = new Date();
    const time = timeFmt.formatToParts(now)
      .filter((p) => p.type !== "dayPeriod")
      .map((p) => p.value)
      .join("")
      .trim();
    if (time !== lastText) {
      lastText = time;
      for (const t of texts) t.textContent = time;
      dateEl.textContent = dateFmt.format(now);
      layout();
    }
  }

  let timer = 0;
  function tick() {
    render();
    // wake just after the next minute boundary rather than polling every second
    const now = new Date();
    timer = setTimeout(tick, 60_000 - (now.getSeconds() * 1000 + now.getMilliseconds()) + 50);
  }

  // Font: null → Tilt Warp; {family} → an installed font (Tilt Warp behind it, so a missing one
  // still looks right); {family, src} → an imported font file, loaded as a FontFace.
  let fontGen = 0, userFace = null;
  async function applyFont() {
    const gen = ++fontGen;
    const f = cfg.font;
    let family = null;
    try {
      if (f?.src) {
        const name = `Hanami User Clock ${gen}`;
        const face = new FontFace(name, `url(${cssString(f.src)})`);
        await face.load();
        if (gen !== fontGen) return;
        if (userFace) document.fonts.delete(userFace);
        document.fonts.add(face);
        userFace = face;
        family = name;
      } else if (f?.family) {
        family = f.family;
        await document.fonts.load(`100px ${cssString(family)}`);
      }
    } catch (err) {
      if (gen !== fontGen) return;
      family = null;
      onFontError?.({ font: f?.id ?? "", message: String(err?.message ?? err) });
    }
    if (gen !== fontGen) return;
    await document.fonts.load(`100px ${cssString(FALLBACK_FAMILY)}`).catch(() => {});
    root.style.setProperty("--clock-family", family ? `${cssString(family)}, ${cssString(FALLBACK_FAMILY)}` : cssString(FALLBACK_FAMILY));
    layout();
  }

  function sanitizeConfig(c) {
    const num = (v, lo, hi, d) => (Number.isFinite(Number(v)) && v !== null ? Math.min(hi, Math.max(lo, Number(v))) : d);
    const font = c?.font && typeof c.font === "object" && (c.font.family || c.font.src) ? c.font : null;
    return {
      x: num(c?.x, 0, 1, DEFAULTS.x),
      y: num(c?.y, 0, 1, DEFAULTS.y),
      size: num(c?.size, 0.5, 1.6, DEFAULTS.size),
      font,
      dateFont: c?.dateFont === "same" ? "same" : "system",
      showDate: c?.showDate !== false,
      tint: typeof c?.tint === "string" && HEX.test(c.tint) ? c.tint : DEFAULTS.tint,
      opacity: num(c?.opacity, 0.04, 0.8, DEFAULTS.opacity),
      frost: num(c?.frost, 0, 40, DEFAULTS.frost),
    };
  }

  // Glass colours: the fill is the tint at its opacity, the rim edge the tint mixed half with white.
  // The filter's solid glyph mask is built from the fill's alpha, so its slope grows as the opacity drops
  // (otherwise every edge effect would fade out with the fill).
  function applyGlass() {
    const [r, g, b] = rgb(cfg.tint);
    const mix = (v) => Math.round((v + 255) / 2);
    root.style.setProperty("--clock-fill", `rgba(${r}, ${g}, ${b}, ${cfg.opacity})`);
    root.style.setProperty("--clock-edge", `rgba(${mix(r)}, ${mix(g)}, ${mix(b)}, 0.28)`);
    root.style.setProperty("--clock-frost", `${cfg.frost * (innerHeight / 1080)}px`);
    maskSlope.setAttribute("slope", Math.max(12, 1.2 / cfg.opacity));
  }

  // Fade in only once the first config's font is ready, so the glass clip never shows the wrong
  // face; without a config (dev page) the defaults show after a moment.
  let revealed = false;
  async function reveal(promise) {
    await promise;
    if (revealed) return;
    revealed = true;
    requestAnimationFrame(() => root.classList.add("shown"));
  }

  let configured = false;
  function set(next) {
    configured = true;
    const prev = cfg;
    cfg = sanitizeConfig(next);
    root.style.setProperty("--cs", cfg.size);
    root.classList.toggle("date-same", cfg.dateFont === "same");
    dateEl.hidden = !cfg.showDate;
    applyGlass();
    const sameFont = JSON.stringify(prev.font) === JSON.stringify(cfg.font) && revealed;
    layout();
    if (sameFont) return;
    reveal(applyFont());
  }

  root.style.setProperty("--cs", cfg.size);
  layout();
  tick();
  addEventListener("resize", () => { applyGlass(); layout(); });
  const fallbackTimer = setTimeout(() => { if (!configured) set(DEFAULTS); }, 1200);
  return {
    set,
    destroy() {
      clearTimeout(timer);
      clearTimeout(fallbackTimer);
      removeEventListener("resize", layout);
      if (userFace) document.fonts.delete(userFace);
      root.remove();
    },
  };
}
