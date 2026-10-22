# W1-M5 The Long Way Home: design note

Generator: `tools/maps/w1_m5.py`, which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Combine flow, power, routing and a deliberate equipment trade-off." The required objective, and the 3★ hold for the final 100 ticks, is a **continuous ground route from the depot to the upper village** (`routes: depot_upper`).

**The route.** The short road over the stone bridge is cut by the 170 s landslide, so at the end the route can only be the **long route**. It crosses the spill channel at the viaduct gap (`bridge:01`) and runs down the ridge road. The ridge road dips (250 mm) past the downstream basin, which the 420 s late inflow fills unless it is **pumped**. Pumping needs a **generator**, and both generator grounds are threatened:
- `generator:01` stands on riverside ground beside the backed-up main river. It floods at the peak unless `barrier:03` seals it.
- `generator:02` stands on the rim of the diversion basin. It floods if the basin is over-filled.

**The flow.** At the fork a 700 mm stone spillway drops peak water into the dry spill channel and the **finite** diversion basin. That basin is the valley's one safe store. `barrier:01` seals the main river's 300 mm sill, which diverts the whole surge into the basin. `barrier:02` seals the spillway, which sends all water down the main river.

**The trade-off.** A single bridge serves either the long route (`bridge:01`) or the orchard river crossing (`bridge:02`). A single skiff serves either the island (5 people, two trips) or the orchard.

## How the map expresses the threat sequence
- **60 s, first surge (reservoir 50 M mm³/s).** The fork and main river rise visibly behind the narrow viaduct arch, which is the main fork's "reveal". At this rate the spillway is not yet topped.
- **160 s warning, then 170 s landslide (`short_road` = the stone bridge edge, disabled).** The village and workshop must have crossed already, or now need the long route.
- **290 s, reservoir overflow (80 M mm³/s).** The fork passes the spillway, and part of the peak falls into the diversion basin. The backed-up river tops the orchard low road (lip at 440 mm) and the riverside generator ground (platform at 400 mm).
- **420 s, late inflow (`downstream_basin` 30 M mm³/s).** Unpumped, the ridge-road dip passes 100 mm at about 483 s and the long way home is cut. With the pump (20 M mm³/s) the dip stays below 60 mm.

The reservoir is a fixed landmark behind its dam. Its outflow source sits just above the fork so that surges reach the valley within the event windows. The island's upper lake joins the outflow channel through reed narrows (conductance 20k): boats pass, but the lake does not act as flood storage.

## Why A and B differ
- **A** follows the brief "Build the long bridge route, protect generator ground, and ferry the isolated island." It places `bridge:01` at once and sends the village and workshop to the depot (they cross the stone bridge before the landslide). The orchard walks its low road to the east landing early. `barrier:03` protects the riverside generator ground, then `generator:01` and `pump:01` go in, and the skiff from the depot ferries the island in two trips. The diversion is never touched, so the basin takes only natural peak spill. Everyone is out by 205 s.
- **B** follows the brief "Prioritize island and orchard with the skiff, then redirect the late surge and open the land route." The skiff spawns at the orchard dock: it takes the orchard first (4, then 1) and then the island (4, then 1, done by 273 s). The village and workshop walk the short road. At t=2850 B **redirects the late surge** by sealing the spillway (`barrier:02`), which keeps the diversion basin empty. It then opens the land route (`bridge:01`) and runs the pump from the **basin-rim generator** (`generator:02`), which stays dry because the basin is never used.

They differ in generator ground (riverside plus barrier versus rim), in flow control (natural spill versus a sealed spillway), and in skiff priority (island only versus orchard then island).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": a poor early diversion.** This is plan B, but at 60 s the main river is sealed at the fork (`barrier:01`) instead of waiting for the late surge. The first surge pours over the spillway into the diversion basin, so its safe capacity is spent before the peak. The basin climbs onto its rim, `generator:02` shuts down at S=385.8 s and breaks at S=482.5 s, and the downstream-basin pump stops. The 420 s late inflow then floods the ridge-road dip (339 mm at the end), cutting the long way home. There is no instant loss: every step follows from visible water. Result: **FAIL** in all six configurations, with 20 of 20 rescued but `depot_upper` (required and critical) and the final-100-tick hold (mastery) missed.
- **No input: FAIL**, with 0 rescued, no bridge, and the dip unpumped.
- Without `barrier:03`, plan A's riverside generator shuts down at S=269.8 s as the river backs up. The pump and route then fail the same way (**FAIL**). Placing a generator on flooded ground is also refused (`UnsafeTarget`), which is why a late generator placement on the riverside fails.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W1-M5`)
```
ok   W1-M5 wide    Standard no-input   -> success=False rescued=0
ok   W1-M5 wide    Standard strategy A -> stars=3 score=9250 rescued=20/20 backup=0 S=5400
ok   W1-M5 wide    Standard strategy B -> stars=3 score=9250 rescued=20/20 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W1-M5 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=20/20` (route lost).

## Notes and open issues
- In B the spillway is sealed, so the fork rises to about 1020 mm and the outflow channel to about 1080 mm. That is about 120 mm below the 1200 mm high ground, and it is the tightest margin on this map. Do not raise the inflow or lower the viaduct conductance (600k) without re-checking it.
- Strong plans have every resident out by about 205–273 s. The 290 s and 420 s events then decide the route and power objectives, which is this mission's "long way home" thesis. A plan that relies on the long route for walkers is slower but valid.
- Wide and compact are currently the same graph (no `class_overrides`).
- Beacon sockets exist but beacons have no rules effect in the current engine, so no strategy spends on them.
- The barrier on the main-river sill gives only a 600 mm crest. Because barriers in series do not stack, diversion is partial at the peak, and some water still crosses the crest.
