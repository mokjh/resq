using System.Linq;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// unity command run_script --file tools/unity/BuildOperationXRScene.cs --entry ResqOperationXRScene.Build
public static class ResqOperationXRScene
{
    const string ScenePath = "Assets/Resq/Scenes/Operation_XR.unity";
    const string MatDir = "Assets/Resq/Game/Materials";

    public static string Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        OVRQuickActionsAPI.AddOVRInteractionRig();
        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        var manager = rig.GetComponent<OVRManager>() ?? rig.gameObject.AddComponent<OVRManager>();
        manager.isInsightPassthroughEnabled = true;
        manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        // MRUK table placement needs the scene permission (Space Setup data); ask for it at startup
        var mso = new SerializedObject(manager);
        var scenePerm = mso.FindProperty("requestScenePermissionOnStartup");
        if (scenePerm != null) { scenePerm.boolValue = true; mso.ApplyModifiedPropertiesWithoutUndo(); }
        rig.gameObject.AddComponent<OVRPassthroughLayer>();
        foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.02f;
        }
        // Waz lesson: SDK hand meshes casting real-time shadows tripled draw calls on Quest.
        int noShadow = 0;
        foreach (var r in rig.transform.root.GetComponentsInChildren<Renderer>(true))
        { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; noShadow++; }
        foreach (var root in scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }

        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95f, 0.86f);
        light.intensity = 1.05f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.46f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.33f, 0.32f, 0.29f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.14f, 0.13f);

        // MR Utility Kit: the room model from Space Setup (device data only: a fallback prefab room would put the
        // board on a table that isn't in the player's room); Resq.XR.TablePlacement reads its tables
        var mrukGo = new GameObject("MRUK");
        var mruk = mrukGo.AddComponent<Meta.XR.MRUtilityKit.MRUK>();
        if (mruk.SceneSettings == null) mruk.SceneSettings = new Meta.XR.MRUtilityKit.MRUK.MRUKSettings();
        mruk.SceneSettings.DataSource = Meta.XR.MRUtilityKit.MRUK.SceneDataSource.Device;
        mruk.SceneSettings.LoadSceneOnStartup = true;

        var boot = new GameObject("GameBootstrap").AddComponent<Resq.Game.GameBootstrap>();
        boot.TerrainMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Terrain.mat");
        boot.WaterMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Water.mat");
        boot.MarkerMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Marker.mat");
        boot.CardMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Card.mat");
        boot.ActorMaterial = boot.TerrainMaterial;
        boot.Guided = false; // guided tutorial UI lands in P1 step 7

        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && !s.path.Contains("Smoke_MR")).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        return $"saved {ScenePath}; shadows off on {noShadow} rig renderers; scene permission {(scenePerm != null ? "requested" : "field missing")}; MRUK device data";
    }
}
