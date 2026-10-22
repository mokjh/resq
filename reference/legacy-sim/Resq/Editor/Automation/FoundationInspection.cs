using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Resq.Editor.Automation
{
    // Native observations only. This does not install packages, create a candidate
    // lock, configure an XR rig, emit acceptance cases or claim an Android build.
    public static class FoundationInspection
    {
        [Serializable] private sealed class FileIdentity
        {
            public string path, sha256;
            public long byte_length;
        }
        [Serializable] private sealed class PackageIdentity
        {
            public string name, version, source, resolved_path;
            public FileIdentity manifest;
            public List<FileIdentity> files = new List<FileIdentity>();
        }
        [Serializable] private sealed class Report
        {
            public string schema = "resq-native-foundation-inspection-1";
            public string scope = "observed local inventory; not F04 acceptance or physical qualification";
            public string unity_version, observed_utc, project_path, active_build_target;
            public bool android_module_supported, accepted_task = false;
            public FileIdentity project_version, project_manifest, packages_lock;
            public List<PackageIdentity> packages = new List<PackageIdentity>();
        }

        public static void Inspect()
        {
            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("Dedicated owned batch Editor required.");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string root = Path.GetFullPath(Path.Combine(project, ".."));
                if (Path.GetFileName(project) != "game" ||
                    !File.Exists(Path.Combine(root, "specification/00_FREEZE_INDEX.md")) ||
                    Path.GetFullPath(Argument("-projectPath")) != project)
                    throw new InvalidOperationException("Exact RESQ/game project required.");
                string output = Path.GetFullPath(Argument("-resqFoundationInspection"));
                string allowed = Path.Combine(root, "evidence/foundation/project-inspection") + Path.DirectorySeparatorChar;
                if (!output.StartsWith(allowed, StringComparison.Ordinal) ||
                    File.Exists(output) || Directory.Exists(output))
                    throw new IOException("Fresh owned foundation-inspection output required.");
                RejectLinksToRoot(output);
                var report = new Report
                {
                    unity_version = Application.unityVersion,
                    observed_utc = DateTime.UtcNow.ToString("o"), project_path = project,
                    active_build_target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    android_module_supported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                    project_version = Identify(Path.Combine(project, "ProjectSettings/ProjectVersion.txt")),
                    project_manifest = Identify(Path.Combine(project, "Packages/manifest.json")),
                    packages_lock = Identify(Path.Combine(project, "Packages/packages-lock.json"))
                };
                var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                if (packages == null || packages.Length == 0 ||
                    packages.Select(package => package.name).Distinct(StringComparer.Ordinal).Count() != packages.Length)
                    throw new InvalidOperationException("Nonempty unique registered package inventory required.");
                foreach (var package in packages.OrderBy(package => package.name, StringComparer.Ordinal))
                {
                    string path = Path.GetFullPath(package.resolvedPath);
                    RejectLinksToRoot(path);
                    var entry = new PackageIdentity { name = package.name, version = package.version,
                        source = package.source.ToString(), resolved_path = path,
                        manifest = Identify(Path.Combine(path, "package.json")) };
                    // Hash actual registered payloads, not registry metadata or guessed pins.
                    string[] files = PackageFiles(path);
                    foreach (string file in files)
                    {
                        var identity = Identify(file);
                        identity.path = file.Substring(path.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                        entry.files.Add(identity);
                    }
                    if (!files.SequenceEqual(PackageFiles(path), StringComparer.Ordinal))
                        throw new IOException("Package inventory changed during inspection: " + package.name);
                    if (entry.manifest.sha256 != Identify(Path.Combine(path, "package.json")).sha256)
                        throw new IOException("Package manifest changed during inspection: " + package.name);
                    report.packages.Add(entry);
                }
                foreach (var identity in new[] { report.project_version, report.project_manifest, report.packages_lock })
                    if (Identify(identity.path).sha256 != identity.sha256)
                        throw new IOException("Project resolution changed during inspection.");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true) + "\n");
                using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                { stream.Write(data, 0, data.Length); stream.Flush(true); }
                Debug.Log("RESQ native package/module inventory recorded; candidate and physical qualification remain pending.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, flag);
            if (index < 0 || Array.LastIndexOf(args, flag) != index || index + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-"))
                throw new ArgumentException("Missing/invalid/duplicate " + flag);
            return args[index + 1];
        }
        private static void RejectLinksToRoot(string path)
        {
            for (string item = path; !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
                if ((File.Exists(item) || Directory.Exists(item)) &&
                    (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Symlink/reparse input or destination refused: " + item);
        }
        private static string[] PackageFiles(string root)
        {
            // Check each directory before descent; never traverse a vendor symlink.
            var files = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop(); RejectLinksToRoot(directory);
                foreach (string child in Directory.GetDirectories(directory))
                { RejectLinksToRoot(child); pending.Push(child); }
                foreach (string file in Directory.GetFiles(directory))
                { RejectLinksToRoot(file); files.Add(file); }
            }
            return files.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }
        private static FileIdentity Identify(string path)
        {
            RejectLinksToRoot(path);
            var before = new FileInfo(path); long length = before.Length; DateTime modified = before.LastWriteTimeUtc;
            string digest;
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                digest = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            var after = new FileInfo(path);
            if (length != after.Length || modified != after.LastWriteTimeUtc)
                throw new IOException("Input changed during hashing: " + path);
            return new FileIdentity { path = path, sha256 = digest, byte_length = length };
        }
    }
}
