# W2-M4 When the Wind Turns: BLOCKED (engine gap)

Status: **not authored**. There is no `map.json` or strategy here on purpose. Under the stop condition, work stopped because the mission has no fire source the engine can create.

## Why the mission needs the missing rule
The contract's events are wind only:

| Event | Effect |
|---|---|
| S=700 | `set_wind` west |
| S=2000 | `warn_wind_change`, which is advisory |
| S=2400 | `set_wind` east |
| S=3600 | `set_wind` west |

There is no `ignite_region` and no `add_heat`. The bible's threat ("70 s: wind is stable westward; 200 s: a 40-second warning precedes an eastward turn; 360 s: a second turn threatens the unprotected flank") and its failure brief ("ignoring the visible wind timeline makes the formerly safe flank dangerous") both assume a fire that is **already burning** and that the wind steers.

Spec 02 §2 says initial conditions belong in the compiled state: "Initial parameters belong in the compiled initial state, not tick-zero effects." So the burning front must come from the map.

## The gap
`MapCompiler.CompileFire` reads legend `fuel`, `wetness` and `building`, but nothing sets an initial heat or an initial burning state:
- `MissionDef.CellHeat0` is allocated and never filled.
- `Simulator.InitFire` sets every cell `Unburned`.

`docs/map-schema.md` also lists no such field. Without an ignition event, no cell can ever burn. The wind changes therefore do nothing, the lookout "flank" decision does not exist, and any mistake could only fail for non-fire reasons.

## Minimal reproduction
Start from `content/w2/m1/map.json`, which compiles, and give the forest legend char an initial heat:
```json
"T": {"terrain": "forest", "code": 14, "elev": 1200, "fuel": 1000, "heat": 800}
```
Compile it against the W2-M1 contract (layout wide), then step a Simulator to S=400. That is before the W2-M1 contract's own ignition at S=500.
- **Observed:** `CellHeat0` is 0 at the forest cell (12,16), its heat is still 0 at S=400, and no cell has left `Unburned`.
- **Expected:** an authored initial fire state reaches the compiled state, for example a per-char or per-region `heat`, or a `fire.initial_burning: [region]` list. Cells authored as burning are `Burning` at S=0. The warning counts as published at S=0, which spec 02 §2 allows ("emit every warning assigned to S=0 exactly once").

This was run headless on 23 Oct with a scratch harness (Sim runtime plus `Lab.cs`, calling `MapCompiler.Compile` and `Simulator.Step`). Nothing in `game/` or `tools/simtest/` was changed.

## Suggested engine change (for the coordinator; not done here)
Read the legend (or region) `heat` into `CellHeat0`. Add `fire.initial_burning` (a list of regions) to start those cells `Burning` with `FWarnStart = 0`. Cover it with a FireTests case, for example "a burning stand at S=0 spreads downwind and turns with `set_wind`".

The alternative is a contract change: adding an `ignite_region` early in W2-M4. That would edit frozen contract values, so it is the user's or coordinator's call, not a content override.

Once either exists, the mission fits the W2 kit:
- the lookout (8) sits on the flank that becomes downwind at 2400;
- the fixed powered tank becomes a power node with `fixed_supply` (the kit has no generator);
- mastery is "no more than 2 firebreak deployments".
