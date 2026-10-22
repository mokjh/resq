using System;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    /// <summary>
    /// World 3 structure stability, authored avalanche runout and winch duty (spec 02 §5, §8). Support loss runs on hazard
    /// pulses from pre-movement resident locations; a collapse can start its bound runout in the same pulse.
    /// </summary>
    public sealed partial class Simulator
    {
        public const int SupportWarnStability = 300;
        public const int SupportClearStability = 400;
        public const int CollapseWarningTicks = 100;
        public const int WinchDutyTicks = 100;
        public const int WinchRestTicks = 30;
        public const int WinchBrakeTicks = 20;
        public const int WinchSeats = 2;

        void InitStability()
        {
            for (int i = 0; i < D.SupportIds.Length; i++)
            {
                S.SupBase[i] = D.SupportBase0[i];
                S.SupStatic[i] = D.SupportStatic0[i];
                S.SupWarnTick[i] = -1;
            }
            for (int i = 0; i < D.SegmentIds.Length; i++) S.SegStart[i] = -1;
        }

        // ----------------------------------------------------------------- supports
        public int IntactBraces(int support)
        {
            int n = 0;
            for (int sock = 0; sock < D.SocketIds.Length; sock++)
            {
                if (D.SocketKind[sock] != ToolKind.Brace || D.SocketSupport[sock] != support) continue;
                int inst = S.SocketInstance[sock];
                if (inst >= 0 && !S.EqBroken[inst]) n++;
            }
            return n;
        }

        public int EffectiveStability(int support) =>
            StabilityRules.Effective(S.SupBase[support], S.SupStatic[support], IntactBraces(support));

        public bool SupportCollapsed(int support) => S.SupCollapsed[support];

        static bool Has(int[] arr, int x)
        {
            for (int i = 0; i < arr.Length; i++) if (arr[i] == x) return true;
            return false;
        }

        /// <summary>Residents on the support's declared nodes/edges, including vehicle occupants (pre-movement locations).</summary>
        int ResidentLoad(int support)
        {
            var nodes = D.SupportLoadNodes[support];
            var edges = D.SupportLoadEdges[support];
            int n = 0;
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                switch ((ResidentState)S.RState[r])
                {
                    case ResidentState.AtNode:
                    case ResidentState.Boarding:
                        if (Has(nodes, S.RNode[r])) n++;
                        break;
                    case ResidentState.Walking:
                        if (Has(edges, S.REdge[r])) n++;
                        break;
                    case ResidentState.Aboard:
                    case ResidentState.Unboarding:
                    {
                        int v = S.RVehicle[r];
                        if (v < 0) break;
                        if (S.VState[v] == (byte)VehicleState.Moving ? Has(edges, S.VEdge[v]) : Has(nodes, S.VNode[v])) n++;
                        break;
                    }
                }
            }
            return n;
        }

        void StabilityPulse()
        {
            long s = S.C.S;
            for (int i = 0; i < D.SupportIds.Length; i++)
            {
                if (S.SupCollapsed[i]) continue;
                int loss = StabilityRules.PulseLoss(S.SupLoss[i], S.SupStatic[i], ResidentLoad(i), ref S.SupRem[i]);
                S.SupBase[i] = Math.Max(-1000, S.SupBase[i] - loss);
                int eff = EffectiveStability(i);
                if (eff <= SupportWarnStability && S.SupWarnTick[i] < 0)
                {
                    S.SupWarnTick[i] = s;
                    S.SupWarnEmergent[i] = true;
                    Emit(SimEventKind.SupportWarning, i, eff);
                }
                else if (eff > SupportClearStability && !S.SupArmed[i] && S.SupWarnEmergent[i])
                {
                    // an untriggered emergent warning clears; scheduled warnings keep their published stamp
                    S.SupWarnTick[i] = -1;
                    S.SupWarnEmergent[i] = false;
                }
                if (eff == 0)
                {
                    S.SupArmed[i] = true;
                    if (S.SupWarnTick[i] >= 0 && s - S.SupWarnTick[i] >= CollapseWarningTicks) Collapse(i);
                }
                else if (S.SupArmed[i]) S.SupArmed[i] = false; // restored above zero before the boundary: cancelled
            }
        }

        void Collapse(int support)
        {
            S.SupCollapsed[support] = true;
            S.SupArmed[support] = false;
            Emit(SimEventKind.SupportCollapsed, support);
            for (int sock = 0; sock < D.SocketIds.Length; sock++)
            {
                if (D.SocketSupport[sock] != support) continue;
                int inst = S.SocketInstance[sock];
                if (inst >= 0) BreakEquipment(inst);
            }
            for (int r = 0; r < D.RunoutIds.Length; r++)
                if (D.RunoutSourceSupport[r] == support) StartRunout(r);
        }

        void BreakEquipment(int inst)
        {
            if (S.EqBroken[inst]) return;
            S.EqBroken[inst] = true;
            S.EqActive[inst] = false;
            S.C.Losses++;
            Emit(SimEventKind.EquipmentBroken, inst, S.EqSocket[inst]);
        }

        /// <summary>Published warnings of events that act on a support are relevant collapse warnings.</summary>
        void StabilityOnWarning(EventDef ev)
        {
            foreach (var op in ev.Ops)
            {
                if (op.Kind != EventOpKind.SetStabilityLoss && op.Kind != EventOpKind.SetSupportLoad &&
                    op.Kind != EventOpKind.Tremor && op.Kind != EventOpKind.WarnSupport) continue;
                foreach (int t in op.Targets) StampSupportWarning(t);
            }
        }

        void StampSupportWarning(int support)
        {
            if (S.SupWarnTick[support] >= 0 && !S.SupWarnEmergent[support]) return;
            if (S.SupWarnTick[support] < 0) S.SupWarnTick[support] = S.C.S;
            S.SupWarnEmergent[support] = false;
        }

        void ApplyStabilityOp(EventOp op)
        {
            foreach (int t in op.Targets)
            {
                switch (op.Kind)
                {
                    case EventOpKind.SetStabilityLoss: S.SupLoss[t] = (int)op.Value; break;
                    case EventOpKind.SetSupportLoad: S.SupStatic[t] = (int)op.Value; break;
                    case EventOpKind.Tremor: if (!S.SupCollapsed[t]) S.SupBase[t] = Math.Max(-1000, S.SupBase[t] - (int)op.Value); break;
                    case EventOpKind.WarnSupport: StampSupportWarning(t); break;
                    case EventOpKind.ReleaseAvalanche: StartRunout(t); break;
                }
            }
        }

        Reject ValidateBrace(int socket) =>
            D.SocketSupport[socket] >= 0 && S.SupCollapsed[D.SocketSupport[socket]] ? Reject.UnsafeTarget : Reject.OK;

        // ----------------------------------------------------------------- runout
        void StartRunout(int runout)
        {
            if (S.RunStarted[runout]) return; // a later duplicate trigger has no effect
            S.RunStarted[runout] = true;
            int first = D.RunoutFirstSegment[runout];
            if (S.SegStart[first] < 0) S.SegStart[first] = S.C.S;
            Emit(SimEventKind.RunoutStarted, runout);
        }

        void RunoutPulse()
        {
            long s = S.C.S;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < D.SegmentIds.Length; i++)
                {
                    long start = S.SegStart[i];
                    if (start < 0 || s < start + D.SegmentDelay[i]) continue;
                    foreach (int nx in D.SegmentNext[i])
                    {
                        long t = start + D.SegmentDelay[i];
                        if (S.SegStart[nx] >= 0 && S.SegStart[nx] <= t) continue;
                        S.SegStart[nx] = t;
                        changed = true;
                    }
                }
            }
        }

        /// <summary>Segment currently occupied by moving or settling debris.</summary>
        public bool SegmentActive(int seg)
        {
            long start = S.SegStart[seg];
            return start >= 0 && S.C.S >= start && S.C.S < start + D.SegmentSettle[seg];
        }

        bool RunoutEdgeOpen(int e)
        {
            foreach (int seg in D.EdgeSegments[e])
            {
                long start = S.SegStart[seg];
                if (start < 0 || S.C.S < start) continue;
                if (SegmentActive(seg) || !D.EdgeDebrisReopenable[e]) return false;
            }
            return true;
        }

        bool RunoutNodeSafe(int n)
        {
            foreach (int seg in D.NodeSegments[n]) if (SegmentActive(seg)) return false;
            return true;
        }

        bool StabilityEdgeOpen(int e)
        {
            for (int i = 0; i < D.SupportIds.Length; i++)
                if (S.SupCollapsed[i] && Has(D.SupportCollapseEdges[i], e)) return false;
            return RunoutEdgeOpen(e);
        }

        bool StabilityNodeSafe(int n)
        {
            for (int i = 0; i < D.SupportIds.Length; i++)
                if (S.SupCollapsed[i] && Has(D.SupportCollapseNodes[i], n)) return false;
            return RunoutNodeSafe(n);
        }

        // ----------------------------------------------------------------- winch
        int VehicleOf(int inst)
        {
            for (int v = 0; v < S.C.VehicleCount; v++) if (S.VInstance[v] == inst) return v;
            return -1;
        }

        bool WinchPowered(int v)
        {
            int inst = S.VInstance[v];
            return inst >= 0 && !S.EqBroken[inst] && S.EqActive[inst] && S.EqPowerGrant[inst] > 0;
        }

        /// <summary>A winch may start a trip only when powered and its duty cycle allows it.</summary>
        bool WinchCanDepart(int v) => WinchPowered(v) && S.VDuty[v] < WinchDutyTicks;

        /// <summary>Rest counts only at a safe, unoccupied endpoint; 30 ticks of rest restore the duty cycle.</summary>
        void WinchRest(int v)
        {
            if (S.VOccupantCount[v] > 0 || S.VTransferResident[v] >= 0 || !NodeSafe(S.VNode[v])) return;
            if (S.VDuty[v] == 0) return;
            if (++S.VRest[v] >= WinchRestTicks) { S.VDuty[v] = 0; S.VRest[v] = 0; }
        }

        /// <summary>
        /// Winch travel for one operation tick. Returns true when the caller should advance progress normally.
        /// Power loss engages the brake for up to 20 ticks; after that a one-time emergency return to the origin.
        /// </summary>
        bool WinchTravelTick(int v)
        {
            if (S.VEmergency[v]) return true; // safety mechanism: 1 progress unit per tick, power cannot reverse it
            if (WinchPowered(v))
            {
                S.VBrake[v] = 0;
                S.VDuty[v]++;
                return true;
            }
            if (++S.VBrake[v] <= WinchBrakeTicks) return false; // braked in place
            int origin = S.VReversing[v] ? S.VNode[v] : S.VFrom[v];
            if (!S.VReversing[v] && NodeSafe(origin))
            {
                S.VReversing[v] = true;
                S.VDuration[v] = S.VProgress[v];
                S.VProgress[v] = 0;
                S.VFrom[v] = S.VNode[v];
                S.VNode[v] = origin;
                S.VEmergency[v] = true;
                Emit(SimEventKind.RouteBlocked, 1000 + v, S.VEdge[v]);
                return false;
            }
            if (S.VReversing[v]) { S.VEmergency[v] = true; return false; }
            // origin unsafe: AwaitingBackup at the current position
            S.EdgeOccupancy[S.VEdge[v]] -= S.VSlots[v];
            S.VState[v] = (byte)VehicleState.Failed;
            for (int k = 0; k < S.VOccupantCount[v]; k++) SetBackup(S.VOccupants[v * S.SeatsMax + k]);
            return false;
        }

        Reject ValidateWinchMove(int inst, int dest)
        {
            int v = VehicleOf(inst);
            if (v < 0) return Reject.OK;
            if (S.VState[v] != (byte)VehicleState.Idle && S.VState[v] != (byte)VehicleState.Waiting) return Reject.NotIdle;
            if (S.VOccupantCount[v] > 0 || S.VTransferResident[v] >= 0) return Reject.NotIdle;
            if (!NodeSafe(D.SocketNode[dest])) return Reject.UnsafeTarget;
            return Reject.OK;
        }

        void MoveWinchCabin(int inst, int dest)
        {
            int v = VehicleOf(inst);
            if (v < 0) return;
            S.VNode[v] = D.SocketNode[dest];
            S.VPhase[v] = (byte)VehiclePhase.None;
            S.VState[v] = (byte)VehicleState.Idle;
        }

        // ----------------------------------------------------------------- metrics
        void UpdateDwell()
        {
            long s = S.C.S;
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                if (S.RState[r] != (byte)ResidentState.AtNode) continue;
                int n = S.RNode[r];
                long dwell = s - S.RDwellStart[r];
                if (dwell > S.NodeMaxDwell[n]) S.NodeMaxDwell[n] = dwell;
            }
        }

        long EdgeTransfersTagged(int tag)
        {
            long n = 0;
            for (int e = 0; e < D.EdgeIds.Length; e++) if (Has(D.EdgeTags[e], tag)) n += S.EdgeTransfers[e];
            return n;
        }
    }
}
