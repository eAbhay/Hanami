// Light | Dark switch for the Background cards: which slot the picker below edits (only while
// light/dark wallpapers are on). The choice is shared by the Wallpaper and Screensaver tabs.
import { h, segmented } from "../util.js";

export function slotSwitch(ctx) {
  const seg = segmented([{ value: "light", label: "☀ Light" }, { value: "dark", label: "☾ Dark" }], "light",
    (v) => ctx.setSlot(v), { ariaLabel: "Wallpaper for light or dark mode" });
  const el = h("div.slot-switch", seg);
  el.update = () => {
    el.hidden = !ctx.split;
    seg.set(ctx.slot);
  };
  return el;
}
