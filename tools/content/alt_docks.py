#!/usr/bin/env python3
"""Author the `alternate_pickup` landing for each Alternate Dock challenge (W1, spec 04 §2.8 and §6).

For the challenge's group, a third legal landing is added beside the group's own pickup dock: same capacity and
dock depths, a ground leg 20 operation ticks longer from the group's assembly, and every water approach 100 ticks
longer than the normal pickup's. The spec's "one cell east of the high-landing anchor" can't be reached by the
group on the authored W1 maps; this keeps its intent (a slower, separate landing that changes only the pickup
binding) and is recorded in DECISIONS.md. The map gains an `alternate_pickup` role; the base mission never uses it.

It also writes challenge copies of the reference plans (strategies/<challenge>_A.json, _B.json) whose explicit
pickup for that group points at the new landing. Re-running is idempotent.
    python3 tools/content/alt_docks.py
"""
import json, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODES = json.loads((ROOT / "specification/content/modes_and_challenges.json").read_text())
OFFSETS = [(0, -1), (0, 1), (1, 0), (-1, 0)]


def cell(rows, x, y):
    return rows[len(rows) - 1 - y][x]   # rows run north to south; y counts from the south edge


def main():
    for ch in MODES["challenges"]:
        if ch["modifier"] != "alternate_dock":
            continue
        mid, cid = ch["mission_id"], ch["id"]
        w, m = mid.lower().split("-")
        mdir = ROOT / "content" / w / m
        path = mdir / "map.json"
        mp = json.loads(path.read_text())
        group = ch["parameters"]["group_id"]
        g = next(x for x in mp["groups"] if x["id"] == group)
        nodes = {n["id"]: n for n in mp["nodes"]}
        pickup = nodes[g["pickup"]]
        alt_id = g["pickup"] + "_alt"
        rows = mp["rows"]
        if alt_id not in nodes:
            px, py = pickup["at"]; wx, wy = pickup["water_at"]
            spot = None
            for dx, dy in OFFSETS:
                land, water = cell(rows, px + dx, py + dy), cell(rows, wx + dx, wy + dy)
                taken = any(n["at"] == [px + dx, py + dy] for n in mp["nodes"])
                if land == cell(rows, px, py) and water == cell(rows, wx, wy) and not taken:
                    spot = (dx, dy); break
            alt = dict(pickup); alt["id"] = alt_id
            if spot is not None:
                dx, dy = spot
                alt["at"] = [px + dx, py + dy]; alt["water_at"] = [wx + dx, wy + dy]
            else:
                # no free land cell beside the dock: share the dock's coordinate (spec 04 §2.2 allows it; capacity is
                # separate) and berth at a neighbouring water cell of the same channel
                for dx, dy in OFFSETS:
                    if cell(rows, wx + dx, wy + dy) == cell(rows, wx, wy) and [wx + dx, wy + dy] != [px, py]:
                        alt["water_at"] = [wx + dx, wy + dy]; break
                else:
                    raise SystemExit(f"{cid}: no water cell beside {g['pickup']} for a second berth")
            mp["nodes"].append(alt)
            new_edges = []
            for e in mp["edges"]:
                ends = (e["from"], e["to"])
                if g["pickup"] not in ends:
                    continue
                c = dict(e); c["id"] = e["id"] + "_alt"
                if c["from"] == g["pickup"]: c["from"] = alt_id
                if c["to"] == g["pickup"]: c["to"] = alt_id
                if e["class"] == "water":
                    c["ticks"] = e["ticks"] + 100
                    if "sample" in c:
                        c["sample"] = [alt["water_at"]] + c["sample"] if e["from"] == g["pickup"] else c["sample"] + [alt["water_at"]]
                elif e["class"] == "ground":
                    c["ticks"] = e["ticks"] + 20
                new_edges.append(c)
            # the two berths sit side by side on one channel: a boat at one can move to the other
            water = next((e for e in mp["edges"] if e["class"] == "water" and g["pickup"] in (e["from"], e["to"])), None)
            if water is not None and alt["water_at"] != pickup["water_at"]:
                link = {k: v for k, v in water.items() if k not in ("id", "from", "to", "ticks", "sample", "via")}
                link.update({"id": f"w_{g['pickup']}_alt_berth", "from": g["pickup"], "to": alt_id, "ticks": 20,
                             "sample": [pickup["water_at"], alt["water_at"]]})
                new_edges.append(link)
            mp["edges"].extend(new_edges)
            mp.setdefault("roles", {})["alternate_pickup"] = {"nodes": [alt_id]}
            path.write_text(json.dumps(mp, indent=1) + "\n")
            print(f"{cid}: {alt_id} at {alt['at']} (+{len(new_edges)} edges)")
        for name in "AB":
            base = json.loads((mdir / "strategies" / f"{name}.json").read_text())
            steps = [dict(s, pickup=alt_id) if s.get("group") == group and s.get("pickup") == g["pickup"] else s for s in base["steps"]]
            out = {"id": f"{cid}_{name}",
                   "description": f"Alternate Dock version of plan {name}: the {group.replace('_', ' ')} group boards at the alternate landing ({alt_id}); otherwise unchanged. " + base.get("description", ""),
                   "steps": steps}
            (mdir / "strategies" / f"{cid}_{name}.json").write_text(json.dumps(out, indent=1) + "\n")


if __name__ == "__main__":
    main()
