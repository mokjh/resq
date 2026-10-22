using System.Linq;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// unity command run_script --file tools/unity/BuildSmokeScene.cs --entry ResqSmoke.Build
public static class ResqSmoke
{
    const string ScenePath = "Assets/Resq/Scenes/Smoke_MR.unity";

    public static string Build()
    {
        System.IO.Directory.CreateDirectory("Assets/Resq/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var mainCam = GameObject.Find("Main Camera");
        if (mainCam != null) Object.DestroyImmediate(mainCam);

        var created = OVRQuickActionsAPI.AddOVRInteractionRig();

        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        var manager = rig.GetComponent<OVRManager>() ?? rig.gameObject.AddComponent<OVRManager>();
        manager.isInsightPassthroughEnabled = true;
        manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;

        // Passthrough underlay; camera clears to transparent black so the room shows through.
        rig.gameObject.AddComponent<OVRPassthroughLayer>();
        foreach (var cam in rig.GetComponentsInChildren<Camera>())
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
        }

        // A stand-in table top and a grabbable cube on it.
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "SmokeTable";
        table.transform.position = new Vector3(0f, 0.72f, 0.55f);
        table.transform.localScale = new Vector3(0.72f, 0.02f, 0.54f);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "SmokeCube";
        cube.transform.localScale = Vector3.one * 0.06f;
        cube.transform.position = new Vector3(0f, 0.78f, 0.45f);
        QuickActionsAPI.AddGrabInteraction(cube);
        var light = GameObject.Find("Directional Light");
        if (light != null) light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        return $"saved {ScenePath}; rig roots={created?.Count()}";
    }
}
