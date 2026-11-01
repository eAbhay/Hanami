// Small DOM + formatting helpers shared by the tabs.
import { icon } from "./icons.js";

/** h("div.card#x", {onclick, attrs...}, ...children) — tiny element builder. */
export function h(tag, props, ...children) {
  const [, name = "div", rest = ""] = /^([a-z0-9-]*)(.*)$/i.exec(tag);
  const el = document.createElement(name || "div");
  for (const m of rest.matchAll(/([.#])([^.#]+)/g)) {
    if (m[1] === ".") el.classList.add(m[2]); else el.id = m[2];
  }
  if (props && (typeof props !== "object" || props instanceof Node || Array.isArray(props))) {
    children.unshift(props); props = null;
  }
  for (const [k, v] of Object.entries(props ?? {})) {
    if (v == null || v === false) continue;
    if (k.startsWith("on") && typeof v === "function") el.addEventListener(k.slice(2), v);
    else if (k === "html") el.innerHTML = v;
    else if (k === "class") el.className += ` ${v}`;
    else if (k === "style" && typeof v === "object") Object.assign(el.style, v);
    else if (k in el && typeof v !== "string") el[k] = v;
    else el.setAttribute(k, v === true ? "" : v);
  }
  append(el, children);
  return el;
}

function append(el, children) {
  for (const c of children.flat(Infinity)) {
    if (c == null || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
}

export const svg = (name, cls) => h("span.ico-wrap", { html: icon(name, cls) });

export function formatBytes(n) {
  if (!n) return "0 B";
  const u = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.min(u.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
  const v = n / 1024 ** i;
  return `${v >= 100 || i === 0 ? Math.round(v) : v.toFixed(1)} ${u[i]}`;
}

export function formatDuration(sec) {
  if (!Number.isFinite(sec) || sec <= 0) return "";
  if (sec < 60) return `${sec < 10 ? sec.toFixed(1).replace(/\.0$/, "") : Math.round(sec)} s`;
  const m = Math.floor(sec / 60), s = Math.round(sec % 60);
  return `${m}:${String(s).padStart(2, "0")}`;
}

export const resLabel = (w, hgt) => (w && hgt ? `${w}×${hgt}` : "");

export function debounce(fn, ms) {
  let t = 0;
  const d = (...a) => { clearTimeout(t); t = setTimeout(() => { t = 0; fn(...a); }, ms); };
  d.pending = () => t !== 0;
  d.flush = () => { if (t) { clearTimeout(t); t = 0; fn(); } };
  return d;
}

// ---------------------------------------------------------------- toasts
let toastHost;
export function toast(message, kind = "info", ms = 3200) {
  toastHost ??= document.body.appendChild(h("div.toasts", { role: "status", "aria-live": "polite" }));
  const ic = kind === "error" ? "alert" : kind === "success" ? "check" : "info";
  const t = h(`div.toast.${kind}`, svg(ic), h("span", message));
  toastHost.append(t);
  requestAnimationFrame(() => t.classList.add("show"));
  setTimeout(() => {
    t.classList.remove("show");
    t.addEventListener("transitionend", () => t.remove(), { once: true });
    setTimeout(() => t.remove(), 600);
  }, ms);
}

export const toastError = (err) => toast(err?.message ?? String(err), "error", 4500);

// ---------------------------------------------------------------- dialogs
/** Modal dialog. Resolves to the clicked button's value (or the input value for prompt). */
export function dialog({ title, body, input, confirmLabel = "OK", cancelLabel = "Cancel", danger = false }) {
  return new Promise((resolve) => {
    const field = input ? h("input.text-input", { type: "text", value: input.value ?? "", maxlength: 120, spellcheck: false }) : null;
    const ok = h(`button.btn.${danger ? "danger" : "primary"}`, { type: "submit" }, confirmLabel);
    const cancel = h("button.btn", { type: "button", onclick: () => close(null) }, cancelLabel);
    const form = h("form.dialog-body", { method: "dialog" },
      h("h2", title),
      body ? h("p.muted", body) : null,
      field,
      h("div.dialog-actions", cancel, ok));
    const dlg = h("dialog.modal", form);
    let done = false;
    const close = (v) => {
      if (done) return; done = true;
      dlg.classList.add("closing");
      setTimeout(() => { dlg.close(); dlg.remove(); }, 140);
      resolve(v);
    };
    form.addEventListener("submit", (e) => {
      e.preventDefault();
      if (field && !field.value.trim()) { field.focus(); return; }
      close(field ? field.value.trim() : true);
    });
    dlg.addEventListener("cancel", (e) => { e.preventDefault(); close(null); });
    dlg.addEventListener("click", (e) => { if (e.target === dlg) close(null); });
    document.body.append(dlg);
    dlg.showModal();
    if (field) { field.focus(); field.select(); } else ok.focus();
  });
}

// ---------------------------------------------------------------- controls
/** Segmented control. options: [{value, label, title?, icon?}] */
export function segmented(options, value, onChange, { cls = "", ariaLabel = "" } = {}) {
  const root = h(`div.segmented${cls ? "." + cls : ""}`, { role: "radiogroup", "aria-label": ariaLabel });
  const btns = options.map((o) => {
    const b = h("button.seg", { type: "button", role: "radio", title: o.title ?? null, "data-value": String(o.value),
      onclick: () => { if (root._value !== o.value) { set(o.value); onChange(o.value); } } },
      o.icon ? svg(o.icon) : null, h("span", o.label));
    return b;
  });
  root.append(h("span.seg-thumb"), ...btns);
  function set(v) {
    root._value = v;
    btns.forEach((b, i) => {
      const on = options[i].value === v;
      b.classList.toggle("on", on);
      b.setAttribute("aria-checked", on ? "true" : "false");
    });
    positionThumb();
  }
  function positionThumb() {
    const i = options.findIndex((o) => o.value === root._value);
    const thumb = root.firstChild;
    if (i < 0) { thumb.style.opacity = 0; return; }
    const b = btns[i];
    if (!b.offsetWidth) { requestAnimationFrame(positionThumb); return; }
    const first = !thumb._placed;
    if (first) thumb.style.transition = "none";
    thumb.style.opacity = 1;
    thumb.style.width = `${b.offsetWidth}px`;
    thumb.style.transform = `translateX(${b.offsetLeft - 3}px)`;
    if (first) { thumb._placed = true; void thumb.offsetWidth; thumb.style.transition = ""; }
  }
  new ResizeObserver(positionThumb).observe(root);
  root.set = set;
  set(value);
  return root;
}

/** Toggle switch. */
export function toggle(checked, onChange, ariaLabel = "") {
  const input = h("input", { type: "checkbox", role: "switch", "aria-label": ariaLabel });
  input.checked = !!checked;
  input.addEventListener("change", () => onChange(input.checked));
  const el = h("label.switch", input, h("span.track", h("span.knob")));
  el.set = (v) => { input.checked = !!v; };
  el.input = input;
  return el;
}

/** Slider row: label, value readout, range input. onInput fires continuously. */
export function slider({ label, min, max, step, value, format, onInput, hint }) {
  const out = h("output.slider-value");
  const input = h("input.range", { type: "range", min, max, step, "aria-label": label });
  const paint = () => {
    const p = ((Number(input.value) - min) / (max - min)) * 100;
    input.style.setProperty("--p", `${p}%`);
    out.textContent = format(Number(input.value));
  };
  input.addEventListener("input", () => { paint(); onInput(Number(input.value)); });
  const el = h("div.slider", h("div.slider-head", h("span.slider-label", label), out), input, hint ? h("div.slider-hint", hint) : null);
  el.set = (v) => {
    if (document.activeElement === input && input.matches(":active")) return;   // don't fight the user's drag
    input.value = v; paint();
  };
  el.input = input;
  el.set(value);
  return el;
}

export function progressBar() {
  const fill = h("div.progress-fill");
  const label = h("span.progress-label");
  const pct = h("span.progress-pct");
  const el = h("div.progress", { hidden: true }, h("div.progress-head", label, pct), h("div.progress-track", fill));
  el.update = ({ done, total, label: l }) => {
    el.hidden = false;
    const p = total > 0 ? Math.min(100, (done / total) * 100) : 0;
    fill.style.width = `${p}%`;
    label.textContent = l || "Working…";
    pct.textContent = `${Math.round(p)}%`;
  };
  el.reset = () => { el.hidden = true; fill.style.width = "0%"; };
  return el;
}
