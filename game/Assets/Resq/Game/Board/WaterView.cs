using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Water surface driven by simulation volumes: one vertex per cell corner, interpolated between the last two
    /// operation steps. Dry corners sink below the terrain so shorelines read naturally. Allocation-free per frame.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WaterView : MonoBehaviour
    {
        public SimDriver Driver;
        public float ForecastBlend; // 0 = live; reserved for forecast contour overlay

        Mesh _mesh;
        Vector3[] _verts;
        Color[] _colors;
        float[] _cellSurface, _cellDepth;
        bool[] _cellWet;
        MissionDef _def;

        public void Build(MissionDef d)
        {
            _def = d;
            int vw = d.GridW + 1, vh = d.GridH + 1;
            _verts = new Vector3[vw * vh];
            _colors = new Color[vw * vh];
            var uv = new Vector2[vw * vh];
            var tris = new int[d.GridW * d.GridH * 6];
            float s = BoardSpace.CellSize(d);
            for (int y = 0; y < vh; y++)
                for (int x = 0; x < vw; x++)
                {
                    int i = y * vw + x;
                    _verts[i] = new Vector3((x - d.GridW * 0.5f) * s, 0, (y - d.GridH * 0.5f) * s);
                    uv[i] = new Vector2(x / (float)d.GridW, y / (float)d.GridH);
                }
            int t = 0;
            for (int y = 0; y < d.GridH; y++)
                for (int x = 0; x < d.GridW; x++)
                {
                    int a = y * vw + x, b = a + vw, c = b + 1, e = a + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = e;
                }
            _mesh = new Mesh { name = "Water_" + d.MissionId };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts; _mesh.colors = _colors; _mesh.uv = uv; _mesh.triangles = tris;
            _mesh.RecalculateNormals();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            int n = d.GridW * d.GridH;
            _cellSurface = new float[n]; _cellDepth = new float[n]; _cellWet = new bool[n];
        }

        void LateUpdate()
        {
            if (Driver == null || Driver.Sim == null || _def != Driver.Def) return;
            var d = _def;
            var cur = Driver.Sim.S.Volume;
            var prev = Driver.PrevVolume;
            float alpha = Driver.Alpha;
            int n = d.GridW * d.GridH;
            float scale = BoardSpace.Scale(d) * BoardSpace.VerticalExaggeration;
            for (int c = 0; c < n; c++)
            {
                float vol = Mathf.Lerp(prev[c], cur[c], alpha);
                float depthMm = vol / d.WArea[c];
                _cellWet[c] = depthMm > 2f;
                _cellDepth[c] = depthMm;
                _cellSurface[c] = (d.CellElevation[c] + depthMm) * scale;
            }
            int vw = d.GridW + 1;
            for (int y = 0; y <= d.GridH; y++)
            {
                for (int x = 0; x <= d.GridW; x++)
                {
                    float sum = 0, depth = 0, minGround = float.MaxValue;
                    int wet = 0;
                    for (int dy = -1; dy <= 0; dy++)
                        for (int dx = -1; dx <= 0; dx++)
                        {
                            int cx = x + dx, cy = y + dy;
                            if (cx < 0 || cy < 0 || cx >= d.GridW || cy >= d.GridH) continue;
                            int c = cy * d.GridW + cx;
                            float ground = d.CellElevation[c] * scale;
                            if (ground < minGround) minGround = ground;
                            if (!_cellWet[c]) continue;
                            sum += _cellSurface[c]; depth += _cellDepth[c]; wet++;
                        }
                    int i = y * vw + x;
                    if (wet > 0)
                    {
                        _verts[i].y = sum / wet;
                        float dn = Mathf.Clamp01(depth / wet / 1500f);
                        _colors[i] = new Color(dn, 0, 0, 1);
                    }
                    else
                    {
                        _verts[i].y = minGround - 0.004f; // tuck dry corners under the terrain
                        _colors[i] = new Color(0, 0, 0, 0);
                    }
                }
            }
            _mesh.vertices = _verts;
            _mesh.colors = _colors;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}
