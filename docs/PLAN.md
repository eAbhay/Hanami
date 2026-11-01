# Implementation plan — round 4

How to use this file (for a fresh chat, e.g. Sonnet):
1. Read `CLAUDE.md`, then this file, then `docs/CONTRACTS.md`. Read `docs/TESTING.md` before testing.
2. Do **one phase per chat**, in order, unless the user says otherwise. Each phase ends with a
   working build, its checks done, boxes ticked here, and one commit.
3. Don't redesign beyond what a phase says; if something in the plan turns out wrong, note it under
   the phase's "Notes" and pick the smallest fix that keeps the intent.

Round 3 (idle freeze, per-effect settings, embers, settings restructure, Tilt Warp clock) is done;
see git history (`git show c2918b8:docs/PLAN.md`). Still open from it: the user's own Win+D
no-flash check.

Decisions already made with the user (don't re-ask):
- **Click burst is off by default** for every effect. Users who already switched it on keep it —
  but stored overrides currently hold *every* key (the old drawer/migration wrote whole objects), so
  a one-time migration drops `clickBurst` from stored overrides (see 1.2).
- Effects look too faint at 100% opacity. Cause (measured from the code, see Phase 2): the
  per-particle alphas and the flicker/pulse/life envelopes multiply the opacity slider down to an
  average visible alpha of roughly 0.2–0.4 for fireflies, sparkles and embers.
- Screensaver clock: the user sets its **position** and **font** (an installed Windows font, the
  bundled Tilt Warp, or a font file they import). Applies to the screensaver clock (the only clock).
- Fingerprint to end the screensaver: **dropped** by the user (not worth the effort).
- Taskbar transparency: not an app feature. On Windows 11 the taskbar is XAML inside Explorer and
  resets its own background, so making it transparent means injecting into `explorer.exe` (what
  TranslucentTB does) — too fragile for Hanami. The user uses TranslucentTB instead.

---

## Phase 1 — Click burst off by default

- [ ] 1.1 `web/shared/effects.js`: `DEFAULT_EFFECT.clickBurst = false` (all presets inherit it).
      `web/settings/defaults.js` `FALLBACK_EFFECT.clickBurst = false`; the drawer's
      `toggle(true, …)` initial value → `false`; dev panel follows `PRESET_DEFAULTS` already.
      `mock-bridge.js` if it seeds `clickBurst: true` anywhere.
- [ ] 1.2 Migration in `SettingsStore.Normalize`: add `AppSettings.SettingsVersion` (int, missing =
      1). When < 2: remove the `clickBurst` key from every `effects[preset]` override, then set 2.
      Write it back on the next save as usual. Unit tests: v1 file with `clickBurst:true` → key gone,
      version 2; v2 file with `clickBurst:true` → kept; pack import of an old settings file migrates too.
- [ ] 1.3 `docs/CONTRACTS.md` (`clickBurst` default false; `settingsVersion`), README line, drawer
      hint text unchanged. `tools/dev/perf.ps1` sends an explicit config — leave it.
- [ ] 1.4 Check: dev page click → no burst on a fresh preset; settings drawer toggle starts off and
      turning it on works on the real desktop. Commit.

## Phase 2 — Effect visibility

**Why they look faint (from the code):** `_render` draws `p.alpha × p.fade × opacity`, and then:
- fireflies: pulse `0.12 + 0.88·s³` → mean ≈ 0.40, × alpha 0.6–1 → **≈ 0.32**;
- sparkles: the same twinkle curve, and size shrinks to 45% at the trough → **≈ 0.3**, smaller too;
- embers: flicker `0.72 ± 0.28` × life envelope `min(1,7t)·(1−t)^1.15` (mean ≈ 0.45) × alpha
  (0.39–1) → **≈ 0.2**;
- snow: small (1.6–5.5 px) with alphas 0.45–0.95; rain alphas 0.22–0.55 (by design, but faint);
- sakura/leaves are mostly fine (alpha 0.85–1 for most), they only fade on bright scenes.
So "100% opacity" never means opaque. Fix in three parts, keeping the perf budget:

- [ ] 2.0 Measure first. Add a dev-only diagnostic to the engine (not in the contract, like
      `stats`): `stats.meanAlpha` = mean of the final `globalAlpha` actually drawn per particle,
      averaged over ~2 s. Record a before table for every preset at defaults in Notes.
- [ ] 2.1 New tuning key **`flicker`** (0–1, 1 = today's depth). Scales the depth of the fireflies
      pulse, the sparkles twinkle (alpha *and* size) and the embers flicker:
      `value = 1 − flicker·(1 − today's factor)`. Per-preset defaults in `PRESET_DEFAULTS`
      (start: fireflies 0.5, sparkles 0.5, embers 0.4; others 0 and unused). Add a capability flag
      on `PRESETS` entries (`flicker: true` for those three) so the drawer shows the slider only
      where it does something. Sanitize 0–1.
- [ ] 2.2 **Opacity above 100%.** Extend `opacity` to 0–2. Up to 1 it multiplies as today; above 1
      it lifts faint particles without clipping bright ones: `a' = 1 − (1 − a)^opacity` where
      `a = p.alpha × fade × envelope` (apply in `_render`/each `draw` where the final alpha is
      known — refactor so the multiplier is applied once). Slider 0–200%, hint "Above 100% makes
      faint particles stronger".
- [ ] 2.3 **Retune defaults** so each preset's `meanAlpha` at defaults is ≥ ~0.55 (rain ≥ 0.4):
      raise the low alpha ranges (snow mid layer, rain, embers `0.55 + 0.45z` term), soften the
      embers life envelope's tail (e.g. exponent 1.15 → 0.8), raise the fireflies pulse floor
      0.12 → 0.3. Don't change motion. Record the after table in Notes.
- [ ] 2.4 Light particles over bright video (snow, sakura, sparkles): bake a very soft dark halo
      (~10–15% alpha, a few px) into the sprites at build time so they separate from white skies.
      Sprite-only change, no runtime cost. Judge on the dev page with a bright image
      (`?src=<bright jpg>`) and a dark scene; if it looks dirty on dark scenes, drop it and note why.
- [ ] 2.5 Contract (`flicker`, opacity range, capability flag), drawer slider + mock bridge, host:
      nothing (overrides are opaque JSON) — but check `EffectSettingsTests` still pass and add one
      for an override containing `flicker` surviving a round-trip.
- [ ] 2.6 Real-screen check: `capture-desktop.ps1` before/after of embers and fireflies over the
      user's video wallpaper; show the user. Commit.

## Phase 3 — Screensaver clock: position, size, font

Contract first:

- [ ] 3.1 `docs/CONTRACTS.md`:
  - `Settings.screensaver.clock: { x: number; y: number; size: number; font: string | null;
    dateFont: "system" | "same"; showDate: boolean }`. `x, y` = the clock block's centre as a
    fraction of the monitor (defaults 0.5, 0.31 = today's spot), `size` 0.5–1.6 (default 1),
    `font` null = bundled Tilt Warp, `"system:<family>"` = an installed Windows font,
    `"library:<file>"` = an imported font in `Library/fonts/`. Same clock on every monitor.
  - Host → wallpaper page `config` gains `clock` (screensaver pages only) with `font` resolved to
    `{family: string, src?: string}` (src = `https://library.hanami/fonts/<file>`).
  - Bridge: `getFonts` → `{bundled: FontInfo[], system: string[], imported: FontInfo[]}`;
    `importFont` (file dialog: .ttf .otf .woff .woff2, copies into `Library/fonts/`) →
    `{imported: FontInfo}` | `{cancelled: true}`; `deleteFont {id}` → `State` (if it's the selected
    font, `clock.font` resets to null).
  - On-disk layout: `Library/fonts/<id>.<ext>`.
- [ ] 3.2 Host: `ClockSettings` with clamping in `Normalize` (x, y 0–1, size range, unknown font
      strings kept but resolved to Tilt Warp if missing). Font ids: sanitize file names (no path
      separators, no `..`, fixed allowed extensions), de-duplicate. System fonts from
      `System.Windows.Media.Fonts.SystemFontFamilies` (family names, sorted, cached). Packs: when
      settings are exported and `clock.font` is a library font, include that file; import restores
      it. Tests: clamping, id sanitizing (`..\x.ttf`, `a/b.otf`, `.exe`), pack round-trip with a
      font, delete-selected resets to null.
- [ ] 3.3 Page (`clock.js`, `style.css`): take `clock` from config. Position the block by its
      centre at `(x·100vw, y·100vh)`, clamped so it stays fully on screen. Scale with `size`
      (font-size and `.time-wrap` box). The `#glass-edge` filter offsets/blurs are in px tuned for
      size 1 — scale them by `size` (update the filter attributes in `layout()`). Font: system →
      family name; library → `new FontFace("Hanami User Clock", "url(src)")`, `document.fonts.add`,
      await load; on failure fall back to Tilt Warp and post `{type:"fontError", font, message}`
      (log it host-side). Keep the fade-in-after-font-load rule. `dateFont: "same"` uses the clock
      family for the date; `showDate` hides it.
- [ ] 3.4 Settings UI, Screensaver tab, new card **"Clock"**:
  - a small monitor-shaped placement preview (current screensaver wallpaper thumbnail, a
    clock-shaped handle showing the current time in the chosen font) — drag the handle to place it,
    snaps to centre lines within ~2%; under it 9 anchor buttons (corners, edges, centre with a
    margin) that set `x, y`; keyboard: arrow keys nudge 1%.
  - Size slider; Font select (groups: Bundled · Imported · Installed), "Import font…" button,
    remove (×) on imported fonts; "Use clock font for the date" toggle; "Show date" toggle.
  - Each font option previews in its own face. Changes → `updateSettings` debounced 150 ms.
  - mock-bridge: `getFonts` with a few fake system fonts, `importFont` fake.
- [ ] 3.5 Check: browser (mock) at 1000×680 and 820×560, light/dark. Real app: pick an installed
      font, import a .ttf (any OFL font from `C:\Windows\Fonts` copied to a temp folder is fine),
      move the clock to bottom-left, size 0.7 → Preview screensaver, `input.ps1 -Crop`, show the
      user. Glass rim must still look right at size 0.6 and 1.5. Commit.

Notes:

## Phase 4 — Ship

- [ ] 4.1 README: click burst default, flicker/opacity, clock position/size/fonts.
- [ ] 4.2 `tools\build.ps1` (tests + `dist\Hanami.exe`), relaunch the dev copy normally
      (`--dev --startup`, no debug port), commit, tell the user `dist\Hanami.exe` is updated.
