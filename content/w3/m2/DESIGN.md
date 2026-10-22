# W3-M2 Above the Snowline: design note

Generator: `tools/maps/w3_m2.py` (with `tools/maps/w3_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Manage independent rescue links and power." The board has three terraces.
- **Observatory** (west top, 6 people, one step-free). It has two ways down:
  - the upper snow path over the col to the village, which is stairs and closes at 50 s;
  - the upper cable from its platform to the sheltered intermediate hut.
- **Intermediate hut** (staging, 4 places). Its only onward link is the lower cable to the depot, or the second lower socket to the high landing.
- **Station group** (east middle terrace, 4 people). It is held until 160 s. It then walks the long east road (600 ticks, step-free), or crosses the 150-tick traverse into the hut and rides the lower cable.
- **Village** (bottom west, 4 people). It walks its road to the depot.

**Power is split.** The upper cable runs on the observatory grid (`generator:01`). The lower cables run on the valley grid (`generator:02` at the hut, `generator:03` at the depot). The grids are not linked, so a two-winch relay needs one generator on each.

**Mastery** is the bible's "without trapping residents at the intermediate node": maximum resident dwell at `intermediate` ≤ 100 scenario ticks.

**The ground graphs are deliberately separate.** The observatory/col/village/depot side never touches the hut/station/high-landing side on foot, so only cables join them. This matters because a vehicle order retargets its pickup group. If the two sides were joined, residents waiting at the top could start walking towards the hut by ground.

## How the map expresses the threat sequence
- **40 s warning, then 50 s: snow closes `g_upper_path`.** This is the col stairs, the only walking way off the observatory. In B the five walkers all enter it by S≈341, and the last of them reaches the col at S≈491.
- **150 s warning, then 160 s: `release_group station`.** The station group can walk from here on.
- **280 s warning, then 290 s: the upper cable platform loses 2 points per second.**
  - The platform is already weakened (base 250) and shows a warning from the start.
  - Unbraced, it reaches zero and collapses at S=4149 (verified), breaking the upper winch.
  - One brace keeps it at 190 at the end. Both plans brace it, so neither loses equipment.

## Why A and B differ
- **A follows "Run both winches as a staged relay with separate power."** One generator goes on each grid (`generator:01` and `generator:02`), plus both winches and a brace.
  - The upper winch lifts the observatory into the hut two at a time.
  - The lower winch is ordered at t=100, before the first pair arrives, and waits in the hut. It takes each pair straight on to the depot.
  - All six observers are out by S=568. The longest any resident is seen in the hut (sampled each tick) is 9 S-ticks.
  - The village walks its road (out by S=646). The released station walks the east road (out by S=2540).
- **B follows "Prioritize the observatory, then reuse the lower link for all groups."**
  - At t=1 the five standard observers take the snow path before it closes; they walk via the village to the depot (out by S=1542). The village walks its road.
  - At t=1700 B re-gathers the observatory at the platform, so the step-free resident walks up. It places `generator:01` and `generator:03` (the depot side), both winches and a brace.
  - The lower winch is ordered at t=1830, after the step-free resident is set down in the hut (≈S=1812), and the resident is out at S=1901.
  - The same lower link then takes the released station group from the hut two at a time (out by S=2457). The 150-tick traverse spaces the pairs so they arrive about one winch cycle apart.
  - Longest stretch in the hut: 44 S-ticks (Standard) and 22 (Relaxed).

They differ in route (the observatory rides the relay versus walks the col; the station walks the east road versus rides the lower cable), in equipment (generator at the hut versus at the depot) and in priority.

## A single major mistake
- **`mistake.json`, from the bible's "moving people uphill into a temporary staging node without onward capacity blocks the route".** This is plan A, but the released station group is sent up the traverse into the hut, and no lift is assigned to take it on.
  - The four fill the hut (capacity 4) and wait there to the end, blocking it.
  - Result: **FAIL** in all six configurations, with 10 of 14 rescued against a minimum of 11, and mastery missed.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W3-M2`)
```
ok   W3-M2 wide    Standard no-input   -> success=False rescued=0
ok   W3-M2 wide    Standard strategy A -> stars=3 score=9176 rescued=14/14 backup=0 S=4200
ok   W3-M2 wide    Standard strategy B -> stars=3 score=9176 rescued=14/14 backup=0 S=4200
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W3-M2 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=10/14`.

## Notes and open issues
- **Relay hand-off (engine behaviour, see the report).**
  - A vehicle with a pickup group switches to Done when every remaining member of that group is aboard (or unboarding from) another vehicle. It does not resume when they are later set down at its pickup.
  - Unloading at a non-evacuation node also clears the whole group's walking target.
  - B therefore orders the lower winch only after the step-free resident is set down in the hut. The usable window is about O=1812 to 1900, and 1830 is used.
  - Reproduction: the same B with that order at t=1780 strands the resident in the hut (13/14, FAIL).
  - In A, the hand-off works with the timing as authored (all six ride both cables in every configuration). This is a timing coincidence, not a guarantee.
- **Upper-path margin.** In B the third pair of walkers reaches the col at S≈491, nine ticks before the closure. The engine does not implement spec §6's "known hard closure" admission check. Under that rule this entry would still be legal, because the arrival is before the closure.
- **Compact layout.** Compact reverses receiving: depot 4 and high landing 10 (wide is 10/4). The east road's outer leg to the high landing goes from 300 to 400.
- **Beacons** have no rules effect and are unused.
- **Recipe deviations.** `required_roles` is a list shared across all W3 recipes; only the event roles and roles that describe this map are bound. `alternate_pickup` is not built.
