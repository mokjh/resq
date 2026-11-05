using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace Resq.XR
{
    /// <summary>
    /// Shows the player's own hands in front of the diorama instead of a glove over them (BUILD_PLAN P4.2): the SDK's
    /// tracked hand meshes and a simple forearm draw with Resq/HandCutout, which punches through to passthrough. Forearm
    /// direction is an estimate (wrist toward a seated elbow), because only the hand is tracked. A headset feature: in
    /// the Editor the Simulator's hands stay visible unless RESQ_REAL_HANDS=1. Presentation only.
    /// </summary>
    public sealed class RealHandsView : MonoBehaviour
    {
        public const string DevEnv = "RESQ_REAL_HANDS";
        const float WristRadius = 0.03f, ElbowRadius = 0.048f, MaxForearm = 0.32f, MinForearm = 0.1f;

        /// <summary>True on the headset; in the Editor only with the dev variable set.</summary>
        public static bool Wanted => !Application.isEditor || System.Environment.GetEnvironmentVariable(DevEnv) == "1";

        Material _mat;
        Mesh _armMesh;
        Transform _head;
        readonly List<(IsdkHandPointer hand, Transform arm, bool left)> _arms = new List<(IsdkHandPointer, Transform, bool)>();
        public int HandMeshes { get; private set; }

        public void Init(List<IsdkHandPointer> hands, Transform head)
        {
            _head = head;
            var sh = Shader.Find("Resq/HandCutout");
            if (sh == null) { Debug.LogWarning("[RESQ XR] hand cutout shader missing: keeping the SDK hands"); return; }
            _mat = new Material(sh) { name = "RealHands" };
            foreach (var visual in FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = _mat;
                    smr.sharedMaterials = mats;
                    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    smr.receiveShadows = false;
                    HandMeshes++;
                }
            _armMesh = BuildArm(10);
            foreach (var h in hands)
            {
                var go = new GameObject("RealForearm_" + h.Kind);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _armMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _arms.Add((h, go.transform, h.Kind == Resq.Game.PointerKind.HandLeft));
            }
            Debug.Log($"[RESQ XR] real hands: {HandMeshes} hand mesh(es), {_arms.Count} forearm(s) cut through to passthrough");
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_armMesh != null) Destroy(_armMesh);
        }

        void LateUpdate()
        {
            if (_head == null) return;
            var flat = _head.forward; flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, flat);
            foreach (var (hand, arm, left) in _arms)
            {
                bool ok = hand.TryJoint(HandJointId.HandWristRoot, out var wrist);
                if (arm.gameObject.activeSelf != ok) arm.gameObject.SetActive(ok);
                if (!ok) continue;
                var elbow = _head.position + side * (left ? -0.2f : 0.2f) + Vector3.down * 0.42f - flat * 0.02f;
                var toElbow = elbow - wrist.position;
                float length = Mathf.Clamp(toElbow.magnitude, MinForearm, MaxForearm);
                arm.SetPositionAndRotation(wrist.position, Quaternion.LookRotation(toElbow.sqrMagnitude > 1e-4f ? toElbow : -flat, Vector3.up));
                arm.localScale = new Vector3(1f, 1f, length);
            }
        }

        /// <summary>A tapered tube along +Z, unit length (scaled per frame), with a rounded start inside the wrist.</summary>
        static Mesh BuildArm(int sides)
        {
            float[] rings = { -0.06f, 0f, 1f };
            float[] radii = { WristRadius * 0.55f, WristRadius, ElbowRadius };
            var v = new Vector3[rings.Length * sides]; var n = new Vector3[v.Length];
            for (int ring = 0; ring < rings.Length; ring++)
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v[ring * sides + s] = dir * radii[ring] + Vector3.forward * rings[ring];
                    n[ring * sides + s] = dir;
                }
            var t = new int[(rings.Length - 1) * sides * 6];
            int k = 0;
            for (int ring = 0; ring < rings.Length - 1; ring++)
                for (int s = 0; s < sides; s++)
                {
                    int a = ring * sides + s, b = ring * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d;
                }
            var m = new Mesh { name = "RealForearm", vertices = v, normals = n, triangles = t };
            m.RecalculateBounds();
            return m;
        }
    }
}
