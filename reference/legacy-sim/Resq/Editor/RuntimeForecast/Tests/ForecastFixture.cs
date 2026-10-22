using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Resq.Content;
using Resq.Core;
using Resq.Persistence;
using Resq.Simulation;
using Resq.Simulation.Objectives;
using Resq.Simulation.Rescue;
using Resq.Simulation.Tools;
using UnityEngine;

namespace Resq.Tests.Forecast
{
    // Explicit synthetic C08 input. This composes the actual C02-C05 owners;
    // it is neither authored campaign content nor a substitute hazard backend.
    public static class ForecastFixture
    {
        public static Action<long, string, JsonValue> ValidationObserved;
        private const string RunId = "forecast-fixture";
        private const string SourceBuild = "synthetic-forecast";
        private const string MissionId = "fixture-v1";
        private const string LayoutId = "fixture-wide";

        public sealed class Rig
        {
            public CompiledContent Content { get; }
            public SimulationFactory Factory { get; }
            public OperationClock Clock { get; set; }
            internal Rig(CompiledContent content, bool relaxed, bool guided)
            {
                Content = content;
                Factory = new SimulationFactory(content, SourceBuild, RestoreDomain, RestoreDomainWork);
                var domain = new Domain(content, MissionId, LayoutId, guided);
                var identity = O("run_id", RunId, "source_build_id", SourceBuild,
                    "mission_version_id", MissionId, "rules_version", "resq-rules-1",
                    "content_hash", content.ContentHash, "layout_version_id", LayoutId,
                    "layout_signature", content.LayoutSignature(LayoutId), "rule_profile_id", relaxed ? "relaxed" : "standard",
                    "input_adapter_id", "reference_replay", "guidance_policy", guided ? "guided_first_play" : "unguided", "challenge_id", "base",
                    "modifiers_hash", A().Sha256(), "seed", 7, "assisted", relaxed);
                Clock = new OperationClock(content, MissionId, LayoutId, identity,
                    new ObjectiveOperationSystems(domain, new ObjectiveSystem(domain.Mission, domain.Layout)));
            }
            public GameCommand Command(string kind, JsonValue payload, long? requestedTick = null)
            {
                long sequence = Clock.NextSequence;
                bool application = kind == "pause" || kind == "resume" || kind == "advance_ticks" ||
                    kind == "enter_placement" || kind == "confirm_placement" || kind == "exit_to_hub";
                return GameCommand.Read(O("schema_version", 1, "command_id", "cmd:" + sequence.ToString(CultureInfo.InvariantCulture),
                    "run_id", RunId, "sequence", sequence, "requested_tick", requestedTick ?? (Clock.OperationTick + (application ? 0 : 1)),
                    "actor_input", "reference_replay", "kind", kind, "payload", payload));
            }
            public void Start()
            {
                var started = Clock.Apply(Command("resume", O()));
                if (!started.Receipt.Accepted) throw new InvalidOperationException("Fixture start rejected");
                Clock.CompleteResumeCountdown();
            }
            public void StartVehicleRoute()
            {
                var resumed = Clock.Apply(Command("resume", O()));
                if (!resumed.Receipt.Accepted) throw new InvalidOperationException("Fixture resume rejected: " + resumed.Receipt.ReasonCode);
                Clock.CompleteResumeCountdown();
                var route = Command("assign_route", O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] {
                    O("node_id", "start", "action", "pickup", "group_id", "group"),
                    O("node_id", "refuge", "action", "evacuate", "group_id", null) })));
                var queued = Clock.Admit(route);
                if (queued.Status != "queued") throw new InvalidOperationException("Fixture route rejected: " + queued.ReasonCode);
            }
        }

        public static Rig Build(bool relaxed = false, bool allPublic = true, bool guided = false,
            long duration = 700, Func<JsonValue, JsonValue> configure = null)
        {
            string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            var source = StrictJson.Parse(File.ReadAllBytes(Path.Combine(root, "content/runtime/fixtures/compiler-valid.json")));
            var originalMission = source["missions"].Items.Single();
            string fixtureMissionId = guided ? "W1-M1" : "fixture";
            var mission = originalMission.With("duration_scenario_ticks", JsonValue.Number(duration))
                .With("id", JsonValue.String(fixtureMissionId))
                .With("groups", JsonValue.Array(originalMission["groups"].Items.Select(group =>
                    group.With("resident_ids", JsonValue.Array(Enumerable.Range(1, group["resident_ids"].Items.Count)
                        .Select(index => JsonValue.String(fixtureMissionId + ":resident:" + group["id"].Text + ":" + index.ToString("00", CultureInfo.InvariantCulture))))))))
                .With("tutorial_policy", JsonValue.String(guided ? "guided_first_play_then_unguided" : "none"))
                .With("rule_profile_ids", A("standard", "relaxed"))
                .With("events", JsonValue.Array(originalMission["events"].Items.Select(e =>
                    e.With("at_scenario_tick", JsonValue.Number(120)).With("warning_at_scenario_tick", JsonValue.Number(20))
                    .With("known_at_start", JsonValue.Bool(allPublic)))))
                .With("milestones", JsonValue.Array(new[] {
                    O("id", "group.boarded", "kind", "group_boarded", "target", "group"),
                    O("id", "final.started", "kind", "final_unload_started", "target", "all_residents"),
                    O("id", "all.safe", "kind", "all_evacuated", "target", "all_residents") }));
            var standard = source["rule_profiles"].Items.Single();
            var relaxedProfile = standard.With("id", JsonValue.String("relaxed")).With("scenario_numerator", JsonValue.Number(1));
            var residentIdentity = originalMission["groups"].Items.SelectMany(group =>
                group["resident_ids"].Items.Select((resident, index) => new {
                    Before = resident.Text,
                    After = fixtureMissionId + ":resident:" + group["id"].Text + ":" + (index + 1).ToString("00", CultureInfo.InvariantCulture)
                })).ToDictionary(row => row.Before, row => row.After, StringComparer.Ordinal);
            var layouts = source["layouts"].Items.Select(layout => layout
                .With("spawns", JsonValue.Array(layout["spawns"].Items.Select(spawn =>
                    spawn.With("resident_id", JsonValue.String(residentIdentity[spawn["resident_id"].Text])))))
                .With("nav_nodes", JsonValue.Array(layout["nav_nodes"].Items.Select(n => n["id"].Text == "start" ? n.With("kind", JsonValue.String("dock")) : n)))
                .With("fixed_vehicles", JsonValue.Array(new[] { O("vehicle_id", "bus", "kind", "fixed_bus", "node_id", "start", "seats", 4,
                    "movement_slots", 2, "initial_route_id", null, "initial_occupant_ids", A()) })));
            var payload = source.With("missions", JsonValue.Array(new[] { mission }))
                .With("rule_profiles", JsonValue.Array(new[] { standard, relaxedProfile })).With("layouts", JsonValue.Array(layouts));
            if (configure != null) payload = configure(payload);
            payload = payload.With("layouts", JsonValue.Array(payload["layouts"].Items.Select(layout =>
                layout.With("graph_hash", JsonValue.String(ContentCompilation.ComputeGraphHash(layout))))));
            return new Rig(ContentCompilation.Compile(payload), relaxed, guided);
        }
        public const int CapacityGraphNodes = 4096;
        public const int CapacityGraphLinks = 16384;
        public const int CapacityResidents = 256;
        public const int CapacityVehicles = 32;
        public const int CapacityPumpStock = 511;

        // Representative simultaneous caps, not a global maximum: finite water,
        // ground transport and pumps use their real owners. Other hazard families
        // and power links are absent. Compilation and preparation are setup work,
        // never part of a forecast timing sample. See maximum-fixture/review.md.
        public static Rig BuildCapacityHeavy(bool relaxed = false, bool allPublic = true) =>
            Build(relaxed, allPublic, false, 3000, payload => WithCapacityHeavyContent(payload, allPublic));

        // This intentionally executes actual commands and owner steps. Call once
        // on a fresh capacity rig, outside the measured forecast calls. At most32
        // commands are pending together. Leave one stock item by requesting510 if
        // a place_equipment preview is desired;511 requires a different preview.
        public static void PrepareCapacityHeavy(Rig rig, int deployedPumps = CapacityPumpStock, bool assignVehicleRoutes = true, Action<string, int> setupProgress = null)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (deployedPumps < 0 || deployedPumps > CapacityPumpStock) throw new ArgumentOutOfRangeException(nameof(deployedPumps));
            var mission = rig.Content.Envelope.payload.missions.Single();
            var layout = rig.Content.Envelope.payload.layouts.Single(l => l.id == LayoutId);
            if (mission.total_residents != CapacityResidents || layout.water_nodes.Count != CapacityGraphNodes ||
                layout.nav_edges.Count != CapacityGraphLinks || layout.sockets.Count != CapacityPumpStock + 1 ||
                mission.maximum_stock.Count != 1 || mission.maximum_stock[0].equipment_id != "pump" ||
                mission.maximum_stock[0].count != CapacityPumpStock || rig.Clock.OperationTick != 0 || rig.Clock.Phase != "planning")
                throw new InvalidOperationException("Fresh capacity fixture required");
            rig.Start();
            for (int first = 1; first <= deployedPumps; first += 32)
            {
                var commands = new List<JsonValue>();
                for (int index = first; index <= Math.Min(deployedPumps, first + 31); index++)
                    commands.Add(O("equipment_id", "pump", "socket_id", "pump:" + index.ToString("00", CultureInfo.InvariantCulture), "orientation_step", 0));
                ExecuteCapacityBatch(rig, "place_equipment", commands);
                setupProgress?.Invoke("pumps_deployed", Math.Min(deployedPumps, first + 31));
            }
            if (assignVehicleRoutes)
            {
                // Initial occupants are real resident/vehicle reciprocal bindings;
                // the remaining four residents in every group remain held ashore.
                var commands = new List<JsonValue>();
                for (int index = 0; index < CapacityVehicles; index++)
                    commands.Add(O("subject_kind", "vehicle", "subject_id", CapacityId("bus", index),
                        "stops", JsonValue.Array(new[] { O("node_id", CapacityId("nav", CapacityGraphNodes - 1), "action", "evacuate", "group_id", null) })));
                ExecuteCapacityBatch(rig, "assign_route", commands);
                setupProgress?.Invoke("vehicle_routes_assigned", CapacityVehicles);
            }
            var actual = rig.Clock.ReadSnapshot().Value;
            if (actual["equipment"].Items.Count != deployedPumps || actual["vehicles"].Items.Count != CapacityVehicles ||
                actual["residents"].Items.Count != CapacityResidents || actual["water"].Items.Count != CapacityGraphNodes)
                throw new InvalidOperationException("Actual capacity preparation state differs from requested population");
        }
        private static void ExecuteCapacityBatch(Rig rig, string kind, IEnumerable<JsonValue> payloads)
        {
            var admitted = new List<string>();
            foreach (var payload in payloads)
            {
                var command = rig.Command(kind, payload, rig.Clock.OperationTick + 1);
                var receipt = rig.Clock.Admit(command);
                if (receipt.Status != "queued") throw new InvalidOperationException("Capacity command admission rejected: " + receipt.ReasonCode);
                admitted.Add(command.CommandId);
            }
            if (rig.Clock.Step() == null) throw new InvalidOperationException(rig.Clock.Fault ?? "Capacity preparation did not advance");
            var receipts = rig.Clock.Receipts.ToDictionary(r => r.CommandId, StringComparer.Ordinal);
            foreach (string id in admitted)
                if (!receipts[id].Result.Accepted) throw new InvalidOperationException("Capacity command execution rejected: " + receipts[id].Result.ReasonCode);
        }
        private static string CapacityId(string family, int index) => family + ":" + index.ToString("0000", CultureInfo.InvariantCulture);
        private static string CapacityWaterRegion(int index) => index >= 1024 && index < 1536 ? "platform" : CapacityId("region", index / 64);
        private static JsonValue CapacityObjective(string id, string tier, string kind, JsonValue parameters) =>
            O("id", id, "tier", tier, "kind", kind, "params", parameters, "temporal", O("kind", "at_end"));

        private static JsonValue WithCapacityHeavyContent(JsonValue payload, bool allPublic)
        {
            var sourceMission = payload["missions"].Items.Single();
            var sourceLayout = payload["layouts"].Items[0];
            var nodeTemplate = sourceLayout["nav_nodes"].Items[0];
            var edgeTemplate = sourceLayout["nav_edges"].Items[0];
            var socketTemplate = sourceLayout["sockets"].Items[0];
            var pump = payload["equipment"].Items.Single().With("id", JsonValue.String("pump"))
                .With("supply_cost", JsonValue.Number(3)).With("socket_kinds", A("pump_link"))
                .With("power_demand", JsonValue.Number(1)).With("parameters", O("rate_mm3_per_second", 20000000, "max_lift_mm", 5000));
            var groups = new List<JsonValue>(); var spawns = new List<JsonValue>(); var vehicles = new List<JsonValue>();
            var milestones = new List<JsonValue>(); var objectives = new List<JsonValue>();
            objectives.Add(CapacityObjective("rescue_minimum", "required", "rescue_count", O("minimum", CapacityResidents)));
            objectives.Add(CapacityObjective("critical", "critical", "route_available", O("route_id", "exit", "traversal_class", "ground")));
            // Root/child/grandchild/leaf gives four levels and exactly16 leaves.
            var branches = new List<JsonValue>();
            for (int a = 0; a < 2; a++)
            {
                var children = new List<JsonValue>();
                for (int b = 0; b < 2; b++)
                {
                    var leaves = new List<JsonValue>();
                    for (int c = 0; c < 4; c++)
                        leaves.Add(O("kind", "exposure_below", "params", O("region_id", CapacityId("region", a * 8 + b * 4 + c), "hazard", "water", "threshold", 100)));
                    children.Add(O("kind", "all", "params", O("conditions", JsonValue.Array(leaves))));
                }
                branches.Add(O("kind", "all", "params", O("conditions", JsonValue.Array(children))));
            }
            objectives.Add(CapacityObjective("mastery", "mastery", "all", O("conditions", JsonValue.Array(branches))));
            for (int group = 0; group < CapacityVehicles; group++)
            {
                string groupId = CapacityId("group", group), nodeId = CapacityId("nav", group), busId = CapacityId("bus", group);
                var residents = Enumerable.Range(1, 8).Select(index => sourceMission["id"].Text + ":resident:" + groupId + ":" + index.ToString("00", CultureInfo.InvariantCulture)).ToArray();
                groups.Add(O("id", groupId, "resident_ids", A(residents), "start_node_id", nodeId, "assembly_node_id", nodeId, "initially_held", true));
                for (int index = 0; index < residents.Length; index++)
                    spawns.Add(O("resident_id", residents[index], "group_id", groupId, "mobility_class", index == 0 ? "step_free" : "standard",
                        "start_node_id", nodeId, "fixed_vehicle_id", index < 4 ? busId : null));
                vehicles.Add(O("vehicle_id", busId, "kind", "fixed_bus", "node_id", nodeId, "seats", 4, "movement_slots", 2,
                    "initial_route_id", null, "initial_occupant_ids", A(residents.Take(4).ToArray())));
                milestones.Add(O("id", CapacityId("boarded", group), "kind", "group_boarded", "target", groupId));
                objectives.Add(CapacityObjective(CapacityId("group_goal", group), "mastery", "group_evacuated", O("group_id", groupId, "minimum", 8)));
                objectives.Add(CapacityObjective(CapacityId("dock_goal", group), "critical", "infrastructure_operational", O("infrastructure_id", CapacityId("dock", group), "operational", true)));
            }
            for (int index = 0; objectives.Count < 128; index++)
                objectives.Add(CapacityObjective(CapacityId("occupancy_goal", index), "mastery", "counter_at_least", O("metric", "peak_node_occupancy", "node_id", CapacityId("nav", index), "minimum", 1)));
            milestones.Add(O("id", "final.started", "kind", "final_unload_started", "target", "all_residents"));
            milestones.Add(O("id", "all.safe", "kind", "all_evacuated", "target", "all_residents"));
            var events = Enumerable.Range(0, 2048).Select(index => O("id", CapacityId("surge", index), "at_scenario_tick", 120 + index,
                "warning_at_scenario_tick", 20 + index, "known_at_start", allPublic,
                "operations", JsonValue.Array(new[] { O("kind", "set_source_rate", "target_role", "source", "value", 100000 + (index % 2) * 100000) })));
            var mission = sourceMission.With("total_residents", JsonValue.Number(CapacityResidents)).With("minimum_rescues", JsonValue.Number(CapacityResidents))
                .With("supply_budget", JsonValue.Number(CapacityPumpStock * 3)).With("permitted_equipment_ids", A("pump"))
                .With("default_stock", JsonValue.Array(new[] { O("equipment_id", "pump", "count", CapacityPumpStock) }))
                .With("maximum_stock", JsonValue.Array(new[] { O("equipment_id", "pump", "count", CapacityPumpStock) }))
                .With("groups", JsonValue.Array(groups)).With("objectives", JsonValue.Array(objectives))
                .With("milestones", JsonValue.Array(milestones)).With("events", JsonValue.Array(events));

            var nodes = new List<JsonValue>(); var edges = new List<JsonValue>(); var water = new List<JsonValue>(); var hydraulic = new List<JsonValue>();
            var sockets = new List<JsonValue>(); var power = new List<JsonValue>(); var infrastructure = new List<JsonValue>();
            int[] offsets = { 1, 2, 4, 8 };
            for (int index = 0; index < CapacityGraphNodes; index++)
            {
                bool refuge = index == CapacityGraphNodes - 1, platform = CapacityWaterRegion(index) == "platform";
                string nodeId = CapacityId("nav", index), waterId = CapacityId("water", index);
                nodes.Add(nodeTemplate.With("id", JsonValue.String(nodeId)).With("kind", JsonValue.String(refuge ? "evacuation" : index < CapacityVehicles ? "dock" : "junction"))
                    .With("position_mm", JsonValue.Array(new[] { JsonValue.Number((index % 32) * 1000), JsonValue.Number(0), JsonValue.Number((index / 32 % 24) * 1000) }))
                    .With("resident_capacity", JsonValue.Number(refuge ? CapacityResidents : 8)).With("vehicle_slots", JsonValue.Number(refuge ? CapacityVehicles : 1))
                    .With("dock_berths", JsonValue.Number(refuge ? CapacityVehicles : 1)).With("hazard_region_ids", A(CapacityWaterRegion(index)))
                    .With("power_consumer_id", index < CapacityVehicles ? JsonValue.String(CapacityId("dock", index)) : JsonValue.Null()));
                water.Add(O("id", waterId, "kind", "terrain", "region_id", CapacityWaterRegion(index), "elevation_mm", platform ? 1200 : 0,
                    "area_mm2", 1000000, "initial_volume_mm3", platform ? 0 : (index % 5 + 1) * 1000000));
                foreach (int offset in offsets)
                {
                    int target = (index + offset) % CapacityGraphNodes;
                    edges.Add(edgeTemplate.With("id", JsonValue.String(CapacityId("edge" + offset.ToString(CultureInfo.InvariantCulture), index)))
                        .With("from_node_id", JsonValue.String(nodeId)).With("to_node_id", JsonValue.String(CapacityId("nav", target)))
                        .With("movement_capacity", JsonValue.Number(4)).With("hazard_region_ids", A(CapacityWaterRegion(index))));
                    hydraulic.Add(O("id", CapacityId("flow" + offset.ToString(CultureInfo.InvariantCulture), index), "from_id", waterId,
                        "to_id", CapacityId("water", target), "from_kind", "water_node", "to_kind", "water_node", "direction", "both",
                        "sill_mm", 0, "conductance_mm2_per_second", 1000, "maximum_transfer_mm3", 1000000,
                        "gate_id", null, "initial_gate_open", true, "barrier_socket_id", null));
                }
            }
            for (int index = 0; index <= CapacityPumpStock; index++)
            {
                sockets.Add(socketTemplate.With("id", JsonValue.String("pump:" + (index + 1).ToString("00", CultureInfo.InvariantCulture)))
                    .With("kind", JsonValue.String("pump_link")).With("node_id", JsonValue.String(CapacityId("nav", index + 1024)))
                    .With("permitted_equipment_ids", A("pump")).With("relocation_zone", JsonValue.String("platform"))
                    .With("footprint_cell_ids", A(CapacityId("water", index + 1024))).With("power_node_id", JsonValue.String(CapacityId("power", index)))
                    .With("intake_node_id", JsonValue.String(CapacityId("water", index))).With("outlet_node_id", JsonValue.String(CapacityId("water", index + 512))));
                power.Add(O("id", CapacityId("power", index), "platform_region_id", "platform", "initial_enabled", true,
                    "fixed_supply", 2, "disconnect_depth_mm", 50, "reconnect_depth_mm", 40));
                if (index < CapacityVehicles)
                    infrastructure.Add(O("id", CapacityId("dock", index), "kind", "dock", "power_node_id", CapacityId("power", index), "initial_demand", 1,
                        "initial_priority", 1, "required_nav_node_ids", A(CapacityId("nav", index)), "required_support_ids", A(), "required_equipment_socket_ids", A(),
                        "hazard_region_ids", A(CapacityWaterRegion(index)), "maximum_depth_mm", 100, "maximum_heat_points", 600));
            }
            var layouts = payload["layouts"].Items.Select(layout => layout.With("nav_nodes", JsonValue.Array(nodes)).With("nav_edges", JsonValue.Array(edges))
                .With("water_nodes", JsonValue.Array(water)).With("hydraulic_links", JsonValue.Array(hydraulic))
                .With("sockets", JsonValue.Array(sockets)).With("power_nodes", JsonValue.Array(power)).With("infrastructure", JsonValue.Array(infrastructure))
                .With("spawns", JsonValue.Array(spawns)).With("fixed_vehicles", JsonValue.Array(vehicles))
                .With("routes", JsonValue.Array(new[] { O("id", "exit", "from_node_id", CapacityId("nav", 0), "to_node_id", CapacityId("nav", CapacityGraphNodes - 1),
                    "traversal_class", "ground", "mobility_class", "step_free", "required_waypoint_ids", A()) }))
                .With("sources", JsonValue.Array(new[] { O("id", "inflow", "water_node_id", CapacityId("water", 0), "initial_rate_mm3_per_second", 0) })));
            return payload.With("equipment", JsonValue.Array(new[] { pump })).With("missions", JsonValue.Array(new[] { mission })).With("layouts", JsonValue.Array(layouts));
        }

        public static JsonValue WithPump(JsonValue payload, long fixedSupply)
        {
            var beacon = payload["equipment"].Items.Single();
            var pump = beacon.With("id", JsonValue.String("pump")).With("supply_cost", JsonValue.Number(3))
                .With("socket_kinds", A("pump_link")).With("power_demand", JsonValue.Number(1))
                .With("relocation_policy", JsonValue.String("idle_same_zone"))
                .With("parameters", O("rate_mm3_per_second", 20000000, "max_lift_mm", 5000));
            var mission = payload["missions"].Items.Single().With("supply_budget", JsonValue.Number(10))
                .With("permitted_equipment_ids", A("beacon", "pump"))
                .With("default_stock", JsonValue.Array(new[] { O("equipment_id", "beacon", "count", 1), O("equipment_id", "pump", "count", 1) }))
                .With("maximum_stock", JsonValue.Array(new[] { O("equipment_id", "beacon", "count", 1), O("equipment_id", "pump", "count", 1) }));
            var layouts = payload["layouts"].Items.Select(layout => {
                var socket = layout["sockets"].Items[0].With("id", JsonValue.String("pump:01"))
                    .With("kind", JsonValue.String("pump_link")).With("permitted_equipment_ids", A("pump"))
                    .With("power_node_id", JsonValue.String("power"))
                    .With("intake_node_id", JsonValue.String("water")).With("outlet_node_id", JsonValue.String("dry2"));
                var alternative = socket.With("id", JsonValue.String("pump:02")).With("footprint_cell_ids", A("dry2")).With("outlet_node_id", JsonValue.String("dry1"));
                return layout.With("sockets", JsonValue.Array(layout["sockets"].Items.Concat(new[] { socket, alternative })))
                    .With("power_nodes", JsonValue.Array(new[] { O("id", "power", "platform_region_id", "dry", "initial_enabled", true,
                        "fixed_supply", fixedSupply, "disconnect_depth_mm", 50, "reconnect_depth_mm", 40) }));
            });
            return payload.With("equipment", JsonValue.Array(new[] { beacon, pump })).With("missions", JsonValue.Array(new[] { mission })).With("layouts", JsonValue.Array(layouts));
        }
        public static JsonValue WithPoweredDock(JsonValue payload)
        {
            // Actual water rises under the fixed supply at S=120. The dock
            // itself stays physically safe; only the real power grant is lost.
            var mission = payload["missions"].Items.Single();
            mission = mission.With("events", JsonValue.Array(mission["events"].Items.Select(e =>
                e.With("operations", JsonValue.Array(new[] { O("kind", "set_source_rate", "target_role", "source", "value", 100000000) })))));
            var layouts = payload["layouts"].Items.Select(layout => layout
                .With("water_nodes", JsonValue.Array(layout["water_nodes"].Items.Select(w => w["id"].Text == "water" ? w.With("area_mm2", JsonValue.Number(10000)) : w)))
                .With("nav_nodes", JsonValue.Array(layout["nav_nodes"].Items.Select(n => n["id"].Text == "start" ? n.With("power_consumer_id", JsonValue.String("dock_power")) : n)))
                .With("power_nodes", JsonValue.Array(new[] { O("id", "power", "platform_region_id", "wet", "initial_enabled", true,
                    "fixed_supply", 1, "disconnect_depth_mm", 50, "reconnect_depth_mm", 40) }))
                .With("infrastructure", JsonValue.Array(new[] { O("id", "dock_power", "kind", "dock", "power_node_id", "power", "initial_demand", 1,
                    "initial_priority", 1, "required_nav_node_ids", A("start"), "required_support_ids", A(), "required_equipment_socket_ids", A(),
                    "hazard_region_ids", A("dry"), "maximum_depth_mm", 100, "maximum_heat_points", 600) })));
            return payload.With("missions", JsonValue.Array(new[] { mission })).With("layouts", JsonValue.Array(layouts));
        }

        public static JsonValue O(params object[] pairs)
        {
            if (pairs.Length % 2 != 0) throw new ArgumentException("Object key/value pairs required");
            var result = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                object value = pairs[i + 1];
                result.Add((string)pairs[i], value == null ? JsonValue.Null() : value is JsonValue json ? json :
                    value is string text ? JsonValue.String(text) : value is bool boolean ? JsonValue.Bool(boolean) : JsonValue.Number(Convert.ToInt64(value, CultureInfo.InvariantCulture)));
            }
            return JsonValue.Object(result);
        }
        public static JsonValue A(params string[] values) => JsonValue.Array(values.Select(JsonValue.String));

        private static ResumableWork<JsonValue> RowsWork<T>(IEnumerable<T> rows, Func<T, JsonValue> map, string stage) => new ResumableWork<JsonValue>(done => RowsRoutine(rows, map, stage, done));
        private static IEnumerable<string> RowsRoutine<T>(IEnumerable<T> rows, Func<T, JsonValue> map, string stage, Action<JsonValue> done)
        {
            var result = new List<JsonValue>(); foreach (var row in rows) { result.Add(map(row)); yield return stage; }
            using (var work = JsonValue.ArrayWork(result)) { while (work.MoveNext()) yield return work.Stage; done(work.Result); }
        }
        private static IOperationSystems RestoreDomain(CompiledContent content, JsonValue snapshot) => RestoreDomainWork(content, snapshot).Drain();
        private static ResumableWork<IOperationSystems> RestoreDomainWork(CompiledContent content, JsonValue snapshot) => new ResumableWork<IOperationSystems>(done => RestoreDomainRoutine(content, snapshot, done));
        private static IEnumerable<string> RestoreDomainRoutine(CompiledContent content, JsonValue snapshot, Action<IOperationSystems> done)
        {
            var identity = snapshot["identity"];
            if (identity["run_id"].Text != RunId) throw new InvalidOperationException("Synthetic persistence fixture identity required");
            Domain domain;
            using (var create = Domain.CreateWork(content, identity["mission_version_id"].Text, identity["layout_version_id"].Text, identity["guidance_policy"].Text == "guided_first_play"))
            { while (create.MoveNext()) yield return create.Stage; domain = create.Result; }
            long operation = snapshot["operation_tick"].Integer, scenario = snapshot["scenario_tick"].Integer;
            using (var domainRestore = domain.RestoreWork(snapshot)) { while (domainRestore.MoveNext()) yield return domainRestore.Stage; }
            JsonValue state; using (var workstate = domain.ReadDomainStateWork()) { while (workstate.MoveNext()) yield return workstate.Stage; state = workstate.Result; }
            ObjectiveObservations facts; using (var workfacts = domain.ObserveObjectivesWork(new OperationContext(operation, scenario, false), state)) { while (workfacts.MoveNext()) yield return workfacts.Stage; facts = workfacts.Result; }
            var metricRows = new List<JsonValue>();
            foreach (var row in snapshot["metrics"].Items) { if (row["id"].Text.StartsWith("objective:", StringComparison.Ordinal)) metricRows.Add(row); yield return "fixture.restore.objective_metric"; }
            JsonValue metrics;
            using (var copy = JsonValue.ArrayWork(metricRows)) { while (copy.MoveNext()) yield return copy.Stage; metrics = copy.Result; }
            bool terminal = snapshot["phase"].Text == "outcome";
            ObjectiveSystem objectiveShell;
            using (var create = ObjectiveSystem.CreateWork(domain.Mission, domain.Layout)) { while (create.MoveNext()) yield return create.Stage; objectiveShell = create.Result; }
            ObjectiveSystem objectives; using (var workobjectives = objectiveShell.RestoreWork(metrics, snapshot["objective_monitors"], operation, scenario, state, facts, snapshot["milestones"].Items, terminal)) { while (workobjectives.MoveNext()) yield return workobjectives.Stage; objectives = workobjectives.Result; }
            if (terminal)
            {
                if (snapshot["outcome"].IsNull) throw new InvalidOperationException("Saved terminal result disagrees with actual restored objectives and residents");
                OutcomeResult outcome; using (var workoutcome = objectives.OutcomeWork(state)) { while (workoutcome.MoveNext()) yield return workoutcome.Stage; outcome = workoutcome.Result; }bool equal; using (var workequal = outcome.ToJson(snapshot["outcome"]["reason"].Text).CanonicalEqualsWork(snapshot["outcome"])) { while (workequal.MoveNext()) yield return workequal.Stage; equal = workequal.Result; } if (!equal) throw new InvalidOperationException("Saved terminal result disagrees with actual restored objectives and residents");
            }
            var wrapped = new ObjectiveOperationSystems(domain, objectives);
            using (var marks = wrapped.RestoreEngineMilestonesWork(snapshot["milestones"].Items, operation, scenario)) { while (marks.MoveNext()) yield return marks.Stage; }
            done(wrapped);
                }

        private sealed class Domain : IResumableOperationSystems, IResumableObjectiveDomainObserver, IResumableCommandSystems
        {
            private readonly CompiledContent content;
            public CompiledMission Mission { get; }
            public LayoutDef Layout { get; }
            private EquipmentSystem equipment;
            private readonly bool guided;
            private HashSet<string> forecastKnowledge;
            // Availability of observations produced during this actual interval.
            // It carries no history and is deliberately absent after restore.
            private long freshFactsOperation = -1;
            private readonly SortedDictionary<string, bool> edgeEnabled;
            private readonly SortedDictionary<string, long> edgeMultipliers;
            private readonly Dictionary<string, NavNode> navigationNodes;
            private readonly Dictionary<string, NavEdge> navigationEdges;
            private readonly Dictionary<string, long> seaHeads = new Dictionary<string, long>(StringComparer.Ordinal);
            private readonly Dictionary<string, bool> gates = new Dictionary<string, bool>(StringComparer.Ordinal);

            public Domain(CompiledContent content, string missionId, string layoutId, bool guided = false)
                : this(content, missionId, layoutId, guided, true)
            { InitializeWork().Drain(); }
            private Domain(CompiledContent content, string missionId, string layoutId, bool guided, bool deferred)
            {
                this.content = content; this.guided = guided;
                Mission = content.Envelope.payload.missions.Single(m => m.mission_version_id == missionId);
                Layout = content.Envelope.payload.layouts.Single(l => l.id == layoutId && l.mission_version_id == missionId);
                edgeEnabled = new SortedDictionary<string, bool>(StringComparer.Ordinal);
                edgeMultipliers = new SortedDictionary<string, long>(StringComparer.Ordinal);
                navigationNodes = new Dictionary<string, NavNode>(StringComparer.Ordinal);
                navigationEdges = new Dictionary<string, NavEdge>(StringComparer.Ordinal);
            }
            public static ResumableWork<Domain> CreateWork(CompiledContent content, string missionId, string layoutId, bool guided)
                => new ResumableWork<Domain>(done => CreateRoutine(content, missionId, layoutId, guided, done));
            private static IEnumerable<string> CreateRoutine(CompiledContent content, string missionId, string layoutId, bool guided, Action<Domain> done)
            {
                var next = new Domain(content, missionId, layoutId, guided, true);
                using (var work = next.InitializeWork()) { while (work.MoveNext()) yield return work.Stage; }
                done(next);
            }
            private ResumableWork<bool> InitializeWork() => new ResumableWork<bool>(InitializeRoutine);
            private IEnumerable<string> InitializeRoutine(Action<bool> done)
            {
                foreach (var unit in RequireSupportedSyntheticContentSteps()) yield return unit;
                foreach (var edge in Layout.nav_edges) { edgeEnabled.Add(edge.id, edge.administrative_enabled); yield return "fixture.create.edge_enabled"; }
                foreach (var edge in Layout.nav_edges) { edgeMultipliers.Add(edge.id, edge.current_multiplier_permille); yield return "fixture.create.edge_multiplier"; }
                foreach (var node in Layout.nav_nodes) { navigationNodes.Add(node.id, node); yield return "fixture.create.node_index"; }
                foreach (var edge in Layout.nav_edges) { navigationEdges.Add(edge.id, edge); yield return "fixture.create.edge_index"; }
                TransportSystem transport;
                using (var work = TransportSystem.CreateWork(Mission, Layout, content.Envelope.payload.equipment)) { while (work.MoveNext()) yield return work.Stage; transport = work.Result; }
                transport.RestoreBoundaryCursor(0);
                WaterSystem water;
                using (var work = WaterSystem.CreateWork(Layout)) { while (work.MoveNext()) yield return work.Stage; water = work.Result; }
                using (var work = EquipmentSystem.CreateWork(Mission, Layout, content.Envelope.payload.equipment, water, transport)) { while (work.MoveNext()) yield return work.Stage; equipment = work.Result; }
                done(true);
            }
            private Domain(Domain other)
            {
                content = other.content; Mission = other.Mission; Layout = other.Layout; guided = other.guided;
                // Completed immutable definition indexes can be shared; boundary state cannot.
                navigationNodes = other.navigationNodes; navigationEdges = other.navigationEdges;
                freshFactsOperation = other.freshFactsOperation;
                forecastKnowledge = other.forecastKnowledge == null ? null : new HashSet<string>(StringComparer.Ordinal);
                edgeEnabled = new SortedDictionary<string, bool>(StringComparer.Ordinal);
                edgeMultipliers = new SortedDictionary<string, long>(StringComparer.Ordinal);
            }
            public IOperationSystems Fork() => ForkWork().Drain();
            public ResumableWork<IOperationSystems> ForkWork() => new ResumableWork<IOperationSystems>(ForkRoutine);
            private IEnumerable<string> ForkRoutine(Action<IOperationSystems> done)
            {
                var next = new Domain(this);
                using (var work = equipment.ForkWork()) { while (work.MoveNext()) yield return work.Stage; next.equipment = work.Result; }
                foreach (var row in edgeEnabled) { next.edgeEnabled.Add(row.Key, row.Value); yield return "fixture.fork.edgeEnabled"; }
                foreach (var row in edgeMultipliers) { next.edgeMultipliers.Add(row.Key, row.Value); yield return "fixture.fork.edgeMultiplier"; }
                if (forecastKnowledge != null) foreach (string id in forecastKnowledge) { next.forecastKnowledge.Add(id); yield return "fixture.fork.knowledge"; }
                foreach (var row in seaHeads) { next.seaHeads.Add(row.Key, row.Value); yield return "fixture.fork.sea"; }
                foreach (var row in gates) { next.gates.Add(row.Key, row.Value); yield return "fixture.fork.gate"; }
                done(next);
            }

            private IEnumerable<string> RequireSupportedSyntheticContentSteps()
            {
                // These families really are absent from the compiled fixtures.
                // Finite, ungated water links use the actual WaterSystem. Reject
                // other expansion instead of supplying safety
                // facts for a hazard/power/mechanics owner that does not exist.
                if (Mission.mission_version_id != MissionId || Mission.tutorial_policy != (guided ? "guided_first_play_then_unguided" : "none") ||
                    Layout.fire_cells.Count != 0 || Layout.fire_links.Count != 0 || Layout.supports.Count != 0 || Layout.runouts.Count != 0 ||
                    Layout.tides.Count != 0 || Layout.lava_segments.Count != 0 || Layout.power_links.Count != 0)
                    throw new InvalidOperationException("Forecast fixture requires its explicit absent hazard families and actual water-only scheduled operations");
                foreach (var item in Layout.infrastructure)
                { if (item.kind != "dock" || item.required_support_ids.Count != 0 || item.required_equipment_socket_ids.Count != 0) UnsupportedContent(); yield return "fixture.create.infrastructure"; }
                if (Layout.resources.Count != 0 || Layout.producers.Count != 0) UnsupportedContent();
                foreach (var link in Layout.hydraulic_links)
                { if (link.from_kind != "water_node" || link.to_kind != "water_node" || link.gate_id != null || link.barrier_socket_id != null) UnsupportedContent(); yield return "fixture.create.hydraulic_link"; }
                foreach (var item in content.Envelope.payload.equipment)
                { if (item.id != "beacon" && item.id != "pump") UnsupportedContent(); yield return "fixture.create.equipment_definition"; }
                foreach (var edge in Layout.nav_edges)
                { if (edge.required_equipment_socket_id != null) UnsupportedContent(); yield return "fixture.create.navigation_edge"; }
                var waterIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in Layout.water_nodes) { waterIds.Add(node.id); yield return "fixture.create.water_index"; }
                foreach (var socket in Layout.sockets)
                {
                    if (socket.support_id != null || socket.lava_segment_id != null || socket.fire_cell_ids.Count != 0 || socket.resource_store_id != null || socket.hydraulic_link_id != null) UnsupportedContent();
                    foreach (string id in socket.footprint_cell_ids) { if (!waterIds.Contains(id)) UnsupportedContent(); yield return "fixture.create.socket_footprint"; }
                    yield return "fixture.create.socket";
                }
                foreach (var op in content.LayoutOperations[Layout.id])
                { if (!(op.Kind == "set_source_rate" && op.TargetKind == "water_source" || op.Kind == "set_edge_enabled" && op.TargetKind == "nav_edge_set")) UnsupportedContent(); yield return "fixture.create.operation"; }
            }
            private static void UnsupportedContent() => throw new InvalidOperationException("Forecast fixture requires its explicit absent hazard families and actual water-only scheduled operations");

        public void Restore(JsonValue snapshot) => RestoreWork(snapshot).Drain();
        public ResumableWork<bool> RestoreWork(JsonValue snapshot) => new ResumableWork<bool>(done => RestoreRoutine(snapshot, done));
        private IEnumerable<string> RestoreRoutine(JsonValue snapshot, Action<bool> done)
        {
                foreach (string field in new[] { "fire", "supports", "runouts", "tides", "lava" })
                    { yield return "fixture.restore"; if (snapshot[field].Items.Count != 0) throw new InvalidOperationException("Unavailable fixture state owner: " + field); }
                var events = snapshot["event_state"];
                foreach (string field in new[] { "dynamic_events", "wind_region_codes", "cooler_socket_enabled", "shelter_region_enabled", "resource_warning_thresholds", "gate_open" })
                    { yield return "fixture.restore"; if (events[field].Items.Count != 0) throw new InvalidOperationException("Unavailable fixture event owner: " + field); }
                long operation = snapshot["operation_tick"].Integer;
                EquipmentSystem restoredEquipment; using (var workrestoredEquipment = SnapshotOwners.RestoreEquipmentWork(content, snapshot, owner => PowerFrame(owner, operation), null, owner => PowerFrameWork(owner, operation))) { while (workrestoredEquipment.MoveNext()) yield return workrestoredEquipment.Stage; restoredEquipment = workrestoredEquipment.Result; } equipment = restoredEquipment;
                freshFactsOperation = -1;
                // Reconstruct scheduled administrative changes from compiled
                // operations and the validated fired set, then compare readback.
                JsonValue expected; using (var workexpected = ReadDomainStateWork()) { while (workexpected.MoveNext()) yield return workexpected.Stage; expected = workexpected.Result; }
                var firedForEdges = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in events["fired_event_ids"].Items) { firedForEdges.Add(row.Text); yield return "fixture.restore.fired_id"; }
                var applied = new List<ResolvedEventOperation>();
                foreach (var op in content.LayoutOperations[Layout.id]) { if (firedForEdges.Contains(op.EventId)) applied.Add(op); yield return "fixture.restore.applied_operation"; }
                ResolvedEventOperation[] appliedOperations;
                using (var sort = ResumableSort.Create(applied, Comparer<ResolvedEventOperation>.Create((a, b) =>
                    { int tick = a.ScenarioTick.CompareTo(b.ScenarioTick); return tick != 0 ? tick : string.CompareOrdinal(a.EventId, b.EventId); })))
                { while (sort.MoveNext()) yield return sort.Stage; appliedOperations = sort.Result; }
                foreach (var op in appliedOperations)
                    { yield return "fixture.restore"; if (op.Kind == "set_edge_enabled") foreach (string id in op.TargetIds) { yield return "fixture.restore"; edgeEnabled[id] = op.Value != 0; } }
                JsonValue afterEdges; using (var workafterEdges = ReadDomainStateWork()) { while (workafterEdges.MoveNext()) yield return workafterEdges.Stage; afterEdges = workafterEdges.Result; } expected = afterEdges;
                foreach (string field in new[] { "administrative_edge_enabled", "edge_current_multipliers", "group_held" })
                    { yield return "fixture.restore"; { bool equal; using (var workequal = events[field].CanonicalEqualsWork(expected["event_state"][field])) { while (workequal.MoveNext()) yield return workequal.Stage; equal = workequal.Result; } if (!equal) throw new InvalidOperationException("Fixture event map disagrees with compiled/current authority: " + field); } }
                bool guidanceMatches; using (var workguidanceMatches = snapshot["guidance_state"].CanonicalEqualsWork(expected["guidance_state"])) { while (workguidanceMatches.MoveNext()) yield return workguidanceMatches.Stage; guidanceMatches = workguidanceMatches.Result; } if (!guidanceMatches) throw new InvalidOperationException("Unsupported fixture guidance continuation");
                JsonValue savedRates;
                using (var read = equipment.Water.ReadRateRemaindersWork()) { while (read.MoveNext()) yield return read.Stage; savedRates = read.Result; }
                var rates = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var row in savedRates.Items) { if (row["kind"].Text == "source") rates.Add(row["id"].Text, row["current_rate_mm3_per_second"].Integer); yield return "fixture.restore.source_rate"; }
                foreach (var source in Layout.sources)
                {yield return "fixture.restore"; 
                    long rate = source.initial_rate_mm3_per_second;
                    foreach (var op in appliedOperations)
                        { yield return "fixture.restore"; if (op.Kind == "set_source_rate" && op.TargetIds.Contains(source.id)) rate = op.Value; }
                    if (rates["source:" + source.id] != rate) throw new InvalidOperationException("Saved source rate disagrees with actual fired scheduled operations");
                }
                using (var validation = ValidateBoundaryWork(new OperationContext(operation, snapshot["scenario_tick"].Integer, false))) { while (validation.MoveNext()) yield return validation.Stage; }
            
            done(true);
        }

        public JsonValue ReadDomainState() => ReadDomainStateWork().Drain();
        public ResumableWork<JsonValue> ReadDomainStateWork() => new ResumableWork<JsonValue>(done => ReadDomainStateRoutine(done));
        private IEnumerable<string> ReadDomainStateRoutine(Action<JsonValue> done)
        {
                JsonValue equipmentState; using (var workequipmentState = equipment.ReadStateWork()) { while (workequipmentState.MoveNext()) yield return workequipmentState.Stage; equipmentState = workequipmentState.Result; } var state = new Dictionary<string, JsonValue>(equipmentState.Members, StringComparer.Ordinal);
                JsonValue transportState; using (var worktransportState = equipment.Transport.ReadStateWork()) { while (worktransportState.MoveNext()) yield return worktransportState.Stage; transportState = worktransportState.Result; } foreach (var pair in transportState.Members) { yield return "fixture.read"; state.Add(pair.Key, pair.Value); }
                state.Add("guidance_state", O("policy", guided ? "guided_first_play" : "unguided", "step_id", guided ? "river_warning" : "none", "completed_step_ids", A(), "acknowledged_warning", guided));
                JsonValue water; using (var workwater = equipment.Water.ReadWaterWork()) { while (workwater.MoveNext()) yield return workwater.Stage; water = workwater.Result; }JsonValue rates; using (var workrates = equipment.Water.ReadRateRemaindersWork()) { while (workrates.MoveNext()) yield return workrates.Stage; rates = workrates.Result; } state.Add("water", water); state.Add("rate_remainders", rates);
                state.Add("water_ledger", equipment.Water.ReadLedger());
                foreach (string field in new[] { "fire", "supports", "runouts", "tides", "lava", "objective_monitors", "recent_events" }) { yield return "fixture.read"; state.Add(field, A()); }
                JsonValue enabledRows; using (var workenabledRows = RowsWork(edgeEnabled, e => O("id", e.Key, "value", e.Value), "fixture.read.edgeEnabled")) { while (workenabledRows.MoveNext()) yield return workenabledRows.Stage; enabledRows = workenabledRows.Result; }JsonValue multiplierRows; using (var workmultiplierRows = RowsWork(edgeMultipliers, e => O("id", e.Key, "value", e.Value), "fixture.read.edgeMultiplier")) { while (workmultiplierRows.MoveNext()) yield return workmultiplierRows.Stage; multiplierRows = workmultiplierRows.Result; }JsonValue heldRows; using (var workheldRows = equipment.Transport.GroupHeldWork()) { while (workheldRows.MoveNext()) yield return workheldRows.Stage; heldRows = workheldRows.Result; } state.Add("event_state", O("next_static_event_index", 0, "fired_event_ids", A(), "revealed_event_ids", A(), "warned_event_ids", A(),
                    "dynamic_events", A(), "administrative_edge_enabled", enabledRows,
                    "edge_current_multipliers", multiplierRows,
                    "gate_open", A(), "group_held", heldRows, "wind_region_codes", A(), "cooler_socket_enabled", A(),
                    "shelter_region_enabled", A(), "resource_warning_thresholds", A()));
                JsonValue marks; using (var workmarks = equipment.Transport.MilestonesWork()) { while (workmarks.MoveNext()) yield return workmarks.Stage; marks = workmarks.Result; }JsonValue metrics; using (var workmetrics = equipment.Transport.HistoricalMetricsWork()) { while (workmetrics.MoveNext()) yield return workmetrics.Stage; metrics = workmetrics.Result; } state.Add("milestones", marks); state.Add("metrics", metrics);
                using (var work = JsonValue.ObjectWork(state)) { while (work.MoveNext()) yield return work.Stage; done(work.Result); }
                    }
            public ValidationResult Validate(GameCommand command, OperationContext context) => ValidateWork(command, context).Drain();
            public ResumableWork<ValidationResult> ValidateWork(GameCommand command, OperationContext context) =>
                new ResumableWork<ValidationResult>(done => ValidateRoutine(command, context, done));
            private IEnumerable<string> ValidateRoutine(GameCommand command, OperationContext context, Action<ValidationResult> done)
            {
                var observer = ValidationObserved;
                if (observer != null)
                {
                    JsonValue state; using (var read = ReadDomainStateWork()) { while (read.MoveNext()) yield return read.Stage; state = read.Result; }
                    observer(context.OperationTick, command.Kind, state); yield return "fixture.command.observed";
                }
                if (command.Payload is AssignRoute route) { done(equipment.Transport.ValidateRoute(route)); yield break; }
                EquipmentFrame frame; using (var build = EquipmentFrameWork(context.OperationTick, context.ScenarioTick)) { while (build.MoveNext()) yield return build.Stage; frame = build.Result; }
                PowerFrame power; using (var build = PowerFrameWork(equipment, context.OperationTick)) { while (build.MoveNext()) yield return build.Stage; power = build.Result; }
                using (var preview = equipment.PreviewWork(command, frame, "active", power)) { while (preview.MoveNext()) yield return preview.Stage; done(preview.Result); }
            }
            public void Apply(GameCommand command, OperationContext context) => ApplyWork(command, context).Drain();
            public ResumableWork<bool> ApplyWork(GameCommand command, OperationContext context) =>
                new ResumableWork<bool>(done => ApplyRoutine(command, context, done));
            private IEnumerable<string> ApplyRoutine(GameCommand command, OperationContext context, Action<bool> done)
            {
                if (command.Payload is AssignRoute route) equipment.Transport.Assign(route, context.OperationTick, command.Sequence);
                else
                {
                    EquipmentFrame frame; using (var build = EquipmentFrameWork(context.OperationTick, context.ScenarioTick)) { while (build.MoveNext()) yield return build.Stage; frame = build.Result; }
                    PowerFrame power; using (var build = PowerFrameWork(equipment, context.OperationTick)) { while (build.MoveNext()) yield return build.Stage; power = build.Result; }
                    using (var apply = equipment.ApplyWork(command, frame, "active", context, power)) { while (apply.MoveNext()) yield return apply.Stage; equipment = apply.Result; }
                }
                done(true);
            }
            public void EvaluateTides(OperationContext context)
            {
                if (Layout.tides.Count != 0) throw new InvalidOperationException("Actual tide owner required");
            }
            public void PublishWarning(EventDef definition, OperationContext context)
            {
                if (context.Events == null) throw new InvalidOperationException("Actual warning event sink required");
                var affected = content.LayoutOperations[Layout.id].Where(op => op.EventId == definition.id && op.Kind == "set_source_rate")
                    .SelectMany(op => op.TargetIds).Select(id => Layout.sources.Single(s => s.id == id).water_node_id)
                    .Select(id => Layout.water_nodes.Single(w => w.id == id).region_id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
                foreach (string region in affected) context.Events.Emit("HazardWarning", region, null, definition.id,
                    O("hazard", "water", "region_id", region, "effect_scenario_tick", definition.at_scenario_tick));
            }
            public void ApplyScheduled(ResolvedEventOperation operation, OperationContext context)
            {
                if (operation.Kind == "set_source_rate" && operation.TargetKind == "water_source")
                    foreach (string id in operation.TargetIds) equipment.Water.SetSourceRate(id, operation.Value);
                else if (operation.Kind == "set_edge_enabled" && operation.TargetKind == "nav_edge_set")
                    foreach (string id in operation.TargetIds) edgeEnabled[id] = operation.Value != 0;
                else throw new InvalidOperationException("Unsupported real scheduled owner");
            }
        public void ResolvePowerAndReservations(OperationContext context) => ResolvePowerAndReservationsWork(context).Drain();
        public ResumableWork<bool> ResolvePowerAndReservationsWork(OperationContext context) => new ResumableWork<bool>(done => ResolvePowerAndReservationsRoutine(context, done));
        private IEnumerable<string> ResolvePowerAndReservationsRoutine(OperationContext context, Action<bool> done)
        { PowerFrame frame; using (var workframe = PowerFrameWork(equipment, context.OperationTick)) { while (workframe.MoveNext()) yield return workframe.Stage; frame = workframe.Result; }EquipmentSystem next; using (var worknext = equipment.ResolvePowerWork(frame, context)) { while (worknext.MoveNext()) yield return worknext.Stage; next = worknext.Result; } equipment = next; 
            done(true);
        }
        public void SimulateHazardInterval(OperationContext context) => SimulateHazardIntervalWork(context).Drain();
        public ResumableWork<bool> SimulateHazardIntervalWork(OperationContext context) => new ResumableWork<bool>(done => SimulateHazardIntervalRoutine(context, done));
        private IEnumerable<string> SimulateHazardIntervalRoutine(OperationContext context, Action<bool> done)
        {
                EquipmentSystem water; using (var workwater = equipment.SimulateWaterWork(context, seaHeads, gates)) { while (workwater.MoveNext()) yield return workwater.Stage; water = workwater.Result; } equipment = water;
                EquipmentSystem coolant; using (var workcoolant = equipment.ProduceCoolantWork(context, new Dictionary<string, bool>(StringComparer.Ordinal))) { while (workcoolant.MoveNext()) yield return workcoolant.Stage; coolant = workcoolant.Result; } equipment = coolant;
            
            done(true);
        }
        public void RecomputeSafetyAndDamage(OperationContext context) => RecomputeSafetyAndDamageWork(context).Drain();
        public ResumableWork<bool> RecomputeSafetyAndDamageWork(OperationContext context) => new ResumableWork<bool>(done => RecomputeSafetyAndDamageRoutine(context, done));
        private IEnumerable<string> RecomputeSafetyAndDamageRoutine(OperationContext context, Action<bool> done)
        {
                EquipmentFrame damageFrame; using (var workdamageFrame = EquipmentFrameWork(context.OperationTick, context.ScenarioTick)) { while (workdamageFrame.MoveNext()) yield return workdamageFrame.Stage; damageFrame = workdamageFrame.Result; }EquipmentSystem damaged; using (var workdamaged = equipment.HazardDamageWork(context, damageFrame)) { while (workdamaged.MoveNext()) yield return workdamaged.Stage; damaged = workdamaged.Result; } equipment = damaged;
                PowerFrame powerFrame; using (var workpowerFrame = PowerFrameWork(equipment, context.OperationTick)) { while (workpowerFrame.MoveNext()) yield return workpowerFrame.Stage; powerFrame = workpowerFrame.Result; }EquipmentSystem powered; using (var workpowered = equipment.ResolvePowerWork(powerFrame, context)) { while (workpowered.MoveNext()) yield return workpowered.Stage; powered = workpowered.Result; } equipment = powered;
            
            done(true);
        }
        public void ResolveMovementAndTransfers(OperationContext context) => ResolveMovementAndTransfersWork(context).Drain();
        public ResumableWork<bool> ResolveMovementAndTransfersWork(OperationContext context) => new ResumableWork<bool>(done => ResolveMovementAndTransfersRoutine(context, done));
        private IEnumerable<string> ResolveMovementAndTransfersRoutine(OperationContext context, Action<bool> done)
        { TransportFrame frame; using (var workframe = TransportFrameWork(context.OperationTick)) { while (workframe.MoveNext()) yield return workframe.Stage; frame = workframe.Result; }using (var movement = equipment.Transport.ResolveMovementWork(context, frame)) { while (movement.MoveNext()) yield return movement.Stage; } 
            done(true);
        }
        public void DeriveMetricsAndObjectives(OperationContext context) => DeriveMetricsAndObjectivesWork(context).Drain();
        public ResumableWork<bool> DeriveMetricsAndObjectivesWork(OperationContext context) => new ResumableWork<bool>(done => DeriveMetricsAndObjectivesRoutine(context, done));
        private IEnumerable<string> DeriveMetricsAndObjectivesRoutine(OperationContext context, Action<bool> done)
        { TransportFrame frame; using (var workframe = TransportFrameWork(context.OperationTick)) { while (workframe.MoveNext()) yield return workframe.Stage; frame = workframe.Result; }using (var derive = equipment.Transport.DeriveWork(context, frame)) { while (derive.MoveNext()) yield return derive.Stage; } freshFactsOperation = context.OperationTick; 
            done(true);
        }
            public void SampleInitialObjectives(OperationContext context) => equipment.Transport.SampleInitialMilestonesWork(context).Drain();
            public ResumableWork<bool> StageWork(OperationStage stage, OperationContext context)
            {
                switch (stage)
                {
                    case OperationStage.ResolvePowerAndReservations: return ResolvePowerAndReservationsWork(context);
                    case OperationStage.SimulateHazardInterval: return SimulateHazardIntervalWork(context);
                    case OperationStage.RecomputeSafetyAndDamage: return RecomputeSafetyAndDamageWork(context);
                    case OperationStage.ResolveMovementAndTransfers: return ResolveMovementAndTransfersWork(context);
                    case OperationStage.DeriveMetricsAndObjectives: return DeriveMetricsAndObjectivesWork(context);
                    case OperationStage.ValidateBoundary: return ValidateBoundaryWork(context);
                    case OperationStage.SampleInitialObjectives: return equipment.Transport.SampleInitialMilestonesWork(context);
                    default: throw new ArgumentOutOfRangeException(nameof(stage));
                }
            }
            public ResumableWork<bool> RequiredConditionsWork() => throw new InvalidOperationException("Actual objective adapter required");
            public ResumableWork<OutcomeResult> OutcomeWork(OperationContext context) => throw new InvalidOperationException("Actual objective adapter required");
            public ResumableWork<ForecastBoundaryObservations> ObservationsWork() => throw new InvalidOperationException("Actual objective adapter required");
            public void EmitMilestone(string milestoneId, OperationContext context) => throw new InvalidOperationException("Objective adapter owns engine milestone notifications");
            // Synthetic external input: the river warning is acknowledged in the
            // initial guidance fixture. The clock must still wait for the actual
            // bus/resident evacuation. No expected tick is injected.
            public bool GuidedEpochReady => guided && equipment.Transport.EvacuatedCount == Mission.total_residents;
            public bool RequiredConditionsHold => throw new InvalidOperationException("Actual objective adapter required");
            public OutcomeResult EvaluateOutcome(OperationContext context) => throw new InvalidOperationException("Actual objective adapter required");
        public void ValidateBoundary(OperationContext context) => ValidateBoundaryWork(context).Drain();
        public ResumableWork<bool> ValidateBoundaryWork(OperationContext context) => new ResumableWork<bool>(done => ValidateBoundaryRoutine(context, done));
        private IEnumerable<string> ValidateBoundaryRoutine(OperationContext context, Action<bool> done)
        {
                using (var equipmentValidation = equipment.ValidateBoundaryWork()) { while (equipmentValidation.MoveNext()) yield return equipmentValidation.Stage; }using (var waterValidation = equipment.Water.ValidateBoundaryWork()) { while (waterValidation.MoveNext()) yield return waterValidation.Stage; }using (var transportValidation = equipment.Transport.ValidateBoundaryWork()) { while (transportValidation.MoveNext()) yield return transportValidation.Stage; }
                TransportFrame frame; using (var workframe = TransportFrameWork(context.OperationTick)) { while (workframe.MoveNext()) yield return workframe.Stage; frame = workframe.Result; }using (var dockInspection = equipment.Transport.InspectDockWaitFactsWork(context, frame)) { while (dockInspection.MoveNext()) yield return dockInspection.Stage; }
            
            done(true);
        }

            private PowerFrame PowerFrame(EquipmentSystem owner, long operation) => PowerFrameWork(owner, operation).Drain();
            private ResumableWork<PowerFrame> PowerFrameWork(EquipmentSystem owner, long operation) => new ResumableWork<PowerFrame>(done => PowerFrameRoutine(owner, operation, done));
            private IEnumerable<string> PowerFrameRoutine(EquipmentSystem owner, long operation, Action<PowerFrame> done)
            {
                var nodes = new List<PowerNodeSample>(); var items = new List<PowerEquipment>(); var consumers = new List<PowerConsumerSample>();
                Dictionary<string, long> depths; using (var read = RegionDepthsWork(owner)) { while (read.MoveNext()) yield return read.Stage; depths = read.Result; }
                foreach (var node in Layout.power_nodes)
                {
                    long depth = depths.TryGetValue(node.platform_region_id, out var sampled) ? Math.Max(0, sampled) : 0;
                    nodes.Add(new PowerNodeSample(node.id, depth, true)); yield return "fixture.power.node";
                }
                ReadOnlyCollection<EquipmentView> equipmentViews; using (var read = owner.ReadEquipmentWork()) { while (read.MoveNext()) yield return read.Stage; equipmentViews = read.Result; }
                foreach (var item in equipmentViews)
                {
                    items.Add(new PowerEquipment(item.Id, item.EquipmentId, item.SocketId, item.Enabled, item.Intact));
                    if (item.EquipmentId == "pump")
                    {
                        var socket = Layout.sockets.Single(s => s.id == item.SocketId); long depth = 0;
                        foreach (string id in socket.footprint_cell_ids) { depth = Math.Max(depth, owner.Water.Depth(id)); yield return "fixture.power.pump.depth"; }
                        consumers.Add(new PowerConsumerSample(item.Id, depth <= 200));
                    }
                    yield return "fixture.power.equipment";
                }
                foreach (var item in Layout.infrastructure) { bool safe; using (var work = InfrastructureSafeWork(item, owner, depths)) { while (work.MoveNext()) yield return work.Stage; safe = work.Result; } consumers.Add(new PowerConsumerSample(item.id, safe)); yield return "fixture.power.infrastructure"; }
                using (var frame = Resq.Simulation.Tools.PowerFrame.CreateWork(operation, nodes, new PowerLinkSample[0], items, consumers)) { while (frame.MoveNext()) yield return frame.Stage; done(frame.Result); }
            }
        private bool InfrastructureSafe(InfrastructureDef item, EquipmentSystem owner) => InfrastructureSafeWork(item, owner).Drain();
        private ResumableWork<bool> InfrastructureSafeWork(InfrastructureDef item, EquipmentSystem owner, Dictionary<string, long> depths = null) => new ResumableWork<bool>(done => InfrastructureSafeRoutine(item, owner, depths, done));
        private IEnumerable<string> InfrastructureSafeRoutine(InfrastructureDef item, EquipmentSystem owner, Dictionary<string, long> depths, Action<bool> done)
        {
            if (depths == null) using (var read = RegionDepthsWork(owner)) { while (read.MoveNext()) yield return read.Stage; depths = read.Result; }
            long depth; using (var read = BoundDepthWork(depths, item.hazard_region_ids, false)) { while (read.MoveNext()) yield return read.Stage; depth = read.Result; }
            bool safe = depth <= item.maximum_depth_mm;
            if (safe) foreach (string id in item.required_nav_node_ids)
            {
                var node = Layout.nav_nodes.Single(n => n.id == id);
                using (var read = BoundDepthWork(depths, node.hazard_region_ids, false)) { while (read.MoveNext()) yield return read.Stage; depth = read.Result; }
                if (depth > node.safe_depth_mm) { safe = false; break; }
                yield return "fixture.infrastructure.node";
            }
            done(safe); yield return "fixture.infrastructure.safety";
        }
            private ResumableWork<Dictionary<string, long>> RegionDepthsWork(EquipmentSystem owner) =>
                new ResumableWork<Dictionary<string, long>>(done => RegionDepthsRoutine(owner, done));
            private IEnumerable<string> RegionDepthsRoutine(EquipmentSystem owner, Action<Dictionary<string, long>> done)
            {
                // Local to this one frame and actual owner. Never reused across
                // water mutation, a fork, restoration or a subsequent boundary.
                var depths = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var node in Layout.water_nodes)
                {
                    long depth = owner.Water.Depth(node.id);
                    depths[node.region_id] = depths.TryGetValue(node.region_id, out var prior) ? Math.Max(prior, depth) : depth;
                    yield return "fixture.water.region_depth";
                }
                done(depths);
            }
            private static long RegionDepth(Dictionary<string, long> depths, string region)
            {
                if (!depths.TryGetValue(region, out var depth)) throw new InvalidOperationException("Actual water region required: " + region);
                return depth;
            }
            private static ResumableWork<long> BoundDepthWork(Dictionary<string, long> depths, IEnumerable<string> regions, bool requireRegion = true) =>
                new ResumableWork<long>(done => BoundDepthRoutine(depths, regions, requireRegion, done));
            private static IEnumerable<string> BoundDepthRoutine(Dictionary<string, long> depths, IEnumerable<string> regions, bool requireRegion, Action<long> done)
            {
                bool any = false; long maximum = 0;
                foreach (string region in regions)
                {
                    if (depths.TryGetValue(region, out var depth)) { maximum = any ? Math.Max(maximum, depth) : depth; any = true; }
                    else if (requireRegion) throw new InvalidOperationException("Actual water region required: " + region);
                    yield return "fixture.water.bound_depth";
                }
                done(any ? maximum : 0);
            }
            private TransportFrame TransportFrame(long operation) => TransportFrameWork(operation).Drain();
            private ResumableWork<TransportFrame> TransportFrameWork(long operation) => new ResumableWork<TransportFrame>(done => TransportFrameRoutine(operation, done));
            private IEnumerable<string> TransportFrameRoutine(long operation, Action<TransportFrame> done)
            {
                Dictionary<string, long> depths; using (var read = RegionDepthsWork(equipment)) { while (read.MoveNext()) yield return read.Stage; depths = read.Result; }
                var nodeSamples = new List<TransportNodeSample>(); foreach (var n in Layout.nav_nodes) {yield return "fixture.transport.frame"; 
                    long depth; using (var read = BoundDepthWork(depths, n.hazard_region_ids)) { while (read.MoveNext()) yield return read.Stage; depth = read.Result; } bool safe = depth <= n.safe_depth_mm;
                    nodeSamples.Add(new TransportNodeSample(n.id, safe, safe && (n.kind != "dock" || depth >= n.dock_min_depth_mm && depth <= n.dock_max_depth_mm), n.power_consumer_id == null || equipment.Power.IsPowered(n.power_consumer_id)));
                }
                var edgeSamples = new List<TransportEdgeSample>(); foreach (var e in Layout.nav_edges) {yield return "fixture.transport.frame"; 
                    long depth; using (var read = BoundDepthWork(depths, e.hazard_region_ids)) { while (read.MoveNext()) yield return read.Stage; depth = read.Result; }
                    edgeSamples.Add(new TransportEdgeSample(e.id, edgeEnabled[e.id] && depth >= e.minimum_water_depth_mm && depth <= e.maximum_water_depth_mm,
                        e.required_power_consumer_id == null || equipment.Power.IsPowered(e.required_power_consumer_id), edgeMultipliers[e.id], null));
                }
                // Actual resident/vehicle location selects the bound water
                // exposure; closure and power come from their real owners.
                JsonValue saved; using (var worksaved = equipment.Transport.ReadStateWork()) { while (worksaved.MoveNext()) yield return worksaved.Stage; saved = worksaved.Result; }
                var vehiclesById = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                var transitsById = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                foreach (var row in saved["vehicles"].Items) { vehiclesById.Add(row["id"].Text, row); yield return "fixture.transport.vehicle_index"; }
                foreach (var row in saved["transits"].Items) { transitsById.Add(row["id"].Text, row); yield return "fixture.transport.transit_index"; }
                var exposure = new Dictionary<string, bool>(StringComparer.Ordinal); foreach (var p in saved["residents"].Items) {yield return "fixture.transport.frame"; 
                    var location = p["location"];
                    if (location["kind"].Text == "vehicle") location = RequiredOwner(vehiclesById, location["vehicle_id"].Text)["location"];
                    if (location["kind"].Text == "node") { var node = RequiredOwner(navigationNodes, location["node_id"].Text); long depth; using (var read = BoundDepthWork(depths, node.hazard_region_ids)) { while (read.MoveNext()) yield return read.Stage; depth = read.Result; } exposure.Add(p["id"].Text, depth > node.safe_depth_mm); continue; }
                    var transit = RequiredOwner(transitsById, location["transit_id"].Text);
                    var edge = RequiredOwner(navigationEdges, transit["edge_id"].Text);
                    long edgeDepth; using (var read = BoundDepthWork(depths, edge.hazard_region_ids)) { while (read.MoveNext()) yield return read.Stage; edgeDepth = read.Result; }
                    exposure.Add(p["id"].Text, edgeDepth > edge.maximum_water_depth_mm);
                }
                using (var frame = Resq.Simulation.Rescue.TransportFrame.CreateWork(operation, nodeSamples, edgeSamples, exposure)) { while (frame.MoveNext()) yield return frame.Stage; done(frame.Result); }
                        }
            private static T RequiredOwner<T>(IReadOnlyDictionary<string, T> owners, string id)
            { if (!owners.TryGetValue(id, out var value)) throw new InvalidOperationException("Missing actual transport owner: " + id); return value; }
            private EquipmentFrame EquipmentFrame(long operation, long scenario) => EquipmentFrameWork(operation, scenario).Drain();
            private ResumableWork<EquipmentFrame> EquipmentFrameWork(long operation, long scenario) => new ResumableWork<EquipmentFrame>(done => EquipmentFrameRoutine(operation, scenario, done));
            private IEnumerable<string> EquipmentFrameRoutine(long operation, long scenario, Action<EquipmentFrame> done)
            {
                JsonValue transport; using (var worktransport = equipment.Transport.ReadStateWork()) { while (worktransport.MoveNext()) yield return worktransport.Stage; transport = worktransport.Result; }
                var samples = new List<EquipmentSocketSample>(); foreach (var socket in Layout.sockets) {yield return "fixture.equipment.frame"; 
                    long depth = socket.footprint_cell_ids.Select(equipment.Water.Depth).DefaultIfEmpty(0).Max();
                    long occupants = transport["residents"].Items.Count(p => p["location"]["kind"].Text == "node" && p["location"]["node_id"].Text == socket.node_id);
                    long reservations = transport["reservations"].Items.Where(r => r["resource_kind"].Text == "node" && r["resource_id"].Text == socket.node_id).Sum(r => r["quantity"].Integer);
                    bool work = transport["transfers"].Items.Any(t => t["node_id"].Text == socket.node_id);
                    bool dependent = transport["routes"].Items.Any(r => r["stops"].Items.Skip((int)r["next_stop_index"].Integer).Any(s => s["node_id"].Text == socket.node_id));
                    // All nonwater damage families and all relevant source
                    // changes at these dry footprints are explicitly absent.
                    bool sourceBound = Layout.sources.Any(s => socket.footprint_cell_ids.Contains(s.water_node_id));
                    bool warning = sourceBound && Mission.events.Any(e => (forecastKnowledge == null || forecastKnowledge.Contains(e.id)) && e.warning_at_scenario_tick <= scenario);
                    samples.Add(new EquipmentSocketSample(socket.id, depth, 0, depth <= 200, !work, occupants, reservations,
                        work, dependent, false, false, false, warning, depth > 200));
                }
                using (var frame = Resq.Simulation.Tools.EquipmentFrame.CreateWork(operation, samples, seaHeads)) { while (frame.MoveNext()) yield return frame.Stage; done(frame.Result); }
                        }

            public void FreezeForecastKnowledge(IReadOnlyCollection<string> revealedEventIds)
            {
                if (forecastKnowledge != null) throw new InvalidOperationException("Forecast knowledge is frozen once per isolated owner");
                forecastKnowledge = new HashSet<string>(revealedEventIds, StringComparer.Ordinal);
            }
            public IEnumerable<ForecastRiskSample> ObserveForecastRisk(OperationContext context, JsonValue domainState) => ObserveForecastRiskWork(context, domainState).Drain();
            public ResumableWork<IEnumerable<ForecastRiskSample>> ObserveForecastRiskWork(OperationContext context, JsonValue domainState) => new ResumableWork<IEnumerable<ForecastRiskSample>>(done => ObserveForecastRiskRoutine(context, domainState, done));
            private IEnumerable<string> ObserveForecastRiskRoutine(OperationContext context, JsonValue domainState, Action<IEnumerable<ForecastRiskSample>> done)
            { var rows = new List<ForecastRiskSample>();
                Dictionary<string, long> depths; using (var read = RegionDepthsWork(equipment)) { while (read.MoveNext()) yield return read.Stage; depths = read.Result; }
                // Uses exactly the same real water owner and safety comparisons
                // as TransportFrame/EquipmentFrame; no fallback hazard threshold.
                var regions = new List<string>(); var unsafeRegions = new Dictionary<string, bool>(StringComparer.Ordinal);
                var regionByWater = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var node in Layout.water_nodes)
                {
                    regionByWater.Add(node.id, node.region_id);
                    if (!unsafeRegions.ContainsKey(node.region_id)) { regions.Add(node.region_id); unsafeRegions.Add(node.region_id, false); }
                    yield return "fixture.observe.risk.region";
                }
                // Visit each actual binding once instead of rescanning the
                // complete graph for every region. Row order remains first
                // occurrence in Layout.water_nodes, matching Distinct above.
                foreach (var node in Layout.nav_nodes)
                {
                    foreach (string region in node.hazard_region_ids)
                    {
                        if (depths.TryGetValue(region, out var depth) && (depth > node.safe_depth_mm || node.kind == "dock" && (depth < node.dock_min_depth_mm || depth > node.dock_max_depth_mm))) unsafeRegions[region] = true;
                        yield return "fixture.observe.risk.node";
                    }
                    yield return "fixture.observe.risk.node_done";
                }
                foreach (var edge in Layout.nav_edges)
                {
                    foreach (string region in edge.hazard_region_ids)
                    {
                        if (depths.TryGetValue(region, out var depth) && (depth < edge.minimum_water_depth_mm || depth > edge.maximum_water_depth_mm)) unsafeRegions[region] = true;
                        yield return "fixture.observe.risk.edge";
                    }
                    yield return "fixture.observe.risk.edge_done";
                }
                foreach (var socket in Layout.sockets)
                {
                    var touched = new HashSet<string>(StringComparer.Ordinal); bool any = false; long maximum = 0;
                    foreach (string id in socket.footprint_cell_ids)
                    {
                        string region = regionByWater.TryGetValue(id, out var bound) ? bound : Layout.water_nodes.Single(w => w.id == id).region_id;
                        touched.Add(region); long depth = equipment.Water.Depth(id); maximum = any ? Math.Max(maximum, depth) : depth; any = true;
                        yield return "fixture.observe.risk.footprint";
                    }
                    if (maximum > 200) foreach (string region in touched) { unsafeRegions[region] = true; yield return "fixture.observe.risk.socket"; }
                    yield return "fixture.observe.risk.socket_done";
                }
                foreach (string region in regions)
                { rows.Add(new ForecastRiskSample(region, "water", RegionDepth(depths, region), unsafeRegions[region])); yield return "fixture.observe.risk"; }
                done(rows);
            }

        public ObjectiveObservations ObserveObjectives(OperationContext context, JsonValue domainState) => ObserveObjectivesWork(context, domainState).Drain();
        public ResumableWork<ObjectiveObservations> ObserveObjectivesWork(OperationContext context, JsonValue domainState) => new ResumableWork<ObjectiveObservations>(done => ObserveObjectivesRoutine(context, domainState, done));
        private IEnumerable<string> ObserveObjectivesRoutine(OperationContext context, JsonValue domainState, Action<ObjectiveObservations> done)
        {
                TransportFrame frame; using (var workframe = TransportFrameWork(context.OperationTick)) { while (workframe.MoveNext()) yield return workframe.Stage; frame = workframe.Result; }
                var routes = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var route in Layout.routes)
                {yield return "fixture.observe.objectives"; 
                    string from = route.from_node_id; bool available = true;
                    foreach (string to in route.required_waypoint_ids.Concat(new[] { route.to_node_id }))
                    {yield return "fixture.observe.objectives"; 
                        if (from != to) { ReadOnlyCollection<string> path; using (var workpath = equipment.Transport.FindPathWork(from, to, route.traversal_class, route.mobility_class, 1, frame)) { while (workpath.MoveNext()) yield return workpath.Stage; path = workpath.Result; } if (path.Count == 0) available = false; }
                        from = to;
                    }
                    routes.Add(ObjectiveObservations.RouteKey(route.id, route.traversal_class), available);
                }
                var exposure = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var node in Layout.water_nodes) { string key = ObjectiveObservations.ExposureKey(node.region_id, "water"); long depth = equipment.Water.Depth(node.id); exposure[key] = exposure.TryGetValue(key, out var old) ? Math.Max(old, depth) : depth; yield return "fixture.observe.exposure"; }
                bool fresh = freshFactsOperation == context.OperationTick;
                IEnumerable<CompletedEdgePassage> passages = fresh ? (IEnumerable<CompletedEdgePassage>)equipment.Transport.ReadBoundaryFacts(context.OperationTick) : new CompletedEdgePassage[0];
                IEnumerable<CompletedNodeDwell> dwells = fresh ? (IEnumerable<CompletedNodeDwell>)equipment.Transport.ReadCompletedNodeDwells(context.OperationTick) : new CompletedNodeDwell[0];
                var peaks = new Dictionary<string, long>(StringComparer.Ordinal);
                if (fresh) { foreach (var peak in equipment.Transport.ReadNodeOccupancyPeaks(context.OperationTick)) { peaks.Add(peak.Key, peak.Value); yield return "fixture.observe.peak"; } }
                else { foreach (var node in Layout.nav_nodes) { peaks.Add(node.id, domainState["residents"].Items.Count(p => p["location"]["kind"].Text == "node" && p["location"]["node_id"].Text == node.id)); yield return "fixture.observe.current.occupancy"; } }
                var infrastructure = new Dictionary<string, bool>(StringComparer.Ordinal);
                Dictionary<string, long> depths; using (var read = RegionDepthsWork(equipment)) { while (read.MoveNext()) yield return read.Stage; depths = read.Result; }
                foreach (var item in Layout.infrastructure) { bool safe; using (var work = InfrastructureSafeWork(item, equipment, depths)) { while (work.MoveNext()) yield return work.Stage; safe = work.Result; } infrastructure.Add(item.id, safe && equipment.Power.IsPowered(item.id)); yield return "fixture.observe.infrastructure"; }
                ReadOnlyCollection<DockWaitSample> dockWaits; using (var workdockWaits = equipment.Transport.InspectDockWaitFactsWork(context, frame)) { while (workdockWaits.MoveNext()) yield return workdockWaits.Stage; dockWaits = workdockWaits.Result; } using (var work = ObjectiveObservations.CreateWork(context.OperationTick, infrastructure, routes, exposure, passages, dockWaits, dwells, peaks)) { while (work.MoveNext()) yield return work.Stage; done(work.Result); }
                    }
        }
    }
}
