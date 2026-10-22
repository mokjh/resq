# W5-M3 The Cooling Works: design note

Generator: `tools/maps/w5_m3.py` (with `tools/maps/w5_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Protect coolant logistics and power." The cooling works (infrastructure `cooling_works`) stands on the west basalt shelf. It needs 1 unit of power at priority 40 and is a producer: 6 feedstock units, store `coolant` (3 charges, capacity 8), one charge per 100 powered ticks. The required and critical objective is that it is operational at the end. Mastery requires at least one charge left in the store.
- **Drainage.** The plant's drainage cutting (the `drainage_basin`, 6 cells) starts flooded at 700 mm. A spring refills it from 50 s at 30 M mm³/s. Pumps drain it into a sea outfall.
  - With no pump, it overtops its 900 mm platform at about S=1100.
  - With one pump, at about S=3200.
  - With two pumps, it falls to about 100 mm (passable) by S≈2140.
  - The short route crosses the cutting on a causeway (passable at 100 mm or less), then the delivery channel at crossing S.
- **Power.** Everything shares the works grid. One generator gives 2 units. Pumps (priority 20) are served before the works (40), so while two pumps run the works is starved and makes no coolant.
- **Generator ground.**
  - `generator:01` on the cutting platform, which is dry only while the cutting is held down.
  - `generator:02` on the dry ridge, which reaches the works grid only through a relay. `relay:01` sits on the same platform. `relay:02` sits beside the delivery channel, where the 170 s arrival destroys it.
- **Routes.**
  - The long mountain path (3100 ticks, 8 slots) climbs north round both vents and comes down to crossing L on the second channel.
  - The village and the research station have short routes that they finish long before any lava (S≤651).
  - Both crossings are narrow (2 slots, 100 ticks), so a crust window passes four walkers.

## How the map expresses the threat sequence (Standard)
- **50 s.** The spring starts (`set_source_rate`).
- **170 s.** The delivery channel (`coolant_delivery`) turns Hot, closing crossing S. Its heat makes the plant terraces and `works_shelter` unsafe, so crew still there need the plant shelter (6 places; 2 more fit in the control room `works_yard`). `relay:02` is broken on arrival.
- **310 s.** The second channel turns Hot and closes crossing L.

## Why A and B differ
- **A** follows the brief "Secure drainage and power first, then cool the short route."
  - **Drainage and power.** At t=5 it places `generator:01` on the cutting platform and both pumps (the works waits for power).
  - **Staging.** The plant crew stages in the shelter (6) and the control room (2) and sits out the heat from S=1700. They used 452 of 600 protection.
  - **The crossing.** Once the causeway is passable (S≈2140), the crew moves to the crossing bank. Crossing S is cooled twice, at S=2240 and 2545 (one refill from the store). The crew is out by S=2895.
  - **The works.** At t=4650 `pump:02` is switched off so the works runs; the cutting refills too slowly to matter. Store at the end: 3 (one refill out, one charge produced).
  - **Relaxed copy.** Steps from t=4250 repeat the crossing sequence for the Relaxed clock and do nothing in Standard.
- **B** follows the brief "Evacuate via the longer path and save cooling for the late channel."
  - **Power.** It uses the dry ridge generator `generator:02` and `relay:01`. Both pumps hold the cutting below the platform.
  - **The long path.** The crew walks the long path at once and reaches crossing L at S=3145, just after the second channel arrives.
  - **The late channel.** The saved cooler opens crossing L twice, at S=3146 and 3450. The crew is out by S=3850.
  - **The works.** At t=4650 the works is given priority 0, the first claim on power. Store at the end: 3.
- **The difference.** They differ in route (short route with drainage and an early channel versus long path and the late channel), in generator site (cutting platform versus ridge and relay), in shelter use (A only), and in how power is handed back to the works (a pump switched off versus priority reordered).

## A single major mistake
- **`mistake.json`: power loss stops production but does not erase stored coolant.** This is plan A, but power is never rebalanced after the drainage: both pumps keep their priority-20 claim on the generator's two units. The works stays unpowered to the end. All 20 are rescued and the store still holds 2 charges (mastery is met), but `cooling_works` is not operational. Result: **FAIL** in all six configurations, with required and critical missed.
- **No shelter (A without `shelter:01`).** The six crew in the shelter go to AwaitingBackup at S=1719. Result: **FAIL**.
- **One pump (A without `pump:02`).** The causeway never drains. The shelter's protection runs out and the crew goes to AwaitingBackup at S=2519. Result: **FAIL**.
- **`relay:02` instead of `relay:01` (plan B).** The relay is destroyed at 170 s, so the pumps stop, the cutting floods `pump:01` (broken at S=3918), and the works is not operational. Result: **FAIL**.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W5-M3`)
```
ok   W5-M3 wide    Standard no-input   -> success=False rescued=0
ok   W5-M3 wide    Standard strategy A -> stars=3 score=9333 rescued=20/20 backup=0 S=4800
ok   W5-M3 wide    Standard strategy B -> stars=3 score=9417 rescued=20/20 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W5-M3 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=20/20` (cooling works not operational).

## Notes and open issues
- **Margins.**
  - A's shelter ends with 148 of 600 protection (160 in Relaxed).
  - The causeway opens about 100 ticks before A's first cooling.
  - B's crew reaches crossing L 45 ticks after the second channel. In Relaxed they cross before it.
- **Late power handover.** Production runs only after t=4650 in both plans, because handing power back earlier would stop the Relaxed drainage (the scenario clock is half speed). A player can hand power back as soon as the crew is over the causeway (S≈2300). Mastery is met either way, because the store starts with 3 charges.
- **Relaxed copy.** A carries a second copy of its crossing commands (t≥4250) so that Relaxed, where the cutting drains at O≈4300, gets the same decision. In Standard those commands find nothing to do or an empty cooler.
- **Compact.** `class_overrides.compact` swaps the receiving capacities: depot 12 / high landing 8 in wide, 8 / 12 in compact. Results are identical, because receiving capacity only limits simultaneous arrivals.
- **Recipe deviations.**
  - The high landing anchor is moved west (20,4) so that the second channel separates it from the research station.
  - There is no coast road between the refuges, so a closed crossing never reroutes walkers round the whole board.
  - The long path is 3100 ticks.
  - The store keeps the recipe's 3 charges and the default production values (feedstock 6, 100 ticks).
