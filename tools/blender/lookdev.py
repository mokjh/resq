"""RESQ look-development quartet: cottage, bridge, skiff, resident (+variants).

Run (one Blender process at a time):
  blender -b --factory-startup --python-exit-code 1 --python tools/blender/lookdev.py -- --assets
  blender -b --factory-startup --python-exit-code 1 --python tools/blender/lookdev.py -- --render
Options: --assets [--only cottage,bridge,skiff,resident] | --calibrate | --render
All geometry is original and procedural; dimensions in millimetres.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kit  # noqa: E402
from kit import MM, V  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))
ART = os.path.join(REPO, "art")
LOOK = os.path.join(ART, "lookdev")
SHARED = os.path.join(ART, "shared")
ATLAS = os.path.join(SHARED, "palette_atlas.png")

TARGETS = {  # mm, from specification/05 section 2.1 (+-1%)
    "cottage": (80.0, 60.0, 70.0),
    "bridge": (120.0, 40.0, 20.0),
    "skiff": (80.0, 35.0, 25.0),
}
BUDGET = {"cottage": (1000, 2500), "bridge": (800, 2500), "skiff": (1500, 3500),
          "resident": (600, 1200)}


# ==========================================================================
# Cottage  (ground-centre root, door on the -Y navigation-facing wall)
# ==========================================================================
CX, CY = 32.0, 21.0          # core half extents
Z0, EAVE = 3.0, 41.0          # plinth top, eave line
RY, RZ = 5.0, 63.0            # ridge (asymmetric: shifted toward +Y)
FRONT_T = (RZ - EAVE) / (RY + CY)     # front slope tan  (~40 deg)
BACK_T = (RZ - EAVE) / (CY - RY)      # back slope tan   (~54 deg)


def front_z(y):
    return EAVE + (y + CY) * FRONT_T


def back_z(y):
    return RZ - (y - RY) * BACK_T


def roof_under(y):
    return front_z(y) if y <= RY else back_z(y)


def build_cottage(lod, mat):
    b = kit.Builder("cottage")
    full = lod == 0
    courses = 8
    COURSE_H = (EAVE - Z0) / courses
    # openings (outer frame extents are aligned to course boundaries)
    win_v0 = Z0 + 3 * COURSE_H + 1.6           # 18.85
    win_v1 = Z0 + 6 * COURSE_H - 1.6           # 29.9
    win_vc, win_h = (win_v0 + win_v1) / 2, win_v1 - win_v0
    door_h = Z0 + 7 * COURSE_H - 1.6 - Z0      # top of frame on a course line

    front = kit.Wall((0, -CY, 0), (1, 0, 0), (0, -1, 0))
    back = kit.Wall((0, CY, 0), (-1, 0, 0), (0, 1, 0))
    right = kit.Wall((CX, 0, 0), (0, 1, 0), (1, 0, 0))
    left = kit.Wall((-CX, 0, 0), (0, -1, 0), (-1, 0, 0))

    # core: cedar box (dark, reads as shadow in siding gaps) + cream gable prism
    b.box(-CX, -CY, Z0, CX, CY, EAVE, "cedar", tone=0.62 if lod < 2 else 0.95, keep=True)
    gable = []
    for x in (-CX, CX):   # apex 0.6 mm under the roof: no zero-gap AO contact
        gable += [V(x, -CY, EAVE), V(x, CY, EAVE), V(x, RY, RZ - 0.6)]
    g = kit.tmp_hull(gable)
    if lod < 2:
        kit.slice_bm(g, (0, 0, 1), (46.5, 52.0, 57.5))
        kit.slice_bm(g, (0, 1, 0), (-12.0, -2.0, 9.0))
    b.add(g, "trim_paint", tone=0.97)

    wall_specs = []  # (wall, span, openings)
    if lod < 2:
        ops_front = [kit.door(b, front, -12.0, 15.0, door_h, v0=Z0, boards=3 if full else 0,
                              knob=full),
                     kit.window(b, front, 14.0, win_vc, 11.0, win_h, muntins=full)]
        ops_back = [kit.window(b, back, -14.0, win_vc, 11.0, win_h, muntins=full),
                    kit.window(b, back, 12.0, win_vc, 11.0, win_h, muntins=full)]
        ops_right = [kit.window(b, right, -4.0, win_vc, 11.0, win_h, muntins=full)]
        ops_left = [kit.window(b, left, 3.0, win_vc, 11.0, win_h, muntins=full)]
        wall_specs = [(front, CX, ops_front, 0), (back, CX, ops_back, 1),
                      (right, CY, ops_right, 2), (left, CY, ops_left, 3)]
        for wall, half, ops, seed in wall_specs:
            kit.lap_siding(b, wall, -half, half, Z0, EAVE, courses, ops, tone_seed=seed)
        # corner boards
        for sx in (-1, 1):
            for sy in (-1, 1):
                x0, x1 = sorted((sx * (CX - 1.6), sx * (CX + 1.9)))
                y0, y1 = sorted((sy * (CY - 1.6), sy * (CY + 1.9)))
                b.box(x0, y0, Z0, x1, y1, EAVE, "trim_paint", bev=0.4 if full else 0.0)
        # frieze board under each gable
        for wall in (right, left):
            kit.wall_box(b, wall, -CY - 1.9, CY + 1.9, EAVE, EAVE + 1.8, 0, 1.6, "trim_paint",
                         tone=0.92)
    else:
        # LOD2: openings as proud panels only (door kept: navigation)
        kit.wall_box(b, front, -19.5, -4.5, Z0, Z0 + door_h, 0, 0.8, "door_paint", tone=0.75)
        kit.wall_box(b, front, -21.1, -2.9, Z0 + door_h, Z0 + door_h + 1.6, 0, 1.0, "trim_paint")
        for wall, u in ((front, 14.0), (back, -14.0), (back, 12.0), (right, -4.0), (left, 3.0)):
            kit.wall_box(b, wall, u - 6.5, u + 6.5, win_v0 - 1.6, win_v1 + 1.6, 0, 0.6, "trim_paint")
            kit.wall_box(b, wall, u - 5.5, u + 5.5, win_v0, win_v1, 0.6, 0.8, "window_glass")

    # gable battens + vent (LOD0 only)
    if full:
        for sx, wall in ((1, right), (-1, left)):
            for y in (-12.0, -2.0, 9.0):
                top = min(roof_under(y - 0.6), roof_under(y + 0.6)) - 0.5
                u = y if sx > 0 else -y
                kit.wall_box(b, wall, u - 0.55, u + 0.55, EAVE + 1.8, top, 0, 0.6, "trim_paint",
                             tone=0.9)
            uv = RY - 2.0 if sx > 0 else -(RY - 2.0)
            kit.window(b, wall, uv, 52.0, 4.2, 4.2, frame=1.0, proud=1.2, muntins=False, sill=False)

    # plinth
    if full:
        b.box(-CX - 0.6, -CY - 0.6, 0, CX + 0.6, CY + 0.6, Z0, "stone", tone=0.5, keep=True)  # mortar
        rows = [((-CX - 1.0, CX + 1.0), "x", -1, [13, 15, 12, 16, 12]),
                ((-CX - 1.0, CX + 1.0), "x", 1, [14, 12, 15, 13, 14]),
                ((-CY + 3.0, CY - 3.0), "y", 1, [12, 13, 11]),
                ((-CY + 3.0, CY - 3.0), "y", -1, [13, 11, 12])]
        k = 0
        for (a0, a1), axis, side, lens in rows:
            scale = (a1 - a0) / sum(lens)
            a = a0
            for L in lens:
                L *= scale
                prot = 1.0 + (0.3 if k % 3 == 0 else (-0.15 if k % 3 == 1 else 0.1))
                tone = (0.95, 0.86, 1.0, 0.9, 0.82, 0.97)[k % 6]
                g = 0.18
                if axis == "x":
                    yo = side * (CY + prot)
                    b.box(a + g, min(yo, side * (CY - 3)), 0, a + L - g, max(yo, side * (CY - 3)),
                          Z0, "stone", tone=tone)
                else:
                    xo = side * (CX + prot)
                    b.box(min(xo, side * (CX - 3)), a + g, 0, max(xo, side * (CX - 3)), a + L - g,
                          Z0, "stone", tone=tone)
                a += L
                k += 1
    else:
        b.box(-CX - 1.0, -CY - 1.0, 0, CX + 1.0, CY + 1.0, Z0, "stone", tone=0.9)
    # door step (usable: leads to the door navigation marker)
    b.box(-20.5, -26.0, 0, -3.5, -CY - 1.0, 2.6, "stone", tone=0.93, bev=0.5 if full else 0.0)

    # roof: asymmetric, exaggerated eaves (9 mm front/back, 8 mm at gables)
    tf = 2.6 / math.cos(math.atan(FRONT_T))
    tb = 2.6 / math.cos(math.atan(BACK_T))
    nc = {0: 4, 1: 2, 2: 1}[lod]
    rb = 0.45 if full else 0.0
    kit.stepped_roof_slope(b, -40.0, 40.0, -30.0, RY, front_z, tf, nc, bev=rb,
                           first_course=-CY - 1.6 if nc > 1 else None)
    kit.stepped_roof_slope(b, -40.0, 40.0, 30.0, RY, back_z, tb, nc, bev=rb, tone_seed=2,
                           first_course=CY + 1.6 if nc > 1 else None)
    ridge = []
    for x in (-40.0, 40.0):
        ridge += [V(x, RY - 3.0, front_z(RY - 3.0) + tf + 0.25),
                  V(x, RY + 3.0, back_z(RY + 3.0) + tb + 0.25),
                  V(x, RY - 3.0, front_z(RY - 3.0) + tf - 0.4),
                  V(x, RY + 3.0, back_z(RY + 3.0) + tb - 0.4),
                  V(x, RY, max(front_z(RY) + tf, back_z(RY) + tb) + 1.2)]
    b.hull(ridge, "roof_tile", tone=0.78, bev=0.4 if full else 0.0)

    # chimney: starts inside the gable body, passes through the back slope
    cx0, cx1, cy0, cy1 = 15.5, 24.5, 7.0, 16.0
    segs = [(46.0, 52.5), (52.5, 58.5), (58.5, 64.0), (64.0, 67.6)] if lod < 2 else [(46.0, 67.6)]
    if lod == 1:
        segs = [(46.0, 58.5), (58.5, 67.6)]
    for i, (z0, z1) in enumerate(segs):
        w = 0.3 if i % 2 else 0.0
        b.box(cx0 - w, cy0 - w, z0, cx1 + w, cy1 + w, z1, "stone",
              tone=(0.92, 1.0, 0.86, 0.97)[i % 4], bev=0.35 if full else 0.0)
    b.box(cx0 - 0.9, cy0 - 0.9, 67.6, cx1 + 0.9, cy1 + 0.9, 69.0, "stone", tone=0.88,
          bev=0.4 if full else 0.0)
    b.box(17.5, 9.0, 69.0, 22.5, 14.0, 70.0, "metal_dark", tone=0.55)

    ob = b.finalize("cottage_LOD%d" % lod, mat,
                    ao=dict(rays=20, dist_mm=9.0, strength=0.9, height_mm=40.0, height_dark=0.12))
    return ob


# ==========================================================================
# Fold-out rescue bridge  (root at deck-top centre)
# ==========================================================================
def build_bridge(lod, mat):
    b = kit.Builder("bridge")
    full = lod == 0
    bev = 0.6 if lod < 2 else 0.0
    for s in (-1, 1):
        ya, yb = sorted((s * 13.5, s * 19.5))
        for h in (-1, 1):                       # two halves, hinge at x = 0
            xi, xm, xo = h * 0.4, h * 55.0, h * 60.0
            pts = []
            for y in (ya, yb):
                pts += [V(xi, y, -4.0), V(xi, y, 1.6), V(xm, y, -4.0), V(xo, y, -1.2), V(xo, y, 1.6)]
            b.hull(pts, "rescue_orange", bev=bev)
            # reflective band near each end (outer face)
            if lod < 2:
                y0, y1 = sorted((s * 19.5, s * 19.8))
                x0, x1 = sorted((h * 49.0, h * 52.5))
                b.box(x0, y0, -3.4, x1, y1, 1.0, "paper")
            # posts + top rail + grip
            post_x = (5.5, 31.0, 56.0) if lod < 2 else (5.5, 56.0)
            for px in post_x:
                b.box(h * px - 1.3, s * 17.0 - 1.3, 1.6, h * px + 1.3, s * 17.0 + 1.3, 11.6,
                      "rescue_orange", tone=0.95)
            x0, x1 = sorted((h * 4.0, h * 57.5))
            y0, y1 = sorted((s * 15.6, s * 18.4))
            b.box(x0, y0, 11.4, x1, y1, 14.0, "rescue_orange", bev=0.5 if lod < 2 else 0.0)
            gseg = {0: 12, 1: 8, 2: 6}[lod]
            b.add(kit.tmp_cyl((h * 22.0, s * 16.8, 12.8), (h * 40.0, s * 16.8, 12.8), 3.2, gseg),
                  "rubber", smooth=True)
            if full:   # grip ribs
                for gx in (24.0, 31.0, 38.0):
                    b.add(kit.tmp_cyl((h * (gx - 0.5), s * 16.8, 12.8), (h * (gx + 0.5), s * 16.8, 12.8),
                                      3.18, gseg, caps=False), "rubber", tone=0.75, smooth=True)
        # hinge: knuckles on the stringer top, leaf plate on the outer face
        kn = [(13.8, 15.5), (15.9, 17.4), (17.8, 19.4)] if lod < 2 else [(13.8, 19.4)]
        for i, (k0, k1) in enumerate(kn):
            b.add(kit.tmp_cyl((0, s * k0, 2.6), (0, s * k1, 2.6), 1.7, 10 if full else 8),
                  "metal_dark", tone=(1.0, 0.85, 1.0)[i % 3], smooth=True)
        y0, y1 = sorted((s * 19.5, s * 19.95))
        b.box(-5.0, y0, -3.0, 5.0, y1, 1.0, "metal_dark", tone=0.9)
        if lod < 2:
            b.add(kit.tmp_cyl((0, s * 15.6, 12.7), (0, s * 18.4, 12.7), 1.45, 8), "metal_dark",
                  smooth=True)
            y0, y1 = sorted((s * 16.4, s * 17.6))
            b.box(-4.2, y0, 12.0, 4.2, y1, 13.4, "metal_dark", tone=0.85)
    # deck
    for h in (-1, 1):
        if lod < 2:
            n = 11 if full else 6
            a0, a1 = sorted((h * 0.6, h * 59.6))
            kit.planks(b, "x", a0, a1, n, 0.5, -13.5, 13.5, -1.8, 0.0, bev=0.3 if full else 0.0,
                       tone_seed=0 if h < 0 else 3)
            for cx in (21.0, 43.0):
                x0, x1 = sorted((h * (cx - 1.2), h * (cx + 1.2)))
                b.box(x0, -13.5, -3.6, x1, 13.5, -1.8, "metal_dark", tone=0.8)
        else:
            a0, a1 = sorted((h * 0.6, h * 59.6))
            b.box(a0, -13.5, -1.8, a1, 13.5, 0.0, "wood_deck")
        # end lip / ramp nose
        x0, x1, x2 = h * 59.6, h * 60.0, h * 57.0
        pts = []
        for y in (-13.5, 13.5):
            pts += [V(x2, y, 0.0), V(x0, y, 0.0), V(x1, y, -0.6), V(x1, y, -1.8), V(x2, y, -1.8)]
        b.hull(pts, "metal_dark", tone=0.9)
    return b.finalize("bridge_LOD%d" % lod, mat,
                      ao=dict(rays=20, dist_mm=8.0, strength=0.85, use_ground=False))


# ==========================================================================
# Rescue skiff  (waterline-centre root, bow +X, boarding side -Y)
# ==========================================================================
STATIONS = [  # x, half-beam, gunwale z, chine half, chine z, keel z, floor z
    (-36.0, 14.0, 6.0, 11.0, -3.4, -5.6, -2.2),
    (-24.0, 14.5, 6.0, 11.6, -3.8, -6.0, -2.4),
    (-8.0, 14.6, 6.1, 11.8, -3.9, -6.0, -2.4),
    (8.0, 14.2, 6.6, 11.0, -3.6, -5.9, -2.2),
    (20.0, 12.6, 7.4, 8.6, -2.6, -5.2, -1.4),
    (29.0, 9.2, 8.4, 5.0, -0.8, -3.6, 0.6),
    (35.0, 5.0, 9.2, 2.2, 1.6, -0.8, 3.4),
    (38.5, 1.6, 9.8, 0.7, 4.5, 3.0, 6.6),
]


def _interp(x, col):
    st = STATIONS
    if x <= st[0][0]:
        return st[0][col]
    for a, c in zip(st, st[1:]):
        if a[0] <= x <= c[0]:
            t = (x - a[0]) / (c[0] - a[0])
            return a[col] + (c[col] - a[col]) * t
    return st[-1][col]


def build_skiff(lod, mat):
    b = kit.Builder("skiff")
    full = lod == 0
    sections = []
    for x, hb, zg, hc, zc, zk, zf in STATIONS:
        outer = [(-hb, zg), (-(hb - 0.4), zg - 3.5), (-hc, zc), (0.0, zk),
                 (hc, zc), (hb - 0.4, zg - 3.5), (hb, zg)]
        inner = [(hb - 1.3, zg), (hc - 0.6, zf + 0.6), (0.0, zf), (-(hc - 0.6), zf + 0.6),
                 (-(hb - 1.3), zg)]
        sections.append([V(x, y, z) for y, z in outer + inner])

    def hull_role(c, n):
        inner = n.z > 0.3 or (n.y * c.y < 0 and n.z > -0.3)
        if inner or n.x < -0.7:
            return "hull_paint"
        return "metal_dark" if c.z < 0.0 else "hull_paint"

    def hull_tone(c, n):
        if n.z > 0.6 and c.z < 2.0:
            return 0.78          # cockpit floor
        if n.z > 0.3 or (n.y * c.y < 0 and n.z > -0.3):
            return 0.88          # inner liner
        return 1.0

    b.add(kit.tmp_loft(sections, closed_ring=True), hull_role, tone=hull_tone, smooth=True)

    # orange collar (RIB tube) along the gunwale
    def half_w(x):
        return 14.9 if x <= 8.0 else 14.9 * math.sqrt(max(0.0, 1 - ((x - 8.0) / 29.4) ** 2))

    xs = {0: [-37.0, -30, -20, -10, 0, 8, 15, 21, 26, 30, 33, 35.4, 36.8],
          1: [-37.0, -22, -6, 8, 18, 26, 32, 35.6],
          2: [-37.0, -10, 8, 24, 33]}[lod]
    tip = 37.4
    path = [V(x, -half_w(x), _interp(x, 2) + 1.0) for x in xs]
    path += [V(tip, 0.0, _interp(tip, 2) + 1.0)]
    path += [V(x, half_w(x), _interp(x, 2) + 1.0) for x in reversed(xs)]
    radii = [2.6] * len(path)
    radii[0] = radii[-1] = 2.3
    seg = {0: 12, 1: 8, 2: 6}[lod]
    b.add(kit.tmp_sweep(path, 2.6, seg=seg, radii=radii), "rescue_orange", smooth=True)
    if lod < 2:   # stern cones of the collar
        for s in (-1, 1):
            p0 = V(-37.0, s * half_w(-37.0), _interp(-37.0, 2) + 1.0)
            b.add(kit.tmp_cyl(p0, p0 + V(-1.8, 0, 0), 2.3, seg, r1=1.2), "rescue_orange",
                  tone=0.9, smooth=True)
        # rubber rub-strake + grab-line loops on the collar
        for s in (-1, 1):
            for gx in ((-26.0, -6.0, 14.0) if full else (-16.0, 10.0)):
                y = s * (half_w(gx) + 2.2)
                z = _interp(gx, 2) + 2.6
                b.add(kit.tmp_sweep([V(gx - 3.0, y, z - 0.6), V(gx - 1.4, y + s * 0.5, z + 0.5),
                                     V(gx + 1.4, y + s * 0.5, z + 0.5), V(gx + 3.0, y, z - 0.6)],
                                    0.55, seg=5), "rubber", smooth=True)

    # thwarts (seat benches) and four seat pads = visible capacity 4
    seat_markers = []
    for i, tx in enumerate((-14.0, 6.0)):
        hw = _interp(tx, 1) - 1.6
        b.box(tx - 3.2, -hw, 1.2, tx + 3.2, hw, 2.6, "wood_deck", tone=0.95 - 0.05 * i,
              bev=0.4 if lod < 2 else 0.0)
        for s in (-1, 1):
            y0, y1 = sorted((s * 1.2, s * 10.2))
            b.box(tx - 2.7, y0, 2.6, tx + 2.7, y1, 3.9, "evergreen", tone=1.0,
                  bev=(0.6 if full else 0.4) if lod < 2 else 0.0, bev_seg=2 if full else 1)
            seat_markers.append(V(tx, s * 5.7, 3.9))
        # thwart legs to the floor
        if lod < 2:
            b.box(tx - 1.0, -3.0, -2.4, tx + 1.0, 3.0, 1.2, "hull_paint", tone=0.8)
    # bow locker / foredeck
    b.hull([V(22.0, -10.0, 6.0), V(22.0, 10.0, 6.0), V(30.5, -6.5, 8.0), V(30.5, 6.5, 8.0),
            V(22.0, -10.0, -1.0), V(22.0, 10.0, -1.0), V(30.5, -6.5, 0.8), V(30.5, 6.5, 0.8)],
           "hull_paint", tone=0.94, bev=0.5 if lod < 2 else 0.0)
    if lod < 2:
        b.box(-35.6, -1.6, -2.0, -33.6, 1.6, 6.0, "hull_paint", tone=0.85)  # transom knee
    # paddles stowed along the port floor (wood)
    if full:
        for s in (-1, 1):
            y = s * 8.6
            b.add(kit.tmp_cyl((-26.0, y, -1.2), (2.0, y, -1.2), 0.6, 6), "wood_deck", tone=0.85)
            b.box(2.0, y - 1.4, -1.6, 9.5, y + 1.4, -0.8, "wood_deck", tone=0.8, bev=0.3)
    # outboard motor
    mb = 1.2 if full else (0.8 if lod == 1 else 0.0)
    b.box(-40.0, -3.6, 9.0, -33.4, 3.6, 19.0, "metal_dark", bev=mb, bev_seg=2 if full else 1)
    if lod < 2:
        b.box(-40.2 + 0.4, -3.75, 14.4, -33.6, 3.75, 15.6, "paper")      # band
    b.box(-37.2, -2.4, 4.5, -34.8, 2.4, 9.2, "metal_dark", tone=0.8)      # clamp bracket
    b.box(-38.4, -1.0, -4.0, -36.2, 1.0, 9.0, "metal_dark", tone=0.75)    # leg
    b.box(-39.2, -1.4, -6.0, -35.6, 1.4, -3.6, "metal_dark", tone=0.75, bev=0.4 if lod < 2 else 0.0)
    if lod < 2:
        b.add(kit.tmp_cyl((-39.95, 0, -4.8), (-39.2, 0, -4.8), 1.6, 8), "metal_dark", tone=0.6)
    kit.rail(b, (-33.6, 0, 14.0), (-24.5, 0, 12.6), 1.4, 1.4, "metal_dark", tone=0.85)
    b.add(kit.tmp_cyl((-27.5, 0, 13.06), (-23.8, 0, 12.5), 1.05, 8), "rubber", smooth=True)
    ob = b.finalize("skiff_LOD%d" % lod, mat,
                    ao=dict(rays=20, dist_mm=8.0, strength=0.85, use_ground=False))
    return ob, seat_markers


# ==========================================================================
# Residents: peg-figure miniatures with a rigid-weight armature
# ==========================================================================
MX = "mixamorig:"
BODY_BONES = ["Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
              "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
              "RightShoulder", "RightArm", "RightForeArm", "RightHand",
              "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
              "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"]
CHAIR_EXTRA = ["resq_Chair", "resq_WheelL", "resq_WheelR"]

# Heads are deliberately enlarged (~29 % of standing height) for miniature
# charm and legibility at 0.6 m. H is the standing height to the hair top.
VARIANTS = {
    "adult": dict(H=34.0, top="cloth_blue", legs="cloth_slate", skin="skin_a", hair="hair_dark",
                  head_r=4.9, hair_r=5.1, half_w=4.6, half_d=3.0, sh_x=4.65, arm_r=1.35,
                  hand_r=1.45, hip_z=13.6, torso_z0=12.4, leg_x=1.9, leg_r=1.75, shoe=(3.3, 5.0, 2.3)),
    "child": dict(H=24.0, top="cloth_mustard", legs="cloth_sage", skin="skin_c", hair="hair_dark",
                  head_r=4.2, hair_r=4.4, half_w=3.5, half_d=2.4, sh_x=3.45, arm_r=1.05,
                  hand_r=1.15, hip_z=8.9, torso_z0=8.2, leg_x=1.45, leg_r=1.35, shoe=(2.6, 4.0, 1.8)),
    "elder": dict(H=34.0, top="cloth_plum", legs="cloth_slate", skin="skin_b", hair="hair_grey",
                  head_r=4.8, hair_r=5.0, half_w=4.7, half_d=3.1, sh_x=4.65, arm_r=1.35,
                  hand_r=1.45, hip_z=13.6, torso_z0=12.4, leg_x=1.9, leg_r=1.75, shoe=(3.3, 4.8, 2.3),
                  stoop=11.0, cane=True),
    "wheelchair": dict(H=34.0, top="cloth_sage", legs="cloth_blue", skin="skin_b", hair="hair_auburn",
                       head_r=4.9, hair_r=5.1, half_w=4.6, half_d=3.0, sh_x=4.65, arm_r=1.35,
                       hand_r=1.45, hip_z=13.6, torso_z0=12.4, leg_x=2.0, leg_r=1.75,
                       shoe=(3.3, 4.6, 2.1), seated=True),
}
TORSO_T = [0.0, 0.18, 0.42, 0.70, 0.84, 0.92, 0.96, 1.0]
TORSO_RX = [0.95, 0.97, 0.90, 1.0, 0.96, 0.65, 0.33, 0.30]
TORSO_RY = [0.97, 1.0, 0.93, 1.0, 0.97, 0.73, 0.43, 0.40]


def tmp_capsule(p0, p1, r, seg=8, rings=1, r_end=None):
    p0, p1 = V(p0), V(p1)
    L = (p1 - p0).length
    r1 = r if r_end is None else r_end
    angs = [i * (math.pi / 2) / rings for i in range(rings + 1)]
    prof = [((r - r * math.cos(a)) / L, r * math.sin(a), r * math.sin(a)) for a in angs]
    prof += [((L - r1 + r1 * math.cos(a)) / L, r1 * math.sin(a), r1 * math.sin(a))
             for a in reversed(angs)]
    return kit.tmp_lathe(p0, p1, prof, seg)


def resident_layout(P):
    """Joint positions (mm, Blender rest pose, facing -Y; Left = +X)."""
    H = P["H"]
    seated = P.get("seated", False)
    dz = -2.6 if seated else 0.0
    hair_c = V(0, 0.8, H - P["hair_r"] * 0.78 + dz)
    head_c = V(0, 0, hair_c.z - 1.1)
    neck_top = head_c.z - P["head_r"] * 0.55
    t0 = P["torso_z0"] + dz
    sh_z = t0 + 0.80 * (neck_top - t0)
    J = {"hair_c": hair_c, "head_c": head_c, "neck_top": neck_top, "torso_z0": t0, "sh_z": sh_z,
         "hip": V(0, 0, P["hip_z"] + dz)}
    for side, s in (("Left", 1), ("Right", -1)):
        sh = V(s * P["sh_x"], 0, sh_z)
        if seated:
            hand = V(s * 6.4, -2.2, 12.4)
            elbow = V(s * 6.0, 1.2, 16.4)
        elif P.get("cane") and s < 0:
            hand = V(s * 5.3, -4.4, 14.6)
            elbow = V(s * 5.6, -1.6, (sh_z + 14.6) / 2)
        else:
            hand = V(s * 5.0, -0.3, 12.8 * (H / 34.0) ** 1.0 if H >= 30 else 8.8)
            elbow = V(s * 5.0, 0.4, (sh_z + hand.z) / 2)
        J[side + "Shoulder"] = V(s * 0.9, 0, sh_z)
        J[side + "Arm"] = sh
        J[side + "ForeArm"] = elbow
        J[side + "Hand"] = hand
        lx = s * P["leg_x"]
        if seated:
            J[side + "UpLeg"] = V(lx, 0.6, 12.0)
            J[side + "Leg"] = V(lx, -7.0, 12.0)
            J[side + "Foot"] = V(lx, -8.2, 3.6)
            J[side + "ToeBase"] = V(lx, -11.0, 3.0)
        else:
            hip = P["hip_z"]
            J[side + "UpLeg"] = V(lx, 0, hip)
            J[side + "Leg"] = V(lx, -0.25, (hip + P["shoe"][2]) * 0.52)
            J[side + "Foot"] = V(lx, 0, P["shoe"][2] - 0.3)
            J[side + "ToeBase"] = V(lx, -2.6, 0.7)
    return J


def build_resident(variant, lod, mat):
    P = VARIANTS[variant]
    b = kit.Builder("resident")
    seated = P.get("seated", False)
    bones = [MX + n for n in BODY_BONES] + (CHAIR_EXTRA if seated else [])
    BI = {n: i for i, n in enumerate(bones)}

    def bi(n):
        return BI[n if n.startswith("resq_") else MX + n]

    J = resident_layout(P)
    lseg = {0: 8, 1: 6, 2: 5}[lod]
    tseg = {0: 10, 1: 8, 2: 6}[lod]
    hseg = {0: (12, 8), 1: (10, 6), 2: (8, 5)}[lod]
    stoop = math.radians(P.get("stoop", 0.0))
    piv = J["hip"]
    up = (Matrix.Translation(piv) @ Matrix.Rotation(stoop, 4, "X") @ Matrix.Translation(-piv)
          if stoop else Matrix.Identity(4))

    # torso (rigid on Spine1)
    z0, z1 = J["torso_z0"], J["neck_top"]
    prof = [(0.0, 0.0, 0.0)] + [(max(1e-4, t), P["half_w"] * rx, P["half_d"] * ry)
                                for t, rx, ry in zip(TORSO_T, TORSO_RX, TORSO_RY)] + [(1.0, 0.0, 0.0)]
    prof[1] = (1e-4, prof[1][1] * 0.8, prof[1][2] * 0.8)
    prof[-2] = (0.9999, prof[-2][1], prof[-2][2])
    b.add(kit.tmp_lathe((0, 0, z0), (0, 0, z1), prof, tseg), P["top"], bone=bi("Spine1"),
          smooth=True, xform=up)
    # head, hair, nose (Head)
    b.add(kit.tmp_sphere(J["head_c"], P["head_r"], *hseg, scale=(1, 1, 1.02)), P["skin"],
          bone=bi("Head"), smooth=True, xform=up)
    b.add(kit.tmp_sphere(J["hair_c"], P["hair_r"], *hseg, scale=(1.0, 1.0, 0.78)), P["hair"],
          bone=bi("Head"), smooth=True, xform=up)
    if lod < 2:
        hc = J["head_c"]
        b.add(kit.tmp_sphere((0, -P["head_r"] + 0.2, hc.z - 0.9), 0.8, 6, 4), P["skin"], tone=0.93,
              bone=bi("Head"), smooth=True, xform=up)
    # arms: upper (Arm), fore (ForeArm), mitten hand (Hand)
    for side in ("Left", "Right"):
        sh, el, ha = J[side + "Arm"], J[side + "ForeArm"], J[side + "Hand"]
        b.add(tmp_capsule(sh, el, P["arm_r"], lseg), P["top"], bone=bi(side + "Arm"), smooth=True,
              xform=up)
        b.add(tmp_capsule(el, ha, P["arm_r"] * 0.92, lseg), P["top"], tone=0.95,
              bone=bi(side + "ForeArm"), smooth=True, xform=up)
        hp = ha + (ha - el).normalized() * 0.5
        b.add(kit.tmp_sphere(hp, P["hand_r"], lseg, max(4, lseg - 3)), P["skin"],
              bone=bi(side + "Hand"), smooth=True, xform=up)
    # legs: thigh (UpLeg), shin (Leg), shoe (Foot)
    sw, sl, shh = P["shoe"]
    for side, s in (("Left", 1), ("Right", -1)):
        hp, kn, an = J[side + "UpLeg"], J[side + "Leg"], J[side + "Foot"]
        b.add(tmp_capsule(hp + V(0, 0, 0.6 if not seated else 0.0), kn, P["leg_r"], lseg),
              P["legs"], bone=bi(side + "UpLeg"), smooth=True)
        b.add(tmp_capsule(kn, an + V(0, 0, 0.3), P["leg_r"] * 0.93, lseg), P["legs"], tone=0.95,
              bone=bi(side + "Leg"), smooth=True)
        fz = 0.0 if not seated else 2.4
        fy = an.y
        b.box(an.x - sw / 2, fy - sl + 1.6, fz, an.x + sw / 2, fy + 1.6, fz + shh, "rubber",
              bone=bi(side + "Foot"), bev=0.7 if lod < 2 else 0.0)
    if P.get("cane"):
        hand = up @ J["RightHand"]
        foot = V(-6.2, -7.4, 0.0)
        b.add(kit.tmp_cyl(foot, hand + V(0, 0, 0.4), 0.6, 6), "wood_deck", tone=0.7,
              bone=bi("RightHand"))
        b.add(kit.tmp_cyl(hand + V(0, 0, 0.4), hand + V(0, 2.4, 1.1), 0.6, 6), "wood_deck",
              tone=0.7, bone=bi("RightHand"))
    if seated:
        C = bi("resq_Chair")
        seat_z = 10.6
        bv = 0.4 if lod < 2 else 0.0
        b.box(-5.0, -6.4, seat_z - 1.0, 5.0, 2.4, seat_z, "cloth_slate", bone=C, bev=bv)
        b.box(-4.8, 2.9, seat_z - 0.6, 4.8, 3.8, seat_z + 9.5, "cloth_slate", tone=0.9, bone=C, bev=bv)
        b.box(-4.0, -12.0, 1.6, 4.0, -8.6, 2.4, "metal_dark", bone=C)          # footplate
        wseg = {0: 16, 1: 12, 2: 10}[lod]
        for side, wb, s in (("L", "resq_WheelL", 1), ("R", "resq_WheelR", -1)):
            wx = s * 7.0
            b.add(kit.tmp_cyl((wx - s * 0.7, 1.2, 7.4), (wx + s * 0.7, 1.2, 7.4), 7.4, wseg),
                  "rubber", bone=bi(wb))
            b.add(kit.tmp_cyl((wx - s * 0.9, 1.2, 7.4), (wx + s * 0.9, 1.2, 7.4), 6.1, wseg),
                  "mineral", bone=bi(wb))
            b.add(kit.tmp_cyl((wx - s * 1.3, 1.2, 7.4), (wx + s * 1.3, 1.2, 7.4), 1.2, 8),
                  "metal_dark", bone=bi(wb))
            fx = s * 5.4
            for p0, p1 in (((fx, 3.4, seat_z + 9.0), (fx, 3.4, 5.0)),
                           ((fx, 3.4, 5.0), (fx, -7.4, 4.6)),
                           ((fx, -6.0, seat_z - 0.6), (fx, -9.4, 2.0)),
                           ((fx, 3.4, seat_z + 9.0), (fx, 5.4, seat_z + 9.4))):
                kit.rail(b, p0, p1, 0.9, 0.9, "metal_dark", bone=C)
            b.add(kit.tmp_cyl((fx, 5.0, seat_z + 9.3), (fx, 7.0, seat_z + 9.7), 0.75, 6), "rubber",
                  bone=C)
            b.add(kit.tmp_cyl((fx - 0.5, -7.4, 2.0), (fx + 0.5, -7.4, 2.0), 2.0, 10), "rubber",
                  bone=C)
            kit.rail(b, (fx, -7.4, 2.0), (fx, -7.4, 4.8), 0.7, 0.7, "metal_dark", bone=C)
    ob = b.finalize("resident_%s_LOD%d" % (variant, lod), mat,
                    ao=dict(rays=16, dist_mm=5.0, strength=0.8, height_mm=14.0, height_dark=0.15),
                    cull=dict(gap_mm=0.3))

    # Mixamo-style bone chain (rest pose = authored pose; stoop baked in)
    def S(p):
        return tuple(up @ V(p))
    hip = J["hip"]
    top = J["hair_c"].z + P["hair_r"] * 0.78
    bl = [(MX + "Hips", tuple(hip), tuple(hip + V(0, 0, 1.6)), None),
          (MX + "Spine", tuple(hip + V(0, 0, 1.6)), S(hip + V(0, 0, 4.0)), MX + "Hips"),
          (MX + "Spine1", S(hip + V(0, 0, 4.0)), S(V(0, 0, (hip.z + 4.0 + J["sh_z"]) / 2 + 1.0)), MX + "Spine"),
          (MX + "Spine2", S(V(0, 0, (hip.z + 4.0 + J["sh_z"]) / 2 + 1.0)), S(V(0, 0, J["sh_z"])), MX + "Spine1"),
          (MX + "Neck", S(V(0, 0, J["sh_z"])), S(V(0, 0, J["neck_top"])), MX + "Spine2"),
          (MX + "Head", S(V(0, 0, J["neck_top"])), S(V(0, 0, top)), MX + "Neck")]
    for side in ("Left", "Right"):
        bl += [(MX + side + "Shoulder", S(J[side + "Shoulder"]), S(J[side + "Arm"]), MX + "Spine2"),
               (MX + side + "Arm", S(J[side + "Arm"]), S(J[side + "ForeArm"]), MX + side + "Shoulder"),
               (MX + side + "ForeArm", S(J[side + "ForeArm"]), S(J[side + "Hand"]), MX + side + "Arm"),
               (MX + side + "Hand", S(J[side + "Hand"]),
                S(J[side + "Hand"] + (J[side + "Hand"] - J[side + "ForeArm"]).normalized() * 2.0),
                MX + side + "ForeArm"),
               (MX + side + "UpLeg", tuple(J[side + "UpLeg"]), tuple(J[side + "Leg"]), MX + "Hips"),
               (MX + side + "Leg", tuple(J[side + "Leg"]), tuple(J[side + "Foot"]), MX + side + "UpLeg"),
               (MX + side + "Foot", tuple(J[side + "Foot"]), tuple(J[side + "ToeBase"]), MX + side + "Leg"),
               (MX + side + "ToeBase", tuple(J[side + "ToeBase"]),
                tuple(J[side + "ToeBase"] + V(0, -1.4, 0)), MX + side + "Foot")]
    if seated:
        bl += [("resq_Chair", (0, 0, 0.5), (0, 0, 6.0), None),
               ("resq_WheelL", (7.0, 1.2, 7.4), (9.0, 1.2, 7.4), "resq_Chair"),
               ("resq_WheelR", (-7.0, 1.2, 7.4), (-9.0, 1.2, 7.4), "resq_Chair")]
    order = {n: i for i, n in enumerate(bones)}
    bl.sort(key=lambda r: order[r[0]])
    return ob, bl, bones


# ==========================================================================
# Publishing
# ==========================================================================
def check_dims(name, size, target):
    res = {}
    ok = True
    for axis, got, want in zip("xyz", size, target):
        dev = (got - want) / want
        res[axis] = {"measured_mm": round(got, 3), "target_mm": want,
                     "deviation_pct": round(dev * 100, 3), "pass": abs(dev) <= 0.01}
        ok &= abs(dev) <= 0.01
    return ok, res


def lod_checks(tris, budget):
    lo, hi = budget
    c = {"lod0_in_budget": lo <= tris[0] <= hi}
    if len(tris) > 1:
        c["lod1_ratio"] = round(tris[1] / tris[0], 3)
        c["lod1_le_60pct"] = tris[1] <= 0.6 * tris[0]
    if len(tris) > 2:
        c["lod2_ratio"] = round(tris[2] / tris[0], 3)
        c["lod2_le_30pct"] = tris[2] <= 0.3 * tris[0]
    return c


def ensure_lod_ratio(lods, mat_name):
    """If a semantic LOD misses its ratio, top it up with collapse decimation."""
    notes = []
    t0 = kit.tri_count(lods[0])
    for i, lim in ((1, 0.6), (2, 0.3)):
        if i >= len(lods):
            continue
        t = kit.tri_count(lods[i])
        if t > lim * t0:
            ratio = (lim * t0 * 0.97) / t
            name = lods[i].name
            old = lods[i]
            new = kit.decimate_copy(old, ratio, name + "_dec")
            bpy.data.objects.remove(old)
            new.name = name
            new.data.name = name
            lods[i] = new
            notes.append("LOD%d semantic build had %d tris; collapse-decimated by %.3f to %d"
                         % (i, t, ratio, kit.tri_count(new)))
    return notes


def markers_doc(markers):
    out = {}
    for ob in markers:
        p = [round(c / MM, 3) for c in ob.matrix_world.translation]
        out[ob.name] = {"blender_mm": p, "unity_mm": kit.blender_to_unity(p),
                        "meaning": ob.get("meaning", "")}
    return out


def roundtrip(fbx_path, names):
    """Blender re-import of the exported FBX; measures LOD0 bounds again."""
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    st = bpy.ops.import_scene.fbx(filepath=fbx_path, use_manual_orientation=False,
                                  global_scale=1.0, use_anim=False, colors_type="SRGB")
    if st != {"FINISHED"}:
        raise RuntimeError("re-import failed " + fbx_path)
    got = {}
    for n in names:
        ob = bpy.data.objects.get(n)
        if ob is None:
            got[n] = None
            continue
        bpy.context.view_layer.update()
        got[n] = kit.bounds_mm([ob])
    roots = {ob.name: [round(x, 5) for x in ob.rotation_euler] for ob in bpy.data.objects
             if ob.parent is None}
    alphas = {n: kit.mean_alpha(bpy.data.objects[n]) for n in names if n in bpy.data.objects}
    return {"lod_bounds_mm": got, "root_rotations_rad": roots, "mean_ao_alpha": alphas,
            "colour_attrs": {ob.name: [a.name for a in ob.data.color_attributes]
                             for ob in bpy.data.objects if ob.type == "MESH"}}


def bake_lods(lods, ground_z_mm, dist_mm):
    """Cycles AO (16 samples) into vertex alpha; geometric AO is the fallback."""
    method = {}
    for o in lods:
        try:
            ok = kit.bake_ao_cycles(o, distance_mm=dist_mm, samples=16, ground_z_mm=ground_z_mm)
        except Exception as ex:  # keep the geometric bake already in alpha
            print("CYCLES BAKE FAILED", o.name, ex)
            ok = False
        method[o.name] = "cycles_ao_16spp" if ok else "geometric_raycast_fallback"
    return method


def publish(asset, root, lods, markers, extra, meta, target=None, folder=None, fbx_name=None,
            save=True, budget_key=None):
    folder = os.path.join(LOOK, folder or asset)
    os.makedirs(folder, exist_ok=True)
    fbx_name = fbx_name or asset
    bpy.context.view_layer.update()
    tris = [kit.tri_count(o) for o in lods]
    bnd = kit.bounds_mm([lods[0]])
    doc = {
        "schema": "resq-lookdev-asset-1", "asset": asset, "kit_version": kit.KIT_VERSION,
        "origin": "original procedural geometry authored in tools/blender (no downloads, no paid generation)",
        "evidence_kind": "automated (Blender headless); Unity import, device and human review pending",
        "units": "metres in files; values below in millimetres",
        "blender_axes": "Z up, asset forward -Y",
        "bounds_lod0_blender_mm": bnd,
        "bounds_lod0_unity_mm_size": [bnd["size"][0], bnd["size"][2], bnd["size"][1]],
        "triangles": {o.name: t for o, t in zip(lods, tris)},
        "material": "RESQ_Shared (one material). Vertex colour 'Col': RGB = albedo (sRGB in FBX), "
                    "A = baked AO; UV = palette-atlas cell (atlas alpha = smoothness)",
        "mean_ao_alpha": {o.name: kit.mean_alpha(o) for o in lods},
        "colliders": {o.name: kit.tri_count(o) for o in extra if o.name.endswith("_COL")},
        "material_roles_faces_lod0": kit.role_counts(lods[0]),
        "markers": markers_doc(markers),
    }
    doc.update(meta)
    checks = {}
    if target:
        ok, dims = check_dims(asset, bnd["size"], target)
        checks["dimensions_pm1pct"] = ok
        doc["dimension_check"] = dims
    checks.update(lod_checks(tris, BUDGET[budget_key or asset]))
    doc["checks"] = checks
    blend = os.path.join(folder, fbx_name + ".blend")
    fbx = os.path.join(folder, fbx_name + ".fbx")
    if save:
        kit.save_blend(blend)
    objs = [root] + list(lods) + list(markers) + list(extra)
    doc["fbx_export_settings"] = kit.export_fbx(fbx, objs)
    doc["fbx_file_inspection"] = kit.fbx_inspect(fbx)
    doc["files"] = {"fbx": os.path.relpath(fbx, REPO), "fbx_sha256": kit.sha256_file(fbx),
                    "fbx_bytes": os.path.getsize(fbx)}
    if os.path.exists(blend):
        doc["files"].update({"blend": os.path.relpath(blend, REPO),
                             "blend_sha256": kit.sha256_file(blend),
                             "blend_bytes": os.path.getsize(blend)})
    pv = os.path.join(folder, fbx_name + "_preview.png")
    kit.render_preview(list(lods), pv)
    doc["files"]["preview"] = os.path.relpath(pv, REPO)
    doc["preview_note"] = "EEVEE, left to right LOD0/LOD1/LOD2, ortho 3/4 view, same light as the contact sheet"
    doc["generator_sha256"] = {"lookdev.py": kit.sha256_file(__file__),
                               "kit.py": kit.sha256_file(kit.__file__)}
    return doc, fbx


def finish_doc(doc, fbx, lod_names, json_path):
    rt = roundtrip(fbx, lod_names)
    doc["blender_reimport"] = rt
    b0 = rt["lod_bounds_mm"].get(lod_names[0])
    if b0:
        dev = max(abs(a - b) for a, b in zip(b0["size"], doc["bounds_lod0_blender_mm"]["size"]))
        doc["checks"]["reimport_bounds_match_0p01mm"] = dev < 0.01
    kit.write_json(json_path, doc)
    print("PUBLISHED", json_path, doc["triangles"], doc["checks"])
    return doc


# ------------------------------------------------------------------ assets
def make_cottage():
    kit.reset_scene()
    mat = kit.shared_material(ATLAS)
    root = kit.empty("cottage", (0, 0, 0), size_mm=10)
    lods = [build_cottage(i, mat) for i in range(3)]
    notes = ensure_lod_ratio(lods, mat.name)
    for o in lods:
        o.parent = root
    for o in lods[1:]:
        o.hide_set(True)
    m = []
    for nm, p, meaning in (
            ("cottage_mk_door", (-12.0, -CY, Z0), "door threshold centre on the -Y navigation-facing wall"),
            ("cottage_mk_door_nav", (-12.0, -28.0, 0.0), "ground navigation point in front of the door step"),
            ("cottage_mk_chimney_top", (20.0, 11.5, 70.0), "chimney top (smoke/hazard anchor)")):
        e = kit.empty(nm, p, root)
        e["meaning"] = meaning
        m.append(e)
    meta = {"pivot": "ground centre: root at footprint centre, z=0 on the support surface",
            "lod_method": "semantic rebuild per LOD (fewer courses/details), decimation only as top-up",
            "lod_notes": notes,
            "design": "cedar lap-siding wedge courses over a dark core (real shadow lines), cream gables, "
                      "asymmetric roof (ridge 5 mm toward +Y, ~40/54 deg slopes) with 9 mm front/back and 8 mm "
                      "gable eaves, stone plinth blocks, chimney rooted inside the gable body"}
    meta["ao_bake"] = bake_lods(lods, 0.0, 8.0)
    col = kit.collider_from([lods[0]], "cottage_COL", parent=root)
    doc, fbx = publish("cottage", root, lods, m, [col], meta, TARGETS["cottage"])
    finish_doc(doc, fbx, [o.name for o in lods], os.path.join(LOOK, "cottage", "cottage.json"))


def make_bridge():
    kit.reset_scene()
    mat = kit.shared_material(ATLAS)
    root = kit.empty("bridge", (0, 0, 0), size_mm=10)
    lods = [build_bridge(i, mat) for i in range(3)]
    notes = ensure_lod_ratio(lods, mat.name)
    for o in lods:
        o.parent = root
    for o in lods[1:]:
        o.hide_set(True)
    m = []
    for nm, p, meaning in (
            ("bridge_mk_support_near", (-52.0, 0.0, -4.0), "near bearing (-X end) on the bank surface"),
            ("bridge_mk_support_far", (52.0, 0.0, -4.0), "far bearing (+X end) on the bank surface"),
            ("bridge_mk_deck_end_near", (-60.0, 0.0, 0.0), "walking-surface end, near"),
            ("bridge_mk_deck_end_far", (60.0, 0.0, 0.0), "walking-surface end, far"),
            ("bridge_mk_hinge", (0.0, 0.0, 2.6), "fold hinge axis (along Y)"),
            ("bridge_mk_grip_near_L", (-31.0, 16.8, 12.8), "grip centre"),
            ("bridge_mk_grip_near_R", (-31.0, -16.8, 12.8), "grip centre"),
            ("bridge_mk_grip_far_L", (31.0, 16.8, 12.8), "grip centre"),
            ("bridge_mk_grip_far_R", (31.0, -16.8, 12.8), "grip centre")):
        e = kit.empty(nm, p, root)
        e["meaning"] = meaning
        m.append(e)
    meta = {"pivot": "deck centre: root on the top surface of the deck planks at mid-span (z=0 = walk surface); "
                     "stringer bottoms at z=-4, grip tops at z=+16",
            "state": "deployed", "lod_notes": notes,
            "design": "two orange halves folding about a central Y-axis hinge (three knuckles per side, leaf plates, "
                      "rail hinge links), deck planks, rubber grips, reflective end bands, under-deck cross members"}
    meta["ao_bake"] = bake_lods(lods, None, 6.0)
    col = kit.collider_from([lods[0]], "bridge_COL", parent=root)
    doc, fbx = publish("bridge", root, lods, m, [col], meta, TARGETS["bridge"])
    finish_doc(doc, fbx, [o.name for o in lods], os.path.join(LOOK, "bridge", "bridge.json"))


def make_skiff():
    kit.reset_scene()
    mat = kit.shared_material(ATLAS)
    root = kit.empty("skiff", (0, 0, 0), size_mm=10)
    built = [build_skiff(i, mat) for i in range(3)]
    lods = [b[0] for b in built]
    seats = built[0][1]
    notes = ensure_lod_ratio(lods, mat.name)
    for o in lods:
        o.parent = root
    for o in lods[1:]:
        o.hide_set(True)
    m = []
    for i, p in enumerate(seats):
        e = kit.empty("skiff_mk_seat_%d" % (i + 1), tuple(p), root)
        e["meaning"] = "seat %d (top of seat pad)" % (i + 1)
        m.append(e)
    for nm, p, meaning in (("skiff_mk_boarding", (0.0, -17.5, 0.0), "boarding point, -Y side, at the waterline"),
                           ("skiff_mk_bow", (40.0, 0.0, 8.0), "bow tip (travel direction +X)")):
        e = kit.empty(nm, p, root)
        e["meaning"] = meaning
        m.append(e)
    meta = {"pivot": "waterline centre: z=0 is the design waterline at mid-length/mid-beam",
            "capacity_seats": 4, "lod_notes": notes,
            "design": "rescue RIB: cream hull with dark bottom paint below the waterline, rescue-orange collar "
                      "band around the gunwale, two thwarts carrying four dark-green seat pads, foredeck locker, "
                      "stowed paddles, outboard motor with tiller"}
    meta["ao_bake"] = bake_lods(lods, None, 6.0)
    col = kit.collider_from([lods[0]], "skiff_COL", parent=root)
    doc, fbx = publish("skiff", root, lods, m, [col], meta, TARGETS["skiff"])
    finish_doc(doc, fbx, [o.name for o in lods], os.path.join(LOOK, "skiff", "skiff.json"))


def make_residents():
    kit.reset_scene()
    mat = kit.shared_material(ATLAS)
    built = {}
    for vi, variant in enumerate(("adult", "child", "elder", "wheelchair")):
        coll = bpy.data.collections.new("resident_" + variant)
        bpy.context.scene.collection.children.link(coll)
        lods, bl, bones = [], None, None
        for lod in range(3):
            ob, bl, bones = build_resident(variant, lod, mat)
            lods.append(ob)
        notes = ensure_lod_ratio(lods, mat.name)
        ao_method = bake_lods(lods, 0.0, 4.0)
        rig = kit.make_armature("resident_" + variant, bl)
        for o in lods:
            kit.rigid_skin(o, rig, bones)
        col = kit.collider_from([lods[2]], "resident_%s_COL" % variant, parent=rig, max_tris=120)
        mk = []
        top = max(v.co.z for v in lods[0].data.vertices) / MM
        for nm, p, meaning in (("ground", (0, 0, 0), "ground contact root (feet / wheels at z=0)"),
                               ("head_top", (0, 0, round(top, 3)), "top of head (label anchor)")):
            e = kit.empty("resident_%s_mk_%s" % (variant, nm), p, rig)
            e["meaning"] = meaning
            mk.append(e)
        for o in [rig] + lods + mk + [col]:
            for c in o.users_collection:
                c.objects.unlink(o)
            coll.objects.link(o)
        for o in lods[1:]:
            o.hide_set(True)
        built[variant] = (rig, lods, mk, notes, bones, col, ao_method)
    os.makedirs(os.path.join(LOOK, "resident"), exist_ok=True)
    kit.save_blend(os.path.join(LOOK, "resident", "resident.blend"))
    summary = {}
    docs = {}
    for variant, (rig, lods, mk, notes, bones, col, ao_method) in built.items():
        bpy.context.view_layer.update()
        b0 = kit.bounds_mm([lods[0]])
        # shoulder width: X extent of vertices in the shoulder band
        P = VARIANTS[variant]
        sh_z = resident_layout(P)["sh_z"]
        xs = [v.co.x / MM for v in lods[0].data.vertices if abs(v.co.z / MM - sh_z) <= 1.5]
        shoulder = round(max(xs) - min(xs), 3) if xs else None
        meta = {"variant": variant,
                "pivot": "ground contact: root at z=0 between the feet (or wheel contacts); faces -Y",
                "height_mm": b0["size"][2], "shoulder_width_mm_at_shoulder_band": shoulder,
                "rig": {"bones": bones, "bone_count": len(bones), "weights": "rigid, 1 bone per vertex",
                        "note": "parts are separate rigid pieces skinned 100% to one bone each, so idle/walk/"
                                "wave/celebrate are transform-only clips"},
                "lod_notes": notes, "ao_bake": ao_method,
                "palette": {k: P[k] for k in ("top", "legs", "skin", "hair")}}
        checks_extra = {}
        if variant == "adult":
            checks_extra["height_34mm_pm1pct"] = abs(b0["size"][2] - 34.0) <= 0.34
            checks_extra["shoulder_12mm_pm1pct"] = shoulder is not None and abs(shoulder - 12.0) <= 0.12
        checks_extra["feet_not_below_ground"] = b0["min"][2] >= -0.001
        fbx_name = "resident" if variant == "adult" else "resident_" + variant
        doc, fbx = publish("resident_" + variant, rig, lods, mk, [col], meta, None, folder="resident",
                           fbx_name=fbx_name, save=False, budget_key="resident")
        doc["checks"].update(checks_extra)
        doc["files"].update({"blend": "art/lookdev/resident/resident.blend",
                             "blend_sha256": kit.sha256_file(os.path.join(LOOK, "resident", "resident.blend"))})
        docs[variant] = (doc, fbx, [o.name for o in lods])
        summary[variant] = {"fbx": doc["files"]["fbx"], "triangles": doc["triangles"],
                            "height_mm": meta["height_mm"], "shoulder_mm": shoulder, "checks": doc["checks"]}
    # round trips (destroy the scene; the .blend is already saved)
    out = {"schema": "resq-lookdev-asset-1", "asset": "resident", "variants": {}}
    for variant, (doc, fbx, names) in docs.items():
        rt = roundtrip(fbx, names)
        doc["blender_reimport"] = rt
        b = rt["lod_bounds_mm"].get(names[0])
        if b:
            doc["checks"]["reimport_bounds_match_0p01mm"] = max(
                abs(a - c) for a, c in zip(b["size"], doc["bounds_lod0_blender_mm"]["size"])) < 0.01
        out["variants"][variant] = doc
        print("PUBLISHED resident", variant, doc["triangles"], doc["checks"])
    kit.write_json(os.path.join(LOOK, "resident", "resident.json"), out)


# ==========================================================================
# Axis calibration arrow
# ==========================================================================
def calibrate():
    kit.reset_scene()
    mat = kit.shared_material(ATLAS)
    root = kit.empty("calib_arrow", (0, 0, 0), size_mm=10)
    b = kit.Builder("arrow")
    b.box(-2, -60, 0, 2, 0, 4, "rescue_orange")                          # shaft along -Y (forward)
    b.hull([V(-6, -60, 0), V(6, -60, 0), V(0, -80, 0), V(-6, -60, 4), V(6, -60, 4), V(0, -80, 4)],
           "rescue_orange")                                              # head at -Y 80 mm
    b.box(0, -1.5, 0, 60, 1.5, 3, "cloth_plum")                          # +X arm 60 mm
    b.box(-1.5, -1.5, 0, 1.5, 1.5, 100, "cloth_blue")                    # +Z arm 100 mm
    b.box(-50, 20, 0, 50, 30, 10, "paper")                               # 100 mm reference bar
    arrow = b.finalize("calib_arrow_mesh", mat, ao=dict(rays=4))
    arrow.parent = root
    tips = []
    for nm, p in (("calib_tip_forward", (0, -80, 2)), ("calib_tip_x", (60, 0, 1.5)),
                  ("calib_tip_up", (0, 0, 100)), ("calib_bar_a", (-50, 25, 5)), ("calib_bar_b", (50, 25, 5))):
        tips.append(kit.empty(nm, p, root))
    os.makedirs(SHARED, exist_ok=True)
    res = {"schema": "resq-axis-calibration-1",
           "evidence_kind": "automated, Blender 5.2.1 headless; FBX file inspection + Blender re-import. "
                            "Unity import NOT executed here.",
           "source": {"forward_tip_mm": [0, -80, 2], "x_tip_mm": [60, 0, 1.5], "up_tip_mm": [0, 0, 100],
                      "bar_mm": 100}}
    presets = {"A_project_calibration_preset": {},
               "B_bake_space_transform": {"bake_space_transform": True}}
    for key, over in presets.items():
        path = os.path.join(SHARED, "calibration_arrow_%s.fbx" % key[0])
        settings = kit.export_fbx(path, [root, arrow] + tips, over)
        insp = kit.fbx_inspect(path)
        res[key] = {"file": os.path.relpath(path, REPO), "overrides": over,
                    "global_settings": insp["global"], "models": insp["models"],
                    "mesh_vertex_bounds_file_units": insp["geometry_bounds_file_units"]}
    # Blender round trip of preset A
    rt = {}
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SHARED, "calibration_arrow_A.fbx"),
                             use_manual_orientation=False, global_scale=1.0)
    bpy.context.view_layer.update()
    for ob in bpy.data.objects:
        if ob.type == "EMPTY":
            rt[ob.name] = [round(c / MM, 4) for c in ob.matrix_world.translation]
    res["A_blender_reimport_world_mm"] = rt
    res["documented_unity_mapping"] = {
        "rule": "Blender (x,y,z) -> Unity (-x, z, -y)",
        "forward_tip_expected_unity_mm": kit.blender_to_unity([0, -80, 2]),
        "x_tip_expected_unity_mm": kit.blender_to_unity([60, 0, 1.5]),
        "up_tip_expected_unity_mm": kit.blender_to_unity([0, 0, 100]),
        "status": "expected from FBX GlobalSettings (Y up, right-handed) and Unity's left-handed X flip; "
                  "must be confirmed by the coordinator's Unity import (calibration measurement entry point)."}
    kit.write_json(os.path.join(SHARED, "axis_calibration.json"), res)
    print("CALIBRATION", res["A_project_calibration_preset"]["models"])


# ==========================================================================
# Main
# ==========================================================================
def main(argv):
    os.makedirs(SHARED, exist_ok=True)
    os.makedirs(LOOK, exist_ok=True)
    if "--render" in argv:
        import lookdev_render
        lookdev_render.render_all()
        return
    kit.write_palette_atlas(ATLAS, os.path.join(SHARED, "palette_atlas.json"))
    if "--calibrate" in argv:
        calibrate()
        return
    only = None
    if "--only" in argv:
        only = argv[argv.index("--only") + 1].split(",")
    for name, fn in (("cottage", make_cottage), ("bridge", make_bridge), ("skiff", make_skiff),
                     ("resident", make_residents)):
        if only is None or name in only:
            fn()


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(argv)
