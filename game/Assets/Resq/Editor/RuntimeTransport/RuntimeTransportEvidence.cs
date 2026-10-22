using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Resq.Content;

namespace Resq.Editor.RuntimeTransport
{
    public static class RuntimeTransportEvidence
    {
        // Called only after actual native XML is saved by the general runner.
        public static void WriteEvidence(string xmlPath, string packetPath)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
            var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlElement test in document.GetElementsByTagName("test-case"))
            {
                if (test.GetAttribute("result") != "Passed") throw new InvalidOperationException("Nonpassing native runtime transport case.");
                var properties = test.SelectNodes("properties/property[@name='case_id']");
                if (properties.Count != 1) throw new InvalidOperationException("Missing/duplicate runtime transport case identity.");
                rows.Add(((XmlElement)properties[0]).GetAttribute("value"), test.GetAttribute("fullname"));
            }
            var expected = Enumerable.Range(1, 4).Select(index => "check-c04/assertion/assertion_id=check-c04-a0" + index);
            if (!rows.Keys.SequenceEqual(expected)) throw new InvalidOperationException("Exact C04 native case set required.");
            string hash; using (var stream = File.OpenRead(xmlPath)) using (var digest = SHA256.Create()) hash = BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            var cases = rows.Select(row => JsonValue.Object(new Dictionary<string, JsonValue> {
                {"case_id",JsonValue.String(row.Key)},{"kind",JsonValue.String("assertion")},{"dimensions",JsonValue.Object(new Dictionary<string,JsonValue>{{"assertion_id",JsonValue.String(row.Key.Split('=')[1])}})},
                {"status",JsonValue.String("passed")},{"metrics",JsonValue.Object(new Dictionary<string,JsonValue>())},{"participant_alias",JsonValue.Null()},{"proof_index",JsonValue.Null()},
                {"observations",JsonValue.Object(new Dictionary<string,JsonValue>{{"native_case_id",JsonValue.String(row.Value)},{"native_xml_sha256",JsonValue.String(hash)}})} }));
            var packet = JsonValue.Object(new Dictionary<string, JsonValue> { {"status",JsonValue.String("passed")},{"case_outcomes",JsonValue.Array(cases)} });
            using (var stream = new FileStream(packetPath, FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true))) writer.WriteLine(packet.Canonical());
        }
    }
}
