using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Presents residents, vehicles and deployed equipment from simulation state each frame (read-only).
    /// Greybox meshes for now; the art kit replaces them through <see cref="ActorKit"/> without code changes.
    /// </summary>
    public sealed class ActorsView : MonoBehaviour
    {
        public SimDriver Driver;
        public Material Material;

        MissionDef _def;
        readonly List<Transform> _residents = new List<Transform>();
        readonly List<Transform> _vehicles = new List<Transform>();
        readonly Dictionary<int, Transform> _equipment = new Dictionary<int, Transform>();
        Mesh _residentMesh, _busMesh, _skiffMesh, _crawlerMesh, _cabinMesh;
        ActorKit _kit;
        float _kitScale = 1f;
        readonly List<float> _vehicleYaw = new List<float>();
        Vector3[] _residentPrev;
        Mesh[] _residentVariants;

        static readonly Color[] GroupColors =
        {
            TerrainBuilder.Hex(0x2F6FA8), TerrainBuilder.Hex(0xE0B23A), TerrainBuilder.Hex(0x8E5BA8),
            TerrainBuilder.Hex(0x3A9E8A), TerrainBuilder.Hex(0xD06A8B), TerrainBuilder.Hex(0x7A8F2E)
        };
        public static readonly Color Rescue = TerrainBuilder.Hex(0xFF6B35);

        public void Build(MissionDef d)
        {
            _def = d;
            foreach (Transform child in transform) Destroy(child.gameObject);
            _residents.Clear(); _vehicles.Clear(); _equipment.Clear(); _vehicleYaw.Clear();
            float s = BoardSpace.CellSize(d) / BoardSpace.WideCell; // compact scales down
            _kit = ActorKit.Load();
            _kitScale = s;
            _residentVariants = new Mesh[GroupColors.Length];
            for (int g = 0; g < GroupColors.Length; g++)
                _residentVariants[g] = new MeshKit()
                    .Cylinder(Vector3.zero, 0.0032f * s, 0.022f * s, 8, GroupColors[g])
                    .Sphere(new Vector3(0, 0.027f * s, 0), 0.0045f * s, TerrainBuilder.Hex(0xF1D2B6))
                    .Build("Resident_" + g);
            _busMesh = new MeshKit()
                .Box(new Vector3(0, 0.009f, 0) * s, new Vector3(0.016f, 0.014f, 0.04f) * s, TerrainBuilder.Hex(0xF3EBDD))
                .Box(new Vector3(0, 0.017f, 0) * s, new Vector3(0.0165f, 0.003f, 0.041f) * s, Rescue)
                .Box(new Vector3(0, 0.003f, 0.012f) * s, new Vector3(0.017f, 0.006f, 0.006f) * s, TerrainBuilder.Hex(0x2B2B2B))
                .Box(new Vector3(0, 0.003f, -0.012f) * s, new Vector3(0.017f, 0.006f, 0.006f) * s, TerrainBuilder.Hex(0x2B2B2B))
                .Build("Bus");
            _skiffMesh = new MeshKit()
                .Box(new Vector3(0, 0.004f, 0) * s, new Vector3(0.016f, 0.008f, 0.036f) * s, TerrainBuilder.Hex(0xF3EBDD))
                .Box(new Vector3(0, 0.0085f, 0) * s, new Vector3(0.0168f, 0.002f, 0.037f) * s, Rescue)
                .Box(new Vector3(0, 0.012f, -0.012f) * s, new Vector3(0.012f, 0.008f, 0.008f) * s, TerrainBuilder.Hex(0x132B2A))
                .Build("Skiff");
            _crawlerMesh = new MeshKit()
                .Box(new Vector3(0, 0.008f, 0) * s, new Vector3(0.017f, 0.012f, 0.034f) * s, Rescue)
                .Box(new Vector3(0, 0.016f, -0.004f) * s, new Vector3(0.014f, 0.006f, 0.018f) * s, TerrainBuilder.Hex(0xF3EBDD))
                .Box(new Vector3(0.009f, 0.003f, 0) * s, new Vector3(0.004f, 0.006f, 0.036f) * s, TerrainBuilder.Hex(0x2B2B2B))
                .Box(new Vector3(-0.009f, 0.003f, 0) * s, new Vector3(0.004f, 0.006f, 0.036f) * s, TerrainBuilder.Hex(0x2B2B2B))
                .Build("Crawler");
            _cabinMesh = new MeshKit()
                .Box(new Vector3(0, -0.008f, 0) * s, new Vector3(0.012f, 0.012f, 0.012f) * s, Rescue)
                .Box(new Vector3(0, 0.0005f, 0) * s, new Vector3(0.002f, 0.006f, 0.002f) * s, TerrainBuilder.Hex(0x2B2B2B))
                .Build("WinchCabin");
            _residentPrev = new Vector3[d.ResidentIds.Length];
            for (int r = 0; r < d.ResidentIds.Length; r++)
            {
                var model = _kit != null ? _kit.ResidentFor(r, d.ResidentStepFree[r]) : null;
                if (model == null) { _residents.Add(Spawn("Resident_" + r, _residentVariants[d.ResidentGroup[r] % GroupColors.Length])); continue; }
                var go = ActorKit.Spawn(model, transform, "Resident_" + r);
                go.transform.localScale = Vector3.one * s;
                // group identity stays readable: a thin coloured base under each resident
                var disc = new GameObject("GroupBase");
                disc.transform.SetParent(go.transform, false);
                disc.AddComponent<MeshFilter>().sharedMesh = _groupBase[d.ResidentGroup[r] % GroupColors.Length]
                    ?? (_groupBase[d.ResidentGroup[r] % GroupColors.Length] = new MeshKit()
                        .Cylinder(Vector3.zero, 0.0068f, 0.0008f, 14, GroupColors[d.ResidentGroup[r] % GroupColors.Length]).Build("GroupBase"));
                disc.AddComponent<MeshRenderer>().sharedMaterial = Material;
                _residents.Add(go.transform);
            }
            BuildCables(d);
            BuildCottages(d);
        }

        Transform Spawn(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Material;
            return go.transform;
        }

        void LateUpdate()
        {
            if (Driver == null || Driver.Sim == null || Driver.Def != _def) return;
            var sim = Driver.Sim;
            var S = sim.S;
            float alpha = Driver.Alpha;
            // vehicles
            while (_vehicles.Count < S.C.VehicleCount)
            {
                int v = _vehicles.Count;
                var kind = (VehicleKind)S.VKind[v];
                if (kind == VehicleKind.Skiff && _kit != null && _kit.Skiff != null)
                {
                    var go = ActorKit.Spawn(_kit.Skiff, transform, "Vehicle_" + v);
                    go.transform.localScale = Vector3.one * _kitScale;
                    _vehicles.Add(go.transform);
                    _vehicleYaw.Add(_kit.SkiffYaw);
                    continue;
                }
                var mesh = kind == VehicleKind.Skiff ? _skiffMesh : kind == VehicleKind.Crawler ? _crawlerMesh
                         : kind == VehicleKind.Winch ? _cabinMesh : _busMesh;
                _vehicles.Add(Spawn("Vehicle_" + v, mesh));
                _vehicleYaw.Add(0f);
            }
            for (int v = 0; v < S.C.VehicleCount; v++)
            {
                Vector3 pos, fwd;
                bool onWater = (VehicleKind)S.VKind[v] == VehicleKind.Skiff;
                if (S.VState[v] == (byte)VehicleState.Moving)
                    EdgePose(S.VEdge[v], S.VFrom[v], S.VNode[v], (S.VProgress[v] + (S.VBrake[v] > 0 ? 0 : alpha)) / Mathf.Max(1, S.VDuration[v]), onWater, out pos, out fwd);
                else { pos = NodePos(S.VNode[v], onWater); fwd = Vector3.forward; }
                if ((VehicleKind)S.VKind[v] == VehicleKind.Winch) { pos.y += CableHeight; fwd = Vector3.zero; }
                var t = _vehicles[v];
                t.localPosition = pos;
                if (fwd.sqrMagnitude > 1e-6f)
                    t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0, _vehicleYaw[v], 0), 0.25f);
                t.gameObject.SetActive(S.VState[v] != (byte)VehicleState.Failed || true);
            }
            // residents
            var slot = _slot; slot.Clear(); // node -> residents drawn there this frame
            // a parked vehicle owns the node centre; people stand around it, not on it
            for (int v = 0; v < S.C.VehicleCount; v++)
                if (S.VState[v] != (byte)VehicleState.Moving && (VehicleKind)S.VKind[v] != VehicleKind.Skiff && S.VNode[v] >= 0)
                    slot[S.VNode[v]] = 3;
            for (int r = 0; r < _residents.Count; r++)
            {
                var st = (ResidentState)S.RState[r];
                var t = _residents[r];
                bool visible = st != ResidentState.Aboard;
                t.gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 pos;
                if (st == ResidentState.Walking)
                {
                    EdgePose(S.REdge[r], S.RFrom[r], S.RNode[r], (S.RProgress[r] + alpha) / Mathf.Max(1, S.RDuration[r]), false, out pos, out _);
                }
                else
                {
                    int node = S.RNode[r];
                    slot.TryGetValue(node, out int k);
                    slot[node] = k + 1;
                    pos = NodePos(node, false) + RingOffset(k);
                }
                if (st == ResidentState.Evacuated) pos.y += Mathf.Abs(Mathf.Sin(Time.time * 4f + r)) * 0.002f; // gentle celebration
                if (st == ResidentState.Walking) pos.y += Mathf.Abs(Mathf.Sin(Time.time * 14f + r)) * 0.0008f; // walking bob
                // face the way they move (kit models), easing so groups don't snap
                if (_kit != null)
                {
                    var delta = pos - _residentPrev[r]; delta.y = 0;
                    if (delta.sqrMagnitude > 1e-9f)
                    {
                        var target = Quaternion.LookRotation(delta.normalized, Vector3.up) * Quaternion.Euler(0, _kit.ResidentYaw, 0);
                        t.localRotation = Quaternion.Slerp(t.localRotation, target, 0.2f);
                    }
                    _residentPrev[r] = pos;
                }
                t.localPosition = pos;
            }
            SyncEquipment(sim);
        }

        Vector3 RingOffset(int k)
        {
            float s = BoardSpace.CellSize(_def);
            if (k == 0) return Vector3.zero;
            float ang = k * 2.39996f; // golden angle spiral keeps groups compact
            float rad = s * (_kit != null ? 0.52f : 0.22f) * Mathf.Sqrt(k); // kit residents are ~13 mm wide
            return new Vector3(Mathf.Cos(ang) * rad, 0, Mathf.Sin(ang) * rad);
        }

        public const float CableHeight = 0.035f;

        /// <summary>Cable edges are drawn as a taut line between their stations, raised above the ground.</summary>
        void BuildCables(MissionDef d)
        {
            var kit = new MeshKit();
            bool any = false;
            for (int e = 0; e < d.EdgeIds.Length; e++)
            {
                if (d.EdgeClass[e] != Traversal.Cable) continue;
                var a = BoardSpace.CellCenter(d, d.NodeCell[d.EdgeFrom[e]]) + Vector3.up * CableHeight;
                var b = BoardSpace.CellCenter(d, d.NodeCell[d.EdgeTo[e]]) + Vector3.up * CableHeight;
                kit.Segment(a, b, 0.0007f, TerrainBuilder.Hex(0x2B2B2B));
                kit.Cylinder(a - Vector3.up * CableHeight, 0.0012f, CableHeight, 6, TerrainBuilder.Hex(0x8C8577));
                kit.Cylinder(b - Vector3.up * CableHeight, 0.0012f, CableHeight, 6, TerrainBuilder.Hex(0x8C8577));
                any = true;
            }
            if (any) Spawn("Cables", kit.Build("Cables"));
        }

        Vector3 NodePos(int node, bool onWater)
        {
            var d = _def;
            if (onWater && d.NodeWaterCell[node] < 0) onWater = false;
            int cell = onWater ? d.NodeWaterCell[node] : d.NodeCell[node];
            var p = BoardSpace.CellCenter(d, cell);
            if (onWater) p.y = WaterSurface(cell);
            return p;
        }

        float WaterSurface(int cell)
        {
            var d = _def;
            long vol = Driver.Sim.S.Volume[cell];
            return BoardSpace.Height(d, d.CellElevation[cell] + vol / d.WArea[cell]);
        }

        /// <summary>Position along an edge polyline (node → sampled cells → node), t in 0..1.</summary>
        void EdgePose(int edge, int fromNode, int toNode, float t, bool vehicle, out Vector3 pos, out Vector3 fwd)
        {
            var d = _def;
            bool water = d.EdgeClass[edge] == Traversal.Water;
            if (d.EdgeClass[edge] == Traversal.Cable)
            {
                // straight span between stations
                var a = NodePos(fromNode, false); var b = NodePos(toNode, false);
                pos = Vector3.Lerp(a, b, Mathf.Clamp01(t)); fwd = b - a; fwd.y = 0;
                return;
            }
            var pts = _scratchPts;
            pts.Clear();
            pts.Add(NodePos(fromNode, water));
            var cells = d.EdgeSampleCells[edge];
            bool forward = d.EdgeFrom[edge] == fromNode;
            if (cells != null && cells.Length > 0)
            {
                // order sampled cells from the 'from' node outwards
                for (int i = 0; i < cells.Length; i++)
                {
                    int c = cells[forward ? i : cells.Length - 1 - i];
                    var p = BoardSpace.CellCenter(d, c);
                    p.y = water ? WaterSurface(c) : Mathf.Max(p.y, BoardSpace.Height(d, 300));
                    pts.Add(p);
                }
            }
            pts.Add(NodePos(toNode, water));
            float total = 0;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            float target = Mathf.Clamp01(t) * total;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (target <= seg || i == pts.Count - 1)
                {
                    pos = Vector3.Lerp(pts[i - 1], pts[i], seg > 1e-6f ? target / seg : 1f);
                    fwd = pts[i] - pts[i - 1]; fwd.y = 0;
                    return;
                }
                target -= seg;
            }
            pos = pts[pts.Count - 1]; fwd = Vector3.forward;
        }

        readonly List<Vector3> _scratchPts = new List<Vector3>(32);
        readonly Mesh[] _groupBase = new Mesh[6];

        /// <summary>
        /// Decorative homes beside each group's starting point (presentation only). A cottage takes a 4×3-cell flat
        /// block at or above the start node's elevation (a home on the slope above its people is fine) that touches no
        /// node, path, socket, water or hazard cell, and faces the node.
        /// </summary>
        void BuildCottages(MissionDef d)
        {
            if (_kit == null || _kit.Cottage == null) return;
            int w = d.GridW, h = d.GridH, n = w * h;
            var blocked = new bool[n];
            // nodes keep a one-cell margin: people waiting there stand around the node, not inside a wall
            for (int i = 0; i < d.NodeIds.Length; i++)
            {
                int nc = d.NodeCell[i];
                if (nc < 0) continue;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = d.CellX(nc) + ox, y = d.CellY(nc) + oy;
                        if (x >= 0 && y >= 0 && x < w && y < h) blocked[d.CellIndex(x, y)] = true;
                    }
            }
            for (int e = 0; e < d.EdgeIds.Length; e++) if (d.EdgeSampleCells[e] != null) foreach (int c in d.EdgeSampleCells[e]) if (c < n) blocked[c] = true;
            for (int s = 0; s < d.SocketIds.Length; s++)
            {
                if (d.SocketCell[s] >= 0) blocked[d.SocketCell[s]] = true;
                if (d.SocketCells[s] != null) foreach (int c in d.SocketCells[s]) blocked[c] = true;
                if (d.SocketPlatformCells[s] != null) foreach (int c in d.SocketPlatformCells[s]) blocked[c] = true;
            }
            for (int c = 0; c < n; c++)
            {
                if (d.WInitVol[c] > 0) blocked[c] = true;
                if (d.CellLavaSeg != null && d.CellLavaSeg[c] >= 0) blocked[c] = true;
            }
            foreach (int c in d.SeaCell) blocked[c] = true;
            foreach (var cells in d.SegmentCells) foreach (int c in cells) if (c < n) blocked[c] = true;
            for (int i = 0; i < d.SourceNode.Length; i++) blocked[d.SourceNode[i]] = true;

            int placed = 0;
            for (int g = 0; g < d.GroupIds.Length && placed < _kit.MaxCottages; g++)
            {
                int start = d.NodeCell[d.GroupStart[g]];
                if (start < 0) continue;
                long elev = d.CellElevation[start];
                int sx = d.CellX(start), sy = d.CellY(start);
                bool done = false;
                for (int radius = 1; radius <= 6 && !done; radius++)
                    for (int oy = -radius; oy <= radius && !done; oy++)
                        for (int ox = -radius; ox <= radius && !done; ox++)
                        {
                            if (Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oy)) != radius) continue;
                            foreach (bool wide in new[] { true, false })
                            {
                                int bw = wide ? 4 : 3, bh = wide ? 3 : 4;
                                int x0 = sx + ox - bw / 2, y0 = sy + oy - bh / 2;
                                if (x0 < 0 || y0 < 0 || x0 + bw > w || y0 + bh > h) continue;
                                bool ok = true;
                                long blockElev = d.CellElevation[d.CellIndex(x0, y0)];
                                if (blockElev < elev) continue;
                                for (int y = y0; y < y0 + bh && ok; y++)
                                    for (int x = x0; x < x0 + bw && ok; x++)
                                    {
                                        int c = d.CellIndex(x, y);
                                        if (blocked[c] || d.CellElevation[c] != blockElev) ok = false;
                                    }
                                if (!ok) continue;
                                for (int y = y0; y < y0 + bh; y++) for (int x = x0; x < x0 + bw; x++) blocked[d.CellIndex(x, y)] = true;
                                float cs = BoardSpace.CellSize(d);
                                var centre = new Vector3((x0 + bw * 0.5f - w * 0.5f) * cs, BoardSpace.Height(d, blockElev), (y0 + bh * 0.5f - h * 0.5f) * cs);
                                var toNode = BoardSpace.CellCenter(d, start) - centre; toNode.y = 0;
                                // front faces the node, snapped to the axis the footprint allows
                                float yaw = wide ? (toNode.z >= 0 ? 0f : 180f) : (toNode.x >= 0 ? 90f : -90f);
                                var go = ActorKit.Spawn(_kit.Cottage, transform, "Cottage_" + d.GroupIds[g]);
                                go.transform.localPosition = centre;
                                go.transform.localRotation = Quaternion.Euler(0, yaw + _kit.CottageYaw, 0);
                                go.transform.localScale = Vector3.one * _kitScale;
                                placed++; done = true;
                                break;
                            }
                        }
            }
        }
        readonly Dictionary<int, int> _slot = new Dictionary<int, int>();

        void SyncEquipment(Simulator sim)
        {
            var S = sim.S;
            for (int inst = 0; inst < S.C.EquipmentCount; inst++)
            {
                var tool = (ToolKind)S.EqKind[inst];
                if (tool == ToolKind.Skiff || tool == ToolKind.Crawler) continue; // shown as vehicles
                int sock = S.EqSocket[inst];
                bool kitBridge = tool == ToolKind.Bridge && _kit != null && _kit.Bridge != null && _def.SocketEdge[sock] >= 0;
                if (!_equipment.TryGetValue(inst, out var t))
                {
                    t = kitBridge ? ActorKit.Spawn(_kit.Bridge, transform, $"Equipment_{inst}_{tool}").transform
                                  : Spawn($"Equipment_{inst}_{tool}", EquipmentMesh(tool, sock));
                    _equipment[inst] = t;
                }
                if (kitBridge) { PoseKitBridge(t, sock, S.EqBroken[inst]); continue; }
                t.localPosition = SocketPos(sock);
                t.localRotation = SocketRotation(sock);
                t.localScale = S.EqBroken[inst] ? new Vector3(1, 0.35f, 1) : Vector3.one;
            }
        }

        /// <summary>
        /// The kit bridge's deck (origin = walking surface, length along local X) spans its edge between the two
        /// bridgeheads at bank height; it is stretched only along its length (to 0.5–1.0 of authored) to suit the gap.
        /// </summary>
        void PoseKitBridge(Transform t, int sock, bool broken)
        {
            var d = _def;
            int e = d.SocketEdge[sock];
            int a = d.NodeCell[d.EdgeFrom[e]], b = d.NodeCell[d.EdgeTo[e]];
            var pa = BoardSpace.CellCenter(d, a); var pb = BoardSpace.CellCenter(d, b);
            var dir = pb - pa; dir.y = 0;
            float span = dir.magnitude;
            float bank = BoardSpace.Height(d, System.Math.Max(d.CellElevation[a], d.CellElevation[b]));
            var mid = (pa + pb) * 0.5f; mid.y = bank;
            t.localPosition = mid;
            t.localRotation = dir.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.right, dir.normalized) : Quaternion.identity;
            float stretch = Mathf.Clamp((span + BoardSpace.CellSize(d) * 2f) / (_kit.BridgeLength * _kitScale), 0.5f, 1f);
            t.localScale = new Vector3(stretch, broken ? 0.35f : 1f, 1f) * _kitScale;
        }

        public Transform EquipmentTransform(int inst) => _equipment.TryGetValue(inst, out var t) ? t : null;

        public Vector3 SocketPos(int sock)
        {
            var d = _def;
            int cell = d.SocketCell[sock] >= 0 ? d.SocketCell[sock] : (d.SocketNode[sock] >= 0 ? d.NodeCell[d.SocketNode[sock]] : 0);
            var p = BoardSpace.CellCenter(d, cell);
            if (d.SocketKind[sock] == ToolKind.Barrier && d.SocketLinks[sock].Length > 0)
            {
                // centre of the link boundaries the barrier spans
                Vector3 sum = Vector3.zero;
                foreach (int l in d.SocketLinks[sock])
                    sum += (BoardSpace.CellCenter(d, d.LinkFrom[l], 0) + BoardSpace.CellCenter(d, d.LinkTo[l], 0)) * 0.5f;
                p = sum / d.SocketLinks[sock].Length;
                p.y = 0;
            }
            if (d.SocketKind[sock] == ToolKind.Bridge) p.y = BoardSpace.Height(d, 300);
            return p;
        }

        public Quaternion SocketRotation(int sock)
        {
            var d = _def;
            if (d.SocketKind[sock] == ToolKind.Barrier && d.SocketLinks[sock].Length > 0)
            {
                int l = d.SocketLinks[sock][0];
                bool horizontalLink = d.CellY(d.LinkFrom[l]) == d.CellY(d.LinkTo[l]);
                return horizontalLink ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
            }
            if (d.SocketKind[sock] == ToolKind.Bridge && d.SocketEdge[sock] >= 0)
            {
                int e = d.SocketEdge[sock];
                var a = BoardSpace.CellCenter(d, d.NodeCell[d.EdgeFrom[e]]);
                var b = BoardSpace.CellCenter(d, d.NodeCell[d.EdgeTo[e]]);
                var dir = b - a; dir.y = 0;
                if (dir.sqrMagnitude > 1e-8f) return Quaternion.LookRotation(dir, Vector3.up);
            }
            return Quaternion.identity;
        }

        Mesh EquipmentMesh(ToolKind tool, int sock)
        {
            var d = _def;
            float c = BoardSpace.CellSize(d);
            var kit = new MeshKit();
            switch (tool)
            {
                case ToolKind.Bridge:
                {
                    float len = c * 3.2f;
                    kit.Box(new Vector3(0, 0.002f, 0), new Vector3(c * 0.8f, 0.004f, len), Rescue)
                       .Box(new Vector3(c * 0.42f, 0.006f, 0), new Vector3(0.0015f, 0.006f, len), TerrainBuilder.Hex(0xF3EBDD))
                       .Box(new Vector3(-c * 0.42f, 0.006f, 0), new Vector3(0.0015f, 0.006f, len), TerrainBuilder.Hex(0xF3EBDD));
                    break;
                }
                case ToolKind.Barrier:
                {
                    int n = Mathf.Max(1, d.SocketLinks[sock].Length);
                    float h = BoardSpace.Height(d, 1100);
                    kit.Box(new Vector3(0, h * 0.5f, 0), new Vector3(0.004f, h, c * n), Rescue)
                       .Box(new Vector3(0, h + 0.001f, 0), new Vector3(0.006f, 0.002f, c * n), TerrainBuilder.Hex(0xF3EBDD));
                    break;
                }
                case ToolKind.Pump:
                    kit.Box(new Vector3(0, 0.006f, 0), new Vector3(c * 0.7f, 0.012f, c * 0.7f), Rescue)
                       .Cylinder(new Vector3(0, 0.012f, 0), 0.002f, 0.006f, 6, TerrainBuilder.Hex(0x2B2B2B));
                    break;
                case ToolKind.Generator:
                    kit.Box(new Vector3(0, 0.007f, 0), new Vector3(c * 0.8f, 0.014f, c * 0.6f), Rescue)
                       .Box(new Vector3(0, 0.0155f, 0), new Vector3(c * 0.5f, 0.003f, c * 0.3f), TerrainBuilder.Hex(0xF3EBDD));
                    break;
                case ToolKind.Firebreak:
                {
                    // a cleared strip: one low dark slab per target cell, centred on the socket cell
                    var at = BoardSpace.CellCenter(d, Mathf.Max(0, d.SocketCell[sock]));
                    foreach (int cell in d.SocketCells[sock])
                    {
                        var p = BoardSpace.CellCenter(d, cell) - at;
                        kit.Box(new Vector3(p.x, 0.0012f, p.z), new Vector3(c * 0.9f, 0.0024f, c * 0.9f), TerrainBuilder.Hex(0x6B5440));
                    }
                    break;
                }
                case ToolKind.Sprinkler:
                    kit.Cylinder(Vector3.zero, 0.001f, 0.022f, 6, TerrainBuilder.Hex(0xF3EBDD))
                       .Cylinder(new Vector3(0, 0.022f, 0), c * 0.3f, 0.002f, 10, TerrainBuilder.Hex(0x5AA9E6))
                       .Box(new Vector3(0, 0.003f, 0), new Vector3(c * 0.5f, 0.006f, c * 0.5f), Rescue);
                    break;
                case ToolKind.Brace:
                    kit.Segment(new Vector3(-c * 0.35f, 0, 0), new Vector3(0, 0.028f, 0), 0.0016f, Rescue)
                       .Segment(new Vector3(c * 0.35f, 0, 0), new Vector3(0, 0.028f, 0), 0.0016f, Rescue)
                       .Box(new Vector3(0, 0.0015f, 0), new Vector3(c * 0.8f, 0.003f, 0.003f), TerrainBuilder.Hex(0x8C8577));
                    break;
                case ToolKind.Winch:
                    kit.Box(new Vector3(0, 0.006f, 0), new Vector3(c * 0.7f, 0.012f, c * 0.5f), Rescue)
                       .Cylinder(new Vector3(0, 0.012f, 0), c * 0.18f, 0.008f, 10, TerrainBuilder.Hex(0x2B2B2B));
                    break;
                case ToolKind.Relay:
                    kit.Cylinder(Vector3.zero, 0.0012f, 0.034f, 6, TerrainBuilder.Hex(0xF3EBDD))
                       .Box(new Vector3(0, 0.034f, 0), new Vector3(0.012f, 0.003f, 0.003f), Rescue)
                       .Box(new Vector3(0, 0.002f, 0), new Vector3(c * 0.5f, 0.004f, c * 0.5f), Rescue);
                    break;
                case ToolKind.Cooler:
                    kit.Cylinder(Vector3.zero, c * 0.28f, 0.016f, 12, TerrainBuilder.Hex(0x5AA9E6))
                       .Cylinder(new Vector3(0, 0.016f, 0), c * 0.3f, 0.003f, 12, TerrainBuilder.Hex(0xF3EBDD));
                    break;
                case ToolKind.Shelter:
                    kit.Box(new Vector3(0, 0.007f, 0), new Vector3(c * 0.9f, 0.014f, c * 0.9f), TerrainBuilder.Hex(0xF3EBDD))
                       .Box(new Vector3(0, 0.0155f, 0), new Vector3(c * 1.0f, 0.003f, c * 1.0f), Rescue);
                    break;
                case ToolKind.Beacon:
                    kit.Cylinder(Vector3.zero, 0.001f, 0.03f, 6, TerrainBuilder.Hex(0xF3EBDD))
                       .Box(new Vector3(0.004f, 0.026f, 0), new Vector3(0.008f, 0.006f, 0.001f), Rescue);
                    break;
                default:
                    kit.Box(new Vector3(0, 0.006f, 0), new Vector3(c * 0.6f, 0.012f, c * 0.6f), Rescue);
                    break;
            }
            return kit.Build(tool.ToString());
        }
    }
}
