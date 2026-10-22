using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;

namespace Resq.Core
{
    // Closed typed registry from frozen03 section4. The sink owns event IDs and
    // ordinal; callers supply actual causes and values, never arbitrary prose.
    public sealed class RuntimeEvent
    {
        private readonly JsonValue value;
        internal RuntimeEvent(long operationTick, long scenarioTick, int ordinal, string kind, string subjectId, string causeCommandId, string causeEventId, JsonValue payload)
        {
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
            string[] fields;
            switch (kind)
            {
                case "CommandResolved": fields = new[] { "sequence", "status", "reason_code" }; break;
                case "EquipmentPlaced": case "EquipmentMoved": case "EquipmentBroken": fields = new[] { "equipment_instance_id", "socket_id" }; break;
                case "PowerGrantChanged": fields = new[] { "consumer_id", "old_grant", "new_grant" }; break;
                case "WaterTransfer": fields = new[] { "request_id", "source_id", "destination_id", "granted_mm3" }; break;
                case "HazardWarning": fields = new[] { "hazard", "region_id", "effect_scenario_tick" }; break;
                case "HazardStateChanged": fields = new[] { "hazard", "subject_id", "old_state", "new_state" }; break;
                case "TransferStarted": case "TransferCompleted": case "TransferCancelled": fields = new[] { "resident_id", "vehicle_id", "node_id", "direction" }; break;
                case "RouteBlocked": fields = new[] { "subject_id", "edge_id", "reason_code" }; break;
                case "ResidentEvacuated": fields = new[] { "resident_id", "node_id", "final_leg_mode" }; break;
                case "ResidentAwaitingBackup": fields = new[] { "resident_id", "location" }; break;
                case "MilestoneReached": fields = new[] { "milestone_id" }; break;
                case "ObjectiveUpdated": fields = new[] { "objective_id", "fulfilled" }; break;
                case "OutcomeResolved": fields = new[] { "success", "stars", "score" }; break;
                default: throw new InvalidOperationException("Unknown runtime event kind: " + kind);
            }
            if (payload.Kind != "object" || payload.Members.Count != fields.Length || fields.Any(field => !payload.Has(field))) throw new InvalidOperationException("Closed runtime event payload required");
            foreach (var pair in payload.Members)
            {
                if (pair.Key == "fulfilled" || pair.Key == "success") { if (pair.Value.Kind != "boolean") throw new InvalidOperationException("Runtime event bool required"); }
                else if (pair.Key == "sequence" || pair.Key == "old_grant" || pair.Key == "new_grant" || pair.Key == "granted_mm3" || pair.Key == "effect_scenario_tick" || pair.Key == "stars" || pair.Key == "score")
                {
                    if (pair.Value.Kind != "integer" || pair.Value.Integer < (pair.Key == "sequence" ? 1 : 0)) throw new InvalidOperationException("Runtime event integer required");
                    long maximum = pair.Key == "sequence" ? 10000 : pair.Key == "stars" ? 3 : pair.Key == "score" ? 10000 : pair.Key == "effect_scenario_tick" ? Tick.Maximum : long.MaxValue;
                    if (pair.Value.Integer > maximum) throw new InvalidOperationException("Runtime event integer bound");
                }
                else if (pair.Key != "location") Id(pair.Value.Text);
            }
            if (kind == "CommandResolved")
            {
                string status = payload["status"].Text, reason = payload["reason_code"].Text;
                if (status != "accepted" && status != "rejected" || status == "accepted" && reason != "OK" || status == "rejected" && new ValidationResult(reason).Allowed) throw new InvalidOperationException("Final CommandResolved receipt required");
            }
            if (kind == "HazardWarning" || kind == "HazardStateChanged") One(payload["hazard"].Text, "water", "fire", "stability", "runout", "tide", "lava", "power", "resource");
            if (kind == "TransferStarted" || kind == "TransferCompleted" || kind == "TransferCancelled") One(payload["direction"].Text, "board", "unboard");
            if (kind == "ResidentEvacuated") One(payload["final_leg_mode"].Text, "ground", "water", "cable");
            if (kind == "ResidentAwaitingBackup")
            {
                var location = payload["location"]; string tag = location["kind"].Text;
                string field = tag == "node" ? "node_id" : tag == "edge" ? "transit_id" : tag == "vehicle" ? "vehicle_id" : throw new InvalidOperationException("Resident location kind");
                if (location.Members.Count != 2 || !location.Has(field)) throw new InvalidOperationException("Resident location fields"); Id(location[field].Text);
            }
            if (subjectId != null) Id(subjectId); if (causeCommandId != null) Id(causeCommandId); if (causeEventId != null) Id(causeEventId);
            new Tick(operationTick); new Tick(scenarioTick);
            value = JsonValue.Object(new Dictionary<string, JsonValue> {
                {"event_id",JsonValue.String("evt:" + operationTick.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture))},
                {"operation_tick",JsonValue.Number(operationTick)},{"scenario_tick",JsonValue.Number(scenarioTick)},{"ordinal",JsonValue.Number(ordinal)},{"kind",JsonValue.String(kind)},
                {"subject_id",subjectId == null ? JsonValue.Null() : JsonValue.String(subjectId)},{"cause_command_id",causeCommandId == null ? JsonValue.Null() : JsonValue.String(causeCommandId)},
                {"cause_event_id",causeEventId == null ? JsonValue.Null() : JsonValue.String(causeEventId)},{"payload",payload} });
        }
        private static void Id(string id) { if (!Regex.IsMatch(id, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Runtime event ID"); }
        private static void One(string value, params string[] allowed) { if (!allowed.Contains(value, StringComparer.Ordinal)) throw new InvalidOperationException("Runtime event enum"); }
        public static RuntimeEvent Read(JsonValue value)
        {
            string[] fields = { "event_id", "operation_tick", "scenario_tick", "ordinal", "kind", "subject_id", "cause_command_id", "cause_event_id", "payload" };
            if (value == null || value.Kind != "object" || value.Members.Count != fields.Length || fields.Any(x => !value.Has(x))) throw new InvalidOperationException("Invalid saved runtime event fields");
            long ordinal = value["ordinal"].Integer;
            if (ordinal < 0 || ordinal >= int.MaxValue) throw new InvalidOperationException("Invalid saved runtime event ordinal");
            var restored = new RuntimeEvent(value["operation_tick"].Integer, value["scenario_tick"].Integer, (int)ordinal, value["kind"].Text,
                value["subject_id"].IsNull ? null : value["subject_id"].Text,
                value["cause_command_id"].IsNull ? null : value["cause_command_id"].Text,
                value["cause_event_id"].IsNull ? null : value["cause_event_id"].Text, value["payload"]);
            if (restored.value["event_id"].Text != value["event_id"].Text) throw new InvalidOperationException("Runtime event ID disagrees with boundary/ordinal");
            return restored;
        }
        public JsonValue ToJson() => value;
    }
    public sealed class RuntimeEventSink
    {
        private readonly List<RuntimeEvent> recent = new List<RuntimeEvent>();
        private readonly List<RuntimeEvent> current = new List<RuntimeEvent>();
        private long operationTick = -1, scenarioTick;
        private int ordinal;
        public ReadOnlyCollection<RuntimeEvent> Recent => recent.ToList().AsReadOnly();
        public ReadOnlyCollection<RuntimeEvent> Current => current.ToList().AsReadOnly();
        public void BeginBoundary(long operationTick, long scenarioTick)
        {
            if (operationTick != this.operationTick) ordinal = 0;
            this.operationTick = operationTick; this.scenarioTick = scenarioTick; current.Clear();
        }
        public RuntimeEvent Emit(string kind, string subjectId, string causeCommandId, string causeEventId, JsonValue payload)
        {
            if (operationTick < 0) throw new InvalidOperationException("Event sink has no boundary");
            var entry = new RuntimeEvent(operationTick, scenarioTick, ordinal, kind, subjectId, causeCommandId, causeEventId, payload);
            ordinal = checked(ordinal + 1); recent.Add(entry); current.Add(entry); if (recent.Count > 256) recent.RemoveAt(0); return entry;
        }
        public RuntimeEventSink Fork() { var copy = new RuntimeEventSink { operationTick = operationTick, scenarioTick = scenarioTick, ordinal = ordinal }; copy.recent.AddRange(recent); copy.current.AddRange(current); return copy; }
        public static RuntimeEventSink Restore(JsonValue savedEvents, long operationTick, long scenarioTick)
            => RestoreWork(savedEvents, operationTick, scenarioTick).Drain();
        public static ResumableWork<RuntimeEventSink> RestoreWork(JsonValue savedEvents, long operationTick, long scenarioTick)
            => new ResumableWork<RuntimeEventSink>(done => RestoreSteps(savedEvents, operationTick, scenarioTick, done));
        private static IEnumerable<string> RestoreSteps(JsonValue savedEvents, long operationTick, long scenarioTick, Action<RuntimeEventSink> complete)
        {
            new Tick(operationTick); new Tick(scenarioTick);
            if (savedEvents.Kind != "array" || savedEvents.Items.Count > 256) throw new InvalidOperationException("Saved recent event bound");
            var copy = new RuntimeEventSink { operationTick = operationTick, scenarioTick = scenarioTick };
            JsonValue previous = null;
            foreach (var row in savedEvents.Items)
            {
                var restored = RuntimeEvent.Read(row); long o = row["operation_tick"].Integer, s = row["scenario_tick"].Integer, n = row["ordinal"].Integer;
                if (o > operationTick || s > scenarioTick || s > o) throw new InvalidOperationException("Saved runtime event lies beyond boundary");
                if (previous == null)
                {
                    if (savedEvents.Items.Count < 256 && n != 0) throw new InvalidOperationException("Uncapped recent events lost the first ordinal");
                }
                else
                {
                    long priorO = previous["operation_tick"].Integer, priorS = previous["scenario_tick"].Integer;
                    if (o < priorO || s < priorS || (o == priorO ? s != priorS || n != previous["ordinal"].Integer + 1 : n != 0)) throw new InvalidOperationException("Saved runtime event order/gap");
                }
                copy.recent.Add(restored); previous = row;
                yield return "restore.event";
            }
            if (previous != null && previous["operation_tick"].Integer == operationTick) copy.ordinal = checked((int)previous["ordinal"].Integer + 1);
            // Diagnostics remain intact, but a load never republishes the prior
            // boundary's effects as new Current events.
            complete(copy);
        }
    }
}
