"""Prepare Meshy hero assets (art/hero/<world>/<id>/) for the board.

Run headless, one Blender job at a time:
  blender -b --factory-startup -P tools/blender/hero_prep.py -- survey   # side views with height grid -> scratch dir
  blender -b --factory-startup -P tools/blender/hero_prep.py -- export   # cut bases, scale, pivot, export to Unity

Per asset (from meta.json + the CUT table below):
  * cut the fused diorama base away below a height fraction (bisect, discard below, open bottom sits on terrain);
  * drop loose debris below a minimum island size (rigging shards, stray trees left on the cut);
  * scale uniformly so the footprint fits target w x d mm (board scale 1:50); tall landmarks may stretch in Z
    up to STRETCH_MAX to approach their target height;
  * pivot = footprint centre on the ground (z = 0); export FBX with the kit preset + the 2K base-colour PNG
    into game/Assets/Resq/Art/Hero/ (Unity imports at 1K and builds a Resq/Hero material).
The script never touches the source FBX/PNG in art/hero.
"""

import json
import math
import os
import shutil
import sys

import bpy
import bmesh
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "blender"))
import kit  # noqa: E402

HERO = os.path.join(ROOT, "art", "hero")
OUT = os.path.join(ROOT, "game", "Assets", "Resq", "Art", "Hero")
SCRATCH = os.environ.get("RESQ_SCRATCH", os.path.join(ROOT, "tools", ".cache", "hero"))

# Height fraction (0..1 of the model's height) below which the fused base is cut away; None keeps everything.
# Filled in from the survey renders (see docs: art/hero/PREP.md).
CUT = {
    "chalet_a": 0.05, "cottage_stone": 0.05, "church": 0.04, "bridge_stone": None, "bus_city": 0.075,
    "boat_rescue": None, "ranger_station": 0.05, "lookout_tower": 0.03, "tanker_truck": 0.08, "bulldozer": 0.08,
    "mountain_hut": 0.11, "snowcat": None, "lighthouse": None, "harbour_houses": 0.1, "fishing_boat": None,
    "basalt_house": 0.05,
}
STRETCH = {"church": 1.25, "lookout_tower": 1.3, "lighthouse": 1.3}
MIN_ISLAND = 150  # faces; smaller loose parts are dropped after the cut


def assets():
    for world in sorted(os.listdir(HERO)):
        wdir = os.path.join(HERO, world)
        if not os.path.isdir(wdir):
            continue
        for aid in sorted(os.listdir(wdir)):
            meta = os.path.join(wdir, aid, "meta.json")
            if os.path.isfile(meta):
                with open(meta) as f:
                    yield world, aid, os.path.join(wdir, aid), json.load(f)


def load(adir, aid):
    kit.reset_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(adir, aid + ".fbx"))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in bpy.context.scene.objects:
        o.select_set(o in meshes)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o != ob:
            bpy.data.objects.remove(o, do_unlink=True)
    ob.name = aid
    return ob


def bounds(ob):
    xs = [v.co.x for v in ob.data.vertices]; ys = [v.co.y for v in ob.data.vertices]; zs = [v.co.z for v in ob.data.vertices]
    return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))


def cut_base(ob, frac):
    lo, hi = bounds(ob)
    z = lo.z + (hi.z - lo.z) * frac
    bm = bmesh.new(); bm.from_mesh(ob.data)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, z), plane_no=(0, 0, 1), clear_inner=True)
    drop_islands(bm)
    bm.to_mesh(ob.data); bm.free()


def drop_islands(bm):
    bm.faces.ensure_lookup_table()
    seen, kill = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, island = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop(); island.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); stack.append(h)
        if len(island) < MIN_ISLAND:
            kill.extend(island)
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")


def fit(ob, meta, aid):
    t = meta["target_size_mm"]
    lo, hi = bounds(ob)
    size = hi - lo
    # footprint: longest horizontal side matches the larger of w/d, the other side follows uniformly
    # the longest horizontal side matches the longer target side; proportions stay the model's own
    s = max(t["w"], t["d"]) / 1000.0 / max(size.x, size.y)
    sz = s
    if aid in STRETCH:
        want = t["h"] / 1000.0 / max(1e-6, size.z)
        sz = min(want, s * STRETCH[aid]) if want > s else s
    cx, cy = (lo.x + hi.x) / 2, (lo.y + hi.y) / 2
    for v in ob.data.vertices:
        v.co = Vector(((v.co.x - cx) * s, (v.co.y - cy) * s, (v.co.z - lo.z) * sz))
    ob.data.update()
    lo, hi = bounds(ob)
    return {"scale": round(s, 5), "z_scale": round(sz, 5), "size_mm": [round((hi - lo)[i] * 1000, 1) for i in range(3)]}


def survey():
    os.makedirs(SCRATCH, exist_ok=True)
    for world, aid, adir, meta in assets():
        ob = load(adir, aid)
        scene = bpy.context.scene
        lo, hi = bounds(ob)
        h = hi.z - lo.z
        # height grid every 5% so the cut fraction can be read off the render
        for k in range(1, 20):
            z = lo.z + h * k / 20
            bpy.ops.mesh.primitive_plane_add(size=1, location=((lo.x + hi.x) / 2, hi.y + 0.01, z), rotation=(math.pi / 2, 0, 0))
            p = bpy.context.active_object
            p.scale = ((hi.x - lo.x) * 1.2, 0.0015 * h * 10, 1)
            mat = bpy.data.materials.new("grid%d" % k); mat.diffuse_color = (1, 0, 0, 1) if k % 2 == 0 else (0, 0.4, 1, 1)
            p.data.materials.append(mat)
        cam_data = bpy.data.cameras.new("cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = max(hi.x - lo.x, h) * 1.25
        cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
        cam.location = ((lo.x + hi.x) / 2, lo.y - 10, lo.z + h / 2); cam.rotation_euler = (math.pi / 2, 0, 0)
        scene.camera = cam
        light = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(light)
        light.rotation_euler = (math.radians(50), 0, math.radians(30))
        scene.render.engine = "BLENDER_WORKBENCH"
        scene.display.shading.color_type = "TEXTURE"
        scene.render.resolution_x = scene.render.resolution_y = 512
        scene.render.filepath = os.path.join(SCRATCH, "%s_%s_side.png" % (world, aid))
        bpy.ops.render.render(write_still=True)
        print("SURVEY", world, aid, "h=%.3f" % h)


def check_render(ob, world, aid):
    """Three-quarter view of the prepared model on a ground plane (scratch only) to review base cuts."""
    os.makedirs(SCRATCH, exist_ok=True)
    scene = bpy.context.scene
    lo, hi = bounds(ob)
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
    bpy.ops.mesh.primitive_plane_add(size=size * 3, location=(0, 0, 0))
    cam_data = bpy.data.cameras.new("cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = size * 1.6
    cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam)
    d = Vector((1.0, -1.3, 0.9)).normalized() * size * 5
    cam.location = d + Vector((0, 0, (hi.z - lo.z) * 0.4))
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x = scene.render.resolution_y = 400
    scene.render.filepath = os.path.join(SCRATCH, "%s_%s_prep.png" % (world, aid))
    bpy.ops.render.render(write_still=True)


def export():
    os.makedirs(OUT, exist_ok=True)
    report = {}
    for world, aid, adir, meta in assets():
        ob = load(adir, aid)
        tris0 = kit.tri_count(ob)
        if CUT.get(aid):
            cut_base(ob, CUT[aid])
        info = fit(ob, meta, aid)
        info["tris"] = kit.tri_count(ob); info["tris_source"] = tris0; info["world"] = world
        fbx = os.path.join(OUT, aid + ".fbx")
        kit.export_fbx(fbx, [ob], preset={"object_types": {"MESH"}, "colors_type": "NONE"})
        tex_src = os.path.join(adir, aid + "_base_color_clean.png")
        if not os.path.isfile(tex_src):
            tex_src = os.path.join(adir, aid + "_base_color.png")
        shutil.copyfile(tex_src, os.path.join(OUT, aid + "_base_color.png"))
        report[aid] = info
        print("EXPORT", aid, json.dumps(info))
        check_render(ob, world, aid)
    with open(os.path.join(HERO, "PREP_REPORT.json"), "w") as f:
        json.dump(report, f, indent=2, sort_keys=True)


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    mode = argv[0] if argv else "survey"
    survey() if mode == "survey" else export()
