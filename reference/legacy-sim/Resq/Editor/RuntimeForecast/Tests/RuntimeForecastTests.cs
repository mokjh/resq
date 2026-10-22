using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Editor.Replay;
using Resq.Simulation;
using Resq.Simulation.Forecast;

namespace Resq.Tests.Forecast
{
    [TestFixture, Category("runtime_forecast")]
    public sealed class RuntimeForecastTests
    {
        private static JsonValue O(params object[] p) => ForecastFixture.O(p);
        private static JsonValue A(params string[] p) => ForecastFixture.A(p);
        private static JsonValue Place(string socket = "beacon:01", string equipment = "beacon") => O("equipment_id", equipment, "socket_id", socket, "orientation_step", 0);
        internal static void Step(OperationClock clock) { Assert.IsNotNull(clock.Step(), clock.Fault ?? "Actual simulator did not advance"); }
        internal static ForecastRequest Request(ForecastFixture.Rig rig, GameCommand preview, long id = 1, int horizon = 300)
        { var state = rig.Clock.ReadSnapshot().Value; return new ForecastRequest(id, state["state_revision"].Integer, state, preview, horizon); }
        internal static ForecastService Service(ForecastFixture.Rig rig) => new ForecastService(rig.Factory, () => rig.Clock.StateRevision, () => true, () => rig.Clock.ApplicationState);
        internal static ForecastResult Finish(ForecastService service, long id = 1, long start = 150, WholeCallWitness witness = null)
        {
            ForecastResult result = null;
            // Presentation frames are no longer operation ticks. The immutable
            // 1200 completed-operation ceiling is asserted by the worker/results.
            for (int frame = 0; frame < 2000000; frame++)
            {
                int collectionStart = GC.CollectionCount(0); long heapBefore = GC.GetTotalMemory(false);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), began = Stopwatch.GetTimestamp();
                try { service.AdvanceFrame(start + frame); result = service.Poll(id); }
                catch { if (witness != null) witness.LastObservedForecastStatus = "exception"; throw; }
                finally
                {
                    long elapsed = (Stopwatch.GetTimestamp() - began) * 1000000L / Stopwatch.Frequency;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    witness?.Record("advance_and_poll", elapsed, bytes, service.CurrentWorkStage, GC.CollectionCount(0) - collectionStart, heapBefore);
                }
                if (witness != null) witness.LastObservedForecastStatus = result.Status;
                if (result.Status != "pending") break;
            }
            Assert.AreNotEqual("pending", result.Status, "Cooperative worker must settle without shortening its horizon"); return result;
        }
        internal sealed class WholeCallWitness
        {
            internal long Frames, MaximumMicroseconds, Overruns, AllocatedBytes, PeakManagedHeapBytes;
            internal long ObservedGen0Collections, OverrunsWithObservedCollection;
            internal string SlowestStage;
            internal string LastObservedForecastStatus = "not_observed";
            private readonly List<JsonValue> firstSamples = new List<JsonValue>();
            private readonly List<JsonValue> firstOverruns = new List<JsonValue>();
            internal void Record(string kind, long elapsed, long allocated, string stage, int collections = 0, long heapBefore = 0)
            {
                Frames++; AllocatedBytes += allocated;
                if (elapsed > MaximumMicroseconds) { MaximumMicroseconds = elapsed; SlowestStage = stage; }
                long heapAfter = GC.GetTotalMemory(false); ObservedGen0Collections += collections;
                if (elapsed > ForecastService.WorkSliceMicroseconds)
                {
                    Overruns++; if (collections > 0) OverrunsWithObservedCollection++;
                    if (firstOverruns.Count < 128) firstOverruns.Add(O("call", kind, "microseconds", elapsed, "stage", stage,
                        "observed_gen0_collection_delta", collections, "heap_before_bytes", heapBefore, "heap_after_bytes", heapAfter));
                }
                PeakManagedHeapBytes = Math.Max(PeakManagedHeapBytes, Math.Max(heapBefore, heapAfter));
                if (firstSamples.Count < 128) firstSamples.Add(O("call", kind, "microseconds", elapsed,
                    "allocated_bytes", ForecastService.AllocationCounterAvailable ? (object)allocated : null, "stage", stage));
            }
            internal void Measure(string kind, Action action)
            {
                int collections = GC.CollectionCount(0); long heap = GC.GetTotalMemory(false);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), began = Stopwatch.GetTimestamp();
                try { action(); }
                finally
                {
                    Record(kind, (Stopwatch.GetTimestamp() - began) * 1000000L / Stopwatch.Frequency,
                        GC.GetAllocatedBytesForCurrentThread() - allocated, kind, GC.CollectionCount(0) - collections, heap);
                }
            }
            internal JsonValue Json() => O("calls", Frames, "maximum_microseconds", MaximumMicroseconds, "budget_overruns", Overruns,
                "last_observed_forecast_status", LastObservedForecastStatus,
                "allocation_counter_available", ForecastService.AllocationCounterAvailable,
                "allocated_bytes", ForecastService.AllocationCounterAvailable ? (object)AllocatedBytes : null,
                "endpoint_sampled_process_managed_heap_peak_bytes", PeakManagedHeapBytes, "observed_gen0_collections", ObservedGen0Collections,
                "timing_scope", "service_activation_request_creation_begin_advance_and_poll_after_fixture_preparation",
                "shared_core_already_exercised_by_fixture_preparation", true,
                "overruns_with_observed_gen0_collection", OverrunsWithObservedCollection,
                "slowest_stage", SlowestStage, "first_call_samples", JsonValue.Array(firstSamples), "samples_truncated", Frames > firstSamples.Count,
                "first_overruns", JsonValue.Array(firstOverruns), "overrun_samples_truncated", Overruns > firstOverruns.Count);
        }
        private static void AssertCooperativeClockBoundaries()
        {
            foreach (bool relaxed in new[] { false, true })
            {
                var rig = ForecastFixture.Build(relaxed: relaxed); rig.StartVehicleRoute();
                for (int n = 0; n < 5; n++) Step(rig.Clock);
                Assert.AreEqual("queued", rig.Clock.Admit(rig.Command("place_equipment", Place())).Status);
                var initial = rig.Clock.ReadSnapshot();
                var reference = rig.Factory.RestoreForReplay(initial.Value); var expected = reference.Step(); Assert.IsNotNull(expected, reference.Fault);
                var resumed = rig.Factory.RestoreForReplay(initial.Value); var stages = new HashSet<string>(StringComparer.Ordinal);
                using (var work = resumed.StepWork())
                {
                    while (work.MoveNext())
                    {
                        Assert.AreEqual(initial.Value["operation_tick"].Integer, resumed.OperationTick, "Partial clock cannot publish");
                        Assert.AreEqual(initial.Value["state_revision"].Integer, resumed.StateRevision, "Partial revision cannot publish");
                        stages.Add(work.Stage);
                    }
                    Assert.AreEqual(expected.StateSha256, work.Result.StateSha256);
                    Assert.AreEqual(reference.ReadSnapshot().Value.Canonical(), resumed.ConsumeCompletedStepSnapshot(work.Result).Value.Canonical());
                }
                Assert.IsTrue(stages.Contains("operation.ready_to_commit"));
                Assert.IsTrue(stages.Contains("equipment.command.applied_private"), "Cancellation coverage must include a real applied private command");
                // Cancel at every distinct partial stage (all repeated unit
                // boundaries were forced above), including completed hash work.
                foreach (string stop in stages)
                {
                    var cancelled = rig.Factory.RestoreForReplay(initial.Value); bool found = false;
                    using (var work = cancelled.StepWork())
                    {
                        while (work.MoveNext()) if (work.Stage == stop) { found = true; break; }
                    }
                    Assert.IsTrue(found); Assert.IsNull(cancelled.Fault);
                    Assert.AreEqual(initial.Value.Canonical(), cancelled.ReadSnapshot().Value.Canonical(), "Cancelled private stage changed authority: " + stop);
                    Assert.AreEqual(expected.StateSha256, cancelled.Step().StateSha256, "Cancel cannot change continuation: " + stop);
                }
                Assert.AreEqual(initial.Value.Canonical(), rig.Clock.ReadSnapshot().Value.Canonical());
                var interleaved = rig.Factory.RestoreForReplay(initial.Value);
                Assert.AreEqual("queued", interleaved.Admit(rig.Command("place_equipment", Place())).Status);
                using (var read = interleaved.ReadSnapshotWork())
                {
                    bool suspended = false;
                    while (read.MoveNext()) if (read.Stage == "snapshot.pending_command") { suspended = true; break; }
                    Assert.IsTrue(suspended, "Read must suspend after old pending/clock fields were captured");
                    Step(interleaved);
                    Assert.Throws<InvalidOperationException>(() => read.MoveNext(), "A mixed boundary must never publish");
                }
                var stable = interleaved.ReadSnapshot();
                Assert.AreEqual(stable.Value.Canonical(), rig.Factory.RestoreForReplay(stable.Value).ReadSnapshot().Value.Canonical());
                using (var read = interleaved.ReadSnapshotWork()) { Assert.IsTrue(read.MoveNext()); }
                Step(interleaved); // Cancelling a read must not retain a mutation lock.
            }
        }

        private static void AssertCooperativeCommandAdmission()
        {
            foreach (bool relaxed in new[] { false, true })
            foreach (bool allowed in new[] { true, false })
            {
                var rig = ForecastFixture.Build(relaxed: relaxed); rig.Start();
                var initial = rig.Clock.ReadSnapshot();
                var command = rig.Command("place_equipment", Place(allowed ? "beacon:01" : "beacon:missing"));
                var reference = rig.Factory.RestoreForReplay(initial.Value);
                var expected = reference.Admit(command);
                Assert.AreEqual(allowed ? "queued" : "rejected", expected.Status);
                var resumed = rig.Factory.RestoreForReplay(initial.Value);
                var stages = new HashSet<string>(StringComparer.Ordinal);
                using (var work = resumed.AdmitWork(command))
                {
                    Assert.AreEqual(initial.StateSha256, resumed.ReadSnapshot().StateSha256, "Creating work is lazy");
                    while (work.MoveNext())
                    {
                        Assert.AreEqual(rig.Clock.StateRevision, resumed.StateRevision, "Partial admission cannot publish a revision");
                        Assert.AreEqual(rig.Clock.NextSequence, resumed.NextSequence, "Partial admission cannot publish a ledger entry");
                        Assert.IsNull(resumed.ReadReceipt(command.CommandId));
                        if (stages.Add(work.Stage))
                        {
                            Assert.Throws<InvalidOperationException>(() => resumed.Admit(command));
                            Assert.Throws<InvalidOperationException>(() => resumed.Step());
                            Assert.Throws<InvalidOperationException>(() => resumed.Interrupt("tracking"));
                            Assert.Throws<InvalidOperationException>(() => resumed.Apply(rig.Command("pause", O("reason", "user"))));
                        }
                    }
                    Assert.AreEqual(expected.ToJson().Canonical(), work.Result.ToJson().Canonical());
                }
                Assert.IsTrue(stages.Contains("command.admission.ready_to_commit"));
                Assert.AreEqual(reference.ReadSnapshot().Value.Canonical(), resumed.ReadSnapshot().Value.Canonical());
                foreach (string stop in stages)
                {
                    var cancelled = rig.Factory.RestoreForReplay(initial.Value); bool found = false;
                    using (var work = cancelled.AdmitWork(command))
                    {
                        while (work.MoveNext()) if (work.Stage == stop) { found = true; break; }
                    }
                    Assert.IsTrue(found); Assert.IsNull(cancelled.Fault);
                    Assert.AreEqual(initial.Value.Canonical(), cancelled.ReadSnapshot().Value.Canonical(), "Cancelled admission changed authority: " + stop);
                    Assert.AreEqual(expected.ToJson().Canonical(), cancelled.Admit(command).ToJson().Canonical());
                    Assert.AreEqual(reference.ReadSnapshot().Value.Canonical(), cancelled.ReadSnapshot().Value.Canonical());
                }
            }
        }

        private static IEnumerable<JsonValue> UnknownSize(IEnumerable<JsonValue> values)
        { foreach (var value in values) yield return value; }

        private static void AssertImmutableArrayCopies()
        {
            foreach (int count in new[] { 0, 1, 63, 64, 65, 128, 129 })
            foreach (bool unknownSize in new[] { false, true })
            {
                var source = new List<JsonValue>(); var expected = new StringBuilder("[");
                for (int index = 0; index < count; index++)
                {
                    long number = index - count / 2;
                    source.Add(JsonValue.Number(number));
                    if (index != 0) expected.Append(',');
                    expected.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                string canonical = expected.Append(']').ToString(); string digest;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
                IEnumerable<JsonValue> input = unknownSize ? UnknownSize(source) : source;
                if (unknownSize) Assert.IsFalse(input is ICollection<JsonValue>);
                JsonValue published;
                using (var work = JsonValue.ArrayWork(input))
                {
                    Assert.Throws<InvalidOperationException>(() => { var unavailable = work.Result; });
                    while (work.MoveNext())
                        Assert.Throws<InvalidOperationException>(() => { var unavailable = work.Result; });
                    published = work.Result;
                    Assert.AreEqual(count, published.Items.Count);
                    CollectionAssert.AreEqual(source, published.Items);
                }
                // Disposal and subsequent source mutation cannot alter a published value.
                if (source.Count != 0) source[0] = JsonValue.Number(long.MaxValue);
                source.Clear(); source.Add(JsonValue.Null());
                Assert.AreEqual(canonical, published.Canonical());
                Assert.AreEqual(digest, published.Sha256());
                var exposed = (IList<JsonValue>)published.Items;
                Assert.Throws<NotSupportedException>(() => exposed.Add(JsonValue.Null()));
                if (count != 0) Assert.Throws<NotSupportedException>(() => exposed[0] = JsonValue.Null());
                Assert.AreEqual(canonical, published.Canonical());
            }
            foreach (bool unknownSize in new[] { false, true })
            {
                var source = Enumerable.Range(0, 129).Select(value => JsonValue.Number(value)).ToList();
                IEnumerable<JsonValue> input = unknownSize ? UnknownSize(source) : source;
                var cancelled = JsonValue.ArrayWork(input);
                try
                {
                    Assert.IsTrue(cancelled.MoveNext(), "The large copy must expose an unfinished cancellation boundary");
                    Assert.IsFalse(cancelled.IsComplete);
                    Assert.Throws<InvalidOperationException>(() => { var unavailable = cancelled.Result; });
                }
                finally { cancelled.Dispose(); }
                Assert.Throws<InvalidOperationException>(() => { var unavailable = cancelled.Result; });
                Assert.Throws<ObjectDisposedException>(() => cancelled.MoveNext());
            }
        }

        [Test, NUnit.Framework.Property("case_id", "check-c08/assertion/assertion_id=check-c08-a01")]
        public void SourceIsolationRevisionCancellationAndLifecycle()
        {
            AssertImmutableArrayCopies();
            AssertCooperativeClockBoundaries();
            AssertCooperativeCommandAdmission();
            foreach (string stop in new[] { "restore.", "fixture.create.", "water.create.", "transport.create.",
                "equipment.create.", "objective.create.", "restore.clock.event_", "operation.ready_to_commit", "forecast.ready_to_publish" })
            {
                var rig = ForecastFixture.Build(); rig.StartVehicleRoute();
                var before = rig.Clock.ReadSnapshot(); bool reached = false;
                using (var worker = new ForecastService(rig.Factory, () => rig.Clock.StateRevision, () => true, () => rig.Clock.ApplicationState, 1))
                {
                    worker.Begin(Request(rig, rig.Command("place_equipment", Place()), horizon: 1));
                    for (int frame = 0; frame < 2000000; frame++)
                    {
                        worker.AdvanceFrame(150 + frame);
                        if ((worker.CurrentWorkStage ?? "").StartsWith(stop, StringComparison.Ordinal)) { reached = true; break; }
                        Assert.AreEqual("pending", worker.Poll(1).Status, "Forced-yield target was skipped: " + stop);
                    }
                    Assert.IsTrue(reached, stop);
                    if (stop == "forecast.ready_to_publish")
                    {
                        Assert.IsTrue(rig.Clock.Apply(rig.Command("pause", O("reason", "user"))).Receipt.Accepted);
                        worker.AdvanceFrame(3000000);
                        Assert.AreEqual("STALE_REVISION", worker.Poll(1).ReasonCode);
                    }
                    else { worker.Cancel(1); Assert.AreEqual("CANCELLED", worker.Poll(1).ReasonCode); }
                    Assert.AreEqual("cancelled", worker.Poll(1).Status);
                    Assert.IsNull(worker.ReadDiagnostics(1)); Assert.IsNull(worker.ReadExplanation(1));
                }
                if (stop != "forecast.ready_to_publish") Assert.AreEqual(before.Value.Canonical(), rig.Clock.ReadSnapshot().Value.Canonical());
            }
            foreach (string lifecycle in new[] { "planning", "running", "paused" })
            {
                var rig = ForecastFixture.Build();
                if (lifecycle != "planning") { rig.StartVehicleRoute(); for (int n = 0; n < 5; n++) Step(rig.Clock); }
                if (lifecycle == "paused") Assert.IsTrue(rig.Clock.Apply(rig.Command("pause", O("reason", "user"))).Receipt.Accepted);
                var before = rig.Clock.ReadSnapshot(); string application = rig.Clock.ApplicationState;
                var preview = rig.Command("place_equipment", Place()); var request = Request(rig, preview, horizon: 25);
                Assert.AreEqual(request.ToJson().Canonical(), ForecastRequest.Read(request.ToJson()).ToJson().Canonical());
                using (var service = Service(rig))
                {
                    service.Begin(request); service.AdvanceFrame(149); Assert.AreEqual("pending", service.Poll(1).Status);
                    Assert.IsNull(service.ReadDiagnostics(1), "Debounce has not fabricated a result");
                    Assert.AreEqual("complete", Finish(service).Status);
                    Assert.AreEqual("valid", service.ReadExplanation(1).Classification);
                }
                Assert.AreEqual(before.Value.Canonical(), rig.Clock.ReadSnapshot().Value.Canonical());
                Assert.AreEqual(before.StateSha256, rig.Clock.ReadSnapshot().StateSha256);
                Assert.AreEqual(application, rig.Clock.ApplicationState);
                if (lifecycle != "planning") Assert.IsTrue(before.Value["transfers"].Items.Any(x => x["progress_operation_ticks"].Integer > 0), "Real partial boarding survives source isolation");
            }
            var owned = ForecastFixture.Build(); owned.StartVehicleRoute(); bool owner = true;
            using (var worker = new ForecastService(owned.Factory, () => owned.Clock.StateRevision, () => owner, () => owned.Clock.ApplicationState))
            {
                worker.Begin(Request(owned, owned.Command("place_equipment", Place()), horizon: 20)); worker.AdvanceFrame(150);
                Assert.IsTrue(owned.Clock.Apply(owned.Command("pause", O("reason", "user"))).Receipt.Accepted);
                Assert.AreEqual("cancelled", worker.Poll(1).Status); Assert.AreEqual("STALE_REVISION", worker.Poll(1).ReasonCode);
                Assert.IsNull(worker.ReadDiagnostics(1)); Assert.IsNull(worker.ReadExplanation(1));
                worker.Begin(Request(owned, owned.Command("place_equipment", Place()), 2, 20));
                worker.Begin(Request(owned, owned.Command("place_equipment", Place()), 3, 20));
                Assert.IsNull(worker.Poll(2), "A superseded handle cannot retrieve newer data");
                worker.Cancel(2); Assert.AreEqual("pending", worker.Poll(3).Status);
                worker.Cancel(3); Assert.AreEqual("cancelled", worker.Poll(3).Status);
                worker.Begin(Request(owned, owned.Command("place_equipment", Place()), 4, 20)); owner = false;
                Assert.AreEqual("MISSION_EXIT", worker.Poll(4).ReasonCode);
            }
            owned.Clock.Interrupt("tracking");
            using (var worker = Service(owned))
            {
                worker.Begin(Request(owned, owned.Command("place_equipment", Place()), horizon: 1));
                Assert.AreEqual("rejected", worker.Poll(1).Status); Assert.AreEqual("LIFECYCLE_UNAVAILABLE", worker.Poll(1).ReasonCode);
            }
            owned.Clock.RecoverInterruption();
            using (var worker = Service(owned))
            {
                var request = Request(owned, owned.Command("place_equipment", Place()), horizon: 1);
                worker.Begin(new ForecastRequest(1, request.SourceStateRevision, request.Snapshot.With("state_revision", JsonValue.Number(request.SourceStateRevision + 1)), request.PreviewCommand, 1));
                Assert.AreEqual("REVISION_MISMATCH", worker.Poll(1).ReasonCode);
                Assert.Throws<ArgumentOutOfRangeException>(() => new ForecastRequest(2, request.SourceStateRevision, request.Snapshot, request.PreviewCommand, 601));
                Assert.Throws<ArgumentOutOfRangeException>(() => new ForecastRequest(2, request.SourceStateRevision, request.Snapshot, request.PreviewCommand, 0));
            }
        }

        [Test, Order(0), NUnit.Framework.Property("case_id", "check-c08/assertion/assertion_id=check-c08-a02")]
        public void ActualReplayEqualityAndRelaxedScenarioHorizon()
        {
            // Explicit ordering retains the first forecast service calls in this
            // fresh native process, after real fixture/snapshot preparation.
            // The second request is separately measured after actual execution.
            var frameBudgets = new List<WholeCallWitness>();
            MeasureCapacityHeavyFixtures(frameBudgets);
            foreach (bool relaxed in new[] { false, true })
            {
                var rig = ForecastFixture.Build(relaxed, duration: 140); rig.StartVehicleRoute();
                for (int n = 0; n < 5; n++) Step(rig.Clock);
                var initial = rig.Clock.ReadSnapshot().Value; var preview = rig.Command("place_equipment", Place());
                string forecastHash; ForecastDiagnostics measurements; var calls = new WholeCallWitness(); frameBudgets.Add(calls);
                ForecastService worker = null;
                calls.Measure("service_activation", () => worker = Service(rig));
                using (worker)
                {
                    ForecastRequest forecastRequest = null;
                    calls.Measure("request_creation", () => forecastRequest = new ForecastRequest(1, initial["state_revision"].Integer, initial, preview, 300));
                    calls.Measure("begin", () => worker.Begin(forecastRequest));
                    var forecast = Finish(worker, witness: calls);
                    Assert.AreEqual("complete", forecast.Status); Assert.AreEqual("TERMINAL_BOUNDARY", forecast.ReasonCode);
                    Assert.AreEqual("exact_given_no_further_orders", forecast.Knowledge);
                    Assert.AreEqual(140 - initial["scenario_tick"].Integer, forecast.SimulatedScenarioTicks);
                    measurements = worker.ReadDiagnostics(1); forecastHash = measurements.FinalStateSha256;
                    Assert.AreEqual("restore_and_admit", measurements.Slices.First().Kind);
                    Assert.GreaterOrEqual(measurements.Slices.Count(x => x.Kind == "restore_and_admit"), 1);
                    Assert.AreEqual(measurements.SimulatedOperationTicks, measurements.CompletedStepMicroseconds.Count);
                    Assert.IsTrue(measurements.Slices.All(x => x.StepMicroseconds.Count <= 4));
                    Assert.LessOrEqual(measurements.Slices.Count, ForecastService.MaximumRecordedSlices);
                    Assert.LessOrEqual(measurements.SimulatedOperationTicks, ForecastService.MaximumOperationSteps);
                    Assert.IsTrue(forecast.Routes.Any(x => x.RouteId.StartsWith("route:", StringComparison.Ordinal) && x.EstimatedArrivalOperationTick.HasValue));
                    Assert.IsNull(forecast.Routes.Single(x => x.RouteId == "exit").EstimatedArrivalOperationTick, "An unassigned graph route has no fabricated arrival");
                }
                // This separate actual run records real outcomes; ReplayRunner
                // must independently reproduce them through its own restore path.
                var recording = rig.Factory.RestoreForReplay(initial); Assert.AreEqual("queued", recording.Admit(preview).Status);
                var checkpoints = new List<JsonValue>();
                while (recording.Phase != "outcome")
                {
                    Step(recording);
                    if (recording.OperationTick % 100 == 0 || recording.Phase == "outcome") checkpoints.Add(O("operation_tick", recording.OperationTick,
                        "scenario_tick", recording.ScenarioTick, "expected_state_sha256", recording.ReadSnapshot().StateSha256));
                }
                var final = recording.ReadSnapshot(); var outcome = final.Value["outcome"];
                Assert.AreEqual(forecastHash, final.StateSha256);
                var document = O("schema_version", 1, "identity", initial["identity"], "initial_snapshot", initial,
                    "commands", JsonValue.Array(new[] { O("admitted_at_operation_tick", initial["operation_tick"], "command", preview.ToJson(),
                        "expected_result", recording.Receipts.Single(x => x.CommandId == preview.CommandId).Result.ToJson()) }),
                    "checkpoints", JsonValue.Array(checkpoints), "expected", O("success", outcome["success"], "rescued", outcome["rescued"],
                        "stars", outcome["stars"], "score", outcome["score"], "final_state_sha256", final.StateSha256),
                    "provenance", O("generator", "test_fixture", "source_build_id", initial["identity"]["source_build_id"],
                        "simulation_assembly_sha256", ReplayRunner.CurrentSimulationAssemblySha256()));
                var actualReplay = new ReplayRunner().Run(ReplayDocument.Read(document), rig.Factory);
                Assert.IsTrue(actualReplay.Passed, actualReplay.Reason); Assert.AreEqual(forecastHash, actualReplay.FinalSnapshot.StateSha256);
                WriteReplayWitness(relaxed ? "relaxed" : "standard", document, actualReplay, forecastHash, measurements, calls);
                var warmCalls = new WholeCallWitness(); frameBudgets.Add(warmCalls);
                ForecastService warmed = null;
                warmCalls.Measure("service_activation", () => warmed = Service(rig));
                using (warmed)
                {
                    ForecastRequest warmRequest = null;
                    warmCalls.Measure("request_creation", () => warmRequest = new ForecastRequest(1, initial["state_revision"].Integer, initial, preview, 300));
                    warmCalls.Measure("begin", () => warmed.Begin(warmRequest));
                    Assert.AreEqual("complete", Finish(warmed, witness: warmCalls).Status);
                    Assert.AreEqual(forecastHash, warmed.ReadDiagnostics(1).FinalStateSha256);
                }
                WriteTimingWitness((relaxed ? "relaxed" : "standard") + ".warm", warmCalls);
            }
            // Odd Relaxed accumulator must shorten the next 600-scenario horizon
            // to 1199 operation steps, without resetting its real remainder.
            var maximum = ForecastFixture.Build(relaxed: true, duration: 1000); maximum.StartVehicleRoute(); Step(maximum.Clock);
            using (var worker = Service(maximum))
            {
                worker.Begin(Request(maximum, maximum.Command("place_equipment", Place()), horizon: 600));
                var result = Finish(worker); Assert.AreEqual("complete", result.Status); Assert.AreEqual(600, result.SimulatedScenarioTicks);
                Assert.AreEqual(1199, worker.ReadDiagnostics(1).SimulatedOperationTicks);
            }
            // A real guided bus interval consumes O while S is frozen. Preserve
            // the total 1200-O ceiling and reject, never publish a short horizon
            // as complete or silently remove that authoritative guided interval.
            var guided = ForecastFixture.Build(relaxed: true, guided: true, duration: 1000); guided.StartVehicleRoute();
            var guidedRequest = Request(guided, guided.Command("place_equipment", Place()), horizon: 600);
            using (var worker = Service(guided))
            {
                worker.Begin(guidedRequest); var result = Finish(worker);
                Assert.AreEqual("rejected", result.Status); Assert.AreEqual("SCENARIO_HORIZON_LIMIT", result.ReasonCode);
                Assert.IsNull(worker.ReadDiagnostics(1)); Assert.AreEqual(0, result.RiskRegions.Count);
            }
            var bounded = guided.Factory.RestoreForForecast(guidedRequest.Snapshot); bounded.Admit(guidedRequest.PreviewCommand);
            for (int n = 0; n < ForecastService.MaximumOperationSteps; n++) Step(bounded);
            Assert.IsTrue(bounded.ScenarioEnabled); Assert.Greater(bounded.ScenarioTick, 0); Assert.Less(bounded.ScenarioTick, 600);
            Assert.Greater(bounded.ReadSnapshot().Value["milestones"].Items.Single(x => x["id"].Text == "operation_started")["operation_tick"].Integer, 0);
            Assert.AreEqual(0, guided.Clock.OperationTick); Assert.AreEqual(0, guided.Clock.ScenarioTick);
            Assert.IsTrue(frameBudgets.All(x => x.Overruns == 0), "Whole public calls exceeded unchanged 1 ms budget; inspect retained first/warmed timing witnesses");
        }
        private static void MeasureCapacityHeavyFixtures(List<WholeCallWitness> frameBudgets)
        {
            foreach (bool relaxed in new[] { false, true })
            {
                string profile = relaxed ? "relaxed" : "standard";
                WriteCapacityProgress(profile, "build_started", 0);
                var rig = ForecastFixture.BuildCapacityHeavy(relaxed);
                WriteCapacityProgress(profile, "build_completed", 0);
                ForecastFixture.PrepareCapacityHeavy(rig, deployedPumps: 510,
                    setupProgress: (stage, count) => WriteCapacityProgress(profile, stage, count));
                var before = rig.Clock.ReadSnapshot();
                WriteCapacityProgress(profile, "snapshot_completed", 510);
                Assert.LessOrEqual(before.Value.CanonicalByteCountWork().Drain(), 16L * 1024 * 1024);
                Assert.AreEqual(510, before.Value["equipment"].Items.Count);
                var preview = rig.Command("place_equipment", Place("pump:511", "pump"));
                string forecastHash = null;
                for (int repetition = 0; repetition < 2; repetition++)
                {
                    var calls = new WholeCallWitness(); frameBudgets.Add(calls);
                    ForecastService worker = null;
                    try
                    {
                        calls.Measure("service_activation", () => worker = Service(rig));
                        using (worker)
                        {
                            ForecastRequest request = null;
                            calls.Measure("request_creation", () => request = new ForecastRequest(1, before.Value["state_revision"].Integer, before.Value, preview, 1));
                            calls.Measure("begin", () => worker.Begin(request));
                            var result = Finish(worker, witness: calls);
                            Assert.AreEqual("complete", result.Status, result.ReasonCode);
                            string actualHash = worker.ReadDiagnostics(1).FinalStateSha256;
                            if (forecastHash != null) Assert.AreEqual(forecastHash, actualHash);
                            forecastHash = actualHash;
                        }
                    }
                    finally { WriteTimingWitness("capacity." + profile + (repetition == 0 ? ".first" : ".warm"), calls); }
                }
                // Run the independent synchronous path after first-use timing so
                // it does not warm the same restore/executor before measurement.
                var authoritative = rig.Factory.RestoreForForecast(before.Value);
                Assert.AreEqual("queued", authoritative.Admit(preview).Status);
                while (authoritative.ScenarioTick - before.Value["scenario_tick"].Integer < 1) Step(authoritative);
                string expected = authoritative.ReadSnapshot().StateSha256;
                Assert.AreEqual(expected, forecastHash);
                Assert.AreEqual(before.StateSha256, rig.Clock.ReadSnapshot().StateSha256);
                var layout = rig.Content.Envelope.payload.layouts.Single(x => x.id == before.Value["identity"]["layout_version_id"].Text);
                var mission = rig.Content.Envelope.payload.missions.Single();
                var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-resqTestResults"); Assert.GreaterOrEqual(at, 0);
                string directory = Path.Combine(Path.GetDirectoryName(args[at + 1]), "forecast-replay-recordings"); Directory.CreateDirectory(directory);
                WriteBounded(Path.Combine(directory, "capacity." + (relaxed ? "relaxed" : "standard") + ".admitted.json"), O(
                    "evidence_kind", "native_synthetic_capacity_fixture", "global_maximum_claimed", false,
                    "content_sha256", rig.Content.ContentHash, "source_state_sha256", before.StateSha256, "actual_final_state_sha256", expected,
                    "water_nodes", layout.water_nodes.Count, "hydraulic_links", layout.hydraulic_links.Count,
                    "navigation_nodes", layout.nav_nodes.Count, "navigation_edges", layout.nav_edges.Count,
                    "residents", before.Value["residents"].Items.Count, "vehicles", before.Value["vehicles"].Items.Count,
                    "prepared_equipment_instances", before.Value["equipment"].Items.Count, "power_nodes", layout.power_nodes.Count,
                    "objectives", mission.objectives.Count, "scheduled_events", mission.events.Count,
                    "snapshot_bytes", before.Value.CanonicalByteCountWork().Drain(), "first_standard_is_first_forecast_service_use", !relaxed));
            }
        }

        private static void WriteCapacityProgress(string profile, string stage, int completed)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-resqTestResults");
            Assert.GreaterOrEqual(at, 0, "Actual native evidence destination is required");
            string directory = Path.Combine(Path.GetDirectoryName(args[at + 1]), "forecast-replay-recordings");
            Directory.CreateDirectory(directory);
            WriteBounded(Path.Combine(directory, "capacity." + profile + ".setup." + stage + "." + completed + ".json"),
                O("evidence_kind", "native_fixture_preparation_progress_only", "profile", profile, "stage", stage,
                    "completed_count", completed, "forecast_calls_started", false, "acceptance_claimed", false));
        }

        private static void WriteReplayWitness(string profile, JsonValue replay, ReplayReport actual, string forecastHash, ForecastDiagnostics measurements, WholeCallWitness calls)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-resqTestResults");
            Assert.GreaterOrEqual(at, 0, "Actual native evidence destination is required");
            string directory = Path.Combine(Path.GetDirectoryName(args[at + 1]), "forecast-replay-recordings"); Directory.CreateDirectory(directory);
            var witness = O("replay_equality_status", actual.Passed ? "passed" : "failed",
                "whole_call_budget_status", calls.Overruns == 0 ? "passed" : "failed", "replay_sha256", replay.Sha256(),
                "actual_final_state_sha256", actual.FinalSnapshot.StateSha256, "forecast_final_state_sha256", forecastHash,
                "evidence_kind", "native_synthetic_fixture", "host_restore_microseconds", measurements.RestoreMicroseconds,
                "host_maximum_step_microseconds", measurements.MaximumStepMicroseconds, "host_maximum_frame_microseconds", measurements.MaximumSliceMicroseconds,
                "host_step_budget_overruns", measurements.StepBudgetOverruns, "target_frame_microseconds", ForecastService.WorkSliceMicroseconds,
                "simulated_operation_ticks", measurements.SimulatedOperationTicks,
                "whole_public_calls", calls.Json(), "host_work_unit_maximum_microseconds", measurements.MaximumWorkUnitMicroseconds,
                "host_longest_work_unit", measurements.LongestWorkUnit, "host_frame_overruns", measurements.FrameBudgetOverruns,
                "scheduled_frames", measurements.ScheduledFrames, "slice_samples_truncated", measurements.SliceSamplesTruncated,
                "completed_operation_cpu_microseconds", JsonValue.Array(measurements.CompletedStepMicroseconds.Select(JsonValue.Number)),
                "host_slices", JsonValue.Array(measurements.Slices.Select(x => O("kind", x.Kind, "elapsed_microseconds", x.ElapsedMicroseconds,
                    "operation_steps", x.StepMicroseconds.Count, "step_microseconds", JsonValue.Array(x.StepMicroseconds.Select(JsonValue.Number))))));
            WriteBounded(Path.Combine(directory, profile + ".replay.json"), replay);
            WriteBounded(Path.Combine(directory, profile + ".result.json"), witness);
        }
        private static void WriteTimingWitness(string name, WholeCallWitness calls)
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-resqTestResults");
            Assert.GreaterOrEqual(at, 0);
            string directory = Path.Combine(Path.GetDirectoryName(args[at + 1]), "forecast-replay-recordings");
            Directory.CreateDirectory(directory); WriteBounded(Path.Combine(directory, name + ".timing.json"), calls.Json());
        }
        private static void WriteBounded(string path, JsonValue value)
        {
            string json = value.Canonical();
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(json), 256 * 1024, "C08 native witness exceeds its explicit per-file output cap");
            File.WriteAllText(path, json);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c08/assertion/assertion_id=check-c08-a03")]
        public void RealCostStockCapacityPowerAndRiskExplanations()
        {
            ReplacedOrdersUseOnlyCurrentRouteState();
            PowerPausedTransferUsesActualVehicleState();
            ActivationAndRelocationExplainActualInstance();
            foreach (long supply in new long[] { 0, 2 })
            {
                var rig = ForecastFixture.Build(configure: payload => ForecastFixture.WithPump(payload, supply)); rig.StartVehicleRoute();
                var before = rig.Clock.ReadSnapshot().Value;
                using (var worker = Service(rig))
                {
                    worker.Begin(Request(rig, rig.Command("place_equipment", Place("pump:01", "pump")), horizon: 5));
                    Assert.AreEqual("complete", Finish(worker).Status); var explanation = worker.ReadExplanation(1);
                    Assert.AreEqual(3, explanation.SupplyCost); Assert.AreEqual(10, explanation.SuppliesBefore); Assert.AreEqual(7, explanation.SuppliesAfter);
                    Assert.AreEqual(1, explanation.PowerDemand); Assert.AreEqual(supply == 0 ? 0 : 1, explanation.ActualPowerGrant);
                    Assert.AreEqual(0, explanation.StockRemaining); Assert.AreEqual(supply == 0 ? "risky" : "valid", explanation.Classification);
                    Assert.AreEqual(supply == 0, explanation.ReasonCodes.Contains("POWER_UNAVAILABLE"));
                }
                Assert.AreEqual(before.Canonical(), rig.Clock.ReadSnapshot().Value.Canonical());
            }
            var stock = ForecastFixture.Build(); stock.Start();
            var first = stock.Command("place_equipment", Place()); Assert.AreEqual("queued", stock.Clock.Admit(first).Status);
            var second = stock.Command("place_equipment", Place("beacon:02")); var stockBefore = stock.Clock.ReadSnapshot().Value;
            using (var worker = Service(stock))
            {
                worker.Begin(Request(stock, second, horizon: 5)); var result = Finish(worker);
                Assert.AreEqual("rejected", result.Status); Assert.AreEqual("INSUFFICIENT_SUPPLIES", result.PreviewReceipt.ReasonCode);
                var explanation = worker.ReadExplanation(1); Assert.AreEqual("invalid", explanation.Classification);
                Assert.AreEqual(1, explanation.SupplyCost); Assert.AreEqual(0, explanation.SuppliesAfter); Assert.AreEqual(0, explanation.StockRemaining);
            }
            Assert.AreEqual(stockBefore.Canonical(), stock.Clock.ReadSnapshot().Value.Canonical(), "Queued competition must remain isolated");
            var capacity = ForecastFixture.Build(); capacity.Start();
            using (var worker = Service(capacity))
            {
                var route = capacity.Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] {
                    O("node_id", "start", "action", "pickup", "group_id", "group"), O("node_id", "refuge", "action", "evacuate", "group_id", null) })));
                worker.Begin(Request(capacity, route, horizon: 60)); Assert.AreEqual("complete", Finish(worker).Status);
                Assert.AreEqual(4, worker.ReadExplanation(1).ResidentSeats); Assert.AreEqual(2, worker.ReadExplanation(1).MovementSlots);
                Assert.IsTrue(worker.Poll(1).Routes.Any(x => x.RouteId == "route:" + route.Sequence && x.EstimatedArrivalOperationTick.HasValue));
            }
            var hazard = ForecastFixture.Build(configure: FloodedBindings); hazard.Start();
            using (var worker = Service(hazard))
            {
                worker.Begin(Request(hazard, hazard.Command("place_equipment", Place()), horizon: 125)); Assert.AreEqual("complete", Finish(worker).Status);
                var wet = worker.Poll(1).RiskRegions.Single(x => x.RegionId == "wet"); Assert.AreEqual(120, wet.FirstUnsafeScenarioTick); Assert.Greater(wet.MaximumValue, 200);
                Assert.AreEqual("risky", worker.ReadExplanation(1).Classification); Assert.Contains("KNOWN_HAZARD_RISK", worker.ReadExplanation(1).ReasonCodes);
            }
        }
        private static void ReplacedOrdersUseOnlyCurrentRouteState()
        {
            var rig = ForecastFixture.Build(configure: payload => {
                var m = payload["missions"].Items.Single();
                m = m.With("events", JsonValue.Array(m["events"].Items.Select(e => e.With("operations", JsonValue.Array(new[] { O("kind", "set_edge_enabled", "target_role", "blocked_branch", "value", 0) })))));
                var layouts = payload["layouts"].Items.Select(l => l.With("roles", JsonValue.Array(l["roles"].Items.Concat(new[] { O("role_id", "blocked_branch", "target_kind", "nav_edge_set", "target_ids", A("c")) }))));
                return payload.With("missions", JsonValue.Array(new[] { m })).With("layouts", JsonValue.Array(layouts));
            }); rig.Start(); while (rig.Clock.OperationTick < 119) Step(rig.Clock);
            var old = rig.Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] { O("node_id", "junction", "action", "dropoff", "group_id", null) })));
            Assert.AreEqual("queued", rig.Clock.Admit(old).Status);
            while (rig.Clock.OperationTick < 130) Step(rig.Clock);
            Assert.AreEqual("edge", rig.Clock.ReadSnapshot().Value["queues"].Items.Single()["kind"].Text);
            var replacement = rig.Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] { O("node_id", "refuge", "action", "evacuate", "group_id", null) })));
            using (var worker = Service(rig))
            {
                worker.Begin(Request(rig, replacement, horizon: 25)); var result = Finish(worker);
                Assert.AreEqual("complete", result.Status); Assert.IsFalse(result.Routes.Any(x => x.RouteId == "route:" + old.Sequence));
                Assert.IsTrue(result.Routes.Single(x => x.RouteId == "route:" + replacement.Sequence).Available);
                Assert.AreEqual("valid", worker.ReadExplanation(1).Classification);
            }
            Assert.AreEqual("route:" + old.Sequence, rig.Clock.ReadSnapshot().Value["routes"].Items.Single()["id"].Text);
        }
        private static void PowerPausedTransferUsesActualVehicleState()
        {
            var rig = ForecastFixture.Build(configure: ForecastFixture.WithPoweredDock); rig.Start();
            while (rig.Clock.OperationTick < 109) Step(rig.Clock);
            var order = rig.Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] {
                O("node_id", "start", "action", "pickup", "group_id", "group"), O("node_id", "refuge", "action", "evacuate", "group_id", null) })));
            Assert.AreEqual("queued", rig.Clock.Admit(order).Status); while (rig.Clock.OperationTick < 119) Step(rig.Clock);
            string before = rig.Clock.ReadSnapshot().Value.Canonical();
            using (var worker = Service(rig))
            {
                worker.Begin(Request(rig, rig.Command("place_equipment", Place()), horizon: 5)); var result = Finish(worker);
                Assert.AreEqual("complete", result.Status);
                var route = result.Routes.Single(x => x.RouteId == "route:" + order.Sequence);
                Assert.IsFalse(route.Available); Assert.AreEqual("POWER_UNAVAILABLE", route.BlockingReasonCode);
                Assert.IsNull(route.EstimatedArrivalOperationTick); Assert.AreEqual("risky", worker.ReadExplanation(1).Classification);
                Assert.Contains("POWER_UNAVAILABLE", worker.ReadExplanation(1).ReasonCodes);
            }
            Assert.AreEqual(before, rig.Clock.ReadSnapshot().Value.Canonical());
            Step(rig.Clock); var actual = rig.Clock.ReadSnapshot().Value;
            Assert.AreEqual("waiting_power", actual["vehicles"].Items.Single()["status"].Text);
            Assert.AreEqual(9, actual["transfers"].Items.Single()["progress_operation_ticks"].Integer);
            Assert.AreEqual(0, actual["queues"].Items.Count); Assert.AreEqual(0, actual["power"]["grants"].Items.Single()["value"].Integer);
        }
        private static void ActivationAndRelocationExplainActualInstance()
        {
            var rig = ForecastFixture.Build(configure: p => ForecastFixture.WithPump(p, 0)); rig.Start();
            Assert.AreEqual("queued", rig.Clock.Admit(rig.Command("place_equipment", Place("pump:01", "pump"))).Status); Step(rig.Clock);
            string instance = rig.Clock.ReadSnapshot().Value["equipment"].Items.Single()["id"].Text;
            foreach (string action in new[] { "enable", "disable" })
            using (var worker = Service(rig))
            {
                worker.Begin(Request(rig, rig.Command("activate_tool", O("equipment_instance_id", instance, "action", action)), horizon: 2));
                Assert.AreEqual("complete", Finish(worker).Status); var explanation = worker.ReadExplanation(1);
                Assert.AreEqual(0, explanation.SupplyCost); Assert.AreEqual(7, explanation.SuppliesBefore); Assert.AreEqual(7, explanation.SuppliesAfter);
                Assert.AreEqual(1, explanation.PowerDemand); Assert.AreEqual(0, explanation.ActualPowerGrant);
                Assert.AreEqual(action == "enable" ? "risky" : "valid", explanation.Classification);
            }
            Assert.AreEqual("queued", rig.Clock.Admit(rig.Command("activate_tool", O("equipment_instance_id", instance, "action", "disable"))).Status); Step(rig.Clock);
            using (var worker = Service(rig))
            {
                worker.Begin(Request(rig, rig.Command("move_idle_equipment", O("equipment_instance_id", instance, "destination_socket_id", "pump:02", "orientation_step", 0)), horizon: 2));
                Assert.AreEqual("complete", Finish(worker).Status); var explanation = worker.ReadExplanation(1);
                Assert.AreEqual("valid", explanation.Classification); Assert.AreEqual(0, explanation.SupplyCost);
                Assert.AreEqual(1, explanation.PowerDemand); Assert.AreEqual(0, explanation.ActualPowerGrant);
            }
            Assert.AreEqual("pump:01", rig.Clock.ReadSnapshot().Value["equipment"].Items.Single()["socket_id"].Text);
        }
        internal static JsonValue FloodedBindings(JsonValue payload)
        {
            var mission = payload["missions"].Items.Single().With("events", JsonValue.Array(payload["missions"].Items.Single()["events"].Items.Select(e =>
                e.With("operations", JsonValue.Array(new[] { O("kind", "set_source_rate", "target_role", "source", "value", 100000000) })))));
            var layouts = payload["layouts"].Items.Select(layout => layout
                .With("water_nodes", JsonValue.Array(layout["water_nodes"].Items.Select(w => w["id"].Text == "water" ? w.With("area_mm2", JsonValue.Number(10000)) : w)))
                .With("nav_nodes", JsonValue.Array(layout["nav_nodes"].Items.Select(n => n["id"].Text == "start" ? n.With("hazard_region_ids", A("wet")) : n))));
            return payload.With("missions", JsonValue.Array(new[] { mission })).With("layouts", JsonValue.Array(layouts));
        }

        [Test, NUnit.Framework.Property("case_id", "check-c08/assertion/assertion_id=check-c08-a04")]
        public void HiddenEventNoninterferenceAndFrozenKnowledge()
        {
            var first = ForecastFixture.Build(allPublic: false, configure: FloodedBindings); first.Start();
            var changed = ForecastFixture.Build(allPublic: false, configure: payload => {
                payload = FloodedBindings(payload); var mission = payload["missions"].Items.Single(); var hidden = mission["events"].Items.Single();
                var extra = hidden.With("id", JsonValue.String("hidden_extra_identifier")).With("at_scenario_tick", JsonValue.Number(160)).With("warning_at_scenario_tick", JsonValue.Number(60));
                hidden = hidden.With("id", JsonValue.String("hidden_changed_identifier")).With("at_scenario_tick", JsonValue.Number(140)).With("warning_at_scenario_tick", JsonValue.Number(40));
                return payload.With("missions", JsonValue.Array(new[] { mission.With("events", JsonValue.Array(new[] { hidden, extra })) }));
            }); changed.Start();
            string output = null;
            foreach (var rig in new[] { first, changed })
            using (var worker = Service(rig))
            {
                var request = Request(rig, rig.Command("place_equipment", Place()), horizon: 170); worker.Begin(request);
                var result = Finish(worker); Assert.AreEqual("complete", result.Status); Assert.AreEqual("conditional_known_events", result.Knowledge);
                Assert.AreEqual(170, result.SimulatedScenarioTicks, "Unknown operations must not create a visible cutoff");
                Assert.AreEqual(0, result.RiskRegions.Single(x => x.RegionId == "wet").MaximumValue);
                string publicJson = result.ToJson().Canonical(); Assert.IsFalse(publicJson.Contains("hidden_"));
                if (output == null) output = publicJson; else Assert.AreEqual(output, publicJson, "Changing hidden IDs, count, timing and operations must not change display output");
                var masked = rig.Factory.RestoreForForecast(request.Snapshot); masked.Admit(request.PreviewCommand);
                for (int n = 0; n < 170; n++) Step(masked);
                Assert.AreEqual(0, masked.ReadSnapshot().Value["event_state"]["revealed_event_ids"].Items.Count);
                Assert.AreEqual(0, masked.ReadSnapshot().Value["event_state"]["warned_event_ids"].Items.Count);
            }
            // Once the real live warning is published, a NEW request includes its
            // exact future operation. The old request never silently learns it.
            for (int n = 0; n < 20; n++) Step(first.Clock);
            Assert.AreEqual(1, first.Clock.ReadSnapshot().Value["event_state"]["revealed_event_ids"].Items.Count);
            using (var worker = Service(first))
            {
                worker.Begin(Request(first, first.Command("place_equipment", Place()), horizon: 110)); var result = Finish(worker);
                Assert.AreEqual("complete", result.Status); Assert.AreEqual("exact_given_no_further_orders", result.Knowledge);
                Assert.AreEqual(120, result.RiskRegions.Single(x => x.RegionId == "wet").FirstUnsafeScenarioTick);
                Assert.Greater(result.RiskRegions.Single(x => x.RegionId == "wet").MaximumValue, 0);
            }
        }
    }
}
