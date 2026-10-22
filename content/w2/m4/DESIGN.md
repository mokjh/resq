# W2-M4 When the Wind Turns: design note

Generator: `tools/maps/w2_m4.py` (legend and helpers in `tools/maps/w2_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`. Every legend char authors `fuel` explicitly.

The contract carries only wind events, so the fire is compiled initial state. A pine stand (x 13..14, y 12..13) is listed in `fire.burning`, which starts it Burning at S=0 (engine commit cd82711).

## Central decision
"Anticipate forecast direction changes." Required and critical: the **whole lookout group (8)** is evacuated. Mastery: **at most two firebreak deployments**, so committing the third firebreak costs the third star.

From the burning stand a one-tree hedge runs west and east along the ridge (y 12), then down beside each flank road:
- the lookout's road (x 6) on the west;
- the east camp's road (x 25) on the east, continuing west above the **safe loop** (y 5).

These are the bible's "two dominant wind-exposed edges". A one-tree hedge burns about 130 pulses per tree downwind, 300 upwind and 150 across the wind, so the forecast decides which flank is in danger and when. A firebreak where each hedge leaves the stand (`firebreak:01` west, `:02` east) protects that whole flank.

The **fixed powered tank** is a power node with a fixed supply of 1 (there is no generator in the kit) and a 600,000,000 mm³ tank. It runs one sprinkler. `sprinkler:01`, on the three hedge trees where the west hedge turns down past the lookout, keeps the whole west flank from igniting while wet.

## How the map expresses the threat sequence
No-input timeline (Standard):
- **S=0.** The stand burns, and both hedge ends catch at about S=150. It is calm until 70 s.
- **600 warning, 700 wind (`forest`, code 1: toward the west).** The west hedge races to the lookout.
  - The lookout road closes at S=866 and the lookout group goes to backup at S=931.
  - The fire runs down beside the lookout road; the west camp is caught at S=1521.
  - The east hedge creeps against the wind.
- **1900 warning, 2000 `warn_wind_change` (advisory).** This also publishes the 40-second warning of the turn.
- **2400 wind (code 2: toward the east).** The east hedge reaches the east camp's corner. Its road closes at S=2272, and the camp goes to backup at S=2443: "the formerly safe flank" is now downwind.
- **3500 warning, 3600 wind (code 1).** The hedge above the safe loop closes the loop from S=3302 to S=5158: "a second turn threatens the unprotected flank".

## Why A and B differ
- **A** follows the brief "Protect both flanks selectively and time transport before the turn".
  - At t=5–6 it cuts both hedges at the stand (`firebreak:01` and `:02`), which is exactly the mastery limit.
  - `crawler:01` (depot) collects the lookout, out by S=2584. The west camp walks out at once.
  - The east camp's walk is timed to start at t=1800 and clear its road across the turn, out by S=2740. That is safe only because the east hedge was cut.
- **B** follows the brief "Concentrate early rescues on the future downwind side, then retreat along the safe loop".
  - B uses **no firebreak**. It holds the present danger, the west flank, with water: `sprinkler:01` on the lookout corner from t=5, fed by the fixed tank.
  - The tank lasts about 1,200 pulses, and the wetness carries the corner to about S=1600, past the west front (out by S≈1130). The lookout shelters in place.
  - Its one crawler (`crawler:02`, high landing) goes first to the **east camp**, the side that will be downwind after the turn; all six are out by S=738. The west camp walks.
  - At t=760 the crawler **retreats along the safe loop** to the depot and up the lookout road for the lookout, all out by S=3636. It uses the loop long before it closes at S=3302.

The plans differ in protection (two firebreaks versus water only), in crawler use (lookout versus east camp then lookout) and in order (lookout first versus the future downwind side first).

| Variant (wide) | Standard | Relaxed |
|---|---|---|
| A without `firebreak:01` | FAIL (lookout not all out: required) | FAIL |
| A without `firebreak:02` | 2★ (18/20; the late east walk is caught) | 3★ |
| A with a third firebreak | 2★ (mastery) | 2★ |
| B without the sprinkler | FAIL (12/20) | FAIL (18/20; lookout not all out) |
| B with its crawler starting 2,400 ticks later, after the turn | FAIL (12/20) | 3★ |

## A single major mistake
- **`mistake.json`, from the bible's "ignoring the visible wind timeline makes the formerly safe flank dangerous".** The player follows plan A on the west: the west firebreak, the lookout crawler and the west camp's walk. Because the wind blows west, the east flank is treated as safe: no east firebreak and no order for the east camp.
- The 200 s warning announces the eastward turn. After it the east hedge races to the camp, whose six go to backup at S=2443. 14 of 20 are rescued, against a minimum of 15.
- Result: **FAIL in all six configurations.**
- **No input: FAIL**, with 0 rescued and all 20 in backup.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W2-M4`)
```
ok   W2-M4 wide    Standard no-input   -> success=False rescued=0
ok   W2-M4 wide    Standard strategy A -> stars=3 score=9579 rescued=20/20 backup=0 S=5400
ok   W2-M4 wide    Standard strategy B -> stars=3 score=9632 rescued=20/20 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W2-M4 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=14/20 backup=6`. All results are automated (SimLab headless).

## Compact layout
The recipe's compact primary is the high landing. Capacities stay with the replays (depot 14 for the lookout and west camp, landing 6 for the east camp; receiving equals the population exactly). `class_overrides.compact` shortens the east camp's road to the landing (300 to 250) and lengthens the west road to the depot (250 to 300). Both plans keep 3★.

## Notes and margins
- **Thinnest margin: B's west corner.** It must stay wet until the west front has passed (S≈1130). The tank runs dry at S≈1205, and wetness carries it to about S=1600. This holds in both clocks because the window is early and the sprinkler simply stays on.
- **The east camp's deadline is just after the turn.** It goes to backup at S≈2440. A's timed east walk depends on the east cut; B finishes the east camp by S=738.
- **Relaxed leniency.** The race variants marked 3★ in Relaxed succeed because Relaxed runs the fire at half speed against operation-tick movement. The mistake fails in Relaxed too, because the east camp is never moved.
- **The lookout path.** A lone walking lookout group cannot clear its 300-tick road (2 movement slots) before it closes at S=866. The tower needs either a protected flank or a crawler.
- **Fourth group.** The bible mentions four groups, but the contract has three; the map follows the contract.
- **Unused sockets.** `crawler:03`, `firebreak:03`/`:04` (east corner, safe-loop corner), `sprinkler:02` (east corner) and the three beacons (no rules effect) are legal alternatives.
