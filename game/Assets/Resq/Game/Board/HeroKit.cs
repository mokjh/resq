using System;
using System.Collections.Generic;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Textured hero models (Resources/ResqHeroKit, built by RESQ/Art/Build Hero Kit from Assets/Resq/Art/Hero):
    /// per-world landmarks that replace the generic cottages and stand at free scenic spots, plus vehicle bodies.
    /// Authored at board scale (wide cell = 20 mm), pivot on the ground at the footprint centre. Presentation only.
    /// </summary>
    [CreateAssetMenu(menuName = "RESQ/Hero Kit")]
    public sealed class HeroKit : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public Mesh Mesh;
            public Material Material;
            [Tooltip("Yaw that turns the model's front towards +Z.")]
            public float Yaw;
            [Tooltip("Footprint in metres at wide scale (x, z), from the mesh bounds.")]
            public Vector2 Footprint;
            [Tooltip("Vertical offset in metres (boats sit slightly into the water).")]
            public float Lift;
        }

        public List<Entry> Entries = new List<Entry>();

        [Tooltip("Landmarks per world, in placement order (group homes first, then scenic spots).")]
        public string[] W1 = { "chalet_a", "cottage_stone", "church" }, W2 = { "ranger_station", "lookout_tower", "tanker_truck" },
                        W3 = { "mountain_hut", "snowcat" }, W4 = { "harbour_houses", "lighthouse" }, W5 = { "basalt_house" };

        [Tooltip("Landmarks that are never homes: they only stand at scenic spots, and homes never repeat them.")]
        public string[] Scenic = { "church", "lookout_tower", "lighthouse", "tanker_truck", "snowcat", "bridge_stone", "fishing_boat" };

        public string Bus = "bus_city", Skiff = "boat_rescue", Crawler = "bulldozer";

        static HeroKit _cached;
        static bool _looked;

        public static HeroKit Load()
        {
            if (!_looked) { _cached = Resources.Load<HeroKit>("ResqHeroKit"); _looked = true; }
            return _cached;
        }

        public Entry Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in Entries) if (e.Id == id && e.Mesh != null && e.Material != null) return e;
            return null;
        }

        public string[] Landmarks(string world) =>
            world == "W1" ? W1 : world == "W2" ? W2 : world == "W3" ? W3 : world == "W4" ? W4 : world == "W5" ? W5 : Array.Empty<string>();

        public bool IsScenic(string id) => Array.IndexOf(Scenic, id) >= 0;

        public static GameObject Spawn(Entry e, Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0, e.Lift, 0);
            body.AddComponent<MeshFilter>().sharedMesh = e.Mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = e.Material;
            return go;
        }
    }
}
