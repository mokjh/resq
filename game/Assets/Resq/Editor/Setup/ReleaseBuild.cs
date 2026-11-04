using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Batch APK build (tools/unity/release_apk.sh). Signs with RESQ_KEYSTORE / RESQ_KEYSTORE_PASS when set, otherwise
    /// debug-signed for sideloading. Version code is yyMMddHH (UTC), so every upload is higher than the last. The
    /// password lives only in the environment of this process and is cleared from PlayerSettings after the build.
    /// </summary>
    public static class ReleaseBuild
    {
        public static void Build()
        {
            int code = 1;
            try { code = Run(); }
            catch (Exception e) { Debug.LogError("[RESQ build] " + e); }
            EditorApplication.Exit(code);
        }

        static int Run()
        {
            string output = Environment.GetEnvironmentVariable("RESQ_APK_OUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("RESQ_APK_OUT is not set");
            string keystore = Environment.GetEnvironmentVariable("RESQ_KEYSTORE");
            bool release = !string.IsNullOrEmpty(keystore);

            PlayerSettings.Android.bundleVersionCode = int.Parse(DateTime.UtcNow.ToString("yyMMddHH"));
            PlayerSettings.Android.useCustomKeystore = release;
            if (release)
            {
                string pass = Environment.GetEnvironmentVariable("RESQ_KEYSTORE_PASS") ?? "";
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = pass;
                PlayerSettings.Android.keyaliasName = "resq";
                PlayerSettings.Android.keyaliasPass = pass;
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Debug.Log($"[RESQ build] {PlayerSettings.applicationIdentifier} {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}) {(release ? "release" : "debug")}-signed, scenes: {string.Join(", ", scenes)}");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = output, target = BuildTarget.Android, options = BuildOptions.None });

            PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = "";
            PlayerSettings.Android.useCustomKeystore = false; PlayerSettings.Android.keystoreName = ""; PlayerSettings.Android.keyaliasName = "";
            Debug.Log($"[RESQ build] result {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalMinutes:F1} min");
            return report.summary.result == BuildResult.Succeeded ? 0 : 1;
        }
    }
}
