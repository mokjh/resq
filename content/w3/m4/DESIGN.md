# W3-M4 Cable and Current: design note

Generator: `tools/maps/w3_m4.py` (with `tools/maps/w3_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Combine snow support and flooding power platforms." The station's main line runs in two spans:
- the upper cable, from the upper terrace (west, 6 people, one step-free) across to the cable station (east, 6 people, one step-free);
- the lower cable, from the station down to the valley depot.

The required objective, which is also the critical one, is `route_available powered_cable`: upper terrace → station → depot, by cable, at the end. Two different hazards threaten the lower span:
- **Load.** Its tower is `busy_cable`, which carries melt load from 190 s.
- **Water.** Its drive house (elevation 150 mm, the `winch:02` socket cell) sits on the rim of the lower basin. The basin also holds the two generator platforms, `lower_platform_a` and `lower_platform_b` (300 mm).

**Mastery** keeps both platforms completely dry for the whole operation.

**Power is one grid** with three generator sockets: platform a, platform b, and the dry station ridge. There is one pump with two sockets, either of which lifts basin water into the river.

**Other ways:**
- The upper terrace's standard walkers can take the 600-tick icy stairs to the lower village.
- The station group can walk the long east road (step-free) from its pickup to the high landing.
- `winch:03` runs a second lower cable from a dry drive to the high landing. It is not the main line.

## How the map expresses the threat sequence
- **50 s warning, then 60 s: lower snowmelt (`lower_basin`, 20 M mm³/s).**
- **180 s warning, then 190 s: `set_support_load busy_cable 150`.** Effective stability falls to 250, losing 1 point per second. Unbraced, the tower collapses at S=4399 (verified), taking the lower winch and the main line. One brace ends at 160.
- **300 s warning, then 310 s: second meltwater source (`melt_basin`, a further 20 M mm³/s).**

The basin is 20 cells with a floor at 0. The pump moves 20 M mm³/s at zero lift.

| Basin handling | Result (verified) |
|---|---|
| Pumped from the start (A) | The basin holds near 17 mm until 310 s and ends at 186 mm. Both platforms stay at 0 mm and the drive house reaches 19 mm. |
| Unpumped (mistake) | Water tops platform a at S=3420 and platform b at S=3441. The drive house sits more than 200 mm deep until `winch:02` breaks at S=3898. `generator:01` shuts down at S=3915. |

## Why A and B differ
- **A follows "Pump the basin before peak melt and brace the busiest cable."** It deploys a full kit from the first second:
  - `generator:03` (ridge) and `generator:01` (platform a);
  - the pump on `pump:01`;
  - both braces on the busy tower;
  - both winches.

  A two-winch relay brings the upper terrace down the main line (out by S=800). The station group walks the east road (S=1249) and the lower village walks its road (S=950).
- **B follows "Evacuate the upper terrace before melt peaks, then shift power priority downward."** It uses one generator only, `generator:03` on the dry ridge (2 units).
  - Both winches (priority 10) take it first. The pump, placed at its default priority 20, waits unpowered while the relay empties the upper terrace (out by S=568).
  - At t=700 B shifts priority downward: the pump goes to 5 and the upper winch to 30. The pump and the lower winch now share the generator.
  - The lower winch then brings the station group down the main line (out by S=1191).
  - One brace goes on the busy tower at t=1700, before the 190 s load.

They differ in power layout (two generators, one on a basin platform, versus one dry generator plus priority control), in the station group's route (east road versus lower cable), and in when the pump starts and how many braces are used.

## A single major mistake
- **`mistake.json`, from the bible's "ignoring the lower basin breaks a logically correct upper rescue plan".** This is plan A exactly, without the pump.
  - All 18 are out by S=1249, so the upper rescue is correct.
  - The basin keeps rising. After the second melt it tops both platforms (mastery lost at S=3420/3441) and drowns the drive house until the lower winch breaks at S=3898. The main cable line is no longer available.
  - Result: **FAIL** in all six configurations, with 18 of 18 rescued but `powered_cable` (required and critical) missed.
- **B without the priority shift: FAIL.** The pump never runs and the same chain follows (`winch:02` broken at S=3898).
- **B without its brace: FAIL.** The tower collapses at S=4399.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W3-M4`)
```
ok   W3-M4 wide    Standard no-input   -> success=False rescued=0
ok   W3-M4 wide    Standard strategy A -> stars=3 score=9050 rescued=18/18 backup=0 S=4800
ok   W3-M4 wide    Standard strategy B -> stars=3 score=9300 rescued=18/18 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W3-M4 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=18/18` (route lost).

## Notes and open issues
- **Margins.**
  - Pumped from the start, the basin peaks 114 mm below the platforms.
  - B's priority shift can come as late as t=1900 and still keep the platforms dry: the basin ends at 294 mm against the 300 mm platforms. At t=2300 the platforms wet and the mission drops to 2★.
  - One brace leaves the busy tower at 160 at the end; two leave it at 360.
- **Aerial cables (engine default, see the report).** A cable edge's default depth sample is the Manhattan ground path between its end nodes. Basin water under the lower span therefore closed the cable in the first build.
  - All W3 cables now sample only their two end stations (`W3Map.cable`).
  - The village road samples its painted cells, which never cross the basin.
- **"Powered" is not checked.** `route_available` checks admission only. Following DECISIONS (23 Oct), cable edges are open while a winch is placed, unbroken and its support stands; power governs movement, not admission. The objective therefore does not test power. The design still makes power matter through movement and the platform mastery.
- **Compact layout.** Compact reverses receiving: depot 6 and high landing 12 (wide is 12/6). The east road's outer leg goes from 300 to 400.
- **Recipe deviations.**
  - The outlet is at (20,0), on the river east of the depot, not at the recipe's (16,0).
  - `required_roles` is a list shared across all W3 recipes; only the event roles and roles that describe this map are bound.
  - `alternate_pickup` is not built.
