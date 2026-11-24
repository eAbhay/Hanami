// Layout pieces every tab shares: page header, card, settings row, sidebar status pill.
import { h, svg } from "../util.js";

/** Page title + subtitle, with an optional action (button) on the right. */
export function pageHeader(title, subtitle, action) {
  return h("header.page-head",
    h("div", h("h1", title), subtitle ? h("p.page-sub", subtitle) : null),
    action ? h("div.page-actions", action) : null);
}

/** Padded card with an optional heading; `meta` is a string or node shown on the right of the heading. */
export function card(title, meta, ...children) {
  const head = title
    ? h("div.card-head", h("h2", title), typeof meta === "string" ? h("span.card-meta", meta) : meta ?? null)
    : null;
  return h("div.card.pad", head, ...children);
}

/**
 * Icon + label + hint on the left, a control on the right. `rule` makes it a bordered list item
 * (several in a `div.rules`). The returned element has `.text` (the label column) for extras.
 */
export function row(icon, label, hint, control, { rule = false } = {}) {
  const text = h("div.row-text", h("div.row-title", label), hint ? h("div.row-sub", hint) : null);
  const el = h(`div.row${rule ? ".rule" : ""}`, icon ? h("div.row-icon", svg(icon)) : null, text, control);
  el.text = text;
  return el;
}

/** The sidebar's status card: dot + headline + sub line, tinted by mode, with the pause button below. */
export function statusPill(...below) {
  const dot = h("span.status-dot");
  const text = h("span.status-text");
  const sub = h("span.status-sub");
  const el = h("div.status-card", h("div.status-pill", dot, h("div.status-lines", text, sub)), ...below);
  el.set = ({ mode, text: t, sub: s }) => {
    el.dataset.mode = mode;
    text.textContent = t;
    sub.textContent = s;
    sub.hidden = !s;
  };
  return el;
}
