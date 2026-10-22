using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Resq.Content
{
    // Single-threaded owned work. Constructing a job never invokes its routine.
    // A result is private until the iterator has completed and its finally blocks ran.
    public sealed class ResumableWork<T> : IDisposable
    {
        private Func<Action<T>, IEnumerable<string>> routine;
        private IEnumerator<string> iterator;
        private T result;
        private bool resultSet, disposed, advancing;
        public bool IsComplete { get; private set; }
        public string Stage { get; private set; }
        public T Result => IsComplete ? result : throw new InvalidOperationException("Resumable work is not complete");

        public ResumableWork(Func<Action<T>, IEnumerable<string>> routine)
        { this.routine = routine ?? throw new ArgumentNullException(nameof(routine)); }

        public bool MoveNext()
        {
            if (IsComplete) return false;
            if (disposed) throw new ObjectDisposedException(nameof(ResumableWork<T>));
            if (advancing) throw new InvalidOperationException("Resumable work cannot be advanced recursively");
            advancing = true;
            try
            {
                if (iterator == null)
                {
                    iterator = routine(SetResult).GetEnumerator();
                    routine = null;
                }
                if (iterator.MoveNext()) { Stage = iterator.Current; return true; }
                if (!resultSet) throw new InvalidOperationException("Resumable routine completed without a result");
                iterator.Dispose(); iterator = null;
                IsComplete = true;
                return false;
            }
            catch
            {
                Dispose();
                throw;
            }
            finally { advancing = false; }
        }

        private void SetResult(T value)
        {
            if (disposed || resultSet) throw new InvalidOperationException("Resumable result may be assigned once");
            result = value; resultSet = true;
        }

        public T Drain() { while (MoveNext()) { } return Result; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; routine = null;
            var pending = iterator; iterator = null;
            try { if (pending != null) pending.Dispose(); }
            finally { if (!IsComplete) { result = default(T); resultSet = false; } }
        }
    }

    // Bottom-up merge sorting shares the same executor in synchronous and yielded
    // callers. Comparers must be bounded; long-key callers use the work overload.
    public static class ResumableSort
    {
        private const int Batch = 64;
        public static ResumableWork<T[]> Create<T>(IEnumerable<T> source, IComparer<T> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));
            return new ResumableWork<T[]>(done => Sort(source, comparer, null, done));
        }

        public static ResumableWork<T[]> Create<T>(IEnumerable<T> source, Func<T, T, ResumableWork<int>> comparer)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (comparer == null) throw new ArgumentNullException(nameof(comparer));
            return new ResumableWork<T[]>(done => Sort(source, null, comparer, done));
        }

        private static IEnumerable<string> Sort<T>(IEnumerable<T> source, IComparer<T> comparer,
            Func<T, T, ResumableWork<int>> compareWork, Action<T[]> done)
        {
            T[] values;
            using (var copy = new ResumableWork<T[]>(finish => ResumableCollection.Copy(source, finish)))
            {
                while (copy.MoveNext()) yield return copy.Stage;
                values = copy.Result;
            }
            if (values.Length < 2) { done(values); yield break; }
            var scratch = new T[values.Length];
            yield return "sort.allocate-scratch";
            int batch = 0;
            for (long width = 1; width < values.Length; width *= 2)
            {
                for (long start = 0; start < values.Length; start += width * 2)
                {
                    int left = (int)start, middle = (int)Math.Min(start + width, values.Length);
                    int right = middle, end = (int)Math.Min(start + width * 2, values.Length), target = left;
                    while (target < end)
                    {
                        bool takeLeft;
                        if (left == middle) takeLeft = false;
                        else if (right == end) takeLeft = true;
                        else if (compareWork == null) takeLeft = comparer.Compare(values[left], values[right]) <= 0;
                        else
                        {
                            using (var comparison = compareWork(values[left], values[right]))
                            {
                                while (comparison.MoveNext()) yield return comparison.Stage;
                                takeLeft = comparison.Result <= 0;
                            }
                        }
                        scratch[target++] = takeLeft ? values[left++] : values[right++];
                        if (++batch == Batch) { batch = 0; yield return "sort.merge"; }
                    }
                }
                var swap = values; values = scratch; scratch = swap;
                yield return "sort.pass";
            }
            done(values);
        }
    }

    internal static class ResumableCollection
    {
        private const int Batch = 64;
        // Large JSON arrays need immutable indexed access, not one contiguous
        // allocation. Every block is private until the entire copy completes.
        internal static IEnumerable<string> CopyReadOnly<T>(IEnumerable<T> source, Action<ReadOnlyCollection<T>> done)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int expected = source is ICollection<T> sized ? sized.Count : -1;
            var result = new OwnedChunks<T>();
            foreach (var item in source)
            {
                if (expected >= 0 && result.Count == expected) throw new InvalidOperationException("Collection changed while copying");
                result.Append(item);
                if (result.Count % Batch == 0) yield return "collection.copy_block";
            }
            if (expected >= 0 && result.Count != expected) throw new InvalidOperationException("Collection changed while copying");
            done(result.Publish());
        }
        private sealed class OwnedChunks<T> : IList<T>
        {
            private readonly List<T[]> blocks = new List<T[]>();
            private bool published;
            public int Count { get; private set; }
            public bool IsReadOnly => true;
            internal void Append(T item)
            {
                if (published) throw new InvalidOperationException("Published collection is immutable");
                int offset = Count % Batch;
                if (offset == 0) blocks.Add(new T[Batch]);
                blocks[Count / Batch][offset] = item;
                Count = checked(Count + 1);
            }
            internal ReadOnlyCollection<T> Publish()
            { published = true; return new ReadOnlyCollection<T>(this); }
            public T this[int index]
            {
                get { if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index)); return blocks[index / Batch][index % Batch]; }
                set => throw new NotSupportedException("Immutable collection");
            }
            public IEnumerator<T> GetEnumerator() { for (int index = 0; index < Count; index++) yield return this[index]; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public int IndexOf(T item)
            { var comparer = EqualityComparer<T>.Default; for (int index = 0; index < Count; index++) if (comparer.Equals(this[index], item)) return index; return -1; }
            public bool Contains(T item) => IndexOf(item) >= 0;
            public void CopyTo(T[] array, int arrayIndex)
            {
                if (array == null) throw new ArgumentNullException(nameof(array));
                if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
                if (arrayIndex > array.Length || Count > array.Length - arrayIndex) throw new ArgumentException("Insufficient destination capacity");
                for (int index = 0; index < Count; index++) array[arrayIndex + index] = this[index];
            }
            public void Add(T item) => throw new NotSupportedException("Immutable collection");
            public void Clear() => throw new NotSupportedException("Immutable collection");
            public void Insert(int index, T item) => throw new NotSupportedException("Immutable collection");
            public bool Remove(T item) => throw new NotSupportedException("Immutable collection");
            public void RemoveAt(int index) => throw new NotSupportedException("Immutable collection");
        }
        internal static IEnumerable<string> Copy<T>(IEnumerable<T> source, Action<T[]> done)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source is ICollection<T> sized)
            {
                // Allocation remains an explicit measured unit; element copying
                // never hides in ToArray/List growth or a collection constructor.
                var result = new T[sized.Count];
                yield return "collection.allocate";
                int index = 0;
                foreach (var item in source)
                {
                    if (index == result.Length) throw new InvalidOperationException("Collection changed while copying");
                    result[index++] = item;
                    if (index % Batch == 0) yield return "collection.copy";
                }
                if (index != result.Length) throw new InvalidOperationException("Collection changed while copying");
                done(result); yield break;
            }
            var chunks = new LinkedList<T[]>(); T[] chunk = null;
            int count = 0, used = Batch;
            foreach (var item in source)
            {
                if (used == Batch) { chunk = new T[Batch]; chunks.AddLast(chunk); used = 0; }
                chunk[used++] = item; count = checked(count + 1);
                if (used == Batch) yield return "collection.collect";
            }
            yield return "collection.collect";
            var collected = new T[count];
            yield return "collection.allocate";
            int offset = 0;
            foreach (var block in chunks)
            {
                int length = Math.Min(Batch, count - offset);
                for (int index = 0; index < length; index++) collected[offset++] = block[index];
                yield return "collection.copy";
            }
            done(collected);
        }
    }
}
