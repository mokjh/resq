using System.Collections.Generic;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// "The world beyond" (Look 2.0): a backdrop arc of landscape behind and beside the board — rolling wooded
    /// hills, a forested ridge, snowy peaks, a coastline, a volcano flank — so each mission reads as a corner of a
    /// larger world instead of a tile on a table. The near (player) side stays open for the tool tray and hands.
    /// One combined static mesh (one draw call); presentation only, never pickable.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WorldRingView : MonoBehaviour
    {
        const float Depth = 0.16f;       // backdrop strip depth beyond the board edge (m)
        const float SideDepth = 0.10f;
        const int Steps = 6;             // cells across the strip depth

        struct Look { public Color Low, High, Peak, Under; public float Height, PeakLine; public bool Sea, Volcano; }

        static Look LookFor(string world)
        {
            Color H(int rgb) => TerrainBuilder.Hex(rgb);
            switch (world)
            {
                case "W2": return new Look { Low = H(0x4E6A36), High = H(0x2E4A30), Peak = H(0x8A6A3A), Under = H(0x3A2A20), Height = 0.075f, PeakLine = 0.9f };
                case "W3": return new Look { Low = H(0xC9D2D8), High = H(0x7D848B), Peak = H(0xF4F7FA), Under = H(0x5E666E), Height = 0.13f, PeakLine = 0.55f };
                case "W4": return new Look { Low = H(0x6E8F5A), High = H(0x8C8577), Peak = H(0xD8C49A), Under = H(0x2F5E6E), Height = 0.05f, PeakLine = 0.95f, Sea = true };
                case "W5": return new Look { Low = H(0x3B3836), High = H(0x2A2624), Peak = H(0x5A3A2E), Under = H(0x1E1A18), Height = 0.12f, PeakLine = 0.8f, Volcano = true };
                default: return new Look { Low = H(0x5E8456), High = H(0x3B6247), Peak = H(0x6E9160), Under = H(0x4A3A2A), Height = 0.06f, PeakLine = 0.95f };
            }
        }

        public void Build(Resq.Sim.MissionDef d)
        {
            var look = LookFor(d.WorldId);
            var ext = BoardSpace.Extent(d) * 0.5f;
            _ext = ext;
            float baseY = 0f; // board elevation 0 = channel bed level; the backdrop rises from there
            var kit = new MeshKit();
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();

            // far strip: x across the board plus side margins, z from the far edge outward
            Strip(verts, cols, tris, new Vector3(-ext.x - SideDepth, 0, ext.y), Vector3.right, (ext.x + SideDepth) * 2, Vector3.forward, Depth, look, baseY, 1);
            // side strips run from the near edge to the far edge, rising outward
            Strip(verts, cols, tris, new Vector3(-ext.x, 0, -ext.y), Vector3.forward, ext.y * 2, Vector3.left, SideDepth, look, baseY, 2);
            Strip(verts, cols, tris, new Vector3(ext.x, 0, -ext.y), Vector3.forward, ext.y * 2, Vector3.right, SideDepth, look, baseY, 3);

            var mesh = new Mesh { name = "WorldRing_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();

            // dress the near slopes with distant (scaled-down) trees from the world palette
            var palette = DecorPalette.For(d.WorldId);
            var trees = palette.Get(d.WorldId == "W5" ? DecorKind.Column : DecorKind.Conifer) ?? palette.Get(DecorKind.Broadleaf);
            var combine = new List<CombineInstance> { new CombineInstance { mesh = mesh, transform = Matrix4x4.identity } };
            if (trees != null && !look.Sea)
            {
                int count = d.WorldId == "W3" ? 70 : 110;
                for (int i = 0; i < count; i++)
                {
                    float u = Hash(i, 3), v = Hash(i, 5);
                    Vector3 p; float h;
                    int side = i % 3;
                    if (side == 0) { p = new Vector3(Mathf.Lerp(-ext.x - SideDepth, ext.x + SideDepth, u), 0, ext.y + v * Depth * 0.55f + 0.004f); h = Height(look, p, ext, 1, v * 0.55f); }
                    else
                    {
                        float sx = side == 1 ? -1 : 1;
                        p = new Vector3(sx * (ext.x + v * SideDepth * 0.55f + 0.004f), 0, Mathf.Lerp(-ext.y * 0.4f, ext.y, u));
                        h = Height(look, p, ext, side + 1, v * 0.55f);
                    }
                    if (h > look.Height * look.PeakLine * 0.85f) continue; // no trees on peaks
                    p.y = baseY + h;
                    var mesh2 = trees[i % trees.Length];
                    float s = Mathf.Lerp(0.45f, 0.7f, Hash(i, 9));
                    combine.Add(new CombineInstance { mesh = mesh2, transform = Matrix4x4.TRS(p, Quaternion.Euler(0, Hash(i, 11) * 360, 0), Vector3.one * s) });
                }
            }
            var final = new Mesh { name = "WorldRingDressed_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            final.CombineMeshes(combine.ToArray(), true, true);
            final.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = final;
        }

        static float Hash(int i, int salt)
        {
            uint h = unchecked((uint)(i * 2654435761u) ^ (uint)(salt * 2246822519u));
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (h & 0xFFFF) / 65535f;
        }

        static float Noise(float x, float y) => Mathf.PerlinNoise(x * 9f + 13.1f, y * 9f + 7.7f) * 0.65f + Mathf.PerlinNoise(x * 23f, y * 23f + 3f) * 0.35f;

        /// <summary>Height at a backdrop point: rises with distance from the board, shaped by noise (and a volcano cone).</summary>
        static float Height(Look look, Vector3 p, Vector2 ext, int strip, float t)
        {
            float rise = Mathf.SmoothStep(0, 1, t);
            float n = Noise(p.x, p.z);
            float h = look.Height * rise * (0.45f + 0.75f * n);
            if (look.Volcano && strip == 1)
            {
                float d = Mathf.Abs(p.x - ext.x * 0.35f);
                h = Mathf.Max(h, look.Height * 1.35f * Mathf.Clamp01(1 - d / 0.22f) * rise);
            }
            if (look.Sea) h = strip == 1 ? look.Height * Mathf.Clamp01(rise * 2 - 1) * n : look.Height * rise * n; // sea first, headland behind
            return h;
        }

        void Strip(List<Vector3> v, List<Color> c, List<int> t, Vector3 origin, Vector3 along, float length, Vector3 outward, float depth,
                   Look look, float baseY, int strip)
        {
            int nu = Mathf.Max(8, Mathf.RoundToInt(length / 0.012f));
            int start = v.Count;
            for (int j = 0; j <= Steps; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float u = i / (float)nu, w = j / (float)Steps;
                    var p = origin + along * (u * length) + outward * (w * depth);
                    float h = Height(look, p, BoardExtent, strip, w);
                    p.y = baseY + h;
                    float k = look.Height > 0 ? h / look.Height : 0;
                    Color col = Color.Lerp(look.Low, look.High, Mathf.Clamp01(k * 1.4f));
                    if (k > look.PeakLine) col = Color.Lerp(col, look.Peak, Mathf.Clamp01((k - look.PeakLine) / 0.15f));
                    if (look.Sea && h < 0.002f) col = look.Under;
                    if (look.Volcano && strip == 1 && k > 1.1f) col = Color.Lerp(col, TerrainBuilder.Hex(0xFF5A1F), Mathf.Clamp01((k - 1.1f) * 4f));
                    // distance haze: further = paler, cooler
                    col = Color.Lerp(col, new Color(0.72f, 0.78f, 0.84f), w * 0.28f);
                    v.Add(p); c.Add(col);
                }
            for (int j = 0; j < Steps; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = start + j * (nu + 1) + i, b = a + 1, e = a + nu + 1, f = e + 1;
                    // wind so faces point up regardless of strip orientation
                    var n = Vector3.Cross(v[e] - v[a], v[b] - v[a]);
                    if (n.y >= 0) { t.Add(a); t.Add(e); t.Add(b); t.Add(b); t.Add(e); t.Add(f); }
                    else { t.Add(a); t.Add(b); t.Add(e); t.Add(b); t.Add(f); t.Add(e); }
                }
            // skirt walls on the outer edge and both ends so the strip reads as solid ground, not a sheet
            float bottom = -BoardSpace.BaseDepth;
            void Skirt(int i0, int j0, int i1, int j1)
            {
                var a = v[start + j0 * (nu + 1) + i0]; var b = v[start + j1 * (nu + 1) + i1];
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(new Vector3(b.x, bottom, b.z)); v.Add(new Vector3(a.x, bottom, a.z));
                for (int q = 0; q < 4; q++) c.Add(look.Under);
                t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
                t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2); // double-sided
            }
            for (int i = 0; i < nu; i++) Skirt(i, Steps, i + 1, Steps);
            for (int j = 0; j < Steps; j++) { Skirt(0, j, 0, j + 1); Skirt(nu, j, nu, j + 1); }
        }

        Vector2 BoardExtent => _ext;
        Vector2 _ext;
    }
}
