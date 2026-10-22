"""W5-M5 Every Little Life — authored layout (recipe W5-M5-layout-v1; anchors kept approximately).

Story: the whole of Cinder Reach in one board, with no new mechanic. Two lava channels and two water basins:
- The west flow (orchard_channel, 60 s) cuts the orchard and the research terraces off from the depot. Its upper crossing
  is open for the orchard until the lava arrives; its lower crossing (the inland route) is reached only after the lava,
  so the research group needs it cooled, or a skiff from its dock. Heat then reaches the orchard terraces (75 s) and the
  research terraces (150 s).
- The staging channel (400 s) runs north-south through the middle down to the harbour. It turns the high village's
  staging zone unsafe and boils the harbour mouth, so the final ferry route (ferry dock to depot) is open at the end only
  if the mouth is cooled in the last window.
- The drainage basin fills from 60 s. Unpumped, it drowns the first transport route (the low causeway the high village
  must walk, closed from about 220 s) and later spills into the high lagoon, which becomes deep enough for skiffs to
  reach the high-village quay. Pumped (one pump keeps the basin low), the causeway stays open and the lagoon stays dry.
- The power basin (290 s) floods the north generator platform unless the barrier seals its lip. The other generator
  platform sits above the drainage basin (dry while it is pumped). The generator powers the ferry terminal (skiffs can
  board at the ferry dock only while it is powered) and the pump.
"""
from w5_kit import W5Map, ROAD, NARROW, rect

m = W5Map("W5-M5")
m.legend.update({
    "L": {"terrain": "low_causeway", "code": 2, "elev": 150},
    "g": {"terrain": "high_lagoon", "code": 4, "elev": 330, "cond": 2_000_000},
    "e": {"terrain": "power_basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "P": {"terrain": "north_platform", "code": 2, "elev": 200},
    "Q": {"terrain": "basin_platform", "code": 2, "elev": 350},
    "u": {"terrain": "outfall", "code": 12, "elev": 0, "solid": True},
})

# ---------------------------------------------------------------- terrain
m.rect("S", 0, 0, 31, 1)                       # harbour (south sea)
m.rect("S", 30, 2, 31, 23)                     # east sea
m.rect("o", 2, 12, 7, 16)                      # orchard
m.rect(".", 4, 5, 8, 9)                        # research terraces
m.rect("v", 13, 14, 17, 17)                    # high village
m.rect("v", 24, 12, 28, 15)                    # geothermal station
m.rect("v", 24, 18, 28, 21)                    # waterfront
m.rect("D", 13, 2, 16, 3)                      # depot
m.rect("H", 24, 2, 27, 4)                      # high landing
m.rect("d", 12, 7, 17, 9)                      # drainage basin (18 cells)
m.rect("L", 12, 10, 17, 10)                    # low causeway (first transport route)
m.rect("g", 12, 5, 17, 6)                      # high lagoon
m.rect("e", 1, 19, 4, 22)                      # power basin (16 cells)
m.rect("P", 5, 19, 5, 20)                      # north generator platform
m.cell("Q", 11, 8)                             # basin generator platform
m.cell("u", 2, 2)                              # pump outfall (sink, no passive links)

# ---------------------------------------------------------------- lava
m.segment("west_flow", rect(9, 2, 10, 18), edges=["g_cross_w", "g_cross_r"],
          next=[("orchard_lobe", 150), ("research_lobe", 900)])
m.segment("orchard_lobe", rect(2, 17, 7, 18), nodes=["orchard_start", "orchard_assembly", "orchard_pickup"])
m.segment("research_lobe", rect(3, 10, 8, 10), nodes=["research_start", "research_assembly", "research_pickup"])
m.segment("staging", rect(19, 2, 20, 22), edges=["g_cross_s", "w_mouth"],
          nodes=["high_village_start", "high_village_assembly", "high_village_pickup", "quay_shelter"])
m.lava_sources = [{"id": "west_vent", "segment": "west_flow"}, {"id": "north_vent", "segment": "staging"}]
m.coolant = [{"id": "coolant", "charges": 2, "capacity": 8}]

# ---------------------------------------------------------------- water
m.sources = [{"id": "drain_in", "at": [14, 7], "rate": 0}, {"id": "power_in", "at": [3, 19], "rate": 0}]
m.sinks = [{"id": "outfall", "at": [2, 2], "rate": 80_000_000}]
m.regions = {"lagoon": "g", "causeway": "L"}

# ---------------------------------------------------------------- navigation
dock = dict(dock_min_depth=100, dock_max_depth=2500)
m.group_nodes("orchard", (5, 14), 6)
m.group_nodes("research", (7, 8), 6)
m.group_nodes("high_village", (15, 16), 6)
m.group_nodes("station", (26, 14), 6)
m.group_nodes("waterfront", (26, 21), 6)
m.node("w_bank", (8, 13), ["junction"], capacity=6)
m.node("w_east", (11, 13), ["junction"], capacity=6)
m.node("r_bank", (8, 6), ["junction"], capacity=6)
m.node("r_east", (11, 4), ["junction"], capacity=6)
m.node("r_dock", (6, 3), ["dock"], capacity=6, water_at=[6, 1], **dock)
m.node("hv_road", (11, 12), ["junction"], capacity=6)
m.node("causeway_e", (18, 11), ["junction"], capacity=6)
m.node("s_east", (21, 11), ["junction"], capacity=6)
m.node("station_junction", (24, 10), ["junction"], capacity=12)
m.node("hv_quay", (18, 5), ["dock"], capacity=2, water_at=[17, 5], **dock)
m.node("quay_shelter", (18, 6), ["shelter"], capacity=6)
m.node("ferry_dock", (29, 19), ["dock"], capacity=6, water_at=[30, 19], **dock)
m.node("harbour_buoy", (23, 1), ["junction"], capacity=6)
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=18, water_at=[15, 1], **dock)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=12, water_at=[25, 1], **dock)

# orchard: the upper crossing of the west flow
m.edge("g_orchard_bank", "orchard_pickup", "w_bank", 300, sample=[[5, 12], [8, 13]], **ROAD)
m.edge("g_cross_w", "w_bank", "w_east", 60, sample=[[9, 13], [10, 13]], tags=["crossing"], **NARROW)
m.edge("g_w_depot", "w_east", "depot", 300, sample=[[11, 13], [11, 3]], **ROAD)
# research: the inland route (lower crossing), or the research dock
m.edge("g_research_bank", "research_pickup", "r_bank", 700, sample=[[7, 6], [8, 6]], **ROAD)
m.edge("g_cross_r", "r_bank", "r_east", 60, sample=[[9, 6], [10, 5]], tags=["crossing"], **NARROW)
m.edge("g_r_depot", "r_east", "depot", 200, sample=[[11, 4], [13, 3]], **ROAD)
m.edge("g_research_dock", "research_pickup", "r_dock", 100, **ROAD)
# high village: the first transport route (low causeway, then the staging crossing), or the quay
m.edge("g_hv_road", "high_village_pickup", "hv_road", 2400, sample=[[15, 14], [11, 12]], **ROAD)
m.edge("g_causeway", "hv_road", "causeway_e", 300, sample=[[x, 10] for x in range(12, 18)], **ROAD)
m.edge("g_cross_s", "causeway_e", "s_east", 60, sample=[[19, 11], [20, 11]], tags=["crossing"], **ROAD)
m.edge("g_s_junction", "s_east", "station_junction", 100, **ROAD)
m.edge("g_hv_quay", "high_village_pickup", "quay_shelter", 400, sample=[[16, 13], [18, 6]], **ROAD)
m.edge("g_quay", "quay_shelter", "hv_quay", 20, **ROAD)
# station: straight down to the high landing
m.edge("g_station_junction", "station_pickup", "station_junction", 100, **ROAD)
m.edge("g_station_landing", "station_junction", "high_landing", 300, sample=[[24, 9], [25, 5]], **ROAD)
# waterfront: down to the ferry dock
m.edge("g_ferry_dock", "waterfront_pickup", "ferry_dock", 40, **ROAD)
# water
water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.edge("w_research", "r_dock", "depot", 250, sample=[[x, 1] for x in range(6, 16)], **water)
m.edge("w_mouth", "depot", "harbour_buoy", 150, sample=[[x, 1] for x in range(15, 24)], **water)
m.edge("w_landing", "harbour_buoy", "high_landing", 50, sample=[[23, 1], [25, 1]], **water)
m.edge("w_east", "harbour_buoy", "ferry_dock", 500, sample=[[x, 1] for x in range(23, 31)] + [[30, y] for y in range(1, 20)], **water)
m.edge("w_lagoon", "hv_quay", "depot", 150, sample=[[x, 5] for x in range(12, 18)] + [[x, 6] for x in range(12, 18)], **water)

# ---------------------------------------------------------------- power
m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.infrastructure = [{"id": "ferry_terminal", "power": "grid", "demand": 1, "nodes": ["ferry_dock"], "gates_dock": True}]

# ---------------------------------------------------------------- sockets
m.socket("generator:01", "generator", at=[5, 20], platform=[[5, 19], [5, 20]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[11, 8], platform=[[11, 8]], power="grid", zone="generators")
m.socket("pump:01", "pump", at=[11, 7], intake=[12, 8], outlet=[2, 2], power="grid", zone="pumps")
m.socket("pump:02", "pump", at=[2, 19], intake=[3, 19], outlet=[2, 2], power="grid", zone="pumps")
m.socket("barrier:01", "barrier", at=[5, 20], links=[[[4, 20], [5, 20]], [[4, 19], [5, 19]]], zone="barriers")
m.socket("barrier:02", "barrier", at=[12, 10], links=[[[12, 9], [12, 10]]], zone="barriers")
m.socket("cooler:01", "cooler", at=[8, 7], segment="west_flow", coolant_store="coolant", zone="coolers")
m.socket("cooler:02", "cooler", at=[21, 2], segment="staging", coolant_store="coolant", zone="coolers")
m.socket("cooler:03", "cooler", at=[18, 12], segment="staging", coolant_store="coolant", zone="coolers")
m.socket("shelter:01", "shelter", node="quay_shelter", at=[18, 7], zone="shelters")
m.socket("shelter:02", "shelter", node="r_shelter", at=[6, 4], zone="shelters")
m.node("r_shelter", (5, 4), ["shelter"], capacity=6)
m.edge("g_r_shelter", "research_pickup", "r_shelter", 60, **ROAD)
m.socket("skiff:01", "skiff", node="depot", at=[15, 1])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 1])
m.socket("skiff:03", "skiff", node="ferry_dock", at=[30, 19])
m.socket("crawler:01", "crawler", node="station_junction", at=[24, 11])
m.socket("crawler:02", "crawler", node="depot", at=[16, 4])

# compact layout: the high landing becomes the primary receiving point (recipe receiving swap)
m.class_overrides = {"compact": {"nodes": [{"id": "depot", "capacity": 12}, {"id": "high_landing", "capacity": 18}]}}
m.groups = [
    {"id": g, "start": f"{g}_start", "assembly": f"{g}_assembly", "pickup": f"{g}_pickup"}
    for g in ["orchard", "research", "high_village", "station", "waterfront"]
]
m.routes = [{"id": "final_ferry", "from": "ferry_dock", "to": "depot", "class": "water"}]
m.roles = {
    "drainage_basin": {"sources": ["drain_in"]},
    "orchard_channel": {"lava_sources": ["west_vent"]},
    "first_transport": {"edges": ["g_causeway"]},
    "power_basin": {"sources": ["power_in"]},
    "staging_channel": {"lava_sources": ["north_vent"]},
    "final_ferry": {"edges": ["w_mouth", "w_east"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(5, 5))
