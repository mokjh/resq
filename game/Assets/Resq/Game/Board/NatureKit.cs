using System.Collections.Generic;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Blender nature kit lookup (Resources/ResqNatureKit, built by RESQ/Art/Build Nature Kit from art/nature).
    /// Entries map a world and decor kind to the LOD0 meshes of its kit assets.
    /// </summary>
    [CreateAssetMenu(menuName = "RESQ/Nature Kit")]
    public sealed class NatureKit : ScriptableObject
    {
        [System.Serializable]
        public sealed class Entry
        {
            public string World;      // W1..W5
            public DecorKind Kind;
            public Mesh Mesh;
            public bool Charred;      // the burnt stand-in for this kind
            public Mesh Lod1, Lod2;   // reduced meshes: LOD1 draws the instances, LOD2 casts their shadows
        }

        public List<Entry> Entries = new List<Entry>();
        public Material Material; // RESQ_Shared (instancing on)

        Dictionary<Mesh, Entry> _byMesh;

        /// <summary>Reduced meshes for a kit LOD0 mesh (falls back to the mesh itself).</summary>
        public void LodsFor(Mesh lod0, out Mesh draw, out Mesh shadow)
        {
            if (_byMesh == null)
            {
                _byMesh = new Dictionary<Mesh, Entry>();
                foreach (var e in Entries) if (e.Mesh != null) _byMesh[e.Mesh] = e;
            }
            draw = shadow = lod0;
            if (lod0 != null && _byMesh.TryGetValue(lod0, out var en))
            {
                if (en.Lod1 != null) draw = en.Lod1;
                shadow = en.Lod2 != null ? en.Lod2 : draw;
            }
        }

        static NatureKit _cached;
        static bool _looked;

        public static NatureKit Load()
        {
            if (!_looked) { _cached = Resources.Load<NatureKit>("ResqNatureKit"); _looked = true; }
            return _cached;
        }

        /// <summary>Backdrop tiles for a world (320 mm, front = +Z after import), in a stable order.</summary>
        public List<Mesh> Backdrops(string world)
        {
            var l = new List<Mesh>();
            foreach (var e in Entries) if (e.World == world && e.Kind == DecorKind.Backdrop && e.Mesh != null) l.Add(e.Mesh);
            l.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return l;
        }

        public int Fill(string world, Dictionary<DecorKind, Mesh[]> meshes, Dictionary<DecorKind, Mesh> charred)
        {
            var lists = new Dictionary<DecorKind, List<Mesh>>();
            int n = 0;
            foreach (var e in Entries)
            {
                if (e.World != world || e.Mesh == null) continue;
                if (e.Charred) { charred[e.Kind] = e.Mesh; continue; }
                if (!lists.TryGetValue(e.Kind, out var l)) lists[e.Kind] = l = new List<Mesh>();
                l.Add(e.Mesh);
                n++;
            }
            foreach (var kv in lists) meshes[kv.Key] = kv.Value.ToArray();
            return n;
        }
    }
}
