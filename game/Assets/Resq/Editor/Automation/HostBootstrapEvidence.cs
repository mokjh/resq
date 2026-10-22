using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using UnityEngine;

namespace Resq.Editor.Automation
{
    // Serialize only IDs actually emitted by successful NUnit leaves. No synthetic cases.
    public static class HostBootstrapEvidence
    {
        public static void Write(string xmlPath, string packetPath)
        {
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(xmlPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                document.Load(reader);
            var rows = new SortedDictionary<string, string>();
            foreach (XmlElement test in document.GetElementsByTagName("test-case"))
            {
                if (test.GetAttribute("result") != "Passed") throw new InvalidOperationException("Nonpassing native case.");
                var properties = test.SelectNodes("properties/property[@name='case_id']");
                if (properties.Count != 1) throw new InvalidOperationException("Missing/duplicate native case identity.");
                string id = ((XmlElement)properties[0]).GetAttribute("value");
                rows.Add(id, test.GetAttribute("fullname"));
            }
            string[] expected = Enumerable.Range(1, 4).Select(index => "check-f03/assertion/assertion_id=check-f03-a0" + index).ToArray();
            if (!rows.Keys.SequenceEqual(expected)) throw new InvalidOperationException("Exact F03 native case set required.");
            string hash;
            using (var stream = File.OpenRead(xmlPath)) using (var digest = SHA256.Create())
                hash = BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            var cases = rows.Select(row => "{\"case_id\":" + Quote(row.Key) +
                ",\"kind\":\"assertion\",\"dimensions\":{\"assertion_id\":" + Quote(row.Key.Split('=')[1]) +
                "},\"status\":\"passed\",\"metrics\":{},\"participant_alias\":null,\"proof_index\":null,\"observations\":{\"native_case_id\":" +
                Quote(row.Value) + ",\"native_xml_sha256\":" + Quote(hash) + ",\"unity_version\":" + Quote(Application.unityVersion) + "}}");
            using (var stream = new FileStream(packetPath, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                writer.WriteLine("{\"status\":\"passed\",\"case_outcomes\":[" + string.Join(",", cases) + "]}");
        }

        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (char character in value)
            {
                if (character == '\\' || character == '"') text.Append('\\').Append(character);
                else if (character < 32) text.Append("\\u").Append(((int)character).ToString("x4"));
                else text.Append(character);
            }
            return text.Append('"').ToString();
        }
    }
}
