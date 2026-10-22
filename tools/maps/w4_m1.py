"""W4-M1 Between Two Tides — authored layout (recipe W4-M1-layout-v1; anchors kept approximately).

Story: two islands off the harbor mainland. The low market (west island) stands on a 900 mm terrace that the
peak tide floods. It reaches the depot by a low causeway (800 mm: dry until the peak) or by the long west high
walk, whose breach needs a bridge (bridge:01). Its jetty is a low-water quay (usable to 900 mm of water).
The outer island is ringed by tidal flats (600 mm): boats cross them only once the tide has risen. It has a
high pier on the flats (all tide once awash), a low-water jetty in a scour hole (deck awash above 500 mm, so
it can never be worked when the flats let a boat in), and an all-tide high causeway to the high landing whose
breach needs a bridge (bridge:02). Receiving: the depot quay is low (closes above 900 mm, from the peak on) and
is lit by the fixed dry source; the high landing works at every tide but its dock light needs a relay link
from the same dry source (relay:01 on the ridge, or relay:02 on the breakwater, which the peak covers).
"""
from mapkit import Map
from w4_kit import new_map, add_tide, compact_swap

m = new_map("W4-M1")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 0, 31, 4)                         # harbor mainland
m.rect("D", 13, 2, 16, 4)                        # depot
m.rect("L", 23, 2, 27, 4)                        # high landing
m.line("=", (1, 4), (12, 4))                     # harbor road west of the depot
m.line("=", (17, 4), (22, 4))
# west island: high ground with the low market terrace on its south-east side
m.rect("#", 1, 13, 7, 19)
m.rect("m", 3, 13, 7, 15)
m.line("c", (5, 5), (5, 12))                     # low causeway to the mainland
m.line("H", (1, 5), (1, 12))                     # west high walk ...
m.cell("~", 1, 9)                                # ... with its breach (bridge:01)
# outer island ringed by tidal flats, high causeway to the high landing
m.rect("f", 21, 11, 30, 20)
m.rect("#", 23, 13, 29, 19)
m.cell("o", 27, 12)                              # scour hole under the low-water jetty
m.line("H", (24, 5), (24, 12))                   # high causeway ...
m.cell("~", 24, 8)                               # ... with its breach (bridge:02)
m.cell("B", 20, 5)                               # harbor breakwater (relay:02)

# ---------------------------------------------------------------- water
add_tide(m)
m.regions = {"low_market": "m", "tidal_flats": "f", "harbor": "~"}

# ---------------------------------------------------------------- navigation
m.node("low_market_start", (5, 15), ["assembly"], capacity=8)
m.node("low_market_assembly", (5, 14), ["assembly"], capacity=8)
m.node("low_market_pickup", (7, 14), ["dock"], capacity=8, water_at=[8, 14], dock_min_depth=100, dock_max_depth=900)
m.node("causeway_north", (5, 13), ["junction"])
m.node("causeway_south", (5, 4), ["junction"])
m.node("west_walk_north", (1, 13), ["junction"])
m.node("west_breach_north", (1, 10), ["junction"])
m.node("west_breach_south", (1, 8), ["junction"])
m.node("west_walk_south", (1, 4), ["junction"])
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=8, water_at=[15, 5], dock_min_depth=100, dock_max_depth=900)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=4, water_at=[25, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("outer_island_start", (26, 15), ["assembly"], capacity=4)
m.node("outer_island_assembly", (26, 14), ["assembly"], capacity=4)
m.node("outer_island_pickup", (23, 14), ["dock"], capacity=6, water_at=[22, 14], dock_min_depth=100, dock_max_depth=2000)
m.node("outer_jetty", (27, 13), ["dock"], capacity=6, water_at=[27, 12], dock_min_depth=100, dock_max_depth=500)
m.node("outer_walk_north", (24, 12), ["junction"])
m.node("outer_breach_north", (24, 9), ["junction"])
m.node("outer_breach_south", (24, 7), ["junction"])
m.node("harbor", (15, 10), ["junction"])

# low market by land
m.edge("g_lm_start", "low_market_start", "low_market_assembly", 20)
m.edge("g_lm_jetty", "low_market_assembly", "low_market_pickup", 20, sample=[[6, 14], [7, 14]])
m.edge("g_lm_causeway_n", "low_market_assembly", "causeway_north", 20)
m.edge("g_low_causeway", "causeway_north", "causeway_south", 200, sample=[[5, y] for y in range(4, 14)])
m.edge("g_causeway_depot", "causeway_south", "depot", 100, sample=[[x, 4] for x in range(5, 16)])
m.edge("g_west_walk_a", "low_market_assembly", "west_walk_north", 80, sample=[[4, 14], [3, 14], [2, 14], [1, 14], [1, 13]])
m.edge("g_west_walk_b", "west_walk_north", "west_breach_north", 100, sample=[[1, y] for y in range(10, 14)])
m.edge("g_west_breach", "west_breach_north", "west_breach_south", 40, sample=[[1, 10], [1, 8]], tags=["bridge"])
m.edge("g_west_walk_c", "west_breach_south", "west_walk_south", 120, sample=[[1, y] for y in range(4, 9)])
m.edge("g_west_road", "west_walk_south", "depot", 260, sample=[[x, 4] for x in range(1, 16)])
# outer island by land (high causeway)
m.edge("g_oi_start", "outer_island_start", "outer_island_assembly", 20)
m.edge("g_oi_pier", "outer_island_assembly", "outer_island_pickup", 30, sample=[[25, 14], [24, 14], [23, 14]])
m.edge("g_oi_jetty", "outer_island_assembly", "outer_jetty", 20, sample=[[27, 14], [27, 13]])
m.edge("g_oi_walk_a", "outer_island_assembly", "outer_walk_north", 60, sample=[[25, 14], [24, 14], [24, 13], [24, 12]])
m.edge("g_oi_walk_b", "outer_walk_north", "outer_breach_north", 120, sample=[[24, y] for y in range(9, 13)])
m.edge("g_outer_breach", "outer_breach_north", "outer_breach_south", 40, sample=[[24, 9], [24, 7]], tags=["bridge"])
m.edge("g_oi_walk_c", "outer_breach_south", "high_landing", 120, sample=[[24, y] for y in range(4, 8)] + [[25, 4]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2000)
m.edge("w_depot_harbor", "depot", "harbor", 60, sample=[[15, y] for y in range(5, 11)], **water)
m.edge("w_harbor_jetty", "harbor", "low_market_pickup", 100,
       sample=[[x, 10] for x in range(8, 16)] + [[8, y] for y in range(10, 15)], **water)
m.edge("w_harbor_pier", "harbor", "outer_island_pickup", 120,
       sample=[[15, y] for y in range(10, 15)] + [[x, 14] for x in range(15, 23)], **water)
m.edge("w_harbor_outer_jetty", "harbor", "outer_jetty", 160,
       sample=[[x, 8] for x in range(15, 28)] + [[27, y] for y in range(8, 13)], **water)
m.edge("w_harbor_landing", "harbor", "high_landing", 120,
       sample=[[x, 8] for x in range(15, 26)] + [[25, y] for y in range(5, 9)], **water)

# ---------------------------------------------------------------- sockets
m.socket("bridge:01", "bridge", edge="g_west_breach", at=[1, 9], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_outer_breach", at=[24, 8], zone="bridges")
m.socket("relay:01", "relay", at=[20, 3], platform=[[20, 3]], zone="relays")      # dry ridge above the harbor road
m.socket("relay:02", "relay", at=[20, 5], platform=[[20, 5]], zone="relays")      # breakwater: covered at the peak
m.socket("skiff:01", "skiff", node="depot", at=[15, 5])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 5])
m.socket("skiff:03", "skiff", node="low_market_pickup", at=[8, 14])
m.socket("beacon:01", "beacon", node="depot", at=[14, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[24, 3])
m.socket("beacon:03", "beacon", node="low_market_assembly", at=[4, 13])

m.power = {
    "nodes": [{"id": "dry_source", "fixed_supply": 2}, {"id": "landing_light", "fixed_supply": 0}],
    "links": [
        {"from": "dry_source", "to": "landing_light", "relay": "relay:01"},
        {"from": "dry_source", "to": "landing_light", "relay": "relay:02"},
    ],
}
m.infrastructure = [
    {"id": "receiving_station", "power": "dry_source", "demand": 1, "nodes": ["depot"], "gates_dock": True},
    {"id": "landing_station", "power": "landing_light", "demand": 1, "nodes": ["high_landing"], "gates_dock": True},
]
m.groups = [
    {"id": "low_market", "start": "low_market_start", "assembly": "low_market_assembly", "pickup": "low_market_pickup"},
    {"id": "outer_island", "start": "outer_island_start", "assembly": "outer_island_assembly", "pickup": "outer_island_pickup"},
]
m.routes = [
    {"id": "low_market_ground", "from": "low_market_assembly", "to": "depot", "class": "ground"},
    {"id": "outer_island_ground", "from": "outer_island_assembly", "to": "high_landing", "class": "ground"},
    {"id": "outer_island_water", "from": "depot", "to": "outer_island_pickup", "class": "water"},
]
m.roles.update({
    "low_market": {"groups": ["low_market"]},
    "tidal_lane": {"edges": ["w_harbor_pier"]},
    "high_causeway": {"edges": ["g_outer_breach"]},
    "receiving_station": {"infrastructure": ["receiving_station"]},
    "receiving_dock": {"infrastructure": ["landing_station"]},
})
# compact: the high landing becomes primary receiving and its outer approach is longer
compact_swap(m, {"depot": 8, "high_landing": 4}, {"depot": 4, "high_landing": 8}, {"w_harbor_landing": 180})

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(4, 1))
