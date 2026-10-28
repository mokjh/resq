"""Prepare the rigged Meshy residents (art/residents/<id>/walk.fbx: mesh + 24-bone rig + walk cycle) for the board.

Run headless, one Blender job at a time:
  blender -b --factory-startup --python-exit-code 1 -P tools/blender/resident_prep.py

Per resident:
  * scale the whole rig so the figure stands at its board height (adults 34 mm, elder 32 mm; residents 34 mm in the
    kit contract), feet on z = 0, facing -Y (kit forward);
  * decimate the skinned mesh to the 1,200-triangle resident budget (weights survive the collapse);
  * keep the walk action, strip root travel so it cycles in place (the simulation moves the resident);
  * find the walk frame with the feet closest together: Unity holds it as the idle pose;
  * add the life clips (Idle, Wait, Wave, Cheer, Sit) made on the man's rig, art/residents/resident_man/clips.fbx:
    all three share Meshy's 24-bone skeleton, so the clips' bone rotations transfer by name; object channels and the
    Hips translation are dropped (Meshy import scale, the man's proportions; the simulation owns position);
  * write the base colour (1K PNG) and export FBX with the kit preset plus the baked clips (one take per NLA strip)
    into game/Assets/Resq/Art/Residents/, and a sidecar art/residents/PREP_REPORT.json.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "blender"))
import kit  # noqa: E402

SRC = os.path.join(ROOT, "art", "residents")
OUT = os.path.join(ROOT, "game", "Assets", "Resq", "Art", "Residents")
HEIGHT_MM = {"resident_woman": 34.0, "resident_man": 35.0, "resident_elder": 32.0}
BUDGET = 1200


def prep(rid):
    kit.reset_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, rid, "walk.fbx"))
    scene = bpy.context.scene
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in scene.objects if o.type == "MESH")
    act = arm.animation_data.action if arm.animation_data else None
    f0, f1 = (int(act.frame_range[0]), int(act.frame_range[1])) if act else (1, 1)

    # in-place walk: remove the hips' horizontal travel (keep the bob)
    # the Meshy action also keys the armature OBJECT's transform (scale 0.01 etc.); those keys would override the
    # board-scale resize when the walk is baked, so drop every object-level channel and keep only pose bones
    dropped = 0
    if act:
        for bag, fc in list(_bag_fcurves(act)):
            if not fc.data_path.startswith("pose.bones"):
                bag.fcurves.remove(fc)
                dropped += 1
    travel = {}
    if act:
        for fc in _fcurves(act):
            if fc.data_path == 'pose.bones["Hips"].location':
                pts = fc.keyframe_points
                ys = [k.co.y for k in pts]
                travel[fc.array_index] = round(max(ys) - min(ys), 3) if ys else 0
                # a channel whose start and end differ by most of its range is travel, not bob: hold it
                if len(pts) > 1 and abs(ys[-1] - ys[0]) > 0.6 * (max(ys) - min(ys)) > 1e-6:
                    v0 = ys[0]
                    for k in pts:
                        k.co.y = v0; k.handle_left.y = v0; k.handle_right.y = v0
                    travel[fc.array_index] = "held"

    # measure the rest height in world space and scale the rig to the board height
    scene.frame_set(f0)
    bpy.context.view_layer.update()
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    height = max(zs) - min(zs)
    s = HEIGHT_MM[rid] / 1000.0 / height
    arm.scale = arm.scale * s
    bpy.context.view_layer.update()
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    arm.location.z -= min(zs)

    scene.frame_set(f0 + 1); scene.frame_set(f0)
    bpy.context.view_layer.update()
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    if abs((max(zs) - min(zs)) * 1000 - HEIGHT_MM[rid]) > 1.0:
        raise RuntimeError("%s: height after resize %.2f mm, expected %.1f" % (rid, (max(zs) - min(zs)) * 1000, HEIGHT_MM[rid]))

    # decimate the skinned mesh to budget
    mesh.data.calc_loop_triangles()
    tris0 = len(mesh.data.loop_triangles)
    if tris0 > BUDGET:
        bpy.context.view_layer.objects.active = mesh
        mod = mesh.modifiers.new("dec", "DECIMATE")
        mod.ratio = (BUDGET - 8) / tris0
        # the decimate must sit before the armature modifier so weights are collapsed with the geometry
        while mesh.modifiers[0] != mod:
            bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    mesh.data.calc_loop_triangles()
    tris = len(mesh.data.loop_triangles)

    # idle pose = the walk frame with the feet closest together
    best, best_d = f0, 1e9
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        pl = arm.pose.bones["LeftFoot"].head
        pr = arm.pose.bones["RightFoot"].head
        d = (arm.matrix_world @ pl - arm.matrix_world @ pr).length
        if d < best_d:
            best, best_d = f, d
    idle_t = (best - f0) / max(1, (f1 - f0))

    # forward: the rig walks toward -Y in Blender after import? record which way the toes point
    scene.frame_set(f0)
    toe = arm.matrix_world @ arm.pose.bones["LeftToeBase"].tail - arm.matrix_world @ arm.pose.bones["LeftFoot"].head
    facing = "-Y" if toe.y < 0 else "+Y"

    # texture -> 1K PNG next to the FBX
    os.makedirs(OUT, exist_ok=True)
    img = next((i for i in bpy.data.images if i.size[0] > 0), None)
    if img is not None:
        if img.packed_file:
            img.unpack(method="USE_ORIGINAL")
        img.scale(1024, 1024)
        img.filepath_raw = os.path.join(OUT, rid + "_base_color.png")
        img.file_format = "PNG"
        img.save()

    # bake exactly the walk cycle (the scene range otherwise defaults to 250 frames); the last frame repeats the first
    scene.frame_start, scene.frame_end = f0, f1 - 1
    life = add_life_clips(arm, act, f0, f1 - 1)
    for o in scene.objects:
        o.select_set(o in (arm, mesh))
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(OUT, rid + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, check_existing=False, use_selection=True, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             use_space_transform=True, bake_space_transform=False, object_types={"MESH", "ARMATURE"},
                             use_mesh_modifiers=False, mesh_smooth_type="OFF", use_triangles=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL", bake_anim=True,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=bool(life), bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.5, path_mode="STRIP", embed_textures=False)
    return {"height_mm": HEIGHT_MM[rid], "scale": round(s, 6), "tris_source": tris0, "tris": tris, "frames": [f0, f1],
            "idle_frame": best, "idle_normalized_time": round(idle_t, 4), "feet_gap_mm": round(best_d * 1000, 2),
            "rest_facing_blender": facing, "hips_travel": travel, "object_channels_dropped": dropped, "texture": rid + "_base_color.png (1024)",
            "life_clips": life}


LIFE = os.path.join(SRC, "resident_man", "clips.fbx")
CLIPS = {"Idle": "Idle", "Alert": "Wait", "Wave_for_Help": "Wave", "Cheer_with_Both_Hands_Up": "Cheer",
         "Chair_Sit_Idle_M": "Sit"}


def add_life_clips(arm, walk, w0, w1):
    """Loads the life clips onto this resident's armature as NLA strips beside the walk (named Walk). Returns
    {clip: [first, last frame]}, or {} when clips.fbx is absent (the export then carries the walk alone)."""
    if not os.path.exists(LIFE) or walk is None:
        return {}
    before_obj, before_act = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=LIFE)
    for o in set(bpy.data.objects) - before_obj:
        bpy.data.objects.remove(o, do_unlink=True)
    names = {b.name for b in arm.pose.bones}
    strips = [("Walk", walk, w0, w1)]
    out = {}
    for a in set(bpy.data.actions) - before_act:
        key = next((k for k in CLIPS if a.name.split("|")[-1].lower().startswith(k.lower())), None)
        if key is None:
            bpy.data.actions.remove(a)
            continue
        for bag, fc in list(_bag_fcurves(a)):
            bone = fc.data_path.split('"')[1] if fc.data_path.startswith("pose.bones") else None
            if bone is None or bone not in names or fc.data_path == 'pose.bones["Hips"].location':
                bag.fcurves.remove(fc)
        a.name = CLIPS[key]
        f = (int(a.frame_range[0]), int(a.frame_range[1]))
        strips.append((a.name, a, f[0], f[1]))
        out[a.name] = list(f)
    missing = sorted(set(CLIPS.values()) - set(out))
    if missing:
        raise RuntimeError("clips.fbx lacks %s" % missing)
    walk.name = "Walk"
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = None
    for name, a, s0, s1 in strips:
        track = ad.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, s0, a)
        if hasattr(strip, "action_slot") and getattr(a, "slots", None):
            strip.action_slot = a.slots[0]
        strip.action_frame_start, strip.action_frame_end = s0, s1
        strip.frame_end = s1
    return out


def _bag_fcurves(act):
    if hasattr(act, "fcurves") and not hasattr(act, "layers"):
        for fc in list(act.fcurves):
            yield act, fc
        return
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in list(bag.fcurves):
                    yield bag, fc


def _fcurves(act):
    # Blender 5 layered actions: walk every channelbag
    if hasattr(act, "fcurves"):
        return list(act.fcurves)
    out = []
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


if __name__ == "__main__" and "--crew" not in sys.argv and "--seated" not in sys.argv:
    report = {}
    for rid in sorted(HEIGHT_MM):
        report[rid] = prep(rid)
        print("RESIDENT", rid, json.dumps(report[rid]))
    with open(os.path.join(SRC, "PREP_REPORT.json"), "w") as f:
        json.dump(report, f, indent=2, sort_keys=True)


def crew_texture():
    """Rescue-crew uniform: the man's base colour with the green jacket recoloured hi-vis orange (skin, jeans and
    boots untouched), so crew read as responders, never as residents."""
    import numpy as np
    src = os.path.join(OUT, "resident_man_base_color.png")
    img = bpy.data.images.load(src)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    r, g, b = px[..., 0], px[..., 1], px[..., 2]
    green = (g > r * 1.08) & (g > b * 1.02) & (g > 0.06)
    lum = 0.3 * r + 0.59 * g + 0.11 * b
    k = np.clip(lum / 0.35, 0.25, 1.6)
    px[..., 0] = np.where(green, np.clip(0.98 * k, 0, 1), r)
    px[..., 1] = np.where(green, np.clip(0.42 * k, 0, 1), g)
    px[..., 2] = np.where(green, np.clip(0.08 * k, 0, 1), b)
    out = bpy.data.images.new("crew", w, h, alpha=True)
    out.pixels[:] = px.ravel().tolist()
    out.filepath_raw = os.path.join(OUT, "crew_base_color.png")
    out.file_format = "PNG"
    out.save()
    return int(green.sum())


if __name__ == "__main__" and "--crew" in sys.argv:
    print("CREW recoloured pixels", crew_texture())


def _aim(arm, name, world_dir):
    """Rotate pose bone `name` (keeping its head) so its head->tail points along world_dir."""
    pb = arm.pose.bones[name]
    mw = arm.matrix_world
    cur = (mw @ pb.tail - mw @ pb.head).normalized()
    rw = mw.to_quaternion()
    r = cur.rotation_difference(Vector(world_dir).normalized())
    r_arm = rw.inverted() @ r @ rw
    m = pb.matrix.copy()
    t = m.translation.copy()
    rot = r_arm.to_matrix().to_4x4() @ m.to_3x3().to_4x4()
    rot.translation = t
    pb.matrix = rot
    bpy.context.view_layer.update()


def seated(rid="resident_woman"):
    """Wheelchair resident: the woman rig posed seated (one-frame clip) + the kit chair as its own mesh."""
    from mathutils import Matrix
    kit.reset_scene()
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, rid, "walk.fbx"))
    scene = bpy.context.scene
    arm = next(o for o in scene.objects if o.type == "ARMATURE")
    mesh = next(o for o in scene.objects if o.type == "MESH")
    act = arm.animation_data.action
    for bag, fc in list(_bag_fcurves(act)):
        if not fc.data_path.startswith("pose.bones"):
            bag.fcurves.remove(fc)
    f0 = int(act.frame_range[0])
    scene.frame_set(f0)
    bpy.context.view_layer.update()
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    s = HEIGHT_MM[rid] / 1000.0 / (max(zs) - min(zs))
    arm.scale = arm.scale * s
    bpy.context.view_layer.update()
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    arm.location.z -= min(zs)
    bpy.context.view_layer.update()
    # freeze the standing (feet-together) pose without the action, parents first
    order = []
    def walk(b):
        order.append(b.name)
        for c in b.children:
            walk(c)
    for b in arm.data.bones:
        if b.parent is None:
            walk(b)
    mats = {n: arm.pose.bones[n].matrix.copy() for n in order}
    arm.animation_data.action = None
    for n in order:
        arm.pose.bones[n].matrix = mats[n]
        bpy.context.view_layer.update()
    mw = arm.matrix_world
    # hips down so the hip joints sit on the seat (chair: seat top 10.6 mm, hip joints ~12 mm, 0.6 mm back)
    hl, hr = arm.pose.bones["LeftUpLeg"].head, arm.pose.bones["RightUpLeg"].head
    hip_w = mw @ ((hl + hr) * 0.5)
    delta_w = Vector((0.0, 0.0006, 0.0120)) - Vector((0.0, hip_w.y, hip_w.z))
    delta_a = mw.inverted().to_3x3() @ delta_w
    pb = arm.pose.bones["Hips"]
    m = pb.matrix.copy(); m.translation = m.translation + delta_a; pb.matrix = m
    bpy.context.view_layer.update()
    for side in ("Left", "Right"):
        _aim(arm, side + "UpLeg", (0, -1, -0.04))
        _aim(arm, side + "Leg", (0, -0.12, -1))
        _aim(arm, side + "Foot", (0, -1, -0.35))
        _aim(arm, side + "Arm", (0.12 if side == "Left" else -0.12, -0.25, -1))
        _aim(arm, side + "ForeArm", (0, -1, -0.45))
    # one-frame "sit" clip
    arm.animation_data_create()
    sit = bpy.data.actions.new("sit")
    arm.animation_data.action = sit
    for f in (1, 2):
        scene.frame_set(f)
        for pbn in arm.pose.bones:
            pbn.keyframe_insert("location", frame=f)
            pbn.keyframe_insert("rotation_quaternion", frame=f)
            pbn.keyframe_insert("scale", frame=f)
    scene.frame_start, scene.frame_end = 1, 2
    scene.frame_set(1)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    zs = [(ev.matrix_world @ v.co).z for v in ev.to_mesh().vertices]
    ev.to_mesh_clear()
    seated_h = (max(zs) - min(zs)) * 1000
    if os.environ.get("RESQ_SCRATCH"):   # review render of the posed rider
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
        cam.data.type = "ORTHO"; cam.data.ortho_scale = 0.05
        cam.location = (0.06, -0.06, 0.03); cam.rotation_euler = (math.radians(80), 0, math.radians(45))
        scene.camera = cam
        scene.render.engine = "BLENDER_WORKBENCH"; scene.display.shading.color_type = "TEXTURE"
        scene.render.resolution_x = scene.render.resolution_y = 400
        scene.render.filepath = os.path.join(os.environ["RESQ_SCRATCH"], "seated.png")
        bpy.ops.render.render(write_still=True)
    # decimate like the standing residents
    mesh.data.calc_loop_triangles()
    if len(mesh.data.loop_triangles) > BUDGET:
        bpy.context.view_layer.objects.active = mesh
        mod = mesh.modifiers.new("dec", "DECIMATE")
        mod.ratio = (BUDGET - 8) / len(mesh.data.loop_triangles)
        while mesh.modifiers[0] != mod:
            bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for o in scene.objects:
        o.select_set(o in (arm, mesh))
    bpy.context.view_layer.objects.active = arm
    out = os.path.join(OUT, "resident_seated.fbx")
    bpy.ops.export_scene.fbx(filepath=out, check_existing=False, use_selection=True, global_scale=1.0,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             use_space_transform=True, bake_space_transform=False, object_types={"MESH", "ARMATURE"},
                             use_mesh_modifiers=False, mesh_smooth_type="OFF", use_triangles=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL", bake_anim=True,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, path_mode="STRIP", embed_textures=False)
    import shutil
    shutil.copyfile(os.path.join(OUT, rid + "_base_color.png"), os.path.join(OUT, "resident_seated_base_color.png"))
    # the chair alone (same geometry and roles as the kit wheelchair in lookdev.py), vertex-colour kit art
    kit.reset_scene()
    mat = kit.shared_material(os.path.join(ROOT, "art", "shared", "palette_atlas.png"))
    b = kit.Builder("chair")
    seat_z = 10.6
    b.box(-5.0, -6.4, seat_z - 1.0, 5.0, 2.4, seat_z, "cloth_slate")
    b.box(-4.8, 2.9, seat_z - 0.6, 4.8, 3.8, seat_z + 9.5, "cloth_slate", tone=0.9)
    b.box(-4.0, -12.0, 1.6, 4.0, -8.6, 2.4, "metal_dark")
    for s_ in (1, -1):
        wx = s_ * 7.0
        b.add(kit.tmp_cyl((wx - s_ * 0.7, 1.2, 7.4), (wx + s_ * 0.7, 1.2, 7.4), 7.4, 12), "rubber")
        b.add(kit.tmp_cyl((wx - s_ * 0.9, 1.2, 7.4), (wx + s_ * 0.9, 1.2, 7.4), 6.1, 12), "mineral")
        b.add(kit.tmp_cyl((wx - s_ * 1.3, 1.2, 7.4), (wx + s_ * 1.3, 1.2, 7.4), 1.2, 6), "metal_dark")
        fx = s_ * 5.4
        for p0, p1 in (((fx, 3.4, seat_z + 9.0), (fx, 3.4, 5.0)), ((fx, 3.4, 5.0), (fx, -7.4, 4.6)),
                       ((fx, -6.0, seat_z - 0.6), (fx, -9.4, 2.0)), ((fx, 3.4, seat_z + 9.0), (fx, 5.4, seat_z + 9.4))):
            kit.rail(b, p0, p1, 0.9, 0.9, "metal_dark")
        b.add(kit.tmp_cyl((fx, 5.0, seat_z + 9.3), (fx, 7.0, seat_z + 9.7), 0.75, 6), "rubber")
        b.add(kit.tmp_cyl((fx - 0.5, -7.4, 2.0), (fx + 0.5, -7.4, 2.0), 2.0, 6), "rubber")
        kit.rail(b, (fx, -7.4, 2.0), (fx, -7.4, 4.8), 0.7, 0.7, "metal_dark")
    ob = b.finalize("wheelchair_chair_LOD0", mat, ao=dict(rays=12, dist_mm=5.0, strength=0.8))
    chair_path = os.path.join(ROOT, "game", "Assets", "Resq", "Art", "Lookdev", "resident", "wheelchair_chair.fbx")
    kit.export_fbx(chair_path, [ob])
    return {"seated_height_mm": round(seated_h, 1), "chair_tris": kit.tri_count(ob)}


if __name__ == "__main__" and "--seated" in sys.argv:
    print("SEATED", json.dumps(seated()))

