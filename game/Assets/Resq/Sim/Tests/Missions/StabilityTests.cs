using System.IO;
using System.Text;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests
{
    /// <summary>
    /// World 3 stability, runout and winch (spec 02 §5, §8; acceptance RS01–RS04). A synthetic dry map for the W3-M1 contract:
    /// the upper station sits on <c>weak_support</c> with a powered cable down to the valley refuge; the lower station walks
    /// out over a rock shelf that an authored runout can bury. Each test supplies its own events.
    /// </summary>
    public static class StabilityTests
    {
        static string Map(int baseStability, int staticLoad, int braceSockets)
        {
            var rows = new StringBuilder();
            for (int r = 0; r < 24; r++) rows.Append(r == 0 ? "" : ",").Append("\"################################\"");
            var braces = new StringBuilder();
            for (int i = 1; i <= braceSockets; i++)
                braces.Append($",{{\"id\":\"brace:0{i}\",\"tool\":\"brace\",\"at\":[{8 + i},18],\"support\":\"weak_support\",\"zone\":\"braces\"}}");
            return "{" +
                $"\"rows\":[{rows}]," +
                "\"legend\":{\"#\":{\"terrain\":\"high\",\"code\":3,\"elev\":1200}}," +
                "\"nodes\":[" +
                "{\"id\":\"upper_station_start\",\"at\":[5,20],\"capacity\":6}," +
                "{\"id\":\"upper_station_assembly\",\"at\":[6,20],\"capacity\":6}," +
                "{\"id\":\"upper_station_pickup\",\"at\":[7,20],\"capacity\":6}," +
                "{\"id\":\"cable_top\",\"at\":[8,18],\"capacity\":6}," +
                "{\"id\":\"lower_station_start\",\"at\":[20,20],\"capacity\":4}," +
                "{\"id\":\"lower_station_assembly\",\"at\":[21,20],\"capacity\":4}," +
                "{\"id\":\"lower_station_pickup\",\"at\":[22,20],\"capacity\":6}," +
                "{\"id\":\"shelf\",\"at\":[20,12],\"capacity\":6}," +
                "{\"id\":\"valley\",\"at\":[15,5],\"flags\":[\"evacuation\"],\"capacity\":12}]," +
                "\"edges\":[" +
                "{\"id\":\"g_up1\",\"from\":\"upper_station_start\",\"to\":\"upper_station_assembly\",\"ticks\":20}," +
                "{\"id\":\"g_up2\",\"from\":\"upper_station_assembly\",\"to\":\"upper_station_pickup\",\"ticks\":20}," +
                "{\"id\":\"g_up3\",\"from\":\"upper_station_pickup\",\"to\":\"cable_top\",\"ticks\":20}," +
                "{\"id\":\"c_cable\",\"from\":\"cable_top\",\"to\":\"valley\",\"ticks\":50,\"class\":\"cable\",\"slots\":1}," +
                "{\"id\":\"g_lo1\",\"from\":\"lower_station_start\",\"to\":\"lower_station_assembly\",\"ticks\":20}," +
                "{\"id\":\"g_lo2\",\"from\":\"lower_station_assembly\",\"to\":\"lower_station_pickup\",\"ticks\":20}," +
                "{\"id\":\"g_lo3\",\"from\":\"lower_station_pickup\",\"to\":\"shelf\",\"ticks\":100,\"tags\":[\"debris_reopenable\",\"rock_shelf_bypass\"]}," +
                "{\"id\":\"g_shelf_valley\",\"from\":\"shelf\",\"to\":\"valley\",\"ticks\":100}]," +
                "\"supports\":[{\"id\":\"weak_support\"," + $"\"base_stability\":{baseStability},\"static_load\":{staticLoad}," +
                "\"nodes\":[\"cable_top\"],\"edges\":[\"c_cable\"],\"collapse_nodes\":[\"cable_top\"],\"collapse_edges\":[\"c_cable\",\"g_up3\"]}]," +
                "\"runouts\":[" +
                "{\"id\":\"slide\",\"segments\":[{\"id\":\"s1\",\"edges\":[\"g_lo3\"],\"nodes\":[\"shelf\"],\"next\":[\"s2\"],\"delay\":10,\"settle\":200}," +
                "{\"id\":\"s2\",\"edges\":[\"g_shelf_valley\"]}]}," +
                "{\"id\":\"support_fall\",\"source_support\":\"weak_support\",\"segments\":[{\"id\":\"s1\",\"nodes\":[\"lower_station_pickup\"],\"settle\":50}]}]," +
                "\"sockets\":[" +
                "{\"id\":\"winch:01\",\"tool\":\"winch\",\"node\":\"cable_top\",\"edge\":\"c_cable\",\"at\":[8,17],\"support\":\"weak_support\",\"power\":\"grid\"}," +
                "{\"id\":\"generator:01\",\"tool\":\"generator\",\"at\":[12,10],\"power\":\"grid\"}," +
                "{\"id\":\"beacon:01\",\"tool\":\"beacon\",\"node\":\"valley\",\"at\":[15,6]}," +
                "{\"id\":\"beacon:02\",\"tool\":\"beacon\",\"node\":\"shelf\",\"at\":[20,13]}" + braces + "]," +
                "\"power\":{\"nodes\":[{\"id\":\"grid\"}],\"links\":[]}," +
                "\"infrastructure\":[{\"id\":\"cable_support\",\"nodes\":[\"cable_top\"],\"support\":\"weak_support\",\"demand\":0}]," +
                "\"roles\":{\"weak_support\":{\"supports\":[\"weak_support\"]},\"slide\":{\"runouts\":[\"slide\"]},\"upper_path\":{\"edges\":[\"g_up3\"]}}" +
                "}";
        }

        static string Ev(string id, int at, int warn, string kind, string role, long value) =>
            $"{{\"id\":\"{id}\",\"at_scenario_tick\":{at},\"warning_at_scenario_tick\":{warn},\"known_at_start\":true," +
            $"\"operations\":[{{\"kind\":\"{kind}\",\"target_role\":\"{role}\",\"value\":{value}}}]}}";

        static MissionDef Fixture(string[] events, int baseStability = 400, int staticLoad = 0, int braceSockets = 3)
        {
            var map = JsonNode.Parse(Map(baseStability, staticLoad, braceSockets));
            JsonNode contract = null;
            var contracts = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/mission_contracts.json")));
            foreach (var m in contracts["missions"].Elements()) if (m.Str("id") == "W3-M1") contract = m;
            var rules = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/equipment_rules.json")));
            contract.Set("events", JsonNode.Parse("[" + string.Join(",", events) + "]"));
            contract.Set("supply_budget", 30);
            return MapCompiler.Compile(map, contract, rules, "wide");
        }

        static void RunTo(Simulator sim, long tick, System.Action each = null)
        {
            while (sim.S.C.S < tick && !sim.IsOver) { sim.Step(); each?.Invoke(); }
        }

        static int Sock(MissionDef d, string id) => d.SocketIndex[id];

        // RS01: per-second loss carries its remainder with denominator 10; braces add 200 each up to the 3-socket cap.
        [SimTest("stability")]
        public static void RS01_Loss_Carry_And_Brace_Cap()
        {
            var d = Fixture(new[] { Ev("loss", 10, 0, "set_stability_loss", "weak_support", 3) });
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 10);
            int base10 = sim.S.SupBase[0];
            RunTo(sim, 110);
            Check.Equal(base10 - 30, sim.S.SupBase[0], "3 points per second over 10 s (100 pulses)");
            // the tick-10 pulse already carried 3 tenths, so 101 pulses hold 30.3 points: 30 emitted, 0.3 carried
            RunTo(sim, 112);
            Check.Equal(base10 - 30, sim.S.SupBase[0], "0.9 points carried, not yet emitted, after 2 more pulses");
            RunTo(sim, 113);
            Check.Equal(base10 - 31, sim.S.SupBase[0], "the carried remainder emits the next point on the 3rd pulse");

            for (int i = 1; i <= 3; i++) sim.Submit(Command.Place(Sock(d, $"brace:0{i}"), ToolKind.Brace));
            sim.Step();
            Check.Equal(3, sim.IntactBraces(0), "three braces placed");
            Check.Equal(System.Math.Min(1000, sim.S.SupBase[0] + 600), sim.EffectiveStability(0), "200 effective points per intact brace");

            bool threw = false;
            try { Fixture(new string[0], braceSockets: 4); } catch (ContentException) { threw = true; }
            Check.True(threw, "a structure with four brace sockets is rejected by the compiler");
        }

        // RS02: collapse happens exactly 100 scenario ticks after the relevant warning, not before.
        [SimTest("stability")]
        public static void RS02_Collapse_Waits_For_The_Warning_Boundary()
        {
            var d = Fixture(new[] { Ev("quake", 50, 50, "tremor", "weak_support", 400) });
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 50);
            Check.Equal(0, sim.EffectiveStability(0), "tremor drops the support to zero");
            Check.True(sim.S.SupArmed[0], "zero arms the collapse");
            RunTo(sim, 149);
            Check.False(sim.SupportCollapsed(0), "no collapse before warning + 100");
            RunTo(sim, 150);
            Check.True(sim.SupportCollapsed(0), "collapse on the exact boundary");
        }

        // An emergent warning (stability <= 300) arms its own clock; raising stability above 400 clears it.
        [SimTest("stability")]
        public static void Emergent_Warning_Starts_And_Clears()
        {
            var d = Fixture(new string[0], baseStability: 250);
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 5);
            Check.True(sim.S.SupWarnTick[0] >= 0, "effective 250 warns");
            sim.Submit(Command.Place(Sock(d, "brace:01"), ToolKind.Brace));
            RunTo(sim, 10);
            Check.Equal(450, sim.EffectiveStability(0), "one brace adds 200");
            Check.Equal(-1L, sim.S.SupWarnTick[0], "above 400 clears the untriggered warning");
        }

        // RS03: restoring stability above zero before the boundary cancels the pending collapse.
        [SimTest("stability")]
        public static void RS03_Bracing_Cancels_A_Pending_Collapse()
        {
            var d = Fixture(new[] { Ev("quake", 50, 50, "tremor", "weak_support", 400) });
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 100);
            Check.True(sim.S.SupArmed[0], "armed after the tremor");
            sim.Submit(Command.Place(Sock(d, "brace:01"), ToolKind.Brace));
            RunTo(sim, 400);
            Check.False(sim.S.SupArmed[0], "brace disarmed it");
            Check.False(sim.SupportCollapsed(0), "no collapse after the boundary");
            Check.Equal(200, sim.EffectiveStability(0), "base 0 + one brace");
        }

        // Collapse closes declared edges/stations for good, breaks attached equipment, stops infrastructure, starts the bound runout.
        [SimTest("stability")]
        public static void Collapse_Effects()
        {
            var d = Fixture(new[] { Ev("quake", 50, 50, "tremor", "weak_support", 400),
                                    Ev("reopen", 300, 300, "set_edge_enabled", "upper_path", 1) });
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(Sock(d, "generator:01"), ToolKind.Generator));
            sim.Submit(Command.Place(Sock(d, "winch:01"), ToolKind.Winch));
            RunTo(sim, 20);
            Check.True(sim.S.InfraOperational[d.InfraIndex["cable_support"]], "station operational while the support stands");
            Check.True(sim.S.EdgeOpen[d.EdgeIndex["c_cable"]], "cable open with a winch");
            RunTo(sim, 150);
            Check.True(sim.SupportCollapsed(0), "collapsed");
            int winch = sim.S.SocketInstance[Sock(d, "winch:01")];
            Check.True(sim.S.EqBroken[winch], "attached winch destroyed");
            Check.Equal(1, sim.S.C.Losses, "one equipment loss");
            Check.False(sim.S.InfraOperational[d.InfraIndex["cable_support"]], "infrastructure on the support is lost");
            Check.False(sim.NodeSafe(d.NodeIndex["cable_top"]), "collapsed station is unsafe");
            Check.True(sim.S.RunStarted[d.RunoutIndex["support_fall"]], "runout bound to the support starts on collapse");
            RunTo(sim, 400);
            Check.False(sim.S.EdgeOpen[d.EdgeIndex["g_up3"]], "set_edge_enabled cannot revive a collapsed structural edge");
            Check.False(sim.S.EdgeOpen[d.EdgeIndex["c_cable"]], "the cable stays down");
        }

        // RS04: runout advances deterministically along its DAG; debris reopens only debris_reopenable edges.
        [SimTest("stability")]
        public static void RS04_Runout_Advances_And_Reopens_Only_Tagged_Edges()
        {
            var d = Fixture(new[] { Ev("slide", 100, 0, "release_avalanche", "slide", 1),
                                    Ev("slide_again", 150, 140, "release_avalanche", "slide", 1) });
            var sim = new Simulator(d, RuleProfile.Standard);
            int s1 = System.Array.IndexOf(d.SegmentIds, "slide/s1"), s2 = System.Array.IndexOf(d.SegmentIds, "slide/s2");
            int shelfEdge = d.EdgeIndex["g_lo3"], valleyEdge = d.EdgeIndex["g_shelf_valley"], shelf = d.NodeIndex["shelf"];
            RunTo(sim, 99);
            Check.True(sim.S.EdgeOpen[shelfEdge] && sim.S.EdgeOpen[valleyEdge], "open before release");
            RunTo(sim, 100);
            Check.Equal(100L, sim.S.SegStart[s1], "first segment starts at the release tick");
            Check.False(sim.S.EdgeOpen[shelfEdge], "occupied segment closes its edge");
            Check.False(sim.NodeSafe(shelf), "runout occupies the node");
            Check.True(sim.S.EdgeOpen[valleyEdge], "successor has not arrived yet");
            RunTo(sim, 110);
            Check.Equal(110L, sim.S.SegStart[s2], "successor after the 10-tick travel delay");
            Check.False(sim.S.EdgeOpen[valleyEdge], "successor closes its edge");
            RunTo(sim, 160);
            Check.Equal(100L, sim.S.SegStart[s1], "a duplicate trigger has no effect");
            RunTo(sim, 299);
            Check.False(sim.S.EdgeOpen[shelfEdge], "still settling");
            RunTo(sim, 300);
            Check.True(sim.S.EdgeOpen[shelfEdge], "debris_reopenable edge reopens after 200 ticks");
            Check.True(sim.NodeSafe(shelf), "node clear after settling");
            RunTo(sim, 600);
            Check.False(sim.S.EdgeOpen[valleyEdge], "an untagged edge stays buried");

            var again = new Simulator(d, RuleProfile.Standard);
            RunTo(again, 600);
            Check.Equal(sim.S.Hash(), again.S.Hash(), "runout is deterministic");
        }

        // Residents standing on a support add floor((static + 10*residents)/100) points per second.
        [SimTest("stability")]
        public static void Resident_Load_Adds_Loss()
        {
            var d = Fixture(new string[0], staticLoad: 50);
            var empty = new Simulator(d, RuleProfile.Standard);
            RunTo(empty, 600);
            Check.Equal(400, empty.S.SupBase[0], "static 50 alone loses nothing ((50+0)/100 = 0)");

            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.GroupRoute(d.GroupIndex["upper_station"], d.NodeIndex["cable_top"], false));
            long full = -1;
            RunTo(sim, 600, () => { if (full < 0 && sim.S.NodeOccupancy[d.NodeIndex["cable_top"]] == 6) full = sim.S.C.S; });
            Check.True(full > 0, "six residents reach the station");
            Check.True(sim.S.SupBase[0] < 400, "(50 + 10*6)/100 = 1 point per second while loaded");
            Check.True(400 - sim.S.SupBase[0] <= (600 - full) / 10 + 1, "no faster than 1 point per second");
        }

        static int Winch(Simulator sim) => 0; // first vehicle slot: no fixed vehicles in this map

        static Simulator WinchRun(MissionDef d)
        {
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(Sock(d, "generator:01"), ToolKind.Generator));
            sim.Submit(Command.Place(Sock(d, "winch:01"), ToolKind.Winch));
            for (int i = 1; i <= 3; i++) sim.Submit(Command.Place(Sock(d, $"brace:0{i}"), ToolKind.Brace));
            sim.Step();
            sim.Submit(Command.VehicleRoute(Winch(sim), d.GroupIndex["upper_station"], d.NodeIndex["cable_top"], d.NodeIndex["valley"], true));
            return sim;
        }

        // Winch duty: 100 powered travelling ticks need 30 ticks of empty rest at a safe endpoint before the next trip.
        [SimTest("winch")]
        public static void Winch_Duty_Cycle_Rests_Between_Round_Trips()
        {
            var d = Fixture(new string[0]);
            var sim = WinchRun(d);
            int v = Winch(sim);
            var prev = (VehicleState)sim.S.VState[v];
            int departures = 0, restsObserved = 0, prevDuty = 0;
            RunTo(sim, 2000, () =>
            {
                var st = (VehicleState)sim.S.VState[v];
                if (st == VehicleState.Moving && prev != VehicleState.Moving)
                {
                    departures++;
                    Check.True(sim.S.VDuty[v] < Simulator.WinchDutyTicks, $"departure {departures} within the duty limit (duty {sim.S.VDuty[v]})");
                }
                if (prevDuty >= Simulator.WinchDutyTicks && sim.S.VDuty[v] == 0) restsObserved++;
                prevDuty = sim.S.VDuty[v];
                prev = st;
            });
            int byCable = 0;
            foreach (int r in d.GroupResidents[d.GroupIndex["upper_station"]])
                if (sim.S.RState[r] == (byte)ResidentState.Evacuated && sim.S.RFinalMode[r] == (int)Traversal.Cable) byCable++;
            Check.Equal(6, byCable, "all six ride the cable to the refuge");
            Check.True(departures >= 5, $"three loaded descents and the climbs between them ({departures})");
            Check.True(restsObserved >= 2, $"the winch rested to restore its duty cycle ({restsObserved})");
        }

        static void RunToMoving(Simulator sim, int v, bool loaded)
        {
            for (int i = 0; i < 2000 && !sim.IsOver; i++)
            {
                if (sim.S.VState[v] == (byte)VehicleState.Moving && (!loaded || sim.S.VOccupantCount[v] > 0) && sim.S.VProgress[v] >= 10) return;
                sim.Step();
            }
        }

        // Power loss brakes the cabin for up to 20 ticks; power back within that window resumes the same trip.
        [SimTest("winch")]
        public static void Winch_Brake_Then_Resume()
        {
            var d = Fixture(new string[0]);
            var sim = WinchRun(d);
            int v = Winch(sim);
            RunToMoving(sim, v, loaded: true);
            int gen = sim.S.SocketInstance[Sock(d, "generator:01")];
            int progress = sim.S.VProgress[v];
            sim.Submit(Command.Disable(gen));
            for (int i = 0; i < 15; i++) sim.Step();
            Check.Equal(progress, sim.S.VProgress[v], "braked: no progress without power");
            Check.False(sim.S.VEmergency[v], "within the brake window");
            sim.Submit(Command.Enable(gen));
            for (int i = 0; i < 80 && sim.S.VState[v] == (byte)VehicleState.Moving; i++) sim.Step();
            Check.Equal(d.NodeIndex["valley"], sim.S.VNode[v], "the same trip resumed to the valley");
        }

        // After 20 braked ticks the cabin makes a one-time emergency return to its origin; power cannot reverse that.
        [SimTest("winch")]
        public static void Winch_Emergency_Return_To_Origin()
        {
            var d = Fixture(new string[0]);
            var sim = WinchRun(d);
            int v = Winch(sim);
            RunToMoving(sim, v, loaded: true);
            int gen = sim.S.SocketInstance[Sock(d, "generator:01")];
            int aboard = sim.S.VOccupantCount[v];
            sim.Submit(Command.Disable(gen));
            for (int i = 0; i < 25; i++) sim.Step();
            Check.True(sim.S.VEmergency[v], "emergency return engaged after the brake window");
            var arrivals = new Arrivals();
            sim.Sink = arrivals;
            sim.Submit(Command.Enable(gen)); // power back mid-return must not reverse the decision
            for (int i = 0; i < 80 && arrivals.First < 0; i++)
            {
                sim.Step();
                if (arrivals.First < 0) Check.True(sim.S.VEmergency[v] && sim.S.VReversing[v], "still returning with power restored");
                if (arrivals.First < 0) Check.Equal(aboard, sim.S.VOccupantCount[v], "passengers stay aboard during the return");
            }
            Check.Equal(d.NodeIndex["cable_top"], arrivals.First, "first arrival is back at the origin");
            Check.Equal(0, sim.S.C.AwaitingBackup, "safe origin: nobody awaits backup");
        }

        sealed class Arrivals : ISimEventSink
        {
            public int First = -1;
            public void Emit(in SimEvent e) { if (e.Kind == SimEventKind.VehicleArrived && First < 0) First = e.A; }
        }

        [SimTest("stability")]
        public static void Edge_Transfer_And_Dwell_Metrics()
        {
            var d = Fixture(new string[0]);
            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.GroupRoute(d.GroupIndex["lower_station"], d.NodeIndex["valley"], true));
            RunTo(sim, 800);
            int tag = System.Array.IndexOf(d.TagIds, "rock_shelf_bypass");
            Check.Equal(4, sim.S.EdgeTransfers[d.EdgeIndex["g_lo3"]], "four residents crossed the shelf");
            Check.True(tag >= 0, "tag compiled");
            Check.True(sim.S.NodeMaxDwell[d.NodeIndex["upper_station_start"]] >= 790, "the unrouted upper group has waited the whole run");
        }

        // A cable spans the ground between its stations: by default only the two end stations are sampled for water.
        [SimTest("stability")]
        public static void Cable_Samples_Only_Its_End_Stations()
        {
            var d = Fixture(new string[0]);
            var cells = d.EdgeSampleCells[d.EdgeIndex["c_cable"]];
            Check.Equal(2, cells.Length, "two end stations");
            Check.True(System.Array.IndexOf(cells, d.NodeCell[d.NodeIndex["cable_top"]]) >= 0 &&
                       System.Array.IndexOf(cells, d.NodeCell[d.NodeIndex["valley"]]) >= 0, "the stations themselves");
            Check.True(d.EdgeSampleCells[d.EdgeIndex["g_up1"]].Length >= 2, "ground edges still sample their whole path");
        }

        // Spec 02 §6: no admission onto an edge with a known hard closure before the predicted arrival.
        [SimTest("stability")]
        public static void Admission_Waits_For_A_Warned_Closure()
        {
            foreach (int closeAt in new[] { 300, 1000 })
            {
                var d = Fixture(new[] { Ev("cut", closeAt, 100, "set_edge_enabled", "upper_path", 0) });
                var sim = new Simulator(d, RuleProfile.Standard);
                int path = d.EdgeIndex["g_up3"], pickup = d.NodeIndex["upper_station_pickup"];
                RunTo(sim, 240);
                sim.Submit(Command.GroupRoute(d.GroupIndex["upper_station"], d.NodeIndex["cable_top"], false));
                bool reversed = false;
                RunTo(sim, closeAt + 40, () =>
                {
                    foreach (int r in d.GroupResidents[d.GroupIndex["upper_station"]])
                    {
                        if (sim.S.RState[r] != (byte)ResidentState.Walking || sim.S.REdge[r] != path) continue;
                        if (sim.S.RReversing[r]) reversed = true;
                        Check.True(sim.S.C.S < closeAt, $"nobody is on the path once it closes at {closeAt}");
                    }
                });
                Check.False(reversed, "no traveller had to be turned back: the closure was known");
                if (closeAt == 300) Check.True(sim.S.NodeOccupancy[pickup] > 0, "late walkers wait at the pickup instead of entering");
                else Check.Equal(6, sim.S.NodeOccupancy[d.NodeIndex["cable_top"]], "with the closure far away, everyone crosses");
            }
        }
    }
}
