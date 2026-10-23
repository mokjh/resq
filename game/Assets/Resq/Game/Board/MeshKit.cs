using System.Collections.Generic;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>Tiny procedural mesh builder with vertex colours (greybox kit until final art lands).</summary>
    public sealed class MeshKit
    {
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        public MeshKit Box(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Face(center, new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), col);
            Face(center, new Vector3(0, -h.y, 0), new Vector3(0, 0, h.z), new Vector3(h.x, 0, 0), col);
            Face(center, new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), col);
            Face(center, new Vector3(-h.x, 0, 0), new Vector3(0, h.y, 0), new Vector3(0, 0, h.z), col);
            Face(center, new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), col);
            Face(center, new Vector3(0, 0, -h.z), new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), col);
            return this;
        }

        /// <summary>A square-section bar from a to b (cables, struts).</summary>
        public MeshKit Segment(Vector3 a, Vector3 b, float halfWidth, Color col)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-6f) return this;
            Vector3 w = axis / len;
            Vector3 u = Vector3.Cross(w, Mathf.Abs(w.y) > 0.9f ? Vector3.right : Vector3.up).normalized * halfWidth;
            Vector3 v = Vector3.Cross(w, u).normalized * halfWidth;
            Vector3 c = (a + b) * 0.5f, h = w * (len * 0.5f);
            Face(c, u, v, h, col);
            Face(c, -u, h, v, col);
            Face(c, v, h, u, col);
            Face(c, -v, u, h, col);
            Face(c, h, u, v, col);
            Face(c, -h, v, u, col);
            return this;
        }

        void Face(Vector3 c, Vector3 normalOffset, Vector3 u, Vector3 v, Color col)
        {
            Vector3 n = normalOffset.normalized;
            Vector3 o = c + normalOffset;
            int i = _v.Count;
            _v.Add(o - u - v); _v.Add(o - u + v); _v.Add(o + u + v); _v.Add(o + u - v);
            for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
            Vector3 cross = Vector3.Cross(_v[i + 1] - _v[i], _v[i + 2] - _v[i]);
            if (Vector3.Dot(cross, n) >= 0) { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3); }
            else { _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); _t.Add(i); _t.Add(i + 3); _t.Add(i + 2); }
        }

        public MeshKit Cylinder(Vector3 bottom, float radius, float height, int sides, Color col, bool cap = true)
        {
            int start = _v.Count;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int i = _v.Count;
                _v.Add(bottom + d0 * radius); _v.Add(bottom + d0 * radius + Vector3.up * height);
                _v.Add(bottom + d1 * radius + Vector3.up * height); _v.Add(bottom + d1 * radius);
                _n.Add(d0); _n.Add(d0); _n.Add(d1); _n.Add(d1);
                for (int k = 0; k < 4; k++) _c.Add(col);
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
            }
            if (cap)
            {
                int ci = _v.Count;
                Vector3 top = bottom + Vector3.up * height;
                _v.Add(top); _n.Add(Vector3.up); _c.Add(col);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    _v.Add(top + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius); _n.Add(Vector3.up); _c.Add(col);
                }
                for (int s = 0; s < sides; s++) { _t.Add(ci); _t.Add(ci + s + 2); _t.Add(ci + s + 1); }
            }
            return this;
        }

        /// <summary>
        /// Normalise without Unity's 1e-5 magnitude cut-off: millimetre-scale faces have cross products around 1e-6
        /// and would otherwise get zero normals (rendering black).
        /// </summary>
        static Vector3 Unit(Vector3 v)
        {
            double m = System.Math.Sqrt((double)v.x * v.x + (double)v.y * v.y + (double)v.z * v.z);
            return m < 1e-20 ? Vector3.zero : new Vector3((float)(v.x / m), (float)(v.y / m), (float)(v.z / m));
        }

        /// <summary>Closed cone (tree tiers, grass blades, vent cones).</summary>
        public MeshKit Cone(Vector3 bottom, float radius, float height, int sides, Color col, Color tipCol)
        {
            Vector3 tip = bottom + Vector3.up * height;
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2 / sides, a1 = (s + 1) * Mathf.PI * 2 / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 p0 = bottom + d0 * radius, p1 = bottom + d1 * radius;
                Vector3 n = Unit(Vector3.Cross(p1 - p0, tip - p0));
                if (n.y < 0) n = -n;
                int i = _v.Count;
                _v.Add(p0); _v.Add(tip); _v.Add(p1);
                _n.Add(n); _n.Add(n); _n.Add(n);
                _c.Add(col); _c.Add(tipCol); _c.Add(col);
                Vector3 cross = Vector3.Cross(_v[i + 1] - _v[i], _v[i + 2] - _v[i]);
                if (Vector3.Dot(cross, n) >= 0) { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); } else { _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); }
            }
            return this;
        }

        /// <summary>
        /// Low-poly organic lump (crowns, rocks, shrubs): a sphere with hashed radial jitter, squash, and a darker
        /// underside standing in for ambient occlusion. Faceted (flat normals) to match the kit's crafted look.
        /// </summary>
        public MeshKit Blob(Vector3 center, Vector3 radii, Color top, Color under, int seed, float jitter = 0.18f, int seg = 7)
        {
            int rings = Mathf.Max(3, seg / 2 + 1);
            var pts = new Vector3[(rings + 1) * (seg + 1)];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float th = 2 * Mathf.PI * (s % seg) / seg;
                    Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    uint h = unchecked((uint)((r * 73 + (s % seg) * 151 + seed * 997) * 2654435761u));
                    float k = 1f + ((h >> 16) / 65535f - 0.5f) * 2f * jitter;
                    if (r == 0 || r == rings) k = 1f;
                    pts[r * (seg + 1) + s] = center + Vector3.Scale(d * k, radii);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    var a = pts[r * (seg + 1) + s]; var b = pts[r * (seg + 1) + s + 1];
                    var c = pts[(r + 1) * (seg + 1) + s]; var e = pts[(r + 1) * (seg + 1) + s + 1];
                    Tri(a, b, c, center, top, under, radii.y);
                    Tri(b, e, c, center, top, under, radii.y);
                }
            return this;
        }

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 centre, Color top, Color under, float ry)
        {
            Vector3 n = Unit(Vector3.Cross(b - a, c - a));
            if (n == Vector3.zero) return;
            if (Vector3.Dot(n, (a + b + c) / 3f - centre) < 0) { var t = b; b = c; c = t; n = -n; }
            int i = _v.Count;
            foreach (var p in new[] { a, b, c })
            {
                _v.Add(p); _n.Add(n);
                float h = Mathf.Clamp01(((p.y - centre.y) / Mathf.Max(1e-6f, ry) + 1f) * 0.5f);
                _c.Add(Color.Lerp(under, top, h));
            }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        public MeshKit Sphere(Vector3 center, float radius, Color col, int seg = 8)
        {
            int rings = seg / 2;
            int start = _v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float th = 2 * Mathf.PI * s / seg;
                    Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    _v.Add(center + d * radius); _n.Add(d); _c.Add(col);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = start + r * (seg + 1) + s, b = a + seg + 1;
                    _t.Add(a); _t.Add(a + 1); _t.Add(b);
                    _t.Add(a + 1); _t.Add(b + 1); _t.Add(b);
                }
            return this;
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(_v); m.SetNormals(_n); m.SetColors(_c); m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
