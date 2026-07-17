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
        public int Age;               // parsed leading number from the "Когда" column
        public int Order;             // source-row index, for stable ordering on age ties

        public IReadOnlyList<ScaleDelta> YesDeltas = System.Array.Empty<ScaleDelta>();
        public IReadOnlyList<ScaleDelta> NoDeltas = System.Array.Empty<ScaleDelta>();

        public string YesNecrolog;    // null when the CSV cell is "—"
        public string NoNecrolog;     // null when the CSV cell is "—"

        public IReadOnlyList<string> Flags = System.Array.Empty<string>();

        public bool IsNoCons;         // NOCONS — intro card, apply nothing / no necrolog line
        public bool IsRond;           // ROND   — droppable from the necrolog first when over the limit
        public bool YesIsFatal;       // FATAL  — choosing ДА ends the run immediately
        public string FatalCause;     // cause phrase for the finale when this card is fatal
        public bool StartsAgeTimer;   // resolving this card (either answer) starts the age timer (I03)

        public override string ToString() => $"{Id}@{Age} \"{Question}\"";
    }
}
