// Wallpaper thumbnail rendering shared by the Wallpapers grid and the Screensaver picker.
import { h, svg } from "./util.js";
import { thumbnailFailed } from "./thumbs.js";

export const TYPE_LABEL = { video: "Video", image: "Image", builtin: "Built-in" };
export const isBuiltin = (w) => !!(w.builtin || w.type === "builtin");

/** Thumbnail element for a WallpaperInfo: CSS scene for builtins, image, or a placeholder. */
export function wallpaperThumb(w) {
  if (isBuiltin(w)) {
    const scene = w.id.split(":")[1] ?? "twilight";
    return h(`div.thumb.scene.scene-${scene}`, h("span.scene-glow"));
  }
  if (w.thumbnail) {
    const img = h("img", { src: w.thumbnail, alt: "", loading: "lazy", decoding: "async", draggable: false });
    img.addEventListener("error", () => img.replaceWith(h("div.thumb-fallback", svg(w.type === "video" ? "play" : "image"))), { once: true });
    return h("div.thumb", img);
  }
  if (thumbnailFailed(w.id) || !w.src) return h("div.thumb", h("div.thumb-fallback", svg(w.type === "video" ? "play" : "image"), h("span", "No preview")));
  return h("div.thumb.loading", h("div.thumb-fallback", h("span.spinner"), h("span", "Making preview…")));
}
