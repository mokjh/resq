using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation.Tools
{
    public sealed class PowerNodeSample
    {
        public string Id { get; }
        public long DepthMm { get; }
        public bool Safe { get; }
        public PowerNodeSample(string id, long depthMm, bool safe)
        { PowerSystem.CheckId(id); PowerSystem.Bound(depthMm, 0, 1000000000000L); Id = id; DepthMm = depthMm; Safe = safe; }
    }
    public sealed class PowerLinkSample
    {
        public string Id { get; }
        public long DepthMm { get; }
        public bool Safe { get; }
        public PowerLinkSample(string id, long depthMm, bool safe)
        { PowerSystem.CheckId(id); PowerSystem.Bound(depthMm, 0, 1000000000000L); Id = id; DepthMm = depthMm; Safe = safe; }
    }
    public sealed class PowerEquipment
    {
        public string Id { get; }
        public string EquipmentId { get; }
        public string SocketId { get; }
        public bool Enabled { get; }
        public bool Intact { get; }
        public PowerEquipment(string id, string equipmentId, string socketId, bool enabled, bool intact)
        { PowerSystem.CheckId(id); PowerSystem.CheckId(equipmentId); PowerSystem.CheckId(socketId); Id = id; EquipmentId = equipmentId; SocketId = socketId; Enabled = enabled; Intact = intact; }
    }
    // Safe includes every bound access/support/hazard condition. A zero-demand
    // infrastructure consumer is still ineligible when this sample is false.
    public sealed class PowerConsumerSample
    {
        public string Id { get; }
        public bool Safe { get; }
        public PowerConsumerSample(string id, bool safe) { PowerSystem.CheckId(id); Id = id; Safe = safe; }
    }
    public sealed class PowerFrame
    {
        public long OperationTick { get; }
        public ReadOnlyDictionary<string, PowerNodeSample> Nodes { get; }
        public ReadOnlyDictionary<string, PowerLinkSample> Links { get; }
        public ReadOnlyDictionary<string, PowerEquipment> Equipment { get; }
        public ReadOnlyDictionary<string, PowerConsumerSample> Consumers { get; }
        public PowerFrame(long operationTick, IEnumerable<PowerNodeSample> nodes, IEnumerable<PowerLinkSample> links, IEnumerable<PowerEquipment> equipment, IEnumerable<PowerConsumerSample> consumers)
        {
            var value = CreateWork(operationTick, nodes, links, equipment, consumers).Drain(); OperationTick = value.OperationTick;
            Nodes = value.Nodes; Links = value.Links; Equipment = value.Equipment; Consumers = value.Consumers;
        }
        private PowerFrame(long operationTick, ReadOnlyDictionary<string, PowerNodeSample> nodes, ReadOnlyDictionary<string, PowerLinkSample> links, ReadOnlyDictionary<string, PowerEquipment> equipment, ReadOnlyDictionary<string, PowerConsumerSample> consumers)
        { OperationTick = operationTick; Nodes = nodes; Links = links; Equipment = equipment; Consumers = consumers; }
        public static ResumableWork<PowerFrame> CreateWork(long operationTick, IEnumerable<PowerNodeSample> nodes, IEnumerable<PowerLinkSample> links, IEnumerable<PowerEquipment> equipment, IEnumerable<PowerConsumerSample> consumers) => new ResumableWork<PowerFrame>(done => CreateRoutine(operationTick, nodes, links, equipment, consumers, done));
        private static IEnumerable<string> CreateRoutine(long operationTick, IEnumerable<PowerNodeSample> nodes, IEnumerable<PowerLinkSample> links, IEnumerable<PowerEquipment> equipment, IEnumerable<PowerConsumerSample> consumers, Action<PowerFrame> done)
        {
            new Tick(operationTick);
            SortedDictionary<string, PowerNodeSample> nodesCopy; using (var worknodesCopy = DomainWork.SortedIndex(nodes, x => x.Id, "power.frame.node", PowerSystem.CheckId)) { while (worknodesCopy.MoveNext()) yield return worknodesCopy.Stage; nodesCopy = worknodesCopy.Result; }
            SortedDictionary<string, PowerLinkSample> linksCopy; using (var worklinksCopy = DomainWork.SortedIndex(links, x => x.Id, "power.frame.link", PowerSystem.CheckId)) { while (worklinksCopy.MoveNext()) yield return worklinksCopy.Stage; linksCopy = worklinksCopy.Result; }
            SortedDictionary<string, PowerEquipment> equipmentCopy; using (var workequipmentCopy = DomainWork.SortedIndex(equipment, x => x.Id, "power.frame.equipment", PowerSystem.CheckId)) { while (workequipmentCopy.MoveNext()) yield return workequipmentCopy.Stage; equipmentCopy = workequipmentCopy.Result; }
            SortedDictionary<string, PowerConsumerSample> consumersCopy; using (var workconsumersCopy = DomainWork.SortedIndex(consumers, x => x.Id, "power.frame.consumer", PowerSystem.CheckId)) { while (workconsumersCopy.MoveNext()) yield return workconsumersCopy.Stage; consumersCopy = workconsumersCopy.Result; }
            done(new PowerFrame(operationTick, new ReadOnlyDictionary<string, PowerNodeSample>(nodesCopy), new ReadOnlyDictionary<string, PowerLinkSample>(linksCopy), new ReadOnlyDictionary<string, PowerEquipment>(equipmentCopy), new ReadOnlyDictionary<string, PowerConsumerSample>(consumersCopy)));
        }
        private static ReadOnlyDictionary<string, T> Read<T>(IEnumerable<T> items, Func<T, string> id)
        { return new ReadOnlyDictionary<string, T>(PowerSystem.Index(items ?? throw new ArgumentNullException(nameof(items)), id)); }
    }
    public sealed class PowerAllocationResult
    {
        public IReadOnlyDictionary<string, long> Grants { get; }
        public IReadOnlyDictionary<string, string> ComponentByNode { get; }
        public ReadOnlyCollection<string> GeneratorShutdownIds { get; }
        public ReadOnlyCollection<string> GeneratorTripIds { get; }
        private readonly IReadOnlyDictionary<string, long> previous;
        internal PowerAllocationResult(IDictionary<string, long> grants, IDictionary<string, long> oldGrants,
            IDictionary<string, string> components, IEnumerable<string> shutdowns, IEnumerable<string> trips)
        {
            using (var work = CreateWork(grants, oldGrants, components, shutdowns, trips))
            {
                var value = work.Drain(); Grants = value.Grants; previous = value.previous; ComponentByNode = value.ComponentByNode;
                GeneratorShutdownIds = value.GeneratorShutdownIds; GeneratorTripIds = value.GeneratorTripIds;
            }
        }
        private PowerAllocationResult(ReadOnlyDictionary<string, long> grants, ReadOnlyDictionary<string, long> oldGrants,
            ReadOnlyDictionary<string, string> components, ReadOnlyCollection<string> shutdowns, ReadOnlyCollection<string> trips)
        { Grants = grants; previous = oldGrants; ComponentByNode = components; GeneratorShutdownIds = shutdowns; GeneratorTripIds = trips; }
        internal static ResumableWork<PowerAllocationResult> CreateWork(IDictionary<string, long> grants, IDictionary<string, long> oldGrants,
            IDictionary<string, string> components, IEnumerable<string> shutdowns, IEnumerable<string> trips)
            => new ResumableWork<PowerAllocationResult>(done => CreateRoutine(grants, oldGrants, components, shutdowns, trips, done));
        private static IEnumerable<string> CreateRoutine(IDictionary<string, long> grants, IDictionary<string, long> oldGrants,
            IDictionary<string, string> components, IEnumerable<string> shutdowns, IEnumerable<string> trips, Action<PowerAllocationResult> done)
        {
            if (grants == null) throw new ArgumentNullException("dictionary");
            var grantCopy = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in grants) { grantCopy.Add(pair.Key, pair.Value); yield return "power.result.grant"; }
            if (oldGrants == null) throw new ArgumentNullException("dictionary");
            var previousCopy = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in oldGrants) { previousCopy.Add(pair.Key, pair.Value); yield return "power.result.previous"; }
            if (components == null) throw new ArgumentNullException("dictionary");
            var componentCopy = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in components) { componentCopy.Add(pair.Key, pair.Value); yield return "power.result.component"; }
            string[] shutdownCopy, tripCopy;
            using (var sort = ResumableSort.Create(shutdowns, StringComparer.Ordinal)) { while (sort.MoveNext()) yield return sort.Stage; shutdownCopy = sort.Result; }
            using (var sort = ResumableSort.Create(trips, StringComparer.Ordinal)) { while (sort.MoveNext()) yield return sort.Stage; tripCopy = sort.Result; }
            done(new PowerAllocationResult(new ReadOnlyDictionary<string, long>(grantCopy), new ReadOnlyDictionary<string, long>(previousCopy),
                new ReadOnlyDictionary<string, string>(componentCopy), System.Array.AsReadOnly(shutdownCopy), System.Array.AsReadOnly(tripCopy)));
        }
        public void EmitGrantChanges(OperationContext context) => EmitGrantChangesWork(context).Drain();
        public ResumableWork<bool> EmitGrantChangesWork(OperationContext context) => new ResumableWork<bool>(done => EmitGrantChangesRoutine(context, done));
        private IEnumerable<string> EmitGrantChangesRoutine(OperationContext context, Action<bool> done)
        {
            if (context.Events == null) throw new InvalidOperationException("Power events require the owned operation event sink");
            foreach (var pair in Grants)
            {yield return "power.events"; 
                long old; if (!previous.TryGetValue(pair.Key, out old)) old = 0;
                if (old != pair.Value) context.Events.Emit("PowerGrantChanged", pair.Key, null, null,
                    PowerSystem.Object("consumer_id", pair.Key, "old_grant", old, "new_grant", pair.Value));
            }
        
            done(true);
        }
    }

    // Pure stage-6 graph allocation. Equipment lifecycle and hazard observations
    // are supplied by their owners; no all-safe/default frame is available.
    public sealed class PowerSystem
    {
        private sealed class Consumer
        {
            public string Id, Node;
            public long Demand, DefaultPriority;
            public bool Safe, Active;
        }
        private readonly SortedDictionary<string, PowerNode> nodes;
        private readonly SortedDictionary<string, PowerLink> links;
        private readonly SortedDictionary<string, Socket> sockets;
        private readonly SortedDictionary<string, InfrastructureDef> infrastructure;
        // Availability is the persisted water-hysteresis latch. Desired portable
        // enable is authoritative EquipmentState and is never copied here.
        private SortedDictionary<string, bool> nodeEnabled, linkEnabled;
        private SortedDictionary<string, long> demands, priorities, grants;
        private SortedDictionary<string, bool> eligible;
        private SortedSet<string> trips;
        public IReadOnlyCollection<string> GeneratorTripIds => trips.ToList().AsReadOnly();

        public PowerSystem(LayoutDef layout) : this(true)
        {
            using (var work = InitializeWork(layout)) work.Drain();
        }
        private PowerSystem(bool deferred)
        {
            nodes = new SortedDictionary<string, PowerNode>(StringComparer.Ordinal); links = new SortedDictionary<string, PowerLink>(StringComparer.Ordinal);
            sockets = new SortedDictionary<string, Socket>(StringComparer.Ordinal); infrastructure = new SortedDictionary<string, InfrastructureDef>(StringComparer.Ordinal);
            nodeEnabled = new SortedDictionary<string, bool>(StringComparer.Ordinal); linkEnabled = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            demands = Numbers(); priorities = Numbers(); grants = Numbers(); eligible = new SortedDictionary<string, bool>(StringComparer.Ordinal); trips = new SortedSet<string>(StringComparer.Ordinal);
        }
        public static ResumableWork<PowerSystem> CreateWork(LayoutDef layout)
            => new ResumableWork<PowerSystem>(done => CreateRoutine(layout, done));
        private static IEnumerable<string> CreateRoutine(LayoutDef layout, Action<PowerSystem> done)
        {
            var next = new PowerSystem(true);
            using (var work = next.InitializeWork(layout)) { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork(LayoutDef layout) => new ResumableWork<bool>(done => InitializeRoutine(layout, done));
        private IEnumerable<string> InitializeRoutine(LayoutDef layout, Action<bool> done)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            yield return "power.create.begin";
            using (var work = IndexIntoWork(layout.power_nodes, x => x.id, nodes)) { while (work.MoveNext()) yield return work.Stage; }
            using (var work = IndexIntoWork(layout.power_links, x => x.id, links)) { while (work.MoveNext()) yield return work.Stage; }
            using (var work = IndexIntoWork(layout.sockets, x => x.id, sockets)) { while (work.MoveNext()) yield return work.Stage; }
            using (var work = IndexIntoWork(layout.infrastructure, x => x.id, infrastructure)) { while (work.MoveNext()) yield return work.Stage; }
            foreach (var node in nodes.Values)
            {
                Bound(node.fixed_supply, 0, int.MaxValue); Bound(node.reconnect_depth_mm, 0, node.disconnect_depth_mm);
                nodeEnabled.Add(node.id, node.initial_enabled);
                yield return "power.create.node";
            }
            foreach (var link in links.Values)
            {
                Node(link.from_node_id); Node(link.to_node_id);
                if (link.from_node_id == link.to_node_id) Fault("invalid power link");
                if (link.relay_socket_id != null)
                {
                    if (!sockets.ContainsKey(link.relay_socket_id)) Fault("invalid power link");
                    using (var check = ContainsWork(sockets[link.relay_socket_id].permitted_equipment_ids, "relay")) { while (check.MoveNext()) yield return check.Stage; if (!check.Result) Fault("invalid power link"); }
                }
                linkEnabled.Add(link.id, link.initial_enabled);
                yield return "power.create.link";
            }
            foreach (var item in infrastructure.Values)
            {
                if (item.power_node_id != null) Node(item.power_node_id);
                if (item.initial_demand > 0 && item.power_node_id == null) Fault("unbound infrastructure demand");
                Bound(item.initial_demand, 0, int.MaxValue); Bound(item.initial_priority, 0, int.MaxValue);
                demands.Add(item.id, item.initial_demand); priorities.Add(item.id, item.initial_priority); grants.Add(item.id, 0); eligible.Add(item.id, false);
                yield return "power.create.infrastructure";
            }
            done(true);
        }
        private PowerSystem(PowerSystem old)
        {
            nodes = old.nodes; links = old.links; sockets = old.sockets; infrastructure = old.infrastructure;
            nodeEnabled = new SortedDictionary<string, bool>(StringComparer.Ordinal); linkEnabled = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            demands = new SortedDictionary<string, long>(StringComparer.Ordinal); priorities = new SortedDictionary<string, long>(StringComparer.Ordinal); grants = new SortedDictionary<string, long>(StringComparer.Ordinal);
            eligible = new SortedDictionary<string, bool>(StringComparer.Ordinal); trips = new SortedSet<string>(StringComparer.Ordinal);
        }
        public PowerSystem Fork() => ForkWork().Drain();
        public ResumableWork<PowerSystem> ForkWork() => new ResumableWork<PowerSystem>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<PowerSystem> done)
        {
            var next = new PowerSystem(this);
            foreach (var pair in nodeEnabled) { next.nodeEnabled.Add(pair.Key, pair.Value); yield return "power.fork.nodeEnabled"; }
            foreach (var pair in linkEnabled) { next.linkEnabled.Add(pair.Key, pair.Value); yield return "power.fork.linkEnabled"; }
            foreach (var pair in demands) { next.demands.Add(pair.Key, pair.Value); yield return "power.fork.demands"; }
            foreach (var pair in priorities) { next.priorities.Add(pair.Key, pair.Value); yield return "power.fork.priorities"; }
            foreach (var pair in grants) { next.grants.Add(pair.Key, pair.Value); yield return "power.fork.grants"; }
            foreach (var pair in eligible) { next.eligible.Add(pair.Key, pair.Value); yield return "power.fork.eligible"; }
            foreach (string id in trips) { next.trips.Add(id); yield return "power.fork.trip"; }
            done(next);
        }
        // Stage-3 deployment registers the lifetime consumer immediately, so a
        // priority command later in the same command batch sees the exact list.
        public void RegisterEquipment(string id, string kind, string socketId)
        {
            CheckId(id); if (!sockets.ContainsKey(socketId) || !sockets[socketId].permitted_equipment_ids.Contains(kind)) Fault("Unbound consumer registration");
            long demand = Demand(kind); if (demand == 0) return;
            demands.Add(id, demand); priorities.Add(id, Priority(kind)); grants.Add(id, 0); eligible.Add(id, false);
        }
        public long Grant(string consumerId) { if (!grants.ContainsKey(consumerId)) Fault("unknown power consumer"); return grants[consumerId]; }
        public bool IsPowered(string consumerId)
        { return Grant(consumerId) == demands[consumerId] && eligible[consumerId]; }
        public void SetPriorities(IEnumerable<string> consumerIds)
        {
            var ids = (consumerIds ?? throw new ArgumentNullException(nameof(consumerIds))).ToArray();
            if (ids.Length != priorities.Count || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(x => !priorities.ContainsKey(x))) Fault("priority order must name every consumer exactly once");
            var next = Numbers(); for (int i = 0; i < ids.Length; i++) next.Add(ids[i], i); priorities = next;
        }
        public bool CanEnableGenerator(string instanceId, PowerFrame frame) => CanEnableGeneratorWork(instanceId, frame).Drain();
        public ResumableWork<bool> CanEnableGeneratorWork(string instanceId, PowerFrame frame)
            => new ResumableWork<bool>(done => CanEnableGeneratorRoutine(instanceId, frame, done));
        private IEnumerable<string> CanEnableGeneratorRoutine(string instanceId, PowerFrame frame, Action<bool> done)
        {
            using (var consumers = ConsumersWork(frame))
            { while (consumers.MoveNext()) yield return consumers.Stage; }
            PowerEquipment item;
            if (!frame.Equipment.TryGetValue(instanceId, out item) || item.EquipmentId != "generator") Fault("unknown generator");
            var sample = frame.Nodes[sockets[item.SocketId].power_node_id];
            done(item.Intact && sample.Safe && sample.DepthMm <= 40);
        }
        // Called only for an accepted explicit activation. The equipment owner
        // enables the instance after this succeeds; merely drying cannot restart.
        public void EnableGenerator(string instanceId, PowerFrame frame) => EnableGeneratorWork(instanceId, frame).Drain();
        public ResumableWork<bool> EnableGeneratorWork(string instanceId, PowerFrame frame)
            => new ResumableWork<bool>(done => EnableGeneratorRoutine(instanceId, frame, done));
        private IEnumerable<string> EnableGeneratorRoutine(string instanceId, PowerFrame frame, Action<bool> done)
        {
            using (var validation = CanEnableGeneratorWork(instanceId, frame))
            {
                while (validation.MoveNext()) yield return validation.Stage;
                if (!validation.Result) Fault("generator restart unsafe");
            }
            yield return "power.generator.ready_to_enable";
            trips.Remove(instanceId); done(true);
        }

        public PowerAllocationResult Allocate(PowerFrame frame) => AllocateWork(frame).Drain();
        public ResumableWork<PowerAllocationResult> AllocateWork(PowerFrame frame) => new ResumableWork<PowerAllocationResult>(done => AllocateRoutine(frame, done));
        private IEnumerable<string> AllocateRoutine(PowerFrame frame, Action<PowerAllocationResult> done)
        {
            SortedDictionary<string, Consumer> consumers; using (var workconsumers = ConsumersWork(frame)) { while (workconsumers.MoveNext()) yield return workconsumers.Stage; consumers = workconsumers.Result; } // Validate complete frame before any mutation.
            PowerSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } var shutdowns = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes.Values)
            {yield return "power.allocate"; 
                var sample = frame.Nodes[node.id];
                next.nodeEnabled[node.id] = Hysteresis(nodeEnabled[node.id], node.initial_enabled, sample.Safe, sample.DepthMm, node.disconnect_depth_mm, node.reconnect_depth_mm);
            }
            foreach (var link in links.Values)
            {yield return "power.allocate"; 
                var sample = frame.Links[link.id];
                next.linkEnabled[link.id] = Hysteresis(linkEnabled[link.id], link.initial_enabled, sample.Safe, sample.DepthMm, 50, 40);
            }
            foreach (var item in frame.Equipment.Values)
            {yield return "power.allocate"; 
                if (item.EquipmentId != "generator") continue;
                var sample = frame.Nodes[sockets[item.SocketId].power_node_id];
                if (item.Enabled && item.Intact && sample.DepthMm > 50 && next.trips.Add(item.Id)) shutdowns.Add(item.Id);
            }
            SortedDictionary<string, string> components; using (var workcomponents = next.ComponentsWork(frame)) { while (workcomponents.MoveNext()) yield return workcomponents.Stage; components = workcomponents.Result; } var supply = Numbers();
            var componentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in components.Values) { if (componentIds.Add(id)) supply.Add(id, 0); yield return "power.allocate.component_supply"; }
            foreach (var node in nodes.Values)
                { yield return "power.allocate"; if (components.ContainsKey(node.id)) supply[components[node.id]] = checked(supply[components[node.id]] + node.fixed_supply); }
            foreach (var item in frame.Equipment.Values)
            {yield return "power.allocate"; 
                if (item.EquipmentId != "generator" || !item.Enabled || !item.Intact || next.trips.Contains(item.Id)) continue;
                string node = sockets[item.SocketId].power_node_id;
                if (components.ContainsKey(node)) supply[components[node]] = checked(supply[components[node]] + 2);
            }
            next.demands = Numbers(); next.grants = Numbers(); next.eligible = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (var consumer in consumers.Values)
            {yield return "power.allocate"; 
                next.demands.Add(consumer.Id, consumer.Demand); next.grants.Add(consumer.Id, 0); next.eligible.Add(consumer.Id, false);
                if (!next.priorities.ContainsKey(consumer.Id)) next.priorities.Add(consumer.Id, consumer.DefaultPriority);
            }
            bool exact;
            using (var check = EqualSortedWork(next.priorities.Keys, consumers.Keys)) { while (check.MoveNext()) yield return check.Stage; exact = check.Result; }
            if (!exact) Fault("equipment consumer ledger lost an instance");
            Consumer[] orderedConsumers;
            using (var sort = ResumableSort.Create(consumers.Values, Comparer<Consumer>.Create((a, b) =>
            {
                int priority = next.priorities[a.Id].CompareTo(next.priorities[b.Id]);
                return priority != 0 ? priority : StringComparer.Ordinal.Compare(a.Id, b.Id);
            }))) { while (sort.MoveNext()) yield return sort.Stage; orderedConsumers = sort.Result; }
            foreach (var consumer in orderedConsumers)
            {yield return "power.allocate"; 
                bool connected = consumer.Node != null && components.ContainsKey(consumer.Node);
                bool available = consumer.Safe && consumer.Active && (consumer.Demand == 0 && consumer.Node == null || connected);
                next.eligible[consumer.Id] = available;
                if (!available || consumer.Demand == 0) continue;
                string component = components[consumer.Node];
                if (supply[component] < consumer.Demand) continue; // Full demand or zero, then continue.
                next.grants[consumer.Id] = consumer.Demand; supply[component] -= consumer.Demand;
            }
            PowerAllocationResult result;
            using (var copy = PowerAllocationResult.CreateWork(next.grants, grants, components, shutdowns, next.trips)) { while (copy.MoveNext()) yield return copy.Stage; result = copy.Result; }
            nodeEnabled = next.nodeEnabled; linkEnabled = next.linkEnabled; demands = next.demands; priorities = next.priorities;
            grants = next.grants; eligible = next.eligible; trips = next.trips;
            done(result);
                }
        private static bool Hysteresis(bool old, bool authoredEnabled, bool safe, long depth, long disconnect, long reconnect)
        { if (!authoredEnabled || !safe || depth > disconnect) return false; if (depth <= reconnect) return true; return old; }
        private SortedDictionary<string, string> Components(PowerFrame frame) => ComponentsWork(frame).Drain();
        private ResumableWork<SortedDictionary<string, string>> ComponentsWork(PowerFrame frame) => new ResumableWork<SortedDictionary<string, string>>(done => ComponentsRoutine(frame, done));
        private IEnumerable<string> ComponentsRoutine(PowerFrame frame, Action<SortedDictionary<string, string>> done)
        {
            var adjacency = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (string id in nodes.Keys) { adjacency.Add(id, new SortedSet<string>(StringComparer.Ordinal)); yield return "power.graph.node"; }
            foreach (var link in links.Values)
            {yield return "power.graph"; 
                if (!linkEnabled[link.id] || !nodeEnabled[link.from_node_id] || !nodeEnabled[link.to_node_id]) continue;
                if (link.relay_socket_id != null)
                {
                    bool enabled = false;
                    foreach (var item in frame.Equipment.Values)
                    { enabled = item.SocketId == link.relay_socket_id && item.EquipmentId == "relay" && item.Enabled && item.Intact; yield return "power.graph.relay"; if (enabled) break; }
                    if (!enabled) continue;
                }
                adjacency[link.from_node_id].Add(link.to_node_id); adjacency[link.to_node_id].Add(link.from_node_id);
            }
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string root in nodes.Keys)
            {yield return "power.graph"; 
                if (!nodeEnabled[root] || result.ContainsKey(root)) continue;
                var pending = new SortedSet<string>(StringComparer.Ordinal) { root };
                while (pending.Count > 0)
                {yield return "power.graph"; 
                    string id = pending.Min; pending.Remove(id); if (result.ContainsKey(id)) continue;
                    result.Add(id, root); foreach (string neighbor in adjacency[id]) { yield return "power.graph"; if (!result.ContainsKey(neighbor)) pending.Add(neighbor); }
                }
            }
            done(result);
                }
        private SortedDictionary<string, Consumer> Consumers(PowerFrame frame) => ConsumersWork(frame).Drain();
        private ResumableWork<SortedDictionary<string, Consumer>> ConsumersWork(PowerFrame frame) => new ResumableWork<SortedDictionary<string, Consumer>>(done => ConsumersRoutine(frame, done));
        private IEnumerable<string> ConsumersRoutine(PowerFrame frame, Action<SortedDictionary<string, Consumer>> done)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            using (var check = ExactSortedWork(frame.Nodes.Keys, nodes.Keys, "node safety frame")) { while (check.MoveNext()) yield return check.Stage; }
            using (var check = ExactSortedWork(frame.Links.Keys, links.Keys, "link safety frame")) { while (check.MoveNext()) yield return check.Stage; }
            var result = new SortedDictionary<string, Consumer>(StringComparer.Ordinal); var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in infrastructure.Values) { yield return "power.consumers"; result.Add(item.id, new Consumer { Id = item.id, Node = item.power_node_id, Demand = item.initial_demand, DefaultPriority = item.initial_priority, Active = true }); }
            foreach (var item in frame.Equipment.Values)
            {yield return "power.consumers"; 
                Socket socket;
                if (!sockets.TryGetValue(item.SocketId, out socket)) Fault("unbound or overlapping power equipment");
                using (var check = ContainsWork(socket.permitted_equipment_ids, item.EquipmentId)) { while (check.MoveNext()) yield return check.Stage; if (!check.Result) Fault("unbound or overlapping power equipment"); }
                if (item.Intact && !occupied.Add(item.SocketId)) Fault("unbound or overlapping power equipment");
                bool electrical = item.EquipmentId == "generator" || item.EquipmentId == "relay" || Demand(item.EquipmentId) > 0;
                if (electrical && socket.power_node_id == null) Fault("electrical equipment lacks power node");
                if (electrical) Node(socket.power_node_id);
                long demand = Demand(item.EquipmentId); if (demand == 0) continue;
                result.Add(item.Id, new Consumer { Id = item.Id, Node = socket.power_node_id, Demand = demand, DefaultPriority = Priority(item.EquipmentId), Active = item.Enabled && item.Intact });
            }
            using (var check = ExactSortedWork(frame.Consumers.Keys, result.Keys, "consumer safety frame")) { while (check.MoveNext()) yield return check.Stage; }
            foreach (var item in result.Values) { yield return "power.consumers"; item.Safe = frame.Consumers[item.Id].Safe; }
            foreach (string id in trips) { if (!frame.Equipment.ContainsKey(id) || frame.Equipment[id].EquipmentId != "generator") Fault("generator trip lost its lifetime instance"); yield return "power.consumers.trip"; }
            done(result);
                }
        private static long Demand(string equipment) => equipment == "pump" || equipment == "sprinkler" || equipment == "winch" ? 1 : 0;
        private static long Priority(string equipment) => equipment == "winch" ? 10 : equipment == "pump" ? 20 : 30;
        public JsonValue ExportPower() => ExportPowerWork().Drain();
        public ResumableWork<JsonValue> ExportPowerWork() => new ResumableWork<JsonValue>(ExportPowerRoutine);
        private IEnumerable<string> ExportPowerRoutine(Action<JsonValue> done)
        {
            JsonValue nodeEnabledRows; using (var worknodeEnabledRows = DomainWork.Rows(nodeEnabled, x => Object("id", x.Key, "value", x.Value), "power.read.nodeEnabled")) { while (worknodeEnabledRows.MoveNext()) yield return worknodeEnabledRows.Stage; nodeEnabledRows = worknodeEnabledRows.Result; }
            JsonValue linkEnabledRows; using (var worklinkEnabledRows = DomainWork.Rows(linkEnabled, x => Object("id", x.Key, "value", x.Value), "power.read.linkEnabled")) { while (worklinkEnabledRows.MoveNext()) yield return worklinkEnabledRows.Stage; linkEnabledRows = worklinkEnabledRows.Result; }
            JsonValue demandsRows; using (var workdemandsRows = DomainWork.Rows(demands, x => Object("id", x.Key, "value", x.Value), "power.read.demands")) { while (workdemandsRows.MoveNext()) yield return workdemandsRows.Stage; demandsRows = workdemandsRows.Result; }
            JsonValue prioritiesRows; using (var workprioritiesRows = DomainWork.Rows(priorities, x => Object("id", x.Key, "value", x.Value), "power.read.priorities")) { while (workprioritiesRows.MoveNext()) yield return workprioritiesRows.Stage; prioritiesRows = workprioritiesRows.Result; }
            JsonValue grantsRows; using (var workgrantsRows = DomainWork.Rows(grants, x => Object("id", x.Key, "value", x.Value), "power.read.grants")) { while (workgrantsRows.MoveNext()) yield return workgrantsRows.Stage; grantsRows = workgrantsRows.Result; }
            JsonValue tripRows; using (var worktripRows = DomainWork.Rows(trips, JsonValue.String, "power.read.trip")) { while (worktripRows.MoveNext()) yield return worktripRows.Stage; tripRows = worktripRows.Result; }
            done(Object("node_enabled", nodeEnabledRows, "link_enabled", linkEnabledRows, "consumer_demands", demandsRows,
                "priorities", prioritiesRows, "grants", grantsRows, "generator_trip_ids", tripRows));
        }
        // Returns an independent restored candidate. Safety remains an explicit
        // current frame and is rederived before gameplay can consume restored grants.
        public PowerSystem RestorePower(JsonValue power, PowerFrame frame) => RestorePowerWork(power, frame).Drain();
        public ResumableWork<PowerSystem> RestorePowerWork(JsonValue power, PowerFrame frame) => new ResumableWork<PowerSystem>(done => RestorePowerRoutine(power, frame, done));
        private IEnumerable<string> RestorePowerRoutine(JsonValue power, PowerFrame frame, Action<PowerSystem> done)
        {
            Fields(power, "node_enabled", "link_enabled", "consumer_demands", "priorities", "grants", "generator_trip_ids");
            PowerSystem next; using (var worknext = ForkWork()) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } SortedDictionary<string, bool> nodeEnabledValue; using (var worknodeEnabledValue = ReadBoolsWork(power["node_enabled"])) { while (worknodeEnabledValue.MoveNext()) yield return worknodeEnabledValue.Stage; nodeEnabledValue = worknodeEnabledValue.Result; } next.nodeEnabled = nodeEnabledValue; SortedDictionary<string, bool> linkEnabledValue; using (var worklinkEnabledValue = ReadBoolsWork(power["link_enabled"])) { while (worklinkEnabledValue.MoveNext()) yield return worklinkEnabledValue.Stage; linkEnabledValue = worklinkEnabledValue.Result; } next.linkEnabled = linkEnabledValue;
            SortedDictionary<string, long> demandsValue; using (var workdemandsValue = ReadCountsWork(power["consumer_demands"])) { while (workdemandsValue.MoveNext()) yield return workdemandsValue.Stage; demandsValue = workdemandsValue.Result; } next.demands = demandsValue; SortedDictionary<string, long> prioritiesValue; using (var workprioritiesValue = ReadCountsWork(power["priorities"])) { while (workprioritiesValue.MoveNext()) yield return workprioritiesValue.Stage; prioritiesValue = workprioritiesValue.Result; } next.priorities = prioritiesValue; SortedDictionary<string, long> grantsValue; using (var workgrantsValue = ReadCountsWork(power["grants"])) { while (workgrantsValue.MoveNext()) yield return workgrantsValue.Stage; grantsValue = workgrantsValue.Result; } next.grants = grantsValue;
            next.trips = new SortedSet<string>(StringComparer.Ordinal); Array(power["generator_trip_ids"]);
            foreach (var value in power["generator_trip_ids"].Items) { yield return "power.restore"; if (!next.trips.Add(Text(value))) Fault("duplicate generator trip"); }
            using (var check = ExactSortedWork(next.nodeEnabled.Keys, nodes.Keys, "restored nodes")) { while (check.MoveNext()) yield return check.Stage; }
            using (var check = ExactSortedWork(next.linkEnabled.Keys, links.Keys, "restored links")) { while (check.MoveNext()) yield return check.Stage; }
            SortedDictionary<string, Consumer> consumers; using (var workconsumers = next.ConsumersWork(frame)) { while (workconsumers.MoveNext()) yield return workconsumers.Stage; consumers = workconsumers.Result; }
            using (var check = ExactSortedWork(next.demands.Keys, consumers.Keys, "restored demands")) { while (check.MoveNext()) yield return check.Stage; }
            using (var check = ExactSortedWork(next.priorities.Keys, consumers.Keys, "restored priorities")) { while (check.MoveNext()) yield return check.Stage; }
            using (var check = ExactSortedWork(next.grants.Keys, consumers.Keys, "restored grants")) { while (check.MoveNext()) yield return check.Stage; }
            foreach (var item in consumers.Values)
                { yield return "power.restore"; if (next.demands[item.Id] != item.Demand || next.grants[item.Id] != 0 && next.grants[item.Id] != item.Demand) Fault("invalid restored full-or-zero power"); }
            foreach (var node in nodes.Values) { yield return "power.restore"; if (!node.initial_enabled && next.nodeEnabled[node.id]) Fault("restored disabled authored node"); }
            foreach (var link in links.Values) { yield return "power.restore"; if (!link.initial_enabled && next.linkEnabled[link.id]) Fault("restored disabled authored link"); }
            PowerSystem expected; using (var workexpected = next.ForkWork()) { while (workexpected.MoveNext()) yield return workexpected.Stage; expected = workexpected.Result; }using (var allocation = expected.AllocateWork(frame)) { while (allocation.MoveNext()) yield return allocation.Stage; }
            JsonValue expectedState; using (var workexpectedState = expected.ExportPowerWork()) { while (workexpectedState.MoveNext()) yield return workexpectedState.Stage; expectedState = workexpectedState.Result; }JsonValue restoredState; using (var workrestoredState = next.ExportPowerWork()) { while (workrestoredState.MoveNext()) yield return workrestoredState.Stage; restoredState = workrestoredState.Result; }bool equivalent; using (var workequivalent = expectedState.CanonicalEqualsWork(restoredState)) { while (workequivalent.MoveNext()) yield return workequivalent.Stage; equivalent = workequivalent.Result; } if (!equivalent) Fault("restored power boundary disagrees with current frame");
            next.eligible = expected.eligible; done(next);
                }
        private void Node(string id) { if (id == null || !nodes.ContainsKey(id)) Fault("unknown power node"); }
        private static SortedDictionary<string, long> Numbers() => new SortedDictionary<string, long>(StringComparer.Ordinal);
        internal static SortedDictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> id)
        {
            var result = new SortedDictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items) { if (item == null) Fault("null power record"); string key = id(item); CheckId(key); result.Add(key, item); } return result;
        }
        private static ResumableWork<bool> IndexIntoWork<T>(IEnumerable<T> items, Func<T, string> id, SortedDictionary<string, T> result)
            => new ResumableWork<bool>(done => IndexIntoRoutine(items, id, result, done));
        private static IEnumerable<string> IndexIntoRoutine<T>(IEnumerable<T> items, Func<T, string> id, SortedDictionary<string, T> result, Action<bool> done)
        {
            foreach (var item in items)
            { if (item == null) Fault("null power record"); string key = id(item); CheckId(key); result.Add(key, item); yield return "power.create.index"; }
            done(true);
        }
        private static ResumableWork<bool> ContainsWork(IEnumerable<string> rows, string value)
            => new ResumableWork<bool>(done => ContainsRoutine(rows, value, done));
        private static IEnumerable<string> ContainsRoutine(IEnumerable<string> rows, string value, Action<bool> done)
        {
            foreach (string row in rows) { bool match = row == value; yield return "power.binding.contains"; if (match) { done(true); yield break; } }
            done(false);
        }
        internal static void CheckId(string id) { if (id == null || !Regex.IsMatch(id, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) Fault("power ID bound"); }
        internal static void Bound(long number, long minimum, long maximum) { if (number < minimum || number > maximum) Fault("power integer bound"); }
        // Every caller supplies keys from our ordinal SortedDictionary indexes,
        // including the read-only PowerFrame wrappers. No new sort is needed.
        private static ResumableWork<bool> ExactSortedWork(IEnumerable<string> actual, IEnumerable<string> expected, string name)
            => new ResumableWork<bool>(done => ExactSortedRoutine(actual, expected, name, done));
        private static IEnumerable<string> ExactSortedRoutine(IEnumerable<string> actual, IEnumerable<string> expected, string name, Action<bool> done)
        {
            using (var check = EqualSortedWork(actual, expected))
            { while (check.MoveNext()) yield return check.Stage; if (!check.Result) Fault("incomplete or extra " + name); }
            done(true);
        }
        private static ResumableWork<bool> EqualSortedWork(IEnumerable<string> actual, IEnumerable<string> expected)
            => new ResumableWork<bool>(done => EqualSortedRoutine(actual, expected, done));
        private static IEnumerable<string> EqualSortedRoutine(IEnumerable<string> actual, IEnumerable<string> expected, Action<bool> done)
        {
            using (var a = actual.GetEnumerator()) using (var b = expected.GetEnumerator())
            {
                while (a.MoveNext())
                {
                    if (!b.MoveNext() || a.Current != b.Current) { done(false); yield break; }
                    yield return "power.validate.exact";
                }
                done(!b.MoveNext());
            }
        }
        private static JsonValue Bools(IDictionary<string, bool> values) => JsonValue.Array(values.Select(x => Object("id", x.Key, "value", x.Value)));
        private static JsonValue Counts(IDictionary<string, long> values) => JsonValue.Array(values.Select(x => Object("id", x.Key, "value", x.Value)));
        private static SortedDictionary<string, bool> ReadBools(JsonValue value) => ReadBoolsWork(value).Drain();
        private static ResumableWork<SortedDictionary<string, bool>> ReadBoolsWork(JsonValue value) => new ResumableWork<SortedDictionary<string, bool>>(done => ReadBoolsRoutine(value, done));
        private static IEnumerable<string> ReadBoolsRoutine(JsonValue value, Action<SortedDictionary<string, bool>> done)
        { Array(value); var result = new SortedDictionary<string, bool>(StringComparer.Ordinal); foreach (var item in value.Items) {yield return "power.restore.bool";  Fields(item, "id", "value"); if (item["value"].Kind != "boolean") Fault("power bool required"); result.Add(Text(item["id"]), item["value"].Boolean); } done(result);         }
        private static SortedDictionary<string, long> ReadCounts(JsonValue value) => ReadCountsWork(value).Drain();
        private static ResumableWork<SortedDictionary<string, long>> ReadCountsWork(JsonValue value) => new ResumableWork<SortedDictionary<string, long>>(done => ReadCountsRoutine(value, done));
        private static IEnumerable<string> ReadCountsRoutine(JsonValue value, Action<SortedDictionary<string, long>> done)
        { Array(value); var result = Numbers(); foreach (var item in value.Items) {yield return "power.restore.count";  Fields(item, "id", "value"); if (item["value"].Kind != "integer") Fault("power count required"); Bound(item["value"].Integer, 0, int.MaxValue); result.Add(Text(item["id"]), item["value"].Integer); } done(result);         }
        private static string Text(JsonValue value) { if (value.Kind != "string") Fault("power ID string required"); CheckId(value.Text); return value.Text; }
        private static void Array(JsonValue value) { if (value.Kind != "array") Fault("power array required"); }
        private static void Fields(JsonValue value, params string[] names)
        { if (value.Kind != "object" || value.Members.Count != names.Length || names.Any(x => !value.Has(x))) Fault("closed power fields required"); }
        internal static JsonValue Object(params object[] fields)
        {
            var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i += 2) { object value = fields[i + 1]; result.Add((string)fields[i], value is JsonValue ? (JsonValue)value : value is string ? JsonValue.String((string)value) : value is bool ? JsonValue.Bool((bool)value) : JsonValue.Number(Convert.ToInt64(value))); }
            return JsonValue.Object(result);
        }
        private static void Fault(string message) { throw new InvalidOperationException("SIMULATION_INVARIANT_FAULT: " + message); }
    }
}
