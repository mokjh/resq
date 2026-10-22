using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Resq.Core;
using Resq.Tests.Shared;
using UnityEditor;
using UnityEngine;

namespace Resq.Tests.EditMode
{
    [TestFixture, Category("host_bootstrap")]
    public sealed class HostBootstrapTests
    {
        private static string Root => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;

        [Test, NUnit.Framework.Property("case_id", "check-f03/assertion/assertion_id=check-f03-a01")]
        public void ActualBatchEditorCompiledCore()
        {
            Assert.That(Application.isEditor && Application.isBatchMode, Is.True);
            Assert.That(EditorApplication.isCompiling, Is.False);
            string selected = File.ReadLines(Path.Combine(Root, "game/ProjectSettings/ProjectVersion.txt"))
                .Single(line => line.StartsWith("m_EditorVersion:")).Split(':')[1].Trim();
            Assert.That(Application.unityVersion, Is.EqualTo(selected));
            Assert.That(File.Exists(typeof(Tick).Assembly.Location), Is.True);
        }

        [Test, NUnit.Framework.Property("case_id", "check-f03/assertion/assertion_id=check-f03-a02")]
        public void IsolatedPureStateChecksExecute()
        {
            foreach (string name in CoreSmokeChecks.Names) CoreSmokeChecks.Run(name);
            Assert.That(CoreSmokeChecks.Names.Length, Is.EqualTo(7));
        }

        [Test, NUnit.Framework.Property("case_id", "check-f03/assertion/assertion_id=check-f03-a03")]
        public void CoreAndAnySimulationAssemblyHaveNoEngineReferences()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly =>
                assembly.GetName().Name == "Resq.Core" || assembly.GetName().Name.StartsWith("Resq.Simulation")).ToArray();
            Assert.That(assemblies.Any(assembly => assembly.GetName().Name == "Resq.Core"), Is.True);
            foreach (var assembly in assemblies)
                Assert.That(assembly.GetReferencedAssemblies().Any(reference =>
                    reference.Name.StartsWith("UnityEngine") || reference.Name.StartsWith("UnityEditor")), Is.False);
            var definition = JsonUtility.FromJson<AssemblyDefinition>(File.ReadAllText(Path.Combine(Root,
                "game/Assets/Resq/Runtime/Core/Resq.Core.asmdef")));
            Assert.That(definition.noEngineReferences, Is.True);
        }

        [Test, NUnit.Framework.Property("case_id", "check-f03/assertion/assertion_id=check-f03-a04")]
        public void OriginalBaselineAndOwnedProjectArePreserved()
        {
            var baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(Path.Combine(Root, "specification/baseline_manifest.json")));
            Assert.That(baseline.files.Length, Is.EqualTo(41));
            foreach (var row in baseline.files)
                Assert.That(Hash(Path.Combine(Root, "Resq_Production_Pack", row.path)), Is.EqualTo(row.sha256), row.path);
            string[] args = Environment.GetCommandLineArgs();
            int projectIndex = Array.IndexOf(args, "-projectPath"), ownerIndex = Array.IndexOf(args, "-resqOwnerPid");
            Assert.That(projectIndex >= 0 && ownerIndex >= 0, Is.True);
            Assert.That(Path.GetFullPath(args[projectIndex + 1]), Is.EqualTo(Path.Combine(Root, "game")));
            int expectedOwner = int.Parse(args[ownerIndex + 1]);
            string lockName;
            using (var digest = SHA256.Create())
                lockName = BitConverter.ToString(digest.ComputeHash(System.Text.Encoding.UTF8.GetBytes("unity-project"))).Replace("-", "").ToLowerInvariant();
            var record = JsonUtility.FromJson<OwnerLock>(File.ReadAllText(Path.Combine(Root, "automation/locks", lockName + ".lock.json")));
            Assert.That(record.resource, Is.EqualTo("unity-project"));
            Assert.That(record.cwd, Is.EqualTo(Root));
            Assert.That(record.pid, Is.EqualTo(expectedOwner));
            Assert.That(Process.GetProcessById(expectedOwner).HasExited, Is.False);
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class AssemblyDefinition { public bool noEngineReferences; }
        [Serializable] private sealed class Baseline { public BaselineFile[] files; }
        [Serializable] private sealed class BaselineFile { public string path; public string sha256; }
        [Serializable] private sealed class OwnerLock { public string resource; public string cwd; public int pid; }
    }
}
