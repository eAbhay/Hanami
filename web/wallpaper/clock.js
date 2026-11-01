// Screensaver clock: glass time slightly above centre, small date (with weekday) underneath.
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

export function mountClock(parent) {
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
    }
  }

  let timer = 0;
  function tick() {
    render();
    // wake just after the next minute boundary rather than polling every second
    const now = new Date();
    timer = setTimeout(tick, 60_000 - (now.getSeconds() * 1000 + now.getMilliseconds()) + 50);
  }

  layout();
  tick();
  addEventListener("resize", layout);
  requestAnimationFrame(() => root.classList.add("shown"));
  return () => {
    clearTimeout(timer);
    removeEventListener("resize", layout);
    root.remove();
  };
}
