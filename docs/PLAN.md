# Implementation plan — round 5: desktop pets

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
- Frame meanings come from Shimeji-ee's default `conf/actions.xml` (see 1.1), stored as a C#
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

## Phase 1 — Pet packs: format, import, library, settings (no pets on screen yet)

- [ ] 1.1 Get the frame meanings: fetch Shimeji-ee's default `conf/actions.xml`
      (https://github.com/gil/shimeji-ee or https://github.com/chaoskagami/shimeji-ee, `conf/`)
      into the scratchpad. From it, write `ShimejiFrames.cs`: for each animation Hanami needs —
      Stand, Walk, Run, Sit (+ sit variants), Sprawl/Lie, Sleep (if present), Fall, Land/Bounce,
      Dragged (with its left/right swing frames), Thrown, GrabWall, ClimbWall, GrabCeiling,
      ClimbCeiling, Pinched/reaction, ChaseMouse — the list of `(frame number, duration in 1/25 s
      ticks, anchor x/y, velocity x/y)` exactly as the XML gives them. Note the source commit in a
      comment. Don't copy the XML into the repo.
- [ ] 1.2 `CONTRACTS.md` first:
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
- [ ] 1.3 `Core/Pets/PetPack.cs` + host import (follow `Library`'s style, reuse its helpers):
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
- [ ] 1.4 Export/Import packs: pet packs become export items (`kind: "pet"`); importing restores
      them and their settings items. Test the round-trip.
- [ ] 1.5 Settings UI, minimal: new tab **Pets** between Screensaver and Power. Header (action:
      Import pet) · card "Your pets": the items (preview = shime1, name, Remove) and an "Add a pet"
      picker over the imported packs · empty state explaining where packs come from (Shimeji packs —
      search for "<character> shimeji"; check each pack's terms). mock-bridge support.
- [ ] 1.6 Check: import a real pack (**ask the user where theirs is** — import into the dev root,
      never the real install), it appears with its preview; a broken folder gives a clear error.
      Commit.

Notes:

## Phase 2 — Pets on screen: ground life, drag and throw

World = each monitor's **work area** only in this phase (floor = top of the taskbar, walls = work
area sides, ceiling = work area top).

- [ ] 2.1 `World.cs`: `Surface { Kind: Floor | Wall | Ceiling, From, To, At, Side, OwnerHwnd? }` in
      physical px, built from `Monitors.Enumerate()` work areas. Queries: floor under a point,
      first floor crossed by a movement segment (for falling), nearest wall in a direction.
- [ ] 2.2 `PetBrain.cs`: states Stand, Walk, Run, Sit, Lie, Sleep, Fall, Land, Dragged, Thrown,
      React. Fixed step at 25 Hz (like Shimeji, so frame durations are exact); the window draws the
      latest state. Gravity + terminal velocity for Fall/Thrown; Thrown keeps the release velocity,
      bounces off walls with damping, lands on a floor. Idle choices weighted by `activity`
      (calm = more sitting/sleeping, lively = more running); Sleep only after a long calm stretch.
      Walking turns at walls. Deterministic with an injected `Random`.
      Tests: falls and lands on the floor, stays inside the work area, thrown at a wall bounces,
      dragged follows the input exactly, a long run (10 simulated minutes, many seeds) never leaves
      the screen or produces NaN.
- [ ] 2.3 `PetWindow.cs`: WPF window as above; frames loaded once per pack as frozen
      `BitmapSource`s (shared by every pet using that pack); drawn at `scale × monitor DPI scale`
      (frames are authored for 100%); flipped horizontally when facing right (Shimeji frames face
      left). Position = pet position minus the frame's anchor. One `SetWindowPos`
      (`SWP_NOACTIVATE | SWP_NOZORDER`) per change, nothing when nothing changed.
- [ ] 2.4 Mouse: left-press on the pet → Dragged (the cursor holds the frame's anchor point, the
      swing frame follows cursor velocity); release → Thrown with the last ~80 ms of cursor velocity.
      A click without moving (< 4 px) → React. Right-click → small menu: Sit · Sleep · Hide pets ·
      Pet settings. The pet window must never take focus (typing in the app underneath keeps
      working while you drag).
- [ ] 2.5 `PetManager.cs`: a window per settings item; each pet spawns falling from the top at a
      random spot on the primary monitor; one `DispatcherTimer` for all pets, stopped when no pet is
      visible. Tray menu: "Show pets / Hide pets" (this session only, not saved). Settings changes
      (add/remove/scale/enabled) apply without restarting the other pets.
- [ ] 2.6 Check on the real screen (`capture-desktop.ps1`; synthetic drag via `input.ps1` if it can,
      else ask the user to try): pets fall and land on the taskbar, walk, sit; drag and throw work;
      clicks on transparent pixels next to a pet reach the app below. Perf with 3 pets: well under
      1% CPU for the pets' part, no memory growth over 10 min. Commit.

Notes:

## Phase 3 — Windows as terrain: stand, climb, get carried

- [ ] 3.1 `Platform/WindowScan.cs`: top-level windows in z-order that are visible, not minimized,
      not cloaked (`DWMWA_CLOAKED`), not tool windows, not ours, not the shell's desktop/taskbar
      windows, at least ~120×60 px; bounds from `DWMWA_EXTENDED_FRAME_BOUNDS` (`GetWindowRect`
      includes the invisible resize border). Maximized windows give no top-edge floor but still
      cover what's behind them.
- [ ] 3.2 `World.cs`: per window, a floor on its top edge and walls on its sides — **only the parts
      not covered by windows above it** in z-order (interval subtraction). Pure; tests with
      overlapping rectangles: fully covered edge → no surface, partly covered → split segments.
- [ ] 3.3 Keeping it fresh: rescan every 500 ms, plus out-of-context `SetWinEventHook` for
      `EVENT_OBJECT_LOCATIONCHANGE` (window objects only), `EVENT_SYSTEM_MOVESIZESTART/END`,
      `EVENT_SYSTEM_MINIMIZESTART`, `EVENT_OBJECT_DESTROY`/`HIDE`, `EVENT_SYSTEM_FOREGROUND`
      (z-order). The window a pet stands or climbs on moves → **carry** the pet by the same delta
      that tick; its surface disappears (minimized, closed, now covered) → Fall. At most one
      hook-driven rescan per tick.
- [ ] 3.4 `PetBrain` climbing: at a wall, GrabWall → ClimbWall up/down; at the top of a window side
      it can step onto that window's top edge; at the work-area top, GrabCeiling → ClimbCeiling,
      eventually letting go → Fall. Walking off the end of a window top → Fall (sometimes a jump down
      to a lower floor). Only when the pack has those frames and `climbWindows` is on (off = work
      area only, as in phase 2). Tests on hand-built worlds.
- [ ] 3.5 Pets are topmost, so a pet on a window that later gets covered would float in front of
      the covering window. The interval subtraction removes covered edges, so it falls instead —
      classic Shimeji behaviour; accept it and note it.
- [ ] 3.6 Check on the real screen with a couple of ordinary windows: pets land on window tops,
      climb sides, ride along when the window is dragged (**ask the user to drag one**), fall when
      it's minimized. Confirm Hanami moved, resized or activated no user window. Commit.

Notes:

## Phase 4 — Living with the rest of Hanami: power, fullscreen, screensaver, monitors

- [ ] 4.1 Per pet, its current monitor decides: a fullscreen app there (`CoverageProbe`) → hide
      that pet; that monitor's mode `paused` (from `ModeResolver`: manual pause, battery rules,
      maximized rule) → freeze on the current frame, still visible; `reduced` → 15 fps cap.
      Screensaver showing → hide all pets (never above it) and restore them where they were when it
      ends. Session locked → hide all, stop the timer.
- [ ] 4.2 Multi-monitor: the world is the union of all monitors; pets walk across where work areas
      touch and fall onto a lower monitor's floor; a pet outside every monitor (monitor unplugged,
      resolution change) respawns falling on the primary. Per-monitor DPI: when a pet crosses to a
      monitor with another scale, resize its window explicitly (don't rely on WPF's automatic
      DPI-change resize for a layered window). Unit-test the world maths with two monitors at
      different DPI; real check on this laptop's single screen, and note the external-monitor check
      for the user's other laptop.
- [ ] 4.3 Resume from sleep and Explorer restarts (`TaskbarCreated` registered message): re-read
      work areas and rescan; pets fall to the new floor.
- [ ] 4.4 Check: screensaver preview with pets out → none over it, back in place after; manual
      pause → pets freeze; fullscreen video in a browser → that monitor's pets vanish and come back.
      Commit.

Notes:

## Phase 5 — Character and polish

- [ ] 5.1 Chase the mouse: now and then (more often with `lively`) a pet walks or runs towards the
      cursor along its current floor and stops next to it (ChaseMouse frames if the pack has them).
- [ ] 5.2 Reactions: click → React (Pinched frames) with a few small hearts drawn inside the same
      pet window (no extra window); a pet dragged around for a while sprawls dizzily after landing.
- [ ] 5.3 Settings UI: card "Behaviour" — Size slider (`scale`), Activity segmented (Calm · Normal ·
      Lively), Climb windows toggle, master "Show pets" toggle; each pet row gets "Call here" (falls
      from above the centre of the primary monitor) and capability chips (Climbs · Ceiling · Sits).
      Pack rename. Check at 1000×680 and 820×560, light and dark.
- [ ] 5.4 README (pets section, where to find packs, licences belong to the pack authors),
      CONTRACTS final pass, a pets section in `docs/TESTING.md`. Commit.

Notes:

## Phase 6 — Ship

- [ ] 6.1 `tools\build.ps1` (tests + `dist\Hanami.exe`), relaunch the dev copy normally
      (`--dev --startup`, no debug port), commit, tell the user `dist\Hanami.exe` is updated.
