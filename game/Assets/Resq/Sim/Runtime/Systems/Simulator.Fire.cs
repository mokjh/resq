using System;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    /// <summary>
    /// World 2 fire, wind, moisture and finite spray (spec 02 §7). Cells update synchronously from the old state;
    /// spray comes from the actual sprinkler grants allocated by the water pulse this interval.
    /// Wind code d means fire spreads toward compass direction d (1 W, 2 E, 3 N, 4 S, 5 NE, 6 SE, 7 SW, 8 NW).
    /// </summary>
    public sealed partial class Simulator
    {
        public const int FireLatchHeat = 300;
        public const int FireReopenHeat = 100;
        public const int FireDangerHeat = 600;

        long[] _spray;             // mm³ sprayed onto each cell this pulse (scratch)
        byte[] _fireOldState;
        int[] _fireOldHeat;

        static readonly int[] DirX = { 0, -1, 1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DirY = { 0, 0, 0, 1, -1, 1, -1, -1, 1 };

        void AllocateFireScratch()
        {
            int n = D.GridW * D.GridH;
            _spray = new long[n];
            _fireOldState = new byte[n];
            _fireOldHeat = new int[n];
        }

        void InitFire()
        {
            int n = D.GridW * D.GridH;
            if (!D.FireEnabled) return;
            for (int c = 0; c < n; c++)
            {
                S.FFuel[c] = D.CellFuel0[c];
                S.FHeat[c] = D.CellHeat0[c];
                S.FWet[c] = D.CellWet0[c];
                S.FState[c] = (byte)FireState.Unburned;
                S.FWarnStart[c] = -1;
                S.FCoolStart[c] = -1;
                if (D.CellBurning0[c])
                {
                    S.FState[c] = (byte)FireState.Burning;
                    S.FHeat[c] = Math.Max(S.FHeat[c], FireDangerHeat);
                    S.FWarnStart[c] = 0;
                }
                else if (S.FHeat[c] >= FireLatchHeat)
                {
                    S.FState[c] = (byte)FireState.Heating; // a warm start is already warned at S=0
                    S.FWarnStart[c] = 0;
                }
                if (S.FHeat[c] >= FireLatchHeat || D.CellBurning0[c]) S.FLatch[c] = true;
            }
        }

        public FireState CellFire(int cell) => (FireState)S.FState[cell];
        public int CellHeat(int cell) => S.FHeat[cell];

        /// <summary>Octant 0..7 of a grid direction, counter-clockwise from east.</summary>
        static int Octant(int dx, int dy)
        {
            if (dx > 0 && dy == 0) return 0;
            if (dx > 0 && dy > 0) return 1;
            if (dx == 0 && dy > 0) return 2;
            if (dx < 0 && dy > 0) return 3;
            if (dx < 0 && dy == 0) return 4;
            if (dx < 0 && dy < 0) return 5;
            if (dx == 0 && dy < 0) return 6;
            return 7;
        }

        void FirePulse()
        {
            if (!D.FireEnabled) return;
            int w = D.GridW, h = D.GridH, n = w * h;
            long s = S.C.S;
            Array.Copy(S.FState, _fireOldState, n);
            Array.Copy(S.FHeat, _fireOldHeat, n);
            for (int c = 0; c < n; c++)
            {
                var old = (FireState)_fireOldState[c];
                int heat = _fireOldHeat[c];
                int fuel = S.FFuel[c];
                int wet = FireRules.UpdateWetness(S.FWet[c], _spray[c], ref S.FSprayRem[c]);
                S.FWet[c] = wet;

                // incoming heat from old Burning neighbours, directional by the receiving cell's wind
                int windRegion = D.CellWindRegion[c];
                int wind = windRegion >= 0 ? S.WindCode[windRegion] : 0;
                int windOct = wind > 0 ? Octant(DirX[wind], DirY[wind]) : -1;
                int incoming = 0;
                int x = c % w, y = c / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int nb = ny * w + nx;
                        if ((FireState)_fireOldState[nb] != FireState.Burning) continue;
                        if (windOct < 0) { incoming += 8; continue; }
                        int linkOct = Octant(-dx, -dy); // direction from neighbour toward this cell
                        int diff = Math.Abs(linkOct - windOct); diff = Math.Min(diff, 8 - diff);
                        incoming += diff <= 1 ? 12 : diff >= 3 ? 4 : 8;
                    }

                // every cell takes heat (roads and water latch closed next to fire); idle cold cells are a no-op
                if (incoming == 0 && heat == 0 && old == FireState.Unburned && !S.FLatch[c]) continue;
                int extra = S.FExtraCool[c];
                int nextHeat = FireRules.NextHeat(old, heat, fuel, wet, incoming, extra);
                int nextFuel = FireRules.NextFuel(old, fuel, wet);
                var next = old;
                switch (old)
                {
                    case FireState.Unburned:
                        if (nextHeat >= FireLatchHeat && nextFuel > 0)
                        {
                            next = FireState.Heating;
                            if (S.FWarnStart[c] < 0) S.FWarnStart[c] = s; // emergent heating starts its own warning
                        }
                        break;
                    case FireState.Heating:
                        if (nextHeat < FireLatchHeat) { next = FireState.Unburned; if (!ScheduledWarning(c)) S.FWarnStart[c] = -1; }
                        else if (nextHeat >= FireDangerHeat && nextFuel > 0 && wet < 600 &&
                                 S.FWarnStart[c] >= 0 && s - S.FWarnStart[c] >= FireRules.IgnitionWarningTicks)
                            next = FireState.Burning;
                        break;
                    case FireState.Burning:
                        if (nextFuel == 0 || wet >= 600) { next = FireState.Cooling; S.FCoolStart[c] = s; }
                        break;
                    case FireState.Cooling:
                        if (s - S.FCoolStart[c] >= FireRules.CoolingMinimumTicks && nextHeat <= FireReopenHeat)
                        {
                            next = nextFuel == 0 ? FireState.SafeAsh : FireState.Unburned;
                            S.FWarnStart[c] = -1;
                        }
                        break;
                }
                S.FHeat[c] = nextHeat;
                S.FFuel[c] = nextFuel;
                S.FState[c] = (byte)next;
                // ground admission latch
                if (nextHeat >= FireLatchHeat || next == FireState.Burning || next == FireState.Cooling) S.FLatch[c] = true;
                else if (S.FLatch[c] && nextHeat <= FireReopenHeat && (next == FireState.Unburned || next == FireState.SafeAsh)) S.FLatch[c] = false;
            }
            Array.Clear(_spray, 0, n);
        }

        bool ScheduledWarning(int cell) => false; // scheduled ignition warnings keep their stamp until ignition

        /// <summary>Called when an event's warning is published: scheduled ignitions start the cells' warning clock.</summary>
        void FireOnWarning(EventDef ev)
        {
            if (!D.FireEnabled) return;
            foreach (var op in ev.Ops)
            {
                if (op.Kind != EventOpKind.IgniteRegion) continue;
                foreach (int region in op.Targets)
                    foreach (int c in D.RegionCells[region])
                        if (S.FWarnStart[c] < 0) S.FWarnStart[c] = S.C.S;
            }
        }

        void ApplyFireOp(EventOp op)
        {
            switch (op.Kind)
            {
                case EventOpKind.IgniteRegion:
                    foreach (int region in op.Targets)
                        foreach (int c in D.RegionCells[region])
                        {
                            S.FHeat[c] = Math.Max(S.FHeat[c], (int)op.Value);
                            if (S.FFuel[c] > 0 && (FireState)S.FState[c] == FireState.Unburned && S.FHeat[c] >= FireLatchHeat)
                            {
                                S.FState[c] = (byte)FireState.Heating;
                                if (S.FWarnStart[c] < 0) S.FWarnStart[c] = S.C.S;
                            }
                            if (S.FHeat[c] >= FireLatchHeat) S.FLatch[c] = true;
                        }
                    break;
                case EventOpKind.AddHeat:
                    foreach (int region in op.Targets)
                        foreach (int c in D.RegionCells[region])
                            S.FHeat[c] = Math.Min(1000, S.FHeat[c] + (int)op.Value);
                    break;
                case EventOpKind.SetWind:
                    foreach (int region in op.Targets) S.WindCode[region] = (int)op.Value;
                    break;
                case EventOpKind.SetRegionCooling:
                    foreach (int region in op.Targets)
                        foreach (int c in D.RegionCells[region]) S.FExtraCool[c] = (int)op.Value;
                    break;
            }
        }

        /// <summary>Firebreak: irreversible fuel removal on its cells (heat remains; warm cells observe cooling).</summary>
        Reject ValidateFirebreak(int socket)
        {
            foreach (int c in D.SocketCells[socket])
            {
                if (D.CellBuilding[c]) return Reject.SocketIncompatible;
                if ((FireState)S.FState[c] == FireState.Burning) return Reject.UnsafeTarget;
            }
            return Reject.OK;
        }

        void ActivateFirebreak(int socket)
        {
            foreach (int c in D.SocketCells[socket])
            {
                S.FFuel[c] = 0;
                var st = (FireState)S.FState[c];
                if (S.FHeat[c] > 0 && st != FireState.Cooling) { S.FState[c] = (byte)FireState.Cooling; S.FCoolStart[c] = S.C.S; }
                else if (S.FHeat[c] == 0) S.FState[c] = (byte)FireState.SafeAsh;
            }
        }

        /// <summary>Sprinkler requests compete with other outflows of the store node (called from the water pulse).</summary>
        void AddSprinklerRequests(int node, ref int count)
        {
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                if ((ToolKind)S.EqKind[inst] != ToolKind.Sprinkler || S.EqBroken[inst] || !S.EqActive[inst]) continue;
                int sock = S.EqSocket[inst];
                int store = D.SocketStore[sock];
                if (store < 0 || D.StoreNode[store] != node || S.EqPowerGrant[inst] <= 0) continue;
                long q = WaterRules.RatePulse(5_000_000, ref S.EqRemainder[inst]);
                if (q > 0) AddRequest(ref count, q, -1, 3, inst);
            }
        }

        void ApplySprayGrant(int inst, long granted)
        {
            var cells = D.SocketCells[S.EqSocket[inst]];
            if (cells.Length == 0 || granted <= 0) return;
            long each = granted / cells.Length, rem = granted % cells.Length;
            for (int i = 0; i < cells.Length; i++) _spray[cells[i]] += each + (i < rem ? 1 : 0);
        }

        long MaxRegionHeat(int region)
        {
            int max = 0;
            foreach (int c in D.RegionCells[region]) if (c < S.FHeat.Length) max = Math.Max(max, S.FHeat[c]);
            return max;
        }
    }
}
