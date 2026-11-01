// Screensaver tab — plugged-in idle screensaver (docs/CONTRACTS.md → "Screensaver behaviour").
import { h, svg, segmented, toggle } from "../util.js";
import { wallpaperThumb, isBuiltin } from "../wallthumb.js";

const IDLE_PRESETS = [1, 2, 5, 10, 15, 30];
const STOP_OPTIONS = [
  { value: 0, label: "Never" }, { value: 1, label: "1 h" }, { value: 2, label: "2 h" }, { value: 4, label: "4 h" }, { value: 8, label: "8 h" },
];
const TIMER_SETTINGS_PATH = "Windows Settings → System → Power & battery → Screen, sleep & hibernate timeouts";

const minutes = (n) => (n == null ? "never" : `${n} min`);

export function createScreensaverTab(ctx) {
  const save = (patch) => ctx.mutate("updateSettings", { screensaver: patch }).catch(() => {});

  // ---------------------------------------------------------------- hero: enable + status + preview
  const enabled = toggle(true, (v) => save({ enabled: v }), "Live screensaver");
  const statusDot = h("span.ss-dot");
  const statusText = h("span.ss-status-text");
  const activeBadge = h("span.ss-badge", { hidden: true }, "Active");
  const previewBtn = h("button.btn", { type: "button", onclick: preview }, svg("eye"), h("span", "Preview"));
  const hero = h("div.card.pad.ss-hero",
    h("div.ss-hero-top",
      h("div.callout-art", svg("screensaver")),
      h("div.callout-text",
        h("div.callout-title", "Live screensaver"),
        h("div.callout-sub", "When plugged in and idle, Hanami shows your live wallpaper fullscreen and keeps the screen on. Any mouse move or key ends it.")),
      enabled),
    h("div.ss-status",
      h("div.ss-status-main", statusDot, statusText, activeBadge),
      previewBtn),
    h("div.ss-preview-hint", "Preview shows it right now — move the mouse or press a key to exit."));

  async function preview() {
    previewBtn.disabled = true;
    try { await ctx.call("previewScreensaver"); }
    catch (err) { ctx.toastError(err); }
    finally { setTimeout(() => { previewBtn.disabled = false; }, 600); }
  }

  // ---------------------------------------------------------------- Windows timers
  const timers = h("div.ss-timers");

  // ---------------------------------------------------------------- when
  const idleSeg = segmented(IDLE_PRESETS.map((v) => ({ value: v, label: `${v}` })), 2, (v) => save({ idleMinutes: v }), { ariaLabel: "Start after (minutes)" });
  const idleInput = h("input.num-input", { type: "number", min: 1, max: 120, step: 1, "aria-label": "Custom idle minutes", title: "Custom: 1–120 minutes" });
  const commitIdle = () => {
    const cur = ctx.state.settings.screensaver.idleMinutes;
    let v = Math.round(Number(idleInput.value));
    if (!Number.isFinite(v) || idleInput.value === "") { idleInput.value = cur; return; }
    v = Math.min(120, Math.max(1, v));
    idleInput.value = v;
    if (v !== cur) save({ idleMinutes: v });
  };
  idleInput.addEventListener("change", commitIdle);
  idleInput.addEventListener("keydown", (e) => { if (e.key === "Enter") { commitIdle(); idleInput.blur(); } });

  const stopSeg = segmented(STOP_OPTIONS, 0, (v) => save({ stopAfterHours: v }), { ariaLabel: "Stop after" });
  const signIn = toggle(false, (v) => save({ requireSignIn: v }), "Require sign-in when it ends");

  const rule = (icon, title, sub, control) => h("div.rule",
    h("div.rule-icon", svg(icon)),
    h("div.rule-text", h("div.rule-title", title), h("div.rule-sub", sub)),
    control);

  const whenCard = h("div.card.pad.ss-section",
    h("div.card-head", h("h2", "Timing")),
    h("div.rules",
      rule("clock", "Start after", "Minutes without mouse or keyboard input, while plugged in.",
        h("div.ss-idle", idleSeg, h("label.num-wrap", idleInput, h("span", "min")))),
      rule("power", "Stop after", "After this, Hanami hides it and lets Windows turn the screen off and sleep as usual.", stopSeg),
      rule("lock", "Require sign-in when it ends", "Locks Windows when the screensaver ends. Leave off if your desk is private.", signIn)));

  // ---------------------------------------------------------------- wallpaper picker
  const picker = h("div.ss-picker", { role: "radiogroup", "aria-label": "Screensaver wallpaper" });
  const pickCard = h("div.card.pad.ss-section",
    h("div.card-head", h("h2", "Wallpaper"), h("span.card-meta", "Effects follow each monitor's desktop effect")),
    picker);
  let pickSig = "";

  function pickTile(id, name, thumb) {
    return h("button.pick", { type: "button", role: "radio", "data-id": id ?? "", title: name,
      onclick: () => { if ((ctx.state.settings.screensaver.wallpaperId ?? null) !== id) save({ wallpaperId: id }); } },
      h("div.thumb-wrap", thumb, h("span.applied-mark", svg("check"))),
      h("span.pick-name", name));
  }

  function renderPicker(s) {
    const sig = s.wallpapers.map((w) => `${w.id}|${w.name}|${w.thumbnail ?? ""}`).join(",");
    if (sig !== pickSig) {
      pickSig = sig;
      const same = h("div.thumb.pick-same", svg("monitors"), h("span", "Each monitor's own"));
      picker.replaceChildren(
        pickTile(null, "Same as desktop", same),
        ...s.wallpapers.filter(isBuiltin).map((w) => pickTile(w.id, w.name, wallpaperThumb(w))),
        ...s.wallpapers.filter((w) => !isBuiltin(w)).map((w) => pickTile(w.id, w.name, wallpaperThumb(w))));
    }
    const sel = s.settings.screensaver.wallpaperId ?? "";
    for (const b of picker.children) {
      const on = b.dataset.id === sel;
      b.classList.toggle("on", on);
      b.setAttribute("aria-checked", on ? "true" : "false");
    }
  }

  // ---------------------------------------------------------------- layout
  const el = h("section",
    h("header.page-head",
      h("div", h("h1", "Screensaver"), h("p.page-sub", "Your live wallpaper, fullscreen, while you're away from your desk."))),
    hero,
    timers,
    whenCard,
    pickCard,
    h("p.footnote", svg("info"),
      h("span", "On battery Hanami never shows the screensaver, so Windows' own timers apply. Closing the lid always follows your Windows lid setting.")));

  function renderTimers(s) {
    const { windowsDisplayOffMinutes: off, windowsSleepMinutes: sleep } = s.screensaver;
    const idle = s.settings.screensaver.idleMinutes;
    const warnings = [];
    if (off != null && off <= idle) warnings.push(`Windows turns the screen off after ${off} min when plugged in — before the screensaver would start.`);
    if (sleep != null && sleep <= idle) warnings.push(`Windows puts your PC to sleep after ${sleep} min when plugged in — before the screensaver would start.`);
    if (warnings.length) {
      timers.className = "ss-timers card pad ss-warn";
      timers.replaceChildren(
        h("div.ss-warn-icon", svg("alert")),
        h("div.ss-warn-text",
          ...warnings.map((w) => h("div.ss-warn-title", w)),
          h("div.ss-warn-sub", `Raise it in ${TIMER_SETTINGS_PATH}.`)));
    } else {
      timers.className = "ss-timers ss-info";
      timers.replaceChildren(svg("info"),
        h("span", `Windows (plugged in): screen off after ${minutes(off)} · sleep after ${minutes(sleep)} — Hanami keeps the screen on only while the screensaver is showing.`));
    }
  }

  function update(s) {
    const cfg = s.settings.screensaver;
    const st = s.screensaver;
    if (!cfg || !st) return;   // host without screensaver support

    enabled.set(cfg.enabled);
    hero.classList.toggle("on", cfg.enabled);
    el.classList.toggle("ss-disabled", !cfg.enabled);

    statusText.textContent = st.status || (cfg.enabled ? "Ready" : "Off");
    activeBadge.hidden = !st.active;
    const tone = st.active ? "active" : !cfg.enabled || s.power.onBattery ? "off" : "ready";
    statusDot.dataset.tone = tone;

    idleSeg.set(cfg.idleMinutes);
    if (document.activeElement !== idleInput) idleInput.value = cfg.idleMinutes;
    idleInput.classList.toggle("custom", !IDLE_PRESETS.includes(cfg.idleMinutes));
    stopSeg.set(cfg.stopAfterHours);
    signIn.set(cfg.requireSignIn);

    renderTimers(s);
    renderPicker(s);
  }

  return { el, update };
}
