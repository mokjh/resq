using System;
using System.Collections.Generic;
using System.Reflection;

namespace Resq.Sim
{
    public enum Phase : byte { Planning, Active, Stabilizing, Outcome }

    public enum ResidentState : byte { AtNode, Walking, Boarding, Aboard, Unboarding, Evacuated, AwaitingBackup }

    public enum VehicleState : byte { Idle, Moving, Loading, Unloading, Waiting, Failed }

    public enum VehiclePhase : byte { None, ToPickup, Loading, ToDestination, Unloading, Done }

    /// <summary>All scalar run state; copied by value when cloning.</summary>
    public struct CoreState
    {
        public long O, S;
        public int Accumulator;
        public bool ScenarioEnabled;
        public RuleProfile Profile;
        public Phase Phase;
        public bool EndRequested, EndConfirmedFailure;
        public bool OperationStartedEmitted, OperationEnded;
        public long StartedO;
        public int TutorialStep;
        public int SupplySpent;
        public int EquipmentCount, VehicleCount;
        public int Evacuated, AwaitingBackup;
        public int Losses, GeneratorShutdowns, StationShutdowns, ShelterExhaustions;
        public long LoadedClosedDockWait;
        public bool Success;
        public int Stars;
        public long ScoreValue;
        public int LastSequence;
        public XorShift32 Rng;
    }

    /// <summary>
    /// Complete mutable simulation state. Arrays are preallocated at construction; <see cref="CopyFrom"/> clones into an
    /// existing instance without allocating (used by forecasts and replays).
    /// </summary>
    public sealed class SimState
    {
        public CoreState C;

        // water
        public long[] Volume, SourceRate, SourceRem, SinkRate, SinkRem, PumpedIntoRegion;
        public bool[] GateOpen;

        // equipment (instance index == deployment order)
        public byte[] EqKind;
        public int[] EqSocket, EqOrientation, EqDurability, EqPriority, EqPowerGrant;
        public bool[] EqActive, EqBroken, EqLatchedOff;
        public long[] EqRemainder;
        public int[] SocketInstance;          // instance at socket or -1
        public int[] Deployed;                // per tool kind, lifetime deployments

        // infrastructure / power
        public int[] InfraDemand, InfraPriority;
        public bool[] InfraPowered, InfraOperational, InfraEverOperational;

        // navigation
        public int[] NodeOccupancy, NodeReserved, NodeBerthUsed, NodePeak;
        public int[] EdgeOccupancy, EdgeCurrent;
        public bool[] EdgeAdmin, EdgeOpen;

        // residents
        public byte[] RState;
        public int[] RNode, RFrom, REdge, RProgress, RDuration, RVehicle, RExposure, RFinalMode, RTransfer;
        public long[] RDwellStart;
        public bool[] RReversing, RBoarded;

        // groups
        public int[] GroupTarget;             // node the group walks to, or -1
        public bool[] GroupEvacuate, GroupHeld, GroupBoarded;

        // vehicles (fixed first, then deployed skiffs/crawlers)
        public byte[] VKind, VState, VPhase;
        public int[] VSeats, VSlots, VNode, VFrom, VEdge, VProgress, VDuration, VInstance;
        public int[] VPickupGroup, VPickupNode, VDestination, VTransferResident, VTransferProgress;
        public bool[] VEvacuate, VReversing;
        public int[] VOccupantCount;
        public int[] VOccupants;              // VehicleCapacity * seatsMax flattened
        public int SeatsMax = 8;

        // fire (World 2) — per grid cell, plus wind per region
        public int[] FHeat, FFuel, FWet, FExtraCool;
        public byte[] FState;
        public long[] FWarnStart, FCoolStart, FSprayRem;
        public bool[] FLatch;
        public int[] WindCode;

        // stability / runout (World 3) — per support, per runout segment
        public int[] SupBase, SupStatic, SupLoss, SupRem;
        public long[] SupWarnTick;            // relevant warning tick or -1
        public bool[] SupWarnEmergent, SupArmed, SupCollapsed, RunStarted;
        public long[] SegStart;               // start tick or -1

        // winch duty cycle / brake (per vehicle slot)
        public int[] VDuty, VRest, VBrake;
        public bool[] VEmergency;

        // tides: ramp per controller, ledger per boundary
        public long[] TideStartTick, TideStartLevel, TideEndTick, TideEndLevel, TideLevel;
        public long[] SeaIn, SeaOut;

        // metrics
        public int[] VQueue;                  // consecutive operation ticks a vehicle waited on a full dock berth
        public long[] NodeMaxQueue;
        public int[] EdgeTransfers;           // resident traversals completed per edge
        public long[] NodeMaxDwell;           // longest continuous scenario-tick wait per node

        // events / milestones / objectives
        public bool[] Warned, Applied;
        public long[] MilestoneO, MilestoneS;
        public bool[] ObjLatched, ObjFailed;
        public long[] ObjFirstTrue, ObjHold;

        static FieldInfo[] _arrayFields;

        public SimState(MissionDef d)
        {
            int wc = d.WaterCount;
            Volume = new long[wc];
            SourceRate = new long[d.SourceNode.Length]; SourceRem = new long[d.SourceNode.Length];
            SinkRate = new long[d.SinkNode.Length]; SinkRem = new long[d.SinkNode.Length];
            PumpedIntoRegion = new long[d.RegionIds.Length];
            GateOpen = new bool[d.GateIds.Length];

            int eq = d.MaxEquipment;
            EqKind = new byte[eq]; EqSocket = new int[eq]; EqOrientation = new int[eq]; EqDurability = new int[eq];
            EqPriority = new int[eq]; EqPowerGrant = new int[eq]; EqActive = new bool[eq]; EqBroken = new bool[eq];
            EqLatchedOff = new bool[eq]; EqRemainder = new long[eq];
            SocketInstance = new int[d.SocketIds.Length];
            Deployed = new int[MissionDef.ToolCount];

            int inf = d.InfraIds.Length;
            InfraDemand = new int[inf]; InfraPriority = new int[inf]; InfraPowered = new bool[inf];
            InfraOperational = new bool[inf]; InfraEverOperational = new bool[inf];

            int n = d.NodeIds.Length, e = d.EdgeIds.Length;
            NodeOccupancy = new int[n]; NodeReserved = new int[n]; NodeBerthUsed = new int[n]; NodePeak = new int[n];
            EdgeOccupancy = new int[e]; EdgeCurrent = new int[e]; EdgeAdmin = new bool[e]; EdgeOpen = new bool[e];

            int r = d.ResidentIds.Length;
            RState = new byte[r]; RNode = new int[r]; RFrom = new int[r]; REdge = new int[r]; RProgress = new int[r];
            RDuration = new int[r]; RVehicle = new int[r]; RExposure = new int[r]; RFinalMode = new int[r];
            RTransfer = new int[r]; RDwellStart = new long[r]; RReversing = new bool[r]; RBoarded = new bool[r];

            int g = d.GroupIds.Length;
            GroupTarget = new int[g]; GroupEvacuate = new bool[g]; GroupHeld = new bool[g]; GroupBoarded = new bool[g];

            int v = d.MaxVehicles;
            VKind = new byte[v]; VState = new byte[v]; VPhase = new byte[v];
            VSeats = new int[v]; VSlots = new int[v]; VNode = new int[v]; VFrom = new int[v]; VEdge = new int[v];
            VProgress = new int[v]; VDuration = new int[v]; VInstance = new int[v];
            VPickupGroup = new int[v]; VPickupNode = new int[v]; VDestination = new int[v];
            VTransferResident = new int[v]; VTransferProgress = new int[v];
            VEvacuate = new bool[v]; VReversing = new bool[v];
            VOccupantCount = new int[v]; VOccupants = new int[v * SeatsMax];

            int cells = d.GridW * d.GridH;
            FHeat = new int[cells]; FFuel = new int[cells]; FWet = new int[cells]; FExtraCool = new int[cells];
            FState = new byte[cells]; FWarnStart = new long[cells]; FCoolStart = new long[cells]; FSprayRem = new long[cells];
            FLatch = new bool[cells];
            WindCode = new int[d.RegionIds.Length];

            int sp = d.SupportIds.Length;
            SupBase = new int[sp]; SupStatic = new int[sp]; SupLoss = new int[sp]; SupRem = new int[sp];
            SupWarnTick = new long[sp]; SupWarnEmergent = new bool[sp]; SupArmed = new bool[sp]; SupCollapsed = new bool[sp];
            RunStarted = new bool[d.RunoutIds.Length];
            SegStart = new long[d.SegmentIds.Length];
            VDuty = new int[v]; VRest = new int[v]; VBrake = new int[v]; VEmergency = new bool[v];
            int td = d.TideIds.Length;
            TideStartTick = new long[td]; TideStartLevel = new long[td]; TideEndTick = new long[td]; TideEndLevel = new long[td];
            TideLevel = new long[td]; SeaIn = new long[td]; SeaOut = new long[td];
            VQueue = new int[v]; NodeMaxQueue = new long[n];
            EdgeTransfers = new int[e];
            NodeMaxDwell = new long[n];

            Warned = new bool[d.Events.Length]; Applied = new bool[d.Events.Length];
            MilestoneO = new long[d.Milestones.Length]; MilestoneS = new long[d.Milestones.Length];
            int o = d.Objectives.Length;
            ObjLatched = new bool[o]; ObjFailed = new bool[o]; ObjFirstTrue = new long[o]; ObjHold = new long[o];
        }

        /// <summary>Copy every field of <paramref name="src"/> into this instance (same mission). Allocation-free.</summary>
        public void CopyFrom(SimState src)
        {
            C = src.C;
            SeatsMax = src.SeatsMax;
            foreach (var f in ArrayFields())
            {
                var a = (Array)f.GetValue(src);
                var b = (Array)f.GetValue(this);
                Array.Copy(a, b, a.Length);
            }
        }

        static FieldInfo[] ArrayFields()
        {
            if (_arrayFields != null) return _arrayFields;
            var list = new List<FieldInfo>();
            foreach (var f in typeof(SimState).GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (f.FieldType.IsArray) list.Add(f);
            list.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            _arrayFields = list.ToArray();
            return _arrayFields;
        }

        /// <summary>Deterministic 64-bit FNV-1a hash over the complete state (computed on demand, not per tick).</summary>
        public ulong Hash()
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                unchecked
                {
                    for (int i = 0; i < 8; i++)
                    {
                        h ^= (byte)(v >> (i * 8));
                        h *= 1099511628211UL;
                    }
                }
            }
            Mix(C.O); Mix(C.S); Mix(C.Accumulator); Mix(C.ScenarioEnabled ? 1 : 0); Mix((long)C.Phase);
            Mix(C.SupplySpent); Mix(C.EquipmentCount); Mix(C.VehicleCount); Mix(C.Evacuated); Mix(C.AwaitingBackup);
            Mix(C.Losses); Mix(C.GeneratorShutdowns); Mix(C.EndRequested ? 1 : 0); Mix(C.TutorialStep);
            foreach (var f in ArrayFields())
            {
                var a = (Array)f.GetValue(this);
                switch (a)
                {
                    case long[] la: foreach (var x in la) Mix(x); break;
                    case int[] ia: foreach (var x in ia) Mix(x); break;
                    case bool[] ba: foreach (var x in ba) Mix(x ? 1 : 0); break;
                    case byte[] bya: foreach (var x in bya) Mix(x); break;
                }
            }
            return h;
        }
    }
}
