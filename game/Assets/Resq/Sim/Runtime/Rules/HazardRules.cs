using System;

namespace Resq.Sim.Rules
{
    public enum FireState : byte { Unburned, Heating, Burning, Cooling, SafeAsh }

    public enum LavaState : byte { Dormant, Hot, Cooling, Crusted, Reheating }

    /// <summary>Clock arithmetic (spec 02 §2).</summary>
    public static class ClockRules
    {
        /// <summary>Advance one operation step. Returns true when this step carries a hazard pulse.</summary>
        public static bool Step(ref long operationTick, ref long scenarioTick, ref int accumulator, int scenarioNumerator, bool enabled)
        {
            operationTick++;
            if (!enabled) return false;
            accumulator += scenarioNumerator;
            if (accumulator >= 2)
            {
                accumulator -= 2;
                scenarioTick++;
                return true;
            }
            return false;
        }
    }

    /// <summary>Fire cell update (spec 02 §7).</summary>
    public static class FireRules
    {
        public const int IgnitionWarningTicks = 100;
        public const int CoolingMinimumTicks = 100;

        public static int IncomingHeat(int downwindBurning, int upwindBurning, int otherBurning) =>
            12 * downwindBurning + 4 * upwindBurning + 8 * otherBurning;

        /// <summary>Wetness gain from spray (1 point per 25,000 mm³ carried) then decay of 1 point.</summary>
        public static int UpdateWetness(int wetness, long sprayMm3, ref long sprayRemainder)
        {
            long n = checked(sprayMm3 + sprayRemainder);
            long gain = n / 25_000;
            sprayRemainder = n % 25_000;
            long w = wetness + gain;
            w = Math.Max(0, w - 1);
            return (int)Math.Min(1000, w);
        }

        public static int HeatLoss(int wetness, int extraCooling) => 2 + wetness / 100 + extraCooling;

        public static int NextHeat(FireState oldState, int heat, int fuel, int wetness, int incoming, int extraCooling)
        {
            int self = (oldState == FireState.Burning && fuel > 0 && wetness < 600) ? 25 : 0;
            int next = heat + incoming + self - HeatLoss(wetness, extraCooling);
            return Math.Max(0, Math.Min(1000, next));
        }

        public static int NextFuel(FireState oldState, int fuel, int wetness) =>
            Math.Max(0, fuel - (oldState == FireState.Burning && wetness < 600 ? 3 : 0));
    }

    /// <summary>Structure stability (spec 02 §8).</summary>
    public static class StabilityRules
    {
        public const int BraceBonus = 200;

        public static int Effective(int baseStability, int staticLoad, int intactBraces) =>
            Math.Max(0, Math.Min(1000, baseStability - staticLoad + BraceBonus * intactBraces));

        /// <summary>Per-pulse loss: per-second rate accumulated with denominator 10.</summary>
        public static int PulseLoss(int lossPerSecond, int staticLoad, int residentLoad, ref int remainder)
        {
            int total = TotalLossPerSecond(lossPerSecond, staticLoad, residentLoad);
            int n = total + remainder;
            remainder = n % 10;
            return n / 10;
        }

        public static int TotalLossPerSecond(int lossPerSecond, int staticLoad, int residentLoad) =>
            lossPerSecond + (staticLoad + 10 * residentLoad) / 100;
    }

    /// <summary>Tide controllers (spec 02 §9).</summary>
    public static class TideRules
    {
        public static readonly long[] SegmentTargets = { 0, 600, 1000, 800 };
        public const int RampTicks = 100;

        public static long Interpolate(long x0, long y0, long x1, long y1, long s) =>
            y0 + IntMath.FloorDiv(checked((y1 - y0) * (s - x0)), x1 - x0);

        public static long RampLevel(long startTick, long startLevel, long endTick, long endLevel, long s)
        {
            if (s >= endTick || endTick == startTick) return endLevel;
            if (s <= startTick) return startLevel;
            return Interpolate(startTick, startLevel, endTick, endLevel, s);
        }
    }

    /// <summary>Lava cooling timeline (spec 02 §10).</summary>
    public static class LavaRules
    {
        public const int CoolingTicks = 50;
        public const int CrustTicks = 200;
        public const int ReheatTicks = 50;

        /// <summary>State of a segment cooled at <paramref name="activationTick"/>, sampled at <paramref name="s"/>.</summary>
        public static LavaState StateAfterCooling(long activationTick, long s)
        {
            long cooled = activationTick + CoolingTicks;
            long expiry = cooled + CrustTicks;
            long hot = expiry + ReheatTicks;
            if (s < cooled) return LavaState.Cooling;
            if (s < expiry) return LavaState.Crusted;
            if (s < hot) return LavaState.Reheating;
            return LavaState.Hot;
        }

        public static bool CrossingOpen(LavaState state) => state == LavaState.Crusted || state == LavaState.Dormant;
    }

    /// <summary>Temporary shelter protection (spec 02 §10).</summary>
    public static class ShelterRules
    {
        public const int InitialProtection = 600;

        /// <summary>Consume one unit for a hazardous occupied pulse. Returns whether this interval is protected.</summary>
        public static bool Pulse(ref int remaining, bool occupied, bool hazardous, ref int exhaustions)
        {
            if (!occupied || !hazardous) return false;
            if (remaining <= 0) return false;
            remaining--;
            if (remaining == 0) exhaustions++;
            return true;
        }
    }
}
