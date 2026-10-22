# W2-M3 Water Is a Route: BLOCKED (engine gap)

Status: **not authored**. There is no `map.json` or strategy here on purpose. Under the stop condition, work stopped because the compiler cannot express this mission's central mechanic. This is not a content tuning problem.

## Why the mission needs the missing rule
The mission is "make water supply and transport choices interact": a pond and a storage tank feed separate areas, and "a pump link can refill the tank without protecting the road by itself". The contract depends on that pump link in four places:
- Brief A: "Pump pond water into the tank and alternate sprinklers."
- Brief B: "Use firebreaks to minimize wetting and reserve pumped water for the return road."
- `event_2` at S=1500: `warn_resource` on `tank`, "the tank drops below half unless replenished".
- Mastery: `resource_remaining` at least 1,000,000 on **both** `pond` and `tank`.

Sprinklers draw only from finite stores (`sprinkler.store`). So the pond and the tank must both be stores, and the pump must move water from one store to the other.

## The gap
`MapCompiler.CompileSockets` reads a pump's `intake` and `outlet` only as grid cells (`Cell(s["intake"])`). A store is a water node (index `cells + k`), but nothing in the map can name it as a pump end, and stores have no hydraulic links to the grid. Water can therefore never be pumped into, or out of, a store.

Spec 02 §4 says pumps can do this:
- "A water node is either a terrain cell or a finite tank/pond."
- "An active powered pump uses a directed source→destination pair from its socket contract."
- "A finite destination receives its incoming grants only in the published next volume."

`Simulator.WaterPulse` already handles pump requests between any water nodes, including store nodes.

## Minimal reproduction
Start from `content/w2/m1/map.json`, which compiles. Add a pond store and one pump socket:
```json
"stores": [{"id": "tank", "elev": 1200, "volume": 600000000},
           {"id": "pond", "elev": 1200, "volume": 1200000000}],
"sockets": [ ..., {"id": "pump:01", "tool": "pump", "at": [26, 9], "intake": "pond", "outlet": "tank", "power": "grid"}]
```
Compile it with `MapCompiler.Compile(map, <W2-M1 contract>, equipment_rules, "wide")`.
- **Observed:** `NullReferenceException` inside `CompileSockets`. `Cell()` indexes `xy[0]` on a string, so this is not even a `ContentException`. A cell intake with a store outlet (`"intake": [26, 9], "outlet": "tank"`) fails the same way.
- **Expected (spec 02 §4):** the pump compiles with intake = pond's store node and outlet = tank's store node. When powered, it moves up to 20,000,000 mm³/s from pond to tank (lift 0), competing with the pond's sprinkler draws, and conserving volume.

This was run headless on 23 Oct with a scratch harness that compiles the Sim runtime plus `tools/simtest/Harness/Lab.cs` and calls `MapCompiler.Compile` directly. Nothing in `game/` or `tools/simtest/` was changed.

## Suggested engine change (for the coordinator; not done here)
Let a pump socket's `intake` and `outlet` be either `[x, y]` or a store id string. Resolve the string to `d.StoreNode[StoreRef(id)]`. Pump admission ("legal intake/outlet") then covers store nodes. A store-to-store pump has lift 0 when both stores have equal `elev`. Also add a SimTest covering pond→tank volume conservation.

Once that exists, the mission can follow the W2-M1 kit (`tools/maps/w2_kit.py`): the orchard edge ignites at 400, the second ignition threatens the return road at 3000, and A and B differ in pump use versus firebreak use.
