using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Store packaging (BUILD_PLAN P4.6): the RESQ app icon and the Horizon OS system splash, both drawn by
    /// tools/brand/icon.py. The splash shows over contextual passthrough while the app loads, so the room stays
    /// visible from the first frame of a mixed-reality game.
    /// </summary>
    public static class BrandSetup
    {
        const string Icon = "Assets/Resq/Content/Brand/app_icon.png";
        const string Splash = "Assets/Resq/Content/Brand/vr_splash.png";

        [MenuItem("RESQ/Build/Apply Brand (icon + splash)")]
        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Icon);
            var splash = AssetDatabase.LoadAssetAtPath<Texture2D>(Splash);
            if (icon == null || splash == null) { Debug.LogError("[RESQ] brand images missing: run tools/brand/icon.py"); return; }
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Unknown, IconKind.Any);
            var icons = new Texture2D[Mathf.Max(1, sizes.Length)];
            for (int i = 0; i < icons.Length; i++) icons[i] = icon;
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, icons, IconKind.Any);
            var cfg = OVRProjectConfig.CachedProjectConfig;
            cfg.systemSplashScreen = splash;
            cfg.systemSplashScreenType = OVRProjectConfig.SystemSplashScreenType.Mono;
            cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            OVRProjectConfig.CommitProjectConfig(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log("[RESQ] brand applied: default icon and system splash (contextual passthrough)");
        }
    }
}
