using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Resq.Content;
using UnityEngine;

namespace Resq.Editor.Content.Tests
{
    [TestFixture,Category("runtime_content")]
    public sealed class ContentCompilerTests
    {
        private static string Root=>Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        private static JsonValue Fixture()=>SystemFixture("valid");
        private static JsonValue SystemFixture(string system)=>StrictJson.Parse(File.ReadAllBytes(Path.Combine(Root,"content/runtime/fixtures/compiler-"+system+".json")));
        private static string Catalogue()=>File.ReadAllText(Path.Combine(Root,"specification/content/mission_contracts.json"));
        private static JsonValue First(JsonValue value,string field)=>value[field].Items[0];
        private static JsonValue ChangeFirst(JsonValue value,string field,Func<JsonValue,JsonValue> change)=>value.With(field,JsonValue.Array(value[field].Items.Select((item,index)=>index==0?change(item):item)));
        private static JsonValue Mission(JsonValue value,Func<JsonValue,JsonValue> change)=>ChangeFirst(value,"missions",change);
        private static JsonValue Layout(JsonValue value,Func<JsonValue,JsonValue> change)=>ChangeFirst(value,"layouts",layout=> { var next=change(layout); return next.With("graph_hash",JsonValue.String(ContentCompilation.ComputeGraphHash(next))); });
        private static void Reject(JsonValue input,string label)=>Assert.Throws<ContentException>(()=>ContentCompilation.Compile(input),label);
        private static void RejectJson(string input)=>Assert.Throws<ContentException>(()=>StrictJson.Parse(input),input);

        [Test,NUnit.Framework.Property("case_id","check-c01/assertion/assertion_id=check-c01-a01")]
        public void TypedContentValidatesAndRoundTripsAllFrozenOperations()
        {
            var input=Fixture();var compiled=ContentCompiler.Compile(input.Canonical());
            Assert.That(compiled.Envelope.payload.missions[0].total_residents,Is.EqualTo(1));
            Assert.That(compiled.Envelope.payload.missions[0].objectives.Single(item=>item.id=="mastery").@params,Is.InstanceOf<ExposureBelowParams>());
            Assert.That(compiled.Envelope.payload.equipment[0].parameters,Is.InstanceOf<BeaconParameters>());
            Assert.That(compiled.LayoutOperations.Count,Is.EqualTo(2));
            Assert.That(compiled.LayoutOperations["fixture-compact"][0].TargetIds,Is.EqualTo(new[]{"inflow"}));
            Assert.That(compiled.LayoutSignature("fixture-compact"),Is.Not.EqualTo(compiled.LayoutSignature("fixture-wide")));
            var roundTrip=ContentCompilation.Load(Encoding.UTF8.GetBytes(compiled.CanonicalEnvelope));
            Assert.That(roundTrip.CanonicalEnvelope,Is.EqualTo(compiled.CanonicalEnvelope));
            Assert.That(roundTrip.ContentHash,Is.EqualTo(compiled.ContentHash));
            WriteAndVerifySyntheticBundle(compiled);
            var reordered=input.With("layouts",JsonValue.Array(input["layouts"].Items.Reverse()));
            Assert.That(ContentCompilation.Compile(reordered).ContentHash,Is.EqualTo(compiled.ContentHash));
            Assert.Throws<NotSupportedException>(()=>((IList<CompiledMission>)compiled.Envelope.payload.missions).Clear());
            Assert.Throws<NotSupportedException>(()=>((IDictionary<string,JsonValue>)input.Members).Clear());
            var missions=AuthoringCatalogue.ReadMissionDefinitions(Catalogue());
            Assert.That(missions.Count,Is.EqualTo(25)); Assert.That(missions.Select(item=>item.world_id).Distinct().Count(),Is.EqualTo(5));
            foreach(var mission in missions)
            {
                Assert.That(mission.groups.Sum(group=>group.resident_ids.Count),Is.EqualTo(mission.total_residents),mission.id);
                foreach(var objective in mission.objectives) { Assert.That(objective.@params,Is.InstanceOf<ConditionParameters>()); ObjectiveDef.Read(objective.ToJson()); }
                foreach(var ev in mission.events) foreach(var operation in ev.operations) Assert.That(operation.value,Is.GreaterThanOrEqualTo(0));
                Assert.That(CompiledMission.Read(StrictJson.Parse(mission.ToJson().Canonical())).ToJson().Canonical(),Is.EqualTo(mission.ToJson().Canonical()));
            }
            var canonical=StrictJson.Parse("{\"b\":[true,null,\"x\"],\"a\":1}");
            Assert.That(canonical.Canonical(),Is.EqualTo("{\"a\":1,\"b\":[true,null,\"x\"]}"));
            Assert.That(canonical.Sha256(),Is.EqualTo("eca8cfb31ab74533e1eb2f4c74d2d55dfe3c79ac704787e54be8647ea7777eb1"));
            Assert.That(StrictJson.Parse("\"\\u0001/<>\u2028😀\"").Canonical(),Is.EqualTo("\"\\u0001/<>\u2028😀\""));
            ReviewPositiveCases(input);
            foreach(string schema in new[]{"ContentBundleEnvelope","ContentBundle","CompiledMission","LayoutDef","ObjectiveDef","EventDef"})
            {
                var committed=StrictJson.Parse(File.ReadAllBytes(Path.Combine(Root,"content/runtime/schemas",schema+".schema.json")));
                Assert.That(StrictJson.Parse(ContentSchemas.Export(schema)).Canonical(),Is.EqualTo(committed.Canonical()),schema);
            }
        }

        [Test,NUnit.Framework.Property("case_id","check-c01/assertion/assertion_id=check-c01-a02")]
        public void InvalidDuplicateFutureAndStructuralMutationsAreRejected()
        {
            foreach(string json in new[]{"{\"x\":1,\"x\":2}","{\"a\":{\"x\":1,\"x\":2}}","1.0","1e1","01","-0","NaN","9223372036854775808","true false","\ufeff{}","\"e\\u0301\"","\"\\ud800\"","{\"é\":1}","[1,]"}) RejectJson(json);
            Assert.Throws<ContentException>(()=>StrictJson.Parse(new byte[]{0xc0,0xaf}));
            var fixture=Fixture();
            Reject(fixture.With("script",JsonValue.String("award_all")),"unknown root field");
            Reject(fixture.Without("rules_version"),"missing root field");
            Reject(fixture.With("rules_version",JsonValue.String("future")),"future rules");
            Reject(fixture.With("worlds",JsonValue.Array(fixture["worlds"].Items.Concat(fixture["worlds"].Items))),"duplicate ID");
            Reject(Mission(fixture,m=>m.With("supply_budget",JsonValue.Number(0))),"budget bound");
            Reject(Mission(fixture,m=>m.With("supply_budget",JsonValue.Number(1)).With("default_stock",JsonValue.Array(new[]{First(m,"default_stock").With("count",JsonValue.Number(2))}))),"stock maximum");
            Reject(Mission(fixture,m=>m.With("total_residents",JsonValue.Number(256))),"population mismatch");
            Reject(Mission(fixture,m=>m.With("minimum_rescues",JsonValue.Number(2))),"rescue bounds");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ev.With("at_scenario_tick",JsonValue.Number(0)))),"effect zero");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ev.With("at_scenario_tick",JsonValue.Number(1000)))),"effect deadline");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ev.With("warning_at_scenario_tick",JsonValue.Number(101)))),"warning after effect");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ev.With("warning_at_scenario_tick",JsonValue.Number(1)))),"missing warning lead");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ChangeFirst(ev,"operations",op=>op.With("kind",JsonValue.String("eval"))))),"unknown opcode");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ChangeFirst(ev,"operations",op=>op.With("target_role",JsonValue.String("missing"))))),"unbound role");
            Reject(Mission(fixture,m=>ChangeFirst(m,"events",ev=>ev.With("operations",JsonValue.Array(ev["operations"].Items.Concat(ev["operations"].Items))))),"contradictory same tick write");
            Reject(Layout(fixture,l=>ChangeFirst(l,"roles",role=>role.With("target_kind",JsonValue.String("group")).With("target_ids",JsonValue.Array(new[]{JsonValue.String("group")})))),"role type mismatch");
            Reject(Layout(fixture,l=>ChangeFirst(l,"roles",role=>role.With("target_ids",JsonValue.Array(new[]{JsonValue.String("missing")})))),"role unknown target");
            Reject(Layout(fixture,l=>l.With("nav_edges",JsonValue.Array(new[]{First(l,"nav_edges")}))),"unreachable evacuation");
            Reject(Layout(fixture,l=>ChangeFirst(l,"nav_edges",edge=>edge.With("from_node_id",JsonValue.String("missing")))),"unknown edge endpoint");
            Reject(Layout(fixture,l=>ChangeFirst(l,"nav_edges",edge=>edge.With("base_operation_ticks",JsonValue.Number(0)))),"zero traversal");
            Reject(Layout(fixture,l=>ChangeFirst(l,"nav_nodes",node=>node.With("resident_capacity",JsonValue.Number(0)))),"insufficient assembly capacity");
            Reject(Layout(fixture,l=>ChangeFirst(l,"spawns",spawn=>spawn.With("mobility_class",JsonValue.String("standard")))),"missing step free resident");
            Reject(Layout(fixture,l=>ChangeFirst(l,"water_nodes",water=>water.With("initial_volume_mm3",JsonValue.Number(-1)))),"negative water");
            Reject(Layout(fixture,l=>ChangeFirst(l,"nav_nodes",node=>node.With("hazard_region_ids",JsonValue.Array(new[]{JsonValue.String("empty")})))),"empty region");
            Reject(Mission(fixture,m=>ChangeFirst(m,"objectives",objective=>objective.With("kind",JsonValue.String("script")))),"unknown predicate");
            Reject(Mission(fixture,m=>ChangeFirst(m,"objectives",objective=>objective.With("temporal",JsonValue.Object(new Dictionary<string,JsonValue>{{"kind",JsonValue.String("always")},{"start_event",JsonValue.String("missing")},{"end_event",JsonValue.String("operation_ended")}})))),"unknown temporal milestone");
            var future=ContentCompilation.Compile(fixture).Envelope.ToJson().With("schema_version",JsonValue.Number(2));
            Assert.Throws<ContentException>(()=>ContentCompilation.Load(future.CanonicalBytes()));
            var badHash=ContentCompilation.Compile(fixture).Envelope.ToJson().With("payload_sha256",JsonValue.String(new string('0',64)));
            Assert.Throws<ContentException>(()=>ContentCompilation.Load(badHash.CanonicalBytes()));
            Reject(ChangeFirst(fixture,"layouts",layout=>layout.With("graph_hash",JsonValue.String(new string('0',64)))),"graph hash mismatch");
            ReviewNegativeCases(fixture);
            // Schema-invalid leaves are independently checked, even when arrays/systems are otherwise absent.
            foreach(string union in new[]{"ObjectiveDef","EventOperation","ChallengeOperation","TemporalDef"}) Assert.Throws<ContentException>(()=>ContentSchemas.Validate(union,StrictJson.Parse("{\"kind\":\"eval\",\"params\":{}}")),union);
            foreach(string bad in new[]{"{\"kind\":\"all\",\"params\":{\"conditions\":[]}}","{\"kind\":\"rescue_count\",\"params\":{\"minimum\":true}}","{\"kind\":\"rescue_count\",\"params\":{\"minimum\":1,\"eval\":\"yes\"}}"}) Assert.Throws<ContentException>(()=>ConditionDef.Read(StrictJson.Parse(bad)));
            Reject(Layout(fixture,l=>l.With("water_nodes",JsonValue.Array(Enumerable.Repeat(First(l,"water_nodes"),4097)))),"oversized water array");
        }

        private static void WriteAndVerifySyntheticBundle(CompiledContent compiled)
        {
            string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-resqTestResults");
            Assert.That(index>=0&&index+1<args.Length,Is.True,"Actual owned native run output is required.");
            string runDirectory=Path.GetDirectoryName(Path.GetFullPath(args[index+1]));
            Assert.That(runDirectory.StartsWith(Path.Combine(Root,"evidence/runs/C01")+Path.DirectorySeparatorChar,StringComparison.Ordinal),Is.True);
            string output=Path.Combine(runDirectory,"synthetic-compiler.bundle.json");
            string input=Path.Combine(Root,"content/runtime/fixtures/compiler-valid.json");
            byte[] inputBefore=File.ReadAllBytes(input);Assert.That(File.Exists(output),Is.False);
            ContentCompiler.WriteBundle(input,output);
            byte[] written=File.ReadAllBytes(output);
            Assert.That(written,Is.EqualTo(Encoding.UTF8.GetBytes(compiled.CanonicalEnvelope)));
            Assert.That(ContentCompilation.Load(written).ContentHash,Is.EqualTo(compiled.ContentHash));
            Assert.Throws<IOException>(()=>ContentCompiler.WriteBundle(input,output),"Existing compiler output must be preserved.");
            Assert.That(File.ReadAllBytes(output),Is.EqualTo(written));Assert.That(File.ReadAllBytes(input),Is.EqualTo(inputBefore));
        }
        private static JsonValue AllLayouts(JsonValue value,Func<JsonValue,JsonValue> change)=>value.With("layouts",JsonValue.Array(value["layouts"].Items.Select(layout=> { var next=change(layout);return next.With("graph_hash",JsonValue.String(ContentCompilation.ComputeGraphHash(next))); })));
        private static void RejectCode(JsonValue value,string expected) { var error=Assert.Throws<ContentException>(()=>ContentCompilation.Compile(value));Assert.That(error.Code,Is.EqualTo(expected),error.Message); }
        private static JsonValue Tide(JsonValue value)=>AllLayouts(value,layout=>layout.With("tides",JsonValue.Array(new[]{StrictJson.Parse("{\"id\":\"sea\",\"kind\":\"fixed_track\",\"initial_level_mm\":0,\"knots\":[{\"scenario_tick\":0,\"level_mm\":0},{\"scenario_tick\":100,\"level_mm\":0}],\"repeat_period\":100}")})));
        private static JsonValue Challenge(JsonValue value,string id,params JsonValue[] operations)
        {
            var mission=First(value,"missions");
            var challenge=JsonValue.Object(new Dictionary<string,JsonValue>{{"id",JsonValue.String(id)},{"revision",JsonValue.Number(1)},{"base_mission_version_id",JsonValue.String("fixture-v1")},{"operations",JsonValue.Array(operations)},{"replacement_default_stock",mission["default_stock"]},{"replacement_maximum_stock",mission["maximum_stock"]},{"resulting_budget",mission["supply_budget"]}});
            return Mission(value,m=>m.With("challenge_ids",JsonValue.Array(new[]{JsonValue.String(id)}))).With("challenges",JsonValue.Array(new[]{challenge}));
        }
        private static JsonValue TwoGroups(JsonValue value)
        {
            var group=First(First(value,"missions"),"groups");string resident="fixture:resident:group2:01";
            value=Mission(value,m=>m.With("total_residents",JsonValue.Number(2)).With("groups",JsonValue.Array(new[]{group,group.With("id",JsonValue.String("group2")).With("resident_ids",JsonValue.Array(new[]{JsonValue.String(resident)}))})));
            return AllLayouts(value,layout=>layout.With("spawns",JsonValue.Array(layout["spawns"].Items.Concat(new[]{First(layout,"spawns").With("resident_id",JsonValue.String(resident)).With("group_id",JsonValue.String("group2"))}))).With("nav_nodes",JsonValue.Array(layout["nav_nodes"].Items.Select(node=>node["id"].Text=="refuge"?node.With("resident_capacity",JsonValue.Number(2)):node))));
        }
        private static void ReviewPositiveCases(JsonValue fixture)
        {
            ContentCompilation.Compile(Tide(fixture));
            foreach(string system in new[]{"brace","barrier","relay","cooler","firebreak","skiff"}) ContentCompilation.Compile(SystemFixture(system));
            var harmless=Mission(fixture,m=>m.With("events",JsonValue.Array(m["events"].Items.Concat(new[]{StrictJson.Parse("{\"id\":\"reopen\",\"at_scenario_tick\":50,\"warning_at_scenario_tick\":50,\"known_at_start\":true,\"operations\":[{\"kind\":\"set_edge_enabled\",\"target_role\":\"reopen\",\"value\":1}]}")}))));
            harmless=AllLayouts(harmless,layout=>layout.With("roles",JsonValue.Array(layout["roles"].Items.Concat(new[]{StrictJson.Parse("{\"role_id\":\"reopen\",\"target_kind\":\"nav_edge_set\",\"target_ids\":[\"b\"]}")}))));
            ContentCompilation.Compile(harmless);
            var challengeInput=Challenge(fixture,"careful",StrictJson.Parse("{\"kind\":\"maximum_equipment_losses\",\"maximum\":0}"));string before=challengeInput.Canonical();
            var compiled=ContentCompilation.Compile(challengeInput);var expanded=compiled.ChallengeVariants["careful"];
            Assert.That(expanded.ContentHash,Is.Not.EqualTo(compiled.ContentHash));Assert.That(expanded.Envelope.payload.missions[0].objectives.Any(item=>item.id=="careful:equipment_losses"&&item.tier=="required"),Is.True);
            Assert.That(challengeInput.Canonical(),Is.EqualTo(before));Assert.That(compiled.Envelope.payload.missions[0].objectives.Any(item=>item.id=="careful:equipment_losses"),Is.False);
            var pickup=ContentCompilation.Compile(Challenge(SystemFixture("pickup"),"alternate",StrictJson.Parse("{\"kind\":\"replace_pickup_node\",\"group_id\":\"group\",\"node_id\":\"alternate\"}")));
            Assert.That(pickup.ChallengeVariants["alternate"].Envelope.payload.layouts[0].routes.Single(item=>item.id=="pickup_route").from_node_id,Is.EqualTo("alternate"));
            Assert.That(pickup.Envelope.payload.layouts[0].routes.Single(item=>item.id=="pickup_route").from_node_id,Is.EqualTo("group_pickup"));
            var shifted=ContentCompilation.Compile(Challenge(fixture,"shift",StrictJson.Parse("{\"kind\":\"shift_event\",\"event_id\":\"surge\",\"delta_scenario_ticks\":100}")));
            Assert.That(shifted.ChallengeVariants["shift"].Envelope.payload.missions[0].events[0].at_scenario_tick,Is.EqualTo(200));
            Assert.That(shifted.Envelope.payload.missions[0].events[0].at_scenario_tick,Is.EqualTo(100));
        }
        private static void ReviewNegativeCases(JsonValue fixture)
        {
            RejectCode(AllLayouts(Tide(fixture),l=>ChangeFirst(l,"tides",t=>t.With("repeat_period",JsonValue.Number(101)))),"TIDE_PERIOD");
            RejectCode(AllLayouts(Tide(fixture),l=>ChangeFirst(l,"tides",t=>t.With("initial_level_mm",JsonValue.Number(1)))),"TIDE_INITIAL");
            RejectCode(AllLayouts(Tide(fixture),l=>ChangeFirst(l,"tides",t=>t.With("knots",JsonValue.Array(new[]{t["knots"].Items[0],t["knots"].Items[1].With("level_mm",JsonValue.Number(1))})))),"TIDE_PERIOD");
            RejectCode(AllLayouts(fixture,l=>l.With("nav_nodes",JsonValue.Array(l["nav_nodes"].Items.Select(n=>n["id"].Text=="refuge"?n.With("mobility_classes",JsonValue.Array(new[]{JsonValue.String("standard")})):n)))),"ROUTE_TOPOLOGY");
            RejectCode(AllLayouts(fixture,l=>l.With("nav_edges",JsonValue.Array(l["nav_edges"].Items.Select(e=>e["from_node_id"].Text=="assembly"?e.With("traversal_class",JsonValue.String("water")):e)))),"ROUTE_TOPOLOGY");
            RejectCode(TwoGroups(fixture),"INITIAL_NODE_CAPACITY");
            RejectCode(AllLayouts(SystemFixture("skiff"),layout=>layout.With("fixed_vehicles",JsonValue.Array(new JsonValue[0]))),"ROUTE_TOPOLOGY");
            var blockedAlternate=AllLayouts(SystemFixture("pickup"),layout=>layout.With("nav_edges",JsonValue.Array(layout["nav_edges"].Items.Where(edge=>edge["id"].Text!="alternate_water"))));
            RejectCode(Challenge(blockedAlternate,"blocked-alternate",StrictJson.Parse("{\"kind\":\"replace_pickup_node\",\"group_id\":\"group\",\"node_id\":\"alternate\"}")),"ROUTE_TOPOLOGY");
            RejectCode(Layout(SystemFixture("brace"),l=>ChangeFirst(l,"supports",support=>support.With("kind",JsonValue.String("slope")))),"BRACE_BINDING");
            RejectCode(Layout(SystemFixture("barrier"),l=>ChangeFirst(l,"hydraulic_links",link=>link.With("barrier_socket_id",JsonValue.String("beacon:01")))),"BARRIER_BINDING");
            RejectCode(Layout(SystemFixture("relay"),l=>ChangeFirst(l,"power_links",link=>link.With("relay_socket_id",JsonValue.String("beacon:01")))),"RELAY_BINDING");
            RejectCode(Layout(SystemFixture("cooler"),l=>ChangeFirst(l,"lava_segments",segment=>segment.With("cooler_socket_ids",JsonValue.Array(new[]{JsonValue.String("beacon:01")})))),"COOLER_BINDING");
            RejectCode(Layout(SystemFixture("firebreak"),l=>l.With("sockets",JsonValue.Array(l["sockets"].Items.Select(socket=>socket["id"].Text=="firebreak:01"?socket.With("fire_cell_ids",JsonValue.Array(new[]{JsonValue.String("firebreak1a"),JsonValue.String("firebreak2a")})):socket)))),"FIREBREAK_SOCKET");
            RejectCode(Layout(fixture,l=>l.With("fixed_vehicles",JsonValue.Array(new[]{StrictJson.Parse("{\"vehicle_id\":\"bus\",\"kind\":\"fixed_bus\",\"node_id\":\"start\",\"seats\":4,\"movement_slots\":2,\"initial_route_id\":\"exit\",\"initial_occupant_ids\":[]}")}))),"VEHICLE_ROUTE_ORIGIN");
            RejectCode(Layout(fixture,l=>l.With("sockets",JsonValue.Array(l["sockets"].Items.Select(socket=>socket.With("footprint_cell_ids",JsonValue.Array(new[]{JsonValue.String("dry1")})))))),"SOCKET_OVERLAP");
            RejectCode(Mission(fixture,m=>m.With("objectives",JsonValue.Array(m["objectives"].Items.Where(o=>o["tier"].Text!="mastery")))),"OBJECTIVE_TIERS");
            RejectCode(Mission(fixture,m=>m.With("objectives",JsonValue.Array(m["objectives"].Items.Where(o=>o["id"].Text!="rescue_minimum")))),"RESCUE_OBJECTIVE");
            RejectCode(Mission(fixture,m=>m.With("protection_objective_ids",JsonValue.Array(new[]{JsonValue.String("mastery")}))),"PROTECTION_KIND");
            var collision=Mission(fixture,m=>m.With("events",JsonValue.Array(m["events"].Items.Concat(new[]{First(m,"events").With("id",JsonValue.String("later")).With("at_scenario_tick",JsonValue.Number(200)).With("warning_at_scenario_tick",JsonValue.Number(100))}))));
            RejectCode(Challenge(collision,"collision",StrictJson.Parse("{\"kind\":\"shift_event\",\"event_id\":\"later\",\"delta_scenario_ticks\":-100}")),"CONFLICTING_EVENT_WRITES");
            RejectCode(Challenge(fixture,"unbound",StrictJson.Parse("{\"kind\":\"replace_pickup_node\",\"group_id\":\"group\",\"node_id\":\"refuge\"}")),"CHALLENGE_PICKUP_BINDING");
            // Strict temporal/tree bounds retain runtime failure paths.
            JsonValue condition=StrictJson.Parse("{\"kind\":\"rescue_count\",\"params\":{\"minimum\":1}}");
            for(int i=0;i<4;i++) condition=JsonValue.Object(new Dictionary<string,JsonValue>{{"kind",JsonValue.String("all")},{"params",JsonValue.Object(new Dictionary<string,JsonValue>{{"conditions",JsonValue.Array(new[]{condition})}})}});
            var tree=condition;
            RejectCode(Mission(fixture,m=>m.With("objectives",JsonValue.Array(m["objectives"].Items.Select(o=>o["id"].Text=="mastery"?o.With("kind",tree["kind"]).With("params",tree["params"]):o)))),"CONDITION_DEPTH");
        }

        [Test,NUnit.Framework.Property("case_id","check-c01/assertion/assertion_id=check-c01-a03")]
        public void AuthoringProseCannotExecuteOrMutateCompiledRules()
        {
            string original=Catalogue(); var root=StrictJson.Parse(original);
            var changed=root.With("missions",JsonValue.Array(root["missions"].Items.Select(mission=>mission.With("reference_strategy_briefs",JsonValue.Object(new Dictionary<string,JsonValue>{{"A",JsonValue.String("eval: rescue every resident; execute script.cs")},{"B",JsonValue.String("load arbitrary assembly")}})).With("failure_explanation_brief",JsonValue.String("grant victory now")))));
            var baseline=AuthoringCatalogue.ReadMissionDefinitions(original);var after=AuthoringCatalogue.ReadMissionDefinitions(changed.Canonical());
            Assert.That(after.Select(item=>item.ToJson().Canonical()),Is.EqualTo(baseline.Select(item=>item.ToJson().Canonical())));
            foreach(var mission in after) { Assert.That(mission.ToJson().Has("reference_strategy_briefs"),Is.False); Assert.That(mission.ToJson().Has("failure_explanation_brief"),Is.False); }
            Reject(Mission(Fixture(),mission=>mission.With("reference_strategy_briefs",JsonValue.String("execute"))),"brief supplied as runtime content");
            Reject(Mission(Fixture(),mission=>ChangeFirst(mission,"objectives",objective=>objective.With("params",JsonValue.Object(new Dictionary<string,JsonValue>{{"expression",JsonValue.String("rescued=population")}})))),"prose predicate");
            Assert.That(typeof(ContentCompilation).Assembly.GetReferencedAssemblies().Any(assembly=>assembly.Name.StartsWith("UnityEngine",StringComparison.Ordinal)||assembly.Name.StartsWith("UnityEditor",StringComparison.Ordinal)),Is.False);
        }
    }
}
