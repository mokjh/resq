using System.IO;
using System.Linq;
using Resq.Game;
using UnityEditor;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Builds Resources/ResqHeroKit.asset from Assets/Resq/Art/Hero (exported by tools/blender/hero_prep.py):
    /// one Resq/Hero material per model with its base-colour texture (imported at 1K for Quest).
    /// </summary>
    public static class HeroKitBuilder
    {
        const string KitPath = "Assets/Resq/Content/Resources/ResqHeroKit.asset";
        public const string HeroRoot = "Assets/Resq/Art/Hero/";

        [MenuItem("RESQ/Art/Build Hero Kit")]
        public static void Build()
        {
            var kit = AssetDatabase.LoadAssetAtPath<HeroKit>(KitPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<HeroKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            var shader = Shader.Find("Resq/Hero");
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { HeroRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string id = Path.GetFileNameWithoutExtension(path);
                var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HeroRoot + id + "_base_color.png");
                if (mesh == null || tex == null) { Debug.LogWarning("[RESQ] hero kit: incomplete " + id); continue; }
                string matPath = HeroRoot + id + "_Hero.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
                mat.shader = shader;
                mat.SetTexture("_BaseMap", tex);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                var e = kit.Entries.Find(x => x.Id == id);
                if (e == null) { e = new HeroKit.Entry { Id = id }; kit.Entries.Add(e); }
                e.Mesh = mesh; e.Material = mat;
                e.Footprint = new Vector2(mesh.bounds.size.x, mesh.bounds.size.z);
            }
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RESQ] hero kit built: {kit.Entries.Count} entries");
        }
    }
}
