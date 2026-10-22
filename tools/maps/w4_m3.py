"""W4-M3 The Floating Market — authored layout (recipe W4-M3-layout-v1; anchors kept approximately).

Story: four market islands (five people each) around a safe harbor island with two receiving docks, each one
berth: the receiving dock (depot anchor) and the high landing. Water routes:
  * the inner harbor junction links the receiving dock, the high landing, the west market and the south market;
  * the pontoon channel is the short marked channel to the north market: a stall pontoon blocks it at 170 s;
  * the tidal lane is the short lane to the east market: closed until it opens at 50 s;
  * the outer loop rings west -> north -> east -> high landing; from 300 s the current makes it faster.
The south market sits on a rock beside the harbor island. A breach separates them (bridge:01); bridged, the
south market can walk home, and its high dock becomes a transfer point (capacity 6) from which boat-delivered
residents walk over the bridge. Its dock is not a refuge: anyone unloaded there must still be walked home.
"""
from mapkit import Map
from w4_kit import new_map, add_tide, compact_swap

m = new_map("W4-M3")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 0, 31, 4)                         # safe harbor island
m.rect("D", 13, 2, 17, 4)                        # receiving dock quay and harbor crane
m.rect("L", 23, 2, 27, 4)                        # high landing
m.rect("v", 2, 13, 7, 18)                        # west market
m.rect("v", 12, 18, 18, 22)                      # north market
m.rect("v", 24, 13, 29, 18)                      # east market
m.rect("v", 6, 6, 10, 9)                         # south market rock
m.cell("~", 8, 5)                                # breach between the south rock and the harbor island (bridge:01)
m.line("H", (6, 10), (6, 12))                    # old footway from the west market to the south rock ...
m.cell("~", 6, 11)                               # ... broken in the middle (bridge:02)

# ---------------------------------------------------------------- water
add_tide(m)                                      # no tide events in this mission: the sea holds 150 mm
m.regions = {"harbor_quay": "D", "south_market": {"x0": 6, "y0": 6, "x1": 10, "y1": 9}}

# ---------------------------------------------------------------- navigation
def market(gid, start, assembly, pickup, water, cap=6):
    m.node(f"{gid}_start", start, ["assembly"], capacity=5)
    m.node(f"{gid}_assembly", assembly, ["assembly"], capacity=5)
    m.node(f"{gid}_pickup", pickup, ["dock"], capacity=cap, water_at=water, dock_min_depth=100, dock_max_depth=2000)
    m.edge(f"g_{gid}_start", f"{gid}_start", f"{gid}_assembly", 20)
    m.edge(f"g_{gid}_pickup", f"{gid}_assembly", f"{gid}_pickup", 20)

market("west_market", (5, 15), (5, 14), (7, 14), [8, 14])
market("north_market", (15, 20), (15, 19), (15, 18), [15, 17])
market("east_market", (26, 15), (26, 14), (24, 14), [23, 14])
market("south_market", (8, 8), (8, 7), (10, 7), [11, 7])          # south high dock: transfer point
m.node("south_bridge_north", (8, 6), ["junction"])
m.node("south_bridge_south", (8, 4), ["junction"])
m.node("footway_north", (6, 12), ["junction"])
m.node("footway_south", (6, 10), ["junction"])
m.node("receiving_dock", (15, 3), ["evacuation", "dock"], capacity=12, water_at=[15, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=8, water_at=[25, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("harbor", (15, 10), ["junction"])

m.edge("g_south_bridge_n", "south_market_assembly", "south_bridge_north", 20)
m.edge("g_south_bridge_dock", "south_market_pickup", "south_bridge_north", 40, sample=[[9, 7], [9, 6], [8, 6]])
m.edge("g_south_breach", "south_bridge_north", "south_bridge_south", 40, sample=[[8, 6], [8, 4]], tags=["bridge"])
m.edge("g_south_road", "south_bridge_south", "receiving_dock", 140, sample=[[x, 4] for x in range(8, 16)])
m.edge("g_footway_n", "west_market_assembly", "footway_north", 60, sample=[[5, 13], [6, 13], [6, 12]])
m.edge("g_footway_breach", "footway_north", "footway_south", 40, sample=[[6, 12], [6, 10]], tags=["bridge"])
m.edge("g_footway_s", "footway_south", "south_market_assembly", 60, sample=[[6, 10], [6, 9], [7, 9], [8, 9], [8, 8], [8, 7]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2000)
m.edge("w_dock_harbor", "receiving_dock", "harbor", 60, sample=[[15, y] for y in range(5, 11)], **water)
m.edge("w_landing_harbor", "high_landing", "harbor", 140, sample=[[x, 10] for x in range(15, 26)] + [[25, y] for y in range(5, 10)], **water)
m.edge("w_south_harbor", "south_market_pickup", "harbor", 80, sample=[[x, 7] for x in range(11, 16)] + [[15, 8], [15, 9]], **water)
m.edge("w_west_harbor", "west_market_pickup", "harbor", 160, sample=[[x, 14] for x in range(8, 16)] + [[15, y] for y in range(10, 14)], **water)
m.edge("w_pontoon_channel", "harbor", "north_market_pickup", 120, sample=[[15, y] for y in range(10, 18)], **water)
m.edge("w_tidal_lane", "harbor", "east_market_pickup", 140, enabled=False,
       sample=[[x, 12] for x in range(15, 24)] + [[23, 13], [23, 14]], **water)
m.edge("w_outer_west", "west_market_pickup", "north_market_pickup", 240,
       sample=[[8, y] for y in range(14, 22)] + [[x, 22] for x in range(8, 12)] + [[11, 17], [14, 17], [15, 17]], tags=["outer_loop"], **water)
m.edge("w_outer_east", "north_market_pickup", "east_market_pickup", 240,
       sample=[[x, 17] for x in range(15, 24)] + [[23, y] for y in range(14, 18)], tags=["outer_loop"], **water)
m.edge("w_outer_south", "east_market_pickup", "high_landing", 200,
       sample=[[30, y] for y in range(5, 15)] + [[x, 5] for x in range(25, 31)], tags=["outer_loop"], **water)

# ---------------------------------------------------------------- sockets
m.socket("bridge:01", "bridge", edge="g_south_breach", at=[8, 5], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_footway_breach", at=[6, 11], zone="bridges")
# Sea walls on the harbor quay. This contract has no tide or source events, so the sea holds its level and these
# sockets have nothing to hold back (they exist for the recipe's socket count; see DESIGN.md).
m.socket("barrier:01", "barrier", at=[15, 5], links=[[[15, 5], [15, 4]]], zone="barriers")
m.socket("barrier:02", "barrier", at=[25, 5], links=[[[25, 5], [25, 4]]], zone="barriers")
m.socket("barrier:03", "barrier", at=[11, 7], links=[[[11, 7], [10, 7]]], zone="barriers")
m.socket("skiff:01", "skiff", node="receiving_dock", at=[15, 5])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 5])
m.socket("skiff:03", "skiff", node="south_market_pickup", at=[11, 7])
m.socket("skiff:04", "skiff", node="west_market_pickup", at=[8, 14])
m.socket("beacon:01", "beacon", node="receiving_dock", at=[14, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[24, 3])
m.socket("beacon:03", "beacon", node="south_market_pickup", at=[10, 8])

m.power = {"nodes": [], "links": []}
m.infrastructure = [
    {"id": "receiving_harbor", "demand": 0, "region": "harbor_quay", "nodes": ["receiving_dock", "high_landing"]},
]
m.groups = [{"id": g, "start": f"{g}_start", "assembly": f"{g}_assembly", "pickup": f"{g}_pickup"}
            for g in ("north_market", "east_market", "south_market", "west_market")]
m.routes = [{"id": "south_market_ground", "from": "south_market_assembly", "to": "receiving_dock", "class": "ground"}]
m.roles.update({
    "tidal_lane": {"edges": ["w_tidal_lane"]},
    "pontoon_channel": {"edges": ["w_pontoon_channel"]},
    "outer_loop": {"edges": ["w_outer_west", "w_outer_east", "w_outer_south"]},
    "receiving_harbor": {"infrastructure": ["receiving_harbor"]},
})
compact_swap(m, {"receiving_dock": 12, "high_landing": 8}, {"receiving_dock": 8, "high_landing": 12}, {"w_landing_harbor": 200})

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(4, 3))
