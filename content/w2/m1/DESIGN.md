# W2-M1 A Line in the Trees: design note

Generator: `tools/maps/w2_m1.py` (shared World 2 legend and helpers in `tools/maps/w2_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`. Every legend char authors `fuel` explicitly (roads, meadow, camp and knoll are `fuel: 0`).

## Central decision
"Learn fuel removal and moisture." The required and critical objective is a **ground path from the campsite to the lookout at the end** (`routes: lookout_path`, campsite_assembly to lookout_assembly). Mastery is **at least 1,000,000 mm³ left in the tank**.

Two wooded paths join the campsite (west) and the lookout (east):
- the **short trail** (y 11, 300 ticks) runs under the **trail hedge** (y 12, x 8..23);
- the **long ridge path** (600 ticks) climbs to the ridge road (y 21) and runs under the **ridge hedge** (y 20, x 8..23).

Every path is a fuel-0 road, so fire can close it only by burning the hedge beside it. A lit hedge crawls along the path one tree at a time, and a road beside burning trees stays latched until about 450 pulses after they stop burning. Between the paths lies the central forest (x 8..20, y 14..18). Its **dry stand** (x 8..9, y 14..15, `dry_cluster`) sits at the west end beside the campsite.

The forest touches each hedge in exactly one place:
- **The trail hedge** is reached through the **fuel strip nearest the trail** (x 9..12, y 13, cleared by `firebreak:01` x 9..10 plus `firebreak:02` x 11..12) and through the **woodshed** (8,13). The woodshed is a building, which no firebreak can clear; only `sprinkler:01` can keep it from igniting. It touches only the dry stand, so it is exposed only while the stand burns (S 500 to about 834).
- **The ridge hedge** is reached through a two-tree **neck** at the forest's east end (x 19..20, y 19; `firebreak:03`).

Evacuation itself is easy if ordered early. The campsite walks to the depot by the trail and the depot road, and the lookout walks down its own road to the high landing. The mission is about keeping one wooded path alive to the end.

## How the map expresses the threat sequence
No-input timeline (Standard):
- **400 warning, 500 ignition (`dry_cluster`, 700 heat).** The stand burns at once. Unwetted, the woodshed lights the trail hedge's west end, and the trail's west half closes at S=764. The fire also spreads east through the central forest, about one cell per 110–130 pulses.
- **About S=930–1210.** The forest crosses the uncleared strip onto the trail hedge, and the trail's east half closes at S=1214.
- **1500 warning, 1600 wind (`forest`, code 1: toward the west).** From here, the trail hedge's fire creeps **east, against the wind**, at about 250–300 pulses per tree. The ridge hedge's fire runs **west, with the wind**, from the neck. Leaves and smoke lean before the routes change.
- **S=2007, ridge closed.** The forest's east end lights the neck and the ridge hedge. The ridge hedge is still burning at its west end at S=3599.
- **2500 warning, 2600 `set_region_cooling` (`firebreak_approach` = the two cut lines, +1 heat loss).** A cleared strip that was latched by the heat beside it cools faster: "the first cooled strip becomes safe to cross". The cooling never opens a road by itself.
- **End (S=3600).** The trail's west half reopened at S=2248, but the east hedge end is burning at S=3599, so the trail stays closed. The ridge is also closed, so no path to the lookout remains.

## Why A and B differ
- **A** follows the brief "Break the nearest fuel strip and wet the approach". At t=5–8 it clears the strip nearest the trail (`firebreak:01` and `:02`), places `generator:01` on the campsite and turns on `sprinkler:01` on the woodshed. It sends the lookout and the campsite out at once. The sprinkler stops at t=1050, after the stand has burned out; the woodshed's wetness carries it through the rest. The trail hedge never ignites, so the trail is the path to the lookout. Tank at the end: 79,000,000 mm³ in Standard and 339,500,000 in Relaxed.
- **B** follows the brief "Defend the longer path and stage the campsite first". It sends the campsite first, at t=5, by the trail, which it does not defend: the walkers are past the hedge long before it burns. B cuts only the ridge neck (`firebreak:03`) and sends the lookout at t=300. The ridge path is the path to the lookout. B uses no water.

The plans differ in which path is kept (trail versus ridge), in tools (two firebreaks, a sprinkler and a generator, versus one firebreak) and in evacuation order. Each element is necessary for its own plan (wide, Standard):

| Variant | Result |
|---|---|
| A without `firebreak:01` | FAIL (route lost) |
| A without `firebreak:02` | FAIL (this is the mistake) |
| A without the sprinkler (woodshed burns) | FAIL (route lost) |
| A never turning the sprinkler off | 2★ (tank empty) |
| B without `firebreak:03` | FAIL (route lost) |

## A single major mistake
- **`mistake.json`, from the bible's "a gap in the firebreak permits predictable spread".** This is plan A without `firebreak:02`, so two trees of the nearest strip (x 11..12) are left standing. The forest fire crosses that gap onto the trail hedge (the trail's west half closes at S=936 and the east half at S=1227). From there the fire creeps east along the hedge against the wind and is still burning at the hedge's east end at S=3599. The unprotected ridge closes at S=2007. Both paths are closed at the end, so `lookout_path` (required and critical) is lost.
- Result: **FAIL in all six configurations**, with 10 of 10 rescued and the route missed. The fire stops exactly where the strip was cleared and passes exactly where it was not.
- **No input: FAIL**, with 0 rescued (no auto-dispatch).

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W2-M1`)
```
ok   W2-M1 wide    Standard no-input   -> success=False rescued=0
ok   W2-M1 wide    Standard strategy A -> stars=3 score=9231 rescued=10/10 backup=0 S=3600
ok   W2-M1 wide    Standard strategy B -> stars=3 score=9846 rescued=10/10 backup=0 S=3600
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W2-M1 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=10/10` (route lost). All results are automated (SimLab headless).

## Compact layout
Recipe `receiving.compact_primary` is the high landing. Receiving capacity equals the population exactly (depot 6 for the campsite, landing 4 for the lookout), and the same reference replays must run on both layouts, so the two points cannot swap capacity. The compact swap is therefore in staging travel: `class_overrides.compact` shortens the landing road (200 to 150) and lengthens the depot approach (200 to 300). Both plans pass with the same stars.

## Notes and margins
- **Relaxed woodshed margin.** In Relaxed the sprinkler stops at S=525. The woodshed's wetness is still about 690 when the stand stops burning (S≈834), and 600 is needed. At that level its heat loss (2 + W/100 = 8) matches the 8 heat from its single burning neighbour. Do not move the sprinkler-off step earlier than about t=1000.
- **Standard tank margin.** 79,000,000 mm³ against the 1,000,000 needed (about 156 sprinkler pulses).
- **Robust end closures.** In the mistake, the trail hedge's last tree and the ridge hedge's last tree are both burning at S=3599. A road stays latched about 780 pulses after its last neighbour ignites, so these closures do not hinge on the exact tick.
- Beacon sockets exist (four, as in the recipe), but beacons have no rules effect in the current engine, so no plan spends on them.
- `generator:02` (beside the trail hedge) and `sprinkler:02` (on the ridge neck) are legal alternatives. A generator there is destroyed by the heat. Wetting the neck instead of cutting it would need water from about S 1900 to 2400, which a single replay cannot time in both Standard and Relaxed.
