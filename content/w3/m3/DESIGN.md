# W3-M3 The Quiet Slope: design note

Generator: `tools/maps/w3_m3.py` (with `tools/maps/w3_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Read runout zones and preserve a secondary route." A broad slope lies between the weather station (west top, 6 people, one step-free) and the valley depot. There are three ways off the top:
- **The short road** (300 ticks) zig-zags straight down the slope. It is the marked runout.
- **The rock-shelf bypass** (600 ticks, stairs, tagged `rock_shelf_bypass`) runs east along the ridge to the shelf landing, then down the shelf stair to the high landing.
- **The shelf lift.** One winch socket carries the cable from the weather station across to the shelf landing, above the short road. A second socket carries the cable from the shelf landing down to the high landing.

The other two groups:
- **The rock-shelf group** (4 people, one step-free) uses the stair. The old debris gully beside it is step-free but blocked until it is cleared at 340 s.
- **The village** (6 people, one step-free) walks the slope-foot lane to the depot, or the long west track.

The depot and the high landing are not linked on the ground. Once the slope is buried, the shelf is the only way out of the top.

**Mastery** requires at least one transfer on a tagged rock-shelf edge.

## How the map expresses the threat sequence
- **80 s warning, then 90 s: `warn_runout`.** This is advisory only; the marked corridor is shown.
- **200 s warning, then 210 s: `release_avalanche quiet_slope`.**
  - At S=2100 the upper segment buries `g_slope_upper` and the middle bend.
  - Ten ticks later the lower segment buries `g_slope_lower`, the slope foot and the village lane.
  - Debris settles for 200 ticks. The road is not `debris_reopenable`, so the short road stays closed for the rest of the mission.
- **330 s warning, then 340 s: `set_edge_enabled debris_bypass`.** The debris gully opens, giving step-free walkers a way off the shelf.

Braces lock onto the lift station and the ridge footbridge, which are both structures; nothing on the slope takes a brace. Neither plan needs them.

## Why A and B differ
- **A follows "Move the crawler through early and use the shelf afterward."**
  - The crawler (`crawler:01` at the depot) leaves at once and climbs the short road. It reaches the top at about O=300, so it is off the slope road long before the release, and it has the whole weather crew loaded by about O=400.
  - It drives on over the shelf bypass to the high landing (crew out by S=1033).
  - It then climbs the shelf stair for the rock-shelf group (out by S=1787).
  - The village walks the slope-foot lane early (out by S=843).
- **B follows "Avoid the short road entirely and stage winch transfers above it."**
  - A generator and `winch:01` lift the weather crew across to the shelf landing two at a time.
  - At t=700 the crew is sent on: the five standard walkers take the shelf stair (from O=1020).
  - Both step-free residents, the weather crew's and the rock shelf's, wait safely on the shelf until the gully is cleared at 340 s (out by S=3720 and S=3740).
  - The village walks the lane early. Nothing in B ever touches the short road.

They differ in equipment (crawler versus generator plus winch), in route (the short road early plus the shelf by vehicle, versus the shelf lift plus the stair and the gully) and in timing (everyone out by 179 s versus waiting for the 340 s gully).

## A single major mistake
- **`mistake.json`, from the bible's "a closed road stops vehicles at a safe node; it does not fling rigidbody residents".**
  - The crawler is held at the depot and sent up the short road only at t=1700, to come back the way it went.
  - It loads the crew at the top at about O=2060. Coming down, it finds `g_slope_upper` buried at 210 s and stops at the safe slope-top bend, with the crew aboard and unharmed but no road to the depot.
  - Result: **FAIL** in Standard and Command (both layouts), with 10 of 16 rescued and the weather crew, required and critical, missed.
  - In Relaxed (both layouts) the same operation-tick script runs before the release in scenario time (dispatch at S=850), so it succeeds with 3★. This matches the W1-M3 precedent: Relaxed doubles the operation time behind every warning, so it is not the same decision.
- **Recovery check: SUCCESS 3★ (Standard).** Ordering the stopped crawler on to the high landing at t=2300 takes the crew over the shelf (out by S=3015). The crew was stopped, never thrown.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W3-M3`)
```
ok   W3-M3 wide    Standard no-input   -> success=False rescued=0
ok   W3-M3 wide    Standard strategy A -> stars=3 score=9778 rescued=16/16 backup=0 S=4800
ok   W3-M3 wide    Standard strategy B -> stars=3 score=9667 rescued=16/16 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W3-M3 {wide,compact} {Standard,Command} mistake: success=False stars=0 rescued=10/16`; `{wide,compact} Relaxed: success=True stars=3`.

## Notes and open issues
- **Vehicles on stairs.** Under rules-v1, vehicles follow ground edges of any step class ("vehicles carry either mobility class"), so the crawler can climb the shelf stair; step-free walkers cannot. If a future rules revision restricts vehicles on stairs, plan A's second run must use the gully after 340 s instead.
- **Margins.**
  - A's crawler is off the slope road about 1800 ticks before the release.
  - B's step-free residents depend on the 340 s gully, with 60 s to spare before the end.
- **Compact layout.** Compact reverses receiving: depot 6 and high landing 10 (wide is 10/6). The shelf stair, the outer approach to the high landing, goes from 300 to 400.
- **Unused sockets.** Beacons and braces have no effect on this mission's outcome and are unused (A keeps 14 supplies, B keeps 12). That is the intended lesson: braces hold structures, not slopes.
- **Recipe deviations.** `required_roles` is a list shared across all W3 recipes; only the event roles and roles that describe this map are bound. `alternate_pickup` is not built.
