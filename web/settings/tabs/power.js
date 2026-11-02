// Power tab — when things run: the desktop wallpaper per condition, the screensaver's timing, frame rates.
import { h, svg, segmented, slider, toggle } from "../util.js";
import { pageHeader, card, row } from "../components/index.js";

const MODES = [
  { value: "full", label: "Full", title: "Video plays, effects run at your FPS cap" },
  { value: "reduced", label: "Reduced", title: "Video holds its frame, effects keep moving at up to 30 FPS" },
  { value: "paused", label: "Paused", title: "Everything frozen — no animation work at all" },
];
const MODE_TEXT = {
  full: "Video plays and effects run at your FPS cap.",
  reduced: "Video holds its frame; effects keep moving at up to 30 FPS.",
  paused: "Everything freezes — no animation work at all.",
};
const MODE_LABEL = { full: "Full", reduced: "Reduced", paused: "Paused" };

const CONDITIONS = [
  { key: "onAC", icon: "plug", title: "Plugged in", sub: "Your PC is on mains power." },
  { key: "onBattery", icon: "battery", title: "On battery", sub: "Unplugged, with charge above the low-battery level." },
  { key: "lowBattery", icon: "batteryLow", title: "Low battery", sub: "Unplugged and at or below the threshold below.", threshold: true },
  { key: "energySaver", icon: "leaf", title: "Energy Saver on", sub: "Windows Energy Saver (Battery Saver) is turned on." },
  { key: "whenMaximized", icon: "maximize", title: "Maximized window", sub: "A maximized window covers that monitor." },
  { key: "whenFullscreen", icon: "fullscreen", title: "Fullscreen app", sub: "A game or video is fullscreen on that monitor." },
];
const FPS = [15, 24, 30, 60, 120];

const IDLE_PRESETS = [1, 2, 5, 10, 15, 30];
const STOP_OPTIONS = [
  { value: 0, label: "Never" }, { value: 1, label: "1 h" }, { value: 2, label: "2 h" }, { value: 4, label: "4 h" }, { value: 8, label: "8 h" },
];
const TIMER_SETTINGS_PATH = "Windows Settings → System → Power & battery → Screen, sleep & hibernate timeouts";
const minutes = (n) => (n == null ? "never" : `${n} min`);

export function createPowerTab(ctx) {
  const setPower = (patch) => ctx.mutate("updateSettings", { power: patch }).catch(() => {});
  const saveSaver = (patch) => ctx.mutate("updateSettings", { screensaver: patch }).catch(() => {});

  // ---------------------------------------------------------------- live status
  const statusIcon = h("div.status-icon");
  const statusTitle = h("div.status-title");
  const statusSub = h("div.status-detail");
  const monitorList = h("div.monitor-modes");
  const statusCard = h("div.card.pad.power-status",
    h("div.power-now", statusIcon, h("div", statusTitle, statusSub)),
    monitorList);

  // ---------------------------------------------------------------- keep running on battery
  const keepSwitch = toggle(false, (on) => setPower({ onBattery: on ? "full" : "paused" }), "Keep running on battery");
  const keepCard = h("div.card.pad.callout",
    h("div.callout-art", svg("battery")),
    h("div.callout-text",
      h("div.callout-title", "Keep running on battery"),
      h("div.callout-sub", "By default Hanami pauses when you unplug to save battery. Turn this on to keep wallpapers playing at full quality on battery.")),
    keepSwitch);

  // ---------------------------------------------------------------- desktop wallpaper rules
  const rows = {};
  const rulesList = h("div.rules");
  for (const c of CONDITIONS) {
    const explain = h("div.rule-explain");
    const active = h("span.rule-active", { hidden: true }, "Now");
    const seg = segmented(MODES, "full", (v) => setPower({ [c.key]: v }), { ariaLabel: c.title, cls: "modes" });
    const extra = c.threshold
      ? slider({ label: "Low-battery threshold", min: 5, max: 50, step: 1, value: 20, format: (v) => `${v}%`,
          onInput: debounced((v) => setPower({ lowBatteryThreshold: v }), 200) })
      : null;
    const r = row(c.icon, [c.title, active], c.sub, seg, { rule: true });
    r.dataset.key = c.key;
    r.text.append(explain);
    if (extra) r.append(h("div.rule-extra", extra));
    rows[c.key] = { row: r, seg, explain, active, extra };
    rulesList.append(r);
  }

  // ---------------------------------------------------------------- screensaver
  const saverEnabled = toggle(true, (v) => saveSaver({ enabled: v }), "Live screensaver");
  const saverStatus = h("span.card-meta");
  const idleSeg = segmented(IDLE_PRESETS.map((v) => ({ value: v, label: `${v}` })), 2, (v) => saveSaver({ idleMinutes: v }), { ariaLabel: "Start after (minutes)" });
  const idleInput = h("input.num-input", { type: "number", min: 1, max: 120, step: 1, "aria-label": "Custom idle minutes", title: "Custom: 1–120 minutes" });
  const commitIdle = () => {
    const cur = ctx.state.settings.screensaver.idleMinutes;
    let v = Math.round(Number(idleInput.value));
    if (!Number.isFinite(v) || idleInput.value === "") { idleInput.value = cur; return; }
    v = Math.min(120, Math.max(1, v));
    idleInput.value = v;
    if (v !== cur) saveSaver({ idleMinutes: v });
  };
  idleInput.addEventListener("change", commitIdle);
  idleInput.addEventListener("keydown", (e) => { if (e.key === "Enter") { commitIdle(); idleInput.blur(); } });
  const stopSeg = segmented(STOP_OPTIONS, 0, (v) => saveSaver({ stopAfterHours: v }), { ariaLabel: "Stop after" });
  const signIn = toggle(false, (v) => saveSaver({ requireSignIn: v }), "Require sign-in when it ends");
  const timers = h("div.ss-timers");

  const saverCard = card("Screensaver", saverStatus,
    h("div.rules",
      row("screensaver", "Live screensaver", "When plugged in and idle, shows your live wallpaper fullscreen with a clock and keeps the screen on. A key press or click ends it — moving the mouse doesn't.", saverEnabled, { rule: true }),
      row("clock", "Start after", "Minutes without mouse or keyboard input, while plugged in.",
        h("div.ss-idle", idleSeg, h("label.num-wrap", idleInput, h("span", "min"))), { rule: true }),
      row("power", "Stop after", "After this, Hanami hides it and lets Windows turn the screen off and sleep as usual.", stopSeg, { rule: true }),
      row("lock", "Require sign-in when it ends", "Locks Windows the moment a key or click ends the screensaver, going straight to the lock screen instead of sliding up. To match that lock screen to the screensaver, see the Screensaver tab. Leave off if your desk is private.", signIn, { rule: true })),
    timers,
    h("p.footnote", svg("info"),
      h("span", "On battery Hanami never shows the screensaver, so Windows' own timers apply. Closing the lid always follows your Windows lid setting.")));

  function renderTimers(s) {
    const { windowsDisplayOffMinutes: off, windowsSleepMinutes: sleep } = s.screensaver;
    const idle = s.settings.screensaver.idleMinutes;
    const warnings = [];
    if (off != null && off <= idle) warnings.push(`Windows turns the screen off after ${off} min when plugged in — before the screensaver would start.`);
    if (sleep != null && sleep <= idle) warnings.push(`Windows puts your PC to sleep after ${sleep} min when plugged in — before the screensaver would start.`);
    if (warnings.length) {
      timers.className = "ss-timers ss-warn";
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

  // ---------------------------------------------------------------- frame rate
  const fpsAC = segmented(FPS.map((v) => ({ value: v, label: String(v) })), 60, (v) => ctx.mutate("updateSettings", { performance: { fpsCap: v } }).catch(() => {}), { ariaLabel: "FPS cap when plugged in" });
  const fpsBat = segmented(FPS.map((v) => ({ value: v, label: String(v) })), 30, (v) => ctx.mutate("updateSettings", { performance: { batteryFpsCap: v } }).catch(() => {}), { ariaLabel: "FPS cap on battery" });

  const el = h("section",
    pageHeader("Power", "Choose when Hanami runs, and how hard it works."),
    statusCard,
    keepCard,
    card("Desktop wallpaper", "If several apply, the most restrictive wins",
      h("div.legend", ...MODES.map((m) => h(`div.legend-item.mode-${m.value}`, h("span.mode-dot"), h("b", m.label), h("span", MODE_TEXT[m.value])))),
      rulesList),
    saverCard,
    card("Frame rate", "Reduced mode is always capped at 30",
      row("plug", "FPS cap when plugged in", "Higher is smoother; 60 suits most screens.", fpsAC),
      h("div.divider"),
      row("battery", "FPS cap on battery", "Used whenever you're unplugged and running Full.", fpsBat)));

  function update(s) {
    const p = s.settings.power;
    for (const c of CONDITIONS) {
      const r = rows[c.key];
      r.seg.set(p[c.key]);
      r.explain.textContent = `${MODE_LABEL[p[c.key]]}: ${MODE_TEXT[p[c.key]]}`;
      r.explain.dataset.mode = p[c.key];
      r.extra?.set(p.lowBatteryThreshold);
    }
    keepSwitch.set(p.onBattery === "full");
    keepCard.classList.toggle("on", p.onBattery === "full");
    fpsAC.set(s.settings.performance.fpsCap);
    fpsBat.set(s.settings.performance.batteryFpsCap);

    const cfg = s.settings.screensaver;
    if (cfg && s.screensaver) {
      saverEnabled.set(cfg.enabled);
      saverStatus.textContent = s.screensaver.status || (cfg.enabled ? "Ready" : "Off");
      idleSeg.set(cfg.idleMinutes);
      if (document.activeElement !== idleInput) idleInput.value = cfg.idleMinutes;
      idleInput.classList.toggle("custom", !IDLE_PRESETS.includes(cfg.idleMinutes));
      stopSeg.set(cfg.stopAfterHours);
      signIn.set(cfg.requireSignIn);
      renderTimers(s);
    }

    // live status
    const pw = s.power;
    const low = pw.onBattery && pw.batteryPercent != null && pw.batteryPercent <= p.lowBatteryThreshold;
    statusIcon.replaceChildren(svg(pw.onBattery ? (low ? "batteryLow" : "battery") : "plug"));
    statusIcon.className = `status-icon ${pw.onBattery ? (low ? "low" : "bat") : "ac"}`;
    statusTitle.textContent = pw.onBattery
      ? `On battery${pw.batteryPercent != null ? ` · ${pw.batteryPercent}%` : ""}${low ? " (low)" : ""}`
      : `Plugged in${pw.batteryPercent != null ? ` · ${pw.batteryPercent}% charged` : ""}`;
    statusSub.textContent = [`Energy Saver ${pw.energySaver ? "on" : "off"}`, s.settings.paused ? "Paused manually" : null].filter(Boolean).join(" · ");

    monitorList.replaceChildren(...s.monitors.map((m) => {
      const md = s.modes[m.key] ?? { mode: "full", reason: "" };
      return h(`div.monitor-mode.mode-${md.mode}`,
        svg("monitor"),
        h("div", h("div.mm-name", m.name), h("div.mm-mode", h("span.mode-dot"), MODE_LABEL[md.mode], md.reason ? h("span.mm-reason", ` — ${md.reason}`) : null)));
    }));

    // highlight the battery/AC rows that currently apply
    const activeKeys = new Set([pw.onBattery ? (low ? "lowBattery" : "onBattery") : "onAC"]);
    if (pw.energySaver) activeKeys.add("energySaver");
    for (const c of CONDITIONS) {
      const on = activeKeys.has(c.key);
      rows[c.key].row.classList.toggle("active", on);
      rows[c.key].active.hidden = !on;
    }
  }

  return { el, update };
}

function debounced(fn, ms) {
  let t;
  return (v) => { clearTimeout(t); t = setTimeout(() => fn(v), ms); };
}
