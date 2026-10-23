using System.Collections.Generic;
using Resq.Sim;
using Resq.Sim.Rules;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// World 2–5 hazards drawn from simulation state: a per-cell overlay (fire heat/state and wetness, lava segment
    /// state, avalanche debris), structure stability labels and the tide gauge. Read-only and allocation-free per
    /// frame; water itself stays in <see cref="WaterView"/>.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class HazardView : MonoBehaviour
    {
        public SimDriver Driver;

        MissionDef _def;
        Mesh _mesh;
        Color[] _colors;
        int[] _cellOfQuad;
        int[] _runoutSegOfCell;
        readonly List<(int support, TextMeshPro label)> _supports = new List<(int, TextMeshPro)>();
        TextMeshPro _tide;

        static readonly Color Ember = TerrainBuilder.Hex(0xE5484D), Flame = TerrainBuilder.Hex(0xFF8A3D), Heat = TerrainBuilder.Hex(0xF2B134),
            Ash = TerrainBuilder.Hex(0x3B3A38), WetBlue = TerrainBuilder.Hex(0x5AA9E6), Snow = TerrainBuilder.Hex(0xF4F7FA),
            Debris = TerrainBuilder.Hex(0xBFC6CC), Lava = TerrainBuilder.Hex(0xFF5A1F), Crust = TerrainBuilder.Hex(0x4A4440),
            Cooling = TerrainBuilder.Hex(0x9B2D20), Clear = new Color(0, 0, 0, 0);

        public bool Active { get; private set; }

        public void Build(MissionDef d)
        {
            _def = d;
            foreach (Transform t in transform) Destroy(t.gameObject);
            _supports.Clear();
            _tide = null;
            int n = d.GridW * d.GridH;
            _runoutSegOfCell = new int[n];
            for (int c = 0; c < n; c++) _runoutSegOfCell[c] = -1;
            for (int s = 0; s < d.SegmentIds.Length; s++)
                foreach (int c in d.SegmentCells[s]) if (c < n) _runoutSegOfCell[c] = s;

            // only cells a hazard can ever touch get a quad
            var cells = new List<int>();
            for (int c = 0; c < n; c++)
            {
                bool fire = d.FireEnabled;
                bool lava = d.CellLavaSeg != null && d.CellLavaSeg[c] >= 0;
                bool runout = _runoutSegOfCell[c] >= 0;
                if (fire || lava || runout) cells.Add(c);
            }
            Active = cells.Count > 0 || d.SupportIds.Length > 0 || d.TideIds.Length > 0;
            _cellOfQuad = cells.ToArray();
            var verts = new Vector3[cells.Count * 4];
            _colors = new Color[cells.Count * 4];
            var tris = new int[cells.Count * 6];
            float h = BoardSpace.CellSize(d) * 0.5f;
            for (int q = 0; q < cells.Count; q++)
            {
                var p = BoardSpace.CellCenter(d, cells[q]);
                p.y += 0.0008f;
                verts[q * 4 + 0] = p + new Vector3(-h, 0, -h);
                verts[q * 4 + 1] = p + new Vector3(-h, 0, h);
                verts[q * 4 + 2] = p + new Vector3(h, 0, h);
                verts[q * 4 + 3] = p + new Vector3(h, 0, -h);
                int t = q * 6, v = q * 4;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _mesh = new Mesh { name = "Hazards_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _mesh.vertices = verts; _mesh.colors = _colors; _mesh.triangles = tris;
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            for (int s = 0; s < d.SupportIds.Length; s++)
            {
                int node = d.SupportLoadNodes[s].Length > 0 ? d.SupportLoadNodes[s][0]
                    : d.SupportCollapseNodes[s].Length > 0 ? d.SupportCollapseNodes[s][0] : -1;
                if (node < 0) continue;
                _supports.Add((s, Label(BoardSpace.CellCenter(d, d.NodeCell[node]) + new Vector3(0, 0.05f, 0))));
            }
            if (d.SeaCell.Length > 0) _tide = Label(BoardSpace.CellCenter(d, d.SeaCell[0]) + new Vector3(0, 0.06f, 0));
        }

        TextMeshPro Label(Vector3 pos)
        {
            var go = new GameObject("HazardLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = 0.11f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = TerrainBuilder.Hex(0x132B2A);
            tmp.rectTransform.sizeDelta = new Vector2(0.12f, 0.03f);
            go.AddComponent<Billboard>();
            return tmp;
        }

        void LateUpdate()
        {
            if (Driver == null || Driver.Sim == null || Driver.Def != _def || !Active) return;
            var sim = Driver.Sim;
            var S = sim.S;
            float t = Comfort.ReducedEffects ? 0f : Time.time;   // reduced effects: steady colours, no flicker
            for (int q = 0; q < _cellOfQuad.Length; q++)
            {
                int c = _cellOfQuad[q];
                Color col = Clear;
                int lavaSeg = _def.CellLavaSeg != null ? _def.CellLavaSeg[c] : -1;
                int runSeg = _runoutSegOfCell[c];
                if (lavaSeg >= 0) col = LavaColor(sim, lavaSeg, c, t);
                if (col.a <= 0 && runSeg >= 0) col = RunoutColor(sim, runSeg);
                if (col.a <= 0 && _def.FireEnabled) col = FireColor(sim, c, t);
                int v = q * 4;
                _colors[v] = col; _colors[v + 1] = col; _colors[v + 2] = col; _colors[v + 3] = col;
            }
            if (_cellOfQuad.Length > 0) _mesh.colors = _colors;

            foreach (var (s, label) in _supports)
            {
                if (S.SupCollapsed[s]) { label.text = "Collapsed"; label.color = Ember; continue; }
                int eff = sim.EffectiveStability(s);
                int braces = sim.IntactBraces(s);
                label.text = $"Stability {eff}{(braces > 0 ? $"  ·  {braces} brace{(braces > 1 ? "s" : "")}" : "")}";
                label.color = eff <= Simulator.SupportWarnStability ? (S.SupArmed[s] ? Ember : Heat) : TerrainBuilder.Hex(0x7BD389);
            }
            if (_tide != null)
            {
                int k = 0;
                long level = S.TideLevel[k];
                long target = S.TideEndLevel[k];
                string arrow = target > level ? " ↑" : target < level ? " ↓" : "";
                _tide.text = $"Tide {level} mm{arrow}";
                _tide.color = TerrainBuilder.Hex(0xA8D8F0);
            }
        }

        Color FireColor(Simulator sim, int c, float t)
        {
            var st = sim.CellFire(c);
            int heat = sim.CellHeat(c);
            switch (st)
            {
                case FireState.Burning:
                    float flick = 0.5f + 0.5f * Mathf.Sin(t * 9f + c * 1.7f);
                    return new Color(Flame.r, Mathf.Lerp(Flame.g, 0.25f, flick), Flame.b, 0.85f);
                case FireState.Cooling: return new Color(Cooling.r, Cooling.g, Cooling.b, 0.7f);
                case FireState.SafeAsh: return new Color(Ash.r, Ash.g, Ash.b, 0.75f);
                case FireState.Heating: return new Color(Heat.r, Heat.g, Heat.b, Mathf.Clamp01(heat / 600f) * 0.7f);
            }
            int wet = sim.S.FWet[c];
            if (wet > 50) return new Color(WetBlue.r, WetBlue.g, WetBlue.b, Mathf.Clamp01(wet / 1000f) * 0.45f);
            if (heat > 0) return new Color(Heat.r, Heat.g, Heat.b, Mathf.Clamp01(heat / 600f) * 0.5f);
            return Clear;
        }

        Color LavaColor(Simulator sim, int seg, int c, float t)
        {
            var S = sim.S;
            switch ((LavaState)S.LState[seg])
            {
                case LavaState.Hot:
                    float glow = 0.85f + 0.15f * Mathf.Sin(t * 3f + c);
                    return new Color(Lava.r * glow, Lava.g * glow, Lava.b, 0.92f);
                case LavaState.Cooling: return new Color(Cooling.r, Cooling.g, Cooling.b, 0.9f);
                case LavaState.Crusted: return new Color(Crust.r, Crust.g, Crust.b, 0.9f);
                case LavaState.Reheating:
                    float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6f);
                    return Color.Lerp(new Color(Crust.r, Crust.g, Crust.b, 0.9f), new Color(Lava.r, Lava.g, Lava.b, 0.9f), pulse);
                default:
                    // published but not yet arrived: a warning shimmer on the channel it will fill
                    if (S.LPlanned[seg] >= 0) return new Color(Heat.r, Heat.g, Heat.b, 0.25f + 0.2f * Mathf.Sin(t * 4f));
                    return Clear;
            }
        }

        Color RunoutColor(Simulator sim, int seg)
        {
            var S = sim.S;
            long start = S.SegStart[seg];
            if (start < 0) return Clear;
            if (sim.SegmentActive(seg)) return new Color(Snow.r, Snow.g, Snow.b, 0.92f);
            return new Color(Debris.r, Debris.g, Debris.b, 0.6f); // settled debris stays visible
        }
    }
}
