"""W4-M5 Harbor of Everyone — authored layout (recipe W4-M5-layout-v1; anchors kept approximately).

Story: three islands and a lighthouse rock around the harbor. The depot is the lighthouse-backed safe harbor:
its dock works only while the lamp on the rock is powered (lighthouse demand 1 from the start, dark until a
network is built). The high landing is not lit and always works.
  * Low market (west island, 900 mm terrace, floods at the 430 s peak): a low causeway (600 mm) to the middle
    island closes when the tide first rises (60 s); its low jetty (to 900 mm of water) closes at the peak.
  * Middle island: the high causeway (1200 mm) runs from it to the mainland across a breach (bridge:01).
    Its west dock is a temporary staging point (not a refuge); its east dock is a high dock.
  * Outer island (north): boats only — the outer loop round the east side (faster from 300 s), or the tidal
    lane across the flats (from 60 s) to its south dock.
  * Lighthouse rock: its low landing (to 900 mm of water) closes at the peak.
Power: generator:01 on the harbor yard pad (950 mm, flooded at the peak unless barrier:01 and barrier:02 seal
its three sea links) or generator:02 on the dry ridge. Links: harbor->lamp by relay:01 (breakwater, dry) or
relay:03 (low cable islet, drowned from 60 s); ridge->harbor by relay:02 (dry).
"""
from mapkit import Map
from w4_kit import new_map, add_tide, compact_swap

m = new_map("W4-M5", {
    "Y": {"terrain": "harbor_yard_pad", "code": 2, "elev": 950, "cond": 2_000_000},
    "q": {"terrain": "cable_islet", "code": 7, "elev": 650, "cond": 2_000_000},
    "c": {"terrain": "low_causeway", "code": 2, "elev": 600, "cond": 2_000_000},
})

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 0, 31, 4)                         # harbor mainland
m.rect("D", 13, 2, 16, 4)                        # depot (lighthouse-backed safe harbor)
m.rect("L", 23, 2, 27, 4)                        # high landing
m.cell("Y", 12, 5)                               # harbor yard pad (generator:01)
m.rect("#", 1, 12, 7, 18)                        # west island
m.rect("m", 3, 12, 7, 15)                        # low market terrace
m.line("c", (8, 15), (11, 15))                   # low causeway to the middle island
m.rect("#", 12, 12, 18, 17)                      # middle island
m.line("H", (17, 5), (17, 11))                   # high causeway ...
m.cell("~", 17, 8)                               # ... and its breach (bridge:01)
m.rect("f", 11, 18, 19, 19)                      # tidal flats between the middle and outer islands
m.rect("#", 12, 20, 18, 23)                      # outer island
m.rect("#", 6, 6, 9, 8)                          # lighthouse rock
m.cell("#", 10, 5)                               # breakwater head (relay:01)
m.cell("q", 8, 5)                                # low cable islet (relay:03)
m.line("H", (8, 17), (10, 17))                   # old high footbridge west island -> middle island ...
                                                 # ... whose last span (11,17) is gone (bridge:02); boats pass under it

# ---------------------------------------------------------------- water
add_tide(m)
m.regions = {"low_market": "m", "lighthouse_rock": {"x0": 6, "y0": 6, "x1": 9, "y1": 8}, "harbor_yard": [[12, 5]]}

# ---------------------------------------------------------------- navigation
m.node("low_market_start", (5, 15), ["assembly"], capacity=8)
m.node("low_market_assembly", (5, 14), ["assembly"], capacity=8)
m.node("low_market_pickup", (5, 12), ["dock"], capacity=8, water_at=[5, 11], dock_min_depth=100, dock_max_depth=900)
m.node("causeway_west", (7, 15), ["junction"])
m.node("footbridge_west", (10, 17), ["junction"])
m.node("footbridge_east", (12, 17), ["junction"])
m.node("middle_island_start", (15, 15), ["assembly"], capacity=6)
m.node("middle_island_assembly", (15, 14), ["assembly"], capacity=6)
m.node("middle_island_pickup", (18, 13), ["dock"], capacity=6, water_at=[19, 13], dock_min_depth=100, dock_max_depth=2000)
m.node("middle_staging", (12, 13), ["dock", "staging"], capacity=8, water_at=[11, 13], dock_min_depth=100, dock_max_depth=2000)
m.node("causeway_east", (12, 15), ["junction"])
m.node("high_causeway_north", (17, 12), ["junction"])
m.node("breach_north", (17, 9), ["junction"])
m.node("breach_south", (17, 7), ["junction"])
m.node("high_causeway_south", (17, 4), ["junction"])
m.node("outer_island_start", (15, 22), ["assembly"], capacity=6)
m.node("outer_island_assembly", (15, 21), ["assembly"], capacity=6)
m.node("outer_island_pickup", (18, 21), ["dock"], capacity=6, water_at=[19, 21], dock_min_depth=100, dock_max_depth=2000)
m.node("outer_south_dock", (15, 20), ["dock"], capacity=6, water_at=[15, 19], dock_min_depth=100, dock_max_depth=2000)
m.node("lighthouse_start", (8, 7), ["assembly"], capacity=6)
m.node("lighthouse_assembly", (8, 8), ["assembly"], capacity=6)
m.node("lighthouse_pickup", (9, 7), ["dock"], capacity=6, water_at=[10, 7], dock_min_depth=100, dock_max_depth=900)
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=16, water_at=[15, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=10, water_at=[25, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("harbor_west", (12, 8), ["junction"])
m.node("harbor_east", (21, 8), ["junction"])

m.edge("g_lm_start", "low_market_start", "low_market_assembly", 20)
m.edge("g_lm_jetty", "low_market_assembly", "low_market_pickup", 30, sample=[[5, 13], [5, 12]])
m.edge("g_lm_causeway_w", "low_market_assembly", "causeway_west", 30, sample=[[6, 14], [7, 14], [7, 15]])
m.edge("g_low_causeway", "causeway_west", "causeway_east", 180, sample=[[x, 15] for x in range(7, 13)])
m.edge("g_causeway_middle", "causeway_east", "high_causeway_north", 100, sample=[[x, 15] for x in range(12, 18)] + [[17, 14], [17, 13], [17, 12]])
m.edge("g_lm_footbridge", "low_market_assembly", "footbridge_west", 80, sample=[[5, 15], [5, 16], [6, 16], [7, 16], [7, 17], [8, 17], [9, 17], [10, 17]])
m.edge("g_footbridge_breach", "footbridge_west", "footbridge_east", 40, sample=[[10, 17], [12, 17]], tags=["bridge"])
m.edge("g_footbridge_middle", "footbridge_east", "high_causeway_north", 160, sample=[[12, 17]] + [[x, 16] for x in range(12, 18)] + [[17, y] for y in range(12, 16)])
m.edge("g_mi_start", "middle_island_start", "middle_island_assembly", 20)
m.edge("g_mi_dock", "middle_island_assembly", "middle_island_pickup", 40, sample=[[16, 14], [17, 14], [18, 14], [18, 13]])
m.edge("g_mi_staging", "middle_island_assembly", "middle_staging", 40, sample=[[14, 14], [13, 14], [12, 14], [12, 13]])
m.edge("g_mi_causeway", "middle_island_assembly", "high_causeway_north", 40, sample=[[16, 14], [17, 14], [17, 13], [17, 12]])
m.edge("g_staging_causeway", "middle_staging", "high_causeway_north", 80, sample=[[12, 13], [12, 12]] + [[x, 12] for x in range(12, 18)])
m.edge("g_high_causeway_n", "high_causeway_north", "breach_north", 100, sample=[[17, y] for y in range(9, 13)])
m.edge("g_high_breach", "breach_north", "breach_south", 40, sample=[[17, 9], [17, 7]], tags=["bridge"])
m.edge("g_high_causeway_s", "breach_south", "high_causeway_south", 100, sample=[[17, y] for y in range(4, 8)])
m.edge("g_causeway_depot", "high_causeway_south", "depot", 40, sample=[[17, 4], [16, 4], [15, 4], [15, 3]])
m.edge("g_oi_start", "outer_island_start", "outer_island_assembly", 20)
m.edge("g_oi_dock", "outer_island_assembly", "outer_island_pickup", 40, sample=[[16, 21], [17, 21], [18, 21]])
m.edge("g_oi_south", "outer_island_assembly", "outer_south_dock", 20, sample=[[15, 21], [15, 20]])
m.edge("g_lh_start", "lighthouse_start", "lighthouse_assembly", 20)
m.edge("g_lh_landing", "lighthouse_assembly", "lighthouse_pickup", 20, sample=[[9, 8], [9, 7]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2000)
m.edge("w_depot_hw", "depot", "harbor_west", 60, sample=[[15, 5], [15, 6], [14, 6], [13, 6], [13, 7], [13, 8], [12, 8]], **water)
m.edge("w_landing_he", "high_landing", "harbor_east", 100, sample=[[25, y] for y in range(5, 9)] + [[x, 8] for x in range(21, 26)], **water)
m.edge("w_harbor_gap", "harbor_west", "harbor_east", 80, sample=[[x, 8] for x in range(12, 22)], **water)
m.edge("w_hw_lowjetty", "harbor_west", "low_market_pickup", 120, sample=[[x, 9] for x in range(5, 13)] + [[5, 10], [5, 11], [12, 8]], **water)
m.edge("w_hw_lighthouse", "harbor_west", "lighthouse_pickup", 60, sample=[[12, 8], [11, 8], [11, 7], [10, 7]], **water)
m.edge("w_hw_staging", "harbor_west", "middle_staging", 100, sample=[[11, y] for y in range(8, 14)] + [[12, 8]], **water)
m.edge("w_he_middle", "harbor_east", "middle_island_pickup", 80, sample=[[21, y] for y in range(8, 14)] + [[20, 13], [19, 13]], **water)
m.edge("w_outer_loop", "harbor_east", "outer_island_pickup", 400,
       sample=[[x, 8] for x in range(21, 31)] + [[30, y] for y in range(8, 22)] + [[x, 21] for x in range(19, 31)], **water)
m.edge("w_tidal_lane", "middle_staging", "outer_south_dock", 160,
       sample=[[11, y] for y in range(13, 19)] + [[x, 18] for x in range(11, 16)] + [[15, 19]], **water)

# ---------------------------------------------------------------- sockets
m.socket("bridge:01", "bridge", edge="g_high_breach", at=[17, 8], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_footbridge_breach", at=[11, 17], zone="bridges")
m.socket("barrier:01", "barrier", at=[11, 5], links=[[[11, 5], [12, 5]], [[12, 6], [12, 5]]], zone="barriers")
m.socket("barrier:02", "barrier", at=[13, 5], links=[[[13, 5], [12, 5]]], zone="barriers")
m.socket("barrier:03", "barrier", at=[7, 11], links=[[[7, 11], [7, 12]]], zone="barriers")
m.socket("generator:01", "generator", at=[12, 5], platform=[[12, 5]], power="harbor", zone="generators")
m.socket("generator:02", "generator", at=[20, 1], platform=[[20, 1]], power="ridge", zone="generators")
m.socket("relay:01", "relay", at=[10, 5], platform=[[10, 5]], zone="relays")
m.socket("relay:02", "relay", at=[18, 2], platform=[[18, 2]], zone="relays")
m.socket("relay:03", "relay", at=[8, 5], platform=[[8, 5]], zone="relays")
m.socket("skiff:01", "skiff", node="depot", at=[15, 5])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 5])
m.socket("skiff:03", "skiff", node="middle_island_pickup", at=[19, 13])
m.socket("skiff:04", "skiff", node="low_market_pickup", at=[5, 11])

m.power = {
    "nodes": [{"id": "harbor"}, {"id": "ridge"}, {"id": "lamp"}],
    "links": [
        {"from": "harbor", "to": "lamp", "relay": "relay:01"},
        {"from": "ridge", "to": "harbor", "relay": "relay:02"},
        {"from": "harbor", "to": "lamp", "relay": "relay:03"},
    ],
}
m.infrastructure = [
    {"id": "lighthouse", "power": "lamp", "demand": 1, "region": "lighthouse_rock", "nodes": ["depot"], "gates_dock": True},
]
m.groups = [
    {"id": "low_market", "start": "low_market_start", "assembly": "low_market_assembly", "pickup": "low_market_pickup"},
    {"id": "middle_island", "start": "middle_island_start", "assembly": "middle_island_assembly", "pickup": "middle_island_pickup"},
    {"id": "lighthouse", "start": "lighthouse_start", "assembly": "lighthouse_assembly", "pickup": "lighthouse_pickup"},
    {"id": "outer_island", "start": "outer_island_start", "assembly": "outer_island_assembly", "pickup": "outer_island_pickup"},
]
m.routes = [{"id": "high_causeway", "from": "middle_island_assembly", "to": "depot", "class": "ground"}]
m.roles.update({
    "harbor_power": {"infrastructure": ["lighthouse"]},
    "lighthouse": {"infrastructure": ["lighthouse"]},
    "outer_loop": {"edges": ["w_outer_loop"]},
    "tidal_lane": {"edges": ["w_tidal_lane"]},
    "high_causeway": {"edges": ["g_high_breach"]},
})
compact_swap(m, {"depot": 16, "high_landing": 10}, {"depot": 10, "high_landing": 16}, {"w_landing_he": 160})

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(4, 5))
