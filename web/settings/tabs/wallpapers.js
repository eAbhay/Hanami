// Wallpaper tab — pick the background and the effect for each monitor (or all of them).
import { h, svg, progressBar } from "../util.js";
import { scopeSelector } from "../scope.js";
import { pageHeader, card, wallpaperPicker, effectPicker, slotSwitch } from "../components/index.js";

export function createWallpapersTab(ctx) {
  const scope = scopeSelector(ctx);
  const progress = progressBar();
  const importBtn = h("button.btn.primary", { type: "button", onclick: doImport }, svg("upload"), h("span", "Import video / image"));

  const backgrounds = wallpaperPicker(ctx, { manage: true, onSelect: apply, onImport: doImport });
  const currentId = () => ctx.assignmentFor(ctx.scopeKey()).wallpaperId;
  const slots = slotSwitch(ctx);
  const emptyDark = h("p.field-hint.slot-hint", "Uses the light wallpaper until you pick one");
  const effects = effectPicker({
    onSelect: (preset) => { const id = currentId(); if (id) return ctx.setLook(id, { desktop: { effectPreset: preset } }).catch(() => {}); },
    onTune: (preset) => ctx.openTuning(preset, { wallpaperId: currentId(), target: "desktop" }),
  });
  const effectNote = h("span.card-meta", "Use the gear to tune an effect");

  const el = h("section",
    pageHeader("Wallpaper", "Pick a background and an effect. Click a card to apply it.", importBtn),
    scope,
    progress,
    card("Background", slots, emptyDark, backgrounds.el),
    card("Effect", effectNote, effects));

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
    if (ctx.assignmentFor(key).own === w.id) return;
    await ctx.assign(w.id);
    ctx.toast(`${w.name} applied ${key === "*" ? "to all monitors" : `to ${ctx.monitorName(key)}`}`, "success", 2200);
  }

  function update(s) {
    scope.update(s);
    const a = ctx.assignmentFor(ctx.scopeKey());
    slots.update();
    emptyDark.hidden = !(ctx.slot === "dark" && a.own == null);
    backgrounds.update(s, a.own, ctx.split ? { light: a.light, dark: a.dark } : null);
    effects.set(ctx.lookFor(a.wallpaperId).desktop.effectPreset);
    const w = ctx.wallpaper(a.wallpaperId);
    effectNote.textContent = w ? `Saved with ${w.name} · gear tunes it` : "Use the gear to tune an effect";
  }

  return { el, update, hide: backgrounds.hide };
}
