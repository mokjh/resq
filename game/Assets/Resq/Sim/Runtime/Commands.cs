namespace Resq.Sim
{
    public enum CommandKind : byte
    {
        PlaceEquipment,     // A=socket, B=tool kind, C=orientation
        MoveIdleEquipment,  // A=instance, B=destination socket, C=orientation
        AssignGroupRoute,   // A=group, B=destination node, C=0 assemble / 1 evacuate
        AssignVehicleRoute, // A=vehicle, B=pickup group (-1 none), C=pickup node, D=destination node, E=1 evacuate / 0 dropoff
        SetPowerPriority,   // A=consumer (infra index or 1000+instance), B=priority
        SetGate,            // A=gate, B=1 open / 0 closed
        EnableTool,         // A=instance
        DisableTool,        // A=instance
        ActivateFirebreak,  // A=instance
        CoolSegment,        // A=instance, B=segment
        RefillTool,         // A=instance, B=store
        EndOperation,       // A=1 confirm failure
        AcknowledgeTutorial // advances guided first play
    }

    public enum Reject : byte
    {
        OK, InvalidSchema, UnknownKind, ContentVersionMismatch, WrongRun, InvalidSequence, SequenceReused,
        DuplicateIdMismatch, InvalidTick, InvalidPhase, UnknownTarget, InsufficientSupplies, StockExhausted,
        SocketIncompatible, Occupied, NotIdle, UnsafeTarget, RouteBlocked, CapacityFull, PowerRequired,
        ResourceEmpty, TrackingUnavailable, ObjectivesUnmet, QueueFull, CommandLimitReached, Pending
    }

    /// <summary>Validated simulation command. Packed integer operands keep it allocation-free and hashable.</summary>
    public struct Command
    {
        public CommandKind Kind;
        public int Sequence;
        public int A, B, C, D, E;

        public static Command Place(int socket, ToolKind tool, int orientation = 0) =>
            new Command { Kind = CommandKind.PlaceEquipment, A = socket, B = (int)tool, C = orientation };

        public static Command GroupRoute(int group, int node, bool evacuate) =>
            new Command { Kind = CommandKind.AssignGroupRoute, A = group, B = node, C = evacuate ? 1 : 0 };

        public static Command VehicleRoute(int vehicle, int pickupGroup, int pickupNode, int destination, bool evacuate) =>
            new Command { Kind = CommandKind.AssignVehicleRoute, A = vehicle, B = pickupGroup, C = pickupNode, D = destination, E = evacuate ? 1 : 0 };

        public static Command Enable(int instance) => new Command { Kind = CommandKind.EnableTool, A = instance };
        public static Command Disable(int instance) => new Command { Kind = CommandKind.DisableTool, A = instance };
        public static Command Move(int instance, int socket, int orientation = 0) =>
            new Command { Kind = CommandKind.MoveIdleEquipment, A = instance, B = socket, C = orientation };
        public static Command End(bool confirmFailure) => new Command { Kind = CommandKind.EndOperation, A = confirmFailure ? 1 : 0 };
        public static Command Priority(int consumer, int priority) =>
            new Command { Kind = CommandKind.SetPowerPriority, A = consumer, B = priority };

        public override string ToString() => $"{Kind}#{Sequence}({A},{B},{C},{D},{E})";
    }

    /// <summary>A command scheduled for an operation tick (replays and the input broker).</summary>
    public struct TimedCommand
    {
        public long OperationTick;
        public Command Command;
    }
}
