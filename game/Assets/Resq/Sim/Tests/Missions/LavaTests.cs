using System.IO;
using System.Text;
using Resq.Sim.Rules;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests
{
    /// <summary>
    /// World 5 lava, coolers, coolant production and shelters (spec 02 §10; acceptance RL01–RL06) on a synthetic dry map.
    /// Lava source <c>first_channel</c> starts at segment flow_a (x10..12, y14..16, under crossing g_cross) and reaches flow_b
    /// (x10..12, y10..12) 60 ticks later. A shelter node at (8,12) has its hazard region over flow_b. The coolant works
    /// (feedstock 3, store 1 of 2) runs on a generator grid.
    /// </summary>
    public static class LavaTests
    {
        const string MapJson = "{" +
            "\"legend\":{\"#\":{\"code\":3,\"elev\":1200}}," +
            "\"regions\":{\"shelter_zone\":{\"x0\":8,\"y0\":11,\"x1\":12,\"y1\":12}}," +
            "\"nodes\":[" +
            "{\"id\":\"camp_start\",\"at\":[5,20],\"capacity\":6},{\"id\":\"camp_assembly\",\"at\":[6,20],\"capacity\":6}," +
            "{\"id\":\"camp_pickup\",\"at\":[7,20],\"capacity\":6},{\"id\":\"west\",\"at\":[8,15],\"capacity\":6}," +
            "{\"id\":\"east\",\"at\":[14,15],\"capacity\":6},{\"id\":\"refuge\",\"at\":[20,15],\"flags\":[\"evacuation\"],\"capacity\":12}," +
            "{\"id\":\"shelter_node\",\"at\":[8,12],\"flags\":[\"shelter\"],\"capacity\":6}]," +
            "\"edges\":[" +
            "{\"id\":\"g_c1\",\"from\":\"camp_start\",\"to\":\"camp_assembly\",\"ticks\":20}," +
            "{\"id\":\"g_c2\",\"from\":\"camp_assembly\",\"to\":\"camp_pickup\",\"ticks\":20}," +
            "{\"id\":\"g_c3\",\"from\":\"camp_pickup\",\"to\":\"west\",\"ticks\":20,\"slots\":4}," +
            "{\"id\":\"g_cross\",\"from\":\"west\",\"to\":\"east\",\"ticks\":40,\"slots\":4}," +
            "{\"id\":\"g_east\",\"from\":\"east\",\"to\":\"refuge\",\"ticks\":40,\"slots\":4}," +
            "{\"id\":\"g_shelter\",\"from\":\"west\",\"to\":\"shelter_node\",\"ticks\":20,\"slots\":4}]," +
            "\"lava\":{\"segments\":[" +
            "{\"id\":\"flow_a\",\"cells\":{\"x0\":10,\"y0\":14,\"x1\":12,\"y1\":16},\"edges\":[\"g_cross\"],\"next\":[{\"to\":\"flow_b\",\"delay\":60}]}," +
            "{\"id\":\"flow_b\",\"cells\":{\"x0\":10,\"y0\":10,\"x1\":12,\"y1\":12}}]," +
            "\"sources\":[{\"id\":\"first_channel\",\"segment\":\"flow_a\"}]}," +
            "\"coolant_stores\":[{\"id\":\"coolant\",\"charges\":1,\"capacity\":2}]," +
            "\"sockets\":[" +
            "{\"id\":\"cooler:01\",\"tool\":\"cooler\",\"at\":[9,15],\"segment\":\"flow_a\",\"coolant_store\":\"coolant\"}," +
            "{\"id\":\"cooler:02\",\"tool\":\"cooler\",\"at\":[9,11],\"segment\":\"flow_b\",\"enabled\":false}," +
            "{\"id\":\"shelter:01\",\"tool\":\"shelter\",\"node\":\"shelter_node\",\"at\":[8,13],\"region\":\"shelter_zone\"}," +
            "{\"id\":\"generator:01\",\"tool\":\"generator\",\"at\":[11,15],\"power\":\"lava_grid\"}," +
            "{\"id\":\"generator:02\",\"tool\":\"generator\",\"at\":[25,5],\"power\":\"works_grid\"}]," +
            "\"power\":{\"nodes\":[{\"id\":\"lava_grid\"},{\"id\":\"works_grid\"}],\"links\":[]}," +
            "\"infrastructure\":[{\"id\":\"cooling_works\",\"power\":\"works_grid\",\"demand\":1,\"priority\":40," +
            "\"producer\":{\"store\":\"coolant\",\"feedstock\":3,\"ticks\":100}}]," +
            "\"roles\":{\"first_channel\":{\"lava_sources\":[\"first_channel\"]},\"crossing_b\":{\"cooler_sockets\":[\"cooler:02\"]}," +
            "\"shelter_a\":{\"shelters\":[\"shelter:01\"]}}" +
            "}";

        static string Ev(string id, int at, int warn, string kind, string role) =>
            $"{{\"id\":\"{id}\",\"at_scenario_tick\":{at},\"warning_at_scenario_tick\":{warn},\"known_at_start\":true," +
            $"\"operations\":[{{\"kind\":\"{kind}\",\"target_role\":\"{role}\",\"value\":1}}]}}";

        static readonly string Lava = Ev("lava", 300, 200, "advance_lava", "first_channel");

        static string Contract(string events) =>
            "{\"id\":\"W5-TEST\",\"world_id\":\"W5\",\"title\":\"Lava fixture\",\"duration_scenario_ticks\":3000," +
            "\"total_residents\":4,\"minimum_rescues\":1,\"supply_budget\":30,\"tutorial_policy\":\"world_mechanic_guidance\"," +
            "\"permitted_tools\":[\"cooler\",\"generator\",\"shelter\"],\"maximum_stock\":{\"cooler\":2,\"generator\":2,\"shelter\":1}," +
            "\"default_loadout\":{\"cooler\":2,\"generator\":2,\"shelter\":1}," +
            "\"groups\":[{\"id\":\"camp\",\"count\":4,\"start_node_id\":\"camp_start\",\"assembly_node_id\":\"camp_assembly\"}]," +
            $"\"events\":[{events}]," +
            "\"milestones\":[{\"id\":\"first_crossing_reheated\",\"kind\":\"first_cooled_crossing_closed\",\"target\":\"first_channel\"}]," +
            "\"objectives\":[{\"id\":\"shelters\",\"tier\":\"required\",\"kind\":\"temporary_shelters_empty\",\"params\":{},\"temporal\":{\"kind\":\"at_end\"}}," +
            "{\"id\":\"critical\",\"tier\":\"critical\",\"kind\":\"rescue_count\",\"params\":{\"minimum\":1},\"temporal\":{\"kind\":\"at_end\"}}]}";

        static string Rows()
        {
            var rows = new StringBuilder();
            for (int r = 0; r < 24; r++) rows.Append(r == 0 ? "" : ",").Append("\"################################\"");
            return rows.ToString();
        }

        static MissionDef Fixture(string events, string mapEdit = null)
        {
            var map = JsonNode.Parse(MapJson);
            map.Set("rows", JsonNode.Parse("[" + Rows() + "]"));
            if (mapEdit != null) foreach (var kv in JsonNode.Parse(mapEdit).Properties()) map.Set(kv.Key, kv.Value);
            var rules = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/equipment_rules.json")));
            return MapCompiler.Compile(map, JsonNode.Parse(Contract(events)), rules, "wide");
        }

        static void RunTo(Simulator sim, long tick, System.Action each = null)
        {
            while (sim.S.C.S < tick && !sim.IsOver) { sim.Step(); each?.Invoke(); }
        }

        static int Seg(MissionDef d, string id) => d.LavaSegIndex[id];

        // RL01: the source warning publishes every downstream arrival >= 100 ticks ahead; each segment arrives once, on time.
        [SimTest("lava")]
        public static void RL01_Source_And_Front_Warnings()
        {
            var d = Fixture(Lava);
            var sim = new Simulator(d, RuleProfile.Standard);
            int a = Seg(d, "flow_a"), b = Seg(d, "flow_b");
            RunTo(sim, 199);
            Check.Equal(-1L, sim.S.LPlanned[a], "nothing published before the warning");
            RunTo(sim, 200);
            Check.Equal(300L, sim.S.LPlanned[a], "source arrival published");
            Check.Equal(360L, sim.S.LPlanned[b], "propagated front published with the source warning");
            Check.True(sim.S.LPlanned[b] - 200 >= 100, "the front has at least 100 ticks of warning");
            RunTo(sim, 299);
            Check.Equal(LavaState.Dormant, sim.SegmentState(a), "dormant before arrival");
            Check.True(sim.S.EdgeOpen[d.EdgeIndex["g_cross"]], "crossing open while dormant");
            RunTo(sim, 300);
            Check.Equal(LavaState.Hot, sim.SegmentState(a), "Hot at its scheduled arrival");
            Check.Equal(1000, sim.S.LHeat[a], "arrival heat 1000");
            Check.False(sim.S.EdgeOpen[d.EdgeIndex["g_cross"]], "Hot closes the crossing");
            RunTo(sim, 359);
            Check.Equal(LavaState.Dormant, sim.SegmentState(b), "successor waits its delay");
            RunTo(sim, 360);
            Check.Equal(LavaState.Hot, sim.SegmentState(b), "successor arrives exactly 60 ticks later");

            bool shortWarn = false, shortDelay = false;
            try { Fixture(Ev("lava", 300, 250, "advance_lava", "first_channel")); } catch (ContentException) { shortWarn = true; }
            try
            {
                Fixture(Lava, "{\"lava\":{\"segments\":[{\"id\":\"flow_a\",\"cells\":[[10,15]],\"next\":[{\"to\":\"flow_b\",\"delay\":40}]}," +
                              "{\"id\":\"flow_b\",\"cells\":[[10,11]]}],\"sources\":[{\"id\":\"first_channel\",\"segment\":\"flow_a\"}]}}");
            }
            catch (ContentException) { shortDelay = true; }
            Check.True(shortWarn, "lava with under 100 ticks of warning is rejected");
            Check.True(shortDelay, "a successor delay under 50 ticks is rejected");
        }

        static Simulator CooledAt310(MissionDef d, out int cooler)
        {
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(d.SocketIndex["cooler:01"], ToolKind.Cooler));
            RunTo(sim, 309);
            cooler = sim.S.SocketInstance[d.SocketIndex["cooler:01"]];
            sim.Submit(Command.Cool(cooler));
            RunTo(sim, 310);
            return sim;
        }

        // RL02: Cooling 50 → Crusted 200 (heat 100, crossing open) → Reheating at the exact expiry (heat 600, closed) → Hot 50 later.
        [SimTest("lava")]
        public static void RL02_Cooling_Crust_Reheat_Boundaries()
        {
            var d = Fixture(Lava);
            var sim = CooledAt310(d, out int cooler);
            int a = Seg(d, "flow_a"), cross = d.EdgeIndex["g_cross"];
            int milestone = 0;
            Check.Equal(LavaState.Cooling, sim.SegmentState(a), "cooling starts at activation");
            Check.Equal(0, sim.S.EqCharges[cooler], "one charge consumed");
            RunTo(sim, 359);
            Check.Equal(LavaState.Cooling, sim.SegmentState(a), "still cooling at +49");
            Check.False(sim.S.EdgeOpen[cross], "closed while cooling");
            RunTo(sim, 360);
            Check.Equal(LavaState.Crusted, sim.SegmentState(a), "crusted at +50");
            Check.Equal(100, sim.S.LHeat[a], "crust heat 100");
            Check.True(sim.S.EdgeOpen[cross], "crust opens the crossing");
            RunTo(sim, 559);
            Check.True(sim.S.EdgeOpen[cross], "open through the crust window");
            Check.Equal(-1L, sim.S.MilestoneS[milestone], "milestone not yet");
            RunTo(sim, 560);
            Check.Equal(LavaState.Reheating, sim.SegmentState(a), "reheating at the exact crust expiry");
            Check.Equal(600, sim.S.LHeat[a], "reheat heat 600");
            Check.False(sim.S.EdgeOpen[cross], "closed before movement on the expiry tick");
            Check.Equal(560L, sim.S.MilestoneS[milestone], "first_cooled_crossing_closed when it actually re-enters Reheating");
            RunTo(sim, 609);
            Check.Equal(LavaState.Reheating, sim.SegmentState(a), "reheating for 50 ticks");
            RunTo(sim, 610);
            Check.Equal(LavaState.Hot, sim.SegmentState(a), "Hot again");
            Check.Equal(1000, sim.S.LHeat[a], "Hot heat restored");
        }

        // RL03: a cooler cannot restart the timer while Cooling/Crusted/Reheating; the charge is not consumed by a refused activation.
        [SimTest("lava")]
        public static void RL03_No_Repeated_Cooldown_Reset()
        {
            var d = Fixture(Lava);
            var sim = CooledAt310(d, out int cooler);
            int a = Seg(d, "flow_a"), store = d.CoolantIndex["coolant"];
            sim.Submit(Command.Refill(cooler, store));
            RunTo(sim, 320);
            Check.Equal(1, sim.S.EqCharges[cooler], "refilled from the bound store while idle");
            foreach (long t in new long[] { 330, 400, 580 })
            {
                RunTo(sim, t - 1);
                sim.Submit(Command.Cool(cooler));
                RunTo(sim, t);
                Check.Equal(310L, sim.S.LCoolStart[a], $"timer unchanged by an activation at {t}");
                Check.Equal(1, sim.S.EqCharges[cooler], $"charge kept after the refused activation at {t}");
            }
            RunTo(sim, 620);
            sim.Submit(Command.Cool(cooler));
            RunTo(sim, 621);
            Check.Equal(LavaState.Cooling, sim.SegmentState(a), "once Hot again it can be cooled");
            Check.Equal(0, sim.S.EqCharges[cooler], "that activation spends the charge");
        }

        // RL04: feedstock + WIP + store + loaded charges + spent is conserved; full storage blocks a job; power loss freezes progress.
        [SimTest("lava")]
        public static void RL04_Finite_Coolant_Conservation()
        {
            var d = Fixture(Lava);
            var sim = new Simulator(d, RuleProfile.Standard);
            int works = d.InfraIndex["cooling_works"], store = d.CoolantIndex["coolant"];
            sim.Submit(Command.Place(d.SocketIndex["generator:02"], ToolKind.Generator));
            sim.Submit(Command.Place(d.SocketIndex["cooler:01"], ToolKind.Cooler));
            long total = 3 + 1 + 1; // feedstock + stored + the cooler's initial charge
            int gen = -1;
            RunTo(sim, 101, () => Check.Equal(total, sim.CoolantAccount(), "conserved"));
            Check.Equal(2, sim.S.CoolantStore[store], "one charge produced");
            Check.Equal(2, sim.S.WorksFeedstock[works], "one unit consumed");
            RunTo(sim, 250, () => Check.Equal(total, sim.CoolantAccount(), "conserved"));
            Check.Equal(0, sim.S.WorksWip[works], "full storage prevents a new job");
            Check.Equal(2, sim.S.WorksFeedstock[works], "no feedstock reserved while full");

            int cooler = sim.S.SocketInstance[d.SocketIndex["cooler:01"]];
            gen = sim.S.SocketInstance[d.SocketIndex["generator:02"]];
            RunTo(sim, 309);
            sim.Submit(Command.Cool(cooler));
            RunTo(sim, 310);
            sim.Submit(Command.Refill(cooler, store));
            RunTo(sim, 340, () => Check.Equal(total, sim.CoolantAccount(), "conserved"));
            Check.Equal(1, sim.S.WorksWip[works], "store below capacity: a new job started");
            int progress = sim.S.WorksProgress[works];
            sim.Submit(Command.Disable(gen));
            RunTo(sim, 390, () => Check.Equal(total, sim.CoolantAccount(), "conserved"));
            Check.Equal(progress, sim.S.WorksProgress[works], "power loss freezes progress (the disable applies before the interval's grant)");
            Check.Equal(1, sim.S.WorksWip[works], "the work-in-progress unit is retained");
            sim.Submit(Command.Enable(gen));
            RunTo(sim, 1500, () => Check.Equal(total, sim.CoolantAccount(), "conserved"));
            Check.Equal(2, sim.S.CoolantStore[store], "the resumed job completed and refilled the store to capacity");
            Check.Equal(0, sim.S.WorksWip[works], "no job while full");
            Check.Equal(1, sim.S.WorksFeedstock[works], "one feedstock unit left unreserved");
        }

        static Simulator ShelterRun(MissionDef d, bool route)
        {
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(d.SocketIndex["shelter:01"], ToolKind.Shelter));
            if (route) sim.Submit(Command.GroupRoute(d.GroupIndex["camp"], d.NodeIndex["shelter_node"], false));
            return sim;
        }

        // RL05: 600 protected pulses; the pulse that reaches zero emits one exhaustion and is still protected; the next is not.
        [SimTest("shelter")]
        public static void RL05_Shelter_Exact_Final_Protected_Pulse()
        {
            var d = Fixture(Ev("front", 300, 200, "enable_shelter_hazard", "shelter_a"));
            var sim = ShelterRun(d, route: true);
            int node = d.NodeIndex["shelter_node"];
            RunTo(sim, 299);
            Check.Equal(4, sim.S.NodeOccupancy[node], "all four sheltering before the front");
            int inst = sim.S.SocketInstance[d.SocketIndex["shelter:01"]];
            Check.Equal(600, sim.S.ShelterRemaining[inst], "safe occupied shelter consumes nothing");
            var residents = d.GroupResidents[d.GroupIndex["camp"]];
            RunTo(sim, 898, () => { foreach (int r in residents) Check.Equal(0, sim.S.RExposure[r], "protected: no exposure"); });
            Check.Equal(1, sim.S.ShelterRemaining[inst], "one unit left before the last protected pulse");
            RunTo(sim, 899);
            Check.Equal(0, sim.S.ShelterRemaining[inst], "600th hazardous pulse reaches zero");
            Check.Equal(1, sim.S.C.ShelterExhaustions, "one exhaustion on that pulse");
            foreach (int r in residents) Check.Equal(0, sim.S.RExposure[r], "the last unit still protects its interval");
            RunTo(sim, 900);
            foreach (int r in residents) Check.Equal(1, sim.S.RExposure[r], "the next hazardous pulse is unprotected");
            RunTo(sim, 918);
            Check.Equal(0, sim.S.C.AwaitingBackup, "19 unprotected pulses: not yet");
            RunTo(sim, 919);
            Check.Equal(4, sim.S.C.AwaitingBackup, "20 consecutive unprotected pulses: awaiting backup");
            Check.Equal(1, sim.S.C.ShelterExhaustions, "exhaustion counted once");

            var empty = ShelterRun(d, route: false);
            RunTo(empty, 1000);
            Check.Equal(600, empty.S.ShelterRemaining[empty.S.SocketInstance[d.SocketIndex["shelter:01"]]], "an empty shelter consumes nothing");
        }

        // RL06: a temporary shelter is never a refuge: no evacuation credit, and an occupied shelter blocks success.
        [SimTest("shelter")]
        public static void RL06_No_Refuge_Credit()
        {
            var d = Fixture(Lava);
            var sim = ShelterRun(d, route: true);
            int g = d.GroupIndex["camp"], node = d.NodeIndex["shelter_node"];
            sim.Submit(Command.GroupRoute(g, node, true)); // evacuate-to-shelter is not a legal order
            RunTo(sim, 2999);
            Check.Equal(0, sim.S.C.Evacuated, "nobody counts as evacuated in a shelter");
            Check.True(sim.S.NodeOccupancy[node] > 0, "they are still sheltering");
            RunTo(sim, 3000);
            Check.True(sim.IsOver, "deadline reached");
            Check.False(sim.S.C.Success, "occupied temporary shelter cannot pass the terminal conditions");

            bool threw = false;
            try { Fixture(Lava, "{\"nodes\":[{\"id\":\"camp_start\",\"at\":[5,20],\"capacity\":6},{\"id\":\"camp_assembly\",\"at\":[6,20],\"capacity\":6}," +
                                "{\"id\":\"camp_pickup\",\"at\":[7,20],\"capacity\":6},{\"id\":\"west\",\"at\":[8,15],\"capacity\":6}," +
                                "{\"id\":\"east\",\"at\":[14,15],\"capacity\":6},{\"id\":\"refuge\",\"at\":[20,15],\"flags\":[\"evacuation\"],\"capacity\":12}," +
                                "{\"id\":\"shelter_node\",\"at\":[8,12],\"flags\":[\"shelter\",\"evacuation\"],\"capacity\":6}]}"); }
            catch (ContentException) { threw = true; }
            Check.True(threw, "a shelter node flagged as a final refuge is rejected");
        }

        // A Hot arrival disables unprotected equipment on its cells at once (one loss); it never counts spent coolant as damage.
        [SimTest("lava")]
        public static void Hot_Arrival_Breaks_Equipment_On_The_Flow()
        {
            var d = Fixture(Lava);
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(d.SocketIndex["generator:01"], ToolKind.Generator));
            sim.Submit(Command.Place(d.SocketIndex["cooler:01"], ToolKind.Cooler));
            RunTo(sim, 299);
            int gen = sim.S.SocketInstance[d.SocketIndex["generator:01"]];
            Check.False(sim.S.EqBroken[gen], "intact before arrival");
            RunTo(sim, 300);
            Check.True(sim.S.EqBroken[gen], "generator on the flow is disabled on arrival");
            Check.Equal(1, sim.S.C.Losses, "one loss");
            int cooler = sim.S.SocketInstance[d.SocketIndex["cooler:01"]];
            sim.Submit(Command.Cool(cooler));
            RunTo(sim, 400);
            Check.Equal(1, sim.S.C.Losses, "spent coolant is not a loss");
            Check.False(sim.S.EqBroken[cooler], "the cooler bound to the flow is not disabled by it");
        }

        // enable_cooler_socket: a cooler on a disabled socket cannot activate until the event enables it.
        [SimTest("lava")]
        public static void Cooler_Socket_Enabled_By_Event()
        {
            var d = Fixture(Lava + "," + Ev("open_b", 800, 700, "enable_cooler_socket", "crossing_b"));
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(d.SocketIndex["cooler:02"], ToolKind.Cooler));
            RunTo(sim, 400);
            int cooler = sim.S.SocketInstance[d.SocketIndex["cooler:02"]], b = Seg(d, "flow_b");
            Check.Equal(LavaState.Hot, sim.SegmentState(b), "flow_b is Hot");
            sim.Submit(Command.Cool(cooler));
            RunTo(sim, 401);
            Check.Equal(LavaState.Hot, sim.SegmentState(b), "disabled socket: activation refused");
            Check.Equal(1, sim.S.EqCharges[cooler], "charge kept");
            RunTo(sim, 800);
            sim.Submit(Command.Cool(cooler));
            RunTo(sim, 801);
            Check.Equal(LavaState.Cooling, sim.SegmentState(b), "enabled socket: cooling starts");
        }

        // Spec 02 §6 with §10: a known crust expiry is a hard closure; nobody steps onto a crossing they cannot finish.
        [SimTest("lava")]
        public static void Admission_Respects_The_Known_Crust_Expiry()
        {
            var d = Fixture(Lava);
            var sim = CooledAt310(d, out int cooler);
            int cross = d.EdgeIndex["g_cross"], g = d.GroupIndex["camp"];
            RunTo(sim, 440);
            sim.Submit(Command.GroupRoute(g, d.NodeIndex["refuge"], true));
            bool waitedAtBank = false;
            RunTo(sim, 700, () =>
            {
                foreach (int r in d.GroupResidents[g])
                {
                    if (sim.S.RState[r] == (byte)ResidentState.Walking && sim.S.REdge[r] == cross && sim.S.RProgress[r] == 0)
                        Check.True(sim.S.C.S + 40 <= 560, $"entered the crossing at {sim.S.C.S} and could not finish before the reheat at 560");
                    if (sim.S.RReversing[r]) Check.True(false, "no resident is caught on the crust and turned back");
                    if (sim.S.C.S > 521 && sim.S.C.S < 560 && sim.S.RState[r] == (byte)ResidentState.AtNode && sim.S.RNode[r] == d.NodeIndex["west"]) waitedAtBank = true;
                }
            });
            Check.True(waitedAtBank, "someone reached the bank too late and waited instead of crossing");
        }
    }
}
