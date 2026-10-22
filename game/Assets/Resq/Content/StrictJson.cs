using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Resq.Content
{
    public sealed class ContentException : Exception
    {
        public string Code { get; }
        public ContentException(string code, string detail) : base(code + ": " + detail) { Code = code; }
    }

    // Immutable syntax tree. Collections and dictionaries are copied before publication.
    public sealed class JsonValue
    {
        public string Kind { get; }
        private readonly object value;
        private JsonValue(string kind, object value) { Kind = kind; this.value = value; }
        public bool IsNull => Kind == "null";
        public string Text => Kind == "string" ? (string)value : throw new ContentException("JSON_TYPE", "string required");
        public long Integer => Kind == "integer" ? (long)value : throw new ContentException("JSON_TYPE", "integer required");
        public bool Boolean => Kind == "boolean" ? (bool)value : throw new ContentException("JSON_TYPE", "boolean required");
        public ReadOnlyCollection<JsonValue> Items => Kind == "array" ? (ReadOnlyCollection<JsonValue>)value : throw new ContentException("JSON_TYPE", "array required");
        public ReadOnlyDictionary<string, JsonValue> Members => Kind == "object" ? (ReadOnlyDictionary<string, JsonValue>)value : throw new ContentException("JSON_TYPE", "object required");
        public JsonValue this[string key] => Members.TryGetValue(key, out var item) ? item : throw new ContentException("MISSING_FIELD", key);
        public bool Has(string key) => Kind == "object" && Members.ContainsKey(key);
        public static JsonValue Null() => new JsonValue("null", null);
        public static JsonValue String(string value) { CheckString(value); return new JsonValue("string", value); }
        public static JsonValue Number(long value) => new JsonValue("integer", value);
        public static JsonValue Bool(bool value) => new JsonValue("boolean", value);
        public static JsonValue Array(IEnumerable<JsonValue> value) => new JsonValue("array", value.ToList().AsReadOnly());
        public static JsonValue Object(IDictionary<string, JsonValue> value) => new JsonValue("object", new ReadOnlyDictionary<string, JsonValue>(new Dictionary<string, JsonValue>(value, StringComparer.Ordinal)));
        public static ResumableWork<JsonValue> ArrayWork(IEnumerable<JsonValue> value) => new ResumableWork<JsonValue>(done => CopyArray(value, done));
        public static ResumableWork<JsonValue> ObjectWork(IDictionary<string, JsonValue> value) => new ResumableWork<JsonValue>(done => CopyObject(value, done));
        public JsonValue With(string key, JsonValue item) { var copy = new Dictionary<string, JsonValue>(Members, StringComparer.Ordinal); copy[key] = item; return Object(copy); }
        public JsonValue Without(params string[] keys) => Object(Members.Where(pair => !keys.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        public ResumableWork<string> CanonicalWork() => new ResumableWork<string>(done => CanonicalJsonWork.Text(this, done));
        public ResumableWork<string> Sha256Work() => new ResumableWork<string>(done => CanonicalJsonWork.Hash(this, done));
        public ResumableWork<long> CanonicalByteCountWork() => new ResumableWork<long>(done => CanonicalJsonWork.ByteCount(this, done));
        public ResumableWork<bool> CanonicalEqualsWork(JsonValue other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            return new ResumableWork<bool>(done => CanonicalJsonWork.Equal(this, other, done));
        }
        public string Canonical() { using (var work = CanonicalWork()) return work.Drain(); }
        public byte[] CanonicalBytes() { using (var work = new ResumableWork<byte[]>(done => CanonicalJsonWork.Bytes(this, done))) return work.Drain(); }
        public string Sha256() { using (var work = Sha256Work()) return work.Drain(); }
        private static IEnumerable<string> CopyArray(IEnumerable<JsonValue> source, Action<JsonValue> done)
        {
            using (var copy = new ResumableWork<ReadOnlyCollection<JsonValue>>(finish => ResumableCollection.CopyReadOnly(source, finish)))
            {
                while (copy.MoveNext()) yield return copy.Stage;
                done(new JsonValue("array", copy.Result));
            }
        }
        private static IEnumerable<string> CopyObject(IDictionary<string, JsonValue> source, Action<JsonValue> done)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new Dictionary<string, JsonValue>(source.Count, StringComparer.Ordinal); int batch = 0;
            yield return "json.allocate-object";
            foreach (var pair in source)
            { copy.Add(pair.Key, pair.Value); if (++batch == 64) { batch = 0; yield return "json.copy-object"; } }
            done(new JsonValue("object", new ReadOnlyDictionary<string, JsonValue>(copy)));
        }
        internal static void CheckString(string value)
        {
            if (value == null) throw new ContentException("JSON_STRING", "null string");
            for (int index = 0; index < value.Length; index++)
                if (char.IsSurrogate(value[index])) { if (!char.IsHighSurrogate(value[index]) || index + 1 == value.Length || !char.IsLowSurrogate(value[++index])) throw new ContentException("JSON_UNICODE", "lone surrogate"); }
            if (!value.IsNormalized(NormalizationForm.FormC)) throw new ContentException("JSON_NFC", "NFC required");
        }
    }

    public static class StrictJson
    {
        public static JsonValue Parse(byte[] bytes)
        {
            if (bytes.Length > 64 * 1024 * 1024) throw new ContentException("JSON_BOUND", "64 MiB limit");
            try { return Parse(new UTF8Encoding(false, true).GetString(bytes)); }
            catch (DecoderFallbackException error) { throw new ContentException("JSON_UTF8", error.Message); }
        }
        public static JsonValue Parse(string text) { var reader = new Reader(text); var value = reader.Value(0); reader.White(); if (!reader.Done) throw new ContentException("JSON_TRAILING", "trailing bytes"); return value; }
        private sealed class Reader
        {
            private readonly string text; private int cursor; private int nodes;
            public Reader(string text) { this.text = text ?? throw new ArgumentNullException(nameof(text)); if (text.Length > 64 * 1024 * 1024 || (text.Length > 0 && text[0] == '\ufeff')) throw new ContentException("JSON_PROFILE", "size/BOM"); }
            public bool Done => cursor == text.Length;
            public void White() { while (cursor < text.Length && (text[cursor] == ' ' || text[cursor] == '\t' || text[cursor] == '\r' || text[cursor] == '\n')) cursor++; }
            private char Peek => cursor < text.Length ? text[cursor] : '\0';
            private void Take(char expected) { if (Peek != expected) throw new ContentException("JSON_SYNTAX", "expected " + expected + " at " + cursor); cursor++; }
            public JsonValue Value(int depth)
            {
                if (depth > 64 || ++nodes > 2000000) throw new ContentException("JSON_BOUND", "depth/node bound");
                White(); char character = Peek;
                if (character == '{') { cursor++; White(); var members = new Dictionary<string, JsonValue>(StringComparer.Ordinal); if (Peek != '}') while (true) { string key = String(); if (key.Any(value => value > 127)) throw new ContentException("JSON_KEY", "ASCII key required"); White(); Take(':'); var item = Value(depth + 1); if (members.ContainsKey(key)) throw new ContentException("DUPLICATE_KEY", key); members.Add(key, item); White(); if (Peek != ',') break; cursor++; White(); } Take('}'); return JsonValue.Object(members); }
                if (character == '[') { cursor++; White(); var items = new List<JsonValue>(); if (Peek != ']') while (true) { items.Add(Value(depth + 1)); White(); if (Peek != ',') break; cursor++; } Take(']'); return JsonValue.Array(items); }
                if (character == '"') return JsonValue.String(String());
                if (character == 't') { Literal("true"); return JsonValue.Bool(true); }
                if (character == 'f') { Literal("false"); return JsonValue.Bool(false); }
                if (character == 'n') { Literal("null"); return JsonValue.Null(); }
                int start = cursor; if (Peek == '-') cursor++; if (Peek < '0' || Peek > '9') throw new ContentException("JSON_SYNTAX", "integer required");
                if (Peek == '0') cursor++; else while (Peek >= '0' && Peek <= '9') cursor++;
                string digits = text.Substring(start, cursor - start);
                if (digits == "-0" || Peek == '.' || Peek == 'e' || Peek == 'E' || (Peek >= '0' && Peek <= '9')) throw new ContentException("JSON_INTEGER", "integer profile violation");
                if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result)) throw new ContentException("JSON_INTEGER", "signed64 overflow");
                return JsonValue.Number(result);
            }
            private void Literal(string value) { foreach (char character in value) Take(character); }
            private string String()
            {
                Take('"'); var result = new StringBuilder(); while (Peek != '"')
                {
                    char character = Peek; if (character < 32) throw new ContentException("JSON_STRING", "unterminated/control"); cursor++;
                    if (character != '\\') { result.Append(character); continue; }
                    character = Peek; cursor++;
                    switch (character) { case '"': case '\\': case '/': result.Append(character); break; case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break; case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                        case 'u': if (cursor + 4 > text.Length || !ushort.TryParse(text.Substring(cursor, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort point)) throw new ContentException("JSON_ESCAPE", "invalid unicode escape"); result.Append((char)point); cursor += 4; break;
                        default: throw new ContentException("JSON_ESCAPE", "invalid escape"); }
                }
                Take('"'); string value = result.ToString(); JsonValue.CheckString(value); return value;
            }
        }
    }

    public abstract class ContentRecord
    {
        private readonly JsonValue value;
        protected ContentRecord(JsonValue value) { this.value = value; }
        public JsonValue ToJson() => value;
    }
    internal static class SchemaValidator
    {
        public static void Validate(JsonValue schema, JsonValue value, JsonValue definitions, string path)
        {
            if (schema.Has("$ref")) { Validate(definitions[schema["$ref"].Text.Substring(8)], value, definitions, path); return; }
            if (schema.Has("oneOf"))
            {
                int matches = 0;
                foreach (var alternative in schema["oneOf"].Items) { try { Validate(alternative, value, definitions, path); matches++; } catch (ContentException) { } }
                if (matches != 1) throw new ContentException("SCHEMA_UNION", path + " must match exactly one closed alternative"); return;
            }
            if (schema.Has("type") && schema["type"].Text != value.Kind) throw new ContentException("SCHEMA_TYPE", path);
            if (schema.Has("const") && schema["const"].Canonical() != value.Canonical()) throw new ContentException("SCHEMA_CONST", path);
            if (schema.Has("enum") && !schema["enum"].Items.Any(item => item.Canonical() == value.Canonical())) throw new ContentException("SCHEMA_ENUM", path);
            if (schema.Has("minimum") && value.Integer < schema["minimum"].Integer || schema.Has("maximum") && value.Integer > schema["maximum"].Integer) throw new ContentException("SCHEMA_BOUND", path);
            if (schema.Has("pattern") && !Regex.IsMatch(value.Text, schema["pattern"].Text+"\\z", RegexOptions.CultureInvariant)) throw new ContentException("SCHEMA_PATTERN", path);
            if (value.Kind == "object")
            {
                var properties = schema["properties"];
                foreach (var field in schema["required"].Items) if (!value.Has(field.Text)) throw new ContentException("MISSING_FIELD", path + "." + field.Text);
                foreach (var field in value.Members) if (!properties.Has(field.Key)) throw new ContentException("UNKNOWN_FIELD", path + "." + field.Key);
                // Discriminators fail before recursive payloads, independent of input property order.
                foreach (var field in properties.Members.OrderBy(pair => pair.Key == "kind" || pair.Key == "id" ? 0 : 1)) Validate(field.Value, value[field.Key], definitions, path + "." + field.Key);
            }
            if (value.Kind == "array")
            {
                if (value.Items.Count < schema["minItems"].Integer || value.Items.Count > schema["maxItems"].Integer) throw new ContentException("SCHEMA_BOUND", path);
                foreach (var item in value.Items) Validate(schema["items"], item, definitions, path + "[]");
            }
        }
    }
}
