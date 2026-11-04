using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;

namespace Resq.EditorTools
{
    /// <summary>
    /// The Meta XR SDK's AARs carry 32-bit telemetry libraries under assets/lib/armeabi-v7a. RESQ is arm64 only and Meta's
    /// upload validation warns about any 32-bit library, so the generated Gradle project skips that asset folder when it
    /// packs the APK. tools/unity/check_apk.sh fails if one comes back.
    /// </summary>
    public sealed class SkipArmv7Assets : IPostGenerateGradleAndroidProject
    {
        const string Skip = "!armeabi-v7a";
        const string Defaults = "!.svn:!.git:!.ds_store:!*.scc:!CVS:!thumbs.db:!picasa.ini:!*~";
        static readonly Regex Pattern = new Regex("ignoreAssetsPattern\\s*=?\\s*['\"]([^'\"]*)['\"]");

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            foreach (var module in new[] { Path.Combine(Path.GetDirectoryName(unityLibraryPath), "launcher"), unityLibraryPath })
            {
                string gradle = Path.Combine(module, "build.gradle");
                if (!File.Exists(gradle)) continue;
                string text = File.ReadAllText(gradle);
                if (text.Contains(Skip)) continue;
                text = Pattern.IsMatch(text)
                    ? Pattern.Replace(text, m => m.Value.Replace(m.Groups[1].Value, m.Groups[1].Value + ":" + Skip), 1)
                    : text + $"\nandroid {{ androidResources {{ ignoreAssetsPattern = \"{Defaults}:{Skip}\" }} }}\n";
                File.WriteAllText(gradle, text);
            }
        }
    }
}
