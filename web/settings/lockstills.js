// Client-side lock pictures: for every wallpaper (video, image, built-in scene) without a current one, draw a still at the
// primary monitor's pixel size and hand the JPEG to the host via saveLockStill. The host never photographs the screensaver;
// it only picks these files (docs/CONTRACTS.md, "Lock screen"). Videos use the same hidden <video> as thumbnails.

import { once, openVideo, seekVideo, thumbnailTime, withTimeout } from "./thumbs.js";
import { createScene } from "../wallpaper/scenes.js";   // relative, so the page also works from a plain static server

const JPEG_QUALITY = 0.92;
const attempted = new Set();   // wallpaper|fit|size already tried this session (success or failure), never loop
let queue = Promise.resolve();

/**
 * Queue a lock picture for every wallpaper that has none, or has one drawn with another fit. Same shape as ensureThumbnails:
 * onDone(id, ok) after each. `state` is the settings State (wallpapers and monitors).
 */
export function ensureLockStills(state, call, onDone) {
  const size = primarySize(state);
  for (const w of state.wallpapers) {
    const builtin = w.builtin || w.type === "builtin";
    if (!builtin && !w.src) continue;
    const fit = lockFit(w);
    if (w.lockStill && w.lockStill.fit === fit) continue;
    const key = `${w.id}|${fit}|${size.w}x${size.h}`;
    if (attempted.has(key)) continue;
    attempted.add(key);
    queue = queue.then(async () => {
      try {
        const shot = await makeLockStill(w, fit, size);
        await call("saveLockStill", { id: w.id, dataUrl: shot.dataUrl, fit, ...(shot.frameSec != null ? { frameSec: shot.frameSec } : {}) });
        onDone?.(w.id, true);
      } catch (err) {
        console.warn(`[lockstills] could not make a lock picture for ${w.id}`, err);
        onDone?.(w.id, false);
      }
    });
  }
}

/** The fit a wallpaper's lock picture is drawn with. WallpaperInfo has no fit yet: the host shows every wallpaper as cover. */
export const lockFit = (w) => w.fit ?? "cover";

/**
 * The wallpaper the lock picture comes from: the primary monitor's screensaver wallpaper for the theme in use (the same rule as
 * Settings.ScreensaverWallpaperFor on the host). Null when the screensaver shows no wallpaper.
 */
export function lockWallpaperId(state) {
  const cfg = state.settings.screensaver, split = !!state.settings.theme?.split, dark = state.theme?.active === "dark";
  if (cfg.wallpaperId != null) return split && dark && cfg.darkWallpaperId != null ? cfg.darkWallpaperId : cfg.wallpaperId;
  const key = (state.monitors.find((m) => m.isPrimary) ?? state.monitors[0])?.key;
  const a = state.settings.assignments;
  const own = !state.settings.sameOnAllMonitors && a[key] ? a[key] : a["*"];
  return split && dark && own.darkWallpaperId != null ? own.darkWallpaperId : own.wallpaperId ?? null;
}

export function primarySize(state) {
  const m = state.monitors?.find((x) => x.isPrimary) ?? state.monitors?.[0];
  if (m?.width > 0 && m?.height > 0) return { w: m.width, h: m.height };
  const dpr = window.devicePixelRatio || 1;
  return { w: Math.round(screen.width * dpr), h: Math.round(screen.height * dpr) };
}

/** Draws `source` (sw × sh) onto the canvas the way the page's object-fit would; the canvas is black first, so `contain` gets bars. */
export function drawFitted(ctx, source, sw, sh, fit, { w, h }) {
  if (fit === "fill") { ctx.drawImage(source, 0, 0, w, h); return; }
  const k = fit === "contain" ? Math.min(w / sw, h / sh) : Math.max(w / sw, h / sh);
  const dw = sw * k, dh = sh * k;
  ctx.drawImage(source, (w - dw) / 2, (h - dh) / 2, dw, dh);
}

/**
 * Draws a wallpaper's lock picture at `size` ({w, h} in pixels): {dataUrl, frameSec}. `frameSec` picks the video frame
 * (null = the default, the thumbnail time); left out, the wallpaper's saved frame is used.
 */
export async function makeLockStill(w, fit, size, frameSec = w.lockStill?.frameSec ?? null) {
  const out = document.createElement("canvas");
  out.width = size.w;
  out.height = size.h;
  const ctx = out.getContext("2d");
  ctx.imageSmoothingQuality = "high";
  ctx.fillStyle = "#000";
  ctx.fillRect(0, 0, size.w, size.h);

  if (w.builtin || w.type === "builtin") {
    drawScene(ctx, w.id.replace(/^builtin:/, ""), size);
  } else if (w.type === "video") {
    // The chosen frame if there is one; else the thumbnail time. Only a chosen frame is sent back, so "default" stays default.
    const { video: v, duration, close } = await openVideo(w.src);
    try {
      const t = frameSec ?? thumbnailTime(duration);
      await seekVideo(v, duration ? Math.min(t, duration) : t);
      drawFitted(ctx, v, v.videoWidth, v.videoHeight, fit, size);
    } finally {
      close();
    }
  } else {
    const img = new Image();
    img.crossOrigin = "anonymous";
    img.decoding = "async";
    const loaded = once(img, "load");
    img.src = w.src;
    await withTimeout(loaded, "image load");
    drawFitted(ctx, img, img.naturalWidth, img.naturalHeight, fit, size);
  }
  return { dataUrl: out.toDataURL("image/jpeg", JPEG_QUALITY), frameSec };   // throws if the canvas is tainted (library.hanami without CORS)
}

/** A scene's first frame (its layers are seeded, so it is repeatable). The scene sizes itself from its canvas, so give it an off-page box. */
function drawScene(ctx, name, { w, h }) {
  const dpr = window.devicePixelRatio || 1;
  const c = document.createElement("canvas");
  c.style.cssText = `position:fixed;left:-99999px;top:0;width:${w / dpr}px;height:${h / dpr}px;pointer-events:none`;
  document.body.append(c);
  let scene;
  try {
    scene = createScene(name, c);
    ctx.drawImage(c, 0, 0, w, h);
  } finally {
    scene?.destroy();
    c.remove();
  }
}
