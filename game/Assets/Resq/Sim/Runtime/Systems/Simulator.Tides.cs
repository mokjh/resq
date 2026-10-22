using Resq.Sim.Rules;

namespace Resq.Sim
{
    /// <summary>
    /// World 4 tides (spec 02 §4, §9). A sea boundary owns a prescribed head (datum + level) and no stored volume. Inflow is an
    /// independently capped request from an unlimited source; outflow competes with the cell's other outward requests. Both
    /// appear in the per-controller ledger, never as a silent overwrite of inland volume.
    /// </summary>
    public sealed partial class Simulator
    {
        public const int SeaRequestKind = 4;

        void InitTides()
        {
            for (int k = 0; k < D.TideIds.Length; k++)
            {
                long level = D.TideKnotTicks[k] != null ? TrackLevel(k, 0) : D.TideInitialLevel[k];
                if (D.TideKnotTicks[k] == null && D.TideStartIndex >= 0) level = TideRules.SegmentTargets[D.TideStartIndex];
                // degenerate initial ramp: start and end both at S=0
                S.TideStartTick[k] = 0; S.TideEndTick[k] = 0;
                S.TideStartLevel[k] = level; S.TideEndLevel[k] = level;
                S.TideLevel[k] = level;
            }
        }

        long TrackLevel(int k, long s)
        {
            var xs = D.TideKnotTicks[k];
            var ys = D.TideKnotLevels[k];
            if (D.TidePeriod[k] > 0) s = IntMath.FloorMod(s, D.TidePeriod[k]);
            if (s <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (s < xs[i]) return TideRules.Interpolate(xs[i - 1], ys[i - 1], xs[i], ys[i], s);
            return ys[ys.Length - 1]; // a nonrepeating track holds its last value
        }

        public long TideLevelAt(int k, long s) =>
            D.TideKnotTicks[k] != null ? TrackLevel(k, s)
                : TideRules.RampLevel(S.TideStartTick[k], S.TideStartLevel[k], S.TideEndTick[k], S.TideEndLevel[k], s);

        /// <summary>Stage 2, before warnings and operations: evaluate every controller at S.</summary>
        void EvaluateTides()
        {
            for (int k = 0; k < D.TideIds.Length; k++) S.TideLevel[k] = TideLevelAt(k, S.C.S);
        }

        /// <summary><c>set_tide_segment</c>: capture the level at this exact S and ramp over 100 scenario ticks to the target.</summary>
        void ApplyTideOp(EventOp op)
        {
            long s = S.C.S;
            foreach (int k in op.Targets)
            {
                long current = TideLevelAt(k, s);
                S.TideStartTick[k] = s; S.TideStartLevel[k] = current;
                S.TideEndTick[k] = s + TideRules.RampTicks; S.TideEndLevel[k] = TideRules.SegmentTargets[op.Value];
                S.TideLevel[k] = current;
            }
        }

        public long SeaHead(int k) => D.TideDatum[k] + S.TideLevel[k];

        /// <summary>Sea inflow into boundary cells from old heads; called after the availability copy in the water pulse.</summary>
        void SeaInflow()
        {
            for (int b = 0; b < D.SeaCell.Length; b++)
            {
                int c = D.SeaCell[b], k = D.SeaTide[b];
                long sea = SeaHead(k);
                if (sea <= _head[c]) continue;
                long q = WaterRules.PassiveRequest(sea, _head[c], D.SeaSill[b], false, D.SeaCond[b], D.SeaMax[b]);
                if (q <= 0) continue;
                _newVol[c] = checked(_newVol[c] + q);
                S.SeaIn[k] = checked(S.SeaIn[k] + q);
            }
        }

        /// <summary>Sea outflow requests for one node; they compete with its passive, pump and sink requests.</summary>
        void AddSeaRequests(int node, ref int count)
        {
            for (int b = 0; b < D.SeaCell.Length; b++)
            {
                if (D.SeaCell[b] != node) continue;
                long sea = SeaHead(D.SeaTide[b]);
                if (_head[node] <= sea) continue;
                long q = WaterRules.PassiveRequest(_head[node], sea, D.SeaSill[b], false, D.SeaCond[b], D.SeaMax[b]);
                if (q > 0) AddRequest(ref count, q, -1, SeaRequestKind, b);
            }
        }

        void RecordSeaOut(int boundary, long granted)
        {
            int k = D.SeaTide[boundary];
            S.SeaOut[k] = checked(S.SeaOut[k] + granted);
        }
    }
}
