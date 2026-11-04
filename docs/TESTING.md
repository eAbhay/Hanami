# Verifying Hanami changes

Unit tests cover `Core/` only. Everything that touches windows, WebView2 or power must be checked
in the running app. Use these recipes; they were all used during development and work.

## 1. Run the dev copy

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\quit.ps1
dotnet build src\Hanami.App\Hanami.App.csproj -c Debug
$env:HANAMI_DEBUG_PORT = "9333"     # enables the DevTools bridge below (dev mode only)
Start-Process src\Hanami.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Hanami.exe -ArgumentList "--dev"
```

- `--dev` uses `devroot/` as the install folder and serves `web/` live from the repo, so HTML/JS
  edits apply on page reload (no rebuild). C# changes need quit → build → start.
- Add `--startup` to start without opening the settings window. To open it later:
  `[System.Threading.EventWaitHandle]::OpenExisting('Local\Hanami.ShowSettings').Set()`.
- Log: `%LocalAppData%\Hanami\hanami.log` — mode changes, screensaver show/end with reason, errors.
- When done, relaunch it without `HANAMI_DEBUG_PORT` and with `--startup` so the user keeps a
  normal running copy.

## 2. Drive pages over DevTools (`tools/cdp.mjs`)

The first argument is matched against page URLs (`settings/`, `wallpaper/`, `screensaver=1`).

```powershell
node tools\cdp.mjs settings/ "await (await import('/settings/bridge.js')).call('getState')"
node tools\cdp.mjs settings/ "(async()=>{const b=await import('/settings/bridge.js'); return await b.call('previewScreensaver')})()"
node tools\cdp.mjs wallpaper/ "[...document.querySelectorAll('video')].map(v=>({paused:v.paused,t:v.currentTime,rs:v.readyState}))"
node tools\cdp.mjs "screensaver=1" "location.reload()"
```

Inject host messages into a wallpaper page (bypasses the host, good for profiling):
`window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'mode',mode:'reduced'}}))`.
Calls that open a native dialog (`importMedia`, `exportPack`, `importPack`) block until it closes:
run the `cdp.mjs` call in a `Start-Job`, then fill the dialog with `tools\dev\filedialog.ps1`.

## 3. See what is really on screen

| Script | Captures |
|---|---|
| `tools\dev\capture-desktop.ps1 -Out x.png` | the desktop layer (Progman) rendered off-screen, even under maximized windows; also lists Progman's children in z-order |
| `tools\dev\capture-window.ps1 -Title Hanami -Out x.png` | the settings window |
| `tools\dev\input.ps1 -Crop x.png` | a full-resolution crop of the real screen centre (use for the screensaver/clock) |

Then open the PNG with the Read tool. For the desktop wallpaper itself the off-screen capture is
the only non-intrusive option; the screensaver is topmost, so a real-screen crop shows it.

## 4. Synthetic input and power state

- `tools\dev\input.ps1 -Move` (mouse wiggle, ends where it started) · `-Key` (taps Shift) ·
  `-Click` (left click at the cursor — only while the screensaver covers the screen).
- `tools\dev\power-state.ps1` prints Windows' system execution state: `0x0` nothing holds the
  screen on, `0x2` some process requires the display, `0x3` display + system (Hanami's screensaver
  request). `-Hold` simulates another app holding a display request for 12 s.
- `tools\dev\perf.ps1` measures CPU of Hanami + its WebView2 processes for several
  wallpaper/effect combinations (expects the dev copy running with `HANAMI_DEBUG_PORT=9333`).
  Readings swing ±5 %; compare, don't trust single numbers.

## 5. The desktop wallpaper is usually hidden

The user typically has a maximized window, which pauses and hides the desktop wallpaper
("Paused (Maximized window), hidden" in the log). To test it, set `power.whenMaximized` to
`"full"` in `devroot/config/settings.json` before starting the dev copy, and restore it after.
Never minimize the user's windows.

## 6. Install/update flow (only when it changed)

Publish (`tools\build.ps1`), then run `dist\Hanami.exe` and drive the setup dialog with
`tools\dev\uia.ps1` helpers into a folder under the session scratchpad with "Start with Windows"
unchecked. Afterwards: quit it (`quit.ps1`), delete `HKCU:\Software\Hanami`, confirm no `Hanami`
value under `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`, delete the test folder.
MessageBox buttons don't expose InvokePattern: click them with `SendMessage(BM_CLICK=0xF5)` on
their `NativeWindowHandle`.

### Uninstall

Install to a throw-away folder (or write `HKCU\Software\Hanami\InstallDir` to a temp folder with a copy of `dist\Hanami.exe`
in it and start that copy with `--startup`), check the Start menu shortcut and the `Uninstall\Hanami` key exist, then run
`<folder>\Hanami.exe --uninstall`: click **Keep my data** (UIA, `tools\dev\uia.ps1`) or **Delete my data too** (then the
confirm box's *Yes*), and dismiss the helper's final box (a Win32 `#32770` titled "Hanami": find its buttons with
`FindWindowEx` and press them with `BM_CLICK`; match the caption without the `&`). Afterwards the exe and `app/` are gone,
`config/` and `Library/` are gone only for *delete*, and the shortcut, `HKCU\Software\Hanami`, the Uninstall key and the Run
value are gone. Careful: this writes the real `HKCU\Software\Hanami` key (and removes the browser cache and, for *delete*,
the log folder), so only do it when no real install is registered.

### Tray menu and tooltip

The tray icon is usually in the hidden-icons flyout, which is awkward to automate. To open the menu without touching the mouse, post the icon's own callback to Hanami's `WindowsForms10.Window.0*`
windows: `PostMessage(hwnd, 0x800, 0, 0x205)`. Its items are UIA `MenuItem`s of the process (read `Name` and `IsEnabled`, click with `InvokePattern`); Esc closes it. While the flyout is open, the icon's UIA `Name` is its tooltip.
To test a download without a handover, use `--update-feed` with assets from a local server whose `.sha256` doesn't match: it fails at the check, with the file deleted. Back up `devroot/config/settings.json` first (a check writes `updates`).

## 7. Browser-only checks

`python -m http.server 8766 --bind 127.0.0.1` from the repo root, then in the built-in browser:
`http://127.0.0.1:8766/web/wallpaper/index.html?dev` (dev panel), add `&screensaver=1` for the
clock, `&video=/devroot/Library/wallpapers/<id>/media.mp4` for a real video;
`http://127.0.0.1:8766/web/settings/index.html` runs the settings UI on the mock bridge.
Stop the server afterwards.

## 8. Desktop pets

Pets are native windows, so check them on the real screen. No pack ships in the exe: generate a test set (46 PNGs,
128×128, e.g. coloured blobs numbered with their frame) into `devroot/Library/pets/pet-test0001/img/shime<N>.png` with a
`manifest.json` (`{id, name, createdAt, frameWidth, frameHeight, frames:[1..46]}`), and add
`"pets":{"items":[{"id":"pa","packId":"pet-test0001"}],"activity":"lively"}` to `devroot/config/settings.json` (keep a backup and
restore it afterwards). Start the dev copy; `Pets: showing N` is logged.

- GDI screen grabs only see layered windows (the pets) with `BitBlt(... SRCCOPY | CAPTUREBLT)`; `Graphics.CopyFromScreen`
  without it shows nothing. Make the grabbing process DPI aware.
- Pets live on top of whatever is maximized; they hide while the screensaver shows (it starts after the idle time: tap Shift
  with `tools\dev\input.ps1 -Key` first) and while a window covers the whole monitor.
- To test window terrain without touching the user's windows, open a small throw-away WinForms window of your own (visible,
  titled, not a tool window), move it and minimize it from the script: pets standing on it are carried and then fall.
- The pure parts (`World`, `PetBrain`, pack import) are unit-tested in `PetBrainTests`, `WorldTests` and `PetPackTests`.
- Things only a person can check: dragging and throwing a pet, riding a window you drag, a click-through next to a pet, a
  second monitor with another DPI.
- Round 6 behaviour (drop on a window, latch, jump, hop, shake, companions, stroke) is covered by `PetBehaviourTests` with scripted
  `World`s. On the real screen, use two test pets, a throw-away WinForms window of your own at 1000x680 and 820x560, and synthetic
  mouse input for the drag: press on a pet, move to the window's title bar, release gently.
