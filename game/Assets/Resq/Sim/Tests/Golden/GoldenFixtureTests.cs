using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Resq.Sim.Rules;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests
{
    /// <summary>Hand-authored golden cases from specification/fixtures/runtime-golden-v1.json.</summary>
    public static class GoldenFixtureTests
    {
        static JsonNode _cases;

        static JsonNode Case(string id)
        {
            if (_cases == null)
            {
                string path = RepoPaths.Resolve("specification/fixtures/runtime-golden-v1.json");
                _cases = JsonNode.Parse(File.ReadAllText(path))["cases"];
            }
            foreach (var c in _cases.Elements())
                if (c.Str("id") == id) return c;
            throw new CheckFailedException($"fixture {id} missing");
        }

        [SimTest]
        public static void RU01_FloorAndCeilNegative()
        {
            var c = Case("RU01-floor-negative");
            long n = c["input"].Long("numerator"), d = c["input"].Long("denominator");
            Check.Equal(c["expected"].Long("floor_div"), IntMath.FloorDiv(n, d));
            Check.Equal(c["expected"].Long("ceil_div"), IntMath.CeilDiv(n, d));
        }

        [SimTest]
        public static void RU03_ScoreRoundedOnce()
        {
            var i = Case("RU03-score-once")["input"];
            var e = Case("RU03-score-once")["expected"];
            var r = Score.Compute(i.Long("rescued"), i.Long("population"), i.Long("required_complete"), i.Long("required_total"),
                i.Long("protection_complete"), i.Long("protection_total"), i.Long("remaining_supplies"), i.Long("budget"));
            Check.Equal(e.Long("exact_numerator").ToString(), r.Numerator.ToString());
            Check.Equal(e.Long("exact_denominator").ToString(), r.Denominator.ToString());
            Check.Equal(e.Long("rounded_score"), r.Rounded);
        }

        [SimTest]
        public static void RO01_RelaxedFourSteps()
        {
            var c = Case("RO01-relaxed-four-steps");
            long o = 0, s = 0; int acc = 0;
            var scen = new long[4]; var accs = new long[4];
            for (int k = 0; k < 4; k++)
            {
                ClockRules.Step(ref o, ref s, ref acc, 1, true);
                scen[k] = s; accs[k] = acc;
            }
            Check.Sequence(Longs(c["expected"]["scenario_ticks_after_each_step"]), scen);
            Check.Sequence(Longs(c["expected"]["accumulator_after_each_step"]), accs);
            Check.Equal(c["expected"].Long("operation_tick"), o);
        }

        [SimTest]
        public static void RW01_SourceCarry()
        {
            var c = Case("RW01-source-carry");
            long rem = 0, total = 0;
            var inj = new long[10]; var rems = new long[10];
            for (int k = 0; k < 10; k++)
            {
                inj[k] = WaterRules.RatePulse(c["input"].Long("rate_mm3_per_second"), ref rem);
                rems[k] = rem; total += inj[k];
            }
            Check.Sequence(Longs(c["expected"]["injected_per_pulse_mm3"]), inj);
            Check.Sequence(Longs(c["expected"]["remainder_after_each_pulse"]), rems);
            Check.Equal(c["expected"].Long("total_injected_mm3"), total);
        }

        [SimTest]
        public static void RW03_LargestRemainderTie()
        {
            var c = Case("RW03-largest-remainder-tie");
            var reqs = c["input"]["requests"];
            var q = new long[reqs.Count];
            for (int k = 0; k < q.Length; k++) q[k] = reqs[k].Long("requested_mm3");
            var g = new long[q.Length]; var scratch = new long[q.Length];
            WaterRules.Allocate(c["input"].Long("available_mm3"), q, g, scratch);
            var exp = c["expected"]["grants_mm3"];
            for (int k = 0; k < q.Length; k++) Check.Equal(exp.Long(reqs[k].Str("id")), g[k], reqs[k].Str("id"));
        }

        [SimTest]
        public static void RW04_PassivePumpDrainCompete()
        {
            var c = Case("RW04-passive-pump-drain-compete");
            var i = c["input"]; var e = c["expected"];
            var nodes = i["water_nodes"];
            long volA = nodes[0].Long("volume_mm3"), areaA = nodes[0].Long("area_mm2"), elevA = nodes[0].Long("elevation_mm");
            long elevB = nodes[1].Long("elevation_mm"), areaB = nodes[1].Long("area_mm2");
            long elevC = nodes[2].Long("elevation_mm"), areaC = nodes[2].Long("area_mm2");
            long srcRem = 0;
            long injected = WaterRules.RatePulse(i["source"].Long("rate_mm3_per_second"), ref srcRem);
            long availA = volA + injected;
            Check.Equal(e.Long("after_source_available_a_mm3"), availA);
            long headA = WaterRules.Head(elevA, availA, areaA);
            long headB = WaterRules.Head(elevB, 0, areaB);
            long headC = WaterRules.Head(elevC, 0, areaC);
            Check.Equal(e.Long("head_a_mm"), headA);
            Check.Equal(e.Long("head_c_mm"), headC);
            var link = i["passive_link"];
            long passive = WaterRules.PassiveRequest(headA, headB, link.Long("sill_mm"), false,
                link.Long("conductance_mm2_per_second"), link.Long("maximum_transfer_mm3"));
            long pumpRem = 0;
            long pump = WaterRules.PumpRequest(headA, headC, ref pumpRem);
            long sinkRem = 0;
            long sink = WaterRules.RatePulse(i["sink"].Long("rate_mm3_per_second"), ref sinkRem);
            // Request IDs in ordinal order: edge:ab, pump:ac, sink:a
            var q = new[] { passive, pump, sink };
            Check.Equal(e["requests_mm3"].Long("edge:ab"), passive);
            Check.Equal(e["requests_mm3"].Long("pump:ac"), pump);
            Check.Equal(e["requests_mm3"].Long("sink:a"), sink);
            var g = new long[3]; var scratch = new long[3];
            WaterRules.Allocate(availA, q, g, scratch);
            Check.Equal(e["grants_mm3"].Long("edge:ab"), g[0]);
            Check.Equal(e["grants_mm3"].Long("pump:ac"), g[1]);
            Check.Equal(e["grants_mm3"].Long("sink:a"), g[2]);
            long newA = availA - g[0] - g[1] - g[2];
            Check.Equal(e["new_volumes_mm3"].Long("a"), newA);
            Check.Equal(e["new_volumes_mm3"].Long("b"), g[0]);
            Check.Equal(e["new_volumes_mm3"].Long("c"), g[1]);
            Check.Equal(e.Long("new_total_mm3"), newA + g[0] + g[1]);
        }

        [SimTest]
        public static void RW05_PumpHeadDerating()
        {
            var c = Case("RW05-pump-head-derating");
            var lifts = Longs(c["input"]["lifts_mm"]);
            var req = new long[lifts.Length];
            for (int k = 0; k < lifts.Length; k++)
            {
                long rem = 0;
                req[k] = WaterRules.PumpRequest(0, lifts[k], ref rem);
            }
            Check.Sequence(Longs(c["expected"]["requested_mm3_per_pulse"]), req);
        }

        [SimTest]
        public static void RE05_BarrierThresholds()
        {
            var c = Case("RE05-barrier-thresholds");
            var diffs = Longs(c["input"]["head_differences_mm"]);
            var warn = new bool[diffs.Length]; var dur = new long[diffs.Length];
            for (int k = 0; k < diffs.Length; k++)
            {
                int d = 100;
                warn[k] = EquipmentRules.BarrierWarning(diffs[k]);
                EquipmentRules.BarrierStrain(diffs[k], ref d);
                dur[k] = d;
            }
            Check.Sequence(Bools(c["expected"]["warning"]), warn);
            Check.Sequence(Longs(c["expected"]["durability_after_one_pulse"]), dur);
        }

        [SimTest]
        public static void RF01_DownwindHeating()
        {
            var c = Case("RF01-downwind-heating");
            var i = c["input"]; var e = c["expected"];
            long sprayRem = 0;
            int w = FireRules.UpdateWetness(i.Int("recipient_wetness"), i.Long("spray_mm3"), ref sprayRem);
            Check.Equal(e.Int("new_wetness"), w);
            int incoming = FireRules.IncomingHeat(i.Int("old_burning_downwind_neighbors"), 0, 0);
            Check.Equal(e.Int("incoming_heat"), incoming);
            Check.Equal(e.Int("heat_loss"), FireRules.HeatLoss(w, i.Int("extra_cooling")));
            int h = FireRules.NextHeat(FireState.Unburned, i.Int("recipient_heat"), i.Int("recipient_fuel"), w, incoming, 0);
            Check.Equal(e.Int("new_heat"), h);
            Check.Equal(e.Int("new_fuel"), FireRules.NextFuel(FireState.Unburned, i.Int("recipient_fuel"), w));
            Check.True(h >= 300, "unburned becomes heating at H>=300");
            Check.Equal(e.Long("earliest_ignition_tick"), i.Long("scenario_tick") + FireRules.IgnitionWarningTicks);
        }

        [SimTest]
        public static void RS01_StabilityLossCarry()
        {
            var c = Case("RS01-stability-loss-carry");
            var i = c["input"]; var e = c["expected"];
            int b = i.Int("initial_base_stability"), rem = i.Int("initial_remainder");
            Check.Equal(e.Int("total_loss_per_second"),
                StabilityRules.TotalLossPerSecond(i.Int("loss_points_per_second"), i.Int("static_load_points"), i.Int("resident_load")));
            for (int k = 0; k < i.Int("hazard_pulses"); k++)
                b -= StabilityRules.PulseLoss(i.Int("loss_points_per_second"), i.Int("static_load_points"), i.Int("resident_load"), ref rem);
            Check.Equal(e.Int("base_stability_after_ten_pulses"), b);
            Check.Equal(e.Int("effective_stability"), StabilityRules.Effective(b, i.Int("static_load_points"), i.Int("braces")));
            Check.Equal(e.Int("remainder"), rem);
        }

        [SimTest]
        public static void RI01_NegativeTideInterpolation()
        {
            var c = Case("RI01-negative-tide-interpolation");
            var k = c["input"]["knots"];
            var ticks = Longs(c["input"]["sample_ticks"]);
            var levels = new long[ticks.Length];
            for (int n = 0; n < ticks.Length; n++)
                levels[n] = TideRules.Interpolate(k[0].Long("scenario_tick"), k[0].Long("level_mm"),
                                                  k[1].Long("scenario_tick"), k[1].Long("level_mm"), ticks[n]);
            Check.Sequence(Longs(c["expected"]["levels_mm"]), levels);
        }

        [SimTest]
        public static void RI02_ContinuousRampInterruption()
        {
            var c = Case("RI02-continuous-ramp-interruption");
            var i = c["input"]; var r = i["first_ramp"]; var e = c["expected"];
            long atInterrupt = TideRules.RampLevel(r.Long("start_tick"), r.Long("start_level_mm"), r.Long("end_tick"),
                                                   r.Long("target_level_mm"), i.Long("interrupt_tick"));
            Check.Equal(e.Long("level_at_interruption_mm"), atInterrupt);
            long t0 = i.Long("interrupt_tick");
            long sample = TideRules.RampLevel(t0, atInterrupt, t0 + i.Long("second_duration_ticks"),
                                              i.Long("second_target_level_mm"), i.Long("sample_tick"));
            Check.Equal(e.Long("sample_level_mm"), sample);
        }

        [SimTest]
        public static void RL02_CoolingBoundaries()
        {
            var c = Case("RL02-cooling-boundaries");
            var i = c["input"]; var e = c["expected"];
            var ticks = Longs(i["sample_ticks"]);
            var states = new string[ticks.Length]; var open = new bool[ticks.Length];
            for (int k = 0; k < ticks.Length; k++)
            {
                var st = LavaRules.StateAfterCooling(i.Long("activation_scenario_tick"), ticks[k]);
                states[k] = st.ToString().ToLowerInvariant();
                open[k] = LavaRules.CrossingOpen(st);
            }
            Check.Sequence(Strings(e["states"]), states);
            Check.Sequence(Bools(e["new_admission_open"]), open);
        }

        [SimTest]
        public static void RL05_LastShelterProtectionPulse()
        {
            var c = Case("RL05-last-shelter-protection-pulse");
            var i = c["input"]; var e = c["expected"];
            int remaining = i.Int("protection_remaining"), ex = 0;
            var prot = new bool[i.Int("hazard_pulses")]; var rems = new long[prot.Length];
            for (int k = 0; k < prot.Length; k++)
            {
                prot[k] = ShelterRules.Pulse(ref remaining, i.Flag("occupied"), i.Flag("externally_hazardous"), ref ex);
                rems[k] = remaining;
            }
            Check.Sequence(Bools(e["protected_each_interval"]), prot);
            Check.Sequence(Longs(e["remaining_after_each_interval"]), rems);
            Check.Equal(e.Int("exhaustions"), ex);
        }

        [SimTest]
        public static void RJ01_StrictBefore()
        {
            var c = Case("RJ01-strict-before");
            var firsts = Longs(c["input"]["first_true_operation_ticks"]);
            long closing = c["input"].Long("closing_milestone_operation_tick");
            var passes = new bool[firsts.Length];
            for (int k = 0; k < firsts.Length; k++) passes[k] = TemporalRules.EverBeforePasses(firsts[k], closing);
            Check.Sequence(Bools(c["expected"]["passes"]), passes);
        }

        [SimTest]
        public static void RJ05_EndBeforeFutureMilestone()
        {
            var c = Case("RJ05-end-before-future-milestone");
            Check.True(TemporalRules.EverBeforePasses(c["input"].Long("first_true_operation_tick"), -1));
        }

        [SimTest]
        public static void RP01_CanonicalJsonAndHash()
        {
            var c = Case("RP01-canonical-json");
            string canon = c["input"].ToCanonical();
            Check.Equal(c["expected"].Str("canonical_utf8_text"), canon);
            Check.Equal(c["expected"].Str("sha256"), Sha256Hex(canon));
            var empty = Case("RP01-empty-modifiers-hash");
            Check.Equal(empty["expected"].Str("sha256"), Sha256Hex(empty["input"].ToCanonical()));
        }

        [SimTest]
        public static void RP01_XorShift32SeedOne()
        {
            var c = Case("RP01-xorshift32-seed-one");
            var rng = new XorShift32((uint)c["input"].Long("state"));
            var outs = new long[c["input"].Int("draws")];
            for (int k = 0; k < outs.Length; k++) outs[k] = rng.Next();
            Check.Sequence(Longs(c["expected"]["outputs"]), outs);
            Check.Equal(c["expected"].Long("final_state"), (long)rng.State);
        }

        // ---------- helpers ----------
        static long[] Longs(JsonNode arr)
        {
            var r = new long[arr.Count];
            for (int i = 0; i < r.Length; i++) r[i] = arr[i].AsLong();
            return r;
        }

        static bool[] Bools(JsonNode arr)
        {
            var r = new bool[arr.Count];
            for (int i = 0; i < r.Length; i++) r[i] = arr[i].AsBool();
            return r;
        }

        static string[] Strings(JsonNode arr)
        {
            var r = new string[arr.Count];
            for (int i = 0; i < r.Length; i++) r[i] = arr[i].AsString();
            return r;
        }

        static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    public static class RepoPaths
    {
        /// <summary>Find a repository-relative file from the current directory (repo root or game/).</summary>
        public static string Resolve(string relative)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException(relative);
        }
    }
}
