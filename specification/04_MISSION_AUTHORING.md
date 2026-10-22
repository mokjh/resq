# Mission authoring and configuration contract

Freeze `resq-spec-freeze-1.1.0`; rules `resq-rules-1`. The 25 records in `content/mission_contracts.json` are the complete design catalogue. They are typed production instructions, not compiled levels or proven solutions. Runtime geometry and actual command recordings are implementation outputs; their precise artifacts/checks are assigned for every mission in the root task graph.

## 1. Fixed authoring inputs

The source briefs preserve each mission's title, duration, population, rescue minimum, supply budget, default equipment and two distinct approach briefs. The freeze adds exact group allocations, step-free residents, typed objectives/events/milestones, stock limits, protection-score targets, rule profiles and layout recipes. Population and budget cannot be changed without a versioned mission correction. Every group has one step-free resident; all members retain unique IDs through transport and backup. IDs are `{mission_id}:resident:{group_id}:{index:02}` with indices starting at one.

Each mission has **AUTHOR → REPLAY → INTEGRATE → POLISH → QUALIFY** tasks. AUTHOR creates `content/runtime/Wn/Mn/mission.json`, `compact.layout.json` and `wide.layout.json`. REPLAY creates a command manifest and recorded matrices from the actual simulation. INTEGRATE creates a real playable blockout. POLISH integrates audited final sources. QUALIFY reruns the full automated matrix against exact integrated hashes. Device, performance and human checks then accept the complete world. A planning row never marks one of these tasks passed.

## 2. Layout definitions and construction

`content/layout_recipes.json` freezes the logical coordinate grid, named world anchors, each group's start/assembly/pickup roles, required event/objective roles, socket counts, capacities, travel-time defaults, terrain heights/rates, resource stores and two dock configurations. The geometry author implements these recipes with the following fixed rules; decoration follows the graph rather than determining it.

1. The logical grid is 32×24, southwest origin, 1000 mm pitch. Preserve the exact named anchor coordinates. Rasterize channels between anchors by x-first Manhattan segments; forks are explicit junctions. Author channel cells at 0 mm, low assembly terraces at100 mm, ordinary roads at300 mm and high ground at1200 mm. Unused cells begin at1200 mm. A channel/road crossing gets a declared bridge or raised crossing; no hidden walkable intersection.
2. Group starts sit on their assigned anchor. Its assembly is one cell south and pickup two cells south; at a grid boundary use one/two cells north. Start→assembly and assembly→pickup have20 operation-tick travel edges. Different nodes may share a coordinate but never share capacity implicitly. Separate logical heights or offset their presentation proxies to avoid visual overlap.
3. Each group has a short and a long evacuation corridor. The short default ground leg is300 operation ticks; long600. Water legs are200; cable50. The path comprises authored segments whose total equals the declared leg duration; never count each geometric segment as the whole leg. State-dependent current factors use the runtime duration rule. Target arrival times are shown before assignment.
4. A corridor connects through a graph node at each junction, dock or safe checkpoint. Edges declare direction, traversal class, movement slots, depth/heat/runout tolerance, step-free support, reverse eligibility and safe endpoints. Ground and water graphs never connect merely because render meshes touch. No-input auto-dispatch is disabled except the tutorial bus's declared waiting route; a route/equipment/transport command initiates rescue.
5. Two reference corridors must differ by actual route, equipment or evacuation priority. Both connect every group to enough permanent refuge when the permitted equipment is considered. A structural compiler reachability pass may temporarily consider legal deployable links; it is only a candidate-solvability check. Actual hazard-time solvability requires the two command replays.
6. Each permitted tool gets `max(2, maximum_stock+1)` legal alternative sockets. Name them `{tool}:01…N`, bind the socket kind from equipment_rules, preserve integer footprint/quarter-turn eligibility, and provide enough non-overlapping dry/elevated alternatives. No socket creates unbounded barrier sealing, water deletion, power or receiving capacity. Candidate sockets have broad presentation targets.
7. Permanent receiving capacity totals exactly the mission population; one resident uses one receiving place. A transfer/staging node is not permanent refuge. Each dock has one berth and explicit pedestrian/vehicle capacity. Wide uses depot as primary receiving point and high landing as secondary; compact reverses them and uses the longer outer approach for the primary dock. Both choices bind actual node/edge IDs in the graph and change staging/travel, not merely UI labels. A room scan selects only these validated choices.
8. `alternate_pickup` is a third legal landing one cell east of the high-landing anchor, with the same group capacity and a water approach100 operation ticks longer than that group's normal pickup. An Alternate Dock challenge changes that group's pickup binding, not its starting population or guaranteed safety. Both layouts require separate replay evidence.
9. Snapshot, forecast and actual gameplay use these same frozen layout definitions. Repositioning preserves the current logical layout/dock configuration. If a different class is selected, require a restart from the current mission definition; never silently substitute another graph inside a saved attempt. A visual compact-scale fallback can preserve the original class and graph.

Default flood nodes use area1,000,000mm², conductance100,000mm²/scenario-second, initial volume0, maximum transfer100,000,000mm³/pulse and outlet80,000,000mm³/scenario-second. Channel links have sill0; links onto low terraces use sill100; high-ground links1200. Source and boundary requests use the runtime conservation algorithm. A basin named in an event resolves to a separately bounded tagged cell set. Spread an event's aggregate source rate across its cells using quotient/remainder in cell-ID order; do not multiply the specified rate by cell count.

Named orchard/medical/clinic/power/platform regions are graph tags with explicit membership. An exposure objective takes the maximum member depth/heat on each sample. A dry-power socket's platform depth is the maximum of its tagged cells. An event target has one declared role type; ambiguous same-name matches are rejected.

The recipe is the first reproducible candidate. If it fails the real replay or visual readability gate, the author corrects the smallest relevant geometry/tuning value, records the failing evidence and creates a new layout/mission revision. This is normal implementation verification; accepting an unsolvable map, erasing its tests or inventing a successful replay is prohibited.

## 3. Distinct world topology and mechanics

| World | Required authored structure and route alternatives |
|---|---|
| W1 Alder | Reservoir→fork→west/east/downstream outlet channels; separate empty diversion basin and occupied orchard basin; high-road loop; marked viaduct gap; low/high docks. Bus needs the bridge; isolated groups need water transfer. Generator/pump choices must affect safe routes. |
| W2 Emberwood | Three fuel sectors with a road loop, short threatened leg and longer refill leg; finite tank/pond nodes. Firebreak sockets span at most8 contiguous eligible cells; sprinklers cover at most4. Fire/wind exposure closes roads through the real fire system. Water supply and transport staging interact. |
| W3 Frostline | Three terraces and two distinct cable links, one intermediate capacity-limited station, tagged supports and warned runout corridor; a longer rock-shelf bypass stays outside runout. Lower meltwater regions couple to power in M4. Braces support structures rather than stop slopes globally. |
| W4 Tidemark | Three islands, low/high causeways, two receiving docks, outer/inner water loops and elevated relay alternatives. Tide ramps change docks/causeways and pump lift; current changes travel duration. The lighthouse is a real power consumer, not a cosmetic reward. |
| W5 Cinder | Three heat-separated sectors, sea/ground transfers, two eligible cooling crossings, final ferry connection and capacity-limited temporary shelters. Coolant and protection are finite; every temporary refuge must empty for success. M5 combines previously qualified mechanics and introduces none. |

The mission-specific group/location, event and reference records refine this topology. A corridor irrelevant to that brief is closed with visible terrain, not an invisible wall or an unmarked interaction. Do not expose future-world tools solely because their schema exists. Layout authors may reuse the kit and graph fragments; all missions still need the brief's different central decision and event sequence.

## 4. Events, warnings and infrastructure

Mission events are ordered typed operations with integer values and stable target roles. `at_scenario_tick` is the effect boundary; `warning_at_scenario_tick` is separate. All current events are known from Planning. W2-M4's40-second wind warning at200s has its effect at240s. Warning operations never secretly apply an effect. Weather changes source/heat/stability parameters; it never awards a rescue or directly writes an objective outcome.

Role binding resolves typed operations to runtime node, edge, region, support, group or store IDs. Effects require1≤tick<deadline; warnings may be at0 and are emitted once at initialized scenario0 before the first active step. Initial parameter values belong in initial state, never effect0. The compiler rejects a role with the wrong type, an unknown ID, contradictory same-tick writes, invalid effect bounds or an unannounced irreversible closure. Disruptive effects require at least100scenario ticks warning. Parametric events are not guaranteed state transitions: e.g. adding inflow does not force a dock to flood; `set_region_cooling` sets a cooling-rate parameter and does not declare a fire safe.

Medical station, clinic power, lighthouse and cooling works demand1 power unit when their declared functionality is required. Refill station, orchard pump house, structural support and harbor demand0 unless their explicit attached service has demand. W2-M4 has a preplaced dry2-capacity source feeding its tank; W4-M1 has a preplaced dry2-capacity source feeding its receiving dock. They are fixed scenario infrastructure, not free deployable generators.

Finite W2 stores start with tank600,000,000mm³ and pond1,200,000,000mm³, used only where present in the brief. W5 named coolant store begins with `max(2, default cooler count)` charges; coolers also retain their one initial charge. Only W5-M3 has a powered production plant/feedstock, with the exact finite-production rule in runtime rules. No other scenario refills coolant invisibly.

The AUTHOR task lists every resolved infrastructure demand, initial power connection, resource-store membership, dock threshold, support/load and hazard coefficient in the runtime layout. It uses the frozen defaults and runtime equations; any override is an explicit versioned value and validated reference dependency.

## 5. Objectives and meaningful mastery

Every mission has rescue_minimum plus its required predicates, at least one critical objective for stars, and one or more mastery predicates. Three stars also require every resident rescued. `protection_objective_ids` references its infrastructure/route targets for the1000-point score term; an empty list yields protection fraction1. Critical transport targets still control stars.

The freeze retains the original end-state/capacity prohibitions as global safety invariants. It adds measurable strategic obligations where the original mastery was implied by rescuing everyone or obeying an invariant:

| Mission | Frozen additional mastery |
|---|---|
| W1-M3 | At least4 west-village residents complete their final evacuation leg by ground. |
| W1-M5 | Depot→upper-village route remains continuously open for the final100 scenario ticks before outcome. |
| W2-M4 | At most2 firebreak deployments from the available3; rebuilding a committed strip remains illegal. |
| W3-M2 | Maximum resident dwell at the intermediate node≤100 scenario ticks. |
| W3-M3 | At least one resident traverses the tagged rock-shelf bypass; runout admission safety remains mandatory. |
| W3-M5 | Receiving-station peak occupancy≤6; no occupied vehicle/refuge can remain at success. |
| W4-M3 | Maximum receiving-dock queue≤50 operation ticks; node-capacity limits remain mandatory. |
| W5-M4 | At least one resident's final evacuation leg is ground and at least one's is water. |

All other mastery conditions are typed exactly in the catalogue: finite water/coolant, preserved power/dry platforms, no outage during final unloading, no equipment loss, no loaded closed-dock wait, and a strictly earlier rescue than reheating where specified. The original descriptive requirement is retained in the unchanged source brief for traceability. Every layout/profile needs at least one recorded three-star solution; A/B need both succeed but need not both earn three stars.

## 6. Loadouts, modes and challenges

Default loadouts/costs are unchanged. The maximum stock equals the frozen mission kit; custom loadout selection may deselect permitted stock but cannot add an unlisted tool. The budget spends on actual deployment. UI warns when a kit is reduced and always offers Reset Default. Guaranteed reference solvability applies to the default and named challenge kits; choosing to omit essential equipment is recoverable through restart/reset, never a hidden inventory trap. Test every permitted tool's placement/rejection path and bounds, not an impossible claim that every deliberately incomplete kit wins.

Three rule profiles and seven input combinations are frozen in modes_and_challenges. Head dwell selects Command; hands/controllers also support Command. Comparisons include mission/rules/layout/modifier/profile/adapter/assistance/guidance identity. The three legacy preset names map explicitly. Relaxed halves scenario progression and enables checkpoint retry; its unchanged scenario-tick warning lead therefore lasts twice as many operation seconds. The warning-operation-time multiplier is derived metadata and must not multiply scenario lead again. Operation movement speed and supply counts stay unchanged.

There are **30 named challenge definitions**: two per W1 mission (Early Surge, Alternate Dock), one per later mission (W2 Crosswind, W3 Careful Operation, W4 High Tide Start, W5 Careful Operation). W5-M5 adds a careful light kit to make its challenge consequential: budget29, remove one barrier from default stock, no equipment losses. Each is Standard rules with hands/controller; no stacking, random selection or extra variants at launch. Challenges unlock after base mission success and never gate story access.

Early Surge shifts the first authored inflow effect and warning300 scenario ticks earlier. Crosswind reverses wind vectors (west↔east, north↔south, NE↔SW, SE↔NW). High Tide Start initializes the tide model at segment index1 (600mm) at scenario tick0, with no active ramp; subsequent authored ramps and event times remain unchanged. It does not shift deadlines or double-fire events. Careful Operation adds a required maximum-losses0 condition, while base mastery still applies. Challenge-specific required predicates and budget changes are in its own immutable compiled definition.

## 7. Exact acceptance matrix

- Base:25missions×2layouts×3rule profiles×2strategies =**300 successful reference configurations**.
- Challenges:30definitions×2layouts×2strategies =**120 successful reference configurations**.
- Each base mission/layout/profile additionally has no-input, major-mistake, save/resume, tracking and anchor recovery cases. Save at partial boarding, travel, a pending event and a fractional Relaxed scenario accumulator across the corpus.
- Initial REPLAY tasks execute twice on host. QUALIFY, world REGRESSION and FULL01 execute on host and both Quest models using Android IL2CPP, twice per target, and compare decisive-tick hashes. A different input adapter must produce equivalent command semantics, with its own input/UX qualification and distinct comparison partition.
- A/B differ in equipment/route/order priorities, not only delays. Their manifests record named strategic decisions and relevant command IDs; an independent reviewer validates the distinction against the original briefs.
- Validate at least one three-star sequence per mission/layout/profile and challenge default if mastery differs. Fuzz1000fixed seeds×1000attempted commands per world, preserving failing seeds.
- Compile validation rejects unresolved roles, incorrect totals/budgets, missing dependencies, overlapping sockets, graph/step-free impossibility, impossible initial capacities, conflicting events and incompatible versions. Runtime conservation, timing, fun and hardware are separately tested.

Actual replay command files are deliberately absent until the real C# simulator executes. Do not fabricate an expected outcome/hash to fill this specification. Producing those files is an assigned implementation task with an executable acceptance command and a failure path.
