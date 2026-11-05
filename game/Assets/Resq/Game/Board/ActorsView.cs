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
        /// <summary>Cells under decorative cottages (scatter keeps clear of them).</summary>
        public readonly HashSet<int> CottageCells = new HashSet<int>();
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
            _crewPending.Clear();
            _crew.Clear(); _crewParts.Clear(); _crowds.Clear(); _crowdList.Clear(); _parts = null;
            _residents.Clear(); _vehicles.Clear(); _equipment.Clear(); _vehicleYaw.Clear(); CottageCells.Clear();
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
            _anims = new Animator[d.ResidentIds.Length];
            _childHeads = new Transform[d.ResidentIds.Length];
            _mode = new byte[d.ResidentIds.Length];
            for (int r = 0; r < d.ResidentIds.Length; r++)
            {
                if (d.ResidentStepFree[r] && _kit != null && _kit.SeatedRider != null && _kit.WheelchairChair != null)
                {
                    _residents.Add(SpawnWheelchair(r, s, d.ResidentGroup[r]));
                    continue;
                }
                var model = _kit != null ? _kit.ResidentFor(r, d.ResidentStepFree[r]) : null;
                if (model == null) { _residents.Add(Spawn("Resident_" + r, _residentVariants[d.ResidentGroup[r] % GroupColors.Length])); continue; }
                var go = ActorKit.Spawn(model, transform, "Resident_" + r);
                go.transform.localScale = Vector3.one * s;
                RigResident(go, model, r, d.ResidentStepFree[r]);
                // group identity stays readable: a thin coloured base under each resident
                var disc = new GameObject("GroupBase");
                disc.transform.SetParent(go.transform, false);
                disc.AddComponent<MeshFilter>().sharedMesh = _groupBase[d.ResidentGroup[r] % GroupColors.Length]
                    ?? (_groupBase[d.ResidentGroup[r] % GroupColors.Length] = new MeshKit()
                        .Cylinder(Vector3.zero, 0.0068f, 0.0008f, 14, GroupColors[d.ResidentGroup[r] % GroupColors.Length]).Build("GroupBase"));
                disc.AddComponent<MeshRenderer>().sharedMaterial = Material;
                _residents.Add(go.transform);
                if (_anims[r] != null)   // rigged: record parts once the group base exists
                {
                    _kit.RigFor(model, out var rmat, out _);
                    RecordParts(r, go, model.name + (_kit.IsDerivedChild(r, d.ResidentStepFree[r]) ? "_child" : ""), rmat);
                }
            }
            BuildCables(d);
            BuildCottages(d);
            BuildCrew(d);
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
            foreach (var c in _crowdList) c.M.Clear();
            if (Driver == null || Driver.Sim == null || Driver.Def != _def) return;
            var sim = Driver.Sim;
            var S = sim.S;
            float alpha = Driver.Alpha;
            // vehicles
            while (_vehicles.Count < S.C.VehicleCount)
            {
                int v = _vehicles.Count;
                var kind = (VehicleKind)S.VKind[v];
                var heroKit = HeroKit.Load();
                var heroBody = heroKit == null ? null : heroKit.Get(kind == VehicleKind.FixedBus ? heroKit.Bus : kind == VehicleKind.Skiff ? heroKit.Skiff
                                                                   : kind == VehicleKind.Crawler ? heroKit.Crawler : null);
                if (heroBody != null)
                {
                    var go = HeroKit.Spawn(heroBody, transform, "Vehicle_" + v);
                    go.transform.localScale = Vector3.one * _kitScale;
                    _vehicles.Add(go.transform);
                    _vehicleYaw.Add(heroBody.Yaw);
                    continue;
                }
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
            _leaders.Clear();                 // nodes whose first standing resident is animated (one per group)
            var viewer = Camera.main != null ? Camera.main.transform : null;
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
                bool walking = st == ResidentState.Walking;
                // one figure per group moves: it waves for help where the group waits, or cheers once safe; the
                // rest hold the shared baked pose (instanced, so a crowd stays a handful of draws)
                byte mode = walking ? ModeWalk : ModeStand;
                if (Celebrate && st == ResidentState.Evacuated) mode = ModeCheer;   // aftercare: everyone home cheers
                else if (!walking && _leaders.Add(S.RNode[r]))
                {
                    bool safe = st == ResidentState.Evacuated || (_def.NodeFlags[S.RNode[r]] & NodeFlags.Evacuation) != 0;
                    mode = safe ? ModeCheer : ModeWave;
                }
                AnimateResident(r, ref mode);
                bool animated = mode != ModeStand;
                if (_anims[r] != null && _anims[r].enabled != animated) _anims[r].enabled = animated;   // standing = baked pose
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
                    else if (viewer != null)
                    {
                        // standing still, they turn to look up at the player, each at its own pace (BUILD_PLAN P4.2)
                        var toViewer = transform.InverseTransformPoint(viewer.position) - pos; toViewer.y = 0;
                        if (toViewer.sqrMagnitude > 1e-4f)
                        {
                            var target = Quaternion.LookRotation(toViewer.normalized, Vector3.up) * Quaternion.Euler(0, _kit.ResidentYaw, 0);
                            t.localRotation = Quaternion.RotateTowards(t.localRotation, target, (35f + (r % 5) * 14f) * Time.deltaTime);
                        }
                    }
                    _residentPrev[r] = pos;
                }
                t.localPosition = pos;
                QueueResident(r, animated);
            }
            SyncEquipment(sim);
            LateUpdateChildHeads();
            FitCrewHelmets();
            QueueCrew();
            DrawCrowds();
        }

        /// <summary>
        /// Rescue crew at every evacuation landing (Look 3 crowds): presentation-only figures in a hi-vis crew
        /// uniform with white helmets and no group base, so they never read as people to rescue. They stand a cell
        /// out from the landing, in the gaps between arrival paths, facing the landing where rescued residents gather.
        /// </summary>
        void BuildCrew(MissionDef d)
        {
            if (_kit == null || _kit.ResidentAdultB == null || _kit.AdultBAnim == null) return;
            if (_crewMat == null) _crewMat = _kit.CrewMaterial;
            if (_crewMat == null && _kit.AdultBMaterial != null)
            {
                _crewMat = new Material(_kit.AdultBMaterial) { name = "CrewUniform" };
                _crewMat.SetColor("_Tint", new Color(1f, 0.6f, 0.32f, 1f));
            }
            var helmet = new MeshKit().Sphere(Vector3.zero, 1f, TerrainBuilder.Hex(0xF4F1EA)).Build("Helmet");
            float cs = BoardSpace.CellSize(d);
            int n = 0;
            for (int i = 0; i < d.NodeIds.Length; i++)
            {
                if ((d.NodeFlags[i] & NodeFlags.Evacuation) == 0 || d.NodeCell[i] < 0) continue;
                var centre = BoardSpace.CellCenter(d, d.NodeCell[i]);
                // arrival directions from this landing to its neighbours
                var dirs = new List<float>();
                foreach (int e in d.NodeOutEdges[i])
                {
                    int other = d.EdgeFrom[e] == i ? d.EdgeTo[e] : d.EdgeFrom[e];
                    if (other < 0 || d.NodeCell[other] < 0) continue;
                    var v = BoardSpace.CellCenter(d, d.NodeCell[other]) - centre;
                    dirs.Add(Mathf.Atan2(v.z, v.x));
                }
                var taken = new List<float>(dirs);
                for (int k = 0; k < 3; k++)
                {
                    float best = 0, bestGap = -1;
                    for (int c = 0; c < 24; c++)
                    {
                        float a = c * Mathf.PI * 2f / 24f, gap = Mathf.PI;
                        foreach (float t in taken) gap = Mathf.Min(gap, Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, t * Mathf.Rad2Deg)) * Mathf.Deg2Rad);
                        if (gap > bestGap) { bestGap = gap; best = a; }
                    }
                    taken.Add(best);
                    var pos = centre + new Vector3(Mathf.Cos(best), 0, Mathf.Sin(best)) * cs * 1.05f;
                    pos.y = TerrainBuilder.HeightAt(d, pos);
                    var go = ActorKit.Spawn(_kit.ResidentAdultB, transform, $"Crew_{d.NodeIds[i]}_{k}");
                    go.transform.localPosition = pos;
                    var look = centre - pos; look.y = 0;
                    go.transform.localRotation = Quaternion.LookRotation(look.normalized, Vector3.up) * Quaternion.Euler(0, _kit.ResidentYaw, 0);
                    go.transform.localScale = Vector3.one * _kitScale;
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    { smr.sharedMaterial = _crewMat; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                    var an = go.GetComponent<Animator>(); if (an == null) an = go.AddComponent<Animator>();   // Unity null: no ?? on components
                    an.runtimeAnimatorController = _kit.AdultBAnim;
                    an.applyRootMotion = false;
                    an.speed = 0f;
                    an.Play(0, 0, _kit.IdleTime);
                    _crewPending.Add((go.transform, helmet));
                    _crew.Add(go.transform);
                    n++;
                }
            }
            CrewCount = n;
        }

        Material _crewMat;
        readonly List<(Transform, Mesh)> _crewPending = new List<(Transform, Mesh)>();
        public int CrewCount { get; private set; }

        /// <summary>Helmets go on after the first animated pose so they sit on the posed head.</summary>
        void FitCrewHelmets()
        {
            if (_crewPending.Count == 0) return;
            foreach (var (root, mesh) in _crewPending)
            {
                Transform head = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") { head = t; break; }
                if (head == null) continue;
                var h = new GameObject("Helmet");
                h.transform.SetParent(head, false);
                h.AddComponent<MeshFilter>().sharedMesh = mesh;
                h.AddComponent<MeshRenderer>().sharedMaterial = Material;
                float radius = 0.0027f * _kitScale;
                // the Head bone sits at the base of the skull: the helmet crowns it ~4.6 mm higher on a 35 mm figure
                h.transform.position = head.position + root.up * (0.0046f * _kitScale);
                h.transform.localScale = new Vector3(radius, radius * 0.7f, radius) / Mathf.Max(1e-6f, head.lossyScale.x);
            }
            _crewPending.Clear();
        }

        /// <summary>Step-free resident: the kit chair with the seated rig in it, on the group's coloured base.</summary>
        Transform SpawnWheelchair(int r, float s, int group)
        {
            var root = new GameObject("Resident_" + r);
            root.transform.SetParent(transform, false);
            root.transform.localScale = Vector3.one * s;
            ActorKit.Spawn(_kit.WheelchairChair, root.transform, "Chair");
            var rider = ActorKit.Spawn(_kit.SeatedRider, root.transform, "Rider");
            _kit.RigFor(_kit.SeatedRider, out var mat, out var ctl);
            foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { if (mat != null) smr.sharedMaterial = mat; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            var an = rider.GetComponent<Animator>(); if (an == null) an = rider.AddComponent<Animator>();
            an.runtimeAnimatorController = ctl;
            an.applyRootMotion = false;
            an.speed = 0f;                       // the sit clip is a single held pose; the chair rolls, the rider doesn't walk
            an.Play(0, 0, 0f);
            var disc = new GameObject("GroupBase");
            disc.transform.SetParent(root.transform, false);
            disc.AddComponent<MeshFilter>().sharedMesh = _groupBase[group % GroupColors.Length]
                ?? (_groupBase[group % GroupColors.Length] = new MeshKit()
                    .Cylinder(Vector3.zero, 0.0068f, 0.0008f, 14, GroupColors[group % GroupColors.Length]).Build("GroupBase"));
            disc.AddComponent<MeshRenderer>().sharedMaterial = Material;
            _anims[r] = null;   // the seated rider never walks: always drawn as its baked pose
            RecordParts(r, root, "seated", mat);
            return root.transform;
        }

        // ------------------------------------------------------------------ instanced crowds (draw-call budget)
        // A standing figure is the same baked pose for every resident of its model, so standing residents, seated
        // riders, crew, group bases, chairs and helmets are drawn instanced: one draw per (mesh, material) instead
        // of one or two per person. Walking residents switch back to their animated skinned renderer.
        sealed class Crowd { public Mesh Mesh; public Material Mat; public readonly List<Matrix4x4> M = new List<Matrix4x4>(); }
        sealed class Parts
        {
            public SkinnedMeshRenderer Smr; public Mesh Baked; public Material SmrMat;
            public readonly List<(Renderer r, Crowd c)> Statics = new List<(Renderer, Crowd)>();
            public Crowd Pose;
        }
        readonly Dictionary<(Mesh, Material), Crowd> _crowds = new Dictionary<(Mesh, Material), Crowd>();
        readonly List<Crowd> _crowdList = new List<Crowd>();
        readonly Dictionary<string, Mesh> _bakedPoses = new Dictionary<string, Mesh>();
        readonly List<Transform> _crew = new List<Transform>();
        readonly List<Parts> _crewParts = new List<Parts>();
        Parts[] _parts;
        Matrix4x4[] _drawBuf = new Matrix4x4[256];

        Crowd CrowdFor(Mesh m, Material mat)
        {
            if (!_crowds.TryGetValue((m, mat), out var c))
            {
                c = new Crowd { Mesh = m, Mat = mat };
                if (mat != null && !mat.enableInstancing) mat.enableInstancing = true;
                _crowds[(m, mat)] = c; _crowdList.Add(c);
            }
            return c;
        }

        /// <summary>Bakes (once per model variant) the standing pose and collects the figure's static parts.</summary>
        Parts BuildParts(GameObject go, string poseKey, Material mat)
        {
            var p = new Parts();
            p.Smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (p.Smr != null && p.Smr.sharedMesh != null && p.Smr.sharedMesh.isReadable)
            {
                if (!_bakedPoses.TryGetValue(poseKey, out var baked))
                {
                    var an = p.Smr.GetComponentInParent<Animator>();
                    if (an != null) { an.Update(0f); }
                    if (poseKey.EndsWith("_child"))
                        foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") t.localScale = Vector3.one * _kit.ChildHead;
                    baked = new Mesh { name = "Pose_" + poseKey };
                    p.Smr.BakeMesh(baked, false);
                    _bakedPoses[poseKey] = baked;
                }
                p.Baked = baked;
                p.SmrMat = mat != null ? mat : p.Smr.sharedMaterial;
                p.Pose = CrowdFor(p.Baked, p.SmrMat);
            }
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                p.Statics.Add((mr, CrowdFor(mf.sharedMesh, mr.sharedMaterial)));
                mr.enabled = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return p;
        }

        void RecordParts(int r, GameObject go, string poseKey, Material mat)
        {
            if (_parts == null) _parts = new Parts[_def.ResidentIds.Length];
            _parts[r] = BuildParts(go, poseKey, mat);
        }

        /// <summary>BakeMesh(useScale: false) leaves vertices at world size relative to the renderer: position and rotation only (verified 23 Oct).</summary>
        static Matrix4x4 PoseMatrix(Transform smr) => Matrix4x4.TRS(smr.position, smr.rotation, Vector3.one);

        void QueueResident(int r, bool animated)
        {
            var p = _parts != null ? _parts[r] : null;
            if (p == null) return;
            bool baked = !animated && p.Pose != null;
            if (p.Smr != null && p.Smr.enabled == baked) p.Smr.enabled = !baked;
            if (baked) p.Pose.M.Add(PoseMatrix(p.Smr.transform));
            foreach (var (rend, c) in p.Statics) c.M.Add(rend.transform.localToWorldMatrix);
        }

        void QueueCrew()
        {
            // crew parts are recorded once their helmets are on (FitCrewHelmets runs after the first posed frame)
            if (_crewParts.Count == 0 && _crew.Count > 0 && _crewPending.Count == 0)
            {
                Material crewMat = _crewMat;
                foreach (var t in _crew) _crewParts.Add(BuildParts(t.gameObject, _kit.ResidentAdultB.name, crewMat));
            }
            foreach (var p in _crewParts)
            {
                if (p.Smr != null && p.Pose != null) { if (p.Smr.enabled) p.Smr.enabled = false; p.Pose.M.Add(PoseMatrix(p.Smr.transform)); }
                foreach (var (rend, c) in p.Statics) c.M.Add(rend.transform.localToWorldMatrix);
            }
        }

        void DrawCrowds()
        {
            var bounds = new Bounds(transform.position, Vector3.one * 4f);
            foreach (var c in _crowdList)
            {
                int n = c.M.Count;
                if (n == 0 || c.Mesh == null || c.Mat == null) continue;
                if (_drawBuf.Length < n) _drawBuf = new Matrix4x4[Mathf.NextPowerOfTwo(n)];
                c.M.CopyTo(_drawBuf);
                var rp = new RenderParams(c.Mat)
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    worldBounds = bounds,
                };
                Graphics.RenderMeshInstanced(rp, c.Mesh, 0, _drawBuf, n);
            }
        }

        Animator[] _anims;
        Transform[] _childHeads;
        byte[] _mode;
        /// <summary>Aftercare (presentation only): every evacuated resident cheers, not just one per landing.</summary>
        public bool Celebrate;
        readonly HashSet<int> _leaders = new HashSet<int>();
        const byte ModeStand = 0, ModeWalk = 1, ModeWave = 2, ModeCheer = 3;
        static readonly int[] ModeState = { Animator.StringToHash("Walk"), Animator.StringToHash("Walk"),
                                            Animator.StringToHash("Wave"), Animator.StringToHash("Cheer") };

        /// <summary>
        /// Rigged residents: textured material, walk controller (paused on the feet-together frame when standing),
        /// and children made from the adult rig, smaller with a larger head. Presentation only.
        /// </summary>
        void RigResident(GameObject go, GameObject model, int r, bool stepFree)
        {
            _kit.RigFor(model, out var mat, out var ctl);
            if (ctl == null) return;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mat != null) smr.sharedMaterial = mat;
                // budget: skinned figures don't cast (a second skinned draw each); the group base anchors them instead
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var an = go.GetComponent<Animator>(); if (an == null) an = go.AddComponent<Animator>();   // Unity null: no ?? on components
            an.runtimeAnimatorController = ctl;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            an.speed = 0f;
            an.Play(0, 0, _kit.IdleTime);
            _anims[r] = an;
            bool child = _kit.IsDerivedChild(r, stepFree);
            if (child)
            {
                go.transform.localScale *= _kit.ChildScale;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") { _childHeads[r] = t; break; }
            }

        }

        /// <summary>Plays the resident's clip for its mode; a figure without that clip (older kit, seated rider)
        /// falls back to the standing pose, and mode reports what it actually shows.</summary>
        void AnimateResident(int r, ref byte mode)
        {
            var an = _anims[r];
            if (an == null) { if (mode != ModeWalk) mode = ModeStand; return; }
            if (mode >= ModeWave && !an.HasState(0, ModeState[mode])) mode = ModeStand;
            if (mode == _mode[r]) return;
            _mode[r] = mode;
            // each resident starts at its own phase so a group doesn't move in step
            an.Play(ModeState[mode], 0, mode == ModeStand ? _kit.IdleTime : (r * 0.37f) % 1f);
            an.speed = mode == ModeStand ? 0f : mode == ModeWalk ? 1.1f : 1f;
        }

        void LateUpdateChildHeads()
        {
            if (_childHeads == null) return;
            for (int r = 0; r < _childHeads.Length; r++)
                if (_childHeads[r] != null && _childHeads[r].gameObject.activeInHierarchy) _childHeads[r].localScale = Vector3.one * _kit.ChildHead;
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
            var hero = HeroKit.Load();
            var landmarks = hero != null ? hero.Landmarks(d.WorldId) : System.Array.Empty<string>();
            bool generic = _kit != null && _kit.Cottage != null;
            if (!generic && landmarks.Length == 0) return;
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

            float cs = BoardSpace.CellSize(d);
            bool Free(int x0, int y0, int bw, int bh, out long blockElev)
            {
                blockElev = 0;
                if (x0 < 0 || y0 < 0 || x0 + bw > w || y0 + bh > h) return false;
                blockElev = d.CellElevation[d.CellIndex(x0, y0)];
                for (int y = y0; y < y0 + bh; y++)
                    for (int x = x0; x < x0 + bw; x++)
                    {
                        int c = d.CellIndex(x, y);
                        if (blocked[c] || d.CellElevation[c] != blockElev) return false;
                    }
                return true;
            }
            void Put(HeroKit.Entry e, int x0, int y0, int bw, int bh, long blockElev, Vector3 face, bool wide, string name)
            {
                for (int y = y0; y < y0 + bh; y++) for (int x = x0; x < x0 + bw; x++) { blocked[d.CellIndex(x, y)] = true; CottageCells.Add(d.CellIndex(x, y)); }
                var centre = new Vector3((x0 + bw * 0.5f - w * 0.5f) * cs, BoardSpace.Height(d, blockElev), (y0 + bh * 0.5f - h * 0.5f) * cs);
                var toward = face - centre; toward.y = 0;
                // front faces the node (or the board centre), snapped to the axis the footprint allows
                float yaw = wide ? (toward.z >= 0 ? 0f : 180f) : (toward.x >= 0 ? 90f : -90f);
                var go = e != null ? HeroKit.Spawn(e, transform, name) : ActorKit.Spawn(_kit.Cottage, transform, name);
                go.transform.localPosition = centre;
                go.transform.localRotation = Quaternion.Euler(0, yaw + (e != null ? e.Yaw : _kit.CottageYaw), 0);
                go.transform.localScale = Vector3.one * _kitScale;
            }
            // footprint in cells for a model: hero bounds (wide scale, 20 mm cells) with a little clearance, else 4×3
            void Cells(HeroKit.Entry e, out int along, out int across)
            {
                if (e == null) { along = 4; across = 3; return; }
                along = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(e.Footprint.x, e.Footprint.y) / BoardSpace.WideCell - 0.25f));
                across = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(e.Footprint.x, e.Footprint.y) / BoardSpace.WideCell - 0.25f));
            }
            landmarks = (string[])landmarks.Clone();
            void Swap(string[] a, int i, int j) { var t = a[i]; a[i] = a[j]; a[j] = t; }
            var homes = new List<HeroKit.Entry>();
            int placed = 0, next = 0;
            int maxHomes = _kit != null ? _kit.MaxCottages : 6;
            for (int g = 0; g < d.GroupIds.Length && placed < maxHomes; g++)
            {
                int start = d.NodeCell[d.GroupStart[g]];
                if (start < 0) continue;
                // homes: the world's house landmarks in order, then those houses again (never the generic cottage
                // when the world has its own), scenic landmarks are left for the high ground below
                HeroKit.Entry e = null;
                bool fresh = false;
                for (int k = next; k < landmarks.Length && e == null; k++)
                    if (!hero.IsScenic(landmarks[k])) { e = hero.Get(landmarks[k]); fresh = e != null; if (fresh) Swap(landmarks, next, k); }
                if (e == null && homes.Count > 0) e = homes[g % homes.Count];
                if (e == null && !generic) continue;
                Cells(e, out int along, out int across);
                bool xLong = e == null || e.Footprint.x >= e.Footprint.y;
                long elev = d.CellElevation[start];
                int sx = d.CellX(start), sy = d.CellY(start);
                bool done = false;
                for (int radius = 1; radius <= 7 && !done; radius++)
                    for (int oy = -radius; oy <= radius && !done; oy++)
                        for (int ox = -radius; ox <= radius && !done; ox++)
                        {
                            if (Mathf.Max(Mathf.Abs(ox), Mathf.Abs(oy)) != radius) continue;
                            foreach (bool wide in new[] { true, false })
                            {
                                // "wide" = the model's long axis along board X
                                int bw = wide == xLong ? along : across, bh = wide == xLong ? across : along;
                                if (!wide && along == across) continue;
                                int x0 = sx + ox - bw / 2, y0 = sy + oy - bh / 2;
                                if (!Free(x0, y0, bw, bh, out long be) || be < elev) continue;
                                Put(e, x0, y0, bw, bh, be, BoardSpace.CellCenter(d, start), wide, "Home_" + d.GroupIds[g]);
                                placed++; done = true;
                                if (fresh) { next++; homes.Add(e); }
                                break;
                            }
                        }
            }
            // scenic landmarks left over: towers, church and lighthouse on the highest free ground toward the back of
            // the board (away from the player's edge, never on the rim); parked vehicles beside the depot
            int depot = -1;
            for (int i = 0; i < d.NodeIds.Length && depot < 0; i++) if (d.NodeIds[i].ToLowerInvariant().Contains("depot") && d.NodeCell[i] >= 0) depot = d.NodeCell[i];
            for (; next < landmarks.Length; next++)
            {
                var e = hero.Get(landmarks[next]);
                if (e == null) continue;
                bool parked = landmarks[next].Contains("truck") || landmarks[next].Contains("snowcat") || landmarks[next].Contains("bulldozer");
                Cells(e, out int along, out int across);
                bool xLong = e.Footprint.x >= e.Footprint.y;
                int bestX = -1, bestY = -1, bestW = 0, bestH = 0; long bestElev = 0; bool bestWide = true; float bestScore = float.MinValue;
                foreach (bool wide in new[] { true, false })
                {
                    int bw = wide == xLong ? along : across, bh = wide == xLong ? across : along;
                    for (int y0 = 1; y0 + bh <= h - 1; y0++)
                        for (int x0 = 1; x0 + bw <= w - 1; x0++)
                        {
                            if (!Free(x0, y0, bw, bh, out long be)) continue;
                            float score;
                            if (parked && depot >= 0)
                            {
                                float dx = x0 + bw * 0.5f - d.CellX(depot), dy = y0 + bh * 0.5f - d.CellY(depot);
                                score = -(dx * dx + dy * dy);
                            }
                            else score = be * 0.01f + (y0 + bh * 0.5f) / h;
                            if (score > bestScore) { bestScore = score; bestElev = be; bestX = x0; bestY = y0; bestW = bw; bestH = bh; bestWide = wide; }
                        }
                }
                if (bestX >= 0) Put(e, bestX, bestY, bestW, bestH, bestElev, parked && depot >= 0 ? BoardSpace.CellCenter(d, depot) : Vector3.zero, bestWide, "Landmark_" + e.Id);
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

        /// <summary>Mesh for the placement hologram of a tool at a socket (same proportions as the placed greybox).</summary>
        public Mesh GhostMesh(ToolKind tool, int sock) => EquipmentMesh(tool, sock);

        public Transform EquipmentTransform(int inst) => _equipment.TryGetValue(inst, out var t) ? t : null;
        /// <summary>Direct lookups for per-frame callers (a Find("Vehicle_" + v) allocates a string every frame).</summary>
        public Transform VehicleTransform(int v) => v >= 0 && v < _vehicles.Count ? _vehicles[v] : null;
        public Transform ResidentTransform(int r) => r >= 0 && r < _residents.Count ? _residents[r] : null;

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
