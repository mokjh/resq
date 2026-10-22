"""W2-M4 When the Wind Turns — authored layout (recipe W2-M4-layout-v1; anchors kept).

Story: a stand of pines in the middle of the reserve is already burning when the mission starts (fire.burning;
the contract carries only wind events). From it a hedge runs west and east along the ridge (y 12) and then down
beside each flank road: the lookout's road (x 6) on the west, the east camp's road (x 25) on the east. Along the
south, the safe loop (y 4) joins the depot and the high landing; the east hedge continues west above it (y 5).
A one-tree hedge crawls about 130 pulses per tree downwind, 300 upwind and 150 across the wind, so the wind
timeline decides which flank is in danger:
- calm until 70 s, then westward: the west hedge races to the lookout (about S=1000) and down the lookout road;
  the east hedge creeps against the wind;
- 240 s (warned at 200 s): eastward: the east hedge reaches the east camp soon after the turn and runs down its road;
- 360 s: westward again: the hedge above the safe loop runs west, closing the loop for the rest of the mission.
A firebreak where each hedge leaves the burning stand protects that whole flank (firebreak:01 west, :02 east).
The lookout tower group must all be evacuated; the 3-star mastery allows at most two firebreak deployments.
"""
from mapkit import Map
from w2_kit import new_map, hline, vline, TANK_VOLUME

m = new_map("W2-M4")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 21, 31, 23)                  # ridge crest
m.rect("Y", 13, 12, 14, 13)                 # the pine stand, burning at S=0
m.line("h", (7, 12), (12, 12))              # west ridge hedge
m.line("h", (7, 11), (7, 5))                # ... and down beside the lookout road
m.line("h", (15, 12), (24, 12))             # east ridge hedge
m.line("h", (24, 11), (24, 6))              # ... and down beside the east camp road
m.line("h", (23, 5), (16, 5))               # ... and west above the safe loop
m.rect("k", 3, 11, 5, 15)                   # lookout knoll (anchor west 6,13)
m.rect("k", 6, 13, 6, 15)
m.rect("v", 8, 6, 10, 8)                    # west camp (anchor orchard 8,7)
m.rect("v", 26, 11, 28, 14)                 # east camp (anchor east 25,13)
m.cell("v", 25, 13); m.cell("v", 25, 12)
# roads
m.line("=", (6, 11), (6, 4), (14, 4))       # lookout road down to the depot
m.line("=", (8, 5), (8, 4))                 # west camp lane
m.line("=", (25, 11), (25, 5))              # east camp road to the high landing
m.line("=", (16, 4), (24, 4))               # the safe loop (depot - high landing)
m.rect("D", 13, 2, 17, 3)
m.rect("H", 24, 3, 27, 4)
m.cell("D", 15, 3)
m.cell("H", 25, 4)
m.cell("=", 16, 4); m.cell("=", 24, 4)

# ---------------------------------------------------------------- regions / fire
m.extra["fire"]["burning"] = {"x0": 13, "y0": 12, "x1": 14, "y1": 13}
m.regions.update({
    "pine_stand": {"x0": 13, "y0": 12, "x1": 14, "y1": 13},
    "west_flank": hline(12, 7, 12) + vline(7, 5, 11),
    "east_flank": hline(12, 15, 24) + vline(24, 6, 11),
    "safe_loop_hedge": hline(5, 16, 23),
})
m.extra["stores"] = [{"id": "tank", "elev": 1200, "volume": TANK_VOLUME}]

# ---------------------------------------------------------------- navigation
m.node("lookout_start", (6, 13), ["assembly"], capacity=8)
m.node("lookout_assembly", (6, 12), ["assembly"], capacity=8)
m.node("lookout_pickup", (6, 11), ["assembly"], capacity=8)
m.node("west_camp_start", (8, 7), ["assembly"], capacity=6)
m.node("west_camp_assembly", (8, 6), ["assembly"], capacity=6)
m.node("west_camp_pickup", (8, 5), ["assembly"], capacity=6)
m.node("east_camp_start", (25, 13), ["assembly"], capacity=6)
m.node("east_camp_assembly", (25, 12), ["assembly"], capacity=6)
m.node("east_camp_pickup", (25, 11), ["assembly"], capacity=6)
m.node("lookout_road_foot", (6, 4), ["junction"])
m.node("west_junction", (8, 4), ["junction"])
m.node("depot", (15, 3), ["evacuation"], capacity=14)
m.node("high_landing", (25, 4), ["evacuation"], capacity=6)

for g in ("lookout", "west_camp", "east_camp"):
    m.edge(f"g_{g}_assembly", f"{g}_start", f"{g}_assembly", 20)
    m.edge(f"g_{g}_pickup", f"{g}_assembly", f"{g}_pickup", 20)
m.edge("g_lookout_road", "lookout_pickup", "lookout_road_foot", 300, sample=vline(6, 4, 11))
m.edge("g_lookout_foot", "lookout_road_foot", "west_junction", 50, sample=hline(4, 6, 8))
m.edge("g_west_lane", "west_camp_pickup", "west_junction", 20, sample=[[8, 5], [8, 4]])
m.edge("g_west_depot", "west_junction", "depot", 250, sample=hline(4, 8, 14) + [[15, 3]])
m.edge("g_east_road", "east_camp_pickup", "high_landing", 300, sample=vline(25, 4, 11))
m.edge("g_safe_loop", "depot", "high_landing", 300, sample=[[15, 3]] + hline(4, 16, 25))

# ---------------------------------------------------------------- sockets
m.socket("firebreak:01", "firebreak", at=[12, 11], cells=hline(12, 11, 12), zone="firebreaks")   # west hedge at the stand
m.socket("firebreak:02", "firebreak", at=[15, 11], cells=hline(12, 15, 16), zone="firebreaks")   # east hedge at the stand
m.socket("firebreak:03", "firebreak", at=[23, 11], cells=[[24, 12], [24, 11], [23, 12]], zone="firebreaks")  # east corner
m.socket("firebreak:04", "firebreak", at=[23, 6], cells=[[24, 6], [23, 5]], zone="firebreaks")   # safe-loop hedge corner
m.socket("sprinkler:01", "sprinkler", at=[5, 10], cells=[[7, 11], [7, 12]], store="tank", power="tank_pump", zone="sprinklers")
m.socket("sprinkler:02", "sprinkler", at=[26, 10], cells=[[24, 11], [24, 12]], store="tank", power="tank_pump", zone="sprinklers")
m.socket("crawler:01", "crawler", node="depot", at=[14, 3])
m.socket("crawler:02", "crawler", node="high_landing", at=[26, 4])
m.socket("crawler:03", "crawler", node="west_junction", at=[9, 3])
m.socket("beacon:01", "beacon", node="depot", at=[16, 2])
m.socket("beacon:02", "beacon", node="high_landing", at=[27, 3])
m.socket("beacon:03", "beacon", node="west_junction", at=[9, 2])

# the fixed powered tank: its pump house supplies one sprinkler (recipe fixed source, no generator in the kit)
m.power = {"nodes": [{"id": "tank_pump", "fixed_supply": 1}], "links": []}
m.groups = [
    {"id": "lookout", "start": "lookout_start", "assembly": "lookout_assembly", "pickup": "lookout_pickup"},
    {"id": "west_camp", "start": "west_camp_start", "assembly": "west_camp_assembly", "pickup": "west_camp_pickup"},
    {"id": "east_camp", "start": "east_camp_start", "assembly": "east_camp_assembly", "pickup": "east_camp_pickup"},
]
m.routes = [{"id": "safe_loop", "from": "depot", "to": "high_landing", "class": "ground"}]
m.roles.update({
    "lookout": {"groups": ["lookout"]},
    "firebreak": {"regions": ["west_flank", "east_flank"]},
    "long_forest_route": {"edges": ["g_safe_loop"]},
    "tank": {"stores": ["tank"]},
})

# Compact layout: the high landing becomes the primary receiving point (recipe receiving.compact_primary). Capacities
# stay with the reference replays (receiving equals the population), so the swap is in staging travel.
m.class_overrides = {
    "compact": {
        "edges": [
            {"id": "g_east_road", "ticks": 250},
            {"id": "g_west_depot", "ticks": 300},
        ],
    },
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(2, 4))
