# W5-M4 Three Ways Out: design note

Generator: `tools/maps/w5_m4.py` (with `tools/maps/w5_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Choose among sea, road, and temporary crossings." The land channel runs north to south down the west of the board.
- **Sectors.** West of the channel are the research crew (north) and the inland orchard (south). East of it are the depot and the high landing. The coast village sits on the east shore. Each group has its own way out, and all of them share one-berth landings.
- **Road (research crew; required and critical, all 8).** The short land route crosses a ravine and then the land channel at crossing A (2 slots, 60 ticks). The ravine needs the bridge (`bridge:01`). Without it the crew goes round (+1560 ticks), arrives after the lava and needs a cooled crossing.
- **Sea (coast village).** Only by skiff from its two-berth dock, round the reef to the outer buoy and on to either landing. Buoys are open water, with no berth.
- **Crawler and cooling, or sea (inland orchard).**
  - The long track (2000 ticks) reaches crossing B after the lava. Crossing B is a wide crust road (4 slots). Its cooler sockets (`crossing_b`: `cooler:02`, `cooler:03`) are locked until the final cooling window at 440 s.
  - The alternative is the orchard's tiny pier (one berth, 4 people) with a shelter beside it. The pier's channel to the south buoy is blocked by debris until 330 s (`sea_route`).
- **Landings.** The high landing and depot each have one berth. A skiff whose job is done stays docked and keeps the berth.

## How the map expresses the threat sequence (Standard)
- **70 s.** `warn_route` on `short_land_route` (crossing A) is advisory.
- **200 s.** The land channel turns Hot and closes crossings A and B.
- **300 s.** The inland lobe makes the orchard terraces and the pier shelter unsafe.
- **330 s.** The pier channel opens (`set_edge_enabled`).
- **440 s.** Crossing B's cooler sockets unlock (`enable_cooler_socket`).

## Why A and B differ
- **A** follows the brief "Ferry the coast early, then use crawler and cooling inland."
  - **Research crew.** `bridge:01` gets them across crossing A by S=1500, before the lava.
  - **Coast.** One skiff is launched at the coast dock and one comes from the high landing. They split the coast village, four each, between the two landings; the coast is out by S=1449.
  - **Inland.** The crawler is placed at the orchard. It loads six; the last two walk behind it down the long track. They wait at crossing B's safe bank from S≈2100. At 440 s `cooler:02` opens the crossing: the crawler and the two walkers cross in one entry, and the orchard is out by S=4716.
- **B** follows the brief "Use shelter to stage inland groups while boats clear the receiving dock."
  - **Research and coast.** Same research and coast moves as A.
  - **Staging at the pier.** The orchard stages at its pier: four on the pier and four in the pier shelter, which turns hazardous at 300 s.
  - **The pickup.** When the pier channel clears (330 s), each skiff takes four from the pier back to its *own* landing, which is still free because nothing else is waiting there. The orchard is out by S=4031.
  - **Protection used.** 464 of the shelter's 600 units.
- **The difference.** They differ in the inland group's mode (ground: crawler and cooled crossing, versus water: shelter and boats), in equipment (crawler and cooler versus shelter), and in timing (the final cooling window versus the debris clearing).
- **Mastery.** Both reach 3★, with at least one ground evacuation and at least one water evacuation.

## A single major mistake
- **`mistake.json`: overloading one final landing creates a queue.** This is plan A, but both coast skiffs are sent to the high landing.
  - `skiff:03` unloads the first four and, its job done, stays on the landing's only berth.
  - `skiff:02`, loaded with the last four coast residents, queues at the outer buoy for the rest of the operation; its state stays Waiting/ToDestination to the end, as the vehicle trace shows.
  - Everyone else is safe, but a vehicle still holds passengers at the end.
  - Result: **FAIL** in all six configurations, with 20 of 24 rescued and `all_vehicles_unloaded` failed.
- **No shelter (B without `shelter:01`).** Four orchard residents in the pier shelter go to AwaitingBackup at S=3019. Result: success, 1★ (20 of 24).
- **No bridge (A without `bridge:01`).** The research crew reaches crossing A after the lava and has no cooler there. Result: **FAIL**, with 16 of 24 rescued and the required crew objective missed.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W5-M4`)
```
ok   W5-M4 wide    Standard no-input   -> success=False rescued=0
ok   W5-M4 wide    Standard strategy A -> stars=3 score=9333 rescued=24/24 backup=0 S=5400
ok   W5-M4 wide    Standard strategy B -> stars=3 score=9444 rescued=24/24 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W5-M4 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=20/24` (loaded skiff at the end).

## Notes and open issues
- **Berths and deadlock.** Berths are reserved at departure, and the origin berth is kept until arrival. A skiff that is waiting at a dock node holds that dock's berth. Water junctions between docks are therefore open-water buoys (`outer_buoy`, `south_buoy`). An earlier draft routed one skiff *through* the depot dock to reach the pier: both skiffs then waited on each other's berth (a deadlock). Routing through buoys removes it.
- **Pickup behaviour.** A skiff with a pickup group keeps shuttling until every member has boarded. A berth is only held for good once the group is fully aboard. That is why the mistake needs the two coast skiffs to load in parallel (the two-berth coast dock).
- **Margins.**
  - B's shelter ends with 136 of 600 protection (218 in Relaxed).
  - A's crawler waits about 2300 ticks at crossing B. That bank is safe, and the staged group is the point of the final window.
- **Compact.** `class_overrides.compact` swaps the receiving capacities: depot 16 / high landing 8 in wide, 8 / 16 in compact. Results are identical: berth count, not pedestrian capacity, is what limits a landing.
- **Recipe deviations.** Bank roads and the long track are long, so that the lava (not the walking speed) closes the crossings. The coast dock has two berths. The `alternate_pickup` landing is not authored.
