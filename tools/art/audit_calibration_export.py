"""Validate an actual generator report and retained source/export bytes.

This audit deliberately cannot accept F06: native Unity measurement, inspected
render and unrelated-state/process provenance are separate required evidence.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import calibration_geometry as geometry


def audit(root, report_path):
    root = Path(root).resolve()
    report = json.loads(Path(report_path).read_text())
    if report.get("schema") != "resq-calibration-export-1":
        raise ValueError("wrong report schema")
    if report.get("background") is not True or report.get("autoexec_disabled") is not True:
        raise ValueError("unsafe generation settings")
    if report.get("geometry_sha256") != geometry.geometry_hash():
        raise ValueError("geometry version/hash mismatch")
    sources = {"generator_sha256": "tools/art/generate_calibration.py",
               "geometry_source_sha256": "tools/art/calibration_geometry.py"}
    for field, path in sources.items():
        if report.get(field) != hashlib.sha256((root / path).read_bytes()).hexdigest():
            raise ValueError("report does not bind current generator sources")
    expected = {"art/calibration/unit.blend": b"BLENDER",
                "art/calibration/unit.fbx": b"Kaydara FBX Binary  \x00\x1a\x00"}
    artifacts = report.get("artifacts", [])
    if len(artifacts) != 2 or {a.get("path") for a in artifacts} != set(expected):
        raise ValueError("missing/unexpected source/export artifacts")
    for item in artifacts:
        path = root / item["path"]
        if path.is_symlink() or path.resolve() != path or not path.is_file():
            raise ValueError("artifact must be a regular owned file")
        data = path.read_bytes()
        if not data.startswith(expected[item["path"]]) or len(data) < 100:
            raise ValueError("invalid native artifact signature/size")
        if len(data) != item.get("bytes") or hashlib.sha256(data).hexdigest() != item.get("sha256"):
            raise ValueError("artifact hash/size mismatch")
    geometry.assert_measurements(report["source_measurements"])
    geometry.assert_measurements(report["blender_reimport_measurements"], tolerance=1e-5)
    preset = report.get("preset", {})
    required = {"global_scale": 1.0, "apply_unit_scale": True,
                "apply_scale_options": "FBX_SCALE_UNITS", "axis_forward": "-Z", "axis_up": "Y",
                "use_space_transform": True, "bake_space_transform": False,
                "object_types": ["EMPTY", "MESH"], "use_selection": True,
                "use_mesh_modifiers": False, "add_leaf_bones": False, "bake_anim": False,
                "use_custom_props": True, "path_mode": "STRIP", "embed_textures": False}
    if preset != required:
        raise ValueError("unreviewed export preset")
    return {"blender_export_record_valid": True, "F06_accepted": False,
            "pending": ["actual licensed Unity import measurements", "Unity render inspection",
                        "coordinator process/unrelated-state provenance", "registered F06 acceptance"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=str(Path(__file__).resolve().parents[2]))
    parser.add_argument("--export-report", required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(audit(args.root, args.export_report), indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"calibration export audit failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
