# Hanami — project guide for Claude

Tray-only Windows 11 live-wallpaper app: video/image/built-in scenes behind the desktop icons,
interactive particle effects, an idle screensaver with a glass clock, export/import packs.
The user runs it on two laptops (this one: Core Ultra 7 165H, Intel graphics, 1920×1200 @125%,
Windows Insider build). Art direction is anime. Read `README.md` for the feature overview.

## Where things are

- `src/Hanami.App/` — C# host (.NET 10, WPF + WinForms tray). `App/` lifecycle, windows, bridge ·
  `Core/` settings, mode rules, screensaver rules, library, packs (pure, unit-tested) · `Platform/`
  + `Native/` Win32.
- `src/Hanami.Tests/` — xUnit v3 tests for `Core/`.
- `web/shared/effects.js` particle engine · `web/wallpaper/` page behind the icons (also the
  screensaver page with `&screensaver=1`, clock in `clock.js`) · `web/settings/` settings UI
  (vanilla ES modules, `mock-bridge.js` lets it run in a plain browser).
- `docs/CONTRACTS.md` — **source of truth** for host ⇄ page messages, settings/state shapes.
  Change it first, then both sides.
- `docs/PLAN.md` — the current implementation plan with checkboxes. Work through it in order.
- `docs/TESTING.md` — how to verify changes for real (dev mode, DevTools bridge, real-screen
  capture, synthetic input). Read it before testing.
- `tools/` — `build.ps1` (tests + single-file exe → `dist/`), demo assets, icon, `cdp.mjs`;
  `tools/dev/` test helpers.

## Commands

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\quit.ps1          # stop a running Hanami first (it locks the exe)
dotnet build src\Hanami.App\Hanami.App.csproj -c Debug
dotnet test --project src\Hanami.Tests\Hanami.Tests.csproj
powershell -ExecutionPolicy Bypass -File tools\build.ps1             # tests + publish dist\Hanami.exe
```

The shell may have a stale PATH: prefix commands with
`$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.

## How we work (plan and execute)

This project uses a plan and execute approach: one chat plans into `docs/PLAN.md`, then each phase runs
in its own fresh chat.

- **Read only what the task needs.** Start from this file and the last finished phase's Notes in
  `docs/PLAN.md`, then open only the code related to the task and the relevant section of
  `docs/CONTRACTS.md`. Notes say where to look; the code is the source of truth, so check it before relying on them.
- **Wide searches go to a subagent**, which reports back only the conclusion.
- **Plans:** each phase lists the files and functions to read (name a function or class, not a line number,
  since line numbers go stale) and what is already diagnosed, so the executor doesn't redo that work.
- **End of a phase:** write its Notes in under 10 lines: what differed from the plan, what is unverified,
  traps, and where new code lives. Don't repeat what git records.
- **Long chats:** when a chat gets long, write a handoff note and continue in a fresh chat. Small fixes don't
  need a separate plan.

## Rules for this repo

- A change includes its tests, comments, `docs/CONTRACTS.md`, `README.md` and settings-UI copy
  that it makes stale.
- Web code: no build step, no npm, no CDNs (must work offline). Fonts/assets are bundled in `web/`.
- Never install the exe into the user's real location or touch `HKCU\Software\Hanami` / the Run key
  without asking; test installs go in a temp folder and get cleaned up (see `docs/TESTING.md`).
- Don't minimize/rearrange the user's windows (no Win+D). To see the desktop wallpaper while
  windows are maximized, set `power.whenMaximized` to `"full"` in `devroot/config/settings.json`
  and put it back afterwards.
- `devroot/` (dev install root, with the user's imported test videos) and `dist/` are gitignored.
- Windows PowerShell 5.1 gotchas: pass `[NullString]::Value`, not `$null`, to string P/Invoke
  params; keep `.ps1` files ASCII (an em dash breaks parsing without a BOM).
- Commit at the end of each plan phase with a descriptive message; tick the phase's boxes in
  `docs/PLAN.md` in the same commit.
