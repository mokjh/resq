# W5-M1 A Path Through Heat: design note

Generator: `tools/maps/w5_m1.py` (with `tools/maps/w5_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Introduce temporary cooled crossings." The north vent feeds a lava fork in the middle of the board.
- **Sectors.** The west arm separates the orchard terraces (north-west) from the depot. The east arm separates the research station (north-east) from the high landing. The lower east branch later cuts the coast road between the two refuges.
- **No early crossing.** The terraces sit high above the channels. The stair roads down to the crossing banks are long (orchard 800 ticks, research crew 1060), so nobody reaches a crossing before the lava does. Every crossing is therefore a cooled crossing: 50 ticks of cooling, then a 200-tick crust window.
- **Short routes.**
  - Crossing A (west arm, 2 slots, 40 ticks) for the orchard. Its cooler socket `cooler:01` is locked until the 210 s cooling opportunity.
  - Crossing B (east arm, 2 slots, 40 ticks) for the research crew (`cooler:02`).
  - Both banks are safe ledges.
- **Longer route.** Ridge paths (900 and 1000 ticks) lead to the fork causeway, a wide crust road (6 slots, 60 ticks) that `cooler:03` opens. Its west bank (`fork_shelter`) is bound to the hot fork and lies outside the safe zone: staging there needs the temporary shelter.
- **Critical route.** The critical objective `high_landing_path` (depot to high landing, ground) runs along the coast road. Its ford lies on the lower east branch (Hot at 140 s), so only the coast bridge keeps it open. `bridge:01` sits on the bank; `bridge:02` stands in the channel and is destroyed when the lava arrives.

## How the map expresses the threat sequence (Standard; S = scenario ticks)
- **60 s warning.** The `advance_lava` warning publishes the whole front:
  - vent at 700
  - fork at 750
  - both arms at 800
  - lower east branch at 1400
- **70–80 s.** The fork and the arms turn Hot and close every crossing. Groups that were sent early are still on their stair roads, and they stop at the safe banks (orchard at S=845, research crew at S=1105).
- **210 s.** `enable_cooler_socket` unlocks `cooler:01` (crossing A).
- **Cooling timeline.** A cooler fired at S=c closes the crossing until c+50. It is crusted (open) from c+50 to c+250, Reheating at c+250, and Hot again at c+300. The milestone `first_crossing_reheated` fires at the first Reheating of any segment of `first_channel`. Mastery ("all 12 before it") therefore needs everyone *evacuated* inside the first window that is used.
- **330 s.** `warn_reheat` is advisory only.

## Why A and B differ
- **A** follows the brief "Cool a crossing only when residents are ready to move."
  - At t=5 it places `bridge:01`, `cooler:01` and `cooler:02`.
  - It sends the orchard to the depot and the research crew to the high landing. Both groups wait on their safe banks.
  - At 210 s it cools crossings A and B together: crust 2151–2351.
  - Each group crosses in three pairs. The last residents are evacuated at S=2311, 40 ticks before the reheat. This holds mastery.
- **B** follows the brief "Stage residents in shelter, cool the longer route, then cross together."
  - It places `bridge:01`, `shelter:01` and `cooler:03`.
  - Both groups take the ridge paths to the fork. The orchard waits in the shelter on the hot west bank, from S=945; it used 151 of 600 protection. The research crew waits on the east ledge.
  - One cooler at S=1050 opens the wide causeway (crust 1100–1300). All twelve cross together to the depot; the last evacuation is at S=1240 (1280 in compact), before the reheat at 1300.
- **The difference.** They differ in route (two short crossings versus the shared long causeway), in timing (waiting for the 210 s socket versus crossing at 110 s), and in equipment (two coolers versus one cooler plus the shelter).

## A single major mistake
- **`mistake.json`: cooling too early wastes the safe window.** This is plan A, but crossing B is cooled at S=801, the moment the east arm turns Hot. The research crew is still on the stair road. Its crust window (851–1051) closes before the crew reaches the bank (S=1105). `cooler:02` is now empty and is not refilled, so the crew stays on the north bank. Result: **FAIL** in Standard and Command (wide and compact), with 6 of 12 rescued against a minimum of 9.
- **Relaxed: the mistake succeeds.** The scenario clock runs at half speed, so the crew reaches the bank (O=1105, S=552) before the lava (S=800) and crosses uncooled. The same operation-tick script is not the same decision in Relaxed, as noted for W1-M3. Result in Relaxed: success, 3★.
- **No shelter (B without `shelter:01`).** The orchard reaches the hot fork bank at S=947 and goes to AwaitingBackup at S=967. Result: **FAIL**, with 6 of 12 rescued.
- **`bridge:02` instead of `bridge:01` (plan A).** The bridge is destroyed at S=1400 and `high_landing_path` is missed. Result: success, 1★.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W5-M1`)
```
ok   W5-M1 wide    Standard no-input   -> success=False rescued=0
ok   W5-M1 wide    Standard strategy A -> stars=3 score=9438 rescued=12/12 backup=0 S=4200
ok   W5-M1 wide    Standard strategy B -> stars=3 score=9375 rescued=12/12 backup=0 S=4200
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake runs give `success=False stars=0 rescued=6/12` in Standard and Command (wide and compact), and `success=True stars=3` in Relaxed (wide and compact).

## Notes and open issues
- **Margins.**
  - A: last evacuation 40 ticks before the reheat (mastery).
  - B: 60 ticks before the reheat in wide and 20 in compact, because the depot takes four at a time there.
  - Shelter: 449 of 600 protection left.
- **Relaxed.** Both reference plans cross before the lava (the stairs are measured in operation ticks), so Relaxed teaches nothing about cooling. That is inherent to a half-speed scenario clock.
- **Compact.** `class_overrides.compact` swaps the receiving capacities: depot 8 / high landing 4 in wide, 4 / 8 in compact. A resident leaves receiving occupancy when evacuated, so this only throttles simultaneous arrivals (B is 40 ticks slower). The recipe's "longer outer approach for the primary dock" is not modelled, because this mission has no docks.
- **Recipe deviations.**
  - Stair approaches (800–1060 ticks) are far longer than the recipe's 300/600 short/long legs. This is deliberate: it stops a "walk out before the lava" solution.
  - Non-crossing roads use 6 slots so a group moves as a unit.
  - The recipe's generic roles (`receiving_dock`, `sea_route`, `final_ferry` and others) are not bound. Only the contract's event roles are bound (`first_channel`, `crossing_a`).
- **Beacons.** Beacon sockets exist, but beacons have no rules effect, so neither plan spends on them.
