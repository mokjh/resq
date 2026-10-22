using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Persistence;
using Resq.Simulation;
using Resq.Editor.Replay;

namespace Resq.Tests.Persistence
{
    [TestFixture, Category("runtime_persistence")]
    public sealed class RuntimePersistenceTests
    {
        private static JsonValue O(params object[] pairs) => PersistenceFixture.O(pairs);
        private static JsonValue A(params string[] values) => PersistenceFixture.A(values);
        private static JsonValue Placement() => O("equipment_id", "beacon", "socket_id", "beacon:01", "orientation_step", 0);
        private static void Step(OperationClock clock)
        { Assert.IsNotNull(clock.Step(), clock.Fault ?? "Actual simulation boundary did not advance"); }
        private static void Same(OperationClock left, OperationClock right)
        {
            Assert.AreEqual(left.ReadSnapshot().Value.Canonical(), right.ReadSnapshot().Value.Canonical(), "Every saved owner must agree at O=" + left.OperationTick);
            Assert.AreEqual(left.ReadSnapshot().StateSha256, right.ReadSnapshot().StateSha256);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c07/assertion/assertion_id=check-c07-a01")]
        public void ExactContinuationAndActualReplay()
        {
            foreach (bool relaxed in new[] { false, true })
            foreach (int cut in relaxed ? new[] { 5, 119, 120, 239, 240, 280 } : new[] { 5, 15, 25, 35, 59, 60, 100, 119, 120, 140 })
            {
                var rig = PersistenceFixture.Build(relaxed); var original = rig.Clock;
                // Planning is also a complete restorable boundary.
                Same(original, rig.Factory.Restore(original.ReadSnapshot().Value));
                rig.StartVehicleRoute();
                while (original.OperationTick < cut) Step(original);
                var before = original.ReadSnapshot().Value;
                if (!relaxed && cut == 5) Assert.IsTrue(before["transfers"].Items.Any(x => x["direction"].Text == "board" && x["progress_operation_ticks"].Integer > 0));
                if (!relaxed && cut == 15) Assert.IsTrue(before["transits"].Items.Any(x => x["progress_operation_ticks"].Integer > 0));
                if (!relaxed && cut == 35) Assert.IsTrue(before["transfers"].Items.Any(x => x["direction"].Text == "unboard" && x["progress_operation_ticks"].Integer > 0));
                if (relaxed && cut == 5) Assert.AreEqual(1, before["scenario_accumulator"].Integer);
                if (original.Phase != "outcome")
                {
                    Assert.AreEqual("queued", original.Admit(rig.Command("place_equipment", Placement())).Status);
                    Assert.IsTrue(original.Apply(rig.Command("pause", O("reason", "user"))).Receipt.Accepted);
                }
                JsonValue snapshot = original.ReadSnapshot().Value;
                SnapshotContract.Validate(snapshot);
                var restored = rig.Factory.Restore(snapshot); Same(original, restored);
                Assert.IsTrue(restored.IsPaused); Assert.IsNull(restored.Step()); Same(original, restored);
                Assert.AreEqual(relaxed, restored.ReadSnapshot().Value["identity"]["assisted"].Boolean, "Resume preserves existing assistance exactly");
                if (original.Phase == "outcome") continue;
                var resume = rig.Command("resume", O());
                Assert.AreEqual(original.Apply(resume).Receipt.ToJson().Canonical(), restored.Apply(resume).Receipt.ToJson().Canonical());
                original.CompleteResumeCountdown(); restored.CompleteResumeCountdown(); Same(original, restored);
                while (original.Phase != "outcome") { Step(original); Step(restored); Same(original, restored); }
                Assert.IsTrue(original.ReadSnapshot().Value["outcome"]["success"].Boolean);
                Assert.AreEqual(1, original.ReadSnapshot().Value["outcome"]["rescued"].Integer);
                Assert.AreEqual(1, original.ReadSnapshot().Value["event_state"]["fired_event_ids"].Items.Count);
                Same(original, rig.Factory.Restore(original.ReadSnapshot().Value));
            }
            VerifyReplay(false); VerifyReplay(true); VerifyReplay(true, true); VerifyReplay(false, false, true);
        }

        private static void VerifyReplay(bool activeInitial, bool pausedInitial = false, bool externalRecovery = false)
        {
            var rig = PersistenceFixture.Build(); var clock = rig.Clock;
            if (activeInitial) { rig.StartVehicleRoute(); while (clock.OperationTick < 5) Step(clock); }
            if (pausedInitial) Assert.IsTrue(clock.Apply(rig.Command("pause", O("reason", "user"))).Receipt.Accepted);
            JsonValue initial = clock.ReadSnapshot().Value;
            var recorded = new List<JsonValue>(); var checkpoints = new List<JsonValue>();
            var recoveries = new List<JsonValue>();
            Action<GameCommand> admit = command => {
                long at = clock.OperationTick;
                CommandResult receipt = command.IsApplication ? clock.Apply(command).Receipt : clock.Admit(command);
                recorded.Add(O("admitted_at_operation_tick", at, "command", command.ToJson(), "expected_result", receipt.ToJson()));
                if (clock.ApplicationState == "countdown") clock.CompleteResumeCountdown();
            };
            if (!activeInitial)
            {
                admit(rig.Command("resume", O()));
                admit(rig.Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] {
                    O("node_id", "start", "action", "pickup", "group_id", "group"), O("node_id", "refuge", "action", "evacuate", "group_id", null) }))));
                while (clock.OperationTick < 5) Step(clock);
            }
            if (pausedInitial)
            {
                admit(rig.Command("place_equipment", Placement()));
                Assert.AreEqual("queued", recorded.Last()["expected_result"]["status"].Text);
                admit(rig.Command("resume", O()));
            }
            else
            {
                admit(rig.Command("resume", O()));
                Assert.AreEqual("rejected", recorded.Last()["expected_result"]["status"].Text, "Already-running resume rejection must remain recorded");
            }
            // Retain a genuine rejection, an explicit pause, and a deterministic
            // advance. The replay removes wall time, not these admitted commands.
            admit(rig.Command("place_equipment", Placement(), clock.OperationTick + 20));
            Assert.AreEqual("rejected", recorded.Last()["expected_result"]["status"].Text);
            admit(rig.Command("pause", O("reason", "user")));
            admit(rig.Command("advance_ticks", O("operation_ticks", 10)));
            admit(rig.Command("resume", O()));
            admit(rig.Command("place_equipment", Placement()));
            if (externalRecovery)
            {
                admit(rig.Command("pause", O("reason", "tracking")));
                admit(rig.Command("resume", O()));
                Assert.AreEqual("rejected", recorded.Last()["expected_result"]["status"].Text);
                recoveries.Add(O("before_command_index", recorded.Count, "operation_tick", clock.OperationTick));
                string beforeRecovery = clock.ReadSnapshot().Value.Canonical();
                clock.RecoverInterruption();
                Assert.AreEqual(beforeRecovery, clock.ReadSnapshot().Value.Canonical());
                admit(rig.Command("resume", O()));
                Assert.AreEqual("accepted", recorded.Last()["expected_result"]["status"].Text);
            }
            while (clock.Phase != "outcome")
            {
                Step(clock);
                if (clock.OperationTick % 100 == 0 || clock.Phase == "outcome") checkpoints.Add(O("operation_tick", clock.OperationTick, "scenario_tick", clock.ScenarioTick, "expected_state_sha256", clock.ReadSnapshot().StateSha256));
            }
            recorded = recorded.Select(row => row.With("expected_result", clock.Receipts.Single(x => x.CommandId == row["command"]["command_id"].Text).Result.ToJson())).ToList();
            var final = clock.ReadSnapshot(); var outcome = final.Value["outcome"];
            var document = O("schema_version", 1, "identity", initial["identity"], "initial_snapshot", initial,
                "commands", JsonValue.Array(recorded), "checkpoints", JsonValue.Array(checkpoints),
                "expected", O("success", outcome["success"], "rescued", outcome["rescued"], "stars", outcome["stars"], "score", outcome["score"], "final_state_sha256", final.StateSha256),
                "provenance", O("generator", "test_fixture", "source_build_id", initial["identity"]["source_build_id"], "simulation_assembly_sha256", ReplayRunner.CurrentSimulationAssemblySha256()));
            var runner = new ReplayRunner();
            var replay = ReplayDocument.Read(document);
            JsonValue journalValue = externalRecovery ? O("schema_version", 1, "replay_sha256", document.Sha256(), "initial_application_state", activeInitial ? (pausedInitial ? "paused" : "running") : "paused", "initial_interruption_reason", null, "recoveries", JsonValue.Array(recoveries)) : null;
            var journal = externalRecovery ? ReplayApplicationJournal.Read(journalValue, replay) : null;
            foreach (int repeat in new[] { 1, 2 })
            {
                var report = externalRecovery ? runner.Run(replay, rig.Factory, journal) : runner.Run(replay, rig.Factory);
                Assert.IsTrue(report.Passed, report.Reason); Assert.AreEqual(final.Value.Canonical(), report.FinalSnapshot.Value.Canonical());
                Assert.AreEqual(2, report.ActualCheckpoints.Count);
                Assert.AreEqual(journal?.Sha256, report.ApplicationJournalSha256);
            }
            if (externalRecovery)
            {
                Assert.IsFalse(runner.Run(replay, rig.Factory).Passed, "Recovery cannot be guessed from expected receipts");
                Assert.Catch(() => ReplayApplicationJournal.Read(journalValue.With("replay_sha256", JsonValue.String(new string('0', 64))), replay));
                var surplus = journalValue.With("recoveries", JsonValue.Array(new[] { O("before_command_index", 0, "operation_tick", initial["operation_tick"]) }.Concat(recoveries)));
                Assert.IsFalse(runner.Run(replay, rig.Factory, ReplayApplicationJournal.Read(surplus, replay)).Passed, "Surplus recovery is not an ignorable diagnostic");
                string[] args = Environment.GetCommandLineArgs(); int flag = Array.IndexOf(args, "-resqTestResults");
                Assert.GreaterOrEqual(flag, 0, "Native evidence destination required");
                string path = Path.Combine(Path.GetDirectoryName(args[flag + 1]), "replay-recordings"); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "tracking-recovery.replay.json"), document.Canonical());
                File.WriteAllText(Path.Combine(path, "tracking-recovery.application.json"), journalValue.Canonical());
                File.WriteAllText(Path.Combine(path, "tracking-recovery.result.json"), O("status", "passed", "replay_sha256", document.Sha256(), "application_journal_sha256", journal.Sha256, "actual_final_state_sha256", final.StateSha256, "evidence_kind", "native_synthetic_fixture").Canonical());
            }
            Assert.IsFalse(runner.Run(ReplayDocument.Read(document.With("expected", document["expected"].With("score", JsonValue.Number(0)))), rig.Factory).Passed, "Expected outcomes cannot overwrite actual simulation results");
            Assert.IsFalse(runner.Run(ReplayDocument.Read(document.With("checkpoints", JsonValue.Array(checkpoints.Skip(1)))), rig.Factory).Passed, "Every 100-O boundary is required");
            Assert.IsFalse(runner.Run(ReplayDocument.Read(document.With("checkpoints", JsonValue.Array(checkpoints.Select((row, i) => i == 0 ? row.With("expected_state_sha256", JsonValue.String(new string('0', 64))) : row)))), rig.Factory).Passed);
            Assert.IsFalse(runner.Run(ReplayDocument.Read(document.With("provenance", document["provenance"].With("simulation_assembly_sha256", JsonValue.String(new string('0', 64))))), rig.Factory).Passed, "Assembly mismatch cannot assume unqualified target compatibility");
        }

        [Test, NUnit.Framework.Property("case_id", "check-c07/assertion/assertion_id=check-c07-a02")]
        public void AtomicGenerationsAndSchemaSafety() => SaveStoreChecks.AtomicGenerationsAndCorruption();

        [Test, NUnit.Framework.Property("case_id", "check-c07/assertion/assertion_id=check-c07-a03")]
        public void ExplicitRecoveryKeepsAssistance()
        {
            var rig = PersistenceFixture.Build(); rig.StartVehicleRoute();
            while (rig.Clock.OperationTick < 15) Step(rig.Clock);
            var player = PlayerSaveSnapshot.Fresh("profile:persistence").WithCurrentRun(rig.Clock.ReadSnapshot().Value, PlayerSaveSnapshot.Recovery(false, "none", false), rig.Factory.Validate);
            SaveStoreChecks.RecoveryAssistance(player, rig.Factory.Validate);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c07/assertion/assertion_id=check-c07-a04")]
        public void ClosedPrivacyAndOwnerValidation()
        {
            var rig = PersistenceFixture.Build(); rig.StartVehicleRoute();
            while (rig.Clock.OperationTick < 15) Step(rig.Clock);
            var snapshot = rig.Clock.ReadSnapshot().Value;
            var player = PlayerSaveSnapshot.Fresh("profile:privacy").WithCurrentRun(snapshot, PlayerSaveSnapshot.Recovery(false, "none", false), rig.Factory.Validate);
            foreach (string field in new[] { "camera_pose", "raw_hand_pose", "room_mesh", "microphone", "account_token", "email", "particle_interpolation", "audio_handle" })
            {
                Assert.Catch(() => rig.Factory.Restore(snapshot.With(field, O("private", true))));
                Assert.Catch(() => PlayerSaveSnapshot.Create(player.ToJson().With(field, O("private", true)), rig.Factory.Validate));
            }
            Assert.Catch(() => rig.Factory.Restore(snapshot.With("water", JsonValue.Array(snapshot["water"].Items.Reverse()))));
            Assert.Catch(() => rig.Factory.Restore(snapshot.With("water", JsonValue.Array(snapshot["water"].Items.Select((row, i) => i == 0 ? row.With("raw_camera_frame", A("private")) : row)))));
            Assert.Catch(() => rig.Factory.Restore(snapshot.With("reservations", JsonValue.Array(snapshot["reservations"].Items.Skip(1)))));
            Assert.Catch(() => rig.Factory.Restore(snapshot.With("scenario_tick", JsonValue.Number(snapshot["scenario_tick"].Integer + 1))));
            Assert.Catch(() => rig.Factory.Restore(snapshot.With("event_state", snapshot["event_state"].With("fired_event_ids", A("surge")))));
            Assert.Catch(() => rig.Factory.Restore(snapshot.With("objective_monitors", JsonValue.Array(snapshot["objective_monitors"].Items.Skip(1)))));
            Assert.Throws<SaveRunIncompatibleException>(() => rig.Factory.Restore(snapshot.With("identity", snapshot["identity"].With("source_build_id", JsonValue.String("unavailable-build")))));
            Assert.Throws<SaveRunIncompatibleException>(() => rig.Factory.Restore(snapshot.With("identity", snapshot["identity"].With("content_hash", JsonValue.String(new string('0', 64))))));
            var text = System.Text.Encoding.UTF8.GetString(player.CanonicalBytes());
            foreach (string field in new[] { "camera_pose", "raw_hand_pose", "room_mesh", "microphone", "account_token", "email" }) Assert.IsFalse(text.Contains("\"" + field + "\""));
            var settings = player.ToJson()["settings"].With("captions", JsonValue.Bool(false));
            Assert.AreEqual(snapshot.Canonical(), player.WithSettings(settings).CurrentRun.Canonical());
        }
    }
}
