"""Synthetic temporary fixtures ONLY: these tests provide no native/F06 evidence."""
import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

import qualify_calibration as q
import calibration_geometry as geometry


ROOT = Path(__file__).resolve().parents[2]


def png_fixture():
    # Four plainly synthetic colored squares; never live qualification imagery.
    rows = []
    for y in range(512):
        row = bytearray(b"\x20\x20\x20\xff" * 2048)
        if 128 <= y < 160:
            for view in range(4):
                color = bytes((80+view*20, 90, 100, 255))
                row[(view*512+128)*4:(view*512+160)*4] = color*32
        rows.append(b"\0"+row)
    def chunk(kind, body):
        return struct.pack(">I", len(body))+kind+body+struct.pack(">I", zlib.crc32(kind+body)&0xffffffff)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 2048, 512, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"".join(rows)))+chunk(b"IEND", b""))


def vec(point):
    return dict(zip(("x", "y", "z"), point))


class SyntheticFixture:
    """Entire fixture, including invented execution/inspection, stays temporary."""
    def __init__(self, root):
        self.root = root
        self.packet = {"schema": "resq-calibration-qualification-input-1", "artifacts": []}
        for source in q.SOURCES:
            self.write(source, (ROOT/source).read_bytes())
        self.write("art/calibration/unit.blend", b"BLENDER"+b"synthetic-fixture"*20)
        fbx = b"Kaydara FBX Binary  \x00\x1a\x00"+b"synthetic-fixture"*20
        self.write("art/calibration/unit.fbx", fbx)
        self.write("game/Assets/Resq/Art/Calibration/unit.fbx", fbx)
        self.write("game/Assets/Resq/Art/Calibration/unit.fbx.meta", b"synthetic fixture meta\n")
        self.write("synthetic-protected-state.txt", b"synthetic unrelated state\n")
        self.write("evidence/calibration/desktop-before.json", b'{"synthetic_desktop": true}\n')
        self.write("evidence/calibration/desktop-after.json", b'{"synthetic_desktop": true}\n')
        self.write("evidence/calibration/license.txt", b"synthetic license evidence; NOT a real license\n")
        preset = {"global_scale": 1.0, "apply_unit_scale": True, "apply_scale_options": "FBX_SCALE_UNITS",
                  "axis_forward": "-Z", "axis_up": "Y", "use_space_transform": True, "bake_space_transform": False,
                  "object_types": ["EMPTY", "MESH"], "use_selection": True, "use_mesh_modifiers": False,
                  "add_leaf_bones": False, "bake_anim": False, "use_custom_props": True,
                  "path_mode": "STRIP", "embed_textures": False}
        source_measurements = {}
        for name, point in {geometry.ROOT: (0, 0, 0), **geometry.MARKERS}.items():
            source_measurements[name] = {"position": list(point), "rotation": [0, 0, 0], "scale": [1, 1, 1]}
        for name, center, size, material in geometry.BOXES:
            source_measurements[name] = {"position": [0, 0, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1],
                                         "bounds": geometry.bounds(geometry.vertices(center, size)),
                                         "triangles": 12, "materials": [material]}
        self.export_ref = "evidence/calibration/blender.json"
        self.json(self.export_ref, {"schema": "resq-calibration-export-1", "background": True, "autoexec_disabled": True,
                  "geometry_sha256": geometry.geometry_hash(), "generator_sha256": self.sha(q.SOURCES[0]),
                  "geometry_source_sha256": self.sha(q.SOURCES[1]), "blender_version": "Synthetic Blender 0.0",
                  "artifacts": [self.artifact(name) for name in ("art/calibration/unit.blend", "art/calibration/unit.fbx")],
                  "source_measurements": source_measurements, "blender_reimport_measurements": source_measurements,
                  "preset": preset})
        self.json("evidence/calibration/blender-audit.json", {"blender_export_record_valid": True, "F06_accepted": False})
        self.measure_ref = "evidence/calibration/measurements.json"
        measured = []
        for name, item in source_measurements.items():
            def convert(point):
                x, y, z = point
                return (x, z, -y)
            entry = {"name": name+"(Clone)" if name == geometry.ROOT else name,
                     "position": vec(convert(item["position"])), "rotation": vec((0, 0, 0)), "scale": vec((1, 1, 1)),
                     "materials": item.get("materials"), "triangles": item.get("triangles", 0)}
            if "bounds" in item:
                name_, center, size, _ = next(box for box in geometry.BOXES if box[0] == name)
                bounds = geometry.bounds([convert(point) for point in geometry.vertices(center, size)])
                entry.update(boundsMin=vec(bounds["min"]), boundsMax=vec(bounds["max"]))
            measured.append(entry)
        self.json(self.measure_ref, {"schema": "resq-calibration-unity-1", "unityVersion": "Synthetic Unity 0.0",
                  "fbxSha256": self.sha("art/calibration/unit.fbx"), "utc": self.time(5),
                  "globalScale": 1, "fileScale": 1, "bakeAxisConversion": True, "useFileScale": True,
                  "materialImportMode": "ImportStandard", "measurements": measured})
        self.image_ref = "evidence/calibration/image.png"
        self.write(self.image_ref, png_fixture())
        self.render_ref = "evidence/calibration/render.json"
        positions = ((.25, .20, .30), (-.25, .20, -.30), (0, .40, 0), (.25, .20, .30))
        self.json(self.render_ref, {"schema": "resq-calibration-unity-render-1", "evidence_kind": "automated",
                  "unityVersion": "Synthetic Unity 0.0", "utc": self.time(8), "graphicsDevice": "SyntheticFixture",
                  "pipeline": "SyntheticFixture", "colorSpace": "Linear", "assetDependencyHash": "a"*32,
                  "fbxSha256": self.sha("art/calibration/unit.fbx"),
                  "importMetaSha256": self.sha("game/Assets/Resq/Art/Calibration/unit.fbx.meta"),
                  "measurementsSha256": self.sha(self.measure_ref), "pngSha256": self.sha(self.image_ref),
                  "width": 2048, "height": 512, "materialOverrides": False, "F06Accepted": False,
                  "pixelInspectionPerformed": False, "shaders": ["SyntheticShader"],
                  "frames": [{"name": name, "cameraPosition": vec(positions[i]), "instanceScale": 5/6 if i == 3 else 1,
                              "orthographicSize": .15, "nonBackgroundPixels": 1024, "magentaPixels": 0}
                             for i, name in enumerate(q.VIEWS)]})
        blender_exe = self.exe("blender", "Synthetic Blender 0.0")
        unity_exe = self.exe("unity", "Synthetic Unity 0.0")
        base = [unity_exe["path"], "-batchmode", "-projectPath", str(root/"game"), "-executeMethod"]
        self.stage("blender", blender_exe, self.export_ref, 1, 3,
                   [blender_exe["path"], "--background", "--factory-startup", "--disable-autoexec", "--python-exit-code", "1",
                    "--python", str(root/q.SOURCES[0]), "--", "--output-dir", str(root/"art/calibration"),
                    "--report", str(root/self.export_ref)],
                   "RESQ calibration Blender export/reimport recorded; Unity acceptance remains pending")
        self.packet["blender"]["independent_audit"] = "evidence/calibration/blender-audit.json"
        measure_log = "evidence/calibration/measurement-editor.log"
        self.stage("measurement", unity_exe, self.measure_ref, 4, 6,
                   base+["Resq.Art.Calibration.CalibrationMeasurement.ImportAndMeasure", "-resqCalibrationReport",
                         str(root/self.measure_ref), "-logFile", str(root/measure_log)],
                   "RESQ calibration measurements recorded. Unity render inspection/F06 acceptance pending.", measure_log)
        render_log = "evidence/calibration/render-editor.log"
        self.stage("render", unity_exe, self.render_ref, 7, 9,
                   base+["Resq.Art.Calibration.CalibrationContactSheet.Render", "-resqCalibrationMeasurements",
                         str(root/self.measure_ref), "-resqCalibrationImage", str(root/self.image_ref),
                         "-resqCalibrationRenderReport", str(root/self.render_ref), "-logFile", str(root/render_log)],
                   "Actual Unity calibration pixels written. Pixel inspection and F06 acceptance remain pending.", render_log)
        self.packet["render"]["image"] = self.image_ref
        self.packet["editor_binding"] = "evidence/calibration/editor.json"
        self.json(self.packet["editor_binding"], {"schema": "resq-calibration-editor-binding-1", "projectPath": str(root/"game"),
                  "unityVersion": unity_exe["version"], "executable": unity_exe, "license_status": "valid",
                  "license_evidence": "evidence/calibration/license.txt", "source_hashes": {name: self.sha(name) for name in q.SOURCES[3:]}})
        self.packet["pixel_inspection"] = "evidence/calibration/inspection.json"
        # Independent expected crop hashes, rather than calling the verifier to fabricate them.
        view_hashes = []
        for i in range(4):
            view = bytearray(b"\x20\x20\x20\xff"*512*512)
            for y in range(128, 160):
                view[(y*512+128)*4:(y*512+160)*4] = bytes((80+i*20, 90, 100, 255))*32
            view_hashes.append(hashlib.sha256(view).hexdigest())
        self.json(self.packet["pixel_inspection"], {"schema": "resq-calibration-pixel-inspection-1", "completed": True,
                  "image": self.image_ref, "pngSha256": self.sha(self.image_ref), "render_report": self.render_ref,
                  "renderSha256": self.sha(self.render_ref), "measurementsSha256": self.sha(self.measure_ref),
                  "fbxSha256": self.sha("art/calibration/unit.fbx"),
                  "importMetaSha256": self.sha("game/Assets/Resq/Art/Calibration/unit.fbx.meta"),
                  "unityVersion": unity_exe["version"], "width": 2048, "height": 512, "utc": self.time(10),
                  "inspector": {"kind": "ai", "identity": "synthetic fixture only, no actual inspection"},
                  "views": [{"name": name, "rgba_sha256": view_hashes[i]} for i, name in enumerate(q.VIEWS)],
                  "observations": {name: {"passed": True, "observation": "Synthetic fixture statement only"} for name in q.OBSERVATIONS}})
        self.packet["preservation"] = "evidence/calibration/preservation.json"
        for moment, second in (("before", 0), ("after", 11)):
            self.json("evidence/calibration/"+moment+".json", {"schema": "resq-calibration-preservation-snapshot-1",
                      "captured_utc": self.time(second), "protected_artifacts": [self.artifact("synthetic-protected-state.txt", absolute=True)],
                      "unrelated_processes": [], "desktop_blender_sessions": [],
                      "desktop_state_artifact": "evidence/calibration/desktop-"+moment+".json",
                      "desktop_state_sha256": self.sha("evidence/calibration/desktop-"+moment+".json")})
        self.json(self.packet["preservation"], {"schema": "resq-calibration-preservation-1", "coordinator": "SyntheticFixture",
                  "capture_method": "contemporaneous_coordinator_capture",
                  "execution_refs": [self.packet[name]["execution"] for name in ("blender", "measurement", "render")],
                  "owned_process_ids": [101, 104, 107], "owned_write_paths": ["art/calibration/", "evidence/calibration/", "game/"],
                  "before_snapshot": "evidence/calibration/before.json", "after_snapshot": "evidence/calibration/after.json"})

    def time(self, seconds):
        return "2026-01-01T00:00:%02d+00:00" % seconds

    def write(self, name, data):
        path = self.root/name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def json(self, name, value):
        self.write(name, json.dumps(value, sort_keys=True).encode())

    def read(self, name):
        return json.loads((self.root/name).read_text())

    def sha(self, name):
        return q.digest((self.root/name).read_bytes())

    def artifact(self, name, absolute=False):
        return {"path": str(self.root/name) if absolute else name, "bytes": (self.root/name).stat().st_size, "sha256": self.sha(name)}

    def exe(self, name, version):
        ref = "synthetic-bin/"+name
        self.write(ref, ("NOT A NATIVE EXECUTABLE: "+version).encode())
        return {**self.artifact(ref, absolute=True), "version": version}

    def stage(self, name, exe, report, start, end, argv, message, log=None):
        out, err = "evidence/calibration/"+name+"-stdout.txt", "evidence/calibration/"+name+"-stderr.txt"
        self.write(out, (exe["version"]+"\n"+message+"\n").encode())
        self.write(err, b"")
        stage = {"report": report, "execution": "evidence/calibration/"+name+"-execution.json", "executable": exe}
        if log:
            stage["log"] = log
            self.write(log, (exe["version"]+"\n"+message+"\n").encode())
        run = {"argv": argv, "cwd": str(self.root), "status": "passed", "exit_code": 0, "timed_out": False,
               "launch_error": None, "stop_reason": None, "output_errors": [], "pid": 100+start, "pgid": 100+start,
               "parent_pid": 99, "owner": "SyntheticFixture", "started_utc": self.time(start), "ended_utc": self.time(end),
               "duration_seconds": end-start,
               "stdout": {"path": str(self.root/out), "byte_length": (self.root/out).stat().st_size, "sha256": self.sha(out)},
               "stderr": {"path": str(self.root/err), "byte_length": 0, "sha256": self.sha(err)}}
        self.json(stage["execution"], {"executable_sha256": exe["sha256"], "process": run})
        self.packet[name] = stage

    def inventory(self):
        self.packet["artifacts"] = [self.artifact(str(path.relative_to(self.root)))
                                    for path in sorted(self.root.rglob("*")) if path.is_file() and not path.is_symlink()]
        return self.packet

    def mutate(self, ref, callback):
        value = self.read(ref)
        callback(value)
        self.json(ref, value)


class QualificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="resq-SYNTHETIC-qualification-")
        self.addCleanup(self.temp.cleanup)
        self.fixture = SyntheticFixture(Path(self.temp.name).resolve())

    def reject(self, text=None, packet=None):
        with self.assertRaisesRegex(q.QualificationError, text or ".+"):
            q.qualify(self.fixture.root, packet or self.fixture.inventory())

    def test_synthetic_complete_records_validate_structure_only(self):
        result = q.qualify(self.fixture.root, self.fixture.inventory())
        self.assertEqual(result["status"], "complete_structural_validation")
        self.assertIs(result["accepted_task"], False)
        self.assertEqual(set(result["assertions"]), {"check-f06-a%02d" % i for i in range(1, 5)})
        self.assertEqual([item["path"] for item in result["artifacts"]],
                         sorted(item["path"] for item in self.fixture.packet["artifacts"]))
        self.assertTrue(all(set(item) == {"path", "sha256", "size_bytes"} for item in result["artifacts"]))

    def test_source_only_packet_cannot_qualify(self):
        source = "tools/art/calibration_geometry.py"
        data = (ROOT/source).read_bytes()
        packet = {"schema": "resq-calibration-qualification-input-1", "artifacts": [
            {"path": source, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}]}
        with self.assertRaisesRegex(q.QualificationError, "unbound artifact"):
            q.qualify(ROOT, packet)

    def test_artifact_hash_mismatch(self):
        packet = self.fixture.inventory()
        (self.fixture.root/self.fixture.export_ref).write_bytes(b"changed")
        self.reject("hash/size mismatch", packet)

    def test_duplicate_artifact(self):
        packet = self.fixture.inventory()
        packet["artifacts"].append(packet["artifacts"][0])
        self.reject("duplicate artifact", packet)

    def test_artifact_escape(self):
        packet = self.fixture.inventory()
        packet["artifacts"][0]["path"] = "../outside"
        self.reject("canonical and relative", packet)

    def test_symlink_artifact(self):
        packet = self.fixture.inventory()
        path = self.fixture.root/self.fixture.export_ref
        path.unlink()
        path.symlink_to(self.fixture.root/self.fixture.measure_ref)
        self.reject("symlink", packet)

    def test_bool_not_artifact_size(self):
        packet = self.fixture.inventory()
        packet["artifacts"][0]["bytes"] = True
        self.reject("hash/size mismatch", packet)

    def test_unsafe_blender_argv(self):
        self.fixture.mutate(self.fixture.packet["blender"]["execution"],
                            lambda obj: obj["process"]["argv"].remove("--disable-autoexec"))
        self.reject("argv/cwd mismatch")

    def test_failed_native_process(self):
        self.fixture.mutate(self.fixture.packet["blender"]["execution"], lambda obj: obj["process"].update(exit_code=1))
        self.reject("did not complete")

    def test_missing_process_log(self):
        self.fixture.mutate(self.fixture.packet["blender"]["execution"], lambda obj: obj["process"].pop("stdout"))
        self.reject("log missing")

    def test_stale_executable_hash(self):
        self.fixture.packet["blender"]["executable"]["sha256"] = "a"*64
        self.reject("executable hash mismatch")

    def test_export_preset_mutation(self):
        self.fixture.mutate(self.fixture.export_ref, lambda obj: obj["preset"].update(bake_space_transform=True))
        self.reject("unreviewed export preset")

    def test_blender_geometry_mutation(self):
        self.fixture.mutate(self.fixture.export_ref,
                            lambda obj: obj["blender_reimport_measurements"]["CAL_RightTip"]["position"].__setitem__(0, -.06))
        self.reject("incorrect pivot/marker")

    def test_unity_wrong_axis(self):
        self.fixture.mutate(self.fixture.measure_ref,
                            lambda obj: next(item for item in obj["measurements"] if item["name"] == "CAL_ForwardTip")["position"].update(z=-.08))
        self.reject("measurement mismatch")

    def test_unity_duplicate_object(self):
        self.fixture.mutate(self.fixture.measure_ref, lambda obj: obj["measurements"][-1].update(name="CAL_Origin"))
        self.reject("duplicate Unity measurement")

    def test_unity_corrective_scale(self):
        self.fixture.mutate(self.fixture.measure_ref, lambda obj: obj["measurements"][0]["scale"].update(x=100))
        self.reject("measurement mismatch")

    def test_unity_wrong_material(self):
        self.fixture.mutate(self.fixture.measure_ref,
                            lambda obj: next(item for item in obj["measurements"] if item["name"] == "CAL_AxisRight").update(materials=["wrong"]))
        self.reject("topology/material")

    def test_unity_missing_editor_binding(self):
        self.fixture.packet.pop("editor_binding")
        self.reject("editor_binding")

    def test_unity_missing_license_evidence(self):
        self.fixture.mutate(self.fixture.packet["editor_binding"], lambda obj: obj.update(license_evidence="missing.txt"))
        self.reject("unbound artifact")

    def test_unity_stale_source_binding(self):
        self.fixture.mutate(self.fixture.packet["editor_binding"], lambda obj: obj["source_hashes"].update({q.SOURCES[3]: "0"*64}))
        self.reject("sources stale/unbound")

    def test_unity_wrong_execute_method(self):
        self.fixture.mutate(self.fixture.packet["measurement"]["execution"],
                            lambda obj: obj["process"]["argv"].__setitem__(5, "Wrong.Method"))
        self.reject("argv/cwd mismatch")

    def test_png_ihdr_crc_mutation(self):
        data = bytearray((self.fixture.root/self.fixture.image_ref).read_bytes())
        data[16] ^= 1
        with self.assertRaisesRegex(q.QualificationError, "CRC mismatch"):
            q.png_pixels(bytes(data))

    def test_png_fake_header_without_pixels(self):
        with self.assertRaises(q.QualificationError):
            q.png_pixels(b"\x89PNG\r\n\x1a\n"+b"\0"*50)

    def test_png_wrong_dimensions(self):
        data = bytearray(png_fixture())
        data[16:20] = struct.pack(">I", 1024)
        data[29:33] = struct.pack(">I", zlib.crc32(data[12:29])&0xffffffff)
        with self.assertRaisesRegex(q.QualificationError, "2048x512"):
            q.png_pixels(bytes(data))

    def test_native_render_stale_measurement_binding(self):
        self.fixture.mutate(self.fixture.render_ref, lambda obj: obj.update(measurementsSha256="0"*64))
        self.reject("render identity mismatch")

    def test_native_render_material_override(self):
        self.fixture.mutate(self.fixture.render_ref, lambda obj: obj.update(materialOverrides=True))
        self.reject("render scope/settings")

    def test_native_render_missing_view(self):
        self.fixture.mutate(self.fixture.render_ref, lambda obj: obj["frames"].pop())
        self.reject("four native frames")

    def test_native_render_compact_scale(self):
        self.fixture.mutate(self.fixture.render_ref, lambda obj: obj["frames"][3].update(instanceScale=.5))
        self.reject("frame instance scale")

    def test_native_render_camera_framing(self):
        self.fixture.mutate(self.fixture.render_ref, lambda obj: obj["frames"][3].update(orthographicSize=.12))
        self.reject("frame framing")

    def test_native_render_pixel_count(self):
        self.fixture.mutate(self.fixture.render_ref, lambda obj: obj["frames"][0].update(nonBackgroundPixels=999))
        self.reject("pixel counts mismatch")

    def test_inspection_must_be_explicit(self):
        self.fixture.mutate(self.fixture.packet["pixel_inspection"], lambda obj: obj.update(completed=False))
        self.reject("completed pixel inspection")

    def test_inspection_missing_observation(self):
        self.fixture.mutate(self.fixture.packet["pixel_inspection"], lambda obj: obj["observations"].pop(q.OBSERVATIONS[0]))
        self.reject("required pixel observations")

    def test_inspection_crop_identity(self):
        self.fixture.mutate(self.fixture.packet["pixel_inspection"], lambda obj: obj["views"][0].update(rgba_sha256="0"*64))
        self.reject("actual view identity")

    def test_inspection_timestamp(self):
        self.fixture.mutate(self.fixture.packet["pixel_inspection"], lambda obj: obj.update(utc=self.fixture.time(5)))
        self.reject("predates rendered pixels")

    def test_preservation_not_retrospective(self):
        self.fixture.mutate("evidence/calibration/before.json", lambda obj: obj.update(captured_utc=self.fixture.time(10)))
        self.reject("bracket native executions")

    def test_preservation_hash_changes(self):
        self.fixture.mutate("evidence/calibration/after.json", lambda obj: obj["protected_artifacts"][0].update(sha256="0"*64))
        self.reject("unrelated artifact state changed")

    def test_preservation_write_scope_missing(self):
        self.fixture.mutate(self.fixture.packet["preservation"], lambda obj: obj.update(owned_write_paths=["art/calibration/"]))
        self.reject("scope omits native outputs")

    def test_preservation_process_changes(self):
        self.fixture.mutate("evidence/calibration/after.json",
                            lambda obj: obj["desktop_blender_sessions"].append({"pid": 55, "pgid": 55, "executable": "/fake/Blender", "cwd": "/fake"}))
        self.reject("desktop Blender snapshot")

    def test_duplicate_json_keys(self):
        self.fixture.write(self.fixture.export_ref, b'{"schema":"one","schema":"two"}')
        self.reject("duplicate JSON key")


if __name__ == "__main__":
    unittest.main()
