// Hanami particle engine — shared by the wallpaper page and the settings preview.
// API contract: docs/CONTRACTS.md ("web/shared/effects.js").
//
// Design notes
// - Canvas 2D. Every particle is a pre-rendered sprite (offscreen canvas) drawn with one
//   setTransform + drawImage, so there is no per-particle path building, gradient creation or
//   shadowBlur at runtime. Glows use pre-rendered glow sprites + additive blending.
// - No per-frame allocations: particles and fx come from free lists, the active arrays are
//   compacted in place.
// - Live multipliers (speed/size/opacity/wind) are eased towards their targets; density changes
//   add/remove particles gradually with fades; a preset change crossfades old → new particles.
// - Sprites are cached per module, so several engines (e.g. settings previews) share them.

const TAU = Math.PI * 2;
const REF_AREA = 1920 * 1080;     // preset base counts are tuned for this many CSS px
const MAX_PARTICLES = 1400;
const MAX_FX = 360;
const MAX_BACKING_PX = 3840 * 2160; // cap the canvas backing store (8K monitors render at 4K)

export const PRESETS = Object.freeze([
  Object.freeze({ id: "none", name: "None", description: "No particles." }),
  Object.freeze({ id: "sakura", name: "Sakura", description: "Cherry blossom petals tumbling on a spring breeze." }),
  Object.freeze({ id: "snow", name: "Snow", description: "Soft snowflakes drifting down in layers of depth." }),
  Object.freeze({ id: "leaves", name: "Autumn leaves", description: "Maple leaves in reds, oranges and golds." }),
  Object.freeze({ id: "fireflies", name: "Fireflies", description: "Glowing fireflies wandering through the dusk." }),
  Object.freeze({ id: "rain", name: "Rain", description: "Light rain with tiny splashes at the bottom." }),
  Object.freeze({ id: "sparkles", name: "Sparkles", description: "Twinkling four-point stars drifting slowly." }),
]);

export const DEFAULT_EFFECT = Object.freeze({
  preset: "sakura",
  density: 1,
  speed: 1,
  size: 1,
  opacity: 1,
  wind: 0,
  interaction: "repel",
  interactionStrength: 1,
  interactionRadius: 160,
  clickBurst: true,
});

const PRESET_IDS = new Set(PRESETS.map((p) => p.id));
const INTERACTIONS = new Set(["none", "repel", "attract", "wind"]);

function num(v, lo, hi, def) {
  v = Number(v);
  if (!Number.isFinite(v)) return def;
  return v < lo ? lo : v > hi ? hi : v;
}

function sanitize(src, base) {
  const o = { ...base };
  if (!src || typeof src !== "object") return o;
  if (PRESET_IDS.has(src.preset)) o.preset = src.preset;
  if ("density" in src) o.density = num(src.density, 0.1, 3, base.density);
  if ("speed" in src) o.speed = num(src.speed, 0.2, 3, base.speed);
  if ("size" in src) o.size = num(src.size, 0.5, 2, base.size);
  if ("opacity" in src) o.opacity = num(src.opacity, 0, 1, base.opacity);
  if ("wind" in src) o.wind = num(src.wind, -1, 1, base.wind);
  if (INTERACTIONS.has(src.interaction)) o.interaction = src.interaction;
  if ("interactionStrength" in src) o.interactionStrength = num(src.interactionStrength, 0, 2, base.interactionStrength);
  if ("interactionRadius" in src) o.interactionRadius = num(src.interactionRadius, 40, 400, base.interactionRadius);
  if (typeof src.clickBurst === "boolean") o.clickBurst = src.clickBurst;
  return o;
}

const rand = (a, b) => a + Math.random() * (b - a);
const pick = (arr) => arr[(Math.random() * arr.length) | 0];
const sign = () => (Math.random() < 0.5 ? -1 : 1);
const byZ = (a, b) => a.z - b.z;

// ───────────────────────────── sprites ─────────────────────────────

function makeCanvas(w, h = w) {
  const c = document.createElement("canvas");
  c.width = w;
  c.height = h;
  return c;
}

function blurred(src, px) {
  const c = makeCanvas(src.width, src.height);
  const x = c.getContext("2d");
  x.filter = `blur(${px}px)`; // one-off at build time; ignored where unsupported
  x.drawImage(src, 0, 0);
  return c;
}

// A sprite is {c: canvas, k}: drawn box size = particle size × k.
const SPRITE_CACHE = Object.create(null);
function sprites(id) {
  return SPRITE_CACHE[id] || (SPRITE_CACHE[id] = SPRITE_BUILDERS[id]());
}

// Cherry blossom petal: base at +y, notched tip at −y, total height L.
function petalPath(x, L) {
  x.beginPath();
  x.moveTo(0, 0.5 * L);
  x.bezierCurveTo(-0.26 * L, 0.38 * L, -0.44 * L, 0.02 * L, -0.33 * L, -0.34 * L);
  x.bezierCurveTo(-0.27 * L, -0.5 * L, -0.08 * L, -0.53 * L, 0, -0.44 * L);
  x.bezierCurveTo(0.08 * L, -0.53 * L, 0.27 * L, -0.5 * L, 0.33 * L, -0.34 * L);
  x.bezierCurveTo(0.44 * L, 0.02 * L, 0.26 * L, 0.38 * L, 0, 0.5 * L);
  x.closePath();
}

function paintPetal(x, L, col) {
  petalPath(x, L);
  const g = x.createLinearGradient(0, 0.5 * L, 0, -0.5 * L);
  g.addColorStop(0, col.base);
  g.addColorStop(0.5, col.mid);
  g.addColorStop(1, col.tip);
  x.fillStyle = g;
  x.fill();
  const b = x.createRadialGradient(0, 0.5 * L, 0, 0, 0.5 * L, 0.42 * L);
  b.addColorStop(0, col.blush);
  b.addColorStop(1, "rgba(255,255,255,0)");
  x.fillStyle = b;
  x.fill();
  const h = x.createRadialGradient(-0.1 * L, -0.14 * L, 0, -0.1 * L, -0.14 * L, 0.42 * L);
  h.addColorStop(0, "rgba(255,255,255,0.5)");
  h.addColorStop(1, "rgba(255,255,255,0)");
  x.fillStyle = h;
  x.fill();
  x.strokeStyle = col.rim;
  x.lineWidth = L * 0.025;
  x.stroke();
  // faint central vein
  x.beginPath();
  x.moveTo(0, 0.44 * L);
  x.quadraticCurveTo(0.03 * L, 0.02 * L, 0, -0.3 * L);
  x.strokeStyle = col.vein;
  x.lineWidth = L * 0.014;
  x.stroke();
}

const SAKURA_COLORS = [
  { base: "#ef8fb1", mid: "#f9c3d5", tip: "#fff5f8", blush: "rgba(225,88,138,0.45)", vein: "rgba(222,120,160,0.30)", rim: "rgba(255,236,242,0.55)" },
  { base: "#f4a8c2", mid: "#fcd7e4", tip: "#ffffff", blush: "rgba(232,112,152,0.32)", vein: "rgba(230,140,170,0.26)", rim: "rgba(255,245,248,0.6)" },
  { base: "#e8769f", mid: "#f5b0c7", tip: "#ffe8f0", blush: "rgba(205,66,118,0.45)", vein: "rgba(210,100,140,0.30)", rim: "rgba(255,226,236,0.5)" },
];

function buildSakura() {
  const S = 96, L = 62;
  const petal = [[], [], []], blossom = [[], [], []];
  for (const col of SAKURA_COLORS) {
    const c = makeCanvas(S);
    const x = c.getContext("2d");
    x.translate(S / 2, S / 2);
    paintPetal(x, L, col);
    const k = S / L;
    petal[0].push({ c, k });
    petal[1].push({ c: blurred(c, 1.6), k });
    petal[2].push({ c: blurred(c, 4.5), k });
  }
  for (const col of [SAKURA_COLORS[1], SAKURA_COLORS[0]]) {
    const c = makeCanvas(S);
    const x = c.getContext("2d");
    const D = 80, Lp = D * 0.5;
    x.translate(S / 2, S / 2);
    for (let i = 0; i < 5; i++) {
      x.save();
      x.rotate((i * TAU) / 5);
      x.translate(0, -Lp * 0.5); // petal base (+y) lands on the flower centre
      paintPetal(x, Lp, col);
      x.restore();
    }
    const cg = x.createRadialGradient(0, 0, 0, 0, 0, D * 0.2);
    cg.addColorStop(0, "rgba(214,70,120,0.85)");
    cg.addColorStop(1, "rgba(214,70,120,0)");
    x.fillStyle = cg;
    x.beginPath();
    x.arc(0, 0, D * 0.2, 0, TAU);
    x.fill();
    x.strokeStyle = "rgba(226,110,150,0.85)";
    x.lineWidth = 0.9;
    x.fillStyle = "#f7d27c";
    for (let i = 0; i < 12; i++) {
      const a = (i / 12) * TAU + 0.2, r = D * (0.13 + (i % 3) * 0.02);
      x.beginPath();
      x.moveTo(Math.cos(a) * D * 0.04, Math.sin(a) * D * 0.04);
      x.lineTo(Math.cos(a) * r, Math.sin(a) * r);
      x.stroke();
      x.beginPath();
      x.arc(Math.cos(a) * r, Math.sin(a) * r, D * 0.014, 0, TAU);
      x.fill();
    }
    const k = S / (D * 0.6); // a blossom of "size" s reads about twice as wide as a petal of size s
    blossom[0].push({ c, k });
    blossom[1].push({ c: blurred(c, 1.6), k });
    blossom[2].push({ c: blurred(c, 4.5), k });
  }
  return { petal, blossom };
}

// Maple leaf outline in polar form: five pointed, serrated lobes, a notch for the stem.
function maplePath(x, R) {
  x.beginPath();
  const N = 220;
  for (let i = 0; i <= N; i++) {
    const t = -Math.PI + (i / N) * TAU;          // 0 = up
    const u = t / (Math.PI / 3);                   // lobe centres at u = 0, ±1, ±2 (60° apart)
    const au = Math.abs(u);
    const scale = 1 - 0.2 * Math.pow(Math.min(au, 2.5), 1.25); // smaller lower lobes
    let r;
    if (au > 2.5) {
      r = 0.28 * scale;                            // gap at the bottom for the stem
    } else {
      const d = Math.abs(u - Math.round(u));       // 0 at lobe tip, 0.5 at a sinus
      const tri = 1 - 2 * d;
      r = (0.28 + 0.72 * Math.pow(tri, 0.85)) * scale;
      r += 0.06 * Math.pow(Math.abs(Math.sin(u * Math.PI * 5)), 3) * tri * scale; // serrations
    }
    const px = Math.sin(t) * r * R, py = -Math.cos(t) * r * R;
    if (i === 0) x.moveTo(px, py);
    else x.lineTo(px, py);
  }
  x.closePath();
}

const LEAF_COLORS = [
  ["#6e1612", "#c2301f", "#ec6a3a"],
  ["#8a2a12", "#df5b26", "#f6a646"],
  ["#8a5a10", "#e3a12a", "#f8d866"],
  ["#7a1d18", "#d0412b", "#f38d42"],
  ["#5e1824", "#ad3239", "#e26a55"],
];

function buildLeaves() {
  const S = 96, R = 38;
  const out = [[], [], []];
  for (const [dark, mid, light] of LEAF_COLORS) {
    const c = makeCanvas(S);
    const x = c.getContext("2d");
    x.translate(S / 2, S / 2 - 2);
    maplePath(x, R);
    const g = x.createRadialGradient(0, R * 0.1, 0, 0, R * 0.1, R);
    g.addColorStop(0, light);
    g.addColorStop(0.55, mid);
    g.addColorStop(1, mid);
    x.fillStyle = g;
    x.fill();
    x.strokeStyle = dark;
    x.globalAlpha = 0.55;
    x.lineWidth = 1.2;
    x.stroke();
    x.globalAlpha = 1;
    const h = x.createRadialGradient(-R * 0.25, -R * 0.3, 0, -R * 0.25, -R * 0.3, R * 0.8);
    h.addColorStop(0, "rgba(255,240,200,0.28)");
    h.addColorStop(1, "rgba(255,240,200,0)");
    x.fillStyle = h;
    x.fill();
    // veins (lighter, anime style) from the stem base to each lobe tip
    x.strokeStyle = "rgba(255,226,170,0.42)";
    x.lineCap = "round";
    for (let i = -2; i <= 2; i++) {
      const t = (i * Math.PI) / 3; // matches the lobe centres in maplePath
      const len = R * 0.8 * (1 - 0.2 * Math.pow(Math.abs(i), 1.25));
      x.lineWidth = i === 0 ? 1.4 : 1.1;
      x.beginPath();
      x.moveTo(0, R * 0.16);
      x.quadraticCurveTo(Math.sin(t) * len * 0.4, R * 0.16 - Math.cos(t) * len * 0.5, Math.sin(t) * len, R * 0.16 - Math.cos(t) * len);
      x.stroke();
    }
    // stem
    x.strokeStyle = dark;
    x.lineWidth = 1.8;
    x.beginPath();
    x.moveTo(0, R * 0.14);
    x.quadraticCurveTo(R * 0.04, R * 0.45, R * 0.1, R * 0.72);
    x.stroke();
    const k = S / (2 * R);
    out[0].push({ c, k });
    out[1].push({ c: blurred(c, 1.6), k });
    out[2].push({ c: blurred(c, 4.5), k });
  }
  return out;
}

function radialSprite(S, stops) {
  const c = makeCanvas(S);
  const x = c.getContext("2d");
  const g = x.createRadialGradient(S / 2, S / 2, 0, S / 2, S / 2, S / 2);
  for (const [o, col] of stops) g.addColorStop(o, col);
  x.fillStyle = g;
  x.fillRect(0, 0, S, S);
  return c;
}

function buildSnow() {
  return {
    soft: {
      c: radialSprite(64, [[0, "rgba(255,255,255,1)"], [0.22, "rgba(255,255,255,0.92)"], [0.5, "rgba(236,244,255,0.38)"], [1, "rgba(236,244,255,0)"]]),
      k: 2,
    },
    bokeh: {
      c: radialSprite(64, [[0, "rgba(255,255,255,0.5)"], [0.55, "rgba(246,250,255,0.42)"], [0.78, "rgba(240,247,255,0.22)"], [1, "rgba(240,247,255,0)"]]),
      k: 1.15,
    },
  };
}

function buildFireflies() {
  const mk = (core, mid, outer) => ({
    c: radialSprite(64, [
      [0, "rgba(255,255,240,1)"],
      [0.07, `rgba(${core},1)`],
      [0.16, `rgba(${mid},0.7)`],
      [0.32, `rgba(${outer},0.26)`],
      [0.6, `rgba(${outer},0.07)`],
      [1, `rgba(${outer},0)`],
    ]),
    k: 1,
  });
  return [
    mk("244,255,190", "214,255,110", "160,230,80"),
    mk("250,255,200", "196,255,120", "130,220,90"),
    mk("255,246,190", "255,222,110", "240,190,70"),
  ];
}

function buildSparkles() {
  const tints = [[255, 255, 255], [255, 238, 190], [255, 214, 236], [214, 236, 255]];
  return tints.map(([r, g, b]) => {
    const S = 64, R = 30;
    const c = makeCanvas(S);
    const x = c.getContext("2d");
    x.translate(S / 2, S / 2);
    const glow = x.createRadialGradient(0, 0, 0, 0, 0, R * 0.55);
    glow.addColorStop(0, `rgba(${r},${g},${b},0.55)`);
    glow.addColorStop(0.4, `rgba(${r},${g},${b},0.14)`);
    glow.addColorStop(1, `rgba(${r},${g},${b},0)`);
    x.fillStyle = glow;
    x.fillRect(-S / 2, -S / 2, S, S);
    const star = (rad, w, alpha) => {
      x.beginPath();
      x.moveTo(0, -rad);
      x.quadraticCurveTo(w, -w, rad, 0);
      x.quadraticCurveTo(w, w, 0, rad);
      x.quadraticCurveTo(-w, w, -rad, 0);
      x.quadraticCurveTo(-w, -w, 0, -rad);
      const sg = x.createRadialGradient(0, 0, 0, 0, 0, rad);
      sg.addColorStop(0, `rgba(255,255,255,${alpha})`);
      sg.addColorStop(0.35, `rgba(${r},${g},${b},${alpha * 0.85})`);
      sg.addColorStop(1, `rgba(${r},${g},${b},0)`);
      x.fillStyle = sg;
      x.fill();
    };
    star(R, R * 0.09, 1);
    x.rotate(Math.PI / 4);
    star(R * 0.45, R * 0.07, 0.7);
    x.fillStyle = "rgba(255,255,255,1)";
    x.beginPath();
    x.arc(0, 0, 1.6, 0, TAU);
    x.fill();
    return { c, k: 1 };
  });
}

function buildRain() {
  const streak = makeCanvas(4, 64);
  const x = streak.getContext("2d");
  const g = x.createLinearGradient(0, 0, 0, 64);
  g.addColorStop(0, "rgba(200,220,255,0)");
  g.addColorStop(0.7, "rgba(214,230,255,0.5)");
  g.addColorStop(1, "rgba(238,246,255,0.95)");
  x.fillStyle = g;
  x.fillRect(0, 0, 4, 64);
  return { streak: { c: streak, k: 1 } };
}

function buildFx() {
  return {
    ring: radialSprite(128, [[0, "rgba(255,255,255,0)"], [0.7, "rgba(255,255,255,0)"], [0.83, "rgba(255,255,255,0.9)"], [0.9, "rgba(255,255,255,0.45)"], [1, "rgba(255,255,255,0)"]]),
    dot: radialSprite(16, [[0, "rgba(240,248,255,1)"], [0.45, "rgba(240,248,255,0.7)"], [1, "rgba(240,248,255,0)"]]),
  };
}
const RING_FRACTION = 0.83; // ring radius / sprite half-size

const SPRITE_BUILDERS = {
  sakura: buildSakura,
  leaves: buildLeaves,
  snow: buildSnow,
  fireflies: buildFireflies,
  sparkles: buildSparkles,
  rain: buildRain,
  fx: buildFx,
};

// ───────────────────────────── particles ─────────────────────────────

class Particle {
  constructor() {
    this.kind = null;
    this.sprite = null;
    this.x = 0; this.y = 0; this.z = 0;
    this.vx = 0; this.vy = 0;       // preset's own motion parameters
    this.ex = 0; this.ey = 0;       // extra velocity from interaction / bursts (decays)
    this.size = 1; this.alpha = 1;
    this.rot = 0; this.rotV = 0;
    this.flip = 0; this.flipV = 0;
    this.phase = 0; this.phaseV = 0; this.phase2 = 0;
    this.n1 = 0; this.n2 = 0; this.n3 = 0; this.n4 = 0;
    this.fade = 0; this.fadeDir = 1; this.fadeRate = 1;
    this.life = -1; this.age = 0;
    this.burst = false; this.dead = false;
  }
}

class Fx {
  constructor() {
    this.type = 0; // 1 ring, 2 drop
    this.x = 0; this.y = 0; this.vx = 0; this.vy = 0;
    this.age = 0; this.life = 1; this.size = 1; this.a = 1; this.sy = 1;
  }
}
const FX_RING = 1, FX_DROP = 2;

// Sprite drawn with rotation, a 3D-ish flip (scaleX = cos(flip)) and a slight scaleY wobble.
function drawTumble(ctx, p, e, a) {
  const spr = p.sprite;
  const s = p.size * spr.k * e.mSize * e.dpr;
  let fx = Math.cos(p.flip);
  if (fx > -0.07 && fx < 0.07) fx = fx < 0 ? -0.07 : 0.07;
  const sx = fx * s, sy = (0.84 + 0.16 * Math.sin(p.phase2)) * s;
  const c = Math.cos(p.rot), sn = Math.sin(p.rot);
  ctx.globalAlpha = a;
  ctx.setTransform(c * sx, sn * sx, -sn * sy, c * sy, p.x * e.dpr, p.y * e.dpr);
  ctx.drawImage(spr.c, -0.5, -0.5, 1, 1);
}

function drawRound(ctx, p, e, a) {
  const s = p.size * p.sprite.k * e.mSize * e.dpr;
  ctx.globalAlpha = a;
  ctx.setTransform(s, 0, 0, s, p.x * e.dpr, p.y * e.dpr);
  ctx.drawImage(p.sprite.c, -0.5, -0.5, 1, 1);
}

function fallerMargin(p) {
  return p.size * 1.6 + 10;
}

function placeFaller(p, e, how) {
  const m = fallerMargin(p);
  if (how === "screen") {
    p.x = rand(-m, e.w + m);
    p.y = rand(-m, e.h);
  } else if (how === "top") {
    p.x = rand(-m, e.w + m);
    p.y = -m - Math.random() * 60;
  }
  p.ex = 0;
  p.ey = 0;
}

function wrapFaller(p, e, kind) {
  const m = fallerMargin(p);
  if (p.burst) {
    if (p.y > e.h + m || p.x < -m || p.x > e.w + m || p.y < -e.h) p.dead = true;
    return;
  }
  if (p.y > e.h + m) {
    kind.init(p, e, "top");
    return;
  }
  if (p.y < -m - 400) p.y = -m - 400;
  if (p.x < -m) p.x += e.w + 2 * m;
  else if (p.x > e.w + m) p.x -= e.w + 2 * m;
}

function wrapTorus(p, e) {
  const m = p.size * 0.6 + 6;
  if (p.burst) {
    if (p.y > e.h + m || p.x < -m || p.x > e.w + m || p.y < -m) p.dead = true;
    return;
  }
  if (p.x < -m) p.x += e.w + 2 * m;
  else if (p.x > e.w + m) p.x -= e.w + 2 * m;
  if (p.y < -m) p.y += e.h + 2 * m;
  else if (p.y > e.h + m) p.y -= e.h + 2 * m;
}

// Petals / leaves: sway + tumble; fall faster when edge-on to the viewer.
function stepTumbler(p, dt, e, kind) {
  const sp = e.mSpeed, dts = dt * sp;
  p.phase += p.phaseV * dts;
  p.phase2 += p.n2 * dts;
  p.flip += p.flipV * dts;
  p.rot += p.rotV * dts;
  const edge = 1 - Math.abs(Math.cos(p.flip));
  const vx = (Math.sin(p.phase) * p.n1 + e.mWind * kind.windPx * (0.35 + 0.65 * p.z)) * sp;
  const vy = p.vy * (0.78 + 0.45 * edge) * sp;
  p.x += (vx + p.ex) * dt;
  p.y += (vy + p.ey) * dt;
  wrapFaller(p, e, kind);
}

function burstParticles(e, kind, x, y, n, vMin, vMax) {
  n = Math.round(n * Math.min(1.6, Math.max(0.7, Math.sqrt(e.cfg.density))));
  for (let i = 0; i < n && e.parts.length < MAX_PARTICLES; i++) {
    const p = e._alloc(kind);
    kind.init(p, e, "burst");
    p.x = x + rand(-4, 4);
    p.y = y + rand(-4, 4);
    const a = rand(0, TAU), v = rand(vMin, vMax);
    p.ex = Math.cos(a) * v;
    p.ey = Math.sin(a) * v - vMin * 0.4;
    p.burst = true;
    p.life = rand(1.4, 2.8);
    p.fade = 1;
    p.fadeRate = 1.1;
    e.parts.push(p);
  }
}

const KINDS = {
  sakura: {
    id: "sakura", base: 150, blend: "source-over", response: 1, windPx: 130,
    init(p, e, how) {
      if (how !== "top") {
        const S = sprites("sakura");
        const z = how === "burst" ? rand(0.3, 0.85) : Math.random();
        let layer;
        p.z = z;
        if (z < 0.22) { layer = 1; p.size = rand(7, 10); p.alpha = rand(0.55, 0.72); p.vy = rand(20, 34); }
        else if (z < 0.9) { layer = 0; p.size = rand(11, 18); p.alpha = rand(0.85, 1); p.vy = rand(36, 66); }
        else { layer = 2; p.size = rand(26, 38); p.alpha = rand(0.5, 0.7); p.vy = rand(78, 108); }
        if (Math.random() < 0.04) { p.sprite = pick(S.blossom[layer]); p.vy *= 1.15; }
        else p.sprite = pick(S.petal[layer]);
        p.rot = rand(0, TAU); p.rotV = rand(0.3, 1.2) * sign();
        p.flip = rand(0, TAU); p.flipV = rand(1.4, 3.4);
        p.phase = rand(0, TAU); p.phaseV = rand(0.5, 1.3);
        p.phase2 = rand(0, TAU); p.n2 = rand(1, 2.2);
        p.n1 = rand(14, 38);
      }
      placeFaller(p, e, how);
    },
    step(p, dt, e) { stepTumbler(p, dt, e, this); },
    draw: drawTumble,
    burst(e, x, y) { burstParticles(e, this, x, y, 22, 140, 420); },
  },

  leaves: {
    id: "leaves", base: 46, blend: "source-over", response: 0.85, windPx: 150,
    init(p, e, how) {
      if (how !== "top") {
        const S = sprites("leaves");
        const z = how === "burst" ? rand(0.3, 0.85) : Math.random();
        let layer;
        p.z = z;
        if (z < 0.2) { layer = 1; p.size = rand(12, 16); p.alpha = rand(0.6, 0.75); p.vy = rand(26, 40); }
        else if (z < 0.92) { layer = 0; p.size = rand(18, 28); p.alpha = rand(0.9, 1); p.vy = rand(42, 72); }
        else { layer = 2; p.size = rand(40, 54); p.alpha = rand(0.55, 0.7); p.vy = rand(80, 105); }
        p.sprite = pick(S[layer]);
        p.rot = rand(0, TAU); p.rotV = rand(0.4, 1.4) * sign();
        p.flip = rand(0, TAU); p.flipV = rand(0.8, 2.2);
        p.phase = rand(0, TAU); p.phaseV = rand(0.4, 1.0);
        p.phase2 = rand(0, TAU); p.n2 = rand(0.6, 1.4);
        p.n1 = rand(25, 60);
      }
      placeFaller(p, e, how);
    },
    step(p, dt, e) { stepTumbler(p, dt, e, this); },
    draw: drawTumble,
    burst(e, x, y) { burstParticles(e, this, x, y, 12, 140, 380); },
  },

  snow: {
    id: "snow", base: 220, blend: "source-over", response: 1, windPx: 90,
    init(p, e, how) {
      if (how !== "top") {
        const S = sprites("snow");
        const z = how === "burst" ? rand(0.45, 0.9) : Math.random();
        p.z = z;
        if (z < 0.45) { p.sprite = S.soft; p.size = rand(1.6, 3); p.alpha = rand(0.45, 0.7); p.vy = rand(16, 30); }
        else if (z < 0.93) { p.sprite = S.soft; p.size = rand(3, 5.5); p.alpha = rand(0.75, 0.95); p.vy = rand(30, 55); }
        else { p.sprite = S.bokeh; p.size = rand(9, 16); p.alpha = rand(0.3, 0.5); p.vy = rand(60, 85); }
        p.phase = rand(0, TAU); p.phaseV = rand(0.4, 1.1);
        p.n1 = rand(6, 22);
      }
      placeFaller(p, e, how);
    },
    step(p, dt, e) {
      const sp = e.mSpeed;
      p.phase += p.phaseV * dt * sp;
      const vx = (Math.sin(p.phase) * p.n1 + e.mWind * this.windPx * (0.3 + 0.7 * p.z)) * sp;
      p.x += (vx + p.ex) * dt;
      p.y += (p.vy * sp + p.ey) * dt;
      wrapFaller(p, e, this);
    },
    draw: drawRound,
    burst(e, x, y) { burstParticles(e, this, x, y, 26, 120, 360); },
  },

  fireflies: {
    id: "fireflies", base: 60, blend: "lighter", response: 1.2, windPx: 30,
    init(p, e, how) {
      const z = Math.random();
      p.z = z;
      p.sprite = pick(sprites("fireflies"));
      p.size = rand(24, 34) + z * 26;
      p.alpha = 0.6 + 0.4 * z;
      p.vy = rand(10, 28);               // cruising speed
      p.rot = rand(0, TAU);              // heading
      p.n1 = rand(0.2, 0.6); p.n2 = rand(0.7, 1.5); p.n3 = rand(0, TAU); p.n4 = rand(0, TAU);
      p.phaseV = rand(0.8, 2.0);
      p.phase = how === "burst" ? Math.PI / 2 : rand(0, TAU);
      if (how === "screen") {
        p.x = rand(0, e.w);
        p.y = e.h * (0.2 + 0.8 * Math.sqrt(Math.random())); // denser towards the ground
      }
      p.ex = 0; p.ey = 0;
    },
    step(p, dt, e) {
      const sp = e.mSpeed, dts = dt * sp, t = e.time;
      p.rot += (Math.sin(t * p.n1 + p.n3) + 0.6 * Math.sin(t * p.n2 + p.n4)) * 1.3 * dts;
      p.phase += p.phaseV * dts;
      const v = p.vy * sp;
      p.x += (Math.cos(p.rot) * v + e.mWind * this.windPx * sp + p.ex) * dt;
      p.y += (Math.sin(p.rot) * v * 0.7 + p.ey) * dt;
      wrapTorus(p, e);
    },
    draw(ctx, p, e, a) {
      let pulse = 0.5 + 0.5 * Math.sin(p.phase);
      pulse = 0.12 + 0.88 * pulse * pulse * pulse;
      const s = p.size * (0.82 + 0.18 * pulse) * e.mSize * e.dpr;
      ctx.globalAlpha = a * pulse;
      ctx.setTransform(s, 0, 0, s, p.x * e.dpr, p.y * e.dpr);
      ctx.drawImage(p.sprite.c, -0.5, -0.5, 1, 1);
    },
    burst(e, x, y) { burstParticles(e, this, x, y, 12, 80, 260); },
  },

  rain: {
    id: "rain", base: 300, blend: "source-over", response: 0.3, windPx: 520,
    init(p, e, how) {
      if (how !== "top") {
        const z = Math.random();
        p.z = z;
        p.sprite = sprites("rain").streak;
        if (z < 0.45) { p.size = rand(16, 26); p.n1 = 1; p.alpha = rand(0.22, 0.34); p.vy = rand(700, 900); }
        else if (z < 0.88) { p.size = rand(28, 42); p.n1 = 1.3; p.alpha = rand(0.34, 0.48); p.vy = rand(1000, 1300); }
        else { p.size = rand(50, 72); p.n1 = 1.8; p.alpha = rand(0.4, 0.55); p.vy = rand(1400, 1750); }
        p.n2 = (1 - z) * 0.06 + rand(0, 0.015); // landing height, as a fraction of h from the bottom
      }
      const vx = 40 + e.mWind * this.windPx;
      const drift = (vx / p.vy) * e.h; // spawn upwind so the screen stays evenly covered
      if (how === "screen") { p.x = rand(-20, e.w + 20); p.y = rand(-p.size, e.h); }
      else if (how === "top") { p.x = rand(-20, e.w + 20) - drift; p.y = -p.size - Math.random() * 120; }
      p.n3 = vx; p.n4 = p.vy;
      p.ex = 0; p.ey = 0;
    },
    step(p, dt, e) {
      const sp = e.mSpeed;
      const vx = (40 + e.mWind * this.windPx) * sp, vy = p.vy * sp;
      p.n3 = vx; p.n4 = vy;
      p.x += (vx + p.ex) * dt;
      p.y += (vy + p.ey) * dt;
      const floor = e.h * (1 - p.n2);
      if (p.y >= floor) {
        if (p.z > 0.3 && Math.random() < 0.65) e._splash(p.x, floor, p.z);
        if (p.burst) p.dead = true;
        else this.init(p, e, "top");
        return;
      }
      const m = 40;
      if (p.x < -m - e.w * 0.5) p.x += e.w * 1.5 + 2 * m;
      else if (p.x > e.w * 1.5 + m) p.x -= e.w * 1.5 + 2 * m;
    },
    draw(ctx, p, e, a) {
      const vx = p.n3 + p.ex, vy = p.n4 + p.ey;
      const n = Math.sqrt(vx * vx + vy * vy) || 1;
      const c = vy / n, s = -vx / n;
      const d = e.dpr, W = p.n1 * e.mSize * d, L = p.size * e.mSize * d;
      ctx.globalAlpha = a;
      ctx.setTransform(c * W, s * W, -s * L, c * L, p.x * d, p.y * d);
      ctx.drawImage(p.sprite.c, -0.5, -1, 1, 1);
    },
    burst(e, x, y) {
      for (let i = 0; i < 18; i++) {
        const a = rand(Math.PI * 1.05, Math.PI * 1.95), v = rand(120, 380);
        e._fx(FX_DROP, x, y, Math.cos(a) * v, Math.sin(a) * v, rand(0.6, 1.0), rand(1.8, 3.2), 0.8, 1);
      }
    },
  },

  sparkles: {
    id: "sparkles", base: 90, blend: "lighter", response: 0.9, windPx: 25,
    init(p, e, how) {
      const z = Math.random();
      p.z = z;
      p.sprite = pick(sprites("sparkles"));
      p.size = rand(10, 18) + z * 16;
      p.alpha = 0.6 + 0.4 * z;
      p.vx = rand(-8, 8); p.vy = rand(-10, 2);
      p.rot = rand(0, TAU); p.rotV = rand(-0.4, 0.4);
      if (how === "burst") { p.phase = Math.PI / 2; p.phaseV = rand(0.6, 1.2); }
      else { p.phase = rand(0, TAU); p.phaseV = rand(0.8, 2.4); }
      if (how === "screen") { p.x = rand(0, e.w); p.y = rand(0, e.h); }
      p.ex = 0; p.ey = 0;
    },
    step(p, dt, e) {
      const sp = e.mSpeed;
      p.phase += p.phaseV * dt * sp;
      p.rot += p.rotV * dt * sp;
      p.x += ((p.vx + e.mWind * this.windPx) * sp + p.ex) * dt;
      p.y += (p.vy * sp + p.ey) * dt;
      wrapTorus(p, e);
    },
    draw(ctx, p, e, a) {
      let tw = 0.5 + 0.5 * Math.sin(p.phase);
      tw = tw * tw * tw;
      const s = p.size * (0.45 + 0.55 * tw) * e.mSize * e.dpr;
      const c = Math.cos(p.rot) * s, sn = Math.sin(p.rot) * s;
      ctx.globalAlpha = a * (0.12 + 0.88 * tw);
      ctx.setTransform(c, sn, -sn, c, p.x * e.dpr, p.y * e.dpr);
      ctx.drawImage(p.sprite.c, -0.5, -0.5, 1, 1);
    },
    burst(e, x, y) { burstParticles(e, this, x, y, 18, 90, 300); },
  },
};

// ───────────────────────────── engine ─────────────────────────────

export class EffectsEngine {
  constructor(canvas, config = DEFAULT_EFFECT) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.cfg = sanitize(config, DEFAULT_EFFECT);
    this.kind = KINDS[this.cfg.preset] || null;
    this.parts = [];
    this.free = [];
    this.fxList = [];
    this.fxFree = [];
    this.w = 0;
    this.h = 0;
    this.dpr = 1;
    // eased live multipliers
    this.mSpeed = this.cfg.speed;
    this.mSize = this.cfg.size;
    this.mOpacity = this.cfg.opacity;
    this.mWind = this.cfg.wind;
    this.time = 0;
    // loop
    this.fpsCap = 60;
    this.running = false;
    this._raf = 0;
    this._suspended = false;
    this._frameTs = 0;
    this._prevTs = 0;
    this._loop = this._loop.bind(this);
    // pointer
    this.px = 0; this.py = 0; this.pInside = false;
    this.pvx = 0; this.pvy = 0; this._pT = 0;
    /** Read-only diagnostics (not part of the contract): rendered fps and live particle count. */
    this.stats = { fps: 0, particles: 0 };
    this._fpsN = 0; this._fpsT = 0;

    this.resize();
    this._seed(false);
    if (typeof ResizeObserver !== "undefined") {
      this._ro = new ResizeObserver(() => this.resize());
      this._ro.observe(canvas);
    }
  }

  /** Current (sanitized) config, as a copy. */
  get config() {
    return { ...this.cfg };
  }

  setConfig(partialConfig) {
    const prev = this.cfg;
    this.cfg = sanitize(partialConfig, prev);
    if (this.cfg.preset !== prev.preset) {
      this.kind = KINDS[this.cfg.preset] || null;
      for (const p of this.parts) {
        p.fadeDir = -1;
        p.fadeRate = 1 / 0.7;
      }
      this._seed(false);
    }
    // density changes are applied gradually in _update()
    if (!this.running) {
      // paused: no frames will animate the change, so show its end state once
      this._settleStatic();
      this._render();
    }
    this._wake();
  }

  setPointer(x, y, inside = true) {
    x = Number(x);
    y = Number(y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return;
    const now = performance.now();
    if (inside && this.pInside && this._pT) {
      const dt = (now - this._pT) / 1000;
      if (dt > 0.001) {
        const vx = Math.max(-4000, Math.min(4000, (x - this.px) / dt));
        const vy = Math.max(-4000, Math.min(4000, (y - this.py) / dt));
        this.pvx = this.pvx * 0.5 + vx * 0.5;
        this.pvy = this.pvy * 0.5 + vy * 0.5;
      }
    } else {
      this.pvx = 0;
      this.pvy = 0;
    }
    this.px = x;
    this.py = y;
    this.pInside = !!inside;
    this._pT = now;
  }

  click(x, y) {
    if (!this.cfg.clickBurst || !this.kind || !this.running) return;
    x = Number(x);
    y = Number(y);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return;
    // ripple: two soft rings
    this._fx(FX_RING, x, y, 0, 0, 0.95, 78, 0.32, 1);
    const f = this._fx(FX_RING, x, y, 0, 0, 0.85, 50, 0.2, 1);
    if (f) f.age = -0.12;
    // shockwave on nearby particles
    const R = this.cfg.interactionRadius * 1.3, R2 = R * R;
    for (const p of this.parts) {
      const dx = p.x - x, dy = p.y - y, d2 = dx * dx + dy * dy;
      if (d2 < R2 && d2 > 0.01) {
        const d = Math.sqrt(d2), f2 = 1 - d / R;
        const k = (320 * f2 * f2 * p.kind.response * (0.35 + 0.65 * p.z)) / d;
        p.ex += dx * k;
        p.ey += dy * k;
      }
    }
    this.kind.burst(this, x, y);
    this.parts.sort(byZ);
    this._wake();
  }

  setFpsCap(fps) {
    this.fpsCap = num(fps, 1, 240, 60);
  }

  start() {
    if (this.running) return;
    this.running = true;
    this._suspended = false;
    this._frameTs = 0;
    this._prevTs = 0;
    this._raf = requestAnimationFrame(this._loop);
  }

  stop() {
    this.running = false;
    this._suspended = false;
    if (this._raf) cancelAnimationFrame(this._raf);
    this._raf = 0;
  }

  resize() {
    const c = this.canvas;
    if (!c) return;
    const cw = c.clientWidth, ch = c.clientHeight;
    const oldW = this.w, oldH = this.h;
    if (cw <= 0 || ch <= 0) {
      this.w = 0;
      this.h = 0;
      return;
    }
    let dpr = window.devicePixelRatio || 1;
    if (cw * ch * dpr * dpr > MAX_BACKING_PX) dpr = Math.sqrt(MAX_BACKING_PX / (cw * ch));
    const bw = Math.max(1, Math.round(cw * dpr)), bh = Math.max(1, Math.round(ch * dpr));
    if (c.width !== bw) c.width = bw;
    if (c.height !== bh) c.height = bh;
    this.dpr = bw / cw;
    this.w = cw;
    this.h = ch;
    if (oldW > 0 && oldH > 0) {
      if (oldW !== cw || oldH !== ch) {
        const sx = cw / oldW, sy = ch / oldH;
        for (const p of this.parts) {
          p.x *= sx;
          p.y *= sy;
        }
      }
    } else if (this.parts.length) {
      // first real layout: the particles were seeded against a 0×0 canvas
      for (const p of this.parts) p.kind.init(p, this, "screen");
    }
    if (!this.running || this._suspended) {
      if (!this.running) this._settleStatic();
      this._render();
    }
  }

  destroy() {
    this.stop();
    if (this._ro) this._ro.disconnect();
    this._ro = null;
    this.parts.length = 0;
    this.free.length = 0;
    this.fxList.length = 0;
    this.fxFree.length = 0;
    if (this.ctx && this.canvas) {
      this.ctx.setTransform(1, 0, 0, 1, 0, 0);
      this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
    }
    this.ctx = null;
    this.canvas = null;
  }

  // ── internals ──

  _target() {
    if (!this.kind) return 0;
    const area = this.w * this.h;
    if (area <= 0) return 0;
    const scale = Math.min(3, Math.max(0.1, Math.pow(area / REF_AREA, 0.8)));
    return Math.min(MAX_PARTICLES, Math.round(this.kind.base * this.cfg.density * scale));
  }

  _alloc(kind) {
    const p = this.free.pop() || new Particle();
    p.kind = kind;
    p.ex = 0; p.ey = 0;
    p.fade = 0; p.fadeDir = 1; p.fadeRate = 1;
    p.life = -1; p.age = 0;
    p.burst = false; p.dead = false;
    return p;
  }

  _spawn(n, fadeIn) {
    const kind = this.kind;
    for (let i = 0; i < n && this.parts.length < MAX_PARTICLES; i++) {
      const p = this._alloc(kind);
      kind.init(p, this, "screen");
      if (fadeIn) p.fadeRate = 1 / 1.1;
      else p.fade = 1;
      this.parts.push(p);
    }
    this.parts.sort(byZ);
  }

  _seed(instant) {
    if (!this.kind) return;
    this._spawn(this._target(), !instant);
  }

  // While stopped there are no frames to animate fades in, so snap to the end state.
  _settleStatic() {
    let w = 0, ambient = 0;
    for (const p of this.parts) {
      if (p.fadeDir < 0 || p.burst || p.kind !== this.kind) {
        this.free.push(p);
        continue;
      }
      p.fade = 1;
      this.parts[w++] = p;
      ambient++;
    }
    this.parts.length = w;
    const diff = this._target() - ambient;
    if (diff > 0) this._spawn(diff, false);
    else if (diff < 0) {
      for (let i = Math.max(0, w + diff); i < w; i++) this.free.push(this.parts[i]);
      this.parts.length = Math.max(0, w + diff);
    }
    this.fxList.length = 0;
    this.mSpeed = this.cfg.speed;
    this.mSize = this.cfg.size;
    this.mOpacity = this.cfg.opacity;
    this.mWind = this.cfg.wind;
  }

  _wake() {
    if (this.running && this._suspended) {
      this._suspended = false;
      this._frameTs = 0;
      this._prevTs = 0;
      this._raf = requestAnimationFrame(this._loop);
    }
  }

  _fx(type, x, y, vx, vy, life, size, a, sy) {
    if (this.fxList.length >= MAX_FX) return null;
    const f = this.fxFree.pop() || new Fx();
    f.type = type; f.x = x; f.y = y; f.vx = vx; f.vy = vy;
    f.age = 0; f.life = life; f.size = size; f.a = a; f.sy = sy;
    this.fxList.push(f);
    return f;
  }

  _splash(x, y, z) {
    const s = 0.5 + z;
    this._fx(FX_RING, x, y, 0, 0, 0.32, 5 + 7 * z, 0.5, 0.28);
    for (let i = 0; i < 2; i++) {
      this._fx(FX_DROP, x, y, rand(-45, 45) * s, -rand(60, 150) * s, rand(0.25, 0.4), 1 + z, 0.55, 1);
    }
  }

  _loop(ts) {
    this._raf = 0;
    if (!this.running) return;
    const interval = 1000 / this.fpsCap;
    const since = ts - this._frameTs;
    if (this._frameTs && since < interval - 1.5) {
      this._raf = requestAnimationFrame(this._loop);
      return;
    }
    // keep the cadence aligned to the cap without drifting (e.g. 60 fps on a 144 Hz display)
    if (!this._frameTs || since > interval * 3 || since < interval) this._frameTs = ts;
    else this._frameTs = ts - (since % interval);

    const dt = this._prevTs ? Math.min(0.05, Math.max(0, (ts - this._prevTs) / 1000)) : 1 / this.fpsCap;
    this._prevTs = ts;
    this._update(dt);
    this._render();

    this._fpsN++;
    if (ts - this._fpsT >= 1000) {
      this.stats.fps = Math.round((this._fpsN * 1000) / (ts - this._fpsT));
      this._fpsN = 0;
      this._fpsT = ts;
    }
    this.stats.particles = this.parts.length;

    if (!this.kind && this.parts.length === 0 && this.fxList.length === 0) {
      this._suspended = true; // nothing to animate ("none"): no frames until something changes
      this.stats.fps = 0;
      return;
    }
    this._raf = requestAnimationFrame(this._loop);
  }

  _update(dt) {
    const cfg = this.cfg;
    const ease = 1 - Math.exp(-dt * 3);
    this.mSpeed += (cfg.speed - this.mSpeed) * ease;
    this.mSize += (cfg.size - this.mSize) * ease;
    this.mOpacity += (cfg.opacity - this.mOpacity) * ease;
    this.mWind += (cfg.wind - this.mWind) * ease;
    this.time += dt * this.mSpeed;

    // cursor velocity decays (pointer messages only arrive on change)
    const pdecay = Math.exp(-dt * 8);
    this.pvx *= pdecay;
    this.pvy *= pdecay;

    const mode = this.pInside && cfg.interactionStrength > 0 ? cfg.interaction : "none";
    const R = cfg.interactionRadius, R2 = R * R, S = cfg.interactionStrength;
    const px = this.px, py = this.py, pvx = this.pvx, pvy = this.pvy;
    const drag = Math.exp(-dt * 2.2);
    const windK = Math.min(1, dt * 6);

    const parts = this.parts;
    let w = 0, ambient = 0;
    for (let i = 0; i < parts.length; i++) {
      const p = parts[i];
      if (p.fadeDir > 0) {
        if (p.fade < 1) p.fade = Math.min(1, p.fade + dt * p.fadeRate);
      } else {
        p.fade -= dt * p.fadeRate;
        if (p.fade <= 0) p.dead = true;
      }
      if (p.life > 0) {
        p.age += dt;
        if (p.age >= p.life && p.fadeDir > 0) p.fadeDir = -1;
      }
      if (!p.dead) {
        if (mode !== "none") {
          const dx = p.x - px, dy = p.y - py, d2 = dx * dx + dy * dy;
          if (d2 < R2 && d2 > 0.25) {
            const d = Math.sqrt(d2);
            let f = 1 - d / R;
            f *= f;
            const resp = p.kind.response * (0.35 + 0.65 * p.z) * S;
            if (mode === "repel") {
              const k = (2600 * f * resp * dt) / d;
              p.ex += dx * k;
              p.ey += dy * k;
            } else if (mode === "attract") {
              // Spring towards a loose ring around the cursor plus a tangential swirl, so
              // particles drift in and orbit instead of collapsing onto one point.
              const lin = 1 - d / R;
              const nx = dx / d, ny = dy / d;
              const radial = (d - R * 0.32) / R;            // >0 outside the ring, <0 inside
              const kr = 2800 * lin * radial * resp * dt;
              const kt = 380 * lin * resp * dt;
              p.ex += -nx * kr - ny * kt;
              p.ey += -ny * kr + nx * kt;
              const damp = 1 - Math.min(0.3, lin * dt * 1.5);
              p.ex *= damp;
              p.ey *= damp;
            } else {
              const k = Math.min(1, f * windK * resp);
              p.ex += (pvx * 0.85 - p.ex) * k;
              p.ey += (pvy * 0.6 - p.ey) * k;
            }
          }
        }
        p.kind.step(p, dt, this);
        p.ex *= drag;
        p.ey *= drag;
        if (p.ex > 900) p.ex = 900; else if (p.ex < -900) p.ex = -900;
        if (p.ey > 900) p.ey = 900; else if (p.ey < -900) p.ey = -900;
      }
      if (p.dead) {
        this.free.push(p);
        continue;
      }
      if (!p.burst && p.fadeDir > 0 && p.kind === this.kind) ambient++;
      parts[w++] = p;
    }
    parts.length = w;

    // gradual density adjustment (no popping)
    const diff = this._target() - ambient;
    if (diff > 0) {
      this._spawn(Math.ceil(diff * Math.min(1, dt * 2.5)), true);
    } else if (diff < 0) {
      let n = Math.ceil(-diff * Math.min(1, dt * 2.5));
      const len = parts.length, start = (Math.random() * len) | 0;
      for (let j = 0; j < len && n > 0; j++) {
        const p = parts[(start + j) % len];
        if (!p.burst && p.fadeDir > 0 && p.kind === this.kind) {
          p.fadeDir = -1;
          p.fadeRate = 1;
          n--;
        }
      }
    }

    // fx
    const fx = this.fxList;
    let fw = 0;
    for (let i = 0; i < fx.length; i++) {
      const f = fx[i];
      f.age += dt;
      if (f.age >= f.life) {
        this.fxFree.push(f);
        continue;
      }
      if (f.type === FX_DROP && f.age > 0) {
        f.vy += 900 * dt;
        f.x += f.vx * dt;
        f.y += f.vy * dt;
      }
      fx[fw++] = f;
    }
    fx.length = fw;
  }

  _render() {
    const ctx = this.ctx;
    if (!ctx) return;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalCompositeOperation = "source-over";
    ctx.globalAlpha = 1;
    ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
    if (this.w <= 0) return;
    const op = this.mOpacity;
    if (op <= 0.002) return;
    let blend = "source-over";
    const parts = this.parts;
    for (let i = 0; i < parts.length; i++) {
      const p = parts[i];
      const a = p.alpha * p.fade * op;
      if (a < 0.004) continue;
      const k = p.kind;
      if (k.blend !== blend) {
        blend = k.blend;
        ctx.globalCompositeOperation = blend;
      }
      k.draw(ctx, p, this, a);
    }
    const fx = this.fxList;
    if (fx.length) {
      const spr = sprites("fx");
      const d = this.dpr;
      ctx.globalCompositeOperation = "source-over";
      for (let i = 0; i < fx.length; i++) {
        const f = fx[i];
        if (f.age < 0) continue;
        const t = f.age / f.life;
        if (f.type === FX_RING) {
          const e = 1 - (1 - t) * (1 - t) * (1 - t);
          const r = f.size * (0.15 + 0.85 * e);
          const s = ((r * 2) / RING_FRACTION) * d;
          ctx.globalAlpha = f.a * (1 - t) * (1 - t) * op;
          ctx.setTransform(s, 0, 0, s * f.sy, f.x * d, f.y * d);
          ctx.drawImage(spr.ring, -0.5, -0.5, 1, 1);
        } else {
          const s = f.size * 2 * d;
          ctx.globalAlpha = f.a * (1 - t) * op;
          ctx.setTransform(s, 0, 0, s, f.x * d, f.y * d);
          ctx.drawImage(spr.dot, -0.5, -0.5, 1, 1);
        }
      }
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalAlpha = 1;
    ctx.globalCompositeOperation = "source-over";
  }
}
