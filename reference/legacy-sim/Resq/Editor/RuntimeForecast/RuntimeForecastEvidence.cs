using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Resq.Content;

namespace Resq.Editor.RuntimeForecast
{
    public static class RuntimeForecastEvidence
    {
        public static void WriteForecastEvidence(string xmlPath, string packetPath) => Write(xmlPath, packetPath, "check-c08", "RuntimeForecastTests", new[] {
            "SourceIsolationRevisionCancellationAndLifecycle", "ActualReplayEqualityAndRelaxedScenarioHorizon",
            "RealCostStockCapacityPowerAndRiskExplanations", "HiddenEventNoninterferenceAndFrozenKnowledge" });
        public static void WriteOrderingEvidence(string xmlPath, string packetPath) => Write(xmlPath, packetPath, "check-runtime-ordering", "RuntimeOrderingTests", new[] { "AllNormativeOrderingFixturesAndBoundaries" });
        private static void Write(string xmlPath, string packetPath, string check, string type, string[] methods)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
            var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlElement test in document.GetElementsByTagName("test-case"))
            {
                if (test.GetAttribute("result") != "Passed") throw new InvalidOperationException("Nonpassing native forecast/ordering case");
                var properties = test.SelectNodes("properties/property[@name='case_id']");
                if (properties.Count != 1) throw new InvalidOperationException("Exactly one actual case identity required");
                rows.Add(((XmlElement)properties[0]).GetAttribute("value"), test.GetAttribute("fullname"));
            }
            var expected = Enumerable.Range(1, methods.Length).Select(i => check + "/assertion/assertion_id=" + check + "-a0" + i).ToArray();
            if (!rows.Keys.SequenceEqual(expected)) throw new InvalidOperationException("Exact required native case set must execute");
            for (int i = 0; i < methods.Length; i++)
                if (rows[expected[i]] != "Resq.Tests.Forecast." + type + "." + methods[i]) throw new InvalidOperationException("Native method identity mismatch");
            string hash;
            using (var stream = File.OpenRead(xmlPath)) using (var digest = SHA256.Create()) hash = BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            var witness = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            if (check == "check-c08")
            {
                string directory = Path.Combine(Path.GetDirectoryName(xmlPath), "forecast-replay-recordings");
                foreach (string profile in new[] { "standard", "relaxed" })
                {
                    var replay = StrictJson.Parse(File.ReadAllBytes(Path.Combine(directory, profile + ".replay.json")));
                    var report = StrictJson.Parse(File.ReadAllBytes(Path.Combine(directory, profile + ".result.json")));
                    if (report["status"].Text != "passed" || report["replay_sha256"].Text != replay.Sha256() ||
                        report["actual_final_state_sha256"].Text != report["forecast_final_state_sha256"].Text ||
                        report["actual_final_state_sha256"].Text != replay["expected"]["final_state_sha256"].Text)
                        throw new InvalidOperationException("Actual recorded replay/forecast witness mismatch");
                    witness.Add(profile + "_replay_sha256", JsonValue.String(replay.Sha256()));
                    witness.Add(profile + "_result_sha256", JsonValue.String(report.Sha256()));
                }
            }
            var cases = rows.Select(row => {
                var observations = new Dictionary<string, JsonValue>(witness, StringComparer.Ordinal) {
                    { "native_case_id", JsonValue.String(row.Value) }, { "native_xml_sha256", JsonValue.String(hash) } };
                return JsonValue.Object(new Dictionary<string, JsonValue> {
                    {"case_id",JsonValue.String(row.Key)}, {"kind",JsonValue.String("assertion")},
                    {"dimensions",JsonValue.Object(new Dictionary<string,JsonValue>{{"assertion_id",JsonValue.String(row.Key.Split('=')[1])}})},
                    {"status",JsonValue.String("passed")}, {"metrics",JsonValue.Object(new Dictionary<string,JsonValue>())},
                    {"participant_alias",JsonValue.Null()}, {"proof_index",JsonValue.Null()}, {"observations",JsonValue.Object(observations)} });
            });
            var packet = JsonValue.Object(new Dictionary<string, JsonValue> { { "status", JsonValue.String("passed") }, { "case_outcomes", JsonValue.Array(cases) } });
            using (var stream = new FileStream(packetPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true))) writer.WriteLine(packet.Canonical());
        }
    }
}
