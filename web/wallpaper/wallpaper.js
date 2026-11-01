// Hanami wallpaper page — one instance per monitor, hosted in WebView2 behind the desktop icons.
// Protocol: docs/CONTRACTS.md ("Host → wallpaper page" / "Page → host").
//
// Layers: #bg holds background layers (video / image / built-in scene canvas / plain dark),
// newest on top; a new background fades in over the old one (600 ms) and the old one is then
// destroyed. #fx is the particle canvas on top of everything.
//
// Standalone dev mode (no window.chrome.webview): real mouse events drive pointer/click, a
// built-in config is applied, and a small dev panel (top-left) switches everything.

import { EffectsEngine, DEFAULT_EFFECT, PRESET_DEFAULTS, PRESETS } from "../shared/effects.js";
import { createScene, SCENES } from "./scenes.js";
import { mountClock } from "./clock.js";

const host = window.chrome?.webview ?? null;
let clock = null;   // screensaver pages only
// The idle screensaver loads this same page in a topmost window (…&screensaver=1): no cursor,
// a glass clock, and it asks the host to end only on a key press or mouse click — moving the
// mouse is ignored.
const isScreensaver = new URLSearchParams(location.search).has("screensaver");
if (isScreensaver) {
  document.documentElement.classList.add("screensaver");
  clock = mountClock(document.body, { onFontError: (e) => post({ type: "fontError", ...e }) });
  const dismiss = (e, input) => {
    e.preventDefault();
    e.stopPropagation();
    post({ type: "dismiss", input });
  };
  addEventListener("keydown", (e) => dismiss(e, "key"), true);
  addEventListener("pointerdown", (e) => dismiss(e, "click"), true);
  addEventListener("contextmenu", (e) => e.preventDefault(), true);
}
const bgRoot = document.getElementById("bg");
const fxCanvas = document.getElementById("fx");

const FADE_MS = 600;
const MODES = ["full", "reduced", "paused"];
const FITS = ["cover", "contain", "fill"];

const engine = new EffectsEngine(fxCanvas, DEFAULT_EFFECT);
let mode = "paused"; // nothing animates until the first config arrives
let fpsCap = 60;
let current = null;  // the visible background layer
let pending = null;  // a layer that is loading and will crossfade in
let devHooks = null; // dev-mode UI callbacks

function post(msg) {
  if (host) host.postMessage(msg);
  else console.info("[hanami → host]", msg);
  devHooks?.onPost(msg);
}

// ───────────────────────────── backgrounds ─────────────────────────────

function normalizeRef(ref) {
  if (!ref || typeof ref !== "object") return null;
  const type = ref.type === "video" || ref.type === "image" || ref.type === "builtin" ? ref.type : null;
  const id = String(ref.id ?? "");
  const out = {
    id,
    name: String(ref.name ?? ""),
    type,
    fit: FITS.includes(ref.fit) ? ref.fit : "cover",
  };
  if (type === "builtin") out.scene = ref.scene || (id.startsWith("builtin:") ? id.slice(8) : "");
  else out.src = typeof ref.src === "string" ? ref.src : "";
  return out;
}

function keyOf(ref) {
  if (!ref) return "none";
  return `${ref.type}|${ref.type === "builtin" ? ref.scene : ref.src}`;
}

function mediaErrorText(err) {
  if (!err) return "Media failed to load.";
  const names = { 1: "aborted", 2: "network error", 3: "decode error", 4: "format not supported or file not found" };
  return `Video ${names[err.code] || "error"}${err.message ? `: ${err.message}` : ""}`;
}

function reportError(layer, message) {
  if (layer.reported) return;
  layer.reported = true;
  post({ type: "mediaError", wallpaperId: layer.ref?.id ?? "", message });
}

function createLayer(ref, key = keyOf(ref)) {
  const el = document.createElement("div");
  el.className = "layer";
  const L = { ref, key, el, video: null, img: null, scene: null, reported: false, ready: null };
  bgRoot.appendChild(el);

  L.ready = new Promise((resolve) => {
    let settled = false;
    let timer = 0;
    const done = (ok) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve(ok);
    };
    const fail = (message) => {
      reportError(L, message);
      done(false);
    };

    if (!ref) return done(true);

    if (ref.type === "video") {
      if (!ref.src) return fail("No video source.");
      const v = document.createElement("video");
      v.muted = true;
      v.defaultMuted = true;
      v.loop = true;
      v.playsInline = true;
      v.autoplay = mode === "full";
      v.preload = "auto";
      v.disablePictureInPicture = true;
      v.setAttribute("muted", "");
      v.setAttribute("playsinline", "");
      v.style.objectFit = ref.fit;
      v.addEventListener("loadeddata", () => done(true), { once: true });
      v.addEventListener("error", () => fail(mediaErrorText(v.error)));
      timer = setTimeout(() => done(true), 10000); // slow source: show whatever we have
      v.src = ref.src;
      el.appendChild(v);
      L.video = v;
    } else if (ref.type === "image") {
      if (!ref.src) return fail("No image source.");
      const img = new Image();
      img.decoding = "async";
      img.draggable = false;
      img.alt = "";
      img.style.objectFit = ref.fit;
      img.onload = () => img.decode().catch(() => {}).then(() => done(true));
      img.onerror = () => fail("Image failed to load or decode.");
      img.src = ref.src;
      el.appendChild(img);
      L.img = img;
    } else if (ref.type === "builtin") {
      if (!SCENES.includes(ref.scene)) return fail(`Unknown built-in scene "${ref.scene}".`);
      const c = document.createElement("canvas");
      el.appendChild(c);
      try {
        L.scene = createScene(ref.scene, c);
        done(true);
      } catch (err) {
        fail(`Scene failed: ${err?.message || err}`);
      }
    } else {
      fail(`Unsupported wallpaper type "${ref.type}".`);
    }
  });
  return L;
}

function destroyLayer(L) {
  if (!L) return;
  if (L.video) {
    L.video.pause();
    L.video.removeAttribute("src");
    L.video.load(); // releases the decoder
  }
  L.scene?.destroy();
  L.el.remove();
}

function applyFit(L, ref) {
  L.ref = ref;
  const el = L.video || L.img;
  if (el) el.style.objectFit = ref?.fit || "cover";
}

function applyModeToLayer(L) {
  if (!L) return;
  if (L.video) {
    if (mode === "full") L.video.play().catch(() => {});
    else L.video.pause();
  }
  if (L.scene) {
    // scenes count as the "video": frozen in reduced, stopped in paused
    if (mode === "full") L.scene.start();
    else L.scene.stop();
  }
}

function show(L) {
  const old = current;
  current = L;
  applyModeToLayer(L);
  void L.el.offsetWidth; // commit opacity:0 so the transition runs
  L.el.classList.add("visible");
  if (old) {
    if (old.video) old.video.pause();
    old.scene?.stop();
    setTimeout(() => destroyLayer(old), FADE_MS + 80);
  }
}

function setWallpaper(rawRef) {
  const ref = normalizeRef(rawRef);
  const key = keyOf(ref);
  if (current && current.key === key) {
    if (pending) destroyLayer(pending);
    pending = null;
    applyFit(current, ref);
    return;
  }
  if (pending && pending.key === key) {
    applyFit(pending, ref);
    return;
  }
  if (pending) destroyLayer(pending);
  const L = createLayer(ref, key);
  pending = L;
  L.ready.then((ok) => {
    if (pending !== L) return; // superseded while loading
    pending = null;
    if (ok) {
      show(L);
    } else {
      // Fall back to the plain dark background, but keep the failed key so an identical
      // config (e.g. an unrelated effect change) does not retry and re-report.
      destroyLayer(L);
      const dark = createLayer(null, key);
      dark.ref = ref;
      show(dark);
    }
  });
}

// ───────────────────────────── modes & messages ─────────────────────────────

function applyMode(m) {
  if (MODES.includes(m)) mode = m;
  applyModeToLayer(current);
  applyModeToLayer(pending);
  if (mode === "paused") {
    engine.stop();
  } else {
    engine.setFpsCap(mode === "reduced" ? Math.min(fpsCap, 30) : fpsCap);
    engine.start();
  }
  devHooks?.onMode(mode);
}

function handle(msg) {
  if (typeof msg === "string") {
    try { msg = JSON.parse(msg); } catch { return; }
  }
  if (!msg || typeof msg !== "object") return;
  switch (msg.type) {
    case "config": {
      const cap = Number(msg.fpsCap);
      if (Number.isFinite(cap) && cap > 0) fpsCap = Math.min(240, cap);
      // The host sends the preset plus the user's overrides only; defaults fill the gaps (this also
      // makes a reset take effect).
      if (msg.effect && typeof msg.effect === "object") engine.setConfig({ ...(PRESET_DEFAULTS[msg.effect.preset] ?? {}), ...msg.effect });
      if ("wallpaper" in msg) setWallpaper(msg.wallpaper);
      if (msg.clock && typeof msg.clock === "object") clock?.set(msg.clock);
      applyMode(MODES.includes(msg.mode) ? msg.mode : mode);
      break;
    }
    case "mode":
      applyMode(msg.mode);
      break;
    case "pointer":
      engine.setPointer(msg.x, msg.y, msg.inside !== false);
      break;
    case "click":
      // Right clicks open the desktop context menu; only left clicks burst.
      if (msg.button !== "right") engine.click(msg.x, msg.y);
      break;
  }
}

let resizeTimer = 0;
window.addEventListener("resize", () => {
  engine.resize();
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    current?.scene?.resize();
    pending?.scene?.resize();
  }, 150);
});

if (host) {
  host.addEventListener("message", (e) => handle(e.data));
  post({ type: "ready" });
} else {
  initDev();
}

// ───────────────────────────── dev mode ─────────────────────────────

function builtinRef(scene) {
  return { id: `builtin:${scene}`, name: scene[0].toUpperCase() + scene.slice(1), type: "builtin", scene, fit: "cover" };
}

function mediaRef(url, fit) {
  const isImage = /\.(jpe?g|png|webp|gif|avif|bmp)([?#]|$)/i.test(url);
  return { id: `dev:${url}`, name: url.split(/[\\/]/).pop() || url, type: isImage ? "image" : "video", src: url, fit };
}

function initDev() {
  const q = new URLSearchParams(location.search);
  const state = {
    background: SCENES.includes(q.get("scene")) ? q.get("scene") : q.get("scene") === "none" ? "none" : "twilight",
    url: q.get("video") || q.get("src") || "",
    fit: FITS.includes(q.get("fit")) ? q.get("fit") : "cover",
    effect: { ...DEFAULT_EFFECT, preset: "sakura" },
    fpsCap: Number(q.get("fps")) || 60,
    mode: MODES.includes(q.get("mode")) ? q.get("mode") : "full",
  };
  if (PRESETS.some((p) => p.id === q.get("preset"))) state.effect = { ...PRESET_DEFAULTS[q.get("preset")] };
  if (["none", "repel", "attract", "wind"].includes(q.get("interaction"))) state.effect.interaction = q.get("interaction");
  if (state.url) state.background = "url";

  const wallpaperFor = () =>
    state.background === "none" ? null : state.background === "url" ? mediaRef(state.url, state.fit) : { ...builtinRef(state.background), fit: state.fit };
  const send = () =>
    handle({ type: "config", wallpaper: wallpaperFor(), effect: { ...state.effect }, fpsCap: state.fpsCap, mode: state.mode });

  // real mouse → the same messages the host would send
  window.addEventListener("pointermove", (e) => handle({ type: "pointer", x: e.clientX, y: e.clientY, inside: true }), { passive: true });
  document.documentElement.addEventListener("mouseleave", () => handle({ type: "pointer", x: -1, y: -1, inside: false }));
  window.addEventListener("blur", () => handle({ type: "pointer", x: -1, y: -1, inside: false }));
  window.addEventListener("pointerdown", (e) => {
    if (e.target instanceof Element && e.target.closest("#dev")) return;
    handle({ type: "click", x: e.clientX, y: e.clientY, button: e.button === 2 ? "right" : "left" });
  });

  buildPanel(state, send);
  send();
  // dev convenience for console experiments
  window.hanami = { engine, handle, state, send, clock };
}

function buildPanel(state, send) {
  const panel = document.createElement("details");
  panel.id = "dev";
  panel.open = true;
  const summary = document.createElement("summary");
  summary.innerHTML = `<span>Hanami dev</span><span class="stats"></span>`;
  panel.appendChild(summary);
  const body = document.createElement("div");
  body.className = "body";
  panel.appendChild(body);
  const stats = summary.querySelector(".stats");

  const select = (label, options, value, onChange) => {
    const l = document.createElement("label");
    l.className = "wide";
    l.append(label);
    const s = document.createElement("select");
    for (const [v, t] of options) s.add(new Option(t, v, false, v === value));
    s.addEventListener("change", () => onChange(s.value));
    l.appendChild(s);
    body.appendChild(l);
    return s;
  };
  const syncs = [];   // refresh the controls after a preset switch resets the tuning
  const slider = (label, key, min, max, step) => {
    const l = document.createElement("label");
    l.append(label);
    const r = document.createElement("input");
    r.type = "range";
    Object.assign(r, { min, max, step, value: state.effect[key] });
    const o = document.createElement("output");
    o.textContent = (+state.effect[key]).toFixed(2);
    r.addEventListener("input", () => {
      state.effect[key] = +r.value;
      o.textContent = (+r.value).toFixed(2);
      send();
    });
    syncs.push(() => { r.value = state.effect[key]; o.textContent = (+state.effect[key]).toFixed(2); });
    l.append(r, o);
    body.appendChild(l);
  };

  const bgSelect = select(
    "Background",
    [...SCENES.map((s) => [s, `Scene: ${s}`]), ["none", "None (dark)"], ["url", "Video / image URL"]],
    state.background,
    (v) => {
      if (v === "url" && !state.url) return;
      state.background = v;
      send();
    }
  );

  const row = document.createElement("div");
  row.className = "row";
  const urlIn = document.createElement("input");
  urlIn.type = "text";
  urlIn.placeholder = "video or image URL";
  urlIn.value = state.url;
  const loadBtn = document.createElement("button");
  loadBtn.textContent = "Load";
  const load = () => {
    const u = urlIn.value.trim();
    if (!u) return;
    state.url = u;
    state.background = "url";
    if (![...bgSelect.options].some((o) => o.value === "url")) bgSelect.add(new Option("Video / image URL", "url"));
    bgSelect.value = "url";
    msg.textContent = "";
    send();
  };
  loadBtn.addEventListener("click", load);
  urlIn.addEventListener("keydown", (e) => e.key === "Enter" && load());
  row.append(urlIn, loadBtn);
  body.appendChild(row);

  select("Fit", FITS.map((f) => [f, f]), state.fit, (v) => { state.fit = v; send(); });
  select("Preset", PRESETS.map((p) => [p.id, p.name]), state.effect.preset, (v) => {
    state.effect = { ...PRESET_DEFAULTS[v] };
    interactionSel.value = state.effect.interaction;
    burstSel.value = String(state.effect.clickBurst);
    for (const f of syncs) f();
    send();
  });
  const interactionSel = select("Interaction", ["repel", "attract", "wind", "none"].map((v) => [v, v]), state.effect.interaction, (v) => { state.effect.interaction = v; send(); });
  const burstSel = select("Click burst", [["true", "on"], ["false", "off"]], String(state.effect.clickBurst), (v) => { state.effect.clickBurst = v === "true"; send(); });
  slider("Density", "density", 0.1, 3, 0.05);
  slider("Speed", "speed", 0.2, 3, 0.05);
  slider("Size", "size", 0.5, 2, 0.05);
  slider("Opacity", "opacity", 0, 2, 0.01);
  slider("Flicker", "flicker", 0, 1, 0.01);
  slider("Wind", "wind", -1, 1, 0.01);
  slider("Strength", "interactionStrength", 0, 2, 0.05);
  slider("Radius", "interactionRadius", 40, 400, 5);
  const modeSelect = select("Mode", MODES.map((m) => [m, m]), state.mode, (v) => { state.mode = v; send(); });
  select("FPS cap", [15, 30, 60, 120, 144].map((f) => [String(f), String(f)]), String(state.fpsCap), (v) => { state.fpsCap = +v; send(); });

  const msg = document.createElement("div");
  msg.className = "msg";
  body.appendChild(msg);

  document.body.appendChild(panel);

  devHooks = {
    onPost(m) {
      if (m.type === "mediaError") msg.textContent = `mediaError: ${m.message}`;
    },
    onMode(m) {
      modeSelect.value = m;
    },
  };
  setInterval(() => {
    stats.textContent = engine.running ? `${engine.stats.fps} fps · ${engine.stats.particles}` : "paused";
  }, 500);
}
