#!/usr/bin/env python3
"""Website data: every mission's valley, read straight from its map file (content/wN/mN/map.json), so the site can
draw all 25 boards as small isometric models. Per cell: legend elevation (mm), a drawing category, and starting
water depth (mm). Nothing is invented: categories only group the map's own terrain names for colouring.
    python3 tools/web/export_valleys.py   ->  web/app/data/valleys.json
"""
import json, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "web/app/data/valleys.json"
BRIEF = json.loads((ROOT / "content/briefings.json").read_text())

# drawing categories (index = value in "kind")
KINDS = ["ground", "water", "road", "building", "landing", "rock", "lava", "forest"]
RULES = [  # first match wins, on the legend's terrain name
    ("lava", ("lava",)),
    ("landing", ("landing", "camp", "dry_stand", "shelter")),
    ("building", ("village", "cabin", "depot", "station", "house", "clinic", "observatory", "workshop", "woodshed",
                  "works", "refill", "medical", "market_terrace", "quay", "pad")),
    ("road", ("road", "causeway", "footpath", "wharf", "underpass", "junction", "cable_line", "viaduct")),
    ("forest", ("forest", "orchard", "hedge", "trees", "meadow")),
    ("rock", ("rock", "basalt", "avalanche", "ravine", "shoulder", "breakwater", "knoll", "rim", "lip", "sill")),
    ("water", ("river", "channel", "reservoir", "sea", "pond", "lake", "lagoon", "basin", "spillway", "outfall",
               "gully", "tidal", "scour", "narrows", "weir", "culvert", "cutting", "sluice", "flats")),
]


def kind_of(name, entry):
    if entry.get("init_depth", 0) > 0:
        return KINDS.index("water")
    for kind, keys in RULES:
        if any(k in name for k in keys):
            return KINDS.index(kind)
    return KINDS.index("ground")


def main():
    out = {"kinds": KINDS, "missions": {}}
    for w in range(1, 6):
        for m in range(1, 6):
            mid = f"W{w}-M{m}"
            mp = json.loads((ROOT / f"content/w{w}/m{m}/map.json").read_text())
            legend, rows = mp["legend"], mp["rows"]
            elev, kind, init = [], [], []
            for row in rows:
                for ch in row:
                    e = legend[ch]
                    elev.append(int(e["elev"])); kind.append(kind_of(e["terrain"], e)); init.append(int(e.get("init_depth", 0)))
            out["missions"][mid] = {"world": f"W{w}", "title": BRIEF["missions"][mid]["title"],
                                    "decision": BRIEF["missions"][mid]["decision"], "w": len(rows[0]), "h": len(rows),
                                    "elev": elev, "kind": kind, "init": init}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, separators=(",", ":")))
    print(f"wrote {OUT.relative_to(ROOT)} ({OUT.stat().st_size // 1024} KB, {len(out['missions'])} missions)")


if __name__ == "__main__":
    main()
