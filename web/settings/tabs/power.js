// Power tab — per-condition Full / Reduced / Paused, battery threshold, FPS caps, live status.
import { h, svg, segmented, slider, toggle } from "../util.js";

const MODES = [
  { value: "full", label: "Full", title: "Video plays, effects run at your FPS cap" },
  { value: "reduced", label: "Reduced", title: "Video holds its current frame, effects at up to 30 FPS" },
  { value: "paused", label: "Paused", title: "Everything frozen — no animation work at all" },
];
const MODE_TEXT = {
  full: "Video plays and effects run at your FPS cap.",
  reduced: "Video holds its current frame; effects keep moving at up to 30 FPS.",
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

export function createPowerTab(ctx) {
  const setPower = (patch) => ctx.mutate("updateSettings", { power: patch }).catch(() => {});

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

  // ---------------------------------------------------------------- rules
  const rows = {};
  const rulesList = h("div.rules");
  for (const c of CONDITIONS) {
    const explain = h("div.rule-explain");
    const seg = segmented(MODES, "full", (v) => setPower({ [c.key]: v }), { ariaLabel: c.title, cls: "modes" });
    const extra = c.threshold
      ? slider({ label: "Low-battery threshold", min: 5, max: 50, step: 1, value: 20, format: (v) => `${v}%`,
          onInput: debounced((v) => setPower({ lowBatteryThreshold: v }), 200) })
      : null;
    const row = h("div.rule", { "data-key": c.key },
      h("div.rule-icon", svg(c.icon)),
      h("div.rule-text", h("div.rule-title", c.title, h("span.rule-active", { hidden: true }, "Now")), h("div.rule-sub", c.sub), explain),
      seg,
      extra ? h("div.rule-extra", extra) : null);
    rows[c.key] = { row, seg, explain, extra };
    rulesList.append(row);
  }

  // ---------------------------------------------------------------- fps
  const fpsAC = segmented(FPS.map((v) => ({ value: v, label: String(v) })), 60, (v) => ctx.mutate("updateSettings", { performance: { fpsCap: v } }).catch(() => {}), { ariaLabel: "FPS cap when plugged in" });
  const fpsBat = segmented(FPS.map((v) => ({ value: v, label: String(v) })), 30, (v) => ctx.mutate("updateSettings", { performance: { batteryFpsCap: v } }).catch(() => {}), { ariaLabel: "FPS cap on battery" });
  const row = (icon, title, sub, control) => h("div.row", h("div.row-icon", svg(icon)), h("div.row-text", h("div.row-title", title), h("div.row-sub", sub)), control);

  const el = h("section",
    h("header.page-head",
      h("div", h("h1", "Power"), h("p.page-sub", "Choose how hard Hanami works in each situation."))),
    statusCard,
    keepCard,
    h("div.card.pad",
      h("div.card-head", h("h2", "When…"), h("span.card-meta", "If several apply, the most restrictive wins")),
      h("div.legend", ...MODES.map((m) => h(`div.legend-item.mode-${m.value}`, h("span.mode-dot"), h("b", m.label), h("span", MODE_TEXT[m.value])))),
      rulesList),
    h("div.card.pad",
      h("div.card-head", h("h2", "Frame rate"), h("span.card-meta", "Reduced mode is always capped at 30")),
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
      rows[c.key].row.querySelector(".rule-active").hidden = !on;
    }
  }

  return { el, update };
}

function debounced(fn, ms) {
  let t;
  return (v) => { clearTimeout(t); t = setTimeout(() => fn(v), ms); };
}
