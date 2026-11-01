// Screensaver tab — what the screensaver shows. When it runs lives on the Power tab.
import { h, svg } from "../util.js";
import { pageHeader, card, wallpaperPicker, effectPicker, createClockCard } from "../components/index.js";

export function createScreensaverTab(ctx) {
  const save = (patch) => ctx.mutate("updateSettings", { screensaver: patch }).catch(() => {});

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
    onSelect: (preset) => save({ effectPreset: preset }),
    onTune: (preset) => {
      const cfg = ctx.state.settings.screensaver;
      ctx.openTuning(preset, { wallpaperId: cfg.wallpaperId ?? ctx.assignmentFor(ctx.scopeKey()).wallpaperId });
    },
  });

  const clockCard = createClockCard(ctx);

  const toPower = h("button.link-row", { type: "button", onclick: () => ctx.showTab("power") },
    svg("clock"), h("span", "Timing and power settings"), h("span.link-row-go", "Power →"));

  const el = h("section",
    pageHeader("Screensaver", "Your live wallpaper, fullscreen, while you're away from your desk.", previewBtn),
    status,
    card("Background", "Same as desktop follows each monitor", backgrounds.el),
    card("Effect", "Same as desktop follows each monitor's effect", effects),
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
    effects.set(cfg.effectPreset ?? null);
    clockCard.update(s);
  }

  return { el, update, hide: backgrounds.hide };
}
