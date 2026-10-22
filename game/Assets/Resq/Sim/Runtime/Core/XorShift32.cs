namespace Resq.Sim
{
    /// <summary>Deterministic PRNG (spec fixture RP01). State persists in snapshots; draws are counted.</summary>
    public struct XorShift32
    {
        public uint State;
        public long Draws;

        public XorShift32(uint seed)
        {
            State = seed == 0 ? 1u : seed;
            Draws = 0;
        }

        public uint Next()
        {
            uint x = State;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            State = x;
            Draws++;
            return x;
        }
    }
}
