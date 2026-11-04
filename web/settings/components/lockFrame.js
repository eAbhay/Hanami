// Frame picker for the lock screen picture (Screensaver tab → Lock screen). While "match the lock screen" is on it shows the picture of
// the primary monitor's screensaver wallpaper for the theme in use: a video gets a live preview and a slider over its duration,
// an image or built-in scene just its picture. Dragging only seeks the preview; "Use this frame" draws the full-resolution still
// and saves it with saveLockStill. The preview <video> is unloaded whenever the tab or the card goes away, so a 4K decode doesn't
// keep running in the background.
import { h, slider } from "../util.js";
import { lockFit, lockWallpaperId, makeLockStill, primarySize } from "../lockstills.js";
import { thumbnailTime } from "../thumbs.js";

const STEP = 0.01;
const THUMB_WIDTH = 640;   // a built-in scene's preview is drawn small; only the saved still is full size
const round = (v) => Math.round(v * 100) / 100;

export function createLockFrame(ctx) {
  const video = document.createElement("video");
  video.muted = true;
  video.playsInline = true;
  video.preload = "auto";
  video.disablePictureInPicture = true;
  const img = h("img", { alt: "", draggable: false });
  const preview = h("div.lock-preview", video, img);
  const range = slider({ label: "Frame", min: 0, max: 1, step: STEP, value: 0, format: (v) => `${v.toFixed(2)} s`, onInput: (v) => { seek(v); const w = current(); if (w) setButtons(w); } });
  const useBtn = h("button.btn.small", { type: "button", onclick: () => save(Number(range.input.value)) }, h("span", "Use this frame"));
  const resetBtn = h("button.btn.subtle.small", { type: "button", onclick: reset }, h("span", "Reset"));
  const note = h("p.field-hint");
  const buttons = h("div.slot-switch", useBtn, resetBtn);
  const el = h("div.lock-frame", { hidden: true }, preview, range, buttons, note);

  let active = false;          // the Screensaver tab is showing
  let loaded = null;           // {id, src, duration} of the video in the preview
  let pictureKey = "";         // what the <img> shows
  let busy = false;
  let seeking = false, pending = null, seekTimer = 0;

  /** Seeks the preview; while a seek is running only the newest request is kept, so dragging stays smooth on 4K. */
  function seek(t) {
    if (!loaded || video.readyState < 1) return;   // before the metadata a seek only sets the start position; loadedmetadata places again
    if (seeking) { pending = t; return; }
    if (Math.abs(video.currentTime - t) < 0.001) return;
    seeking = true;
    clearTimeout(seekTimer);
    seekTimer = setTimeout(endSeek, 1500);   // a seek that never reports back must not freeze the slider
    video.currentTime = t;
  }
  function endSeek() {
    clearTimeout(seekTimer);
    seeking = false;
    if (pending != null) { const t = pending; pending = null; seek(t); }
  }
  video.addEventListener("seeked", endSeek);

  const defaultTime = () => thumbnailTime(loaded?.duration ?? 0);
  const dirty = (w) => !!loaded && Math.abs(Number(range.input.value) - (w.lockStill?.frameSec ?? defaultTime())) > 0.005;

  function setButtons(w) {
    useBtn.disabled = busy || !dirty(w);
    resetBtn.disabled = busy || (w.lockStill?.frameSec == null && Math.abs(Number(range.input.value) - defaultTime()) <= 0.005);
  }

  /** Puts the slider (and the preview) at `t`. */
  function place(t) {
    range.set(t);
    range.input.dispatchEvent(new Event("input"));   // repaints the track and readout, and seeks
  }

  async function save(frameSec) {
    const w = current();
    if (!w || busy) return;
    busy = true;
    setButtons(w);
    try {
      const fit = lockFit(w), size = primarySize(ctx.state);
      const t = frameSec == null ? null : round(frameSec);
      const shot = await makeLockStill(w, fit, size, t);
      await ctx.call("saveLockStill", { id: w.id, dataUrl: shot.dataUrl, fit, ...(t != null ? { frameSec: t } : {}) });
      await ctx.mutate("getState");
      ctx.toast("Lock screen picture updated", "success");
    } catch (err) {
      ctx.toastError(err);
    } finally {
      busy = false;
      const now = current();
      if (now) setButtons(now);
    }
  }

  function reset() {
    place(defaultTime());
    return save(null);
  }

  function current() {
    const s = ctx.state;
    return s ? ctx.wallpaper(lockWallpaperId(s)) : null;
  }

  function unloadVideo() {
    clearTimeout(seekTimer);
    seeking = false;
    pending = null;
    if (!loaded && !video.getAttribute("src")) return;
    video.pause();
    video.removeAttribute("src");
    video.load();   // releases the decoder
    loaded = null;
  }

  function showPicture(src) {
    if (pictureKey !== src) { pictureKey = src; img.src = src; }
    img.hidden = false;
    video.hidden = true;
  }

  async function builtinPicture(w, size) {
    const small = { w: THUMB_WIDTH, h: Math.round((THUMB_WIDTH * size.h) / size.w) };
    return (await makeLockStill(w, "cover", small)).dataUrl;
  }

  function loadVideo(w) {
    video.hidden = false;
    img.hidden = true;
    if (loaded?.id === w.id) return;
    unloadVideo();
    loaded = { id: w.id, src: w.src, duration: w.durationSec ?? null };
    video.style.objectFit = lockFit(w);
    video.src = w.src;
    video.addEventListener("loadedmetadata", () => {
      if (!loaded || loaded.id !== w.id) return;
      if (Number.isFinite(video.duration) && video.duration > 0) loaded.duration = video.duration;
      const d = loaded.duration ?? 0;
      range.input.max = String(Math.max(STEP, round(d)));
      const now = current();
      place(Math.min(now?.lockStill?.frameSec ?? defaultTime(), d));
      if (now) setButtons(now);
    }, { once: true });
    range.input.max = String(Math.max(STEP, round(loaded.duration ?? 1)));
    place(Math.min(w.lockStill?.frameSec ?? thumbnailTime(loaded.duration ?? 0), loaded.duration ?? 1));
  }

  function update(s) {
    const w = s?.settings.screensaver.syncLockScreen ? current() : null;
    if (!active || !w) { el.hidden = true; unloadVideo(); pictureKey = ""; img.removeAttribute("src"); return; }
    el.hidden = false;
    const size = primarySize(s);
    preview.style.aspectRatio = `${size.w} / ${size.h}`;
    const isVideo = w.type === "video" && !!w.src;
    range.hidden = buttons.hidden = !isVideo;
    if (isVideo) {
      loadVideo(w);
      img.style.objectFit = "";
      setButtons(w);
      note.textContent = "The frame Windows shows on the lock screen. Drag to look, then Use this frame; Reset goes back to the default frame near the start.";
      return;
    }
    unloadVideo();
    img.style.objectFit = lockFit(w);
    note.textContent = w.type === "builtin" ? "The lock screen shows a still of this scene." : "The lock screen shows this picture.";
    if (w.type === "builtin") {
      const key = `builtin:${w.id}`;
      if (pictureKey !== key) {
        pictureKey = key;
        builtinPicture(w, size).then((url) => { if (pictureKey === key) { img.src = url; img.hidden = false; video.hidden = true; } }).catch(() => {});
      }
    } else if (w.src) showPicture(w.src);
  }

  return {
    el,
    update,
    /** The Screensaver tab became visible or hidden; hidden unloads the video. */
    setActive(on) { active = on; update(ctx.state); },
  };
}
