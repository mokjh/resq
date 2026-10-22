"""W5-M2 Borrowed Safety — authored layout (recipe W5-M2-layout-v1; anchors kept approximately).

Story: one lava arm runs east-west across the board and cuts both villages off from the refuges. Its single crossing
is a narrow crust stair (4 movement slots, 190 ticks): a crust window lets four walkers start across, or the crawler
(two slots, six seats) plus two walkers. The crossing turns Hot at 60 s, before anyone can walk down to it, so every
crossing is a cooled crossing, and a second window needs the same segment to reheat first (300 ticks after cooling).
- Heat advances on the orchard first: its terraces turn unsafe at 70 s. The village terraces follow at 100 s.
- The north bank is a tiny safe ledge (3 people). Beside it stand two temporary shelter sites: the orchard shelter
  (shelter_a, safe ground until its external heat front starts at 180 s) and the village shelter (inside the village
  heat zone, hazardous from 100 s).
- The quarry hut (6 places) and its track, behind the village, are far from any heat; the hut is a third shelter
  site. Residents can wait there safely, but a shelter is never a refuge: they still have to leave by the outer route
  (round the east headland), which a fallen crust ridge keeps closed until 320 s.
- Coolers refill from the coolant store (2 charges): with two coolers there are four crust windows in total.
"""
from w5_kit import W5Map, ROAD, rect

m = W5Map("W5-M2")

# ---------------------------------------------------------------- terrain
m.rect("o", 2, 13, 8, 16)           # orchard terraces
m.rect("v", 24, 13, 30, 16)         # village terraces
m.rect("D", 13, 2, 16, 4)           # depot
m.rect("H", 24, 3, 27, 5)           # high landing
m.line("=", (5, 12), (11, 12), (11, 13), (14, 13))    # orchard road to the north bank
m.line("=", (26, 12), (19, 12), (19, 13), (16, 13))   # village road to the north bank
m.line("=", (15, 9), (15, 5))                         # south bank road to the depot
m.line("=", (16, 3), (23, 3))                         # coast road between the refuges
m.line("=", (28, 17), (31, 17), (31, 5), (27, 5))     # outer route round the east headland
m.rect("S", 0, 0, 31, 0)
m.rect("l", 15, 12, 15, 21)                           # the vent stem (scenery; the arm is the active flow)

# ---------------------------------------------------------------- lava (role assembly_channel)
m.segment("arm", rect(0, 10, 31, 11), edges=["g_cross"], next=[("orchard_lobe", 100), ("village_lobe", 400)])
m.segment("orchard_lobe", rect(2, 17, 8, 18), nodes=["orchard_start", "orchard_assembly", "orchard_pickup"])
m.segment("village_lobe", rect(23, 17, 27, 18), nodes=["village_start", "village_assembly", "village_pickup", "e_shelter"])
m.lava_sources = [{"id": "assembly_vent", "segment": "arm"}]
m.coolant = [{"id": "coolant", "charges": 2, "capacity": 8}]
m.regions = {"village_heat": rect(23, 17, 27, 18)}

# ---------------------------------------------------------------- navigation
m.group_nodes("orchard", (5, 14), 9)
m.group_nodes("village", (26, 14), 9)
m.node("w_shelter", (13, 13), ["shelter"], capacity=6)
m.node("n_bank", (15, 13), ["junction"], capacity=3)
m.node("e_shelter", (17, 13), ["shelter"], capacity=6)
m.node("s_bank", (15, 9), ["junction"], capacity=9)
m.node("quarry_track", (27, 16), ["junction"], capacity=9)
m.node("quarry", (28, 16), ["shelter"], capacity=6)
m.node("outer_gate", (31, 12), ["junction"], capacity=9)
m.node("depot", (14, 3), ["evacuation"], capacity=12)        # wide primary receiving point
m.node("high_landing", (25, 4), ["evacuation"], capacity=6)  # wide secondary

m.edge("g_orchard_road", "orchard_pickup", "w_shelter", 580, sample=[[5, 12], [11, 13]], **ROAD)
m.edge("g_w_bank", "w_shelter", "n_bank", 20, **ROAD)
m.edge("g_village_road", "village_pickup", "e_shelter", 580, sample=[[26, 12], [19, 13]], **ROAD)
m.edge("g_e_bank", "e_shelter", "n_bank", 20, **ROAD)
m.edge("g_cross", "n_bank", "s_bank", 190, sample=[[15, 11], [15, 10]], tags=["crossing"], slots=4)
m.edge("g_s_depot", "s_bank", "depot", 60, sample=[[15, 5], [15, 4]], **ROAD)
m.edge("g_coast", "depot", "high_landing", 200, sample=[[16, 3], [23, 3]], **ROAD)
m.edge("g_quarry_track", "village_start", "quarry_track", 30, **ROAD)
m.edge("g_quarry", "quarry_track", "quarry", 10, **ROAD)
m.edge("g_outer_a", "quarry", "outer_gate", 300, sample=[[28, 17], [31, 17], [31, 12]], enabled=False, **ROAD)
m.edge("g_outer_b", "outer_gate", "high_landing", 300, sample=[[31, 11], [31, 5], [27, 5]], **ROAD)

# ---------------------------------------------------------------- sockets
m.socket("cooler:01", "cooler", at=[14, 12], segment="arm", coolant_store="coolant", zone="coolers")
m.socket("cooler:02", "cooler", at=[16, 9], segment="arm", coolant_store="coolant", zone="coolers")
m.socket("cooler:03", "cooler", at=[14, 9], segment="arm", coolant_store="coolant", zone="coolers")
m.socket("shelter:01", "shelter", node="w_shelter", at=[13, 14], zone="shelters")
m.socket("shelter:02", "shelter", node="e_shelter", at=[17, 14], region="village_heat", zone="shelters")
m.socket("shelter:03", "shelter", node="quarry", at=[29, 16], zone="shelters")
m.socket("crawler:01", "crawler", node="depot", at=[15, 4])
m.socket("crawler:02", "crawler", node="s_bank", at=[16, 8])
for i, (node, at) in enumerate([("depot", [13, 3]), ("high_landing", [24, 4]), ("n_bank", [15, 14]),
                                ("s_bank", [14, 8]), ("quarry", [28, 15])], 1):
    m.socket(f"beacon:{i:02d}", "beacon", node=node, at=at)

# compact layout: the high landing becomes the primary receiving point (recipe receiving swap)
m.class_overrides = {"compact": {"nodes": [{"id": "depot", "capacity": 6}, {"id": "high_landing", "capacity": 12}]}}
m.groups = [
    {"id": "orchard", "start": "orchard_start", "assembly": "orchard_assembly", "pickup": "orchard_pickup"},
    {"id": "village", "start": "village_start", "assembly": "village_assembly", "pickup": "village_pickup"},
]
m.roles = {
    "assembly_channel": {"lava_sources": ["assembly_vent"]},
    "shelter_a": {"shelters": ["shelter:01"]},
    "outer_route": {"edges": ["g_outer_a"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(5, 2))
