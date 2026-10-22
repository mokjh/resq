using System.Numerics;

namespace Resq.Sim
{
    /// <summary>Terminal score (spec 02 §11): exact rational sum of four terms, rounded once, ties upward.</summary>
    public static class Score
    {
        public struct Result
        {
            public BigInteger Numerator;
            public BigInteger Denominator;
            public long Rounded;
        }

        public static Result Compute(long rescued, long population, long requiredComplete, long requiredTotal,
                                     long protectionComplete, long protectionTotal, long remainingSupplies, long budget)
        {
            // Each fraction capped to [0,1]; empty protection list means fraction 1.
            var a = Frac(rescued, population);
            var b = Frac(requiredComplete, requiredTotal);
            var c = protectionTotal <= 0 ? (BigInteger.One, BigInteger.One) : Frac(protectionComplete, protectionTotal);
            var d = Frac(remainingSupplies, budget);

            BigInteger num = 0, den = 1;
            Add(ref num, ref den, 6000 * a.n, a.d);
            Add(ref num, ref den, 2000 * b.n, b.d);
            Add(ref num, ref den, 1000 * c.Item1, c.Item2);
            Add(ref num, ref den, 1000 * d.n, d.d);
            var g = BigInteger.GreatestCommonDivisor(num, den);
            if (!g.IsZero) { num /= g; den /= g; }
            // Round half up for nonnegative values: floor((2*num + den) / (2*den)).
            BigInteger rounded = BigInteger.Divide(2 * num + den, 2 * den);
            return new Result { Numerator = num, Denominator = den, Rounded = (long)rounded };
        }

        static (BigInteger n, BigInteger d) Frac(long n, long d)
        {
            if (d <= 0) throw new SimFaultException("score denominator must be positive");
            if (n < 0) n = 0;
            if (n > d) n = d;
            return (n, d);
        }

        static void Add(ref BigInteger num, ref BigInteger den, BigInteger n, BigInteger d)
        {
            num = num * d + n * den;
            den *= d;
        }
    }
}
