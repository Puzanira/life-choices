using System;

namespace ThanksNoThanks
{
    /// <summary>
    /// PURE host-reaction picker for the speech bubble (engine-free, injected RNG). Given a resolved card
    /// and the chosen <see cref="AnswerSide"/>, returns the line to show — or <c>null</c> for «no bubble
    /// this time». Two stages, in priority order:
    ///
    ///  1. NAMED line — the card's «Ведущий (ДА)/(НЕТ)» cell (<see cref="Card.HostYes"/>/<see cref="Card.HostNo"/>)
    ///     for the chosen side, when non-empty. Named ALWAYS shows and beats the pool (canon). Not used on
    ///     a timeout (silence reads as «пропуск», not the coin-flipped side).
    ///  2. TONE POOL fallback — with probability <see cref="PoolChance"/>, a random line from the tone
    ///     pool for the choice's <see cref="HostTone"/>. Tuned so the OVERALL bubble rate lands ~30–40%.
    ///
    /// <see cref="Classify"/> is the single tunable tone heuristic (a documented PLACEHOLDER pending the
    /// optional CSV «Тон» column the design agent proposed): FATAL and timeout are exact; the rest is a
    /// rough read of the chosen side's Δ. Text and pools live in <see cref="HostContent"/>, never here.
    /// </summary>
    public sealed class HostVoice
    {
        // ---- tunables ----
        /// <summary>
        /// Chance a NON-named choice shows a pool line. 0.35 → seeded pool-bubble frequency ~35% (inside
        /// the 30–40% band). Named-line cards additionally always show, nudging the true overall a touch
        /// higher; named cards are a deck minority, so the combined rate stays in-band. #1 tunable.
        /// </summary>
        public double PoolChance = 0.35;

        /// <summary>Net Δ on the chosen side at/above which the tone reads «positive».</summary>
        public int PositiveNetThreshold = 2;
        /// <summary>Net Δ on the chosen side at/below which the tone reads «risky».</summary>
        public int RiskyNetThreshold = -2;

        private readonly Func<double> _rng;   // uniform in [0,1)

        /// <param name="rng">Uniform [0,1) source (e.g. <c>new System.Random(seed).NextDouble</c>).</param>
        public HostVoice(Func<double> rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>
        /// Pick the bubble line for a resolved card, or null for no bubble. Draws the RNG once for the
        /// show-gate and (only if showing) once more for which pool line — deterministic per seed.
        /// </summary>
        public string Pick(Card card, AnswerSide side)
        {
            if (card == null) return null;

            // 1) Named line for the chosen side (priority). Silence (timeout) skips named — it's «пропуск».
            if (side != AnswerSide.Timeout)
            {
                string named = side == AnswerSide.Yes ? card.HostYes : card.HostNo;
                if (!string.IsNullOrEmpty(named)) return named;
            }

            // 2) Tone-pool fallback, gated by PoolChance.
            if (_rng() >= PoolChance) return null;
            var tone = Classify(card, side);
            var lines = HostContent.Pool[tone];
            int i = (int)(_rng() * lines.Length);
            if (i < 0) i = 0;
            else if (i >= lines.Length) i = lines.Length - 1;
            return lines[i];
        }

        /// <summary>
        /// Single tunable tone heuristic (PLACEHOLDER — see class summary). Exact where it can be:
        /// timeout → Skip, ДА-into-FATAL → Fatal, ROND «кек» flavour → Absurd. Otherwise a rough read of
        /// the chosen side's Δ: any gamble (±N) / money-loss / health-hit / big net-negative → Risky;
        /// big net-positive → Positive; low-impact ДА → Positive (bold), low-impact НЕТ → Cautious.
        /// </summary>
        public HostTone Classify(Card card, AnswerSide side)
        {
            if (side == AnswerSide.Timeout) return HostTone.Skip;
            bool yes = side == AnswerSide.Yes;
            if (yes && card.YesIsFatal) return HostTone.Fatal;

            // ROND «кек»-карты — комедийная оценка, которой нет в Δ (кек и риск бывают одинаковы по Δ).
            if (card.IsRond) return HostTone.Absurd;

            var deltas = yes ? card.YesDeltas : card.NoDeltas;
            int net = 0;
            bool gamble = false, moneyLoss = false, healthHit = false;
            if (deltas != null)
            {
                foreach (var d in deltas)
                {
                    if (d.Kind == DeltaKind.RandomPlusMinus) { gamble = true; continue; } // ±N — азарт
                    if (d.Kind == DeltaKind.Set) continue;                                // абсолютный set — неоднозначно
                    net += d.Value;
                    if (d.Value < 0 && d.Scale == Scale.Money) moneyLoss = true;
                    if (d.Value < 0 && d.Scale == Scale.Health) healthHit = true;
                }
            }

            if (gamble || moneyLoss || healthHit || net <= RiskyNetThreshold) return HostTone.Risky;
            if (net >= PositiveNetThreshold) return HostTone.Positive;
            return yes ? HostTone.Positive : HostTone.Cautious;   // low-impact: смелое ДА / осторожное НЕТ
        }
    }
}
