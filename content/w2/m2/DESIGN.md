# W2-M2 Last Bus from Camp: design note

Generator: `tools/maps/w2_m2.py` (legend and helpers in `tools/maps/w2_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`. Every legend char authors `fuel` explicitly.

## Central decision
"Coordinate finite ground transport capacity." Three camps of six wait at their tents for a crawler. They are **held** groups (map `groups[].held`, as for the W1-M4 tram): the camp tracks are crawler tracks, not footpaths, so only crawlers can bring campers out.

The kit has two crawlers, each with 6 seats and 2 movement slots. Every road has 2 slots, so one crawler fills a road and crawlers queue behind each other. Each crawler serves one camp per trip, so the order of pickups and the loop each crawler uses are the decision.

Critical: `refill_station` is operational at the end. Mastery: it is operational for the whole operation. The station is an infrastructure entry with demand 0, max heat 300, region `refill_station_region` (buildings plus junction cell) and required node `refill_junction`.

Two road loops meet at the refill station junction (15,14):
- The **west loop** runs station → north camp (250) → west camp (200) → depot (200).
- The **east loop**, the longer one, runs station → east camp (250) → high landing (250) → depot (250).
- The **short road** (300) runs straight down from the station to the depot between them.

Fire geometry:
- The **short-road woods** (x 16..17, y 6..11, `short_road_fuel`) touch only the short road.
- They reach the **east woods** (x 19..24, y 6..12) through a two-tree **neck** (18, 8..9; `firebreak:01`).
- Once lit, the east woods burn the hedge under the east-camp road, the hedge beside the east camp's road to the landing, and the tree beside the east camp's tents. They also creep west along the loop-road hedge into the **junction hedge** (16..18, 13; `firebreak:02`) beside the station.
- So **cutting the neck protects the whole longer loop and the station**, while **cutting the junction hedge protects only the station**.

## How the map expresses the threat sequence
No-input timeline (Standard):
- **500 warning, 600 ignition of the short-road woods.** The short road closes at S=614: "one road becomes threatened".
- **S≈750–1300.** Through the neck, the east woods burn. The station-to-east-camp road closes at S=1278 and the east camp's road to the landing at S=1449. The east camp's tents become unsafe at about S=1580, and campers still there go to backup 20 pulses later (S=1599 in the mistake).
- **S=1636.** The fire reaches the junction hedge: the station goes down and the mastery fails. The station recovers at S=2271 after the hedge burns out, so an unprotected station passes the critical objective but not mastery.
- **1800 warning, 1900 wind (`forest`, code 2: toward the east).** The alternate (east) loop is the downwind side: "a wind warning marks the alternate loop".
- **3200 warning, 3300 `add_heat` +200 (`refill_station_region`).** On a protected (cold) station, heat peaks at 198 and falls back to 0 by S=3400: "the refill station approaches heat threshold". A station still warm from a nearby fire would cross 300.

## Why A and B differ
- **A** follows the brief "Stage one crawler on each loop and protect the common junction". At t=5 it cuts the junction hedge (`firebreak:02`). It stages `crawler:02` at the high landing on the east loop, which collects the east camp at once (out by S=639). It stages `crawler:01` at the depot on the west loop, which takes the west camp (out by S=540) and then, at t=600, the north camp (out by S=1531). The east woods still burn and the east loop closes, but its camp is already out, and the cleared junction hedge keeps the station cold.
- **B** follows the brief "Run both on the nearest camp first, then use the protected longer loop". At t=5 it cuts the neck (`firebreak:01`), so the east woods never burn and the whole east loop stays open. Both depot crawlers (`crawler:01` and `crawler:03`) go to the nearest camp, the west camp, and split it 3 + 3 (out by S=673). At t=600 one takes the north camp and the other drives the protected longer loop (depot → landing → east camp). Everyone is out by S=1739.

The plans differ in which firebreak is cut (junction versus neck), in which crawler sockets are used (depot plus landing versus two depot bays), and in pickup order (one crawler per loop versus pooled on the nearest camp first).

| Variant (wide, Standard) | Result |
|---|---|
| A without its firebreak | 2★ (station down at S=1636) |
| B without its firebreak | 2★ (station down) |
| B with the junction firebreak instead of the neck | 3★ (B's routing is fast enough to reach the east camp before S≈1580) |

## A single major mistake
- **`mistake.json`, from the bible's "an empty vehicle on the wrong loop costs time but can be rerouted".** This is plan A, but the crawler staged at the high landing on the east loop is sent **empty across to the north camp on the west loop**. The crawler can be rerouted, and at t=1200 it is sent back to the east camp, but the detour has cost too much time. The east woods reach the east camp's tents first, and its six campers go to backup at S=1599 while the crawler is still on its way (it reaches the landing at S=1450 and the camp later). 12 of 18 are rescued, against a minimum of 14.
- Result: **FAIL in Standard and Command on both layouts.** In **Relaxed** the same replay **succeeds (3★)**: Relaxed runs the fire at half speed against the crawlers' operation-tick movement, so the late reroute arrives in time. That is the brief's "can be rerouted", but it means the mistake is not a failure in Relaxed (compare W1-M3's Relaxed note).
- **No input: FAIL**, with 0 rescued (held campers, no crawler).

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W2-M2`)
```
ok   W2-M2 wide    Standard no-input   -> success=False rescued=0
ok   W2-M2 wide    Standard strategy A -> stars=3 score=9444 rescued=18/18 backup=0 S=4800
ok   W2-M2 wide    Standard strategy B -> stars=3 score=9444 rescued=18/18 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W2-M2 {wide,compact} {Standard,Command} mistake: success=False stars=0 rescued=12/18 backup=6`. `RESULT W2-M2 {wide,compact} Relaxed mistake: success=True stars=3 rescued=18/18`. All results are automated (SimLab headless).

## Compact layout
The recipe's compact primary is the high landing. Receiving capacity equals the population exactly (depot 12 for the west and north camps, landing 6 for the east camp), so capacities cannot swap under the same replays. `class_overrides.compact` instead shortens the east camp's road to the landing (250 to 200) and lengthens the west camp's road to the depot (200 to 250). Both plans keep 3★.

## Notes and margins
- **Held groups.** The contract does not say how campers leave; holding them at their tents is the map's reading of "Last Bus from Camp". Crawler pickups therefore name the camp's `*_start` node. The recipe's `*_assembly` and `*_pickup` nodes exist but are not used by the replays.
- **Station margin.** With either cut the station peaks at 198 heat (the 330 s event) against a limit of 300.
- **East camp margin.** The east camp's tents become unsafe at about S=1580. A collects them by S≈640 and B's protection removes the threat.
- **Unused tools.** The kit's sprinkler and generator, and their sockets (`sprinkler:01` on the station, `sprinkler:02` by the east camp; `generator:02` beside the east woods is destroyed by the heat), are not needed by either reference plan. A cold station never reaches the threshold, so wetting it changes nothing. They remain legal alternatives.
- **Single-lane roads.** Crawlers queue on the 2-slot roads. B's two crawlers going to the same camp is affordable only because the west camp is the closest (200 ticks).
