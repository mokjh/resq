"""W5-M1 A Path Through Heat — authored layout (recipe W5-M1-layout-v1; anchors kept approximately).

Story: the north vent feeds a lava fork at the middle of the board. Its west arm separates the orchard terraces (north
west) from the depot, its east arm separates the research station (north east) from the high landing, and a lower east
branch later cuts the coast road between the two refuges. The terraces sit high above the channels: the stair roads
down to the crossing banks are long, so no one reaches a crossing before the lava does. Every crossing is therefore a
cooled crossing: 50 ticks of cooling, then a 200-tick crust window.
- Short routes: the orchard crosses the west arm at crossing A (cooler:01, locked until the 210 s cooling opportunity);
  the research crew crosses the east arm at crossing B (cooler:02). Both banks are safe ledges.
- Longer route: both groups can walk the ridge paths to the fork causeway, a wide crust road (6 slots). Its west bank
  sits on the hot fork (outside the safe zone): staging there needs the temporary shelter. Cooler:03 cools the fork.
- The coast road ford is on the lower east branch (arrives at 140 s); only the coast bridge (bridge:01, on the bank)
  keeps the high-landing path open; bridge:02 stands in the channel and is destroyed by the arrival.
"""
from w5_kit import W5Map, ROAD, NARROW, rect

m = W5Map("W5-M1")

# ---------------------------------------------------------------- terrain
m.rect(".", 3, 12, 8, 15)           # orchard terraces
m.rect("o", 2, 14, 7, 16)
m.rect("v", 24, 12, 28, 15)         # research station terrace
m.rect("D", 13, 2, 16, 4)           # depot
m.rect("H", 24, 3, 27, 5)           # high landing
m.line("=", (10, 9), (10, 5), (14, 5))        # west causeway road to the depot
m.line("=", (24, 9), (24, 6))                 # east road to the high landing
m.line("=", (15, 9), (15, 5))                 # fork road to the depot
m.line("=", (16, 3), (23, 3))                 # coast road (fords the lower east branch)
m.line("=", (5, 12), (9, 12), (9, 13))        # orchard stair road
m.line("=", (26, 12), (26, 13), (24, 13))     # research stair road
m.line("=", (5, 16), (12, 16), (12, 14))      # orchard ridge path to the fork
m.line("=", (26, 16), (18, 16), (18, 14))     # research ridge path to the fork
m.rect("S", 0, 0, 31, 0)                      # south sea (scenery)

# ---------------------------------------------------------------- lava (authored DAG, spec 02 §10)
m.segment("vent", rect(15, 16, 15, 21), next=[("fork", 50)])
m.rect("l", 14, 22, 16, 22)  # crater (part of the vent segment below)
m.segments[-1]["cells"] = [[15, y] for y in range(16, 23)] + [[14, 22], [16, 22]]
m.segment("fork", rect(14, 10, 16, 15), edges=["g_fork_w", "g_fork_e"], nodes=["fork_shelter"],
          next=[("west_arm", 50), ("east_arm", 50)])
m.segment("west_arm", rect(0, 10, 13, 11), edges=["g_cross_a"])
m.segment("east_arm", rect(17, 10, 31, 11), edges=["g_cross_b"], next=[("east_lower", 600)])
m.segment("east_lower", rect(20, 1, 21, 9), edges=["g_ford"])
m.lava_sources = [{"id": "north_vent", "segment": "vent"}]
m.coolant = [{"id": "coolant", "charges": 2, "capacity": 8}]
m.regions = {"fork_heat": rect(14, 10, 16, 15), "vent_heat": rect(15, 16, 15, 21)}

# ---------------------------------------------------------------- navigation
m.group_nodes("orchard", (5, 14), 6)
m.group_nodes("research", (26, 14), 6)
m.node("orchard_stair", (8, 12), ["junction"], capacity=6)
m.node("a_bank", (10, 12), ["junction"], capacity=6)
m.node("a_south", (10, 9), ["junction"], capacity=6)
m.node("research_stair", (25, 13), ["junction"], capacity=6)
m.node("b_bank", (24, 12), ["junction"], capacity=6)
m.node("b_south", (24, 9), ["junction"], capacity=6)
m.node("orchard_ridge", (8, 16), ["junction"], capacity=6)
m.node("fork_shelter", (13, 13), ["shelter"], capacity=6)
m.node("research_ridge", (22, 16), ["junction"], capacity=6)
m.node("ridge_hut", (21, 15), ["shelter"], capacity=6)
m.node("fork_east", (17, 13), ["junction"], capacity=6)
m.node("fork_south", (15, 9), ["junction"], capacity=12)
m.node("depot", (14, 3), ["evacuation"], capacity=8)        # wide primary receiving point
m.node("ford_west", (19, 3), ["junction"], capacity=6)
m.node("ford_east", (22, 3), ["junction"], capacity=6)
m.node("high_landing", (25, 4), ["evacuation"], capacity=4)  # wide secondary

# short routes (stairs are long: the terraces are high above the channels)
m.edge("g_orchard_stair", "orchard_pickup", "orchard_stair", 400, sample=[[5, 12], [8, 12]], **ROAD)
m.edge("g_orchard_bank", "orchard_stair", "a_bank", 400, sample=[[9, 12], [10, 12]], **ROAD)
m.edge("g_cross_a", "a_bank", "a_south", 40, sample=[[10, 11], [10, 10]], tags=["crossing"], **NARROW)
m.edge("g_a_depot", "a_south", "depot", 40, sample=[[10, 5], [14, 5]], **ROAD)
m.edge("g_research_stair", "research_pickup", "research_stair", 500, sample=[[26, 12], [25, 13]], **ROAD)
m.edge("g_research_bank", "research_stair", "b_bank", 560, sample=[[24, 13], [24, 12]], **ROAD)
m.edge("g_cross_b", "b_bank", "b_south", 40, sample=[[24, 11], [24, 10]], tags=["crossing"], **NARROW)
m.edge("g_b_landing", "b_south", "high_landing", 40, sample=[[24, 6], [25, 4]], **ROAD)
# longer route: ridge paths to the fork causeway (a wide crust road)
m.edge("g_orchard_ridge", "orchard_pickup", "orchard_ridge", 450, sample=[[5, 16], [8, 16]], **ROAD)
m.edge("g_orchard_fork", "orchard_ridge", "fork_shelter", 450, sample=[[12, 16], [13, 13]], **ROAD)
m.edge("g_research_ridge", "research_pickup", "research_ridge", 500, sample=[[26, 16], [22, 16]], **ROAD)
m.edge("g_research_fork", "research_ridge", "fork_east", 500, sample=[[18, 16], [17, 13]], **ROAD)
m.edge("g_ridge_hut", "research_ridge", "ridge_hut", 20, **ROAD)
m.edge("g_fork_w", "fork_shelter", "fork_south", 60, sample=[[14, 12], [15, 10]], tags=["crossing"], **ROAD)
m.edge("g_fork_e", "fork_east", "fork_south", 60, sample=[[16, 12], [15, 10]], tags=["crossing"], **ROAD)
m.edge("g_fork_depot", "fork_south", "depot", 40, sample=[[15, 9], [15, 5]], **ROAD)
# coast road between the refuges: a ford on the lower east branch, or a bridge
m.edge("g_coast_w", "depot", "ford_west", 60, sample=[[16, 3], [19, 3]], **ROAD)
m.edge("g_ford", "ford_west", "ford_east", 40, sample=[[20, 3], [21, 3]], **ROAD)
m.edge("g_coast_bridge", "ford_west", "ford_east", 40, sample=[[19, 4], [22, 4]], tags=["bridge"], **ROAD)
m.edge("g_channel_bridge", "ford_west", "ford_east", 40, sample=[[20, 2], [21, 2]], tags=["bridge"], **ROAD)
m.edge("g_coast_e", "ford_east", "high_landing", 60, sample=[[22, 3], [25, 4]], **ROAD)

# ---------------------------------------------------------------- sockets
m.socket("cooler:01", "cooler", at=[9, 11], segment="west_arm", enabled=False, zone="coolers")       # crossing A
m.socket("cooler:02", "cooler", at=[23, 12], segment="east_arm", coolant_store="coolant", zone="coolers")  # crossing B
m.socket("cooler:03", "cooler", at=[17, 14], segment="fork", coolant_store="coolant", zone="coolers")  # fork causeway
m.socket("bridge:01", "bridge", edge="g_coast_bridge", at=[19, 4], zone="bridges")
m.socket("bridge:02", "bridge", edge="g_channel_bridge", at=[20, 2], zone="bridges")   # stands in the lower channel
m.socket("shelter:01", "shelter", node="fork_shelter", at=[13, 14], region="fork_heat", zone="shelters")
m.socket("shelter:02", "shelter", node="ridge_hut", at=[21, 16], region="vent_heat", zone="shelters")
m.socket("beacon:01", "beacon", node="depot", at=[13, 3])
m.socket("beacon:02", "beacon", node="high_landing", at=[26, 4])
m.socket("beacon:03", "beacon", node="a_bank", at=[11, 12])
m.socket("beacon:04", "beacon", node="b_bank", at=[23, 13])

m.groups = [
    {"id": "orchard", "start": "orchard_start", "assembly": "orchard_assembly", "pickup": "orchard_pickup"},
    {"id": "research", "start": "research_start", "assembly": "research_assembly", "pickup": "research_pickup"},
]
# compact layout: the high landing becomes the primary receiving point (recipe receiving swap)
m.class_overrides = {"compact": {"nodes": [{"id": "depot", "capacity": 4}, {"id": "high_landing", "capacity": 8}]}}
m.routes = [{"id": "high_landing_path", "from": "depot", "to": "high_landing", "class": "ground"}]
m.roles = {
    "first_channel": {"lava_sources": ["north_vent"]},
    "crossing_a": {"cooler_sockets": ["cooler:01"]},
    "high_landing_path": {"edges": ["g_ford", "g_coast_bridge"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(5, 1))
