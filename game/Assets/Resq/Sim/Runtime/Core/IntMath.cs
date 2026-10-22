using System;

namespace Resq.Sim
{
    /// <summary>Exact integer helpers used by every rule (spec 02 §1). All arithmetic is checked.</summary>
    public static class IntMath
    {
        /// <summary>Mathematical floor division (rounds toward negative infinity). b must be positive.</summary>
        public static long FloorDiv(long a, long b)
        {
            if (b <= 0) throw new SimFaultException($"FloorDiv by non-positive {b}");
            long q = a / b;
            if ((a % b != 0) && (a < 0)) q--;
            return q;
        }

        /// <summary>Mathematical ceiling division. b must be positive.</summary>
        public static long CeilDiv(long a, long b) => checked(-FloorDiv(-a, b));

        public static long FloorMod(long a, long b) => checked(a - FloorDiv(a, b) * b);

        public static long Clamp(long value, long min, long max) => value < min ? min : (value > max ? max : value);

        public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        public static long Mul(long a, long b) => checked(a * b);

        public static long Add(long a, long b) => checked(a + b);
    }

    /// <summary>A rule invariant failed (spec 02 §1: SIMULATION_INVARIANT_FAULT). The run stops; nothing is awarded.</summary>
    public sealed class SimFaultException : Exception
    {
        public SimFaultException(string message) : base(message) { }
    }
}
