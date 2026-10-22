using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Resq.Content;
using Resq.Simulation.Objectives;

namespace Resq.Simulation
{
    // Read-only facts from the actual hazard/safety owner at a completed boundary.
    // Unsafe must use that owner's real admission/exposure checks. Forecast never
    // invents a hazard threshold or infers safety from absence of a sample.
    public sealed class ForecastRiskSample
    {
        public string RegionId { get; }
        public string Hazard { get; }
        public long Value { get; }
        public bool Unsafe { get; }
        public ForecastRiskSample(string regionId, string hazard, long value, bool unsafeNow)
        {
            if (string.IsNullOrEmpty(regionId) || string.IsNullOrEmpty(hazard) || value < 0) throw new ArgumentException("Actual bound region/hazard observation required");
            RegionId = regionId; Hazard = hazard; Value = value; Unsafe = unsafeNow;
        }
    }
    public sealed class ForecastBoundaryObservations
    {
        public long OperationTick { get; }
        public ObjectiveObservations Objectives { get; }
        public ReadOnlyCollection<ForecastRiskSample> Risks { get; }
        public ForecastBoundaryObservations(long operationTick, ObjectiveObservations objectives, IEnumerable<ForecastRiskSample> risks)
        {
            OperationTick = operationTick; Objectives = objectives ?? throw new ArgumentNullException(nameof(objectives));
            Risks = (risks ?? throw new ArgumentNullException(nameof(risks))).OrderBy(x => x.RegionId, StringComparer.Ordinal).ThenBy(x => x.Hazard, StringComparer.Ordinal).ToList().AsReadOnly();
            if (Objectives.OperationTick != operationTick || Risks.Select(x => x.RegionId + ":" + x.Hazard).Distinct(StringComparer.Ordinal).Count() != Risks.Count)
                throw new InvalidOperationException("Forecast observation boundary/uniqueness mismatch");
            foreach (var risk in Risks)
                if (!Objectives.RegionExposure.TryGetValue(ObjectiveObservations.ExposureKey(risk.RegionId, risk.Hazard), out var exposure) || exposure != risk.Value)
                    throw new InvalidOperationException("Forecast risk must match the actual objective exposure owner");
            if (Risks.Count != Objectives.RegionExposure.Count) throw new InvalidOperationException("Every actual region/hazard exposure requires a safety sample");
        }
    }
    public interface IForecastDomainObserver
    {
        // No Step/Derive, event publication, consumption or history replay here.
        // Return this fork's complete real hazard/safety samples or fail closed.
        void FreezeForecastKnowledge(IReadOnlyCollection<string> revealedEventIds);
        IEnumerable<ForecastRiskSample> ObserveForecastRisk(OperationContext context, JsonValue domainState);
    }
}
