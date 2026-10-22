using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;
using Resq.Progression;

namespace Resq.Persistence
{
    // A validator uses this distinction when sound save bytes require content or
    // rules that this build cannot reconstruct. Such files are never reset as corrupt.
    public sealed class SaveRunIncompatibleException : Exception
    {
        public SaveRunIncompatibleException(string message) : base(message) { }
    }

    public sealed class SaveValidationException : Exception
    {
        public string Code { get; }
        public SaveValidationException(string code, string message) : base(message) { Code = code; }
    }

    // Complete immutable player payload, captured at a published operation boundary.
    // JsonValue owns immutable copies; callers never lend this object live arrays.
    public sealed class PlayerSaveSnapshot
    {
        public const int SchemaVersion = 1;
        public const int MaximumCurrentRunBytes = 16 * 1024 * 1024;
        public const int MaximumEnvelopeBytes = 64 * 1024 * 1024;
        public const int MaximumCommandReceipts = 10000;
        private static readonly string[] PayloadFields = {
            "local_profile_id", "settings", "mission_progress", "unlocks", "reward_receipts",
            "current_run", "completed_runs", "application_recovery"
        };
        private static readonly string[] SnapshotFields = {
            "schema_version", "identity", "state_revision", "phase", "operation_tick", "scenario_tick",
            "scenario_accumulator", "scenario_enabled", "guidance_state", "rng", "supplies_remaining",
            "selected_stock", "deployments", "water", "rate_remainders", "power", "equipment", "vehicles",
            "residents", "transits", "transfers", "reservations", "routes", "queues", "fire", "supports",
            "runouts", "tides", "lava", "stores", "producers", "event_state", "milestones", "metrics",
            "objective_monitors", "last_admitted_sequence", "pending_commands", "command_receipts",
            "recent_events", "water_ledger", "end_request", "outcome"
        };
        private readonly JsonValue payload;
        private readonly byte[] canonicalBytes;
        private readonly Action<JsonValue> validateCurrentRun;
        public string LocalProfileId => payload["local_profile_id"].Text;
        public string PayloadSha256 { get; }
        public JsonValue CurrentRun => payload["current_run"];
        public bool RequiresPausedContinue => !CurrentRun.IsNull;

        private PlayerSaveSnapshot(JsonValue payload, byte[] bytes, Action<JsonValue> validateCurrentRun)
        {
            this.payload = payload; canonicalBytes = bytes; this.validateCurrentRun = validateCurrentRun;
            PayloadSha256 = payload.Sha256();
        }

        // The required non-null-run callback performs the complete closed Snapshot
        // and content/domain reconstruction validation. It must not advance a clock.
        // This envelope layer separately bounds the run and closes its outer shape.
        public static PlayerSaveSnapshot Create(JsonValue playerSave, Action<JsonValue> validateCurrentRun = null)
        {
            if (playerSave == null) throw new ArgumentNullException(nameof(playerSave));
            byte[] bytes = playerSave.CanonicalBytes();
            if (bytes.Length > MaximumEnvelopeBytes) throw new SaveValidationException("SAVE_SIZE", "Player payload exceeds the strict JSON limit");
            JsonValue copy = StrictJson.Parse(bytes);
            Fields(copy, PayloadFields);
            StableId(copy["local_profile_id"]);
            ValidateSettings(copy["settings"]);
            ValidateRecovery(copy["application_recovery"]);
            JsonValue progression = ProgressionPart(copy);
            JsonValue restored = ProgressionLedger.Restore(progression).ToJson();
            // A load must reject malformed ordering, not silently repair hashed data.
            if (restored.Canonical() != progression.Canonical())
                throw new SaveValidationException("SAVE_ORDER", "Campaign collections must be in their canonical stable-ID order");
            JsonValue run = copy["current_run"];
            if (!run.IsNull)
            {
                Fields(run, SnapshotFields);
                Number(run["schema_version"], 1, 1, "snapshot schema_version");
                if (run.CanonicalBytes().Length > MaximumCurrentRunBytes)
                    throw new SaveValidationException("SAVE_RUN_SIZE", "Current run exceeds 16 MiB; causal state was not removed");
                RunIdentity.Validate(run["identity"]);
                if (run["command_receipts"].Kind != "array" || run["command_receipts"].Items.Count > MaximumCommandReceipts)
                    throw new SaveValidationException("SAVE_RECEIPTS", "Current run exceeds the admitted-receipt bound");
                if (validateCurrentRun == null)
                    throw new SaveValidationException("SAVE_RUN_VALIDATOR", "A content-aware complete Snapshot validator is required");
                validateCurrentRun(run);
            }
            return new PlayerSaveSnapshot(copy, bytes, validateCurrentRun);
        }

        public static PlayerSaveSnapshot Fresh(string localProfileId)
        {
            JsonValue empty = ProgressionLedger.Empty().ToJson();
            return Create(Row("local_profile_id", JsonValue.String(localProfileId), "settings", DefaultSettings(),
                "mission_progress", empty["mission_progress"], "unlocks", empty["unlocks"], "reward_receipts", empty["reward_receipts"],
                "current_run", JsonValue.Null(), "completed_runs", empty["completed_runs"],
                "application_recovery", Recovery(false, "none", false)));
        }

        public JsonValue ToJson() => payload;
        public byte[] CanonicalBytes() => (byte[])canonicalBytes.Clone();
        public ProgressionLedger ReadProgression() => ProgressionLedger.Restore(ProgressionPart(payload));
        public PlayerSaveSnapshot WithSettings(JsonValue settings) => Create(payload.With("settings", settings), validateCurrentRun);
        public PlayerSaveSnapshot WithRecovery(JsonValue recovery) => Create(payload.With("application_recovery", recovery), validateCurrentRun);
        public PlayerSaveSnapshot WithCurrentRun(JsonValue currentRun, JsonValue recovery, Action<JsonValue> validator)
            => Create(payload.With("current_run", currentRun).With("application_recovery", recovery), validator);

        // ProgressionLedger.Apply owns latest-64 insertion-order retention and keeps
        // all mission partitions, unlocks and one-time reward receipts.
        public PlayerSaveSnapshot WithProgression(ProgressionLedger progression)
        {
            if (progression == null) throw new ArgumentNullException(nameof(progression));
            JsonValue result = payload;
            foreach (var pair in progression.ToJson().Members) result = result.With(pair.Key, pair.Value);
            return Create(result, validateCurrentRun);
        }

        public static JsonValue Recovery(bool interrupted, string reason, bool layoutReconfirmationRequired)
        {
            JsonValue result = Row("interrupted", JsonValue.Bool(interrupted), "reason", JsonValue.String(reason),
                "layout_reconfirmation_required", JsonValue.Bool(layoutReconfirmationRequired));
            ValidateRecovery(result); return result;
        }

        public static JsonValue DefaultSettings() => Row(
            "preferred_rule_profile", JsonValue.String("standard"), "preferred_input_adapter", JsonValue.String("hands"),
            "preferred_hand", JsonValue.String("right"), "dwell_ms", JsonValue.Number(800),
            "text_scale_percent", JsonValue.Number(100), "captions", JsonValue.Bool(true),
            "caption_text_scale_percent", JsonValue.Number(100), "caption_background_opacity_percent", JsonValue.Number(90),
            "target_scale_percent", JsonValue.Number(100), "highlight_intensity_percent", JsonValue.Number(70),
            "reduced_effects", JsonValue.Bool(false), "audio_music", JsonValue.Number(50),
            "audio_world", JsonValue.Number(80), "audio_warnings", JsonValue.Number(100), "audio_speech", JsonValue.Number(80),
            "preferred_layout", JsonValue.String("wide"));

        public static void ValidateSettings(JsonValue settings)
        {
            Fields(settings, "preferred_rule_profile", "preferred_input_adapter", "preferred_hand", "dwell_ms",
                "text_scale_percent", "captions", "caption_text_scale_percent", "caption_background_opacity_percent",
                "target_scale_percent", "highlight_intensity_percent", "reduced_effects", "audio_music", "audio_world",
                "audio_warnings", "audio_speech", "preferred_layout");
            Choice(settings["preferred_rule_profile"], "standard", "relaxed", "command");
            StableId(settings["preferred_input_adapter"]);
            Choice(settings["preferred_hand"], "left", "right");
            Integers(settings["dwell_ms"], 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500);
            Integers(settings["text_scale_percent"], 100, 125, 150);
            Bool(settings["captions"]);
            Integers(settings["caption_text_scale_percent"], 100, 125, 150);
            Integers(settings["caption_background_opacity_percent"], 70, 80, 90, 100);
            Integers(settings["target_scale_percent"], 100, 150);
            Integers(settings["highlight_intensity_percent"], 40, 50, 60, 70, 80, 90, 100);
            Bool(settings["reduced_effects"]);
            foreach (string key in new[] { "audio_music", "audio_world", "audio_warnings", "audio_speech" })
                Integers(settings[key], 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100);
            Choice(settings["preferred_layout"], "wide", "compact");
        }

        public static void ValidateRecovery(JsonValue recovery)
        {
            Fields(recovery, "interrupted", "reason", "layout_reconfirmation_required");
            Bool(recovery["interrupted"]); Bool(recovery["layout_reconfirmation_required"]);
            Choice(recovery["reason"], "none", "tracking", "anchor", "platform", "user");
        }

        internal PlayerSaveSnapshot AsAssistedRecovery()
        {
            JsonValue run = CurrentRun;
            if (!run.IsNull) run = run.With("identity", run["identity"].With("assisted", JsonValue.Bool(true)));
            return Create(payload.With("current_run", run).With("application_recovery", Recovery(true, "user", !run.IsNull)), validateCurrentRun);
        }

        internal static void ValidateNoProgressLoss(PlayerSaveSnapshot previous, PlayerSaveSnapshot next)
        {
            if (previous.LocalProfileId != next.LocalProfileId)
                throw new SaveValidationException("SAVE_PROFILE", "An ordinary write cannot replace a different local profile");
            JsonValue before = previous.payload, after = next.payload;
            var progress = after["mission_progress"].Items.ToDictionary(row => row["comparison_partition"].Text, StringComparer.Ordinal);
            foreach (JsonValue old in before["mission_progress"].Items)
            {
                if (!progress.TryGetValue(old["comparison_partition"].Text, out JsonValue current)
                    || old["mission_version_id"].Text != current["mission_version_id"].Text
                    || old["campaign_mission_id"].Text != current["campaign_mission_id"].Text
                    || (old["completed"].Boolean && !current["completed"].Boolean)
                    || current["best_stars"].Integer < old["best_stars"].Integer
                    || (current["best_stars"].Integer == old["best_stars"].Integer && current["best_score"].Integer < old["best_score"].Integer)
                    || (current["best_stars"].Integer == old["best_stars"].Integer && current["best_score"].Integer == old["best_score"].Integer
                        && current["best_run_id"].Text != old["best_run_id"].Text))
                    throw new SaveValidationException("SAVE_PROGRESS_LOSS", "An ordinary generation cannot discard or regress a mission partition");
            }
            RetainRows(before, after, "unlocks", "id", false);
            RetainRows(before, after, "reward_receipts", "reward_id", true);
            RetainLatestSummaries(before["completed_runs"], after["completed_runs"]);
            // Assistance is permanent for the same attempt, including settings saves.
            if (!previous.CurrentRun.IsNull && !next.CurrentRun.IsNull
                && previous.CurrentRun["identity"]["run_id"].Text == next.CurrentRun["identity"]["run_id"].Text
                && previous.CurrentRun["identity"]["assisted"].Boolean && !next.CurrentRun["identity"]["assisted"].Boolean)
                throw new SaveValidationException("SAVE_ASSISTANCE", "A restored attempt cannot clear its assistance marker");
            if (!previous.CurrentRun.IsNull && !next.CurrentRun.IsNull
                && previous.CurrentRun["identity"]["run_id"].Text == next.CurrentRun["identity"]["run_id"].Text
                && previous.CurrentRun["identity"].Without("input_adapter_id", "assisted").Canonical()
                    != next.CurrentRun["identity"].Without("input_adapter_id", "assisted").Canonical())
                throw new SaveValidationException("SAVE_ATTEMPT_IDENTITY", "An ordinary generation cannot replace rules or content under the same run ID");
            if (!previous.CurrentRun.IsNull && !next.CurrentRun.IsNull
                && previous.CurrentRun["identity"]["run_id"].Text == next.CurrentRun["identity"]["run_id"].Text
                && (next.CurrentRun["operation_tick"].Integer < previous.CurrentRun["operation_tick"].Integer
                    || next.CurrentRun["scenario_tick"].Integer < previous.CurrentRun["scenario_tick"].Integer
                    || next.CurrentRun["state_revision"].Integer < previous.CurrentRun["state_revision"].Integer
                    || next.CurrentRun["last_admitted_sequence"].Integer < previous.CurrentRun["last_admitted_sequence"].Integer))
                throw new SaveValidationException("SAVE_ROLLBACK", "An earlier state of this attempt requires explicit backup recovery");
        }

        private static void RetainLatestSummaries(JsonValue before, JsonValue after)
        {
            if (before.Items.Count == 0) return;
            var oldById = before.Items.ToDictionary(row => row["identity"]["run_id"].Text, StringComparer.Ordinal);
            foreach (JsonValue row in after.Items)
                if (oldById.TryGetValue(row["identity"]["run_id"].Text, out JsonValue prior) && row.Canonical() != prior.Canonical())
                    throw new SaveValidationException("SAVE_SUMMARY_REWRITE", "A retained run summary cannot be rewritten");
            if (after.Items.Count == 0)
                throw new SaveValidationException("SAVE_SUMMARY_LOSS", "An ordinary generation cannot remove retained run summaries");
            string firstId = after.Items[0]["identity"]["run_id"].Text;
            int first = -1;
            for (int index = 0; index < before.Items.Count; index++)
                if (before.Items[index]["identity"]["run_id"].Text == firstId) { first = index; break; }
            if (first < 0)
            {
                if (after.Items.Count != 64 || after.Items.Any(row => oldById.ContainsKey(row["identity"]["run_id"].Text)))
                    throw new SaveValidationException("SAVE_SUMMARY_ORDER", "Summary retention must preserve insertion order");
                return;
            }
            int retained = before.Items.Count - first;
            if (after.Items.Count < retained || (first > 0 && after.Items.Count != 64))
                throw new SaveValidationException("SAVE_SUMMARY_LOSS", "Only the oldest summaries may leave the full 64-record window");
            for (int index = 0; index < retained; index++)
                if (before.Items[first + index].Canonical() != after.Items[index].Canonical())
                    throw new SaveValidationException("SAVE_SUMMARY_ORDER", "Retained summaries must preserve their insertion order");
            if (after.Items.Skip(retained).Any(row => oldById.ContainsKey(row["identity"]["run_id"].Text)))
                throw new SaveValidationException("SAVE_SUMMARY_ORDER", "A prior summary cannot be reinserted as a new completion");
        }

        private static void RetainRows(JsonValue before, JsonValue after, string array, string key, bool receipt)
        {
            var indexed = after[array].Items.ToDictionary(row => row[key].Text, StringComparer.Ordinal);
            foreach (JsonValue old in before[array].Items)
            {
                if (!indexed.TryGetValue(old[key].Text, out JsonValue current))
                    throw new SaveValidationException("SAVE_PROGRESS_LOSS", "An ordinary generation cannot discard " + array);
                bool preserved = receipt
                    ? old["source_run_id"].Text == current["source_run_id"].Text && (!old["applied"].Boolean || current["applied"].Boolean)
                    : old.Canonical() == current.Canonical();
                if (!preserved) throw new SaveValidationException("SAVE_PROGRESS_LOSS", "An ordinary generation cannot rewrite prior " + array);
            }
        }

        private static JsonValue ProgressionPart(JsonValue value) => Row(
            "mission_progress", value["mission_progress"], "unlocks", value["unlocks"],
            "reward_receipts", value["reward_receipts"], "completed_runs", value["completed_runs"]);
        internal static JsonValue Row(params object[] pairs)
        {
            var row = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int index = 0; index < pairs.Length; index += 2) row.Add((string)pairs[index], (JsonValue)pairs[index + 1]);
            return JsonValue.Object(row);
        }
        internal static void Fields(JsonValue value, params string[] names)
        {
            if (value == null || value.Kind != "object" || value.Members.Count != names.Length || names.Any(name => !value.Has(name)))
                throw new SaveValidationException("SAVE_FIELDS", "Invalid closed save object fields");
        }
        internal static void StableId(JsonValue value)
        {
            if (value == null || value.Kind != "string" || !Regex.IsMatch(value.Text, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant))
                throw new SaveValidationException("SAVE_ID", "Invalid stable ID");
        }
        internal static void Hash(JsonValue value)
        {
            if (value == null || value.Kind != "string" || !Regex.IsMatch(value.Text, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant))
                throw new SaveValidationException("SAVE_HASH", "Expected lowercase SHA-256");
        }
        internal static void Number(JsonValue value, long minimum, long maximum, string name)
        {
            if (value == null || value.Kind != "integer" || value.Integer < minimum || value.Integer > maximum)
                throw new SaveValidationException("SAVE_INTEGER", "Invalid integer: " + name);
        }
        private static void Bool(JsonValue value)
        {
            if (value == null || value.Kind != "boolean") throw new SaveValidationException("SAVE_BOOLEAN", "A boolean is required");
        }
        private static void Choice(JsonValue value, params string[] choices)
        {
            if (value == null || value.Kind != "string" || !choices.Contains(value.Text))
                throw new SaveValidationException("SAVE_ENUM", "Invalid save enumeration");
        }
        private static void Integers(JsonValue value, params long[] choices)
        {
            if (value == null || value.Kind != "integer" || !choices.Contains(value.Integer))
                throw new SaveValidationException("SAVE_ENUM", "Invalid integer enumeration");
        }
    }
}
