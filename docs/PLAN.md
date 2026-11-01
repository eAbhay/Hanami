# Implementation plan — round 5: per-wallpaper looks, light/dark wallpapers, then desktop pets

How to use this file (for a fresh chat, e.g. Sonnet):
1. Read `CLAUDE.md`, then this file, then `docs/CONTRACTS.md`. Read `docs/TESTING.md` before testing.
2. Do **one phase per chat**, in order, unless the user says otherwise. Each phase ends with a
   working build, its checks done, boxes ticked here, and one commit.
3. Don't redesign beyond what a phase says; if something in the plan turns out wrong, note it under
   the phase's "Notes" and pick the smallest fix that keeps the intent.
4. Keep this file UTF-8 (round 4's copy got double-encoded: `â€”` instead of `—`). Edit it with the
   file tools, not PowerShell `Set-Content`/`Out-File`.

Earlier rounds are in git history (`git log -- docs/PLAN.md`). Still open from round 4, for the
user on the real desktop: click burst starts off (1.4), effect visibility before/after (2.6), the
clock at a custom position/size/font in a real screensaver (3.5).

## What we're building

Shimeji-style companions that live on top of the desktop: they walk along the taskbar and the tops
of app windows, climb window sides and screen edges, hang from the top of the screen, sit, sleep,
fall when the window under them moves away, and can be picked up with the mouse and thrown.

Decisions already made with the user (don't re-ask):
- **Art = Shimeji image sets.** A pack is a folder of PNGs named `shime1.png … shime46.png` (the
  standard Shimeji-ee frame set). Hanami has its **own behaviour logic**; it does not run the packs'
  `actions.xml` / `behaviors.xml` scripts. Packs are imported from a folder or `.zip`.
  No pet ships in the exe (pack licences vary; the user downloads packs).
- **Windows:** pets stand on and climb windows, and get carried or fall when windows move.
  They **never move, resize or focus the user's windows**.
- **Visibility:** always on top of apps; hidden while a fullscreen app/game runs on their monitor
  and while the screensaver shows. When the power rules put a monitor in `paused`, its pets sit
  still (no animation); `reduced` → pets at most 15 fps.
- **Count:** the user adds/removes pets in settings (the same pack may be added more than once);
  pets **never multiply**. Cap: 8 pets.

Technical decisions (keep them unless they turn out wrong):
- **Native WPF, not WebView2.** One small borderless window per pet (`AllowsTransparency`,
  topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, not in the taskbar or Alt+Tab). A layered window
  hit-tests on pixel alpha, so clicks on the transparent parts go to the app underneath for free.
  Each pet window is ~128×128 px; a WebView2 per pet (or a full-screen transparent overlay) would
  cost far more memory and GPU for a few sprites.
- **Simulation is pure and unit-tested** (`Core/Pets/`), in physical screen pixels, stepped at a
  fixed rate. The window only draws the frame and moves to where the simulation says.
- Frame meanings come from Shimeji-ee's default `conf/actions.xml` (see 3.1), stored as a C#
  table, not parsed at runtime.

```
Core/Pets/PetPack.cs        pack manifest, frame set, validation (pure)
Core/Pets/ShimejiFrames.cs  frame numbers + durations + anchors per animation (pure table)
Core/Pets/World.cs          surfaces: floors / walls / ceilings from work areas + windows (pure)
Core/Pets/PetBrain.cs       state machine + physics: Step(dt, world, input) (pure)
Platform/WindowScan.cs      EnumWindows → visible rects in z-order; WinEvent hooks
App/PetWindow.cs            WPF window: draws a frame, moves, mouse drag/throw/click
App/PetManager.cs           owns the pets, ticks them, applies power/visibility rules
```

---

## Phase 1 — Per-wallpaper looks (do this before the pets)

**Today:** the effect belongs to the monitor slot (`assignments[key].effectPreset`), slider tuning
is shared per effect (`effects[preset]`), and the clock is one global `screensaver.clock`. So
switching wallpaper and back loses nothing only by accident, and one clock spot has to suit every
artwork.

**New (decided with the user):** every wallpaper remembers its own **look**, so switching
wallpapers or screensavers back and forth brings each one's settings back. No profiles.
- **Desktop look** of a wallpaper: which effect, plus that wallpaper's own slider tuning for it.
- **Screensaver look** of a wallpaper (used when it is the screensaver background): its **clock**
  (position, size, font, date options) and its effect. The effect defaults to **"Same as
  desktop"**, which uses the wallpaper's desktop effect *and the same tuning* — one set of settings
  when a wallpaper is used for both. Picking a different screensaver effect gives that look its own
  tuning.
- Tuning layers, lowest first: `PRESET_DEFAULTS[preset]` → `effects[preset]` (now the "effect
  defaults": the starting point for wallpapers that haven't tuned that effect) → the look's own
  `tuning[preset]`. Tuning is kept per preset inside a look, so going sakura → snow → sakura on one
  wallpaper keeps its sakura sliders.
- A wallpaper selected for the first time copies the look of the wallpaper it replaces on that
  monitor (desktop) / the previous screensaver background (screensaver), so nothing suddenly
  changes; from then on it has its own.
- The same wallpaper on two monitors shares one look (accepted).

Contract first, then code:

- [x] 1.1 `docs/CONTRACTS.md`:
  - `type ClockSettings` = today's `screensaver.clock` shape (unchanged fields).
  - `Settings.looks: Record<wallpaperId, { desktop: { effectPreset: PresetId;
    tuning: Partial<Record<PresetId, Partial<EffectTuning>>> };
    screensaver: { effectPreset: PresetId | null;   // null = same as desktop (shares its tuning)
                   tuning: Partial<Record<PresetId, Partial<EffectTuning>>>;
                   clock: ClockSettings } }>`.
  - `assignments[key]` becomes `{ wallpaperId }` only. `screensaver.effectPreset` and
    `screensaver.clock` move into looks; `screensaver.wallpaperId` stays (null = each monitor's
    desktop wallpaper).
  - `effects[preset]` documented as the effect defaults.
  - Bridge: `setAssignment {monitorKey, wallpaperId}` (creates the look by copying, as above);
    `setLook {wallpaperId, desktop?: {effectPreset?}, screensaver?: {effectPreset?: PresetId|null,
    clock?: Partial<ClockSettings>}}` → State; `setEffectTuning {preset, tuning, wallpaperId?,
    target?: "desktop" | "screensaver"}` — with `wallpaperId` it edits that look's tuning (target
    `screensaver` while the screensaver effect is "same as desktop" edits the desktop tuning, since
    they're shared), without it the effect defaults; `tuning: null` resets that level.
  - Host → page messages unchanged in shape (`config.effect`, `config.clock` are composed by the
    host).
- [x] 1.2 Host (`Core/Settings.cs` + composition):
  - `LookSettings`, `AppSettings.Looks`; `ComposeEffect(preset, look tuning)`; desktop config =
    look of the monitor's wallpaper; screensaver config = look of the screensaver background
    (`screensaver.wallpaperId ?? desktop wallpaper`): effect = its screensaver effect or, when null,
    its desktop effect + desktop tuning; clock = its screensaver clock.
  - Migration (`SettingsVersion` 2 → 3) that changes nothing visible: for every assignment
    `{wallpaperId W, effectPreset P}` → `looks[W].desktop.effectPreset = P` (empty tuning, so the
    global `effects` still apply); the old `screensaver.effectPreset` and `screensaver.clock` →
    the screensaver look of every wallpaper currently used as screensaver background; then clear
    the legacy fields (nullable legacy properties, never written back — as in round 3).
  - Missing look (wallpaper never selected, e.g. after import) → built-in defaults: sakura, "same as
    desktop", default clock. Deleting a wallpaper deletes its look; unknown wallpaper ids in looks
    are dropped on load.
  - Export/Import packs: a wallpaper item carries its look (even when settings aren't included);
    importing restores it unless the wallpaper already has one.
  - Tests: migration from a v2 fixture (desktop and screensaver output identical before/after),
    copy-on-first-select, "same as desktop" shares tuning (editing via target screensaver lands in
    desktop tuning), own screensaver effect has separate tuning, layering order, per-preset tuning
    survives switching presets, delete wallpaper removes look, pack round-trip with looks.
- [x] 1.3 Settings UI (`web/settings/`):
  - Wallpaper tab: the Effect card edits the selected wallpaper's desktop look; caption "Saved
    with <wallpaper name>".
  - Screensaver tab: Effect and Clock cards edit the screensaver background's screensaver look
    (with "Same as desktop" background on several monitors and `!sameOnAllMonitors`, add the same
    monitor scope control as the Wallpaper tab). Effect picker's first tile "Same as desktop"
    shows which effect that is.
  - Tuning drawer: edits the look it was opened from; note "Saved for <wallpaper> · <desktop |
    screensaver>" (for a shared "same as desktop" effect: "Shared by <wallpaper>'s desktop and
    screensaver"); actions "Reset" (drops this look's tuning → effect defaults) and "Use as default
    for <Effect>" (writes `effects[preset]` = the current values; affects wallpapers that haven't
    tuned it).
  - Wallpaper tiles: a tiny effect icon/name on each tile that has a look, so you can see what
    comes back.
  - `mock-bridge.js`: looks, `setLook`, extended `setEffectTuning`.
- [x] 1.4 Check: in the real app, wallpaper A + embers tuned to 150% → switch to B, set rain → back
      to A: embers at 150% is back. Screensaver on A with "same as desktop" → preview shows A's
      embers at 150% and A's clock; move A's clock, switch screensaver background to B, move B's
      clock elsewhere, back to A → A's clock spot is back. Dev settings restored afterwards.
      Browser check at 1000×680 and 820×560, light/dark. README (per-wallpaper looks) and the
      settings copy. Commit.

Notes:
- Done 2026-11-01. 1.4 was checked with the mock bridge in a browser (A's embers tuning and clock come
  back after visiting B; "same as desktop" label); the real-desktop pass is still for the user.
- "Use as default" and Reset are in the drawer's Look card; wallpaper tiles show their effect as a
  small badge. The screensaver tab shows the monitor scope control while its background is "Same as desktop".

## Phase 2 — Light and dark wallpapers (needs Phase 1)

**Idea:** a wallpaper slot (each monitor's desktop, and the screensaver background) can hold a
**light** and a **dark** wallpaper. Which one shows follows Windows' light/dark setting, or is
forced to light or dark. Because Phase 1 makes every wallpaper carry its own look (effect, tuning,
clock), the theme only decides *which wallpaper*; nothing else needs a light/dark copy.

**Keeping it uncluttered (the rule for this phase):** it is **off by default**, and while it's off
the app looks exactly as it does now. Turning it on adds one small Light/Dark switch to the two
Background cards, plus marks on the tiles. No second set of pickers, no new tab.

- [x] 2.1 `docs/CONTRACTS.md`:
  - `Settings.theme: { split: boolean (default false); mode: "system" | "light" | "dark"
    (default "system") }`. `split` off → one wallpaper per slot, as today.
  - `assignments[key]: { wallpaperId; darkWallpaperId: string | null }` and
    `screensaver.darkWallpaperId: string | null` (`null` = use the light one, so an unset dark slot
    is never empty). The screensaver's "Same as desktop" means the desktop wallpaper for the theme
    that's active.
  - `State.theme: { active: "light" | "dark"; windows: "light" | "dark" }`.
  - Bridge: `setAssignment` gains `theme?: "light" | "dark"` (which slot to write, default light);
    `updateSettings` covers `theme` and the screensaver's dark slot.
- [x] 2.2 Host:
  - `Platform/ThemeWatch.cs`: read `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`
    `AppsUseLightTheme` (apps theme; matches what users flip in Settings → Colors), and watch it via
    `SystemEvents.UserPreferenceChanged` (category General), re-reading the value; raise
    `Changed` only when it actually flips.
  - Active theme = `mode` if light/dark, else Windows'. Effective wallpaper per slot =
    `split && active == dark && darkWallpaperId != null ? darkWallpaperId : wallpaperId`. Used by
    `WallpaperManager` and `ScreensaverManager` (a theme flip while the screensaver shows swaps its
    background too). The page's existing 600 ms crossfade handles the switch.
  - Phase 1's copy-on-first-select: a wallpaper first chosen for the dark slot copies the look of
    that slot's light wallpaper.
  - Deleting a wallpaper used in a dark slot clears that slot (falls back to light).
  - Tray menu: "Theme" submenu (Follow Windows · Light · Dark), shown only when `split` is on.
  - Tests (pure resolver): split off ignores dark slots; system mode follows the flag; forced
    modes; null dark slot falls back; "same as desktop" screensaver follows the active theme;
    delete clears the slot; packs keep `theme` and dark slots with settings.
- [x] 2.3 Settings UI:
  - **General** tab: card "Light and dark" — toggle "Different wallpapers for light and dark
    mode" (hint: "Hanami switches with Windows' light/dark setting") and, when on, segmented
    **Follow Windows · Light · Dark** with a line "Windows is in dark mode right now".
  - Wallpaper and Screensaver tabs, only when `split` is on: a small segmented **☀ Light | ☾
    Dark** in the Background card header. It picks which slot the picker below edits, and starts on
    the active theme. Tiles get a small ☀ / ☾ badge for the light/dark wallpaper of the slot being
    edited, so both choices are visible at a glance. An empty dark slot shows the hint "Uses the
    light wallpaper until you pick one".
  - The Effect / Clock cards keep editing the look of the wallpaper selected in the Background
    card (the slot being edited), so editing dark mode's effect is just: Dark → change effect.
  - The settings window's own light/dark already follows Windows; leave it alone.
  - `mock-bridge.js`: `theme`, dark slots, a way to flip Windows' theme in the mock.
- [x] 2.4 Check: split on, light = a day scene, dark = a night scene; flip Windows' theme in
      Settings → Personalization → Colors (**ask the user to flip it**, or set the registry value
      in a dev-only test and restore it) → wallpaper crossfades; forced Light/Dark from the tray;
      screensaver preview in both themes; split off → identical to before. Browser check at
      1000×680 and 820×560, light/dark. README. Commit.

Notes:
- Done 2026-11-01. 2.4 was checked with the mock bridge in a browser (split switch, slot marks, dark slot
  selection) and unit tests for the resolver; flipping the real Windows theme is still for the user.
- On the Screensaver tab, picking a dark background while the light one is "same as desktop" pins that
  desktop wallpaper as the light background (an own dark slot needs an own light one).
- Not included: switching at sunset/sunrise on a schedule. Windows has no built-in schedule; if
  the user wants one, "Follow Windows" already works with the free *Auto Dark Mode* app. A
  `mode: "schedule"` could be added later without changing the data shape.

## Phase 3 — Pet packs: format, import, library, settings (no pets on screen yet)

- [x] 3.1 Get the frame meanings: fetch Shimeji-ee's default `conf/actions.xml`
      (https://github.com/gil/shimeji-ee or https://github.com/chaoskagami/shimeji-ee, `conf/`)
      into the scratchpad. From it, write `ShimejiFrames.cs`: for each animation Hanami needs —
      Stand, Walk, Run, Sit (+ sit variants), Sprawl/Lie, Sleep (if present), Fall, Land/Bounce,
      Dragged (with its left/right swing frames), Thrown, GrabWall, ClimbWall, GrabCeiling,
      ClimbCeiling, Pinched/reaction, ChaseMouse — the list of `(frame number, duration in 1/25 s
      ticks, anchor x/y, velocity x/y)` exactly as the XML gives them. Note the source commit in a
      comment. Don't copy the XML into the repo.
- [x] 3.2 `CONTRACTS.md` first:
  - On disk: `Library/pets/<packId>/manifest.json` (`{id, name, createdAt, frameWidth,
    frameHeight, frames: number[]}` — which shime numbers exist) + `img/shime<N>.png`.
  - `Settings.pets: { enabled: boolean (default true); items: {id: string; packId: string}[];
    scale: number (0.5–2, default 1); climbWindows: boolean (default true);
    activity: "calm" | "normal" | "lively" (default "normal") }`.
  - `State.petPacks: PetPackInfo[]` (`{id, name, preview: library.hanami URL of shime1, frames,
    sizeBytes, capabilities: {climb, ceiling, sit, sleep}}`).
  - Bridge: `importPet` (dialog: pick a folder **or** .zip files) → `{imported: PetPackInfo[]}` |
    `{cancelled: true}`; `renamePet {id, name}`; `deletePet {id}` (also removes its items);
    `addPet {packId}` → State; `removePet {id}` → State. `updateSettings` covers the rest.
- [x] 3.3 `Core/Pets/PetPack.cs` + host import (follow `Library`'s style, reuse its helpers):
  - Find the images: the chosen folder itself, its `img/`, or one level deeper (packs are often
    `Name/img/…` or `shimeji-ee/img/Name/…`; a zip may hold several characters → import each as its
    own pack, named after its folder).
  - Accept only `shime<N>.png`, N in 1–46; everything else is ignored. Required: shime1 and the
    frames of Stand, Walk, Fall, Dragged — otherwise reject with a readable reason. Missing climb /
    ceiling / sit frames just switch those capabilities off.
  - Safety: zip-slip proof (never trust entry paths), caps (≤ 200 entries considered, ≤ 2 MB per
    PNG, ≤ 40 MB per pack), decode each PNG once to check it really is one and read its size; all
    frames of a pack must share one size (else reject).
  - Tests: the folder layouts above, a zip with two characters, missing required frame, non-PNG
    named `.png`, `../` entry, oversize, mixed frame sizes, manifest round-trip, settings clamping
    (`scale`, cap of 8 items, items whose pack is gone are dropped on load).
- [x] 3.4 Export/Import packs: pet packs become export items (`kind: "pet"`); importing restores
      them and their settings items. Test the round-trip.
- [x] 3.5 Settings UI, minimal: new tab **Pets** between Screensaver and Power. Header (action:
      Import pet) · card "Your pets": the items (preview = shime1, name, Remove) and an "Add a pet"
      picker over the imported packs · empty state explaining where packs come from (Shimeji packs —
      search for "<character> shimeji"; check each pack's terms). mock-bridge support.
- [x] 3.6 Check: import a real pack (**ask the user where theirs is** — import into the dev root,
      never the real install), it appears with its preview; a broken folder gives a clear error.
      Commit.

Notes:
- Done 2026-11-01 (3.6 with generated test sets and the mock bridge; importing a real pack is still for the user).
- Frame table: Shimeji-ee conf/actions.xml at commit 46bf9e0 (see ShimejiFrames.cs). Required frames are shime1–10.
- A zip is capped at 2000 entries considered (200 per folder) instead of 200 in total, so a zip with several characters works.
- The Pets tab has two import buttons (folder / .zip) because a Windows dialog can't pick either.

## Phase 4 — Pets on screen: ground life, drag and throw

World = each monitor's **work area** only in this phase (floor = top of the taskbar, walls = work
area sides, ceiling = work area top).

- [x] 4.1 `World.cs`: `Surface { Kind: Floor | Wall | Ceiling, From, To, At, Side, OwnerHwnd? }` in
      physical px, built from `Monitors.Enumerate()` work areas. Queries: floor under a point,
      first floor crossed by a movement segment (for falling), nearest wall in a direction.
- [x] 4.2 `PetBrain.cs`: states Stand, Walk, Run, Sit, Lie, Sleep, Fall, Land, Dragged, Thrown,
      React. Fixed step at 25 Hz (like Shimeji, so frame durations are exact); the window draws the
      latest state. Gravity + terminal velocity for Fall/Thrown; Thrown keeps the release velocity,
      bounces off walls with damping, lands on a floor. Idle choices weighted by `activity`
      (calm = more sitting/sleeping, lively = more running); Sleep only after a long calm stretch.
      Walking turns at walls. Deterministic with an injected `Random`.
      Tests: falls and lands on the floor, stays inside the work area, thrown at a wall bounces,
      dragged follows the input exactly, a long run (10 simulated minutes, many seeds) never leaves
      the screen or produces NaN.
- [x] 4.3 `PetWindow.cs`: WPF window as above; frames loaded once per pack as frozen
      `BitmapSource`s (shared by every pet using that pack); drawn at `scale × monitor DPI scale`
      (frames are authored for 100%); flipped horizontally when facing right (Shimeji frames face
      left). Position = pet position minus the frame's anchor. One `SetWindowPos`
      (`SWP_NOACTIVATE | SWP_NOZORDER`) per change, nothing when nothing changed.
- [x] 4.4 Mouse: left-press on the pet → Dragged (the cursor holds the frame's anchor point, the
      swing frame follows cursor velocity); release → Thrown with the last ~80 ms of cursor velocity.
      A click without moving (< 4 px) → React. Right-click → small menu: Sit · Sleep · Hide pets ·
      Pet settings. The pet window must never take focus (typing in the app underneath keeps
      working while you drag).
- [x] 4.5 `PetManager.cs`: a window per settings item; each pet spawns falling from the top at a
      random spot on the primary monitor; one `DispatcherTimer` for all pets, stopped when no pet is
      visible. Tray menu: "Show pets / Hide pets" (this session only, not saved). Settings changes
      (add/remove/scale/enabled) apply without restarting the other pets.
- [x] 4.6 Check on the real screen (`capture-desktop.ps1`; synthetic drag via `input.ps1` if it can,
      else ask the user to try): pets fall and land on the taskbar, walk, sit; drag and throw work;
      clicks on transparent pixels next to a pet reach the app below. Perf with 3 pets: well under
      1% CPU for the pets' part, no memory growth over 10 min. Commit.

Notes:
- Done 2026-11-01. The simulation (World, PetBrain) already includes windows, climbing and ceilings, which phases 5 and 7 build
  the host side and settings for; they are unit-tested in PetBrainTests / WorldTests.
- 4.6: on the real screen two test pets (generated blob frames, in the dev root only) fall, land on the taskbar and walk; ~0.5 % of
  one core for the whole Hanami process with 2 pets, 178 MB. Drag/throw/click-through still need the user's hands.
- Pets use the power rules *without* the maximized/fullscreen rules (they live on top of windows); a fullscreen app on their
  monitor hides them. The pet timer runs at 30 ms and catches up to 4 ticks.
- Screen-capture tip: GDI needs CAPTUREBLT to see layered windows (the pets), see tools/dev if a script is added.

## Phase 5 — Windows as terrain: stand, climb, get carried

- [x] 5.1 `Platform/WindowScan.cs`: top-level windows in z-order that are visible, not minimized,
      not cloaked (`DWMWA_CLOAKED`), not tool windows, not ours, not the shell's desktop/taskbar
      windows, at least ~120×60 px; bounds from `DWMWA_EXTENDED_FRAME_BOUNDS` (`GetWindowRect`
      includes the invisible resize border). Maximized windows give no top-edge floor but still
      cover what's behind them.
- [x] 5.2 `World.cs`: per window, a floor on its top edge and walls on its sides — **only the parts
      not covered by windows above it** in z-order (interval subtraction). Pure; tests with
      overlapping rectangles: fully covered edge → no surface, partly covered → split segments.
- [x] 5.3 Keeping it fresh: rescan every 500 ms, plus out-of-context `SetWinEventHook` for
      `EVENT_OBJECT_LOCATIONCHANGE` (window objects only), `EVENT_SYSTEM_MOVESIZESTART/END`,
      `EVENT_SYSTEM_MINIMIZESTART`, `EVENT_OBJECT_DESTROY`/`HIDE`, `EVENT_SYSTEM_FOREGROUND`
      (z-order). The window a pet stands or climbs on moves → **carry** the pet by the same delta
      that tick; its surface disappears (minimized, closed, now covered) → Fall. At most one
      hook-driven rescan per tick.
- [x] 5.4 `PetBrain` climbing: at a wall, GrabWall → ClimbWall up/down; at the top of a window side
      it can step onto that window's top edge; at the work-area top, GrabCeiling → ClimbCeiling,
      eventually letting go → Fall. Walking off the end of a window top → Fall (sometimes a jump down
      to a lower floor). Only when the pack has those frames and `climbWindows` is on (off = work
      area only, as in phase 4). Tests on hand-built worlds.
- [x] 5.5 Pets are topmost, so a pet on a window that later gets covered would float in front of
      the covering window. The interval subtraction removes covered edges, so it falls instead —
      classic Shimeji behaviour; accept it and note it.
- [x] 5.6 Check on the real screen with a couple of ordinary windows: pets land on window tops,
      climb sides, ride along when the window is dragged (**ask the user to drag one**), fall when
      it's minimized. Confirm Hanami moved, resized or activated no user window. Commit.

Notes:
- Done 2026-11-01. WindowScan + WinEvent hooks drive a World rebuilt at most once per tick (plus a 500 ms safety scan);
  carrying, falling, climbing and the covered-edge subtraction are unit-tested. On the real screen the test pets climbed the
  screen edges; riding a window you drag is still for you to try (5.6), as is minimizing one.
- 5.5 as planned: a pet on a window that gets covered falls (classic Shimeji behaviour).

## Phase 6 — Living with the rest of Hanami: power, fullscreen, screensaver, monitors

- [x] 6.1 Per pet, its current monitor decides: a fullscreen app there (`CoverageProbe`) → hide
      that pet; that monitor's mode `paused` (from `ModeResolver`: manual pause, battery rules,
      maximized rule) → freeze on the current frame, still visible; `reduced` → 15 fps cap.
      Screensaver showing → hide all pets (never above it) and restore them where they were when it
      ends. Session locked → hide all, stop the timer.
- [x] 6.2 Multi-monitor: the world is the union of all monitors; pets walk across where work areas
      touch and fall onto a lower monitor's floor; a pet outside every monitor (monitor unplugged,
      resolution change) respawns falling on the primary. Per-monitor DPI: when a pet crosses to a
      monitor with another scale, resize its window explicitly (don't rely on WPF's automatic
      DPI-change resize for a layered window). Unit-test the world maths with two monitors at
      different DPI; real check on this laptop's single screen, and note the external-monitor check
      for the user's other laptop.
- [x] 6.3 Resume from sleep and Explorer restarts (`TaskbarCreated` registered message): re-read
      work areas and rescan; pets fall to the new floor.
- [x] 6.4 Check: screensaver preview with pets out → none over it, back in place after; manual
      pause → pets freeze; fullscreen video in a browser → that monitor's pets vanish and come back.
      Commit.

Notes:
- Done 2026-11-01. Pets hide for a fullscreen app on their monitor, the screensaver (seen: none over the screensaver), a locked
  session; they freeze under `paused` and draw at most every other tick under `reduced`. Work areas are re-read every 2 s (taskbar
  moved, Explorer restarted, monitors changed) and after resume; the external-monitor check is for the user's other laptop.
  The world maths is in physical pixels, so per-monitor DPI only changes a pet's drawn size (resized explicitly each frame).

## Phase 7 — Character and polish

- [x] 7.1 Chase the mouse: now and then (more often with `lively`) a pet walks or runs towards the
      cursor along its current floor and stops next to it (ChaseMouse frames if the pack has them).
- [x] 7.2 Reactions: click → React (Pinched frames) with a few small hearts drawn inside the same
      pet window (no extra window); a pet dragged around for a while sprawls dizzily after landing.
- [x] 7.3 Settings UI: card "Behaviour" — Size slider (`scale`), Activity segmented (Calm · Normal ·
      Lively), Climb windows toggle, master "Show pets" toggle; each pet row gets "Call here" (falls
      from above the centre of the primary monitor) and capability chips (Climbs · Ceiling · Sits).
      Pack rename. Check at 1000×680 and 820×560, light and dark.
- [x] 7.4 README (pets section, where to find packs, licences belong to the pack authors),
      CONTRACTS final pass, a pets section in `docs/TESTING.md`. Commit.

Notes:
- Done 2026-11-01. Chase, reactions (hearts drawn inside the pet window), dizzy sprawl and the Behaviour card are in; "Call here" is
  the bridge method `callPet`. Checked in the browser on the mock bridge at 800 px (dark); the 1000-px and light-theme
  looks use the same shared styles.

## Phase 8 — Ship

- [x] 8.1 `tools\build.ps1` (tests + `dist\Hanami.exe`), relaunch the dev copy normally
      (`--dev --startup`, no debug port), commit, tell the user `dist\Hanami.exe` is updated.

Notes:
- Done 2026-11-01: 159 tests pass, `dist\Hanami.exe` (72.7 MB) rebuilt, dev copy relaunched with `--dev --startup`.
