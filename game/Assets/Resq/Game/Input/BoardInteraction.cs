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
            if (s.HasPokePoint)
            {
                var near = Physics.OverlapSphere(s.PokePoint, PokeRadius);
                foreach (var c in near)
                {
                    var p = c.GetComponentInParent<Pickable>();
                    if (p != null) return p;
                }
            }
            if (Physics.SphereCast(s.Ray, 0.004f, out var hit, 3f))
                return hit.collider.GetComponentInParent<Pickable>();
            return null;
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
                    return (Driver.Def.NodeFlags[p.Index] & NodeFlags.Evacuation) != 0 ? Reject.OK : Reject.UnknownTarget;
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
                    if (Mode == InteractMode.ToolArmed && ArmedTool == tool) { Cancel(); return; }
                    Mode = InteractMode.ToolArmed;
                    ArmedTool = tool;
                    return;
                }
                case PickKind.Socket when Mode == InteractMode.ToolArmed:
                    TryPlace(p.Index);
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
                        int pickup = g >= 0 ? Driver.Def.GroupPickup[g] : -1;
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
                default: return r.ToString();
            }
        }
    }
}
