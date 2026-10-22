using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Persistence;

namespace Resq.Simulation.Forecast
{
    // One mission owner, one private continuation; no threads or second simulator.
    public sealed class ForecastService : IForecastService, IDisposable
    {
        public const long DebounceMilliseconds = 150;
        public const long WorkSliceMicroseconds = 1000;
        public const int MaximumOperationSteps = 1200;
        public const int MaximumRecordedSlices = 512;
        private const long SchedulingTargetMicroseconds = 200;
        public static bool AllocationCounterAvailable { get; private set; }
        private static bool allocationCounterProbed;
        // The positive control is ordinary forecast work. Its first execution
        // remains inside a measured AdvanceFrame call, never a hidden warm-up.
        private static bool ProbeAllocationCounter()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[8192]; probe[8191] = 1;
            long after = GC.GetAllocatedBytesForCurrentThread(); GC.KeepAlive(probe);
            return after - before >= probe.Length;
        }
        private readonly SimulationFactory factory;
        private readonly Func<long> authoritativeRevision;
        private readonly Func<bool> ownsMission;
        private readonly Func<string> sourceApplicationState;
        private readonly int maximumWorkUnitsPerFrame;
        private ForecastRequest request;
        private ForecastResult result, preparedResult;
        private OperationClock clone;
        private ForecastProjection projection;
        private PreviewExplanation explanation;
        private ForecastDiagnostics diagnostics;
        private ResumableWork<bool> work;
        private List<ForecastSliceMeasurement> slices = new List<ForecastSliceMeasurement>();
        // Only the first bounded diagnostics copy this scratch list. It is never
        // retained by a result and does not allocate on every active frame.
        private readonly List<long> frameStepTimes = new List<long>(4);
        private List<long> completedStepTimings = new List<long>();
        private long latestRequestId, frameMilliseconds, readyAt, steps, maximumSlice, restoreMicroseconds, maximumStepMicroseconds, stepOverruns;
        private long scheduledFrames, frameOverruns, maximumUnit, allocatedBytes, stepCpu;
        private string knowledge, longestUnit, workKind = "restore_and_admit";
        private bool disposed, measuringStep, completedStepPending;
        public string CurrentWorkStage => work?.Stage;

        public ForecastService(SimulationFactory factory, Func<long> authoritativeRevision, Func<bool> ownsMission,
            Func<string> sourceApplicationState, int maximumWorkUnitsPerFrame = 64)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.authoritativeRevision = authoritativeRevision ?? throw new ArgumentNullException(nameof(authoritativeRevision));
            this.ownsMission = ownsMission ?? throw new ArgumentNullException(nameof(ownsMission));
            this.sourceApplicationState = sourceApplicationState ?? throw new ArgumentNullException(nameof(sourceApplicationState));
            if (maximumWorkUnitsPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(maximumWorkUnitsPerFrame));
            this.maximumWorkUnitsPerFrame = maximumWorkUnitsPerFrame;
        }
        public ForecastHandle Begin(ForecastRequest next)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ForecastService));
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (next.RequestId <= latestRequestId) throw new ArgumentException("Request IDs must increase within the mission owner");
            ReleaseClone(); request = next; latestRequestId = next.RequestId;
            explanation = null; diagnostics = null; preparedResult = null;
            // Prior completed diagnostics remain immutable if their caller retained them.
            slices = new List<ForecastSliceMeasurement>(); completedStepTimings = new List<long>(); steps = 0; maximumSlice = 0; restoreMicroseconds = 0;
            maximumStepMicroseconds = 0; stepOverruns = 0; scheduledFrames = 0; frameOverruns = 0;
            maximumUnit = 0; allocatedBytes = 0; stepCpu = 0; measuringStep = false; completedStepPending = false;
            longestUnit = null; workKind = "restore_and_admit"; knowledge = "conditional_known_events";
            readyAt = checked(frameMilliseconds + DebounceMilliseconds);
            result = new ForecastResult(request, "pending", "DEBOUNCING", knowledge);
            if (!ownsMission()) Finish("cancelled", "MISSION_EXIT");
            else if (authoritativeRevision() != request.SourceStateRevision) Finish("cancelled", "STALE_REVISION");
            else if (request.Snapshot["state_revision"].Integer != request.SourceStateRevision) Finish("rejected", "REVISION_MISMATCH");
            else if (!EligibleLifecycle()) Finish("rejected", "LIFECYCLE_UNAVAILABLE");
            return new ForecastHandle(request);
        }
        public void Cancel(long requestId)
        { if (request != null && request.RequestId == requestId) Finish("cancelled", "CANCELLED"); }
        public ForecastResult Poll(long requestId)
        {
            if (requestId <= 0) throw new ArgumentOutOfRangeException(nameof(requestId));
            if (request == null || request.RequestId != requestId) return null;
            GuardOwnership(); return result;
        }
        public PreviewExplanation ReadExplanation(long requestId)
        { var value = Poll(requestId); return value != null && (value.Status == "complete" || value.Status == "rejected") ? explanation : null; }
        public ForecastDiagnostics ReadDiagnostics(long requestId)
        { return Poll(requestId)?.Status == "complete" ? diagnostics : null; }

        // Whole-call witnesses are taken by the driver around this public method,
        // including these guards and diagnostic publication. Internal timings are
        // also retained; they are not substituted for the enclosing measurement.
        public void AdvanceFrame(long monotonicMilliseconds)
        {
            long started = Stopwatch.GetTimestamp();
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            bool accounted = false; string frameKind = workKind;
            try
            {
                if (monotonicMilliseconds < frameMilliseconds) throw new ArgumentOutOfRangeException(nameof(monotonicMilliseconds));
                frameMilliseconds = monotonicMilliseconds;
                if (disposed || request == null) return;
                GuardOwnership();
                if (result.Status != "pending" || frameMilliseconds < readyAt) return;
                accounted = true; scheduledFrames++;
                frameStepTimes.Clear();
                if (work == null) work = new ResumableWork<bool>(RunRequest);
                int units = 0;
                while (result.Status == "pending" && units < maximumWorkUnitsPerFrame && frameStepTimes.Count < 4 &&
                    (units == 0 || MicrosecondsSince(started) < SchedulingTargetMicroseconds))
                {
                    GuardOwnership(); if (result.Status != "pending") break;
                    bool wasStep = measuringStep; string previousKind = workKind;
                    long unitStarted = Stopwatch.GetTimestamp();
                    bool more = work.MoveNext();
                    long elapsed = MicrosecondsSince(unitStarted); units++;
                    if (elapsed > maximumUnit) { maximumUnit = elapsed; longestUnit = work.Stage ?? "pipeline.initialize"; }
                    if (previousKind == "restore_and_admit") restoreMicroseconds += elapsed;
                    if (wasStep || measuringStep) stepCpu += elapsed;
                    if (completedStepPending)
                    {
                        frameStepTimes.Add(stepCpu); completedStepTimings.Add(stepCpu); maximumStepMicroseconds = Math.Max(maximumStepMicroseconds, stepCpu);
                        // Aggregate CPU for a full operation, not a single-frame duration.
                        if (stepCpu > WorkSliceMicroseconds) stepOverruns++;
                        stepCpu = 0; completedStepPending = false;
                    }
                    if (!more)
                    {
                        GuardOwnership();
                        if (result.Status == "pending")
                        {
                            if (preparedResult == null) throw new InvalidOperationException("Forecast completed without private publication");
                            result = preparedResult;
                        }
                        ReleaseClone(); break;
                    }
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is OverflowException || error is ContentException)
            {
                Finish("rejected", "FORECAST_OWNER_OR_STATE_INVALID");
            }
            finally
            {
                if (accounted)
                {
                    ForecastSliceMeasurement sample = null;
                    if (slices.Count < MaximumRecordedSlices)
                    {
                        sample = new ForecastSliceMeasurement(frameKind, 0, frameStepTimes);
                        slices.Add(sample);
                    }
                    allocatedBytes += Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocationStart);
                    long elapsed = MicrosecondsSince(started);
                    maximumSlice = Math.Max(maximumSlice, elapsed);
                    if (elapsed > WorkSliceMicroseconds) frameOverruns++;
                    sample?.CompleteTiming(elapsed);
                    diagnostics?.CompleteFrameMetrics(maximumSlice, scheduledFrames, frameOverruns, maximumUnit, longestUnit,
                        AllocationCounterAvailable ? (long?)allocatedBytes : null, scheduledFrames > slices.Count);
                }
            }
        }
        private IEnumerable<string> RunRequest(Action<bool> complete)
        {
            if (!allocationCounterProbed)
            {
                AllocationCounterAvailable = ProbeAllocationCounter(); allocationCounterProbed = true;
                yield return "forecast.allocation_counter_probe";
            }
            var content = factory.BoundContent(request.Snapshot["identity"]);
            var mission = content.Envelope.payload.missions.Single(x => x.mission_version_id == request.Snapshot["identity"]["mission_version_id"].Text);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in request.Snapshot["event_state"]["revealed_event_ids"].Items)
            { known.Add(row.Text); yield return "forecast.knowledge_id"; }
            bool hiddenFuture = false;
            foreach (var definition in mission.events)
            {
                hiddenFuture |= definition.at_scenario_tick > request.Snapshot["scenario_tick"].Integer && !known.Contains(definition.id);
                yield return "forecast.knowledge_event";
            }
            knowledge = hiddenFuture ? "conditional_known_events" : "exact_given_no_further_orders";
            if (request.PreviewCommand.IsApplication)
            { preparedResult = new ForecastResult(request, "rejected", "APPLICATION_COMMAND_NOT_PREVIEWABLE", knowledge); complete(true); yield break; }
            using (var restore = factory.RestoreForForecastWork(request.Snapshot))
            { while (restore.MoveNext()) yield return restore.Stage; clone = restore.Result; }
            using (var create = ForecastProjection.CreateWork(content, request.Snapshot))
            { while (create.MoveNext()) yield return create.Stage; projection = create.Result; }
            SimulationSnapshot state;
            using (var snapshot = clone.ReadSnapshotWork()) { while (snapshot.MoveNext()) yield return snapshot.Stage; state = snapshot.Result; }
            ForecastBoundaryObservations observations;
            using (var facts = clone.ReadForecastBoundaryObservationsWork()) { while (facts.MoveNext()) yield return facts.Stage; observations = facts.Result; }
            using (var observe = projection.ObserveWork(state.Value, observations)) { while (observe.MoveNext()) yield return observe.Stage; }
            CommandResult receipt;
            using (var admission = clone.AdmitWork(request.PreviewCommand))
            { while (admission.MoveNext()) yield return admission.Stage; receipt = admission.Result; }
            yield return "forecast.admit_preview";
            if (receipt.Status != "queued")
            {
                using (var explain = projection.ExplainWork(request.PreviewCommand, receipt, state.Value))
                { while (explain.MoveNext()) yield return explain.Stage; explanation = explain.Result; }
                preparedResult = new ForecastResult(request, "rejected", receipt.ReasonCode, knowledge, receipt: receipt);
                complete(true); yield break;
            }
            result = new ForecastResult(request, "pending", "SIMULATING", knowledge, receipt: receipt);
            workKind = "operation_work"; yield return "forecast.restored_and_admitted";
            while (steps < MaximumOperationSteps)
            {
                StepResult step;
                measuringStep = true;
                using (var operation = clone.StepWork()) { while (operation.MoveNext()) yield return operation.Stage; step = operation.Result; }
                measuringStep = false; completedStepPending = true;
                if (step == null)
                {
                    preparedResult = new ForecastResult(request, "rejected", clone.Fault == null ? "FORECAST_CANNOT_ADVANCE" : "SIMULATION_INVARIANT_FAULT", knowledge);
                    complete(true); yield break;
                }
                steps++;
                state = clone.ConsumeCompletedStepSnapshot(step);
                yield return "forecast.completed_operation";
                using (var facts = clone.ReadForecastBoundaryObservationsWork()) { while (facts.MoveNext()) yield return facts.Stage; observations = facts.Result; }
                using (var observe = projection.ObserveWork(state.Value, observations)) { while (observe.MoveNext()) yield return observe.Stage; }
                long simulated = checked(clone.ScenarioTick - request.Snapshot["scenario_tick"].Integer);
                var preview = clone.ReadReceipt(request.PreviewCommand.CommandId).Result;
                if (preview.Status == "rejected")
                {
                    using (var explain = projection.ExplainWork(request.PreviewCommand, preview, state.Value))
                    { while (explain.MoveNext()) yield return explain.Stage; explanation = explain.Result; }
                    preparedResult = new ForecastResult(request, "rejected", preview.ReasonCode, knowledge, receipt: preview);
                    complete(true); yield break;
                }
                if (simulated >= request.HorizonScenarioTicks || step.Terminal)
                {
                    using (var explain = projection.ExplainWork(request.PreviewCommand, preview, state.Value))
                    { while (explain.MoveNext()) yield return explain.Stage; explanation = explain.Result; }
                    using (var publication = projection.ResultWork(request, step.Terminal ? "TERMINAL_BOUNDARY" : "HORIZON_REACHED", knowledge, simulated, preview))
                    { while (publication.MoveNext()) yield return publication.Stage; preparedResult = publication.Result; }
                    diagnostics = new ForecastDiagnostics(state.StateSha256, steps, maximumSlice, restoreMicroseconds,
                        maximumStepMicroseconds, stepOverruns, slices.AsReadOnly());
                    diagnostics.SetCompletedStepTimings(completedStepTimings.AsReadOnly());
                    yield return "forecast.ready_to_publish";
                    complete(true); yield break;
                }
            }
            preparedResult = new ForecastResult(request, "rejected", "SCENARIO_HORIZON_LIMIT", knowledge);
            complete(true);
        }
        private static long MicrosecondsSince(long started) => checked((Stopwatch.GetTimestamp() - started) * 1000000L / Stopwatch.Frequency);
        private bool EligibleLifecycle() { string state = sourceApplicationState(); return state == "paused" || state == "running" || state == "countdown"; }
        private void GuardOwnership()
        {
            if (request == null || result == null || result.Status == "cancelled") return;
            if (disposed || !ownsMission()) Finish("cancelled", "MISSION_EXIT");
            else if (authoritativeRevision() != request.SourceStateRevision) Finish("cancelled", "STALE_REVISION");
            else if (!EligibleLifecycle() && result.Status != "rejected") Finish("cancelled", "LIFECYCLE_UNAVAILABLE");
        }
        private void Finish(string status, string reason, CommandResult receipt = null)
        {
            if (request != null) result = new ForecastResult(request, status, reason, knowledge, receipt: receipt);
            if (status == "cancelled") { explanation = null; diagnostics = null; slices = new List<ForecastSliceMeasurement>(); }
            preparedResult = null; ReleaseClone();
        }
        private void ReleaseClone()
        {
            var abandoned = work; work = null; abandoned?.Dispose();
            clone = null; projection = null; measuringStep = false;
        }
        public void Dispose() { if (disposed) return; disposed = true; Finish("cancelled", "MISSION_EXIT"); }
    }
}
