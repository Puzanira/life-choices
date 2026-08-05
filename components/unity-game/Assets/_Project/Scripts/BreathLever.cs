namespace ThanksNoThanks
{
    /// <summary>
    /// PURE edge detector for the breathing lever: turns the height sensor's continuous 0..1 value into
    /// one «вдох» pulse per UP-stroke through mid-travel. Rising past <see cref="High"/> fires once and
    /// disarms; the lever must come back down to <see cref="Low"/> before the next stroke can fire, so
    /// parking a raised hand (or a stuck key) cannot machine-gun pulses.
    ///
    /// Extracted out of <see cref="ArcadeInputSource"/> (which owns Unity plumbing and cannot be stepped
    /// deterministically) so the WHOLE keyboard path — package HeightSimulator → this → BreathRhythm →
    /// the §D tutorial's exit condition — can be simulated at human tempos in an EditMode test. That sim
    /// is the regression guard for the founder's «туториал энергии непроходим» (2026-08-05).
    /// </summary>
    public sealed class BreathLever
    {
        // Mid-travel thresholds, hysteresis pair. These describe the PHYSICAL gesture (hand rises past
        // the middle of the sensor's travel, then falls back) and are deliberately NOT what was retuned
        // on 2026-08-05 — the keyboard emulation was brought up to meet them instead, so the cabinet
        // lever keeps the behaviour the founder already signed off on.
        public const float High = 0.5f;
        public const float Low = 0.35f;

        private float _prev;
        private bool _armed = true;

        /// <summary>True while the lever is low enough that the next up-stroke will register.</summary>
        public bool Armed => _armed;

        /// <summary>Feed the current sensor value; returns true on the frame an up-stroke crosses mid-travel.</summary>
        public bool Step(float value)
        {
            bool pulse = false;
            if (_armed && _prev < High && value >= High)
            {
                pulse = true;
                _armed = false;
            }
            else if (!_armed && value <= Low)
            {
                _armed = true;   // lever came back down → ready for the next breath
            }
            _prev = value;
            return pulse;
        }

        public void Reset()
        {
            _prev = 0f;
            _armed = true;
        }
    }
}
