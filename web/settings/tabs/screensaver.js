// Screensaver tab — what the screensaver shows. When it runs lives on the Power tab.
import { h, svg, toggle, dialog } from "../util.js";
import { scopeSelector } from "../scope.js";
import { pageHeader, card, row, wallpaperPicker, effectPicker, createClockCard, slotSwitch } from "../components/index.js";
import { createLockFrame } from "../components/lockFrame.js";

export function createScreensaverTab(ctx) {
  const save = (patch) => ctx.mutate("updateSettings", { screensaver: patch }).catch(() => {});
  const backgroundId = () => ctx.screensaverBackground();

  // ---------------------------------------------------------------- header + status
  const previewBtn = h("button.btn", { type: "button", onclick: preview }, svg("eye"), h("span", "Preview"));
  const statusDot = h("span.ss-dot");
  const statusText = h("span.ss-status-text");
  const activeBadge = h("span.ss-badge", { hidden: true }, "Active");
  const status = h("div.ss-status-line", statusDot, statusText, activeBadge,
    h("span.ss-status-hint", "Preview shows it right now — press any key or click and it slides up and away (with sign-in required it locks instead, and Preview never locks)."));

  async function preview() {
    previewBtn.disabled = true;
    try { await ctx.call("previewScreensaver"); }
    catch (err) { ctx.toastError(err); }
    finally { setTimeout(() => { previewBtn.disabled = false; }, 600); }
  }

  // ---------------------------------------------------------------- background + effect
  const backgrounds = wallpaperPicker(ctx, {
    allowSameAsDesktop: true,
    onSelect: pickBackground,
  });
  const slots = slotSwitch(ctx);
  const emptyDark = h("p.field-hint.slot-hint");

  /** Light slot: the screensaver's background (null = the desktop's). Dark slot: its dark one (null = the light one);
   *  picking one while the light background is "same as desktop" pins that desktop wallpaper as the light one. */
  function pickBackground(w) {
    const cfg = ctx.state.settings.screensaver;
    const id = w?.id ?? null;
    if (ctx.slot === "dark") {
      if ((cfg.darkWallpaperId ?? null) === id) return;
      if (id === null) return save({ darkWallpaperId: null });
      return save({ wallpaperId: cfg.wallpaperId ?? ctx.assignmentFor(ctx.scopeKey()).light, darkWallpaperId: id });
    }
    if ((cfg.wallpaperId ?? null) === id) return;
    return save(id === null ? { wallpaperId: null, darkWallpaperId: null } : { wallpaperId: id });
  }
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

  // ---------------------------------------------------------------- lock screen
  // A still of the primary monitor's screensaver wallpaper as the Windows lock screen picture. Off by default.
  const signIn = toggle(false, (v) => save({ requireSignIn: v }), "Require sign-in when it ends");
  const syncToggle = toggle(false, onSyncToggle, "Match the lock screen to the screensaver");
  const lockStatus = h("p.field-hint.lock-status");
  const lockFrame = createLockFrame(ctx);   // which frame of a video the lock screen shows
  const restoreBtn = h("button.btn.small", { type: "button", hidden: true, onclick: restoreLock }, svg("reset"), h("span", "Restore my previous picture"));
  const settingsBtn = h("button.btn.subtle.small", { type: "button", onclick: () => ctx.call("openLockScreenSettings").catch((e) => ctx.toastError(e)) },
    h("span", "Lock screen settings…"));
  const lockCard = card("Lock screen", null,
    row("lock", "Require sign-in when it ends", "Locks Windows the moment a key or click ends the screensaver, going straight to the lock screen instead of sliding up. Leave off if your desk is private.", signIn),
    h("div.divider"),
    row("lock", "Match the lock screen to the screensaver",
      "Uses a still of the screensaver's wallpaper as your Windows lock screen. Your own picture comes back when you turn this off or quit Hanami.",
      syncToggle),
    lockStatus,
    lockFrame.el,
    h("div.slot-switch.lock-actions",
      h("span.field-hint", "Needs your lock screen set to Picture, not Windows Spotlight"),
      settingsBtn, restoreBtn));

  async function onSyncToggle(on) {
    if (on) {
      const ok = await dialog({
        title: "Replace your lock screen picture?",
        body: "Hanami will set your Windows lock screen picture to the screensaver's wallpaper and keep a copy of your current one. It puts your picture back when you turn this off or quit Hanami (or use Restore here if Hanami ever crashes). Windows Spotlight overrides this, so set your lock screen to Picture in Windows first.",
        confirmLabel: "Turn on",
      });
      if (!ok) { syncToggle.set(false); return; }
    }
    save({ syncLockScreen: on });
  }
  async function restoreLock() {
    try { await ctx.mutate("restoreLockScreen"); ctx.toast("Your previous lock screen picture is back", "success"); }
    catch (err) { ctx.toastError(err); }
  }

  const el = h("section",
    pageHeader("Screensaver", "Your live wallpaper, fullscreen, while you're away from your desk.", previewBtn),
    status,
    scope,
    card("Background", slots, emptyDark, backgrounds.el),
    card("Effect", effectNote, effects),
    clockCard.el,
    lockCard);

  function update(s) {
    const cfg = s.settings.screensaver;
    const st = s.screensaver;
    if (!cfg || !st) return;   // host without screensaver support

    statusText.textContent = st.status || (cfg.enabled ? "Ready" : "Off");
    activeBadge.hidden = !st.active;
    statusDot.dataset.tone = st.active ? "active" : !cfg.enabled || s.power.onBattery ? "off" : "ready";

    slots.update();
    const dark = ctx.slot === "dark";
    backgrounds.setSameLabel(dark && cfg.wallpaperId != null ? "Same as light" : "Same as desktop");
    emptyDark.hidden = !dark;
    emptyDark.textContent = cfg.wallpaperId == null
      ? "Follows the desktop's dark wallpaper until you pick one"
      : "Uses the light background until you pick one";
    backgrounds.update(s, dark ? cfg.darkWallpaperId ?? null : cfg.wallpaperId ?? null,
      ctx.split ? { light: cfg.wallpaperId ?? null, dark: cfg.darkWallpaperId ?? null } : null);
    scope.update(s);
    if (cfg.wallpaperId != null) scope.hidden = true;   // an own background has one look on every monitor
    const id = backgroundId();
    const look = ctx.lookFor(id);
    effects.set(look.screensaver.effectPreset);
    effects.setSameDetail(look.desktop.effectPreset);
    effectNote.textContent = ctx.wallpaper(id) ? `Saved with ${ctx.wallpaper(id).name}` : "Same as desktop follows the wallpaper's effect";
    clockCard.update(s);

    const lock = st.lockScreen ?? { status: "off", hasBackup: false };
    signIn.set(!!cfg.requireSignIn);
    syncToggle.set(!!cfg.syncLockScreen);
    lockStatus.hidden = !cfg.syncLockScreen && !lock.error;
    const lockLine = lock.status === "ok" ? `Updated ${new Date(lock.updatedAt).toLocaleString()}`
      : lock.status === "waiting" ? "Preparing the picture for this wallpaper…"
      : cfg.syncLockScreen ? "Waiting to update" : "";
    // Windows has one lock picture for all monitors, so the main monitor's wallpaper decides it.
    lockStatus.textContent = lock.status === "failed" ? `Couldn't update it: ${lock.error ?? "unknown error"}`
      : cfg.syncLockScreen ? `${lockLine} · Uses your main monitor's wallpaper` : lockLine;
    restoreBtn.hidden = !lock.hasBackup || !!cfg.syncLockScreen;
    lockFrame.update(s);
  }

  return {
    el,
    update,
    show: () => lockFrame.setActive(true),
    hide: () => { backgrounds.hide(); lockFrame.setActive(false); },   // hiding unloads the preview video
  };
}
