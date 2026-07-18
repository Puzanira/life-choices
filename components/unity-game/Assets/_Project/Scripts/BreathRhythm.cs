namespace ThanksNoThanks
{
    /// <summary>
    /// PURE rhythm validator for the «дыхание» energy-restore gesture (ENERGY_PULSE / keyboard E,
    /// later a physical breathing lever). Deterministic, engine-free, with an injectable clock —
    /// advanced explicitly via <see cref="Advance"/> (driver Update / tests), never the wall clock,
    /// mirroring <see cref="MoneyCrankAutoRepeat"/>.
    ///
    /// A breath is «valid» only when it lands in a calm cadence: the interval since the previous
    /// pulse must fall inside [<see cref="RhythmMin"/>, <see cref="RhythmMax"/>] seconds. Машинг
    /// (intervals below the floor) and too-sparse taps (above the ceiling) return <c>false</c> and
    /// yield nothing — only a steady rhythm restores energy. The very first pulse of a life (or after
    /// a reset) has no predecessor and merely seeds the cadence (returns <c>false</c>).
    /// The driver forwards ONLY valid pulses to <see cref="Game"/>, so the logic stays semantic-only.
    /// </summary>
    public sealed class BreathRhythm
    {
        // Tunable rhythm window (seconds between consecutive breaths). Defaults per increment §Энергия.
        public double RhythmMin = 0.4;
        public double RhythmMax = 1.5;

        // Boundary tolerance: a clock accumulated over float frames lands a hair off an exact interval
        // (e.g. 1.0 + 0.4 rounds just under 0.4). Treat the window as closed within this epsilon so a
        // breath sitting exactly on a boundary still counts; it is far smaller than any real cadence gap.
        private const double Eps = 1e-6;

        private double _now;
        private double _lastPulse = double.NegativeInfinity;

        public void Reset()
        {
            _now = 0;
            _lastPulse = double.NegativeInfinity;
        }

        /// <summary>Advance the injected clock by <paramref name="dt"/> seconds (driver Update / tests).</summary>
        public void Advance(double dt) => _now += dt;

        /// <summary>
        /// Register a breath pulse at the current clock. Returns true iff it completes a VALID cycle —
        /// i.e. the gap since the previous pulse is within [<see cref="RhythmMin"/>, <see cref="RhythmMax"/>].
        /// Always re-anchors the cadence to now (so a mash streak stays a stream of too-short, invalid
        /// gaps, and a sparse tap re-seeds timing for the next attempt).
        /// </summary>
        public bool Pulse()
        {
            double interval = _now - _lastPulse;
            _lastPulse = _now;
            return interval >= RhythmMin - Eps && interval <= RhythmMax + Eps;
        }
    }
}
