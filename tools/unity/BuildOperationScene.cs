using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// unity command run_script --file tools/unity/BuildOperationScene.cs --entry ResqOperationScene.Build
public static class ResqOperationScene
{
    const string ScenePath = "Assets/Resq/Scenes/Operation_Desktop.unity";
    const string MatDir = "Assets/Resq/Game/Materials";

    public static string Build()
    {
        System.IO.Directory.CreateDirectory(MatDir);
        var terrain = Mat("Terrain", "Resq/VertexLit");
        var water = Mat("Water", "Resq/Water");
        var marker = Mat("Marker", "Resq/VertexLit");
        var card = Mat("Card", "Resq/VertexLit");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95f, 0.86f);
        light.intensity = 1.05f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.46f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.33f, 0.32f, 0.29f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.14f, 0.13f);

        var boot = new GameObject("GameBootstrap").AddComponent<Resq.Game.GameBootstrap>();
        boot.TerrainMaterial = terrain; boot.WaterMaterial = water; boot.MarkerMaterial = marker;
        boot.CardMaterial = card; boot.ActorMaterial = terrain;

        EditorSceneManager.SaveScene(scene, ScenePath);
        return "saved " + ScenePath;
    }

    static Material Mat(string name, string shader)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader)) { name = name, enableInstancing = true };
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }
}
