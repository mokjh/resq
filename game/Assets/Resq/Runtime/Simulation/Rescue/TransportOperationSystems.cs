using System;
using System.Linq;
using System.Collections.Generic;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation.Rescue
{
    // The other-domain owner remains mandatory. Its methods supply actual
    // hazard/power/objective/terminal behavior and must not own transport fields.
    // frameFactory reads that isolated owner after its stage-6 recomputation.
    public interface ITransportDomainPeer
    {
        // The peer uses this exact isolated transport authority for metrics,
        // guidance, initial objectives and terminal evaluation.
        void BindTransport(TransportSystem transport);
    }
    public sealed class TransportOperationSystems : IResumableOperationSystems
    {
        private readonly IOperationSystems remaining;
        private readonly TransportSystem transport;
        private readonly Func<TransportSystem, IOperationSystems, OperationContext, TransportFrame> frameFactory;
        private readonly Func<TransportSystem, IOperationSystems, bool> guidedEpochReady;
        private TransportFrame currentFrame;
        public TransportSystem Transport => transport;
        public TransportOperationSystems(IOperationSystems remaining, TransportSystem transport,
            Func<TransportSystem, IOperationSystems, OperationContext, TransportFrame> frameFactory,
            Func<TransportSystem, IOperationSystems, bool> guidedEpochReady)
        {
            this.remaining = remaining ?? throw new ArgumentNullException(nameof(remaining));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!(remaining is ITransportDomainPeer peer)) throw new InvalidOperationException("Remaining-domain owner must bind the isolated transport authority");
            peer.BindTransport(transport);
            this.frameFactory = frameFactory ?? throw new ArgumentNullException(nameof(frameFactory));
            // Guidance additionally requires actual bus-group depot arrival AND
            // river-warning acknowledgement; the guidance owner supplies the latter.
            this.guidedEpochReady = guidedEpochReady ?? throw new ArgumentNullException(nameof(guidedEpochReady));
        }
        public IOperationSystems Fork() => ForkWork().Drain();
        public ResumableWork<IOperationSystems> ForkWork() => new ResumableWork<IOperationSystems>(done => ForkRoutine(done));
        private IEnumerable<string> ForkRoutine(Action<IOperationSystems> done)
        {
            IOperationSystems fork; using (var workfork = DomainWork.Fork(remaining)) { while (workfork.MoveNext()) yield return workfork.Stage; fork = workfork.Result; } if (fork == null) throw new InvalidOperationException("Missing other-domain fork");
            if (ReferenceEquals(fork, remaining)) throw new InvalidOperationException("Other-domain fork aliases authority");
            TransportSystem copy; using (var workcopy = transport.ForkWork()) { while (workcopy.MoveNext()) yield return workcopy.Stage; copy = workcopy.Result; } done(new TransportOperationSystems(fork, copy, frameFactory, guidedEpochReady));
                }
        public JsonValue ReadDomainState() => ReadDomainStateWork().Drain();
        public ResumableWork<JsonValue> ReadDomainStateWork() => new ResumableWork<JsonValue>(done => ReadDomainStateRoutine(done));
        private IEnumerable<string> ReadDomainStateRoutine(Action<JsonValue> done)
        {
            JsonValue state; using (var workstate = DomainWork.State(remaining)) { while (workstate.MoveNext()) yield return workstate.Stage; state = workstate.Result; }JsonValue owned; using (var workowned = transport.ReadStateWork()) { while (workowned.MoveNext()) yield return workowned.Stage; owned = workowned.Result; }
            foreach (var pair in owned.Members)
            {yield return "transport.adapter.state"; 
                if (!state.Has(pair.Key) || state[pair.Key].Kind != "array" || state[pair.Key].Items.Count != 0) throw new InvalidOperationException("Conflicting/missing transport field owner: " + pair.Key);
                state = state.With(pair.Key, pair.Value);
            }
            if (!state.Has("milestones") || !state.Has("event_state")) throw new InvalidOperationException("Missing remaining-domain state");
            JsonValue milestones; using (var workmilestones = transport.MilestonesWork()) { while (workmilestones.MoveNext()) yield return workmilestones.Stage; milestones = workmilestones.Result; } var all = state["milestones"].Items.Concat(milestones.Items).OrderBy(x => x["id"].Text, StringComparer.Ordinal).ToArray();
            if (all.Select(x => x["id"].Text).Distinct(StringComparer.Ordinal).Count() != all.Length) throw new InvalidOperationException("Duplicate milestone owner");
            JsonValue history; using (var workhistory = transport.HistoricalMetricsWork()) { while (workhistory.MoveNext()) yield return workhistory.Stage; history = workhistory.Result; } var metrics = state["metrics"].Items.Concat(history.Items).OrderBy(x => x["id"].Text, StringComparer.Ordinal).ToArray();
            if (metrics.Select(x => x["id"].Text).Distinct(StringComparer.Ordinal).Count() != metrics.Length) throw new InvalidOperationException("Duplicate transport historical metric owner");
            JsonValue held; using (var workheld = transport.GroupHeldWork()) { while (workheld.MoveNext()) yield return workheld.Stage; held = workheld.Result; } done(state.With("metrics", JsonValue.Array(metrics)).With("milestones", JsonValue.Array(all)).With("event_state", state["event_state"].With("group_held", held)));
                }
        public ValidationResult Validate(GameCommand command, OperationContext context)
        {
            if (command.Kind == "assign_route") return transport.ValidateRoute((AssignRoute)command.Payload);
            return remaining.Validate(command, context);
        }
        public void Apply(GameCommand command, OperationContext context)
        {
            if (command.Kind == "assign_route") transport.Assign((AssignRoute)command.Payload, context.OperationTick, command.Sequence);
            else remaining.Apply(command, context);
        }
        public void EvaluateTides(OperationContext c) => remaining.EvaluateTides(c);
        public void PublishWarning(EventDef definition, OperationContext c) => remaining.PublishWarning(definition, c);
        public void ApplyScheduled(ResolvedEventOperation operation, OperationContext c)
        {
            if (operation.Kind == "release_group") foreach (string id in operation.TargetIds) transport.ReleaseGroup(id);
            else remaining.ApplyScheduled(operation, c);
        }
        public void ResolvePowerAndReservations(OperationContext c) => StageWork(OperationStage.ResolvePowerAndReservations, c).Drain();
        public void SimulateHazardInterval(OperationContext c) => StageWork(OperationStage.SimulateHazardInterval, c).Drain();
        public void RecomputeSafetyAndDamage(OperationContext c) => StageWork(OperationStage.RecomputeSafetyAndDamage, c).Drain();
        public void ResolveMovementAndTransfers(OperationContext c) => StageWork(OperationStage.ResolveMovementAndTransfers, c).Drain();
        public void DeriveMetricsAndObjectives(OperationContext c) => StageWork(OperationStage.DeriveMetricsAndObjectives, c).Drain();
        public ResumableWork<bool> StageWork(OperationStage stage, OperationContext c) => new ResumableWork<bool>(done => StageRoutine(stage, c, done));
        private IEnumerable<string> StageRoutine(OperationStage stage, OperationContext c, Action<bool> done)
        {
            if (stage == OperationStage.ResolveMovementAndTransfers)
            {
                using (var work = transport.ResolveMovementWork(c, currentFrame)) { while (work.MoveNext()) yield return work.Stage; }
            }
            else if (stage == OperationStage.DeriveMetricsAndObjectives)
            {
                currentFrame = frameFactory(transport, remaining, c) ?? throw new InvalidOperationException("Missing post-movement exposure frame");
                yield return "transport.adapter.actual.frame.atomic";
                using (var work = transport.DeriveWork(c, currentFrame)) { while (work.MoveNext()) yield return work.Stage; }
            }
            else if (stage == OperationStage.SampleInitialObjectives)
            { using (var work = transport.SampleInitialMilestonesWork(c)) { while (work.MoveNext()) yield return work.Stage; } }
            else if (stage == OperationStage.ValidateBoundary)
            { using (var work = transport.ValidateBoundaryWork()) { while (work.MoveNext()) yield return work.Stage; } }
            using (var work = DomainWork.Stage(remaining, stage, c)) { while (work.MoveNext()) yield return work.Stage; }
            if (stage == OperationStage.RecomputeSafetyAndDamage)
            {
                currentFrame = frameFactory(transport, remaining, c) ?? throw new InvalidOperationException("Missing actual transport safety frame");
                yield return "transport.adapter.actual.frame.atomic";
            }
            done(true);
        }
        public void EmitMilestone(string id, OperationContext c) => remaining.EmitMilestone(id, c);
        public void SampleInitialObjectives(OperationContext c) => StageWork(OperationStage.SampleInitialObjectives, c).Drain();
        public bool GuidedEpochReady => guidedEpochReady(transport, remaining);
        public bool RequiredConditionsHold => RequiredConditionsWork().Drain();
        public ResumableWork<bool> RequiredConditionsWork() => new ResumableWork<bool>(RequiredConditionsRoutine);
        private IEnumerable<string> RequiredConditionsRoutine(Action<bool> done)
        {
            if (!transport.TerminalTransportClear) { done(false); yield break; }
            using (var work = remaining is IResumableOperationSystems resumable ? resumable.RequiredConditionsWork() : DomainWork.Atomic(() => remaining.RequiredConditionsHold, "legacy.required.atomic"))
            { while (work.MoveNext()) yield return work.Stage; done(work.Result); }
        }
        public ResumableWork<ForecastBoundaryObservations> ObservationsWork() => throw new InvalidOperationException("Actual forecast observation composition required");
        public OutcomeResult EvaluateOutcome(OperationContext c) => EvaluateOutcomeWork(c).Drain();
        public ResumableWork<OutcomeResult> EvaluateOutcomeWork(OperationContext c) => new ResumableWork<OutcomeResult>(done => EvaluateOutcomeRoutine(c, done));
        private IEnumerable<string> EvaluateOutcomeRoutine(OperationContext c, Action<OutcomeResult> done)
        {
            OutcomeResult result; using (var workresult = remaining is IResumableOperationSystems resumable ? resumable.OutcomeWork(c) : DomainWork.Atomic(() => remaining.EvaluateOutcome(c), "legacy.outcome.atomic")) { while (workresult.MoveNext()) yield return workresult.Stage; result = workresult.Result; } if (result == null) throw new InvalidOperationException("Missing actual terminal evaluator");
            if (result.Rescued != transport.EvacuatedCount || result.Success && !transport.TerminalTransportClear) throw new InvalidOperationException("Terminal evaluator disagrees with transport authority");
            done(result);
                }
        public void ValidateBoundary(OperationContext c) => StageWork(OperationStage.ValidateBoundary, c).Drain();
        public ResumableWork<OutcomeResult> OutcomeWork(OperationContext c) => EvaluateOutcomeWork(c);
    }
}
