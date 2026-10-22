using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Resq.Content
{
    // A traversal view supplies semantic changes without allocating another tree.
    // Context values belong to the caller; the canonical encoder only emits bytes.
    public interface ICanonicalJsonProjection
    {
        void ValidateNode(int context, JsonValue value);
        bool IncludeMember(int context, string key);
        int MemberContext(int context, string key);
        int ArrayItemContext(int context);
        bool TryIntegerReference(string key, JsonValue value, out long replacement);
    }

    // One canonical character emitter feeds text, UTF-8, comparison and hashes.
    // It never normalizes input or changes array order. JsonValue owns validation.
    public static class CanonicalJsonWork
    {
        private const int CharacterBlock = 256;
        private const int ComparisonBlock = 128;
        private static readonly IComparer<KeyValuePair<string, JsonValue>> ShortKeyComparer =
            Comparer<KeyValuePair<string, JsonValue>>.Create((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

        public static ResumableWork<string> HashWork(JsonValue value, ICanonicalJsonProjection projection, int context)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            return new ResumableWork<string>(done => Hash(value, done, projection, context));
        }

        internal static IEnumerable<string> Text(JsonValue value, Action<string> done)
        {
            var text = new StringBuilder();
            using (var cursor = new CharacterCursor(value))
            {
                while (cursor.MoveNext())
                {
                    if (cursor.Count != 0) text.Append(cursor.Buffer, 0, cursor.Count);
                    yield return cursor.Stage;
                }
            }
            // A string requires one contiguous immutable result. This final copy is
            // explicit; budgeted snapshot paths use streaming count/equality/hash.
            string result = text.ToString();
            yield return "canonical.materialize";
            done(result);
        }

        internal static IEnumerable<string> Bytes(JsonValue value, Action<byte[]> done)
        {
            var blocks = new LinkedList<byte[]>(); int length = 0;
            using (var cursor = new ByteCursor(value))
            {
                while (cursor.MoveNext())
                {
                    if (cursor.Count != 0)
                    {
                        var block = new byte[cursor.Count];
                        Buffer.BlockCopy(cursor.Buffer, 0, block, 0, block.Length);
                        blocks.AddLast(block); length = checked(length + block.Length);
                    }
                    yield return cursor.Stage;
                }
            }
            var result = new byte[length];
            yield return "canonical.bytes.allocate";
            int offset = 0;
            foreach (var block in blocks)
            {
                Buffer.BlockCopy(block, 0, result, offset, block.Length); offset += block.Length;
                yield return "canonical.bytes.copy";
            }
            done(result);
        }

        internal static IEnumerable<string> ByteCount(JsonValue value, Action<long> done)
        {
            long count = 0;
            using (var cursor = new ByteCursor(value))
            {
                while (cursor.MoveNext())
                {
                    count = checked(count + cursor.Count);
                    yield return cursor.Stage;
                }
            }
            done(count);
        }

        internal static IEnumerable<string> Hash(JsonValue value, Action<string> done,
            ICanonicalJsonProjection projection = null, int context = 0)
        {
            using (var digest = SHA256.Create())
            using (var cursor = new ByteCursor(value, projection, context))
            {
                yield return "canonical.sha256.initialize";
                while (cursor.MoveNext())
                {
                    if (cursor.Count != 0) digest.TransformBlock(cursor.Buffer, 0, cursor.Count, cursor.Buffer, 0);
                    yield return cursor.Count == 0 ? cursor.Stage : "canonical.sha256.chunk";
                }
                digest.TransformFinalBlock(new byte[0], 0, 0);
                yield return "canonical.sha256.finalize";
                byte[] hash = digest.Hash; var hex = new char[hash.Length * 2];
                const string digits = "0123456789abcdef";
                for (int index = 0; index < hash.Length; index++)
                { hex[index * 2] = digits[hash[index] >> 4]; hex[index * 2 + 1] = digits[hash[index] & 15]; }
                done(new string(hex));
            }
        }

        internal static IEnumerable<string> Equal(JsonValue left, JsonValue right, Action<bool> done)
        {
            bool equal = true, leftDone = false, rightDone = false;
            int leftOffset = 0, rightOffset = 0;
            using (var a = new ByteCursor(left))
            using (var b = new ByteCursor(right))
            {
                while (true)
                {
                    if (!leftDone && leftOffset == a.Count)
                    {
                        leftDone = !a.MoveNext(); leftOffset = 0;
                        if (!leftDone) { yield return a.Stage; continue; }
                    }
                    if (!rightDone && rightOffset == b.Count)
                    {
                        rightDone = !b.MoveNext(); rightOffset = 0;
                        if (!rightDone) { yield return b.Stage; continue; }
                    }
                    if (leftDone && rightDone) break;
                    if (leftDone || rightDone)
                    {
                        // Keep consuming both encoders even after a mismatch so an
                        // invalid trailing surrogate is still rejected.
                        equal = false; leftOffset = a.Count; rightOffset = b.Count;
                    }
                    else
                    {
                        int count = Math.Min(a.Count - leftOffset, b.Count - rightOffset);
                        for (int index = 0; index < count; index++)
                            if (a.Buffer[leftOffset + index] != b.Buffer[rightOffset + index]) equal = false;
                        leftOffset += count; rightOffset += count;
                    }
                    yield return "canonical.compare-bytes";
                }
            }
            done(equal);
        }

        private sealed class CharacterBuffer
        {
            internal readonly char[] Data = new char[CharacterBlock];
            internal int Used, Published;
            internal bool Append(char value) { Data[Used++] = value; return Used == Data.Length; }
            internal void Publish() { Published = Used; }
            internal void Reset() { Published = Used = 0; }
        }

        private sealed class CharacterCursor : IDisposable
        {
            private readonly CharacterBuffer buffer = new CharacterBuffer();
            private readonly IEnumerator<string> iterator;
            internal CharacterCursor(JsonValue value, ICanonicalJsonProjection projection = null, int context = 0)
            { iterator = Emit(value, buffer, projection, context).GetEnumerator(); }
            internal char[] Buffer => buffer.Data;
            internal int Count => buffer.Published;
            internal string Stage => iterator.Current;
            internal bool MoveNext() => iterator.MoveNext();
            public void Dispose() { iterator.Dispose(); }
        }

        private sealed class ByteCursor : IDisposable
        {
            private readonly CharacterCursor characters;
            private readonly Encoder encoder = new UTF8Encoding(false, true).GetEncoder();
            internal readonly byte[] Buffer = new byte[CharacterBlock * 4];
            internal int Count { get; private set; }
            internal string Stage { get; private set; }
            private bool flushed;
            internal ByteCursor(JsonValue value, ICanonicalJsonProjection projection = null, int context = 0)
            { characters = new CharacterCursor(value, projection, context); }
            internal bool MoveNext()
            {
                Count = 0;
                if (flushed) return false;
                if (characters.MoveNext())
                {
                    Stage = characters.Stage;
                    if (characters.Count != 0)
                    {
                        Count = encoder.GetBytes(characters.Buffer, 0, characters.Count, Buffer, 0, false);
                        Stage = "canonical.utf8";
                    }
                    return true;
                }
                Count = encoder.GetBytes(characters.Buffer, 0, 0, Buffer, 0, true);
                flushed = true; Stage = "canonical.utf8.finalize"; return true;
            }
            public void Dispose() { characters.Dispose(); }
        }

        private static IEnumerable<string> Emit(JsonValue value, CharacterBuffer buffer,
            ICanonicalJsonProjection projection, int context)
        {
            foreach (string stage in Write(value, buffer, projection, context)) yield return stage;
            if (buffer.Used != 0)
            {
                buffer.Publish(); yield return "canonical.characters"; buffer.Reset();
            }
        }

        private static IEnumerable<string> Token(string token, CharacterBuffer buffer)
        {
            for (int index = 0; index < token.Length; index++)
                if (buffer.Append(token[index]))
                { buffer.Publish(); yield return "canonical.characters"; buffer.Reset(); }
        }

        private static IEnumerable<string> Quote(string value, CharacterBuffer buffer)
        {
            foreach (string stage in Token("\"", buffer)) yield return stage;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '"' || character == '\\' || character < 32)
                {
                    string escaped = character == '"' ? "\\\"" : character == '\\' ? "\\\\"
                        : "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture);
                    foreach (string stage in Token(escaped, buffer)) yield return stage;
                }
                else if (buffer.Append(character))
                { buffer.Publish(); yield return "canonical.characters"; buffer.Reset(); }
            }
            foreach (string stage in Token("\"", buffer)) yield return stage;
        }

        private static IEnumerable<string> Write(JsonValue value, CharacterBuffer buffer,
            ICanonicalJsonProjection projection, int context)
        {
            if (projection != null) projection.ValidateNode(context, value);
            switch (value.Kind)
            {
                case "null": foreach (string stage in Token("null", buffer)) yield return stage; break;
                case "boolean": foreach (string stage in Token(value.Boolean ? "true" : "false", buffer)) yield return stage; break;
                case "integer": foreach (string stage in Token(value.Integer.ToString(CultureInfo.InvariantCulture), buffer)) yield return stage; break;
                case "string": foreach (string stage in Quote(value.Text, buffer)) yield return stage; break;
                case "array":
                    foreach (string stage in Token("[", buffer)) yield return stage;
                    for (int index = 0; index < value.Items.Count; index++)
                    {
                        if (index != 0) foreach (string stage in Token(",", buffer)) yield return stage;
                        foreach (string stage in Write(value.Items[index], buffer, projection,
                            projection == null ? 0 : projection.ArrayItemContext(context))) yield return stage;
                    }
                    foreach (string stage in Token("]", buffer)) yield return stage;
                    break;
                case "object":
                    // Most schema keys are short. Prove the bound before using one
                    // scalar comparison per merge; arbitrary long keys retain the
                    // cooperative comparer and exactly the same ordinal ordering.
                    bool shortKeys = true; int inspected = 0;
                    foreach (var member in value.Members)
                    {
                        if (member.Key.Length > ComparisonBlock) { shortKeys = false; break; }
                        if (++inspected % 64 == 0) yield return "canonical.scan-key-bounds";
                    }
                    KeyValuePair<string, JsonValue>[] members;
                    using (var sort = shortKeys ? ResumableSort.Create(value.Members, ShortKeyComparer)
                        : ResumableSort.Create(value.Members,
                            (KeyValuePair<string, JsonValue> a, KeyValuePair<string, JsonValue> b) =>
                                new ResumableWork<int>(done => CompareOrdinal(a.Key, b.Key, done))))
                    {
                        while (sort.MoveNext()) yield return sort.Stage;
                        members = sort.Result;
                    }
                    foreach (string stage in Token("{", buffer)) yield return stage;
                    bool first = true;
                    for (int index = 0; index < members.Length; index++)
                    {
                        var member = members[index];
                        if (projection != null && !projection.IncludeMember(context, member.Key))
                        { yield return "canonical.projection.exclude-field"; continue; }
                        if (!first) foreach (string stage in Token(",", buffer)) yield return stage;
                        first = false;
                        foreach (string stage in Quote(member.Key, buffer)) yield return stage;
                        foreach (string stage in Token(":", buffer)) yield return stage;
                        if (projection != null && projection.TryIntegerReference(member.Key, member.Value, out long replacement))
                        {
                            foreach (string stage in Token(replacement.ToString(CultureInfo.InvariantCulture), buffer)) yield return stage;
                        }
                        else
                        {
                            foreach (string stage in Write(member.Value, buffer, projection,
                                projection == null ? 0 : projection.MemberContext(context, member.Key))) yield return stage;
                        }
                    }
                    foreach (string stage in Token("}", buffer)) yield return stage;
                    break;
                default: throw new ContentException("JSON_TYPE", value.Kind);
            }
        }

        private static IEnumerable<string> CompareOrdinal(string left, string right, Action<int> done)
        {
            int length = Math.Min(left.Length, right.Length), batch = 0;
            for (int index = 0; index < length; index++)
            {
                int difference = left[index] - right[index];
                if (difference != 0) { done(difference); yield break; }
                if (++batch == ComparisonBlock) { batch = 0; yield return "canonical.compare-key"; }
            }
            done(left.Length.CompareTo(right.Length));
        }
    }
}
