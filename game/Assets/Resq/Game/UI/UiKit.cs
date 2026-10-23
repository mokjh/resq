using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    public enum Icon : byte { People, Box, Check, Ring, Star, Mountain, Tree, Wave, Wind, Volcano, Snow, Drop, Pinch, Rotate, Warning, Boat, Bridge, Barrier }

    /// <summary>
    /// HUD building blocks in the concept style: frosted rounded glass panels, flat vector icons as meshes (no
    /// texture atlas needed) and the Atkinson Hyperlegible fonts (OFL, chosen for legibility at a distance).
    /// Everything is built in the XY plane facing -Z, the side the seated player sees.
    /// </summary>
    public static class UiKit
    {
        public static Color GlassFill => Comfort.HighContrast ? new Color(0.02f, 0.03f, 0.035f, 0.96f) : new Color(0.075f, 0.10f, 0.115f, 0.80f);
        public static readonly Color GlassEdge = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color Ink = TerrainBuilder.Hex(0xF3F5F2);
        public static readonly Color InkSoft = new Color(0.80f, 0.86f, 0.86f, 1f);
        public static readonly Color Accent = TerrainBuilder.Hex(0x5EE6F2);  // hologram cyan
        public static readonly Color Good = TerrainBuilder.Hex(0x7BD389);
        public static readonly Color Warn = TerrainBuilder.Hex(0xF2B134);
        public static readonly Color Bad = TerrainBuilder.Hex(0xE5484D);

        static TMP_FontAsset _regular, _bold;

        public static TMP_FontAsset Regular => _regular != null ? _regular : (_regular = Load("Fonts/AtkinsonHyperlegible-Regular"));
        public static TMP_FontAsset Bold => _bold != null ? _bold : (_bold = Load("Fonts/AtkinsonHyperlegible-Bold"));

        static TMP_FontAsset Load(string path)
        {
            var font = Resources.Load<Font>(path);
            if (font == null) return null; // falls back to TMP's default
            var asset = TMP_FontAsset.CreateFontAsset(font, 64, 6, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
            asset.name = font.name;
            return asset;
        }

        // ------------------------------------------------------------------ panels
        /// <summary>Rounded rectangle centred on the origin; UV.y runs 0→1 bottom to top (for the glass sheen).</summary>
        public static Mesh RoundedRect(float w, float h, float r, Color fill, int cornerSegs = 6)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            var v = new List<Vector3> { Vector3.zero };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var corners = new[] { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= cornerSegs; s++)
                {
                    float a = (c * 90f + s * 90f / cornerSegs) * Mathf.Deg2Rad;
                    var p = corners[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    v.Add(p); uv.Add(new Vector2(p.x / w + 0.5f, p.y / h + 0.5f));
                }
            var t = new List<int>();
            for (int i = 1; i < v.Count; i++) { t.Add(0); t.Add(i); t.Add(i == v.Count - 1 ? 1 : i + 1); }
            var cols = new Color[v.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = fill;
            var m = new Mesh { name = "RoundedRect" };
            m.SetVertices(v); m.SetUVs(0, uv); m.colors = cols; m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A glass panel with a hairline edge, parented and positioned; returns its root.</summary>
        public static Transform Panel(Transform parent, string name, Vector3 localPos, float w, float h, Material glass, float radius = 0.012f, Color? fill = null)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            Quad(root, "Edge", RoundedRect(w + 0.0016f, h + 0.0016f, radius + 0.0008f, GlassEdge), glass, 0.0004f, 0);
            Quad(root, "Fill", RoundedRect(w, h, radius, fill ?? GlassFill), glass, 0f, 1); // fill always over its hairline edge
            return root;
        }

        static void Quad(Transform parent, string name, Mesh mesh, Material mat, float z, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.sortingOrder = order;
        }

        public static TextMeshPro Text(Transform parent, string text, Vector3 pos, float size, Color color, bool bold = false,
                                       float w = 0.2f, float h = 0.03f, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -0.0008f);
            var tmp = go.AddComponent<TextMeshPro>();
            var font = bold ? Bold : Regular;
            if (font != null) tmp.font = font;
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.rectTransform.sizeDelta = new Vector2(w, h);
            tmp.sortingOrder = 3; // URP sorts by sorting order before queue: text always over glass (1) and icons (2)
            return tmp;
        }

        // ------------------------------------------------------------------ icons
        public static Transform IconObj(Transform parent, Icon icon, Vector3 pos, float size, Color color, Material glass)
        {
            var go = new GameObject("Icon_" + icon);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos + new Vector3(0, 0, -0.0006f);
            go.AddComponent<MeshFilter>().sharedMesh = IconMesh(icon, size, color);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = glass;
            r.sortingOrder = 2;
            return go.transform;
        }

        static readonly Dictionary<(Icon, int, int), Mesh> IconCache = new Dictionary<(Icon, int, int), Mesh>();

        public static Mesh IconMesh(Icon icon, float s, Color col)
        {
            var key = (icon, Mathf.RoundToInt(s * 10000), col.GetHashCode());
            if (IconCache.TryGetValue(key, out var cached)) return cached;
            var b = new Shape(col);
            float h = s * 0.5f;
            switch (icon)
            {
                case Icon.People:
                    b.Disc(new Vector2(-0.18f * s, 0.18f * s), 0.13f * s); b.HalfDisc(new Vector2(-0.18f * s, -0.32f * s), 0.24f * s);
                    b.Disc(new Vector2(0.2f * s, 0.24f * s), 0.15f * s); b.HalfDisc(new Vector2(0.2f * s, -0.32f * s), 0.28f * s);
                    break;
                case Icon.Box:
                    b.Poly(new Vector2(0, 0.42f * s), new Vector2(0.42f * s, 0.2f * s), new Vector2(0, -0.02f * s), new Vector2(-0.42f * s, 0.2f * s));
                    b.Poly(new Vector2(-0.42f * s, 0.14f * s), new Vector2(-0.03f * s, -0.08f * s), new Vector2(-0.03f * s, -0.48f * s), new Vector2(-0.42f * s, -0.26f * s));
                    b.Poly(new Vector2(0.03f * s, -0.08f * s), new Vector2(0.42f * s, 0.14f * s), new Vector2(0.42f * s, -0.26f * s), new Vector2(0.03f * s, -0.48f * s));
                    break;
                case Icon.Check:
                    b.Annulus(Vector2.zero, h * 0.86f, h); b.Line(new Vector2(-0.2f * s, 0), new Vector2(-0.05f * s, -0.16f * s), 0.09f * s);
                    b.Line(new Vector2(-0.05f * s, -0.16f * s), new Vector2(0.23f * s, 0.17f * s), 0.09f * s);
                    break;
                case Icon.Ring: b.Annulus(Vector2.zero, h * 0.82f, h); break;
                case Icon.Star:
                {
                    var pts = new List<Vector2>();
                    for (int i = 0; i < 10; i++) { float a = Mathf.PI / 2 + i * Mathf.PI / 5; float r = i % 2 == 0 ? h : h * 0.42f; pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
                    b.Fan(Vector2.zero, pts);
                    break;
                }
                case Icon.Mountain:
                    b.Poly(new Vector2(-0.5f * s, -0.32f * s), new Vector2(-0.08f * s, 0.3f * s), new Vector2(0.26f * s, -0.32f * s));
                    b.Poly(new Vector2(-0.02f * s, -0.32f * s), new Vector2(0.2f * s, 0.06f * s), new Vector2(0.5f * s, -0.32f * s));
                    break;
                case Icon.Tree:
                    b.Poly(new Vector2(0, 0.48f * s), new Vector2(0.3f * s, 0.05f * s), new Vector2(-0.3f * s, 0.05f * s));
                    b.Poly(new Vector2(0, 0.25f * s), new Vector2(0.38f * s, -0.22f * s), new Vector2(-0.38f * s, -0.22f * s));
                    b.Line(new Vector2(0, -0.22f * s), new Vector2(0, -0.46f * s), 0.1f * s);
                    break;
                case Icon.Wave:
                    for (int k = 0; k < 2; k++)
                    {
                        float y0 = (0.14f - k * 0.3f) * s;
                        for (int i = 0; i < 10; i++)
                        {
                            float x0 = -0.46f * s + i * 0.092f * s, x1 = x0 + 0.092f * s;
                            b.Line(new Vector2(x0, y0 + Mathf.Sin(i * 0.7f) * 0.09f * s), new Vector2(x1, y0 + Mathf.Sin((i + 1) * 0.7f) * 0.09f * s), 0.07f * s);
                        }
                    }
                    break;
                case Icon.Wind:
                    b.Line(new Vector2(-0.46f * s, 0.2f * s), new Vector2(0.3f * s, 0.2f * s), 0.07f * s);
                    b.Line(new Vector2(-0.46f * s, 0f), new Vector2(0.42f * s, 0f), 0.07f * s);
                    b.Line(new Vector2(-0.46f * s, -0.2f * s), new Vector2(0.18f * s, -0.2f * s), 0.07f * s);
                    break;
                case Icon.Volcano:
                    b.Poly(new Vector2(-0.48f * s, -0.38f * s), new Vector2(-0.12f * s, 0.12f * s), new Vector2(0.12f * s, 0.12f * s), new Vector2(0.48f * s, -0.38f * s));
                    b.Disc(new Vector2(0.06f * s, 0.32f * s), 0.11f * s); b.Disc(new Vector2(-0.12f * s, 0.4f * s), 0.08f * s);
                    break;
                case Icon.Snow:
                    for (int i = 0; i < 3; i++) { float a = i * Mathf.PI / 3; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h; b.Line(-d, d, 0.07f * s); }
                    break;
                case Icon.Drop:
                    b.Disc(new Vector2(0, -0.12f * s), 0.28f * s); b.Poly(new Vector2(0, 0.46f * s), new Vector2(0.25f * s, -0.02f * s), new Vector2(-0.25f * s, -0.02f * s));
                    break;
                case Icon.Pinch:
                    b.Annulus(new Vector2(-0.12f * s, -0.04f * s), 0.2f * s, 0.27f * s);
                    b.Line(new Vector2(0.1f * s, 0.06f * s), new Vector2(0.36f * s, 0.42f * s), 0.09f * s);
                    b.Disc(new Vector2(0.1f * s, 0.06f * s), 0.07f * s);
                    break;
                case Icon.Rotate:
                    for (int i = 0; i < 12; i++) { float a0 = 0.5f + i * 0.42f, a1 = a0 + 0.42f; b.Line(new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * h * 0.8f, new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * h * 0.8f, 0.08f * s); }
                    b.Poly(new Vector2(0.48f * s, 0.24f * s), new Vector2(0.26f * s, 0.3f * s), new Vector2(0.4f * s, 0.06f * s));
                    break;
                case Icon.Warning:
                    b.Poly(new Vector2(0, 0.46f * s), new Vector2(0.5f * s, -0.4f * s), new Vector2(-0.5f * s, -0.4f * s));
                    break;
                case Icon.Boat:
                    b.Poly(new Vector2(-0.48f * s, -0.05f * s), new Vector2(0.48f * s, -0.05f * s), new Vector2(0.3f * s, -0.32f * s), new Vector2(-0.36f * s, -0.32f * s));
                    b.Poly(new Vector2(-0.1f * s, -0.05f * s), new Vector2(0.18f * s, -0.05f * s), new Vector2(0.18f * s, 0.22f * s), new Vector2(-0.1f * s, 0.22f * s));
                    break;
                case Icon.Bridge:
                    b.Line(new Vector2(-0.48f * s, -0.1f * s), new Vector2(0.48f * s, -0.1f * s), 0.09f * s);
                    for (int i = -2; i <= 2; i++) b.Line(new Vector2(i * 0.22f * s, -0.1f * s), new Vector2(i * 0.22f * s + 0.11f * s, 0.22f * s), 0.05f * s);
                    b.Line(new Vector2(-0.44f * s, 0.22f * s), new Vector2(0.48f * s, 0.22f * s), 0.06f * s);
                    break;
                case Icon.Barrier:
                    b.Poly(new Vector2(-0.46f * s, -0.36f * s), new Vector2(0.46f * s, -0.36f * s), new Vector2(0.46f * s, 0.2f * s), new Vector2(-0.46f * s, 0.2f * s));
                    b.Line(new Vector2(-0.3f * s, -0.36f * s), new Vector2(-0.3f * s, 0.36f * s), 0.08f * s);
                    b.Line(new Vector2(0.3f * s, -0.36f * s), new Vector2(0.3f * s, 0.36f * s), 0.08f * s);
                    break;
            }
            var mesh = b.Build("Icon_" + icon);
            IconCache[key] = mesh;
            return mesh;
        }

        /// <summary>2D filled-shape builder in the XY plane (alpha 1, so the glass sheen skips icons).</summary>
        sealed class Shape
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<int> _t = new List<int>();
            readonly Color _c;
            public Shape(Color c) { _c = new Color(c.r, c.g, c.b, 1f); }

            public void Fan(Vector2 c, List<Vector2> rim)
            {
                int o = _v.Count;
                _v.Add(c);
                foreach (var p in rim) _v.Add(p);
                for (int i = 0; i < rim.Count; i++) { _t.Add(o); _t.Add(o + 1 + i); _t.Add(o + 1 + (i + 1) % rim.Count); }
            }

            public void Disc(Vector2 c, float r, int n = 16)
            {
                var rim = new List<Vector2>();
                for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2 / n; rim.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
                Fan(c, rim);
            }

            public void HalfDisc(Vector2 c, float r, int n = 10)
            {
                var rim = new List<Vector2>();
                for (int i = 0; i <= n; i++) { float a = i * Mathf.PI / n; rim.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
                int o = _v.Count;
                _v.Add(c);
                foreach (var p in rim) _v.Add(p);
                for (int i = 0; i < n; i++) { _t.Add(o); _t.Add(o + 1 + i); _t.Add(o + 2 + i); }
            }

            public void Annulus(Vector2 c, float r0, float r1, int n = 24)
            {
                int o = _v.Count;
                for (int i = 0; i <= n; i++)
                {
                    float a = i * Mathf.PI * 2 / n; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    _v.Add(c + d * r0); _v.Add(c + d * r1);
                }
                for (int i = 0; i < n; i++)
                {
                    int a = o + i * 2;
                    _t.Add(a); _t.Add(a + 1); _t.Add(a + 3); _t.Add(a); _t.Add(a + 3); _t.Add(a + 2);
                }
            }

            public void Poly(params Vector2[] pts)
            {
                var c = Vector2.zero;
                foreach (var p in pts) c += p;
                Fan(c / pts.Length, new List<Vector2>(pts));
            }

            public void Line(Vector2 a, Vector2 b, float w)
            {
                var d = (b - a).normalized; var n = new Vector2(-d.y, d.x) * (w * 0.5f);
                Poly(a + n, b + n, b - n, a - n);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                var cols = new Color[_v.Count];
                for (int i = 0; i < cols.Length; i++) cols[i] = _c;
                m.colors = cols;
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
