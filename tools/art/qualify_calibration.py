"""Validate a complete F06 evidence packet without executing or accepting F06.

This future inlet validates retained records and their byte bindings. It cannot
prove that an author truthfully described a process, inspection, or preservation
capture. The registered runner and independent evidence review remain necessary.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys
import zlib

# Existing reviewed export auditor uses a local bare geometry import. Support
# both `from art.qualify_calibration import qualify` and direct CLI invocation.
sys.path.insert(0, str(Path(__file__).resolve().parent))
import audit_calibration_export
import calibration_geometry as geometry


class QualificationError(ValueError):
    """Missing, stale, unsafe, or inconsistent qualification evidence."""


VIEWS = ("wide-three-quarter", "wide-reverse", "wide-top", "compact-three-quarter")
OBSERVATIONS = ("red_right", "green_forward", "blue_up", "reference_endpoints_visible",
                "no_magenta_missing_materials", "no_clipping_backface_holes",
                "compact_scale_5_over_6", "reference_bar_matches_measurements")
SOURCES = ("tools/art/generate_calibration.py", "tools/art/calibration_geometry.py",
           "tools/art/audit_calibration_export.py",
           "game/Assets/Resq/Art/Calibration/Editor/CalibrationMeasurement.cs",
           "game/Assets/Resq/Art/Calibration/Editor/CalibrationContactSheet.cs")


def require(condition, message):
    if not condition:
        raise QualificationError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def timestamp(value, label):
    require(isinstance(value, str), label + ": UTC timestamp required")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise QualificationError(label + ": invalid timestamp") from error
    require(parsed.tzinfo is not None and parsed.utcoffset().total_seconds() == 0,
            label + ": timestamp must be UTC")
    require(parsed <= datetime.now(timezone.utc), label + ": future timestamp")
    return parsed


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def near(value, expected, label, tolerance=1e-5):
    require(finite(value) and abs(value - expected) <= tolerance, label + ": measurement mismatch")


def vector(value, expected, label):
    require(isinstance(value, dict) and set(value) == {"x", "y", "z"}, label + ": vector required")
    for axis, target in zip(("x", "y", "z"), expected):
        near(value[axis], target, label + "." + axis)


class Inventory:
    def __init__(self, root, records):
        self.root = Path(root).resolve()
        self.records = {}
        require(isinstance(records, list) and records, "artifact inventory missing")
        for item in records:
            require(isinstance(item, dict) and set(item) == {"path", "bytes", "sha256"},
                    "artifact requires exact path/bytes/sha256 fields")
            name = item["path"]
            require(isinstance(name, str) and name and not Path(name).is_absolute()
                    and Path(name).as_posix() == name and ".." not in Path(name).parts,
                    "artifact path must be canonical and relative")
            require(name not in self.records, "duplicate artifact: " + name)
            path = self.root / name
            require(path.resolve() == path and path.is_file(), "artifact missing/symlink/escape: " + name)
            data = path.read_bytes()
            require(type(item["bytes"]) is int and item["bytes"] == len(data)
                    and item["sha256"] == digest(data), "artifact hash/size mismatch: " + name)
            self.records[name] = item

    def path(self, ref):
        require(isinstance(ref, str) and ref in self.records, "unbound artifact reference: " + str(ref))
        return self.root / ref

    def read(self, ref):
        return self.path(ref).read_bytes()

    def json(self, ref, schema=None):
        try:
            # Reject duplicate keys and JSON NaN/Infinity rather than silently normalizing them.
            def unique(pairs):
                obj = {}
                for key, value in pairs:
                    require(key not in obj, "duplicate JSON key: " + key)
                    obj[key] = value
                return obj
            obj = json.loads(self.read(ref), object_pairs_hook=unique,
                             parse_constant=lambda value: require(False, "nonfinite JSON: " + value))
        except (UnicodeError, json.JSONDecodeError) as error:
            raise QualificationError("invalid JSON artifact: " + str(ref)) from error
        require(isinstance(obj, dict), "JSON object required: " + str(ref))
        if schema:
            require(obj.get("schema") == schema, "wrong schema: " + str(ref))
        return obj

    def sha(self, ref):
        self.path(ref)
        return self.records[ref]["sha256"]


def executable(record):
    require(isinstance(record, dict) and set(record) == {"path", "bytes", "sha256", "version"},
            "exact executable path/bytes/hash/version required")
    path = Path(record["path"])
    require(path.is_absolute() and path.resolve() == path and path.is_file(),
            "executable must be an existing absolute regular file")
    require(type(record["bytes"]) is int and path.stat().st_size == record["bytes"],
            "executable size mismatch")
    with path.open("rb") as source:
        sha = hashlib.file_digest(source, "sha256").hexdigest()
    require(sha == record["sha256"], "executable hash mismatch")
    require(isinstance(record["version"], str) and record["version"].strip(), "executable version missing")


def process(inv, stage, argv, version):
    executable(stage["executable"])
    execution = inv.json(stage["execution"])
    require(execution.get("executable_sha256") == stage["executable"]["sha256"],
            "execution executable identity missing/mismatch")
    run = execution.get("process", execution)
    require(isinstance(run, dict), "process record missing")
    require(run.get("argv") == argv and run.get("cwd") == str(inv.root), "process argv/cwd mismatch")
    require(run.get("status") == "passed" and type(run.get("exit_code")) is int
            and run["exit_code"] == 0 and run.get("timed_out") is False
            and run.get("launch_error") is None and run.get("stop_reason") is None
            and run.get("output_errors") == [], "native process did not complete successfully")
    for key in ("pid", "pgid", "parent_pid"):
        require(type(run.get(key)) is int and run[key] > 0, "process identity missing: " + key)
    require(bool(run.get("owner")), "process owner missing")
    begin = timestamp(run.get("started_utc"), "process start")
    end = timestamp(run.get("ended_utc"), "process end")
    require(begin < end and finite(run.get("duration_seconds")) and run["duration_seconds"] > 0,
            "invalid process time interval")
    require(abs((end-begin).total_seconds()-run["duration_seconds"]) < 2,
            "process duration disagrees with timestamps")
    logs = []
    for field in ("stdout", "stderr"):
        log = run.get(field)
        require(isinstance(log, dict), "process " + field + " log missing")
        raw_path = log.get("path")
        require(isinstance(raw_path, str), "process log path missing")
        try:
            ref = str(Path(raw_path).relative_to(inv.root)) if Path(raw_path).is_absolute() else raw_path
        except ValueError as error:
            raise QualificationError("process log outside workspace") from error
        require(log.get("sha256") == inv.sha(ref) and log.get("byte_length") == len(inv.read(ref)),
                "process log identity mismatch")
        logs.append(inv.read(ref).decode("utf-8", errors="replace"))
    if "log" in stage:
        logs.append(inv.read(stage["log"]).decode("utf-8", errors="replace"))
    require(version in "\n".join(logs), "native logs do not bind executable version")
    if "--python" in argv:
        success = "RESQ calibration Blender export/reimport recorded; Unity acceptance remains pending"
    elif "Resq.Art.Calibration.CalibrationMeasurement.ImportAndMeasure" in argv:
        success = "RESQ calibration measurements recorded. Unity render inspection/F06 acceptance pending."
    else:
        success = "Actual Unity calibration pixels written. Pixel inspection and F06 acceptance remain pending."
    require(success in "\n".join(logs), "native completion log missing")
    require(stage["executable"]["version"] == version, "executable/report version mismatch")
    return run, begin, end


def png_pixels(data):
    """Validate actual noninterlaced RGB(A) PNG chunks, CRC, dimensions and pixels."""
    require(data.startswith(b"\x89PNG\r\n\x1a\n"), "actual PNG signature missing")
    offset, compressed, header, ended = 8, bytearray(), None, False
    while offset < len(data):
        require(offset + 12 <= len(data), "truncated PNG chunk")
        size = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset+4:offset+8]
        require(offset+12+size <= len(data), "truncated PNG payload")
        body = data[offset+8:offset+8+size]
        crc = struct.unpack_from(">I", data, offset+8+size)[0]
        require(zlib.crc32(kind+body) & 0xffffffff == crc, "PNG CRC mismatch")
        if header is None:
            require(kind == b"IHDR" and size == 13, "PNG IHDR must be first")
            header = struct.unpack(">IIBBBBB", body)
            require(header[:3] == (2048, 512, 8) and header[3] in (2, 6)
                    and header[4:] == (0, 0, 0), "PNG must be 2048x512 8-bit noninterlaced RGB(A)")
        elif kind == b"IHDR":
            raise QualificationError("duplicate PNG IHDR")
        if kind == b"IDAT":
            compressed.extend(body)
        if kind == b"IEND":
            require(size == 0 and offset+12 == len(data), "PNG trailing data/invalid IEND")
            ended = True
            break
        require(kind in (b"IHDR", b"IDAT", b"IEND", b"PLTE") or kind[0] & 32,
                "unsupported critical PNG chunk")
        offset += size+12
    require(ended and compressed, "PNG data/IEND missing")
    channels = 3 if header[3] == 2 else 4
    stride = 2048*channels
    decoder = zlib.decompressobj()
    try:
        raw = decoder.decompress(bytes(compressed), (stride+1)*512+1)
    except zlib.error as error:
        raise QualificationError("invalid PNG deflate") from error
    require(decoder.eof and not decoder.unused_data and not decoder.unconsumed_tail
            and len(raw) == (stride+1)*512, "PNG decoded size mismatch")
    previous, rows = bytearray(stride), []
    for y in range(512):
        start = y*(stride+1)
        mode, row = raw[start], bytearray(raw[start+1:start+1+stride])
        require(mode <= 4, "unsupported PNG filter")
        for x in range(stride):
            left = row[x-channels] if x >= channels else 0
            up = previous[x]
            corner = previous[x-channels] if x >= channels else 0
            if mode == 1:
                predictor = left
            elif mode == 2:
                predictor = up
            elif mode == 3:
                predictor = (left+up)//2
            elif mode == 4:
                p = left+up-corner
                distances = (abs(p-left), abs(p-up), abs(p-corner))
                predictor = (left, up, corner)[distances.index(min(distances))]
            else:
                predictor = 0
            row[x] = (row[x]+predictor) & 255
        rows.append(bytes(row))
        previous = row
    views = []
    for index in range(4):
        pixels = bytearray()
        for row in rows:
            for x in range(index*512*channels, (index+1)*512*channels, channels):
                pixels.extend(row[x:x+3])
                pixels.append(row[x+3] if channels == 4 else 255)
        background = pixels[:3]
        varied = sum(sum(abs(pixels[p+i]-background[i]) for i in range(3)) > 12
                     for p in range(0, len(pixels), 4))
        magenta = sum(pixels[p] > 240 and pixels[p+2] > 240 and pixels[p+1] < 25
                      for p in range(0, len(pixels), 4))
        require(varied >= 128 and magenta == 0, "PNG contains blank/error-shader view")
        views.append({"rgba_sha256": digest(pixels), "nonBackgroundPixels": varied, "magentaPixels": magenta})
    return views


def measurements(record):
    require(record.get("schema") == "resq-calibration-unity-1", "wrong Unity measurement schema")
    near(record.get("globalScale"), 1, "globalScale")
    require(finite(record.get("fileScale")) and record["fileScale"] > 0, "invalid native fileScale")
    require(record.get("bakeAxisConversion") is True and record.get("useFileScale") is True
            and record.get("materialImportMode") == "ImportStandard", "unreviewed Unity importer settings")
    items = record.get("measurements")
    require(isinstance(items, list), "Unity measurements missing")
    expected_names = {geometry.ROOT, *geometry.MARKERS, *(box[0] for box in geometry.BOXES)}
    # Unity instantiated model appends (Clone) to its root only.
    require(len(items) == len(expected_names), "unexpected Unity object count")
    by_name = {}
    for item in items:
        require(isinstance(item, dict) and isinstance(item.get("name"), str), "measurement name missing")
        name = item["name"]
        if name == geometry.ROOT+"(Clone)":
            name = geometry.ROOT
        require(name not in by_name, "duplicate Unity measurement: " + name)
        by_name[name] = item
        vector(item.get("scale"), (1, 1, 1), name+" scale")
        vector(item.get("rotation"), (0, 0, 0), name+" rotation")
    require(set(by_name) == expected_names, "unexpected/missing Unity object names")
    for name, position in {geometry.ROOT: (0, 0, 0), **geometry.MARKERS}.items():
        x, y, z = position
        vector(by_name[name].get("position"), (x, z, -y), name+" position")
    for name, center, size, material in geometry.BOXES:
        item = by_name[name]
        points = [(x, z, -y) for x, y, z in geometry.vertices(center, size)]
        bounds = geometry.bounds(points)
        vector(item.get("position"), (0, 0, 0), name+" pivot")
        vector(item.get("boundsMin"), bounds["min"], name+" minimum")
        vector(item.get("boundsMax"), bounds["max"], name+" maximum")
        require(type(item.get("triangles")) is int and item["triangles"] == 12
                and item.get("materials") == [material], name+": topology/material mismatch")
    for name in expected_names - {box[0] for box in geometry.BOXES}:
        require(by_name[name].get("materials") in (None, [])
                and by_name[name].get("triangles", 0) == 0, "unexpected mesh on marker/root")


def preservation(inv, ref, runs):
    record = inv.json(ref, "resq-calibration-preservation-1")
    require(isinstance(record.get("coordinator"), str) and record["coordinator"].strip(), "preservation coordinator missing")
    require(record.get("execution_refs") == [entry[0] for entry in runs], "preservation execution refs mismatch")
    require(record.get("owned_process_ids") == [entry[1]["pid"] for entry in runs], "preservation owned process set mismatch")
    writes = record.get("owned_write_paths")
    require(isinstance(writes, list) and writes and len(writes) == len(set(writes)), "owned write scope missing")
    for path in writes:
        require(isinstance(path, str) and not Path(path).is_absolute() and ".." not in Path(path).parts
                and path.startswith(("art/calibration/", "evidence/calibration/", "game/")), "unreviewed write scope")
    required_writes = ["art/calibration/unit.blend", "art/calibration/unit.fbx",
                       "game/Assets/Resq/Art/Calibration/unit.fbx",
                       "game/Assets/Resq/Art/Calibration/unit.fbx.meta"]
    for _, run, _, _ in runs:
        args = run["argv"]
        for flag in ("--report", "-resqCalibrationReport", "-resqCalibrationImage", "-resqCalibrationRenderReport", "-logFile"):
            if flag in args:
                required_writes.append(str(Path(args[args.index(flag)+1]).relative_to(inv.root)))
        for field in ("stdout", "stderr"):
            path = Path(run[field]["path"])
            required_writes.append(str(path.relative_to(inv.root)) if path.is_absolute() else str(path))
    require(all(any(name == write.rstrip("/") or name.startswith(write.rstrip("/")+"/")
                    for write in writes) for name in required_writes), "owned write scope omits native outputs")
    before = inv.json(record.get("before_snapshot"), "resq-calibration-preservation-snapshot-1")
    after = inv.json(record.get("after_snapshot"), "resq-calibration-preservation-snapshot-1")
    start = min(entry[2] for entry in runs)
    end = max(entry[3] for entry in runs)
    require(timestamp(before.get("captured_utc"), "before capture") <= start
            and timestamp(after.get("captured_utc"), "after capture") >= end,
            "preservation snapshots must bracket native executions; no retrospective before record")
    require(record.get("capture_method") == "contemporaneous_coordinator_capture", "preservation capture method missing")
    protected = before.get("protected_artifacts")
    require(isinstance(protected, list) and protected, "nonempty protected artifact inventory required")
    require(protected == after.get("protected_artifacts"), "unrelated artifact state changed")
    seen = set()
    for item in protected:
        require(isinstance(item, dict) and set(item) == {"path", "bytes", "sha256"}, "protected artifact identity missing")
        path = Path(item["path"])
        require(path.is_absolute() and str(path) not in seen and path.resolve() == path and path.is_file(),
                "protected artifact must be a unique retained regular file")
        seen.add(str(path))
        require(not any(str(path) == str(inv.root / write) or str(path).startswith(str(inv.root / write)+"/")
                        for write in writes), "protected artifact overlaps owned write scope")
        require(type(item["bytes"]) is int and path.stat().st_size == item["bytes"]
                and digest(path.read_bytes()) == item["sha256"], "protected artifact hash/size mismatch")
    for field in ("unrelated_processes", "desktop_blender_sessions"):
        require(isinstance(before.get(field), list) and before[field] == after.get(field),
                "missing/changed unrelated process or desktop Blender snapshot")
        for item in before[field]:
            require(isinstance(item, dict) and type(item.get("pid")) is int and item["pid"] > 0
                    and type(item.get("pgid")) is int and item["pgid"] > 0
                    and isinstance(item.get("executable"), str) and item["executable"]
                    and isinstance(item.get("cwd"), str) and item["cwd"], "incomplete preserved process identity")
            require(item["pid"] not in record["owned_process_ids"], "unrelated process overlaps owned process set")
    require(before.get("desktop_state_sha256") == after.get("desktop_state_sha256")
            and isinstance(before.get("desktop_state_sha256"), str)
            and re.fullmatch("[0-9a-f]{64}", before["desktop_state_sha256"]), "desktop state binding missing/changed")
    for snapshot in (before, after):
        require(inv.sha(snapshot.get("desktop_state_artifact")) == snapshot["desktop_state_sha256"],
                "desktop state capture artifact mismatch")
    return record


def _qualify(root, packet):
    require(isinstance(packet, dict) and packet.get("schema") == "resq-calibration-qualification-input-1",
            "qualification input schema missing/wrong")
    inv = Inventory(root, packet.get("artifacts"))
    for source in SOURCES:
        inv.path(source)
    blender, measurement, render = (packet[key] for key in ("blender", "measurement", "render"))
    export = inv.json(blender["report"], "resq-calibration-export-1")
    audit_calibration_export.audit(inv.root, inv.path(blender["report"]))
    retained_audit = inv.json(blender["independent_audit"])
    require(retained_audit.get("blender_export_record_valid") is True
            and retained_audit.get("F06_accepted") is False, "independent Blender audit missing/invalid")
    for item in export["artifacts"]:
        require(item == inv.records.get(item["path"]), "export artifact inventory mismatch")
    blender_argv = [blender["executable"]["path"], "--background", "--factory-startup", "--disable-autoexec",
                    "--python-exit-code", "1", "--python", str(inv.path(SOURCES[0])), "--", "--output-dir",
                    str(inv.root / "art/calibration"), "--report", str(inv.path(blender["report"]))]
    run_b, start_b, end_b = process(inv, blender, blender_argv, export["blender_version"])
    if "executable_sha256" in inv.json(blender["execution"]):
        require(inv.json(blender["execution"])["executable_sha256"] == blender["executable"]["sha256"],
                "Blender execution executable hash mismatch")
    unity = inv.json(measurement["report"], "resq-calibration-unity-1")
    measurements(unity)
    require(unity.get("fbxSha256") == inv.sha("art/calibration/unit.fbx")
            == inv.sha("game/Assets/Resq/Art/Calibration/unit.fbx"), "Unity imported FBX identity mismatch")
    binding = inv.json(packet["editor_binding"], "resq-calibration-editor-binding-1")
    require(binding.get("projectPath") == str(inv.root / "game")
            and binding.get("unityVersion") == unity.get("unityVersion")
            and binding.get("executable") == measurement["executable"] == render["executable"],
            "Unity Editor/project/version binding mismatch")
    require(binding.get("license_status") == "valid" and inv.read(binding.get("license_evidence")).strip(),
            "actual Editor license evidence missing")
    require(binding.get("source_hashes") == {name: inv.sha(name) for name in SOURCES[3:]},
            "Unity execution sources stale/unbound")
    base = [measurement["executable"]["path"], "-batchmode", "-projectPath", str(inv.root / "game"), "-executeMethod"]
    measure_argv = base + ["Resq.Art.Calibration.CalibrationMeasurement.ImportAndMeasure", "-resqCalibrationReport",
                           str(inv.path(measurement["report"])), "-logFile", str(inv.path(measurement["log"]))]
    run_m, start_m, end_m = process(inv, measurement, measure_argv, unity["unityVersion"])
    require(start_m >= end_b and start_m <= timestamp(unity.get("utc"), "Unity measurements") <= end_m,
            "Unity measurement chronology mismatch")
    rendered = inv.json(render["report"], "resq-calibration-unity-render-1")
    render_argv = base + ["Resq.Art.Calibration.CalibrationContactSheet.Render", "-resqCalibrationMeasurements",
                          str(inv.path(measurement["report"])), "-resqCalibrationImage", str(inv.path(render["image"])),
                          "-resqCalibrationRenderReport", str(inv.path(render["report"])), "-logFile", str(inv.path(render["log"]))]
    run_r, start_r, end_r = process(inv, render, render_argv, unity["unityVersion"])
    require(start_r >= end_m and start_r <= timestamp(rendered.get("utc"), "Unity render") <= end_r,
            "Unity render chronology mismatch")
    for field, expected in (("fbxSha256", unity["fbxSha256"]), ("unityVersion", unity["unityVersion"]),
                            ("measurementsSha256", inv.sha(measurement["report"])),
                            ("pngSha256", inv.sha(render["image"])),
                            ("importMetaSha256", inv.sha("game/Assets/Resq/Art/Calibration/unit.fbx.meta"))):
        require(rendered.get(field) == expected, "render identity mismatch: " + field)
    require(rendered.get("width") == 2048 and rendered.get("height") == 512
            and rendered.get("materialOverrides") is False and rendered.get("F06Accepted") is False
            and rendered.get("pixelInspectionPerformed") is False and rendered.get("evidence_kind") == "automated",
            "invalid native render scope/settings")
    require(isinstance(rendered.get("graphicsDevice"), str) and rendered["graphicsDevice"].strip()
            and "null" not in rendered["graphicsDevice"].lower(), "actual graphics device missing")
    require(isinstance(rendered.get("pipeline"), str) and rendered["pipeline"].strip()
            and rendered.get("colorSpace") in ("Linear", "Gamma")
            and isinstance(rendered.get("assetDependencyHash"), str)
            and re.fullmatch("[0-9a-f]{32}", rendered["assetDependencyHash"]), "render pipeline/dependency binding missing")
    require(isinstance(rendered.get("shaders"), list) and rendered["shaders"]
            and len(rendered["shaders"]) == len(set(rendered["shaders"]))
            and all(isinstance(shader, str) and shader.strip() and "error" not in shader.lower()
                    for shader in rendered["shaders"]), "invalid render shader records")
    pixels = png_pixels(inv.read(render["image"]))
    frames = rendered.get("frames")
    require(isinstance(frames, list) and len(frames) == 4, "exactly four native frames required")
    positions = ((.25, .20, .30), (-.25, .20, -.30), (0, .40, 0), (.25, .20, .30))
    for index, frame in enumerate(frames):
        require(frame.get("name") == VIEWS[index], "native frame order/name mismatch")
        vector(frame.get("cameraPosition"), positions[index], "camera position")
        near(frame.get("instanceScale"), 5/6 if index == 3 else 1, "frame instance scale", 1e-6)
        near(frame.get("orthographicSize"), .15, "frame framing", 1e-6)
        require(all(frame.get(key) == pixels[index][key] for key in ("nonBackgroundPixels", "magentaPixels")),
                "native frame pixel counts mismatch")
    inspection = inv.json(packet["pixel_inspection"], "resq-calibration-pixel-inspection-1")
    require(inspection.get("completed") is True and inspection.get("image") == render["image"]
            and inspection.get("pngSha256") == rendered["pngSha256"]
            and inspection.get("render_report") == render["report"]
            and inspection.get("renderSha256") == inv.sha(render["report"])
            and inspection.get("measurementsSha256") == inv.sha(measurement["report"])
            and inspection.get("fbxSha256") == unity["fbxSha256"]
            and inspection.get("importMetaSha256") == rendered["importMetaSha256"]
            and inspection.get("unityVersion") == unity["unityVersion"]
            and inspection.get("width") == 2048 and inspection.get("height") == 512,
            "completed pixel inspection identity missing/mismatch")
    require(timestamp(inspection.get("utc"), "pixel inspection") >= end_r, "inspection predates rendered pixels")
    inspector = inspection.get("inspector")
    require(isinstance(inspector, dict) and inspector.get("kind") in ("human", "ai")
            and isinstance(inspector.get("identity"), str) and inspector["identity"].strip(), "actual inspector identity missing")
    inspected_views = inspection.get("views")
    require(isinstance(inspected_views, list) and len(inspected_views) == 4, "four inspected view records required")
    for index, view in enumerate(inspected_views):
        require(view.get("name") == VIEWS[index] and view.get("rgba_sha256") == pixels[index]["rgba_sha256"],
                "inspected actual view identity mismatch")
    observations = inspection.get("observations")
    require(isinstance(observations, dict) and set(observations) == set(OBSERVATIONS), "required pixel observations missing")
    for key, observation in observations.items():
        require(isinstance(observation, dict) and observation.get("passed") is True
                and isinstance(observation.get("observation"), str) and observation["observation"].strip(),
                "incomplete/failed pixel observation: " + key)
    preservation(inv, packet["preservation"], [(blender["execution"], run_b, start_b, end_b),
                                              (measurement["execution"], run_m, start_m, end_m),
                                              (render["execution"], run_r, start_r, end_r)])
    return {"status": "complete_structural_validation", "accepted_task": False,
            "evidence_limits": "Retained record/byte consistency only; process, preservation and inspector truthfulness require independent review. AI inspection is not human/device acceptance.",
            "assertions": {
                "check-f06-a01": {"export_report": blender["report"], "execution": blender["execution"], "safe_argv_validated": True},
                "check-f06-a02": {"measurement_report": measurement["report"], "strict_geometry_materials_validated": True},
                "check-f06-a03": {"render_report": render["report"], "image": render["image"], "inspection": packet["pixel_inspection"], "inspector_kind": inspector["kind"]},
                "check-f06-a04": {"preservation": packet["preservation"], "contemporaneous_snapshot_bindings_validated": True}},
            "artifacts": [{"path": name, "sha256": inv.records[name]["sha256"],
                           "size_bytes": inv.records[name]["bytes"]} for name in sorted(inv.records)]}


def qualify(root: Path, packet: dict) -> dict:
    """Validate all four F06 evidence bundles or raise QualificationError; no writes."""
    try:
        return _qualify(root, packet)
    except QualificationError:
        raise
    except (OSError, KeyError, TypeError, ValueError, AttributeError, IndexError, OverflowError) as error:
        raise QualificationError("missing or malformed qualification evidence: " + str(error)) from error


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--packet", type=Path, required=True)
    args = parser.parse_args()
    try:
        packet = json.loads(args.packet.read_text())
        print(json.dumps(qualify(args.root, packet), indent=2))
        return 0
    except (QualificationError, OSError, ValueError) as error:
        print("calibration qualification blocked: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
