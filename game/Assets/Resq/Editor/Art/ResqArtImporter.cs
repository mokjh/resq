using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Import contract for the RESQ art kit (everything under Assets/Resq/Art). Mirrors the Blender kit conventions
    /// (tools/blender/kit.py, art/lookdev/*.json): metres, Z-up converted on import, one shared RESQ Lit material,
    /// vertex RGB albedo + A AO, `_LOD0..2` children become a LODGroup, `_COL` meshes become colliders, `_mk_` empties
    /// stay as markers. Nothing here alters gameplay; the simulation never reads art.
    /// </summary>
    public sealed class ResqArtImporter : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Resq/Art/";
        public const string SharedMaterialPath = ArtRoot + "Shared/RESQ_Shared.mat";
        public const string AtlasPath = ArtRoot + "Shared/palette_atlas.png";

        static bool IsArt(string path) => path.StartsWith(ArtRoot) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        /// <summary>Textured hero models (Meshy, prepared by hero_prep.py) keep their own Resq/Hero material and UVs.</summary>
        public static bool IsHero(string path) => path.StartsWith(HeroKitBuilder.HeroRoot);

        void OnPreprocessTexture()
        {
            if (!IsHero(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 1024;   // Quest budget: 1K per hero, ASTC by platform default
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        void OnPreprocessModel()
        {
            if (!IsArt(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importVisibility = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Low;
            mi.importNormals = ModelImporterNormals.Import;
            mi.addCollider = false;
            mi.preserveHierarchy = true;
            bool rigged = Path.GetFileNameWithoutExtension(assetPath).StartsWith("resident");
            mi.animationType = rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.importAnimation = rigged;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsArt(assetPath) || IsHero(assetPath)) return;
            var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            var lods = new SortedDictionary<int, List<Renderer>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (n.EndsWith("_COL"))
                {
                    // proxy collider: keep the mesh as a MeshCollider, drop the visual
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mc = r.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;
                    }
                    Object.DestroyImmediate(r);
                    if (mf != null) Object.DestroyImmediate(mf);
                    continue;
                }
                if (shared != null)
                {
                    var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = shared;
                    r.sharedMaterials = mats;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                int lod = LodIndex(n);
                if (lod >= 0)
                {
                    if (!lods.TryGetValue(lod, out var list)) lods[lod] = list = new List<Renderer>();
                    list.Add(r);
                }
            }
            if (lods.Count > 1)
            {
                var group = root.GetComponent<LODGroup>() ?? root.AddComponent<LODGroup>();
                // diorama props are small on screen: transitions tuned for a 0.6 m seated view of a 0.64 m board
                float[] heights = { 0.12f, 0.05f, 0.015f };
                var arr = new List<LOD>();
                int k = 0;
                foreach (var kv in lods)
                {
                    float h = k < heights.Length ? heights[k] : heights[heights.Length - 1] * 0.5f;
                    arr.Add(new LOD(h, kv.Value.ToArray()));
                    k++;
                }
                group.SetLODs(arr.ToArray());
                group.RecalculateBounds();
            }
        }

        public static int LodIndex(string name)
        {
            int i = name.LastIndexOf("_LOD");
            if (i < 0 || i + 4 >= name.Length) return -1;
            return int.TryParse(name.Substring(i + 4), out int lod) ? lod : -1;
        }

        // ------------------------------------------------------------------ menu: setup / sync / validate
        [MenuItem("RESQ/Art/Create Shared Material")]
        public static void CreateSharedMaterial()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SharedMaterialPath));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Resq/Lit")) { name = "RESQ_Shared" };
                AssetDatabase.CreateAsset(mat, SharedMaterialPath);
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas != null) mat.SetTexture("_Atlas", atlas);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[RESQ] shared material ready: " + SharedMaterialPath);
        }

        /// <summary>Re-imports every art model so the contract above applies (after the shared material exists).</summary>
        [MenuItem("RESQ/Art/Reimport Art")]
        public static void ReimportArt()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArtRoot.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log("[RESQ] art reimported");
        }

        /// <summary>
        /// Validates every imported model against its Blender sidecar (same stem, .json) and the Quest budgets.
        /// Writes art/IMPORT_REPORT.md (automated evidence) and returns the number of failures.
        /// </summary>
        [MenuItem("RESQ/Art/Validate Art")]
        public static int Validate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Art import report (automated, Unity Editor)");
            sb.AppendLine();
            sb.AppendLine("Generated by `RESQ/Art/Validate Art`. Checks each model in `Assets/Resq/Art` against its Blender sidecar and the Quest budgets. Evidence kind: automated (Editor import); device and human review pending.");
            sb.AppendLine();
            sb.AppendLine("| Model | Check | Result |");
            sb.AppendLine("|---|---|---|");
            int failures = 0;
            var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArtRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (IsHero(path))
                {
                    // hero contract: one textured mesh, ≤ 6k triangles, grounded pivot, footprint within 6 cells (120 mm)
                    var hm = go.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null).ToList();
                    int htris = hm.Sum(m => m.triangles.Length / 3);
                    bool uvs = hm.All(m => m.uv != null && m.uv.Length == m.vertexCount);
                    var hb = hm.Count > 0 ? hm[0].bounds : new Bounds();
                    void HRow(string check, bool ok, string detail) { if (!ok) failures++; sb.AppendLine($"| {name} | {check} | {(ok ? "ok" : "**FAIL**")} {detail} |"); }
                    HRow("hero triangles ≤ 6000", htris <= 6000, $"({htris})");
                    HRow("hero UVs present", uvs, "");
                    HRow("hero pivot on ground (min y ≈ 0)", Mathf.Abs(hb.min.y) < 0.0005f, $"(min y {hb.min.y * 1000f:F2} mm)");
                    HRow("hero footprint ≤ 120 mm", hb.size.x <= 0.1205f && hb.size.z <= 0.1205f, $"({hb.size.x * 1000f:F0}×{hb.size.z * 1000f:F0} mm)");
                    HRow("hero base-colour texture", AssetDatabase.LoadAssetAtPath<Texture2D>(HeroKitBuilder.HeroRoot + name + "_base_color.png") != null, "");
                    continue;
                }
                var sidecar = LoadSidecar(path);
                void Row(string check, bool ok, string detail)
                {
                    if (!ok) failures++;
                    sb.AppendLine($"| {name} | {check} | {(ok ? "ok" : "**FAIL**")} {detail} |");
                }
                var filters = go.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToList();
                var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToList();
                var meshes = filters.Select(f => (f.gameObject.name, f.sharedMesh))
                                    .Concat(skinned.Select(s => (s.gameObject.name, s.sharedMesh))).ToList();
                // triangles per LOD vs sidecar and budget
                // BUILD_PLAN art budgets: residents 1.2k, vehicles/heroes 4k, buildings and props 2.5k (LOD0)
                int budget = name.StartsWith("resident") ? 1200
                           : name.StartsWith("skiff") || name.StartsWith("bus") || name.StartsWith("crawler") || name.StartsWith("winch") ? 4000 : 2500;
                foreach (var (meshName, mesh) in meshes)
                {
                    int tris = mesh.triangles.Length / 3;
                    int lod = LodIndex(meshName);
                    if (lod == 0) Row($"{meshName} triangles ≤ {budget}", tris <= budget, $"({tris})");
                    if (sidecar != null && sidecar.TryGetValue("tri:" + meshName, out float expected))
                        Row($"{meshName} triangles match Blender", Mathf.Abs(tris - expected) < 0.5f, $"({tris} vs {expected})");
                    bool colours = mesh.colors32 != null && mesh.colors32.Length == mesh.vertexCount;
                    Row($"{meshName} vertex colours (albedo + AO)", colours, "");
                }
                // size and grounding of LOD0 against the sidecar's Unity-space size
                var lod0 = meshes.FirstOrDefault(m => LodIndex(m.name) == 0);
                if (lod0.sharedMesh != null && sidecar != null && sidecar.TryGetValue("size_x", out float sx))
                {
                    var b = lod0.sharedMesh.bounds;
                    var t = FindTransform(go.transform, lod0.name);
                    Vector3 size = Vector3.Scale(b.size, t != null ? t.lossyScale : Vector3.one) * 1000f;
                    Vector3 want = new Vector3(sx, sidecar["size_y"], sidecar["size_z"]);
                    bool sizeOk = Within(size.x, want.x) && Within(size.y, want.y) && Within(size.z, want.z);
                    Row("LOD0 size matches Blender (±1%, axes converted)", sizeOk, $"({size.x:F1}×{size.y:F1}×{size.z:F1} mm vs {want.x:F1}×{want.y:F1}×{want.z:F1})");
                }
                // shared material only
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                bool oneMat = renderers.All(r => r.sharedMaterials.All(m => m == shared));
                Row("single RESQ_Shared material", shared != null && oneMat, shared == null ? "(material missing: run Create Shared Material)" : "");
                // colliders when the sidecar declares them
                if (sidecar != null && sidecar.ContainsKey("has_colliders"))
                    Row("proxy colliders imported", go.GetComponentsInChildren<MeshCollider>(true).Length > 0, "");
                bool lodGroup = meshes.Count(m => LodIndex(m.name) >= 0) <= 1 || go.GetComponent<LODGroup>() != null;
                Row("LODGroup built", lodGroup, "");
            }
            sb.AppendLine();
            sb.AppendLine(failures == 0 ? "**All checks passed.**" : $"**{failures} check(s) failed.**");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            File.WriteAllText(Path.Combine(repo, "art", "IMPORT_REPORT.md"), sb.ToString());
            Debug.Log($"[RESQ] art validation: {failures} failure(s); report at art/IMPORT_REPORT.md");
            return failures;
        }

        static float Num(Resq.Sim.JsonNode n) =>
            float.Parse(n.ToCanonical(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);

        static bool Within(float a, float b) => Mathf.Abs(a - b) <= Mathf.Max(0.5f, Mathf.Abs(b) * 0.01f);

        static Transform FindTransform(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var t = FindTransform(c, name); if (t != null) return t; }
            return null;
        }

        /// <summary>Reads the few sidecar fields the validator needs (triangles per mesh, Unity-space LOD0 size, colliders).</summary>
        static Dictionary<string, float> LoadSidecar(string modelPath)
        {
            string json = Path.ChangeExtension(modelPath, ".json");
            if (!File.Exists(json)) return null;
            var doc = Resq.Sim.JsonNode.Parse(File.ReadAllText(json));
            var d = new Dictionary<string, float>();
            var tris = doc["triangles"];
            if (tris != null) foreach (var kv in tris.Properties()) d["tri:" + kv.Key] = kv.Value.AsLong();
            var size = doc["bounds_lod0_unity_mm_size"];
            if (size != null) { d["size_x"] = Num(size[0]); d["size_y"] = Num(size[1]); d["size_z"] = Num(size[2]); }
            if (doc["colliders"] != null && doc["colliders"].Properties().Any()) d["has_colliders"] = 1;
            return d;
        }
    }
}
