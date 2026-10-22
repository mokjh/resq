"""W2-M2 Last Bus from Camp — authored layout (recipe W2-M2-layout-v1; anchors kept).

Story: three camps of six wait at their tents for a crawler (they are held: the camp tracks are crawler tracks,
not footpaths, so only the two crawlers can bring them out). Two road loops meet at the refill station in the
middle of the reserve:
- the west loop runs station -> north camp -> west camp -> depot;
- the east loop runs station -> east camp -> high landing -> depot (the longer loop);
- the short road runs straight from the station down to the depot between them.
At 60 s the woods along the short road ignite. From there fire can reach the east woods only through a narrow
neck; once in the east woods it lights the hedge under the east loop road, threatens the east camp, and creeps
west along that hedge into the junction hedge beside the station. A firebreak on the neck protects the whole
longer loop (and so the station); a firebreak on the junction hedge protects only the station.
"""
from mapkit import Map
from w2_kit import new_map, hline, vline, TANK_VOLUME

m = new_map("W2-M2")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 21, 31, 23)                  # northern ridge
m.rect("v", 4, 12, 8, 15)                   # north camp ground (anchor west 6,13)
m.rect("v", 6, 6, 10, 9)                    # west camp ground (anchor orchard 8,7)
m.rect("v", 24, 12, 28, 15)                 # east camp ground (anchor east 25,13)
m.rect("R", 14, 15, 16, 16)                 # refill station buildings north of the junction
m.rect("T", 16, 6, 17, 11)                  # short-road woods (short_road_fuel), east of the short road
m.rect("T", 19, 6, 23, 12)                  # east woods
m.cell("T", 24, 12)                         # their corner beside the east camp tents
m.line("T", (18, 8), (18, 9))               # neck: the only link from the short-road woods to the east woods
m.line("h", (12, 13), (23, 13))             # junction hedge (x 12..18) and east-loop hedge (x 19..23)
m.line("h", (24, 6), (24, 11))              # hedge beside the east camp's road down to the landing
# roads
m.line("=", (15, 13), (15, 4))              # short road: station junction -> depot
m.line("=", (7, 14), (24, 14))              # loop road through the junction (west to the north camp, east to the east camp)
m.line("=", (6, 12), (6, 10), (8, 10))      # north camp -> west camp
m.line("=", (9, 6), (9, 4), (14, 4))        # west camp -> depot
m.line("=", (25, 12), (25, 5))              # east camp -> high landing
m.line("=", (16, 4), (24, 4))               # high landing -> depot
m.rect("D", 13, 2, 17, 3)
m.rect("H", 25, 3, 28, 5)
m.cell("D", 15, 3)
m.cell("H", 25, 4)

# ---------------------------------------------------------------- regions / fire
m.regions.update({
    "short_road_fuel": {"x0": 16, "y0": 6, "x1": 17, "y1": 11},
    "refill_station_region": [[14, 15], [15, 15], [16, 15], [14, 16], [15, 16], [16, 16], [15, 14]],
    "east_woods": {"x0": 19, "y0": 6, "x1": 23, "y1": 12},
    "junction_hedge": hline(13, 12, 18),
})
m.extra["stores"] = [{"id": "tank", "elev": 1200, "volume": TANK_VOLUME}]

# ---------------------------------------------------------------- navigation
for g, (x, y) in {"north_camp": (6, 13), "west_camp": (8, 7), "east_camp": (25, 13)}.items():
    m.node(f"{g}_start", (x, y), ["assembly"], capacity=6)
m.node("north_camp_assembly", (5, 13), ["assembly"], capacity=6)
m.node("north_camp_pickup", (6, 14), ["assembly"], capacity=6)
m.node("west_camp_assembly", (8, 8), ["assembly"], capacity=6)
m.node("west_camp_pickup", (9, 7), ["assembly"], capacity=6)
m.node("east_camp_assembly", (26, 13), ["assembly"], capacity=6)
m.node("east_camp_pickup", (25, 12), ["assembly"], capacity=6)
m.node("refill_junction", (15, 14), ["junction"])
m.node("depot", (15, 3), ["evacuation"], capacity=12)
m.node("high_landing", (25, 4), ["evacuation"], capacity=6)

for g in ("north_camp", "west_camp", "east_camp"):
    m.edge(f"g_{g}_assembly", f"{g}_start", f"{g}_assembly", 20)
    m.edge(f"g_{g}_pickup", f"{g}_assembly", f"{g}_pickup", 20)
# west loop (650): junction -> north camp -> west camp -> depot
m.edge("g_junction_north", "refill_junction", "north_camp_start", 250, sample=hline(14, 7, 15) + [[6, 14], [6, 13]])
m.edge("g_north_west", "north_camp_start", "west_camp_start", 200, sample=vline(6, 10, 13) + hline(10, 6, 8) + [[8, 9], [8, 8], [8, 7]])
m.edge("g_west_depot", "west_camp_start", "depot", 200, sample=[[9, 7], [9, 6]] + vline(9, 4, 6) + hline(4, 9, 14) + [[15, 3]])
# east loop (750): junction -> east camp -> high landing -> depot
m.edge("g_junction_east", "refill_junction", "east_camp_start", 250, sample=hline(14, 15, 24) + [[25, 14], [25, 13]])
m.edge("g_east_landing", "east_camp_start", "high_landing", 250, sample=vline(25, 4, 13))
m.edge("g_landing_depot", "high_landing", "depot", 250, sample=hline(4, 15, 25) + [[15, 3]])
# short road (300): junction -> depot
m.edge("g_short_road", "refill_junction", "depot", 300, sample=vline(15, 3, 14))

# ---------------------------------------------------------------- sockets
m.socket("crawler:01", "crawler", node="depot", at=[14, 3])
m.socket("crawler:02", "crawler", node="high_landing", at=[26, 4])
m.socket("crawler:03", "crawler", node="depot", at=[16, 3])
m.socket("firebreak:01", "firebreak", at=[18, 9], cells=vline(18, 8, 9), zone="firebreaks")       # the neck
m.socket("firebreak:02", "firebreak", at=[15, 13], cells=hline(13, 16, 18), zone="firebreaks")    # junction hedge
m.socket("firebreak:03", "firebreak", at=[24, 8], cells=vline(24, 6, 11), zone="firebreaks")      # east camp road hedge
m.socket("sprinkler:01", "sprinkler", at=[13, 16], cells=[[14, 15], [15, 15], [16, 15], [15, 14]], store="tank", power="grid", zone="sprinklers")
m.socket("sprinkler:02", "sprinkler", at=[26, 11], cells=[[24, 11], [24, 12], [24, 13]], store="tank", power="grid", zone="sprinklers")
m.socket("generator:01", "generator", at=[12, 17], platform=[[12, 17]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[20, 4], platform=[[20, 5]], power="grid", zone="generators")

m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.infrastructure = [
    {"id": "refill_station", "power": "grid", "demand": 0, "region": "refill_station_region", "max_heat": 300,
     "nodes": ["refill_junction"]},
]
m.groups = [
    {"id": "north_camp", "start": "north_camp_start", "assembly": "north_camp_assembly", "pickup": "north_camp_pickup", "held": True},
    {"id": "west_camp", "start": "west_camp_start", "assembly": "west_camp_assembly", "pickup": "west_camp_pickup", "held": True},
    {"id": "east_camp", "start": "east_camp_start", "assembly": "east_camp_assembly", "pickup": "east_camp_pickup", "held": True},
]
m.routes = []
m.roles.update({
    "short_road_fuel": {"regions": ["short_road_fuel"]},
    "refill_station_region": {"regions": ["refill_station_region"]},
    "refill_station": {"infrastructure": ["refill_station"]},
    "short_forest_route": {"edges": ["g_short_road"]},
    "long_forest_route": {"edges": ["g_junction_east", "g_east_landing", "g_landing_depot"]},
    "tank": {"stores": ["tank"]},
})

# Compact layout: high landing becomes the primary receiving point (recipe receiving.compact_primary). Capacities
# stay with the reference replays (receiving capacity equals the population exactly), so the swap is in staging
# travel: the landing approach shortens and the depot's west approach lengthens.
m.class_overrides = {
    "compact": {
        "edges": [
            {"id": "g_east_landing", "ticks": 200},
            {"id": "g_west_depot", "ticks": 250},
        ],
    },
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(2, 2))
