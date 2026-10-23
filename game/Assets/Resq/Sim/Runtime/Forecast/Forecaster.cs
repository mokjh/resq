using System;

namespace Resq.Sim
{
    /// <summary>What the future looks like if the player does nothing else (spec 02 §12: exact given no further orders).</summary>
    public sealed class ForecastResult
    {
        public int Revision;
        public long FromO, FromS, ToS;
        public Reject PreviewVerdict = Reject.OK;
        public long[] MaxDepth;          // per water node, maximum depth (mm) over the horizon
        public long[] EndDepth;          // per water node, depth at the horizon
        public bool[] EdgeEverClosed;    // per nav edge
        public bool[] NodeEverUnsafe;    // per nav node
        public int Evacuated, Backup;
        public bool Finished, Success;
        public int Stars;
        public int StepsRun;

        public ForecastResult(MissionDef d)
        {
            MaxDepth = new long[d.WaterCount];
            EndDepth = new long[d.WaterCount];
            EdgeEverClosed = new bool[d.EdgeIds.Length];
            NodeEverUnsafe = new bool[d.NodeIds.Length];
        }
    }

    /// <summary>
    /// Runs the real simulator on an isolated clone: no events, rewards, saves or presentation side effects.
    /// The clone is allocated once and reused, so repeated forecasts do not allocate.
    /// </summary>
    public sealed class Forecaster
    {
        public const int DefaultHorizon = 300;
        public const int MaxHorizon = 600;
        public const int MaxSteps = 1200;

        Simulator _clone;
        readonly VerdictSink _sink = new VerdictSink();

        sealed class VerdictSink : ISimEventSink
        {
            public int WatchSequence = -1;
            public Reject Verdict;
            public void Emit(in SimEvent e)
            {
                if (e.Kind == SimEventKind.CommandResolved && e.Subject == WatchSequence) Verdict = e.Code;
            }
        }

        long _fromO, _fromS;

        public void Run(Simulator live, Command? preview, int horizonScenarioTicks, ForecastResult result)
        {
            Prepare(live, preview);
            Advance(horizonScenarioTicks, result);
        }

        /// <summary>
        /// Main thread: copy the live state into this forecaster's private clone and submit the previewed command.
        /// After this the clone shares nothing mutable with the live run, so <see cref="Advance"/> may run on a
        /// worker thread while the game keeps stepping.
        /// </summary>
        public void Prepare(Simulator live, Command? preview)
        {
            if (_clone == null || _clone.D != live.D) _clone = new Simulator(live);
            else _clone.CopyStateFrom(live);
            _clone.Sink = _sink;
            _sink.WatchSequence = -1;
            _sink.Verdict = Reject.OK;
            _fromO = live.S.C.O; _fromS = live.S.C.S;
            if (preview.HasValue)
            {
                var r = _clone.Submit(preview.Value);
                if (r != Reject.Pending) _sink.Verdict = r;
                else _sink.WatchSequence = _clone.S.C.LastSequence;
            }
        }

        /// <summary>Any thread: step the prepared clone over the horizon and fill <paramref name="result"/>.</summary>
        public void Advance(int horizonScenarioTicks, ForecastResult result)
        {
            int horizon = Math.Max(1, Math.Min(MaxHorizon, horizonScenarioTicks));
            var d = _clone.D;
            result.FromO = _fromO;
            result.FromS = _fromS;
            Array.Clear(result.MaxDepth, 0, result.MaxDepth.Length);
            Array.Clear(result.EdgeEverClosed, 0, result.EdgeEverClosed.Length);
            Array.Clear(result.NodeEverUnsafe, 0, result.NodeEverUnsafe.Length);
            Sample(result, d);
            long targetS = _fromS + horizon;
            int steps = 0;
            // A guided clock or Relaxed pacing advances S slower than O; bound by operation steps too.
            while (!_clone.IsOver && _clone.S.C.S < targetS && steps < MaxSteps)
            {
                _clone.Step();
                steps++;
                Sample(result, d);
            }
            for (int i = 0; i < d.WaterCount; i++) result.EndDepth[i] = _clone.Depth(i);
            result.ToS = _clone.S.C.S;
            result.StepsRun = steps;
            result.PreviewVerdict = _sink.Verdict;
            result.Evacuated = _clone.S.C.Evacuated;
            result.Backup = _clone.S.C.AwaitingBackup;
            result.Finished = _clone.IsOver;
            result.Success = _clone.S.C.Success;
            result.Stars = _clone.S.C.Stars;
            _clone.Sink = null;
        }

        void Sample(ForecastResult r, MissionDef d)
        {
            var s = _clone;
            for (int i = 0; i < d.WaterCount; i++)
            {
                long depth = s.Depth(i);
                if (depth > r.MaxDepth[i]) r.MaxDepth[i] = depth;
            }
            for (int e = 0; e < d.EdgeIds.Length; e++) if (!s.S.EdgeOpen[e]) r.EdgeEverClosed[e] = true;
            for (int n = 0; n < d.NodeIds.Length; n++) if (!s.NodeSafe(n)) r.NodeEverUnsafe[n] = true;
        }
    }
}
