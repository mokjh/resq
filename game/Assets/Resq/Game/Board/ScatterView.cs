using System.Collections.Generic;
using Resq.Sim;
using Resq.Sim.Rules;
using UnityEngine;

namespace Resq.Game
{
    public enum DecorKind : byte { Broadleaf, Conifer, Shrub, Rock, Grass, Flower, Reed, Crop, Orchard, Drift, Column, Prop }

    /// <summary>
    /// World dressing (Look 2.0): trees, rocks, ground cover and small props scattered deterministically over every
    /// free cell, by biome rules per world, drawn with GPU instancing (a few draw calls for hundreds of items).
    /// Presentation only — never on paths, nodes, sockets, water, lava, runout or cottages, so it can't hide anything
    /// the player must read. Trees and shrubs on cells the fire has burned switch to a charred variant.
    /// Meshes come from <see cref="NatureKit"/> when the Blender kit is imported, otherwise from built-in placeholders.
    /// </summary>
    public sealed class ScatterView : MonoBehaviour
    {
        public SimDriver Driver;
        public Material Material;
        public ActorsView Actors;

        sealed class Batch
        {
            public Mesh Mesh, Charred;
            public bool Shadows, Burnable;
            public readonly List<Matrix4x4> Local = new List<Matrix4x4>();
            public readonly List<int> Cells = new List<int>();
            public Matrix4x4[] World = new Matrix4x4[0], WorldCharred = new Matrix4x4[0];
            public int Live, Burnt;
        }

        MissionDef _def;
        readonly List<Batch> _batches = new List<Batch>();
        readonly Dictionary<Mesh, Batch> _byMesh = new Dictionary<Mesh, Batch>();
        Matrix4x4 _lastBoard;
        int _lastFireHash = int.MinValue;
        float _scale = 1f;
        Bounds _bounds;
        public int InstanceCount { get; private set; }
        public int BatchCount => _batches.Count;

        // ------------------------------------------------------------------ build
        public void Build(MissionDef d)
        {
            _def = d;
            _batches.Clear(); _byMesh.Clear();
            InstanceCount = 0;
            _scale = BoardSpace.CellSize(d) / BoardSpace.WideCell;
            var palette = DecorPalette.For(d.WorldId);
            var blocked = BlockedCells(d, out var nearNode);
            int n = d.GridW * d.GridH;
            float cs = BoardSpace.CellSize(d);
            for (int c = 0; c < n; c++)
            {
                if (blocked[c]) continue;
                var recipe = Biome.For(d, c);
                if (recipe == null) continue;
                int item = 0;
                foreach (var (kind, density) in recipe)
                {
                    if (nearNode[c] && (kind == DecorKind.Broadleaf || kind == DecorKind.Conifer || kind == DecorKind.Orchard || kind == DecorKind.Column))
                        continue; // keep sight lines to waiting groups
                    // expected count = density: whole part always, fraction by hash
                    int count = Mathf.FloorToInt(density);
                    if (Rand(c, item * 31 + 7) < density - count) count++;
                    for (int k = 0; k < count; k++, item++)
                    {
                        var variants = palette.Get(kind);
                        if (variants == null || variants.Length == 0) continue;
                        var mesh = variants[(int)(Rand(c, item * 13 + 1) * variants.Length) % variants.Length];
                        bool tree = kind == DecorKind.Broadleaf || kind == DecorKind.Conifer || kind == DecorKind.Orchard;
                        // trees near the cell centre (one per cell reads as a grove), ground cover anywhere
                        float spread = tree ? 0.22f : 0.42f;
                        float ox = (Rand(c, item * 17 + 3) * 2 - 1) * spread, oz = (Rand(c, item * 19 + 5) * 2 - 1) * spread;
                        if (kind == DecorKind.Orchard) { ox *= 0.3f; oz *= 0.3f; } // orchards stand in rows
                        var p = BoardSpace.CellCenter(d, c) + new Vector3(ox * cs, 0, oz * cs);
                        // follow the natural terrain; a hair lower on slopes so bases never float
                        p.y = TerrainBuilder.HeightAt(d, p) - 0.0006f * _scale;
                        float yaw = Rand(c, item * 23 + 11) * 360f;
                        float size = Mathf.Lerp(0.82f, 1.18f, Rand(c, item * 29 + 13)) * _scale;
                        Add(mesh, palette.Charred(kind), Matrix4x4.TRS(p, Quaternion.Euler(0, yaw, 0), Vector3.one * size),
                            c, kind != DecorKind.Grass && kind != DecorKind.Flower && kind != DecorKind.Drift,
                            d.FireEnabled && (tree || kind == DecorKind.Shrub || kind == DecorKind.Grass || kind == DecorKind.Reed));
                    }
                }
            }
            var ext = BoardSpace.Extent(d);
            _bounds = new Bounds(Vector3.zero, new Vector3(ext.x + 1f, 1f, ext.y + 1f));
            _lastBoard = Matrix4x4.zero;
            _lastFireHash = int.MinValue;
        }

        void Add(Mesh mesh, Mesh charred, Matrix4x4 local, int cell, bool shadows, bool burnable)
        {
            if (!_byMesh.TryGetValue(mesh, out var b))
            {
                b = new Batch { Mesh = mesh, Charred = charred, Shadows = shadows, Burnable = burnable && charred != null };
                _byMesh[mesh] = b;
                _batches.Add(b);
            }
            b.Local.Add(local);
            b.Cells.Add(cell);
            InstanceCount++;
        }

        static float Rand(int cell, int salt)
        {
            uint h = unchecked((uint)(cell * 2654435761u) ^ (uint)(salt * 2246822519u));
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Cells that must stay readable, plus a one-cell ring around nodes for tall items.</summary>
        static bool[] BlockedCells(MissionDef d, out bool[] nearNode)
        {
            int w = d.GridW, h = d.GridH, n = w * h;
            var b = new bool[n];
            nearNode = new bool[n];
            for (int i = 0; i < d.NodeIds.Length; i++)
            {
                int nc = d.NodeCell[i];
                if (nc < 0) continue;
                b[nc] = true;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = d.CellX(nc) + ox, y = d.CellY(nc) + oy;
                        if (x >= 0 && y >= 0 && x < w && y < h) nearNode[d.CellIndex(x, y)] = true;
                    }
            }
            for (int e = 0; e < d.EdgeIds.Length; e++)
                if (d.EdgeSampleCells[e] != null && d.EdgeClass[e] == Traversal.Ground) foreach (int c in d.EdgeSampleCells[e]) if (c < n) b[c] = true;
            for (int s = 0; s < d.SocketIds.Length; s++)
            {
                if (d.SocketCell[s] >= 0) b[d.SocketCell[s]] = true;
                if (d.SocketCells[s] != null) foreach (int c in d.SocketCells[s]) b[c] = true;
                if (d.SocketPlatformCells[s] != null) foreach (int c in d.SocketPlatformCells[s]) b[c] = true;
            }
            for (int c = 0; c < n; c++)
            {
                if (d.WInitVol[c] > 0) b[c] = true;
                if (d.CellLavaSeg != null && d.CellLavaSeg[c] >= 0) b[c] = true;
            }
            foreach (int c in d.SeaCell) b[c] = true;
            foreach (var cells in d.SegmentCells) foreach (int c in cells) if (c < n) b[c] = true;
            for (int i = 0; i < d.SourceNode.Length; i++) b[d.SourceNode[i]] = true;
            return b;
        }

        // ------------------------------------------------------------------ draw
        void LateUpdate()
        {
            if (Driver == null || Driver.Sim == null || Driver.Def != _def || _batches.Count == 0) return;
            if (Actors != null && Actors.CottageCells.Count > 0 && !_cottagesApplied) ApplyCottageMask();
            var board = transform.localToWorldMatrix;
            int fireHash = _def.FireEnabled ? FireHash() : 0;
            if (board != _lastBoard || fireHash != _lastFireHash)
            {
                _lastBoard = board; _lastFireHash = fireHash;
                foreach (var b in _batches) Compose(b, board);
            }
            var bounds = new Bounds(board.MultiplyPoint3x4(Vector3.zero), _bounds.size);
            foreach (var b in _batches)
            {
                var rp = new RenderParams(Material)
                {
                    shadowCastingMode = b.Shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    worldBounds = bounds,
                };
                if (b.Live > 0) Graphics.RenderMeshInstanced(rp, b.Mesh, 0, b.World, b.Live);
                if (b.Burnt > 0 && b.Charred != null) Graphics.RenderMeshInstanced(rp, b.Charred, 0, b.WorldCharred, b.Burnt);
            }
        }

        bool _cottagesApplied;

        /// <summary>Cottages are placed by ActorsView after this view builds; drop any dressing under their footprints.</summary>
        void ApplyCottageMask()
        {
            _cottagesApplied = true;
            foreach (var b in _batches)
                for (int i = b.Cells.Count - 1; i >= 0; i--)
                    if (Actors.CottageCells.Contains(b.Cells[i])) { b.Cells.RemoveAt(i); b.Local.RemoveAt(i); InstanceCount--; }
            _lastBoard = Matrix4x4.zero;
        }

        void Compose(Batch b, Matrix4x4 board)
        {
            if (b.World.Length < b.Local.Count) { b.World = new Matrix4x4[b.Local.Count]; b.WorldCharred = new Matrix4x4[b.Local.Count]; }
            b.Live = 0; b.Burnt = 0;
            var sim = Driver.Sim;
            for (int i = 0; i < b.Local.Count; i++)
            {
                var m = board * b.Local[i];
                bool burnt = b.Burnable && Burnt(sim, b.Cells[i]);
                if (burnt) b.WorldCharred[b.Burnt++] = m; else b.World[b.Live++] = m;
            }
        }

        static bool Burnt(Simulator sim, int cell)
        {
            var st = sim.CellFire(cell);
            return st == FireState.Burning || st == FireState.Cooling || st == FireState.SafeAsh;
        }

        int FireHash()
        {
            var S = Driver.Sim.S;
            int h = 17;
            for (int c = 0; c < S.FState.Length; c++) if (S.FState[c] >= (byte)FireState.Burning) h = h * 31 + c * 7 + S.FState[c];
            return h;
        }
    }

    /// <summary>Biome recipes: (kind, expected items per cell) by world and terrain code.</summary>
    public static class Biome
    {
        static readonly (DecorKind, float)[] Woods = { (DecorKind.Broadleaf, 0.45f), (DecorKind.Conifer, 0.3f), (DecorKind.Shrub, 0.3f), (DecorKind.Rock, 0.12f), (DecorKind.Grass, 0.9f) };
        static readonly (DecorKind, float)[] Meadow = { (DecorKind.Grass, 1.7f), (DecorKind.Flower, 0.4f), (DecorKind.Shrub, 0.08f), (DecorKind.Rock, 0.05f) };
        static readonly (DecorKind, float)[] Field = { (DecorKind.Crop, 1.0f), (DecorKind.Grass, 0.3f) };
        static readonly (DecorKind, float)[] OrchardRows = { (DecorKind.Orchard, 0.9f), (DecorKind.Grass, 0.7f) };
        static readonly (DecorKind, float)[] Village = { (DecorKind.Shrub, 0.25f), (DecorKind.Flower, 0.3f), (DecorKind.Grass, 0.4f), (DecorKind.Prop, 0.12f) };
        static readonly (DecorKind, float)[] Basin = { (DecorKind.Grass, 0.9f), (DecorKind.Reed, 0.35f) };
        static readonly (DecorKind, float)[] PineForest = { (DecorKind.Conifer, 0.75f), (DecorKind.Broadleaf, 0.15f), (DecorKind.Shrub, 0.35f), (DecorKind.Grass, 0.6f) };
        static readonly (DecorKind, float)[] Hedge = { (DecorKind.Shrub, 1.1f), (DecorKind.Grass, 0.5f) };
        static readonly (DecorKind, float)[] DryMeadow = { (DecorKind.Grass, 1.5f), (DecorKind.Shrub, 0.12f), (DecorKind.Rock, 0.06f) };
        static readonly (DecorKind, float)[] Rocky = { (DecorKind.Rock, 0.55f), (DecorKind.Grass, 0.35f), (DecorKind.Conifer, 0.15f) };
        static readonly (DecorKind, float)[] Camp = { (DecorKind.Prop, 0.2f), (DecorKind.Grass, 0.5f), (DecorKind.Shrub, 0.1f) };
        static readonly (DecorKind, float)[] SnowRock = { (DecorKind.Rock, 0.5f), (DecorKind.Conifer, 0.35f), (DecorKind.Drift, 0.3f) };
        static readonly (DecorKind, float)[] SnowField = { (DecorKind.Conifer, 0.45f), (DecorKind.Drift, 0.35f), (DecorKind.Rock, 0.08f) };
        static readonly (DecorKind, float)[] SnowSlope = { (DecorKind.Drift, 0.3f) };
        static readonly (DecorKind, float)[] CoastWoods = { (DecorKind.Conifer, 0.4f), (DecorKind.Grass, 1.0f), (DecorKind.Rock, 0.2f), (DecorKind.Shrub, 0.15f) };
        static readonly (DecorKind, float)[] RockyCoast = { (DecorKind.Rock, 0.5f), (DecorKind.Grass, 0.2f) };
        static readonly (DecorKind, float)[] Beach = { (DecorKind.Grass, 0.35f), (DecorKind.Rock, 0.08f) };
        static readonly (DecorKind, float)[] Harbour = { (DecorKind.Prop, 0.18f), (DecorKind.Shrub, 0.15f), (DecorKind.Grass, 0.3f) };
        static readonly (DecorKind, float)[] Basalt = { (DecorKind.Rock, 0.45f), (DecorKind.Column, 0.22f), (DecorKind.Grass, 0.4f) };
        static readonly (DecorKind, float)[] ToughMeadow = { (DecorKind.Grass, 1.1f), (DecorKind.Shrub, 0.22f), (DecorKind.Rock, 0.08f) };
        static readonly (DecorKind, float)[] Ash = { (DecorKind.Drift, 0.45f), (DecorKind.Rock, 0.25f), (DecorKind.Grass, 0.15f) };

        public static (DecorKind, float)[] For(MissionDef d, int cell)
        {
            byte code = d.CellTerrain[cell];
            switch (d.WorldId)
            {
                case "W2":
                    switch (code)
                    {
                        case 14: return PineForest;
                        case 15: return Hedge;
                        case 16: return PineForest;
                        case 13: return DryMeadow;
                        case 3: case 18: return Rocky;
                        case 10: return OrchardRows;
                        case 11: case 17: return Camp;
                        default: return null;
                    }
                case "W3":
                    switch (code)
                    {
                        case 3: case 29: case 22: return SnowRock;
                        case 27: case 28: return SnowField;
                        case 21: return SnowSlope;
                        default: return null;
                    }
                case "W4":
                    switch (code)
                    {
                        case 3: return CoastWoods;
                        case 7: return RockyCoast;
                        case 6: return Beach;
                        case 11: return Harbour;
                        default: return null;
                    }
                case "W5":
                    switch (code)
                    {
                        case 3: return Basalt;
                        case 1: return ToughMeadow;
                        case 10: return OrchardRows;
                        case 6: return Ash;
                        case 11: return Village;
                        default: return null;
                    }
                default:
                    switch (code)
                    {
                        case 3: return Woods;
                        case 1: case 7: return Meadow;
                        case 5: return Field;
                        case 10: return OrchardRows;
                        case 11: return Village;
                        case 6: return Basin;
                        default: return null;
                    }
            }
        }
    }
}
