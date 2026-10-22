using System;
using System.Collections.Generic;
using System.Linq;

namespace Resq.Sim
{
    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }
    }

    /// <summary>
    /// Compiles an authored text map (content/wN/mM/map.json) together with the frozen mission contract and
    /// equipment rules into an immutable <see cref="MissionDef"/>. One map drives both the simulation graph and the
    /// visual board, so they cannot disagree.
    /// </summary>
    public static class MapCompiler
    {
        public const long DefaultArea = 1_000_000;
        public const long DefaultConductance = 100_000;
        public const long DefaultMaxTransfer = 100_000_000;

        static readonly string[] ToolNames =
        {
            "barrier", "beacon", "brace", "bridge", "cooler", "crawler", "firebreak", "generator", "pump", "relay",
            "shelter", "skiff", "sprinkler", "winch"
        };

        public static ToolKind ParseTool(string name)
        {
            int i = Array.IndexOf(ToolNames, name);
            if (i < 0) throw new ContentException($"unknown tool '{name}'");
            return (ToolKind)i;
        }

        public static string ToolName(ToolKind t) => ToolNames[(int)t];

        public static MissionDef Compile(JsonNode map, JsonNode contract, JsonNode equipmentRules, string layoutClass)
        {
            var d = new MissionDef
            {
                MissionId = contract.Str("id"),
                WorldId = contract.Str("world_id"),
                Title = contract.Str("title"),
                LayoutClass = layoutClass,
                DurationTicks = contract.Long("duration_scenario_ticks"),
                TotalResidents = contract.Int("total_residents"),
                MinimumRescues = contract.Int("minimum_rescues"),
                SupplyBudget = contract.Int("supply_budget"),
                GuidedFirstPlay = contract.Str("tutorial_policy") == "guided_first_play_then_unguided",
            };
            ApplyOverrides(map, layoutClass);
            CompileStock(d, contract, equipmentRules);
            var ctx = new Ctx(d, map);
            ctx.CompileGrid();
            ctx.CompileRegions();
            ctx.CompileWater();
            ctx.CompileNodes();
            ctx.CompileEdges();
            ctx.CompilePower();
            ctx.CompileSockets();
            ctx.CompileInfrastructure();
            ctx.CompileGroups(contract);
            ctx.CompileVehicles();
            ctx.CompileRoutes();
            ctx.CompileEvents(contract);
            ctx.CompileObjectives(contract);
            ctx.Validate();
            d.ContentHash = Fnv(map.ToCanonical() + "|" + contract.ToCanonical() + "|" + layoutClass);
            return d;
        }

        static void ApplyOverrides(JsonNode map, string layoutClass)
        {
            var ov = map["class_overrides"]?[layoutClass];
            if (ov == null) return;
            foreach (var kv in ov.Properties())
            {
                // shallow replace of whole top-level sections, or per-id merge for arrays of objects with "id"
                var baseNode = map[kv.Key];
                if (baseNode != null && baseNode.Kind == JsonKind.Array && kv.Value.Kind == JsonKind.Array)
                {
                    foreach (var item in kv.Value.Elements())
                    {
                        string id = item.Str("id");
                        var target = baseNode.Elements().FirstOrDefault(x => x.Str("id") == id);
                        if (target == null) { baseNode.Add(item); continue; }
                        foreach (var p in item.Properties()) target.Set(p.Key, p.Value);
                    }
                }
                else map.Set(kv.Key, kv.Value);
            }
        }

        static void CompileStock(MissionDef d, JsonNode contract, JsonNode rules)
        {
            foreach (var e in rules["equipment"].Elements())
                d.ToolCost[(int)ParseTool(e.Str("id"))] = e.Int("supply_cost");
            foreach (var t in contract["permitted_tools"].Elements()) d.Permitted[(int)ParseTool(t.AsString())] = true;
            foreach (var kv in contract["maximum_stock"].Properties()) d.MaxStock[(int)ParseTool(kv.Key)] = kv.Value.AsInt();
            foreach (var kv in contract["default_loadout"].Properties()) d.DefaultLoadout[(int)ParseTool(kv.Key)] = kv.Value.AsInt();
        }

        static string Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            unchecked { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } }
            return h.ToString("x16");
        }

        // =====================================================================================================
        sealed class Ctx
        {
            readonly MissionDef d;
            readonly JsonNode m;
            char[] _chars;
            readonly Dictionary<char, JsonNode> _legend = new Dictionary<char, JsonNode>();
            readonly Dictionary<string, int> _powerIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> _vehicleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<long, int> _linkByPair = new Dictionary<long, int>();
            readonly Dictionary<string, JsonNode> _roles = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            readonly Dictionary<string, int> _sourceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<string, int> _milestoneIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            public Ctx(MissionDef def, JsonNode map) { d = def; m = map; }

            int Cell(JsonNode xy)
            {
                int x = xy[0].AsInt(), y = xy[1].AsInt();
                if (x < 0 || y < 0 || x >= d.GridW || y >= d.GridH) throw new ContentException($"cell out of range ({x},{y})");
                return d.CellIndex(x, y);
            }

            public void CompileGrid()
            {
                var rows = m["rows"];
                if (rows.Count != d.GridH) throw new ContentException($"map needs {d.GridH} rows, has {rows.Count}");
                foreach (var kv in m["legend"].Properties()) _legend[kv.Key[0]] = kv.Value;
                int n = d.GridW * d.GridH;
                _chars = new char[n];
                d.CellElevation = new long[n];
                d.CellTerrain = new byte[n];
                for (int r = 0; r < d.GridH; r++)
                {
                    string row = rows[r].AsString();
                    if (row.Length != d.GridW) throw new ContentException($"row {r} has {row.Length} chars, needs {d.GridW}");
                    int y = d.GridH - 1 - r; // first row is north
                    for (int x = 0; x < d.GridW; x++)
                    {
                        char ch = row[x];
                        if (!_legend.TryGetValue(ch, out var lg)) throw new ContentException($"legend missing '{ch}'");
                        int c = d.CellIndex(x, y);
                        _chars[c] = ch;
                        d.CellElevation[c] = lg.Long("elev");
                        d.CellTerrain[c] = (byte)lg.Int("code");
                    }
                }
            }

            public void CompileRegions()
            {
                var ids = new List<string>();
                var cells = new List<int[]>();
                var regions = m["regions"];
                if (regions != null)
                {
                    foreach (var kv in regions.Properties().OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        ids.Add(kv.Key);
                        cells.Add(ResolveCells(kv.Value));
                    }
                }
                d.RegionIds = ids.ToArray();
                d.RegionCells = cells.ToArray();
                for (int i = 0; i < d.RegionIds.Length; i++) d.RegionIndex[d.RegionIds[i]] = i;
            }

            /// <summary>Cells from a legend-char string ("RS"), an explicit [[x,y],...] list, or a rect {"x0","y0","x1","y1"}.</summary>
            int[] ResolveCells(JsonNode spec)
            {
                var list = new List<int>();
                if (spec.Kind == JsonKind.String)
                {
                    string chars = spec.AsString();
                    for (int c = 0; c < _chars.Length; c++) if (chars.IndexOf(_chars[c]) >= 0) list.Add(c);
                }
                else if (spec.Kind == JsonKind.Array)
                {
                    foreach (var xy in spec.Elements()) list.Add(Cell(xy));
                }
                else if (spec.Kind == JsonKind.Object)
                {
                    for (int y = spec.Int("y0"); y <= spec.Int("y1"); y++)
                        for (int x = spec.Int("x0"); x <= spec.Int("x1"); x++) list.Add(d.CellIndex(x, y));
                }
                list.Sort();
                return list.Distinct().ToArray();
            }

            public void CompileWater()
            {
                int n = d.GridW * d.GridH;
                d.WaterCount = n;
                d.WElev = new long[n]; d.WArea = new long[n]; d.WInitVol = new long[n]; d.WIsTank = new bool[n];
                for (int c = 0; c < n; c++)
                {
                    d.WElev[c] = d.CellElevation[c];
                    d.WArea[c] = DefaultArea;
                    long initDepth = _legend[_chars[c]].Long("init_depth");
                    d.WInitVol[c] = initDepth * DefaultArea;
                }
                var from = new List<int>(); var to = new List<int>(); var sill = new List<long>(); var cond = new List<long>();
                for (int y = 0; y < d.GridH; y++)
                {
                    for (int x = 0; x < d.GridW; x++)
                    {
                        int a = d.CellIndex(x, y);
                        if (x + 1 < d.GridW) AddLink(a, d.CellIndex(x + 1, y));
                        if (y + 1 < d.GridH) AddLink(a, d.CellIndex(x, y + 1));
                    }
                }
                void AddLink(int a, int b)
                {
                    if (_legend[_chars[a]].Flag("solid") || _legend[_chars[b]].Flag("solid")) return;
                    _linkByPair[Key(a, b)] = from.Count;
                    from.Add(a); to.Add(b);
                    sill.Add(Math.Max(d.CellElevation[a], d.CellElevation[b]));
                    cond.Add(Math.Min(_legend[_chars[a]].Long("cond", DefaultConductance), _legend[_chars[b]].Long("cond", DefaultConductance)));
                }
                int lc = from.Count;
                d.LinkFrom = from.ToArray(); d.LinkTo = to.ToArray(); d.LinkSill = sill.ToArray();
                d.LinkCond = cond.ToArray();
                d.LinkMax = Enumerable.Repeat(DefaultMaxTransfer, lc).ToArray();
                d.LinkForward = new bool[lc];
                d.LinkBarrierSocket = Enumerable.Repeat(-1, lc).ToArray();
                d.LinkGate = Enumerable.Repeat(-1, lc).ToArray();

                var sIds = new List<string>(); var sNode = new List<int>(); var sRate = new List<long>();
                if (m["sources"] != null)
                    foreach (var s in m["sources"].Elements().OrderBy(e => e.Str("id"), StringComparer.Ordinal))
                    {
                        _sourceIndex[s.Str("id")] = sIds.Count;
                        sIds.Add(s.Str("id")); sNode.Add(Cell(s["at"])); sRate.Add(s.Long("rate"));
                    }
                d.SourceIds = sIds.ToArray(); d.SourceNode = sNode.ToArray(); d.SourceRate0 = sRate.ToArray();
                var kIds = new List<string>(); var kNode = new List<int>(); var kRate = new List<long>();
                if (m["sinks"] != null)
                    foreach (var s in m["sinks"].Elements().OrderBy(e => e.Str("id"), StringComparer.Ordinal))
                    { kIds.Add(s.Str("id")); kNode.Add(Cell(s["at"])); kRate.Add(s.Long("rate")); }
                d.SinkIds = kIds.ToArray(); d.SinkNode = kNode.ToArray(); d.SinkRate0 = kRate.ToArray();
            }

            static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            int Link(int a, int b)
            {
                if (!_linkByPair.TryGetValue(Key(a, b), out int l))
                    throw new ContentException($"no hydraulic link between cells {a} and {b}");
                return l;
            }

            public void CompileNodes()
            {
                var nodes = m["nodes"].Elements().OrderBy(n => n.Str("id"), StringComparer.Ordinal).ToList();
                int k = nodes.Count;
                d.NodeIds = new string[k]; d.NodeFlags = new NodeFlags[k]; d.NodeCell = new int[k]; d.NodeWaterCell = new int[k];
                d.NodeCapacity = new int[k]; d.NodeBerths = new int[k]; d.NodeVehicleSlots = new int[k];
                d.NodeSafeDepth = new long[k]; d.NodeDockMinDepth = new long[k]; d.NodeDockMaxDepth = new long[k];
                d.NodeInfra = new int[k];
                for (int i = 0; i < k; i++)
                {
                    var n = nodes[i];
                    d.NodeIds[i] = n.Str("id");
                    d.NodeIndex[d.NodeIds[i]] = i;
                    d.NodeCell[i] = Cell(n["at"]);
                    d.NodeWaterCell[i] = n["water_at"] != null ? Cell(n["water_at"]) : d.NodeCell[i];
                    var flags = NodeFlags.None;
                    if (n["flags"] != null)
                        foreach (var f in n["flags"].Elements())
                            flags |= (NodeFlags)Enum.Parse(typeof(NodeFlags), f.AsString().Replace("_", ""), true);
                    d.NodeFlags[i] = flags;
                    d.NodeCapacity[i] = n.Int("capacity", 0);
                    d.NodeBerths[i] = n.Int("berths", (flags & NodeFlags.Dock) != 0 ? 1 : 0);
                    d.NodeVehicleSlots[i] = n.Int("vehicle_slots", 1);
                    d.NodeSafeDepth[i] = n.Long("safe_depth", 100);
                    d.NodeDockMinDepth[i] = n.Long("dock_min_depth", 100);
                    d.NodeDockMaxDepth[i] = n.Long("dock_max_depth", 2000);
                    d.NodeInfra[i] = -1;
                }
            }

            public void CompileEdges()
            {
                var edges = m["edges"].Elements().OrderBy(e => e.Str("id"), StringComparer.Ordinal).ToList();
                int k = edges.Count;
                d.EdgeIds = new string[k]; d.EdgeFrom = new int[k]; d.EdgeTo = new int[k]; d.EdgeSlots = new int[k];
                d.EdgeTicks = new int[k]; d.EdgeRequiredSocket = new int[k]; d.EdgeCurrent0 = new int[k];
                d.EdgeClass = new Traversal[k]; d.EdgeStepFree = new bool[k]; d.EdgeAdmin0 = new bool[k];
                d.EdgeMaxDepth = new long[k]; d.EdgeMinDepth = new long[k]; d.EdgeSampleCells = new int[k][];
                d.EdgeTags = new int[k][];
                var tagIds = new List<string>();
                for (int i = 0; i < k; i++)
                {
                    var e = edges[i];
                    d.EdgeIds[i] = e.Str("id");
                    d.EdgeIndex[d.EdgeIds[i]] = i;
                    d.EdgeFrom[i] = NodeRef(e.Str("from"));
                    d.EdgeTo[i] = NodeRef(e.Str("to"));
                    string cls = e.Str("class", "ground");
                    d.EdgeClass[i] = cls == "water" ? Traversal.Water : cls == "cable" ? Traversal.Cable : Traversal.Ground;
                    bool water = d.EdgeClass[i] == Traversal.Water;
                    d.EdgeSlots[i] = e.Int("slots", 2);
                    d.EdgeTicks[i] = e.Int("ticks");
                    if (d.EdgeTicks[i] <= 0) throw new ContentException($"edge {d.EdgeIds[i]} needs positive ticks");
                    d.EdgeStepFree[i] = e.Flag("step_free", true);
                    d.EdgeAdmin0[i] = e.Flag("enabled", true);
                    d.EdgeCurrent0[i] = e.Int("current", 1000);
                    d.EdgeMaxDepth[i] = e.Long("max_depth", water ? 2000 : 100);
                    d.EdgeMinDepth[i] = e.Long("min_depth", water ? 100 : 0);
                    d.EdgeRequiredSocket[i] = -1; // bound after sockets compile
                    d.EdgeSampleCells[i] = e["sample"] != null ? ResolveCells(e["sample"]) :
                        ManhattanPath(d.NodeCell[d.EdgeFrom[i]], d.NodeCell[d.EdgeTo[i]], e["via"]);
                    var tags = new List<int>();
                    if (e["tags"] != null)
                        foreach (var t in e["tags"].Elements())
                        {
                            int ti = tagIds.IndexOf(t.AsString());
                            if (ti < 0) { ti = tagIds.Count; tagIds.Add(t.AsString()); }
                            tags.Add(ti);
                        }
                    d.EdgeTags[i] = tags.ToArray();
                }
                d.TagIds = tagIds.ToArray();
                var adj = new List<int>[d.NodeIds.Length];
                for (int n = 0; n < adj.Length; n++) adj[n] = new List<int>();
                for (int i = 0; i < k; i++)
                {
                    adj[d.EdgeFrom[i]].Add(i);
                    adj[d.EdgeTo[i]].Add(i); // all nav edges are traversable both ways in rules-v1 maps
                }
                d.NodeOutEdges = adj.Select(l => l.ToArray()).ToArray();
            }

            /// <summary>x-first Manhattan rasterisation through optional waypoints (spec 04 §2.1).</summary>
            int[] ManhattanPath(int fromCell, int toCell, JsonNode via)
            {
                var pts = new List<int> { fromCell };
                if (via != null) foreach (var p in via.Elements()) pts.Add(Cell(p));
                pts.Add(toCell);
                var cells = new List<int>();
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    int x0 = d.CellX(pts[i]), y0 = d.CellY(pts[i]), x1 = d.CellX(pts[i + 1]), y1 = d.CellY(pts[i + 1]);
                    int x = x0, y = y0;
                    cells.Add(d.CellIndex(x, y));
                    while (x != x1) { x += Math.Sign(x1 - x); cells.Add(d.CellIndex(x, y)); }
                    while (y != y1) { y += Math.Sign(y1 - y); cells.Add(d.CellIndex(x, y)); }
                }
                return cells.Distinct().ToArray();
            }

            int NodeRef(string id)
            {
                if (id == null || !d.NodeIndex.TryGetValue(id, out int i)) throw new ContentException($"unknown node '{id}'");
                return i;
            }

            public void CompilePower()
            {
                var p = m["power"];
                var names = new List<string>();
                if (p?["nodes"] != null) foreach (var n in p["nodes"].Elements()) names.Add(n.Str("id") ?? n.AsString());
                names.Sort(StringComparer.Ordinal);
                d.PowerNodeCount = names.Count;
                d.PowerNodeIds = names.ToArray();
                for (int i = 0; i < names.Count; i++) _powerIndex[names[i]] = i;
                d.PowerNodeFixedSupply = new int[names.Count];
                d.PowerNodePlatformCells = new int[names.Count][];
                if (p?["nodes"] != null)
                    foreach (var n in p["nodes"].Elements())
                    {
                        if (n.Kind != JsonKind.Object) continue;
                        int i = _powerIndex[n.Str("id")];
                        d.PowerNodeFixedSupply[i] = n.Int("fixed_supply");
                    }
                var lf = new List<int>(); var lt = new List<int>(); var lr = new List<int>();
                if (p?["links"] != null)
                    foreach (var l in p["links"].Elements())
                    { lf.Add(PowerRef(l[0].AsString())); lt.Add(PowerRef(l[1].AsString())); lr.Add(-1); }
                d.PowerLinkFrom = lf.ToArray(); d.PowerLinkTo = lt.ToArray(); d.PowerLinkRelaySocket = lr.ToArray();
            }

            int PowerRef(string id)
            {
                if (id == null) return -1;
                if (!_powerIndex.TryGetValue(id, out int i)) throw new ContentException($"unknown power node '{id}'");
                return i;
            }

            public void CompileSockets()
            {
                var sockets = m["sockets"].Elements().OrderBy(s => s.Str("id"), StringComparer.Ordinal).ToList();
                int k = sockets.Count;
                d.SocketIds = new string[k]; d.SocketKind = new ToolKind[k]; d.SocketCell = new int[k]; d.SocketZone = new int[k];
                d.SocketEdge = new int[k]; d.SocketIntake = new int[k]; d.SocketOutlet = new int[k]; d.SocketNode = new int[k];
                d.SocketPowerNode = new int[k]; d.SocketDestination = new int[k]; d.SocketLinks = new int[k][];
                d.SocketPlatformCells = new int[k][]; d.SocketOrientations = new byte[k];
                var zones = new List<string>();
                for (int i = 0; i < k; i++)
                {
                    var s = sockets[i];
                    d.SocketIds[i] = s.Str("id");
                    d.SocketIndex[d.SocketIds[i]] = i;
                    var tool = ParseTool(s.Str("tool"));
                    d.SocketKind[i] = tool;
                    d.SocketCell[i] = s["at"] != null ? Cell(s["at"]) : -1;
                    string zone = s.Str("zone", d.SocketIds[i]);
                    int zi = zones.IndexOf(zone); if (zi < 0) { zi = zones.Count; zones.Add(zone); }
                    d.SocketZone[i] = zi;
                    d.SocketEdge[i] = s.Str("edge") != null ? EdgeRef(s.Str("edge")) : -1;
                    d.SocketIntake[i] = s["intake"] != null ? Cell(s["intake"]) : -1;
                    d.SocketOutlet[i] = s["outlet"] != null ? Cell(s["outlet"]) : -1;
                    d.SocketNode[i] = s.Str("node") != null ? NodeRef(s.Str("node")) : -1;
                    d.SocketDestination[i] = s.Str("destination") != null ? NodeRef(s.Str("destination")) : -1;
                    d.SocketPowerNode[i] = PowerRef(s.Str("power"));
                    d.SocketOrientations[i] = (byte)s.Int("orientations", 1);
                    d.SocketPlatformCells[i] = s["platform"] != null ? ResolveCells(s["platform"]) : null;
                    var links = new List<int>();
                    if (s["links"] != null)
                        foreach (var pair in s["links"].Elements()) links.Add(Link(Cell(pair[0]), Cell(pair[1])));
                    d.SocketLinks[i] = links.ToArray();
                    foreach (int l in links)
                    {
                        if (d.LinkBarrierSocket[l] >= 0) throw new ContentException($"link {l} bound to two barrier sockets");
                        d.LinkBarrierSocket[l] = i;
                    }
                    if (tool == ToolKind.Bridge && d.SocketEdge[i] >= 0) d.EdgeRequiredSocket[d.SocketEdge[i]] = i;
                    if (tool == ToolKind.Pump && (d.SocketIntake[i] < 0 || d.SocketOutlet[i] < 0))
                        throw new ContentException($"pump socket {d.SocketIds[i]} needs intake and outlet");
                    if ((tool == ToolKind.Skiff || tool == ToolKind.Crawler) && d.SocketNode[i] < 0)
                        throw new ContentException($"vehicle socket {d.SocketIds[i]} needs a node");
                }
            }

            int EdgeRef(string id)
            {
                if (!d.EdgeIndex.TryGetValue(id, out int e)) throw new ContentException($"unknown edge '{id}'");
                return e;
            }

            public void CompileInfrastructure()
            {
                var list = m["infrastructure"]?.Elements().OrderBy(x => x.Str("id"), StringComparer.Ordinal).ToList()
                           ?? new List<JsonNode>();
                int k = list.Count;
                d.InfraIds = new string[k]; d.InfraPowerNode = new int[k]; d.InfraDemand0 = new int[k];
                d.InfraPriority0 = new int[k]; d.InfraRegion = new int[k]; d.InfraMaxDepth = new long[k];
                d.InfraRequiredNodes = new int[k][];
                for (int i = 0; i < k; i++)
                {
                    var x = list[i];
                    d.InfraIds[i] = x.Str("id");
                    d.InfraIndex[d.InfraIds[i]] = i;
                    d.InfraPowerNode[i] = PowerRef(x.Str("power"));
                    d.InfraDemand0[i] = x.Int("demand");
                    d.InfraPriority0[i] = x.Int("priority");
                    d.InfraRegion[i] = x.Str("region") != null ? d.RegionIndex[x.Str("region")] : -1;
                    d.InfraMaxDepth[i] = x.Long("max_depth", 100);
                    var nodes = new List<int>();
                    if (x["nodes"] != null) foreach (var n in x["nodes"].Elements()) nodes.Add(NodeRef(n.AsString()));
                    d.InfraRequiredNodes[i] = nodes.ToArray();
                    if (x.Flag("gates_dock"))
                        foreach (int n in nodes) d.NodeInfra[n] = i;
                }
            }

            public void CompileGroups(JsonNode contract)
            {
                var groups = contract["groups"].Elements().OrderBy(g => g.Str("id"), StringComparer.Ordinal).ToList();
                var mapGroups = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
                if (m["groups"] != null) foreach (var g in m["groups"].Elements()) mapGroups[g.Str("id")] = g;
                int k = groups.Count;
                d.GroupIds = new string[k]; d.GroupStart = new int[k]; d.GroupAssembly = new int[k]; d.GroupPickup = new int[k];
                d.GroupHeld = new bool[k]; d.GroupResidents = new int[k][];
                var rIds = new List<string>(); var rGroup = new List<int>(); var rStep = new List<bool>();
                for (int i = 0; i < k; i++)
                {
                    var g = groups[i];
                    string id = g.Str("id");
                    d.GroupIds[i] = id;
                    d.GroupIndex[id] = i;
                    mapGroups.TryGetValue(id, out var mg);
                    d.GroupStart[i] = NodeRef(mg?.Str("start") ?? g.Str("start_node_id"));
                    d.GroupAssembly[i] = NodeRef(mg?.Str("assembly") ?? g.Str("assembly_node_id"));
                    d.GroupPickup[i] = NodeRef(mg?.Str("pickup") ?? (id + "_pickup"));
                    d.GroupHeld[i] = g.Flag("initially_held") || (mg?.Flag("held") ?? false);
                    int count = g.Int("count");
                    int stepFree = g["mobility_counts"]?.Int("step_free") ?? 0;
                    var members = new List<int>();
                    for (int n = 1; n <= count; n++)
                    {
                        members.Add(rIds.Count);
                        rIds.Add($"{d.MissionId}:resident:{id}:{n:00}");
                        rGroup.Add(i);
                        rStep.Add(n > count - stepFree); // last members of each group are step-free
                    }
                    d.GroupResidents[i] = members.ToArray();
                }
                d.ResidentIds = rIds.ToArray(); d.ResidentGroup = rGroup.ToArray(); d.ResidentStepFree = rStep.ToArray();
                if (d.ResidentIds.Length != d.TotalResidents)
                    throw new ContentException($"population {d.ResidentIds.Length} != contract {d.TotalResidents}");
            }

            public void CompileVehicles()
            {
                var list = m["vehicles"]?.Elements().OrderBy(v => v.Str("id"), StringComparer.Ordinal).ToList() ?? new List<JsonNode>();
                int k = list.Count;
                d.FixedVehicleIds = new string[k]; d.FixedVehicleKind = new VehicleKind[k]; d.FixedVehicleNode = new int[k];
                d.FixedVehicleSeats = new int[k]; d.FixedVehicleSlots = new int[k];
                for (int i = 0; i < k; i++)
                {
                    var v = list[i];
                    d.FixedVehicleIds[i] = v.Str("id");
                    _vehicleIndex[v.Str("id")] = i;
                    string kind = v.Str("kind", "fixed_bus");
                    d.FixedVehicleKind[i] = kind == "fixed_bus" ? VehicleKind.FixedBus : kind == "crawler" ? VehicleKind.Crawler :
                                            kind == "winch" ? VehicleKind.Winch : VehicleKind.Skiff;
                    d.FixedVehicleNode[i] = NodeRef(v.Str("node"));
                    d.FixedVehicleSeats[i] = v.Int("seats", 4);
                    d.FixedVehicleSlots[i] = v.Int("slots", 2);
                }
                var tut = m["tutorial"];
                if (tut != null)
                {
                    int g = d.GroupIndex[tut.Str("group")];
                    d.TutorialGroup = g;
                    d.BusAutoRoute = new[] { g, NodeRef(tut.Str("pickup")), NodeRef(tut.Str("destination")) };
                }
            }

            public void CompileRoutes()
            {
                var list = m["routes"]?.Elements().OrderBy(r => r.Str("id"), StringComparer.Ordinal).ToList() ?? new List<JsonNode>();
                d.RouteIds = list.Select(r => r.Str("id")).ToArray();
                d.RouteFrom = list.Select(r => NodeRef(r.Str("from"))).ToArray();
                d.RouteTo = list.Select(r => NodeRef(r.Str("to"))).ToArray();
                d.RouteClass = list.Select(r => r.Str("class", "ground") == "water" ? Traversal.Water : Traversal.Ground).ToArray();
                if (m["roles"] != null) foreach (var kv in m["roles"].Properties()) _roles[kv.Key] = kv.Value;
            }

            int[] RoleTargets(string role, EventOpKind kind)
            {
                if (!_roles.TryGetValue(role, out var r)) throw new ContentException($"role '{role}' not bound in map");
                var list = new List<int>();
                switch (kind)
                {
                    case EventOpKind.SetSourceRate:
                        foreach (var s in r["sources"].Elements()) list.Add(_sourceIndex[s.AsString()]);
                        break;
                    case EventOpKind.SetEdgeEnabled:
                    case EventOpKind.SetCurrentMultiplier:
                    case EventOpKind.WarnRoute:
                        foreach (var e in r["edges"].Elements()) list.Add(EdgeRef(e.AsString()));
                        break;
                    case EventOpKind.ReleaseGroup:
                        foreach (var g in r["groups"].Elements()) list.Add(d.GroupIndex[g.AsString()]);
                        break;
                    case EventOpKind.SetInfrastructureDemand:
                    case EventOpKind.WarnSupport:
                        foreach (var g in r["infrastructure"].Elements()) list.Add(d.InfraIndex[g.AsString()]);
                        break;
                    default:
                        if (r["regions"] != null) foreach (var g in r["regions"].Elements()) list.Add(d.RegionIndex[g.AsString()]);
                        break;
                }
                if (list.Count == 0) throw new ContentException($"role '{role}' resolves to nothing for {kind}");
                return list.ToArray();
            }

            public void CompileEvents(JsonNode contract)
            {
                var evs = contract["events"].Elements().OrderBy(e => e.Long("at_scenario_tick")).ThenBy(e => e.Str("id"), StringComparer.Ordinal).ToList();
                var result = new List<EventDef>();
                foreach (var e in evs)
                {
                    long at = e.Long("at_scenario_tick");
                    long warn = e.Long("warning_at_scenario_tick");
                    if (at < 1 || at >= d.DurationTicks) throw new ContentException($"event {e.Str("id")} effect tick out of range");
                    if (warn < 0 || warn > at) throw new ContentException($"event {e.Str("id")} warning tick invalid");
                    var ops = new List<EventOp>();
                    foreach (var op in e["operations"].Elements())
                    {
                        var kind = ParseOp(op.Str("kind"));
                        ops.Add(new EventOp { Kind = kind, Targets = RoleTargets(op.Str("target_role"), kind), Value = op.Long("value") });
                    }
                    result.Add(new EventDef { Id = e.Str("id"), AtTick = at, WarningTick = warn, Ops = ops.ToArray(),
                                              WarningKey = $"{d.MissionId}.{e.Str("id")}" });
                }
                d.Events = result.ToArray();
                d.WarningOrder = result.OrderBy(x => x.WarningTick).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
            }

            static EventOpKind ParseOp(string s)
            {
                var parts = s.Split('_');
                string pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
                return (EventOpKind)Enum.Parse(typeof(EventOpKind), pascal);
            }

            public void CompileObjectives(JsonNode contract)
            {
                var ms = contract["milestones"]?.Elements().OrderBy(x => x.Str("id"), StringComparer.Ordinal).ToList() ?? new List<JsonNode>();
                d.Milestones = new MilestoneDef[ms.Count];
                for (int i = 0; i < ms.Count; i++)
                {
                    var x = ms[i];
                    var kind = x.Str("kind");
                    var md = new MilestoneDef { Id = x.Str("id") };
                    switch (kind)
                    {
                        case "group_boarded": md.Kind = MilestoneKind.GroupBoarded; md.Target = d.GroupIndex[x.Str("target")]; break;
                        case "final_unload_started": md.Kind = MilestoneKind.FinalUnloadStarted; break;
                        case "all_evacuated": md.Kind = MilestoneKind.AllEvacuated; break;
                        case "first_cooled_crossing_closed": md.Kind = MilestoneKind.FirstCooledCrossingClosed; break;
                        default: throw new ContentException($"unknown milestone kind {kind}");
                    }
                    d.Milestones[i] = md;
                    _milestoneIndex[md.Id] = i;
                }
                var objs = contract["objectives"].Elements().ToList();
                d.Objectives = new ObjectiveDef[objs.Count];
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    var t = o["temporal"];
                    var od = new ObjectiveDef
                    {
                        Id = o.Str("id"),
                        Tier = ParseTier(o.Str("tier")),
                        Condition = ParseCondition(o.Str("kind"), o["params"]),
                    };
                    switch (t.Str("kind"))
                    {
                        case "at_end": od.Temporal = TemporalKind.AtEnd; break;
                        case "ever_by": od.Temporal = TemporalKind.EverBy; od.DeadlineTick = t.Long("deadline_tick"); break;
                        case "always": od.Temporal = TemporalKind.Always; od.StartMilestone = Milestone(t.Str("start_event")); od.EndMilestone = Milestone(t.Str("end_event")); break;
                        case "ever_before": od.Temporal = TemporalKind.EverBefore; od.EndMilestone = Milestone(t.Str("end_event")); break;
                        case "hold_for": od.Temporal = TemporalKind.HoldFor; od.HoldTicks = t.Long("scenario_ticks"); od.StartMilestone = Milestone(t.Str("start_event")); od.EndMilestone = Milestone(t.Str("end_event")); break;
                        default: throw new ContentException($"unknown temporal {t.Str("kind")}");
                    }
                    d.Objectives[i] = od;
                }
                var prot = new List<int>();
                if (contract["protection_objective_ids"] != null)
                    foreach (var p in contract["protection_objective_ids"].Elements())
                        prot.Add(Array.FindIndex(d.Objectives, x => x.Id == p.AsString()));
                d.ProtectionObjectives = prot.ToArray();
            }

            int Milestone(string id)
            {
                if (id == "operation_started") return Simulator.MilestoneOperationStarted;
                if (id == "operation_ended") return Simulator.MilestoneOperationEnded;
                if (!_milestoneIndex.TryGetValue(id, out int i)) throw new ContentException($"unknown milestone '{id}'");
                return i;
            }

            static Tier ParseTier(string s) =>
                s == "required" ? Tier.Required : s == "critical" ? Tier.Critical : s == "mastery" ? Tier.Mastery : Tier.Optional;

            Condition ParseCondition(string kind, JsonNode p)
            {
                var c = new Condition();
                switch (kind)
                {
                    case "rescue_count": c.Kind = ConditionKind.RescueCount; c.Value = p.Long("minimum"); break;
                    case "group_evacuated": c.Kind = ConditionKind.GroupEvacuated; c.A = d.GroupIndex[p.Str("group_id")]; c.Value = p.Long("minimum"); break;
                    case "infrastructure_operational": c.Kind = ConditionKind.InfrastructureOperational; c.A = InfraRef(p.Str("infrastructure_id")); c.Flag = p.Flag("operational"); break;
                    case "route_available": c.Kind = ConditionKind.RouteAvailable; c.A = Array.IndexOf(d.RouteIds, p.Str("route_id")); if (c.A < 0) throw new ContentException($"unknown route {p.Str("route_id")}"); break;
                    case "exposure_below":
                        c.Kind = ConditionKind.ExposureBelow; c.A = d.RegionIndex.TryGetValue(p.Str("region_id"), out int ri) ? ri : throw new ContentException($"unknown region {p.Str("region_id")}");
                        c.Hazard = Array.IndexOf(new[] { "water", "fire", "lava", "runout" }, p.Str("hazard")); c.Value = p.Long("threshold"); break;
                    case "equipment_losses_at_most": c.Kind = ConditionKind.EquipmentLossesAtMost; c.Value = p.Long("maximum"); break;
                    case "supply_remaining_at_least": c.Kind = ConditionKind.SupplyRemainingAtLeast; c.Value = p.Long("minimum"); break;
                    case "resource_remaining": c.Kind = ConditionKind.ResourceRemaining; c.Value = p.Long("minimum"); break;
                    case "all_vehicles_unloaded": c.Kind = ConditionKind.AllVehiclesUnloaded; break;
                    case "temporary_shelters_empty": c.Kind = ConditionKind.TemporaryShelterEmpty; break;
                    case "temporary_staging_empty": c.Kind = ConditionKind.TemporaryStagingEmpty; break;
                    case "scenario_elapsed_at_most": c.Kind = ConditionKind.ScenarioElapsedAtMost; c.Value = p.Long("maximum"); break;
                    case "counter_at_least":
                    case "counter_at_most":
                        c.Kind = kind == "counter_at_least" ? ConditionKind.CounterAtLeast : ConditionKind.CounterAtMost;
                        c.Value = p.Has("minimum") ? p.Long("minimum") : p.Long("maximum");
                        ParseMetric(c, p);
                        break;
                    case "all":
                    case "any":
                        c.Kind = kind == "all" ? ConditionKind.All : ConditionKind.Any;
                        c.Children = p["conditions"].Elements().Select(x => ParseCondition(x.Str("kind"), x["params"])).ToArray();
                        break;
                    default: throw new ContentException($"unknown condition '{kind}'");
                }
                return c;
            }

            void ParseMetric(Condition c, JsonNode p)
            {
                switch (p.Str("metric"))
                {
                    case "evacuated_by_mode":
                        c.Metric = MetricKind.EvacuatedByMode;
                        c.MetricMode = Array.IndexOf(new[] { "ground", "water", "cable" }, p.Str("mode"));
                        c.MetricTarget = p.Str("group_id") != null ? d.GroupIndex[p.Str("group_id")] : -1;
                        break;
                    case "equipment_deployments": c.Metric = MetricKind.EquipmentDeployments; c.MetricMode = (int)ParseTool(p.Str("equipment_id")); break;
                    case "peak_node_occupancy": c.Metric = MetricKind.PeakNodeOccupancy; c.MetricTarget = NodeRef(p.Str("node_id")); break;
                    case "loaded_closed_dock_wait_operation_ticks": c.Metric = MetricKind.LoadedClosedDockWait; break;
                    case "pumped_into_region_mm3": c.Metric = MetricKind.PumpedIntoRegion; c.MetricTarget = d.RegionIndex[p.Str("region_id")]; break;
                    case "generator_shutdowns": c.Metric = MetricKind.GeneratorShutdowns; break;
                    case "station_shutdowns": c.Metric = MetricKind.StationShutdowns; break;
                    case "shelter_exhaustions": c.Metric = MetricKind.ShelterExhaustions; break;
                    default: c.Metric = MetricKind.None; break; // world-specific metrics resolved by modules
                }
            }

            int InfraRef(string id)
            {
                if (!d.InfraIndex.TryGetValue(id, out int i)) throw new ContentException($"unknown infrastructure '{id}'");
                return i;
            }

            public void Validate()
            {
                for (int g = 0; g < d.GroupIds.Length; g++)
                    if (d.GroupResidents[g].Length == 0) throw new ContentException($"group {d.GroupIds[g]} empty");
                int refuge = 0;
                for (int n = 0; n < d.NodeIds.Length; n++)
                    if ((d.NodeFlags[n] & NodeFlags.Evacuation) != 0) refuge += Math.Max(0, d.NodeCapacity[n]);
                if (refuge != 0 && refuge < d.TotalResidents)
                    throw new ContentException($"permanent refuge capacity {refuge} < population {d.TotalResidents}");
            }
        }
    }
}
