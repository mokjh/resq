# 06 — ERD, content definitions, runtime state, and save contracts

## 1. Read this before implementing a database

The ERD describes **logical entities and ownership**, not a requirement to run PostgreSQL, Supabase, an ORM, or a network server. Resq's authored definitions compile into read-only local content. Runtime state lives in memory. Player progress and snapshots persist as versioned JSON aggregates.

Source diagram: `diagrams/game_data_erd.mmd`. Render with a current Mermaid-capable viewer. The runtime flow is in `diagrams/runtime_flow.mmd`.

Do not persist every simulation tick as normalized database rows. Persist periodic complete snapshots plus bounded command logs. The website and optional newsletter must not share the game's local profile or expose gameplay save data.

## 2. Content domain

| Entity | Required content and invariants |
|---|---|
| World | Stable world ID, display/localization keys, order, release scope, art/audio bundle IDs. No filename-as-ID dependency. |
| MissionVersion | Stable mission ID plus immutable revision/version ID, world FK, rules version, content hash, duration, rescue minimum, total residents, budget. Changed mechanics create a new version. |
| LayoutVersion | Mission FK, layout class, navigation/hazard graph, docking configuration, graph hash. Only compiled approved layouts may be selected. |
| ObjectiveDef | Typed condition and validated parameters, required flag, tier. Runtime never evaluates prose. |
| EventDef | Tick, event type, typed payload, affected IDs, warning lead time, once-only flag. |
| EquipmentDef | Cost, power, capacity, allowed socket kinds, operation rules, art reference. Tuning versioned with rules. |
| MissionEquipment | Mission/equipment composite key; default stock ≤ maximum; default total cost ≤ budget. |
| NavNode | Logical coordinates, capacity, kind, permitted traversal, associated hazard region and shelter properties. |
| NavEdge | From/to node, direction, travel ticks, capacity, conditional access, vehicle classes. Both node IDs must exist. |
| HazardCell/Link | Initial typed state, integer geometry/conductance, connections and associated equipment sockets. |
| ResidentSpawn | Unique resident ID, group ID, start node, mobility class. Count must equal declared mission population. |

Strings in `content/missions.json` are **authoring briefs**. They are deliberately labeled `authored_brief_unimplemented`. The content compiler must produce typed runtime records with actual node/cell/socket IDs and reference replays. Never parse English descriptions to decide mission outcomes during play.

## 3. Runtime and persistence domain

| Entity | Ownership / lifecycle |
|---|---|
| PlayerSave | One local installation/profile aggregate; no email/name required. Carries schema/generation/integrity metadata. |
| Settings | Input preset, handedness, dwell duration, text scale, audio, effects, placement preferences. |
| MissionProgress | Best outcome per mission version and comparison partition; supports content migration rules. |
| RunRecord | One attempt; references frozen content/layout/rules versions, seed, modifiers, status, and assisted flag. |
| CommandRecord | Ordered run command with unique ID; accepted/rejected result recorded; duplicate processing forbidden. |
| ObjectiveResult | Per-run metrics and completion tick for each definition. |
| Snapshot | Complete authoritative state at a tick, PRNG state, command cursor, pending events, supplies, hazards, power, agents, equipment, routes, and reservations. |
| ResidentState | Exactly one valid location/movement state per resident; evacuated is terminal. |
| VehicleState | Route/progress, occupants, reservation state, fuel/duty state where relevant. No duplicated occupants. |
| EquipmentState | Definition, placement socket, orientation, durability, active flags and consumables. |
| Unlock | Unique awarded content/cosmetic access; cannot reference unreleased content as playable. |
| RewardReceipt | Idempotency ledger preventing repeated completion/reload from duplicating rewards. |

Comparison partition = mission revision + rules version + layout signature + modifiers hash + input preset + assisted status. Do not imply equivalence between hands and command modes when gameplay pacing differs.

## 4. Foreign-key and conservation rules

Every referenced world, mission, tool, node, socket, cell, and objective exists in the content version. Composite runtime IDs include the run/snapshot scope when necessary.

A resident cannot simultaneously occupy a ground node and a vehicle. Vehicle occupant IDs must reference residents whose state matches that vehicle. An evacuated resident cannot board again. Total people across all states equals declared population at every tick.

Supplies never go below zero. Deployment reservations cannot outlive their action. Power consumers cannot exceed granted capacity. Water and transport resources follow the explicit conservation rules in the gameplay specification.

Derived presentation fields are not persisted as authority. Animation position, particle seed, water mesh interpolation, camera position, and temporary hover state can be recreated.

## 5. Command contract

Simulation commands: `place_equipment`, `move_idle_equipment`, `assign_route`, `set_power_priority`, `set_gate`, `activate_tool`, `end_operation`.

Clock/application commands: `pause`, `resume`, `advance_ticks`, `enter_placement`, `confirm_placement`, `exit_to_hub`. These are validated and recorded but must execute through the application/clock controller immediately; do not queue Resume behind a paused simulation tick.

Each command includes `command_id`, `run_id`, monotonically increasing `sequence`, `requested_tick`, `kind`, and a typed `payload`. Server timestamps are unnecessary. A command with an old/repeated sequence is rejected or deduplicated. The command processor returns `accepted`, a stable `reason_code`, and the actual executed tick.

Rejection codes include `INVALID_PHASE`, `UNKNOWN_TARGET`, `INSUFFICIENT_SUPPLIES`, `STOCK_EXHAUSTED`, `SOCKET_INCOMPATIBLE`, `ROUTE_BLOCKED`, `CAPACITY_FULL`, `POWER_REQUIRED`, `TRACKING_UNAVAILABLE`, and `CONTENT_VERSION_MISMATCH`.

A cancel intent is not a deployment. Simultaneous hand selections are resolved consistently; one equipment instance cannot be controlled by two adapters at once.

## 6. Objective contract

Supported first-chapter predicates should be deliberately small:

- `rescue_count`: minimum integer.
- `group_evacuated`: group ID and required count.
- `infrastructure_operational`: infrastructure ID, required end-state.
- `route_available`: source/destination and traversal class.
- `exposure_below`: region ID, hazard kind, threshold.
- `equipment_losses_at_most`: integer.
- `supply_remaining_at_least`: integer.
- `all_vehicles_unloaded`: boolean condition.
- `temporary_shelters_empty`: full-release condition.

Combine predicates through explicit all/any nodes with bounded depth. Do not build a scripting language or arbitrary expression evaluator unless a real shipped mission requires it. Compiler validates parameter types and references.

## 7. Save envelope

Envelope fields: `schema_version`, `generation`, `payload`, `payload_sha256`. Payload includes `local_profile_id`, `settings`, `mission_progress`, `unlocks`, `reward_receipts`, and optional `current_run`.

Hash an explicitly defined canonical encoding of the payload. Use UTF-8, stable key ordering, invariant number formatting, integer authoritative values, and reject duplicate JSON keys. Implement shared test fixtures across the validation tool and Unity. The hash is an integrity check only; users can modify local files, and this is not an anti-cheat system.

A current-run snapshot must include outstanding route reservations, partial boarding progress, pending event cursor, queued validated actions, generator/consumer state, and RNG state. Saving only positions or the initial seed will not reproduce a run correctly.

## 8. Migration and failure behavior

Schema migrations are explicit `vN → vN+1` transforms with fixtures and backup preservation. Do not overwrite a future-version save. Show an understandable incompatibility message and preserve the file.

Content updates may retain campaign completion while invalidating a competitive best score for changed rules. A resumed run referencing removed content offers restart from the current mission definition while retaining prior completed progress. Do not silently substitute a new level under an old snapshot.

If both current and backup generations are invalid, show a recovery screen; creating a new profile requires explicit confirmation. Automated tests must simulate truncation, interrupted promotion, storage failure, duplicate rewards, and future schema data.

## 9. Asset/build domain

Asset provenance is separate from gameplay saves. Each asset manifest includes asset ID, author/tool, source project/revision, prompt/reference record, rights evidence, file hash, mesh statistics, textures, rig/animation list, export settings, Unity import results, and approval state.

Build manifests link commit, toolchain, content, assets, APK, evidence, video, and site release state. A published feature claim must be traceable to a tested build, not merely a row in a planning document.

## 10. Pack schemas

`schemas/mission_briefs.schema.json` validates this pack's authored mission catalogue. `schemas/task_result.schema.json` defines automation evidence reports. `schemas/asset_manifest.schema.json` defines art handoff metadata. These are production-data contracts, not a claimed finished runtime implementation.

`tools/validate_pack.py` checks JSON parsing, basic schema contracts without third-party dependencies, referential IDs, equipment cost, scope counts, event order, and task DAG consistency. Passing it establishes internal pack consistency only; it does not certify playability or Unity correctness.
