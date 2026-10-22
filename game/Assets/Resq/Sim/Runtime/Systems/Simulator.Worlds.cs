namespace Resq.Sim
{
    /// <summary>
    /// Dispatch to World 2–5 hazard modules (fire/wind and stability/runout/winch and tides/relays now; lava/coolant/shelters next).
    /// Alder Valley content leaves them inert, so W1 behaviour is unchanged.
    /// </summary>
    public sealed partial class Simulator
    {
        void HazardModulesPulse()
        {
            FirePulse();
            StabilityPulse();   // a collapse here may start its bound runout this pulse
            RunoutPulse();
        }

        void WorldOnWarning(EventDef ev)
        {
            FireOnWarning(ev);
            StabilityOnWarning(ev);
        }

        void ApplyWorldEventOp(EventOp op)
        {
            switch (op.Kind)
            {
                case EventOpKind.IgniteRegion:
                case EventOpKind.AddHeat:
                case EventOpKind.SetWind:
                case EventOpKind.SetRegionCooling:
                    ApplyFireOp(op);
                    break;
                case EventOpKind.SetStabilityLoss:
                case EventOpKind.SetSupportLoad:
                case EventOpKind.Tremor:
                case EventOpKind.WarnSupport:
                case EventOpKind.ReleaseAvalanche:
                    ApplyStabilityOp(op);
                    break;
                case EventOpKind.SetTideSegment:
                    ApplyTideOp(op);
                    break;
            }
        }

        Reject ExecuteWorldCommand(Command c) => Reject.UnknownKind;

        bool WorldEdgeOpen(int edge) => FireEdgeOpen(edge) && StabilityEdgeOpen(edge);

        bool WorldNodeSafe(int node) => FireNodeSafe(node) && StabilityNodeSafe(node);

        bool FireEdgeOpen(int edge)
        {
            if (!D.FireEnabled) return true;
            var cells = D.EdgeSampleCells[edge];
            if (cells == null) return true;
            foreach (int c in cells) if (c < S.FLatch.Length && S.FLatch[c]) return false;
            return true;
        }

        bool FireNodeSafe(int node)
        {
            if (!D.FireEnabled) return true;
            int c = D.NodeCell[node];
            return (Rules.FireState)S.FState[c] != Rules.FireState.Burning && S.FHeat[c] < FireDangerHeat;
        }

        bool WorldMilestone(MilestoneDef m) => false;

        long WorldHazardLevel(int region, int hazard) => hazard == 1 ? MaxRegionHeat(region) : 0;

        long WorldResource(int store) => store >= 0 && store < D.StoreNode.Length ? S.Volume[D.StoreNode[store]] : 0;

        long WorldMetric(Condition c)
        {
            switch (c.Metric)
            {
                case MetricKind.MaxNodeDwell: return S.NodeMaxDwell[c.MetricTarget];
                case MetricKind.EdgeTransfers: return EdgeTransfersTagged(c.MetricMode);
                case MetricKind.MaxDockQueue: return S.NodeMaxQueue[c.MetricTarget];
                default: return 0;
            }
        }
    }
}
