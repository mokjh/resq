using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Simulation;
using Resq.Simulation.Rescue;
using Resq.Simulation.Tools;
using UnityEngine;

namespace Resq.Tests.RuntimeEquipment
{
    [TestFixture, Category("runtime_equipment")]
    public sealed class RuntimeEquipmentTests
    {
        private static string Root => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        private static JsonValue J(string text) => StrictJson.Parse(text);
        private static JsonValue O(params object[] pairs)
        {
            var d = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) { var v = pairs[i + 1]; d.Add((string)pairs[i], v == null ? JsonValue.Null() : v is JsonValue j ? j : v is string s ? JsonValue.String(s) : v is bool b ? JsonValue.Bool(b) : JsonValue.Number(Convert.ToInt64(v))); }
            return JsonValue.Object(d);
        }
        private static JsonValue A(params string[] values) => JsonValue.Array(values.Select(JsonValue.String));
        private static JsonValue Count(string kind, long count) => O("equipment_id", kind, "count", count);
        private static readonly Dictionary<string, string> Parameters = new Dictionary<string, string> {
            {"barrier","{\"crest_mm\":600,\"warning_head_mm\":600,\"damage_head_mm\":800,\"damage_per_pulse\":1}"},
            {"bridge","{\"close_depth_mm\":100,\"damage_depth_mm\":500}"}, {"pump","{\"rate_mm3_per_second\":20000000,\"max_lift_mm\":5000}"},
            {"skiff","{\"boarding_operation_ticks\":10,\"unboarding_operation_ticks\":10}"}, {"beacon","{}"},
            {"generator","{\"shutdown_depth_mm\":50,\"restart_depth_mm\":40,\"damage_depth_mm\":200}"},
            {"firebreak","{\"maximum_cells\":8,\"uses\":1}"}, {"sprinkler","{\"rate_mm3_per_second\":5000000,\"maximum_cells\":4}"},
            {"crawler","{\"boarding_operation_ticks\":10,\"unboarding_operation_ticks\":10}"}, {"brace","{\"stability_bonus\":200,\"maximum_per_support\":3}"},
            {"winch","{\"crossing_operation_ticks\":50,\"duty_operation_ticks\":100,\"rest_operation_ticks\":30,\"brake_operation_ticks\":10}"},
            {"relay","{\"disconnect_depth_mm\":50,\"reconnect_depth_mm\":40}"},
            {"cooler","{\"initial_charges\":1,\"capacity_charges\":1,\"cooling_scenario_ticks\":50,\"crust_scenario_ticks\":200,\"reheat_scenario_ticks\":50}"},
            {"shelter","{\"initial_protection_scenario_ticks\":600}"} };
        // Explicit component fixture. It really changes bounded fire fuel,
        // structural bonus, lava state/timers and accumulated spray. This is not
        // world-mechanic acceptance or an IOperationSystems success engine.
        private sealed class MechanicFixture : IEquipmentMechanics
        {
            public bool FireAvailable { get; private set; }
            public bool StructureAvailable { get; private set; }
            public bool LavaAvailable { get; private set; }
            public Dictionary<string, long> Fuel = new Dictionary<string, long>(StringComparer.Ordinal);
            public Dictionary<string, long> Bonus = new Dictionary<string, long>(StringComparer.Ordinal);
            public Dictionary<string, long> Cooling = new Dictionary<string, long>(StringComparer.Ordinal);
            public long Spray;
            public bool Burning;
            public MechanicFixture(bool fire, bool structure, bool lava)
            { FireAvailable = fire; StructureAvailable = structure; LavaAvailable = lava; Fuel.Add("fire", 1000); }
            public IEquipmentMechanics Fork()
            { var x = new MechanicFixture(FireAvailable, StructureAvailable, LavaAvailable) { Spray = Spray, Burning = Burning }; x.Fuel = new Dictionary<string, long>(Fuel, StringComparer.Ordinal); x.Bonus = new Dictionary<string, long>(Bonus, StringComparer.Ordinal); x.Cooling = new Dictionary<string, long>(Cooling, StringComparer.Ordinal); return x; }
            public ValidationResult ValidateDeployment(EquipmentDef d, Socket s)
            {
                bool valid = d.id == "firebreak" || d.id == "sprinkler" ? FireAvailable && s.fire_cell_ids.All(Fuel.ContainsKey) : d.id == "crawler" ? FireAvailable && s.node_id != null : d.id == "brace" || d.id == "winch" ? StructureAvailable && (s.support_id != null || s.node_id != null) : LavaAvailable && (s.lava_segment_id != null || s.node_id != null);
                return new ValidationResult(valid ? "OK" : "UNKNOWN_TARGET");
            }
            public ValidationResult ValidateFirebreak(Socket s) => new ValidationResult(FireAvailable && !Burning && s.fire_cell_ids.All(Fuel.ContainsKey) ? "OK" : "UNSAFE_TARGET");
            public void ClearFirebreak(Socket s, long maximum)
            { if (!ValidateFirebreak(s).Allowed || s.fire_cell_ids.Count > maximum) throw new InvalidOperationException("Invalid clearing"); foreach (string id in s.fire_cell_ids) Fuel[id] = 0; }
            public void ApplySpray(Socket s, long granted)
            { if (!FireAvailable || !s.fire_cell_ids.All(Fuel.ContainsKey) || granted < 0) throw new InvalidOperationException("Invalid spray"); Spray += granted; }
            public void CommitBrace(string id, Socket s, long bonus)
            { if (!StructureAvailable || s.support_id != "support" || bonus != 200 || Bonus.ContainsKey(id)) throw new InvalidOperationException("Invalid brace"); Bonus.Add(id, bonus); }
            public ValidationResult ValidateCooling(Socket s, string segment) => new ValidationResult(LavaAvailable && s.lava_segment_id == segment && !Cooling.ContainsKey(segment) ? "OK" : "UNSAFE_TARGET");
            public void BeginCooling(string id, Socket s, string segment, long tick, CoolerParameters p)
            { if (!ValidateCooling(s, segment).Allowed) throw new InvalidOperationException("Invalid cooling"); Cooling.Add(segment, tick + p.cooling_scenario_ticks); }
            public bool CoolingActive(string id, string segment) => Cooling.ContainsKey(segment);
            public void Finish(string segment) { Cooling.Remove(segment); }
        }
        private sealed class Rig
        {
            public EquipmentSystem System;
            public LayoutDef Layout;
            public CompiledMission Mission;
            public EquipmentDef[] Definitions;
            public long Tick, Sequence;
            public RuntimeEventSink Sink = new RuntimeEventSink();
            public EquipmentFrame Frame(long depth = 0, long heat = 0, bool safe = true, bool idle = true, long occupants = 0, long reservations = 0, bool pending = false, bool order = false, bool hot = false, bool collapsed = false, bool warning = true, bool arrival = false)
                => new EquipmentFrame(Tick, Layout.sockets.Select(x => new EquipmentSocketSample(x.id, depth, heat, safe, idle, occupants, reservations, pending, order, hot, arrival, collapsed, warning, hot || heat >= 600 || depth > 200)), new Dictionary<string, long>());
            public PowerFrame PowerFrame(long generatorDepth = 0, long relayDepth = 0, bool consumerSafe = true)
            {
                var gear = System.Equipment.Select(x => new PowerEquipment(x.Id, x.EquipmentId, x.SocketId, x.Enabled, x.Intact));
                var ids = Layout.infrastructure.Select(x => x.id).Concat(System.Equipment.Where(x => x.EquipmentId == "pump" || x.EquipmentId == "sprinkler" || x.EquipmentId == "winch").Select(x => x.Id));
                return new PowerFrame(Tick, Layout.power_nodes.Select(x => new PowerNodeSample(x.id, x.id == "a" ? generatorDepth : 0, true)), Layout.power_links.Select(x => new PowerLinkSample(x.id, relayDepth, true)), gear, ids.Select(x => new PowerConsumerSample(x, consumerSafe)));
            }
            public GameCommand Command(string kind, JsonValue payload, long? sequence = null)
            {
                long seq = sequence ?? ++Sequence;
                return GameCommand.Read(O("schema_version", 1, "command_id", "cmd:" + seq, "run_id", "equipment-fixture", "sequence", seq, "requested_tick", Tick + 1, "actor_input", "reference_replay", "kind", kind, "payload", payload));
            }
            public void Apply(GameCommand command, EquipmentFrame frame = null)
            { if (frame == null) { Tick++; frame = Frame(); } Sink.BeginBoundary(Tick, Tick); System = System.Apply(command, frame, "active", new OperationContext(Tick, Tick, false, Sink), PowerFrame()); }
            public string Place(string kind, int socket = 0)
            { var command = Command("place_equipment", O("equipment_id", kind, "socket_id", kind + ":" + socket, "orientation_step", 0)); Apply(command); return "eq:" + command.Sequence; }
            public void Act(string id, string action, string target = null)
            { Apply(Command("activate_tool", action == "refill" ? O("equipment_instance_id", id, "action", action, "store_id", target) : action == "cool_segment" ? O("equipment_instance_id", id, "action", action, "segment_id", target) : O("equipment_instance_id", id, "action", action))); }
            public void ResolvePower(long depth = 0, long relayDepth = 0)
            { Tick++; Sink.BeginBoundary(Tick, Tick); System = System.ResolvePower(PowerFrame(depth, relayDepth), new OperationContext(Tick, Tick, false, Sink), out _); }
            public void Damage(long depth = 0, long heat = 0, long occupants = 0)
            { Tick++; Sink.BeginBoundary(Tick, Tick); System = System.HazardDamage(new OperationContext(Tick, Tick, true, Sink), Frame(depth, heat, occupants: occupants), out _); }
        }
        private static Rig Build(bool fire = true, bool structure = true, bool lava = true, string world = "W5", long stock = 2, long budget = 200)
        {
            var bundle = J(File.ReadAllText(Path.Combine(Root, "content/runtime/fixtures/compiler-valid.json")));
            var machine = J(File.ReadAllText(Path.Combine(Root, "specification/content/equipment_rules.json")))["equipment"].Items;
            var kinds = machine.Select(x => x["id"].Text).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var definitions = machine.Select(row => {
                string id = row["id"].Text; string first = new[] { "firebreak", "sprinkler", "crawler" }.Contains(id) ? "W2" : new[] { "brace", "winch" }.Contains(id) ? "W3" : id == "relay" ? "W4" : new[] { "cooler", "shelter" }.Contains(id) ? "W5" : "W1";
                return EquipmentDef.Read(O("id", id, "revision", 1, "supply_cost", row["supply_cost"], "first_world_id", first, "socket_kinds", JsonValue.Array(new[] { row["socket_kind"] }), "power_supply", id == "generator" ? 2 : 0,
                    "power_demand", new[] { "pump", "sprinkler", "winch" }.Contains(id) ? 1 : 0, "resident_seats", id == "skiff" ? 4 : id == "crawler" || id == "shelter" ? 6 : id == "winch" ? 2 : 0,
                    "movement_slots", id == "bridge" || id == "crawler" ? 2 : id == "skiff" || id == "winch" ? 1 : 0, "relocation_policy", new[] { "skiff", "crawler", "winch", "brace" }.Contains(id) ? "none" : "idle_same_zone", "durability", 100, "parameters", J(Parameters[id]), "prefab_id", row["prefab_id"])); }).ToArray();
            var mission = CompiledMission.Read(bundle["missions"].Items[0].With("world_id", JsonValue.String(world)).With("supply_budget", JsonValue.Number(budget)).With("default_stock", JsonValue.Array(kinds.Select(x => Count(x, stock)))).With("maximum_stock", JsonValue.Array(kinds.Select(x => Count(x, stock)))).With("permitted_equipment_ids", A(kinds)).With("events", J("[]")));
            var source = bundle["layouts"].Items[0]; var nodeTemplate = source["nav_nodes"].Items[0]; var edgeTemplate = source["nav_edges"].Items[0];
            var nav = source["nav_nodes"].Items.ToList();
            foreach (string id in new[] { "dock", "depot", "winch-node", "shelter-node" }) nav.Add(nodeTemplate.With("id", JsonValue.String(id)).With("kind", JsonValue.String(id == "shelter-node" ? "shelter" : "dock")).With("resident_capacity", JsonValue.Number(16)).With("vehicle_slots", JsonValue.Number(4)).With("dock_berths", JsonValue.Number(4)));
            var sockets = new List<JsonValue>(); var edges = source["nav_edges"].Items.ToList(); var hydraulic = new List<JsonValue>(); var powerLinks = new List<JsonValue>();
            foreach (var definition in definitions) for (int n = 0; n < 2; n++)
            {
                string id = definition.id, sid = id + ":" + n;
                string node = id == "skiff" ? "dock" : id == "crawler" ? "depot" : id == "winch" ? "winch-node" : id == "shelter" ? "shelter-node" : "assembly";
                sockets.Add(O("id", sid, "kind", definition.socket_kinds.Single(), "node_id", node, "permitted_equipment_ids", A(id), "orientation_steps", J("[0,1,2,3]"), "relocation_zone", "zone", "footprint_cell_ids", A("foot:" + sid),
                    "hydraulic_link_id", id == "barrier" ? "hydraulic:" + n : null, "intake_node_id", id == "pump" ? "water:a" : null, "outlet_node_id", id == "pump" ? "water:b" : null,
                    "power_node_id", id == "generator" ? "a" : new[] { "relay", "pump", "sprinkler", "winch" }.Contains(id) ? "b" : null,
                    "support_id", id == "brace" ? "support" : null, "lava_segment_id", id == "cooler" ? "lava:" + n : null,
                    "fire_cell_ids", id == "firebreak" || id == "sprinkler" ? A("fire") : A(), "resource_store_id", id == "sprinkler" ? "water-store" : id == "cooler" ? "charges" : null, "enabled", true));
                if (id == "bridge" || id == "winch") edges.Add(edgeTemplate.With("id", JsonValue.String("edge:" + sid)).With("from_node_id", JsonValue.String(id == "winch" ? "winch-node" : "start")).With("to_node_id", JsonValue.String("assembly")).With("traversal_class", JsonValue.String(id == "winch" ? "cable" : "ground")).With("required_equipment_socket_id", JsonValue.String(sid)));
                if (id == "barrier") hydraulic.Add(O("id", "hydraulic:" + n, "from_kind", "water_node", "from_id", "water:a", "to_kind", "water_node", "to_id", "water:b", "sill_mm", 0, "conductance_mm2_per_second", 0, "maximum_transfer_mm3", 100000000, "direction", "both", "gate_id", null, "initial_gate_open", true, "barrier_socket_id", sid));
                if (id == "relay") powerLinks.Add(O("id", "link:" + n, "from_node_id", "a", "to_node_id", "b", "initial_enabled", true, "relay_socket_id", sid));
            }
            Func<string, string, long, long, JsonValue> store = (id, unit, amount, capacity) => O("id", id, "unit", unit, "initial_amount", amount, "capacity", capacity, "water_node_id", unit == "water_mm3" ? "water:a" : null);
            var layoutJson = source.With("nav_nodes", JsonValue.Array(nav)).With("nav_edges", JsonValue.Array(edges)).With("sockets", JsonValue.Array(sockets)).With("hydraulic_links", JsonValue.Array(hydraulic)).With("sources", J("[]")).With("sinks", J("[]"))
                .With("water_nodes", JsonValue.Array(new[] { O("id", "water:a", "kind", "tank", "area_mm2", 10000, "elevation_mm", 0, "initial_volume_mm3", 1000000, "region_id", "wet"), O("id", "water:b", "kind", "tank", "area_mm2", 10000, "elevation_mm", 0, "initial_volume_mm3", 0, "region_id", "wet") }))
                .With("power_nodes", JsonValue.Array(new[] { "a", "b", "island" }.Select(id => O("id", id, "platform_region_id", "dry", "initial_enabled", true, "fixed_supply", 0, "disconnect_depth_mm", 50, "reconnect_depth_mm", 40))))
                .With("power_links", JsonValue.Array(powerLinks)).With("infrastructure", JsonValue.Array(new[] { "clinic", "plant", "island-clinic" }.Select(id => O("id", id, "kind", id == "plant" ? "cooling_works" : "clinic", "power_node_id", id == "island-clinic" ? "island" : "b", "initial_demand", 1, "initial_priority", id == "plant" ? 40 : 0, "required_nav_node_ids", A(), "required_support_ids", A(), "required_equipment_socket_ids", A(), "hazard_region_ids", A(), "maximum_depth_mm", 50, "maximum_heat_points", 600))))
                .With("fire_cells", JsonValue.Array(new[] { O("id", "fire", "region_id", "fire-region", "initial_fuel", 1000, "initial_heat", 0, "initial_wetness", 0, "extra_cooling_points_per_pulse", 0, "building_cell", false) }))
                .With("supports", JsonValue.Array(new[] { O("id", "support", "kind", "structure", "node_ids", A("assembly"), "edge_ids", A(), "region_id", "dry", "initial_base_stability", 500, "static_load_points", 0, "loss_points_per_second", 0, "brace_socket_ids", A("brace:0", "brace:1"), "collapse_edge_ids", A(), "station_ids", A()) }))
                .With("lava_segments", JsonValue.Array(new[] { 0, 1 }.Select(n => O("id", "lava:" + n, "region_id", "lava-region", "successor_ids", A(), "arrival_delay_scenario_ticks", 100, "crossing_edge_ids", A(), "cooler_socket_ids", A("cooler:" + n), "source_enabled", true))))
                .With("resources", JsonValue.Array(new[] { store("water-store", "water_mm3", 1000000, 10000000), store("charges", "coolant_charge", 2, 8), store("feed", "coolant_feedstock", 6, 6) }))
                .With("producers", JsonValue.Array(new[] { O("id", "producer", "infrastructure_id", "plant", "input_store_id", "feed", "output_store_id", "charges", "input_per_charge", 1, "production_scenario_ticks", 100, "power_demand", 1) }));
            var layout = LayoutDef.Read(layoutJson);
            return new Rig { Layout = layout, Mission = mission, Definitions = definitions, System = new EquipmentSystem(mission, layout, definitions, new WaterSystem(layout), new TransportSystem(mission, layout), new MechanicFixture(fire, structure, lava)) };
        }
        private static JsonValue Item(Rig r, string id) => r.System.ReadState()["equipment"].Items.Single(x => x["id"].Text == id);

        [Test, NUnit.Framework.Property("case_id", "check-c05/assertion/assertion_id=check-c05-a01")]
        public void AllFourteenDispatchToActualOwnedServicesAndDefaults()
        {
            var r = Build(); var ids = new Dictionary<string, string>();
            foreach (var definition in r.Definitions.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                long before = r.System.SuppliesRemaining; string id = r.Place(definition.id); ids.Add(definition.id, id);
                Assert.AreEqual(before - definition.supply_cost, r.System.SuppliesRemaining); Assert.AreEqual(definition.id, Item(r, id)["equipment_id"].Text);
                Assert.AreEqual(1, r.System.ReadState()["deployments"].Items.Single(x => x["equipment_id"].Text == definition.id)["count"].Integer);
            }
            Assert.IsTrue(r.System.BridgeAdmissionOpen("bridge:0", r.Frame(depth: 100)));
            Assert.IsFalse(r.System.BridgeAdmissionOpen("bridge:0", r.Frame(depth: 101)));
            Assert.IsTrue(r.System.EquipmentAtSocket("beacon:0", "beacon"));
            var heatRig = Build(); string hotBarrier = heatRig.Place("barrier"), hotBridge = heatRig.Place("bridge");
            heatRig.Damage(heat: 600); Assert.AreEqual(99, Item(heatRig, hotBarrier)["durability"].Integer, "Low head barrier still takes fire damage"); Assert.AreEqual(99, Item(heatRig, hotBridge)["durability"].Integer, "Shallow bridge still takes fire damage");
            heatRig.Damage(depth: 501, heat: 600); Assert.AreEqual(98, Item(heatRig, hotBridge)["durability"].Integer, "Water and fire causes never stack in one pulse");
            Assert.AreEqual(14, r.System.Equipment.Count); Assert.AreEqual(3, r.System.Transport.ReadState()["vehicles"].Items.Count);
            Assert.AreEqual(ids["skiff"], r.System.Transport.ReadState()["vehicles"].Items.Single(x => x["kind"].Text == "skiff")["equipment_instance_id"].Text);
            Assert.AreEqual(4, r.Definitions.Single(x => x.id == "skiff").resident_seats);
            var mechanic = (MechanicFixture)r.System.Mechanics; Assert.AreEqual(200, mechanic.Bonus.Values.Single());
            r.Act(ids["firebreak"], "firebreak"); Assert.AreEqual(0, ((MechanicFixture)r.System.Mechanics).Fuel["fire"]); Assert.IsTrue(Item(r, ids["firebreak"])["parameters"]["activated"].Boolean);
            Assert.AreEqual("RESOURCE_EMPTY", r.System.Preview(r.Command("activate_tool", O("equipment_instance_id", ids["firebreak"], "action", "firebreak")), r.Frame(), "active").ReasonCode);
            r.Act(ids["cooler"], "cool_segment", "lava:0"); Assert.AreEqual(0, Item(r, ids["cooler"])["parameters"]["charges"].Integer); Assert.IsTrue(((MechanicFixture)r.System.Mechanics).Cooling.ContainsKey("lava:0"));
            r.ResolvePower(); Assert.AreEqual(0, r.System.Power.Grant("island-clinic"), "Decorative proximity never connects an island");
            // Actual C03 finite tank/grants feed actual fixture spray state.
            r.System = r.System.Apply(r.Command("set_power_priority", O("consumer_ids", JsonValue.Array(new[] { ids["sprinkler"] }.Concat(r.System.ReadState()["power"]["consumer_demands"].Items.Select(x => x["id"].Text).Where(x => x != ids["sprinkler"])).Select(JsonValue.String)))), r.Frame(), "active", new OperationContext(r.Tick, r.Tick, false, r.Sink));
            r.ResolvePower(); r.Tick++; r.Sink.BeginBoundary(r.Tick, r.Tick);
            long initial = r.System.Water.Volume("water:a"); r.System = r.System.SimulateWater(new OperationContext(r.Tick, r.Tick, true, r.Sink), new Dictionary<string, long>(), new Dictionary<string, bool>(), out var pulse);
            Assert.Greater(pulse.SprayGrants[ids["sprinkler"]], 0); Assert.AreEqual(pulse.SprayGrants[ids["sprinkler"]], ((MechanicFixture)r.System.Mechanics).Spray);
            Assert.AreEqual(initial - pulse.SprayGrants[ids["sprinkler"]], r.System.Water.Volume("water:a") + r.System.Water.Volume("water:b"));
            Assert.AreEqual(pulse.PumpRemainders[ids["pump"]], Item(r, ids["pump"])["parameters"]["fractional_remainder"].Integer);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c05/assertion/assertion_id=check-c05-a02")]
        public void InvalidPreviewDuplicateStockAtomicMovesAndFiniteCoolantNeverRefund()
        {
            var r = Build(stock: 1); string before = r.System.ReadState().Canonical();
            var wrong = r.Command("place_equipment", O("equipment_id", "generator", "socket_id", "beacon:0", "orientation_step", 0), 1);
            Assert.AreEqual("SOCKET_INCOMPATIBLE", r.System.Preview(wrong, r.Frame(), "active").ReasonCode); Assert.AreEqual(before, r.System.ReadState().Canonical());
            var first = r.Command("place_equipment", O("equipment_id", "beacon", "socket_id", "beacon:0", "orientation_step", 0));
            Assert.IsTrue(r.System.Preview(first, r.Frame(), "planning").Allowed); Assert.IsTrue(r.System.Preview(first, r.Frame(), "planning").Allowed); Assert.AreEqual(before, r.System.ReadState().Canonical());
            // Real accepted C02 ledger prevents repeat command execution.
            var ledger = new CommandLedger(); r.Apply(first);
            var ledgerReceipt = new CommandResult(first, "accepted", "OK", r.Tick, 1, new[] { "eq:" + first.Sequence }); ledger.Admit(first, ledgerReceipt);
            Assert.AreSame(ledgerReceipt, ledger.FindDuplicate(first, r.Tick, 1)); long supplies = r.System.SuppliesRemaining;
            Assert.Throws<CommandException>(() => r.Apply(first)); Assert.AreEqual(supplies, r.System.SuppliesRemaining);
            Assert.AreEqual("STOCK_EXHAUSTED", r.System.Preview(r.Command("place_equipment", O("equipment_id", "beacon", "socket_id", "beacon:1", "orientation_step", 0)), r.Frame(), "active").ReasonCode);
            string beacon = "eq:" + first.Sequence; r.Damage(heat: 600); long durability = Item(r, beacon)["durability"].Integer;
            var move = r.Command("move_idle_equipment", O("equipment_instance_id", beacon, "destination_socket_id", "beacon:1", "orientation_step", 2));
            string snapshot = r.System.ReadState().Canonical(); Assert.AreEqual("OCCUPIED", r.System.Preview(move, r.Frame(occupants: 1), "active").ReasonCode); Assert.AreEqual(snapshot, r.System.ReadState().Canonical());
            r.Apply(move); Assert.AreEqual("beacon:1", Item(r, beacon)["socket_id"].Text); Assert.AreEqual(durability, Item(r, beacon)["durability"].Integer); Assert.AreEqual(supplies, r.System.SuppliesRemaining);
            string skiff = r.Place("skiff"); Assert.AreEqual("NOT_IDLE", r.System.Preview(r.Command("move_idle_equipment", O("equipment_instance_id", skiff, "destination_socket_id", "skiff:1", "orientation_step", 0)), r.Frame(), "active").ReasonCode);
            string cooler = r.Place("cooler"); r.Act(cooler, "cool_segment", "lava:0"); ((MechanicFixture)r.System.Mechanics).Finish("lava:0"); r.Damage();
            r.Apply(r.Command("move_idle_equipment", O("equipment_instance_id", cooler, "destination_socket_id", "cooler:1", "orientation_step", 1)));
            Assert.AreEqual(0, Item(r, cooler)["parameters"]["charges"].Integer); long stored = r.System.Coolant.Amount("charges"); r.Act(cooler, "refill", "charges"); Assert.AreEqual(stored - 1, r.System.Coolant.Amount("charges"));
            Assert.AreEqual("CAPACITY_FULL", r.System.Preview(r.Command("activate_tool", O("equipment_instance_id", cooler, "action", "refill", "store_id", "charges")), r.Frame(), "active").ReasonCode);
            var coolant = new FiniteCoolantSystem(r.Layout); var ready = new Dictionary<string, bool> { { "producer", true } };
            var sink = new RuntimeEventSink();
            for (int tick = 1; tick <= 50; tick++) coolant.Pulse(new OperationContext(tick, tick, true, sink), ready);
            Assert.AreEqual(5, coolant.Amount("feed")); Assert.AreEqual(1, coolant.ReadProducers().Items[0]["work_in_progress_units"].Integer); Assert.AreEqual(2, coolant.Amount("charges"));
            Assert.Throws<InvalidOperationException>(() => coolant.Pulse(new OperationContext(50, 50, true, sink), ready));
            var saved = coolant.Restore(coolant.ReadStores(), coolant.ReadProducers(), 50); ready["producer"] = false;
            for (int tick = 51; tick <= 100; tick++) saved.Pulse(new OperationContext(tick, tick, true, sink), ready);
            Assert.AreEqual(50, saved.ReadProducers().Items[0]["completed_work_scenario_ticks"].Integer); ready["producer"] = true;
            for (int tick = 101; tick <= 650; tick++) saved.Pulse(new OperationContext(tick, tick, true, sink), ready);
            Assert.AreEqual(0, saved.Amount("feed")); Assert.AreEqual(8, saved.Amount("charges")); Assert.AreEqual(0, saved.ReadProducers().Items[0]["work_in_progress_units"].Integer);
            for (int tick = 651; tick <= 750; tick++) saved.Pulse(new OperationContext(tick, tick, true, sink), ready);
            Assert.AreEqual(8, saved.Amount("charges")); Assert.AreEqual(0, saved.Amount("feed"));
            // Full equipment save projections retain identity/stock/costs; actual
            // hazard owner and water/transport were already forked/restored.
            r.ResolvePower(); var state = r.System.ReadState(); var restored = r.System.RestoreOwnedState(state, r.Tick, owner => FrameFor(r.Layout, owner, r.Tick)); Assert.AreEqual(state.Canonical(), restored.ReadState().Canonical());
            Assert.Throws<InvalidOperationException>(() => r.System.RestoreOwnedState(state.With("supplies_remaining", JsonValue.Number(200)), r.Tick, owner => FrameFor(r.Layout, owner, r.Tick)));
            // Both halves of the actual deployed-vehicle identity must survive
            // loading; well-shaped equipment rows cannot rename the transport
            // authority's binding or refund its lifetime stock.
            var vehicleRig = Build(); string vehicleId = vehicleRig.Place("skiff"); vehicleRig.ResolvePower(); var vehicleState = vehicleRig.System.ReadState();
            var renamed = vehicleState.With("equipment", JsonValue.Array(vehicleState["equipment"].Items.Select(x => x.With("id", JsonValue.String("eq:9999")))));
            Assert.Throws<InvalidOperationException>(() => vehicleRig.System.RestoreOwnedState(renamed, vehicleRig.Tick, owner => FrameFor(vehicleRig.Layout, owner, vehicleRig.Tick)));
            var missing = vehicleState.With("equipment", J("[]")).With("deployments", JsonValue.Array(vehicleState["deployments"].Items.Select(x => x.With("count", JsonValue.Number(0))))).With("supplies_remaining", JsonValue.Number(vehicleRig.Mission.supply_budget));
            Assert.Throws<InvalidOperationException>(() => vehicleRig.System.RestoreOwnedState(missing, vehicleRig.Tick, owner => FrameFor(vehicleRig.Layout, owner, vehicleRig.Tick)));
            var wrongKind = vehicleState.With("equipment", JsonValue.Array(vehicleState["equipment"].Items.Select(x => x.With("equipment_id", JsonValue.String("crawler")).With("socket_id", JsonValue.String("crawler:0"))))).With("deployments", JsonValue.Array(vehicleState["deployments"].Items.Select(x => x.With("count", JsonValue.Number(x["equipment_id"].Text == "crawler" ? 1 : 0)))));
            Assert.Throws<InvalidOperationException>(() => vehicleRig.System.RestoreOwnedState(wrongKind, vehicleRig.Tick, owner => FrameFor(vehicleRig.Layout, owner, vehicleRig.Tick)));
            var falselyBroken = vehicleState.With("equipment", JsonValue.Array(vehicleState["equipment"].Items.Select(x => x.With("lifecycle", JsonValue.String("broken")).With("durability", JsonValue.Number(0)).With("enabled", JsonValue.Bool(false)))));
            Assert.Throws<InvalidOperationException>(() => vehicleRig.System.RestoreOwnedState(falselyBroken, vehicleRig.Tick, owner => FrameFor(vehicleRig.Layout, owner, vehicleRig.Tick)));
            var duplicate = vehicleRig.System.Fork(); Assert.Throws<InvalidOperationException>(() => duplicate.Transport.AddDeployedVehicle(VehicleSpawn.Read(O("vehicle_id", "extra-skiff", "kind", "skiff", "node_id", "dock", "seats", 4, "movement_slots", 1, "initial_route_id", null, "initial_occupant_ids", A())), vehicleId));
            duplicate.ValidateBoundary(); // Rejected duplicate admission preserves the valid original owner.
            Assert.Throws<CommandException>(() => GameCommand.Parse("{\"schema_version\":1,\"command_id\":\"bad\",\"run_id\":\"equipment-fixture\",\"sequence\":1,\"requested_tick\":1,\"actor_input\":\"hands_left\",\"kind\":\"grab_resident\",\"payload\":{}}"));
        }
        private static PowerFrame FrameFor(LayoutDef layout, EquipmentSystem system, long tick) => new PowerFrame(tick, layout.power_nodes.Select(x => new PowerNodeSample(x.id, 0, true)), layout.power_links.Select(x => new PowerLinkSample(x.id, 0, true)), system.Equipment.Select(x => new PowerEquipment(x.Id, x.EquipmentId, x.SocketId, x.Enabled, x.Intact)), layout.infrastructure.Select(x => x.id).Concat(system.Equipment.Where(x => x.EquipmentId == "pump" || x.EquipmentId == "sprinkler" || x.EquipmentId == "winch").Select(x => x.Id)).Select(x => new PowerConsumerSample(x, true)));

        [Test, NUnit.Framework.Property("case_id", "check-c05/assertion/assertion_id=check-c05-a03")]
        public void FullOrZeroStablePriorityRelayHysteresisAndExplicitRestart()
        {
            var r = Build(); string generator = r.Place("generator"), relay = r.Place("relay"), pump = r.Place("pump"), sprinkler = r.Place("sprinkler"), winch = r.Place("winch");
            // Same command-stage priority list already contains all new equipment.
            r.Apply(r.Command("set_power_priority", O("consumer_ids", A(pump, sprinkler, "clinic", winch, "plant", "island-clinic")))); r.ResolvePower();
            Assert.AreEqual(1, r.System.Power.Grant(pump)); Assert.AreEqual(1, r.System.Power.Grant(sprinkler)); Assert.AreEqual(0, r.System.Power.Grant("clinic")); Assert.AreEqual(0, r.System.Power.Grant("island-clinic"));
            r.ResolvePower(depth: 50); Assert.IsFalse(Item(r, generator)["parameters"]["trip_latched"].Boolean);
            r.ResolvePower(depth: 51); Assert.IsTrue(Item(r, generator)["parameters"]["trip_latched"].Boolean); Assert.IsFalse(Item(r, generator)["enabled"].Boolean); Assert.AreEqual(0, r.System.Power.Grant(pump));
            int tripCount = r.System.Power.GeneratorTripIds.Count; r.ResolvePower(depth: 51); Assert.AreEqual(tripCount, r.System.Power.GeneratorTripIds.Count);
            r.ResolvePower(depth: 40); Assert.AreEqual(0, r.System.Power.Grant(pump), "Drying never automatically restarts generator");
            var enable = r.Command("activate_tool", O("equipment_instance_id", generator, "action", "enable"));
            Assert.AreEqual("UNSAFE_TARGET", r.System.Preview(enable, r.Frame(), "active", r.PowerFrame(41)).ReasonCode);
            r.Tick++; r.Sink.BeginBoundary(r.Tick, r.Tick); r.System = r.System.Apply(enable, r.Frame(), "active", new OperationContext(r.Tick, r.Tick, false, r.Sink), r.PowerFrame(40)); r.ResolvePower(); Assert.AreEqual(1, r.System.Power.Grant(pump));
            r.ResolvePower(relayDepth: 51); Assert.AreEqual(0, r.System.Power.Grant(pump)); r.ResolvePower(relayDepth: 45); Assert.AreEqual(0, r.System.Power.Grant(pump)); r.ResolvePower(relayDepth: 40); Assert.AreEqual(1, r.System.Power.Grant(pump));
            var missing = new PowerFrame(r.Tick, new PowerNodeSample[0], new PowerLinkSample[0], new PowerEquipment[0], new PowerConsumerSample[0]); Assert.Throws<InvalidOperationException>(() => r.System.Power.Fork().Allocate(missing));
            var fork = r.System.Fork(); fork.Power.SetPriorities(new[] { "clinic", "plant", pump, sprinkler, winch, "island-clinic" }); Assert.AreNotEqual(r.System.ReadState().Canonical(), fork.ReadState().Canonical());
            // A demand larger than supply receives zero while a later consumer
            // can still receive its complete smaller demand.
            var layoutJson = r.Layout.ToJson().With("infrastructure", JsonValue.Array(r.Layout.infrastructure.Select(x => x.ToJson().With("initial_demand", JsonValue.Number(x.id == "clinic" ? 3 : 1)))));
            var power = new PowerSystem(LayoutDef.Read(layoutJson)); var frame = r.PowerFrame(); var allocation = power.Allocate(frame); Assert.AreEqual(0, allocation.Grants["clinic"]); Assert.AreEqual(1, allocation.Grants[winch]); Assert.AreEqual(1, allocation.Grants[pump]);
            Assert.AreEqual(power.ExportPower().Canonical(), power.RestorePower(power.ExportPower(), frame).ExportPower().Canonical());
        }

        [Test, NUnit.Framework.Property("case_id", "check-c05/assertion/assertion_id=check-c05-a04")]
        public void LaterMechanicsMissingFramesAndUnintroducedDefinitionsFailClosed()
        {
            var r = Build(fire: false, structure: false, lava: false);
            foreach (string kind in new[] { "firebreak", "sprinkler", "crawler", "brace", "winch", "cooler", "shelter" })
            {
                string before = r.System.ReadState().Canonical(); var command = r.Command("place_equipment", O("equipment_id", kind, "socket_id", kind + ":0", "orientation_step", 0));
                Assert.AreEqual("UNKNOWN_TARGET", r.System.Preview(command, r.Frame(), "active").ReasonCode); Assert.Throws<CommandException>(() => r.Apply(command)); Assert.AreEqual(before, r.System.ReadState().Canonical());
            }
            Assert.Throws<InvalidOperationException>(() => r.System.Preview(r.Command("place_equipment", O("equipment_id", "beacon", "socket_id", "beacon:0", "orientation_step", 0)), null, "active"));
            var absent = new EquipmentSystem(r.Mission, r.Layout, r.Definitions, new WaterSystem(r.Layout), new TransportSystem(r.Mission, r.Layout));
            Assert.AreEqual("UNKNOWN_TARGET", absent.Preview(r.Command("place_equipment", O("equipment_id", "cooler", "socket_id", "cooler:0", "orientation_step", 0)), r.Frame(), "active").ReasonCode);
            var early = Build(world: "W1"); foreach (var d in early.Definitions.Where(x => x.first_world_id != "W1")) Assert.AreEqual("UNKNOWN_TARGET", early.System.Preview(early.Command("place_equipment", O("equipment_id", d.id, "socket_id", d.id + ":0", "orientation_step", 0)), early.Frame(), "active").ReasonCode);
            foreach (var d in early.Definitions)
            {
                var introduced = Build(world: d.first_world_id); Assert.IsTrue(introduced.System.Preview(introduced.Command("place_equipment", O("equipment_id", d.id, "socket_id", d.id + ":0", "orientation_step", 0)), introduced.Frame(), "active").Allowed, d.id);
            }
            var available = Build(); string id = available.Place("firebreak"); ((MechanicFixture)available.System.Mechanics).Burning = true;
            Assert.AreEqual("UNSAFE_TARGET", available.System.Preview(available.Command("activate_tool", O("equipment_instance_id", id, "action", "firebreak")), available.Frame(), "active").ReasonCode);
            string shelter = available.Place("shelter"); IReadOnlyCollection<string> protection = null;
            for (int n = 0; n < 600; n++) { available.Tick++; available.Sink.BeginBoundary(available.Tick, available.Tick); available.System = available.System.HazardDamage(new OperationContext(available.Tick, available.Tick, true, available.Sink), available.Frame(heat: 600, occupants: 1), out protection); Assert.Contains(shelter, protection.ToList()); }
            Assert.AreEqual(0, Item(available, shelter)["parameters"]["protection_remaining"].Integer); Assert.AreEqual(100, Item(available, shelter)["durability"].Integer); available.Damage(heat: 600, occupants: 1); Assert.AreEqual(99, Item(available, shelter)["durability"].Integer);
            var interval = Build(); string intervalShelter = interval.Place("shelter");
            interval.Tick++; interval.Sink.BeginBoundary(interval.Tick, 1);
            interval.System = interval.System.HazardDamage(new OperationContext(interval.Tick, 1, true, interval.Sink), interval.Frame(heat: 1000, occupants: 1, hot: true, arrival: true), out protection);
            Assert.Contains(intervalShelter, protection.ToList()); Assert.AreEqual(599, Item(interval, intervalShelter)["parameters"]["protection_remaining"].Integer);
            interval.Tick++; interval.Sink.BeginBoundary(interval.Tick, 1);
            interval.System = interval.System.HazardDamage(new OperationContext(interval.Tick, 1, false, interval.Sink), interval.Frame(heat: 1000, occupants: 1, hot: true), out protection);
            Assert.AreEqual(100, Item(interval, intervalShelter)["durability"].Integer, "Persistent Hot state is not a second arrival on nonpulse O"); Assert.AreEqual(599, Item(interval, intervalShelter)["parameters"]["protection_remaining"].Integer);
            for (int scenario = 2; scenario <= 600; scenario++)
            {
                interval.Tick++; interval.Sink.BeginBoundary(interval.Tick, scenario);
                interval.System = interval.System.HazardDamage(new OperationContext(interval.Tick, scenario, true, interval.Sink), interval.Frame(heat: 1000, occupants: 1, hot: true), out protection);
                Assert.Contains(intervalShelter, protection.ToList());
            }
            Assert.AreEqual(0, Item(interval, intervalShelter)["parameters"]["protection_remaining"].Integer); Assert.IsTrue(Item(interval, intervalShelter)["parameters"]["exhaustion_emitted"].Boolean);
            var finalPulseState = interval.System.ReadState();
            interval.System = interval.System.RestoreOwnedState(finalPulseState, interval.Tick, owner => FrameFor(interval.Layout, owner, interval.Tick));
            interval.Tick++; interval.Sink.BeginBoundary(interval.Tick, 600);
            interval.System = interval.System.HazardDamage(new OperationContext(interval.Tick, 600, false, interval.Sink), interval.Frame(heat: 1000, occupants: 1, hot: true), out protection);
            Assert.AreEqual(100, Item(interval, intervalShelter)["durability"].Integer, "Nonpulse after final protected pulse consumes no unit and cannot replay arrival"); Assert.AreEqual(0, Item(interval, intervalShelter)["parameters"]["protection_remaining"].Integer);
            interval.Tick++; interval.Sink.BeginBoundary(interval.Tick, 601);
            interval.System = interval.System.HazardDamage(new OperationContext(interval.Tick, 601, true, interval.Sink), interval.Frame(heat: 1000, occupants: 1, hot: true), out protection);
            Assert.IsFalse(protection.Contains(intervalShelter)); Assert.AreEqual(99, Item(interval, intervalShelter)["durability"].Integer, "Next hazardous pulse is unprotected");
            Assert.Throws<InvalidOperationException>(() => r.System.ProduceCoolant(new OperationContext(1, 1, true, new RuntimeEventSink()), new Dictionary<string, bool> { { "plant", true } }));
            // No production/domain handler claims a complete mission outcome.
            Assert.IsFalse(typeof(IOperationSystems).IsAssignableFrom(typeof(EquipmentSystem)));
        }
    }
}
