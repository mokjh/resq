using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Progression;
using Resq.Simulation;
using Resq.Simulation.Objectives;
using Resq.Simulation.Rescue;
using UnityEngine;
namespace Resq.Tests.RuntimeObjectives
{
    [TestFixture]
    public sealed class RuntimeObjectiveTests
    {
        private static string Root => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        private static JsonValue J(string text) => StrictJson.Parse(text);
        private static JsonValue O(params object[] pairs) { var d = new Dictionary<string, JsonValue>(StringComparer.Ordinal); for (int i=0; i<pairs.Length; i+=2) { var v=pairs[i+1]; d.Add((string)pairs[i],v==null?JsonValue.Null():v is JsonValue j?j:v is string s?JsonValue.String(s):v is bool b?JsonValue.Bool(b):JsonValue.Number(Convert.ToInt64(v))); } return JsonValue.Object(d); }
        private static JsonValue A(params string[] xs) => JsonValue.Array(xs.Select(JsonValue.String));
        private static JsonValue Payload() => J(File.ReadAllText(Path.Combine(Root,"content/runtime/fixtures/compiler-valid.json")));
        private static JsonValue Golden(string id) => J(File.ReadAllText(Path.Combine(Root,"specification/fixtures/runtime-golden-v1.json")))["cases"].Items.Single(x=>x["id"].Text==id);
        private static JsonValue Def(string id,string tier,string kind,JsonValue p,JsonValue temporal=null) => O("id",id,"tier",tier,"kind",kind,"params",p,"temporal",temporal??O("kind","at_end"));
        private sealed class Rig
        {
            public CompiledMission Mission; public LayoutDef Layout; public ObjectiveSystem System; public JsonValue State;
            public readonly RuntimeEventSink Events = new RuntimeEventSink(); public long OTick=-1, STick;
            public readonly Dictionary<string,bool> Infrastructure = new Dictionary<string,bool>(StringComparer.Ordinal);
            public readonly Dictionary<string,bool> Routes = new Dictionary<string,bool>(StringComparer.Ordinal);
            public readonly Dictionary<string,long> Exposures = new Dictionary<string,long>(StringComparer.Ordinal);
            public readonly List<JsonValue> Marks = new List<JsonValue>(); public CompletedEdgePassage[] Passages = new CompletedEdgePassage[0]; public DockWaitSample[] Waits = new DockWaitSample[0];
            public ObjectiveObservations Facts() => new ObjectiveObservations(OTick,Infrastructure,Routes,Exposures,Passages,Waits,new CompletedNodeDwell[0],Layout.nav_nodes.ToDictionary(x=>x.id,x=>(long)State["residents"].Items.Count(p=>p["location"]["kind"].Text=="node"&&p["location"]["node_id"].Text==x.id),StringComparer.Ordinal));
            public void Mark(string id) { Marks.Add(RuntimeObjectiveTests.O("id",id,"operation_tick",OTick,"scenario_tick",STick)); Events.Emit("MilestoneReached",id,null,null,RuntimeObjectiveTests.O("milestone_id",id)); }
            public void Step(bool pulse=true, string mark=null, Action<OperationContext> emit=null)
            { OTick++; if(OTick>0&&pulse) STick++; Events.BeginBoundary(OTick,STick); var c=new OperationContext(OTick,STick,OTick>0&&pulse,Events); if(mark!=null) Mark(mark); emit?.Invoke(c); System.Sample(State,Facts(),Marks,c); Passages=new CompletedEdgePassage[0]; }
            public OutcomeResult End() { Mark("operation_ended"); var c=new OperationContext(OTick,STick,false,Events); System.Sample(State,Facts(),Marks,c,true); return System.Outcome(State); }
            public void Rescue(int count,string mode="ground") { State=State.With("residents",JsonValue.Array(State["residents"].Items.Select((x,i)=>i<count?x.With("state",JsonValue.String("evacuated")).With("location",RuntimeObjectiveTests.O("kind","node","node_id","refuge")).With("final_leg_mode",JsonValue.String(mode)):x))); }
        }
        private static Rig Build(IEnumerable<JsonValue> definitions=null,int population=10)
        {
            var payload=Payload();var source=payload["missions"].Items[0];
            var defs=definitions?.ToList()??new List<JsonValue>{Def("required","required","rescue_count",O("minimum",1)),Def("critical","critical","rescue_count",O("minimum",population)),Def("mastery","mastery","equipment_losses_at_most",O("maximum",0))};
            if(!defs.Any(x=>x["tier"].Text=="required"))defs.Add(Def("required","required","rescue_count",O("minimum",0)));
            if(!defs.Any(x=>x["tier"].Text=="critical"))defs.Add(Def("critical","critical","rescue_count",O("minimum",population)));
            var ids=Enumerable.Range(1,population).Select(i=>"fixture:resident:group:"+i.ToString("00")).ToArray();
            var mission=CompiledMission.Read(source.With("total_residents",JsonValue.Number(population)).With("minimum_rescues",JsonValue.Number(1)).With("groups",JsonValue.Array(new[]{O("id","group","resident_ids",A(ids),"start_node_id","start","assembly_node_id","assembly","initially_held",false)})).With("objectives",JsonValue.Array(defs)).With("protection_objective_ids",A()).With("milestones",J("[]")));
            var layout=LayoutDef.Read(payload["layouts"].Items.Single(x=>x["id"].Text=="fixture-wide"));
            var state=O("residents",JsonValue.Array(ids.Select(id=>O("id",id,"group_id","group","state","waiting","location",O("kind","node","node_id","start"),"reservation_ids",A(),"node_entered_scenario_tick",0,"consecutive_exposure_ticks",0,"final_leg_mode",null))),"vehicles",J("[]"),"equipment",J("[]"),"deployments",J("[]"),"supplies_remaining",1,"stores",J("[]"),"water",J("[]"));
            return new Rig{Mission=mission,Layout=layout,State=state,System=new ObjectiveSystem(mission,layout)};
        }
        private static bool Fulfilled(Rig r,string id)=>r.System.Monitors.Items.Single(x=>x["id"].Text==id)["fulfilled"].Boolean;
        [Test, Category("runtime_objectives"), NUnit.Framework.Property("case_id","check-c06/assertion/assertion_id=check-c06-a01")]
        public void AllTwentyFiveTypedDefinitionsAndClosedConditions()
        {
            var missions=ObjectiveCatalogue.ReadMissionDefinitions(File.ReadAllText(Path.Combine(Root,"specification/content/mission_contracts.json")));
            Assert.AreEqual(25,missions.Count); var template=Payload()["layouts"].Items[0];
            foreach(var mission in missions)
            {
                var layout=LayoutDef.Read(template.With("mission_version_id",JsonValue.String(mission.mission_version_id)));
                var system=new ObjectiveSystem(mission,layout); Assert.AreEqual(mission.objectives.Count,system.Monitors.Items.Count);
                foreach(var objective in mission.objectives) Assert.AreEqual(objective.ToJson().Canonical(),ObjectiveDef.Read(objective.ToJson()).ToJson().Canonical());
            }
            Assert.Throws<ContentException>(()=>ObjectiveDef.Read(Def("bad","required","rescue_count",O("minimum",0,"eval","true"))));
            var r=Build();r.Step(mark:"operation_started"); r.Rescue(2);
            var all=O("conditions",JsonValue.Array(new[]{O("kind","rescue_count","params",O("minimum",2)),O("kind","scenario_elapsed_at_most","params",O("maximum",0))}));
            Assert.IsTrue(r.System.Evaluate("all",all,r.State,r.Facts(),0));Assert.IsFalse(r.System.Evaluate("all",all,r.State,r.Facts(),1));
            Assert.IsTrue(r.System.Evaluate("any",all,r.State,r.Facts(),1));
            r.Exposures[ObjectiveObservations.ExposureKey("region","water")]=100;
            Assert.IsFalse(r.System.Evaluate("exposure_below",O("region_id","region","hazard","water","threshold",100),r.State,r.Facts(),0));
            Assert.IsTrue(r.System.Evaluate("exposure_below",O("region_id","region","hazard","water","threshold",101),r.State,r.Facts(),0));
            Assert.Throws<KeyNotFoundException>(()=>r.System.Evaluate("route_available",O("route_id","missing","traversal_class","ground"),r.State,r.Facts(),0));
        }
        [Test, Category("runtime_objectives"), NUnit.Framework.Property("case_id","check-c06/assertion/assertion_id=check-c06-a02")]
        public void CriticalMasteryStarsAndTerminalSafety()
        {
            foreach(int rescued in new[]{8,9,10})
            {
                var defs=new[]{Def("required","required","rescue_count",O("minimum",1)),Def("critical","critical","rescue_count",O("minimum",9)),Def("mastery","mastery","equipment_losses_at_most",O("maximum",0))};
                var r=Build(defs);r.Step(mark:"operation_started");r.Rescue(rescued);r.Step();var result=r.End();Assert.IsTrue(result.Success);Assert.AreEqual(rescued==10?3:rescued==9?2:1,result.Stars);
            }
            var failed=Build();failed.Step(mark:"operation_started"); Assert.IsFalse(failed.End().Success);Assert.AreEqual(0,failed.System.Outcome(failed.State).Stars);
            var occupied=Build();occupied.Rescue(10);occupied.State=occupied.State.With("vehicles",JsonValue.Array(new[]{O("id","failed","occupant_ids",A("fixture:resident:group:01"))}));occupied.Step(mark:"operation_started");Assert.IsFalse(occupied.End().Success,"Failed occupied vehicle cannot be cleaned into success");
            var shelter=Build();var node=shelter.Layout.nav_nodes.Single(x=>x.id=="start");shelter.Layout=LayoutDef.Read(shelter.Layout.ToJson().With("nav_nodes",JsonValue.Array(shelter.Layout.nav_nodes.Select(x=>x.id=="start"?x.ToJson().With("kind",JsonValue.String("shelter")):x.ToJson()))));shelter.System=new ObjectiveSystem(shelter.Mission,shelter.Layout);shelter.Rescue(9);shelter.Step(mark:"operation_started");Assert.IsFalse(shelter.End().Success);
            var protect=Build();protect.Mission=CompiledMission.Read(protect.Mission.ToJson().With("protection_objective_ids",A("critical")));protect.System=new ObjectiveSystem(protect.Mission,protect.Layout);protect.Rescue(1);protect.Step(mark:"operation_started");var outcome=protect.End();Assert.IsTrue(outcome.Success);Assert.AreEqual(1,outcome.Stars);Assert.Less(outcome.Score,TerminalScore.Compute(1,10,1,1,0,0,1,1),"Protection fraction is explicit rather than critical fallback");
        }
        [Test, Category("runtime_objectives"), NUnit.Framework.Property("case_id","check-c06/assertion/assertion_id=check-c06-a03")]
        public void TemporalBoundariesAndExactRationalScore()
        {
            var golden=Golden("RJ01-strict-before"); foreach(var first in golden["input"]["first_true_operation_ticks"].Items)
            {
                var r=Build(new[]{Def("strict","mastery","supply_remaining_at_least",O("minimum",1),O("kind","ever_before","end_event","close"))});r.State=r.State.With("supplies_remaining",JsonValue.Number(0));r.Step(mark:"operation_started");
                for(int i=1;i<=10;i++){if(i==first.Integer)r.State=r.State.With("supplies_remaining",JsonValue.Number(1));r.Step(mark:i==10?"close":null);}r.End();Assert.AreEqual(first.Integer<10,Fulfilled(r,"strict"));
            }
            var future=Build(new[]{Def("strict","mastery","rescue_count",O("minimum",1),O("kind","ever_before","end_event","future"))});future.Rescue(1);future.Step(mark:"operation_started");Assert.IsTrue(future.End().Success);Assert.IsTrue(Fulfilled(future,"strict"),"RJ05 ending before future closure");
            var temporal=new[]{Def("inclusive","mastery","rescue_count",O("minimum",1),O("kind","ever_by","deadline_tick",2)),Def("always","mastery","supply_remaining_at_least",O("minimum",1),O("kind","always","start_event","operation_started","end_event","close")),Def("hold","mastery","supply_remaining_at_least",O("minimum",1),O("kind","hold_for","scenario_ticks",2,"start_event","operation_started","end_event","close")),Def("absent","mastery","rescue_count",O("minimum",0),O("kind","always","start_event","missing","end_event","operation_ended"))};
            var t=Build(temporal);t.Step(mark:"operation_started");t.Step();t.State=t.State.With("supplies_remaining",JsonValue.Number(0));t.Step(false);t.State=t.State.With("supplies_remaining",JsonValue.Number(1));t.Rescue(1);t.Step();Assert.IsTrue(Fulfilled(t,"inclusive"));t.Step();t.Step(mark:"close");t.End();Assert.IsFalse(Fulfilled(t,"always"));Assert.IsTrue(Fulfilled(t,"hold"));Assert.IsFalse(Fulfilled(t,"absent"));
            var late=Build(new[]{Def("late","mastery","rescue_count",O("minimum",1),O("kind","ever_by","deadline_tick",1))});late.Step(mark:"operation_started");late.Step();late.Rescue(1);late.Step();late.End();Assert.IsFalse(Fulfilled(late,"late"));
            var startZero=Build(new[]{Def("hold","mastery","rescue_count",O("minimum",0),O("kind","hold_for","scenario_ticks",1,"start_event","operation_started","end_event","close"))});startZero.Step(mark:"operation_started");Assert.AreEqual(0,startZero.System.Monitors.Items.Single(x=>x["id"].Text=="hold")["current_hold_scenario_ticks"].Integer);startZero.Mark("close");startZero.End();Assert.IsFalse(Fulfilled(startZero,"hold"));
            Assert.AreEqual(6396,TerminalScore.Compute(9,12,2,3,1,2,1,16));Assert.AreEqual(10000,TerminalScore.Compute(long.MaxValue,1,long.MaxValue,1,long.MaxValue,1,long.MaxValue,1));
            Assert.AreEqual(3001,TerminalScore.Compute(0,1,1,1,0,0,1,2000),"Half score rounds upward once");
            var prospective=Build(new[]{Def("terminal_guard","required","supply_remaining_at_least",O("minimum",1),O("kind","always","start_event","operation_started","end_event","operation_ended"))});prospective.Rescue(1);prospective.Step(mark:"operation_started");Assert.IsTrue(prospective.System.CanEndSuccessfully(prospective.State,prospective.Facts(),0),"End admission may close a satisfied always obligation without pre-latching operation_ended");Assert.IsFalse(Fulfilled(prospective,"terminal_guard"));Assert.IsTrue(prospective.End().Success);
            var continuity=Build(new[]{Def("hold","mastery","supply_remaining_at_least",O("minimum",1),O("kind","hold_for","scenario_ticks",1,"start_event","operation_started","end_event","close"))});continuity.Step(mark:"operation_started");continuity.Step();continuity.State=continuity.State.With("supplies_remaining",JsonValue.Number(0));continuity.Step(false);var uninterrupted=continuity.System.Fork();continuity.System=continuity.System.Restore(continuity.System.Metrics,continuity.System.Monitors,continuity.OTick,continuity.STick,continuity.State,continuity.Facts(),continuity.Marks);continuity.State=continuity.State.With("supplies_remaining",JsonValue.Number(1));continuity.Step();var sink=new RuntimeEventSink();sink.BeginBoundary(continuity.OTick,continuity.STick);uninterrupted.Sample(continuity.State,continuity.Facts(),continuity.Marks,new OperationContext(continuity.OTick,continuity.STick,true,sink));Assert.AreEqual(uninterrupted.Monitors.Canonical(),continuity.System.Monitors.Canonical());Assert.AreEqual(0,continuity.System.Monitors.Items.Single(x=>x["id"].Text=="hold")["current_hold_scenario_ticks"].Integer,"First true pulse after false nonpulse does not recover prior interval");continuity.Step(mark:"close");continuity.End();Assert.IsTrue(Fulfilled(continuity,"hold"));
            ClockLastTickArrival();
        }
        [Test, Category("runtime_objectives"), NUnit.Framework.Property("case_id","check-c06/assertion/assertion_id=check-c06-a04")]
        public void CausalCountersRestoreDebriefAndCampaignRewards()
        {
            var metrics=new[]{Def("edge","mastery","counter_at_least",O("metric","edge_transfers","edge_tag","fixture_path","minimum",1)),Def("pump","optional","counter_at_least",O("metric","pumped_into_region_mm3","region_id","dry","minimum",1)),Def("generator","optional","counter_at_most",O("metric","generator_shutdowns","maximum",1)),Def("shelter","optional","counter_at_most",O("metric","shelter_exhaustions","maximum",1)),Def("dwell","optional","counter_at_most",O("metric","max_node_dwell_scenario_ticks","node_id","start","maximum",1000000)),Def("peak","optional","counter_at_least",O("metric","peak_node_occupancy","node_id","start","minimum",1)),Def("deploy","optional","counter_at_least",O("metric","equipment_deployments","equipment_id","beacon","minimum",1)),Def("mode","optional","counter_at_least",O("metric","evacuated_by_mode","group_id",null,"mode","water","minimum",1)),Def("dock","optional","counter_at_most",O("metric","max_dock_queue_operation_ticks","node_id","refuge","maximum",5)),Def("closed","optional","counter_at_most",O("metric","loaded_closed_dock_wait_operation_ticks","maximum",5)),Def("station","optional","counter_at_most",O("metric","station_shutdowns","maximum",1))};
            var r=Build(metrics);var station=O("id","station","kind","station","power_node_id",null,"initial_demand",0,"initial_priority",0,"required_nav_node_ids",A(),"required_support_ids",A(),"required_equipment_socket_ids",A(),"hazard_region_ids",A(),"maximum_depth_mm",100,"maximum_heat_points",600);
            r.Layout=LayoutDef.Read(r.Layout.ToJson().With("infrastructure",JsonValue.Array(new[]{station})));r.System=new ObjectiveSystem(r.Mission,r.Layout);r.Infrastructure["station"]=false;
            r.State=r.State.With("vehicles",JsonValue.Array(new[]{O("id","boat","occupant_ids",A())})).With("deployments",JsonValue.Array(new[]{O("equipment_id","beacon","count",1)}));
            r.Step(mark:"operation_started");r.Infrastructure["station"]=true;r.Waits=new[]{new DockWaitSample("refuge","boat",true,true,true)};r.Passages=new[]{new CompletedEdgePassage("fixture:resident:group:01","a")};r.Step();
            long maxFirst=MetricValue(r,"max_dock_queue_operation_ticks");Assert.AreEqual(0,maxFirst,"First actual waiting boundary creates zero elapsed wait");Assert.AreEqual(0,MetricValue(r,"loaded_closed_dock_wait_operation_ticks"));
            r.Infrastructure["station"]=false;r.Step(emit:c=>{c.Events.Emit("HazardStateChanged","generator",null,null,O("hazard","power","subject_id","generator","old_state","active","new_state","safety_tripped"));c.Events.Emit("HazardStateChanged","shelter",null,null,O("hazard","resource","subject_id","shelter","old_state","protected","new_state","exhausted"));});
            Assert.AreEqual(1,MetricValue(r,"station_shutdowns"));Assert.AreEqual(1,MetricValue(r,"max_dock_queue_operation_ticks"));Assert.AreEqual(1,MetricValue(r,"loaded_closed_dock_wait_operation_ticks"));
            var savedMetrics=r.System.Metrics;var savedMonitors=r.System.Monitors;
            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(JsonValue.Array(savedMetrics.Items.Skip(1)),savedMonitors,r.OTick,r.STick,r.State,r.Facts(),r.Marks));
            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(savedMetrics,JsonValue.Array(savedMonitors.Items.Select(x=>x.With("fulfilled",JsonValue.Bool(true)))),r.OTick,r.STick,r.State,r.Facts(),r.Marks),"Forged preterminal at-end success rejected");
            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(JsonValue.Array(savedMetrics.Items.Select(x=>x["metric"].Text=="objective_station_activation"?x.With("value",JsonValue.Number(1)):x)),savedMonitors,r.OTick,r.STick,r.State,r.Facts(),r.Marks),"Station current truth binds activation state");

            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(JsonValue.Array(savedMetrics.Items.Select(x=>x["metric"].Text=="peak_node_occupancy"?x.With("value",JsonValue.Number(0)):x)),savedMonitors,r.OTick,r.STick,r.State,r.Facts(),r.Marks));
            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(JsonValue.Array(savedMetrics.Items.Select(x=>x["metric"].Text=="max_dock_queue_operation_ticks"?x.With("value",JsonValue.Number(0)):x)),savedMonitors,r.OTick,r.STick,r.State,r.Facts(),r.Marks));
            var restored=r.System.Restore(savedMetrics,savedMonitors,r.OTick,r.STick,r.State,r.Facts(),r.Marks);Assert.AreEqual(savedMetrics.Canonical(),restored.Metrics.Canonical());r.System=restored;
            r.Waits=new[]{new DockWaitSample("refuge","boat",true,true,false)};r.Step();Assert.AreEqual(2,MetricValue(r,"max_dock_queue_operation_ticks"),"Closed edge-only wait preserves completed final berth/capacity interval");Assert.AreEqual(2,MetricValue(r,"loaded_closed_dock_wait_operation_ticks"));r.Waits=new DockWaitSample[0];
            // A real hydraulic allocation supplies the event; destination-region
            // counter consumes the actual grant, not a requested or expected grant.
            var waterLayout=r.Layout.ToJson();var socket=waterLayout["sockets"].Items[0].With("id",JsonValue.String("pump-site")).With("permitted_equipment_ids",A("pump")).With("intake_node_id",JsonValue.String("water")).With("outlet_node_id",JsonValue.String("dry1")).With("power_node_id",JsonValue.String("power"));
            waterLayout=waterLayout.With("sockets",JsonValue.Array(new[]{socket})).With("water_nodes",JsonValue.Array(waterLayout["water_nodes"].Items.Select(x=>x["id"].Text=="water"?x.With("initial_volume_mm3",JsonValue.Number(1000000000)):x)));
            var water=new WaterSystem(LayoutDef.Read(waterLayout));var pulse=water.Pulse(new[]{new WaterPump("pump","pump-site",true,true)},new WaterSprinkler[0],new Dictionary<string,long>(),new Dictionary<string,bool>(),new string[0]);
            long granted=pulse.Grants.Single(x=>x.Kind=="pump").GrantedMm3;Assert.Greater(granted,0);r.Step(emit:c=>pulse.EmitTransfers(c));Assert.AreEqual(granted,MetricValue(r,"pumped_into_region_mm3"));
            r.Rescue(1,"water");for(int i=0;i<300;i++)r.Step(emit:c=>c.Events.Emit("RouteBlocked","diagnostic",null,null,O("subject_id","diagnostic","edge_id","a","reason_code","UNSAFE_TARGET")));
            r.End();Assert.AreEqual(1,MetricValue(r,"edge_transfers"));Assert.AreEqual(1,MetricValue(r,"generator_shutdowns"));Assert.AreEqual(1,MetricValue(r,"shelter_exhaustions"));Assert.AreEqual(1,MetricValue(r,"evacuated_by_mode"));Assert.AreEqual(1,MetricValue(r,"equipment_deployments"));
            var debrief=r.System.Debrief(r.Marks,r.Events.Recent);Assert.IsFalse(debrief["diagnostics_complete_history"].Boolean);Assert.AreEqual(1,debrief["metric_summaries"].Items.Single(x=>x["metric"].Text=="edge_transfers")["value"].Integer);Assert.LessOrEqual(debrief["recent_diagnostics"].Items.Count,256);
            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(JsonValue.Array(savedMetrics.Items.Skip(1)),savedMonitors,2,2,r.State,r.Facts(),r.Marks));
            Assert.Throws<InvalidOperationException>(()=>r.System.Restore(savedMetrics,JsonValue.Array(savedMonitors.Items.Select(x=>x.With("extra",JsonValue.Number(1)))),2,2,r.State,r.Facts(),r.Marks));
            var identity=Identity("one");var summary=Summary(identity,true,2,7000);var grant=new RewardGrant("campaign.postcard","postcard.one","postcard");
            var ledger=ProgressionLedger.Empty().Apply("W1-M1",summary,new[]{grant});Assert.IsTrue(ledger.IsCampaignMissionCompleted("W1-M1"));Assert.AreEqual(1,ledger.ToJson()["reward_receipts"].Items.Count);
            ledger=ledger.Apply("W1-M1",Summary(Identity("two").With("mission_version_id",JsonValue.String("fixture-v2")),true,3,8000),new[]{grant});Assert.AreEqual(1,ledger.ToJson()["reward_receipts"].Items.Count);Assert.AreEqual(1,ledger.ToJson()["unlocks"].Items.Count);
            Assert.AreEqual("one",ledger.ToJson()["reward_receipts"].Items.Single()["source_run_id"].Text);
            var failed=ProgressionLedger.Empty().Apply("W1-M1",Summary(Identity("failed"),false,0,9000),new[]{grant});Assert.AreEqual(0,failed.ToJson()["reward_receipts"].Items.Count);Assert.IsFalse(failed.IsCampaignMissionCompleted("W1-M1"));
            Assert.AreNotEqual(ProgressionLedger.ComparisonPartition(identity),ProgressionLedger.ComparisonPartition(identity.With("rule_profile_id",JsonValue.String("relaxed")).With("assisted",JsonValue.Bool(true))));
            Assert.AreEqual(ledger.ToJson().Canonical(),ProgressionLedger.Restore(ledger.ToJson()).ToJson().Canonical());
            for(int i=0;i<70;i++)ledger=ledger.Apply("W1-M1",Summary(Identity("retention:"+i),true,1,i),new[]{grant});Assert.AreEqual(64,ledger.ToJson()["completed_runs"].Items.Count);Assert.AreEqual("retention:6",ledger.ToJson()["completed_runs"].Items[0]["identity"]["run_id"].Text);Assert.AreEqual("retention:69",ledger.ToJson()["completed_runs"].Items.Last()["identity"]["run_id"].Text);Assert.AreEqual(1,ledger.ToJson()["reward_receipts"].Items.Count);Assert.AreEqual("one",ledger.ToJson()["reward_receipts"].Items.Single()["source_run_id"].Text);Assert.IsTrue(ledger.IsCampaignMissionCompleted("W1-M1"));Assert.IsTrue(ledger.ToJson()["mission_progress"].Items.Any(x=>x["best_run_id"].Text=="one"));Assert.Throws<InvalidOperationException>(()=>ledger.Apply("W1-M1",Summary(Identity("retention:69"),true,1,9999),new[]{grant}));
            ActualTransportPassageFacts();
        }
        private static long MetricValue(Rig r,string metric)=>r.System.Metrics.Items.Single(x=>x["metric"].Text==metric)["value"].Integer;
        private static JsonValue Identity(string run) => O("run_id",run,"source_build_id","synthetic-objectives","mission_version_id","fixture-v1","rules_version","resq-rules-1","content_hash",J("{}").Sha256(),"layout_version_id","fixture-wide","layout_signature",J("{}").Sha256(),"rule_profile_id","standard","input_adapter_id","reference_replay","guidance_policy","unguided","challenge_id","base","modifiers_hash",J("[]").Sha256(),"seed",1,"assisted",false);
        private static JsonValue Summary(JsonValue id,bool success,int stars,int score)=>O("identity",id,"success",success,"stars",stars,"score",score,"rescued",success?1:0,"backed_up",0,"final_operation_tick",100,"final_scenario_tick",100,"final_state_hash",J("{}").Sha256());
        [Test, Category("runtime_units"), NUnit.Framework.Property("case_id","check-runtime-units/assertion/assertion_id=check-runtime-units-a01")]
        public void NormativeIntegerGoldenAndOverflowBounds()
        {
            var floor=Golden("RU01-floor-negative");Assert.AreEqual(floor["expected"]["floor_div"].Integer,IntegerMath.FloorDiv(floor["input"]["numerator"].Integer,floor["input"]["denominator"].Integer));Assert.AreEqual(floor["expected"]["ceil_div"].Integer,IntegerMath.CeilDiv(-100,3));
            foreach(long value in new[]{long.MinValue,-10L,-1L,0L,1L,10L,long.MaxValue}) foreach(long denominator in new[]{1L,2L,3L,long.MaxValue})
            { var f=IntegerMath.FloorDiv(value,denominator);var c=IntegerMath.CeilDiv(value,denominator);Assert.LessOrEqual(f,c);Assert.LessOrEqual((System.Numerics.BigInteger)f*denominator,(System.Numerics.BigInteger)value);Assert.GreaterOrEqual((System.Numerics.BigInteger)c*denominator,(System.Numerics.BigInteger)value);Assert.LessOrEqual(c-f,1); }
            Assert.Throws<ArgumentOutOfRangeException>(()=>IntegerMath.FloorDiv(1,0));Assert.Throws<ArgumentOutOfRangeException>(()=>IntegerMath.CeilDiv(1,-1));Assert.Throws<ArgumentOutOfRangeException>(()=>new Tick(-1));Assert.Throws<ArgumentOutOfRangeException>(()=>new Tick(Tick.Maximum+1));Assert.AreEqual(Tick.Maximum,new Tick(Tick.Maximum).Value);
            long remainder;Assert.Throws<OverflowException>(()=>WaterSystem.PumpRequest(long.MinValue,long.MaxValue,0,true,true,out remainder));Assert.Throws<InvalidOperationException>(()=>WaterSystem.RateRequest(1000000001,0,out remainder));
            Assert.Throws<ArgumentOutOfRangeException>(()=>TerminalScore.Compute(1,0,1,1,0,0,1,1));Assert.Throws<ArgumentOutOfRangeException>(()=>TerminalScore.Compute(1,1,1,0,0,0,1,1));Assert.Throws<ArgumentOutOfRangeException>(()=>TerminalScore.Compute(1,1,1,1,0,0,1,0));
            var score=Golden("RU03-score-once");var input=score["input"];Assert.AreEqual(score["expected"]["rounded_score"].Integer,TerminalScore.Compute(input["rescued"].Integer,input["population"].Integer,input["required_complete"].Integer,input["required_total"].Integer,input["protection_complete"].Integer,input["protection_total"].Integer,input["remaining_supplies"].Integer,input["budget"].Integer));
            var canonical=Golden("RP01-canonical-json");Assert.AreEqual(canonical["expected"]["canonical_utf8_text"].Text,canonical["input"].Canonical());Assert.AreEqual(canonical["expected"]["sha256"].Text,canonical["input"].Sha256());
            var rng=Golden("RP01-xorshift32-seed-one");var generator=new XorShift32(rng["input"]["state"].Integer);foreach(var expected in rng["expected"]["outputs"].Items)Assert.AreEqual(expected.Integer,generator.Next());Assert.AreEqual(rng["expected"]["final_state"].Integer,generator.State);Assert.AreEqual(rng["expected"]["draw_count"].Integer,generator.Draws);
        }
        private static OperationClock RunActualClock()
        {
            var payload=Payload();var mission=payload["missions"].Items.Single().With("duration_scenario_ticks",JsonValue.Number(21)).With("events",J("[]")).With("protection_objective_ids",A()).With("objectives",JsonValue.Array(new[]{Def("rescue_minimum","required","rescue_count",O("minimum",1)),Def("critical","critical","rescue_count",O("minimum",1)),Def("passage","mastery","counter_at_least",O("metric","edge_transfers","edge_tag","fixture_path","minimum",1)),Def("actual_dwell","optional","counter_at_least",O("metric","max_node_dwell_scenario_ticks","node_id","start","minimum",1)),Def("actual_peak","optional","counter_at_least",O("metric","peak_node_occupancy","node_id","assembly","minimum",1))}));
            var content=ContentCompilation.Compile(payload.With("missions",JsonValue.Array(new[]{mission})));var m=content.Envelope.payload.missions.Single();var layout=content.Envelope.payload.layouts.Single(x=>x.id=="fixture-wide");
            var transport=new TransportSystem(content,m.mission_version_id,layout.id);var domain=new SyntheticSafetyDomain(layout);
            var transportSystems=new TransportOperationSystems(domain,transport,(authority,owner,c)=>((SyntheticSafetyDomain)owner).Frame(c), (authority,owner)=>false);
            var objectives=new ObjectiveOperationSystems(transportSystems,new ObjectiveSystem(m,layout),(owner,c,state)=>
            {
                var actual=((TransportOperationSystems)owner).Transport;
                return new ObjectiveObservations(c.OperationTick,new Dictionary<string,bool>(),new Dictionary<string,bool>(),new Dictionary<string,long>(),c.OperationTick==0?new CompletedEdgePassage[0]:actual.ReadBoundaryFacts(c.OperationTick),c.OperationTick==0?new DockWaitSample[0]:actual.ReadDockWaitFacts(c.OperationTick),c.OperationTick==0?new CompletedNodeDwell[0]:actual.ReadCompletedNodeDwells(c.OperationTick),c.OperationTick==0?layout.nav_nodes.ToDictionary(x=>x.id,x=>(long)state["residents"].Items.Count(p=>p["location"]["kind"].Text=="node"&&p["location"]["node_id"].Text==x.id),StringComparer.Ordinal):actual.ReadNodeOccupancyPeaks(c.OperationTick).ToDictionary(x=>x.Key,x=>(long)x.Value,StringComparer.Ordinal));
            });
            var identity=Identity("clock").With("content_hash",JsonValue.String(content.ContentHash)).With("layout_signature",JsonValue.String(content.LayoutSignature(layout.id)));
            var clock=new OperationClock(content,m.mission_version_id,layout.id,identity,objectives);
            Func<long,string,JsonValue,GameCommand> command=(seq,kind,body)=>GameCommand.Read(O("schema_version",1,"command_id","cmd:"+seq,"run_id","clock","sequence",seq,"requested_tick",kind=="assign_route"?clock.OperationTick+1:clock.OperationTick,"actor_input","reference_replay","kind",kind,"payload",body));
            clock.Apply(command(1,"resume",O()));clock.CompleteResumeCountdown();
            var receipt=clock.Admit(command(2,"assign_route",O("subject_kind","group","subject_id","group","stops",JsonValue.Array(new[]{O("node_id","refuge","action","evacuate","group_id",null)}))));Assert.AreEqual("queued",receipt.Status,receipt.ReasonCode);
            for(int i=1;i<=21;i++) { var step=clock.Step();Assert.IsNotNull(step,clock.Fault);Assert.AreEqual(i,step.OperationTick);if(i<21)Assert.IsFalse(step.Terminal); }
            return clock;
        }
        private static void ClockLastTickArrival()
        {
            var clock=RunActualClock();var snapshot=clock.ReadSnapshot().Value;Assert.AreEqual(21,snapshot["scenario_tick"].Integer);Assert.IsTrue(snapshot["outcome"]["success"].Boolean);Assert.AreEqual(1,snapshot["outcome"]["rescued"].Integer);Assert.AreEqual(3,snapshot["outcome"]["stars"].Integer);
            var events=snapshot["recent_events"].Items;var arrival=events.Single(x=>x["kind"].Text=="ResidentEvacuated");var outcome=events.Single(x=>x["kind"].Text=="OutcomeResolved");Assert.AreEqual(21,arrival["operation_tick"].Integer);Assert.Less(arrival["ordinal"].Integer,outcome["ordinal"].Integer);
        }
        private static void ActualTransportPassageFacts()
        {
            var clock=RunActualClock();var state=clock.ReadSnapshot().Value;Assert.AreEqual(1,state["metrics"].Items.Single(x=>x["metric"].Text=="max_node_dwell_scenario_ticks")["value"].Integer,"Actual final departure interval is consumed before node entry timestamp clears");Assert.AreEqual(1,state["metrics"].Items.Single(x=>x["metric"].Text=="peak_node_occupancy")["value"].Integer,"Actual transient walking waypoint occupancy is retained");var edge=state["metrics"].Items.Single(x=>x["metric"].Text=="edge_transfers");Assert.AreEqual(1,edge["value"].Integer,"One resident completing two matching edges remains distinct");Assert.AreEqual(1,edge["distinct_resident_ids"].Items.Count);Assert.IsTrue(state["objective_monitors"].Items.Single(x=>x["id"].Text=="passage")["fulfilled"].Boolean);
        }
        // Explicit synthetic safety fixture for clock/transport/objective stage
        // integration. Its water engine actually runs; absent hazard families are
        // not represented. It cannot accept arbitrary commands or decide outcomes.
        private sealed class SyntheticSafetyDomain : IOperationSystems, ITransportDomainPeer
        {
            private readonly LayoutDef layout; private WaterSystem water;private TransportSystem transport;
            public SyntheticSafetyDomain(LayoutDef layout){this.layout=layout;water=new WaterSystem(layout);}
            public void BindTransport(TransportSystem value){transport=value;}
            public IOperationSystems Fork()=>new SyntheticSafetyDomain(layout){water=water.Fork()};
            public TransportFrame Frame(OperationContext c)=>new TransportFrame(c.OperationTick,layout.nav_nodes.Select(x=>new TransportNodeSample(x.id,true,true,true)),layout.nav_edges.Select(x=>new TransportEdgeSample(x.id,true,true,1000,null)),transport.ReadState()["residents"].Items.ToDictionary(x=>x["id"].Text,x=>false,StringComparer.Ordinal));
            public ValidationResult Validate(GameCommand command,OperationContext c)=>new ValidationResult("UNKNOWN_TARGET");
            public void Apply(GameCommand command,OperationContext c){throw new InvalidOperationException("Synthetic safety fixture has no direct commands");}
            public void EvaluateTides(OperationContext c){if(layout.tides.Count!=0)throw new InvalidOperationException("Unsupported fixture tide");}
            public void PublishWarning(EventDef definition,OperationContext c){throw new InvalidOperationException("Fixture events are empty");}
            public void ApplyScheduled(ResolvedEventOperation op,OperationContext c){throw new InvalidOperationException("Fixture events are empty");}
            public void ResolvePowerAndReservations(OperationContext c){if(layout.power_nodes.Count!=0)throw new InvalidOperationException("Unsupported fixture power");}
            public void SimulateHazardInterval(OperationContext c){water.Pulse(new WaterPump[0],new WaterSprinkler[0],new Dictionary<string,long>(),new Dictionary<string,bool>(),new string[0]).EmitTransfers(c);}
            public void RecomputeSafetyAndDamage(OperationContext c){water.ValidateBoundary();}
            public void ResolveMovementAndTransfers(OperationContext c){Assert.AreEqual(1,transport.Population);}
            public void DeriveMetricsAndObjectives(OperationContext c){Assert.AreEqual(c.OperationTick,Frame(c).OperationTick);}
            public void EmitMilestone(string id,OperationContext c){throw new InvalidOperationException("Outer objective owner handles engine milestones");}
            public void SampleInitialObjectives(OperationContext c){Assert.AreEqual(0,c.OperationTick);}
            public bool GuidedEpochReady=>false;
            public bool RequiredConditionsHold=>throw new InvalidOperationException("Outer objective owner evaluates required predicates");
            public OutcomeResult EvaluateOutcome(OperationContext c)=>throw new InvalidOperationException("Outer real ObjectiveSystem evaluates outcome");
            public void ValidateBoundary(OperationContext c){water.ValidateBoundary();transport.ValidateBoundary();}
            public JsonValue ReadDomainState()
            {
                var d=new Dictionary<string,JsonValue>(StringComparer.Ordinal);foreach(string field in new[]{"equipment","vehicles","residents","transits","transfers","reservations","routes","queues","fire","supports","runouts","tides","lava","stores","producers","milestones","metrics","objective_monitors","recent_events"})d.Add(field,J("[]"));
                d["water"]=water.ReadWater();d["rate_remainders"]=water.ReadRateRemainders();d["water_ledger"]=water.ReadLedger();d["supplies_remaining"]=JsonValue.Number(1);d["selected_stock"]=J("[{\"equipment_id\":\"beacon\",\"count\":1}]");d["deployments"]=J("[{\"equipment_id\":\"beacon\",\"count\":0}]");
                d["guidance_state"]=O("policy","unguided","step_id","none","completed_step_ids",A(),"acknowledged_warning",false);d["power"]=J("{\"node_enabled\":[],\"link_enabled\":[],\"consumer_demands\":[],\"priorities\":[],\"grants\":[],\"generator_trip_ids\":[]}");
                d["event_state"]=J("{\"next_static_event_index\":0,\"fired_event_ids\":[],\"revealed_event_ids\":[],\"warned_event_ids\":[],\"dynamic_events\":[],\"administrative_edge_enabled\":[],\"edge_current_multipliers\":[],\"gate_open\":[],\"group_held\":[],\"wind_region_codes\":[],\"cooler_socket_enabled\":[],\"shelter_region_enabled\":[],\"resource_warning_thresholds\":[]}");return JsonValue.Object(d);
            }
        }
    }
}
