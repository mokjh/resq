"""W1-M5 The Long Way Home — authored layout (recipe W1-M5-layout-v1; anchors kept approximately).

Story: the whole valley in one board. The reservoir feeds the fork; the main river runs east of the central
ridge through a narrow viaduct arch, so each surge backs it up. At the fork a stone spillway (500 mm) lets
high water fall into the dry spill channel and on to the empty diversion basin in the south-west — the
valley's one safe store, with finite capacity. Three elevations give alternate routes: the upper village
(raised terrace) and the workshop reach the depot by the short road over a stone bridge (a landslide closes it
at 170 s) or by the long route over the viaduct gap (bridge:01) and down the ridge road. The island landing in
the upper lake can only leave by skiff; the orchard on the east bank walks a low road to the east landing that
the peak surge overtops, or crosses the river by bridge:02 / boards at its dock. The ridge road's last stretch
to the depot passes the downstream basin, which the 420 s late inflow fills unless it is pumped; the pump's
generator stands on riverside ground (floods at the peak unless a barrier seals it) or on the diversion-basin
rim (floods if the basin is over-filled).
"""
from mapkit import Map, LEGEND_W1

VIADUCT_COND = 600_000
SPILL_ELEV = 700
GATE_ELEV = 300
BASIN_Y0 = 2

LEGEND = dict(LEGEND_W1)
LEGEND.update({
    "R": {"terrain": "reservoir", "code": 4, "elev": 0, "init_depth": 300, "cond": 2_000_000},
    "k": {"terrain": "reed_narrows", "code": 0, "elev": 0, "init_depth": 300, "cond": 20_000},
    "u": {"terrain": "upper_lake", "code": 4, "elev": 0, "init_depth": 300, "cond": 2_000_000},
    "c": {"terrain": "viaduct_arch", "code": 0, "elev": 0, "init_depth": 300, "cond": VIADUCT_COND},
    "x": {"terrain": "fork_spillway", "code": 12, "elev": SPILL_ELEV, "cond": 1_000_000},
    "G": {"terrain": "main_river_sill", "code": 12, "elev": GATE_ELEV, "init_depth": 100, "cond": 2_000_000},
    "s": {"terrain": "spill_channel", "code": 0, "elev": 0, "cond": 2_000_000},
    "V": {"terrain": "village", "code": 11, "elev": 700},
    "I": {"terrain": "island", "code": 9, "elev": 1200},
    "W": {"terrain": "workshop", "code": 11, "elev": 600},
    "b": {"terrain": "low_lip", "code": 1, "elev": 440},
    "B": {"terrain": "basin_rim_notch", "code": 1, "elev": 900},
    "n": {"terrain": "downstream_basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "l": {"terrain": "ridge_road_dip", "code": 2, "elev": 250},
    "P": {"terrain": "riverside_platform", "code": 2, "elev": 400},
    "Q": {"terrain": "rim_platform", "code": 2, "elev": 300},
    "h": {"terrain": "high_road", "code": 2, "elev": 1200},
})

m = Map("W1-M5", legend=LEGEND)

# ---------------------------------------------------------------- terrain
m.rect("R", 13, 21, 17, 23)                     # reservoir
m.rect("u", 4, 17, 10, 20)                      # upper lake ...
m.rect("I", 5, 18, 6, 19)                       # ... with the island landing
m.line("~", (11, 19), (14, 19))                 # lake mouth joins the dam outflow channel
m.cell("k", 11, 19)                             # reed narrows at the lake mouth (boats pass, little flow)
m.line("~", (15, 19), (15, 15))                 # dam outflow channel to the fork
m.line("~", (16, 15), (19, 15), (19, 2))        # main river (east of the ridge)
m.line("~", (18, 2), (16, 2), (16, 1))
m.cell("w", 16, 0)
m.cell("c", 19, 5)                              # narrow viaduct arch
m.cell("x", 14, 15)                             # fork spillway
m.cell("G", 16, 15)                             # stone sill at the head of the main river
m.line("s", (13, 15), (11, 15), (11, 4))        # dry spill channel
m.rect("d", 4, BASIN_Y0, 11, 3)                        # empty diversion basin
m.cell("B", 8, 4)                               # low notch in the basin rim
m.rect("W", 6, 5, 9, 7)                         # workshop terrace
m.cell("Q", 10, 4)                              # rim platform (generator:02)
m.rect("V", 4, 11, 8, 13)                       # upper village
m.line("=", (7, 10), (7, 8), (10, 8))           # short road ...
m.cell("h", 12, 8)                              # ... over the stone bridge at (11,8) to a high abutment
m.line("h", (9, 12), (10, 12))                  # long route west abutment
m.line("h", (12, 12), (13, 12), (13, 9))        # ridge road
m.cell("h", 13, 8)
m.line("=", (13, 7), (13, 5))
m.cell("l", 13, 6); m.cell("l", 13, 7)          # the ridge road dips past the downstream basin
m.rect("n", 14, 6, 15, 7)                       # downstream basin
m.rect("D", 13, 2, 15, 4)                       # depot
m.cell("P", 18, 9)                              # riverside platform (generator:01)
# east bank: orchard, its low road and the east landing
m.rect("o", 21, 11, 25, 13)
m.cell("b", 20, 8)                              # lip where the backed-up river tops onto the low road
m.rect("L", 21, 7, 23, 9)
m.line("L", (23, 10), (23, 6))
m.rect("H", 21, 3, 26, 5)

# ---------------------------------------------------------------- water
m.sources = [
    {"id": "reservoir_in", "at": [15, 17], "rate": 10_000_000},
    {"id": "downstream_in", "at": [15, 7], "rate": 0},
]
m.sinks = [{"id": "outlet", "at": [16, 0], "rate": 80_000_000}]
m.regions = {"diversion_basin": "d", "downstream_basin": "n", "upper_village": "V", "workshop": "W",
             "orchard": "o", "upper_lake": "u"}

# ---------------------------------------------------------------- navigation
m.node("upper_village_start", (7, 12), ["assembly"], capacity=5)
m.node("upper_village_assembly", (7, 11), ["assembly"], capacity=5)
m.node("upper_village_pickup", (7, 10), ["assembly"], capacity=6)
m.node("short_road_bend", (7, 8), ["junction"])
m.node("stone_bridge_west", (10, 8), ["junction"])
m.node("stone_bridge_east", (12, 8), ["junction"])
m.node("ridge_junction", (13, 8), ["junction"])
m.node("viaduct_west", (10, 12), ["junction"])
m.node("viaduct_east", (12, 12), ["junction"])
m.node("depot", (14, 3), ["evacuation", "dock"], capacity=15, water_at=[16, 2], dock_min_depth=100, dock_max_depth=2500)
m.node("workshop_start", (8, 6), ["assembly"], capacity=5)
m.node("workshop_assembly", (8, 7), ["assembly"], capacity=5)
m.node("workshop_pickup", (9, 7), ["assembly"], capacity=6)
m.node("island_start", (5, 19), ["assembly"], capacity=5)
m.node("island_assembly", (5, 18), ["assembly"], capacity=5)
m.node("island_pickup", (6, 18), ["dock"], capacity=6, water_at=[7, 18], dock_min_depth=100, dock_max_depth=2500)
m.node("lake_mouth", (14, 19), ["junction"])
m.node("fork", (16, 15), ["junction"])
m.node("orchard_start", (23, 12), ["assembly"], capacity=5)
m.node("orchard_assembly", (23, 11), ["assembly"], capacity=5)
m.node("orchard_pickup", (20, 12), ["dock"], capacity=6, water_at=[19, 12], dock_min_depth=100, dock_max_depth=2500)
m.node("orchard_road", (23, 10), ["junction"])
m.node("east_crossing", (20, 10), ["junction"])
m.node("west_crossing", (18, 10), ["junction"])
m.node("high_landing", (21, 4), ["evacuation", "dock"], capacity=5, water_at=[19, 4], dock_min_depth=100, dock_max_depth=2500)

m.edge("g_uv_start", "upper_village_start", "upper_village_assembly", 20)
m.edge("g_uv_pickup", "upper_village_assembly", "upper_village_pickup", 20)
m.edge("g_short_a", "upper_village_pickup", "short_road_bend", 60, sample=[[7, 9], [7, 8]])
m.edge("g_short_b", "short_road_bend", "stone_bridge_west", 60, sample=[[x, 8] for x in range(7, 11)])
m.edge("g_short_bridge", "stone_bridge_west", "stone_bridge_east", 40, sample=[[10, 8], [12, 8]])
m.edge("g_short_c", "stone_bridge_east", "ridge_junction", 20, sample=[[12, 8], [13, 8]])
m.edge("g_ridge_south", "ridge_junction", "depot", 100, sample=[[13, y] for y in range(4, 9)])
m.edge("g_long_a", "upper_village_assembly", "viaduct_west", 160, sample=[[8, 12], [9, 12], [10, 12]])
m.edge("g_long_gap", "viaduct_west", "viaduct_east", 60, sample=[[10, 12], [12, 12]], tags=["bridge"])
m.edge("g_long_b", "viaduct_east", "ridge_junction", 240, sample=[[12, 12], [13, 12], [13, 11], [13, 10], [13, 9]])
m.edge("g_ws_start", "workshop_start", "workshop_assembly", 20)
m.edge("g_ws_pickup", "workshop_assembly", "workshop_pickup", 20)
m.edge("g_ws_road", "workshop_pickup", "stone_bridge_west", 40, sample=[[9, 7], [10, 7], [10, 8]])
m.edge("g_ws_up", "workshop_assembly", "short_road_bend", 40, sample=[[8, 7], [7, 7], [7, 8]])
m.edge("g_is_start", "island_start", "island_assembly", 20)
m.edge("g_is_pickup", "island_assembly", "island_pickup", 20)
m.edge("g_or_start", "orchard_start", "orchard_assembly", 20)
m.edge("g_or_dock", "orchard_assembly", "orchard_pickup", 40, sample=[[22, 11], [21, 11], [20, 12]])
m.edge("g_or_road_a", "orchard_assembly", "orchard_road", 20)
m.edge("g_or_road_b", "orchard_road", "high_landing", 240, via=[[23, 4]], sample=[[23, y] for y in range(5, 11)])
m.edge("g_or_cross", "orchard_assembly", "east_crossing", 60, sample=[[22, 11], [21, 10], [20, 10]])
m.edge("g_bridge_east", "east_crossing", "west_crossing", 40, sample=[[20, 10], [18, 10]], tags=["bridge"])
m.edge("g_ridge_east", "west_crossing", "ridge_junction", 120, sample=[[18, 10], [14, 10], [13, 9]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.edge("w_island_mouth", "island_pickup", "lake_mouth", 120, sample=[[x, 19] for x in range(7, 15)] + [[7, 18]], **water)
m.edge("w_mouth_fork", "lake_mouth", "fork", 140, sample=[[15, y] for y in range(15, 20)], **water)
m.edge("w_fork_orchard", "fork", "orchard_pickup", 60, sample=[[17, 15], [18, 15], [19, 15], [19, 14], [19, 13], [19, 12]], **water)
m.edge("w_orchard_depot", "orchard_pickup", "depot", 160, sample=[[19, y] for y in range(2, 13)] + [[18, 2], [17, 2], [16, 2]], **water)
m.edge("w_depot_landing", "depot", "high_landing", 60, sample=[[16, 2], [17, 2], [18, 2], [19, 2], [19, 3], [19, 4]], **water)

# ---------------------------------------------------------------- sockets
m.socket("bridge:01", "bridge", edge="g_long_gap", at=[11, 12], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_bridge_east", at=[19, 10], zone="bridges")
m.socket("barrier:01", "barrier", at=[16, 15], links=[[[15, 15], [16, 15]]], zone="fork")      # seal the main river: divert into the basin
m.socket("barrier:02", "barrier", at=[14, 15], links=[[[15, 15], [14, 15]]], zone="fork")      # seal the spillway
m.socket("barrier:03", "barrier", at=[18, 9], links=[[[19, 9], [18, 9]]], zone="banks")        # protect riverside generator ground
m.socket("barrier:04", "barrier", at=[20, 8], links=[[[19, 8], [20, 8]]], zone="banks")        # protect the orchard low road
m.socket("pump:01", "pump", at=[17, 6], intake=[15, 6], outlet=[19, 6], power="grid", zone="pumps")
m.socket("pump:02", "pump", at=[12, 6], intake=[14, 6], outlet=[11, 6], power="grid", zone="pumps")
m.socket("generator:01", "generator", at=[18, 9], platform=[[18, 9]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[10, 4], platform=[[10, 4]], power="grid", zone="generators")
m.socket("skiff:01", "skiff", node="depot", at=[16, 2])
m.socket("skiff:02", "skiff", node="orchard_pickup", at=[19, 12])
m.socket("beacon:01", "beacon", node="depot", at=[14, 4])
m.socket("beacon:02", "beacon", node="high_landing", at=[22, 4])

m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "upper_village", "start": "upper_village_start", "assembly": "upper_village_assembly", "pickup": "upper_village_pickup"},
    {"id": "orchard", "start": "orchard_start", "assembly": "orchard_assembly", "pickup": "orchard_pickup"},
    {"id": "workshop", "start": "workshop_start", "assembly": "workshop_assembly", "pickup": "workshop_pickup"},
    {"id": "island", "start": "island_start", "assembly": "island_assembly", "pickup": "island_pickup"},
]
m.routes = [{"id": "depot_upper", "from": "depot", "to": "upper_village_assembly", "class": "ground"}]
m.roles = {
    "reservoir": {"sources": ["reservoir_in"]},
    "short_road": {"edges": ["g_short_bridge"]},
    "downstream_basin": {"sources": ["downstream_in"]},
    "depot_upper": {"edges": ["g_long_gap", "g_short_bridge"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(1, 5))
