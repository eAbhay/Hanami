// Client-side thumbnail generation for library wallpapers whose thumbnail is still null.
// Loads the media in a hidden <video>/<img>, grabs a frame (~1 s or 10% in, whichever is
// earlier), draws it at 480 px wide and hands the JPEG to the host via saveThumbnail.

const WIDTH = 480;
const TIMEOUT_MS = 15000;
const attempted = new Set();   // ids already tried this session (success or failure) — never loop
const failed = new Set();
let queue = Promise.resolve();

export const thumbnailFailed = (id) => failed.has(id);

/** Queue thumbnail generation for every wallpaper that needs one. onDone(id, ok) after each. */
export function ensureThumbnails(wallpapers, call, onDone) {
  for (const w of wallpapers) {
    if (w.builtin || w.type === "builtin" || w.thumbnail != null || !w.src || attempted.has(w.id)) continue;
    attempted.add(w.id);
    queue = queue.then(async () => {
      try {
        const shot = w.type === "video" ? await grabVideo(w.src) : await grabImage(w.src);
        await call("saveThumbnail", { id: w.id, ...shot });
        onDone?.(w.id, true);
      } catch (err) {
        failed.add(w.id);
        console.warn(`[thumbs] could not generate a thumbnail for ${w.id}`, err);
        onDone?.(w.id, false);
      }
    });
  }
}

function withTimeout(promise, what) {
  return Promise.race([promise, new Promise((_, rej) => setTimeout(() => rej(new Error(`${what} timed out`)), TIMEOUT_MS))]);
}

function draw(source, sw, sh) {
  const scale = WIDTH / sw;
  const c = document.createElement("canvas");
  c.width = WIDTH;
  c.height = Math.max(1, Math.round(sh * scale));
  const ctx = c.getContext("2d");
  ctx.imageSmoothingQuality = "high";
  ctx.drawImage(source, 0, 0, c.width, c.height);
  return c.toDataURL("image/jpeg", 0.8);   // throws if the canvas is tainted (missing CORS on library.hanami)
}

function once(target, ok, bad = "error") {
  return new Promise((resolve, reject) => {
    const onOk = () => { cleanup(); resolve(); };
    const onBad = () => { cleanup(); reject(target.error ?? new Error(`media ${bad}`)); };
    const cleanup = () => { target.removeEventListener(ok, onOk); target.removeEventListener(bad, onBad); };
    target.addEventListener(ok, onOk);
    target.addEventListener(bad, onBad);
  });
}

async function grabVideo(src) {
  const v = document.createElement("video");
  v.muted = true;
  v.playsInline = true;
  v.preload = "auto";
  v.crossOrigin = "anonymous";   // library.hanami is a different origin from app.hanami
  v.style.cssText = "position:fixed;left:-9999px;top:0;width:2px;height:2px;opacity:0;pointer-events:none";
  document.body.append(v);
  try {
    const loaded = once(v, "loadeddata");
    v.src = src;
    await withTimeout(loaded, "video load");
    let d = v.duration;
    if (!Number.isFinite(d)) {
      // Some files (e.g. MediaRecorder webm) report Infinity until you seek past the end.
      const changed = once(v, "durationchange").catch(() => {});
      v.currentTime = 1e7;
      await withTimeout(changed, "duration probe").catch(() => {});
      d = v.duration;
    }
    const target = Number.isFinite(d) && d > 0 ? Math.min(1, d * 0.1) : 0;
    const seeked = once(v, "seeked");
    v.currentTime = target;
    await withTimeout(seeked, "video seek");
    if (!v.videoWidth) throw new Error("video has no frames");
    return {
      dataUrl: draw(v, v.videoWidth, v.videoHeight),
      width: v.videoWidth, height: v.videoHeight,
      ...(Number.isFinite(d) && d > 0 ? { durationSec: Math.round(d * 100) / 100 } : {}),
    };
  } finally {
    v.removeAttribute("src");
    v.load();
    v.remove();
  }
}

async function grabImage(src) {
  const img = new Image();
  img.crossOrigin = "anonymous";
  img.decoding = "async";
  const loaded = once(img, "load");
  img.src = src;
  await withTimeout(loaded, "image load");
  return { dataUrl: draw(img, img.naturalWidth, img.naturalHeight), width: img.naturalWidth, height: img.naturalHeight };
}
