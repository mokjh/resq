namespace Resq.Sim
{
    /// <summary>
    /// Extension points for World 2–5 hazard modules (fire/wind, stability/runout, tides, lava/coolant/shelters).
    /// Alder Valley uses none of them; modules plug in here without touching the core step order.
    /// </summary>
    public sealed partial class Simulator
    {
        void HazardModulesPulse() { }

        void ApplyWorldEventOp(EventOp op) { }

        Reject ExecuteWorldCommand(Command c) => Reject.UnknownKind;

        bool WorldEdgeOpen(int edge) => true;

        bool WorldNodeSafe(int node) => true;

        bool WorldMilestone(MilestoneDef m) => false;

        long WorldHazardLevel(int region, int hazard) => 0;

        long WorldResource(int store) => 0;

        long WorldMetric(Condition c) => 0;
    }
}
