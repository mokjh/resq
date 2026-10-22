using System;
using System.Collections.Generic;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Simulation.Rescue;

namespace Resq.Simulation.Objectives
{
    // Outer C02 composition adapter. Facts always come from the actual domain
    // owner; work is allowed to suspend only while its candidate is private.
    public sealed class ObjectiveOperationSystems : IResumableOperationSystems, IResumableCommandSystems, ITransportDomainPeer
    {
        private readonly IOperationSystems domain;
        private readonly Func<IOperationSystems, OperationContext, JsonValue, ObjectiveObservations> observer;
        private readonly bool customObserver;
        private ObjectiveSystem objectives;
        private readonly SortedDictionary<string, JsonValue> engineMilestones = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
        private OperationContext boundary;
        public ObjectiveSystem Objectives => objectives;
        public ObjectiveOperationSystems(IOperationSystems domain, ObjectiveSystem objectives, Func<IOperationSystems, OperationContext, JsonValue, ObjectiveObservations> observer = null)
        {
            this.domain = domain ?? throw new ArgumentNullException(nameof(domain)); this.objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
            customObserver = observer != null;
            this.observer = observer ?? (domain is IObjectiveDomainObserver ? (Func<IOperationSystems, OperationContext, JsonValue, ObjectiveObservations>)((owner, c, state) => ((IObjectiveDomainObserver)owner).ObserveObjectives(c, state)) : throw new InvalidOperationException("Actual objective observation owner required"));
        }
        public void BindTransport(TransportSystem transport)
        {
            if (!(domain is ITransportDomainPeer peer)) throw new InvalidOperationException("Actual transport binding owner required"); peer.BindTransport(transport);
        }
        public IOperationSystems Fork() => ForkWork().Drain();
        public ResumableWork<IOperationSystems> ForkWork() => new ResumableWork<IOperationSystems>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<IOperationSystems> done)
        {
            IOperationSystems next; using (var work = DomainWork.Fork(domain)) { while (work.MoveNext()) yield return work.Stage; next = work.Result; }
            if (next == null || ReferenceEquals(next, domain)) throw new InvalidOperationException("Objective domain fork aliases/misses authority");
            ObjectiveSystem objectiveCopy; using (var work = objectives.ForkWork()) { while (work.MoveNext()) yield return work.Stage; objectiveCopy = work.Result; }
            var copy = new ObjectiveOperationSystems(next, objectiveCopy, customObserver ? observer : null) { boundary = boundary };
            foreach (var row in engineMilestones) { copy.engineMilestones.Add(row.Key, row.Value); yield return "objective.adapter.fork.milestone"; }
            done(copy);
        }
        public void FreezeForecastKnowledge(IReadOnlyCollection<string> revealedEventIds)
        {
            if (!(domain is IForecastDomainObserver riskOwner)) throw new InvalidOperationException("Actual forecast knowledge owner required");
            riskOwner.FreezeForecastKnowledge(revealedEventIds);
        }
        private ResumableWork<ObjectiveObservations> FactsWork(OperationContext c, JsonValue state)
            => !customObserver && domain is IResumableObjectiveDomainObserver actual ? actual.ObserveObjectivesWork(c, state)
                : DomainWork.Atomic(() => observer(domain, c, state) ?? throw new InvalidOperationException("Missing actual objective observations"), "legacy.objective.facts.atomic");
        private ResumableWork<IEnumerable<JsonValue>> MilestonesWork(JsonValue state) => new ResumableWork<IEnumerable<JsonValue>>(done => MilestonesRoutine(state, done));
        private IEnumerable<string> MilestonesRoutine(JsonValue state, Action<IEnumerable<JsonValue>> done)
        {
            var rows = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var row in state["milestones"].Items.Concat(engineMilestones.Values))
            {
                if (rows.ContainsKey(row["id"].Text)) throw new InvalidOperationException("Duplicate objective milestone owner");
                rows.Add(row["id"].Text, row); yield return "objective.adapter.milestone";
            }
            done(rows.Values);
        }
        public ForecastBoundaryObservations ReadForecastBoundaryObservations() => ObservationsWork().Drain();
        public ResumableWork<ForecastBoundaryObservations> ObservationsWork() => new ResumableWork<ForecastBoundaryObservations>(ObservationsRoutine);
        private IEnumerable<string> ObservationsRoutine(Action<ForecastBoundaryObservations> done)
        {
            if (!(domain is IForecastDomainObserver riskOwner)) throw new InvalidOperationException("Actual forecast risk/safety owner required");
            JsonValue state; using (var work = DomainWork.State(domain)) { while (work.MoveNext()) yield return work.Stage; state = work.Result; }
            ObjectiveObservations facts; using (var work = FactsWork(boundary, state)) { while (work.MoveNext()) yield return work.Stage; facts = work.Result; }
            IEnumerable<ForecastRiskSample> risks;
            if (domain is IResumableObjectiveDomainObserver actual)
            { using (var work = actual.ObserveForecastRiskWork(boundary, state)) { while (work.MoveNext()) yield return work.Stage; risks = work.Result; } }
            else
            {
                var rows = new List<ForecastRiskSample>();
                foreach (var risk in riskOwner.ObserveForecastRisk(boundary, state)) { rows.Add(risk); yield return "legacy.forecast.risk.sample"; }
                risks = rows;
            }
            done(new ForecastBoundaryObservations(boundary.OperationTick, facts, risks));
        }
        public JsonValue ReadDomainState() => ReadDomainStateWork().Drain();
        public ResumableWork<JsonValue> ReadDomainStateWork() => new ResumableWork<JsonValue>(ReadDomainStateRoutine);
        private IEnumerable<string> ReadDomainStateRoutine(Action<JsonValue> done)
        {
            JsonValue state; using (var work = DomainWork.State(domain)) { while (work.MoveNext()) yield return work.Stage; state = work.Result; }
            if (state["objective_monitors"].Items.Count != 0) throw new InvalidOperationException("Conflicting objective monitor owner");
            JsonValue ownedMetrics; using (var work = objectives.MetricsWork()) { while (work.MoveNext()) yield return work.Stage; ownedMetrics = work.Result; }
            var metrics = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (var row in state["metrics"].Items.Concat(ownedMetrics.Items))
            {
                if (metrics.ContainsKey(row["id"].Text)) throw new InvalidOperationException("Conflicting objective metric owner");
                metrics.Add(row["id"].Text, row); yield return "objective.adapter.metric";
            }
            JsonValue metricRows; using (var work = JsonValue.ArrayWork(metrics.Values)) { while (work.MoveNext()) yield return work.Stage; metricRows = work.Result; }
            JsonValue monitorRows; using (var work = objectives.MonitorsWork()) { while (work.MoveNext()) yield return work.Stage; monitorRows = work.Result; }
            // Engine milestones remain solely in OperationClock's aggregate.
            done(state.With("metrics", metricRows).With("objective_monitors", monitorRows));
        }
        public ValidationResult Validate(GameCommand command, OperationContext c) => ValidateWork(command, c).Drain();
        public ResumableWork<ValidationResult> ValidateWork(GameCommand command, OperationContext c)
            => command.Kind == "end_operation" ? DomainWork.Atomic(() => new ValidationResult("OK"), "objective.command.end_validation") : DomainWork.Validate(domain, command, c);
        public void Apply(GameCommand command, OperationContext c) => ApplyWork(command, c).Drain();
        public ResumableWork<bool> ApplyWork(GameCommand command, OperationContext c) => DomainWork.Apply(domain, command, c);
        public void EvaluateTides(OperationContext c) => domain.EvaluateTides(c);
        public void PublishWarning(EventDef d, OperationContext c) => domain.PublishWarning(d, c);
        public void ApplyScheduled(ResolvedEventOperation op, OperationContext c) => domain.ApplyScheduled(op, c);
        public void ResolvePowerAndReservations(OperationContext c) => StageWork(OperationStage.ResolvePowerAndReservations, c).Drain();
        public void SimulateHazardInterval(OperationContext c) => StageWork(OperationStage.SimulateHazardInterval, c).Drain();
        public void RecomputeSafetyAndDamage(OperationContext c) => StageWork(OperationStage.RecomputeSafetyAndDamage, c).Drain();
        public void ResolveMovementAndTransfers(OperationContext c) => StageWork(OperationStage.ResolveMovementAndTransfers, c).Drain();
        public void DeriveMetricsAndObjectives(OperationContext c) => StageWork(OperationStage.DeriveMetricsAndObjectives, c).Drain();
        public void SampleInitialObjectives(OperationContext c) => StageWork(OperationStage.SampleInitialObjectives, c).Drain();
        public void ValidateBoundary(OperationContext c) => StageWork(OperationStage.ValidateBoundary, c).Drain();
        public ResumableWork<bool> StageWork(OperationStage stage, OperationContext c) => new ResumableWork<bool>(done => StageRoutine(stage, c, done));
        private IEnumerable<string> StageRoutine(OperationStage stage, OperationContext c, Action<bool> done)
        {
            using (var work = DomainWork.Stage(domain, stage, c)) { while (work.MoveNext()) yield return work.Stage; }
            if (stage == OperationStage.DeriveMetricsAndObjectives || stage == OperationStage.SampleInitialObjectives)
            {
                using (var work = SampleWork(c, false, false)) { while (work.MoveNext()) yield return work.Stage; }
                boundary = c;
            }
            else if (stage == OperationStage.ValidateBoundary)
            { using (var work = ReadDomainStateWork()) { while (work.MoveNext()) yield return work.Stage; } }
            done(true);
        }
        private ResumableWork<bool> SampleWork(OperationContext c, bool terminal, bool resample) => new ResumableWork<bool>(done => SampleRoutine(c, terminal, resample, done));
        private IEnumerable<string> SampleRoutine(OperationContext c, bool terminal, bool resample, Action<bool> done)
        {
            JsonValue state; using (var work = DomainWork.State(domain)) { while (work.MoveNext()) yield return work.Stage; state = work.Result; }
            ObjectiveObservations facts; using (var work = FactsWork(c, state)) { while (work.MoveNext()) yield return work.Stage; facts = work.Result; }
            IEnumerable<JsonValue> milestones; using (var work = MilestonesWork(state)) { while (work.MoveNext()) yield return work.Stage; milestones = work.Result; }
            using (var work = objectives.SampleWork(state, facts, milestones, c, terminal, resample)) { while (work.MoveNext()) yield return work.Stage; }
            done(true);
        }
        public void EmitMilestone(string id, OperationContext c)
        {
            if (id != "operation_started" && id != "operation_ended") throw new InvalidOperationException("Engine milestone owner only");
            if (engineMilestones.ContainsKey(id)) throw new InvalidOperationException("Duplicate engine milestone notification");
            engineMilestones.Add(id, ObjectiveSystem.O("id", id, "operation_tick", c.OperationTick, "scenario_tick", c.ScenarioTick));
            if (id == "operation_started" && c.OperationTick > 0) SampleWork(c, false, true).Drain();
        }
        public bool GuidedEpochReady => domain.GuidedEpochReady;
        public bool RequiredConditionsHold => RequiredConditionsWork().Drain();
        public ResumableWork<bool> RequiredConditionsWork() => new ResumableWork<bool>(RequiredConditionsRoutine);
        private IEnumerable<string> RequiredConditionsRoutine(Action<bool> done)
        {
            JsonValue state; using (var work = DomainWork.State(domain)) { while (work.MoveNext()) yield return work.Stage; state = work.Result; }
            ObjectiveObservations facts; using (var work = FactsWork(boundary, state)) { while (work.MoveNext()) yield return work.Stage; facts = work.Result; }
            using (var work = objectives.CanEndSuccessfullyWork(state, facts, boundary.ScenarioTick)) { while (work.MoveNext()) yield return work.Stage; done(work.Result); }
        }
        public OutcomeResult EvaluateOutcome(OperationContext c) => OutcomeWork(c).Drain();
        public ResumableWork<OutcomeResult> OutcomeWork(OperationContext c) => new ResumableWork<OutcomeResult>(done => OutcomeRoutine(c, done));
        private IEnumerable<string> OutcomeRoutine(OperationContext c, Action<OutcomeResult> done)
        {
            using (var work = SampleWork(c, true, false)) { while (work.MoveNext()) yield return work.Stage; }
            JsonValue state; using (var work = DomainWork.State(domain)) { while (work.MoveNext()) yield return work.Stage; state = work.Result; }
            using (var work = objectives.OutcomeWork(state)) { while (work.MoveNext()) yield return work.Stage; done(work.Result); }
        }
        // C07 reconstructs these exact clock notifications from Snapshot.milestones.
        public void RestoreEngineMilestones(IEnumerable<JsonValue> milestones, long operationTick, long scenarioTick) => RestoreEngineMilestonesWork(milestones, operationTick, scenarioTick).Drain();
        public ResumableWork<bool> RestoreEngineMilestonesWork(IEnumerable<JsonValue> milestones, long operationTick, long scenarioTick) => new ResumableWork<bool>(done => RestoreEngineMilestonesRoutine(milestones, operationTick, scenarioTick, done));
        private IEnumerable<string> RestoreEngineMilestonesRoutine(IEnumerable<JsonValue> milestones, long operationTick, long scenarioTick, Action<bool> done)
        {
            if (engineMilestones.Count != 0) throw new InvalidOperationException("Restore requires fresh objective adapter");
            foreach (var row in milestones.Where(x => x["id"].Text == "operation_started" || x["id"].Text == "operation_ended"))
            {
                if (row["operation_tick"].Integer > operationTick || row["scenario_tick"].Integer > scenarioTick) throw new InvalidOperationException("Future restored engine milestone");
                engineMilestones.Add(row["id"].Text, row); yield return "objective.adapter.restore.milestone";
            }
            boundary = new OperationContext(operationTick, scenarioTick, false); done(true);
        }
    }
}
