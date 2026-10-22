"""W1-M2 The Lower Road — authored layout (recipe W1-M2-layout-v1; anchors kept approximately).

Story: rain falls on the upper basin, a small lake beside the lower road. The lake drains south through a
narrow culvert into the lower creek and on to the main river by the depot. The lower road — the only ground
route out of the ferry landing — dips through a sunken underpass still flooded from the last storm. A pump
can lift that water west into the empty basin (correct) or north into the orchard (the trap: it transfers the
hazard onto the orchard assembly). When the lake swells it seeps over two low lips onto the road shoulder;
paired barriers on the lips spare the road, but with the lips sealed the late surge tops the stone spillway
by the viaduct instead, so the pump is still needed for the medical station. The medical station stands on a
low pad on the road shoulder; its generator can stand on the dry ridge beside it or on a convenient low
platform by the viaduct that the late surge drowns. The orchard is reachable only by skiff; the river houses
walk to the high landing on the east bank.
"""
from mapkit import Map, LEGEND_W1

LIP_ELEV = 370          # lips seep once the lake rises with the rain
LIP_COND = 18_000
SPILL_ELEV = 950       # spillway only tops when the lips are sealed and the late surge arrives
SPILL_COND = 500_000
CULVERT_COND = 200_000

LEGEND = dict(LEGEND_W1)
LEGEND.update({
    "R": {"terrain": "reservoir", "code": 4, "elev": 0, "init_depth": 300, "cond": 2_000_000},
    "u": {"terrain": "upper_basin", "code": 4, "elev": 0, "init_depth": 340, "cond": 2_000_000},
    "c": {"terrain": "culvert", "code": 0, "elev": 0, "init_depth": 300, "cond": CULVERT_COND},
    "p": {"terrain": "underpass", "code": 2, "elev": 0, "init_depth": 250},
    "b": {"terrain": "low_lip", "code": 1, "elev": LIP_ELEV, "cond": LIP_COND},
    "x": {"terrain": "spillway", "code": 2, "elev": SPILL_ELEV, "cond": SPILL_COND},
    "s": {"terrain": "shoulder", "code": 7, "elev": 100, "cond": 500_000},
    "M": {"terrain": "medical_pad", "code": 8, "elev": 200},
    "P": {"terrain": "low_platform", "code": 2, "elev": 650},
})

m = Map("W1-M2", legend=LEGEND)

# ---------------------------------------------------------------- terrain
# main river (east): mountain reservoir -> east branch -> weir outlet (as W1-M1)
m.rect("R", 13, 21, 17, 23)
m.line("~", (15, 20), (15, 15))
m.line("~", (16, 15), (19, 15), (19, 2))
m.line("~", (18, 2), (16, 2), (16, 1))
m.cell("w", 16, 0)
# upper basin lake, culvert outlet and the two-cell-wide lower creek to the main river
m.rect("u", 11, 7, 12, 10)
m.cell("c", 11, 6)
m.rect("~", 11, 1, 12, 5)
m.line("~", (13, 1), (15, 1))
# west bank: orchard terrace (skiff only), empty basin, lower road with its flooded underpass
m.rect("o", 4, 11, 8, 13)
m.rect("d", 1, 6, 4, 9)
m.rect("v", 6, 3, 9, 5)            # ferry landing terrace
m.cell("=", 8, 6)
m.rect("p", 7, 7, 8, 8)            # sunken underpass, still flooded
m.cell("=", 8, 9); m.cell("s", 9, 9)
m.cell("s", 9, 7); m.cell("s", 9, 8)
m.cell("b", 10, 7); m.cell("b", 10, 8)
m.cell("x", 10, 9)                 # stone spillway beside the viaduct
m.cell("M", 9, 6)                  # medical station pad
m.cell("P", 13, 10)                # low generator platform by the viaduct
# east bank: viaduct landing, high road, depot
m.line("=", (14, 9), (14, 5))
m.rect("D", 13, 2, 15, 4)
# river houses village, riverside road and long ridge road to the high landing
m.rect("v", 21, 10, 25, 13)
m.line("=", (23, 9), (23, 6))
m.rect("H", 20, 3, 25, 5)
m.line("=", (26, 11), (28, 11), (28, 4), (26, 4))

# ---------------------------------------------------------------- water
m.sources = [
    {"id": "upper_in", "at": [12, 10], "rate": 10_000_000},
    {"id": "reservoir_in", "at": [15, 22], "rate": 10_000_000},
]
m.sinks = [{"id": "outlet", "at": [16, 0], "rate": 80_000_000}]
m.regions = {
    "orchard_assembly": "o",
    "medical": "M",
    "lower_road": "p",
    "diversion_basin": "d",
    "upper_basin": "u",
}

# ---------------------------------------------------------------- navigation
cap = dict(capacity=4)
m.node("orchard_start", (7, 12), ["assembly"], **cap)
m.node("orchard_assembly", (7, 11), ["assembly"], **cap)
m.node("orchard_dock", (10, 10), ["dock"], capacity=6, water_at=[11, 10], dock_min_depth=100, dock_max_depth=800)
m.node("ferry_landing_start", (7, 4), ["assembly"], **cap)
m.node("ferry_landing_assembly", (8, 4), ["assembly"], **cap)
m.node("ferry_landing_pickup", (9, 5), ["assembly"], capacity=6)
m.node("ferry_dock", (10, 5), ["dock"], capacity=6, water_at=[11, 5], dock_min_depth=100, dock_max_depth=800)
m.node("lower_road_south", (8, 6), ["junction"])
m.node("underpass", (8, 8), ["junction"])
m.node("west_viaduct", (9, 9), ["junction"])
m.node("east_viaduct", (14, 9), ["junction"])
m.node("depot", (13, 3), ["evacuation", "dock"], capacity=8, water_at=[12, 3], dock_min_depth=100, dock_max_depth=2500)
m.node("river_houses_start", (23, 12), ["assembly"], **cap)
m.node("river_houses_assembly", (23, 11), ["assembly"], **cap)
m.node("river_houses_pickup", (23, 10), ["assembly", "vehicle_parking"], capacity=6)
m.node("ridge_corner", (28, 11), ["junction"])
m.node("high_landing", (20, 4), ["evacuation", "dock"], capacity=4, water_at=[19, 4], dock_min_depth=100, dock_max_depth=2500)

m.edge("g_orchard_start", "orchard_start", "orchard_assembly", 20)
m.edge("g_orchard_dock", "orchard_assembly", "orchard_dock", 30, sample=[[8, 11]])
m.edge("g_ferry_start", "ferry_landing_start", "ferry_landing_assembly", 20)
m.edge("g_ferry_pickup", "ferry_landing_assembly", "ferry_landing_pickup", 20)
m.edge("g_ferry_dock", "ferry_landing_pickup", "ferry_dock", 10)
m.edge("g_lower_road_a", "ferry_landing_assembly", "lower_road_south", 40, sample=[[8, 4], [8, 5], [8, 6]])
m.edge("g_lower_road_b", "lower_road_south", "underpass", 40, sample=[[8, 6], [8, 7], [8, 8]])
m.edge("g_lower_road_c", "underpass", "west_viaduct", 40, sample=[[8, 8], [8, 9], [9, 9]])
m.edge("g_viaduct", "west_viaduct", "east_viaduct", 50, sample=[[9, 9], [14, 9]])
m.edge("g_high_road", "east_viaduct", "depot", 130, sample=[[14, y] for y in range(5, 10)])
m.edge("g_river_start", "river_houses_start", "river_houses_assembly", 20)
m.edge("g_river_short", "river_houses_assembly", "river_houses_pickup", 20)
m.edge("g_river_road", "river_houses_pickup", "high_landing", 260, via=[[23, 4]], sample=[[23, y] for y in range(4, 11)])
m.edge("g_river_ridge", "river_houses_assembly", "ridge_corner", 200, sample=[[26, 11], [27, 11], [28, 11]])
m.edge("g_ridge_down", "ridge_corner", "high_landing", 380, sample=[[28, y] for y in range(4, 12)])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.edge("w_orchard_ferry", "orchard_dock", "ferry_dock", 100, sample=[[11, y] for y in range(5, 11)], **water)
m.edge("w_ferry_depot", "ferry_dock", "depot", 60, sample=[[11, 5], [11, 4], [12, 4], [12, 3]], **water)
m.edge("w_depot_landing", "depot", "high_landing", 200,
       sample=[[12, 2], [12, 1], [13, 1], [14, 1], [15, 1], [16, 1], [16, 2], [17, 2], [18, 2], [19, 2], [19, 3], [19, 4]], **water)

# ---------------------------------------------------------------- sockets
m.socket("pump:01", "pump", at=[6, 8], intake=[7, 8], outlet=[4, 8], power="grid", zone="pumps")
m.socket("pump:02", "pump", at=[8, 10], intake=[8, 8], outlet=[8, 11], power="grid", zone="pumps")
m.socket("generator:01", "generator", at=[7, 6], platform=[[7, 6]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[13, 10], platform=[[13, 10]], power="grid", zone="generators")
m.socket("barrier:01", "barrier", at=[10, 7], links=[[[11, 7], [10, 7]]], zone="lips")
m.socket("barrier:02", "barrier", at=[10, 8], links=[[[11, 8], [10, 8]]], zone="lips")
m.socket("barrier:03", "barrier", at=[10, 9], links=[[[11, 9], [10, 9]]], zone="lips")
m.socket("skiff:01", "skiff", node="depot", at=[12, 3])
m.socket("skiff:02", "skiff", node="ferry_dock", at=[11, 5])
m.socket("beacon:01", "beacon", node="depot", at=[14, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[21, 4])

m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.infrastructure = [
    {"id": "medical_station", "power": "grid", "demand": 1, "region": "medical", "max_depth": 100},
]
m.groups = [
    {"id": "orchard", "start": "orchard_start", "assembly": "orchard_assembly", "pickup": "orchard_dock"},
    {"id": "river_houses", "start": "river_houses_start", "assembly": "river_houses_assembly", "pickup": "river_houses_pickup"},
    {"id": "ferry_landing", "start": "ferry_landing_start", "assembly": "ferry_landing_assembly", "pickup": "ferry_dock"},
]
m.roles = {
    "upper_basin": {"sources": ["upper_in"]},
    "lower_road": {"edges": ["g_lower_road_b", "g_lower_road_c"]},
    "medical_station": {"infrastructure": ["medical_station"]},
    "orchard_assembly": {"regions": ["orchard_assembly"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(1, 2))
