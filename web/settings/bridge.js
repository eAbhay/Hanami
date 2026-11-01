// Settings bridge client (see docs/CONTRACTS.md → "Settings bridge").
//
//   call(method, params) → Promise<result>   (rejects with Error(message) on {id, error})
//   on(event, handler)   → unsubscribe fn     (events: "stateChanged", "progress")
//
// Uses window.chrome.webview inside the Hanami host; falls back to the in-memory mock
// (mock-bridge.js) when the page is opened in a normal browser.

const handlers = new Map();   // event name → Set<fn>
const pending = new Map();    // request id → {resolve, reject}
let nextId = 1;
let transport = null;         // { post(obj), isMock }

function dispatch(msg) {
  if (msg == null || typeof msg !== "object") return;
  if (msg.event) {
    for (const fn of handlers.get(msg.event) ?? []) {
      try { fn(msg.data); } catch (err) { console.error(`[bridge] ${msg.event} handler failed`, err); }
    }
    return;
  }
  if (msg.id != null && pending.has(msg.id)) {
    const { resolve, reject } = pending.get(msg.id);
    pending.delete(msg.id);
    if (msg.error != null) reject(new Error(String(msg.error)));
    else resolve(msg.result);
  }
}

async function getTransport() {
  if (transport) return transport;
  const wv = window.chrome?.webview;
  if (wv) {
    wv.addEventListener("message", (e) => {
      let data = e.data;
      if (typeof data === "string") { try { data = JSON.parse(data); } catch { return; } }
      dispatch(data);
    });
    transport = { post: (obj) => wv.postMessage(obj), isMock: false };
  } else {
    const { createMockHost } = await import("./mock-bridge.js");
    const host = createMockHost(dispatch);
    transport = { post: (obj) => host.receive(obj), isMock: true };
  }
  return transport;
}

export async function call(method, params) {
  const t = await getTransport();
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    const msg = params === undefined ? { id, method } : { id, method, params };
    try { t.post(msg); } catch (err) { pending.delete(id); reject(err); }
  });
}

export function on(event, handler) {
  if (!handlers.has(event)) handlers.set(event, new Set());
  handlers.get(event).add(handler);
  return () => handlers.get(event)?.delete(handler);
}

export async function isMock() {
  return (await getTransport()).isMock;
}
