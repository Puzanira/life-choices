namespace ThanksNoThanks
{
    /// <summary>Исход одного импульса дыхания на ритм-гейте.</summary>
    public enum BreathPulse
    {
        /// <summary>Первый вдох жизни (или после сброса): предшественника нет — он лишь задаёт каденцию.</summary>
        Seeded,
        /// <summary>Интервал вне окна — вдох НЕ принят (заколачивание или слишком редко).</summary>
        OffRhythm,
        /// <summary>Интервал в окне — вдох принят, энергия восстанавливается.</summary>
        Valid,
    }

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
        // ---- Tunable rhythm window (seconds between consecutive breaths) -----------------------------
        // ⚠ КАЛИБРОВКА 2026-08-05 (плейтест основательницы «жму — ничего не происходит»). Числа окна
        // согласованы со скоростями клавиатурной эмуляции датчика в конфиге пакета arcade-controls
        // (heightRise/Fall 1.25, heightReturn 2.0 ед/с) — менять их порознь нельзя, см. диагностику в
        // docs/increments/2026-08-05-playtest-fixes-r1.md §4(b):
        //   • НИЗ 0.4 c — это и есть защита «дыхание ритмом, а не заколачивание»: чаще = не вдох.
        //     На клавиатуре порог хода физически недостижим быстрее ~0.7 c, поэтому заколачивание там
        //     не даёт импульсов ВООБЩЕ; на живом рычаге его режет это число.
        //   • ВЕРХ 3.0 c (было 1.5) — 1.5 отсекало комфортный человеческий вдох-выдох 1.5–2.5 c: игрок
        //     дышал ровно, а игра молчала. Верх держит «размеренность»: реже раза в 3 с — уже не ритм.
        public double RhythmMin = 0.4;
        public double RhythmMax = 3.0;

        /// <summary>Каденция, которую игра НАЗЫВАЕТ игроку словами («раз в ~2 секунды»): середина окна,
        /// на которую откалиброван и туториал, и текст задачи энергии (host-content §4).</summary>
        public const double TargetSeconds = 2.0;

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
        public bool Pulse() => PulseDetailed() == BreathPulse.Valid;

        /// <summary>
        /// Same as <see cref="Pulse"/>, but tells the caller WHY a pulse yielded nothing. The UI needs the
        /// difference: an OFF-RHYTHM breath deserves a visible «не в ритм» nudge (плейтест 2026-08-05 §5),
        /// while the very FIRST breath of a life has no predecessor to be off-rhythm against — scolding
        /// the player for it would be a lie.
        /// </summary>
        public BreathPulse PulseDetailed()
        {
            bool seeding = double.IsNegativeInfinity(_lastPulse);
            double interval = _now - _lastPulse;
            _lastPulse = _now;
            if (seeding) return BreathPulse.Seeded;
            return interval >= RhythmMin - Eps && interval <= RhythmMax + Eps
                ? BreathPulse.Valid
                : BreathPulse.OffRhythm;
        }
    }
}
