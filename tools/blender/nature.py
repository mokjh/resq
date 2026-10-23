"""RESQ nature & dressing kits (trees, shrubs, rocks, ground cover, props) and
backdrop ring pieces for all five worlds. Original procedural geometry only.

Run (one Blender process at a time):
  blender -b --factory-startup --python-exit-code 1 --python tools/blender/nature.py -- [options]
Options:
  --world w1_alder,w2_ember,...   worlds to build (default: all five)
  --only id1,id2                  build only these asset ids (manifest is merged)
  --no-bake                       keep the geometric ray-cast AO (skip Cycles AO bake)
  --no-preview                    skip the world preview sheets
  --list                          print the asset table and exit

Outputs per world under art/nature/<world>/: <id>.fbx (root empty <id> with
<id>_LOD0/1/2 children, optional <id>_mk_* empties), <id>.json sidecar,
<world>_preview.png (LOD0 contact strip), <world>_lods.png (LOD0/1/2 per asset),
<world>_kit.blend (editable source). art/nature/manifest.json lists every asset.

Sizes follow the coordinator's board-readability targets (23 Oct 2026), in
board millimetres (residents 34 mm, cells 20 mm); backdrop pieces are
320 x 120 mm, <= 160 mm tall, and tile end to end along X.
Conventions: metres in files, Z up, forward -Y, pivot at ground centre, ground
contact sunk 0.5-0.6 mm, one shared material, vertex colour RGB = palette role
x tone (sRGB), A = baked AO, UV = palette-atlas cell. No colliders.
"""

import json
import math
import os
import random
import sys
import tempfile
import time
import zlib

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kit  # noqa: E402
from kit import MM, V  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))
ART = os.path.join(REPO, "art")
NAT = os.path.join(ART, "nature")
SHARED = os.path.join(ART, "shared")
ATLAS = os.path.join(SHARED, "palette_atlas.png")

WORLDS = {
    "w1_alder": "W1 Alder Valley (spring river valley)",
    "w2_ember": "W2 Emberwood (late-summer pine forest)",
    "w3_frost": "W3 Frostline (alpine snow)",
    "w4_tide": "W4 Tidemark (temperate coast)",
    "w5_cinder": "W5 Cinder Reach (volcanic island)",
}
BUDGET = {"tree": 1000, "shrub": 250, "rock": 400, "ground_cover": 80, "prop": 400, "backdrop": 3000}
AO_GEO = {  # geometric fallback AO (also the value used when --no-bake)
    "tree": dict(rays=16, dist_mm=5.0, strength=0.85, height_mm=20.0, height_dark=0.12),
    "shrub": dict(rays=16, dist_mm=3.0, strength=0.85, height_mm=6.0, height_dark=0.12),
    "rock": dict(rays=16, dist_mm=3.0, strength=0.8, height_mm=4.0, height_dark=0.1),
    "ground_cover": dict(rays=12, dist_mm=1.6, strength=0.8, height_mm=3.0, height_dark=0.18),
    "prop": dict(rays=16, dist_mm=2.5, strength=0.85, height_mm=5.0, height_dark=0.08),
    "backdrop": dict(rays=12, dist_mm=14.0, strength=0.75, use_ground=False),
}
BAKE_DIST = {"tree": 5.0, "shrub": 3.0, "rock": 3.0, "ground_cover": 1.6, "prop": 2.5, "backdrop": 14.0}
LOD_START = {1: 0.95, 2: 0.6}   # starting density for LOD1/LOD2, lowered until the ratio holds

TAU = 2 * math.pi


# ==========================================================================
# Noise, tone and role helpers (deterministic)
# ==========================================================================
def hsh(*a):
    s = 0.0
    for i, x in enumerate(a):
        s += x * (12.9898, 78.233, 37.719, 4.581, 9.151)[i % 5]
    v = math.sin(s) * 43758.5453
    return v - math.floor(v)


def sstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def vnoise(x, y, seed):
    ix, iy = math.floor(x), math.floor(y)
    u, v = sstep(x - ix), sstep(y - iy)
    a, b = hsh(ix, iy, seed), hsh(ix + 1, iy, seed)
    c, d = hsh(ix, iy + 1, seed), hsh(ix + 1, iy + 1, seed)
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v


def fbm(x, y, seed, octaves=3):
    s, amp, norm = 0.0, 1.0, 0.0
    for i in range(octaves):
        s += amp * vnoise(x * (2 ** i), y * (2 ** i), seed + 13 * i)
        norm += amp
        amp *= 0.5
    return s / norm


def tn(base, var=0.07, top=0.16, seed=0.0):
    """Per-face tone: base x top-light gradient x hash jitter."""
    def f(c, n):
        return base * (1 - top * 0.5 + top * (0.5 + 0.5 * n.z)) * \
            (1 + var * (2 * hsh(c.x * 1.7 + seed, c.y * 1.3, c.z * 1.1) - 1))
    return f


def jit(c, seed=0.0, amt=0.08):
    """Small per-face tone jitter; the main variation is per-vertex and noise-coherent."""
    return 1 + 0.4 * amt * (2 * hsh(c.x * 2.1 + seed, c.y * 1.7, c.z * 1.3 + seed) - 1)


def pn(c, f=0.5, seed=0):
    """Spatially coherent 0..1 noise at a face centre (mm), frequency f per mm."""
    return fbm(c.x * f + c.z * f * 0.61 + seed * 3.7, c.y * f - c.z * f * 0.43, int(seed * 13) % 251)


def n_of(n, q, lo=1):
    return max(lo, min(n, int(round(n * q))))


# ==========================================================================
# Builder with solid-part bookkeeping and interior/below-ground culling
# ==========================================================================
class NB(kit.Builder):
    def __init__(self, name):
        super().__init__(name)
        self.solid = set()
        self.markers = []    # (name, (x, y, z) mm, meaning)

    def add(self, tmp, role, tone=1.0, solid=True, **kw):
        if solid:
            self.solid.add(self.parts)
        super().add(tmp, role, tone=tone, **kw)

    def addm(self, tmp, fn, **kw):
        """fn(c, n) -> (role, tone): one decision drives both, so tones match roles."""
        self.add(tmp, lambda c, n: fn(c, n)[0], tone=lambda c, n: fn(c, n)[1], **kw)


def cull_inside(b, keep_below=False):
    """Delete faces that sit entirely inside another closed (solid) part, or
    entirely below the ground plane. Returns the number of faces removed."""
    bm = b.bm
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    parts = {}
    for f in bm.faces:
        parts.setdefault(f[b.l_part], []).append(f)
    bv = {}
    for p in b.solid:
        faces = parts.get(p)
        if not faces:
            continue
        verts = list({v for f in faces for v in f.verts})
        idx = {v: i for i, v in enumerate(verts)}
        lo = [min(v.co[i] for v in verts) for i in range(3)]
        hi = [max(v.co[i] for v in verts) for i in range(3)]
        bv[p] = (BVHTree.FromPolygons([v.co.copy() for v in verts], [[idx[v] for v in f.verts] for f in faces]),
                 lo, hi)
    eps = 0.02 * MM
    kill = []
    for f in bm.faces:
        if f[b.l_keep]:
            continue
        if not keep_below and all(v.co.z < -eps for v in f.verts):
            kill.append(f)
            continue
        c = f.calc_center_median()
        pts = [c] + [c + (v.co - c) * 0.92 for v in f.verts]
        me = f[b.l_part]
        for p, (tree, lo, hi) in bv.items():
            if p == me:
                continue
            if any(not all(lo[i] - eps <= q[i] <= hi[i] + eps for i in range(3)) for q in pts):
                continue
            inside = True
            for q in pts:
                loc, nor, _i, _d = tree.find_nearest(q)
                if loc is None or (q - loc).dot(nor) > -1e-9:
                    inside = False
                    break
            if inside:
                kill.append(f)
                break
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="FACES_ONLY")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
    return len(kill)


def bm_merge(dst, src):
    vm = {v: dst.verts.new(v.co) for v in src.verts}
    for f in src.faces:
        try:
            dst.faces.new([vm[v] for v in f.verts])
        except ValueError:
            pass
    src.free()
    return dst


# ==========================================================================
# Primitive generators (millimetres, temporary bmeshes)
# ==========================================================================
def tmp_tube(path, radii, seg=6, cap0=True, cap1=True):
    """Tube along any polyline with parallel-transport frames (vertical-safe).
    A radius of 0 makes a pole (pointed tip)."""
    path = [V(p) for p in path]
    n = len(path)
    bm = bmesh.new()
    t0 = (path[1] - path[0]).normalized()
    ref = V(1, 0, 0) if abs(t0.x) < 0.9 else V(0, 1, 0)
    e1 = (ref - t0 * ref.dot(t0)).normalized()
    prev = t0
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        e1 = prev.rotation_difference(t) @ e1
        e1 = (e1 - t * e1.dot(t)).normalized()
        e2 = t.cross(e1)
        r = radii[i]
        if r < 1e-6:
            rings.append([bm.verts.new(p)])
        else:
            rings.append([bm.verts.new(p + e1 * (r * math.cos(TAU * k / seg)) + e2 * (r * math.sin(TAU * k / seg)))
                          for k in range(seg)])
        prev = t
    kit._bridge_rings(bm, rings, closed=True)
    if cap0 and len(rings[0]) > 2:
        bm.faces.new(list(reversed(rings[0])))
    if cap1 and len(rings[-1]) > 2:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def tmp_clump(c, rx, ry, rz, level, seed, jitter=0.16, flat=0.72):
    """Foliage clump: jittered sphere. level 2 = ico/80 tris, 1 = uv 7x4 (42), 0 = ico/20."""
    bm = bmesh.new()
    if level >= 2:
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    elif level == 1:
        bmesh.ops.create_uvsphere(bm, u_segments=7, v_segments=4, radius=1.0)
    else:
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    c = V(c)
    rot = hsh(seed, 3.3) * TAU
    cs, sn = math.cos(rot), math.sin(rot)
    for v in bm.verts:
        d = v.co.normalized()
        k = 1 + jitter * (2 * hsh(d.x * 3.1 + seed, d.y * 2.7, d.z * 1.9) - 1)
        z = d.z * (flat if d.z < 0 else 1.0)
        x, y = d.x * cs - d.y * sn, d.x * sn + d.y * cs
        v.co = V(c.x + x * rx * k, c.y + y * ry * k, c.z + z * rz * k)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def vnoise3(x, y, z, seed):
    iz = math.floor(z)
    t = sstep(z - iz)
    return vnoise(x + 17.3 * iz, y - 9.1 * iz, seed) * (1 - t) + vnoise(x + 17.3 * (iz + 1), y - 9.1 * (iz + 1), seed) * t


def tmp_rock(c, rx, ry, rz, n, seed, jitter=0.22, floor_z=-0.6, yaw=0.0, squash_top=1.0, detail=0,
             amp=0.0, cuts=0, flat_top=0.0):
    """Natural rock: convex hull of a jittered Fibonacci ellipsoid, then (detail>0)
    subdivided and noise-displaced (concavities), cut by a few fracture planes,
    top flattened, base flat and sunk."""
    ga = math.pi * (3 - math.sqrt(5))
    c = V(c)
    cs, sn = math.cos(yaw), math.sin(yaw)
    pts = []
    for i in range(n):
        y = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(max(0.0, 1 - y * y))
        a = i * ga + seed
        d = V(r * math.cos(a), r * math.sin(a), y)
        k = 1 + jitter * (2 * hsh(i, seed, 1.7) - 1)
        x0, y0 = d.x * rx * k, d.y * ry * k
        z = d.z * rz * k * (squash_top if d.z > 0 else 1.0)
        p = V(c.x + x0 * cs - y0 * sn, c.y + x0 * sn + y0 * cs, c.z + z)
        p.z = max(p.z, floor_z)
        pts.append(p)
    bm = kit.tmp_hull(pts)
    if detail <= 0 and cuts <= 0 and flat_top <= 0:
        return bm
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    if detail > 0:
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=detail, use_grid_fill=True)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
    rmin = min(rx, ry, rz)
    top = max(v.co.z for v in bm.verts)
    planes = []
    for k in range(cuts):         # fracture planes: flat facets like split granite
        a = hsh(seed, k, 4.4) * TAU
        el = 0.15 + 0.9 * hsh(seed, k, 5.5)
        nrm = V(math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el))
        far = max((v.co - c).dot(nrm) for v in bm.verts)
        planes.append((nrm, far * (0.78 + 0.1 * hsh(seed, k, 6.6))))
    for v in bm.verts:
        p = v.co
        if amp > 0:
            d = (p - c)
            if d.length > 1e-6:
                nz = vnoise3(p.x * 0.55 + seed, p.y * 0.55, p.z * 0.55, int(seed * 7) % 97) - 0.5
                nz += 0.5 * (vnoise3(p.x * 1.4, p.y * 1.4 + seed, p.z * 1.4, int(seed * 11) % 89) - 0.5)
                p = p + d.normalized() * (amp * rmin * 2.0 * nz)
        for nrm, off in planes:
            s = (p - c).dot(nrm) - off
            if s > 0:
                p = p - nrm * s
        if flat_top > 0 and p.z > top * (1 - flat_top):
            zt = top * (1 - flat_top)
            p = V(p.x, p.y, zt + (p.z - zt) * 0.3)
        p = V(p.x, p.y, max(p.z, floor_z))
        v.co = p
    bm.normal_update()
    return bm


def tmp_cone(base, r, h, seg, phase=0.0, sink=0.0):
    """Open cone (side faces only): ring at base.z - sink, apex at base.z + h."""
    base = V(base)
    bm = bmesh.new()
    ring = [bm.verts.new(V(base.x + r * math.cos(phase + TAU * k / seg),
                           base.y + r * math.sin(phase + TAU * k / seg), base.z - sink)) for k in range(seg)]
    apex = bm.verts.new(V(base.x, base.y, base.z + h))
    for k in range(seg):
        bm.faces.new((ring[k], ring[(k + 1) % seg], apex))
    return bm


def tmp_blades(blades):
    """Open triangular blades: (root, tip, half_width, phase). 3 triangles each,
    wound so normals face outward; the open base sits below ground."""
    bm = bmesh.new()
    for root, tip, w, ph in blades:
        root, tip = V(root), V(tip)
        bs = [bm.verts.new(V(root.x + w * math.cos(ph + TAU * k / 3), root.y + w * math.sin(ph + TAU * k / 3), root.z))
              for k in range(3)]
        tv = bm.verts.new(tip)
        for k in range(3):
            bm.faces.new((bs[k], bs[(k + 1) % 3], tv))
    return bm


def tmp_tier(cx, cy, z0, r, h, n, seed, droop=0.0, mid=2, star=0.8, under=0.18, jr=0.14):
    """Conifer branch tier: irregular star skirt. Branch tips (even rim points)
    hang below z0 by ~droop, notches sit higher; a shoulder ring gives the
    up-then-drooping branch arc. mid: 2 = apex/inner/shoulder/rim, 1 = apex/
    shoulder/rim, 0 = apex/rim. Closed by an underside fan."""
    bm = bmesh.new()
    ph = hsh(seed, 1.1) * TAU
    apex = bm.verts.new(V(cx + 0.06 * r * (hsh(seed, 2.2) - 0.5), cy + 0.06 * r * (hsh(seed, 3.3) - 0.5), z0 + h))
    tipk = [1 + jr * (2 * hsh(k, seed) - 1) for k in range(n)]
    angs = [ph + TAU * (k + 0.3 * (hsh(k, seed, 7.0) - 0.5)) / n for k in range(n)]

    def ring_at(rf, zf, notch, dz_tip=0.0):
        out = []
        for k in range(n):
            a = angs[k]
            tip = k % 2 == 0
            rr = r * rf * (tipk[k] if tip else notch * (0.95 + 0.1 * hsh(k, seed, 8.0)))
            dz = (-dz_tip * (0.7 + 0.6 * hsh(k, seed, 9.0))) if tip else 0.0
            out.append(bm.verts.new(V(cx + rr * math.cos(a), cy + rr * math.sin(a), z0 + h * zf + dz)))
        return out
    rim = ring_at(1.0, 0.04, star, droop)
    cen = bm.verts.new(V(cx, cy, z0 + h * under))
    rings = [[apex]]
    if mid >= 2:
        rings.append(ring_at(0.46, 0.56, 0.9))
    if mid >= 1:
        rings.append(ring_at(0.8, 0.24, star * 1.05, droop * 0.25))
    rings.append(rim)
    kit._bridge_rings(bm, rings, closed=True)
    for k in range(n):
        bm.faces.new((cen, rim[(k + 1) % n], rim[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def tmp_frond(root, ang, length, width, rise, segs, droop=0.5):
    """V-folded fern frond arching out from root; single-sided, normals up."""
    root = V(root)
    d = V(math.cos(ang), math.sin(ang), 0)
    side = V(-d.y, d.x, 0)
    bm = bmesh.new()
    secs = []
    for k in range(segs + 1):
        t = k / segs
        p = root + d * (length * t) + V(0, 0, rise * math.sin(math.pi * t * (1 - droop * 0.5)) - droop * rise * t * t)
        w = width * math.sin(math.pi * min(1.0, 0.15 + t * 0.95)) if k < segs else 0.0
        fold = w * 0.45
        if w < 1e-6:
            secs.append([bm.verts.new(p)])
        else:
            secs.append([bm.verts.new(p + side * w + V(0, 0, fold)), bm.verts.new(p),
                         bm.verts.new(p - side * w + V(0, 0, fold))])
    for a, c in zip(secs, secs[1:]):
        if len(c) == 1:
            bm.faces.new((a[0], a[1], c[0]))
            bm.faces.new((a[1], a[2], c[0]))
        else:
            bm.faces.new((a[0], a[1], c[1], c[0]))
            bm.faces.new((a[1], a[2], c[2], c[1]))
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    return bm


def tmp_heightfield(xs, ys, H, zb=-0.5):
    """Closed block: height-field top over xs x ys, front row at zb, vertical
    back wall at ys[-1], end caps at xs[0]/xs[-1], bottom at zb (culled)."""
    bm = bmesh.new()
    top = [[bm.verts.new(V(x, y, max(zb, H(x, y)) if j > 0 else zb)) for x in xs] for j, y in enumerate(ys)]
    back = [bm.verts.new(V(x, ys[-1], zb)) for x in xs]
    nx, ny = len(xs), len(ys)
    for j in range(ny - 1):
        for i in range(nx - 1):
            bm.faces.new((top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i]))
    for i in range(nx - 1):
        bm.faces.new((top[-1][i], back[i], back[i + 1], top[-1][i + 1]))
    bm.faces.new([top[j][0] for j in range(ny)] + [back[0]])
    bm.faces.new([back[-1]] + [top[j][-1] for j in reversed(range(ny))])
    bm.faces.new([top[0][i] for i in range(nx)] + [back[i] for i in reversed(range(nx))])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    # triangulate now so ray casts for dressing see the final surface
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    return bm


# ==========================================================================
# Shared role/tone recipes
# ==========================================================================
def charred_fn(seed=0.0):
    def f(c, n):
        h = pn(c, 0.7, seed)
        if h < 0.3:
            return ("hair_dark", 0.72)
        if h > 0.74 and n.z > 0.2:
            return ("metal_dark", 1.12)          # grey ash on upward faces
        return ("rubber", 0.85 + 0.3 * pn(c, 1.3, seed + 1))
    return f


def bark_fn(role, tone, seed=0.0, birch=False):
    def f(c, n):
        if birch:
            if hsh(round(c.z * 1.4), round(c.x * 3), 5 + seed) < 0.28:
                return ("hair_dark", 0.85)
            return ("trim_paint", 0.95 * jit(c, seed, 0.04))
        return (role, tone * jit(c, seed, 0.1) * (0.92 + 0.08 * n.z))
    return f


def ash_patch(b, r, seed):
    """Disabled after review: a flat ash disc read as a black slab; the burnt
    ground decal/terrain carries the ash. Kept as a no-op for call sites."""
    return
    b.addm(tmp_clump((0, 0, -0.35), r, r * 0.9, 0.55, 0, seed, jitter=0.25, flat=1.0),
           lambda c, n: ("metal_dark", 1.05 * jit(c, seed, 0.1)) if hsh(c.x, c.y, seed) > 0.35 else ("rubber", 1.0))


def leaf_fn(pairs, seed=0.0, top=0.22, var=0.08):
    """pairs: [(role, tone)] chosen by coherent noise (patches, not a triangle
    mosaic); lighter where the face looks up, dark undersides."""
    def f(c, n):
        h = pn(c, 0.2, seed + 1.0)
        idx = int(max(0.0, min(0.999, (h - 0.2) / 0.6)) * len(pairs))
        role, t = pairs[idx]
        shade = 1 - top * 0.5 + top * (0.5 + 0.5 * n.z)
        if n.z < -0.3:
            shade *= 0.72
        return role, t * shade
    return f


# ==========================================================================
# Plant generators: gen(b, lod, q, rng, P)
# ==========================================================================
def clump_level(lod, cr, big):
    if lod == 0:
        return 2 if cr >= big else 1
    if lod == 1:
        return 1 if cr >= big else 0
    return 0


def g_decid(b, lod, q, rng, P):
    """Layered-crown broadleaf (also alder, olive, orchard): trunk(s), branches
    into the crown, tiers of foliage clumps around a core clump. P['char'] makes
    the burnt twin: same skeleton, bare branches, a few charred leaf remnants."""
    H, R = P["h"], P["r"]
    th = H * P.get("trunk", 0.42)
    r0 = P.get("rt", 1.1)
    char = P.get("char", False)
    lx, ly = P.get("lean", (0.0, 0.0))
    seg = (6, 5, 4)[lod] if not char else (6, 4, 3)[lod]
    bfn = charred_fn(1.0) if char else bark_fn(*P.get("bark", ("soil", 0.78)), birch=P.get("birch", False))
    crown_c = V(lx * 0.6, ly * 0.6, H * P.get("core_z", 0.66))
    # trunk(s)
    tops = []
    stems = P.get("stems") or [(0.0, 0.0, 0.0, 0.0)]
    for si, (dx, dy, tx, ty) in enumerate(stems):
        base = V(dx, dy, -0.6)
        top = V(lx * 0.5 + tx, ly * 0.5 + ty, th)
        mid = base.lerp(top, 0.5) + V(P.get("twist", 0.0) * math.cos(si * 2.4), P.get("twist", 0.0) * math.sin(si * 2.4), 0)
        rr = r0 * (1.0 if len(stems) == 1 else 0.72)
        b.addm(tmp_tube([base, base + V(0, 0, 0.9), mid, top], [rr * 1.5, rr * 1.3, rr, rr * 0.8], seg, cap1=char), bfn,
               smooth=True)
        tops.append(top)
    # crown clumps
    cl = []
    for li, (zf, rf, n, cf) in enumerate(P["layers"]):
        ph = rng.random() * TAU
        for k in range(n):
            a = ph + TAU * k / n + rng.uniform(-0.3, 0.3)
            rr = rf * R * rng.uniform(0.85, 1.08)
            c = V(math.cos(a) * rr + lx * zf, math.sin(a) * rr + ly * zf, zf * H + rng.uniform(-0.03, 0.03) * H)
            cr = cf * R * rng.uniform(0.9, 1.08)
            cl.append([cr, c, li, rng.random()])
    sq = P.get("sq", 0.8)
    rc = R * P.get("core", 0.55)
    top_z = max(e[1].z for e in cl)
    axis = lambda z: V(lx * min(1.0, z / H), ly * min(1.0, z / H), z)
    for e in cl:     # every clump must touch the core clump or the hidden leader on the crown axis
        cr, c = e[0], e[1]
        dc = V(c.x - crown_c.x, c.y - crown_c.y, (c.z - crown_c.z) / sq)
        if dc.length < 0.8 * (rc + cr):
            continue
        a = axis(c.z)
        hv = V(c.x - a.x, c.y - a.y, 0)
        if hv.length > 0.6 * cr:
            hv = hv.normalized() * (0.6 * cr)
        e[1] = V(a.x + hv.x, a.y + hv.y, c.z)
    kept = sorted(cl, key=lambda e: -e[0])[:n_of(len(cl), q)]
    grow = 1 + 0.22 * (1 - len(kept) / len(cl))
    leaves = P.get("leaf", [("foliage", 0.95), ("grass", 0.9)])
    big = P.get("big", R * 0.4)
    # branches: from the trunk top into the biggest low clumps
    nb = (4, 3, 2)[lod]
    lows = sorted([e for e in kept if e[2] == 0], key=lambda e: -e[0])[:nb]
    if char:
        lows = sorted(cl, key=lambda e: (e[2], -e[0]))[:(8, 4, 2)[lod]]
    for i, e in enumerate(lows):
        t0 = tops[i % len(tops)]
        end = e[1] if not char else e[1] + (e[1] - t0).normalized() * e[0] * 0.6
        mid = t0.lerp(end, 0.5) + V(0, 0, 0.12 * (end - t0).length)
        if char or lod < 2:
            b.addm(tmp_tube([t0 - V(0, 0, th * 0.12), mid, end],
                            [r0 * 0.55, r0 * 0.38, 0.0 if char else r0 * 0.25], (5, 4, 3)[lod],
                            cap0=False, cap1=not char), bfn, smooth=True)
        else:
            b.addm(tmp_tube([t0 - V(0, 0, th * 0.12), end], [r0 * 0.5, r0 * 0.28], 3, cap0=False), bfn, smooth=True)
    if char:
        # a leader to keep the burnt silhouette tall, plus a few charred leaf remnants
        top0 = tops[0]
        b.addm(tmp_tube([top0 - V(0, 0, 1), top0.lerp(crown_c, 0.6) + V(0, 0, H * 0.12), V(lx, ly, H * 0.9)],
                        [r0 * 0.6, r0 * 0.4, 0.0], (5, 4, 3)[lod], cap0=False), bfn, smooth=True)
        rem = []
        for cr, c, li, _ in rem:
            b.addm(tmp_clump(c, cr * 0.42, cr * 0.42, cr * 0.32, 1 if lod == 0 else 0, hsh(c.x, c.y)), charred_fn(2.0),
                   smooth=True)
        if lod == 0:      # twigs off each branch end
            for i, e in enumerate(lows):
                t0 = tops[i % len(tops)]
                end = e[1] + (e[1] - t0).normalized() * e[0] * 0.6
                mid = t0.lerp(end, 0.5) + V(0, 0, 0.12 * (end - t0).length)
                st = mid.lerp(end, 0.5)
                for k in (-1, 1):
                    d = (end - t0).normalized().cross(V(0, 0, 1)).normalized() * k + V(0, 0, 0.8)
                    b.addm(tmp_tube([st, st + d.normalized() * e[0] * 0.55], [r0 * 0.22, 0.0], 3, cap0=False), bfn)
        ash_patch(b, R * 0.55, 3.0)
        return
    # hidden leader up the crown axis, then the core clump binding trunk and crown (interiors are culled)
    b.addm(tmp_tube([tops[0] - V(0, 0, 1), axis(top_z)], [r0 * 0.5, r0 * 0.3], 4, cap0=False), bfn, smooth=True)
    b.addm(tmp_clump(crown_c, R * P.get("core", 0.55), R * P.get("core", 0.55), R * P.get("core", 0.55) * sq,
                     clump_level(lod, R, big), 7.7), leaf_fn(leaves[:1], 0.3), smooth=True)
    for cr, c, li, h in kept:
        pal = leaves[min(li, len(leaves) - 1):min(li, len(leaves) - 1) + 2] or leaves[-1:]
        lvl = clump_level(lod, cr * grow, big)
        b.addm(tmp_clump(c, cr * grow, cr * grow, cr * grow * sq, lvl, h * 97.0),
               leaf_fn(pal, h * 11.0, top=0.26), smooth=True)
    for role, n, r in P.get("dots", []):          # blossom / fruit / berry dots on the crown
        for k in range(n_of(n, q, 0)):
            e = kept[k % len(kept)]
            a = rng.random() * TAU
            el = rng.uniform(0.0, 1.1)
            d = V(math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el) * sq)
            p = e[1] + d * (e[0] * grow * 0.7)
            b.add(kit.tmp_hull([p + V(r, 0, 0), p + V(-r * 0.5, r * 0.87, 0), p + V(-r * 0.5, -r * 0.87, 0),
                                p + V(0, 0, r * 1.2)]), role[0], tone=role[1])


def g_conifer(b, lod, q, rng, P):
    """Star-rimmed conifer tiers (spruce/fir/pine), optional snow load; char:
    trunk with whorls of bare branch stubs and a few charred tier remnants."""
    H, R, nt = P["h"], P["r"], P["tiers"]
    clear = P.get("clear", 0.1) * H
    char = P.get("char", False)
    r0 = P.get("rt", 1.0)
    lx, ly = P.get("lean", (0.0, 0.0))
    nd, nt_l = P.get("n", 14), nt
    if lod >= 1:
        nt_l = max(4, int(round(nt * (0.85 if lod == 1 else 0.62))))
    n = max(6, 2 * int(round(nd * q / 2)))
    snow = P.get("snow", 0.0)
    needle = P.get("needle", [("foliage", 0.82), ("foliage", 0.95)])
    bfn = charred_fn(4.0) if char else bark_fn(*P.get("bark", ("soil", 0.7)))
    b.addm(tmp_tube([V(0, 0, -0.6), V(lx * 0.4, ly * 0.4, H * 0.5), V(lx, ly, H * (0.97 if char else 0.86))],
                    [r0 * 1.4, r0 * 0.8, 0.0 if char else r0 * 0.4], (6, 5, 4)[lod]), bfn, smooth=True)
    tiers = []
    for i in range(nt_l):
        t = i / nt_l
        z0 = clear + (H - clear) * t * P.get("spread", 0.9)
        rr = max(R * 0.22, R * (1 - t) ** P.get("taper", 0.85))
        h = (H - z0) if i == nt_l - 1 else (H - clear) / nt_l * P.get("tier_h", 1.9)
        tiers.append((z0, rr, h, t))
    if char:
        nb = (6, 3, 2)[lod]
        for i, (z0, rr, h, t) in enumerate(tiers[:-1]):
            c = V(lx * (z0 / H), ly * (z0 / H), z0 + h * 0.3)
            for k in range(nb):
                a = hsh(i, k, 3.1) * 0.6 + TAU * k / nb + i * 0.7
                d = V(math.cos(a), math.sin(a), 0.1)
                if lod == 0:
                    b.addm(tmp_tube([c, c + d * (rr * 0.5), c + d * (rr * 0.9) + V(0, 0, -rr * 0.25)],
                                    [r0 * 0.4, r0 * 0.25, 0.0], 4, cap0=False), bfn)
                else:
                    b.addm(tmp_tube([c, c + d * (rr * 0.85)], [r0 * 0.38, 0.0], 3, cap0=False), bfn)
        for i, (z0, rr, h, t) in enumerate(tiers):
            if (lod < 2 and hsh(i, 9.1) < 0.3) or i == len(tiers) - 1:
                b.addm(tmp_tier(lx * (z0 / H), ly * (z0 / H), z0, rr * 0.45, h * 0.6, 6, i + 31.0,
                                droop=0.3, mid=False), charred_fn(5.0))
        ash_patch(b, R * 0.7, 6.0)
        return
    sd = P.get("seed", 0)
    for i, (z0, rr, h, t) in enumerate(tiers):
        # per-tier jitter: centre wander, radius and height variation (no perfect cones)
        jx = (hsh(i, sd, 1.0) - 0.5) * rr * 0.12
        jy = (hsh(i, sd, 2.0) - 0.5) * rr * 0.12
        rr *= 0.84 + 0.32 * hsh(i, sd, 3.0)
        z0 += (hsh(i, sd, 4.0) - 0.5) * h * 0.12
        z0 = max(z0, rr * P.get("droop", 0.2) * 1.35 + 0.3)     # drooping tips stay above ground
        cx, cy = lx * (z0 / H) + jx, ly * (z0 / H) + jy
        role, tone = needle[i % len(needle)]
        tone *= 0.84 + 0.24 * t
        sn = snow

        def fn(c, nn, role=role, tone=tone, z0=z0, h=h, sn=sn, i=i):
            if sn > 0 and nn.z > 0.3 and (c.z - z0) > h * (0.4 - 0.32 * sn) and hsh(c.x, c.y, i) < 0.4 + 0.6 * sn:
                return ("snow", (0.92 + 0.07 * nn.z) * jit(c, i, 0.03))
            if nn.z < -0.15:                        # shaded underside of the branch skirt
                return (role, tone * 0.58 * jit(c, i, 0.06))
            return (role, tone * (0.8 + 0.3 * max(0.0, nn.z)) * jit(c, i + 0.5, 0.1))
        tilt_ax = V(math.cos(hsh(i, sd, 5.0) * TAU), math.sin(hsh(i, sd, 5.0) * TAU), 0)
        ctr = V(cx, cy, z0)
        tilt = Matrix.Translation(ctr) @ Matrix.Rotation(math.radians(7) * hsh(i, sd, 6.0), 4, tilt_ax) @ Matrix.Translation(-ctr)
        b.addm(tmp_tier(cx, cy, z0, rr, h, n, i * 7.3 + sd, droop=rr * P.get("droop", 0.2),
                        mid=(2, 1, 0)[lod], star=P.get("star", 0.66)), fn, smooth=True, xform=tilt)


def g_pads(b, lod, q, rng, P):
    """Pad-crowned pine (Scots pine, wind-swept coastal pine): curved bare trunk,
    a few flattened foliage pads near the top, optionally pushed downwind (+X)."""
    H, R = P["h"], P["r"]
    char = P.get("char", False)
    r0 = P.get("rt", 1.0)
    sw = P.get("sweep", 0.0)       # downwind offset of the top (mm, +X)
    bfn = charred_fn(7.0) if char else bark_fn(*P.get("bark", ("cedar", 0.62)))
    pts = [V(0, 0, -0.6), V(sw * 0.1, 0, H * 0.3), V(sw * 0.45, 0, H * 0.58), V(sw * 0.85, 0.0, H * 0.8)]
    b.addm(tmp_tube(pts, [r0 * 1.5, r0 * 1.0, r0 * 0.8, 0.0 if char else r0 * 0.55],
                    (6, 5, 4)[lod] if not char else (7, 4, 3)[lod]), bfn, smooth=True)
    pads = []
    for k, (zf, off, rf) in enumerate(P["pads"]):
        a = rng.uniform(0, TAU)
        c = V(sw * (zf * 1.05) + off * math.cos(a) * R + sw * 0.25 * zf, off * math.sin(a) * R, zf * H)
        pads.append((rf * R, c, k))
    kept = sorted(pads, key=lambda e: -e[0])[:n_of(len(pads), q)]
    grow = 1 + 0.2 * (1 - len(kept) / len(pads))
    top = pts[-1]
    for i, (cr, c, k) in enumerate(sorted(pads, key=lambda e: e[1].z)):
        if not char and (cr, c, k) not in kept:
            continue
        if char and lod == 2 and i >= 2:
            continue
        start = pts[2] if c.z < H * 0.7 else top
        end = c if not char else c + (c - start).normalized() * cr * 0.5
        mid = start.lerp(end, 0.5) + V(0, 0, 1.0)
        if char and lod == 2:
            b.addm(tmp_tube([start, end], [r0 * 0.45, 0.0], 3, cap0=False), bfn)
            continue
        b.addm(tmp_tube([start, mid, end], [r0 * 0.5, r0 * 0.35, 0.0 if char else r0 * 0.25], (5, 4, 3)[lod], cap0=False),
               bfn, smooth=True)
        if char and lod == 0:
            for sgn in (-1, 1):
                d = ((end - start).normalized().cross(V(0, 0, 1)).normalized() * sgn + V(0, 0, 0.5)).normalized()
                st = mid.lerp(end, 0.5)
                b.addm(tmp_tube([st, st + d * cr * 0.6], [r0 * 0.2, 0.0], 3, cap0=False), bfn)
    if char:
        ash_patch(b, R * 0.6, 8.0)
        return
    leaves = P.get("leaf", [("foliage", 0.78), ("foliage", 0.92), ("grass", 0.78)])
    for cr, c, k in kept:
        lvl = clump_level(lod, cr * grow, P.get("big", R * 0.45))
        b.addm(tmp_clump(c, cr * grow, cr * grow * 0.85, cr * grow * P.get("sq", 0.42), lvl, k * 13.0 + 1, jitter=0.2),
               leaf_fn(leaves, k * 3.0, top=0.3), smooth=True)


def g_bush(b, lod, q, rng, P):
    """Low bush / hardy shrub: ground-hugging clumps; char: bare twigs + ash."""
    R, Hh = P["r"], P["h"]
    char = P.get("char", False)
    sqb = P.get("sq", 0.82)
    rc = R * 0.62
    cl = [[rc, V(0, 0, max(rc * sqb * 0.62 - 0.5, Hh - rc * sqb)), 0]]
    for k in range(P.get("n", 6)):
        a = TAU * k / P.get("n", 6) + rng.uniform(-0.4, 0.4)
        rr = R * rng.uniform(0.38, 0.58)
        cr = R * rng.uniform(0.36, 0.5)
        cl.append([cr, V(math.cos(a) * rr, math.sin(a) * rr, cr * sqb * 0.72 * 0.86 - 0.5), k + 1])
    if char:
        bfn = charred_fn(9.0)
        nt = (10, 5, 3)[lod]
        for k in range(nt):
            a = TAU * k / nt + hsh(k, 2.0)
            tip = V(math.cos(a) * R * 0.7, math.sin(a) * R * 0.7, Hh * (0.6 + 0.4 * hsh(k, 5.0)))
            mid = V(0, 0, Hh * 0.2).lerp(tip, 0.4)
            if lod < 2:
                b.addm(tmp_tube([V(0, 0, -0.5), mid, tip], [0.45, 0.3, 0.0], 3, cap0=False), bfn)
            else:
                b.addm(tmp_tube([V(0, 0, -0.5), tip], [0.45, 0.0], 3, cap0=False), bfn)
            if lod == 0:
                d = V(-math.sin(a), math.cos(a), 0.9).normalized()
                st = mid.lerp(tip, 0.5)
                b.addm(tmp_tube([st, st + d * R * 0.3], [0.22, 0.0], 3, cap0=False), bfn)
        return
    kept = sorted(cl, key=lambda e: -e[0])[:n_of(len(cl), q)]
    grow = 1 + 0.2 * (1 - len(kept) / len(cl))
    leaves = P.get("leaf", [("foliage", 0.9), ("grass", 0.85)])
    for cr, c, k in kept:
        lvl = clump_level(lod, cr * grow, R * 0.55)
        rz = cr * grow * P.get("sq", 0.82)
        if k == 0:   # the raised centre clump reaches the ground, so outer clumps dropped at low LOD never strand it
            rz = max(rz, c.z + 0.4)
        b.addm(tmp_clump(c, cr * grow, cr * grow, rz, lvl, k * 5.0 + P.get("seed", 0), jitter=0.2),
               leaf_fn(leaves, k * 2.0, top=0.28), smooth=True)
    for role, n, r in (P.get("dots", []) if lod < 2 else []):
        for k in range(n_of(n, q, 0)):
            e = kept[k % len(kept)]
            a = rng.random() * TAU
            el = rng.uniform(0.15, 1.2)
            d = V(math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el) * 0.8)
            p = e[1] + d * (e[0] * grow * 0.7)
            b.add(kit.tmp_hull([p + V(r, 0, 0), p + V(-r * 0.5, r * 0.87, 0), p + V(-r * 0.5, -r * 0.87, 0),
                                p + V(0, 0, r * 1.1)]), role[0], tone=role[1])


def g_tuft(b, lod, q, rng, P):
    """Blade tufts: grass, dry grass, dune grass, reeds (+ cattails), flower patches."""
    n = n_of(P["blades"], q, 3)
    r, Hh = P["r"], P["h"]
    lean = P.get("lean", 0.35)
    wind = V(P.get("wind", (0.0, 0.0)) + (0.0,))
    bl = []
    for k in range(P["blades"]):
        a = rng.uniform(0, TAU)
        d = r * math.sqrt(rng.random())
        root = V(math.cos(a) * d, math.sin(a) * d, -0.5)
        h = Hh * rng.uniform(0.55, 1.0)
        out = V(math.cos(a), math.sin(a), 0) * (h * lean * rng.uniform(0.4, 1.0)) + wind * h
        bl.append((h, root, root + out + V(0, 0, h + 0.5), P.get("w", 0.35) * rng.uniform(0.8, 1.2), rng.uniform(0, TAU)))
    bl = sorted(bl, key=lambda e: -e[0])[:n]
    roles = P.get("roles", [("grass", 0.9), ("grass", 1.1), ("cloth_sage", 0.95)])

    def fn(c, nn):
        h = hsh(round(c.x * 4), round(c.y * 4), P.get("seed", 0))
        role, t = roles[int(h * len(roles)) % len(roles)]
        return role, t * jit(c, 2.0, 0.06)
    b.addm(tmp_blades([(e[1], e[2], e[3], e[4]) for e in bl]), fn, solid=False)
    if P.get("mound"):
        mr, mh, mrole = P["mound"]
        b.addm(tmp_rock((0, 0, -0.3), mr, mr * 0.8, mh + 0.3, (10, 8, 6)[lod], 4.2, jitter=0.12, floor_z=-0.5),
               lambda c, nn: (mrole[0], mrole[1] * jit(c, 1.0, 0.05) * (0.9 + 0.1 * nn.z)), smooth=True)
    heads = P.get("heads")          # [(role, tone)], count, size, height-frac
    if heads:
        roles_h, count, size = heads
        for k in range(n_of(count, q, 1 if lod < 2 else 0)):
            e = bl[k % len(bl)]
            tip = e[2]
            role, t = roles_h[k % len(roles_h)]
            if P.get("cattail"):
                p0 = e[1].lerp(tip, 0.72)
                p1 = e[1].lerp(tip, 0.95)
                ax = (p1 - p0)
                c = (p0 + p1) / 2
                s = size
                ring = [c + V(s, 0, 0), c + V(0, s, 0), c + V(-s, 0, 0), c + V(0, -s, 0)]
                b.add(kit.tmp_hull(ring + [p0 - ax * 0.2, p1 + ax * 0.15]), role, tone=t)
            else:
                c = tip + V(0, 0, size * 0.2)
                s = size
                b.add(kit.tmp_hull([c + V(s, 0, 0), c + V(-s * 0.5, s * 0.87, 0), c + V(-s * 0.5, -s * 0.87, 0),
                                    c + V(0, 0, s * 0.9), c + V(0, 0, -s * 0.6)]), role, tone=t)


def g_fern(b, lod, q, rng, P):
    n = n_of(P["fronds"], q, 3)
    segs = P.get("segs", (2, 2, 1))[lod]
    if P.get("char"):
        k_n = (12, 6, 3)[lod]
        bl = []
        for k in range(k_n):
            a = TAU * k / k_n + 0.4
            L = P["len"] * (0.45 + 0.25 * hsh(k, 1.0))
            bl.append((V(0.4 * math.cos(a), 0.4 * math.sin(a), -0.5),
                       V(L * 0.7 * math.cos(a), L * 0.7 * math.sin(a), P["rise"] * (0.45 + 0.3 * hsh(k, 2.0))), 0.3, a))
        b.addm(tmp_blades(bl), charred_fn(12.0), solid=False)
        return
    roles = P.get("roles", [("foliage", 1.05), ("grass", 0.85)])
    bm = bmesh.new()
    ph = rng.uniform(0, TAU)
    for k in range(n):
        a = ph + TAU * k / n + rng.uniform(-0.2, 0.2)
        L = P["len"] * rng.uniform(0.8, 1.05)
        bm_merge(bm, tmp_frond((0, 0, -0.45), a, L, P["w"], P["rise"] * rng.uniform(0.8, 1.1), segs, droop=P.get("droop", 0.6)))
    b.addm(bm, lambda c, nn: (roles[int(hsh(c.x * 3, c.y * 3, 1.0) * len(roles)) % len(roles)][0],
                              roles[int(hsh(c.x * 3, c.y * 3, 1.0) * len(roles)) % len(roles)][1] * jit(c, 1.0, 0.07)),
           solid=False, smooth=True)


# ==========================================================================
# Rocks
# ==========================================================================
ROCK_STYLE = {}


def _rock_style(name):
    def lit(n):
        return 0.84 + 0.2 * max(0.0, n.z) - (0.14 if n.z < -0.2 else 0.0)

    def plain(c, n, z):     # granite: warm grey with value variation in broad patches, rare cool streaks
        v = pn(c, 0.2, 21)
        if v > 0.66:
            return ("mineral", (0.5 + 0.12 * v) * lit(n))
        return ("stone", (0.5 + 0.2 * v) * lit(n))

    def moss(c, n, z):
        m = pn(c, 0.25, 31)
        if n.z > 0.58 and m < 0.48:
            return ("foliage", (0.9 + 0.3 * (0.48 - m)) * lit(n)) if pn(c, 0.3, 32) < 0.6 else ("grass", 0.66 * lit(n))
        if n.z > 0.2 and pn(c, 0.5, 33) > 0.78:
            return ("cloth_sage", 0.72 * lit(n))     # grey-green lichen
        return plain(c, n, z)

    def granite(c, n, z):   # W2: cooler grey, lichen and moss on top
        if n.z > 0.58 and pn(c, 0.25, 41) < 0.45:
            return ("foliage", 0.88 * lit(n))
        if n.z > 0.2 and pn(c, 0.5, 43) > 0.78:
            return ("cloth_sage", 0.72 * lit(n))
        v = pn(c, 0.18, 42)
        return ("stone", (0.46 + 0.18 * v) * lit(n)) if v < 0.5 else ("mineral", (0.46 + 0.16 * v) * lit(n))

    def snowy(c, n, z):
        if n.z > 0.45 + 0.25 * (pn(c, 0.6, 5) - 0.5):
            return ("snow", 0.92 + 0.08 * n.z)
        return ("cloth_slate", (1.12 + 0.2 * pn(c, 0.4, 6)) * lit(n))

    def algae(c, n, z):
        if c.z < z * 0.34:
            return ("hair_auburn", 0.6) if pn(c, 0.5, 7) < 0.35 else ("grass", 0.56)
        if c.z < z * 0.5:
            return ("cloth_slate", 0.84)
        return ("stone", (0.56 + 0.12 * pn(c, 0.5, 8)) * lit(n))

    def lava(c, n, z):
        if pn(c, 0.55, 9) > 0.66:
            return ("hair_auburn", 0.6 * lit(n))
        return ("rubber", (0.95 + 0.35 * pn(c, 0.8, 10)) * lit(n))

    def pumice(c, n, z):
        return ("cloth_slate", 1.6 * lit(n)) if pn(c, 0.45, 10) < 0.45 else ("stone", 0.78 * lit(n))

    def ice(c, n, z):
        if n.z > 0.72:
            return ("snow", 1.0) if pn(c, 0.8, 11) < 0.55 else ("mineral", 1.04)
        return ("window_glass", (2.2 + 0.45 * pn(c, 0.7, 12)))

    def basalt(c, n, z):
        if n.z > 0.8:
            return ("cloth_slate", 0.92 + 0.12 * pn(c, 0.5, 12))
        return ("metal_dark", 0.86 + 0.14 * pn(c, 0.4, 13))

    def ash(c, n, z):
        return ("cloth_slate", 1.18 + 0.14 * n.z) if pn(c, 0.4, 13) < 0.65 else ("stone", 0.56)

    return dict(plain=plain, moss=moss, granite=granite, snowy=snowy, algae=algae, lava=lava,
                pumice=pumice, ice=ice, basalt=basalt, ash=ash)[name]


def g_rocks(b, lod, q, rng, P):
    """P['rocks']: [(cx, cy, rx, ry, rz, yaw)] largest first; P['style'].
    LOD0/1: subdivided, noise-displaced, fractured hulls; LOD2: plain hulls."""
    style = _rock_style(P["style"])
    rocks = P["rocks"][:n_of(len(P["rocks"]), q if lod else 1.0)]
    top = max(r[4] * 2 for r in P["rocks"])
    natural = P.get("natural", True)
    for i, (cx, cy, rx, ry, rz, yaw) in enumerate(rocks):
        frac = (1.0, 0.62, 0.62)[lod] * q if lod < 2 else 0.6
        npt = max(7, int(round(P.get("pts", 16) * frac)))
        cz = rz * P.get("lift", 0.5)
        small = max(rx, ry) < 1.7
        det = 1 if (natural and lod < 2 and not small) else 0
        b.addm(tmp_rock((cx, cy, cz), rx, ry, rz, npt, i * 3.7 + P.get("seed", 0.0), jitter=P.get("jit", 0.2),
                        yaw=yaw, squash_top=P.get("squash", 0.85), detail=det,
                        amp=(P.get("amp", 0.13) if det else 0.0), cuts=(P.get("cuts", 2) if natural and lod < 2 else 0),
                        flat_top=(P.get("flat_top", 0.12) if natural else 0.0)),
               lambda c, n: style(c, n, top), smooth=P.get("smooth", True))


def g_basalt(b, lod, q, rng, P):
    """Hexagonal basalt columns on an axial grid; heights from P['hfun']."""
    rc = P.get("rc", 2.6)
    gap = 0.16
    style = _rock_style("basalt")
    cols = []
    for (qa, ra) in P["cells"]:
        x = (rc * 1.5 + gap) * qa
        y = (math.sqrt(3) * rc + gap) * (ra + qa / 2)
        h = P["hfun"](x, y, hsh(qa, ra, P.get("seed", 1.0)))
        if h > 0.8:
            cols.append((h, x, y, qa, ra))
    cols = sorted(cols, key=lambda e: -e[0])[:n_of(len(cols), q if lod == 0 else q)]
    for h, x, y, qa, ra in cols:
        tilt = V((hsh(qa, ra, 2.0) - 0.5) * 0.35, (hsh(qa, ra, 3.0) - 0.5) * 0.35, 1)
        pts = []
        for k in range(6):
            a = TAU * k / 6
            px, py = x + rc * math.cos(a), y + rc * math.sin(a)
            pts.append(V(px, py, -0.6))
            pts.append(V(px, py, h + tilt.x * (px - x) + tilt.y * (py - y)))
        b.addm(kit.tmp_hull(pts), lambda c, n: style(c, n, 0), smooth=False)
    for (x0, y0, x1, y1) in P.get("fallen", []):
        if lod < 2:
            p0, p1 = V(x0, y0, rc * 0.82 - 0.5), V(x1, y1, rc * 0.82 - 0.5)
            b.addm(kit.tmp_cyl(p0, p1, rc * 0.92, 6), lambda c, n: style(c, n, 0))


# ==========================================================================
# Props
# ==========================================================================
def wood_fn(role="cedar", tone=0.7, end_role="wood_deck", end_tone=1.0, axis=None, seed=0.0):
    def f(c, n):
        if axis is not None and abs(n.dot(axis)) > 0.9:
            return end_role, end_tone * jit(c, seed, 0.05)
        return role, tone * jit(c, seed, 0.1) * (0.9 + 0.1 * max(0.0, n.z))
    return f


def g_fence(b, lod, q, rng, P):
    """Rail fence, 40 mm pitch: end posts centred on x = +-20 (coincide when tiled)."""
    L, Hh = P.get("len", 40.0), P.get("h", 10.0)
    wf = wood_fn(P.get("wood", "cedar"), P.get("tone", 0.66), P.get("wood", "cedar"), P.get("tone", 0.66) * 1.2,
                 axis=V(0, 0, 1), seed=1.0)
    posts = [-L / 2, 0.0, L / 2] if lod < 2 else [-L / 2, L / 2]
    for i, x in enumerate(posts):
        h = Hh * (1.0 if i != 1 else 0.96)
        w = 0.62
        pts = [V(x + sx * w, sy * w, z) for sx in (-1, 1) for sy in (-1, 1) for z in (-0.6, h)] + [V(x, 0, h + 0.7)]
        b.addm(kit.tmp_hull(pts), wf, bev=(0.12 if lod == 0 else 0.0))
        if lod == 0 and not P.get("snow"):   # grass grown up around the post foot
            bl = [(V(x + 0.9 * math.cos(a), 0.9 * math.sin(a), -0.5),
                   V(x + 1.7 * math.cos(a + 0.3), 1.7 * math.sin(a + 0.3), 2.2 + 0.8 * hsh(x, a)), 0.28, a)
                  for a in (TAU * k / 7 + 0.3 for k in range(7))]
            b.addm(tmp_blades(bl), lambda c, n: (("grass", 0.85) if hsh(c.x, c.y) < 0.6 else ("cloth_sage", 0.8)),
                   solid=False)
    rails = P.get("rails", (0.42, 0.78))
    rw, rh, ry = P.get("rail_dims", (0.5, 0.75, 0.65))
    rt = P.get("rail_tone", (0.82, 0.74))
    for k, zf in enumerate(rails):
        sag = (0.25 if k == 0 else -0.15) * P.get("sag", 1.0)
        spans = ((-L / 2, 0.0), (0.0, L / 2)) if lod < 2 else ((-L / 2, L / 2),)
        for s0, s1 in spans:
            kit.rail(b, (s0, ry, Hh * zf), (s1, ry, Hh * zf + sag * (1 if s0 < 0 else -1) * (len(spans) - 1)), rw, rh,
                     P.get("rail_role", "wood_deck"), tone=rt[k % len(rt)] * (1.0 if s0 < 0 else 0.93),
                     bev=(min(rw, rh) * 0.22 if lod == 0 else 0.0))
    if P.get("slats") and lod < 2:
        n = n_of(P["slats"], q, 4)
        for k in range(n):
            x = -L / 2 + L * (k + 0.5) / n
            if min(abs(x - p) for p in posts) < 1.0:
                continue
            ht = Hh * (0.92 + 0.08 * hsh(k, 1.0))
            b.box(x - 0.55, 0.65 + 0.35, 0.8, x + 0.55, 0.65 + 0.75, ht, "wood_deck",
                  tone=(0.78 + 0.16 * hsh(k, 2.0)))
    if P.get("snow"):
        for k, zf in enumerate(rails):       # snow riding on the rails
            for s0, s1 in ((-L / 2 + 0.7, -0.7), (0.7, L / 2 - 0.7)):
                b.add(kit.tmp_hull([V(s0, 0.3, Hh * zf + 0.3), V(s1, 0.3, Hh * zf + 0.3), V(s0, 1.05, Hh * zf + 0.3),
                                    V(s1, 1.05, Hh * zf + 0.3), V(s0 + 1, 0.65, Hh * zf + 0.9),
                                    V(s1 - 1, 0.65, Hh * zf + 0.85)]), "snow", tone=0.98)
        secs = []
        nd = 5 if lod < 2 else 3     # drift sections - 1 (5 at LOD0/1 keeps the fence within its 400-tri budget)
        for i in range(nd + 1):
            x = -L / 2 + L * i / nd
            h = 1.2 + 2.2 * math.sin(i * 1.3 + 0.4) ** 2
            if i in (0, nd):
                h = 1.2
            secs.append([V(x, -2.4, -0.5), V(x, -1.2, h * 0.6), V(x, 0.4, h), V(x, 1.5, h * 0.5), V(x, 2.1, -0.5)])
        b.addm(kit.tmp_loft(secs), lambda c, n: ("snow", 0.96 + 0.04 * max(0.0, n.z)), smooth=True)


def g_haybale(b, lod, q, rng, P):
    r = 3.6
    seg = (12, 8, 6)[lod]
    hay = lambda c, n: (("wood_deck", 0.92) if abs(n.x) > 0.9 else
                        (("cloth_mustard", 0.84) if pn(c, 0.9, 1) < 0.4 else ("wood_deck", 1.06)))
    b.addm(kit.tmp_cyl((-3.6, 0, r - 0.5), (3.6, 0, r - 0.5), r, seg), lambda c, n: (hay(c, n)[0], hay(c, n)[1] * jit(c, 1.0, 0.06)),
           smooth=True)
    if lod == 0:
        for x in (-1.6, 1.6):   # net-wrap bands
            b.add(kit.tmp_cyl((x - 0.3, 0, r - 0.5), (x + 0.3, 0, r - 0.5), r + 0.08, seg, caps=False), "cloth_mustard",
                  tone=0.72, smooth=True)
    # small square bale leaning on it
    b.addm(kit.tmp_hull([V(x, y, z) for x in (-2.4, 2.4) for y in (-6.0, -3.2) for z in (-0.5, 2.6)]),
           lambda c, n: ("wood_deck", (0.95 if abs(n.y) > 0.9 else 1.04) * jit(c, 2.0, 0.05)))
    if lod < 2:
        b.box(-2.45, -6.05, 0.7, 2.45, -3.15, 0.95, "soil", tone=0.6)
        b.box(-2.45, -6.05, 1.75, 2.45, -3.15, 2.0, "soil", tone=0.6)
    if lod == 0:
        b.addm(tmp_blades([(V(rng.uniform(-4, 4), rng.uniform(-1.5, 3.8), -0.5), V(rng.uniform(-4.5, 4.5), rng.uniform(-2, 4), 1.2),
                            0.25, rng.uniform(0, TAU)) for _ in range(8)]),
               lambda c, n: ("wood_deck", 1.05), solid=False)


def g_vegrows(b, lod, q, rng, P):
    """Kitchen-garden patch 30 x 20 mm: soil bed, three ridged rows of crops."""
    W, D = 15.0, 10.0
    b.addm(kit.tmp_hull([V(x, y, z) for x in (-W, W) for y in (-D, D) for z in (-0.6, 0.35)]),
           lambda c, n: ("soil", 0.82 * jit(c, 1.0, 0.05)))
    rows = (-6.0, 0.0, 6.0)
    for k, y in enumerate(rows):
        pts = [V(x, y + s * 2.0, 0.2) for x in (-W + 1.2, W - 1.2) for s in (-1, 1)] + \
              [V(x, y, 1.3) for x in (-W + 1.6, W - 1.6)]
        b.addm(kit.tmp_hull(pts), lambda c, n: ("soil", (0.95 + 0.1 * n.z) * jit(c, k + 2.0, 0.05)))
    n_pl = n_of(7, q, 2)
    if lod == 2:
        for k, y in enumerate(rows):
            b.add(kit.tmp_hull([V(x, y + s * 1.4, 1.0) for x in (-W + 2.0, W - 2.0) for s in (-1, 1)] +
                               [V(x, y, 3.2 if k != 1 else 4.5) for x in (-W + 2.4, W - 2.4)]),
                  ("cloth_sage", "grass", "grass")[k], tone=(0.86, 0.84, 1.05)[k])
        return
    for k, y in enumerate(rows):
        for i in range(n_pl):
            x = -W + 2.8 + (2 * W - 5.6) * i / max(1, n_pl - 1)
            if k == 1:      # leeks: upright blades
                bl = [(V(x + 0.2 * math.cos(a), y + 0.2 * math.sin(a), 0.8),
                       V(x + 0.9 * math.cos(a), y + 0.9 * math.sin(a), 5.6), 0.3, a) for a in (0.3, 2.4, 4.4)]
                b.addm(tmp_blades(bl), lambda c, n: ("cloth_sage", 0.82 + 0.15 * hsh(c.x, c.y)), solid=False)
            else:           # cabbages / lettuce
                role, t = (("cloth_sage", 0.92) if k == 0 else ("grass", 1.12))
                b.addm(tmp_clump((x, y, 2.1), 1.5, 1.5, 1.25, 1 if (lod == 0 and k == 0) else 0, i * 3.0 + k),
                       lambda c, n, role=role, t=t: (role, t * (0.85 + 0.2 * n.z) * jit(c, 3.0, 0.06)), smooth=True)
    if lod == 0:   # stake + twine at the row ends
        for y in rows:
            b.add(kit.tmp_cyl((-W + 0.8, y, -0.5), (-W + 0.8, y, 4.0), 0.25, 4), "wood_deck", tone=0.8)


def g_logpile(b, lod, q, rng, P):
    L, r = 16.0, 1.55
    seg = (9, 6, 4)[lod]
    lay = [(-1, 0), (0, 0), (1, 0), (-0.5, 1), (0.5, 1), (0, 2)][:(6, 6, 5)[lod]]
    wf = wood_fn("cedar", 0.6, "wood_deck", 1.02, axis=V(1, 0, 0), seed=3.0)
    for i, (u, v) in enumerate(lay):
        y = u * (2 * r + 0.05)
        z = r - 0.5 + v * (r * (1.6 if lod < 2 else 1.15))   # stacked logs interpenetrate slightly: no floating at low LOD
        dx = (hsh(i, 1.0) - 0.5) * 1.6
        b.addm(kit.tmp_cyl((-L / 2 + dx, y, z), (L / 2 + dx, y, z), r * (0.95 + 0.08 * hsh(i, 2.0)), seg), wf, smooth=True)
    if lod == 0:
        for x in (-L / 2 + 1.5, L / 2 - 1.5):
            for s_ in (-1, 1):
                b.add(kit.tmp_cyl((x, s_ * (3 * r + 0.6), -0.5), (x, s_ * (3 * r + 0.4), 5.6), 0.32, 5), "cedar", tone=0.5)


def g_fallenlog(b, lod, q, rng, P):
    L, r = P.get("len", 28.0), P.get("r", 2.1)
    npts = (6, 4, 3)[lod]
    path = [V(-L / 2 + L * i / (npts - 1), 0.8 * math.sin(i * 1.1), r - 0.55 + 0.15 * math.sin(i * 0.7)) for i in range(npts)]
    radii = [r * (1.05 - 0.2 * i / (npts - 1)) for i in range(npts)]
    bark = P.get("bark", ("soil", 0.66))
    b.addm(tmp_tube(path, radii, (7, 6, 5)[lod]),
           lambda c, n: (("wood_deck", 0.98) if abs(n.x) > 0.92 and abs(c.x) > L / 2 - 0.5 else
                         (("foliage", 0.95) if (P.get("moss", True) and n.z > 0.8 and pn(c, 0.35, 2) < 0.42)
                          else (bark[0], bark[1] * (0.88 + 0.24 * pn(c, 0.6, 3))))), smooth=True)
    if lod < 2:
        for i, (x, a) in enumerate(((-4.0, 0.9), (3.0, -1.2), (8.0, 2.2))[:(3, 2)[lod]]):
            p0 = V(x, 0.5 * math.sin(a), r * 1.2 - 0.5)
            d = V(0.3, math.cos(a), math.sin(abs(a)) * 0.6 + 0.3).normalized()
            b.addm(tmp_tube([p0, p0 + d * (r * 1.9)], [r * 0.32, 0.0], 4, cap0=False),
                   lambda c, n: (bark[0], bark[1] * 0.9))


def g_stump(b, lod, q, rng, P):
    r, h = P.get("r", 2.8), P.get("h", 3.4)
    seg = (12, 7, 5)[lod]
    broken = P.get("broken", False)
    prof = [(0.0, r * 1.25, r * 1.2), (0.3, r * 1.0, r * 0.98), (1.0, r * 0.9, r * 0.88)]
    if lod == 0:
        prof = [(0.0, r * 1.3, r * 1.25), (0.15, r * 1.08, r * 1.05), (0.4, r * 0.98, r * 0.97), (0.75, r * 0.93, r * 0.92),
                (1.0, r * 0.9, r * 0.88)]
    bm = kit.tmp_lathe((0, 0, -0.6), (0, 0, h), prof, seg)
    if broken:
        for v in bm.verts:
            if v.co.z > h - 0.01:
                v.co.z = h + 1.6 * hsh(v.co.x, v.co.y, 1.0) - 0.4
    bark = P.get("bark", ("soil", 0.7))
    b.addm(bm, lambda c, n: (("wood_deck", 1.02) if (n.z > 0.85 and not broken) else
                             (("grass", 0.8) if (broken and n.z > 0.45 and pn(c, 0.6, 4) < 0.6)
                              else (bark[0], bark[1] * (0.9 + 0.2 * pn(c, 0.8, 1))))), smooth=True)
    nr = (5, 3, 0)[lod]
    for k in range(nr):
        a = TAU * k / nr + 0.4
        p0 = V(math.cos(a) * r * 0.6, math.sin(a) * r * 0.6, h * 0.35)
        p1 = V(math.cos(a) * r * 2.0, math.sin(a) * r * 2.0, -0.5)
        path = [p0, p0.lerp(p1, 0.55) + V(0, 0, 0.2), p1] if lod == 0 else [p0, p1]
        b.addm(tmp_tube(path, [r * 0.38, r * 0.25, 0.0][-len(path):] if lod == 0 else [r * 0.36, 0.0], 4 if lod == 0 else 3,
                        cap0=False), lambda c, n: (bark[0], bark[1] * 0.92))
    if lod == 0 and not broken:   # growth rings on the cut face
        b.add(kit.tmp_cyl((0, 0, h - 0.05), (0, 0, h + 0.06), r * 0.55, seg), "cedar", tone=0.75)


def g_charred_snag(b, lod, q, rng, P):
    """Generic burnt dead tree: tall broken trunk with a few bare branches."""
    H = P["h"]
    r0 = P.get("rt", 1.3)
    lx = P.get("lean", 0.0)
    fn = charred_fn(P.get("seed", 1.0))
    top = V(lx, 0, H)
    pts = [V(0, 0, -0.6), V(lx * 0.2, 0.2, H * 0.4), V(lx * 0.6, -0.2, H * 0.75), top]
    rad = [r0 * 1.6, r0 * 1.0, r0 * 0.7, r0 * 0.5]
    if lod == 0:
        pts.insert(1, V(lx * 0.05, 0.1, H * 0.12))
        rad.insert(1, r0 * 1.2)
    if lod == 2:
        pts, rad = [pts[0], pts[2], top], [rad[0], rad[2], rad[3]]
    b.addm(tmp_tube(pts, rad, (8, 5, 3)[lod], cap1=(lod == 2)), fn, smooth=True)
    if lod < 2:   # jagged broken top
        jag = [top + V(r0 * 0.5 * math.cos(a), r0 * 0.5 * math.sin(a), 0) for a in (0, 2.1, 4.2)] + \
              [top + V(0.2, 0.1, 2.4), top + V(-0.3, 0.2, 1.4)]
        b.addm(kit.tmp_hull(jag), fn)
    for i, (zf, a, L) in enumerate(P.get("branches", [])[:(5, 3, 1)[lod]]):
        p0 = V(lx * zf, 0, H * zf)
        d = V(math.cos(a), math.sin(a), 0.75).normalized()
        if lod == 0:
            pm = p0 + d * L * 0.55 + V(0, 0, 0.6)
            b.addm(tmp_tube([p0, pm, p0 + d * L], [r0 * 0.4, r0 * 0.25, 0.0], 5, cap0=False), fn, smooth=True)
            side = d.cross(V(0, 0, 1)).normalized()
            b.addm(tmp_tube([pm, pm + (side + V(0, 0, 0.8)).normalized() * L * 0.4], [r0 * 0.18, 0.0], 3, cap0=False), fn)
        else:
            b.addm(tmp_tube([p0, p0 + d * L], [r0 * 0.35, 0.0], 3, cap0=False), fn)


def g_campfire(b, lod, q, rng, P):
    n = (10, 8, 6)[lod]
    for k in range(n):
        a = TAU * k / n
        b.addm(tmp_rock((math.cos(a) * 3.9, math.sin(a) * 3.9, 0.35), 1.0, 0.8, 0.8, (10, 7, 6)[lod], k * 1.3, yaw=a),
               lambda c, nn: ("stone", (0.5 + 0.25 * nn.z) * (0.9 + 0.2 * pn(c, 0.8, 1))) if c.z > 0.3 else ("rubber", 0.9),
               smooth=(lod == 0))
    b.add(kit.tmp_cyl((0, 0, -0.5), (0, 0, 0.12), 3.1, (12, 8, 6)[lod]), "metal_dark", tone=1.05)
    fn = charred_fn(3.0)
    for k in range((4, 3, 0)[lod]):
        a = TAU * k / 4 + 0.3
        p0 = V(math.cos(a) * 2.6, math.sin(a) * 2.6, 0.1)
        if lod == 0:
            b.addm(tmp_tube([p0, p0.lerp(V(0, 0, 3.0), 0.5) + V(0, 0, 0.1), V(0, 0, 3.0)], [0.42, 0.36, 0.28], 5, cap0=False), fn)
        else:
            b.addm(tmp_tube([p0, V(0, 0, 3.0)], [0.42, 0.28], 4, cap0=False), fn)
    for k, (role, t) in enumerate((("roof_tile", 1.22), ("cloth_mustard", 1.12), ("roof_tile", 1.15))[:(3, 2, 1)[lod]]):
        a = TAU * k / 3
        b.add(tmp_clump((math.cos(a) * 0.9, math.sin(a) * 0.9, 0.35), 0.9, 0.9, 0.5, 1 if lod == 0 else 0, k + 0.5), role,
              tone=t)
    b.markers.append(("fire", (0.0, 0.0, 1.5), "fire/smoke VFX anchor (ring centre)"))


def g_drift(b, lod, q, rng, P):
    """Wind-carved drift (snow/ash): lofted sections, gentle windward slope,
    crest, slight lee overhang. LOD0 7x6, LOD1 5x5, LOD2 3x4 sections x points."""
    L, Hh, D = P["len"], P["h"], P["d"]
    ns, npf = ((7, 6), (5, 5), (3, 4))[lod]
    role = P.get("role", ("snow", 0.97))
    prof6 = [(-0.62, -0.5, 0), (-0.38, 0.4, 0), (-0.12, 0.85, 0), (0.08, 1.0, 0), (0.22, 0.88, 0), (0.34, -0.5, 0)]
    prof = {6: prof6, 5: [prof6[i] for i in (0, 1, 3, 4, 5)], 4: [prof6[i] for i in (0, 2, 4, 5)]}[npf]
    secs = []
    for i in range(ns):
        t = i / (ns - 1)
        x = -L / 2 + L * t
        sfac = math.sin(math.pi * (0.06 + 0.88 * t)) ** 0.7
        h = max(0.25, Hh * sfac * (0.88 + 0.24 * hsh(i * 7 // max(1, ns - 1), P.get("seed", 1.0))))
        dd = D * (0.35 + 0.65 * sfac)
        yo = 0.6 * math.sin(t * 3.0 + P.get("seed", 0))
        secs.append([V(x, yo + dd * py, (-0.5 if pz < 0 else h * pz)) for py, pz, _ in prof])
    b.addm(kit.tmp_loft(secs), lambda c, n: (role[0], role[1] * (0.9 + 0.12 * max(0.0, n.z)) * (0.97 + 0.06 * pn(c, 0.5, 1))),
           smooth=True)


def g_marker(b, lod, q, rng, P):
    """Snow marker pole: banded pole, signal reflector cap, snow mound."""
    Hh = P.get("h", 13.0)
    bm = kit.tmp_cyl((0, 0, -0.6), (0, 0, Hh), 0.45, (8, 5, 3)[lod])
    cuts = ((0.45, 0.6, 0.75, 0.9), (0.45, 0.6, 0.75, 0.9), (0.75,))[lod]
    kit.slice_bm(bm, (0, 0, 1), [Hh * f for f in cuts])
    if lod < 2:
        fn = lambda c, n: (("roof_tile", 0.98) if int((c.z / Hh - 0.45) / 0.15) % 2 == 0 and c.z > Hh * 0.45 else ("paper", 0.95))
    else:
        fn = lambda c, n: (("roof_tile", 0.98) if c.z > Hh * 0.75 else ("paper", 0.95))
    b.addm(bm, fn, smooth=True)
    if lod < 2:   # the reflector is sub-millimetre at LOD2 distance
        b.add(kit.tmp_hull([V(x, y, z) for x in (-0.75, 0.75) for y in (-0.3, 0.3) for z in (Hh - 1.6, Hh - 0.4)]),
              "cloth_mustard", tone=1.15)
    b.addm(tmp_clump((0, 0, -0.2), 2.2, 2.0, 1.3, (2, 0, 0)[lod], 2.0, jitter=0.12, flat=1.0),
           lambda c, n: ("snow", 0.95 + 0.04 * n.z), smooth=True)


def g_larch(b, lod, q, rng, P):
    """Bare winter larch: tapered trunk, whorls of drooping up-turned branches,
    a few snow clumps in the forks."""
    H = P["h"]
    R = P["r"]
    r0 = P.get("rt", 1.0)
    lx = P.get("lean", 0.0)
    bark = lambda c, n: ("hair_auburn", 0.95 * jit(c, 1.0, 0.1)) if c.z > H * 0.25 else ("soil", 0.66 * jit(c, 2.0, 0.08))
    b.addm(tmp_tube([V(0, 0, -0.6), V(lx * 0.3, 0, H * 0.45), V(lx, 0, H)], [r0 * 1.4, r0 * 0.75, 0.0], (6, 5, 4)[lod]),
           bark, smooth=True)
    nw = (P.get("whorls", 9), 7, 5)[lod]
    per = (4, 4, 3)[lod]
    two = lod == 0
    snow_spots = []
    for i in range(nw):
        t = (i + 0.5) / nw
        z = H * (0.18 + 0.75 * t)
        L = R * (1 - t) ** 0.8 + 1.2
        for k in range(per):
            a = TAU * k / per + i * 0.9 + hsh(i, k) * 0.5
            d = V(math.cos(a), math.sin(a), 0)
            p0 = V(lx * z / H, 0, z)
            if two:
                pm = p0 + d * (L * 0.55) + V(0, 0, -L * 0.12)
                p1 = p0 + d * L + V(0, 0, L * 0.15)
                b.addm(tmp_tube([p0, pm, p1], [0.32, 0.22, 0.0], 3, cap0=False), bark)
                spot = p0.lerp(pm, 0.75)
            else:
                b.addm(tmp_tube([p0, p0 + d * L], [0.3, 0.0], 3, cap0=False), bark)
                spot = p0 + d * (L * 0.3)
            if hsh(i, k, 7.0) < P.get("snow", 0.3) * q:
                snow_spots.append(spot)
    for k, p in enumerate(snow_spots[:(8, 5, 2)[lod]]):
        b.add(tmp_clump(p + V(0, 0, 0.2), 0.9, 0.9, 0.55, 0, k + 0.3), "snow", tone=0.98, smooth=True)
    b.addm(tmp_clump((0, 0, -0.3), 3.2, 3.0, 1.1, 0, 5.5, flat=1.0), lambda c, n: ("snow", 0.96), smooth=True)


def g_mooring(b, lod, q, rng, P):
    Hh, r = P.get("h", 12.0), 1.5
    seg = (10, 6, 5)[lod]
    bm = kit.tmp_lathe((0, 0, -0.6), (0, 0, Hh), [(0.0, r, r), (0.92, r, r), (0.97, r * 0.85, r * 0.85), (1.0, 0.0, 0.0)], seg)
    kit.slice_bm(bm, (0, 0, 1), [2.4])
    b.addm(bm, lambda c, n: (("grass", 0.62) if c.z < 2.4 else ("cedar", 0.55 * (0.9 + 0.2 * pn(c, 0.8, 2)))), smooth=True)
    if lod < 2:
        b.add(kit.tmp_cyl((0, 0, Hh * 0.62), (0, 0, Hh * 0.62 + 0.6), r + 0.12, seg, caps=False), "metal_dark", tone=0.9)
        turns = ((Hh * 0.45, Hh * 0.45 + 0.75), (Hh * 0.45,))[lod]
        n = (10, 6)[lod]
        for k, z in enumerate(turns):
            path = [V((r + 0.3) * math.cos(TAU * i / n), (r + 0.3) * math.sin(TAU * i / n), z) for i in range(n)]
            b.add(kit.tmp_sweep(path, 0.34, seg=4, closed=True), "cloth_cream", tone=0.82, smooth=True)
        if lod == 0:
            b.add(tmp_tube([V(r + 0.3, 0, Hh * 0.45), V(r + 2.6, -0.6, 2.0), V(r + 4.0, -1.0, 0.0)], [0.34, 0.34, 0.34], 4),
                  "cloth_cream", tone=0.8, smooth=True)


def g_buoy(b, lod, q, rng, P):
    """Floating buoy, pivot at the waterline (z = 0); hull extends below."""
    seg = (14, 8, 6)[lod]
    if P.get("kind") == "can":
        prof = [(0.0, 0.0, 0.0), (0.02, 1.6, 1.6), (0.12, 2.9, 2.9), (0.55, 2.9, 2.9), (0.62, 2.6, 2.6), (0.9, 0.9, 0.9),
                (1.0, 0.0, 0.0)]
        if lod == 1:
            prof = [prof[i] for i in (0, 2, 3, 4, 5, 6)]
        elif lod == 2:
            prof = [prof[i] for i in (0, 2, 4, 6)]
        bm = kit.tmp_lathe((0, 0, -2.6), (0, 0, 9.0), prof, seg)
        if lod < 2:
            kit.slice_bm(bm, (0, 0, 1), [2.2, 3.6])
        b.addm(bm, lambda c, n: ((("paper", 0.95) if 2.2 < c.z < 3.6 else ("roof_tile", 0.98))
                                 if c.z > 0.2 else ("rubber", 1.1)), smooth=True)
        if lod < 2:
            b.add(kit.tmp_cyl((0, 0, 8.6), (0, 0, 11.6), 0.25, 4), "metal_dark")
            b.add(kit.tmp_hull([V(x, y, z) for x in (-0.8, 0.8) for y in (-0.15, 0.15) for z in (10.4, 12.0)]),
                  "cloth_mustard", tone=1.12)
    else:
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=(9, 6, 4)[lod], radius=3.4,
                                  matrix=Matrix.Translation((0, 0, 1.2)))
        b.addm(bm, lambda c, n: (("cloth_mustard", 0.92) if c.z > -0.4 else ("rubber", 1.0)), smooth=True)
        if lod < 2:
            b.add(kit.tmp_sweep([V(1.0 * math.cos(TAU * i / 8), 0.0, 4.9 + 1.0 * math.sin(TAU * i / 8)) for i in range(8)],
                                0.25, seg=4, closed=True), "metal_dark", tone=0.9, smooth=True)
        else:
            b.add(kit.tmp_cyl((0, 0, 4.4), (0, 0, 5.6), 0.5, 4), "metal_dark")
    if lod == 0:
        b.add(kit.tmp_cyl((0, 0, 0.0), (0, 0, 0.12), 3.6 if P.get("kind") != "can" else 3.1, seg, caps=False), "paper",
              tone=0.9, keep=True)


def crate(b, x, y, z, s, h, yaw, detail, tone=1.0, seed=0.0):
    """Slatted crate s x s x h at (x, y, z) base, rotated by yaw.
    detail 3: slats + bevelled lid boards and corner posts; 2: slats; 1: core,
    posts and lid; 0: plain box."""
    rot = Matrix.Translation((x, y, z)) @ Matrix.Rotation(yaw, 4, "Z")
    w = s / 2
    if detail == 0:
        b.add(kit.tmp_box(-w, -w, 0, w, w, h), "wood_deck", tone=0.92 * tone, xform=rot)
        return
    b.add(kit.tmp_box(-w + 0.25, -w + 0.25, 0, w - 0.25, w - 0.25, h - 0.15), "cedar" if detail > 1 else "wood_deck",
          tone=(0.45 if detail > 1 else 0.8) * tone, xform=rot, keep=True)
    ns = 3
    sh = (h - 0.2) / ns
    bv = 0.1 if detail == 3 else 0.0
    if detail >= 2:
        for side in range(4):
            for k in range(ns):
                t = (0.92, 1.0, 0.86)[(k + side + int(seed)) % 3] * tone
                z0, z1 = k * sh + 0.12, (k + 1) * sh - 0.12
                bx = kit.tmp_box(-w + 0.3, w - 0.45, z0, w - 0.3, w, z1)
                kit.transform(bx, Matrix.Rotation(side * math.pi / 2, 4, "Z"))
                b.add(bx, "wood_deck", tone=t, xform=rot)
    for k in range(ns):   # lid boards
        y0 = -w + s * k / ns + 0.1
        b.add(kit.tmp_box(-w + 0.1, y0, h - 0.35, w - 0.1, y0 + s / ns - 0.2, h), "wood_deck",
              tone=(0.95, 0.88, 1.0)[k % 3] * tone, xform=rot, bev=bv)
    for sx in (-1, 1):    # corner posts
        for sy in (-1, 1):
            b.add(kit.tmp_box(sx * w - 0.45, sy * w - 0.45, 0, sx * w + 0.45, sy * w + 0.45, h - 0.3), "cedar",
                  tone=0.7 * tone, xform=rot, bev=bv)


def g_crates(b, lod, q, rng, P):
    det = P.get("detail", (3, 2, 0))[lod]
    for (x, y, z, s_, h, yaw, t) in P["crates"]:
        crate(b, x, y, z, s_, h, yaw, det, t, seed=x)


def g_barrels(b, lod, q, rng, P):
    seg = (7, 6, 5)[lod]

    def barrel(p0, p1, r):
        full = [(0.0, r * 0.86, r * 0.86), (0.13, r * 0.97, r * 0.97), (0.19, r * 1.0, r * 1.0), (0.5, r * 1.06, r * 1.06),
                (0.81, r * 1.0, r * 1.0), (0.87, r * 0.97, r * 0.97), (1.0, r * 0.86, r * 0.86)]
        prof = full if lod == 0 else ([full[0], full[3], full[6]] if lod == 1 else [full[0], full[6]])
        bm = kit.tmp_lathe(p0, p1, prof, seg)
        ax = (V(p1) - V(p0)).normalized()
        L = (V(p1) - V(p0)).length

        def fn(c, n):
            t = (c - V(p0)).dot(ax) / L
            if abs(n.dot(ax)) > 0.9:
                return ("wood_deck", 0.8)
            if lod == 0 and (0.13 < t < 0.19 or 0.81 < t < 0.87):
                return ("metal_dark", 0.9)
            return ("cedar", 0.72 * (0.9 + 0.2 * pn(c, 0.7, 1)))
        b.addm(bm, fn, smooth=True)
    for x, y in ((-2.3, 1.4), (2.3, 1.4), (0.0, 5.3)):
        barrel((x, y, -0.5), (x, y, 6.0), 2.1)
    barrel((-3.4, -3.4, 1.55), (3.4, -3.4, 1.55), 2.0)


def g_netrack(b, lod, q, rng, P):
    L, Hh = 24.0, 11.5
    for x in (-L / 2, L / 2):
        if lod < 2:
            for sgn in (-1, 1):
                kit.rail(b, (x, sgn * 3.4, -0.5), (x, sgn * 0.2, Hh), 0.55, 0.55, "wood_deck", tone=0.76)
        else:
            b.box(x - 0.3, -0.3, -0.5, x + 0.3, 0.3, Hh, "wood_deck", tone=0.76)
    b.add(kit.tmp_cyl((-L / 2 - 1.2, 0, Hh - 0.2), (L / 2 + 1.2, 0, Hh - 0.2), 0.45, (6, 5, 4)[lod]), "cedar", tone=0.6)
    nx = (9, 6, 3)[lod]
    rich = lod == 0
    secs = []
    bottoms = []
    for i in range(nx):
        t = i / (nx - 1)
        x = -L / 2 + 0.8 + (L - 1.6) * t
        drop = 2.6 + 1.6 * math.sin(i * 1.7) ** 2 + (0.8 if 0 < i < nx - 1 else 0.0)
        bot = 3.2 + 0.6 * math.sin(i * 2.3)
        th = 0.2
        if rich:
            secs.append([V(x, -2.4, bot), V(x, -1.2, Hh - drop * 0.6), V(x, 0, Hh - 0.15), V(x, 1.2, Hh - drop * 0.55),
                         V(x, 2.4, bot + 0.4), V(x, 2.4 - th, bot + 0.4), V(x, 1.2 - th, Hh - drop * 0.55 - th),
                         V(x, 0, Hh - 0.15 - th * 2), V(x, -1.2 + th, Hh - drop * 0.6 - th), V(x, -2.4 + th, bot)])
        else:
            secs.append([V(x, -2.4, bot), V(x, 0, Hh - 0.15), V(x, 2.4, bot + 0.4), V(x, 2.4 - th, bot + 0.4),
                         V(x, 0, Hh - 0.15 - th * 2), V(x, -2.4 + th, bot)])
        bottoms.append((V(x, -2.4 + th / 2, bot), V(x, 2.4 - th / 2, bot + 0.4)))
    b.addm(kit.tmp_loft(secs), lambda c, n: ("cloth_sage", 0.62) if pn(c, 0.3, 1) < 0.6 else ("cloth_slate", 0.95),
           smooth=False)
    if lod < 2:
        for k, (pa, pb) in enumerate(bottoms[1:-1][::(1, 2)[lod]]):
            for p in (pa, pb):
                r = 0.5
                b.add(kit.tmp_hull([p + V(r, 0, 0), p + V(-r * 0.5, r * 0.87, 0), p + V(-r * 0.5, -r * 0.87, 0),
                                    p + V(0, 0, -r * 1.2)]), "cloth_mustard", tone=1.05)


def g_pots(b, lod, q, rng, P):
    """Lobster pots: half-barrel slatted traps, two side by side and one on top."""
    def pot(cx, cy, cz, yaw):
        rot = Matrix.Translation((cx, cy, cz)) @ Matrix.Rotation(yaw, 4, "Z")
        L, W, Hh = 5.8, 4.2, 3.4
        b.add(kit.tmp_box(-L / 2, -W / 2, 0, L / 2, W / 2, 0.45), "wood_deck", tone=0.7, xform=rot, keep=True)
        na = (6, 5, 3)[lod]
        secs = []
        for x in (-L / 2 + 0.3, L / 2 - 0.3):
            secs.append([V(x, (W / 2 - 0.15) * math.cos(math.pi * k / (na - 1)),
                           0.4 + (Hh - 0.4) * math.sin(math.pi * k / (na - 1))) for k in range(na)])
        b.addm(kit.tmp_loft(secs, closed_ring=True), lambda c, n: ("cloth_slate", 0.8), xform=rot, smooth=False)
        if lod == 0:
            for x in (-L / 2 + 0.25, L / 2 - 0.25):
                path = [V(x, (W / 2) * math.cos(math.pi * k / 5), 0.4 + (Hh - 0.3) * math.sin(math.pi * k / 5)) for k in range(6)]
                b.add(tmp_tube(path, [0.22] * 6, 3), "wood_deck", tone=0.92, xform=rot, smooth=True)
    pot(-3.1, 0.0, -0.5, 0.05)
    pot(3.1, 0.3, -0.5, -0.08)
    pot(0.2, 0.1, 2.5, 0.12)
    if lod == 0:
        b.add(tmp_clump((6.8, -3.2, 0.6), 1.0, 1.0, 1.1, 0, 3.0, flat=1.0), "roof_tile", tone=1.0)


def g_driftwood(b, lod, q, rng, P):
    L = 20.0
    npts = (7, 4, 3)[lod]
    path = [V(-L / 2 + L * i / (npts - 1), 1.2 * math.sin(i * 1.4), 0.9 + 0.35 * math.sin(i * 2.1)) for i in range(npts)]
    radii = [1.4 * (1.0 - 0.55 * i / (npts - 1)) * (1 + 0.15 * math.sin(i * 3.1)) for i in range(npts)]
    fn = lambda c, n: (("stone", 0.84 * jit(c, 1.0, 0.1)) if hsh(round(c.x * 1.5), 2.0) < 0.6 else ("wood_deck", 0.9))
    b.addm(tmp_tube(path, radii, (6, 5, 4)[lod]), fn, smooth=True)
    # root knob + fork
    b.addm(tmp_rock((-L / 2 - 0.4, 0.2, 0.9), 1.9, 1.7, 1.5, (12, 8, 6)[lod], 2.0, jitter=0.3), fn, smooth=False)
    if lod < 2:
        p0 = path[npts // 2]
        b.addm(tmp_tube([p0, p0 + V(3.0, 2.6, 0.6), p0 + V(6.0, 3.6, -0.3)], [0.6, 0.45, 0.0], 4, cap0=False), fn,
               smooth=True)


def g_vent(b, lod, q, rng, P):
    """Steam vent cone: crater cone with sulphur-crusted rim; marker for VFX."""
    seg = (16, 9, 6)[lod]
    R, Hh = 10.0, 7.0
    prof = [(0.0, R, R), (0.35, R * 0.62, R * 0.6), (0.85, R * 0.32, R * 0.3), (1.0, R * 0.26, R * 0.25),
            (0.92, R * 0.17, R * 0.17), (0.62, 0.0, 0.0)]
    if lod == 2:
        prof = [prof[i] for i in (0, 2, 3, 5)]
    # lathe expects increasing t; build manually as rings
    bm = bmesh.new()
    rings = []
    for k, (t, rx, ry) in enumerate(prof):
        z = -0.6 + (Hh + 0.6) * t
        if rx < 1e-6:
            rings.append([bm.verts.new(V(0, 0, z))])
            continue
        ring = []
        for i in range(seg):
            a = TAU * i / seg
            j = 1 + 0.12 * (2 * hsh(i, k, 1.0) - 1)
            ring.append(bm.verts.new(V(rx * j * math.cos(a), ry * j * math.sin(a), z + (0.5 * (hsh(i, k) - 0.5) if 0 < k < 4 else 0))))
        rings.append(ring)
    kit._bridge_rings(bm, rings, closed=True)
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

    def fn(c, n):
        if c.z > Hh * 0.82:
            return ("cloth_mustard", 0.92) if pn(c, 0.35, 2) < 0.6 else ("paper", 0.86)
        if c.z > Hh * 0.6 and pn(c, 0.3, 3) < 0.35:
            return ("cloth_mustard", 0.78)
        return ("metal_dark", (0.85 + 0.25 * n.z)) if pn(c, 0.12, 5) < 0.68 else ("hair_auburn", 0.55)
    # the crater's inner faces point inward; recalc_face_normals on this closed shell keeps them outward-consistent
    b.addm(bm, fn, smooth=False)
    if lod < 2:
        for k in range(4 if lod == 0 else 2):
            a = TAU * k / 4 + 0.6
            b.addm(tmp_rock((math.cos(a) * R * 0.95, math.sin(a) * R * 0.95, 0.4), 1.4, 1.1, 1.0, 8, k + 2.0, yaw=a),
                   lambda c, n: ("rubber", 1.1 * jit(c, 5.0, 0.1)))
    b.markers.append(("steam", (0.0, 0.0, Hh * 0.62 + 0.3), "steam VFX anchor (crater floor)"))


def g_stonewall(b, lod, q, rng, P):
    """Dry-stone wall, 40 mm pitch along X: battered courses of irregular stones
    around a dark hidden core, vertical coping stones, moss on the tops."""
    L, Hh, T = P.get("len", 40.0), P.get("h", 7.0), P.get("t", 4.6)
    style = _rock_style(P.get("style", "moss"))
    b.box(-L / 2, -T * 0.16, -0.5, L / 2, T * 0.16, Hh - 1.6, "stone", tone=0.28, keep=True)

    def stone_fn(k):
        warm = hsh(k, 7.7) < 0.75
        t = (0.48 + 0.18 * hsh(k, 8.8)) if warm else (0.5 + 0.14 * hsh(k, 9.9))
        role = "stone" if warm else "mineral"

        def f(c, n):
            if n.z > 0.6 and pn(c, 0.22, 3) < 0.42:
                return ("foliage", 0.92) if pn(c, 0.3, 4) < 0.6 else ("grass", 0.66)
            if n.z > 0.1 and pn(c, 0.5, 5) > 0.8:
                return ("cloth_sage", 0.72)
            return (role, t * (0.86 + 0.18 * max(0.0, n.z)))
        return f
    if lod == 2:
        b.addm(kit.tmp_hull([V(x, y * s, z) for x in (-L / 2, L / 2) for y, z in ((T / 2, -0.5), (T * 0.36, Hh - 1.6))
                             for s in (-1, 1)]), lambda c, n: style(c, n, 99))
        for k in range(8):
            x = -L / 2 + L * (k + 0.5) / 8
            b.addm(tmp_rock((x, 0, Hh - 1.4), 2.4, T * 0.4, 1.5, 7, k + 0.5, jitter=0.15, floor_z=-99), lambda c, n: style(c, n, 99))
        return
    courses = 3
    hc = (Hh - 1.8) / courses
    srng = random.Random(seed_of("wall%d" % P.get("seed", 1)))
    mean = P.get("stone", 4.4) / max(0.3, q)
    for ci in range(courses):
        z0 = ci * hc
        half_t = T / 2 * (1 - 0.1 * ci)
        x = -L / 2 + (srng.uniform(0, mean * 0.5) if ci % 2 else 0.0)
        xs = [-L / 2]
        while x < L / 2 - 1.0:
            x += mean * srng.uniform(0.7, 1.3)
            xs.append(min(x, L / 2))
        if xs[-1] < L / 2:
            xs.append(L / 2)
        for k, (a, c_) in enumerate(zip(xs, xs[1:])):
            if c_ - a < 0.6:
                continue
            cz = z0 + hc * 0.5
            b.addm(tmp_rock(((a + c_) / 2, srng.uniform(-0.25, 0.25), cz), (c_ - a) * 0.62, half_t * 1.2, hc * 0.68,
                            (12, 9)[lod], ci * 31 + k + 0.5, jitter=0.14, floor_z=(-0.6 if ci == 0 else -99),
                            squash_top=0.85, yaw=srng.uniform(-0.15, 0.15)),
                   stone_fn(ci * 31 + k), smooth=True)
    nco = n_of(int(L / 2.6), q if lod == 0 else q * 0.8, 6)
    for k in range(nco):
        x = -L / 2 + L * (k + 0.5) / nco
        b.addm(tmp_rock((x, 0, Hh - 1.6), L / nco * 0.55, T * 0.4, 1.8, 9, k * 1.7 + 3.0, jitter=0.15, floor_z=-99,
                        yaw=srng.uniform(-0.2, 0.2)), stone_fn(100 + k), smooth=True)


def g_logjam(b, lod, q, rng, P):
    """River log jam: wet and bleached logs piled across each other with stubs."""
    seg = (7, 6, 5)[lod]
    logs = [((-14, -3, 1.2), (12, -5, 1.2), 1.8, "wet"), ((-12, 4, 1.0), (13, 2, 1.0), 1.6, "bleach"),
            ((-6, -8, 2.9), (8, 7, 3.1), 1.5, "wet"), ((10, -9, 0.7), (-4, 1, 4.8), 1.3, "bleach"),
            ((-15, 6, 0.4), (-5, 9, 0.6), 1.0, "wet"), ((3, 8, 0.4), (14, 10, 1.6), 0.9, "bleach")]
    logs = logs[:n_of(len(logs), q, 3) if lod < 2 else 3]

    def fn_for(kind, axis):
        def f(c, n):
            if abs(n.dot(axis)) > 0.9:
                return ("wood_deck", 1.0 * jit(c, 1.0, 0.06))
            if kind == "bleach":
                return ("stone", 0.8 * jit(c, 2.0, 0.1)) if hsh(round(c.x * 1.2), c.y, 3.0) < 0.7 else ("wood_deck", 0.82)
            if c.z < 0.9:
                return ("hair_dark", 0.9)
            return ("soil", 0.62 * jit(c, 3.0, 0.12)) if hsh(round(c.x), round(c.y), 4.0) < 0.75 else ("grass", 0.6)
        return f
    for i, (p0, p1, r, kind) in enumerate(logs):
        p0, p1 = V(p0), V(p1)
        mid = p0.lerp(p1, 0.5) + V(0, 0, 0.25 * math.sin(i))
        ax = (p1 - p0).normalized()
        path, radii = ([p0, mid, p1], [r, r * 0.95, r * 0.86]) if lod < 2 else ([p0, p1], [r, r * 0.88])
        b.addm(tmp_tube(path, radii, seg), fn_for(kind, ax), smooth=True)
        if lod < 2 and i < 4:
            t = 0.3 + 0.4 * hsh(i, 1.0)
            pb = p0.lerp(p1, t)
            d = ax.cross(V(0, 0, 1)).normalized() * (1 if i % 2 else -1) + V(0, 0, 0.6)
            b.addm(tmp_tube([pb, pb + d.normalized() * (r * 2.4)], [r * 0.32, 0.0], 4, cap0=False), fn_for(kind, ax))
    b.markers.append(("water_edge", (0.0, -10.0, 0.0), "place this line on the water's edge; logs reach into the water"))


# ==========================================================================
# Backdrop ring pieces (320 x 120 mm, <= 160 mm, tile end to end along X)
# ==========================================================================
BX, BY = 160.0, 60.0


def seam_w(x):
    return sstep((BX - abs(x)) / 55.0)


def ridge(x, y, y0, yc, yb, A, seed, var=0.5, rough=0.3, sc=90.0, fall=0.25, ridged=False):
    w = seam_w(x)
    nz = fbm(x / sc + 7.0, seed * 3.1, seed)
    if ridged:
        nz = 1 - abs(2 * fbm(x / (sc * 0.6) + 3.0, seed * 1.7, seed + 2) - 1)
    m = 1 + var * w * (2 * nz - 1)
    ycx = yc + w * (yb - yc) * 0.3 * (2 * fbm(x / 70 + 1.0, 3.3, seed + 11) - 1)
    if y <= ycx:
        base = sstep((y - y0) / max(1e-6, ycx - y0))
    else:
        base = 1 - fall * (y - ycx) / max(1e-6, yb - ycx)
    det = rough * w * (2 * fbm(x / 22 + 5.0, y / 18 + 2.0, seed + 5) - 1)
    z = -0.5 + (A * m + 0.5) * base * (1 + det)
    return min(158.0, max(-0.5, z))


def backdrop_recipe(world, var):
    """-> dict(layers=[(ys, H, fn)], trees=fn(rng, q, surf)->[(group, tmp)], extras=fn(b, lod, rng), markers)."""
    sd = {"a": 1, "b": 2, "c": 3}[var]
    R = {}

    def lin(a, b_, n):
        return [a + (b_ - a) * i / (n - 1) for i in range(n)]

    if world == "w1_alder":
        def f_front(c, n):
            if n.z < 0.78:
                return ("grass", 0.8 * jit(c, 1.0, 0.04))
            cell = hsh(math.floor((c.x + 400) / 34), math.floor((c.y + 400) / 13), 3.0 + sd)
            role, t = [("grass", 0.92), ("grass", 1.04), ("cloth_sage", 0.9), ("wood_deck", 0.86),
                       ("grass", 0.84), ("foliage", 1.2)][int(cell * 6)]
            return role, t * (0.94 + 0.12 * pn(c, 0.08, 2))

        def f_mid(c, n):
            if fbm(c.x / 30, c.y / 30, sd + 4) > 0.53:
                return ("foliage", (1.0 + 0.2 * pn(c, 0.1, 3)))
            return ("grass", (0.88 if n.z > 0.8 else 0.8) * (0.94 + 0.12 * pn(c, 0.08, 4)))

        def f_far(c, n):
            return ("cloth_sage", (0.92 + 0.16 * pn(c, 0.05, 6)) * (0.9 + 0.12 * max(0.0, n.z)))
        R["layers"] = [
            (lin(-60, -6, 8), lambda x, y: ridge(x, y, -60, -30, -6, 26, sd, 0.6, 0.3), f_front),
            (lin(-40, 30, 9), lambda x, y: ridge(x, y, -40, 2, 30, 70, sd + 10, 0.55, 0.35), f_mid),
            (lin(-12, 60, 8), lambda x, y: ridge(x, y, -12, 38, 60, 104, sd + 20, 0.28, 0.06, sc=150), f_far)]
        R["tree"] = dict(kind="blob", n=46, zone=(-55, 20), r=(3.2, 5.6), roles=[("foliage", 1.12), ("foliage", 1.26),
                                                                                ("grass", 0.92)], clusters=7)
    elif world == "w2_ember":
        f_front = lambda c, n: ("foliage", (0.62 + 0.12 * n.z) * jit(c, 1.0, 0.05))
        f_mid = lambda c, n: ("foliage", (0.68 + 0.12 * n.z) * (0.92 + 0.16 * pn(c, 0.06, 2)))
        f_far = lambda c, n: ("cloth_sage", (0.66 + 0.08 * n.z) * (0.94 + 0.12 * pn(c, 0.05, 3)))
        R["layers"] = [
            (lin(-60, -4, 8), lambda x, y: ridge(x, y, -60, -24, -4, 34, sd, 0.55, 0.35), f_front),
            (lin(-38, 32, 9), lambda x, y: ridge(x, y, -38, 8, 32, 92, sd + 10, 0.45, 0.3), f_mid),
            (lin(-10, 60, 8), lambda x, y: ridge(x, y, -10, 40, 60, 128, sd + 20, 0.22, 0.05, sc=150), f_far)]
        R["tree"] = dict(kind="cone", n=170, zone=(-56, 26), r=(3.6, 5.8), h=(13, 25), zmax=100, clusters=14,
                         roles=[("foliage", 0.82), ("foliage", 0.92), ("foliage", 1.02)],
                         alt=[("cloth_mustard", 0.68), ("roof_tile", 0.74), ("hair_auburn", 0.95)], alt_p=0.1)
    elif world == "w3_frost":
        def f_snowrock(rock_t, thr, snowline=999.0):
            def f(c, n):
                steep = n.z < thr + 0.12 * (pn(c, 0.08, 1) - 0.5)
                if steep and not (c.z > snowline and n.z > 0.28):
                    return ("cloth_slate", rock_t * (0.92 + 0.14 * pn(c, 0.1, 2)))
                return ("snow", (0.9 + 0.08 * n.z))
            return f
        R["layers"] = [
            (lin(-60, -6, 8), lambda x, y: ridge(x, y, -60, -28, -6, 30, sd, 0.6, 0.35), f_snowrock(1.28, 0.42)),
            (lin(-40, 30, 10), lambda x, y: ridge(x, y, -40, 4, 30, 100, sd + 10, 0.55, 0.45, sc=70, ridged=True),
             f_snowrock(1.3, 0.5, 62.0)),
            (lin(-12, 60, 9), lambda x, y: ridge(x, y, -12, 36, 60, 150, sd + 20, 0.35, 0.3, sc=80, ridged=True),
             f_snowrock(1.55, 0.45, 92.0))]
        R["tree"] = dict(kind="cone", n=70, zone=(-56, -5), r=(2.6, 4.2), h=(9, 17), zmax=38, snowtip=True,
                         roles=[("foliage", 0.8), ("foliage", 0.92), ("evergreen", 2.2)])
    elif world == "w4_tide":
        def coast(x, y):
            w = seam_w(x)
            ys = -38 + w * (({"a": 9 * math.exp(-(x / 55) ** 2), "b": -3.0, "c": 4.0}[var])
                            + 4 * (2 * fbm(x / 40, 1.0, sd) - 1))
            A = 50 * (1 + w * ({"a": 0.1, "b": 0.25, "c": -0.55}[var]) + w * 0.25 * (2 * fbm(x / 60, 2.0, sd + 3) - 1))
            if y < ys:
                return -0.5 + 2.4 * sstep((y + 60) / (ys + 60)) ** 0.5
            if y < ys + 4.5:
                return 1.9 + 2.3 * (y - ys) / 4.5
            yt = ys + 4.5 + (3.5 if var != "c" else 9.0)
            if y < yt:
                return 4.2 + (A - 4.2) * sstep((y - ys - 4.5) / (yt - ys - 4.5)) ** (0.6 if var != "c" else 1.0)
            return A + 7 * sstep((y - yt) / 30) + 3 * w * (2 * fbm(x / 20, y / 20, sd + 9) - 1)

        def f_coast(c, n):
            if c.z < 1.7:
                return ("wood_deck", 0.78)
            if abs(c.z - 2.15) < 0.4 and n.z > 0.25:
                return ("paper", 0.96)
            if c.z < 4.6 and n.z > 0.5:
                return ("stone", 1.0 * jit(c, 1.0, 0.04))
            if n.z < 0.5:
                band = math.floor(c.z / 3.4)
                return ("stone", (0.6 + 0.16 * hsh(band, 1.0)) * jit(c, 2.0, 0.04)) if hsh(band, 2.0) < 0.6 \
                    else ("cloth_slate", 1.15 * jit(c, 3.0, 0.04))
            if var == "c" and c.z < 26:
                return ("wood_deck", 1.0) if pn(c, 0.1, 4) < 0.55 else ("cloth_sage", 0.9)
            return ("grass", 0.9 * jit(c, 5.0, 0.05)) if fbm(c.x / 25, c.y / 25, 3.0) < 0.6 else ("cloth_sage", 0.95)
        f_far = lambda c, n: ("cloth_sage", (0.88 + 0.14 * pn(c, 0.05, 6)) * (0.9 + 0.12 * max(0.0, n.z)))
        ys = [-60, -55] + [-52 + 2.4 * i for i in range(19)] + [-4, 2, 8]
        R["layers"] = [
            (ys, coast, f_coast),
            (lin(-6, 60, 8), lambda x, y: ridge(x, y, -6, 40, 60, 92, sd + 20, 0.35, 0.25, sc=110), f_far)]
        R["sea"] = (2.0, ("water", 0.9))
        R["tree"] = dict(kind="blob", n=14, zone=(-30, 6), r=(3.0, 4.5), zmin=10,
                         roles=[("foliage", 0.82), ("foliage", 0.95)], sq=0.55)
        if var == "b":
            R["stacks"] = [(-70, -54, 4.5, 30), (-48, -51, 3.0, 22), (82, -55, 5.0, 26)]
    elif world == "w5_cinder":
        def chan(x, y):
            if var not in ("a", "b") or seam_w(x) < 0.99:
                return 99.0
            xc = (-30 if var == "a" else 40) + 12 * math.sin(y / 22.0)
            return abs(x - xc)

        def flank(x, y):
            z = ridge(x, y, -36, 18, 32, 86, sd + 10, 0.35, 0.3)
            d = chan(x, y)
            if d < 7 and -34 < y < 22:
                z -= 2.0 * (1 - d / 7)
            return z

        def f_front(c, n):
            v = fbm(c.x / 24, c.y / 16, sd + 1)
            if abs(v - 0.5) < 0.008 and n.z > 0.6:
                return ("roof_tile", 1.15)
            return ("rubber", (0.95 + 0.3 * n.z)) if v < 0.56 else ("metal_dark", 0.82)

        def f_flank(c, n):
            d = chan(c.x, c.y)
            if d < 2.6 and -34 < c.y < 22:
                return ("cloth_mustard", 1.12)
            if d < 6.0 and -34 < c.y < 22:
                return ("roof_tile", 1.22 * jit(c, 3.0, 0.03))
            if d < 8.5 and -34 < c.y < 22:
                return ("rubber", 0.85)
            if c.z > 60:
                return ("cloth_slate", (1.2 + 0.12 * n.z) * jit(c, 4.0, 0.04))
            return ("metal_dark", (0.85 + 0.15 * n.z)) if fbm(c.x / 30, c.y / 30, 7.0) < 0.66 \
                else ("hair_auburn", 0.5)

        def summit(x, y):
            z = ridge(x, y, -8, 36, 60, 122, sd + 20, 0.2, 0.15, sc=140)
            if var == "b":
                w = seam_w(x)
                z = -0.5 + (z + 0.5) * (1 + 0.27 * w * math.exp(-(x / 70) ** 2))
                d = math.sqrt(x * x + ((y - 36) * 1.4) ** 2)
                if d < 17:
                    z -= 15 * (1 - (d / 17) ** 2)
            return min(158.0, z)

        def f_far(c, n):
            if var == "b" and math.sqrt(c.x ** 2 + ((c.y - 36) * 1.4) ** 2) < 11 and c.z < 140:
                return ("roof_tile", 1.24)
            return ("cloth_slate", (1.1 + 0.15 * n.z)) if pn(c, 0.04, 3) < 0.65 else ("metal_dark", 1.05)
        R["layers"] = [
            (lin(-60, -10, 8), lambda x, y: ridge(x, y, -60, -32, -10, 16, sd, 0.6, 0.6), f_front),
            ([-36, -30, -24, -18, -12, -6, 0, 6, 12, 18, 25, 32], flank, f_flank),
            (lin(-8, 60, 10), summit, f_far)]
        R["tree"] = dict(kind="blob", n=12, zone=(-56, -30), r=(2.4, 3.6), roles=[("grass", 0.85), ("cloth_sage", 0.85)])
        if var == "b":
            R["markers"] = [("glow_crater", (0.0, 36.0, 135.0), "lava-glow / steam VFX anchor (summit crater)")]
        if var in ("a", "b"):
            xc = -30 if var == "a" else 40
            R.setdefault("markers", []).append(("glow_channel", (xc, -5.0, flank(xc, -5.0) + 1.0),
                                                "lava-channel glow VFX anchor"))
    return R


def g_backdrop(b, lod, q, rng, P):
    R = backdrop_recipe(P["world"], P["variant"])
    nx = (33, 17, 11)[lod]
    xs = [-BX + 2 * BX * i / (nx - 1) for i in range(nx)]
    bvhs = []
    for li, (ys, H, fn) in enumerate(R["layers"]):
        step = (1, 2, 3)[lod]
        ysl = ys[::step]
        if ysl[-1] != ys[-1]:
            ysl.append(ys[-1])
        tm = tmp_heightfield(xs, ysl, H)
        bvhs.append(BVHTree.FromBMesh(tm))
        b.addm(tm, fn, smooth=True)
    if "sea" in R:
        z, (role, t) = R["sea"]
        b.addm(kit.tmp_box(-BX, -BY, -0.5, BX, 2.0, z), lambda c, n: (role, t * (0.97 + 0.06 * hsh(round(c.x / 20), 1.0))))
    for (sx, sy, sr, sh) in R.get("stacks", []):
        if lod < 2:
            b.addm(tmp_rock((sx, sy, sh * 0.45), sr, sr * 0.8, sh * 0.55, (14, 9)[lod], sx, jitter=0.15),
                   lambda c, n: ("grass", 0.85) if n.z > 0.8 and c.z > 12 else ("stone", (0.62 + 0.15 * n.z) * jit(c, 1.0, 0.05)))

    def surf(x, y):
        """Height of this LOD's actual terrain mesh under (x, y) (ray cast)."""
        best = -1.0
        for bv in bvhs:
            hit = bv.ray_cast(V(x, y, 400.0), V(0, 0, -1), 1000.0)
            if hit[0] is not None:
                best = max(best, hit[0].z)
        return best
    T = R.get("tree")
    if T and lod < 2:
        n = n_of(T["n"], q * (1.0 if lod == 0 else 0.45), 0)
        groups = {}
        trng = random.Random(zlib.crc32(("trees" + P["world"] + P["variant"]).encode()))
        cands = []
        centres = [(trng.uniform(-140, 140), trng.uniform(*T["zone"])) for _ in range(T.get("clusters", 10))]
        for i in range(T["n"] * 3):
            cx, cy = centres[i % len(centres)]
            x = cx + trng.gauss(0, 22)
            y = cy + trng.gauss(0, 9)
            cands.append((x, y, trng.random(), trng.random(), trng.random()))
        placed = 0
        for x, y, r1, r2, r3 in cands:
            if placed >= n:
                break
            if abs(x) > BX - 8 or not (T["zone"][0] <= y <= T["zone"][1]):
                continue
            z = surf(x, y)
            if z < T.get("zmin", 4.0) or z > T.get("zmax", 999):
                continue
            gz = max(abs(surf(x + 2, y) - surf(x - 2, y)), abs(surf(x, y + 2) - surf(x, y - 2))) / 4
            if gz > 1.1:
                continue
            r = T["r"][0] + (T["r"][1] - T["r"][0]) * r1
            alt = T.get("alt") and r3 < T.get("alt_p", 0)
            pal = T["alt"] if alt else T["roles"]
            role = pal[int(r2 * len(pal)) % len(pal)]
            if T["kind"] == "blob":
                tm = tmp_clump((x, y, z + r * T.get("sq", 0.85) * 0.35 - (0.6 if lod == 0 else 2.0)), r, r, r * T.get("sq", 0.85), 0, x * 0.1)
            else:
                h = T["h"][0] + (T["h"][1] - T["h"][0]) * r2
                tm = tmp_cone((x, y, z), r, h, 5, phase=r3 * TAU, sink=1.5)
                if T.get("snowtip"):
                    groups.setdefault(("snow", 0.97), bmesh.new())
                    bm_merge(groups[("snow", 0.97)], tmp_cone((x, y, z + h * 0.5), r * 0.42, h * 0.5 + 0.2, 5,
                                                               phase=r3 * TAU))
            groups.setdefault(role, bmesh.new())
            bm_merge(groups[role], tm)
            placed += 1
        for (role, t), bm in groups.items():
            b.addm(bm, lambda c, nn, role=role, t=t: (role, t * (0.85 + 0.25 * max(0.0, nn.z)) * jit(c, 1.0, 0.06)),
                   solid=False, smooth=True, keep=True)   # keep: buried halves anchor the trees to the slope
    for nm, p, meaning in R.get("markers", []):
        b.markers.append((nm, p, meaning))


# ==========================================================================
# Asset table
# ==========================================================================
ASSETS = []
FORCE_PREVIEW = [False]
PLACE = {
    "tree": dict(min_spacing_mm=16, cluster="groups of 3-7 at 10-16 mm spacing, or single specimens",
                 yaw="random", scale_range=[0.9, 1.1],
                 keep_off="routes, nodes, equipment sockets and rescue targets; prefer non-interactive cells and the "
                          "board margin (tall: can hide residents from the seated view)"),
    "shrub": dict(min_spacing_mm=8, cluster="loose groups of 2-5", yaw="random", scale_range=[0.85, 1.15],
                  keep_off="routes and nodes"),
    "rock": dict(min_spacing_mm=4, cluster="scatter in 2-6s, larger pieces at slope feet and cell edges",
                 yaw="random", scale_range=[0.8, 1.2], keep_off="route centre lines"),
    "ground_cover": dict(min_spacing_mm=3, cluster="dense drifts of 6-30; may sit at cell edges and along routes",
                         yaw="random", scale_range=[0.8, 1.2], keep_off="route centre lines (edges are fine)"),
    "prop": dict(min_spacing_mm=10, cluster="single, or small vignettes of 2-3 related props", yaw="random or "
                 "aligned to the nearest cell edge", scale_range=[0.95, 1.05], keep_off="routes, nodes and sockets"),
    "backdrop": dict(min_spacing_mm=0, cluster="ring outside the board edge, end to end along local X at a 320 mm "
                     "pitch, any order of variants", yaw="local -Y (front) faces the board", scale_range=[1.0, 1.0],
                     keep_off="the board itself and the 30 mm clear margin"),
}


def A(world, aid, cat, tags, gen, P=None, design="", place=None, charred=False, keep_below=False):
    P = dict(P or {})
    pl = dict(PLACE[cat])
    pl.update(place or {})
    ASSETS.append(dict(id=aid, world=world, category=cat, tags=list(tags), gen=gen, P=P, design=design, place=pl,
                       keep_below=keep_below))
    if charred:
        Pc = dict(P)
        Pc["char"] = True
        plc = dict(pl)
        plc["swap_for"] = aid
        plc["note"] = "burnt twin of %s: same pivot and footprint, swap in when fire burns the cell" % aid
        ASSETS.append(dict(id=aid + "_charred", world=world, category=cat, tags=[t for t in tags if t != "meadow"] + ["burnt"],
                           gen=gen, P=Pc, design="charred twin: blackened trunk/stems, bare or remnant crown, ash ring",
                           place=plc, keep_below=keep_below))


def rock_list(spec):
    return [tuple(r) for r in spec]


# ---------------------------------------------------------------- W1 Alder Valley
W = "w1_alder"
LEAF_W1 = [("foliage", 0.84), ("foliage", 0.97), ("grass", 0.8), ("foliage", 1.1)]
A(W, "w1_tree_broadleaf_a", "tree", ["forest", "meadow", "village"], g_decid,
  dict(h=56, r=13.5, trunk=0.4, rt=1.25, layers=[(0.5, 0.7, 7, 0.34), (0.65, 0.58, 7, 0.33), (0.79, 0.4, 5, 0.32),
                                                  (0.92, 0.12, 2, 0.3)], big=5.4,
       leaf=LEAF_W1, sq=0.78), "oak-like broadleaf: three crown tiers of foliage clumps, darker below, sunlit grass-green tops",
  charred=True)
A(W, "w1_tree_broadleaf_b", "tree", ["forest", "meadow", "orchard", "village"], g_decid,
  dict(h=46, r=13.0, trunk=0.36, rt=1.15, layers=[(0.52, 0.68, 7, 0.36), (0.7, 0.5, 6, 0.35), (0.88, 0.2, 3, 0.33)],
       leaf=LEAF_W1[1:], sq=0.74, big=5.6,
       lean=(1.2, -0.6)), "rounder, lower broadleaf with two crown tiers and a slight lean", charred=True)
A(W, "w1_tree_birch_c", "tree", ["forest", "meadow", "riverbank"], g_decid,
  dict(h=58, r=10.0, trunk=0.46, rt=0.95, birch=True, layers=[(0.5, 0.55, 4, 0.42), (0.66, 0.5, 4, 0.4), (0.8, 0.36, 3, 0.4),
                                                              (0.94, 0.1, 1, 0.36)],
       leaf=[("grass", 0.84), ("foliage", 1.12), ("grass", 0.94)], sq=0.85, core=0.5),
  "slender birch: white trunk with dark flecks, light spring-green layered crown", charred=True)
A(W, "w1_alder_a", "tree", ["riverbank", "near_water"], g_decid,
  dict(h=50, r=10.5, trunk=0.44, rt=0.95, stems=[(-1.0, 0.4, 2.5, 0.6), (1.0, -0.5, 4.0, -0.4)], twist=0.6,
       bark=("soil", 0.62), lean=(5.0, 0.0), layers=[(0.52, 0.55, 5, 0.38), (0.66, 0.5, 5, 0.37), (0.8, 0.38, 4, 0.36),
                                                     (0.93, 0.12, 2, 0.34)], big=4.6,
       leaf=[("foliage", 0.82), ("foliage", 0.95), ("grass", 0.82)], sq=0.92, core=0.5),
  "twin-stemmed alder leaning toward the water (+X), narrow dark oval crown, grey bark",
  place=dict(min_spacing_mm=10, cluster="lines of 2-5 along the bank", yaw="rotate so local +X (lean) points at the water",
             edge="water_edge: trunk base 2-8 mm from the water line"), charred=True)
A(W, "w1_alder_b", "tree", ["riverbank", "near_water"], g_decid,
  dict(h=42, r=9.5, trunk=0.4, rt=0.9, stems=[(0.0, 0.0, 2.0, 0.0), (-0.8, 0.9, 0.5, 2.2), (0.9, 0.8, 3.0, 2.0)], twist=0.5,
       bark=("soil", 0.58), lean=(4.0, 1.0), layers=[(0.6, 0.48, 5, 0.42), (0.85, 0.22, 2, 0.4)],
       leaf=[("foliage", 0.86), ("foliage", 1.0)], sq=0.88),
  "three-stemmed low alder clump leaning toward the water (+X)",
  place=dict(min_spacing_mm=10, cluster="lines of 2-5 along the bank", yaw="rotate so local +X (lean) points at the water",
             edge="water_edge: trunk base 2-8 mm from the water line"), charred=True)
A(W, "w1_conifer_spruce", "tree", ["forest", "rocky"], g_conifer,
  dict(h=58, r=11.5, tiers=8, clear=0.07, needle=[("foliage", 0.66), ("foliage", 0.78), ("foliage", 0.9)], seed=1.0),
  "spruce: seven star-rimmed drooping tiers", charred=True)
A(W, "w1_conifer_fir", "tree", ["forest", "village"], g_conifer,
  dict(h=46, r=12.5, tiers=6, clear=0.1, taper=0.7, tier_h=2.1, needle=[("foliage", 0.72), ("foliage", 0.86)],
       droop=0.15, star=0.74, seed=3.0), "fir: six broad, softer tiers", charred=True)
A(W, "w1_bush_a", "shrub", ["meadow", "forest", "village"], g_bush,
  dict(r=8.5, h=11, n=6, leaf=[("foliage", 0.95), ("grass", 0.9)], seed=1.0), "rounded hedge bush", charred=True)
A(W, "w1_bush_b", "shrub", ["meadow", "village", "orchard"], g_bush,
  dict(r=7.5, h=9, n=5, leaf=[("grass", 0.95), ("foliage", 1.05)], dots=[(("paper", 0.98), 9, 0.55)], seed=2.0),
  "flowering bush with white blossom dots", charred=True)
A(W, "w1_reeds_a", "ground_cover", ["riverbank", "near_water"], g_tuft,
  dict(blades=17, r=3.2, h=13.5, lean=0.16, w=0.32, roles=[("cloth_sage", 0.9), ("grass", 0.88), ("wood_deck", 1.0)],
       heads=([("hair_auburn", 0.85), ("soil", 0.7)], 3, 0.55), cattail=True),
  "reed clump with three cattail heads", place=dict(edge="water_edge: in the first 0-4 mm of water or bank",
                                                    min_spacing_mm=4))
A(W, "w1_reeds_b", "ground_cover", ["riverbank", "near_water"], g_tuft,
  dict(blades=22, r=4.2, h=11, lean=0.3, w=0.3, roles=[("grass", 0.85), ("cloth_sage", 1.0), ("grass", 1.05)], seed=2.0),
  "wide fanning reed/sedge clump", place=dict(edge="water_edge: in the first 0-4 mm of water or bank", min_spacing_mm=4))
A(W, "w1_grass_a", "ground_cover", ["meadow", "forest", "riverbank", "village"], g_tuft,
  dict(blades=24, r=2.2, h=5.6, lean=0.4, w=0.34, roles=[("grass", 0.82), ("cloth_sage", 0.78), ("foliage", 1.12),
                                                           ("grass", 0.98)]),
  "meadow grass tuft")
A(W, "w1_grass_b", "ground_cover", ["meadow", "riverbank", "orchard"], g_tuft,
  dict(blades=24, r=3.0, h=4.2, lean=0.55, w=0.34, roles=[("grass", 0.9), ("foliage", 1.18), ("cloth_sage", 0.86),
                                                            ("wood_deck", 0.92)], seed=3.0),
  "low spreading grass tuft")
A(W, "w1_flowers_a", "ground_cover", ["meadow", "village", "orchard"], g_tuft,
  dict(blades=11, r=3.2, h=4.4, lean=0.3, w=0.3, roles=[("grass", 0.95), ("grass", 1.1)],
       heads=([("paper", 0.98), ("cloth_mustard", 1.12), ("paper", 0.98)], 6, 0.6)),
  "wildflower patch: white and yellow heads")
A(W, "w1_flowers_b", "ground_cover", ["meadow", "riverbank"], g_tuft,
  dict(blades=11, r=3.4, h=4.8, lean=0.25, w=0.3, roles=[("grass", 0.9), ("cloth_sage", 1.0)],
       heads=([("cloth_plum", 1.25), ("cloth_blue", 1.35), ("roof_tile", 1.05)], 6, 0.6), seed=4.0),
  "wildflower patch: purple, cornflower blue and poppy red heads")
A(W, "w1_rock_boulder", "rock", ["meadow", "rocky", "riverbank"], g_rocks,
  dict(style="moss", rocks=[(0, 0, 6.5, 5.0, 5.2, 0.3), (5.6, 2.4, 2.4, 2.0, 1.8, 1.0)], pts=18),
  "mossy boulder (12 mm) with a small companion stone")
A(W, "w1_rock_slabs", "rock", ["rocky", "riverbank", "meadow"], g_rocks,
  dict(style="plain", rocks=[(0, 0, 5.5, 3.8, 2.0, 0.2), (3.8, -3.0, 3.6, 2.6, 1.4, 1.1), (-4.0, 2.8, 2.6, 2.0, 1.1, 2.0)],
       pts=12, squash=0.7, lift=0.3), "three flat stone slabs (river slabs)")
A(W, "w1_rock_cluster", "rock", ["rocky", "meadow", "forest"], g_rocks,
  dict(style="moss", rocks=[(0, 0, 2.6, 2.2, 2.4, 0.0), (2.9, 1.2, 1.8, 1.5, 1.6, 1.0), (-1.4, 2.6, 1.5, 1.3, 1.2, 2.0),
                            (1.5, -2.5, 1.2, 1.0, 1.0, 0.5)], pts=12), "cluster of four small stones (3-5 mm)")
A(W, "w1_fence_timber", "prop", ["meadow", "village", "orchard"], g_fence,
  dict(len=40, h=10, wood="wood_deck", tone=0.58, rail_tone=(0.68, 0.6)), "split-rail timber fence segment; tiles at a 40 mm pitch along X (end posts coincide)",
  place=dict(cluster="lines along cell edges", yaw="align local X to the cell edge", tile_pitch_mm=40,
             min_spacing_mm=0))
A(W, "w1_hay_bale", "prop", ["meadow", "village"], g_haybale, {}, "round hay bale (7 mm) with a small square bale")
A(W, "w1_veg_rows", "prop", ["village", "meadow"], g_vegrows, {},
  "kitchen garden 30 x 20 mm: three soil ridges of cabbage, leek and lettuce",
  place=dict(cluster="single, next to cottages", yaw="align to cell grid", footprint_cells=[1.5, 1.0]))
A(W, "w1_stone_wall", "prop", ["meadow", "village", "rocky", "orchard"], g_stonewall, dict(len=40, h=7.0, t=4.6, seed=1),
  "dry-stone wall segment: three battered courses of irregular granite stones, vertical coping, moss on top; "
  "40 mm pitch along X",
  place=dict(cluster="lines along field and cell edges", yaw="align local X to the edge", tile_pitch_mm=40,
             min_spacing_mm=0))
A(W, "w1_fence_board", "prop", ["meadow", "village"], g_fence,
  dict(len=40, h=9.0, rails=(0.38, 0.72), rail_dims=(0.35, 1.5, -0.85), rail_tone=(0.7, 0.62, 0.76), sag=0.3,
       wood="wood_deck", tone=0.56, rail_role="wood_deck"),
  "post-and-board timber fence: sawn posts, two weathered boards nailed to the front; 40 mm pitch",
  place=dict(cluster="lines along cell edges", yaw="align local X to the cell edge", tile_pitch_mm=40,
             min_spacing_mm=0))
A(W, "w1_log_jam", "prop", ["riverbank", "near_water"], g_logjam, {},
  "river log jam: six wet and bleached logs piled across each other (30 x 20 mm)",
  place=dict(edge="water_edge: marker _mk_water_edge on the bank line, logs reaching into the water",
             min_spacing_mm=30))
A(W, "w1_driftwood", "prop", ["riverbank", "near_water"], g_driftwood, {}, "bleached forked driftwood on the bank",
  place=dict(edge="riverbank, 0-10 mm above the water line"))
A(W, "w1_boulders_mossy", "rock", ["forest", "rocky", "meadow", "riverbank"], g_rocks,
  dict(style="moss", rocks=[(0, 0, 6.2, 5.0, 5.4, 0.3), (6.2, 2.8, 3.6, 3.0, 3.0, 1.1), (-4.6, 4.2, 3.0, 2.6, 2.4, 2.1),
                            (2.4, -5.0, 2.2, 1.8, 1.6, 0.7), (-6.4, -2.4, 1.5, 1.3, 1.1, 2.6)], pts=16, seed=8.0),
  "mossy boulder cluster: one 11 mm boulder with four companions")
A(W, "w1_fern_clump", "shrub", ["forest", "riverbank", "meadow"], g_fern,
  dict(fronds=14, len=8.0, w=1.6, rise=5.5, segs=(3, 2, 1), roles=[("foliage", 1.0), ("grass", 0.78), ("foliage", 1.18),
                                                                  ("cloth_sage", 0.78)]),
  "lush fern clump: fourteen arching fronds (16 mm across)", charred=True)

# ---------------------------------------------------------------- W2 Emberwood
W = "w2_ember"
NEEDLE_W2 = [("foliage", 0.62), ("foliage", 0.72), ("foliage", 0.84)]
A(W, "w2_pine_scots", "tree", ["forest", "rocky"], g_pads,
  dict(h=60, r=11.0, rt=1.0, bark=("cedar", 0.72), sweep=2.0,
       pads=[(0.66, 0.55, 0.5), (0.78, 0.45, 0.55), (0.9, 0.2, 0.55), (0.7, 0.6, 0.42), (0.84, 0.55, 0.4)],
       leaf=[("foliage", 0.72), ("foliage", 0.84), ("grass", 0.7)], sq=0.5),
  "Scots pine: tall orange-barked trunk, flat needle pads high up", charred=True)
A(W, "w2_pine_dense", "tree", ["forest"], g_conifer,
  dict(h=61, r=11.0, tiers=9, clear=0.06, needle=NEEDLE_W2, droop=0.24, bark=("soil", 0.8), seed=5.0),
  "dense cone pine, dark Emberwood green", charred=True)
A(W, "w2_pine_young", "tree", ["forest", "burnt"], g_conifer,
  dict(h=40, r=9.5, tiers=6, n=12, clear=0.12, needle=[("foliage", 0.74), ("foliage", 0.86)], droop=0.16, seed=7.0),
  "young pine (replanting / forest edge)", charred=True)
A(W, "w2_autumn_ochre", "tree", ["forest", "meadow"], g_decid,
  dict(h=50, r=13.0, trunk=0.4, rt=1.15, bark=("soil", 0.82), layers=[(0.5, 0.68, 7, 0.35), (0.66, 0.55, 6, 0.34),
                                                                     (0.8, 0.38, 5, 0.33), (0.93, 0.1, 2, 0.3)],
       leaf=[("foliage", 1.0), ("cloth_mustard", 0.66), ("wood_deck", 0.78), ("cloth_mustard", 0.76)], sq=0.8, big=5.2),
  "ochre late-summer broadleaf", charred=True)
A(W, "w2_autumn_orange", "tree", ["forest", "meadow"], g_decid,
  dict(h=44, r=12.0, trunk=0.38, rt=1.1, bark=("soil", 0.8), lean=(-1.0, 0.8),
       layers=[(0.52, 0.66, 7, 0.36), (0.7, 0.5, 6, 0.35), (0.88, 0.2, 3, 0.33)],
       leaf=[("hair_auburn", 0.95), ("roof_tile", 0.74), ("cloth_mustard", 0.66), ("roof_tile", 0.82)], sq=0.78, big=5.0),
  "rust-orange broadleaf (roof-tile terracotta / russet, not rescue orange)", charred=True)
A(W, "w2_dead_charred_a", "tree", ["burnt", "forest"], g_charred_snag,
  dict(h=46, rt=1.3, branches=[(0.55, 0.4, 7.0), (0.68, 2.6, 6.0), (0.78, 4.4, 5.0), (0.86, 1.4, 4.0), (0.6, 5.5, 4.5)],
       seed=1.0), "standing burnt snag with bare branches")
A(W, "w2_dead_charred_b", "tree", ["burnt"], g_charred_snag,
  dict(h=28, rt=1.5, lean=3.5, branches=[(0.5, 0.9, 6.0), (0.7, 3.6, 5.0)], seed=2.0, ash=5.0),
  "broken, leaning burnt trunk")
A(W, "w2_stump_cut", "prop", ["forest", "burnt"], g_stump, dict(r=2.8, h=3.4, bark=("soil", 0.75)),
  "sawn stump with root flare and ring face")
A(W, "w2_stump_broken", "prop", ["forest"], g_stump, dict(r=3.0, h=4.2, broken=True, bark=("soil", 0.68)),
  "old broken stump with moss")
A(W, "w2_log_pile", "prop", ["forest", "village"], g_logpile, {}, "stacked log pile (16 mm) held by stakes",
  place=dict(yaw="align local X to a path or cell edge"))
A(W, "w2_fallen_log", "prop", ["forest"], g_fallenlog, dict(len=28, r=2.1), "fallen mossy log with branch stubs")
A(W, "w2_fern_a", "ground_cover", ["forest"], g_fern, dict(fronds=6, len=6.5, w=1.4, rise=3.6),
  "bracken fern, six arching fronds")
A(W, "w2_fern_b", "ground_cover", ["forest"], g_fern,
  dict(fronds=7, len=5.2, w=1.2, rise=2.8, roles=[("cloth_mustard", 0.78), ("grass", 0.8)], droop=0.5),
  "late-summer fern turning ochre")
A(W, "w2_grass_dry_a", "ground_cover", ["meadow", "forest", "burnt"], g_tuft,
  dict(blades=24, r=2.2, h=5.6, lean=0.45, w=0.32, roles=[("wood_deck", 0.95), ("cloth_mustard", 0.82),
                                                            ("wood_deck", 1.08), ("cloth_sage", 0.78)]),
  "dry grass tuft")
A(W, "w2_grass_dry_b", "ground_cover", ["meadow", "burnt"], g_tuft,
  dict(blades=18, r=3.0, h=4.0, lean=0.6, w=0.34, roles=[("cloth_mustard", 0.78), ("wood_deck", 0.92), ("grass", 0.78)],
       seed=5.0), "low flattened dry grass")
A(W, "w2_rock_a", "rock", ["forest", "rocky"], g_rocks,
  dict(style="granite", rocks=[(0, 0, 6.0, 4.6, 4.4, 0.4), (4.8, -2.6, 2.2, 1.8, 1.6, 1.2)], pts=16),
  "lichen granite boulder")
A(W, "w2_rock_b", "rock", ["forest", "rocky"], g_rocks,
  dict(style="granite", rocks=[(0, 0, 3.2, 2.4, 2.0, 0.0), (3.2, 1.4, 2.0, 1.6, 1.4, 1.0), (-2.6, 1.8, 1.6, 1.4, 1.1, 2.0)],
       pts=12, seed=4.0), "small granite stone cluster")
A(W, "w2_campfire_ring", "prop", ["forest"], g_campfire, {}, "stone campfire ring (9.8 mm) with charred logs and embers",
  place=dict(cluster="single, at camps", min_spacing_mm=20))
A(W, "w2_boulders_mossy", "rock", ["forest", "rocky"], g_rocks,
  dict(style="granite", rocks=[(0, 0, 6.6, 5.2, 5.0, 0.5), (5.8, -3.0, 3.4, 2.8, 2.8, 1.4), (-5.2, 3.0, 3.0, 2.6, 2.2, 2.4),
                               (3.0, 4.6, 1.8, 1.5, 1.3, 0.2)], pts=16, seed=9.0),
  "moss-topped granite boulder cluster")
A(W, "w2_fern_clump", "shrub", ["forest"], g_fern,
  dict(fronds=13, len=7.5, w=1.5, rise=5.0, segs=(3, 2, 1), roles=[("foliage", 0.88), ("foliage", 1.05), ("grass", 0.72),
                                                                  ("cloth_mustard", 0.7)]),
  "bracken clump with a few fronds turning", charred=True)

# ---------------------------------------------------------------- W3 Frostline
W = "w3_frost"
NEEDLE_W3 = [("foliage", 0.64), ("evergreen", 1.9), ("foliage", 0.78)]
A(W, "w3_pine_snow_a", "tree", ["snow", "forest"], g_conifer,
  dict(h=60, r=10.5, tiers=9, clear=0.06, needle=NEEDLE_W3, droop=0.24, snow=0.55, seed=11.0),
  "tall snow-laden spruce")
A(W, "w3_pine_snow_b", "tree", ["snow", "forest"], g_conifer,
  dict(h=50, r=11.5, tiers=7, clear=0.08, needle=NEEDLE_W3, droop=0.26, snow=0.7, taper=0.8, seed=12.0),
  "medium spruce, heavy snow")
A(W, "w3_pine_snow_c", "tree", ["snow", "forest", "rocky"], g_conifer,
  dict(h=41, r=9.0, tiers=6, n=12, clear=0.1, needle=NEEDLE_W3[:2], droop=0.28, snow=0.85, seed=13.0),
  "small snow-buried pine")
A(W, "w3_rock_snow_a", "rock", ["snow", "rocky"], g_rocks,
  dict(style="snowy", rocks=[(0, 0, 6.5, 5.0, 5.6, 0.3), (5.0, 2.8, 2.2, 1.9, 1.8, 1.2)], pts=18),
  "snow-capped boulder (12 mm)")
A(W, "w3_rock_snow_b", "rock", ["snow", "rocky"], g_rocks,
  dict(style="snowy", rocks=[(0, 0, 4.5, 3.0, 4.6, 1.0), (3.6, -1.2, 2.6, 2.2, 3.0, 2.2)], pts=14, jit=0.28, seed=3.0),
  "jagged rock shelf pair, snow on the ledges")
A(W, "w3_rock_snow_c", "rock", ["snow", "rocky"], g_rocks,
  dict(style="snowy", rocks=[(0, 0, 2.4, 2.0, 2.0, 0.0), (2.6, 1.4, 1.6, 1.4, 1.3, 1.0), (-1.8, 2.0, 1.4, 1.2, 1.1, 2.0)],
       pts=11, seed=5.0), "small snowy stones")
A(W, "w3_snow_drift_a", "ground_cover", ["snow"], g_drift, dict(len=12, h=4.2, d=6.0, seed=1.0),
  "wind-carved snow drift, steep lee side to +Y")
A(W, "w3_snow_drift_b", "ground_cover", ["snow"], g_drift, dict(len=9, h=3.2, d=5.0, seed=2.5), "small snow drift")
A(W, "w3_ice_a", "rock", ["snow", "near_water"], g_rocks,
  dict(style="ice", rocks=[(0, 0, 4.2, 2.6, 3.8, 0.5), (4.0, 2.0, 1.6, 1.2, 1.4, 1.4), (-3.6, -2.2, 1.4, 1.0, 1.2, 2.4)],
       pts=14, jit=0.3, squash=1.0, lift=0.55, natural=False,
       smooth=False),
  "tilted ice block (glossy glass-blue, frosted top)")
A(W, "w3_ice_b", "rock", ["snow", "near_water"], g_rocks,
  dict(style="ice", rocks=[(0, 0, 2.6, 1.6, 4.2, 0.0), (2.4, 1.2, 1.8, 1.3, 2.6, 1.2), (-2.0, 1.4, 1.4, 1.1, 1.8, 2.2)],
       pts=9, jit=0.32, squash=1.0, lift=0.6, seed=7.0, natural=False, smooth=False), "cluster of ice shards")
A(W, "w3_marker_pole", "prop", ["snow"], g_marker, dict(h=13.0),
  "snow marker pole: red/white bands, signal-yellow reflector, snow mound",
  place=dict(cluster="lines along routes at 30-40 mm spacing (decorative only)", min_spacing_mm=20))
A(W, "w3_snow_fence", "prop", ["snow"], g_fence,
  dict(len=40, h=11, rails=(0.3, 0.8), slats=12, snow=True, wood="cedar", tone=0.55),
  "slatted snow fence segment with snow on the rails and a drift at its foot; 40 mm pitch",
  place=dict(cluster="lines across slopes", yaw="align local X to the contour", tile_pitch_mm=40, min_spacing_mm=0))
A(W, "w3_larch_bare_a", "tree", ["snow", "forest", "rocky"], g_larch, dict(h=54, r=10.0, whorls=9, snow=0.35),
  "bare winter larch, drooping up-turned branches, snow in the forks")
A(W, "w3_larch_bare_b", "tree", ["snow", "rocky"], g_larch, dict(h=42, r=8.5, whorls=8, lean=2.5, snow=0.45, rt=0.9),
  "smaller leaning bare larch")

# ---------------------------------------------------------------- W4 Tidemark
W = "w4_tide"
A(W, "w4_pine_wind_a", "tree", ["beach", "rocky", "village"], g_pads,
  dict(h=46, r=10.0, rt=1.15, bark=("soil", 0.7), sweep=5.5,
       pads=[(0.72, 0.3, 0.55), (0.86, 0.35, 0.6), (0.96, 0.25, 0.45), (0.78, 0.65, 0.4)],
       leaf=[("foliage", 0.78), ("foliage", 0.9), ("grass", 0.74)], sq=0.42, big=4.5),
  "wind-swept coastal pine, crown pushed downwind (+X)",
  place=dict(yaw="rotate so local +X points away from the open sea (same for the whole board)"), charred=True)
A(W, "w4_pine_wind_b", "tree", ["beach", "rocky"], g_pads,
  dict(h=38, r=10.5, rt=1.2, bark=("soil", 0.66), sweep=6.5,
       pads=[(0.7, 0.25, 0.6), (0.82, 0.4, 0.62), (0.92, 0.55, 0.48)],
       leaf=[("foliage", 0.8), ("grass", 0.72)], sq=0.38, big=4.5),
  "low, strongly bent coastal pine",
  place=dict(yaw="rotate so local +X points away from the open sea (same for the whole board)"), charred=True)
A(W, "w4_dune_grass_a", "ground_cover", ["beach"], g_tuft,
  dict(blades=19, r=2.6, h=6.0, lean=0.25, wind=(0.35, 0.0), w=0.3, roles=[("cloth_sage", 0.92), ("wood_deck", 1.0),
                                                                              ("cloth_sage", 1.08)],
       mound=(3.4, 0.9, ("stone", 1.0))), "marram dune grass on a sand hummock, leaning +X",
  place=dict(yaw="local +X downwind, same for the board"))
A(W, "w4_dune_grass_b", "ground_cover", ["beach"], g_tuft,
  dict(blades=22, r=3.4, h=4.6, lean=0.4, wind=(0.25, 0.1), w=0.3, roles=[("wood_deck", 1.05), ("cloth_sage", 0.85)],
       seed=6.0), "sparse dune grass")
A(W, "w4_rock_weed_a", "rock", ["beach", "near_water", "rocky"], g_rocks,
  dict(style="algae", rocks=[(0, 0, 6.5, 5.0, 4.6, 0.4), (5.0, 2.6, 2.4, 2.0, 1.8, 1.0)], pts=18),
  "shore boulder with an algae/kelp foot and wet band",
  place=dict(edge="near_water: on the tide line or 0-6 mm into the water"))
A(W, "w4_rock_weed_b", "rock", ["beach", "near_water", "rocky"], g_rocks,
  dict(style="algae", rocks=[(0, 0, 4.0, 3.0, 2.6, 0.0), (3.6, 1.0, 2.6, 2.0, 2.0, 1.0), (-3.0, 1.6, 2.0, 1.8, 1.5, 2.0)],
       pts=13, seed=2.0), "low weedy rock group", place=dict(edge="near_water: tide line"))
A(W, "w4_rock_weed_c", "rock", ["beach", "near_water"], g_rocks,
  dict(style="algae", rocks=[(0, 0, 2.2, 1.8, 1.8, 0.0), (2.0, -1.4, 1.6, 1.3, 1.3, 1.0), (-1.6, 1.2, 1.3, 1.0, 1.0, 2.0)],
       pts=10, seed=3.0), "small weedy pebbles", place=dict(edge="near_water: tide line"))
A(W, "w4_mooring_post", "prop", ["beach", "near_water", "village"], g_mooring, dict(h=12.0),
  "timber mooring post with rope turns, iron band and algae foot",
  place=dict(edge="quay or dock edge; may stand in shallow water", min_spacing_mm=12))
A(W, "w4_buoy_can", "prop", ["near_water"], g_buoy, dict(kind="can"),
  "red can buoy with white band and signal-yellow top mark; pivot = waterline",
  place=dict(edge="on_water: set pivot Y to the water surface; hull extends 2.6 mm below", min_spacing_mm=15,
             yaw="random"), keep_below=True)
A(W, "w4_buoy_ball", "prop", ["near_water"], g_buoy, dict(kind="ball"),
  "yellow spherical mooring buoy with lifting ring; pivot = waterline",
  place=dict(edge="on_water: set pivot Y to the water surface; hull extends 2.2 mm below", min_spacing_mm=12),
  keep_below=True)
A(W, "w4_crate_single", "prop", ["village", "beach"], g_crates, dict(crates=[(0, 0, -0.5, 7.0, 7.0, 0.0, 1.0)]),
  "slatted fish crate (7 mm)")
A(W, "w4_crate_stack", "prop", ["village", "beach"], g_crates,
  dict(crates=[(-3.2, 0, -0.5, 6.0, 5.4, 0.08, 1.0), (3.0, 0.3, -0.5, 5.6, 5.4, -0.12, 0.92), (-2.6, 0.2, 4.9, 4.4, 4.0, 0.3, 1.06)]),
  "two crates with a small one on top (9.4 mm)")
ASSETS[-1]["P"]["detail"] = (2, 0, 0)
A(W, "w4_barrel_stack", "prop", ["village"], g_barrels, {}, "three standing barrels and one lying (6.5 mm)")
A(W, "w4_net_rack", "prop", ["village", "beach"], g_netrack, {}, "net-drying rack with green net and floats (24 x 12 mm)",
  place=dict(yaw="align local X to the quay"))
A(W, "w4_lobster_pots", "prop", ["village", "beach"], g_pots, {}, "stack of three lobster pots and a marker float")
A(W, "w4_driftwood", "prop", ["beach"], g_driftwood, {}, "bleached forked driftwood (20 mm)",
  place=dict(edge="beach, above the tide line"))

# ---------------------------------------------------------------- W5 Cinder Reach
W = "w5_cinder"
A(W, "w5_basalt_tall", "rock", ["basalt", "rocky"], g_basalt,
  dict(rc=2.5, cells=[(0, 0), (1, 0), (-1, 1), (0, 1), (1, -1), (0, -1), (-1, 0), (2, -1), (-2, 1), (1, 1), (-1, 2)],
       hfun=lambda x, y, h: 24 - 3.4 * math.hypot(x, y) ** 0.9 + 4 * (h - 0.5), seed=1.0),
  "tall basalt column cluster (24 mm), honeycomb tops", place=dict(min_spacing_mm=12))
A(W, "w5_basalt_steps", "rock", ["basalt", "rocky", "beach"], g_basalt,
  dict(rc=2.6, cells=[(q, r) for q in range(-2, 3) for r in range(-1, 2) if abs(q + r) <= 2],
       hfun=lambda x, y, h: 15 + 0.7 * x - 2.5 * h + (2.0 if h > 0.8 else 0.0), seed=2.0),
  "stepped basalt causeway cluster (16 mm)", place=dict(min_spacing_mm=12))
A(W, "w5_basalt_broken", "rock", ["basalt", "rocky"], g_basalt,
  dict(rc=2.4, cells=[(0, 0), (1, 0), (0, 1), (-1, 1), (1, -1), (-1, 0), (2, -1)],
       hfun=lambda x, y, h: 8 + 14 * h * (1 if (x > -1) else 0.5), fallen=[(-6.0, -6.5, 6.0, -8.5)], seed=3.0),
  "broken basalt columns with a fallen column (22 mm)", place=dict(min_spacing_mm=12))
A(W, "w5_lava_rock_a", "rock", ["basalt", "rocky", "burnt"], g_rocks,
  dict(style="lava", rocks=[(0, 0, 5.0, 4.0, 3.6, 0.3), (4.2, 2.0, 2.0, 1.7, 1.5, 1.0)], pts=20, jit=0.32),
  "jagged lava boulder with oxide patches")
A(W, "w5_lava_rock_b", "rock", ["basalt", "rocky"], g_rocks,
  dict(style="lava", rocks=[(0, 0, 3.0, 2.4, 1.8, 0.0), (2.8, 1.5, 2.0, 1.6, 1.4, 1.0), (-2.4, 1.6, 1.8, 1.5, 1.2, 2.0),
                            (0.6, -2.6, 1.4, 1.2, 1.0, 0.5)], pts=12, jit=0.35, seed=2.0), "lava clinker group")
A(W, "w5_lava_rock_c", "rock", ["basalt", "rocky"], g_rocks,
  dict(style="lava", rocks=[(0, 0, 6.0, 3.0, 2.0, 0.6)], pts=16, jit=0.38, squash=0.7, seed=3.0, lift=0.3),
  "flat ropey lava slab")
A(W, "w5_shrub_a", "shrub", ["rocky", "orchard", "basalt"], g_bush,
  dict(r=7.5, h=8.5, n=6, leaf=[("cloth_sage", 0.68), ("grass", 0.72), ("foliage", 1.0)], sq=0.7, seed=5.0),
  "low hardy maquis shrub", charred=True)
A(W, "w5_shrub_b", "shrub", ["rocky", "orchard"], g_bush,
  dict(r=6.0, h=10, n=5, leaf=[("grass", 0.72), ("foliage", 1.0), ("cloth_sage", 0.72)], dots=[(("cloth_mustard", 1.0), 8, 0.5)],
       seed=6.0),
  "gorse-like shrub with yellow flowers", charred=True)
LEAF_OLIVE = [("cloth_sage", 0.7), ("grass", 0.74), ("cloth_sage", 0.8), ("cloth_sage", 0.88)]
A(W, "w5_olive_a", "tree", ["orchard"], g_decid,
  dict(h=33, r=13.0, trunk=0.36, rt=1.3, stems=[(-0.9, 0.0, -2.2, 0.6), (0.9, 0.3, 2.4, -0.4)], twist=1.4,
       bark=("mineral", 0.46), layers=[(0.6, 0.6, 6, 0.4), (0.82, 0.36, 3, 0.4)], leaf=LEAF_OLIVE, sq=0.66,
       core_z=0.66, dots=[(("hair_dark", 1.0), 6, 0.35)]),
  "gnarled twin-trunk olive, silvery crown with dark olives",
  place=dict(cluster="orchard rows at 20 mm (one per cell) or 2-3 per terrace", min_spacing_mm=18), charred=True)
A(W, "w5_olive_b", "tree", ["orchard"], g_decid,
  dict(h=29, r=11.5, trunk=0.4, rt=1.4, stems=[(0, 0, 1.2, 0.8)], twist=1.0, bark=("mineral", 0.44),
       layers=[(0.66, 0.55, 5, 0.42), (0.88, 0.2, 2, 0.38)], leaf=LEAF_OLIVE[1:], sq=0.7, lean=(1.5, 0.0)),
  "young olive", place=dict(cluster="orchard rows at 20 mm (one per cell)", min_spacing_mm=18), charred=True)
A(W, "w5_grass_a", "ground_cover", ["rocky", "orchard", "basalt"], g_tuft,
  dict(blades=18, r=2.0, h=5.6, lean=0.18, w=0.32, roles=[("cloth_sage", 0.85), ("wood_deck", 0.95), ("grass", 0.8)]),
  "spiky upright tough grass")
A(W, "w5_grass_b", "ground_cover", ["rocky", "basalt", "burnt"], g_tuft,
  dict(blades=16, r=2.8, h=4.0, lean=0.5, w=0.32, roles=[("wood_deck", 0.9), ("cloth_mustard", 0.75)], seed=7.0),
  "dry tough grass")
A(W, "w5_steam_vent", "rock", ["basalt", "rocky"], g_vent, {},
  "fumarole cone (20 mm wide, 7 mm) with sulphur-crusted crater; marker _mk_steam for the steam VFX",
  place=dict(cluster="single, 1-2 per board", min_spacing_mm=30))
A(W, "w5_ash_drift_a", "ground_cover", ["basalt", "burnt"], g_drift,
  dict(len=12, h=4.0, d=6.0, role=("mineral", 0.6), seed=3.0), "wind-shaped ash drift")
A(W, "w5_ash_drift_b", "ground_cover", ["basalt", "burnt"], g_drift,
  dict(len=9, h=3.0, d=4.6, role=("stone", 0.58), seed=4.5), "small brown-grey ash drift")
A(W, "w5_pumice_boulders", "rock", ["basalt", "beach", "rocky"], g_rocks,
  dict(style="pumice", rocks=[(0, 0, 5.4, 4.6, 4.2, 0.2), (5.2, 2.0, 3.0, 2.6, 2.4, 1.0), (-3.6, 3.6, 2.4, 2.0, 1.8, 2.0)],
       pts=22, jit=0.12, smooth=True), "rounded pale pumice boulders (11 mm)")

# ---------------------------------------------------------------- backdrops
BACK_DESIGN = {
    "w1_alder": "rolling wooded hills: patchwork fields in front, wooded mid hills, hazy sage far hills",
    "w2_ember": "forested ridge: dense dark pine cones with ochre/russet accents, blue-grey far ridge",
    "w3_frost": "snowy mountain range: snowy foothills with dark pines, jagged rock-and-snow peaks",
    "w4_tide": "coastline: sea strip with surf line, sand, layered cliffs with grass tops, hazy headlands",
    "w5_cinder": "volcano flank: lava field with glowing cracks, ash-capped flank, lava channel / summit crater glow",
}
BACK_VAR = {
    "w4_tide": {"a": "cove with a wide beach", "b": "high cliffs with three sea stacks", "c": "low dune coast"},
    "w5_cinder": {"a": "flank with a lava channel", "b": "summit crater with lava-lake glow and a lava channel",
                  "c": "ash-capped flank"},
}
for W in WORLDS:
    for var in ("a", "b", "c"):
        A(W, "%s_backdrop_%s" % (W.split("_")[0], var), "backdrop", ["backdrop"], g_backdrop,
          dict(world=W, variant=var), BACK_DESIGN[W] + (" (%s)" % BACK_VAR[W][var] if W in BACK_VAR else
                                                       " (variant %s)" % var),
          place=dict(tile_pitch_mm=320, edge_profile="identical at x = +-160 for every variant of this world"))


# ==========================================================================
# Build, check, publish
# ==========================================================================
def seed_of(aid):
    return zlib.crc32(aid.encode()) & 0xFFFFFFFF


def remove_ob(ob):
    me = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    if me is not None and me.users == 0:
        bpy.data.meshes.remove(me)


def make_lod(spec, lod, q, mat):
    rng = random.Random(seed_of(spec["id"]))
    b = NB(spec["id"])
    spec["gen"](b, lod, q, rng, spec["P"])
    culled_in = cull_inside(b, keep_below=spec["keep_below"])
    ao = dict(AO_GEO[spec["category"]])
    if spec["keep_below"]:
        ao["use_ground"] = False
    ob = b.finalize("%s_LOD%d" % (spec["id"], lod), mat, ao=ao,
                    cull=(dict(gap_mm=0.3, use_ground=False) if spec["category"] != "backdrop" else False))
    ob["resq_culled_inside"] = culled_in
    vertex_tone_jitter(ob, VJIT[spec["category"]])
    return ob, b.markers


VJIT = {"tree": 0.09, "shrub": 0.08, "rock": 0.12, "ground_cover": 0.07, "prop": 0.06, "backdrop": 0.05}
AO_GAMMA = {"tree": 1.15, "shrub": 1.15, "rock": 1.45, "ground_cover": 1.1, "prop": 1.2, "backdrop": 1.1}


def vertex_tone_jitter(ob, amt):
    """Per-vertex albedo variation: every corner of a vertex gets the same scalar
    tone factor (hash of its position), so the palette x tone contract holds and
    neighbouring faces of one role blend smoothly."""
    import array as _a
    me = ob.data
    col = me.color_attributes.get("Col")
    if col is None or amt <= 0:
        return
    n = len(me.loops)
    buf = _a.array("f", [0.0]) * (n * 4)
    col.data.foreach_get("color", buf)
    vi = _a.array("i", [0]) * n
    me.loops.foreach_get("vertex_index", vi)
    fac = {}
    for v in me.vertices:
        c = v.co / MM
        f = 1 + amt * (2 * fbm(c.x * 0.9 + c.z * 0.37, c.y * 0.9 - c.z * 0.23, 57) - 1) * 1.4
        f *= 1 + amt * 0.5 * (2 * hsh(c.x * 3.1, c.y * 2.3, c.z * 1.7) - 1)
        fac[v.index] = max(0.0, f) ** 2.2          # scalar tone applied in sRGB -> linear
    for i in range(n):
        f = fac[vi[i]]
        for k in range(3):
            buf[i * 4 + k] = min(1.0, buf[i * 4 + k] * f)
    col.data.foreach_set("color", buf)


def ao_contrast(ob, gamma):
    import array as _a
    col = ob.data.color_attributes.get("Col")
    if col is None or gamma == 1.0:
        return
    n = len(col.data)
    buf = _a.array("f", [0.0]) * (n * 4)
    col.data.foreach_get("color", buf)
    for i in range(n):
        buf[i * 4 + 3] = max(0.0, min(1.0, buf[i * 4 + 3])) ** gamma
    col.data.foreach_set("color", buf)


def build_lods(spec, mat):
    budget = BUDGET[spec["category"]]
    notes = []
    q = 1.0
    for it in range(10):
        ob0, markers = make_lod(spec, 0, q, mat)
        t0 = kit.tri_count(ob0)
        if t0 <= budget or it == 9:
            break
        notes.append("LOD0 at density %.2f had %d tris (> %d); density lowered" % (q, t0, budget))
        remove_ob(ob0)
        q *= 0.85
    lods = [ob0]
    for lod, lim in ((1, 0.6), (2, 0.3)):
        ql = q * LOD_START[lod]
        for it in range(14):
            ob, _m = make_lod(spec, lod, ql, mat)
            t = kit.tri_count(ob)
            if t <= lim * t0:
                break
            if it == 13:
                notes.append("LOD%d could not reach %.0f%% semantically (%d tris)" % (lod, lim * 100, t))
                break
            remove_ob(ob)
            ql *= 0.82
        lods.append(ob)
    return lods, markers, q, notes


def islands_supported(ob, ground=0.05, keep_below=False):
    """Every connected piece must reach the ground (z <= ground mm) or touch a
    supported piece. Returns (ok, n_islands, n_floating)."""
    me = ob.data
    nv = len(me.vertices)
    parent = list(range(nv))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for e in me.edges:
        a, c = find(e.vertices[0]), find(e.vertices[1])
        if a != c:
            parent[a] = c
    me.calc_loop_triangles()
    isl = {}
    for t in me.loop_triangles:
        isl.setdefault(find(t.vertices[0]), []).append(tuple(t.vertices))
    keys = list(isl)
    co = [v.co.copy() for v in me.vertices]
    data = {}
    for k in keys:
        vs = sorted({i for tri in isl[k] for i in tri})
        idx = {v: i for i, v in enumerate(vs)}
        tree = BVHTree.FromPolygons([co[v] for v in vs], [[idx[i] for i in tri] for tri in isl[k]])
        minz = min(co[v].z for v in vs) / MM
        data[k] = (tree, minz, vs)
    sup = {k for k in keys if data[k][1] <= ground or keep_below}
    changed = True
    while changed:
        changed = False
        for k in keys:
            if k in sup:
                continue
            tree, _, vs = data[k]
            for s in list(sup):
                ts = data[s][0]
                hit = bool(tree.overlap(ts))
                if not hit:
                    step = max(1, len(vs) // 40)
                    for v in vs[::step]:
                        loc, _n, _i, d = ts.find_nearest(co[v])
                        if loc is not None and d <= 0.25 * MM:
                            hit = True
                            break
                if hit:
                    sup.add(k)
                    changed = True
                    break
    for k in keys:
        if k not in sup:
            vs = data[k][2]
            ctr = [round(sum(co[v][i] for v in vs) / len(vs) / MM, 1) for i in range(3)]
            print("FLOATING ISLAND", ob.name, len(vs), "verts, centre mm", ctr, "min z", round(data[k][1], 2))
    return len(sup) == len(keys), len(keys), len(keys) - len(sup)


def asset_metrics(ob):
    xs = [v.co for v in ob.data.vertices]
    fr = max(math.hypot(v.x, v.y) for v in xs) / MM
    return round(fr, 2), round(max(v.z for v in xs) / MM, 2), round(min(v.z for v in xs) / MM, 2)


def publish_asset(spec, mat, folder, bake=True):
    t_start = time.time()
    root = kit.empty(spec["id"], (0, 0, 0), size_mm=5)
    lods, markers, q, notes = build_lods(spec, mat)
    for o in lods:
        o.parent = root
    mk = []
    for nm, p, meaning in markers:
        e = kit.empty("%s_mk_%s" % (spec["id"], nm), p, root, size_mm=2)
        e["meaning"] = meaning
        mk.append(e)
    ao_method = {}
    if bake:
        import array as _a
        for o in lods:
            col = o.data.color_attributes["Col"]
            n = len(col.data)
            geo = _a.array("f", [0.0]) * (n * 4)
            col.data.foreach_get("color", geo)
            try:
                ok = kit.bake_ao_cycles(o, distance_mm=BAKE_DIST[spec["category"]], samples=16,
                                        ground_z_mm=None if spec["keep_below"] else 0.0)
            except Exception as ex:   # keep the geometric bake already in alpha
                print("CYCLES BAKE FAILED", o.name, ex)
                ok = False
            if ok:
                # Cycles samples AO at the exact corner position; where kit parts interpenetrate that corner
                # is inside another part and bakes black. Floor it at the geometric AO (sampled 20% in from
                # the corner) minus 0.25 so contact shading stays but buried corners do not go black.
                buf = _a.array("f", [0.0]) * (n * 4)
                col.data.foreach_get("color", buf)
                for i in range(n):
                    buf[i * 4 + 3] = max(buf[i * 4 + 3], geo[i * 4 + 3] - 0.25)
                col.data.foreach_set("color", buf)
            ao_method[o.name] = ("cycles_ao_16spp floored at geometric-0.25" if ok else "geometric_raycast_fallback")
    else:
        ao_method = {o.name: "geometric_raycast (--no-bake)" for o in lods}
    for o in lods:
        ao_contrast(o, AO_GAMMA[spec["category"]])
    ao_method["contrast_gamma"] = AO_GAMMA[spec["category"]]
    bpy.context.view_layer.update()
    tris = [kit.tri_count(o) for o in lods]
    bnd = kit.bounds_mm([lods[0]])
    fr, hmax, zmin = asset_metrics(lods[0])
    budget = BUDGET[spec["category"]]
    sup = [islands_supported(o, keep_below=spec["keep_below"]) for o in lods]
    checks = {"lod0_in_budget": tris[0] <= budget,
              "lod1_ratio": round(tris[1] / tris[0], 3), "lod1_le_60pct": tris[1] <= 0.6 * tris[0],
              "lod2_ratio": round(tris[2] / tris[0], 3), "lod2_le_30pct": tris[2] <= 0.3 * tris[0],
              "no_floating_parts": all(s[0] for s in sup),
              "grounded_base_sunk": (-1.0 <= zmin <= 0.0) if not spec["keep_below"] else zmin < 0.0}
    if spec["category"] == "backdrop":
        sz = bnd["size"]
        checks["backdrop_envelope_320x120x160"] = (abs(sz[0] - 320) < 0.01 and sz[1] <= 120.01 and bnd["max"][2] <= 160.0)
    for o in lods[1:]:
        o.hide_set(True)
    doc = {
        "schema": "resq-nature-asset-1", "asset": spec["id"], "world": spec["world"], "category": spec["category"],
        "tags": spec["tags"], "kit_version": kit.KIT_VERSION,
        "origin": "original procedural geometry authored in tools/blender/nature.py (no downloads, no paid generation)",
        "evidence_kind": "automated (Blender headless); Unity import, device and human review pending",
        "units": "metres in files; values below in millimetres", "blender_axes": "Z up, asset forward -Y",
        "pivot": ("waterline centre: z = 0 is the water surface; the hull extends below" if spec["keep_below"] else
                  "ground centre: root at the footprint centre, z = 0 on the support surface, base sunk ~0.5 mm"),
        "bounds_lod0_blender_mm": bnd,
        "bounds_lod0_unity_mm_size": [bnd["size"][0], bnd["size"][2], bnd["size"][1]],
        "footprint_radius_mm": fr, "height_mm": hmax,
        "triangles": {o.name: t for o, t in zip(lods, tris)}, "budget_lod0": budget,
        "material": "RESQ_Shared (one material). Vertex colour 'Col': RGB = albedo (sRGB in FBX), A = baked AO; "
                    "UV = palette-atlas cell (atlas alpha = smoothness)",
        "mean_ao_alpha": {o.name: kit.mean_alpha(o) for o in lods},
        "colliders": {}, "material_roles_faces_lod0": kit.role_counts(lods[0]),
        "markers": {e.name: {"blender_mm": [round(c / MM, 3) for c in e.matrix_world.translation],
                             "unity_mm": kit.blender_to_unity([round(c / MM, 3) for c in e.matrix_world.translation]),
                             "meaning": e.get("meaning", "")} for e in mk},
        "lod_method": "semantic rebuild per LOD (fewer clumps/blades/segments) with an automatic density search; "
                      "no decimation", "lod_density": round(q, 3), "lod_notes": notes,
        "islands": {o.name: {"count": s[1], "floating": s[2]} for o, s in zip(lods, sup)},
        "hidden_faces_culled": {o.name: {"inside_other_parts_or_below_ground": o.get("resq_culled_inside"),
                                         "covered_contact": o.get("resq_culled")} for o in lods},
        "ao_bake": ao_method, "design": spec["design"], "placement": spec["place"], "checks": checks,
    }
    fbx = os.path.join(folder, spec["id"] + ".fbx")
    doc["fbx_export_settings"] = kit.export_fbx(fbx, [root] + lods + mk)
    doc["fbx_file_inspection"] = kit.fbx_inspect(fbx)
    doc["files"] = {"fbx": os.path.relpath(fbx, REPO), "fbx_sha256": kit.sha256_file(fbx), "fbx_bytes": os.path.getsize(fbx)}
    doc["generator_sha256"] = {"nature.py": kit.sha256_file(__file__), "kit.py": kit.sha256_file(kit.__file__)}
    doc["build_seconds"] = round(time.time() - t_start, 2)
    print("BUILT %-28s tris %s  checks %s  %.1fs" % (spec["id"], tris, {k: v for k, v in checks.items()
                                                                           if v is False}, doc["build_seconds"]))
    return root, lods, mk, doc


def roundtrip(fbx_path, names):
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    st = bpy.ops.import_scene.fbx(filepath=fbx_path, use_manual_orientation=False, global_scale=1.0, use_anim=False,
                                  colors_type="SRGB")
    if st != {"FINISHED"}:
        raise RuntimeError("re-import failed " + fbx_path)
    bpy.context.view_layer.update()
    got = {n: (kit.bounds_mm([bpy.data.objects[n]]) if n in bpy.data.objects else None) for n in names}
    alphas = {n: kit.mean_alpha(bpy.data.objects[n]) for n in names if n in bpy.data.objects}
    cols = {ob.name: [a.name for a in ob.data.color_attributes] for ob in bpy.data.objects if ob.type == "MESH"}
    return {"lod_bounds_mm": got, "mean_ao_alpha": alphas, "colour_attrs": cols}


# ==========================================================================
# Preview sheets
# ==========================================================================
def _text_mat():
    m = bpy.data.materials.get("pv_label")
    if m is None:
        m = bpy.data.materials.new("pv_label")
        m.use_nodes = True
        bs = m.node_tree.nodes.get("Principled BSDF")
        bs.inputs["Base Color"].default_value = (0.05, 0.07, 0.07, 1.0)
        bs.inputs["Roughness"].default_value = 1.0
    return m


def render_panel(groups, path, width_px=2000, el=34.0, az=0.0, label=True, max_h_px=1100, min_h_px=320):
    """groups: [(label, [objs])]; objs of a group sit side by side. Shelf-packed,
    tallest rows at the back. Objects are moved temporarily and restored."""
    sc = bpy.context.scene
    objs = [o for _, g in groups for o in g]
    saved = {o: o.location.copy() for o in objs}
    hid = {o: (o.hide_get(), o.hide_render) for o in sc.objects}
    temp = []
    try:
        for o in sc.objects:
            o.hide_render = True
        info = []
        for lab, g in groups:
            bbs = []
            for o in g:
                o.hide_set(False)
                o.hide_render = False
            bpy.context.view_layer.update()
            for o in g:
                bbs.append(kit.bounds_mm([o]))
            gap_in = max(2.0, 0.15 * max(bb["size"][0] for bb in bbs))
            w = sum(bb["size"][0] for bb in bbs) + gap_in * (len(g) - 1)
            d = max(bb["size"][1] for bb in bbs)
            h = max(bb["max"][2] for bb in bbs)
            info.append([lab, g, bbs, w, d, h, gap_in])
        tot = sum(i[3] * max(i[4], 8) for i in info)
        row_w = max(max(i[3] for i in info), math.sqrt(tot) * 2.4)
        order = sorted(info, key=lambda i: -i[5])
        rows, cur, cw = [], [], 0.0
        for it in order:
            gap = max(4.0, 0.25 * it[3])
            if cur and cw + it[3] > row_w:
                rows.append(cur)
                cur, cw = [], 0.0
            cur.append(it)
            cw += it[3] + gap
        if cur:
            rows.append(cur)
        cot = 1.0 / math.tan(math.radians(el))
        y = 0.0
        label_px = 24
        ppm_guess = width_px / (row_w * 1.1)
        lsize = label_px / ppm_guess
        placed = []
        for ri, row in enumerate(rows):
            d = max(it[4] for it in row)
            h = max(it[5] for it in row)
            if ri > 0:
                y -= (prev_d / 2 + d / 2 + 4 + h * cot * 0.55 + (lsize * 2.2 if label else 0))
            x = -sum(it[3] for it in row) / 2 - 2.0 * (len(row) - 1)
            for it in row:
                lab, g, bbs, w, dd, hh, gin = it
                gx = x
                for o, bb in zip(g, bbs):
                    o.location.x += gx - bb["min"][0] * MM / MM * 1.0 if False else 0.0
                    o.location.x += (gx - bb["min"][0]) * MM
                    o.location.y += (y - (bb["min"][1] + bb["max"][1]) / 2) * MM
                    gx += bb["size"][0] + gin
                placed.append((lab, x + w / 2, y - d / 2 - lsize * 1.3))
                x += w + max(4.0, 0.25 * w)
            prev_d = d
        bpy.context.view_layer.update()
        if label:
            mat = _text_mat()
            for lab, lx, ly in placed:
                cu = bpy.data.curves.new("pv_lab", "FONT")
                cu.body = lab
                cu.size = lsize * MM
                cu.align_x = "CENTER"
                tob = bpy.data.objects.new("pv_lab", cu)
                tob.location = (lx * MM, ly * MM, 0.05 * MM)
                cu.materials.append(mat)
                sc.collection.objects.link(tob)
                temp.append(tob)
        allb = kit.bounds_mm(objs)
        gm = bpy.data.meshes.new("pv_ground")
        s = 3.0
        gm.from_pydata([(-s, -s, -0.0003), (s, -s, -0.0003), (s, s, -0.0003), (-s, s, -0.0003)], [], [(0, 1, 2, 3)])
        pm = bpy.data.materials.get("pv_paper") or bpy.data.materials.new("pv_paper")
        pm.use_nodes = True
        pb = pm.node_tree.nodes.get("Principled BSDF")
        pb.inputs["Base Color"].default_value = (0.70, 0.66, 0.58, 1.0)
        pb.inputs["Roughness"].default_value = 1.0
        gm.materials.append(pm)
        g = bpy.data.objects.new("pv_ground", gm)
        sc.collection.objects.link(g)
        temp.append(g)
        sun = kit.add_lookdev_lights(sc)
        temp.append(sun)
        a, e = math.radians(az), math.radians(el)
        dvec = V((-math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        fwd = -dvec
        right = fwd.cross(V(0, 0, 1)).normalized()
        up = right.cross(fwd).normalized()
        pts = []
        lo, hi = allb["min"], allb["max"]
        for xx in (lo[0], hi[0]):
            for yy in (min(lo[1], min(p[2] for p in placed) - lsize), hi[1]):
                for zz in (0.0, hi[2]):
                    pts.append(V((xx, yy, zz)) * MM)
        us = [p.dot(right) for p in pts]
        vs = [p.dot(up) for p in pts]
        fs = [p.dot(fwd) for p in pts]
        wext, hext = (max(us) - min(us)) * 1.04, (max(vs) - min(vs)) * 1.06
        h_px = int(max(min_h_px, min(max_h_px, width_px * hext / wext)))
        cd = bpy.data.cameras.new("pv_cam")
        cd.type = "ORTHO"
        cam = bpy.data.objects.new("pv_cam", cd)
        sc.collection.objects.link(cam)
        temp.append(cam)
        cu_, cv_ = (max(us) + min(us)) / 2, (max(vs) + min(vs)) / 2
        cam.location = right * cu_ + up * cv_ + fwd * (min(fs) - 1.0)
        cam.rotation_euler = fwd.to_track_quat("-Z", "Y").to_euler()
        cd.clip_start, cd.clip_end = 0.01, 10.0
        cd.ortho_scale = max(wext, hext * width_px / h_px)
        sc.camera = cam
        kit.setup_eevee(sc)
        sc.render.resolution_x, sc.render.resolution_y = width_px, h_px
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
    finally:
        for o, l in saved.items():
            o.location = l
        for o in temp:
            bpy.data.objects.remove(o, do_unlink=True)
        for o, (hv, hr) in hid.items():
            if o.name in bpy.data.objects:
                o.hide_set(hv)
                o.hide_render = hr
    return path


def stack_pngs(paths, out):
    import numpy as np
    imgs = []
    for p in paths:
        im = bpy.data.images.load(p)
        w, h = im.size
        a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        imgs.append(a)
        bpy.data.images.remove(im)
    W = max(a.shape[1] for a in imgs)
    sep = np.ones((6, W, 4), dtype=np.float32) * np.array([0.08, 0.17, 0.17, 1.0], dtype=np.float32)
    parts = []
    for a in reversed(imgs):    # pixels are bottom-up: last panel first
        if a.shape[1] < W:
            pad = np.ones((a.shape[0], W - a.shape[1], 4), dtype=np.float32)
            a = np.concatenate([a, pad], axis=1)
        parts += [a, sep]
    full = np.concatenate(parts[:-1], axis=0)
    H = full.shape[0]
    im = bpy.data.images.new("sheet", W, H, alpha=False)
    im.pixels.foreach_set(full.ravel())
    im.filepath_raw = out
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)
    return out


def world_previews(world, built, folder):
    tmp = tempfile.mkdtemp(prefix="resq_nature_")
    small = [(s["id"].split("_", 1)[1], [lods[0]]) for s, root, lods in built
             if s["category"] in ("ground_cover", "rock", "prop", "shrub") and kit.bounds_mm([lods[0]])["max"][2] < 16]
    big = [(s["id"].split("_", 1)[1], [lods[0]]) for s, root, lods in built
           if s["category"] != "backdrop" and (s["id"].split("_", 1)[1], [lods[0]]) not in small
           and kit.bounds_mm([lods[0]])["max"][2] >= 16 or (s["category"] == "tree")]
    small_ids = {g[0] for g in small}
    big = [g for g in big if g[0] not in small_ids]
    backs = [lods[0] for s, root, lods in built if s["category"] == "backdrop"]
    panels = []
    if big:
        panels.append(render_panel(big, os.path.join(tmp, "big.png")))
    if small:
        panels.append(render_panel(small, os.path.join(tmp, "small.png")))
    temp_objs = []
    if backs:
        strip = list(backs)
        if len(backs) >= 2:      # repeat the first piece to show the last->first seam too
            cp = backs[0].copy()
            cp.name = backs[0].name + "_seamcopy"
            bpy.context.scene.collection.objects.link(cp)
            strip.append(cp)
            temp_objs.append(cp)
        panels.append(render_panel([("backdrop " + "+".join(o.name.split("_")[2] for o in strip), strip)],
                                   os.path.join(tmp, "back.png"), el=14.0, label=True, max_h_px=700))
    out = stack_pngs(panels, os.path.join(folder, world + "_preview.png"))
    # LOD sheet
    lods_groups = [(s["id"].split("_", 1)[1] + " L0/1/2", list(lods)) for s, root, lods in built
                   if s["category"] != "backdrop"]
    lp = []
    tall = [g for g in lods_groups if kit.bounds_mm([g[1][0]])["max"][2] >= 16]
    short = [g for g in lods_groups if g not in tall]
    for i, grp in enumerate((tall, short)):
        if grp:
            lp.append(render_panel(grp, os.path.join(tmp, "lods%d.png" % i)))
    bl = [(s["id"].split("_", 1)[1] + " L0/L1/L2", list(lods)) for s, root, lods in built if s["category"] == "backdrop"][:1]
    if bl:
        lp.append(render_panel(bl, os.path.join(tmp, "lodsb.png"), el=14.0, max_h_px=600))
    lout = stack_pngs(lp, os.path.join(folder, world + "_lods.png")) if lp else None
    for o in temp_objs:
        bpy.data.objects.remove(o, do_unlink=True)
    for f in os.listdir(tmp):
        os.remove(os.path.join(tmp, f))
    os.rmdir(tmp)
    return out, lout


# ==========================================================================
# Main
# ==========================================================================
def build_world(world, only, bake, preview, manifest):
    folder = os.path.join(NAT, world)
    os.makedirs(folder, exist_ok=True)
    specs = [s for s in ASSETS if s["world"] == world and (only is None or s["id"] in only)]
    if not specs:
        return
    kit.reset_scene()
    mat = kit.shared_material(ATLAS)
    built, docs = [], {}
    for spec in specs:
        coll = bpy.data.collections.new(spec["id"])
        bpy.context.scene.collection.children.link(coll)
        try:
            root, lods, mk, doc = publish_asset(spec, mat, folder, bake)
        except Exception as ex:
            import traceback
            traceback.print_exc()
            manifest["skipped"][spec["id"]] = "build failed: %s" % ex
            continue
        for o in [root] + lods + mk:
            for c in o.users_collection:
                c.objects.unlink(o)
            coll.objects.link(o)
        built.append((spec, root, lods))
        docs[spec["id"]] = (doc, lods)
    pv = (None, None)
    if preview and (only is None or FORCE_PREVIEW[0]):
        pv = world_previews(world, built, folder)
    blend = os.path.join(folder, world + "_kit.blend")
    if only is None:
        kit.save_blend(blend)
    for aid, (doc, lods) in docs.items():
        names = list(doc["triangles"].keys())
        fbx = os.path.join(REPO, doc["files"]["fbx"])
        rt = roundtrip(fbx, names)
        doc["blender_reimport"] = rt
        b0 = rt["lod_bounds_mm"].get(names[0])
        doc["checks"]["reimport_bounds_match_0p01mm"] = bool(b0) and max(
            abs(a - c) for a, c in zip(b0["size"], doc["bounds_lod0_blender_mm"]["size"])) < 0.01
        if os.path.exists(blend):
            doc["files"]["blend"] = os.path.relpath(blend, REPO)
        if pv[0]:
            doc["files"]["world_preview"] = os.path.relpath(pv[0], REPO)
        kit.write_json(os.path.join(folder, aid + ".json"), doc)
        spec = next(s for s in ASSETS if s["id"] == aid)
        manifest["assets"][aid] = {
            "id": aid, "world": world, "category": doc["category"], "tags": doc["tags"],
            "footprint_radius_mm": doc["footprint_radius_mm"], "height_mm": doc["height_mm"],
            "bounds_lod0_blender_mm": doc["bounds_lod0_blender_mm"]["size"],
            "triangles": {"LOD0": list(doc["triangles"].values())[0], "LOD1": list(doc["triangles"].values())[1],
                          "LOD2": list(doc["triangles"].values())[2]},
            "budget_lod0": doc["budget_lod0"], "checks_passed": all(v for k, v in doc["checks"].items()
                                                                    if isinstance(v, bool)),
            "failed_checks": [k for k, v in doc["checks"].items() if v is False],
            "placement": doc["placement"], "markers": list(doc["markers"].keys()),
            "fbx": doc["files"]["fbx"], "sidecar": os.path.relpath(os.path.join(folder, aid + ".json"), REPO),
            "design": doc["design"]}
        manifest["skipped"].pop(aid, None)
    if pv[0]:
        manifest["previews"][world] = {"lod0_sheet": os.path.relpath(pv[0], REPO),
                                       "lod_sheet": os.path.relpath(pv[1], REPO) if pv[1] else None,
                                       "blend": os.path.relpath(blend, REPO)}


def main(argv):
    if "--list" in argv:
        for s in ASSETS:
            print("%-10s %-30s %-13s %s" % (s["world"], s["id"], s["category"], ",".join(s["tags"])))
        print(len(ASSETS), "assets")
        return
    os.makedirs(NAT, exist_ok=True)
    kit.write_palette_atlas(ATLAS, os.path.join(SHARED, "palette_atlas.json"))
    worlds = list(WORLDS)
    if "--world" in argv:
        worlds = argv[argv.index("--world") + 1].split(",")
    only = None
    if "--only" in argv:
        only = set(argv[argv.index("--only") + 1].split(","))
    bake = "--no-bake" not in argv
    FORCE_PREVIEW[0] = "--preview" in argv
    preview = "--no-preview" not in argv
    mpath = os.path.join(NAT, "manifest.json")
    manifest = {"assets": {}, "skipped": {}, "previews": {}}
    if os.path.exists(mpath):
        with open(mpath) as f:
            old = json.load(f)
        manifest["assets"] = {a["id"]: a for a in old.get("assets", [])}
        manifest["skipped"] = old.get("skipped", {})
        manifest["previews"] = old.get("previews", {})
    t0 = time.time()
    for w in worlds:
        build_world(w, only, bake, preview, manifest)
    order = {s["id"]: i for i, s in enumerate(ASSETS)}
    doc = {
        "schema": "resq-nature-manifest-1", "kit_version": kit.KIT_VERSION, "generator": "tools/blender/nature.py",
        "evidence_kind": "automated (Blender headless); Unity import, device and human review pending",
        "units": "millimetres (board scale; cells 20 mm, residents 34 mm)",
        "axes": "Blender Z up / forward -Y; FBX preset maps Blender (x, y, z) -> Unity (-x, z, -y)",
        "budgets_lod0_tris": BUDGET, "lod_rule": "LOD1 <= 60% and LOD2 <= 30% of LOD0",
        "material": "RESQ_Shared; vertex colour RGB albedo (sRGB) + A AO; UV = palette-atlas cell",
        "palette_roles_added": {"snow": {"index": kit.ROLE_INDEX["snow"], "hex_srgb": "#E8F1EF",
                                         "why": "W3 palette snow; no existing role reaches a cool white with a scalar tone"}},
        "palette_note": "The 256x16 atlas holds 32 roles of 8 px; 31 are now used (one slot left). Other world colours "
                        "come from existing roles x tone (e.g. basalt = metal_dark x0.95, lava glow = roof_tile x1.2, "
                        "W3 rock = cloth_slate x1.3, ice = window_glass x2.3, sand = stone, ochre = cloth_mustard). "
                        "Rescue orange is never used in nature assets.",
        "tag_vocabulary": sorted({t for s in ASSETS for t in s["tags"]}),
        "unity_lod_note": "ResqArtImporter's LODGroup screen heights (0.12/0.05/0.015) were tuned for props; small "
                          "ground cover will reach LOD2/cull early at 0.6 m, which is intended for decoration.",
        "assets": sorted(manifest["assets"].values(), key=lambda a: order.get(a["id"], 9999)),
        "skipped": manifest["skipped"], "previews": manifest["previews"],
        "totals": {"assets": len(manifest["assets"]),
                   "by_world": {w: sum(1 for a in manifest["assets"].values() if a["world"] == w) for w in WORLDS}},
        "build_seconds_last_run": round(time.time() - t0, 1),
    }
    kit.write_json(mpath, doc)
    print("MANIFEST", mpath, doc["totals"])


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(argv)
