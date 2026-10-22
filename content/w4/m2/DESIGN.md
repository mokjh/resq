# W4-M2 Keep the Light: design note

Generator: `tools/maps/w4_m2.py` (kit `tools/maps/w4_kit.py`; the sea model is described in `content/w4/m1/DESIGN.md`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Keep a dry power route while transporting residents."
- **The lighthouse** is a real consumer on its own power node (`lamp`, on the rock). It also gates the depot dock (`gates_dock`, nodes `[depot]`). Its demand is 0 until the 210 s event (`set_infrastructure_demand` 1). From then on, every unload at the depot needs a live route from the plant to the lamp, and `lighthouse` is operational only while it is powered and the depot dock is usable.
- **The keepers** (lighthouse group) can only leave from the rock's high shelf landing (900 mm). A boat can work it only at the 350 s peak, so the final unloading always happens after the peak, at the depot, and needs lamp power. This makes the mastery check (no outage between `final_unloading_started` and `final_unloading_ended`) a real check of the network at peak tide.

**Power network.** The generator stands on `generator:01` in the dry yard (`generator:02` on the depot wharf is covered at the peak). The routes to the lamp:

| Socket | Platform | Link | Fate |
|---|---|---|---|
| relay:01 | low cable-channel islet, 650 mm | plant → lamp | link drops at the 70 s rise (S≈790) and never returns |
| relay:03 | yard, 1200 mm | plant → ridge | always dry. **This is the only plant → ridge span.** |
| relay:02 | breakwater, 1000 mm | ridge → lamp (short) | dry at 750; link drops at the peak (S≈3584, 150 mm) |
| relay:04 | sea stack, 1200 mm | ridge → lamp (long) | always dry |

All relays share one relocation zone. Stock is 3 relays; each plan places 2.

**Transport.**
- The west island is reachable only by boat, across tidal flats that open with the first rise (S≈800).
- The market's terrace (900 mm) floods at the peak. The market can walk its low causeway (800 mm) to the high landing, or board at its low-water pontoon (to 900 mm); both are lost at the peak.

## Threat sequence (scenario ticks, Standard)
- **S 700–800 (70 s), first rise to 750 mm.** The low cable channel drowns (relay:01 drops at S≈790). The west island's flats open.
- **S 2100 (210 s).** The lighthouse demand becomes 1. From now on, a dark lamp closes the depot dock.
- **S 3500–3600 (350 s), peak 1150 mm.** The rock shelf opens (S≈3570). The breakwater relay drowns (S≈3584), which cuts the short network. The market terrace, causeway and pontoon are lost.

## Why A and B differ
- **A** follows "Build the longer elevated network immediately."
  - At t=5 it places the generator, relay:03 and relay:04, which make the always-dry long route.
  - The market walks its causeway to the high landing (S 348–908).
  - The high-landing skiff ferries the west island to the high landing (S 1434–2072).
  - The depot skiff waits at its own berth for the peak and lifts the keepers. Final unloading runs S 3815–3858 with the lamp lit.
- **B** follows "Use the short network early, then transfer idle relays to the high sockets before peak tide."
  - It places relay:03 and the breakwater relay:02, the short route that carries the lamp through the 210 s demand.
  - The high-landing skiff takes the market off its pontoon (S 623–1261). The depot skiff ferries the west island to the depot (S 1274–1752), then waits at the depot for the peak and lifts the keepers.
  - At t=3300 B switches relay:02 off, moves it to the sea-stack socket relay:04 (same zone; a relay must be off to move) and switches it on. This leaves a 2-tick outage at S 3300–3302. No boat is unloading then, and the final unloading has not started.
  - Final unloading runs S 3815–3858 with the lamp lit.

They differ in network timing (long route at once versus short route then relocation), in market transport (walk versus boat) and in which receiving dock each island uses.

In Relaxed the same operation-tick script moves the relay at S 1650, before the demand starts. It is the same plan with more slack, and it still earns 3★.

## A single major mistake
- **`mistake.json`, from the bible's "failure to communicate": "moving a relay while its only route is actively required blocks unloading."** This is plan B, but at the transfer the player switches off and moves **relay:03** (the yard relay) instead of the idle breakwater relay. relay:03 carried the only plant → ridge span, so the lamp goes dark as soon as it is switched off, and from the 210 s demand on the depot's safe-route signal is out.
  - The peak then drowns the breakwater relay as well.
  - The keepers' boat lands at the dark depot and can never unload.
  - At the end the lighthouse is not operational (required and critical) and a skiff is still loaded.
  - Result: **FAIL** in all six configurations, with 12 of 16 rescued.
  - In this script the block is permanent, because the player never puts a relay back on the plant → ridge span. The brief's "temporarily" describes the same situation with a later repair.
- **No input: FAIL**, with 0 rescued.
- Related check: plan B **without** the transfer also fails. The breakwater relay drowns at S≈3584, the lamp is dark at the final unloading, and the result is the same 12/16 failure.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W4-M2`)
```
ok   W4-M2 wide    Standard no-input   -> success=False rescued=0
ok   W4-M2 wide    Standard strategy A -> stars=3 score=9211 rescued=16/16 backup=0 S=4800
ok   W4-M2 wide    Standard strategy B -> stars=3 score=9211 rescued=16/16 backup=0 S=4800
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W4-M2 {wide,compact} {Standard,Relaxed,Command} mistake: success=False stars=0 rescued=12/16`.

## Compact layout
`class_overrides.compact` swaps receiving capacity (depot 10 → 6, high landing 6 → 10) and lengthens the high landing's outer approach (`w_harbor_landing` 140 → 200 ticks). Both plans pass unchanged at 3★.

## Margins and notes
- The breakwater relay's platform takes 150 mm at the peak against the 50 mm drop threshold. The sea stack and yard are 1200 mm and never wet. The rock shelf carries 250 mm at the peak against its 100 mm minimum.
- The final unloading ends at S 3858 (Standard), or S 3714 in Relaxed, against a 4800 deadline.
- B's relocation lands about 280 ticks before the breakwater relay drowns. In Standard the outage happens while the route is required (after 2100) but while nothing is unloading, so neither the at-end required objective nor the final-unloading mastery is touched.
- **Berth parking (engine behaviour, used by design).** A skiff that has finished its group stays moored in its last berth, so it keeps that dock's only berth (see the W4-M3 note). Both plans give each receiving dock one boat: the market and west-island boats finish at the high landing (A) or at separate docks (B), and the keepers' boat always comes back to the depot. A first draft of A that sent two boats to the depot deadlocked, with the keepers stuck aboard.
- Beacons have no rules effect, so no strategy spends on them. Recipe anchors are kept approximately. The bound roles are `tide`, `lighthouse`, `receiving_dock` and `tidal_lane`.
