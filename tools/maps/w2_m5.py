"""W2-M5 The Green Corridor — authored layout (recipe W2-M5-layout-v1; anchors kept).

Story: the green corridor is the reserve's east-west road (y 11) from the campsite (west) to the orchard (east),
lined on its north side by hedges. North of it the great north forest burns from its outer (west) edge at 50 s and
the front rolls east for the rest of the mission. The forest reaches the corridor hedges only through two tongues:
- the west tongue (x 11..12): a forest strip plus a wooden cabin, which no firebreak can clear, so it must be
  wetted; it lights the short west hedge (x 7..12) at about S=1700, which burns past the campsite;
- the east tongue (x 23..24): forest only; the front reaches it at about S=3000, just before the wind turns east,
  and the long east hedge (x 14..24) then creeps west against the wind for the rest of the mission.
The orchard pump house stands on the irrigation line beside the corridor, where the east hedge's fire passes late.
The sawmill (south-west) is threatened by the sawmill woods through a single line of shade trees that reaches the
sheds beside the crew's yard; the 170 s heat event lands on that spur and those sheds.
The campsite walks out along the corridor. The sawmill crew and the orchard crew stay at their posts until a crawler
collects them (held groups). The orchard also has one long safe road down to the high landing.
"""
from mapkit import Map
from w2_kit import new_map, hline, vline, TANK_VOLUME, POND_VOLUME

m = new_map("W2-M5")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 21, 31, 23)                  # ridge
m.rect("T", 1, 15, 24, 20)                  # great north forest (outer edge x 1..2 ignites)
m.rect("o", 25, 15, 28, 18)                 # orchard trees at its east end
m.rect("P", 18, 13, 19, 13)                 # orchard pump house, on the irrigation line beside the corridor
m.rect("c", 3, 10, 7, 14)                   # campsite clearing
m.cell("b", 4, 12)                          # campsite lodge (decorative: no burning fuel can reach it)
for xy in ((3, 12), (3, 13)):
    m.cell("T", *xy)                        # two ornamental trees by the lodge
m.rect("v", 25, 12, 26, 14)                 # orchard yard
# the two tongues from the forest down to the corridor hedges
m.line("b", (11, 13), (11, 14))             # west tongue: wooden cabin (building, fuel)
m.line("T", (12, 13), (12, 14))             #              and a forest strip
m.rect("T", 23, 13, 24, 14)                 # east tongue
m.line("h", (7, 12), (12, 12))              # west hedge
m.line("h", (14, 12), (24, 12))             # east hedge (x 13 is a gap)
# sawmill corner
m.rect("T", 1, 1, 5, 3)                     # sawmill woods
for xy in ((5, 4), (5, 5), (5, 6), (4, 7), (4, 8), (5, 9)):
    m.cell("T", *xy)                        # a single line of shade trees from the woods to the sheds
m.cell("b", 6, 9)                           # sawmill sheds (fuel), the last one beside the crew's yard
m.cell("b", 7, 8)
# roads
m.line("=", (6, 11), (25, 11))              # the green corridor
m.line("=", (15, 10), (15, 4))              # depot road
m.line("=", (8, 5), (14, 5))                # sawmill spur
m.line("=", (25, 10), (25, 5))              # orchard's long safe road to the high landing
m.line("=", (16, 3), (24, 3))               # landing - depot road
m.rect("D", 13, 2, 17, 3)
m.rect("H", 24, 3, 27, 4)
m.cell("D", 15, 3)
m.cell("H", 25, 4)

# ---------------------------------------------------------------- regions / fire
m.regions.update({
    "outer_forest": {"x0": 1, "y0": 15, "x1": 2, "y1": 20},
    "sawmill_outer": {"x0": 1, "y0": 1, "x1": 2, "y1": 3},
    "sawmill_access": hline(5, 8, 14) + [[6, 9], [7, 8], [5, 9]],
    "corridor_flank": hline(12, 7, 12) + hline(12, 14, 24),
    "corridor": hline(11, 6, 25),
    "orchard_pump_house": "P",
})
m.extra["stores"] = [
    {"id": "tank", "elev": 1200, "volume": TANK_VOLUME},
    {"id": "pond", "elev": 1200, "volume": POND_VOLUME},
]

# ---------------------------------------------------------------- navigation
m.node("campsite_start", (6, 13), ["assembly"], capacity=8)
m.node("campsite_assembly", (6, 12), ["assembly"], capacity=8)
m.node("campsite_pickup", (6, 11), ["assembly"], capacity=8)
m.node("orchard_start", (25, 13), ["assembly"], capacity=8)
m.node("orchard_assembly", (25, 12), ["assembly"], capacity=8)
m.node("orchard_pickup", (25, 11), ["assembly"], capacity=8)
m.node("sawmill_start", (8, 7), ["assembly"], capacity=8)
m.node("sawmill_assembly", (8, 6), ["assembly"], capacity=8)
m.node("sawmill_pickup", (8, 5), ["assembly"], capacity=8)
m.node("corridor_mid", (15, 11), ["junction"])
m.node("sawmill_junction", (15, 5), ["junction"])
m.node("depot", (15, 3), ["evacuation"], capacity=16)
m.node("high_landing", (25, 4), ["evacuation"], capacity=8)

for g in ("campsite", "orchard", "sawmill"):
    m.edge(f"g_{g}_assembly", f"{g}_start", f"{g}_assembly", 20)
    m.edge(f"g_{g}_pickup", f"{g}_assembly", f"{g}_pickup", 20)
m.edge("g_corridor_w", "campsite_pickup", "corridor_mid", 300, sample=hline(11, 6, 15))
m.edge("g_corridor_e", "corridor_mid", "orchard_pickup", 300, sample=hline(11, 15, 25))
m.edge("g_depot_road", "corridor_mid", "sawmill_junction", 200, sample=vline(15, 5, 10))
m.edge("g_depot_gate", "sawmill_junction", "depot", 50, sample=vline(15, 3, 5))
m.edge("g_spur", "sawmill_pickup", "sawmill_junction", 150, sample=hline(5, 8, 15))
m.edge("g_orchard_safe", "orchard_pickup", "high_landing", 600, sample=vline(25, 4, 10))
m.edge("g_landing_depot", "high_landing", "depot", 250, sample=hline(3, 15, 25))

# ---------------------------------------------------------------- sockets
m.socket("crawler:01", "crawler", node="depot", at=[14, 3])
m.socket("crawler:02", "crawler", node="high_landing", at=[26, 4])
m.socket("crawler:03", "crawler", node="corridor_mid", at=[16, 10])
m.socket("firebreak:01", "firebreak", at=[12, 13], cells=vline(12, 13, 14), zone="firebreaks")        # west tongue strip
m.socket("firebreak:02", "firebreak", at=[23, 13], cells=[[23, 13], [24, 13], [23, 14], [24, 14]], zone="firebreaks")  # east tongue
m.socket("firebreak:03", "firebreak", at=[27, 15], cells=hline(15, 25, 28), zone="firebreaks")       # orchard trees by the pump house
m.socket("firebreak:04", "firebreak", at=[4, 13], cells=[[3, 12], [3, 13]], zone="firebreaks")  # lodge trees (decorative)
m.socket("sprinkler:01", "sprinkler", at=[10, 13], cells=vline(11, 13, 14), store="tank", power="grid", zone="sprinklers")  # west tongue cabin
m.socket("sprinkler:02", "sprinkler", at=[22, 9], cells=[[18, 13], [19, 13], [18, 12], [19, 12]], store="pond", power="grid", zone="sprinklers")  # pump house bend
m.socket("sprinkler:03", "sprinkler", at=[4, 10], cells=[[4, 12]], store="tank", power="grid", zone="sprinklers")  # campsite lodge (decorative)
m.socket("generator:01", "generator", at=[17, 8], platform=[[17, 8]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[13, 13], platform=[[13, 13]], power="grid", zone="generators")

m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.infrastructure = [
    {"id": "orchard_pump_house", "power": "grid", "demand": 0, "region": "orchard_pump_house", "max_heat": 300},
]
m.groups = [
    {"id": "campsite", "start": "campsite_start", "assembly": "campsite_assembly", "pickup": "campsite_pickup"},
    {"id": "orchard", "start": "orchard_start", "assembly": "orchard_assembly", "pickup": "orchard_pickup", "held": True},
    {"id": "sawmill", "start": "sawmill_start", "assembly": "sawmill_assembly", "pickup": "sawmill_pickup", "held": True},
]
m.routes = [{"id": "central_route", "from": "campsite_pickup", "to": "orchard_pickup", "class": "ground"}]
m.roles.update({
    "outer_forest": {"regions": ["outer_forest", "sawmill_outer"]},
    "sawmill_access": {"regions": ["sawmill_access"]},
    "corridor_flank": {"regions": ["corridor_flank"]},
    "orchard_pump_house": {"infrastructure": ["orchard_pump_house"]},
    "central_route": {"edges": ["g_corridor_w", "g_corridor_e"]},
    "tank": {"stores": ["tank"]},
    "pond": {"stores": ["pond"]},
})

# Compact layout: the high landing becomes the primary receiving point (recipe receiving.compact_primary). Capacities
# stay with the reference replays (receiving equals the population exactly), so the swap is in staging travel.
m.class_overrides = {
    "compact": {
        "edges": [
            {"id": "g_orchard_safe", "ticks": 500},
            {"id": "g_depot_gate", "ticks": 100},
        ],
    },
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(2, 5))
