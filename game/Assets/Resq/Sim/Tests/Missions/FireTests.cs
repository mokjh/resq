using System.IO;
using Resq.Sim.Rules;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests
{
    /// <summary>
    /// World 2 fire module (spec 02 §7) on a fire-enabled copy of the W1-M1 layout: only the west terrace (x 5..9, y 11..13)
    /// carries fuel. Ignition column x=5, firebreak column x=7, sprinkler column x=9 fed by a finite tank on a powered node.
    /// </summary>
    public static class FireTests
    {
        const string Ignite = "{\"id\":\"fire_ignite\",\"at_scenario_tick\":300,\"warning_at_scenario_tick\":200,\"known_at_start\":true," +
                              "\"operations\":[{\"kind\":\"ignite_region\",\"target_role\":\"ignition\",\"value\":700}]}";

        static MissionDef Fixture(int windCode = 2, long tankVolume = 2_000_000_000, int igniteX = 5, System.Action<JsonNode, JsonNode> edit = null)
        {
            var map = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("content/w1/m1/map.json")));
            JsonNode contract = null;
            var contracts = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/mission_contracts.json")));
            foreach (var m in contracts["missions"].Elements()) if (m.Str("id") == "W1-M1") contract = m;
            var rules = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/equipment_rules.json")));

            map.Set("fire", JsonNode.Parse("{\"enabled\":true,\"wind_regions\":[\"wind_all\"]}"));
            foreach (var kv in map["legend"].Properties()) kv.Value.Set("fuel", kv.Key == "." ? 1000 : 0);
            map["regions"].Set("wind_all", JsonNode.Parse("{\"x0\":0,\"y0\":0,\"x1\":31,\"y1\":23}"));
            map["regions"].Set("ignition", JsonNode.Parse($"[[{igniteX},11],[{igniteX},12],[{igniteX},13]]"));
            map["roles"].Set("ignition", JsonNode.Parse("{\"regions\":[\"ignition\"]}"));
            map["roles"].Set("wind", JsonNode.Parse("{\"regions\":[\"wind_all\"]}"));
            map.Set("stores", JsonNode.Parse($"[{{\"id\":\"tank\",\"elev\":1200,\"volume\":{tankVolume}}}]"));
            map["power"].Set("nodes", JsonNode.Parse("[{\"id\":\"grid\",\"fixed_supply\":2}]"));
            map["sockets"].Add(JsonNode.Parse("{\"id\":\"firebreak:01\",\"tool\":\"firebreak\",\"at\":[7,12],\"cells\":[[7,11],[7,12],[7,13]]}"));
            map["sockets"].Add(JsonNode.Parse("{\"id\":\"sprinkler:01\",\"tool\":\"sprinkler\",\"at\":[9,12],\"cells\":[[9,11],[9,12],[9,13]],\"store\":\"tank\",\"power\":\"grid\"}"));

            contract["permitted_tools"].Add(JsonNode.OfString("firebreak"));
            contract["permitted_tools"].Add(JsonNode.OfString("sprinkler"));
            foreach (var section in new[] { "maximum_stock", "default_loadout" })
            {
                contract[section].Set("firebreak", 1);
                contract[section].Set("sprinkler", 1);
            }
            contract.Set("supply_budget", 30);
            contract["events"].Add(JsonNode.Parse(Ignite));
            if (windCode != 0)
                contract["events"].Add(JsonNode.Parse("{\"id\":\"fire_wind\",\"at_scenario_tick\":10,\"warning_at_scenario_tick\":0,\"known_at_start\":true," +
                                                      $"\"operations\":[{{\"kind\":\"set_wind\",\"target_role\":\"wind\",\"value\":{windCode}}}]}}"));
            edit?.Invoke(map, contract);
            return MapCompiler.Compile(map, contract, rules, "wide");
        }

        static int C(MissionDef d, int x, int y) => d.CellIndex(x, y);

        static void RunTo(Simulator sim, long tick, System.Action each = null)
        {
            while (sim.S.C.S < tick && !sim.IsOver) { sim.Step(); each?.Invoke(); }
        }

        [SimTest("fire")]
        public static void Scheduled_Ignition_Uses_Its_Warning_Then_Burns()
        {
            var d = Fixture();
            var sim = new Simulator(d, RuleProfile.Standard);
            int c = C(d, 5, 12);
            RunTo(sim, 299);
            Check.Equal(FireState.Unburned, sim.CellFire(c), "no fire before the ignition tick");
            Check.Equal(0, sim.CellHeat(c), "no heat before the ignition tick");
            Check.Equal(200L, sim.S.FWarnStart[c], "warning clock starts at the published warning");
            RunTo(sim, 300);
            Check.Equal(FireState.Burning, sim.CellFire(c), "100 ticks of warning have passed, so 700 heat ignites at once");
        }

        [SimTest("fire")]
        public static void Emergent_Heating_Waits_100_Ticks_Before_Burning()
        {
            var d = Fixture();
            var sim = new Simulator(d, RuleProfile.Standard);
            int c = C(d, 6, 12);
            long heatingAt = -1, burningAt = -1;
            RunTo(sim, 900, () =>
            {
                if (heatingAt < 0 && sim.CellFire(c) == FireState.Heating) heatingAt = sim.S.C.S;
                if (burningAt < 0 && sim.CellFire(c) == FireState.Burning) burningAt = sim.S.C.S;
            });
            Check.True(heatingAt > 300, "neighbour starts heating after ignition");
            Check.True(burningAt >= heatingAt + FireRules.IgnitionWarningTicks, $"heating {heatingAt} → burning {burningAt} needs its own warning");
        }

        [SimTest("fire")]
        public static void Fire_Spreads_Faster_Downwind()
        {
            var d = Fixture(windCode: 2, igniteX: 7); // wind toward east
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 330);
            int east = sim.CellHeat(C(d, 8, 12)), west = sim.CellHeat(C(d, 6, 12));
            Check.True(east > west, $"downwind (east) cell heats faster: east {east}, west {west}");
            long eastBurn = -1, westBurn = -1;
            RunTo(sim, 1200, () =>
            {
                if (eastBurn < 0 && sim.CellFire(C(d, 8, 12)) == FireState.Burning) eastBurn = sim.S.C.S;
                if (westBurn < 0 && sim.CellFire(C(d, 6, 12)) == FireState.Burning) westBurn = sim.S.C.S;
            });
            Check.True(eastBurn > 0, "downwind cell ignites");
            Check.True(westBurn < 0 || westBurn > eastBurn, $"downwind ignites first: east {eastBurn}, west {westBurn}");
        }

        static bool EverBurns(MissionDef d, Simulator sim, long until, params (int x, int y)[] cells)
        {
            bool burned = false;
            RunTo(sim, until, () =>
            {
                foreach (var (x, y) in cells) if (sim.CellFire(C(d, x, y)) == FireState.Burning) burned = true;
            });
            return burned;
        }

        [SimTest("fire")]
        public static void Firebreak_Stops_The_Front()
        {
            var d = Fixture();
            var control = new Simulator(d, RuleProfile.Standard);
            Check.True(EverBurns(d, control, 2000, (8, 12), (9, 12)), "without a firebreak the front crosses the terrace");

            var sim = new Simulator(d, RuleProfile.Standard);
            int sock = d.SocketIndex["firebreak:01"];
            Check.Equal(Reject.OK, sim.ValidatePlace(sock, ToolKind.Firebreak, 0), "firebreak placeable");
            sim.Submit(Command.Place(sock, ToolKind.Firebreak));
            sim.Step();
            foreach (int c in d.SocketCells[sock])
            {
                Check.Equal(0, sim.S.FFuel[c], "firebreak removes fuel");
                Check.Equal(FireState.SafeAsh, sim.CellFire(c), "a cold cleared cell is SafeAsh at once");
            }
            bool far = EverBurns(d, sim, 2000, (8, 11), (8, 12), (8, 13), (9, 11), (9, 12), (9, 13));
            bool cleared = false;
            foreach (int c in d.SocketCells[sock]) if (sim.CellFire(c) == FireState.Burning) cleared = true;
            Check.False(far, "nothing beyond the firebreak ignites");
            Check.False(cleared, "cleared cells never burn");
            Check.True(sim.CellFire(C(d, 6, 12)) != FireState.Unburned, "the near side still burned");
        }

        [SimTest("fire")]
        public static void Firebreak_Rejects_A_Burning_Cell()
        {
            var d = Fixture(igniteX: 7);
            var sim = new Simulator(d, RuleProfile.Standard);
            RunTo(sim, 300);
            Check.Equal(Reject.UnsafeTarget, sim.ValidatePlace(d.SocketIndex["firebreak:01"], ToolKind.Firebreak, 0), "cannot clear a burning strip");
        }

        [SimTest("fire")]
        public static void Sprinkler_Wets_Its_Cells_From_A_Finite_Tank()
        {
            long tank0 = 40_000_000;
            var d = Fixture(tankVolume: tank0);
            var sim = new Simulator(d, RuleProfile.Standard);
            int sock = d.SocketIndex["sprinkler:01"];
            int tankNode = d.StoreNode[d.StoreIndex["tank"]];
            sim.Submit(Command.Place(sock, ToolKind.Sprinkler));
            RunTo(sim, 20);
            Check.True(sim.S.Volume[tankNode] < tank0, "the tank drains");
            foreach (int c in d.SocketCells[sock]) Check.True(sim.S.FWet[c] > 0, "target cells get wet");
            Check.Equal(0, sim.S.FWet[C(d, 8, 12)], "only target cells are sprayed");
            RunTo(sim, 600);
            Check.Equal(0L, sim.S.Volume[tankNode], "a finite tank runs dry");
            int wetAfterDry = sim.S.FWet[C(d, 9, 12)];
            RunTo(sim, 700);
            Check.True(sim.S.FWet[C(d, 9, 12)] < wetAfterDry || wetAfterDry == 0, "wetness decays once the water is gone");
        }

        [SimTest("fire")]
        public static void Sprinkler_Keeps_Its_Cells_From_Igniting()
        {
            var d = Fixture();
            var control = new Simulator(d, RuleProfile.Standard);
            Check.True(EverBurns(d, control, 2000, (9, 11), (9, 12), (9, 13)), "without spray the far column burns");

            var sim = new Simulator(d, RuleProfile.Standard);
            sim.Submit(Command.Place(d.SocketIndex["sprinkler:01"], ToolKind.Sprinkler));
            Check.False(EverBurns(d, sim, 2000, (9, 11), (9, 12), (9, 13)), "sprayed cells never ignite while the tank lasts");
        }

        [SimTest("fire")]
        public static void Heat_Latches_The_Road_Next_To_The_Fire()
        {
            var d = Fixture();
            var sim = new Simulator(d, RuleProfile.Standard);
            int edge = d.EdgeIndex["g_west_road"];
            Check.True(sim.S.EdgeOpen[edge], "road open before the fire");
            bool closed = false, roadBurned = false;
            RunTo(sim, 1500, () =>
            {
                if (!sim.S.EdgeOpen[edge]) closed = true;
                for (int x = 5; x <= 10; x++) if (sim.CellFire(C(d, x, 10)) == FireState.Burning) roadBurned = true;
            });
            Check.True(closed, "heat from the burning terrace closes the road");
            Check.False(roadBurned, "a road has no fuel and never burns");
        }

        [SimTest("fire")]
        public static void Fire_Run_Is_Deterministic_And_Clones_Exactly()
        {
            var d = Fixture();
            var a = new Simulator(d, RuleProfile.Standard);
            a.Submit(Command.Place(d.SocketIndex["sprinkler:01"], ToolKind.Sprinkler));
            RunTo(a, 350);
            var b = new Simulator(a);
            RunTo(a, 900);
            RunTo(b, 900);
            Check.Equal(a.S.Hash(), b.S.Hash(), "clone stepped the same horizon has the same hash");
            for (int c = 0; c < d.GridW * d.GridH; c++) Check.Equal(a.CellHeat(c), b.CellHeat(c), "heat cell " + c);

            var r = new Simulator(d, RuleProfile.Standard);
            r.Submit(Command.Place(d.SocketIndex["sprinkler:01"], ToolKind.Sprinkler));
            RunTo(r, 900);
            Check.Equal(a.S.Hash(), r.S.Hash(), "replay from scratch matches");
        }

        // Initial state, not a tick-zero event (spec 02 §2): a map can author a fire already burning at S=0.
        [SimTest("fire")]
        public static void Map_Can_Start_With_A_Burning_Fire()
        {
            var d = Fixture(edit: (map, contract) =>
            {
                map["fire"].Set("burning", JsonNode.Parse("[[7,12]]"));
                // drop the scheduled ignition so only the authored initial fire exists
                var events = JsonNode.Parse("[]");
                foreach (var ev in contract["events"].Elements()) if (ev.Str("id") != "fire_ignite") events.Add(ev);
                contract.Set("events", events);
            });
            var sim = new Simulator(d, RuleProfile.Standard);
            int c = d.CellIndex(7, 12);
            Check.Equal(FireState.Burning, sim.CellFire(c), "burning at S=0");
            Check.True(sim.CellHeat(c) >= Simulator.FireDangerHeat, "initial burning heat at least the danger level");
            Check.True(sim.S.FLatch[c], "a burning cell latches ground admission from the start");
            RunTo(sim, 200);
            Check.True(sim.CellHeat(d.CellIndex(8, 12)) > 0, "it spreads heat from the first pulse");
            bool threw = false;
            try { Fixture(edit: (map, contract) => map["fire"].Set("burning", JsonNode.Parse("[[2,2]]"))); } catch (ContentException) { threw = true; }
            Check.True(threw, "a cell with no fuel cannot start burning");
        }

        // A pump may draw from or deliver to a finite store (tanks and ponds are water nodes too, spec 02 §4).
        [SimTest("fire")]
        public static void Pump_Moves_Water_From_Pond_To_Tank()
        {
            var d = Fixture(tankVolume: 0, edit: (map, contract) =>
            {
                map.Set("stores", JsonNode.Parse("[{\"id\":\"tank\",\"elev\":1200,\"volume\":0},{\"id\":\"pond\",\"elev\":1000,\"volume\":500000000}]"));
                map["sockets"].Add(JsonNode.Parse("{\"id\":\"pump:09\",\"tool\":\"pump\",\"at\":[20,20],\"intake\":\"pond\",\"outlet\":\"tank\",\"power\":\"grid\"}"));
                contract["permitted_tools"].Add(JsonNode.OfString("pump"));
                contract["maximum_stock"].Set("pump", 1);
                contract["default_loadout"].Set("pump", 1);
            });
            var sim = new Simulator(d, RuleProfile.Standard);
            int tank = d.StoreNode[d.StoreIndex["tank"]], pond = d.StoreNode[d.StoreIndex["pond"]];
            // the W1-M1 base map has river sources and an outlet, so check the closed pond+tank pair, not the whole map
            long pair = sim.S.Volume[tank] + sim.S.Volume[pond];
            sim.Submit(Command.Place(d.SocketIndex["pump:09"], ToolKind.Pump));
            RunTo(sim, 50, () => Check.Equal(pair, sim.S.Volume[tank] + sim.S.Volume[pond], "pumping between stores conserves volume"));
            Check.True(sim.S.Volume[tank] > 0, "the tank fills");
            Check.Equal(500_000_000L - sim.S.Volume[tank], sim.S.Volume[pond], "exactly what left the pond");
        }
    }
}
