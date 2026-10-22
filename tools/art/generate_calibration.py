"""Run only in a new background Blender with --factory-startup --disable-autoexec.

No existing blend, desktop connection, subprocess, download, or external asset is used.
The caller owns storage preflight, heavy slot, process provenance and log capture.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import calibration_geometry as geometry


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def is_factory_viewer(image):
    expected = {"Render Result": "RENDER_RESULT", "Viewer Node": "COMPOSITING"}
    return (expected.get(image.name) == image.type and image.source == "VIEWER"
            and not image.filepath and image.packed_file is None)


def measure(bpy):
    bpy.context.view_layer.update()  # Resolve recently authored/parented transforms before measuring.
    result = {}
    for obj in bpy.context.scene.objects:
        item = {"position": list(obj.matrix_world.translation),
                "scale": list(obj.scale), "rotation": list(obj.rotation_euler)}
        if obj.type == "MESH":
            obj.data.calc_loop_triangles()
            item.update(bounds=geometry.bounds([tuple(obj.matrix_world @ v.co)
                                                 for v in obj.data.vertices]),
                        triangles=len(obj.data.loop_triangles),
                        materials=[m.name if m else None for m in obj.data.materials])
        elif obj.type != "EMPTY":
            raise ValueError("unexpected nonmesh/nonmarker object")
        result[obj.name] = item
    return result


def main():
    import bpy
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--report", required=True)
    opts = parser.parse_args(args)
    if not bpy.app.background or "--factory-startup" not in sys.argv or "--disable-autoexec" not in sys.argv:
        raise RuntimeError("dedicated background factory startup with autoexec disabled required")
    if bpy.context.preferences.filepaths.use_scripts_auto_execute:
        raise RuntimeError("Blender still allows automatic script execution")
    if bpy.data.filepath:
        raise RuntimeError("refusing an existing Blender document")
    # Blender 5.2 factory startup supplies two empty viewer buffers. They are
    # not textures or linked assets. Remove only those exact safe buffers in
    # this disposable process; every other image still fails the strict guard.
    removed_factory_viewers = []
    for image in list(bpy.data.images):
        if is_factory_viewer(image):
            removed_factory_viewers.append(image.name)
            bpy.data.images.remove(image)
    base = Path(__file__).resolve().parents[2]
    output = Path(opts.output_dir).resolve()
    report = Path(opts.report).resolve()
    if output != base / "art/calibration" or not report.is_relative_to(base / "evidence/calibration"):
        raise RuntimeError("destination must remain inside owned RESQ calibration folders")
    blend, fbx = output / "unit.blend", output / "unit.fbx"
    if any(p.exists() for p in (blend, fbx, report)):
        raise FileExistsError("preserve prior attempt outputs; archive explicitly before retry")
    output.mkdir(parents=True, exist_ok=True)
    report.parent.mkdir(parents=True, exist_ok=True)
    # New process only: remove the factory scene, never an existing user's scene.
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for block in list(bpy.data.materials):
        bpy.data.materials.remove(block)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    spec = geometry.specification()
    materials = {}
    for name, rgba in spec["materials"].items():
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = rgba
        mat.use_nodes = True
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = rgba
        shader.inputs["Roughness"].default_value = 0.8
        shader.inputs["Metallic"].default_value = 0.0
        materials[name] = mat
    root = bpy.data.objects.new(geometry.ROOT, None)
    bpy.context.scene.collection.objects.link(root)
    root["resq_calibration_version"] = geometry.VERSION
    root["resq_source_forward"] = "-Y"
    for item in spec["meshes"]:
        mesh = bpy.data.meshes.new(item["name"] + "_Mesh")
        mesh.from_pydata(item["vertices"], [], item["faces"])
        mesh.update()
        obj = bpy.data.objects.new(item["name"], mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        mesh.materials.append(materials[item["material"]])
    for name, point in geometry.MARKERS.items():
        obj = bpy.data.objects.new(name, None)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        obj.location = point
        obj.empty_display_size = 0.005
    if bpy.data.libraries or bpy.data.images or bpy.data.texts:
        raise RuntimeError("unexpected linked file, image, or embedded script")
    source = measure(bpy)
    geometry.assert_measurements(source)
    preset = dict(global_scale=1.0, apply_unit_scale=True,
                  apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                  use_space_transform=True, bake_space_transform=False,
                  object_types={"MESH", "EMPTY"}, use_selection=True,
                  use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
                  use_custom_props=True, path_mode="STRIP", embed_textures=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), check_existing=True, compress=False)
    for obj in bpy.context.scene.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    status = bpy.ops.export_scene.fbx(filepath=str(fbx), check_existing=True, **preset)
    if status != {"FINISHED"} or not fbx.is_file() or fbx.stat().st_size < 100:
        raise RuntimeError("real FBX export did not finish")
    if not blend.read_bytes().startswith(b"BLENDER"):
        raise RuntimeError("saved source is not an uncompressed native blend")
    if not fbx.read_bytes().startswith(b"Kaydara FBX Binary  \x00\x1a\x00"):
        raise RuntimeError("export is not binary FBX")
    # Independent exporter/importer round trip within this disposable process.
    # Saved .blend is preserved. This does not stand in for Unity import evidence.
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)
    imported = bpy.ops.import_scene.fbx(filepath=str(fbx), use_manual_orientation=False,
                                        global_scale=1.0, use_anim=False)
    if imported != {"FINISHED"}:
        raise RuntimeError("FBX reimport did not finish")
    roundtrip = measure(bpy)
    geometry.assert_measurements(roundtrip, tolerance=1e-5)
    payload = {"schema": "resq-calibration-export-1", "evidence_kind": "automated",
               "scope": "Blender export and Blender reimport only; Unity pending",
               "geometry_sha256": geometry.geometry_hash(),
               "generator_sha256": sha(__file__),
               "geometry_source_sha256": sha(Path(geometry.__file__)),
               "blender_version": bpy.app.version_string,
               "pid": os.getpid(), "cwd": os.getcwd(),
               "background": bpy.app.background, "autoexec_disabled": True,
               "removed_factory_viewers": removed_factory_viewers,
               "preset": {k: sorted(v) if isinstance(v, set) else v for k, v in preset.items()},
               "source_measurements": source, "blender_reimport_measurements": roundtrip,
               "artifacts": [{"path": str(p.relative_to(base)), "sha256": sha(p),
                               "bytes": p.stat().st_size} for p in (blend, fbx)]}
    with report.open("x") as stream:
        json.dump(payload, stream, indent=2, sort_keys=True)
        stream.write("\n")
    print("RESQ calibration Blender export/reimport recorded; Unity acceptance remains pending")


if __name__ == "__main__":
    main()
