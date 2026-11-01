// Screensaver tab — what the screensaver shows. When it runs lives on the Power tab.
import { h, svg } from "../util.js";
import { scopeSelector } from "../scope.js";
import { pageHeader, card, wallpaperPicker, effectPicker, createClockCard } from "../components/index.js";

export function createScreensaverTab(ctx) {
  const save = (patch) => ctx.mutate("updateSettings", { screensaver: patch }).catch(() => {});
  const backgroundId = () => ctx.screensaverBackground();

  // ---------------------------------------------------------------- header + status
  const previewBtn = h("button.btn", { type: "button", onclick: preview }, svg("eye"), h("span", "Preview"));
  const statusDot = h("span.ss-dot");
  const statusText = h("span.ss-status-text");
  const activeBadge = h("span.ss-badge", { hidden: true }, "Active");
  const status = h("div.ss-status-line", statusDot, statusText, activeBadge,
    h("span.ss-status-hint", "Preview shows it right now — press any key or click to exit."));

  async function preview() {
    previewBtn.disabled = true;
    try { await ctx.call("previewScreensaver"); }
    catch (err) { ctx.toastError(err); }
    finally { setTimeout(() => { previewBtn.disabled = false; }, 600); }
  }

  // ---------------------------------------------------------------- background + effect
  const backgrounds = wallpaperPicker(ctx, {
    allowSameAsDesktop: true,
    onSelect: (w) => { const id = w?.id ?? null; if ((ctx.state.settings.screensaver.wallpaperId ?? null) !== id) return save({ wallpaperId: id }); },
  });
  const effects = effectPicker({
    allowSameAsDesktop: true,
    ariaLabel: "Screensaver effect",
    onSelect: (preset) => { const id = backgroundId(); if (id) return ctx.setLook(id, { screensaver: { effectPreset: preset } }).catch(() => {}); },
    onTune: async (preset) => {
      const id = backgroundId();
      if (!id) return;
      // Tuning the effect that "same as desktop" shows edits the desktop's (shared) sliders; any other
      // effect has to become the screensaver's own first, so its sliders have somewhere to live.
      const look = ctx.lookFor(id);
      if (look.screensaver.effectPreset === null && look.desktop.effectPreset !== preset) {
        try { await ctx.setLook(id, { screensaver: { effectPreset: preset } }); } catch { return; }
      }
      ctx.openTuning(preset, { wallpaperId: id, target: "screensaver" });
    },
  });
  const effectNote = h("span.card-meta", "Same as desktop follows the wallpaper's effect");

  // With "Same as desktop" and several monitors: which monitor's wallpaper is being edited.
  const scope = scopeSelector(ctx, { verb: "Edits apply" });

  const clockCard = createClockCard(ctx);

  const toPower = h("button.link-row", { type: "button", onclick: () => ctx.showTab("power") },
    svg("clock"), h("span", "Timing and power settings"), h("span.link-row-go", "Power →"));

  const el = h("section",
    pageHeader("Screensaver", "Your live wallpaper, fullscreen, while you're away from your desk.", previewBtn),
    status,
    scope,
    card("Background", "Same as desktop follows each monitor", backgrounds.el),
    card("Effect", effectNote, effects),
    clockCard.el,
    toPower);

  function update(s) {
    const cfg = s.settings.screensaver;
    const st = s.screensaver;
    if (!cfg || !st) return;   // host without screensaver support

    statusText.textContent = st.status || (cfg.enabled ? "Ready" : "Off");
    activeBadge.hidden = !st.active;
    statusDot.dataset.tone = st.active ? "active" : !cfg.enabled || s.power.onBattery ? "off" : "ready";

    backgrounds.update(s, cfg.wallpaperId ?? null);
    scope.update(s);
    if (cfg.wallpaperId != null) scope.hidden = true;   // an own background has one look on every monitor
    const id = backgroundId();
    const look = ctx.lookFor(id);
    effects.set(look.screensaver.effectPreset);
    effects.setSameDetail(look.desktop.effectPreset);
    effectNote.textContent = ctx.wallpaper(id) ? `Saved with ${ctx.wallpaper(id).name}` : "Same as desktop follows the wallpaper's effect";
    clockCard.update(s);
  }

  return { el, update, hide: backgrounds.hide };
}
