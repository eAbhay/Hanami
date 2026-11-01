// Built-in procedural scenes ("builtin:<scene>"): twilight, dawn, night.
//
// Everything expensive (sky gradients, hills, star field, moon, cloud sprites) is pre-rendered
// once per size into offscreen canvases. A frame is then just a few drawImage calls: sky,
// drifting cloud sprites, hills, and a handful of twinkling stars. Scenes render at a capped
// backing resolution (long side ≤ 1920 px) and are upscaled by CSS — they are soft by design.
// They animate at SCENE_FPS and only while started; stop() cancels the rAF loop entirely.

const TAU = Math.PI * 2;
const SCENE_FPS = 24;
const MAX_LONG_SIDE = 1920;

export const SCENES = ["twilight", "dawn", "night"];

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function makeCanvas(w, h) {
  const c = document.createElement("canvas");
  c.width = Math.max(1, Math.round(w));
  c.height = Math.max(1, Math.round(h));
  return c;
}

function vGradient(x, y0, y1, stops) {
  const g = x.createLinearGradient(0, y0, 0, y1);
  for (const [o, c] of stops) g.addColorStop(o, c);
  return g;
}

function radial(x, cx, cy, r, stops) {
  const g = x.createRadialGradient(cx, cy, 0, cx, cy, r);
  for (const [o, c] of stops) g.addColorStop(o, c);
  return g;
}

// Soft cloud sprite: overlapping ellipse puffs with a flat-ish base, vertically graded
// (top colour → lit underside), blurred once.
function makeCloud(rng, cw, ch, top, bottom, rim, blur) {
  const pad = Math.ceil(blur * 3 + 4);
  const W = cw + pad * 2, H = ch * 2.2 + pad * 2;
  const tmp = makeCanvas(W, H);
  const x = tmp.getContext("2d");
  const base = pad + ch * 1.7;
  x.fillStyle = vGradient(x, base - ch * 1.5, base, [[0, top], [1, bottom]]);
  x.beginPath();
  // flat, wide base
  const nb = 4 + Math.floor(rng() * 3);
  for (let i = 0; i < nb; i++) {
    const u = (i + 0.5) / nb;
    const cx = pad + u * cw, rx = (cw / nb) * (0.75 + rng() * 0.3), ry = ch * (0.18 + rng() * 0.08);
    const cy = base - ry - rng() * ch * 0.12; // slightly ragged underside
    x.moveTo(cx + rx, cy);
    x.ellipse(cx, cy, rx, ry, 0, 0, TAU);
  }
  // cumulus bumps on top, tallest towards the middle
  const n = 9 + Math.floor(rng() * 7);
  for (let i = 0; i < n; i++) {
    const u = 0.1 + 0.8 * ((i + 0.5) / n) + (rng() - 0.5) * 0.06;
    const cx = pad + u * cw;
    const env = 1 - Math.pow(Math.abs(u - 0.5) * 2, 1.4) * 0.7;
    const r = ch * (0.22 + 0.5 * rng()) * env;
    const cy = base - ch * 0.25 - r * 0.6;
    x.moveTo(cx + r * 1.25, cy);
    x.ellipse(cx, cy, r * 1.25, r, 0, 0, TAU);
  }
  x.fill();
  // lit underside / rim
  x.globalCompositeOperation = "source-atop";
  // (fade from the rim colour at alpha 0, not transparent black, or the gradient greys out)
  const rimClear = rim.replace(/,\s*[\d.]+\)$/, ",0)");
  x.fillStyle = vGradient(x, base - ch * 0.9, base, [[0, rimClear], [1, rim]]);
  x.fillRect(0, 0, W, H);
  x.globalCompositeOperation = "source-over";
  const out = makeCanvas(W, H);
  const o = out.getContext("2d");
  o.filter = `blur(${blur}px)`;
  o.drawImage(tmp, 0, 0);
  return { c: out, pad };
}

// Silhouette ridge from (0,H) across to (W,H); y in px.
function ridge(x, W, H, rng, baseY, amp, rough) {
  const ph = [rng() * TAU, rng() * TAU, rng() * TAU, rng() * TAU, rng() * TAU];
  const fr = [1 + rng(), 2.3 + rng() * 1.5, 5 + rng() * 3, 11 + rng() * 6, 37 + rng() * 20];
  const am = [0.5, 0.28, 0.13, 0.035, 0.004 + rough * 0.4];
  x.beginPath();
  x.moveTo(0, H);
  const step = Math.max(2, W / 480);
  for (let px = 0; px <= W + step; px += step) {
    const u = px / W;
    let s = 0;
    for (let i = 0; i < 5; i++) s += am[i] * Math.sin(TAU * fr[i] * u + ph[i]);
    x.lineTo(px, baseY - amp * (0.5 + 0.5 * s));
  }
  x.lineTo(W, H);
  x.closePath();
}

function glowDot(size, rgb) {
  const c = makeCanvas(size, size);
  const x = c.getContext("2d");
  x.fillStyle = radial(x, size / 2, size / 2, size / 2, [
    [0, `rgba(${rgb},1)`],
    [0.18, `rgba(${rgb},0.8)`],
    [0.45, `rgba(${rgb},0.15)`],
    [1, `rgba(${rgb},0)`],
  ]);
  x.fillRect(0, 0, size, size);
  return c;
}

function scatterStars(x, rng, W, H, n, s, yMax, alphaMul) {
  for (let i = 0; i < n; i++) {
    const sx = rng() * W, sy = Math.pow(rng(), 1.3) * yMax;
    const r = (0.35 + Math.pow(rng(), 3) * 1.1) * s;
    const fadeToHorizon = 1 - sy / yMax;
    const a = (0.25 + 0.75 * rng()) * alphaMul * (0.3 + 0.7 * fadeToHorizon);
    const tint = rng();
    x.fillStyle = tint < 0.15 ? `rgba(255,226,200,${a})` : tint < 0.35 ? `rgba(200,220,255,${a})` : `rgba(255,255,255,${a})`;
    x.beginPath();
    x.arc(sx, sy, r, 0, TAU);
    x.fill();
  }
}

// ───────────────────────────── scene definitions ─────────────────────────────

const DEFS = {
  twilight: {
    seed: 7,
    build(sc) {
      const { W, H, s, rng } = sc;
      const sky = makeCanvas(W, H);
      const x = sky.getContext("2d");
      x.fillStyle = vGradient(x, 0, H, [
        [0, "#1d1b4b"], [0.25, "#3f3274"], [0.45, "#7d4a8a"], [0.6, "#c0617e"],
        [0.72, "#ec8a66"], [0.8, "#ffb877"], [0.88, "#ffd59a"], [1, "#ffe2b0"],
      ]);
      x.fillRect(0, 0, W, H);
      x.fillStyle = radial(x, W * 0.64, H * 0.82, W * 0.5, [
        [0, "rgba(255,236,196,0.85)"], [0.12, "rgba(255,196,140,0.45)"], [0.4, "rgba(250,140,120,0.16)"], [1, "rgba(240,120,130,0)"],
      ]);
      x.fillRect(0, 0, W, H);
      scatterStars(x, rng, W, H, 70, s, H * 0.32, 0.55);
      sc.sky = sky;

      sc.clouds = [];
      for (let i = 0; i < 7; i++) {
        const yU = 0.16 + (i / 8) * 0.46 + rng() * 0.05;
        const low = (yU - 0.16) / 0.5; // 0 high → 1 low (closer to the sun, warmer)
        const cw = W * (0.16 + rng() * 0.26), ch = cw * (0.11 + rng() * 0.07);
        const top = low < 0.5 ? "rgb(92,66,128)" : "rgb(150,86,130)";
        const bottom = low < 0.5 ? "rgb(214,128,150)" : "rgb(255,170,138)";
        const cl = makeCloud(rng, cw, ch, top, bottom, "rgba(255,214,170,0.9)", 3 * s + 1.5);
        sc.clouds.push({ ...cl, x0: ((i * 0.618 + rng() * 0.1) % 1) * W, y: H * yU - cl.c.height * 0.6, v: (3 + rng() * 6 + low * 4) * s, a: 0.55 + 0.35 * rng() });
      }

      const hills = makeCanvas(W, H);
      const h = hills.getContext("2d");
      ridge(h, W, H, rng, H * 0.82, H * 0.12, 0);
      h.fillStyle = vGradient(h, H * 0.7, H, [[0, "#a5719a"], [1, "#c58a8e"]]);
      h.fill();
      h.fillStyle = vGradient(h, H * 0.76, H * 0.9, [[0, "rgba(255,190,150,0)"], [1, "rgba(255,190,150,0.35)"]]);
      h.fillRect(0, H * 0.76, W, H * 0.24);
      ridge(h, W, H, rng, H * 0.88, H * 0.1, 0.01);
      h.fillStyle = vGradient(h, H * 0.78, H, [[0, "#5d3a68"], [1, "#4a2f58"]]);
      h.fill();
      ridge(h, W, H, rng, H * 0.95, H * 0.1, 0.02);
      h.fillStyle = vGradient(h, H * 0.85, H, [[0, "#2a1b3b"], [1, "#160f22"]]);
      h.fill();
      sc.hills = hills;
    },
  },

  dawn: {
    seed: 21,
    build(sc) {
      const { W, H, s, rng } = sc;
      const sky = makeCanvas(W, H);
      const x = sky.getContext("2d");
      x.fillStyle = vGradient(x, 0, H, [
        [0, "#86acdf"], [0.3, "#b9bfe6"], [0.5, "#eec3d6"], [0.66, "#ffd3bd"],
        [0.8, "#ffe9cc"], [1, "#fff4e2"],
      ]);
      x.fillRect(0, 0, W, H);
      const sx = W * 0.3, sy = H * 0.7;
      // faint light rays
      x.save();
      x.translate(sx, sy);
      for (let i = 0; i < 8; i++) {
        const a = -Math.PI / 2 + (rng() - 0.5) * 2.2, w = 0.015 + rng() * 0.035;
        x.fillStyle = radial(x, 0, 0, W * 0.8, [[0, "rgba(255,248,230,0.16)"], [0.5, "rgba(255,248,230,0.05)"], [1, "rgba(255,248,230,0)"]]);
        x.beginPath();
        x.moveTo(0, 0);
        x.arc(0, 0, W * 0.8, a - w, a + w);
        x.closePath();
        x.fill();
      }
      x.restore();
      x.fillStyle = radial(x, sx, sy, W * 0.55, [
        [0, "rgba(255,252,240,0.9)"], [0.06, "rgba(255,244,214,0.7)"], [0.2, "rgba(255,220,180,0.28)"],
        [0.5, "rgba(255,206,190,0.08)"], [1, "rgba(255,206,190,0)"],
      ]);
      x.fillRect(0, 0, W, H);
      x.fillStyle = radial(x, sx, sy, H * 0.045, [[0, "rgba(255,255,250,1)"], [0.7, "rgba(255,252,236,0.95)"], [1, "rgba(255,248,226,0)"]]);
      x.beginPath();
      x.arc(sx, sy, H * 0.045, 0, TAU);
      x.fill();
      sc.sky = sky;

      sc.clouds = [];
      for (let i = 0; i < 7; i++) {
        const yU = 0.12 + (i / 7) * 0.48 + rng() * 0.05;
        const cw = W * (0.14 + rng() * 0.24), ch = cw * (0.12 + rng() * 0.08);
        const cl = makeCloud(rng, cw, ch, "rgb(255,255,255)", "rgb(246,206,214)", "rgba(255,228,200,0.85)", 3.5 * s + 1.5);
        sc.clouds.push({ ...cl, x0: ((i * 0.618 + rng() * 0.1) % 1) * W, y: H * yU - cl.c.height * 0.6, v: (4 + rng() * 7) * s, a: 0.45 + 0.35 * rng() });
      }

      const hills = makeCanvas(W, H);
      const h = hills.getContext("2d");
      ridge(h, W, H, rng, H * 0.8, H * 0.14, 0);
      h.fillStyle = vGradient(h, H * 0.66, H, [[0, "#cdbfdc"], [1, "#e2cfd9"]]);
      h.fill();
      h.fillStyle = vGradient(h, H * 0.74, H * 0.9, [[0, "rgba(255,240,230,0)"], [1, "rgba(255,240,230,0.55)"]]);
      h.fillRect(0, H * 0.74, W, H * 0.26);
      ridge(h, W, H, rng, H * 0.88, H * 0.1, 0.01);
      h.fillStyle = vGradient(h, H * 0.78, H, [[0, "#a9a3cc"], [1, "#b8b0d2"]]);
      h.fill();
      h.fillStyle = vGradient(h, H * 0.84, H * 0.95, [[0, "rgba(255,244,236,0)"], [1, "rgba(255,244,236,0.4)"]]);
      h.fillRect(0, H * 0.84, W, H * 0.16);
      ridge(h, W, H, rng, H * 0.96, H * 0.09, 0.02);
      h.fillStyle = vGradient(h, H * 0.86, H, [[0, "#7c7aae"], [1, "#6a689c"]]);
      h.fill();
      sc.hills = hills;
    },
  },

  night: {
    seed: 42,
    build(sc) {
      const { W, H, s, rng } = sc;
      const sky = makeCanvas(W, H);
      const x = sky.getContext("2d");
      x.fillStyle = vGradient(x, 0, H, [[0, "#020717"], [0.4, "#06163a"], [0.75, "#0c2655"], [1, "#1b3c70"]]);
      x.fillRect(0, 0, W, H);
      // milky band
      x.save();
      x.translate(W * 0.45, H * 0.45);
      x.rotate(-0.42);
      x.globalCompositeOperation = "lighter";
      const bandL = Math.hypot(W, H) * 0.6, bandW = H * 0.16;
      for (let i = 0; i < 14; i++) {
        const bx = (rng() - 0.5) * bandL * 1.6, by = (rng() - 0.5) * bandW * 0.6;
        const r = bandW * (0.6 + rng() * 0.9);
        x.save();
        x.translate(bx, by);
        x.scale(2.6, 0.55);
        x.fillStyle = radial(x, 0, 0, r, [[0, rng() < 0.5 ? "rgba(130,160,255,0.07)" : "rgba(190,190,255,0.05)"], [1, "rgba(140,150,255,0)"]]);
        x.fillRect(-r, -r, r * 2, r * 2);
        x.restore();
      }
      for (let i = 0; i < 2600; i++) {
        const bx = (rng() - 0.5) * bandL * 2;
        const g = (rng() + rng() + rng() - 1.5) / 1.5; // ≈ gaussian
        const by = g * bandW * 0.7;
        const a = 0.05 + rng() * 0.3 * (1 - Math.abs(g));
        x.fillStyle = `rgba(225,230,255,${a})`;
        x.fillRect(bx, by, (0.6 + rng() * 0.8) * s, (0.6 + rng() * 0.8) * s);
      }
      x.restore();
      x.globalCompositeOperation = "source-over";
      scatterStars(x, rng, W, H, 420, s, H * 0.85, 1);
      // moon
      const mx = W * 0.78, my = H * 0.2, mr = H * 0.045;
      x.globalCompositeOperation = "lighter";
      x.fillStyle = radial(x, mx, my, H * 0.38, [
        [0, "rgba(210,222,255,0.32)"], [0.12, "rgba(170,190,255,0.14)"], [0.4, "rgba(120,140,230,0.05)"], [1, "rgba(120,140,230,0)"],
      ]);
      x.fillRect(0, 0, W, H);
      x.globalCompositeOperation = "source-over";
      x.fillStyle = radial(x, mx - mr * 0.3, my - mr * 0.3, mr * 1.4, [[0, "#ffffff"], [0.7, "#eef0f4"], [1, "#d5d9e2"]]);
      x.beginPath();
      x.arc(mx, my, mr, 0, TAU);
      x.fill();
      x.fillStyle = "rgba(140,150,175,0.13)";
      for (let i = 0; i < 7; i++) {
        const a = rng() * TAU, d = rng() * mr * 0.65, r = mr * (0.08 + rng() * 0.16);
        x.beginPath();
        x.arc(mx + Math.cos(a) * d, my + Math.sin(a) * d, r, 0, TAU);
        x.fill();
      }
      sc.sky = sky;

      sc.clouds = [];
      for (let i = 0; i < 4; i++) {
        const cw = W * (0.2 + rng() * 0.25), ch = cw * (0.07 + rng() * 0.05);
        const cl = makeCloud(rng, cw, ch, "rgb(40,58,104)", "rgb(70,92,146)", "rgba(150,170,220,0.5)", 4 * s + 1);
        sc.clouds.push({ ...cl, x0: ((i * 0.618 + rng() * 0.1) % 1) * W, y: H * (0.12 + rng() * 0.45), v: (2 + rng() * 4) * s, a: 0.25 + rng() * 0.2 });
      }

      const hills = makeCanvas(W, H);
      const h = hills.getContext("2d");
      h.fillStyle = vGradient(h, H * 0.75, H, [[0, "rgba(60,90,150,0)"], [1, "rgba(60,90,150,0.25)"]]);
      h.fillRect(0, H * 0.75, W, H * 0.25);
      ridge(h, W, H, rng, H * 0.9, H * 0.09, 0);
      h.fillStyle = vGradient(h, H * 0.8, H, [[0, "#0d1a3a"], [1, "#0a1430"]]);
      h.fill();
      ridge(h, W, H, rng, H * 0.97, H * 0.08, 0.03);
      h.fillStyle = "#040917";
      h.fill();
      sc.hills = hills;

      // twinkling stars (drawn per frame)
      sc.twinkleSprite = glowDot(Math.max(8, Math.round(10 * s)), "235,240,255");
      sc.twinkles = [];
      for (let i = 0; i < 70; i++) {
        sc.twinkles.push({
          x: rng() * W, y: Math.pow(rng(), 1.4) * H * 0.75,
          r: (5 + rng() * 7) * s, f: 0.6 + rng() * 2.2, p: rng() * TAU, a: 0.5 + rng() * 0.5,
        });
      }
      sc.shoot = { next: 8 + rng() * 15, t: -1, x: 0, y: 0, dx: 0, dy: 0 };
    },
    frame(sc, x, t, dt) {
      const sh = sc.shoot;
      if (sh.t < 0 && t > sh.next) {
        sh.t = 0;
        sh.x = sc.W * (0.15 + Math.random() * 0.6);
        sh.y = sc.H * (0.05 + Math.random() * 0.3);
        const a = 0.35 + Math.random() * 0.4;
        sh.dx = Math.cos(a) * sc.W * 0.5;
        sh.dy = Math.sin(a) * sc.W * 0.5;
      }
      if (sh.t >= 0) {
        sh.t += dt;
        const life = 0.9, u = sh.t / life;
        if (u >= 1) {
          sh.t = -1;
          sh.next = t + 12 + Math.random() * 25;
        } else {
          const hx = sh.x + sh.dx * u, hy = sh.y + sh.dy * u;
          const tl = 0.18;
          const g = x.createLinearGradient(hx - sh.dx * tl, hy - sh.dy * tl, hx, hy);
          const a = Math.sin(u * Math.PI);
          g.addColorStop(0, "rgba(255,255,255,0)");
          g.addColorStop(1, `rgba(235,242,255,${0.85 * a})`);
          x.strokeStyle = g;
          x.lineWidth = 1.6 * sc.s;
          x.lineCap = "round";
          x.beginPath();
          x.moveTo(hx - sh.dx * tl, hy - sh.dy * tl);
          x.lineTo(hx, hy);
          x.stroke();
        }
      }
    },
  },
};

// ───────────────────────────── runtime ─────────────────────────────

class Scene {
  constructor(name, canvas) {
    this.name = DEFS[name] ? name : "twilight";
    this.def = DEFS[this.name];
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d", { alpha: false });
    this.t = 0;
    this.running = false;
    this._raf = 0;
    this._last = 0;
    this._prev = 0;
    this._key = "";
    this._loop = this._loop.bind(this);
    this.resize();
  }

  resize() {
    const cw = this.canvas.clientWidth || window.innerWidth;
    const ch = this.canvas.clientHeight || window.innerHeight;
    const dpr = window.devicePixelRatio || 1;
    const s = Math.min(dpr, MAX_LONG_SIDE / Math.max(cw, ch));
    const W = Math.max(1, Math.round(cw * s)), H = Math.max(1, Math.round(ch * s));
    const key = `${W}x${H}`;
    if (key !== this._key) {
      this._key = key;
      this.canvas.width = W;
      this.canvas.height = H;
      this.W = W;
      this.H = H;
      this.s = s;
      this.rng = mulberry32(this.def.seed);
      this.twinkles = null;
      this.def.build(this);
    }
    this.render(0);
  }

  render(dt) {
    const x = this.ctx, W = this.W, H = this.H, t = this.t;
    x.globalAlpha = 1;
    x.drawImage(this.sky, 0, 0);
    if (this.twinkles) {
      const spr = this.twinkleSprite;
      x.globalCompositeOperation = "lighter";
      for (const st of this.twinkles) {
        const v = 0.5 + 0.5 * Math.sin(t * st.f + st.p);
        x.globalAlpha = st.a * v * v;
        x.drawImage(spr, st.x - st.r / 2, st.y - st.r / 2, st.r, st.r);
      }
      x.globalCompositeOperation = "source-over";
    }
    if (this.def.frame) {
      x.globalAlpha = 1;
      this.def.frame(this, x, t, dt);
    }
    for (const c of this.clouds) {
      const span = W + c.c.width;
      let cx = (c.x0 + t * c.v) % span;
      cx -= c.c.width;
      x.globalAlpha = c.a;
      x.drawImage(c.c, cx, c.y);
    }
    x.globalAlpha = 1;
    x.drawImage(this.hills, 0, 0);
  }

  start() {
    if (this.running) return;
    this.running = true;
    this._last = 0;
    this._prev = 0;
    this._raf = requestAnimationFrame(this._loop);
  }

  stop() {
    this.running = false;
    if (this._raf) cancelAnimationFrame(this._raf);
    this._raf = 0;
  }

  destroy() {
    this.stop();
    this.sky = this.hills = null;
    this.clouds = [];
    this.twinkles = null;
  }

  _loop(ts) {
    this._raf = 0;
    if (!this.running) return;
    this._raf = requestAnimationFrame(this._loop);
    if (this._last && ts - this._last < 1000 / SCENE_FPS - 2) return;
    this._last = ts;
    const dt = this._prev ? Math.min(0.1, (ts - this._prev) / 1000) : 0;
    this._prev = ts;
    this.t += dt;
    this.render(dt);
  }
}

export function createScene(name, canvas) {
  return new Scene(name, canvas);
}
