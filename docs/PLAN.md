# Implementation plan — round 3

How to use this file (for a fresh chat, e.g. Sonnet):
1. Read `CLAUDE.md`, then this file, then `docs/CONTRACTS.md`. Read `docs/TESTING.md` before testing.
2. Do **one phase per chat**, in order, unless the user says otherwise. Each phase ends with a
   working build, its checks done, boxes ticked here, and one commit.
3. Don't redesign beyond what a phase says; if something in the plan turns out wrong, note it under
   the phase's "Notes" and pick the smallest fix that keeps the intent.

Decisions already made with the user (don't re-ask):
- Each effect has **its own settings** (density, speed, size, opacity, wind, mouse interaction,
  strength, radius, click burst). Desktop and screensaver each just **pick an effect**; the effect
  brings its settings. A gear button on every effect tile opens that effect's settings.
- New effect: **flame embers**.
- Settings navigation: **Wallpaper · Screensaver · Power · General · Export / Import**. The Effects
  tab goes away (its controls live in the per-effect settings drawer). Wallpaper and Screensaver
  tabs choose *what shows*; Power holds *when things run*, including all screensaver timing.
- Screensaver clock: higher (about a third of the way down), abstract display font **Tilt Warp**
  (SIL OFL, bundled), no new settings. Date stays in the clean system font.
- Screensaver ends only on key press or click (already done). Sign-in off by default (done).

---

## Phase 1 — Bug: a playing video wallpaper keeps the screen on

**Symptom (user):** on the desktop, plugged in, screensaver set to 1 min never starts.
**Root cause (confirmed):** Chromium/WebView2 takes a Windows "display required" request while a
`<video>` plays, even muted. `tools\dev\power-state.ps1` shows `0x2` while the desktop video plays.
So `ScreensaverPolicy` sees "another app is keeping the screen on" and waits forever — and Windows
itself never turns the screen off on the desktop (on battery too, if On battery = Full).

**Fix: freeze the desktop video shortly before any idle deadline**, so our own request disappears
and the remaining requests (if any) really are other apps'.

- [x] 1.1 `Native/Win32.Power.cs`: add `OnBatteryTimeouts()` (same as `PluggedInTimeouts()` but
      `PowerReadDCValueIndex`), or generalise to `DisplayTimeouts(bool onBattery)` returning
      `(displayOff, sleep)` minutes.
- [x] 1.2 `Core/IdleFreezePolicy.cs` (pure): `TimeSpan? FreezeAfter(ScreensaverSettings s, bool onBattery,
      double? displayOffMinutes)` = the earliest of (screensaver start, if enabled and plugged in) and
      (Windows display-off timeout for the current power source), minus a 5 s lead; null = never.
      Unit tests: AC with screensaver 1 min → 55 s; battery → Windows DC timeout − 5 s; disabled
      screensaver + no Windows timeout → null; never negative.
- [x] 1.3 `ScreensaverManager`: it already tracks idle every second. Expose `bool IdleFrozen`
      (true once idle ≥ FreezeAfter, false on the next real input) and raise `Changed` when it
      flips. Before starting the screensaver, require the freeze to have been in effect ≥ 2 s, then
      check `AnyDisplayRequest()` (measure how long Chromium takes to drop its request after pause;
      adjust the 2 s if needed and note it).
- [x] 1.4 `ModeResolver.Resolve(..., bool idleFrozen)`: when frozen, cap at `Mode.Reduced` with
      reason "Idle" (video and scenes freeze, particles keep moving — canvas animation takes no
      power request). Not hidden. Wire it through `WallpaperManager` like `ScreensaverShowing`.
      Unit test.
- [x] 1.4b **Pause, never hide, behind windows** (user request): Win+D briefly shows the stock
      wallpaper because a maximized/fullscreen-covered wallpaper is hidden + `TrySuspend`ed, and
      resuming takes a moment. Fix: in `ModeResolver.Resolve` drop the `covered` hiding, so
      `Hidden` is true only for the screensaver case (and the "Paused" comment above it goes);
      in `WallpaperView.SetModeAsync` the hidden branch then only serves the screensaver. A paused
      wallpaper stays visible and frozen (video holds its frame, no display request), so showing
      the desktop is instant. Update the `ModeResolverTests` that expect `Hidden` when covered,
      the "Hidden" text in CONTRACTS/README if any. Cost: the page stays resident (more RAM than
      suspended) — acceptable. Verify with the user: Win+D with a maximized window → no flash.
- [x] 1.5 After the screensaver ends for a non-input reason (stop-after, unplug) the freeze must
      stay until input, so Windows' own timers can turn the screen off.
- [x] 1.6 Verify (see TESTING.md §5): `devroot` settings `power.whenMaximized="full"`, a video
      wallpaper, `screensaver.idleMinutes=1`. **Ask the user to keep hands off for ~90 s.** Expect:
      `power-state.ps1` `0x2` while playing → log "Reduced (Idle)" at ~55 s → `0x0` → "Screensaver
      shown" at ~60 s → `input.ps1 -Key` ends it → back to Full on the next input. Restore settings.
- [x] 1.7 README (screensaver/power text if needed), CONTRACTS (mode reasons), commit.

Notes:
- 1.1 became `DisplayTimeouts(bool onBattery)`; the only other caller (settings state) passes `false`.
- Verified live (video wallpaper, `whenMaximized=full`, idle 1 min): `0x2` while playing → log
  "Reduced (Idle)" → `0x0` → "Screensaver shown" about 5 s later (the freeze lead, which also covers
  the 2 s settle) → `0x3` during the screensaver → a key tap ended it → back to Full/`0x2`.
  The 2 s settle was enough; Chromium dropped its request well within it.
- 1.4b is implemented and unit-tested but the Win+D no-flash check is the user's to do (CLAUDE.md
  forbids Win+D for Claude).

## Phase 2 — Per-effect settings, flame embers, screensaver effect (data + host + engine)

Contract first (`docs/CONTRACTS.md`), then code:

- [ ] 2.1 Contract changes:
  - `type EffectTuning = Omit<EffectConfig, "preset">`.
  - `Settings.effects: Record<PresetId, Partial<EffectTuning>>` — the user's overrides per effect;
    missing keys fall back to that preset's defaults (`PRESET_DEFAULTS` in effects.js).
  - `Settings.assignments[key] = { wallpaperId, effectPreset: PresetId }` (replaces the `effect` object).
  - `Settings.screensaver.effectPreset: PresetId | null` — null = same as each monitor's desktop.
  - Host → page `config.effect` stays a full `EffectConfig` shape but may be partial:
    `{preset, ...settings.effects[preset]}`; the page fills gaps from `PRESET_DEFAULTS[preset]`.
  - Bridge: `setAssignment({monitorKey, wallpaperId?, effectPreset?})`;
    new `setEffectTuning({preset, tuning: Partial<EffectTuning> | null})` → `State` (null = reset).
  - Presets list gains `"embers"`.
- [ ] 2.2 `web/shared/effects.js`:
  - Export `PRESET_DEFAULTS` (per preset, sensible: fireflies → interaction attract, lower density;
    rain → wind; sakura/snow/leaves → repel; embers → wind). Keep `DEFAULT_EFFECT` exported.
  - `setConfig`: on a **preset change**, start from the new preset's defaults merged with the given
    partial — never carry the previous preset's tuning over. Same preset → merge as today.
  - New preset **embers** ("Flame embers"): glowing particles rising from the bottom third with
    slight turbulent sway; colour cools over life white-yellow → orange → deep red while shrinking
    and fading; per-particle flicker; occasional fast thin spark streaks; additive blending
    (`lighter`) with pre-rendered glow sprites in 3–4 temperature tints (no per-particle
    shadowBlur). Interaction wind = cursor gusts fan them; click burst = upward spark shower.
    Keep the existing perf budget (see the engine's header comment).
  - Check in the browser dev page (`?dev`, TESTING.md §7): all presets, preset switching resets
    tuning, embers looks good over dark and bright scenes, no console errors.
- [ ] 2.3 Host (`Core/Settings.cs` etc.):
  - `Assignment { WallpaperId, EffectPreset = "sakura" }`; `AppSettings.Effects:
    Dictionary<string, JsonObject>`; `ScreensaverSettings.EffectPreset: string?`.
  - Remove `Effects.Default()` from C# — defaults live only in effects.js.
  - Migration in `SettingsStore.Normalize`: an old assignment `effect` object → `effectPreset =
    effect.preset`, and if `effects[preset]` is unset copy its tuning keys there. (Read the legacy
    property via a nullable `[JsonPropertyName("effect")] JsonObject? LegacyEffect` that is cleared
    after migration so it is never written back.)
  - `ComposeEffect(preset)` → `{preset} + clone(effects[preset])`; used by `WallpaperManager`
    (desktop) and `ScreensaverManager` (`screensaver.effectPreset ?? desktop preset`).
  - Bridge: update `setAssignment`, add `setEffectTuning`.
  - Tests: migration from an old-shape JSON fixture; compose; screensaver override null/non-null;
    tuning reset; pack round-trip keeps `effects`.
- [ ] 2.4 Keep the current settings UI working enough not to crash (minimal adapt in
      `web/settings/` + `mock-bridge.js` to the new shape); the real UI rework is phase 3.
- [ ] 2.5 Run in dev mode: desktop shows its effect, preview screensaver shows a *different*
      effect when set via `cdp.mjs` + `updateSettings({screensaver:{effectPreset:"embers"}})`. Commit.

Notes:

## Phase 3 — Settings UI restructure and consistency pass

All in `web/settings/` (+ `mock-bridge.js`). Keep the visual language (soft sakura/twilight
accents, light + dark).

- [ ] 3.1 Shared components in `web/settings/components/`: `pageHeader(title, subtitle, action?)`,
      `card(title, meta?)`, `row(icon, label, hint, control)`, `segmented(options, value, onChange)`,
      `wallpaperPicker({selectedId, allowSameAsDesktop})`, `effectPicker({selectedPreset,
      allowSameAsDesktop, onTune})`, `tuningDrawer(preset)`, `statusPill()`. Refactor existing tabs
      to use them; delete duplicated markup/CSS.
- [ ] 3.2 Navigation: Wallpaper · Screensaver · Power · General · Export / Import. Remove the
      Effects tab (keep its slider code for the drawer).
- [ ] 3.3 **Wallpaper** tab: header (action: Import video / image) · monitor scope (All monitors /
      each monitor) · card "Background" (wallpaperPicker) · card "Effect" (effectPicker).
- [ ] 3.4 **Screensaver** tab: header (action: Preview) · status line · card "Background"
      (wallpaperPicker with *Same as desktop* first) · card "Effect" (effectPicker with *Same as
      desktop* first) · small link row "Timing and power settings → Power".
- [ ] 3.5 **Effect tiles + gear:** every effect tile (both pickers) gets a small round gear button
      at its top-right (always visible on the selected tile, on hover/focus otherwise; keyboard
      reachable; `aria-label="<Effect> settings"`). It opens `tuningDrawer(preset)`: a right-side
      drawer (bottom sheet under 900 px) titled "<Effect> settings", with the live preview canvas
      (real `EffectsEngine` over the current wallpaper's thumbnail), the sliders, interaction
      segmented control + strength/radius, click burst toggle, "Reset to defaults", and the note
      "Used wherever <Effect> is selected." Changes → `setEffectTuning` debounced 150 ms; slider
      defaults come from `PRESET_DEFAULTS`.
- [ ] 3.6 **Power** tab: card "Desktop wallpaper" (the six condition rows with Full / Reduced /
      Paused; Reduced described as "Video holds its frame; effects keep moving at up to 30 FPS") ·
      card "Screensaver" (on/off, Start after presets + custom, Stop after, Require sign-in, Windows
      timers info/warning, battery + lid footnote — moved from the Screensaver tab) · card "Frame
      rate".
- [ ] 3.7 Sidebar status pill shows both: e.g. "Running · screensaver in 1 min", "Paused —
      maximized window", "Off on battery".
- [ ] 3.8 `mock-bridge.js`: new shapes and `setEffectTuning`; embers in presets.
- [ ] 3.9 Check every tab at 1000×680 and 820×560, light and dark, no console errors (mock in the
      browser), then in the real app (`capture-window.ps1` screenshots, spot-check bridge calls with
      `cdp.mjs`). Commit.

Notes:

## Phase 4 — Screensaver clock: position and font

- [ ] 4.1 Fetch the font (OFL) into `web/wallpaper/fonts/`:
      `curl -L -o web/wallpaper/fonts/TiltWarp.ttf "https://github.com/google/fonts/raw/main/ofl/tiltwarp/TiltWarp%5BXROT,YROT%5D.ttf"`
      and `curl -L -o web/wallpaper/fonts/OFL.txt https://github.com/google/fonts/raw/main/ofl/tiltwarp/OFL.txt`.
      Check the TTF starts with bytes `00 01 00 00` and is > 20 KB; if the URL 404s, find the path at
      https://github.com/google/fonts/tree/main/ofl/tiltwarp. Mention the font + licence in README.
- [ ] 4.2 `web/wallpaper/style.css`: `@font-face { font-family: "Hanami Clock"; src:
      url("fonts/TiltWarp.ttf") format("truetype"); font-display: block; }`; use it for
      `#clock text.glyphs` (keep Segoe UI for `.date`). Try `font-variation-settings: "XROT" …,
      "YROT" …` for a slightly tilted dimensional look; pick values by eye.
- [ ] 4.3 `clock.js`: before the first `layout()`, `await document.fonts.load('100px "Hanami Clock"')`
      so the clip path and glyph metrics use the real font.
- [ ] 4.4 Move it up: `#clock { padding-bottom: ~34vh }` so the time's centre sits about a third of
      the way down; keep the date right under it.
- [ ] 4.5 Re-tune `#glass-edge` offsets if the rim/shade look off with the new glyph shapes.
- [ ] 4.6 Real-screen check: preview via `cdp.mjs`, `input.ps1 -Crop`, look at it; show the user the
      crop. If they dislike the font, alternatives: Rubik Moonrocks, Unbounded (same procedure). Commit.

Notes:

## Phase 5 — Housekeeping and ship

- [ ] 5.1 Unit tests currently write a "settings.json unreadable" warning into the user's real log.
      Let tests redirect `Log` (e.g. internal settable `Log.FilePath` set from a test module
      initializer to a temp file).
- [ ] 5.2 README: per-effect settings, embers, new tab layout, clock font credit.
- [ ] 5.3 `tools\build.ps1` (tests + `dist\Hanami.exe`), relaunch the dev copy normally
      (`--dev --startup`, no debug port), commit, and tell the user `dist\Hanami.exe` is updated.

Notes:
