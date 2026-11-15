using System;
using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Owns the authoritative run. Steps the deterministic simulator at 10 Hz from unscaled time (max 4 steps per
    /// frame, excess debt discarded — spec 02 §2), and keeps the previous step's water/positions for interpolation.
    /// Presentation reads state; it never writes it.
    /// </summary>
    public sealed class SimDriver : MonoBehaviour, ISimEventSink
    {
        public const float StepSeconds = 0.1f;
        public const int MaxStepsPerFrame = 4;

        public string MissionId = "W1-M1";
        public string LayoutClass = "wide";
        public RuleProfile Profile = RuleProfile.Standard;
        public bool Guided;
        public bool AutoStart = true;
        /// <summary>The named challenge this run plays (null for the base mission).</summary>
        public string ChallengeId { get; private set; }

        public Simulator Sim { get; private set; }
        public MissionDef Def => Sim?.D;
        public bool Paused { get; private set; } = true;
        public float Alpha { get; private set; }
        public long[] PrevVolume { get; private set; }
        public int DroppedSteps { get; private set; }

        public event Action<SimEvent> OnSimEvent;
        public event Action OnStepped;
        public event Action OnRunLoaded;

        StrategyScript _autopilot;
        int _autopilotCursor;
        float _accumulator;
        readonly List<SimEvent> _events = new List<SimEvent>(64);
        /// <summary>Footage only (tools/xr/shoot.py): slows the run together with Time.timeScale, because this Mac can't
        /// film the Simulator at speed. It changes when steps happen, never what they do. 1 everywhere else.</summary>
        public float CapturePace = 1f;
        /// <summary>Footage only (Editor capture): when above zero, each rendered frame advances the simulation by exactly this
        /// many seconds, however slowly the Mac renders, so a frame-stepped clip plays back at true game speed.</summary>
        public static float FixedFrameStep;

        void Start()
        {
            if (AutoStart) LoadRun(MissionId, LayoutClass, Profile, Guided);
        }

        public void LoadRun(string missionId, string layout, RuleProfile profile, bool guided, string challenge = null)
        {
            MissionId = missionId; LayoutClass = layout; Profile = profile; Guided = guided; ChallengeId = challenge;
            var def = ContentLibrary.Compile(missionId, layout, challenge);
            Sim = new Simulator(def, profile, this, guided);
            PrevVolume = (long[])Sim.S.Volume.Clone();
            _accumulator = 0;
            Paused = true;
            _autopilot = null;
            OnRunLoaded?.Invoke();
            FlushEvents();
        }

        /// <summary>Replays a validated strategy through the normal command path (dev/autopilot/capture only).</summary>
        public void StartAutopilot(string strategy)
        {
            _autopilot = ContentLibrary.Strategy(MissionId, strategy, ChallengeId);
            _autopilotCursor = 0;
        }

        public void SetPaused(bool paused) => Paused = paused;

        public Reject Submit(Command c) => Sim == null ? Reject.InvalidPhase : Sim.Submit(c);

        /// <summary>Command-mode stepping: advance exactly n operation steps (10 = 1 s, 50 = 5 s).</summary>
        public void Advance(int operationSteps)
        {
            for (int i = 0; i < operationSteps && !Sim.IsOver; i++) StepOnce();
        }

        void Update()
        {
            if (Sim == null) return;
            if (Paused || Sim.IsOver) { Alpha = 1f; return; }
            _accumulator += FixedFrameStep > 0f ? FixedFrameStep : Time.unscaledDeltaTime * CapturePace;
            int steps = 0;
            while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
            {
                StepOnce();
                _accumulator -= StepSeconds;
                steps++;
            }
            if (_accumulator > StepSeconds)
            {
                DroppedSteps += (int)(_accumulator / StepSeconds);
                _accumulator %= StepSeconds;
            }
            Alpha = Mathf.Clamp01(_accumulator / StepSeconds);
        }

        void StepOnce()
        {
            Array.Copy(Sim.S.Volume, PrevVolume, PrevVolume.Length);
            _autopilot?.SubmitDue(Sim, ref _autopilotCursor);
            Sim.Step();
            FlushEvents();
            OnStepped?.Invoke();
        }

        public void Emit(in SimEvent e) => _events.Add(e);

        void FlushEvents()
        {
            if (_events.Count == 0) return;
            for (int i = 0; i < _events.Count; i++) OnSimEvent?.Invoke(_events[i]);
            _events.Clear();
        }
    }
}
