"""RESQ shared Blender kit (Blender 5.2, bpy/bmesh/mathutils only).

Everything is original procedural geometry authored from numbers.

Conventions (see tools/blender/README.md):
  * Blender scene units are metres, unit scale 1.0, Z up, asset forward = -Y.
  * Geometry helpers take MILLIMETRES (asset drawings are in mm) and the
    Builder converts to metres when parts are merged.
  * One shared material "RESQ_Shared": palette atlas (UV -> role colour,
    alpha = smoothness) multiplied by a linear vertex colour "Col" that holds
    baked fake AO and per-part tone variation.
"""

import array
import hashlib
import json
import math
import os
import struct
import zlib

try:
    import bpy
    import bmesh
    from mathutils import Matrix, Vector
    from mathutils.bvhtree import BVHTree
except ImportError:  # palette/atlas functions still work in plain Python
    bpy = bmesh = Matrix = Vector = BVHTree = None

MM = 0.001
KIT_VERSION = "resq-kit-1"

# --------------------------------------------------------------------------
# Palette: role -> sRGB hex, roughness, metallic, material class
# Roughness values sit inside specification/05 section 2.2 ranges.
# --------------------------------------------------------------------------
PALETTE = [
    # role,            hex,       rough, metal, class
    ("rescue_orange",  "#FF6B35", 0.72, 0.00, "painted_metal"),
    ("metal_dark",     "#3E474C", 0.70, 0.10, "painted_metal"),
    ("rubber",         "#2B2F31", 0.92, 0.00, "rubber"),
    ("cedar",          "#A4643F", 0.82, 0.00, "wood"),
    ("wood_deck",      "#B79B74", 0.84, 0.00, "wood"),
    ("trim_paint",     "#EDE3CD", 0.80, 0.00, "painted_wood"),
    ("door_paint",     "#355D47", 0.78, 0.00, "painted_wood"),
    ("window_glass",   "#2B4249", 0.45, 0.00, "glass"),
    ("roof_tile",      "#C56646", 0.86, 0.00, "roof_tile"),
    ("stone",          "#D8CCAF", 0.92, 0.00, "stone"),
    ("foliage",        "#355D47", 0.90, 0.00, "foliage"),
    ("grass",          "#5E8456", 0.95, 0.00, "ground"),
    ("soil",           "#7B5B42", 0.95, 0.00, "ground"),
    ("water",          "#438B9B", 0.42, 0.00, "water"),
    ("hull_paint",     "#E8E0CF", 0.74, 0.00, "painted_metal"),
    ("paper",          "#F3EBDD", 0.85, 0.00, "paper"),
    ("evergreen",      "#132B2A", 0.82, 0.00, "cloth"),
    ("mineral",        "#D9E4DB", 0.85, 0.00, "painted"),
    ("cloth_blue",     "#4E6A8C", 0.92, 0.00, "cloth"),
    ("cloth_mustard",  "#D3A145", 0.92, 0.00, "cloth"),
    ("cloth_sage",     "#8EA382", 0.94, 0.00, "cloth"),
    ("cloth_plum",     "#7A566B", 0.92, 0.00, "cloth"),
    ("cloth_slate",    "#4D5961", 0.94, 0.00, "cloth"),
    ("cloth_cream",    "#E6DAC2", 0.94, 0.00, "cloth"),
    ("skin_a",         "#EBC19E", 0.80, 0.00, "skin"),
    ("skin_b",         "#C28A63", 0.80, 0.00, "skin"),
    ("skin_c",         "#8A5A3E", 0.80, 0.00, "skin"),
    ("hair_dark",      "#3A2C27", 0.90, 0.00, "hair"),
    ("hair_grey",      "#C3BDB1", 0.90, 0.00, "hair"),
    ("hair_auburn",    "#7A3F2A", 0.90, 0.00, "hair"),
]
ROLE_INDEX = {r[0]: i for i, r in enumerate(PALETTE)}
ATLAS_W, ATLAS_H, ATLAS_CELL = 256, 16, 8
assert len(PALETTE) <= ATLAS_W // ATLAS_CELL


def hex_to_srgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def role_uv(role):
    i = ROLE_INDEX[role]
    return ((i * ATLAS_CELL + ATLAS_CELL / 2) / ATLAS_W, 0.5)


def write_png(path, width, height, rgba_bytes_top_down):
    """Minimal RGBA8 PNG writer (pure Python). rows are top-down."""
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)
        raw.extend(rgba_bytes_top_down[y * stride:(y + 1) * stride])

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def write_palette_atlas(png_path, json_path):
    """256x16 RGBA atlas: one 8 px column block per role.
    RGB = sRGB base colour, A = smoothness (1 - roughness), linear."""
    px = bytearray(ATLAS_W * ATLAS_H * 4)
    for x in range(ATLAS_W):
        i = x // ATLAS_CELL
        if i < len(PALETTE):
            _, hx, rough, _, _ = PALETTE[i]
            r, g, b = (round(c * 255) for c in hex_to_srgb(hx))
            a = round((1.0 - rough) * 255)
        else:
            r, g, b, a = 255, 0, 255, 255  # unused cells: loud magenta
        for y in range(ATLAS_H):
            o = (y * ATLAS_W + x) * 4
            px[o:o + 4] = bytes((r, g, b, a))
    write_png(png_path, ATLAS_W, ATLAS_H, px)
    roles = {}
    for i, (role, hx, rough, metal, cls) in enumerate(PALETTE):
        u, v = role_uv(role)
        roles[role] = {
            "index": i, "hex_srgb": hx, "roughness": rough,
            "smoothness_alpha": round(1.0 - rough, 4), "metallic": metal,
            "class": cls, "uv": [u, v],
            "pixel_rect": [i * ATLAS_CELL, 0, ATLAS_CELL, ATLAS_H],
            "linear_rgb": [round(srgb_to_linear(c), 5) for c in hex_to_srgb(hx)],
        }
    doc = {
        "schema": "resq-palette-atlas-1", "kit_version": KIT_VERSION,
        "file": os.path.basename(png_path), "size": [ATLAS_W, ATLAS_H],
        "cell_width_px": ATLAS_CELL,
        "channels": {"rgb": "base colour, sRGB", "a": "smoothness = 1 - roughness, linear"},
        "uv_rule": "u = (index*8 + 4)/256, v = 0.5; every corner of a face uses its role's centre UV",
        "unity_import": {"sRGB": True, "alphaIsTransparency": False, "filterMode": "Point",
                         "mipmaps": False, "wrapMode": "Clamp", "compression": "None",
                         "note": "point/no-mip keeps neighbouring roles from bleeding"},
        "metallic_note": "All atlas roles are authored for metallic 0 in Unity; metal_dark's 0.10 is within the painted-metal 0-0.15 band and is not encoded.",
        "vertex_colour": "Mesh colour attribute 'Col' (linear float, exported LINEAR) = tone * baked AO; shader multiplies albedo by it.",
        "roles": roles,
        "png_sha256": sha256_file(png_path),
    }
    with open(json_path, "w") as f:
        json.dump(doc, f, indent=2)
    return doc


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# --------------------------------------------------------------------------
# Scene / material
# --------------------------------------------------------------------------
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    sc.unit_settings.length_unit = "MILLIMETERS"
    return sc


def shared_material(atlas_path):
    mat = bpy.data.materials.get("RESQ_Shared")
    if mat:
        return mat
    img = bpy.data.images.load(atlas_path, check_existing=True)
    img.colorspace_settings.name = "sRGB"
    img.alpha_mode = "CHANNEL_PACKED"
    mat = bpy.data.materials.new("RESQ_Shared")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    tex.extension = "EXTEND"
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    mul = nt.nodes.new("ShaderNodeVectorMath")
    mul.operation = "MULTIPLY"
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(tex.outputs["Color"], mul.inputs[0])
    nt.links.new(vc.outputs["Color"], mul.inputs[1])
    nt.links.new(mul.outputs["Vector"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    bsdf.inputs["Metallic"].default_value = 0.0
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.4
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    for i, n in enumerate((tex, vc, mul, inv, bsdf, out)):
        n.location = (i * 220 - 600, 0)
    return mat


# --------------------------------------------------------------------------
# Temporary primitive bmeshes, in millimetres
# --------------------------------------------------------------------------
def V(*a):
    return Vector(a[0]) if len(a) == 1 else Vector(a)


def tmp_box(x0, y0, z0, x1, y1, z1):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    c = V((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    s = V(abs(x1 - x0), abs(y1 - y0), abs(z1 - z0))
    for v in bm.verts:
        v.co = V(c.x + v.co.x * s.x, c.y + v.co.y * s.y, c.z + v.co.z * s.z)
    return bm


def tmp_hull(points):
    """Closed convex solid through points, coplanar triangles merged."""
    bm = bmesh.new()
    seen = []
    for p in points:
        p = V(p)
        if all((p - q).length > 1e-6 for q in seen):
            seen.append(p)
    vs = [bm.verts.new(p) for p in seen]
    res = bmesh.ops.convex_hull(bm, input=vs)
    kill = [g for g in res["geom_interior"] + res["geom_unused"]
            if isinstance(g, bmesh.types.BMVert)]
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.5),
                             verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def _frame(d):
    d = d.normalized()
    ref = V(0, 0, 1) if abs(d.z) < 0.9 else V(1, 0, 0)
    e1 = ref.cross(d).normalized()
    e2 = d.cross(e1).normalized()
    return e1, e2


def tmp_cyl(p0, p1, r, seg=12, r1=None, caps=True):
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    rot = V(0, 0, 1).rotation_difference(d.normalized()).to_matrix().to_4x4()
    mat = Matrix.Translation((p0 + p1) / 2) @ rot
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg,
                          radius1=r, radius2=(r if r1 is None else r1),
                          depth=d.length, matrix=mat)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def tmp_sphere(c, r, useg=12, vseg=8, scale=(1, 1, 1)):
    bm = bmesh.new()
    mat = Matrix.Translation(V(c)) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    bmesh.ops.create_uvsphere(bm, u_segments=useg, v_segments=vseg, radius=r, matrix=mat)
    return bm


def tmp_lathe(p0, p1, profile, seg=10, phase=0.0):
    """Surface of revolution along p0->p1. profile: [(t 0..1, rx, ry)], rx along
    the frame's e1 (world X for vertical axes), ry along e2. rx=ry=0 -> pole."""
    p0, p1 = V(p0), V(p1)
    d = p1 - p0
    if abs(d.normalized().z) > 0.9:
        e1, e2 = V(1, 0, 0), V(0, 1, 0) if d.z > 0 else V(0, -1, 0)
    else:
        e1, e2 = _frame(d)
    bm = bmesh.new()
    rings = []
    for t, rx, ry in profile:
        c = p0 + d * t
        if rx < 1e-6 and ry < 1e-6:
            rings.append([bm.verts.new(c)])
        else:
            ring = []
            for i in range(seg):
                a = phase + 2 * math.pi * i / seg
                ring.append(bm.verts.new(c + e1 * (rx * math.cos(a)) + e2 * (ry * math.sin(a))))
            rings.append(ring)
    _bridge_rings(bm, rings, closed=True)
    for idx in (0, len(rings) - 1):
        if len(rings[idx]) > 2:
            bm.faces.new(rings[idx] if idx else list(reversed(rings[idx])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def _bridge_rings(bm, rings, closed=True):
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1 or len(b) == 1:
            pole, ring = (a[0], b) if len(a) == 1 else (b[0], a)
            n = len(ring)
            rng = range(n) if closed else range(n - 1)
            for i in rng:
                try:
                    bm.faces.new((pole, ring[i], ring[(i + 1) % n]))
                except ValueError:
                    pass
            continue
        n = len(a)
        rng = range(n) if closed else range(n - 1)
        for i in rng:
            j = (i + 1) % n
            try:
                bm.faces.new((a[i], a[j], b[j], b[i]))
            except ValueError:
                pass


def tmp_loft(sections, closed_ring=True, cap0=True, cap1=True):
    """Loft equal-length point lists (mm). Caps are ngons (may be concave;
    they are triangulated by Builder.finalize)."""
    bm = bmesh.new()
    rings = [[bm.verts.new(V(p)) for p in s] for s in sections]
    _bridge_rings(bm, rings, closed=closed_ring)
    if cap0:
        bm.faces.new(list(reversed(rings[0])))
    if cap1:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def tmp_sweep(path, radius, seg=8, closed=False, radii=None):
    """Tube of circular section along a polyline path (mm)."""
    path = [V(p) for p in path]
    n = len(path)
    bm = bmesh.new()
    rings = []
    for i, p in enumerate(path):
        if closed:
            t = path[(i + 1) % n] - path[i - 1]
        else:
            t = path[min(i + 1, n - 1)] - path[max(i - 1, 0)]
        t.normalize()
        side = V(0, 0, 1).cross(t).normalized()
        up = t.cross(side).normalized()
        r = radii[i] if radii else radius
        rings.append([bm.verts.new(p + side * (r * math.cos(2 * math.pi * k / seg))
                                   + up * (r * math.sin(2 * math.pi * k / seg)))
                      for k in range(seg)])
    if closed:
        rings.append(rings[0])
    _bridge_rings(bm, rings, closed=True)
    if not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


def bevel(bm, width, segments=1, edges=None):
    if width <= 0:
        return bm
    geom = edges if edges is not None else bm.edges[:]
    bmesh.ops.bevel(bm, geom=geom, offset=width, offset_type="OFFSET",
                    segments=segments, profile=0.5, affect="EDGES",
                    clamp_overlap=True)
    bm.normal_update()
    return bm


def transform(bm, mat):
    bmesh.ops.transform(bm, matrix=mat, verts=bm.verts[:])
    return bm


# --------------------------------------------------------------------------
# Builder: merges parts, carries role / tone / bone per face
# --------------------------------------------------------------------------
class Builder:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        fl = self.bm.faces.layers
        self.l_role = fl.int.new("role")
        self.l_tone = fl.float.new("tone")
        self.l_bone = fl.int.new("bone")
        self.parts = 0

    def add(self, tmp, role, tone=1.0, bone=0, smooth=False, bev=0.0, bev_seg=1,
            xform=None):
        """role / tone may be callables taking (face_center_mm, face_normal)."""
        if bev > 0:
            bevel(tmp, bev, bev_seg)
        if xform is not None:
            transform(tmp, xform)
        tmp.normal_update()
        vmap = {v: self.bm.verts.new(v.co * MM) for v in tmp.verts}
        for f in tmp.faces:
            try:
                nf = self.bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue
            c = f.calc_center_median()
            r = role(c, f.normal) if callable(role) else role
            t = tone(c, f.normal) if callable(tone) else tone
            nf[self.l_role] = ROLE_INDEX[r]
            nf[self.l_tone] = t
            nf[self.l_bone] = bone
            nf.smooth = smooth
        tmp.free()
        self.parts += 1

    def box(self, x0, y0, z0, x1, y1, z1, role, **kw):
        self.add(tmp_box(x0, y0, z0, x1, y1, z1), role, **kw)

    def hull(self, pts, role, **kw):
        self.add(tmp_hull(pts), role, **kw)

    def finalize(self, name, material, ao=None, sharp_angle=48.0):
        """Triangulate, bake AO + tone into 'Col', write UVs, return object."""
        bm = self.bm
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
        # remove degenerate faces
        bad = [f for f in bm.faces if f.calc_area() < 1e-14]
        if bad:
            bmesh.ops.delete(bm, geom=bad, context="FACES_ONLY")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.normal_update()
        thr = math.radians(sharp_angle)
        for e in bm.edges:
            lf = e.link_faces
            if len(lf) != 2:
                e.smooth = False
                continue
            f1, f2 = lf
            e.smooth = bool(f1.smooth and f2.smooth and f1.normal.angle(f2.normal, 0.0) < thr)
        uv = bm.loops.layers.uv.new("UVMap")
        col = bm.loops.layers.float_color.new("Col")
        aoval = corner_ao(bm, **(ao or {}))
        for f in bm.faces:
            u, v = role_uv(PALETTE[f[self.l_role]][0])
            tone = f[self.l_tone]
            for k, lp in enumerate(f.loops):
                lp[uv].uv = (u, v)
                a = aoval.get((f.index, k), 1.0)
                c = max(0.0, min(1.0, tone * a))
                lp[col] = (c, c, c, 1.0)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        me.color_attributes.active_color_name = "Col"
        try:
            me.color_attributes.render_color_index = me.color_attributes.find("Col")
        except Exception:
            pass
        me.materials.append(material)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        roles = {}
        for f in bm.faces:
            rn = PALETTE[f[self.l_role]][0]
            roles[rn] = roles.get(rn, 0) + 1
        ob["resq_roles"] = json.dumps(roles, sort_keys=True)
        bm.free()
        self.bm = None
        return ob


# --------------------------------------------------------------------------
# Fake ambient occlusion baked per face corner
# --------------------------------------------------------------------------
def _hemi_dirs(n):
    ga = math.pi * (3 - math.sqrt(5))
    out = []
    for i in range(n):
        u = (i + 0.5) / n
        r = math.sqrt(u)
        a = i * ga
        out.append((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1 - u))))
    return out


def corner_ao(bm, rays=20, dist_mm=10.0, strength=0.85, floor=0.38,
              ground_z_mm=0.0, use_ground=True, height_mm=None, height_dark=0.0,
              extra_bvh=None):
    """Ray-cast hemisphere AO per corner against the asset itself plus an
    optional ground plane (contact darkening) and an optional height gradient.
    Returns {(face_index, corner_position_in_face): value}."""
    bm.faces.ensure_lookup_table()
    bm.verts.index_update()
    bm.faces.index_update()
    bvh = BVHTree.FromBMesh(bm)
    D = dist_mm * MM
    gz = ground_z_mm * MM
    dirs = _hemi_dirs(rays)
    eps = 0.02 * MM
    out = {}
    for f in bm.faces:
        n = f.normal
        if n.length < 0.5:
            continue
        e1, e2 = _frame(n)
        wd = [e1 * d[0] + e2 * d[1] + n * d[2] for d in dirs]
        c = f.calc_center_median()
        for k, lp in enumerate(f.loops):
            p = lp.vert.co
            o = p + (c - p) * 0.2 + n * eps
            occ = 0.0
            for d in wd:
                hit = bvh.ray_cast(o, d, D)
                best = hit[3] if hit[0] is not None else None
                if extra_bvh is not None:
                    h2 = extra_bvh.ray_cast(o, d, D)
                    if h2[0] is not None and (best is None or h2[3] < best):
                        best = h2[3]
                if use_ground and d.z < -1e-6:
                    t = (gz - o.z) / d.z
                    if 0 <= t < D and (best is None or t < best):
                        best = t
                if best is not None:
                    occ += 1.0 - best / D
            a = 1.0 - strength * occ / len(wd)
            if height_mm and height_dark > 0:
                h = max(0.0, min(1.0, (o.z - gz) / (height_mm * MM)))
                a *= 1.0 - height_dark * (1.0 - h) ** 2
            out[(f.index, k)] = max(floor, min(1.0, a))
    return out


# --------------------------------------------------------------------------
# Objects, markers, armature, LOD, measurement
# --------------------------------------------------------------------------
def empty(name, loc_mm=(0, 0, 0), parent=None, size_mm=3.0, kind="PLAIN_AXES"):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = kind
    ob.empty_display_size = size_mm * MM
    ob.location = V(loc_mm) * MM
    bpy.context.scene.collection.objects.link(ob)
    if parent:
        ob.parent = parent
    return ob


def role_counts(ob):
    me = ob.data
    attr = me.attributes.get("role")
    out = {}
    if attr is None:
        return out
    for d in attr.data:
        rn = PALETTE[d.value][0]
        out[rn] = out.get(rn, 0) + 1
    return dict(sorted(out.items()))


def tri_count(ob):
    me = ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def bounds_mm(obs):
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for ob in obs:
        mw = ob.matrix_world
        for v in ob.data.vertices:
            w = mw @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i] / MM)
                hi[i] = max(hi[i], w[i] / MM)
    return {"min": [round(x, 3) for x in lo], "max": [round(x, 3) for x in hi],
            "size": [round(hi[i] - lo[i], 3) for i in range(3)]}


def decimate_copy(ob, ratio, name):
    """Collapse-decimate a copy of ob (generic fallback LOD tool)."""
    cp = ob.copy()
    cp.data = ob.data.copy()
    cp.name = name
    cp.data.name = name
    bpy.context.scene.collection.objects.link(cp)
    mod = cp.modifiers.new("lod", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = cp.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    cp.modifiers.clear()
    old = cp.data
    cp.data = me
    me.name = name
    bpy.data.meshes.remove(old)
    return cp


def make_armature(name, bones, parent=None):
    """bones: [(name, head_mm, tail_mm, parent_name or None)]."""
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(ob)
    if parent:
        ob.parent = parent
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    ebs = {}
    for bn, h, t, par in bones:
        eb = arm.edit_bones.new(bn)
        eb.head = V(h) * MM
        eb.tail = V(t) * MM
        eb.roll = 0.0
        if par:
            eb.parent = ebs[par]
        ebs[bn] = eb
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def rigid_skin(mesh_ob, arm_ob, bone_names):
    """Assign each vertex 100% to the bone of the part it belongs to, read from
    the per-face 'bone' attribute written by Builder (survives decimation)."""
    groups = {bn: mesh_ob.vertex_groups.new(name=bn) for bn in bone_names}
    me = mesh_ob.data
    attr = me.attributes["bone"]
    vb = {}
    for poly in me.polygons:
        b = attr.data[poly.index].value
        for vi in poly.vertices:
            vb[vi] = b
    buckets = {}
    for vi, b in vb.items():
        buckets.setdefault(b, []).append(vi)
    for b, vis in buckets.items():
        groups[bone_names[b]].add(vis, 1.0, "REPLACE")
    mod = mesh_ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_ob
    mesh_ob.parent = arm_ob


# --------------------------------------------------------------------------
# FBX export + inspection
# --------------------------------------------------------------------------
# Same axis/unit preset as the project calibration (art/calibration, attempt 03),
# plus mesh/colour options. See README for the verification record.
FBX_PRESET = dict(
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", use_space_transform=True, bake_space_transform=False,
    object_types={"MESH", "EMPTY", "ARMATURE"}, use_selection=True,
    use_mesh_modifiers=False, mesh_smooth_type="OFF", use_triangles=True,
    use_tspace=False, colors_type="LINEAR", prioritize_active_color=True,
    add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    armature_nodetype="NULL", bake_anim=False, use_custom_props=True,
    path_mode="STRIP", embed_textures=False,
)


def export_fbx(path, objects, preset=None):
    p = dict(FBX_PRESET)
    if preset:
        p.update(preset)
    for ob in bpy.context.scene.objects:
        ob.select_set(False)
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    st = bpy.ops.export_scene.fbx(filepath=path, check_existing=False, **p)
    if st != {"FINISHED"} or not os.path.isfile(path):
        raise RuntimeError("FBX export failed: %s" % path)
    return {k: (sorted(v) if isinstance(v, set) else v) for k, v in p.items()}


def _props70(elem):
    out = {}
    for sub in elem.elems:
        if sub.id == b"Properties70":
            for p in sub.elems:
                name = p.props[0].decode("utf-8", "replace")
                out[name] = list(p.props[4:])
    return out


def fbx_inspect(path):
    """Read the binary FBX back with Blender's bundled parser (file-level facts)."""
    from io_scene_fbx import parse_fbx
    root, version = parse_fbx.parse(path)
    info = {"fbx_version": version, "global": {}, "models": {}, "geometry_bounds_file_units": {}}
    objs = None
    conns = []
    for e in root.elems:
        if e.id == b"GlobalSettings":
            g = _props70(e)
            for k in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis",
                      "CoordAxisSign", "UnitScaleFactor", "OriginalUnitScaleFactor"):
                if k in g:
                    info["global"][k] = g[k][0]
        elif e.id == b"Objects":
            objs = e
        elif e.id == b"Connections":
            conns = [(c.props[1], c.props[2]) for c in e.elems if c.id == b"C"]
    geoms = {}
    if objs:
        for e in objs.elems:
            if e.id == b"Model":
                nm = e.props[1].split(b"\x00")[0].decode()
                pr = _props70(e)
                info["models"][nm] = {
                    "uid": e.props[0], "type": e.props[2].decode(),
                    "lcl_translation": [round(x, 6) for x in pr.get("Lcl Translation", [0, 0, 0])],
                    "lcl_rotation": [round(x, 6) for x in pr.get("Lcl Rotation", [0, 0, 0])],
                    "lcl_scaling": [round(x, 6) for x in pr.get("Lcl Scaling", [1, 1, 1])],
                }
            elif e.id == b"Geometry":
                for sub in e.elems:
                    if sub.id == b"Vertices":
                        vs = sub.props[0]
                        xs, ys, zs = vs[0::3], vs[1::3], vs[2::3]
                        geoms[e.props[0]] = {"min": [min(xs), min(ys), min(zs)],
                                             "max": [max(xs), max(ys), max(zs)],
                                             "count": len(xs)}
    uid2name = {m["uid"]: n for n, m in info["models"].items()}
    parents = {}
    for child, par in conns:
        if child in geoms and par in uid2name:
            info["geometry_bounds_file_units"][uid2name[par]] = geoms[child]
        if child in uid2name:
            parents[uid2name[child]] = uid2name.get(par, "<scene root>")
    for n in info["models"]:
        info["models"][n]["parent"] = parents.get(n)
        info["models"][n].pop("uid")
    return info


def blender_to_unity(p):
    """Documented mapping for this preset: Blender (x, y, z) -> Unity (-x, z, -y).
    Blender -Y forward -> Unity +Z forward; Blender +Z up -> Unity +Y up;
    Blender +X -> Unity -X (pure axis relabelling between a right-handed and a
    left-handed frame; geometry is not mirrored)."""
    return [round(-p[0], 3), round(p[2], 3), round(-p[1], 3)]


def save_blend(path):
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True, relative_remap=True)


def write_json(path, doc):
    with open(path, "w") as f:
        json.dump(doc, f, indent=2, sort_keys=False)


# --------------------------------------------------------------------------
# Higher-level construction helpers (all millimetres)
# --------------------------------------------------------------------------
class Wall:
    """Local wall frame: u along the wall (left->right seen from outside),
    v up, d outward from the core face."""

    def __init__(self, origin, u_axis, out_axis):
        self.o = V(origin)
        self.u = V(u_axis).normalized()
        self.n = V(out_axis).normalized()
        self.v = V(0, 0, 1)

    def p(self, u, v, d=0.0):
        return self.o + self.u * u + self.v * v + self.n * d

    def box_pts(self, u0, u1, v0, v1, d0, d1):
        return [self.p(u, v, d) for u in (u0, u1) for v in (v0, v1) for d in (d0, d1)]


def wall_box(b, wall, u0, u1, v0, v1, d0, d1, role, **kw):
    b.hull(wall.box_pts(u0, u1, v0, v1, d0, d1), role, **kw)


def _subtract_spans(lo, hi, holes):
    spans = [(lo, hi)]
    for h0, h1 in sorted(holes):
        nxt = []
        for s0, s1 in spans:
            if h1 <= s0 or h0 >= s1:
                nxt.append((s0, s1))
                continue
            if h0 > s0:
                nxt.append((s0, h0))
            if h1 < s1:
                nxt.append((h1, s1))
        spans = nxt
    return [s for s in spans if s[1] - s[0] > 0.4]


def lap_siding(b, wall, u0, u1, v0, v1, courses, openings, role="cedar",
               thick_bottom=1.3, thick_top=0.35, tone_seed=0):
    """Horizontal lap-siding courses (wedge profile gives real shadow lines).
    openings: [(u0, u1, v0, v1)] outer extents of frames to skip."""
    h = (v1 - v0) / courses
    for k in range(courses):
        c0, c1 = v0 + k * h, v0 + (k + 1) * h
        holes = [(o[0], o[1]) for o in openings if o[2] < c1 - 1e-6 and o[3] > c0 + 1e-6]
        for j, (s0, s1) in enumerate(_subtract_spans(u0, u1, holes)):
            pts = [wall.p(s0, c0, 0), wall.p(s1, c0, 0), wall.p(s0, c1, 0), wall.p(s1, c1, 0),
                   wall.p(s0, c0, thick_bottom), wall.p(s1, c0, thick_bottom),
                   wall.p(s0, c1, thick_top), wall.p(s1, c1, thick_top)]
            t = 0.90 + 0.10 * ((k * 7 + j * 3 + tone_seed * 5) % 5) / 4.0
            b.hull(pts, role, tone=t)


def window(b, wall, uc, vc, w, h, frame=1.6, proud=1.8, muntins=True, sill=True,
           frame_role="trim_paint", glass_role="window_glass", glass_depth=0.3):
    """Recessed window: glass on the core face, frame and sill stand proud.
    Returns outer opening extents (u0, u1, v0, v1) for siding cut-outs."""
    u0, u1, v0, v1 = uc - w / 2, uc + w / 2, vc - h / 2, vc + h / 2
    wall_box(b, wall, u0, u1, v0, v1, 0.0, glass_depth, glass_role, tone=0.9)
    F = frame
    wall_box(b, wall, u0 - F, u1 + F, v1, v1 + F, 0, proud, frame_role)          # head
    wall_box(b, wall, u0 - F, u0, v0, v1, 0, proud, frame_role)                  # jamb L
    wall_box(b, wall, u1, u1 + F, v0, v1, 0, proud, frame_role)                  # jamb R
    if sill:
        wall_box(b, wall, u0 - F - 0.8, u1 + F + 0.8, v0 - F, v0, 0, proud + 0.9,
                 frame_role, tone=0.95)
    else:
        wall_box(b, wall, u0 - F, u1 + F, v0 - F, v0, 0, proud, frame_role)
    if muntins:
        m = 0.55
        wall_box(b, wall, uc - m, uc + m, v0, v1, 0, glass_depth + 0.6, frame_role)
        wall_box(b, wall, u0, u1, vc - m, vc + m, 0, glass_depth + 0.6, frame_role)
    return (u0 - F - (0.8 if sill else 0), u1 + F + (0.8 if sill else 0), v0 - F, v1 + F)


def door(b, wall, uc, w, h, v0=0.0, frame=1.6, proud=1.8, boards=3,
         door_role="door_paint", frame_role="trim_paint", knob=True):
    """Recessed plank door; returns outer extents for siding cut-outs."""
    u0, u1 = uc - w / 2, uc + w / 2
    wall_box(b, wall, u0, u1, v0, v0 + h, 0.0, 0.3, door_role, tone=0.7)
    if boards:
        bw = w / boards
        for i in range(boards):
            t = (0.96, 1.0, 0.92, 0.98)[i % 4]
            wall_box(b, wall, u0 + i * bw + 0.15, u0 + (i + 1) * bw - 0.15, v0, v0 + h - 0.2,
                     0.3, 0.75, door_role, tone=t)
        wall_box(b, wall, u0 + 0.4, u1 - 0.4, v0 + h * 0.62, v0 + h * 0.62 + 1.6, 0.75, 1.05,
                 door_role, tone=0.85)
        wall_box(b, wall, u0 + 0.4, u1 - 0.4, v0 + h * 0.2, v0 + h * 0.2 + 1.6, 0.75, 1.05,
                 door_role, tone=0.85)
    F = frame
    wall_box(b, wall, u0 - F, u1 + F, v0 + h, v0 + h + F, 0, proud, frame_role)
    wall_box(b, wall, u0 - F, u0, v0, v0 + h, 0, proud, frame_role)
    wall_box(b, wall, u1, u1 + F, v0, v0 + h, 0, proud, frame_role)
    if knob:
        p = wall.p(u1 - 2.2, v0 + h * 0.48, 1.45)
        b.add(tmp_sphere(p, 0.9, 6, 4), "metal_dark", smooth=True)
    return (u0 - F, u1 + F, v0, v0 + h + F)


def stepped_roof_slope(b, x0, x1, y_eave, y_ridge, z_of_y, thickness_v, courses,
                       step=0.9, role="roof_tile", bev=0.0, tone_seed=0):
    """One roof plane split into shingle courses with a saw-tooth step.
    z_of_y gives the roof underside height. All end faces are vertical, so the
    eave line is exactly at y_eave (bounds stay exact)."""
    ys = [y_eave + (y_ridge - y_eave) * k / courses for k in range(courses + 1)]
    for k in range(courses):
        ya, yb = ys[k], ys[k + 1]
        za, zb = z_of_y(ya), z_of_y(yb)
        ta = thickness_v + (step if k < courses - 1 or courses == 1 else step * 0.5)
        tb = thickness_v
        pts = []
        for x in (x0, x1):
            pts += [V(x, ya, za), V(x, yb, zb), V(x, ya, za + ta), V(x, yb, zb + tb)]
        t = (1.0, 0.93, 0.97, 0.9, 1.02)[(k + tone_seed) % 5]
        b.hull(pts, role, tone=t, bev=bev)


def planks(b, axis, start, end, count, gap, cross0, cross1, z0, z1, role="wood_deck",
           bev=0.0, tone_seed=0):
    """Row of plank boxes along axis 'x' or 'y' between start and end."""
    pitch = (end - start) / count
    for i in range(count):
        a0 = start + i * pitch + gap / 2
        a1 = start + (i + 1) * pitch - gap / 2
        t = (1.0, 0.92, 0.97, 0.88, 1.03, 0.95)[(i * 5 + tone_seed) % 6]
        if axis == "x":
            b.box(a0, cross0, z0, a1, cross1, z1, role, tone=t, bev=bev)
        else:
            b.box(cross0, a0, z0, cross1, a1, z1, role, tone=t, bev=bev)


def rail(b, p0, p1, w, h, role, bev=0.0, **kw):
    """Rectangular bar from p0 to p1 (centre line), width w, height h."""
    p0, p1 = V(p0), V(p1)
    d = (p1 - p0)
    e1, e2 = _frame(d)
    if abs(d.normalized().z) < 0.9:
        e2 = V(0, 0, 1)
        e1 = d.normalized().cross(e2).normalized()
    pts = []
    for p in (p0, p1):
        for s1 in (-1, 1):
            for s2 in (-1, 1):
                pts.append(p + e1 * (s1 * w / 2) + e2 * (s2 * h / 2))
    b.hull(pts, role, bev=bev, **kw)


def cutaway_terrain(b, x0, x1, y0, y1, nx, ny, height_fn, base_z=0.0,
                    top_role_fn=None, tone_fn=None, grass_band=2.5, rock_band=7.0):
    """Board tile: height-field top plus cutaway side walls showing strata
    (grass band following the surface, soil, horizontal rock base)."""
    bm = bmesh.new()
    xs = [x0 + (x1 - x0) * i / nx for i in range(nx + 1)]
    ys = [y0 + (y1 - y0) * j / ny for j in range(ny + 1)]
    grid = [[bm.verts.new(V(x, y, height_fn(x, y))) for x in xs] for y in ys]
    for j in range(ny):
        for i in range(nx):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()

    def top_role(c, n):
        return top_role_fn(c, n) if top_role_fn else "grass"

    def top_tone(c, n):
        return tone_fn(c, n) if tone_fn else 1.0

    b.add(bm, top_role, tone=top_tone)

    # side walls: boundary polyline, each column split into strata
    edges = []
    edges.append([(x, y0) for x in xs])
    edges.append([(x1, y) for y in ys])
    edges.append([(x, y1) for x in reversed(xs)])
    edges.append([(x0, y) for y in reversed(ys)])
    rock_top = base_z + rock_band
    for poly in edges:
        for (ax, ay), (bx, by) in zip(poly, poly[1:]):
            za, zb = height_fn(ax, ay), height_fn(bx, by)
            ga, gb = za - grass_band, zb - grass_band
            bands = [
                ("grass", za, zb, ga, gb, 0.82),
                ("soil", ga, gb, min(ga, rock_top), min(gb, rock_top), 1.0),
                ("stone", min(ga, rock_top), min(gb, rock_top), base_z, base_z, 0.78),
            ]
            for role, ta, tb, la, lb, tone in bands:
                ta, tb = max(ta, la), max(tb, lb)
                if ta - la < 0.05 and tb - lb < 0.05:
                    continue
                sm = bmesh.new()
                q = [sm.verts.new(V(ax, ay, la)), sm.verts.new(V(bx, by, lb)),
                     sm.verts.new(V(bx, by, tb)), sm.verts.new(V(ax, ay, ta))]
                try:
                    sm.faces.new(q)
                except ValueError:
                    sm.free()
                    continue
                sm.normal_update()
                cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
                f = sm.faces[0]
                mid = f.calc_center_median()
                if f.normal.dot(V(mid.x - cx, mid.y - cy, 0)) < 0:
                    f.normal_flip()
                b.add(sm, role, tone=tone)
    # bottom
    sm = bmesh.new()
    q = [sm.verts.new(V(x, y, base_z)) for x, y in ((x0, y0), (x0, y1), (x1, y1), (x1, y0))]
    sm.faces.new(q)
    sm.normal_update()
    if sm.faces[0].normal.z > 0:
        sm.faces[0].normal_flip()
    b.add(sm, "stone", tone=0.6)
