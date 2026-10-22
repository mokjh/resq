"""World 5 (Cinder Reach) authoring helpers on top of mapkit.

Terrain: basalt high ground fills the board. Lava channels are painted with 'l' (elevation 0, `solid`, so water never
enters them). The sea is `S` (elevation 0, 800 mm standing water, `solid`, so it holds its level without sources). Roads
and terraces are visual; navigation is the authored graph. Lava is an authored DAG (`Map.extra["lava"]`), never a fluid.
"""
from __future__ import annotations

from mapkit import Map, LEGEND_W1

LEGEND_W5 = {
    "#": {"terrain": "basalt", "code": 3, "elev": 1200},
    "=": {"terrain": "road", "code": 2, "elev": 300},
    ".": {"terrain": "terrace", "code": 1, "elev": 100},
    "o": {"terrain": "orchard", "code": 10, "elev": 100},
    "v": {"terrain": "village", "code": 11, "elev": 300},
    "D": {"terrain": "depot", "code": 8, "elev": 1200},
    "H": {"terrain": "high_landing", "code": 9, "elev": 1200},
    "l": {"terrain": "lava_channel", "code": 13, "elev": 0, "solid": True},
    "c": {"terrain": "cooling_works", "code": 14, "elev": 1200},
    "S": {"terrain": "sea", "code": 4, "elev": 0, "init_depth": 800, "cond": 2_000_000, "solid": True},
    "q": {"terrain": "quay", "code": 2, "elev": 1200},
    # drainage (World 1 water rules): an empty basin, its low platform, and a sea outfall
    "d": {"terrain": "basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "P": {"terrain": "low_platform", "code": 2, "elev": 200},
    "w": {"terrain": "outfall", "code": 12, "elev": 0, "cond": 2_000_000},
}

ROAD = dict(slots=6)          # wide basalt roads: a group walks together
NARROW = dict(slots=2)        # crust paths and stairs


class W5Map(Map):
    def __init__(self, mission_id: str):
        super().__init__(mission_id, fill="#", legend=dict(LEGEND_W5))
        self.segments = []
        self.lava_sources = []
        self.coolant = []

    def group_nodes(self, gid, at, capacity, south=True):
        """start on the anchor, assembly one cell south, pickup two cells south (north at the board edge)."""
        x, y = at
        dy = -1 if south else 1
        self.node(f"{gid}_start", (x, y), ["assembly"], capacity=capacity)
        self.node(f"{gid}_assembly", (x, y + dy), ["assembly"], capacity=capacity)
        self.node(f"{gid}_pickup", (x, y + 2 * dy), ["assembly"], capacity=capacity)
        self.edge(f"g_{gid}_start", f"{gid}_start", f"{gid}_assembly", 20, **ROAD)
        self.edge(f"g_{gid}_pickup", f"{gid}_assembly", f"{gid}_pickup", 20, **ROAD)

    def segment(self, id, cells, edges=(), nodes=(), next=(), paint=True):
        """cells: rect dict or list of [x,y]."""
        seg = {"id": id, "cells": cells}
        if edges:
            seg["edges"] = list(edges)
        if nodes:
            seg["nodes"] = list(nodes)
        if next:
            seg["next"] = [n if isinstance(n, str) else {"to": n[0], "delay": n[1]} for n in next]
        self.segments.append(seg)
        if paint:
            if isinstance(cells, dict):
                self.rect("l", cells["x0"], cells["y0"], cells["x1"], cells["y1"])
            else:
                for x, y in cells:
                    self.cell("l", x, y)

    def to_json(self):
        if self.segments:
            self.extra["lava"] = {"segments": self.segments, "sources": self.lava_sources}
        if self.coolant:
            self.extra["coolant_stores"] = self.coolant
        return super().to_json()


def rect(x0, y0, x1, y1):
    return {"x0": x0, "y0": y0, "x1": x1, "y1": y1}
