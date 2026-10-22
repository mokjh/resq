"""Original RESQ calibration geometry. Pure Python; meters, Z up, -Y forward."""
import hashlib
import json
import math

VERSION = "resq-calibration-1"
ROOT = "RESQ_CAL_Unit"
TOLERANCE = 0.01
MATERIALS = {
    "CAL_Right_Red": [0.8, 0.03, 0.03, 1.0],
    "CAL_Forward_Green": [0.03, 0.6, 0.08, 1.0],
    "CAL_Up_Blue": [0.03, 0.12, 0.8, 1.0],
    "CAL_Reference_Paper": [0.85, 0.8, 0.7, 1.0],
}
# Geometry is baked into local vertices. Every mesh object has identity transforms.
BOXES = (
    ("CAL_AxisRight", (0.03, 0.0, 0.003), (0.06, 0.006, 0.006), "CAL_Right_Red"),
    ("CAL_AxisForward", (0.0, -0.04, 0.003), (0.006, 0.08, 0.006), "CAL_Forward_Green"),
    ("CAL_AxisUp", (0.0, 0.0, 0.05), (0.006, 0.006, 0.10), "CAL_Up_Blue"),
    ("CAL_Reference100mm", (-0.07, 0.03, 0.005), (0.10, 0.01, 0.01), "CAL_Reference_Paper"),
)
MARKERS = {
    "CAL_Origin": (0.0, 0.0, 0.0),
    "CAL_RightTip": (0.06, 0.0, 0.0),
    "CAL_ForwardTip": (0.0, -0.08, 0.0),
    "CAL_UpTip": (0.0, 0.0, 0.10),
    "CAL_BarStart": (-0.12, 0.03, 0.0),
    "CAL_BarEnd": (-0.02, 0.03, 0.0),
}
# Consistent outward winding, six quads / twelve triangles per box.
FACES = ((1, 3, 2, 0), (6, 7, 5, 4), (4, 5, 1, 0),
         (3, 7, 6, 2), (2, 6, 4, 0), (5, 7, 3, 1))


def vertices(center, size):
    if len(center) != 3 or len(size) != 3 or any(
        not math.isfinite(v) for v in (*center, *size)
    ) or any(v <= 0 for v in size):
        raise ValueError("finite 3D center and positive dimensions required")
    return [tuple(center[i] + signs[i] * size[i] / 2 for i in range(3))
            for signs in ((-1, -1, -1), (-1, -1, 1), (-1, 1, -1), (-1, 1, 1),
                          (1, -1, -1), (1, -1, 1), (1, 1, -1), (1, 1, 1))]


def bounds(points):
    return {"min": [min(p[i] for p in points) for i in range(3)],
            "max": [max(p[i] for p in points) for i in range(3)]}


def specification():
    return {"version": VERSION, "root": ROOT, "units": "meters",
            "source_axes": {"up": "+Z", "forward": "-Y", "right": "+X"},
            "target_axes": {"up": "+Y", "forward": "+Z", "right": "+X"},
            "tolerance_relative": TOLERANCE,
            "materials": MATERIALS, "markers": MARKERS,
            "meshes": [{"name": name, "center": center, "size": size,
                        "material": material, "vertices": vertices(center, size),
                        "faces": FACES, "bounds": bounds(vertices(center, size)),
                        "triangles": 12} for name, center, size, material in BOXES]}


def geometry_hash():
    return hashlib.sha256(json.dumps(specification(), sort_keys=True,
                                    separators=(",", ":")).encode()).hexdigest()


def assert_measurements(record, tolerance=1e-6):
    """Fail closed against independent measured Blender world-space records."""
    expected_names = {ROOT, *MARKERS, *(item[0] for item in BOXES)}
    if set(record) != expected_names:
        raise ValueError("unexpected/missing calibration objects")
    for name in expected_names:
        item = record[name]
        for key, expected in (("scale", (1, 1, 1)), ("rotation", (0, 0, 0))):
            if len(item[key]) != 3 or any(not math.isfinite(v) or abs(v-e) > tolerance
                                        for v, e in zip(item[key], expected)):
                raise ValueError(f"{name}: nonidentity {key}")
    for name, point in {ROOT: (0, 0, 0), **MARKERS}.items():
        measured = record[name]["position"]
        if len(measured) != 3 or any(not math.isfinite(v) or abs(v-e) > tolerance
                                   for v, e in zip(measured, point)):
            raise ValueError(f"{name}: incorrect pivot/marker position")
    for name, center, size, material in BOXES:
        item = record[name]
        if len(item["position"]) != 3 or any(not math.isfinite(v) or abs(v) > tolerance
                                             for v in item["position"]):
            raise ValueError(f"{name}: mesh origin must remain at the root")
        expected = bounds(vertices(center, size))
        for key in ("min", "max"):
            measured = item["bounds"][key]
            if len(measured) != 3 or any(not math.isfinite(v) or abs(v-e) > tolerance
                                       for v, e in zip(measured, expected[key])):
                raise ValueError(f"{name}: incorrect {key} bounds")
        if item["triangles"] != 12 or item["materials"] != [material]:
            raise ValueError(f"{name}: topology/material mismatch")
