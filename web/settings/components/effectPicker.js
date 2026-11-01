// Effect tiles. Each tile has a gear button that opens that effect's settings drawer.
import { h, svg } from "../util.js";
import { loadEffects } from "../effects-module.js";

// Tile artwork per preset id. Each glyph is repeated at a few positions for a "scene" feel.
const GLYPH = {
  none: '<circle cx="12" cy="12" r="7.5" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M6.8 17.2 17.2 6.8" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>',
  sakura: '<g fill="currentColor">' + [0, 72, 144, 216, 288].map((a) => `<path transform="rotate(${a} 12 12)" d="M12 11c-2.6-1.6-3.4-4.8-1.7-7 .7-.8 1.3-.3 1.7.6.4-.9 1-1.4 1.7-.6 1.7 2.2.9 5.4-1.7 7Z"/>`).join("") + '</g><circle cx="12" cy="12" r="1.3" fill="#fff6"/>',
  snow: '<g stroke="currentColor" stroke-width="1.5" stroke-linecap="round" fill="none"><path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9"/><path d="m9.8 4.8 2.2 2 2.2-2M9.8 19.2l2.2-2 2.2 2"/></g>',
  leaves: '<path fill="currentColor" d="M5 19C5 11 10 5.5 19.5 4.5 19 14 13.5 19 5 19Z"/><path d="M5 19 13 11" stroke="#0003" stroke-width="1.2" stroke-linecap="round"/>',
  fireflies: '<circle cx="12" cy="12" r="6" fill="currentColor" opacity=".25"/><circle cx="12" cy="12" r="3" fill="currentColor"/>',
  rain: '<g stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><path d="M9 4 6.5 11M15 6l-2.5 7M11 13l-2.5 7M17.5 14 15 21"/></g>',
  embers: '<circle cx="12" cy="13" r="6.5" fill="currentColor" opacity=".25"/><path fill="currentColor" d="M12 4c.5 3.2 4.5 5 4.5 9a4.5 4.5 0 0 1-9 0c0-2 1-3 2-4 .2 1.2.7 1.8 1.4 2C10.5 8 11.4 6 12 4Z"/>',
  sparkles: '<path fill="currentColor" d="M12 2.5c.6 4.6 2.9 6.9 7.5 7.5v.1c-4.6.6-6.9 2.9-7.5 7.5h-.1c-.6-4.6-2.9-6.9-7.4-7.5v-.1c4.5-.6 6.8-2.9 7.4-7.5Z"/>',
};
const SPOTS = [[18, 26, 1, -12], [58, 18, 0.7, 20], [76, 54, 0.85, 40], [30, 66, 0.6, 70], [52, 78, 0.5, 5]];

const cap = (id) => id[0].toUpperCase() + id.slice(1);

/**
 * onSelect(presetId | null)  — null is the "Same as desktop" tile (allowSameAsDesktop only)
 * onTune(presetId)           — the gear button
 * Returns an element with set(presetId | null) to show the current choice.
 */
export function effectPicker({ onSelect, onTune, allowSameAsDesktop = false, ariaLabel = "Effect" }) {
  const grid = h("div.preset-grid", { role: "radiogroup", "aria-label": ariaLabel });
  let presets = Object.keys(GLYPH).map((id) => ({ id, name: cap(id), description: "" }));
  let selected;   // preset id, or null for "same as desktop"
  let sameDetail = "";

  function tile(id, name, description, art, gear) {
    const t = h("div.tile", { role: "radio", tabindex: 0, "data-id": id ?? "", "aria-checked": "false", title: description || name,
      onclick: () => choose(id),
      onkeydown: (e) => { if (e.target === t && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); choose(id); } } },
      art, h("span.tile-name", name));
    if (gear) {
      t.append(h("button.tile-gear", { type: "button", title: `${name} settings`, "aria-label": `${name} settings`,
        onclick: (e) => { e.stopPropagation(); onTune(id); } }, svg("gear")));
    }
    return t;
  }

  function build() {
    const art = (id) => h(`div.tile-art.art-${id}`, { html: (id === "none" ? [[50, 50, 1.2, 0]] : SPOTS)
      .map(([x, y, s, r]) => `<svg viewBox="0 0 24 24" style="left:${x}%;top:${y}%;--s:${s};--r:${r}deg">${GLYPH[id] ?? GLYPH.sparkles}</svg>`).join("") });
    grid.replaceChildren(...[
      allowSameAsDesktop ? tile(null, "Same as desktop", "Use whatever effect the desktop wallpaper shows", h("div.tile-art.art-same", svg("monitors")), false) : null,
      ...presets.map((p) => tile(p.id, p.name, p.description, art(p.id), p.id !== "none"))].filter(Boolean));
    if (selected !== undefined) grid.set(selected);
    if (sameDetail) grid.setSameDetail(presets.find((p) => p.name === sameDetail)?.id ?? sameDetail.toLowerCase());
  }

  function choose(id) {
    if (id === selected) return;
    grid.set(id);
    onSelect(id);
  }

  /** Names the effect "Same as desktop" resolves to (allowSameAsDesktop only). */
  grid.setSameDetail = (presetId) => {
    sameDetail = presets.find((p) => p.id === presetId)?.name ?? (presetId ? cap(presetId) : "");
    const t = grid.querySelector('[data-id=""] .tile-name');
    if (t) t.textContent = sameDetail ? `Same as desktop · ${sameDetail}` : "Same as desktop";
  };

  grid.set = (id) => {
    selected = id;
    for (const t of grid.children) {
      const on = t.dataset.id === (id ?? "");
      t.classList.toggle("on", on);
      t.setAttribute("aria-checked", on ? "true" : "false");
    }
  };

  build();
  loadEffects().then((m) => { presets = m.PRESETS; build(); }).catch(() => {});
  return grid;
}
