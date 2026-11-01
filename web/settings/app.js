// Hanami settings — app shell: state, sidebar, tab routing, monitor scope.
import { call, on, isMock } from "./bridge.js";
import { h, svg, toast, toastError } from "./util.js";
import { LOGO } from "./icons.js";
import { createWallpapersTab } from "./tabs/wallpapers.js";
import { createEffectsTab } from "./tabs/effects.js";
import { createPowerTab } from "./tabs/power.js";
import { createGeneralTab } from "./tabs/general.js";
import { createTransferTab } from "./tabs/transfer.js";
import { FALLBACK_EFFECT } from "./defaults.js";


const TABS = [
  { id: "wallpapers", label: "Wallpapers", icon: "image", create: createWallpapersTab },
  { id: "effects", label: "Effects", icon: "sparkles", create: createEffectsTab },
  { id: "power", label: "Power", icon: "gauge", create: createPowerTab },
  { id: "general", label: "General", icon: "gear", create: createGeneralTab },
  { id: "transfer", label: "Export / Import", icon: "transfer", create: createTransferTab },
];

let state = null;
let selectedMonitor = null;   // monitorKey shown when !sameOnAllMonitors
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

  /** Effective {wallpaperId, effect} for a key (falls back to "*"). */
  assignmentFor(key) {
    const a = state?.settings.assignments ?? {};
    const x = a[key] ?? a["*"] ?? { wallpaperId: null, effect: FALLBACK_EFFECT };
    return { wallpaperId: x.wallpaperId ?? null, effect: { ...FALLBACK_EFFECT, ...(x.effect ?? {}) } };
  },

  /** setAssignment for the current scope. When creating a new per-monitor override we send
   *  the full assignment (copied from "*"), so the result never depends on how the host
   *  fills in missing fields. */
  assign({ wallpaperId, effect }, key = ctx.scopeKey()) {
    const params = { monitorKey: key };
    const isNewOverride = key !== "*" && !state.settings.assignments[key];
    if (isNewOverride) {
      const cur = ctx.assignmentFor(key);
      const wid = wallpaperId ?? cur.wallpaperId;
      if (wid) params.wallpaperId = wid;
      params.effect = { ...cur.effect, ...(effect ?? {}) };
    } else {
      if (wallpaperId !== undefined) params.wallpaperId = wallpaperId;
      if (effect) params.effect = effect;
    }
    return ctx.mutate("setAssignment", params);
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
  let sub = "";
  if (!allSame && entries.length > 1) {
    const affected = entries.filter((e) => e.mode === worst.mode).map((e) => e.m.name);
    sub = `on ${affected.join(", ")}`;
  }
  return { mode: worst.mode, text, sub };
}

function buildSidebar(nav) {
  const dot = h("span.status-dot");
  const text = h("span.status-text");
  const sub = h("span.status-sub");
  const battery = h("span.status-battery");
  const pauseLabel = h("span");
  const pauseIcon = h("span.ico-wrap");
  const pauseBtn = h("button.pause-btn", { type: "button",
    onclick: async () => {
      pauseBtn.disabled = true;
      try { await ctx.mutate("setPaused", { paused: !state.settings.paused }); } catch {}
      pauseBtn.disabled = false;
    } }, pauseIcon, pauseLabel);
  const status = h("div.status-card", h("div.status-pill", dot, h("div.status-lines", text, sub)), battery, pauseBtn);

  const side = h("aside.sidebar",
    h("div.brand", h("span", { html: LOGO }), h("div", h("div.brand-name", "Hanami"), h("div.brand-sub", "Live wallpaper"))),
    nav,
    h("div.spacer"),
    status);

  ctx.onState((s) => {
    const sum = summarizeModes(s);
    status.dataset.mode = sum.mode;
    text.textContent = sum.text;
    sub.textContent = sum.sub;
    sub.hidden = !sum.sub;
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
