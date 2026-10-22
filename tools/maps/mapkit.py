"""Small authoring kit for RESQ text maps (32x24, southwest origin, first row = north).

Each mission has a generator script (e.g. tools/maps/w1_m1.py) that paints terrain and declares nodes,
edges, sockets and roles, then writes content/wN/mM/map.json. The C# MapCompiler is the single source of
truth for semantics; this kit only produces authoring data.
"""
from __future__ import annotations

import json
from pathlib import Path

W, H = 32, 24
ROOT = Path(__file__).resolve().parents[2]

# World 1 legend. elev in mm; cond = link conductance (mm^2/s); init_depth = initial water depth (mm).
LEGEND_W1 = {
    "#": {"terrain": "high", "code": 3, "elev": 1200},
    "=": {"terrain": "road", "code": 2, "elev": 300},
    ".": {"terrain": "terrace", "code": 1, "elev": 100},
    ",": {"terrain": "field", "code": 5, "elev": 300},
    "~": {"terrain": "channel", "code": 0, "elev": 0, "init_depth": 300, "cond": 2_000_000},
    "R": {"terrain": "reservoir", "code": 4, "elev": 0, "init_depth": 900, "cond": 2_000_000},
    "d": {"terrain": "basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "L": {"terrain": "low_terrace", "code": 7, "elev": 100},
    "D": {"terrain": "depot", "code": 8, "elev": 1200},
    "H": {"terrain": "high_landing", "code": 9, "elev": 1200},
    "o": {"terrain": "orchard", "code": 10, "elev": 100},
    "v": {"terrain": "village", "code": 11, "elev": 300},
    "w": {"terrain": "weir", "code": 12, "elev": 200, "cond": 2_000_000},
}


class Map:
    def __init__(self, mission_id: str, fill: str = "#", legend=None):
        self.mission_id = mission_id
        self.grid = [[fill] * W for _ in range(H)]  # grid[y][x]
        self.legend = legend or LEGEND_W1
        self.nodes, self.edges, self.sockets = [], [], []
        self.sources, self.sinks, self.vehicles, self.infrastructure = [], [], [], []
        self.regions, self.roles, self.groups, self.routes = {}, {}, [], []
        self.power = {"nodes": [], "links": []}
        self.tutorial = None
        self.class_overrides = {}

    # ---- painting ----
    def rect(self, ch, x0, y0, x1, y1):
        for y in range(min(y0, y1), max(y0, y1) + 1):
            for x in range(min(x0, x1), max(x0, x1) + 1):
                self.grid[y][x] = ch

    def line(self, ch, *pts):
        """x-first Manhattan polyline through points."""
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            x, y = x0, y0
            self.grid[y][x] = ch
            while x != x1:
                x += 1 if x1 > x else -1
                self.grid[y][x] = ch
            while y != y1:
                y += 1 if y1 > y else -1
                self.grid[y][x] = ch

    def cell(self, ch, x, y):
        self.grid[y][x] = ch

    def at(self, x, y):
        return self.grid[y][x]

    # ---- declarations ----
    def node(self, id, at, flags=(), **kw):
        self.nodes.append({"id": id, "at": list(at), "flags": list(flags), **kw})

    def edge(self, id, a, b, ticks, cls="ground", **kw):
        self.edges.append({"id": id, "from": a, "to": b, "ticks": ticks, "class": cls, **kw})

    def socket(self, id, tool, **kw):
        self.sockets.append({"id": id, "tool": tool, **kw})

    def to_json(self):
        rows = ["".join(self.grid[y]) for y in range(H - 1, -1, -1)]
        used = sorted({c for r in rows for c in r})
        doc = {
            "mission_id": self.mission_id,
            "rows": rows,
            "legend": {k: v for k, v in self.legend.items() if k in used},
            "regions": self.regions,
            "sources": self.sources,
            "sinks": self.sinks,
            "nodes": self.nodes,
            "edges": self.edges,
            "sockets": self.sockets,
            "power": self.power,
            "infrastructure": self.infrastructure,
            "vehicles": self.vehicles,
            "groups": self.groups,
            "routes": self.routes,
            "roles": self.roles,
        }
        if self.tutorial:
            doc["tutorial"] = self.tutorial
        if self.class_overrides:
            doc["class_overrides"] = self.class_overrides
        return doc

    def write(self, world: int, mission: int):
        out = ROOT / f"content/w{world}/m{mission}/map.json"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(self.to_json(), indent=1) + "\n")
        return out

    def ascii(self):
        return "\n".join("".join(self.grid[y]) for y in range(H - 1, -1, -1))
