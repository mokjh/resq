# W5-M2 Borrowed Safety: design note

Generator: `tools/maps/w5_m2.py` (with `tools/maps/w5_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Use refuge as a temporary logistics tool, not a destination." One lava arm (`assembly_channel`) runs east to west across the board and cuts both villages off from the depot.
- **The only crossing.** A crust stair on the arm: 4 slots, 170 ticks.
  - It turns Hot at 60 s, before anyone can walk down to it (the bank roads are 600 ticks).
  - **Admission (engine a02c558, spec 02 §6).** Nobody starts a crossing that the crust's known expiry (cool start + 250) would close before they arrive. A traveller must therefore enter within 29 ticks of the crust opening, at c+50 to c+79.
  - A window lets one wave start across: four walkers from the ledge, or the crawler (2 slots, 6 seats) plus two walkers. The crawler waits beside the ledge and reaches the stair at c+70.
  - The south-bank road to the depot is short (10 ticks), so a returning crawler reaches the stair by c+60.
  - A second window on the same segment needs it to reheat first, 300 ticks after cooling.
  - Two coolers plus the store's 2 charges give four windows in total.
- **The north bank.** A tiny safe ledge (4 people) with two shelter sites beside it:
  - `w_shelter` (`shelter_a`) is safe ground until its external front starts at 180 s.
  - `e_shelter` lies inside the village heat zone and is unsafe from 100 s.
- **The quarry hut.** Far behind the village, out of the heat. The hut (6 places, shelter-flagged) and its track (3 places) are safe for the whole mission, but a shelter is never a refuge. Residents there must still leave by the outer route round the east headland, which is closed until 320 s.

## How the map expresses the threat sequence (Standard)
- **60 s.** The arm turns Hot and the crossing closes. The warning at 50 s publishes the whole front:
  - orchard terraces at 70 s
  - village terraces and `e_shelter` at 100 s
- **70 s.** The orchard terraces become unsafe. The orchard must already be at its shelter or on the ledge.
- **100 s.** The village terraces become unsafe. Anyone waiting in `e_shelter` from now on uses protection.
- **180 s.** `enable_shelter_hazard` starts `shelter_a`'s front; nobody is still there in either plan.
- **320 s.** `set_edge_enabled` opens the outer route.

## Why A and B differ
- **A** follows the brief "Stage one group, evacuate the other, then return along the cooled route."
  - **The crawler.** It is placed at the depot and crosses the arm before the lava, at S=20. It waits at `w_shelter` while the orchard comes down, then loads six (S=640–700).
  - **The village.** It stages at once in the quarry hut and track.
  - **The cooling windows.** The single cooler `cooler:01` is cooled three times (initial charge plus two refills):
    - S=706: the crawler takes six across.
    - S=1070: the crawler returns empty.
    - S=1395: the crawler takes the last three orchard residents. The orchard is out by S=1677.
  - **The outer route.** At 320 s the village walks it; the last evacuation is at S=4111.
  - **Cost.** No shelter equipment is needed, and A spends 7 supplies.
- **B** follows the brief "Use both shelters briefly and alternate transport pickups before heat peaks."
  - **The crawler.** It is placed on the south bank and crosses first.
  - **Staging.** The orchard fills the ledge and its shelter; the village fills `e_shelter` (shelter placed).
  - **The cooling windows.** Two coolers, each refilled once, give four windows (cooled at S=700, 1010, 1320 and 1630). The group orders give each window to one wave:
    - Window 1: the crawler takes six villagers, and the orchard is released at t=771 so two orchard walkers fill the free slots. The crawler is then parked at the depot (t=1021) so it never takes slots again.
    - Window 2: four orchard residents. The orchard is re-staged on the ledge at t=960 and released at t=1011.
    - Window 3: the last three villagers, who waited in `e_shelter` through the village heat. The orchard is held back in its shelter at t=1250 so the ledge is free.
    - Window 4: the last three orchard residents, before `shelter_a`'s front at 180 s.
  - **Protection used.** `e_shelter` used 149 of its 600 units. Everyone is out by S=1880.
  - **Relaxed copy.** Steps t=3690–3702 (a refill, one more cooling, and both groups released) only matter in the Relaxed clock, where the Standard schedule falls out of step with the crust windows. In Standard everyone is already out.
- **The difference.** They differ in transport (the crawler moves the orchard versus the crawler moves the village), shelter use (none versus both sites), routes (the outer route versus four crossing windows) and coolant (one cooler versus two).

## A single major mistake
- **`mistake.json`: leaving residents in shelters fails the evacuation condition even if they remain safe.** This is plan A, but the village is never sent on along the outer route when it opens. The quarry keeps them perfectly safe to the end, but six residents are still in a temporary shelter and three are on the quarry track. Result: **FAIL** in all six configurations, with 9 of 18 rescued. `rescue_minimum`, `temporary_shelters_empty` (required) and the critical objective are all missed.
- **No shelter (B without its shelters).** The last three villagers reach `e_shelter` after the village heat has started and go to AwaitingBackup at S=1240–1262. Result: success, 1★ (15 of 18). This shows that B really borrows the shelter's safety.
- **Before the a02c558 retune.** With a 190-tick stair, a 60-tick depot road and a 3-place ledge, the new admission rule left a loaded crawler at the ledge and stranded walkers. Results then were A 1★ (15/18) and B 2★ (17/18).
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W5-M2`)
```
ok   W5-M2 wide    Standard no-input   -> success=False rescued=0
ok   W5-M2 wide    Standard strategy A -> stars=3 score=9682 rescued=18/18 backup=0 S=4800
ok   W5-M2 wide    Standard strategy B -> stars=3 score=9182 rescued=18/18 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W5-M2 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=9/18`.

## Notes and open issues
- **Margins.** In B (Standard), `e_shelter` ends with 451 of 600 protection and `w_shelter` is not used. In Relaxed, `w_shelter` ends with 414 of 600 (its front comes during the Relaxed tail). Under the admission rule each window has a 29-tick entry slot (c+50 to c+79): the crawler enters at c+70 and walkers at c+50 to c+71. Do not lengthen the stair (170) or the ledge-to-shelter links (20) without re-running B.
- **Waiting rule.** Residents only start along a path when the whole path is open. Groups are therefore staged with non-evacuating orders (to `w_shelter`, `e_shelter`, the quarry) and released with an evacuation order when a window opens.
- **Compact.** `class_overrides.compact` swaps the receiving capacities: depot 12 / high landing 6 in wide, 6 / 12 in compact. Evacuated residents leave occupancy, so results are identical. The recipe's longer outer approach for the primary dock is not modelled (no docks in this mission).
- **Recipe deviations.**
  - A single crossing replaces the recipe's per-group corridors.
  - The bank roads (580 ticks) are longer than the 300-tick short leg, so that no one can cross before the lava.
  - Generic recipe roles are not bound; only the contract's event roles are.
- **Beacons.** No rules effect; unused.
