using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Builds Assets/Resq/Scenes/Lookdev.unity: the look-dev quartet on a board-coloured plinth under the same
    /// single main light the operation scene uses, with a seated-view camera — the contact-sheet setup for the look review.
    /// </summary>
    public static class LookdevScene
    {
        const string ScenePath = "Assets/Resq/Scenes/Lookdev.unity";

        [MenuItem("RESQ/Art/Build Lookdev Scene")]
        public static void Build()
        {
            ResqArtImporter.CreateSharedMaterial();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Main Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.24f);

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "Plinth";
            plinth.transform.position = new Vector3(0, -0.005f, 0);
            plinth.transform.localScale = new Vector3(0.42f, 0.01f, 0.18f);
            var plinthMat = new Material(Shader.Find("Resq/VertexLit"));
            plinthMat.SetColor("_Tint", new Color(0.43f, 0.57f, 0.38f));
            plinth.GetComponent<Renderer>().sharedMaterial = plinthMat;

            float x = 0f;
            var row = new Bounds();
            bool any = false;
            foreach (var path in new[]
            {
                "Assets/Resq/Art/Lookdev/cottage/cottage.fbx", "Assets/Resq/Art/Lookdev/bridge/bridge.fbx",
                "Assets/Resq/Art/Lookdev/skiff/skiff.fbx", "Assets/Resq/Art/Lookdev/resident/resident.fbx",
                "Assets/Resq/Art/Lookdev/resident/resident_child.fbx", "Assets/Resq/Art/Lookdev/resident/resident_elder.fbx",
                "Assets/Resq/Art/Lookdev/resident/resident_wheelchair.fbx",
            })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[RESQ] lookdev: missing " + path); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var b = Bounds(inst);
                float width = b.size.x;
                inst.transform.position = new Vector3(x + width * 0.5f - b.center.x + inst.transform.position.x, -b.min.y, 0);
                x += width + 0.015f;
                var nb = Bounds(inst);
                if (!any) { row = nb; any = true; } else row.Encapsulate(nb);
            }
            // centre the row on the plinth and frame it from a seated-height three-quarter view
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<LODGroup>() != null || root.name.StartsWith("resident"))
                    root.transform.position -= new Vector3(row.center.x, 0, 0);
            plinth.transform.localScale = new Vector3(row.size.x + 0.06f, 0.01f, 0.18f);

            var cam = new GameObject("LookdevCamera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.01f;
            cam.fieldOfView = 34f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.14f);
            float dist = row.size.x * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / 1.6f + 0.1f;
            cam.transform.position = new Vector3(0, dist * 0.45f, -dist);
            cam.transform.LookAt(new Vector3(0, 0.02f, 0));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[RESQ] lookdev scene saved: " + ScenePath);
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
