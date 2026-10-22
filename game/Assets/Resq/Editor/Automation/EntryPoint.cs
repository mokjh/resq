// Source prepared by F02. Actual licensed compilation/package qualification is F03/F04.
// TestRunnerApi: https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/api/UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.html
// BuildReport: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Build.Reporting.BuildSummary.html
// Never pass -quit for asynchronous test entry points; callbacks own the exit code.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Resq.Editor.Automation
{
    [InitializeOnLoad]
    public static class EntryPoint
    {
        private const string ActiveKey = "Resq.Automation.TestsActive";
        private const string ResultsKey = "Resq.Automation.TestResults";
        private static TestRunnerApi api;
        private static Callbacks callbacks;

        static EntryPoint()
        {
            // PlayMode tests may reload the domain. Re-register only this run's
            // callback, with its destination persisted by the original entry.
            if (SessionState.GetBool(ActiveKey, false))
                RegisterCallbacks();
        }

        public static void RunEditModeTests() { RunTests(TestMode.EditMode); }
        public static void RunPlayModeTests() { RunTests(TestMode.PlayMode); }

        private static void RegisterCallbacks()
        {
            if (api != null) return;
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            callbacks = new Callbacks();
            api.RegisterCallbacks(callbacks);
        }

        private static void RunTests(TestMode mode)
        {
            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("RESQ automation requires a dedicated batch Editor.");
                if (SessionState.GetBool(ActiveKey, false))
                    throw new InvalidOperationException("A RESQ test operation is already active.");
                if (Environment.GetCommandLineArgs().Contains("-quit"))
                    throw new InvalidOperationException("Do not use -quit with asynchronous tests.");
                string output = RequireNewOutput("-resqTestResults");
                SessionState.SetString(ResultsKey, output);
                SessionState.SetBool(ActiveKey, true);
                RegisterCallbacks();
                var filter = new Filter { testMode = mode };
                string assembly = Argument("-resqTestAssembly", false);
                string test = Argument("-resqTestFilter", false);
                string category = Argument("-resqTestCategory", false);
                if (assembly != null) filter.assemblyNames = new[] { assembly };
                if (test != null) filter.testNames = new[] { test };
                // NUnit3.5 rejects '-' in CategoryAttribute names. Keep the
                // registered command/suite ID stable and map only its native tag.
                if (category != null) filter.categoryNames = new[] {
                    category == "host-bootstrap" ? "host_bootstrap" :
                    category == "runtime-content" ? "runtime_content" :
                    category == "runtime-commands" ? "runtime_commands" :
                    category == "runtime-water" ? "runtime_water" :
                    category == "runtime-transport" ? "runtime_transport" :
                    category == "runtime-equipment" ? "runtime_equipment" :
                    category == "runtime-objectives" ? "runtime_objectives" :
                    category == "runtime-units" ? "runtime_units" :
                    category == "runtime-persistence" ? "runtime_persistence" :
                    category == "runtime-forecast" ? "runtime_forecast" :
                    category == "runtime-ordering" ? "runtime_ordering" : category };
                api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception error)
            {
                Abort(error);
            }
        }

        private sealed class Callbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("RESQ native tests discovered: " + testsToRun.TestCaseCount);
            }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                    Debug.Log("RESQ native case: " + result.Test.FullName + " => " + result.ResultState);
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(ActiveKey, false)) return;
                string output = SessionState.GetString(ResultsKey, "");
                string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    TestRunnerApi.SaveResultToFile(result, temporary);
                    if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                        throw new IOException("Test framework produced no native XML.");
                    File.Move(temporary, output); // Existing destination fails; no replacement.
                    bool passed = result.PassCount > 0 && result.FailCount == 0 &&
                                  result.SkipCount == 0 && result.InconclusiveCount == 0 &&
                                  result.ResultState == "Passed";
                    if (passed && Argument("-resqTestCategory", false) == "host-bootstrap")
                        HostBootstrapEvidence.Write(output, RequireNewOutput("-resqCaseEvidence"));
                    if (passed && Argument("-resqTestCategory", false) == "runtime-content")
                    {
                        // Keep the general runner independent of the content
                        // test assembly; its exact reviewed emitter reads XML.
                        var emitter = Type.GetType("Resq.Editor.Content.ContentCompiler, Resq.Content.Editor", true);
                        var method = emitter.GetMethod("WriteEvidence", new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("ContentCompiler.WriteEvidence");
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && Argument("-resqTestCategory", false) == "runtime-commands")
                    {
                        var emitter = Type.GetType("Resq.Editor.RuntimeCommands.RuntimeCommandEvidence, Resq.Runtime.Commands.Editor", true);
                        var method = emitter.GetMethod("WriteEvidence", new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimeCommandEvidence.WriteEvidence");
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && Argument("-resqTestCategory", false) == "runtime-water")
                    {
                        var emitter = Type.GetType("Resq.Editor.RuntimeWater.RuntimeWaterEvidence, Resq.Runtime.Water.Editor", true);
                        var method = emitter.GetMethod("WriteEvidence", new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimeWaterEvidence.WriteEvidence");
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && Argument("-resqTestCategory", false) == "runtime-transport")
                    {
                        var emitter = Type.GetType("Resq.Editor.RuntimeTransport.RuntimeTransportEvidence, Resq.Runtime.Transport.Editor", true);
                        var method = emitter.GetMethod("WriteEvidence", new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimeTransportEvidence.WriteEvidence");
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && Argument("-resqTestCategory", false) == "runtime-equipment")
                    {
                        var emitter = Type.GetType("Resq.Editor.RuntimeEquipment.RuntimeEquipmentEvidence, Resq.Runtime.Equipment.Editor", true);
                        var method = emitter.GetMethod("WriteEvidence", new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimeEquipmentEvidence.WriteEvidence");
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && (Argument("-resqTestCategory", false) == "runtime-objectives" ||
                                   Argument("-resqTestCategory", false) == "runtime-units"))
                    {
                        var emitter = Type.GetType("Resq.Editor.RuntimeObjectives.RuntimeObjectiveEvidence, Resq.Runtime.Objectives.Editor", true);
                        string methodName = Argument("-resqTestCategory", false) == "runtime-units" ? "WriteUnitsEvidence" : "WriteEvidence";
                        var method = emitter.GetMethod(methodName, new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimeObjectiveEvidence." + methodName);
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && Argument("-resqTestCategory", false) == "runtime-persistence")
                    {
                        var emitter = Type.GetType("Resq.Editor.Replay.RuntimePersistenceEvidence, Resq.Replay.Editor", true);
                        var method = emitter.GetMethod("WriteEvidence", new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimePersistenceEvidence.WriteEvidence");
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    if (passed && (Argument("-resqTestCategory", false) == "runtime-forecast" ||
                                   Argument("-resqTestCategory", false) == "runtime-ordering"))
                    {
                        var emitter = Type.GetType("Resq.Editor.RuntimeForecast.RuntimeForecastEvidence, Resq.Runtime.Forecast.Editor", true);
                        string methodName = Argument("-resqTestCategory", false) == "runtime-ordering" ? "WriteOrderingEvidence" : "WriteForecastEvidence";
                        var method = emitter.GetMethod(methodName, new[] { typeof(string), typeof(string) });
                        if (method == null) throw new MissingMethodException("RuntimeForecastEvidence." + methodName);
                        method.Invoke(null, new object[] { output, RequireNewOutput("-resqCaseEvidence") });
                    }
                    SessionState.SetBool(ActiveKey, false);
                    EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
                }
                catch (Exception error)
                {
                    // Preserve temporary evidence on failure.
                    Abort(error);
                }
            }
            public void OnError(string message) { Abort(new Exception(message)); }
        }

        public static void BuildAndroid()
        {
            try
            {
                if (!Application.isBatchMode)
                    throw new InvalidOperationException("RESQ automation requires a dedicated batch Editor.");
                string reportPath = RequireNewOutput("-resqBuildReport");
                string apkPath = RequireNewOutput("-resqApk");
                if (!apkPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("An APK output is required.");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                    EditorUserBuildSettings.buildAppBundle ||
                    PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP ||
                    PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                    throw new InvalidOperationException("Qualified Android target, ARM64 IL2CPP and APK settings are required before build.");
                string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                    .Select(scene => scene.path).ToArray();
                if (scenes.Length == 0 || scenes.Any(scene => !File.Exists(scene)))
                    throw new InvalidOperationException("Nonempty existing enabled build scenes required.");
                BuildReport native = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                });
                if (native == null) throw new InvalidOperationException("BuildPipeline returned no BuildReport.");
                BuildSummary summary = native.summary;
                bool apkExists = File.Exists(apkPath);
                var record = new NativeBuildSummary
                {
                    result = summary.result.ToString(),
                    total_errors = summary.totalErrors,
                    total_warnings = summary.totalWarnings,
                    total_size = summary.totalSize,
                    output_path = Path.GetFullPath(summary.outputPath),
                    apk_byte_length = apkExists ? new FileInfo(apkPath).Length : 0,
                    apk_sha256 = apkExists ? HashFile(apkPath) : null,
                    platform = summary.platform.ToString(),
                    unity_version = Application.unityVersion,
                    build_guid = summary.guid.ToString(),
                    started_utc = summary.buildStartedAt.ToUniversalTime().ToString("o"),
                    ended_utc = summary.buildEndedAt.ToUniversalTime().ToString("o")
                };
                WriteJsonExclusive(reportPath, JsonUtility.ToJson(record, true));
                bool passed = summary.result == BuildResult.Succeeded && summary.totalErrors == 0 &&
                              summary.totalSize > 0 && record.apk_byte_length > 0 &&
                              record.output_path == apkPath;
                EditorApplication.Exit(passed ? 0 : 1);
            }
            catch (Exception error) { Abort(error); }
        }

        [Serializable]
        private sealed class NativeBuildSummary
        {
            public string result;
            public int total_errors;
            public int total_warnings;
            public ulong total_size;
            public string output_path;
            public long apk_byte_length;
            public string apk_sha256;
            public string platform;
            public string unity_version;
            public string build_guid;
            public string started_utc;
            public string ended_utc;
        }

        private static string Argument(string flag, bool required)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, flag);
            if (index < 0)
            {
                if (required) throw new ArgumentException("Missing " + flag);
                return null;
            }
            if (Array.LastIndexOf(args, flag) != index || index + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-"))
                throw new ArgumentException("Invalid or duplicate " + flag);
            return args[index + 1];
        }

        private static string RequireNewOutput(string flag)
        {
            string path = Path.GetFullPath(Argument(flag, true));
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (path.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Evidence/build outputs must be outside Assets.");
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException("Output already exists: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteJsonExclusive(string path, string value)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(value);
                writer.Write("\n");
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, path);
        }

        private static void Abort(Exception error)
        {
            SessionState.SetBool(ActiveKey, false);
            Debug.LogException(error);
            EditorApplication.Exit(2);
        }
    }
}
