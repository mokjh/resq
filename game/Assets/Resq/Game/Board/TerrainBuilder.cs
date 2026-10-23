using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Builds the diorama terrain mesh from a MissionDef (presentation only; the simulation never reads it).
    /// Look 2.0: each cell top is a 3×3 patch with smooth world-space colour variation and soft blends between
    /// natural ground types (roads, platforms and docks keep crisp edges), a little interior relief, chamfered
    /// cliff tops, and stratified rock faces whose layers line up across the whole board. Cell borders stay at the
    /// exact simulated elevation so water, nodes and equipment sit where the rules put them.
    /// </summary>
    public static class TerrainBuilder
    {
        // World 1 palette (art bible): foliage, water, roof, stone, rescue.
        static readonly Color Foliage = Hex(0x355D47), FoliageLight = Hex(0x4C7A5A), Stone = Hex(0xD8CCAF),
            Earth = Hex(0x7A5A3C), EarthDark = Hex(0x5A4130), Rock = Hex(0x8C8577), Bed = Hex(0x3E4F4A),
            Terrace = Hex(0x6E9160), Depot = Hex(0xC9B98F), Basin = Hex(0x5C6B4E);

        const int Sub = 3;                 // top patch subdivisions per cell side
        const float ReliefMm = 0.55f;      // interior relief amplitude (board metres = mm * 0.001)
        const float ChamferMm = 1.2f;      // cliff-top chamfer drop
        const float StrataMm = 3.2f;       // rock layer thickness on cliff faces

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        // ------------------------------------------------------------------ palettes
        // Worlds 2–5 top colours by terrain code (codes come from each world's map kit legend).
        static readonly Dictionary<int, int> Emberwood = new Dictionary<int, int>
        {
            { 0, 0x3E5A60 }, { 2, 0xC9A46A }, { 3, 0x8A8173 }, { 8, 0xD2C29A }, { 9, 0xD2C29A }, { 10, 0x6F8A3E }, { 11, 0xB79B6B },
            { 13, 0x9AA857 }, { 14, 0x3F5B32 }, { 15, 0x4E6A36 }, { 16, 0x8C7A3A }, { 17, 0xA8875C }, { 18, 0x9B8C6A },
            { 19, 0x7A5A3C }, { 20, 0x6E8E9A }, { 22, 0x8C8577 }, { 23, 0x9B6B4A },
        };
        static readonly Dictionary<int, int> Frostline = new Dictionary<int, int>
        {
            { 0, 0x4A6A7A }, { 2, 0x9AA3AA }, { 3, 0x6E747A }, { 6, 0x5C7A8A }, { 8, 0xD8D2C4 }, { 9, 0xD8D2C4 }, { 11, 0xB8B0A4 },
            { 21, 0xF1F4F7 }, { 22, 0x8E959C }, { 23, 0xA88A6A }, { 24, 0xC9CCD0 }, { 25, 0xD9DEE3 }, { 26, 0xE6EBF0 },
            { 27, 0xE9EEF2 }, { 28, 0xDCE3E8 }, { 29, 0x7D848B },
        };
        static readonly Dictionary<int, int> Tidemark = new Dictionary<int, int>
        {
            { 0, 0x2F5E6E }, { 2, 0xC8B790 }, { 3, 0x6E8F5A }, { 6, 0xB9A57E }, { 7, 0x9A9283 }, { 8, 0xD9CBA6 }, { 9, 0xD9CBA6 },
            { 11, 0xD6C7AE }, { 12, 0x8C8577 },
        };
        static readonly Dictionary<int, int> CinderReach = new Dictionary<int, int>
        {
            { 1, 0x6E7A4E }, { 2, 0x8E8579 }, { 3, 0x3B3836 }, { 4, 0x2E4E58 }, { 6, 0x4F4A44 }, { 8, 0xC2B596 }, { 9, 0xC2B596 },
            { 10, 0x5E7A3E }, { 11, 0xB8A88A }, { 12, 0x5A5048 }, { 13, 0x2A2624 }, { 14, 0x9AA3AA },
        };

        /// <summary>Cliff strata per world, top to bottom of a repeating cycle.</summary>
        static Color[] Strata(string world)
        {
            switch (world)
            {
                case "W2": return new[] { Hex(0x8A5A3A), Hex(0x6E4630), Hex(0x9C7350), Hex(0x5E3E2C) };
                case "W3": return new[] { Hex(0x8C949C), Hex(0x6E767E), Hex(0x9AA2A8), Hex(0x5E666E) };
                case "W4": return new[] { Hex(0xC2A878), Hex(0xA88C62), Hex(0xD0B98C), Hex(0x8E7556) };
                case "W5": return new[] { Hex(0x3A3532), Hex(0x2A2624), Hex(0x5A3A2E), Hex(0x463F3A) };
                default: return new[] { Hex(0x8C8577), Hex(0x7A6A58), Hex(0x9A8E7C), Hex(0x6A5A48) };
            }
        }

        static Color Lip(string world) => world == "W3" ? Hex(0xF4F7FA) : world == "W5" ? Hex(0x5A524C) : world == "W4" ? Hex(0xD8C49A) : Hex(0x7E9A62);

        static Dictionary<int, int> WorldPalette(string worldId)
        {
            switch (worldId)
            {
                case "W2": return Emberwood;
                case "W3": return Frostline;
                case "W4": return Tidemark;
                case "W5": return CinderReach;
                default: return null;
            }
        }

        /// <summary>Codes that keep crisp edges (built surfaces): roads, docks, depots, landings, platforms, buildings, water beds.</summary>
        static bool Hard(MissionDef d, byte code)
        {
            switch (code)
            {
                case 0: case 2: case 4: case 8: case 9: case 12: return true;
                case 23: case 24: case 26: return d.WorldId == "W3" || d.WorldId == "W2";
                case 14: case 20: case 22: return d.WorldId == "W5" || (d.WorldId == "W2" && code != 14);
                default: return false;
            }
        }

        static Color BaseColor(MissionDef d, byte code)
        {
            var pal = WorldPalette(d.WorldId);
            if (pal != null && pal.TryGetValue(code, out int rgb)) return Hex(rgb);
            switch (code)
            {
                case 0: case 4: case 12: return Bed;
                case 1: case 7: return Terrace;
                case 2: return Stone;
                case 3: return Foliage;
                case 5: return FoliageLight;
                case 6: return Basin;
                case 8: case 9: return Depot;
                case 10: return Hex(0x5E8A4E);
                case 11: return Hex(0xB9A98A);
                default: return Foliage;
            }
        }

        public static Color TopColor(MissionDef d, byte terrainCode, int cell) => BaseColor(d, terrainCode);
        public static Color TopColor(byte terrainCode, int cell) => BaseColor(null_def, terrainCode);
        static readonly MissionDef null_def = new MissionDef();

        // ------------------------------------------------------------------ noise (deterministic, presentation only)
        static float Hash(int x, int y, int seed)
        {
            uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 1442695040));
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        static float Value(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), e = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, e, fx), fy);
        }

        /// <summary>Two-octave noise in cell units, centred on 0.</summary>
        static float Fbm(float x, float y, int seed) => (Value(x * 0.22f, y * 0.22f, seed) - 0.5f) * 0.7f + (Value(x * 0.9f, y * 0.9f, seed + 7) - 0.5f) * 0.3f;

        static Color Vary(Color c, float gx, float gy, bool natural)
        {
            float v = Fbm(gx, gy, 11) * (natural ? 0.16f : 0.07f);
            float hueShift = natural ? Fbm(gx + 31, gy - 17, 23) * 0.08f : 0f;
            return new Color(Mathf.Clamp01(c.r + v + hueShift * 0.5f), Mathf.Clamp01(c.g + v + hueShift), Mathf.Clamp01(c.b + v - hueShift * 0.6f), 1f);
        }

        // ------------------------------------------------------------------ build (Look 3: natural heightfield)
        const int HfSub = 4;          // heightfield samples per cell side
        const float Sigma = 0.26f;    // Gaussian blend width (cells): plateaus at centres, steep rounded cliffs at borders
        const float Warp = 0.14f;     // domain warp (cells) for irregular cliff lines

        /// <summary>
        /// Natural terrain (art direction 23 Oct): one continuous heightfield whose cell centres sit exactly at the
        /// simulated elevation while borders between levels become steep, rounded, irregular rock faces. Colour =
        /// ground type (soft-blended) on flats, layered rock on slopes, darker in hollows (AO in vertex alpha for
        /// Resq/Terrain). The board edge keeps the cutaway geology walls.
        /// </summary>
        public static Mesh Build(MissionDef d)
        {
            int W = d.GridW * HfSub, H = d.GridH * HfSub;
            int vw = W + 1, vh = H + 1;
            float s = BoardSpace.CellSize(d);
            float metresPerMm = s / 20f;
            var heights = new float[vw * vh];
            var cellY = new float[d.GridW * d.GridH];
            for (int c = 0; c < cellY.Length; c++) cellY[c] = BoardSpace.CellCenter(d, c).y;
            for (int j = 0; j < vh; j++)
                for (int i = 0; i < vw; i++)
                {
                    float gx = i / (float)HfSub, gy = j / (float)HfSub;
                    heights[j * vw + i] = Sample(d, cellY, gx, gy);
                }
            var verts = new List<Vector3>(vw * vh + 4000);
            var cols = new List<Color>(vw * vh + 4000);
            var norms = new List<Vector3>(vw * vh + 4000);
            var tris = new List<int>(W * H * 6 + 8000);
            var strata = Strata(d.WorldId);
            var lip = Lip(d.WorldId);
            float ox = -d.GridW * 0.5f * s, oz = -d.GridH * 0.5f * s;
            for (int j = 0; j < vh; j++)
                for (int i = 0; i < vw; i++)
                {
                    float gx = i / (float)HfSub, gy = j / (float)HfSub;
                    float h = heights[j * vw + i];
                    verts.Add(new Vector3(ox + gx * s, h, oz + gy * s));
                    // slope and concavity from neighbours
                    float hl = heights[j * vw + Mathf.Max(0, i - 1)], hr = heights[j * vw + Mathf.Min(vw - 1, i + 1)];
                    float hd = heights[Mathf.Max(0, j - 1) * vw + i], hu = heights[Mathf.Min(vh - 1, j + 1) * vw + i];
                    float step = s / HfSub;
                    float slope = new Vector2((hr - hl) / (2 * step), (hu - hd) / (2 * step)).magnitude;
                    float concave = (hl + hr + hd + hu - 4 * h) / (step * 4f);
                    int cx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, d.GridW - 1), cy = Mathf.Clamp(Mathf.FloorToInt(gy), 0, d.GridH - 1);
                    var baseCol = Vary(BlendedBase(d, cx, cy, gx - cx, gy - cy, cellY[d.CellIndex(cx, cy)]), gx, gy, !Hard(d, d.CellTerrain[d.CellIndex(cx, cy)]));
                    int band = Mathf.FloorToInt(h / (StrataMm * metresPerMm) + Fbm(gx * 3f, gy * 3f, 41) * 1.5f);
                    var rock = strata[((band % strata.Length) + strata.Length) % strata.Length];
                    float rj = Fbm(gx * 5f, gy * 5f, 43) * 0.12f;
                    rock = new Color(Mathf.Clamp01(rock.r + rj), Mathf.Clamp01(rock.g + rj), Mathf.Clamp01(rock.b + rj), 1f);
                    float rockMix = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.8f, slope));
                    var col = Color.Lerp(baseCol, rock, rockMix);
                    // grassy lip on cliff tops (convex), snow/sand/ash per world
                    if (concave < -0.15f && rockMix > 0.05f && rockMix < 0.7f) col = Color.Lerp(col, lip, 0.35f);
                    float ao = Mathf.Clamp01(1f - Mathf.Max(0f, concave) * 0.55f) * Mathf.Lerp(1f, 0.82f, rockMix);
                    col.a = ao;
                    cols.Add(col);
                    norms.Add(Vector3.up);
                }
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    int a = j * vw + i, b = a + 1, e = a + vw, f = e + 1;
                    if (((i + j) & 1) == 0) { tris.Add(a); tris.Add(e); tris.Add(f); tris.Add(a); tris.Add(f); tris.Add(b); }
                    else { tris.Add(a); tris.Add(e); tris.Add(b); tris.Add(b); tris.Add(e); tris.Add(f); }
                }
            int topVerts = verts.Count;
            // cutaway walls around the board edge, following the heightfield's top line
            float baseY = -BoardSpace.BaseDepth;
            void EdgeWall(int i0, int j0, int i1, int j1, Vector3 outward)
            {
                var p0 = verts[j0 * vw + i0]; var p1 = verts[j1 * vw + i1];
                float soil = Mathf.Min(0f, Mathf.Min(p0.y, p1.y));
                // rock from the surface down to elevation 0, then two soil bands to the base
                AddWallQuad(verts, cols, norms, tris, p0, p1, p0.y, p1.y, soil, soil, outward, strata, metresPerMm);
                var earth = Earth; earth.a = 0.85f; var earthDark = EarthDark; earthDark.a = 0.75f;
                float mid = Mathf.Lerp(soil, baseY, 0.45f);
                AddFlatQuad(verts, cols, norms, tris, p0, p1, soil, mid, outward, earth);
                AddFlatQuad(verts, cols, norms, tris, p0, p1, mid, baseY, outward, earthDark);
            }
            for (int i = 0; i < W; i++) { EdgeWall(i, 0, i + 1, 0, Vector3.back); EdgeWall(i, H, i + 1, H, Vector3.forward); }
            for (int j = 0; j < H; j++) { EdgeWall(0, j, 0, j + 1, Vector3.left); EdgeWall(W, j, W, j + 1, Vector3.right); }
            var ext = BoardSpace.Extent(d) * 0.5f;
            Quad(verts, cols, norms, tris,
                new Vector3(-ext.x, baseY, ext.y), new Vector3(-ext.x, baseY, -ext.y),
                new Vector3(ext.x, baseY, -ext.y), new Vector3(ext.x, baseY, ext.y), Vector3.down, EarthDark);
            var mesh = new Mesh { name = "Terrain_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            // smooth normals over the shared heightfield grid; walls keep their own flat normals
            var smooth = new Vector3[topVerts];
            for (int t = 0; t < W * H * 6; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                var fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (fn.y < 0) fn = -fn;
                smooth[a] += fn; smooth[b] += fn; smooth[c] += fn;
            }
            for (int i = 0; i < topVerts; i++) norms[i] = smooth[i].sqrMagnitude > 1e-20f ? smooth[i].normalized : Vector3.up;
            mesh.SetNormals(norms);
            mesh.RecalculateBounds();
            return mesh;
        }

        static MissionDef _hDef;
        static float[] _hCellY;

        /// <summary>Terrain surface height (board-local metres) under a board-local point, matching the built mesh.</summary>
        public static float HeightAt(MissionDef d, Vector3 local)
        {
            if (_hDef != d)
            {
                _hDef = d;
                _hCellY = new float[d.GridW * d.GridH];
                for (int c = 0; c < _hCellY.Length; c++) _hCellY[c] = BoardSpace.CellCenter(d, c).y;
            }
            float s = BoardSpace.CellSize(d);
            float gx = Mathf.Clamp(local.x / s + d.GridW * 0.5f, 0, d.GridW), gy = Mathf.Clamp(local.z / s + d.GridH * 0.5f, 0, d.GridH);
            return Sample(d, _hCellY, gx, gy);
        }

        /// <summary>Height at a grid-space point: Gaussian blend of nearby cell elevations with a light domain warp.</summary>
        static float Sample(MissionDef d, float[] cellY, float gx, float gy)
        {
            float wx = gx + Fbm(gx * 2.7f, gy * 2.7f, 61) * Warp * 2f;
            float wy = gy + Fbm(gx * 2.7f + 17f, gy * 2.7f - 9f, 67) * Warp * 2f;
            int cx = Mathf.FloorToInt(wx), cy = Mathf.FloorToInt(wy);
            float sum = 0, wsum = 0;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int x = Mathf.Clamp(cx + ox, 0, d.GridW - 1), y = Mathf.Clamp(cy + oy, 0, d.GridH - 1);
                    float dx = wx - (x + 0.5f), dy = wy - (y + 0.5f);
                    float w = Mathf.Exp(-(dx * dx + dy * dy) / (2f * Sigma * Sigma));
                    sum += w * cellY[d.CellIndex(x, y)]; wsum += w;
                }
            return wsum > 0 ? sum / wsum : cellY[d.CellIndex(Mathf.Clamp(cx, 0, d.GridW - 1), Mathf.Clamp(cy, 0, d.GridH - 1))];
        }

        static void AddWallQuad(List<Vector3> v, List<Color> c, List<Vector3> n, List<int> t, Vector3 p0, Vector3 p1,
                                float top0, float top1, float bot0, float bot1, Vector3 normal, Color[] strata, float metresPerMm)
        {
            if (top0 - bot0 < 1e-5f && top1 - bot1 < 1e-5f) return;
            var col = strata[0]; col.a = 0.9f;
            Quad(v, c, n, t, new Vector3(p0.x, bot0, p0.z), new Vector3(p0.x, top0, p0.z), new Vector3(p1.x, top1, p1.z), new Vector3(p1.x, bot1, p1.z), normal, col);
        }

        static void AddFlatQuad(List<Vector3> v, List<Color> c, List<Vector3> n, List<int> t, Vector3 p0, Vector3 p1, float top, float bottom, Vector3 normal, Color col)
        {
            if (top - bottom < 1e-5f) return;
            Quad(v, c, n, t, new Vector3(p0.x, bottom, p0.z), new Vector3(p0.x, top, p0.z), new Vector3(p1.x, top, p1.z), new Vector3(p1.x, bottom, p1.z), normal, col);
        }

        /// <summary>The wooden diorama tray around the board (concept frames): bevelled rim with a little grain.</summary>
        public static Mesh BuildFrame(MissionDef d)
        {
            var ext = BoardSpace.Extent(d) * 0.5f;
            float rim = 0.016f, top = 0.006f, bottom = -BoardSpace.BaseDepth - 0.008f, lip = 0.0025f;
            var kit = new MeshKit();
            var wood = Hex(0x7A5234); var woodLight = Hex(0x93653F); var woodDark = Hex(0x5E3E27);
            // four rails (long sides full length, short sides between them), plus a raised inner lip
            kit.Box(new Vector3(0, (top + bottom) / 2, -ext.y - rim / 2), new Vector3(ext.x * 2 + rim * 2, top - bottom, rim), wood);
            kit.Box(new Vector3(0, (top + bottom) / 2, ext.y + rim / 2), new Vector3(ext.x * 2 + rim * 2, top - bottom, rim), wood);
            kit.Box(new Vector3(-ext.x - rim / 2, (top + bottom) / 2, 0), new Vector3(rim, top - bottom, ext.y * 2), woodLight);
            kit.Box(new Vector3(ext.x + rim / 2, (top + bottom) / 2, 0), new Vector3(rim, top - bottom, ext.y * 2), woodLight);
            kit.Box(new Vector3(0, top + lip / 2, -ext.y - rim * 0.3f), new Vector3(ext.x * 2 + rim * 1.4f, lip, rim * 0.4f), woodDark);
            kit.Box(new Vector3(0, top + lip / 2, ext.y + rim * 0.3f), new Vector3(ext.x * 2 + rim * 1.4f, lip, rim * 0.4f), woodDark);
            kit.Box(new Vector3(-ext.x - rim * 0.3f, top + lip / 2, 0), new Vector3(rim * 0.4f, lip, ext.y * 2), woodDark);
            kit.Box(new Vector3(ext.x + rim * 0.3f, top + lip / 2, 0), new Vector3(rim * 0.4f, lip, ext.y * 2), woodDark);
            // grain strips on the long rails' tops
            for (int i = 0; i < 6; i++)
            {
                float z = -ext.y - rim + (i + 0.5f) * rim / 6f;
                kit.Box(new Vector3(0, top + 0.0002f, z), new Vector3(ext.x * 2 + rim * 2, 0.0003f, rim / 14f), i % 2 == 0 ? woodDark : woodLight);
                kit.Box(new Vector3(0, top + 0.0002f, -z), new Vector3(ext.x * 2 + rim * 2, 0.0003f, rim / 14f), i % 2 == 0 ? woodDark : woodLight);
            }
            return kit.Build("Frame");
        }

        public static Mesh BuildCells(MissionDef d)
        {
            var verts = new List<Vector3>(30000);
            var cols = new List<Color>(30000);
            var norms = new List<Vector3>(30000);
            var tris = new List<int>(60000);
            float s = BoardSpace.CellSize(d);
            float mmToBoard = BoardSpace.Scale(d) * BoardSpace.VerticalExaggeration; // vertical metres per sim mm
            float metresPerMm = s / 20f;                                              // presentation mm at this board scale
            float baseY = -BoardSpace.BaseDepth;
            var strata = Strata(d.WorldId);
            var lip = Lip(d.WorldId);
            int n = d.GridW * d.GridH;
            var grid = new Vector3[(Sub + 1) * (Sub + 1)];
            var gcol = new Color[(Sub + 1) * (Sub + 1)];

            for (int c = 0; c < n; c++)
            {
                var center = BoardSpace.CellCenter(d, c);
                float y = center.y;
                float h = s * 0.5f;
                int x = d.CellX(c), cy = d.CellY(c);
                byte code = d.CellTerrain[c];
                bool hard = Hard(d, code);
                // which sides drop to lower ground (chamfer them) — the board edge always drops
                bool dropW = Lower(d, x - 1, cy, y), dropE = Lower(d, x + 1, cy, y), dropS = Lower(d, x, cy - 1, y), dropN = Lower(d, x, cy + 1, y);
                float chamfer = ChamferMm * metresPerMm;

                for (int j = 0; j <= Sub; j++)
                    for (int i = 0; i <= Sub; i++)
                    {
                        float u = i / (float)Sub, v = j / (float)Sub;
                        float gx = x + u, gy = cy + v; // grid space (cells)
                        var p = new Vector3(center.x - h + u * s, y, center.z - h + v * s);
                        bool interior = i > 0 && i < Sub && j > 0 && j < Sub;
                        if (interior && !hard) p.y += Fbm(gx * 2.3f, gy * 2.3f, 5) * ReliefMm * metresPerMm * 2f;
                        bool lipVert = (i == 0 && dropW) || (i == Sub && dropE) || (j == 0 && dropS) || (j == Sub && dropN);
                        if (lipVert) p.y -= chamfer;
                        grid[j * (Sub + 1) + i] = p;
                        var col = Vary(BlendedBase(d, x, cy, u, v, y), gx, gy, !hard);
                        if (lipVert && !hard) col = Color.Lerp(col, lip, 0.35f);
                        if (code == 2 && (i == 0 || i == Sub || j == 0 || j == Sub) && RoadEdge(d, x, cy, i, j)) col *= 0.82f; // kerb shading
                        col.a = 1f;
                        gcol[j * (Sub + 1) + i] = col;
                    }
                int start = verts.Count;
                for (int k = 0; k < grid.Length; k++) { verts.Add(grid[k]); cols.Add(gcol[k]); norms.Add(Vector3.up); }
                for (int j = 0; j < Sub; j++)
                    for (int i = 0; i < Sub; i++)
                    {
                        int a = start + j * (Sub + 1) + i, b = a + 1, e = a + Sub + 1, f = e + 1;
                        // alternate the diagonal so relief doesn't shade in stripes
                        if (((i + j) & 1) == 0) { tris.Add(a); tris.Add(e); tris.Add(f); tris.Add(a); tris.Add(f); tris.Add(b); }
                        else { tris.Add(a); tris.Add(e); tris.Add(b); tris.Add(b); tris.Add(e); tris.Add(f); }
                    }
                // walls towards lower neighbours, and cutaway walls on the board edge
                float topW = dropW ? y - chamfer : y, topE = dropE ? y - chamfer : y, topS = dropS ? y - chamfer : y, topN = dropN ? y - chamfer : y;
                Wall(d, verts, cols, norms, tris, center, h, topW, x - 1, cy, new Vector3(-1, 0, 0), baseY, strata, metresPerMm, mmToBoard);
                Wall(d, verts, cols, norms, tris, center, h, topE, x + 1, cy, new Vector3(1, 0, 0), baseY, strata, metresPerMm, mmToBoard);
                Wall(d, verts, cols, norms, tris, center, h, topS, x, cy - 1, new Vector3(0, 0, -1), baseY, strata, metresPerMm, mmToBoard);
                Wall(d, verts, cols, norms, tris, center, h, topN, x, cy + 1, new Vector3(0, 0, 1), baseY, strata, metresPerMm, mmToBoard);
            }
            // bottom plate
            var ext = BoardSpace.Extent(d) * 0.5f;
            Quad(verts, cols, norms, tris,
                new Vector3(-ext.x, baseY, ext.y), new Vector3(-ext.x, baseY, -ext.y),
                new Vector3(ext.x, baseY, -ext.y), new Vector3(ext.x, baseY, ext.y), Vector3.down, EarthDark);
            var mesh = new Mesh { name = "Terrain_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            RecalculateTopNormals(mesh, verts, tris, n);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Face normals for the relief patches (walls keep their flat normals).</summary>
        static void RecalculateTopNormals(Mesh mesh, List<Vector3> verts, List<int> tris, int cells)
        {
            var normals = new List<Vector3>(verts.Count);
            mesh.GetNormals(normals);
            int topVerts = (Sub + 1) * (Sub + 1);
            var acc = new Vector3[verts.Count];
            var isTop = new bool[verts.Count];
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (normals[a] != Vector3.up || normals[b] != Vector3.up || normals[c] != Vector3.up) continue;
                var fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (fn.y < 0) fn = -fn;
                acc[a] += fn; acc[b] += fn; acc[c] += fn;
                isTop[a] = isTop[b] = isTop[c] = true;
            }
            for (int i = 0; i < verts.Count; i++) if (isTop[i] && acc[i].sqrMagnitude > 1e-14f) normals[i] = acc[i].normalized;
            mesh.SetNormals(normals);
        }

        static bool Lower(MissionDef d, int nx, int ny, float y)
        {
            if (nx < 0 || ny < 0 || nx >= d.GridW || ny >= d.GridH) return true;
            return BoardSpace.CellCenter(d, d.CellIndex(nx, ny)).y < y - 1e-5f;
        }

        static bool RoadEdge(MissionDef d, int x, int y, int i, int j)
        {
            int nx = i == 0 ? x - 1 : i == Sub ? x + 1 : x;
            int ny = j == 0 ? y - 1 : j == Sub ? y + 1 : y;
            if (nx == x && ny == y) return false;
            if (nx < 0 || ny < 0 || nx >= d.GridW || ny >= d.GridH) return true;
            return d.CellTerrain[d.CellIndex(nx, ny)] != 2;
        }

        /// <summary>Bilinear blend of neighbouring natural ground at the same height; built surfaces stay crisp.</summary>
        static Color BlendedBase(MissionDef d, int x, int y, float u, float v, float height)
        {
            int c0 = d.CellIndex(x, y);
            byte code0 = d.CellTerrain[c0];
            var own = BaseColor(d, code0);
            if (Hard(d, code0)) return own;
            // sample the four cell centres around this point (in cell-centre space)
            float px = x + u - 0.5f, py = y + v - 0.5f;
            int x0 = Mathf.FloorToInt(px), y0 = Mathf.FloorToInt(py);
            float fx = px - x0, fy = py - y0;
            Color Sample(int sx, int sy)
            {
                if (sx < 0 || sy < 0 || sx >= d.GridW || sy >= d.GridH) return own;
                int c = d.CellIndex(sx, sy);
                if (Mathf.Abs(BoardSpace.CellCenter(d, c).y - height) > 1e-5f || Hard(d, d.CellTerrain[c])) return own;
                return BaseColor(d, d.CellTerrain[c]);
            }
            var a = Sample(x0, y0); var b = Sample(x0 + 1, y0); var e = Sample(x0, y0 + 1); var f = Sample(x0 + 1, y0 + 1);
            return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(e, f, fx), fy);
        }

        static void Wall(MissionDef d, List<Vector3> v, List<Color> c, List<Vector3> n, List<int> t,
                         Vector3 center, float h, float y, int nx, int ny, Vector3 dir, float baseY, Color[] strata,
                         float metresPerMm, float mmToBoard)
        {
            bool outside = nx < 0 || ny < 0 || nx >= d.GridW || ny >= d.GridH;
            float lowY = outside ? baseY : BoardSpace.CellCenter(d, d.CellIndex(nx, ny)).y;
            if (lowY >= y - 1e-5f) return;
            Vector3 side = new Vector3(dir.z, 0, -dir.x); // perpendicular in xz
            Vector3 mid = center + dir * h;
            Vector3 a = mid + side * h, b = mid - side * h;
            // stratified rock above the base; on the board edge, soil bands below elevation 0
            float layer = StrataMm * metresPerMm;
            float top = y;
            float soilTop = outside ? Mathf.Min(y, 0f) : lowY;
            while (top > soilTop + 1e-5f)
            {
                // band boundaries sit on a global ladder (multiples of the layer height) so layers line up across walls
                int k = Mathf.FloorToInt((top - 1e-5f) / layer);
                float bottom = Mathf.Max(soilTop, k * layer);
                int band = ((-k) % strata.Length + strata.Length) % strata.Length;
                var col = strata[band];
                float jitter = (Hash(Mathf.RoundToInt(mid.x * 500), Mathf.RoundToInt(mid.z * 500), k) - 0.5f) * 0.06f;
                col = new Color(Mathf.Clamp01(col.r + jitter), Mathf.Clamp01(col.g + jitter), Mathf.Clamp01(col.b + jitter), 1f);
                Band(v, c, n, t, a, b, top, bottom, dir, col);
                top = bottom;
            }
            if (outside && soilTop > lowY)
            {
                Band(v, c, n, t, a, b, soilTop, Mathf.Lerp(soilTop, lowY, 0.45f), dir, Earth);
                Band(v, c, n, t, a, b, Mathf.Lerp(soilTop, lowY, 0.45f), lowY, dir, EarthDark);
            }
        }

        static void Band(List<Vector3> v, List<Color> c, List<Vector3> n, List<int> t, Vector3 a, Vector3 b,
                         float top, float bottom, Vector3 normal, Color col)
        {
            if (top - bottom < 1e-5f) return;
            Quad(v, c, n, t, new Vector3(a.x, bottom, a.z), new Vector3(a.x, top, a.z),
                new Vector3(b.x, top, b.z), new Vector3(b.x, bottom, b.z), normal, col);
        }

        static void Quad(List<Vector3> v, List<Color> c, List<Vector3> n, List<int> t,
                         Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Color col)
        {
            int i = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int k = 0; k < 4; k++) { c.Add(col); n.Add(normal); }
            // choose winding so the face points along the normal
            Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
            if (Vector3.Dot(cross, normal) >= 0) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
        }
    }
}
