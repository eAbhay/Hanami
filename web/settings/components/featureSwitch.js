// The on/off row at the top of the Wallpaper, Screensaver and Pets tabs. One component so the three match.
import { h, toggle } from "../util.js";
import { row } from "./layout.js";

/**
 * `settingsKey` is the settings block ("wallpaper", "screensaver", "pets") whose `enabled` it edits.
 * The returned element has `.set(enabled)`; call it from the tab's `update`.
 */
export function featureSwitch(ctx, { settingsKey, icon, title, sub }) {
  const sw = toggle(true, (v) => ctx.mutate("updateSettings", { [settingsKey]: { enabled: v } }).catch(() => sw.set(!v)), title);
  const el = h("div.card.pad.feature-switch", row(icon, title, sub, sw));
  el.set = (v) => sw.set(v ?? true);
  return el;
}
