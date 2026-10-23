using System;

namespace Resq.Sim
{
    public sealed partial class Simulator
    {
        public const int TransferTicks = 10;
        public const int ExposureLimitPulses = 20;

        void AllocateTransportScratch() { }

        // ----------------------------------------------------------------- vehicles
        void ResetVehicleSlot(int v)
        {
            S.VNode[v] = -1; S.VFrom[v] = -1; S.VEdge[v] = -1; S.VInstance[v] = -1;
            S.VPickupGroup[v] = -1; S.VPickupNode[v] = -1; S.VDestination[v] = -1;
            S.VTransferResident[v] = -1; S.VTransferProgress[v] = 0; S.VOccupantCount[v] = 0;
            S.VPhase[v] = (byte)VehiclePhase.None; S.VState[v] = (byte)VehicleState.Idle;
            S.VBerthFrom[v] = -1; S.VBerthTo[v] = -1; S.VStandOff[v] = false;
        }

        int SpawnVehicle(VehicleKind kind, int node, int seats, int slots, int instance)
        {
            int v = S.C.VehicleCount++;
            ResetVehicleSlot(v);
            S.VKind[v] = (byte)kind;
            S.VSeats[v] = Math.Min(seats, S.SeatsMax);
            S.VSlots[v] = slots;
            S.VNode[v] = node;
            S.VInstance[v] = instance;
            if (UsesBerths(v) && (D.NodeFlags[node] & NodeFlags.Dock) != 0) S.NodeBerthUsed[node]++;
            return v;
        }

        bool UsesBerths(int v) => (VehicleKind)S.VKind[v] == VehicleKind.Skiff;

        public static Traversal VehicleClass(VehicleKind k) =>
            k == VehicleKind.Skiff ? Traversal.Water : k == VehicleKind.Winch ? Traversal.Cable : Traversal.Ground;

        Reject AssignVehicleRoute(int v, int pickupGroup, int pickupNode, int destination, bool evacuate)
        {
            if (v < 0 || v >= S.C.VehicleCount) return Reject.UnknownTarget;
            if (S.VState[v] == (byte)VehicleState.Failed) return Reject.NotIdle;
            if (destination < 0 || destination >= D.NodeIds.Length) return Reject.UnknownTarget;
            if (evacuate && (D.NodeFlags[destination] & NodeFlags.Evacuation) == 0) return Reject.UnknownTarget;
            if (pickupGroup >= D.GroupIds.Length || pickupNode >= D.NodeIds.Length) return Reject.UnknownTarget;
            if (pickupGroup >= 0 && pickupNode < 0) return Reject.UnknownTarget;
            ApplyVehicleRoute(v, pickupGroup, pickupNode, destination, evacuate);
            return Reject.OK;
        }

        void ApplyVehicleRoute(int v, int pickupGroup, int pickupNode, int destination, bool evacuate)
        {
            S.VPickupGroup[v] = pickupGroup;
            S.VPickupNode[v] = pickupNode;
            S.VDestination[v] = destination;
            S.VEvacuate[v] = evacuate;
            if (S.VState[v] != (byte)VehicleState.Moving)
                S.VPhase[v] = (byte)(pickupGroup >= 0 && S.VOccupantCount[v] == 0 ? VehiclePhase.ToPickup : VehiclePhase.ToDestination);
            else
                S.VPhase[v] = (byte)(pickupGroup >= 0 && S.VOccupantCount[v] == 0 ? VehiclePhase.ToPickup : VehiclePhase.ToDestination);
            // The pickup group walks to the pickup point unless it already has its own evacuation order.
            if (pickupGroup >= 0 && !(S.GroupTarget[pickupGroup] >= 0 && S.GroupEvacuate[pickupGroup]))
            {
                S.GroupTarget[pickupGroup] = pickupNode;
                S.GroupEvacuate[pickupGroup] = false;
            }
        }

        Reject AssignGroupRoute(int g, int node, bool evacuate)
        {
            if (g < 0 || g >= D.GroupIds.Length) return Reject.UnknownTarget;
            if (node < 0 || node >= D.NodeIds.Length) return Reject.UnknownTarget;
            if (evacuate && (D.NodeFlags[node] & NodeFlags.Evacuation) == 0) return Reject.UnknownTarget;
            S.GroupTarget[g] = node;
            S.GroupEvacuate[g] = evacuate;
            foreach (int r in D.GroupResidents[g]) S.RStay[r] = false; // a new group order moves everyone again
            return Reject.OK;
        }

        // ----------------------------------------------------------------- edges
        public long MaxEdgeDepth(int e)
        {
            long max = 0;
            var cells = D.EdgeSampleCells[e];
            if (cells != null) foreach (int c in cells) max = Math.Max(max, Depth(c));
            return max;
        }

        long MinEdgeDepth(int e)
        {
            long min = long.MaxValue;
            var cells = D.EdgeSampleCells[e];
            if (cells == null || cells.Length == 0) return 0;
            foreach (int c in cells) min = Math.Min(min, Depth(c));
            return min;
        }

        void DeriveEdgeAvailability()
        {
            for (int e = 0; e < D.EdgeIds.Length; e++)
            {
                bool open = S.EdgeAdmin[e];
                int sock = D.EdgeRequiredSocket[e];
                if (open && sock >= 0)
                {
                    int inst = S.SocketInstance[sock];
                    open = inst >= 0 && !S.EqBroken[inst];
                }
                if (open)
                {
                    if (D.EdgeClass[e] == Traversal.Water)
                        open = MinEdgeDepth(e) >= D.EdgeMinDepth[e] && MaxEdgeDepth(e) <= D.EdgeMaxDepth[e];
                    else
                        open = MaxEdgeDepth(e) <= D.EdgeMaxDepth[e];
                }
                if (open) open = WorldEdgeOpen(e);
                S.EdgeOpen[e] = open;
            }
        }

        public bool NodeSafe(int node) => NodeDepth(node) <= D.NodeSafeDepth[node] && WorldNodeSafe(node);

        public bool DockUsable(int node)
        {
            if ((D.NodeFlags[node] & NodeFlags.Dock) != 0)
            {
                long depth = Depth(D.NodeWaterCell[node]);
                if (depth < D.NodeDockMinDepth[node] || depth > D.NodeDockMaxDepth[node]) return false;
            }
            if (!NodeSafe(node)) return false;
            int infra = D.NodeInfra[node];
            if (infra >= 0 && infra != _evaluatingInfra && !S.InfraPowered[infra]) return false;
            return true;
        }

        int _evaluatingInfra = -1;

        int EdgeDuration(int e)
        {
            int ticks = D.EdgeTicks[e];
            int cur = S.EdgeCurrent[e] <= 0 ? 1000 : S.EdgeCurrent[e];
            return (int)IntMath.CeilDiv((long)ticks * 1000, cur);
        }

        // ----------------------------------------------------------------- shortest path
        /// <summary>
        /// Dijkstra over open edges of one traversal class. Minimises (ticks, edge count, edge index sequence).
        /// Returns the first edge from <paramref name="from"/> towards <paramref name="to"/>, or -1.
        /// </summary>
        public int NextEdge(int from, int to, Traversal cls, bool stepFree, bool requireOpen = true)
        {
            if (from == to) return -1;
            int n = D.NodeIds.Length;
            for (int i = 0; i < n; i++) { _pathDist[i] = int.MaxValue; _pathPrevEdge[i] = -1; _pathEdgeCount[i] = 0; _pathDone[i] = false; }
            _pathDist[from] = 0;
            while (true)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                {
                    if (_pathDone[i] || _pathDist[i] == int.MaxValue) continue;
                    if (u < 0 || _pathDist[i] < _pathDist[u] ||
                        (_pathDist[i] == _pathDist[u] && _pathEdgeCount[i] < _pathEdgeCount[u])) u = i;
                }
                if (u < 0) return -1;
                if (u == to) break;
                _pathDone[u] = true;
                foreach (int e in D.NodeOutEdges[u])
                {
                    if (D.EdgeClass[e] != cls) continue;
                    if (stepFree && !D.EdgeStepFree[e]) continue;
                    if (requireOpen && !S.EdgeOpen[e]) continue;
                    int w = D.EdgeTo[e] == u ? D.EdgeFrom[e] : D.EdgeTo[e];
                    if (_pathDone[w]) continue;
                    int nd = _pathDist[u] + D.EdgeTicks[e];
                    int nc = _pathEdgeCount[u] + 1;
                    if (nd < _pathDist[w] || (nd == _pathDist[w] && (nc < _pathEdgeCount[w] ||
                        (nc == _pathEdgeCount[w] && FirstEdge(u, e) < FirstEdge(w, _pathPrevEdge[w])))))
                    {
                        _pathDist[w] = nd; _pathEdgeCount[w] = nc; _pathPrevEdge[w] = e;
                    }
                }
            }
            // walk back to find the first edge
            int node = to, first = -1;
            while (node != from)
            {
                int e = _pathPrevEdge[node];
                if (e < 0) return -1;
                first = e;
                node = D.EdgeTo[e] == node ? D.EdgeFrom[e] : D.EdgeTo[e];
            }
            return first;
        }

        int FirstEdge(int node, int viaEdge) => viaEdge < 0 ? int.MaxValue : viaEdge;

        public bool Reachable(int from, int to, Traversal cls, bool stepFree, bool requireOpen = true) =>
            from == to || NextEdge(from, to, cls, stepFree, requireOpen) >= 0;

        static int Other(MissionDef d, int e, int node) => d.EdgeTo[e] == node ? d.EdgeFrom[e] : d.EdgeTo[e];

        // ----------------------------------------------------------------- admission (spec 02 §6)
        /// <summary>Scenario tick at which a traveller entering edge <paramref name="e"/> now would arrive.</summary>
        long PredictedArrivalS(int e)
        {
            if (!S.C.ScenarioEnabled) return S.C.S;
            int numerator = S.C.Profile == RuleProfile.Relaxed ? 1 : 2;
            return S.C.S + (S.C.Accumulator + (long)EdgeDuration(e) * numerator) / 2;
        }

        /// <summary>
        /// A hard closure already known to the player that lands on this edge before the traveller would arrive:
        /// a warned administrative closure, a published lava arrival, a known crust expiry, or a warned/started runout.
        /// Predicted depth/heat changes are not "known" closures; transit recovery handles them.
        /// </summary>
        bool KnownClosureBeforeArrival(int e)
        {
            long now = S.C.S, arrive = PredictedArrivalS(e);
            if (arrive <= now) return false;
            for (int i = 0; i < D.Events.Length; i++)
            {
                if (!S.Warned[i] || S.Applied[i]) continue;
                var ev = D.Events[i];
                if (ev.AtTick > arrive) continue;
                foreach (var op in ev.Ops)
                {
                    if (op.Kind == EventOpKind.SetEdgeEnabled && op.Value == 0 && Has(op.Targets, e)) return true;
                    if (op.Kind == EventOpKind.ReleaseAvalanche)
                        foreach (int run in op.Targets)
                            if (RunoutHitsEdgeBy(run, ev.AtTick, e, now, arrive)) return true;
                }
            }
            for (int run = 0; run < D.RunoutIds.Length; run++)
                if (S.RunStarted[run] && RunoutHitsEdgeBy(run, S.SegStart[D.RunoutFirstSegment[run]], e, now, arrive)) return true;
            if (D.EdgeLavaSegs != null)
                foreach (int seg in D.EdgeLavaSegs[e])
                {
                    var st = (Rules.LavaState)S.LState[seg];
                    if (st == Rules.LavaState.Dormant && S.LPlanned[seg] > now && S.LPlanned[seg] <= arrive) return true;
                    if ((st == Rules.LavaState.Cooling || st == Rules.LavaState.Crusted) && S.LCoolStart[seg] >= 0)
                    {
                        long reheat = S.LCoolStart[seg] + Rules.LavaRules.CoolingTicks + Rules.LavaRules.CrustTicks;
                        if (reheat > now && reheat <= arrive) return true;
                    }
                }
            return false;
        }

        bool RunoutHitsEdgeBy(int run, long runStart, int e, long now, long arrive)
        {
            foreach (int seg in D.EdgeSegments[e])
            {
                if (D.SegmentRunout[seg] != run || D.SegmentOffset[seg] < 0) continue;
                long t = runStart + D.SegmentOffset[seg];
                if (t > now && t <= arrive) return true;
            }
            return false;
        }

        bool NodeHasRoom(int node)
        {
            int cap = D.NodeCapacity[node];
            return cap <= 0 || S.NodeOccupancy[node] + S.NodeReserved[node] < cap;
        }

        // ----------------------------------------------------------------- per-step movement (stage 7)
        void TransportStep()
        {
            // existing movement first (vehicles, then residents), arrivals complete this stage
            for (int v = 0; v < S.C.VehicleCount; v++) AdvanceVehicleMovement(v);
            for (int r = 0; r < D.ResidentIds.Length; r++) AdvanceResidentMovement(r);
            // transfers and new admissions
            for (int v = 0; v < S.C.VehicleCount; v++) VehicleLogic(v);
            for (int r = 0; r < D.ResidentIds.Length; r++) ResidentLogic(r);
            for (int n = 0; n < D.NodeIds.Length; n++)
                if (S.NodeOccupancy[n] > S.NodePeak[n]) S.NodePeak[n] = S.NodeOccupancy[n];
            UpdateDwell();
            TutorialProgress();
        }

        void AdvanceResidentMovement(int r)
        {
            if (S.RState[r] != (byte)ResidentState.Walking) return;
            int e = S.REdge[r];
            int dest = S.RNode[r], origin = S.RFrom[r];
            if (!S.EdgeOpen[e] && !S.RReversing[r])
            {
                // closure while in transit (spec 02 §6)
                if (NodeSafe(dest)) { /* bounded emergency egress: keep going */ }
                else if (NodeSafe(origin))
                {
                    S.RReversing[r] = true;
                    S.RDuration[r] = S.RProgress[r];
                    S.RProgress[r] = 0;
                    S.NodeReserved[dest]--;
                    S.RNode[r] = origin;
                    S.RFrom[r] = dest;
                    Emit(SimEventKind.RouteBlocked, r, e);
                    return;
                }
                else
                {
                    S.EdgeOccupancy[e]--;
                    S.NodeReserved[dest]--;
                    S.NodeReserved[origin]--;
                    SetBackup(r);
                    return;
                }
            }
            S.RProgress[r]++;
            if (S.RProgress[r] < S.RDuration[r]) return;
            // arrive
            S.EdgeOccupancy[e]--;
            if (S.RReversing[r]) { S.NodeReserved[S.RNode[r]]--; }
            else { S.NodeReserved[dest]--; S.NodeReserved[origin]--; S.EdgeTransfers[e]++; }
            int at = S.RNode[r];
            S.RState[r] = (byte)ResidentState.AtNode;
            S.REdge[r] = -1; S.RReversing[r] = false; S.RFrom[r] = -1;
            S.NodeOccupancy[at]++;
            S.RDwellStart[r] = S.C.S;
            int g = D.ResidentGroup[r];
            if (S.GroupEvacuate[g] && S.GroupTarget[g] == at && (D.NodeFlags[at] & NodeFlags.Evacuation) != 0)
                Evacuate(r, at, (int)Traversal.Ground);
        }

        void ResidentLogic(int r)
        {
            if (S.RState[r] != (byte)ResidentState.AtNode) return;
            int g = D.ResidentGroup[r];
            if (S.GroupHeld[g]) return;
            int target = S.GroupTarget[g];
            int at = S.RNode[r];
            if (target < 0 || target == at)
            {
                if (target == at && S.GroupEvacuate[g] && (D.NodeFlags[at] & NodeFlags.Evacuation) != 0)
                    Evacuate(r, at, (int)Traversal.Ground);
                return;
            }
            if (S.RStay[r]) return; // carried part-way: wait for the next vehicle or a new group order
            int e = NextEdge(at, target, Traversal.Ground, D.ResidentStepFree[r]);
            if (e < 0) return;
            if (KnownClosureBeforeArrival(e)) return; // spec 02 §6: wait rather than walk into a published closure
            int to = Other(D, e, at);
            if (S.EdgeOccupancy[e] + 1 > D.EdgeSlots[e]) return;
            if (!NodeHasRoom(to)) return;
            S.EdgeOccupancy[e]++;
            S.NodeOccupancy[at]--;
            S.NodeReserved[to]++;
            S.NodeReserved[at]++; // origin return slot
            S.RState[r] = (byte)ResidentState.Walking;
            S.REdge[r] = e; S.RFrom[r] = at; S.RNode[r] = to;
            S.RProgress[r] = 0; S.RDuration[r] = EdgeDuration(e);
            S.RExposure[r] = 0;
        }

        void Evacuate(int r, int node, int mode)
        {
            if (S.RState[r] == (byte)ResidentState.Evacuated) return;
            if (S.RState[r] == (byte)ResidentState.AtNode) S.NodeOccupancy[node]--;
            S.RState[r] = (byte)ResidentState.Evacuated;
            S.RNode[r] = node;
            S.RFinalMode[r] = mode;
            S.C.Evacuated++;
            Emit(SimEventKind.ResidentEvacuated, r, node, mode);
        }

        void SetBackup(int r)
        {
            if (S.RState[r] == (byte)ResidentState.AwaitingBackup || S.RState[r] == (byte)ResidentState.Evacuated) return;
            S.RState[r] = (byte)ResidentState.AwaitingBackup;
            S.C.AwaitingBackup++;
            Emit(SimEventKind.ResidentBackup, r, S.RNode[r]);
        }

        // ----------------------------------------------------------------- vehicle movement
        void AdvanceVehicleMovement(int v)
        {
            if (S.VState[v] != (byte)VehicleState.Moving) return;
            int e = S.VEdge[v];
            int dest = S.VNode[v], origin = S.VFrom[v];
            if (!S.EdgeOpen[e] && !S.VReversing[v])
            {
                if (VehicleNodeSafe(v, dest)) { }
                else if (VehicleNodeSafe(v, origin))
                {
                    S.VReversing[v] = true;
                    S.VDuration[v] = S.VProgress[v];
                    S.VProgress[v] = 0;
                    S.VNode[v] = origin; S.VFrom[v] = dest;
                    Emit(SimEventKind.RouteBlocked, 1000 + v, e);
                    return;
                }
                else
                {
                    S.EdgeOccupancy[e] -= S.VSlots[v];
                    S.VState[v] = (byte)VehicleState.Failed;
                    for (int k = 0; k < S.VOccupantCount[v]; k++) SetBackup(S.VOccupants[v * S.SeatsMax + k]);
                    return;
                }
            }
            if ((VehicleKind)S.VKind[v] == VehicleKind.Winch && !WinchTravelTick(v)) return;
            S.VProgress[v]++;
            if (S.VProgress[v] < S.VDuration[v]) return;
            S.EdgeOccupancy[e] -= S.VSlots[v];
            int at = S.VNode[v];
            if (!S.VReversing[v]) S.EdgeTransfers[e] += S.VOccupantCount[v];
            // berths: a normal arrival frees the origin berth; a reversal frees the destination reservation instead
            int release = S.VReversing[v] ? S.VBerthTo[v] : S.VBerthFrom[v];
            if (release >= 0) S.NodeBerthUsed[release]--;
            S.VStandOff[v] = UsesBerths(v) && S.VReversing[v] && S.VBerthFrom[v] < 0 && (D.NodeFlags[at] & NodeFlags.Dock) != 0;
            S.VBerthFrom[v] = -1; S.VBerthTo[v] = -1;
            S.VEmergency[v] = false; S.VBrake[v] = 0;
            S.VState[v] = (byte)VehicleState.Idle;
            S.VEdge[v] = -1; S.VReversing[v] = false; S.VFrom[v] = -1;
            for (int k = 0; k < S.VOccupantCount[v]; k++) S.RNode[S.VOccupants[v * S.SeatsMax + k]] = at;
            Emit(SimEventKind.VehicleArrived, v, at);
        }

        bool VehicleNodeSafe(int v, int node) =>
            (VehicleKind)S.VKind[v] == VehicleKind.Skiff ? DockUsable(node) || (D.NodeFlags[node] & NodeFlags.Dock) == 0 : NodeSafe(node);

        bool TryMoveVehicle(int v, int target)
        {
            var cls = VehicleClass((VehicleKind)S.VKind[v]);
            int at = S.VNode[v];
            _berthBlocked = false;
            int e = NextEdge(at, target, cls, false);
            if (e < 0) return false;
            if (KnownClosureBeforeArrival(e)) return false;
            if (S.EdgeOccupancy[e] + S.VSlots[v] > D.EdgeSlots[e]) return false;
            bool winch = (VehicleKind)S.VKind[v] == VehicleKind.Winch;
            if (winch && !WinchCanDepart(v)) return false;
            int to = Other(D, e, at);
            if (UsesBerths(v) && (D.NodeFlags[to] & NodeFlags.Dock) != 0 && to == target && S.NodeBerthUsed[to] >= Math.Max(1, D.NodeBerths[to]))
            {
                _berthBlocked = true;
                return false;
            }
            S.EdgeOccupancy[e] += S.VSlots[v];
            // reserve the destination berth now, so two skiffs can never both land at a one-berth dock;
            // keep the origin berth until arrival, so a turned-back skiff always has its berth to return to
            S.VBerthFrom[v] = UsesBerths(v) && (D.NodeFlags[at] & NodeFlags.Dock) != 0 && !S.VStandOff[v] ? at : -1;
            S.VBerthTo[v] = -1;
            S.VStandOff[v] = false;
            if (UsesBerths(v) && (D.NodeFlags[to] & NodeFlags.Dock) != 0) { S.NodeBerthUsed[to]++; S.VBerthTo[v] = to; }
            S.VState[v] = (byte)VehicleState.Moving;
            S.VEdge[v] = e; S.VFrom[v] = at; S.VNode[v] = to;
            S.VProgress[v] = 0; S.VDuration[v] = EdgeDuration(e);
            if (winch) { S.VRest[v] = 0; S.VBrake[v] = 0; }
            return true;
        }

        void VehicleLogic(int v)
        {
            var st = (VehicleState)S.VState[v];
            if (st == VehicleState.Moving || st == VehicleState.Failed) return;
            if ((VehicleKind)S.VKind[v] == VehicleKind.Winch) WinchRest(v);
            var phase = (VehiclePhase)S.VPhase[v];
            int at = S.VNode[v];
            // a finished skiff moors aside: it no longer blocks the berth for loaded boats behind it
            if (phase == VehiclePhase.Done && UsesBerths(v) && !S.VStandOff[v] && (D.NodeFlags[at] & NodeFlags.Dock) != 0)
            {
                S.NodeBerthUsed[at]--;
                S.VStandOff[v] = true;
            }
            // spec 03: cumulative ticks a loaded vehicle waits while its chosen unload dock is closed
            if (S.VOccupantCount[v] > 0 && S.VDestination[v] >= 0 && !DockUsable(S.VDestination[v])) S.C.LoadedClosedDockWait++;
            switch (phase)
            {
                case VehiclePhase.ToPickup:
                    if (at == S.VPickupNode[v] && !ReclaimBerth(v, at)) { S.VState[v] = (byte)VehicleState.Waiting; break; }
                    if (at == S.VPickupNode[v]) { S.VPhase[v] = (byte)VehiclePhase.Loading; goto case VehiclePhase.Loading; }
                    if (!TryMoveVehicle(v, S.VPickupNode[v])) { S.VState[v] = (byte)VehicleState.Waiting; QueueTick(v, S.VPickupNode[v]); }
                    else S.VQueue[v] = 0;
                    break;
                case VehiclePhase.Loading:
                    LoadingTick(v);
                    break;
                case VehiclePhase.ToDestination:
                    if (at == S.VDestination[v] && !ReclaimBerth(v, at)) { S.VState[v] = (byte)VehicleState.Waiting; break; }
                    if (at == S.VDestination[v]) { S.VPhase[v] = (byte)VehiclePhase.Unloading; goto case VehiclePhase.Unloading; }
                    if (!TryMoveVehicle(v, S.VDestination[v]))
                    {
                        QueueTick(v, S.VDestination[v]);
                        S.VState[v] = (byte)VehicleState.Waiting;
                    }
                    else S.VQueue[v] = 0;
                    break;
                case VehiclePhase.Unloading:
                    UnloadingTick(v);
                    break;
            }
        }

        bool _berthBlocked;

        /// <summary>A moored-aside skiff must take the berth back before it can load or unload at this dock.</summary>
        bool ReclaimBerth(int v, int at)
        {
            if (!S.VStandOff[v]) return true;
            if (S.NodeBerthUsed[at] >= Math.Max(1, D.NodeBerths[at]))
            {
                _berthBlocked = true;
                QueueTick(v, at);
                return false;
            }
            S.NodeBerthUsed[at]++;
            S.VStandOff[v] = false;
            S.VQueue[v] = 0;
            return true;
        }

        /// <summary>Dock queue metric: consecutive operation ticks a vehicle waits because its target dock's berth is taken.</summary>
        void QueueTick(int v, int target)
        {
            if (!_berthBlocked) { S.VQueue[v] = 0; return; }
            S.VQueue[v]++;
            if (S.VQueue[v] > S.NodeMaxQueue[target]) S.NodeMaxQueue[target] = S.VQueue[v];
        }

        /// <summary>
        /// Members this vehicle should still wait for: anyone not yet aboard or terminal, plus members riding another
        /// vehicle towards this vehicle's pickup (the hand-off leg of a relay).
        /// </summary>
        bool GroupHasPendingMembers(int g, int v)
        {
            int pickup = S.VPickupNode[v];
            if (GroupHasUnboardedMembers(g, pickup)) return true;
            if (pickup < 0) return false;
            foreach (int r in D.GroupResidents[g])
            {
                var st = (ResidentState)S.RState[r];
                if (st != ResidentState.Aboard && st != ResidentState.Unboarding) continue;
                int u = S.RVehicle[r];
                if (u >= 0 && u != v && S.VDestination[u] == pickup) return true;
            }
            return false;
        }

        /// <summary>Members not yet aboard or terminal. A member moored elsewhere awaits a new order, not this pickup.</summary>
        bool GroupHasUnboardedMembers(int g, int pickup)
        {
            foreach (int r in D.GroupResidents[g])
            {
                var st = (ResidentState)S.RState[r];
                if (st == ResidentState.AtNode && S.RStay[r] && S.RNode[r] != pickup) continue;
                if (st == ResidentState.AtNode || st == ResidentState.Walking || st == ResidentState.Boarding) return true;
            }
            return false;
        }

        bool GroupMemberWaitingAt(int g, int node, out int resident)
        {
            resident = -1;
            foreach (int r in D.GroupResidents[g])
            {
                if (S.RState[r] == (byte)ResidentState.AtNode && S.RNode[r] == node) { resident = r; return true; }
            }
            return false;
        }

        bool GroupMemberHeadingTo(int g, int node)
        {
            if (S.GroupTarget[g] != node || S.GroupHeld[g]) return false;
            foreach (int r in D.GroupResidents[g])
            {
                var st = (ResidentState)S.RState[r];
                if (st == ResidentState.Walking || (st == ResidentState.AtNode && S.RNode[r] != node && !S.RStay[r])) return true;
            }
            return false;
        }

        void LoadingTick(int v)
        {
            int at = S.VNode[v];
            int g = S.VPickupGroup[v];
            S.VState[v] = (byte)VehicleState.Loading;
            int tr = S.VTransferResident[v];
            if (tr >= 0)
            {
                if (!DockUsable(at))
                {
                    // dock closed: cancel the incomplete transfer, resident stays at its valid location
                    S.RState[tr] = (byte)ResidentState.AtNode;
                    S.VTransferResident[v] = -1; S.VTransferProgress[v] = 0;
                    Emit(SimEventKind.TransferCancelled, tr, v, at);
                    return;
                }
                S.VTransferProgress[v]++;
                if (S.VTransferProgress[v] >= TransferTicks)
                {
                    S.NodeOccupancy[at]--;
                    S.RState[tr] = (byte)ResidentState.Aboard;
                    S.RVehicle[tr] = v;
                    S.VOccupants[v * S.SeatsMax + S.VOccupantCount[v]++] = tr;
                    S.RBoarded[tr] = true;
                    S.RStay[tr] = false;
                    S.VTransferResident[v] = -1; S.VTransferProgress[v] = 0;
                    Emit(SimEventKind.TransferCompleted, tr, v, at);
                }
                return;
            }
            // a winch at its duty limit rests (empty) before anyone boards
            if ((VehicleKind)S.VKind[v] == VehicleKind.Winch && S.VOccupantCount[v] == 0 && S.VDuty[v] >= WinchDutyTicks) return;
            bool full = S.VOccupantCount[v] >= S.VSeats[v];
            if (!full && g >= 0 && DockUsable(at) && GroupMemberWaitingAt(g, at, out int next))
            {
                S.RState[next] = (byte)ResidentState.Boarding;
                S.VTransferResident[v] = next; S.VTransferProgress[v] = 0;
                Emit(SimEventKind.TransferStarted, next, v, at);
                return;
            }
            bool moreComing = !full && g >= 0 && GroupMemberHeadingTo(g, at);
            if (S.VOccupantCount[v] > 0 && !moreComing)
            {
                S.VPhase[v] = (byte)VehiclePhase.ToDestination;
                VehicleLogic(v);
                return;
            }
            if (S.VOccupantCount[v] == 0 && g >= 0 && !GroupHasPendingMembers(g, v))
                S.VPhase[v] = (byte)VehiclePhase.Done;
        }

        void UnloadingTick(int v)
        {
            int at = S.VNode[v];
            S.VState[v] = (byte)VehicleState.Unloading;
            int tr = S.VTransferResident[v];
            if (tr >= 0)
            {
                if (!DockUsable(at))
                {
                    S.VTransferResident[v] = -1; S.VTransferProgress[v] = 0;
                    S.RState[tr] = (byte)ResidentState.Aboard;
                    Emit(SimEventKind.TransferCancelled, tr, v, at);
                    return;
                }
                S.VTransferProgress[v]++;
                if (S.VTransferProgress[v] >= TransferTicks)
                {
                    RemoveOccupant(v, tr);
                    S.RVehicle[tr] = -1;
                    S.VTransferResident[v] = -1; S.VTransferProgress[v] = 0;
                    var cls = VehicleClass((VehicleKind)S.VKind[v]);
                    if (S.VEvacuate[v] && (D.NodeFlags[at] & NodeFlags.Evacuation) != 0)
                    {
                        S.RState[tr] = (byte)ResidentState.AtNode; // transient so Evacuate() balances occupancy
                        S.NodeOccupancy[at]++;
                        Evacuate(tr, at, (int)cls);
                    }
                    else
                    {
                        S.RState[tr] = (byte)ResidentState.AtNode;
                        S.RNode[tr] = at;
                        S.RDwellStart[tr] = S.C.S;
                        S.NodeOccupancy[at]++;
                        S.RStay[tr] = true; // only this resident stops; group members still walking keep their order
                    }
                    Emit(SimEventKind.TransferCompleted, tr, v, at);
                }
                return;
            }
            if (S.VOccupantCount[v] > 0)
            {
                if (!DockUsable(at)) return;
                if (!NodeHasRoom(at))
                {
                    // waiting at the dock for receiving capacity counts towards the dock queue metric
                    S.VQueue[v]++;
                    if (S.VQueue[v] > S.NodeMaxQueue[at]) S.NodeMaxQueue[at] = S.VQueue[v];
                    return;
                }
                S.VQueue[v] = 0;
                int next = S.VOccupants[v * S.SeatsMax];
                S.RState[next] = (byte)ResidentState.Unboarding;
                S.VTransferResident[v] = next; S.VTransferProgress[v] = 0;
                Emit(SimEventKind.TransferStarted, next, v, at);
                return;
            }
            int g = S.VPickupGroup[v];
            S.VPhase[v] = (byte)(g >= 0 && GroupHasPendingMembers(g, v) ? VehiclePhase.ToPickup : VehiclePhase.Done);
            S.VState[v] = (byte)VehicleState.Idle;
        }

        void RemoveOccupant(int v, int r)
        {
            int baseIdx = v * S.SeatsMax;
            int n = S.VOccupantCount[v];
            for (int k = 0; k < n; k++)
            {
                if (S.VOccupants[baseIdx + k] != r) continue;
                for (int j = k; j < n - 1; j++) S.VOccupants[baseIdx + j] = S.VOccupants[baseIdx + j + 1];
                S.VOccupantCount[v] = n - 1;
                return;
            }
        }

        // ----------------------------------------------------------------- exposure (stage 8, hazard pulses)
        void ExposureStep()
        {
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                var st = (ResidentState)S.RState[r];
                bool hazardous;
                switch (st)
                {
                    case ResidentState.AtNode:
                    case ResidentState.Boarding:
                        hazardous = !NodeSafe(S.RNode[r]) && !ShelterProtects(S.RNode[r]);
                        break;
                    case ResidentState.Aboard:
                    case ResidentState.Unboarding:
                    {
                        int v = S.RVehicle[r];
                        hazardous = v >= 0 && S.VState[v] != (byte)VehicleState.Moving &&
                                    (VehicleKind)S.VKind[v] != VehicleKind.Skiff && !NodeSafe(S.VNode[v]);
                        break;
                    }
                    default:
                        hazardous = false;
                        break;
                }
                if (!hazardous) { S.RExposure[r] = 0; continue; }
                if (++S.RExposure[r] >= ExposureLimitPulses)
                {
                    if (st == ResidentState.AtNode || st == ResidentState.Boarding) S.NodeOccupancy[S.RNode[r]]--;
                    SetBackup(r);
                }
            }
        }

        public bool BoardedOnce(int r) => S.RBoarded[r];

        // ----------------------------------------------------------------- guided first play
        void TutorialProgress()
        {
            if (S.C.TutorialStep <= 0 || S.C.ScenarioEnabled) return;
            int bus = D.TutorialGroup;
            if (bus < 0) return;
            bool delivered = true;
            foreach (int r in D.GroupResidents[bus])
                if (S.RState[r] != (byte)ResidentState.Evacuated) { delivered = false; break; }
            if (delivered && S.C.TutorialStep >= 2) EnableScenarioClock();
        }
    }
}
