using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Resq.Content;

namespace Resq.Editor.Content
{
    public static class ContentCompiler
    {
        public static CompiledContent Compile(string payloadJson) => ContentCompilation.Compile(payloadJson);
        public static void WriteBundle(string payloadPath,string outputPath)
        {
            var content=Compile(File.ReadAllText(payloadPath,new UTF8Encoding(false,true)));
            byte[] bytes=new UTF8Encoding(false,true).GetBytes(content.CanonicalEnvelope);
            // Validate the exact bytes before exposing a compiler artifact; existing files are preserved.
            ContentCompilation.Load(bytes);
            using(var stream=new FileStream(outputPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
        }
        // EntryPoint invokes this after actual native XML is durable, never from a synthetic case list.
        public static void WriteEvidence(string xmlPath,string packetPath)
        {
            var document=new XmlDocument{XmlResolver=null};
            using(var reader=XmlReader.Create(xmlPath,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) document.Load(reader);
            var rows=new SortedDictionary<string,string>(StringComparer.Ordinal);
            foreach(XmlElement test in document.GetElementsByTagName("test-case"))
            {
                if(test.GetAttribute("result")!="Passed") throw new InvalidOperationException("Nonpassing native content case.");
                var properties=test.SelectNodes("properties/property[@name='case_id']");
                if(properties.Count!=1) throw new InvalidOperationException("Missing/duplicate content case identity.");
                rows.Add(((XmlElement)properties[0]).GetAttribute("value"),test.GetAttribute("fullname"));
            }
            var expected=Enumerable.Range(1,3).Select(index=>"check-c01/assertion/assertion_id=check-c01-a0"+index);
            if(!rows.Keys.SequenceEqual(expected)) throw new InvalidOperationException("Exact C01 native case set required.");
            string hash; using(var stream=File.OpenRead(xmlPath)) using(var digest=SHA256.Create()) hash=BitConverter.ToString(digest.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            var cases=rows.Select(row=>JsonValue.Object(new Dictionary<string,JsonValue> {
                {"case_id",JsonValue.String(row.Key)},{"kind",JsonValue.String("assertion")},{"dimensions",JsonValue.Object(new Dictionary<string,JsonValue>{{"assertion_id",JsonValue.String(row.Key.Split('=')[1])}})},
                {"status",JsonValue.String("passed")},{"metrics",JsonValue.Object(new Dictionary<string,JsonValue>())},{"participant_alias",JsonValue.Null()},{"proof_index",JsonValue.Null()},
                {"observations",JsonValue.Object(new Dictionary<string,JsonValue>{{"native_case_id",JsonValue.String(row.Value)},{"native_xml_sha256",JsonValue.String(hash)}})} }));
            var packet=JsonValue.Object(new Dictionary<string,JsonValue>{{"status",JsonValue.String("passed")},{"case_outcomes",JsonValue.Array(cases)}});
            using(var stream=new FileStream(packetPath,FileMode.CreateNew,FileAccess.Write)) using(var writer=new StreamWriter(stream,new UTF8Encoding(false,true))) writer.WriteLine(packet.Canonical());
        }
    }
}
