using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Resq.Content;
namespace Resq.Editor.RuntimeObjectives
{
    public static class RuntimeObjectiveEvidence
    {
        public static void WriteEvidence(string xmlPath, string packetPath) => Write(xmlPath, packetPath, "check-c06", new[] { "AllTwentyFiveTypedDefinitionsAndClosedConditions", "CriticalMasteryStarsAndTerminalSafety", "TemporalBoundariesAndExactRationalScore", "CausalCountersRestoreDebriefAndCampaignRewards" });
        public static void WriteUnitsEvidence(string xmlPath, string packetPath) => Write(xmlPath, packetPath, "check-runtime-units", new[] { "NormativeIntegerGoldenAndOverflowBounds" });
        private static void Write(string xmlPath, string packetPath, string check, string[] methods)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
            var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlElement test in document.GetElementsByTagName("test-case"))
            {
                if (test.GetAttribute("result") != "Passed") throw new InvalidOperationException("Nonpassing native objective/unit case");
                var properties = test.SelectNodes("properties/property[@name='case_id']"); if (properties.Count != 1) throw new InvalidOperationException("Exact native case identity required");
                rows.Add(((XmlElement)properties[0]).GetAttribute("value"), test.GetAttribute("fullname"));
            }
            var expected = Enumerable.Range(1, methods.Length).Select(i => check + "/assertion/assertion_id=" + check + "-a0" + i).ToArray();
            if (!rows.Keys.SequenceEqual(expected)) throw new InvalidOperationException("Exact objective/unit native case set required");
            for (int i = 0; i < methods.Length; i++) if (rows[expected[i]] != "Resq.Tests.RuntimeObjectives.RuntimeObjectiveTests." + methods[i]) throw new InvalidOperationException("Unexpected native test method identity");
            string hash; using (var stream = File.OpenRead(xmlPath)) using (var digest = SHA256.Create()) hash = BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            var cases = rows.Select(row => JsonValue.Object(new Dictionary<string, JsonValue> {
                {"case_id",JsonValue.String(row.Key)},{"kind",JsonValue.String("assertion")},{"dimensions",JsonValue.Object(new Dictionary<string,JsonValue>{{"assertion_id",JsonValue.String(row.Key.Split('=')[1])}})},
                {"status",JsonValue.String("passed")},{"metrics",JsonValue.Object(new Dictionary<string,JsonValue>())},{"participant_alias",JsonValue.Null()},{"proof_index",JsonValue.Null()},
                {"observations",JsonValue.Object(new Dictionary<string,JsonValue>{{"native_case_id",JsonValue.String(row.Value)},{"native_xml_sha256",JsonValue.String(hash)}})} }));
            var packet = JsonValue.Object(new Dictionary<string,JsonValue> { {"status",JsonValue.String("passed")},{"case_outcomes",JsonValue.Array(cases)} });
            using (var stream = new FileStream(packetPath, FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true))) writer.WriteLine(packet.Canonical());
        }
    }
}
