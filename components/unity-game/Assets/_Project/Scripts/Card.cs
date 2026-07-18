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

        public IReadOnlyList<string> Flags = System.Array.Empty<string>();

        public bool IsNoCons;         // NOCONS — intro card, apply nothing / no necrolog line
        public bool IsRond;           // ROND   — droppable from the necrolog first when over the limit
        public bool YesIsFatal;       // FATAL  — choosing ДА ends the run immediately

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
