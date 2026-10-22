# W3-M1 Hold the Station: design note

Generator: `tools/maps/w3_m1.py` (with `tools/maps/w3_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Introduce structure-specific support and vertical transfers." Two terraces meet at the cable station. The station platform, its cable to the valley depot and the stepped footbridge from the lower station all stand on one known weak support (`weak_support`). The required objective, which is also the critical one, is that `cable_support` is operational at the end. That infrastructure:
- needs one unit of power, so a generator is required;
- needs the support standing, so a collapse ends it;
- gates boarding (`gates_dock`), so the cable platform only boards while the station is powered.

The mastery objective, `station_shutdowns ≤ 0`, fails on any transition from operational to not operational.

- **Upper station** (6 people, one step-free). The powered cable is its only step-free way down. Its five standard walkers can instead take the long icy switchback, which is 600 ticks of stairs.
- **Lower station** (4 people, one step-free). It walks the long lower road to the high landing (600 ticks, step-free), or crosses the stepped footbridge to the cable.
- **Braces.**
  - Three brace sockets lock onto the weak support.
  - One socket locks onto the lower-road retaining wall. That wall is an eligible structure, but nothing ever threatens it.
  - The rocks under the station are decorative and take no brace.
- **Second winch.** `winch:02` is a second winch socket on a lower cable, from the lower station to the high landing, off the weak support. Neither reference plan uses it.

## How the map expresses the threat sequence
The support starts at base 400. Effective stability is base, minus static load, plus 200 per brace.
- **50 s warning, then 60 s: loss of 1 point per second.** The scheduled warning stamps the support, so later collapse needs no new warning.
- **150 s warning, then 160 s: melt load of 100 static.** Effective stability drops by 100 at once, and the loss doubles to 2 points per second.
- **250 s warning, then 260 s: tremor of −150.** The points are removed once.

Outcomes by number of braces:

| Braces | Outcome (verified) |
|---|---|
| None | Effective stability reaches 0 at S=2599. The support collapses on the spot, and the winch and the station go with it. |
| One | Collapse at S=2849. |
| Two | The mission ends at 50. |
| Three | The mission ends at 250. |

## Why A and B differ
- **A follows "Brace the weak support and winch the upper group."** It works protection first: generator, winch and all three braces in the first half second.
  - The winch then ferries the whole upper station down in three two-seat trips, and they are all at the depot by S=527.
  - The lower station walks the lower road and is out by S=947.
- **B follows "Evacuate the upper group early, then stabilize for the remaining transfer."** It works evacuation first.
  - At t=1 the five standard walkers take the switchback (out by S=1241) and the lower station walks its road.
  - The step-free resident cannot use the stairs and stays.
  - At t=1300, with the walkers clear, B powers the station, sets three braces and places the winch. The support has already lost 70 points by then.
  - B re-gathers the upper group at the platform and winches the one remaining resident down (S=1451).

They differ in route (five residents walk the switchback in B; all six ride the cable in A), in order (protect first versus evacuate first) and in when the equipment goes in.

## A single major mistake
- **`mistake.json`, from the bible's "bracing decorative rocks has no effect; only eligible supports accept placement".** This is plan A, but the second brace goes on the lower-road retaining wall, the rock-built structure the player trusts, instead of the weak support.
  - Everyone is out by S=947, but the weak support carries a single brace.
  - After the tremor it reaches zero and collapses at S=2849, breaking the winch and the brace and taking the station down.
  - Result: **FAIL** in all six configurations, with 10 of 10 rescued but `cable_support` (required and critical) missed and mastery missed.
- **No braces: FAIL**, with collapse at S=2599.
- **No generator: FAIL**, with 4 of 10 rescued. The platform never boards without station power, and the station is never operational.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W3-M1`)
```
ok   W3-M1 wide    Standard no-input   -> success=False rescued=0
ok   W3-M1 wide    Standard strategy A -> stars=3 score=9143 rescued=10/10 backup=0 S=3600
ok   W3-M1 wide    Standard strategy B -> stars=3 score=9143 rescued=10/10 backup=0 S=3600
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W3-M1 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=10/10` (the support collapses at S=2849).

## Notes and open issues
- **Margins.** Three braces end at 250 effective. Two braces would end at 50, which is thin, so both plans use three. The collapse warning is the scheduled one (stamped at S=500), so a support that reaches zero collapses at once. Resident load never matters here: `floor((100 + 10r)/100)` needs ten people on the station at once.
- **Compact layout.** Compact reverses receiving: depot 4 and high landing 6 (wide is 6/4). It also lengthens the outer approach to the new primary refuge (`g_lower_road_b` goes from 300 to 400). Both plans pass on both layouts.
- **Unused sockets.** Beacons have no rules effect in the current engine, so neither plan spends on them (2 supplies left). `winch:02` (the lower station's own cable) is a valid alternative that neither plan needs.
- **Recipe deviations.**
  - The recipe's `required_roles` is a list shared across all W3 recipes. Only the event role (`weak_support`) and roles that describe this map are bound.
  - `alternate_pickup` (spec 04 §2.8, a water landing) has no meaning on this dry board and is not built.
