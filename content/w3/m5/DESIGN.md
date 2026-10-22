# W3-M5 Dawn at Frostline: design note

Generator: `tools/maps/w3_m5.py` (with `tools/maps/w3_kit.py`), which writes `map.json`. Strategies: `strategies/A.json`, `B.json` and `mistake.json`. Contract values are used unchanged; there is no `contract_overrides.json`.

## Central decision
"Coordinate a complete mountain evacuation." Three groups converge through limited safe terraces.
- **The observatory** (west top, 8 people, one step-free) is cliff-bound. Its only way down is the upper cable (`winch:01`, or the slower `winch:03` span) to the station deck.
- **The station group** (east, 8 people, one step-free) lives beside the deck. It can ride the lower cable from the deck, or walk the long east road from its assembly point to the high landing.
- **The lower cable** (`winch:02`) drops from the deck to the **receiving station**. That is a valley transfer point (staging, 8 places, never a refuge).
- **From the receiving station** the crawler (6 seats) runs the 150-tick valley shortcut to the depot. The lower transport window, a long route to the high landing, opens only at 420 s.
- **The village** (bottom west, 6 people, one step-free) walks the lane through the receiving station and down the shortcut, or the long west track.

**The deck stands on `upper_support`.** A collapse takes the deck and both cables.

**Mastery** keeps the receiving station's peak occupancy at 6 or fewer, so it cannot be used as storage.

## How the map expresses the threat sequence
- **60 s warning, then 70 s: forecast warming.** The deck loses 1 point per second. Unbraced, it collapses at S=4699 (verified), sooner if ten or more residents load it.
- **170 s warning, then 180 s: `warn_support` on the deck.** This is a warning stamp only.
- **290 s warning, then 300 s: `release_avalanche valley_slope`.** The runout buries the valley shortcut. It does not reopen.
- **410 s warning, then 420 s: `set_edge_enabled lower_window`.** The transport window from the receiving station to the high landing opens: the long way, 600 ticks.

## Why A and B differ
- **A follows "Use a vertical relay before the runout, then transfer onto the crawler."**
  - Equipment: one generator on the deck side (`generator:02`), both winches and the crawler at the receiving station.
  - The upper winch lifts the observatory onto the deck two at a time. The lower winch takes each pair to the receiving station, where the crawler is waiting. The crawler carries whoever has arrived down the shortcut, and the observatory is out by S=1136.
  - The station group walks the east road from its assembly point (S=1526), so it never touches the deck or the receiving station.
  - The village first gathers at the west-track junction, away from the receiving station, then walks on to the depot (S=1900).
  - One brace goes on the deck after the warming forecast (t=650).
  - Peak sampled occupancy at the receiving station: 3.
- **B follows "Clear the lower village first to free receiving capacity for the mountain groups."**
  - The crawler's first run fetches the whole village from its pickup, through the receiving station and down the shortcut (out by S=586).
  - Meanwhile the upper winch (`generator:01` on the observatory terrace) lifts the observatory onto the deck to wait.
  - With the receiving station empty, the lower winch and crawler bring the observatory down (S=1728).
  - The same lower winch and crawler then serve the station group from the deck (S=2506, 494 ticks before the runout). Both mountain groups pass through the receiving station, and nobody walks the east road.
  - One brace goes on the deck at t=1700, ahead of the support warning.
  - Peak sampled occupancy at the receiving station: 4.

They differ in priority (mountain first versus village first), in the station group's route (east road versus deck and lower cable), in what the crawler does (mountain shuttle only versus village run first) and in generator site.

## A single major mistake
- **`mistake.json`, from the bible's "using the receiving station as indefinite storage causes a capacity bottleneck".** This is plan B, but once the village is out the crawler is ordered to take the mountain groups to the high landing through the transport window, which opens only at 420 s, instead of shuttling them down the shortcut.
  - The crawler loads the first pair and waits at the closed window.
  - The receiving station fills to its eight places.
  - The lower winch cannot unload and stands loaded, and four station residents back up on the deck.
  - When the window opens, the crawler's long run brings out only its own two.
  - Result: **FAIL** in all six configurations. Standard and Command rescue 8 of 22; Relaxed rescues 12 of 22. Observatory and station (required and critical) and mastery are all missed.
- **No input: FAIL**, with 0 rescued.

## SimLab validation (`python3 tools/simtest/simtest.py lab validate W3-M5`)
```
ok   W3-M5 wide    Standard no-input   -> success=False rescued=0
ok   W3-M5 wide    Standard strategy A -> stars=3 score=9318 rescued=22/22 backup=0 S=5400
ok   W3-M5 wide    Standard strategy B -> stars=3 score=9318 rescued=22/22 backup=0 S=5400
ok   ... identical for Relaxed and Command, wide and compact ...
VALIDATION PASSED: 18/18 runs as expected
```
Mistake: `RESULT W3-M5 {wide,compact} {Standard,Command} mistake: success=False stars=0 rescued=8/22`; `Relaxed: rescued=12/22`.

## Notes and open issues
- **Relay hand-off (engine behaviour, see the report).** B's station orders, crawler first and then the lower winch at t=1590/1591, sit in a timing window.
  - Any order time from t=1560 to 1620 gives the same result.
  - At t=1540 the crawler and lower winch drop the observatory group too early, and one resident is left at the receiving station (21/22, FAIL).
  - At t=1650 or later the station group ends later (S=2807), which is thin against the 300 s runout.
- **Order of re-assignment matters.** A vehicle order retargets its group to that vehicle's pickup. The crawler order (pickup: receiving station) is therefore issued before the lower-winch order (pickup: the deck), so the station group walks to the deck and not toward the unreachable valley.
- **Shortcut length.** The shortcut is 150 ticks, not the recipe's 300-tick short leg. With a 2-seat lower winch delivering a pair about every 170 ticks, a 6-seat crawler keeps the receiving station at or below 6 only if its round trip stays under about 500 ticks. Measured peaks are 3 (A) and 4 (B).
- **Observatory corridors.** The observatory has no ground corridor, being cliff-bound. Its two alternatives are the two upper cable sockets, `winch:01` (50 ticks) and `winch:03` (100 ticks).
- **Compact layout.** Compact reverses receiving: depot 8 and high landing 14 (wide is 14/8). The east road's outer leg goes from 300 to 400.
- **Recipe deviations.** `required_roles` is a list shared across all W3 recipes; only the event roles and roles that describe this map are bound. `alternate_pickup` is not built.
