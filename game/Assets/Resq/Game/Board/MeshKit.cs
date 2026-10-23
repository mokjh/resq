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
