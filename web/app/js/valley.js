// RESQ valleys, drawn from the game's own files (tools/web/export_valleys.py, lab webflood).
// Static mode draws a mission's map as a small isometric model. Flood mode replays the deterministic simulation:
// the water frames are exactly what RESQ's simulator produced for each barrier choice; nothing here is animated by hand.
(function () {
  "use strict";
  const PALETTE = {
    W1: { ground: "#86A95E", forest: "#5C8A48", rock: "#8E8A80" },
    W2: { ground: "#A9A35A", forest: "#4E7A3C", rock: "#8A7B6A" },
    W3: { ground: "#EEF2F4", forest: "#5E7D6A", rock: "#DCE4E9" },
    W4: { ground: "#D9C9A0", forest: "#6C9150", rock: "#9AA3A6" },
    W5: { ground: "#6B4A3A", forest: "#6C7F45", rock: "#4A3C36" },
  };
  const COMMON = { water: "#3E8FB8", road: "#CBB892", building: "#E9E1D3", roof: "#B85C3E", landing: "#F2BE22", lava: "#FF6A2B" };
  const KEPT = "rgba(70, 190, 110, 0.85)", NEWLY = "rgba(232, 72, 44, 0.85)";

  function rgbOf(c) {
    if (c[0] === "#") { const n = parseInt(c.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
    return c.match(/\d+/g).slice(0, 3).map(Number);
  }
  function shade(color, k) {
    const [r0, g0, b0] = rgbOf(color);
    const r = Math.round(r0 * k), g = Math.round(g0 * k), b = Math.round(b0 * k);
    return `rgb(${Math.min(255, r)},${Math.min(255, g)},${Math.min(255, b)})`;
  }

  // isometric layout for a w×h grid fitted into a canvas
  function layout(canvas, w, h, lift) {
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const cssW = canvas.clientWidth, cssH = canvas.clientHeight;
    canvas.width = Math.round(cssW * dpr); canvas.height = Math.round(cssH * dpr);
    const a = Math.min(cssW / (w + h), cssH / ((w + h) * 0.5 + lift * 1.15));   // half tile width
    const b = a * 0.5, z = a * lift / 1200;                                        // px per mm of elevation
    const ox = h * a + (cssW - (w + h) * a) / 2, oy = lift * a * 1.05 + (cssH - ((w + h) * b + lift * a * 1.05)) / 2;
    return { dpr, a, b, z, ox, oy, w, h };
  }

  function proj(L, gx, gy, mm) { return [L.ox + (gx - gy) * L.a, L.oy + (gx + gy) * L.b - mm * L.z]; }

  function quad(ctx, pts, fill) {
    ctx.beginPath(); ctx.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
    ctx.closePath(); ctx.fillStyle = fill; ctx.fill();
  }

  // one map cell as a column: top face, plus the two visible side faces where the neighbour is lower
  function column(ctx, L, gx, gy, top, base, color, side) {
    const t = [proj(L, gx, gy, top), proj(L, gx + 1, gy, top), proj(L, gx + 1, gy + 1, top), proj(L, gx, gy + 1, top)];
    if (side) {
      const br = proj(L, gx + 1, gy + 1, base), bl = proj(L, gx, gy + 1, base), rr = proj(L, gx + 1, gy, base);
      quad(ctx, [t[3], t[2], br, bl], shade(color, 0.72));    // front-left face
      quad(ctx, [t[2], t[1], rr, br], shade(color, 0.86));    // front-right face
    }
    quad(ctx, t, color);
  }

  function tree(ctx, L, x, y, top, world, h) {
    const p = PALETTE[world] || PALETTE.W1;
    const snow = world === "W3", tall = 520 + (h % 5) * 90;
    const [bx, by] = proj(L, x, y, top), [tx, ty] = proj(L, x, y, top + tall);
    const r = L.a * 0.34;
    ctx.beginPath(); ctx.moveTo(bx - r, by); ctx.lineTo(tx, ty); ctx.lineTo(bx, by + r * 0.35); ctx.closePath();
    ctx.fillStyle = shade(snow ? "#3F5E4E" : p.forest, 0.9); ctx.fill();
    ctx.beginPath(); ctx.moveTo(bx + r, by); ctx.lineTo(tx, ty); ctx.lineTo(bx, by + r * 0.35); ctx.closePath();
    ctx.fillStyle = shade(snow ? "#3F5E4E" : p.forest, 0.68); ctx.fill();
    if (snow) { ctx.beginPath(); ctx.moveTo(tx - r * 0.35, ty + r * 0.9); ctx.lineTo(tx, ty); ctx.lineTo(tx + r * 0.35, ty + r * 0.9); ctx.closePath(); ctx.fillStyle = "#F4F7F8"; ctx.fill(); }
  }

  function cellColor(kind, world) {
    const p = PALETTE[world] || PALETTE.W1;
    switch (kind) {
      case "water": return COMMON.water;
      case "road": return COMMON.road;
      case "building": return COMMON.building;
      case "landing": return COMMON.landing;
      case "lava": return COMMON.lava;
      case "forest": return p.forest;
      case "rock": return p.rock;
      default: return p.ground;
    }
  }

  // draw a valley; water(c) returns the water depth (mm) on cell c, tint(c) an optional overlay colour
  function draw(canvas, map, opt) {
    const L = layout(canvas, map.w, map.h, opt.lift || 1.6);
    const ctx = canvas.getContext("2d");
    ctx.setTransform(L.dpr, 0, 0, L.dpr, 0, 0);
    ctx.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
    const kinds = opt.kinds, elev = opt.elevation || map.elev, base = -260;
    // base slab under the whole board, like the game's wooden frame
    const W = map.w, H = map.h;
    quad(ctx, [proj(L, 0, H, base), proj(L, W, H, base), proj(L, W, H, base - 220), proj(L, 0, H, base - 220)], "#7A4A2A");
    quad(ctx, [proj(L, W, 0, base), proj(L, W, H, base), proj(L, W, H, base - 220), proj(L, W, 0, base - 220)], "#8E5A33");
    for (let s = 0; s < W + H - 1; s++) {
      for (let gy = Math.max(0, s - W + 1); gy <= Math.min(H - 1, s); gy++) {
        const gx = s - gy, c = gy * W + gx;
        const kind = kinds[map.kind ? map.kind[c] : 0] || "ground";
        let top = elev[c];
        const isBuilding = kind === "building";
        const color = cellColor(kind === "water" ? "water" : kind, map.world || "W1");
        const bed = kind === "water" ? shade(color, 0.55) : color;
        column(ctx, L, gx, gy, top, base, bed, true);
        if (isBuilding) column(ctx, L, gx + 0.18, gy + 0.18, top + 420, top, COMMON.roof, true);
        const hsh = ((c * 2654435761) >>> 0) % 100;
        if ((kind === "forest" && hsh < 70) || (kind === "ground" && hsh < 9 && (map.world || "W1") !== "W5")) tree(ctx, L, gx + 0.5, gy + 0.5, top, map.world || "W1", hsh);
        const depth = opt.water ? opt.water(c) : (map.init ? map.init[c] : 0);
        if (depth > 20) {
          const surf = top + depth;
          const k = Math.min(1, depth / 1600);
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
    return L;
  }

  // ---------- static valleys (the campaign table) ----------
  function drawStatic(canvas, data, id) {
    const m = data.missions[id];
    draw(canvas, m, { kinds: data.kinds, lift: 2.4 });
  }

  // ---------- the flood player ----------
  function floodPlayer(root, flood, valleys) {
    const canvas = root.querySelector("canvas");
    const map = Object.assign({}, valleys.missions[flood.mission]);
    const n = flood.w * flood.h;
    const frames = {};
    for (const key of Object.keys(flood.scenarios)) {
      const bin = atob(flood.scenarios[key].depth);
      const arr = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
      frames[key] = arr;
    }
    const total = flood.scenarios.none.frames;
    const secPerFrame = flood.every / flood.ticksPerSecond;
    let scenario = root.dataset.start || "none", t = 0, playing = !matchMedia("(prefers-reduced-motion: reduce)").matches, last = 0;
    const named = JSON.parse(root.dataset.places || "{}");
    const status = root.querySelector("[data-status]");
    const clock = root.querySelector("[data-clock]");
    const scrub = root.querySelector("input[type=range]");
    const playBtn = root.querySelector("[data-play]");
    scrub.max = String(total - 1);

    const depth = (key, f, c) => frames[key][Math.min(total - 1, f) * n + c] * 10;
    function waterAt(c) {
      const f0 = Math.floor(t), f1 = Math.min(total - 1, f0 + 1), k = t - f0;
      return depth(scenario, f0, c) * (1 - k) + depth(scenario, f1, c) * k;
    }
    function tintAt(c) {
      if (scenario === "none") return null;
      const f = Math.round(t), mine = depth(scenario, f, c) > 20, base = depth("none", f, c) > 20;
      if (base && !mine) return KEPT;
      if (!base && mine) return NEWLY;
      return null;
    }
    function render() {
      draw(canvas, map, { kinds: valleys.kinds, elevation: flood.elevation, water: waterAt, tint: tintAt, lift: 2.4 });
      const sec = Math.round(t * secPerFrame);
      clock.textContent = `${Math.floor(sec / 60)}:${String(sec % 60).padStart(2, "0")}`;
      scrub.value = String(Math.round(t));
      // live status of the places that matter, straight from the frames
      const f = Math.round(t);
      status.innerHTML = Object.entries(named).map(([id, label]) => {
        const node = flood.nodes.find(nd => nd.id === id);
        if (!node) return "";
        const wet = depth(scenario, f, node.cell) > 20;
        let since = "";
        if (wet) { let k = f; while (k > 0 && depth(scenario, k - 1, node.cell) > 20) k--; const s = Math.round(k * secPerFrame); since = ` since ${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`; }
        return `<li class="${wet ? "wet" : "dry"}"><span>${label}</span><b>${wet ? "Flooded" + since : "Dry"}</b></li>`;
      }).join("");
    }
    function tick(now) {
      if (playing) {
        if (last) t += (now - last) / 1000 / 0.2;      // 4 minutes of simulation in about 12 seconds
        if (t >= total - 1) { t = total - 1; playing = false; playBtn.textContent = "Replay"; }
      }
      last = now;
      render();
      if (playing) requestAnimationFrame(tick); else last = 0;
    }
    function play() { if (t >= total - 1) t = 0; playing = true; playBtn.textContent = "Pause"; requestAnimationFrame(tick); }
    playBtn.addEventListener("click", () => { if (playing) { playing = false; playBtn.textContent = "Play"; } else play(); });
    scrub.addEventListener("input", () => { playing = false; playBtn.textContent = "Play"; t = +scrub.value; render(); });
    root.querySelectorAll("[data-scenario]").forEach(btn => btn.addEventListener("click", () => {
      scenario = btn.dataset.scenario;
      root.querySelectorAll("[data-scenario]").forEach(b => b.setAttribute("aria-pressed", String(b === btn)));
      t = 0; play();
    }));
    window.addEventListener("resize", render);
    render();
    if (playing) { playBtn.textContent = "Pause"; requestAnimationFrame(tick); } else playBtn.textContent = "Play";
  }

  async function boot() {
    const need = document.querySelector("[data-valley], [data-flood]");
    if (!need) return;
    const valleys = await (await fetch("data/valleys.json")).json();
    document.querySelectorAll("canvas[data-valley]").forEach(c => {
      const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { drawStatic(c, valleys, c.dataset.valley); io.disconnect(); } }));
      io.observe(c);
    });
    const fl = document.querySelector("[data-flood]");
    if (fl) floodPlayer(fl, await (await fetch(fl.dataset.flood)).json(), valleys);
    let rt; window.addEventListener("resize", () => { clearTimeout(rt); rt = setTimeout(() => document.querySelectorAll("canvas[data-valley]").forEach(c => drawStatic(c, valleys, c.dataset.valley)), 150); });
  }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot); else boot();
})();
