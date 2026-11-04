// General tab — startup, folders, version, quit.
import { h, svg, toggle, segmented, dialog, progressBar } from "../util.js";
import { LOGO } from "../icons.js";
import { pageHeader, card, row } from "../components/index.js";

export function createGeneralTab(ctx) {
  const startup = toggle(true, (v) => ctx.mutate("updateSettings", { startWithWindows: v }).catch(() => startup.set(!v)), "Start with Windows");
  const split = toggle(false, (v) => ctx.mutate("updateSettings", { theme: { split: v } }).catch(() => split.set(!v)), "Different wallpapers for light and dark mode");
  const mode = segmented([{ value: "system", label: "Follow Windows" }, { value: "light", label: "☀ Light" }, { value: "dark", label: "☾ Dark" }],
    "system", (v) => ctx.mutate("updateSettings", { theme: { mode: v } }).catch(() => {}), { ariaLabel: "Theme" });
  const autoCheck = toggle(true, (v) => ctx.mutate("updateSettings", { updates: { autoCheck: v } }).catch(() => autoCheck.set(!v)), "Tell me when updates are available");
  const channel = segmented([{ value: "stable", label: "Stable" }, { value: "beta", label: "Beta" }],
    "stable", (v) => ctx.mutate("updateSettings", { updates: { channel: v } }).catch(() => {}), { ariaLabel: "Update channel" });
  const checkBtn = h("button.btn", { type: "button", onclick: async () => {
    checkBtn.disabled = true;
    try { await ctx.mutate("checkForUpdates"); } catch (err) { ctx.toastError(err); }
    finally { checkBtn.disabled = false; }
  } }, svg("reset"), h("span", "Check now"));
  const checkRow = row("reset", "Check for updates", null, checkBtn, { rule: true });
  const checkStatus = h("div.row-sub");
  checkRow.text.append(checkStatus);
  // The banner: notes come from the network, so they are only ever set as text.
  const bannerTitle = h("div.update-title");
  const bannerMeta = h("div.update-meta");
  const bannerNotes = h("pre.update-notes");
  const bannerProgress = progressBar();
  const installBtn = h("button.btn.primary", { type: "button", onclick: async () => {
    try { await ctx.mutate("installUpdate"); } catch (err) { ctx.toastError(err); }
  } }, svg("download"), h("span", "Update"));
  const openPage = h("button.btn", { type: "button", onclick: () => ctx.call("openUpdatePage").catch((err) => ctx.toastError(err)) }, h("span", "Open download page"));
  const skipBtn = h("button.btn", { type: "button", onclick: () => ctx.mutate("skipUpdate").catch((err) => ctx.toastError(err)) }, h("span", "Skip this version"));
  const actions = h("div.update-actions", installBtn, openPage, skipBtn);
  const banner = h("div.card.pad.update-banner", { hidden: true }, bannerTitle, bannerMeta, bannerProgress, bannerNotes, actions);
  const windowsNow = h("p.field-hint");
  const modeBox = h("div.field", mode, windowsNow);
  const installPath = h("code.path");
  const libraryPath = h("code.path");
  const version = h("span");

  // Open-source notices: web/THIRD-PARTY-NOTICES.md (built into the exe), fetched the first time the section opens.
  const noticesText = h("pre.notices-text", "Loading…");
  const notices = h("details.card.pad.notices", h("summary", "Open-source notices"), noticesText);
  let noticesLoaded = false;
  notices.addEventListener("toggle", async () => {
    if (!notices.open || noticesLoaded) return;
    noticesLoaded = true;
    try {
      const res = await fetch(new URL("../../THIRD-PARTY-NOTICES.md", import.meta.url));
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      noticesText.textContent = await res.text();
    } catch {
      noticesLoaded = false;
      noticesText.textContent = "Couldn't load the notices. They are also in THIRD-PARTY-NOTICES.md in the Hanami repository.";
    }
  });

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
    pageHeader("General", "Startup, updates, storage and app info."),
    banner,
    card(null, null,
      row("rocket", "Start with Windows", "Launch Hanami quietly in the tray when you sign in.", startup)),
    card("Updates", null, h("div.rules",
      row("download", "Tell me when updates are available", "Hanami asks GitHub for new releases about once a day. It sends nothing about you.", autoCheck, { rule: true }),
      row("sparkles", "Channel", "Stable is for normal releases. Beta also includes early releases that may be rougher.", channel, { rule: true }),
      checkRow)),
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
      quitBtn),
    notices);

  function updateUi(s) {
    const u = s.update ?? {}, cfg = s.settings.updates ?? {};
    autoCheck.set(cfg.autoCheck !== false);
    channel.set(cfg.channel === "beta" ? "beta" : "stable");
    checkBtn.disabled = u.status === "checking" || u.status === "downloading";
    const when = u.lastCheck ? `Last checked ${new Date(u.lastCheck).toLocaleString()}.` : "Not checked yet.";
    const msg = u.status === "checking" ? "Checking…"
      : u.status === "failed" ? u.error
      : u.status === "available" ? (u.error ? `${u.error} Version ${u.version} is still available.` : `Version ${u.version} is available. ${when}`)
      : u.status === "downloading" ? `Updating to ${u.version}…`
      : u.status === "upToDate" ? `You're up to date. ${when}`
      : when;
    checkStatus.textContent = msg;
    // The banner is for an offered version: available, being installed, or an install that failed (a failed check has no version).
    const downloading = u.status === "downloading", failedInstall = u.status === "failed" && !!u.version;
    banner.hidden = !(u.status === "available" || downloading || failedInstall);
    if (!banner.hidden) {
      bannerTitle.textContent = downloading ? `Updating to Hanami ${u.version}` : failedInstall ? `Couldn't update to Hanami ${u.version}` : `Hanami ${u.version} is available`;
      const mb = u.sizeBytes ? ` · ${(u.sizeBytes / 1e6).toFixed(1)} MB` : "";
      bannerMeta.textContent = failedInstall ? u.error : `You have ${s.version}${mb}`;
      bannerNotes.textContent = u.notes || "";
      bannerNotes.hidden = !u.notes || downloading;
      if (downloading) {
        const p = u.progress ?? 0;
        bannerProgress.update({ done: p, total: 1, label: p >= 1 ? "Installing and restarting Hanami…" : "Downloading…" });
      } else bannerProgress.reset();
      installBtn.hidden = downloading;
      installBtn.lastChild.textContent = failedInstall ? "Try again" : "Update";
      openPage.hidden = !failedInstall;
      skipBtn.hidden = downloading;
    }
  }

  function update(s) {
    startup.set(s.settings.startWithWindows);
    updateUi(s);
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
