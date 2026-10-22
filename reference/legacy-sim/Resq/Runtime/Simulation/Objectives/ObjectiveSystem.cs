using System;
using System.Collections.Generic;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Progression;

namespace Resq.Simulation.Objectives
{
    public sealed class ObjectiveSystem
    {
        private readonly CompiledMission mission;
        private readonly LayoutDef layout;
        private SortedDictionary<string, JsonValue> monitors = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
        private SortedDictionary<string, JsonValue> metrics = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, JsonValue> queries = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
        // Cursors derive from the containing Snapshot boundary on restore.
        private long sampledOperationTick = -1;
        public ObjectiveSystem(CompiledMission mission, LayoutDef layout) : this(mission, layout, true)
        { InitializeWork().Drain(); }
        private ObjectiveSystem(CompiledMission mission, LayoutDef layout, bool deferred)
        {
            this.mission = mission ?? throw new ArgumentNullException(nameof(mission)); this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }
        public static ResumableWork<ObjectiveSystem> CreateWork(CompiledMission mission, LayoutDef layout)
            => new ResumableWork<ObjectiveSystem>(done => CreateRoutine(mission, layout, done));
        private static IEnumerable<string> CreateRoutine(CompiledMission mission, LayoutDef layout, Action<ObjectiveSystem> done)
        {
            var next = new ObjectiveSystem(mission, layout, true);
            using (var work = next.InitializeWork()) { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork() => new ResumableWork<bool>(InitializeRoutine);
        private IEnumerable<string> InitializeRoutine(Action<bool> done)
        {
            if (layout.mission_version_id != mission.mission_version_id) throw new InvalidOperationException("Objective mission binding");
            bool critical = false, required = false;
            foreach (var objective in mission.objectives)
            { critical |= objective.tier == "critical"; required |= objective.tier == "required"; yield return "objective.create.tier"; }
            if (!critical || !required) throw new InvalidOperationException("Objective mission binding");
            foreach (var objective in mission.objectives)
            {
                monitors.Add(objective.id, O("id", objective.id, "started_operation_tick", null, "ended_operation_tick", null, "first_true_operation_tick", null, "first_true_scenario_tick", null, "violated", false, "current_hold_scenario_ticks", 0, "fulfilled", false));
                yield return "objective.create.monitor";
                foreach (var unit in RegisterQueriesSteps(objective.ToJson())) yield return unit;
            }
            bool stationShutdowns = false;
            foreach (var pair in queries)
            {
                metrics.Add(pair.Key, MetricRow(pair.Key, pair.Value, 0, Array.Empty<string>()));
                stationShutdowns |= pair.Value["metric"].Text == "station_shutdowns";
                yield return "objective.create.metric";
            }
            if (stationShutdowns)
                foreach (var station in layout.infrastructure)
                {
                    if (IsStation(station))
                    { string id = StationId(station.id); metrics.Add(id, O("id", id, "metric", "objective_station_activation", "subject_id", station.id, "value", 0, "distinct_resident_ids", A())); }
                    yield return "objective.create.station";
                }
            done(true);
        }
        private ObjectiveSystem(ObjectiveSystem old)
        { mission = old.mission; layout = old.layout; sampledOperationTick = old.sampledOperationTick;  }
        public ObjectiveSystem Fork() => ForkWork().Drain();
        public ResumableWork<ObjectiveSystem> ForkWork() => new ResumableWork<ObjectiveSystem>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<ObjectiveSystem> done)
        {
            var next = new ObjectiveSystem(this);
            foreach (var row in monitors) { next.monitors.Add(row.Key, row.Value); yield return "objective.fork.monitor"; }
            foreach (var row in metrics) { next.metrics.Add(row.Key, row.Value); yield return "objective.fork.metric"; }
            foreach (var row in queries) { next.queries.Add(row.Key, row.Value); yield return "objective.fork.query"; }
            done(next);
        }
        public JsonValue Monitors => MonitorsWork().Drain();
        public ResumableWork<JsonValue> MonitorsWork() => DomainWork.Rows(monitors.Values, row => row, "objective.read.monitor");
        public JsonValue Metrics => MetricsWork().Drain();
        public ResumableWork<JsonValue> MetricsWork() => DomainWork.Rows(metrics.Values, row => row, "objective.read.metric");
        public static string MetricId(string missionVersionId, JsonValue parameters) => "objective:metric:" + O("mission_version_id", missionVersionId, "query", parameters.Without("minimum", "maximum")).Sha256();
        private string StationId(string station) => "objective:station:" + O("mission_version_id", mission.mission_version_id, "infrastructure_id", station).Sha256();
        private static bool IsStation(InfrastructureDef x) => x.kind == "station";
        private IEnumerable<string> RegisterQueriesSteps(JsonValue condition)
        {
            string kind = condition["kind"].Text; var p = condition["params"];
            yield return "objective.create.query";
            if (kind == "all" || kind == "any") { foreach (var child in p["conditions"].Items) foreach (var unit in RegisterQueriesSteps(child)) yield return unit; yield break; }
            if (kind != "counter_at_least" && kind != "counter_at_most") yield break;
            string id = MetricId(mission.mission_version_id, p); if (!queries.ContainsKey(id)) queries.Add(id, p.Without("minimum", "maximum"));
            yield return "objective.create.query_id";
        }
        private static string Subject(JsonValue query)
        { foreach (string key in new[] { "group_id", "edge_tag", "equipment_id", "node_id", "region_id" }) if (query.Has(key)) return query[key].IsNull ? null : query[key].Text; return null; }
        private static JsonValue MetricRow(string id, JsonValue query, long value, IEnumerable<string> residents) => O("id", id, "metric", query["metric"].Text, "subject_id", Subject(query), "value", value, "distinct_resident_ids", JsonValue.Array(residents.OrderBy(x => x, StringComparer.Ordinal).Select(JsonValue.String)));
        public void Sample(JsonValue state, ObjectiveObservations facts, IEnumerable<JsonValue> milestones, OperationContext context, bool terminal = false, bool resample = false) => SampleWork(state, facts, milestones, context, terminal, resample).Drain();
        public ResumableWork<bool> SampleWork(JsonValue state, ObjectiveObservations facts, IEnumerable<JsonValue> milestones, OperationContext context, bool terminal = false, bool resample = false) => new ResumableWork<bool>(done => SampleRoutine(state, facts, milestones, context, terminal, resample, done));
        private IEnumerable<string> SampleRoutine(JsonValue state, ObjectiveObservations facts, IEnumerable<JsonValue> milestones, OperationContext context, bool terminal, bool resample, Action<bool> done)
        {
            if (context.Events == null || facts == null || facts.OperationTick != context.OperationTick) throw new InvalidOperationException("Actual boundary/event/observation required");
            new Tick(context.OperationTick); new Tick(context.ScenarioTick);
            if (!terminal && !resample && context.OperationTick != sampledOperationTick + 1 || (terminal || resample) && context.OperationTick != sampledOperationTick) throw new InvalidOperationException("Objective sampling boundary/order");
            var register = milestones.ToDictionary(x => x["id"].Text, StringComparer.Ordinal);
            if (!terminal && !resample) { using (var metricsWork = UpdateMetricsWork(state, facts, context)) { while (metricsWork.MoveNext()) yield return metricsWork.Stage; } sampledOperationTick = context.OperationTick; }
            foreach (var def in mission.objectives.OrderBy(x => x.id, StringComparer.Ordinal))
            {yield return "objective.sample"; 
                var m = monitors[def.id]; var temporal = def.temporal.ToJson(); string kind = temporal["kind"].Text;
                bool truth; using (var worktruth = EvaluateWork(def.kind, def.@params.ToJson(), state, facts, context.ScenarioTick)) { while (worktruth.MoveNext()) yield return worktruth.Stage; truth = worktruth.Result; }
                bool old = m["fulfilled"].Boolean;
                JsonValue start = temporal.Has("start_event") && register.TryGetValue(temporal["start_event"].Text, out var st) ? st : JsonValue.Null();
                JsonValue end = temporal.Has("end_event") && register.TryGetValue(temporal["end_event"].Text, out var en) ? en : JsonValue.Null();
                if (!start.IsNull) m = m.With("started_operation_tick", start["operation_tick"]);
                if (!end.IsNull) m = m.With("ended_operation_tick", end["operation_tick"]);
                if (kind == "at_end") { if (terminal) m = m.With("fulfilled", JsonValue.Bool(truth)); }
                else if (kind == "ever_by")
                { if (truth && context.ScenarioTick <= temporal["deadline_tick"].Integer && m["first_true_operation_tick"].IsNull) m = FirstTrue(m, context); m = m.With("fulfilled", JsonValue.Bool(!m["first_true_operation_tick"].IsNull)); }
                else if (kind == "ever_before")
                {
                    if (truth && m["first_true_operation_tick"].IsNull && (end.IsNull || context.OperationTick < end["operation_tick"].Integer)) m = FirstTrue(m, context);
                    m = m.With("fulfilled", JsonValue.Bool(!m["first_true_operation_tick"].IsNull && (end.IsNull || m["first_true_operation_tick"].Integer < end["operation_tick"].Integer)));
                }
                else if (kind == "always" || kind == "hold_for")
                {
                    bool inside = !start.IsNull && context.OperationTick >= start["operation_tick"].Integer && (end.IsNull || context.OperationTick <= end["operation_tick"].Integer);
                    // Terminal is a second evaluation at the same boundary; never
                    // count its pulse twice or erase an earlier same-boundary failure.
                    if (inside)
                    {
                        if (kind == "always") { if (!truth) m = m.With("violated", JsonValue.Bool(true)); else if (m["first_true_operation_tick"].IsNull) m = FirstTrue(m, context); }
                        else
                        {
                            bool previousFalse = m["violated"].Boolean;
                            long held = truth && !previousFalse && !terminal && !resample && context.HazardPulse && context.OperationTick > start["operation_tick"].Integer ? checked(m["current_hold_scenario_ticks"].Integer + 1) : truth ? m["current_hold_scenario_ticks"].Integer : 0;
                            m = m.With("current_hold_scenario_ticks", JsonValue.Number(held)).With("violated", JsonValue.Bool(!truth));
                            if (truth && m["first_true_operation_tick"].IsNull) m = FirstTrue(m, context);
                        }
                    }
                    if (!start.IsNull && !end.IsNull && end["operation_tick"].Integer >= start["operation_tick"].Integer)
                        m = m.With("fulfilled", JsonValue.Bool(kind == "always" ? !m["violated"].Boolean : !m["violated"].Boolean && m["current_hold_scenario_ticks"].Integer >= temporal["scenario_ticks"].Integer));
                }
                else throw new InvalidOperationException("Unknown typed temporal policy");
                monitors[def.id] = m;
                if (old != m["fulfilled"].Boolean) context.Events.Emit("ObjectiveUpdated", def.id, null, null, O("objective_id", def.id, "fulfilled", m["fulfilled"].Boolean));
            }
        
            done(true);
        }
        private static JsonValue FirstTrue(JsonValue m, OperationContext c) => m.With("first_true_operation_tick", JsonValue.Number(c.OperationTick)).With("first_true_scenario_tick", JsonValue.Number(c.ScenarioTick));
        private void UpdateMetrics(JsonValue state, ObjectiveObservations facts, OperationContext c) => UpdateMetricsWork(state, facts, c).Drain();
        private ResumableWork<bool> UpdateMetricsWork(JsonValue state, ObjectiveObservations facts, OperationContext c) => new ResumableWork<bool>(done => UpdateMetricsRoutine(state, facts, c, done));
        private IEnumerable<string> UpdateMetricsRoutine(JsonValue state, ObjectiveObservations facts, OperationContext c, Action<bool> done)
        {
            var people = state["residents"].Items;
            if (queries.Values.Any(x => x["metric"].Text == "peak_node_occupancy") && (!facts.NodeOccupancyPeaks.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(layout.nav_nodes.Select(x => x.id).OrderBy(x => x, StringComparer.Ordinal)) || facts.NodeOccupancyPeaks.Values.Any(x => x < 0 || x > mission.total_residents))) throw new InvalidOperationException("Complete actual node occupancy peaks required");
            var events = c.Events.Current.Select(x => x.ToJson()).ToArray();
            foreach (var passage in facts.EdgePassages) {yield return "objective.metrics";  if (!people.Any(x => x["id"].Text == passage.ResidentId) || !layout.nav_edges.Any(x => x.id == passage.EdgeId)) throw new InvalidOperationException("Unbound completed edge passage"); }
            foreach (var dwell in facts.NodeDwells) { yield return "objective.metrics"; if (dwell.ScenarioTicks < 0 || dwell.ScenarioTicks > c.ScenarioTick || !people.Any(x => x["id"].Text == dwell.ResidentId) || !layout.nav_nodes.Any(x => x.id == dwell.NodeId)) throw new InvalidOperationException("Unbound completed node dwell"); }
            foreach (var wait in facts.DockWaits) {yield return "objective.metrics";  if (!layout.nav_nodes.Any(x => x.id == wait.NodeId) || !state["vehicles"].Items.Any(x => x["id"].Text == wait.VehicleId)) throw new InvalidOperationException("Unbound dock wait"); }
            using (var dockWindows = RegisterDockWindowsWork(state)) { while (dockWindows.MoveNext()) yield return dockWindows.Stage; }
            long loadedClosedTicks = 0;
            var dockElapsed = DockNodes().ToDictionary(x => x, x => 0L, StringComparer.Ordinal);
            foreach (var vehicle in state["vehicles"].Items) { yield return "objective.metrics"; foreach (string node in DockNodes())
            {yield return "objective.metrics"; 
                string id = DockId(vehicle["id"].Text, node); var old = metrics[id];
                var wait = facts.DockWaits.SingleOrDefault(x => x.VehicleId == vehicle["id"].Text && x.NodeId == node);
                if (old["value"].Integer > 0) dockElapsed[node] = Math.Max(dockElapsed[node], old["value"].Integer);
                long prior = metrics[DockId(vehicle["id"].Text, node, true)]["value"].Integer;
                if (prior > 0 && wait != null && wait.Loaded && wait.ChosenUnloadDockClosed) loadedClosedTicks++;
                metrics[DockId(vehicle["id"].Text, node, true)] = metrics[DockId(vehicle["id"].Text, node, true)].With("value", JsonValue.Number(wait == null ? 0 : checked(prior + 1)));
                metrics[id] = old.With("value", JsonValue.Number(wait == null || !wait.ForBerthOrReceivingCapacity ? 0 : checked(old["value"].Integer + 1)));
            } }
            long stationShutdowns = 0;
            foreach (string id in metrics.Keys.Where(x => metrics[x]["metric"].Text == "objective_station_activation").ToArray())
            {yield return "objective.metrics"; 
                var row = metrics[id]; bool active = facts.InfrastructureOperational[row["subject_id"].Text]; long old = row["value"].Integer;
                if (old == 1 && !active) stationShutdowns++;
                metrics[id] = row.With("value", JsonValue.Number(active ? 1 : old == 0 ? 0 : 2));
            }
            foreach (var query in queries)
            {yield return "objective.metrics"; 
                var q = query.Value; var old = metrics[query.Key]; long value = old["value"].Integer; var distinct = new SortedSet<string>(old["distinct_resident_ids"].Items.Select(x => x.Text), StringComparer.Ordinal);
                string metric = q["metric"].Text;
                switch (metric)
                {
                    case "evacuated_by_mode": foreach (var p in people.Where(x => x["state"].Text == "evacuated" && x["final_leg_mode"].Text == q["mode"].Text && (q["group_id"].IsNull || x["group_id"].Text == q["group_id"].Text))) { yield return "objective.metrics"; distinct.Add(p["id"].Text); } value = distinct.Count; break;
                    case "edge_transfers": foreach (var p in facts.EdgePassages.Where(x => layout.nav_edges.Single(e => e.id == x.EdgeId).tags.Contains(q["edge_tag"].Text))) { yield return "objective.metrics"; distinct.Add(p.ResidentId); } value = distinct.Count; break;
                    case "equipment_deployments": value = state["deployments"].Items.Where(x => x["equipment_id"].Text == q["equipment_id"].Text).Sum(x => x["count"].Integer); break;
                    case "max_node_dwell_scenario_ticks": value = Math.Max(value, Math.Max(facts.NodeDwells.Where(x => x.NodeId == q["node_id"].Text).Select(x => x.ScenarioTicks).DefaultIfEmpty(0).Max(), people.Where(x => (x["state"].Text == "waiting" || x["state"].Text == "boarding") && x["location"]["kind"].Text == "node" && x["location"]["node_id"].Text == q["node_id"].Text && !x["node_entered_scenario_tick"].IsNull).Select(x => checked(c.ScenarioTick - x["node_entered_scenario_tick"].Integer)).DefaultIfEmpty(0).Max())); break;
                    case "peak_node_occupancy": value = Math.Max(value, facts.NodeOccupancyPeaks[q["node_id"].Text]); break;
                    case "max_dock_queue_operation_ticks": value = Math.Max(value, Math.Max(dockElapsed[q["node_id"].Text], state["vehicles"].Items.Select(x => Math.Max(0, metrics[DockId(x["id"].Text, q["node_id"].Text)]["value"].Integer - 1)).DefaultIfEmpty(0).Max())); break;
                    case "loaded_closed_dock_wait_operation_ticks": if (c.OperationTick > 0) value = checked(value + loadedClosedTicks); break;
                    case "pumped_into_region_mm3": foreach (var e in events.Where(x => x["kind"].Text == "WaterTransfer" && x["payload"]["request_id"].Text.StartsWith("pump:", StringComparison.Ordinal))) { yield return "objective.metrics"; if (layout.water_nodes.Any(x => x.id == e["payload"]["destination_id"].Text && x.region_id == q["region_id"].Text)) value = checked(value + e["payload"]["granted_mm3"].Integer); } break;
                    case "generator_shutdowns": value = checked(value + events.Count(x => x["kind"].Text == "HazardStateChanged" && x["payload"]["hazard"].Text == "power" && x["payload"]["old_state"].Text == "active" && x["payload"]["new_state"].Text == "safety_tripped")); break;
                    case "station_shutdowns": value = checked(value + stationShutdowns); break;
                    case "shelter_exhaustions": value = checked(value + events.Count(x => x["kind"].Text == "HazardStateChanged" && x["payload"]["hazard"].Text == "resource" && x["payload"]["old_state"].Text == "protected" && x["payload"]["new_state"].Text == "exhausted")); break;
                    default: throw new InvalidOperationException("Unknown closed metric");
                }
                ValidateMetricValue(metric, value, c.OperationTick); metrics[query.Key] = MetricRow(query.Key, q, value, distinct);
            }
        
            done(true);
        }
        private string DockId(string vehicle, string node, bool presence = false) => (presence ? "objective:dockpresence:" : "objective:dockwait:") + O("mission_version_id", mission.mission_version_id, "vehicle_id", vehicle, "node_id", node).Sha256();
        private string[] DockNodes()
        {
            if (!queries.Values.Any(x => x["metric"].Text == "max_dock_queue_operation_ticks" || x["metric"].Text == "loaded_closed_dock_wait_operation_ticks")) return new string[0];
            return layout.nav_nodes.Where(x => x.vehicle_slots > 0).Select(x => x.id).Concat(queries.Values.Where(x => x["metric"].Text == "max_dock_queue_operation_ticks").Select(x => x["node_id"].Text)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        private void RegisterDockWindows(JsonValue state) => RegisterDockWindowsWork(state).Drain();
        private ResumableWork<bool> RegisterDockWindowsWork(JsonValue state) => new ResumableWork<bool>(done => RegisterDockWindowsRoutine(state, done));
        private IEnumerable<string> RegisterDockWindowsRoutine(JsonValue state, Action<bool> done)
        {
            foreach (var vehicle in state["vehicles"].Items) { yield return "objective.register.dock"; foreach (string node in DockNodes())
            {yield return "objective.register.dock"; 
                string id = DockId(vehicle["id"].Text, node);
                if (!metrics.ContainsKey(id)) metrics.Add(id, O("id", id, "metric", "objective_dock_wait", "subject_id", vehicle["id"].Text, "value", 0, "distinct_resident_ids", A()));
                id = DockId(vehicle["id"].Text, node, true);
                if (!metrics.ContainsKey(id)) metrics.Add(id, O("id", id, "metric", "objective_dock_presence_wait", "subject_id", vehicle["id"].Text, "value", 0, "distinct_resident_ids", A()));
            } }
        
            done(true);
        }
        public bool Evaluate(string kind, JsonValue p, JsonValue state, ObjectiveObservations facts, long scenarioTick) => EvaluateWork(kind, p, state, facts, scenarioTick).Drain();
        public ResumableWork<bool> EvaluateWork(string kind, JsonValue p, JsonValue state, ObjectiveObservations facts, long scenarioTick) => new ResumableWork<bool>(done => EvaluateRoutine(kind, p, state, facts, scenarioTick, done));
        private IEnumerable<string> EvaluateRoutine(string kind, JsonValue p, JsonValue state, ObjectiveObservations facts, long scenarioTick, Action<bool> done)
        { yield return "objective.evaluate.condition";
            var people = state["residents"].Items;
            switch (kind)
            {
                case "rescue_count": { done(people.Count(x => x["state"].Text == "evacuated") >= p["minimum"].Integer); yield break; }
                case "group_evacuated": { done(people.Count(x => x["state"].Text == "evacuated" && x["group_id"].Text == p["group_id"].Text) >= p["minimum"].Integer); yield break; }
                case "infrastructure_operational": { done(facts.InfrastructureOperational[p["infrastructure_id"].Text] == p["operational"].Boolean); yield break; }
                case "route_available": { done(facts.RoutesAvailable[ObjectiveObservations.RouteKey(p["route_id"].Text, p["traversal_class"].Text)]); yield break; }
                case "exposure_below": { done(facts.RegionExposure[ObjectiveObservations.ExposureKey(p["region_id"].Text, p["hazard"].Text)] < p["threshold"].Integer); yield break; }
                case "equipment_losses_at_most": { done(state["equipment"].Items.Count(x => x["lifecycle"].Text == "broken") <= p["maximum"].Integer); yield break; }
                case "supply_remaining_at_least": { done(state["supplies_remaining"].Integer >= p["minimum"].Integer); yield break; }
                case "resource_remaining":
                    var store = layout.resources.Single(x => x.id == p["store_id"].Text);
                    long amount = store.unit == "water_mm3" ? state["water"].Items.Single(x => x["id"].Text == store.water_node_id)["volume_mm3"].Integer : state["stores"].Items.Single(x => x["id"].Text == store.id)["amount"].Integer;
                    { done(amount >= p["minimum"].Integer); yield break; }
                case "all_vehicles_unloaded": { done(VehiclesClear(state)); yield break; }
                case "temporary_shelters_empty": { done(SheltersClear(state)); yield break; }
                case "temporary_staging_empty": { done(!people.Any(x => x["location"]["kind"].Text == "node" && layout.nav_nodes.Single(n => n.id == x["location"]["node_id"].Text).is_temporary_staging)); yield break; }
                case "scenario_elapsed_at_most": { done(scenarioTick <= p["maximum"].Integer); yield break; }
                case "counter_at_least": { done(metrics[MetricId(mission.mission_version_id, p)]["value"].Integer >= p["minimum"].Integer); yield break; }
                case "counter_at_most": { done(metrics[MetricId(mission.mission_version_id, p)]["value"].Integer <= p["maximum"].Integer); yield break; }
                case "all": case "any":
                    foreach (var child in p["conditions"].Items)
                    {yield return "objective.evaluate"; 
                        bool childTruth; using (var work = EvaluateWork(child["kind"].Text, child["params"], state, facts, scenarioTick)) { while (work.MoveNext()) yield return work.Stage; childTruth = work.Result; }
                        if (childTruth == (kind == "any")) { done(childTruth); yield break; }
                    }
                    done(kind == "all"); yield break;
                default: throw new InvalidOperationException("Unknown typed condition");
            }
                }
        private static bool VehiclesClear(JsonValue state) => state["vehicles"].Items.All(x => x["occupant_ids"].Items.Count == 0);
        private bool SheltersClear(JsonValue state) => !state["residents"].Items.Any(x => x["location"]["kind"].Text == "node" && layout.nav_nodes.Single(n => n.id == x["location"]["node_id"].Text).kind == "shelter");
        public bool CanEndSuccessfully(JsonValue state, ObjectiveObservations facts, long scenarioTick) => CanEndSuccessfullyWork(state, facts, scenarioTick).Drain();
        public ResumableWork<bool> CanEndSuccessfullyWork(JsonValue state, ObjectiveObservations facts, long scenarioTick) => new ResumableWork<bool>(done => CanEndSuccessfullyRoutine(state, facts, scenarioTick, done));
        private IEnumerable<string> CanEndSuccessfullyRoutine(JsonValue state, ObjectiveObservations facts, long scenarioTick, Action<bool> done)
        {
            long rescued = 0;
            foreach (var person in state["residents"].Items) { if (person["state"].Text == "evacuated") rescued++; yield return "objective.required.resident"; }
            if (rescued < mission.minimum_rescues || !VehiclesClear(state) || !SheltersClear(state)) { done(false); yield break; }
            foreach (var def in mission.objectives.Where(x => x.tier == "required"))
            {
                using (var work = ProspectiveRequiredWork(def, state, facts, scenarioTick)) { while (work.MoveNext()) yield return work.Stage; if (!work.Result) { done(false); yield break; } }
            }
            done(true);
        }
        private bool ProspectiveRequired(ObjectiveDef def, JsonValue state, ObjectiveObservations facts, long scenarioTick) => ProspectiveRequiredWork(def, state, facts, scenarioTick).Drain();
        private ResumableWork<bool> ProspectiveRequiredWork(ObjectiveDef def, JsonValue state, ObjectiveObservations facts, long scenarioTick) => new ResumableWork<bool>(done => ProspectiveRequiredRoutine(def, state, facts, scenarioTick, done));
        private IEnumerable<string> ProspectiveRequiredRoutine(ObjectiveDef def, JsonValue state, ObjectiveObservations facts, long scenarioTick, Action<bool> done)
        {
            var t = def.temporal.ToJson(); var m = monitors[def.id]; string kind = t["kind"].Text;
            if (kind == "at_end") { using (var work = EvaluateWork(def.kind, def.@params.ToJson(), state, facts, scenarioTick)) { while (work.MoveNext()) yield return work.Stage; done(work.Result); } yield break; }
            if (m["fulfilled"].Boolean) { { done(true); yield break; } }
            if ((kind == "always" || kind == "hold_for") && t["end_event"].Text == "operation_ended" && !m["started_operation_tick"].IsNull && m["ended_operation_tick"].IsNull && !m["violated"].Boolean)
            {
                using (var work = EvaluateWork(def.kind, def.@params.ToJson(), state, facts, scenarioTick))
                { while (work.MoveNext()) yield return work.Stage; done(work.Result && (kind == "always" || m["current_hold_scenario_ticks"].Integer >= t["scenario_ticks"].Integer)); } yield break;
            }
            done(false);
        }
        public OutcomeResult Outcome(JsonValue state) => OutcomeWork(state).Drain();
        public ResumableWork<OutcomeResult> OutcomeWork(JsonValue state) => new ResumableWork<OutcomeResult>(done => OutcomeRoutine(state, done));
        private IEnumerable<string> OutcomeRoutine(JsonValue state, Action<OutcomeResult> done)
        {
            int rescued = 0, backup = 0; foreach (var person in state["residents"].Items) {yield return "objective.outcome";  if (person["state"].Text == "evacuated") rescued++; if (person["state"].Text == "awaiting_backup") backup++; }
            bool required = Tier("required"), critical = Tier("critical"), mastery = Tier("mastery");
            bool success = rescued >= mission.minimum_rescues && required && VehiclesClear(state) && SheltersClear(state);
            int stars = !success ? 0 : rescued == mission.total_residents && critical && mastery ? 3 : checked(10L * rescued) >= checked(9 * mission.total_residents) && critical ? 2 : 1;
            int score = TerminalScore.Compute(rescued, mission.total_residents, mission.objectives.Count(x => x.tier == "required" && monitors[x.id]["fulfilled"].Boolean), mission.objectives.Count(x => x.tier == "required"), mission.protection_objective_ids.Count(x => monitors[x]["fulfilled"].Boolean), mission.protection_objective_ids.Count, state["supplies_remaining"].Integer, mission.supply_budget);
            done(new OutcomeResult(success, stars, score, rescued, backup));
                }
        private bool Tier(string tier) => mission.objectives.Where(x => x.tier == tier).All(x => monitors[x.id]["fulfilled"].Boolean);
        // Bounded durable causal facts. Recent events are supplied as diagnostics;
        // this does not claim that a capped log retains the full run history.
        public JsonValue Debrief(IEnumerable<JsonValue> milestones, IEnumerable<RuntimeEvent> diagnostics) => O("objective_summaries", Monitors, "metric_summaries", Metrics, "milestones", CanonicalState.SortedEntities(milestones), "recent_diagnostics", JsonValue.Array(diagnostics.Select(x => x.ToJson())), "diagnostics_complete_history", false);
        public ObjectiveSystem Restore(JsonValue savedMetrics, JsonValue savedMonitors, long operationTick, long scenarioTick, JsonValue domainState, ObjectiveObservations restoredFacts, IEnumerable<JsonValue> milestones, bool terminal = false) => RestoreWork(savedMetrics, savedMonitors, operationTick, scenarioTick, domainState, restoredFacts, milestones, terminal).Drain();
        public ResumableWork<ObjectiveSystem> RestoreWork(JsonValue savedMetrics, JsonValue savedMonitors, long operationTick, long scenarioTick, JsonValue domainState, ObjectiveObservations restoredFacts, IEnumerable<JsonValue> milestones, bool terminal = false) => new ResumableWork<ObjectiveSystem>(done => RestoreRoutine(savedMetrics, savedMonitors, operationTick, scenarioTick, domainState, restoredFacts, milestones, terminal, done));
        private IEnumerable<string> RestoreRoutine(JsonValue savedMetrics, JsonValue savedMonitors, long operationTick, long scenarioTick, JsonValue domainState, ObjectiveObservations restoredFacts, IEnumerable<JsonValue> milestones, bool terminal, Action<ObjectiveSystem> done)
        {
            ObjectiveSystem copy;
            using (var create = CreateWork(mission, layout)) { while (create.MoveNext()) yield return create.Stage; copy = create.Result; }
            new Tick(operationTick); new Tick(scenarioTick); if (scenarioTick > operationTick) throw new InvalidOperationException("Restore clock coherence");
            var register = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var row in milestones) { register.Add(row["id"].Text, row); yield return "objective.restore.milestone_index"; }
            if (terminal != register.ContainsKey("operation_ended")) throw new InvalidOperationException("Restore terminal milestone coherence");
            foreach (var row in register.Values) { yield return "objective.restore"; if (row["operation_tick"].Integer < 0 || row["operation_tick"].Integer > operationTick || row["scenario_tick"].Integer < 0 || row["scenario_tick"].Integer > scenarioTick) throw new InvalidOperationException("Restore milestone clock bounds"); } if (restoredFacts == null || restoredFacts.OperationTick != operationTick) throw new InvalidOperationException("Actual restored boundary observations required"); using (var dockWindows = copy.RegisterDockWindowsWork(domainState)) { while (dockWindows.MoveNext()) yield return dockWindows.Stage; }
            var rows = new Dictionary<string, JsonValue>(StringComparer.Ordinal); var ms = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var row in savedMetrics.Items) { rows.Add(row["id"].Text, row); yield return "objective.restore.metric_index"; }
            foreach (var row in savedMonitors.Items) { ms.Add(row["id"].Text, row); yield return "objective.restore.monitor_index"; }
            if (rows.Count != copy.metrics.Count) throw new InvalidOperationException("Objective restore exact registered owners");
            foreach (string key in rows.Keys) { if (!copy.metrics.ContainsKey(key)) throw new InvalidOperationException("Objective restore exact registered owners"); yield return "objective.restore.metric_owner"; }
            if (ms.Count != monitors.Count) throw new InvalidOperationException("Objective restore exact registered owners");
            foreach (string key in ms.Keys) { if (!monitors.ContainsKey(key)) throw new InvalidOperationException("Objective restore exact registered owners"); yield return "objective.restore.monitor_owner"; }
            foreach (var pair in rows)
            {yield return "objective.restore"; 
                var row = pair.Value; var expected = copy.metrics[pair.Key]; Fields(row, "id", "metric", "subject_id", "value", "distinct_resident_ids");
                if (row["metric"].Canonical() != expected["metric"].Canonical() || row["subject_id"].Canonical() != expected["subject_id"].Canonical() || row["value"].Kind != "integer" || row["value"].Integer < 0) throw new InvalidOperationException("Metric binding/value");
                ValidateMetricValue(row["metric"].Text, row["value"].Integer, operationTick);
                if (row["metric"].Text == "objective_station_activation" && (row["value"].Integer == 1) != restoredFacts.InfrastructureOperational[row["subject_id"].Text]) throw new InvalidOperationException("Station restore operational coherence");
                if (queries.TryGetValue(pair.Key, out var query) && row["metric"].Text == "evacuated_by_mode")
                {
                    var expectedIds = domainState["residents"].Items.Where(x => x["state"].Text == "evacuated" && x["final_leg_mode"].Text == query["mode"].Text && (query["group_id"].IsNull || x["group_id"].Text == query["group_id"].Text)).Select(x => x["id"].Text).OrderBy(x => x, StringComparer.Ordinal);
                    if (!row["distinct_resident_ids"].Items.Select(x => x.Text).SequenceEqual(expectedIds)) throw new InvalidOperationException("Evacuation metric restore coherence");
                }
                if (queries.TryGetValue(pair.Key, out query) && row["metric"].Text == "equipment_deployments" && row["value"].Integer != domainState["deployments"].Items.Where(x => x["equipment_id"].Text == query["equipment_id"].Text).Sum(x => x["count"].Integer)) throw new InvalidOperationException("Deployment metric restore coherence");
                if (queries.TryGetValue(pair.Key, out query) && row["metric"].Text == "peak_node_occupancy" && row["value"].Integer < domainState["residents"].Items.Count(x => x["location"]["kind"].Text == "node" && x["location"]["node_id"].Text == query["node_id"].Text)) throw new InvalidOperationException("Peak occupancy restore lower bound");
                if (queries.TryGetValue(pair.Key, out query) && row["metric"].Text == "max_node_dwell_scenario_ticks" && row["value"].Integer < domainState["residents"].Items.Where(x => (x["state"].Text == "waiting" || x["state"].Text == "boarding") && x["location"]["kind"].Text == "node" && x["location"]["node_id"].Text == query["node_id"].Text && !x["node_entered_scenario_tick"].IsNull).Select(x => scenarioTick - x["node_entered_scenario_tick"].Integer).DefaultIfEmpty(0).Max()) throw new InvalidOperationException("Dwell restore lower bound");
                var ids = row["distinct_resident_ids"].Items.Select(x => x.Text).ToArray(); if (!ids.SequenceEqual(ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) || ids.Any(x => !mission.groups.Any(g => g.resident_ids.Contains(x)))) throw new InvalidOperationException("Metric resident set");
                if (row["metric"].Text == "evacuated_by_mode" || row["metric"].Text == "edge_transfers") { if (row["value"].Integer != ids.Length) throw new InvalidOperationException("Metric distinct count"); }
                else if (ids.Length != 0 || (row["metric"].Text == "objective_station_activation" && row["value"].Integer > 2 || (row["metric"].Text == "objective_dock_wait" || row["metric"].Text == "objective_dock_presence_wait") && row["value"].Integer > operationTick + 1)) throw new InvalidOperationException("Metric closed shape");
            }
            copy.metrics = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var pair in rows) { copy.metrics.Add(pair.Key, pair.Value); yield return "objective.restore.publish_metric"; }
            foreach (var pair in ms)
            {yield return "objective.restore"; 
                var m = pair.Value; Fields(m, "id", "started_operation_tick", "ended_operation_tick", "first_true_operation_tick", "first_true_scenario_tick", "violated", "current_hold_scenario_ticks", "fulfilled");
                foreach (string key in new[] { "started_operation_tick", "ended_operation_tick", "first_true_operation_tick", "first_true_scenario_tick" }) { yield return "objective.restore"; if (!m[key].IsNull && (m[key].Kind != "integer" || m[key].Integer < 0 || m[key].Integer > operationTick)) throw new InvalidOperationException("Monitor tick"); }
                if (m["violated"].Kind != "boolean" || m["fulfilled"].Kind != "boolean" || m["current_hold_scenario_ticks"].Kind != "integer" || m["current_hold_scenario_ticks"].Integer < 0 || m["current_hold_scenario_ticks"].Integer > Tick.Maximum || m["first_true_operation_tick"].IsNull != m["first_true_scenario_tick"].IsNull) throw new InvalidOperationException("Monitor closed value");
                var def = mission.objectives.Single(x => x.id == pair.Key); var temporal = def.temporal.ToJson(); string kind = temporal["kind"].Text;
                JsonValue start = temporal.Has("start_event") && register.TryGetValue(temporal["start_event"].Text, out var startRow) ? startRow : JsonValue.Null();
                JsonValue finish = temporal.Has("end_event") && register.TryGetValue(temporal["end_event"].Text, out var endRow) ? endRow : JsonValue.Null();
                if (m["started_operation_tick"].Canonical() != (start.IsNull ? JsonValue.Null() : start["operation_tick"]).Canonical() || m["ended_operation_tick"].Canonical() != (finish.IsNull ? JsonValue.Null() : finish["operation_tick"]).Canonical()) throw new InvalidOperationException("Monitor milestone binding");
                bool first = !m["first_true_operation_tick"].IsNull; long held = m["current_hold_scenario_ticks"].Integer;
                if (first && (m["first_true_scenario_tick"].Integer > scenarioTick || m["first_true_scenario_tick"].Integer > m["first_true_operation_tick"].Integer || !start.IsNull && m["first_true_operation_tick"].Integer < start["operation_tick"].Integer)) throw new InvalidOperationException("Monitor first-true clock coherence");
                bool expected;
                if (kind == "at_end") { expected = terminal && copy.Evaluate(def.kind, def.@params.ToJson(), domainState, restoredFacts, scenarioTick); if (first || m["violated"].Boolean || held != 0) throw new InvalidOperationException("At-end monitor coherence"); }
                else if (kind == "ever_by") { expected = first; if (first && m["first_true_scenario_tick"].Integer > temporal["deadline_tick"].Integer || m["violated"].Boolean || held != 0) throw new InvalidOperationException("Ever-by monitor coherence"); }
                else if (kind == "ever_before") { expected = first && (finish.IsNull || m["first_true_operation_tick"].Integer < finish["operation_tick"].Integer); if (first && !expected || m["violated"].Boolean || held != 0) throw new InvalidOperationException("Strict-before monitor coherence"); }
                else
                {
                    if (start.IsNull && (first || m["violated"].Boolean || held != 0) || first && !finish.IsNull && m["first_true_operation_tick"].Integer > finish["operation_tick"].Integer) throw new InvalidOperationException("Interval sample clock coherence");
                    bool inWindow = !start.IsNull && operationTick >= start["operation_tick"].Integer && (finish.IsNull || operationTick <= finish["operation_tick"].Integer);
                    if (inWindow && !copy.Evaluate(def.kind, def.@params.ToJson(), domainState, restoredFacts, scenarioTick) && (!m["violated"].Boolean || kind == "hold_for" && held != 0)) throw new InvalidOperationException("Interval current truth coherence");
                    bool boundedInterval = !start.IsNull && !finish.IsNull && finish["operation_tick"].Integer >= start["operation_tick"].Integer;
                    if (kind == "always") { expected = boundedInterval && !m["violated"].Boolean; if (held != 0) throw new InvalidOperationException("Always hold coherence"); }
                    else { long available = start.IsNull ? 0 : (finish.IsNull ? scenarioTick : finish["scenario_tick"].Integer) - start["scenario_tick"].Integer; if (held > Math.Max(0, available) || m["violated"].Boolean && held != 0 || held > 0 && !first) throw new InvalidOperationException("Hold duration coherence"); expected = boundedInterval && !m["violated"].Boolean && held >= temporal["scenario_ticks"].Integer; }
                }
                if (m["fulfilled"].Boolean != expected) throw new InvalidOperationException("Temporal fulfillment coherence");

            }
            foreach (var vehicle in domainState["vehicles"].Items) { yield return "objective.restore"; foreach (string node in copy.DockNodes())
            {yield return "objective.restore"; 
                var wait = restoredFacts.DockWaits.SingleOrDefault(x => x.VehicleId == vehicle["id"].Text && x.NodeId == node);
                if ((rows[copy.DockId(vehicle["id"].Text, node)]["value"].Integer > 0) != (wait != null && wait.ForBerthOrReceivingCapacity) || (rows[copy.DockId(vehicle["id"].Text, node, true)]["value"].Integer > 0) != (wait != null)) throw new InvalidOperationException("Dock window restore active coherence");
            } }
            foreach (var query in queries.Where(x => x.Value["metric"].Text == "max_dock_queue_operation_ticks"))
            {yield return "objective.restore"; 
                long active = domainState["vehicles"].Items.Select(x => Math.Max(0, rows[copy.DockId(x["id"].Text, query.Value["node_id"].Text)]["value"].Integer - 1)).DefaultIfEmpty(0).Max();
                if (rows[query.Key]["value"].Integer < active) throw new InvalidOperationException("Dock maximum restore lower bound");
            }
            copy.monitors = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var pair in ms) { copy.monitors.Add(pair.Key, pair.Value); yield return "objective.restore.publish_monitor"; }
            copy.sampledOperationTick = operationTick; done(copy);
                }
        private void ValidateMetricValue(string metric, long value, long operationTick)
        {
            long bound;
            switch (metric)
            {
                case "peak_node_occupancy": case "evacuated_by_mode": case "edge_transfers": bound = mission.total_residents; break;
                case "equipment_deployments": bound = 512; break;
                case "max_node_dwell_scenario_ticks": case "max_dock_queue_operation_ticks": bound = Tick.Maximum; break;
                case "loaded_closed_dock_wait_operation_ticks": bound = checked(32 * operationTick); break;
                case "generator_shutdowns": case "shelter_exhaustions": bound = checked(512 * (operationTick + 1)); break;
                case "station_shutdowns": bound = checked(layout.infrastructure.Count(IsStation) * (operationTick + 1)); break;
                case "objective_station_activation": bound = 2; break;
                case "objective_dock_wait": case "objective_dock_presence_wait": bound = operationTick + 1; break;
                case "pumped_into_region_mm3": bound = long.MaxValue; break;
                default: throw new InvalidOperationException("Unknown metric value owner");
            }
            if (value < 0 || value > bound) throw new InvalidOperationException("SIMULATION_INVARIANT_FAULT: metric domain bound");
        }
        private static void Fields(JsonValue row, params string[] fields) { if (row.Kind != "object" || row.Members.Count != fields.Length || fields.Any(x => !row.Has(x))) throw new InvalidOperationException("Closed objective state fields"); }
        internal static JsonValue A(params string[] xs) => JsonValue.Array(xs.Select(JsonValue.String));
        internal static JsonValue O(params object[] pairs)
        { var d = new Dictionary<string, JsonValue>(StringComparer.Ordinal); for (int i = 0; i < pairs.Length; i += 2) { var v = pairs[i + 1]; d.Add((string)pairs[i], v == null ? JsonValue.Null() : v is JsonValue j ? j : v is bool b ? JsonValue.Bool(b) : v is string s ? JsonValue.String(s) : JsonValue.Number(Convert.ToInt64(v))); } return JsonValue.Object(d); }
    }
}
