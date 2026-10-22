using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Content;
using Resq.Core;
using Resq.Simulation;
using UnityEngine;

namespace Resq.Tests.RuntimeCommands
{
    [TestFixture, Category("runtime_commands")]
    public sealed class RuntimeCommandTests
    {
        private static string Root => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        private static JsonValue Json(string text) => StrictJson.Parse(text);
        private static JsonValue Golden(string id) => Json(File.ReadAllText(Path.Combine(Root, "specification/fixtures/runtime-golden-v1.json")))["cases"].Items.Single(item => item["id"].Text == id);
        private static CompiledContent Content(long duration = 1000, bool scheduledClosure = false, bool challenge = false)
        {
            var root = Json(File.ReadAllText(Path.Combine(Root, "content/runtime/fixtures/compiler-valid.json")));
            root = RenameFixtureResidents(root);
            var profiles = new[] { "standard", "relaxed", "command" }.Select(profile => JsonValue.Object(new Dictionary<string,JsonValue> { {"id",JsonValue.String(profile)}, {"scenario_numerator",JsonValue.Number(profile == "relaxed" ? 1 : 2)}, {"scenario_denominator",JsonValue.Number(2)}, {"planning_allowed",JsonValue.Bool(true)}, {"extra_stock",Json("[]")}, {"permitted_input_adapters",JsonValue.Array((profile == "command" ? new[] { "hands", "controller", "head_dwell", "reference_replay" } : new[] { "hands", "controller", "reference_replay" }).Select(JsonValue.String))} }));
            var events = scheduledClosure ? Json("[{\"id\":\"z-close\",\"at_scenario_tick\":100,\"warning_at_scenario_tick\":0,\"known_at_start\":true,\"operations\":[{\"kind\":\"set_edge_enabled\",\"target_role\":\"road\",\"value\":0}]},{\"id\":\"a-open\",\"at_scenario_tick\":100,\"warning_at_scenario_tick\":0,\"known_at_start\":true,\"operations\":[{\"kind\":\"set_source_rate\",\"target_role\":\"source\",\"value\":10}]}]") : Json("[]");
            var missions = root["missions"].Items.Select(mission => mission.With("id", JsonValue.String("W1-M1")).With("tutorial_policy", JsonValue.String("guided_first_play_then_unguided")).With("duration_scenario_ticks", JsonValue.Number(duration)).With("rule_profile_ids", Json("[\"standard\",\"relaxed\",\"command\"]")).With("events", events));
            var layouts = root["layouts"].Items.Select(layout => layout.With("roles", JsonValue.Array(layout["roles"].Items.Concat(new[] { Json("{\"role_id\":\"road\",\"target_kind\":\"nav_edge_set\",\"target_ids\":[\"b\"]}") }))));
            // Recompute logical graph hashes exactly as the accepted compiler does.
            layouts = layouts.Select(layout => layout.With("graph_hash", JsonValue.String(ContentCompilation.ComputeGraphHash(layout))));
            root = root.With("rule_profiles", JsonValue.Array(profiles)).With("missions", JsonValue.Array(missions)).With("layouts", JsonValue.Array(layouts));
            if (challenge)
            {
                var mission = root["missions"].Items[0];
                var definition = JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String("fixture-light")},{"revision",JsonValue.Number(1)},{"base_mission_version_id",JsonValue.String("fixture-v1")},{"operations",Json("[{\"kind\":\"maximum_equipment_losses\",\"maximum\":0}]" )},{"replacement_default_stock",mission["default_stock"]},{"replacement_maximum_stock",mission["maximum_stock"]},{"resulting_budget",mission["supply_budget"]} });
                root = root.With("challenges", JsonValue.Array(new[] { definition })).With("missions", JsonValue.Array(new[] { mission.With("challenge_ids", Json("[\"fixture-light\"]")) }));
            }
            return ContentCompilation.Compile(root.Canonical());
        }
        private static JsonValue RenameFixtureResidents(JsonValue value)
        {
            if (value.Kind == "string" && value.Text.StartsWith("fixture:resident:", StringComparison.Ordinal)) return JsonValue.String("W1-M1:resident:" + value.Text.Substring("fixture:resident:".Length));
            if (value.Kind == "array") return JsonValue.Array(value.Items.Select(RenameFixtureResidents));
            if (value.Kind == "object") return JsonValue.Object(value.Members.ToDictionary(pair => pair.Key, pair => RenameFixtureResidents(pair.Value), StringComparer.Ordinal));
            return value;
        }
        private static JsonValue Identity(CompiledContent content, string profile = "standard", string run = "run-one", string adapter = "hands", string guidance = "unguided") => JsonValue.Object(new Dictionary<string, JsonValue> {
            {"run_id",JsonValue.String(run)},{"source_build_id",JsonValue.String("synthetic-native-fixture")},{"mission_version_id",JsonValue.String("fixture-v1")},{"rules_version",JsonValue.String("resq-rules-1")},
            {"content_hash",JsonValue.String(content.ContentHash)},{"layout_version_id",JsonValue.String("fixture-wide")},{"layout_signature",JsonValue.String(content.LayoutSignature("fixture-wide"))},
            {"rule_profile_id",JsonValue.String(profile)},{"input_adapter_id",JsonValue.String(adapter)},{"guidance_policy",JsonValue.String(guidance)},{"challenge_id",JsonValue.String("base")},{"modifiers_hash",JsonValue.String(Json("[]").Sha256())},{"seed",JsonValue.Number(1)},{"assisted",JsonValue.Bool(false)} });
        private static OperationClock Clock(CompiledContent content, SyntheticSystems systems, string profile = "standard", string run = "run-one", string adapter = "hands", string guidance = "unguided") => new OperationClock(content, "fixture-v1", "fixture-wide", Identity(content, profile, run, adapter, guidance), systems);
        private static GameCommand Command(long sequence, long tick, string kind, string payload = "{}", string id = null, string run = "run-one", string actor = "hands_right") => GameCommand.Read(JsonValue.Object(new Dictionary<string, JsonValue> {
            {"schema_version",JsonValue.Number(1)},{"command_id",JsonValue.String(id ?? "cmd:" + sequence.ToString(CultureInfo.InvariantCulture))},{"run_id",JsonValue.String(run)},{"sequence",JsonValue.Number(sequence)},{"requested_tick",JsonValue.Number(tick)},{"actor_input",JsonValue.String(actor)},{"kind",JsonValue.String(kind)},{"payload",Json(payload)} }));
        private static void Start(OperationClock clock, string run = "run-one", string actor = "hands_right") { var result = clock.Apply(Command(clock.NextSequence, clock.OperationTick, "resume", run: run, actor: actor)); Assert.That(result.Receipt.Accepted, Is.True); Assert.That(clock.Step(), Is.Null); clock.CompleteResumeCountdown(); }
        private const string Place = "{\"equipment_id\":\"beacon\",\"socket_id\":\"beacon:01\",\"orientation_step\":0}";

        [Test, NUnit.Framework.Property("case_id", "check-c02/assertion/assertion_id=check-c02-a01")]
        public void CanonicalRngProjectionAndOrderingGoldens()
        {
            foreach (string id in new[] { "RP01-canonical-json", "RP01-empty-modifiers-hash" })
            {
                var golden = Golden(id); Assert.That(golden["input"].Canonical(), Is.EqualTo(golden["expected"]["canonical_utf8_text"].Text)); Assert.That(golden["input"].Sha256(), Is.EqualTo(golden["expected"]["sha256"].Text));
            }
            var rngGolden = Golden("RP01-xorshift32-seed-one"); var rng = new XorShift32(rngGolden["input"]["state"].Integer);
            var output = Enumerable.Range(0, (int)rngGolden["input"]["draws"].Integer).Select(_ => (long)rng.Next()).ToArray();
            Assert.That(output, Is.EqualTo(rngGolden["expected"]["outputs"].Items.Select(value => value.Integer).ToArray()));
            Assert.That((long)rng.State, Is.EqualTo(rngGolden["expected"]["final_state"].Integer)); Assert.That(rng.Draws, Is.EqualTo(rngGolden["expected"]["draw_count"].Integer));
            var restored = XorShift32.Restore(rng.State, rng.Draws); Assert.That(restored.Next(), Is.EqualTo(rng.Next())); Assert.That(restored.Draws, Is.EqualTo(rng.Draws));
            Assert.That(new XorShift32(0).State, Is.EqualTo(0x9E3779B9u)); Assert.That(new XorShift32(1L << 32).State, Is.EqualTo(0x9E3779B9u));
            Assert.Throws<ArgumentException>(() => XorShift32.Restore(0, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => rng.Bounded(0));
            var bounded = new XorShift32(1); Assert.That(bounded.Bounded(2147483649u), Is.EqualTo(499951812u)); Assert.That(bounded.Draws, Is.EqualTo(3));
            var overflowRng = XorShift32.Restore(1, long.MaxValue); Assert.Throws<OverflowException>(() => overflowRng.Next()); Assert.That(overflowRng.State, Is.EqualTo(1));
            var units = Golden("RU01-floor-negative"); long n = units["input"]["numerator"].Integer, d = units["input"]["denominator"].Integer;
            Assert.That(IntegerMath.FloorDiv(n, d), Is.EqualTo(units["expected"]["floor_div"].Integer)); Assert.That(IntegerMath.CeilDiv(n, d), Is.EqualTo(units["expected"]["ceil_div"].Integer));
            Assert.That(IntegerMath.CeilDiv(long.MinValue, 1), Is.EqualTo(long.MinValue)); Assert.That(IntegerMath.FloorDiv(long.MaxValue, 1), Is.EqualTo(long.MaxValue));
            string[] malformed = { "{\"a\":1,\"a\":2}", "{\"a\":-0}", "{\"a\":01}", "{\"a\":1.0}", "{\"a\":1e0}", "{\"a\":9223372036854775808}", "{\"a\":\"e\\u0301\"}", "{\"a\":\"\\ud800\"}", "\ufeff{}" };
            foreach (string invalid in malformed) Assert.Throws<ContentException>(() => Json(invalid), invalid);
            Assert.Throws<ContentException>(() => StrictJson.Parse(new byte[] { 0xc3, 0x28 }));
            Assert.That(Json("{\"x\":\"/<> &\\u2028\\u2029\\n\"}").Canonical(), Is.EqualTo("{\"x\":\"/<> &\u2028\u2029\\u000a\"}"));
            Assert.That(Json("[2,1]").Canonical(), Is.EqualTo("[2,1]"));

            var content = Content(); var first = Clock(content, new SyntheticSystems()); var second = Clock(content, new SyntheticSystems(), run: "run-two", adapter: "controller");
            first.Admit(Command(1, 1, "place_equipment", Place, "uuid-one")); second.Admit(Command(1, 1, "place_equipment", Place, "uuid-two", "run-two", "controller_right"));
            Assert.That(first.ReadSnapshot().StateSha256, Is.EqualTo(second.ReadSnapshot().StateSha256));
            Assert.That(CanonicalState.IntegrityHash(first.ReadSnapshot().Value), Is.Not.EqualTo(CanonicalState.IntegrityHash(second.ReadSnapshot().Value)));
            Start(first); Start(second, "run-two", "controller_right"); first.Step(); second.Step();
            Assert.That(first.ReadSnapshot().StateSha256, Is.EqualTo(second.ReadSnapshot().StateSha256));
            var snapshot = first.ReadSnapshot().Value;
            // Every remaining authority field participates, including every future
            // subsystem collection. No generic cosmetic-field exclusion exists.
            foreach (var field in snapshot.Members.Where(pair => pair.Key != "identity" && pair.Key != "state_revision" && pair.Key != "command_receipts" && pair.Key != "recent_events" && pair.Key != "pending_commands" && pair.Key != "end_request"))
            {
                JsonValue changed = field.Value.Kind == "array" ? JsonValue.Array(field.Value.Items.Concat(new[] { JsonValue.String("authority-sentinel") })) : field.Value.Kind == "boolean" ? JsonValue.Bool(!field.Value.Boolean) : field.Value.Kind == "integer" ? JsonValue.Number(field.Value.Integer + 1) : field.Value.Kind == "object" ? field.Value.With("authority_sentinel", JsonValue.Number(1)) : JsonValue.String("authority-sentinel");
                Assert.That(CanonicalState.RuleHash(snapshot.With(field.Key, changed)), Is.Not.EqualTo(CanonicalState.RuleHash(snapshot)), field.Key);
            }
            var receiptId = snapshot["command_receipts"].Items[0]["command_id"].Text;
            var withCause = snapshot.With("guidance_state", snapshot["guidance_state"].With("cause_command_id", JsonValue.String(receiptId)));
            Assert.That(CanonicalState.RuleProjection(withCause)["guidance_state"]["cause_command_id"].Integer, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => CanonicalState.RuleProjection(snapshot.With("guidance_state", snapshot["guidance_state"].With("cause_command_id", JsonValue.String("unknown")))));
            Assert.That(CanonicalState.RuleHash(snapshot.With("identity", snapshot["identity"].With("assisted", JsonValue.Bool(true)))), Is.Not.EqualTo(CanonicalState.RuleHash(snapshot)));
            Assert.That(CanonicalState.SortedEntities(new[] { Json("{\"id\":\"b\"}"), Json("{\"id\":\"A\"}"), Json("{\"id\":\"a\"}") }).Items.Select(item => item["id"].Text), Is.EqualTo(new[] { "A", "a", "b" }));
            foreach (var assembly in new[] { typeof(GameCommand).Assembly, typeof(OperationClock).Assembly }) Assert.That(assembly.GetReferencedAssemblies().Any(reference => reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal) || reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal)), Is.False);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c02/assertion/assertion_id=check-c02-a02")]
        public void StrictCommandsImmediatePauseAndExactReceiptDeduplication()
        {
            var examples = new Dictionary<string, string> {
                {"place_equipment",Place},{"move_idle_equipment","{\"equipment_instance_id\":\"eq:1\",\"destination_socket_id\":\"socket\",\"orientation_step\":3}"},
                {"assign_route","{\"subject_kind\":\"vehicle\",\"subject_id\":\"skiff\",\"stops\":[{\"node_id\":\"dock\",\"action\":\"pickup\",\"group_id\":\"group\"}]}"},
                {"set_power_priority","{\"consumer_ids\":[\"pump\",\"clinic\"]}"},{"set_gate","{\"gate_id\":\"gate\",\"open\":false}"},
                {"activate_tool","{\"equipment_instance_id\":\"eq:1\",\"action\":\"cool_segment\",\"segment_id\":\"lava\"}"},{"end_operation","{\"confirm_failure\":true}"},
                {"pause","{\"reason\":\"user\"}"},{"resume","{}"},{"advance_ticks","{\"operation_ticks\":10}"},{"enter_placement","{}"},
                {"confirm_placement","{\"layout_version_id\":\"layout\",\"layout_signature\":\"" + new string('a',64) + "\"}"},{"exit_to_hub","{\"disposition\":\"save_and_exit\"}"} };
            foreach (var pair in examples)
            {
                var command = Command(1, 1, pair.Key, pair.Value); Assert.That(GameCommand.Parse(command.ToJson().CanonicalBytes()).RequestSha256, Is.EqualTo(command.RequestSha256));
                Assert.Throws<CommandException>(() => GameCommand.Read(command.ToJson().With("unexpected", JsonValue.Bool(true))), pair.Key + " outer unknown");
                Assert.Throws<CommandException>(() => GameCommand.Read(command.ToJson().With("payload", command.Payload.ToJson().With("unexpected", JsonValue.Bool(true)))), pair.Key + " payload unknown");
                foreach (string field in command.Payload.ToJson().Members.Keys) Assert.Throws<CommandException>(() => GameCommand.Read(command.ToJson().With("payload", command.Payload.ToJson().Without(field))), pair.Key + " missing " + field);
            }
            foreach (string action in new[] { "enable", "disable", "firebreak", "refill" }) Command(1, 1, "activate_tool", "{\"equipment_instance_id\":\"eq:1\",\"action\":\"" + action + "\"" + (action == "refill" ? ",\"store_id\":\"store\"" : "") + "}");
            Command(1, 1, "assign_route", "{\"subject_kind\":\"group\",\"subject_id\":\"group\",\"stops\":[{\"node_id\":\"refuge\",\"action\":\"evacuate\",\"group_id\":null}]}");
            string validAdvanceWire = Command(1, 0, "advance_ticks", "{\"operation_ticks\":10}").ToJson().Canonical();
            foreach (string payload in new[] { "{\"operation_ticks\":11}", "{\"operation_ticks\":10.0}", "{\"operation_ticks\":true}" }) Assert.Throws<CommandException>(() => GameCommand.Parse(validAdvanceWire.Replace("{\"operation_ticks\":10}", payload)));
            var valid = Command(1, 1, "place_equipment", Place).ToJson();
            foreach (var mutation in new[] { valid.With("schema_version", JsonValue.Number(2)), valid.With("sequence", JsonValue.Number(0)), valid.With("requested_tick", JsonValue.Number(1000001)), valid.With("command_id", JsonValue.String("../bad")), valid.With("actor_input", JsonValue.String("resident_drag")), valid.With("payload", JsonValue.Null()) }) Assert.Throws<CommandException>(() => GameCommand.Read(mutation));
            Assert.That(Assert.Throws<CommandException>(() => GameCommand.Read(valid.With("kind", JsonValue.String("drag_resident")))).Code, Is.EqualTo("UNKNOWN_KIND"));
            Assert.Throws<CommandException>(() => GameCommand.Parse("{\"schema_version\":1,\"schema_version\":1}"));
            Assert.Throws<CommandException>(() => Command(1, 1, "assign_route", "{\"subject_kind\":\"vehicle\",\"subject_id\":\"v\",\"stops\":[{\"node_id\":\"n\",\"action\":\"pickup\",\"group_id\":null}]}"));
            Assert.Throws<CommandException>(() => Command(1, 1, "set_power_priority", "{\"consumer_ids\":[\"a\",\"a\"]}"));

            var content = Content(); var clock = Clock(content, new SyntheticSystems()); var first = Command(1, 1, "place_equipment", Place);
            Assert.That(clock.Admit(Command(1, 1, "place_equipment", Place, run: "wrong-run")).ReasonCode, Is.EqualTo("WRONG_RUN")); Assert.That(clock.NextSequence, Is.EqualTo(1));
            Assert.That(clock.Admit(Command(2, 1, "place_equipment", Place)).ReasonCode, Is.EqualTo("INVALID_SEQUENCE")); Assert.That(clock.NextSequence, Is.EqualTo(1));
            var beforePreviewDomain = clock.ReadSnapshot().Value.Without("state_revision", "last_admitted_sequence", "pending_commands", "command_receipts", "recent_events").Canonical();
            var preview = clock.Admit(first); Assert.That(preview.Status, Is.EqualTo("queued")); Assert.That(preview.Accepted, Is.False); Assert.That(preview.ExecutedTick, Is.EqualTo(-1));
            Assert.That(clock.ReadSnapshot().Value.Without("state_revision", "last_admitted_sequence", "pending_commands", "command_receipts", "recent_events").Canonical(), Is.EqualTo(beforePreviewDomain), "Preview changes accounting only; whole domain authority remains equal.");
            Assert.That(clock.ReadSnapshot().Value["supplies_remaining"].Integer, Is.EqualTo(1)); Assert.That(clock.Admit(first), Is.SameAs(preview));
            Assert.That(clock.Admit(Command(2, 1, "place_equipment", Place, first.CommandId)).ReasonCode, Is.EqualTo("DUPLICATE_ID_MISMATCH"));
            Assert.That(clock.Admit(Command(1, 1, "place_equipment", Place, "different-id")).ReasonCode, Is.EqualTo("SEQUENCE_REUSED"));
            Assert.That(clock.Admit(Command(2, 1, "place_equipment", Place)).Status, Is.EqualTo("queued"));
            var pause = Command(3, 0, "pause", "{\"reason\":\"user\"}"); Assert.That(clock.Apply(pause).Receipt.Accepted, Is.True); Assert.That(clock.OperationTick, Is.Zero); Assert.That(clock.Step(), Is.Null);
            Start(clock); Assert.That(clock.OperationTick, Is.Zero); Assert.That(clock.ScenarioTick, Is.Zero); var step = clock.Step(); Assert.That(step.OperationTick, Is.EqualTo(1));
            Assert.That(clock.Receipts[0].Result.Status, Is.EqualTo("accepted")); Assert.That(clock.Receipts[1].Result.ReasonCode, Is.EqualTo("INSUFFICIENT_SUPPLIES"));
            Assert.That(clock.ReadSnapshot().Value["supplies_remaining"].Integer, Is.Zero); Assert.That(clock.ReadSnapshot().Value["equipment"].Items.Select(item => item["id"].Text), Is.EqualTo(new[] { "eq:1" }));
            Assert.That(step.Events.Select(item => item.ToJson()["kind"].Text), Is.EqualTo(new[] { "CommandResolved", "CommandResolved" }));
            Assert.That(step.Events.Select(item => item.ToJson()["payload"]["status"].Text), Is.EqualTo(new[] { "accepted", "rejected" }));
            Assert.That(step.Events.Select(item => item.ToJson()["event_id"].Text), Is.EqualTo(new[] { "evt:1:0", "evt:1:1" }));
            string before = clock.ReadSnapshot().Value.Canonical(); Assert.That(clock.Admit(first), Is.SameAs(clock.Receipts[0].Result)); Assert.That(clock.ReadSnapshot().Value.Canonical(), Is.EqualTo(before));
            long rejectedSequence = clock.NextSequence; Assert.That(clock.Admit(Command(rejectedSequence, clock.OperationTick + 10, "place_equipment", Place)).ReasonCode, Is.EqualTo("INVALID_TICK")); Assert.That(clock.NextSequence, Is.EqualTo(rejectedSequence + 1));
            var pauseActive = Command(clock.NextSequence, clock.OperationTick, "pause", "{\"reason\":\"user\"}"); clock.Apply(pauseActive);
            var advance = Command(clock.NextSequence, clock.OperationTick, "advance_ticks", "{\"operation_ticks\":10}"); var advanced = clock.Apply(advance); Assert.That(advanced.Advanced.Steps.Count, Is.EqualTo(10));
            long afterAdvance = clock.OperationTick; var retry = clock.Apply(advance); Assert.That(retry.DesiredTransition, Is.EqualTo("none")); Assert.That(retry.Advanced, Is.Null); Assert.That(clock.OperationTick, Is.EqualTo(afterAdvance));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(11)); Assert.Throws<InvalidOperationException>(() => clock.Advance(10));
            clock.Interrupt("tracking"); Assert.That(clock.Apply(Command(clock.NextSequence, clock.OperationTick, "resume")).Receipt.Accepted, Is.False); Assert.That(clock.Step(), Is.Null); clock.RecoverInterruption(); Start(clock);
            foreach (string reason in new[] { "platform", "loading", "tracking", "anchor" })
            {
                long boundary = clock.OperationTick; clock.Interrupt(reason);
                clock.Apply(Command(clock.NextSequence, clock.OperationTick, "pause", "{\"reason\":\"user\"}"));
                Assert.That(clock.Apply(Command(clock.NextSequence, clock.OperationTick, "resume")).Receipt.Accepted, Is.False, reason);
                Assert.Throws<InvalidOperationException>(() => clock.CompleteResumeCountdown()); Assert.That(clock.Step(), Is.Null); Assert.That(clock.OperationTick, Is.EqualTo(boundary));
                clock.RecoverInterruption(); Start(clock);
            }
            for (int index = 0; index < 260; index++) clock.Apply(Command(clock.NextSequence, clock.OperationTick, "pause", "{\"reason\":\"user\"}"));
            Assert.That(clock.ReadSnapshot().Value["recent_events"].Items.Count, Is.EqualTo(256));
            Assert.Throws<InvalidOperationException>(() => new RuntimeEventSink().Emit("CommandResolved", null, null, null, Json("{\"sequence\":1,\"status\":\"accepted\",\"reason_code\":\"OK\"}")));
            var queue = Clock(content, new SyntheticSystems()); for (int index = 1; index <= 32; index++) Assert.That(queue.Admit(Command(index, 1, "set_gate", "{\"gate_id\":\"gate\",\"open\":true}")).Status, Is.EqualTo("queued"));
            Assert.That(queue.Admit(Command(33, 1, "set_gate", "{\"gate_id\":\"gate\",\"open\":true}")).ReasonCode, Is.EqualTo("QUEUE_FULL")); Assert.That(queue.NextSequence, Is.EqualTo(34));
            var precedence = Clock(content, new SyntheticSystems()); precedence.Interrupt("tracking");
            Assert.That(precedence.Admit(Command(1, 1, "move_idle_equipment", "{\"equipment_instance_id\":\"unknown\",\"destination_socket_id\":\"unknown\",\"orientation_step\":0}")).ReasonCode, Is.EqualTo("UNKNOWN_TARGET"), "Frozen domain UNKNOWN_TARGET precedes simultaneous TRACKING_UNAVAILABLE.");
            Assert.That(precedence.Admit(Command(2, 1, "place_equipment", Place)).ReasonCode, Is.EqualTo("TRACKING_UNAVAILABLE"));
            Assert.Throws<ArgumentNullException>(() => Clock(content, null));
            var faultClock = Clock(content, new SyntheticSystems(failAtMovement: true)); Start(faultClock); string validBoundary = faultClock.ReadSnapshot().Value.Canonical(); Assert.That(faultClock.Step(), Is.Null); Assert.That(faultClock.Fault, Does.StartWith("SIMULATION_INVARIANT_FAULT")); Assert.That(faultClock.ReadSnapshot().Value.Canonical(), Is.EqualTo(validBoundary)); Assert.That(faultClock.Step(), Is.Null);
        }

        [Test, NUnit.Framework.Property("case_id", "check-c02/assertion/assertion_id=check-c02-a03")]
        public void ExactProfilesScheduleBeforeCommandsAndDeadlineAfterArrival()
        {
            var content = Content(); var golden = Golden("RO01-relaxed-four-steps");
            foreach (string profile in new[] { "standard", "relaxed", "command" })
            {
                var clock = Clock(content, new SyntheticSystems(), profile); Start(clock); var s = new List<long>(); var a = new List<int>();
                for (int index = 0; index < 4; index++) { clock.Step(); s.Add(clock.ScenarioTick); a.Add(clock.ScenarioAccumulator); }
                Assert.That(s, Is.EqualTo(profile == "relaxed" ? golden["expected"]["scenario_ticks_after_each_step"].Items.Select(item => item.Integer).ToArray() : new long[] { 1, 2, 3, 4 }));
                Assert.That(a, Is.EqualTo(profile == "relaxed" ? golden["expected"]["accumulator_after_each_step"].Items.Select(item => (int)item.Integer).ToArray() : new[] { 0, 0, 0, 0 }));
                clock.Apply(Command(clock.NextSequence, clock.OperationTick, "pause", "{\"reason\":\"user\"}"));
                long oldO = clock.OperationTick, oldS = clock.ScenarioTick;
                Assert.That(clock.Step(), Is.Null); Assert.That(clock.OperationTick, Is.EqualTo(oldO)); Assert.That(clock.ScenarioTick, Is.EqualTo(oldS));
                var result = clock.Apply(Command(clock.NextSequence, clock.OperationTick, "advance_ticks", "{\"operation_ticks\":50}"));
                Assert.That(result.Advanced.Steps.Count, Is.EqualTo(50)); Assert.That(clock.OperationTick - oldO, Is.EqualTo(50)); Assert.That(clock.ScenarioTick - oldS, Is.EqualTo(profile == "relaxed" ? 25 : 50));
            }
            var budget = new FrameStepBudget(); Assert.That(budget.Offer(100000, false), Is.Zero); Assert.That(budget.DebtMilliseconds, Is.Zero); Assert.That(budget.Offer(1000, true), Is.EqualTo(4)); Assert.That(budget.DebtMilliseconds, Is.EqualTo(400)); Assert.That(budget.DiscardedMilliseconds, Is.EqualTo(200)); Assert.That(budget.Offer(0, true), Is.EqualTo(4)); Assert.That(budget.Offer(0, true), Is.Zero);

            var scheduledContent = Content(200, true); var closureSystems = new SyntheticSystems(arrivalTick: 100); var closure = Clock(scheduledContent, closureSystems);
            var initial = closure.ReadSnapshot().Value; Assert.That(initial["event_state"]["warned_event_ids"].Items.Select(item => item.Text), Is.EqualTo(new[] { "a-open", "z-close" }));
            Assert.That(initial["event_state"]["warned_event_ids"].Items.Count, Is.EqualTo(2));
            Start(closure); for (int index = 0; index < 99; index++) closure.Step();
            var route = Command(closure.NextSequence, 100, "assign_route", "{\"subject_kind\":\"group\",\"subject_id\":\"group\",\"stops\":[{\"node_id\":\"refuge\",\"action\":\"evacuate\",\"group_id\":null}]}"); Assert.That(closure.Admit(route).Status, Is.EqualTo("queued")); closure.Step();
            Assert.That(closure.Receipts.Single(item => item.Command.CommandId == route.CommandId).Result.ReasonCode, Is.EqualTo("ROUTE_BLOCKED"));
            var trace = closureSystems.StageTrace.ToArray();
            Assert.That(trace, Is.EqualTo(new[] { "tides", "event:a-open:0", "event:z-close:0", "power", "hazards", "safety", "movement", "objectives" }));
            Assert.That(closure.ReadSnapshot().Value["metrics"].Items.Single(item => item["id"].Text == "arrivals")["value"].Integer, Is.Zero);
            closure.Apply(Command(closure.NextSequence, 100, "pause", "{\"reason\":\"user\"}")); Start(closure); Assert.That(closure.ReadSnapshot().Value["event_state"]["warned_event_ids"].Items.Count, Is.EqualTo(2));

            var deadlineGolden = Golden("RO04-deadline-arrival"); long deadline = deadlineGolden["input"]["deadline_scenario_tick"].Integer;
            var finalClock = Clock(Content(deadline), new SyntheticSystems(arrivalTick: deadline)); Start(finalClock); for (int index = 0; index < deadline; index++) finalClock.Step();
            Assert.That(finalClock.Phase, Is.EqualTo("outcome")); Assert.That(finalClock.ReadSnapshot().Value["outcome"]["success"].Boolean, Is.EqualTo(deadlineGolden["expected"]["success"].Boolean));
            Assert.That(finalClock.ReadSnapshot().Value["outcome"]["rescued"].Integer, Is.EqualTo(1)); Assert.That(finalClock.Step(), Is.Null); Assert.That(finalClock.OperationTick, Is.EqualTo(deadline));
            Assert.That(finalClock.Apply(Command(finalClock.NextSequence, finalClock.OperationTick, "exit_to_hub", "{\"disposition\":\"save_and_exit\"}")).Receipt.Accepted, Is.True);
            Assert.That(finalClock.ApplicationState, Is.EqualTo("hub")); Assert.That(finalClock.OperationTick, Is.EqualTo(deadline));
            var earlyEnd = Clock(content, new SyntheticSystems(arrivalTick: 1)); Start(earlyEnd); Assert.That(earlyEnd.Admit(Command(earlyEnd.NextSequence, 1, "end_operation", "{\"confirm_failure\":false}")).ReasonCode, Is.EqualTo("OBJECTIVES_UNMET"));
            Assert.That(earlyEnd.Admit(Command(earlyEnd.NextSequence, 1, "end_operation", "{\"confirm_failure\":true}")).Status, Is.EqualTo("queued")); earlyEnd.Step(); Assert.That(earlyEnd.ReadSnapshot().Value["outcome"]["rescued"].Integer, Is.EqualTo(1));
            var ending = Clock(content, new SyntheticSystems()); Start(ending); ending.Admit(Command(ending.NextSequence, 1, "end_operation", "{\"confirm_failure\":true}")); ending.Step(); Assert.That(ending.ReadSnapshot().Value["outcome"]["reason"].Text, Is.EqualTo("player_ended_unmet"));
            var guided = Clock(content, new SyntheticSystems(guidedReadyTick: 3), guidance: "guided_first_play"); Start(guided);
            for (int index = 0; index < 3; index++) { guided.Step(); Assert.That(guided.ScenarioTick, Is.Zero); Assert.That(guided.ScenarioAccumulator, Is.Zero); }
            Assert.That(guided.ScenarioEnabled, Is.True); guided.Step(); Assert.That(guided.ScenarioTick, Is.EqualTo(1));
            Assert.That(guided.ReadSnapshot().Value["milestones"].Items.Single(item => item["id"].Text == "operation_started")["operation_tick"].Integer, Is.EqualTo(3));
            Assert.That(CanonicalState.RuleHash(guided.ReadSnapshot().Value), Is.Not.EqualTo(CanonicalState.RuleHash(Clock(content, new SyntheticSystems()).ReadSnapshot().Value)));
            Assert.Throws<InvalidOperationException>(() => new OperationClock(content, "fixture-v1", "fixture-wide", Identity(content).With("modifiers_hash", JsonValue.String(new string('a', 64))), new SyntheticSystems()));
            Assert.Throws<InvalidOperationException>(() => Clock(content, new SyntheticSystems(), "standard", adapter: "head_dwell"));
            Assert.Throws<InvalidOperationException>(() => Clock(content, new SyntheticSystems(), guidance: "unknown-policy"));
            var noGuidance = ContentCompilation.Compile(content.Envelope.payload.ToJson().With("missions", JsonValue.Array(content.Envelope.payload.ToJson()["missions"].Items.Select(mission => mission.With("tutorial_policy", JsonValue.String("none"))))).Canonical());
            Assert.Throws<InvalidOperationException>(() => Clock(noGuidance, new SyntheticSystems(), guidance: "guided_first_play"));
            var challengeContent = Content(challenge: true); var challengeDef = challengeContent.Envelope.payload.challenges.Single(); var variant = challengeContent.ChallengeVariants[challengeDef.id];
            var challengeIdentity = Identity(variant).With("challenge_id", JsonValue.String(challengeDef.id)).With("modifiers_hash", JsonValue.String(challengeDef.ToJson().Sha256()));
            var challengeClock = new OperationClock(challengeContent, "fixture-v1", "fixture-wide", challengeIdentity, new SyntheticSystems()); Assert.That(challengeClock.ReadSnapshot().Value["identity"]["content_hash"].Text, Is.EqualTo(variant.ContentHash));
            Assert.Throws<InvalidOperationException>(() => new OperationClock(challengeContent, "fixture-v1", "fixture-wide", challengeIdentity.With("content_hash", JsonValue.String(challengeContent.ContentHash)), new SyntheticSystems()));
            Assert.Throws<InvalidOperationException>(() => new OperationClock(challengeContent, "fixture-v1", "fixture-wide", challengeIdentity.With("modifiers_hash", JsonValue.String(Json("[]").Sha256())), new SyntheticSystems()));
        }

        // Explicitly synthetic kernel fixture, NOT a rescue/hazard/transport
        // implementation. It models one unit of stock and one scheduled arrival
        // solely to observe command accounting and the mandatory stage order.
        private sealed class SyntheticSystems : IOperationSystems
        {
            private long supplies = 1, deployments, warnings, arrivals, tick;
            private bool roadOpen = true;
            private readonly long arrivalTick, guidedReadyTick;
            private readonly bool failAtMovement;
            // Shared diagnostic sink is deliberately absent from authority and hashes.
            private readonly List<string> trace;
            public IReadOnlyList<string> StageTrace => trace;
            private readonly List<JsonValue> equipment = new List<JsonValue>();
            public SyntheticSystems(long arrivalTick = -1, long guidedReadyTick = -1, bool failAtMovement = false, List<string> diagnostics = null) { this.arrivalTick = arrivalTick; this.guidedReadyTick = guidedReadyTick; this.failAtMovement = failAtMovement; trace = diagnostics ?? new List<string>(); }
            public IOperationSystems Fork()
            {
                var copy = new SyntheticSystems(arrivalTick, guidedReadyTick, failAtMovement, trace) { supplies = supplies, deployments = deployments, warnings = warnings, arrivals = arrivals, tick = tick, roadOpen = roadOpen };
                copy.equipment.AddRange(equipment); return copy;
            }
            public ValidationResult Validate(GameCommand command, OperationContext context)
            {
                if (command.Kind == "place_equipment") return new ValidationResult(supplies < 1 ? "INSUFFICIENT_SUPPLIES" : deployments > 0 ? "STOCK_EXHAUSTED" : "OK");
                if (command.Kind == "assign_route") return new ValidationResult(roadOpen ? "OK" : "ROUTE_BLOCKED");
                if (command.Kind == "set_gate" || command.Kind == "end_operation") return new ValidationResult("OK");
                return new ValidationResult("UNKNOWN_TARGET");
            }
            public void Apply(GameCommand command, OperationContext context)
            {
                if (command.Kind == "place_equipment") { supplies--; deployments++; equipment.Add(Json("{\"id\":\"eq:" + command.Sequence + "\",\"equipment_id\":\"beacon\",\"socket_id\":\"beacon:01\",\"orientation_step\":0,\"lifecycle\":\"deployed\",\"enabled\":true,\"durability\":100,\"parameters\":{}}")); }
                else if (command.Kind == "set_gate") roadOpen = ((SetGate)command.Payload).Open;
                else if (command.Kind != "assign_route") throw new InvalidOperationException("Synthetic fixture unsupported command");
            }
            public void EvaluateTides(OperationContext context) { trace.Clear(); trace.Add("tides"); tick = context.OperationTick; }
            public void PublishWarning(EventDef definition, OperationContext context) { warnings++; trace.Add("warning:" + definition.id); context.Events.Emit("HazardWarning", "wet", null, definition.id, JsonValue.Object(new Dictionary<string, JsonValue> { {"hazard",JsonValue.String("water")},{"region_id",JsonValue.String("wet")},{"effect_scenario_tick",JsonValue.Number(definition.at_scenario_tick)} })); }
            public void ApplyScheduled(ResolvedEventOperation operation, OperationContext context) { trace.Add("event:" + operation.EventId + ":0"); if (operation.Kind == "set_edge_enabled") roadOpen = operation.Value == 1; else if (operation.Kind != "set_source_rate") throw new InvalidOperationException("Synthetic fixture unsupported event"); }
            public void ResolvePowerAndReservations(OperationContext context) { if (!context.HazardPulse) { trace.Clear(); tick = context.OperationTick; } trace.Add("power"); }
            public void SimulateHazardInterval(OperationContext context) { trace.Add("hazards"); }
            public void RecomputeSafetyAndDamage(OperationContext context) { trace.Add("safety"); }
            public void ResolveMovementAndTransfers(OperationContext context) { trace.Add("movement"); if (failAtMovement) throw new InvalidOperationException("synthetic injected invariant"); if (context.OperationTick == arrivalTick && roadOpen) arrivals++; }
            public void DeriveMetricsAndObjectives(OperationContext context) { trace.Add("objectives"); }
            public void EmitMilestone(string milestoneId, OperationContext context) { trace.Add("milestone:" + milestoneId); }
            public void SampleInitialObjectives(OperationContext context) { trace.Add("initial-objectives"); }
            public bool GuidedEpochReady => guidedReadyTick >= 0 && tick >= guidedReadyTick;
            public bool RequiredConditionsHold => arrivals > 0;
            public OutcomeResult EvaluateOutcome(OperationContext context) { trace.Add("outcome"); return new OutcomeResult(arrivals > 0, arrivals > 0 ? 1 : 0, arrivals > 0 ? 1000 : 0, (int)arrivals, 0); }
            public void ValidateBoundary(OperationContext context) { if (supplies < 0 || deployments > 1 || arrivals > 1) throw new InvalidOperationException("synthetic conservation"); }
            private static JsonValue Metric(string id, long value, IEnumerable<string> ids = null) => JsonValue.Object(new Dictionary<string, JsonValue> { {"id",JsonValue.String(id)},{"metric",JsonValue.String("evacuated_by_mode")},{"subject_id",JsonValue.Null()},{"value",JsonValue.Number(value)},{"distinct_resident_ids",JsonValue.Array((ids ?? new string[0]).Select(JsonValue.String))} });
            public JsonValue ReadDomainState()
            {
                var state = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                foreach (string field in new[] { "water", "rate_remainders", "equipment", "vehicles", "residents", "transits", "transfers", "reservations", "routes", "queues", "fire", "supports", "runouts", "tides", "lava", "stores", "producers", "milestones", "metrics", "objective_monitors", "recent_events" }) state.Add(field, Json("[]"));
                state["guidance_state"] = Json("{\"policy\":\"synthetic\",\"step_id\":\"step\",\"completed_step_ids\":[],\"acknowledged_warning\":false}");
                state["supplies_remaining"] = JsonValue.Number(supplies); state["selected_stock"] = Json("[{\"equipment_id\":\"beacon\",\"count\":1}]"); state["deployments"] = Json("[{\"equipment_id\":\"beacon\",\"count\":" + deployments + "}]");
                state["power"] = Json("{\"node_enabled\":[],\"link_enabled\":[],\"consumer_demands\":[],\"priorities\":[],\"grants\":[],\"generator_trip_ids\":[]}");
                state["equipment"] = CanonicalState.SortedEntities(equipment);
                state["event_state"] = Json("{\"next_static_event_index\":0,\"fired_event_ids\":[],\"revealed_event_ids\":[],\"warned_event_ids\":[],\"dynamic_events\":[],\"administrative_edge_enabled\":[],\"edge_current_multipliers\":[],\"gate_open\":[],\"group_held\":[],\"wind_region_codes\":[],\"cooler_socket_enabled\":[],\"shelter_region_enabled\":[],\"resource_warning_thresholds\":[]}");
                state["metrics"] = CanonicalState.SortedEntities(new[] { Metric("arrivals", arrivals) });
                state["water_ledger"] = Json("{\"initial_total_mm3\":0,\"direct_sources_mm3\":0,\"sea_in_mm3\":0,\"drains_mm3\":0,\"sea_out_mm3\":0,\"sprayed_mm3\":0}");
                return JsonValue.Object(state);
            }
        }
    }
}
