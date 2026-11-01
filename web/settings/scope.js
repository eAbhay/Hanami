// Monitor scope selector: "All monitors" (sameOnAllMonitors) or one tab per monitor.
import { h, svg } from "./util.js";

export function scopeSelector(ctx, { verb = "Applies" } = {}) {
  const row = h("div.scope-tabs", { role: "tablist", "aria-label": "Monitor" });
  const hint = h("div.scope-hint");
  const el = h("div.scope", row, hint);
  let sig = "";

  function update(s) {
    el.hidden = s.monitors.length < 2;
    const nextSig = s.monitors.map((m) => `${m.key}|${m.name}|${m.width}x${m.height}`).join(",");
    if (nextSig !== sig) {
      sig = nextSig;
      row.replaceChildren(
        tabBtn("*", "All monitors", `${s.monitors.length} displays`, "monitors"),
        ...s.monitors.map((m) => tabBtn(m.key, m.name, `${m.width}×${m.height}${m.isPrimary ? " · Primary" : ""}`, "monitor")));
    }
    const key = ctx.scopeKey();
    for (const b of row.children) {
      const on = b.dataset.key === key;
      b.classList.toggle("on", on);
      b.setAttribute("aria-selected", on ? "true" : "false");
    }
    if (key === "*") {
      hint.textContent = `${verb} to every monitor.`;
    } else {
      const own = !!s.settings.assignments[key];
      hint.textContent = own
        ? `${verb} to ${ctx.monitorName(key)} only.`
        : `${verb} to ${ctx.monitorName(key)} only — it currently follows the All-monitors choice.`;
    }
  }

  function tabBtn(key, name, sub, ic) {
    return h("button.scope-tab", { type: "button", role: "tab", "data-key": key, onclick: () => ctx.setScope(key).catch(() => {}) },
      svg(ic), h("span.scope-text", h("span.scope-name", name), h("span.scope-sub", sub)));
  }

  el.update = update;
  return el;
}
