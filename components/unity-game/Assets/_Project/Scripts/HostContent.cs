using System.Collections.Generic;

namespace ThanksNoThanks
{
    /// <summary>The host's fallback reaction tone when a card has no named line (see <see cref="HostVoice"/>).</summary>
    /// <summary>
    /// Тон запасной реплики Ведущего (см. <see cref="HostVoice"/>).
    /// <see cref="Debt"/> — не «оценка выбора», а РЕАКЦИЯ НА СОБЫТИЕ: счёт ушёл ниже нуля. Поэтому
    /// <see cref="HostVoice.Classify"/> его никогда не возвращает и тегом «Тон» в CSV он не ставится —
    /// пул дёргает драйвер по <see cref="Game.DebtEntered"/>.
    /// </summary>
    public enum HostTone { Positive, Risky, Absurd, Cautious, Fatal, Skip, Debt }

    /// <summary>
    /// FINAL host content, baked from <c>docs/new_concept/host-content.md</c> (commit 6ef28fc) — the
    /// design agent's canon, NOT the GAME_SPEC drafts. Tone: Цезарь Фликерман (восторженно, громко,
    /// чуть глумливо). The main table is <see cref="Pool"/> — the fallback speech-bubble lines per tone,
    /// used only when a card has no named «Ведущий (ДА)/(НЕТ)» line for the chosen side.
    /// Named CSV lines always win over the pool (that priority lives in <see cref="HostVoice"/>).
    /// Pure data — no engine references.
    ///
    /// ⚠ ВСЯ СЕРИЯ БАННЕРОВ-РУБРИК ВЕХ («СВЕТ! КАМЕРА! ЖИЗНЬ!», «ПОРА ЗАРАБАТЫВАТЬ!», «ПЕРВАЯ ЛЮБОВЬ!»,
    /// «ПЕРВАЯ УСТАЛОСТЬ!», «СВАДЬБА! ГОРЬКО!», «ПОПОЛНЕНИЕ!», «ВТОРАЯ МОЛОДОСТЬ!», «ПТЕНЦЫ УЛЕТЕЛИ!»)
    /// СНЯТА плейтестом основательницы 2026-08-05: «их нет в присланных макетах» — вместе с жёлтой
    /// плашкой и её блокирующим «баннер-битом». Вехи идут обычными карточками. Уцелели только ДВА
    /// объявления (<see cref="CrisisAnnounce"/> / <see cref="DepressionAnnounce"/>) — это не вехи-карточки,
    /// а смены состояния со своим оверлеем, и Ведущий произносит их В ОБЛАЧКЕ, которое осталось.
    /// Канон-таблица §1 в host-content.md помечена как снятая — не восстанавливать без основательницы.
    /// </summary>
    public static class HostContent
    {
        /// <summary>Кризис среднего возраста (CR00) — реплика Ведущего в облачке на входе в блиц.</summary>
        public const string CrisisAnnounce = "КРИЗИС СРЕДНЕГО ВОЗРАСТА! БЛИЦ!";

        /// <summary>Депрессия (CR09) — единственное объявление, где восторг Ведущего намеренно ломается.</summary>
        public const string DepressionAnnounce = "ТЁМНАЯ ПОЛОСА…";

        /// <summary>
        /// Ведущий's fast, mockingly-hurrying nag lines shouted over the blitz thoughts (crisis-content §2).
        /// The driver shows one per thought (indexed by thought number), keeping the pressure up.
        /// </summary>
        public static readonly string[] BlitzNags =
            { "Быстрее!", "Соберись!", "Не тормозим!", "Улыбаемся!", "Всё хорошо, правда?", "Держим лицо!" };

        /// <summary>Nag line for blitz thought <paramref name="number"/> (1-based), cycled over the pool.</summary>
        public static string BlitzNagFor(int number)
            => BlitzNags[((number - 1) % BlitzNags.Length + BlitzNags.Length) % BlitzNags.Length];

        /// <summary>
        /// Ведущий's muted/distorted depression mutterings (crisis-content §1) — short, low-energy lines of
        /// nudging encouragement, tone «подсева, без восторга». Shown one per successful catch as the colour
        /// returns. Cycled by <see cref="DepressionMutterFor"/>.
        /// </summary>
        public static readonly string[] DepressionMutterings =
            { "…ну же…", "…почти…", "…вот так…" };

        /// <summary>Muttering for catch number <paramref name="number"/> (1-based), cycled over the pool.</summary>
        public static string DepressionMutterFor(int number)
            => DepressionMutterings[((number - 1) % DepressionMutterings.Length + DepressionMutterings.Length) % DepressionMutterings.Length];

        /// <summary>S13 impulse-round warning: INVERT means silence accepts — press → to decline.</summary>
        public const string ImpulseInvertWarning = "МОЛЧАНИЕ = ДА!";
        /// <summary>S13 sub-line prompting the active decline (the highlighted «СПАСИБО, НЕ НАДО» → button).</summary>
        public const string ImpulseDeclinePrompt = "ЖМИ «СПАСИБО, НЕ НАДО» →";

        /// <summary>Tone → the 6 short (1–3 word) fallback lines. Verbatim from host-content.md §2.</summary>
        public static readonly IReadOnlyDictionary<HostTone, string[]> Pool = new Dictionary<HostTone, string[]>
        {
            { HostTone.Positive, new[] { "Красавчик!", "Вот это по-нашему!", "Уважаю!", "Браво!", "Умница!", "Вот это да!" } },
            { HostTone.Risky,    new[] { "А вот это зря…", "Ну-ну.", "Смело!", "Рисковый вы!", "На грани!", "Ох, держитесь…" } },
            { HostTone.Absurd,   new[] { "Да вы шутник!", "Оригинально…", "Гениально! (нет)", "Ну вы даёте!", "Это что было?", "Занятно…" } },
            { HostTone.Cautious, new[] { "И правильно!", "Скучно…", "Перестраховщик!", "Как благоразумно!", "Зевота…", "Тихоня!" } },
            { HostTone.Fatal,    new[] { "Ой.", "Спасибо за игру!", "Занавес!", "Ну вот и всё!", "Аплодисменты!", "…" } },
            { HostTone.Skip,     new[] { "Молчание — тоже ответ!", "Задумались? Бывает.", "Ау, вы тут?", "Время-время!", "Решили не решать!", "Тишина в студии!" } },
            // Уход счёта в минус (отрезок 0, §3.2 — «долг должен звучать, а не просто краснеть»).
            { HostTone.Debt,     new[] { "В долг! Красота!", "Живём один раз!", "Банк вас любит!", "Смело, но глупо!", "Ой, минус!", "Заплатите потом!" } },
        };

        /// <summary>Реплика про долг номер <paramref name="number"/> (1-based), по кругу пула.</summary>
        public static string DebtLineFor(int number)
        {
            var lines = Pool[HostTone.Debt];
            return lines[((number - 1) % lines.Length + lines.Length) % lines.Length];
        }
    }
}
