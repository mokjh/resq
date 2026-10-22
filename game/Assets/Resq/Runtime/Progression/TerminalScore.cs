using System;
using System.Numerics;

namespace Resq.Progression
{
    // Only terminal scoring uses arbitrary precision. No component is rounded.
    public static class TerminalScore
    {
        public static int Compute(long rescued, long population, long requiredCompleted, long requiredCount,
            long protectionCompleted, long protectionCount, long remaining, long budget)
        {
            Nonnegative(rescued, nameof(rescued)); Positive(population, nameof(population));
            Nonnegative(requiredCompleted, nameof(requiredCompleted)); Positive(requiredCount, nameof(requiredCount));
            Nonnegative(protectionCompleted, nameof(protectionCompleted)); Nonnegative(protectionCount, nameof(protectionCount));
            Nonnegative(remaining, nameof(remaining)); Positive(budget, nameof(budget));
            if (protectionCount == 0 && protectionCompleted != 0) throw new ArgumentOutOfRangeException(nameof(protectionCompleted));
            var score = new Fraction(6000 * (BigInteger)Math.Min(rescued, population), population)
                .Add(new Fraction(2000 * (BigInteger)Math.Min(requiredCompleted, requiredCount), requiredCount))
                .Add(protectionCount == 0 ? new Fraction(1000, 1)
                    : new Fraction(1000 * (BigInteger)Math.Min(protectionCompleted, protectionCount), protectionCount))
                .Add(new Fraction(1000 * (BigInteger)Math.Min(remaining, budget), budget));
            BigInteger remainder;
            BigInteger rounded = BigInteger.DivRem(score.Numerator, score.Denominator, out remainder);
            if (2 * remainder >= score.Denominator) rounded += BigInteger.One;
            return checked((int)rounded);
        }

        private static void Nonnegative(long value, string name) { if (value < 0) throw new ArgumentOutOfRangeException(name); }
        private static void Positive(long value, string name) { if (value <= 0) throw new ArgumentOutOfRangeException(name); }

        private readonly struct Fraction
        {
            public BigInteger Numerator { get; }
            public BigInteger Denominator { get; }
            public Fraction(BigInteger numerator, BigInteger denominator)
            {
                if (numerator < 0 || denominator <= 0) throw new ArgumentOutOfRangeException();
                BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
                Numerator = numerator / divisor; Denominator = denominator / divisor;
            }
            public Fraction Add(Fraction other)
            {
                BigInteger divisor = BigInteger.GreatestCommonDivisor(Denominator, other.Denominator);
                BigInteger leftMultiplier = other.Denominator / divisor;
                BigInteger rightMultiplier = Denominator / divisor;
                return new Fraction(Numerator * leftMultiplier + other.Numerator * rightMultiplier, Denominator * leftMultiplier);
            }
        }
    }
}
