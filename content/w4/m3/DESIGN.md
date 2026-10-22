# W4-M3 The Floating Market: design note

Generator: `tools/maps/w4_m3.py` (kit `tools/maps/w4_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Handle multiple pickups and staging capacity."
- **Four markets of five** surround a safe harbor island. It has two receiving docks, each with **one berth**: the receiving dock (depot anchor, capacity 12) and the high landing (capacity 8). Refuge totals exactly 20.
- **Three skiffs carry 4 each.** Every market needs two trips (4 + 1), so the fleet must share two berths without a queue at the receiving dock. The 3★ condition is `max_dock_queue_operation_ticks ≤ 50` at `receiving_dock`.
- **Berths are reserved at departure**, so two hulls can never overlap. A boat that cannot get its berth waits at its last node, which makes the queue readable.
- **A boat that has finished its group stays moored in its last berth** (engine behaviour; see the notes). Plans must therefore stand a finished boat off, or give each dock one last user.

**Water routes:**
- the harbor junction links the receiving dock (60), the high landing (140), the west market (160) and the south market's high dock (80);
- `pontoon_channel`: harbor ↔ north market (120). A stall pontoon blocks it at 170 s;
- `tidal_lane`: harbor ↔ east market (140). Closed until 50 s;
- `outer_loop`: west → north → east → high landing (240 / 240 / 200). From 300 s the current makes it 1.5× faster.

**Ground.** The south market sits on a rock beside the harbor island, across a breach (`bridge:01`). Once bridged, the south market walks home, and its **high dock becomes a transfer point**: a non-refuge dock of capacity 6. Residents unloaded there have to be walked on over the bridge. A second, useless breach (`bridge:02`, a broken footway between the west market and the rock) is a decoy for the single bridge in stock.

## Threat sequence (scenario ticks, Standard)
This contract has **no tide or water events**, so the sea holds its 150 mm datum throughout. The events are route changes:
- **S 500 (50 s):** the tidal lane opens.
- **S 1700 (170 s):** the pontoon channel closes. After this the north market can only be reached by the outer loop (west or east).
- **S 3000 (300 s):** outer-loop travel takes `ceil(t × 1000 / 1500)`.

## Why A and B differ
- **A** follows "Stagger arrivals through the two receiving docks."
  - The south skiff lands both south-market loads at the receiving dock first (S 240–576), then is ordered to stand off in the harbor at t=600.
  - The depot skiff is placed only once that berth is free (t=360). It lands the west market (S 855–1351), then the north market (S 1814–2750; its second trip goes round the outer loop after the pontoon closes).
  - The high-landing skiff lands the east market at the high landing (S 462–918).
  - The receiving dock never sees two boats at once, so the dock queue is **0**.
- **B** follows "Evacuate one island fully, then reuse its high dock as a transfer point."
  - The bridge closes the south breach, and the whole south market walks home (S 226–506).
  - The depot skiff drops the north market at the emptied south high dock, `evacuate: false`, two trips through the pontoon channel before 170 s. At t=2500 the north market is walked on over the bridge (S 2720–3000). The group order has to come after the group's last unload, because each unload at a non-refuge dock clears that group's route.
  - The west-pier skiff lands the west market at the receiving dock (S 324–820). The high-landing skiff lands the east market at the high landing (S 466–922).
  - Each dock has a single boat user, so the receiving-dock queue is **0**.

They differ in route (all-water versus the bridge and a ground transfer), in equipment (3 skiffs versus 3 skiffs plus the bridge) and in evacuation priority (south first by boat versus the south island emptied on foot).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": "assigning all boats to one dock creates a readable queue, not overlapping models."** This is plan A's boats, but every boat is sent to the receiving dock and none is held back or stood off.
  - The boats line up for the single berth: each waits at the harbor junction or its pickup with its berth reservation refused, and two hulls never share the dock.
  - The south skiff finishes first and **stays moored in the berth**. The loaded east and west skiffs wait behind it for the rest of the operation, and the north market is never collected.
  - Result: **FAIL** in all six configurations, with 5 of 20 rescued, loaded skiffs at the end and the dock queue far above 50.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W4-M3`)
```
ok   W4-M3 wide    Standard no-input   -> success=False rescued=0
ok   W4-M3 wide    Standard strategy A -> stars=3 score=9429 rescued=20/20 backup=0 S=4800
ok   W4-M3 wide    Standard strategy B -> stars=3 score=9286 rescued=20/20 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W4-M3 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=5/20`.

Queue margin check (a temporary `contract_overrides.json` that tightened the mastery maximum to 0, 10 and 25, then removed): A and B met all three thresholds, and the mistake missed all three. The shipped map has no override.

## Compact layout
`class_overrides.compact` swaps receiving capacity (receiving dock 12 → 8, high landing 8 → 12) and lengthens the high landing's approach (`w_landing_harbor` 140 → 200 ticks). Both plans pass unchanged at 3★.

An earlier A, which re-routed the south skiff mid-trip, lost a resident in compact because the longer approach shifted its timing. The shipped A uses only timings that the compact change cannot reach: its receiving-dock windows use routes the override does not touch.

## Margins and notes
- **Timing robustness.** No early event changes A's or B's routes (the tidal lane is never the shortest path for them), so their operation-tick timings are identical in all three profiles until S 1700. The stand-off at t=600 comes 24 ticks after the south skiff's last unload (S 576). The depot skiff reserves the berth about 250 ticks after the stand-off. B's walk order at t=2500 comes more than 1000 ticks after the north market's last drop.
- **Critical (`receiving_harbor` operational).** The infrastructure is the harbor quay plus both receiving docks (demand 0, as the spec's harbor default). With no water events nothing in this contract can threaten it, so the critical objective holds for any plan. That is a property of the frozen contract and worth a look at the next design review.
- **Barriers.** The kit includes 2 barriers and the recipe asks for 3 barrier sockets. They are authored as harbor sea-wall sockets, but with no tide or source events the sea never moves, so they have nothing to hold back and no strategy spends on them. Beacons likewise have no rules effect.
- **Anchors.** The recipe binds `north_market` to the west anchor and `west_market` to the upper anchor. The map places markets by name instead (north market at the upper anchor, west market at the west anchor); the east and south (orchard) anchors are kept.
- **Engine behaviour to note.** A skiff whose group is complete stays in its berth indefinitely, and the spec does not say whether an idle vehicle vacates. The mistake's deadlock and A's stand-off order both rest on this. See the report to the coordinator.
