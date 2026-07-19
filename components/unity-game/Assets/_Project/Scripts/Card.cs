using System.Collections.Generic;

namespace ThanksNoThanks
{
    /// <summary>The five life scales. Only Health is displayed this increment; the rest are passive.</summary>
    public enum Scale { Health, Energy, Money, Relationships, Child }

    public enum DeltaKind
    {
        Add,             // Здр +1 / Эн −1
        RandomPlusMinus, // ±N  → randomly +N or −N
        Set              // → 80%  → set the scale to the value
    }

    /// <summary>A single scale change parsed from the Δ column of scenes.csv.</summary>
    public struct ScaleDelta
    {
        public Scale Scale;
        public DeltaKind Kind;
        public int Value;

        public ScaleDelta(Scale scale, DeltaKind kind, int value)
        {
            Scale = scale;
            Kind = kind;
            Value = value;
        }
    }

    public enum LongEffectKind
    {
        Mult,   // income multiplier (MULT:Дн=x2 FROM:25 / x1.5 / x5|0)
        Drain   // timed drain / installment (DRAIN:Дн=-0.3/s DUR:10y)
    }

    /// <summary>
    /// A durable effect parsed from the «Длительный эффект» column (col 13, 2026-07-18 canon).
    /// Applied on ДА. Only <see cref="Scale.Money"/> effects act this increment (income multipliers
    /// and installment drains); non-money entries (e.g. health MULT) parse but stay inert.
    /// </summary>
    public struct LongEffect
    {
        public LongEffectKind Kind;
        public Scale Scale;      // target scale (Дн = Money)

        // --- Mult ---
        public double MultValue; // ×2 / ×1.5 / ×5
        public bool RandomZero;  // "x5|0": coin → ×MultValue OR wipe money to 0 (RANDOM_OUTCOME)
        public int FromAge;      // FROM:n → multiplier active only once Age >= n (0 = immediately)

        // --- Drain ---
        public double DrainPerSec; // signed ₽/сек while active (e.g. -0.3)
        public int DurYears;       // DUR:Ny → active for N game-years from its start
    }

    /// <summary>
    /// One question card. Pure data — no engine references. Built by <see cref="CardLoader"/>
    /// from scenes.csv (the authoritative source of text/Δ/flags/necrolog lines).
    /// </summary>
    public sealed class Card
    {
        public string Id;
        public string Question;       // "Съесть жука?"
        public string When;           // raw "Когда" cell ("4–8", "20+", "любой", "свадьба +2")
        public int Age;               // assigned event-age (leading number on load; window pick after sampling)
        public int Order;             // source-row index, for stable ordering on age ties

        public IReadOnlyList<ScaleDelta> YesDeltas = System.Array.Empty<ScaleDelta>();
        public IReadOnlyList<ScaleDelta> NoDeltas = System.Array.Empty<ScaleDelta>();

        public string YesNecrolog;    // null when the CSV cell is "—"
        public string NoNecrolog;     // null when the CSV cell is "—"

        /// <summary>
        /// «Ведущий (ДА)/(НЕТ)» (cols 8/9): the host's NAMED reaction line for each side, shown in the
        /// speech bubble with priority over the tone pool. null/empty when the CSV cell is blank or «—»
        /// (then the driver falls back to a tone-pool line). Pure data — no reaction logic here.
        /// </summary>
        public string HostYes;
        public string HostNo;

        /// <summary>
        /// TIMELINE flag: a one-off life milestone (старт/работа/любовь/свадьба…). When such a card
        /// becomes current the driver announces its rubric banner (S4). Semantic marker only — the
        /// banner text lives in the driver's <c>HostContent</c>, keyed by <see cref="Id"/>.
        /// </summary>
        public bool IsTimeline;

        public IReadOnlyList<string> Flags = System.Array.Empty<string>();

        public bool IsNoCons;         // NOCONS — intro card, apply nothing / no necrolog line
        public bool IsRond;           // ROND   — droppable from the necrolog first when over the limit
        public bool YesIsFatal;       // FATAL  — choosing ДА ends the run immediately

        /// <summary>
        /// BLITZ — кризис-мысль (CR00–CR05). Маркер для кризис-режима; сами мысли Δ не несут — их
        /// «последствие» это давление и счётчик провалов (обрабатывается особым состоянием в <see cref="Game"/>).
        /// </summary>
        public bool IsBlitz;

        /// <summary>
        /// INVERT — импульс-карта кризиса (CR06–CR08): молчание/таймаут = ДА. Игрок должен АКТИВНО нажать
        /// «СПАСИБО, НЕ НАДО» (→), чтобы отказаться. Семантика инверсии живёт в кризис-состоянии <see cref="Game"/>.
        /// </summary>
        public bool IsInvert;

        /// <summary>
        /// BLOCK$ — карта доступна только при деньгах ≥ цены. Цена — в прозе (тюнинг-константы в
        /// <see cref="Game"/>), не в CSV. При нехватке денег карта выпадает затемнённой и пропускается
        /// без Δ и без строки некролога (мокап S10). Обрабатывается в <see cref="Game"/>.
        /// </summary>
        public bool IsBlockCost;

        /// <summary>
        /// «Длительный эффект» (col 13): множители дохода и рассрочки-дренажи. Применяются на ДА.
        /// Только денежные (Дн) действуют в этом инкременте; прочие парсятся, но инертны.
        /// </summary>
        public IReadOnlyList<LongEffect> LongEffects = System.Array.Empty<LongEffect>();

        /// <summary>
        /// FORCED — веха/объявление: карта показывается, но реального выбора нет. Такие карты
        /// НИКОГДА не пишут строк в некролог (канон; enforced structurally by <see cref="Game"/>).
        /// </summary>
        public bool IsForced;

        /// <summary>
        /// RANDOM_TRIGGER (или legacy «RANDOM») — вероятностное ВЫПАДЕНИЕ карты: появится ли она
        /// в забеге вообще. Ключ для вероятностной выборки в <see cref="DeckSampler"/>.
        /// </summary>
        public bool IsRandomTrigger;

        /// <summary>
        /// RANDOM_OUTCOME — случаен ИСХОД карты (уже реализован через ±N в Δ-колонке), НЕ выпадение.
        /// Механически no-op в этом инкременте; хранится для тестов/ясности (карта выбирается обычно).
        /// </summary>
        public bool IsRandomOutcome;
        public string FatalCause;     // cause phrase for the finale when this card is fatal
        public bool StartsAgeTimer;   // resolving this card (either answer) starts the age timer (I03)

        /// <summary>
        /// CHAIN gate: this card is only drawn if the card with this id resolved ДА earlier in
        /// the run (null = ungated). Set by <see cref="DeckSampler"/>, honored by <see cref="Game"/>.
        /// </summary>
        public string RequiresParentYes;

        /// <summary>
        /// DELAY(n)+FATAL: choosing ДА does NOT end the run immediately; instead the finale
        /// «за вами пришли» is scheduled for (this card's age + n) event-years (RND01 only).
        /// 0 = no delayed fatal. When &gt; 0, <see cref="YesIsFatal"/> stays false.
        /// </summary>
        public int DelayedFatalYears;

        public override string ToString() => $"{Id}@{Age} \"{Question}\"";
    }
}
