using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Persistence;
using Resq.Simulation;
using Resq.Simulation.Objectives;
using Resq.Simulation.Rescue;
using Resq.Simulation.Tools;
using UnityEngine;

namespace Resq.Tests.Persistence
{
    // Explicit synthetic C07 input. This composes the actual C02-C05 owners;
    // it is neither authored campaign content nor a substitute hazard backend.
    public static class PersistenceFixture
    {
        private const string RunId = "persistence-fixture";
        private const string SourceBuild = "synthetic-persistence";
        private const string MissionId = "fixture-v1";
        private const string LayoutId = "fixture-wide";

        public sealed class Rig
        {
            public CompiledContent Content { get; }
            public SimulationFactory Factory { get; }
            public OperationClock Clock { get; set; }
            internal Rig(CompiledContent content, bool relaxed)
            {
                Content = content;
                Factory = new SimulationFactory(content, SourceBuild, RestoreDomain);
                var domain = new Domain(content, MissionId, LayoutId);
                var identity = O("run_id", RunId, "source_build_id", SourceBuild,
                    "mission_version_id", MissionId, "rules_version", "resq-rules-1",
                    "content_hash", content.ContentHash, "layout_version_id", LayoutId,
                    "layout_signature", content.LayoutSignature(LayoutId), "rule_profile_id", relaxed ? "relaxed" : "standard",
                    "input_adapter_id", "reference_replay", "guidance_policy", "unguided", "challenge_id", "base",
                    "modifiers_hash", A().Sha256(), "seed", 7, "assisted", relaxed);
                Clock = new OperationClock(content, MissionId, LayoutId, identity,
                    new ObjectiveOperationSystems(domain, new ObjectiveSystem(domain.Mission, domain.Layout)));
            }
            public GameCommand Command(string kind, JsonValue payload, long? requestedTick = null)
            {
                long sequence = Clock.NextSequence;
                bool application = kind == "pause" || kind == "resume" || kind == "advance_ticks" ||
                    kind == "enter_placement" || kind == "confirm_placement" || kind == "exit_to_hub";
                return GameCommand.Read(O("schema_version", 1, "command_id", "cmd:" + sequence.ToString(CultureInfo.InvariantCulture),
                    "run_id", RunId, "sequence", sequence, "requested_tick", requestedTick ?? (Clock.OperationTick + (application ? 0 : 1)),
                    "actor_input", "reference_replay", "kind", kind, "payload", payload));
            }
            public void StartVehicleRoute()
            {
                var resumed = Clock.Apply(Command("resume", O()));
                if (!resumed.Receipt.Accepted) throw new InvalidOperationException("Fixture resume rejected: " + resumed.Receipt.ReasonCode);
                Clock.CompleteResumeCountdown();
                var route = Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] {
                    O("node_id", "start", "action", "pickup", "group_id", "group"),
                    O("node_id", "refuge", "action", "evacuate", "group_id", null) })));
                var queued = Clock.Admit(route);
                if (queued.Status != "queued") throw new InvalidOperationException("Fixture route rejected: " + queued.ReasonCode);
            }
        }

        public static Rig Build(bool relaxed = false)
        {
            string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            var source = StrictJson.Parse(File.ReadAllBytes(Path.Combine(root, "content/runtime/fixtures/compiler-valid.json")));
            var mission = source["missions"].Items.Single().With("duration_scenario_ticks", JsonValue.Number(140))
                .With("rule_profile_ids", A("standard", "relaxed"))
                .With("events", JsonValue.Array(source["missions"].Items.Single()["events"].Items.Select(e =>
                    e.With("at_scenario_tick", JsonValue.Number(120)).With("warning_at_scenario_tick", JsonValue.Number(20)).With("known_at_start", JsonValue.Bool(false)))))
                .With("milestones", JsonValue.Array(new[] {
                    O("id", "group.boarded", "kind", "group_boarded", "target", "group"),
                    O("id", "final.started", "kind", "final_unload_started", "target", "all_residents"),
                    O("id", "all.safe", "kind", "all_evacuated", "target", "all_residents") }));
            var standard = source["rule_profiles"].Items.Single();
            var relaxedProfile = standard.With("id", JsonValue.String("relaxed")).With("scenario_numerator", JsonValue.Number(1));
            var layouts = source["layouts"].Items.Select(layout => {
                var changed = layout.With("nav_nodes", JsonValue.Array(layout["nav_nodes"].Items.Select(n => n["id"].Text == "start" ? n.With("kind", JsonValue.String("dock")) : n)))
                    .With("fixed_vehicles", JsonValue.Array(new[] { O("vehicle_id", "bus", "kind", "fixed_bus", "node_id", "start", "seats", 4,
                        "movement_slots", 2, "initial_route_id", null, "initial_occupant_ids", A()) }));
                return changed.With("graph_hash", JsonValue.String(ContentCompilation.ComputeGraphHash(changed)));
            });
            var payload = source.With("missions", JsonValue.Array(new[] { mission }))
                .With("rule_profiles", JsonValue.Array(new[] { standard, relaxedProfile })).With("layouts", JsonValue.Array(layouts));
            return new Rig(ContentCompilation.Compile(payload), relaxed);
        }

        public static JsonValue O(params object[] pairs)
        {
            if (pairs.Length % 2 != 0) throw new ArgumentException("Object key/value pairs required");
            var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                object value = pairs[i + 1];
                result.Add((string)pairs[i], value == null ? JsonValue.Null() : value is JsonValue json ? json :
                    value is string text ? JsonValue.String(text) : value is bool boolean ? JsonValue.Bool(boolean) : JsonValue.Number(Convert.ToInt64(value, CultureInfo.InvariantCulture)));
            }
            return JsonValue.Object(result);
        }
        public static JsonValue A(params string[] values) => JsonValue.Array(values.Select(JsonValue.String));

        private static IOperationSystems RestoreDomain(CompiledContent content, JsonValue snapshot)
        {
            var identity = snapshot["identity"];
            if (identity["run_id"].Text != RunId || identity["guidance_policy"].Text != "unguided") throw new InvalidOperationException("Synthetic persistence fixture identity required");
            var domain = new Domain(content, identity["mission_version_id"].Text, identity["layout_version_id"].Text);
            long operation = snapshot["operation_tick"].Integer, scenario = snapshot["scenario_tick"].Integer;
            domain.Restore(snapshot);
            var state = domain.ReadDomainState();
            var facts = domain.ObserveObjectives(new OperationContext(operation, scenario, false), state);
            var metrics = JsonValue.Array(snapshot["metrics"].Items.Where(m => m["id"].Text.StartsWith("objective:", StringComparison.Ordinal)));
            bool terminal = snapshot["phase"].Text == "outcome";
            var objectives = new ObjectiveSystem(domain.Mission, domain.Layout).Restore(metrics, snapshot["objective_monitors"],
                operation, scenario, state, facts, snapshot["milestones"].Items, terminal);
            if (terminal && (snapshot["outcome"].IsNull || objectives.Outcome(state).ToJson(snapshot["outcome"]["reason"].Text).Canonical() != snapshot["outcome"].Canonical()))
                throw new InvalidOperationException("Saved terminal result disagrees with actual restored objectives and residents");
            var wrapped = new ObjectiveOperationSystems(domain, objectives);
            wrapped.RestoreEngineMilestones(snapshot["milestones"].Items, operation, scenario);
            return wrapped;
        }

        private sealed class Domain : IOperationSystems, IObjectiveDomainObserver
        {
            private readonly CompiledContent content;
            public CompiledMission Mission { get; }
            public LayoutDef Layout { get; }
            private EquipmentSystem equipment;
            // Availability of observations produced during this actual interval.
            // It carries no history and is deliberately absent after restore.
            private long freshFactsOperation = -1;
            private readonly SortedDictionary<string, bool> edgeEnabled;
            private readonly SortedDictionary<string, long> edgeMultipliers;
            private readonly Dictionary<string, long> seaHeads = new Dictionary<string, long>(StringComparer.Ordinal);
            private readonly Dictionary<string, bool> gates = new Dictionary<string, bool>(StringComparer.Ordinal);

            public Domain(CompiledContent content, string missionId, string layoutId)
            {
                this.content = content;
                Mission = content.Envelope.payload.missions.Single(m => m.mission_version_id == missionId);
                Layout = content.Envelope.payload.layouts.Single(l => l.id == layoutId && l.mission_version_id == missionId);
                RequireSupportedSyntheticContent();
                edgeEnabled = new SortedDictionary<string, bool>(Layout.nav_edges.ToDictionary(e => e.id, e => e.administrative_enabled), StringComparer.Ordinal);
                edgeMultipliers = new SortedDictionary<string, long>(Layout.nav_edges.ToDictionary(e => e.id, e => e.current_multiplier_permille), StringComparer.Ordinal);
                var transport = new TransportSystem(content, missionId, layoutId);
                transport.RestoreBoundaryCursor(0);
                equipment = new EquipmentSystem(Mission, Layout, content.Envelope.payload.equipment, new WaterSystem(Layout), transport);
            }
            private Domain(Domain other)
            {
                content = other.content; Mission = other.Mission; Layout = other.Layout;
                equipment = other.equipment.Fork(); freshFactsOperation = other.freshFactsOperation;
                edgeEnabled = new SortedDictionary<string, bool>(other.edgeEnabled, StringComparer.Ordinal);
                edgeMultipliers = new SortedDictionary<string, long>(other.edgeMultipliers, StringComparer.Ordinal);
            }
            public IOperationSystems Fork() => new Domain(this);

            private void RequireSupportedSyntheticContent()
            {
                // These families really are absent from this deliberately small
                // compiled fixture. Reject expansion instead of supplying safety
                // facts for a hazard/power/mechanics owner that does not exist.
                if (Mission.mission_version_id != MissionId || Mission.tutorial_policy != "none" ||
                    Layout.fire_cells.Count != 0 || Layout.fire_links.Count != 0 || Layout.supports.Count != 0 || Layout.runouts.Count != 0 ||
                    Layout.tides.Count != 0 || Layout.lava_segments.Count != 0 || Layout.power_nodes.Count != 0 || Layout.power_links.Count != 0 ||
                    Layout.infrastructure.Count != 0 || Layout.resources.Count != 0 || Layout.producers.Count != 0 || Layout.hydraulic_links.Count != 0 ||
                    content.Envelope.payload.equipment.Any(e => e.id != "beacon") ||
                    Layout.nav_nodes.Any(n => n.hazard_region_ids.Count != 0 || n.power_consumer_id != null) ||
                    Layout.nav_edges.Any(e => e.hazard_region_ids.Count != 0 || e.required_power_consumer_id != null || e.required_equipment_socket_id != null) ||
                    Layout.sockets.Any(s => s.power_node_id != null || s.support_id != null || s.lava_segment_id != null || s.fire_cell_ids.Count != 0 ||
                        s.resource_store_id != null || s.hydraulic_link_id != null || s.footprint_cell_ids.Any(id => !Layout.water_nodes.Any(w => w.id == id))) ||
                    content.LayoutOperations[Layout.id].Any(op => op.Kind != "set_source_rate" || op.TargetKind != "water_source"))
                    throw new InvalidOperationException("Persistence fixture requires its explicit absent hazard families and actual water-only scheduled operations");
            }

            public void Restore(JsonValue snapshot)
            {
                foreach (string field in new[] { "fire", "supports", "runouts", "tides", "lava" })
                    if (snapshot[field].Items.Count != 0) throw new InvalidOperationException("Unavailable fixture state owner: " + field);
                var events = snapshot["event_state"];
                foreach (string field in new[] { "dynamic_events", "wind_region_codes", "cooler_socket_enabled", "shelter_region_enabled", "resource_warning_thresholds", "gate_open" })
                    if (events[field].Items.Count != 0) throw new InvalidOperationException("Unavailable fixture event owner: " + field);
                long operation = snapshot["operation_tick"].Integer;
                equipment = SnapshotOwners.RestoreEquipment(content, snapshot, owner => PowerFrame(owner, operation));
                freshFactsOperation = -1;
                // No event in this fixture mutates these maps. Canonical readback
                // also checks every owned field, including unguided state.
                var expected = ReadDomainState();
                foreach (string field in new[] { "administrative_edge_enabled", "edge_current_multipliers", "group_held" })
                    if (events[field].Canonical() != expected["event_state"][field].Canonical()) throw new InvalidOperationException("Fixture event map disagrees with compiled/current authority: " + field);
                if (snapshot["guidance_state"].Canonical() != expected["guidance_state"].Canonical()) throw new InvalidOperationException("Unsupported fixture guidance continuation");
                var fired = new HashSet<string>(events["fired_event_ids"].Items.Select(x => x.Text), StringComparer.Ordinal);
                var rates = equipment.Water.ReadRateRemainders().Items.Where(r => r["kind"].Text == "source").ToDictionary(r => r["id"].Text, r => r["current_rate_mm3_per_second"].Integer, StringComparer.Ordinal);
                foreach (var source in Layout.sources)
                {
                    long rate = source.initial_rate_mm3_per_second;
                    foreach (var op in content.LayoutOperations[Layout.id].Where(op => fired.Contains(op.EventId)).OrderBy(op => op.ScenarioTick).ThenBy(op => op.EventId, StringComparer.Ordinal))
                        if (op.TargetIds.Contains(source.id)) rate = op.Value;
                    if (rates["source:" + source.id] != rate) throw new InvalidOperationException("Saved source rate disagrees with actual fired scheduled operations");
                }
                ValidateBoundary(new OperationContext(operation, snapshot["scenario_tick"].Integer, false));
            }

            public JsonValue ReadDomainState()
            {
                var state = new Dictionary<string, JsonValue>(equipment.ReadState().Members, StringComparer.Ordinal);
                foreach (var pair in equipment.Transport.ReadState().Members) state.Add(pair.Key, pair.Value);
                state.Add("guidance_state", O("policy", "unguided", "step_id", "none", "completed_step_ids", A(), "acknowledged_warning", false));
                state.Add("water", equipment.Water.ReadWater()); state.Add("rate_remainders", equipment.Water.ReadRateRemainders());
                state.Add("water_ledger", equipment.Water.ReadLedger());
                foreach (string field in new[] { "fire", "supports", "runouts", "tides", "lava", "objective_monitors", "recent_events" }) state.Add(field, A());
                state.Add("event_state", O("next_static_event_index", 0, "fired_event_ids", A(), "revealed_event_ids", A(), "warned_event_ids", A(),
                    "dynamic_events", A(), "administrative_edge_enabled", JsonValue.Array(edgeEnabled.Select(e => O("id", e.Key, "value", e.Value))),
                    "edge_current_multipliers", JsonValue.Array(edgeMultipliers.Select(e => O("id", e.Key, "value", e.Value))),
                    "gate_open", A(), "group_held", equipment.Transport.GroupHeld, "wind_region_codes", A(), "cooler_socket_enabled", A(),
                    "shelter_region_enabled", A(), "resource_warning_thresholds", A()));
                state.Add("milestones", equipment.Transport.Milestones); state.Add("metrics", equipment.Transport.HistoricalMetrics);
                return JsonValue.Object(state);
            }
            public ValidationResult Validate(GameCommand command, OperationContext context)
            {
                if (command.Payload is AssignRoute route) return equipment.Transport.ValidateRoute(route);
                return equipment.Preview(command, EquipmentFrame(context.OperationTick), "active", PowerFrame(equipment, context.OperationTick));
            }
            public void Apply(GameCommand command, OperationContext context)
            {
                if (command.Payload is AssignRoute route) equipment.Transport.Assign(route, context.OperationTick, command.Sequence);
                else equipment = equipment.Apply(command, EquipmentFrame(context.OperationTick), "active", context, PowerFrame(equipment, context.OperationTick));
            }
            public void EvaluateTides(OperationContext context)
            {
                if (Layout.tides.Count != 0) throw new InvalidOperationException("Actual tide owner required");
            }
            public void PublishWarning(EventDef definition, OperationContext context)
            {
                if (context.Events == null) throw new InvalidOperationException("Actual warning event sink required");
                var affected = content.LayoutOperations[Layout.id].Where(op => op.EventId == definition.id)
                    .SelectMany(op => op.TargetIds).Select(id => Layout.sources.Single(s => s.id == id).water_node_id)
                    .Select(id => Layout.water_nodes.Single(w => w.id == id).region_id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
                foreach (string region in affected) context.Events.Emit("HazardWarning", region, null, definition.id,
                    O("hazard", "water", "region_id", region, "effect_scenario_tick", definition.at_scenario_tick));
            }
            public void ApplyScheduled(ResolvedEventOperation operation, OperationContext context)
            {
                if (operation.Kind != "set_source_rate" || operation.TargetKind != "water_source") throw new InvalidOperationException("Unsupported fixture scheduled owner");
                foreach (string id in operation.TargetIds) equipment.Water.SetSourceRate(id, operation.Value);
            }
            public void ResolvePowerAndReservations(OperationContext context)
            { equipment = equipment.ResolvePower(PowerFrame(equipment, context.OperationTick), context, out _); }
            public void SimulateHazardInterval(OperationContext context)
            {
                equipment = equipment.SimulateWater(context, seaHeads, gates, out _);
                equipment = equipment.ProduceCoolant(context, new Dictionary<string, bool>(StringComparer.Ordinal));
            }
            public void RecomputeSafetyAndDamage(OperationContext context)
            {
                equipment = equipment.HazardDamage(context, EquipmentFrame(context.OperationTick), out _);
                equipment = equipment.ResolvePower(PowerFrame(equipment, context.OperationTick), context, out _);
            }
            public void ResolveMovementAndTransfers(OperationContext context)
            { equipment.Transport.ResolveMovement(context, TransportFrame(context.OperationTick)); }
            public void DeriveMetricsAndObjectives(OperationContext context)
            { equipment.Transport.Derive(context, TransportFrame(context.OperationTick)); freshFactsOperation = context.OperationTick; }
            public void SampleInitialObjectives(OperationContext context) => equipment.Transport.SampleInitialMilestones(context);
            public void EmitMilestone(string milestoneId, OperationContext context) => throw new InvalidOperationException("Objective adapter owns engine milestone notifications");
            public bool GuidedEpochReady => false; // Compiled and checked tutorial_policy:none.
            public bool RequiredConditionsHold => throw new InvalidOperationException("Actual objective adapter required");
            public OutcomeResult EvaluateOutcome(OperationContext context) => throw new InvalidOperationException("Actual objective adapter required");
            public void ValidateBoundary(OperationContext context)
            {
                equipment.ValidateBoundary(); equipment.Water.ValidateBoundary(); equipment.Transport.ValidateBoundary();
                equipment.Transport.InspectDockWaitFacts(context, TransportFrame(context.OperationTick));
            }

            private PowerFrame PowerFrame(EquipmentSystem owner, long operation) => new PowerFrame(operation,
                new PowerNodeSample[0], new PowerLinkSample[0],
                owner.Equipment.Select(e => new PowerEquipment(e.Id, e.EquipmentId, e.SocketId, e.Enabled, e.Intact)), new PowerConsumerSample[0]);
            private long RegionDepth(string region)
            {
                var nodes = Layout.water_nodes.Where(w => w.region_id == region).ToArray();
                if (nodes.Length == 0) throw new InvalidOperationException("Actual water region required: " + region);
                return nodes.Max(w => equipment.Water.Depth(w.id));
            }
            private long BoundDepth(IEnumerable<string> regions) => regions.Select(RegionDepth).DefaultIfEmpty(0).Max();
            private TransportFrame TransportFrame(long operation)
            {
                var nodeSamples = Layout.nav_nodes.Select(n => {
                    long depth = BoundDepth(n.hazard_region_ids); bool safe = depth <= n.safe_depth_mm;
                    return new TransportNodeSample(n.id, safe, safe && (n.kind != "dock" || depth >= n.dock_min_depth_mm && depth <= n.dock_max_depth_mm), n.power_consumer_id == null);
                });
                var edgeSamples = Layout.nav_edges.Select(e => {
                    long depth = BoundDepth(e.hazard_region_ids);
                    return new TransportEdgeSample(e.id, edgeEnabled[e.id] && depth >= e.minimum_water_depth_mm && depth <= e.maximum_water_depth_mm,
                        e.required_power_consumer_id == null, edgeMultipliers[e.id], null);
                });
                // Every possible resident node/edge has an explicitly empty
                // hazard binding, checked above. Water in wet remains real and
                // observable to the exposure objective, outside those locations.
                var exposure = Layout.spawns.ToDictionary(s => s.resident_id, s => false, StringComparer.Ordinal);
                return new TransportFrame(operation, nodeSamples, edgeSamples, exposure);
            }
            private EquipmentFrame EquipmentFrame(long operation)
            {
                var transport = equipment.Transport.ReadState();
                var samples = Layout.sockets.Select(socket => {
                    long depth = socket.footprint_cell_ids.Select(equipment.Water.Depth).DefaultIfEmpty(0).Max();
                    long occupants = transport["residents"].Items.Count(p => p["location"]["kind"].Text == "node" && p["location"]["node_id"].Text == socket.node_id);
                    long reservations = transport["reservations"].Items.Where(r => r["resource_kind"].Text == "node" && r["resource_id"].Text == socket.node_id).Sum(r => r["quantity"].Integer);
                    bool work = transport["transfers"].Items.Any(t => t["node_id"].Text == socket.node_id);
                    bool dependent = transport["routes"].Items.Any(r => r["stops"].Items.Skip((int)r["next_stop_index"].Integer).Any(s => s["node_id"].Text == socket.node_id));
                    // All nonwater damage families and all relevant source
                    // changes at these dry footprints are explicitly absent.
                    bool sourceBound = Layout.sources.Any(s => socket.footprint_cell_ids.Contains(s.water_node_id));
                    if (sourceBound) throw new InvalidOperationException("Fixture socket warning integration would be required");
                    return new EquipmentSocketSample(socket.id, depth, 0, depth <= 200, !work, occupants, reservations,
                        work, dependent, false, false, false, false, depth > 200);
                });
                return new EquipmentFrame(operation, samples, seaHeads);
            }

            public ObjectiveObservations ObserveObjectives(OperationContext context, JsonValue domainState)
            {
                var frame = TransportFrame(context.OperationTick);
                var routes = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var route in Layout.routes)
                {
                    string from = route.from_node_id; bool available = true;
                    foreach (string to in route.required_waypoint_ids.Concat(new[] { route.to_node_id }))
                    {
                        if (from != to && equipment.Transport.FindPath(from, to, route.traversal_class, route.mobility_class, 1, frame).Count == 0) available = false;
                        from = to;
                    }
                    routes.Add(ObjectiveObservations.RouteKey(route.id, route.traversal_class), available);
                }
                var exposure = Layout.water_nodes.Select(w => w.region_id).Distinct(StringComparer.Ordinal)
                    .ToDictionary(region => ObjectiveObservations.ExposureKey(region, "water"), RegionDepth, StringComparer.Ordinal);
                bool fresh = freshFactsOperation == context.OperationTick;
                IEnumerable<CompletedEdgePassage> passages = fresh ? (IEnumerable<CompletedEdgePassage>)equipment.Transport.ReadBoundaryFacts(context.OperationTick) : new CompletedEdgePassage[0];
                IEnumerable<CompletedNodeDwell> dwells = fresh ? (IEnumerable<CompletedNodeDwell>)equipment.Transport.ReadCompletedNodeDwells(context.OperationTick) : new CompletedNodeDwell[0];
                var peaks = fresh ? equipment.Transport.ReadNodeOccupancyPeaks(context.OperationTick).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) :
                    Layout.nav_nodes.ToDictionary(n => n.id, n => (long)domainState["residents"].Items.Count(p => p["location"]["kind"].Text == "node" && p["location"]["node_id"].Text == n.id), StringComparer.Ordinal);
                return new ObjectiveObservations(context.OperationTick, new Dictionary<string, bool>(StringComparer.Ordinal), routes, exposure,
                    passages, equipment.Transport.InspectDockWaitFacts(context, frame), dwells, peaks);
            }
        }
    }
}
