using System.IO;
using System.Linq;
using Resq.Game;
using UnityEditor;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Builds Resources/ResqNatureKit.asset from Assets/Resq/Art/Nature/W1..W5 (copied from art/nature, assets that
    /// pass every Blender check only). Maps each asset id to a scatter <see cref="DecorKind"/>; `_charred` twins become
    /// the burnt stand-in for their kind. Uses the LOD0 mesh (instanced scatter draws one mesh per batch).
    /// </summary>
    public static class NatureKitBuilder
    {
        const string KitPath = "Assets/Resq/Content/Resources/ResqNatureKit.asset";
        const string Root = "Assets/Resq/Art/Nature";

        [MenuItem("RESQ/Art/Build Nature Kit")]
        public static void Build()
        {
            var kit = AssetDatabase.LoadAssetAtPath<NatureKit>(KitPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<NatureKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            kit.Entries.Clear();
            kit.Material = AssetDatabase.LoadAssetAtPath<Material>(ResqArtImporter.SharedMaterialPath);
            int skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string id = Path.GetFileNameWithoutExtension(path);
                string world = Path.GetFileName(Path.GetDirectoryName(path));
                bool charred = id.EndsWith("_charred") || id.Contains("dead_charred");
                var kind = KindFor(id);
                var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>()
                    .OrderBy(m => ResqArtImporter.LodIndex(m.name) < 0 ? 99 : ResqArtImporter.LodIndex(m.name)).FirstOrDefault();
                if (kind == null || mesh == null) { skipped++; continue; }
                // the first charred twin per kind is the burnt stand-in; dead snags stand in for conifers in W2
                if (charred && kit.Entries.Any(e => e.World == world && e.Kind == kind.Value && e.Charred)) continue;
                kit.Entries.Add(new NatureKit.Entry { World = world, Kind = kind.Value, Mesh = mesh, Charred = charred });
            }
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            var summary = string.Join(", ", kit.Entries.GroupBy(e => e.World).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
            Debug.Log($"[RESQ] nature kit built: {kit.Entries.Count} entries ({summary}); {skipped} unmapped");
        }

        static DecorKind? KindFor(string id)
        {
            string s = id.Substring(3); // drop "wN_"
            if (s.StartsWith("olive")) return DecorKind.Orchard;
            if (s.StartsWith("dead_charred")) return DecorKind.Conifer;
            if (s.Contains("pine") || s.Contains("conifer") || s.Contains("spruce") || s.Contains("fir") || s.Contains("larch")) return DecorKind.Conifer;
            if (s.Contains("broadleaf") || s.Contains("birch") || s.StartsWith("alder") || s.Contains("autumn")) return DecorKind.Broadleaf;
            if (s.Contains("bush") || s.Contains("shrub") || s.Contains("fern_clump")) return DecorKind.Shrub;
            if (s.StartsWith("basalt")) return DecorKind.Column;
            if (s.Contains("rock") || s.Contains("boulder") || s.StartsWith("ice") || s.Contains("pumice") || s.Contains("steam_vent")) return DecorKind.Rock;
            if (s.Contains("flowers")) return DecorKind.Flower;
            if (s.Contains("reeds")) return DecorKind.Reed;
            if (s.Contains("drift") && !s.Contains("driftwood")) return DecorKind.Drift;
            if (s.Contains("grass") || s.StartsWith("fern_")) return DecorKind.Grass;
            if (s.Contains("veg_rows") || s.Contains("hay_bale")) return DecorKind.Crop;
            return DecorKind.Prop;
        }
    }
}
