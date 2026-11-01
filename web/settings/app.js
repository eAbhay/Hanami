// Hanami settings — app shell: state, sidebar, tab routing, monitor scope.
import { call, on, isMock } from "./bridge.js";
import { h, svg, toast, toastError } from "./util.js";
import { LOGO } from "./icons.js";
import { createWallpapersTab } from "./tabs/wallpapers.js";
import { createPowerTab } from "./tabs/power.js";
import { createScreensaverTab } from "./tabs/screensaver.js";
import { createGeneralTab } from "./tabs/general.js";
import { createTransferTab } from "./tabs/transfer.js";
import { FALLBACK_EFFECT, DEFAULT_CLOCK } from "./defaults.js";
import { statusPill, createTuningDrawer } from "./components/index.js";
import { loadEffects } from "./effects-module.js";


const TABS = [
  { id: "wallpapers", label: "Wallpaper", icon: "image", create: createWallpapersTab },
  { id: "screensaver", label: "Screensaver", icon: "screensaver", create: createScreensaverTab },
  { id: "power", label: "Power", icon: "gauge", create: createPowerTab },
  { id: "general", label: "General", icon: "gear", create: createGeneralTab },
  { id: "transfer", label: "Export / Import", icon: "transfer", create: createTransferTab },
];

let state = null;
let selectedMonitor = null;   // monitorKey shown when !sameOnAllMonitors
let presetDefaults = {};      // PRESET_DEFAULTS from the effects engine, once it has loaded
const listeners = new Set();

// ------------------------------------------------------------------ context shared with tabs
const ctx = {
  call,
  on,
  get state() { return state; },

  /** Run a bridge method that returns State, apply it, toast on error. */
  async mutate(method, params) {
    try {
      const s = await call(method, params);
      if (s && s.settings) applyState(s);
      return s;
    } catch (err) {
      toastError(err);
      throw err;
    }
  },

  /** The assignment key edits go to: "*" or a monitorKey. */
  scopeKey() {
    if (!state || state.settings.sameOnAllMonitors) return "*";
    return selectedMonitor ?? "*";
  },

  /** Full EffectConfig for a preset: contract fallback < the preset's defaults < the effect defaults
   *  (settings.effects) < `tuning`, a look's own overrides for it. */
  effectFor(preset, tuning = null) {
    return { ...FALLBACK_EFFECT, ...(presetDefaults[preset] ?? {}), ...(state?.settings.effects?.[preset] ?? {}), ...(tuning ?? {}), preset };
  },

  /** A wallpaper's look; the built-in default for a wallpaper that has none yet (nothing is stored for it). */
  lookFor(wallpaperId) {
    const l = state?.settings.looks?.[wallpaperId] ?? {};
    return {
      desktop: { effectPreset: l.desktop?.effectPreset ?? FALLBACK_EFFECT.preset, tuning: l.desktop?.tuning ?? {} },
      screensaver: {
        effectPreset: l.screensaver?.effectPreset ?? null,   // null = same as desktop
        tuning: l.screensaver?.tuning ?? {},
        clock: { ...DEFAULT_CLOCK, ...(l.screensaver?.clock ?? {}) },
      },
    };
  },

  /** The tuning overrides a look keeps for `preset` on `target`. A screensaver effect that is "same as
   *  desktop" shares the desktop's tuning. */
  lookTuning(wallpaperId, preset, target = "desktop") {
    const look = ctx.lookFor(wallpaperId);
    const own = target === "screensaver" && look.screensaver.effectPreset !== null;
    return (own ? look.screensaver.tuning : look.desktop.tuning)[preset] ?? null;
  },

  /** Effective {wallpaperId} for a key (falls back to "*"). */
  assignmentFor(key) {
    const a = state?.settings.assignments ?? {};
    const x = a[key] ?? a["*"] ?? { wallpaperId: null };
    return { wallpaperId: x.wallpaperId ?? null };
  },

  /** The effect a wallpaper shows on the desktop: {preset, ...EffectConfig}. */
  desktopEffect(wallpaperId) {
    const d = ctx.lookFor(wallpaperId).desktop;
    return ctx.effectFor(d.effectPreset, d.tuning[d.effectPreset]);
  },

  /** The wallpaper the screensaver shows for a key: its own, or that monitor's desktop wallpaper. */
  screensaverBackground(key = ctx.scopeKey()) {
    return state?.settings.screensaver.wallpaperId ?? ctx.assignmentFor(key).wallpaperId;
  },

  /** The effect a wallpaper shows as a screensaver: its own, or (null) the desktop one with shared tuning. */
  screensaverEffect(wallpaperId) {
    const look = ctx.lookFor(wallpaperId);
    const own = look.screensaver.effectPreset;
    return own ? ctx.effectFor(own, look.screensaver.tuning[own]) : ctx.desktopEffect(wallpaperId);
  },

  /** Shows a wallpaper on the current scope's desktop. The host copies the look of the one it replaces. */
  async assign(wallpaperId, key = ctx.scopeKey()) {
    return ctx.mutate("setAssignment", { monitorKey: key, wallpaperId });
  },

  /** Edits a wallpaper's look: {desktop?: {effectPreset?}, screensaver?: {effectPreset?: id | null, clock?}}. */
  setLook(wallpaperId, patch) {
    return ctx.mutate("setLook", { wallpaperId, ...patch });
  },

  /** Scope switching: "*" = same on all monitors, otherwise a monitorKey. */
  async setScope(target) {
    if (target === "*") {
      if (!state.settings.sameOnAllMonitors) await ctx.mutate("updateSettings", { sameOnAllMonitors: true });
    } else {
      selectedMonitor = target;
      if (state.settings.sameOnAllMonitors) await ctx.mutate("updateSettings", { sameOnAllMonitors: false });
      else notify();
    }
  },
  get selectedMonitor() { return selectedMonitor; },

  monitorName(key) { return state?.monitors.find((m) => m.key === key)?.name ?? key; },
  wallpaper(id) { return state?.wallpapers.find((w) => w.id === id) ?? null; },
  onState(fn) { listeners.add(fn); return () => listeners.delete(fn); },
  toast,
  toastError,
};

function applyState(s) {
  state = s;
  if (!selectedMonitor || !s.monitors.some((m) => m.key === selectedMonitor)) {
    selectedMonitor = (s.monitors.find((m) => m.isPrimary) ?? s.monitors[0])?.key ?? null;
  }
  notify();
}

// The effects engine holds each preset's defaults; refresh once it has loaded.
loadEffects().then((m) => { presetDefaults = m.PRESET_DEFAULTS ?? {}; if (state) notify(); }).catch(() => {});

function notify() {
  for (const fn of listeners) {
    try { fn(state); } catch (err) { console.error(err); }
  }
}

// ------------------------------------------------------------------ sidebar status
const MODE_LABEL = { full: "Running", reduced: "Reduced", paused: "Paused" };
const RANK = { paused: 0, reduced: 1, full: 2 };

function summarizeModes(s) {
  const entries = s.monitors.map((m) => ({ m, ...(s.modes[m.key] ?? { mode: "full", reason: "" }) }));
  if (!entries.length) return { mode: "full", text: "Running", sub: "" };
  const worst = entries.reduce((a, b) => (RANK[b.mode] < RANK[a.mode] ? b : a));
  const allSame = entries.every((e) => e.mode === worst.mode && e.reason === worst.reason);
  const label = MODE_LABEL[worst.mode];
  const r = (worst.reason ?? "").trim();
  let text = label;
  if (r && worst.mode !== "full") {
    // "paused manually" → "Paused manually" rather than "Paused — paused manually"
    text = r.toLowerCase().startsWith(label.toLowerCase()) ? r[0].toUpperCase() + r.slice(1) : `${label} — ${r}`;
  }
  const parts = [screensaverClause(s)];
  if (!allSame && entries.length > 1) {
    const affected = entries.filter((e) => e.mode === worst.mode).map((e) => e.m.name);
    parts.push(`on ${affected.join(", ")}`);
  }
  return { mode: worst.mode, text, sub: parts.filter(Boolean).join(" · ") };
}

/** The screensaver's part of the sidebar status: when it starts, or why it won't. */
function screensaverClause(s) {
  const st = s.screensaver, cfg = s.settings.screensaver;
  if (!st || !cfg) return "";
  if (st.active) return "Screensaver showing";
  if (!cfg.enabled) return "";
  if (s.power.onBattery) return "Screensaver off on battery";
  const m = /^Starts after (\d+) min idle/.exec(st.status ?? "");
  if (m) return `Screensaver in ${m[1]} min`;
  return st.status?.startsWith("Waiting") ? "Screensaver waiting" : "";
}

function buildSidebar(nav) {
  const battery = h("span.status-battery");
  const pauseLabel = h("span");
  const pauseIcon = h("span.ico-wrap");
  const pauseBtn = h("button.pause-btn", { type: "button",
    onclick: async () => {
      pauseBtn.disabled = true;
      try { await ctx.mutate("setPaused", { paused: !state.settings.paused }); } catch {}
      pauseBtn.disabled = false;
    } }, pauseIcon, pauseLabel);
  const status = statusPill(battery, pauseBtn);

  const side = h("aside.sidebar",
    h("div.brand", h("span", { html: LOGO }), h("div", h("div.brand-name", "Hanami"), h("div.brand-sub", "Live wallpaper"))),
    nav,
    h("div.spacer"),
    status);

  ctx.onState((s) => {
    status.set(summarizeModes(s));
    const p = s.power;
    battery.hidden = !p.onBattery;
    battery.innerHTML = "";
    if (p.onBattery) {
      const low = p.batteryPercent != null && p.batteryPercent <= s.settings.power.lowBatteryThreshold;
      battery.classList.toggle("low", low);
      battery.append(svg(low ? "batteryLow" : "battery"),
        h("span", p.batteryPercent != null ? `On battery · ${p.batteryPercent}%` : "On battery"));
      if (p.energySaver) battery.append(h("span.chip", "Energy Saver"));
    }
    const paused = s.settings.paused;
    pauseBtn.classList.toggle("is-paused", paused);
    pauseIcon.innerHTML = "";
    pauseIcon.append(svg(paused ? "play" : "pause"));
    pauseLabel.textContent = paused ? "Resume" : "Pause";
    pauseBtn.title = paused ? "Resume wallpapers on every monitor" : "Pause wallpapers on every monitor";
  });
  return side;
}

// ------------------------------------------------------------------ boot
async function boot() {
  const app = document.getElementById("app");
  const main = h("main.content");
  const nav = h("nav.nav", { "aria-label": "Settings sections" });
  const tabs = new Map();
  let active = null;

  function show(id) {
    if (!tabs.has(id)) id = "wallpapers";
    if (active === id) return;
    const prev = active && tabs.get(active);
    if (prev) { prev.el.hidden = true; prev.hide?.(); }
    active = id;
    const t = tabs.get(id);
    t.el.hidden = false;
    t.el.classList.remove("enter"); void t.el.offsetWidth; t.el.classList.add("enter");
    main.scrollTop = 0;
    t.show?.();
    for (const b of nav.children) b.classList.toggle("active", b.dataset.tab === id);
    try { localStorage.setItem("hanami.tab", id); } catch {}
    if (location.hash !== `#${id}`) history.replaceState(null, "", `#${id}`);
  }

  for (const t of TABS) {
    nav.append(h("button.nav-item", { type: "button", "data-tab": t.id, onclick: () => show(t.id) },
      svg(t.icon), h("span", t.label)));
  }

  app.append(buildSidebar(nav), main);

  // One shared "<Effect> settings" drawer; tabs open it from an effect tile's gear.
  const drawer = createTuningDrawer(ctx);
  ctx.openTuning = drawer.open;
  ctx.showTab = (id) => show(id);
  ctx.onState((s) => drawer.update(s));

  try {
    applyState(await call("getState"));
  } catch (err) {
    main.append(h("div.fatal", h("h2", "Couldn't reach Hanami"), h("p.muted", err?.message ?? String(err))));
    return;
  }
  on("stateChanged", (s) => s && applyState(s));

  for (const t of TABS) {
    const tab = t.create(ctx);
    tab.el.hidden = true;
    tab.el.classList.add("tab");
    main.append(tab.el);
    tabs.set(t.id, tab);
    tab.update?.(state);
    ctx.onState((s) => tab.update?.(s));
  }

  let initial = location.hash.slice(1);
  if (!initial) { try { initial = localStorage.getItem("hanami.tab") ?? ""; } catch {} }
  show(initial || "wallpapers");
  window.addEventListener("hashchange", () => show(location.hash.slice(1)));

  if (await isMock()) document.documentElement.dataset.mock = "";
  document.body.classList.add("ready");
}

boot();
