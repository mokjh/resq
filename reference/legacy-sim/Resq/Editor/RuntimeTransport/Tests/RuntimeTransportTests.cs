using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Simulation;
using Resq.Simulation.Rescue;
using UnityEngine;

namespace Resq.Tests.RuntimeTransport
{
    [TestFixture, Category("runtime_transport")]
    public sealed class RuntimeTransportTests
    {
        private static string Root => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        private static JsonValue J(string text) => StrictJson.Parse(text);
        private static JsonValue O(params object[] pairs)
        {
            var d = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) { var v = pairs[i + 1]; d.Add((string)pairs[i], v == null ? JsonValue.Null() : v is JsonValue j ? j : v is string s ? JsonValue.String(s) : v is bool b ? JsonValue.Bool(b) : JsonValue.Number(Convert.ToInt64(v))); }
            return JsonValue.Object(d);
        }
        private static JsonValue Golden(string id) => J(File.ReadAllText(Path.Combine(Root, "specification/fixtures/runtime-golden-v1.json")))["cases"].Items.Single(x => x["id"].Text == id);
        private sealed class Rig
        {
            public TransportSystem System;
            public string[] NodeIds, EdgeIds, ResidentIds;
            public readonly RuntimeEventSink Sink = new RuntimeEventSink();
            public long Tick;
            public TransportFrame Frame(bool dockOpen = true, bool destinationSafe = true, bool originSafe = true, bool edgeOpen = true, bool powered = true, bool exposure = false, long? closure = null, long speed = 1000)
                => new TransportFrame(Tick, NodeIds.Select(x => new TransportNodeSample(x, x == "p" ? originSafe : x == "e" ? destinationSafe : true, x != "p" && x != "e" || dockOpen, powered)), EdgeIds.Select(x => new TransportEdgeSample(x, edgeOpen, powered, speed, closure)), ResidentIds.ToDictionary(x => x, x => exposure, StringComparer.Ordinal));
            public JsonValue Step(bool dockOpen = true, bool destinationSafe = true, bool originSafe = true, bool edgeOpen = true, bool powered = true, bool exposure = false, bool pulse = true, long speed = 1000)
            {
                Tick++; Sink.BeginBoundary(Tick, Tick); var c = new OperationContext(Tick, Tick, pulse, Sink); var f = Frame(dockOpen, destinationSafe, originSafe, edgeOpen, powered, exposure, speed: speed);
                System.ResolveMovement(c, f); System.Derive(c, f); System.ValidateBoundary(); var state = System.ReadState();
                Assert.AreEqual(ResidentIds.Length, state["residents"].Items.Count, "People conserved at every boundary");
                Assert.AreEqual(ResidentIds.Length, state["residents"].Items.Select(x => x["id"].Text).Distinct(StringComparer.Ordinal).Count(), "No duplicate resident identity");
                foreach (var p in state["residents"].Items)
                    if (p["location"]["kind"].Text == "vehicle") Assert.AreEqual(1, state["vehicles"].Items.Single(v => v["id"].Text == p["location"]["vehicle_id"].Text)["occupant_ids"].Items.Count(x => x.Text == p["id"].Text));
                return state;
            }
            public JsonValue Break(string equipmentInstanceId, bool repeat = false)
            {
                Tick++; Sink.BeginBoundary(Tick, Tick); var context = new OperationContext(Tick, Tick, true, Sink);
                System.BreakDeployedVehicle(equipmentInstanceId, context);
                if (repeat)
                {
                    string state = System.ReadState().Canonical(); int events = Sink.Current.Count;
                    System.BreakDeployedVehicle(equipmentInstanceId, context);
                    Assert.AreEqual(state, System.ReadState().Canonical(), "Double break is state-idempotent"); Assert.AreEqual(events, Sink.Current.Count, "Double break emits no duplicate events");
                }
                var frame = Frame(); System.ResolveMovement(context, frame); System.Derive(context, frame); System.ValidateBoundary();
                Assert.AreEqual(0, System.ReadBoundaryFacts(Tick).Count, "Breaking an occupied vehicle does not complete its edge");
                Assert.AreEqual(ResidentIds.Length, System.Population); return System.ReadState();
            }
            public void Assign(string kind, string subject, long sequence, params JsonValue[] stops)
            {
                var command = GameCommand.Read(O("schema_version", 1, "command_id", "cmd:" + sequence, "run_id", "synthetic", "sequence", sequence, "requested_tick", Tick, "actor_input", "reference_replay", "kind", "assign_route", "payload", O("subject_kind", kind, "subject_id", subject, "stops", JsonValue.Array(stops))));
                System.Assign((AssignRoute)command.Payload, Tick, sequence);
            }
        }
        private static JsonValue Stop(string node, string action, string group = null) => O("node_id", node, "action", action, "group_id", group);
        // Immutable typed synthetic graph: the accepted compiler fixture supplies
        // closed records. These component fixtures are not authored mission runs.
        private static Rig Build(int count = 4, bool aboard = false, string vehicleKind = "fixed_bus", long travel = 10, long edgeCapacity = 2, bool stepFree = true, bool alternate = false, bool held = false, bool reverseEdge = false, long receivingCapacity = 16, long pickupBerths = 1, bool finalRefuge = true, bool deployedVehicle = false, bool directEdge = true, string spawnNode = "p")
        {
            var payload = J(File.ReadAllText(Path.Combine(Root, "content/runtime/fixtures/compiler-valid.json")));
            var sourceLayout = payload["layouts"].Items[0]; var templateNode = sourceLayout["nav_nodes"].Items[0]; var templateEdge = sourceLayout["nav_edges"].Items[0];
            var ids = Enumerable.Range(1, count).Select(i => "fixture:resident:group:" + i.ToString("00")).ToArray();
            var nodes = new[] { "p", "j", "e" }.Select(id => templateNode.With("id", JsonValue.String(id)).With("kind", JsonValue.String(id == "e" && finalRefuge ? "evacuation" : "dock")).With("resident_capacity", JsonValue.Number(id == "e" ? receivingCapacity : 16)).With("vehicle_slots", JsonValue.Number(2)).With("dock_berths", JsonValue.Number(id == "p" ? pickupBerths : 1)).With("mobility_classes", J("[\"standard\",\"step_free\"]")).With("power_consumer_id", JsonValue.Null()));
            string mode = vehicleKind == "skiff" ? "water" : vehicleKind == "winch" ? "cable" : "ground";
            Func<string, string, string, long, bool, JsonValue> edge = (id, from, to, ticks, sf) => templateEdge.With("id", JsonValue.String(id)).With("from_node_id", JsonValue.String(from)).With("to_node_id", JsonValue.String(to)).With("traversal_class", JsonValue.String(mode)).With("base_operation_ticks", JsonValue.Number(ticks)).With("movement_capacity", JsonValue.Number(edgeCapacity)).With("mobility_classes", J("[\"standard\",\"step_free\"]")).With("step_free", JsonValue.Bool(sf)).With("required_equipment_socket_id", JsonValue.Null()).With("required_power_consumer_id", JsonValue.Null());
            var navEdges = new List<JsonValue>(); if (directEdge) navEdges.Add(edge("direct", "p", "e", travel, stepFree));
            if (reverseEdge) navEdges.Add(edge("reverse", "e", "p", travel, true));
            if (alternate) { navEdges.Add(edge("a", "p", "j", travel / 2, true)); navEdges.Add(edge("b", "j", "e", travel - travel / 2, true)); navEdges.Add(edge("z", "p", "j", travel / 2, true)); }
            var missionJson = payload["missions"].Items[0].With("total_residents", JsonValue.Number(count)).With("groups", JsonValue.Array(new[] { O("id", "group", "resident_ids", JsonValue.Array(ids.Select(JsonValue.String)), "start_node_id", spawnNode, "assembly_node_id", "p", "initially_held", held) })).With("milestones", J("[{\"id\":\"group.boarded\",\"kind\":\"group_boarded\",\"target\":\"group\"},{\"id\":\"final.started\",\"kind\":\"final_unload_started\",\"target\":\"all_residents\"},{\"id\":\"all.safe\",\"kind\":\"all_evacuated\",\"target\":\"all_residents\"}]"));
            var vehicle = O("vehicle_id", "bus", "kind", vehicleKind, "node_id", "p", "seats", vehicleKind == "winch" ? 2 : 4, "movement_slots", vehicleKind == "skiff" ? 1 : 2, "initial_route_id", null, "initial_occupant_ids", JsonValue.Array((aboard ? ids : new string[0]).Select(JsonValue.String)));
            var layoutJson = sourceLayout.With("nav_nodes", JsonValue.Array(nodes)).With("nav_edges", JsonValue.Array(navEdges)).With("routes", J("[]")).With("fixed_vehicles", deployedVehicle ? J("[]") : JsonValue.Array(new[] { vehicle })).With("spawns", JsonValue.Array(ids.Select((id, index) => O("resident_id", id, "group_id", "group", "mobility_class", index == 0 ? "step_free" : "standard", "start_node_id", spawnNode, "fixed_vehicle_id", aboard ? "bus" : null))));
            var rig = new Rig { System = new TransportSystem(CompiledMission.Read(missionJson), LayoutDef.Read(layoutJson)), NodeIds = new[] { "p", "j", "e" }, EdgeIds = navEdges.Select(x => x["id"].Text).OrderBy(x => x, StringComparer.Ordinal).ToArray(), ResidentIds = ids };
            if (deployedVehicle) { if (aboard) throw new InvalidOperationException("Deployed fixture must board through actual transport"); rig.System.AddDeployedVehicle(VehicleSpawn.Read(vehicle), "eq:vehicle"); }
            rig.Sink.BeginBoundary(0, 0); rig.System.SampleInitialMilestones(new OperationContext(0, 0, false, rig.Sink)); return rig;
        }
        private static JsonValue Person(JsonValue state, int index = 0) => state["residents"].Items[index];
        private static JsonValue Vehicle(JsonValue state) => state["vehicles"].Items.Single();

        [Test, NUnit.Framework.Property("case_id", "check-c04/assertion/assertion_id=check-c04-a01")]
        public void PeopleConservedGroupsFinalLegAndCausalMilestones()
        {
            WalkingBoundaryFactsAreFreshIsolatedAndRepeatable();
            ActualRelaxedClockCompletesNodeDwellsInScenarioTicks();
            NodeTerminalTransitionsCompleteActualDwells();
            var rig = Build(); rig.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"), Stop("e", "evacuate"));
            JsonValue state = null;
            for (int i = 0; i < 100; i++) state = rig.Step();
            Assert.AreEqual(4, rig.System.EvacuatedCount); Assert.IsTrue(rig.System.TerminalTransportClear);
            Assert.IsTrue(state["residents"].Items.All(x => x["final_leg_mode"].Text == "ground"));
            var marks = rig.System.Milestones.Items.ToDictionary(x => x["id"].Text, x => x["operation_tick"].Integer);
            Assert.Less(marks["group.boarded"], marks["final.started"]); Assert.Less(marks["final.started"], marks["all.safe"]);
            Assert.AreEqual(3, rig.Sink.Recent.Count(x => x.ToJson()["kind"].Text == "MilestoneReached"));
            var water = Build(aboard: true, vehicleKind: "skiff"); water.Assign("vehicle", "bus", 1, Stop("e", "evacuate"));
            for (int i = 0; i < 60; i++) state = water.Step();
            Assert.IsTrue(state["residents"].Items.All(x => x["final_leg_mode"].Text == "water"));
            var isolated = water.System.Fork(); Assert.AreEqual(water.System.ReadState().Canonical(), isolated.ReadState().Canonical()); isolated.ReleaseGroup("group");
            Assert.Throws<InvalidOperationException>(() => rig.System.ResolveMovement(new OperationContext(101, 101, true, rig.Sink), null));
            ClockCompositionAndGuidedEpoch();
            BoardingHistorySurvivesEventRetentionAndIsHashed();
            // Ground backup remains conserved and unrescued; a safe non-pulse
            // boundary resets consecutive exposure before another hazard pulse.
            var exposed = Build(count: 1); for (int i = 0; i < 19; i++) exposed.Step(exposure: true);
            Assert.AreEqual(19, Person(exposed.System.ReadState())["consecutive_exposure_ticks"].Integer);
            exposed.Step(exposure: false, pulse: false); Assert.AreEqual(0, Person(exposed.System.ReadState())["consecutive_exposure_ticks"].Integer);
            for (int i = 0; i < 20; i++) state = exposed.Step(exposure: true);
            Assert.AreEqual("awaiting_backup", Person(state)["state"].Text); Assert.AreEqual("p", Person(state)["location"]["node_id"].Text); Assert.AreEqual(0, exposed.System.EvacuatedCount);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c04/assertion/assertion_id=check-c04-a02")]
        public void SeatsSlotsParkingBerthsReceivingAndFairQueuesRemainDistinct()
        {
            var rig = Build(aboard: true); rig.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); var state = rig.Step();
            Assert.AreEqual(0, rig.System.Milestones.Items.Single(x => x["id"].Text == "group.boarded")["operation_tick"].Integer); Assert.AreEqual(0, rig.System.Milestones.Items.Single(x => x["id"].Text == "group.boarded")["scenario_tick"].Integer);
            Assert.AreEqual(4, Vehicle(state)["occupant_ids"].Items.Count);
            Assert.AreEqual("route:1", state["routes"].Items.Single()["id"].Text); Assert.IsTrue(state["reservations"].Items.All(x => x["id"].Text.StartsWith("reservation:1:", StringComparison.Ordinal)));
            Assert.AreEqual(2, state["reservations"].Items.Single(x => x["resource_kind"].Text == "edge_slots")["quantity"].Integer);
            Assert.AreEqual(1, state["reservations"].Items.Single(x => x["resource_kind"].Text == "return_node")["quantity"].Integer);
            Assert.AreEqual(1, state["reservations"].Items.Single(x => x["resource_kind"].Text == "dock_berth")["quantity"].Integer);
            Assert.AreEqual(0, state["reservations"].Items.Count(x => x["resource_kind"].Text == "node_people"));
            for (int i = 0; i < 10; i++) state = rig.Step();
            Assert.AreEqual(0, state["transits"].Items.Count); Assert.AreEqual(4, Vehicle(state)["occupant_ids"].Items.Count);
            var arrivalFacts = rig.System.ReadBoundaryFacts(rig.Tick);
            CollectionAssert.AreEqual(rig.ResidentIds, arrivalFacts.Select(x => x.ResidentId).ToArray(), "Actual vehicle arrival credits each real occupant in stable order");
            Assert.IsTrue(arrivalFacts.All(x => x.EdgeId == "direct"));
            Assert.AreSame(arrivalFacts, rig.System.ReadBoundaryFacts(rig.Tick), "Repeated reads preserve the same immutable observation");
            Assert.AreEqual(1, state["reservations"].Items.Count(x => x["resource_kind"].Text == "node_people"));
            Assert.AreEqual("vehicle", Person(state)["location"]["kind"].Text);
            for (int i = 0; i < 10; i++) state = rig.Step();
            Assert.AreEqual(3, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual("evacuated", Person(state)["state"].Text);
            Assert.AreEqual(0, rig.System.ReadBoundaryFacts(rig.Tick).Count, "Unloading is not another completed edge");
            Assert.AreEqual(4, arrivalFacts.Count, "An older immutable observation cannot change when transport advances");
            var boarding = Build(); boarding.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group")); state = boarding.Step();
            Assert.AreEqual(0, boarding.System.ReadBoundaryFacts(boarding.Tick).Count, "Boarding admission has no edge credit");
            Assert.AreEqual("node", Person(state)["location"]["kind"].Text); Assert.AreEqual(0, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual(1, state["reservations"].Items.Count);
            Assert.AreEqual("vehicle_seat", state["reservations"].Items[0]["resource_kind"].Text);
            var queue = Build(edgeCapacity: 1, travel: 2); queue.Assign("group", "group", 1, Stop("e", "evacuate")); state = queue.Step();
            Assert.AreEqual(1, state["transits"].Items.Count); Assert.AreEqual(queue.ResidentIds[0], state["transits"].Items[0]["mover_id"].Text); Assert.AreEqual(3, state["queues"].Items.Count);
            for (int i = 0; i < 12; i++) state = queue.Step();
            Assert.AreEqual(4, queue.System.EvacuatedCount); Assert.AreEqual(0, state["queues"].Items.Count); Assert.AreEqual(0, state["reservations"].Items.Count);
            Assert.IsFalse(queue.System.Milestones.Items.Any(x => x["id"].Text == "group.boarded"), "Walking must not manufacture boarding history");
            // Two actual vehicles at one pickup cannot both reserve one person.
            var concurrent = Build();
            concurrent.System.AddDeployedVehicle(VehicleSpawn.Read(O("vehicle_id", "second", "kind", "crawler", "node_id", "p", "seats", 4, "movement_slots", 2, "initial_route_id", null, "initial_occupant_ids", J("[]"))), "eq:second");
            concurrent.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group")); concurrent.Assign("vehicle", "second", 2, Stop("p", "pickup", "group"));
            state = concurrent.Step(); Assert.AreEqual(2, state["transfers"].Items.Count); Assert.AreEqual(2, state["transfers"].Items.Select(x => x["resident_id"].Text).Distinct().Count());
            for (int i = 0; i < 30; i++) state = concurrent.Step();
            var allOccupants = state["vehicles"].Items.SelectMany(x => x["occupant_ids"].Items).Select(x => x.Text).ToArray(); Assert.AreEqual(4, allOccupants.Length); Assert.AreEqual(4, allOccupants.Distinct().Count());
            // Evacuated residents remain real receiving occupants. A capacity-one
            // refuge prevents the next unload and preserves the other passengers.
            var receiving = Build(aboard: true, receivingCapacity: 1); receiving.Assign("vehicle", "bus", 1, Stop("e", "evacuate"));
            for (int i = 0; i < 35; i++) state = receiving.Step();
            Assert.AreEqual(1, receiving.System.EvacuatedCount); Assert.AreEqual(3, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual(0, state["transfers"].Items.Count); Assert.AreEqual("capacity", state["queues"].Items.Single()["kind"].Text);
            var capacityWait = receiving.System.ReadDockWaitFacts(receiving.Tick).Single(); Assert.AreEqual("e", capacityWait.NodeId); Assert.AreEqual("bus", capacityWait.VehicleId); Assert.IsTrue(capacityWait.Loaded); Assert.IsTrue(capacityWait.ForBerthOrReceivingCapacity); Assert.IsFalse(capacityWait.ChosenUnloadDockClosed);
            // Two water vehicles have enough edge slots but compete for the one
            // receiving berth. Releasing the first trip's return reservation
            // finally admits the older waiting second skiff.
            var berths = Build(aboard: true, vehicleKind: "skiff", pickupBerths: 2, reverseEdge: true);
            berths.System.AddDeployedVehicle(VehicleSpawn.Read(O("vehicle_id", "second", "kind", "skiff", "node_id", "p", "seats", 4, "movement_slots", 1, "initial_route_id", null, "initial_occupant_ids", J("[]"))), "eq:second");
            berths.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); berths.Assign("vehicle", "second", 2, Stop("e", "pickup", "group")); state = berths.Step();
            Assert.AreEqual(1, state["transits"].Items.Count); Assert.AreEqual(1, state["reservations"].Items.Single(x => x["resource_kind"].Text == "edge_slots")["quantity"].Integer); Assert.AreEqual("berth", state["queues"].Items.Single()["kind"].Text);
            for (int i = 0; i < 50; i++) state = berths.Step(); Assert.AreEqual(4, berths.System.EvacuatedCount); Assert.AreEqual(1, state["queues"].Items.Count);
            berths.Assign("vehicle", "bus", 3, Stop("p", "pickup", "group")); for (int i = 0; i < 25; i++) state = berths.Step();
            Assert.AreEqual("e", state["vehicles"].Items.Single(x => x["id"].Text == "second")["location"]["node_id"].Text); Assert.AreEqual(0, state["queues"].Items.Count); Assert.AreEqual(0, state["reservations"].Items.Count);

        }

        [Test, NUnit.Framework.Property("case_id", "check-c04/assertion/assertion_id=check-c04-a03")]
        public void GoldenClosurePartialTransfersEgressReturnBackupAndWinchPower()
        {
            DockWaitFactsUseActualSafeHoldingAndChosenUnload();
            DeployedVehicleBreakPreservesLocationsAndPassengerWork();
            var g3 = Golden("RT03-closed-dock-final-boarding-tick"); var rig = Build(); rig.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"));
            JsonValue state = rig.Step(); for (int i = 0; i < g3["input"]["boarding_progress_before_step"].Integer; i++) state = rig.Step();
            Assert.AreEqual(9, state["transfers"].Items.Single()["progress_operation_ticks"].Integer); state = rig.Step(dockOpen: false);
            Assert.AreEqual(0, rig.System.ReadBoundaryFacts(rig.Tick).Count, "Cancelled partial boarding has no edge credit");
            Assert.AreEqual(0, state["transfers"].Items.Count); Assert.AreEqual(0, state["reservations"].Items.Count); Assert.AreEqual("node", Person(state)["location"]["kind"].Text); Assert.AreEqual("p", Person(state)["location"]["node_id"].Text); Assert.AreEqual(0, Vehicle(state)["occupant_ids"].Items.Count);
            var partial = Build(); partial.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group")); for (int i = 0; i < 20; i++) state = partial.Step();
            Assert.AreEqual(1, Vehicle(state)["occupant_ids"].Items.Count); state = partial.Step(dockOpen: false); Assert.AreEqual(1, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual(0, state["transfers"].Items.Count);
            // Power freezes a partial boarding at its precise tick; it resumes
            // without creating a second transfer or charging a new seat.
            var power = Build(count: 1); power.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group")); for (int i = 0; i < 5; i++) state = power.Step(); long progress = state["transfers"].Items.Single()["progress_operation_ticks"].Integer;
            for (int i = 0; i < 7; i++) state = power.Step(powered: false);
            Assert.AreEqual(progress, state["transfers"].Items.Single()["progress_operation_ticks"].Integer); Assert.AreEqual("waiting_power", Vehicle(state)["status"].Text);
            for (int i = 0; i < 6; i++) state = power.Step(); Assert.AreEqual(1, Vehicle(state)["occupant_ids"].Items.Count);
            var g4 = Golden("RT04-return-without-teleport"); var returning = Build(aboard: true); returning.Assign("vehicle", "bus", 1, Stop("e", "evacuate"));
            returning.Step(); for (int i = 0; i < g4["input"]["progress_before_closure_operation_ticks"].Integer; i++) state = returning.Step();
            state = returning.Step(destinationSafe: false, edgeOpen: false); Assert.AreEqual(g4["expected"]["mode"].Text, state["transits"].Items.Single()["mode"].Text); Assert.AreEqual(3, state["transits"].Items.Single()["remaining_return_ticks"].Integer); Assert.AreEqual("edge", Vehicle(state)["location"]["kind"].Text);
            Assert.AreEqual(0, returning.System.ReadBoundaryFacts(returning.Tick).Count);
            // Remove the route at the boundary to observe arrival without an
            // immediate new legal admission; restoration cannot flip the return.
            returning.Assign("vehicle", "bus", 2, Stop("p", "pickup", "group"));
            for (int i = 0; i < 3; i++) { state = returning.Step(); Assert.AreEqual(0, returning.System.ReadBoundaryFacts(returning.Tick).Count, "A partial-edge return is not a completed edge"); } Assert.AreEqual("p", Vehicle(state)["location"]["node_id"].Text); Assert.AreEqual(9, returning.Tick); Assert.AreEqual(0, state["reservations"].Items.Count);
            // The golden clock starts already in transit at O=0; this test has
            // an explicit O=1 admission, so all golden arrival O values offset +1.
            Assert.AreEqual(g4["expected"]["arrival_at_origin_operation_tick"].Integer + 1, returning.Tick);
            var forward = Build(aboard: true, travel: 4); forward.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); forward.Step(); state = forward.Step(edgeOpen: false); Assert.AreEqual("forward", state["transits"].Items.Single()["mode"].Text);
            for (int i = 0; i < 3; i++) state = forward.Step(edgeOpen: false); Assert.AreEqual("e", Vehicle(state)["location"]["node_id"].Text);
            Assert.AreEqual(4, forward.System.ReadBoundaryFacts(forward.Tick).Count, "A latched closure permits actual forward egress and counts its occupants");
            var backup = Build(aboard: true); backup.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); backup.Step(); backup.Step(); state = backup.Step(destinationSafe: false, originSafe: false, edgeOpen: false);
            Assert.AreEqual("backup", state["transits"].Items.Single()["mode"].Text); long frozen = state["transits"].Items.Single()["progress_operation_ticks"].Integer;
            Assert.AreEqual(0, backup.System.ReadBoundaryFacts(backup.Tick).Count);
            for (int i = 0; i < 5; i++) { state = backup.Step(); Assert.AreEqual(0, backup.System.ReadBoundaryFacts(backup.Tick).Count, "Failed partial progress has no edge credit"); } Assert.AreEqual(frozen, state["transits"].Items.Single()["progress_operation_ticks"].Integer); Assert.AreEqual("failed", Vehicle(state)["status"].Text); Assert.AreEqual(4, Vehicle(state)["occupant_ids"].Items.Count); Assert.IsFalse(backup.System.TerminalTransportClear); Assert.AreEqual(0, state["reservations"].Items.Count); Assert.IsTrue(state["residents"].Items.All(x => x["state"].Text == "awaiting_backup"));
            var winch = Build(count: 2, aboard: true, vehicleKind: "winch", travel: 50); winch.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); winch.Step(); for (int i = 0; i < 4; i++) winch.Step();
            for (int i = 0; i < 20; i++) state = winch.Step(powered: false);
            Assert.AreEqual("braked", Vehicle(state)["status"].Text); Assert.AreEqual(4, state["transits"].Items.Single()["progress_operation_ticks"].Integer);
            state = winch.Step(powered: false); Assert.AreEqual("return", state["transits"].Items.Single()["mode"].Text); Assert.AreEqual(3, state["transits"].Items.Single()["remaining_return_ticks"].Integer);
            winch.Assign("vehicle", "bus", 2, Stop("p", "pickup", "group")); for (int i = 0; i < 3; i++) state = winch.Step(); Assert.AreEqual("p", Vehicle(state)["location"]["node_id"].Text);
            var combined = Build(count: 2, aboard: true, vehicleKind: "winch", travel: 50); combined.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); combined.Step(); for (int i = 0; i < 4; i++) combined.Step();
            state = combined.Step(powered: false, edgeOpen: false, destinationSafe: false, originSafe: false); Assert.AreEqual("backup", state["transits"].Items.Single()["mode"].Text); Assert.AreEqual(4, state["transits"].Items.Single()["progress_operation_ticks"].Integer); Assert.AreEqual("failed", Vehicle(state)["status"].Text); Assert.AreEqual(0, state["reservations"].Items.Count);
            var combinedReturn = Build(count: 2, aboard: true, vehicleKind: "winch", travel: 50); combinedReturn.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); combinedReturn.Step(); for (int i = 0; i < 4; i++) combinedReturn.Step(); state = combinedReturn.Step(powered: false, edgeOpen: false, destinationSafe: false); Assert.AreEqual("return", state["transits"].Items.Single()["mode"].Text); Assert.AreEqual(3, state["transits"].Items.Single()["remaining_return_ticks"].Integer);
            var duty = Build(count: 2, aboard: true, vehicleKind: "winch", travel: 50, reverseEdge: true, finalRefuge: false); duty.Assign("vehicle", "bus", 1, Stop("e", "dropoff"), Stop("e", "pickup", "group"), Stop("p", "dropoff")); bool sawLoadedRestArrival = false, sawEndpointUnloading = false;
            for (int i = 0; i < 240; i++)
            {
                state = duty.Step(); var vehicle = Vehicle(state);
                if (vehicle["rest_remaining"].Integer == 30 && vehicle["location"]["kind"].Text == "node" && vehicle["location"]["node_id"].Text == "p" && vehicle["occupant_ids"].Items.Count > 0) { sawLoadedRestArrival = true; sawEndpointUnloading |= state["transfers"].Items.Count == 1; }
            }
            Assert.IsTrue(sawLoadedRestArrival); Assert.IsTrue(sawEndpointUnloading); Assert.AreEqual(0, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual(0, Vehicle(state)["rest_remaining"].Integer); Assert.AreEqual(0, Vehicle(state)["duty_used"].Integer); Assert.IsTrue(state["residents"].Items.All(x => x["location"]["node_id"].Text == "p"));
            var reload = Build(count: 2, aboard: true, vehicleKind: "winch", travel: 50, reverseEdge: true, finalRefuge: false); reload.Assign("vehicle", "bus", 1, Stop("e", "dropoff"), Stop("e", "pickup", "group"), Stop("p", "dropoff"), Stop("p", "pickup", "group"), Stop("e", "dropoff"));
            bool sawEmptyRest = false, sawReloadAfterRest = false;
            for (int i = 0; i < 360; i++)
            {
                state = reload.Step(); var vehicle = Vehicle(state);
                if (vehicle["rest_remaining"].Integer > 0 && vehicle["occupant_ids"].Items.Count == 0) { sawEmptyRest = true; Assert.AreEqual(0, state["transfers"].Items.Count, "Pending pickup cannot interrupt empty rest"); }
                if (state["transfers"].Items.Any(x => x["node_id"].Text == "p" && x["direction"].Text == "board")) { sawReloadAfterRest = true; Assert.AreEqual(0, vehicle["rest_remaining"].Integer); Assert.AreEqual(0, vehicle["duty_used"].Integer); }
            }
            Assert.IsTrue(sawEmptyRest); Assert.IsTrue(sawReloadAfterRest); Assert.AreEqual(0, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual(50, Vehicle(state)["duty_used"].Integer); Assert.IsTrue(state["residents"].Items.All(x => x["location"]["node_id"].Text == "e"));
            var resume = Build(count: 2, aboard: true, vehicleKind: "winch", travel: 50); resume.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); resume.Step(); resume.Step(); resume.Step(powered: false); state = resume.Step(); Assert.AreEqual(2, state["transits"].Items.Single()["progress_operation_ticks"].Integer); Assert.AreEqual("forward", state["transits"].Items.Single()["mode"].Text);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c04/assertion/assertion_id=check-c04-a04")]
        public void StepFreeCanonicalPathsClosurePredictionAndCapturedSpeed()
        {
            BoundaryFactsRequireBothActualStages();
            Assert.Throws<CommandException>(() => GameCommand.Read(O("schema_version", 1, "command_id", "illegal-vehicle-assemble", "run_id", "synthetic", "sequence", 1, "requested_tick", 1, "actor_input", "reference_replay", "kind", "assign_route", "payload", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] { Stop("p", "assemble") })))));
            var rig = Build(count: 1, stepFree: false, alternate: true); var path = rig.System.FindPath("p", "e", "ground", "step_free", 1, rig.Frame()); CollectionAssert.AreEqual(new[] { "a", "b" }, path);
            CollectionAssert.AreEqual(new[] { "direct" }, rig.System.FindPath("p", "e", "ground", "standard", 1, rig.Frame()), "Equal total time prefers fewer edges");
            var blocked = Build(count: 1, stepFree: false); Assert.AreEqual(0, blocked.System.FindPath("p", "e", "ground", "step_free", 1, blocked.Frame()).Count);
            Assert.AreEqual(0, rig.System.FindPath("p", "e", "ground", "standard", 1, rig.Frame(closure: 10)).Count, "Closure on the arrival tick is already too late");
            rig.Assign("group", "group", 1, Stop("e", "evacuate")); var state = rig.Step(); Assert.AreEqual("a", state["transits"].Items.Single()["edge_id"].Text); Assert.AreEqual(5, state["transits"].Items.Single()["duration_operation_ticks"].Integer);
            state = rig.Step(speed: 2000); Assert.AreEqual(5, state["transits"].Items.Single()["duration_operation_ticks"].Integer);
            bool sawIntermediatePeak = false;
            for (int i = 0; i < 10; i++)
            {
                state = rig.Step();
                if (rig.Tick == 6)
                {
                    Assert.AreEqual(0, state["residents"].Items.Count(x => x["location"]["kind"].Text == "node" && x["location"]["node_id"].Text == "j"));
                    var peaks = rig.System.ReadNodeOccupancyPeaks(rig.Tick); CollectionAssert.AreEqual(new[] { "e", "j", "p" }, peaks.Keys.ToArray()); Assert.AreEqual(1, peaks["j"], "Real intermediate arrival counts even when the same boundary admits its onward walk"); sawIntermediatePeak = true;
                    Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)peaks)["j"] = 99);
                    var isolatedPeaks = rig.System.Fork(); Assert.AreNotSame(peaks, isolatedPeaks.ReadNodeOccupancyPeaks(rig.Tick)); isolatedPeaks.RestoreBoundaryCursor(rig.Tick); Assert.Throws<InvalidOperationException>(() => isolatedPeaks.ReadNodeOccupancyPeaks(rig.Tick)); Assert.AreEqual(1, rig.System.ReadNodeOccupancyPeaks(rig.Tick)["j"]);
                    var zeroDwell = rig.System.ReadCompletedNodeDwells(rig.Tick).Single(); Assert.AreEqual("j", zeroDwell.NodeId); Assert.AreEqual(0, zeroDwell.ScenarioTicks, "Same-boundary onward departure has a legitimate zero-length node dwell");
                }
            }
            Assert.IsTrue(sawIntermediatePeak); Assert.AreEqual(1, rig.System.EvacuatedCount); Assert.AreEqual(1, rig.System.ReadNodeOccupancyPeaks(rig.Tick)["e"], "Terminal receiving occupants remain actual node occupants");
            var held = Build(count: 1, held: true); held.Assign("group", "group", 1, Stop("e", "evacuate")); state = held.Step(); Assert.AreEqual(0, state["transits"].Items.Count); held.System.ReleaseGroup("group"); state = held.Step(); Assert.AreEqual(1, state["transits"].Items.Count);
            // Fixed preplaced bus begins its authored depot route only after
            // actual access opens, independently of the scenario hazard epoch.
            var bus = Build(aboard: true); bus.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); state = bus.Step(edgeOpen: false, pulse: false); Assert.AreEqual(0, state["transits"].Items.Count); state = bus.Step(pulse: false); Assert.AreEqual(1, state["transits"].Items.Count);
        }
        private static void WalkingBoundaryFactsAreFreshIsolatedAndRepeatable()
        {
            var walking = Build(count: 3, travel: 2, edgeCapacity: 3);
            Assert.Throws<InvalidOperationException>(() => walking.System.ReadBoundaryFacts(0), "Initial milestone sampling does not fabricate movement facts");
            Assert.Throws<InvalidOperationException>(() => walking.System.ReadCompletedNodeDwells(0));
            walking.Assign("group", "group", 1, Stop("e", "evacuate"));
            walking.Step(); Assert.AreEqual(0, walking.System.ReadBoundaryFacts(walking.Tick).Count);
            var dwells = walking.System.ReadCompletedNodeDwells(walking.Tick); CollectionAssert.AreEqual(walking.ResidentIds, dwells.Select(x => x.ResidentId).ToArray()); Assert.IsTrue(dwells.All(x => x.NodeId == "p" && x.ScenarioTicks == 1));
            Assert.AreSame(dwells, walking.System.ReadCompletedNodeDwells(walking.Tick)); Assert.Throws<NotSupportedException>(() => ((IList<CompletedNodeDwell>)dwells).Add(dwells[0]));
            var dwellFork = walking.System.Fork(); Assert.AreNotSame(dwells, dwellFork.ReadCompletedNodeDwells(walking.Tick)); dwellFork.RestoreBoundaryCursor(walking.Tick); Assert.Throws<InvalidOperationException>(() => dwellFork.ReadCompletedNodeDwells(walking.Tick)); Assert.AreEqual(3, walking.System.ReadCompletedNodeDwells(walking.Tick).Count);
            walking.Step(); Assert.AreEqual(0, walking.System.ReadBoundaryFacts(walking.Tick).Count, "Partial forward progress has no edge credit");
            Assert.AreEqual(0, walking.System.ReadCompletedNodeDwells(walking.Tick).Count, "Travel is not node dwell"); Assert.AreEqual(3, dwells.Count); Assert.Throws<InvalidOperationException>(() => walking.System.ReadCompletedNodeDwells(walking.Tick - 1));
            var state = walking.Step(); Assert.AreEqual(3, walking.System.EvacuatedCount);
            Assert.AreEqual(3, walking.System.ReadCompletedNodeDwells(walking.Tick).Count); Assert.IsTrue(walking.System.ReadCompletedNodeDwells(walking.Tick).All(x => x.NodeId == "e" && x.ScenarioTicks == 0), "Immediate evacuation completes a legitimate zero-length destination dwell");
            var facts = walking.System.ReadBoundaryFacts(walking.Tick); string before = state.Canonical();
            CollectionAssert.AreEqual(walking.ResidentIds, facts.Select(x => x.ResidentId).ToArray()); Assert.IsTrue(facts.All(x => x.EdgeId == "direct"));
            Assert.AreSame(facts, walking.System.ReadBoundaryFacts(walking.Tick)); Assert.AreEqual(before, walking.System.ReadState().Canonical(), "Reads do not mutate authoritative transport state");
            Assert.Throws<NotSupportedException>(() => ((IList<CompletedEdgePassage>)facts).Add(new CompletedEdgePassage("fake", "direct")));
            Assert.Throws<NotSupportedException>(() => ((IList<CompletedEdgePassage>)facts)[0] = new CompletedEdgePassage("fake", "direct"));
            Assert.Throws<InvalidOperationException>(() => walking.System.ReadBoundaryFacts(walking.Tick - 1));
            Assert.Throws<InvalidOperationException>(() => walking.System.ReadBoundaryFacts(walking.Tick + 1));
            var fork = walking.System.Fork(); var forkFacts = fork.ReadBoundaryFacts(walking.Tick);
            Assert.AreNotSame(facts, forkFacts); CollectionAssert.AreEqual(facts.Select(x => x.ResidentId).ToArray(), forkFacts.Select(x => x.ResidentId).ToArray());
            fork.RestoreBoundaryCursor(walking.Tick); Assert.Throws<InvalidOperationException>(() => fork.ReadBoundaryFacts(walking.Tick), "Restoring a cursor cannot reconstruct discarded observations");
            Assert.AreEqual(3, walking.System.ReadBoundaryFacts(walking.Tick).Count, "Fork restoration cannot clear its source observation");
            walking.Step(); Assert.AreEqual(0, walking.System.ReadBoundaryFacts(walking.Tick).Count); Assert.AreEqual(3, facts.Count); Assert.AreEqual(3, forkFacts.Count);
            Assert.Throws<InvalidOperationException>(() => walking.System.ReadBoundaryFacts(walking.Tick - 1), "The owner only exposes its current actual boundary");
            var repeat = Build(count: 1, travel: 2, reverseEdge: true, finalRefuge: false);
            repeat.Assign("group", "group", 1, Stop("e", "assemble")); for (int i = 0; i < 3; i++) repeat.Step();
            var first = repeat.System.ReadBoundaryFacts(repeat.Tick).Single();
            repeat.Assign("group", "group", 2, Stop("p", "assemble")); for (int i = 0; i < 3; i++) repeat.Step();
            Assert.AreEqual("reverse", repeat.System.ReadBoundaryFacts(repeat.Tick).Single().EdgeId);
            repeat.Assign("group", "group", 3, Stop("e", "assemble")); for (int i = 0; i < 3; i++) repeat.Step();
            var again = repeat.System.ReadBoundaryFacts(repeat.Tick).Single(); Assert.AreEqual(first.ResidentId, again.ResidentId); Assert.AreEqual(first.EdgeId, again.EdgeId, "Repeating an edge preserves the resident identity for C06 distinct counting");
        }
        private static void BoundaryFactsRequireBothActualStages()
        {
            var rig = Build(count: 1, travel: 1); rig.Assign("group", "group", 1, Stop("e", "evacuate")); rig.Step();
            rig.Tick++; rig.Sink.BeginBoundary(rig.Tick, rig.Tick); var context = new OperationContext(rig.Tick, rig.Tick, true, rig.Sink); var frame = rig.Frame();
            rig.System.ResolveMovement(context, frame);
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadBoundaryFacts(rig.Tick), "Successful stage-7 arrival is not yet a completed transport boundary");
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadDockWaitFacts(rig.Tick));
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadCompletedNodeDwells(rig.Tick));
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadNodeOccupancyPeaks(rig.Tick));
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadBoundaryFacts(rig.Tick - 1), "New movement invalidates the previous boundary immediately");
            rig.System.Derive(context, frame); Assert.AreEqual(rig.ResidentIds.Single(), rig.System.ReadBoundaryFacts(rig.Tick).Single().ResidentId);
            string state = rig.System.ReadState().Canonical();
            Assert.Throws<InvalidOperationException>(() => rig.System.ResolveMovement(context, frame)); Assert.Throws<InvalidOperationException>(() => rig.System.Derive(context, frame));
            Assert.AreEqual(state, rig.System.ReadState().Canonical()); Assert.AreEqual(1, rig.System.ReadBoundaryFacts(rig.Tick).Count, "Rejected repeated stages neither duplicate nor drain facts");
            rig.System.RestoreBoundaryCursor(rig.Tick); Assert.Throws<InvalidOperationException>(() => rig.System.ReadBoundaryFacts(rig.Tick));
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadDockWaitFacts(rig.Tick));
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadCompletedNodeDwells(rig.Tick));
            Assert.Throws<InvalidOperationException>(() => rig.System.ReadNodeOccupancyPeaks(rig.Tick));
            rig.Step(); Assert.AreEqual(0, rig.System.ReadBoundaryFacts(rig.Tick).Count, "The first actual post-restore boundary has only its own facts");
        }
        private static void DockWaitFactsUseActualSafeHoldingAndChosenUnload()
        {
            var closed = Build(aboard: true, travel: 2); closed.Assign("vehicle", "bus", 1, Stop("e", "evacuate"));
            Assert.Throws<InvalidOperationException>(() => closed.System.ReadDockWaitFacts(0));
            var state = closed.Step(dockOpen: false, edgeOpen: false);
            Assert.AreEqual("p", Vehicle(state)["location"]["node_id"].Text); Assert.AreEqual("edge", state["queues"].Items.Single()["kind"].Text);
            var facts = closed.System.ReadDockWaitFacts(closed.Tick); var fact = facts.Single();
            Assert.AreEqual("e", fact.NodeId, "NodeId identifies the chosen unload dock rather than the physical holding node"); Assert.AreEqual("bus", fact.VehicleId); Assert.IsTrue(fact.Loaded); Assert.IsTrue(fact.ChosenUnloadDockClosed); Assert.IsFalse(fact.ForBerthOrReceivingCapacity, "Edge-only closed wait is excluded from max dock capacity queue time");
            string before = state.Canonical(); Assert.AreSame(facts, closed.System.ReadDockWaitFacts(closed.Tick)); Assert.AreEqual(before, closed.System.ReadState().Canonical());
            Assert.Throws<NotSupportedException>(() => ((IList<DockWaitSample>)facts).Add(fact));
            var fork = closed.System.Fork(); Assert.AreNotSame(facts, fork.ReadDockWaitFacts(closed.Tick)); fork.RestoreBoundaryCursor(closed.Tick); Assert.Throws<InvalidOperationException>(() => fork.ReadDockWaitFacts(closed.Tick)); Assert.AreEqual(1, closed.System.ReadDockWaitFacts(closed.Tick).Count);
            string restoredBefore = fork.ReadState().Canonical();
            var inspected = fork.InspectDockWaitFacts(new OperationContext(closed.Tick, closed.Tick, true), closed.Frame(dockOpen: false, edgeOpen: false)); Assert.AreEqual("e", inspected.Single().NodeId); Assert.IsFalse(inspected.Single().ForBerthOrReceivingCapacity); Assert.AreEqual(restoredBefore, fork.ReadState().Canonical(), "Saved-boundary inspection changes no location, queue, timer or rules projection"); Assert.AreEqual(fork.ReadState().Sha256(), StrictJson.Parse(restoredBefore).Sha256());
            Assert.Throws<InvalidOperationException>(() => fork.ReadDockWaitFacts(closed.Tick)); Assert.Throws<InvalidOperationException>(() => fork.ReadBoundaryFacts(closed.Tick)); Assert.Throws<InvalidOperationException>(() => fork.ReadCompletedNodeDwells(closed.Tick)); Assert.Throws<InvalidOperationException>(() => fork.ReadNodeOccupancyPeaks(closed.Tick));
            Assert.Throws<InvalidOperationException>(() => fork.InspectDockWaitFacts(new OperationContext(closed.Tick + 1, closed.Tick, false), closed.Frame(dockOpen: false, edgeOpen: false)));
            Assert.Throws<InvalidOperationException>(() => fork.InspectDockWaitFacts(new OperationContext(closed.Tick, closed.Tick, true), null)); Assert.AreEqual(restoredBefore, fork.ReadState().Canonical());
            closed.Step(dockOpen: false, edgeOpen: false); Assert.AreEqual(1, closed.System.ReadDockWaitFacts(closed.Tick).Count); Assert.Throws<InvalidOperationException>(() => closed.System.ReadDockWaitFacts(closed.Tick - 1));
            closed.Step(dockOpen: false); Assert.AreEqual(0, closed.System.ReadDockWaitFacts(closed.Tick).Count, "Approach travel cannot accumulate dock wait");
            closed.Step(dockOpen: false); Assert.AreEqual(0, closed.System.ReadDockWaitFacts(closed.Tick).Count);
            state = closed.Step(dockOpen: false); Assert.AreEqual("e", Vehicle(state)["location"]["node_id"].Text);
            fact = closed.System.ReadDockWaitFacts(closed.Tick).Single(); Assert.IsTrue(fact.ChosenUnloadDockClosed); Assert.IsTrue(fact.ForBerthOrReceivingCapacity); Assert.AreEqual("berth", state["queues"].Items.Single()["kind"].Text);
            closed.Step(); Assert.AreEqual(0, closed.System.ReadDockWaitFacts(closed.Tick).Count, "Actual unloading is not waiting"); Assert.AreEqual(1, facts.Count, "Old read-only samples remain stable");
            // A real closed route from a farther approach node must not begin
            // either dock window. The graph has only p→j→e, with no p→e edge.
            var far = Build(aboard: true, alternate: true, directEdge: false); far.Assign("vehicle", "bus", 1, Stop("e", "evacuate"));
            state = far.Step(dockOpen: false, edgeOpen: false); Assert.AreEqual("e", state["queues"].Items.Single()["target_node_id"].Text); Assert.AreEqual(0, far.System.ReadDockWaitFacts(far.Tick).Count);
            var unsafeHolding = Build(aboard: true); unsafeHolding.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); unsafeHolding.Step(dockOpen: false, edgeOpen: false, originSafe: false); Assert.AreEqual(0, unsafeHolding.System.ReadDockWaitFacts(unsafeHolding.Tick).Count);
            // Replacement removes the old queue. Its new pickup at the same
            // closed node is not the later unload stop, despite matching IDs.
            var replaced = Build(aboard: true); replaced.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); replaced.Step(dockOpen: false, edgeOpen: false);
            replaced.Assign("vehicle", "bus", 2, Stop("p", "pickup", "group")); replaced.Step(dockOpen: false, edgeOpen: false); Assert.AreEqual(0, replaced.System.ReadDockWaitFacts(replaced.Tick).Count, "Replaced unload target/sequence cannot retain an old wait fact");
            var pickup = Build(count: 1); pickup.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"), Stop("p", "dropoff"));
            state = pickup.Step(dockOpen: false); Assert.AreEqual("berth", state["queues"].Items.Single()["kind"].Text); Assert.AreEqual(0, pickup.System.ReadDockWaitFacts(pickup.Tick).Count, "A pickup queue is not its later unload, even at the same node");
            var failed = Build(count: 1, vehicleKind: "crawler", deployedVehicle: true); failed.Assign("vehicle", "bus", 1, Stop("e", "evacuate")); failed.Step(dockOpen: false, edgeOpen: false); failed.Break("eq:vehicle"); Assert.AreEqual(0, failed.System.ReadDockWaitFacts(failed.Tick).Count);
        }
        private static void DeployedVehicleBreakPreservesLocationsAndPassengerWork()
        {
            foreach (string kind in new[] { "skiff", "crawler", "winch" })
            {
                var parked = Build(count: 2, vehicleKind: kind, deployedVehicle: true);
                parked.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"));
                JsonValue state = null; for (int i = 0; i < 21; i++) state = parked.Step();
                Assert.AreEqual(2, Vehicle(state)["occupant_ids"].Items.Count); Assert.AreEqual(0, state["transfers"].Items.Count);
                state = parked.Break("eq:vehicle", repeat: true);
                Assert.AreEqual("failed", Vehicle(state)["status"].Text); Assert.AreEqual("p", Vehicle(state)["location"]["node_id"].Text); Assert.AreEqual(2, Vehicle(state)["occupant_ids"].Items.Count);
                Assert.IsTrue(state["residents"].Items.All(x => x["state"].Text == "awaiting_backup" && x["location"]["kind"].Text == "vehicle" && x["location"]["vehicle_id"].Text == "bus" && x["final_leg_mode"].IsNull));
                Assert.AreEqual(2, parked.Sink.Current.Count(x => x.ToJson()["kind"].Text == "ResidentAwaitingBackup")); Assert.AreEqual(0, state["reservations"].Items.Count); Assert.AreEqual(0, parked.System.EvacuatedCount); Assert.IsFalse(parked.System.TerminalTransportClear);
                int historicalEvents = parked.Sink.Recent.Count; state = parked.Break("eq:vehicle", repeat: true); Assert.AreEqual(historicalEvents, parked.Sink.Recent.Count);
                for (int i = 0; i < 5; i++) state = parked.Step(); Assert.AreEqual("failed", Vehicle(state)["status"].Text); Assert.AreEqual(2, Vehicle(state)["occupant_ids"].Items.Count);
                Assert.Throws<InvalidOperationException>(() => parked.Assign("vehicle", "bus", 2, Stop("e", "evacuate")), "Failed occupied vehicle cannot be reused");
            }
            var boarding = Build(count: 2, vehicleKind: "skiff", deployedVehicle: true); boarding.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"), Stop("e", "evacuate"));
            JsonValue boardingState = null; for (int i = 0; i < 5; i++) boardingState = boarding.Step(); Assert.AreEqual(4, boardingState["transfers"].Items.Single()["progress_operation_ticks"].Integer);
            boardingState = boarding.Break("eq:vehicle", repeat: true); Assert.AreEqual(0, boardingState["transfers"].Items.Count); Assert.AreEqual(0, boardingState["reservations"].Items.Count); Assert.AreEqual(0, Vehicle(boardingState)["occupant_ids"].Items.Count);
            Assert.IsTrue(boardingState["residents"].Items.All(x => x["state"].Text == "waiting" && x["location"]["kind"].Text == "node" && x["location"]["node_id"].Text == "p"));
            Assert.AreEqual(1, boarding.Sink.Current.Count(x => x.ToJson()["kind"].Text == "TransferCancelled")); Assert.AreEqual(0, boarding.Sink.Current.Count(x => x.ToJson()["kind"].Text == "ResidentAwaitingBackup"));
            var unloading = Build(count: 2, vehicleKind: "crawler", deployedVehicle: true); unloading.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"), Stop("e", "evacuate"));
            JsonValue unloadState = null; for (int i = 0; i < 37; i++) unloadState = unloading.Step(); Assert.AreEqual("unboard", unloadState["transfers"].Items.Single()["direction"].Text); Assert.AreEqual(5, unloadState["transfers"].Items.Single()["progress_operation_ticks"].Integer);
            unloadState = unloading.Break("eq:vehicle", repeat: true); Assert.AreEqual("e", Vehicle(unloadState)["location"]["node_id"].Text); Assert.AreEqual(2, Vehicle(unloadState)["occupant_ids"].Items.Count); Assert.AreEqual(0, unloadState["transfers"].Items.Count); Assert.AreEqual(0, unloadState["reservations"].Items.Count);
            Assert.AreEqual(1, unloading.Sink.Current.Count(x => x.ToJson()["kind"].Text == "TransferCancelled")); Assert.AreEqual(2, unloading.Sink.Current.Count(x => x.ToJson()["kind"].Text == "ResidentAwaitingBackup"));
            for (int i = 0; i < 12; i++) unloadState = unloading.Step(); Assert.AreEqual(0, unloading.System.EvacuatedCount); Assert.AreEqual("failed", Vehicle(unloadState)["status"].Text); Assert.IsTrue(unloadState["residents"].Items.All(x => x["location"]["kind"].Text == "vehicle"));
            var travelling = Build(count: 2, vehicleKind: "winch", travel: 50, deployedVehicle: true); travelling.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"), Stop("e", "evacuate"));
            JsonValue transitState = null; for (int i = 0; i < 26; i++) transitState = travelling.Step(); long originalProgress = transitState["transits"].Items.Single()["progress_operation_ticks"].Integer; string originalTransit = transitState["transits"].Items.Single()["id"].Text;
            Assert.AreEqual(4, originalProgress); transitState = travelling.Break("eq:vehicle", repeat: true);
            Assert.AreEqual("edge", Vehicle(transitState)["location"]["kind"].Text); Assert.AreEqual(originalTransit, Vehicle(transitState)["location"]["transit_id"].Text); Assert.AreEqual(originalProgress, transitState["transits"].Items.Single()["progress_operation_ticks"].Integer); Assert.AreEqual("backup", transitState["transits"].Items.Single()["mode"].Text); Assert.AreEqual(0, transitState["reservations"].Items.Count);
            Assert.AreEqual(1, travelling.Sink.Current.Count(x => x.ToJson()["kind"].Text == "RouteBlocked")); Assert.AreEqual(2, travelling.Sink.Current.Count(x => x.ToJson()["kind"].Text == "ResidentAwaitingBackup"));
            for (int i = 0; i < 8; i++) transitState = travelling.Step(); Assert.AreEqual(originalProgress, transitState["transits"].Items.Single()["progress_operation_ticks"].Integer); Assert.AreEqual(2, Vehicle(transitState)["occupant_ids"].Items.Count); Assert.IsFalse(travelling.System.TerminalTransportClear);
            // Validation rejects unknown/fixed bindings and invalid stage calls
            // before changing any resident, reservation, status or event.
            var fixedBus = Build(aboard: true); string before = fixedBus.System.ReadState().Canonical(); var context = new OperationContext(1, 1, true, fixedBus.Sink);
            Assert.Throws<InvalidOperationException>(() => fixedBus.System.BreakDeployedVehicle("bus", context)); Assert.Throws<InvalidOperationException>(() => fixedBus.System.BreakDeployedVehicle("eq:unknown", context)); Assert.AreEqual(before, fixedBus.System.ReadState().Canonical());
            var invalid = Build(count: 2, vehicleKind: "crawler", deployedVehicle: true); invalid.Step(); before = invalid.System.ReadState().Canonical();
            Assert.Throws<InvalidOperationException>(() => invalid.System.BreakDeployedVehicle("eq:vehicle", new OperationContext(1, 1, true, invalid.Sink)));
            Assert.Throws<InvalidOperationException>(() => invalid.System.BreakDeployedVehicle("eq:vehicle", new OperationContext(3, 3, true, invalid.Sink)));
            Assert.Throws<InvalidOperationException>(() => invalid.System.BreakDeployedVehicle("eq:vehicle", new OperationContext(2, 2, true)));
            Assert.AreEqual(before, invalid.System.ReadState().Canonical());
            Assert.Throws<InvalidOperationException>(() => invalid.System.AddDeployedVehicle(VehicleSpawn.Read(O("vehicle_id", "duplicate", "kind", "crawler", "node_id", "p", "seats", 6, "movement_slots", 2, "initial_route_id", null, "initial_occupant_ids", J("[]"))), "eq:vehicle")); Assert.AreEqual(before, invalid.System.ReadState().Canonical());
        }
        private static void BoardingHistorySurvivesEventRetentionAndIsHashed()
        {
            var rig = Build(); long sequence = 0;
            for (int cycle = 0; cycle < 65; cycle++)
            {
                rig.Assign("vehicle", "bus", ++sequence, Stop("p", "pickup", "group"));
                // Change only future stops before the first board completes. The
                // existing board finishes and then unloads, so no other group
                // member has ever boarded despite >256 real transfer events.
                for (int i = 0; i < 10; i++) rig.Step();
                rig.Assign("vehicle", "bus", ++sequence, Stop("p", "dropoff"));
                for (int i = 0; i < 11; i++) rig.Step();
            }
            Assert.AreEqual(256, rig.Sink.Recent.Count); Assert.AreEqual(1, rig.System.HistoricalMetrics.Items.Single()["value"].Integer); Assert.AreEqual(rig.ResidentIds[0], rig.System.HistoricalMetrics.Items.Single()["distinct_resident_ids"].Items.Single().Text); Assert.AreEqual(0, rig.System.Milestones.Items.Count);
            var peer = new SyntheticPeer(); var systems = new TransportOperationSystems(peer, rig.System, (a, b, c) => null, (a, b) => false);
            var identity = O("run_id", "component-history", "source_build_id", "synthetic-component-hash", "mission_version_id", "fixture-v1", "rules_version", "resq-rules-1", "content_hash", rig.System.ReadState().Sha256(), "layout_version_id", "fixture-wide", "layout_signature", J("{}").Sha256(), "rule_profile_id", "standard", "input_adapter_id", "reference_replay", "guidance_policy", "unguided", "challenge_id", "base", "modifiers_hash", J("[]").Sha256(), "seed", 1, "assisted", false); RunIdentity.Validate(identity);
            var snapshot = systems.ReadDomainState().With("schema_version", JsonValue.Number(1)).With("identity", identity).With("phase", JsonValue.String("active"))
                .With("state_revision", JsonValue.Number(0)).With("operation_tick", JsonValue.Number(rig.Tick)).With("scenario_tick", JsonValue.Number(rig.Tick)).With("scenario_accumulator", JsonValue.Number(0)).With("scenario_enabled", JsonValue.Bool(true)).With("rng", new XorShift32(1).ToJson()).With("last_admitted_sequence", JsonValue.Number(sequence)).With("pending_commands", J("[]")).With("command_receipts", J("[]")).With("end_request", JsonValue.Null()).With("outcome", JsonValue.Null());
            string actual = CanonicalState.RuleHash(snapshot);
            var emptyHistory = rig.System.HistoricalMetrics.Items.Single().With("value", JsonValue.Number(0)).With("distinct_resident_ids", J("[]"));
            Assert.AreNotEqual(actual, CanonicalState.RuleHash(snapshot.With("metrics", JsonValue.Array(new[] { emptyHistory }))), "Unlatched actual boarding history participates in rule hash");
            var churn = Build(); churn.Assign("vehicle", "bus", 1, Stop("p", "pickup", "group"), Stop("e", "evacuate")); for (int i = 0; i < 100; i++) churn.Step();
            for (int seq = 2; seq <= 7; seq++) churn.Assign("vehicle", "bus", seq, Stop("e", "pickup", "group"));
            Assert.AreEqual("route:7", churn.System.ReadState()["routes"].Items.Single()["id"].Text, "Accepted route ID follows sequence after many reservation/event creations");
        }
        private static JsonValue RenameResidents(JsonValue value)
        {
            if (value.Kind == "string" && value.Text.StartsWith("fixture:resident:", StringComparison.Ordinal)) return JsonValue.String("W1-M1:resident:" + value.Text.Substring("fixture:resident:".Length));
            if (value.Kind == "array") return JsonValue.Array(value.Items.Select(RenameResidents));
            if (value.Kind == "object") return JsonValue.Object(value.Members.ToDictionary(x => x.Key, x => RenameResidents(x.Value), StringComparer.Ordinal));
            return value;
        }
        private static void ActualRelaxedClockCompletesNodeDwellsInScenarioTicks()
        {
            // The actual OperationClock owns Relaxed's numerator/accumulator;
            // the test peer observes facts at the real stage-8 integration seam.
            var payload = RenameResidents(J(File.ReadAllText(Path.Combine(Root, "content/runtime/fixtures/compiler-valid.json"))));
            payload = payload.With("rule_profiles", JsonValue.Array(payload["rule_profiles"].Items.Concat(new[] { payload["rule_profiles"].Items.Single().With("id", JsonValue.String("relaxed")).With("scenario_numerator", JsonValue.Number(1)) })))
                .With("missions", JsonValue.Array(payload["missions"].Items.Select(m => m.With("id", JsonValue.String("W1-M1")).With("rule_profile_ids", J("[\"standard\",\"relaxed\"]")))));
            payload = payload.With("layouts", JsonValue.Array(payload["layouts"].Items.Select(l =>
            {
                l = l.With("fixed_vehicles", JsonValue.Array(new[] { O("vehicle_id", "bus", "kind", "fixed_bus", "node_id", "start", "seats", 4, "movement_slots", 2, "initial_route_id", null, "initial_occupant_ids", J("[]")) }));
                return l.With("graph_hash", JsonValue.String(ContentCompilation.ComputeGraphHash(l)));
            })));
            var content = ContentCompilation.Compile(payload);
            foreach (string mode in new[] { "board-nonpulse", "walk-nonpulse", "walk-pulse", "walk-zero-nonpulse" })
            {
                var observed = new List<Tuple<long, long, CompletedNodeDwell>>();
                var peer = new SyntheticPeer(observer: (authority, c) =>
                {
                    foreach (var dwell in authority.ReadCompletedNodeDwells(c.OperationTick)) observed.Add(Tuple.Create(c.OperationTick, c.ScenarioTick, dwell));
                    if (mode == "board-nonpulse" && c.OperationTick <= 10) Assert.AreEqual(0, authority.ReadCompletedNodeDwells(c.OperationTick).Count, "Partial boarding remains an incomplete node dwell");
                }, routesOpenInitially: true);
                var transport = new TransportSystem(content, "fixture-v1", "fixture-wide");
                var systems = new TransportOperationSystems(peer, transport, (a, b, c) => ((SyntheticPeer)b).Frame(a, c), (a, b) => false);
                string run = "relaxed-dwell-" + mode;
                var identity = O("run_id", run, "source_build_id", "synthetic-relaxed-dwell", "mission_version_id", "fixture-v1", "rules_version", "resq-rules-1", "content_hash", content.ContentHash, "layout_version_id", "fixture-wide", "layout_signature", content.LayoutSignature("fixture-wide"), "rule_profile_id", "relaxed", "input_adapter_id", "reference_replay", "guidance_policy", "unguided", "challenge_id", "base", "modifiers_hash", J("[]").Sha256(), "seed", 1, "assisted", false);
                var clock = new OperationClock(content, "fixture-v1", "fixture-wide", identity, systems);
                clock.Apply(GameCommand.Read(O("schema_version", 1, "command_id", "resume", "run_id", run, "sequence", 1, "requested_tick", 0, "actor_input", "reference_replay", "kind", "resume", "payload", O()))); clock.CompleteResumeCountdown();
                int beforeCommand = mode == "walk-nonpulse" ? 4 : mode == "walk-pulse" ? 5 : 0;
                for (int i = 0; i < beforeCommand; i++) Assert.IsNotNull(clock.Step(), clock.Fault);
                bool boarding = mode == "board-nonpulse";
                var route = O("subject_kind", boarding ? "vehicle" : "group", "subject_id", boarding ? "bus" : "group", "stops", JsonValue.Array(new[] { boarding ? Stop("start", "pickup", "group") : Stop("refuge", "evacuate") }));
                var admitted = clock.Admit(GameCommand.Read(O("schema_version", 1, "command_id", "route", "run_id", run, "sequence", 2, "requested_tick", clock.OperationTick + 1, "actor_input", "reference_replay", "kind", "assign_route", "payload", route)));
                Assert.AreEqual("queued", admitted.Status, admitted.ReasonCode);
                int afterCommand = boarding ? 11 : 1;
                for (int i = 0; i < afterCommand; i++) Assert.IsNotNull(clock.Step(), clock.Fault);
                var completed = observed.Single(); long expectedO = boarding ? 11 : beforeCommand + 1; long expectedS = expectedO / 2;
                Assert.AreEqual(expectedO, completed.Item1); Assert.AreEqual(expectedS, completed.Item2); Assert.AreEqual(expectedS, completed.Item3.ScenarioTicks); Assert.AreEqual("start", completed.Item3.NodeId); Assert.AreEqual("W1-M1:resident:group:01", completed.Item3.ResidentId);
                Assert.AreNotEqual(completed.Item1, completed.Item3.ScenarioTicks, "Node dwell must not use operation ticks");
                Assert.AreEqual(mode == "walk-pulse" ? 0 : 1, clock.ScenarioAccumulator, "Boarding and nonpulse walking finish on an actual Relaxed nonpulse");
                var snapshot = clock.ReadSnapshot().Value; Assert.IsTrue(snapshot["residents"].Items.Single()["node_entered_scenario_tick"].Kind == "null", "Departure clears node-entry time after the fact is captured");
                Assert.IsNotNull(clock.Step(), clock.Fault); Assert.AreEqual(1, observed.Count, "Subsequent travel/vehicle occupancy does not extend the old dwell");
            }
        }
        private static void NodeTerminalTransitionsCompleteActualDwells()
        {
            var backup = Build(count: 1);
            for (int i = 0; i < 19; i++) { backup.Step(exposure: true); Assert.AreEqual(0, backup.System.ReadCompletedNodeDwells(backup.Tick).Count); }
            var state = backup.Step(exposure: true); Assert.AreEqual("awaiting_backup", Person(state)["state"].Text);
            var dwell = backup.System.ReadCompletedNodeDwells(backup.Tick).Single(); Assert.AreEqual("p", dwell.NodeId); Assert.AreEqual(20, dwell.ScenarioTicks, "Exposure backup retains the final waiting interval");
            backup.Step(exposure: true); Assert.AreEqual(0, backup.System.ReadCompletedNodeDwells(backup.Tick).Count, "An already terminal resident cannot complete its dwell again");
            var sameNode = Build(count: 1, held: true, spawnNode: "e");
            sameNode.Step(); sameNode.Step(); Assert.AreEqual("waiting", Person(sameNode.System.ReadState())["state"].Text);
            sameNode.System.ReleaseGroup("group"); sameNode.Assign("group", "group", 1, Stop("e", "evacuate")); state = sameNode.Step();
            Assert.AreEqual("evacuated", Person(state)["state"].Text); dwell = sameNode.System.ReadCompletedNodeDwells(sameNode.Tick).Single(); Assert.AreEqual("e", dwell.NodeId); Assert.AreEqual(3, dwell.ScenarioTicks, "Direct same-node evacuation includes its final scenario interval"); Assert.AreEqual(0, sameNode.System.ReadBoundaryFacts(sameNode.Tick).Count);
            sameNode.Step(); Assert.AreEqual(0, sameNode.System.ReadCompletedNodeDwells(sameNode.Tick).Count);
            var arrival = Build(count: 1, aboard: true, travel: 1); arrival.Assign("vehicle", "bus", 1, Stop("e", "evacuate"));
            for (int i = 0; i < 11; i++) { arrival.Step(); Assert.AreEqual(0, arrival.System.ReadCompletedNodeDwells(arrival.Tick).Count, "Already aboard residents have no source node dwell"); }
            state = arrival.Step(); Assert.AreEqual(1, arrival.System.EvacuatedCount); dwell = arrival.System.ReadCompletedNodeDwells(arrival.Tick).Single(); Assert.AreEqual("e", dwell.NodeId); Assert.AreEqual(0, dwell.ScenarioTicks, "Completed unloading followed by evacuation has a zero-length node dwell");
            arrival.Step(); Assert.AreEqual(0, arrival.System.ReadCompletedNodeDwells(arrival.Tick).Count);
        }
        private static void ClockCompositionAndGuidedEpoch()
        {
            var payload = RenameResidents(J(File.ReadAllText(Path.Combine(Root, "content/runtime/fixtures/compiler-valid.json"))));
            payload = payload.With("missions", JsonValue.Array(payload["missions"].Items.Select(m => m.With("id", JsonValue.String("W1-M1")).With("tutorial_policy", JsonValue.String("guided_first_play_then_unguided")))));
            var layouts = payload["layouts"].Items.Select(l =>
            {
                string person = l["spawns"].Items.Single()["resident_id"].Text;
                l = l.With("spawns", JsonValue.Array(l["spawns"].Items.Select(p => p.With("fixed_vehicle_id", JsonValue.String("bus")))))
                    .With("fixed_vehicles", JsonValue.Array(new[] { O("vehicle_id", "bus", "kind", "fixed_bus", "node_id", "start", "seats", 4, "movement_slots", 2, "initial_route_id", "depot", "initial_occupant_ids", JsonValue.Array(new[] { JsonValue.String(person) })) }))
                    .With("routes", JsonValue.Array(l["routes"].Items.Concat(new[] { O("id", "depot", "from_node_id", "start", "to_node_id", "refuge", "required_waypoint_ids", J("[\"assembly\"]"), "traversal_class", "ground", "mobility_class", "standard") })));
                return l.With("graph_hash", JsonValue.String(ContentCompilation.ComputeGraphHash(l)));
            });
            var content = ContentCompilation.Compile(payload.With("layouts", JsonValue.Array(layouts)));
            var transport = new TransportSystem(content, "fixture-v1", "fixture-wide");
            var peer = new SyntheticPeer(); var trace = peer.Trace;
            var systems = new TransportOperationSystems(peer, transport, (authority, owner, c) => ((SyntheticPeer)owner).Frame(authority, c), (authority, owner) => authority.GroupAtNode("group", "refuge") && ((SyntheticPeer)owner).Acknowledged);
            Assert.Throws<ArgumentNullException>(() => new TransportOperationSystems(null, transport, (a, b, c) => null, (a, b) => false));
            var identity = O("run_id", "synthetic", "source_build_id", "synthetic-transport-integration", "mission_version_id", "fixture-v1", "rules_version", "resq-rules-1", "content_hash", content.ContentHash, "layout_version_id", "fixture-wide", "layout_signature", content.LayoutSignature("fixture-wide"), "rule_profile_id", "standard", "input_adapter_id", "reference_replay", "guidance_policy", "guided_first_play", "challenge_id", "base", "modifiers_hash", J("[]").Sha256(), "seed", 1, "assisted", false);
            var clock = new OperationClock(content, "fixture-v1", "fixture-wide", identity, systems);
            clock.Apply(GameCommand.Read(O("schema_version", 1, "command_id", "resume", "run_id", "synthetic", "sequence", 1, "requested_tick", 0, "actor_input", "reference_replay", "kind", "resume", "payload", O()))); clock.CompleteResumeCountdown();
            for (int i = 1; i <= 40; i++)
            {
                var step = clock.Step(); Assert.IsNotNull(step, clock.Fault); var state = clock.ReadSnapshot().Value; Assert.AreEqual(0, state["scenario_tick"].Integer);
                if (i == 2) Assert.AreEqual("waiting_route", state["vehicles"].Items.Single()["status"].Text);
                if (i == 13) { Assert.AreEqual("assembly", state["vehicles"].Items.Single()["location"]["node_id"].Text); Assert.AreEqual(1, state["routes"].Items.Single()["next_stop_index"].Integer); Assert.AreEqual(0, state["routes"].Items.Single()["submitted_sequence"].Integer); Assert.AreEqual("assemble", state["routes"].Items.Single()["stops"].Items[0]["action"].Text); }
                if (i == 34) { Assert.AreEqual("evacuated", state["residents"].Items.Single()["state"].Text); Assert.IsFalse(state["scenario_enabled"].Boolean, "Arrival alone does not acknowledge the river warning"); }
                Assert.Less(trace.IndexOf("safety"), trace.IndexOf("movement")); Assert.Less(trace.IndexOf("movement"), trace.IndexOf("objectives"));
            }
            Assert.IsTrue(clock.ReadSnapshot().Value["scenario_enabled"].Boolean); Assert.AreEqual(1, clock.ReadSnapshot().Value["milestones"].Items.Count(x => x["id"].Text == "operation_started"));
            Assert.IsNotNull(clock.Step(), clock.Fault); Assert.AreEqual(1, clock.ReadSnapshot().Value["scenario_tick"].Integer);
            Assert.AreEqual(1, clock.ReadSnapshot().Value["recent_events"].Items.Count(x => x["kind"].Text == "HazardWarning"), "Tick-zero warnings remain once-only during guided O steps");
            Assert.AreEqual(1, clock.ReadSnapshot().Value["residents"].Items.Count); Assert.AreEqual(0, clock.ReadSnapshot().Value["vehicles"].Items.Single()["occupant_ids"].Items.Count);
        }
        // Explicit test-only peer. It models only safety grants and one warning
        // acknowledgement for integration ordering, and cannot qualify a mission.
        private sealed class SyntheticPeer : IOperationSystems, ITransportDomainPeer
        {
            private TransportSystem authority;
            private long tick;
            private readonly Action<TransportSystem, OperationContext> observer;
            private readonly bool routesOpenInitially;
            public bool Acknowledged { get; private set; }
            public readonly List<string> Trace;
            public SyntheticPeer(List<string> trace = null, Action<TransportSystem, OperationContext> observer = null, bool routesOpenInitially = false) { Trace = trace ?? new List<string>(); this.observer = observer; this.routesOpenInitially = routesOpenInitially; }
            public void BindTransport(TransportSystem transport) { authority = transport ?? throw new ArgumentNullException(nameof(transport)); }
            public IOperationSystems Fork() => new SyntheticPeer(Trace, observer, routesOpenInitially) { tick = tick, Acknowledged = Acknowledged };
            public TransportFrame Frame(TransportSystem transport, OperationContext c)
            {
                Assert.AreSame(authority, transport, "Forked peer is bound to the forked transport authority");
                return new TransportFrame(c.OperationTick, new[] { "start", "assembly", "junction", "refuge" }.Select(x => new TransportNodeSample(x, true, true, true)), new[] { "a", "b", "c", "d" }.Select(x => new TransportEdgeSample(x, routesOpenInitially || c.OperationTick >= 3, true, 1000, null)), authority.ReadState()["residents"].Items.ToDictionary(x => x["id"].Text, x => false, StringComparer.Ordinal));
            }
            public ValidationResult Validate(GameCommand command, OperationContext c) => new ValidationResult(command.Kind == "end_operation" ? "OK" : "UNKNOWN_TARGET");
            public void Apply(GameCommand command, OperationContext c) { throw new InvalidOperationException("Synthetic peer has no simulation commands"); }
            public void EvaluateTides(OperationContext c) { }
            public void PublishWarning(EventDef definition, OperationContext c) { c.Events.Emit("HazardWarning", "wet", null, definition.id, O("hazard", "water", "region_id", "wet", "effect_scenario_tick", definition.at_scenario_tick)); }
            public void ApplyScheduled(ResolvedEventOperation op, OperationContext c) { if (op.Kind != "set_source_rate") throw new InvalidOperationException("Unexpected synthetic event"); }
            public void ResolvePowerAndReservations(OperationContext c) { Trace.Clear(); Trace.Add("power"); tick = c.OperationTick; }
            public void SimulateHazardInterval(OperationContext c) { Trace.Add("hazards"); }
            public void RecomputeSafetyAndDamage(OperationContext c) { Trace.Add("safety"); }
            public void ResolveMovementAndTransfers(OperationContext c) { Trace.Add("movement"); Assert.AreEqual(1, authority.Population); }
            public void DeriveMetricsAndObjectives(OperationContext c) { Trace.Add("objectives"); observer?.Invoke(authority, c); if (tick == 40) Acknowledged = true; }
            public void EmitMilestone(string id, OperationContext c) { }
            public void SampleInitialObjectives(OperationContext c) { Assert.AreEqual(1, authority.Population); }
            public bool GuidedEpochReady => Acknowledged;
            public bool RequiredConditionsHold => authority.EvacuatedCount == 1;
            public OutcomeResult EvaluateOutcome(OperationContext c) => new OutcomeResult(authority.EvacuatedCount == 1, authority.EvacuatedCount == 1 ? 1 : 0, 0, authority.EvacuatedCount, 0);
            public void ValidateBoundary(OperationContext c) { authority.ValidateBoundary(); }
            public JsonValue ReadDomainState()
            {
                var state = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                foreach (string field in new[] { "water", "rate_remainders", "equipment", "vehicles", "residents", "transits", "transfers", "reservations", "routes", "queues", "fire", "supports", "runouts", "tides", "lava", "stores", "producers", "milestones", "metrics", "objective_monitors", "recent_events" }) state.Add(field, J("[]"));
                state["guidance_state"] = O("policy", "guided_first_play", "step_id", "river-warning", "completed_step_ids", J("[]"), "acknowledged_warning", Acknowledged);
                state["supplies_remaining"] = JsonValue.Number(1); state["selected_stock"] = J("[{\"equipment_id\":\"beacon\",\"count\":1}]"); state["deployments"] = J("[{\"equipment_id\":\"beacon\",\"count\":0}]");
                state["power"] = J("{\"node_enabled\":[],\"link_enabled\":[],\"consumer_demands\":[],\"priorities\":[],\"grants\":[],\"generator_trip_ids\":[]}");
                state["event_state"] = J("{\"next_static_event_index\":0,\"fired_event_ids\":[],\"revealed_event_ids\":[],\"warned_event_ids\":[],\"dynamic_events\":[],\"administrative_edge_enabled\":[],\"edge_current_multipliers\":[],\"gate_open\":[],\"group_held\":[],\"wind_region_codes\":[],\"cooler_socket_enabled\":[],\"shelter_region_enabled\":[],\"resource_warning_thresholds\":[]}");
                state["water_ledger"] = J("{\"initial_total_mm3\":0,\"direct_sources_mm3\":0,\"sea_in_mm3\":0,\"drains_mm3\":0,\"sea_out_mm3\":0,\"sprayed_mm3\":0}");
                return JsonValue.Object(state);
            }
        }
    }
}
