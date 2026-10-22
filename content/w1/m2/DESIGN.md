# W1-M2 The Lower Road: design note

Generator: `tools/maps/w1_m2.py`, which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Understand downstream consequences and dry power placement." The medical station must be powered at the end. Its pad sits on the lower road's shoulder. Two choices decide the mission:
1. **Where the generator stands.** `generator:01` is on the dry ridge beside the station. `generator:02` is on a tempting low platform by the viaduct (elevation 650 mm), and the late surge floods it.
2. **Where the flood water goes.** The pump drains the flooded underpass. With `pump:01` the outlet runs west into the empty basin. With `pump:02` it runs north into the orchard terrace. The two lips can also be sealed with paired barriers, but then the late surge tops the stone spillway by the viaduct instead.

## How the map expresses the threat sequence
- **Start.** The sunken underpass on the lower road still holds water from the last storm (250 mm, so the road is closed). The ferry landing can leave only through the underpass or by skiff. The orchard can leave only by skiff (its dock is on the lake).
- **45 s, rain in the upper basin (40 M mm³/s).** The small lake beside the road can drain only through a narrow culvert (conductance 200k). It rises past the 370 mm lips within seconds. Water seeps over the lips onto the shoulder and back into the underpass (lip conductance 18k). This is a visible, causal re-flooding of the road.
- **130 s, lower-road closure forecast (`warn_route` on the underpass edges).** If nothing is done, the shoulder fills, the medical pad starts to wet, and the road stays shut.
- **250 s, peak inflow (80 M mm³/s).** The lake climbs towards about 1050 mm. The low generator platform floods at about 263 s. If the lips are sealed (barrier crest 970 mm), the lake tops the 950 mm spillway instead and pours onto the shoulder. Without a pump, the medical pad (elevation 200 mm, operational only while dry to 100 mm) floods before the end.
- The main river is fed by a constant reservoir and keeps the depot and high-landing docks steady. Its weir outlet holds boat depth, as in W1-M1.

## Why A and B differ
- **A** follows the brief "Put the generator on high ground and pump into the empty basin while the skiff cycles." It places `generator:01`, then `pump:01` at t=6. The underpass drops below the 100 mm closure depth by about 35 s, so the ferry landing walks out over the reopened road (the "road reappears" payoff). The skiff from the depot cycles to the orchard dock, and the river houses walk. The pump keeps pace with the lip seepage all mission, with no barriers.
- **B** follows the brief "Use paired barriers to spare the road, ferry the orchard first, and reserve the pump for the late surge." It places `barrier:01` and `barrier:02` at once, then a skiff at the ferry dock. The skiff takes the orchard first and the ferry landing on a second trip, so both groups leave by water and the road is never used. `generator:01` and `pump:01` are deployed at t=2300, ahead of the peak, to handle the spillway overflow.

They differ in equipment (barriers versus none), in the ferry landing's route (ground versus water), and in timing and priority (pump first versus pump reserved).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate".** The pump outlet is laid into the orchard (`pump:02`) while the orchard group waits for the skiff's second trip. The underpass water is moved onto the orchard assembly point. Mastery fails at once (`exposure_below` on `orchard_assembly`). At S=65 s all four waiting orchard residents go to AwaitingBackup. Result: **FAIL**, with 8 of 12 rescued against a minimum of 9. This holds in all six configurations (wide and compact × Standard, Relaxed and Command).
- **Generator on the low platform.** This is A with `generator:02`. The generator shuts down at S=263.5 s and the medical station is unpowered at the end. Result: **FAIL**, with 12 of 12 rescued but required and critical objectives missed.
- **Dry-power-only plans also fail.** Generator plus skiff with no pump fails, and so does B without the pump (barriers alone). Both FAIL because the medical pad floods. No input at all FAILS with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W1-M2`)
```
ok   W1-M2 wide    Standard no-input   -> success=False rescued=0
ok   W1-M2 wide    Standard strategy A -> stars=3 score=9375 rescued=12/12 backup=0 S=4200
ok   W1-M2 wide    Standard strategy B -> stars=3 score=9125 rescued=12/12 backup=0 S=4200
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W1-M2 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=8/12 backup=4`.

## Notes and open issues
- Wide and compact are currently the same graph, as in W1-M1, because `class_overrides` is not authored yet. The recipe's compact receiving swap (high landing as primary) is still to do.
- The "upper basin" sits beside the road, not at the recipe's `upper` anchor (8,19). This keeps the lake small enough to respond within the event windows (time constant about 80 s). With a large northern lake the rain had no visible effect before the deadline.
- The engine applies `set_source_rate` to each bound source separately. Spec 04 §2 asks for the rate to be split across cells. Each event role here binds exactly one source, so the difference has no effect.
- The visual art still needs the hose for `pump:01`/`pump:02`, the spillway, the culvert and the underpass puddle.
