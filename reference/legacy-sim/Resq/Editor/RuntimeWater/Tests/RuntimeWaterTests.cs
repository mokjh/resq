using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Simulation;
using UnityEngine;

namespace Resq.Tests.RuntimeWater
{
    [TestFixture, Category("runtime_water")]
    public sealed class RuntimeWaterTests
    {
        private static string Root => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        private static JsonValue Json(string text) => StrictJson.Parse(text);
        private static JsonValue Golden(string id) => Json(File.ReadAllText(Path.Combine(Root, "specification/fixtures/runtime-golden-v1.json")))["cases"].Items.Single(item => item["id"].Text == id);
        private static WaterNode Node(string id, long volume, long elevation = 0, long area = 10000, string kind = "terrain") => WaterNode.Read(JsonValue.Object(new Dictionary<string, JsonValue> {
            {"id",JsonValue.String(id)}, {"kind",JsonValue.String(kind)}, {"region_id",JsonValue.String("region:" + id)}, {"elevation_mm",JsonValue.Number(elevation)}, {"area_mm2",JsonValue.Number(area)}, {"initial_volume_mm3",JsonValue.Number(volume)} }));
        private static WaterRateDef Rate(string id, string node, long rate) => WaterRateDef.Read(JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String(id)}, {"water_node_id",JsonValue.String(node)}, {"initial_rate_mm3_per_second",JsonValue.Number(rate)} }));
        private static HydraulicLink Link(string id, string from, string to, long conductance = 200, long sill = 0, string direction = "both", string fromKind = "water_node", string toKind = "water_node", string gate = null, bool open = true, string barrier = null, long cap = WaterSystem.MaximumTransfer) => HydraulicLink.Read(JsonValue.Object(new Dictionary<string, JsonValue> {
            {"id",JsonValue.String(id)}, {"from_id",JsonValue.String(from)}, {"to_id",JsonValue.String(to)}, {"from_kind",JsonValue.String(fromKind)}, {"to_kind",JsonValue.String(toKind)}, {"direction",JsonValue.String(direction)}, {"sill_mm",JsonValue.Number(sill)}, {"conductance_mm2_per_second",JsonValue.Number(conductance)}, {"maximum_transfer_mm3",JsonValue.Number(cap)}, {"gate_id",gate == null ? JsonValue.Null() : JsonValue.String(gate)}, {"initial_gate_open",JsonValue.Bool(open)}, {"barrier_socket_id",barrier == null ? JsonValue.Null() : JsonValue.String(barrier)} }));
        private static ResourceStoreDef Store(string id, string node, long initial, long capacity = WaterSystem.MaximumVolume) => ResourceStoreDef.Read(JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String(id)}, {"unit",JsonValue.String("water_mm3")}, {"initial_amount",JsonValue.Number(initial)}, {"capacity",JsonValue.Number(capacity)}, {"water_node_id",JsonValue.String(node)} }));
        private static Socket Socket(string id, string equipment, string from = null, string to = null, string link = null, string store = null) => Resq.Content.Socket.Read(JsonValue.Object(new Dictionary<string, JsonValue> {
            {"id",JsonValue.String(id)}, {"kind",JsonValue.String("water")}, {"node_id",JsonValue.Null()}, {"permitted_equipment_ids",JsonValue.Array(new[] {JsonValue.String(equipment)})}, {"orientation_steps",Json("[0]")}, {"relocation_zone",JsonValue.String("zone")}, {"footprint_cell_ids",Json("[\"a\"]")}, {"hydraulic_link_id",link == null ? JsonValue.Null() : JsonValue.String(link)}, {"intake_node_id",from == null ? JsonValue.Null() : JsonValue.String(from)}, {"outlet_node_id",to == null ? JsonValue.Null() : JsonValue.String(to)}, {"power_node_id",equipment == "pump" ? JsonValue.String("power") : JsonValue.Null()}, {"support_id",JsonValue.Null()}, {"lava_segment_id",JsonValue.Null()}, {"fire_cell_ids",equipment == "sprinkler" ? Json("[\"fire:a\"]") : Json("[]")}, {"resource_store_id",store == null ? JsonValue.Null() : JsonValue.String(store)}, {"enabled",JsonValue.Bool(true)} }));
        private static WaterSystem System(IEnumerable<WaterNode> nodes, IEnumerable<HydraulicLink> links = null, IEnumerable<WaterRateDef> sources = null, IEnumerable<WaterRateDef> sinks = null, IEnumerable<ResourceStoreDef> stores = null, IEnumerable<Socket> sockets = null) => new WaterSystem(nodes, links ?? new HydraulicLink[0], sources ?? new WaterRateDef[0], sinks ?? new WaterRateDef[0], stores ?? new ResourceStoreDef[0], sockets ?? new Socket[0]);
        private static WaterPulseResult Pulse(WaterSystem system, IEnumerable<WaterPump> pumps = null, IEnumerable<WaterSprinkler> sprinklers = null, IReadOnlyDictionary<string, long> sea = null, IReadOnlyDictionary<string, bool> gates = null, IEnumerable<string> barriers = null) => system.Pulse(pumps ?? new WaterPump[0], sprinklers ?? new WaterSprinkler[0], sea ?? new Dictionary<string, long>(), gates ?? new Dictionary<string, bool>(), barriers ?? new string[0]);
        private static long Grant(WaterPulseResult result, string id) => result.Grants.Single(item => item.Id == id).GrantedMm3;
        private static string Boundary(WaterSystem system) => system.ReadWater().Canonical() + system.ReadRateRemainders().Canonical() + system.ReadLedger().Canonical();
        private static long Total(WaterSystem system) => system.ReadWater().Items.Sum(item => item["volume_mm3"].Integer);
        private static void Conserves(WaterSystem system)
        {
            var ledger = system.ReadLedger(); Assert.That(Total(system), Is.EqualTo(ledger["initial_total_mm3"].Integer + ledger["direct_sources_mm3"].Integer + ledger["sea_in_mm3"].Integer - ledger["drains_mm3"].Integer - ledger["sea_out_mm3"].Integer - ledger["sprayed_mm3"].Integer)); system.ValidateBoundary();
        }

        [Test, NUnit.Framework.Property("case_id", "check-c03/assertion/assertion_id=check-c03-a01")]
        public void AllFrozenWaterGoldensExecuteProductionWaterCode()
        {
            var actualGoldenIds = Json(File.ReadAllText(Path.Combine(Root, "specification/fixtures/runtime-golden-v1.json")))["cases"].Items.Where(item => item["suite"].Text == "runtime-water").Select(item => item["id"].Text).OrderBy(id => id, StringComparer.Ordinal);
            Assert.That(actualGoldenIds, Is.EqualTo(new[] { "RW01-source-carry", "RW03-largest-remainder-tie", "RW04-passive-pump-drain-compete", "RW05-pump-head-derating" }));
            var golden = Golden("RW01-source-carry"); var input = golden["input"]; var expected = golden["expected"];
            Assert.That(input["initial_remainder"].Integer, Is.EqualTo(0));
            var system = System(new[] { Node("a", 0) }, sources: new[] { Rate("a", "a", input["rate_mm3_per_second"].Integer) });
            for (int index = 0; index < input["hazard_pulses"].Integer; index++)
            {
                var result = Pulse(system); Assert.That(Grant(result, "source:a"), Is.EqualTo(expected["injected_per_pulse_mm3"].Items[index].Integer));
                Assert.That(system.ReadRateRemainders().Items[0]["remainder"].Integer, Is.EqualTo(expected["remainder_after_each_pulse"].Items[index].Integer)); Conserves(system);
            }
            Assert.That(system.Volume("a"), Is.EqualTo(expected["total_injected_mm3"].Integer));
            golden = Golden("RW03-largest-remainder-tie"); input = golden["input"]; expected = golden["expected"];
            var allocation = WaterSystem.Allocate(input["available_mm3"].Integer, input["requests"].Items.Reverse().Select(item => new WaterRequest(item["id"].Text, item["requested_mm3"].Integer)));
            foreach (var pair in expected["grants_mm3"].Members) Assert.That(allocation[pair.Key], Is.EqualTo(pair.Value.Integer));
            Assert.That(input["available_mm3"].Integer - allocation.Values.Sum(), Is.EqualTo(expected["remaining_mm3"].Integer));
            golden = Golden("RW04-passive-pump-drain-compete"); input = golden["input"]; expected = golden["expected"];
            var source = input["source"]; var edge = input["passive_link"]; var pump = input["pump"]; var sink = input["sink"];
            Assert.That(source["remainder"].Integer, Is.EqualTo(0)); Assert.That(sink["remainder"].Integer, Is.EqualTo(0));
            Assert.That(pump["rate_mm3_per_second"].Integer, Is.EqualTo(20000000)); Assert.That(pump["maximum_lift_mm"].Integer, Is.EqualTo(5000));
            system = System(input["water_nodes"].Items.Select(item => Node(item["id"].Text, item["volume_mm3"].Integer, item["elevation_mm"].Integer, item["area_mm2"].Integer)),
                new[] { Link("ab", edge["from_id"].Text, edge["to_id"].Text, edge["conductance_mm2_per_second"].Integer, edge["sill_mm"].Integer, cap: edge["maximum_transfer_mm3"].Integer) },
                new[] { Rate("a", source["destination_id"].Text, source["rate_mm3_per_second"].Integer) }, new[] { Rate("a", sink["from_id"].Text, sink["rate_mm3_per_second"].Integer) },
                sockets: new[] { Socket("pump-socket", "pump", pump["from_id"].Text, pump["to_id"].Text) });
            var competition = Pulse(system, new[] { new WaterPump("ac", "pump-socket", true, pump["powered"].Boolean, pump["remainder"].Integer) });
            foreach (var pair in expected["requests_mm3"].Members) Assert.That(competition.Grants.Single(item => item.Id == pair.Key).RequestedMm3, Is.EqualTo(pair.Value.Integer));
            foreach (var pair in expected["grants_mm3"].Members) Assert.That(Grant(competition, pair.Key), Is.EqualTo(pair.Value.Integer));
            foreach (var pair in expected["new_volumes_mm3"].Members) Assert.That(system.Volume(pair.Key), Is.EqualTo(pair.Value.Integer));
            Assert.That(competition.IntervalLedger["initial_total_mm3"].Integer, Is.EqualTo(expected["old_total_mm3"].Integer)); Assert.That(competition.IntervalLedger["direct_sources_mm3"].Integer, Is.EqualTo(expected["direct_source_mm3"].Integer)); Assert.That(competition.IntervalLedger["drains_mm3"].Integer, Is.EqualTo(expected["drained_mm3"].Integer)); Assert.That(Total(system), Is.EqualTo(expected["new_total_mm3"].Integer)); Conserves(system);
            golden = Golden("RW05-pump-head-derating"); input = golden["input"]; expected = golden["expected"];
            Assert.That(input["rate_mm3_per_second"].Integer, Is.EqualTo(20000000)); Assert.That(input["maximum_lift_mm"].Integer, Is.EqualTo(5000));
            for (int index = 0; index < input["lifts_mm"].Items.Count; index++)
            {
                long remainder; long request = WaterSystem.PumpRequest(0, input["lifts_mm"].Items[index].Integer, input["initial_remainder"].Integer, true, true, out remainder);
                Assert.That(request, Is.EqualTo(expected["requested_mm3_per_pulse"].Items[index].Integer)); Assert.That(remainder, Is.EqualTo(expected["remainders"].Items[index].Integer));
            }
        }

        [Test, NUnit.Framework.Property("case_id", "check-c03/assertion/assertion_id=check-c03-a02")]
        public void OldStateSharedAllocationAllLedgersDeratingBarrierAndRepeatability()
        {
            // RW02/RW04: all four consumers share one finite tank. Sources enter B;
            // sea arrivals and internal arrivals cannot be re-exported this pulse.
            var system = System(new[] { Node("a", 1000, 100, kind: "tank"), Node("b", 0), Node("c", 0, 5095), Node("d", 0, 50) },
                new[] { Link("ab", "a", "b"), Link("bd", "b", "d", direction: "both"), Link("sea-d", "sea", "d", 100, direction: "forward", fromKind: "sea_boundary") },
                new[] { Rate("a", "a", 2000) }, new[] { Rate("a", "a", 20000) }, new[] { Store("tank", "a", 1000) },
                new[] { Socket("pump-socket", "pump", "a", "c"), Socket("spray-socket", "sprinkler", store: "tank") });
            var result = Pulse(system, new[] { new WaterPump("ac", "pump-socket", true, true) }, new[] { new WaterSprinkler("spray", "spray-socket", true, true) }, new Dictionary<string, long> { {"sea",150} });
            Assert.That(result.Grants.Single(item => item.Id == "sprinkler:spray").RequestedMm3, Is.EqualTo(500000));
            // Requests 2k/2k/2k/500k; floor shares 4/4/4/1185. Spray has
            // the largest residue, then edge/pump win the equal-residue ties.
            Assert.That(Grant(result, "edge:ab"), Is.EqualTo(5)); Assert.That(Grant(result, "pump:ac"), Is.EqualTo(5)); Assert.That(Grant(result, "sink:a"), Is.EqualTo(4)); Assert.That(result.SprayGrants["spray"], Is.EqualTo(1186));
            Assert.That(Grant(result, "edge:bd"), Is.EqualTo(0)); Assert.That(Grant(result, "edge:sea-d"), Is.EqualTo(1000));
            Assert.That(system.Volume("a"), Is.EqualTo(0)); Assert.That(system.Volume("b"), Is.EqualTo(5)); Assert.That(system.Volume("c"), Is.EqualTo(5)); Assert.That(system.Volume("d"), Is.EqualTo(1000));
            Assert.That(system.StoreAmount("tank"), Is.EqualTo(system.Volume("a"))); Conserves(system);
            var events = new RuntimeEventSink(); events.BeginBoundary(1, 1); result.EmitTransfers(new OperationContext(1, 1, true, events));
            Assert.That(events.Current.Select(item => item.ToJson()["payload"]["request_id"].Text), Is.EqualTo(result.Grants.Where(item => item.GrantedMm3 > 0).Select(item => item.Id)));
            var restored = system.Restore(system.ReadWater(), system.ReadRateRemainders(), system.ReadLedger()); Assert.That(Boundary(restored), Is.EqualTo(Boundary(system)));
            var fork = system.Fork(); fork.SetSourceRate("a", 0); Assert.That(Boundary(system), Is.Not.EqualTo(Boundary(fork)));

            // Unmet whole demand is discarded, fractional demand only is retained.
            long remainder; Assert.That(WaterSystem.PumpRequest(0, 0, 49999, true, true, out remainder), Is.EqualTo(2000000)); Assert.That(remainder, Is.EqualTo(49999));
            Assert.That(WaterSystem.PumpRequest(0, 5000, remainder, true, true, out remainder), Is.EqualTo(0)); Assert.That(remainder, Is.EqualTo(49999));
            Assert.That(WaterSystem.PumpRequest(0, 0, 17, false, true, out remainder), Is.EqualTo(0)); Assert.That(remainder, Is.EqualTo(17));
            Assert.That(WaterSystem.PumpRequest(0, 0, 17, true, false, out remainder), Is.EqualTo(0)); Assert.That(remainder, Is.EqualTo(17));
            var dry = System(new[] { Node("a", 0), Node("b", 0) }, sources: new[] { Rate("a", "a", 10) }, sockets: new[] { Socket("pump", "pump", "a", "b") });
            Assert.That(Grant(Pulse(dry, new[] { new WaterPump("p", "pump", true, true) }), "pump:p"), Is.EqualTo(1));
            Assert.That(Grant(Pulse(dry, new[] { new WaterPump("p", "pump", true, true) }), "pump:p"), Is.EqualTo(1)); Conserves(dry);
            var carried = System(new[] { Node("a", 100) }, sources: new[] { Rate("a", "a", 3) }, sinks: new[] { Rate("a", "a", 3) });
            for (int pulse = 0; pulse < 10; pulse++) Pulse(carried);
            Assert.That(carried.ReadLedger()["direct_sources_mm3"].Integer, Is.EqualTo(3)); Assert.That(carried.ReadLedger()["drains_mm3"].Integer, Is.EqualTo(3)); Assert.That(carried.Volume("a"), Is.EqualTo(100)); Conserves(carried);
            Pulse(carried); carried.SetSourceRate("a", 7); Assert.That(Grant(Pulse(carried), "source:a"), Is.EqualTo(1)); Assert.That(carried.ReadRateRemainders().Items.Single(item => item["kind"].Text == "source")["remainder"].Integer, Is.EqualTo(0)); Conserves(carried);

            // Sea outflow is a competing request and is a separate ledger sink.
            var outgoing = System(new[] { Node("a", 1000, 100) }, new[] { Link("sea", "a", "sea", 200, toKind: "sea_boundary") }, sinks: new[] { Rate("a", "a", 20000) });
            var outResult = Pulse(outgoing, sea: new Dictionary<string, long> { {"sea",0} }); Assert.That(Grant(outResult, "edge:sea"), Is.EqualTo(500)); Assert.That(Grant(outResult, "sink:a"), Is.EqualTo(500)); Assert.That(outgoing.ReadLedger()["sea_out_mm3"].Integer, Is.EqualTo(500)); Conserves(outgoing);

            // RW06: intact barrier raises sill, overtops; break publication is
            // supplied by stage 6 and changes only the following interval.
            var barrier = System(new[] { Node("a", 10000000, 0), Node("b", 0) }, new[] { Link("ab", "a", "b", 10, barrier: "barrier", gate: "gate", open: true) }, sockets: new[] { Socket("barrier", "barrier", link: "ab") });
            Assert.That(Grant(Pulse(barrier, barriers: new[] { "barrier" }), "edge:ab"), Is.EqualTo(400));
            Assert.That(Grant(Pulse(barrier), "edge:ab"), Is.EqualTo(999));
            Assert.That(Grant(Pulse(barrier, gates: new Dictionary<string, bool> { {"gate",false} }), "edge:ab"), Is.EqualTo(0)); Conserves(barrier);
            var held = System(new[] { Node("a", 5000000), Node("b", 0) }, new[] { Link("ab", "a", "b", 10, barrier: "barrier") }, sockets: new[] { Socket("barrier", "barrier", link: "ab") });
            Assert.That(Grant(Pulse(held, barriers: new[] { "barrier" }), "edge:ab"), Is.EqualTo(0)); Assert.That(held.Volume("a"), Is.EqualTo(5000000));
            var oneWay = System(new[] { Node("a", 10000), Node("b", 1000000) }, new[] { Link("ab", "a", "b", direction: "forward") }); Assert.That(Grant(Pulse(oneWay), "edge:ab"), Is.EqualTo(0));

            // RW07: the accepted Core PRNG generates bounded graphs, independent
            // of execution ordering. Production code must repeat byte-for-byte
            // under shuffled authored arrays and conserve every interval.
            var rng = new XorShift32(734321);
            for (int graph = 0; graph < 40; graph++)
            {
                var nodes = Enumerable.Range(0, 8).Select(index => Node("n" + index, rng.Bounded(20000000), (long)rng.Bounded(2000) - 1000)).ToArray();
                var links = Enumerable.Range(0, 8).Select(index => Link("l" + index, "n" + index, "n" + ((index + 1) % 8), rng.Bounded(1000001), (long)rng.Bounded(1000) - 500, direction: index % 2 == 0 ? "forward" : "both", cap: rng.Bounded(100000001))).ToArray();
                var sources = new[] { Rate("s", "n0", rng.Bounded(1000000001)) }; var sinks = new[] { Rate("d", "n4", rng.Bounded(1000000001)) };
                var first = System(nodes, links, sources, sinks); var second = System(nodes.Reverse(), links.Reverse(), sources.Reverse(), sinks.Reverse());
                for (int pulse = 0; pulse < 12; pulse++)
                {
                    var firstPulse = Pulse(first); var secondPulse = Pulse(second);
                    Assert.That(Boundary(first), Is.EqualTo(Boundary(second)), "graph " + graph + " pulse " + pulse); Assert.That(firstPulse.Grants.Select(item => item.GrantedMm3), Is.EqualTo(secondPulse.Grants.Select(item => item.GrantedMm3))); Conserves(first); Conserves(second);
                }
            }
        }

        [Test, NUnit.Framework.Property("case_id", "check-c03/assertion/assertion_id=check-c03-a03")]
        public void HighGradientArithmeticAndInvalidStateRejectBeforePromotion()
        {
            var system = System(new[] { Node("a", WaterSystem.MaximumVolume, 100000), Node("b", 0, -10000) }, new[] { Link("ab", "a", "b", 1000000) });
            var result = Pulse(system); Assert.That(Grant(result, "edge:ab"), Is.EqualTo(WaterSystem.MaximumTransfer)); Assert.That(system.Volume("a"), Is.EqualTo(WaterSystem.MaximumVolume - WaterSystem.MaximumTransfer)); Conserves(system);
            var requests = Enumerable.Range(0, 32).Select(index => new WaterRequest("edge:" + index.ToString("D2"), WaterSystem.MaximumTransfer)).ToArray();
            Assert.That(WaterSystem.Allocate(WaterSystem.MaximumVolume, requests).Values.Sum(), Is.EqualTo(3200000000L));
            Assert.That(WaterSystem.Allocate(3199999999L, requests).Values.Sum(), Is.EqualTo(3199999999L));
            Assert.That(WaterSystem.Allocate(0, new WaterRequest[0]).Count, Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() => WaterSystem.Allocate(1, requests.Concat(new[] { new WaterRequest("extra", 1) })));
            Assert.Throws<InvalidOperationException>(() => WaterSystem.Allocate(-1, requests)); Assert.Throws<InvalidOperationException>(() => new WaterRequest("bad", -1)); Assert.Throws<InvalidOperationException>(() => new WaterRequest("bad", WaterSystem.MaximumTransfer + 1));
            long remainder; Assert.Throws<OverflowException>(() => WaterSystem.PumpRequest(long.MinValue, long.MaxValue, 0, true, true, out remainder)); Assert.Throws<InvalidOperationException>(() => WaterSystem.RateRequest(-1, 0, out remainder)); Assert.Throws<InvalidOperationException>(() => new WaterPump("p", "socket", true, true, 50000));
            string before = Boundary(system); var water = system.ReadWater(); var rates = system.ReadRateRemainders(); var ledger = system.ReadLedger();
            Assert.Throws<InvalidOperationException>(() => system.Restore(JsonValue.Array(water.Items.Select(item => item["id"].Text == "a" ? item.With("volume_mm3", JsonValue.Number(-1)) : item)), rates, ledger));
            Assert.Throws<InvalidOperationException>(() => system.Restore(water, rates, ledger.With("direct_sources_mm3", JsonValue.Number(1))));
            Assert.Throws<InvalidOperationException>(() => system.Restore(water, rates, ledger.With("drains_mm3", JsonValue.Number(-1))));
            Assert.Throws<OverflowException>(() => system.Restore(water, rates, ledger.With("direct_sources_mm3", JsonValue.Number(long.MaxValue)).With("drains_mm3", JsonValue.Number(long.MaxValue))));
            Assert.That(Boundary(system), Is.EqualTo(before));
            var full = System(new[] { Node("a", WaterSystem.MaximumVolume) }, sources: new[] { Rate("a", "a", 10) }); before = Boundary(full);
            Assert.Throws<InvalidOperationException>(() => Pulse(full)); Assert.That(Boundary(full), Is.EqualTo(before));
            var tank = System(new[] { Node("a", 100, kind: "tank") }, sources: new[] { Rate("a", "a", 10) }, stores: new[] { Store("tank", "a", 100, 100) }); before = Boundary(tank);
            Assert.Throws<InvalidOperationException>(() => Pulse(tank)); Assert.That(Boundary(tank), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => System(new[] { Node("a", 100, kind: "tank") }, stores: new[] { Store("tank1", "a", 100), Store("tank2", "a", 100) }));
            var sea = System(new[] { Node("a", 0) }, new[] { Link("sea", "sea", "a", fromKind: "sea_boundary") }); before = Boundary(sea);
            Assert.Throws<InvalidOperationException>(() => Pulse(sea)); Assert.That(Boundary(sea), Is.EqualTo(before));
            var carry = System(new[] { Node("a", 0) }, sources: new[] { Rate("a", "a", 3) }); Pulse(carry); before = Boundary(carry);
            Assert.Throws<InvalidOperationException>(() => carry.Restore(carry.ReadWater(), JsonValue.Array(carry.ReadRateRemainders().Items.Select(item => item.With("remainder", JsonValue.Number(10)))), carry.ReadLedger())); Assert.That(Boundary(carry), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => carry.SetSourceRate("a", -1)); Assert.That(Boundary(carry), Is.EqualTo(before));
        }
    }
}
