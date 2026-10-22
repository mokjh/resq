# 02 — Gameplay rules and simulation specification

All numerical values here are initial tuning targets. Treat them as versioned content, not facts about optimal game design.

## 1. Interaction-to-simulation contract

Every player action becomes a validated `GameCommand`. Input adapters never alter world state directly. Near grab, hand ray, controller selection, and head/dwell selection must produce equivalent commands.

Commands have a stable ID, run ID, sequence number, requested simulation tick, actor/input mode, action type, and typed payload. The simulation validates affordability, compatibility, occupancy, permitted phase, valid anchors, and objective restrictions. It then emits success or rejection events. Never apply a half-valid action.

Example: `PlaceEquipment(barrier, socket_12, orientation_90)` → verify stock and socket → reserve cost → instantiate authoritative equipment entity → change affected hydraulic edges → rebuild forecasts → emit EquipmentPlaced. Failure returns a reason and spends nothing.

### Player powers and hard limits

Can: deploy approved equipment, reposition idle portable equipment through a validated transfer, assign legal routes, toggle gates and networks, inspect forecasts, pause, and rotate/resize the board in placement/planning mode.

Cannot: grab inhabitants, drag a boat through blocked terrain, lift buildings, erase water/fire, move a running generator through a crowd, spawn unlimited equipment, or change room geometry mid-operation to escape a threat.

Board transformations change presentation, not distances, speeds, physics, or costs in the simulation.

## 2. Time and phase rules

Lifecycle: `Briefing → Loadout → Placement → Planning → Active → Stabilizing → Outcome → Debrief`. Pause, tracking loss, and platform suspension are interrupt states with explicit restoration targets.

Simulation advances at 10 Hz with integer tick IDs; rendering interpolates presentation. Hazard systems may update every second or fourth simulation tick according to deterministic schedules. Decorative animation uses rendering time and never changes outcomes.

Standard allows pause anytime. Planning freezes hazards and permits forecast inspection plus queued commands; on Resume, commands execute in sequence with a short confirmation countdown. No additional score is awarded for avoiding pause. This keeps strategy central and avoids rewarding uncomfortable input speed.

Command mode supports pause plus Advance 1 second / Advance 5 seconds / Run. Essential opportunities cannot require sub-second manual reaction. Relaxed mode runs hazards at a specified content multiplier without changing locomotion comfort.

A scenario deadline is simulation time, not wall time. Device sleep, menus, tracking interruptions, and application suspension never consume rescue time.

## 3. Supplies, stock, power, and equipment

Supplies are a single mission-level deployment budget. Equipment has a supply cost and a maximum stock count. Spending occurs only on successful deployment. Picking up a never-committed preview costs nothing.

Once deployed, equipment remains a mission asset. Repositioning an eligible idle tool transfers that same asset; it does not refund supplies. Broken/used-up equipment cannot be resold. This prevents infinite resource loops.

Power is separate: dry connected power sources supply integer capacity; pumps, sprinklers, and cable stations consume it. The network has a visible capacity readout and deterministic priority. Over-budget consumers do not flicker randomly: the player chooses priorities, and excess consumers display “Waiting for power.”

### Equipment catalog

| ID | Cost | First world | Exact role and limitation |
|---|---:|---|---|
| barrier | 2 | Alder | Closes or raises one approved hydraulic edge; pressure threshold and visible damage; cannot seal entire map boundary |
| bridge | 3 | Alder | Connects two marked ground nodes across a bounded gap; capacity 2 simultaneous agents; closes when flooded beyond rating |
| pump | 3 | Alder | Transfers bounded water volume between configured cells; needs 1 power; intake/outlet must be legal; cannot destroy water |
| skiff | 4 | Alder | Boat with capacity 4 residents; navigates water graph; must load/unload at valid docks; cannot operate on dry terrain |
| beacon | 1 | Alder | Marks an approved assembly node and displays evacuation route; does not teleport residents or generate shelter capacity |
| generator | 3 | Alder | Supplies 2 power while dry and connected; visibly shuts down before hazardous water exposure |
| firebreak | 2 | Emberwood | Removes fuel along a prevalidated strip; cannot delete buildings; bounded uses and placement sockets |
| sprinkler | 3 | Emberwood | Consumes 1 power and finite water supply to raise wetness on a small cell set |
| crawler | 4 | Emberwood | Ground transport with capacity 6; follows safe roads and cannot cross active fire or collapsed bridges |
| brace | 2 | Frostline | Raises stability of a tagged support; requires a safe placement socket; not an invincibility buff |
| winch | 3 | Frostline | Transfers up to 2 residents across a marked vertical link; limited duty cycle; requires 1 power |
| relay | 2 | Tidemark | Connects approved power nodes; must remain dry or elevated; cannot create power |
| cooler | 3 | Cinder | Consumes finite coolant to harden/slacken a tagged lava channel; not a universal extinguisher |
| shelter | 4 | Cinder | Temporary refuge with capacity 6 and a finite hazard protection interval; not equivalent to final evacuation |

Initial available equipment is restricted by mission. Do not expose all tools immediately. Artifacts under `content/` specify budgets and default loadouts. Content compilation rejects over-budget defaults.

## 4. Flood model

Use a shallow, bounded hydraulic network over the board. The first implementation can use a 32×24 grid with authored heights, channels, walls, and outlets. Authoring may merge decorative regions; collision/render meshes are independent.

Authoritative fields per water cell: terrain elevation in millimetres, area in square millimetres, volume in cubic millimetres, source/sink rates, and region ID. Store volume in signed 64-bit integers, validate nonnegative, and bound all authored values to prevent overflow. Head height is elevation plus volume divided by area, with an explicit rounding rule.

For each connected edge, compute a requested transfer from upstream/downstream head, conductance, barrier state, and fixed delta time. Accumulate requests from the old state. When a cell has insufficient volume, allocate available water proportionally using integer arithmetic and a stable edge-ID tie break. Apply all transfers simultaneously. Sources and sinks have separately recorded totals.

Invariant: `new total volume = old total + sources − sinks`, exactly in the chosen integer representation. Transfers never produce negative water. Never independently clamp each edge against the same full cell volume, which would duplicate water.

A pump creates a bounded directed transfer, not a sink. Weather modifies source rates at authored ticks. Surge events alter inflow, not directly damage residents. Visual foam, ripples, splashes, and reflections are non-authoritative.

### Forecast

When a placement or route is previewed, clone the small simulation state and advance a bounded forecast horizon. Show affected cells and a confidence label. The near-term deterministic preview is exact under current known events; hidden future story events must not be presented as already known. Update previews when queued equipment changes. Limit preview work and debounce target movement.

A red edge means illegal deployment, amber means valid but risky, and a shape/icon explains why. Never rely only on color.

## 5. Fire and moisture

Emberwood uses a cell graph with fuel, heat, wetness, and burning state. Wind biases transfer across neighbors. Updates are synchronous and use seeded deterministic variation if any. Visual flame particles do not determine spread.

An ignition warning precedes gameplay damage. Firebreaks remove fuel on approved cells; sprinklers increase wetness while consuming water and power; burned areas can become traversable only after a cooling interval. State transitions are `Unburned → Heating → Burning → Cooling → SafeAsh`.

Do not require real smoke rendering. Use a small number of readable billboards and ground heat indicators. Smoke cannot obscure the only path or essential UI. Provide reduced-effects presentation without reducing game information.

## 6. Snow, stability, tides, and lava

**Snow/stability:** tagged slopes have a stability meter and connected runout regions. Load, warming, and tremors reduce stability. Braces support eligible structures; they do not stop an entire mountain. A warning identifies the runout corridor before release. Avalanche debris moves along authored channels and changes graph edges, avoiding general rigidbody collapse.

**Tides:** a deterministic water-level schedule changes dock eligibility and power exposure. A tide ruler and forecast timeline make windows readable. Players may stage vehicles to exploit a future connection. Clock phase is stored in saves.

**Lava:** a slower channel network with heat and cooldown states. Coolant transforms only authorized segments into temporary safe crossings after a visible cooling delay. Lava forecasts are slower and more legible than water but constrain space for longer.

These systems share hazard interfaces, not an unrealistic universal “disaster meter.” Each has different state, counterplay, and visual communication.

## 7. Inhabitants, groups, and agents

Each resident has a stable entity ID, starting group, current node, mobility class, transport reservation, and state. States: `Sheltered`, `Waiting`, `Walking`, `Boarding`, `InTransit`, `Disembarking`, `Evacuated`, `AwaitingBackup`. Hazard failure does not show injury: an emergency fallback service takes over, the resident counts as not rescued by the player, and the debrief explains why.

Residents leave an assembly point only when a complete route to the next safe checkpoint is available or a vehicle reservation is confirmed. Do not make them run knowingly into new danger. If a path becomes unsafe, stop at the previous safe node, replan, and signal the blocked route.

The navigation model is an authored graph. Nodes represent doors, walkways, rooftops, docks, and shelters; edges declare permitted traversal, capacity, hazard tolerance, and travel time. Avoid baking NavMesh repeatedly as floods move.

For larger groups, simulate group-level travel and render a few representative individuals. Keep individual rescue counts and IDs authoritative so repeated boarding or reload cannot double-count them.

## 8. Vehicles and route planning

Vehicles require eligible pickup and destination nodes. A valid route is displayed before assignment. Default routes minimize travel time subject to hazard constraints, not shortest Euclidean distance.

Skiff cycle: idle at dock → reserved pickup → travel → board capacity → travel → unload into safe receiving capacity → update resident state → next order. Boarding takes a short content-defined duration and requires the dock to stay valid. A destroyed/blocked destination triggers a safe replan, not a teleport.

Reassignment may change future stops but cannot discard carried residents. Occupied vehicles cannot be grabbed into inventory. During preview, show capacity, estimated arrival, and any expected tide/current restriction.

## 9. Objectives, success, and scoring

Each mission declares total residents, minimum required rescues, required objectives, optional objectives, critical infrastructure, and time limit. Success occurs when the minimum rescue target and required objectives are met before the scenario's failure condition. A mission can continue briefly for optional rescues when safe, or the player may end it.

Objective conditions are event/state predicates, never natural-language strings evaluated by an LLM. Examples: `EvacuatedCount >= 12`, `PowerAvailable(hospital)`, `NoHazardExposure(assembly_node)`, `AllGroupsTransferred`, `ShelterOccupancy == 0 at end`.

### Star rules

- One star: mission success.
- Two stars: success plus at least 90% of residents rescued and critical infrastructure objective met.
- Three stars: all residents rescued, critical infrastructure met, and the authored mastery objective completed.

Three stars never gate story access. Some missions have no separate infrastructure; their critical objective is explicitly defined as a transport or stabilization target.

### Score formula

Proposed maximum 10,000:

`round(6000 × rescued/total + 2000 × required_objective_fraction + 1000 × protected_infrastructure_fraction + 1000 × unspent_supplies/budget)`.

All terms are clamped to [0,1]. Required objective fraction is for scoring only; unmet mandatory conditions still prevent success. No time-speed bonus by default. A zero budget is invalid content. A mission with no infrastructure entries defines its protection fraction as 1. If extra equipment is supplied for an accessibility preset, scores live in a separate preset partition.

## 10. Failure, recovery, and anti-frustration

Standard: restart the mission or restore the last explicit phase checkpoint. Restoring a checkpoint marks the attempt as assisted for local score grouping but preserves story completion. The game never humiliates the player with accusatory dialogue.

Before a critical irreversible outcome, show an actionable warning whenever the design permits. Suggested copy: “The east dock will close soon. Send the skiff to the high landing.”

Debrief includes the first important causal mistake, a visual timeline, and one specific suggestion. Example: “The pump stopped when the generator's platform flooded.” Do not output “Improve your strategy.”

## 11. Replay and variation

Replayable missions combine fixed structure with validated modifiers. A variation changes one or two meaningful parameters: inflow timing, blocked route, supply budget, initial vehicle location, wind direction, or tide phase. Every shipped configuration needs a reference solution and solvability tests.

Seeds identify cosmetic variation and permitted deterministic scenario choices. Do not use a random seed to excuse unfairly impossible maps. Standard runs compare only with the same mission version, rules version, layout class, modifiers, and input preset.

## 12. Sample first 90 seconds

0–15 seconds after placement: the miniature village opens, a bus stops at a broken crossing, and the first objective appears: “Get the bus to high ground.”

15–35: a single bridge tool is highlighted. The player brings it to two large sockets; a green route appears. Release commits the bridge. No hazard can fail during this teaching step.

35–55: the bus crosses, passengers wave, the rescue count increases, and a river warning activates.

55–75: the player places a barrier. Forecast arrows show diversion; a dock remains safe.

75–90: the skiff becomes available. A guided route selection begins the next real objective. Guidance recedes as the player demonstrates competence.

The tutorial is state-driven, not a video timed to assume actions happened.
