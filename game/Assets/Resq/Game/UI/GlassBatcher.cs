using System.Collections.Generic;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Draw-call budget for the HUD (Quest ≤150 per frame): merges every glass panel and icon under one root into a
    /// single mesh drawn by one renderer, instead of one draw per edge, fill and icon. The original renderers stay
    /// in the hierarchy (pickables, colliders and SetActive logic are untouched) but are switched off. The merged
    /// mesh rebuilds only when something under the root is shown, hidden or moved. Draw order is kept: edges
    /// (sorting 0), then fills (1), then icons (2), each in hierarchy order; text (3) still draws on top.
    /// Put it only on roots whose panels sit still relative to the root; moving pieces (tooltip, bubbles) stay out.
    /// </summary>
    public sealed class GlassBatcher : MonoBehaviour
    {
        Material _glass;
        MeshRenderer _out;
        Mesh _mesh;
        readonly List<MeshRenderer> _items = new List<MeshRenderer>();
        readonly List<Matrix4x4> _lastMatrix = new List<Matrix4x4>();
        readonly List<bool> _lastActive = new List<bool>();
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Color> _c = new List<Color>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _t = new List<int>();
        readonly List<Vector3> _tmpV = new List<Vector3>();
        readonly List<Color> _tmpC = new List<Color>();
        readonly List<Vector2> _tmpUv = new List<Vector2>();
        readonly List<int> _tmpT = new List<int>();
        public int MergedCount { get; private set; }

        public static GlassBatcher Attach(Transform root, Material glass)
        {
            var b = root.gameObject.AddComponent<GlassBatcher>();
            b._glass = glass;
            return b;
        }

        void Start() { Collect(); Rebuild(); }

        /// <summary>Re-scan the root (call after adding panels at runtime).</summary>
        public void Collect()
        {
            _items.Clear(); _lastMatrix.Clear(); _lastActive.Clear();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == _out || r.sharedMaterial != _glass) continue;
                if (r.GetComponent<MeshFilter>() == null || r.GetComponent<MeshFilter>().sharedMesh == null) continue;
                if (!r.GetComponent<MeshFilter>().sharedMesh.isReadable) continue;
                _items.Add(r);
                _lastMatrix.Add(Matrix4x4.zero);
                _lastActive.Add(false);
            }
            // stable draw order: sorting order first, hierarchy order within it (GetComponentsInChildren is depth-first)
            var order = new List<int>();
            for (int i = 0; i < _items.Count; i++) order.Add(i);
            order.Sort((a, b) => _items[a].sortingOrder != _items[b].sortingOrder ? _items[a].sortingOrder.CompareTo(_items[b].sortingOrder) : a.CompareTo(b));
            var sorted = new List<MeshRenderer>(_items.Count);
            foreach (int i in order) sorted.Add(_items[i]);
            _items.Clear(); _items.AddRange(sorted);
            if (_out == null)
            {
                var go = new GameObject("GlassBatch");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh = new Mesh { name = "GlassBatch_" + name };
                _mesh.MarkDynamic();
                _out = go.AddComponent<MeshRenderer>();
                _out.sharedMaterial = _glass;
                _out.sortingOrder = 1;
                _out.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _out.receiveShadows = false;
            }
            foreach (var r in _items) r.enabled = false;
        }

        void LateUpdate()
        {
            var toRoot = transform.worldToLocalMatrix;
            bool dirty = false;
            for (int i = 0; i < _items.Count; i++)
            {
                var r = _items[i];
                bool active = r != null && r.gameObject.activeInHierarchy;
                var m = active ? toRoot * r.transform.localToWorldMatrix : Matrix4x4.zero;
                if (active != _lastActive[i] || (active && m != _lastMatrix[i])) { dirty = true; _lastActive[i] = active; _lastMatrix[i] = m; }
            }
            if (dirty) Rebuild();
        }

        void Rebuild()
        {
            _v.Clear(); _c.Clear(); _uv.Clear(); _t.Clear();
            var toRoot = transform.worldToLocalMatrix;
            int n = 0;
            foreach (var r in _items)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                var src = r.GetComponent<MeshFilter>().sharedMesh;
                var m = toRoot * r.transform.localToWorldMatrix;
                int baseV = _v.Count;
                src.GetVertices(_tmpV); src.GetColors(_tmpC); src.GetUVs(0, _tmpUv); src.GetTriangles(_tmpT, 0);
                for (int k = 0; k < _tmpV.Count; k++)
                {
                    _v.Add(m.MultiplyPoint3x4(_tmpV[k]));
                    _c.Add(k < _tmpC.Count ? _tmpC[k] : Color.white);
                    _uv.Add(k < _tmpUv.Count ? _tmpUv[k] : Vector2.zero);
                }
                foreach (int idx in _tmpT) _t.Add(baseV + idx);
                n++;
            }
            _mesh.Clear();
            _mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
            MergedCount = n;
        }
    }
}
