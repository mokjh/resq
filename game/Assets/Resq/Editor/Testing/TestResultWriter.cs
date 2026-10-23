using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Writes every finished test run to tools/.cache/test_results.json, so tools/unity/smoke.sh can wait on a file
    /// instead of polling the Editor bridge mid-run. A bridge request that times out while a test runs makes the
    /// bridge log an error, and the Test Framework then fails that test for a reason unrelated to the game.
    /// </summary>
    [InitializeOnLoad]
    static class TestResultWriter
    {
        static TestResultWriter()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        public static string OutPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "tools", ".cache", "test_results.json"));

        sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
                File.WriteAllText(OutPath, "{\"status\": \"running\"}");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.Append("{\"status\": \"completed\", \"passed\": ").Append(result.PassCount)
                  .Append(", \"failed\": ").Append(result.FailCount).Append(", \"skipped\": ").Append(result.SkipCount)
                  .Append(", \"duration\": ").Append(result.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Append(", \"results\": [");
                bool first = true;
                Collect(result, sb, ref first);
                sb.Append("]}");
                File.WriteAllText(OutPath, sb.ToString());
            }

            static void Collect(ITestResultAdaptor r, StringBuilder sb, ref bool first)
            {
                if (!r.HasChildren)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append("{\"name\": ").Append(Q(r.FullName)).Append(", \"status\": ").Append(Q(r.TestStatus.ToString()))
                      .Append(", \"duration\": ").Append(r.Duration.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(", \"message\": ").Append(Q(r.Message ?? "")).Append('}');
                    return;
                }
                foreach (var c in r.Children) Collect(c, sb, ref first);
            }

            static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
