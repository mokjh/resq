"""W3-M2 Above the Snowline — authored layout (recipe W3-M2-layout-v1; anchors kept).

Story: three terraces. The observatory (west top, 6 people, one step-free) has two ways down: the upper snow
path over the col to the village (stairs, closes under snow at 50 s) and the upper cable from its platform to the
sheltered intermediate hut on the middle terrace. The hut holds four people; its only way on is the lower cable to
the valley depot (or the second lower cable socket to the high landing). The station group (east middle terrace,
4, one step-free) is held until 160 s; it then walks the long east road to the high landing or crosses the short
traverse into the hut and rides the lower cable. The village (bottom west, 4) walks its road to the depot.

Power is split: the upper cable runs on the observatory grid (one generator socket on the top terrace), the
lower cables on the valley grid (generator sockets at the hut and at the depot). The grids are not linked, so a
staged relay needs one generator per grid. The upper cable platform stands on `upper_platform`, already weakened
(base 250, so it shows a warning from the start); from 290 s it loses 2 points/s and, unbraced, reaches zero and
collapses at S≈4150, taking the upper winch with it. One brace (+200) keeps it standing to the end (190). Ground graphs are deliberately separate: the observatory/col/
village/depot side never touches the hut/station/high-landing side on foot, so only cables join them.
"""
from w3_kit import W3Map, ROAD, STAIR

m = W3Map("W3-M2")

# ---------------------------------------------------------------- terrain (presentation)
m.rect("O", 3, 16, 9, 20)            # observatory terrace (top)
m.rect("u", 3, 14, 9, 15)            # observatory platform
m.line("t", (10, 16), (11, 16), (11, 12), (8, 12), (8, 10))    # upper snow path over the col
m.line("|", (9, 14), (12, 14), (12, 13))                       # upper cable
m.rect("m", 12, 11, 15, 12)          # sheltered intermediate hut (middle terrace)
m.line("|", (14, 10), (14, 5))       # lower cable to the depot
m.line("|", (16, 11), (24, 11), (24, 7))   # second lower cable socket (to the high landing)
m.rect("m", 22, 15, 28, 20)          # station terrace (east)
m.line("=", (21, 14), (16, 14), (16, 12))  # traverse up to the hut
m.line("=", (28, 14), (28, 7))       # long east road
m.rect("v", 5, 6, 9, 9)              # village (bottom west)
m.line("=", (10, 6), (13, 6), (13, 4))     # village road
m.rect("D", 13, 2, 17, 4)
m.rect("H", 24, 4, 28, 6)

# ---------------------------------------------------------------- navigation
m.group_nodes("observatory", (6, 18), 6)
m.group_nodes("station", (25, 18), 4, pickup_capacity=6)
m.group_nodes("village", (7, 9), 4, pickup_capacity=6)
m.node("col", (10, 12), ["junction"], capacity=6)
m.node("intermediate", (13, 12), ["staging"], capacity=4)
m.node("east_bend", (28, 10), ["junction"], capacity=6)
m.node("orchard_lane", (4, 4), ["junction"], capacity=6)
m.refuges((15, 3), (26, 5), {"depot": 10, "high_landing": 4}, {"depot": 4, "high_landing": 10},
          compact_edge_ticks={"g_east_road_b": 400})

m.edge("g_upper_path", "observatory_pickup", "col", 150, **STAIR)
m.edge("g_col_descent", "col", "village_pickup", 300, **STAIR)
m.cable("c_upper", "observatory_pickup", "intermediate", 50)
m.cable("c_lower", "intermediate", "depot", 50)
m.cable("c_lower_east", "intermediate", "high_landing", 50)
m.edge("g_station_traverse", "station_pickup", "intermediate", 150, **ROAD)
m.edge("g_east_road_a", "station_pickup", "east_bend", 300, **ROAD)
m.edge("g_east_road_b", "east_bend", "high_landing", 300, **ROAD)
m.edge("g_village_road", "village_pickup", "depot", 300, **ROAD)
m.edge("g_orchard_lane_a", "village_pickup", "orchard_lane", 300, **ROAD)
m.edge("g_orchard_lane_b", "orchard_lane", "depot", 300, **ROAD)

# ---------------------------------------------------------------- structures
m.support("upper_platform", base=250, nodes=["observatory_pickup"], edges=["c_upper"],
          collapse_nodes=["observatory_pickup"], collapse_edges=["c_upper"])

# ---------------------------------------------------------------- sockets
m.socket("winch:01", "winch", node="observatory_pickup", edge="c_upper", at=[9, 14], support="upper_platform",
         power="upper_grid", zone="upper_winch")
m.socket("winch:02", "winch", node="intermediate", edge="c_lower", at=[14, 11], power="lower_grid", zone="lower_winches")
m.socket("winch:03", "winch", node="intermediate", edge="c_lower_east", at=[15, 11], power="lower_grid",
         zone="lower_winches")
m.socket("generator:01", "generator", at=[4, 17], platform=[[4, 17]], power="upper_grid", zone="upper_generators")
m.socket("generator:02", "generator", at=[12, 11], platform=[[12, 11]], power="lower_grid", zone="lower_generators")
m.socket("generator:03", "generator", at=[17, 3], platform=[[17, 3]], power="lower_grid", zone="lower_generators")
for i, x in enumerate((7, 8, 9), 1):
    m.socket(f"brace:{i:02d}", "brace", at=[x, 14], support="upper_platform", zone="braces")
m.beacons([("depot", (14, 3)), ("high_landing", (27, 5))])

m.power = {"nodes": [{"id": "upper_grid", "fixed_supply": 0}, {"id": "lower_grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "observatory", "start": "observatory_start", "assembly": "observatory_assembly", "pickup": "observatory_pickup"},
    {"id": "station", "start": "station_start", "assembly": "station_assembly", "pickup": "station_pickup", "held": True},
    {"id": "village", "start": "village_start", "assembly": "village_assembly", "pickup": "village_pickup"},
]
m.routes = [{"id": "observatory_cable", "from": "observatory_pickup", "to": "depot", "class": "cable"}]
m.roles = {
    "upper_path": {"edges": ["g_upper_path"]},
    "station": {"groups": ["station"]},
    "observatory": {"groups": ["observatory"]},
    "upper_platform": {"supports": ["upper_platform"]},
    "intermediate": {"edges": ["c_upper", "c_lower"]},
    "powered_cable": {"edges": ["c_upper", "c_lower"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(3, 2))
