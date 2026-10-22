"""World 3 (Frostline) authoring helpers on top of mapkit.

Frostline is dry except W3-M4's lower basin: rock high ground (1200 mm) fills the board, snow terraces, roads,
the avalanche slope, rock shelves and cable lines are painted for presentation only. Navigation is the authored
graph; stability, runout and winch rules come from `supports`, `runouts` and winch sockets (docs/map-schema.md,
spec 02 §5 and §8, DECISIONS 23 Oct).

Conventions (spec 04 §2):
- group start on the anchor, assembly one cell south, pickup two cells south; 20-tick edges;
- short ground legs total 300 operation ticks, long legs 600, cable legs 50 (2 seats, 1 cabin);
- permanent receiving capacity (depot + high landing) equals the population; compact reverses primary and
  secondary receiving capacity and lengthens the outer approach to the new primary (high landing);
- every permitted tool has max(2, stock + 1) sockets.
"""
from __future__ import annotations

from mapkit import Map

# W1 codes reused where the meaning matches; 20+ are Frostline terrain codes.
LEGEND_W3 = {
    "#": {"terrain": "rock", "code": 3, "elev": 1200},
    "=": {"terrain": "road", "code": 2, "elev": 300},
    ".": {"terrain": "snow_terrace", "code": 20, "elev": 1200},
    "v": {"terrain": "village", "code": 11, "elev": 300},
    "D": {"terrain": "depot", "code": 8, "elev": 1200},
    "H": {"terrain": "high_landing", "code": 9, "elev": 1200},
    "a": {"terrain": "avalanche_slope", "code": 21, "elev": 1200},
    "k": {"terrain": "rock_shelf", "code": 22, "elev": 1200},
    "S": {"terrain": "cable_station", "code": 23, "elev": 1200},
    "O": {"terrain": "observatory", "code": 24, "elev": 1200},
    "t": {"terrain": "footpath", "code": 25, "elev": 1200},
    "|": {"terrain": "cable_line", "code": 26, "elev": 1200},
    "u": {"terrain": "upper_terrace", "code": 27, "elev": 1200},
    "m": {"terrain": "mid_terrace", "code": 28, "elev": 1200},
    "x": {"terrain": "decorative_rocks", "code": 29, "elev": 1200},
}

ROAD = dict(slots=2)                   # nav default: two movement slots
STAIR = dict(slots=2, step_free=False)  # icy stairs and switchbacks: standard residents only
CABLE = dict(cls="cable", slots=1)


class W3Map(Map):
    def __init__(self, mission_id: str, legend_extra: dict | None = None):
        legend = dict(LEGEND_W3)
        if legend_extra:
            legend.update(legend_extra)
        super().__init__(mission_id, fill="#", legend=legend)
        self.supports = []
        self.runouts = []

    def group_nodes(self, gid, at, capacity, pickup_capacity=None, south=True, pickup_flags=("assembly",)):
        """start on the anchor, assembly one cell south, pickup two cells south (north at the board edge)."""
        x, y = at
        dy = -1 if south else 1
        self.node(f"{gid}_start", (x, y), ["assembly"], capacity=capacity)
        self.node(f"{gid}_assembly", (x, y + dy), ["assembly"], capacity=capacity)
        self.node(f"{gid}_pickup", (x, y + 2 * dy), list(pickup_flags), capacity=pickup_capacity or capacity)
        self.edge(f"g_{gid}_start", f"{gid}_start", f"{gid}_assembly", 20, **ROAD)
        self.edge(f"g_{gid}_pickup", f"{gid}_assembly", f"{gid}_pickup", 20, **ROAD)

    def cable(self, id, a, b, ticks=50, **kw):
        """An aerial cable edge. Its admission samples only its two end stations, never the ground it spans."""
        pos = {n["id"]: n["at"] for n in self.nodes}
        self.edge(id, a, b, ticks, sample=[list(pos[a]), list(pos[b])], **CABLE, **kw)

    def refuges(self, depot_at, landing_at, wide_caps, compact_caps, compact_edge_ticks=None):
        """Permanent receiving: depot (wide primary) and high landing (compact primary)."""
        self.node("depot", depot_at, ["evacuation"], capacity=wide_caps["depot"])
        self.node("high_landing", landing_at, ["evacuation"], capacity=wide_caps["high_landing"])
        ov = {"nodes": [{"id": k, "capacity": v} for k, v in compact_caps.items()]}
        if compact_edge_ticks:
            ov["edges"] = [{"id": k, "ticks": v} for k, v in compact_edge_ticks.items()]
        self.class_overrides = {"compact": ov}

    def support(self, id, nodes=(), edges=(), collapse_nodes=(), collapse_edges=(), base=400, static=0):
        self.supports.append({"id": id, "base_stability": base, "static_load": static, "nodes": list(nodes),
                              "edges": list(edges), "collapse_nodes": list(collapse_nodes),
                              "collapse_edges": list(collapse_edges)})

    def runout(self, id, segments, source_support=None):
        r = {"id": id, "segments": segments}
        if source_support:
            r["source_support"] = source_support
        self.runouts.append(r)

    def beacons(self, nodes):
        for i, (node, at) in enumerate(nodes, 1):
            self.socket(f"beacon:{i:02d}", "beacon", node=node, at=list(at), zone="beacons")

    def to_json(self):
        if self.supports:
            self.extra["supports"] = self.supports
        if self.runouts:
            self.extra["runouts"] = self.runouts
        return super().to_json()


def seg(id, edges=(), nodes=(), cells=None, next=(), delay=10, settle=200):
    s = {"id": id, "edges": list(edges), "nodes": list(nodes), "delay": delay, "settle": settle}
    if cells is not None:
        s["cells"] = cells
    if next:
        s["next"] = list(next)
    return s
