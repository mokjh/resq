using System.IO;
using System.Linq;
using Resq.Game;
using UnityEditor;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>Builds Resources/ResqActorKit.asset from the imported look-dev models (rerun after new kit art lands).</summary>
    public static class ActorKitBuilder
    {
        const string KitPath = "Assets/Resq/Content/Resources/ResqActorKit.asset";
        const string Look = "Assets/Resq/Art/Lookdev/";

        [MenuItem("RESQ/Art/Build Actor Kit")]
        public static void Build()
        {
            var kit = AssetDatabase.LoadAssetAtPath<ActorKit>(KitPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<ActorKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            kit.ResidentAdult = Model("resident/resident.fbx");
            kit.ResidentChild = Model("resident/resident_child.fbx");
            kit.ResidentElder = Model("resident/resident_elder.fbx");
            kit.ResidentWheelchair = Model("resident/resident_wheelchair.fbx");
            // rigged Meshy residents replace the vertex-colour figures when they're imported (wheelchair stays kit art)
            var woman = Rigged("resident_woman", out kit.AdultMaterial, out kit.AdultAnim);
            if (woman != null)
            {
                kit.ResidentAdult = woman;
                kit.ResidentAdultB = Rigged("resident_man", out kit.AdultBMaterial, out kit.AdultBAnim);
                kit.ResidentElder = Rigged("resident_elder", out kit.ElderMaterial, out kit.ElderAnim) ?? kit.ResidentElder;
                kit.ResidentChild = null;   // children: the adult rig scaled down with a larger head
                kit.SeatedRider = Rigged("resident_seated", out kit.SeatedMaterial, out kit.SeatedAnim);
                kit.WheelchairChair = Model("resident/wheelchair_chair.fbx");
                var crewTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ResqArtImporter.ResidentRoot + "crew_base_color.png");
                if (crewTex != null)
                {
                    string cp = ResqArtImporter.ResidentRoot + "crew_Resident.mat";
                    var cm = AssetDatabase.LoadAssetAtPath<Material>(cp);
                    if (cm == null) { cm = new Material(Shader.Find("Resq/Hero")); AssetDatabase.CreateAsset(cm, cp); }
                    cm.SetTexture("_BaseMap", crewTex);
                    cm.enableInstancing = true;
                    EditorUtility.SetDirty(cm);
                    kit.CrewMaterial = cm;
                }
            }
            kit.Skiff = Model("skiff/skiff.fbx");
            kit.Bridge = Model("bridge/bridge.fbx");
            kit.Cottage = Model("cottage/cottage.fbx");
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log("[RESQ] actor kit built: " + KitPath);
        }

        /// <summary>A rigged resident: model, a Resq/Hero material with its texture, and a one-state walk controller.</summary>
        static GameObject Rigged(string id, out Material mat, out RuntimeAnimatorController anim)
        {
            mat = null; anim = null;
            string root = ResqArtImporter.ResidentRoot;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(root + id + ".fbx");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(root + id + "_base_color.png");
            var clip = AssetDatabase.LoadAllAssetsAtPath(root + id + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (go == null || tex == null || clip == null) { if (go != null) Debug.LogWarning("[RESQ] actor kit: incomplete rig " + id); return null; }
            string matPath = root + id + "_Resident.mat";
            mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Resq/Hero")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", tex);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            string ctlPath = root + id + "_Walk.controller";
            var ctl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ctlPath)
                      ?? UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(ctlPath, clip);
            anim = ctl;
            return go;
        }

        static GameObject Model(string rel)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Look + rel);
            if (go == null) Debug.LogWarning("[RESQ] actor kit: missing " + Look + rel);
            return go;
        }
    }
}
