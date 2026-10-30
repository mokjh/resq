// RESQ valleys, drawn from the game's own files (tools/web/export_valleys.py, lab webflood).
// Static mode draws a mission's map as a small isometric model that assembles as it enters view. Flood mode replays
// the deterministic simulation: the water frames are exactly what RESQ's simulator produced for each barrier choice.
// The hero is a scroll-told mission: scrolling advances the real simulation clock. Reduced motion shows final states.
(function () {
  "use strict";
  const REDUCED = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const PALETTE = {
    W1: { ground: "#86A95E", forest: "#5C8A48", rock: "#8E8A80" },
    W2: { ground: "#A9A35A", forest: "#4E7A3C", rock: "#8A7B6A" },
    W3: { ground: "#EEF2F4", forest: "#5E7D6A", rock: "#DCE4E9" },
    W4: { ground: "#D9C9A0", forest: "#6C9150", rock: "#9AA3A6" },
    W5: { ground: "#6B4A3A", forest: "#6C7F45", rock: "#4A3C36" },
  };
  const COMMON = { water: "#3E8FB8", road: "#CBB892", building: "#E9E1D3", roof: "#B85C3E", landing: "#F2BE22", lava: "#FF6A2B" };
  const KEPT = "rgba(70, 190, 110, 0.85)", NEWLY = "rgba(232, 72, 44, 0.85)";
  const ease = x => 1 - Math.pow(1 - Math.min(1, Math.max(0, x)), 3);

  function rgbOf(c) {
    if (c[0] === "#") { const n = parseInt(c.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
    return c.match(/\d+/g).slice(0, 3).map(Number);
  }
  function shade(color, k) {
    const [r, g, b] = rgbOf(color);
    return `rgb(${Math.min(255, Math.round(r * k))},${Math.min(255, Math.round(g * k))},${Math.min(255, Math.round(b * k))})`;
  }

  // isometric layout for a w×h grid fitted into a canvas
  function layout(canvas, w, h, lift) {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const cssW = canvas.clientWidth, cssH = canvas.clientHeight;
    if (canvas.width !== Math.round(cssW * dpr) || canvas.height !== Math.round(cssH * dpr)) { canvas.width = Math.round(cssW * dpr); canvas.height = Math.round(cssH * dpr); }
    const a = Math.min(cssW / (w + h), cssH / ((w + h) * 0.5 + lift * 1.15));
    const b = a * 0.5, z = a * lift / 1200;
    const ox = h * a + (cssW - (w + h) * a) / 2, oy = lift * a * 1.05 + (cssH - ((w + h) * b + lift * a * 1.05)) / 2;
    return { dpr, a, b, z, ox, oy, w, h };
  }
  const proj = (L, gx, gy, mm) => [L.ox + (gx - gy) * L.a, L.oy + (gx + gy) * L.b - mm * L.z];

  function quad(ctx, pts, fill) {
    ctx.beginPath(); ctx.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
    ctx.closePath(); ctx.fillStyle = fill; ctx.fill();
  }
  function column(ctx, L, gx, gy, top, base, color) {
    const t = [proj(L, gx, gy, top), proj(L, gx + 1, gy, top), proj(L, gx + 1, gy + 1, top), proj(L, gx, gy + 1, top)];
    const br = proj(L, gx + 1, gy + 1, base), bl = proj(L, gx, gy + 1, base), rr = proj(L, gx + 1, gy, base);
    quad(ctx, [t[3], t[2], br, bl], shade(color, 0.72));
    quad(ctx, [t[2], t[1], rr, br], shade(color, 0.86));
    quad(ctx, t, color);
  }
  function tree(ctx, L, x, y, top, world, h, s) {
    const p = PALETTE[world] || PALETTE.W1, snow = world === "W3", tall = (520 + (h % 5) * 90) * s;
    const [bx, by] = proj(L, x, y, top), [tx, ty] = proj(L, x, y, top + tall), r = L.a * 0.34 * s;
    const col = snow ? "#3F5E4E" : p.forest;
    ctx.beginPath(); ctx.moveTo(bx - r, by); ctx.lineTo(tx, ty); ctx.lineTo(bx, by + r * 0.35); ctx.closePath(); ctx.fillStyle = shade(col, 0.9); ctx.fill();
    ctx.beginPath(); ctx.moveTo(bx + r, by); ctx.lineTo(tx, ty); ctx.lineTo(bx, by + r * 0.35); ctx.closePath(); ctx.fillStyle = shade(col, 0.68); ctx.fill();
    if (snow) { ctx.beginPath(); ctx.moveTo(tx - r * 0.35, ty + r * 0.9); ctx.lineTo(tx, ty); ctx.lineTo(tx + r * 0.35, ty + r * 0.9); ctx.closePath(); ctx.fillStyle = "#F4F7F8"; ctx.fill(); }
  }
  function cellColor(kind, world) {
    const p = PALETTE[world] || PALETTE.W1;
    return { water: COMMON.water, road: COMMON.road, building: COMMON.building, landing: COMMON.landing, lava: COMMON.lava,
             forest: p.forest, rock: p.rock }[kind] || p.ground;
  }

  // draw a valley. opt: kinds, elevation, water(c) mm, tint(c), grow(c) 0..1 (assembly), marks [{cell,label,on}]
  function draw(canvas, map, opt) {
    const L = layout(canvas, map.w, map.h, opt.lift || 2.4);
    const ctx = canvas.getContext("2d");
    ctx.setTransform(L.dpr, 0, 0, L.dpr, 0, 0);
    ctx.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
    const kinds = opt.kinds, elev = opt.elevation || map.elev, world = map.world || "W1";
    const W = map.w, H = map.h, base = -260, grow = opt.grow;
    const frame = grow ? ease(opt.frameGrow == null ? 1 : opt.frameGrow) : 1;
    if (frame > 0) {
      const fb = base - 220 * frame;
      quad(ctx, [proj(L, 0, H, base), proj(L, W, H, base), proj(L, W, H, fb), proj(L, 0, H, fb)], "#7A4A2A");
      quad(ctx, [proj(L, W, 0, base), proj(L, W, H, base), proj(L, W, H, fb), proj(L, W, 0, fb)], "#8E5A33");
    }
    for (let s = 0; s < W + H - 1; s++) {
      for (let gy = Math.max(0, s - W + 1); gy <= Math.min(H - 1, s); gy++) {
        const gx = s - gy, c = gy * W + gx;
        const g = grow ? grow(c) : 1;
        if (g <= 0) continue;
        const kind = kinds[map.kind ? map.kind[c] : 0] || "ground";
        const top = base + (elev[c] - base) * ease(g);
        const color = cellColor(kind, world);
        column(ctx, L, gx, gy, top, base, kind === "water" ? shade(color, 0.55) : color);
        if (kind === "building" && g > 0.6) column(ctx, L, gx + 0.18, gy + 0.18, top + 420 * ease((g - 0.6) / 0.4), top, COMMON.roof);
        const hsh = ((c * 2654435761) >>> 0) % 100;
        if (g > 0.75 && ((kind === "forest" && hsh < 70) || (kind === "ground" && hsh < 9 && world !== "W5")))
          tree(ctx, L, gx + 0.5, gy + 0.5, top, world, hsh, ease((g - 0.75) / 0.25));
        const depth = opt.water ? opt.water(c) : (map.init ? map.init[c] * (g >= 1 ? 1 : 0) : 0);
        if (depth > 20) {
          const surf = top + depth, k = Math.min(1, depth / 1600);
          quad(ctx, [proj(L, gx, gy, surf), proj(L, gx + 1, gy, surf), proj(L, gx + 1, gy + 1, surf), proj(L, gx, gy + 1, surf)],
               `rgba(${Math.round(70 - 30 * k)},${Math.round(150 - 40 * k)},${Math.round(205 - 25 * k)},${0.62 + 0.3 * k})`);
        }
        const tint = opt.tint ? opt.tint(c) : null;
        if (tint) {
          const surf = top + Math.max(depth, 0) + 6;
          quad(ctx, [proj(L, gx, gy, surf), proj(L, gx + 1, gy, surf), proj(L, gx + 1, gy + 1, surf), proj(L, gx, gy + 1, surf)], tint);
        }
      }
    }
    // barrier spots: numbered markers floating over their cells
    for (const m of opt.marks || []) {
      const gx = m.cell % W, gy = Math.floor(m.cell / W);
      const [x, y] = proj(L, gx + 0.5, gy + 0.5, elev[m.cell] + 700);
      const r = Math.max(9, L.a * 0.62);
      ctx.beginPath(); ctx.arc(x, y, r, 0, Math.PI * 2);
      ctx.fillStyle = m.on ? "#FF6B35" : "rgba(255,255,255,0.92)"; ctx.fill();
      ctx.lineWidth = 2; ctx.strokeStyle = "#132B2A"; ctx.stroke();
      ctx.beginPath(); ctx.moveTo(x, y + r); ctx.lineTo(x, y + r + L.a * 0.6); ctx.stroke();
      ctx.fillStyle = "#132B2A"; ctx.font = `700 ${Math.round(r * 1.1)}px "Atkinson Hyperlegible", Arial, sans-serif`;
      ctx.textAlign = "center"; ctx.textBaseline = "middle"; ctx.fillText(m.label, x, y + 1);
    }
    return L;
  }

  // assembly order: from the far corner forwards, with a little jitter, so the valley builds itself
  function assembler(map) {
    const W = map.w, H = map.h, delay = new Float32Array(W * H);
    for (let c = 0; c < W * H; c++) {
      const gx = c % W, gy = Math.floor(c / W), jit = (((c * 2654435761) >>> 0) % 100) / 100;
      delay[c] = ((gx + gy) / (W + H)) * 0.62 + jit * 0.12;
    }
    return { at: p => c => (p - delay[c]) / 0.26 };
  }
  function animate(ms, step, done) {
    if (REDUCED) { step(1); if (done) done(); return; }
    const t0 = performance.now();
    (function f(now) { const p = Math.min(1, (now - t0) / ms); step(p); if (p < 1) requestAnimationFrame(f); else if (done) done(); })(t0);
  }

  // ---------- static valleys that build themselves when they come into view ----------
  function revealStatic(canvas, data, id, lift) {
    const m = data.missions[id], A = assembler(m);
    const delayMs = +(canvas.dataset.delay || 0);
    const go = () => animate(1500, p => draw(canvas, m, { kinds: data.kinds, lift: lift || 2.4, grow: A.at(p * 1.05), frameGrow: p * 3 }));
    if (REDUCED || !delayMs) go(); else setTimeout(go, delayMs);
    canvas._redraw = () => draw(canvas, m, { kinds: data.kinds, lift: lift || 2.4 });
  }

  // ---------- water frames ----------
  function decode(flood) {
    const frames = {};
    for (const key of Object.keys(flood.scenarios)) {
      const bin = atob(flood.scenarios[key].depth), arr = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
      frames[key] = arr;
    }
    const n = flood.w * flood.h, total = flood.scenarios.none.frames;
    const depth = (key, f, c) => frames[key][Math.max(0, Math.min(total - 1, f)) * n + c] * 10;
    return { n, total, depth, secPerFrame: flood.every / flood.ticksPerSecond };
  }
  const fmt = s => `${Math.floor(s / 60)}:${String(Math.round(s) % 60).padStart(2, "0")}`;
  function placeStatus(flood, F, scenario, f, named) {
    return Object.entries(named).map(([id, label]) => {
      const node = flood.nodes.find(nd => nd.id === id);
      if (!node) return "";
      const wet = F.depth(scenario, f, node.cell) > 20;
      let since = "";
      if (wet) { let k = f; while (k > 0 && F.depth(scenario, k - 1, node.cell) > 20) k--; since = `<i class="since"> since</i> ${fmt(k * F.secPerFrame)}`; }
      return `<li class="${wet ? "wet" : "dry"}"><span>${label}</span><b>${wet ? `<i class="word">Flooded</i>${since}` : "Dry"}</b></li>`;
    }).join("");
  }
  const marksFor = (flood, chosen) => flood.sockets.map((s, i) => ({ cell: s.cell, label: String(i + 1), on: s.id === chosen }));
  function waterFn(F, scenario, t) {
    const f0 = Math.floor(t), f1 = Math.min(F.total - 1, f0 + 1), k = t - f0;
    return c => F.depth(scenario, f0, c) * (1 - k) + F.depth(scenario, f1, c) * k;
  }
  function tintFn(F, scenario, t) {
    if (scenario === "none") return null;
    const f = Math.round(t);
    return c => { const mine = F.depth(scenario, f, c) > 20, base = F.depth("none", f, c) > 20; return base && !mine ? KEPT : (!base && mine ? NEWLY : null); };
  }

  // ---------- the hero: a scroll-told mission ----------
  function story(root, flood, valleys) {
    const canvas = root.querySelector("canvas"), F = decode(flood);
    const map = Object.assign({}, valleys.missions[flood.mission]);
    const steps = [...root.querySelectorAll("[data-step]")];
    const clock = root.querySelector("[data-clock]"), status = root.querySelector("[data-status]");
    const named = JSON.parse(root.dataset.places || "{}");
    const A = assembler(map);
    let built = REDUCED ? 1 : 0, progress = 0, frame = 0;

    function render() {
      // scroll progress → beats: arrive · read what's coming · place the barrier · watch it play out
      const p = progress;
      const beat = p < 0.12 ? 0 : p < 0.4 ? 1 : p < 0.62 ? 2 : 3;
      steps.forEach((s, i) => s.classList.toggle("on", i === beat));
      root.dataset.beat = String(beat);
      let scenario = "none", t = 0;
      if (beat === 1) t = ease((p - 0.12) / 0.28) * 15;
      if (beat >= 2) { scenario = "barrier:03"; t = 15; }
      if (beat === 3) t = 15 + ease((p - 0.62) / 0.34) * (F.total - 16);
      draw(canvas, map, {
        kinds: valleys.kinds, elevation: flood.elevation, lift: 2.4,
        grow: built < 1 ? A.at(built * 1.05) : null, frameGrow: built * 3,
        water: built >= 1 ? waterFn(F, scenario, t) : null, tint: beat === 3 ? tintFn(F, scenario, t) : null,
        marks: beat >= 2 && built >= 1 ? marksFor(flood, "barrier:03") : [],
      });
      clock.textContent = fmt(t * F.secPerFrame);
      status.innerHTML = placeStatus(flood, F, scenario, Math.round(t), named);
    }
    function onScroll() {
      const r = root.getBoundingClientRect(), span = r.height - innerHeight;
      progress = span > 0 ? Math.min(1, Math.max(0, -r.top / span)) : 0;
      if (!frame) frame = requestAnimationFrame(() => { frame = 0; render(); });
    }
    animate(1800, p => { built = p; render(); }, () => { built = 1; render(); });
    addEventListener("scroll", onScroll, { passive: true });
    addEventListener("resize", onScroll);
    onScroll();
  }

  // ---------- the interactive player ----------
  function floodPlayer(root, flood, valleys) {
    const canvas = root.querySelector("canvas"), F = decode(flood);
    const map = Object.assign({}, valleys.missions[flood.mission]);
    let scenario = root.dataset.start || "none", t = 0, playing = false, last = 0;
    const named = JSON.parse(root.dataset.places || "{}");
    const status = root.querySelector("[data-status]"), clock = root.querySelector("[data-clock]");
    const scrub = root.querySelector("input[type=range]"), playBtn = root.querySelector("[data-play]");
    scrub.max = String(F.total - 1);
    function render() {
      draw(canvas, map, { kinds: valleys.kinds, elevation: flood.elevation, water: waterFn(F, scenario, t), tint: tintFn(F, scenario, t),
                          lift: 2.4, marks: marksFor(flood, scenario) });
      clock.textContent = fmt(t * F.secPerFrame);
      scrub.value = String(Math.round(t));
      status.innerHTML = placeStatus(flood, F, scenario, Math.round(t), named);
    }
    function tick(now) {
      if (playing) {
        if (last) t += (now - last) / 1000 / 0.2;
        if (t >= F.total - 1) { t = F.total - 1; playing = false; playBtn.textContent = "Replay"; }
      }
      last = now; render();
      if (playing) requestAnimationFrame(tick); else last = 0;
    }
    function play() { if (t >= F.total - 1) t = 0; if (REDUCED) { t = F.total - 1; render(); return; } playing = true; playBtn.textContent = "Pause"; requestAnimationFrame(tick); }
    playBtn.addEventListener("click", () => { if (playing) { playing = false; playBtn.textContent = "Play"; } else play(); });
    scrub.addEventListener("input", () => { playing = false; playBtn.textContent = "Play"; t = +scrub.value; render(); });
    root.querySelectorAll("[data-scenario]").forEach(btn => btn.addEventListener("click", () => {
      scenario = btn.dataset.scenario;
      root.querySelectorAll("[data-scenario]").forEach(b => b.setAttribute("aria-pressed", String(b === btn)));
      t = 0; play();
    }));
    addEventListener("resize", render);
    render();
  }

  // ---------- reveal on scroll for anything marked .reveal ----------
  function reveals() {
    const els = document.querySelectorAll(".reveal");
    if (REDUCED) { els.forEach(e => e.classList.add("in")); return; }
    const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } }), { rootMargin: "0px 0px -12% 0px" });
    els.forEach(e => io.observe(e));
  }

  async function boot() {
    reveals();
    if (!document.querySelector("[data-valley], [data-flood], [data-story]")) return;
    const valleys = await (await fetch("data/valleys.json")).json();
    const statics = [...document.querySelectorAll("canvas[data-valley]")];
    const io = new IntersectionObserver(es => es.forEach(e => {
      if (!e.isIntersecting) return;
      revealStatic(e.target, valleys, e.target.dataset.valley, +(e.target.dataset.lift || 0) || null);
      io.unobserve(e.target);
    }), { rootMargin: "0px 0px -8% 0px" });
    statics.forEach(c => io.observe(c));
    const src = document.querySelector("[data-story]") || document.querySelector("[data-flood]");
    const flood = src ? await (await fetch(src.dataset.story || src.dataset.flood)).json() : null;
    const st = document.querySelector("[data-story]");
    if (st && flood) story(st, flood, valleys);
    const fl = document.querySelector("[data-flood]");
    if (fl && flood) floodPlayer(fl, flood, valleys);
    let rt; addEventListener("resize", () => { clearTimeout(rt); rt = setTimeout(() => statics.forEach(c => c._redraw && c._redraw()), 150); });
  }
  document.documentElement.classList.add("js");
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot); else boot();
})();
