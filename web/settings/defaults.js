// Contract defaults (docs/CONTRACTS.md → EffectConfig), used only as a fallback when the host
// sends an assignment without an effect or the effects engine module can't be loaded.
export const FALLBACK_EFFECT = {
  preset: "sakura", density: 1, speed: 1, size: 1, opacity: 1, flicker: 0, wind: 0,
  interaction: "repel", interactionStrength: 1, interactionRadius: 160, clickBurst: false,
};

// Contract default for a look's screensaver clock (docs/CONTRACTS.md → ClockSettings).
export const DEFAULT_CLOCK = { x: 0.5, y: 0.31, size: 1, font: null, dateFont: "system", showDate: true };
