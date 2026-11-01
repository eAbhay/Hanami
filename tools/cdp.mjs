// Dev helper: evaluate JS in a running Hanami page over the DevTools protocol.
// Start Hanami with HANAMI_DEBUG_PORT=9333 and --dev, then:
//   node tools/cdp.mjs settings "await (await import('/settings/bridge.js')).call('getState')"
//   node tools/cdp.mjs wallpaper "document.querySelector('video')?.paused"
// The first argument matches against the page URL.
const [, , match = "settings", expr = "document.title", port = process.env.HANAMI_DEBUG_PORT || "9333"] = process.argv;

const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
const pages = targets.filter((t) => t.type === "page" && t.url.includes(match));
if (pages.length === 0) {
  console.error(`No page matching "${match}". Pages:\n` + targets.map((t) => "  " + t.url).join("\n"));
  process.exit(1);
}

for (const page of pages) {
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener("open", r, { once: true }));
  const result = await new Promise((resolve) => {
    ws.addEventListener("message", (e) => {
      const msg = JSON.parse(e.data);
      if (msg.id === 1) resolve(msg.result);
    });
    ws.send(JSON.stringify({
      id: 1,
      method: "Runtime.evaluate",
      params: { expression: `(async () => (${expr}))()`, awaitPromise: true, returnByValue: true },
    }));
  });
  ws.close();
  const value = result.exceptionDetails ? { error: result.exceptionDetails.exception?.description ?? result.exceptionDetails.text } : result.result.value;
  console.log(`[${page.url}]`);
  console.log(typeof value === "string" ? value : JSON.stringify(value, null, 2));
}
