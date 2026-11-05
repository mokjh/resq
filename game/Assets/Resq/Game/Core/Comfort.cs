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
        /// <summary>Text-size setting (1, 1.25, 1.5). The HUD, tool tray and run controls scale as a whole, so text
        /// and touch targets grow together and layouts never clip.</summary>
        public static float UiScale = 1f;
        /// <summary>Near-opaque panels behind all text (for bright rooms and low vision).</summary>
        public static bool HighContrast;
        /// <summary>Show a short line for meaningful sounds (warnings, rescues, collapses), for players who can't hear them.</summary>
        public static bool Captions = true;
    }
}
