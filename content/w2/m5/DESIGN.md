# W2-M5 The Green Corridor: design note

Generator: `tools/maps/w2_m5.py` (legend and helpers in `tools/maps/w2_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`. Every legend char authors `fuel` explicitly.

## Central decision
"Maintain a living evacuation corridor through a combined burn." The **green corridor** is the reserve's east–west road (y 11) from the campsite (west) to the orchard (east). Required and critical: it is available at the end (`routes: central_route`, campsite_pickup to orchard_pickup). Mastery: the **orchard pump house** is operational at the end (infrastructure, demand 0, max heat 300, region `P` at x 18..19, y 13, on the orchard's irrigation line beside the corridor).

North of the corridor, the **great north forest** (x 1..24, y 15..20) burns from its outer west edge at 50 s, and the front rolls east for most of the mission. It can reach the corridor's hedges (y 12) only through two **tongues**:
- **The west tongue** (x 11..12, y 13..14) is a forest strip (`firebreak:01`) plus a **wooden cabin**, which no firebreak can clear; it must be wetted (`sprinkler:01`, tank). It lights the short **west hedge** (x 7..12), which closes the corridor's west half and burns beside the campsite's tents.
- **The east tongue** (x 23..24, y 13..14; `firebreak:02`) is reached just before the wind turns east. It lights the long **east hedge** (x 14..24), which then creeps west against the wind for the rest of the mission, past the pump house and the corridor's middle.

Groups:
- The **campsite** walks out along the corridor. Its 8 walkers pass two at a time, because roads have 2 movement slots.
- The **sawmill crew** and the **orchard crew** stay at their posts until a crawler collects them (held groups, as in W2-M2).
- The sawmill is threatened from the south-west: the sawmill woods light a single line of shade trees to the sawmill sheds, which are fuel buildings beside the crew's yard.
- The orchard has **one long safe road** (600) down to the high landing; the bible's "one long safe route".

Receiving: depot 16, high landing 8.

## How the map expresses the threat sequence
No-input timeline (Standard):
- **400 warning, 500 ignition (`outer_forest` = the forest's outer west edge plus the sawmill woods' outer edge).** The north front moves east at about 115 pulses per cell. The sawmill woods burn towards the shade-tree line.
- **1600 warning, 1700 `add_heat` +200 (`sawmill_access` = the spur road, the last shade tree and the sheds).** The heat lands as the shade-tree fire reaches the sheds: "sawmill access becomes threatened". The crew's yard becomes unsafe, and a crew still there goes to backup at S=2056.
- **S≈1650–1900, the west tongue.** The forest reaches the west tongue, the corridor's west half closes at S=1888, and the west hedge burns. A campsite still at its tents goes to backup at S=2463. The west half reopens at S=3128.
- **3000 warning, 3100 wind (`forest`, code 2: toward the east).** "The wind shifts toward the central route." The front has just reached the east tongue: orchard crew still at the yard go to backup at S=3176, and the corridor's east half closes at S=3291.
- **From S≈3100.** The east hedge's fire creeps west, against the wind, at about 300 pulses per tree.
- **4200 warning, 4300 `add_heat` +200 (`corridor_flank` = the two corridor hedges).** "The final heat peak reaches the corridor flank." Warm hedge trees next to the creeping fire are pushed over. Cleared or cold hedges only warm to 200 and cool again. The pump house goes down at S=4135, and the west half closes again at S=5235.
- **End.** The corridor is closed and the pump house is down.

## Why A and B differ
- **A** follows the brief "Clear a continuous corridor and wet only its vulnerable bends".
  - At t=5–7 it cuts both tongues (`firebreak:01` and `:02`) and places `generator:01`.
  - The only bend a firebreak cannot clear is the cabin; `sprinkler:01` goes on there at t=1300 and wets it through the west front's pass. Its tank runs dry at about S 2500 in Standard and the wetness carries it through.
  - `crawler:01` (depot) collects the sawmill crew first (out by S=1146). `crawler:03` (corridor middle) collects the orchard crew and takes them down the safe road to the high landing (out by S=2447).
  - Because the whole corridor is kept alive, the campsite can wait: it walks out at t=2000 along the corridor while the forest burns on both sides (out by S=3490).
- **B** follows the brief "Evacuate the farthest group first and progressively retreat the transport line".
  - B cuts only the east tongue (`firebreak:02`), which the end route and the pump house need. It uses no sprinkler or generator.
  - The campsite walks first, at t=6, and is out by S=1496, before the unprotected west half closes at S=1888.
  - The crawler line starts at the farthest group: `crawler:03` takes the orchard crew from t=8.
  - Only at t=600 does B add `crawler:01` at the depot for the nearer sawmill (out by S=1737, against the S=2056 deadline). The transport line retreats toward the receiving points as the west front comes in.

The plans differ in protection (both tongues, cabin wetting and a generator, versus the east tongue only), in campsite timing (late through a living corridor, versus first), and in crawler order (nearest crew first, versus farthest crew first).

| Variant (wide, Standard) | Result |
|---|---|
| A without `firebreak:02` | FAIL (route, pump house) |
| A without `firebreak:01` or without the sprinkler | FAIL (16/24: the late campsite is caught) |
| B without `firebreak:02` | FAIL |
| B with the campsite ordered at t=2000 | FAIL (16/24) |
| B with the sawmill crawler 300 ticks later | FAIL (16/24) |

## A single major mistake
- **`mistake.json`, from the bible's "protecting every decorative building leaves too little transport capacity".** The player spends the firebreak stock on the ornamental trees by the campsite lodge (`firebreak:04`), the orchard trees (`firebreak:03`) and the west tongue (`firebreak:01`). It also sprinkles the decorative lodge (`sprinkler:03`) and the west cabin. Everyone is evacuated (24/24, using A's crawler plan), but no firebreak is left for the east tongue.
- The east hedge is lit at about S=3000 and creeps west against the wind. The corridor, the line the crawlers and walkers depend on, is closed at the end, and the pump house beside it is down.
- Result: **FAIL in all six configurations**: `central_route` (required and critical) and the pump house (mastery) are missed. No supplies are left (23 of 23 spent).
- **No input: FAIL**, with 0 rescued and 24 in backup.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W2-M5`)
```
ok   W2-M5 wide    Standard no-input   -> success=False rescued=0
ok   W2-M5 wide    Standard strategy A -> stars=3 score=9217 rescued=24/24 backup=0 S=5400
ok   W2-M5 wide    Standard strategy B -> stars=3 score=9565 rescued=24/24 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W2-M5 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=24/24` (route and pump house lost). All results are automated (SimLab headless).

## Compact layout
The recipe's compact primary is the high landing. Capacities stay with the replays (depot 16 for the campsite and sawmill, landing 8 for the orchard; receiving equals the population exactly). `class_overrides.compact` shortens the orchard's safe road to the landing (600 to 500) and lengthens the depot gate (50 to 100). Both plans keep 3★.

## Notes and margins
- **Thinnest margin: B's sawmill.** In compact the last two sawmill crew are delivered at S=2036, so they board about 250 ticks before the yard becomes unsafe (the crew go to backup at S=2056). Moving B's sawmill crawler 300 ticks later fails. In wide the margin is about 500 ticks.
- **B's campsite.** The last walker arrives at S=1496; the corridor's west half closes at S=1888.
- **Relaxed is lenient for races, not for the corridor.** The race variants above (a late campsite, an unwetted cabin) succeed in Relaxed, because the fire runs at half speed against operation-tick movement. The east-tongue failures (route and pump house at the end) fail in every profile.
- **A's water.** A's cabin wetting is timed to work in both clocks: on at t=1300, the 600,000,000 mm³ tank empties (S≈2500 Standard, S≈1850 Relaxed), and the wetness carries the cabin past the west front (S≈1650–2050). The pond (`sprinkler:02`, pump house bend) and `sprinkler:03` and `firebreak:03`/`:04` are legal alternatives or decoys that neither reference plan needs.
- **Pump house placement.** The recipe puts the pump house in the orchard. It sits on the orchard's irrigation line beside the corridor (x 18..19), because only the corridor's late, slow fire can still threaten something at the end. A pump house among the orchard trees would burn at about S 3100 and recover before the end.
- **Unused generator socket.** `generator:02` (beside the west tongue) is destroyed by the heat.
