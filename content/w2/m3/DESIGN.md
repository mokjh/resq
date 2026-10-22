# W2-M3 Water Is a Route: design note

Generator: `tools/maps/w2_m3.py` (legend and helpers in `tools/maps/w2_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`. Every legend char authors `fuel` explicitly. The mission uses the store-to-store pump added in engine commit cd82711: a pump socket's `intake`/`outlet` can be a store id.

## Central decision
"Make water supply and transport choices interact." Required and critical: the **orchard assembly route** is usable at the end (`routes: orchard_assembly_route`, depot to orchard_assembly). The route is the orchard road (y 11) plus the return road (x 15) down to the depot. Mastery: **neither the pond nor the tank** is below 1,000,000 mm³ at the end.

Two fires threaten that route:
- **The orchard edge (40 s).** The orchard's inner row (`orchard_edge`, y 14) ignites, and the front reaches the four-tree **strip** along the orchard road (x 7..10, y 12). The strip also stands beside the orchard yard's three nodes. If it burns, the yard becomes unsafe and the road closes. It can be cleared (`firebreak:01`) or wetted from the tank (`sprinkler:01`).
- **The return road (300 s).** A brush patch beside the depot (`return_road_fuel`) ignites. It touches only the **culvert shed** (16,5). That shed heads a row of **timber sheds** (buildings, which no firebreak can clear) up the return road and along the orchard road. If the culvert shed is dry while the brush burns, the fire creeps up the sheds for the rest of the mission. Only water can protect it: `sprinkler:02`, drawing from the tank.

Water:
- The **storage tank** (600,000,000 mm³, recipe value) stands on the hillside, 4,400 mm above the **pond** (1,200,000,000 mm³).
- A **pump** (pond → tank) lifts pond water up to it. The lift grows as the tank fills and the pond falls, so a running pump stops by itself once the tank holds 600,000,000 mm³ more than the pond. A pump left on therefore never empties the pond: "a pump link can refill the tank without protecting the road by itself".
- The generator gives 2 units of power: the pump takes 1, and each sprinkler takes 1. With the pump running, only **one sprinkler can run at a time**.

## How the map expresses the threat sequence
No-input timeline (Standard):
- **300 warning, 400 ignition (orchard edge).** The orchard road closes at S=648, the yard edges at S=722, and the orchard group goes to backup at S=791. The road reopens at S=1456.
- **1400 warning, 1500 `warn_resource` on the tank.** This is advisory: "the tank drops below half unless replenished". A plan that wets the orchard from the tank alone has it empty by S≈1210.
- **2900 warning, 3000 ignition of the brush patch.** An unwetted culvert shed catches. The return road closes at S=3200 and the orchard road at S=3950. The last timber shed is still burning at S=4799, so the route is lost at the end.

## Why A and B differ
- **A** follows the brief "Pump pond water into the tank and alternate sprinklers".
  - t=5–7: generator, pump (`pump:01`), and `sprinkler:01` on the orchard strip.
  - The crawler (`crawler:02`, at the orchard junction) collects the orchard group; the pond houses walk to the high landing.
  - t=1300: the strip sprinkler goes off. t=2850: `sprinkler:02` goes on the culvert shed and stays on through the brush fire. That is the alternation: one sprinkler at a time beside the running pump.
  - A's water use (1,293 strip pulses plus the shed) is more than the tank alone holds; the pump keeps it supplied.
  - Stores at the end: pond 61,000,000 / tank 117,000,000 mm³ (Standard), pond 115,000,000 / tank 574,000,000 (Relaxed).
- **B** follows the brief "Use firebreaks to minimize wetting and reserve pumped water for the return road".
  - It clears the strip (`firebreak:01`) instead of wetting it.
  - It places the generator and pump, and both groups walk out.
  - The only water it spends is the culvert-shed sprinkler, on from t=2850 to the end.
  - Stores at the end: pond 372,000,000 / tank 452,000,000 (Standard), pond 60,000,000 / tank 52,000,000 (Relaxed). The pump replenished the tank throughout.

The plans differ in how the orchard edge is held (water versus a firebreak), in whether sprinklers alternate, and in transport (crawler versus walking).

| Variant (wide) | Standard | Relaxed |
|---|---|---|
| A without the pump | FAIL | FAIL (the mistake) |
| A without the strip sprinkler | 1★ (14/16; two orchard walkers caught) | 1★ |
| A not switching the strip sprinkler off | FAIL (the shed sprinkler gets no power) | FAIL |
| A without its t=6000 step | 3★ | 2★ (water exhausted) |
| B without its firebreak | 1★ (12/16) | 3★ |
| B without the shed sprinkler | FAIL | FAIL |
| B without the pump | 2★ (tank exhausted) | FAIL (the tank runs dry before 300 s: route lost) |
| A without the crawler, orchard walking | 3★ (convenience, not a necessity) | 3★ |

## A single major mistake
- **`mistake.json`, from the bible's "a sprinkler without water shows a visible dry gauge rather than pretending to operate".** This is plan A without the pump, so nothing replenishes the tank after the 150 s warning.
- The orchard sprinkler empties the tank (S=1210 in Standard, S=1980 in Relaxed), while still keeping the strip wet through its danger window. The shed sprinkler placed at t=2850 then has nothing to spray: its gauge reads empty when the brush ignites at 300 s.
- The culvert shed catches, the return road closes at S=3200, and the fire creeps up the timber sheds. The orchard assembly route (required and critical) is lost at the end, with 16 of 16 rescued.
- Result: **FAIL in all six configurations.**
- **No input: FAIL**, with 0 rescued and the orchard group in backup.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W2-M3`)
```
ok   W2-M3 wide    Standard no-input   -> success=False rescued=0
ok   W2-M3 wide    Standard strategy A -> stars=3 score=9200 rescued=16/16 backup=0 S=4800
ok   W2-M3 wide    Standard strategy B -> stars=3 score=9450 rescued=16/16 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W2-M3 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=16/16` (route lost). All results are automated (SimLab headless).

## Compact layout
The recipe's compact primary is the high landing. Capacities stay with the replays (depot 8 for the orchard, landing 8 for the pond houses; receiving equals the population exactly). `class_overrides.compact` shortens the pond-house road to the landing (300 to 250) and lengthens the return road (300 to 350). Both plans keep 3★.

## Notes and margins
- **Relaxed-only step (flagged).** A's last step, `disable sprinkler:02` at t=6000, falls after the Standard mission ends (O=4800). It runs only in Relaxed, where it switches the shed sprinkler off at S=3000, after the brush has burned out.
  - Relaxed runs the scenario clock at half speed, so A's earlier steps there all happen at half the scenario time, and A leaving the shed sprinkler on to the end would spray about 1.69 G mm³. That exhausts both stores: success at 2★ instead of 3★.
  - B needs no such step; it holds 3★ in Relaxed with the sprinkler left on.
  - Remove the step if Relaxed-only steps are unwanted; A in Relaxed then gives 2★.
- **Water margins.** B in Relaxed ends with pond 60,000,000 and tank 52,000,000 against 1,000,000 needed, about 100 sprinkler pulses each. A in Standard ends with pond 61,000,000 and tank 117,000,000.
- **Strip timing.** In Relaxed the strip sprinkler stops at S=650, and its wetness carries the strip past the orchard front.
- **Culvert shed timing.** The shed sprinkler (one cell) is wet within about 30 pulses of t=2850, well before the 300 s ignition.
- **Alternatives and decoys.** `pump:02` is a second pond-to-tank socket. `sprinkler:03` (pond houses, fed from the pond), `firebreak:02` and `:03` (orchard rows), `generator:02` (inside the orchard, destroyed by the heat) and `crawler:01` are legal alternatives or decoys.
