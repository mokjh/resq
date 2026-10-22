using System;
using System.Collections.Generic;

namespace Resq.Sim.Testing
{
    /// <summary>
    /// Marks a parameterless public method as a simulation test. The same tests run headlessly
    /// (tools/simtest/simtest.py) and inside the Unity Test Runner through the NUnit adapter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SimTestAttribute : Attribute
    {
        public SimTestAttribute(string category = "") { Category = category; }
        public string Category { get; }
    }

    public sealed class CheckFailedException : Exception
    {
        public CheckFailedException(string message) : base(message) { }
    }

    public static class Check
    {
        public static void True(bool condition, string message = null)
        {
            if (!condition) throw new CheckFailedException(message ?? "expected true");
        }

        public static void False(bool condition, string message = null)
        {
            if (condition) throw new CheckFailedException(message ?? "expected false");
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new CheckFailedException($"{message ?? "values differ"}: expected <{expected}> actual <{actual}>");
        }

        public static void Sequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message = null)
        {
            if (expected.Count != actual.Count)
                throw new CheckFailedException($"{message ?? "sequence"}: expected length {expected.Count}, actual {actual.Count} " +
                                               $"[{string.Join(",", actual)}]");
            for (int i = 0; i < expected.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                    throw new CheckFailedException($"{message ?? "sequence"}: index {i} expected <{expected[i]}> actual <{actual[i]}> " +
                                                   $"[{string.Join(",", actual)}]");
            }
        }

        public static void Throws<TException>(Action action, string message = null) where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            throw new CheckFailedException(message ?? $"expected {typeof(TException).Name}");
        }

        public static void Fail(string message) => throw new CheckFailedException(message);
    }
}
