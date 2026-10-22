"""Shared authoring kit for World 4 (Tidemark): sea legend, tide boundary and receiving-dock helpers.

Tide model (DECISIONS 23 Oct): sea head = datum + level; `set_tide_segment` ramps over 100 scenario ticks to
0 / 600 / 1000 / 800 mm. Tidemark uses DATUM = 150 mm, so the open sea stands at

    segment 0 (low water)  150 mm      segment 1 (rising)     750 mm
    segment 2 (peak)      1150 mm      segment 3 (falling)    950 mm

High ground (1200 mm) is never overtopped. Every other height in the legend is chosen against these four heads,
so each tide segment opens or closes something specific (see each mission's DESIGN.md).
"""
from __future__ import annotations

from mapkit import Map, W, H

DATUM = 150
SEA_HEAD = {0: DATUM, 1: DATUM + 600, 2: DATUM + 1000, 3: DATUM + 800}

SEA_COND = 2_000_000

LEGEND_W4 = {
    # water
    "~": {"terrain": "sea", "code": 0, "elev": 0, "init_depth": DATUM, "cond": SEA_COND},
    "o": {"terrain": "scour_hole", "code": 0, "elev": 0, "cond": SEA_COND},              # deep pocket behind flats, starts dry
    "f": {"terrain": "tidal_flats", "code": 6, "elev": 600, "cond": SEA_COND},           # awash from segment 1 (750 mm)
    # crossings
    "c": {"terrain": "low_causeway", "code": 2, "elev": 800, "cond": SEA_COND},          # dry at 750, 350 mm under at the peak
    "H": {"terrain": "high_causeway", "code": 2, "elev": 1200},                          # all-tide raised walkway
    "B": {"terrain": "breakwater", "code": 12, "elev": 1050, "cond": SEA_COND},          # 100 mm under at the peak only
    # land
    "#": {"terrain": "high_ground", "code": 3, "elev": 1200},
    "m": {"terrain": "market_terrace", "code": 11, "elev": 900, "cond": SEA_COND},       # floods at the peak (250 mm)
    "D": {"terrain": "depot", "code": 8, "elev": 1200},
    "L": {"terrain": "high_landing", "code": 9, "elev": 1200},
    "v": {"terrain": "plaster_village", "code": 11, "elev": 1200},
    "=": {"terrain": "harbor_road", "code": 2, "elev": 1200},
}


def new_map(mission_id: str, legend_extra: dict | None = None) -> Map:
    legend = dict(LEGEND_W4)
    if legend_extra:
        legend.update(legend_extra)
    return Map(mission_id, fill="~", legend=legend)


def sea_cells(m: Map, chars: str = "~") -> list[list[int]]:
    """Every cell painted as open sea exchanges with the tide controller.

    Hydraulic diffusion between cells is slow (an explicit scheme needs conductance <= 2.5M mm^2/s, so a
    15-cell reach takes ~900 ticks), while the tide must reach the docks within each 100-tick ramp. The board
    is therefore a window on the open sea: every open-water cell is a sea boundary cell, and only enclosed
    water (flats, scour holes, inner basins) lags behind the tide by real cell-to-cell flow.
    """
    return [[x, y] for y in range(H) for x in range(W) if m.at(x, y) in chars]


def add_tide(m: Map, tide_id: str = "tide", cells=None):
    m.extra["tides"] = [{
        "id": tide_id,
        "datum": DATUM,
        "cells": cells if cells is not None else sea_cells(m),
        "conductance": SEA_COND,
        "initial_level": 0,
    }]
    m.roles.setdefault("tide", {"tides": [tide_id]})


def compact_swap(m: Map, wide_caps: dict, compact_caps: dict, edge_ticks: dict | None = None):
    """Recipe receiving swap (spec 04 §2.7): compact reverses primary/secondary receiving capacity and lengthens
    the outer approach to the (new) primary dock. Same graph, different staging and travel."""
    for node in m.nodes:
        if node["id"] in wide_caps:
            node["capacity"] = wide_caps[node["id"]]
    ov = {"nodes": [{"id": k, "capacity": v} for k, v in compact_caps.items()]}
    if edge_ticks:
        ov["edges"] = [{"id": k, "ticks": v} for k, v in edge_ticks.items()]
    m.class_overrides = {"compact": ov}
