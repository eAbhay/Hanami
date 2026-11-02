# Implementation plan — round 6: screensaver slide-up, matching lock screen, clock glass, livelier pets

How to use this file (for a fresh chat, e.g. Sonnet):
1. Read `CLAUDE.md`, then this file. In `docs/CONTRACTS.md`, read the sections your phase names and search it
   for any setting or message you change; read all of it only when changing its structure. Read `docs/TESTING.md` before testing.
2. Do **one phase per chat**, in order, unless the user says otherwise. Each phase ends with a
   working build, its checks done, boxes ticked here, and one commit.
3. Don't redesign beyond what a phase says; if something in the plan turns out wrong, note it under
   the phase's "Notes" and pick the smallest fix that keeps the intent.
4. Keep this file UTF-8. Edit it with the file tools, not PowerShell `Set-Content`/`Out-File`.
5. A change includes its tests, comments, `docs/CONTRACTS.md`, `README.md` and settings-UI copy.
   Change the contract first, then both sides. `web/settings/mock-bridge.js` must keep working.

Earlier rounds are in git history (`git log -- docs/PLAN.md`). Still open from round 5, for the user on
the real desktop: real pack drag/throw, riding a dragged window, flipping the Windows theme, a
second-monitor DPI check on the other laptop. The in-wallpaper Live2D character is not part of this round.

**Safety rules for this round, do not break them while testing**
- Never call `SetImageFileAsync` on the user's real lock screen, and never call `LockWorkStation`, without
  asking in that chat first. Automated tests must not lock the PC. Use Preview (it never locks) for the slide.
- Don't enable `requireSignIn` in the dev settings unless the user is ready to test with you.
- Never register a lock image from a unit test. Everything that touches Windows goes behind a small interface.

## Decisions already made with the user (don't re-ask)

- **Slide-up:** any key or click that ends the screensaver slides it up off the screen, like the Windows lock
  screen. No setting for it. Only input-driven ends animate: unplug, sleep, lock and stop-after stay instant.
- **Matching lock screen:** a new setting, **off by default**, in the Screensaver tab. Live wallpaper on the real
  lock screen isn't possible (it runs on Windows' secure desktop, no app window can appear there), so it's a still.
- **Refresh:** every time a screensaver starts, and whenever the screensaver's wallpaper or the theme changes.
- **Never restored automatically.** While the option is on Hanami doesn't put the old image back. The first time it
  writes, it keeps a copy of the previous image; turning the option off offers "Restore my previous picture" and a
  link to Windows' lock screen settings. Windows Spotlight (the user has it on) can't be cleanly turned back on by an
  app, which is why that link exists and why enabling the option asks for confirmation.
- **One image:** Windows has one lock image for all monitors, so it uses the primary monitor's screensaver wallpaper.
- **Two presses are accepted.** With sign-in on: press once to slide to the Windows lock screen, again to reach
  the PIN box. Apps can't send input to the secure desktop. No NoLockScreen policy, no registry tricks, no PersonalizationCSP.
- **Clock:** tint colour, glass opacity and frost amount, saved per wallpaper with the rest of the clock.
- **Pets:** latch on when dropped near a wall or ceiling; sit on windows when dropped on them; at a window's edge
  they turn back, step off or climb down; a window overlapping their body blocks them, and they climb or jump to its
  side; they also sometimes **seek** a nearby window on purpose. Jump reach is about **2 pet heights**.
- **Extras, all four chosen:** window play (dangling legs, hopping between windows), window reactions (shake off,
  yelp when a window goes), companions (pets greet, sit together, follow), hover to stroke plus Stay here / Follow me.
- Still true from round 5: pets **never move, resize or focus the user's windows**, never multiply, and Hanami runs
  only the frames, never a pack's scripts. Maximized windows still give no edges.

## Why the current pets don't do this (already diagnosed, read the code with this in mind)

- **Not sitting on windows:** `PetBrain.EndDrag` always starts a throw, and `StepAir` only lands when the feet *cross a
  floor going down* (`World.FirstFloorCrossed`). The cursor holds the feet, so letting go a few pixels inside a title
  bar starts below the top edge and the pet drops through to the taskbar.
- **Not latching:** nothing checks for walls on release, and `StepAir` always bounces off a wall it hits.
- **Rarely climbing windows:** `MoveOnFloor` tests walls only at `Y - 6 * _ps`, i.e. the feet. A floating window that
  stops above the taskbar is never "in the way", so pets walk past in front of it.
- **Edges:** `MoveOnFloor` only turns back or steps off at a window's edge; climbing down isn't an option.

---

## Phase 1 — Clock tint, opacity, frost

Contract sections: `ClockSettings`, the `config.clock` row, the `setLook` row.

Additive fields, so `AppSettings.CurrentVersion` stays 3 (missing values take the defaults).

- [x] 1.1 **Contract:** add to `ClockSettings` in `docs/CONTRACTS.md`, and to the `config.clock` row:
      `tint: string` (`#rrggbb`, default `#fff4fc`), `opacity: number` (0.04–0.8, default 0.16),
      `frost: number` (blur px at 1080p, 0–40, default 18).
- [x] 1.2 **Host:** `Core/ScreensaverPolicy.cs` `ClockSettings` gains `Tint`, `Opacity`, `Frost` with `Default*`
      constants. `Settings.cs` `NormalizeClock`: clamp the numbers with `Finite`, replace a tint that fails
      `^#[0-9a-fA-F]{6}$` with the default. `FontLibrary.ClockMessage` passes them through (check how it builds the object).
- [x] 1.3 **Page, `web/wallpaper/clock.js`:** add to `DEFAULTS` and `sanitizeConfig`. In `set()`, set CSS variables on
      `#clock`: `--clock-fill` (tint at the opacity), `--clock-edge` (tint mixed 50% with white, alpha 0.28),
      `--clock-frost` (px, scaled by `innerHeight / 1080`). In `style.css`, `#clock text.face` uses `--clock-fill` and
      `--clock-edge`, `.frost` uses `blur(var(--clock-frost, 18px))` (keep `saturate`/`brightness`, both prefixes).
      **Watch out:** `<feFuncA slope="12">` builds the solid glyph mask from the fill's alpha, so at low opacity every
      edge effect fades away. Set the slope to `Math.max(12, 1.2 / opacity)` in `set()`. That's why the minimum isn't 0.
      Keep the date white: it needs to stay readable.
- [x] 1.4 **Settings, `components/clockCard.js`:** a "Glass" group under the font: tint swatches (Default `#fff4fc`, White
      `#ffffff`, Ice `#cfe8ff`, Sakura `#ffc4dc`, Gold `#ffe2a8`, Ink `#20182e`) plus an `<input type=color>`, then sliders
      "Glass opacity" (shown as %) and "Frost". Same debounce path as size (`change`). The preview's `timeEl` shows the
      tint with its opacity, approximately; say nothing about it being exact. Update `defaults.js` `DEFAULT_CLOCK` and the
      `mock-bridge.js` clock defaults and sanitising.
- [x] 1.5 **Tests:** `ClockTests`: defaults, clamping, bad tint, old settings without the fields load with defaults, round trip.
- [x] 1.6 **Check:** Preview at low opacity, high opacity, a dark tint and frost 0 and 40, on a bright and a dark wallpaper,
      using `tools\dev\input.ps1 -Crop`. The rim must survive at minimum opacity. Commit.

Notes:
Done. Checked in the browser dev page with the CSS variables set by hand (dark tint, 4% opacity, frost 0, slope 30): rim, shade and shadow survive. Not done: the real-screen Preview check with `input.ps1 -Crop` on bright and dark wallpapers. Glass code is `applyGlass()` in `web/wallpaper/clock.js`; the settings preview colour is approximate.

## Phase 2 — Slide-up dismiss

Contract sections: "Screensaver behaviour", the `dismiss` message row.

The window itself moves, because the desktop has to be visible behind it. Host-driven, so no page changes.

**Flow in `ScreensaverManager`** (split `Hide`):
- `OnPageMessage` / `WatchInput` input ends call `DismissAsync(why)`. `End(why)` and `TickAsync`'s automatic ends keep calling
  the instant path. `End` during a slide (lock, sleep) cancels it and finishes instantly.
- New `_sliding` flag: `TickAsync` returns early while it's set, further `dismiss` messages are ignored, `_inputWatch` stops,
  `KeepOnTop` doesn't run. `Showing` stays **true until the end**, so pets stay hidden until the screensaver is gone.
- New `Revealing` property, raised with `Changed` at slide start when not locking. `HanamiHost.OnScreensaverChanged` sets
  `_wallpapers.ScreensaverShowing = Showing && !Revealing` (pets keep using `Showing`).
  **This matters:** `ModeResolver` returns `Hidden: true` while the screensaver shows, so without it the slide reveals a suspended
  wallpaper. Check that the mode is re-evaluated right after `Changed` (`WallpaperManager.ApplyModesAsync`), and wait about 150 ms before the
  first frame moves. Tune that delay by eye; if the wallpaper needs longer, say so in Notes rather than making the slide feel laggy.
- `DismissAsync` finish: dispose views, keepAwake off, restore foreground, and the same `_blocked`/`_inputBaseline`/log/status work
  `Hide` does today. Move that into one shared `Finish(byInput, lockIt)` so the two paths can't drift.

**The animation:**
- [x] 2.1 `Native/Win32.cs`: `SetWindowRgn`, `CreateRectRgn` (the system owns a region once it's set; don't delete it).
- [x] 2.2 `WallpaperView.SlideTo(int offset)`: `SetWindowRgn(hwnd, CreateRectRgn(0, offset, w, h), false)`, then
      `SetWindowPos(hwnd, 0, left, top - offset, w, h, SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSIZE)`. The region clips the window to its
      own monitor, so with an external monitor **above** the laptop the slide doesn't appear on that screen.
- [x] 2.3 The slide loop: all monitors together, 420 ms, ease-out `1 - (1 - t)^3`, timed by `Stopwatch` and driven by
      `CompositionTarget.Rendering` (remove the handler when done). Offset is a fraction of each monitor's own height.
      Put the curve in a pure `ScreensaverPolicy.SlideOffset(t, height)` with tests (0 at start, full height at 1, monotonic, clamped).
- [x] 2.4 **With `requireSignIn`, the desktop must never show unlocked.** New `App/ScreenCurtain.cs`: one topmost, no-activate,
      tool-window WPF window per monitor, opaque black with an optional `UniformToFill` image, placed in physical pixels with
      `SetWindowPos`. Create it when the screensaver shows (when `requireSignIn && !preview`), z-ordered directly under that monitor's
      screensaver window. If it doesn't exist at dismiss (setting changed while showing), create it and yield a render first.
      For now its image is black; phase 3 supplies the real one.
      Locking flow: slide, `LockWorkStation()`, dispose the views, then close the curtains when `OnSessionSwitch` reports the lock, or after
      a 3 s timeout (log it: lock may be blocked by policy). `End("Windows locked")` must close curtains, so route it through the same place.
      Preview never locks, so it never gets a curtain.
- [x] 2.5 **Check with Preview, no real lock:** click and key both slide; video keeps playing; the wallpaper behind is live, not black;
      the click doesn't land on the app underneath once the window has moved (the WebView2 has mouse capture; verify in a real app);
      focus returns to the previous window. Use `tools\dev\perf.ps1` during a video slide on Intel graphics.
      If moving the live window janks: capture one frame with `CapturePreviewAsync` at dismiss, show it in a plain WPF window and slide that instead.
      Note it in Notes. A second-monitor check (including `WM_DPICHANGED` when the window nears another DPI) is for the user's other laptop: list it at the end.
- [ ] 2.6 Ask the user to try one real sign-in dismiss (they enable `requireSignIn` themselves). They should see no flash of desktop.
- [x] 2.7 Update the "Screensaver behaviour" section of the contract and the status hint text in `tabs/screensaver.js`. Commit.

Notes:
Done except 2.6 (the user's own sign-in dismiss test). Slide ran on the real screen in the dev copy: the log shows the desktop wallpaper resuming, then the end ~600 ms after the key (150 ms reveal delay + 420 ms slide), so `CompositionTarget.Rendering` ticks fine. Not checked: how it looks mid-slide, perf on a video, the click not reaching the app below, and anything with `requireSignIn`/`ScreenCurtain` (never enabled). Code: `ScreensaverManager.DismissAsync`/`SlideAsync`/`Finish`, `App/ScreenCurtain.cs` (has an `Image` setter for phase 3), `WallpaperView.SlideTo`. For the other laptop: external monitor above the laptop, other DPI.

## Phase 3 — Matching lock screen

Contract sections: "Screensaver behaviour", the `config` and page-message tables, the `screensaver` part of settings and `State`, the bridge methods table.

**First step, stop if it fails:**
- [x] 3.1 Change `TargetFramework` to `net10.0-windows10.0.19041.0` in **both** `Hanami.App.csproj` and `Hanami.Tests.csproj`. Build, run tests, check the
      published exe's size change. New `Platform/LockScreenImage.cs` wrapping `Windows.System.UserProfile.LockScreen`:
      `SetAsync(path)` (`StorageFile.GetFileFromPathAsync` + `SetImageFileAsync`, called on the UI thread) and `BackupAsync(path)`
      (`GetImageStream` to a file). Catch and report failures, never throw into the host.
      **Ask the user before the first real call**, then set a test image on this Insider build and have them press Win+L. Check whether Spotlight
      switches off and how long the image takes to appear. If the API fails on an unpackaged app, stop and report; don't reach for registry or policy routes.

**Getting the frame.** It must be the wallpaper alone, without the clock or particles, since Windows draws its own clock.
- [x] 3.2 **Pure rules, `Core/LockImagePolicy.cs`** (tested): `Signature(wallpaperId, dark)`; `ShouldCapture(enabled, trigger, lastSignature, signature, type)`: on a screensaver start
      always for video and builtin, for an image only when the signature changed; on a settings or theme change only when it changed.
      Add `screensaver.syncLockScreen: boolean` (default false) to contract and `ScreensaverSettings`.
- [x] 3.3 **Page handshake**, added to the contract: `config.holdForCapture: true` on the primary monitor's screensaver page only. The page keeps the clock unrevealed and
      `#fx` hidden until the first background frame is fully visible (video `playing`/image loaded/scene drawn, plus the 600 ms `FADE_MS` crossfade), then posts `{type:"readyForCapture"}`.
      The host replies `{type:"captureDone"}`, and the page reveals the clock and starts `#fx`. The page also reveals after 3 s by itself, so a host failure can't leave it without a clock.
      `clock.js` needs a way to hold its `reveal` for this.
- [x] 3.4 `WallpaperView.CaptureAsync(path)`: `CoreWebView2.CapturePreviewAsync(Jpeg, stream)`, writing to a temp file then moving it over the target.
- [x] 3.5 **New `App/LockScreenSync.cs`** owns: `<config>/lockscreen/current.jpg` and `previous.jpg` (add to `AppPaths`), the last signature, the first-write backup (only if `previous.jpg` doesn't exist) and status.
      Screensaver start (not preview): capture from the primary view, then set. Must not delay the screensaver.
- [x] 3.6 **Refresh without a screensaver** (setting turned on, wallpaper or theme changed), debounced 1.5 s, skipped while showing or locked: a temporary hidden capture view from a new
      `WallpaperView.CreateCaptureAsync` at the primary monitor's size with `&screensaver=1&capture=1` (no clock mounted, no input handling).
      Chromium may not paint an off-screen window. Try off-screen first; if frames don't come, use a click-through, no-activate layered window with alpha 1 on the monitor; if that fails too, for a video
      use `thumb.jpg` and tell the user the first image is low-res until the next screensaver. Dispose it right after.
      Trigger from `HanamiHost` next to `_screensaver.PushConfigAll()` and in `OnThemeChanged`.
- [x] 3.7 **Curtain image:** sync on: `current.jpg`, swapped in when a capture finishes. Sync off: the image from `GetImageStream`, decoded once, else black.
- [x] 3.8 **Bridge and state** (contract first): `state.screensaver.lockScreen: {status: "off"|"ok"|"failed", error?, updatedAt?, hasBackup}`; methods `restoreLockScreen` and `openLockScreenSettings` (`ms-settings:lockscreen`, shell execute).
- [x] 3.9 **Settings, `tabs/screensaver.js`:** a "Lock screen" card with the toggle "Match the lock screen to the screensaver", default off. Copy says it's a still image from the screensaver, updated each time
      it starts, and shared by all monitors (primary's wallpaper). Turning it on opens a confirm `dialog`: "This replaces your Windows lock screen picture, and turns off Windows Spotlight if it's on. Hanami keeps a copy of your current picture."
      Status line shows last update or error. When off with a backup: "Restore my previous picture" and "Lock screen settings…". Mock-bridge support for all of it.
      On the Power tab, mention the option in the `requireSignIn` row's hint.
- [ ] 3.10 **Check with the user present:** turn on, Win+L, compare; change wallpaper, Win+L; flip theme; real screensaver with sign-in end to end; restore. Clock absent from the image; video, image and builtin all work.
- [x] 3.11 README section; note in `Uninstaller`/data docs that Windows keeps its own copy of the picture after uninstall. Commit.

Notes:
Done except 3.10 (the user's own checks: Win+L per wallpaper type, theme flip, real sign-in end to end, restore). 3.1: the API works only with the lock screen set to **Picture**; with Windows Spotlight it succeeds but Spotlight still shows, and Hanami can't switch Spotlight (the registry value `RotatingLockScreenEnabled` already read 0 while Spotlight was active), so the card and confirm dialog tell the user to pick Picture and link to Windows' settings (the plan's wording that Hanami turns Spotlight off was dropped). The exe grew 72.7 -> 78.8 MB. `GetImageStream` returned the same original picture even after our set, so `previous.jpg` is Windows' view of the pre-Hanami picture, not necessarily what the user last saw. Verified on this laptop: clean capture of a video/image wallpaper (no clock, no particles) both from the hidden 1/255-alpha capture view and via `holdForCapture` at a real screensaver start (about 1.2 s, clock appears afterwards). Not checked: video wallpapers specifically, builtin scenes, the curtain image, a theme flip. Code: `App/LockScreenSync.cs`, `Core/LockImagePolicy.cs`, `Platform/LockScreenImage.cs`, `WallpaperView.CreateCaptureAsync`/`CaptureAsync`.

## Phase 4 — Pets: drop on windows, latch on walls

Contract sections for phases 4–6: "Desktop pets". Phase 7 also: the `pets` part of settings.

`PetBrain` stays pure and deterministic; everything gets tests in `PetBrainTests`/`WorldTests`. The existing tests use `EndDrag(vx, vy)`; update them for the new signature.

- [x] 4.1 `World` queries:
      `NearestWall(x, y, reach)`: nearest wall with `|At - x| <= reach` whose span holds `y` (with `Tolerance`);
      `DropFloor(x, y, above, below)`: nearest floor whose range holds `x` with `At` in `[y - below, y + above]` (floors are already cut where a window in front covers them, so a covered title bar isn't offered);
      `FirstCeilingCrossed(x0, y0, x1, y1)`, same shape as `FirstFloorCrossed` but going up.
- [x] 4.2 `EndDrag(vx, vy, World world)`. Gentle release (`hypot(vx, vy) < 6 * _ps`) picks the **closest** of:
      a wall within `40 * _ps` (needs `Caps.Climb`), clamping `Y` into `[From + 2, To]`;
      the ceiling within `40 * _ps` (needs `Caps.Ceiling`);
      a floor from `DropFloor(x, y, 24 * _ps, 56 * _ps)`, which is the fix for dropping into a title bar.
      None nearby, or a fast release: throw as today. A window's interior below that band still falls: that's intended.
- [x] 4.3 Refactor: `LandOn(floor)` out of `StepAir` (hard-landing bounce, dizzy sprawl) used by the floor case too. `LatchWall(wall)` and `LatchCeiling(ceiling)`: like `GrabWall`, but the pet **hangs on for 5–20 s** (125–500 ticks) before it starts climbing, so it stays where it was put. Clear `_dizzy` on a latch.
- [x] 4.4 Thrown pets: in `StepAir`, when `State == Thrown` and it meets a wall (`FirstWallCrossed`) with `Caps.Climb`, latch instead of bouncing (the short hold, 12–37 ticks). Same for a ceiling crossed while rising, with `Caps.Ceiling`. Plain `Fall` keeps bouncing.
- [x] 4.5 `PetManager.OnReleased`: `RebuildWorld()` first when `_windowTerrain` (the scan can be up to 500 ms old), then `EndDrag(vx, vy, _world)`.
- [x] 4.6 **Tests:** drop inside a title bar lands on it; just above it too; deep in the window falls; drop near a window side, screen edge and ceiling latches and stays through the hold; no-climb pack doesn't; covered title bar doesn't catch; corner picks the closer; fast throw into a wall latches; fast throw in open air doesn't.
- [ ] 4.7 **Real check** (pet test set per `TESTING.md` §8): drag with synthetic input onto a real window's title bar and each screen edge, capture with CAPTUREBLT. Update the contract's Desktop pets section. Commit.

Notes:
Done in code and tests (`PetBehaviourTests`, `PetAnimationTests`, 221 pass); the real-screen check of 4.7 is NOT done (unticked), so is the part of 4.7 that
needs a human. Differences: `EndDrag(vx, vy, world)`; `LandOn` also attaches the surface; an existing test (`Pet_IsCarriedWhenItsWindowMoves`)
now moves the window in small steps because one 85 px jump in a tick counts as a shake (6.3). A thrown pet decays to a stop, so a ceiling
test must start within about 90 px of the ceiling. New `World` queries: `NearestWall`, `DropFloor`, `FirstCeilingCrossed`, the body-span `FirstWallCrossed`,
`ReachableWall`, `HopTarget`. All brain logic is in `Core/Pets/PetBrain.cs`; `PetManager.OnReleased` rebuilds the world before `EndDrag`.

## Phase 5 — Pets: window edges and being blocked

- [x] 5.1 **Edges:** replace the end-of-floor block in `MoveOnFloor`. For window floors pick by weight: turn back 35, climb down 35 (needs `Caps.Climb` and `world.Find(Wall, _on.Owner, Y + 2, X, 3)`), step off 30. New `StartClimbDown(wall)`: `Attach`, `X = wall.At`, `Y = wall.From`, face the wall, `_climbDir = +1`, `GrabWall` state. A window side that sits on the screen border has no wall, so it just drops out of the options. Work-area behaviour unchanged.
      Check the `Y >= wall.To` exit: `StartFall(wall.Side * 2 * _ps)` still fine for a window ending in mid-air.
- [x] 5.2 **Body height:** `PetProfile` gains `double BodyHeight = 0` (0 means `128 * PixelScale`). `PetManager.Reset` passes `Frames.Height * Ps * 0.85`.
- [x] 5.3 `World.FirstWallCrossed` gets an overload for a vertical span (`headY`, `feetY`): a wall counts if its span overlaps the span. `MoveOnFloor` uses `Y - BodyHeight` to `Y - 6 * _ps`. The old feet-only overload stays for `StepAir`.
- [x] 5.4 **Jump state:** `PetState.Jump`, a scripted arc, not physics, so it's exact and testable: start point, target point, `N = 14 + distance / (6 * _ps)` ticks, X and Y interpolated with `arc = 4 * h * t * (1 - t)`, `h = 24 * _ps`. Target is a `Surface`. Each tick re-find it with `world.Find`; gone means `StartFall`. Add `Jump` to the exclusion in `Step` so `FollowSurface` doesn't drop it. On arrival: a wall means `GrabWall` climbing up, a floor means `Attach` then `Stand`.
- [x] 5.5 **The jump animation:** add `"jump"` to `PetAnimations.Names` (default `ShimejiFrames.Jump`, `actions.xml` `Jumping`). **Important:** in `FromMapping`, a pack with its own map that doesn't define it must fall back to its own `fall`, not the default table, because its frames are numbered differently (e.g. `uma`). Same rule for every animation added in this round. Add a `PetPackTests` case.
- [x] 5.6 **Blocked:** when the body hits a wall in `MoveOnFloor`: wall reaches the feet: `GrabWall` with probability 0.85 for windows and the existing 0.6 for screen edges, else turn back. Bottom is above the feet (`wall.To < Y - 6 * _ps`): `Caps.Climb` means jump to `wall.To - 2`, otherwise turn back.
- [x] 5.7 **Seek:** `World.ReachableWall(x, feetY, reach, maxDistance)`: the nearest window wall whose open side faces the pet and whose bottom is within `reach` above the feet (`reach = 2 * BodyHeight`, `maxDistance = 600 * _ps`). New `PetState.Seek`: `ChooseNext` adds it with weight calm 0.3, normal 1, lively 2.5 when `Caps.Climb` and there's a target. It walks with the walk anim, re-validates the target each tick by owner and side, gives up after 400 ticks or if it goes, and on arrival within `40 * _ps` jumps (or grabs if the wall reaches the floor). Prefers to climb all the way: set `_climbTicks` so it doesn't let go part-way.
- [ ] 5.8 **Tests:** walks past a window far overhead; blocked by one at head height; jumps and grabs; no-climb pack turns back; never jumps beyond reach; climbs down an edge to mid-air then falls; seek reaches the top of a window; target closing mid-jump falls.
      Use fixed seeds and long runs to check the pet never ends up outside `Bounds`. Real check with a real floating window. Contract section. Commit.

Notes:
Done in code and tests; real-screen check (5.8) NOT done. Jump = `PetState.Jump` scripted arc; `Seek`, window `Perch`, hop all in `PetBrain`
(`WindowEdge`, `Blocked`, `StartJump`). `jump`/`sitDangle` animations fall back to a mapped pack's own fall/sit (`PetAnimations.FromMapping`).
`PetProfile.BodyHeight` = frame height x scale x 0.85 (`PetManager.Reset`). Seek/hop odds and the 600 px / 3 and 1.5 body-height limits are as planned, untuned.

## Phase 6 — Pets: window play and window reactions

- [x] 6.1 **Dangling:** add `"sitDangle"` to `Names` (default `ShimejiFrames.SitDangleLegs`, `actions.xml` `SitAndDangleLegs`, fallback for mapped packs: their `sit`). New `PetState.Perch`: at a window edge, an extra weighted option (only on window floors, only if `Caps.Sit`): `X` moved `20 * _ps` back onto the surface, facing outward, 200–750 ticks, then `ChooseNext`, facing inward. `Click` and `Command` treat it as a floor state.
- [x] 6.2 **Hop:** `World.HopTarget(x, y, facing, gapMax, riseMax)`: the nearest other window's floor in that direction, gap up to `3 * BodyHeight`, height difference up to `1.5 * BodyHeight`, target point not covered. A weighted option at an edge, and also from `ChooseNext` when near one. Uses `Jump` with a floor target.
- [x] 6.3 **Shake off:** in `FollowSurface`, record each tick's delta of `_ownerRect` in a short buffer. Thrown if the direction reverses at least twice within 15 ticks at more than `25 * _ps` px/tick, or one jerk is over `60 * _ps`: `StartFall(Thrown, delta * 0.6, -8 * _ps)` and `_dizzy = true`. Normal dragging keeps carrying. Thresholds are guesses: tune by dragging a real window, and write the final numbers here.
- [x] 6.4 **Yelp:** when `FollowSurface` fails because `world.RectOf(owner)` is null (closed or minimized, not merely covered): a small hop `StartFall(Fall, 0, -6 * _ps)` plus a startled mark.
      Generalise `ReactProgress` into `Mark` (`None`, `Hearts`, `Startled`) + progress; `PetWindow.Present` draws a "!" in the same way as hearts (update its doc comment, and fix the mojibake `0â€“1`/`âˆ’1` there while you're on that line).
- [ ] 6.5 **Tests** for all of the above with scripted `World`s (moving a window between steps). Real check: drag, shake, minimize, close with `tools/dev` scripts on a window you opened yourself. Never touch the user's windows. Contract section. Commit.

Notes:
Done in code and tests; real-screen check (6.5) NOT done. Shake thresholds are the plan's guesses (25 px/tick x2 reversals within 15 ticks, or one
60 px jerk): tune by dragging a real window. `Mark`/`MarkProgress` replace using only `ReactProgress` for drawing (a "!" via `PetWindow.Present(..., startled)`).
The mojibake doc comment in `PetWindow.Present` is fixed.

## Phase 7 — Pets: companions, stroking, Stay and Follow

- [x] 7.1 **`Core/Pets/PetSocial.cs`** (pure, tested), called by `PetManager` every 10 ticks with the brains. Two pets on the same surface (`Owner` and `At`), both free (Stand, Walk, Run, Sit), within `90 * _ps`, past their 60 s pair cooldown (calm 120 s, lively 30 s), neither on Stay or Follow: both `Meet(otherX)`: face each other, hearts for about 2 s. Then 40% both sit for 5–15 s (only with `Caps.Sit`); 30% one follows the other for up to 15 s, keeping a `70 * _ps` gap (`PetState.FollowPet`, given the target's X each tick; ends when the target leaves the surface); 30% they walk off in opposite directions.
- [x] 7.2 New setting `pets.socialize: boolean` (default true), toggle "Play together" in the Pets tab's Behaviour card, shown with 2+ pets. Contract, normalize, mock-bridge. `PetManager` holds the pair cooldowns; brains stay ignorant of each other.
- [x] 7.3 **Stroke:** `PetWindow` `MouseEnter`/`MouseLeave` (layered alpha hit-testing means only the visible pet counts). `PetManager`: the cursor on the pet for 0.6 s, not pressed: `BeginStroke()`; leaving (or a press): `EndStroke()`. New `PetState.Stroked` (floor states only; hearts looping, stands or sits still), returning to `Stand` on end. Should end by itself after 30 s.
- [x] 7.4 **Stay and Follow:** brain flags `Stay` (`ChooseNext` only offers Stand, Sit, Lie, Sleep; on a wall or ceiling it holds on indefinitely; falling and being dragged still happen, then it stays where it ends up) and `FollowCursor` (for 60 s or until toggled or clicked: tracks the cursor's X along its floor, lets go of walls, steps off edges rather than turning back; no climbing).
      Stay and Follow cancel each other. The right-click menu becomes: Sit · Sleep · — · Stay here (checkable) · Follow me (checkable) · — · Hide pets · Pet settings.
      Session only, not saved: keep them on `PetManager.Pet`, and re-apply in `Reset`, which builds a new brain.
- [ ] 7.5 **Tests:** meeting rules, cooldown, Stay never walks and holds a wall across thousands of ticks, Follow ends after 60 s, stroke end, a Stay pet falling then staying. Real check with two test pets, light and dark settings, at 1000×680 and 820×560. README pets section, contract, `TESTING.md` §8. Commit.

Notes:
Done in code and tests; real-screen check (7.5) NOT done. `PetSocial` (pure) holds pair cooldowns and followers; `PetManager.StepSocial` calls it every 10 ticks.
`pets.socialize` is new (default true, shown with 2+ pets). Stay/Follow are session flags on `PetManager.Pet`, re-applied in `Reset`. Stroke starts after 0.6 s of
`MouseEnter` on the layered window (not yet seen working on a real pet).

## Phase 8 — Ship

- [x] 8.1 `tools\build.ps1` (tests + `dist\Hanami.exe`), relaunch the dev copy normally (`--dev --startup`, no debug port), restore any dev-settings changes made for testing, commit, tell the user `dist\Hanami.exe` is updated and its size.
- [x] 8.2 Give the user the list for the real desktop: sign-in slide; lock image match per wallpaper type; Restore; second monitor, especially one stacked above the laptop, with a different DPI, on the other laptop; shake thresholds with a real pack (`uma` frames: jump, dangle fallbacks); any performance change on Intel graphics during the slide.

Notes:
Done for what is built (phases 1, 2, 4-7). Phase 3 (lock screen sync) is NOT started: it waits for the user's go-ahead before the first real `SetImageFileAsync` call (3.1). `dist\Hanami.exe` rebuilt (72.7 MB, 221 tests). Dev copy relaunched with `--dev --startup`. A smoke run with two lively test pets ran about 50 s with no errors in the log; dev settings and the test pack were restored/removed. Real-desktop checks for 2.6 and 4.7-7.5 are still open.
