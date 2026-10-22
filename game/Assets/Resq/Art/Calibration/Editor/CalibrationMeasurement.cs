// Source only until compiled/run in the licensed RESQ Editor. No startup hooks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resq.Art.Calibration
{
    public static class CalibrationMeasurement
    {
        const string AssetPath = "Assets/Resq/Art/Calibration/unit.fbx";
        const float Epsilon = 0.00001f; // 10 micrometers; below the frozen 1% bound.
        [Serializable] public class Measurement
        {
            public string name;
            public Vector3 position, rotation, scale, boundsMin, boundsMax;
            public string[] materials;
            public int triangles;
        }
        [Serializable] public class Report
        {
            public string schema = "resq-calibration-unity-1";
            public string scope = "Unity import measurements only; render inspection/F06 acceptance pending";
            public string unityVersion, fbxSha256, utc, materialImportMode;
            public float globalScale, fileScale;
            public bool bakeAxisConversion, useFileScale;
            public List<Measurement> measurements = new List<Measurement>();
        }

        public static void ImportAndMeasure()
        {
            Measure(true);
        }

        // Diagnose the failed native import without repairing the source,
        // changing importer settings, or relaxing any qualification assertion.
        public static void InspectImported()
        {
            Scene preview = default(Scene);
            try
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Owned batch Editor required");
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-resqCalibrationReport");
                if (index < 0 || index+1 >= args.Length) throw new ArgumentException("Diagnostic output required");
                string destination = Path.GetFullPath(args[index+1]);
                if (!destination.StartsWith(Path.Combine(root,"evidence/calibration")+Path.DirectorySeparatorChar,StringComparison.Ordinal)
                    || File.Exists(destination)) throw new IOException("Fresh owned diagnostic output required");
                string imported = Path.Combine(root,"game",AssetPath);
                string source = Path.Combine(root,"art/calibration/unit.fbx");
                if (Hash(source) != Hash(imported)) throw new IOException("Imported FBX source differs");
                var importer = AssetImporter.GetAtPath(AssetPath) as ModelImporter;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
                if (importer == null || model == null) throw new IOException("Existing native import missing");
                preview = EditorSceneManager.NewPreviewScene();
                var instance = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(instance,preview);
                var report = new Report { scope="Native failed-import diagnosis only; no repair or F06 acceptance",
                    unityVersion=Application.unityVersion,utc=DateTime.UtcNow.ToString("o"),fbxSha256=Hash(imported),
                    globalScale=importer.globalScale,fileScale=importer.fileScale,useFileScale=importer.useFileScale,
                    bakeAxisConversion=importer.bakeAxisConversion,materialImportMode=importer.materialImportMode.ToString() };
                foreach (Transform item in instance.GetComponentsInChildren<Transform>())
                {
                    var observation = new Measurement {name=item.name,position=item.position,rotation=item.localEulerAngles,scale=item.localScale};
                    var filter = item.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        var points=filter.sharedMesh.vertices.Select(item.TransformPoint).ToArray();
                        var bounds=new Bounds(points[0],Vector3.zero);
                        foreach(var point in points) bounds.Encapsulate(point);
                        observation.boundsMin=bounds.min;observation.boundsMax=bounds.max;
                        observation.triangles=filter.sharedMesh.triangles.Length/3;
                        observation.materials=item.GetComponent<MeshRenderer>().sharedMaterials.Select(m=>m==null?"<null>":m.name).ToArray();
                    }
                    report.measurements.Add(observation);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using(var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))
                using(var writer=new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report,true));
                EditorSceneManager.ClosePreviewScene(preview);preview=default(Scene);
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);}
        }

        // Both stages use the same imported asset and Editor session. The renderer
        // still validates the retained measurement bytes and owns its exit code.
        public static void MeasureAndRender()
        {
            try
            {
                if (!Application.isBatchMode ||
                    SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Owned graphics-capable batch Editor required before import.");
                string[] args = Environment.GetCommandLineArgs();
                int output = Array.IndexOf(args, "-resqCalibrationReport");
                int input = Array.IndexOf(args, "-resqCalibrationMeasurements");
                if (output < 0 || input < 0 || output + 1 >= args.Length || input + 1 >= args.Length ||
                    Array.LastIndexOf(args, "-resqCalibrationReport") != output ||
                    Array.LastIndexOf(args, "-resqCalibrationMeasurements") != input ||
                    args[output + 1].StartsWith("-") || args[input + 1].StartsWith("-") ||
                    Path.GetFullPath(args[output + 1]) != Path.GetFullPath(args[input + 1]))
                    throw new ArgumentException("Combined rendering must consume this session's measurement output.");
                if (Measure(false)) CalibrationContactSheet.Render();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static bool Measure(bool exitAfterMeasurement)
        {
            Scene preview = default(Scene);
            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("Use a dedicated owned batch Editor.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string root = Path.GetFullPath(Path.Combine(project, ".."));
                if (!File.Exists(Path.Combine(root, "specification/00_FREEZE_INDEX.md")) ||
                    !File.Exists(Path.Combine(root, "tools/art/generate_calibration.py")) ||
                    Path.GetFileName(project) != "game")
                    throw new InvalidOperationException("Editor is not bound to RESQ/game.");
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-resqCalibrationReport");
                if (index < 0 || index + 1 >= args.Length)
                    throw new ArgumentException("-resqCalibrationReport required");
                string destination = Path.GetFullPath(args[index + 1]);
                string allowed = Path.Combine(root, "evidence/calibration") + Path.DirectorySeparatorChar;
                if (!destination.StartsWith(allowed, StringComparison.Ordinal) || File.Exists(destination))
                    throw new IOException("Use a new owned calibration report path.");
                string source = Path.Combine(root, "art/calibration/unit.fbx");
                string imported = Path.Combine(project, AssetPath);
                string hash = Hash(source);
                if (Hash(imported) != hash)
                    throw new InvalidOperationException("Unity FBX differs from retained Blender export.");
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(AssetPath) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("FBX ModelImporter missing.");
                // Explicit file-unit interpretation, never an empirical corrective scale/rotation.
                importer.globalScale = 1;
                importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                importer.isReadable = true; // Editor calibration only, excluded from runtime use.
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
                if (model == null) throw new InvalidOperationException("No imported calibration model.");
                preview = EditorSceneManager.NewPreviewScene();
                var instance = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(instance, preview);
                var report = new Report { unityVersion = Application.unityVersion,
                    fbxSha256 = hash, utc = DateTime.UtcNow.ToString("o"),
                    globalScale = importer.globalScale, fileScale = importer.fileScale,
                    useFileScale = importer.useFileScale, bakeAxisConversion = importer.bakeAxisConversion,
                    materialImportMode = importer.materialImportMode.ToString() };
                foreach (Transform item in instance.GetComponentsInChildren<Transform>())
                {
                    Near(item.localScale, Vector3.one, item.name + " scale");
                    if (Quaternion.Angle(item.localRotation, Quaternion.identity) > 0.01f)
                        throw new InvalidOperationException(item.name + " has an unexplained rotation");
                    var measured = new Measurement { name = item.name, position = item.position,
                        rotation = item.localEulerAngles, scale = item.localScale };
                    var filter = item.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        var mesh = filter.sharedMesh;
                        if (mesh == null || mesh.vertexCount == 0) throw new InvalidOperationException("Empty mesh");
                        var points = mesh.vertices.Select(item.TransformPoint).ToArray();
                        var bounds = new Bounds(points[0], Vector3.zero);
                        foreach (var point in points) bounds.Encapsulate(point);
                        measured.boundsMin = bounds.min; measured.boundsMax = bounds.max;
                        measured.triangles = mesh.triangles.Length / 3;
                        var renderer = item.GetComponent<MeshRenderer>();
                        if (renderer == null || renderer.sharedMaterials.Length != 1 ||
                            renderer.sharedMaterials.Any(m => m == null || m.shader == null ||
                            !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader"))
                            throw new InvalidOperationException(item.name + " has invalid materials/shader");
                        measured.materials = renderer.sharedMaterials.Select(m => m.name).ToArray();
                        if (measured.triangles != 12) throw new InvalidOperationException("Unexpected topology");
                    }
                    report.measurements.Add(measured);
                }
                Marker(instance, "CAL_Origin", Vector3.zero);
                Marker(instance, "CAL_RightTip", new Vector3(.06f, 0, 0));
                Marker(instance, "CAL_ForwardTip", new Vector3(0, 0, .08f));
                Marker(instance, "CAL_UpTip", new Vector3(0, .10f, 0));
                Marker(instance, "CAL_BarStart", new Vector3(-.12f, 0, -.03f));
                Marker(instance, "CAL_BarEnd", new Vector3(-.02f, 0, -.03f));
                var bar = report.measurements.Single(m => m.name == "CAL_Reference100mm");
                Near(bar.boundsMin, new Vector3(-.12f, 0, -.035f), "bar minimum");
                Near(bar.boundsMax, new Vector3(-.02f, .01f, -.025f), "bar maximum");
                BoundsAndMaterial(report, "CAL_AxisRight", new Vector3(0, 0, -.003f),
                    new Vector3(.06f, .006f, .003f), "CAL_Right_Red");
                BoundsAndMaterial(report, "CAL_AxisForward", new Vector3(-.003f, 0, 0),
                    new Vector3(.003f, .006f, .08f), "CAL_Forward_Green");
                BoundsAndMaterial(report, "CAL_AxisUp", new Vector3(-.003f, 0, -.003f),
                    new Vector3(.003f, .10f, .003f), "CAL_Up_Blue");
                BoundsAndMaterial(report, "CAL_Reference100mm", new Vector3(-.12f, 0, -.035f),
                    new Vector3(-.02f, .01f, -.025f), "CAL_Reference_Paper");
                string[] required = { "CAL_AxisRight", "CAL_AxisForward", "CAL_AxisUp", "CAL_Reference100mm" };
                if (report.measurements.Count(m => m.materials != null) != 4 ||
                    required.Any(n => report.measurements.Count(m => m.name == n) != 1))
                    throw new InvalidOperationException("Unexpected/missing calibration geometry");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report, true));
                Debug.Log("RESQ calibration measurements recorded. Unity render inspection/F06 acceptance pending.");
                EditorSceneManager.ClosePreviewScene(preview);
                preview = default(Scene);
                if (exitAfterMeasurement) EditorApplication.Exit(0);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
                return false;
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static void Marker(GameObject instance, string name, Vector3 expected)
        {
            var matches = instance.GetComponentsInChildren<Transform>().Where(t => t.name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Missing/duplicate " + name);
            Near(matches[0].position, expected, name);
        }
        static void BoundsAndMaterial(Report report, string name, Vector3 min, Vector3 max, string material)
        {
            var item = report.measurements.Single(m => m.name == name);
            Near(item.boundsMin, min, name + " minimum");
            Near(item.boundsMax, max, name + " maximum");
            Near(item.position, Vector3.zero, name + " pivot");
            if (item.materials == null || item.materials.Length != 1 || item.materials[0] != material)
                throw new InvalidOperationException(name + " material reference mismatch");
        }
        static void Near(Vector3 actual, Vector3 expected, string label)
        {
            if (float.IsNaN(actual.x) || float.IsNaN(actual.y) || float.IsNaN(actual.z) ||
                (actual - expected).sqrMagnitude > Epsilon * Epsilon)
                throw new InvalidOperationException(label + ": expected " + expected + ", measured " + actual);
        }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
