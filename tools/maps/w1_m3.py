"""W1-M3 Two Banks — authored layout (recipe W1-M3-layout-v1; anchors kept approximately).

Story: the main river splits the valley. The west village (8) sits on a raised terrace; its quick way out is a
narrow low footpath to the depot that passes a gully head (the central basin). The east workshop (6) stands
beyond the east lane, a side channel that leaves the river at the fork and runs down to the east landing; the
workshop can only leave by boat. Boats reach the depot down the inner lane (the main river) or the east
landing down the east lane. A weir at the foot of the east lane keeps boats from cutting across. Two bridge
sockets join only high paths: a ravine gap on the long west high road (to the depot) and a footbridge over the
river from the west high path to the east-bank strip path (to the east landing). Neither joins the docks.

Threats: at 70 s the central basin surges and overtops two lips onto the low footpath (paired barriers can
hold it; a stone spillway then carries the surge to the river); at 180 s debris closes the inner lane, so the
depot can no longer be reached by boat; at 320 s the east lane current quickens, making the east landing the
shorter remaining boat route.
"""
from mapkit import Map, LEGEND_W1

LEGEND = dict(LEGEND_W1)
LEGEND.update({
    "R": {"terrain": "reservoir", "code": 4, "elev": 0, "init_depth": 300, "cond": 2_000_000},
    "V": {"terrain": "village", "code": 11, "elev": 700},
    "f": {"terrain": "low_footpath", "code": 7, "elev": 400, "cond": 1_000_000},
    "b": {"terrain": "low_lip", "code": 1, "elev": 300, "cond": 1_000_000},
    "F": {"terrain": "footpath_dip", "code": 7, "elev": 300, "cond": 1_000_000},
    "e": {"terrain": "central_basin", "code": 6, "elev": 150, "cond": 1_000_000},
    "x": {"terrain": "spillway", "code": 2, "elev": 900, "cond": 2_000_000},
    "g": {"terrain": "gully", "code": 0, "elev": 350, "cond": 2_000_000},
    "h": {"terrain": "high_road", "code": 2, "elev": 1200},
    "r": {"terrain": "ravine", "code": 6, "elev": 0},
    "W": {"terrain": "weir", "code": 12, "elev": 200, "cond": 2_000_000},
})

m = Map("W1-M3", legend=LEGEND)

# ---------------------------------------------------------------- terrain
m.rect("R", 14, 21, 18, 23)                  # mountain reservoir
m.line("~", (16, 20), (16, 1))               # main river = inner lane
m.cell("w", 16, 0)                           # outlet weir
m.line("~", (17, 15), (22, 15), (22, 3))     # east lane leaves at the fork
m.line("~", (22, 2), (17, 2))
m.cell("W", 19, 2)                           # weir at the foot of the east lane (boats cannot pass)
# west bank
m.rect("V", 5, 10, 9, 13)                    # west village terrace
m.line("f", (7, 9), (7, 6), (12, 6), (12, 4))  # narrow low footpath to the depot
m.rect("e", 10, 8, 11, 8)                    # central basin (gully head)
m.cell("b", 10, 7); m.cell("b", 11, 7)       # two low lips above the footpath
m.cell("F", 10, 6); m.cell("F", 11, 6)       # the footpath dips where the gully water crosses it
m.cell("x", 12, 8)                           # stone spillway
m.line("g", (13, 8), (15, 8))                # spillway gully to the river
m.line("g", (13, 6), (15, 6))                # footpath drain gully to the river
m.line("h", (5, 12), (3, 12), (3, 2), (12, 2))   # long west high road
m.cell("r", 3, 7)                            # ravine gap (bridge:01)
m.line("h", (10, 13), (10, 14), (15, 14))    # west high path to the river footbridge
m.line("h", (10, 12), (14, 12))              # path to the west dock
m.rect("D", 13, 2, 15, 4)                    # depot
# east bank strip between the river and the east lane
m.line("h", (17, 14), (18, 14), (18, 6))
m.rect("H", 18, 3, 21, 5)                    # east landing (high landing)
# east workshop beyond the east lane
m.rect("v", 24, 10, 27, 13)
m.cell("h", 23, 13)

# ---------------------------------------------------------------- water
m.sources = [
    {"id": "reservoir_in", "at": [16, 22], "rate": 10_000_000},
    {"id": "central_in", "at": [10, 8], "rate": 0},
]
m.sinks = [{"id": "outlet", "at": [16, 0], "rate": 80_000_000}]
m.regions = {"central_basin": "e", "low_footpath": "f", "west_village": "V", "east_workshop": "v"}

# ---------------------------------------------------------------- navigation
m.node("west_village_start", (7, 12), ["assembly"], capacity=8)
m.node("west_village_assembly", (7, 11), ["assembly"], capacity=8)
m.node("west_village_pickup", (7, 10), ["assembly"], capacity=8)
m.node("footpath_bend", (7, 6), ["junction"])
m.node("footpath_ford", (11, 6), ["junction"])
m.node("high_corner_nw", (3, 12), ["junction"])
m.node("ravine_north", (3, 8), ["junction"])
m.node("ravine_south", (3, 6), ["junction"])
m.node("high_corner_sw", (3, 2), ["junction"])
m.node("west_dock", (15, 12), ["dock"], capacity=8, water_at=[16, 12], dock_min_depth=100, dock_max_depth=2500)
m.node("west_bridgehead", (15, 14), ["junction"])
m.node("east_bridgehead", (17, 14), ["junction"])
m.node("depot", (14, 3), ["evacuation", "dock"], capacity=8, water_at=[16, 3], dock_min_depth=100, dock_max_depth=2500)
m.node("high_landing", (21, 4), ["evacuation", "dock"], capacity=6, water_at=[22, 4], dock_min_depth=100, dock_max_depth=2500)
m.node("fork", (17, 15), ["junction"])
m.node("east_workshop_start", (25, 12), ["assembly"], capacity=6)
m.node("east_workshop_assembly", (25, 11), ["assembly"], capacity=6)
m.node("east_workshop_pickup", (23, 13), ["dock"], capacity=6, water_at=[22, 13], dock_min_depth=100, dock_max_depth=2500)

m.edge("g_wv_start", "west_village_start", "west_village_assembly", 20)
m.edge("g_wv_pickup", "west_village_assembly", "west_village_pickup", 20)
foot = dict(slots=1)
m.edge("g_foot_a", "west_village_pickup", "footpath_bend", 110, sample=[[7, y] for y in range(6, 10)], **foot)
m.edge("g_foot_b", "footpath_bend", "footpath_ford", 130, sample=[[x, 6] for x in range(7, 12)], **foot)
m.edge("g_foot_c", "footpath_ford", "depot", 60, sample=[[11, 6], [12, 6], [12, 5], [12, 4]], **foot)
m.edge("g_high_a", "west_village_assembly", "high_corner_nw", 60, sample=[[5, 12], [4, 12], [3, 12]])
m.edge("g_high_b", "high_corner_nw", "ravine_north", 100, sample=[[3, y] for y in range(8, 13)])
m.edge("g_ravine", "ravine_north", "ravine_south", 40, sample=[[3, 8], [3, 6]], tags=["bridge"])
m.edge("g_high_c", "ravine_south", "high_corner_sw", 100, sample=[[3, y] for y in range(2, 7)])
m.edge("g_high_d", "high_corner_sw", "depot", 280, sample=[[x, 2] for x in range(3, 13)])
m.edge("g_dock_path", "west_village_assembly", "west_dock", 120, sample=[[x, 12] for x in range(10, 15)])
m.edge("g_xriver_a", "west_village_assembly", "west_bridgehead", 160, sample=[[10, 13], [10, 14]] + [[x, 14] for x in range(11, 16)])
m.edge("g_xriver", "west_bridgehead", "east_bridgehead", 40, sample=[[15, 14], [17, 14]], tags=["bridge"])
m.edge("g_strip", "east_bridgehead", "high_landing", 300, sample=[[18, y] for y in range(6, 15)])
m.edge("g_ws_start", "east_workshop_start", "east_workshop_assembly", 20)
m.edge("g_ws_pickup", "east_workshop_assembly", "east_workshop_pickup", 40, sample=[[24, 13], [23, 13]])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.edge("w_west_fork", "west_dock", "fork", 60, sample=[[16, y] for y in range(12, 16)], **water)
m.edge("w_workshop_fork", "east_workshop_pickup", "fork", 100, sample=[[x, 15] for x in range(17, 23)] + [[22, 14], [22, 13]], **water)
m.edge("w_inner", "fork", "depot", 200, sample=[[16, y] for y in range(3, 16)], **water)
m.edge("w_east", "east_workshop_pickup", "high_landing", 400, sample=[[22, y] for y in range(4, 14)], **water)

# ---------------------------------------------------------------- sockets
m.socket("bridge:01", "bridge", edge="g_ravine", at=[3, 7], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_xriver", at=[16, 14], zone="bridges")
m.socket("barrier:01", "barrier", at=[10, 7], links=[[[10, 8], [10, 7]]], zone="lips")
m.socket("barrier:02", "barrier", at=[11, 7], links=[[[11, 8], [11, 7]]], zone="lips")
m.socket("barrier:03", "barrier", at=[12, 8], links=[[[11, 8], [12, 8]]], zone="lips")
m.socket("skiff:01", "skiff", node="depot", at=[16, 3])
m.socket("skiff:02", "skiff", node="high_landing", at=[22, 4])
m.socket("skiff:03", "skiff", node="west_dock", at=[16, 12])
m.socket("beacon:01", "beacon", node="depot", at=[14, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[20, 4])
m.socket("beacon:03", "beacon", node="west_dock", at=[15, 12])

m.groups = [
    {"id": "west_village", "start": "west_village_start", "assembly": "west_village_assembly", "pickup": "west_village_pickup"},
    {"id": "east_workshop", "start": "east_workshop_start", "assembly": "east_workshop_assembly", "pickup": "east_workshop_pickup"},
]
m.roles = {
    "central_basin": {"sources": ["central_in"]},
    "boat_lane_inner": {"edges": ["w_inner"]},
    "east_lane": {"edges": ["w_east"]},
    "west_village": {"groups": ["west_village"]},
    "east_workshop": {"groups": ["east_workshop"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(1, 3))
