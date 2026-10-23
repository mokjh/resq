namespace Resq.Game
{
    /// <summary>
    /// Comfort and accessibility switches the presentation reads (set from <see cref="Settings"/> by the game flow).
    /// They change how things look and where controls sit, never the simulation.
    /// </summary>
    public static class Comfort
    {
        /// <summary>No flicker, pulsing or shimmer; hazards keep their colours, so nothing important is hidden.</summary>
        public static bool ReducedEffects;
        /// <summary>Mirror the run controls to the left side of the board.</summary>
        public static bool LeftHanded;
    }
}
