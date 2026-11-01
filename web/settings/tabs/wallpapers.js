// Wallpapers tab — library grid, apply to scope, import, rename/delete, thumbnails.
import { h, svg, formatBytes, formatDuration, resLabel, dialog, progressBar, debounce } from "../util.js";
import { scopeSelector } from "../scope.js";
import { ensureThumbnails, thumbnailFailed } from "../thumbs.js";

const TYPE_LABEL = { video: "Video", image: "Image", builtin: "Built-in" };
const SCENE_BLURB = { twilight: "Violet dusk fading to amber", dawn: "Pink sunrise, golden haze", night: "Deep blue sky, quiet stars" };

export function createWallpapersTab(ctx) {
  const scope = scopeSelector(ctx);
  const progress = progressBar();
  const importBtn = h("button.btn.primary", { type: "button", onclick: doImport }, svg("upload"), h("span", "Import video / image"));

  const builtinGrid = h("div.grid");
  const libGrid = h("div.grid");
  const libMeta = h("span.section-meta");
  const empty = h("div.empty",
    h("div.empty-art", svg("image")),
    h("div.empty-title", "Your library is empty"),
    h("div.empty-text", "Import an MP4 / WebM video or a JPG / PNG / WebP image to use it as a wallpaper."),
    h("button.btn", { type: "button", onclick: doImport }, svg("upload"), h("span", "Import")));

  const el = h("section",
    h("header.page-head",
      h("div", h("h1", "Wallpapers"), h("p.page-sub", "Pick a scene or one of your own videos. Click a card to apply it.")),
      h("div.page-actions", importBtn)),
    scope,
    progress,
    h("div.section-head", h("h2", "Built-in scenes"), h("span.section-meta", "Animated, no files needed")),
    builtinGrid,
    h("div.section-head", h("h2", "Your library"), libMeta),
    libGrid,
    empty);

  const cards = new Map();   // id → card element
  let applying = null;
  // saveThumbnail only returns {ok}; re-fetch State so the new thumbnail URL shows up.
  const applyFresh = debounce(() => ctx.mutate("getState").catch(() => {}), 250);

  ctx.on("progress", (p) => {
    if (p?.task !== "import") return;
    progress.update(p);
    if (p.done >= p.total) setTimeout(() => progress.reset(), 900);
  });

  async function doImport() {
    importBtn.disabled = true;
    try {
      const r = await ctx.call("importMedia");
      if (r?.cancelled) return;
      const n = r?.imported?.length ?? 0;
      await ctx.mutate("getState");
      if (n) ctx.toast(`Imported ${n} wallpaper${n === 1 ? "" : "s"}`, "success");
    } catch (err) {
      ctx.toastError(err);
    } finally {
      importBtn.disabled = false;
      progress.reset();
    }
  }

  async function apply(w) {
    const key = ctx.scopeKey();
    if (ctx.assignmentFor(key).wallpaperId === w.id) return;
    applying = w.id;
    cards.get(w.id)?.classList.add("applying");
    try {
      await ctx.assign({ wallpaperId: w.id });
      ctx.toast(`${w.name} applied ${key === "*" ? "to all monitors" : `to ${ctx.monitorName(key)}`}`, "success", 2200);
    } catch {} finally {
      applying = null;
      cards.get(w.id)?.classList.remove("applying");
    }
  }

  async function rename(w) {
    const name = await dialog({ title: "Rename wallpaper", input: { value: w.name }, confirmLabel: "Rename" });
    if (!name || name === w.name) return;
    try { await ctx.mutate("renameWallpaper", { id: w.id, name }); } catch {}
  }

  async function remove(w) {
    const s = ctx.state;
    const usedOn = Object.entries(s.settings.assignments).filter(([, a]) => a.wallpaperId === w.id).map(([k]) => (k === "*" ? "all monitors" : ctx.monitorName(k)));
    const ok = await dialog({
      title: `Delete “${w.name}”?`,
      body: `This removes the ${w.type} (${formatBytes(w.sizeBytes)}) from your library. ${usedOn.length ? `It's currently applied to ${usedOn.join(", ")}. ` : ""}This can't be undone.`,
      confirmLabel: "Delete", danger: true,
    });
    if (!ok) return;
    try { await ctx.mutate("deleteWallpaper", { id: w.id }); ctx.toast(`Deleted ${w.name}`, "success", 2200); } catch {}
  }

  // ------------------------------------------------------------------ card rendering
  function thumbFor(w) {
    if (w.builtin || w.type === "builtin") {
      const scene = w.id.split(":")[1] ?? "twilight";
      return h(`div.thumb.scene.scene-${scene}`, h("span.scene-glow"));
    }
    if (w.thumbnail) {
      const img = h("img", { src: w.thumbnail, alt: "", loading: "lazy", decoding: "async", draggable: false });
      img.addEventListener("error", () => img.replaceWith(h("div.thumb-fallback", svg(w.type === "video" ? "play" : "image"))), { once: true });
      return h("div.thumb", img);
    }
    if (thumbnailFailed(w.id) || !w.src) return h("div.thumb", h("div.thumb-fallback", svg(w.type === "video" ? "play" : "image"), h("span", "No preview")));
    return h("div.thumb.loading", h("div.thumb-fallback", h("span.spinner"), h("span", "Making preview…")));
  }

  function metaFor(w) {
    if (w.builtin || w.type === "builtin") return SCENE_BLURB[w.id.split(":")[1]] ?? "Animated scene";
    return [w.type === "video" ? formatDuration(w.durationSec) : "", resLabel(w.width, w.height)].filter(Boolean).join(" · ") || TYPE_LABEL[w.type];
  }

  function cardSig(w) {
    return [w.name, w.thumbnail ?? "", thumbnailFailed(w.id), w.durationSec, w.width, w.height, w.sizeBytes].join("|");
  }

  function buildCard(w) {
    const menuBtn = h("button.icon-btn.card-menu", { type: "button", title: "More options", "aria-label": `More options for ${w.name}`,
      onclick: (e) => { e.stopPropagation(); openMenu(menuBtn, w); } }, svg("more"));
    const card = h("div.card.wp", { tabindex: 0, role: "button", "aria-label": `Apply ${w.name}`, "data-id": w.id,
      onclick: () => apply(ctx.wallpaper(w.id) ?? w),
      onkeydown: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); apply(ctx.wallpaper(w.id) ?? w); } } },
      h("div.thumb-wrap",
        thumbFor(w),
        h(`span.badge.badge-${w.type}`, TYPE_LABEL[w.type] ?? w.type),
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
      let c = cards.get(w.id);
      if (!c || c._sig !== cardSig(w)) {
        const fresh = buildCard(w);
        if (c) c.replaceWith(fresh);
        c = fresh;
        cards.set(w.id, c);
      }
      keep.push(c);
    }
    // order + removal
    keep.forEach((c, i) => { if (grid.children[i] !== c) grid.insertBefore(c, grid.children[i] ?? null); });
    while (grid.children.length > keep.length) grid.lastChild.remove();
  }

  function update(s) {
    scope.update(s);
    const builtins = s.wallpapers.filter((w) => w.builtin || w.type === "builtin");
    const lib = s.wallpapers.filter((w) => !(w.builtin || w.type === "builtin"));
    const ids = new Set(s.wallpapers.map((w) => w.id));
    for (const id of [...cards.keys()]) if (!ids.has(id)) { cards.get(id).remove(); cards.delete(id); }
    renderGrid(builtinGrid, builtins);
    renderGrid(libGrid, lib);
    empty.hidden = lib.length > 0;
    libGrid.hidden = lib.length === 0;
    libMeta.textContent = lib.length ? `${lib.length} item${lib.length === 1 ? "" : "s"} · ${formatBytes(lib.reduce((a, w) => a + (w.sizeBytes || 0), 0))}` : "";

    const current = ctx.assignmentFor(ctx.scopeKey()).wallpaperId;
    for (const [id, c] of cards) {
      c.classList.toggle("applied", id === current);
      c.setAttribute("aria-pressed", id === current ? "true" : "false");
      if (id === applying) c.classList.add("applying");
    }

    ensureThumbnails(s.wallpapers, ctx.call, (id, ok) => {
      if (ok) applyFresh();
      else { const w = ctx.wallpaper(id); if (w) update(ctx.state); }
    });
  }

  // ------------------------------------------------------------------ card menu
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
    const builtin = w.builtin || w.type === "builtin";
    const item = (ic, label, fn, opts = {}) => h(`button.menu-item${opts.danger ? ".danger" : ""}`, {
      type: "button", disabled: !!opts.disabled, title: opts.title ?? null,
      onclick: () => { closeMenu(); fn(); } }, svg(ic), h("span", label));
    menu = h("div.menu", { role: "menu" },
      item("check", "Apply", () => apply(w)),
      item("pencil", "Rename…", () => rename(w), { disabled: builtin, title: builtin ? "Built-in scenes can't be renamed" : null }),
      h("div.menu-sep"),
      item("trash", "Delete…", () => remove(w), { danger: true, disabled: builtin, title: builtin ? "Built-in scenes can't be deleted" : null }));
    menu._for = w.id;
    document.body.append(menu);
    const r = anchor.getBoundingClientRect();
    menu._anchor = anchor;
    menu._anchorTop = r.top;
    const mw = menu.offsetWidth, mh = menu.offsetHeight;
    let left = Math.min(window.innerWidth - mw - 8, r.right - mw);
    let top = r.bottom + 6;
    if (top + mh > window.innerHeight - 8) top = r.top - mh - 6;
    menu.style.left = `${Math.max(8, left)}px`;
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

  return { el, update, hide: closeMenu };
}
