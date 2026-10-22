using System;
using System.Collections.Generic;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Simulation.Objectives;

namespace Resq.Simulation.Forecast
{
    internal sealed class ForecastProjection
    {
        private sealed class Region
        { internal string Id, Hazard; internal long Maximum; internal long? FirstUnsafe; }
        private sealed class Route
        { internal string Id; internal bool Available = true, SeenIncomplete; internal long? Arrival; internal string Reason; }
        private readonly CompiledContent content;
        private readonly LayoutDef layout;
        private readonly JsonValue source;
        private readonly SortedDictionary<string, Region> regions = new SortedDictionary<string, Region>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Route> routes = new SortedDictionary<string, Route>(StringComparer.Ordinal);
        private readonly HashSet<string> namedRouteIds;
        private string[] regionKeys;
        internal IEnumerable<RiskRegion> Risks => regions.Values.Select(x => new RiskRegion(x.Id, x.Hazard, x.Maximum, x.FirstUnsafe));
        internal IEnumerable<RouteForecast> Routes => routes.Values.Select(x => new RouteForecast(x.Id, x.Available, x.Arrival, x.Reason));
        internal ForecastProjection(CompiledContent content, JsonValue source) : this(content, source, true)
        { InitializeWork().Drain(); }
        private ForecastProjection(CompiledContent content, JsonValue source, bool deferred)
        {
            this.content = content; this.source = source;
            layout = content.Envelope.payload.layouts.Single(x => x.id == source["identity"]["layout_version_id"].Text);
            namedRouteIds = new HashSet<string>(StringComparer.Ordinal);
        }
        internal static ResumableWork<ForecastProjection> CreateWork(CompiledContent content, JsonValue source)
            => new ResumableWork<ForecastProjection>(done => CreateRoutine(content, source, done));
        private static IEnumerable<string> CreateRoutine(CompiledContent content, JsonValue source, Action<ForecastProjection> done)
        {
            var next = new ForecastProjection(content, source, true);
            yield return "projection.initialize";
            using (var work = next.InitializeWork()) { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork() => new ResumableWork<bool>(InitializeRoutine);
        private IEnumerable<string> InitializeRoutine(Action<bool> done)
        {
            foreach (var route in layout.routes) { namedRouteIds.Add(route.id); yield return "projection.initialize.route"; }
            done(true);
        }
        internal void Observe(JsonValue state, ForecastBoundaryObservations facts) => ObserveWork(state, facts).Drain();
        internal ResumableWork<bool> ObserveWork(JsonValue state, ForecastBoundaryObservations facts)
            => new ResumableWork<bool>(done => ObserveSteps(state, facts, done));
        private IEnumerable<string> ObserveSteps(JsonValue state, ForecastBoundaryObservations facts, Action<bool> complete)
        {
            long operation = state["operation_tick"].Integer, scenario = state["scenario_tick"].Integer;
            if (facts.OperationTick != operation) throw new InvalidOperationException("Forecast observation cursor mismatch");
            string[] keys;
            using (var ordered = ResumableSort.Create(facts.Risks.Select(x => ObjectiveObservations.ExposureKey(x.RegionId, x.Hazard)), StringComparer.Ordinal))
            { while (ordered.MoveNext()) yield return ordered.Stage; keys = ordered.Result; }
            if (regionKeys == null) regionKeys = keys;
            else
            {
                if (regionKeys.Length != keys.Length) throw new InvalidOperationException("Forecast hazard samples changed ownership set");
                for (int index = 0; index < keys.Length; index++)
                { if (regionKeys[index] != keys[index]) throw new InvalidOperationException("Forecast hazard samples changed ownership set"); yield return "projection.risk_identity"; }
            }
            foreach (var risk in facts.Risks)
            {
                string key = ObjectiveObservations.ExposureKey(risk.RegionId, risk.Hazard);
                if (!regions.TryGetValue(key, out var row)) { row = new Region { Id = risk.RegionId, Hazard = risk.Hazard }; regions.Add(key, row); }
                row.Maximum = Math.Max(row.Maximum, risk.Value);
                if (risk.Unsafe && !row.FirstUnsafe.HasValue) row.FirstUnsafe = scenario;
                yield return "projection.risk";
            }
            foreach (var definition in layout.routes)
            {
                string key = ObjectiveObservations.RouteKey(definition.id, definition.traversal_class);
                if (!facts.Objectives.RoutesAvailable.TryGetValue(key, out bool available)) throw new InvalidOperationException("Actual named-route observation missing");
                if (!routes.TryGetValue(definition.id, out var row)) { row = new Route { Id = definition.id }; routes.Add(row.Id, row); }
                row.Available &= available;
                if (!available) row.Reason = "ROUTE_BLOCKED";
                yield return "projection.named_route";
                // A graph query has no mover/order or queue admission. Its ETA
                // remains null; only an actual route order can yield an arrival.
            }
            var currentOrders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in state["routes"].Items) { currentOrders.Add(row["id"].Text); yield return "projection.order_index"; }
            var removedIds = new List<string>();
            foreach (string id in routes.Keys) { if (!namedRouteIds.Contains(id) && !currentOrders.Contains(id)) removedIds.Add(id); yield return "projection.obsolete_route"; }
            foreach (string id in removedIds) { routes.Remove(id); yield return "projection.remove_route"; }
            var personGroups = new Dictionary<string, string>(StringComparer.Ordinal);
            var failedGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var person in state["residents"].Items)
            {
                personGroups.Add(person["id"].Text, person["group_id"].Text);
                if (person["state"].Text == "awaiting_backup") failedGroups.Add(person["group_id"].Text);
                yield return "projection.resident_index";
            }
            var directQueues = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            var groupQueues = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var queued in state["queues"].Items)
            {
                string subjectId = queued["subject_id"].Text;
                if (!directQueues.ContainsKey(subjectId)) directQueues.Add(subjectId, queued);
                if (personGroups.TryGetValue(subjectId, out string group) && !groupQueues.ContainsKey(group)) groupQueues.Add(group, queued);
                yield return "projection.queue_index";
            }
            var vehicles = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var vehicle in state["vehicles"].Items) { vehicles.Add(vehicle["id"].Text, vehicle); yield return "projection.vehicle_index"; }
            foreach (var order in state["routes"].Items)
            {
                string id = order["id"].Text;
                if (namedRouteIds.Contains(id)) throw new InvalidOperationException("Route order collides with authored route identity");
                if (!routes.TryGetValue(id, out var row)) { row = new Route { Id = id }; routes.Add(id, row); }
                if (row.Arrival.HasValue) { yield return "projection.arrived_route"; continue; }
                string subject = order["subject_id"].Text;
                bool routeComplete = order["next_stop_index"].Integer >= order["stops"].Items.Count;
                if (routeComplete)
                {
                    // An already complete source order has no saved completion
                    // timestamp. Only a completion observed in this continuation
                    // supplies an ETA; never substitute the snapshot's O value.
                    if (row.SeenIncomplete) row.Arrival = operation;
                    row.Available = true; row.Reason = null; yield return "projection.completed_route"; continue;
                }
                row.SeenIncomplete = true;
                directQueues.TryGetValue(subject, out var queue);
                if (queue == null && order["subject_kind"].Text == "group") groupQueues.TryGetValue(subject, out queue);
                var vehicle = order["subject_kind"].Text == "vehicle" ? vehicles[subject] : null;
                bool failed = vehicle != null ? vehicle["status"].Text == "failed" : failedGroups.Contains(subject);
                bool waitingPower = vehicle != null && (vehicle["status"].Text == "waiting_power" || vehicle["status"].Text == "braked");
                row.Available = !failed && !waitingPower && queue == null;
                row.Reason = failed ? "AWAITING_BACKUP" : waitingPower ? "POWER_UNAVAILABLE" : queue == null ? null : QueueReason(queue["kind"].Text);
                yield return "projection.route_order";
            }
            complete(true);
        }
        internal ResumableWork<ForecastResult> ResultWork(ForecastRequest request, string reason, string knowledge, long simulated, CommandResult receipt)
            => new ResumableWork<ForecastResult>(done => ResultSteps(request, reason, knowledge, simulated, receipt, done));
        private IEnumerable<string> ResultSteps(ForecastRequest request, string reason, string knowledge, long simulated, CommandResult receipt, Action<ForecastResult> complete)
        {
            var riskRows = new List<RiskRegion>(); var routeRows = new List<RouteForecast>();
            foreach (var region in regions.Values) { riskRows.Add(new RiskRegion(region.Id, region.Hazard, region.Maximum, region.FirstUnsafe)); yield return "publication.risk"; }
            foreach (var route in routes.Values) { routeRows.Add(new RouteForecast(route.Id, route.Available, route.Arrival, route.Reason)); yield return "publication.route"; }
            complete(ForecastResult.FromOwnedRows(request, reason, knowledge, simulated, riskRows, routeRows, receipt));
        }
        private static string QueueReason(string kind)
        {
            switch (kind)
            {
                case "power": return "POWER_UNAVAILABLE";
                case "capacity": return "CAPACITY_FULL";
                case "berth": return "BERTH_UNAVAILABLE";
                case "edge": return "ROUTE_BLOCKED";
                default: throw new InvalidOperationException("Unknown actual queue kind");
            }
        }
        internal PreviewExplanation Explain(GameCommand command, CommandResult receipt, JsonValue state) => ExplainWork(command, receipt, state).Drain();
        internal ResumableWork<PreviewExplanation> ExplainWork(GameCommand command, CommandResult receipt, JsonValue state)
            => new ResumableWork<PreviewExplanation>(done => ExplainSteps(command, receipt, state, done));
        private IEnumerable<string> ExplainSteps(GameCommand command, CommandResult receipt, JsonValue state, Action<PreviewExplanation> complete)
        {
            EquipmentDef definition = null; JsonValue placed = null;
            long seats = 0, slots = 0, cost = 0, demand = 0; long? stock = null, grant = null;
            var reasons = new List<string>();
            if (command.Payload is PlaceEquipment place)
            {
                definition = content.Envelope.payload.equipment.SingleOrDefault(x => x.id == place.EquipmentId);
                if (definition != null)
                {
                    cost = definition.supply_cost; demand = definition.power_demand; seats = definition.resident_seats; slots = definition.movement_slots;
                    var selected = state["selected_stock"].Items.SingleOrDefault(x => x["equipment_id"].Text == definition.id);
                    var deployed = state["deployments"].Items.SingleOrDefault(x => x["equipment_id"].Text == definition.id);
                    if (selected != null && deployed != null) stock = checked(selected["count"].Integer - deployed["count"].Integer);
                    placed = state["equipment"].Items.FirstOrDefault(x => x["equipment_id"].Text == definition.id && x["socket_id"].Text == place.SocketId);
                }
            }
            else if (command.Payload is AssignRoute route && route.SubjectKind == "vehicle")
            {
                var fixedVehicle = layout.fixed_vehicles.SingleOrDefault(x => x.vehicle_id == route.SubjectId);
                if (fixedVehicle != null) { seats = fixedVehicle.seats; slots = fixedVehicle.movement_slots; }
                else
                {
                    var vehicle = state["vehicles"].Items.SingleOrDefault(x => x["id"].Text == route.SubjectId);
                    if (vehicle != null && !vehicle["equipment_instance_id"].IsNull)
                        placed = state["equipment"].Items.Single(x => x["id"].Text == vehicle["equipment_instance_id"].Text);
                    if (placed != null) { definition = content.Envelope.payload.equipment.Single(x => x.id == placed["equipment_id"].Text); seats = definition.resident_seats; slots = definition.movement_slots; demand = definition.power_demand; }
                }
            }
            else if (command.Payload is ActivateTool || command.Payload is MoveIdleEquipment)
            {
                string instanceId = command.Payload is ActivateTool action ? action.EquipmentInstanceId : ((MoveIdleEquipment)command.Payload).EquipmentInstanceId;
                placed = state["equipment"].Items.SingleOrDefault(x => x["id"].Text == instanceId);
                if (placed != null)
                {
                    definition = content.Envelope.payload.equipment.Single(x => x.id == placed["equipment_id"].Text);
                    seats = definition.resident_seats; slots = definition.movement_slots; demand = definition.power_demand;
                }
                // Activation and relocation do not buy another deployment.
                // Nominal demand remains visible even when explicitly disabled.
            }
            if (placed != null)
            {
                var row = state["power"]["grants"].Items.SingleOrDefault(x => x["id"].Text == placed["id"].Text);
                grant = demand == 0 ? 0 : row == null ? (long?)null : row["value"].Integer;
                if (receipt.Accepted && placed["enabled"].Boolean && demand > 0 && (!grant.HasValue || grant.Value < demand)) reasons.Add("POWER_UNAVAILABLE");
            }
            else if (demand == 0) grant = 0;
            yield return "explanation.command";
            long waiting = 0;
            foreach (var queued in state["queues"].Items) { if (queued["kind"].Text == "capacity" || queued["kind"].Text == "berth") waiting++; yield return "explanation.queue"; }
            if (waiting > 0) reasons.Add("CAPACITY_WAIT");
            bool knownRisk = false;
            foreach (var region in regions.Values) { knownRisk |= region.FirstUnsafe.HasValue; yield return "explanation.region"; }
            if (knownRisk) reasons.Add("KNOWN_HAZARD_RISK");
            foreach (var route in routes.Values) { if (!route.Available) reasons.Add(route.Reason ?? "ROUTE_BLOCKED"); yield return "explanation.route"; }
            string classification;
            if (receipt.Status == "rejected") { classification = "invalid"; reasons.Insert(0, receipt.ReasonCode); }
            else { classification = reasons.Count > 0 ? "risky" : "valid"; if (reasons.Count == 0) reasons.Add("OK"); }
            complete(new PreviewExplanation(classification, reasons, cost, source["supplies_remaining"].Integer,
                state["supplies_remaining"].Integer, seats, slots, stock, demand, grant, waiting));
        }
    }
}
