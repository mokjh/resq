# W4-M5 Harbor of Everyone: design note

Generator: `tools/maps/w4_m5.py` (kit `tools/maps/w4_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Master transport, tide windows, and power topology." The board has three islands, two causeways, four pickup groups and a lighthouse-backed safe harbor.

**The harbor and the lamp.**
- The depot is the safe harbor. Its dock works only while the lamp on the lighthouse rock is powered: `lighthouse` has demand 1 from the start and gates the depot. It is dark until a network is built.
- `lighthouse` operational at the end is both required and critical.
- The high landing is unlit and always works.

**Groups.**

| Group | Where | Ways out |
|---|---|---|
| low_market (8) | west island, 900 mm terrace (floods at the peak) | low causeway (600 mm) to the middle island, closed by the first rise; low jetty (to 900 mm of water), closed at the peak; a second, all-tide footbridge whose last span is gone (`bridge:02`, a decoy for the single bridge) |
| middle_island (6) | middle island | `high_causeway` (1200 mm) to the mainland across a breach (`bridge:01`); high east dock (all tide); west dock = `middle_staging`, a temporary staging point (flag `staging`), which must be empty at the end |
| outer_island (6) | north island | boats only: `outer_loop` round the east side (400 ticks, faster from 300 s) to its east dock, or the `tidal_lane` over the flats (open from 60 s) from the staging dock to its south dock |
| lighthouse (6) | the rock | low landing (to 900 mm of water), closed at the peak |

**Power.** A generator on the harbor yard pad (`generator:01`, 950 mm) is flooded at the peak unless **barrier:01 and barrier:02 seal its three sea links**. `generator:02` stands on the dry ridge. Links:
- harbor → lamp by relay:01 (breakwater, dry) or relay:03 (low cable islet, 650 mm, drowned from the first rise);
- ridge → harbor by relay:02 (dry).

**Mastery.** `high_causeway` (middle island → depot, ground) must be usable at the end, so the bridge must be in place and dry.

## Threat sequence (scenario ticks, Standard)
- **S 600–700 (60 s), rise to 750 mm.** The low causeway closes (over 100 mm at S≈701). The relay:03 islet drowns (S≈700). The flats open the tidal lane.
- **S 1800 (180 s), `warn_support` on `harbor_power`.** This is bound to the lighthouse infrastructure and is advisory only (DECISIONS, 23 Oct). It is the cue that the harbor's power is what the lamp, and so the depot, depends on.
- **S 3000 (300 s).** The outer loop runs 1.5× faster.
- **S 4300–4400 (430 s), peak 1150 mm.** The low jetty and the rock's low landing close (S≈4342). The low-market terrace floods (anyone left goes to backup at S≈4417). An unprotected yard pad drowns its generator. The low-tide pads and causeway stay closed to the end.

## Why A and B differ
- **A** follows "Prioritize low docks, then use the outer loop for the final groups."
  - The network is built on the dry ridge: generator:02, relay:02, relay:01.
  - Boats work the **low docks** first: the low-jetty skiff takes the low market to the depot (S 301–783), and the high-landing skiff takes the keepers off the rock's low landing (S 545–1103).
  - The low-jetty skiff then lifts the middle island from its high dock (S 1294–1812).
  - The **final group** goes by the **outer loop** once the current has turned (t=3000): the outer island, S 3788–4600.
  - The bridge is placed at t=4000 so the high causeway is usable at the end. A never walks it.
  - Cost: 18 of 26.
- **B** follows "Open the high causeway and use boats only for isolated islands while protecting the network."
  - At t=5 the bridge opens the high causeway. The middle island walks home (S 347–547), and the low market walks the low causeway, the middle island and the high causeway (S 647–1156; its last pair is on the low causeway by about S 596, before the S≈701 closure).
  - The network is protected rather than placed high: generator:01 on the yard pad, sealed by barrier:01 + barrier:02 (pad dry at the peak), with relay:01 to the lamp.
  - Boats go **only to the isolated groups**: the keepers to the depot (S 307–625) and the outer island by the outer loop to the high landing (S 1069–2147).
  - Cost: 20 of 26.

They differ in transport mode for the two causeway-linked groups (boats versus walking), in network (dry ridge generator with two relays versus a barrier-protected harbor generator with one relay) and in what the bridge is for (only the mastery route versus the main evacuation corridor).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": "saving the lighthouse alone cannot satisfy the rescue goal."** This is plan B with the network built and protected first, the keepers and the outer island lifted, and the high causeway open. But the low market's walking order waits until the 180 s harbor-power warning.
  - By then the low causeway has been closed for over 100 s. The low market has no ground path, and the boats are committed to the isolated groups.
  - At the peak the terrace floods: all 8 go to backup at S 4417.
  - The lamp is lit, the harbor is operational, the staging is empty and the high causeway is open, but only 18 of 26 are rescued against the minimum of 20.
  - Result: **FAIL** in all six configurations, with `critical` met and `rescue_minimum` missed. The same result in Relaxed: t=1800 is S 900 there, still after the closure.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W4-M5`)
```
ok   W4-M5 wide    Standard no-input   -> success=False rescued=0
ok   W4-M5 wide    Standard strategy A -> stars=3 score=9308 rescued=26/26 backup=0 S=5400
ok   W4-M5 wide    Standard strategy B -> stars=3 score=9231 rescued=26/26 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W4-M5 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=18/26 backup=8`.

## Compact layout
`class_overrides.compact` swaps receiving capacity (depot 16 → 10, high landing 10 → 16) and lengthens the high landing's approach (`w_landing_he` 100 → 160 ticks). Both plans pass unchanged at 3★.

## Margins and notes
- **Thinnest margin: B's low-market walk.** In Standard and Command the last pair enters the low causeway about 105 ticks before it closes (2 slots × 180 ticks, 8 walkers). In Relaxed the margin is much larger, because walking is in operation ticks.
- A's outer-island boat is back at S 4600 against the 5400 deadline. A's low-dock lifts finish more than 2500 ticks before those docks close.
- B's yard pad stays dry at the peak behind its two barriers. The head difference is about 200 mm, far below the 800 mm damage threshold.
- `temporary_staging_empty` holds in both plans because neither leaves anyone at `middle_staging`. It is a live check for transfer-style plans: the tidal lane runs from that dock, and a probe confirmed the lane opens after the first rise.
- `barrier:03` (a seal on one link of the low-market terrace) exists for the recipe's socket count. One link cannot keep the terrace dry, and no strategy uses it.
- Recipe anchors are kept approximately. The groups are placed by name: the outer island at the upper anchor, the lighthouse at the orchard anchor, the low market at the west anchor, and the middle island in the centre rather than at the east anchor. The bound roles are `tide`, `harbor_power`, `lighthouse`, `outer_loop`, `tidal_lane` and `high_causeway`.
