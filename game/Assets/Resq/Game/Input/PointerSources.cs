using UnityEngine;
using UnityEngine.InputSystem;

namespace Resq.Game
{
    /// <summary>Editor/desktop adapter: mouse ray + left button. Same command path as hands (used by PlayMode tests).</summary>
    public sealed class DesktopPointer : IPointerSource
    {
        readonly Camera _cam;
        public DesktopPointer(Camera cam) { _cam = cam; }

        public PointerSample Sample()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null) return default;
            Vector2 p = mouse.position.ReadValue();
            return new PointerSample
            {
                Kind = PointerKind.Desktop,
                Valid = true,
                Ray = _cam.ScreenPointToRay(p),
                Pressed = mouse.leftButton.isPressed,
            };
        }

        public void Feedback(Pickable hover, float progress) { }
    }

    /// <summary>Tracked-hand adapter (Meta Core SDK OVRHand): pointer-pose ray, index pinch, fingertip poke point.</summary>
    public sealed class HandPointer : IPointerSource
    {
        readonly OVRHand _hand;
        readonly OVRSkeleton _skeleton;
        readonly PointerKind _kind;
        bool _pinched;
        public HandPointer(OVRHand hand, OVRSkeleton skeleton, bool left)
        {
            _hand = hand; _skeleton = skeleton; _kind = left ? PointerKind.HandLeft : PointerKind.HandRight;
        }

        public PointerSample Sample()
        {
            if (_hand == null || !_hand.IsTracked || !_hand.IsPointerPoseValid) { _pinched = false; return new PointerSample { Kind = _kind }; }
            float strength = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            // hysteresis so a pinch does not flicker at the threshold
            _pinched = _pinched ? strength > 0.55f : strength > 0.8f;
            var pose = _hand.PointerPose;
            var s = new PointerSample
            {
                Kind = _kind,
                Valid = true,
                Ray = new Ray(pose.position, pose.forward),
                Pressed = _pinched,
            };
            if (_skeleton != null && _skeleton.IsDataValid && _skeleton.Bones != null)
            {
                foreach (var b in _skeleton.Bones)
                {
                    if (b.Id != OVRSkeleton.BoneId.Hand_IndexTip) continue;
                    s.HasPokePoint = true;
                    s.PokePoint = b.Transform.position;
                    break;
                }
            }
            return s;
        }

        public void Feedback(Pickable hover, float progress) { }
    }

    /// <summary>
    /// Head-directed Command path (no eye tracking on Quest 3/3S): a ray from the head pose; holding the gaze on a
    /// target for the dwell time selects it. Inspection never activates anything by itself.
    /// </summary>
    public sealed class HeadDwellPointer : IPointerSource
    {
        readonly Transform _head;
        public float DwellSeconds = 0.8f;
        Pickable _target;
        float _held;
        bool _fired;
        public float Progress => Mathf.Clamp01(_held / DwellSeconds);

        public HeadDwellPointer(Transform head) { _head = head; }

        public PointerSample Sample()
        {
            if (_head == null) return default;
            var ray = new Ray(_head.position, _head.forward);
            Physics.SyncTransforms();   // moving handles: cast against this frame's positions (see BoardInteraction.Pick)
            Pickable hit = BoardInteraction.CastPickable(ray, 0.006f, 2f);   // skips the rig's own colliders
            if (hit != _target) { _target = hit; _held = 0; _fired = false; }
            else if (hit != null) _held += Time.unscaledDeltaTime;
            bool press = false;
            if (_target != null && !_fired && _held >= DwellSeconds) { press = true; _fired = true; }
            return new PointerSample { Kind = PointerKind.HeadDwell, Valid = true, Ray = ray, Pressed = press };
        }

        public void Feedback(Pickable hover, float progress) { }
    }
}
