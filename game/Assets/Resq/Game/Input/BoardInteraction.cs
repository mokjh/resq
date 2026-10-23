using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    public enum InteractMode : byte { Idle, ToolArmed, GroupRouting, VehiclePickup, VehicleDestination }

    /// <summary>
    /// Turns pointer intent from any adapter into validated simulation commands:
    ///   tool card → socket (tap, or pinch-drag-release) = place equipment;
    ///   group → destination = walk route; vehicle → group → destination = transport route.
    /// The first adapter that presses owns the gesture until release (spec: one gesture owner).
    /// </summary>
    public sealed class BoardInteraction : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardView Board;
        public float PokeRadius = 0.012f;

        public InteractMode Mode { get; private set; }
        public ToolKind ArmedTool { get; private set; }
        public int SelectedGroup { get; private set; } = -1;
        public int SelectedVehicle { get; private set; } = -1;
        public int PickupGroup { get; private set; } = -1;
        public Pickable Hover { get; private set; }
        public Reject HoverVerdict { get; private set; }
        public string Hint { get; private set; } = "";
        /// <summary>Optional gate on which tool cards respond (guided first play unlocks tools step by step).</summary>
        public System.Func<ToolKind, bool> ToolAllowed;
        /// <summary>Dev/capture only: when set, stands in for whatever the pointer hovers (no presses are synthesised).</summary>
        public Pickable DebugHover;

        /// <summary>Dev/capture only: arm a tool as if its card had been pinched.</summary>
        public void DebugArm(ToolKind tool) { Mode = InteractMode.ToolArmed; ArmedTool = tool; }

        readonly List<IPointerSource> _sources = new List<IPointerSource>();
        IPointerSource _owner;
        bool _ownerWasPressed;
        Pickable _pressTarget;

        public void AddSource(IPointerSource s) => _sources.Add(s);
        public void ClearSources() => _sources.Clear();

        void Update()
        {
            if (Driver?.Sim == null) return;
            // choose the gesture owner: keep the current owner while pressed; otherwise first valid source that hovers something
            PointerSample sample = default;
            IPointerSource active = null;
            if (_owner != null)
            {
                sample = _owner.Sample();
                active = _owner;
            }
            if (active == null || (!sample.Pressed && !_ownerWasPressed))
            {
                active = null;
                foreach (var src in _sources)
                {
                    var s = src.Sample();
                    if (!s.Valid) continue;
                    if (active == null || s.Pressed) { active = src; sample = s; if (s.Pressed) break; }
                }
            }
            if (active == null) { Hover = null; return; }

            Pickable hit = Pick(sample);
            Hover = hit;
            HoverVerdict = Evaluate(hit);
            active.Feedback(hit, 0);

            bool pressedNow = sample.Pressed;
            if (pressedNow && !_ownerWasPressed) { _owner = active; _pressTarget = hit; OnPress(hit); }
            else if (!pressedNow && _ownerWasPressed) { OnRelease(hit); _owner = null; }
            _ownerWasPressed = pressedNow;
            UpdateHint();
        }

        Pickable Pick(PointerSample s)
        {
            if (DebugHover != null) return DebugHover;
            // handles move in LateUpdate (vehicles, groups); colliders otherwise only catch up on the next fixed step,
            // so at 72-90 fps a pick could test last frame's positions and miss a moving boat (found by the smoke gate)
            Physics.SyncTransforms();
            if (s.HasPokePoint)
            {
                var near = Physics.OverlapSphere(s.PokePoint, PokeRadius);
                foreach (var c in near)
                {
                    var p = c.GetComponentInParent<Pickable>();
                    if (p != null) return p;
                }
            }
            return CastPickable(s.Ray, 0.004f, 3f);
        }

        static readonly RaycastHit[] _hits = new RaycastHit[24];

        /// <summary>
        /// Nearest pickable along a ray. Non-game colliders are skipped (the XR rig's own PlayerController capsule,
        /// MRUK room geometry): taking the first hit let the player's body swallow head and hand rays (found in the
        /// Meta XR Simulator, 23 Oct). Non-allocating.
        /// </summary>
        public static Pickable CastPickable(Ray ray, float radius, float maxDistance)
        {
            int n = Physics.SphereCastNonAlloc(ray, radius, _hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Pickable best = null; float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].distance >= bestD) continue;
                var p = _hits[i].collider.GetComponentInParent<Pickable>();
                if (p == null) continue;
                best = p; bestD = _hits[i].distance;
            }
            return best;
        }

        /// <summary>Legality of acting on a target in the current mode (drives red/amber/green feedback).</summary>
        public Reject Evaluate(Pickable p)
        {
            if (p == null) return Reject.UnknownTarget;
            var sim = Driver.Sim;
            switch (Mode)
            {
                case InteractMode.ToolArmed when p.Kind == PickKind.Socket:
                    return sim.ValidatePlace(p.Index, ArmedTool, 0);
                case InteractMode.GroupRouting when p.Kind == PickKind.Node:
                    return sim.Reachable(CurrentNodeOfGroup(SelectedGroup), p.Index, Traversal.Ground, false, false) ? Reject.OK : Reject.RouteBlocked;
                case InteractMode.VehicleDestination when p.Kind == PickKind.Node:
                {
                    // any node the vehicle can reach: refuges evacuate, other nodes are relay/staging drop-offs
                    var sim2 = Driver.Sim;
                    int v = SelectedVehicle;
                    if (v < 0 || v >= sim2.S.C.VehicleCount) return Reject.UnknownTarget;
                    var cls = Simulator.VehicleClass((VehicleKind)sim2.S.VKind[v]);
                    return sim2.Reachable(sim2.S.VNode[v], p.Index, cls, false, false) ? Reject.OK : Reject.RouteBlocked;
                }
                case InteractMode.Idle when p.Kind == PickKind.Equipment:
                    return EquipmentAction(p.Index, out _) ? Reject.OK : Reject.NotIdle;
                default:
                    return Reject.OK;
            }
        }

        void OnPress(Pickable p)
        {
            if (p == null) return;
            switch (p.Kind)
            {
                case PickKind.Button:
                    p.OnPress?.Invoke();
                    return;
                case PickKind.Tool:
                {
                    var tool = (ToolKind)p.Index;
                    if (ToolAllowed != null && !ToolAllowed(tool)) { Hint = "Not yet — follow the guide"; return; }
                    if (Mode == InteractMode.ToolArmed && ArmedTool == tool) { Cancel(); return; }
                    Mode = InteractMode.ToolArmed;
                    ArmedTool = tool;
                    return;
                }
                case PickKind.Socket when Mode == InteractMode.ToolArmed:
                    TryPlace(p.Index);
                    return;
                case PickKind.Equipment when Mode == InteractMode.Idle:
                    if (EquipmentAction(p.Index, out var cmd)) Driver.Submit(cmd);
                    else Hint = "Nothing to do with that right now";
                    return;
                case PickKind.Group:
                    if (Mode == InteractMode.VehiclePickup) { PickupGroup = p.Index; Mode = InteractMode.VehicleDestination; return; }
                    Mode = InteractMode.GroupRouting; SelectedGroup = p.Index;
                    return;
                case PickKind.Vehicle:
                    Mode = InteractMode.VehiclePickup; SelectedVehicle = p.Index; PickupGroup = -1;
                    return;
                case PickKind.Node:
                    if (Mode == InteractMode.GroupRouting)
                    {
                        bool evac = (Driver.Def.NodeFlags[p.Index] & NodeFlags.Evacuation) != 0;
                        Driver.Submit(Command.GroupRoute(SelectedGroup, p.Index, evac));
                        Cancel();
                    }
                    else if (Mode == InteractMode.VehicleDestination || Mode == InteractMode.VehiclePickup)
                    {
                        int g = PickupGroup;
                        int pickup = g >= 0 ? PickupNodeFor(g, SelectedVehicle) : -1;
                        bool evac = (Driver.Def.NodeFlags[p.Index] & NodeFlags.Evacuation) != 0;
                        Driver.Submit(Command.VehicleRoute(SelectedVehicle, g, pickup, p.Index, evac));
                        Cancel();
                    }
                    return;
            }
        }

        void OnRelease(Pickable p)
        {
            // pinch-drag-release from a tool card onto a socket places it in one gesture
            if (_pressTarget != null && _pressTarget.Kind == PickKind.Tool && p != null && p.Kind == PickKind.Socket && Mode == InteractMode.ToolArmed)
                TryPlace(p.Index);
            _pressTarget = null;
        }

        void TryPlace(int socket)
        {
            var verdict = Driver.Sim.ValidatePlace(socket, ArmedTool, 0);
            if (verdict != Reject.OK) { Hint = Explain(verdict); return; }
            Driver.Submit(Command.Place(socket, ArmedTool, 0));
            Cancel();
        }

        /// <summary>
        /// Where a vehicle should collect a group: members dropped part-way (a relay stop) are collected where they
        /// wait; otherwise the group's authored pickup.
        /// </summary>
        /// <summary>
        /// Pickup for a group and a chosen vehicle. The authored pickup suits the group's usual vehicle; when this
        /// vehicle can't reach it (a cable winch only serves its stations), collect the group at the nearest node the
        /// vehicle reaches and the group can walk to. Found by the smoke gate (W3-M1 23 Oct): the winch was sent to
        /// the ground pickup, which no player could have fixed from the UI.
        /// </summary>
        public int PickupNodeFor(int g, int vehicle)
        {
            int authored = PickupNodeFor(g);
            var sim = Driver.Sim; var d = Driver.Def;
            if (vehicle < 0 || vehicle >= sim.S.C.VehicleCount || authored < 0) return authored;
            var cls = Simulator.VehicleClass((VehicleKind)sim.S.VKind[vehicle]);
            int from = sim.S.VNode[vehicle];
            if (from < 0 || sim.Reachable(from, authored, cls, false, false)) return authored;
            int at = CurrentNodeOfGroup(g);
            if (at < 0) return authored;
            var here = BoardSpace.CellCenter(d, d.NodeCell[at]);
            int best = authored; float bestD = float.MaxValue;
            for (int n = 0; n < d.NodeIds.Length; n++)
            {
                if (d.NodeCell[n] < 0 || !sim.Reachable(from, n, cls, false, false) || !sim.Reachable(at, n, Traversal.Ground, false, false)) continue;
                float dist = (BoardSpace.CellCenter(d, d.NodeCell[n]) - here).sqrMagnitude;
                if (dist < bestD) { bestD = dist; best = n; }
            }
            return best;
        }

        public int PickupNodeFor(int g)
        {
            var S = Driver.Sim.S;
            foreach (int r in Driver.Def.GroupResidents[g])
                if (S.RState[r] == (byte)ResidentState.AtNode && S.RStay[r]) return S.RNode[r];
            return Driver.Def.GroupPickup[g];
        }

        /// <summary>What tapping placed equipment does: coolers cool their segment (or refill when empty);
        /// switchable tools toggle on/off. Structural tools (barrier, bridge, brace, firebreak) do nothing.</summary>
        public bool EquipmentAction(int inst, out Command cmd)
        {
            cmd = default;
            var sim = Driver.Sim; var S = sim.S; var d = Driver.Def;
            if (inst < 0 || inst >= S.C.EquipmentCount || S.EqBroken[inst]) return false;
            var tool = (ToolKind)S.EqKind[inst];
            int sock = S.EqSocket[inst];
            switch (tool)
            {
                case ToolKind.Cooler:
                    if (S.EqCharges[inst] > 0) { cmd = Command.Cool(inst); return true; }
                    if (d.SocketCoolantStore[sock] >= 0 && S.CoolantStore[d.SocketCoolantStore[sock]] > 0)
                    { cmd = Command.Refill(inst, d.SocketCoolantStore[sock]); return true; }
                    return false;
                case ToolKind.Pump: case ToolKind.Generator: case ToolKind.Sprinkler: case ToolKind.Relay: case ToolKind.Winch:
                    cmd = S.EqActive[inst] ? Command.Disable(inst) : Command.Enable(inst);
                    return true;
                default:
                    return false;
            }
        }

        public void Cancel()
        {
            Mode = InteractMode.Idle; SelectedGroup = -1; SelectedVehicle = -1; PickupGroup = -1;
        }

        public int CurrentNodeOfGroup(int g)
        {
            if (g < 0) return -1;
            var S = Driver.Sim.S;
            foreach (int r in Driver.Def.GroupResidents[g])
            {
                var st = (ResidentState)S.RState[r];
                if (st == ResidentState.AtNode || st == ResidentState.Walking) return S.RNode[r];
            }
            return Driver.Def.GroupStart[g];
        }

        void UpdateHint()
        {
            switch (Mode)
            {
                case InteractMode.ToolArmed: Hint = $"Place the {MapCompiler.ToolName(ArmedTool)} on a glowing spot"; break;
                case InteractMode.GroupRouting: Hint = "Choose where this group should walk"; break;
                case InteractMode.VehiclePickup: Hint = "Choose a group to pick up, or a destination"; break;
                case InteractMode.VehicleDestination: Hint = "Choose a safe place to drop them off"; break;
                default: if (Hint.StartsWith("Place") || Hint.StartsWith("Choose")) Hint = ""; break;
            }
        }

        public static string Explain(Reject r)
        {
            switch (r)
            {
                case Reject.InsufficientSupplies: return "Not enough supplies";
                case Reject.StockExhausted: return "None of that tool left";
                case Reject.SocketIncompatible: return "That tool doesn't fit there";
                case Reject.Occupied: return "Something is already there";
                case Reject.UnsafeTarget: return "Too dangerous to place there";
                case Reject.RouteBlocked: return "No safe way there right now";
                case Reject.ResourceEmpty: return "Out of coolant";
                case Reject.NotIdle: return "It's busy right now";
                case Reject.PowerRequired: return "Needs power";
                default: return r.ToString();
            }
        }
    }
}
