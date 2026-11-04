using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// "Your valley is this way": when the board is out of view, a small glass hint eases into the player's gaze with
    /// an arrow toward it. MRUK picks the nearest table that fits, which can be beside or behind a player who started
    /// facing elsewhere (found in the Simulator, 5 Nov: nothing but their own hands in view, menu card included).
    /// Lazy-follow rather than head-locked (Meta comfort guidance), and it hides once the board is back in view.
    /// Presentation only: no colliders, no input, no simulation access.
    /// </summary>
    public sealed class BoardFinderView : MonoBehaviour
    {
        public Transform Target;
        public const float ShowAngle = 50f, HideAngle = 32f, ShowDelay = 0.6f, Distance = 0.9f;
        const float BehindAngle = 120f;
        const float Scale = 1.6f;   // text about 1.5° tall at Distance (Meta: at least 1°), pill about 33° wide

        public bool Visible => _root != null && _root.gameObject.activeSelf;
        public string Message => _label != null ? _label.text : "";
        /// <summary>Arrow direction in the hint's plane, degrees: 0 = player's right, 90 = up, 180 = left.</summary>
        public float ArrowDegrees { get; private set; }
        public Transform Root => _root;

        Transform _root, _arrow;
        TextMeshPro _label;
        float _outFor;
        bool _snap;

        void Build()
        {
            var glass = new Material(Shader.Find("Resq/Glass"));
            _root = new GameObject("BoardFinderHint").transform;
            _root.SetParent(transform, false);
            _root.localScale = Vector3.one * Scale * Comfort.UiScale;
            UiKit.Panel(_root, "Pill", Vector3.zero, 0.34f, 0.056f, glass, 0.028f);
            var go = new GameObject("Arrow");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(-0.135f, 0, -0.0006f);
            go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh(UiKit.Accent);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = glass;
            r.sortingOrder = 2;
            _arrow = go.transform;
            _label = UiKit.Text(_root, "", new Vector3(0.02f, 0, 0), 0.16f, UiKit.Ink, true, 0.27f, 0.04f, TextAlignmentOptions.Center);
            _label.enableAutoSizing = true; _label.fontSizeMin = 0.12f; _label.fontSizeMax = 0.16f;
            _root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Target == null) return;
            if (_root == null) Build();
            var head = cam.transform;
            var to = Target.position - head.position;
            float off = Vector3.Angle(head.forward, to);
            if (Visible)
            {
                if (off < HideAngle) { _root.gameObject.SetActive(false); _outFor = 0f; return; }
            }
            else
            {
                _outFor = off > ShowAngle ? _outFor + Time.unscaledDeltaTime : 0f;
                if (_outFor < ShowDelay) return;
                _root.gameObject.SetActive(true);
                _root.localScale = Vector3.one * Scale * Comfort.UiScale;
                _snap = true;
            }

            var want = head.position + head.forward * Distance + Vector3.down * 0.05f;
            float k = _snap ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f);
            _snap = false;
            _root.position = Vector3.Lerp(_root.position, want, k);
            var look = _root.position - head.position;
            if (look.sqrMagnitude > 1e-6f) _root.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            var flatFwd = new Vector3(head.forward.x, 0f, head.forward.z);
            var flatTo = new Vector3(to.x, 0f, to.z);
            bool behind = Vector3.Angle(flatFwd, flatTo) > BehindAngle;
            var local = _root.InverseTransformDirection(to.normalized);
            // straight behind has no screen direction of its own: point to the nearer way round
            ArrowDegrees = behind ? (local.x >= 0f ? 0f : 180f) : Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            _arrow.localRotation = Quaternion.Euler(0, 0, ArrowDegrees);
            string text = behind ? "Your valley is behind you" : "Your valley is this way";
            if (_label.text != text) _label.text = text;
        }

        /// <summary>A flat arrow pointing along +X.</summary>
        static Mesh ArrowMesh(Color c)
        {
            var v = new[]
            {
                new Vector3(0.017f, 0f, 0f), new Vector3(0f, 0.015f, 0f), new Vector3(0f, -0.015f, 0f),                // head
                new Vector3(-0.017f, 0.005f, 0f), new Vector3(0f, 0.005f, 0f), new Vector3(0f, -0.005f, 0f), new Vector3(-0.017f, -0.005f, 0f),   // stem
            };
            var colors = new Color[v.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = c;
            var m = new Mesh { name = "FinderArrow", vertices = v, colors = colors, uv = new Vector2[v.Length], triangles = new[] { 1, 0, 2, 3, 4, 5, 3, 5, 6 } };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
