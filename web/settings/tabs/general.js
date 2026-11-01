// General tab — startup, folders, version, quit.
import { h, svg, toggle, segmented, dialog } from "../util.js";
import { LOGO } from "../icons.js";
import { pageHeader, card, row } from "../components/index.js";

export function createGeneralTab(ctx) {
  const startup = toggle(true, (v) => ctx.mutate("updateSettings", { startWithWindows: v }).catch(() => startup.set(!v)), "Start with Windows");
  const split = toggle(false, (v) => ctx.mutate("updateSettings", { theme: { split: v } }).catch(() => split.set(!v)), "Different wallpapers for light and dark mode");
  const mode = segmented([{ value: "system", label: "Follow Windows" }, { value: "light", label: "☀ Light" }, { value: "dark", label: "☾ Dark" }],
    "system", (v) => ctx.mutate("updateSettings", { theme: { mode: v } }).catch(() => {}), { ariaLabel: "Theme" });
  const windowsNow = h("p.field-hint");
  const modeBox = h("div.field", mode, windowsNow);
  const installPath = h("code.path");
  const libraryPath = h("code.path");
  const version = h("span");

  const copyBtn = (getText) => h("button.icon-btn", { type: "button", title: "Copy path",
    onclick: async () => {
      try { await navigator.clipboard.writeText(getText()); ctx.toast("Path copied", "success", 1600); }
      catch { ctx.toast("Couldn't copy to the clipboard", "error"); }
    } }, svg("copy"));

  const openLib = h("button.btn", { type: "button", onclick: async () => {
    try { await ctx.call("openLibraryFolder"); } catch (err) { ctx.toastError(err); }
  } }, svg("folder"), h("span", "Open library folder"));

  const uninstallBtn = h("button.btn.danger-outline", { type: "button", onclick: async () => {
    uninstallBtn.disabled = true;
    try {
      const r = await ctx.call("uninstall");   // the host asks what to do with your data, in its own dialog
      if (r?.started) document.body.append(h("div.quit-overlay", h("div", { html: LOGO }), h("div.quit-title", "Uninstalling Hanami…"), h("div.muted", "Hanami is closing and will be removed. You can close this window.")));
    } catch (err) { ctx.toastError(err); }
    finally { uninstallBtn.disabled = false; }
  } }, svg("trash"), h("span", "Uninstall Hanami…"));

  const quitBtn = h("button.btn.danger-outline", { type: "button", onclick: async () => {
    const ok = await dialog({ title: "Quit Hanami?", body: "Your wallpapers will stop and the tray icon will close. Start Hanami again from the Start menu.", confirmLabel: "Quit", danger: true });
    if (!ok) return;
    try {
      await ctx.call("quit");
      document.body.append(h("div.quit-overlay", h("div", { html: LOGO }), h("div.quit-title", "Hanami has quit"), h("div.muted", "You can close this window.")));
    } catch (err) { ctx.toastError(err); }
  } }, svg("power"), h("span", "Quit Hanami"));

  const pathRow = (icon, title, codeEl, extra) => h("div.row.path-row", h("div.row-icon", svg(icon)),
    h("div.row-text", h("div.row-title", title), codeEl),
    h("div.row-actions", copyBtn(() => codeEl.textContent), extra));

  const el = h("section",
    pageHeader("General", "Startup, storage and app info."),
    card(null, null,
      row("rocket", "Start with Windows", "Launch Hanami quietly in the tray when you sign in.", startup)),
    card("Light and dark", null,
      row("moon", "Different wallpapers for light and dark mode", "Hanami switches with Windows' light/dark setting. Pick each one on the Wallpaper and Screensaver tabs.", split),
      modeBox),
    card("Storage", null,
      pathRow("box", "Install folder", installPath),
      h("div.divider"),
      pathRow("folder", "Library folder", libraryPath, openLib)),
    card("Uninstall", null,
      row("trash", "Remove Hanami from this PC", "Removes the app, its Start menu shortcut and the start-with-Windows entry. You choose whether to keep or delete your wallpapers, pet packs and settings.", uninstallBtn)),
    h("div.card.pad.about",
      h("div.about-brand", h("span", { html: LOGO }), h("div", h("div.about-name", "Hanami"), h("div.muted", "Version ", version))),
      quitBtn));

  function update(s) {
    startup.set(s.settings.startWithWindows);
    split.set(s.settings.theme?.split);
    modeBox.hidden = !s.settings.theme?.split;
    mode.set(s.settings.theme?.mode ?? "system");
    windowsNow.textContent = s.theme
      ? `Windows is in ${s.theme.windows} mode right now${s.theme.active !== s.theme.windows ? ` · showing the ${s.theme.active} wallpapers` : ""}`
      : "";
    installPath.textContent = s.installDir;
    installPath.title = s.installDir;
    libraryPath.textContent = s.libraryDir;
    libraryPath.title = s.libraryDir;
    version.textContent = s.version;
  }
  return { el, update };
}
