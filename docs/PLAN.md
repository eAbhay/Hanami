# Implementation plan — round 7: lock screen without the flash, frame picker, GitHub releases

**Current scope (user's decision, 4 Nov 2026): do phase 1 only, the lock screen bug fix.**
- Phases 2 and 3 are on hold. Don't start them, and don't offer to, until the user asks.
- **Commit nothing until the user has tested the fix and said the bug is gone.** This overrides the usual "commit at the
  end of each phase" rule. Leave the work uncommitted on the current branch, and don't create a commit "to save progress".
- The user tests the bug themselves (step 1.10). Stop there and wait for them. Automated checks don't count as fixed.

How to use this file (for a fresh chat, e.g. Sonnet):
1. Read `CLAUDE.md`, then this file. In `docs/CONTRACTS.md`, read the sections your phase names and search it
   for any setting or message you change. Read `docs/TESTING.md` before testing.
2. Do **one phase per chat**, in order. Each phase ends with a working build, its checks done, boxes ticked here,
   and, once the user has confirmed it where the phase says so, one commit.
3. Don't redesign beyond what a phase says; if something here turns out wrong, note it under the phase's "Notes"
   and pick the smallest fix that keeps the intent.
4. Keep this file UTF-8. Edit it with the file tools, not PowerShell `Set-Content`/`Out-File`.
5. A change includes its tests, comments, `docs/CONTRACTS.md`, `README.md` and settings-UI copy.
   Change the contract first, then both sides. `web/settings/mock-bridge.js` must keep working.

Earlier rounds are in git history (`git log -- docs/PLAN.md`). Still open from round 6, for the user on the real
desktop: pets with a real pack, built-in scenes and a theme flip on the lock screen, second monitor and DPI.

**Safety rules for this round**
- Never call `SetImageFileAsync` on the user's real lock screen, and never call `LockWorkStation`, without asking
  in that chat first. In dev, test with `syncLockScreen` off or with a fake `ILockScreenImage`.
- Never push, tag, merge to `main` or publish a release without the user's yes in that chat.

## Decisions (don't re-ask)

- **Order:** the flash fix first, on its own. The frame picker and the first GitHub release stay on hold until the user asks for them.
- **Approach to the flash:** a lock picture is saved per wallpaper ahead of time, and Hanami stops photographing the
  screensaver altogether. Hanami no longer opens any window to refresh the lock screen.
- **The lock picture does not change between screensavers.** It is one chosen frame, not a new one each time, and it
  doesn't have to match the frame the screensaver stops on (the user is fine with this).
- **Default frame:** the same time thumbnails use (1 s or 10% in, whichever is earlier), not literally frame 0: many
  anime loops open on black or mid-fade. The slider can go to 0.
- **Still no registry or policy routes** (round 6 decision). Spotlight still overrides the picture.

## What is already diagnosed

**The flash (read from the code, not reproduced; the user's test in step 1.10 settles it).** `LockScreenSync.RefreshAsync`
runs when the option is turned on, the screensaver wallpaper or the theme changes, and at every app start (`_lastSignature`
is null then, see `HanamiHost` constructor). It creates `WallpaperView.CreateCaptureAsync`: a window really on the monitor, not
topmost but above other normal windows, relying on 1/255 layered alpha to be invisible. WebView2 draws through its own child
window and composition, which very likely ignores the parent's layered alpha, so the page shows for the second or so the capture takes.
Round 6's check only confirmed the captured image was clean, not that the window was invisible. The screensaver-start path
(`holdForCapture`) causes no window but holds the clock back about 1.2 s; it goes away too.

**Why this is cheap to fix.** `web/settings/thumbs.js` already loads each video in a hidden `<video>` in the settings page,
seeks, draws to a canvas and sends a JPEG to the host (`saveThumbnail` → `Library.SaveThumbnail`). `docs/CONTRACTS.md` confirms `library.hanami` media can
be exported to a canvas. It decodes with the same engine as the wallpaper, so any video that plays gets a picture. No ffmpeg, no native
decoding, no new window. A slider on top of that is a range input setting `video.currentTime`.

**Copyright audit for the release (done).** Tracked media are only `src/Hanami.App/Assets/hanami.ico` (drawn by
`tools/make-icon.ps1`) and `web/wallpaper/fonts/TiltWarp.ttf` (SIL OFL, `OFL.txt` beside it and embedded in the exe). The largest blob in
all of git history is that font, so no video or image was ever committed. `devroot/` and `dist/` are ignored, and the exe embeds only
`web/` and the icon, so the user's imported media are not in it. Built-in scenes are drawn by code. No secrets or personal paths found.
Pet frame timings in `Core/Pets/ShimejiFrames.cs` are from Shimeji-ee's `conf/actions.xml`: permissive licence (Yuki Yamada, 2009; "New BSD" per
its maintainer), acknowledgment optional. Credit it anyway in phase 3.

---

## Phase 1 — Lock pictures saved ahead of time (the flash fix)

Read: `App/LockScreenSync.cs`, `Core/LockImagePolicy.cs`, `App/ScreensaverManager.cs` (`SendConfig`, `CaptureForLockScreenAsync`, the `readyForCapture` branch),
`App/WallpaperView.cs` (`CreateCaptureAsync`, `CaptureAsync`, `holdForCapture`), `web/wallpaper/wallpaper.js` (capture handshake),
`web/settings/thumbs.js`, `Core/Library.cs` (`SaveThumbnail`), `web/wallpaper/scenes.js` (`Scene`), CONTRACTS "Lock screen".

- [ ] 1.1 **Contract first.** New bridge method `saveLockStill {id, dataUrl, frameSec?}`; `<wallpaper dir>/lock.jpg`; manifest `lockFrameSec` and `lockFit`; state status
      `"waiting"` (no picture for the chosen wallpaper yet). Rewrite "Lock screen"; remove `holdForCapture`, `captureDone`, `readyForCapture`, `&capture=1`.
- [ ] 1.2 **Settings page, new `web/settings/lockstills.js`,** sharing the helpers in `thumbs.js` (export `once`, `withTimeout`, seek): for every video and image without a current
      still, queued one at a time like `ensureThumbnails`, draw at the primary monitor's pixel size honouring the wallpaper's `fit` (black bars for `contain`), JPEG quality about 0.92,
      send `saveLockStill`. A wallpaper whose `fit` differs from `lockFit` is redone at its `lockFrameSec`. Call it wherever `ensureThumbnails` is called, so imports (media and packs) and existing libraries are both covered.
- [ ] 1.3 **Built-in scenes:** draw with `Scene` from `/wallpaper/scenes.js` on an unattached canvas, one frame (its layers are seeded, so it is repeatable). Saved under
      `<config>/lockscreen/builtin-<scene>.jpg`. If `Scene` needs layout (`clientWidth`), give the canvas an off-page size rather than changing the wallpaper's behaviour.
- [ ] 1.4 **Host:** `Library.SaveLockStill` (validate the data URL as `SaveThumbnail` does; write to a temp file, then move). Deleting a wallpaper removes it with the folder. Packs don't export `lock.jpg` (the importer regenerates it), but do carry `lockFrameSec` in the manifest if that's no extra work.
- [ ] 1.5 **`LockScreenSync`:** no capturing. `RefreshAsync` finds the still of the primary monitor's screensaver wallpaper; none → status `"waiting"`, keep the current picture, write nothing. Otherwise copy to `current.jpg` and call `ApplyAsync`.
      `saveLockStill` calls `RequestRefresh`. Persist the signature (`<config>/lockscreen/`) so an app start doesn't rewrite the picture. Signature = wallpaper id + dark + the still's write time.
- [ ] 1.6 **Remove the capture path:** `CreateCaptureAsync`, `CaptureAsync`, `holdForCapture`, `CaptureForLockScreenAsync`, `WantsCaptureOnStart`, `CaptureFromScreensaverAsync`, `LockTrigger`, page `isCapture`/hold/`captureDone`, the clock's `hold`/`release` if nothing else uses them. Check `_blocked` still earns its place.
- [ ] 1.7 **Tests:** replace `LockImagePolicyTests` for the signature-only rule (no rewrite when unchanged, rewrite when the id, theme or still changes, none when off). Library: `SaveLockStill` rejects bad data, manifest round trip. Mock bridge: `saveLockStill`.
- [ ] 1.8 **Copy:** Screensaver tab card and confirm dialog drop "updated each time it starts"; add a `"waiting"` line. README section.
- [ ] 1.9 **Check, automated in dev (sync off):** still files look right for a video, an image, each built-in scene, and a `contain` wallpaper. Screensaver shows its clock at once. Tracing the window list while changing wallpaper shows no new Hanami window.
- [ ] 1.10 **The user tests the bug. Stop here and wait.** Build, start the app for them (`--dev`, see `docs/TESTING.md`) and give them these steps. This writes to their real lock screen, so ask first, and they turn the option on themselves:
      1. Screensaver tab: turn on "Match the lock screen to the screensaver". No flash.
      2. Change the screensaver wallpaper a few times (video, image, built-in scene). No flash, and the status line updates.
      3. Flip the theme, if light and dark use different screensaver wallpapers. No flash.
      4. Quit and restart the app. No flash.
      5. Import a new video and pick it as the screensaver wallpaper. No flash.
      6. Win+L: the lock screen shows the right picture, with no clock or particles. Repeat for an image and a built-in scene.
      7. Let the screensaver start (or Preview): its clock appears at once, with no flash.
      If they still see a flash, ask what it looked like and when, and fix that. Don't commit, and don't tick this box. The diagnosis above was read from the code, so it may be incomplete.
- [ ] 1.11 **Only after the user says the bug is gone:** tick the boxes, write the Notes and make one commit. Don't push. Then stop: phases 2 and 3 stay on hold.

Notes:

## Phase 2 — Choose the frame (on hold until the user asks)

Read: `web/settings/tabs/screensaver.js` (the Lock screen card), `web/settings/lockstills.js`, `web/settings/style.css` (existing slider rows).

- [ ] 2.1 **Contract:** `lockFrameSec` is exposed on `WallpaperInfo`; document the picker.
- [ ] 2.2 In the Lock screen card, when sync is on and the primary monitor's screensaver wallpaper is a **video**: a live `<video>` preview (shown with the wallpaper's fit, primary monitor's aspect), a range slider over its duration, and **Use this frame** / **Reset**.
      Dragging seeks, dropping pending seeks so it stays smooth on 4K. Save only on the button: a full-resolution draw plus `saveLockStill {frameSec}`, which triggers a refresh. For images and built-in scenes, show the picture with no slider.
- [ ] 2.3 Light and dark: the picker follows the wallpaper the current theme uses. Frames are per wallpaper, so nothing else to store.
- [ ] 2.4 Unload the preview `<video>` when the tab or card goes away (`thumbs.js` shows how), so a 4K decode doesn't run in the background.
- [ ] 2.5 Mock bridge support. Check in the browser (mock) and the dev app. With the user: pick a frame, Win+L. Commit.

Notes:

## Phase 3 — First GitHub release (on hold until the user asks)

Read: `tools/build.ps1`, `src/Hanami.App/Hanami.App.csproj`, `App/AppPaths.cs` (`Version`), `App/Installer.cs` (replacing an installed copy), README "Licence".

- [ ] 3.1 **Notices:** `THIRD-PARTY-NOTICES.md` in the repo and shipped under `web/` so it is in the exe: Tilt Warp (OFL), .NET runtime and WPF/WinForms (MIT), WebView2 SDK (Microsoft's licence; check its redistribution terms and quote accurately), Shimeji-ee frame timings (Yuki Yamada). Link it from the README. Link it from the settings About area if there is one.
- [ ] 3.2 **`CHANGELOG.md`:** one entry per version, written for users. First entry summarises what the app does.
- [ ] 3.3 **Version from the tag:** `tools/build.ps1 -Version x.y.z` passes `-p:Version=`; the csproj keeps its value for local builds. Print a SHA-256 for the exe into `dist/Hanami.exe.sha256`.
- [ ] 3.4 **`.github/workflows/release.yml`:** on a `v*` tag, on `windows-latest`: setup .NET per `global.json`, run `tools\build.ps1 -Version <tag>`, and create a release named after the tag, with the matching changelog section as notes and `Hanami.exe` plus the checksum attached. `permissions: contents: write`, actions pinned. Tests failing means no release. Also a `workflow_dispatch` dry run which uploads a build artifact without releasing. Confirm the tests pass on a clean runner (no `devroot`, no ffmpeg).
- [ ] 3.5 **README:** a **Download** section at the top linking to the latest release, requirements (Windows 11 x64, WebView2 runtime: built into Windows 11), and how to update (download the new exe and run it: `Installer` replaces the copy). Explain the SmartScreen warning: the exe isn't code-signed, so choose More info → Run anyway, and compare the checksum. Add a screenshot or short GIF (made from original or demo content only, not imported third-party art).
- [ ] 3.6 **With the user's yes:** merge `round-6` and this round into `main` (`main` has nothing `round-6` lacks, 9 commits behind today, so a fast-forward works), push, run the dry run, download its exe and test it from a temp folder per `docs/TESTING.md`.
- [ ] 3.7 **With the user's yes:** tag `v0.1.0`, push the tag, check the published page and download. From now on: bump the changelog, tag, done.

Notes:

---

## Future plans (not in this round; each needs its own planning)

**Hanami's own Shimeji packs.** Feasible; the app needs nothing new. Format is `shime1.png`…`shime46.png` at 128×128 facing left, and any frame a pack lacks falls back to `shime1` (`PetBrain`), so a pack can start with about 12 key poses and grow. Plan: pick one original character (no existing anime characters, so it can be bundled and redistributed freely), write a pose sheet mapping frames to `ShimejiFrames`, make a small tool that turns a sprite sheet into a pack and previews it, and bundle a first pet as a starter. The hard part is art: keeping one character consistent across poses.

**Other ideas, in the order suggested:** update check against GitHub Releases (tray notice, no silent installs) · code signing to remove the SmartScreen warning · a playlist per monitor, rotating wallpapers by time or time of day · drag and drop import onto the settings window · the in-wallpaper Live2D character, still not started.
