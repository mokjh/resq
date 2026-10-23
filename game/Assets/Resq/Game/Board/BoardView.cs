using System.Collections.Generic;
using Resq.Sim;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// The diorama: terrain, water, actors, and every pickable target (sockets, destinations, groups, vehicles),
    /// all generated from the compiled mission so the board can never disagree with the simulation.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;
        public Material TerrainMaterial, WaterMaterial, ActorMaterial, MarkerMaterial, OverlayMaterial;
        public HazardView Hazards { get; private set; }
        public ScatterView Scatter { get; private set; }
        public ForecastView Forecast { get; private set; }

        public WaterView Water { get; private set; }
        public ActorsView Actors { get; private set; }

        readonly List<(Pickable p, Renderer r)> _sockets = new List<(Pickable, Renderer)>();
        readonly List<(Pickable p, Renderer r)> _nodes = new List<(Pickable, Renderer)>();
        readonly List<Pickable> _groups = new List<Pickable>();
        readonly List<Pickable> _vehicles = new List<Pickable>();
        readonly List<Pickable> _equipment = new List<Pickable>();
        Transform _root;
        MaterialPropertyBlock _mpb;
        MissionDef _def;

        static readonly int TintId = Shader.PropertyToID("_Tint");
        public static readonly Color Ok = TerrainBuilder.Hex(0x7BD389), Risky = TerrainBuilder.Hex(0xF2B134), Bad = TerrainBuilder.Hex(0xE5484D);

        void Awake() { _mpb = new MaterialPropertyBlock(); }

        void OnEnable() { if (Driver != null) Driver.OnRunLoaded += Rebuild; }
        void OnDisable() { if (Driver != null) Driver.OnRunLoaded -= Rebuild; }

        public void Rebuild()
        {
            _def = Driver.Def;
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("BoardRoot").transform;
            _root.SetParent(transform, false);
            _sockets.Clear(); _nodes.Clear(); _groups.Clear(); _vehicles.Clear(); _equipment.Clear();

            var terrain = new GameObject("Terrain");
            terrain.transform.SetParent(_root, false);
            terrain.AddComponent<MeshFilter>().sharedMesh = TerrainBuilder.Build(_def);
            terrain.AddComponent<MeshRenderer>().sharedMaterial = TerrainMaterial;

            var water = new GameObject("Water");
            water.transform.SetParent(_root, false);
            water.AddComponent<MeshFilter>();
            water.AddComponent<MeshRenderer>().sharedMaterial = WaterMaterial;
            Water = water.AddComponent<WaterView>();
            Water.Driver = Driver;
            Water.Build(_def);

            var actors = new GameObject("Actors");
            actors.transform.SetParent(_root, false);
            Actors = actors.AddComponent<ActorsView>();
            Actors.Driver = Driver; Actors.Material = ActorMaterial;
            Actors.Build(_def);

            var scatter = new GameObject("Scatter");
            scatter.transform.SetParent(_root, false);
            Scatter = scatter.AddComponent<ScatterView>();
            Scatter.Driver = Driver; Scatter.Actors = Actors;
            var natureKit = NatureKit.Load();
            Scatter.Material = natureKit != null && natureKit.Material != null ? natureKit.Material : ActorMaterial;
            Scatter.Build(_def);

            var ring = new GameObject("WorldRing");
            ring.transform.SetParent(_root, false);
            ring.AddComponent<MeshFilter>();
            var ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = TerrainMaterial;
            ring.AddComponent<WorldRingView>().Build(_def);

            var hazards = new GameObject("Hazards");
            hazards.transform.SetParent(_root, false);
            hazards.AddComponent<MeshFilter>();
            hazards.AddComponent<MeshRenderer>().sharedMaterial = OverlayMaterial;
            Hazards = hazards.AddComponent<HazardView>();
            Hazards.Driver = Driver;
            Hazards.Build(_def);

            var forecast = new GameObject("Forecast");
            forecast.transform.SetParent(_root, false);
            forecast.AddComponent<MeshFilter>();
            forecast.AddComponent<MeshRenderer>().sharedMaterial = OverlayMaterial;
            Forecast = forecast.AddComponent<ForecastView>();
            Forecast.Driver = Driver; Forecast.Interaction = Interaction;
            Forecast.Build(_def);

            BuildSockets();
            BuildNodes();
            BuildGroupsAndVehicles();
        }

        Pickable MakePick(string name, PickKind kind, int index, Vector3 pos, float radius, Mesh visual, Color color, out Renderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            var col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.isTrigger = true;
            var p = go.AddComponent<Pickable>();
            p.Kind = kind; p.Index = index;
            r = null;
            if (visual != null)
            {
                var vis = new GameObject("Marker");
                vis.transform.SetParent(go.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = visual;
                r = vis.AddComponent<MeshRenderer>();
                r.sharedMaterial = MarkerMaterial;
                SetTint(r, color);
            }
            return p;
        }

        void BuildSockets()
        {
            float c = BoardSpace.CellSize(_def);
            var ring = new MeshKit().Cylinder(Vector3.zero, c * 0.42f, 0.0015f, 16, Color.white).Build("SocketRing");
            for (int s = 0; s < _def.SocketIds.Length; s++)
            {
                Vector3 pos = Actors.SocketPos(s);
                pos.y = Mathf.Max(pos.y, BoardSpace.Height(_def, _def.CellElevation[Mathf.Max(0, _def.SocketCell[s])]) + 0.001f);
                var p = MakePick("Socket_" + _def.SocketIds[s], PickKind.Socket, s, pos, c * 0.9f, ring, Ok, out var r);
                p.Label = _def.SocketIds[s];
                _sockets.Add((p, r));
            }
        }

        void BuildNodes()
        {
            float c = BoardSpace.CellSize(_def);
            var flag = new MeshKit().Cylinder(Vector3.zero, 0.0008f, 0.03f, 6, Color.white)
                                    .Box(new Vector3(0.005f, 0.026f, 0), new Vector3(0.01f, 0.007f, 0.001f), Color.white).Build("NodeFlag");
            // every place a group or vehicle can sensibly be sent: flagged nodes, group pickups/assemblies, vehicle stations
            var useful = new HashSet<int>();
            for (int n = 0; n < _def.NodeIds.Length; n++) if (_def.NodeFlags[n] != NodeFlags.None) useful.Add(n);
            for (int g = 0; g < _def.GroupIds.Length; g++) { useful.Add(_def.GroupPickup[g]); useful.Add(_def.GroupAssembly[g]); }
            for (int s = 0; s < _def.SocketIds.Length; s++) if (_def.SocketNode[s] >= 0) useful.Add(_def.SocketNode[s]);
            for (int n = 0; n < _def.NodeIds.Length; n++)
            {
                var flags = _def.NodeFlags[n];
                if (!useful.Contains(n)) continue;
                var pos = BoardSpace.CellCenter(_def, _def.NodeCell[n]);
                bool refuge = (flags & NodeFlags.Evacuation) != 0;
                var p = MakePick("Node_" + _def.NodeIds[n], PickKind.Node, n, pos, c * 0.8f, refuge ? flag : null, ActorsView.Rescue, out var r);
                p.Label = Pretty(_def.NodeIds[n]);
                if (refuge) AddLabel(p.transform, p.Label, new Vector3(0, 0.04f, 0));
                _nodes.Add((p, r));
            }
        }

        void BuildGroupsAndVehicles()
        {
            float c = BoardSpace.CellSize(_def);
            for (int g = 0; g < _def.GroupIds.Length; g++)
            {
                var p = MakePick("Group_" + _def.GroupIds[g], PickKind.Group, g, Vector3.zero, c * 0.9f, null, Color.white, out _);
                p.Label = Pretty(_def.GroupIds[g]);
                AddLabel(p.transform, p.Label, new Vector3(0, 0.045f, 0));
                _groups.Add(p);
            }
        }

        void AddLabel(Transform parent, string text, Vector3 offset)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 0.12f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TerrainBuilder.Hex(0xF3EBDD);
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = TerrainBuilder.Hex(0x132B2A);
            go.AddComponent<Billboard>();
        }

        public static string Pretty(string id)
        {
            var parts = id.Replace('_', ' ').Split(' ');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        void SetTint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(TintId, c);
            r.SetPropertyBlock(_mpb);
        }

        void LateUpdate()
        {
            if (Driver?.Sim == null || _def != Driver.Def) return;
            var sim = Driver.Sim;
            var mode = Interaction != null ? Interaction.Mode : InteractMode.Idle;
            // sockets glow only for the armed tool, coloured by legality
            foreach (var (p, r) in _sockets)
            {
                bool show = mode == InteractMode.ToolArmed && _def.SocketKind[p.Index] == Interaction.ArmedTool && sim.S.SocketInstance[p.Index] < 0;
                if (p.gameObject.activeSelf != show) p.gameObject.SetActive(show);
                if (!show) continue;
                var verdict = sim.ValidatePlace(p.Index, Interaction.ArmedTool, 0);
                bool hovered = Interaction.Hover == p;
                Color col = verdict == Reject.OK ? Ok : verdict == Reject.InsufficientSupplies || verdict == Reject.StockExhausted ? Risky : Bad;
                if (hovered) col = Color.Lerp(col, Color.white, 0.35f);
                SetTint(r, col);
                p.transform.localScale = Vector3.one * (hovered ? 1.25f : 1f + 0.08f * Mathf.Sin(Time.time * 4f));
            }
            // destinations active only while choosing a destination
            bool choosing = mode == InteractMode.GroupRouting || mode == InteractMode.VehicleDestination || mode == InteractMode.VehiclePickup;
            foreach (var (p, r) in _nodes)
            {
                bool refuge = (_def.NodeFlags[p.Index] & NodeFlags.Evacuation) != 0;
                p.GetComponent<Collider>().enabled = choosing;
                if (r != null) SetTint(r, choosing && Interaction.Hover == p ? Color.white : ActorsView.Rescue);
                if (!refuge && r != null) r.enabled = choosing;
            }
            // group handles follow their members
            var S = sim.S;
            for (int g = 0; g < _groups.Count; g++)
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (int r in _def.GroupResidents[g])
                {
                    var st = (ResidentState)S.RState[r];
                    if (st == ResidentState.Evacuated || st == ResidentState.Aboard || st == ResidentState.AwaitingBackup) continue;
                    sum += BoardSpace.CellCenter(_def, _def.NodeCell[S.RNode[r]]); n++;
                }
                _groups[g].gameObject.SetActive(n > 0);
                if (n > 0) _groups[g].transform.localPosition = sum / n;
            }
            // vehicle handles follow vehicles
            while (_vehicles.Count < S.C.VehicleCount)
            {
                int v = _vehicles.Count;
                var p = MakePick("VehicleHandle_" + v, PickKind.Vehicle, v, Vector3.zero, BoardSpace.CellSize(_def) * 0.9f, null, Color.white, out _);
                p.Label = v < _def.FixedVehicleIds.Length ? Pretty(_def.FixedVehicleIds[v]) : ((VehicleKind)S.VKind[v]).ToString();
                _vehicles.Add(p);
            }
            // placed equipment can be tapped (cool, refill, switch on/off)
            while (_equipment.Count < S.C.EquipmentCount)
            {
                int inst = _equipment.Count;
                var p = MakePick("EquipmentHandle_" + inst, PickKind.Equipment, inst, Vector3.zero, BoardSpace.CellSize(_def) * 0.7f, null, Color.white, out _);
                p.Label = ((ToolKind)S.EqKind[inst]).ToString();
                _equipment.Add(p);
            }
            for (int i = 0; i < _equipment.Count; i++)
            {
                var actor = Actors.EquipmentTransform(i);
                bool tappable = actor != null && mode == InteractMode.Idle && Interaction.EquipmentAction(i, out _);
                if (_equipment[i].gameObject.activeSelf != tappable) _equipment[i].gameObject.SetActive(tappable);
                if (tappable) _equipment[i].transform.localPosition = actor.localPosition + Vector3.up * 0.01f;
            }
            for (int v = 0; v < _vehicles.Count; v++)
            {
                var actor = Actors.transform.Find("Vehicle_" + v);
                if (actor != null) _vehicles[v].transform.localPosition = actor.localPosition;
            }
        }
    }

    /// <summary>Keeps world-space labels facing the viewer.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var fwd = transform.position - cam.transform.position;
            fwd.y = 0;
            if (fwd.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
    }
}
