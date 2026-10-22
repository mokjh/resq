using System;
using System.Collections.Generic;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Simulation;
using Resq.Simulation.Rescue;
using Resq.Simulation.Tools;

namespace Resq.Persistence
{
    public sealed class SimulationFactory
    {
        private readonly CompiledContent content;
        private readonly string sourceBuildId;
        private readonly Func<CompiledContent, JsonValue, IOperationSystems> restoreDomain;
        private readonly Func<CompiledContent, JsonValue, ResumableWork<IOperationSystems>> restoreDomainWork;
        public SimulationFactory(CompiledContent content, string sourceBuildId, Func<CompiledContent, JsonValue, IOperationSystems> restoreDomain, Func<CompiledContent, JsonValue, ResumableWork<IOperationSystems>> restoreDomainWork = null)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            SnapshotContract.Id(JsonValue.String(sourceBuildId)); this.sourceBuildId = sourceBuildId;
            this.restoreDomain = restoreDomain ?? throw new ArgumentNullException(nameof(restoreDomain));
            this.restoreDomainWork = restoreDomainWork;
        }
        public CompiledContent BoundContent(JsonValue identity)
        {
            RunIdentity.Validate(identity);
            if (identity["source_build_id"].Text != sourceBuildId || identity["rules_version"].Text != "resq-rules-1") throw new SaveRunIncompatibleException("Exact source build/rules required; cross-target compatibility is unqualified");
            var effective = content;
            if (identity["challenge_id"].Text != "base" && !content.ChallengeVariants.TryGetValue(identity["challenge_id"].Text, out effective)) throw new SaveRunIncompatibleException("Expanded challenge unavailable");
            if (effective.ContentHash != identity["content_hash"].Text || !effective.Envelope.payload.missions.Any(m => m.mission_version_id == identity["mission_version_id"].Text) || !effective.Envelope.payload.layouts.Any(l => l.id == identity["layout_version_id"].Text && l.mission_version_id == identity["mission_version_id"].Text) || effective.LayoutSignature(identity["layout_version_id"].Text) != identity["layout_signature"].Text) throw new SaveRunIncompatibleException("Exact saved content/mission/layout unavailable; preserve campaign progress and offer an explicit restart");
            return effective;
        }
        public OperationClock Restore(JsonValue snapshot) => RestoreWork(snapshot).Drain();
        public ResumableWork<OperationClock> RestoreWork(JsonValue snapshot)
            => new ResumableWork<OperationClock>(done => RestoreSteps(snapshot, false, false, done));
        public OperationClock RestoreForReplay(JsonValue snapshot, bool runningAtInitialBoundary = true)
            => new ResumableWork<OperationClock>(done => RestoreSteps(snapshot, runningAtInitialBoundary, false, done)).Drain();
        public OperationClock RestoreForForecast(JsonValue snapshot) => RestoreForForecastWork(snapshot).Drain();
        public ResumableWork<OperationClock> RestoreForForecastWork(JsonValue snapshot)
            => new ResumableWork<OperationClock>(done => RestoreSteps(snapshot, false, true, done));
        private IEnumerable<string> RestoreSteps(JsonValue snapshot, bool replayRunning, bool forecast, Action<OperationClock> complete)
        {
            using (var contract = SnapshotContract.ValidateWork(snapshot)) { while (contract.MoveNext()) yield return contract.Stage; }
            var effective = BoundContent(snapshot["identity"]); yield return "restore.content_binding";
            IOperationSystems owner;
            if (restoreDomainWork != null)
            {
                using (var domain = restoreDomainWork(effective, snapshot))
                { while (domain.MoveNext()) yield return domain.Stage; owner = domain.Result; }
            }
            else
            {
                if (forecast) throw new InvalidOperationException("Forecast requires the actual resumable domain restore owner");
                owner = restoreDomain(effective, snapshot); yield return "restore.legacy_domain";
            }
            if (owner == null) throw new InvalidOperationException("Actual restored domain owner required");
            if (forecast && !(owner is IResumableOperationSystems)) throw new InvalidOperationException("Forecast requires actual resumable simulation owners");
            using (var clock = forecast ? OperationClock.RestoreForForecastWork(content, snapshot, owner) :
                replayRunning ? OperationClock.RestoreForReplayWork(content, snapshot, owner) : OperationClock.RestoreWork(content, snapshot, owner))
            { while (clock.MoveNext()) yield return clock.Stage; complete(clock.Result); }
        }
        public void Validate(JsonValue snapshot) { Restore(snapshot); }
    }

    // Reconstruct accepted C03-C05 authorities from their single saved owners.
    // Actual world modules provide safety/mechanic observations, never defaults.
    public static class SnapshotOwners
    {
        public static EquipmentSystem RestoreEquipment(CompiledContent effectiveContent, JsonValue snapshot,
            Func<EquipmentSystem, PowerFrame> actualPowerFrame, IEquipmentMechanics mechanics = null)
            => RestoreEquipmentWork(effectiveContent, snapshot, actualPowerFrame, mechanics).Drain();
        public static ResumableWork<EquipmentSystem> RestoreEquipmentWork(CompiledContent effectiveContent, JsonValue snapshot,
            Func<EquipmentSystem, PowerFrame> actualPowerFrame, IEquipmentMechanics mechanics = null,
            Func<EquipmentSystem, ResumableWork<PowerFrame>> actualPowerFrameWork = null)
            => new ResumableWork<EquipmentSystem>(done => RestoreEquipmentSteps(effectiveContent, snapshot, actualPowerFrame, mechanics, actualPowerFrameWork, done));
        private static IEnumerable<string> RestoreEquipmentSteps(CompiledContent effectiveContent, JsonValue snapshot,
            Func<EquipmentSystem, PowerFrame> actualPowerFrame, IEquipmentMechanics mechanics,
            Func<EquipmentSystem, ResumableWork<PowerFrame>> actualPowerFrameWork, Action<EquipmentSystem> complete)
        {
            if (actualPowerFrame == null) throw new ArgumentNullException(nameof(actualPowerFrame));
            var identity = snapshot["identity"];
            var mission = effectiveContent.Envelope.payload.missions.Single(m => m.mission_version_id == identity["mission_version_id"].Text);
            var layout = effectiveContent.Envelope.payload.layouts.Single(l => l.id == identity["layout_version_id"].Text);
            if ((layout.fire_cells.Count > 0 && (mechanics == null || !mechanics.FireAvailable)) || (layout.supports.Count > 0 && (mechanics == null || !mechanics.StructureAvailable)) || (layout.lava_segments.Count > 0 && (mechanics == null || !mechanics.LavaAvailable))) throw new SaveRunIncompatibleException("Corresponding actual world-state owner unavailable");
            yield return "restore.equipment.references";
            WaterSystem waterShell;
            using (var create = WaterSystem.CreateWork(layout)) { while (create.MoveNext()) yield return create.Stage; waterShell = create.Result; }
            WaterSystem water;
            using (var work = waterShell.RestoreWork(snapshot["water"], snapshot["rate_remainders"], snapshot["water_ledger"]))
            { while (work.MoveNext()) yield return work.Stage; water = work.Result; }
            TransportSystem transportShell;
            using (var create = TransportSystem.CreateWork(mission, layout, effectiveContent.Envelope.payload.equipment)) { while (create.MoveNext()) yield return create.Stage; transportShell = create.Result; }
            TransportSystem transport;
            using (var work = transportShell.RestoreWork(snapshot)) { while (work.MoveNext()) yield return work.Stage; transport = work.Result; }
            var selected = new List<EquipmentCount>();
            foreach (var row in snapshot["selected_stock"].Items) { selected.Add(EquipmentCount.Read(row)); yield return "restore.equipment.stock"; }
            EquipmentSystem equipment;
            using (var create = EquipmentSystem.CreateWork(mission, layout, effectiveContent.Envelope.payload.equipment, waterShell, transportShell, mechanics, selected))
            { while (create.MoveNext()) yield return create.Stage; equipment = create.Result; }
            var fields = new[] { "supplies_remaining", "selected_stock", "deployments", "equipment", "power", "stores", "producers" };
            var owned = JsonValue.Object(fields.ToDictionary(f => f, f => snapshot[f], StringComparer.Ordinal));
            using (var work = equipment.RestoreOwnedStateWork(owned, snapshot["operation_tick"].Integer, actualPowerFrame, water, transport, actualPowerFrameWork))
            { while (work.MoveNext()) yield return work.Stage; complete(work.Result); }
        }
    }
}
