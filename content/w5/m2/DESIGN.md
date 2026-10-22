# W5-M2 Borrowed Safety: design note

Generator: `tools/maps/w5_m2.py` (with `tools/maps/w5_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Use refuge as a temporary logistics tool, not a destination." One lava arm (`assembly_channel`) runs east to west across the board and cuts both villages off from the depot.
- **The only crossing.** A crust stair on the arm: 4 slots, 190 ticks.
  - It turns Hot at 60 s, before anyone can walk down to it (the bank roads are 600 ticks).
  - A window lets four walkers start across, or the crawler (2 slots, 6 seats) and two walkers.
  - A second window on the same segment needs it to reheat first, 300 ticks after cooling.
  - Two coolers plus the store's 2 charges give four windows in total.
- **The north bank.** A tiny safe ledge (3 people) with two shelter sites beside it:
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
  - **The crawler.** It is placed at the depot and crosses the arm before the lava, at S=70. It waits at `w_shelter` while the orchard comes down, then loads six (S=640–700).
  - **The village.** It stages at once in the quarry hut and track.
  - **The cooling windows.** The single cooler `cooler:01` is cooled three times (initial charge plus two refills):
    - S=706: the crawler takes six across.
    - S=1070: the crawler returns empty.
    - S=1395: the crawler takes the last three orchard residents. The orchard is out by S=1747.
  - **The outer route.** At 320 s the village walks it; the last evacuation is at S=4111.
  - **Cost.** No shelter equipment is needed, and A spends 7 supplies.
- **B** follows the brief "Use both shelters briefly and alternate transport pickups before heat peaks."
  - **The crawler.** It is placed on the south bank and crosses first.
  - **Staging.** The orchard fills the ledge and its shelter; the village fills `e_shelter` (shelter placed).
  - **The cooling windows.** Two coolers, each refilled once, give four windows (S=700, 1010, 1320 and 1630):
    - Window 1: the crawler takes six villagers, entering behind the first orchard walkers.
    - Windows 1–2: the orchard walks across in pairs.
    - Windows 3–4: the last villagers walk out. They waited in `e_shelter` through the village heat.
  - **Protection used.** `e_shelter` used 529 of its 600 units. Everyone is out by S=2020.
- **The difference.** They differ in transport (the crawler moves the orchard versus the crawler moves the village), shelter use (none versus both sites), routes (the outer route versus four crossing windows) and coolant (one cooler versus two).

## A single major mistake
- **`mistake.json`: leaving residents in shelters fails the evacuation condition even if they remain safe.** This is plan A, but the village is never sent on along the outer route when it opens. The quarry keeps them perfectly safe to the end, but six residents are still in a temporary shelter and three are on the quarry track. Result: **FAIL** in all six configurations, with 9 of 18 rescued. `rescue_minimum`, `temporary_shelters_empty` (required) and the critical objective are all missed.
- **No shelter (B without its shelters).** The last three villagers reach `e_shelter` after the village heat has started and go to AwaitingBackup at S=1240–1262. Result: success, 1★ (15 of 18). This shows that B really borrows the shelter's safety.
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
- **Thin margin.** In B (Standard), `e_shelter` ends with 71 of 600 protection units, so mastery (`shelter_exhaustions = 0`) has 71 pulses of slack. Do not lengthen the crossing or move the village lobe earlier without re-running B. In Relaxed, B never needs the protection (600 left).
- **Waiting rule.** Residents only start along a path when the whole path is open. Groups are therefore staged with non-evacuating orders (to `w_shelter`, `e_shelter`, the quarry) and released with an evacuation order when a window opens.
- **Compact.** `class_overrides.compact` swaps the receiving capacities: depot 12 / high landing 6 in wide, 6 / 12 in compact. Evacuated residents leave occupancy, so results are identical. The recipe's longer outer approach for the primary dock is not modelled (no docks in this mission).
- **Recipe deviations.**
  - A single crossing replaces the recipe's per-group corridors.
  - The bank roads (580 ticks) are longer than the 300-tick short leg, so that no one can cross before the lava.
  - Generic recipe roles are not bound; only the contract's event roles are.
- **Beacons.** No rules effect; unused.
