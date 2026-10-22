"""W3-M1 Hold the Station — authored layout (recipe W3-M1-layout-v1; anchors kept).

Story: two terraces meet at the cable station. The upper station (west, 6 people, one step-free) stands on a
snow terrace whose only step-free way down is the powered cable from the station platform to the valley depot;
its five standard walkers can also take the long icy switchback (600 ticks, stairs). The lower station (east,
4 people, one step-free) walks the long lower road to the high landing (600 ticks, step-free) or crosses the
stepped station footbridge to ride the cable. The station platform, its cable and the footbridge stand on one
known weak support. The station is real infrastructure: `cable_support` needs one unit of power (a generator)
and a standing support; its platform only boards while the station is powered. Braces lock onto the weak
support (three sockets) or onto the lower-road retaining wall (one socket, never threatened). The rocks beside
the station are decorative: they take no brace.

Stability (spec 02 §8), base 400: from 60 s the support loses 1 point/s; from 160 s melt adds 100 static load
(effective -100 and 2 points/s); at 260 s a tremor removes 150 at once. Unbraced it reaches zero at S≈2600 and
collapses on the spot (the published warnings are long past); one brace holds it to S≈2850; two braces end the
mission at 50; three braces at 250.
"""
from w3_kit import W3Map, ROAD, STAIR

m = W3Map("W3-M1")

# ---------------------------------------------------------------- terrain (presentation)
m.rect("u", 3, 15, 9, 20)            # upper terrace
m.rect("S", 10, 14, 11, 16)          # cable station platform (on the weak support)
m.line("|", (12, 14), (12, 5), (14, 5))
m.rect("D", 13, 2, 17, 4)            # valley depot
m.line("t", (6, 14), (6, 8), (12, 8), (12, 6))   # icy switchback
m.rect("m", 22, 15, 28, 20)          # lower station terrace
m.line("=", (21, 16), (12, 16))      # stepped station footbridge
m.line("=", (25, 14), (25, 7))       # lower road
m.rect("H", 24, 4, 28, 6)            # high landing
m.rect("x", 8, 12, 9, 13)            # decorative rocks below the station (no brace sockets)
m.cell("k", 26, 10)                  # lower-road retaining wall

# ---------------------------------------------------------------- navigation
m.group_nodes("upper_station", (6, 18), 6)
m.group_nodes("lower_station", (25, 18), 4, pickup_capacity=6)
m.node("cable_station", (10, 15), ["assembly"], capacity=6)
m.node("switchback", (6, 9), ["junction"], capacity=6)
m.node("lower_bend", (25, 10), ["junction"], capacity=6)
m.refuges((15, 3), (26, 5), {"depot": 6, "high_landing": 4}, {"depot": 4, "high_landing": 6},
          compact_edge_ticks={"g_lower_road_b": 400})

m.edge("g_upper_platform", "upper_station_pickup", "cable_station", 40, **ROAD)
m.edge("g_switchback_a", "upper_station_pickup", "switchback", 300, **STAIR)
m.edge("g_switchback_b", "switchback", "depot", 300, **STAIR)
m.cable("c_station", "cable_station", "depot", 50)
m.edge("g_station_deck", "lower_station_pickup", "cable_station", 260, **STAIR)
m.edge("g_lower_road_a", "lower_station_pickup", "lower_bend", 300, **ROAD)
m.edge("g_lower_road_b", "lower_bend", "high_landing", 300, **ROAD)
m.cable("c_lower", "lower_station_pickup", "high_landing", 50)

# ---------------------------------------------------------------- structures
m.support("weak_support", nodes=["cable_station"], edges=["c_station", "g_station_deck"],
          collapse_nodes=["cable_station"], collapse_edges=["c_station", "g_station_deck", "g_upper_platform"])
m.support("lower_road_wall", base=600, nodes=["lower_bend"], edges=["g_lower_road_a", "g_lower_road_b"])

# ---------------------------------------------------------------- sockets
m.socket("winch:01", "winch", node="cable_station", edge="c_station", at=[11, 14], support="weak_support",
         power="station_grid", zone="winches")
m.socket("winch:02", "winch", node="lower_station_pickup", edge="c_lower", at=[26, 15], power="station_grid",
         zone="winches")
m.socket("generator:01", "generator", at=[9, 17], platform=[[9, 17]], power="station_grid", zone="generators")
m.socket("generator:02", "generator", at=[17, 4], platform=[[17, 4]], power="station_grid", zone="generators")
for i, x in enumerate((9, 10, 11), 1):
    m.socket(f"brace:{i:02d}", "brace", at=[x, 13], support="weak_support", zone="braces")
m.socket("brace:04", "brace", at=[26, 10], support="lower_road_wall", zone="braces")
m.beacons([("depot", (14, 3)), ("high_landing", (27, 5)), ("cable_station", (10, 16))])

m.power = {"nodes": [{"id": "station_grid", "fixed_supply": 0}], "links": []}
m.infrastructure = [
    {"id": "cable_support", "power": "station_grid", "demand": 1, "support": "weak_support",
     "nodes": ["cable_station"], "gates_dock": True},
]
m.groups = [
    {"id": "upper_station", "start": "upper_station_start", "assembly": "upper_station_assembly",
     "pickup": "upper_station_pickup"},
    {"id": "lower_station", "start": "lower_station_start", "assembly": "lower_station_assembly",
     "pickup": "lower_station_pickup"},
]
m.routes = [{"id": "upper_station_cable", "from": "cable_station", "to": "depot", "class": "cable"}]
m.roles = {
    "weak_support": {"supports": ["weak_support"]},
    "cable_support": {"infrastructure": ["cable_support"]},
    "upper_station": {"groups": ["upper_station"]},
    "lower_station": {"groups": ["lower_station"]},
    "powered_cable": {"edges": ["c_station"]},
    "upper_path": {"edges": ["g_switchback_a"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(3, 1))
