using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;

namespace Resq.Progression
{
    // Explicit grants come from the campaign owner, never from a display label or
    // a version-derived reward ID. A null unlock records a receipt-only reward.
    public sealed class RewardGrant
    {
        public string RewardId { get; }
        public string UnlockId { get; }
        public string UnlockKind { get; }
        public RewardGrant(string rewardId, string unlockId = null, string unlockKind = null)
        {
            ProgressionLedger.ValidateId(rewardId);
            if ((unlockId == null) != (unlockKind == null)) throw new ArgumentException("Unlock ID and kind must both be present or absent");
            if (unlockId != null) { ProgressionLedger.ValidateId(unlockId); ProgressionLedger.ValidateUnlockKind(unlockKind); }
            RewardId = rewardId; UnlockId = unlockId; UnlockKind = unlockKind;
        }
    }

    // This immutable component owns only the four prescribed progression arrays.
    // Disk generations, settings, migration and current-run snapshots belong to C07.
    public sealed class ProgressionLedger
    {
        private static readonly string[] PartitionFields = { "mission_version_id", "rules_version", "layout_signature", "modifiers_hash", "rule_profile_id", "input_adapter_id", "guidance_policy", "assisted" };
        private readonly JsonValue state;
        private ProgressionLedger(JsonValue state) { this.state = state; }

        public static ProgressionLedger Empty() => new ProgressionLedger(Row(
            "mission_progress", JsonValue.Array(new JsonValue[0]), "unlocks", JsonValue.Array(new JsonValue[0]),
            "reward_receipts", JsonValue.Array(new JsonValue[0]), "completed_runs", JsonValue.Array(new JsonValue[0])));

        public JsonValue ToJson() => state;

        public static string ComparisonPartition(JsonValue identity)
        {
            RunIdentity.Validate(identity);
            return JsonValue.Object(PartitionFields.ToDictionary(field => field, field => identity[field], StringComparer.Ordinal)).Sha256();
        }

        public bool IsCampaignMissionCompleted(string campaignMissionId)
        {
            ValidateId(campaignMissionId);
            return state["mission_progress"].Items.Any(row => row["campaign_mission_id"].Text == campaignMissionId && row["completed"].Boolean);
        }

        public static ProgressionLedger Restore(JsonValue state)
        {
            Fields(state, "mission_progress", "unlocks", "reward_receipts", "completed_runs");
            var partitions = new HashSet<string>(StringComparer.Ordinal);
            var missionCampaigns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in Rows(state, "mission_progress"))
            {
                Fields(row, "mission_version_id", "comparison_partition", "completed", "best_stars", "best_score", "best_run_id", "campaign_mission_id");
                Id(row, "mission_version_id"); Hash(row, "comparison_partition"); Id(row, "best_run_id"); Id(row, "campaign_mission_id");
                Number(row, "best_stars", 0, 3); Number(row, "best_score", 0, 10000); Boolean(row, "completed");
                if (row["completed"].Boolean != (row["best_stars"].Integer > 0)) throw new InvalidOperationException("Progress success/stars mismatch");
                if (!partitions.Add(row["comparison_partition"].Text)) throw new InvalidOperationException("Duplicate comparison partition");
                string version = row["mission_version_id"].Text, campaign = row["campaign_mission_id"].Text;
                if (missionCampaigns.TryGetValue(version, out string prior) && prior != campaign) throw new InvalidOperationException("Mission version maps to multiple campaign missions");
                missionCampaigns[version] = campaign;
            }
            var unlockIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(state, "unlocks"))
            {
                Fields(row, "id", "kind", "awarded_by_run_id"); Id(row, "id"); Id(row, "awarded_by_run_id");
                ValidateUnlockKind(row["kind"].Text);
                if (!unlockIds.Add(row["id"].Text)) throw new InvalidOperationException("Duplicate unlock ID");
            }
            var receiptIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(state, "reward_receipts"))
            {
                Fields(row, "reward_id", "source_run_id", "applied"); Id(row, "reward_id"); Id(row, "source_run_id"); Boolean(row, "applied");
                if (!receiptIds.Add(row["reward_id"].Text)) throw new InvalidOperationException("Duplicate campaign reward ID");
            }
            var runIds = new HashSet<string>(StringComparer.Ordinal);
            var summaries = Rows(state, "completed_runs");
            if (summaries.Count > 64) throw new InvalidOperationException("RunSummary retention limit exceeded");
            foreach (var summary in summaries)
            {
                ValidateRunSummary(summary);
                if (!runIds.Add(summary["identity"]["run_id"].Text)) throw new InvalidOperationException("Duplicate completed run ID");
            }
            return new ProgressionLedger(SortedState(state));
        }

        // The caller binds a stable campaign mission ID across compatible revisions.
        // Best tuple ordering is completed, then stars, then score; equal tuples retain
        // the original run. This keeps best_run_id coherent with its stored result.
        public ProgressionLedger Apply(string campaignMissionId, JsonValue summary, IEnumerable<RewardGrant> grants)
        {
            ValidateId(campaignMissionId); ValidateRunSummary(summary);
            if (grants == null) throw new ArgumentNullException(nameof(grants));
            RewardGrant[] requested = grants.ToArray();
            if (requested.Any(grant => grant == null)) throw new ArgumentException("Null reward grant", nameof(grants));
            if (requested.Select(grant => grant.RewardId).Distinct(StringComparer.Ordinal).Count() != requested.Length)
                throw new ArgumentException("Duplicate reward grant ID", nameof(grants));
            var identity = summary["identity"];
            string version = identity["mission_version_id"].Text, runId = identity["run_id"].Text;
            string partition = ComparisonPartition(identity);
            bool success = summary["success"].Boolean;
            var progress = state["mission_progress"].Items.ToList();
            if (progress.Any(row => row["mission_version_id"].Text == version && row["campaign_mission_id"].Text != campaignMissionId))
                throw new InvalidOperationException("Mission version campaign binding changed");
            int index = progress.FindIndex(row => row["comparison_partition"].Text == partition);
            var candidate = Row("mission_version_id", JsonValue.String(version), "comparison_partition", JsonValue.String(partition),
                "completed", JsonValue.Bool(success), "best_stars", summary["stars"], "best_score", summary["score"],
                "best_run_id", JsonValue.String(runId), "campaign_mission_id", JsonValue.String(campaignMissionId));
            if (index < 0) progress.Add(candidate);
            else if (IsBetter(candidate, progress[index])) progress[index] = candidate;

            var summaries = state["completed_runs"].Items.ToList();
            var previous = summaries.FirstOrDefault(row => row["identity"]["run_id"].Text == runId);
            if (previous != null && previous.Canonical() != summary.Canonical()) throw new InvalidOperationException("Conflicting completion for the same run ID");
            if (previous == null)
            {
                summaries.Add(summary);
                if (summaries.Count > 64) summaries.RemoveAt(0);
            }
            var receipts = state["reward_receipts"].Items.ToDictionary(row => row["reward_id"].Text, StringComparer.Ordinal);
            var unlocks = state["unlocks"].Items.ToDictionary(row => row["id"].Text, StringComparer.Ordinal);
            if (success)
            {
                foreach (var grant in requested.OrderBy(grant => grant.RewardId, StringComparer.Ordinal))
                {
                    receipts.TryGetValue(grant.RewardId, out var receipt);
                    if (receipt != null && receipt["applied"].Boolean) continue;
                    string sourceRun = receipt == null ? runId : receipt["source_run_id"].Text;
                    if (grant.UnlockId != null)
                    {
                        if (unlocks.TryGetValue(grant.UnlockId, out var unlocked))
                        {
                            if (unlocked["kind"].Text != grant.UnlockKind) throw new InvalidOperationException("Unlock ID kind changed");
                        }
                        else unlocks.Add(grant.UnlockId, Row("id", JsonValue.String(grant.UnlockId), "kind", JsonValue.String(grant.UnlockKind),
                            "awarded_by_run_id", JsonValue.String(sourceRun)));
                    }
                    receipts[grant.RewardId] = Row("reward_id", JsonValue.String(grant.RewardId), "source_run_id", JsonValue.String(sourceRun), "applied", JsonValue.Bool(true));
                }
            }
            return Restore(Row("mission_progress", JsonValue.Array(progress), "unlocks", JsonValue.Array(unlocks.Values),
                "reward_receipts", JsonValue.Array(receipts.Values), "completed_runs", JsonValue.Array(summaries)));
        }

        public static void ValidateRunSummary(JsonValue summary)
        {
            Fields(summary, "identity", "success", "stars", "score", "rescued", "backed_up", "final_operation_tick", "final_scenario_tick", "final_state_hash");
            RunIdentity.Validate(summary["identity"]); Boolean(summary, "success");
            Number(summary, "stars", 0, 3); Number(summary, "score", 0, 10000);
            Number(summary, "rescued", 0, 256); Number(summary, "backed_up", 0, 256);
            if (summary["rescued"].Integer + summary["backed_up"].Integer > 256) throw new InvalidOperationException("RunSummary population bound exceeded");
            Number(summary, "final_operation_tick", 0, 1000000); Number(summary, "final_scenario_tick", 0, 1000000);
            if (summary["final_scenario_tick"].Integer > summary["final_operation_tick"].Integer) throw new InvalidOperationException("Scenario tick exceeds operation tick");
            Hash(summary, "final_state_hash");
            if (summary["success"].Boolean != (summary["stars"].Integer > 0)) throw new InvalidOperationException("RunSummary success/stars mismatch");
        }

        private static bool IsBetter(JsonValue candidate, JsonValue previous)
        {
            if (candidate["completed"].Boolean != previous["completed"].Boolean) return candidate["completed"].Boolean;
            long stars = candidate["best_stars"].Integer, priorStars = previous["best_stars"].Integer;
            return stars > priorStars || (stars == priorStars && candidate["best_score"].Integer > previous["best_score"].Integer);
        }
        private static JsonValue SortedState(JsonValue state) => Row(
            "mission_progress", JsonValue.Array(state["mission_progress"].Items.OrderBy(row => row["mission_version_id"].Text, StringComparer.Ordinal).ThenBy(row => row["comparison_partition"].Text, StringComparer.Ordinal)),
            "unlocks", JsonValue.Array(state["unlocks"].Items.OrderBy(row => row["id"].Text, StringComparer.Ordinal)),
            "reward_receipts", JsonValue.Array(state["reward_receipts"].Items.OrderBy(row => row["reward_id"].Text, StringComparer.Ordinal)),
            "completed_runs", state["completed_runs"]);

        internal static void ValidateId(string value)
        {
            if (value == null || !Regex.IsMatch(value, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid stable ID");
        }
        internal static void ValidateUnlockKind(string value)
        {
            if (value != "mission" && value != "challenge" && value != "postcard" && value != "skin") throw new ArgumentException("Invalid unlock kind");
        }
        private static void Fields(JsonValue row, params string[] names)
        {
            if (row == null || row.Kind != "object" || row.Members.Count != names.Length || names.Any(name => !row.Has(name))) throw new InvalidOperationException("Invalid closed progression fields");
        }
        private static System.Collections.ObjectModel.ReadOnlyCollection<JsonValue> Rows(JsonValue state, string key)
        {
            if (state[key].Kind != "array") throw new InvalidOperationException("Progression array required: " + key);
            return state[key].Items;
        }
        private static void Id(JsonValue row, string key) { ValidateId(row[key].Text); }
        private static void Boolean(JsonValue row, string key) { if (row[key].Kind != "boolean") throw new InvalidOperationException("Boolean required: " + key); }
        private static void Hash(JsonValue row, string key)
        {
            if (row[key].Kind != "string" || !Regex.IsMatch(row[key].Text, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Hash required: " + key);
        }
        private static void Number(JsonValue row, string key, long minimum, long maximum)
        {
            if (row[key].Kind != "integer" || row[key].Integer < minimum || row[key].Integer > maximum) throw new InvalidOperationException("Invalid integer: " + key);
        }
        private static JsonValue Row(params object[] fields)
        {
            var row = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int index = 0; index < fields.Length; index += 2) row.Add((string)fields[index], (JsonValue)fields[index + 1]);
            return JsonValue.Object(row);
        }
    }
}
