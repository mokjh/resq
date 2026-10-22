using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;

namespace Resq.Core
{
    public sealed class CommandException : Exception
    {
        public string Code { get; }
        public CommandException(string code, string detail) : base(code + ": " + detail) { Code = code; }
    }

    // Immutable closed command union. A payload can only be constructed through the
    // strict reader; authoring/input DTOs never become mutable simulation authority.
    public abstract class CommandPayload
    {
        private readonly JsonValue value;
        protected CommandPayload(JsonValue value) { this.value = value; }
        public JsonValue ToJson() => value;
    }
    public sealed class PlaceEquipment : CommandPayload
    {
        internal PlaceEquipment(JsonValue value) : base(value) { }
        public string EquipmentId => ToJson()["equipment_id"].Text;
        public string SocketId => ToJson()["socket_id"].Text;
        public int OrientationStep => (int)ToJson()["orientation_step"].Integer;
    }
    public sealed class MoveIdleEquipment : CommandPayload
    {
        internal MoveIdleEquipment(JsonValue value) : base(value) { }
        public string EquipmentInstanceId => ToJson()["equipment_instance_id"].Text;
        public string DestinationSocketId => ToJson()["destination_socket_id"].Text;
        public int OrientationStep => (int)ToJson()["orientation_step"].Integer;
    }
    public sealed class RouteStop
    {
        public string NodeId { get; }
        public string Action { get; }
        public string GroupId { get; }
        internal RouteStop(JsonValue value) { NodeId = value["node_id"].Text; Action = value["action"].Text; GroupId = value["group_id"].IsNull ? null : value["group_id"].Text; }
    }
    public sealed class AssignRoute : CommandPayload
    {
        internal AssignRoute(JsonValue value) : base(value) { Stops = value["stops"].Items.Select(item => new RouteStop(item)).ToList().AsReadOnly(); }
        public string SubjectKind => ToJson()["subject_kind"].Text;
        public string SubjectId => ToJson()["subject_id"].Text;
        public ReadOnlyCollection<RouteStop> Stops { get; }
    }
    public sealed class SetPowerPriority : CommandPayload
    {
        internal SetPowerPriority(JsonValue value) : base(value) { ConsumerIds = value["consumer_ids"].Items.Select(item => item.Text).ToList().AsReadOnly(); }
        public ReadOnlyCollection<string> ConsumerIds { get; }
    }
    public sealed class SetGate : CommandPayload
    {
        internal SetGate(JsonValue value) : base(value) { }
        public string GateId => ToJson()["gate_id"].Text;
        public bool Open => ToJson()["open"].Boolean;
    }
    public sealed class ActivateTool : CommandPayload
    {
        internal ActivateTool(JsonValue value) : base(value) { }
        public string EquipmentInstanceId => ToJson()["equipment_instance_id"].Text;
        public string Action => ToJson()["action"].Text;
        public string SegmentId => Action == "cool_segment" ? ToJson()["segment_id"].Text : null;
        public string StoreId => Action == "refill" ? ToJson()["store_id"].Text : null;
    }
    public sealed class EndOperation : CommandPayload
    {
        internal EndOperation(JsonValue value) : base(value) { }
        public bool ConfirmFailure => ToJson()["confirm_failure"].Boolean;
    }
    public sealed class PauseOperation : CommandPayload
    {
        internal PauseOperation(JsonValue value) : base(value) { }
        public string Reason => ToJson()["reason"].Text;
    }
    public sealed class EmptyApplicationPayload : CommandPayload { internal EmptyApplicationPayload(JsonValue value) : base(value) { } }
    public sealed class AdvanceTicks : CommandPayload
    {
        internal AdvanceTicks(JsonValue value) : base(value) { }
        public int OperationTicks => (int)ToJson()["operation_ticks"].Integer;
    }
    public sealed class ConfirmPlacement : CommandPayload
    {
        internal ConfirmPlacement(JsonValue value) : base(value) { }
        public string LayoutVersionId => ToJson()["layout_version_id"].Text;
        public string LayoutSignature => ToJson()["layout_signature"].Text;
    }
    public sealed class ExitToHub : CommandPayload
    {
        internal ExitToHub(JsonValue value) : base(value) { }
        public string Disposition => ToJson()["disposition"].Text;
    }

    public sealed class GameCommand
    {
        private readonly JsonValue value;
        public string CommandId => value["command_id"].Text;
        public string RunId => value["run_id"].Text;
        public long Sequence => value["sequence"].Integer;
        public long RequestedTick => value["requested_tick"].Integer;
        public string ActorInput => value["actor_input"].Text;
        public string Kind => value["kind"].Text;
        public CommandPayload Payload { get; }
        public bool IsApplication => Kind == "pause" || Kind == "resume" || Kind == "advance_ticks" || Kind == "enter_placement" || Kind == "confirm_placement" || Kind == "exit_to_hub";
        public string RequestSha256 { get; }
        private GameCommand(JsonValue value, CommandPayload payload) { this.value = value; Payload = payload; RequestSha256 = value.Sha256(); }
        public JsonValue ToJson() => value;
        public static GameCommand Parse(byte[] bytes) { try { return Read(StrictJson.Parse(bytes)); } catch (ContentException error) { throw new CommandException("INVALID_SCHEMA", error.Message); } }
        public static GameCommand Parse(string text) { try { return Read(StrictJson.Parse(text)); } catch (ContentException error) { throw new CommandException("INVALID_SCHEMA", error.Message); } }
        public static GameCommand Read(JsonValue value)
        {
            try
            {
                Fields(value, "schema_version", "command_id", "run_id", "sequence", "requested_tick", "actor_input", "kind", "payload");
                Number(value["schema_version"], 1, 1); Id(value["command_id"]); Id(value["run_id"]);
                Number(value["sequence"], 1, 10000); Number(value["requested_tick"], 0, Tick.Maximum);
                One(value["actor_input"], "hands_left", "hands_right", "controller_left", "controller_right", "head_dwell", "reference_replay", "system_tutorial");
                string kind = value["kind"].Text; JsonValue p = value["payload"]; CommandPayload payload;
                switch (kind)
                {
                    case "place_equipment": Fields(p, "equipment_id", "socket_id", "orientation_step"); Id(p["equipment_id"]); Id(p["socket_id"]); Number(p["orientation_step"], 0, 3); payload = new PlaceEquipment(p); break;
                    case "move_idle_equipment": Fields(p, "equipment_instance_id", "destination_socket_id", "orientation_step"); Id(p["equipment_instance_id"]); Id(p["destination_socket_id"]); Number(p["orientation_step"], 0, 3); payload = new MoveIdleEquipment(p); break;
                    case "assign_route":
                        Fields(p, "subject_kind", "subject_id", "stops"); One(p["subject_kind"], "group", "vehicle"); Id(p["subject_id"]); Count(p["stops"], 1, 16);
                        foreach (var stop in p["stops"].Items)
                        {
                            Fields(stop, "node_id", "action", "group_id"); Id(stop["node_id"]);
                            string action = stop["action"].Text;
                            if (p["subject_kind"].Text == "group") { One(stop["action"], "assemble", "evacuate"); Null(stop["group_id"]); }
                            else { One(stop["action"], "pickup", "dropoff", "evacuate"); if (action == "pickup") Id(stop["group_id"]); else Null(stop["group_id"]); }
                        }
                        payload = new AssignRoute(p); break;
                    case "set_power_priority": Fields(p, "consumer_ids"); Count(p["consumer_ids"], 0, 4096); foreach (var item in p["consumer_ids"].Items) Id(item); if (p["consumer_ids"].Items.Select(item => item.Text).Distinct(StringComparer.Ordinal).Count() != p["consumer_ids"].Items.Count) Bad("duplicate consumer"); payload = new SetPowerPriority(p); break;
                    case "set_gate": Fields(p, "gate_id", "open"); Id(p["gate_id"]); Bool(p["open"]); payload = new SetGate(p); break;
                    case "activate_tool":
                        string actionName = p["action"].Text;
                        One(p["action"], "enable", "disable", "firebreak", "cool_segment", "refill");
                        if (actionName == "cool_segment") { Fields(p, "equipment_instance_id", "action", "segment_id"); Id(p["segment_id"]); }
                        else if (actionName == "refill") { Fields(p, "equipment_instance_id", "action", "store_id"); Id(p["store_id"]); }
                        else Fields(p, "equipment_instance_id", "action");
                        Id(p["equipment_instance_id"]); payload = new ActivateTool(p); break;
                    case "end_operation": Fields(p, "confirm_failure"); Bool(p["confirm_failure"]); payload = new EndOperation(p); break;
                    case "pause": Fields(p, "reason"); One(p["reason"], "user", "tracking", "anchor", "platform"); payload = new PauseOperation(p); break;
                    case "resume": case "enter_placement": Fields(p); payload = new EmptyApplicationPayload(p); break;
                    case "advance_ticks": Fields(p, "operation_ticks"); Number(p["operation_ticks"], 10, 50); if (p["operation_ticks"].Integer != 10 && p["operation_ticks"].Integer != 50) Bad("advance ticks must be 10 or 50"); payload = new AdvanceTicks(p); break;
                    case "confirm_placement": Fields(p, "layout_version_id", "layout_signature"); Id(p["layout_version_id"]); if (!Regex.IsMatch(p["layout_signature"].Text, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)) Bad("layout hash"); payload = new ConfirmPlacement(p); break;
                    case "exit_to_hub": Fields(p, "disposition"); One(p["disposition"], "save_and_exit", "abandon"); payload = new ExitToHub(p); break;
                    default: throw new CommandException("UNKNOWN_KIND", kind);
                }
                return new GameCommand(value, payload);
            }
            catch (ContentException error) { throw new CommandException("INVALID_SCHEMA", error.Message); }
        }
        private static void Bad(string detail) { throw new CommandException("INVALID_SCHEMA", detail); }
        private static void Fields(JsonValue value, params string[] keys) { if (value.Kind != "object" || value.Members.Count != keys.Length || keys.Any(key => !value.Has(key))) Bad("closed object fields"); }
        private static void Number(JsonValue value, long minimum, long maximum) { if (value.Kind != "integer" || value.Integer < minimum || value.Integer > maximum) Bad("integer range"); }
        private static void Count(JsonValue value, int minimum, int maximum) { if (value.Kind != "array" || value.Items.Count < minimum || value.Items.Count > maximum) Bad("array bound"); }
        private static void One(JsonValue value, params string[] choices) { if (value.Kind != "string" || !choices.Contains(value.Text, StringComparer.Ordinal)) Bad("closed enum"); }
        private static void Id(JsonValue value) { if (value.Kind != "string" || !Regex.IsMatch(value.Text, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) Bad("ID"); }
        private static void Bool(JsonValue value) { if (value.Kind != "boolean") Bad("bool"); }
        private static void Null(JsonValue value) { if (!value.IsNull) Bad("null required"); }
    }

    public sealed class ValidationResult
    {
        public bool Allowed => ReasonCode == "OK";
        public string ReasonCode { get; }
        public ReadOnlyCollection<string> AffectedIds { get; }
        private static readonly string[] Rejections = { "INVALID_SCHEMA", "UNKNOWN_KIND", "CONTENT_VERSION_MISMATCH", "WRONG_RUN", "INVALID_SEQUENCE", "SEQUENCE_REUSED", "DUPLICATE_ID_MISMATCH", "INVALID_TICK", "INVALID_PHASE", "UNKNOWN_TARGET", "INSUFFICIENT_SUPPLIES", "STOCK_EXHAUSTED", "SOCKET_INCOMPATIBLE", "OCCUPIED", "NOT_IDLE", "UNSAFE_TARGET", "ROUTE_BLOCKED", "CAPACITY_FULL", "POWER_REQUIRED", "RESOURCE_EMPTY", "TRACKING_UNAVAILABLE", "OBJECTIVES_UNMET", "QUEUE_FULL", "COMMAND_LIMIT_REACHED" };
        public ValidationResult(string reasonCode, params string[] affectedIds)
        {
            if (reasonCode != "OK" && !Rejections.Contains(reasonCode, StringComparer.Ordinal)) throw new ArgumentException("Unknown rejection code", nameof(reasonCode));
            ReasonCode = reasonCode; AffectedIds = ((string[])affectedIds.Clone()).ToList().AsReadOnly();
        }
        public static ValidationResult First(params ValidationResult[] results) => results.Where(result => !result.Allowed).OrderBy(result => Array.IndexOf(Rejections, result.ReasonCode)).FirstOrDefault() ?? new ValidationResult("OK");
    }

    public sealed class CommandResult
    {
        public string CommandId { get; }
        public long Sequence { get; }
        public string Status { get; }
        public bool Accepted => Status == "accepted";
        public string ReasonCode { get; }
        public long ExecutedTick { get; }
        public long StateRevision { get; }
        public ReadOnlyCollection<string> AffectedIds { get; }
        public CommandResult(GameCommand command, string status, string reasonCode, long executedTick, long stateRevision, IEnumerable<string> affectedIds)
            : this(command.CommandId, command.Sequence, status, reasonCode, executedTick, stateRevision, affectedIds) { }
        private CommandResult(string commandId, long sequence, string status, string reasonCode, long executedTick, long stateRevision, IEnumerable<string> affectedIds)
        {
            if (status != "queued" && status != "accepted" && status != "rejected") throw new ArgumentException("status");
            if (status == "queued" ? reasonCode != "PENDING_EXECUTION" || executedTick != -1 : executedTick < 0 || (status == "accepted" ? reasonCode != "OK" : new ValidationResult(reasonCode).Allowed)) throw new ArgumentException("receipt coherence");
            CommandId = commandId; Sequence = sequence; Status = status; ReasonCode = reasonCode; ExecutedTick = executedTick; StateRevision = stateRevision; AffectedIds = affectedIds.ToList().AsReadOnly();
        }
        public static CommandResult Read(JsonValue value)
        {
            Fields(value, "command_id", "sequence", "status", "accepted", "reason_code", "executed_tick", "affected_ids", "state_revision");
            Id(value["command_id"]); Number(value["sequence"], 1, 10000); Number(value["executed_tick"], -1, Tick.Maximum); Number(value["state_revision"], 0, int.MaxValue);
            if (value["affected_ids"].Kind != "array") throw new InvalidOperationException("Receipt affected IDs must be an array");
            foreach (var id in value["affected_ids"].Items) Id(id);
            var result = new CommandResult(value["command_id"].Text, value["sequence"].Integer, value["status"].Text, value["reason_code"].Text, value["executed_tick"].Integer, value["state_revision"].Integer, value["affected_ids"].Items.Select(x => x.Text));
            if (value["accepted"].Boolean != result.Accepted) throw new InvalidOperationException("Receipt accepted flag disagrees with status");
            return result;
        }
        internal static void Fields(JsonValue value, params string[] fields) { if (value == null || value.Kind != "object" || value.Members.Count != fields.Length || fields.Any(x => !value.Has(x))) throw new InvalidOperationException("Invalid saved command closed fields"); }
        internal static void Number(JsonValue value, long minimum, long maximum) { if (value.Kind != "integer" || value.Integer < minimum || value.Integer > maximum) throw new InvalidOperationException("Invalid saved command integer"); }
        internal static void Id(JsonValue value) { if (value.Kind != "string" || !Regex.IsMatch(value.Text, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Invalid saved command ID"); }
        public JsonValue ToJson() => JsonValue.Object(new Dictionary<string, JsonValue> {
            {"command_id",JsonValue.String(CommandId)},{"sequence",JsonValue.Number(Sequence)},{"status",JsonValue.String(Status)},{"accepted",JsonValue.Bool(Accepted)},
            {"reason_code",JsonValue.String(ReasonCode)},{"executed_tick",JsonValue.Number(ExecutedTick)},{"affected_ids",JsonValue.Array(AffectedIds.Select(JsonValue.String))},{"state_revision",JsonValue.Number(StateRevision)} });
    }

    public sealed class CommandReceipt
    {
        // Completed command bodies are deliberately absent from the save format.
        // This is null for restored final receipts; never synthesize a command.
        public GameCommand Command { get; }
        public string CommandId { get; }
        public long Sequence { get; }
        public string RequestSha256 { get; }
        public CommandResult Result { get; }
        public CommandReceipt(GameCommand command, CommandResult result)
            : this(command.CommandId, command.Sequence, command.RequestSha256, result, command) { }
        private CommandReceipt(string commandId, long sequence, string requestSha256, CommandResult result, GameCommand command)
        {
            if (result.CommandId != commandId || result.Sequence != sequence) throw new InvalidOperationException("Receipt header/result mismatch");
            CommandId = commandId; Sequence = sequence; RequestSha256 = requestSha256; Result = result; Command = command;
        }
        internal static CommandReceipt Read(JsonValue value, GameCommand pendingCommand)
        {
            CommandResult.Fields(value, "command_id", "sequence", "request_sha256", "result"); CommandResult.Id(value["command_id"]); CommandResult.Number(value["sequence"], 1, 10000);
            if (value["request_sha256"].Kind != "string" || !Regex.IsMatch(value["request_sha256"].Text, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Invalid saved request hash");
            var result = CommandResult.Read(value["result"]);
            if ((result.Status == "queued") != (pendingCommand != null)) throw new InvalidOperationException("Saved pending/receipt status mismatch");
            if (pendingCommand != null && (pendingCommand.CommandId != value["command_id"].Text || pendingCommand.Sequence != value["sequence"].Integer || pendingCommand.RequestSha256 != value["request_sha256"].Text)) throw new InvalidOperationException("Saved pending command identity/hash mismatch");
            return new CommandReceipt(value["command_id"].Text, value["sequence"].Integer, value["request_sha256"].Text, result, pendingCommand);
        }
        public JsonValue ToJson() => JsonValue.Object(new Dictionary<string, JsonValue> { {"command_id",JsonValue.String(CommandId)}, {"sequence",JsonValue.Number(Sequence)}, {"request_sha256",JsonValue.String(RequestSha256)}, {"result",Result.ToJson()} });
    }

    // Only admitted envelopes occupy this ledger. Rejected malformed envelopes
    // have no cursor or history effect; an admitted preview rejection does.
    public sealed class CommandLedger
    {
        private readonly SortedDictionary<long, CommandReceipt> receipts = new SortedDictionary<long, CommandReceipt>();
        private readonly Dictionary<string, long> ids = new Dictionary<string, long>(StringComparer.Ordinal);
        public long LastAdmittedSequence { get; private set; }
        public ReadOnlyCollection<CommandReceipt> Receipts => receipts.Values.ToList().AsReadOnly();
        // Enumeration is private-owner work; consumers must not retain it across a mutation.
        public IEnumerable<CommandReceipt> EnumerateReceipts() => receipts.Values;
        public bool TryGetReceipt(string commandId, out CommandReceipt receipt)
        {
            receipt = null; if (!ids.TryGetValue(commandId, out long sequence)) return false; receipt = receipts[sequence]; return true;
        }
        public CommandResult FindDuplicate(GameCommand command, long tick, long revision)
        {
            if (TryGetReceipt(command.CommandId, out var prior)) return prior.RequestSha256 == command.RequestSha256 ? prior.Result : Reject(command, "DUPLICATE_ID_MISMATCH", tick, revision);
            if (receipts.ContainsKey(command.Sequence)) return Reject(command, "SEQUENCE_REUSED", tick, revision);
            return null;
        }
        public ValidationResult ValidateNew(GameCommand command, string runId)
        {
            if (command.RunId != runId) return new ValidationResult("WRONG_RUN");
            if (command.Sequence != LastAdmittedSequence + 1) return new ValidationResult("INVALID_SEQUENCE");
            if (receipts.Count >= 10000) return new ValidationResult("COMMAND_LIMIT_REACHED");
            return new ValidationResult("OK");
        }
        private void Add(CommandReceipt receipt)
        {
            if (receipt.Sequence != LastAdmittedSequence + 1 || ids.ContainsKey(receipt.CommandId)) throw new InvalidOperationException("ledger admission");
            LastAdmittedSequence = receipt.Sequence; receipts.Add(receipt.Sequence, receipt); ids.Add(receipt.CommandId, receipt.Sequence);
        }
        public void Admit(GameCommand command, CommandResult result) => Add(new CommandReceipt(command, result));
        public void Resolve(GameCommand command, CommandResult result)
        {
            if (!receipts.TryGetValue(command.Sequence, out var receipt) || receipt.RequestSha256 != command.RequestSha256 || receipt.Result.Status != "queued") throw new InvalidOperationException("ledger resolution");
            receipts[command.Sequence] = new CommandReceipt(command, result);
        }
        public static CommandLedger Restore(JsonValue savedReceipts, IEnumerable<GameCommand> pendingCommands, string runId, long lastAdmittedSequence, long operationTick, long stateRevision)
            => RestoreWork(savedReceipts, pendingCommands, runId, lastAdmittedSequence, operationTick, stateRevision).Drain();
        public static ResumableWork<CommandLedger> RestoreWork(JsonValue savedReceipts, IEnumerable<GameCommand> pendingCommands, string runId, long lastAdmittedSequence, long operationTick, long stateRevision)
            => new ResumableWork<CommandLedger>(done => RestoreSteps(savedReceipts, pendingCommands, runId, lastAdmittedSequence, operationTick, stateRevision, done));
        private static IEnumerable<string> RestoreSteps(JsonValue savedReceipts, IEnumerable<GameCommand> pendingCommands, string runId, long lastAdmittedSequence, long operationTick, long stateRevision, Action<CommandLedger> complete)
        {            new Tick(operationTick);
            // Every revision is one admission, completed operation step or
            // completed resume countdown. Each countdown requires an admission.
            if (savedReceipts.Kind != "array" || savedReceipts.Items.Count > 10000 || lastAdmittedSequence != savedReceipts.Items.Count || stateRevision < operationTick + lastAdmittedSequence || stateRevision > operationTick + 2 * lastAdmittedSequence) throw new InvalidOperationException("Saved receipt cursor/revision coherence");
            var waiting = pendingCommands.ToArray();
            if (waiting.Length > 32 || !waiting.Select(x => x.Sequence).SequenceEqual(waiting.Select(x => x.Sequence).OrderBy(x => x)) || waiting.Select(x => x.Sequence).Distinct().Count() != waiting.Length) throw new InvalidOperationException("Saved pending command order/bound");
            foreach (var command in waiting) if (command.IsApplication || command.RunId != runId || command.Sequence > lastAdmittedSequence || command.RequestedTick != operationTick + 1) throw new InvalidOperationException("Saved pending command boundary");
            var bySequence = waiting.ToDictionary(x => x.Sequence);
            var copy = new CommandLedger();
            foreach (var row in savedReceipts.Items)
            {
                CommandResult.Fields(row, "command_id", "sequence", "request_sha256", "result");
                bySequence.TryGetValue(row["sequence"].Integer, out var command);
                var receipt = CommandReceipt.Read(row, command); var result = receipt.Result;
                if (result.StateRevision < Math.Max(0, result.ExecutedTick) + receipt.Sequence || result.StateRevision > stateRevision || result.ExecutedTick > operationTick) throw new InvalidOperationException("Saved receipt execution/revision boundary");
                if (result.Status == "queued" && result.StateRevision < operationTick + receipt.Sequence) throw new InvalidOperationException("Saved pending admission revision");
                copy.Add(receipt);
                yield return "restore.receipt";
            }
            complete(copy);
        }
        public CommandLedger Clone() => CloneWork().Drain();
        public ResumableWork<CommandLedger> CloneWork() => new ResumableWork<CommandLedger>(CloneSteps);
        private IEnumerable<string> CloneSteps(Action<CommandLedger> complete)
        {
            var copy = new CommandLedger();
            foreach (var receipt in receipts.Values) { copy.Add(receipt); yield return "fork.receipt"; }
            complete(copy);
        }
        public static CommandResult Reject(GameCommand command, string reason, long tick, long revision) => new CommandResult(command, "rejected", reason, tick, revision, new string[0]);
    }
}
