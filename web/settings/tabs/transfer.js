// Export / Import tab — pick library items, export a pack; import a pack from another PC.
import { h, svg, formatBytes, progressBar } from "../util.js";
import { pageHeader } from "../components/index.js";

export function createTransferTab(ctx) {
  let items = [];            // wallpaper ExportItems
  let settingsItem = null;   // ExportItem with kind "settings", if the host lists one
  const selected = new Set();
  const known = new Set();   // ids we've seen — new ones start checked
  let includeSettings = true;
  let busy = false;
  let libSig = null;

  const list = h("div.export-list", { role: "group", "aria-label": "Items to export" });
  const totalEl = h("span.export-total");
  const settingsCb = h("input", { type: "checkbox", checked: true, onchange: () => { includeSettings = settingsCb.checked; render(); } });
  const settingsSize = h("span.export-size");
  const exportProgress = progressBar();
  const importProgress = progressBar();

  const exportBtn = h("button.btn.primary", { type: "button", onclick: doExport }, svg("download"), h("span", "Export pack…"));
  const importBtn = h("button.btn", { type: "button", onclick: doImport }, svg("upload"), h("span", "Import pack…"));
  const selAll = h("button.link-btn", { type: "button", onclick: () => { items.forEach((i) => selected.add(i.id)); render(); } }, "Select all");
  const selNone = h("button.link-btn", { type: "button", onclick: () => { selected.clear(); render(); } }, "Select none");
  const emptyNote = h("div.export-empty", { hidden: true }, "Your library has no imported wallpapers yet. Built-in scenes are part of Hanami and don't need exporting.");

  ctx.on("progress", (p) => {
    if (p?.task === "export") { exportProgress.update(p); }
    if (p?.task === "importPack") { importProgress.update(p); }
  });

  const el = h("section",
    pageHeader("Export / Import", "Move your library and settings to another PC as a single pack file.", importBtn),
    importProgress,
    h("div.card.pad",
      h("div.card-head", h("h2", "Export"), h("div.select-links", selAll, h("span.sep", "·"), selNone)),
      list,
      emptyNote,
      h("label.export-row.settings-row",
        settingsCb,
        h("div.export-thumb.settings-thumb", svg("gear")),
        h("div.export-name", h("div", "Include settings"), h("div.export-sub", "Monitor assignments, effects, power rules, frame rates and screensaver")),
        settingsSize),
      h("div.export-foot", totalEl, exportBtn),
      exportProgress));

  async function loadItems() {
    try {
      const all = (await ctx.call("getExportItems")) ?? [];
      settingsItem = all.find((i) => i.kind === "settings") ?? null;
      items = all.filter((i) => i.kind !== "settings");
      for (const i of items) if (!known.has(i.id)) { known.add(i.id); selected.add(i.id); }
      for (const id of [...selected]) if (!items.some((i) => i.id === id)) selected.delete(id);
      render(true);
    } catch (err) { ctx.toastError(err); }
  }

  function render(rebuild = false) {
    if (rebuild || list.children.length !== items.length) {
      list.replaceChildren(...items.map((i) => {
        const cb = h("input", { type: "checkbox", "data-id": i.id, onchange: () => { cb.checked ? selected.add(i.id) : selected.delete(i.id); render(); } });
        const thumb = i.thumbnail ? h("img", { src: i.thumbnail, alt: "", loading: "lazy" }) : svg("image");
        return h("label.export-row", cb, h("div.export-thumb", thumb), h("div.export-name", h("div", { title: i.name }, i.name)), h("span.export-size", formatBytes(i.sizeBytes)));
      }));
    }
    for (const cb of list.querySelectorAll("input[type=checkbox]")) {
      cb.checked = selected.has(cb.dataset.id);
      cb.closest(".export-row").classList.toggle("checked", cb.checked);
    }
    emptyNote.hidden = items.length > 0;
    settingsCb.checked = includeSettings;
    settingsCb.closest(".export-row").classList.toggle("checked", includeSettings);
    settingsSize.textContent = settingsItem ? formatBytes(settingsItem.sizeBytes) : "";
    const bytes = items.filter((i) => selected.has(i.id)).reduce((a, i) => a + (i.sizeBytes || 0), 0) + (includeSettings ? settingsItem?.sizeBytes ?? 0 : 0);
    const n = selected.size;
    totalEl.innerHTML = "";
    totalEl.append(h("b", `${n} of ${items.length}`), ` wallpaper${items.length === 1 ? "" : "s"}${includeSettings ? " + settings" : ""} · `, h("b", formatBytes(bytes)));
    exportBtn.disabled = busy || (n === 0 && !includeSettings);
    selAll.disabled = n === items.length;
    selNone.disabled = n === 0;
  }

  async function doExport() {
    busy = true; render(); importBtn.disabled = true;
    try {
      const ids = items.filter((i) => selected.has(i.id)).map((i) => i.id);
      const r = await ctx.call("exportPack", { ids, includeSettings });
      if (r?.cancelled) return;
      ctx.toast(`Pack saved to ${r?.path ?? "the chosen location"}`, "success", 5000);
    } catch (err) { ctx.toastError(err); }
    finally {
      busy = false; importBtn.disabled = false; render();
      setTimeout(() => exportProgress.reset(), 900);
    }
  }

  async function doImport() {
    importBtn.disabled = true; busy = true; render();
    try {
      const r = await ctx.call("importPack");
      if (r?.cancelled) return;
      await ctx.mutate("getState");
      ctx.toast(r?.imported ? `Imported ${r.imported} item${r.imported === 1 ? "" : "s"} from the pack` : "Pack imported", "success");
    } catch (err) { ctx.toastError(err); }
    finally {
      importBtn.disabled = false; busy = false; render();
      setTimeout(() => importProgress.reset(), 900);
    }
  }

  function update(s) {
    const sig = s.wallpapers.filter((w) => !w.builtin).map((w) => `${w.id}:${w.name}:${w.thumbnail ?? ""}:${w.sizeBytes}`).join("|");
    if (sig !== libSig) { libSig = sig; loadItems(); }
  }

  return { el, update };
}
