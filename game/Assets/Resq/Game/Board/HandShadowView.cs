using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Real hands cast soft shadows on the miniature (BUILD_PLAN P4.2): a dark disc under each palm and a small one
    /// under each pointing fingertip, projected straight down onto the terrain. A closer hand gives a smaller, darker
    /// shadow, so players can judge their height above the board before they touch it, as with a real tabletop.
    /// The terrain carries a collider on the Ignore Raycast layer (BoardView), which pointer picks never see.
    /// Presentation only: the XR layer (XrHandShadows) or a test supplies the points each frame.
    /// </summary>
    public sealed class HandShadowView : MonoBehaviour
    {
        public const int TerrainLayer = 2;          // Unity's built-in Ignore Raycast layer
        public const int Slots = 4;                 // two palms, two fingertips
        [Tooltip("Above this height (m) a hand casts no shadow.")]
        public float MaxHeight = 0.30f;
        // URP blends in linear space, so a half-transparent black reads far weaker on screen than its alpha
        // suggests: 0.65 only darkened grass by ~27% in an Editor capture (30 Oct). These values read at a glance.
        public float NearOpacity = 0.9f, FarOpacity = 0.15f;

        readonly Transform[] _blobs = new Transform[Slots];
        readonly float[] _opacity = new float[Slots];
        readonly Material[] _mats = new Material[Slots];   // one per slot keeps the SRP Batcher (a property block would opt out)
        static Mesh _disc;
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        /// <summary>Opacity drawn for a slot last frame (0 when hidden), for tests and diagnostics.</summary>
        public float Opacity(int slot) => _blobs[slot] != null && _blobs[slot].gameObject.activeSelf ? _opacity[slot] : 0f;
        public Transform Blob(int slot) => _blobs[slot];

        void Awake()
        {
            if (_disc == null) _disc = BuildDisc(24);
            var sh = Shader.Find("Resq/Overlay");
            for (int i = 0; i < Slots; i++)
            {
                var go = new GameObject("HandShadow_" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _disc;
                var r = go.AddComponent<MeshRenderer>();
                _mats[i] = sh != null ? new Material(sh) { name = "HandShadow_" + i } : null;
                r.sharedMaterial = _mats[i];
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _blobs[i] = go.transform;
            }
        }

        /// <summary>One frame of shadows: points[i] is a palm or fingertip, radii[i] its shadow radius (m) when
        /// touching the board (it grows by 2.2x at MaxHeight). Slots beyond count are hidden.</summary>
        public void Frame(int count, Vector3[] points, float[] radii)
        {
            for (int i = 0; i < Slots; i++)
            {
                bool shown = i < count && Cast(i, points[i], radii[i]);
                if (_blobs[i].gameObject.activeSelf != shown) _blobs[i].gameObject.SetActive(shown);
            }
        }

        bool Cast(int slot, Vector3 point, float radius)
        {
            if (!Physics.Raycast(point, Vector3.down, out var hit, MaxHeight, 1 << TerrainLayer, QueryTriggerInteraction.Ignore)) return false;
            float t = Mathf.Clamp01(hit.distance / MaxHeight);
            float r = radius * Mathf.Lerp(1f, 2.2f, t);
            var b = _blobs[slot];
            b.SetPositionAndRotation(hit.point + hit.normal * 0.0015f, Quaternion.FromToRotation(Vector3.up, hit.normal));
            var parentScale = transform.lossyScale;
            b.localScale = new Vector3(r / Mathf.Max(1e-4f, parentScale.x), 1f, r / Mathf.Max(1e-4f, parentScale.z));
            _opacity[slot] = Mathf.Lerp(NearOpacity, FarOpacity, t * t);
            if (_mats[slot] != null) _mats[slot].SetFloat(OpacityId, _opacity[slot]);
            return true;
        }

        void OnDestroy() { foreach (var m in _mats) if (m != null) Destroy(m); }

        /// <summary>Unit disc in XZ: black with a soft core (nearly opaque to 55% of the radius) fading to clear at
        /// the rim, so the shadow reads at a glance; a plain centre-to-rim fade only darkens a dot. Resq/Overlay
        /// blends by vertex alpha.</summary>
        static Mesh BuildDisc(int segments)
        {
            const float Core = 0.55f; const byte CoreAlpha = 235;
            var v = new Vector3[1 + segments * 2]; var c = new Color32[v.Length]; var tri = new int[segments * 9];
            v[0] = Vector3.zero; c[0] = new Color32(0, 0, 0, 255);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[1 + i] = dir * Core; c[1 + i] = new Color32(0, 0, 0, CoreAlpha);
                v[1 + segments + i] = dir; c[1 + segments + i] = new Color32(0, 0, 0, 0);
            }
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments, ci = 1 + i, cj = 1 + j, ri = 1 + segments + i, rj = 1 + segments + j, t = i * 9;
                tri[t] = 0; tri[t + 1] = cj; tri[t + 2] = ci;                       // core fan
                tri[t + 3] = ci; tri[t + 4] = cj; tri[t + 5] = rj;                  // fade ring
                tri[t + 6] = ci; tri[t + 7] = rj; tri[t + 8] = ri;
            }
            var m = new Mesh { name = "ShadowDisc", vertices = v, colors32 = c, triangles = tri };
            m.RecalculateBounds();
            return m;
        }
    }
}
