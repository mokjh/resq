"""Lightweight geometry/negative measurement tests; no Blender/Unity execution."""
import copy
import math
import unittest

import calibration_geometry as g


def correct_measurements():
    record = {name: {"position": list(p), "scale": [1, 1, 1], "rotation": [0, 0, 0]}
              for name, p in {g.ROOT: (0, 0, 0), **g.MARKERS}.items()}
    for mesh in g.specification()["meshes"]:
        record[mesh["name"]] = {"position": [0, 0, 0], "scale": [1, 1, 1],
                                "rotation": [0, 0, 0], "bounds": mesh["bounds"],
                                "triangles": 12, "materials": [mesh["material"]]}
    return copy.deepcopy(record)


class GeometryTests(unittest.TestCase):
    def test_box_faces_have_outward_normals(self):
        for mesh in g.specification()["meshes"]:
            vertices = mesh["vertices"]
            for face in mesh["faces"]:
                a, b, c = (vertices[i] for i in face[:3])
                u, v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
                normal = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
                outward = [a[i]-mesh["center"][i] for i in range(3)]
                self.assertGreater(sum(normal[i]*outward[i] for i in range(3)), 0)

    def test_bar_is_100mm_and_three_axes_distinct(self):
        self.assertAlmostEqual(math.dist(g.MARKERS["CAL_BarStart"], g.MARKERS["CAL_BarEnd"]), 0.1)
        self.assertEqual(g.MARKERS["CAL_ForwardTip"], (0, -0.08, 0))
        self.assertEqual(g.MARKERS["CAL_UpTip"], (0, 0, 0.1))
        g.assert_measurements(correct_measurements())

    def test_measurement_failures_do_not_pass(self):
        mutations = (
            lambda r: r.pop("CAL_Origin"),
            lambda r: r["CAL_Reference100mm"]["bounds"]["max"].__setitem__(0, 10),
            lambda r: r["CAL_ForwardTip"]["position"].__setitem__(1, 0.08),
            lambda r: r[g.ROOT]["rotation"].__setitem__(0, 1.57),
            lambda r: r["CAL_AxisUp"]["scale"].__setitem__(2, 100),
            lambda r: r["CAL_AxisRight"].__setitem__("materials", [None]),
            lambda r: r["CAL_Origin"]["position"].__setitem__(0, float("nan")),
            lambda r: r["CAL_AxisUp"].__setitem__("triangles", 0),
            lambda r: r["CAL_Reference100mm"]["position"].__setitem__(0, 0.01),
        )
        for mutate in mutations:
            record = correct_measurements()
            mutate(record)
            with self.assertRaises(ValueError):
                g.assert_measurements(record)

    def test_reject_nonfinite_or_negative_geometry(self):
        for size in ((1, -1, 1), (0, 1, 1), (float("inf"), 1, 1)):
            with self.assertRaises(ValueError):
                g.vertices((0, 0, 0), size)


if __name__ == "__main__":
    unittest.main()
