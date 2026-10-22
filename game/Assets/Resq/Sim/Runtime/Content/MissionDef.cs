using System;
using System.Collections.Generic;

namespace Resq.Sim
{
    public enum ToolKind : byte
    {
        Barrier, Beacon, Brace, Bridge, Cooler, Crawler, Firebreak, Generator, Pump, Relay, Shelter, Skiff, Sprinkler, Winch
    }

    public enum Traversal : byte { Ground, Water, Cable }

    [Flags]
    public enum NodeFlags : ushort
    {
        None = 0,
        Assembly = 1,
        Junction = 2,
        Dock = 4,
        Shelter = 8,
        Evacuation = 16,      // final permanent refuge
        VehicleParking = 32,
        Staging = 64,         // temporary staging (not refuge)
    }

    public enum RuleProfile : byte { Standard, Relaxed, Command }

    public enum Tier : byte { Required, Critical, Mastery, Optional }

    public enum TemporalKind : byte { AtEnd, EverBy, Always, EverBefore, HoldFor }

    public enum ConditionKind : byte
    {
        RescueCount, GroupEvacuated, InfrastructureOperational, RouteAvailable, ExposureBelow, EquipmentLossesAtMost,
        SupplyRemainingAtLeast, ResourceRemaining, AllVehiclesUnloaded, TemporaryShelterEmpty, TemporaryStagingEmpty,
        ScenarioElapsedAtMost, CounterAtLeast, CounterAtMost, All, Any
    }

    public enum MetricKind : byte
    {
        None, EvacuatedByMode, EdgeTransfers, EquipmentDeployments, MaxNodeDwell, PeakNodeOccupancy, MaxDockQueue,
        LoadedClosedDockWait, PumpedIntoRegion, GeneratorShutdowns, StationShutdowns, ShelterExhaustions
    }

    public enum EventOpKind : byte
    {
        SetSourceRate, WarnRoute, SetEdgeEnabled, SetCurrentMultiplier, ReleaseGroup, IgniteRegion, SetWind, SetRegionCooling,
        WarnResource, AddHeat, WarnWindChange, SetStabilityLoss, SetSupportLoad, Tremor, WarnRunout, ReleaseAvalanche,
        WarnSupport, SetTideSegment, SetInfrastructureDemand, AdvanceLava, EnableCoolerSocket, WarnReheat, EnableShelterHazard
    }

    public enum VehicleKind : byte { FixedBus, Skiff, Crawler, Winch }

    public sealed class Condition
    {
        public ConditionKind Kind;
        public int A = -1;        // group / infrastructure / route / region / store index
        public long Value;        // minimum / maximum / threshold
        public int Hazard;        // 0 water 1 fire 2 lava 3 runout
        public bool Flag;         // operational
        public MetricKind Metric;
        public int MetricMode = -1;   // traversal for evacuated_by_mode, or tool kind
        public int MetricTarget = -1; // node / region / group / tag
        public Condition[] Children;
    }

    public sealed class ObjectiveDef
    {
        public string Id;
        public Tier Tier;
        public Condition Condition;
        public TemporalKind Temporal;
        public long DeadlineTick;
        public int StartMilestone = -1; // milestone index; -2 = operation_started
        public int EndMilestone = -1;   // -3 = operation_ended
        public long HoldTicks;
    }

    public enum MilestoneKind : byte { GroupBoarded, FinalUnloadStarted, AllEvacuated, FirstCooledCrossingClosed }

    public sealed class MilestoneDef
    {
        public string Id;
        public MilestoneKind Kind;
        public int Target = -1;
    }

    public struct EventOp
    {
        public EventOpKind Kind;
        public int[] Targets;   // resolved target indices (meaning depends on kind)
        public long Value;
    }

    public sealed class EventDef
    {
        public string Id;
        public long AtTick;
        public long WarningTick;
        public EventOp[] Ops;
        public string WarningKey; // localisation key for the warning text
    }

    /// <summary>
    /// Immutable compiled mission + chosen layout. All entity arrays are sorted by stable ID (ordinal), so index order
    /// equals the spec's "stable ID order" tie-break everywhere.
    /// </summary>
    public sealed class MissionDef
    {
        // ---- identity / budgets ----
        public string MissionId, WorldId, Title, LayoutClass, ContentHash;
        public long DurationTicks;
        public int TotalResidents, MinimumRescues, SupplyBudget;
        public bool GuidedFirstPlay;
        public int[] MaxStock = new int[14], DefaultLoadout = new int[14], ToolCost = new int[14];
        public bool[] Permitted = new bool[14];

        // ---- grid ----
        public int GridW = 32, GridH = 24;
        public long CellPitchMm = 1000;
        public long[] CellElevation;       // mm, length W*H
        public byte[] CellTerrain;          // authored terrain code (presentation + rules)

        // ---- water (terrain cells first: water node index == cell index; then tanks) ----
        public int WaterCount;
        public long[] WElev, WArea, WInitVol;
        public bool[] WIsTank;
        public int[] LinkFrom, LinkTo;       // water node indices; LinkTo = -1 - seaIndex for sea boundaries
        public long[] LinkSill, LinkCond, LinkMax;
        public bool[] LinkForward;           // directed from→to only
        public int[] LinkBarrierSocket;      // socket index or -1
        public int[] LinkGate;               // gate index or -1
        public bool[] GateInitialOpen = Array.Empty<bool>();
        public string[] GateIds = Array.Empty<string>();
        public int[] SourceNode; public long[] SourceRate0; public string[] SourceIds;
        public int[] SinkNode; public long[] SinkRate0; public string[] SinkIds;
        public string[] RegionIds;
        public int[][] RegionCells;          // water/fire cells that belong to region
        public Dictionary<string, int> RegionIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- navigation ----
        public string[] NodeIds;
        public NodeFlags[] NodeFlags;
        public int[] NodeCell, NodeWaterCell, NodeCapacity, NodeBerths, NodeVehicleSlots;
        public long[] NodeSafeDepth, NodeDockMinDepth, NodeDockMaxDepth;
        public int[] NodeInfra;              // infrastructure whose power/operation gates the dock, or -1
        public Dictionary<string, int> NodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        public string[] EdgeIds;
        public int[] EdgeFrom, EdgeTo, EdgeSlots, EdgeTicks, EdgeRequiredSocket, EdgeCurrent0;
        public Traversal[] EdgeClass;
        public bool[] EdgeStepFree, EdgeAdmin0;
        public long[] EdgeMaxDepth, EdgeMinDepth;
        public int[][] EdgeSampleCells;      // water cells sampled for depth closure
        public int[][] EdgeTags;             // tag indices
        public string[] TagIds = Array.Empty<string>();
        public int[][] NodeOutEdges;         // adjacency (sorted by edge index)
        public Dictionary<string, int> EdgeIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- named routes ----
        public string[] RouteIds = Array.Empty<string>();
        public int[] RouteFrom = Array.Empty<int>(), RouteTo = Array.Empty<int>();
        public Traversal[] RouteClass = Array.Empty<Traversal>();

        // ---- sockets ----
        public string[] SocketIds;
        public ToolKind[] SocketKind;
        public int[] SocketCell, SocketZone, SocketEdge, SocketIntake, SocketOutlet, SocketNode, SocketPowerNode, SocketDestination;
        public int[][] SocketLinks;          // barrier links
        public int[][] SocketPlatformCells;  // cells sampled for generator/relay platform depth
        public byte[] SocketOrientations;    // bitmask of allowed quarter turns
        public Dictionary<string, int> SocketIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- power ----
        public int PowerNodeCount;
        public string[] PowerNodeIds = Array.Empty<string>();
        public int[] PowerNodeFixedSupply = Array.Empty<int>();
        public int[][] PowerNodePlatformCells = Array.Empty<int[]>();
        public int[] PowerLinkFrom = Array.Empty<int>(), PowerLinkTo = Array.Empty<int>(), PowerLinkRelaySocket = Array.Empty<int>();

        // ---- infrastructure (power consumers with fixed demand) ----
        public string[] InfraIds = Array.Empty<string>();
        public int[] InfraPowerNode = Array.Empty<int>(), InfraDemand0 = Array.Empty<int>(), InfraPriority0 = Array.Empty<int>();
        public int[] InfraRegion = Array.Empty<int>();
        public long[] InfraMaxDepth = Array.Empty<long>();
        public int[][] InfraRequiredNodes = Array.Empty<int[]>();
        public Dictionary<string, int> InfraIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- groups / residents / vehicles ----
        public string[] GroupIds;
        public int[] GroupStart, GroupAssembly, GroupPickup;
        public bool[] GroupHeld;
        public int[][] GroupResidents;
        public Dictionary<string, int> GroupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        public string[] ResidentIds;
        public int[] ResidentGroup;
        public bool[] ResidentStepFree;

        public string[] FixedVehicleIds = Array.Empty<string>();
        public VehicleKind[] FixedVehicleKind = Array.Empty<VehicleKind>();
        public int[] FixedVehicleNode = Array.Empty<int>(), FixedVehicleSeats = Array.Empty<int>(), FixedVehicleSlots = Array.Empty<int>();
        public int MaxVehicles = 8;
        public int TutorialGroup = -1;           // guided first play: group the fixed bus must deliver
        public int[] BusAutoRoute;               // guided first play: {pickupGroup, pickupNode, destinationNode}
        public int MaxEquipment = 32;

        // ---- events / objectives ----
        public EventDef[] Events = Array.Empty<EventDef>();      // sorted by (AtTick, Id)
        public EventDef[] WarningOrder = Array.Empty<EventDef>(); // sorted by (WarningTick, Id)
        public ObjectiveDef[] Objectives = Array.Empty<ObjectiveDef>();
        public MilestoneDef[] Milestones = Array.Empty<MilestoneDef>();
        public int[] ProtectionObjectives = Array.Empty<int>();

        public int CellIndex(int x, int y) => y * GridW + x;
        public int CellX(int cell) => cell % GridW;
        public int CellY(int cell) => cell / GridW;

        public static int ToolCount => 14;
    }
}
