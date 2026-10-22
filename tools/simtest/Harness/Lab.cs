using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Resq.Sim.Headless
{
    /// <summary>
    /// SimLab: run missions headlessly for authoring and validation.
    ///   lab show  W1-M1 [wide|compact]                      — ASCII map with nodes/sockets
    ///   lab run   W1-M1 wide standard A [--every 300] [--maps 600,1200]
    ///   lab validate W1-M1 [--all]                            — no-input must fail; A and B must win (both layouts, 3 presets)
    /// </summary>
    public static class Lab
    {
        public static string Root;

        public static int Run(string[] args)
        {
            Root = Testing.RepoPathsHeadless.RepoRoot();
            if (args.Length < 2) { Console.WriteLine("usage: lab show|run|validate MISSION ..."); return 2; }
            string cmd = args[0], mission = args[1];
            try
            {
                switch (cmd)
                {
                    case "show": return Show(mission, args.Length > 2 ? args[2] : "wide");
                    case "run": return RunOne(mission, args.Skip(2).ToArray());
                    case "validate": return Validate(mission.Split(','), args.Contains("--quick"));
                }
            }
            catch (ContentException ex) { Console.WriteLine("CONTENT ERROR: " + ex.Message); return 2; }
            Console.WriteLine("unknown lab command " + cmd);
            return 2;
        }

        // ------------------------------------------------------------------ content loading
        static readonly Dictionary<string, JsonNode> _contracts = new Dictionary<string, JsonNode>();
        static JsonNode _rules;

        public static string MissionDir(string missionId)
        {
            // W1-M1 -> content/w1/m1
            var parts = missionId.ToLowerInvariant().Split('-');
            return Path.Combine(Root, "content", parts[0], parts[1]);
        }

        public static MissionDef Load(string missionId, string layout)
        {
            if (_rules == null)
            {
                _rules = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specification/content/equipment_rules.json")));
                var contracts = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specification/content/mission_contracts.json")));
                foreach (var m in contracts["missions"].Elements()) _contracts[m.Str("id")] = m;
            }
            var map = JsonNode.Parse(File.ReadAllText(Path.Combine(MissionDir(missionId), "map.json")));
            var contract = _contracts[missionId];
            var overridePath = Path.Combine(MissionDir(missionId), "contract_overrides.json");
            if (File.Exists(overridePath))
            {
                contract = JsonNode.Parse(contract.ToCanonical());
                foreach (var kv in JsonNode.Parse(File.ReadAllText(overridePath)).Properties()) contract.Set(kv.Key, kv.Value);
            }
            return MapCompiler.Compile(map, contract, _rules, layout);
        }

        public static StrategyScript LoadStrategy(string missionId, string name)
        {
            string path = Path.Combine(MissionDir(missionId), "strategies", name + ".json");
            if (!File.Exists(path)) return null;
            return StrategyScript.Parse(JsonNode.Parse(File.ReadAllText(path)));
        }

        static RuleProfile Profile(string s) =>
            s == "relaxed" ? RuleProfile.Relaxed : s == "command" ? RuleProfile.Command : RuleProfile.Standard;

        // ------------------------------------------------------------------ show
        static int Show(string missionId, string layout)
        {
            var d = Load(missionId, layout);
            Console.WriteLine($"{d.MissionId} {d.Title} [{layout}] residents={d.TotalResidents} min={d.MinimumRescues} budget={d.SupplyBudget}");
            Console.WriteLine($"water nodes={d.WaterCount} links={d.LinkFrom.Length} nav nodes={d.NodeIds.Length} edges={d.EdgeIds.Length} sockets={d.SocketIds.Length}");
            var sim = new Simulator(d, RuleProfile.Standard);
            Console.WriteLine(DepthMap(sim, true));
            return 0;
        }

        public static string DepthMap(Simulator sim, bool markers)
        {
            var d = sim.D;
            var sb = new StringBuilder();
            var mark = new Dictionary<int, char>();
            if (markers)
            {
                foreach (var n in Enumerable.Range(0, d.NodeIds.Length)) mark[d.NodeCell[n]] = 'N';
                for (int s = 0; s < d.SocketIds.Length; s++) if (d.SocketCell[s] >= 0) mark[d.SocketCell[s]] = 'S';
                for (int r = 0; r < d.ResidentIds.Length; r++)
                {
                    var st = (ResidentState)sim.S.RState[r];
                    if (st == ResidentState.AtNode || st == ResidentState.Walking) mark[d.NodeCell[sim.S.RNode[r]]] = 'p';
                }
            }
            sb.AppendLine("   " + string.Concat(Enumerable.Range(0, d.GridW).Select(x => (x % 10).ToString())));
            for (int y = d.GridH - 1; y >= 0; y--)
            {
                sb.Append($"{y,2} ");
                for (int x = 0; x < d.GridW; x++)
                {
                    int c = d.CellIndex(x, y);
                    if (mark.TryGetValue(c, out char ch)) { sb.Append(ch); continue; }
                    long dep = sim.Depth(c);
                    sb.Append(dep == 0 ? (d.CellElevation[c] >= 1200 ? '#' : d.CellElevation[c] >= 300 ? '=' : '.') :
                              dep < 100 ? '-' : dep < 300 ? '1' : dep < 600 ? '2' : dep < 1000 ? '3' : dep < 2000 ? '4' : '5');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ run
        sealed class PrintSink : ISimEventSink
        {
            public Simulator Sim;
            public readonly List<string> Lines = new List<string>();
            public void Emit(in SimEvent e)
            {
                var d = Sim?.D;
                string what;
                switch (e.Kind)
                {
                    case SimEventKind.CommandResolved:
                        what = $"command #{e.Subject} {(CommandKind)e.A} -> {e.Code}"; break;
                    case SimEventKind.Warning: what = $"WARNING {d.Events[e.Subject].Id}"; break;
                    case SimEventKind.EventApplied: what = $"EVENT {d.Events[e.Subject].Id}"; break;
                    case SimEventKind.EquipmentPlaced: what = $"placed {(ToolKind)e.B} at {d.SocketIds[e.A]}"; break;
                    case SimEventKind.EquipmentBroken: what = $"BROKEN equipment {e.Subject} at {d.SocketIds[e.A]}"; break;
                    case SimEventKind.ResidentEvacuated: what = $"evacuated {d.ResidentIds[e.Subject]} at {d.NodeIds[e.A]} mode={(Traversal)e.B}"; break;
                    case SimEventKind.ResidentBackup: what = $"BACKUP {d.ResidentIds[e.Subject]}"; break;
                    case SimEventKind.MilestoneReached: what = $"milestone {d.Milestones[e.Subject].Id}"; break;
                    case SimEventKind.ObjectiveUpdated: what = $"objective {d.Objectives[e.Subject].Id} failed"; break;
                    case SimEventKind.GeneratorShutdown: what = $"generator {e.Subject} shut down"; break;
                    case SimEventKind.RouteBlocked: what = $"route blocked subject={e.Subject} edge={d.EdgeIds[e.A]}"; break;
                    case SimEventKind.OperationStarted: what = "operation started"; break;
                    case SimEventKind.OperationEnded: what = "operation ended"; break;
                    default: return;
                }
                Lines.Add($"  O={e.O,5} S={e.S,5}  {what}");
            }
        }

        static int RunOne(string missionId, string[] a)
        {
            string layout = a.Length > 0 ? a[0] : "wide";
            var profile = Profile(a.Length > 1 ? a[1] : "standard");
            string strat = a.Length > 2 ? a[2] : "none";
            int every = 0;
            var maps = new HashSet<long>();
            var probes = new List<(int x, int y)>();
            bool guided = a.Contains("--guided");
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == "--every") every = int.Parse(a[i + 1]);
                if (a[i] == "--maps") foreach (var t in a[i + 1].Split(',')) maps.Add(long.Parse(t));
                if (a[i] == "--probe") // "x,y;x,y" -> print water depth (mm) at those cells on --every lines
                    foreach (var t in a[i + 1].Split(';')) { var xy = t.Split(','); probes.Add((int.Parse(xy[0]), int.Parse(xy[1]))); }
            }
            var d = Load(missionId, layout);
            var script = strat == "none" ? null : LoadStrategy(missionId, strat);
            if (strat != "none" && script == null) { Console.WriteLine("missing strategy " + strat); return 2; }
            var sink = new PrintSink();
            var sim = new Simulator(d, profile, sink, guided);
            sink.Sim = sim;
            var log = new List<string>();
            int cursor = 0;
            long steps = 0;
            while (!sim.IsOver && steps++ < 30000)
            {
                script?.SubmitDue(sim, ref cursor, log);
                sim.Step();
                if (every > 0 && sim.S.C.O % every == 0)
                    sink.Lines.Add($"  -- O={sim.S.C.O} S={sim.S.C.S} evac={sim.S.C.Evacuated} backup={sim.S.C.AwaitingBackup} " +
                                   $"phase={sim.S.C.Phase} vol={sim.TotalVolume() / 1_000_000_000}m3 " + Positions(sim) +
                                   (probes.Count > 0 ? " depth[" + string.Join(" ", probes.Select(p => $"{p.x},{p.y}:{sim.Depth(d.CellIndex(p.x, p.y))}")) + "]" : ""));
                if (maps.Contains(sim.S.C.O)) sink.Lines.Add($"depth map at O={sim.S.C.O}:\n" + DepthMap(sim, true));
            }
            foreach (var l in log) Console.WriteLine(l);
            foreach (var l in sink.Lines) Console.WriteLine(l);
            Console.WriteLine($"RESULT {missionId} {layout} {profile} {strat}: success={sim.S.C.Success} stars={sim.S.C.Stars} " +
                              $"score={sim.S.C.ScoreValue} rescued={sim.S.C.Evacuated}/{d.TotalResidents} backup={sim.S.C.AwaitingBackup} " +
                              $"O={sim.S.C.O} S={sim.S.C.S} supplies_left={sim.RemainingSupplies} hash={sim.S.Hash():x16}");
            for (int i = 0; i < d.Objectives.Length; i++)
                Console.WriteLine($"  objective {d.Objectives[i].Id,-16} {d.Objectives[i].Tier,-9} {(sim.ObjectiveFulfilled(i) ? "MET" : "missed")}");
            return 0;
        }

        static string Positions(Simulator sim)
        {
            var d = sim.D;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (int r = 0; r < d.ResidentIds.Length; r++)
            {
                var st = (ResidentState)sim.S.RState[r];
                string k = st == ResidentState.Evacuated ? "evac@" + d.NodeIds[sim.S.RNode[r]]
                    : st == ResidentState.Aboard ? "aboard" : st.ToString() + "@" + d.NodeIds[sim.S.RNode[r]];
                counts[k] = counts.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            return string.Join(" ", counts.Select(kv => $"{kv.Key}x{kv.Value}"));
        }

        // ------------------------------------------------------------------ validate
        static int Validate(string[] missions, bool quick)
        {
            int failures = 0, runs = 0;
            var layouts = new[] { "wide", "compact" };
            var profiles = quick ? new[] { RuleProfile.Standard } : new[] { RuleProfile.Standard, RuleProfile.Relaxed, RuleProfile.Command };
            foreach (var mission in missions)
            {
                foreach (var layout in layouts)
                {
                    MissionDef d;
                    try { d = Load(mission, layout); }
                    catch (Exception ex) { Console.WriteLine($"FAIL {mission} {layout}: compile {ex.Message}"); failures++; continue; }
                    foreach (var profile in profiles)
                    {
                        var none = StrategyRunner.Run(d, profile, null);
                        runs++;
                        bool noneOk = !none.Success;
                        Console.WriteLine($"{(noneOk ? "ok  " : "FAIL")} {mission} {layout,-7} {profile,-8} no-input   -> success={none.Success} rescued={none.Rescued}");
                        if (!noneOk) failures++;
                        foreach (var name in new[] { "A", "B" })
                        {
                            var script = LoadStrategy(mission, name);
                            runs++;
                            if (script == null) { Console.WriteLine($"FAIL {mission} strategy {name} missing"); failures++; continue; }
                            var r = StrategyRunner.Run(d, profile, script);
                            Console.WriteLine($"{(r.Success ? "ok  " : "FAIL")} {mission} {layout,-7} {profile,-8} strategy {name} -> stars={r.Stars} score={r.Score} rescued={r.Rescued}/{d.TotalResidents} backup={r.Backup} S={r.EndS}");
                            if (!r.Success) failures++;
                        }
                    }
                }
            }
            Console.WriteLine($"VALIDATION {(failures == 0 ? "PASSED" : "FAILED")}: {runs - failures}/{runs} runs as expected");
            return failures == 0 ? 0 : 1;
        }
    }
}

namespace Resq.Sim.Testing
{
    public static class RepoPathsHeadless
    {
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "specification"))) dir = dir.Parent;
            if (dir == null) throw new DirectoryNotFoundException("repo root not found");
            return dir.FullName;
        }
    }
}
