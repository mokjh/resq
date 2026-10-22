using System;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    /// <summary>
    /// World 5 lava, coolers, finite coolant production and temporary shelters (spec 02 §10). Lava is an authored DAG of
    /// segments with scheduled first arrivals, not a fluid. Cooling runs Hot → Cooling (50) → Crusted (200) → Reheating (50) → Hot.
    /// Shelter protection for an interval is decided in stage 5 from the pre-pulse balance and masks exposure in stage 8.
    /// </summary>
    public sealed partial class Simulator
    {
        public const int LavaHotHeat = 1000, LavaCrustHeat = 100, LavaReheatHeat = 600;

        bool[] _shelterProtected; // per equipment instance, this interval only (scratch)

        void AllocateLavaScratch() => _shelterProtected = new bool[D.MaxEquipment];

        void InitLava()
        {
            for (int i = 0; i < D.LavaSegIds.Length; i++)
            {
                S.LState[i] = (byte)LavaState.Dormant;
                S.LArrival[i] = -1; S.LPlanned[i] = -1; S.LCoolStart[i] = -1;
            }
            for (int s = 0; s < D.SocketIds.Length; s++) S.CoolerEnabled[s] = D.SocketInitiallyEnabled[s];
            for (int i = 0; i < D.CoolantIds.Length; i++) S.CoolantStore[i] = D.CoolantCharges0[i];
            for (int i = 0; i < D.InfraIds.Length; i++) S.WorksFeedstock[i] = D.InfraFeedstock0[i];
        }

        public LavaState SegmentState(int seg) => (LavaState)S.LState[seg];

        /// <summary>Earliest arrival per reachable segment from a source activated at <paramref name="t0"/> (DAG relaxation).</summary>
        void ScheduleArrivals(int source, long t0, long[] into)
        {
            int first = D.LavaSourceFirst[source];
            if (into[first] < 0 || t0 < into[first]) into[first] = t0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (int u in D.LavaSourceSegments[source])
                {
                    if (into[u] < 0) continue;
                    var next = D.LavaSegNext[u];
                    for (int j = 0; j < next.Length; j++)
                    {
                        long t = into[u] + D.LavaSegNextDelay[u][j];
                        if (into[next[j]] < 0 || t < into[next[j]]) { into[next[j]] = t; changed = true; }
                    }
                }
            }
        }

        /// <summary>The advance_lava warning publishes the whole front: every downstream arrival is known from this point.</summary>
        void LavaOnWarning(EventDef ev)
        {
            foreach (var op in ev.Ops)
            {
                if (op.Kind != EventOpKind.AdvanceLava || op.Value == 0) continue;
                foreach (int src in op.Targets) ScheduleArrivals(src, ev.AtTick, S.LPlanned);
            }
        }

        void ApplyLavaOp(EventOp op)
        {
            switch (op.Kind)
            {
                case EventOpKind.AdvanceLava:
                    if (op.Value == 0) break;
                    foreach (int src in op.Targets)
                    {
                        if (S.LavaSourceActive[src]) continue;
                        S.LavaSourceActive[src] = true;
                        ScheduleArrivals(src, S.C.S, S.LArrival);
                    }
                    break;
                case EventOpKind.EnableCoolerSocket:
                    foreach (int sock in op.Targets) S.CoolerEnabled[sock] = op.Value != 0;
                    break;
                case EventOpKind.EnableShelterHazard:
                    foreach (int sock in op.Targets) S.ShelterFront[sock] = op.Value != 0;
                    break;
            }
        }

        /// <summary>Stage 5: arrivals and cooling timers, then coolant production from stage-4 power, then shelter protection.</summary>
        void LavaPulse()
        {
            long s = S.C.S;
            for (int i = 0; i < D.LavaSegIds.Length; i++)
            {
                if (!S.LArrived[i] && S.LArrival[i] >= 0 && s >= S.LArrival[i])
                {
                    S.LArrived[i] = true; // each segment receives its first arrival once
                    if ((LavaState)S.LState[i] == LavaState.Dormant) BecomeHot(i);
                }
                if (S.LCoolStart[i] < 0) continue;
                var next = LavaRules.StateAfterCooling(S.LCoolStart[i], s);
                var old = (LavaState)S.LState[i];
                if (next == old) continue;
                switch (next)
                {
                    case LavaState.Crusted: S.LState[i] = (byte)next; S.LHeat[i] = LavaCrustHeat; break;
                    case LavaState.Reheating:
                        S.LState[i] = (byte)next; S.LHeat[i] = LavaReheatHeat;
                        if (!S.LReheated[i]) { S.LReheated[i] = true; Emit(SimEventKind.LavaReheating, i); }
                        break;
                    case LavaState.Hot: S.LCoolStart[i] = -1; BecomeHot(i); break;
                }
            }
            for (int i = 0; i < D.InfraIds.Length; i++) if (D.InfraProducerStore[i] >= 0) ProduceCoolant(i);
            ShelterPulse();
        }

        void BecomeHot(int seg)
        {
            S.LState[seg] = (byte)LavaState.Hot;
            S.LHeat[seg] = LavaHotHeat;
            Emit(SimEventKind.LavaArrived, seg);
            // a Hot segment disables unprotected equipment on its cells at once
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                if (S.EqBroken[inst]) continue;
                int sock = S.EqSocket[inst];
                int cell = D.SocketCell[sock];
                if (cell < 0 || cell >= D.CellLavaSeg.Length || D.CellLavaSeg[cell] != seg) continue;
                var tool = (ToolKind)S.EqKind[inst];
                if (tool == ToolKind.Cooler && D.SocketLavaSeg[sock] == seg) continue;
                if (tool == ToolKind.Shelter && S.ShelterRemaining[inst] > 0) continue;
                BreakEquipment(inst);
            }
        }

        /// <summary>One feedstock unit becomes one charge after 100 powered scenario ticks; power/safety loss freezes progress.</summary>
        void ProduceCoolant(int infra)
        {
            int store = D.InfraProducerStore[infra];
            bool running = S.InfraOperational[infra];
            if (S.WorksWip[infra] == 0)
            {
                if (!running || S.WorksFeedstock[infra] <= 0 || S.CoolantStore[store] >= D.CoolantCapacity[store]) return;
                S.WorksFeedstock[infra]--;          // reserve when work starts
                S.WorksWip[infra] = 1;
                S.WorksProgress[infra] = 0;
            }
            if (!running) return;
            if (++S.WorksProgress[infra] < D.InfraProduceTicks[infra]) return;
            S.WorksWip[infra] = 0;
            S.WorksProgress[infra] = 0;
            S.CoolantStore[store]++;                // the charge exists only on completion
        }

        // ----------------------------------------------------------------- coolers
        Reject CoolSegment(int inst)
        {
            if (inst < 0 || inst >= S.C.EquipmentCount || (ToolKind)S.EqKind[inst] != ToolKind.Cooler) return Reject.UnknownTarget;
            if (S.EqBroken[inst]) return Reject.NotIdle;
            int sock = S.EqSocket[inst];
            if (!S.CoolerEnabled[sock]) return Reject.SocketIncompatible;
            int seg = D.SocketLavaSeg[sock];
            if ((LavaState)S.LState[seg] != LavaState.Hot) return Reject.NotIdle; // cannot restart a running cooldown
            if (S.EqCharges[inst] <= 0) return Reject.ResourceEmpty;
            S.EqCharges[inst]--;
            S.CoolantSpent[0]++;
            S.LCoolStart[seg] = S.C.S;
            S.LState[seg] = (byte)LavaState.Cooling; // crossing stays closed until S+50
            Emit(SimEventKind.SegmentCooling, seg, inst);
            return Reject.OK;
        }

        Reject RefillCooler(int inst, int store)
        {
            if (inst < 0 || inst >= S.C.EquipmentCount || (ToolKind)S.EqKind[inst] != ToolKind.Cooler) return Reject.UnknownTarget;
            if (S.EqBroken[inst]) return Reject.NotIdle;
            int sock = S.EqSocket[inst];
            if (store < 0 || store != D.SocketCoolantStore[sock]) return Reject.SocketIncompatible;
            if (S.EqCharges[inst] >= 1) return Reject.NotIdle;          // holds at most one charge
            if (S.CoolantStore[store] <= 0) return Reject.ResourceEmpty;
            S.CoolantStore[store]--;
            S.EqCharges[inst] = 1;
            return Reject.OK;
        }

        /// <summary>Coolant conservation: feedstock + work in progress + stored + loaded in coolers + spent.</summary>
        public long CoolantAccount()
        {
            long n = S.CoolantSpent[0];
            for (int i = 0; i < D.InfraIds.Length; i++) n += S.WorksFeedstock[i] + S.WorksWip[i];
            for (int i = 0; i < D.CoolantIds.Length; i++) n += S.CoolantStore[i];
            for (int inst = 0; inst < S.C.EquipmentCount; inst++) if ((ToolKind)S.EqKind[inst] == ToolKind.Cooler) n += S.EqCharges[inst];
            return n;
        }

        // ----------------------------------------------------------------- shelters
        int ShelterOccupants(int node)
        {
            int n = 0;
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                var st = (ResidentState)S.RState[r];
                if ((st == ResidentState.AtNode || st == ResidentState.Boarding) && S.RNode[r] == node) n++;
            }
            return n;
        }

        void ShelterPulse()
        {
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                _shelterProtected[inst] = false;
                if ((ToolKind)S.EqKind[inst] != ToolKind.Shelter || S.EqBroken[inst]) continue;
                int sock = S.EqSocket[inst];
                int node = D.SocketNode[sock];
                bool occupied = ShelterOccupants(node) > 0;
                bool hazardous = !NodeSafe(node) || ShelterRegionHazardous(sock);
                int before = S.C.ShelterExhaustions;
                _shelterProtected[inst] = ShelterRules.Pulse(ref S.ShelterRemaining[inst], occupied, hazardous, ref S.C.ShelterExhaustions);
                if (S.C.ShelterExhaustions != before) Emit(SimEventKind.ShelterExhausted, inst);
            }
        }

        bool ShelterRegionHazardous(int sock)
        {
            int region = D.SocketShelterRegion[sock];
            if (region < 0) return false;
            if (LavaRegionHeat(region) >= LavaReheatHeat) return true;
            if (D.FireEnabled && MaxRegionHeat(region) >= FireDangerHeat) return true;
            return false;
        }

        /// <summary>A placed shelter whose protection was granted this interval masks exposure at its node.</summary>
        bool ShelterProtects(int node)
        {
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
                if (_shelterProtected[inst] && (ToolKind)S.EqKind[inst] == ToolKind.Shelter && D.SocketNode[S.EqSocket[inst]] == node) return true;
            return false;
        }

        Reject ValidateShelterMove(int inst) =>
            ShelterOccupants(D.SocketNode[S.EqSocket[inst]]) > 0 ? Reject.NotIdle : Reject.OK;

        // ----------------------------------------------------------------- hooks
        bool LavaEdgeOpen(int e)
        {
            foreach (int seg in D.EdgeLavaSegs[e]) if (!LavaRules.CrossingOpen((LavaState)S.LState[seg])) return false;
            return true;
        }

        bool LavaNodeSafe(int node)
        {
            foreach (int seg in D.NodeLavaSegs[node]) if (S.LHeat[seg] >= LavaReheatHeat) return false;
            int cell = D.NodeCell[node];
            if (cell >= 0 && cell < D.CellLavaSeg.Length && D.CellLavaSeg[cell] >= 0 && S.LHeat[D.CellLavaSeg[cell]] >= LavaReheatHeat) return false;
            for (int sock = 0; sock < D.SocketIds.Length; sock++)
                if (S.ShelterFront[sock] && D.SocketNode[sock] == node) return false; // the shelter's external heat front
            return true;
        }

        public int CellLavaHeat(int cell) =>
            cell >= 0 && cell < D.CellLavaSeg.Length && D.CellLavaSeg[cell] >= 0 ? S.LHeat[D.CellLavaSeg[cell]] : 0;

        long LavaRegionHeat(int region)
        {
            int max = 0;
            foreach (int c in D.RegionCells[region]) max = Math.Max(max, CellLavaHeat(c));
            return max;
        }

        bool LavaMilestone(MilestoneDef m)
        {
            foreach (int seg in m.Segments) if (S.LReheated[seg]) return true;
            return false;
        }
    }
}
