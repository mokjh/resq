using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;

namespace Resq.Persistence
{
    // Closed wire validation precedes owner reconstruction. Each real owner then
    // checks content references, conservation and its derived boundary values.
    public static class SnapshotContract
    {
        public const int MaximumBytes = 16 * 1024 * 1024;
        public static void Fields(JsonValue value, params string[] names)
        {
            if (value == null || value.Kind != "object" || value.Members.Count != names.Length || names.Any(n => !value.Has(n)))
                throw new InvalidOperationException("Closed snapshot object required: " + String.Join(",", names));
        }
        public static void Number(JsonValue value, long minimum, long maximum)
        { if (value.Kind != "integer" || value.Integer < minimum || value.Integer > maximum) throw new InvalidOperationException("Snapshot integer bound"); }
        public static void Id(JsonValue value)
        { if (value.Kind != "string" || !Regex.IsMatch(value.Text, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Snapshot identifier"); }
        public static void Hash(JsonValue value)
        { if (value.Kind != "string" || !Regex.IsMatch(value.Text, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Snapshot hash"); }
        private static void Primitive(JsonValue value, string type)
        {
            if (type.StartsWith("?", StringComparison.Ordinal)) { if (value.IsNull) return; type = type.Substring(1); }
            switch (type)
            {
                case "id": Id(value); break;
                case "hash": Hash(value); break;
                case "b": if (value.Kind != "boolean") throw new InvalidOperationException("Snapshot boolean"); break;
                case "t": Number(value, 0, Tick.Maximum); break;
                case "c": Number(value, 0, int.MaxValue); break;
                case "q": Number(value, 0, long.MaxValue); break;
                case "s": Number(value, long.MinValue, long.MaxValue); break;
                case "ids": Ids(value); break;
                default:
                    if (type.StartsWith("#", StringComparison.Ordinal))
                    { var bounds = type.Substring(1).Split(':'); Number(value, long.Parse(bounds[0], System.Globalization.CultureInfo.InvariantCulture), long.Parse(bounds[1], System.Globalization.CultureInfo.InvariantCulture)); }
                    else if (value.Kind != "string" || !type.Split('|').Contains(value.Text)) throw new InvalidOperationException("Snapshot enum: " + type);
                    break;
            }
        }
        private static void Record(JsonValue value, string declaration)
        {
            var fields = declaration.Split(' '); Fields(value, fields.Select(f => f.Substring(0, f.IndexOf('='))).ToArray());
            foreach (string field in fields) { int split = field.IndexOf('='); Primitive(value[field.Substring(0, split)], field.Substring(split + 1)); }
        }
        public static void Ids(JsonValue value)
        {
            if (value.Kind != "array") throw new InvalidOperationException("Snapshot ID array");
            foreach (var id in value.Items) Id(id);
            if (!value.Items.Select(x => x.Text).SequenceEqual(value.Items.Select(x => x.Text).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))) throw new InvalidOperationException("Snapshot IDs must be unique ordinal order");
        }
        private static void Rows(JsonValue value, Action<JsonValue> validate, int maximum = 16384, string key = "id")
        {
            if (value.Kind != "array" || value.Items.Count > maximum) throw new InvalidOperationException("Snapshot collection bound");
            foreach (var row in value.Items) validate(row);
            if (key != null) Ids(JsonValue.Array(value.Items.Select(x => x[key])));
        }
        private static void Rows(JsonValue root, string field, string declaration, int maximum = 16384, string key = "id")
            => Rows(root[field], row => Record(row, declaration), maximum, key);
        private static IEnumerable<string> RowsWork(JsonValue value, Action<JsonValue> validate, int maximum = 16384, string key = "id")
        {
            if (value.Kind != "array" || value.Items.Count > maximum) throw new InvalidOperationException("Snapshot collection bound");
            foreach (var row in value.Items) { validate(row); yield return "restore.schema.row"; }
            if (key != null)
            {
                string previous = null;
                foreach (var row in value.Items)
                {
                    var id = row[key]; Id(id);
                    if (previous != null && StringComparer.Ordinal.Compare(previous, id.Text) >= 0) throw new InvalidOperationException("Snapshot IDs must be unique ordinal order");
                    previous = id.Text; yield return "restore.schema.identity";
                }
            }
        }
        private static IEnumerable<string> RowsWork(JsonValue root, string field, string declaration, int maximum = 16384, string key = "id")
            => RowsWork(root[field], row => Record(row, declaration), maximum, key);
        private static void Location(JsonValue value, bool resident)
        {
            string kind = value["kind"].Text;
            if (kind == "node") Record(value, "kind=node node_id=id");
            else if (kind == "edge") Record(value, "kind=edge transit_id=id");
            else if (resident && kind == "vehicle") Record(value, "kind=vehicle vehicle_id=id");
            else throw new InvalidOperationException("Snapshot location union");
        }
        public static void Validate(JsonValue state) => ValidateWork(state).Drain();
        public static ResumableWork<bool> ValidateWork(JsonValue state)
            => new ResumableWork<bool>(done => ValidateSteps(state, done));
        private static IEnumerable<string> ValidateSteps(JsonValue state, Action<bool> complete)
        {
            Fields(state, "schema_version", "identity", "state_revision", "phase", "operation_tick", "scenario_tick", "scenario_accumulator", "scenario_enabled", "guidance_state", "rng", "supplies_remaining", "selected_stock", "deployments", "water", "rate_remainders", "power", "equipment", "vehicles", "residents", "transits", "transfers", "reservations", "routes", "queues", "fire", "supports", "runouts", "tides", "lava", "stores", "producers", "event_state", "milestones", "metrics", "objective_monitors", "last_admitted_sequence", "pending_commands", "command_receipts", "recent_events", "water_ledger", "end_request", "outcome");
            Number(state["schema_version"], 1, 1); RunIdentity.Validate(state["identity"]);
            Number(state["state_revision"], 0, int.MaxValue); Number(state["operation_tick"], 0, Tick.Maximum); Number(state["scenario_tick"], 0, Tick.Maximum);
            Primitive(state["phase"], "planning|active|stabilizing|outcome"); Primitive(state["scenario_enabled"], "b"); Number(state["scenario_accumulator"], 0, 1);
            Record(state["guidance_state"], "policy=id step_id=id completed_step_ids=ids acknowledged_warning=b");
            if (state["guidance_state"]["policy"].Text != state["identity"]["guidance_policy"].Text) throw new InvalidOperationException("Guidance identity mismatch");
            Record(state["rng"], "algorithm=xorshift32-v1 state=#1:4294967295 draws=q");
            Number(state["supplies_remaining"], 0, int.MaxValue);
            foreach (var unit in RowsWork(state, "selected_stock", "equipment_id=id count=c", 14, "equipment_id")) yield return unit; foreach (var unit in RowsWork(state, "deployments", "equipment_id=id count=c", 14, "equipment_id")) yield return unit;
            foreach (var unit in RowsWork(state, "water", "id=id volume_mm3=#0:1000000000000", 4096)) yield return unit;
            foreach (var unit in RowsWork(state, "rate_remainders", "id=id kind=source|sink|sprinkler remainder=#0:9 current_rate_mm3_per_second=#0:1000000000")) yield return unit;
            Fields(state["power"], "node_enabled", "link_enabled", "consumer_demands", "priorities", "grants", "generator_trip_ids");
            foreach (var field in new[] { "node_enabled", "link_enabled" }) foreach (var unit in RowsWork(state["power"], field, "id=id value=b")) yield return unit;
            foreach (var field in new[] { "consumer_demands", "priorities", "grants" }) foreach (var unit in RowsWork(state["power"], field, "id=id value=c")) yield return unit;
            Ids(state["power"]["generator_trip_ids"]);
            foreach (var unit in RowsWork(state["equipment"], row => {
                Fields(row, "id", "equipment_id", "socket_id", "orientation_step", "lifecycle", "enabled", "durability", "parameters");
                Record(row.Without("parameters"), "id=id equipment_id=id socket_id=id orientation_step=#0:3 lifecycle=deployed|broken|exhausted enabled=b durability=c");
                string kind = row["equipment_id"].Text; var p = row["parameters"];
                switch (kind) {
                    case "pump": Record(p, "fractional_remainder=#0:49999"); break;
                    case "generator": Record(p, "trip_latched=b"); break;
                    case "firebreak": Record(p, "activated=b"); break;
                    case "cooler": Record(p, "charges=#0:1 active_segment_id=?id"); break;
                    case "shelter": Record(p, "protection_remaining=#0:600 exhaustion_emitted=b"); break;
                    case "barrier": case "bridge": case "beacon": case "brace": case "relay": case "skiff": case "crawler": case "sprinkler": case "winch": Fields(p); break;
                    default: throw new InvalidOperationException("Unknown saved equipment kind");
                }
            }, 512)) yield return unit;
            foreach (var unit in RowsWork(state["vehicles"], row => {
                Fields(row, "id", "equipment_instance_id", "kind", "location", "occupant_ids", "route_order_id", "status", "duty_used", "rest_remaining", "brake_remaining");
                Record(row.Without("location"), "id=id equipment_instance_id=?id kind=fixed_bus|skiff|crawler|winch occupant_ids=ids route_order_id=?id status=idle|waiting_route|boarding|travelling|unboarding|waiting_power|braked|returning|resting|failed duty_used=c rest_remaining=c brake_remaining=c"); Location(row["location"], false);
            }, 32)) yield return unit;
            foreach (var unit in RowsWork(state["residents"], row => {
                Fields(row, "id", "group_id", "state", "location", "reservation_ids", "node_entered_scenario_tick", "consecutive_exposure_ticks", "final_leg_mode");
                Record(row.Without("location"), "id=id group_id=id state=sheltered|waiting|walking|boarding|in_transit|disembarking|evacuated|awaiting_backup reservation_ids=ids node_entered_scenario_tick=?t consecutive_exposure_ticks=c final_leg_mode=?ground|water|cable"); Location(row["location"], true);
            }, 256)) yield return unit;
            foreach (var unit in RowsWork(state, "transits", "id=id mover_kind=resident|vehicle mover_id=id edge_id=id origin_node_id=id destination_node_id=id duration_operation_ticks=#1:2147483647 progress_operation_ticks=c mode=forward|return|backup remaining_return_ticks=c closure_latched=b", 288)) yield return unit;
            foreach (var unit in RowsWork(state, "transfers", "id=id resident_id=id vehicle_id=id node_id=id direction=board|unboard progress_operation_ticks=#0:9 seat_reservation_id=id node_reservation_id=?id", 256)) yield return unit;
            foreach (var unit in RowsWork(state, "reservations", "id=id owner_id=id resource_kind=edge_slots|node_people|vehicle_seat|dock_berth|return_node resource_id=id quantity=#1:2147483647 created_operation_tick=t")) yield return unit;
            foreach (var unit in RowsWork(state["routes"], row => {
                Fields(row, "id", "subject_kind", "subject_id", "stops", "next_stop_index", "resolved_edge_ids", "submitted_operation_tick", "submitted_sequence");
                Record(row.Without("stops", "resolved_edge_ids"), "id=id subject_kind=group|vehicle subject_id=id next_stop_index=c submitted_operation_tick=t submitted_sequence=c");
                Rows(row["stops"], stop => Record(stop, "node_id=id action=assemble|pickup|dropoff|evacuate group_id=?id"), 16, null);
                if (row["stops"].Items.Count == 0 || row["next_stop_index"].Integer > row["stops"].Items.Count) throw new InvalidOperationException("Route stop index");
                foreach (var id in row["resolved_edge_ids"].Items) Id(id);
            }, 10000)) yield return unit;
            foreach (var unit in RowsWork(state, "queues", "id=id subject_id=id target_node_id=id kind=berth|capacity|edge|power since_operation_tick=t submitted_sequence=c")) yield return unit;
            foreach (var unit in RowsWork(state, "fire", "id=id fuel=#0:1000 heat=#0:1000 wetness=#0:1000 state=unburned|heating|burning|cooling|safe_ash warning_started_tick=?t cooling_started_tick=?t admission_closed=b spray_remainder_mm3=#0:24999 extra_cooling_points_per_pulse=c", 4096)) yield return unit;
            foreach (var unit in RowsWork(state, "supports", "id=id base_stability=#-1000:1000 static_load_points=#0:1000 loss_points_per_second=#0:100 loss_remainder=#0:9 warning_started_tick=?t collapse_pending=b collapsed=b")) yield return unit;
            foreach (var unit in RowsWork(state["runouts"], row => { Fields(row, "id", "activated", "segments"); Id(row["id"]); Primitive(row["activated"], "b"); Rows(row, "segments", "id=id arrival_tick=?t clearing_tick=?t state=dormant|moving|settling|cleared"); })) yield return unit;
            foreach (var unit in RowsWork(state, "tides", "id=id ramp_start_tick=t ramp_start_level_mm=s ramp_end_tick=t ramp_end_level_mm=s current_level_mm=s")) yield return unit;
            foreach (var unit in RowsWork(state, "lava", "id=id state=dormant|hot|cooling|crusted|reheating arrival_tick=?t cooling_complete_tick=?t crust_expiry_tick=?t hot_again_tick=?t ever_cooled=b cooler_instance_id=?id")) yield return unit;
            foreach (var unit in RowsWork(state, "stores", "id=id unit=coolant_charge|coolant_feedstock amount=q")) yield return unit;
            foreach (var unit in RowsWork(state, "producers", "id=id work_in_progress_units=#0:1 completed_work_scenario_ticks=#0:99 reserved_input_store_id=?id")) yield return unit;
            var es = state["event_state"];
            Fields(es, "next_static_event_index", "fired_event_ids", "revealed_event_ids", "warned_event_ids", "dynamic_events", "administrative_edge_enabled", "edge_current_multipliers", "gate_open", "group_held", "wind_region_codes", "cooler_socket_enabled", "shelter_region_enabled", "resource_warning_thresholds");
            Primitive(es["next_static_event_index"], "c"); foreach (var f in new[] { "fired_event_ids", "revealed_event_ids", "warned_event_ids" }) Ids(es[f]);
            foreach (var f in new[] { "administrative_edge_enabled", "gate_open", "group_held", "cooler_socket_enabled", "shelter_region_enabled" }) foreach (var unit in RowsWork(es, f, "id=id value=b")) yield return unit;
            foreach (var f in new[] { "edge_current_multipliers", "wind_region_codes", "resource_warning_thresholds" }) foreach (var unit in RowsWork(es, f, "id=id value=c")) yield return unit;
            foreach (var unit in RowsWork(es, "dynamic_events", "id=id at_scenario_tick=t kind=runout_arrival|runout_clear|lava_arrival target_id=id cause_event_id=id", 16384, null)) yield return unit;
            var dynamics = es["dynamic_events"].Items; if (!dynamics.SequenceEqual(dynamics.OrderBy(x => x["at_scenario_tick"].Integer).ThenBy(x => x["id"].Text, StringComparer.Ordinal)) || dynamics.Select(x => x["id"].Text).Distinct(StringComparer.Ordinal).Count() != dynamics.Count) throw new InvalidOperationException("Dynamic event order/identity");
            foreach (var unit in RowsWork(state, "milestones", "id=id operation_tick=t scenario_tick=t")) yield return unit;
            foreach (var unit in RowsWork(state, "metrics", "id=id metric=id subject_id=?id value=q distinct_resident_ids=ids")) yield return unit;
            foreach (var unit in RowsWork(state, "objective_monitors", "id=id started_operation_tick=?t ended_operation_tick=?t first_true_operation_tick=?t first_true_scenario_tick=?t violated=b current_hold_scenario_ticks=c fulfilled=b")) yield return unit;
            Number(state["last_admitted_sequence"], 0, 10000);
            foreach (var unit in RowsWork(state["pending_commands"], x => GameCommand.Read(x), 32, null)) yield return unit;
            foreach (var unit in RowsWork(state["command_receipts"], x => { Fields(x, "command_id", "sequence", "request_sha256", "result"); Id(x["command_id"]); Number(x["sequence"], 1, 10000); Hash(x["request_sha256"]); Record(x["result"], "command_id=id sequence=c status=queued|accepted|rejected accepted=b reason_code=id executed_tick=#-1:1000000 affected_ids=ids state_revision=c"); }, 10000, null)) yield return unit;
            foreach (var unit in RowsWork(state["recent_events"], x => RuntimeEvent.Read(x), 256, null)) yield return unit;
            Record(state["water_ledger"], "initial_total_mm3=q direct_sources_mm3=q sea_in_mm3=q drains_mm3=q sea_out_mm3=q sprayed_mm3=q");
            if (!state["end_request"].IsNull) Record(state["end_request"], "command_id=id confirm_failure=b");
            if (!state["outcome"].IsNull) Record(state["outcome"], "success=b stars=#0:3 score=#0:10000 rescued=c backed_up=c reason=success|deadline_unmet|player_ended_unmet");
            if ((state["phase"].Text == "outcome") != !state["outcome"].IsNull) throw new InvalidOperationException("Outcome phase mismatch");
            using (var size = state.CanonicalByteCountWork())
            {
                while (size.MoveNext()) yield return size.Stage;
                if (size.Result > MaximumBytes) throw new InvalidOperationException("Snapshot exceeds 16MiB");
            }
            complete(true);
        }
    }
}
