# RESQ decision ledger

Newest first. Decisions before 22 Oct 2026 are archived verbatim in `docs/archive/process-2026-10/DECISIONS.md`.

## 22 Oct 2026 (late) — art pipeline and credit caps (user-approved)

| Decision | Result |
|---|---|
| Art pipeline | Adopt the Waz-proven routing, re-authored in RESQ (no copied code): scripted Blender kits for buildings and simple props; Meshy-t2 for organic props; Tripo (via Higgsfield) or Meshy-7.1 for landmarks and vehicles; Higgsfield sheet, then Meshy, then a shared rig plus library clips for residents. RESQ Lit toon shader. |
| Credit caps | Raised to **Meshy 600 / Higgsfield 150** for characters, landmarks and swatches. Every spend is logged in `docs/spend-log.md`. Current Higgsfield balance (110) is below the cap; a top-up is the user's call. |

## 22 Oct 2026 — studio reset and competition plan (user-approved)

| Decision | Result |
|---|---|
| Competition entry | Meta VR Start Developer Competition 2026: Gaming track, New Experience division. Submit Tue 17 Nov; the deadline is Wed 18 Nov, 22:00 Cairo. |
| Scope | The user chose to attempt all 25 missions in the entry. Per-world ship gate: a world is playable in the entry only if all five of its missions pass validation, a Simulator playthrough and a device smoke test. Otherwise it is shown honestly as "in development". Alder Valley's five missions are never cut. |
| Process | Studio mode replaces the 229-task evidence/admission regime. Gates are real tests, deterministic replays, performance budgets, playtests and user reviews. Old records are archived, not deleted. |
| Device testing | Last. Physical Quest 3/3S work happens only in phase P5 (12–15 Nov); everything before that runs in the Editor, Meta XR Simulator and SpatialSim. |
| Simulation | New lean "Sim v2": struct-of-arrays, integer, allocation-free, deterministic, covering every world's rules. The old engine is kept unchanged as a golden-value oracle in `reference/legacy-sim/`. |
| Content | Mission factory: recipe plus contract feed a layout compiler, then a headless validator (no input must fail; strategies A and B must win on both layouts and all three presets). |
| Art | Original scripted-Blender kits on one palette atlas. Paid generation capped at Meshy ≤ 300 and Higgsfield ≤ 60 credits, each spend logged with its license. |
| Machine | RESQ gets exclusive use of Unity on this Mac until 18 Nov (the user pauses Waz). Reclaimed the throwaway bootstrap Unity `Library/` cache in `evidence/` (5.7 GB). |
| Git | Local commits on `main` at each working step. No push without the user's OK. |
| External writes | Store uploads, deploys, YouTube and domain are confirmed with the user at the time. |

## 23 Oct 2026 — rule readings for the World 2 and World 3 modules (spec 02 §5, §7, §8)

| Decision | Result |
|---|---|
| Fire on non-fuel cells | Every cell takes heat, so roads and water next to a fire latch closed. A legend char with no `fuel` defaults to 1000 (spec §7), so World 2 maps must author `fuel: 0` for roads and water. |
| Collapse warnings | A published warning of any event that acts on a support (`set_stability_loss`, `set_support_load`, `tremor`, `warn_support`) counts as the relevant warning and keeps its stamp. Only an emergent warning (effective ≤ 300) is cleared when stability rises above 400. |
| `warn_support` target | Resolves to `supports` in the map's roles, not infrastructure. |
| Winch duty | Duty counts powered travelling operation ticks. Departure needs duty < 100. A winch at its limit rests empty at a safe endpoint before anyone boards; 30 rest ticks reset duty to 0. |
| Cable edges | Open while a winch is placed, unbroken and its support stands. Power governs movement (brake, then a one-time emergency return), not edge admission. |
| Runout | Segments start at the release (or bound collapse) tick; successors start exactly `delay` ticks after their predecessor. Nodes are unsafe while a segment settles; `debris_reopenable` edges reopen after `settle`; other edges stay closed. |

## 23 Oct 2026 — rule readings for the World 4 module (spec 02 §4, §5, §9)

| Decision | Result |
|---|---|
| Sea boundary | A map `tides` entry names its boundary grid cells. Sea head = `datum` + level. Each boundary cell exchanges with the sea through a passive link (sill = cell bed, default conductance 2,000,000). Inflow is added independently; outflow competes with the cell's other outward requests. Per-controller `SeaIn`/`SeaOut` ledgers keep volume conservation exact. |
| Controllers | One per boundary: an event ramp (`set_tide_segment`) or a fixed knot track (optional loop period). The compiler rejects event ramps on a track controller and segment indexes outside 0–3. |
| High Tide Start | `MissionDef.TideStartIndex` sets the initial level with a degenerate ramp at S=0; events keep their authored ticks. |
| Relay | A power link object `{from, to, relay}` binds a relay socket. The link carries power while the relay is placed, enabled, intact and connected. It drops above 50 mm and reconnects by itself at ≤ 40 mm. Relays start enabled on placement. Every relay socket must bind a link. |
| `warn_support` in World 4 | When its role binds infrastructure rather than a support, the operation is advisory only. |
| Dock queue metric | Longest run of consecutive operation ticks a vehicle waits because its target dock's only berth is taken. |
| Berths (fixed 23 Oct) | A skiff reserves its destination berth at departure and keeps its origin berth until arrival. A normal arrival frees the origin; a turned-back skiff frees the destination. Two skiffs can no longer share a one-berth dock. |

## 23 Oct 2026 — rule readings for the World 5 module (spec 02 §5, §10)

| Decision | Result |
|---|---|
| Lava schedule | The `advance_lava` warning publishes every downstream arrival (earliest path, `LPlanned`). Activation schedules the same arrivals; each segment receives its first arrival once. The compiler rejects lava events with under 100 ticks of warning, and successor delays under 50. |
| Lava hazard | A crossing edge is open only while its segments are Dormant or Crusted. A node bound to a segment (or standing on its cells) is unsafe while that segment's heat is ≥ 600: Hot, Cooling or Reheating. |
| Coolers | Activation is the existing `CoolSegment` command on a placed cooler. It needs an enabled socket, a Hot segment and a charge. A refused activation keeps the charge. `RefillTool` moves one charge from the socket's bound coolant store only when the cooler is empty. |
| Coolant works | An infrastructure entry with `producer {store, feedstock, ticks}`. It runs while that infrastructure is operational (power and safety from stage 4). Feedstock is reserved at job start; a job starts only below store capacity. `resource_remaining` on a coolant store counts charges. |
| Shelters | A placed shelter on a `shelter` node (capacity 1–6, never a refuge). Each hazard pulse it consumes protection when occupied and its node is unsafe or its hazard region is hot. That interval's protection masks resident exposure and shelter damage. `enable_shelter_hazard` makes the shelter node itself unsafe. |
| Arrival damage | A Hot arrival (or a return to Hot) breaks equipment on the segment's cells, except coolers bound to that segment and shelters with protection left. Spent coolant is never a loss. |

## 23 Oct 2026 — engine gaps closed for World 2 (reported by the W2 mission helper)

| Decision | Result |
|---|---|
| Pumps and stores | A pump `intake`/`outlet` may name a finite store, so tank ↔ pond ↔ cell pumping works (spec 02 §4). This previously crashed the compiler. |
| Initial fire | `fire.burning` cells start Burning at S=0, and a legend `heat` sets initial heat. These are compiled initial state, not tick-zero events (spec 02 §2). W2-M4's contract has only wind events, so its fire must already be burning. |

## 23 Oct 2026 — engine gaps from the mission helpers, fixed

| Decision | Result |
|---|---|
| Relay hand-off | A vehicle keeps waiting at its pickup while group members ride another vehicle towards that pickup. A resident dropped at a non-refuge node waits there (`RStay`) until boarded or given a new group order. Members still walking keep the group's order (previously the whole group's order was cleared). |
| Cable sampling | A cable edge without an explicit `sample` checks only its two end stations, not the ground beneath it. |
| Closed-dock wait | Spec 03: cumulative ticks a loaded, stationary vehicle waits while its chosen unload destination is closed. Berth waits no longer count. |
| Dock queue | Also counts waiting at the dock for receiving capacity. |
| Finished skiffs | A skiff whose order is done moors aside: it frees its berth but stays at the node. It must reclaim a free berth before loading or unloading there again. A loaded boat is never blocked by an idle one. |
| Hard-closure admission | Spec 02 §6: no traveller enters an edge if a known hard closure lands on it before the predicted arrival. Known closures are a warned `set_edge_enabled(0)`, a published lava arrival, a running crust's expiry, and a warned or started runout segment. Predicted depth/heat changes are not "known"; transit recovery handles them. |
| Moored members (follow-up) | A resident moored at a transfer node is awaiting a new order. It no longer counts as heading to, or still to board at, another vehicle's pickup, so a loaded boat departs instead of waiting for it. |
