using System.Collections.Generic;
using Oculus.Interaction.Input;
using Resq.Game;
using Resq.Sim;
using UnityEngine;

namespace Resq.XR
{
    /// <summary>
    /// Headset input sources, all feeding the one BoardInteraction command path (spec: one gesture owner):
    ///   • Interaction SDK hands: pointer-pose ray, index pinch with hysteresis, fingertip poke point; each hand on
    ///     its own, so one-hand play is the same as two-hand play;
    ///   • head-gaze dwell for the Command profile (Quest 3/3S have no eye tracking);
    ///   • optional head-gaze + pinch (aim with the head, confirm with either hand) for players who can't aim a
    ///     hand ray steadily.
    /// Falls back to the Core SDK OVRHand adapter when the Interaction SDK rig has no hands.
    /// </summary>
    public static class XrPointers
    {
        /// <summary>Accessibility setting: aim with the head, confirm with a pinch.</summary>
        public static bool GazePinch;
        /// <summary>The hand pointers in use (diagnostics, tests).</summary>
        public static readonly List<IsdkHandPointer> Active = new List<IsdkHandPointer>();

        public static void Setup(GameBootstrap boot, OVRCameraRig rig)
        {
            var bySide = new Dictionary<Handedness, List<IHand>>();
            foreach (var h in Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!bySide.TryGetValue(h.Handedness, out var list)) bySide[h.Handedness] = list = new List<IHand>();
                list.Add(h);
            }
            var hands = new List<IsdkHandPointer>();
            Active.Clear();
            foreach (var kv in bySide)
            {
                var p = new IsdkHandPointer(kv.Value, kv.Key);
                hands.Add(p);
                Active.Add(p);
                boot.Interaction.AddSource(p);
            }
            if (hands.Count == 0)
                foreach (var hand in Object.FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
                    boot.Interaction.AddSource(new HandPointer(hand, hand.GetComponent<OVRSkeleton>(), hand.GetHand() == OVRPlugin.Hand.HandLeft));
            if (boot.Profile == RuleProfile.Command) boot.Interaction.AddSource(new HeadDwellPointer(rig.centerEyeAnchor));
            if (GazePinch && hands.Count > 0) boot.Interaction.AddSource(new HeadPinchPointer(rig.centerEyeAnchor, hands));
            Debug.Log($"[RESQ XR] pointers: {hands.Count} Interaction SDK hand(s){(boot.Profile == RuleProfile.Command ? " + head dwell" : "")}{(GazePinch ? " + gaze-pinch" : "")}");
        }
    }

    /// <summary>One tracked hand through the Interaction SDK's IHand (the rig may expose several per side; the first tracked one wins).</summary>
    public sealed class IsdkHandPointer : IPointerSource
    {
        readonly List<IHand> _candidates;
        readonly PointerKind _kind;
        bool _pinched;
        public bool Pinched => _pinched;
        public bool Tracked { get; private set; }
        public Ray LastRay { get; private set; }
        public PointerKind Kind => _kind;

        public IsdkHandPointer(List<IHand> candidates, Handedness side)
        {
            _candidates = candidates;
            _kind = side == Handedness.Left ? PointerKind.HandLeft : PointerKind.HandRight;
        }

        IHand Current()
        {
            foreach (var h in _candidates) if (h != null && h.IsConnected && h.IsTrackedDataValid) return h;
            return null;
        }

        public PointerSample Sample()
        {
            var hand = Current();
            Tracked = hand != null && hand.IsPointerPoseValid;
            if (!Tracked || !hand.GetPointerPose(out var pose)) { _pinched = false; Tracked = false; return new PointerSample { Kind = _kind }; }
            float strength = hand.GetFingerPinchStrength(HandFinger.Index);
            _pinched = _pinched ? strength > 0.55f : strength > 0.8f;   // hysteresis: no flicker at the threshold
            var s = new PointerSample { Kind = _kind, Valid = true, Ray = new Ray(pose.position, pose.forward), Pressed = _pinched };
            LastRay = s.Ray;
            if (hand.GetJointPose(HandJointId.HandIndexTip, out var tip)) { s.HasPokePoint = true; s.PokePoint = tip.position; }
            return s;
        }

        public void Feedback(Pickable hover, float progress) { }
    }

    /// <summary>Aim with the head, confirm with a pinch on either hand (no hand ray needed).</summary>
    public sealed class HeadPinchPointer : IPointerSource
    {
        readonly Transform _head;
        readonly List<IsdkHandPointer> _hands;
        public HeadPinchPointer(Transform head, List<IsdkHandPointer> hands) { _head = head; _hands = hands; }

        public PointerSample Sample()
        {
            if (_head == null) return default;
            bool pinch = false;
            foreach (var h in _hands) pinch |= h.Pinched;
            return new PointerSample { Kind = PointerKind.HeadDwell, Valid = true, Ray = new Ray(_head.position, _head.forward), Pressed = pinch };
        }

        public void Feedback(Pickable hover, float progress) { }
    }
}
