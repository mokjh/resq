using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Resq.Sim
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal JSON DOM used for content loading and canonical serialisation. Numbers keep their raw text so
    /// integers round-trip exactly; object members keep insertion order (canonical output sorts by ordinal key).
    /// </summary>
    public sealed class JsonNode
    {
        public JsonKind Kind;
        public bool Bool;
        public string Text; // number literal or string value
        public List<JsonNode> Items;
        public List<KeyValuePair<string, JsonNode>> Members;

        public static readonly JsonNode Null = new JsonNode { Kind = JsonKind.Null };

        public bool IsNull => Kind == JsonKind.Null;

        public JsonNode this[string key]
        {
            get
            {
                if (Kind != JsonKind.Object) return null;
                for (int i = 0; i < Members.Count; i++)
                    if (string.Equals(Members[i].Key, key, StringComparison.Ordinal)) return Members[i].Value;
                return null;
            }
        }

        public JsonNode this[int index] => Kind == JsonKind.Array ? Items[index] : null;

        public int Count => Kind == JsonKind.Array ? Items.Count : Kind == JsonKind.Object ? Members.Count : 0;

        public bool Has(string key) => this[key] != null;

        public long AsLong()
        {
            if (Kind != JsonKind.Number) throw new FormatException($"Expected number, got {Kind}");
            return long.Parse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        public int AsInt() => checked((int)AsLong());

        public string AsString()
        {
            if (Kind == JsonKind.String) return Text;
            if (Kind == JsonKind.Null) return null;
            throw new FormatException($"Expected string, got {Kind}");
        }

        public bool AsBool()
        {
            if (Kind != JsonKind.Bool) throw new FormatException($"Expected bool, got {Kind}");
            return Bool;
        }

        public long Long(string key, long fallback = 0)
        {
            var n = this[key];
            return n == null || n.IsNull ? fallback : n.AsLong();
        }

        public int Int(string key, int fallback = 0) => checked((int)Long(key, fallback));

        public string Str(string key, string fallback = null)
        {
            var n = this[key];
            return n == null || n.IsNull ? fallback : n.AsString();
        }

        public bool Flag(string key, bool fallback = false)
        {
            var n = this[key];
            return n == null || n.IsNull ? fallback : n.AsBool();
        }

        public IEnumerable<JsonNode> Elements()
        {
            if (Kind == JsonKind.Array)
                foreach (var item in Items) yield return item;
        }

        public IEnumerable<KeyValuePair<string, JsonNode>> Properties()
        {
            if (Kind == JsonKind.Object)
                foreach (var m in Members) yield return m;
        }

        // ---------- construction helpers ----------
        public static JsonNode NewObject() => new JsonNode { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonNode>>() };
        public static JsonNode NewArray() => new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
        public static JsonNode OfString(string s) => s == null ? Null : new JsonNode { Kind = JsonKind.String, Text = s };
        public static JsonNode OfNumber(long v) => new JsonNode { Kind = JsonKind.Number, Text = v.ToString(CultureInfo.InvariantCulture) };
        public static JsonNode OfBool(bool v) => new JsonNode { Kind = JsonKind.Bool, Bool = v };

        public JsonNode Set(string key, JsonNode value)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    Members[i] = new KeyValuePair<string, JsonNode>(key, value);
                    return this;
                }
            }
            Members.Add(new KeyValuePair<string, JsonNode>(key, value));
            return this;
        }

        public JsonNode Set(string key, long value) => Set(key, OfNumber(value));
        public JsonNode Set(string key, string value) => Set(key, OfString(value));
        public JsonNode Set(string key, bool value) => Set(key, OfBool(value));

        public JsonNode Add(JsonNode value)
        {
            Items.Add(value);
            return this;
        }

        // ---------- parsing ----------
        public static JsonNode Parse(string text)
        {
            int pos = 0;
            var node = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length) throw new FormatException($"Trailing JSON content at {pos}");
            return node;
        }

        static void SkipWs(string s, ref int p)
        {
            while (p < s.Length && (s[p] == ' ' || s[p] == '\n' || s[p] == '\r' || s[p] == '\t')) p++;
        }

        static JsonNode ParseValue(string s, ref int p)
        {
            SkipWs(s, ref p);
            if (p >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[p];
            switch (c)
            {
                case '{':
                {
                    p++;
                    var obj = NewObject();
                    SkipWs(s, ref p);
                    if (s[p] == '}') { p++; return obj; }
                    while (true)
                    {
                        SkipWs(s, ref p);
                        string key = ParseString(s, ref p);
                        SkipWs(s, ref p);
                        if (s[p] != ':') throw new FormatException($"Expected ':' at {p}");
                        p++;
                        obj.Members.Add(new KeyValuePair<string, JsonNode>(key, ParseValue(s, ref p)));
                        SkipWs(s, ref p);
                        if (s[p] == ',') { p++; continue; }
                        if (s[p] == '}') { p++; return obj; }
                        throw new FormatException($"Expected ',' or '}}' at {p}");
                    }
                }
                case '[':
                {
                    p++;
                    var arr = NewArray();
                    SkipWs(s, ref p);
                    if (s[p] == ']') { p++; return arr; }
                    while (true)
                    {
                        arr.Items.Add(ParseValue(s, ref p));
                        SkipWs(s, ref p);
                        if (s[p] == ',') { p++; continue; }
                        if (s[p] == ']') { p++; return arr; }
                        throw new FormatException($"Expected ',' or ']' at {p}");
                    }
                }
                case '"':
                    return new JsonNode { Kind = JsonKind.String, Text = ParseString(s, ref p) };
                case 't':
                    Expect(s, ref p, "true");
                    return OfBool(true);
                case 'f':
                    Expect(s, ref p, "false");
                    return OfBool(false);
                case 'n':
                    Expect(s, ref p, "null");
                    return Null;
                default:
                {
                    int start = p;
                    if (s[p] == '-') p++;
                    while (p < s.Length && (char.IsDigit(s[p]) || s[p] == '.' || s[p] == 'e' || s[p] == 'E' || s[p] == '+' || s[p] == '-')) p++;
                    if (p == start) throw new FormatException($"Unexpected character '{c}' at {p}");
                    return new JsonNode { Kind = JsonKind.Number, Text = s.Substring(start, p - start) };
                }
            }
        }

        static void Expect(string s, ref int p, string literal)
        {
            if (string.CompareOrdinal(s, p, literal, 0, literal.Length) != 0) throw new FormatException($"Expected {literal} at {p}");
            p += literal.Length;
        }

        static string ParseString(string s, ref int p)
        {
            if (s[p] != '"') throw new FormatException($"Expected string at {p}");
            p++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[p++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[p++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        p += 4;
                        break;
                    default: throw new FormatException($"Bad escape at {p}");
                }
            }
        }

        // ---------- canonical writing ----------
        /// <summary>Canonical compact JSON: object keys sorted by ordinal, no whitespace (spec fixture RP01).</summary>
        public string ToCanonical()
        {
            var sb = new StringBuilder();
            WriteCanonical(sb, this);
            return sb.ToString();
        }

        /// <summary>Readable JSON with 2-space indent, keys in insertion order.</summary>
        public string ToPretty()
        {
            var sb = new StringBuilder();
            WritePretty(sb, this, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static void WriteCanonical(StringBuilder sb, JsonNode n)
        {
            switch (n.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(n.Bool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(n.Text); break;
                case JsonKind.String: WriteString(sb, n.Text); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < n.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteCanonical(sb, n.Items[i]);
                    }
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    var members = new List<KeyValuePair<string, JsonNode>>(n.Members);
                    members.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                    sb.Append('{');
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(sb, members[i].Key);
                        sb.Append(':');
                        WriteCanonical(sb, members[i].Value);
                    }
                    sb.Append('}');
                    break;
            }
        }

        static void WritePretty(StringBuilder sb, JsonNode n, int indent)
        {
            switch (n.Kind)
            {
                case JsonKind.Array:
                    if (n.Items.Count == 0) { sb.Append("[]"); return; }
                    bool scalars = n.Items.TrueForAll(x => x.Kind != JsonKind.Array && x.Kind != JsonKind.Object);
                    if (scalars)
                    {
                        sb.Append('[');
                        for (int i = 0; i < n.Items.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            WriteCanonical(sb, n.Items[i]);
                        }
                        sb.Append(']');
                        return;
                    }
                    sb.Append("[\n");
                    for (int i = 0; i < n.Items.Count; i++)
                    {
                        sb.Append(' ', indent + 2);
                        WritePretty(sb, n.Items[i], indent + 2);
                        if (i < n.Items.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', indent).Append(']');
                    return;
                case JsonKind.Object:
                    if (n.Members.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    for (int i = 0; i < n.Members.Count; i++)
                    {
                        sb.Append(' ', indent + 2);
                        WriteString(sb, n.Members[i].Key);
                        sb.Append(": ");
                        WritePretty(sb, n.Members[i].Value, indent + 2);
                        if (i < n.Members.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    sb.Append(' ', indent).Append('}');
                    return;
                default:
                    WriteCanonical(sb, n);
                    return;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
