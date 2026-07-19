using System.Collections.Generic;

namespace ThanksNoThanks
{
    /// <summary>The host's fallback reaction tone when a card has no named line (see <see cref="HostVoice"/>).</summary>
    public enum HostTone { Positive, Risky, Absurd, Cautious, Fatal, Skip }

    /// <summary>
    /// FINAL host content, baked from <c>docs/new_concept/host-content.md</c> (commit 6ef28fc) — the
    /// design agent's canon, NOT the GAME_SPEC drafts. Tone: Цезарь Фликерман (восторженно, громко,
    /// чуть глумливо). Two tables:
    ///  • <see cref="Banners"/> — S4 rubric captions keyed by TIMELINE card id (big caps, host tone);
    ///  • <see cref="Pool"/> — the fallback speech-bubble lines per tone (used only when a card has no
    ///    named «Ведущий (ДА)/(НЕТ)» line for the chosen side).
    /// Named CSV lines always win over the pool (that priority lives in <see cref="HostVoice"/>).
    /// Pure data — no engine references.
    /// </summary>
    public static class HostContent
    {
        /// <summary>
        /// Rubric banner captions by TIMELINE card id. Only I03/YA01/YA03/YA05/MD01 are in THIS
        /// increment's playable pool; the rest (later increments) are baked too — harmless. CR09 is
        /// deliberately un-celebratory (the driver styles its banner muted).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Banners = new Dictionary<string, string>
        {
            { "I03",  "СВЕТ! КАМЕРА! ЖИЗНЬ!" },
            { "YA01", "ПОРА ЗАРАБАТЫВАТЬ!" },
            { "YA03", "ПЕРВАЯ ЛЮБОВЬ!" },
            { "YA05", "ПЕРВАЯ УСТАЛОСТЬ!" },
            { "MD01", "СВАДЬБА! ГОРЬКО!" },
            { "MD02", "ПОПОЛНЕНИЕ!" },
            { "CR00", "КРИЗИС СРЕДНЕГО ВОЗРАСТА! БЛИЦ!" },
            { "MD06", "ВТОРАЯ МОЛОДОСТЬ!" },
            { "CR09", "ТЁМНАЯ ПОЛОСА…" },        // muted / sarcastic — styled dim by the driver
            { "LT04", "ПТЕНЦЫ УЛЕТЕЛИ!" },
        };

        /// <summary>Card id whose banner is intentionally un-celebratory (dim styling in the driver).</summary>
        public const string MutedBannerId = "CR09";

        /// <summary>
        /// Ведущий's fast, mockingly-hurrying nag lines shouted over the blitz thoughts (crisis-content §2).
        /// The driver shows one per thought (indexed by thought number), keeping the pressure up.
        /// </summary>
        public static readonly string[] BlitzNags =
            { "Быстрее!", "Соберись!", "Не тормозим!", "Улыбаемся!", "Всё хорошо, правда?", "Держим лицо!" };

        /// <summary>Nag line for blitz thought <paramref name="number"/> (1-based), cycled over the pool.</summary>
        public static string BlitzNagFor(int number)
            => BlitzNags[((number - 1) % BlitzNags.Length + BlitzNags.Length) % BlitzNags.Length];

        /// <summary>S13 impulse-round warning: INVERT means silence accepts — press → to decline.</summary>
        public const string ImpulseInvertWarning = "МОЛЧАНИЕ = ДА!";
        /// <summary>S13 sub-line prompting the active decline (the highlighted «СПАСИБО, НЕ НАДО» → button).</summary>
        public const string ImpulseDeclinePrompt = "ЖМИ «СПАСИБО, НЕ НАДО» →";

        /// <summary>Fallback banner for any TIMELINE card without a named rubric (none in the current deck).</summary>
        public const string GenericBanner = "НОВАЯ ВЕХА!";

        /// <summary>Banner caption for a card id, or the generic fallback.</summary>
        public static string BannerFor(string id)
            => id != null && Banners.TryGetValue(id, out var b) ? b : GenericBanner;

        /// <summary>Tone → the 6 short (1–3 word) fallback lines. Verbatim from host-content.md §2.</summary>
        public static readonly IReadOnlyDictionary<HostTone, string[]> Pool = new Dictionary<HostTone, string[]>
        {
            { HostTone.Positive, new[] { "Красавчик!", "Вот это по-нашему!", "Уважаю!", "Браво!", "Умница!", "Вот это да!" } },
            { HostTone.Risky,    new[] { "А вот это зря…", "Ну-ну.", "Смело!", "Рисковый вы!", "На грани!", "Ох, держитесь…" } },
            { HostTone.Absurd,   new[] { "Да вы шутник!", "Оригинально…", "Гениально! (нет)", "Ну вы даёте!", "Это что было?", "Занятно…" } },
            { HostTone.Cautious, new[] { "И правильно!", "Скучно…", "Перестраховщик!", "Как благоразумно!", "Зевота…", "Тихоня!" } },
            { HostTone.Fatal,    new[] { "Ой.", "Спасибо за игру!", "Занавес!", "Ну вот и всё!", "Аплодисменты!", "…" } },
            { HostTone.Skip,     new[] { "Молчание — тоже ответ!", "Задумались? Бывает.", "Ау, вы тут?", "Время-время!", "Решили не решать!", "Тишина в студии!" } },
        };
    }
}
