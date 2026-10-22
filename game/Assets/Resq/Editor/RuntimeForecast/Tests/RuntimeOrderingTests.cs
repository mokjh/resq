using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Progression;
using Resq.Simulation;
using Resq.Simulation.Forecast;
using UnityEngine;

namespace Resq.Tests.Forecast
{
    [TestFixture, Category("runtime_ordering")]
    public sealed class RuntimeOrderingTests
    {
        private static JsonValue O(params object[] p) => ForecastFixture.O(p);
        private static JsonValue A(params string[] p) => ForecastFixture.A(p);
        private static void Step(OperationClock clock) => RuntimeForecastTests.Step(clock);
        private static JsonValue Place() => O("equipment_id", "beacon", "socket_id", "beacon:01", "orientation_step", 0);

        [Test, NUnit.Framework.Property("case_id", "check-runtime-ordering/assertion/assertion_id=check-runtime-ordering-a01")]
        public void AllNormativeOrderingFixturesAndBoundaries()
        {
            string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            var fixture = StrictJson.Parse(File.ReadAllBytes(Path.Combine(root, "specification/fixtures/runtime-golden-v1.json")));
            var assigned = fixture["cases"].Items.Where(x => x["suite"].Text == "runtime-ordering").ToArray();
            CollectionAssert.AreEquivalent(new[] { "RO01-relaxed-four-steps", "RO04-deadline-arrival" }, assigned.Select(x => x["id"].Text));
            EpochsAndNoCatchup(assigned.Single(x => x["id"].Text.StartsWith("RO01", StringComparison.Ordinal)));
            ScheduledClosureBeforeCommandAndBoarding();
            LastTickArrivalAndStrictBefore(assigned.Single(x => x["id"].Text.StartsWith("RO04", StringComparison.Ordinal)));
            GuidedEpochUsesActualBusCompletion();
            InitialWarningsExactlyOnceAndEventBounds();
            TestContext.WriteLine("RO01 RO02 RO03 RO04 RO05 RO06 RO07 executed with actual C02-C07 domain owners; RF/RS/RI/RL belong to their own frozen suites.");
        }
        private static void EpochsAndNoCatchup(JsonValue golden)
        {
            foreach (bool relaxed in new[] { false, true })
            {
                var rig = ForecastFixture.Build(relaxed); rig.StartVehicleRoute();
                var scenario = new List<long>(); var accumulator = new List<long>();
                for (int i = 0; i < golden["input"]["operation_steps"].Integer; i++) { Step(rig.Clock); scenario.Add(rig.Clock.ScenarioTick); accumulator.Add(rig.Clock.ScenarioAccumulator); }
                CollectionAssert.AreEqual(relaxed ? golden["expected"]["scenario_ticks_after_each_step"].Items.Select(x => x.Integer) : new long[] { 1, 2, 3, 4 }, scenario);
                CollectionAssert.AreEqual(relaxed ? golden["expected"]["accumulator_after_each_step"].Items.Select(x => x.Integer) : new long[] { 0, 0, 0, 0 }, accumulator);
                Assert.AreEqual(golden["expected"]["operation_tick"].Integer, rig.Clock.OperationTick);
                Assert.IsTrue(rig.Clock.Apply(rig.Command("pause", O("reason", "user"))).Receipt.Accepted);
                string before = rig.Clock.ReadSnapshot().Value.Canonical();
                for (int n = 0; n < 8; n++) Assert.IsNull(rig.Clock.Step());
                var scheduler = new FrameStepBudget(); Assert.AreEqual(0, scheduler.Offer(1000000, false)); Assert.AreEqual(0, scheduler.DebtMilliseconds);
                Assert.AreEqual(before, rig.Clock.ReadSnapshot().Value.Canonical());
                long at = rig.Clock.OperationTick, s = rig.Clock.ScenarioTick;
                var resume = rig.Clock.Apply(rig.Command("resume", O())); Assert.IsTrue(resume.Receipt.Accepted);
                Assert.IsNull(rig.Clock.Step()); Assert.AreEqual(at, rig.Clock.OperationTick); Assert.AreEqual(s, rig.Clock.ScenarioTick);
                rig.Clock.CompleteResumeCountdown(); Assert.AreEqual(at, rig.Clock.OperationTick);
                Assert.AreEqual(0, scheduler.Offer(0, true)); Assert.AreEqual(4, scheduler.Offer(1000000, true));
                Assert.LessOrEqual(scheduler.DebtMilliseconds, 400); Assert.Greater(scheduler.DiscardedMilliseconds, 0);
                // Explicit player advance remains ten O steps, never a wall-clock
                // catch-up and never ten S steps in Relaxed.
                Assert.IsTrue(rig.Clock.Apply(rig.Command("pause", O("reason", "user"))).Receipt.Accepted);
                var advance = rig.Clock.Apply(rig.Command("advance_ticks", O("operation_ticks", 10)));
                Assert.IsTrue(advance.Receipt.Accepted); Assert.AreEqual(10, advance.Advanced.Steps.Count);
                Assert.AreEqual(at + 10, rig.Clock.OperationTick); Assert.AreEqual(s + (relaxed ? 5 : 10), rig.Clock.ScenarioTick);
                Assert.IsTrue(rig.Clock.IsPaused);
            }
        }
        private static JsonValue RouteToBus() => O("subject_kind", "vehicle", "subject_id", "bus", "stops", JsonValue.Array(new[] {
            O("node_id", "start", "action", "pickup", "group_id", "group"), O("node_id", "refuge", "action", "evacuate", "group_id", null) }));
        private static void ScheduledClosureBeforeCommandAndBoarding()
        {
            // A real source effect floods an actual dock exactly on the final
            // boarding tick. Water stage5 precedes transfer completion stage7.
            var boarding = ForecastFixture.Build(configure: RuntimeForecastTests.FloodedBindings); boarding.Start();
            while (boarding.Clock.OperationTick < 109) Step(boarding.Clock);
            Assert.AreEqual("queued", boarding.Clock.Admit(boarding.Command("assign_route", RouteToBus())).Status);
            while (boarding.Clock.OperationTick < 119) Step(boarding.Clock);
            Assert.AreEqual(9, boarding.Clock.ReadSnapshot().Value["transfers"].Items.Single()["progress_operation_ticks"].Integer);
            Step(boarding.Clock); var closed = boarding.Clock.ReadSnapshot().Value;
            Assert.AreEqual(0, closed["transfers"].Items.Count); Assert.AreEqual(0, closed["vehicles"].Items.Single()["occupant_ids"].Items.Count);
            var resident = closed["residents"].Items.Single(); Assert.AreEqual("node", resident["location"]["kind"].Text); Assert.AreEqual("start", resident["location"]["node_id"].Text);
            Assert.IsFalse(closed["reservations"].Items.Any(x => x["resource_kind"].Text == "vehicle_seat"));
            Assert.IsTrue(closed["recent_events"].Items.Any(x => x["kind"].Text == "TransferCancelled" && x["operation_tick"].Integer == 120));

            // Actual set_edge_enabled schedule is visible to validation before a
            // same-step real route order; it cannot admit movement on the edge.
            var command = ForecastFixture.Build(configure: payload => {
                var mission = payload["missions"].Items.Single(); var original = mission["events"].Items.Single();
                var effect = original.With("operations", JsonValue.Array(new[] { O("kind", "set_edge_enabled", "target_role", "closed_route", "value", 0) }));
                var layouts = payload["layouts"].Items.Select(layout => layout.With("roles", JsonValue.Array(layout["roles"].Items.Concat(new[] { O("role_id", "closed_route", "target_kind", "nav_edge_set", "target_ids", A("a")) }))));
                return payload.With("missions", JsonValue.Array(new[] { mission.With("events", JsonValue.Array(new[] { effect })) })).With("layouts", JsonValue.Array(layouts));
            }); command.Start(); while (command.Clock.OperationTick < 119) Step(command.Clock);
            var route = command.Command("assign_route", O("subject_kind", "group", "subject_id", "group", "stops", JsonValue.Array(new[] { O("node_id", "refuge", "action", "evacuate", "group_id", null) })));
            Assert.AreEqual("queued", command.Clock.Admit(route).Status);
            bool sawRealValidation = false;
            ForecastFixture.ValidationObserved = (tick, kind, state) => {
                if (tick == 120 && kind == "assign_route") { sawRealValidation = true; Assert.IsFalse(state["event_state"]["administrative_edge_enabled"].Items.Single(x => x["id"].Text == "a")["value"].Boolean); }
            };
            try { Step(command.Clock); } finally { ForecastFixture.ValidationObserved = null; }
            Assert.IsTrue(sawRealValidation); Assert.AreEqual(0, command.Clock.ReadSnapshot().Value["transits"].Items.Count);
            Assert.IsTrue(command.Clock.ReadSnapshot().Value["queues"].Items.Any(x => x["kind"].Text == "edge"));
        }
        private static void LastTickArrivalAndStrictBefore(JsonValue golden)
        {
            long deadline = golden["input"]["deadline_scenario_tick"].Integer;
            var rig = ForecastFixture.Build(duration: deadline, configure: payload => {
                var mission = payload["missions"].Items.Single().With("events", A());
                var strict = O("id", "strict_same_tick", "tier", "mastery", "kind", "rescue_count", "params", O("minimum", 1), "temporal", O("kind", "ever_before", "end_event", "all.safe"));
                var before = O("id", "strict_before", "tier", "optional", "kind", "supply_remaining_at_least", "params", O("minimum", 1), "temporal", O("kind", "ever_before", "end_event", "all.safe"));
                mission = mission.With("objectives", JsonValue.Array(mission["objectives"].Items.Concat(new[] { strict, before })));
                var layouts = payload["layouts"].Items.Select(layout => layout.With("nav_edges", JsonValue.Array(layout["nav_edges"].Items
                    .Select(e => e.With("base_operation_ticks", JsonValue.Number(e["id"].Text == "a" ? 10 : e["id"].Text == "b" ? deadline - 11 : deadline))))));
                return payload.With("missions", JsonValue.Array(new[] { mission })).With("layouts", JsonValue.Array(layouts));
            }); rig.Start();
            Assert.AreEqual("queued", rig.Clock.Admit(rig.Command("assign_route", O("subject_kind", "group", "subject_id", "group", "stops", JsonValue.Array(new[] { O("node_id", "refuge", "action", "evacuate", "group_id", null) })))).Status);
            while (rig.Clock.OperationTick < deadline - 1) Step(rig.Clock);
            Assert.AreNotEqual("evacuated", rig.Clock.ReadSnapshot().Value["residents"].Items.Single()["state"].Text);
            Step(rig.Clock); var state = rig.Clock.ReadSnapshot().Value;
            Assert.AreEqual("outcome", rig.Clock.Phase); Assert.AreEqual(golden["expected"]["success"].Boolean, state["outcome"]["success"].Boolean);
            Assert.AreEqual(1, state["outcome"]["rescued"].Integer);
            var arrival = state["recent_events"].Items.Single(x => x["kind"].Text == "ResidentEvacuated");
            var outcome = state["recent_events"].Items.Single(x => x["kind"].Text == "OutcomeResolved");
            Assert.AreEqual(golden["input"]["arrival_completes_on_operation_tick"].Integer, arrival["operation_tick"].Integer);
            Assert.Less(arrival["ordinal"].Integer, outcome["ordinal"].Integer);
            var same = state["objective_monitors"].Items.Single(x => x["id"].Text == "strict_same_tick");
            Assert.IsFalse(same["fulfilled"].Boolean, "Same-step first truth cannot satisfy strict-before");
            Assert.IsTrue(state["objective_monitors"].Items.Single(x => x["id"].Text == "strict_before")["fulfilled"].Boolean);
            Assert.IsNull(rig.Clock.Step());
        }
        private static void GuidedEpochUsesActualBusCompletion()
        {
            var guided = ForecastFixture.Build(guided: true); guided.StartVehicleRoute();
            long epoch = -1;
            for (int n = 0; n < 80 && !guided.Clock.ScenarioEnabled; n++)
            {
                Step(guided.Clock); Assert.AreEqual(0, guided.Clock.ScenarioTick); Assert.AreEqual(0, guided.Clock.ScenarioAccumulator);
                var state = guided.Clock.ReadSnapshot().Value;
                bool arrived = state["residents"].Items.All(x => x["state"].Text == "evacuated") && state["vehicles"].Items.All(x => x["occupant_ids"].Items.Count == 0);
                Assert.AreEqual(arrived, guided.Clock.ScenarioEnabled, "Epoch is triggered by actual bus delivery, never an injected target tick");
                if (arrived) epoch = guided.Clock.OperationTick;
            }
            Assert.Greater(epoch, 0); var start = guided.Clock.ReadSnapshot().Value["milestones"].Items.Single(x => x["id"].Text == "operation_started");
            Assert.AreEqual(epoch, start["operation_tick"].Integer); Assert.AreEqual(0, start["scenario_tick"].Integer);
            Step(guided.Clock); Assert.AreEqual(1, guided.Clock.ScenarioTick);
            var unguided = ForecastFixture.Build(); unguided.StartVehicleRoute(); Step(unguided.Clock); Assert.AreEqual(1, unguided.Clock.ScenarioTick);
            var guidedIdentity = guided.Clock.ReadSnapshot().Value["identity"];
            Assert.AreNotEqual(ProgressionLedger.ComparisonPartition(guidedIdentity), ProgressionLedger.ComparisonPartition(guidedIdentity.With("guidance_policy", JsonValue.String("unguided"))));
        }
        private static void InitialWarningsExactlyOnceAndEventBounds()
        {
            foreach (long invalidTick in new long[] { 0, 700 })
                Assert.Throws<ContentException>(() => ForecastFixture.Build(configure: payload => {
                    var m = payload["missions"].Items.Single(); return payload.With("missions", JsonValue.Array(new[] { m.With("events", JsonValue.Array(m["events"].Items.Select(e => e.With("at_scenario_tick", JsonValue.Number(invalidTick)).With("warning_at_scenario_tick", JsonValue.Number(0))))) }));
                }));
            var rig = ForecastFixture.Build(configure: payload => {
                var m = payload["missions"].Items.Single(); var e = m["events"].Items.Single().With("warning_at_scenario_tick", JsonValue.Number(0));
                return payload.With("missions", JsonValue.Array(new[] { m.With("events", JsonValue.Array(new[] {
                    e.With("id", JsonValue.String("z_warning")), e.With("id", JsonValue.String("a_warning")).With("at_scenario_tick", JsonValue.Number(130)) })) }));
            });
            Func<OperationClock, string[]> warnings = clock => clock.ReadSnapshot().Value["recent_events"].Items.Where(x => x["kind"].Text == "HazardWarning").Select(x => x["cause_event_id"].Text).ToArray();
            CollectionAssert.AreEqual(new[] { "a_warning", "z_warning" }, warnings(rig.Clock));
            var initial = rig.Clock.ReadSnapshot().Value; var restored = rig.Factory.Restore(initial); CollectionAssert.AreEqual(warnings(rig.Clock), warnings(restored));
            rig.Start(); CollectionAssert.AreEqual(new[] { "a_warning", "z_warning" }, warnings(rig.Clock));
            var clone = rig.Factory.RestoreForForecast(rig.Clock.ReadSnapshot().Value); CollectionAssert.AreEqual(warnings(rig.Clock), warnings(clone));
            Step(clone); CollectionAssert.AreEqual(new[] { "a_warning", "z_warning" }, warnings(clone));
            using (var worker = RuntimeForecastTests.Service(rig))
            {
                worker.Begin(RuntimeForecastTests.Request(rig, rig.Command("place_equipment", Place()), horizon: 10));
                Assert.AreEqual("complete", RuntimeForecastTests.Finish(worker).Status);
            }
            CollectionAssert.AreEqual(new[] { "a_warning", "z_warning" }, warnings(rig.Clock));
        }
    }
}
