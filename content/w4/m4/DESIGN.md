# W4-M4 Storm Switch: design note

Generator: `tools/maps/w4_m4.py` (kit `tools/maps/w4_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Combine rain inflow and sea-level changes."
- **Inland basin.** 36 cells, bed 330 mm. Rain falls into it (`inland_basin` source: 40 M mm³/s from 60 s, 80 M from 440 s). The basin has **no drain**: rain leaves only through pumps.
- **Tide sill.** The basin's west side is a three-cell tide sill (1000 mm) on the west sea. At the 190 s peak (1150 mm) the sea pours over it, and nothing lets that water back out afterwards.
- **Medical road.** The medical landing's only road home (`medical_landing_access`, required and critical at the end) runs along the basin's north rim at 700 mm. It is lost while the basin head exceeds 800 mm.
- **barrier:01** seals the sill: crest 1600 mm, one socket covering the three sill links. A barrier loses durability while the head difference across it exceeds **800 mm**. A sealed sill therefore holds only while the basin keeps some **stored rain** behind it: basin head above 350 mm while the sea stands at 1150. "Store inflow behind a barrier" is the mechanism, not a slogan.
- **Pumps,** all drawing from the inland basin:

| Socket | Outlet | Note |
|---|---|---|
| pump:01 | over the east ridge into the **occupied market basin** | mastery (`pumped_into_region_mm3` in `market_basin` = 0) forbids it |
| pump:02 | west sea, beside the sill | under the tide at the peak |
| pump:03 | north sea, by rising main | under the tide at the peak |

  A submerged outlet raises the lift by the tide (lift = sea head − basin head), so flow drops to about 84 % at the peak and returns to 100 % at low water. This is the head-dependent flow rule of spec §4, with no separate tide penalty.
- **Power.** The generator stands in the depot yard. The pumphouse is reached by relay:01 on a low harbor pad (1000 mm: covered at the peak, reconnects by itself afterwards) or by the ridge pair relay:02 + relay:03 (always dry).
- **Market.** The market's terrace (800 mm) stands over the market basin, which a sluice joins to the harbor. The peak floods it, so the market must be gone before 190 s. barrier:02 can seal that sluice instead, which spends the single barrier on the wrong basin.

## Threat sequence (scenario ticks, Standard)
- **S 600 (60 s):** rain starts at 40 M (0.11 mm/tick over the basin).
- **S 1900–2000 (190 s):** the peak, 1150 mm. The sea overtops the unsealed sill, the market basin and terrace flood, the low relay pad drowns, and the pump outlets are submerged (reduced flow).
- **S 3200–3300 (320 s):** the low-tide window, 150 mm. The low relay reconnects and the pump outlets are clear (full flow). The tide stays low to the end.
- **S 4400 (440 s):** rain doubles to 80 M. Two pumps (40 M) now cover only half of it.

## Why A and B differ
- **A** follows "Store inflow behind a barrier, then pump during low tide."
  - Everyone walks out early: inland S 226–586, market S 706–1066, and the medical landing along the rim road S 1186–1426.
  - At t=1800 barrier:01 seals the sill, with the basin head about 473 mm. The rain is stored behind it, and the head difference at the peak is about 680 mm: the barrier warns (≥ 600) but takes no damage.
  - At t=3300, in the low-tide window, A places the generator, the **cheap low relay:01** and two pumps (pump:02 and pump:03, outlets now above water).
  - Medical road at the end: 30 mm (limit 100). Cost: 13 of 23.
- **B** follows "Keep high docks available and prioritize boat evacuation while managing only the critical basin."
  - Skiffs lift the market from its dock to the high landing (S 318–800) and the medical landing from its jetty to the depot (S 742–1500), while the inland village walks (S 229–589).
  - barrier:01 seals the **inland** sill. The market basin, already emptied by boat, is left to the tide.
  - **One** pump (pump:03) on the always-dry ridge relays runs from the start. It keeps the basin down without ever stripping the barrier of its back-pressure: basin head about 412 mm when the peak arrives, difference about 740 mm.
  - Medical road at the end: 16 mm. Cost: 20 of 23 (the full kit apart from one pump).

They differ in transport (ground versus boats to the high docks), in pump timing and count (two pumps after the peak versus one pump throughout) and in power route (the low relay that is safe only at low water versus the dry ridge pair).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": "a pump outlet submerged under a tide has reduced effect, clearly shown in its forecast."** This is plan B's boats and dry power, but the barrier is skipped and both pumps are relied on to hold the basin through the peak.
  - At the peak both outlets are under the tide. Each pump's lift rises by about 800 mm and its flow drops to about 84 %, the figure its forecast shows. Meanwhile the sea pours over the three-cell sill far faster than the pumps can lift.
  - The basin climbs towards sea level (head about 1050 mm), and nothing drains it after the peak.
  - At the end the medical road is under about 330 mm.
  - Result: **FAIL** in all six configurations (`medical_landing_access` missed). All 22 are rescued, which is exactly the "keep the medical landing accessible" lesson.
- **No input: FAIL**, with 0 rescued.
- Related checks (temporary probe scripts, not shipped):

| Probe | Result |
|---|---|
| Barrier but no pumps | road under 233 mm at the end: FAIL |
| Barrier on the market sluice instead of the tide sill | road under 328 mm: FAIL |
| Plan B with **both** pumps from the start | the basin is drawn to its bed, the head difference passes 800 mm, barrier:01 breaks at S 2126, the sea floods in: FAIL |
| Plan B with pump:01 instead of pump:03 | succeeds at 2★; mastery fails at S 609, the first pumped volume into the market basin |

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W4-M4`)
```
ok   W4-M4 wide    Standard no-input   -> success=False rescued=0
ok   W4-M4 wide    Standard strategy A -> stars=3 score=9435 rescued=22/22 backup=0 S=5400
ok   W4-M4 wide    Standard strategy B -> stars=3 score=9130 rescued=22/22 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W4-M4 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=22/22`.

## Compact layout
`class_overrides.compact` swaps receiving capacity (depot 14 → 8, high landing 8 → 14) and lengthens the high landing's approach (`w_landing_harbor` 100 → 160 ticks). Both plans pass unchanged at 3★.

## Margins and notes
- **Thinnest margin: B's barrier.** The head difference when the peak arrives is about 740 mm against the 800 mm damage threshold. A second early pump breaks the barrier (probe above). The pump is placed at about S 0 in every profile, so the margin does not depend on the profile.
- **End road depth.** A: 30 mm in Standard and 0 mm in Relaxed (its t=3300 pumps start at S 1650 there, and the low relay simply drops out over the peak and reconnects). B: 16 mm. The limit is 100 mm.
- **Relaxed.** A's barrier lands at S 900 and its pumps at S 1650, before the peak. The low relay is cut over the peak and reconnects at low water, so the pumps sit idle over the peak and store rain exactly as intended. It still earns 3★.
- The bible's "tide reduces discharge efficiency" is the spec's head-dependent pump flow: about 16 % at this map's heads, honestly modest. The decisive tide threat is the sill overtopping, and the barrier is what answers it.
- The `market_basin` role binds a region (no event targets it). `medical_landing_access` binds the rim-road edge. Recipe anchors are kept approximately: the market is moved to a terrace beside its basin so pump:01's outlet is physical.
