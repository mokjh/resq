"""W1-M1 First Crossing — authored layout (recipe W1-M1-layout-v1 anchors).

Story: the bus group waits on the west bank; the only fast road to the depot crosses the west branch, which
needs the bridge. Four residents wait at the low dock on the east bank; a skiff can ferry them down the main
river. A barrier at either branch head decides where the reservoir surge goes.
"""
from mapkit import Map

m = Map("W1-M1")

# ---- terrain (roads first; water painted over them) ----
m.rect(".", 5, 11, 9, 13)                          # west-bank terrace (bus group)
m.line("=", (5, 10), (10, 10))                     # west road
m.line("=", (12, 10), (18, 10))                    # east road to the crossing
m.line("=", (14, 10), (14, 4))                     # high road down to the depot
m.rect("R", 13, 21, 17, 23)                       # mountain reservoir
m.line("~", (15, 20), (15, 15))                    # outflow to the fork
m.line("~", (16, 15), (19, 15), (19, 2))           # main river (east branch)
m.line("~", (18, 2), (16, 2), (16, 0))             # to the south outlet
m.line("~", (14, 15), (11, 15), (11, 6))           # west branch
m.rect("d", 4, 3, 10, 6)                           # empty diversion basin
m.rect("L", 20, 9, 24, 13)                         # low terrace at the low dock
m.rect("D", 13, 2, 15, 4)                          # rescue depot
m.rect("H", 20, 3, 25, 5)                          # high landing
m.line(".", (24, 8), (24, 6))                      # low causeway (long way on foot)
m.cell("w", 16, 0)                                 # outlet weir keeps boat depth in the river

# ---- water ----
m.sources = [
    {"id": "reservoir_in", "at": [15, 22], "rate": 10_000_000},
    {"id": "low_basin_in", "at": [22, 11], "rate": 0},
]
m.sinks = [{"id": "outlet", "at": [16, 0], "rate": 80_000_000}]
m.regions = {
    "low_terrace": "L",
    "diversion_basin": "d",
    "west_terrace": ".",
}

# ---- navigation ----
m.node("bus_start", (7, 12), ["assembly"], capacity=4)
m.node("bus_assembly", (7, 11), ["assembly"], capacity=4)
m.node("bus_pickup", (7, 10), ["assembly", "vehicle_parking"], capacity=6)
m.node("west_bridgehead", (10, 10), ["junction"])
m.node("east_bridgehead", (12, 10), ["junction"])
m.node("road_junction", (14, 10), ["junction"])
m.node("west_crossing", (18, 10), ["junction"])
m.node("east_crossing", (20, 10), ["junction"], safe_depth=100)
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=8, water_at=[16, 2], dock_min_depth=100, dock_max_depth=2500)
m.node("high_landing", (20, 4), ["evacuation", "dock"], capacity=4, water_at=[19, 4], dock_min_depth=100, dock_max_depth=2500)
m.node("low_dock_start", (23, 12), ["assembly"], capacity=4)
m.node("low_dock_assembly", (23, 11), ["assembly"], capacity=4)
m.node("low_dock", (20, 11), ["dock"], capacity=6, water_at=[19, 11], dock_min_depth=100, dock_max_depth=700)

m.edge("g_bus_start", "bus_start", "bus_assembly", 20)
m.edge("g_bus_assembly", "bus_assembly", "bus_pickup", 20)
m.edge("g_west_road", "bus_pickup", "west_bridgehead", 60)
m.edge("g_bridge_a", "west_bridgehead", "east_bridgehead", 40, sample=[[11, 10]], max_depth=800, tags=["bridge"])
m.edge("g_east_road", "east_bridgehead", "road_junction", 40)
m.edge("g_high_road", "road_junction", "depot", 160)
m.edge("g_crossing_road", "west_crossing", "road_junction", 80)
m.edge("g_bridge_b", "east_crossing", "west_crossing", 40, sample=[[19, 10]], max_depth=800, tags=["bridge"])
m.edge("g_low_start", "low_dock_start", "low_dock_assembly", 20)
m.edge("g_low_dock", "low_dock_assembly", "low_dock", 20)
m.edge("g_low_crossing", "low_dock_assembly", "east_crossing", 40)
m.edge("g_causeway", "low_dock_assembly", "high_landing", 600, via=[[24, 11], [24, 5]], max_depth=100)

water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.edge("w_low_to_landing", "low_dock", "high_landing", 200, sample=[[19, y] for y in range(4, 12)], **water)
m.edge("w_landing_to_depot", "high_landing", "depot", 80, sample=[[19, 3], [19, 2], [18, 2], [17, 2], [16, 2]], **water)

# ---- sockets ----
m.socket("bridge:01", "bridge", edge="g_bridge_a", at=[11, 10], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_bridge_b", at=[19, 10], zone="bridges")
m.socket("barrier:01", "barrier", at=[16, 15], links=[[[15, 15], [16, 15]]], zone="fork")
m.socket("barrier:02", "barrier", at=[14, 15], links=[[[15, 15], [14, 15]]], zone="fork")
m.socket("barrier:03", "barrier", at=[19, 11], links=[[[19, y], [20, y]] for y in range(9, 14)], zone="levee")
m.socket("skiff:01", "skiff", node="low_dock", at=[19, 11])
m.socket("skiff:02", "skiff", node="high_landing", at=[19, 4])
m.socket("beacon:01", "beacon", node="depot", at=[15, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[21, 4])

m.infrastructure = [{"id": "low_dock", "nodes": ["low_dock"], "demand": 0}]
m.vehicles = [{"id": "bus", "kind": "fixed_bus", "node": "bus_pickup", "seats": 4, "slots": 2}]
m.groups = [
    {"id": "bus", "start": "bus_start", "assembly": "bus_assembly", "pickup": "bus_pickup"},
    {"id": "low_dock", "start": "low_dock_start", "assembly": "low_dock_assembly", "pickup": "low_dock"},
]
m.roles = {
    "reservoir": {"sources": ["reservoir_in"]},
    "low_basin": {"sources": ["low_basin_in"]},
}
m.tutorial = {"group": "bus", "pickup": "bus_pickup", "destination": "depot"}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(1, 1))
