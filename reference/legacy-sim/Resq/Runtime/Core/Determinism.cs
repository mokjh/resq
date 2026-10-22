using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;

namespace Resq.Core
{
    public static class RunIdentity
    {
        private static readonly string[] Fields = { "run_id", "source_build_id", "mission_version_id", "rules_version", "content_hash", "layout_version_id", "layout_signature", "rule_profile_id", "input_adapter_id", "guidance_policy", "challenge_id", "modifiers_hash", "seed", "assisted" };
        public static void Validate(JsonValue value)
        {
            if (value.Kind != "object" || value.Members.Count != Fields.Length || Fields.Any(field => !value.Has(field))) throw new InvalidOperationException("Invalid RunIdentity closed fields");
            foreach (string field in Fields)
            {
                if (field == "seed") { if (value[field].Kind != "integer") throw new InvalidOperationException("Invalid seed"); }
                else if (field == "assisted") { if (value[field].Kind != "boolean") throw new InvalidOperationException("Invalid assistance"); }
                else
                {
                    string pattern = field == "content_hash" || field == "layout_signature" || field == "modifiers_hash" ? "^[a-f0-9]{64}\\z" : "^[A-Za-z0-9_.:@-]{1,128}\\z";
                    if (value[field].Kind != "string" || !Regex.IsMatch(value[field].Text, pattern, RegexOptions.CultureInvariant)) throw new InvalidOperationException("Invalid RunIdentity " + field);
                }
            }
            if (value["rule_profile_id"].Text != "standard" && value["rule_profile_id"].Text != "relaxed" && value["rule_profile_id"].Text != "command") throw new InvalidOperationException("Invalid rule profile");
        }
    }
    public static class IntegerMath
    {
        public static long FloorDiv(long numerator, long denominator)
        {
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
            long quotient = numerator / denominator; return numerator % denominator < 0 ? checked(quotient - 1) : quotient;
        }
        public static long CeilDiv(long numerator, long denominator)
        {
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
            long quotient = numerator / denominator; return numerator % denominator > 0 ? checked(quotient + 1) : quotient;
        }
    }
    public sealed class XorShift32
    {
        public uint State { get; private set; }
        public long Draws { get; private set; }
        public XorShift32(long seed) { State = unchecked((uint)seed); if (State == 0) State = 0x9E3779B9u; }
        private XorShift32(uint state, long draws) { if (state == 0 || draws < 0) throw new ArgumentException("invalid RNG snapshot"); State = state; Draws = draws; }
        public static XorShift32 Restore(uint state, long draws) => new XorShift32(state, draws);
        public static XorShift32 Restore(JsonValue saved)
        {
            if (saved == null || saved.Kind != "object" || saved.Members.Count != 3 || !saved.Has("algorithm") || !saved.Has("state") || !saved.Has("draws") || saved["algorithm"].Text != "xorshift32-v1") throw new InvalidOperationException("Invalid RNG snapshot fields/algorithm");
            long state = saved["state"].Integer, draws = saved["draws"].Integer;
            if (state < 1 || state > uint.MaxValue || draws < 0) throw new InvalidOperationException("Invalid RNG snapshot bounds");
            return Restore((uint)state, draws);
        }
        public uint Next()
        {
            long nextDraws = checked(Draws + 1); uint x = State;
            unchecked { x ^= x << 13; x ^= x >> 17; x ^= x << 5; }
            if (x == 0) throw new InvalidOperationException("SIMULATION_INVARIANT_FAULT: zero RNG");
            State = x; Draws = nextDraws; return x;
        }
        public uint Bounded(uint bound)
        {
            if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
            uint threshold = unchecked(0u - bound) % bound, draw;
            do { draw = Next(); } while (draw < threshold);
            return draw % bound;
        }
        public XorShift32 Clone() => Restore(State, Draws);
        public JsonValue ToJson() => JsonValue.Object(new Dictionary<string, JsonValue> { {"algorithm",JsonValue.String("xorshift32-v1")}, {"state",JsonValue.Number(State)}, {"draws",JsonValue.Number(Draws)} });
    }

    public static class CanonicalState
    {
        // Canonical projection does not sort arbitrary arrays or remove adapter
        // provenance from save integrity. Callers supply a validated Snapshot.
        public static JsonValue RuleProjection(JsonValue snapshot) { using (var work = RuleProjectionWork(snapshot)) return work.Drain(); }
        public static ResumableWork<JsonValue> RuleProjectionWork(JsonValue snapshot)
            => new ResumableWork<JsonValue>(done => Project(snapshot, done));
        private static IEnumerable<string> Project(JsonValue snapshot, Action<JsonValue> done)
        {
            RuleProjectionPolicy policy = null;
            foreach (string stage in PrepareProjection(snapshot, prepared => policy = prepared)) yield return stage;
            foreach (string stage in ReplaceCommandReferences(snapshot, policy, (int)ProjectionPart.Root, done)) yield return stage;
        }
        private static IEnumerable<string> PrepareProjection(JsonValue snapshot, Action<RuleProjectionPolicy> done)
        {
            var receipts = snapshot["command_receipts"].Items;
            var sequenceById = new Dictionary<string, long>(receipts.Count, StringComparer.Ordinal);
            yield return "rule-projection.allocate-receipts";
            int batch = 0;
            foreach (var receipt in receipts)
            {
                sequenceById.Add(receipt["command_id"].Text, receipt["sequence"].Integer);
                if (++batch == 64) { batch = 0; yield return "rule-projection.receipts"; }
            }
            yield return "rule-projection.receipts";
            // Preserve the original projection's required fields and container types.
            _ = snapshot["identity"].Members;
            _ = snapshot["pending_commands"].Items;
            var end = snapshot["end_request"];
            if (!end.IsNull) _ = end.Members;
            done(new RuleProjectionPolicy(sequenceById));
        }
        private enum ProjectionPart { Normal, Root, Identity, PendingCommands, PendingCommand, EndRequest }
        private sealed class RuleProjectionPolicy : ICanonicalJsonProjection
        {
            private readonly Dictionary<string, long> sequenceById;
            internal RuleProjectionPolicy(Dictionary<string, long> sequenceById) { this.sequenceById = sequenceById; }
            public void ValidateNode(int context, JsonValue value)
            { if (context == (int)ProjectionPart.PendingCommand) _ = value.Members; }
            public bool IncludeMember(int context, string key)
            {
                var part = (ProjectionPart)context;
                return !(part == ProjectionPart.Root && (key == "state_revision" || key == "command_receipts" || key == "recent_events")
                    || part == ProjectionPart.Identity && (key == "run_id" || key == "source_build_id" || key == "input_adapter_id")
                    || part == ProjectionPart.PendingCommand && (key == "run_id" || key == "command_id" || key == "actor_input")
                    || part == ProjectionPart.EndRequest && key == "command_id");
            }
            public int MemberContext(int context, string key)
            {
                if (context == (int)ProjectionPart.Root)
                {
                    if (key == "identity") return (int)ProjectionPart.Identity;
                    if (key == "pending_commands") return (int)ProjectionPart.PendingCommands;
                    if (key == "end_request") return (int)ProjectionPart.EndRequest;
                }
                return (int)ProjectionPart.Normal;
            }
            public int ArrayItemContext(int context)
                => context == (int)ProjectionPart.PendingCommands ? (int)ProjectionPart.PendingCommand : (int)ProjectionPart.Normal;
            public bool TryIntegerReference(string key, JsonValue value, out long replacement)
            {
                replacement = 0;
                if ((key != "command_id" && !key.EndsWith("_command_id", StringComparison.Ordinal)) || value.IsNull) return false;
                if (!sequenceById.TryGetValue(value.Text, out replacement)) throw new InvalidOperationException("Unbound command reference in snapshot: " + value.Text);
                return true;
            }
        }
        private static IEnumerable<string> ReplaceCommandReferences(JsonValue value, RuleProjectionPolicy policy,
            int context, Action<JsonValue> done)
        {
            policy.ValidateNode(context, value);
            if (value.Kind == "array")
            {
                var items = new List<JsonValue>(value.Items.Count);
                yield return "rule-projection.allocate-array";
                foreach (var item in value.Items)
                {
                    JsonValue projected = null;
                    foreach (string stage in ReplaceCommandReferences(item, policy, policy.ArrayItemContext(context),
                        childValue => projected = childValue)) yield return stage;
                    items.Add(projected);
                    yield return "rule-projection.array-item";
                }
                using (var copy = JsonValue.ArrayWork(items))
                {
                    while (copy.MoveNext()) yield return copy.Stage;
                    done(copy.Result);
                }
                yield break;
            }
            if (value.Kind != "object") { done(value); yield break; }
            var result = new Dictionary<string, JsonValue>(value.Members.Count, StringComparer.Ordinal);
            yield return "rule-projection.allocate-object";
            foreach (var pair in value.Members)
            {
                if (!policy.IncludeMember(context, pair.Key))
                { yield return "rule-projection.exclude-field"; continue; }
                if (policy.TryIntegerReference(pair.Key, pair.Value, out long sequence))
                {
                    result.Add(pair.Key, JsonValue.Number(sequence));
                }
                else
                {
                    JsonValue projected = null;
                    foreach (string stage in ReplaceCommandReferences(pair.Value, policy, policy.MemberContext(context, pair.Key), item => projected = item)) yield return stage;
                    result.Add(pair.Key, projected);
                }
                yield return "rule-projection.object-field";
            }
            using (var copy = JsonValue.ObjectWork(result))
            {
                while (copy.MoveNext()) yield return copy.Stage;
                done(copy.Result);
            }
        }
        public static ResumableWork<string> RuleHashWork(JsonValue snapshot) => new ResumableWork<string>(done => HashProjection(snapshot, done));
        private static IEnumerable<string> HashProjection(JsonValue snapshot, Action<string> done)
        {
            RuleProjectionPolicy policy = null;
            foreach (string stage in PrepareProjection(snapshot, prepared => policy = prepared)) yield return stage;
            // Traverse the immutable snapshot through the same policy as the public
            // projection, without allocating a projected node/collection per input.
            using (var work = CanonicalJsonWork.HashWork(snapshot, policy, (int)ProjectionPart.Root))
            {
                while (work.MoveNext()) yield return work.Stage;
                done(work.Result);
            }
        }
        public static string RuleHash(JsonValue snapshot) { using (var work = RuleHashWork(snapshot)) return work.Drain(); }
        public static string IntegrityHash(JsonValue snapshot) => snapshot.Sha256();
        public static JsonValue SortedEntities(IEnumerable<JsonValue> entities) { using (var work = SortedEntitiesWork(entities)) return work.Drain(); }
        public static ResumableWork<JsonValue> SortedEntitiesWork(IEnumerable<JsonValue> entities)
            => new ResumableWork<JsonValue>(done => SortEntities(entities, done));
        private static IEnumerable<string> SortEntities(IEnumerable<JsonValue> entities, Action<JsonValue> done)
        {
            JsonValue[] rows;
            using (var sort = ResumableSort.Create(entities,
                Comparer<JsonValue>.Create((left, right) => StringComparer.Ordinal.Compare(left["id"].Text, right["id"].Text))))
            {
                while (sort.MoveNext()) yield return sort.Stage;
                rows = sort.Result;
            }
            for (int index = 0; index < rows.Length; index++)
            {
                string id = rows[index]["id"].Text;
                if (index != 0 && StringComparer.Ordinal.Equals(rows[index - 1]["id"].Text, id)) throw new InvalidOperationException("duplicate state ID");
                if (index % 64 == 63) yield return "state.sort-entities.unique";
            }
            using (var copy = JsonValue.ArrayWork(rows))
            {
                while (copy.MoveNext()) yield return copy.Stage;
                done(copy.Result);
            }
        }
    }
}
