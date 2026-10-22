"""Shared World 2 (Emberwood) authoring helpers: fire legend, wind region, finite stores.

Fire rules (spec 02 §7, DECISIONS 23 Oct) that shape every W2 layout:
- Only Burning cells emit heat, and only to their 8 neighbours. A fuel-0 cell never burns, so a straight
  one-cell road or meadow row is already a complete firebreak. Fire travels only through 8-connected fuel.
- A road (fuel 0) beside a burning cell latches closed (heat >= 300) and reopens only at heat <= 100. It heats
  towards 1000 while neighbours burn and then cools 2 points per pulse, so it stays closed about 450 pulses after
  the last neighbour stops burning.
- A forest cell burns for 333 pulses (fuel 1000, 3 per pulse). The 100-tick ignition warning dominates spread:
  a broad front advances about one cell per 110-130 pulses whatever the wind. A one-cell hedge crawls 130 pulses
  per cell downwind, 150 crosswind or calm, and 300 upwind. Long hedges beside a road therefore keep it closed
  for a long time; that is how a W2 map makes a fire still matter at the end of the mission.
- A sprinkler (5,000,000 mm3/s) gives 500,000 mm3 per pulse split over its 1-4 cells; 25,000 mm3 is one wetness
  point and wetness decays one point per pulse. Wet (>= 600) cells cannot ignite and lose 2 + W/100 heat per pulse.
Every legend char authors `fuel` explicitly (the compiler defaults to 1000 when fire is enabled).
"""
from mapkit import Map

# terrain codes 13+ are World 2; 2/3/8/9 keep their W1 meanings (road, high, depot, landing)
LEGEND_W2 = {
    "#": {"terrain": "rock", "code": 3, "elev": 1200, "fuel": 0},
    ",": {"terrain": "meadow", "code": 13, "elev": 1200, "fuel": 0},
    "T": {"terrain": "forest", "code": 14, "elev": 1200, "fuel": 1000},
    "h": {"terrain": "hedge_trees", "code": 15, "elev": 1200, "fuel": 1000},
    "Y": {"terrain": "dry_stand", "code": 16, "elev": 1200, "fuel": 1000},
    "=": {"terrain": "road", "code": 2, "elev": 300, "fuel": 0},
    "c": {"terrain": "campsite", "code": 17, "elev": 100, "fuel": 0},
    "k": {"terrain": "lookout_knoll", "code": 18, "elev": 1200, "fuel": 0},
    "D": {"terrain": "depot", "code": 8, "elev": 1200, "fuel": 0},
    "H": {"terrain": "high_landing", "code": 9, "elev": 1200, "fuel": 0},
    "w": {"terrain": "woodshed", "code": 19, "elev": 1200, "fuel": 1000, "building": True},
    "o": {"terrain": "orchard", "code": 10, "elev": 100, "fuel": 1000},
    "v": {"terrain": "camp_ground", "code": 11, "elev": 300, "fuel": 0},
    "R": {"terrain": "refill_station", "code": 20, "elev": 1200, "fuel": 0, "building": True},
    "S": {"terrain": "sawmill", "code": 21, "elev": 1200, "fuel": 0, "building": True},
    "P": {"terrain": "pump_house", "code": 22, "elev": 1200, "fuel": 0, "building": True},
    "~": {"terrain": "pond", "code": 0, "elev": 0, "fuel": 0, "init_depth": 300, "cond": 2_000_000},
    "b": {"terrain": "cabin", "code": 23, "elev": 1200, "fuel": 1000, "building": True},
}

TANK_VOLUME = 600_000_000   # recipe resource_stores.tank
POND_VOLUME = 1_200_000_000  # recipe resource_stores.pond


def new_map(mission_id: str, fill: str = ",", legend_extra=None) -> Map:
    legend = dict(LEGEND_W2)
    if legend_extra:
        legend.update(legend_extra)
    for ch, spec in legend.items():
        if "fuel" not in spec:
            raise ValueError(f"legend '{ch}' must author fuel explicitly")
    m = Map(mission_id, fill=fill, legend=legend)
    m.regions["forest"] = {"x0": 0, "y0": 0, "x1": 31, "y1": 23}  # one wind region: the whole reserve
    m.extra["fire"] = {"enabled": True, "wind_regions": ["forest"]}
    m.roles["forest"] = {"regions": ["forest"]}
    return m


def cells_of(m: Map, ch: str):
    return [[x, y] for y in range(24) for x in range(32) if m.grid[y][x] == ch]


def hline(y, x0, x1):
    return [[x, y] for x in range(min(x0, x1), max(x0, x1) + 1)]


def vline(x, y0, y1):
    return [[x, y] for y in range(min(y0, y1), max(y0, y1) + 1)]
