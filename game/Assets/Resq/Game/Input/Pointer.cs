using UnityEngine;

namespace Resq.Game
{
    public enum PickKind : byte { None, Tool, Socket, Node, Group, Vehicle, Button }

    /// <summary>Anything a pointer can select. Residents/hazards are never pickable as objects to move.</summary>
    public sealed class Pickable : MonoBehaviour
    {
        public PickKind Kind;
        public int Index;
        public string Label;
        public System.Action OnPress; // for Button
    }

    public enum PointerKind : byte { Desktop, HandLeft, HandRight, ControllerLeft, ControllerRight, HeadDwell }

    /// <summary>One frame of pointer intent from any adapter (hand ray, poke, controller, head-gaze dwell, mouse).</summary>
    public struct PointerSample
    {
        public PointerKind Kind;
        public bool Valid;
        public Ray Ray;
        public bool Pressed;          // pinch / trigger / click / dwell completed this frame (held state)
        public bool HasPokePoint;
        public Vector3 PokePoint;     // fingertip for near/poke interaction
    }

    public interface IPointerSource
    {
        PointerSample Sample();
        /// <summary>Visual feedback hook (hover target, dwell progress 0..1).</summary>
        void Feedback(Pickable hover, float progress);
    }
}
