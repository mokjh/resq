using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation.Rescue
{
    // Supplied after stage 6 by the actual hazard/power/equipment owner. There is
    // deliberately no all-safe/default provider. Closure times are explicit O
    // predictions calculated by that owner from the current scenario epoch.
    public sealed class TransportNodeSample
    {
        public string Id { get; }
        public bool Safe { get; }
        public bool DockOpen { get; }
        public bool Powered { get; }
        public TransportNodeSample(string id, bool safe, bool dockOpen, bool powered) { Id = id; Safe = safe; DockOpen = dockOpen; Powered = powered; }
    }
    public sealed class TransportEdgeSample
    {
        public string Id { get; }
        public bool Open { get; }
        public bool Powered { get; }
        public long SpeedPermille { get; }
        public long? KnownClosureOperationTick { get; }
        public TransportEdgeSample(string id, bool open, bool powered, long speedPermille, long? knownClosureOperationTick)
        {
            if (speedPermille < 500 || speedPermille > 2000 || knownClosureOperationTick < 0) throw new ArgumentOutOfRangeException(nameof(speedPermille));
            Id = id; Open = open; Powered = powered; SpeedPermille = speedPermille; KnownClosureOperationTick = knownClosureOperationTick;
        }
    }
    public sealed class TransportFrame
    {
        public long OperationTick { get; }
        public ReadOnlyDictionary<string, TransportNodeSample> Nodes { get; }
        public ReadOnlyDictionary<string, TransportEdgeSample> Edges { get; }
        // Each resident's complete exposure envelope, including vehicle/edge,
        // skiff depth exception and this interval's shelter protection grant.
        public ReadOnlyDictionary<string, bool> ExposedResidents { get; }
        public TransportFrame(long operationTick, IEnumerable<TransportNodeSample> nodes, IEnumerable<TransportEdgeSample> edges, IDictionary<string, bool> exposedResidents)
        {
            var value = CreateWork(operationTick, nodes, edges, exposedResidents).Drain(); OperationTick = value.OperationTick;
            Nodes = value.Nodes; Edges = value.Edges; ExposedResidents = value.ExposedResidents;
        }
        private TransportFrame(long operationTick, ReadOnlyDictionary<string, TransportNodeSample> nodes, ReadOnlyDictionary<string, TransportEdgeSample> edges, ReadOnlyDictionary<string, bool> exposedResidents)
        { OperationTick = operationTick; Nodes = nodes; Edges = edges; ExposedResidents = exposedResidents; }
        public static ResumableWork<TransportFrame> CreateWork(long operationTick, IEnumerable<TransportNodeSample> nodes, IEnumerable<TransportEdgeSample> edges, IDictionary<string, bool> exposedResidents) => new ResumableWork<TransportFrame>(done => CreateRoutine(operationTick, nodes, edges, exposedResidents, done));
        private static IEnumerable<string> CreateRoutine(long operationTick, IEnumerable<TransportNodeSample> nodes, IEnumerable<TransportEdgeSample> edges, IDictionary<string, bool> exposedResidents, Action<TransportFrame> done)
        {
            new Tick(operationTick);
            Dictionary<string, TransportNodeSample> nodesCopy; using (var worknodesCopy = DomainWork.Index(nodes, x => x.Id, "transport.frame.node")) { while (worknodesCopy.MoveNext()) yield return worknodesCopy.Stage; nodesCopy = worknodesCopy.Result; }
            Dictionary<string, TransportEdgeSample> edgesCopy; using (var workedgesCopy = DomainWork.Index(edges, x => x.Id, "transport.frame.edge")) { while (workedgesCopy.MoveNext()) yield return workedgesCopy.Stage; edgesCopy = workedgesCopy.Result; }
            Dictionary<string, bool> exposedResidentsCopy; using (var workexposedResidentsCopy = DomainWork.Copy(exposedResidents, "transport.frame.exposure")) { while (workexposedResidentsCopy.MoveNext()) yield return workexposedResidentsCopy.Stage; exposedResidentsCopy = workexposedResidentsCopy.Result; }
            done(new TransportFrame(operationTick, new ReadOnlyDictionary<string, TransportNodeSample>(nodesCopy), new ReadOnlyDictionary<string, TransportEdgeSample>(edgesCopy), new ReadOnlyDictionary<string, bool>(exposedResidentsCopy)));
        }
    }

    // Stage-7 observations for the metric owner, not saved history or gameplay
    // events. Only an actual forward arrival creates one of these immutable facts.
    public sealed class CompletedEdgePassage
    {
        public string ResidentId { get; }
        public string EdgeId { get; }
        public CompletedEdgePassage(string residentId, string edgeId)
        {
            ResidentId = residentId ?? throw new ArgumentNullException(nameof(residentId));
            EdgeId = edgeId ?? throw new ArgumentNullException(nameof(edgeId));
        }
    }
    public sealed class DockWaitSample
    {
        // NodeId names the chosen unload dock, not the physical holding node.
        public string NodeId { get; }
        public string VehicleId { get; }
        public bool ChosenUnloadDockClosed { get; }
        public bool Loaded { get; }
        public bool ForBerthOrReceivingCapacity { get; }
        public DockWaitSample(string nodeId, string vehicleId, bool chosenUnloadDockClosed, bool loaded, bool forBerthOrReceivingCapacity)
        {
            NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            VehicleId = vehicleId ?? throw new ArgumentNullException(nameof(vehicleId));
            ChosenUnloadDockClosed = chosenUnloadDockClosed; Loaded = loaded;
            ForBerthOrReceivingCapacity = forBerthOrReceivingCapacity;
        }
    }
    public sealed class CompletedNodeDwell
    {
        public string ResidentId { get; }
        public string NodeId { get; }
        public long ScenarioTicks { get; }
        public CompletedNodeDwell(string residentId, string nodeId, long scenarioTicks)
        {
            ResidentId = residentId ?? throw new ArgumentNullException(nameof(residentId));
            NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            if (scenarioTicks < 0) throw new ArgumentOutOfRangeException(nameof(scenarioTicks));
            ScenarioTicks = scenarioTicks;
        }
    }

    // Sole owner of resident locations, occupants, transport routes and capacity
    // reservations. Call ResolveMovement once at stage 7, then Derive at stage 8.
    // All mutable objects stay private; published JSON is immutable.
    public sealed class TransportSystem
    {
        private sealed class Person
        {
            public string Id, Group, Mobility, State, LocationKind, LocationId, FinalMode;
            public long? EnteredScenario;
            public int Exposure;
            public Person Clone() => (Person)MemberwiseClone();
        }
        private sealed class Vehicle
        {
            public string Id, Kind, Node, TransitId, Status, EquipmentId, RouteId;
            public long Seats, Slots;
            public int Duty, Rest, Brake;
            public SortedSet<string> Occupants = new SortedSet<string>(StringComparer.Ordinal);
            public Vehicle Clone() { var x = (Vehicle)MemberwiseClone(); x.Occupants = new SortedSet<string>(Occupants, StringComparer.Ordinal); return x; }
        }
        private sealed class Transit
        {
            public string Id, MoverKind, MoverId, Edge, Origin, Destination, Mode = "forward";
            public long Duration, Progress, Return;
            public bool Closure;
            public Transit Clone() => (Transit)MemberwiseClone();
        }
        private sealed class Transfer
        {
            public string Id, Person, Vehicle, Node, Direction, SeatReservation, NodeReservation;
            public int Progress;
            public Transfer Clone() => (Transfer)MemberwiseClone();
        }
        private sealed class Reservation
        {
            public string Id, Owner, Kind, Resource, Channel;
            public long Quantity, Tick;
            public Reservation Clone() => (Reservation)MemberwiseClone();
        }
        private sealed class Stop
        {
            public string Node, Action, Group;
            public Stop(string node, string action, string group) { Node = node; Action = action; Group = group; }
            public JsonValue Json() => O("node_id", Node, "action", Action, "group_id", Group);
        }
        private sealed class Order
        {
            public string Id, Kind, Subject;
            public Stop[] Stops;
            public string[] Edges = new string[0];
            public int Next;
            public long Tick, Sequence;
            // Stops and edge arrays are constructed privately, then only replaced.
            // No owner mutates their elements, so a fork can share those values.
            public Order Clone() => (Order)MemberwiseClone();
        }
        private sealed class Waiting
        {
            public string Id, Subject, Target, Kind;
            public long Tick, Sequence;
            public Waiting Clone() => (Waiting)MemberwiseClone();
        }
        private readonly CompiledMission mission;
        private readonly LayoutDef layout;
        private readonly SortedDictionary<string, EquipmentDef> equipmentDefinitions;
        private readonly SortedDictionary<string, NavNode> nodes;
        private readonly SortedDictionary<string, NavEdge> edges;
        // Published only after complete yielded construction. Definitions and the
        // linked adjacency lists are immutable thereafter and shared by forks.
        private SortedDictionary<string, LinkedList<NavEdge>> outgoingEdges;
        private readonly SortedDictionary<string, Person> people = new SortedDictionary<string, Person>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Vehicle> vehicles = new SortedDictionary<string, Vehicle>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Transit> transits = new SortedDictionary<string, Transit>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Transfer> transfers = new SortedDictionary<string, Transfer>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Reservation> reservations = new SortedDictionary<string, Reservation>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Order> orders = new SortedDictionary<string, Order>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Waiting> queues = new SortedDictionary<string, Waiting>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, bool> held = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        private readonly SortedSet<string> boardedEver = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, JsonValue> milestones = new SortedDictionary<string, JsonValue>(StringComparer.Ordinal);
        private long lastMovementTick = -1, lastDerivedTick = -1;
        private readonly List<CompletedEdgePassage> completedEdgePassages = new List<CompletedEdgePassage>();
        private ReadOnlyCollection<CompletedEdgePassage> boundaryFacts = new List<CompletedEdgePassage>().AsReadOnly();
        private ReadOnlyCollection<DockWaitSample> dockWaitFacts = new List<DockWaitSample>().AsReadOnly();
        private readonly List<CompletedNodeDwell> completedNodeDwells = new List<CompletedNodeDwell>();
        private ReadOnlyCollection<CompletedNodeDwell> nodeDwellFacts = new List<CompletedNodeDwell>().AsReadOnly();
        private readonly SortedDictionary<string, long> nodeOccupancyPeaks = new SortedDictionary<string, long>(StringComparer.Ordinal);
        private ReadOnlyDictionary<string, long> occupancyPeakFacts = new ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(StringComparer.Ordinal));
        private long boundaryFactsTick = -1;
        public int Population => people.Count;
        public int EvacuatedCount => people.Values.Count(x => x.State == "evacuated");
        public bool TerminalTransportClear => vehicles.Values.All(x => x.Occupants.Count == 0) && people.Values.All(x => x.LocationKind != "node" || nodes[x.LocationId].kind != "shelter");
        public TransportSystem(CompiledContent content, string missionVersionId, string layoutId)
            : this((content ?? throw new ArgumentNullException(nameof(content))).Envelope.payload.missions.Single(x => x.mission_version_id == missionVersionId), content.Envelope.payload.layouts.Single(x => x.id == layoutId), content.Envelope.payload.equipment) { }
        // Typed synthetic graph constructor also supports isolated component tests;
        // production callers use exact compiler output through the constructor above.
        public TransportSystem(CompiledMission mission, LayoutDef layout, IEnumerable<EquipmentDef> equipmentDefinitions = null)
            : this(mission, layout, true)
        {
            InitializeWork(equipmentDefinitions).Drain();
        }
        private TransportSystem(CompiledMission mission, LayoutDef layout, bool deferred)
        {
            this.mission = mission ?? throw new ArgumentNullException(nameof(mission));
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            equipmentDefinitions = new SortedDictionary<string, EquipmentDef>(StringComparer.Ordinal);
            nodes = new SortedDictionary<string, NavNode>(StringComparer.Ordinal);
            edges = new SortedDictionary<string, NavEdge>(StringComparer.Ordinal);
        }
        public static ResumableWork<TransportSystem> CreateWork(CompiledMission mission, LayoutDef layout, IEnumerable<EquipmentDef> equipmentDefinitions = null)
            => new ResumableWork<TransportSystem>(done => CreateRoutine(mission, layout, equipmentDefinitions, done));
        private static IEnumerable<string> CreateRoutine(CompiledMission mission, LayoutDef layout, IEnumerable<EquipmentDef> equipmentDefinitions, Action<TransportSystem> done)
        {
            var next = new TransportSystem(mission, layout, true);
            using (var work = next.InitializeWork(equipmentDefinitions)) { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork(IEnumerable<EquipmentDef> definitions)
            => new ResumableWork<bool>(done => InitializeRoutine(definitions, done));
        private IEnumerable<string> InitializeRoutine(IEnumerable<EquipmentDef> definitions, Action<bool> done)
        {
            foreach (var definition in definitions ?? Enumerable.Empty<EquipmentDef>())
            { equipmentDefinitions.Add(definition.id, definition); yield return "transport.create.equipment"; }
            if (layout.mission_version_id != mission.mission_version_id) throw new InvalidOperationException("Transport content identity mismatch");
            foreach (var node in layout.nav_nodes) { nodes.Add(node.id, node); yield return "transport.create.node"; }
            foreach (var edge in layout.nav_edges) { edges.Add(edge.id, edge); yield return "transport.create.edge"; }
            var adjacency = new SortedDictionary<string, LinkedList<NavEdge>>(StringComparer.Ordinal);
            // The former full scan enumerated this exact sorted dictionary. An
            // append preserves that ordinal edge order inside each origin list.
            foreach (var edge in edges.Values)
            {
                if (!adjacency.TryGetValue(edge.from_node_id, out var outgoing))
                { outgoing = new LinkedList<NavEdge>(); adjacency.Add(edge.from_node_id, outgoing); }
                outgoing.AddLast(edge);
                yield return "transport.create.outgoing-edge";
            }
            outgoingEdges = adjacency;
            using (var work = InitializePopulationWork()) { while (work.MoveNext()) yield return work.Stage; }
            done(true);
        }
        private ResumableWork<bool> InitializePopulationWork()
            => new ResumableWork<bool>(InitializePopulationRoutine);
        private IEnumerable<string> InitializePopulationRoutine(Action<bool> done)
        {
            foreach (var group in mission.groups) { held.Add(group.id, group.initially_held); yield return "transport.create.group"; }
            foreach (var spawn in layout.spawns)
            {
                if (!held.ContainsKey(spawn.group_id) || !nodes.ContainsKey(spawn.start_node_id)) throw new InvalidOperationException("Unknown resident group/node");
                people.Add(spawn.resident_id, new Person { Id = spawn.resident_id, Group = spawn.group_id, Mobility = spawn.mobility_class, State = spawn.fixed_vehicle_id == null ? "waiting" : "in_transit", LocationKind = spawn.fixed_vehicle_id == null ? "node" : "vehicle", LocationId = spawn.fixed_vehicle_id ?? spawn.start_node_id, EnteredScenario = spawn.fixed_vehicle_id == null ? (long?)0 : null });
                yield return "transport.create.resident";
            }
            foreach (var spawn in layout.fixed_vehicles)
            {
                if (spawn.kind == "fixed_bus" && (spawn.seats != 4 || spawn.movement_slots != 2)) throw new InvalidOperationException("Fixed bus requires four seats and two ground slots");
                var v = new Vehicle { Id = spawn.vehicle_id, Kind = spawn.kind, Seats = spawn.seats, Slots = spawn.movement_slots, Node = spawn.node_id, Status = "idle" };
                foreach (var id in spawn.initial_occupant_ids) { v.Occupants.Add(id); boardedEver.Add(id); yield return "transport.create.occupant"; }
                vehicles.Add(v.Id, v);
                yield return "transport.create.vehicle";
                if (spawn.initial_route_id != null)
                {
                    NamedRoute named = null;
                    foreach (var route in layout.routes)
                    {
                        if (route.id == spawn.initial_route_id)
                        { if (named != null) throw new InvalidOperationException("Multiple matching initial routes"); named = route; }
                        yield return "transport.create.route-search";
                    }
                    if (named == null) throw new InvalidOperationException("No matching initial route");
                    var routeStops = new Stop[named.required_waypoint_ids.Count + 1];
                    yield return "transport.create.route-allocation";
                    for (int index = 0; index < named.required_waypoint_ids.Count; index++)
                    { routeStops[index] = new Stop(named.required_waypoint_ids[index], "assemble", null); yield return "transport.create.route-stop"; }
                    routeStops[routeStops.Length - 1] = new Stop(named.to_node_id, nodes[named.to_node_id].kind == "evacuation" ? "evacuate" : "dropoff", null);
                    var order = new Order { Id = StableId("route", 0, "vehicle", v.Id, "named_route", named.id), Kind = "vehicle", Subject = v.Id, Stops = routeStops, Tick = 0, Sequence = 0 }; orders.Add(order.Id, order); v.RouteId = order.Id;
                    yield return "transport.create.route";
                }
            }
            foreach (var group in mission.groups)
            {
                string[] expected;
                using (var sort = ResumableSort.Create(group.resident_ids, StringComparer.Ordinal))
                { while (sort.MoveNext()) yield return sort.Stage; expected = sort.Result; }
                int index = 0;
                foreach (var person in people.Values)
                {
                    if (person.Group == group.id)
                    {
                        if (index == expected.Length || expected[index++] != person.Id) throw new InvalidOperationException("Group population mismatch");
                    }
                    yield return "transport.create.group-population";
                }
                if (index != expected.Length) throw new InvalidOperationException("Group population mismatch");
            }
            using (var validation = ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
            done(true);
        }
        private TransportSystem(TransportSystem old, bool initial = false)
        {
            mission = old.mission; layout = old.layout; equipmentDefinitions = old.equipmentDefinitions; nodes = old.nodes; edges = old.edges; outgoingEdges = old.outgoingEdges;
            if (!initial)
            {
                lastMovementTick = old.lastMovementTick; lastDerivedTick = old.lastDerivedTick; boundaryFactsTick = old.boundaryFactsTick;
                // Published boundary observations are immutable; mutable interval
                // lists and owner records are copied by ForkWork below.
                boundaryFacts = old.boundaryFacts; dockWaitFacts = old.dockWaitFacts; nodeDwellFacts = old.nodeDwellFacts; occupancyPeakFacts = old.occupancyPeakFacts;
            }
        }
        public TransportSystem Fork() => ForkWork().Drain();
        public ResumableWork<TransportSystem> ForkWork() => new ResumableWork<TransportSystem>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<TransportSystem> done)
        {
            var next = new TransportSystem(this);
            foreach (var pair in people) { next.people.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.people"; }
            foreach (var pair in vehicles) { next.vehicles.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.vehicles"; }
            foreach (var pair in transits) { next.transits.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.transits"; }
            foreach (var pair in transfers) { next.transfers.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.transfers"; }
            foreach (var pair in reservations) { next.reservations.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.reservations"; }
            foreach (var pair in orders) { next.orders.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.orders"; }
            foreach (var pair in queues) { next.queues.Add(pair.Key, pair.Value.Clone()); yield return "transport.fork.queues"; }
            foreach (var pair in held) { next.held.Add(pair.Key, pair.Value); yield return "transport.fork.held"; }
            foreach (var pair in milestones) { next.milestones.Add(pair.Key, pair.Value); yield return "transport.fork.milestones"; }
            foreach (var pair in nodeOccupancyPeaks) { next.nodeOccupancyPeaks.Add(pair.Key, pair.Value); yield return "transport.fork.nodeOccupancyPeaks"; }
            foreach (var value in boardedEver) { next.boardedEver.Add(value); yield return "transport.fork.boardedEver"; }
            foreach (var value in completedEdgePassages) { next.completedEdgePassages.Add(value); yield return "transport.fork.completedEdgePassages"; }
            foreach (var value in completedNodeDwells) { next.completedNodeDwells.Add(value); yield return "transport.fork.completedNodeDwells"; }
            done(next);
        }
        // Sequence is the accepted command's stable simulation identity. The
        // digest binds full resident/resource IDs and O without exceeding the
        // frozen 128-character identifier bound for long authored IDs.
        private static string StableId(string prefix, long sequence, params object[] fields) => prefix + ":" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + O(fields).Sha256();
        private static string Mode(Vehicle vehicle) => vehicle.Kind == "skiff" ? "water" : vehicle.Kind == "winch" ? "cable" : "ground";
        private static string VehicleChannel(Vehicle vehicle) => vehicle.Kind == "skiff" ? "dock_berths" : "vehicle_slots";
        private static bool Terminal(Person person) => person.State == "evacuated" || person.State == "awaiting_backup";
        public void ReleaseGroup(string groupId) { if (!held.ContainsKey(groupId)) throw new InvalidOperationException("Unknown held group"); held[groupId] = false; }
        public ValidationResult ValidateRoute(AssignRoute route)
        {
            if (route.SubjectKind == "group" ? !held.ContainsKey(route.SubjectId) : !vehicles.ContainsKey(route.SubjectId)) return new ValidationResult("UNKNOWN_TARGET");
            if (route.SubjectKind == "vehicle" && vehicles[route.SubjectId].Status == "failed") return new ValidationResult("ROUTE_BLOCKED");
            foreach (var stop in route.Stops)
            {
                if (!nodes.ContainsKey(stop.NodeId) || stop.GroupId != null && !held.ContainsKey(stop.GroupId)) return new ValidationResult("UNKNOWN_TARGET");
                if (stop.Action == "evacuate" && nodes[stop.NodeId].kind != "evacuation") return new ValidationResult("ROUTE_BLOCKED");
                if (route.SubjectKind == "group" && (stop.Action == "pickup" || stop.Action == "dropoff")) return new ValidationResult("ROUTE_BLOCKED");
            }
            return new ValidationResult("OK");
        }
        public void Assign(AssignRoute route, long operationTick, long sequence)
        {
            var check = ValidateRoute(route); if (!check.Allowed) throw new InvalidOperationException(check.ReasonCode);
            // A replacement changes future stops only; active legs/transfers keep
            // their existing reservations and precise locations.
            foreach (var old in orders.Values.Where(x => x.Kind == route.SubjectKind && x.Subject == route.SubjectId).ToArray()) orders.Remove(old.Id);
            var order = new Order { Id = "route:" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), Kind = route.SubjectKind, Subject = route.SubjectId, Tick = operationTick, Sequence = sequence, Stops = route.Stops.Select(x => new Stop(x.NodeId, x.Action, x.GroupId)).ToArray() };
            orders.Add(order.Id, order); if (route.SubjectKind == "vehicle") vehicles[route.SubjectId].RouteId = order.Id;
            foreach (var queue in queues.Values.Where(x => x.Subject == route.SubjectId || route.SubjectKind == "group" && people.ContainsKey(x.Subject) && people[x.Subject].Group == route.SubjectId).ToArray()) queues.Remove(queue.Id);
        }
        public void AddDeployedVehicle(VehicleSpawn spawn, string equipmentInstanceId) => AddDeployedVehicleWork(spawn, equipmentInstanceId).Drain();
        public ResumableWork<bool> AddDeployedVehicleWork(VehicleSpawn spawn, string equipmentInstanceId)
            => new ResumableWork<bool>(done => AddDeployedVehicleRoutine(spawn, equipmentInstanceId, done));
        private IEnumerable<string> AddDeployedVehicleRoutine(VehicleSpawn spawn, string equipmentInstanceId, Action<bool> done)
        {
            if (spawn.kind == "fixed_bus" || spawn.initial_occupant_ids.Count != 0 || spawn.initial_route_id != null || string.IsNullOrEmpty(equipmentInstanceId) || vehicles.Values.Any(x => x.EquipmentId == equipmentInstanceId) || vehicles.Count >= 32) throw new InvalidOperationException("Invalid deployed vehicle");
            var vehicle = new Vehicle { Id = spawn.vehicle_id, Kind = spawn.kind, Seats = spawn.seats, Slots = spawn.movement_slots, Node = spawn.node_id, Status = "idle", EquipmentId = equipmentInstanceId };
            if (!nodes.ContainsKey(vehicle.Node) || Available(vehicle.Node, VehicleChannel(vehicle)) < 1) throw new InvalidOperationException("No deployed vehicle berth/parking");
            // The caller owns the transaction/fork exactly as in the synchronous
            // API; validation is resumed on that private candidate, not skipped.
            vehicles.Add(vehicle.Id, vehicle); yield return "transport.deploy.vehicle";
            using (var validation = ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
            done(true);
        }
        // C05 calls this in the upcoming operation's safety/damage stage,
        // before stage 7. The outer OperationClock fork owns atomic rollback.
        // EquipmentBroken and its once-only loss metric remain C05's ownership;
        // this seam emits only the actual transport cancellations/backup events.
        public void BreakDeployedVehicle(string equipmentInstanceId, OperationContext context)
        {
            if (string.IsNullOrEmpty(equipmentInstanceId)) throw new ArgumentException("Exact deployed equipment binding required", nameof(equipmentInstanceId));
            var matches = vehicles.Values.Where(x => x.EquipmentId == equipmentInstanceId).ToArray();
            if (matches.Length != 1 || matches[0].Kind == "fixed_bus") throw new InvalidOperationException("Unknown/ambiguous deployed vehicle equipment binding");
            new Tick(context.OperationTick); new Tick(context.ScenarioTick);
            long precedingBoundary = lastMovementTick < 0 ? 0 : lastMovementTick;
            if (context.Events == null || context.ScenarioTick > context.OperationTick || context.OperationTick != precedingBoundary + 1 || lastDerivedTick != lastMovementTick) throw new InvalidOperationException("Vehicle break requires upcoming operation safety stage before movement");
            var vehicle = matches[0]; bool newlyFailed = vehicle.Status != "failed";
            // Cancel first: boarding people remain at their node, unloading
            // people remain aboard. Cancellation cannot clear a failed status.
            foreach (var transfer in transfers.Values.Where(x => x.Vehicle == vehicle.Id).ToArray()) CancelTransfer(transfer, context);
            if (vehicle.TransitId != null)
            {
                var transit = transits[vehicle.TransitId]; FailedTransit(transit, context);
                if (newlyFailed) Emit(context, "RouteBlocked", vehicle.Id, O("subject_id", vehicle.Id, "edge_id", transit.Edge, "reason_code", "UNSAFE_TARGET"));
            }
            else
            {
                vehicle.Status = "failed"; vehicle.Brake = 0;
                foreach (string residentId in vehicle.Occupants) MarkBackup(people[residentId], context);
            }
            ClearWait(vehicle.Id); ValidateBoundary();
        }
        private ResumableWork<bool> CheckFrameWork(OperationContext context, TransportFrame frame)
            => new ResumableWork<bool>(done => CheckFrameRoutine(context, frame, done));
        private IEnumerable<string> CheckFrameRoutine(OperationContext context, TransportFrame frame, Action<bool> done)
        {
            const string error = "Exact mandatory transport safety/power/exposure frame required";
            if (frame == null || frame.OperationTick != context.OperationTick || frame.Nodes.Count != nodes.Count ||
                frame.Edges.Count != edges.Count || frame.ExposedResidents.Count != people.Count) throw new InvalidOperationException(error);
            // Both maps have unique ordinal keys. Equal cardinality and exact
            // membership prove the same set without sorting whole frame arrays.
            foreach (string id in frame.Nodes.Keys)
            { if (!nodes.ContainsKey(id)) throw new InvalidOperationException(error); yield return "transport.frame.node-key"; }
            foreach (string id in frame.Edges.Keys)
            { if (!edges.ContainsKey(id)) throw new InvalidOperationException(error); yield return "transport.frame.edge-key"; }
            foreach (string id in frame.ExposedResidents.Keys)
            { if (!people.ContainsKey(id)) throw new InvalidOperationException(error); yield return "transport.frame.resident-key"; }
            done(true);
        }
        private long Duration(NavEdge edge, TransportFrame frame) => checked((checked(edge.base_operation_ticks * 1000) + frame.Edges[edge.id].SpeedPermille - 1) / frame.Edges[edge.id].SpeedPermille);
        private sealed class Path
        {
            public string Node, EdgeId;
            public long Ticks;
            public int EdgeCount;
            public Path Parent;
            // Binary ancestors belong to this immutable prefix. At most 31
            // entries for an int-sized path; no full route copy on extension.
            public Path[] Ancestors;
        }
        private sealed class PathHeapNode
        {
            public Path Value;
            public PathHeapNode Parent, Left, Right;
        }
        private sealed class PathHeap
        {
            public PathHeapNode Root;
            public int Count;
        }
        private static Path Ancestor(Path path, int level) => level < path.Ancestors.Length ? path.Ancestors[level] : null;
        private static string PathEdgeAt(Path path, int index)
        {
            // A fixed int bit width bounds this primitive even for long paths.
            int distance = path.EdgeCount - index - 1, level = 0;
            while (distance != 0)
            {
                if ((distance & 1) != 0) path = path.Ancestors[level];
                distance >>= 1; level++;
            }
            return path.EdgeId;
        }
        private static ResumableWork<Path> ExtendPathWork(Path parent, NavEdge edge, long ticks)
            => new ResumableWork<Path>(done => ExtendPathRoutine(parent, edge, ticks, done));
        private static IEnumerable<string> ExtendPathRoutine(Path parent, NavEdge edge, long ticks, Action<Path> done)
        {
            int count = checked(parent.EdgeCount + 1), levels = 1;
            for (int depth = count; depth > 1; depth >>= 1) levels++;
            var path = new Path { Node = edge.to_node_id, Ticks = ticks, EdgeId = edge.id,
                EdgeCount = count, Parent = parent, Ancestors = new Path[levels] };
            path.Ancestors[0] = parent;
            yield return "transport.path.prefix-allocate";
            for (int level = 1; level < levels; level++)
            {
                path.Ancestors[level] = Ancestor(path.Ancestors[level - 1], level - 1);
                yield return "transport.path.prefix-link";
            }
            done(path);
        }
        private static ResumableWork<int> ComparePathsWork(Path a, Path b)
            => new ResumableWork<int>(done => ComparePathsRoutine(a, b, done));
        private static IEnumerable<string> ComparePathsRoutine(Path a, Path b, Action<int> done)
        {
            int comparison = a.Ticks.CompareTo(b.Ticks);
            if (comparison == 0) comparison = a.EdgeCount.CompareTo(b.EdgeCount);
            yield return "transport.path.compare-cost";
            if (comparison != 0) { done(comparison); yield break; }
            if (!ReferenceEquals(a, b))
            {
                // Equal-length prefixes form a tree. Climb unequal ancestors to
                // the two edges immediately after their common prefix, without
                // hashing or replacing ordinal sequence comparison with node ID.
                var left = a; var right = b;
                for (int level = Math.Max(a.Ancestors.Length, b.Ancestors.Length) - 1; level >= 0; level--)
                {
                    var leftAncestor = Ancestor(left, level); var rightAncestor = Ancestor(right, level);
                    if (leftAncestor != null && rightAncestor != null && !ReferenceEquals(leftAncestor, rightAncestor))
                    { left = leftAncestor; right = rightAncestor; }
                    yield return "transport.path.compare-prefix";
                }
                comparison = StringComparer.Ordinal.Compare(left.EdgeId, right.EdgeId);
                yield return "transport.path.compare-edge";
                if (comparison != 0) { done(comparison); yield break; }
                // Equal IDs in structurally distinct prefixes are also handled
                // exactly. This preserves comparator equality/duplicate behavior
                // without requiring reference identity to prove equal sequences.
                for (int index = 0; index < a.EdgeCount; index++)
                {
                    comparison = StringComparer.Ordinal.Compare(PathEdgeAt(a, index), PathEdgeAt(b, index));
                    yield return "transport.path.compare-sequence";
                    if (comparison != 0) { done(comparison); yield break; }
                }
            }
            done(StringComparer.Ordinal.Compare(a.Node, b.Node));
            yield return "transport.path.compare-node";
        }
        private static int HighestPathBit(int position)
        {
            int bit = 1;
            while (bit <= position / 2) bit <<= 1;
            return bit;
        }
        private static ResumableWork<bool> PushPathWork(PathHeap heap, Path path)
            => new ResumableWork<bool>(done => PushPathRoutine(heap, path, done));
        private static IEnumerable<string> PushPathRoutine(PathHeap heap, Path path, Action<bool> done)
        {
            int position = checked(heap.Count + 1);
            var item = new PathHeapNode { Value = path };
            if (heap.Root == null) { heap.Root = item; heap.Count = position; done(true); yield return "transport.path.queue-insert"; yield break; }
            var parent = heap.Root;
            for (int bit = HighestPathBit(position) >> 1; bit > 1; bit >>= 1)
            {
                parent = (position & bit) == 0 ? parent.Left : parent.Right;
                yield return "transport.path.queue-locate";
            }
            item.Parent = parent;
            if ((position & 1) == 0) parent.Left = item; else parent.Right = item;
            heap.Count = position;
            yield return "transport.path.queue-insert";
            while (item.Parent != null)
            {
                int comparison;
                using (var work = ComparePathsWork(item.Value, item.Parent.Value))
                { while (work.MoveNext()) yield return work.Stage; comparison = work.Result; }
                if (comparison >= 0) break;
                var swap = item.Value; item.Value = item.Parent.Value; item.Parent.Value = swap;
                item = item.Parent;
                yield return "transport.path.queue-rise";
            }
            done(true);
        }
        private static ResumableWork<Path> PopPathWork(PathHeap heap)
            => new ResumableWork<Path>(done => PopPathRoutine(heap, done));
        private static IEnumerable<string> PopPathRoutine(PathHeap heap, Action<Path> done)
        {
            var minimum = heap.Root.Value;
            if (heap.Count == 1) { heap.Root = null; heap.Count = 0; done(minimum); yield return "transport.path.queue-remove"; yield break; }
            var last = heap.Root;
            for (int bit = HighestPathBit(heap.Count) >> 1; bit > 0; bit >>= 1)
            {
                last = (heap.Count & bit) == 0 ? last.Left : last.Right;
                yield return "transport.path.queue-locate";
            }
            heap.Root.Value = last.Value;
            if (ReferenceEquals(last.Parent.Left, last)) last.Parent.Left = null; else last.Parent.Right = null;
            heap.Count--;
            yield return "transport.path.queue-remove";
            var current = heap.Root;
            while (current.Left != null)
            {
                var least = current.Left;
                int comparison;
                if (current.Right != null)
                {
                    using (var work = ComparePathsWork(current.Right.Value, least.Value))
                    { while (work.MoveNext()) yield return work.Stage; comparison = work.Result; }
                    if (comparison < 0) least = current.Right;
                }
                using (var work = ComparePathsWork(least.Value, current.Value))
                { while (work.MoveNext()) yield return work.Stage; comparison = work.Result; }
                if (comparison >= 0) break;
                var swap = current.Value; current.Value = least.Value; least.Value = swap;
                current = least;
                yield return "transport.path.queue-sink";
            }
            done(minimum);
        }
        public ReadOnlyCollection<string> FindPath(string origin, string destination, string traversal, string mobility, long slots, TransportFrame frame) => FindPathWork(origin, destination, traversal, mobility, slots, frame).Drain();
        public ResumableWork<ReadOnlyCollection<string>> FindPathWork(string origin, string destination, string traversal, string mobility, long slots, TransportFrame frame) => new ResumableWork<ReadOnlyCollection<string>>(done => FindPathRoutine(origin, destination, traversal, mobility, slots, frame, done));
        private IEnumerable<string> FindPathRoutine(string origin, string destination, string traversal, string mobility, long slots, TransportFrame frame, Action<ReadOnlyCollection<string>> done)
        {
            if (!nodes.ContainsKey(origin) || !nodes.ContainsKey(destination) || !frame.Nodes[destination].Safe) { done(new List<string>().AsReadOnly()); yield break; }
            var frontier = new PathHeap();
            using (var push = PushPathWork(frontier, new Path { Node = origin, Ticks = 0, EdgeCount = 0, Ancestors = new Path[0] }))
            { while (push.MoveNext()) yield return push.Stage; }
            var best = new SortedSet<string>(StringComparer.Ordinal);
            // The transport owner and immutable frame are fixed for this search.
            // Capture closure membership once, one actual transit per work unit.
            var closedEdges = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var transit in transits.Values)
            { if (transit.Closure) closedEdges.Add(transit.Edge); yield return "transport.path.closure-index"; }
            while (frontier.Count > 0)
            {
                Path path;
                using (var pop = PopPathWork(frontier)) { while (pop.MoveNext()) yield return pop.Stage; path = pop.Result; }
                if (!best.Add(path.Node)) { yield return "transport.path.duplicate"; continue; }
                yield return "transport.path.settle";
                if (path.Node == destination)
                {
                    var result = new string[path.EdgeCount];
                    yield return "transport.path.result-allocate";
                    var cursor = path;
                    for (int index = result.Length - 1; index >= 0; index--)
                    { result[index] = cursor.EdgeId; cursor = cursor.Parent; yield return "transport.path.result-copy"; }
                    done(new ReadOnlyCollection<string>(result)); yield break;
                }
                if (!outgoingEdges.TryGetValue(path.Node, out var outgoing)) continue;
                foreach (var edge in outgoing)
                {
                    yield return "transport.path.outgoing-edge";
                    if (edge.traversal_class != traversal) continue;
                    var sample = frame.Edges[edge.id]; var end = nodes[edge.to_node_id];
                    long duration = Duration(edge, frame), arrival = checked(frame.OperationTick + path.Ticks + duration);
                    if (closedEdges.Contains(edge.id) || !sample.Open || !sample.Powered || edge.movement_capacity < slots || !frame.Nodes[end.id].Safe) continue;
                    if (mobility != null)
                    {
                        bool allowed = false;
                        foreach (string value in edge.mobility_classes)
                        { allowed = value == mobility; yield return "transport.path.edge-mobility"; if (allowed) break; }
                        if (!allowed) continue;
                        allowed = false;
                        foreach (string value in end.mobility_classes)
                        { allowed = value == mobility; yield return "transport.path.node-mobility"; if (allowed) break; }
                        if (!allowed || mobility == "step_free" && !edge.step_free) continue;
                    }
                    if (sample.KnownClosureOperationTick.HasValue && sample.KnownClosureOperationTick.Value <= arrival) continue;
                    if (best.Contains(end.id)) continue;
                    Path candidate;
                    using (var extend = ExtendPathWork(path, edge, checked(path.Ticks + duration)))
                    { while (extend.MoveNext()) yield return extend.Stage; candidate = extend.Result; }
                    using (var push = PushPathWork(frontier, candidate)) { while (push.MoveNext()) yield return push.Stage; }
                }
            }
            done(new List<string>().AsReadOnly());
        }
        private long Capacity(string node, string channel) => channel == "node_people" ? nodes[node].resident_capacity : channel == "dock_berths" ? nodes[node].dock_berths : nodes[node].vehicle_slots;
        private long Occupied(string node, string channel) => channel == "node_people" ? people.Values.LongCount(x => x.LocationKind == "node" && x.LocationId == node) : vehicles.Values.LongCount(x => x.Node == node && VehicleChannel(x) == channel);
        private long Available(string node, string channel) => checked(Capacity(node, channel) - Occupied(node, channel) - reservations.Values.Where(x => x.Resource == node && x.Channel == channel).Sum(x => x.Quantity));
        private string Reserve(string owner, string kind, string resource, string channel, long quantity, long tick)
        {
            long sequence = long.Parse(owner.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture); string id = StableId("reservation", sequence, "owner", owner, "kind", kind, "resource", resource, "operation_tick", tick); reservations.Add(id, new Reservation { Id = id, Owner = owner, Kind = kind, Resource = resource, Channel = channel, Quantity = quantity, Tick = tick }); return id;
        }
        private void Release(string owner) { foreach (var item in reservations.Values.Where(x => x.Owner == owner).ToArray()) reservations.Remove(item.Id); }
        private void Wait(string subject, string target, string kind, Order order, long requestTick)
        {
            var current = queues.Values.SingleOrDefault(x => x.Subject == subject);
            if (current == null) { string id = StableId("queue", order.Sequence, "subject", subject, "operation_tick", requestTick); queues.Add(id, new Waiting { Id = id, Subject = subject, Target = target, Kind = kind, Tick = requestTick, Sequence = order.Sequence }); }
            else { current.Target = target; current.Kind = kind; }
        }
        private void ClearWait(string subject) { foreach (var q in queues.Values.Where(x => x.Subject == subject).ToArray()) queues.Remove(q.Id); }
        private void Emit(OperationContext context, string kind, string subject, JsonValue payload) { if (context.Events == null) throw new InvalidOperationException("Transport events require the owned runtime event sink"); context.Events.Emit(kind, subject, null, null, payload); }
        private void TransferEvent(OperationContext c, string kind, Transfer t) => Emit(c, kind, t.Person, O("resident_id", t.Person, "vehicle_id", t.Vehicle, "node_id", t.Node, "direction", t.Direction));
        private void MarkBackup(Person p, OperationContext c)
        {
            if (Terminal(p)) return;
            if (p.LocationKind == "node" && p.EnteredScenario.HasValue) CompleteNodeDwell(p, c);
            p.State = "awaiting_backup"; ClearWait(p.Id);
            Emit(c, "ResidentAwaitingBackup", p.Id, O("resident_id", p.Id, "location", Location(p.LocationKind, p.LocationId)));
        }
        private void FailedTransit(Transit t, OperationContext c)
        {
            t.Mode = "backup"; t.Closure = true; Release(t.Id);
            if (t.MoverKind == "resident") MarkBackup(people[t.MoverId], c);
            else { var v = vehicles[t.MoverId]; v.Status = "failed"; v.Brake = 0; foreach (string id in v.Occupants) MarkBackup(people[id], c); ClearWait(v.Id); }
        }
        private void Return(Transit t, OperationContext c)
        {
            t.Closure = true; t.Mode = "return"; t.Return = t.Progress;
            if (t.MoverKind == "vehicle") { vehicles[t.MoverId].Status = "returning"; vehicles[t.MoverId].Brake = 0; }
            // Destination capacity is no longer needed; keep origin/edge until
            // the frozen same-edge return really arrives.
            foreach (var r in reservations.Values.Where(x => x.Owner == t.Id && x.Kind != "return_node" && x.Kind != "edge_slots").ToArray()) reservations.Remove(r.Id);
        }
        private void Arrive(Transit t, bool returning, OperationContext c)
        {
            string destination = returning ? t.Origin : t.Destination; string traversal = edges[t.Edge].traversal_class;
            if (t.MoverKind == "resident")
            {
                var p = people[t.MoverId]; p.LocationKind = "node"; p.LocationId = destination; p.State = "waiting"; p.EnteredScenario = c.ScenarioTick;
                ObserveNodeOccupancy(destination);
                if (!returning) { completedEdgePassages.Add(new CompletedEdgePassage(p.Id, t.Edge)); Evacuate(p, traversal, c); }
            }
            else
            {
                var v = vehicles[t.MoverId]; v.Node = destination; v.TransitId = null; v.Status = "idle"; v.Brake = 0;
                if (!returning) foreach (string residentId in v.Occupants) completedEdgePassages.Add(new CompletedEdgePassage(residentId, t.Edge));
            }
            Release(t.Id); transits.Remove(t.Id);
        }
        public void ResolveMovement(OperationContext c, TransportFrame frame) => ResolveMovementWork(c, frame).Drain();
        public ResumableWork<bool> ResolveMovementWork(OperationContext c, TransportFrame frame) => new ResumableWork<bool>(done => ResolveMovementRoutine(c, frame, done));
        private IEnumerable<string> ResolveMovementRoutine(OperationContext c, TransportFrame frame, Action<bool> done)
        {
            using (var check = CheckFrameWork(c, frame)) { while (check.MoveNext()) yield return check.Stage; }
            if (c.OperationTick <= lastMovementTick) throw new InvalidOperationException("Transport movement stage repeated/out of order");
            completedEdgePassages.Clear(); boundaryFacts = new List<CompletedEdgePassage>().AsReadOnly(); dockWaitFacts = new List<DockWaitSample>().AsReadOnly(); boundaryFactsTick = -1; lastMovementTick = c.OperationTick;
            completedNodeDwells.Clear(); nodeDwellFacts = new List<CompletedNodeDwell>().AsReadOnly();
            nodeOccupancyPeaks.Clear(); foreach (string nodeId in nodes.Keys) { yield return "transport.movement"; nodeOccupancyPeaks.Add(nodeId, Occupied(nodeId, "node_people")); }
            occupancyPeakFacts = new ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(StringComparer.Ordinal));
            // Existing recovery and movement. New admissions happen only below.
            foreach (var t in transits.Values.ToArray())
            {yield return "transport.movement"; 
                if (t.Mode == "backup") continue;
                var sample = frame.Edges[t.Edge]; var v = t.MoverKind == "vehicle" ? vehicles[t.MoverId] : null;
                if (t.Mode == "forward" && (!sample.Open || !frame.Nodes[t.Destination].Safe))
                {
                    t.Closure = true;
                    if (!frame.Nodes[t.Destination].Safe)
                    {
                        if (frame.Nodes[t.Origin].Safe) Return(t, c); else { FailedTransit(t, c); continue; }
                    }
                }
                if (t.Mode == "forward" && v != null && v.Kind == "winch" && !sample.Powered)
                {
                    if (v.Status != "braked") { v.Status = "braked"; v.Brake = 20; }
                    if (v.Brake > 0) { v.Brake--; continue; }
                    if (frame.Nodes[t.Origin].Safe) Return(t, c); else { FailedTransit(t, c); continue; }
                }
                if (t.Mode == "return")
                {
                    if (!frame.Nodes[t.Origin].Safe) { FailedTransit(t, c); continue; }
                    if (t.Return > 0) { t.Return--; t.Progress--; }
                    if (t.Return == 0) Arrive(t, true, c);
                }
                else
                {
                    if (v != null) { v.Status = "travelling"; v.Brake = 0; if (v.Kind == "winch") { v.Duty = checked(v.Duty + 1); if (v.Duty >= 100) v.Rest = 30; } }
                    t.Progress = checked(t.Progress + 1); if (t.Progress == t.Duration) Arrive(t, false, c);
                }
            }
            foreach (var transfer in transfers.Values.ToArray())
            {yield return "transport.movement"; 
                var node = frame.Nodes[transfer.Node]; var v = vehicles[transfer.Vehicle]; var p = people[transfer.Person];
                if (v.Status == "failed" || !node.Safe || !node.DockOpen || v.Node != transfer.Node || Terminal(p)) { CancelTransfer(transfer, c); continue; }
                if (!node.Powered) { v.Status = "waiting_power"; continue; }
                v.Status = transfer.Direction == "board" ? "boarding" : "unboarding";
                transfer.Progress++; if (transfer.Progress < 10) continue;
                if (transfer.Direction == "board") { CompleteNodeDwell(p, c); p.LocationKind = "vehicle"; p.LocationId = v.Id; p.State = "in_transit"; p.EnteredScenario = null; v.Occupants.Add(p.Id); boardedEver.Add(p.Id); }
                else { v.Occupants.Remove(p.Id); p.LocationKind = "node"; p.LocationId = transfer.Node; p.State = "waiting"; p.EnteredScenario = c.ScenarioTick; ObserveNodeOccupancy(transfer.Node); Evacuate(p, Mode(v), c); }
                Release(transfer.Id); transfers.Remove(transfer.Id); v.Status = "idle"; TransferEvent(c, "TransferCompleted", transfer);
            }
            foreach (var v in vehicles.Values.Where(x => x.Status != "failed" && x.Node != null && x.Kind == "winch" && x.Rest > 0))
                { yield return "transport.movement"; if (frame.Nodes[v.Node].Safe && v.Occupants.Count == 0 && !transfers.Values.Any(x => x.Vehicle == v.Id)) { v.Status = "resting"; if (--v.Rest == 0) { v.Duty = 0; v.Status = "idle"; } } }
            var candidates = orders.Values.SelectMany(order => order.Kind == "vehicle" ? new[] { Tuple.Create(order, order.Subject) } : people.Values.Where(p => p.Group == order.Subject && !Terminal(p)).Select(p => Tuple.Create(order, p.Id))).OrderBy(x => queues.Values.SingleOrDefault(q => q.Subject == x.Item2)?.Tick ?? c.OperationTick).ThenBy(x => queues.Values.SingleOrDefault(q => q.Subject == x.Item2)?.Sequence ?? x.Item1.Sequence).ThenBy(x => x.Item2, StringComparer.Ordinal).ToArray();
            foreach (var candidate in candidates)
            {yield return "transport.movement"; 
                var order = candidate.Item1; if (order.Next >= order.Stops.Length) { ClearWait(candidate.Item2); continue; }
                if (order.Kind == "vehicle") using (var admission = AdmitVehicleWork(vehicles[candidate.Item2], order, c, frame)) { while (admission.MoveNext()) yield return admission.Stage; }
                else using (var admission = AdmitPersonWork(people[candidate.Item2], order, c, frame)) { while (admission.MoveNext()) yield return admission.Stage; }
            }
            using (var validation = ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
        
            done(true);
        }
        private void CancelTransfer(Transfer t, OperationContext c)
        {
            var p = people[t.Person]; if (!Terminal(p)) p.State = t.Direction == "board" ? "waiting" : "in_transit";
            Release(t.Id); transfers.Remove(t.Id); if (vehicles[t.Vehicle].Status != "failed") vehicles[t.Vehicle].Status = "idle"; TransferEvent(c, "TransferCancelled", t);
        }
        private void AdmitPerson(Person p, Order order, OperationContext c, TransportFrame frame) => AdmitPersonWork(p, order, c, frame).Drain();
        private ResumableWork<bool> AdmitPersonWork(Person p, Order order, OperationContext c, TransportFrame frame) => new ResumableWork<bool>(done => AdmitPersonRoutine(p, order, c, frame, done));
        private IEnumerable<string> AdmitPersonRoutine(Person p, Order order, OperationContext c, TransportFrame frame, Action<bool> done)
        {
            if (Terminal(p) || held[p.Group] || p.LocationKind != "node" || transfers.Values.Any(x => x.Person == p.Id)) { done(true); yield break; }
            var stop = order.Stops[order.Next];
            if (p.LocationId == stop.Node)
            {
                Evacuate(p, "ground", c);
                if (people.Values.Where(x => x.Group == p.Group && !Terminal(x)).All(x => x.LocationKind == "node" && x.LocationId == stop.Node)) order.Next++;
                ClearWait(p.Id); { done(true); yield break; }
            }
            using (var leg = AdmitLegWork("resident", p.Id, p.LocationId, stop.Node, "ground", p.Mobility, 1, "node_people", order, c, frame)) { while (leg.MoveNext()) yield return leg.Stage; }
        
            done(true);
        }
        private void AdmitVehicle(Vehicle v, Order order, OperationContext c, TransportFrame frame) => AdmitVehicleWork(v, order, c, frame).Drain();
        private ResumableWork<bool> AdmitVehicleWork(Vehicle v, Order order, OperationContext c, TransportFrame frame) => new ResumableWork<bool>(done => AdmitVehicleRoutine(v, order, c, frame, done));
        private IEnumerable<string> AdmitVehicleRoutine(Vehicle v, Order order, OperationContext c, TransportFrame frame, Action<bool> done)
        {
            if (v.Node == null || v.Status == "failed" || transfers.Values.Any(x => x.Vehicle == v.Id)) { done(true); yield break; }
            var stop = order.Stops[order.Next];
            if (v.Node == stop.Node)
            {
                if (stop.Action == "pickup" && v.Rest > 0) { v.Status = "resting"; { done(true); yield break; } }
                string direction = stop.Action == "pickup" ? "board" : stop.Action == "dropoff" || stop.Action == "evacuate" ? "unboard" : null;
                var person = direction == "board" ? people.Values.FirstOrDefault(x => !Terminal(x) && !held[x.Group] && x.LocationKind == "node" && x.LocationId == v.Node && !transfers.Values.Any(t => t.Person == x.Id) && (stop.Group == null || x.Group == stop.Group)) : direction == "unboard" ? v.Occupants.Select(x => people[x]).FirstOrDefault(x => !Terminal(x) && (stop.Group == null || x.Group == stop.Group)) : null;
                if (person != null && (direction != "board" || v.Occupants.Count < v.Seats)) { StartTransfer(person, v, direction, order, c, frame); { done(true); yield break; } }
                order.Next++; ClearWait(v.Id); { done(true); yield break; }
            }
            if (v.Rest > 0) { v.Status = "resting"; { done(true); yield break; } }
            using (var leg = AdmitLegWork("vehicle", v.Id, v.Node, stop.Node, Mode(v), null, v.Slots, VehicleChannel(v), order, c, frame)) { while (leg.MoveNext()) yield return leg.Stage; }
        
            done(true);
        }
        private void AdmitLeg(string moverKind, string id, string origin, string checkpoint, string traversal, string mobility, long slots, string channel, Order order, OperationContext c, TransportFrame frame) => AdmitLegWork(moverKind, id, origin, checkpoint, traversal, mobility, slots, channel, order, c, frame).Drain();
        private ResumableWork<bool> AdmitLegWork(string moverKind, string id, string origin, string checkpoint, string traversal, string mobility, long slots, string channel, Order order, OperationContext c, TransportFrame frame) => new ResumableWork<bool>(done => AdmitLegRoutine(moverKind, id, origin, checkpoint, traversal, mobility, slots, channel, order, c, frame, done));
        private IEnumerable<string> AdmitLegRoutine(string moverKind, string id, string origin, string checkpoint, string traversal, string mobility, long slots, string channel, Order order, OperationContext c, TransportFrame frame, Action<bool> done)
        {
            ReadOnlyCollection<string> path; using (var workpath = FindPathWork(origin, checkpoint, traversal, mobility, slots, frame)) { while (workpath.MoveNext()) yield return workpath.Stage; path = workpath.Result; }
            var resolvedEdges = new string[path.Count];
            yield return "transport.path.order-allocate";
            for (int index = 0; index < path.Count; index++)
            { resolvedEdges[index] = path[index]; yield return "transport.path.order-copy"; }
            order.Edges = resolvedEdges;
            if (path.Count == 0) { var blockedEdge = edges.Values.FirstOrDefault(x => x.from_node_id == origin && x.traversal_class == traversal); if (blockedEdge != null && !queues.Values.Any(x => x.Subject == id)) Emit(c, "RouteBlocked", id, O("subject_id", id, "edge_id", blockedEdge.id, "reason_code", "ROUTE_BLOCKED")); Wait(id, checkpoint, "edge", order, c.OperationTick); if (moverKind == "vehicle") vehicles[id].Status = "waiting_route"; { done(true); yield break; } }
            var edge = edges[path[0]];
            if (reservations.Values.Where(x => x.Kind == "edge_slots" && x.Resource == edge.id).Sum(x => x.Quantity) + slots > edge.movement_capacity) { Wait(id, checkpoint, "edge", order, c.OperationTick); { done(true); yield break; } }
            if (Available(edge.to_node_id, channel) < 1) { Wait(id, edge.to_node_id, channel == "dock_berths" ? "berth" : "capacity", order, c.OperationTick); { done(true); yield break; } }
            // Current origin occupancy is converted, atomically, into its return
            // reservation. Reserving it a second time would double-count people.
            var t = new Transit { Id = StableId("transit", order.Sequence, "mover", id, "edge", edge.id, "operation_tick", c.OperationTick), MoverKind = moverKind, MoverId = id, Edge = edge.id, Origin = origin, Destination = edge.to_node_id, Duration = Duration(edge, frame) };
            transits.Add(t.Id, t); Reserve(t.Id, "edge_slots", edge.id, "edge_slots", slots, c.OperationTick); Reserve(t.Id, "return_node", origin, channel, 1, c.OperationTick); Reserve(t.Id, channel == "node_people" ? "node_people" : "dock_berth", edge.to_node_id, channel, 1, c.OperationTick);
            if (moverKind == "resident") { var p = people[id]; CompleteNodeDwell(p, c); p.LocationKind = "edge"; p.LocationId = t.Id; p.State = "walking"; p.EnteredScenario = null; }
            else { var v = vehicles[id]; v.Node = null; v.TransitId = t.Id; v.Status = "travelling"; }
            ClearWait(id);
        
            done(true);
        }
        private void CompleteNodeDwell(Person p, OperationContext context)
        {
            if (p.LocationKind != "node" || p.State != "waiting" && p.State != "boarding" || !p.EnteredScenario.HasValue || context.ScenarioTick < p.EnteredScenario.Value) throw new InvalidOperationException("Node departure requires a valid waiting/boarding scenario dwell");
            completedNodeDwells.Add(new CompletedNodeDwell(p.Id, p.LocationId, checked(context.ScenarioTick - p.EnteredScenario.Value)));
        }
        private void ObserveNodeOccupancy(string nodeId)
        {
            nodeOccupancyPeaks[nodeId] = Math.Max(nodeOccupancyPeaks[nodeId], Occupied(nodeId, "node_people"));
        }
        private void StartTransfer(Person p, Vehicle v, string direction, Order order, OperationContext c, TransportFrame frame)
        {
            var node = frame.Nodes[v.Node];
            if (!node.Safe || !node.DockOpen) { Wait(v.Id, v.Node, "berth", order, c.OperationTick); return; }
            if (!node.Powered) { v.Status = "waiting_power"; Wait(v.Id, v.Node, "power", order, c.OperationTick); return; }
            if (!nodes[v.Node].mobility_classes.Contains(p.Mobility)) { Wait(v.Id, v.Node, "capacity", order, c.OperationTick); return; }
            if (direction == "unboard" && Available(v.Node, "node_people") < 1) { Wait(v.Id, v.Node, "capacity", order, c.OperationTick); return; }
            if (direction == "board" && v.Occupants.Count + reservations.Values.Where(x => x.Resource == v.Id && x.Kind == "vehicle_seat").Sum(x => x.Quantity) >= v.Seats) { Wait(v.Id, v.Node, "capacity", order, c.OperationTick); return; }
            var t = new Transfer { Id = StableId("transfer", order.Sequence, "resident", p.Id, "vehicle", v.Id, "direction", direction, "operation_tick", c.OperationTick), Person = p.Id, Vehicle = v.Id, Node = v.Node, Direction = direction };
            // On unloading the occupied seat itself is retained; the required
            // seat_reservation_id names this transfer's retained-seat record,
            // excluded from free-seat arithmetic because occupancy already owns it.
            t.SeatReservation = Reserve(t.Id, "vehicle_seat", v.Id, direction == "board" ? "vehicle_seat" : "retained_seat", 1, c.OperationTick);
            if (direction == "unboard") t.NodeReservation = Reserve(t.Id, "node_people", v.Node, "node_people", 1, c.OperationTick);
            transfers.Add(t.Id, t); p.State = direction == "board" ? "boarding" : "disembarking"; v.Status = direction == "board" ? "boarding" : "unboarding"; ClearWait(v.Id);
            if (direction == "unboard" && nodes[v.Node].kind == "evacuation" && people.Values.Where(x => x.State != "evacuated").All(x => x.State != "awaiting_backup" && x.LocationKind == "vehicle" && VehicleHasFinalStop(vehicles[x.LocationId]))) LatchKind("final_unload_started", null, c);
            TransferEvent(c, "TransferStarted", t);
        }
        private bool VehicleHasFinalStop(Vehicle v) => v.RouteId != null && orders.ContainsKey(v.RouteId) && orders[v.RouteId].Stops.Skip(orders[v.RouteId].Next).Any(x => x.Action == "evacuate" && nodes[x.Node].kind == "evacuation");
        private void Evacuate(Person p, string finalMode, OperationContext c)
        {
            if (Terminal(p) || p.LocationKind != "node" || nodes[p.LocationId].kind != "evacuation") return;
            if (p.EnteredScenario.HasValue) CompleteNodeDwell(p, c);
            p.State = "evacuated"; p.FinalMode = finalMode; p.Exposure = 0; ClearWait(p.Id);
            Emit(c, "ResidentEvacuated", p.Id, O("resident_id", p.Id, "node_id", p.LocationId, "final_leg_mode", finalMode));
        }
        private void LatchKind(string kind, string target, OperationContext c)
        {
            foreach (var def in mission.milestones.Where(x => x.kind == kind && (target == null || x.target == target)))
                if (!milestones.ContainsKey(def.id)) { milestones.Add(def.id, O("id", def.id, "operation_tick", c.OperationTick, "scenario_tick", c.ScenarioTick)); Emit(c, "MilestoneReached", def.id, O("milestone_id", def.id)); }
        }
        public void Derive(OperationContext c, TransportFrame frame) => DeriveWork(c, frame).Drain();
        public ResumableWork<bool> DeriveWork(OperationContext c, TransportFrame frame) => new ResumableWork<bool>(done => DeriveRoutine(c, frame, done));
        private IEnumerable<string> DeriveRoutine(OperationContext c, TransportFrame frame, Action<bool> done)
        {
            using (var check = CheckFrameWork(c, frame)) { while (check.MoveNext()) yield return check.Stage; }
            if (lastMovementTick != c.OperationTick || lastDerivedTick >= c.OperationTick) throw new InvalidOperationException("Transport derivation must follow movement exactly once"); lastDerivedTick = c.OperationTick;
            foreach (var p in people.Values)
            {yield return "transport.derive"; 
                if (Terminal(p)) continue;
                if (!frame.ExposedResidents[p.Id]) p.Exposure = 0;
                else if (c.HazardPulse && ++p.Exposure >= 20)
                {
                    foreach (var t in transfers.Values.Where(x => x.Person == p.Id).ToArray()) { yield return "transport.derive"; CancelTransfer(t, c); }
                    if (p.LocationKind == "edge") FailedTransit(transits[p.LocationId], c);
                    else if (p.LocationKind == "vehicle")
                    {
                        var v = vehicles[p.LocationId];
                        if (v.TransitId != null) FailedTransit(transits[v.TransitId], c);
                        else { v.Status = "failed"; MarkBackup(p, c); ClearWait(v.Id); }
                    }
                    else MarkBackup(p, c);
                }
            }
            foreach (var group in mission.groups) { yield return "transport.derive"; if (group.resident_ids.All(boardedEver.Contains)) LatchKind("group_boarded", group.id, c); }
            if (EvacuatedCount == Population) LatchKind("all_evacuated", null, c);
            using (var validation = ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
            boundaryFacts = completedEdgePassages.OrderBy(x => x.ResidentId, StringComparer.Ordinal).ThenBy(x => x.EdgeId, StringComparer.Ordinal).ToList().AsReadOnly();
            ReadOnlyCollection<DockWaitSample> dockFacts; using (var workdockFacts = ObserveDockWaitsWork(frame)) { while (workdockFacts.MoveNext()) yield return workdockFacts.Stage; dockFacts = workdockFacts.Result; } dockWaitFacts = dockFacts;
            nodeDwellFacts = completedNodeDwells.OrderBy(x => x.ResidentId, StringComparer.Ordinal).ThenBy(x => x.NodeId, StringComparer.Ordinal).ToList().AsReadOnly();
            occupancyPeakFacts = new ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(nodeOccupancyPeaks, StringComparer.Ordinal));
            boundaryFactsTick = c.OperationTick;
        
            done(true);
        }
        // Read once or repeatedly without draining/mutating the observation.
        // C06 must merge these facts into its existing distinct MetricState
        // ledger during stage 8, before the complete boundary is published.
        public ReadOnlyCollection<CompletedEdgePassage> ReadBoundaryFacts(long operationTick)
        {
            RequireBoundaryFacts(operationTick);
            return boundaryFacts;
        }
        public ReadOnlyCollection<DockWaitSample> ReadDockWaitFacts(long operationTick)
        {
            RequireBoundaryFacts(operationTick);
            return dockWaitFacts;
        }
        public ReadOnlyCollection<CompletedNodeDwell> ReadCompletedNodeDwells(long operationTick)
        {
            RequireBoundaryFacts(operationTick);
            return nodeDwellFacts;
        }
        public ReadOnlyDictionary<string, long> ReadNodeOccupancyPeaks(long operationTick)
        {
            RequireBoundaryFacts(operationTick);
            return occupancyPeakFacts;
        }
        // Pure saved-boundary eligibility inspection for C07 reconstruction.
        // This cannot recreate or authorize any discarded interval observation.
        public ReadOnlyCollection<DockWaitSample> InspectDockWaitFacts(OperationContext context, TransportFrame frame) => InspectDockWaitFactsWork(context, frame).Drain();
        public ResumableWork<ReadOnlyCollection<DockWaitSample>> InspectDockWaitFactsWork(OperationContext context, TransportFrame frame) => new ResumableWork<ReadOnlyCollection<DockWaitSample>>(done => InspectDockWaitFactsRoutine(context, frame, done));
        private IEnumerable<string> InspectDockWaitFactsRoutine(OperationContext context, TransportFrame frame, Action<ReadOnlyCollection<DockWaitSample>> done)
        {
            new Tick(context.OperationTick); new Tick(context.ScenarioTick);
            if (context.ScenarioTick > context.OperationTick || lastMovementTick != context.OperationTick || lastDerivedTick != context.OperationTick) throw new InvalidOperationException("Dock inspection requires the matching complete transport cursor");
            using (var check = CheckFrameWork(context, frame)) { while (check.MoveNext()) yield return check.Stage; }
            ReadOnlyCollection<DockWaitSample> facts; using (var workfacts = ObserveDockWaitsWork(frame)) { while (workfacts.MoveNext()) yield return workfacts.Stage; facts = workfacts.Result; } done(facts);
                }
        private void RequireBoundaryFacts(long operationTick)
        {
            new Tick(operationTick);
            if (boundaryFactsTick != operationTick || lastMovementTick != operationTick || lastDerivedTick != operationTick) throw new InvalidOperationException("Transport facts require an actual freshly completed matching boundary");
        }
        private ReadOnlyCollection<DockWaitSample> ObserveDockWaits(TransportFrame frame) => ObserveDockWaitsWork(frame).Drain();
        private ResumableWork<ReadOnlyCollection<DockWaitSample>> ObserveDockWaitsWork(TransportFrame frame) => new ResumableWork<ReadOnlyCollection<DockWaitSample>>(done => ObserveDockWaitsRoutine(frame, done));
        private IEnumerable<string> ObserveDockWaitsRoutine(TransportFrame frame, Action<ReadOnlyCollection<DockWaitSample>> done)
        {
            var facts = new List<DockWaitSample>();
            foreach (var v in vehicles.Values)
            {yield return "transport.observe.dock"; 
                if (v.Node == null || v.TransitId != null || v.Status == "failed" || v.Rest > 0 || v.Status == "resting" || transfers.Values.Any(x => x.Vehicle == v.Id) || !frame.Nodes[v.Node].Safe || v.RouteId == null || !orders.ContainsKey(v.RouteId)) continue;
                var order = orders[v.RouteId]; var chosen = order.Stops.Skip(order.Next).FirstOrDefault(x => x.Action == "dropoff" || x.Action == "evacuate");
                if (chosen == null || !ReferenceEquals(order.Stops[order.Next], chosen)) continue;
                var queue = queues.Values.SingleOrDefault(x => x.Subject == v.Id);
                if (queue == null || queue.Target != chosen.Node || queue.Sequence != order.Sequence) continue;
                bool closed = !frame.Nodes[chosen.Node].DockOpen;
                if (queue.Kind != "berth" && queue.Kind != "capacity" && !(closed && queue.Kind == "edge")) continue;
                bool atHolding = v.Node == chosen.Node || edges.Values.Any(e => e.from_node_id == v.Node && e.to_node_id == chosen.Node && e.traversal_class == Mode(v) && e.movement_capacity >= v.Slots);
                if (atHolding) facts.Add(new DockWaitSample(chosen.Node, v.Id, closed, v.Occupants.Count != 0, queue.Kind == "berth" || queue.Kind == "capacity"));
            }
            done(facts.OrderBy(x => x.NodeId, StringComparer.Ordinal).ThenBy(x => x.VehicleId, StringComparer.Ordinal).ToList().AsReadOnly());
                }
        public void SampleInitialMilestones(OperationContext context) => SampleInitialMilestonesWork(context).Drain();
        public ResumableWork<bool> SampleInitialMilestonesWork(OperationContext context) => new ResumableWork<bool>(done => SampleInitialMilestonesRoutine(context, done));
        private IEnumerable<string> SampleInitialMilestonesRoutine(OperationContext context, Action<bool> done)
        {
            if (context.OperationTick != 0 || context.ScenarioTick != 0) throw new InvalidOperationException("Initial transport milestone sample requires O=S=0");
            foreach (var group in mission.groups) { yield return "transport.initial"; if (group.resident_ids.All(boardedEver.Contains)) LatchKind("group_boarded", group.id, context); }
        
            done(true);
        }
        public JsonValue HistoricalMetrics => HistoricalMetricsWork().Drain();
        public ResumableWork<JsonValue> HistoricalMetricsWork() => DomainWork.Rows(mission.milestones.Where(x => x.kind == "group_boarded").Select(x => x.target).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal), group =>
        {
            var ids = boardedEver.Where(id => people[id].Group == group).ToArray();
            return O("id", "transport:boarding:" + O("mission_version_id", mission.mission_version_id, "group_id", group).Sha256(), "metric", "group_boarded", "subject_id", group, "value", ids.Length, "distinct_resident_ids", Strings(ids));
        }, "transport.read.history");
        public bool GroupAtNode(string groupId, string nodeId) => people.Values.Where(x => x.Group == groupId).Any() && people.Values.Where(x => x.Group == groupId).All(x => x.LocationKind == "node" && x.LocationId == nodeId && x.State != "awaiting_backup");
        public void ValidateBoundary() => ValidateBoundaryWork().Drain();
        public ResumableWork<bool> ValidateBoundaryWork() => new ResumableWork<bool>(done => ValidateBoundaryRoutine(done));
        private IEnumerable<string> ValidateBoundaryRoutine(Action<bool> done)
        {
            if (people.Count != mission.total_residents || people.Count > 256 || vehicles.Count > 32 || queues.Count > people.Count + vehicles.Count) throw new InvalidOperationException("Transport population/bounds invariant");
            // Sequence-zero orders are compiler-bound initial named routes,
            // never user commands. Their waypoint assemble stops are legal wire
            // RouteStop values but cannot be manufactured through vehicle input.
            foreach (var order in orders.Values.Where(x => x.Sequence == 0))
            {yield return "transport.validate"; 
                var spawn = layout.fixed_vehicles.SingleOrDefault(x => x.vehicle_id == order.Subject);
                if (order.Kind != "vehicle" || spawn == null || spawn.initial_route_id == null) throw new InvalidOperationException("Unbound initial transport order");
                var named = layout.routes.Single(x => x.id == spawn.initial_route_id);
                var expected = named.required_waypoint_ids.Select(x => new Stop(x, "assemble", null)).Concat(new[] { new Stop(named.to_node_id, nodes[named.to_node_id].kind == "evacuation" ? "evacuate" : "dropoff", null) }).ToArray();
                if (order.Id != StableId("route", 0, "vehicle", spawn.vehicle_id, "named_route", named.id) || order.Tick != 0 || order.Next < 0 || order.Next > expected.Length || order.Stops.Length != expected.Length || order.Stops.Where((stop, index) => stop.Json().Canonical() != expected[index].Json().Canonical()).Any()) throw new InvalidOperationException("Initial named-route waypoint binding mismatch");
            }
            foreach (var p in people.Values)
            {yield return "transport.validate"; 
                if (p.LocationKind == "node" && !nodes.ContainsKey(p.LocationId) || p.LocationKind == "edge" && (!transits.ContainsKey(p.LocationId) || transits[p.LocationId].MoverId != p.Id) || p.LocationKind == "vehicle" && (!vehicles.ContainsKey(p.LocationId) || !vehicles[p.LocationId].Occupants.Contains(p.Id))) throw new InvalidOperationException("Resident location invariant");
                if (p.State == "evacuated" && (p.LocationKind != "node" || nodes[p.LocationId].kind != "evacuation" || p.FinalMode == null)) throw new InvalidOperationException("Evacuated location invariant");
            }
            foreach (var v in vehicles.Values)
            {yield return "transport.validate"; 
                if (v.Occupants.Count + reservations.Values.Where(x => x.Resource == v.Id && x.Channel == "vehicle_seat").Sum(x => x.Quantity) > v.Seats || v.Occupants.Any(x => !people.ContainsKey(x) || people[x].LocationKind != "vehicle" || people[x].LocationId != v.Id)) throw new InvalidOperationException("Vehicle seat invariant");
                if ((v.Node == null) == (v.TransitId == null) || v.TransitId != null && (!transits.ContainsKey(v.TransitId) || transits[v.TransitId].MoverId != v.Id)) throw new InvalidOperationException("Vehicle location invariant");
            }
            foreach (string node in nodes.Keys) { yield return "transport.validate"; foreach (string channel in new[] { "node_people", "vehicle_slots", "dock_berths" }) { yield return "transport.validate"; if (Available(node, channel) < 0) throw new InvalidOperationException("Transport capacity invariant: " + node + ":" + channel); } }
            foreach (var edge in edges.Values) { yield return "transport.validate"; if (reservations.Values.Where(x => x.Kind == "edge_slots" && x.Resource == edge.id).Sum(x => x.Quantity) > edge.movement_capacity) throw new InvalidOperationException("Edge slot invariant"); }
            foreach (var r in reservations.Values) { yield return "transport.validate"; if (r.Quantity <= 0 || !transits.ContainsKey(r.Owner) && !transfers.ContainsKey(r.Owner)) throw new InvalidOperationException("Orphan reservation"); }
            if (transfers.Values.GroupBy(t => t.Person).Any(g => g.Count() > 1) || transfers.Values.GroupBy(t => t.Vehicle).Any(g => g.Count() > 1)) throw new InvalidOperationException("Concurrent passenger transfer invariant");
            foreach (var t in transfers.Values) { yield return "transport.validate"; if (!reservations.ContainsKey(t.SeatReservation) || t.NodeReservation != null && !reservations.ContainsKey(t.NodeReservation) || t.Progress < 0 || t.Progress > 9) throw new InvalidOperationException("Transfer reservation invariant"); }
        
            done(true);
        }
        // Reconstruct a new authority from the frozen Snapshot, never from the
        // receiver's current mutable state. Content supplies nonserialized
        // mobility/seats/slots; saved owners supply every continuation value.
        // Other domain owners still validate their own rows and actual frame.
        public TransportSystem Restore(JsonValue snapshot) => RestoreWork(snapshot).Drain();
        public ResumableWork<TransportSystem> RestoreWork(JsonValue snapshot) => new ResumableWork<TransportSystem>(done => RestoreRoutine(snapshot, done));
        private IEnumerable<string> RestoreRoutine(JsonValue snapshot, Action<TransportSystem> done)
        {
            RestoreFields(snapshot, "schema_version", "identity", "state_revision", "phase", "operation_tick", "scenario_tick", "scenario_accumulator", "scenario_enabled", "guidance_state", "rng", "supplies_remaining", "selected_stock", "deployments", "water", "rate_remainders", "power", "equipment", "vehicles", "residents", "transits", "transfers", "reservations", "routes", "queues", "fire", "supports", "runouts", "tides", "lava", "stores", "producers", "event_state", "milestones", "metrics", "objective_monitors", "last_admitted_sequence", "pending_commands", "command_receipts", "recent_events", "water_ledger", "end_request", "outcome");
            RestoreNumber(snapshot["schema_version"], 1, 1); RunIdentity.Validate(snapshot["identity"]);
            RequireRestore(snapshot["identity"]["mission_version_id"].Text == mission.mission_version_id && snapshot["identity"]["layout_version_id"].Text == layout.id && snapshot["identity"]["rules_version"].Text == mission.rules_version, "compiled mission/layout identity");
            long operation = RestoreNumber(snapshot["operation_tick"], 0, Tick.Maximum), scenario = RestoreNumber(snapshot["scenario_tick"], 0, operation);
            long lastSequence = RestoreNumber(snapshot["last_admitted_sequence"], 0, 10000);
            Dictionary<long, JsonValue> receipts; using (var workreceipts = RestoreAcceptedReceiptsWork(snapshot["command_receipts"], lastSequence, operation)) { while (workreceipts.MoveNext()) yield return workreceipts.Stage; receipts = workreceipts.Result; }
            // Only immutable compiled indexes are reused. Every initial owner
            // and its validation are rebuilt by the same yielded constructor work.
            var next = new TransportSystem(this, true);
            using (var initial = next.InitializePopulationWork()) { while (initial.MoveNext()) yield return initial.Stage; }
            JsonValue initialState = null; if (operation == 0) { using (var work = next.ReadStateWork()) { while (work.MoveNext()) yield return work.Stage; initialState = work.Result; } }
            next.people.Clear(); next.vehicles.Clear(); next.transits.Clear(); next.transfers.Clear(); next.reservations.Clear(); next.orders.Clear(); next.queues.Clear(); next.held.Clear(); next.boardedEver.Clear(); next.milestones.Clear();
            var residentReservations = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var spawns = layout.spawns.ToDictionary(x => x.resident_id, StringComparer.Ordinal);
            foreach (var row in RestoreRows(snapshot["residents"], 256))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "group_id", "state", "location", "reservation_ids", "node_entered_scenario_tick", "consecutive_exposure_ticks", "final_leg_mode");
                string id = RestoreId(row["id"]), group = RestoreId(row["group_id"]);
                RequireRestore(spawns.TryGetValue(id, out var spawn) && group == spawn.group_id, "compiled resident/group identity");
                var location = RestoreLocation(row["location"], true);
                var p = new Person { Id = id, Group = group, Mobility = spawn.mobility_class, State = RestoreOne(row["state"], "sheltered", "waiting", "walking", "boarding", "in_transit", "disembarking", "evacuated", "awaiting_backup"), LocationKind = location.Item1, LocationId = location.Item2,
                    EnteredScenario = row["node_entered_scenario_tick"].IsNull ? (long?)null : RestoreNumber(row["node_entered_scenario_tick"], 0, scenario), Exposure = (int)RestoreNumber(row["consecutive_exposure_ticks"], 0, 20), FinalMode = row["final_leg_mode"].IsNull ? null : RestoreOne(row["final_leg_mode"], "ground", "water", "cable") };
                RequireRestore((p.LocationKind == "node") == p.EnteredScenario.HasValue && (p.State == "evacuated") == (p.FinalMode != null), "resident dwell/final mode coherence");
                RequireRestore(p.State == "awaiting_backup" || p.State == "walking" && p.LocationKind == "edge" || (p.State == "in_transit" || p.State == "disembarking") && p.LocationKind == "vehicle" || (p.State == "waiting" || p.State == "boarding" || p.State == "sheltered" || p.State == "evacuated") && p.LocationKind == "node", "resident state/location coherence");
                RequireRestore(Terminal(p) || p.Exposure < 20, "nonterminal resident exposure");
                RequireRestore(p.State != "evacuated" || p.Exposure == 0, "evacuated exposure");
                residentReservations.Add(id, RestoreIds(row["reservation_ids"], 3, true)); next.people.Add(id, p);
            }
            RequireRestore(next.people.Keys.SequenceEqual(spawns.Keys.OrderBy(x => x, StringComparer.Ordinal)), "exact compiled population");
            var equipmentRows = RestoreRows(snapshot["equipment"], 512).ToDictionary(x => x["id"].Text, StringComparer.Ordinal);
            foreach (var row in RestoreRows(snapshot["vehicles"], 32))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "equipment_instance_id", "kind", "location", "occupant_ids", "route_order_id", "status", "duty_used", "rest_remaining", "brake_remaining");
                string id = RestoreId(row["id"]), kind = RestoreOne(row["kind"], "fixed_bus", "skiff", "crawler", "winch");
                string equipmentId = RestoreNullableId(row["equipment_instance_id"]); var location = RestoreLocation(row["location"], false);
                var v = new Vehicle { Id = id, Kind = kind, EquipmentId = equipmentId, Node = location.Item1 == "node" ? location.Item2 : null, TransitId = location.Item1 == "edge" ? location.Item2 : null,
                    RouteId = RestoreNullableId(row["route_order_id"]), Status = RestoreOne(row["status"], "idle", "waiting_route", "boarding", "travelling", "unboarding", "waiting_power", "braked", "returning", "resting", "failed"), Duty = (int)RestoreNumber(row["duty_used"], 0, operation), Rest = (int)RestoreNumber(row["rest_remaining"], 0, 30), Brake = (int)RestoreNumber(row["brake_remaining"], 0, 20) };
                var fixedSpawn = layout.fixed_vehicles.SingleOrDefault(x => x.vehicle_id == id);
                if (fixedSpawn != null)
                {
                    RequireRestore(equipmentId == null && kind == fixedSpawn.kind, "fixed vehicle binding"); v.Seats = fixedSpawn.seats; v.Slots = fixedSpawn.movement_slots;
                }
                else
                {
                    RequireRestore(equipmentId == id && kind != "fixed_bus" && equipmentDefinitions.ContainsKey(kind), "deployed vehicle requires exact compiled equipment definition");
                    // Lookup after the guard also makes the missing-definition
                    // failure explicit for synthetic constructors without a catalog.
                    var definition = equipmentDefinitions[kind]; v.Seats = definition.resident_seats; v.Slots = definition.movement_slots;
                    RequireRestore(v.Seats > 0 && v.Slots > 0 && equipmentRows.TryGetValue(equipmentId, out var equipment) && equipment["equipment_id"].Text == kind, "deployed vehicle equipment row");
                    var savedEquipment = equipmentRows[equipmentId]; long sequence = RestoreSequence(id, "eq", false, false);
                    RequireRestore(receipts.ContainsKey(sequence), "deployed vehicle accepted placement sequence");
                    next.RestoreCommandIdentity(snapshot, receipts[sequence], sequence, receipts[sequence]["result"]["executed_tick"].Integer, "place_equipment", O("equipment_id", kind, "socket_id", savedEquipment["socket_id"], "orientation_step", savedEquipment["orientation_step"]));
                    RequireRestore(savedEquipment["lifecycle"].Text != "broken" || v.Status == "failed", "broken deployed vehicle failure");
                }
                RequireRestore(kind == "winch" || v.Duty == 0 && v.Rest == 0 && v.Brake == 0, "non-winch duty/rest/brake state");
                RequireRestore(v.Brake == 0 || v.Status == "braked", "brake status");
                foreach (string occupant in RestoreIds(row["occupant_ids"], 256, true)) { yield return "transport.restore"; v.Occupants.Add(occupant); }
                next.vehicles.Add(id, v);
            }
            RequireRestore(layout.fixed_vehicles.All(x => next.vehicles.ContainsKey(x.vehicle_id)), "missing fixed vehicle");
            RequireRestore(equipmentRows.Values.Where(x => new[] { "skiff", "crawler", "winch" }.Contains(x["equipment_id"].Text)).Select(x => x["id"].Text).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(next.vehicles.Values.Where(x => x.EquipmentId != null).Select(x => x.EquipmentId).OrderBy(x => x, StringComparer.Ordinal)), "vehicle/equipment bijection");
            foreach (var row in RestoreRows(snapshot["routes"], 288))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "subject_kind", "subject_id", "stops", "next_stop_index", "resolved_edge_ids", "submitted_operation_tick", "submitted_sequence");
                var order = new Order { Id = RestoreId(row["id"]), Kind = RestoreOne(row["subject_kind"], "group", "vehicle"), Subject = RestoreId(row["subject_id"]), Tick = RestoreNumber(row["submitted_operation_tick"], 0, operation), Sequence = RestoreNumber(row["submitted_sequence"], 0, lastSequence) };
                RequireRestore(order.Kind == "group" ? mission.groups.Any(x => x.id == order.Subject) : next.vehicles.ContainsKey(order.Subject), "route subject");
                RequireRestore(row["stops"].Kind == "array" && row["stops"].Items.Count >= 1 && row["stops"].Items.Count <= 16, "route stop count");
                order.Stops = row["stops"].Items.Select(stop =>
                {
                    RestoreFields(stop, "node_id", "action", "group_id"); string node = RestoreId(stop["node_id"]), action = RestoreOne(stop["action"], "assemble", "pickup", "dropoff", "evacuate"), group = RestoreNullableId(stop["group_id"]);
                    RequireRestore(nodes.ContainsKey(node) && (group == null || mission.groups.Any(x => x.id == group)), "route stop reference");
                    RequireRestore(action != "evacuate" || nodes[node].kind == "evacuation", "route evacuation target");
                    RequireRestore(order.Sequence == 0 || order.Kind == "group" && (action == "assemble" || action == "evacuate") && group == null || order.Kind == "vehicle" && (action == "pickup" && group != null || (action == "dropoff" || action == "evacuate") && group == null), "route stop subject/action");
                    return new Stop(node, action, group);
                }).ToArray();
                order.Next = (int)RestoreNumber(row["next_stop_index"], 0, order.Stops.Length); order.Edges = RestoreIds(row["resolved_edge_ids"], edges.Count, false);
                RequireRestore(order.Edges.Distinct(StringComparer.Ordinal).Count() == order.Edges.Length, "route path duplicate edge");
                for (int i = 0; i < order.Edges.Length; i++)
                {yield return "transport.restore"; 
                    RequireRestore(edges.TryGetValue(order.Edges[i], out var edge), "route path edge");
                    RequireRestore(edge.traversal_class == (order.Kind == "group" ? "ground" : Mode(next.vehicles[order.Subject])) && (i == 0 || edges[order.Edges[i - 1]].to_node_id == edge.from_node_id), "route path continuity/class");
                }
                if (order.Sequence != 0)
                {
                    RequireRestore(order.Id == "route:" + order.Sequence.ToString(CultureInfo.InvariantCulture) && receipts.ContainsKey(order.Sequence), "route command identity");
                    next.RestoreCommandIdentity(snapshot, receipts[order.Sequence], order.Sequence, order.Tick, "assign_route", O("subject_kind", order.Kind, "subject_id", order.Subject, "stops", row["stops"]));
                }
                next.orders.Add(order.Id, order);
            }
            RequireRestore(!next.orders.Values.GroupBy(x => x.Kind + ":" + x.Subject, StringComparer.Ordinal).Any(x => x.Count() != 1), "one current route per subject");
            foreach (var v in next.vehicles.Values)
            {yield return "transport.restore"; 
                var own = next.orders.Values.SingleOrDefault(x => x.Kind == "vehicle" && x.Subject == v.Id);
                RequireRestore(v.RouteId == own?.Id, "vehicle route reciprocity");
                RequireRestore(!layout.fixed_vehicles.Any(x => x.vehicle_id == v.Id && x.initial_route_id != null) || own != null, "missing initial/replacement vehicle route");
            }
            foreach (var row in RestoreRows(snapshot["transits"], 288))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "mover_kind", "mover_id", "edge_id", "origin_node_id", "destination_node_id", "duration_operation_ticks", "progress_operation_ticks", "mode", "remaining_return_ticks", "closure_latched");
                var t = new Transit { Id = RestoreId(row["id"]), MoverKind = RestoreOne(row["mover_kind"], "resident", "vehicle"), MoverId = RestoreId(row["mover_id"]), Edge = RestoreId(row["edge_id"]), Origin = RestoreId(row["origin_node_id"]), Destination = RestoreId(row["destination_node_id"]), Duration = RestoreNumber(row["duration_operation_ticks"], 1, int.MaxValue), Progress = RestoreNumber(row["progress_operation_ticks"], 0, operation), Mode = RestoreOne(row["mode"], "forward", "return", "backup"), Return = RestoreNumber(row["remaining_return_ticks"], 0, operation), Closure = row["closure_latched"].Boolean };
                RequireRestore(edges.ContainsKey(t.Edge) && t.Progress < t.Duration && t.Return <= t.Progress && (t.Mode == "forward" ? t.Return == 0 : t.Closure), "transit progress/mode");
                RequireRestore(t.Mode != "return" || t.Return == t.Progress && t.Return > 0, "return progress");
                RequireRestore(t.Mode != "backup" || t.Return == 0 || t.Return == t.Progress, "backup return progress");
                RequireRestore(edges[t.Edge].from_node_id == t.Origin && edges[t.Edge].to_node_id == t.Destination, "transit endpoints");
                long numerator = checked(edges[t.Edge].base_operation_ticks * 1000);
                long minimumSpeed = Math.Max(500, IntegerMath.CeilDiv(numerator, t.Duration)), maximumSpeed = Math.Min(2000, t.Duration == 1 ? 2000 : (numerator - 1) / (t.Duration - 1));
                RequireRestore(minimumSpeed <= maximumSpeed, "transit duration must be produced by a supported integer speed");
                next.transits.Add(t.Id, t);
            }
            foreach (var row in RestoreRows(snapshot["transfers"], 32))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "resident_id", "vehicle_id", "node_id", "direction", "progress_operation_ticks", "seat_reservation_id", "node_reservation_id");
                var t = new Transfer { Id = RestoreId(row["id"]), Person = RestoreId(row["resident_id"]), Vehicle = RestoreId(row["vehicle_id"]), Node = RestoreId(row["node_id"]), Direction = RestoreOne(row["direction"], "board", "unboard"), Progress = (int)RestoreNumber(row["progress_operation_ticks"], 0, 9), SeatReservation = RestoreId(row["seat_reservation_id"]), NodeReservation = RestoreNullableId(row["node_reservation_id"]) };
                RequireRestore(next.people.ContainsKey(t.Person) && next.vehicles.ContainsKey(t.Vehicle) && nodes.ContainsKey(t.Node), "passenger transfer references"); next.transfers.Add(t.Id, t);
            }
            foreach (var row in RestoreRows(snapshot["reservations"], 928))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "owner_id", "resource_kind", "resource_id", "quantity", "created_operation_tick");
                var r = new Reservation { Id = RestoreId(row["id"]), Owner = RestoreId(row["owner_id"]), Kind = RestoreOne(row["resource_kind"], "edge_slots", "node_people", "vehicle_seat", "dock_berth", "return_node"), Resource = RestoreId(row["resource_id"]), Quantity = RestoreNumber(row["quantity"], 1, int.MaxValue), Tick = RestoreNumber(row["created_operation_tick"], 1, operation) };
                next.reservations.Add(r.Id, r);
            }
            using (var ownerRestore = next.RestoreTransitAndTransferOwnersWork(receipts, operation)) { while (ownerRestore.MoveNext()) yield return ownerRestore.Stage; }
            foreach (var row in RestoreRows(snapshot["queues"], 288))
            {yield return "transport.restore"; 
                RestoreFields(row, "id", "subject_id", "target_node_id", "kind", "since_operation_tick", "submitted_sequence");
                var q = new Waiting { Id = RestoreId(row["id"]), Subject = RestoreId(row["subject_id"]), Target = RestoreId(row["target_node_id"]), Kind = RestoreOne(row["kind"], "berth", "capacity", "edge", "power"), Tick = RestoreNumber(row["since_operation_tick"], 1, operation), Sequence = RestoreNumber(row["submitted_sequence"], 0, lastSequence) };
                bool resident = next.people.ContainsKey(q.Subject), vehicle = next.vehicles.ContainsKey(q.Subject);
                RequireRestore(resident != vehicle && nodes.ContainsKey(q.Target), "queue subject/target");
                var order = next.orders.Values.SingleOrDefault(x => x.Kind == (resident ? "group" : "vehicle") && x.Subject == (resident ? next.people[q.Subject].Group : q.Subject));
                RequireRestore(order != null && order.Sequence == q.Sequence && order.Tick <= q.Tick && order.Next < order.Stops.Length, "queue current route binding");
                RequireRestore(resident ? !Terminal(next.people[q.Subject]) && next.people[q.Subject].LocationKind != "edge" && (q.Kind == "edge" || q.Kind == "capacity") : next.vehicles[q.Subject].Node != null && next.vehicles[q.Subject].Status != "failed" && !next.transfers.Values.Any(x => x.Vehicle == q.Subject), "queue physical work coherence");
                RequireRestore(q.Id == StableId("queue", q.Sequence, "subject", q.Subject, "operation_tick", q.Tick), "queue command-derived identity"); next.queues.Add(q.Id, q);
            }
            RequireRestore(!next.queues.Values.GroupBy(x => x.Subject, StringComparer.Ordinal).Any(x => x.Count() != 1), "duplicate waiting subject");
            using (var historyRestore = next.RestoreHeldAndHistoryWork(snapshot, operation, scenario)) { while (historyRestore.MoveNext()) yield return historyRestore.Stage; }
            using (var reciprocity = next.ValidateRestoredReciprocityWork(residentReservations)) { while (reciprocity.MoveNext()) yield return reciprocity.Stage; }
            using (var validation = next.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
            JsonValue readback; using (var workreadback = next.ReadStateWork()) { while (workreadback.MoveNext()) yield return workreadback.Stage; readback = workreadback.Result; } foreach (var field in readback.Members) { bool equal; using (var workequal = field.Value.CanonicalEqualsWork(snapshot[field.Key])) { while (workequal.MoveNext()) yield return workequal.Stage; equal = workequal.Result; } RequireRestore(equal, "owned state changed during reconstruction"); }
            if (operation == 0) { bool equal; using (var workequal = readback.CanonicalEqualsWork(initialState)) { while (workequal.MoveNext()) yield return workequal.Stage; equal = workequal.Result; } RequireRestore(equal, "zero-boundary transport differs from compiled initial state"); }
            // No ResolveMovement/Derive call: current dock eligibility can be
            // inspected, but old edge/dwell/peak facts must remain unavailable.
            next.RestoreBoundaryCursor(operation); done(next);
                }
        private void RestoreTransitAndTransferOwners(Dictionary<long, JsonValue> receipts, long operation) => RestoreTransitAndTransferOwnersWork(receipts, operation).Drain();
        private ResumableWork<bool> RestoreTransitAndTransferOwnersWork(Dictionary<long, JsonValue> receipts, long operation) => new ResumableWork<bool>(done => RestoreTransitAndTransferOwnersRoutine(receipts, operation, done));
        private IEnumerable<string> RestoreTransitAndTransferOwnersRoutine(Dictionary<long, JsonValue> receipts, long operation, Action<bool> done)
        {
            foreach (var t in transits.Values)
            {yield return "transport.RestoreTransitAndTransferOwners"; 
                var owned = reservations.Values.Where(x => x.Owner == t.Id).ToArray();
                long sequence = RestoreSequence(t.Id, "transit", true, true);
                string channel; long slots;
                if (t.MoverKind == "resident")
                {
                    RequireRestore(people.TryGetValue(t.MoverId, out var person) && person.LocationKind == "edge" && person.LocationId == t.Id, "resident transit reciprocity");
                    RequireRestore(person.State == (t.Mode == "backup" ? "awaiting_backup" : "walking") && edges[t.Edge].traversal_class == "ground" && edges[t.Edge].mobility_classes.Contains(person.Mobility) && nodes[t.Destination].mobility_classes.Contains(person.Mobility) && (person.Mobility != "step_free" || edges[t.Edge].step_free), "resident transit state/mobility");
                    channel = "node_people"; slots = 1;
                }
                else
                {
                    RequireRestore(vehicles.TryGetValue(t.MoverId, out var vehicle) && vehicle.TransitId == t.Id && vehicle.Node == null, "vehicle transit reciprocity");
                    RequireRestore(edges[t.Edge].traversal_class == Mode(vehicle), "vehicle transit class");
                    RequireRestore(t.Mode == "backup" ? vehicle.Status == "failed" : t.Mode == "return" ? vehicle.Status == "returning" : vehicle.Status == "travelling" || vehicle.Kind == "winch" && vehicle.Status == "braked", "vehicle transit status");
                    if (t.Mode == "backup") RequireRestore(vehicle.Occupants.All(x => people.ContainsKey(x) && people[x].State == "awaiting_backup"), "failed transit occupant state");
                    channel = VehicleChannel(vehicle); slots = vehicle.Slots;
                }
                RequireRestore(edges[t.Edge].movement_capacity >= slots, "mover edge capacity");
                RestoreOwnerSequence(sequence, t.MoverKind, t.MoverId, receipts);
                if (t.Mode == "backup")
                {
                    RequireRestore(owned.Length == 0, "backup transit retained reservations");
                    // Failure releases every admission-tick reservation. The
                    // frozen row retains its identity and exact inert position,
                    // but not the old admission O. Validate the accepted sequence
                    // and digest syntax; never invent a tick or scan a million
                    // hash preimages. Save integrity is not historical anti-cheat.
                    continue;
                }
                RequireRestore(owned.Length == (t.Mode == "forward" ? 3 : 2), "transit reservation count");
                long started = owned[0].Tick;
                RequireRestore(owned.All(x => x.Tick == started) && (sequence == 0 || receipts[sequence]["result"]["executed_tick"].Integer <= started) && t.Progress <= operation - started, "transit admission clock");
                RequireRestore(t.Id == StableId("transit", sequence, "mover", t.MoverId, "edge", t.Edge, "operation_tick", started), "transit command-derived identity");
                RestoreReservation(t.Id, sequence, "edge_slots", t.Edge, "edge_slots", slots, started);
                RestoreReservation(t.Id, sequence, "return_node", t.Origin, channel, 1, started);
                if (t.Mode == "forward") RestoreReservation(t.Id, sequence, channel == "node_people" ? "node_people" : "dock_berth", t.Destination, channel, 1, started);
            }
            foreach (var t in transfers.Values)
            {yield return "transport.RestoreTransitAndTransferOwners"; 
                var p = people[t.Person]; var v = vehicles[t.Vehicle]; var owned = reservations.Values.Where(x => x.Owner == t.Id).ToArray();
                long sequence = RestoreSequence(t.Id, "transfer", true, true); RestoreOwnerSequence(sequence, "vehicle", v.Id, receipts);
                RequireRestore(v.Node == t.Node && v.TransitId == null && nodes[t.Node].mobility_classes.Contains(p.Mobility) && !Terminal(p), "passenger transfer physical binding");
                RequireRestore(t.Direction == "board" ? p.State == "boarding" && p.LocationKind == "node" && p.LocationId == t.Node && !v.Occupants.Contains(p.Id) : p.State == "disembarking" && p.LocationKind == "vehicle" && p.LocationId == v.Id && v.Occupants.Contains(p.Id), "passenger transfer location/state");
                RequireRestore(v.Status == (t.Direction == "board" ? "boarding" : "unboarding") || v.Status == "waiting_power" || v.Status == "failed", "passenger transfer vehicle status");
                RequireRestore(owned.Length == (t.Direction == "board" ? 1 : 2), "passenger transfer reservation count");
                long started = owned[0].Tick;
                RequireRestore(owned.All(x => x.Tick == started) && (sequence == 0 || receipts[sequence]["result"]["executed_tick"].Integer <= started) && t.Progress <= operation - started, "passenger transfer admission clock");
                RequireRestore(t.Id == StableId("transfer", sequence, "resident", t.Person, "vehicle", t.Vehicle, "direction", t.Direction, "operation_tick", started), "passenger transfer command-derived identity");
                string seat = RestoreReservation(t.Id, sequence, "vehicle_seat", v.Id, t.Direction == "board" ? "vehicle_seat" : "retained_seat", 1, started);
                string node = t.Direction == "unboard" ? RestoreReservation(t.Id, sequence, "node_people", t.Node, "node_people", 1, started) : null;
                RequireRestore(t.SeatReservation == seat && t.NodeReservation == node, "passenger reservation pointers");
            }
            RequireRestore(reservations.Values.All(x => x.Channel != null), "unknown/orphan reservation ownership");
        
            done(true);
        }
        private string RestoreReservation(string owner, long sequence, string kind, string resource, string channel, long quantity, long tick)
        {
            string id = StableId("reservation", sequence, "owner", owner, "kind", kind, "resource", resource, "operation_tick", tick);
            RequireRestore(reservations.TryGetValue(id, out var r) && r.Owner == owner && r.Kind == kind && r.Resource == resource && r.Quantity == quantity && r.Tick == tick && r.Channel == null, "reservation identity/resource/quantity");
            r.Channel = channel; return id;
        }
        private void RestoreOwnerSequence(long sequence, string moverKind, string moverId, Dictionary<long, JsonValue> receipts)
        {
            RequireRestore(sequence != 0 ? receipts.ContainsKey(sequence) : moverKind == "vehicle" && layout.fixed_vehicles.Any(x => x.vehicle_id == moverId && x.initial_route_id != null), "transport owner accepted/initial sequence");
            string subject = moverKind == "resident" ? people[moverId].Group : moverId;
            var current = orders.Values.SingleOrDefault(x => x.Kind == (moverKind == "resident" ? "group" : "vehicle") && x.Subject == subject);
            RequireRestore(current != null && current.Sequence >= sequence, "in-flight work retains its current or replacement route");
        }
        private void RestoreHeldAndHistory(JsonValue snapshot, long operation, long scenario) => RestoreHeldAndHistoryWork(snapshot, operation, scenario).Drain();
        private ResumableWork<bool> RestoreHeldAndHistoryWork(JsonValue snapshot, long operation, long scenario) => new ResumableWork<bool>(done => RestoreHeldAndHistoryRoutine(snapshot, operation, scenario, done));
        private IEnumerable<string> RestoreHeldAndHistoryRoutine(JsonValue snapshot, long operation, long scenario, Action<bool> done)
        {
            var eventState = snapshot["event_state"];
            RestoreFields(eventState, "next_static_event_index", "fired_event_ids", "revealed_event_ids", "warned_event_ids", "dynamic_events", "administrative_edge_enabled", "edge_current_multipliers", "gate_open", "group_held", "wind_region_codes", "cooler_socket_enabled", "shelter_region_enabled", "resource_warning_thresholds");
            var fired = RestoreIds(eventState["fired_event_ids"], mission.events.Count, true);
            RequireRestore(fired.All(id => mission.events.Any(x => x.id == id)), "group release fired event identity");
            var released = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in mission.events.Where(x => fired.Contains(x.id, StringComparer.Ordinal)))
            { yield return "transport.RestoreHeldAndHistory"; foreach (var action in definition.operations.Where(x => x.kind == "release_group"))
            {yield return "transport.RestoreHeldAndHistory"; 
                var role = layout.roles.SingleOrDefault(x => x.role_id == action.target_role);
                RequireRestore(role != null && role.target_kind == "group", "group release role"); released.UnionWith(role.target_ids);
            } }
            foreach (var row in RestoreRows(eventState["group_held"], mission.groups.Count))
            {yield return "transport.RestoreHeldAndHistory"; 
                RestoreFields(row, "id", "value"); string id = RestoreId(row["id"]); var group = mission.groups.SingleOrDefault(x => x.id == id);
                RequireRestore(group != null && row["value"].Boolean == (group.initially_held && !released.Contains(id)), "held group event coherence"); held.Add(id, row["value"].Boolean);
            }
            RequireRestore(held.Keys.SequenceEqual(mission.groups.Select(x => x.id).OrderBy(x => x, StringComparer.Ordinal)), "exact held group rows");
            var targetGroups = mission.milestones.Where(x => x.kind == "group_boarded").Select(x => x.target).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var expected = targetGroups.ToDictionary(group => "transport:boarding:" + O("mission_version_id", mission.mission_version_id, "group_id", group).Sha256(), group => group, StringComparer.Ordinal);
            // The objective owner may have two dock rows per vehicle/node. Do
            // not impose a smaller transport-specific cap on that shared array.
            var histories = RestoreRows(snapshot["metrics"], int.MaxValue).Where(x => x["id"].Text.StartsWith("transport:", StringComparison.Ordinal) || x["metric"].Text == "group_boarded").ToArray();
            RequireRestore(histories.Select(x => x["id"].Text).SequenceEqual(expected.Keys.OrderBy(x => x, StringComparer.Ordinal)), "exact compiled transport causal metric rows");
            foreach (var row in histories)
            {yield return "transport.RestoreHeldAndHistory"; 
                RestoreFields(row, "id", "metric", "subject_id", "value", "distinct_resident_ids"); string group = expected[row["id"].Text];
                RequireRestore(row["metric"].Text == "group_boarded" && row["subject_id"].Text == group, "boarding metric identity");
                var ids = RestoreIds(row["distinct_resident_ids"], people.Count, true); long count = RestoreNumber(row["value"], 0, people.Count);
                RequireRestore(count == ids.Length && ids.All(id => people.ContainsKey(id) && people[id].Group == group), "boarding metric population/count");
                RequireRestore(layout.spawns.Where(x => x.group_id == group && x.fixed_vehicle_id != null).All(x => ids.Contains(x.resident_id, StringComparer.Ordinal)), "initial occupant boarding history");
                RequireRestore(people.Values.Where(x => x.Group == group && x.LocationKind == "vehicle").All(x => ids.Contains(x.Id, StringComparer.Ordinal)), "current occupants require completed boarding history");
                if (operation == 0) RequireRestore(ids.SequenceEqual(layout.spawns.Where(x => x.group_id == group && x.fixed_vehicle_id != null).Select(x => x.resident_id).OrderBy(x => x, StringComparer.Ordinal)), "zero-boundary boarding history");
                boardedEver.UnionWith(ids);
            }
            // Unqueried groups have no causal row by contract; only their
            // compiled initial occupants are needed. No future rule consumes
            // unrecorded boarding history for those groups.
            boardedEver.UnionWith(layout.spawns.Where(x => x.fixed_vehicle_id != null).Select(x => x.resident_id));
            foreach (var row in RestoreRows(snapshot["milestones"], mission.milestones.Count + 2))
            {yield return "transport.RestoreHeldAndHistory"; 
                RestoreFields(row, "id", "operation_tick", "scenario_tick"); string id = RestoreId(row["id"]); var definition = mission.milestones.SingleOrDefault(x => x.id == id);
                RequireRestore(definition != null || id == "operation_started" || id == "operation_ended", "unknown milestone"); long tick = RestoreNumber(row["operation_tick"], 0, operation), scenarioTick = RestoreNumber(row["scenario_tick"], 0, Math.Min(scenario, tick));
                RequireRestore(tick != operation || scenarioTick == scenario, "milestone at saved boundary clock");
                // Clock owns the two reserved engine marks in the full snapshot.
                if (definition == null) continue;
                if (definition.kind == "group_boarded" || definition.kind == "final_unload_started" || definition.kind == "all_evacuated") milestones.Add(id, row);
            }
            foreach (var definition in mission.milestones)
            {yield return "transport.RestoreHeldAndHistory"; 
                bool exists = milestones.TryGetValue(definition.id, out var mark);
                if (definition.kind == "group_boarded")
                {
                    var group = mission.groups.Single(x => x.id == definition.target); bool complete = group.resident_ids.All(boardedEver.Contains);
                    RequireRestore(exists == complete, "boarding milestone/history coherence");
                    if (exists)
                    {
                        bool initial = group.resident_ids.All(id => layout.spawns.Any(x => x.resident_id == id && x.fixed_vehicle_id != null));
                        RequireRestore(initial ? mark["operation_tick"].Integer == 0 && mark["scenario_tick"].Integer == 0 : mark["operation_tick"].Integer > 0, "boarding milestone first-boundary coherence");
                    }
                }
                if (definition.kind == "all_evacuated") RequireRestore(exists == (EvacuatedCount == Population) && (!exists || mark["operation_tick"].Integer > 0), "all-evacuated milestone coherence");
                if (definition.kind == "final_unload_started" && exists)
                {
                    RequireRestore(mark["operation_tick"].Integer > 0, "final unload cannot be initial");
                    foreach (var all in mission.milestones.Where(x => x.kind == "all_evacuated" && milestones.ContainsKey(x.id))) { yield return "transport.RestoreHeldAndHistory"; RequireRestore(mark["operation_tick"].Integer < milestones[all.id]["operation_tick"].Integer, "final unload precedes completed evacuation"); }
                }
            }
        
            done(true);
        }
        private void ValidateRestoredReciprocity(Dictionary<string, string[]> residentReservations) => ValidateRestoredReciprocityWork(residentReservations).Drain();
        private ResumableWork<bool> ValidateRestoredReciprocityWork(Dictionary<string, string[]> residentReservations) => new ResumableWork<bool>(done => ValidateRestoredReciprocityRoutine(residentReservations, done));
        private IEnumerable<string> ValidateRestoredReciprocityRoutine(Dictionary<string, string[]> residentReservations, Action<bool> done)
        {
            RequireRestore(!transits.Values.GroupBy(x => x.MoverKind + ":" + x.MoverId, StringComparer.Ordinal).Any(x => x.Count() != 1), "one transit per mover");
            foreach (var p in people.Values)
            {yield return "transport.ValidateRestoredReciprocity"; 
                RequireRestore(p.LocationKind != "node" || nodes.ContainsKey(p.LocationId), "resident node reference");
                var ownTransfer = transfers.Values.Where(x => x.Person == p.Id).ToArray();
                RequireRestore((p.State == "boarding" || p.State == "disembarking") == (ownTransfer.Length == 1) && ownTransfer.Length <= 1, "resident passenger-transfer reciprocity");
                if (p.LocationKind == "edge") RequireRestore(transits.ContainsKey(p.LocationId) && transits[p.LocationId].MoverKind == "resident" && transits[p.LocationId].MoverId == p.Id, "resident edge reciprocity");
                if (p.LocationKind == "vehicle") RequireRestore(vehicles.ContainsKey(p.LocationId) && vehicles[p.LocationId].Occupants.Contains(p.Id), "resident occupant reciprocity");
                var owned = reservations.Values.Where(r => transfers.ContainsKey(r.Owner) && transfers[r.Owner].Person == p.Id || transits.ContainsKey(r.Owner) && transits[r.Owner].MoverKind == "resident" && transits[r.Owner].MoverId == p.Id).Select(x => x.Id);
                RequireRestore(residentReservations[p.Id].SequenceEqual(owned), "resident reservation reciprocity");
            }
            foreach (var v in vehicles.Values)
            {yield return "transport.ValidateRestoredReciprocity"; 
                RequireRestore(v.Node == null || nodes.ContainsKey(v.Node), "vehicle node reference");
                RequireRestore(v.Node == null || v.Brake == 0, "parked vehicle brake");
                RequireRestore(v.Node == null || v.Status != "travelling" && v.Status != "returning" && v.Status != "braked", "parked vehicle travel status");
                RequireRestore(v.Status != "resting" || v.Kind == "winch" && v.Node != null && v.Rest > 0, "resting vehicle state");
                if (v.Status == "boarding" || v.Status == "unboarding") RequireRestore(transfers.Values.Any(x => x.Vehicle == v.Id && x.Direction == (v.Status == "boarding" ? "board" : "unboard")), "vehicle passenger-transfer reciprocity");
                if (v.TransitId != null) RequireRestore(transits.ContainsKey(v.TransitId) && transits[v.TransitId].MoverKind == "vehicle" && transits[v.TransitId].MoverId == v.Id, "vehicle edge reciprocity");
                RequireRestore(v.Occupants.All(id => people.ContainsKey(id) && people[id].LocationKind == "vehicle" && people[id].LocationId == v.Id && people[id].State != "evacuated"), "vehicle occupant reciprocity");
            }
        
            done(true);
        }
        private static Dictionary<long, JsonValue> RestoreAcceptedReceipts(JsonValue rows, long lastSequence, long operation) => RestoreAcceptedReceiptsWork(rows, lastSequence, operation).Drain();
        private static ResumableWork<Dictionary<long, JsonValue>> RestoreAcceptedReceiptsWork(JsonValue rows, long lastSequence, long operation) => new ResumableWork<Dictionary<long, JsonValue>>(done => RestoreAcceptedReceiptsRoutine(rows, lastSequence, operation, done));
        private static IEnumerable<string> RestoreAcceptedReceiptsRoutine(JsonValue rows, long lastSequence, long operation, Action<Dictionary<long, JsonValue>> done)
        {
            RequireRestore(rows.Kind == "array" && rows.Items.Count == lastSequence, "complete admitted receipt history");
            var result = new Dictionary<long, JsonValue>(); var commandIds = new HashSet<string>(StringComparer.Ordinal); long previous = 0;
            foreach (var row in rows.Items)
            {yield return "transport.restore.receipt"; 
                RestoreFields(row, "command_id", "sequence", "request_sha256", "result"); string command = RestoreId(row["command_id"]); long sequence = RestoreNumber(row["sequence"], 1, 10000);
                RequireRestore(sequence == ++previous && commandIds.Add(command) && Regex.IsMatch(row["request_sha256"].Text, "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant), "receipt sequence/hash identity");
                var receipt = row["result"]; RestoreFields(receipt, "command_id", "sequence", "status", "accepted", "reason_code", "executed_tick", "affected_ids", "state_revision");
                string status = RestoreOne(receipt["status"], "queued", "accepted", "rejected");
                RequireRestore(RestoreId(receipt["command_id"]) == command && RestoreNumber(receipt["sequence"], 1, 10000) == sequence && receipt["accepted"].Boolean == (status == "accepted"), "receipt result identity");
                RestoreNumber(receipt["executed_tick"], status == "queued" ? -1 : 0, status == "queued" ? -1 : operation);
                if (status == "accepted") { RequireRestore(receipt["reason_code"].Text == "OK", "accepted receipt reason"); result.Add(sequence, row); }
            }
            done(result);
                }
        private void RestoreCommandIdentity(JsonValue snapshot, JsonValue receipt, long sequence, long tick, string kind, JsonValue payload)
        {
            RequireRestore(tick > 0 && receipt["result"]["executed_tick"].Integer == tick, "transport command execution boundary");
            bool matches = new[] { "hands_left", "hands_right", "controller_left", "controller_right", "head_dwell", "reference_replay", "system_tutorial" }.Any(actor =>
                O("schema_version", 1, "command_id", receipt["command_id"], "run_id", snapshot["identity"]["run_id"], "sequence", sequence, "requested_tick", tick, "actor_input", actor, "kind", kind, "payload", payload).Sha256() == receipt["request_sha256"].Text);
            RequireRestore(matches, "current transport command body must match its accepted receipt hash");
        }
        private static void RequireRestore(bool valid, string detail) { if (!valid) throw new InvalidOperationException("Invalid saved transport: " + detail); }
        private static void RestoreFields(JsonValue row, params string[] fields) { RequireRestore(row != null && row.Kind == "object" && row.Members.Count == fields.Length && fields.All(row.Has), "closed required fields"); }
        private static long RestoreNumber(JsonValue value, long minimum, long maximum) { RequireRestore(value.Kind == "integer" && value.Integer >= minimum && value.Integer <= maximum, "integer range"); return value.Integer; }
        private static string RestoreOne(JsonValue value, params string[] choices) { RequireRestore(value.Kind == "string" && choices.Contains(value.Text, StringComparer.Ordinal), "closed enum"); return value.Text; }
        private static string RestoreId(JsonValue value) { RequireRestore(value.Kind == "string" && Regex.IsMatch(value.Text, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant), "ID"); return value.Text; }
        private static string RestoreNullableId(JsonValue value) => value.IsNull ? null : RestoreId(value);
        private static IEnumerable<JsonValue> RestoreRows(JsonValue rows, int maximum)
        {
            RequireRestore(rows.Kind == "array" && rows.Items.Count <= maximum, "entity array bound"); string previous = null;
            foreach (var row in rows.Items) { string id = RestoreId(row["id"]); RequireRestore(previous == null || StringComparer.Ordinal.Compare(previous, id) < 0, "sorted unique entity IDs"); previous = id; yield return row; }
        }
        private static string[] RestoreIds(JsonValue values, int maximum, bool sorted)
        {
            RequireRestore(values.Kind == "array" && values.Items.Count <= maximum, "ID array bound"); var ids = values.Items.Select(RestoreId).ToArray();
            RequireRestore(!sorted || ids.SequenceEqual(ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)), "sorted unique IDs"); return ids;
        }
        private static Tuple<string, string> RestoreLocation(JsonValue location, bool resident)
        {
            string kind = resident ? RestoreOne(location["kind"], "node", "edge", "vehicle") : RestoreOne(location["kind"], "node", "edge"); string field = kind == "node" ? "node_id" : kind == "edge" ? "transit_id" : "vehicle_id";
            RestoreFields(location, "kind", field); return Tuple.Create(kind, RestoreId(location[field]));
        }
        private static long RestoreSequence(string id, string prefix, bool digest, bool allowZero)
        {
            string[] parts = id.Split(':'); long sequence = -1;
            RequireRestore(parts.Length == (digest ? 3 : 2) && parts[0] == prefix && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence >= (allowZero ? 0 : 1) && sequence <= 10000 && parts[1] == sequence.ToString(CultureInfo.InvariantCulture) && (!digest || Regex.IsMatch(parts[2], "^[a-f0-9]{64}\\z", RegexOptions.CultureInvariant)), "command-derived entity identity syntax"); return sequence;
        }
        public JsonValue ReadState() => ReadStateWork().Drain();
        public ResumableWork<JsonValue> ReadStateWork() => new ResumableWork<JsonValue>(ReadStateRoutine);
        private IEnumerable<string> ReadStateRoutine(Action<JsonValue> done)
        {
            JsonValue vehiclesRows; using (var workvehiclesRows = DomainWork.Rows(vehicles.Values, v => O("id", v.Id, "equipment_instance_id", v.EquipmentId, "kind", v.Kind, "location", Location(v.Node == null ? "edge" : "node", v.Node ?? v.TransitId), "occupant_ids", Strings(v.Occupants), "route_order_id", v.RouteId, "status", v.Status, "duty_used", v.Duty, "rest_remaining", v.Rest, "brake_remaining", v.Brake), "transport.read.vehiclesRows")) { while (workvehiclesRows.MoveNext()) yield return workvehiclesRows.Stage; vehiclesRows = workvehiclesRows.Result; }
            JsonValue peopleRows; using (var workpeopleRows = DomainWork.Rows(people.Values, p => O("id", p.Id, "group_id", p.Group, "state", p.State, "location", Location(p.LocationKind, p.LocationId), "reservation_ids", Strings(reservations.Values.Where(r => transfers.ContainsKey(r.Owner) && transfers[r.Owner].Person == p.Id || transits.ContainsKey(r.Owner) && transits[r.Owner].MoverKind == "resident" && transits[r.Owner].MoverId == p.Id).Select(r => r.Id)), "node_entered_scenario_tick", p.EnteredScenario.HasValue ? JsonValue.Number(p.EnteredScenario.Value) : JsonValue.Null(), "consecutive_exposure_ticks", p.Exposure, "final_leg_mode", p.FinalMode), "transport.read.peopleRows")) { while (workpeopleRows.MoveNext()) yield return workpeopleRows.Stage; peopleRows = workpeopleRows.Result; }
            JsonValue transitsRows; using (var worktransitsRows = DomainWork.Rows(transits.Values, t => O("id", t.Id, "mover_kind", t.MoverKind, "mover_id", t.MoverId, "edge_id", t.Edge, "origin_node_id", t.Origin, "destination_node_id", t.Destination, "duration_operation_ticks", t.Duration, "progress_operation_ticks", t.Progress, "mode", t.Mode, "remaining_return_ticks", t.Return, "closure_latched", t.Closure), "transport.read.transitsRows")) { while (worktransitsRows.MoveNext()) yield return worktransitsRows.Stage; transitsRows = worktransitsRows.Result; }
            JsonValue transfersRows; using (var worktransfersRows = DomainWork.Rows(transfers.Values, t => O("id", t.Id, "resident_id", t.Person, "vehicle_id", t.Vehicle, "node_id", t.Node, "direction", t.Direction, "progress_operation_ticks", t.Progress, "seat_reservation_id", t.SeatReservation, "node_reservation_id", t.NodeReservation), "transport.read.transfersRows")) { while (worktransfersRows.MoveNext()) yield return worktransfersRows.Stage; transfersRows = worktransfersRows.Result; }
            JsonValue reservationsRows; using (var workreservationsRows = DomainWork.Rows(reservations.Values, r => O("id", r.Id, "owner_id", r.Owner, "resource_kind", r.Kind, "resource_id", r.Resource, "quantity", r.Quantity, "created_operation_tick", r.Tick), "transport.read.reservationsRows")) { while (workreservationsRows.MoveNext()) yield return workreservationsRows.Stage; reservationsRows = workreservationsRows.Result; }
            JsonValue ordersRows; using (var workordersRows = DomainWork.Rows(orders.Values, x => O("id", x.Id, "subject_kind", x.Kind, "subject_id", x.Subject, "stops", JsonValue.Array(x.Stops.Select(s => s.Json())), "next_stop_index", x.Next, "resolved_edge_ids", Strings(x.Edges), "submitted_operation_tick", x.Tick, "submitted_sequence", x.Sequence), "transport.read.ordersRows")) { while (workordersRows.MoveNext()) yield return workordersRows.Stage; ordersRows = workordersRows.Result; }
            JsonValue queuesRows; using (var workqueuesRows = DomainWork.Rows(queues.Values, x => O("id", x.Id, "subject_id", x.Subject, "target_node_id", x.Target, "kind", x.Kind, "since_operation_tick", x.Tick, "submitted_sequence", x.Sequence), "transport.read.queuesRows")) { while (workqueuesRows.MoveNext()) yield return workqueuesRows.Stage; queuesRows = workqueuesRows.Result; }

            done(O("vehicles", vehiclesRows,
                "residents", peopleRows,
                "transits", transitsRows,
                "transfers", transfersRows,
                "reservations", reservationsRows,
                "routes", ordersRows,
                "queues", queuesRows));
        }
        public JsonValue Milestones => MilestonesWork().Drain();
        public ResumableWork<JsonValue> MilestonesWork() => DomainWork.Rows(milestones.Values, row => row, "transport.read.milestone");
        public JsonValue GroupHeld => GroupHeldWork().Drain();
        public ResumableWork<JsonValue> GroupHeldWork() => DomainWork.Rows(held, x => O("id", x.Key, "value", x.Value), "transport.read.held");
        // Stage cursors are derived from outer Snapshot.operation_tick on load,
        // never a separate wire field or source of generated simulation IDs.
        public void RestoreBoundaryCursor(long operationTick)
        {
            new Tick(operationTick); lastMovementTick = operationTick; lastDerivedTick = operationTick;
            completedEdgePassages.Clear(); boundaryFacts = new List<CompletedEdgePassage>().AsReadOnly(); dockWaitFacts = new List<DockWaitSample>().AsReadOnly(); boundaryFactsTick = -1;
            completedNodeDwells.Clear(); nodeDwellFacts = new List<CompletedNodeDwell>().AsReadOnly();
            nodeOccupancyPeaks.Clear(); occupancyPeakFacts = new ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(StringComparer.Ordinal));
        }
        private static JsonValue Strings(IEnumerable<string> values) => JsonValue.Array(values.Select(JsonValue.String));
        private static JsonValue Location(string kind, string id) => O("kind", kind, kind == "node" ? "node_id" : kind == "edge" ? "transit_id" : "vehicle_id", id);
        internal static JsonValue O(params object[] pairs)
        {
            var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) { object v = pairs[i + 1]; result.Add((string)pairs[i], v == null ? JsonValue.Null() : v is JsonValue json ? json : v is string s ? JsonValue.String(s) : v is bool b ? JsonValue.Bool(b) : JsonValue.Number(Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture))); }
            return JsonValue.Object(result);
        }
    }
}
