using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using Resq.Content;
using Resq.Core;

namespace Resq.Simulation
{
    public sealed class WaterRequest
    {
        public string Id { get; }
        public long RequestedMm3 { get; }
        public WaterRequest(string id, long requestedMm3) { WaterSystem.CheckId(id); WaterSystem.Bound(requestedMm3, 0, WaterSystem.MaximumTransfer, "requested water"); Id = id; RequestedMm3 = requestedMm3; }
    }

    // Equipment remains the sole owner of pump fractional_remainder. The result
    // supplies its next value; callers publish it with the equipment next state.
    public sealed class WaterPump
    {
        public string Id { get; }
        public string SocketId { get; }
        public bool Active { get; }
        public bool Powered { get; }
        public long FractionalRemainder { get; }
        public WaterPump(string id, string socketId, bool active, bool powered, long fractionalRemainder = 0)
        { WaterSystem.CheckId(id); WaterSystem.CheckId(socketId); WaterSystem.Bound(fractionalRemainder, 0, 49999, "pump remainder"); Id = id; SocketId = socketId; Active = active; Powered = powered; FractionalRemainder = fractionalRemainder; }
    }
    public sealed class WaterSprinkler
    {
        public string Id { get; }
        public string SocketId { get; }
        public bool Active { get; }
        public bool Powered { get; }
        public WaterSprinkler(string id, string socketId, bool active, bool powered)
        { WaterSystem.CheckId(id); WaterSystem.CheckId(socketId); Id = id; SocketId = socketId; Active = active; Powered = powered; }
    }
    public sealed class WaterGrant
    {
        public string Id { get; }
        public string Kind { get; }
        public string SourceId { get; }
        public string DestinationId { get; }
        public long RequestedMm3 { get; }
        public long GrantedMm3 { get; }
        internal WaterGrant(string id, string kind, string from, string to, long requested, long granted)
        { Id = id; Kind = kind; SourceId = from; DestinationId = to; RequestedMm3 = requested; GrantedMm3 = granted; }
    }
    public sealed class WaterPulseResult
    {
        public ReadOnlyCollection<WaterGrant> Grants { get; }
        public IReadOnlyDictionary<string, long> PumpRemainders { get; }
        public IReadOnlyDictionary<string, long> SprayGrants { get; }
        public JsonValue IntervalLedger { get; }
        internal WaterPulseResult(IEnumerable<WaterGrant> grants, IDictionary<string, long> pumpRemainders, JsonValue ledger)
        {
            Grants = grants.OrderBy(item => item.Id, StringComparer.Ordinal).ToList().AsReadOnly();
            PumpRemainders = new ReadOnlyDictionary<string, long>(new SortedDictionary<string, long>(pumpRemainders, StringComparer.Ordinal));
            SprayGrants = new ReadOnlyDictionary<string, long>(Grants.Where(item => item.Kind == "sprinkler").ToDictionary(item => item.Id.Substring("sprinkler:".Length), item => item.GrantedMm3, StringComparer.Ordinal));
            IntervalLedger = ledger;
        }
        // Compose into IOperationSystems at stage 5. Fire consumes SprayGrants,
        // metrics consume actual pump grants, and the owned C02 event sink owns IDs.
        public void EmitTransfers(OperationContext context) => EmitTransfersWork(context).Drain();
        public ResumableWork<bool> EmitTransfersWork(OperationContext context) => new ResumableWork<bool>(done => EmitTransfersRoutine(context, done));
        private IEnumerable<string> EmitTransfersRoutine(OperationContext context, Action<bool> done)
        {
            if (!context.HazardPulse || context.Events == null) throw new InvalidOperationException("Water events require a hazard interval and owned event sink");
            foreach (var grant in Grants.Where(item => item.GrantedMm3 > 0))
                { yield return "water.events"; context.Events.Emit("WaterTransfer", grant.Id, null, null, JsonValue.Object(new Dictionary<string, JsonValue> {
                    {"request_id",JsonValue.String(grant.Id)}, {"source_id",JsonValue.String(grant.SourceId)}, {"destination_id",JsonValue.String(grant.DestinationId)}, {"granted_mm3",JsonValue.Number(grant.GrantedMm3)} })); }
        
            done(true);
        }
    }

    // Pure rules-v1 subsystem, deliberately not an incomplete IOperationSystems.
    // No physics, rendering, frame clocks, random draws, or external writes.
    public sealed class WaterSystem
    {
        public const long MaximumVolume = 1000000000000L;
        public const long MaximumTransfer = 100000000L;
        private sealed class Rate
        {
            public string Id, Kind;
            public long Current, Remainder;
            public Rate Copy() => new Rate { Id = Id, Kind = Kind, Current = Current, Remainder = Remainder };
        }
        private sealed class Transfer
        {
            public string Id, Kind, From, To;
            public bool FromSea, ToSea;
            public long Requested, Granted;
        }
        private readonly SortedDictionary<string, WaterNode> nodes;
        private readonly SortedDictionary<string, HydraulicLink> links;
        private readonly SortedDictionary<string, WaterRateDef> sources, sinks;
        private readonly SortedDictionary<string, ResourceStoreDef> stores;
        private readonly SortedDictionary<string, Socket> sockets;
        private SortedDictionary<string, long> volumes;
        private SortedDictionary<string, Rate> rates;
        private long initial, direct, seaIn, drains, seaOut, sprayed;

        public WaterSystem(LayoutDef layout) : this(layout.water_nodes, layout.hydraulic_links, layout.sources, layout.sinks, layout.resources, layout.sockets) { }
        public WaterSystem(IEnumerable<WaterNode> waterNodes, IEnumerable<HydraulicLink> hydraulicLinks, IEnumerable<WaterRateDef> sourceDefinitions,
            IEnumerable<WaterRateDef> sinkDefinitions, IEnumerable<ResourceStoreDef> resourceStores, IEnumerable<Socket> equipmentSockets)
            : this()
        {
            InitializeWork(waterNodes, hydraulicLinks, sourceDefinitions, sinkDefinitions, resourceStores, equipmentSockets).Drain();
        }
        private WaterSystem()
        {
            nodes = new SortedDictionary<string, WaterNode>(StringComparer.Ordinal);
            links = new SortedDictionary<string, HydraulicLink>(StringComparer.Ordinal);
            sources = new SortedDictionary<string, WaterRateDef>(StringComparer.Ordinal);
            sinks = new SortedDictionary<string, WaterRateDef>(StringComparer.Ordinal);
            stores = new SortedDictionary<string, ResourceStoreDef>(StringComparer.Ordinal);
            sockets = new SortedDictionary<string, Socket>(StringComparer.Ordinal);
            volumes = new SortedDictionary<string, long>(StringComparer.Ordinal);
            rates = new SortedDictionary<string, Rate>(StringComparer.Ordinal);
        }
        public static ResumableWork<WaterSystem> CreateWork(LayoutDef layout)
            => new ResumableWork<WaterSystem>(done => CreateRoutine(layout.water_nodes, layout.hydraulic_links, layout.sources, layout.sinks, layout.resources, layout.sockets, done));
        public static ResumableWork<WaterSystem> CreateWork(IEnumerable<WaterNode> waterNodes, IEnumerable<HydraulicLink> hydraulicLinks, IEnumerable<WaterRateDef> sourceDefinitions,
            IEnumerable<WaterRateDef> sinkDefinitions, IEnumerable<ResourceStoreDef> resourceStores, IEnumerable<Socket> equipmentSockets)
            => new ResumableWork<WaterSystem>(done => CreateRoutine(waterNodes, hydraulicLinks, sourceDefinitions, sinkDefinitions, resourceStores, equipmentSockets, done));
        private static IEnumerable<string> CreateRoutine(IEnumerable<WaterNode> waterNodes, IEnumerable<HydraulicLink> hydraulicLinks, IEnumerable<WaterRateDef> sourceDefinitions,
            IEnumerable<WaterRateDef> sinkDefinitions, IEnumerable<ResourceStoreDef> resourceStores, IEnumerable<Socket> equipmentSockets, Action<WaterSystem> done)
        {
            var next = new WaterSystem();
            yield return "water.create.shell";
            using (var work = next.InitializeWork(waterNodes, hydraulicLinks, sourceDefinitions, sinkDefinitions, resourceStores, equipmentSockets))
            { while (work.MoveNext()) yield return work.Stage; }
            done(next);
        }
        private ResumableWork<bool> InitializeWork(IEnumerable<WaterNode> waterNodes, IEnumerable<HydraulicLink> hydraulicLinks, IEnumerable<WaterRateDef> sourceDefinitions,
            IEnumerable<WaterRateDef> sinkDefinitions, IEnumerable<ResourceStoreDef> resourceStores, IEnumerable<Socket> equipmentSockets)
            => new ResumableWork<bool>(done => InitializeRoutine(waterNodes, hydraulicLinks, sourceDefinitions, sinkDefinitions, resourceStores, equipmentSockets, done));
        private IEnumerable<string> InitializeRoutine(IEnumerable<WaterNode> waterNodes, IEnumerable<HydraulicLink> hydraulicLinks, IEnumerable<WaterRateDef> sourceDefinitions,
            IEnumerable<WaterRateDef> sinkDefinitions, IEnumerable<ResourceStoreDef> resourceStores, IEnumerable<Socket> equipmentSockets, Action<bool> done)
        {
            // Preserve family and source enumeration order: all six indexes are
            // complete before the count and value checks, just as in construction.
            foreach (string stage in PopulateIndex(waterNodes, item => item.id, nodes, "water.create.index.node")) yield return stage;
            foreach (string stage in PopulateIndex(hydraulicLinks, item => item.id, links, "water.create.index.link")) yield return stage;
            foreach (string stage in PopulateIndex(sourceDefinitions, item => item.id, sources, "water.create.index.source")) yield return stage;
            foreach (string stage in PopulateIndex(sinkDefinitions, item => item.id, sinks, "water.create.index.sink")) yield return stage;
            foreach (string stage in PopulateIndex(resourceStores, item => item.id, stores, "water.create.index.store")) yield return stage;
            foreach (string stage in PopulateIndex(equipmentSockets, item => item.id, sockets, "water.create.index.socket")) yield return stage;
            Bound(nodes.Count, 0, 4096, "water nodes"); Bound(links.Count, 0, 16384, "hydraulic links"); Bound(sockets.Count, 0, 512, "equipment sockets");
            yield return "water.create.counts";
            // Tree entries avoid a whole-table resize inside one yielded unit.
            var fan = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in nodes.Keys) { fan.Add(id, 0); yield return "water.create.fan"; }
            foreach (var node in nodes.Values)
            {
                Bound(node.area_mm2, 10000, 10000000, "water area"); Bound(node.elevation_mm, -10000, 100000, "water elevation"); Bound(node.initial_volume_mm3, 0, MaximumVolume, "initial volume");
                volumes.Add(node.id, node.initial_volume_mm3); initial = checked(initial + node.initial_volume_mm3);
                yield return "water.create.node";
            }
            foreach (var link in links.Values)
            {
                Bound(link.conductance_mm2_per_second, 0, 1000000, "conductance"); Bound(link.maximum_transfer_mm3, 0, MaximumTransfer, "link transfer");
                if (link.from_id == link.to_id || link.from_kind == "sea_boundary" && link.to_kind == "sea_boundary") Fault("invalid hydraulic endpoints");
                if (link.from_kind == "water_node") { Node(link.from_id); fan[link.from_id]++; }
                if (link.to_kind == "water_node") { Node(link.to_id); fan[link.to_id]++; }
                yield return "water.create.link";
            }
            foreach (var definition in sources.Values) { AddRate(definition, "source", fan); yield return "water.create.source"; }
            foreach (var definition in sinks.Values) { AddRate(definition, "sink", fan); yield return "water.create.sink"; }
            foreach (int count in fan.Values) { if (count > 32) Fault("hydraulic fan bound"); yield return "water.create.fan_bound"; }
            var aliases = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var store in stores.Values)
            {
                if (store.unit == "water_mm3")
                {
                    Node(store.water_node_id);
                    if (nodes[store.water_node_id].kind != "tank" || store.initial_amount != volumes[store.water_node_id] || store.initial_amount > store.capacity || !aliases.Add(store.water_node_id)) Fault("invalid or duplicated water store alias");
                }
                yield return "water.create.store";
            }
            using (var validation = ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; }
            done(true);
        }
        private WaterSystem(WaterSystem other)
        {
            // Typed content is immutable; mutable boundary values never alias.
            nodes = other.nodes; links = other.links; sources = other.sources; sinks = other.sinks; stores = other.stores; sockets = other.sockets;
            volumes = new SortedDictionary<string, long>(StringComparer.Ordinal);
            rates = new SortedDictionary<string, Rate>(StringComparer.Ordinal);
            initial = other.initial; direct = other.direct; seaIn = other.seaIn; drains = other.drains; seaOut = other.seaOut; sprayed = other.sprayed;
        }
        public WaterSystem Fork() => ForkWork().Drain();
        public ResumableWork<WaterSystem> ForkWork() => new ResumableWork<WaterSystem>(ForkRoutine);
        private IEnumerable<string> ForkRoutine(Action<WaterSystem> done)
        {
            var next = new WaterSystem(this);
            foreach (var pair in volumes) { next.volumes.Add(pair.Key, pair.Value); yield return "water.fork.volume"; }
            foreach (var pair in rates) { next.rates.Add(pair.Key, pair.Value.Copy()); yield return "water.fork.rate"; }
            done(next);
        }
        public long Volume(string nodeId) { Node(nodeId); return volumes[nodeId]; }
        public long Depth(string nodeId) { Node(nodeId); return IntegerMath.FloorDiv(volumes[nodeId], nodes[nodeId].area_mm2); }
        public long StoreAmount(string storeId)
        { if (!stores.ContainsKey(storeId) || stores[storeId].unit != "water_mm3") throw new ArgumentException("Unknown water store"); return Volume(stores[storeId].water_node_id); }
        public void SetSourceRate(string sourceId, long rate)
        { if (!sources.ContainsKey(sourceId)) throw new ArgumentException("Unknown water source"); Bound(rate, 0, 1000000000, "source rate"); rates[Key("source", sourceId)].Current = rate; }
        private void AddRate(WaterRateDef definition, string kind, IDictionary<string, int> fan)
        {
            Node(definition.water_node_id); Bound(definition.initial_rate_mm3_per_second, 0, 1000000000, "rate"); fan[definition.water_node_id]++;
            string id = Key(kind, definition.id); rates.Add(id, new Rate { Id = id, Kind = kind, Current = definition.initial_rate_mm3_per_second });
        }

        public static long RateRequest(long rate, long remainder, out long nextRemainder)
        {
            Bound(rate, 0, 1000000000, "rate"); Bound(remainder, 0, 9, "rate remainder"); long n = checked(rate + remainder);
            nextRemainder = n % 10; return IntegerMath.FloorDiv(n, 10);
        }
        public static long PumpRequest(long sourceHead, long destinationHead, long remainder, bool active, bool powered, out long nextRemainder)
        {
            Bound(remainder, 0, 49999, "pump remainder"); nextRemainder = remainder;
            if (!active || !powered) return 0;
            long lift = Math.Max(0, checked(destinationHead - sourceHead)); long factor = Math.Max(0, checked(5000 - lift));
            long n = checked(20000000L * factor + remainder); nextRemainder = n % 50000;
            return IntegerMath.FloorDiv(n, 50000);
        }
        public static IReadOnlyDictionary<string, long> Allocate(long available, IEnumerable<WaterRequest> requestSource)
        {
            Bound(available, 0, MaximumVolume, "available volume"); var requests = Index(requestSource, item => item.Id); Bound(requests.Count, 0, 32, "outward requests");
            long total = 0; foreach (var item in requests.Values) total = checked(total + item.RequestedMm3);
            var result = new SortedDictionary<string, long>(StringComparer.Ordinal);
            if (total <= available) { foreach (var item in requests.Values) result.Add(item.Id, item.RequestedMm3); }
            else
            {
                var residue = new List<KeyValuePair<string, long>>(); long used = 0;
                foreach (var item in requests.Values)
                {
                    // Constrained availability < total <= 32*100M, so this
                    // checked product is bounded below 3.2e17, unlike V*q in
                    // the unconstrained case (which must never be evaluated).
                    long product = checked(available * item.RequestedMm3), granted = product / total;
                    result.Add(item.Id, granted); used = checked(used + granted); if (item.RequestedMm3 > 0) residue.Add(new KeyValuePair<string, long>(item.Id, product % total));
                }
                long left = available - used;
                foreach (var item in residue.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)) { if (left == 0) break; result[item.Key]++; left--; }
                if (left != 0) Fault("largest remainder allocation failed");
            }
            return new ReadOnlyDictionary<string, long>(result);
        }

        // Called only for an explicit scenario hazard pulse. All control inputs
        // are current stage-2/3 parameters and stage-4 power grants. Barrier
        // intactness is captured before stage-6 damage, affecting the next pulse.
        public WaterPulseResult Pulse(IEnumerable<WaterPump> pumpSource, IEnumerable<WaterSprinkler> sprinklerSource,
            IReadOnlyDictionary<string, long> seaHeads, IReadOnlyDictionary<string, bool> gateOpen, IEnumerable<string> intactBarrierSockets) => PulseWork(pumpSource, sprinklerSource, seaHeads, gateOpen, intactBarrierSockets).Drain();
        public ResumableWork<WaterPulseResult> PulseWork(IEnumerable<WaterPump> pumpSource, IEnumerable<WaterSprinkler> sprinklerSource, IReadOnlyDictionary<string, long> seaHeads, IReadOnlyDictionary<string, bool> gateOpen, IEnumerable<string> intactBarrierSockets) => new ResumableWork<WaterPulseResult>(done => PulseRoutine(pumpSource, sprinklerSource, seaHeads, gateOpen, intactBarrierSockets, done));
        private IEnumerable<string> PulseRoutine(IEnumerable<WaterPump> pumpSource, IEnumerable<WaterSprinkler> sprinklerSource, IReadOnlyDictionary<string, long> seaHeads, IReadOnlyDictionary<string, bool> gateOpen, IEnumerable<string> intactBarrierSockets, Action<WaterPulseResult> done)
        {
            using (var initialValidation = ValidateBoundaryWork()) { while (initialValidation.MoveNext()) yield return initialValidation.Stage; } var pumps = Index(pumpSource, item => item.Id); var sprinklers = Index(sprinklerSource, item => item.Id);
            Bound(checked(pumps.Count + sprinklers.Count), 0, 512, "water equipment");
            if (pumps.Keys.Intersect(sprinklers.Keys, StringComparer.Ordinal).Any()) Fault("duplicated equipment identity");
            var barriers = new HashSet<string>(intactBarrierSockets, StringComparer.Ordinal);
            foreach (string id in barriers) { yield return "water.pulse"; if (!sockets.ContainsKey(id) || !sockets[id].permitted_equipment_ids.Contains("barrier")) Fault("unknown intact barrier socket"); }
            foreach (var pair in gateOpen) { yield return "water.pulse"; if (!links.Values.Any(item => item.gate_id == pair.Key)) Fault("unknown hydraulic gate"); }
            foreach (var pair in seaHeads) {yield return "water.pulse";  CheckId(pair.Key); Bound(pair.Value, -10000, 100000, "sea head"); }
            var nextRates = new SortedDictionary<string, Rate>(StringComparer.Ordinal); foreach (var pair in rates) { yield return "water.pulse"; nextRates.Add(pair.Key, pair.Value.Copy()); }
            var available = new SortedDictionary<string, long>(StringComparer.Ordinal); foreach (var pair in volumes) { yield return "water.pulse"; available.Add(pair.Key, pair.Value); } var transfers = new List<Transfer>();
            long added = 0, entered = 0, drained = 0, exited = 0, spray = 0;
            foreach (var definition in sources.Values)
            {yield return "water.pulse"; 
                var rate = nextRates[Key("source", definition.id)]; long remainder; long requested = RateRequest(rate.Current, rate.Remainder, out remainder); rate.Remainder = remainder;
                available[definition.water_node_id] = checked(available[definition.water_node_id] + requested); Bound(available[definition.water_node_id], 0, MaximumVolume, "source-added volume"); added = checked(added + requested);
                transfers.Add(new Transfer { Id = rate.Id, Kind = "source", From = rate.Id, To = definition.water_node_id, FromSea = true, Requested = requested, Granted = requested });
            }
            var heads = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var node in nodes.Values) { yield return "water.pulse"; heads.Add(node.id, checked(node.elevation_mm + IntegerMath.FloorDiv(available[node.id], node.area_mm2))); }
            Func<string, bool, long> head = (id, sea) => { if (!sea) { Node(id); return heads[id]; } if (!seaHeads.ContainsKey(id)) { Fault("missing prescribed sea head"); } return seaHeads[id]; };
            foreach (var link in links.Values)
            {yield return "water.pulse"; 
                bool fromSea = link.from_kind == "sea_boundary", toSea = link.to_kind == "sea_boundary";
                string from = link.from_id, to = link.to_id; long fromHead = head(from, fromSea), toHead = head(to, toSea);
                bool open = link.gate_id == null ? true : gateOpen.ContainsKey(link.gate_id) ? gateOpen[link.gate_id] : link.initial_gate_open;
                long requested = 0;
                if (open && (fromHead > toHead || link.direction == "both" && toHead > fromHead))
                {
                    if (toHead > fromHead) { string swap = from; from = to; to = swap; bool seaSwap = fromSea; fromSea = toSea; toSea = seaSwap; long headSwap = fromHead; fromHead = toHead; toHead = headSwap; }
                    long sill = checked(link.sill_mm + (link.barrier_socket_id != null && barriers.Contains(link.barrier_socket_id) ? 600 : 0));
                    long driving = Math.Max(0, checked(fromHead - Math.Max(toHead, sill)));
                    requested = Math.Min(link.maximum_transfer_mm3, IntegerMath.FloorDiv(checked(link.conductance_mm2_per_second * driving), 10));
                }
                transfers.Add(new Transfer { Id = Key("edge", link.id), Kind = "edge", From = from, To = to, FromSea = fromSea, ToSea = toSea, Requested = requested });
            }
            var pumpRemainders = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var pump in pumps.Values)
            {yield return "water.pulse"; 
                var socket = SocketFor(pump.SocketId, "pump"); Node(socket.intake_node_id); Node(socket.outlet_node_id);
                if (socket.intake_node_id == socket.outlet_node_id || socket.power_node_id == null) Fault("invalid pump socket binding");
                long remainder; long requested = PumpRequest(heads[socket.intake_node_id], heads[socket.outlet_node_id], pump.FractionalRemainder, pump.Active && socket.enabled, pump.Powered, out remainder);
                pumpRemainders.Add(pump.Id, remainder);
                transfers.Add(new Transfer { Id = Key("pump", pump.Id), Kind = "pump", From = socket.intake_node_id, To = socket.outlet_node_id, Requested = requested });
            }
            foreach (var definition in sinks.Values)
            {yield return "water.pulse"; 
                var rate = nextRates[Key("sink", definition.id)]; long remainder; long requested = RateRequest(rate.Current, rate.Remainder, out remainder); rate.Remainder = remainder;
                transfers.Add(new Transfer { Id = rate.Id, Kind = "sink", From = definition.water_node_id, To = rate.Id, ToSea = true, Requested = requested });
            }
            foreach (var sprinkler in sprinklers.Values)
            {yield return "water.pulse"; 
                var socket = SocketFor(sprinkler.SocketId, "sprinkler");
                if (socket.resource_store_id == null || !stores.ContainsKey(socket.resource_store_id) || stores[socket.resource_store_id].unit != "water_mm3" || socket.fire_cell_ids.Count < 1 || socket.fire_cell_ids.Count > 4) Fault("invalid sprinkler socket binding");
                string id = Key("sprinkler", sprinkler.Id); Rate rate;
                if (!nextRates.TryGetValue(id, out rate)) { rate = new Rate { Id = id, Kind = "sprinkler", Current = 5000000 }; nextRates.Add(id, rate); }
                long requested = 0;
                if (sprinkler.Active && sprinkler.Powered && socket.enabled) { long remainder; requested = RateRequest(rate.Current, rate.Remainder, out remainder); rate.Remainder = remainder; }
                transfers.Add(new Transfer { Id = id, Kind = "sprinkler", From = stores[socket.resource_store_id].water_node_id, To = id, ToSea = true, Requested = requested });
            }
            var transferIds = new HashSet<string>(StringComparer.Ordinal);
            var outgoingByNode = new Dictionary<string, List<Transfer>>(StringComparer.Ordinal); var incomingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in nodes.Keys) {yield return "water.pulse";  outgoingByNode.Add(id, new List<Transfer>()); incomingCounts.Add(id, 0); }
            foreach (var item in transfers) {yield return "water.pulse";  if (!transferIds.Add(item.Id)) Fault("duplicate transfer identity"); if (!item.FromSea) outgoingByNode[item.From].Add(item); if (!item.ToSea) incomingCounts[item.To]++; }
            foreach (string id in nodes.Keys)
            {yield return "water.pulse"; 
                var outgoing = outgoingByNode[id];
                if (outgoing.Count > 32 || incomingCounts[id] > 32) Fault("hydraulic request fan bound");
                var allocation = Allocate(available[id], outgoing.Select(item => new WaterRequest(item.Id, item.Requested)));
                foreach (var item in outgoing) { yield return "water.pulse"; item.Granted = allocation[item.Id]; }
            }
            // Unlimited incoming boundary requests are independently capped;
            // they do not participate in finite-node outward availability.
            foreach (var item in transfers.Where(item => item.FromSea && item.Kind != "source")) { yield return "water.pulse"; item.Granted = item.Requested; }
            var next = new SortedDictionary<string, long>(StringComparer.Ordinal); foreach (var pair in available) { yield return "water.pulse"; next.Add(pair.Key, pair.Value); }
            foreach (var item in transfers.Where(item => item.Kind != "source"))
            {yield return "water.pulse"; 
                if (!item.FromSea) next[item.From] = checked(next[item.From] - item.Granted);
                if (!item.ToSea) next[item.To] = checked(next[item.To] + item.Granted);
                if (item.Kind == "sink") drained = checked(drained + item.Granted);
                else if (item.Kind == "sprinkler") spray = checked(spray + item.Granted);
                else { if (item.FromSea) entered = checked(entered + item.Granted); if (item.ToSea) exited = checked(exited + item.Granted); }
            }
            WaterSystem candidate; using (var workcandidate = ForkWork()) { while (workcandidate.MoveNext()) yield return workcandidate.Stage; candidate = workcandidate.Result; } candidate.volumes = next; candidate.rates = nextRates;
            candidate.direct = checked(direct + added); candidate.seaIn = checked(seaIn + entered); candidate.drains = checked(drains + drained); candidate.seaOut = checked(seaOut + exited); candidate.sprayed = checked(sprayed + spray);
            using (var finalValidation = candidate.ValidateBoundaryWork()) { while (finalValidation.MoveNext()) yield return finalValidation.Stage; }
            long oldTotal = 0, newTotal = 0;
            foreach (long value in volumes.Values) { oldTotal = checked(oldTotal + value); yield return "water.validate.interval_old"; }
            foreach (long value in next.Values) { newTotal = checked(newTotal + value); yield return "water.validate.interval_new"; }
            if (newTotal != checked(oldTotal + added + entered - drained - exited - spray)) Fault("interval water conservation");
            var result = new WaterPulseResult(transfers.Select(item => new WaterGrant(item.Id, item.Kind, item.From, item.To, item.Requested, item.Granted)), pumpRemainders, Ledger(oldTotal, added, entered, drained, exited, spray));
            // All checks and result construction precede the only promotion.
            volumes = candidate.volumes; rates = candidate.rates; direct = candidate.direct; seaIn = candidate.seaIn; drains = candidate.drains; seaOut = candidate.seaOut; sprayed = candidate.sprayed;
            done(result);
                }

        public JsonValue ReadWater() => ReadWaterWork().Drain();
        public ResumableWork<JsonValue> ReadWaterWork() => DomainWork.Rows(volumes, pair => JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String(pair.Key)}, {"volume_mm3",JsonValue.Number(pair.Value)} }), "water.read.volume");
        public JsonValue ReadRateRemainders() => ReadRateRemaindersWork().Drain();
        public ResumableWork<JsonValue> ReadRateRemaindersWork() => DomainWork.Rows(rates.Values, rate => JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String(rate.Id)}, {"kind",JsonValue.String(rate.Kind)}, {"remainder",JsonValue.Number(rate.Remainder)}, {"current_rate_mm3_per_second",JsonValue.Number(rate.Current)} }), "water.read.rate");
        public JsonValue ReadLedger() => Ledger(initial, direct, seaIn, drains, seaOut, sprayed);
        public WaterSystem Restore(JsonValue water, JsonValue rateRemainders, JsonValue ledger) => RestoreWork(water, rateRemainders, ledger).Drain();
        public ResumableWork<WaterSystem> RestoreWork(JsonValue water, JsonValue rateRemainders, JsonValue ledger) => new ResumableWork<WaterSystem>(done => RestoreRoutine(water, rateRemainders, ledger, done));
        private IEnumerable<string> RestoreRoutine(JsonValue water, JsonValue rateRemainders, JsonValue ledger, Action<WaterSystem> done)
        {
            WaterSystem candidate; using (var workcandidate = ForkWork()) { while (workcandidate.MoveNext()) yield return workcandidate.Stage; candidate = workcandidate.Result; } candidate.volumes = new SortedDictionary<string, long>(StringComparer.Ordinal); candidate.rates = new SortedDictionary<string, Rate>(StringComparer.Ordinal);
            Array(water); Array(rateRemainders);
            foreach (var item in water.Items) {yield return "water.restore";  Fields(item, "id", "volume_mm3"); string id = Text(item["id"]); Node(id); candidate.volumes.Add(id, Number(item["volume_mm3"])); }
            foreach (var item in rateRemainders.Items)
            {yield return "water.restore"; 
                Fields(item, "id", "kind", "remainder", "current_rate_mm3_per_second"); string id = Text(item["id"]), kind = Text(item["kind"]);
                if (kind != "source" && kind != "sink" && kind != "sprinkler" || !id.StartsWith(kind + ":", StringComparison.Ordinal)) Fault("unknown water rate identity");
                if (kind != "sprinkler" && !rates.ContainsKey(id)) Fault("unbound water rate");
                candidate.rates.Add(id, new Rate { Id = id, Kind = kind, Remainder = Number(item["remainder"]), Current = Number(item["current_rate_mm3_per_second"]) });
            }
            using (var saved = candidate.volumes.Keys.GetEnumerator())
            using (var defined = nodes.Keys.GetEnumerator())
            {
                while (true)
                {
                    bool hasSaved = saved.MoveNext(), hasDefined = defined.MoveNext();
                    if (hasSaved != hasDefined || hasSaved && saved.Current != defined.Current) Fault("incomplete water boundary");
                    if (!hasSaved) break;
                    yield return "water.restore.volume_identity";
                }
            }
            foreach (var rate in rates.Values)
            {
                if (rate.Kind != "sprinkler" && !candidate.rates.ContainsKey(rate.Id)) Fault("incomplete water boundary");
                yield return "water.restore.rate_identity";
            }
            Fields(ledger, "initial_total_mm3", "direct_sources_mm3", "sea_in_mm3", "drains_mm3", "sea_out_mm3", "sprayed_mm3");
            candidate.initial = Number(ledger["initial_total_mm3"]); candidate.direct = Number(ledger["direct_sources_mm3"]); candidate.seaIn = Number(ledger["sea_in_mm3"]); candidate.drains = Number(ledger["drains_mm3"]); candidate.seaOut = Number(ledger["sea_out_mm3"]); candidate.sprayed = Number(ledger["sprayed_mm3"]);
            if (candidate.initial != initial) Fault("water initial ledger mismatch"); using (var validation = candidate.ValidateBoundaryWork()) { while (validation.MoveNext()) yield return validation.Stage; } done(candidate);
                }
        public void ValidateBoundary() => ValidateBoundaryWork().Drain();
        public ResumableWork<bool> ValidateBoundaryWork() => new ResumableWork<bool>(done => ValidateBoundaryRoutine(done));
        private IEnumerable<string> ValidateBoundaryRoutine(Action<bool> done)
        {
            long total = 0; foreach (var pair in volumes) {yield return "water.validate";  Bound(pair.Value, 0, MaximumVolume, "water volume"); total = checked(total + pair.Value); }
            foreach (var store in stores.Values) { yield return "water.validate"; if (store.unit == "water_mm3") Bound(volumes[store.water_node_id], 0, store.capacity, "finite water store capacity"); }
            foreach (var rate in rates.Values) {yield return "water.validate";  Bound(rate.Current, 0, 1000000000, "current rate"); Bound(rate.Remainder, 0, 9, "rate remainder"); if (rate.Kind == "sprinkler" && rate.Current != 5000000) Fault("sprinkler rate differs from rules-v1"); }
            foreach (long value in new[] { initial, direct, seaIn, drains, seaOut, sprayed }) { yield return "water.validate"; Bound(value, 0, long.MaxValue, "water ledger"); }
            if (total != checked(initial + direct + seaIn - drains - seaOut - sprayed)) Fault("cumulative water conservation");
        
            done(true);
        }
        private Socket SocketFor(string id, string equipment)
        { if (!sockets.ContainsKey(id) || !sockets[id].permitted_equipment_ids.Contains(equipment)) { Fault("unbound water equipment socket"); } return sockets[id]; }
        private void Node(string id) { if (id == null || !nodes.ContainsKey(id)) Fault("unknown finite water node"); }
        private static JsonValue Ledger(long initial, long direct, long seaIn, long drains, long seaOut, long sprayed) => JsonValue.Object(new Dictionary<string, JsonValue> { {"initial_total_mm3",JsonValue.Number(initial)}, {"direct_sources_mm3",JsonValue.Number(direct)}, {"sea_in_mm3",JsonValue.Number(seaIn)}, {"drains_mm3",JsonValue.Number(drains)}, {"sea_out_mm3",JsonValue.Number(seaOut)}, {"sprayed_mm3",JsonValue.Number(sprayed)} });
        private static SortedDictionary<string, T> Index<T>(IEnumerable<T> values, Func<T, string> id)
        { var result = new SortedDictionary<string, T>(StringComparer.Ordinal); foreach (var value in values) { string key = id(value); CheckId(key); result.Add(key, value); } return result; }
        private static IEnumerable<string> PopulateIndex<T>(IEnumerable<T> values, Func<T, string> id, SortedDictionary<string, T> result, string stage)
        { foreach (var value in values) { string key = id(value); CheckId(key); result.Add(key, value); yield return stage; } }
        private static string Key(string kind, string id) { string value = kind + ":" + id; CheckId(value); return value; }
        internal static void CheckId(string id) { if (id == null || !Regex.IsMatch(id, "^[A-Za-z0-9_.:@-]{1,128}\\z", RegexOptions.CultureInvariant)) Fault("water ID bound"); }
        internal static void Bound(long value, long minimum, long maximum, string name) { if (value < minimum || value > maximum) Fault(name + " out of bounds"); }
        private static void Fault(string message) { throw new InvalidOperationException("SIMULATION_INVARIANT_FAULT: " + message); }
        private static void Fields(JsonValue value, params string[] names) { if (value.Kind != "object" || value.Members.Count != names.Length || names.Any(name => !value.Has(name))) Fault("closed water state fields"); }
        private static void Array(JsonValue value) { if (value.Kind != "array") Fault("water state array required"); }
        private static long Number(JsonValue value) { if (value.Kind != "integer") Fault("water state integer required"); return value.Integer; }
        private static string Text(JsonValue value) { if (value.Kind != "string") Fault("water state ID required"); CheckId(value.Text); return value.Text; }
    }
}
