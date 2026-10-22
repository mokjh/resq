using System;
using System.Collections.Generic;
using Resq.Content;
using Resq.Core;
using Resq.Simulation.Objectives;

namespace Resq.Simulation
{
    public enum OperationStage
    {
        ResolvePowerAndReservations, SimulateHazardInterval, RecomputeSafetyAndDamage,
        ResolveMovementAndTransfers, DeriveMetricsAndObjectives, ValidateBoundary,
        SampleInitialObjectives
    }

    // Work mutates only an isolated operation/restore candidate. The containing
    // clock publishes it after every stage, invariant and outcome has completed.
    public interface IResumableOperationSystems : IOperationSystems
    {
        ResumableWork<IOperationSystems> ForkWork();
        ResumableWork<JsonValue> ReadDomainStateWork();
        ResumableWork<bool> StageWork(OperationStage stage, OperationContext context);
        ResumableWork<bool> RequiredConditionsWork();
        ResumableWork<OutcomeResult> OutcomeWork(OperationContext context);
        ResumableWork<ForecastBoundaryObservations> ObservationsWork();
    }

    // Validation is read-only. Apply mutates only the clock's private operation
    // candidate; no partially applied owner is published when work is cancelled.
    public interface IResumableCommandSystems : IOperationSystems
    {
        ResumableWork<ValidationResult> ValidateWork(GameCommand command, OperationContext context);
        ResumableWork<bool> ApplyWork(GameCommand command, OperationContext context);
    }

    public interface IResumableObjectiveDomainObserver : IObjectiveDomainObserver, IForecastDomainObserver
    {
        ResumableWork<ObjectiveObservations> ObserveObjectivesWork(OperationContext context, JsonValue domainState);
        ResumableWork<IEnumerable<ForecastRiskSample>> ObserveForecastRiskWork(OperationContext context, JsonValue domainState);
    }

    internal static class DomainWork
    {
        internal static ResumableWork<SortedDictionary<string, T>> SortedIndex<T>(IEnumerable<T> values, Func<T, string> key, string stage, Action<string> validate)
            => new ResumableWork<SortedDictionary<string, T>>(done => SortedIndexRoutine(values, key, stage, validate, done));
        private static IEnumerable<string> SortedIndexRoutine<T>(IEnumerable<T> values, Func<T, string> key, string stage, Action<string> validate, Action<SortedDictionary<string, T>> done)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var result = new SortedDictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if ((object)value == null) throw new InvalidOperationException("Null domain sample");
                string id = key(value); validate(id); result.Add(id, value); yield return stage;
            }
            done(result);
        }
        internal static ResumableWork<Dictionary<string, T>> Index<T>(IEnumerable<T> values, Func<T, string> key, string stage, Action<string> validate = null)
            => new ResumableWork<Dictionary<string, T>>(done => IndexRoutine(values, key, stage, validate, done));
        private static IEnumerable<string> IndexRoutine<T>(IEnumerable<T> values, Func<T, string> key, string stage, Action<string> validate, Action<Dictionary<string, T>> done)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if ((object)value == null) throw new InvalidOperationException("Null domain sample");
                string id = key(value); validate?.Invoke(id); result.Add(id, value); yield return stage;
            }
            done(result);
        }
        internal static ResumableWork<Dictionary<string, T>> Copy<T>(IEnumerable<KeyValuePair<string, T>> values, string stage)
            => new ResumableWork<Dictionary<string, T>>(done => CopyRoutine(values, stage, done));
        private static IEnumerable<string> CopyRoutine<T>(IEnumerable<KeyValuePair<string, T>> values, string stage, Action<Dictionary<string, T>> done)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var result = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values) { result.Add(value.Key, value.Value); yield return stage; }
            done(result);
        }
        internal static ResumableWork<List<T>> List<T>(IEnumerable<T> values, string stage)
            => new ResumableWork<List<T>>(done => ListRoutine(values, stage, done));
        private static IEnumerable<string> ListRoutine<T>(IEnumerable<T> values, string stage, Action<List<T>> done)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var result = new List<T>(); foreach (var value in values) { result.Add(value); yield return stage; } done(result);
        }
        internal static ResumableWork<T> Atomic<T>(Func<T> operation, string stage)
            => new ResumableWork<T>(done => AtomicRoutine(operation, stage, done));
        private static IEnumerable<string> AtomicRoutine<T>(Func<T> operation, string stage, Action<T> done)
        { done(operation()); yield return stage; }
        internal static ResumableWork<IOperationSystems> Fork(IOperationSystems owner)
            => owner is IResumableOperationSystems resumable ? resumable.ForkWork() : Atomic(owner.Fork, "legacy.domain.fork.atomic");
        internal static ResumableWork<JsonValue> State(IOperationSystems owner)
            => owner is IResumableOperationSystems resumable ? resumable.ReadDomainStateWork() : Atomic(owner.ReadDomainState, "legacy.domain.state.atomic");
        internal static ResumableWork<ValidationResult> Validate(IOperationSystems owner, GameCommand command, OperationContext context)
            => owner is IResumableCommandSystems resumable ? resumable.ValidateWork(command, context)
                : Atomic(() => owner.Validate(command, context), "legacy.command.validate.atomic");
        internal static ResumableWork<bool> Apply(IOperationSystems owner, GameCommand command, OperationContext context)
            => owner is IResumableCommandSystems resumable ? resumable.ApplyWork(command, context)
                : Atomic(() => { owner.Apply(command, context); return true; }, "legacy.command.apply.atomic");
        internal static ResumableWork<bool> Stage(IOperationSystems owner, OperationStage stage, OperationContext context)
            => owner is IResumableOperationSystems resumable ? resumable.StageWork(stage, context) : Atomic(() =>
            {
                switch (stage)
                {
                    case OperationStage.ResolvePowerAndReservations: owner.ResolvePowerAndReservations(context); break;
                    case OperationStage.SimulateHazardInterval: owner.SimulateHazardInterval(context); break;
                    case OperationStage.RecomputeSafetyAndDamage: owner.RecomputeSafetyAndDamage(context); break;
                    case OperationStage.ResolveMovementAndTransfers: owner.ResolveMovementAndTransfers(context); break;
                    case OperationStage.DeriveMetricsAndObjectives: owner.DeriveMetricsAndObjectives(context); break;
                    case OperationStage.ValidateBoundary: owner.ValidateBoundary(context); break;
                    case OperationStage.SampleInitialObjectives: owner.SampleInitialObjectives(context); break;
                    default: throw new ArgumentOutOfRangeException(nameof(stage));
                }
                return true;
            }, "legacy.domain." + stage + ".atomic");
        internal static ResumableWork<JsonValue> Rows<T>(IEnumerable<T> rows, Func<T, JsonValue> map, string stage)
            => new ResumableWork<JsonValue>(done => ReadRows(rows, map, stage, done));
        private static IEnumerable<string> ReadRows<T>(IEnumerable<T> rows, Func<T, JsonValue> map, string stage, Action<JsonValue> done)
        {
            var result = new List<JsonValue>();
            foreach (var row in rows) { result.Add(map(row)); yield return stage; }
            using (var array = JsonValue.ArrayWork(result))
            { while (array.MoveNext()) yield return array.Stage; done(array.Result); }
        }
    }
}
