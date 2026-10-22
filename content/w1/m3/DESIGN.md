# W1-M3 Two Banks: design note

Generator: `tools/maps/w1_m3.py`, which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Allocate transport capacity between separated groups." The main river splits the board.
- **West village (8).** It sits on a raised terrace on the west bank. It has three ways out: a narrow single-file low footpath to the depot (about 300 ticks), the long west high road to the depot (600 ticks, with a ravine gap for `bridge:01`), and a high footbridge over the river (`bridge:02`) to the east-bank strip path and the east landing (about 520 ticks). It can also board at the west dock.
- **East workshop (6).** It stands beyond the east lane, a side channel that leaves the river at the fork, and can leave **only by boat**. Boats run down the inner lane (the main river) to the depot, or down the east lane to the east landing. A weir at the foot of the east lane stops boats cutting across.
- **The constraint.** There are two skiffs (4 seats each) and one bridge. The workshop must be emptied (required and critical). Mastery needs at least 4 west villagers to finish on foot. Both bridge sockets join only high paths; neither joins the docks.

## How the map expresses the threat sequence
- **70 s, central surge (`central_basin` source set to 60 M mm³/s).** The small gully head beside the footpath fills in seconds. It tops two 300 mm lips, and the footpath dips under them. The dip floods past 100 mm at about 86 s, which closes `g_foot_b` and `g_foot_c`. Walkers already on that leg turn back. Late walkers can be caught in the flooded western footpath and need backup. With paired barriers on the lips, the gully head rises instead to the 900 mm stone spillway and drains to the river, so the footpath stays open. The barriers never strain, because the head difference stays at or below 600 mm.
- **170 s warning, then 180 s debris (`set_edge_enabled boat_lane_inner = 0`).** The inner lane closes. From then on **the depot cannot be reached by boat**, and only the east lane to the east landing remains.
- **320 s, the east lane current quickens (`set_current_multiplier east_lane 1500`).** The 400-tick east-lane leg becomes 267 ticks, so the east landing is the shorter remaining boat route.

## Why A and B differ
- **A** follows the brief "Bridge the high road for western pedestrians; use both boats on eastern and overflow groups." It places `bridge:01` (the ravine) first and sends the whole west village on foot to the depot. Five slip down the footpath before the surge closes it, and the other three re-route over the bridged high road. `skiff:01` (from the depot) and `skiff:02` (from the east landing) both serve the workshop. One delivers 4 to the depot before the debris closes the inner lane, and the other takes the remaining 2 down the east lane. Result: 8 west villagers finish on foot.
- **B** follows the brief "Evacuate east early with one skiff, route the other west, then bridge the final safe path." `skiff:01` evacuates the workshop early, making two trips to the depot (4, then 2, done by 134 s). `skiff:03` takes 4 west villagers from the west dock. At t=600 `bridge:02` (the river footbridge) is placed, and the last 4 villagers walk the high path to the east landing. Result: 4 finish on foot.

They differ in bridge socket and destination (ravine high road to the depot versus river footbridge to the east landing), in boat allocation (both boats east versus one east and one west), and in which groups go first.

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate".** One skiff clears the west dock in two trips. At t=1700 it is sent for the workshop with the depot still as its destination. The debris closes the inner lane at 180 s. The loaded skiff then waits at the safe workshop dock because it has no legal route to the depot, and it is never reassigned to the east landing. Result in Standard and Command (wide and compact): **FAIL**, with 8 of 14 rescued, a loaded skiff stranded, the workshop not emptied (critical), and no west villager on foot (mastery).
  - In Relaxed the same operation-tick script fires before the closure at scenario time, so it is not the same decision. That run succeeds with only 2★, because mastery is missed.
- **Walking the footpath late.** Here the west village is ordered out at t=650 with no bridge. The surge reverses the walkers and floods the western footpath, and 2 villagers need backup. Result: **FAIL**, with 6 of 14 rescued.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W1-M3`)
```
ok   W1-M3 wide    Standard no-input   -> success=False rescued=0
ok   W1-M3 wide    Standard strategy A -> stars=3 score=9353 rescued=14/14 backup=0 S=4800
ok   W1-M3 wide    Standard strategy B -> stars=3 score=9353 rescued=14/14 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: Standard and Command (both layouts) give `success=False rescued=8/14`. Relaxed (both layouts) gives `success=True stars=2`, as explained above.

## Notes and open issues
- A strong plan finishes by about 190 s, so the 320 s current event mainly matters for slower or single-skiff plans. The 180 s debris is the decisive late event.
- Wide and compact are currently the same graph (no `class_overrides`), as in W1-M1.
- `barrier:03` sits on the spillway. Sealing it together with one lip only sends the surge over the other lip, which is harmless but useless. It exists to meet the recipe socket count.
- The engine reroutes vehicles automatically over open edges, so "a boat waits at a safe node" happens only when no legal route to its destination remains. That is exactly the depot-after-debris case.
