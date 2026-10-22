"""W3-M3 The Quiet Slope — authored layout (recipe W3-M3-layout-v1; anchors kept).

Story: a broad snow slope lies between the weather station (west top, 6 people, one step-free) and the valley
depot. The short road (300 ticks) zig-zags straight down it; a longer rock-shelf bypass (600 ticks, stairs) runs
east along the ridge to the rock shelf (4 people, one step-free) and down the shelf stair to the high landing.
The shelf's old debris gully is step-free but administratively blocked until 340 s, when the cooled debris can be
cleared. The village (bottom west, 6) reaches the depot by the slope-foot lane or by the long west track.

The marked runout `quiet_slope` releases at 210 s: the upper segment buries the upper slope road and its middle
bend, 10 ticks later the lower segment buries the lower road, the slope foot and the village lane. Debris settles
for 200 ticks but the road is not `debris_reopenable`: the short road is gone for the rest of the mission. The
depot and the high landing are not linked on the ground, so after the release the only ways out of the top are
the shelf bypass and the shelf cable.

Vehicles follow ground edges of any step class (rules-v1), so a crawler can climb the shelf stair; step-free
residents on foot cannot. Braces lock onto the lift station and the ridge footbridge; they cannot hold a slope.
"""
from w3_kit import W3Map, ROAD, STAIR, seg

m = W3Map("W3-M3")

# ---------------------------------------------------------------- terrain (presentation)
m.rect("u", 3, 15, 9, 20)                     # weather station terrace
m.rect("a", 9, 5, 15, 14)                     # the quiet slope (marked runout)
m.line("=", (9, 15), (12, 10), (14, 6), (15, 5))   # short road down the slope
m.line("t", (10, 18), (21, 18), (21, 16))     # ridge traverse (rock-shelf bypass)
m.rect("k", 22, 14, 28, 20)                   # rock shelf
m.line("t", (24, 13), (24, 7))                # shelf stair
m.line("=", (27, 13), (27, 7))                # debris gully (cleared at 340 s)
m.line("|", (10, 16), (22, 16))               # shelf lift cable (above the short road)
m.rect("v", 4, 6, 8, 9)                       # village
m.line("=", (9, 6), (13, 6))                  # slope-foot lane
m.line("t", (3, 5), (3, 3), (12, 3))          # long west track
m.rect("D", 13, 2, 17, 4)
m.rect("H", 24, 4, 28, 6)

# ---------------------------------------------------------------- navigation
m.group_nodes("weather_crew", (6, 18), 6)
m.group_nodes("rock_shelf", (25, 18), 4, pickup_capacity=6)
m.group_nodes("village", (7, 9), 6)
m.node("slope_top", (9, 15), ["junction"], capacity=6)
m.node("slope_mid", (12, 10), ["junction"], capacity=6)
m.node("slope_foot", (14, 6), ["junction"], capacity=6)
m.node("ridge_gap", (15, 18), ["junction"], capacity=6)
m.node("shelf_landing", (22, 16), ["assembly"], capacity=6)
m.node("west_track", (3, 4), ["junction"], capacity=6)
m.refuges((15, 3), (26, 5), {"depot": 10, "high_landing": 6}, {"depot": 6, "high_landing": 10},
          compact_edge_ticks={"g_shelf_stair": 400})

BYPASS = dict(STAIR, tags=["rock_shelf_bypass"])
m.edge("g_short_top", "weather_crew_pickup", "slope_top", 50, **ROAD)
m.edge("g_slope_upper", "slope_top", "slope_mid", 100, **ROAD)
m.edge("g_slope_lower", "slope_mid", "slope_foot", 100, **ROAD)
m.edge("g_short_bottom", "slope_foot", "depot", 50, **ROAD)
m.edge("g_ridge_traverse", "weather_crew_pickup", "ridge_gap", 150, **BYPASS)
m.edge("g_shelf_traverse", "ridge_gap", "shelf_landing", 130, **BYPASS)
m.edge("g_shelf_walk", "shelf_landing", "rock_shelf_pickup", 20, **ROAD)
m.edge("g_shelf_stair", "rock_shelf_pickup", "high_landing", 300, **BYPASS)
m.edge("g_debris_gully", "rock_shelf_pickup", "high_landing", 300, enabled=False, **ROAD)
m.cable("c_shelf_lift", "weather_crew_pickup", "shelf_landing", 50)
m.cable("c_shelf_drop", "shelf_landing", "high_landing", 50)
m.edge("g_village_lane", "village_pickup", "slope_foot", 250, **ROAD)
m.edge("g_west_track_a", "village_pickup", "west_track", 300, **ROAD)
m.edge("g_west_track_b", "west_track", "depot", 300, **ROAD)

# ---------------------------------------------------------------- structures and runout
m.support("lift_station", nodes=["weather_crew_pickup"], edges=["c_shelf_lift"])
m.support("ridge_bridge", nodes=["ridge_gap"], edges=["g_ridge_traverse", "g_shelf_traverse"])
m.runout("quiet_slope", [
    seg("upper", edges=["g_slope_upper"], nodes=["slope_mid"], next=["lower"],
        cells={"x0": 9, "y0": 9, "x1": 15, "y1": 14}),
    seg("lower", edges=["g_slope_lower", "g_village_lane"], nodes=["slope_foot"],
        cells={"x0": 9, "y0": 5, "x1": 15, "y1": 8}),
])

# ---------------------------------------------------------------- sockets
m.socket("crawler:01", "crawler", node="depot", at=[16, 3])
m.socket("crawler:02", "crawler", node="high_landing", at=[25, 5])
m.socket("winch:01", "winch", node="weather_crew_pickup", edge="c_shelf_lift", at=[9, 16], support="lift_station",
         power="ridge_grid", zone="winches")
m.socket("winch:02", "winch", node="shelf_landing", edge="c_shelf_drop", at=[23, 15], power="ridge_grid", zone="winches")
m.socket("generator:01", "generator", at=[4, 16], platform=[[4, 16]], power="ridge_grid", zone="generators")
m.socket("generator:02", "generator", at=[23, 16], platform=[[23, 16]], power="ridge_grid", zone="generators")
m.socket("brace:01", "brace", at=[8, 15], support="lift_station", zone="braces")
m.socket("brace:02", "brace", at=[9, 14], support="lift_station", zone="braces")
m.socket("brace:03", "brace", at=[15, 17], support="ridge_bridge", zone="braces")
m.socket("brace:04", "brace", at=[16, 17], support="ridge_bridge", zone="braces")
m.beacons([("depot", (14, 3)), ("high_landing", (27, 5)), ("shelf_landing", (22, 17))])

m.power = {"nodes": [{"id": "ridge_grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "weather_crew", "start": "weather_crew_start", "assembly": "weather_crew_assembly", "pickup": "weather_crew_pickup"},
    {"id": "rock_shelf", "start": "rock_shelf_start", "assembly": "rock_shelf_assembly", "pickup": "rock_shelf_pickup"},
    {"id": "village", "start": "village_start", "assembly": "village_assembly", "pickup": "village_pickup"},
]
m.routes = [{"id": "weather_crew_cable", "from": "weather_crew_pickup", "to": "shelf_landing", "class": "cable"}]
m.roles = {
    "quiet_slope": {"runouts": ["quiet_slope"]},
    "debris_bypass": {"edges": ["g_debris_gully"]},
    "weather_crew": {"groups": ["weather_crew"]},
    "upper_path": {"edges": ["g_slope_upper"]},
    "rock_shelf_bypass": {"edges": ["g_ridge_traverse", "g_shelf_traverse", "g_shelf_stair"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(3, 3))
