"""W4-M4 Storm Switch — authored layout (recipe W4-M4-layout-v1; anchors kept approximately).

Story: rain falls into the inland basin (36 cells, bed BASIN_BED; from 60 s, doubling at 440 s). The basin has
no drain: rain leaves it only through pumps. Its west side is a low tide sill (SILL_ELEV) on the west sea. At
the 190 s peak the sea stands 150 mm over the sill and pours in, and nothing lets that water out afterwards.
barrier:01 seals the sill (crest SILL_ELEV + 600). A barrier is damaged while the head difference across it
exceeds 800 mm, so the sealed sill holds only while the basin keeps some stored rain behind it (head above
350 mm at the peak): pumping the basin dry before the peak strips the barrier of its back-pressure.
The medical landing's only road home runs along the basin's north rim (ROAD_ELEV): the route is lost while the
basin stands more than 100 mm over it. The inland village is on the south rim (1000 mm). The market terrace
(800 mm) stands over the market basin, which a sluice joins to the harbor: the peak floods it.

Pumps (all draw from the inland basin):
  * pump:01 lifts over the east ridge into the occupied market basin (mastery forbids it);
  * pump:02 discharges into the west sea beside the sill; pump:03 by a rising main into the north sea. Both
    outlets are under the tide at the peak, which raises their lift and reduces their flow.
Power: generators stand in the depot yard. The pumphouse is reached by relay:01 on the low harbor pad
(covered at the peak, reconnects by itself after it) or by the ridge pair relay:02 + relay:03 (always dry).
"""
from mapkit import Map
from w4_kit import new_map, add_tide, compact_swap

SILL_ELEV = 1000
BASIN_BED = 330
ROAD_ELEV = 700

m = new_map("W4-M4", {
    "n": {"terrain": "inland_basin", "code": 6, "elev": BASIN_BED, "cond": 1_000_000},
    "k": {"terrain": "market_basin", "code": 6, "elev": 300, "cond": 1_000_000},
    "u": {"terrain": "tide_sill", "code": 12, "elev": SILL_ELEV, "cond": 2_000_000},
    "w": {"terrain": "market_sluice", "code": 12, "elev": 600, "cond": 200_000},
    "r": {"terrain": "medical_road", "code": 2, "elev": ROAD_ELEV, "cond": 1_000_000},
    "i": {"terrain": "inland_village", "code": 11, "elev": 1000, "cond": 1_000_000},
    "M": {"terrain": "market_terrace", "code": 11, "elev": 800, "cond": 1_000_000},
    "q": {"terrain": "low_relay_pad", "code": 7, "elev": 1000, "cond": 2_000_000},
})

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 0, 31, 4)                         # harbor mainland
m.rect("D", 13, 2, 16, 4)                        # depot
m.rect("L", 23, 2, 27, 4)                        # high landing
m.rect("#", 2, 5, 13, 19)                        # the storm island
m.rect("n", 3, 7, 8, 12)                         # inland basin (36 cells)
m.line("u", (2, 8), (2, 10))                     # three-cell tide sill to the west sea
m.line("r", (3, 13), (8, 13))                    # medical road on the basin's north rim
m.line("i", (3, 6), (8, 6))                      # inland village terrace (south rim)
m.rect("k", 10, 7, 13, 10)                       # market basin
m.cell("w", 14, 8)                               # market sluice to the harbor
m.rect("M", 10, 11, 13, 12)                      # market terrace
m.cell("q", 14, 5)                               # low relay pad (relay:01)

# ---------------------------------------------------------------- water
add_tide(m)
m.sources = [{"id": "inland_rain", "at": [5, 10], "rate": 0}]
m.regions = {
    "inland_basin": "n",
    "market_basin": "k",
    "market_terrace": "M",
    "inland_village": "i",
    "medical_road": "r",
}

# ---------------------------------------------------------------- navigation
m.node("medical_landing_start", (6, 16), ["assembly"], capacity=6)
m.node("medical_landing_assembly", (6, 15), ["assembly"], capacity=6)
m.node("medical_landing_pickup", (6, 19), ["dock"], capacity=6, water_at=[6, 20], dock_min_depth=100, dock_max_depth=2000)
m.node("road_west", (2, 13), ["junction"])
m.node("road_east", (9, 13), ["junction"])
m.node("island_south", (9, 5), ["junction"])
m.node("inland_start", (5, 6), ["assembly"], capacity=8)
m.node("inland_assembly", (6, 6), ["assembly"], capacity=8)
m.node("inland_pickup", (6, 5), ["assembly"], capacity=8)
m.node("market_start", (11, 12), ["assembly"], capacity=8)
m.node("market_assembly", (11, 11), ["assembly"], capacity=8)
m.node("market_pickup", (13, 13), ["dock"], capacity=8, water_at=[14, 13], dock_min_depth=100, dock_max_depth=2000)
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=14, water_at=[15, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=8, water_at=[25, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("harbor", (20, 10), ["junction"])

m.edge("g_ml_start", "medical_landing_start", "medical_landing_assembly", 20)
m.edge("g_ml_jetty", "medical_landing_assembly", "medical_landing_pickup", 60, sample=[[6, y] for y in range(15, 20)])
m.edge("g_ml_road", "medical_landing_assembly", "road_west", 100, sample=[[x, 15] for x in range(2, 7)] + [[2, 14], [2, 13]])
m.edge("g_medical_road", "road_west", "road_east", 140, sample=[[x, 13] for x in range(2, 10)])
m.edge("g_ridge_road", "road_east", "island_south", 140, sample=[[9, y] for y in range(5, 14)])
m.edge("g_island_depot", "island_south", "depot", 120, sample=[[9, 5], [9, 4]] + [[x, 4] for x in range(9, 16)])
m.edge("g_in_start", "inland_start", "inland_assembly", 20)
m.edge("g_in_pickup", "inland_assembly", "inland_pickup", 20, sample=[[6, 6], [6, 5]])
m.edge("g_in_road", "inland_pickup", "island_south", 60, sample=[[x, 5] for x in range(6, 10)])
m.edge("g_mk_start", "market_start", "market_assembly", 20)
m.edge("g_mk_dock", "market_assembly", "market_pickup", 40, sample=[[12, 11], [13, 11], [13, 12], [13, 13]])
m.edge("g_mk_road", "market_assembly", "road_east", 60, sample=[[10, 11], [9, 11], [9, 12], [9, 13]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2000)
m.edge("w_depot_harbor", "depot", "harbor", 100, sample=[[15, y] for y in range(5, 11)] + [[x, 10] for x in range(15, 21)], **water)
m.edge("w_landing_harbor", "high_landing", "harbor", 100, sample=[[25, y] for y in range(5, 11)] + [[x, 10] for x in range(20, 26)], **water)
m.edge("w_harbor_market", "harbor", "market_pickup", 80, sample=[[x, 13] for x in range(14, 21)] + [[20, 11], [20, 12]], **water)
m.edge("w_harbor_medical", "harbor", "medical_landing_pickup", 240,
       sample=[[20, y] for y in range(10, 21)] + [[x, 20] for x in range(6, 21)], **water)

# ---------------------------------------------------------------- sockets
m.socket("barrier:01", "barrier", at=[2, 9], links=[[[2, y], [3, y]] for y in (8, 9, 10)], zone="barriers")  # seal the tide sill
m.socket("barrier:02", "barrier", at=[14, 8], links=[[[13, 8], [14, 8]]], zone="barriers")    # seal the market sluice
m.socket("pump:01", "pump", at=[9, 10], intake=[8, 10], outlet=[10, 10], power="pumphouse", zone="pumps")
m.socket("pump:02", "pump", at=[2, 11], intake=[3, 11], outlet=[1, 11], power="pumphouse", zone="pumps")
m.socket("pump:03", "pump", at=[7, 14], intake=[7, 12], outlet=[7, 20], power="pumphouse", zone="pumps")
m.socket("generator:01", "generator", at=[12, 2], platform=[[12, 2]], power="yard", zone="generators")
m.socket("generator:02", "generator", at=[18, 2], platform=[[18, 2]], power="yard", zone="generators")
m.socket("relay:01", "relay", at=[14, 5], platform=[[14, 5]], zone="relays")
m.socket("relay:02", "relay", at=[10, 3], platform=[[10, 3]], zone="relays")
m.socket("relay:03", "relay", at=[9, 6], platform=[[9, 6]], zone="relays")
m.socket("skiff:01", "skiff", node="depot", at=[15, 5])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 5])
m.socket("skiff:03", "skiff", node="market_pickup", at=[14, 13])

m.power = {
    "nodes": [{"id": "yard"}, {"id": "ridge"}, {"id": "pumphouse"}],
    "links": [
        {"from": "yard", "to": "pumphouse", "relay": "relay:01"},
        {"from": "yard", "to": "ridge", "relay": "relay:02"},
        {"from": "ridge", "to": "pumphouse", "relay": "relay:03"},
    ],
}
m.groups = [
    {"id": "medical_landing", "start": "medical_landing_start", "assembly": "medical_landing_assembly", "pickup": "medical_landing_pickup"},
    {"id": "market", "start": "market_start", "assembly": "market_assembly", "pickup": "market_pickup"},
    {"id": "inland", "start": "inland_start", "assembly": "inland_assembly", "pickup": "inland_pickup"},
]
m.routes = [{"id": "medical_landing_access", "from": "medical_landing_assembly", "to": "depot", "class": "ground"}]
m.roles.update({
    "inland_basin": {"sources": ["inland_rain"]},
    "market_basin": {"regions": ["market_basin"]},
    "medical_landing_access": {"edges": ["g_medical_road"]},
})
compact_swap(m, {"depot": 14, "high_landing": 8}, {"depot": 8, "high_landing": 14}, {"w_landing_harbor": 160})

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(4, 4))
