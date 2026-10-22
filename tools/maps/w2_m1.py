"""W2-M1 A Line in the Trees — authored layout (recipe W2-M1-layout-v1; anchors kept).

Story: the campsite (west) and the lookout (east) are joined by two wooded paths. The short trail runs straight
between them under a hedge of trees (north side); the long ridge path climbs north from the campsite, runs along a
second hedge under the ridge and drops to the lookout. Between the two paths lies the central forest; its dry stand,
at the west end beside the campsite, ignites at 50 s. Every path is a fuel-0 road, so fire can only close it by
burning the hedge beside it, and a lit hedge crawls slowly along the path, keeping it closed for the rest of the
mission. Each hedge touches the forest in one place:
- the trail hedge through the four-tree strip nearest the trail (x 9..12, two firebreak sockets) and the woodshed
  (a building beside the dry stand, which no firebreak can clear; only a sprinkler can keep it from igniting);
- the ridge hedge through a two-tree neck at the forest's east end (one firebreak socket).
Evacuation: the campsite walks to the depot (by the trail and the depot road, or the long south road), the lookout
walks down its own road to the high landing.
"""
from mapkit import Map
from w2_kit import new_map, hline, vline, TANK_VOLUME

m = new_map("W2-M1")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 22, 31, 23)                 # ridge crest
m.rect("c", 3, 10, 7, 14)                  # campsite clearing
m.rect("k", 23, 9, 27, 15)                 # lookout knoll
m.rect("T", 8, 14, 20, 18)                 # central forest
m.rect("Y", 8, 14, 9, 15)                  # dry stand (dry_cluster) at its west end, beside the campsite
m.cell("w", 8, 13)                         # woodshed between the dry stand and the trail hedge
m.line("T", (9, 13), (12, 13))             # the fuel strip nearest the trail (x 9..12): with the woodshed,
                                           # the only link from the forest to the trail hedge
m.line("h", (8, 12), (23, 12))             # trail hedge (short path), x 8..23
m.line("T", (19, 19), (20, 19))            # neck joining the forest's east end to the ridge hedge
m.line("h", (8, 20), (23, 20))             # ridge hedge (long path), x 8..23
# roads
m.line("=", (6, 11), (25, 11))             # the short trail
m.line("=", (6, 14), (6, 21), (25, 21), (25, 14))  # the long ridge path
m.line("=", (15, 10), (15, 4))             # depot road
m.line("=", (25, 10), (25, 5))             # landing road
m.line("=", (6, 10), (6, 4), (14, 4))      # campsite south road
m.rect("D", 13, 2, 17, 3)
m.rect("H", 24, 3, 27, 4)
m.cell("D", 15, 3)
m.cell("H", 25, 4)

# ---------------------------------------------------------------- regions / fire
m.regions.update({
    "dry_cluster": "Y",
    "trail_hedge": hline(12, 8, 23),
    "ridge_hedge": hline(20, 8, 23),
    "firebreak_approach": hline(13, 9, 12) + hline(19, 19, 20),   # the two cut lines: the strips that cool first
    "lookout": {"x0": 23, "y0": 9, "x1": 27, "y1": 15},
    "campsite": {"x0": 3, "y0": 10, "x1": 7, "y1": 14},
})
m.extra["stores"] = [{"id": "tank", "elev": 1200, "volume": TANK_VOLUME}]

# ---------------------------------------------------------------- navigation
m.node("campsite_start", (6, 13), ["assembly"], capacity=6)
m.node("campsite_assembly", (6, 12), ["assembly"], capacity=6)
m.node("campsite_pickup", (6, 11), ["assembly"], capacity=6)
m.node("lookout_start", (25, 13), ["assembly"], capacity=4)
m.node("lookout_assembly", (25, 12), ["assembly"], capacity=4)
m.node("lookout_pickup", (25, 11), ["assembly"], capacity=6)
m.node("trail_mid", (15, 11), ["junction"])
m.node("ridge_west", (6, 21), ["junction"])
m.node("ridge_east", (25, 21), ["junction"])
m.node("camp_south", (6, 4), ["junction"])
m.node("depot", (15, 3), ["evacuation"], capacity=6)
m.node("high_landing", (25, 4), ["evacuation"], capacity=4)

m.edge("g_camp_start", "campsite_start", "campsite_assembly", 20)
m.edge("g_camp_pickup", "campsite_assembly", "campsite_pickup", 20)
m.edge("g_look_start", "lookout_start", "lookout_assembly", 20)
m.edge("g_look_pickup", "lookout_assembly", "lookout_pickup", 20)
# short wooded path (300): the trail under the hedge
m.edge("g_trail_w", "campsite_pickup", "trail_mid", 150, sample=hline(11, 6, 15))
m.edge("g_trail_e", "trail_mid", "lookout_pickup", 150, sample=hline(11, 15, 25))
# long wooded path (600): the ridge path under the ridge hedge
m.edge("g_ridge_up", "campsite_start", "ridge_west", 150, sample=vline(6, 14, 21))
m.edge("g_ridge", "ridge_west", "ridge_east", 300, sample=hline(21, 6, 25))
m.edge("g_ridge_down", "ridge_east", "lookout_start", 150, sample=vline(25, 14, 21))
# evacuation roads
m.edge("g_depot_road", "trail_mid", "depot", 200, sample=vline(15, 3, 10))
m.edge("g_landing_road", "lookout_pickup", "high_landing", 200, sample=vline(25, 4, 10))
m.edge("g_camp_south", "campsite_pickup", "camp_south", 200, sample=vline(6, 4, 10))
m.edge("g_south_road", "camp_south", "depot", 250, sample=hline(4, 6, 14) + [[15, 3]])

# ---------------------------------------------------------------- sockets
m.socket("firebreak:01", "firebreak", at=[9, 13], cells=hline(13, 9, 10), zone="firebreaks")
m.socket("firebreak:02", "firebreak", at=[12, 13], cells=hline(13, 11, 12), zone="firebreaks")
m.socket("firebreak:03", "firebreak", at=[20, 19], cells=hline(19, 19, 20), zone="firebreaks")
m.socket("sprinkler:01", "sprinkler", at=[4, 14], cells=[[8, 13]], store="tank", power="grid", zone="sprinklers")
m.socket("sprinkler:02", "sprinkler", at=[22, 18], cells=[[19, 19], [20, 19]], store="tank", power="grid", zone="sprinklers")
m.socket("generator:01", "generator", at=[4, 11], platform=[[4, 11]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[21, 13], platform=[[21, 13]], power="grid", zone="generators")
m.socket("beacon:01", "beacon", node="depot", at=[14, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[26, 4])
m.socket("beacon:03", "beacon", node="trail_mid", at=[16, 10])
m.socket("beacon:04", "beacon", node="ridge_west", at=[5, 21])

m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "campsite", "start": "campsite_start", "assembly": "campsite_assembly", "pickup": "campsite_pickup"},
    {"id": "lookout", "start": "lookout_start", "assembly": "lookout_assembly", "pickup": "lookout_pickup"},
]
m.routes = [{"id": "lookout_path", "from": "campsite_assembly", "to": "lookout_assembly", "class": "ground"}]
m.roles.update({
    "dry_cluster": {"regions": ["dry_cluster"]},
    "firebreak_approach": {"regions": ["firebreak_approach"]},
    "tank": {"stores": ["tank"]},
    "short_forest_route": {"edges": ["g_trail_w", "g_trail_e"]},
    "long_forest_route": {"edges": ["g_ridge_up", "g_ridge", "g_ridge_down"]},
    "lookout_path": {"edges": ["g_trail_w", "g_trail_e", "g_ridge_up", "g_ridge", "g_ridge_down"]},
    "safe_receiving": {"nodes": ["depot", "high_landing"]},
})

# Compact layout: the high landing becomes the primary receiving point (recipe receiving.compact_primary).
# Capacities stay with the groups that use them (the same reference replays run on both layouts and receiving
# capacity equals the population exactly), so the swap is in staging travel: the landing road shortens and the
# depot is reached by the longer outer approach.
m.class_overrides = {
    "compact": {
        "edges": [
            {"id": "g_depot_road", "ticks": 300},
            {"id": "g_landing_road", "ticks": 150},
        ],
    },
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(2, 1))
