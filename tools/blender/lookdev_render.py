"""Look-dev contact sheet: renders the EXPORTED FBX files (re-imported) on a
small Alder Valley diorama tile with EEVEE. Called from lookdev.py --render.

Lighting/material are kept close to the planned Unity setup: one sun with a
shadow, flat ambient, one shared material (atlas x vertex colour), no GI, no
ray tracing, no post effects.
"""

import array
import math
import os
import shutil
import sys
import tempfile

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kit  # noqa: E402
from kit import MM, V  # noqa: E402

REPO = os.path.dirname(os.path.dirname(HERE))
LOOK = os.path.join(REPO, "art", "lookdev")
ATLAS = os.path.join(REPO, "art", "shared", "palette_atlas.png")

BANK = 32.0
RIVER_X = 55.0


def river_cx(y):
    return RIVER_X + 3.0 * math.sin(y / 38.0)


def height(x, y):
    d = abs(x - river_cx(y))
    if d >= 50.0:
        return BANK
    if d >= 38.0:
        t = (d - 38.0) / 12.0
        return 20.0 + 12.0 * (t * t * (3 - 2 * t)) ** 0.85
    if d >= 30.0:
        return 12.0 + 8.0 * (d - 30.0) / 8.0
    return 12.0


def _hash(x, y):
    v = math.sin(x * 12.9898 + y * 78.233) * 43758.5453
    return v - math.floor(v)


def build_tile(mat):
    b = kit.Builder("tile")

    def role(c, n):
        if c.z >= BANK - 0.05:
            return "grass"
        if c.z >= 20.2:
            return "soil"
        return "stone"

    def tone(c, n):
        if c.z >= BANK - 0.05:
            # broad mottling + fine variation, worn path toward the bridge
            m = 0.93 + 0.07 * math.sin(c.x / 23.0) * math.cos(c.y / 17.0) + 0.04 * (_hash(c.x, c.y) - 0.5)
            return max(0.8, min(1.0, m))
        if c.z >= 20.2:
            return 0.78 + 0.18 * (c.z - 20.0) / 12.0
        return 0.62 + 0.08 * _hash(c.x, c.y)

    kit.cutaway_terrain(b, -130.0, 130.0, -95.0, 95.0, 104, 38, height, base_z=0.0,
                        top_role_fn=role, tone_fn=tone)
    tile = b.finalize("tile", mat, ao=dict(rays=10, dist_mm=14.0, strength=0.7, use_ground=False))

    w = kit.Builder("water")
    # water slab: front/back faces inset 0.3 mm to avoid coplanar fighting
    sections = []
    for yy in [-94.7 + i * (189.4 / 19) for i in range(20)]:
        cx = river_cx(yy)
        sections.append([V(cx - 39.0, yy, 11.0), V(cx + 39.0, yy, 11.0),
                         V(cx + 39.0, yy, 20.0), V(cx - 39.0, yy, 20.0)])
    w.add(kit.tmp_loft(sections, closed_ring=True), "water", tone=1.0)
    water = w.finalize("water", mat, ao=dict(rays=1, strength=0.0, use_ground=False))

    d = kit.Builder("dressing")
    # stylised cedars / pines
    for i, (x, y, s) in enumerate(((-112, 72, 1.0), (-96, 84, 0.8), (-122, 40, 0.7),
                                   (118, 72, 0.9), (121, -64, 0.75), (-120, -78, 0.65))):
        d.add(kit.tmp_cyl((x, y, BANK - 1), (x, y, BANK + 9 * s), 1.8 * s, 7), "cedar", tone=0.55)
        for k, (r, z0, z1) in enumerate(((12, 6, 26), (9.5, 18, 38), (6.5, 30, 50))):
            d.add(kit.tmp_cyl((x, y, BANK + z0 * s), (x, y, BANK + z1 * s), r * s, 9, r1=0.6 * s),
                  "foliage", tone=(0.9, 0.97, 1.0)[k] - 0.05 * (i % 2), smooth=False)
    # bushes
    for i, (x, y, r) in enumerate(((-36, 34, 7), (-29, 40, 5), (-112, -18, 6), (-104, -48, 4.5),
                                   (100, -30, 6), (112, 10, 5), (-24, -70, 4))):
        d.add(kit.tmp_sphere((x, y, BANK + r * 0.35), r, 9, 6, scale=(1, 1, 0.72)), "foliage",
              tone=0.95 + 0.05 * (i % 2), smooth=True)
    # stepping-stone path from the cottage door to the bridge
    path = [(-84, -24), (-74, -30), (-62, -32), (-50, -28), (-39, -20), (-30, -10), (-22, 2),
            (-15, 14), (-10, 27), (-5, 40)]
    for i, (x, y) in enumerate(path):
        r = 3.6 + 0.6 * _hash(x, y)
        d.add(kit.tmp_cyl((x, y, BANK - 0.5), (x, y, BANK + 0.7), r, 8), "stone",
              tone=0.85 + 0.15 * _hash(y, x), bev=0.3)
    # small jetty for the skiff
    for x0 in (8.0, 13.0, 18.0, 23.0):
        d.box(x0, -48.0, 23.0, x0 + 4.6, -32.0, 24.4, "wood_deck", tone=0.82 + 0.05 * (x0 % 3))
    for x in (12.0, 24.0):
        for y in (-46.5, -33.5):
            d.add(kit.tmp_cyl((x, y, 11.0), (x, y, 26.0), 1.1, 7), "cedar", tone=0.55)
    d.box(2.0, -48.0, 30.6, 9.0, -32.0, BANK + 0.4, "wood_deck", tone=0.8)
    dressing = d.finalize("dressing", mat, ao=dict(rays=12, dist_mm=8.0, strength=0.8,
                                                   ground_z_mm=BANK, use_ground=True))

    t = kit.Builder("table")
    t.box(-1000, -1000, -10, 1000, 1000, 0, "paper", tone=0.80)
    table = t.finalize("table", mat, ao=dict(rays=1, strength=0.0, use_ground=False))
    return [tile, water, dressing, table]


def import_asset(path, mat):
    before = set(bpy.data.objects)
    st = bpy.ops.import_scene.fbx(filepath=path, use_manual_orientation=False, global_scale=1.0,
                                  use_anim=False, colors_type="LINEAR")
    if st != {"FINISHED"}:
        raise RuntimeError("import failed " + path)
    new = [o for o in bpy.data.objects if o not in before]
    root = [o for o in new if o.parent is None]
    for o in new:
        if o.type == "MESH":
            if o.data.color_attributes and "Col" not in o.data.color_attributes:
                o.data.color_attributes[0].name = "Col"
            o.data.materials.clear()
            o.data.materials.append(mat)
            if "_LOD0" not in o.name:
                o.hide_render = True
                o.hide_set(True)
    return root[0], new


def place(root, loc_mm, rot_z_deg=0.0):
    root.location = V(loc_mm) * MM
    root.rotation_euler = (0.0, 0.0, math.radians(rot_z_deg))


def pose_bone_world(arm, bone, axis_world, angle_deg):
    """Rotate a pose bone about its head around a world axis."""
    bpy.context.view_layer.update()
    pb = arm.pose.bones.get(bone)
    if pb is None:
        return False
    ax = (arm.matrix_world.inverted().to_3x3() @ V(axis_world)).normalized()
    head = pb.matrix.translation.copy()
    R = Matrix.Translation(head) @ Matrix.Rotation(math.radians(angle_deg), 4, ax) @ Matrix.Translation(-head)
    pb.matrix = R @ pb.matrix
    bpy.context.view_layer.update()
    return True


def look_at(cam, target_mm, az_deg, el_deg, dist_mm):
    t = V(target_mm) * MM
    az, el = math.radians(az_deg), math.radians(el_deg)
    d = V((-math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    cam.location = t + d * dist_mm * MM
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def setup_render(sc):
    sc.render.engine = "BLENDER_EEVEE"
    e = sc.eevee
    for k, v in (("taa_render_samples", 48), ("use_shadows", True), ("use_raytracing", False),
                 ("use_fast_gi", False), ("shadow_ray_count", 2), ("shadow_step_count", 8)):
        try:
            setattr(e, k, v)
        except Exception:
            pass
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.0
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.resolution_percentage = 100
    world = bpy.data.worlds.new("lookdev_world")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.66, 0.69, 0.72, 1.0)
    bg.inputs[1].default_value = 0.62
    sc.world = world
    sun_d = bpy.data.lights.new("key_sun", "SUN")
    sun_d.energy = 2.6
    sun_d.color = (1.0, 0.89, 0.76)
    sun_d.angle = math.radians(5.0)
    sun = bpy.data.objects.new("key_sun", sun_d)
    sc.collection.objects.link(sun)
    to_light = V((-0.48, -0.62, 0.62)).normalized()
    sun.rotation_euler = (-to_light).to_track_quat("-Z", "Y").to_euler()
    cam_d = bpy.data.cameras.new("cam")
    cam_d.sensor_width = 36.0
    cam_d.clip_start = 0.005
    cam_d.clip_end = 10.0
    cam = bpy.data.objects.new("cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def render_to(sc, path, w, h):
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    if not os.path.isfile(path):
        raise RuntimeError("render failed " + path)


def load_rgba(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    buf = array.array("f", [0.0]) * (w * h * 4)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return w, h, buf


def compose(panels, W, H, bg=(243, 235, 221)):
    """panels: [(path, x, y_top)]; writes a top-down RGBA byte buffer."""
    out = bytearray(bytes((bg[0], bg[1], bg[2], 255)) * (W * H))
    for path, px, py in panels:
        w, h, buf = load_rgba(path)
        for r in range(h):
            src = (h - 1 - r) * w * 4
            row = bytes(int(max(0.0, min(1.0, v)) * 255 + 0.5) for v in buf[src:src + w * 4])
            dst = ((py + r) * W + px) * 4
            out[dst:dst + w * 4] = row
    return out


def render_all():
    kit.reset_scene()
    sc = bpy.context.scene
    mat = kit.shared_material(ATLAS)
    build_tile(mat)
    A = {}
    for key, rel in (("cottage", "cottage/cottage.fbx"), ("bridge", "bridge/bridge.fbx"),
                     ("skiff", "skiff/skiff.fbx"), ("adult", "resident/resident.fbx"),
                     ("child", "resident/resident_child.fbx"), ("elder", "resident/resident_elder.fbx"),
                     ("wheelchair", "resident/resident_wheelchair.fbx")):
        A[key] = import_asset(os.path.join(LOOK, rel), mat)[0]
    place(A["cottage"], (-72.0, 8.0, BANK))
    place(A["bridge"], (river_cx(45.0), 45.0, BANK + 4.0))
    place(A["skiff"], (40.0, -40.0, 20.0), -90.0)
    place(A["adult"], (-52.0, -36.0, BANK), 15.0)
    place(A["child"], (-44.0, -40.0, BANK), 25.0)
    place(A["elder"], (-96.0, -30.0, BANK), -20.0)
    place(A["wheelchair"], (-24.0, -2.0, BANK), 205.0)
    pose_bone_world(A["adult"], "arm_L", (0, 1, 0), -150.0)
    pose_bone_world(A["child"], "arm_R", (0, 1, 0), 140.0)

    # lineup copies (true scale, side by side) placed 1.5 m away on the table
    LX = 1500.0
    L = {}
    for key, rel in (("cottage", "cottage/cottage.fbx"), ("bridge", "bridge/bridge.fbx"),
                     ("skiff", "skiff/skiff.fbx"), ("adult", "resident/resident.fbx"),
                     ("child", "resident/resident_child.fbx"), ("elder", "resident/resident_elder.fbx"),
                     ("wheelchair", "resident/resident_wheelchair.fbx")):
        L[key] = import_asset(os.path.join(LOOK, rel), mat)[0]
    xs = {"cottage": -150.0, "bridge": -40.0, "skiff": 80.0, "adult": 137.0, "child": 156.0,
          "elder": 175.0, "wheelchair": 198.0}
    for k, x in xs.items():
        z = 6.0 if k == "skiff" else (4.0 if k == "bridge" else 0.0)
        place(L[k], (LX + x, 0.0, z))
    bar = kit.Builder("scale_bar")
    for i in range(10):
        bar.box(LX - 50 + i * 10, -60, 0, LX - 40 + i * 10, -52, 1.5,
                "metal_dark" if i % 2 == 0 else "paper")
    bar.finalize("scale_bar_100mm", mat, ao=dict(rays=1, strength=0.0, use_ground=False))

    cam = setup_render(sc)
    tmp = tempfile.mkdtemp(prefix="resq_lookdev_")
    try:
        p_wide = os.path.join(tmp, "wide.png")
        cam.data.type = "PERSP"
        cam.data.lens = 60.0
        look_at(cam, (0.0, 2.0, 22.0), 18.0, 35.0, 600.0)
        render_to(sc, p_wide, 1280, 560)
        p_close = os.path.join(tmp, "close.png")
        cam.data.lens = 50.0
        look_at(cam, (-62.0, -22.0, 34.0), 24.0, 26.0, 230.0)
        render_to(sc, p_close, 636, 352)
        close_full = os.path.join(LOOK, "contact_closeup.png")
        render_to(sc, close_full, 1280, 720)
        p_rev = os.path.join(tmp, "reverse.png")
        cam.data.lens = 55.0
        look_at(cam, (15.0, 10.0, 22.0), 205.0, 40.0, 600.0)
        render_to(sc, p_rev, 636, 352)
        p_line = os.path.join(tmp, "lineup.png")
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = 0.44
        look_at(cam, (LX + 20.0, 0.0, 24.0), 0.0, 14.0, 600.0)
        render_to(sc, p_line, 1280, 352)
        W, H = 1280, 1280
        buf = compose([(p_wide, 0, 0), (p_close, 0, 568), (p_rev, 644, 568), (p_line, 0, 928)], W, H)
        out = os.path.join(LOOK, "contact_sheet.png")
        kit.write_png(out, W, H, buf)
        print("CONTACT SHEET", out, os.path.getsize(out))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
