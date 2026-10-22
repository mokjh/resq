# W4-M1 Between Two Tides: design note

Generator: `tools/maps/w4_m1.py` (shared kit `tools/maps/w4_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## The Tidemark sea (shared by W4-M1…M5)
- **Datum 150 mm.** Sea head = 150 + level, so the four tide segments stand at 150 / 750 / 1150 / 950 mm. High ground is 1200 mm and is never overtopped. Every other height on the board is chosen against these four heads.
- **Every open-sea cell is a sea boundary cell.** Cell-to-cell diffusion is slow: an explicit scheme needs conductance ≤ 2.5 M mm²/s, so a 15-cell reach takes about 900 ticks. In a first draft with only the border cells as boundary, the harbor lagged the tide by 300–600 ticks, and the flats opened at S≈2000 instead of S≈750. With every open-sea cell exchanging with the controller, the open water follows each 100-tick ramp within about 20 ticks. Only enclosed water lags by real cell-to-cell flow: flats, scour holes and inner basins.

## Central decision
"Learn readable tide windows." Every dock and crossing has a window, and the tide moves each one open or shut:

| Place | Height / bound | Window |
|---|---|---|
| Low causeway (west island → mainland) | 800 mm | dry at 150 and 750; closes when the peak covers it (depth > 100 mm at S≈1850) |
| Low-market jetty (low_market_pickup) | water 0–900 mm | low water and the first rise; closed from S≈1844 on |
| Market terrace (low market start/assembly) | 900 mm | floods at the peak (over 100 mm from S≈1893, 250 mm at the top): anyone still there goes to backup |
| Tidal flats round the outer island | 600 mm | dry at low water: boats cross only from the first rise (100 mm at S≈715) |
| Outer-island high pier (outer_island_pickup) | on the flats | usable from S≈715 at every later tide |
| Outer-island low jetty (outer_jetty) | scour hole, deck awash above 500 mm | never usable when the flats let a boat in: the "incorrect dock" |
| Depot quay (receiving dock, lit by the fixed dry source) | water 0–900 mm | closes at S≈1842 and stays closed at 950 |
| High landing | water 0–2000 mm | every tide, but its dock light needs a relay from the dry source |
| Breakwater relay pad (relay:02) | 1050 mm | 100 mm under at the peak (link drops); dry again at 950 |

The depot quay is fed by the fixed dry 2-unit source (`receiving_station`, demand 1, gates the depot), as the brief requires. The high landing's `landing_station` is reached only through a relay link from the same source (relay:01 on the ridge, or relay:02 on the breakwater).

## Threat sequence (scenario ticks, Standard)
- **S 0–600, low water (150 mm).** The causeway, the low-market jetty and the depot quay are all open. The flats are dry, so no boat can reach the outer island.
- **S 600–700, first rise to 750 mm.** The open sea follows the ramp within about 20 ticks. The flats flood, and the pier is usable from about S 715. The low jetty and the depot quay stay open.
- **S 1800–1900, peak 1150 mm.** The depot quay and the low jetty close (S≈1842–1844), then the causeway (S≈1850), and the market terrace floods (S≈1893). From here only the high landing receives, which is the bible's "high landing becomes optimal".
- **S 3000–3100, falling to 950 mm.** The breakwater relay pad dries. The depot quay (950 > 900), the jetty and the causeway stay closed.

## Why A and B differ
- **A** follows "Walk the nearest group early and ferry the late group." At t=5 the low market walks the low causeway to the depot. The causeway has 2 slots × 200 ticks, so 8 people need four waves; they evacuate at S 345, 545, 745 and 945, well before the S 1850 closure. A skiff placed at the depot waits empty until the flats flood. It then crosses (from about S 715), lifts the outer island from its high pier and lands them at the depot quay before the peak (S 1128–1161). Cost: 4 of 15 supplies.
- **B** follows "Use both boats from the beginning and reserve the bridge for the high route." It uses the low-water window instead. skiff:03 spawns at the low-market jetty and takes 4 to the depot. skiff:02 comes from the high landing for the other 4, lit by relay:01. The bridge closes the high-causeway breach (bridge:02), and the outer island walks the all-tide high route to the high landing. Everyone is out by S 599, before the first rise. Cost: 13 of 15.

They differ in mode per group (A: low market by ground and outer island by water; B: the reverse) and in which tide window they use (A: the rising tide that opens the flats; B: the low water before it). B's two loaded boats go to two different receiving docks, so neither waits for the other's berth.

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": "an incorrect dock remains visible but rejects loading with a tide explanation."** This is plan A, but the skiff is sent to the outer island's **low jetty** instead of its high pier. The jetty sits in a scour hole behind the flats. A boat can only get there once the flats are awash (head ≥ 700), and by then the jetty's water is about 750 mm, over its 500 mm deck limit. The skiff lies alongside, every boarding is refused (dock out of range), and the outer island waits at the visible jetty for the rest of the operation. Result: **FAIL** in all six configurations, with 8 of 12 rescued against a minimum of 9.
- **No input: FAIL**, with 0 rescued.
- Related checks (not shipped strategies):
  - A loaded skiff ordered to the depot after S 1842 arrives and can never unload, so the run fails because a vehicle is still loaded. See the engine note below.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W4-M1`)
```
ok   W4-M1 wide    Standard no-input   -> success=False rescued=0
ok   W4-M1 wide    Standard strategy A -> stars=3 score=9733 rescued=12/12 backup=0 S=4200
ok   W4-M1 wide    Standard strategy B -> stars=3 score=9133 rescued=12/12 backup=0 S=4200
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W4-M1 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=8/12`.

## Compact layout
`class_overrides.compact` swaps primary and secondary receiving capacity (depot 8 → 4, high landing 4 → 8) and lengthens the high landing's outer approach (`w_harbor_landing` 120 → 180 ticks). Both plans pass unchanged at 3★.

## Margins and notes
- A's last low-market pair clears the causeway about 900 ticks before it closes. Starting the walk after about S 1100 strands the last pair.
- A's skiff is back at the depot about 680 ticks before the quay closes.
- The mistake's scour hole is already at about 470–600 mm when the flats first let a boat onto the approach (S≈700–715). The skiff needs at least 220 operation ticks to get there from the depot, and by then the hole stands at about 750 mm against the 500 mm limit. It never comes back into range, because the tide never falls below 950 mm again.
- Mastery (`loaded_closed_dock_wait_operation_ticks = 0`) is met by both plans with zero waits. In this engine the counter only grows when a loaded boat cannot set off (route closed, or the destination berth taken). It does not grow while a loaded boat sits at a destination dock that is closed. See the engine note.
- Beacons have no rules effect in the current engine, so no strategy spends on them (as in W1).
- Recipe anchors are kept approximately. The recipe's generic W4 role list (`medical_landing_access`, `pontoon_channel`, …) is not bound where this mission has no such feature. The bound roles are `tide`, `low_market`, `tidal_lane`, `high_causeway`, `receiving_station` and `receiving_dock`.
