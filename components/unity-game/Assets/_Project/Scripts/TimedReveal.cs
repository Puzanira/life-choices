namespace ThanksNoThanks
{
    /// <summary>
    /// PURE, engine-free auto-hiding reveal with an INJECTED clock — the shared timing spine for both the
    /// host speech-bubble (~2s) and the rubric banner (~1.5s). <see cref="Show"/> makes it visible with a
    /// text and arms the countdown; <see cref="Advance"/> (driver Update / tests) ticks the clock and
    /// hides it once <see cref="Duration"/> elapses. Deterministic — never touches the wall clock, so the
    /// «auto-hide after N seconds» behaviour is unit-testable, mirroring <see cref="BreathRhythm"/>.
    /// </summary>
    public sealed class TimedReveal
    {
        /// <summary>Seconds the reveal stays visible after <see cref="Show"/> (tunable).</summary>
        public double Duration;

        public bool Visible { get; private set; }
        public string Text { get; private set; } = "";

        private double _remaining;

        public TimedReveal(double duration) { Duration = duration; }

        /// <summary>Show <paramref name="text"/> and (re)arm the countdown from <see cref="Duration"/>.</summary>
        public void Show(string text)
        {
            Text = text ?? "";
            Visible = true;
            _remaining = Duration;
        }

        /// <summary>Hide immediately (next card, restart, leaving play).</summary>
        public void Hide()
        {
            Visible = false;
            _remaining = 0;
        }

        /// <summary>
        /// Advance the injected clock by <paramref name="dt"/> seconds. Returns true ONLY on the step it
        /// auto-hides (so a caller can hand off «banner done → reveal the card»). No-op while hidden.
        /// </summary>
        public bool Advance(double dt)
        {
            if (!Visible) return false;
            _remaining -= dt;
            if (_remaining <= 0)
            {
                Visible = false;
                _remaining = 0;
                return true;
            }
            return false;
        }
    }
}
