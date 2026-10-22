using System;

namespace Resq.Sim.Rules
{
    /// <summary>Exact water arithmetic kernels (spec 02 §4). Systems call these over their arrays.</summary>
    public static class WaterRules
    {
        public const long MaxTransferPerPulse = 100_000_000;
        public const long BarrierCrestMm = 600;
        public const long PumpNominalRate = 20_000_000;
        public const long PumpMaxLiftMm = 5000;

        /// <summary>Rate accumulator shared by sources, drains and sprinklers: injected = floor((rate+rem)/10).</summary>
        public static long RatePulse(long ratePerSecond, ref long remainder)
        {
            long n = checked(ratePerSecond + remainder);
            long injected = IntMath.FloorDiv(n, 10);
            remainder = IntMath.FloorMod(n, 10);
            return injected;
        }

        public static long Head(long elevationMm, long volume, long areaMm2) =>
            checked(elevationMm + IntMath.FloorDiv(volume, areaMm2));

        /// <summary>Passive request from the upstream end (caller orients up/down by head).</summary>
        public static long PassiveRequest(long headUp, long headDown, long baseSillMm, bool barrier,
                                          long conductance, long maxTransfer)
        {
            long sill = baseSillMm + (barrier ? BarrierCrestMm : 0);
            long driving = Math.Max(0, headUp - Math.Max(headDown, sill));
            if (driving == 0) return 0;
            long req = IntMath.FloorDiv(checked(conductance * driving), 10);
            return Math.Min(Math.Min(maxTransfer, MaxTransferPerPulse), req);
        }

        /// <summary>Powered pump request with head derating and a fractional remainder.</summary>
        public static long PumpRequest(long sourceHead, long destinationHead, ref long remainder,
                                       long nominalRate = PumpNominalRate, long maxLift = PumpMaxLiftMm)
        {
            long lift = Math.Max(0, destinationHead - sourceHead);
            long factor = Math.Max(0, maxLift - lift);
            long n = checked(nominalRate * factor + remainder);
            long denom = checked(maxLift * 10);
            long req = IntMath.FloorDiv(n, denom);
            remainder = IntMath.FloorMod(n, denom);
            return Math.Min(req, MaxTransferPerPulse);
        }

        /// <summary>
        /// Largest-remainder allocation of <paramref name="available"/> across requests whose stable IDs are
        /// already in ascending ordinal order (index order == ID order). Grants written into <paramref name="grants"/>.
        /// </summary>
        public static void Allocate(long available, ReadOnlySpan<long> requests, Span<long> grants, Span<long> scratchRemainders)
        {
            long total = 0;
            for (int i = 0; i < requests.Length; i++) total = checked(total + requests[i]);
            if (total <= available)
            {
                requests.CopyTo(grants);
                return;
            }
            long granted = 0;
            for (int i = 0; i < requests.Length; i++)
            {
                long prod = checked(available * requests[i]);
                grants[i] = prod / total;
                scratchRemainders[i] = prod % total;
                granted += grants[i];
            }
            long left = available - granted;
            // Award one unit each in descending remainder order, ties by index (ID).
            while (left > 0)
            {
                int best = -1;
                for (int i = 0; i < requests.Length; i++)
                {
                    if (scratchRemainders[i] < 0 || requests[i] == 0) continue;
                    if (best < 0 || scratchRemainders[i] > scratchRemainders[best]) best = i;
                }
                if (best < 0) break;
                grants[best]++;
                scratchRemainders[best] = -1; // each request gets at most one extra unit
                left--;
            }
        }
    }
}
