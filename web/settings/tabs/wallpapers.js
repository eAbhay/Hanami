// Wallpaper tab — pick the background and the effect for each monitor (or all of them).
import { h, svg, progressBar } from "../util.js";
import { scopeSelector } from "../scope.js";
import { pageHeader, card, wallpaperPicker, effectPicker } from "../components/index.js";

export function createWallpapersTab(ctx) {
  const scope = scopeSelector(ctx);
  const progress = progressBar();
  const importBtn = h("button.btn.primary", { type: "button", onclick: doImport }, svg("upload"), h("span", "Import video / image"));

  const backgrounds = wallpaperPicker(ctx, { manage: true, onSelect: apply, onImport: doImport });
  const effects = effectPicker({
    onSelect: (preset) => ctx.assign({ effect: { preset } }).catch(() => {}),
    onTune: (preset) => ctx.openTuning(preset, { wallpaperId: ctx.assignmentFor(ctx.scopeKey()).wallpaperId }),
  });

  const el = h("section",
    pageHeader("Wallpaper", "Pick a background and an effect. Click a card to apply it.", importBtn),
    scope,
    progress,
    card("Background", null, backgrounds.el),
    card("Effect", "Use the gear to tune an effect", effects));

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
    await ctx.assign({ wallpaperId: w.id });
    ctx.toast(`${w.name} applied ${key === "*" ? "to all monitors" : `to ${ctx.monitorName(key)}`}`, "success", 2200);
  }

  function update(s) {
    scope.update(s);
    const a = ctx.assignmentFor(ctx.scopeKey());
    backgrounds.update(s, a.wallpaperId);
    effects.set(a.effectPreset);
  }

  return { el, update, hide: backgrounds.hide };
}
