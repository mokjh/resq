using System;
using System.Collections.Generic;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    public enum SimEventKind : byte
    {
        CommandResolved, EquipmentPlaced, EquipmentMoved, EquipmentBroken, Warning, EventApplied, RouteBlocked,
        TransferStarted, TransferCompleted, TransferCancelled, ResidentEvacuated, ResidentBackup, MilestoneReached,
        ObjectiveUpdated, OperationStarted, OperationEnded, PowerChanged, GeneratorShutdown, VehicleArrived
    }

    public struct SimEvent
    {
        public SimEventKind Kind;
        public long O, S;
        public int Subject, A, B;
        public Reject Code;
    }

    public interface ISimEventSink
    {
        void Emit(in SimEvent e);
    }

    /// <summary>
    /// Deterministic RESQ rules engine (spec 02). One instance owns one run. Stages follow spec 02 §3 exactly;
    /// systems live in partial files. No UnityEngine, no floats in rules, no wall clock.
    /// </summary>
    public sealed partial class Simulator
    {
        public readonly MissionDef D;
        public readonly SimState S;
        public ISimEventSink Sink;

        // queued commands for the next operation step
        readonly List<Command> _pending = new List<Command>(32);
        bool _hazardPulse;

        public Simulator(MissionDef def, RuleProfile profile, ISimEventSink sink = null, bool guided = false)
        {
            D = def;
            S = new SimState(def);
            Sink = sink;
            AllocateScratch();
            Initialize(profile, guided);
        }

        /// <summary>Clone constructor for forecasts: shares the immutable definition, copies state.</summary>
        public Simulator(Simulator source)
        {
            D = source.D;
            S = new SimState(source.D);
            AllocateScratch();
            S.CopyFrom(source.S);
            _pending.AddRange(source._pending);
        }

        public void CopyStateFrom(Simulator source)
        {
            S.CopyFrom(source.S);
            _pending.Clear();
            _pending.AddRange(source._pending);
        }

        public IReadOnlyList<Command> Pending => _pending;

        void Initialize(RuleProfile profile, bool guided)
        {
            var c = new CoreState
            {
                Profile = profile,
                Phase = Phase.Planning,
                Rng = new XorShift32(1),
                TutorialStep = guided ? 1 : 0,
            };
            S.C = c;
            for (int i = 0; i < D.WaterCount; i++) S.Volume[i] = D.WInitVol[i];
            for (int i = 0; i < D.SourceNode.Length; i++) S.SourceRate[i] = D.SourceRate0[i];
            for (int i = 0; i < D.SinkNode.Length; i++) S.SinkRate[i] = D.SinkRate0[i];
            InitFire();
            for (int i = 0; i < D.GateIds.Length; i++) S.GateOpen[i] = D.GateInitialOpen[i];
            for (int i = 0; i < S.SocketInstance.Length; i++) S.SocketInstance[i] = -1;
            for (int i = 0; i < D.InfraIds.Length; i++)
            {
                S.InfraDemand[i] = D.InfraDemand0[i];
                S.InfraPriority[i] = D.InfraPriority0[i];
            }
            for (int e = 0; e < D.EdgeIds.Length; e++)
            {
                S.EdgeAdmin[e] = D.EdgeAdmin0[e];
                S.EdgeCurrent[e] = D.EdgeCurrent0[e];
            }
            for (int g = 0; g < D.GroupIds.Length; g++)
            {
                S.GroupTarget[g] = -1;
                S.GroupHeld[g] = D.GroupHeld[g];
            }
            for (int r = 0; r < D.ResidentIds.Length; r++)
            {
                int g = D.ResidentGroup[r];
                S.RState[r] = (byte)ResidentState.AtNode;
                S.RNode[r] = D.GroupStart[g];
                S.RFrom[r] = -1; S.REdge[r] = -1; S.RVehicle[r] = -1; S.RFinalMode[r] = -1; S.RTransfer[r] = -1;
                S.NodeOccupancy[D.GroupStart[g]]++;
            }
            for (int i = 0; i < D.Milestones.Length; i++) { S.MilestoneO[i] = -1; S.MilestoneS[i] = -1; }
            for (int i = 0; i < D.Objectives.Length; i++) { S.ObjFirstTrue[i] = -1; }
            for (int v = 0; v < S.VNode.Length; v++) ResetVehicleSlot(v);
            for (int f = 0; f < D.FixedVehicleIds.Length; f++)
                SpawnVehicle(D.FixedVehicleKind[f], D.FixedVehicleNode[f], D.FixedVehicleSeats[f], D.FixedVehicleSlots[f], -1);

            // Fixed tutorial bus: preassigned depot route waiting for legal access.
            for (int f = 0; f < D.FixedVehicleIds.Length; f++)
            {
                if (D.FixedVehicleKind[f] == VehicleKind.FixedBus && D.BusAutoRoute != null && guided)
                    ApplyVehicleRoute(f, D.BusAutoRoute[0], D.BusAutoRoute[1], D.BusAutoRoute[2], true);
            }

            DerivePowerAndInfrastructure();
            DeriveEdgeAvailability();

            // Tick-zero warnings, once, in event-ID order (WarningOrder is sorted by tick then id).
            foreach (var ev in D.WarningOrder)
            {
                if (ev.WarningTick != 0) continue;
                int idx = Array.IndexOf(D.Events, ev);
                S.Warned[idx] = true;
                FireOnWarning(ev);
                Emit(SimEventKind.Warning, idx);
            }
            if (!guided)
            {
                S.C.ScenarioEnabled = true;
                S.C.OperationStartedEmitted = true;
                Emit(SimEventKind.OperationStarted, -1);
            }
            EvaluateObjectives(initialSample: true);
        }

        // ----------------------------------------------------------------- command admission
        /// <summary>Queue a command for the next operation step. Returns Pending, or a fast rejection.</summary>
        public Reject Submit(Command cmd)
        {
            if (S.C.Phase == Phase.Outcome) return Reject.InvalidPhase;
            if (_pending.Count >= 32) return Reject.QueueFull;
            cmd.Sequence = ++S.C.LastSequence;
            _pending.Add(cmd);
            return Reject.Pending;
        }

        /// <summary>Begin the Active phase (the first step after placement/briefing).</summary>
        public void BeginActive()
        {
            if (S.C.Phase == Phase.Planning) S.C.Phase = Phase.Active;
        }

        public bool IsOver => S.C.Phase == Phase.Outcome;

        // ----------------------------------------------------------------- the step (spec 02 §3)
        public void Step()
        {
            if (S.C.Phase == Phase.Outcome) return;
            if (S.C.Phase == Phase.Planning) S.C.Phase = Phase.Active;

            // 1. clocks
            int numerator = S.C.Profile == RuleProfile.Relaxed ? 1 : 2;
            _hazardPulse = ClockRules.Step(ref S.C.O, ref S.C.S, ref S.C.Accumulator, numerator, S.C.ScenarioEnabled);

            // 2. scheduled warnings / parameter operations
            if (_hazardPulse) ApplyScheduledEvents();

            // 3. commands, revalidated against current state
            for (int i = 0; i < _pending.Count; i++) ExecuteCommand(_pending[i]);
            _pending.Clear();

            // 4. power grants for the interval
            DerivePowerAndInfrastructure();

            // 5. hazards from old state
            if (_hazardPulse)
            {
                WaterPulse();
                HazardModulesPulse();
            }

            // 6. safety recomputation, closures, equipment strain
            if (_hazardPulse) ApplyEquipmentStrain();
            DerivePowerAndInfrastructure();
            DeriveEdgeAvailability();

            // 7. movement and transfers
            TransportStep();

            // 8. resident state, metrics, milestones, temporal objectives
            if (_hazardPulse) ExposureStep();
            UpdateMilestones();
            EvaluateObjectives(initialSample: false);

            // 9. terminal evaluation
            if (S.C.S >= D.DurationTicks || S.C.EndRequested) Terminate();
            else S.C.Phase = TerminalConditionsMet() ? Phase.Stabilizing : Phase.Active;
        }

        void ApplyScheduledEvents()
        {
            long s = S.C.S;
            for (int i = 0; i < D.Events.Length; i++)
            {
                var ev = D.Events[i];
                if (!S.Warned[i] && ev.WarningTick == s)
                {
                    S.Warned[i] = true;
                    FireOnWarning(ev);
                    Emit(SimEventKind.Warning, i);
                }
            }
            for (int i = 0; i < D.Events.Length; i++)
            {
                var ev = D.Events[i];
                if (S.Applied[i] || ev.AtTick != s) continue;
                S.Applied[i] = true;
                foreach (var op in ev.Ops) ApplyEventOp(op);
                Emit(SimEventKind.EventApplied, i);
            }
        }

        void ApplyEventOp(EventOp op)
        {
            switch (op.Kind)
            {
                case EventOpKind.SetSourceRate:
                    foreach (int t in op.Targets) S.SourceRate[t] = op.Value;
                    break;
                case EventOpKind.SetEdgeEnabled:
                    foreach (int t in op.Targets) S.EdgeAdmin[t] = op.Value != 0;
                    break;
                case EventOpKind.SetCurrentMultiplier:
                    foreach (int t in op.Targets) S.EdgeCurrent[t] = (int)op.Value;
                    break;
                case EventOpKind.ReleaseGroup:
                    foreach (int t in op.Targets) S.GroupHeld[t] = false;
                    break;
                case EventOpKind.SetInfrastructureDemand:
                    foreach (int t in op.Targets) S.InfraDemand[t] = (int)op.Value;
                    break;
                case EventOpKind.WarnRoute:
                case EventOpKind.WarnResource:
                case EventOpKind.WarnWindChange:
                case EventOpKind.WarnRunout:
                case EventOpKind.WarnSupport:
                case EventOpKind.WarnReheat:
                    break; // advisory only
                default:
                    ApplyWorldEventOp(op);
                    break;
            }
        }

        void Terminate()
        {
            S.C.OperationEnded = true;
            Emit(SimEventKind.OperationEnded, -1);
            EvaluateObjectives(initialSample: false, terminal: true);
            ComputeOutcome();
            S.C.Phase = Phase.Outcome;
        }

        // ----------------------------------------------------------------- helpers
        public long Depth(int waterNode) => Math.Max(0, IntMath.FloorDiv(S.Volume[waterNode], D.WArea[waterNode]));

        public long NodeDepth(int node) => D.NodeCell[node] >= 0 ? Depth(D.NodeCell[node]) : 0;

        public long Head(int waterNode) => WaterRules.Head(D.WElev[waterNode], S.Volume[waterNode], D.WArea[waterNode]);

        void Emit(SimEventKind kind, int subject, int a = 0, int b = 0, Reject code = Reject.OK)
        {
            Sink?.Emit(new SimEvent { Kind = kind, O = S.C.O, S = S.C.S, Subject = subject, A = a, B = b, Code = code });
        }
    }
}
