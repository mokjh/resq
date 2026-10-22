# W5-M5 Every Little Life: design note

Generator: `tools/maps/w5_m5.py` (with `tools/maps/w5_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Final combined-system rescue with meaningful priorities." No new mechanic is introduced. The required and critical objective is the **final ferry route** (`final_ferry`: ferry dock to depot, water) open at the end. Mastery is zero equipment losses. The board has two lava channels and two water basins.
- **The west flow** (`orchard_channel`, 60 s) cuts the orchard and the research terraces off from the depot.
  - The orchard's upper crossing is reachable before the lava.
  - The research group's lower crossing (the inland route, 700-tick bank road) is reached only after the lava, so it needs cooling, or a skiff from the research dock.
  - The heat then reaches the orchard terraces (75 s) and the research terraces (150 s).
- **The staging channel** (`staging_channel`, 400 s) runs north to south through the middle down to the harbour.
  - It makes the high village's staging zone (its terraces and the quay shelter) unsafe.
  - It closes the harbour mouth, so the final ferry route is open at the end only if the mouth is cooled in the last window (S 5150–5349).
- **The drainage basin** (`drainage_basin`, 60 s, 30 M mm³/s).
  - Unpumped, it drowns the low causeway, the `first_transport` route the high village must walk (closed from about S=2400; `warn_route` at 170 s). It then spills into the high lagoon, which becomes deep enough for skiffs to reach the high-village quay at about S=4150.
  - With `pump:01` running, the causeway stays open and the lagoon stays dry. The basin generator platform (`generator:02`) is dry only while the basin is pumped (unpumped it floods from about S=3900).
- **The power basin** (`power_basin`, 290 s, 40 M mm³/s) floods the north generator platform (`generator:01`) from about S=4000, and damages a generator there from about S=4700, unless `barrier:01` seals its lip.
- **The generator** powers the ferry terminal (skiffs can board at the ferry dock only while it is powered) and the pump.

## How the map expresses the threat sequence (Standard)
- **60 s.** The west flow turns Hot and the drainage spring starts.
- **75 s.** The orchard terraces become unsafe.
- **150 s.** The research terraces become unsafe.
- **170 s.** Route warning on the causeway.
- **290 s.** The power basin fills.
- **400 s.** The staging channel turns Hot (staging zone and harbour mouth). This needs at least 100 ticks of warning, published at 390 s.

## Why A and B differ
- **A** follows the brief "Stabilize the power/drainage spine, ferry coast groups, and cool the inland route just in time."
  - **The spine.** `generator:02` on the basin platform and `pump:01` on the drainage basin.
  - **Walkers.** The orchard crosses before the lava. The station walks to the high landing. The high village walks the long road and the pumped causeway (high landing at S=3213).
  - **Waterfront.** Two skiffs split the waterfront between two landings.
  - **Research.** The inland crossing is cooled at S=765, the moment the research group reaches its bank; the group is out by S=1195.
  - **The final ferry.** The harbour mouth is cooled at S=5200 (crust 5250–5450), so the route is open at the end.
- **B** follows the brief "Evacuate the most exposed groups first, use shelter for the high village, and dedicate late resources to the ferry connection."
  - **Exposed groups first.** The orchard crosses first. A skiff takes the research group from its dock (out by S=1145). A second skiff ferries the waterfront, with the north generator behind the barrier powering the ferry terminal.
  - **Drainage left unpumped.** The causeway drowns, but the lagoon fills.
  - **The high village.** It waits in the quay shelter through the staging heat (protection used 362 of 600). It leaves by skiff across the lagoon (S=4522–4900).
  - **The final ferry.** The harbour mouth is cooled in the last window.
- **The difference.** They differ in generator site (basin platform plus pump versus north platform plus barrier), in the drainage choice, which decides the high village's way out (dry causeway on foot versus flooded lagoon by boat), in the research group's mode (cooled inland crossing versus skiff), and in shelter use (B only).

## A single major mistake
- **`mistake.json`: a plan that protects scenery but delays evacuation cannot earn a successful outcome.** This is plan A's equipment, plus a barrier for the unused north platform, but every group and boat order waits until the spine is "secure" at 90 s.
  - The west flow has already closed the orchard's crossing, and the orchard terraces burn (AwaitingBackup at S=769).
  - The inland crossing is cooled on A's schedule at 76.5 s, before the research group, which now leaves late, reaches its bank. The window is wasted and the group is stranded.
  - Result: **FAIL** in all six configurations, with 18 of 30 rescued against a minimum of 23.
- **A without the final mouth cooling.** All 30 are rescued, but `final_ferry` is closed at the end. Result: **FAIL**.
- **A without the pump.** The high village finds the causeway drowned. Result: success, 1★ (24 of 30).
- **B without the shelter.** Four high-village residents go to AwaitingBackup at S=4019. Result: success, 1★ (26 of 30).
- **B without the barrier.** `generator:01` is broken at S=4768, so mastery fails. Result: 2★.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W5-M5`)
```
ok   W5-M5 wide    Standard no-input   -> success=False rescued=0
ok   W5-M5 wide    Standard strategy A -> stars=3 score=9333 rescued=30/30 backup=0 S=5400
ok   W5-M5 wide    Standard strategy B -> stars=3 score=9333 rescued=30/30 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W5-M5 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=18/30`.

## Notes and open issues
- **Margins.**
  - B's quay shelter ends with 238 of 600 protection (338 in Relaxed).
  - The lagoon becomes navigable about 150 ticks after the staging heat.
  - In A the causeway peaks at 87 mm against the 100 mm limit (pumped).
  - The final mouth cooling has a 200-tick slot (S 5150–5349) that leaves the crust open at S=5400.
- **Relaxed copy.** Both plans repeat the last cooling at t=10400 for the Relaxed clock. In Standard that step comes after the end.
- **Compact.** `class_overrides.compact` swaps the receiving capacities: depot 18 / high landing 12 in wide, 12 / 18 in compact. Results are identical: berths, not pedestrian capacity, limit the landings.
- **Unused sockets.** `barrier:02` seals only one causeway link and does not save the causeway; it is a weak alternative. The crawler sockets exist, but neither plan needs the crawler; it is in the kit only.
- **Recipe deviations.**
  - The high-village anchor sits at (15,16) instead of (15,15), and the waterfront at (26,21) instead of (23,21), to keep both clear of the channels.
  - Roads are long so the lava, not walking speed, decides the inland crossing.
