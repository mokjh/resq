// Native Unity renderer source. No image exists until this runs in the licensed Editor.
// APIs verified against Unity6.3 docs: Camera.scene and RenderPipeline.StandardRequest.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Resq.Art.Calibration
{
    public static class CalibrationContactSheet
    {
        private const string AssetPath = "Assets/Resq/Art/Calibration/unit.fbx";
        private const int Size = 512;

        [Serializable] private sealed class Frame
        {
            public string name;
            public Vector3 cameraPosition;
            public float instanceScale, orthographicSize;
            public int nonBackgroundPixels, magentaPixels;
        }
        [Serializable] private sealed class Report
        {
            public string schema = "resq-calibration-unity-render-1";
            public string evidence_kind = "automated";
            public string unityVersion, utc, graphicsDevice, pipeline, colorSpace;
            public string fbxSha256, importMetaSha256, measurementsSha256, pngSha256, assetDependencyHash;
            public string[] shaders;
            public int width = Size * 4, height = Size;
            public bool pixelInspectionPerformed = false, F06Accepted = false, materialOverrides = false;
            public List<Frame> frames = new List<Frame>();
        }

        public static void Render()
        {
            Scene preview = default(Scene);
            Texture2D sheet = null;
            RenderTexture target = null, previousTarget = RenderTexture.active;
            try
            {
                if (!Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Dedicated batch Editor with actual graphics device required; omit -nographics.");
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                if (!File.Exists(Path.Combine(root, "specification/00_FREEZE_INDEX.md")) ||
                    Path.GetFileName(Path.GetDirectoryName(Application.dataPath)) != "game")
                    throw new InvalidOperationException("Not the RESQ/game project.");
                string image = NewOutput(root, "-resqCalibrationImage"), reportPath = NewOutput(root, "-resqCalibrationRenderReport");
                if (image == reportPath) throw new ArgumentException("Distinct image/report destinations required.");
                string measurements = Path.GetFullPath(Argument("-resqCalibrationMeasurements"));
                if (!InsideEvidence(root, measurements) || !File.Exists(measurements))
                    throw new InvalidOperationException("Owned native Unity measurement input required.");
                var measured = JsonUtility.FromJson<CalibrationMeasurement.Report>(File.ReadAllText(measurements));
                string fbx = Path.Combine(Application.dataPath, "Resq/Art/Calibration/unit.fbx");
                string fbxHash = Hash(fbx);
                if (measured.schema != "resq-calibration-unity-1" || measured.unityVersion != Application.unityVersion ||
                    measured.fbxSha256 != fbxHash || Hash(Path.Combine(root, "art/calibration/unit.fbx")) != fbxHash ||
                    measured.measurements == null || measured.measurements.Count(item => item.materials != null) != 4)
                    throw new InvalidOperationException("Measurements do not bind this exact import/Editor.");
                if (!image.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("PNG contact sheet required.");
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
                var importer = AssetImporter.GetAtPath(AssetPath) as ModelImporter;
                if (model == null || importer == null)
                    throw new InvalidOperationException("Actual imported FBX required.");
                if (Math.Abs(importer.globalScale-measured.globalScale) > .000001f ||
                    Math.Abs(importer.fileScale-measured.fileScale) > .000001f ||
                    importer.useFileScale != measured.useFileScale || importer.bakeAxisConversion != measured.bakeAxisConversion ||
                    importer.materialImportMode.ToString() != measured.materialImportMode)
                    throw new InvalidOperationException("Import settings changed after native measurements.");
                preview = EditorSceneManager.NewPreviewScene();
                var instance = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(instance, preview);
                if (instance.transform.position.sqrMagnitude > .0000000001f ||
                    Quaternion.Angle(instance.transform.rotation, Quaternion.identity) > .01f ||
                    (instance.transform.localScale-Vector3.one).sqrMagnitude > .0000000001f)
                    throw new InvalidOperationException("Imported root must already have identity transform; no correction is applied.");
                if (instance.GetComponentsInChildren<MeshFilter>().Length != 4)
                    throw new InvalidOperationException("Unexpected model mesh set.");
                var renderers = instance.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length != 4 || renderers.Any(renderer => renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterials.Any(material => material == null || material.shader == null ||
                    !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")))
                    throw new InvalidOperationException("Missing/unsupported actual imported material; no override is applied.");
                var cameraObject = new GameObject("RESQ_CalibrationCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = preview; // Restricts rendering to the disposable preview scene.
                camera.orthographic = true;
                camera.orthographicSize = .15f;
                camera.aspect = 1;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.93f, .91f, .87f, 1);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                var lightObject = new GameObject("RESQ_CalibrationLight");
                SceneManager.MoveGameObjectToScene(lightObject, preview);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1;
                light.shadows = LightShadows.None;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                if (!target.Create()) throw new InvalidOperationException("Native render target allocation failed.");
                camera.targetTexture = target;
                sheet = new Texture2D(Size * 4, Size, TextureFormat.RGBA32, false);
                var report = new Report { unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("o"),
                    graphicsDevice = SystemInfo.graphicsDeviceName, colorSpace = QualitySettings.activeColorSpace.ToString(),
                    pipeline = GraphicsSettings.currentRenderPipeline == null ? "BuiltIn" : GraphicsSettings.currentRenderPipeline.GetType().FullName,
                    fbxSha256 = fbxHash, importMetaSha256 = Hash(fbx + ".meta"), measurementsSha256 = Hash(measurements),
                    assetDependencyHash = AssetDatabase.GetAssetDependencyHash(AssetPath).ToString(),
                    shaders = renderers.SelectMany(renderer => renderer.sharedMaterials).Select(material => material.shader.name).Distinct().OrderBy(name => name).ToArray() };
                var positions = new[] { new Vector3(.25f,.20f,.30f), new Vector3(-.25f,.20f,-.30f), new Vector3(0,.40f,0), new Vector3(.25f,.20f,.30f) };
                var names = new[] { "wide-three-quarter", "wide-reverse", "wide-top", "compact-three-quarter" };
                for (int index = 0; index < 4; index++)
                {
                    float scale = index == 3 ? 5f / 6f : 1;
                    instance.transform.localScale = Vector3.one * scale;
                    camera.transform.position = positions[index];
                    camera.transform.rotation = Quaternion.LookRotation(-positions[index], index == 2 ? Vector3.forward : Vector3.up);
                    if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
                    else
                    {
                        var request = new RenderPipeline.StandardRequest { destination = target };
                        if (!RenderPipeline.SupportsRenderRequest(camera, request))
                            throw new InvalidOperationException("Active pipeline cannot render this native base-camera request.");
                        RenderPipeline.SubmitRenderRequest(camera, request);
                    }
                    RenderTexture.active = target;
                    var frame = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                    try
                    {
                        frame.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                        frame.Apply(false, false);
                        Color32[] pixels = frame.GetPixels32();
                        Color32 background = pixels[0];
                        int varied = pixels.Count(pixel => Math.Abs(pixel.r-background.r) + Math.Abs(pixel.g-background.g) + Math.Abs(pixel.b-background.b) > 12);
                        int magenta = pixels.Count(pixel => pixel.r > 240 && pixel.b > 240 && pixel.g < 25);
                        if (varied < 128 || magenta > 0) throw new InvalidOperationException("Blank or error-shader native frame.");
                        sheet.SetPixels32(index * Size, 0, Size, Size, pixels);
                        report.frames.Add(new Frame { name = names[index], cameraPosition = positions[index], instanceScale = scale,
                            orthographicSize = camera.orthographicSize, nonBackgroundPixels = varied, magentaPixels = magenta });
                    }
                    finally { UnityEngine.Object.DestroyImmediate(frame); }
                }
                sheet.Apply(false, false);
                Write(image, sheet.EncodeToPNG());
                report.pngSha256 = Hash(image);
                Write(reportPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true) + "\n"));
                Debug.Log("Actual Unity calibration pixels written. Pixel inspection and F06 acceptance remain pending.");
                EditorSceneManager.ClosePreviewScene(preview); preview = default(Scene);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            finally
            {
                RenderTexture.active = previousTarget;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (sheet != null) UnityEngine.Object.DestroyImmediate(sheet);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, flag);
            if (index < 0 || Array.LastIndexOf(args, flag) != index || index + 1 >= args.Length || args[index+1].StartsWith("-"))
                throw new ArgumentException("Missing/duplicate/invalid " + flag);
            return args[index+1];
        }
        private static bool InsideEvidence(string root, string path) => path.StartsWith(Path.Combine(root, "evidence/calibration") + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        private static string NewOutput(string root, string flag)
        {
            string path = Path.GetFullPath(Argument(flag));
            if (!InsideEvidence(root, path) || File.Exists(path) || Directory.Exists(path)) throw new IOException("New owned evidence output required.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); return path;
        }
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void Write(string path, byte[] data)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { stream.Write(data, 0, data.Length); stream.Flush(true); }
        }
    }
}
