using System.IO;
using System.Text;
using Resq.Sim.Rules;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests
{
    /// <summary>
    /// World 4 tides, sea exchange, relays and the dock-queue metric (spec 02 §4, §5, §9) on a synthetic harbor: a channel
    /// along y=10 opens to the sea at (0,10) (datum 300 mm, so the channel holds 300 mm at low tide); a low causeway cell
    /// (10,11) at 300 mm; a relay platform (12,11) at 450 mm on the plant→lamp power link.
    /// </summary>
    public static class TideTests
    {
        static string Map(string tideExtra)
        {
            var grid = new char[24, 32];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++) grid[y, x] = '#';
            for (int x = 0; x <= 20; x++) grid[10, x] = '~';
            grid[11, 10] = 'c';
            grid[11, 12] = 'p';
            var rows = new StringBuilder();
            for (int r = 0; r < 24; r++)
            {
                int y = 23 - r;
                var row = new StringBuilder();
                for (int x = 0; x < 32; x++) row.Append(grid[y, x]);
                rows.Append(r == 0 ? "" : ",").Append('"').Append(row).Append('"');
            }
            return "{" +
                $"\"rows\":[{rows}]," +
                "\"legend\":{\"#\":{\"code\":3,\"elev\":1200},\"~\":{\"code\":0,\"elev\":0,\"init_depth\":300,\"cond\":2000000}," +
                "\"c\":{\"code\":2,\"elev\":300,\"cond\":2000000},\"p\":{\"code\":1,\"elev\":450,\"cond\":2000000}}," +
                "\"regions\":{\"harbor\":\"~\"}," +
                "\"tides\":[{\"id\":\"tide\",\"datum\":300,\"cells\":[[0,10]]" + tideExtra + "}]," +
                "\"nodes\":[" +
                "{\"id\":\"dock_a\",\"at\":[3,11],\"flags\":[\"dock\"],\"water_at\":[3,10],\"capacity\":4}," +
                "{\"id\":\"dock_b\",\"at\":[5,11],\"flags\":[\"dock\"],\"water_at\":[5,10],\"capacity\":4}," +
                "{\"id\":\"dock_c\",\"at\":[18,11],\"flags\":[\"dock\",\"evacuation\"],\"water_at\":[18,10],\"capacity\":4,\"dock_max_depth\":320}," +
                "{\"id\":\"land_w\",\"at\":[9,11]},{\"id\":\"land_e\",\"at\":[11,11]}," +
                "{\"id\":\"harbor_start\",\"at\":[14,14],\"capacity\":4},{\"id\":\"harbor_assembly\",\"at\":[15,14],\"capacity\":4}," +
                "{\"id\":\"harbor_pickup\",\"at\":[16,14],\"capacity\":4}]," +
                "\"edges\":[" +
                "{\"id\":\"w_ac\",\"from\":\"dock_a\",\"to\":\"dock_c\",\"ticks\":100,\"class\":\"water\",\"sample\":{\"x0\":3,\"y0\":10,\"x1\":18,\"y1\":10}}," +
                "{\"id\":\"w_bc\",\"from\":\"dock_b\",\"to\":\"dock_c\",\"ticks\":100,\"class\":\"water\",\"sample\":{\"x0\":5,\"y0\":10,\"x1\":18,\"y1\":10}}," +
                "{\"id\":\"g_causeway\",\"from\":\"land_w\",\"to\":\"land_e\",\"ticks\":40,\"sample\":[[10,11]]}," +
                "{\"id\":\"g_h1\",\"from\":\"harbor_start\",\"to\":\"harbor_assembly\",\"ticks\":20}," +
                "{\"id\":\"g_h2\",\"from\":\"harbor_assembly\",\"to\":\"harbor_pickup\",\"ticks\":20}," +
                "{\"id\":\"g_h3\",\"from\":\"harbor_pickup\",\"to\":\"dock_a\",\"ticks\":100,\"sample\":[[16,14]]}]," +
                "\"sockets\":[" +
                "{\"id\":\"skiff:01\",\"tool\":\"skiff\",\"node\":\"dock_a\",\"at\":[3,12]}," +
                "{\"id\":\"skiff:02\",\"tool\":\"skiff\",\"node\":\"dock_b\",\"at\":[5,12]}," +
                "{\"id\":\"relay:01\",\"tool\":\"relay\",\"at\":[12,11],\"platform\":[[12,11]]}]," +
                "\"power\":{\"nodes\":[{\"id\":\"plant\",\"fixed_supply\":2},{\"id\":\"lamp\"}]," +
                "\"links\":[{\"from\":\"plant\",\"to\":\"lamp\",\"relay\":\"relay:01\"}]}," +
                "\"infrastructure\":[{\"id\":\"lighthouse\",\"power\":\"lamp\",\"demand\":1}]," +
                "\"roles\":{\"tide\":{\"tides\":[\"tide\"]},\"outer_loop\":{\"edges\":[\"w_ac\"]}}" +
                "}";
        }

        static string Contract(string events) =>
            "{\"id\":\"W4-TEST\",\"world_id\":\"W4\",\"title\":\"Harbor fixture\",\"duration_scenario_ticks\":3000," +
            "\"total_residents\":2,\"minimum_rescues\":1,\"supply_budget\":30,\"tutorial_policy\":\"world_mechanic_guidance\"," +
            "\"permitted_tools\":[\"relay\",\"skiff\"],\"maximum_stock\":{\"relay\":1,\"skiff\":2},\"default_loadout\":{\"relay\":1,\"skiff\":2}," +
            "\"groups\":[{\"id\":\"harbor\",\"count\":2,\"start_node_id\":\"harbor_start\",\"assembly_node_id\":\"harbor_assembly\"}]," +
            $"\"events\":[{events}],\"milestones\":[]," +
            "\"objectives\":[{\"id\":\"critical\",\"tier\":\"critical\",\"kind\":\"rescue_count\",\"params\":{\"minimum\":1},\"temporal\":{\"kind\":\"at_end\"}}]}";

        static string Tide(string id, int at, int index) =>
            $"{{\"id\":\"{id}\",\"at_scenario_tick\":{at},\"warning_at_scenario_tick\":{at - 100},\"known_at_start\":true," +
            $"\"operations\":[{{\"kind\":\"set_tide_segment\",\"target_role\":\"tide\",\"value\":{index}}}]}}";

        static MissionDef Fixture(string events = "", string tideExtra = "")
        {
            var rules = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/equipment_rules.json")));
            return MapCompiler.Compile(JsonNode.Parse(Map(tideExtra)), JsonNode.Parse(Contract(events)), rules, "wide");
        }

        static void RunTo(Simulator sim, long tick, System.Action each = null)
        {
            while (sim.S.C.S < tick && !sim.IsOver) { sim.Step(); each?.Invoke(); }
        }

        // §9: level unchanged at the event tick, target at +100, floor_div interpolation between.
        [SimTest("tide")]
        public static void Tide_Ramp_Exact_Boundaries()
        {
            var sim = new Simulator(Fixture(Tide("rise", 200, 1)), RuleProfile.Standard);
            RunTo(sim, 200);
            Check.Equal(0L, sim.S.TideLevel[0], "unchanged at the event tick");
            RunTo(sim, 233);
            Check.Equal(198L, sim.S.TideLevel[0], "floor(600*33/100)");
            RunTo(sim, 300);
            Check.Equal(600L, sim.S.TideLevel[0], "target at event + 100");
            RunTo(sim, 450);
            Check.Equal(600L, sim.S.TideLevel[0], "holds");
        }

        // A new ramp interrupts continuously from the then-current level.
        [SimTest("tide")]
        public static void Tide_Ramp_Interrupts_From_Current_Level()
        {
            var sim = new Simulator(Fixture(Tide("rise", 200, 1) + "," + Tide("surge", 250, 2)), RuleProfile.Standard);
            RunTo(sim, 250);
            Check.Equal(300L, sim.S.TideLevel[0], "captured mid-ramp at the new event tick");
            RunTo(sim, 300);
            Check.Equal(650L, sim.S.TideLevel[0], "300 + floor(700*50/100)");
            RunTo(sim, 350);
            Check.Equal(1000L, sim.S.TideLevel[0], "segment 2 target");
        }

        // High Tide Start: 600 mm at S=0 with a degenerate ramp; events keep their times and ramp from the real level.
        [SimTest("tide")]
        public static void High_Tide_Start_Begins_At_Segment_One()
        {
            var d = Fixture(Tide("ebb", 200, 0));
            d.TideStartIndex = 1;
            var sim = new Simulator(d, RuleProfile.Standard);
            Check.Equal(600L, sim.S.TideLevel[0], "600 mm at S=0");
            RunTo(sim, 250);
            Check.Equal(300L, sim.S.TideLevel[0], "ebb ramps 600→0 from its own event tick");
            Check.True(sim.S.Applied[0], "event fired once at its authored tick");
        }

        [SimTest("tide")]
        public static void Fixed_Track_Loops_And_Holds()
        {
            var loop = new Simulator(Fixture("", ",\"track\":[[0,0],[100,500],[200,0]],\"period\":200"), RuleProfile.Standard);
            RunTo(loop, 50); Check.Equal(250L, loop.S.TideLevel[0], "rising knot segment");
            RunTo(loop, 150); Check.Equal(250L, loop.S.TideLevel[0], "falling knot segment");
            RunTo(loop, 300); Check.Equal(500L, loop.S.TideLevel[0], "S % period");
            var hold = new Simulator(Fixture("", ",\"track\":[[0,0],[100,800]]"), RuleProfile.Standard);
            RunTo(hold, 500); Check.Equal(800L, hold.S.TideLevel[0], "nonrepeating track holds its last value");

            bool threw = false;
            try { Fixture(Tide("rise", 200, 1), ",\"track\":[[0,0],[100,800]]"); } catch (ContentException) { threw = true; }
            Check.True(threw, "event ramps cannot drive a fixed-track controller");
        }

        // §4 ledger: inland volume changes only through recorded sea-in / sea-out (no sources or sinks here).
        [SimTest("tide")]
        public static void Sea_Exchange_Conserves_Through_The_Ledger()
        {
            var d = Fixture(Tide("rise", 200, 1) + "," + Tide("ebb", 900, 0));
            var sim = new Simulator(d, RuleProfile.Standard);
            long v0 = sim.TotalVolume();
            int far = d.CellIndex(18, 10);
            long peak = 0;
            RunTo(sim, 2500, () =>
            {
                Check.Equal(v0 + sim.S.SeaIn[0] - sim.S.SeaOut[0], sim.TotalVolume(), "volume = initial + sea in − sea out");
                peak = System.Math.Max(peak, sim.Depth(far));
            });
            Check.True(sim.S.SeaIn[0] > 0 && sim.S.SeaOut[0] > 0, "the tide flowed in and back out");
            Check.True(peak >= 500, $"the flood tide reaches the far end of the channel (peak {peak} mm)");
            // the narrow 20-cell channel drains slowly: it must be well on its way back, not instantly reset
            Check.True(sim.Depth(far) <= peak - 150, $"the far channel drains on the ebb ({sim.Depth(far)} of peak {peak} mm)");
        }

        // Between two tides: a low causeway opens at low water and closes when the tide covers it by more than 100 mm.
        [SimTest("tide")]
        public static void Causeway_Opens_Between_Tides()
        {
            var d = Fixture(Tide("rise", 200, 1) + "," + Tide("ebb", 900, 0));
            var sim = new Simulator(d, RuleProfile.Standard);
            int causeway = d.EdgeIndex["g_causeway"];
            RunTo(sim, 150);
            Check.True(sim.S.EdgeOpen[causeway], "open at low tide");
            bool closed = false;
            RunTo(sim, 900, () => { if (!sim.S.EdgeOpen[causeway]) closed = true; });
            Check.True(closed, "high tide closes it");
            RunTo(sim, 2500);
            Check.True(sim.S.EdgeOpen[causeway], "low tide reopens it");
        }

        // Relay: the named link carries power only while placed, enabled and dry; above 50 mm it drops, at <= 40 mm it reconnects.
        [SimTest("relay")]
        public static void Relay_Link_With_Low_Connection_Hysteresis()
        {
            var d = Fixture(Tide("rise", 200, 1) + "," + Tide("ebb", 700, 0));
            int light = d.InfraIndex["lighthouse"], sock = d.SocketIndex["relay:01"];
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 5);
            Check.False(sim.S.InfraPowered[light], "no relay: the lamp is islanded");
            sim.Submit(Command.Place(sock, ToolKind.Relay));
            RunTo(sim, 10);
            Check.True(sim.S.InfraPowered[light], "relay joins plant and lamp");
            int inst = sim.S.SocketInstance[sock];
            sim.Submit(Command.Disable(inst));
            RunTo(sim, 15);
            Check.False(sim.S.InfraPowered[light], "a switched-off relay carries nothing");
            sim.Submit(Command.Enable(inst));
            RunTo(sim, 20);
            Check.True(sim.S.InfraPowered[light], "switched back on");

            int platform = d.CellIndex(12, 11);
            bool dropped = false, reconnected = false;
            RunTo(sim, 2500, () =>
            {
                long depth = sim.Depth(platform);
                bool latched = sim.S.EqLatchedOff[inst];
                if (latched) { dropped = true; Check.True(depth > 40, $"latched only while deeper than 40 mm ({depth})"); }
                else { Check.True(depth <= 50, $"connected only at <= 50 mm ({depth})"); if (dropped) reconnected = true; }
                Check.Equal(!latched, sim.S.InfraPowered[light], "lamp power follows the relay link");
            });
            Check.True(dropped, "the high tide floods the low connection");
            Check.True(reconnected, "it reconnects by itself after the ebb");
            Check.False(sim.S.EqBroken[inst], "100 mm of water never damages it (damage starts above 200 mm)");
        }

        [SimTest("tide")]
        public static void Current_Multiplier_Range_Is_Validated()
        {
            string ev = "{\"id\":\"rip\",\"at_scenario_tick\":300,\"warning_at_scenario_tick\":200,\"known_at_start\":true," +
                        "\"operations\":[{\"kind\":\"set_current_multiplier\",\"target_role\":\"outer_loop\",\"value\":2500}]}";
            bool threw = false;
            try { Fixture(ev); } catch (ContentException) { threw = true; }
            Check.True(threw, "current multiplier outside 500..2000 is rejected");
        }

        // Berth reservation: two skiffs ordered to a one-berth dock on the same tick. Only one may set off; the other
        // queues at its own dock until the first has finished and moored aside, then it lands. The berth is never shared.
        [SimTest("tide")]
        public static void Berth_Is_Reserved_At_Departure_And_Freed_When_Done()
        {
            var d = Fixture();
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(d.SocketIndex["skiff:01"], ToolKind.Skiff));
            sim.Submit(Command.Place(d.SocketIndex["skiff:02"], ToolKind.Skiff));
            sim.Step();
            int dockC = d.NodeIndex["dock_c"];
            sim.Submit(Command.VehicleRoute(0, -1, -1, dockC, false));
            sim.Submit(Command.VehicleRoute(1, -1, -1, dockC, false));
            long firstAway = -1, secondDeparts = -1;
            int first = -1;
            for (int i = 0; i < 600; i++)
            {
                sim.Step();
                Check.True(sim.S.NodeBerthUsed[dockC] <= 1, $"the single berth is never double-booked ({sim.S.NodeBerthUsed[dockC]})");
                for (int v = 0; v < 2; v++)
                {
                    if (first < 0 && sim.S.VState[v] == (byte)VehicleState.Moving) first = v;
                    if (first >= 0 && v != first && secondDeparts < 0 && sim.S.VState[v] == (byte)VehicleState.Moving) secondDeparts = sim.S.C.O;
                }
                if (first >= 0 && firstAway < 0 && sim.S.VStandOff[first]) firstAway = sim.S.C.O;
            }
            Check.True(first >= 0 && firstAway > 0, "the first skiff landed, finished and moored aside");
            Check.True(secondDeparts > 0 && secondDeparts >= firstAway, $"the second set off only once the berth was free ({secondDeparts} vs {firstAway})");
            Check.Equal(dockC, sim.S.VNode[0], "first skiff at the dock");
            Check.Equal(dockC, sim.S.VNode[1], "second skiff at the dock");
            Check.True(sim.S.NodeMaxQueue[dockC] >= 90, $"the queue metric recorded the wait during the first crossing ({sim.S.NodeMaxQueue[dockC]})");
            Check.Equal(0, sim.S.NodeBerthUsed[dockC], "both finished boats moored aside");
        }

        // Spec 03 metric: cumulative ticks a loaded vehicle waits while its chosen unload dock is closed.
        [SimTest("tide")]
        public static void Loaded_Closed_Dock_Wait_Counts_A_Closed_Destination()
        {
            foreach (bool tide in new[] { false, true })
            {
                var d = Fixture(tide ? Tide("rise", 200, 1) : "");
                var sim = new Simulator(d, RuleProfile.Standard);
                sim.Submit(Command.Place(d.SocketIndex["skiff:01"], ToolKind.Skiff));
                RunTo(sim, 600);
                sim.Submit(Command.VehicleRoute(0, d.GroupIndex["harbor"], d.NodeIndex["dock_a"], d.NodeIndex["dock_c"], true));
                RunTo(sim, 1500);
                if (!tide)
                {
                    Check.Equal(0L, sim.S.C.LoadedClosedDockWait, "open destination: no closed-dock wait");
                    Check.Equal(2, sim.S.C.Evacuated, "both landed at the refuge dock");
                }
                else
                {
                    Check.True(sim.S.C.LoadedClosedDockWait > 100, $"the flooded dock_c keeps the loaded skiff waiting ({sim.S.C.LoadedClosedDockWait})");
                    Check.Equal(0, sim.S.C.Evacuated, "nobody can unload at a closed dock");
                }
            }
        }
    }
}
