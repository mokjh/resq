"""W3-M5 Dawn at Frostline — authored layout (recipe W3-M5-layout-v1; anchors kept).

Story: the whole mountain in one board. The observatory (west top, 8 people, one step-free) is cliff-bound: its
only way down is the upper cable to the station deck (the hub) on the east terrace. The station group (8, one
step-free) lives beside the deck. From the deck the lower cable drops to the receiving station in the valley, a
temporary transfer point (8 places, never a refuge). From there the crawler (6 seats) runs the short valley
shortcut to the depot until the 300 s runout buries it; the lower transport window (receiving station → high
landing, the long way) only opens at 420 s. The station group can also walk the long east road from its assembly
point to the high landing. The village (bottom west, 6, one step-free) walks the village lane through the
receiving station and the shortcut, or the long west track.

The station deck stands on `upper_support`: from 70 s it loses 1 point/s (warned at 60 s and again at 170 s),
reaching zero at S≈4700 unbraced, sooner if ten or more residents load it. A collapse takes both cables and the deck.
Mastery: the receiving station never holds more than six people at once.
"""
from w3_kit import W3Map, ROAD, seg

m = W3Map("W3-M5")

# ---------------------------------------------------------------- terrain (presentation)
m.rect("O", 3, 15, 9, 20)                      # observatory terrace (cliff-bound)
m.line("|", (10, 16), (21, 16))                # upper cable to the station deck
m.rect("S", 22, 15, 28, 20)                    # station terrace and deck
m.line("|", (22, 14), (22, 9), (16, 9))        # lower cable to the receiving station
m.rect("m", 14, 7, 16, 9)                      # receiving station (valley)
m.rect("a", 12, 4, 18, 6)                      # valley slope (marked runout over the shortcut)
m.line("=", (15, 6), (15, 4))                  # valley shortcut
m.rect("D", 13, 2, 17, 3)
m.line("=", (17, 8), (22, 8), (22, 6))         # lower transport window (opens at 420 s)
m.rect("H", 24, 4, 28, 6)
m.line("=", (28, 14), (28, 7))                 # long east road
m.rect("v", 4, 6, 8, 10)                       # village
m.line("=", (9, 7), (13, 7))                   # village lane to the receiving station
m.line("t", (3, 5), (3, 3), (12, 3))           # long west track

# ---------------------------------------------------------------- navigation
m.group_nodes("observatory", (6, 18), 8)
m.group_nodes("station", (25, 18), 8)
m.group_nodes("village", (7, 9), 6)
m.node("receiving_station", (15, 8), ["staging"], capacity=8)
m.node("window_bend", (22, 8), ["junction"], capacity=6)
m.node("east_bend", (28, 10), ["junction"], capacity=8)
m.node("west_track", (3, 4), ["junction"], capacity=6)
m.refuges((15, 3), (26, 5), {"depot": 14, "high_landing": 8}, {"depot": 8, "high_landing": 14},
          compact_edge_ticks={"g_east_road_b": 400})

m.cable("c_upper", "observatory_pickup", "station_pickup", 50)
m.cable("c_upper_west", "observatory_pickup", "station_pickup", 100)
m.cable("c_lower", "station_pickup", "receiving_station", 50)
m.edge("g_valley_shortcut", "receiving_station", "depot", 150, **ROAD)
m.edge("g_lower_window_a", "receiving_station", "window_bend", 300, enabled=False, **ROAD)
m.edge("g_lower_window_b", "window_bend", "high_landing", 300, **ROAD)
m.edge("g_east_road_a", "station_assembly", "east_bend", 300, **ROAD)
m.edge("g_east_road_b", "east_bend", "high_landing", 300, **ROAD)
m.edge("g_village_lane", "village_pickup", "receiving_station", 150, **ROAD)
m.edge("g_west_track_a", "village_pickup", "west_track", 300, **ROAD)
m.edge("g_west_track_b", "west_track", "depot", 300, **ROAD)

# ---------------------------------------------------------------- structures and runout
m.support("upper_support", nodes=["station_pickup"], edges=["c_upper", "c_upper_west", "c_lower"],
          collapse_nodes=["station_pickup"], collapse_edges=["c_upper", "c_upper_west", "c_lower"])
m.support("west_tower", base=600, nodes=["observatory_pickup"])
m.runout("valley_slope", [
    seg("shortcut", edges=["g_valley_shortcut"], cells={"x0": 12, "y0": 4, "x1": 18, "y1": 6}),
])

# ---------------------------------------------------------------- sockets
m.socket("winch:01", "winch", node="observatory_pickup", edge="c_upper", at=[10, 16], support="upper_support",
         power="frost_grid", zone="upper_winches")
m.socket("winch:03", "winch", node="observatory_pickup", edge="c_upper_west", at=[9, 15], support="upper_support",
         power="frost_grid", zone="upper_winches")
m.socket("winch:02", "winch", node="station_pickup", edge="c_lower", at=[22, 14], support="upper_support",
         power="frost_grid", zone="lower_winch")
m.socket("generator:01", "generator", at=[4, 16], platform=[[4, 16]], power="frost_grid", zone="generators")
m.socket("generator:02", "generator", at=[27, 15], platform=[[27, 15]], power="frost_grid", zone="generators")
m.socket("generator:03", "generator", at=[17, 2], platform=[[17, 2]], power="frost_grid", zone="generators")
m.socket("crawler:01", "crawler", node="receiving_station", at=[16, 8])
m.socket("crawler:02", "crawler", node="depot", at=[14, 2])
for i, (x, y) in enumerate(((21, 15), (21, 14), (23, 14)), 1):
    m.socket(f"brace:{i:02d}", "brace", at=[x, y], support="upper_support", zone="braces")
m.socket("brace:04", "brace", at=[8, 15], support="west_tower", zone="braces")

m.power = {"nodes": [{"id": "frost_grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "observatory", "start": "observatory_start", "assembly": "observatory_assembly", "pickup": "observatory_pickup"},
    {"id": "station", "start": "station_start", "assembly": "station_assembly", "pickup": "station_pickup"},
    {"id": "village", "start": "village_start", "assembly": "village_assembly", "pickup": "village_pickup"},
]
m.routes = [{"id": "observatory_cable", "from": "observatory_pickup", "to": "receiving_station", "class": "cable"}]
m.roles = {
    "upper_support": {"supports": ["upper_support"]},
    "valley_slope": {"runouts": ["valley_slope"]},
    "lower_window": {"edges": ["g_lower_window_a"]},
    "observatory": {"groups": ["observatory"]},
    "station": {"groups": ["station"]},
    "receiving_station": {"edges": ["c_lower", "g_valley_shortcut"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(3, 5))
