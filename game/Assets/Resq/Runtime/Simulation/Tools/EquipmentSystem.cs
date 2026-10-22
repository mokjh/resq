using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;
using Resq.Simulation.Rescue;

namespace Resq.Simulation.Tools
{
    // Pure, offline integer authority. Apply and interval methods return isolated
    // next owners. The caller publishes them only after its complete boundary is
    // valid; C02 remains the sole command admission/receipt/deduplication owner.
    public sealed class EquipmentSystem
    {
        private sealed class Instance
        {
            public string Id, Kind, Socket, Lifecycle = "deployed", Segment;
            public int Orientation;
            public bool Enabled = true, Activated, ExhaustionEmitted;
            public long Durability, Remainder, Charges, Protection;
            public Instance Copy() => (Instance)MemberwiseClone();
        }
        private readonly CompiledMission mission;
        private readonly LayoutDef layout;
        private readonly SortedDictionary<string, EquipmentDef> definitions;
        private readonly SortedDictionary<string, Socket> sockets;
        private readonly SortedDictionary<string, long> selected;
        private SortedDictionary<string, long> deployments;
        private SortedDictionary<string, Instance> instances = new SortedDictionary<string, Instance>(StringComparer.Ordinal);
        private IEquipmentMechanics mechanics;
        private long lastDamageOperation, lastWaterOperation;
        public IEquipmentMechanics Mechanics => mechanics;
        public long SuppliesRemaining { get; private set; }
        public WaterSystem Water { get; private set; }
        public TransportSystem Transport { get; private set; }
        public PowerSystem Power { get; private set; }
        public FiniteCoolantSystem Coolant { get; private set; }
        public int EquipmentLosses => instances.Values.Count(x => x.Lifecycle == "broken");
        public ReadOnlyCollection<EquipmentView> Equipment => ReadEquipmentWork().Drain();
        public ResumableWork<ReadOnlyCollection<EquipmentView>> ReadEquipmentWork() => new ResumableWork<ReadOnlyCollection<EquipmentView>>(ReadEquipmentRoutine);
        private IEnumerable<string> ReadEquipmentRoutine(Action<ReadOnlyCollection<EquipmentView>> done)
        {
            var views = new List<EquipmentView>();
            foreach (var item in instances.Values) { views.Add(new EquipmentView(item.Id, item.Kind, item.Socket, item.Enabled, item.Lifecycle != "broken")); yield return "equipment.read.view"; }
            done(views.AsReadOnly());
        }

        public EquipmentSystem(CompiledMission mission, LayoutDef layout, IEnumerable<EquipmentDef> equipmentDefinitions,
            WaterSystem water, TransportSystem transport, IEquipmentMechanics mechanics = null, IEnumerable<EquipmentCount> selectedStock = null)
            : this(mission, layout)
        {
            using (var work = InitializeWork(equipmentDefinitions, water, transport, mechanics, selectedStock)) work.Drain();
        }
        private EquipmentSystem(CompiledMission mission, LayoutDef layout)
        {
            this.mission = mission ?? throw new ArgumentNullException(nameof(mission)); this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            if (layout.mission_version_id != mission.mission_version_id || mission.rules_version != "resq-rules-1") throw new InvalidOperationException("Equipment content identity mismatch");
            definitions = new SortedDictionary<string, EquipmentDef>(StringComparer.Ordinal);
            sockets = new SortedDictionary<string, Socket>(StringComparer.Ordinal);
            selected = new SortedDictionary<string, long>(StringComparer.Ordinal); deployments = new SortedDictionary<string, long>(StringComparer.Ordinal);
        }
        public static ResumableWork<EquipmentSystem> CreateWork(CompiledMission mission, LayoutDef layout, IEnumerable<EquipmentDef> equipmentDefinitions,
            WaterSystem water, TransportSystem transport, IEquipmentMechanics mechanics = null, IEnumerable<EquipmentCount> selectedStock = null)
            => new ResumableWork<EquipmentSystem>(done => CreateRoutine(mission, layout, equipmentDefinitions, water, transport, mechanics, selectedStock, done));
        private static IEnumerable<string> CreateRoutine(CompiledMission mission, LayoutDef layout, IEnumerable<EquipmentDef> equipmentDefinitions,
            WaterSystem water, TransportSystem transport, IEquipmentMechanics mechanics, IEnumerable<EquipmentCount> selectedStock, Action<EquipmentSystem> done)
        {
            var next = new EquipmentSystem(mission, layout);
            using (var work = next.InitializeWork(equipmentDefinitions, water, transport, mechanics, selectedStock)) { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork(IEnumerable<EquipmentDef> equipmentDefinitions, WaterSystem water, TransportSystem transport,
            IEquipmentMechanics mechanics, IEnumerable<EquipmentCount> selectedStock)
            => new ResumableWork<bool>(done => InitializeRoutine(equipmentDefinitions, water, transport, mechanics, selectedStock, done));
        private IEnumerable<string> InitializeRoutine(IEnumerable<EquipmentDef> equipmentDefinitions, WaterSystem water, TransportSystem transport,
            IEquipmentMechanics mechanics, IEnumerable<EquipmentCount> selectedStock, Action<bool> done)
        {
            yield return "equipment.create.begin";
            foreach (var definition in equipmentDefinitions)
            {
                using (var check = ValidateDefinitionWork(definition)) { while (check.MoveNext()) yield return check.Stage; }
                definitions.Add(definition.id, definition); yield return "equipment.create.definition";
            }
            foreach (var socket in layout.sockets) { sockets.Add(socket.id, socket); yield return "equipment.create.socket"; }
            foreach (var stock in selectedStock ?? mission.default_stock)
            {
                EquipmentCount maximum;
                using (var scan = SingleOrDefaultWork(mission.maximum_stock, x => x.equipment_id == stock.equipment_id)) { while (scan.MoveNext()) yield return scan.Stage; maximum = scan.Result; }
                if (maximum == null || stock.count > maximum.count) throw new InvalidOperationException("Invalid selected stock");
                bool permitted;
                using (var scan = AnyWork(mission.permitted_equipment_ids, x => x == stock.equipment_id)) { while (scan.MoveNext()) yield return scan.Stage; permitted = scan.Result; }
                if (!permitted || !definitions.ContainsKey(stock.equipment_id)) throw new InvalidOperationException("Invalid selected stock");
                selected.Add(stock.equipment_id, stock.count); deployments.Add(stock.equipment_id, 0);
                yield return "equipment.create.stock";
            }
            SuppliesRemaining = mission.supply_budget;
            Water = water ?? throw new ArgumentNullException(nameof(water)); Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.mechanics = mechanics;
            using (var work = PowerSystem.CreateWork(layout)) { while (work.MoveNext()) yield return work.Stage; Power = work.Result; }
            using (var work = FiniteCoolantSystem.CreateWork(layout)) { while (work.MoveNext()) yield return work.Stage; Coolant = work.Result; }
            using (var check = ValidateBoundaryWork()) { while (check.MoveNext()) yield return check.Stage; }
            done(true);
        }
        private EquipmentSystem(EquipmentSystem other)
        {
            mission = other.mission; layout = other.layout; definitions = other.definitions; sockets = other.sockets; selected = other.selected;
            deployments = new SortedDictionary<string, long>(StringComparer.Ordinal);
            SuppliesRemaining = other.SuppliesRemaining; lastDamageOperation = other.lastDamageOperation; lastWaterOperation = other.lastWaterOperation;
            
        }
        public EquipmentSystem Fork() => ForkWork().Drain();
        public ResumableWork<EquipmentSystem> ForkWork() => new ResumableWork<EquipmentSystem>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<EquipmentSystem> done)
        {
            var next = new EquipmentSystem(this);
            foreach (var pair in deployments) { next.deployments.Add(pair.Key, pair.Value); yield return "equipment.fork.deployment"; }
            foreach (var pair in instances) { next.instances.Add(pair.Key, pair.Value.Copy()); yield return "equipment.fork.instance"; }
            WaterSystem water; using (var workwater = Water.ForkWork()) { while (workwater.MoveNext()) yield return workwater.Stage; water = workwater.Result; } next.Water = water;
            TransportSystem transport; using (var worktransport = Transport.ForkWork()) { while (worktransport.MoveNext()) yield return worktransport.Stage; transport = worktransport.Result; } next.Transport = transport;
            PowerSystem power; using (var workpower = Power.ForkWork()) { while (workpower.MoveNext()) yield return workpower.Stage; power = workpower.Result; } next.Power = power;
            FiniteCoolantSystem coolant; using (var workcoolant = Coolant.ForkWork()) { while (workcoolant.MoveNext()) yield return workcoolant.Stage; coolant = workcoolant.Result; } next.Coolant = coolant;
            next.mechanics = mechanics?.Fork();
            if (next.mechanics != null && ReferenceEquals(next.mechanics, mechanics)) throw new InvalidOperationException("Hazard equipment fork aliases authority");
            yield return "equipment.fork.mechanics";
            done(next);
        }
        private bool Available(string kind)
        {
            switch (kind)
            {
                case "barrier": case "bridge": case "pump": case "skiff": case "beacon": case "generator": case "relay": return true;
                case "firebreak": case "sprinkler": case "crawler": return mechanics != null && mechanics.FireAvailable;
                case "brace": case "winch": return mechanics != null && mechanics.StructureAvailable;
                case "cooler": case "shelter": return mechanics != null && mechanics.LavaAvailable;
                default: return false;
            }
        }
        public ValidationResult Preview(GameCommand command, EquipmentFrame frame, string phase, PowerFrame powerFrame = null) => PreviewWork(command, frame, phase, powerFrame).Drain();
        public ResumableWork<ValidationResult> PreviewWork(GameCommand command, EquipmentFrame frame, string phase, PowerFrame powerFrame = null) =>
            new ResumableWork<ValidationResult>(done => PreviewRoutine(command, frame, phase, powerFrame, done));
        private IEnumerable<string> PreviewRoutine(GameCommand command, EquipmentFrame frame, string phase, PowerFrame powerFrame, Action<ValidationResult> done)
        {
            using (var check = CheckFrameWork(frame)) { while (check.MoveNext()) yield return check.Stage; }
            var phaseCheck = new ValidationResult(phase == "active" || phase == "stabilizing" || phase == "planning" ? "OK" : "INVALID_PHASE");
            ValidationResult commandCheck;
            if (command.Payload is PlaceEquipment place)
            { using (var check = ValidatePlaceWork(place, command.Sequence, frame)) { while (check.MoveNext()) yield return check.Stage; commandCheck = check.Result; } }
            else if (command.Payload is MoveIdleEquipment move)
            { using (var check = ValidateMoveWork(move, frame)) { while (check.MoveNext()) yield return check.Stage; commandCheck = check.Result; } }
            else if (command.Payload is ActivateTool activate)
            { using (var check = ValidateActivationWork(activate, frame, powerFrame)) { while (check.MoveNext()) yield return check.Stage; commandCheck = check.Result; } }
            else if (command.Payload is SetPowerPriority priority)
            {
                JsonValue power; using (var read = Power.ExportPowerWork()) { while (read.MoveNext()) yield return read.Stage; power = read.Result; }
                string[] expected, supplied;
                using (var sort = ResumableSort.Create(power["consumer_demands"].Items.Select(x => x["id"].Text), StringComparer.Ordinal)) { while (sort.MoveNext()) yield return sort.Stage; expected = sort.Result; }
                using (var sort = ResumableSort.Create(priority.ConsumerIds, StringComparer.Ordinal)) { while (sort.MoveNext()) yield return sort.Stage; supplied = sort.Result; }
                bool equal = supplied.Length == expected.Length;
                for (int i = 0; equal && i < expected.Length; i++) { equal = supplied[i] == expected[i]; yield return "equipment.preview.priority"; }
                commandCheck = new ValidationResult(equal ? "OK" : "UNKNOWN_TARGET");
            }
            else commandCheck = new ValidationResult("UNKNOWN_KIND");
            done(ValidationResult.First(phaseCheck, commandCheck));
        }
        private ResumableWork<ValidationResult> ValidatePlaceWork(PlaceEquipment p, long sequence, EquipmentFrame frame) =>
            new ResumableWork<ValidationResult>(done => ValidatePlaceRoutine(p, sequence, frame, done));
        private IEnumerable<string> ValidatePlaceRoutine(PlaceEquipment p, long sequence, EquipmentFrame frame, Action<ValidationResult> done)
        {
            if (!definitions.TryGetValue(p.EquipmentId, out var definition) || !sockets.TryGetValue(p.SocketId, out var socket) || !Available(p.EquipmentId) || !mission.permitted_equipment_ids.Contains(p.EquipmentId) || WorldOrder(definition.first_world_id) > WorldOrder(mission.world_id)) { done(new ValidationResult("UNKNOWN_TARGET")); yield break; }
            var checks = new List<ValidationResult> {
                R(SuppliesRemaining >= definition.supply_cost, "INSUFFICIENT_SUPPLIES"),
                R(selected.ContainsKey(p.EquipmentId) && deployments[p.EquipmentId] < selected[p.EquipmentId] && instances.Count < 512, "STOCK_EXHAUSTED")
            };
            using (var check = CompatibleWork(definition, socket, p.OrientationStep)) { while (check.MoveNext()) yield return check.Stage; checks.Add(check.Result); }
            bool occupied = instances.ContainsKey(EntityId(sequence));
            if (!occupied) using (var check = OccupiedWork(socket, null)) { while (check.MoveNext()) yield return check.Stage; occupied = check.Result; }
            checks.Add(R(!occupied, "OCCUPIED"));
            checks.Add(R(frame.Sockets[socket.id].Safe && !frame.Sockets[socket.id].Collapsed && !frame.Sockets[socket.id].HotLava, "UNSAFE_TARGET"));
            using (var check = BindingsWork(definition, socket)) { while (check.MoveNext()) yield return check.Stage; checks.Add(check.Result); }
            if (NeedsMechanics(p.EquipmentId)) checks.Add(mechanics.ValidateDeployment(definition, socket) ?? throw new InvalidOperationException("Missing mechanic deployment result"));
            yield return "equipment.preview.place.mechanics";
            if (IsVehicle(p.EquipmentId))
            {
                var sample = frame.Sockets[socket.id]; checks.Add(R(sample.Occupants == 0 && sample.Reservations == 0, "OCCUPIED"));
                if (socket.node_id != null)
                { using (var probe = VehiclePlacementWork(definition, socket, sequence)) { while (probe.MoveNext()) yield return probe.Stage; if (!probe.Result) checks.Add(new ValidationResult("OCCUPIED")); } }
            }
            if (p.EquipmentId == "brace")
            {
                int count = 0; foreach (var item in instances.Values) { if (item.Kind == "brace" && sockets[item.Socket].support_id == socket.support_id && item.Lifecycle != "broken") count++; yield return "equipment.preview.brace"; }
                checks.Add(R(count < ((BraceParameters)definition.parameters).maximum_per_support, "CAPACITY_FULL"));
            }
            done(ValidationResult.First(checks.ToArray()));
        }
        private ResumableWork<bool> VehiclePlacementWork(EquipmentDef definition, Socket socket, long sequence) =>
            new ResumableWork<bool>(done => VehiclePlacementRoutine(definition, socket, sequence, done));
        private IEnumerable<string> VehiclePlacementRoutine(EquipmentDef definition, Socket socket, long sequence, Action<bool> done)
        {
            // The former synchronous probe converted only InvalidOperationException
            // from both the private fork and deployment into OCCUPIED.
            using (var probe = new ResumableWork<bool>(finish => VehiclePlacementProbe(definition, socket, sequence, finish)))
            {
                while (true)
                {
                    bool more = false, rejected = false;
                    try { more = probe.MoveNext(); } catch (InvalidOperationException) { rejected = true; }
                    if (rejected) { done(false); yield break; }
                    if (!more) { done(probe.Result); yield break; }
                    yield return probe.Stage;
                }
            }
        }
        private IEnumerable<string> VehiclePlacementProbe(EquipmentDef definition, Socket socket, long sequence, Action<bool> done)
        {
            TransportSystem candidate; using (var fork = Transport.ForkWork()) { while (fork.MoveNext()) yield return fork.Stage; candidate = fork.Result; }
            using (var apply = candidate.AddDeployedVehicleWork(Spawn(definition, socket, EntityId(sequence)), EntityId(sequence))) { while (apply.MoveNext()) yield return apply.Stage; }
            done(true);
        }
        private ResumableWork<ValidationResult> ValidateMoveWork(MoveIdleEquipment p, EquipmentFrame frame) =>
            new ResumableWork<ValidationResult>(done => ValidateMoveRoutine(p, frame, done));
        private IEnumerable<string> ValidateMoveRoutine(MoveIdleEquipment p, EquipmentFrame frame, Action<ValidationResult> done)
        {
            if (!instances.TryGetValue(p.EquipmentInstanceId, out var instance) || !sockets.TryGetValue(p.DestinationSocketId, out var destination)) { done(new ValidationResult("UNKNOWN_TARGET")); yield break; }
            var definition = definitions[instance.Kind]; var source = sockets[instance.Socket]; var a = frame.Sockets[source.id]; var b = frame.Sockets[destination.id];
            bool idle = instance.Lifecycle != "broken" && a.Idle && b.Idle && !a.PendingWork && !b.PendingWork && instance.Segment == null;
            if (IsVehicle(instance.Kind) || instance.Kind == "brace" || instance.Kind == "firebreak" && instance.Activated || definition.relocation_policy != "idle_same_zone") idle = false;
            if (new[] { "pump", "generator", "sprinkler", "relay" }.Contains(instance.Kind) && instance.Enabled) idle = false;
            if (instance.Kind == "beacon")
            {
                bool dependent = a.DependentOrder;
                if (!dependent) using (var check = BeaconOrderWork(source)) { while (check.MoveNext()) yield return check.Stage; dependent = check.Result; }
                if (dependent) idle = false;
            }
            if (instance.Kind == "generator" && (a.DepthMm != 0 || b.DepthMm != 0)) idle = false;
            if (instance.Kind == "barrier" && (!BarrierMovable(source, frame) || !BarrierMovable(destination, frame))) idle = false;
            var checks = new List<ValidationResult>();
            using (var check = CompatibleWork(definition, destination, p.OrientationStep)) { while (check.MoveNext()) yield return check.Stage; checks.Add(check.Result); }
            using (var check = BindingsWork(definition, destination)) { while (check.MoveNext()) yield return check.Stage; checks.Add(check.Result); }
            checks.Add(R(source.relocation_zone == destination.relocation_zone, "SOCKET_INCOMPATIBLE"));
            bool occupied; using (var check = OccupiedWork(destination, instance.Id)) { while (check.MoveNext()) yield return check.Stage; occupied = check.Result; }
            bool clear = !occupied && a.Occupants == 0 && a.Reservations == 0 && b.Occupants == 0 && b.Reservations == 0;
            if (clear) using (var check = TransportOccupiedWork(source)) { while (check.MoveNext()) yield return check.Stage; clear = !check.Result; }
            if (clear) using (var check = TransportOccupiedWork(destination)) { while (check.MoveNext()) yield return check.Stage; clear = !check.Result; }
            checks.Add(R(clear, "OCCUPIED")); checks.Add(R(idle, "NOT_IDLE"));
            checks.Add(R(a.Safe && b.Safe && !a.HotLava && !b.HotLava && !a.Collapsed && !b.Collapsed, "UNSAFE_TARGET"));
            done(ValidationResult.First(checks.ToArray()));
        }
        private ResumableWork<ValidationResult> ValidateActivationWork(ActivateTool p, EquipmentFrame frame, PowerFrame powerFrame) =>
            new ResumableWork<ValidationResult>(done => ValidateActivationRoutine(p, frame, powerFrame, done));
        private IEnumerable<string> ValidateActivationRoutine(ActivateTool p, EquipmentFrame frame, PowerFrame powerFrame, Action<ValidationResult> done)
        {
            if (!instances.TryGetValue(p.EquipmentInstanceId, out var instance) || !Available(instance.Kind)) { done(new ValidationResult("UNKNOWN_TARGET")); yield break; }
            var socket = sockets[instance.Socket]; var sample = frame.Sockets[socket.id];
            if (instance.Lifecycle == "broken") { done(new ValidationResult("UNSAFE_TARGET")); yield break; }
            if (p.Action == "disable") { done(new ValidationResult("OK")); yield break; }
            if (p.Action == "enable")
            {
                if (instance.Kind == "generator")
                {
                    if (powerFrame == null) throw new InvalidOperationException("Generator activation requires actual power frame");
                    using (var check = CheckPowerEquipmentWork(powerFrame)) { while (check.MoveNext()) yield return check.Stage; }
                    if (powerFrame.OperationTick != frame.OperationTick) throw new InvalidOperationException("Generator frame boundary mismatch");
                    bool safe = sample.Safe;
                    if (safe) using (var check = Power.CanEnableGeneratorWork(instance.Id, powerFrame)) { while (check.MoveNext()) yield return check.Stage; safe = check.Result; }
                    done(R(safe, "UNSAFE_TARGET")); yield break;
                }
                ValidationResult binding; using (var check = BindingsWork(definitions[instance.Kind], socket)) { while (check.MoveNext()) yield return check.Stage; binding = check.Result; }
                done(ValidationResult.First(binding, R(sample.Safe, "UNSAFE_TARGET"), R(instance.Lifecycle != "exhausted", "RESOURCE_EMPTY"))); yield break;
            }
            if (p.Action == "firebreak")
            {
                if (instance.Kind != "firebreak") { done(new ValidationResult("SOCKET_INCOMPATIBLE")); yield break; }
                if (instance.Activated) { done(new ValidationResult("RESOURCE_EMPTY")); yield break; }
                done(mechanics.ValidateFirebreak(socket) ?? throw new InvalidOperationException("Missing firebreak validation")); yield break;
            }
            if (p.Action == "cool_segment")
            {
                if (instance.Kind != "cooler") { done(new ValidationResult("SOCKET_INCOMPATIBLE")); yield break; }
                done(ValidationResult.First(R(instance.Enabled && socket.enabled && socket.lava_segment_id == p.SegmentId, "SOCKET_INCOMPATIBLE"),
                    R(sample.Idle && instance.Segment == null, "NOT_IDLE"), mechanics.ValidateCooling(socket, p.SegmentId) ?? throw new InvalidOperationException("Missing cooling validation"), R(instance.Charges > 0, "RESOURCE_EMPTY"))); yield break;
            }
            if (p.Action == "refill")
            {
                if (instance.Kind != "cooler" || socket.resource_store_id != p.StoreId || !layout.resources.Any(x => x.id == p.StoreId && x.unit == "coolant_charge")) { done(new ValidationResult("SOCKET_INCOMPATIBLE")); yield break; }
                done(ValidationResult.First(R(sample.Idle && instance.Segment == null, "NOT_IDLE"), R(sample.Safe, "UNSAFE_TARGET"),
                    R(instance.Charges < ((CoolerParameters)definitions[instance.Kind].parameters).capacity_charges, "CAPACITY_FULL"), R(Coolant.Amount(p.StoreId) > 0, "RESOURCE_EMPTY"))); yield break;
            }
            done(new ValidationResult("UNKNOWN_KIND"));
        }
        public EquipmentSystem Apply(GameCommand command, EquipmentFrame frame, string phase, OperationContext context, PowerFrame powerFrame = null) => ApplyWork(command, frame, phase, context, powerFrame).Drain();
        public ResumableWork<EquipmentSystem> ApplyWork(GameCommand command, EquipmentFrame frame, string phase, OperationContext context, PowerFrame powerFrame = null) =>
            new ResumableWork<EquipmentSystem>(done => ApplyRoutine(command, frame, phase, context, powerFrame, done));
        private IEnumerable<string> ApplyRoutine(GameCommand command, EquipmentFrame frame, string phase, OperationContext context, PowerFrame powerFrame, Action<EquipmentSystem> done)
        {
            if (context.OperationTick != frame.OperationTick) throw new InvalidOperationException("Equipment boundary mismatch");
            if (phase != "active" && phase != "stabilizing") throw new CommandException("INVALID_PHASE", "Planning commands must execute through C02 queue at an active boundary");
            ValidationResult validation; using (var check = PreviewWork(command, frame, phase, powerFrame)) { while (check.MoveNext()) yield return check.Stage; validation = check.Result; }
            if (!validation.Allowed) throw new CommandException(validation.ReasonCode, "Equipment execution revalidation");
            EquipmentSystem next; using (var fork = ForkWork()) { while (fork.MoveNext()) yield return fork.Stage; next = fork.Result; }
            if (command.Payload is PlaceEquipment place)
            {
                var definition = definitions[place.EquipmentId]; var socket = sockets[place.SocketId];
                var instance = new Instance { Id = EntityId(command.Sequence), Kind = place.EquipmentId, Socket = place.SocketId, Orientation = place.OrientationStep, Durability = definition.durability };
                if (definition.parameters is CoolerParameters cooler) instance.Charges = cooler.initial_charges;
                if (definition.parameters is ShelterParameters shelter) instance.Protection = shelter.initial_protection_scenario_ticks;
                if (IsVehicle(instance.Kind)) using (var apply = next.Transport.AddDeployedVehicleWork(Spawn(definition, socket, instance.Id), instance.Id)) { while (apply.MoveNext()) yield return apply.Stage; }
                if (instance.Kind == "brace") next.mechanics.CommitBrace(instance.Id, socket, ((BraceParameters)definition.parameters).stability_bonus);
                next.instances.Add(instance.Id, instance); next.Power.RegisterEquipment(instance.Id, instance.Kind, instance.Socket); next.deployments[instance.Kind]++; next.SuppliesRemaining -= definition.supply_cost;
                Emit(context, "EquipmentPlaced", instance, command.CommandId);
            }
            else if (command.Payload is MoveIdleEquipment move)
            {
                var instance = next.instances[move.EquipmentInstanceId]; instance.Socket = move.DestinationSocketId; instance.Orientation = move.OrientationStep;
                Emit(context, "EquipmentMoved", instance, command.CommandId);
            }
            else if (command.Payload is SetPowerPriority priority) next.Power.SetPriorities(priority.ConsumerIds);
            else if (command.Payload is ActivateTool activate)
            {
                var instance = next.instances[activate.EquipmentInstanceId]; var socket = sockets[instance.Socket];
                switch (activate.Action)
                {
                    case "enable":
                        if (instance.Kind == "generator") using (var enable = next.Power.EnableGeneratorWork(instance.Id, powerFrame)) { while (enable.MoveNext()) yield return enable.Stage; }
                        instance.Enabled = true; break;
                    case "disable": instance.Enabled = false; break;
                    case "firebreak": next.mechanics.ClearFirebreak(socket, ((FirebreakParameters)definitions[instance.Kind].parameters).maximum_cells); instance.Activated = true; instance.Lifecycle = "exhausted"; instance.Enabled = false; break;
                    case "cool_segment": next.mechanics.BeginCooling(instance.Id, socket, activate.SegmentId, context.ScenarioTick, (CoolerParameters)definitions[instance.Kind].parameters); instance.Charges--; instance.Segment = activate.SegmentId; break;
                    case "refill": next.Coolant.TakeCharge(activate.StoreId); instance.Charges++; instance.Lifecycle = "deployed"; break;
                    default: throw new InvalidOperationException("Closed equipment action dispatch");
                }
            }
            yield return "equipment.command.applied_private";
            using (var check = next.ValidateBoundaryWork()) { while (check.MoveNext()) yield return check.Stage; }
            done(next);
        }
        public EquipmentSystem ResolvePower(PowerFrame frame, OperationContext context, out PowerAllocationResult allocation)
        { PowerAllocationResult value = null; var next = ResolvePowerWork(frame, context, x => value = x).Drain(); allocation = value; return next; }
        public ResumableWork<EquipmentSystem> ResolvePowerWork(PowerFrame frame, OperationContext context, Action<PowerAllocationResult> completedAllocation = null) => new ResumableWork<EquipmentSystem>(done => ResolvePowerRoutine(frame, context, completedAllocation, done));
        private IEnumerable<string> ResolvePowerRoutine(PowerFrame frame, OperationContext context, Action<PowerAllocationResult> completedAllocation, Action<EquipmentSystem> done)
        {
            PowerAllocationResult allocation;
            using (var frameValidation = CheckPowerEquipmentWork(frame)) { while (frameValidation.MoveNext()) yield return frameValidation.Stage; } if (frame.OperationTick != context.OperationTick) throw new InvalidOperationException("Power frame boundary mismatch"); EquipmentSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } PowerAllocationResult powerAllocation; using (var workpowerAllocation = next.Power.AllocateWork(frame)) { while (workpowerAllocation.MoveNext()) yield return workpowerAllocation.Stage; powerAllocation = workpowerAllocation.Result; } allocation = powerAllocation;
            foreach (string id in allocation.GeneratorShutdownIds)
            {yield return "equipment.ResolvePower"; 
                next.instances[id].Enabled = false;
                if (context.Events == null) throw new InvalidOperationException("Generator shutdown requires owned event sink");
                context.Events.Emit("HazardStateChanged", id, null, null, EquipmentJson.O("hazard", "power", "subject_id", id, "old_state", "active", "new_state", "safety_tripped"));
            }
            // The exact transition IDs/events are consumed by C06's compiled
            // generator_shutdowns MetricState owner in this boundary. No hidden
            // private history counter or capped recent-event inference exists.
            using (var events = allocation.EmitGrantChangesWork(context)) { while (events.MoveNext()) yield return events.Stage; } using (var validation = next.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } completedAllocation?.Invoke(allocation); done(next);
                }
        // Actual transport frame builders use these availability projections;
        // a missing bridge/winch/shelter is never inferred from decorative art.
        public bool BridgeAdmissionOpen(string socketId, EquipmentFrame frame)
        {
            CheckFrame(frame); if (!sockets.ContainsKey(socketId)) throw new InvalidOperationException("Unknown bridge socket");
            return instances.Values.Any(x => x.Kind == "bridge" && x.Socket == socketId && x.Lifecycle == "deployed" && x.Enabled && frame.Sockets[socketId].Safe && frame.Sockets[socketId].DepthMm <= ((BridgeParameters)definitions[x.Kind].parameters).close_depth_mm);
        }
        public bool EquipmentAtSocket(string socketId, string equipmentId)
        { if (!sockets.ContainsKey(socketId) || !definitions.ContainsKey(equipmentId)) throw new InvalidOperationException("Unknown equipment socket/type"); return instances.Values.Any(x => x.Kind == equipmentId && x.Socket == socketId && x.Lifecycle != "broken" && x.Enabled); }
        public EquipmentSystem SimulateWater(OperationContext context, IReadOnlyDictionary<string, long> seaHeads,
            IReadOnlyDictionary<string, bool> gateOpen, out WaterPulseResult pulse)
        { WaterPulseResult value = null; var next = SimulateWaterWork(context, seaHeads, gateOpen, x => value = x).Drain(); pulse = value; return next; }
        public ResumableWork<EquipmentSystem> SimulateWaterWork(OperationContext context, IReadOnlyDictionary<string, long> seaHeads, IReadOnlyDictionary<string, bool> gateOpen, Action<WaterPulseResult> completedPulse = null) => new ResumableWork<EquipmentSystem>(done => SimulateWaterRoutine(context, seaHeads, gateOpen, completedPulse, done));
        private IEnumerable<string> SimulateWaterRoutine(OperationContext context, IReadOnlyDictionary<string, long> seaHeads, IReadOnlyDictionary<string, bool> gateOpen, Action<WaterPulseResult> completedPulse, Action<EquipmentSystem> done)
        {
            WaterPulseResult pulse;
            if (!context.HazardPulse || context.OperationTick <= lastWaterOperation) throw new InvalidOperationException("Water requires a fresh hazard boundary");
            EquipmentSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } next.lastWaterOperation = context.OperationTick;
            using (var waterPulse = next.Water.PulseWork(next.instances.Values.Where(x => x.Kind == "pump").Select(x => new WaterPump(x.Id, x.Socket, x.Enabled && x.Lifecycle == "deployed", next.Power.IsPowered(x.Id), x.Remainder)),
                next.instances.Values.Where(x => x.Kind == "sprinkler").Select(x => new WaterSprinkler(x.Id, x.Socket, x.Enabled && x.Lifecycle == "deployed", next.Power.IsPowered(x.Id))),
                seaHeads, gateOpen, next.instances.Values.Where(x => x.Kind == "barrier" && x.Lifecycle != "broken").Select(x => x.Socket))) { while (waterPulse.MoveNext()) yield return waterPulse.Stage; pulse = waterPulse.Result; }
            foreach (var pair in pulse.PumpRemainders) { yield return "equipment.SimulateWater"; next.instances[pair.Key].Remainder = pair.Value; }
            foreach (var pair in pulse.SprayGrants.Where(x => x.Value > 0))
            {yield return "equipment.SimulateWater";  if (next.mechanics == null || !next.mechanics.FireAvailable) throw new InvalidOperationException("Actual fire spray consumer unavailable"); next.mechanics.ApplySpray(sockets[next.instances[pair.Key].Socket], pair.Value); }
            using (var events = pulse.EmitTransfersWork(context)) { while (events.MoveNext()) yield return events.Stage; } using (var validation = next.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } completedPulse?.Invoke(pulse); done(next);
                }
        public EquipmentSystem HazardDamage(OperationContext context, EquipmentFrame frame, out IReadOnlyCollection<string> protectedShelters)
        { IReadOnlyCollection<string> value = null; var next = HazardDamageWork(context, frame, x => value = x).Drain(); protectedShelters = value; return next; }
        public ResumableWork<EquipmentSystem> HazardDamageWork(OperationContext context, EquipmentFrame frame, Action<IReadOnlyCollection<string>> completedShelters = null) => new ResumableWork<EquipmentSystem>(done => HazardDamageRoutine(context, frame, completedShelters, done));
        private IEnumerable<string> HazardDamageRoutine(OperationContext context, EquipmentFrame frame, Action<IReadOnlyCollection<string>> completedShelters, Action<EquipmentSystem> done)
        {
            IReadOnlyCollection<string> protectedShelters;
            using (var check = CheckFrameWork(frame)) { while (check.MoveNext()) yield return check.Stage; }
            if (context.OperationTick != frame.OperationTick || context.OperationTick <= lastDamageOperation) throw new InvalidOperationException("Fresh equipment damage boundary required");
            EquipmentSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } next.lastDamageOperation = context.OperationTick;
            var protectedIds = new List<string>();
            foreach (var instance in next.instances.Values)
            {yield return "equipment.HazardDamage"; 
                if (instance.Lifecycle == "broken") continue;
                var sample = frame.Sockets[instance.Socket]; bool protectedInterval = false;
                // Lava arrival is an actual warned stage-5 transition, not the
                // persistent Hot field. Scenario hazards do not advance on a
                // nonpulse operation; the prior interval grants were already
                // consumed through stages 6-8 at its complete boundary.
                if (sample.HotLavaArrival && !context.HazardPulse) throw new InvalidOperationException("Lava arrival requires actual hazard pulse");
                if (instance.Kind == "cooler" && instance.Segment != null && !next.mechanics.CoolingActive(instance.Id, instance.Segment)) instance.Segment = null;
                if (instance.Kind == "shelter" && instance.Enabled && context.HazardPulse && sample.Occupants > 0 && sample.ExternallyHazardous)
                {
                    protectedInterval = instance.Protection > 0;
                    if (protectedInterval)
                    {
                        protectedIds.Add(instance.Id); instance.Protection--;
                        if (instance.Protection == 0)
                        {
                            instance.ExhaustionEmitted = true; instance.Lifecycle = "exhausted";
                            if (context.Events == null) throw new InvalidOperationException("Shelter exhaustion requires event sink");
                            context.Events.Emit("HazardStateChanged", instance.Id, null, null, EquipmentJson.O("hazard", "resource", "subject_id", instance.Id, "old_state", "protected", "new_state", "exhausted"));
                        }
                    }
                }
                bool immediate = sample.Collapsed || sample.HotLavaArrival && !protectedInterval;
                long damage = 0;
                if (context.HazardPulse && !protectedInterval)
                {
                    if (instance.Kind == "barrier") { if (BarrierHeadDifference(sockets[instance.Socket], frame) > ((BarrierParameters)definitions[instance.Kind].parameters).damage_head_mm) damage = ((BarrierParameters)definitions[instance.Kind].parameters).damage_per_pulse; }
                    else if (instance.Kind == "bridge") { if (sample.DepthMm > ((BridgeParameters)definitions[instance.Kind].parameters).damage_depth_mm) damage = 1; }
                    else if (sample.DepthMm > 200 && !IsVehicle(instance.Kind)) damage = 1;
                    // Bridge/barrier overrides affect water exposure only. Fire
                    // still damages every tool; simultaneous causes do not stack.
                    if (sample.Heat >= 600) damage = Math.Max(damage, 1);
                }
                if ((damage > 0 || immediate) && !sample.DamageWarningPublished && !sample.Collapsed) throw new InvalidOperationException("Damage without actual relevant warning");
                if (immediate) instance.Durability = 0;
                else instance.Durability = Math.Max(0, instance.Durability - damage);
                if (instance.Durability == 0 && IsVehicle(instance.Kind)) next.Transport.BreakDeployedVehicle(instance.Id, context);
                if (instance.Durability == 0) { instance.Lifecycle = "broken"; instance.Enabled = false; Emit(context, "EquipmentBroken", instance, null); }
            }
            protectedShelters = protectedIds.AsReadOnly(); using (var validation = next.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } completedShelters?.Invoke(protectedShelters); done(next);
                }
        public EquipmentSystem ProduceCoolant(OperationContext context, IReadOnlyDictionary<string, bool> safeInfrastructure) => ProduceCoolantWork(context, safeInfrastructure).Drain();
        public ResumableWork<EquipmentSystem> ProduceCoolantWork(OperationContext context, IReadOnlyDictionary<string, bool> safeInfrastructure) => new ResumableWork<EquipmentSystem>(done => ProduceCoolantRoutine(context, safeInfrastructure, done));
        private IEnumerable<string> ProduceCoolantRoutine(OperationContext context, IReadOnlyDictionary<string, bool> safeInfrastructure, Action<EquipmentSystem> done)
        {
            if (layout.producers.Count > 0 && (mechanics == null || !mechanics.LavaAvailable)) throw new InvalidOperationException("Cooling works requires actual lava integration");
            if (!safeInfrastructure.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(layout.producers.Select(x => x.infrastructure_id).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))) throw new InvalidOperationException("Exact cooling works safety required");
            EquipmentSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } using (var pulse = next.Coolant.PulseWork(context, layout.producers.ToDictionary(x => x.id, x => safeInfrastructure[x.infrastructure_id] && next.Power.Grant(x.infrastructure_id) == x.power_demand, StringComparer.Ordinal))) { while (pulse.MoveNext()) yield return pulse.Stage; }
            using (var validation = next.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } done(next);
                }
        public JsonValue ReadState() => ReadStateWork().Drain();
        public ResumableWork<JsonValue> ReadStateWork() => new ResumableWork<JsonValue>(ReadStateRoutine);
        private IEnumerable<string> ReadStateRoutine(Action<JsonValue> done)
        {
            JsonValue stock; using (var workstock = DomainWork.Rows(selected, x => EquipmentJson.O("equipment_id", x.Key, "count", x.Value), "equipment.read.stock")) { while (workstock.MoveNext()) yield return workstock.Stage; stock = workstock.Result; }
            JsonValue deploymentRows; using (var workdeploymentRows = DomainWork.Rows(deployments, x => EquipmentJson.O("equipment_id", x.Key, "count", x.Value), "equipment.read.deployment")) { while (workdeploymentRows.MoveNext()) yield return workdeploymentRows.Stage; deploymentRows = workdeploymentRows.Result; }
            JsonValue equipmentRows; using (var workequipmentRows = DomainWork.Rows(instances.Values, InstanceJson, "equipment.read.instance")) { while (workequipmentRows.MoveNext()) yield return workequipmentRows.Stage; equipmentRows = workequipmentRows.Result; }
            JsonValue power; using (var workpower = Power.ExportPowerWork()) { while (workpower.MoveNext()) yield return workpower.Stage; power = workpower.Result; }
            JsonValue stores; using (var workstores = Coolant.ReadStoresWork()) { while (workstores.MoveNext()) yield return workstores.Stage; stores = workstores.Result; }
            JsonValue producers; using (var workproducers = Coolant.ReadProducersWork()) { while (workproducers.MoveNext()) yield return workproducers.Stage; producers = workproducers.Result; }
            done(EquipmentJson.O("supplies_remaining", SuppliesRemaining, "selected_stock", stock, "deployments", deploymentRows,
                "equipment", equipmentRows, "power", power, "stores", stores, "producers", producers));
        }
        private JsonValue InstanceJson(Instance x)
        {
            JsonValue parameters;
            switch (x.Kind)
            {
                case "pump": parameters = EquipmentJson.O("fractional_remainder", x.Remainder); break;
                case "generator": parameters = EquipmentJson.O("trip_latched", Power.GeneratorTripIds.Contains(x.Id)); break;
                case "firebreak": parameters = EquipmentJson.O("activated", x.Activated); break;
                case "cooler": parameters = EquipmentJson.O("charges", x.Charges, "active_segment_id", x.Segment); break;
                case "shelter": parameters = EquipmentJson.O("protection_remaining", x.Protection, "exhaustion_emitted", x.ExhaustionEmitted); break;
                default: parameters = EquipmentJson.O(); break;
            }
            return EquipmentJson.O("id", x.Id, "equipment_id", x.Kind, "socket_id", x.Socket, "orientation_step", x.Orientation,
                "lifecycle", x.Lifecycle, "enabled", x.Enabled, "durability", x.Durability, "parameters", parameters);
        }
        // Full run/content identity and receipt restoration remain C07's owner.
        // This reader validates the existing owned wire fields, retains every
        // lifetime instance, and derives only stage guards from saved O.
        public EquipmentSystem RestoreOwnedState(JsonValue state, long operationTick, Func<EquipmentSystem, PowerFrame> actualPowerFrame,
            WaterSystem restoredWater = null, TransportSystem restoredTransport = null) => RestoreOwnedStateWork(state, operationTick, actualPowerFrame, restoredWater, restoredTransport).Drain();
        public ResumableWork<EquipmentSystem> RestoreOwnedStateWork(JsonValue state, long operationTick, Func<EquipmentSystem, PowerFrame> actualPowerFrame, WaterSystem restoredWater = null, TransportSystem restoredTransport = null, Func<EquipmentSystem, ResumableWork<PowerFrame>> actualPowerFrameWork = null) => new ResumableWork<EquipmentSystem>(done => RestoreOwnedStateRoutine(state, operationTick, actualPowerFrame, restoredWater, restoredTransport, actualPowerFrameWork, done));
        private IEnumerable<string> RestoreOwnedStateRoutine(JsonValue state, long operationTick, Func<EquipmentSystem, PowerFrame> actualPowerFrame, WaterSystem restoredWater, TransportSystem restoredTransport, Func<EquipmentSystem, ResumableWork<PowerFrame>> actualPowerFrameWork, Action<EquipmentSystem> done)
        {
            EquipmentJson.Fields(state, "supplies_remaining", "selected_stock", "deployments", "equipment", "power", "stores", "producers");
            if (operationTick < 0 || operationTick > Tick.Maximum || state["supplies_remaining"].Kind != "integer") throw new InvalidOperationException("Equipment saved boundary");
            EquipmentSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } next.instances.Clear(); next.deployments.Clear();
            // C07 restores these single owners together with equipment. A
            // partially constructed live owner is never published or repaired.
            if ((restoredWater == null) != (restoredTransport == null)) throw new InvalidOperationException("Restore both water and transport owners together");
            if (restoredWater != null) { WaterSystem water; using (var workwater = restoredWater.ForkWork()) { while (workwater.MoveNext()) yield return workwater.Stage; water = workwater.Result; }TransportSystem transport; using (var worktransport = restoredTransport.ForkWork()) { while (worktransport.MoveNext()) yield return worktransport.Stage; transport = worktransport.Result; } next.Water = water; next.Transport = transport; }
            JsonValue expectedStock; using (var workexpectedStock = DomainWork.Rows(selected, x => EquipmentJson.O("equipment_id", x.Key, "count", x.Value), "equipment.restore.selected")) { while (workexpectedStock.MoveNext()) yield return workexpectedStock.Stage; expectedStock = workexpectedStock.Result; }bool stockMatches; using (var workstockMatches = state["selected_stock"].CanonicalEqualsWork(expectedStock)) { while (workstockMatches.MoveNext()) yield return workstockMatches.Stage; stockMatches = workstockMatches.Result; } if (!stockMatches) throw new InvalidOperationException("Saved selected stock mismatch");
            foreach (var row in state["deployments"].Items)
            {yield return "equipment.restore";  EquipmentJson.Fields(row, "equipment_id", "count"); if (row["count"].Kind != "integer" || row["count"].Integer < 0) throw new InvalidOperationException("Deployment count"); next.deployments.Add(row["equipment_id"].Text, row["count"].Integer); }
            if (!next.deployments.Keys.SequenceEqual(selected.Keys)) throw new InvalidOperationException("Exact lifetime stock rows required");
            var tripClaims = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (var row in state["equipment"].Items)
            {yield return "equipment.restore"; 
                EquipmentJson.Fields(row, "id", "equipment_id", "socket_id", "orientation_step", "lifecycle", "enabled", "durability", "parameters");
                string id = row["id"].Text, kind = row["equipment_id"].Text, socket = row["socket_id"].Text;
                if (!definitions.ContainsKey(kind) || !selected.ContainsKey(kind) || !sockets.ContainsKey(socket) || !mission.permitted_equipment_ids.Contains(kind) || WorldOrder(definitions[kind].first_world_id) > WorldOrder(mission.world_id)) throw new InvalidOperationException("Saved equipment identity");
                if (row["orientation_step"].Kind != "integer" || row["orientation_step"].Integer < 0 || row["orientation_step"].Integer > 3 || row["durability"].Kind != "integer" || row["enabled"].Kind != "boolean" || !new[] { "deployed", "broken", "exhausted" }.Contains(row["lifecycle"].Text)) throw new InvalidOperationException("Saved equipment field types");
                var x = new Instance { Id = id, Kind = kind, Socket = socket, Orientation = (int)row["orientation_step"].Integer, Lifecycle = row["lifecycle"].Text, Enabled = row["enabled"].Boolean, Durability = row["durability"].Integer };
                var p = row["parameters"];
                switch (kind)
                {
                    case "pump": EquipmentJson.Fields(p, "fractional_remainder"); x.Remainder = p["fractional_remainder"].Integer; break;
                    case "generator": EquipmentJson.Fields(p, "trip_latched"); if (p["trip_latched"].Kind != "boolean") throw new InvalidOperationException("Generator latch bool"); tripClaims.Add(id, p["trip_latched"].Boolean); break;
                    case "firebreak": EquipmentJson.Fields(p, "activated"); if (p["activated"].Kind != "boolean") throw new InvalidOperationException("Firebreak activation bool"); x.Activated = p["activated"].Boolean; break;
                    case "cooler": EquipmentJson.Fields(p, "charges", "active_segment_id"); x.Charges = p["charges"].Integer; x.Segment = p["active_segment_id"].IsNull ? null : p["active_segment_id"].Text; if (x.Segment != null && (sockets[socket].lava_segment_id != x.Segment || next.mechanics == null || !next.mechanics.LavaAvailable || !next.mechanics.CoolingActive(id, x.Segment))) throw new InvalidOperationException("Saved cooler active binding"); break;
                    case "shelter": EquipmentJson.Fields(p, "protection_remaining", "exhaustion_emitted"); x.Protection = p["protection_remaining"].Integer; if (p["exhaustion_emitted"].Kind != "boolean") throw new InvalidOperationException("Shelter exhaustion bool"); x.ExhaustionEmitted = p["exhaustion_emitted"].Boolean; if (x.ExhaustionEmitted != (x.Protection == 0)) throw new InvalidOperationException("Shelter exhaustion coherence"); break;
                    default: EquipmentJson.Fields(p); break;
                }
                next.instances.Add(id, x);
            }
            next.SuppliesRemaining = state["supplies_remaining"].Integer;
            FiniteCoolantSystem coolant; using (var workcoolant = next.Coolant.RestoreWork(state["stores"], state["producers"], operationTick)) { while (workcoolant.MoveNext()) yield return workcoolant.Stage; coolant = workcoolant.Result; } next.Coolant = coolant;
            PowerFrame frame;
            if (actualPowerFrameWork == null) { frame = actualPowerFrame(next); yield return "equipment.restore.power.frame.atomic"; }
            else { using (var work = actualPowerFrameWork(next)) { while (work.MoveNext()) yield return work.Stage; frame = work.Result; } }
            if (frame == null) throw new InvalidOperationException("Missing restored power safety"); if (frame.OperationTick != operationTick) throw new InvalidOperationException("Restored power frame boundary"); using (var frameValidation = next.CheckPowerEquipmentWork(frame)) { while (frameValidation.MoveNext()) yield return frameValidation.Stage; }
            PowerSystem power; using (var workpower = next.Power.RestorePowerWork(state["power"], frame)) { while (workpower.MoveNext()) yield return workpower.Stage; power = workpower.Result; } next.Power = power;
            foreach (var pair in tripClaims) { yield return "equipment.restore"; if (pair.Value != next.Power.GeneratorTripIds.Contains(pair.Key) || pair.Value && next.instances[pair.Key].Enabled) throw new InvalidOperationException("Generator latch single-owner mismatch"); }
            next.lastDamageOperation = operationTick; next.lastWaterOperation = operationTick; using (var validation = next.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } done(next);
                }
        public void ValidateBoundary() => ValidateBoundaryWork().Drain();
        public ResumableWork<bool> ValidateBoundaryWork() => new ResumableWork<bool>(done => ValidateBoundaryRoutine(done));
        private IEnumerable<string> ValidateBoundaryRoutine(Action<bool> done)
        {
            if (instances.Count > 512 || SuppliesRemaining < 0) throw new InvalidOperationException("Lifetime supply conservation");
            long spent = 0;
            foreach (var instance in instances.Values) { spent = checked(spent + definitions[instance.Kind].supply_cost); yield return "equipment.validate.supply"; }
            if (SuppliesRemaining != mission.supply_budget - spent) throw new InvalidOperationException("Lifetime supply conservation");
            foreach (var pair in deployments)
            {
                int count = 0;
                foreach (var instance in instances.Values) { if (instance.Kind == pair.Key) count = checked(count + 1); yield return "equipment.validate.stock"; }
                if (pair.Value != count || pair.Value > selected[pair.Key]) throw new InvalidOperationException("Lifetime stock conservation");
                yield return "equipment.validate";
            }
            foreach (var instance in instances.Values)
            {yield return "equipment.validate"; 
                var definition = definitions[instance.Kind];
                if (!Regex.IsMatch(instance.Id, "^eq:[1-9][0-9]{0,4}\\z", RegexOptions.CultureInvariant) || long.Parse(instance.Id.Substring(3), CultureInfo.InvariantCulture) > 10000 || instance.Durability < 0 || instance.Durability > definition.durability || (instance.Lifecycle == "broken") != (instance.Durability == 0) || instance.Lifecycle == "broken" && instance.Enabled) throw new InvalidOperationException("Equipment identity/state invariant");
                using (var check = CompatibleWork(definition, sockets[instance.Socket], instance.Orientation)) { while (check.MoveNext()) yield return check.Stage; if (!check.Result.Allowed) throw new InvalidOperationException("Equipment identity/state invariant"); }
                if (instance.Remainder < 0 || instance.Remainder > 49999 || instance.Charges < 0 || instance.Charges > 1 || instance.Protection < 0 || instance.Protection > 600) throw new InvalidOperationException("Equipment consumable bounds");
                if (instance.Kind == "firebreak" && instance.Activated && instance.Lifecycle != "exhausted" && instance.Lifecycle != "broken") throw new InvalidOperationException("Firebreak activation lost");
                if (instance.Lifecycle != "broken")
                { using (var occupied = OccupiedWork(sockets[instance.Socket], instance.Id)) { while (occupied.MoveNext()) yield return occupied.Stage; if (occupied.Result) throw new InvalidOperationException("Equipment footprint overlap"); } }
            }
            JsonValue transportState; using (var worktransportState = Transport.ReadStateWork()) { while (worktransportState.MoveNext()) yield return worktransportState.Stage; transportState = worktransportState.Result; } var vehicleRows = transportState["vehicles"].Items;
            var boundVehicles = new LinkedList<JsonValue>();
            foreach (var vehicle in vehicleRows) { if (!vehicle["equipment_instance_id"].IsNull) boundVehicles.AddLast(vehicle); yield return "equipment.validate.vehicle_set"; }
            int vehicleEquipmentCount = 0;
            foreach (var instance in instances.Values) { if (IsVehicle(instance.Kind)) vehicleEquipmentCount = checked(vehicleEquipmentCount + 1); yield return "equipment.validate.vehicle_equipment"; }
            if (boundVehicles.Count != vehicleEquipmentCount) throw new InvalidOperationException("Deployed vehicle equipment bijection");
            var boundIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var vehicle in boundVehicles) { boundIds.Add(vehicle["equipment_instance_id"].Text); yield return "equipment.validate.vehicle_bijection"; }
            if (boundIds.Count != boundVehicles.Count) throw new InvalidOperationException("Deployed vehicle equipment bijection");
            foreach (var vehicle in boundVehicles)
            {yield return "equipment.validate"; 
                string equipmentId = vehicle["equipment_instance_id"].Text;
                if (!instances.TryGetValue(equipmentId, out var equipment) || !IsVehicle(equipment.Kind) || vehicle["id"].Text != equipment.Id || vehicle["kind"].Text != equipment.Kind) throw new InvalidOperationException("Deployed vehicle identity/binding mismatch");
                // Transport can legitimately fail an intact vehicle when both
                // transit endpoints are unsafe. Equipment destruction requires
                // failure, while route failure never invents equipment damage.
                if (equipment.Lifecycle == "broken" && vehicle["status"].Text != "failed") throw new InvalidOperationException("Broken vehicle must remain terminal failed");
                if (equipment.Lifecycle == "exhausted") throw new InvalidOperationException("Vehicle failure is not consumable exhaustion");
            }
            foreach (var vehicle in vehicleRows)
            {yield return "equipment.validate";
                if (!vehicle["equipment_instance_id"].IsNull) continue;
                VehicleSpawn spawn;
                using (var scan = SingleOrDefaultWork(layout.fixed_vehicles, x => x.vehicle_id == vehicle["id"].Text)) { while (scan.MoveNext()) yield return scan.Stage; spawn = scan.Result; }
                if (spawn == null || spawn.kind != vehicle["kind"].Text) throw new InvalidOperationException("Unbound transport vehicle is not authored fixed content");
            }
            long feedConsumed = 0, initialStoreCharges = 0, storedCharges = 0, equipmentCharges = 0, wip = 0;
            foreach (var store in layout.resources) { if (store.unit == "coolant_feedstock") { long consumed = store.initial_amount - Coolant.Amount(store.id); feedConsumed = checked(feedConsumed + consumed); } yield return "equipment.validate.feedstock"; }
            foreach (var store in layout.resources) { if (store.unit == "coolant_charge") initialStoreCharges = checked(initialStoreCharges + store.initial_amount); yield return "equipment.validate.initial_charge"; }
            int coolerCount = 0;
            foreach (var instance in instances.Values) { if (instance.Kind == "cooler") coolerCount = checked(coolerCount + 1); yield return "equipment.validate.cooler_count"; }
            long initialCharges = initialStoreCharges + coolerCount;
            foreach (var store in layout.resources) { if (store.unit == "coolant_charge") storedCharges = checked(storedCharges + Coolant.Amount(store.id)); yield return "equipment.validate.store_charge"; }
            foreach (var instance in instances.Values) { if (instance.Kind == "cooler") equipmentCharges = checked(equipmentCharges + instance.Charges); yield return "equipment.validate.equipment_charge"; }
            long ownedCharges = storedCharges + equipmentCharges;
            JsonValue producerRows; using (var read = Coolant.ReadProducersWork()) { while (read.MoveNext()) yield return read.Stage; producerRows = read.Result; }
            foreach (var row in producerRows.Items) { wip = checked(wip + row["work_in_progress_units"].Integer); yield return "equipment.validate.wip"; }
            if (ownedCharges + wip > initialCharges + feedConsumed) throw new InvalidOperationException("Coolant store/equipment/WIP conservation");
            using (var waterValidation = Water.ValidateBoundaryWork()) { while (waterValidation.MoveNext()) yield return waterValidation.Stage; }using (var transportValidation = Transport.ValidateBoundaryWork()) { while (transportValidation.MoveNext()) yield return transportValidation.Stage; }using (var coolantValidation = Coolant.ValidateBoundaryWork()) { while (coolantValidation.MoveNext()) yield return coolantValidation.Stage; }
        
            done(true);
        }
        private void CheckFrame(EquipmentFrame frame) => CheckFrameWork(frame).Drain();
        private ResumableWork<bool> CheckFrameWork(EquipmentFrame frame) => new ResumableWork<bool>(done => CheckFrameRoutine(frame, done));
        private IEnumerable<string> CheckFrameRoutine(EquipmentFrame frame, Action<bool> done)
        {
            if (frame == null || frame.Sockets.Count != sockets.Count) throw new InvalidOperationException("Exact mandatory equipment environment required");
            foreach (string id in sockets.Keys)
            { if (!frame.Sockets.ContainsKey(id)) throw new InvalidOperationException("Exact mandatory equipment environment required"); yield return "equipment.environment.socket"; }
            done(true);
        }
        private void CheckPowerEquipment(PowerFrame frame) => CheckPowerEquipmentWork(frame).Drain();
        private ResumableWork<bool> CheckPowerEquipmentWork(PowerFrame frame) => new ResumableWork<bool>(done => CheckPowerEquipmentRoutine(frame, done));
        private IEnumerable<string> CheckPowerEquipmentRoutine(PowerFrame frame, Action<bool> done)
        {
            if (frame == null) throw new InvalidOperationException("Missing power frame");
            if (frame.Equipment.Count != instances.Count) throw new InvalidOperationException("Power equipment frame disagrees with owned state");
            foreach (var item in instances.Values)
            {
                if (!frame.Equipment.TryGetValue(item.Id, out var supplied) || supplied.EquipmentId != item.Kind || supplied.SocketId != item.Socket || supplied.Enabled != item.Enabled || supplied.Intact != (item.Lifecycle != "broken"))
                    throw new InvalidOperationException("Power equipment frame disagrees with owned state");
                yield return "equipment.power.frame.instance";
            }
            done(true);
        }
        private ResumableWork<bool> TransportOccupiedWork(Socket socket) => new ResumableWork<bool>(done => TransportOccupiedRoutine(socket, done));
        private IEnumerable<string> TransportOccupiedRoutine(Socket socket, Action<bool> done)
        {
            JsonValue state; using (var read = Transport.ReadStateWork()) { while (read.MoveNext()) yield return read.Stage; state = read.Result; }
            var edgeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in layout.nav_edges) { if (edge.required_equipment_socket_id == socket.id) edgeIds.Add(edge.id); yield return "equipment.transport.binding"; }
            foreach (var row in state["transits"].Items) { if (edgeIds.Contains(row["edge_id"].Text)) { done(true); yield break; } yield return "equipment.transport.transit"; }
            foreach (var row in state["reservations"].Items) { if (row["resource_kind"].Text == "edge_slots" && edgeIds.Contains(row["resource_id"].Text)) { done(true); yield break; } yield return "equipment.transport.reservation"; }
            if (socket.kind == "refuge" && socket.node_id != null)
            {
                foreach (var row in state["residents"].Items) { if (row["location"]["kind"].Text == "node" && row["location"]["node_id"].Text == socket.node_id) { done(true); yield break; } yield return "equipment.transport.resident"; }
                foreach (var row in state["reservations"].Items) { if (row["resource_id"].Text == socket.node_id) { done(true); yield break; } yield return "equipment.transport.refuge"; }
            }
            done(false);
        }
        private ResumableWork<bool> BeaconOrderWork(Socket socket) => new ResumableWork<bool>(done => BeaconOrderRoutine(socket, done));
        private IEnumerable<string> BeaconOrderRoutine(Socket socket, Action<bool> done)
        {
            JsonValue state; using (var read = Transport.ReadStateWork()) { while (read.MoveNext()) yield return read.Stage; state = read.Result; }
            foreach (var route in state["routes"].Items)
            {
                yield return "equipment.beacon.route";
                if (route["subject_kind"].Text != "group") continue;
                foreach (var stop in route["stops"].Items.Skip((int)route["next_stop_index"].Integer))
                { if (stop["node_id"].Text == socket.node_id) { done(true); yield break; } yield return "equipment.beacon.stop"; }
            }
            done(false);
        }
        private ResumableWork<bool> OccupiedWork(Socket socket, string except) => new ResumableWork<bool>(done => OccupiedRoutine(socket, except, done));
        private IEnumerable<string> OccupiedRoutine(Socket socket, string except, Action<bool> done)
        {
            var footprint = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in socket.footprint_cell_ids) { footprint.Add(id); yield return "equipment.occupied.footprint"; }
            foreach (var item in instances.Values)
            {
                yield return "equipment.occupied.instance";
                if (item.Id == except || item.Lifecycle == "broken") continue;
                if (item.Socket == socket.id) { done(true); yield break; }
                foreach (string id in sockets[item.Socket].footprint_cell_ids)
                { if (footprint.Contains(id)) { done(true); yield break; } yield return "equipment.occupied.cell"; }
            }
            done(false);
        }
        private static ValidationResult R(bool valid, string code) => new ValidationResult(valid ? "OK" : code);
        private static ResumableWork<ValidationResult> CompatibleWork(EquipmentDef definition, Socket socket, int orientation)
            => new ResumableWork<ValidationResult>(done => CompatibleRoutine(definition, socket, orientation, done));
        private static IEnumerable<string> CompatibleRoutine(EquipmentDef definition, Socket socket, int orientation, Action<ValidationResult> done)
        {
            bool valid = socket.enabled;
            if (valid) using (var scan = AnyWork(socket.permitted_equipment_ids, x => x == definition.id)) { while (scan.MoveNext()) yield return scan.Stage; valid = scan.Result; }
            if (valid) using (var scan = AnyWork(definition.socket_kinds, x => x == socket.kind)) { while (scan.MoveNext()) yield return scan.Stage; valid = scan.Result; }
            if (valid) using (var scan = AnyWork(socket.orientation_steps, x => x == orientation)) { while (scan.MoveNext()) yield return scan.Stage; valid = scan.Result; }
            done(R(valid, "SOCKET_INCOMPATIBLE"));
        }
        private static ResumableWork<bool> AnyWork<T>(IEnumerable<T> rows, Func<T, bool> matches) => new ResumableWork<bool>(done => AnyRoutine(rows, matches, done));
        private static IEnumerable<string> AnyRoutine<T>(IEnumerable<T> rows, Func<T, bool> matches, Action<bool> done)
        {
            foreach (var row in rows) { bool match = matches(row); yield return "equipment.binding.scan"; if (match) { done(true); yield break; } }
            done(false);
        }
        private static ResumableWork<T> SingleOrDefaultWork<T>(IEnumerable<T> rows, Func<T, bool> matches) where T : class
            => new ResumableWork<T>(done => SingleOrDefaultRoutine(rows, matches, done));
        private static IEnumerable<string> SingleOrDefaultRoutine<T>(IEnumerable<T> rows, Func<T, bool> matches, Action<T> done) where T : class
        {
            T result = null; bool found = false;
            foreach (var row in rows)
            {
                if (matches(row))
                {
                    if (found) throw new InvalidOperationException("Sequence contains more than one matching element");
                    result = row; found = true;
                }
                yield return "equipment.binding.single";
            }
            done(result);
        }
        private ResumableWork<ValidationResult> BindingsWork(EquipmentDef d, Socket s) => new ResumableWork<ValidationResult>(done => BindingsRoutine(d, s, done));
        private IEnumerable<string> BindingsRoutine(EquipmentDef d, Socket s, Action<ValidationResult> done)
        {
            bool valid = false;
            switch (d.id)
            {
                case "barrier": using (var check = AnyWork(layout.hydraulic_links, x => x.id == s.hydraulic_link_id && x.barrier_socket_id == s.id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "bridge": using (var check = AnyWork(layout.nav_edges, x => x.required_equipment_socket_id == s.id && x.traversal_class == "ground")) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "pump":
                    valid = s.intake_node_id != s.outlet_node_id;
                    if (valid) using (var check = AnyWork(layout.water_nodes, x => x.id == s.intake_node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    if (valid) using (var check = AnyWork(layout.water_nodes, x => x.id == s.outlet_node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    if (valid) using (var check = AnyWork(layout.power_nodes, x => x.id == s.power_node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    break;
                case "skiff": using (var check = AnyWork(layout.nav_nodes, x => x.id == s.node_id && x.kind == "dock")) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "beacon": using (var check = AnyWork(layout.nav_nodes, x => x.id == s.node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "generator": using (var check = AnyWork(layout.power_nodes, x => x.id == s.power_node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "firebreak":
                    valid = s.fire_cell_ids.Count > 0 && s.fire_cell_ids.Count <= ((FirebreakParameters)d.parameters).maximum_cells;
                    if (valid) foreach (string id in s.fire_cell_ids)
                    { using (var check = AnyWork(layout.fire_cells, x => x.id == id && !x.building_cell)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } if (!valid) break; }
                    break;
                case "sprinkler":
                    valid = s.fire_cell_ids.Count > 0 && s.fire_cell_ids.Count <= ((SprinklerParameters)d.parameters).maximum_cells;
                    if (valid) using (var check = AnyWork(layout.resources, x => x.id == s.resource_store_id && x.unit == "water_mm3")) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    if (valid) using (var check = AnyWork(layout.power_nodes, x => x.id == s.power_node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    break;
                case "crawler": using (var check = AnyWork(layout.nav_nodes, x => x.id == s.node_id && x.vehicle_slots > 0)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "brace": using (var check = AnyWork(layout.supports, x => x.id == s.support_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "winch":
                    using (var check = AnyWork(layout.nav_nodes, x => x.id == s.node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    if (valid) using (var check = AnyWork(layout.nav_edges, x => x.required_equipment_socket_id == s.id && x.traversal_class == "cable")) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    if (valid) using (var check = AnyWork(layout.power_nodes, x => x.id == s.power_node_id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; }
                    break;
                case "relay": using (var check = AnyWork(layout.power_links, x => x.relay_socket_id == s.id)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "cooler": using (var check = AnyWork(layout.lava_segments, x => x.id == s.lava_segment_id && x.cooler_socket_ids.Contains(s.id))) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
                case "shelter": using (var check = AnyWork(layout.nav_nodes, x => x.id == s.node_id && x.kind == "shelter" && x.resident_capacity >= d.resident_seats)) { while (check.MoveNext()) yield return check.Stage; valid = check.Result; } break;
            }
            done(R(valid, "SOCKET_INCOMPATIBLE"));
        }
        private long Head(string kind, string id, EquipmentFrame frame)
        { if (kind == "sea_boundary") { if (!frame.SeaHeads.ContainsKey(id)) throw new InvalidOperationException("Missing actual sea head"); return frame.SeaHeads[id]; } var node = layout.water_nodes.Single(x => x.id == id); return checked(node.elevation_mm + Water.Depth(id)); }
        private long BarrierHeadDifference(Socket socket, EquipmentFrame frame) { var link = layout.hydraulic_links.Single(x => x.id == socket.hydraulic_link_id); return Math.Abs(Head(link.from_kind, link.from_id, frame) - Head(link.to_kind, link.to_id, frame)); }
        private bool BarrierMovable(Socket socket, EquipmentFrame frame)
        { var link = layout.hydraulic_links.Single(x => x.id == socket.hydraulic_link_id); long a = Head(link.from_kind, link.from_id, frame), b = Head(link.to_kind, link.to_id, frame); return Math.Abs(a - b) <= 100 && a <= link.sill_mm + 100 && b <= link.sill_mm + 100; }
        private static bool IsVehicle(string kind) => kind == "skiff" || kind == "crawler" || kind == "winch";
        private static bool NeedsMechanics(string kind) => new[] { "firebreak", "sprinkler", "crawler", "brace", "winch", "cooler", "shelter" }.Contains(kind);
        private static int WorldOrder(string id) { if (id == null || id.Length != 2 || id[0] != 'W' || id[1] < '1' || id[1] > '5') throw new InvalidOperationException("Unknown world introduction"); return id[1] - '0'; }
        private static string EntityId(long sequence) { if (sequence < 1 || sequence > 10000) throw new InvalidOperationException("Deployment sequence"); return "eq:" + sequence.ToString(CultureInfo.InvariantCulture); }
        private static VehicleSpawn Spawn(EquipmentDef definition, Socket socket, string id) => VehicleSpawn.Read(EquipmentJson.O("vehicle_id", id, "kind", definition.id, "node_id", socket.node_id, "seats", definition.resident_seats, "movement_slots", definition.movement_slots, "initial_route_id", null, "initial_occupant_ids", JsonValue.Array(new JsonValue[0])));
        private static void Emit(OperationContext context, string kind, Instance instance, string commandId)
        { if (context.Events == null) throw new InvalidOperationException("Equipment events require actual owned event sink"); context.Events.Emit(kind, instance.Id, commandId, null, EquipmentJson.O("equipment_instance_id", instance.Id, "socket_id", instance.Socket)); }
        private static ResumableWork<bool> ValidateDefinitionWork(EquipmentDef d)
            => new ResumableWork<bool>(done => ValidateDefinitionRoutine(d, done));
        private static IEnumerable<string> ValidateDefinitionRoutine(EquipmentDef d, Action<bool> done)
        {
            // C03/C04 are rules-v1. Explicitly reject unsupported numerical tuning
            // instead of silently using hard-coded water/transport defaults.
            string expected;
            switch (d.id)
            {
                case "barrier": expected = "{\"crest_mm\":600,\"damage_head_mm\":800,\"damage_per_pulse\":1,\"warning_head_mm\":600}"; break;
                case "bridge": expected = "{\"close_depth_mm\":100,\"damage_depth_mm\":500}"; break;
                case "pump": expected = "{\"max_lift_mm\":5000,\"rate_mm3_per_second\":20000000}"; break;
                case "skiff": case "crawler": expected = "{\"boarding_operation_ticks\":10,\"unboarding_operation_ticks\":10}"; break;
                case "beacon": expected = "{}"; break;
                case "generator": expected = "{\"damage_depth_mm\":200,\"restart_depth_mm\":40,\"shutdown_depth_mm\":50}"; break;
                case "firebreak": expected = "{\"maximum_cells\":8,\"uses\":1}"; break;
                case "sprinkler": expected = "{\"maximum_cells\":4,\"rate_mm3_per_second\":5000000}"; break;
                case "brace": expected = "{\"maximum_per_support\":3,\"stability_bonus\":200}"; break;
                case "winch": expected = "{\"brake_operation_ticks\":10,\"crossing_operation_ticks\":50,\"duty_operation_ticks\":100,\"rest_operation_ticks\":30}"; break;
                case "relay": expected = "{\"disconnect_depth_mm\":50,\"reconnect_depth_mm\":40}"; break;
                case "cooler": expected = "{\"capacity_charges\":1,\"cooling_scenario_ticks\":50,\"crust_scenario_ticks\":200,\"initial_charges\":1,\"reheat_scenario_ticks\":50}"; break;
                case "shelter": expected = "{\"initial_protection_scenario_ticks\":600}"; break;
                default: throw new InvalidOperationException("Unknown equipment definition");
            }
            long cost = d.id == "beacon" ? 1 : new[] { "barrier", "firebreak", "brace", "relay" }.Contains(d.id) ? 2 : new[] { "skiff", "crawler", "shelter" }.Contains(d.id) ? 4 : 3;
            long demand = new[] { "pump", "sprinkler", "winch" }.Contains(d.id) ? 1 : 0;
            long seats = d.id == "skiff" ? 4 : d.id == "crawler" || d.id == "shelter" ? 6 : d.id == "winch" ? 2 : 0;
            long slots = d.id == "bridge" || d.id == "crawler" ? 2 : d.id == "skiff" || d.id == "winch" ? 1 : 0;
            if (d.supply_cost != cost || d.power_demand != demand || d.power_supply != (d.id == "generator" ? 2 : 0) || d.resident_seats != seats || d.movement_slots != slots) throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: unsupported equipment quantities");
            string actualParameters, expectedParameters;
            using (var read = d.parameters.ToJson().CanonicalWork()) { while (read.MoveNext()) yield return read.Stage; actualParameters = read.Result; }
            using (var read = StrictJson.Parse(expected).CanonicalWork()) { while (read.MoveNext()) yield return read.Stage; expectedParameters = read.Result; }
            if (actualParameters != expectedParameters || d.durability != 100) throw new InvalidOperationException("CONTENT_VERSION_MISMATCH: unsupported equipment parameters");
            done(true);
        }
    }
}
