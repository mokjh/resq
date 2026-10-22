# RESQ runtime rules — frozen design 1

Freeze: `resq-spec-freeze-1.1.0`. Rules ID: `resq-rules-1`. Status: **specified, unimplemented, unbalanced, not device-qualified**. This document resolves the numerical and ordering gaps in the original production pack; it does not modify that pack. `03_RUNTIME_CONTRACTS.md` defines serialization and interfaces. `content/mission_contracts.json`, `content/equipment_rules.json`, and `content/modes_and_challenges.json` supply mission-specific values. A disagreement is a compile error, not permission to choose silently.

Numbers below are the first executable design, not measured realism, fun, performance, or solvability. A failing reference solution requires a documented revision to the affected content/rules and new evidence. Never relax an invariant or expected result to qualify it. Geometry production and real reference replays remain assigned implementation gates.

## 1. Integer units and limits

Authoritative quantities use signed 64-bit integers unless a smaller type is explicitly named. Intermediate arithmetic is checked. No float, render delta time, PhysX result, animation callback, wall clock, or dictionary enumeration decides a result.

| Quantity | Unit / admitted range |
|---|---|
| Operation and scenario tick | 100 ms; nonnegative; at most 1,000,000 per run |
| Logical coordinate/elevation | mm; coordinate -1,000,000..1,000,000; water elevation -10,000..100,000 |
| Water area | mm²; 10,000..10,000,000; default 1,000,000 |
| Water volume per node | mm³; 0..1,000,000,000,000 |
| Water source/sink/pump rate | mm³/scenario second; 0..1,000,000,000 |
| Conductance | mm²/scenario second; 0..1,000,000 |
| One requested water transfer | mm³/pulse; capped at 100,000,000 |
| Heat, wetness, fuel, effective stability | dimensionless integer points, 0..1000 |
| Cost, stock, people, seats, power | integer counts; no fractional people/resources |
| Content size ceilings | 4096 water nodes, 16384 hydraulic links, 4096 nav nodes, 16384 nav edges, 256 residents, 32 vehicles, 512 equipment instances |
| Hydraulic fan-in/fan-out | at most 32 requests per node, including pump/drain/sprinkler requests |
| Objective/event ceilings | 128 objectives, 2048 scheduled events, 256 milestones; condition depth 4 and 16 leaves |

These are safe-format ceilings; the lower presentation/performance budgets in the original technical specification still apply. Compile rejects out-of-range values. Runtime bound/overflow failure is a `SIMULATION_INVARIANT_FAULT`: stop the run at the preceding valid boundary, preserve diagnostics, award nothing. Never clamp excess water away.

`floor_div(a,b)` means mathematical floor, including negative `a`; `b > 0`. `ceil_div(a,b) = -floor_div(-a,b)`. Ordinary nonnegative divisions truncate downward. Rounding to nearest uses ties upward for nonnegative scores. Stable ordering is ordinal ASCII ID order.

The board has a 32×24 logical region at 1000 mm pitch. Wide and compact layouts use the same scalar simulation region; physical scaling, placement, rotation and anchor recovery never change these units or state. The agreed physical boards are 0.72×0.54 m and 0.60×0.45 m.

## 2. Clocks, tutorial, phase and pause

There are two counters, not an arbitrary time-scale slider:

- `operation_tick O` advances once per accepted 100 ms fixed step. Movement, boarding, winch duty and input command execution use O.
- `scenario_tick S` advances only on a hazard pulse. Hazards, event schedules, supply production/consumption rates, exposure, protection, tide and mission deadline use S.
- Standard and Command add 2 to `scenario_accumulator` each operation step; Relaxed adds 1. The denominator is always 2. When accumulator ≥2, subtract 2 and increment S once. Initial accumulator is 0. Relaxed therefore advances S on O=2,4,6,... . Movement retains its normal pace.
- The authoring profile's `warning_operation_time_multiplier=2` for Relaxed describes this doubled operation-time warning duration. It does not multiply authored scenario warning ticks again. Standard/Command authoring rates 1/1 normalize to accumulator values 2/2; Relaxed 1/2 remains 1/2.
- `Advance 1 second` means exactly 10 operation steps; `Advance 5 seconds` means 50. In Relaxed these advance hazard time by 0.5 and 2.5 seconds respectively. The UI displays both meanings.
- Paused, tracking/anchor-interrupted, suspended, loading, outcome, debrief and aftercare states advance neither counter. Resume is an immediate application command; it is never queued behind a paused simulation step. A two-second presentation countdown advances neither counter.

Initialization sets O=S=0, applies the compiled initial values and evaluates initial state. Authored event effects satisfy `1 <= at_scenario_tick < duration_scenario_ticks`; effects at zero or at the deadline are invalid. Initial parameters belong in the compiled initial state, not tick-zero effects. Warnings satisfy `0 <= warning_at_scenario_tick <= at_scenario_tick`. During new-run initialization, emit every warning assigned to S=0 exactly once in event-ID order, before the initial objective sample and before the first Active step. Persist their event IDs in `warned_event_ids`; resume, replay from an initialized snapshot and forecast cloning do not emit them again. The initial operation phase is Planning. `resume` starts Active after the countdown. No automatic clock catch-up occurs after suspension or a long frame; the scheduler limits work to four steps in one render frame and carries at most four steps of ordinary frame debt. Excess real-time debt is discarded and recorded as performance telemetry, not converted into extra rescue damage. Replays consume explicit steps independent of this presentation scheduler.

W1-M1 guided first play is an explicit policy: the bridge is available first, the fixed bus travels once the bridge route opens, then barrier and skiff guidance unlocks. O and bus movement run; S and its accumulator remain zero until the bus group has reached the depot and the river-warning step is acknowledged. At that boundary emit `operation_started` and enable the scenario clock. This pause of the hazard epoch is recorded in the comparison partition as `guided_first_play`. Unguided replay exposes the whole permitted loadout; it emits `operation_started` at O=S=0 and supports reference strategies A and B. Other worlds' guidance does not freeze S after Start.

The application flow is Boot → Hub → permission/placement → Briefing/Loadout → Planning → Active → Stabilizing → Outcome → Debrief/Aftercare. Stabilizing means that success is currently achievable; it has no second hidden deadline and hazards continue. Return to Active if required conditions cease to hold. The player may continue until the hard scenario deadline to improve the result. Loading a current run or recovering placement returns to the saved phase and selected layout, bypassing briefing/loadout. Ordinary resume does not mark assistance. Explicit rollback to an earlier checkpoint does, permanently for that attempt.

## 3. A complete operation step

Only complete step boundaries may be persisted or published. The following order is normative:

1. Increment O. If the scenario clock is enabled, update the accumulator and, when due, increment S. Record whether this step has a hazard pulse.
2. On a hazard pulse, first evaluate each existing tide controller at S. Then publish warnings scheduled for S that are absent from `warned_event_ids`, record their IDs, and apply scheduled parameter operations at S, ordered by event ID then operation array index. Tick-zero warnings have already been emitted by initialization. A new tide ramp therefore captures the old controller's level at this exact S, not its preceding-tick level. Reject content with conflicting writes to one parameter at one tick. Warning-only operations never apply the warned effect.
3. Revalidate due simulation commands against the current state, after scheduled changes; apply accepted commands atomically in sequence order. Record rejected commands without cost. `end_operation` records an end request; it does not skip the rest of this step.
4. Derive power grants from the current network and existing hazard state. Resolve explicit tool activations and finite-store reservations. This is the supply available for the interval about to simulate.
5. On a hazard pulse, compute water allocations, spray grants, fire, stability/runout, lava/cooling, protection and production from their specified old-state inputs. Publish their next states together. Newly arrived water cannot flow out again in this pulse. Inter-system reads use the dependencies defined below, never component execution order.
6. Recompute power safety from the new hazard state, close unsafe admission edges/docks, and apply equipment strain/damage. Broken barrier conductance changes the following pulse. Closure affects movement in this same operation step.
7. Resolve existing movement recovery; then move vehicles/residents, board/unboard, and admit new movements. An arrival or a passenger transfer completes at the end of this stage. New admissions begin progress on the next O. Queue order is `(request_operation_tick, request_sequence, entity_id)`.
8. Derive resident rescue/fallback state, metrics and declared milestones; evaluate temporal objective monitors. Record ordered typed events.
9. If the deadline `S >= duration_scenario_ticks` is reached, or an accepted end request exists, emit `operation_ended`, evaluate terminal conditions, stars and score, and enter Outcome. Otherwise update Active/Stabilizing. Publish the state hash and optional checkpoint.

Stage 5 has these explicit dependencies: water uses the stage-2/3 parameters and old volumes; fire uses old fire neighbours plus the actual newly allocated spray; support load uses pre-movement resident locations, and a resulting collapse can activate its bound runout; lava uses its scheduled arrivals and stored cooldown timers; shelter protection uses the resulting hazard fields and its pre-pulse balance. Production uses stage-4 power grants for that interval. Thus a generator newly flooded in this interval may finish its already powered production interval, then shuts down at stage 6. Protection granted by the shelter's last unit remains effective through stages 6–8 even though its saved remaining balance has become zero. These temporary interval grants are consumed before the published boundary and need not survive a snapshot.

There is **zero tolerance in tick comparisons**. A closure at S=100 occurs before boarding/movement at that step. A boarding operation whose final tick is that step cannot complete at a now-closed dock. A resident safely arriving at stage 7 on the deadline step can count: deadline evaluation is stage 9. A strict `ever_before` objective cannot first succeed in the same operation step as its closing milestone. No epsilon, grace tick, frame-rate exception or input-mode exception changes these rules.

Application pause, cancellation, suspension and placement commands execute outside the step through the application controller at the current boundary. A raw tracking-loss callback immediately latches cancellation so the pending gesture cannot commit; the XR recovery policy controls the visible interruption. No application callback mutates half a simulation step.

## 4. Water, pumps and finite water stores

A water node is either a terrain cell or a finite tank/pond. Both own volume. A tank is not an inexhaustible sprinkler supply. A sea boundary owns a prescribed head but no stored volume; exchanges with it appear in source/sink ledgers.

For each direct source on a hazard pulse:

```text
n = rate_mm3_per_second + source_remainder
injected = floor_div(n, 10)
source_remainder = n % 10
B[cell] = old_volume[cell] + sum(injected)
head[cell] = elevation_mm + floor_div(B[cell], area_mm2)
```

Direct source additions occur before request calculation. `B` is the immutable availability vector for this pulse. Every hydraulic edge has a sill elevation, conductance, maximum transfer and allowed direction. For an open bidirectional edge, select the higher-head end as upstream; equal heads request zero. A directed edge requests zero against its direction.

```text
effective_sill = base_sill + (intact_barrier_present ? 600 : 0)
driving_head = max(0, head_up - max(head_down, effective_sill))
requested_passive = min(max_transfer_mm3, floor_div(conductance * driving_head, 10))
```

A closed gate requests zero. A barrier raises the sill; it does not erase water and can overtop. Passive transfers have no fractional accumulator: sub-mm³ requests are zero, without violating mass conservation.

An active powered pump uses a directed source→destination pair from its socket contract. It has 20,000,000 mm³/s nominal flow and 5000 mm maximum lift:

```text
lift = max(0, destination_head - source_head)
factor = max(0, 5000 - lift)
n = 20_000_000 * factor + pump_remainder
requested_pump = floor_div(n, 50_000)
pump_remainder = n % 50_000
```

An inactive/unpowered pump requests zero and keeps only its fractional remainder; unmet whole-volume requests are never backlogged. Greater sea/outlet head therefore reduces discharge without a separate tide cheat. Maximum lift gives zero request. Pump rates in future revisions use the same denominator and declared maximum lift.

Drains and sprinklers request volume using the source-rate accumulator formula. A sprinkler requests 5,000,000 mm³/s from one finite named water node. The actual granted volume, not the requested volume, creates spray/wetness. All outward passive, pump, drain, boundary-outflow and sprinkler requests from a node compete **together**. Their stable IDs begin `edge:`, `pump:`, `sink:` or `sprinkler:` followed by the immutable instance/link ID.

For requests `q_i`, available `V=B[cell]`, and `Q=sum(q_i)`:

- If Q≤V, grant every q_i.
- Otherwise grant `a_i=floor(V*q_i/Q)`. Compute `r_i=(V*q_i)%Q`. Award each remaining unit, once, in descending r_i order, ties by request ID. There are fewer remaining units than positive requests.
- The constrained case has `V<Q≤32×100,000,000`; therefore `V*q_i<3.2×10^17`, inside signed 64-bit. Do not multiply by V unnecessarily in the unconstrained case.

Apply all grants simultaneously. A finite destination receives its incoming grants only in the published next volume. Sea inflow is an independently capped incoming request from an unlimited external source; it cannot be re-exported during this pulse. Sea outflow competes for the cell's B volume. Emit separate injected, sea-in, drained, sea-out and sprayed totals.

```text
sum(new_volume) = sum(old_volume)
                + direct_sources + sea_in
                - drains - sea_out - sprayed_water
```

Internal flow and pumps cancel exactly in this equation. Evaporated sprinkler water is a recorded sink; fire does not silently subtract reservoir volume. Validation checks bounds before promotion. Default sockets and terrain heights are authored content, not inferred from meshes.

Golden examples in `fixtures/runtime-golden-v1.json`: three equal requests sharing five units grant 2/2/1 by ID; source+edge+pump+sink competition preserves exactly 800 mm³ from 1000 initial plus 200 injected minus 400 drained; a 3 mm³/s source injects exactly three units over ten pulses; a pump with a 2500 mm lift requests 1,000,000 mm³ per pulse.

## 5. Power and equipment lifecycle

Power is a graph of authored nodes/links and enabled relays. Compute connected components in ID order. Each safe active generator supplies 2 integer units. Consumers are sorted by `(player_priority, consumer_id)`, smaller priority first; initial priorities are infrastructure 0, winch 10, pump 20, sprinkler 30, cooling plant 40. Grant full demand or zero, then continue to the next consumer. There is no partial power or random flicker. The player can explicitly reorder all consumers, including infrastructure.

A generator automatically shuts down when its platform depth exceeds 50 mm. It stays latched off until an explicit activation at depth≤40 mm. Count one `generator_shutdowns` transition, not one per tick. A low relay connection similarly becomes unavailable above 50 mm, but automatically reconnects when depth≤40 mm if still enabled and intact. Islanded power cannot cross a decorative cable. Fixed infrastructure demand is explicit content; zero-demand receiving nodes still require their other access conditions.

Deployment validates phase, mission permission, chosen stock, supply balance, socket type, orientation, footprint, current safety and all parameters before spending once. `maximum_stock` caps the selectable mission loadout; `default_loadout` is its preselected subset. The budget is charged when an instance is deployed, never at loadout selection. Stock means maximum lifetime deployments, including broken tools. A preview/cancel costs nothing. There are no refunds, resale, respawned consumables or restored durability through relocation.

<a id="equipment-defaults"></a>

| Tool | Rules-v1 behavior and eligibility |
|---|---|
| barrier | Cost 2; crest +600 mm; durability 100. Warn at absolute head difference≥600 mm; lose 1 durability/pulse while difference>800 mm. It breaks at 0. Relocate only when difference≤100 mm and both heads≤base sill+100 mm. |
| bridge | Cost 3; 2 movement slots. Pedestrian consumes 1, fixed bus/crawler 2. Deck depth>100 mm closes admission; >500 mm damages 1/pulse. Relocate only without occupants or reservations. |
| pump | Cost 3; power 1; head-dependent flow above. Activate only with a legal intake/outlet. Relocate only off with no pending work; remainder and durability travel with it. |
| skiff | Cost 4; 4 resident seats; 1 water movement slot. Spawn only at an eligible empty dock; thereafter use routes, never inventory relocation. No fuel in rules-v1. |
| beacon | Cost 1; selects an authored assembly/route destination, adds no capacity. Relocate only when no active group order depends on it. Assembly-panel group routing is also available in missions without beacons. |
| generator | Cost 3; supply 2; water shutdown/restart above. No fuel in rules-v1. Relocate only switched off and dry. |
| firebreak | Cost 2; one irreversible activation removing fuel from at most 8 tagged cells; rejects building cells and cells already Burning. Heat remains. No relocation after activation. Clearing cannot duplicate stock. |
| sprinkler | Cost 3; power 1; 5,000,000 mm³/s; at most 4 tagged fire cells; finite named source. Relocate only off. |
| crawler | Cost 4; 6 seats, 2 ground movement slots. No fuel in rules-v1. Route movement only after deployment. |
| brace | Cost 2; adds 200 effective stability points to its tagged structure; at most 3 per structure. Structural commit is irreversible; no relocation or slope-wide immunity. |
| winch | Cost 3; power 1; 2 seats; default crossing 50 operation ticks. 100 powered travelling ticks require 30 operation ticks rest at a safe endpoint before another trip. Occupied travel/boarding cannot be relocated. |
| relay | Cost 2; creates the named graph connection, supplies no power. Relocate only off; dependent operations can wait for power. |
| cooler | Cost 3; one initial coolant charge, capacity 1. One charge cools one eligible Hot segment; 50 scenario ticks cooling, then 200 scenario ticks of safe crust. Refill only from a bound finite store while idle. |
| shelter | Cost 4; 6 people; 600 scenario ticks of total protection, consumed only when occupied and externally hazardous. Relocate only empty; remaining protection is preserved. Never counts as final evacuation. |

The fixed W1 bus has four seats and two ground movement slots. It is a preplaced scenario vehicle, not a purchasable tool or an early crawler unlock. Its residents remain individually identified while aboard. The compiler gives its bridge route vehicle clearance. The first tutorial bus has a preassigned depot route that waits for legal access.

Relocatable equipment moves atomically between declared sockets in the same `relocation_zone`; it must be idle, eligible and hazard-safe at both ends. No source/destination occupancy overlap is observable. Vehicles, committed braces/firebreaks and occupied shelters are never portable. Orientation is one of 0,1,2,3 quarter-turns allowed by the socket.

All non-vehicle electronic tools and shelters have durability 100. Exposure depth>200 mm or fire heat≥600 damages them 1 per qualifying hazard pulse; there is no hidden extra damage timer. The presentation warns at depth>50 mm or heat≥300, and content qualification requires the relevant authored/emergent warning before a damaging pulse. An active shelter is protected during each interval for which it consumed a protection unit. Skiffs ignore water damage; vehicles take fire damage at the same heat threshold. Bridge/barrier water rules override the generic rule. A Hot lava segment disables unprotected equipment immediately on its warned arrival; a collapsing support destroys attached equipment. `Exhausted` consumables are not equipment losses; `Broken` is. Planned firebreak fuel removal and spent coolant never count as damage. Each broken instance increments loss once and remains in the ledger.

## 6. Transport, occupancy and closing routes

Seats, movement slots, node capacity and dock berths are separate quantities. A bus with four passengers occupies two edge slots; its four residents each occupy one seat and count separately. Boarding/unboarding defaults to 10 operation ticks **per resident per vehicle**, serially. Queue/resident ties use resident ID. A default dock has one berth. Node resident capacity and vehicle parking/berth capacity must be authored separately.

Graph paths have positive integer travel ticks and allowed traversal/mobility classes. Dijkstra minimizes `(total travel ticks, edge count, lexicographic edge-ID sequence)`. `standard` and `step_free` residents travel at the same rules-v1 speed, but step-free residents may use only step-free edges. Vehicles carry either mobility class. Route assignment to a group or vehicle uses approved nodes/waypoints; residents are never directly grabbed. Assignment changes future stops, never removes occupants.

Before entering an edge, reserve its movement slots, a compatible destination capacity slot and an origin return slot. Existing residents at the origin still occupy it while boarding; a resident on an edge has one transit location, while retained origin capacity is a reservation rather than a second resident. Release origin/edge reservations on arrival and convert destination reservation to occupancy. Capacity checks include reservations. Reserve only the next leg; do not claim an entire multi-leg network indefinitely. Positive travel times and safe-buffer capacity are content/replay checks.

Admission requires the edge currently open, a legal complete route to the next safe checkpoint, and no **known hard closure** before the predicted next-leg arrival. Use the fixed edge travel time captured at entry. Current/tide speed changes affect later admissions, not travellers already halfway across. If availability closes while a mover is in transit:

1. Stop new admissions immediately. If its reserved destination is still hazard-safe, complete the existing traversal as a bounded emergency egress, retaining the original remaining travel ticks. The edge is `closing` for those occupants until they exit; never admit another traveller using this allowance.
2. Otherwise, if the reserved origin remains safe, reverse along the same edge. If progress was p ticks, the return consumes p operation ticks. Freeze the reversal decision; a reopening cannot repeatedly flip direction.
3. If neither endpoint is safe, retain the precise edge/progress location, stop the mover, and place affected residents in `AwaitingBackup`. They do not teleport, count as rescued, board again or earn rewards. The presentation shows backup at that location. Reservations owned by that failed transfer are released once; the closed edge remains unavailable. An occupied failed vehicle is a terminal failed vehicle, not reusable stock.

These emergency-egress rules are part of the discrete strategy model, not collision/rigidbody physics. Geometry authoring must provide believable retained refuge/endpoint positions and readable warnings. No mission may rely on repeatedly exploiting emergency egress as its reference solution.

Dock boarding needs a berth, eligible depth, receiving/pickup capacity and any required power throughout each person's 10-tick operation. Until its final tick a boarding resident remains at the node with a seat reservation; until an unloading final tick the resident remains aboard with a node reservation. If power is lost, freeze progress and display the exact consumer waiting. If a dock closes, cancel the current incomplete passenger transfer, retaining the resident at its previous valid location and releasing only that transfer's reservation. Already completed passenger transfers remain completed. A loaded vehicle can take another safe route; it cannot be discarded.

Winch power loss while travelling engages a mechanical brake for at most 20 operation ticks. Power returning during that interval resumes the same trip. Otherwise perform a one-time emergency return to its reserved origin at one progress unit per operation tick using the safety mechanism; power restoration cannot reverse that decision. If the origin is unsafe, use AwaitingBackup at the current position. Waiting at an endpoint consumes no duty; rest counters advance only at a safe unoccupied endpoint. Full power loss does not magically complete a lift or drop passengers.

Arrival at a final evacuation node changes each resident to `Evacuated` once and records its final-leg mode. Arrival at an intermediate dock or temporary shelter does not. `AwaitingBackup` and `Evacuated` are terminal for this attempt. Total people across all states remains the declared population. Global success also requires every vehicle occupant array and every temporary shelter to be empty. AwaitingBackup occupants remain aboard their failed vehicle at its real location, so that attempt cannot pass this terminal safety condition. Backup residents waiting at ground nodes remain unrescued but do not by themselves prevent a partial success. Cleanup cannot convert a failed occupied vehicle into an empty successful one.

For a nonterminal resident at a node, a hazardous interval means water depth exceeds that node's `safe_depth_mm`, or a bound cell is Burning/Hot, or runout occupies the node. A vehicle occupant uses the vehicle's current node/edge envelope; skiff occupants ignore ordinary water depth within that route's admitted range, but not fire, runout or an out-of-range unsafe hold. Temporary shelter protection masks these exposure checks for the protected interval. Twenty consecutive unprotected scenario pulses cause AwaitingBackup at the actual location; a safe sample at any operation boundary resets this consecutive counter. Structural/transit failures use their explicit immediate recovery rules first. Terminal residents do not accrue exposure or change outcome.

## 7. Fire, wind, moisture and finite spray

Fire cells use fuel F, heat H and wetness W, all 0..1000, plus `Unburned`, `Heating`, `Burning`, `Cooling`, `SafeAsh`. Default fuel is 1000. Every hazard pulse uses old neighbour burning states. Wind codes are 0=calm, 1=west, 2=east, 3=north, 4=south, 5=northeast, 6=southeast, 7=southwest, 8=northwest. Links have the corresponding geometric direction.

Divide a sprinkler's actual granted volume equally among its target cells; distribute a remainder by cell ID. Per cell, accumulate spray numerator and increase W by one point per 25,000 mm³ applied, carrying the remainder. Then lose one wetness point per pulse, clamped at zero. Multiple sprinklers add their grants before this conversion; clamp W at 1000 and record over-wetting without creating more water.

For each old Burning neighbour, incoming heat is 12 downwind (within 45°), 4 upwind (within 45° of opposite), or 8 otherwise; calm gives 8. Compute using old neighbour state, independent of iteration. Let `extra_cooling` initially be 0, set by authored operations.

```text
heat_loss = 2 + floor(W / 100) + extra_cooling
self_heat = (old_state == Burning and F > 0 and W < 600) ? 25 : 0
next_H = clamp(H + sum(neighbour_heat) + self_heat - heat_loss, 0, 1000)
next_F = max(0, F - (old_state == Burning and W < 600 ? 3 : 0))
```

Unburned becomes Heating at H≥300. Heating may return to Unburned below 300. It may ignite at H≥600, F>0 and W<600 only after at least 100 scenario ticks of ignition warning. The compiler associates a scheduled `ignite_region` with its warning event, so its earliest valid ignition warning can precede the actual heat injection. Emergent Heating starts its own warning. Warning does not guarantee ignition if wetting/fuel control prevents it.

Burning becomes Cooling when fuel is zero or wetness≥600. Cooling lasts at least 100 scenario ticks and until H≤100; it then becomes SafeAsh if fuel is zero, otherwise Unburned. It cannot re-ignite while Cooling. Firebreak activation sets fuel to zero without deleting heat; a warm cleared cell enters Cooling and observes the same delay, while a zero-heat cleared cell becomes SafeAsh immediately. The equipment records irreversible activation and subsequent activations reject `RESOURCE_EMPTY`. SafeAsh has no combustible fuel unless a future rules revision explicitly introduces refuelling; rules-v1 does not.

Ground admission latches closed at cell heat≥300 or Burning/Cooling. Clear that latch only when H≤100 and state is Unburned or SafeAsh. A merely Heating cell that never burned may therefore reopen after cooling below 100 without a fabricated Burning interval; cells that entered Cooling must complete its 100-tick minimum. `set_region_cooling` changes heat-loss points per pulse; it never forces SafeAsh, opens a road or awards an objective. An ignition event injects heat, not injury. Resident exposure follows section 6; historical exposure metrics retain their peaks/violations after a safe sample resets the consecutive counter.

## 8. Structure stability and authored runout

Support `base_stability` is signed -1000..1000, initially 400 unless the layout explicitly supplies another value. Effective stability is `clamp(base_stability - static_load_points + 200*intact_braces, 0, 1000)`. Structural supports accept braces; terrain slopes do not. At most three brace sockets belong to one structure.

`set_stability_loss` sets points lost per scenario second, not per 100 ms pulse. Load adds `floor((static_load_points + 10*resident_load)/100)` points per second. `resident_load` counts the residents currently on the support's declared nodes/edges, including vehicle occupants. Accumulate the integer rate with denominator 10; subtract the emitted loss from base stability, lower-bounded at -1000. `set_support_load` changes static load; `tremor` immediately subtracts its points once. It never spawns a physics impulse.

At effective stability≤300, warn; above 400 clears an untriggered warning. Reaching zero arms collapse, but the actual collapse cannot occur until at least 100 scenario ticks after a relevant warning. Restoring stability above zero before that boundary cancels the pending collapse. A collapse disables its declared edges/stations for the rest of the mission and breaks attached equipment. It uses the transit-recovery rules, never rigidbody casualty tests.

Avalanches follow a preauthored directed acyclic runout graph. `release_avalanche` starts the first segment after its required warning; a non-null `source_support_id` also starts that runout once when that support collapses. A later duplicate trigger has no effect. Advance to each successor after that segment's authored travel delay, default 10 scenario ticks. Segment occupancy closes associated nav edges. Debris becomes passable after its declared cooling/settling duration, default 200 scenario ticks, only for edges tagged `debris_reopenable`. Collapsed structural edges remain closed. `set_edge_enabled(1)` removes an authored administrative block but cannot override an active avalanche, heat, flood or missing structure. Validate the whole runout against alternate routes and warning lead time.

## 9. Tide, currents and continuous water-level changes

Rules-v1 supports both a compiled piecewise-linear tide track and the mission event operation `set_tide_segment`. A mission chooses one controller per boundary; the current mission contracts use event ramps. No sine functions or float interpolation are authoritative.

For a fixed track, knots are `(scenario_tick, level_mm)` with strictly increasing ticks; optional repeat period ends at a knot matching the first level. Evaluate `y0 + floor_div((y1-y0)*(S-x0), x1-x0)`. A nonrepeating track holds its last value. If looping, use `S % period`.

For `set_tide_segment(index)`, capture the current level at the event tick and linearly ramp over 100 scenario ticks to the indexed target: 0→0 mm, 1→600 mm, 2→1000 mm, 3→800 mm. At the event tick the level is unchanged; at event+100 it equals the target. A new ramp interrupts continuously from the then-current level. Store start tick/start level/end tick/end level; do not reconstruct from a wall clock.

High Tide Start expands `initial_tide_segment_index:1` to `tide_start_index(index:1)` for the mission's bound tide controller. This sets its initial level to 600 mm at S=0, with a degenerate initial ramp whose start/end tick are both zero. It leaves every event and clock epoch unchanged. It is not a 600-tick phase offset. The controller then follows the ordinary event ramps from that actual level.

Dock/edge depth bounds are inclusive. A skiff edge's default admitted depth is 100..2000 mm; a causeway closes at depth>100 mm. Each layout may author narrower approved bounds. Power uses its own platform threshold. Current multiplier is 500..2000 permille, initially 1000; admitted travel duration is `ceil_div(base_operation_ticks*1000, current_multiplier)`. Existing travel duration never changes midway. Tide boundary exchange uses the water ledger in section 4; sea level does not directly overwrite inland cell volume.

## 10. Lava, cooling, coolant and shelters

Lava is an authored advancing channel network, not a fluid solver. Its directed acyclic segments have named sources and successor arrival delays, default 100 scenario ticks and minimum 50. `advance_lava(1)` activates the named source; each segment receives its first arrival once. The source and propagated front have at least 100 scenario ticks of warning; if a successor delay is shorter, its warning is announced upstream early enough. Unknown future paths cannot appear without the authored warning.

Segment states are Dormant → Hot → Cooling → Crusted → Reheating → Hot. Hot arrival sets heat 1000 and closes crossing admission. Activating a cooler requires an enabled matching socket, the segment Hot, and one coolant charge. Consume once, set `cooling_complete_tick=S+50`, and keep the crossing closed. At completion, Crusted heat is 100 and crossing admission opens. Set `crust_expiry_tick=cooling_complete_tick+200`; at that exact tick, Reheating sets heat 600 and closes admission before movement. Fifty ticks later it is Hot again. A cooler cannot restart the timer while Cooling/Crusted/Reheating. Existing travellers use transit recovery; this does not create new safe admissions.

The UI shows the actual relative expiry. `warn_reheat` is an advisory based on that timer; it neither extends the window nor fabricates an unrelated closure. `first_cooled_crossing_closed` is emitted once when the first cooled segment in its bound role actually enters Reheating.

Each cooler begins with one charge and holds at most one. Refill atomically transfers one charge from a bound named store while the cooler is idle. The cooling-works producer uses finite feedstock: default six feedstock units, two stored charges, store capacity eight, one unit→one charge in 100 powered scenario ticks, power demand one. Reserve/decrement feedstock when work starts, retain one work-in-progress unit, and add the resulting charge only on completion. Power/safety loss freezes progress and never deletes stored coolant. Full storage prevents a new job. No production without remaining feedstock. Mission contracts/layout may override these initial finite counts explicitly, creating a different content hash.

A shelter has six seats and 600 scenario ticks of protection. If occupied and its bound region is hazardous, consume one protection tick per hazard pulse. A positive pre-pulse balance protects during that interval; the pulse reducing it to zero emits one exhaustion, and the next hazardous pulse is unprotected. Empty or safe shelters consume nothing. Occupant transfer uses normal reservations. Neither successful end nor evacuation count may include temporary shelter residents. `enable_shelter_hazard` enables the shelter region's declared external heat front, not a direct resident penalty.

## 11. Objectives, temporal monitors and outcomes

Conditions are the closed typed union in `03_RUNTIME_CONTRACTS.md`; there is no string expression evaluator. An objective has a stable ID, one tier, one condition, and one temporal policy. `all`/`any` combine bounded condition trees; they cannot invoke arbitrary code. Multiple temporal obligations are separate objectives.

| Temporal kind | Exact semantics |
|---|---|
| `at_end` | Evaluate after `operation_ended` at the terminal boundary. |
| `ever_by` | Latch the first true end-of-step condition at S≤`deadline_tick`; the deadline scenario tick is inclusive. Later truth cannot repair a miss. |
| `always` | Start on the declared milestone's operation tick; test every operation boundary through the end milestone's operation tick, inclusive. Any false condition permanently fails. Both milestones must occur; otherwise unfulfilled at end. |
| `ever_before` | Latch truth only on an operation tick strictly earlier than the named closing milestone. If it is latched and that milestone has not occurred when the player ends, it passes without waiting. A latch on the closing tick does not pass. |
| `hold_for` | Between named start/end milestones, keep the continuous true suffix length in scenario pulses. Reset on a false condition at **any** operation boundary, even a Relaxed non-pulse. Pass at the end milestone only if suffix≥`scenario_ticks`. Both milestones must occur. |

`operation_started` means the scenario epoch enabled. `operation_ended` is emitted only in terminal evaluation. `group_boarded` latches when every member of the group has completed boarding at least once; evacuation without boarding does not manufacture it. `final_unload_started` is causal: all remaining non-evacuated residents are aboard vehicles assigned to final evacuation stops, then the first unload in that final phase begins. `all_evacuated` closes it only when every resident is actually evacuated. A partial success cannot earn that final-unload mastery.

Temporal conditions are evaluated on the state after movement. Tick-zero `operation_started` receives an initial sample before any step. `hold_for` starts at zero: a sample on its starting boundary does not retroactively count a pulse preceding that boundary. Metric counters and temporal state persist in snapshots; reopening a route cannot erase a past outage. The immutable event/milestone register supplies actual operation/scenario timestamps.

Terminal success requires the minimum rescues, all required objectives, empty vehicle occupant arrays and empty temporary shelters. At least one explicit critical objective is required per mission, including a transport/stabilization target where there is no building. A failure produces zero stars. Success produces one; two requires `10*rescued >= 9*population` and every critical objective; three requires all residents rescued, all critical and all mastery objectives. Redundant original mastery conditions remain safety invariants alongside the newly authored meaningful mastery goals.

Score is the exact rational sum of the original four terms, rounded once (ties upward):

```text
6000*rescued/population
+2000*completed_required/required_count
+1000*protection_fraction
+1000*remaining_supplies/budget
```

`protection_objective_ids` is an explicit separate list of typed infrastructure/route protection objectives. If empty, `protection_fraction=1`; otherwise it is completed listed objectives divided by list length. The star-critical set may also contain a rescue/transport objective and is not silently substituted into this fraction. Every other denominator is positive in compiled content; cap each fraction to [0,1]. Use a small checked rational helper with `BigInteger` intermediates for this one terminal computation, reduce fractions, then round once. Do not round each component independently. Failed runs may show this score as a failed result but never gain success/stars/unlocks. Reward IDs are campaign-wide stable IDs; repeating a mission/version migration cannot reissue a one-time reward. Story completion is allowed in Relaxed and assisted attempts, with distinct comparison partitions.

## 12. Forecast knowledge and simulation isolation

Forecast takes an immutable boundary snapshot, queued commands, one preview command and a requested scenario horizon. Default horizon is 300 scenario ticks; maximum is 600 (at most 1200 Relaxed operation steps). It runs the real simulator in an isolated clone, with a disposable event sink, no rewards, file writes, audio or renderer side effects. Revalidate the preview and queued commands using the same command path. Clone PRNG, reservations, partial work, objective counters and all clock remainders.

All current frozen mission events are known at start; their forecasts include those exact event operations. The contract also supports later disclosed events: include only events public at request time, freeze that knowledge set during the forecast, and omit future disclosure/effect operations for hidden events. Do not reveal their IDs, timing, count or a cutoff point. A mission containing hidden future events labels the preview `conditional_known_events`; the current all-public missions label it `exact_given_no_further_orders`. Either assumes no unspecified future player commands. Forecast uncertainty is not random jitter.

The forecast worker has one in-flight request, cancellation on a newer authoritative revision, 150 ms target debounce and a 1 ms/frame work slice. These are implementation budgets to profile, not a reason to change results. UI may retain a visibly stale preview until the matching revision arrives. A late result cannot commit a command or overwrite a newer forecast.

## 13. Assigned verification gates

The future command form is `python3 tools/resq.py game test --suite <suite> --report <path>`. These are **acceptance commands to implement**, not commands claimed to exist or have passed. Each report binds source/build, rules/content hashes, test IDs, real runner exit code and evidence paths. Python fixture validation is not C# simulation proof.

| Suite | Required cases |
|---|---|
| `runtime-units` | RU01 signed floor/ceil; RU02 overflow/bound rejection; RU03 canonical integer fixtures and score 6396 from fixture fractions. |
| `runtime-ordering` | RO01 Standard/Relaxed epochs; RO02 no pause catch-up; RO03 scheduled closure before command/boarding; RO04 last-tick arrival counted; RO05 strict-before same-tick failure; RO06 guided versus unguided clock/partition; RO07 effects at zero/deadline rejected and tick-zero warnings emitted once across initialization/resume/forecast. |
| `runtime-water` | RW01 source carry; RW02 exact mass per pulse including spray/boundaries; RW03 largest-remainder ties; RW04 pump/sink/passive competition; RW05 pump head derating; RW06 overtopping/break next-pulse effect; RW07 randomized bounded graphs conserve water and repeat bit-for-bit. |
| `runtime-equipment` | RE01 invalid deployment spends nothing; RE02 one deployment/stock decrement despite duplicate command; RE03 relocation preserves consumables/durability; RE04 no occupied-vehicle pickup; RE05 deterministic power priority/restart hysteresis; RE06 no refill/production duplication. |
| `runtime-transport` | RT01 seat/slot/node/berth separation; RT02 no population duplication; RT03 close-dock partial transfer cancellation; RT04 forward egress/reverse/backup cases; RT05 power-loss brake/return; RT06 fair bounded queues/reservation cleanup; RT07 step-free graph exclusions; RT08 final-leg mode and causal milestones. |
| `runtime-fire` | RF01 synchronous neighbour update; RF02 wetness actual-grant conservation; RF03 warning/ignition boundary; RF04 firebreak preserves heat; RF05 cooling delay and no perpetual implicit refuel. |
| `runtime-stability` | RS01 per-second loss carry and brace cap; RS02 warning/collapse exact boundary; RS03 cancelling pending collapse; RS04 deterministic runout and reopening cannot revive collapsed structure. |
| `runtime-tide` | RI01 signed interpolation; RI02 continuous interrupted ramps; RI03 equality depth thresholds; RI04 duration frozen in transit; RI05 tide exchange appears in mass ledger. |
| `runtime-lava` | RL01 source/arrival warning; RL02 50/200/50 cooling boundaries; RL03 no repeated cooldown reset; RL04 finite feedstock+WIP+store conservation; RL05 shelter exact final protected pulse; RL06 no refuge rescue credit. |
| `runtime-objectives` | RJ01 every temporal policy including absent milestones; RJ02 Relaxed non-pulse violation resets hold; RJ03 critical/mastery mapping; RJ04 exact star thresholds; RJ05 ever-before may finish before closing event; RJ06 causal counters never disappear on save/resume. |
| `runtime-forecast` | RV01 preview/live equality with same known commands; RV02 source snapshot unchanged; RV03 knowledge does not leak hidden events; RV04 stale/cancelled results ignored; RV05 Relaxed horizon uses scenario time. |

Reports belong under `evidence/runtime/<suite>.json` in the future harness. Numeric fixture tests must run against the actual independent C# simulation, including Android IL2CPP/device parity before a cross-device determinism claim. No suite substitutes for mission reference replays, rendered clarity, seated hand interaction, comfort, native performance or fun qualification.
