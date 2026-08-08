namespace ThanksNoThanks
{
    /// <summary>
    /// КАНОН ОТРЕЗКА 0 — единая конверсия Δ (`docs/design-tasks/cards-script-00-deltas-and-prices.md` §2).
    ///
    /// Колонка Δ в `scenes.csv` — это КАЧЕСТВЕННАЯ шкала −3…+3 («мелочь, но видно» / «заметно, требует
    /// компенсации» / «меняет жизнь»), язык дизайнера, а НЕ сырые единицы. До 2026-08-08 движок применял её
    /// буквально (`Scales.cs:64`, `Game.ApplyCardDeltas`), и это делало эффекты декоративными: «Дн −2» на
    /// айфоне снимало 2 ₽ при доходе ~4 ₽/сек (полсекунды кручения), «Отн −3» на карточке «БРОСИТЬ ПАРТНЁРА»
    /// двигало шкалу 55 → 52 и оставляло в зелёной зоне. Живой плейтест основательницы: «можно купить айфон
    /// с нулём на счету», «не вижу, чтобы что-то сильно било по отношениям».
    ///
    /// Величины подобраны основательницей по ЯКОРЮ «айфон (`FC08`) = 50 ₽» так, чтобы один шаг Δ стоил
    /// сопоставимого времени отыгрыша руками на всех шкалах (50 ₽ ≈ 13 секунд кручения).
    ///
    /// ⚠ АБСОЛЮТНЫЕ ЗНАЧЕНИЯ НЕ КОНВЕРТИРУЮТСЯ. Та же колонка смешивает две системы счёта: у `CH01`
    /// «Здр +1» — качественный шаг, у `LT02` «Здр +40» и `LT08` «Здр → 80%» — настоящие проценты. Признак
    /// чисто арифметический и не требует новой колонки: шаг ≤ 3 по модулю — качественный, всё крупнее —
    /// абсолютное число, а форма «→ N» (<see cref="DeltaKind.Set"/>) абсолютна всегда.
    ///
    /// Чистая функция, ни состояния, ни движка — маппинг живёт на СЛОЕ ПРИМЕНЕНИЯ Δ (<see cref="Game"/>),
    /// а CSV остаётся в качественной шкале. Цены платных карточек в ₽ — отдельный канон
    /// (<see cref="Game.BlockPrices"/> / <see cref="Game.CardYesIncome"/>), они тоже не конвертируются.
    /// </summary>
    public static class DeltaScale
    {
        /// <summary>Максимальный по модулю КАЧЕСТВЕННЫЙ шаг. Всё, что крупнее, — абсолютное значение.</summary>
        public const int QualitativeMax = 3;

        // Индекс = |шаг| − 1. Таблица §2 дизайн-дока, дословно.
        private static readonly int[] MoneyTable = { 20, 50, 100 };   // ₽
        private static readonly int[] HealthTable = { 7, 15, 30 };    // п.п.
        private static readonly int[] EnergyTable = { 8, 18, 35 };    // п.п.
        // Отношения: ±1 = 5, ±2 = 12, ±3 = «выбивает из зоны 40–75». Зона 40…75 при старте 55, значит
        // «выбить» — это шаг, который из середины гарантированно уводит за любую из границ: 55 − 30 = 25
        // (ниже пола зоны) и 55 + 30 = 85 (выше потолка). Берём 30 — минимальное число, удовлетворяющее
        // формулировке дизайн-дока с обеих сторон. Тюнимо.
        private static readonly int[] RelationsTable = { 5, 12, 30 };
        // Ребёнок: дизайн-док таблицу для «Реб» не задаёт (шкала сигнальная, её значение выставляет сама
        // механика — 70 на открытии, ±4/−12 по звонкам). Берём таблицу отношений как ближайшую по смыслу,
        // чтобы у токена «Реб ±N» вообще был масштаб; на практике его перебивает механика звонков.
        private static readonly int[] ChildTable = { 5, 12, 30 };

        /// <summary>
        /// Шаг КАЧЕСТВЕННЫЙ (подлежит конверсии)? Форма «→ N» абсолютна всегда, ноль не шаг, |N| &gt; 3 —
        /// уже настоящие проценты/рубли, написанные автором карточки как точное число.
        /// </summary>
        public static bool IsQualitative(DeltaKind kind, int value)
            => kind != DeltaKind.Set
               && value != 0
               && value >= -QualitativeMax
               && value <= QualitativeMax;

        /// <summary>
        /// Развернуть шаг Δ в реальную величину своей шкалы. Знак сохраняется; для <see cref="DeltaKind.
        /// RandomPlusMinus"/> на входе МОДУЛЬ шага (± разыгрывается вызывающим), поэтому и на выходе модуль.
        /// Абсолютные значения возвращаются как есть.
        /// </summary>
        public static int Resolve(Scale scale, DeltaKind kind, int value)
        {
            if (!IsQualitative(kind, value)) return value;
            int step = value < 0 ? -value : value;
            int mapped = TableFor(scale)[step - 1];
            return value < 0 ? -mapped : mapped;
        }

        /// <summary>Развернуть Δ целиком, сохранив шкалу и вид.</summary>
        public static ScaleDelta Resolve(ScaleDelta d)
            => new ScaleDelta(d.Scale, d.Kind, Resolve(d.Scale, d.Kind, d.Value));

        private static int[] TableFor(Scale s) => s switch
        {
            Scale.Money => MoneyTable,
            Scale.Health => HealthTable,
            Scale.Energy => EnergyTable,
            Scale.Relationships => RelationsTable,
            Scale.Child => ChildTable,
            _ => RelationsTable,
        };
    }
}
