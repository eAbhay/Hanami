// Inline SVG icons (24×24, stroke-based, Fluent-ish). icon(name) → SVG markup string.

const P = {
  image: '<rect x="3.5" y="4.5" width="17" height="15" rx="3"/><circle cx="9" cy="10" r="1.8"/><path d="m4 17 4.6-4.2a1.5 1.5 0 0 1 2 0L15 17m-1.7-1.6 1.8-1.7a1.5 1.5 0 0 1 2 0L20 16.5"/>',
  sparkles: '<path d="M10 3.5 11.6 8a2 2 0 0 0 1.2 1.2L17.5 11l-4.7 1.7a2 2 0 0 0-1.2 1.2L10 18.5l-1.6-4.6a2 2 0 0 0-1.2-1.2L2.5 11l4.7-1.8A2 2 0 0 0 8.4 8Z"/><path d="M18 3v4m-2-2h4M18.5 16v3m-1.5-1.5h3"/>',
  power: '<path d="M12 3.5v8"/><path d="M7.2 6.3a7.5 7.5 0 1 0 9.6 0"/>',
  battery: '<rect x="2.5" y="7" width="17" height="10" rx="2.5"/><path d="M21.5 10.5v3"/>',
  batteryLow: '<rect x="2.5" y="7" width="17" height="10" rx="2.5"/><path d="M21.5 10.5v3M6 10.5v3"/>',
  plug: '<path d="M9 3.5v4m6-4v4M7 7.5h10v3a5 5 0 0 1-10 0Zm5 8v5"/>',
  leaf: '<path d="M5 19c0-8 5-13.5 14.5-14.5C19 14 13.5 19 5 19Z"/><path d="M5 19 13 11"/>',
  maximize: '<rect x="4" y="4" width="16" height="16" rx="3"/><path d="M4 8.5h16"/>',
  fullscreen: '<path d="M4 9V6a2 2 0 0 1 2-2h3m6 0h3a2 2 0 0 1 2 2v3m0 6v3a2 2 0 0 1-2 2h-3m-6 0H6a2 2 0 0 1-2-2v-3"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M12 2.8l1.4 2.4 2.7-.7.7 2.7 2.4 1.4-1.4 2.4 1.4 2.4-2.4 1.4-.7 2.7-2.7-.7L12 21.2l-1.4-2.4-2.7.7-.7-2.7-2.4-1.4L6.2 13 4.8 10.6 7.2 9.2l.7-2.7 2.7.7Z"/>',
  transfer: '<path d="M4 8h13m-3.5-3.5L17 8l-3.5 3.5M20 16H7m3.5-3.5L7 16l3.5 3.5"/>',
  play: '<path d="M8 5.5v13a1 1 0 0 0 1.5.86l10.5-6.5a1 1 0 0 0 0-1.72L9.5 4.64A1 1 0 0 0 8 5.5Z"/>',
  pause: '<rect x="6.5" y="5" width="3.6" height="14" rx="1.2"/><rect x="13.9" y="5" width="3.6" height="14" rx="1.2"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  upload: '<path d="M12 15.5V4m-4.5 4.5L12 4l4.5 4.5M4.5 15v2.5a2.5 2.5 0 0 0 2.5 2.5h10a2.5 2.5 0 0 0 2.5-2.5V15"/>',
  download: '<path d="M12 4v11.5m-4.5-4.5L12 15.5l4.5-4.5M4.5 15v2.5a2.5 2.5 0 0 0 2.5 2.5h10a2.5 2.5 0 0 0 2.5-2.5V15"/>',
  more: '<circle cx="6" cy="12" r="1.3"/><circle cx="12" cy="12" r="1.3"/><circle cx="18" cy="12" r="1.3"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  monitor: '<rect x="3" y="4.5" width="18" height="12" rx="2.5"/><path d="M9 20h6m-3-3.5V20"/>',
  monitors: '<rect x="2.5" y="6" width="12" height="8.5" rx="2"/><rect x="9.5" y="3.5" width="12" height="8.5" rx="2"/><path d="M6.5 19h4m-2-4.5V19"/>',
  folder: '<path d="M3.5 7.5A2.5 2.5 0 0 1 6 5h3.6l2 2H18a2.5 2.5 0 0 1 2.5 2.5v7A2.5 2.5 0 0 1 18 19H6a2.5 2.5 0 0 1-2.5-2.5Z"/>',
  pencil: '<path d="M4.5 19.5 5.3 16 15.7 5.6a2 2 0 0 1 2.8 0l-.1-.1a2 2 0 0 1 0 2.8L8 18.7Z"/><path d="m14 7.3 2.8 2.8"/>',
  trash: '<path d="M4.5 7h15M10 4h4M6.5 7l.8 11a2 2 0 0 0 2 1.9h5.4a2 2 0 0 0 2-1.9l.8-11M10 11v5m4-5v5"/>',
  x: '<path d="m6 6 12 12M18 6 6 18"/>',
  info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5m0-8.2v.2"/>',
  alert: '<path d="M12 4 2.8 19.5h18.4Z"/><path d="M12 10v4.5m0 2.3v.2"/>',
  rocket: '<path d="M13.5 4.5c3-1.5 6-1 6-1s.5 3-1 6L13 15l-4-4Z"/><path d="M9 11 5.5 10l2-3h4M13 15l1 3.5 3-2v-4M8 16c-1.5 0-3 1.5-3 3 1.5 0 3-1.5 3-3Z"/>',
  reset: '<path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3L4.5 9"/><path d="M4.5 4.5V9H9"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2.5"/><path d="M16 8V6.5A2.5 2.5 0 0 0 13.5 4h-7A2.5 2.5 0 0 0 4 6.5v7A2.5 2.5 0 0 0 6.5 16H8"/>',
  pointer: '<path d="M6 3.5 18.5 12l-5.6 1.3L10 18.8Z"/>',
  burst: '<circle cx="12" cy="12" r="2.2"/><path d="M12 3v3m0 12v3M3 12h3m12 0h3M5.6 5.6l2.1 2.1m8.6 8.6 2.1 2.1m0-12.8-2.1 2.1m-8.6 8.6-2.1 2.1"/>',
  gauge: '<path d="M4.2 17a8.5 8.5 0 1 1 15.6 0"/><path d="m12 13 3.5-4"/><circle cx="12" cy="13" r="1.2"/>',
  box: '<path d="M3.5 8 12 3.5 20.5 8v8L12 20.5 3.5 16Z"/><path d="M3.5 8 12 12.5 20.5 8M12 12.5v8"/>',
};

export function icon(name, cls = "") {
  return `<svg class="ico ${cls}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${P[name] ?? ""}</svg>`;
}

// Brand mark: a five-petal sakura blossom.
export const LOGO = `<svg class="logo" viewBox="0 0 48 48" aria-hidden="true">
  <defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffb3cf"/><stop offset="1" stop-color="#a68af0"/></linearGradient></defs>
  <g transform="translate(24 24)" fill="url(#lg)">
    ${[0, 72, 144, 216, 288].map((a) => `<path transform="rotate(${a})" d="M0 -2C-6 -6 -8 -14 -4 -19.5C-2.4 -21.3 -1 -20 0 -18C1 -20 2.4 -21.3 4 -19.5C8 -14 6 -6 0 -2Z"/>`).join("")}
  </g>
  <circle cx="24" cy="24" r="3.2" fill="#fff3f8"/>
</svg>`;
