using System;

namespace Resq.Sim
{
    public sealed partial class Simulator
    {
        public const int MilestoneOperationStarted = -2;
        public const int MilestoneOperationEnded = -3;

        long MilestoneTick(int m)
        {
            if (m == MilestoneOperationStarted) return S.C.OperationStartedEmitted ? S.C.StartedO : -1;
            if (m == MilestoneOperationEnded) return S.C.OperationEnded ? S.C.O : -1;
            return m >= 0 ? S.MilestoneO[m] : -1;
        }

        void UpdateMilestones()
        {
            for (int i = 0; i < D.Milestones.Length; i++)
            {
                if (S.MilestoneO[i] >= 0) continue;
                var m = D.Milestones[i];
                bool hit = false;
                switch (m.Kind)
                {
                    case MilestoneKind.GroupBoarded:
                        hit = true;
                        foreach (int r in D.GroupResidents[m.Target]) if (!S.RBoarded[r]) { hit = false; break; }
                        break;
                    case MilestoneKind.AllEvacuated:
                        hit = S.C.Evacuated == D.TotalResidents;
                        break;
                    case MilestoneKind.FinalUnloadStarted:
                        hit = FinalUnloadStarted();
                        break;
                    case MilestoneKind.FirstCooledCrossingClosed:
                        hit = WorldMilestone(m);
                        break;
                }
                if (hit)
                {
                    S.MilestoneO[i] = S.C.O;
                    S.MilestoneS[i] = S.C.S;
                    Emit(SimEventKind.MilestoneReached, i);
                }
            }
        }

        bool FinalUnloadStarted()
        {
            bool anyUnloading = false;
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                var st = (ResidentState)S.RState[r];
                if (st == ResidentState.Evacuated) continue;
                if (st != ResidentState.Aboard && st != ResidentState.Unboarding) return false;
                int v = S.RVehicle[r];
                if (v < 0 || !S.VEvacuate[v]) return false;
                if (st == ResidentState.Unboarding) anyUnloading = true;
            }
            return anyUnloading;
        }

        // ----------------------------------------------------------------- conditions
        public bool EvaluateCondition(Condition c)
        {
            switch (c.Kind)
            {
                case ConditionKind.RescueCount: return S.C.Evacuated >= c.Value;
                case ConditionKind.GroupEvacuated: return GroupEvacuatedCount(c.A) >= c.Value;
                case ConditionKind.InfrastructureOperational: return S.InfraOperational[c.A] == c.Flag;
                case ConditionKind.RouteAvailable: return Reachable(D.RouteFrom[c.A], D.RouteTo[c.A], D.RouteClass[c.A], false);
                case ConditionKind.ExposureBelow: return HazardLevel(c.A, c.Hazard) < c.Value;
                case ConditionKind.EquipmentLossesAtMost: return S.C.Losses <= c.Value;
                case ConditionKind.SupplyRemainingAtLeast: return RemainingSupplies >= c.Value;
                case ConditionKind.ResourceRemaining: return (c.Flag ? S.CoolantStore[c.A] : WorldResource(c.A)) >= c.Value;
                case ConditionKind.AllVehiclesUnloaded: return AllVehiclesUnloaded();
                case ConditionKind.TemporaryShelterEmpty: return CountAtFlag(NodeFlags.Shelter) == 0;
                case ConditionKind.TemporaryStagingEmpty: return CountAtFlag(NodeFlags.Staging) == 0;
                case ConditionKind.ScenarioElapsedAtMost: return S.C.S <= c.Value;
                case ConditionKind.CounterAtLeast: return Metric(c) >= c.Value;
                case ConditionKind.CounterAtMost: return Metric(c) <= c.Value;
                case ConditionKind.All:
                    foreach (var ch in c.Children) if (!EvaluateCondition(ch)) return false;
                    return true;
                case ConditionKind.Any:
                    foreach (var ch in c.Children) if (EvaluateCondition(ch)) return true;
                    return false;
            }
            return false;
        }

        public int GroupEvacuatedCount(int g)
        {
            int n = 0;
            foreach (int r in D.GroupResidents[g]) if (S.RState[r] == (byte)ResidentState.Evacuated) n++;
            return n;
        }

        long HazardLevel(int region, int hazard)
        {
            if (hazard == 0) return RegionDepth(region);
            return WorldHazardLevel(region, hazard);
        }

        public bool AllVehiclesUnloaded()
        {
            for (int v = 0; v < S.C.VehicleCount; v++) if (S.VOccupantCount[v] > 0) return false;
            return true;
        }

        int CountAtFlag(NodeFlags flag)
        {
            int n = 0;
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                var st = (ResidentState)S.RState[r];
                if ((st == ResidentState.AtNode || st == ResidentState.Boarding) && (D.NodeFlags[S.RNode[r]] & flag) != 0) n++;
            }
            return n;
        }

        long Metric(Condition c)
        {
            switch (c.Metric)
            {
                case MetricKind.EvacuatedByMode:
                {
                    long n = 0;
                    for (int r = 0; r < D.ResidentIds.Length; r++)
                    {
                        if (S.RState[r] != (byte)ResidentState.Evacuated) continue;
                        if (c.MetricTarget >= 0 && D.ResidentGroup[r] != c.MetricTarget) continue;
                        if (S.RFinalMode[r] == c.MetricMode) n++;
                    }
                    return n;
                }
                case MetricKind.EquipmentDeployments: return S.Deployed[c.MetricMode];
                case MetricKind.PeakNodeOccupancy: return S.NodePeak[c.MetricTarget];
                case MetricKind.LoadedClosedDockWait: return S.C.LoadedClosedDockWait;
                case MetricKind.PumpedIntoRegion: return S.PumpedIntoRegion[c.MetricTarget];
                case MetricKind.GeneratorShutdowns: return S.C.GeneratorShutdowns;
                case MetricKind.StationShutdowns: return S.C.StationShutdowns;
                case MetricKind.ShelterExhaustions: return S.C.ShelterExhaustions;
                default: return WorldMetric(c);
            }
        }

        // ----------------------------------------------------------------- temporal monitors
        void EvaluateObjectives(bool initialSample, bool terminal = false)
        {
            long o = S.C.O;
            for (int i = 0; i < D.Objectives.Length; i++)
            {
                var obj = D.Objectives[i];
                if (S.ObjFailed[i]) continue;
                switch (obj.Temporal)
                {
                    case TemporalKind.AtEnd:
                        if (terminal) S.ObjLatched[i] = EvaluateCondition(obj.Condition);
                        break;
                    case TemporalKind.EverBy:
                        if (!S.ObjLatched[i] && S.C.S <= obj.DeadlineTick && EvaluateCondition(obj.Condition))
                        { S.ObjLatched[i] = true; S.ObjFirstTrue[i] = o; }
                        break;
                    case TemporalKind.EverBefore:
                    {
                        if (S.ObjLatched[i]) break;
                        long close = MilestoneTick(obj.EndMilestone);
                        if (close >= 0 && o >= close) break; // strict: cannot latch on/after the closing tick
                        if (EvaluateCondition(obj.Condition)) { S.ObjLatched[i] = true; S.ObjFirstTrue[i] = o; }
                        break;
                    }
                    case TemporalKind.Always:
                    {
                        long start = MilestoneTick(obj.StartMilestone);
                        long end = MilestoneTick(obj.EndMilestone);
                        if (start < 0 || o < start) break;
                        if (end >= 0 && o > end) break;
                        if (!EvaluateCondition(obj.Condition)) { S.ObjFailed[i] = true; Emit(SimEventKind.ObjectiveUpdated, i, 0); }
                        break;
                    }
                    case TemporalKind.HoldFor:
                    {
                        long start = MilestoneTick(obj.StartMilestone);
                        long end = MilestoneTick(obj.EndMilestone);
                        if (start < 0 || o < start) break;
                        if (end >= 0 && o > end) break;
                        if (!EvaluateCondition(obj.Condition)) S.ObjHold[i] = 0;
                        else if (_hazardPulse && o > start) S.ObjHold[i]++;
                        break;
                    }
                }
            }
        }

        public bool ObjectiveFulfilled(int i)
        {
            var obj = D.Objectives[i];
            if (S.ObjFailed[i]) return false;
            switch (obj.Temporal)
            {
                case TemporalKind.AtEnd:
                    return S.C.OperationEnded ? S.ObjLatched[i] : EvaluateCondition(obj.Condition);
                case TemporalKind.EverBy:
                case TemporalKind.EverBefore:
                    return S.ObjLatched[i];
                case TemporalKind.Always:
                    return MilestoneTick(obj.StartMilestone) >= 0 && MilestoneTick(obj.EndMilestone) >= 0;
                case TemporalKind.HoldFor:
                    return MilestoneTick(obj.EndMilestone) >= 0 && S.ObjHold[i] >= obj.HoldTicks;
            }
            return false;
        }

        /// <summary>Global success predicate (spec 02 §11), evaluated on the current state.</summary>
        public bool TerminalConditionsMet()
        {
            if (S.C.Evacuated < D.MinimumRescues) return false;
            for (int i = 0; i < D.Objectives.Length; i++)
            {
                if (D.Objectives[i].Tier != Tier.Required) continue;
                var obj = D.Objectives[i];
                bool ok = obj.Temporal == TemporalKind.AtEnd ? EvaluateCondition(obj.Condition) : ObjectiveFulfilled(i);
                if (!ok) return false;
            }
            return AllVehiclesUnloaded() && CountAtFlag(NodeFlags.Shelter) == 0;
        }

        void ComputeOutcome()
        {
            bool success = TerminalConditionsMet() && !(S.C.EndConfirmedFailure && !TerminalConditionsMet());
            int requiredTotal = 0, requiredDone = 0;
            bool allCritical = true, allMastery = true;
            for (int i = 0; i < D.Objectives.Length; i++)
            {
                bool done = ObjectiveFulfilled(i);
                switch (D.Objectives[i].Tier)
                {
                    case Tier.Required: requiredTotal++; if (done) requiredDone++; break;
                    case Tier.Critical: allCritical &= done; break;
                    case Tier.Mastery: allMastery &= done; break;
                }
            }
            int protTotal = D.ProtectionObjectives.Length, protDone = 0;
            foreach (int p in D.ProtectionObjectives) if (ObjectiveFulfilled(p)) protDone++;
            int rescued = S.C.Evacuated;
            int stars = 0;
            if (success)
            {
                stars = 1;
                if (10 * rescued >= 9 * D.TotalResidents && allCritical) stars = 2;
                if (rescued == D.TotalResidents && allCritical && allMastery) stars = 3;
            }
            var score = Score.Compute(rescued, D.TotalResidents, requiredDone, Math.Max(1, requiredTotal),
                protDone, protTotal, Math.Max(0, RemainingSupplies), D.SupplyBudget);
            S.C.Success = success;
            S.C.Stars = stars;
            S.C.ScoreValue = score.Rounded;
        }
    }
}
