"""W4-M2 Keep the Light — authored layout (recipe W4-M2-layout-v1; anchors kept approximately).

Story: the lighthouse keepers (lighthouse group) live on a rock off the depot. Its landing is a high shelf
(900 mm) that a boat can only work at the peak tide, so the keepers are always the last group out. From 210 s
the depot's safe-route signal needs lighthouse power (`set_infrastructure_demand`), so every unload at the
depot after that tick, including the final one, needs a live power route from the harbor plant to the lamp.

Power: a generator (generator:01 on the dry yard; generator:02 on the depot wharf, which the peak covers)
feeds the plant. Routes to the lamp:
  * relay:01, the low cable channel straight across the strait (650 mm islet): drowned from 70 s;
  * relay:03 (yard, plant->ridge) then relay:02 (breakwater, 1000 mm, ridge->lamp): the short network,
    covered by 150 mm at the 350 s peak;
  * relay:03 then relay:04 (sea stack, 1200 mm, ridge->lamp): the long elevated network, dry at every tide.
relay:03 carries the only plant->ridge span. Relays share one relocation zone.

Transport: the west island can only be reached by boat across tidal flats (from 70 s). The market stands on a
900 mm terrace that floods at the peak; it can walk the low causeway to the high landing or board at its
low-water pontoon (both lost at the peak).
"""
from mapkit import Map
from w4_kit import new_map, add_tide, compact_swap

m = new_map("W4-M2", {
    "q": {"terrain": "cable_islet", "code": 7, "elev": 650, "cond": 2_000_000},
    "b": {"terrain": "breakwater", "code": 12, "elev": 1000, "cond": 2_000_000},
    "k": {"terrain": "rock_shelf", "code": 7, "elev": 900, "cond": 2_000_000},
    "W": {"terrain": "depot_wharf", "code": 2, "elev": 1000, "cond": 2_000_000},
})

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 0, 31, 4)                         # harbor mainland
m.rect("D", 13, 2, 16, 4)                        # depot
m.cell("W", 17, 4)                               # depot wharf (generator:02)
m.rect("L", 23, 2, 27, 4)                        # high landing
m.line("=", (1, 1), (12, 1))                     # yard road to the ridge
# west island with tidal flats on its harbor side
m.rect("#", 1, 13, 7, 19)
m.rect("f", 8, 12, 10, 20)
# market island: terrace on its south-west side, low causeway to the high landing
m.rect("#", 24, 13, 30, 19)
m.rect("m", 24, 13, 28, 15)
m.line("c", (26, 5), (26, 12))
# lighthouse rock with its high shelf landing; relay islets in the strait
m.rect("#", 6, 6, 9, 8)
m.cell("k", 10, 7); m.cell("k", 11, 7)
m.cell("q", 11, 5)                               # low cable channel islet (relay:01)
m.cell("b", 9, 5)                                # breakwater (relay:02)
m.cell("#", 4, 6)                                # sea stack (relay:04)

# ---------------------------------------------------------------- water
add_tide(m)
m.regions = {"market": "m", "west_flats": "f", "lighthouse_rock": {"x0": 6, "y0": 6, "x1": 9, "y1": 8}}

# ---------------------------------------------------------------- navigation
m.node("west_island_start", (5, 15), ["assembly"], capacity=6)
m.node("west_island_assembly", (5, 14), ["assembly"], capacity=6)
m.node("west_island_pickup", (7, 14), ["dock"], capacity=6, water_at=[8, 14], dock_min_depth=100, dock_max_depth=2000)
m.node("market_start", (26, 15), ["assembly"], capacity=6)
m.node("market_assembly", (26, 14), ["assembly"], capacity=6)
m.node("market_pickup", (24, 14), ["dock"], capacity=6, water_at=[23, 14], dock_min_depth=100, dock_max_depth=900)
m.node("causeway_north", (26, 13), ["junction"])
m.node("causeway_south", (26, 4), ["junction"])
m.node("lighthouse_start", (8, 7), ["assembly"], capacity=4)
m.node("lighthouse_assembly", (8, 8), ["assembly"], capacity=4)
m.node("lighthouse_pickup", (9, 7), ["dock"], capacity=6, water_at=[10, 7], dock_min_depth=100, dock_max_depth=2000)
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=10, water_at=[15, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=6, water_at=[25, 5], dock_min_depth=100, dock_max_depth=2000)
m.node("harbor", (15, 10), ["junction"])

m.edge("g_wi_start", "west_island_start", "west_island_assembly", 20)
m.edge("g_wi_pier", "west_island_assembly", "west_island_pickup", 30, sample=[[6, 14], [7, 14]])
m.edge("g_mk_start", "market_start", "market_assembly", 20)
m.edge("g_mk_pontoon", "market_assembly", "market_pickup", 30, sample=[[25, 14], [24, 14]])
m.edge("g_mk_causeway_n", "market_assembly", "causeway_north", 20)
m.edge("g_market_causeway", "causeway_north", "causeway_south", 280, sample=[[26, y] for y in range(4, 14)])
m.edge("g_causeway_landing", "causeway_south", "high_landing", 20, sample=[[26, 4], [25, 4]])
m.edge("g_lh_start", "lighthouse_start", "lighthouse_assembly", 20)
m.edge("g_lh_landing", "lighthouse_assembly", "lighthouse_pickup", 20, sample=[[9, 8], [9, 7]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2000)
m.edge("w_depot_harbor", "depot", "harbor", 60, sample=[[15, y] for y in range(5, 11)], **water)
m.edge("w_harbor_west", "harbor", "west_island_pickup", 140,
       sample=[[15, y] for y in range(10, 15)] + [[x, 14] for x in range(8, 15)], **water)
m.edge("w_harbor_market", "harbor", "market_pickup", 140,
       sample=[[15, y] for y in range(10, 15)] + [[x, 14] for x in range(15, 24)], **water)
m.edge("w_harbor_landing", "harbor", "high_landing", 140,
       sample=[[x, 10] for x in range(15, 26)] + [[25, y] for y in range(5, 10)], **water)
m.edge("w_depot_rock", "depot", "lighthouse_pickup", 100,
       sample=[[15, 5], [15, 6], [14, 6], [13, 6], [12, 6], [12, 7], [11, 7], [10, 7]], **water)
m.edge("w_harbor_rock", "harbor", "lighthouse_pickup", 120,
       sample=[[x, 10] for x in range(12, 16)] + [[12, 9], [12, 8], [12, 7], [11, 7], [10, 7]], **water)

# ---------------------------------------------------------------- sockets
m.socket("generator:01", "generator", at=[14, 1], platform=[[14, 1]], power="plant", zone="generators")
m.socket("generator:02", "generator", at=[17, 4], platform=[[17, 4]], power="plant", zone="generators")
m.socket("relay:01", "relay", at=[11, 5], platform=[[11, 5]], zone="relays")   # low cable channel
m.socket("relay:02", "relay", at=[9, 5], platform=[[9, 5]], zone="relays")     # short: breakwater
m.socket("relay:03", "relay", at=[12, 2], platform=[[12, 2]], zone="relays")   # plant -> ridge (yard)
m.socket("relay:04", "relay", at=[4, 6], platform=[[4, 6]], zone="relays")     # long: sea stack
m.socket("skiff:01", "skiff", node="depot", at=[15, 5])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 5])
m.socket("skiff:03", "skiff", node="market_pickup", at=[23, 14])
m.socket("beacon:01", "beacon", node="depot", at=[14, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[24, 3])
m.socket("beacon:03", "beacon", node="market_assembly", at=[27, 14])

m.power = {
    "nodes": [{"id": "plant", "fixed_supply": 0}, {"id": "ridge", "fixed_supply": 0}, {"id": "lamp", "fixed_supply": 0}],
    "links": [
        {"from": "plant", "to": "lamp", "relay": "relay:01"},
        {"from": "plant", "to": "ridge", "relay": "relay:03"},
        {"from": "ridge", "to": "lamp", "relay": "relay:02"},
        {"from": "ridge", "to": "lamp", "relay": "relay:04"},
    ],
}
# Demand 0 until the 210 s event; from then on the depot dock is usable only while the lamp is powered.
m.infrastructure = [
    {"id": "lighthouse", "power": "lamp", "demand": 0, "region": "lighthouse_rock", "nodes": ["depot"], "gates_dock": True},
]
m.groups = [
    {"id": "west_island", "start": "west_island_start", "assembly": "west_island_assembly", "pickup": "west_island_pickup"},
    {"id": "market", "start": "market_start", "assembly": "market_assembly", "pickup": "market_pickup"},
    {"id": "lighthouse", "start": "lighthouse_start", "assembly": "lighthouse_assembly", "pickup": "lighthouse_pickup"},
]
m.routes = [
    {"id": "market_ground", "from": "market_assembly", "to": "high_landing", "class": "ground"},
    {"id": "lighthouse_water", "from": "depot", "to": "lighthouse_pickup", "class": "water"},
]
m.roles.update({
    "lighthouse": {"infrastructure": ["lighthouse"]},
    "tidal_lane": {"edges": ["w_harbor_west"]},
    "receiving_dock": {"infrastructure": ["lighthouse"]},
})
compact_swap(m, {"depot": 10, "high_landing": 6}, {"depot": 6, "high_landing": 10}, {"w_harbor_landing": 200})

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(4, 2))
