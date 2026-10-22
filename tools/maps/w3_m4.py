"""W3-M4 Cable and Current — authored layout (recipe W3-M4-layout-v1; anchors kept).

Story: the station's main line runs in two spans. The upper cable crosses the valley from the upper terrace
(west, 6 people, one step-free) to the cable station (east, 6, one step-free); the lower cable drops from the
station to the valley depot. Its tower is the busy cable (`busy_cable`): from 190 s melt load adds 150 static
load (effective -150, one point per second), and unbraced it collapses at S≈4400. Its drive house sits on the
rim of the lower basin. The required route `powered_cable` is the whole main line, upper terrace → station →
depot, by cable, at the end.

The lower basin (20 cells, floor 0) fills from 60 s (lower snowmelt, 20 M mm³/s) and again from 310 s (melt
basin, a further 20 M mm³/s). Its two generator platforms (300 mm) and the drive house (150 mm) stand on the rim;
one pump (20 M mm³/s at zero lift) can lift basin water into the river. Pumped from the start, the basin ends
near 185 mm: platforms dry, drive house about 20 mm deep. Unpumped, the water tops the platforms after the second melt and
drowns the drive house deep enough to break the lower winch before the end.

Other ways: the upper terrace's standard walkers can take the long icy stairs to the lower village; the station
group can walk the long east road (step-free) to the high landing; a second lower cable socket (winch:03) runs
to the high landing from a dry drive, but it is not the main line. The lower village walks its road to the depot.
Ground graphs on the two sides of the valley are separate, so only the cables join them.
"""
from w3_kit import W3Map, ROAD, STAIR

LEGEND = {
    "d": {"terrain": "lower_basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "P": {"terrain": "lower_platform_a", "code": 2, "elev": 300},
    "Q": {"terrain": "lower_platform_b", "code": 2, "elev": 300},
    "w": {"terrain": "drive_house", "code": 23, "elev": 150},
    "~": {"terrain": "river", "code": 0, "elev": 0, "cond": 2_000_000},
}
m = W3Map("W3-M4", legend_extra=LEGEND)

# ---------------------------------------------------------------- terrain
m.rect("u", 3, 15, 9, 20)                     # upper terrace
m.line("|", (10, 16), (21, 16))               # upper cable across the valley
m.rect("S", 22, 15, 28, 20)                   # cable station
m.line("|", (22, 14), (22, 10), (17, 10), (17, 9))   # lower cable (busy tower) down to the basin rim
m.rect("d", 13, 5, 17, 8)                     # lower basin
m.cell("P", 12, 6)                            # generator platform a (west rim)
m.cell("Q", 18, 6)                            # generator platform b (east rim)
m.cell("w", 16, 4)                            # lower-cable drive house
m.rect("D", 13, 2, 17, 3)                     # depot
m.line("~", (20, 9), (20, 0))                 # river to the outlet
m.line("=", (28, 14), (28, 7))                # long east road
m.rect("H", 24, 4, 28, 6)
m.rect("v", 4, 7, 8, 10)                      # lower village
m.line("t", (6, 14), (6, 11))                 # icy stairs from the upper terrace
m.line("=", (9, 7), (11, 7), (11, 3), (12, 3))   # village road to the depot
m.line("t", (3, 6), (3, 2), (12, 2))          # long west track

# ---------------------------------------------------------------- water
m.sources = [
    {"id": "lower_basin_in", "at": [15, 7], "rate": 0},
    {"id": "melt_basin_in", "at": [14, 8], "rate": 0},
]
m.sinks = [{"id": "outlet", "at": [20, 0], "rate": 80_000_000}]
m.regions = {"lower_basin": "d", "lower_platform_a": "P", "lower_platform_b": "Q", "drive_house": "w"}

# ---------------------------------------------------------------- navigation
m.group_nodes("upper_terrace", (6, 18), 6)
m.group_nodes("cable_station", (25, 18), 6)
m.group_nodes("lower_village", (7, 9), 6)
m.node("east_bend", (28, 10), ["junction"], capacity=6)
m.node("west_track", (3, 4), ["junction"], capacity=6)
m.refuges((15, 3), (26, 5), {"depot": 12, "high_landing": 6}, {"depot": 6, "high_landing": 12},
          compact_edge_ticks={"g_east_road_b": 400})

m.cable("c_upper", "upper_terrace_pickup", "cable_station_pickup", 50)
m.cable("c_lower", "cable_station_pickup", "depot", 50)
m.cable("c_lower_east", "cable_station_pickup", "high_landing", 50)
m.edge("g_upper_stairs", "upper_terrace_pickup", "lower_village_assembly", 600, **STAIR)
m.edge("g_east_road_a", "cable_station_pickup", "east_bend", 300, **ROAD)
m.edge("g_east_road_b", "east_bend", "high_landing", 300, **ROAD)
m.edge("g_village_road", "lower_village_pickup", "depot", 300,
       sample=[[x, 7] for x in range(7, 12)] + [[11, y] for y in range(3, 7)] + [[12, 3], [13, 3]], **ROAD)
m.edge("g_west_track_a", "lower_village_pickup", "west_track", 300, **ROAD)
m.edge("g_west_track_b", "west_track", "depot", 300, **ROAD)

# ---------------------------------------------------------------- structures
m.support("busy_cable", nodes=["cable_station_pickup"], edges=["c_lower"], collapse_edges=["c_lower"])
m.support("upper_tower", base=600, nodes=["upper_terrace_pickup"], edges=["c_upper"], collapse_edges=["c_upper"])

# ---------------------------------------------------------------- sockets
m.socket("winch:01", "winch", node="upper_terrace_pickup", edge="c_upper", at=[9, 16], support="upper_tower",
         power="valley_grid", zone="upper_winch")
m.socket("winch:02", "winch", node="cable_station_pickup", edge="c_lower", at=[16, 4], support="busy_cable",
         power="valley_grid", zone="lower_winches")
m.socket("winch:03", "winch", node="cable_station_pickup", edge="c_lower_east", at=[27, 8], power="valley_grid",
         zone="lower_winches")
m.socket("generator:01", "generator", at=[12, 6], platform="P", power="valley_grid", zone="generators")
m.socket("generator:02", "generator", at=[18, 6], platform="Q", power="valley_grid", zone="generators")
m.socket("generator:03", "generator", at=[23, 14], platform=[[23, 14]], power="valley_grid", zone="generators")
m.socket("pump:01", "pump", at=[19, 7], intake=[17, 6], outlet=[20, 6], power="valley_grid", zone="pumps")
m.socket("pump:02", "pump", at=[12, 4], intake=[13, 5], outlet=[20, 3], power="valley_grid", zone="pumps")
m.socket("brace:01", "brace", at=[21, 14], support="busy_cable", zone="braces")
m.socket("brace:02", "brace", at=[23, 13], support="busy_cable", zone="braces")
m.socket("brace:03", "brace", at=[10, 15], support="upper_tower", zone="braces")
m.beacons([("depot", (14, 2)), ("high_landing", (27, 5))])

m.power = {"nodes": [{"id": "valley_grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "upper_terrace", "start": "upper_terrace_start", "assembly": "upper_terrace_assembly", "pickup": "upper_terrace_pickup"},
    {"id": "cable_station", "start": "cable_station_start", "assembly": "cable_station_assembly", "pickup": "cable_station_pickup"},
    {"id": "lower_village", "start": "lower_village_start", "assembly": "lower_village_assembly", "pickup": "lower_village_pickup"},
]
m.routes = [{"id": "powered_cable", "from": "upper_terrace_pickup", "to": "depot", "class": "cable"}]
m.roles = {
    "lower_basin": {"sources": ["lower_basin_in"]},
    "melt_basin": {"sources": ["melt_basin_in"]},
    "busy_cable": {"supports": ["busy_cable"]},
    "lower_platform_a": {"regions": ["lower_platform_a"]},
    "lower_platform_b": {"regions": ["lower_platform_b"]},
    "powered_cable": {"edges": ["c_upper", "c_lower"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(3, 4))
