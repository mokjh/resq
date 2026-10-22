# 03 — World and mission bible

## Content contract

Full-game target: five worlds, five missions each. Only Alder Valley is the default competition scope. Every entry below is an authored production brief, not an implemented level. Event times are simulation seconds after the Active phase begins; pausing stops them. Tutorial time is state-driven and excluded until hazards are enabled. Timelines describe threats; actual outcomes remain governed by simulation. All values require balance validation.

Do not use these descriptions as runtime logic. Codex must compile each brief into typed objectives, legal sockets, navigation/hazard graphs, parameterized events, reference replays, and a validated content bundle. A written solution is not evidence that a level is solvable.

## Shared narrative and art structure

The rescue organization is fictional. Recurring roles are a dispatcher, a driver, an engineer, and a boat pilot. Use a small set of stylized original character designs with distinct silhouettes. Their dialogue is short and situational: warnings, acknowledgment, a useful clarification, or a recovery detail. No talking-head exposition or AI-generated conversations. Avoid real-world disasters, identifiable people, national caricatures, and real corporate/rescue-service insignia.

The first world teaches; the second alters hazard control; the third adds vertical logistics; the fourth adds time windows and power routing; the fifth combines prior mastery. Each world needs a recognizable landmark, different evacuation geometry, and an aftercare transformation.

## W1 — Alder Valley

**Identity:** A stepped river valley with cedar houses, terraced fields, a stone viaduct, a bright rescue depot, and a mountain reservoir.
**Mechanical thesis:** Redirect water without transferring danger onto an evacuation route.
**Board geometry:** North reservoir → central fork → west farms / east village → south outlet. A high road connects the depot to the viaduct; two docks have different flood tolerances.
**Art palette:** foliage #355D47; water #438B9B; roof #C56646; stone #D8CCAF; rescue #FF6B35.
**Sound direction:** Water over stone, low timber creaks, a gentle three-note rescue acknowledgment; restrained acoustic plucks.
**Aftercare:** A repaired bridge carries the bus past a dry village square. The final postcard animates a river lantern festival.
**New tools:** barrier, bridge, pump, skiff, beacon, generator.

### W1-M1 — First Crossing

**Target duration:** 6–7 minutes including decisions; hard simulation limit 360 seconds. **Residents:** 8; minimum rescue goal 6. **Supply budget:** 12; default deployment cost 12.
**Default kit:** 1 × bridge, 2 × barrier, 1 × skiff, 1 × beacon.
**Decision:** Learn bridge placement, flood forecasts, and a first boat route.
**Layout:** Bus group of 4 at the west crossing; 4 residents at the low dock; depot and high landing are safe.

**Threat sequence:** 60s: Reservoir inflow increases; highlight the fork forecast.; 140s: Low-dock water rises toward closure threshold.; 240s: Final moderate surge tests the placed barrier.

**Required outcome:** Evacuate at least 6 residents; deliver the bus group to the depot.
**Three-star mastery:** Rescue all 8 and keep the low dock usable until its group boards.
**Reference approach A:** Bridge the bus crossing, divert water west, then skiff the dock group.
**Reference approach B:** Divert first, skiff the low dock, then build the bridge before the last surge.
**Failure to communicate:** Leaving the dock above the flood threshold too long closes boarding; backup transport recovers the unrescued group.
**Memorable payoff:** The first passengers turn and wave as the bridge unfolds.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W1-M2 — The Lower Road

**Target duration:** 7–8 minutes including decisions; hard simulation limit 420 seconds. **Residents:** 12; minimum rescue goal 9. **Supply budget:** 16; default deployment cost 15.
**Default kit:** 1 × pump, 1 × generator, 1 × skiff, 2 × barrier, 1 × beacon.
**Decision:** Understand downstream consequences and dry power placement.
**Layout:** Three groups of 4: orchard, river houses, and ferry landing; pump has two legal outlet regions.

**Threat sequence:** 45s: Rain begins in the upper basin.; 130s: A warning reveals the lower-road closure forecast.; 250s: Upper inflow peaks; a low generator platform becomes unsafe.

**Required outcome:** Evacuate at least 9; maintain power at the medical station at mission end.
**Three-star mastery:** Rescue all 12 without exposing the orchard assembly point.
**Reference approach A:** Put the generator on high ground and pump into the empty basin while the skiff cycles.
**Reference approach B:** Use paired barriers to spare the road, ferry the orchard first, and reserve the pump for the late surge.
**Failure to communicate:** An outlet into the orchard transfers the hazard to waiting residents; debrief displays the actual flow path.
**Memorable payoff:** A pumping hose visibly bulges while a previously flooded road reappears.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W1-M3 — Two Banks

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 14; minimum rescue goal 11. **Supply budget:** 17; default deployment cost 17.
**Default kit:** 1 × bridge, 2 × skiff, 2 × barrier, 2 × beacon.
**Decision:** Allocate transport capacity between separated groups.
**Layout:** West village has 8 residents; east workshop 6. One bridge socket connects only the high paths, not both docks.

**Threat sequence:** 70s: A central surge closes the low footpath.; 180s: Debris blocks one boat lane after a clear warning.; 320s: The east landing becomes the shorter remaining route.

**Required outcome:** Evacuate at least 11; empty the east workshop group.
**Three-star mastery:** Rescue all 14 with no idle fully loaded skiff at the end.
**Reference approach A:** Bridge the high road for western pedestrians; use both boats on eastern and overflow groups.
**Reference approach B:** Evacuate east early with one skiff, route the other west, then bridge the final safe path.
**Failure to communicate:** A boat assigned to a closing lane waits at a safe node; the player must reassign it.
**Memorable payoff:** Two boats cross beneath the viaduct carrying different rescue groups.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W1-M4 — Power Before Water

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 16; minimum rescue goal 12. **Supply budget:** 18; default deployment cost 18.
**Default kit:** 2 × pump, 2 × generator, 1 × barrier, 1 × skiff.
**Decision:** Trade early protection against immediate evacuation.
**Layout:** Clinic, tram depot, and riverside housing share a power bottleneck. Dry source sockets are scarce but sufficient.

**Threat sequence:** 50s: First spill enters the clinic basin.; 160s: Tram passengers reach the assembly point.; 280s: One low connection shuts down unless protected.; 380s: Final spill threatens the second basin.

**Required outcome:** Evacuate at least 12; preserve the clinic power connection at end.
**Three-star mastery:** Rescue all 16 while keeping both generators operational.
**Reference approach A:** Protect the dry power spine and alternate pump assignments to lower both basins.
**Reference approach B:** Evacuate riverside first, then dedicate both pumps to the clinic/tram sector.
**Failure to communicate:** Overcommitting power leaves a pump queued; the UI names the inactive consumer.
**Memorable payoff:** The town’s lights return in a visible chain when power is restored.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W1-M5 — The Long Way Home

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 20; minimum rescue goal 15. **Supply budget:** 20; default deployment cost 20.
**Default kit:** 1 × bridge, 1 × pump, 1 × generator, 1 × skiff, 3 × barrier, 1 × beacon.
**Decision:** Combine flow, power, routing, and a deliberate equipment tradeoff.
**Layout:** Four groups of 5 occupy the upper village, orchard, workshop, and island landing. Three elevations create alternate safe routes.

**Threat sequence:** 60s: First surge reveals the main fork.; 170s: A forecast landslide closes the short road.; 290s: Reservoir overflow increases source flow.; 420s: Late inflow tests the downstream basin.

**Required outcome:** Evacuate at least 15; restore a continuous route between depot and upper village.
**Three-star mastery:** Rescue all 20 and finish with no stranded loaded vehicle.
**Reference approach A:** Build the long bridge route, protect generator ground, and ferry the isolated island.
**Reference approach B:** Prioritize island and orchard with the skiff, then redirect the late surge and open the land route.
**Failure to communicate:** A poor early diversion consumes safe basin capacity and forces a longer evacuation; no instant unannounced loss.
**Memorable payoff:** The final bus and skiff arrive together while the town’s rescue bell rings.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

## W2 — Emberwood

**Identity:** An inhabited forest reserve with a lookout tower, sawmill, campsite, orchard, and hillside water tanks.
**Mechanical thesis:** Create defensible transport routes by managing fuel, water, power, and forecast wind.
**Board geometry:** Three forest sectors connect through a road loop. Fire can cut the shorter road; the longer road passes a refill station. The lookout is a visible timing landmark.
**Art palette:** foliage #254D42; bark #6B4937; ember #E98A43; smoke #B4AEA1; sky #D1DDD4.
**Sound direction:** Dry needles, distant birds, restrained fire crackle; percussion increases only near an actual route closure.
**Aftercare:** Rain arrives after stabilization, not as a deus ex machina that solves the mission. Residents replant the cleared strip.
**New tools:** firebreak, sprinkler, crawler.

### W2-M1 — A Line in the Trees

**Target duration:** 6–7 minutes including decisions; hard simulation limit 360 seconds. **Residents:** 10; minimum rescue goal 8. **Supply budget:** 13; default deployment cost 13.
**Default kit:** 2 × firebreak, 1 × sprinkler, 1 × generator, 3 × beacon.
**Decision:** Learn fuel removal and moisture.
**Layout:** Campsite and lookout connect through two wooded paths; one tank supplies both sprinkler sockets.

**Threat sequence:** 50s: A marked dry-cell cluster ignites.; 160s: Steady wind advances heat toward the short path.; 260s: The first cooled strip becomes safe to cross.

**Required outcome:** Rescue at least 8; preserve a path to the lookout.
**Three-star mastery:** Rescue all 10 while retaining water in the tank.
**Reference approach A:** Break the nearest fuel strip and wet the approach.
**Reference approach B:** Defend the longer path and stage the campsite first.
**Failure to communicate:** A gap in the firebreak permits predictable spread.
**Memorable payoff:** The fire stops along the exact strip the player cleared.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W2-M2 — Last Bus from Camp

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 18; minimum rescue goal 14. **Supply budget:** 18; default deployment cost 18.
**Default kit:** 2 × crawler, 2 × firebreak, 1 × sprinkler, 1 × generator.
**Decision:** Coordinate finite ground transport capacity.
**Layout:** Three camps each hold 6 people; two road loops meet at a refill station.

**Threat sequence:** 60s: One road becomes threatened.; 190s: A wind warning marks the alternate loop.; 330s: The refill station approaches heat threshold.

**Required outcome:** Rescue at least 14; unload every occupied crawler before finishing.
**Three-star mastery:** Rescue all 18 without losing the refill station.
**Reference approach A:** Stage one crawler on each loop and protect the common junction.
**Reference approach B:** Run both on the nearest camp first, then use the protected longer loop.
**Failure to communicate:** An empty vehicle on the wrong loop costs time but can be rerouted.
**Memorable payoff:** Campers board with tiny backpacks and return a lost flag.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W2-M3 — Water Is a Route

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 16; minimum rescue goal 12. **Supply budget:** 20; default deployment cost 20.
**Default kit:** 2 × sprinkler, 1 × generator, 1 × pump, 1 × crawler, 2 × firebreak.
**Decision:** Make water supply and transport choices interact.
**Layout:** A pond and storage tank feed separate areas; a pump link can refill the tank without protecting the road by itself.

**Threat sequence:** 40s: Orchard edge heats.; 150s: The tank drops below half unless replenished.; 300s: A second ignition threatens the return road.

**Required outcome:** Rescue at least 12; keep the orchard assembly route usable.
**Three-star mastery:** Rescue all 16 with neither water source exhausted.
**Reference approach A:** Pump pond water into the tank and alternate sprinklers.
**Reference approach B:** Use firebreaks to minimize wetting and reserve pumped water for the return road.
**Failure to communicate:** A sprinkler without water shows a visible dry gauge rather than pretending to operate.
**Memorable payoff:** A hose network creates a brief corridor through a glowing forest edge.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W2-M4 — When the Wind Turns

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 20; minimum rescue goal 15. **Supply budget:** 19; default deployment cost 19.
**Default kit:** 2 × crawler, 3 × firebreak, 1 × sprinkler, 2 × beacon.
**Decision:** Anticipate forecast direction changes.
**Layout:** Four groups use a road network with two dominant wind-exposed edges; a fixed powered tank is provided.

**Threat sequence:** 70s: Wind is stable westward.; 200s: A 40-second warning precedes an eastward turn.; 360s: A second turn threatens the unprotected flank.

**Required outcome:** Rescue at least 15; empty the lookout group.
**Three-star mastery:** Rescue all 20 without rebuilding a committed firebreak.
**Reference approach A:** Protect both flanks selectively and time transport before the turn.
**Reference approach B:** Concentrate early rescues on the future downwind side, then retreat along the safe loop.
**Failure to communicate:** Ignoring the visible wind timeline makes the formerly safe flank dangerous.
**Memorable payoff:** Leaves and smoke lean before the route warning changes.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W2-M5 — The Green Corridor

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 24; minimum rescue goal 18. **Supply budget:** 23; default deployment cost 23.
**Default kit:** 2 × crawler, 2 × sprinkler, 1 × generator, 3 × firebreak.
**Decision:** Maintain a living evacuation corridor through a combined burn.
**Layout:** Campsite, orchard, and sawmill each hold 8; two water tanks and one long safe route can support a coordinated solution.

**Threat sequence:** 50s: Outer forest ignition.; 170s: Sawmill access becomes threatened.; 310s: Wind shifts toward the central route.; 430s: Final heat peak reaches the corridor flank.

**Required outcome:** Rescue at least 18; preserve the central route at end.
**Three-star mastery:** Rescue all 24 and save the orchard pump house.
**Reference approach A:** Clear a continuous corridor and wet only its vulnerable bends.
**Reference approach B:** Evacuate the farthest group first and progressively retreat the transport line.
**Failure to communicate:** Protecting every decorative building leaves too little transport capacity.
**Memorable payoff:** The last crawler exits as residents begin planting the first new sapling.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

## W3 — Frostline

**Identity:** A snow-covered alpine settlement with a funicular, weather observatory, cable station, avalanche fences, and steep terraces.
**Mechanical thesis:** Stabilize selected structures and schedule vertical rescue before runout paths close.
**Board geometry:** Three elevation terraces and two cable links. Slope runout does not cover all routes simultaneously. Stable rock shelves provide alternate staging.
**Art palette:** snow #E8F1EF; rock #677D8C; pine #355458; roof #C54C43; signal #F4B64B.
**Sound direction:** Soft snow, cable tension, a clear warning bell, subdued glass-and-string score; no sudden full-volume cracks.
**Aftercare:** The funicular returns to service, carrying supplies rather than a compulsory victory cutscene.
**New tools:** brace, winch.

### W3-M1 — Hold the Station

**Target duration:** 6–7 minutes including decisions; hard simulation limit 360 seconds. **Residents:** 10; minimum rescue goal 8. **Supply budget:** 14; default deployment cost 14.
**Default kit:** 3 × brace, 1 × winch, 1 × generator, 2 × beacon.
**Decision:** Introduce structure-specific support and vertical transfers.
**Layout:** Two terraces connect at the cable station; one known weak support threatens the link.

**Threat sequence:** 60s: A support begins losing stability.; 160s: Meltwater increases load.; 260s: A minor tremor tests bracing.

**Required outcome:** Rescue at least 8; preserve the cable support.
**Three-star mastery:** Rescue all 10 with no station shutdown.
**Reference approach A:** Brace the weak support and winch the upper group.
**Reference approach B:** Evacuate the upper group early, then stabilize for the remaining transfer.
**Failure to communicate:** Bracing decorative rocks has no effect; only eligible supports accept placement.
**Memorable payoff:** Cable tension relaxes visibly when a brace locks into place.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W3-M2 — Above the Snowline

**Target duration:** 7–8 minutes including decisions; hard simulation limit 420 seconds. **Residents:** 14; minimum rescue goal 11. **Supply budget:** 17; default deployment cost 17.
**Default kit:** 2 × winch, 2 × generator, 2 × brace, 1 × beacon.
**Decision:** Manage independent rescue links and power.
**Layout:** Three terraces; two winch links and one sheltered intermediate node.

**Threat sequence:** 50s: Upper path closes under snow.; 160s: Sheltered node receives an arriving group.; 290s: One unbraced cable platform approaches instability.

**Required outcome:** Rescue at least 11; empty the top observatory.
**Three-star mastery:** Rescue all 14 without trapping residents at the intermediate node.
**Reference approach A:** Run both winches as a staged relay with separate power.
**Reference approach B:** Prioritize the observatory, then reuse the lower link for all groups.
**Failure to communicate:** Moving people uphill into a temporary staging node without onward capacity blocks the route.
**Memorable payoff:** Tiny lights descend the mountain in two synchronized lifts.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W3-M3 — The Quiet Slope

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 16; minimum rescue goal 12. **Supply budget:** 18; default deployment cost 18.
**Default kit:** 3 × brace, 1 × crawler, 1 × winch, 1 × generator, 2 × beacon.
**Decision:** Read runout zones and preserve a secondary route.
**Layout:** A broad slope threatens one road; a rock shelf provides a longer bypass.

**Threat sequence:** 90s: Slope stability warning appears.; 210s: A marked avalanche runout closes the short road.; 340s: Cooling makes one debris edge clearable in the authored route.

**Required outcome:** Rescue at least 12; evacuate the weather crew.
**Three-star mastery:** Rescue all 16 without entering the marked runout during release.
**Reference approach A:** Move the crawler through early and use the shelf afterward.
**Reference approach B:** Avoid the short road entirely and stage winch transfers above it.
**Failure to communicate:** A closed road stops vehicles at a safe node; it does not fling rigidbody residents.
**Memorable payoff:** A soft wall of stylized snow follows the warned corridor exactly.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W3-M4 — Cable and Current

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 18; minimum rescue goal 14. **Supply budget:** 20; default deployment cost 20.
**Default kit:** 2 × winch, 2 × generator, 2 × brace, 1 × pump, 1 × beacon.
**Decision:** Combine snow support and flooding power platforms.
**Layout:** A warming lower basin threatens generators while upper cables carry residents.

**Threat sequence:** 60s: Lower snowmelt starts.; 190s: Cable load increases.; 310s: A second meltwater source activates.

**Required outcome:** Rescue at least 14; keep one complete powered cable route available at end.
**Three-star mastery:** Rescue all 18 while keeping both lower platforms dry.
**Reference approach A:** Pump the basin before peak melt and brace the busiest cable.
**Reference approach B:** Evacuate the upper terrace before melt peaks, then shift power priority downward.
**Failure to communicate:** Ignoring the lower basin breaks a logically correct upper rescue plan.
**Memorable payoff:** A small stream passes beneath the rescued cable line rather than over its power box.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W3-M5 — Dawn at Frostline

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 22; minimum rescue goal 17. **Supply budget:** 22; default deployment cost 22.
**Default kit:** 2 × winch, 2 × generator, 3 × brace, 1 × crawler.
**Decision:** Coordinate a complete mountain evacuation.
**Layout:** Observatory, station, and village groups converge through limited safe terraces.

**Threat sequence:** 70s: Forecast warming begins.; 180s: First cable support warning.; 300s: Runout closes the valley shortcut.; 420s: Final transport window opens at the lower station.

**Required outcome:** Rescue at least 17; clear the observatory and station groups.
**Three-star mastery:** Rescue all 22 with no occupied vehicle or shelter left behind.
**Reference approach A:** Use a vertical relay before the runout, then transfer onto the crawler.
**Reference approach B:** Clear the lower village first to free receiving capacity for the mountain groups.
**Failure to communicate:** Using the receiving station as indefinite storage causes a capacity bottleneck.
**Memorable payoff:** The sun reaches the restored station as the last cabin arrives.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

## W4 — Tidemark

**Identity:** A fictional coastal archipelago with pale plaster houses, boatyards, a lighthouse, raised walkways, and a tidal market.
**Mechanical thesis:** Exploit safe tide windows and keep the power network dry while coordinating multiple transport legs.
**Board geometry:** Three islands linked by temporary causeways and marked boat routes. Tide phases open one route while threatening another; elevated relays connect critical services.
**Art palette:** sea #2B7F88; plaster #EEE0C5; roof #BB694B; algae #587868; night #273847.
**Sound direction:** Water under piers, restrained gulls, ropes and bells; spatial buoy notes communicate routes without stereotyping a real culture.
**Aftercare:** The lighthouse sweeps across safe harbor and the floating market reopens.
**New tools:** relay.

### W4-M1 — Between Two Tides

**Target duration:** 7–8 minutes including decisions; hard simulation limit 420 seconds. **Residents:** 12; minimum rescue goal 9. **Supply budget:** 15; default deployment cost 15.
**Default kit:** 2 × skiff, 1 × bridge, 2 × beacon, 1 × relay.
**Decision:** Learn readable tide windows.
**Layout:** Two islands connect by a low causeway and an all-tide boat route; a fixed dry source powers the receiving dock.

**Threat sequence:** 60s: Tide begins rising.; 180s: Causeway closes.; 300s: High landing becomes optimal for unloading.

**Required outcome:** Rescue at least 9; clear the low-market group.
**Three-star mastery:** Rescue all 12 without a loaded boat waiting at a closed dock.
**Reference approach A:** Walk the nearest group early and ferry the late group.
**Reference approach B:** Use both boats from the beginning and reserve the bridge for the high route.
**Failure to communicate:** An incorrect dock remains visible but rejects loading with a tide explanation.
**Memorable payoff:** Market umbrellas fold as the water reaches the tide ruler.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W4-M2 — Keep the Light

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 16; minimum rescue goal 12. **Supply budget:** 19; default deployment cost 19.
**Default kit:** 3 × relay, 1 × generator, 2 × skiff, 2 × beacon.
**Decision:** Keep a dry power route while transporting residents.
**Layout:** Three elevated relay sockets can route around a flood-prone connection to the lighthouse.

**Threat sequence:** 70s: Low cable channel floods.; 210s: Receiving dock needs lighthouse power for its safe route signal.; 350s: Peak tide threatens the shortest relay path.

**Required outcome:** Rescue at least 12; power the lighthouse at end.
**Three-star mastery:** Rescue all 16 with no power outage during final unloading.
**Reference approach A:** Build the longer elevated network immediately.
**Reference approach B:** Use the short network early, then transfer idle relays to the high sockets before peak tide.
**Failure to communicate:** Moving a relay while its only route is actively required temporarily blocks unloading.
**Memorable payoff:** A lighthouse beam returns exactly when the circuit closes.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W4-M3 — The Floating Market

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 20; minimum rescue goal 15. **Supply budget:** 21; default deployment cost 21.
**Default kit:** 3 × skiff, 2 × barrier, 2 × beacon, 1 × bridge.
**Decision:** Handle multiple pickups and staging capacity.
**Layout:** Four markets each hold 5; the safe island has limited docks but sufficient total refuge.

**Threat sequence:** 50s: First tidal lane opens.; 170s: A stall pontoon blocks one marked channel.; 300s: Current direction favors the outer loop.

**Required outcome:** Rescue at least 15; unload all occupied skiffs.
**Three-star mastery:** Rescue all 20 without exceeding any staging node capacity.
**Reference approach A:** Stagger arrivals through the two receiving docks.
**Reference approach B:** Evacuate one island fully, then reuse its high dock as a transfer point.
**Failure to communicate:** Assigning all boats to one dock creates a readable queue, not overlapping models.
**Memorable payoff:** Boats line up between colorful awnings and a miniature harbor crane.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W4-M4 — Storm Switch

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 22; minimum rescue goal 17. **Supply budget:** 23; default deployment cost 23.
**Default kit:** 2 × skiff, 2 × pump, 1 × generator, 2 × relay, 1 × barrier.
**Decision:** Combine rain inflow and sea-level changes.
**Layout:** Rain fills an inland basin while tide threatens pump outlets and the power network.

**Threat sequence:** 60s: Rain source starts.; 190s: Tide reduces discharge efficiency.; 320s: A brief low-tide window opens.; 440s: Final rain peak.

**Required outcome:** Rescue at least 17; keep the medical landing accessible at end.
**Three-star mastery:** Rescue all 22 without pumping into the occupied market basin.
**Reference approach A:** Store inflow behind a barrier, then pump during low tide.
**Reference approach B:** Keep high docks available and prioritize boat evacuation while managing only the critical basin.
**Failure to communicate:** A pump outlet submerged under a tide has reduced effect, clearly shown in its forecast.
**Memorable payoff:** Two different water levels become visible across the barrier line.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W4-M5 — Harbor of Everyone

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 26; minimum rescue goal 20. **Supply budget:** 26; default deployment cost 26.
**Default kit:** 3 × skiff, 2 × relay, 1 × generator, 1 × bridge, 2 × barrier.
**Decision:** Master transport, tide windows, and power topology.
**Layout:** Three islands, two causeways, four pickup groups, and a lighthouse-backed safe harbor.

**Threat sequence:** 60s: Rising tide closes the first causeway.; 180s: Power warning at the harbor.; 300s: Outer route becomes faster with the current.; 430s: Last high-water phase closes low docks.

**Required outcome:** Rescue at least 20; restore lighthouse power and empty every temporary staging dock.
**Three-star mastery:** Rescue all 26 with the high causeway still usable.
**Reference approach A:** Prioritize low docks, then use the outer loop for the final groups.
**Reference approach B:** Open the high causeway and use boats only for isolated islands while protecting the network.
**Failure to communicate:** Saving the lighthouse alone cannot satisfy the rescue goal.
**Memorable payoff:** The harbor fills with rescued boats under a single calm sweep of light.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

## W5 — Cinder Reach

**Identity:** A volcanic island research town with basalt terraces, a geothermal station, orchards, a ferry terminal, and cooling channels.
**Mechanical thesis:** Trade temporary protection against permanent evacuation while slow hazards remove routes.
**Board geometry:** Heat channels divide the board into three sectors. Coolant can open a temporary crossing; shelters create a safe pause but must be emptied before completion.
**Art palette:** basalt #3B4248; lava #EF7145; steam #D3DDDA; orchard #5C7651; sea #356C7A.
**Sound direction:** Low geothermal rumble and sparse metallic tones; cooling produces a soft chime that communicates a traversable crossing.
**Aftercare:** The rescue fleet leaves together; recovered residents send postcards from every earlier world.
**New tools:** cooler, shelter.

### W5-M1 — A Path Through Heat

**Target duration:** 7–8 minutes including decisions; hard simulation limit 420 seconds. **Residents:** 12; minimum rescue goal 9. **Supply budget:** 16; default deployment cost 16.
**Default kit:** 2 × cooler, 1 × bridge, 3 × beacon, 1 × shelter.
**Decision:** Introduce temporary cooled crossings.
**Layout:** Two heat channels divide an orchard from a high landing; one shelter socket lies outside the permanent safe zone.

**Threat sequence:** 70s: Lava advances into a marked channel.; 210s: First cooling opportunity appears.; 330s: Cooled segment begins reheating.

**Required outcome:** Rescue at least 9; empty the temporary shelter before completion.
**Three-star mastery:** Rescue all 12 before the first crossing reheats.
**Reference approach A:** Cool a crossing only when residents are ready to move.
**Reference approach B:** Stage residents in shelter, cool the longer route, then cross together.
**Failure to communicate:** Cooling too early wastes the safe window; the timer remains visible.
**Memorable payoff:** A black crust forms under a cooling plume and opens a narrow path.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W5-M2 — Borrowed Safety

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 18; minimum rescue goal 14. **Supply budget:** 22; default deployment cost 22.
**Default kit:** 2 × shelter, 2 × cooler, 1 × crawler, 4 × beacon.
**Decision:** Use refuge as a temporary logistics tool, not a destination.
**Layout:** Two groups need protection while the crawler travels between sectors.

**Threat sequence:** 60s: Heat advances toward the first assembly point.; 180s: Shelter protection starts being consumed.; 320s: The outer route opens after a cooling phase.

**Required outcome:** Rescue at least 14; both temporary shelters empty at end.
**Three-star mastery:** Rescue all 18 without exhausting a shelter protection interval.
**Reference approach A:** Stage one group, evacuate the other, then return along the cooled route.
**Reference approach B:** Use both shelters briefly and alternate transport pickups before heat peaks.
**Failure to communicate:** Leaving residents in shelters fails the evacuation condition even if they remain safe.
**Memorable payoff:** Shelter lamps change state as the last group boards the crawler.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W5-M3 — The Cooling Works

**Target duration:** 8–9 minutes including decisions; hard simulation limit 480 seconds. **Residents:** 20; minimum rescue goal 15. **Supply budget:** 24; default deployment cost 24.
**Default kit:** 3 × cooler, 2 × pump, 1 × generator, 1 × relay, 1 × shelter.
**Decision:** Protect coolant logistics and power.
**Layout:** A cooling plant supplies finite coolant; water drainage protects its generator and relay.

**Threat sequence:** 50s: Drainage basin begins filling.; 170s: Heat threatens coolant delivery.; 310s: A second channel cuts the default route.

**Required outcome:** Rescue at least 15; keep the cooling works available at end.
**Three-star mastery:** Rescue all 20 while retaining one coolant charge.
**Reference approach A:** Secure drainage and power first, then cool the short route.
**Reference approach B:** Evacuate via the longer path and save cooling for the late channel.
**Failure to communicate:** Power loss stops production but does not erase stored coolant.
**Memorable payoff:** The plant’s pipes light in sequence and a safe corridor opens.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W5-M4 — Three Ways Out

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 24; minimum rescue goal 18. **Supply budget:** 27; default deployment cost 25.
**Default kit:** 2 × skiff, 1 × crawler, 2 × cooler, 1 × shelter, 1 × bridge.
**Decision:** Choose among sea, road, and temporary crossings.
**Layout:** Three sectors support different transport types; all routes share limited final receiving capacity.

**Threat sequence:** 70s: Road warning.; 200s: Heat closes the short land route.; 330s: Sea route improves as debris clears.; 440s: Final cooling window.

**Required outcome:** Rescue at least 18; clear the research crew and all temporary refuge.
**Three-star mastery:** Rescue all 24 with no stranded vehicle load.
**Reference approach A:** Ferry the coast early, then use crawler and cooling inland.
**Reference approach B:** Use shelter to stage inland groups while boats clear the receiving dock.
**Failure to communicate:** Overloading one final landing creates a queue; capacity indicators must predict it.
**Memorable payoff:** A boat and crawler transfer groups at the same tiny pier.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

### W5-M5 — Every Little Life

**Target duration:** 9–10 minutes including decisions; hard simulation limit 540 seconds. **Residents:** 30; minimum rescue goal 23. **Supply budget:** 30; default deployment cost 30.
**Default kit:** 2 × skiff, 1 × crawler, 2 × cooler, 1 × shelter, 1 × generator, 1 × pump, 1 × barrier.
**Decision:** Final combined-system rescue with meaningful priorities.
**Layout:** Five groups of 6 span an orchard, station, research terraces, waterfront, and high village. No new mechanic is introduced.

**Threat sequence:** 60s: Rain and heat begin in separate regions.; 170s: First transport route warning.; 290s: Generator platform needs drainage.; 400s: Last heat front approaches the staging zone.

**Required outcome:** Rescue at least 23; empty all temporary shelters and restore the final ferry route.
**Three-star mastery:** Rescue all 30 without any equipment loss.
**Reference approach A:** Stabilize the power/drainage spine, ferry coast groups, and cool the inland route just in time.
**Reference approach B:** Evacuate the most exposed groups first, use shelter for the high village, and dedicate late resources to the ferry connection.
**Failure to communicate:** A plan that protects scenery but delays evacuation cannot earn a successful outcome.
**Memorable payoff:** Every rescued group joins a single departure; the credits can be skipped without interrupting the final save.

**Level acceptance:** Both reference approaches must be implemented as command replays and pass on the same shipped configuration. Validate all resident counts, resources, legal paths, warning lead times, safe receiving capacity, and empty-vehicle end states. Test a no-input run, a single major mistake, a tracking interruption, and a saved/resumed run.

## Alder Valley authoring coordinates

Use a 32×24 logical grid, origin at the southwest of the board. These are layout anchors, not complete hydraulic terrain data. Reservoir: (15,22). Fork: (15,15). West basin: (7,12). East village: (23,12). Orchard: (8,7). Depot: (15,3). High landing: (25,4). South outlet: (16,0). Viaduct: endpoints (11,10) and (19,10). Upper settlement: (8,19).

Maintain a minimum of two distinct drainage destinations and a high-ground land route. Author terrain heights and conductance explicitly; do not trust visually plausible terrain to produce valid flow. The first mission closes unused regions with natural cliffs, not invisible collision surprises. Later missions reuse the kit but alter access and threat configuration.

## Challenge modifiers

| Modifier | What changes | Fairness requirement |
|---|---|---|
| Early surge | A known inflow event occurs 30 seconds earlier | Brief and forecast update; both reference plans are retested |
| Light kit | Supply budget lower by 2 | A validated cheaper default loadout replaces the original |
| Alternate dock | One pickup moves to a different legal landing | Travel time and capacity remain solvable |
| Crosswind | One Emberwood wind phase reverses | Runout/fire warnings retain sufficient lead time |
| High tide start | Tidemark begins later in its cycle | Players see a complete tide timeline before Start |
| Careful operation | Equipment-loss mastery target set to zero | No mandatory unavoidable loss in the level |

Do not combine modifiers freely until the combinations have been validated. Store each shipped combination as a distinct challenge definition. Cosmetic seeds must never alter navigation or hazards.

## Content production gate

A world is complete only when it has a final modular kit, finished sound set, all five validated missions, final onboarding for its new mechanic, aftercare content, clean memory transitions, all accessibility paths, and real device captures. A generated screenshot or imported landscape mesh does not satisfy this gate.
