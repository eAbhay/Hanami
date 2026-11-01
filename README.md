# Hanami 🌸

Live wallpapers for Windows 11 with interactive particle effects. It runs from the
system tray as a single self-contained `.exe`.

- **Video, image or built-in animated scenes** play behind your desktop icons, one per monitor.
- **Interactive effects** draw over the wallpaper: sakura petals, snow, autumn leaves, fireflies,
  rain and sparkles. They react to the mouse (repel, attract, or a gust of wind) and burst when
  you click the desktop.
- **Battery-aware.** Each condition (plugged in, on battery, low battery, Energy Saver, a
  maximized window, a fullscreen app) can be set to run *Full*, *Reduced* (video frozen,
  effects at 30 fps) or *Paused*. By default it pauses on battery, and you can override that
  to keep it running. A wallpaper fully covered by a window is suspended, so it uses no CPU.
- **Library.** Imported videos and images are copied into the app's own `Library` folder, so
  deleting the original download doesn't break the wallpaper.
- **Export / import.** A `.hanamipack` file moves any selection of the library, plus your
  settings, to another PC.

> Status: phase 1 (video + effects). Planned next: interactive illustrations (Live2D
> characters that follow the cursor, a cat that roams the wallpaper), then desktop companions
> that climb your windows.

## Install

1. Build it (below), or take `dist/Hanami.exe`.
2. Run `Hanami.exe`. On first run it asks where to install. The default is
   `%LocalAppData%\Programs\Hanami`. It copies itself there, creates `Library/` and
   `config/`, and optionally registers itself to start with Windows.
3. Hanami lives in the tray. Click the 🌸 icon to open settings.

**Updating:** run a newer `Hanami.exe` from anywhere. It offers to replace the installed copy,
then restarts Hanami. Your settings and library are kept.

**Moving to another PC:** Settings → Export / Import → choose items → *Export*. On the other
PC, install Hanami and use *Import pack*.

The first launch of an unsigned exe can show a SmartScreen "unknown publisher" prompt
(*More info → Run anyway*).

## Build

Requires the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`). ffmpeg is optional and
only used by the asset scripts in `tools/`.

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1          # runs tests, then publishes dist\Hanami.exe
dotnet test --project src\Hanami.Tests\Hanami.Tests.csproj         # tests only
```

## Develop

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-demo-assets.ps1   # placeholder videos in devroot\Library
dotnet build src\Hanami.App\Hanami.App.csproj
src\Hanami.App\bin\Debug\net10.0-windows\win-x64\Hanami.exe --dev
```

`--dev` uses `devroot/` as the install folder and serves `web/` straight from the repo,
so HTML/JS edits apply when the page reloads. Dev mode also enables the WebView2 dev tools
and context menu.

The web pages also run in a normal browser with mocked host messages:
`python -m http.server 8765 --directory web`, then open
`http://localhost:8765/wallpaper/index.html?dev` or `http://localhost:8765/settings/index.html`.

To script the running app's pages, set `HANAMI_DEBUG_PORT=9333` before starting with `--dev`,
then e.g. `node tools/cdp.mjs settings "await (await import('/settings/bridge.js')).call('getState')"`.

Logs: `%LocalAppData%\Hanami\hanami.log`.

## How it works

```
Hanami.exe (C#, WPF, .NET 10) ── tray, install/update, power & window watching, library
 ├─ per-monitor WebView2 windows parented behind the desktop icons ──► web/wallpaper/
 └─ settings window (WebView2) ⇄ JSON bridge ──────────────────────► web/settings/
                                       both use web/shared/effects.js
```

- **Behind the icons:** sending Explorer's `Progman` window the undocumented `0x052C`
  message creates a layer between the wallpaper and the icons. On Windows 11 24H2+ our
  window becomes a layered child of `Progman`, placed just below `SHELLDLL_DefView`. On
  older builds it goes into the spawned `WorkerW`. See `src/Hanami.App/Platform/DesktopHost.cs`.
  If Explorer restarts or the desktop is recreated, Hanami re-attaches within a second or two.
- **Mouse input:** the desktop windows sit above the wallpaper, so it never receives mouse
  events directly. The host polls the cursor position (~60 Hz, only while a wallpaper is live)
  and the button state, and forwards both to the page on that monitor. It uses no global
  hooks.
- **Message formats** between host and pages are specified in [docs/CONTRACTS.md](docs/CONTRACTS.md).

```
src/Hanami.App/    C# host           (App/ lifecycle & UI, Core/ settings·library·packs, Platform/ Win32)
src/Hanami.Tests/  xUnit tests for the pure logic (mode rules, settings, library, packs)
web/shared/        effects.js particle engine
web/wallpaper/     the page behind the icons
web/settings/      the settings UI
tools/             build, icon and demo-asset scripts, CDP helper
```
