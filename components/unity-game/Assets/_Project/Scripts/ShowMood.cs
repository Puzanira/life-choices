using System;

namespace ThanksNoThanks
{
    /// <summary>
    /// PURE «реакция шоу на состояние» (canon §Тон и обёртка): the TV show self-dims when the player's
    /// overall state sags and shines when everything is fine. This is the rough first step — a global
    /// screen-brightness veil driven by the mean of the live scales (health + energy this increment;
    /// relationships fold in once they go live). Engine-free and deterministic so the curve is testable
    /// (contract: brightness a monotonic pure function of state). The driver LERPS toward this target
    /// each frame so the veil never flickers.
    /// </summary>
    public static class ShowMood
    {
        /// <summary>Darkest the veil ever gets (state = 0). Tunable curve endpoint.</summary>
        public const double MaxDarkAlpha = 0.45;

        /// <summary>
        /// Target dark-veil alpha for a mean state in [0,100]: 0 at full (bright, «шоу сверкает») up to
        /// <see cref="MaxDarkAlpha"/> at empty («шоу тускнеет»). Monotonically NON-INCREASING in
        /// <paramref name="avgState"/> — more life ⇒ never darker. Clamped so out-of-range inputs are safe.
        /// </summary>
        public static double DarkAlpha(double avgState)
        {
            double t = avgState / 100.0;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            return MaxDarkAlpha * (1.0 - t);
        }

        /// <summary>Convenience: mean of the two live scales (0–100 each) → target dark-veil alpha.</summary>
        public static double DarkAlphaFor(int health, int energy)
            => DarkAlpha((Math.Max(0, health) + Math.Max(0, energy)) / 2.0);
    }
}
