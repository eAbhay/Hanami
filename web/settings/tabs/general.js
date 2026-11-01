// General tab — startup, folders, version, quit.
import { h, svg, toggle, dialog } from "../util.js";
import { LOGO } from "../icons.js";
import { pageHeader, card, row } from "../components/index.js";

export function createGeneralTab(ctx) {
  const startup = toggle(true, (v) => ctx.mutate("updateSettings", { startWithWindows: v }).catch(() => startup.set(!v)), "Start with Windows");
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
    card("Storage", null,
      pathRow("box", "Install folder", installPath),
      h("div.divider"),
      pathRow("folder", "Library folder", libraryPath, openLib)),
    h("div.card.pad.about",
      h("div.about-brand", h("span", { html: LOGO }), h("div", h("div.about-name", "Hanami"), h("div.muted", "Version ", version))),
      quitBtn));

  function update(s) {
    startup.set(s.settings.startWithWindows);
    installPath.textContent = s.installDir;
    installPath.title = s.installDir;
    libraryPath.textContent = s.libraryDir;
    libraryPath.title = s.libraryDir;
    version.textContent = s.version;
  }
  return { el, update };
}
