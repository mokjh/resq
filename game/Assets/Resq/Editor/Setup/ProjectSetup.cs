using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Resq.EditorTools
{
    /// <summary>
    /// Reproducible Quest project configuration. Run from the menu or the CLI:
    /// unity command eval --code "Resq.EditorTools.ProjectSetup.Apply(); return \"ok\";"
    /// </summary>
    public static class ProjectSetup
    {
        public const string ApplicationId = "com.playresq.resq";
        const string SettingsDir = "Assets/Resq/Settings";

        [MenuItem("RESQ/Setup/Apply Quest Project Settings")]
        public static void Apply()
        {
            ApplyPlayerSettings();
            ApplyRenderPipeline();
            ApplyQuality();
            AssetDatabase.SaveAssets();
            Debug.Log("[RESQ] Project settings applied");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "RESQ";
            PlayerSettings.productName = "RESQ";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, ApplicationId);
            if (PlayerSettings.Android.bundleVersionCode < 1) PlayerSettings.Android.bundleVersionCode = 1;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.Android.forceSDCardPermission = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.MTRendering = true;
            PlayerSettings.gpuSkinning = true;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
        }

        static void ApplyRenderPipeline()
        {
            Directory.CreateDirectory(SettingsDir);
            string rendererPath = SettingsDir + "/RESQ_Quest_Renderer.asset";
            string assetPath = SettingsDir + "/RESQ_Quest_URP.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var rso = new SerializedObject(renderer);
            SetInt(rso, "m_RenderingMode", 0);              // Forward
            SetInt(rso, "m_DepthPrimingMode", 0);           // Disabled
            SetBool(rso, "m_ShadowTransparentReceive", false);
            rso.ApplyModifiedPropertiesWithoutUndo();

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            var so = new SerializedObject(asset);
            SetInt(so, "m_MSAA", 4);
            SetBool(so, "m_SupportsHDR", false);
            SetBool(so, "m_RequireDepthTexture", false);
            SetBool(so, "m_RequireOpaqueTexture", false);
            SetFloat(so, "m_RenderScale", 1f);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 1024);
            SetInt(so, "m_AdditionalLightsRenderingMode", 0);  // disabled
            SetFloat(so, "m_ShadowDistance", 1.5f);
            SetInt(so, "m_ShadowCascadeCount", 1);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetBool(so, "m_UseSRPBatcher", true);
            SetBool(so, "m_SupportsDynamicBatching", false);
            SetInt(so, "m_GPUResidentDrawerMode", 0);
            SetBool(so, "m_UseAdaptivePerformance", false);
            so.ApplyModifiedPropertiesWithoutUndo();

            GraphicsSettings.defaultRenderPipeline = asset;
            EditorUtility.SetDirty(asset);
        }

        static void ApplyQuality()
        {
            var asset = GraphicsSettings.defaultRenderPipeline;
            string[] names = QualitySettings.names;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
                QualitySettings.antiAliasing = 4;
                QualitySettings.vSyncCount = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.billboardsFaceCameraPosition = true;
            }
            QualitySettings.SetQualityLevel(current, true);
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value; else Debug.LogWarning($"[RESQ] property {name} not found");
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value; else Debug.LogWarning($"[RESQ] property {name} not found");
        }

        static void SetFloat(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = value; else Debug.LogWarning($"[RESQ] property {name} not found");
        }
    }
}
