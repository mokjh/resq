namespace Resq.Sim
{
    /// <summary>
    /// Dispatch to World 2–5 hazard modules (fire/wind now; stability/runout, tides, lava/coolant/shelters next).
    /// Alder Valley content leaves them inert, so W1 behaviour is unchanged.
    /// </summary>
    public sealed partial class Simulator
    {
        void HazardModulesPulse()
        {
            FirePulse();
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
            }
        }

        Reject ExecuteWorldCommand(Command c) => Reject.UnknownKind;

        bool WorldEdgeOpen(int edge)
        {
            if (!D.FireEnabled) return true;
            var cells = D.EdgeSampleCells[edge];
            if (cells == null) return true;
            foreach (int c in cells) if (c < S.FLatch.Length && S.FLatch[c]) return false;
            return true;
        }

        bool WorldNodeSafe(int node)
        {
            if (!D.FireEnabled) return true;
            int c = D.NodeCell[node];
            return (Rules.FireState)S.FState[c] != Rules.FireState.Burning && S.FHeat[c] < FireDangerHeat;
        }

        bool WorldMilestone(MilestoneDef m) => false;

        long WorldHazardLevel(int region, int hazard) => hazard == 1 ? MaxRegionHeat(region) : 0;

        long WorldResource(int store) => store >= 0 && store < D.StoreNode.Length ? S.Volume[D.StoreNode[store]] : 0;

        long WorldMetric(Condition c) => 0;
    }
}
