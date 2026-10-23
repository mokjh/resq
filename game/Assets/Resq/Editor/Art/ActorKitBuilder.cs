using System.IO;
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
            kit.Skiff = Model("skiff/skiff.fbx");
            kit.Bridge = Model("bridge/bridge.fbx");
            kit.Cottage = Model("cottage/cottage.fbx");
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log("[RESQ] actor kit built: " + KitPath);
        }

        static GameObject Model(string rel)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Look + rel);
            if (go == null) Debug.LogWarning("[RESQ] actor kit: missing " + Look + rel);
            return go;
        }
    }
}
