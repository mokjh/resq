# W4-M3 The Floating Market: design note

Generator: `tools/maps/w4_m3.py` (kit `tools/maps/w4_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

Retuned after engine commit a02c558, in which finished skiffs moor aside, the relay hand-off rules changed and known-closure admission was added. The first version's mistake relied on a finished boat blocking the only berth, and that no longer happens. Its B lost one resident to the new hand-off rule.

## Central decision
"Handle multiple pickups and staging capacity."
- **Four markets of five** surround a safe harbor island. It has two receiving docks, each with **one berth**: the receiving dock (depot anchor, capacity 12) and the high landing (capacity 8). Refuge totals exactly 20.
- **Three skiffs carry 4 each,** so every market needs two trips (4 + 1).
- **The receiving dock is the slow, threatened dock.** It lies at the head of the long marked **pontoon channel**, 250 ticks from the harbor junction. A boat reserves the berth when it sets off up the channel and keeps it until it is back at the harbor. Each visit therefore holds the berth for about 540 ticks, while a high-landing visit holds it for about 320.
- **At 170 s a stall pontoon blocks that channel** (`set_edge_enabled(0)`, warned at 160 s). From then on the receiving dock takes walkers only, and boats can use only the high landing. Under the spec's known-closure admission rule, no boat sets off up the channel if the closure would land before it arrives.
- **The 3★ condition** is `max_dock_queue_operation_ticks ≤ 50` at `receiving_dock`.

**Water routes:**
- the harbor junction links the high landing (140), the west market (160), the north market (`w_north_channel`, 120) and the south market's high dock (80);
- `tidal_lane`: harbor ↔ east market (140). Closed until 50 s;
- `outer_loop`: west → north → east → high landing (240 / 240 / 200). 1.5× faster from 300 s.

**Ground.** The south market sits on a rock beside the harbor island, across a breach (`bridge:01`). Once bridged, the south market walks home, and its **high dock becomes a transfer point**: a non-refuge staging dock of capacity 6. A resident dropped there waits until given a new group order and then walks on over the bridge. A second, useless breach (`bridge:02`, a broken footway from the west market) is a decoy for the single bridge.

## Threat sequence (scenario ticks, Standard)
There are no tide or water events in this contract (the sea holds its 150 mm datum); every event is a route change:
- **S 500:** the tidal lane opens.
- **S 1600:** warning, then **S 1700:** the pontoon channel to the receiving dock is blocked.
- **S 3000:** the outer loop gets faster.

## Why A and B differ
- **A** follows "Stagger arrivals through the two receiving docks."
  - The south skiff lands both south-market loads at the receiving dock (S 430–1146), well before the channel closes.
  - The east and west skiffs land at the high landing from the start (S 462–1232 and S 847–1503).
  - Once free (t=1200), the south skiff lifts the north market to the high landing too (S 1884–2460).
  - Only one boat ever uses the receiving dock, so its queue is **0**, and nothing is still bound for it at 170 s.
- **B** follows "Evacuate one island fully, then reuse its high dock as a transfer point."
  - The bridge closes the south breach, and the whole south market walks home (S 226–506).
  - The south skiff drops the north market at the emptied south high dock. Each drop is walked on over the bridge: an order at t=600 after the first four, and another at t=1300 after the last one (S 820–1520).
  - The west and east skiffs land at the high landing (S 404–1174 and S 789–1386).
  - No boat ever enters the pontoon channel, so the receiving-dock queue is **0**.

They differ in route (all-water versus the bridge and a ground transfer), in equipment (3 skiffs versus 3 skiffs plus the bridge) and in which docks carry the load (the receiving dock by water early versus the receiving dock on foot only).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": "assigning all boats to one dock creates a readable queue, not overlapping models."** This is plan A's boats, but every boat is sent to the receiving dock.
  - Its berth is held for each boat's whole run up and down the pontoon channel. The boats form a readable queue at the harbor junction: berths are reserved before a boat sets off, so hulls never overlap.
  - At 170 s the stall pontoon blocks the channel. The boats still in the queue are already loaded and can never reach the dock, and the north market is left waiting at its pickup.
  - Standard and Command: 9 rescued, 9 people stuck aboard. Relaxed (the closure comes at operation tick 3400): the queue nearly clears, but the north market's last boat is still queued when the channel closes, so 19 are rescued and one occupied skiff remains.
  - Result: **FAIL** in all six configurations: `all_vehicles_unloaded` (required) is missed everywhere, `rescue_minimum` is also missed in Standard and Command, and mastery (dock queue) is missed everywhere.
  - The failure follows from published rules only: one berth per dock, berth reservation at departure, and a warned channel closure. It no longer relies on idle boats blocking a berth.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W4-M3`, after a02c558)
```
ok   W4-M3 wide    Standard no-input   -> success=False rescued=0
ok   W4-M3 wide    Standard strategy A -> stars=3 score=9429 rescued=20/20 backup=0 S=4800
ok   W4-M3 wide    Standard strategy B -> stars=3 score=9286 rescued=20/20 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake:
- `RESULT W4-M3 {wide,compact} {Standard,Command} mistake: success=False stars=0 rescued=9/20`
- `RESULT W4-M3 {wide,compact} Relaxed mistake: success=False stars=0 rescued=19/20` (one loaded skiff left)

Queue check (a temporary override tightening the mastery maximum to 0, then removed): A and B both meet it in wide and compact. Their receiving-dock queue is 0.

## Compact layout
`class_overrides.compact` swaps receiving capacity (receiving dock 12 → 8, high landing 8 → 12) and lengthens the high landing's approach (`w_landing_harbor` 140 → 200 ticks). Both plans pass unchanged at 3★.

## Margins and notes
- **Thinnest margin: the mistake in Relaxed.** It fails by one boatload: the last north-market boat is still queued when the channel closes at operation tick 3400. Shortening the pontoon channel below about 250 ticks would let the Relaxed mistake clear its queue.
- A's last receiving-dock visit ends about 550 ticks before the closure (Standard). B never uses the channel.
- **B's transfer orders must follow the drops.** A resident dropped at a non-refuge dock waits there until a new group order. Also, while some members are parked at the transfer dock, the boat waiting at the group's pickup still counts them as heading to that pickup (see the engine note in the report). So B gives a walk order right after the first four are dropped, which clears that, and another after the last drop. With a single late order (the pre-a02c558 B), the boat holding the fifth resident waited at the north pickup until that order came, and the fifth resident was never walked home: 19/20.
- **Critical (`receiving_harbor` operational)** is the harbor quay plus both receiving docks (demand 0). Nothing in this contract can threaten it, and that is worth a look at the next design review. The blocked channel closes a route, not the dock.
- **Barriers.** The recipe asks for 3 barrier sockets, authored as harbor sea walls. With no tide or source events they have nothing to hold back, and no strategy spends on them. Beacons have no rules effect.
- **Anchors.** Markets are placed by name: north market at the upper anchor and west market at the west anchor, swapped relative to the recipe's bindings. The east and south anchors are kept.
