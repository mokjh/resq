using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>Builds the diorama terrain mesh (flat-topped cells, stepped walls, cutaway geology sides) from a MissionDef.</summary>
    public static class TerrainBuilder
    {
        // World 1 palette (art bible): foliage, water, roof, stone, rescue.
        static readonly Color Foliage = Hex(0x355D47), FoliageLight = Hex(0x4C7A5A), Stone = Hex(0xD8CCAF),
            Earth = Hex(0x7A5A3C), EarthDark = Hex(0x5A4130), Rock = Hex(0x8C8577), Bed = Hex(0x3E4F4A),
            Terrace = Hex(0x6E9160), Depot = Hex(0xC9B98F), Basin = Hex(0x5C6B4E);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        public static Color TopColor(byte terrainCode, int cell)
        {
            uint hash = unchecked((uint)cell * 2654435761u);
            float jitter = (hash >> 24) / 255f * 0.06f - 0.03f; // tiny deterministic variation
            Color c;
            switch (terrainCode)
            {
                case 0: case 4: case 12: c = Bed; break;            // channel / reservoir / weir bed
                case 1: case 7: c = Terrace; break;                 // terraces
                case 2: c = Stone; break;                           // road
                case 3: c = (cell % 3 == 0) ? FoliageLight : Foliage; break;
                case 5: c = FoliageLight; break;                    // field
                case 6: c = Basin; break;
                case 8: case 9: c = Depot; break;                   // depot / landing
                case 10: c = Hex(0x5E8A4E); break;                  // orchard
                case 11: c = Hex(0xB9A98A); break;                  // village
                default: c = Foliage; break;
            }
            return new Color(Mathf.Clamp01(c.r + jitter), Mathf.Clamp01(c.g + jitter), Mathf.Clamp01(c.b + jitter), 1f);
        }

        public static Mesh Build(MissionDef d)
        {
            var verts = new List<Vector3>(8000);
            var cols = new List<Color>(8000);
            var norms = new List<Vector3>(8000);
            var tris = new List<int>(16000);
            float s = BoardSpace.CellSize(d);
            float baseY = -BoardSpace.BaseDepth;
            for (int c = 0; c < d.GridW * d.GridH; c++)
            {
                var center = BoardSpace.CellCenter(d, c);
                float y = center.y;
                float h = s * 0.5f;
                var top = TopColor(d.CellTerrain[c], c);
                Quad(verts, cols, norms, tris,
                    new Vector3(center.x - h, y, center.z - h), new Vector3(center.x - h, y, center.z + h),
                    new Vector3(center.x + h, y, center.z + h), new Vector3(center.x + h, y, center.z - h), Vector3.up, top);
                int x = d.CellX(c), cy = d.CellY(c);
                // walls towards lower neighbours, and cutaway walls on the board edge
                Wall(d, verts, cols, norms, tris, center, h, y, x - 1, cy, new Vector3(-1, 0, 0), baseY);
                Wall(d, verts, cols, norms, tris, center, h, y, x + 1, cy, new Vector3(1, 0, 0), baseY);
                Wall(d, verts, cols, norms, tris, center, h, y, x, cy - 1, new Vector3(0, 0, -1), baseY);
                Wall(d, verts, cols, norms, tris, center, h, y, x, cy + 1, new Vector3(0, 0, 1), baseY);
            }
            // bottom plate
            var ext = BoardSpace.Extent(d) * 0.5f;
            Quad(verts, cols, norms, tris,
                new Vector3(-ext.x, baseY, ext.y), new Vector3(-ext.x, baseY, -ext.y),
                new Vector3(ext.x, baseY, -ext.y), new Vector3(ext.x, baseY, ext.y), Vector3.down, EarthDark);
            var mesh = new Mesh { name = "Terrain_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Wall(MissionDef d, List<Vector3> v, List<Color> c, List<Vector3> n, List<int> t,
                         Vector3 center, float h, float y, int nx, int ny, Vector3 dir, float baseY)
        {
            bool outside = nx < 0 || ny < 0 || nx >= d.GridW || ny >= d.GridH;
            float lowY = outside ? baseY : BoardSpace.CellCenter(d, d.CellIndex(nx, ny)).y;
            if (lowY >= y - 1e-5f) return;
            Vector3 side = new Vector3(dir.z, 0, -dir.x); // perpendicular in xz
            Vector3 mid = center + dir * h;
            Vector3 a = mid + side * h, b = mid - side * h;
            // geology bands on the cutaway edge, plain earth/rock on interior steps
            if (outside)
            {
                float zeroY = 0f;
                float split = Mathf.Max(lowY, Mathf.Min(y, zeroY));
                if (y > split) Band(v, c, n, t, a, b, y, split, dir, Rock);
                Band(v, c, n, t, a, b, split, Mathf.Lerp(split, lowY, 0.45f), dir, Earth);
                Band(v, c, n, t, a, b, Mathf.Lerp(split, lowY, 0.45f), lowY, dir, EarthDark);
            }
            else Band(v, c, n, t, a, b, y, lowY, dir, Color.Lerp(Rock, Earth, 0.35f));
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
