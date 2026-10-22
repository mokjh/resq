using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Resq.Content;
using Resq.Persistence;
using Resq.Progression;

namespace Resq.Tests.Persistence
{
    // Root-owned registered cases call these helpers. This file intentionally adds
    // no NUnit case/category/acceptance IDs of its own.
    public static class SaveStoreChecks
    {
        public static void AtomicGenerationsAndCorruption()
        {
            using (var sandbox = new Sandbox())
            {
                GenerationsAndQueue(sandbox.Child("generations"));
                SettingsAndRetention(sandbox.Child("settings"));
                foreach (Fault fault in new[] { Fault.PendingWrite, Fault.PendingTruncation, Fault.PreviousCopy,
                    Fault.PreviousPromotion, Fault.CurrentPromotion, Fault.CurrentPromotionAfterReplace })
                    InterruptedWrite(sandbox.Child("fault-" + fault), fault);
                WarningEpisodes(sandbox.Child("warning"));
                StrictFilesAndFutureSchema(sandbox.Child("strict"));
                ConfirmedResetArchives(sandbox.Child("reset"));
            }
        }

        public static void RecoveryAssistance(PlayerSaveSnapshot snapshot, Action<JsonValue> validator)
        {
            Assert.IsNotNull(snapshot); Assert.IsNotNull(validator);
            Assert.IsFalse(snapshot.CurrentRun.IsNull, "This check requires the coordinator's actual complete run snapshot");
            Assert.IsFalse(snapshot.CurrentRun["identity"]["assisted"].Boolean, "Fixture must start as an unassisted attempt");
            using (var sandbox = new Sandbox())
            {
                string path = sandbox.Child("run-recovery");
                var store = new SaveStore(path, validator);
                Persist(store, snapshot);
                Persist(store, snapshot.WithSettings(snapshot.ToJson()["settings"].With("text_scale_percent", JsonValue.Number(125))));
                var restarted = new SaveStore(path, validator);
                SaveReadResult ordinary = restarted.ReadValidated();
                Assert.AreEqual(SaveReadStatus.Valid, ordinary.Status);
                Assert.IsTrue(ordinary.RequiresPausedContinue);
                Assert.IsFalse(ordinary.Snapshot.CurrentRun["identity"]["assisted"].Boolean, "Ordinary resume never adds assistance");
                Assert.AreEqual(snapshot.CurrentRun.Canonical(), ordinary.Snapshot.CurrentRun.Canonical());

                string current = Path.Combine(path, SaveStore.CurrentFilename);
                string previous = Path.Combine(path, SaveStore.PreviousFilename);
                byte[] exactBackup = File.ReadAllBytes(previous);
                File.WriteAllText(current, "{\"truncated\":", new UTF8Encoding(false));
                SaveReadResult recovery = restarted.ReadValidated();
                Assert.AreEqual(SaveReadStatus.RecoveryRequired, recovery.Status);
                Assert.AreEqual(1, recovery.Decision.BackupGeneration);
                SaveWriteHandle noConsent = restarted.WriteGeneration(snapshot); restarted.Pump();
                Assert.AreEqual(SaveWriteStatus.Rejected, restarted.Poll(noConsent).Status);
                CollectionAssert.AreEqual(exactBackup, File.ReadAllBytes(previous));
                SaveWriteHandle approved = restarted.RecoverPrevious(recovery.Decision);
                Assert.AreEqual(SaveWriteStatus.Queued, restarted.Poll(approved).Status);
                restarted.Pump();
                Assert.AreEqual(SaveWriteStatus.Succeeded, restarted.Poll(approved).Status);
                Assert.IsTrue(restarted.Poll(approved).IsRecovery);
                SaveReadResult restored = restarted.ReadValidated();
                Assert.AreEqual(SaveReadStatus.Valid, restored.Status);
                Assert.IsTrue(restored.Snapshot.CurrentRun["identity"]["assisted"].Boolean);
                JsonValue expectedRun = snapshot.CurrentRun.With("identity", snapshot.CurrentRun["identity"].With("assisted", JsonValue.Bool(true)));
                Assert.AreEqual(expectedRun.Canonical(), restored.Snapshot.CurrentRun.Canonical(), "Recovery preserves every causal field and clock while marking this attempt");
                Assert.IsTrue(restored.RequiresPausedContinue);
                Assert.IsTrue(restored.Snapshot.ToJson()["application_recovery"]["layout_reconfirmation_required"].Boolean);
                Assert.IsFalse(snapshot.CurrentRun["identity"]["assisted"].Boolean, "Recovery cannot mutate an earlier immutable snapshot");
                CollectionAssert.AreEqual(exactBackup, File.ReadAllBytes(previous));

                byte[] recoveredBytes = File.ReadAllBytes(current);
                SaveWriteHandle clearAssistance = restarted.WriteGeneration(snapshot); restarted.Pump();
                Assert.AreEqual(SaveWriteStatus.Failed, restarted.Poll(clearAssistance).Status);
                Assert.AreEqual("SAVE_ASSISTANCE", restarted.Poll(clearAssistance).ReasonCode);
                CollectionAssert.AreEqual(recoveredBytes, File.ReadAllBytes(current));

                SaveDecisionTicket stale = restored.Decision;
                Assert.IsNotNull(stale);
                Persist(restarted, restored.Snapshot.WithSettings(restored.Snapshot.ToJson()["settings"].With("text_scale_percent", JsonValue.Number(150))));
                byte[] newest = File.ReadAllBytes(current);
                SaveWriteHandle staleRecovery = restarted.RecoverPrevious(stale); restarted.Pump();
                Assert.AreEqual(SaveWriteStatus.Rejected, restarted.Poll(staleRecovery).Status);
                Assert.AreEqual("SAVE_DECISION_STALE", restarted.Poll(staleRecovery).ReasonCode);
                CollectionAssert.AreEqual(newest, File.ReadAllBytes(current));
                var otherOwner = new SaveStore(path, validator);
                Assert.Throws<ArgumentException>(() => otherOwner.RecoverPrevious(stale));
                OlderIncompatibleBackup(sandbox.Child("older-incompatible-backup"), snapshot, validator);
            }
        }

        private static void OlderIncompatibleBackup(string path, PlayerSaveSnapshot snapshot, Action<JsonValue> validator)
        {
            var store = new SaveStore(path, validator); Persist(store, snapshot); Persist(store, snapshot);
            string current = Path.Combine(path, SaveStore.CurrentFilename), previous = Path.Combine(path, SaveStore.PreviousFilename);
            byte[] newest = File.ReadAllBytes(current);
            var old = StrictJson.Parse(File.ReadAllBytes(previous));
            var incompatibleRun = snapshot.CurrentRun.With("identity", snapshot.CurrentRun["identity"].With("source_build_id", JsonValue.String("old-unavailable-build")));
            var payload = old["payload"].With("current_run", incompatibleRun);
            old = old.With("payload", payload).With("payload_sha256", JsonValue.String(payload.Sha256()));
            File.WriteAllBytes(previous, old.CanonicalBytes()); byte[] oldBytes = File.ReadAllBytes(previous);
            var read = new SaveStore(path, validator).ReadValidated();
            Assert.AreEqual(SaveReadStatus.Valid, read.Status); Assert.AreEqual(SaveSource.Current, read.Source); Assert.AreEqual(2, read.Generation);
            Assert.AreEqual(SaveFileStatus.IncompatibleRun, read.Previous.Status);
            CollectionAssert.AreEqual(newest, File.ReadAllBytes(current)); CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(previous));
            File.WriteAllBytes(previous, old.With("generation", JsonValue.Number(3)).CanonicalBytes());
            Assert.AreEqual(SaveReadStatus.IncompatibleRun, store.ReadValidated().Status, "A newer incompatible attempt cannot be silently replaced");
            File.WriteAllBytes(previous, oldBytes);
            Persist(store, snapshot.WithSettings(snapshot.ToJson()["settings"].With("text_scale_percent", JsonValue.Number(150))));
            Assert.AreEqual(3, store.ReadValidated().Generation); CollectionAssert.AreEqual(newest, File.ReadAllBytes(previous));
        }

        private static void GenerationsAndQueue(string path)
        {
            var store = new SaveStore(path, null);
            var fresh = PlayerSaveSnapshot.Fresh("profile:queue");
            var newest = fresh.WithSettings(fresh.ToJson()["settings"].With("captions", JsonValue.Bool(false)));
            SaveWriteHandle first = store.WriteGeneration(fresh), second = store.WriteGeneration(newest);
            Assert.AreEqual(SaveWriteStatus.Coalesced, store.Poll(first).Status);
            Assert.AreEqual(SaveWriteStatus.Queued, store.Poll(second).Status);
            Assert.IsFalse(File.Exists(Path.Combine(path, SaveStore.CurrentFilename)), "Queuing and polling perform no hidden disk write");
            Assert.IsTrue(store.Pump()); Assert.IsFalse(store.Pump());
            Assert.AreEqual(SaveWriteStatus.Succeeded, store.Poll(second).Status);
            Assert.AreEqual(1, store.Poll(second).Generation);
            byte[] firstGeneration = File.ReadAllBytes(Path.Combine(path, SaveStore.CurrentFilename));
            Assert.IsFalse(store.ReadValidated().Snapshot.ToJson()["settings"]["captions"].Boolean);
            Persist(store, fresh);
            Assert.AreEqual(2, store.ReadValidated().Generation);
            CollectionAssert.AreEqual(firstGeneration, File.ReadAllBytes(Path.Combine(path, SaveStore.PreviousFilename)), "Previous is the exact prior full generation");
            Assert.IsFalse(File.Exists(Path.Combine(path, SaveStore.PendingFilename)));
            Assert.IsFalse(File.Exists(Path.Combine(path, "save.previous.pending.json")));
            Assert.Throws<ArgumentException>(() => new SaveStore(path, null).Poll(second));
        }

        private static void InterruptedWrite(string path, Fault fault)
        {
            var injection = new FaultFiles(); var warnings = new List<string>();
            var store = new SaveStore(path, null, injection, warnings.Add);
            var fresh = PlayerSaveSnapshot.Fresh("profile:fault");
            Persist(store, fresh);
            var prior = fresh.WithSettings(fresh.ToJson()["settings"].With("text_scale_percent", JsonValue.Number(125)));
            Persist(store, prior);
            string current = Path.Combine(path, SaveStore.CurrentFilename), previous = Path.Combine(path, SaveStore.PreviousFilename);
            byte[] beforeCurrent = File.ReadAllBytes(current), beforePrevious = File.ReadAllBytes(previous);
            injection.Arm = fault;
            var next = prior.WithSettings(prior.ToJson()["settings"].With("text_scale_percent", JsonValue.Number(150)));
            SaveWriteHandle interrupted = store.WriteGeneration(next); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Failed, store.Poll(interrupted).Status, fault.ToString());
            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual(SaveReadStatus.Valid, store.ReadValidated().Status, "An interrupted write leaves a readable supported generation");
            if (fault == Fault.CurrentPromotionAfterReplace)
            {
                CollectionAssert.AreEqual(beforeCurrent, File.ReadAllBytes(previous));
                Assert.AreEqual(next.PayloadSha256, store.ReadValidated().PayloadSha256, "An exception after actual promotion is not fabricated success, but disk truth remains inspectable");
            }
            else
            {
                CollectionAssert.AreEqual(beforeCurrent, File.ReadAllBytes(current), fault + " must retain current bytes");
                if (fault == Fault.CurrentPromotion) CollectionAssert.AreEqual(beforeCurrent, File.ReadAllBytes(previous));
                else CollectionAssert.AreEqual(beforePrevious, File.ReadAllBytes(previous));
            }
        }

        private static void WarningEpisodes(string path)
        {
            var files = new FaultFiles(); var warnings = new List<string>();
            var store = new SaveStore(path, null, files, warnings.Add);
            var snapshot = PlayerSaveSnapshot.Fresh("profile:warning"); Persist(store, snapshot);
            files.Arm = Fault.PendingWrite; var first = store.WriteGeneration(snapshot); store.Pump();
            files.Arm = Fault.PendingTruncation; var second = store.WriteGeneration(snapshot); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Failed, store.Poll(first).Status);
            Assert.AreEqual(SaveWriteStatus.Failed, store.Poll(second).Status);
            Assert.AreEqual(1, warnings.Count, "Consecutive runtime storage failures warn once");
            Persist(store, snapshot);
            files.Arm = Fault.CurrentPromotion; var afterSuccess = store.WriteGeneration(snapshot); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Failed, store.Poll(afterSuccess).Status);
            Assert.AreEqual(2, warnings.Count, "A successful promotion ends the prior failure episode");
        }

        private static void StrictFilesAndFutureSchema(string path)
        {
            var store = new SaveStore(path, null); var fresh = PlayerSaveSnapshot.Fresh("profile:strict");
            Persist(store, fresh); Persist(store, fresh);
            string current = Path.Combine(path, SaveStore.CurrentFilename), previous = Path.Combine(path, SaveStore.PreviousFilename);
            byte[] baseline = File.ReadAllBytes(current), exactBackup = File.ReadAllBytes(previous);
            JsonValue envelope = StrictJson.Parse(baseline);
            string reordered = "{" + string.Join(",", envelope.Members.Reverse().Select(pair => JsonValue.String(pair.Key).Canonical() + ":" + pair.Value.Canonical())) + "}";
            File.WriteAllText(current, reordered, new UTF8Encoding(false));
            Assert.AreEqual(SaveReadStatus.Valid, store.ReadValidated().Status, "Object-key order does not change canonical payload integrity");
            Assert.AreEqual(fresh.PayloadSha256, store.ReadValidated().PayloadSha256);
            File.WriteAllText(current, "{\"schema_version\":1,\"schema_version\":1}", new UTF8Encoding(false));
            Assert.AreEqual("DUPLICATE_KEY", store.ReadValidated().Current.ReasonCode);
            Assert.AreEqual(SaveReadStatus.RecoveryRequired, store.ReadValidated().Status);
            JsonValue privatePayload = fresh.ToJson().With("room_mesh", JsonValue.String("disallowed"));
            File.WriteAllBytes(current, Envelope(privatePayload, 3, 1).CanonicalBytes());
            Assert.AreEqual(SaveFileStatus.Corrupt, store.ReadValidated().Current.Status, "Hash-valid private/unknown fields are still rejected");
            Assert.AreEqual(SaveReadStatus.RecoveryRequired, store.ReadValidated().Status);
            JsonValue future = Envelope(fresh.ToJson(), 4, 2).With("future_framing", JsonValue.Bool(true));
            byte[] futureBytes = future.CanonicalBytes(); File.WriteAllBytes(current, futureBytes);
            SaveReadResult incompatible = store.ReadValidated();
            Assert.AreEqual(SaveReadStatus.IncompatibleSchema, incompatible.Status);
            Assert.AreEqual(SaveFileStatus.Valid, incompatible.Previous.Status, "Readable old backup does not authorize downgrading a future save");
            Assert.IsNull(incompatible.Decision);
            SaveWriteHandle blocked = store.WriteGeneration(fresh); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Rejected, store.Poll(blocked).Status);
            CollectionAssert.AreEqual(futureBytes, File.ReadAllBytes(current));
            CollectionAssert.AreEqual(exactBackup, File.ReadAllBytes(previous));
            JsonValue badFuture = future.With("payload_sha256", JsonValue.String(new string('0', 64)));
            File.WriteAllBytes(current, badFuture.CanonicalBytes());
            Assert.AreEqual(SaveFileStatus.Corrupt, store.ReadValidated().Current.Status, "A future version tag cannot hide failed integrity");
            Assert.AreEqual(SaveReadStatus.RecoveryRequired, store.ReadValidated().Status);
            File.WriteAllBytes(current, envelope.With("schema_version", JsonValue.Number(0)).CanonicalBytes());
            Assert.AreEqual(SaveFileStatus.Corrupt, store.ReadValidated().Current.Status, "Schema 1 has no predecessor migration");
            Assert.AreEqual(SaveReadStatus.RecoveryRequired, store.ReadValidated().Status);
            CollectionAssert.AreEqual(exactBackup, File.ReadAllBytes(previous));
            File.WriteAllBytes(current, baseline);
            File.WriteAllBytes(previous, futureBytes);
            Assert.AreEqual(SaveReadStatus.IncompatibleSchema, store.ReadValidated().Status, "A future previous file is preserved as well");
            SaveWriteHandle previousFuture = store.WriteGeneration(fresh); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Rejected, store.Poll(previousFuture).Status);
            CollectionAssert.AreEqual(baseline, File.ReadAllBytes(current));
            CollectionAssert.AreEqual(futureBytes, File.ReadAllBytes(previous));
        }

        private static void ConfirmedResetArchives(string path)
        {
            Directory.CreateDirectory(path);
            string current = Path.Combine(path, SaveStore.CurrentFilename), previous = Path.Combine(path, SaveStore.PreviousFilename), pending = Path.Combine(path, SaveStore.PendingFilename);
            byte[] invalidCurrent = Encoding.UTF8.GetBytes("{bad current"), invalidPrevious = Encoding.UTF8.GetBytes("[bad previous"), incompletePending = Encoding.UTF8.GetBytes("{partial pending");
            File.WriteAllBytes(current, invalidCurrent); File.WriteAllBytes(previous, invalidPrevious); File.WriteAllBytes(pending, incompletePending);
            var store = new SaveStore(path, null); var fresh = PlayerSaveSnapshot.Fresh("profile:reset");
            SaveReadResult decision = store.ReadValidated();
            Assert.AreEqual(SaveReadStatus.NewProfileConfirmationRequired, decision.Status);
            var forbidden = store.WriteGeneration(fresh); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Rejected, store.Poll(forbidden).Status);
            CollectionAssert.AreEqual(invalidCurrent, File.ReadAllBytes(current)); CollectionAssert.AreEqual(invalidPrevious, File.ReadAllBytes(previous));
            Assert.IsFalse(Directory.Exists(Path.Combine(path, "save.recovery-archive")), "Reading and a refused write cannot create a reset archive");
            SaveWriteHandle allowed = store.AuthorizeNewProfile(decision.Decision, fresh); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Succeeded, store.Poll(allowed).Status);
            Assert.AreEqual(SaveReadStatus.Valid, store.ReadValidated().Status);
            Assert.AreEqual(fresh.PayloadSha256, store.ReadValidated().PayloadSha256);
            string archive = Directory.GetDirectories(Path.Combine(path, "save.recovery-archive")).Single();
            CollectionAssert.AreEqual(invalidCurrent, File.ReadAllBytes(Path.Combine(archive, SaveStore.CurrentFilename)));
            CollectionAssert.AreEqual(invalidPrevious, File.ReadAllBytes(Path.Combine(archive, SaveStore.PreviousFilename)));
            CollectionAssert.AreEqual(incompletePending, File.ReadAllBytes(Path.Combine(archive, SaveStore.PendingFilename)));
        }

        private static void SettingsAndRetention(string path)
        {
            var fresh = PlayerSaveSnapshot.Fresh("profile:settings"); var store = new SaveStore(path, null);
            Assert.AreEqual(PlayerSaveSnapshot.DefaultSettings().Canonical(), fresh.ToJson()["settings"].Canonical());
            var minimum = fresh.ToJson()["settings"]
                .With("preferred_rule_profile", JsonValue.String("relaxed")).With("preferred_input_adapter", JsonValue.String("controllers"))
                .With("preferred_hand", JsonValue.String("left")).With("dwell_ms", JsonValue.Number(500))
                .With("caption_background_opacity_percent", JsonValue.Number(70)).With("highlight_intensity_percent", JsonValue.Number(40))
                .With("captions", JsonValue.Bool(false)).With("reduced_effects", JsonValue.Bool(true)).With("preferred_layout", JsonValue.String("compact"))
                .With("audio_music", JsonValue.Number(0)).With("audio_world", JsonValue.Number(0)).With("audio_warnings", JsonValue.Number(0)).With("audio_speech", JsonValue.Number(0));
            var maximum = minimum.With("preferred_rule_profile", JsonValue.String("command")).With("preferred_input_adapter", JsonValue.String("head_dwell"))
                .With("dwell_ms", JsonValue.Number(1500)).With("text_scale_percent", JsonValue.Number(150))
                .With("caption_text_scale_percent", JsonValue.Number(150)).With("caption_background_opacity_percent", JsonValue.Number(100))
                .With("target_scale_percent", JsonValue.Number(150)).With("highlight_intensity_percent", JsonValue.Number(100))
                .With("audio_music", JsonValue.Number(100)).With("audio_world", JsonValue.Number(100)).With("audio_warnings", JsonValue.Number(100)).With("audio_speech", JsonValue.Number(100));
            foreach (JsonValue setting in new[] { fresh.ToJson()["settings"], minimum, maximum })
            {
                var snapshot = fresh.WithSettings(setting); Persist(store, snapshot);
                Assert.AreEqual(setting.Canonical(), new SaveStore(path, null).ReadValidated().Snapshot.ToJson()["settings"].Canonical(), "Every field survives an actual disk restart");
            }
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("dwell_ms", JsonValue.Number(501))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("audio_music", JsonValue.Number(3))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("text_scale_percent", JsonValue.Number(110))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("caption_text_scale_percent", JsonValue.Number(110))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("caption_background_opacity_percent", JsonValue.Number(75))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("target_scale_percent", JsonValue.Number(125))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("highlight_intensity_percent", JsonValue.Number(75))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.With("captions", JsonValue.Number(1))));
            Assert.Catch<Exception>(() => fresh.WithSettings(minimum.Without("preferred_hand")));
            Assert.Catch<Exception>(() => PlayerSaveSnapshot.Create(fresh.ToJson().With("account_token", JsonValue.String("disallowed"))));
            Assert.Catch<Exception>(() => fresh.WithRecovery(fresh.ToJson()["application_recovery"].With("raw_hand_pose", JsonValue.Array(new JsonValue[0]))));

            var grant = new RewardGrant("campaign:postcard", "postcard:one", "postcard");
            ProgressionLedger ledger = ProgressionLedger.Empty();
            for (int index = 0; index < 70; index++)
                ledger = ledger.Apply("W1-M1", Summary("run:" + index, "mission:v1", index), new[] { grant });
            var retained = fresh.WithProgression(ledger); Persist(store, retained);
            var read = new SaveStore(path, null).ReadValidated().Snapshot;
            Assert.AreEqual(64, read.ToJson()["completed_runs"].Items.Count);
            Assert.AreEqual("run:6", read.ToJson()["completed_runs"].Items[0]["identity"]["run_id"].Text);
            Assert.AreEqual("run:69", read.ToJson()["completed_runs"].Items[63]["identity"]["run_id"].Text);
            Assert.AreEqual(1, read.ToJson()["reward_receipts"].Items.Count);
            Assert.AreEqual("run:0", read.ToJson()["reward_receipts"].Items[0]["source_run_id"].Text);
            Assert.IsTrue(read.ReadProgression().IsCampaignMissionCompleted("W1-M1"));
            var losingProgress = store.WriteGeneration(fresh); store.Pump();
            Assert.AreEqual(SaveWriteStatus.Failed, store.Poll(losingProgress).Status);
            Assert.AreEqual(retained.PayloadSha256, store.ReadValidated().PayloadSha256);
            var twoPartitions = ledger.Apply("W2-M1", Summary("run:new", "mission:v2", 100), new RewardGrant[0]);
            var sorted = fresh.WithProgression(twoPartitions);
            Assert.Catch<Exception>(() => PlayerSaveSnapshot.Create(sorted.ToJson().With("mission_progress", JsonValue.Array(sorted.ToJson()["mission_progress"].Items.Reverse()))));
        }

        private static SaveWriteResult Persist(SaveStore store, PlayerSaveSnapshot snapshot)
        {
            SaveWriteHandle handle = store.WriteGeneration(snapshot); Assert.IsTrue(store.Pump());
            SaveWriteResult result = store.Poll(handle);
            Assert.AreEqual(SaveWriteStatus.Succeeded, result.Status, result.ReasonCode + ": " + result.Detail);
            return result;
        }
        private static JsonValue Envelope(JsonValue payload, long generation, long schema) => Object(
            "schema_version", JsonValue.Number(schema), "generation", JsonValue.Number(generation),
            "payload_sha256", JsonValue.String(payload.Sha256()), "payload", payload);
        private static JsonValue Summary(string runId, string missionVersion, long score)
        {
            JsonValue identity = Object("run_id", JsonValue.String(runId), "source_build_id", JsonValue.String("synthetic-save-store-check"),
                "mission_version_id", JsonValue.String(missionVersion), "rules_version", JsonValue.String("resq-rules-1"),
                "content_hash", JsonValue.String(new string('a', 64)), "layout_version_id", JsonValue.String("synthetic-layout"),
                "layout_signature", JsonValue.String(new string('b', 64)), "rule_profile_id", JsonValue.String("standard"),
                "input_adapter_id", JsonValue.String("hands"), "guidance_policy", JsonValue.String("unguided"), "challenge_id", JsonValue.String("base"),
                "modifiers_hash", JsonValue.String(StrictJson.Parse("[]").Sha256()), "seed", JsonValue.Number(1), "assisted", JsonValue.Bool(false));
            return Object("identity", identity, "success", JsonValue.Bool(true), "stars", JsonValue.Number(1), "score", JsonValue.Number(score),
                "rescued", JsonValue.Number(1), "backed_up", JsonValue.Number(0), "final_operation_tick", JsonValue.Number(100),
                "final_scenario_tick", JsonValue.Number(100), "final_state_hash", JsonValue.String(new string('c', 64)));
        }
        private static JsonValue Object(params object[] pairs)
        {
            var value = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int index = 0; index < pairs.Length; index += 2) value.Add((string)pairs[index], (JsonValue)pairs[index + 1]);
            return JsonValue.Object(value);
        }

        private enum Fault { None, PendingWrite, PendingTruncation, PreviousCopy, PreviousPromotion, CurrentPromotion, CurrentPromotionAfterReplace }
        private sealed class FaultFiles : ISaveFileSystem
        {
            private readonly PhysicalSaveFileSystem actual = new PhysicalSaveFileSystem();
            public Fault Arm;
            public bool Exists(string path) => actual.Exists(path);
            public byte[] ReadAllBytes(string path, int maximumBytes) => actual.ReadAllBytes(path, maximumBytes);
            public string FileSha256(string path) => actual.FileSha256(path);
            public void CreateDirectory(string path) => actual.CreateDirectory(path);
            public void WriteAndFlush(string path, byte[] bytes, bool createNew)
            {
                if (Path.GetFileName(path) == SaveStore.PendingFilename && Take(Fault.PendingWrite)) throw new IOException("Injected pending write failure");
                if (Path.GetFileName(path) == SaveStore.PendingFilename && Take(Fault.PendingTruncation))
                { actual.WriteAndFlush(path, bytes.Take(7).ToArray(), createNew); return; }
                actual.WriteAndFlush(path, bytes, createNew);
            }
            public void CopyAndFlush(string source, string destination, bool createNew)
            {
                if (Path.GetFileName(destination) == "save.previous.pending.json" && Take(Fault.PreviousCopy)) throw new IOException("Injected previous copy failure");
                actual.CopyAndFlush(source, destination, createNew);
            }
            public void AtomicReplace(string source, string destination)
            {
                if (Path.GetFileName(destination) == SaveStore.PreviousFilename && Take(Fault.PreviousPromotion)) throw new IOException("Injected previous promotion failure");
                if (Path.GetFileName(destination) == SaveStore.CurrentFilename && Take(Fault.CurrentPromotion)) throw new IOException("Injected current promotion failure");
                bool after = Path.GetFileName(destination) == SaveStore.CurrentFilename && Take(Fault.CurrentPromotionAfterReplace);
                actual.AtomicReplace(source, destination);
                if (after) throw new IOException("Injected failure after actual current promotion");
            }
            private bool Take(Fault fault) { if (Arm != fault) return false; Arm = Fault.None; return true; }
        }
        private sealed class Sandbox : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "resq-c07-save-store-" + Guid.NewGuid().ToString("N"));
            public Sandbox() { Directory.CreateDirectory(directory); }
            public string Child(string name) => Path.Combine(directory, name);
            public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
