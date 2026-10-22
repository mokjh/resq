# RESQ runtime contracts — frozen design 1

Freeze `resq-spec-freeze-1.1.0`; rules `resq-rules-1`; compiled-content schema 1; command schema 1; save schema 1; replay schema 1. **Design contracts only: no serializer, simulator, compiled level, migration or runtime test is claimed implemented.** The original pack remains unchanged. `02_RUNTIME_RULES.md` is normative for algorithms and tick order; this document is normative for data shapes and compatibility.

## 1. Contract conventions

All objects below are closed: every listed field is required, unknown fields are rejected, and null is legal only where explicitly stated. Tagged unions admit exactly one listed shape. A property has its stated type, not a coercible string/number substitute. Collections are bounded by `02_RUNTIME_RULES.md`; all identifiers resolve within the exact content/run version. Missing fields are compile/load errors, not inferred defaults. A compiler expands documented authoring defaults into explicit values before hashing.

`id` is ASCII `[A-Za-z0-9_.:@-]{1,128}`; no whitespace/path syntax. `hash` is 64 lowercase hexadecimal characters. `tick` is an integer 0..1,000,000. `count` is nonnegative signed 32-bit unless a lower ceiling is given. `quantity` is a signed 64-bit JSON integer within its domain bound. `int` never means floating point. IDs are case-sensitive. Display/localization keys are not IDs for simulation targets.

C# contracts use plain DTO classes with explicit strict readers and immutable compiled equivalents. No UnityEngine types, reflection-dependent dynamic type loading, or arbitrary serialized class names enter Core/Simulation. Public fields below specify JSON names exactly; a strict converter selects the payload type from the outer `kind`. After deserialization, validation produces a new immutable value graph; live simulation never shares a mutable authoring DTO.

```csharp
public sealed class CommandDto {
    public int schema_version;
    public string command_id;
    public string run_id;
    public long sequence;
    public long requested_tick;
    public string actor_input;
    public string kind;
    public CommandPayloadDto payload;
}
public abstract class CommandPayloadDto { }
public sealed class PlaceEquipmentDto : CommandPayloadDto {
    public string equipment_id;
    public string socket_id;
    public int orientation_step;
}
public sealed class MoveIdleEquipmentDto : CommandPayloadDto {
    public string equipment_instance_id;
    public string destination_socket_id;
    public int orientation_step;
}
public sealed class AssignRouteDto : CommandPayloadDto {
    public string subject_kind; // exactly "group" or "vehicle"
    public string subject_id;
    public RouteStopDto[] stops;
}
public sealed class RouteStopDto {
    public string node_id;
    public string action; // exactly "assemble", "pickup", "dropoff", "evacuate"
    public string group_id; // nullable only as specified in section 3
}
public sealed class CommandResultDto {
    public string command_id;
    public long sequence;
    public string status; // queued, accepted, rejected
    public bool accepted;
    public string reason_code;
    public long executed_tick; // -1 only while queued
    public string[] affected_ids;
    public long state_revision;
}
public sealed class RunIdentityDto {
    public string run_id;
    public string source_build_id;
    public string mission_version_id;
    public string rules_version;
    public string content_hash;
    public string layout_version_id;
    public string layout_signature;
    public string rule_profile_id;
    public string input_adapter_id;
    public string guidance_policy;
    public string challenge_id;
    public string modifiers_hash;
    public long seed;
    public bool assisted;
}
public sealed class ObjectiveDefDto {
    public string id;
    public string tier;
    public string kind;
    public ConditionParamsDto @params;
    public TemporalDto temporal;
}
public abstract class ConditionParamsDto { }
public abstract class TemporalDto { }
```

`@params` serializes as `params`. `bool`, `int`, `long`, `uint`, `string`, arrays and declared DTOs are the only DTO primitives. A nullable ID maps to `string` with an explicit null check; there is no empty-string sentinel. Immutable runtime structures may use integer-indexed arrays after validated ID resolution. The binary/index order is derived from ordinal ID sorting, never file directory order.

## 2. Versioned content and compilation

The preserved pack and `specification/content/` contain production inputs, not runtime bundles. The future compiler consumes those typed inputs plus authored layout geometry, expands defaults, validates references and invariants, and emits a **new** immutable `ContentBundleEnvelope`:

```text
ContentBundleEnvelope {
  schema_version: 1,
  payload_sha256: hash,
  payload: ContentBundle
}
ContentBundle {
  freeze_id: id,
  rules_version: "resq-rules-1",
  worlds: WorldDef[], equipment: EquipmentDef[], rule_profiles: RuleProfileDef[],
  challenges: ChallengeDef[], missions: CompiledMission[], layouts: LayoutDef[],
  asset_catalog_hash: hash
}
```

Definition arrays are sorted by ID before canonical hashing. The bundle payload hash is the run `content_hash`; it includes numeric tuning, layouts, all objective/event data, mode/challenge values and asset identity. Preview art changes still produce a new content hash, even if the independent rules version is unchanged. An evidence source manifest `{payload_sha256,payload_path,files}` binds that canonical compiled payload; hashing the manifest file itself is never a substitute for `content_hash`.

| Type | Exact required fields |
|---|---|
| `WorldDef` | `id:id, order:int(1..5), release_scope:competition|full_release, title_key:id, art_bundle_id:id, audio_bundle_id:id` |
| `EquipmentDef` | `id:id, revision:positive int, supply_cost:positive int, first_world_id:id, socket_kinds:id[], power_supply:count, power_demand:count, resident_seats:count, movement_slots:count, relocation_policy:none|idle_same_zone, durability:count, parameters:EquipmentParameters, prefab_id:id` |
| `RuleProfileDef` | `id:standard|relaxed|command, scenario_numerator:int(1|2), scenario_denominator:2, planning_allowed:true, extra_stock:EquipmentCount[], permitted_input_adapters:id[]` |
| `EquipmentCount` | `equipment_id:id, count:count` |
| `ChallengeDef` | `id:id, revision:positive int, base_mission_version_id:id, operations:ChallengeOperation[], replacement_default_stock:EquipmentCount[], replacement_maximum_stock:EquipmentCount[], resulting_budget:positive int` |
| `CompiledMission` | `mission_version_id:id, id:id, revision:positive int, world_id:id, rules_version:id, duration_scenario_ticks:tick, total_residents:count(1..256), minimum_rescues:count, supply_budget:positive int, default_stock:EquipmentCount[], maximum_stock:EquipmentCount[], permitted_equipment_ids:id[], layout_ids:id[], groups:GroupDef[], objectives:ObjectiveDef[], protection_objective_ids:id[], milestones:MilestoneDef[], events:EventDef[], rule_profile_ids:id[], challenge_ids:id[], tutorial_policy:none|guided_first_play_then_unguided|world_mechanic_guidance` |
| `GroupDef` | `id:id, resident_ids:id[], start_node_id:id, assembly_node_id:id, initially_held:bool` |
| `LayoutDef` | `id:id, mission_version_id:id, layout_class:wide|compact, graph_hash:hash, geometry_art_id:id, nav_nodes:NavNode[], nav_edges:NavEdge[], routes:NamedRoute[], sockets:Socket[], water_nodes:WaterNode[], hydraulic_links:HydraulicLink[], sources:WaterRateDef[], sinks:WaterRateDef[], power_nodes:PowerNode[], power_links:PowerLink[], infrastructure:InfrastructureDef[], fire_cells:FireCellDef[], fire_links:FireLink[], supports:SupportDef[], runouts:RunoutDef[], tides:TideDef[], lava_segments:LavaSegmentDef[], resources:ResourceStoreDef[], producers:ProducerDef[], spawns:ResidentSpawn[], fixed_vehicles:VehicleSpawn[], roles:RoleBinding[]` |

Every table field above has a concrete type below. Empty arrays are allowed only for systems absent from a mission. Required rescue systems, named roles, usable evacuation capacity, stock and objective reference solutions must exist before a layout becomes qualified. `graph_hash` hashes the canonical logical layout excluding `graph_hash` and `geometry_art_id`; visual board transforms are absent. Each layout signature is SHA-256 of `{layout_class,graph_hash}` under the canonical encoder.

`EquipmentParameters` is a closed union keyed by its owning equipment ID. It contains exactly the rules-v1 table fields applicable to that equipment: barrier `{crest_mm,warning_head_mm,damage_head_mm,damage_per_pulse}`; bridge `{close_depth_mm,damage_depth_mm}`; pump `{rate_mm3_per_second,max_lift_mm}`; skiff/crawler `{boarding_operation_ticks,unboarding_operation_ticks}`; beacon `{}`; generator `{shutdown_depth_mm,restart_depth_mm,damage_depth_mm}`; firebreak `{maximum_cells,uses}`; sprinkler `{rate_mm3_per_second,maximum_cells}`; brace `{stability_bonus,maximum_per_support}`; winch `{crossing_operation_ticks,duty_operation_ticks,rest_operation_ticks,brake_operation_ticks}`; relay `{disconnect_depth_mm,reconnect_depth_mm}`; cooler `{initial_charges,capacity_charges,cooling_scenario_ticks,crust_scenario_ticks,reheat_scenario_ticks}`; shelter `{initial_protection_scenario_ticks}`. All are nonnegative integers, with exact frozen defaults in `02_RUNTIME_RULES.md#equipment-defaults`; different tuning requires versioned explicit input.

Challenge operations are also closed: `{kind:"shift_event",event_id:id,delta_scenario_ticks:signed int}`; `{kind:"replace_pickup_node",group_id:id,node_id:id}`; `{kind:"reverse_wind_vectors",enabled:true}`; `{kind:"tide_start_index",controller_id:id,index:int(0..3)}`; `{kind:"maximum_equipment_losses",maximum:count}`. A light kit is represented by the replacement stock/budget fields, not a hidden subtract operation. Wind reversal maps 1↔2, 3↔4, 5↔7, 6↔8 and 0→0. High Tide Start's authoring `initial_tide_segment_index:1` expands to `tide_start_index` with the bound controller ID and index 1, setting initial level 600 mm at S=0; it shifts neither events nor the scenario epoch. No arbitrary modifier combinations are generated at runtime. A challenge's expanded mission/layout is separately hashed and reference-qualified.

### Logical layout records

| Record | Exact fields and semantics |
|---|---|
| `NavNode` | `id, kind:assembly|junction|dock|shelter|evacuation|vehicle_parking, position_mm:[long,long,long], resident_capacity:count, vehicle_slots:count, dock_berths:count, mobility_classes:[standard|step_free], hazard_region_ids:id[], power_consumer_id:id|null, is_temporary_staging:bool, safe_depth_mm:quantity, safe_heat_points:int, dock_min_depth_mm:quantity, dock_max_depth_mm:quantity` |
| `NavEdge` | `id, from_node_id, to_node_id, traversal_class:ground|water|cable, mobility_classes:[standard|step_free], movement_capacity:count, base_operation_ticks:positive int, current_multiplier_permille:int(500..2000), administrative_enabled:bool, required_equipment_socket_id:id|null, required_power_consumer_id:id|null, hazard_region_ids:id[], maximum_water_depth_mm:quantity, minimum_water_depth_mm:quantity, maximum_heat_points:int, step_free:bool, tags:id[]` |
| `NamedRoute` | `id, from_node_id, to_node_id, required_waypoint_ids:id[], traversal_class:ground|water|cable, mobility_class:standard|step_free` |
| `Socket` | `id, kind:id, node_id:id|null, permitted_equipment_ids:id[], orientation_steps:int[], relocation_zone:id, footprint_cell_ids:id[], hydraulic_link_id:id|null, intake_node_id:id|null, outlet_node_id:id|null, power_node_id:id|null, support_id:id|null, lava_segment_id:id|null, fire_cell_ids:id[], resource_store_id:id|null, enabled:bool` |
| `WaterNode` | `id, kind:terrain|tank, region_id, elevation_mm:quantity, area_mm2:quantity, initial_volume_mm3:quantity` |
| `HydraulicLink` | `id, from_id, to_id, from_kind:water_node|sea_boundary, to_kind:water_node|sea_boundary, direction:both|forward, sill_mm:quantity, conductance_mm2_per_second:quantity, maximum_transfer_mm3:quantity, gate_id:id|null, initial_gate_open:bool, barrier_socket_id:id|null` |
| `WaterRateDef` | `id, water_node_id, initial_rate_mm3_per_second:quantity`; lists distinguish source from sink |
| `PowerNode` | `id, platform_region_id, initial_enabled:bool, fixed_supply:count, disconnect_depth_mm:quantity, reconnect_depth_mm:quantity` |
| `PowerLink` | `id, from_node_id, to_node_id, initial_enabled:bool, relay_socket_id:id|null` |
| `InfrastructureDef` | `id, kind:clinic|lighthouse|dock|station|pump_house|cooling_works|route_service, power_node_id:id|null, initial_demand:count, initial_priority:count, required_nav_node_ids:id[], required_support_ids:id[], required_equipment_socket_ids:id[], hazard_region_ids:id[], maximum_depth_mm:quantity, maximum_heat_points:int` |
| `FireCellDef` | `id, region_id, initial_fuel:int(0..1000), initial_heat:int(0..1000), initial_wetness:int(0..1000), extra_cooling_points_per_pulse:count, building_cell:bool` |
| `FireLink` | `id, from_cell_id, to_cell_id, direction_code:int(1..8)`; directed links, max eight incoming |
| `SupportDef` | `id, kind:structure|slope, node_ids:id[], edge_ids:id[], region_id, initial_base_stability:int(-1000..1000), static_load_points:int(0..1000), loss_points_per_second:int(0..100), brace_socket_ids:id[], collapse_edge_ids:id[], station_ids:id[]` |
| `RunoutDef` | `id, source_support_id:id|null, segments:RunoutSegment[]` |
| `RunoutSegment` | `id, successor_ids:id[], travel_scenario_ticks:positive int, settle_scenario_ticks:positive int, region_ids:id[], edge_ids:id[], debris_reopenable:bool` |
| `TideDef` | `id, kind:event_ramps|fixed_track, initial_level_mm:quantity, knots:TideKnot[], repeat_period:tick|null`; event_ramps requires empty knots and null repeat |
| `TideKnot` | `scenario_tick:tick, level_mm:quantity` |
| `LavaSegmentDef` | `id, region_id, successor_ids:id[], arrival_delay_scenario_ticks:int(50..6000), crossing_edge_ids:id[], cooler_socket_ids:id[], source_enabled:bool` |
| `ResourceStoreDef` | `id, unit:water_mm3|coolant_charge|coolant_feedstock, initial_amount:quantity, capacity:quantity, water_node_id:id|null`; water stores require the node ID and matching node initial volume |
| `ProducerDef` | `id, infrastructure_id, input_store_id, output_store_id, input_per_charge:1, production_scenario_ticks:100, power_demand:1` |
| `ResidentSpawn` | `resident_id, group_id, mobility_class:standard|step_free, start_node_id, fixed_vehicle_id:id|null`; non-null fixed vehicle starts the person aboard it, not also at the node |
| `VehicleSpawn` | `vehicle_id, kind:fixed_bus|skiff|crawler|winch, node_id, seats:positive int, movement_slots:positive int, initial_route_id:id|null, initial_occupant_ids:id[]` |
| `RoleBinding` | `role_id, target_kind:water_source|nav_edge_set|nav_node|group|fire_region|wind_region|water_store|support|runout|tide|infrastructure|lava_source|socket|shelter_region, target_ids:id[]` |

Fields written without a primitive suffix in this table are `id`. Region membership is the union of water/fire/lava/runout/support records carrying that region ID. A required region with no members is invalid. Water depth for a region is maximum cell depth; exposure tests on an empty region cannot vacuously pass. `NamedRoute` uses a single traversal class; a multi-leg evacuation is an ordered sequence of route legs, not a silently mixed-class Dijkstra query.

For water resources, volume is authoritative only in `WaterNodeState`. Resource lookup aliases that node rather than storing a second mutable water quantity. Sources/sinks are explicit, including fixed equipment facilities. Content compiler must reject duplicated aliases that would make one physical tank independently spendable twice.

## 3. Commands and receipts

The outer command is the C# `CommandDto` above. JSON `schema_version=1`. `actor_input` is exactly `hands_left`, `hands_right`, `controller_left`, `controller_right`, `head_dwell`, `reference_replay` or `system_tutorial`. Simulation commands are legal in Active/Stabilizing and queued Planning/Pause; phase-specific validation still applies. Scene/permission/settings operations belong to the application controller, not entity mutation.

| `kind` | Exact `payload` shape |
|---|---|
| `place_equipment` | `{equipment_id:id,socket_id:id,orientation_step:int(0..3)}` |
| `move_idle_equipment` | `{equipment_instance_id:id,destination_socket_id:id,orientation_step:int(0..3)}` |
| `assign_route` | `{subject_kind:group|vehicle,subject_id:id,stops:RouteStop[]}`; 1..16 stops |
| `set_power_priority` | `{consumer_ids:id[]}`; complete ordered consumer list for the addressed run; duplicates/missing consumers rejected |
| `set_gate` | `{gate_id:id,open:bool}` |
| `activate_tool` | One of `{equipment_instance_id:id,action:"enable"}`, `{equipment_instance_id:id,action:"disable"}`, `{equipment_instance_id:id,action:"firebreak"}`, `{equipment_instance_id:id,action:"cool_segment",segment_id:id}`, `{equipment_instance_id:id,action:"refill",store_id:id}` |
| `end_operation` | `{confirm_failure:bool}` |
| `pause` | `{reason:user|tracking|anchor|platform}` |
| `resume` | `{}` |
| `advance_ticks` | `{operation_ticks:10|50}` |
| `enter_placement` | `{}` |
| `confirm_placement` | `{layout_version_id:id,layout_signature:hash}`; physical transform is handled separately by XR and is not serialized as simulation command authority |
| `exit_to_hub` | `{disposition:save_and_exit|abandon}` |

For a group, stops use `assemble` or `evacuate` with `group_id=null`; it is the subject group. Vehicle pickup stops require `group_id` identifying the group; dropoff/evacuate stops have `group_id=null`. A dropoff is to an intermediate node; an evacuate stop must target a final evacuation node. Never accept individual resident drag commands. A group can be routed from its fixed assembly panel without owning a beacon. Route assignment cannot change the current in-flight leg or discard a partial passenger/occupied vehicle; it replaces future stops after current safe arrival/transfer resolution.

The input broker generates a UUID string command ID and strictly increasing sequence starting at 1. Accepted placement generates `eq:<sequence>`, route assignment generates `route:<sequence>`, and their reservation IDs append the stable resident/resource IDs to that sequence. These simulation entity IDs never incorporate UUIDs, wall time or object instance addresses. The broker samples intent in a stable adapter order (active preferred hand first, other hand next, controllers left/right, head); only the selected input adapter family may commit. A captured equipment instance has one gesture owner. Cancel releases capture and does not create a deploy command.

Simulation `requested_tick` must equal O+1 at admission. Planned commands queue for the next operation step, maximum 32, in sequence order. The broker can collect more commands only after those execute or are explicitly cancelled through its local planning UI before admission. Arbitrary far-future command injection is rejected. Application commands request the current O and execute immediately at a boundary. A run permits at most 10,000 admitted commands; excess is rejected with `COMMAND_LIMIT_REACHED` and cannot silently drop history.

Admission validates syntax/version/run/sequence and a nonmutating current-state preview. A queued result is `{status:"queued",accepted:false,reason_code:"PENDING_EXECUTION",executed_tick:-1,...}`; it is not final acceptance. At execution, validate again against the then-current state. A queued preview has no reserved supplies or authoritative equipment entity. Two affordable preview actions competing for the same final stock may therefore result in one accepted and one stable rejection. The result history distinguishes this honestly.

Deduplication precedes mutation. Store `(command_id,sequence,request_sha256,result)` in the run receipt ledger. Repeating the exact command returns the current/original receipt without reapplying. Reusing an ID or sequence with different bytes rejects `DUPLICATE_ID_MISMATCH`/`SEQUENCE_REUSED`. A new command must have sequence exactly `last_admitted_sequence+1`; old/gapped sequences reject `INVALID_SEQUENCE`. Once the header/schema/run/ID/sequence are valid, admission consumes the sequence and records a receipt even if phase, stock, target or another preview check rejects it. Malformed/wrong-run/reused/gapped envelopes do not consume a sequence; the broker reads the current next expected sequence, without retrying a mutation. Pending commands retain admission identity even if later immediate pause/resume commands have higher sequences. Execution must not falsely reject them because a higher application sequence already completed.

Final receipts have status accepted/rejected, accepted true/false respectively, a nonnegative executed_tick and stable reason code. Acceptance code is `OK`. Closed rejection codes are `INVALID_SCHEMA`, `UNKNOWN_KIND`, `CONTENT_VERSION_MISMATCH`, `WRONG_RUN`, `INVALID_SEQUENCE`, `SEQUENCE_REUSED`, `DUPLICATE_ID_MISMATCH`, `INVALID_TICK`, `INVALID_PHASE`, `UNKNOWN_TARGET`, `INSUFFICIENT_SUPPLIES`, `STOCK_EXHAUSTED`, `SOCKET_INCOMPATIBLE`, `OCCUPIED`, `NOT_IDLE`, `UNSAFE_TARGET`, `ROUTE_BLOCKED`, `CAPACITY_FULL`, `POWER_REQUIRED`, `RESOURCE_EMPTY`, `TRACKING_UNAVAILABLE`, `OBJECTIVES_UNMET`, `QUEUE_FULL`, `COMMAND_LIMIT_REACHED`. Evaluate checks in this listed order where multiple failures apply. `PENDING_EXECUTION` is a queue code only.

`end_operation(false)` rejects unless the current terminal safety/required predicates could succeed; otherwise it schedules terminal evaluation at the end of the next step. Hazards may still change the final result in that step. `end_operation(true)` explicitly permits ending a failed attempt. Pause→step→resume application operations use the same recorded command path, with no private simulation shortcut for a replay solver.

## 4. Events: typed parameters, not prose

Authoring `EventDef` is exactly `{id,at_scenario_tick,warning_at_scenario_tick,known_at_start,operations}`. Authored effect ticks satisfy `1 <= at_scenario_tick < duration_scenario_ticks`; warning ticks satisfy `0 <= warning_at_scenario_tick <= at_scenario_tick`. Disruptive effects have at least 100 scenario ticks lead unless a longer source warning is explicitly bound. Effects at zero or at the deadline are rejected; initial values belong in compiled initial-state fields. New-run initialization emits S=0 warnings exactly once in event-ID order before the initial objective sample and first Active step, storing their IDs in `EventState.warned_event_ids`. Loading an initialized snapshot and forecast cloning preserve that set without repeating warning emission. `operations` is a nonempty array of `{kind,target_role,value}` with integer `value`. Compilation resolves roles to typed target IDs and emits corresponding immutable operands. It rejects role-type mismatch, unbound roles and conflicting parameter writes at one tick.

| Operation | Target kind and meaning of `value` |
|---|---|
| `set_source_rate` | water_source; replaces mm³/scenario-second rate, 0..1,000,000,000 |
| `warn_route` | nav_edge_set; advisory forecast lookahead in scenario ticks; never closes an edge by itself |
| `set_edge_enabled` | nav_edge_set; 0/1 administrative admission flag; 1 never overrides physical hazard/power/capacity conditions |
| `set_current_multiplier` | nav_edge_set; 500..2000 travel-speed permille for subsequent admissions |
| `release_group` | group; exactly 1; clears initial hold, preserving population and actual start location |
| `ignite_region` | fire_region; 0..1000 minimum heat injection (`H=max(H,value)`), followed by ordinary fuel/wetness/warning rules |
| `set_wind` | wind_region; compass code 0..8 from rules; takes effect at the event tick |
| `set_region_cooling` | fire_region; 0..100 extra heat-loss points per hazard pulse; no forced safe state |
| `warn_resource` | water_store; threshold 0..1000 permille of capacity; installs/displays that low-resource advisory, no volume change |
| `add_heat` | fire_region; 0..1000 heat added and clamped to 1000; no direct resident-state change |
| `warn_wind_change` | wind_region; lead in scenario ticks to an actual scheduled later set_wind; compiler verifies that effect, never secretly schedules it |
| `set_stability_loss` | support; 0..100 points/scenario-second loss |
| `set_support_load` | support; 0..1000 static load points |
| `tremor` | support; 0..1000 base-stability subtraction once |
| `warn_runout` | runout; lead ticks to a declared release; marks its warning timestamp, never releases it |
| `release_avalanche` | runout; exactly 1; starts the authored graph after warning checks |
| `warn_support` | support or infrastructure; advisory lookahead ticks; marks support warning where applicable, no power/stability mutation |
| `set_tide_segment` | tide; ramp target index 0..3; continuous 100-tick ramp from current level |
| `set_infrastructure_demand` | infrastructure; demand 0..32 power units |
| `advance_lava` | lava_source; exactly 1; first bound source segment arrives Hot at this warned effect tick; successors use authored delay |
| `enable_cooler_socket` | socket; 0/1 eligibility flag, no coolant or cooled segment created |
| `warn_reheat` | lava_source/segment role; lookahead ticks for its actual cooldown-expiry advisory; never creates or extends a timer |
| `enable_shelter_hazard` | shelter_region; 0/1 enabled external heat-front flag; ordinary finite protection/exposure applies |

All freeze-v1 mission events are known at start. An eventual later-disclosure event is known only when its warning is published. Forecast knowledge handling remains the rule in doc 02. Event logs identify the actual field change, causal event ID and target IDs; narrative descriptions are localization lookup keys only.

Runtime emitted events have `{event_id:id,operation_tick:tick,scenario_tick:tick,ordinal:count,kind:RuntimeEventKind,subject_id:id|null,cause_command_id:id|null,cause_event_id:id|null,payload:TypedEventPayload}`. `event_id` is `evt:<operation_tick>:<ordinal>`; ordinal follows fixed stage then stable subject order. Kinds/payloads are a closed registry: CommandResolved `{sequence,status,reason_code}`; EquipmentPlaced/Moved/Broken `{equipment_instance_id,socket_id}`; PowerGrantChanged `{consumer_id,old_grant,new_grant}`; WaterTransfer `{request_id,source_id,destination_id,granted_mm3}`; HazardWarning `{hazard,region_id,effect_scenario_tick}`; HazardStateChanged `{hazard,subject_id,old_state,new_state}`; TransferStarted/Completed/Cancelled `{resident_id,vehicle_id,node_id,direction}`; RouteBlocked `{subject_id,edge_id,reason_code}`; ResidentEvacuated `{resident_id,node_id,final_leg_mode}`; ResidentAwaitingBackup `{resident_id,location}`; MilestoneReached `{milestone_id}`; ObjectiveUpdated `{objective_id,fulfilled}`; OutcomeResolved `{success,stars,score}`. Telemetry/cosmetic events use a separate nonauthoritative stream.

The full event stream is optional diagnostic evidence, not an unbounded save. Persist only the causal summary/metrics required by objectives and debrief, plus the last 256 ordered gameplay events; reference-replay evidence may write the complete stream outside the player save.

## 5. Exact objective and metric union

Each objective is `{id,tier,kind,params,temporal}`; tier is required/critical/mastery/optional. A condition is `{kind,params}`. `all` and `any` params are exactly `{conditions:Condition[]}` with 1..16 children; children do not carry their own temporal policy. Maximum recursive depth is 4. Empty all/any is invalid.

| Condition kind | Exact params / truth definition |
|---|---|
| `rescue_count` | `{minimum:count}`; evacuated unique resident count≥minimum |
| `group_evacuated` | `{group_id:id,minimum:count}`; group's evacuated unique count≥minimum |
| `infrastructure_operational` | `{infrastructure_id:id,operational:bool}`; compare the fully derived operational state |
| `route_available` | `{route_id:id,traversal_class:ground|water|cable}`; named route endpoints/waypoints admit a compatible path now, including required power/structure; class must match NamedRoute |
| `exposure_below` | `{region_id:id,hazard:water|fire|lava|runout,threshold:quantity}`; strict `< threshold`; water is maximum depth mm, fire/lava maximum heat points, runout is 0 or 1 occupancy |
| `equipment_losses_at_most` | `{maximum:count}`; broken-instance count≤maximum |
| `supply_remaining_at_least` | `{minimum:count}`; deployment budget remaining≥minimum |
| `resource_remaining` | `{store_id:id,minimum:quantity}`; current amount in that store's declared unit≥minimum; no cross-unit arithmetic |
| `all_vehicles_unloaded` | `{}`; all occupant arrays empty, including failed vehicles |
| `temporary_shelters_empty` | `{}`; no resident located in any temporary shelter |
| `temporary_staging_empty` | `{}`; no resident located at any node tagged temporary staging |
| `scenario_elapsed_at_most` | `{maximum:tick}`; S≤maximum |
| `counter_at_least`, `counter_at_most` | One closed metric parameter shape below, with `minimum` or `maximum` respectively |

Metric shapes include exactly `metric`, their listed filters and the threshold field. No optional free-form filters are admitted:

| Metric | Filters and authoritative meaning |
|---|---|
| `evacuated_by_mode` | `group_id:id|null,mode:ground|water|cable`; distinct evacuated residents whose **final evacuation leg** has that mode; null means all groups |
| `edge_transfers` | `edge_tag:id`; count distinct resident IDs that completed an edge with this tag, including vehicle passengers; repeating the edge does not increment that person's count again |
| `equipment_deployments` | `equipment_id:id`; total successful deployments, including subsequent breakages; relocation is not deployment |
| `max_node_dwell_scenario_ticks` | `node_id:id`; maximum continuous time any nonterminal resident spent at this node while waiting/boarding; includes current incomplete dwell, excludes travel time |
| `peak_node_occupancy` | `node_id:id`; highest actual resident occupancy, including people boarding but still at node, excluding capacity reservations for in-transit people |
| `max_dock_queue_operation_ticks` | `node_id:id`; longest continuous wait after a vehicle reaches that dock's safe holding node for a berth/receiving capacity; includes current wait, not approach travel |
| `loaded_closed_dock_wait_operation_ticks` | no filters; cumulative operation ticks a loaded vehicle waits at a dock/holding node whose chosen next unload dock is closed; sum across vehicles |
| `pumped_into_region_mm3` | `region_id:id`; actual cumulative pump grants entering water nodes in that region; includes all destinations there, not only currently occupied nodes |
| `generator_shutdowns` | no filters; active generator→safety-tripped transitions; initial off is not a shutdown |
| `station_shutdowns` | no filters; operational station infrastructure→nonoperational transitions after first activation; initial unpowered is not a shutdown |
| `shelter_exhaustions` | no filters; positive protection→zero while occupied/hazardous, once per shelter |

Milestone authoring is `{id,kind,target}`. Kinds are `group_boarded` targeting a group, `final_unload_started` and `all_evacuated` targeting reserved `all_residents`, and `first_cooled_crossing_closed` targeting a bound lava role. Reserved `operation_started`/`operation_ended` are engine-generated and cannot be redefined. Every milestone stores its first `(O,S)` only. Final-unload causality and first-crossing timing are fixed in doc 02; they are not inferred retrospectively from the final replay.

Temporal JSON is the following exact one-of union (no other fields):

```json
{
  "$defs": {
    "temporal": {
      "oneOf": [
        {"type":"object","additionalProperties":false,"required":["kind"],"properties":{"kind":{"const":"at_end"}}},
        {"type":"object","additionalProperties":false,"required":["kind","deadline_tick"],"properties":{"kind":{"const":"ever_by"},"deadline_tick":{"type":"integer","minimum":0,"maximum":1000000}}},
        {"type":"object","additionalProperties":false,"required":["kind","start_event","end_event"],"properties":{"kind":{"const":"always"},"start_event":{"type":"string","minLength":1},"end_event":{"type":"string","minLength":1}}},
        {"type":"object","additionalProperties":false,"required":["kind","end_event"],"properties":{"kind":{"const":"ever_before"},"end_event":{"type":"string","minLength":1}}},
        {"type":"object","additionalProperties":false,"required":["kind","scenario_ticks","start_event","end_event"],"properties":{"kind":{"const":"hold_for"},"scenario_ticks":{"type":"integer","minimum":1,"maximum":1000000},"start_event":{"type":"string","minLength":1},"end_event":{"type":"string","minLength":1}}}
      ]
    }
  }
}
```

The compiler additionally applies ID syntax/reference validation to start/end strings and the interval semantics in doc 02. It must never accept a generic `eval`, script path, method name or LLM-derived expression as a condition.

## 6. Full snapshot ownership

A snapshot is an aggregate, not the ERD's small illustrative field subset. Its exact top-level shape is:

```text
Snapshot {
  schema_version:1, identity:RunIdentity, state_revision:count,
  phase:planning|active|stabilizing|outcome, operation_tick:tick, scenario_tick:tick,
  scenario_accumulator:int(0..1), scenario_enabled:bool,
  guidance_state:GuidanceState, rng:RngState,
  supplies_remaining:count, selected_stock:EquipmentCount[], deployments:EquipmentCount[],
  water:WaterNodeState[], rate_remainders:RateRemainder[],
  power:PowerState, equipment:EquipmentState[], vehicles:VehicleState[],
  residents:ResidentState[], transits:TransitState[], transfers:PassengerTransfer[],
  reservations:Reservation[], routes:RouteOrder[], queues:QueueEntry[],
  fire:FireCellState[], supports:SupportState[], runouts:RunoutState[],
  tides:TideState[], lava:LavaState[], stores:StoreState[], producers:ProducerState[],
  event_state:EventState, milestones:MilestoneState[],
  metrics:MetricState[], objective_monitors:ObjectiveMonitor[],
  last_admitted_sequence:count, pending_commands:CommandDto[],
  command_receipts:CommandReceipt[], recent_events:RuntimeEvent[],
  water_ledger:WaterLedger, end_request:EndRequest|null, outcome:OutcomeState|null
}
```

All state arrays are sorted by ID except ordered route stops, event order and command sequence. Their concrete required fields follow. Counters/remaining values may be zero; references can be null only where stated.

| State type | Exact stored fields |
|---|---|
| `GuidanceState` | `policy:id, step_id:id, completed_step_ids:id[], acknowledged_warning:bool` |
| `RngState` | `algorithm:"xorshift32-v1", state:uint(1..4294967295), draws:nonnegative long` |
| `WaterNodeState` | `id, volume_mm3:quantity` |
| `RateRemainder` | `id, kind:source|sink|sprinkler, remainder:int(0..9), current_rate_mm3_per_second:quantity` |
| `PowerState` | `node_enabled:IdBool[], link_enabled:IdBool[], consumer_demands:IdCount[], priorities:IdCount[], grants:IdCount[], generator_trip_ids:id[]` |
| `IdBool` / `IdCount` | `{id,value:bool}` / `{id,value:count}` |
| `EquipmentState` | `id, equipment_id, socket_id, orientation_step:int(0..3), lifecycle:deployed|broken|exhausted, enabled:bool, durability:count, parameters:EquipmentRuntimeParameters` |
| `VehicleState` | `id, equipment_instance_id:id|null, kind:fixed_bus|skiff|crawler|winch, location:VehicleLocation, occupant_ids:id[], route_order_id:id|null, status:idle|waiting_route|boarding|travelling|unboarding|waiting_power|braked|returning|resting|failed, duty_used:count, rest_remaining:count, brake_remaining:count` |
| `ResidentState` | `id, group_id, state:sheltered|waiting|walking|boarding|in_transit|disembarking|evacuated|awaiting_backup, location:ResidentLocation, reservation_ids:id[], node_entered_scenario_tick:tick|null, consecutive_exposure_ticks:count, final_leg_mode:ground|water|cable|null` |
| `TransitState` | `id, mover_kind:resident|vehicle, mover_id, edge_id, origin_node_id, destination_node_id, duration_operation_ticks:positive int, progress_operation_ticks:count, mode:forward|return|backup, remaining_return_ticks:count, closure_latched:bool` |
| `PassengerTransfer` | `id, resident_id, vehicle_id, node_id, direction:board|unboard, progress_operation_ticks:int(0..9), seat_reservation_id, node_reservation_id:id|null` |
| `Reservation` | `id, owner_id, resource_kind:edge_slots|node_people|vehicle_seat|dock_berth|return_node, resource_id, quantity:positive int, created_operation_tick:tick` |
| `RouteOrder` | `id, subject_kind:group|vehicle, subject_id, stops:RouteStop[], next_stop_index:count, resolved_edge_ids:id[], submitted_operation_tick:tick, submitted_sequence:count` |
| `QueueEntry` | `id, subject_id, target_node_id, kind:berth|capacity|edge|power, since_operation_tick:tick, submitted_sequence:count` |
| `FireCellState` | `id, fuel:int, heat:int, wetness:int, state:unburned|heating|burning|cooling|safe_ash, warning_started_tick:tick|null, cooling_started_tick:tick|null, admission_closed:bool, spray_remainder_mm3:int(0..24999), extra_cooling_points_per_pulse:count` |
| `SupportState` | `id, base_stability:int(-1000..1000), static_load_points:int, loss_points_per_second:int, loss_remainder:int(0..9), warning_started_tick:tick|null, collapse_pending:bool, collapsed:bool` |
| `RunoutState` | `id, activated:bool, segments:RunoutSegmentState[]` |
| `RunoutSegmentState` | `id, arrival_tick:tick|null, clearing_tick:tick|null, state:dormant|moving|settling|cleared` |
| `TideState` | `id, ramp_start_tick:tick, ramp_start_level_mm:quantity, ramp_end_tick:tick, ramp_end_level_mm:quantity, current_level_mm:quantity` |
| `LavaState` | `id, state:dormant|hot|cooling|crusted|reheating, arrival_tick:tick|null, cooling_complete_tick:tick|null, crust_expiry_tick:tick|null, hot_again_tick:tick|null, ever_cooled:bool, cooler_instance_id:id|null` |
| `StoreState` | `id, unit:coolant_charge|coolant_feedstock, amount:quantity`; water is intentionally excluded |
| `ProducerState` | `id, work_in_progress_units:int(0..1), completed_work_scenario_ticks:int(0..99), reserved_input_store_id:id|null` |
| `MilestoneState` | `id, operation_tick:tick, scenario_tick:tick` |
| `MetricState` | `id, metric:id, subject_id:id|null, value:quantity, distinct_resident_ids:id[]`; exact metric/filter identity is compiled and ID-bound |
| `ObjectiveMonitor` | `id, started_operation_tick:tick|null, ended_operation_tick:tick|null, first_true_operation_tick:tick|null, first_true_scenario_tick:tick|null, violated:bool, current_hold_scenario_ticks:count, fulfilled:bool` |
| `CommandReceipt` | `command_id, sequence:count, request_sha256:hash, result:CommandResultDto` |
| `WaterLedger` | `initial_total_mm3:quantity, direct_sources_mm3:quantity, sea_in_mm3:quantity, drains_mm3:quantity, sea_out_mm3:quantity, sprayed_mm3:quantity` |
| `EndRequest` | `command_id, confirm_failure:bool` |
| `OutcomeState` | `success:bool, stars:int(0..3), score:int(0..10000), rescued:count, backed_up:count, reason:success|deadline_unmet|player_ended_unmet`; non-null exactly when phase is outcome |

Controlled amendment 9: completed boarding is persistent causal history, required by the existing `group_boarded` milestone rule. Register an **internal causal-summary** `MetricState` for each distinct group targeted by a compiled `group_boarded` milestone. Its ID is `transport:boarding:` followed by SHA-256 of canonical `{mission_version_id,group_id}`; `metric="group_boarded"`, `subject_id=group_id`, `value` is the size of `distinct_resident_ids`, and that array is the sorted unique completed-boarder subset of the compiled group. Initial vehicle occupants seed the history. Add a resident only on completed boarding; retain history after unloading, evacuation or backup. Walking evacuation never creates boarding history. The milestone latches once when the history reaches the entire group, using the actual first boundary; initial true history is sampled at O=S=0.

This internal row is derived from compiled mission/group/milestone identity, not an authorable counter query. The closed public objective metric union above remains unchanged: `group_boarded` is not a new `counter_at_least`/`counter_at_most` parameter. Producers merge metrics by stable ID and reject duplicate ownership. A future loader must verify exact expected internal rows, group membership, sorted uniqueness/count, initial-occupant inclusion and milestone coherence, then reconstruct boarding history. These rows participate in the existing rule-state projection and full save integrity. Capped/hash-excluded `recent_events` and private continuation fields cannot substitute for this history. Route and reservation identities remain command-derived as specified in section 3; transport uses no hidden global entity serial. Completed stage guards derive from the saved operation boundary.

The root Snapshot and MetricState shapes and rules-v1 outcomes do not change. This clarification closes an implementation-state gap before any accepted transport/save result; no accepted resident save or replay is migrated or silently reinterpreted. Later C07 still owns complete load/replay acceptance.

`VehicleLocation` is exactly `{kind:"node",node_id}` or `{kind:"edge",transit_id}`. `ResidentLocation` adds `{kind:"vehicle",vehicle_id}` to those alternatives. Boarding keeps node location until completion; unloading keeps vehicle location. An evacuated resident remains at its final node; a backup resident retains the actual node/edge/vehicle location. The compiler authors sufficient final refuge capacity; final evacuation does not secretly delete people to make a dock fit.

`EquipmentRuntimeParameters` is keyed by owning equipment ID: barrier/bridge/beacon/brace/relay/skiff/crawler `{}`; pump `{fractional_remainder:int(0..49999)}`; generator `{trip_latched:bool}`; firebreak `{activated:bool}`; sprinkler `{}` (rate remainder is owned by RateRemainder); winch `{}` (duty/rest/brake are owned by VehicleState); cooler `{charges:int(0..1),active_segment_id:id|null}`; shelter `{protection_remaining:int(0..600),exhaustion_emitted:bool}`. Firebreak activation persists even if the equipment later breaks; it is never inferred from current fuel or enabled state. Derived values are validated against their single owner, never independently mutated in two places.

`EventState` is exactly `{next_static_event_index:count,fired_event_ids:id[],revealed_event_ids:id[],warned_event_ids:id[],dynamic_events:DynamicEvent[],administrative_edge_enabled:IdBool[],edge_current_multipliers:IdCount[],gate_open:IdBool[],group_held:IdBool[],wind_region_codes:IdCount[],cooler_socket_enabled:IdBool[],shelter_region_enabled:IdBool[],resource_warning_thresholds:IdCount[]}`. `revealed_event_ids` records forecast knowledge; `warned_event_ids` separately records actual warning emission, including initialization at S=0. A known-at-start event is revealed before its warning, so these sets cannot be conflated. Dynamic events are only `{id,at_scenario_tick,kind:runout_arrival|runout_clear|lava_arrival,target_id,cause_event_id}`. Cooler clocks live in LavaState, not a second event queue. Sort dynamic events by tick then ID. Persist source/event cursors, not just the initial seed. Runtime advisory-only forecasts can be reconstructed from the fired/revealed/warned events; they do not introduce an unrecorded parameter mutation.

A load validator checks bidirectional location/occupant references, resident conservation, no evacuated reboarding, capacities including reservations, ledger equality, stocked deployment counts, complete objective/milestone state, event once-only state, pending command/receipt coherence, and recomputed power/derived thresholds. Recompute-only fields must equal their saved values where serialized; an inconsistent snapshot is rejected rather than silently repaired.

Application save metadata holds paused/interrupted/pending-placement status separately. Reload always presents an explicit paused Continue path; it never advances simulation while rebuilding scenes. Rendering transforms, camera pose, particle interpolation, hover/gesture previews, transient audio handles, raw hand/room data and real-time countdown progress are absent from authority. After resume, a new countdown starts without changing the saved simulation.

## 7. Canonical JSON, hashing and PRNG

The strict JSON profile is `resq-canonical-json-1`, not a claim of general RFC canonicalization support:

1. UTF-8 without BOM; reject malformed UTF-8, duplicate keys at any depth, lone surrogate code units, NaN/infinity, floating/exponent number forms, leading-zero integers and `-0`.
2. All integer values must fit signed 64-bit except the explicitly bounded uint32 state, which also fits that envelope. JSON booleans are actual booleans, never accepted as integers by a Python validator.
3. String data must already be Unicode NFC; reject non-NFC input rather than normalizing it under a hash. Keys are ASCII schema keys, sorted ordinal ascending in canonical output.
4. Emit no insignificant whitespace. Emit integers in shortest base-10 form. Escape `"` and `\`; emit U+0000..U+001F as lowercase `\u00xx`; emit other characters as literal UTF-8. Do not escape `/`, ASCII `<`/`>`/`&`, U+2028 or U+2029.
5. Arrays preserve semantic order. The content compiler sorts definition/state entity collections by ID; route stops, command sequences, event operation arrays and tuple coordinates retain their declared order. Canonical encoding never sorts arbitrary arrays on its own.
6. SHA-256 is computed over these canonical bytes. Envelope hashes exclude the envelope and hash only its `payload`. A graph hash excludes its own field and art ID as defined above. Integrity hashing is neither encryption nor an anti-cheat claim.

The authoritative rule-state hash is the canonical Snapshot projection excluding `identity.run_id`, `identity.source_build_id`, `identity.input_adapter_id`, `state_revision`, `command_receipts`, `recent_events` and `end_request.command_id`; replace every remaining command-ID reference in the projection with its recorded sequence. Each pending command also excludes `run_id`, `command_id` and `actor_input`. Include all other rule-relevant state, command cursor, pending semantic command bodies/sequences/ticks, clocks, objectives, RNG and layout/preset/challenge/guidance identity. `assisted` remains included. Equal semantic commands at equal ticks under the same rules/content/layout/profile/guidance therefore produce equal rule-state hashes across equivalent hands, controller and dwell adapters, including an adapter switch. Pure and mixed adapter comparison partitions remain distinct. Save integrity hashes cover the complete payload, including adapter identity, actor labels, IDs and receipts; excluding them from rule-state comparison does not erase provenance.

Rules-v1 has no random hazard spread or random mission topology. A separate cosmetic RNG never enters state hashes. The reserved authoritative PRNG is `xorshift32-v1`, initialized from the low 32 bits of the run seed; seed/state zero is replaced at initialization by 0x9E3779B9. Subsequent zero state is invalid. Its exact operation is:

```csharp
public static uint Next(ref uint state) {
    unchecked {
        uint x = state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state = x;
        return x;
    }
}
```

`draws` increments once per Next call. For a bounded sample `bound` in 1..uint.MaxValue, compute `threshold=unchecked(0u-bound)%bound`, draw until `r>=threshold`, return `r%bound`. Never use `%` alone for a uniform bounded choice. Adding an authoritative use of randomness requires a rules revision and specified draw sites/order. Snapshot stores current state/draw count, not merely seed. The seed-1 first five values are 270369, 67634689, 2647435461, 307599695, 2398689233.

The fixture canonical object `{"a":1,"b":[true,null,"x"]}` hashes exactly its UTF-8 bytes. The machine-readable fixture supplies the actual expected hash; C# and Python must independently match those bytes and hash.

## 8. Saves, migration and replay compatibility

```text
SaveEnvelope {
  schema_version:1, generation:positive long, payload_sha256:hash, payload:PlayerSave
}
PlayerSave {
  local_profile_id:id, settings:Settings,
  mission_progress:MissionProgress[], unlocks:Unlock[], reward_receipts:RewardReceipt[],
  current_run:Snapshot|null, completed_runs:RunSummary[], application_recovery:RecoveryState
}
Settings {
  preferred_rule_profile:standard|relaxed|command, preferred_input_adapter:id,
  preferred_hand:left|right, dwell_ms:int(500..1500,step:100),
  text_scale_percent:100|125|150, captions:bool, caption_text_scale_percent:100|125|150,
  caption_background_opacity_percent:70|80|90|100, target_scale_percent:100|150,
  highlight_intensity_percent:40|50|60|70|80|90|100, reduced_effects:bool,
  audio_music:int(0..100,step:5), audio_world:int(0..100,step:5),
  audio_warnings:int(0..100,step:5), audio_speech:int(0..100,step:5),
  preferred_layout:wide|compact
}
MissionProgress {
  mission_version_id:id, comparison_partition:hash, completed:bool,
  best_stars:int(0..3), best_score:int(0..10000), best_run_id:id,
  campaign_mission_id:id
}
Unlock { id:id, kind:mission|challenge|postcard|skin, awarded_by_run_id:id }
RewardReceipt { reward_id:id, source_run_id:id, applied:bool }
RunSummary {
  identity:RunIdentity, success:bool, stars:int(0..3), score:int(0..10000),
  rescued:count, backed_up:count, final_operation_tick:tick, final_scenario_tick:tick,
  final_state_hash:hash
}
RecoveryState { interrupted:bool, reason:none|tracking|anchor|platform|user, layout_reconfirmation_required:bool }
```

All Settings fields persist across restart. `int(a..b,step:n)` admits exactly `a+k*n` for integer k within the inclusive bounds; generate an explicit integer enum and reject off-step values rather than rounding. Fresh-profile defaults are Standard/hands, right hand, dwell 800 ms, text and caption text 100%, captions on, caption background 90%, target scale 100%, highlight 70%, reduced effects off, music/world/warnings/speech 50/80/100/80, and preferred wide layout subject to actual placement eligibility. Caption text size is stored independently from general text size. The 100% target mode is the already-large baseline in doc 05; 150% is its optional enlargement.

No email, account token, microphone, room mesh, raw camera frame or raw hand pose is stored. Settings are profile-wide; the run freezes its own rule/input/guidance comparison values. Changing a presentation setting does not change authoritative rules. Changing rule profile midway requires restarting the mission; switching equivalent hand/controller adapter preserves rule-state hashes but records an `input_adapter_id="mixed"` comparison partition at the next command, never overwriting the original evidence as pure hands. Command mode pacing is a distinct rule profile.

Comparison partition hashes `{mission_version_id,rules_version,layout_signature,modifiers_hash,rule_profile_id,input_adapter_id,guidance_policy,assisted}`. `challenge_id="base"` is the explicit no-challenge ID; its modifiers hash is SHA-256 of canonical `[]`. Profile progress aggregates stable campaign mission completion across compatible revisions while best scores remain in exact partitions.

Save filenames are `save.current.json`, `save.previous.json`, `save.pending.json` beneath the platform persistent-data path. At a boundary, clone a complete immutable save generation; coalesce pending save requests to the newest complete generation. Write pending, flush when supported, reopen/strictly deserialize/hash/validate it. Preserve the current valid bytes into a temporary previous file and atomically replace previous; then atomically replace current with pending using the verified Android operation. A crash at any step leaves at least current or previous valid. Do not remove the only valid generation before promotion. Runtime storage errors warn once per failure episode and keep the previous valid progress; they do not fabricate success.

On load, validate current/previous independently and choose the highest valid supported generation. A valid future-schema file is preserved without overwrite; show incompatibility and require a compatible build, even if an older backup is readable. A corrupt current with a valid supported backup offers recovery from that exact backup. If both are invalid, new-profile creation requires explicit confirmation; archive the invalid bytes locally for recovery without automatic upload.

Migration is a pure `vN -> vN+1` transform on a copy, with checked fixtures for every supported predecessor and a new validated/hash-encoded generation. Schema 1 currently has **no earlier schemas** and no invented migration implementation. The first actual schema change must supply migration fixtures before adoption. Numeric tuning changes create a new immutable mission/content version; algorithm/order changes create a new rules version. Incompatible in-progress runs offer restart on current content while retaining prior campaign completion. Never substitute a new graph under an old snapshot.

Replay envelope:

```text
Replay {
  schema_version:1, identity:RunIdentity, initial_snapshot:Snapshot,
  commands:RecordedCommand[], checkpoints:ReplayCheckpoint[], expected:ReplayExpectation,
  provenance:ReplayProvenance
}
RecordedCommand { admitted_at_operation_tick:tick, command:CommandDto, expected_result:CommandResultDto }
ReplayCheckpoint { operation_tick:tick, scenario_tick:tick, expected_state_sha256:hash }
ReplayExpectation { success:bool, rescued:count, stars:int(0..3), score:int(0..10000), final_state_sha256:hash }
ReplayProvenance { generator:manual_input|reference_agent|test_fixture, source_build_id:id, simulation_assembly_sha256:hash }
```

Record a checkpoint every 100 operation ticks and at terminal outcome; save/resume experiments may add boundaries. A reference replay executes real commands through the actual validator/simulator; no expected result is injected into state. Rejected commands are retained. Application commands are replayed at their admission boundary, including pauses/advances, with the wall-clock wait omitted. Playback pauses only as dictated by recorded application operations; reference completion cannot depend on a live human gesture.

### Replay application journal — controlled amendment 15

An external application recovery callback is an input distinct from a recorded Command. The separately supplied `ReplayApplicationJournal` below records that lifecycle input and the actual initial application state. It closes the previously unrepresented recovery input before any C07 acceptance. It is a developer replay/evidence artifact, not a Snapshot field, command payload, player-save field or substitute for authoritative continuation state. Existing Snapshot, Command and Replay shapes and schema versions remain unchanged.

```text
ReplayApplicationJournal {
  schema_version:1, replay_sha256:hash,
  initial_application_state:running|paused|interrupted|loading|outcome,
  initial_interruption_reason:null|tracking|anchor|platform|loading,
  recoveries:ReplayApplicationRecovery[]
}
ReplayApplicationRecovery { before_command_index:count, operation_tick:tick }
```

Both journal objects are closed and use the strict readers and canonical JSON profile in sections 1 and 7. `replay_sha256` is SHA-256 over the canonical bytes of the complete, exact supplied Replay object, including its initial snapshot, commands, expected results, checkpoints, expectation and provenance; it is not a rule-state hash or a hash of selected fields. A wrong binding rejects the journal. The journal's own SHA-256 covers its complete canonical object with no excluded fields. Whenever a journal is supplied, including one with no recovery rows, `ReplayReport.ApplicationJournalSha256` and the replay evidence must bind that hash and retain the exact journal artifact alongside the exact Replay. The report value is null only when no journal was supplied. Evidence may not omit the journal and still claim reproduction of a trace that used it.

Initial lifecycle validation is exact:

| Initial Snapshot phase | Allowed initial application state | Required initial interruption reason |
|---|---|---|
| `planning` | `paused` | null |
| `active` or `stabilizing` | `running` or `paused` | null |
| `active` or `stabilizing` | `interrupted` | `tracking`, `anchor` or `platform`, matching the actual recorded interruption |
| `active` or `stabilizing` | `loading` | `loading`, matching the actual recorded interruption |
| `outcome` | `outcome` | null |

Create fresh actual domain owners from the exact initial Snapshot for each replay execution. To establish an explicitly recorded interrupted/loading start, call the actual `Interrupt(reason)` application API on that freshly restored authority; do not insert an interruption flag into authoritative state. Initial application lifecycle setup must leave the complete canonical Snapshot unchanged, including clocks, revision, command cursor, receipts, RNG and domain state. This replay setup policy does not change normal player load: a loaded current run still presents the explicit paused Continue path in section 6.

`before_command_index` is the zero-based index into the supplied Replay's ordered `commands`. Recovery rows must be strictly increasing and unique by that index; each index is less than `commands.count`, so the recovery array is also bounded by that count. Each row's `operation_tick` must equal that command's `admitted_at_operation_tick`. After actual stepping reaches that boundary, consume the row exactly once immediately before admitting that command, at exactly the matching actual operation tick. Invoke the real `RecoverInterruption()` API and require success from a currently owned interruption. Recovery changes application lifecycle to paused Continue without advancing or editing authoritative state; normal recorded commands decide any subsequent resume or advance. No pending interruption, wrong index/tick/binding, duplicate or out-of-order rows, an unconsumed row, or an extra recovery after the final command fails the replay with a precise reason. Never skip an invalid row or fabricate a successful recovery.

The recorder obtains the initial lifecycle/reason from the actual application controller. It appends recovery rows from successful actual recovery callbacks, recording the current operation tick and the next replay command index; a row must later bind to that actual next command. It does not derive recoveries or initial lifecycle from `expected_result`, checkpoints, expected score or the desired outcome. The journal stores no poses, gestures, room/camera data, wall time, countdown progress, domain state or private continuation blob. Replay executes the actual application operations without waiting for wall time or a live gesture.

The existing `Run(replay, factory)` overload supplies no journal and performs no unrecorded recovery. Planning starts paused and terminal state starts at outcome. For an active/stabilizing initial Snapshot, it may execute at most two independent actual traces from that same canonical authority: running first, then paused only if running fails. A trial passes only when all actual admission/resolved receipts, required checkpoints and terminal outcome match; expected values are comparisons only and are never injected into state or used to synthesize a lifecycle operation. If neither trace matches, return the actual failures. The journal overload uses only its validated explicit initial state and recovery sequence, with no alternate-state trial. A trace requiring an external recovery absent from its inputs fails explicitly; neither overload invents recovery to reach an expected result.

C07's existing native persistence/replay cases must exercise a real tracking pause, a rejected resume while interrupted, an actual recovery callback and a subsequent accepted resume, with the recorded journal bound to the exact trace. The same trace with a missing journal, wrong binding/index/tick, duplicate or surplus recovery must fail. These assertions close the external-input gap within existing task/check/case definitions; this amendment supplies specification evidence only and makes no Android, device or native qualification claim.

Runtime accepts a replay only when schema, rules, mission version, content hash, layout signature, expanded challenge and rule profile match exactly, and every command kind is supported. Build ID may differ only after an explicit compatibility report proves the same rules/content on that target. No compatibility report is assumed for Android IL2CPP versus Editor. Unsupported old replay remains accessible as metadata, with an incompatibility reason; it is never silently rebased. Source/replay hashes and independent test outcome are evidence, not an arbitrary `passed` string in a file.

Player save retains at most 64 RunSummary records plus all campaign progress/one-time reward receipts. It retains no more than one current snapshot and 10,000 admitted command receipts in that run; max canonical current-run payload is 16 MiB. A size overflow is a diagnosed save error with preserved prior generation, not silent deletion of causal state. Developer replay/evidence files have separate retention and do not enter the player profile.

## 9. Core interfaces and ownership

```csharp
public interface ICommandValidator {
    ValidationResult Validate(SimulationState state, GameCommand command);
}
public interface ISimulation {
    SimulationSnapshot ReadSnapshot();
    AdmissionResult Admit(GameCommand command);
    StepResult Step(); // one operation step; caller must own the clock lease
}
public interface ISimulationClock {
    bool IsPaused { get; }
    ApplicationResult Apply(ApplicationCommand command);
    StepBatchResult Advance(int operationTicks); // only 10 or 50 in player Command UI
}
public interface IForecastService {
    ForecastHandle Begin(ForecastRequest request);
    void Cancel(long requestId);
    ForecastResult Poll(long requestId);
}
public interface IContentCatalog {
    CompiledMission GetMission(string missionVersionId, string contentHash);
    CompiledLayout GetLayout(string layoutVersionId, string graphHash);
}
public interface ISaveStore {
    SaveReadResult ReadValidated();
    SaveWriteHandle WriteGeneration(PlayerSaveSnapshot snapshot);
    SaveWriteResult Poll(SaveWriteHandle handle);
}
public interface IReplayRunner {
    ReplayReport Run(ReplayDocument replay, SimulationFactory factory);
    ReplayReport Run(ReplayDocument replay, SimulationFactory factory,
                     ReplayApplicationJournal applicationJournal);
}
```

`ValidationResult` is immutable `{allowed:bool,reason_code:id,affected_ids:id[]}`. `AdmissionResult` returns the queue/final receipt above. `StepResult` is `{operation_tick,scenario_tick,state_revision,state_sha256,events,terminal:bool}`; `StepBatchResult` is the ordered StepResult array, stopping early on outcome/fault. `ApplicationResult` contains its receipt plus a desired application transition; the application service owns actual scene/anchor work. No validator spends resources, no input adapter writes SimulationState, and no presentation listener decides outcomes.

`ForecastRequest` is `{request_id:positive long,source_state_revision:count,snapshot:Snapshot,preview_command:CommandDto,horizon_scenario_ticks:int(1..600)}`. `ForecastResult` is `{request_id,source_state_revision,status:pending|complete|cancelled|rejected,reason_code,knowledge:exact_given_no_further_orders|conditional_known_events,simulated_scenario_ticks:count,risk_regions:RiskRegion[],routes:RouteForecast[],preview_receipt:CommandResultDto|null}`. RiskRegion contains `{region_id,hazard,maximum_value,first_unsafe_scenario_tick:tick|null}`; RouteForecast contains `{route_id,available:bool,estimated_arrival_operation_tick:tick|null,blocking_reason_code:id|null}`. A completed result is displayable only if source revision still matches. Handles have one lifecycle owner and cancellation on hub/mission exit.

`SimulationFactory` accepts only compiled immutable content, an initial snapshot/seed and an isolated event sink. It does not receive an outcome setter. Bootstrap composes services; Operation owns one simulation/clock, XR owns placement/permissions, UI owns navigation, Presentation consumes snapshots/events, Persistence owns generation promotion. The Editor compiler/build runner cannot share writes with the interactive Editor. These boundaries retain the original eight assemblies and canonical `game/Assets/Resq/Runtime/...` repository layout.

## 10. JSON Schema generation and acceptance

The field tables are a normative schema: generate draft-2020-12 schemas mechanically from them, with `additionalProperties:false`, every field required, `oneOf` for each closed tagged union, integer minima/maxima, ID/hash patterns, and explicit null alternatives. Cross-reference/conservation/temporal checks remain compiled validation; JSON Schema alone cannot establish them. Do not accept a generic `object`/`params:any` where this document names a union.

This example is the exact command-header schema construction pattern; payload alternatives must use the complete closed table in section 3, not this example as a permissive fallback:

```json
{
  "$schema":"https://json-schema.org/draft/2020-12/schema",
  "type":"object",
  "additionalProperties":false,
  "required":["schema_version","command_id","run_id","sequence","requested_tick","actor_input","kind","payload"],
  "properties":{
    "schema_version":{"const":1},
    "command_id":{"type":"string","pattern":"^[A-Za-z0-9_.:@-]{1,128}$"},
    "run_id":{"type":"string","pattern":"^[A-Za-z0-9_.:@-]{1,128}$"},
    "sequence":{"type":"integer","minimum":1,"maximum":10000},
    "requested_tick":{"type":"integer","minimum":0,"maximum":1000000},
    "actor_input":{"enum":["hands_left","hands_right","controller_left","controller_right","head_dwell","reference_replay","system_tutorial"]},
    "kind":{"const":"place_equipment"},
    "payload":{
      "type":"object","additionalProperties":false,
      "required":["equipment_id","socket_id","orientation_step"],
      "properties":{
        "equipment_id":{"type":"string","pattern":"^[A-Za-z0-9_.:@-]{1,128}$"},
        "socket_id":{"type":"string","pattern":"^[A-Za-z0-9_.:@-]{1,128}$"},
        "orientation_step":{"type":"integer","minimum":0,"maximum":3}
      }
    }
  }
}
```

Future required commands are `python3 tools/resq.py game test --suite runtime-commands --report evidence/runtime/runtime-commands.json` and `python3 tools/resq.py game test --suite runtime-persistence --report evidence/runtime/runtime-persistence.json`. The harness/actual C# tests are not supplied by this document.

- RC01: reject every unknown command/predicate/event opcode, unknown field, wrong type, duplicate key, invalid ID/reference, oversized array and noninteger authoritative value; reject authored effects at zero or the deadline and warnings outside their allowed range.
- RC02: queued revalidation, competition for last stock, application-command bypass and exact receipt deduplication; no duplicated spend/equipment.
- RC03: every authored operation/counter/milestone from the 25 frozen missions compiles to the closed typed registry; no mission prose is read by a runtime rule.
- RC04: compile→canonical encode→strict decode preserves all definitions, hashes and array ordering; root/event schema mutations fail.
- RP01: canonical bytes/SHA-256 and xorshift fixtures independently agree in Python and the real C# assembly.
- RP02: uninterrupted versus snapshot→reload continuation gives identical projected state hash and result at every checkpoint, including half-boarded resident, moving return, power trip, pump remainder, Relaxed accumulator, pending command, tick-zero warning emission, cooling/crust timer, objective interval and finite production WIP. Equivalent adapter commands share rule-state hashes while save integrity and pure/mixed comparison partitions retain their distinct provenance.
- RP03: inject truncation, disk-full/write failure, crash before/after every promotion stage, corrupt current, corrupt both, future schema, hash mismatch and duplicate reward; preserve the correct last valid generation and honest status.
- RP04: exact mismatch rejection for rules/content/layout/challenge/preset/replay schema; explicit migration fixtures for every actually supported schema transition.
- RP05: checkpoint rollback marks assistance permanently; normal suspend/resume does not; replaying completion/version migration cannot duplicate campaign reward IDs.
- RP06: clean mission exit cancels loads/forecasts and releases runtime ownership; repeated W1→W5→hub cycles preserve only profile/progression and return memory near the qualified baseline.
- RP07: every Settings field round-trips through save/restart at its default and allowed extrema; text/caption/opacity/target/highlight enums and dwell/audio steps reject off-grid values. Settings changes do not alter rule-state hashes.

The outstanding implementation gates are authored graph/terrain bundles, the generated strict schemas/readers, actual pure C# algorithms, the shared command harness, tested Android atomic save behavior, and platform parity evidence. Their owner is the corresponding foundation/content/runtime/QA task in the execution graph. They are implementation/proof work, not unresolved product-design choices or permission to claim the game exists.


Controlled amendment 13 — objective causal summaries and boundary observations:

- Public objective metric IDs are `objective:metric:` plus SHA-256 of canonical `{mission_version_id,query}`, where query is the exact closed metric parameters excluding `minimum`/`maximum`. Subject is its declared group, edge tag, equipment, node or region filter (null for no filter/all groups). Distinct-person metrics retain sorted unique compiled resident IDs; count equals array length. Multiple thresholds share their one exact producer.
- When station shutdowns are queried, each compiled infrastructure row of kind `station` has one internal `objective_station_activation` MetricState. ID is `objective:station:` plus SHA-256 of canonical `{mission_version_id,infrastructure_id}`; subject is that infrastructure ID, distinct IDs empty, value 0=never operational, 1=currently operational, 2=previously operational and currently off. Initial operational samples set 1 without a shutdown; only 1→2 increments shutdowns. Restore binds current operational truth to this state.
- If either dock metric is queried, register two internal rows per actual saved vehicle ID × bound node. Bound nodes are layout nodes with vehicle_slots>0 union explicit max-dock-query nodes. IDs are `objective:dockwait:` / `objective:dockpresence:` plus SHA-256 of canonical `{mission_version_id,vehicle_id,node_id}`. Metrics are `objective_dock_wait` / `objective_dock_presence_wait`, subject vehicle ID, distinct IDs empty. Value 0 means inactive; positive value is 1+elapsed continuous operation ticks, bounded by O+1. The first observed waiting boundary has elapsed zero. Continued matching waits advance once per next O. A departing/changed wait closes on that boundary; its final elapsed interval contributes to the public maximum before its row resets. Register zero rows for the exact current fleet; retain failed vehicle rows.
- The chosen unload target is the first remaining dropoff/evacuate stop and must be the actual pending stop for a dock-wait observation. A safe holding node is the actual safe parked vehicle node equal to that target or a direct predecessor connected by an edge with the vehicle's traversal class and sufficient movement capacity. The actual queue must match target and route sequence. Exclude travel, failed/resting vehicles, passenger work, far approach and pickup-only waits. Berth/capacity waits qualify for both rows. An edge-only wait qualifies for the presence row only when that chosen unload dock is closed. Closed-loaded cumulative ticks require a prior active presence window and a current actual loaded/closed wait; a new arrival/first observed wait gets no retrospective tick.
- Completed forward-edge passage facts carry the actual resident and edge IDs, including actual vehicle occupants. A partial return to origin is not a completed edge. Completed node-dwell facts carry resident/node and S minus node-entered S on actual departure from waiting/boarding; travel contributes no node dwell. These are immutable transient observations from actual movement/derivation, consumed into the durable metrics before publishing the complete boundary. Clear each new step; fork independently; a restored cursor cannot fabricate old interval facts. No new event kind, private history, or Snapshot field is introduced.
- For `always`, ObjectiveMonitor.violated is a permanent false-sample latch. For `hold_for`, it records whether the last in-window sample was false; a false sample resets the suffix and its first subsequent true pulse does not claim the preceding false interval. Terminal/epoch resampling never increments a pulse twice. Restoration validates exact compiled metric/monitor ownership, actual milestone clocks, O/S bounds, current station/dock coherence and policy-specific fulfillment/suffix invariants. Other root/content/run/save integrity remains C07's responsibility.

These internal causal-summary metrics are not new authorable public counter kinds. Debrief may use durable monitors/metrics/milestones and the actual bounded recent diagnostic events; it must not claim that the capped diagnostics contain the complete history. Pumped cumulative quantities use checked signed64 arithmetic and their cumulative domain bounds, not the per-node volume ceiling.
