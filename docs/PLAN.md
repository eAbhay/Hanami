# Implementation plan — round 10: reorganise the settings tabs

**Current scope (user's decision, 4 Nov 2026): round 10 below, on branch `round-10`.** Rounds 8 and 9 are finished and stay
for their Notes. Round 7 is finished except for what round 8 took over.

## Round 10 — settings tabs reorganised

Settings UI only: no setting, message or host change, so `docs/CONTRACTS.md` should not need edits (search it to confirm).

**Decisions (don't re-ask):**
- Light and dark moves to Power because it is shared by the Wallpaper and Screensaver tabs, so it shouldn't sit on either. It goes last on the page, after Frame rate, so it doesn't interrupt the power flow or read as a power rule.
- The tab stays named Power, with the same id and subtitle.
- The Screensaver tab loses its link to Power and gets no replacement text. Wallpaper has no pointer to its power and frame rate settings either, and the two tabs should match. Screensaver on/off and timing stay on Power.
- Pets get no battery option. They already follow the power rules (`PetManager.ModeOf`), so only the wording changes.

**Rules:** work only on `round-10`. No commit, merge, push, tag or version bump without the user's yes in that chat.

### Phase 1 — Move the settings

- [x] 1.1 **Light and dark to Power.** In `web/settings/tabs/general.js`, `createGeneralTab`, take out `split`, `mode`, `windowsNow`, `modeBox`, the "Light and dark" card and their lines in `update`. Add them to `web/settings/tabs/power.js`, `createPowerTab`, as the last card, with the same copy and behaviour. Update both files' header comments if they go stale. General ends up as Startup, Updates, Storage, Uninstall, About, notices.
- [x] 1.2 **Sign-in to the Lock screen card.** Move `signIn` and its row and `update` line from `createPowerTab` to `createScreensaverTab` in `web/settings/tabs/screensaver.js`. Save it with that tab's `save` helper. Make it the card's first row, followed by a divider, because the status, frame picker and buttons below all belong to the "Match" row. Drop "To match that lock screen to the screensaver, see the Screensaver tab." from its description. In Power, check that the Screensaver card's rows and rules still look right without it.
- [x] 1.3 **Remove the Power link.** Remove `toPower` from `createScreensaverTab` and the `.link-row` styles from `web/settings/style.css`. `ctx.showTab` may then be unused: remove it only if nothing else calls it (check `web/settings/app.js`).
- [x] 1.4 **Pets order.** In `web/settings/tabs/pets.js`, `createPetsTab`, the order becomes Your pets, Pet library, Behaviour.
- [x] 1.5 **"Keep running on battery" wording.** In `createPowerTab`, `keepCard`, the description becomes: "By default Hanami pauses wallpapers and pets when you unplug to save battery. Turn this on to keep them running at full quality." The screensaver isn't mentioned because it never shows on battery.
- [x] 1.6 **Docs.** `README.md`'s light and dark bullet says "Turn it on in General": change it to Power. Add a `CHANGELOG.md` entry following its format, with the version left for the user to decide.
- [x] 1.7 **Check** with the mock bridge, in a plain browser (`docs/TESTING.md`):
      - General, Power, Screensaver and Pets show the new layout, with no console errors.
      - Turning light and dark on in Power shows the mode choice there and the Light | Dark switch on Wallpaper and Screensaver. Turning it off hides them.
      - Require sign-in saves from its new place, and the Preview hint on the Screensaver tab still reads correctly.
      - The update banner and General's tab dot still work.
      - `dotnet test --project src\Hanami.Tests\Hanami.Tests.csproj` and the Debug build still pass (no C# should change).
      - Then ask the user to look at it before any commit.

Notes: 1.1-1.6 done; 1.7 checked on the mock (4 Nov 2026): General, Power, Screensaver and Pets layouts, light/dark toggle (mode choice on Power; switch on Wallpaper and Screensaver, gone when off), Require sign-in saves from the Lock screen card, Preview hint unchanged, update banner and General dot. 328 tests pass, Debug build clean (a running dev copy had to be quit first). `ctx.showTab` was unused after 1.3 and is removed; CONTRACTS.md needed no edit. Merged to `main` and released as `v0.1.2` (5 Nov 2026).

After that, on the user's request (5 Nov 2026), the Lock screen card got less text: a two-line "Match" description, "Uses your main monitor's wallpaper" in the status line while on, the Picture-not-Spotlight hint on the left beside **Lock screen settings…**, and a header over the frame picker instead of its note ("Pick your lock screen" for a video, "Your lock screen" otherwise). The user checked this and the tab reorganisation in the dev app (5 Nov 2026) and confirmed they work.

**Tray install checked for real (5 Nov 2026):** on a temp install of `v0.1.1` with an empty library, the user chose **Update available: install 0.1.2** in the tray menu. It downloaded from GitHub, verified, swapped and restarted on 0.1.2; the exe matched the published checksum, Apps & features showed 0.1.2, update settings were kept and no files were left over. The temp install was then removed. Not covered: a real library across a tray update (the 0.1.0 update test kept settings and library).

## Round 9 — "update available" is hard to miss (ships as `0.1.1`)

Done 4 Nov 2026: `v0.1.1` is released as Latest (tag on 824823c, run 41873520842, checksum and stamped version checked on the download; no real install of it run yet). **User's decision:** the balloon shows once more about 3 days after the first if the version is still waiting. This replaces round 8's "one notification per version". With `autoCheck` off there is no reminder.
Notes: the rule is in `Core/UpdateAttention`; tray in `App/TrayIcon` (`ShowUpdate`, `WithDot`) and `HanamiHost.OnUpdateChanged` / `InstallUpdateFromTray`; tab dot in `web/settings/app.js`.
Differed from the brief: a failed **Check now** used to drop the offer, which would have removed the dots, so it now keeps the offer (status `available` + `error`). An automatic check finishing during a download no longer overwrites `downloading`. Added an "Updating Hanami" balloon when the tray click starts an install, because the click otherwise shows nothing.
Checked in the dev copy with a local feed and a throttled asset server (wrong checksum, so no handover): dot on the icon, tooltip, menu items, newer version after a skip, failed check, tray install with progress in the menu, failure balloon, *View details*. Mock bridge: all tab dot states.
Unverified: the download and failure tooltip lines on screen (unit-tested; only the "available" line was read back), a real tray-started install through to the restart, a tray click on a real release.
Trap: Windows 11 puts Hanami's icon in the hidden-icons flyout unless the user pins it, so the dot is only seen on the taskbar once pinned. Hanami doesn't touch that setting.

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

## Round 8 safety rules

- Never call `SetImageFileAsync` on the user's real lock screen, or `LockWorkStation`, without asking in that chat first.
  In dev, use a fake `ILockScreenImage` or `syncLockScreen` off.
- Never push, tag, merge to `main` or publish a release without the user's yes in that chat. Each tag needs its own yes.
- Installs touch `HKCU\Software\Hanami`, so ask first and follow `docs/TESTING.md`. Check each UI automation step
  worked before clicking Install: round 7's test once installed to the real folder by accident.

## Round 8 decisions (don't re-ask)

- **Order:** the lock screen fix, then updates, then the release. Nothing is tagged until phase E, so `v0.1.0` ships with the updater.
- **Channels split by stability, not by major or minor.** Stable gets every normal release (`v0.2.0`, `v0.2.1`). Beta gets those plus
  prereleases (`v0.3.0-beta.1`). The user keeps Stable quiet by publishing small changes as betas. Nobody is offered a lower version:
  someone moving from Beta to Stable stays put until Stable passes them.
- **The check lives in the app:** no scheduled task or script. About 60 s after start, then once 24 h have passed since the saved last
  check (a tick about hourly, so sleep doesn't matter). Never in `--dev` unless a test feed is given. No telemetry.
- **Updates need a click:** no silent installs. One-click means download, verify, swap and restart. The notice is one tray notification per
  version, a lasting tray menu item, and a settings banner with notes, size, **Update** and **Skip this version**.
- **Setting:** "Tell me when updates are available", on by default, in General settings and as a ticked checkbox in `SetupWindow` under
  the startup one. Off stops automatic checks only; **Check now** always works. There is also a Stable/Beta choice.
- **Security for now:** HTTPS and SHA-256, plus 2FA on the user's GitHub account. The checksum catches corruption, not a hacked account.
  Signed updates can come in a later version. No fixed list of download hosts: GitHub redirects assets to hosts it has changed before.
- **Frozen once `v0.1.0` is out (old copies depend on them), recorded in `docs/CONTRACTS.md`:** the repo's releases URL, the asset names
  `Hanami.exe` and `Hanami.exe.sha256`, the checksum format, and the `--apply-update` flag.

---

## Phase A — The lock screen really updates

Read: `App/LockScreenSync.cs` (`RefreshAsync`, `ApplyAsync`, `RestoreAsync`), `Platform/LockScreenImage.cs`, `Core/LockImagePolicy.cs`, `Hanami.Tests/LockScreenSyncTests.cs` (`FakeLockScreen`), CONTRACTS "Lock screen".

**Already diagnosed and tested on the real lock screen (4 Nov 2026), don't redo.** Windows ignores a file at a path it already knows unless
the file's date is newer, and `SetImageFileAsync` still succeeds. `RefreshAsync` uses `File.Copy`, which keeps the still's old date, so the log says
"updated" while Windows keeps its old picture. Results: same path + older date, not taken; same path + same date + different picture, not taken;
same path + fresh date, taken; new path + older date, taken. A new filename each time isn't needed. Not tested: going back to a path
Windows already knows with an unchanged date, which is what putting `previous.jpg` back does.

Comparing pictures: read through `LockScreen.GetImageStream`, scaled to 32×20, mean RGB difference per channel. The right picture scored 0.2 to 0.7;
the nearest wrong one in the library scored 45. Windows re-encodes, so bytes never match. Windows may also crop other aspect ratios (a 2200×1238 cached
size scored 9.6, though the stream itself scored 0).

- [x] A.1 **Contract first:** add the date rule and the read-back to "Lock screen". Fix the `restoreLockScreen` row, which still says Hanami never restores by itself.
- [x] A.2 **Fresh date:** in `LockScreenSync`, set a fresh write time on the file just before every `_api.SetAsync`, in `ApplyAsync` and `RestoreAsync`. Keep it in `LockScreenSync`, not the wrapper, so the fake can test it. The signature uses the still's date, so it doesn't change.
- [x] A.3 **Read-back:** after each set, save the picture with `_api.BackupAsync` to a temp file and compare. A pure comparison goes in `LockImagePolicy`, decoding stays out of `Core/`. Threshold about 15; retry for up to about 5 s. No match: status `failed` with a plain message, no signature saved. If Windows gives no picture back, log it and carry on as unverified. Check how this behaves with Spotlight on, where the call succeeds but Spotlight shows; the message should mention the Picture setting if so.
- [x] A.4 **Tests:** make `FakeLockScreen` follow the date rule and return the picture it holds. Changing wallpaper must really change it. A fake that ignores the write must end as `failed`. Put-back is covered too.
- [x] A.5 **The user tests. Stop and wait.** Ask first, then give them these steps: Win+L after changing wallpaper (video, image, built-in scene), after a theme flip, after a restart, and after *Use this frame* (closes round 7's 2.5). Turn the option off and quit: is the previous picture back? Also ask if they expected the burning tree: Windows is in dark mode, so `the-burning-of-irminsul` is the right picture, and Furina Rain is the light-mode choice.
- [x] A.6 **Only after the user says it works:** tick the boxes, write Notes, one commit. Also tick round 7's 2.5.

Notes: user confirmed on the real lock screen (4 Nov 2026) that all checks pass. `SetVerifiedAsync` / `VerifyAsync` in `App/LockScreenSync.cs`; the pure comparison is in `Core/LockImagePolicy`; `ILockScreenImage` gained `SampleAsync` (32x20 RGB, WPF decode in `Platform/LockScreenImage`).
Differed from the plan: `ApplyAsync` is `internal` so tests can reach it (the refresh path needs a dispatcher and monitors). Read-back timeout and interval are properties so tests shorten them.
Unverified: the Spotlight case (read-back result not observed). Still unanswered: whether the user expected the burning tree (dark-mode picture is correct). Storage: one `lock.jpg` per wallpaper, plus `current.jpg`, `previous.jpg`; Windows' own cache is not measured.

## Phase B — Check and notify

Read: `App/HanamiHost.cs` (`StartAsync`, `BuildState`, bridge switch), `App/TrayIcon.cs`, `App/Installer.cs` (`SetupWindow`, `Install`), `Core/Settings.cs`, `App/AppPaths.cs` (`Version`, `IsDev`), `web/settings/tabs/general.js`, `web/settings/mock-bridge.js`, CONTRACTS "Settings bridge".

- [x] B.1 **Contract first:** settings `updates: {autoCheck: true, channel: "stable" | "beta", skippedVersion: null, lastCheckUtc: null}`, state `update: {status: "idle" | "checking" | "upToDate" | "available" | "downloading" | "failed", version, notes, sizeBytes, url, progress, error, lastCheck}`, and bridge methods `checkForUpdates`, `installUpdate`, `skipUpdate`, `openUpdatePage`. See if defaults cover old files; only bump `CurrentVersion` if a migration is really needed.
- [x] B.2 **`Core/UpdatePolicy.cs` (pure, tested):** version parse and compare with semver prerelease rules (`0.1.0-rc.2` < `0.1.0`, `rc.2` < `rc.10`). `System.Version` can't parse these. `PickRelease` skips drafts, prereleases on Stable, releases missing either asset, and anything not newer. Also when to check and when to notify (once per version, not for the skipped one; a newer version clears a skip).
- [x] B.3 **`App/UpdateService.cs`:** `GET https://api.github.com/repos/eAbhay/Hanami/releases?per_page=30`, not `/releases/latest`, which can't see prereleases. Send a `User-Agent` and `If-None-Match` (a 304 doesn't count against the 60 requests an hour limit). 20 s timeout. No releases means up to date. Offline or rate limited: only a log line on automatic checks, a message on **Check now**. In dev only, `--update-feed <url>` swaps the address, so B and C can be tested with no real release.
- [x] B.4 **Tray:** a notification once per version (clicking it opens settings), and a lasting "Update to x.y.z" menu item while one is available.
- [x] B.5 **Settings UI:** banner, setting, channel, **Check now**, last checked. Notes are shown as plain text, never HTML: they come from the network. Mock bridge support.
- [x] B.6 **`SetupWindow` checkbox**, saved in `Install` the same way as `startWithWindows`, including when reinstalling over an existing folder.
- [x] B.7 **README:** say the check contacts GitHub and sends nothing else.
- [x] B.8 **Check in dev** against a local fake feed: Stable and Beta pick correctly, skip, one notification per version, off means no automatic request. Commit.

Notes: new code in `Core/UpdatePolicy.cs` (SemVer, ParseReleases, PickRelease, ShouldCheck/Notify), `App/UpdateService.cs`, tray item + balloon in `App/TrayIcon.cs`, General tab (`web/settings/tabs/general.js`). 279 tests pass. Checked in the dev app against a local fake feed (`--update-feed <file or url>`, dev only): Stable took 0.2.0 not the beta, Beta took the beta, skip held, a newer beta cleared the skip, `notifiedVersion` recorded once per version, autoCheck off made no request in 80 s, a broken feed showed `failed` for Check now.
Differed from the plan: settings gained `updates.notifiedVersion` (the "once per version" memory survives restarts); the check ~60 s after start runs even if the last one was recent (the answer isn't kept across restarts and a 304 is free); `installUpdate` opens the release page until phase C, and the banner button is *Open release page* for now (C adds *Update*).
Unverified: the real GitHub request (no release exists yet, so a 304/ETag and the 403 text are untested); the tray balloon and menu item were not looked at on screen; the `SetupWindow` checkbox and reinstall-keeps-choice were not run (an install touches HKCU, needs the user's yes). Feed files given with `--update-feed` are read from disk in dev only.

## Phase C — Download and install

Read: `App/Installer.cs` (`Resolve`, `Update`, `Launch`), `Program.cs`, `App/HanamiHost.cs` (`Quit`), `App/Uninstaller.cs`, `Platform/Registration.cs`, CONTRACTS "Install registration and uninstall", "On-disk layout".

The exe already replaces itself: `Resolve` asks the running copy to quit, `Update` copies over the installed exe, then starts it with `--updated`. Downloading and a flag are what's missing. The new version does the replacing, so later versions can fix that part; what the old version does (download, verify, start with the flag) can't be fixed for anyone who doesn't update.

- [x] C.1 **Contract first:** the frozen items above, and the download folder in "On-disk layout".
- [x] C.2 **Download** to `<install>/update/Hanami-<version>.exe`, so uninstalling removes it. HTTPS only, except a dev test feed. Progress shown in settings. Check the size against the asset and the SHA-256 against `Hanami.exe.sha256` (`<hash> *Hanami.exe`, see `tools/build.ps1`). On any failure delete the file and leave the app running. Don't download until the click. Delete leftovers on the next start.
- [x] C.3 **Handover:** the running copy starts the new exe with `--apply-update`, and it replaces the installed copy with no `MessageBox`. The new exe asks the old one to quit, so if Windows blocks it the old copy simply keeps running. A failed start (`Win32Exception`, as Application Control might do on this laptop; it already blocks fresh test DLLs) shows `failed` with **Open download page**.
- [x] C.4 **Safe replace:** `Update` uses `File.Copy` over the 80 MB exe, so a crash mid-copy breaks the install. Copy to `Hanami.exe.new`, then swap it in with a move. With the flag, a failure starts the old copy again instead of leaving nothing running.
- [x] C.5 **Trap in `Update`:** after `mutex.WaitOne(10 s)` times out, `ReleaseMutex` throws `ApplicationException`, which isn't caught. Handle the timeout, and allow for `QuitAsync`, which can take 3 s putting the lock picture back.
- [x] C.6 **Check:** does the Apps & features version change after an update? It doesn't re-register today; fix it if the version is stale.
- [x] C.7 **Tests:** the replace and checksum logic, pulled out so it can run without Windows prompts.
- [x] C.8 **Check with a local feed and a temp install, with the user's yes:** settings and library kept, restart with no settings window, lock picture right afterwards (quit puts it back, the next start sets it again), bad checksum, cut download, locked exe. Clean up. Commit.

Notes: new code in `Core/UpdateInstall.cs` (checksum parse, verify, allowed URLs, `Replace`), `UpdateService.InstallAsync` (+ handover watchdog), `Installer.ApplyUpdate` / `StopRunningCopy`; banner in `general.js` (Update, progress, Try again, Open download page) and mock. 328 tests pass.
Differed from the plan: `--update-feed` is now also honored by an installed copy when it is a local file or loopback address (otherwise a temp install couldn't be rehearsed); C.6 needed no fix (`RegisterApp` runs on every start; Apps & features showed 0.2.0 after the update).
Checked on a temp install (v0.1.0 to v0.2.0 via a file feed, WebView2 debug port through `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`): update kept settings, no settings window after restart, `update/` and `.new` gone; wrong checksum and wrong size both ended in `failed` with the file deleted and the app running; a locked exe made `ApplyUpdate` fail after ~5 s, exe untouched, old copy restarted. Cleaned up (registry, Run value, shortcut, folders).
Not run: lock picture across the update (`syncLockScreen` was off: real lock screen needs the user's yes); a truly cut HTTP download and a real HTTPS GitHub download (size check covers the cut case in tests); old copy refusing to quit; Application Control blocking the new exe (`Win32Exception` path); the banner was not looked at on screen (the mock bridge covers it: `hanamiMock.setInstallResult("fail")`). If the swap fails the old copy restarts silently (log only).

## Phase D — Release pipeline

Read: `tools/build.ps1`, `.github/workflows/release.yml`, `CHANGELOG.md`, README "Download".

- [x] D.1 **`tools/build.ps1`:** its check rejects anything but `1.2.3`, so prerelease tags would fail. Accept `1.2.3-rc.1`. Check `AppPaths.Version` returns the prerelease text and the file version still builds.
- [x] D.2 **`release.yml`:** a tag with `-` gets `--prerelease`. Bump the pinned actions off Node 20, pinned to commits as now, checking each new major's changes.
- [x] D.3 **Docs:** README "Updating" now describes the in-app route, with the manual route as the fallback. Write down how to cut a stable and a beta release. `CHANGELOG.md` needs a section per tag, or the workflow stops.
- [x] D.4 **With the user's yes:** push, run the dry run. Commit.

Notes: D.1-D.3 done (5 Nov 2026), D.4: pushed and dry run started with the user's yes.
- `build.ps1 -SkipTests -Version v9.9.9-rc.1` built: FileVersion 9.9.9.0, ProductVersion `9.9.9-rc.1+<sha>`, so `AppPaths.Version` (split on `+`) gives `9.9.9-rc.1`. `1.2` is still rejected. `dist/` now holds that 9.9.9-rc.1 exe: rebuild before using it.
- `release.yml`: `--prerelease` when the tag contains `-` (`@extra` splat on `gh release create`; not run yet, the dry run doesn't reach it, so the first real run is rc.1). Actions now checkout v7.0.1, setup-dotnet v6.1.0, upload-artifact v7.0.2, all Node 24, pinned to commit SHAs. Release notes of each new major read: ESM move, Node 24, upload-artifact `archive` option; nothing affects this workflow.
- Dry run (run 41851586039, 5 Nov 2026) passed on windows-latest with the tests and no Node 20 warning. Phase D is pushed as c736762.
- README gained "Releasing" under Build (stable vs beta, dry run, frozen items). `CHANGELOG.md` got rc.1 and rc.2 stub sections so those tags could build (removed again once the rcs were deleted).

## Phase E — Rehearsal, then `v0.1.0`

Prereleases don't become GitHub's "latest", so the README's download link is unaffected. Release tests can't run on this laptop (Application Control) but CI runs them; Debug tests pass locally.

- [x] E.1 **With the user's yes:** tag `v0.1.0-rc.1`. This is the first real tagged run of `release.yml`. Download it, check the checksum, install.
- [x] E.2 **With the user's yes:** tag `v0.1.0-rc.2`. On Beta, rc.1 finds it and installs it with one click. Confirm the version, settings and library. On Stable, rc.1 must not offer it.
- [x] E.3 **Application Control:** note whether Windows blocks the unsigned exe here, and that the fallback appears if it does.
- [x] E.4 **With the user's yes:** tag `v0.1.0`. rc.2 on Stable finds it and installs. This ticks round 7's 3.7. Check the release page and the README link.
- [x] E.5 Ask the user about GitHub's immutable releases setting (check how it affects `gh release create` first, and that tags can't be reused), 2FA, and whether to delete the rc releases. Then a final pass over stale docs.

Notes: E.1-E.3 done 4 Nov 2026 (user's yes for each tag and the temp install).
- rc.1 and rc.2 builds passed on CI and are published as prereleases with both assets; downloaded rc.1's SHA-256 matched. The first real HTTPS check/download from GitHub worked: Stable said "up to date" with rc.2 out, Beta offered rc.2 (82.6 MB), one click downloaded, verified, swapped and restarted. Version 0.1.0-rc.2 in the exe and Apps & features, settings (channel beta) and Library kept, no settings window after the restart, `update/` gone.
- Application Control did not block the unsigned downloaded exe (rc.1 install or the rc.2 swap) on this laptop, so the `Win32Exception` fallback banner was not seen here.
- E.4: `v0.1.0` published as Latest (run on tag 3b40758), assets `Hanami.exe` + `.sha256`, `releases/latest` redirects to it. The test install on Stable (rc.2) was offered 0.1.0 and updated with one click (ARP shows 0.1.0). Test install then removed (registry keys, shortcut, folder); the dev copy runs again.
- E.5 so far: user has 2FA on; rc.1 and rc.2 releases and tags were deleted (`gh release delete --cleanup-tag`), their CHANGELOG stubs removed. **User's decision (5 Nov 2026):** GitHub's immutable-releases setting stays as it is, unchanged. The final pass over stale docs is done too.
- The user confirmed (4 Nov 2026) the burning tree on the lock screen was expected: it is their screensaver wallpaper.

---

# Round 7 (finished; kept for its Notes)

**Scope when written:** phase 1 first, with no commit until the user tested it. Since done, apart from 2.5 and 3.7, which round 8 takes over (A.5, E.4).

**Safety rules for that round**
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

- [x] 1.1 **Contract first.** New bridge method `saveLockStill {id, dataUrl, frameSec?}`; `<wallpaper dir>/lock.jpg`; manifest `lockFrameSec` and `lockFit`; state status
      `"waiting"` (no picture for the chosen wallpaper yet). Rewrite "Lock screen"; remove `holdForCapture`, `captureDone`, `readyForCapture`, `&capture=1`.
- [x] 1.2 **Settings page, new `web/settings/lockstills.js`,** sharing the helpers in `thumbs.js` (export `once`, `withTimeout`, seek): for every video and image without a current
      still, queued one at a time like `ensureThumbnails`, draw at the primary monitor's pixel size honouring the wallpaper's `fit` (black bars for `contain`), JPEG quality about 0.92,
      send `saveLockStill`. A wallpaper whose `fit` differs from `lockFit` is redone at its `lockFrameSec`. Call it wherever `ensureThumbnails` is called, so imports (media and packs) and existing libraries are both covered.
- [x] 1.3 **Built-in scenes:** draw with `Scene` from `/wallpaper/scenes.js` on an unattached canvas, one frame (its layers are seeded, so it is repeatable). Saved under
      `<config>/lockscreen/builtin-<scene>.jpg`. If `Scene` needs layout (`clientWidth`), give the canvas an off-page size rather than changing the wallpaper's behaviour.
- [x] 1.4 **Host:** `Library.SaveLockStill` (validate the data URL as `SaveThumbnail` does; write to a temp file, then move). Deleting a wallpaper removes it with the folder. Packs don't export `lock.jpg` (the importer regenerates it), but do carry `lockFrameSec` in the manifest if that's no extra work.
- [x] 1.5 **`LockScreenSync`:** no capturing. `RefreshAsync` finds the still of the primary monitor's screensaver wallpaper; none → status `"waiting"`, keep the current picture, write nothing. Otherwise copy to `current.jpg` and call `ApplyAsync`.
      `saveLockStill` calls `RequestRefresh`. Persist the signature (`<config>/lockscreen/`) so an app start doesn't rewrite the picture. Signature = wallpaper id + dark + the still's write time.
- [x] 1.6 **Remove the capture path:** `CreateCaptureAsync`, `CaptureAsync`, `holdForCapture`, `CaptureForLockScreenAsync`, `WantsCaptureOnStart`, `CaptureFromScreensaverAsync`, `LockTrigger`, page `isCapture`/hold/`captureDone`, the clock's `hold`/`release` if nothing else uses them. Check `_blocked` still earns its place.
- [x] 1.7 **Tests:** replace `LockImagePolicyTests` for the signature-only rule (no rewrite when unchanged, rewrite when the id, theme or still changes, none when off). Library: `SaveLockStill` rejects bad data, manifest round trip. Mock bridge: `saveLockStill`.
- [x] 1.8 **Copy:** Screensaver tab card and confirm dialog drop "updated each time it starts"; add a `"waiting"` line. README section.
- [x] 1.9 **Check, automated in dev (sync off):** still files look right for a video, an image, each built-in scene, and a `contain` wallpaper. Screensaver shows its clock at once. Tracing the window list while changing wallpaper shows no new Hanami window.
- [x] 1.10 **The user tests the bug. Stop here and wait.** Build, start the app for them (`--dev`, see `docs/TESTING.md`) and give them these steps. This writes to their real lock screen, so ask first, and they turn the option on themselves:
      1. Screensaver tab: turn on "Match the lock screen to the screensaver". No flash.
      2. Change the screensaver wallpaper a few times (video, image, built-in scene). No flash, and the status line updates.
      3. Flip the theme, if light and dark use different screensaver wallpapers. No flash.
      4. Quit and restart the app. No flash.
      5. Import a new video and pick it as the screensaver wallpaper. No flash.
      6. Win+L: the lock screen shows the right picture, with no clock or particles. Repeat for an image and a built-in scene.
      7. Let the screensaver start (or Preview): its clock appears at once, with no flash.
      If they still see a flash, ask what it looked like and when, and fix that. Don't commit, and don't tick this box. The diagnosis above was read from the code, so it may be incomplete.
- [x] 1.11 **Only after the user says the bug is gone:** tick the boxes, write the Notes and make one commit. Don't push. Then stop: phases 2 and 3 stay on hold.

Notes: the user confirmed on the real desktop (4 Nov 2026) that the flash is gone.
- Stills are made by the settings page, so the settings window has to have been opened once after an update for existing wallpapers (status `waiting` until then). `ensureLockStills` runs from `wallpaperPicker` (manage mode), next to `ensureThumbnails`.
- `WallpaperInfo.lockStill {fit, frameSec}` is new; `frameSec` is phase 2's 2.1 item, pulled forward because redoing a still needs it. Built-in stills are `config/lockscreen/builtin-<scene>.jpg` (`Library.BuiltinStillDir`).
- The host always shows wallpapers as `cover`, so `contain` can't occur yet; drawing for it exists and was checked numerically only.
- `_blocked` was removed (it only avoided a capture window). Unused now: `Win32.SetLayeredWindowAttributes`. No stills are remade if the primary monitor's resolution changes.
- Stills are not made for a wallpaper whose video fails to load (logged, not retried that session). A null screensaver wallpaper id stays `waiting`.
- Changed after the user's test (4 Nov 2026): Hanami now puts the previous lock picture back by itself when the option is turned off, on quit, and at the next start after a crash (the persisted signature marks "our picture is in place"). Round 6's "never restored automatically" no longer holds; *Restore my previous picture* stays for when that didn't happen. Code: `LockScreenSync.RevertAsync/RevertOnExitAsync`, `HanamiHost.QuitAsync`; tests in `LockScreenSyncTests`.
- `devroot/config/settings.json` had `syncLockScreen` on; it was set to off for the automated checks (backup was in the session scratchpad), so a dev start no longer writes the real lock screen by itself.

## Phase 2 — Choose the frame (on hold until the user asks)

Read: `web/settings/tabs/screensaver.js` (the Lock screen card), `web/settings/lockstills.js`, `web/settings/style.css` (existing slider rows).

- [x] 2.1 **Contract:** `lockFrameSec` is exposed on `WallpaperInfo`; document the picker.
- [x] 2.2 In the Lock screen card, when sync is on and the primary monitor's screensaver wallpaper is a **video**: a live `<video>` preview (shown with the wallpaper's fit, primary monitor's aspect), a range slider over its duration, and **Use this frame** / **Reset**.
      Dragging seeks, dropping pending seeks so it stays smooth on 4K. Save only on the button: a full-resolution draw plus `saveLockStill {frameSec}`, which triggers a refresh. For images and built-in scenes, show the picture with no slider.
- [x] 2.3 Light and dark: the picker follows the wallpaper the current theme uses. Frames are per wallpaper, so nothing else to store.
- [x] 2.4 Unload the preview `<video>` when the tab or card goes away (`thumbs.js` shows how), so a 4K decode doesn't run in the background.
- [x] 2.5 Mock bridge support. Check in the browser (mock) and the dev app. With the user: pick a frame, Win+L. Commit.

Notes: 2.5 is left unticked: the browser (mock) check is done, the real Win+L check is still for the user (now round 8's A.5).
- New `web/settings/components/lockFrame.js` (created from the Screensaver tab, which now has `show`/`hide` to load/unload the preview) and `makeLockStill`, `lockWallpaperId`, `primarySize`, `lockFit` exported from `lockstills.js`. No host change was needed: `lockStill.frameSec` was already in `WallpaperInfo` (phase 1).
- Checked in the browser on the mock with a real local video: starts on the default frame, drag seeks (pending seeks dropped), *Use this frame* saves `frameSec`, *Reset* saves without it, leaving the tab unloads the `<video>`, image and built-in show a picture without a slider.
- Not checked on the real host: turning sync on in the dev app writes the real lock screen. Try: Screensaver tab, pick a frame, *Use this frame*, Win+L.
- Trap: seeking only works on a server that supports HTTP Range. `python -m http.server` doesn't, so a browser check of this needs a Range-capable server (WebView2's virtual host is fine).

## Phase 3 — First GitHub release (on hold until the user asks)

Read: `tools/build.ps1`, `src/Hanami.App/Hanami.App.csproj`, `App/AppPaths.cs` (`Version`), `App/Installer.cs` (replacing an installed copy), README "Licence".

- [x] 3.1 **Notices:** `THIRD-PARTY-NOTICES.md` in the repo and shipped under `web/` so it is in the exe: Tilt Warp (OFL), .NET runtime and WPF/WinForms (MIT), WebView2 SDK (Microsoft's licence; check its redistribution terms and quote accurately), Shimeji-ee frame timings (Yuki Yamada). Link it from the README. Link it from the settings About area if there is one.
- [x] 3.2 **`CHANGELOG.md`:** one entry per version, written for users. First entry summarises what the app does.
- [x] 3.3 **Version from the tag:** `tools/build.ps1 -Version x.y.z` passes `-p:Version=`; the csproj keeps its value for local builds. Print a SHA-256 for the exe into `dist/Hanami.exe.sha256`.
- [x] 3.4 **`.github/workflows/release.yml`:** on a `v*` tag, on `windows-latest`: setup .NET per `global.json`, run `tools\build.ps1 -Version <tag>`, and create a release named after the tag, with the matching changelog section as notes and `Hanami.exe` plus the checksum attached. `permissions: contents: write`, actions pinned. Tests failing means no release. Also a `workflow_dispatch` dry run which uploads a build artifact without releasing. Confirm the tests pass on a clean runner (no `devroot`, no ffmpeg).
- [x] 3.5 **README:** a **Download** section at the top linking to the latest release, requirements (Windows 11 x64, WebView2 runtime: built into Windows 11), and how to update (download the new exe and run it: `Installer` replaces the copy). Explain the SmartScreen warning: the exe isn't code-signed, so choose More info → Run anyway, and compare the checksum. Add a screenshot or short GIF (made from original or demo content only, not imported third-party art).
- [x] 3.6 **With the user's yes:** merge `round-6` and this round into `main` (`main` has nothing `round-6` lacks, 9 commits behind today, so a fast-forward works), push, run the dry run, download its exe and test it from a temp folder per `docs/TESTING.md`.
- [x] 3.7 **With the user's yes:** tag `v0.1.0`, push the tag, check the published page and download. From now on: bump the changelog, tag, done.

Notes: 3.1–3.6 are done; 3.7 (tag `v0.1.0`) now happens in round 8's E.4, so the first release includes the updater.
- 3.6 (4 Nov 2026, user's yes): `main` fast-forwarded to `round-6` and both pushed. Dry run (run 41834794087) passed on `windows-latest` in 1m11s with the tests, so tests do pass on a clean runner. Its exe: SHA-256 matched the `.sha256` file, version `0.1.0+d7dfa22`. Installing it ran fine (web assets extracted, notices file present, built-in lock stills made, uninstall keys written) and was cleaned up. The run warns that the pinned v4 actions use Node 20 (forced to Node 24); bump them to newer majors before it becomes an error.
- Trap while testing: `uia.ps1`'s Edit lookup failed silently and the installer used its default folder (the real `%LocalAppData%\Programs\Hanami`). It was a fresh install, removed again (folder, `HKCU\Software\Hanami`, the Uninstall key, the Start menu shortcut). Check each UIA step succeeded before clicking Install, and stop on errors.
- Notices live in `web/THIRD-PARTY-NOTICES.md` (so the exe embeds them with no csproj change); General tab shows them in an "Open-source notices" `<details>`, the README links them. The Shimeji-ee wording follows this plan's note ("New BSD" per its maintainer), not a licence text I read.
- `release.yml` has never run and wasn't linted (no PyYAML/actionlint here). Actions are pinned to the commits `v4` pointed at on 4 Nov 2026. `global.json` has no SDK version, so it uses `dotnet-version: 10.0.x`. The CHANGELOG-section extraction was run locally. The dry run (`workflow_dispatch`) is the first real test, and it also answers "do the tests pass on a clean runner" (they don't reference `devroot` or ffmpeg).
- On this laptop a Release `dotnet test` is blocked by Windows Application Control ("blocked this file") for the fresh test DLL, so `tools\build.ps1` with tests can't finish here right now; Debug tests pass (230). `-SkipTests -Version v9.9.9` was checked: version stamped, checksum matches.
- The README screenshot `docs/img/settings.jpg` is the settings page on the mock bridge (built-in scenes and procedural samples only). The README repo link assumes `github.com/eAbhay/Hanami`.

---

## Future plans (not in this round; each needs its own planning)

**Hanami's own Shimeji packs.** Feasible; the app needs nothing new. Format is `shime1.png`…`shime46.png` at 128×128 facing left, and any frame a pack lacks falls back to `shime1` (`PetBrain`), so a pack can start with about 12 key poses and grow. Plan: pick one original character (no existing anime characters, so it can be bundled and redistributed freely), write a pose sheet mapping frames to `ShimejiFrames`, make a small tool that turns a sprite sheet into a pack and previews it, and bundle a first pet as a starter. The hard part is art: keeping one character consistent across poses.

**Other ideas, in the order suggested:** signed updates and code signing to remove the SmartScreen warning · a playlist per monitor, rotating wallpapers by time or time of day · drag and drop import onto the settings window · the in-wallpaper Live2D character, still not started.
