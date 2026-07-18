namespace ThanksNoThanks
{
    /// <summary>What the crank repeater emitted this frame — the input layer keeps the two apart.</summary>
    public enum CrankEmit
    {
        None,
        Press,    // FRESH physical keydown → GameInput.MoneyTick (may confirm/dismiss outside gameplay)
        Repeat    // held-Space autorepeat  → GameInput.MoneyTickRepeat (income-only, inert on screens)
    }

    /// <summary>
    /// SOURCE-side crank repeater (deterministic, injectable clock). Held Space auto-repeats
    /// «focused cranking» at ~<see cref="AutoHz"/>/s; every DISCRETE keydown emits immediately and is
    /// NEVER swallowed — outside gameplay a fresh Space doubles as CONFIRM (driver re-map) and must be
    /// instant, while REPEATS stay inert there (holding must never skip screens or dismiss hints).
    /// Repeat scheduling is an accumulator (<c>_next += interval</c>) so a 60fps hold stays a true
    /// ~4/s instead of drifting to 16-frame intervals; a stall clamp stops catch-up bursts.
    /// The anti-mash income cap deliberately does NOT live here: it is applied by the driver on the
    /// gameplay-crank branch only (<see cref="MoneyTickThrottle"/>), so it can't eat a confirm/dismiss.
    /// </summary>
    public sealed class MoneyCrankAutoRepeat
    {
        public double AutoHz = 4.0;   // held-Space auto-repeat rate (tunable)

        private double Interval => 1.0 / AutoHz;

        private double _now;
        private double _next = double.PositiveInfinity;   // next scheduled repeat (accumulator)

        public void Reset()
        {
            _now = 0;
            _next = double.PositiveInfinity;
        }

        /// <summary>
        /// Advance the clock by <paramref name="dt"/> and report what to emit this frame.
        /// A fresh keydown always emits <see cref="CrankEmit.Press"/> and re-phases the schedule;
        /// holding emits <see cref="CrankEmit.Repeat"/> once every 1/<see cref="AutoHz"/> seconds.
        /// </summary>
        public CrankEmit Step(double dt, bool pressedThisFrame, bool held)
        {
            _now += dt;

            if (pressedThisFrame)
            {
                _next = _now + Interval;                 // re-phase off the fresh press
                return CrankEmit.Press;
            }
            if (!held)
            {
                _next = double.PositiveInfinity;         // released → no pending repeat
                return CrankEmit.None;
            }
            if (double.IsPositiveInfinity(_next))
                _next = _now + Interval;                 // held without a seen keydown (edge) → schedule
            if (_now >= _next)
            {
                _next += Interval;                       // accumulator: no per-emit re-anchor drift
                if (_now - _next >= Interval)            // stalled >1 interval behind → clamp, no burst
                    _next = _now + Interval;
                return CrankEmit.Repeat;
            }
            return CrankEmit.None;
        }
    }

    /// <summary>
    /// DRIVER-side INCOME cap: clamps accepted money ticks to ~<see cref="CapHz"/>/s (anti-mashgun;
    /// balance math assumes focused ≈4/s). Applied ONLY while actually cranking during gameplay —
    /// Space-as-CONFIRM (opener/finale/tutorial) never passes through it. Deterministic: the clock is
    /// advanced explicitly via <see cref="Advance"/> (driver Update / tests), never the wall clock.
    /// </summary>
    public sealed class MoneyTickThrottle
    {
        public double CapHz = 5.0;    // global income-tick ceiling (tunable)

        private double _now;
        private double _lastAccept = double.NegativeInfinity;

        public void Reset()
        {
            _now = 0;
            _lastAccept = double.NegativeInfinity;
        }

        public void Advance(double dt) => _now += dt;

        /// <summary>True if a crank event arriving now is inside the allowed rate; accepting re-arms the window.</summary>
        public bool TryAccept()
        {
            if (_now - _lastAccept < 1.0 / CapHz) return false;
            _lastAccept = _now;
            return true;
        }
    }
}
