"""W2-M3 Water Is a Route — authored layout (recipe W2-M3-layout-v1; anchors kept).

Story: the orchard (west) and the pond houses (east) evacuate to the depot and the high landing. The orchard road
(y 11) runs from the orchard yard to the junction, and the return road (x 15) runs from there down to the depot;
together they are the orchard assembly route that must stay usable to the end.
- At 40 s the orchard's inner edge (row 14) ignites. The front reaches the four-tree strip along the orchard road
  (x 7..10, y 12), which also stands beside the orchard yard's nodes; if the strip burns, the yard is unsafe and the
  road closes. It can be kept by clearing it (firebreak:01) or by wetting it from the tank (sprinkler:01).
- At 300 s a patch of brush beside the depot ignites. It touches only the culvert shed at the foot of the return
  road; the shed heads a row of timber sheds (buildings: no firebreak can clear them) that runs up the return road
  and along the orchard road. If the culvert shed is dry when the brush burns, the fire creeps up the sheds for the
  rest of the mission and the route is lost. Only water (sprinkler:02, from the tank) protects it.
Water: the storage tank (600,000,000 mm3) stands high on the hillside, 4,400 mm above the pond (1,200,000,000 mm3).
A pump can lift pond water into it; because the lift grows as the tank fills and the pond falls, the pump stops by
itself while the tank holds 600,000,000 mm3 more than the pond, so a running pump never empties the pond.
"""
from mapkit import Map
from w2_kit import new_map, hline, vline, TANK_VOLUME, POND_VOLUME

m = new_map("W2-M3")

# ---------------------------------------------------------------- terrain
m.rect("#", 0, 21, 31, 23)                  # hillside crest (the tank stands up here)
m.rect("v", 3, 10, 7, 14)                   # orchard yard (anchor west 6,13)
m.rect("o", 8, 13, 11, 18)                  # the orchard
m.line("o", (7, 12), (10, 12))              # the strip of orchard trees along the orchard road
m.rect("v", 23, 10, 27, 14)                 # pond houses (anchor east 25,13)
m.rect("~", 21, 15, 28, 18)                 # the pond (scenery water; the pond store is its usable volume)
# return road: culvert shed at its foot, then a row of timber sheds up the road and along the orchard road
m.cell("b", 16, 5)                          # culvert shed (head of the shed row)
for y in range(6, 12):
    m.cell("b", 16, y)
for x in (13, 14, 15):
    m.cell("b", x, 12)
m.rect("T", 17, 3, 18, 4)                   # brush patch beside the depot (return_road_fuel)
# roads
m.line("=", (6, 11), (15, 11))              # orchard road
m.line("=", (15, 10), (15, 4))              # return road
m.line("=", (25, 10), (25, 5))              # pond houses road to the high landing
m.line("=", (16, 3), (16, 3))
m.rect("D", 13, 2, 17, 3)
m.rect("H", 24, 3, 27, 4)
m.cell("D", 15, 3)
m.cell("H", 25, 4)
m.cell("T", 17, 3); m.cell("T", 17, 4)

# ---------------------------------------------------------------- regions / fire
m.regions.update({
    "orchard_edge": hline(14, 8, 11),
    "return_road_fuel": {"x0": 17, "y0": 3, "x1": 18, "y1": 4},
    "orchard_strip": hline(12, 7, 10),
    "timber_sheds": vline(16, 5, 11) + hline(12, 13, 15),
})
m.extra["stores"] = [
    {"id": "tank", "elev": 5600, "volume": TANK_VOLUME},
    {"id": "pond", "elev": 1200, "volume": POND_VOLUME},
]

# ---------------------------------------------------------------- navigation
m.node("orchard_start", (6, 13), ["assembly"], capacity=8)
m.node("orchard_assembly", (6, 12), ["assembly"], capacity=8)
m.node("orchard_pickup", (6, 11), ["assembly"], capacity=8)
m.node("pond_houses_start", (25, 13), ["assembly"], capacity=8)
m.node("pond_houses_assembly", (25, 12), ["assembly"], capacity=8)
m.node("pond_houses_pickup", (25, 11), ["assembly"], capacity=8)
m.node("orchard_junction", (15, 11), ["junction"])
m.node("depot", (15, 3), ["evacuation"], capacity=8)
m.node("high_landing", (25, 4), ["evacuation"], capacity=8)

for g in ("orchard", "pond_houses"):
    m.edge(f"g_{g}_assembly", f"{g}_start", f"{g}_assembly", 20)
    m.edge(f"g_{g}_pickup", f"{g}_assembly", f"{g}_pickup", 20)
m.edge("g_orchard_road", "orchard_pickup", "orchard_junction", 300, sample=hline(11, 6, 15))
m.edge("g_return_road", "orchard_junction", "depot", 300, sample=vline(15, 3, 11))
m.edge("g_pond_road", "pond_houses_pickup", "high_landing", 300, sample=vline(25, 4, 11))

# ---------------------------------------------------------------- sockets
m.socket("pump:01", "pump", at=[19, 20], intake="pond", outlet="tank", power="grid", zone="pumps")
m.socket("pump:02", "pump", at=[22, 19], intake="pond", outlet="tank", power="grid", zone="pumps")
m.socket("sprinkler:01", "sprinkler", at=[4, 12], cells=hline(12, 7, 10), store="tank", power="grid", zone="sprinklers")  # orchard strip
m.socket("sprinkler:02", "sprinkler", at=[14, 6], cells=[[16, 5]], store="tank", power="grid", zone="sprinklers")         # culvert shed
m.socket("sprinkler:03", "sprinkler", at=[24, 9], cells=[[23, 14], [27, 14]], store="pond", power="grid", zone="sprinklers")  # pond houses
m.socket("firebreak:01", "firebreak", at=[8, 11], cells=hline(12, 7, 10), zone="firebreaks")    # orchard strip
m.socket("firebreak:02", "firebreak", at=[12, 14], cells=hline(13, 8, 11), zone="firebreaks")   # orchard's first row
m.socket("firebreak:03", "firebreak", at=[10, 19], cells=hline(18, 8, 11), zone="firebreaks")   # orchard's back row
m.socket("generator:01", "generator", at=[20, 8], platform=[[20, 8]], power="grid", zone="generators")
m.socket("generator:02", "generator", at=[12, 15], platform=[[12, 15]], power="grid", zone="generators")
m.socket("crawler:01", "crawler", node="depot", at=[14, 3])
m.socket("crawler:02", "crawler", node="orchard_junction", at=[14, 10])

m.power = {"nodes": [{"id": "grid", "fixed_supply": 0}], "links": []}
m.groups = [
    {"id": "orchard", "start": "orchard_start", "assembly": "orchard_assembly", "pickup": "orchard_pickup"},
    {"id": "pond_houses", "start": "pond_houses_start", "assembly": "pond_houses_assembly", "pickup": "pond_houses_pickup"},
]
m.routes = [{"id": "orchard_assembly_route", "from": "depot", "to": "orchard_assembly", "class": "ground"}]
m.roles.update({
    "orchard_edge": {"regions": ["orchard_edge"]},
    "return_road_fuel": {"regions": ["return_road_fuel"]},
    "tank": {"stores": ["tank"]},
    "pond": {"stores": ["pond"]},
    "orchard_assembly_route": {"edges": ["g_orchard_assembly", "g_orchard_pickup", "g_orchard_road", "g_return_road"]},
})

# Compact layout: the high landing becomes the primary receiving point (recipe receiving.compact_primary).
# Capacities stay with the reference replays (receiving equals the population), so the swap is in staging travel.
m.class_overrides = {
    "compact": {
        "edges": [
            {"id": "g_pond_road", "ticks": 250},
            {"id": "g_return_road", "ticks": 350},
        ],
    },
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(2, 3))
