# Wallpaper page

The page the host shows behind the desktop icons, one WebView2 instance per monitor.
The protocol is in `docs/CONTRACTS.md`. The particle engine lives in `../shared/effects.js`.

| File | What it does |
|---|---|
| `index.html` | Layers: `#bg` (background layers) and `#fx` (particle canvas) |
| `wallpaper.js` | Host messaging, background crossfades, modes, dev mode |
| `scenes.js` | Built-in procedural scenes: `twilight`, `dawn`, `night` |
| `style.css` | Full-viewport layers and the dev panel |

## Dev workflow

No build step. Serve `web/` over HTTP, because ES modules don't load from `file://`:

```sh
cd web
python -m http.server 8765
```

Then open <http://localhost:8765/wallpaper/index.html?dev>.

When `window.chrome.webview` is missing, the page runs in dev mode:

- Real mouse moves and clicks are turned into the same `pointer` and `click` messages the host sends.
- A built-in config is applied: the twilight scene with sakura.
- A dev panel (top-left, collapsible) switches the scene, preset, interaction, sliders, mode and fps cap.
  It can also load a video or image URL. The header shows the live fps and particle count.
- Media errors that would go to the host are logged with `console.info` and shown in the panel.

Query parameters for quick checks: `scene=twilight|dawn|night|none`, `preset=<id>`,
`interaction=repel|attract|wind|none`, `mode=full|reduced|paused`, `fps=<n>`,
`video=<url>` (or `src=`), and `fit=cover|contain|fill`.
For example: `index.html?dev&scene=night&preset=fireflies`.

From the console, `window.hanami` exposes `{engine, handle, state, send}` (dev mode only).
`hanami.handle({...})` injects any host message.
