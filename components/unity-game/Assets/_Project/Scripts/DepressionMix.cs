using UnityEngine;

namespace ThanksNoThanks
{
    /// <summary>
    /// ★ ГЛАВНЫЙ ХУДОЖЕСТВЕННЫЙ ПРИЁМ — «микс в вате». Не набор ассетов, а ОБРАБОТКА мастер-шины:
    /// на входе в депрессию весь микс (включая музыку) уходит под жёсткий лоу-пасс, и каждое из
    /// <see cref="Steps"/> попаданий пульса снимает фильтр на ступень — «мир включают обратно».
    ///
    /// Таблица утверждена основательницей НА СЛУХ по демо (manifest.md, §«Спека главного приёма»):
    ///
    /// <code>
    /// ступень 0 (вход)  500 Гц   −9 дБ
    /// ступень 1         900 Гц   −7 дБ
    /// ступень 2        1700 Гц   −5 дБ
    /// ступень 3        3200 Гц   −3 дБ
    /// ступень 4        6000 Гц   −1 дБ
    /// ступень 5 (выход) фильтр снят, 0 дБ
    /// </code>
    ///
    /// Чистая логика без сцены: <see cref="AudioLayer"/> только развозит числа по источникам, а
    /// ступени/громкости проверяются EditMode-тестом. Пульс депрессии этой обработке НЕ подлежит —
    /// он играется мимо фильтруемых каналов (<see cref="SoundBus.Unfiltered"/>).
    /// </summary>
    public static class DepressionMix
    {
        /// <summary>Пять попаданий возвращают мир целиком (совпадает с <see cref="Game.DepressionGraySteps"/>).</summary>
        public const int Steps = 5;

        /// <summary>«Фильтр снят»: потолок Unity-лоу-пасса, слышимо прозрачный.</summary>
        public const float CutoffOff = 22000f;

        /// <summary>2 каскада по 2 полюса — как в спеке: один AudioLowPassFilter даёт 2 полюса.</summary>
        public const int Cascades = 2;

        // Индекс = число ПОПАДАНИЙ (0 = только что вошли, 5 = вышли).
        private static readonly float[] Cutoffs = { 500f, 900f, 1700f, 3200f, 6000f, CutoffOff };
        private static readonly float[] Decibels = { -9f, -7f, -5f, -3f, -1f, 0f };

        private static int Clamp(int hits) => hits < 0 ? 0 : (hits > Steps ? Steps : hits);

        /// <summary>Частота среза для данного числа попаданий.</summary>
        public static float CutoffFor(int hits) => Cutoffs[Clamp(hits)];

        /// <summary>Ослабление мастера в децибелах (спека) для данного числа попаданий.</summary>
        public static float DecibelsFor(int hits) => Decibels[Clamp(hits)];

        /// <summary>То же ослабление ЛИНЕЙНЫМ множителем — в таком виде его ест AudioSource.volume.</summary>
        public static float GainFor(int hits) => Mathf.Pow(10f, DecibelsFor(hits) / 20f);

        /// <summary>Ступень снята полностью (фильтровать нечего) — ровно на выходе.</summary>
        public static bool Transparent(int hits) => Clamp(hits) >= Steps;

        /// <summary>
        /// <see cref="Game.DepressionGray"/> считает ОСТАВШУЮСЯ серость (5 → 0), спека же считает
        /// НАБРАННЫЕ попадания (0 → 5). Один переводчик, чтобы направление не путалось по коду.
        /// </summary>
        public static int HitsFromGray(int gray) => Clamp(Steps - gray);
    }
}
