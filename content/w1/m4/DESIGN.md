# W1-M4 Power Before Water: design note

Generator: `tools/maps/w1_m4.py`, which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Trade early protection against immediate evacuation." The clinic stands on a raised pad (elevation 300 mm) between two town basins. `clinic_power` is operational only while the pad is dry (100 mm or less) **and** powered.
- **Consumers.** Everything shares one power spine. The clinic takes 1 unit and each pump takes 1. Each generator gives 2, so one generator cannot run the clinic and two pumps: the second pump queues.
- **Generator ground is scarce but sufficient.**
  - `generator:01`, the ridge above the clinic, is always dry.
  - `generator:02`, the spine-junction platform beside the power basin, is dry only if `barrier:01` seals the basin's lip before the 280 s spill.
  - `generator:03`, the basin-shore platform, floods.
- **Pumps.** There are three sockets. `pump:01` and `pump:03` drain the clinic basin (into the west branch and the fork). `pump:02` drains the second basin into the east branch. All three share one relocation zone.
- **Groups.** The riverside houses can leave only by skiff. The tram passengers are held until 160 s and then walk to the east landing. The clinic group walks the clinic road to the depot.

## How the map expresses the threat sequence
- **50 s, first spill (`clinic_basin` 30 M mm³/s into 12 cells).** With no pump the basin passes the 350 mm lip onto the riverside terrace at about 190 s. It tops the pad (400 mm head) at about 233 s, which floods the clinic group and the clinic power. One pump started before about 75 s (worked out by hand) holds the basin below the pad for the whole mission.
- **160 s, tram released.** The `release_group` event clears the `held` flag, and only then can the tram passengers walk.
- **280 s, power basin spill (30 M mm³/s into 4 cells).** Without `barrier:01` the water crosses onto the spine-junction platform. `generator:02` shuts down at about 334 s, mastery fails, and the spine drops to 2 units.
- **380 s, final spill (`second_basin` 50 M mm³/s into 8 cells).** Unpumped, the second basin climbs to about 520 mm and floods the pad from the east. With `pump:02` it stays below the threshold: the pad is at most 56 mm in B.

## Why A and B differ
- **A** follows the brief "Protect the dry power spine and alternate pump assignments to lower both basins." At once it places `generator:01`, `barrier:01` (sealing the power basin) and `generator:02`, giving 4 units. It runs two pumps on the clinic basin (`pump:01` and `pump:03`) so that basin stays almost empty, and sends every group out early. At t=3500 it disables `pump:03`, moves it to `pump:02` (same zone), and re-enables it on the second basin before the final spill. Pad at the end: 28 mm.
- **B** follows the brief "Evacuate riverside first, then dedicate both pumps to the clinic/tram sector." First it spawns a skiff at the riverside dock and clears the riverside houses, while the clinic and tram groups get their walking orders. Only at t=350 does it deploy power (ridge generator, sealing barrier, junction generator). It then puts one pump on each basin (`pump:01` and `pump:02`) for the rest of the mission, with no relocation. Pad at the end: 56 mm.

They differ in timing and priority (protection first versus evacuation first) and in pump assignment (two pumps on the first basin then a relocation, versus one fixed pump per basin).

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": overcommitted power.** Only the ridge generator is placed, but both pumps are laid. The clinic (priority 0) and `pump:01` take the 2 units, and `pump:02` sits queued and unpowered through the final spill. The second basin floods the pad (about 135 mm). Result: **FAIL** in all six configurations, with 16 of 16 rescued but `clinic_power` (required and critical) missed.
- **No barrier (B without `barrier:01`).** `generator:02` shuts down at S=334 s, so mastery fails. The spine then falls to 2 units and the second-basin pump is starved. Result: **FAIL**.
- **Generator on the basin shore (`generator:03`).** It shuts down at S=343 s. Result: **FAIL**.
- **No input: FAIL**, with 0 rescued and 10 residents awaiting backup.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W1-M4`)
```
ok   W1-M4 wide    Standard no-input   -> success=False rescued=0
ok   W1-M4 wide    Standard strategy A -> stars=3 score=9000 rescued=16/16 backup=0 S=4800
ok   W1-M4 wide    Standard strategy B -> stars=3 score=9000 rescued=16/16 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W1-M4 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=16/16` (clinic power lost).

## Notes and open issues
- The spec's "low relay connection" applies to relays only; relays are not permitted in this mission, and fixed power links have no flood rule. The bible's "one low connection shuts down unless protected" is therefore the spine-junction **generator platform**. That platform is a generator socket, and protecting it is what the mastery objective (`generator_shutdowns ≤ 0`) measures.
- Margins: the second-basin pad depth is 56 mm pumped (B) against 135 mm unpumped (mistake), with the threshold at 100 mm. A full-kit plan (all 18 supplies) is required for 3★.
- Wide and compact are currently the same graph (no `class_overrides`).
- A strong plan has everyone out by about 190 s. After that the mission is about keeping the clinic powered and dry, which is this mission's thesis.
