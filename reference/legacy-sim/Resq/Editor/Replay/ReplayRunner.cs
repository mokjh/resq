using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Resq.Content;
using Resq.Core;
using Resq.Persistence;
using Resq.Simulation;

namespace Resq.Editor.Replay
{
    public sealed class ReplayDocument
    {
        public JsonValue Value { get; }
        private ReplayDocument(JsonValue value) { Value = value; }
        public static ReplayDocument Read(JsonValue value)
        {
            SnapshotContract.Fields(value, "schema_version", "identity", "initial_snapshot", "commands", "checkpoints", "expected", "provenance");
            SnapshotContract.Number(value["schema_version"], 1, 1); RunIdentity.Validate(value["identity"]); SnapshotContract.Validate(value["initial_snapshot"]);
            if (value["identity"].Canonical() != value["initial_snapshot"]["identity"].Canonical()) throw new InvalidOperationException("Replay initial identity mismatch");
            if (value["commands"].Items.Count > 10000) throw new InvalidOperationException("Replay command limit");
            long previousTick = value["initial_snapshot"]["operation_tick"].Integer;
            foreach (var row in value["commands"].Items)
            {
                SnapshotContract.Fields(row, "admitted_at_operation_tick", "command", "expected_result"); SnapshotContract.Number(row["admitted_at_operation_tick"], previousTick, Tick.Maximum);
                previousTick = row["admitted_at_operation_tick"].Integer; GameCommand.Read(row["command"]); CommandResult.Read(row["expected_result"]);
                if (row["command"]["command_id"].Text != row["expected_result"]["command_id"].Text || row["command"]["sequence"].Integer != row["expected_result"]["sequence"].Integer) throw new InvalidOperationException("Replay command receipt identity mismatch");
            }
            long last = -1;
            foreach (var row in value["checkpoints"].Items)
            {
                SnapshotContract.Fields(row, "operation_tick", "scenario_tick", "expected_state_sha256"); SnapshotContract.Number(row["operation_tick"], Math.Max(last + 1, value["initial_snapshot"]["operation_tick"].Integer), Tick.Maximum);
                SnapshotContract.Number(row["scenario_tick"], 0, Tick.Maximum); SnapshotContract.Hash(row["expected_state_sha256"]); last = row["operation_tick"].Integer;
            }
            var expected = value["expected"]; SnapshotContract.Fields(expected, "success", "rescued", "stars", "score", "final_state_sha256");
            if (expected["success"].Kind != "boolean") throw new InvalidOperationException("Replay success bool"); SnapshotContract.Number(expected["rescued"], 0, 256); SnapshotContract.Number(expected["stars"], 0, 3); SnapshotContract.Number(expected["score"], 0, 10000); SnapshotContract.Hash(expected["final_state_sha256"]);
            var provenance = value["provenance"]; SnapshotContract.Fields(provenance, "generator", "source_build_id", "simulation_assembly_sha256");
            if (!new[] { "manual_input", "reference_agent", "test_fixture" }.Contains(provenance["generator"].Text)) throw new InvalidOperationException("Replay generator");
            SnapshotContract.Id(provenance["source_build_id"]); SnapshotContract.Hash(provenance["simulation_assembly_sha256"]);
            if (provenance["source_build_id"].Text != value["identity"]["source_build_id"].Text) throw new InvalidOperationException("Replay source mismatch");
            return new ReplayDocument(value);
        }
        public static ReplayDocument Parse(byte[] bytes) => Read(StrictJson.Parse(bytes));
    }
    public sealed class ReplayReport
    {
        public bool Passed { get; }
        public string Reason { get; }
        public SimulationSnapshot FinalSnapshot { get; }
        public ReadOnlyCollection<JsonValue> ActualCheckpoints { get; }
        public string ApplicationJournalSha256 { get; }
        internal ReplayReport(bool passed, string reason, SimulationSnapshot final, IEnumerable<JsonValue> checkpoints, string applicationJournalSha256 = null)
        { Passed = passed; Reason = reason; FinalSnapshot = final; ActualCheckpoints = checkpoints.ToList().AsReadOnly(); ApplicationJournalSha256 = applicationJournalSha256; }
    }
    public interface IReplayRunner
    {
        ReplayReport Run(ReplayDocument replay, SimulationFactory factory);
        ReplayReport Run(ReplayDocument replay, SimulationFactory factory, ReplayApplicationJournal applicationJournal);
    }
    public sealed class ReplayRunner : IReplayRunner
    {
        public static string CurrentSimulationAssemblySha256()
        {
            using (var stream = File.OpenRead(typeof(OperationClock).Assembly.Location)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public ReplayReport Run(ReplayDocument replay, SimulationFactory factory)
        {
            if (replay == null || factory == null) throw new ArgumentNullException();
            // Snapshot excludes application pacing. For an active saved boundary,
            // validate the recorded trace against both legal initial lifecycles.
            // Neither trial injects expected receipts or edits authoritative state.
            // This also handles simulation admissions before the first resume and
            // genuine rejected resume commands at an already-running boundary.
            var running = RunWithInitialLifecycle(replay, factory, true);
            string phase = replay.Value["initial_snapshot"]["phase"].Text;
            if (running.Passed || phase != "active" && phase != "stabilizing") return running;
            var paused = RunWithInitialLifecycle(replay, factory, false);
            if (paused.Passed) return paused;
            return new ReplayReport(false, "Running initial lifecycle: " + running.Reason + "; paused initial lifecycle: " + paused.Reason,
                running.FinalSnapshot ?? paused.FinalSnapshot, running.ActualCheckpoints);
        }
        public ReplayReport Run(ReplayDocument replay, SimulationFactory factory, ReplayApplicationJournal applicationJournal)
        {
            if (replay == null || factory == null || applicationJournal == null) throw new ArgumentNullException();
            applicationJournal.ValidateBinding(replay);
            return RunWithInitialLifecycle(replay, factory, applicationJournal.InitialState == "running" || applicationJournal.InitialState == "outcome", applicationJournal);
        }
        private ReplayReport RunWithInitialLifecycle(ReplayDocument replay, SimulationFactory factory, bool runningAtInitialBoundary, ReplayApplicationJournal applicationJournal = null)
        {
            var actual = new List<JsonValue>(); OperationClock clock = null;
            try
            {
                var doc = replay.Value;
                if (doc["provenance"]["simulation_assembly_sha256"].Text != CurrentSimulationAssemblySha256()) throw new SaveRunIncompatibleException("Replay assembly differs; explicit target compatibility report required");
                var commands = doc["commands"].Items; var initial = doc["initial_snapshot"];
                clock = factory.RestoreForReplay(initial, runningAtInitialBoundary);
                applicationJournal?.Initialize(clock);
                var checkpoints = doc["checkpoints"].Items.ToDictionary(x => x["operation_tick"].Integer);
                var visited = new HashSet<long>(); var awaiting = new List<JsonValue>();
                Action<long, long, string> inspect = (o, s, hash) => {
                    JsonValue expected;
                    if (checkpoints.TryGetValue(o, out expected))
                    {
                        if (expected["scenario_tick"].Integer != s || expected["expected_state_sha256"].Text != hash) throw new InvalidOperationException("Replay checkpoint mismatch at O=" + o);
                        if (visited.Add(o)) actual.Add(JsonValue.Object(new Dictionary<string, JsonValue> { { "operation_tick", JsonValue.Number(o) }, { "scenario_tick", JsonValue.Number(s) }, { "state_sha256", JsonValue.String(hash) } }));
                    }
                    else if (o > initial["operation_tick"].Integer && o % 100 == 0) throw new InvalidOperationException("Missing required 100-tick replay checkpoint");
                };
                Action resolve = () => {
                    foreach (var item in awaiting.ToArray())
                    {
                        var receipt = clock.Receipts.SingleOrDefault(x => x.CommandId == item["command"]["command_id"].Text);
                        if (receipt == null || receipt.Result.Status == "queued") continue;
                        if (receipt.Result.ToJson().Canonical() != item["expected_result"].Canonical()) throw new InvalidOperationException("Replay resolved receipt mismatch");
                        awaiting.Remove(item);
                    }
                };
                inspect(clock.OperationTick, clock.ScenarioTick, clock.ReadSnapshot().StateSha256);
                int index = 0, appliedRecoveries = 0;
                while (clock.Phase != "outcome" || index < commands.Count)
                {
                    if (index < commands.Count && commands[index]["admitted_at_operation_tick"].Integer < clock.OperationTick) throw new InvalidOperationException("Replay skipped command admission boundary");
                    if (index < commands.Count && commands[index]["admitted_at_operation_tick"].Integer == clock.OperationTick)
                    {
                        if (applicationJournal != null && applicationJournal.BeforeCommand(index, clock)) appliedRecoveries++;
                        var item = commands[index++]; var command = GameCommand.Read(item["command"]); CommandResult receipt;
                        if (command.IsApplication)
                        {
                            var result = clock.Apply(command); receipt = result.Receipt;
                            if (result.Advanced != null) foreach (var step in result.Advanced.Steps) inspect(step.OperationTick, step.ScenarioTick, step.StateSha256);
                            if (result.Advanced != null && result.Advanced.Fault != null) throw new InvalidOperationException(result.Advanced.Fault);
                        }
                        else receipt = clock.Admit(command);
                        if (receipt.Status == "queued" && item["expected_result"]["status"].Text != "queued") awaiting.Add(item);
                        else if (receipt.ToJson().Canonical() != item["expected_result"].Canonical()) throw new InvalidOperationException("Replay admission receipt mismatch");
                        if (clock.ApplicationState == "countdown") clock.CompleteResumeCountdown();
                        resolve(); continue;
                    }
                    if (clock.Phase == "outcome") break;
                    if (clock.OperationTick >= Tick.Maximum || clock.IsPaused) throw new InvalidOperationException("Replay has no recorded operation that can make progress");
                    var moved = clock.Step() ?? throw new InvalidOperationException(clock.Fault ?? "Replay step did not execute");
                    inspect(moved.OperationTick, moved.ScenarioTick, moved.StateSha256); resolve();
                }
                resolve(); var final = clock.ReadSnapshot(); var outcome = final.Value["outcome"]; var expectedFinal = doc["expected"];
                if (index != commands.Count || awaiting.Count != 0 || outcome.IsNull || visited.Count != checkpoints.Count || !checkpoints.ContainsKey(clock.OperationTick) || appliedRecoveries != (applicationJournal?.RecoveryCount ?? 0)) throw new InvalidOperationException("Replay incomplete commands, terminal outcome, application recovery or checkpoint coverage");
                foreach (var field in new[] { "success", "rescued", "stars", "score" }) if (outcome[field].Canonical() != expectedFinal[field].Canonical()) throw new InvalidOperationException("Replay actual terminal " + field + " mismatch");
                if (final.StateSha256 != expectedFinal["final_state_sha256"].Text) throw new InvalidOperationException("Replay actual final hash mismatch");
                return new ReplayReport(true, "Actual simulator commands, receipts, checkpoints and terminal outcome matched", final, actual, applicationJournal?.Sha256);
            }
            catch (Exception error) { return new ReplayReport(false, error.GetType().Name + ": " + error.Message, clock?.ReadSnapshot(), actual, applicationJournal?.Sha256); }
        }
    }
}
