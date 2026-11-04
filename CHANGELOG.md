# Changelog

One section per version, newest first. The section for a version is used as that release's notes on GitHub, so each is written
for people using Hanami, not for developers. The heading is just the version number (`## 0.1.0`, `## 0.2.0-beta.1`), and a tag
without a matching section stops the release. Versions with a `-` (release candidates, betas) are prereleases.

## 0.1.0

First public release. Hanami is a tray app for Windows 11 that puts a live wallpaper behind your desktop icons.

- **Wallpapers:** play a video or show an image on each monitor, or pick one of the built-in animated scenes. Imported files are
  copied into Hanami's own library, so deleting the original doesn't break anything.
- **Effects:** sakura petals, snow, leaves, fireflies, rain, sparkles and embers drift over the wallpaper and react to your
  mouse. Every wallpaper remembers its own effect settings.
- **Light and dark:** optionally use a different wallpaper when Windows is in dark mode.
- **Desktop pets:** import Shimeji image sets and let them walk along your taskbar and windows. Pet packs are not included.
- **Live screensaver:** after a few idle minutes (plugged in) the wallpaper covers every monitor with a glass clock. A key press or
  click slides it away, or locks Windows if you ask it to.
- **Matching lock screen (optional):** your Windows lock screen can show a still of the screensaver's wallpaper, with a
  frame picker for videos. Hanami never changes it unless you turn this on, and puts your old picture back when you turn it off or quit Hanami.
- **Battery-aware:** pause or reduce the wallpaper on battery, Energy Saver, or when a window is maximized or fullscreen.
- **Export and import** your library and settings as a `.hanamipack` file to move to another PC.

Hanami is a single `Hanami.exe`: run it and it offers to install itself. Running a newer `Hanami.exe` later updates the
installed copy and keeps your settings and library. It is not code-signed, so Windows SmartScreen may warn the first time
(choose **More info**, then **Run anyway**).
