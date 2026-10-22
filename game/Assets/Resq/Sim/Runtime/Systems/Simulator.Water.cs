using System;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    public sealed partial class Simulator
    {
        // scratch (preallocated; never part of state)
        long[] _avail, _newVol, _head;
        long[] _reqAmount, _reqGrant, _reqScratch;
        int[] _reqDest, _reqKind, _reqRef;
        int[][] _nodeLinks;      // links incident to a water node (sorted by link index)
        int[] _pathDist, _pathPrevEdge, _pathEdgeCount;
        bool[] _pathDone;

        void AllocateScratch()
        {
            int wc = D.WaterCount;
            _avail = new long[wc]; _newVol = new long[wc]; _head = new long[wc];
            int maxReq = 64;
            _reqAmount = new long[maxReq]; _reqGrant = new long[maxReq]; _reqScratch = new long[maxReq];
            _reqDest = new int[maxReq]; _reqKind = new int[maxReq]; _reqRef = new int[maxReq];
            var lists = new System.Collections.Generic.List<int>[wc];
            for (int i = 0; i < wc; i++) lists[i] = new System.Collections.Generic.List<int>(4);
            for (int l = 0; l < D.LinkFrom.Length; l++)
            {
                lists[D.LinkFrom[l]].Add(l);
                if (D.LinkTo[l] >= 0) lists[D.LinkTo[l]].Add(l);
            }
            _nodeLinks = new int[wc][];
            for (int i = 0; i < wc; i++) _nodeLinks[i] = lists[i].ToArray();
            int n = D.NodeIds.Length;
            _pathDist = new int[n]; _pathPrevEdge = new int[n]; _pathEdgeCount = new int[n]; _pathDone = new bool[n];
            AllocateTransportScratch();
            AllocateFireScratch();
        }

        bool BarrierIntact(int link)
        {
            int sock = D.LinkBarrierSocket[link];
            if (sock < 0) return false;
            int inst = S.SocketInstance[sock];
            return inst >= 0 && !S.EqBroken[inst] && (ToolKind)S.EqKind[inst] == ToolKind.Barrier;
        }

        /// <summary>One hazard pulse of hydraulics (spec 02 §4): sources, simultaneous competing requests, publish.</summary>
        void WaterPulse()
        {
            int wc = D.WaterCount;
            Array.Copy(S.Volume, _avail, wc);
            for (int i = 0; i < D.SourceNode.Length; i++)
            {
                long inj = WaterRules.RatePulse(S.SourceRate[i], ref S.SourceRem[i]);
                _avail[D.SourceNode[i]] = checked(_avail[D.SourceNode[i]] + inj);
            }
            for (int i = 0; i < wc; i++) _head[i] = WaterRules.Head(D.WElev[i], _avail[i], D.WArea[i]);
            Array.Copy(_avail, _newVol, wc);
            SeaInflow();

            for (int node = 0; node < wc; node++)
            {
                if (_avail[node] <= 0) continue;
                int count = 0;
                // passive links (request IDs "edge:*" sort before pump/sink)
                foreach (int l in _nodeLinks[node])
                {
                    int a = D.LinkFrom[l], b = D.LinkTo[l];
                    if (b < 0) continue;
                    if (D.LinkGate[l] >= 0 && !S.GateOpen[D.LinkGate[l]]) continue;
                    int up, down;
                    if (_head[a] > _head[b]) { up = a; down = b; }
                    else if (_head[b] > _head[a]) { up = b; down = a; }
                    else continue;
                    if (up != node) continue;
                    if (D.LinkForward[l] && up != a) continue;
                    long q = WaterRules.PassiveRequest(_head[up], _head[down], D.LinkSill[l], BarrierIntact(l), D.LinkCond[l], D.LinkMax[l]);
                    if (q <= 0) continue;
                    AddRequest(ref count, q, down, 0, l);
                }
                // powered pumps drawing from this node
                for (int inst = 0; inst < S.C.EquipmentCount; inst++)
                {
                    if ((ToolKind)S.EqKind[inst] != ToolKind.Pump || S.EqBroken[inst] || !S.EqActive[inst]) continue;
                    int sock = S.EqSocket[inst];
                    if (sock < 0 || D.SocketIntake[sock] != node) continue;
                    if (S.EqPowerGrant[inst] <= 0) { continue; }
                    int dst = D.SocketOutlet[sock];
                    long q = WaterRules.PumpRequest(_head[node], _head[dst], ref S.EqRemainder[inst]);
                    if (q <= 0) continue;
                    AddRequest(ref count, q, dst, 1, inst);
                }
                // sinks / outlets
                for (int k = 0; k < D.SinkNode.Length; k++)
                {
                    if (D.SinkNode[k] != node) continue;
                    long rem = S.SinkRem[k];
                    long q = WaterRules.RatePulse(S.SinkRate[k], ref rem);
                    S.SinkRem[k] = rem;
                    if (q <= 0) continue;
                    AddRequest(ref count, q, -1, 2, k);
                }
                AddSeaRequests(node, ref count);
                AddSprinklerRequests(node, ref count);
                if (count == 0) continue;
                WaterRules.Allocate(_avail[node], new ReadOnlySpan<long>(_reqAmount, 0, count),
                                    new Span<long>(_reqGrant, 0, count), new Span<long>(_reqScratch, 0, count));
                for (int r = 0; r < count; r++)
                {
                    long g = _reqGrant[r];
                    if (g <= 0) continue;
                    _newVol[node] -= g;
                    int dst = _reqDest[r];
                    if (dst >= 0) _newVol[dst] = checked(_newVol[dst] + g);
                    if (_reqKind[r] == 1 && dst >= 0) RecordPumpedInto(dst, g);
                    if (_reqKind[r] == 3) ApplySprayGrant(_reqRef[r], g);
                    if (_reqKind[r] == SeaRequestKind) RecordSeaOut(_reqRef[r], g);
                }
            }
            for (int i = 0; i < wc; i++)
            {
                if (_newVol[i] < 0) throw new SimFaultException($"negative volume at water node {i}");
                S.Volume[i] = _newVol[i];
            }
        }

        void AddRequest(ref int count, long q, int dest, int kind, int reference)
        {
            if (count >= _reqAmount.Length) throw new SimFaultException("hydraulic fan-out exceeds 64 requests");
            _reqAmount[count] = q; _reqDest[count] = dest; _reqKind[count] = kind; _reqRef[count] = reference;
            count++;
        }

        void RecordPumpedInto(int waterNode, long amount)
        {
            for (int r = 0; r < D.RegionCells.Length; r++)
            {
                var cells = D.RegionCells[r];
                for (int k = 0; k < cells.Length; k++)
                    if (cells[k] == waterNode) { S.PumpedIntoRegion[r] += amount; break; }
            }
        }

        /// <summary>Maximum water depth over a region's cells.</summary>
        public long RegionDepth(int region)
        {
            long max = 0;
            foreach (int c in D.RegionCells[region]) max = Math.Max(max, Depth(c));
            return max;
        }

        /// <summary>Total water volume (conservation checks).</summary>
        public long TotalVolume()
        {
            long t = 0;
            for (int i = 0; i < D.WaterCount; i++) t = checked(t + S.Volume[i]);
            return t;
        }
    }
}
