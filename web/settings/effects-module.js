// Where the particle engine comes from. The settings preview runs the *real* engine that
// the wallpaper page uses (web/shared/effects.js). If that module is ever missing during
// development, point this at a local stub with the same exports.
export const EFFECTS_MODULE = "../shared/effects.js";

let loaded;
/** → Promise<{ EffectsEngine, PRESETS, DEFAULT_EFFECT }> */
export function loadEffects() {
  loaded ??= import(EFFECTS_MODULE);
  return loaded;
}
