namespace LifeChoices
{
    /// <summary>
    /// The finale lookup tables from docs/mvp-content.md: scale-death causes,
    /// the life label (by highest scale), and who came to the funeral (by Люди).
    /// </summary>
    public static class DeathTables
    {
        /// <summary>Cause text for a scale that hit a lethal boundary.</summary>
        public static string BrokenCause(Scale s, bool high) => (s, high) switch
        {
            (Scale.Mood, false) => "Умер от скуки, так и не начав жить.",
            (Scale.Mood, true) => "Сгорел на кутеже — «жил ярко».",
            (Scale.Health, false) => "Здоровье кончилось раньше жизни.",
            (Scale.Health, true) => "Помешался на ЗОЖ, умер от стерильности.",
            (Scale.Money, false) => "Нищета: отключили отопление.",
            (Scale.Money, true) => "Пришла налоговая — наследники не дождались.",
            (Scale.People, false) => "Умер в одиночестве, нашли через неделю.",
            (Scale.People, true) => "Задушили заботой и вниманием.",
            _ => "Причина неизвестна."
        };

        // Life label is "прожил как все" unless some scale clearly dominates.
        public const int LabelDominanceThreshold = 60;

        public static string Label(ScaleState scales)
        {
            Scale top = scales.Highest(out int value);
            if (value < LabelDominanceThreshold) return "Прожил как все";
            return top switch
            {
                Scale.Mood => "Прожигатель жизни",
                Scale.Money => "Успешный успех™",
                Scale.People => "Душа компании",
                Scale.Health => "Биохакер-долгожитель",
                _ => "Прожил как все"
            };
        }

        // Funeral turnout bands on the Люди scale.
        public const int FuneralHigh = 67;
        public const int FuneralLow = 33;

        public static string Funeral(int people)
        {
            if (people >= FuneralHigh) return "Пришли все, даже бывшие и налоговая.";
            if (people <= FuneralLow) return "Пришли двое: нотариус и кот.";
            return "Пришли родственники и пара коллег.";
        }
    }
}
