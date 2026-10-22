"""W5-M4 Three Ways Out — authored layout (recipe W5-M4-layout-v1; anchors kept approximately).

Story: the land channel runs north-south down the west of the board and turns Hot at 200 s. West of it live the
research crew (north) and the inland orchard (south); east of it are the depot and the high landing; the coast village
sits on the east shore. Three ways out:
- Road: the research crew's short land route crosses a ravine (needs the bridge) and then the land channel at crossing A.
  Without the bridge the crew must go round the ravine and arrives after the lava.
- Sea: the coast village leaves only by skiff from its two-berth dock, round the reef to the outer buoy and on to
  either landing (buoys are open water: no berth). The inland orchard has a tiny one-berth pier (4 people) with a
  shelter beside it, but the pier's channel to the south buoy (off the depot) is blocked by debris until 330 s.
- Crawler and cooling: the inland orchard's long track reaches crossing B after the lava; crossing B's cooler sockets
  are locked until the final cooling window at 440 s. Crossing B is a wide crust road (4 slots).
- 300 s: the inland lobe makes the orchard terraces and the pier shelter unsafe.
- 384 s: a southern lobe of the land channel reaches the high landing, which then closes for good (published with
  the 190 s warning, so every capacity panel shows its closing time).
- The landings and the pier have one berth each (a finished skiff moors aside and frees its berth).
"""
from w5_kit import W5Map, ROAD, NARROW, rect

m = W5Map("W5-M4")

# ---------------------------------------------------------------- terrain
m.rect("S", 0, 0, 31, 1)                       # south sea
m.rect("S", 30, 2, 31, 15)                     # east sea
m.rect("v", 3, 13, 8, 16)                      # research station terraces
m.rect("o", 4, 5, 9, 9)                        # inland orchard
m.rect("q", 7, 2, 9, 3)                        # tiny pier
m.rect("D", 14, 2, 17, 4)                      # depot
m.rect("H", 24, 2, 27, 4)                      # high landing
m.rect("v", 25, 12, 29, 15)                    # coast village
m.line("=", (5, 12), (9, 12), (9, 11), (15, 11), (15, 5))   # research short route
m.line("=", (7, 6), (7, 10), (10, 10), (10, 7), (14, 7))    # inland track to crossing B

# ---------------------------------------------------------------- lava (role land_channel)
m.segment("land", rect(11, 2, 12, 20), edges=["g_cross_a", "g_cross_b"], next=[("inland_lobe", 1000), ("harbour_lobe", 1840)])
m.segment("inland_lobe", rect(2, 10, 6, 11),
          nodes=["inland_start", "inland_assembly", "inland_pickup", "pier_shelter"])
m.segment("harbour_lobe", rect(22, 5, 23, 6), nodes=["high_landing"])   # reaches the high landing at 384 s
m.lava_sources = [{"id": "land_vent", "segment": "land"}]
m.coolant = [{"id": "coolant", "charges": 2, "capacity": 8}]

# ---------------------------------------------------------------- navigation
m.group_nodes("research_crew", (5, 14), 8)
m.group_nodes("coast", (27, 14), 8)
m.group_nodes("inland", (7, 8), 8)
dock = dict(dock_min_depth=100, dock_max_depth=2500)
m.node("ravine_w", (8, 12), ["junction"], capacity=8)
m.node("ravine_e", (10, 12), ["junction"], capacity=8)
m.node("a_bank", (10, 11), ["junction"], capacity=8)
m.node("a_east", (13, 11), ["junction"], capacity=8)
m.node("b_track", (7, 10), ["junction"], capacity=8)
m.node("b_bank", (10, 7), ["junction"], capacity=8)
m.node("b_east", (13, 7), ["junction"], capacity=8)
m.node("pier_shelter", (8, 4), ["shelter"], capacity=6)
m.node("pier", (8, 3), ["dock"], capacity=4, water_at=[8, 1], **dock)
m.node("coast_dock", (29, 12), ["dock"], capacity=8, berths=2, water_at=[30, 12], **dock)
m.node("depot", (15, 3), ["evacuation", "dock"], capacity=16, water_at=[15, 1], **dock)
m.node("high_landing", (25, 4), ["evacuation", "dock"], capacity=8, water_at=[25, 1], **dock)

# research crew: short route over the ravine bridge, or round the ravine
m.edge("g_research_ravine", "research_crew_pickup", "ravine_w", 400, sample=[[5, 12], [8, 12]], **ROAD)
m.edge("g_ravine_bridge", "ravine_w", "ravine_e", 40, sample=[[9, 12]], tags=["bridge"], **ROAD)
m.edge("g_ravine_bridge2", "ravine_w", "ravine_e", 60, sample=[[9, 13]], tags=["bridge"], **ROAD)
m.edge("g_ravine_round", "ravine_w", "ravine_e", 1600, sample=[[8, 13], [8, 16], [10, 16], [10, 13]], **ROAD)
m.edge("g_ravine_bank", "ravine_e", "a_bank", 400, sample=[[10, 12], [10, 11]], **ROAD)
m.edge("g_cross_a", "a_bank", "a_east", 60, sample=[[11, 11], [12, 11]], tags=["crossing"], **NARROW)
m.edge("g_a_depot", "a_east", "depot", 150, sample=[[15, 11], [15, 5]], **ROAD)
# inland: long track to crossing B, or the pier
m.edge("g_inland_track", "inland_pickup", "b_track", 1000, sample=[[7, 9], [7, 10]], **ROAD)
m.edge("g_track_bank", "b_track", "b_bank", 1000, sample=[[10, 10], [10, 7]], **ROAD)
m.edge("g_cross_b", "b_bank", "b_east", 100, sample=[[11, 7], [12, 7]], tags=["crossing"], slots=4)
m.edge("g_b_depot", "b_east", "depot", 100, sample=[[14, 7], [15, 4]], **ROAD)
m.edge("g_pier_shelter", "inland_pickup", "pier_shelter", 60, **ROAD)
m.edge("g_pier", "pier_shelter", "pier", 20, **ROAD)
# coast: walk down to the dock
m.edge("g_coast_dock", "coast_pickup", "coast_dock", 40, **ROAD)
# water
water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.node("outer_buoy", (29, 1), ["junction"], capacity=8)       # open water off the reef (no berth)
m.edge("w_outer", "coast_dock", "outer_buoy", 500, sample=[[30, y] for y in range(2, 13)], **water)
m.edge("w_buoy_landing", "outer_buoy", "high_landing", 100, sample=[[x, 1] for x in range(25, 30)], **water)
m.node("south_buoy", (14, 1), ["junction"], capacity=8)       # open water off the depot (no berth)
m.edge("w_buoy_south", "outer_buoy", "south_buoy", 230, sample=[[x, 1] for x in range(14, 30)], **water)
m.edge("w_depot_buoy", "south_buoy", "depot", 20, sample=[[14, 1], [15, 1]], **water)
m.edge("w_landing_buoy", "high_landing", "south_buoy", 180, sample=[[x, 1] for x in range(14, 26)], **water)
m.edge("w_pier_channel", "pier", "south_buoy", 100, sample=[[x, 1] for x in range(8, 15)], enabled=False, **water)

# ---------------------------------------------------------------- sockets
m.socket("bridge:01", "bridge", edge="g_ravine_bridge", at=[9, 12], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_ravine_bridge2", at=[9, 13], zone="bridges")
m.socket("cooler:01", "cooler", at=[10, 13], segment="land", coolant_store="coolant", zone="coolers")   # crossing A
m.socket("cooler:02", "cooler", at=[10, 6], segment="land", enabled=False, zone="coolers")           # crossing B
m.socket("cooler:03", "cooler", at=[13, 6], segment="land", enabled=False, zone="coolers")           # crossing B
m.socket("shelter:01", "shelter", node="pier_shelter", at=[9, 4], zone="shelters")
m.socket("shelter:02", "shelter", node="b_shelter", at=[9, 8], zone="shelters")
m.node("b_shelter", (9, 7), ["shelter"], capacity=6)
m.edge("g_b_shelter", "b_bank", "b_shelter", 20, **ROAD)
m.socket("crawler:01", "crawler", node="inland_pickup", at=[8, 6])
m.socket("crawler:02", "crawler", node="depot", at=[16, 4])
m.socket("skiff:01", "skiff", node="depot", at=[15, 1])
m.socket("skiff:02", "skiff", node="high_landing", at=[25, 1])
m.socket("skiff:03", "skiff", node="coast_dock", at=[30, 12])

# compact layout: the high landing becomes the primary receiving point (recipe receiving swap)
m.class_overrides = {"compact": {"nodes": [{"id": "depot", "capacity": 8}, {"id": "high_landing", "capacity": 16}]}}
m.groups = [
    {"id": "research_crew", "start": "research_crew_start", "assembly": "research_crew_assembly", "pickup": "research_crew_pickup"},
    {"id": "coast", "start": "coast_start", "assembly": "coast_assembly", "pickup": "coast_pickup"},
    {"id": "inland", "start": "inland_start", "assembly": "inland_assembly", "pickup": "inland_pickup"},
]
m.roles = {
    "short_land_route": {"edges": ["g_cross_a"]},
    "land_channel": {"lava_sources": ["land_vent"]},
    "sea_route": {"edges": ["w_pier_channel"]},
    "crossing_b": {"cooler_sockets": ["cooler:02", "cooler:03"]},
    "research_crew": {"groups": ["research_crew"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(5, 4))
