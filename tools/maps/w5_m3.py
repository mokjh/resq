"""W5-M3 The Cooling Works — authored layout (recipe W5-M3-layout-v1; anchors kept approximately).

Story: the cooling works stands on the west basalt shelf. Below it, the plant's drainage cutting is flooded (700 mm)
and from 50 s a spring refills it (30 M mm3/s). The short route from the works crosses that cutting on a causeway (passable
at 100 mm or less), then the delivery channel at crossing S. The long mountain path climbs north round both vents and comes
down the east side to crossing L on the second channel.
- Power: the works (demand 1, priority 40) and the drainage pumps (1 each, priority 20) share the works grid. One
  generator gives 2 units, so while two pumps run the works is starved and makes no coolant. Generator ground: the
  cutting's platform (900 mm, floods only if the cutting overtops) or the dry ridge, which needs a relay into the works
  grid: relay:01 on the same platform, or relay:02 beside the delivery channel, where the 170 s arrival destroys it.
- 170 s: the delivery channel turns Hot, closing crossing S, and its heat makes the plant terraces unsafe: residents
  still there need the plant shelter (six places; two more fit in the control room).
- 310 s: the second channel turns Hot and closes crossing L.
- Both crossings are narrow (2 slots, 100 ticks): a crust window passes four walkers.
- Finite coolant: three coolers (one charge each) and the works store (3 charges, capacity 8). The works turns six
  feedstock units into charges, one per 100 powered ticks, only while it is operational.
"""
from w5_kit import W5Map, ROAD, NARROW, rect

m = W5Map("W5-M3")
m.legend.update({
    "f": {"terrain": "drainage_cutting", "code": 6, "elev": 0, "init_depth": 700, "cond": 1_000_000},
    "P": {"terrain": "cutting_platform", "code": 2, "elev": 900},
})

# ---------------------------------------------------------------- terrain
m.rect("c", 1, 14, 4, 17)            # the cooling works
m.rect(".", 4, 12, 6, 15)            # plant terraces
m.rect("o", 5, 5, 9, 9)              # village orchard terraces
m.rect("f", 8, 11, 10, 12)           # flooded drainage cutting (the drainage basin)
m.rect("P", 8, 13, 10, 13)           # cutting platform
m.cell("w", 9, 9)                    # outfall (pumps discharge here; a sink drains it to the sea)
m.rect("D", 15, 2, 17, 4)            # depot
m.rect("H", 19, 3, 21, 5)            # high landing
m.rect("v", 26, 12, 29, 15)          # research station
m.line("=", (5, 18), (5, 21), (30, 21), (30, 10), (26, 10))   # long mountain path
m.line("=", (15, 9), (15, 5))
m.line("=", (22, 9), (21, 6))
m.rect("S", 0, 0, 31, 0)

# ---------------------------------------------------------------- lava
m.segment("delivery", rect(13, 1, 14, 16), edges=["g_cross_short"],
          nodes=["cooling_works_start", "cooling_works_assembly", "cooling_works_pickup", "works_shelter"])
m.segment("late", rect(23, 1, 24, 19), edges=["g_cross_late"])
m.lava_sources = [{"id": "delivery_vent", "segment": "delivery"}, {"id": "east_vent", "segment": "late"}]
m.coolant = [{"id": "coolant", "charges": 3, "capacity": 8}]

# ---------------------------------------------------------------- water
m.sources = [{"id": "drain_in", "at": [9, 12], "rate": 0}]
m.sinks = [{"id": "outfall", "at": [9, 9], "rate": 80_000_000}]
m.regions = {"works": "c", "cutting": "f", "plant_heat": rect(13, 12, 14, 16)}

# ---------------------------------------------------------------- navigation
m.group_nodes("cooling_works", (5, 14), 8)
m.group_nodes("village", (7, 8), 6)
m.group_nodes("research", (27, 14), 6)
m.node("works_yard", (6, 12), ["junction"], capacity=2)          # control room
m.node("works_shelter", (7, 13), ["shelter"], capacity=6)
m.node("d_bank", (12, 11), ["junction"], capacity=8)
m.node("short_south", (15, 9), ["junction"], capacity=8)
m.node("ridge_west", (5, 21), ["junction"], capacity=8)
m.node("ridge_east", (30, 21), ["junction"], capacity=8)
m.node("east_strip", (30, 11), ["junction"], capacity=8)
m.node("ridge_hut", (18, 21), ["shelter"], capacity=6)
m.node("late_bank", (25, 10), ["junction"], capacity=8)
m.node("late_south", (22, 9), ["junction"], capacity=8)
m.node("depot", (16, 3), ["evacuation"], capacity=12)        # wide primary receiving point
m.node("high_landing", (20, 4), ["evacuation"], capacity=8)   # wide secondary

m.edge("g_works_yard", "cooling_works_pickup", "works_yard", 20, **ROAD)
m.edge("g_works_shelter", "works_yard", "works_shelter", 20, **ROAD)
m.edge("g_causeway", "works_shelter", "d_bank", 100, sample=[[8, 12], [9, 12], [10, 12]], **ROAD)
m.edge("g_cross_short", "d_bank", "short_south", 100, sample=[[13, 11], [14, 10]], tags=["crossing"], **NARROW)
m.edge("g_short_depot", "short_south", "depot", 100, sample=[[15, 9], [15, 5]], **ROAD)
m.edge("g_village_road", "village_pickup", "d_bank", 150, sample=[[9, 6], [11, 8], [12, 11]], **ROAD)
m.edge("g_long_a", "cooling_works_pickup", "ridge_west", 600, sample=[[5, 18], [5, 21]], slots=8)
m.edge("g_long_b", "ridge_west", "ridge_east", 1400, sample=[[6, 21], [29, 21]], slots=8)
m.edge("g_ridge_hut", "ridge_east", "ridge_hut", 40, **ROAD)
m.edge("g_long_c", "ridge_east", "east_strip", 800, sample=[[30, 20], [30, 12]], slots=8)
m.edge("g_long_d", "east_strip", "late_bank", 300, sample=[[30, 10], [26, 10]], slots=8)
m.edge("g_research_bank", "research_pickup", "late_bank", 150, sample=[[27, 12], [25, 10]], **ROAD)
m.edge("g_cross_late", "late_bank", "late_south", 100, sample=[[24, 10], [23, 9]], tags=["crossing"], **NARROW)
m.edge("g_late_landing", "late_south", "high_landing", 150, sample=[[22, 9], [21, 6]], **ROAD)

# ---------------------------------------------------------------- power and the cooling works
m.power = {"nodes": [{"id": "works_grid", "fixed_supply": 0}, {"id": "ridge_grid", "fixed_supply": 0}],
           "links": [{"from": "ridge_grid", "to": "works_grid", "relay": "relay:01"},
                     {"from": "ridge_grid", "to": "works_grid", "relay": "relay:02"}]}
m.infrastructure = [{"id": "cooling_works", "power": "works_grid", "demand": 1, "priority": 40, "region": "works",
                     "producer": {"store": "coolant", "feedstock": 6, "ticks": 100}}]

# ---------------------------------------------------------------- sockets
m.socket("generator:01", "generator", at=[8, 13], platform=[[8, 13]], power="works_grid", zone="generators")
m.socket("generator:02", "generator", at=[6, 19], platform=[[6, 19]], power="ridge_grid", zone="generators")
m.socket("relay:01", "relay", at=[10, 13], platform=[[10, 13]], zone="relays")
m.socket("relay:02", "relay", at=[13, 13], platform=[[13, 13]], zone="relays")      # stands on the delivery channel
m.socket("pump:01", "pump", at=[9, 13], intake=[9, 12], outlet=[9, 9], power="works_grid", zone="pumps")
m.socket("pump:02", "pump", at=[11, 12], intake=[10, 11], outlet=[9, 9], power="works_grid", zone="pumps")
m.socket("pump:03", "pump", at=[7, 11], intake=[8, 11], outlet=[9, 9], power="works_grid", zone="pumps")
m.socket("cooler:01", "cooler", at=[12, 12], segment="delivery", coolant_store="coolant", zone="coolers")
m.socket("cooler:02", "cooler", at=[25, 11], segment="late", coolant_store="coolant", zone="coolers")
m.socket("cooler:03", "cooler", at=[12, 10], segment="delivery", coolant_store="coolant", zone="coolers")
m.socket("cooler:04", "cooler", at=[25, 9], segment="late", coolant_store="coolant", zone="coolers")
m.socket("shelter:01", "shelter", node="works_shelter", at=[7, 14], region="plant_heat", zone="shelters")
m.socket("shelter:02", "shelter", node="ridge_hut", at=[18, 22], zone="shelters")

# compact layout: the high landing becomes the primary receiving point (recipe receiving swap)
m.class_overrides = {"compact": {"nodes": [{"id": "depot", "capacity": 8}, {"id": "high_landing", "capacity": 12}]}}
m.groups = [
    {"id": "cooling_works", "start": "cooling_works_start", "assembly": "cooling_works_assembly", "pickup": "cooling_works_pickup"},
    {"id": "village", "start": "village_start", "assembly": "village_assembly", "pickup": "village_pickup"},
    {"id": "research", "start": "research_start", "assembly": "research_assembly", "pickup": "research_pickup"},
]
m.roles = {
    "drainage_basin": {"sources": ["drain_in"]},
    "coolant_delivery": {"lava_sources": ["delivery_vent"]},
    "second_channel": {"lava_sources": ["east_vent"]},
    "cooling_works": {"infrastructure": ["cooling_works"]},
}

if __name__ == "__main__":
    print(m.ascii())
    print("wrote", m.write(5, 3))
