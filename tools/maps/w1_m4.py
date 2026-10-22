"""W1-M4 Power Before Water — authored layout (recipe W1-M4-layout-v1; anchors kept approximately).

Story: the clinic stands on a raised pad between two town basins. The first spill fills the clinic basin
(west of the pad) from 50 s; the final spill fills the second basin (east of the pad) from 380 s. If either
basin climbs over the pad the clinic power fails. Three pump sockets: two drain the clinic basin (one into the
west branch, one into the fork), one drains the second basin into the east branch. Pumps and the clinic share
one power spine; each generator gives 2 units, the clinic takes 1 and each pump 1, so a single generator
cannot run both pumps (the second queues). Dry generator ground is scarce: the ridge socket is always dry, the
spine junction beside the power basin is dry only if a barrier seals the basin's lip before the 280 s spill,
and the basin-shore platform floods. The tram passengers are released to their assembly point at 160 s and walk
to the east landing. The riverside houses can only leave by skiff; the clinic basin overtops onto their
terrace if it is not pumped.
"""
from mapkit import Map, LEGEND_W1

LEGEND = dict(LEGEND_W1)
LEGEND.update({
    "R": {"terrain": "reservoir", "code": 4, "elev": 0, "init_depth": 300, "cond": 2_000_000},
    "k": {"terrain": "clinic_basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "n": {"terrain": "second_basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "e": {"terrain": "power_basin", "code": 6, "elev": 0, "cond": 1_000_000},
    "M": {"terrain": "clinic_pad", "code": 8, "elev": 300},
    "b": {"terrain": "low_lip", "code": 1, "elev": 350},
    "q": {"terrain": "basin_weir", "code": 12, "elev": 600, "cond": 300_000},
    "P": {"terrain": "low_platform", "code": 2, "elev": 200},
    "J": {"terrain": "spine_junction", "code": 2, "elev": 300},
})

m = Map("W1-M4", legend=LEGEND)

# ---------------------------------------------------------------- terrain
m.rect("R", 13, 21, 17, 23)
m.line("~", (15, 20), (15, 15))
m.line("~", (14, 15), (10, 15), (10, 1), (16, 1))      # west branch past the riverside houses
m.line("~", (16, 15), (23, 15), (23, 2), (17, 2), (17, 1))  # east branch past the east landing
m.cell("~", 16, 1)
m.cell("w", 16, 0)
# town between the branches
m.rect("k", 12, 10, 15, 12)        # clinic basin
m.rect("M", 16, 9, 16, 12)         # clinic pad
m.rect("n", 17, 9, 18, 12)         # second basin
m.line("=", (16, 8), (16, 3))      # clinic road to the depot
m.rect("D", 13, 2, 15, 4)
m.rect("L", 12, 6, 14, 8)          # riverside houses terrace
m.cell("b", 13, 9)                 # lip between the clinic basin and the riverside terrace
m.rect("e", 20, 5, 21, 6)          # power basin
m.cell("q", 22, 5)                 # its weir to the east branch
m.cell("J", 19, 6)                 # spine junction platform (generator:02)
m.cell("P", 12, 13)                # basin-shore platform (generator:03)
# east bank: tram depot and east landing
m.rect("v", 25, 10, 28, 13)
m.line("=", (25, 9), (25, 6))
m.rect("H", 24, 3, 28, 5)

# ---------------------------------------------------------------- water
m.sources = [
    {"id": "reservoir_in", "at": [15, 22], "rate": 10_000_000},
    {"id": "clinic_in", "at": [13, 11], "rate": 0},
    {"id": "power_in", "at": [20, 6], "rate": 0},
    {"id": "second_in", "at": [18, 10], "rate": 0},
]
m.sinks = [{"id": "outlet", "at": [16, 0], "rate": 80_000_000}]
m.regions = {"clinic": "M", "clinic_basin": "k", "second_basin": "n", "power_basin": "e", "riverside": "L"}

# ---------------------------------------------------------------- navigation
m.node("clinic_start", (16, 12), ["assembly"], capacity=6)
m.node("clinic_assembly", (16, 11), ["assembly"], capacity=6)
m.node("clinic_pickup", (16, 10), ["assembly"], capacity=6)
m.node("clinic_gate", (16, 8), ["junction"])
m.node("depot", (14, 3), ["evacuation", "dock"], capacity=10, water_at=[13, 1], dock_min_depth=100, dock_max_depth=2500)
m.node("riverside_start", (13, 7), ["assembly"], capacity=4)
m.node("riverside_assembly", (12, 7), ["assembly"], capacity=4)
m.node("riverside_pickup", (11, 7), ["dock"], capacity=6, water_at=[10, 7], dock_min_depth=100, dock_max_depth=2500)
m.node("tram_start", (26, 12), ["assembly"], capacity=6)
m.node("tram_assembly", (26, 11), ["assembly"], capacity=6)
m.node("tram_pickup", (25, 10), ["assembly"], capacity=6)
m.node("high_landing", (24, 4), ["evacuation", "dock"], capacity=6, water_at=[23, 4], dock_min_depth=100, dock_max_depth=2500)

m.edge("g_clinic_start", "clinic_start", "clinic_assembly", 20)
m.edge("g_clinic_pickup", "clinic_assembly", "clinic_pickup", 20)
m.edge("g_clinic_gate", "clinic_pickup", "clinic_gate", 30, sample=[[16, 10], [16, 9], [16, 8]])
m.edge("g_clinic_road", "clinic_gate", "depot", 250, sample=[[16, y] for y in range(3, 9)])
m.edge("g_riverside_start", "riverside_start", "riverside_assembly", 20)
m.edge("g_riverside_pickup", "riverside_assembly", "riverside_pickup", 20)
m.edge("g_tram_start", "tram_start", "tram_assembly", 20)
m.edge("g_tram_pickup", "tram_assembly", "tram_pickup", 20)
m.edge("g_tram_road", "tram_pickup", "high_landing", 260, via=[[25, 4]], sample=[[25, y] for y in range(4, 11)])

water = dict(cls="water", slots=2, min_depth=100, max_depth=2500)
m.edge("w_riverside_depot", "riverside_pickup", "depot", 200, sample=[[10, y] for y in range(1, 8)] + [[11, 1], [12, 1], [13, 1]], **water)
m.edge("w_depot_landing", "depot", "high_landing", 260,
       sample=[[14, 1], [15, 1], [16, 1], [17, 1], [17, 2]] + [[x, 2] for x in range(18, 24)] + [[23, 3], [23, 4]], **water)

# ---------------------------------------------------------------- sockets
m.socket("pump:01", "pump", at=[11, 11], intake=[12, 11], outlet=[10, 11], power="spine", zone="pumps")
m.socket("pump:02", "pump", at=[20, 10], intake=[18, 10], outlet=[23, 10], power="spine", zone="pumps")
m.socket("pump:03", "pump", at=[14, 13], intake=[14, 12], outlet=[14, 15], power="spine", zone="pumps")
m.socket("generator:01", "generator", at=[16, 13], platform=[[16, 13]], power="spine", zone="generators")
m.socket("generator:02", "generator", at=[19, 6], platform=[[19, 6]], power="spine", zone="generators")
m.socket("generator:03", "generator", at=[12, 13], platform=[[12, 13]], power="spine", zone="generators")
m.socket("barrier:01", "barrier", at=[20, 6], links=[[[20, 6], [19, 6]]], zone="barriers")
m.socket("barrier:02", "barrier", at=[13, 9], links=[[[13, 10], [13, 9]]], zone="barriers")
m.socket("skiff:01", "skiff", node="depot", at=[13, 1])
m.socket("skiff:02", "skiff", node="riverside_pickup", at=[10, 7])

m.power = {"nodes": [{"id": "spine", "fixed_supply": 0}], "links": []}
m.infrastructure = [
    {"id": "clinic_power", "power": "spine", "demand": 1, "region": "clinic", "max_depth": 100},
]
m.groups = [
    {"id": "clinic", "start": "clinic_start", "assembly": "clinic_assembly", "pickup": "clinic_pickup"},
    {"id": "tram", "start": "tram_start", "assembly": "tram_assembly", "pickup": "tram_pickup", "held": True},
    {"id": "riverside", "start": "riverside_start", "assembly": "riverside_assembly", "pickup": "riverside_pickup"},
]
m.roles = {
    "clinic_basin": {"sources": ["clinic_in"]},
    "power_basin": {"sources": ["power_in"]},
    "second_basin": {"sources": ["second_in"]},
    "tram": {"groups": ["tram"]},
    "clinic_power": {"infrastructure": ["clinic_power"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(1, 4))
