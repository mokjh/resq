using System;

namespace Resq.Sim.Rules
{
    /// <summary>Equipment thresholds (spec 02 §5 table).</summary>
    public static class EquipmentRules
    {
        public const int Durability = 100;
        public const long BarrierWarnHeadMm = 600;
        public const long BarrierDamageHeadMm = 800;
        public const long BridgeCloseDepthMm = 100;
        public const long BridgeDamageDepthMm = 500;
        public const long GeneratorShutdownDepthMm = 50;
        public const long GeneratorRestartDepthMm = 40;
        public const int GeneratorSupply = 2;
        public const long ElectronicDamageDepthMm = 200;
        public const int ElectronicDamageHeat = 600;

        public static bool BarrierWarning(long headDifference) => Math.Abs(headDifference) >= BarrierWarnHeadMm;

        /// <summary>Lose one durability point per pulse while |difference| exceeds 800 mm. Returns true when broken now.</summary>
        public static bool BarrierStrain(long headDifference, ref int durability)
        {
            if (durability <= 0) return false;
            if (Math.Abs(headDifference) > BarrierDamageHeadMm)
            {
                durability--;
                return durability == 0;
            }
            return false;
        }
    }

    /// <summary>Temporal objective helpers (spec 02 §11).</summary>
    public static class TemporalRules
    {
        /// <summary>ever_before: latch must occur strictly before the closing milestone (closingTick &lt; 0 = never closed).</summary>
        public static bool EverBeforePasses(long firstTrueOperationTick, long closingMilestoneOperationTick)
        {
            if (firstTrueOperationTick < 0) return false;
            if (closingMilestoneOperationTick < 0) return true;
            return firstTrueOperationTick < closingMilestoneOperationTick;
        }
    }
}
