using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Dashed cyan evacuation routes (concept style): for every group or vehicle with an order, the path it will
    /// take from where it is now to its target, and — while the player is choosing a destination — a brighter
    /// preview to the hovered node. Paths come from the simulator's own shortest-path query, so the line is
    /// exactly the route the rules will use. Rebuilt a few times a second; one mesh, one draw call.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RouteView : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;
        const float Width = 0.0022f, Lift = 0.0035f, Interval = 0.2f;

        MissionDef _def;
        Mesh _mesh;
        float _next;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<int> _t = new List<int>();
        readonly List<Vector3> _pts = new List<Vector3>();
        int _lastHover = -1;

        public void Build(MissionDef d)
        {
            _def = d;
            _mesh = new Mesh { name = "Routes" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        void LateUpdate()
        {
            if (Driver?.Sim == null || Driver.Def != _def) return;
            int hover = Interaction != null && Interaction.Hover != null && Interaction.Hover.Kind == PickKind.Node ? Interaction.Hover.Index : -1;
            if (Time.unscaledTime < _next && hover == _lastHover) return;
            _next = Time.unscaledTime + Interval; _lastHover = hover;
            _v.Clear(); _uv.Clear(); _n.Clear(); _t.Clear();
            var sim = Driver.Sim; var S = sim.S;
            for (int g = 0; g < _def.GroupIds.Length; g++)
            {
                int target = S.GroupTarget[g];
                if (target < 0 || S.GroupHeld[g]) continue;
                int from = FirstWaiting(g, target);
                if (from >= 0) AddPath(sim, from, target, Traversal.Ground, false);
            }
            for (int v = 0; v < S.C.VehicleCount; v++)
            {
                var phase = (VehiclePhase)S.VPhase[v];
                int target = phase == VehiclePhase.ToPickup ? S.VPickupNode[v] : phase == VehiclePhase.ToDestination ? S.VDestination[v] : -1;
                if (target < 0 || S.VState[v] == (byte)VehicleState.Failed) continue;
                AddPath(sim, S.VNode[v], target, Simulator.VehicleClass((VehicleKind)S.VKind[v]), false);
            }
            // preview while choosing where a group should go
            if (hover >= 0 && Interaction.Mode == InteractMode.GroupRouting)
                AddPath(sim, Interaction.CurrentNodeOfGroup(Interaction.SelectedGroup), hover, Traversal.Ground, true);
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetUVs(0, _uv); _mesh.SetNormals(_n); _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        int FirstWaiting(int g, int target)
        {
            var S = Driver.Sim.S;
            foreach (int r in _def.GroupResidents[g])
            {
                var st = (ResidentState)S.RState[r];
                if (st == ResidentState.AtNode && S.RNode[r] != target && !S.RStay[r]) return S.RNode[r];
                if (st == ResidentState.Walking) return S.RNode[r];
            }
            return -1;
        }

        void AddPath(Simulator sim, int from, int to, Traversal cls, bool preview)
        {
            if (from < 0 || to < 0 || from == to) return;
            _pts.Clear();
            _pts.Add(NodePoint(from, cls));
            int at = from;
            for (int guard = 0; guard < 40 && at != to; guard++)
            {
                int e = sim.NextEdge(at, to, cls, false);
                if (e < 0) break;
                int next = _def.EdgeTo[e] == at ? _def.EdgeFrom[e] : _def.EdgeTo[e];
                var cells = _def.EdgeSampleCells[e];
                if (cells != null && cls != Traversal.Cable)
                {
                    bool forward = _def.EdgeFrom[e] == at;
                    for (int i = 0; i < cells.Length; i++)
                    {
                        int c = cells[forward ? i : cells.Length - 1 - i];
                        var p = BoardSpace.CellCenter(_def, c);
                        if (cls == Traversal.Water) p.y = WaterY(c);
                        _pts.Add(p + Vector3.up * Lift);
                    }
                }
                _pts.Add(NodePoint(next, cls));
                at = next;
            }
            Ribbon(preview);
        }

        Vector3 NodePoint(int node, Traversal cls)
        {
            int cell = cls == Traversal.Water && _def.NodeWaterCell[node] >= 0 ? _def.NodeWaterCell[node] : _def.NodeCell[node];
            var p = BoardSpace.CellCenter(_def, cell);
            if (cls == Traversal.Water) p.y = WaterY(cell);
            if (cls == Traversal.Cable) p.y += ActorsView.CableHeight;
            return p + Vector3.up * Lift;
        }

        float WaterY(int cell) => BoardSpace.Height(_def, _def.CellElevation[cell] + Driver.Sim.S.Volume[cell] / _def.WArea[cell]);

        /// <summary>Flat ribbon along the polyline; U = metres along the path so the shader can march dashes.</summary>
        void Ribbon(bool preview)
        {
            float w = preview ? Width * 1.6f : Width;
            float along = 0;
            for (int i = 0; i + 1 < _pts.Count; i++)
            {
                var a = _pts[i]; var b = _pts[i + 1];
                var d = b - a; float len = d.magnitude;
                if (len < 1e-5f) continue;
                var side = Vector3.Cross(Vector3.up, d / len) * (w * 0.5f);
                if (side.sqrMagnitude < 1e-12f) side = Vector3.right * (w * 0.5f);
                int k = _v.Count;
                _v.Add(a - side); _v.Add(a + side); _v.Add(b + side); _v.Add(b - side);
                _uv.Add(new Vector2(along, 0)); _uv.Add(new Vector2(along, 1)); _uv.Add(new Vector2(along + len, 1)); _uv.Add(new Vector2(along + len, 0));
                for (int q = 0; q < 4; q++) _n.Add(Vector3.up);
                _t.Add(k); _t.Add(k + 1); _t.Add(k + 2); _t.Add(k); _t.Add(k + 2); _t.Add(k + 3);
                along += len;
            }
        }
    }
}
