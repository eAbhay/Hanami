// Wallpaper choice, in two shapes that share the selection logic:
//   manage — the Wallpaper tab: big cards in two sections, rename / delete menu, library meta.
//   pick   — compact tiles (Screensaver tab), optionally led by a "Same as desktop" tile.
import { h, svg, formatBytes, formatDuration, resLabel, dialog, debounce } from "../util.js";
import { ensureThumbnails, thumbnailFailed } from "../thumbs.js";
import { ensureLockStills } from "../lockstills.js";
import { wallpaperThumb as thumbFor, TYPE_LABEL, isBuiltin } from "../wallthumb.js";

const SCENE_BLURB = { twilight: "Violet dusk fading to amber", dawn: "Pink sunrise, golden haze", night: "Deep blue sky, quiet stars" };

/**
 * ctx          the settings context (app.js)
 * onSelect(w)  w is a WallpaperInfo, or null for "Same as desktop"; may return a promise (the card shows
 *              a busy state until it settles)
 * onImport()   manage only: the empty library's Import button
 * Returns { el, update(state, selectedId), hide() } — selectedId null = "Same as desktop".
 */
export function wallpaperPicker(ctx, { onSelect, onImport, manage = false, allowSameAsDesktop = false }) {
  const els = new Map();   // id → card / tile element
  let applying = null;
  let sig = "";

  let sameLabel = "Same as desktop";

  const choose = async (w) => {
    applying = w?.id ?? "";
    els.get(applying)?.classList.add("applying");
    try { await onSelect(w); } catch {} finally {
      els.get(applying)?.classList.remove("applying");
      applying = null;
    }
  };

  // ------------------------------------------------------------------ pick mode
  function pickTile(id, name, thumb, w) {
    return h("button.pick", { type: "button", role: "radio", "data-id": id ?? "", title: name, onclick: () => choose(w) },
      h("div.thumb-wrap", thumb, h("span.applied-mark", svg("check"))),
      h("span.pick-name", name));
  }

  function renderTiles(grid, s) {
    const next = sameLabel + "|" + s.wallpapers.map((w) => `${w.id}|${w.name}|${w.thumbnail ?? ""}`).join(",");
    if (next === sig) return;
    sig = next;
    els.clear();
    const list = [...s.wallpapers.filter(isBuiltin), ...s.wallpapers.filter((w) => !isBuiltin(w))];
    const tiles = list.map((w) => pickTile(w.id, w.name, thumbFor(w), w));
    if (allowSameAsDesktop) {
      const same = h("div.thumb.pick-same", svg("monitors"), h("span", "Each monitor's own"));
      tiles.unshift(pickTile(null, sameLabel, same, null));
    }
    for (const t of tiles) els.set(t.dataset.id, t);
    grid.replaceChildren(...tiles);
  }

  // ------------------------------------------------------------------ manage mode
  const metaFor = (w) => {
    if (isBuiltin(w)) return SCENE_BLURB[w.id.split(":")[1]] ?? "Animated scene";
    return [w.type === "video" ? formatDuration(w.durationSec) : "", resLabel(w.width, w.height)].filter(Boolean).join(" · ") || TYPE_LABEL[w.type];
  };
  // What comes back when this wallpaper is applied: the effect of its look (none until it has one).
  const fxName = (w) => {
    const p = ctx.state.settings.looks?.[w.id]?.desktop?.effectPreset;
    return !p ? "" : p === "none" ? "No effect" : p[0].toUpperCase() + p.slice(1);
  };
  const fxBadge = (w) => (fxName(w) ? h("span.badge.badge-fx", { title: "Effect saved with this wallpaper" }, fxName(w)) : null);
  const cardSig = (w) => [fxName(w), w.name, w.thumbnail ?? "", thumbnailFailed(w.id), w.durationSec, w.width, w.height, w.sizeBytes].join("|");

  function buildCard(w) {
    const menuBtn = h("button.icon-btn.card-menu", { type: "button", title: "More options", "aria-label": `More options for ${w.name}`,
      onclick: (e) => { e.stopPropagation(); openMenu(menuBtn, w); } }, svg("more"));
    const pick = () => choose(ctx.wallpaper(w.id) ?? w);
    const card = h("div.card.wp", { tabindex: 0, role: "button", "aria-label": `Apply ${w.name}`, "data-id": w.id, onclick: pick,
      onkeydown: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); pick(); } } },
      h("div.thumb-wrap",
        thumbFor(w),
        h(`span.badge.badge-${w.type}`, TYPE_LABEL[w.type] ?? w.type),
        fxBadge(w),
        h("span.applied-mark", { title: "Applied" }, svg("check"))),
      h("div.card-body",
        h("div.card-text", h("div.card-title", { title: w.name }, w.name),
          h("div.card-meta", h("span", metaFor(w)), w.builtin ? null : h("span.dot-sep", formatBytes(w.sizeBytes)))),
        menuBtn));
    card._sig = cardSig(w);
    return card;
  }

  function renderGrid(grid, list) {
    const keep = [];
    for (const w of list) {
      let c = els.get(w.id);
      if (!c || c._sig !== cardSig(w)) {
        const fresh = buildCard(w);
        if (c) c.replaceWith(fresh);
        c = fresh;
        els.set(w.id, c);
      }
      keep.push(c);
    }
    keep.forEach((c, i) => { if (grid.children[i] !== c) grid.insertBefore(c, grid.children[i] ?? null); });
    while (grid.children.length > keep.length) grid.lastChild.remove();
  }

  async function rename(w) {
    const name = await dialog({ title: "Rename wallpaper", input: { value: w.name }, confirmLabel: "Rename" });
    if (!name || name === w.name) return;
    try { await ctx.mutate("renameWallpaper", { id: w.id, name }); } catch {}
  }

  async function remove(w) {
    const usedOn = Object.entries(ctx.state.settings.assignments).filter(([, a]) => a.wallpaperId === w.id).map(([k]) => (k === "*" ? "all monitors" : ctx.monitorName(k)));
    const ok = await dialog({
      title: `Delete “${w.name}”?`,
      body: `This removes the ${w.type} (${formatBytes(w.sizeBytes)}) from your library. ${usedOn.length ? `It's currently applied to ${usedOn.join(", ")}. ` : ""}This can't be undone.`,
      confirmLabel: "Delete", danger: true,
    });
    if (!ok) return;
    try { await ctx.mutate("deleteWallpaper", { id: w.id }); ctx.toast(`Deleted ${w.name}`, "success", 2200); } catch {}
  }

  // card menu
  let menu = null;
  function closeMenu() {
    if (!menu) return;
    menu.remove(); menu = null;
    document.removeEventListener("pointerdown", outside, true);
    document.removeEventListener("keydown", esc, true);
  }
  const outside = (e) => { if (menu && !menu.contains(e.target)) closeMenu(); };
  const esc = (e) => { if (e.key === "Escape") { e.stopPropagation(); closeMenu(); } };

  function openMenu(anchor, w) {
    const wasSame = menu && menu._for === w.id;
    closeMenu();
    if (wasSame) return;
    const builtin = isBuiltin(w);
    const item = (ic, label, fn, opts = {}) => h(`button.menu-item${opts.danger ? ".danger" : ""}`, {
      type: "button", disabled: !!opts.disabled, title: opts.title ?? null,
      onclick: () => { closeMenu(); fn(); } }, svg(ic), h("span", label));
    menu = h("div.menu", { role: "menu" },
      item("check", "Apply", () => choose(w)),
      item("pencil", "Rename…", () => rename(w), { disabled: builtin, title: builtin ? "Built-in scenes can't be renamed" : null }),
      h("div.menu-sep"),
      item("trash", "Delete…", () => remove(w), { danger: true, disabled: builtin, title: builtin ? "Built-in scenes can't be deleted" : null }));
    menu._for = w.id;
    document.body.append(menu);
    const r = anchor.getBoundingClientRect();
    menu._anchor = anchor;
    menu._anchorTop = r.top;
    const mw = menu.offsetWidth, mh = menu.offsetHeight;
    let top = r.bottom + 6;
    if (top + mh > window.innerHeight - 8) top = r.top - mh - 6;
    menu.style.left = `${Math.max(8, Math.min(window.innerWidth - mw - 8, r.right - mw))}px`;
    menu.style.top = `${Math.max(8, top)}px`;
    document.addEventListener("pointerdown", outside, true);
    document.addEventListener("keydown", esc, true);
    menu.querySelector("button:not([disabled])")?.focus({ preventScroll: true });
  }
  // close when the page scrolls under the menu (ignore scroll events that don't move its anchor)
  document.addEventListener("scroll", () => {
    if (menu && Math.abs(menu._anchor.getBoundingClientRect().top - menu._anchorTop) > 2) closeMenu();
  }, true);
  window.addEventListener("resize", closeMenu);

  // ------------------------------------------------------------------ layout
  let el, render;
  if (manage) {
    const builtinGrid = h("div.grid");
    const libGrid = h("div.grid");
    const libMeta = h("span.section-meta");
    const empty = h("div.empty",
      h("div.empty-art", svg("image")),
      h("div.empty-title", "Your library is empty"),
      h("div.empty-text", "Import an MP4 / WebM video or a JPG / PNG / WebP image to use it as a wallpaper."),
      h("button.btn", { type: "button", onclick: () => onImport?.() }, svg("upload"), h("span", "Import")));
    el = h("div",
      h("div.sub-head", h("h3", "Built-in scenes"), h("span.section-meta", "Animated, no files needed")),
      builtinGrid,
      h("div.sub-head", h("h3", "Your library"), libMeta),
      libGrid,
      empty);
    // saveThumbnail only returns {ok}; re-fetch State so the new thumbnail URL shows up.
    const refetch = debounce(() => ctx.mutate("getState").catch(() => {}), 250);
    render = (s) => {
      const ids = new Set(s.wallpapers.map((w) => w.id));
      for (const id of [...els.keys()]) if (!ids.has(id)) { els.get(id).remove(); els.delete(id); }
      const lib = s.wallpapers.filter((w) => !isBuiltin(w));
      renderGrid(builtinGrid, s.wallpapers.filter(isBuiltin));
      renderGrid(libGrid, lib);
      empty.hidden = lib.length > 0;
      libGrid.hidden = lib.length === 0;
      libMeta.textContent = lib.length ? `${lib.length} item${lib.length === 1 ? "" : "s"} · ${formatBytes(lib.reduce((a, w) => a + (w.sizeBytes || 0), 0))}` : "";
      ensureThumbnails(s.wallpapers, ctx.call, (id, ok) => {
        if (ok) refetch();
        else if (ctx.wallpaper(id)) render(ctx.state);
      });
      ensureLockStills(s, ctx.call, (_id, ok) => { if (ok) refetch(); });   // so State shows the new lockStill
    };
  } else {
    el = h("div.pick-grid", { role: "radiogroup", "aria-label": "Wallpaper" });
    render = (s) => renderTiles(el, s);
  }

  /** Names the leading tile (pick mode): "Same as desktop", or "Same as light" for a dark slot. */
  function setSameLabel(text) {
    if (text === sameLabel) return;
    sameLabel = text;
    sig = "";
  }

  /** marks = {light, dark} wallpaper ids of the slot being edited (light/dark wallpapers on), else null. */
  function update(s, selectedId, marks = null) {
    render(s);
    const sel = selectedId ?? "";
    for (const [id, c] of els) {
      c.querySelector(".slot-mark")?.remove();
      const glyphs = marks && id ? (id === marks.light ? "☀" : "") + (id === marks.dark ? "☾" : "") : "";
      if (glyphs) (c.querySelector(".thumb-wrap") ?? c).append(h("span.slot-mark", { title: "Light / dark wallpaper of this slot" }, glyphs));
      const on = id === sel;
      c.classList.toggle(manage ? "applied" : "on", on);
      c.setAttribute(manage ? "aria-pressed" : "aria-checked", on ? "true" : "false");
      if (id === applying) c.classList.add("applying");
    }
  }

  return { el, update, setSameLabel, hide: closeMenu };
}
