// In-memory fake Hanami host, used when the settings page runs in a normal browser.
// Implements every settings-bridge method from docs/CONTRACTS.md with realistic state.
//
// Dev helpers (browser console):
//   hanamiMock.setPower({ onBattery: true, batteryPercent: 64, energySaver: false })
//   hanamiMock.setWindowState("\\\\.\\DISPLAY2", "fullscreen" | "maximized" | null)
//   hanamiMock.failNext("setAssignment", "Something went wrong")
//   hanamiMock.setWindowsTimers(displayOffMin | null, sleepMin | null)
//   hanamiMock.setWindowsTheme("dark" | "light")
//   hanamiMock.state   (live state object)

const MON1 = "\\\\.\\DISPLAY1";
const MON2 = "\\\\.\\DISPLAY2";
const LIB = "D:\\Apps\\Hanami\\Library";

const clone = (o) => (o === undefined ? o : JSON.parse(JSON.stringify(o)));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const isObj = (v) => v !== null && typeof v === "object" && !Array.isArray(v);

function deepMerge(target, patch) {
  for (const [k, v] of Object.entries(patch ?? {})) {
    if (isObj(v) && isObj(target[k])) deepMerge(target[k], v);
    else target[k] = clone(v);
  }
  return target;
}

const randomId = () => Math.random().toString(36).slice(2, 8);

// ---------------------------------------------------------------------------------------------
// Procedural sample media (so thumbnails and the client-side thumbnail generator can be tested
// offline). Each "scene" is a small painter function.
const PALETTES = {
  river:   { sky: ["#f7c8d8", "#f1a3bf", "#b68fd6"], ground: "#5b4a7d", water: "#9fb6e8", accent: "#ffe3ec" },
  alley:   { sky: ["#141a3a", "#2a2560", "#5a3a7a"], ground: "#0d1024", water: "#2c3a6e", accent: "#ff7eb6" },
  temple:  { sky: ["#ffd9a8", "#f7a68a", "#9a6fb0"], ground: "#3f3354", water: "#e7b6a0", accent: "#fff2d6" },
  lantern: { sky: ["#2b1d4a", "#6a2f5f", "#e0786a"], ground: "#1a1230", water: "#41305e", accent: "#ffc36b" },
  koi:     { sky: ["#bfe6e0", "#86c6c9", "#6a8fb8"], ground: "#2f4f5f", water: "#5b9fb0", accent: "#ff9f7a" },
  shrine:  { sky: ["#0c1330", "#1d2556", "#3b3a7a"], ground: "#0a0f22", water: "#1c2b55", accent: "#cfd8ff" },
  fields:  { sky: ["#fff1c9", "#ffd1a1", "#f5a3b5"], ground: "#6f8a5a", water: "#b9d48e", accent: "#ffffff" },
};

function paintScene(ctx, w, h, p, t = 0) {
  const g = ctx.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, p.sky[0]); g.addColorStop(0.55, p.sky[1]); g.addColorStop(1, p.sky[2]);
  ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
  // sun / moon
  ctx.fillStyle = p.accent; ctx.globalAlpha = 0.85;
  ctx.beginPath(); ctx.arc(w * 0.72, h * 0.32, h * 0.09, 0, Math.PI * 2); ctx.fill();
  ctx.globalAlpha = 1;
  // hills
  ctx.fillStyle = p.ground;
  ctx.beginPath(); ctx.moveTo(0, h * 0.72);
  for (let x = 0; x <= w; x += w / 24) ctx.lineTo(x, h * (0.66 + 0.06 * Math.sin(x / w * 7 + 1.3)));
  ctx.lineTo(w, h); ctx.lineTo(0, h); ctx.fill();
  // water band with moving shimmer
  ctx.fillStyle = p.water; ctx.globalAlpha = 0.75;
  ctx.fillRect(0, h * 0.82, w, h * 0.18);
  ctx.globalAlpha = 0.5; ctx.fillStyle = p.accent;
  for (let i = 0; i < 14; i++) {
    const x = ((i * 97 + t * 60) % (w + 80)) - 40;
    ctx.fillRect(x, h * (0.86 + (i % 3) * 0.04), 36, 2);
  }
  // drifting petals / stars
  ctx.globalAlpha = 0.8;
  for (let i = 0; i < 26; i++) {
    const x = (i * 131 + t * 40 * (1 + (i % 3))) % w;
    const y = (i * 71 + t * 25) % (h * 0.8);
    ctx.beginPath(); ctx.ellipse(x, y, 3, 2, i, 0, Math.PI * 2); ctx.fill();
  }
  ctx.globalAlpha = 1;
}

function sceneImage(palette, w = 480, h = 270) {
  const c = document.createElement("canvas");
  c.width = w; c.height = h;
  paintScene(c.getContext("2d"), w, h, PALETTES[palette]);
  return c.toDataURL("image/jpeg", 0.85);
}

/** Records a short animated clip of a scene; resolves to a blob: URL (or null if unsupported). */
async function sceneVideo(palette, seconds = 1.6) {
  try {
    if (typeof MediaRecorder === "undefined") return null;
    const c = document.createElement("canvas");
    c.width = 640; c.height = 360;
    const ctx = c.getContext("2d");
    const stream = c.captureStream(30);
    const mime = ["video/webm;codecs=vp9", "video/webm;codecs=vp8", "video/webm"].find((m) => MediaRecorder.isTypeSupported(m));
    if (!mime) return null;
    const rec = new MediaRecorder(stream, { mimeType: mime });
    const chunks = [];
    rec.ondataavailable = (e) => e.data.size && chunks.push(e.data);
    const done = new Promise((r) => (rec.onstop = r));
    const t0 = performance.now();
    paintScene(ctx, 640, 360, PALETTES[palette], 0);
    rec.start(200);
    const timer = setInterval(() => paintScene(ctx, 640, 360, PALETTES[palette], (performance.now() - t0) / 1000), 33);
    await sleep(seconds * 1000);
    clearInterval(timer);
    rec.stop();
    await done;
    return URL.createObjectURL(new Blob(chunks, { type: "video/webm" }));
  } catch (err) {
    console.warn("[mock] could not record sample video", err);
    return null;
  }
}

// ---------------------------------------------------------------------------------------------
function initialState() {
  const builtins = [
    { id: "builtin:twilight", name: "Twilight", type: "builtin", builtin: true, sizeBytes: 0 },
    { id: "builtin:dawn",     name: "Dawn",     type: "builtin", builtin: true, sizeBytes: 0 },
    { id: "builtin:night",    name: "Night",    type: "builtin", builtin: true, sizeBytes: 0 },
  ];
  const lib = [
    { id: "k3j2h1", name: "Sakura river", type: "video", palette: "river",
      sizeBytes: 48_213_402, width: 1920, height: 1080, durationSec: 12.5, createdAt: "2026-10-12T09:21:00Z" },
    { id: "p9x7aa", name: "Rainy alley at night", type: "video", palette: "alley",
      sizeBytes: 131_877_120, width: 3840, height: 2160, durationSec: 30, createdAt: "2026-10-20T19:02:00Z" },
    { id: "t4mpl3", name: "Mountain temple", type: "image", palette: "temple",
      sizeBytes: 3_412_990, width: 3840, height: 2160, createdAt: "2026-10-28T14:45:00Z", noThumb: true },
  ].map((w) => {
    const ext = w.type === "video" ? "mp4" : "jpg";
    const media = sceneImage(w.palette, w.type === "image" ? 1280 : 480, w.type === "image" ? 720 : 270);
    return {
      id: w.id, name: w.name, type: w.type, builtin: false,
      // In the real host these are library.hanami URLs. The mock uses data URLs so they load offline.
      src: w.type === "image" ? media : `https://library.hanami/wallpapers/${w.id}/media.${ext}`,
      thumbnail: w.noThumb ? null : media,
      sizeBytes: w.sizeBytes, width: w.width, height: w.height,
      ...(w.durationSec ? { durationSec: w.durationSec } : {}),
      createdAt: w.createdAt,
    };
  });

  return {
    version: "0.1.0",
    installDir: "D:\\Apps\\Hanami",
    libraryDir: LIB,
    settings: {
      sameOnAllMonitors: true,
      assignments: { "*": { wallpaperId: "builtin:twilight" } },
      effects: {},   // effect defaults: per-effect overrides, e.g. { snow: { density: 2 } }
      pets: { enabled: true, items: [], scale: 1, climbWindows: true, socialize: true, activity: "normal" },
      theme: { split: false, mode: "system" },   // light/dark wallpapers: off by default
      looks: {},     // per wallpaper: { desktop: { effectPreset, tuning }, screensaver: { effectPreset | absent, tuning, clock } }
      power: {
        onAC: "full", onBattery: "paused", lowBattery: "paused", lowBatteryThreshold: 20,
        energySaver: "reduced", whenMaximized: "paused", whenFullscreen: "paused",
      },
      performance: { fpsCap: 60, batteryFpsCap: 30 },
      screensaver: { enabled: true, idleMinutes: 2, wallpaperId: null, requireSignIn: false, stopAfterHours: 0 },
      startWithWindows: true,
      paused: false,
    },
    monitors: [
      { key: MON1, name: "Built-in display", isPrimary: true,  width: 2560, height: 1600, scale: 1.5 },
      { key: MON2, name: "DELL U2723QE",     isPrimary: false, width: 3840, height: 2160, scale: 1.5 },
    ],
    modes: {},
    power: { onBattery: false, batteryPercent: 82, energySaver: false },
    wallpapers: [...builtins, ...lib],
    // Windows' plugged-in timers (the real host reads them from the active power plan).
    screensaver: { active: false, status: "", windowsDisplayOffMinutes: 10, windowsSleepMinutes: 30 },
    theme: { active: "light", windows: "light" },
    petPacks: [],
  };
}

const RANK = { paused: 0, reduced: 1, full: 2 };
const defaultLook = () => ({
  desktop: { effectPreset: "sakura", tuning: {} },
  screensaver: { tuning: {}, clock: { x: 0.5, y: 0.31, size: 1, font: null, dateFont: "system", showDate: true, tint: "#fff4fc", opacity: 0.16, frost: 18 } },
});

export function createMockHost(post) {
  const state = initialState();
  const windowState = { [MON1]: null, [MON2]: null };   // null | "maximized" | "fullscreen"
  const failures = new Map();
  const importNames = [
    ["Lantern festival", "lantern"], ["Koi pond", "koi"], ["Starlit shrine", "shrine"], ["Summer fields", "fields"],
  ];
  let importCounter = 0;

  function computeModes() {
    const s = state.settings, p = state.power;
    for (const m of state.monitors) {
      const c = [];
      if (s.paused) c.push({ mode: "paused", reason: "paused manually" });
      if (windowState[m.key] === "fullscreen") c.push({ mode: s.power.whenFullscreen, reason: "fullscreen app" });
      if (windowState[m.key] === "maximized") c.push({ mode: s.power.whenMaximized, reason: "maximized window" });
      if (p.onBattery) {
        const low = p.batteryPercent != null && p.batteryPercent <= s.power.lowBatteryThreshold;
        c.push(low ? { mode: s.power.lowBattery, reason: "low battery" } : { mode: s.power.onBattery, reason: "on battery" });
      } else {
        c.push({ mode: s.power.onAC, reason: "plugged in" });
      }
      if (p.energySaver) c.push({ mode: s.power.energySaver, reason: "Energy Saver" });
      let best = { mode: "full", reason: p.onBattery ? "on battery" : "plugged in" };
      for (const x of c) if (RANK[x.mode] < RANK[best.mode]) best = x;
      state.modes[m.key] = best;
    }
  }
  computeModes();

  let previewTimer = 0;
  function computeScreensaver() {
    const cfg = state.settings.screensaver, ss = state.screensaver;
    if (ss.active) ss.status = "Showing";
    else if (!cfg.enabled) ss.status = "Off";
    else if (state.power.onBattery) ss.status = "Off on battery";
    else ss.status = `Starts after ${cfg.idleMinutes} min idle`;
  }

  function computeTheme() {
    const m = state.settings.theme.mode;
    state.theme.active = m === "system" ? state.theme.windows : m;
  }
  const snapshot = () => { computeModes(); computeScreensaver(); computeTheme(); return clone(state); };
  const emit = (event, data) => setTimeout(() => post({ event, data: clone(data) }), 0);
  const emitState = () => emit("stateChanged", snapshot());
  const findWp = (id) => state.wallpapers.find((w) => w.id === id);
  const lookOf = (id) => state.settings.looks[id] ?? defaultLook();

  async function progress(task, steps, labelFor) {
    for (let i = 0; i <= steps; i++) {
      emit("progress", { task, done: i, total: steps, label: labelFor(i) });
      if (i < steps) await sleep(90 + Math.random() * 70);
    }
  }

  async function addFakeWallpaper(name, palette) {
    const id = randomId();
    const src = await sceneVideo(palette);
    const isVideo = !!src;
    const w = {
      id, name, type: isVideo ? "video" : "image", builtin: false,
      src: src ?? sceneImage(palette, 1280, 720),
      thumbnail: null,      // the settings UI generates it via saveThumbnail
      sizeBytes: Math.round((isVideo ? 20 + Math.random() * 90 : 2 + Math.random() * 4) * 1_000_000),
      width: isVideo ? 1920 : 3840, height: isVideo ? 1080 : 2160,
      createdAt: new Date().toISOString(),
    };
    state.wallpapers.push(w);
    return w;
  }

  let importedFonts = [];
  const methods = {
    getState: () => snapshot(),

    updateSettings: (patch) => {
      if (!isObj(patch)) throw new Error("updateSettings expects an object");
      const prevBackground = state.settings.screensaver.wallpaperId ?? state.settings.assignments["*"].wallpaperId;
      deepMerge(state.settings, patch);
      const ss = state.settings.screensaver;
      ss.idleMinutes = Math.min(120, Math.max(1, Math.round(ss.idleMinutes)));
      if (ss.wallpaperId && !state.settings.looks[ss.wallpaperId]) {   // a new background brings the previous one's screensaver look
        const look = defaultLook();
        look.screensaver = clone(lookOf(prevBackground).screensaver);
        state.settings.looks[ss.wallpaperId] = look;
      }
      emitState();
      return snapshot();
    },

    setAssignment: ({ monitorKey, wallpaperId, theme } = {}) => {
      if (monitorKey !== "*" && !state.monitors.some((m) => m.key === monitorKey)) throw new Error(`Unknown monitor ${monitorKey}`);
      if (wallpaperId !== undefined && wallpaperId !== null && !findWp(wallpaperId)) throw new Error("That wallpaper no longer exists");
      const a = state.settings.assignments;
      const cur = a[monitorKey] ?? clone(a["*"]) ?? { wallpaperId: null };
      if (wallpaperId && !state.settings.looks[wallpaperId]) state.settings.looks[wallpaperId] = clone(lookOf(cur.wallpaperId));   // starts from the look it replaces
      if (wallpaperId !== undefined) { if (theme === "dark") cur.darkWallpaperId = wallpaperId; else cur.wallpaperId = wallpaperId; }
      a[monitorKey] = cur;
      emitState();
      return snapshot();
    },

    setLook: ({ wallpaperId, desktop, screensaver } = {}) => {
      if (!wallpaperId) throw new Error("Missing wallpaperId");
      const look = (state.settings.looks[wallpaperId] ??= defaultLook());
      if (desktop) deepMerge(look.desktop, desktop);
      if (screensaver) {
        const { effectPreset, clock, ...rest } = screensaver;
        deepMerge(look.screensaver, rest);
        if ("effectPreset" in screensaver) { if (effectPreset) look.screensaver.effectPreset = effectPreset; else delete look.screensaver.effectPreset; }
        if (clock) {
          deepMerge(look.screensaver.clock, clock);
          const ck = look.screensaver.clock;
          ck.x = Math.min(1, Math.max(0, ck.x)); ck.y = Math.min(1, Math.max(0, ck.y));
          ck.size = Math.min(1.6, Math.max(0.5, ck.size));
          ck.opacity = Math.min(0.8, Math.max(0.04, Number(ck.opacity) || 0.16));
          ck.frost = Math.min(40, Math.max(0, Number.isFinite(Number(ck.frost)) ? Number(ck.frost) : 18));
          if (!/^#[0-9a-fA-F]{6}$/.test(ck.tint)) ck.tint = "#fff4fc";
        }
      }
      emitState();
      return snapshot();
    },

    setEffectTuning: ({ preset, tuning, wallpaperId, target = "desktop" } = {}) => {
      if (!preset) throw new Error("Missing preset");
      let bag = state.settings.effects;
      if (wallpaperId) {
        const look = (state.settings.looks[wallpaperId] ??= defaultLook());
        bag = target === "screensaver" && look.screensaver.effectPreset ? look.screensaver.tuning : look.desktop.tuning;
      }
      if (tuning === null) delete bag[preset];
      else bag[preset] = { ...(bag[preset] ?? {}), ...tuning };
      emitState();
      return snapshot();
    },

    importPet: async ({ source } = {}) => {
      await sleep(400);
      const n = state.petPacks.length + 1;
      const preview = `data:image/svg+xml;utf8,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><circle cx="32" cy="34" r="22" fill="#f4a6c0"/><circle cx="24" cy="30" r="3"/><circle cx="40" cy="30" r="3"/></svg>`)}`;
      const pack = { id: `pet-mock${n}`, name: source === "folder" ? `Folder pet ${n}` : `Zip pet ${n}`, preview, frames: 46, sizeBytes: 640_000 * n,
        frameWidth: 128, frameHeight: 128, capabilities: { climb: true, ceiling: n % 2 === 1, sit: true, sleep: true } };
      state.petPacks.push(pack);
      emitState();
      return { imported: [clone(pack)], rejected: [] };
    },

    renamePet: ({ id, name }) => {
      const p = state.petPacks.find((x) => x.id === id);
      if (!p) throw new Error("Pet pack not found");
      if (String(name ?? "").trim()) p.name = String(name).trim();
      emitState();
      return snapshot();
    },

    deletePet: ({ id }) => {
      state.petPacks = state.petPacks.filter((x) => x.id !== id);
      state.settings.pets.items = state.settings.pets.items.filter((i) => i.packId !== id);
      emitState();
      return snapshot();
    },

    addPet: ({ packId }) => {
      if (state.settings.pets.items.length >= 8) throw new Error("At most 8 pets can be out at once.");
      state.settings.pets.items.push({ id: `p${randomId()}`, packId });
      emitState();
      return snapshot();
    },

    callPet: ({ id }) => {
      if (!state.settings.pets.items.some((i) => i.id === id)) throw new Error("Pet not found");
      return snapshot();
    },

    removePet: ({ id }) => {
      state.settings.pets.items = state.settings.pets.items.filter((i) => i.id !== id);
      emitState();
      return snapshot();
    },

    importMedia: async () => {
      await sleep(500);   // "file dialog"
      const picks = [importNames[importCounter++ % importNames.length], importNames[importCounter++ % importNames.length]];
      const steps = 16;
      const created = Promise.all(picks.map(([n, p]) => addFakeWallpaper(n, p)));
      await progress("import", steps, (i) => {
        const f = picks[Math.min(picks.length - 1, Math.floor((i / steps) * picks.length))];
        return i === steps ? "Done" : `Copying ${f[0].toLowerCase().replace(/ /g, "-")}.mp4`;
      });
      const imported = await created;
      emitState();
      return { imported: clone(imported) };
    },

    renameWallpaper: ({ id, name }) => {
      const w = findWp(id);
      if (!w) throw new Error("Wallpaper not found");
      if (w.builtin) throw new Error("Built-in wallpapers can't be renamed");
      if (!String(name ?? "").trim()) throw new Error("Name can't be empty");
      w.name = String(name).trim();
      emitState();
      return snapshot();
    },

    deleteWallpaper: ({ id }) => {
      const w = findWp(id);
      if (!w) throw new Error("Wallpaper not found");
      if (w.builtin) throw new Error("Built-in wallpapers can't be deleted");
      state.wallpapers = state.wallpapers.filter((x) => x.id !== id);
      for (const a of Object.values(state.settings.assignments)) {
        if (a.wallpaperId === id) a.wallpaperId = null;
        if (a.darkWallpaperId === id) delete a.darkWallpaperId;
      }
      if (state.settings.screensaver.darkWallpaperId === id) delete state.settings.screensaver.darkWallpaperId;
      if (state.settings.screensaver.wallpaperId === id) state.settings.screensaver.wallpaperId = null;
      delete state.settings.looks[id];
      emitState();
      return snapshot();
    },

    saveThumbnail: ({ id, dataUrl, width, height, durationSec }) => {
      const w = findWp(id);
      if (!w) throw new Error("Wallpaper not found");
      if (!String(dataUrl).startsWith("data:image/jpeg")) throw new Error("Expected a JPEG data URL");
      w.thumbnail = dataUrl;
      if (width) w.width = width;
      if (height) w.height = height;
      if (durationSec && Number.isFinite(durationSec)) w.durationSec = durationSec;
      emitState();
      return { ok: true };
    },

    setPaused: ({ paused }) => {
      state.settings.paused = !!paused;
      emitState();
      return snapshot();
    },

    openLibraryFolder: () => { console.info(`[mock] would open ${LIB}`); return { ok: true }; },

    getExportItems: () => [
      { id: "settings", kind: "settings", name: "Settings", sizeBytes: 3_284 },
      ...state.wallpapers.filter((w) => !w.builtin).map((w) => ({
        id: w.id, kind: "wallpaper", name: w.name, sizeBytes: w.sizeBytes, thumbnail: w.thumbnail ?? null,
      })),
    ],

    exportPack: async ({ ids, includeSettings }) => {
      if (!Array.isArray(ids)) throw new Error("ids must be an array");
      await sleep(400);   // "save dialog"
      const names = ids.map((id) => findWp(id)?.name ?? id);
      if (includeSettings) names.push("settings.json");
      const steps = Math.max(8, names.length * 6);
      await progress("export", steps, (i) => (i === steps ? "Done" : `Packing ${names[Math.min(names.length - 1, Math.floor((i / steps) * names.length))] ?? ""}`));
      return { path: "C:\\Users\\you\\Desktop\\Hanami-pack-2026-11-01.hanamipack" };
    },

    importPack: async () => {
      await sleep(400);   // "open dialog"
      const picks = [importNames[importCounter++ % importNames.length]];
      const steps = 12;
      const created = Promise.all(picks.map(([n, p]) => addFakeWallpaper(n, p)));
      await progress("importPack", steps, (i) => (i === steps ? "Done" : "Unpacking Hanami-pack.hanamipack"));
      await created;
      emitState();
      return { imported: picks.length };
    },

    getFonts: async () => {
      await sleep(80);
      return {
        bundled: [{ id: "", name: "Tilt Warp", src: "", sizeBytes: 0 }],
        system: ["Arial", "Bahnschrift", "Cascadia Code", "Comic Sans MS", "Consolas", "Georgia", "Impact", "Segoe Print", "Segoe Script", "Segoe UI", "Times New Roman", "Verdana"],
        imported: clone(importedFonts),
      };
    },

    importFont: async () => {
      await sleep(400);   // "file dialog"
      const names = ["Lobster", "Pacifico", "Bebas Neue"];
      const name = names[importedFonts.length % names.length];
      const f = { id: `${name}.ttf`, name, src: "", sizeBytes: 140_000 };
      if (!importedFonts.some((x) => x.id === f.id)) importedFonts.push(f);
      return { imported: clone(f) };
    },

    deleteFont: ({ id }) => {
      importedFonts = importedFonts.filter((f) => f.id !== id);
      for (const look of Object.values(state.settings.looks)) if (look.screensaver.clock.font === `library:${id}`) look.screensaver.clock.font = null;
      emitState();
      return snapshot();
    },

    previewScreensaver: () => {
      // Real host: shows it now, ending on input. Mock: "show" it for a few seconds.
      state.screensaver.active = true;
      emitState();
      clearTimeout(previewTimer);
      previewTimer = setTimeout(() => { state.screensaver.active = false; emitState(); }, 4000);
      return { ok: true };
    },

    uninstall: async () => {
      await sleep(300);   // the real host asks "keep or delete my data?" in a native dialog
      return { cancelled: true };
    },

    quit: () => { setTimeout(() => console.info("[mock] host would quit now"), 0); return undefined; },
  };

  async function receive(msg) {
    const { id, method, params } = msg ?? {};
    await sleep(25 + Math.random() * 60);
    try {
      if (failures.has(method)) { const m = failures.get(method); failures.delete(method); throw new Error(m); }
      const fn = methods[method];
      if (!fn) throw new Error(`Unknown method ${method}`);
      const result = await fn(params ?? {});
      post({ id, result: clone(result) });
    } catch (err) {
      post({ id, error: err?.message ?? String(err) });
    }
  }

  window.hanamiMock = {
    state,
    setPower(p) {
      Object.assign(state.power, p);
      if (state.power.onBattery) state.screensaver.active = false;   // unplugging ends it
      emitState();
    },
    /** Flips Windows' light/dark setting in the mock. */
    setWindowsTheme(t) { state.theme.windows = t === "dark" ? "dark" : "light"; emitState(); },
    setWindowsTimers(displayOff, sleep) { Object.assign(state.screensaver, { windowsDisplayOffMinutes: displayOff, windowsSleepMinutes: sleep }); emitState(); },
    setWindowState(key, s) { windowState[key] = s; emitState(); },
    failNext(method, message = "Simulated host error") { failures.set(method, message); },
  };
  console.info("[Hanami] running with the mock bridge (no WebView2 host). See window.hanamiMock.");

  return { receive };
}
