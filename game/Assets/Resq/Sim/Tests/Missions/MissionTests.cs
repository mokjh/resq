using System.Diagnostics;
using System.IO;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests
{
    /// <summary>End-to-end checks on authored missions (content/), using the shared compiler and engine.</summary>
    public static class MissionTests
    {
        static JsonNode _rules, _contracts;

        public static MissionDef Load(string missionId, string layout = "wide")
        {
            if (_rules == null)
            {
                _rules = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/equipment_rules.json")));
                _contracts = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("specification/content/mission_contracts.json")));
            }
            var parts = missionId.ToLowerInvariant().Split('-');
            var map = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve($"content/{parts[0]}/{parts[1]}/map.json")));
            JsonNode contract = null;
            foreach (var m in _contracts["missions"].Elements()) if (m.Str("id") == missionId) contract = m;
            return MapCompiler.Compile(map, contract, _rules, layout);
        }

        public static StrategyScript Strategy(string missionId, string name)
        {
            var parts = missionId.ToLowerInvariant().Split('-');
            return StrategyScript.Parse(JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve($"content/{parts[0]}/{parts[1]}/strategies/{name}.json"))));
        }

        [SimTest("missions")]
        public static void W1M1_StrategyA_Wins_And_Replays_Identically()
        {
            var d = Load("W1-M1");
            var a = StrategyRunner.Run(d, RuleProfile.Standard, Strategy("W1-M1", "A"));
            var b = StrategyRunner.Run(d, RuleProfile.Standard, Strategy("W1-M1", "A"));
            Check.True(a.Success, "strategy A must succeed");
            Check.Equal(a.Hash, b.Hash, "deterministic replay hash");
        }

        [SimTest("missions")]
        public static void W1M1_NoInput_Fails()
        {
            var r = StrategyRunner.Run(Load("W1-M1"), RuleProfile.Standard, null);
            Check.False(r.Success, "doing nothing must lose");
        }

        // RV02: the live run is untouched by a forecast.
        [SimTest("forecast")]
        public static void RV02_Forecast_Leaves_Source_Unchanged()
        {
            var d = Load("W1-M1");
            var live = new Simulator(d, RuleProfile.Standard);
            for (int i = 0; i < 50; i++) live.Step();
            ulong before = live.S.Hash();
            var f = new Forecaster();
            var result = new ForecastResult(d);
            f.Run(live, Command.Place(d.SocketIndex["barrier:01"], ToolKind.Barrier), 300, result);
            Check.Equal(before, live.S.Hash(), "live state hash unchanged");
            Check.Equal(0, live.S.C.EquipmentCount, "no equipment leaked into the live run");
        }

        // RV01: forecast with a preview equals actually applying it and stepping the same horizon.
        [SimTest("forecast")]
        public static void RV01_Preview_Equals_Live_Outcome()
        {
            var d = Load("W1-M1");
            var live = new Simulator(d, RuleProfile.Standard);
            for (int i = 0; i < 20; i++) live.Step();
            var cmd = Command.Place(d.SocketIndex["barrier:01"], ToolKind.Barrier);
            var result = new ForecastResult(d);
            new Forecaster().Run(live, cmd, 300, result);
            Check.Equal(Reject.OK, result.PreviewVerdict, "preview accepted");
            live.Submit(cmd);
            long target = live.S.C.S + 300;
            while (live.S.C.S < target && !live.IsOver) live.Step();
            for (int c = 0; c < d.WaterCount; c++)
                Check.Equal(live.Depth(c), result.EndDepth[c], "depth at horizon, cell " + c);
        }

        [SimTest("forecast")]
        public static void Forecast_Reports_Rejection_And_Is_Fast()
        {
            var d = Load("W1-M1");
            var live = new Simulator(d, RuleProfile.Standard);
            var f = new Forecaster();
            var result = new ForecastResult(d);
            // bridge tool on a barrier socket is incompatible
            f.Run(live, Command.Place(d.SocketIndex["barrier:01"], ToolKind.Bridge), 300, result);
            Check.Equal(Reject.SocketIncompatible, result.PreviewVerdict, "rejection surfaces in the preview");
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) f.Run(live, Command.Place(d.SocketIndex["barrier:01"], ToolKind.Barrier), 300, result);
            double ms = sw.Elapsed.TotalMilliseconds / 5;
            System.Console.WriteLine($"    forecast 300 ticks W1-M1: {ms:F1} ms (host)");
            Check.True(ms < 200, $"forecast too slow on host: {ms:F1} ms");
        }

        // Relay hand-off (found by the W3 mission helper): ordering the second winch before every group member has left
        // the first must not strand anyone. Previously the 1780 order lost a step-free resident at the intermediate node.
        [SimTest("missions")]
        public static void W3M2_Relay_Hand_Off_Survives_An_Early_Second_Order()
        {
            var d = Load("W3-M2");
            var doc = JsonNode.Parse(File.ReadAllText(RepoPaths.Resolve("content/w3/m2/strategies/B.json")));
            int moved = 0;
            foreach (var st in doc["steps"].Elements())
                if (st.Long("t") == 1830 && st.Str("vehicle") == "winch:02") { st.Set("t", 1780); moved++; }
            Check.Equal(1, moved, "the reproduction step exists in plan B");
            var r = StrategyRunner.Run(d, RuleProfile.Standard, StrategyScript.Parse(doc));
            Check.True(r.Success, "the early hand-off still succeeds");
            Check.Equal(14, r.Rescued, "nobody is stranded at the intermediate node");
        }
    }
}
