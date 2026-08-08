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
        Drain,  // timed drain / installment (DRAIN:Дн=-0.3/s DUR:10y)
        Drift   // множитель ПАССИВНОГО дрейфа шкалы (DRIFT:Отн=x2 [DUR:10y]) — отрезок 0
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
        public int DurYears;       // DUR:Ny → active for N game-years from its start (0 = бессрочно, для Drift)

        /// <summary>
        /// СТОРОНА, на которой эффект применяется (отрезок 0). Колонка «Длительный эффект» одна на строку,
        /// а Δ у карточки две — ДА и НЕТ; после `MD01` («СВАДЬБА!» смягчает дрейф на ДА и УСКОРЯЕТ его на
        /// НЕТ) одной стороны стало мало. Формат: запись можно префиксовать <c>ДА:</c> или <c>НЕТ:</c>;
        /// БЕЗ префикса — ДА, как было всегда (обратная совместимость со всеми существующими строками).
        /// </summary>
        public bool OnNoSide;
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

        /// <summary>
        /// «Тон» (col 14, 2026-07-19 canon): the design agent's EXPLICIT host-tone tag
        /// (positive/risky/absurd/cautious) for cards where the Δ-heuristic misfires (кек-карты,
        /// соблазны). Empty/null when the CSV cell is blank → <see cref="HostVoice"/> falls back to
        /// its heuristic. Raw string here (parsed to <see cref="HostTone"/> in HostVoice); fatal/skip
        /// precedence and the named «Ведущий» line still override it.
        /// </summary>
        public string Tone;

        public IReadOnlyList<string> Flags = System.Array.Empty<string>();

        // ---- OPEN:* — «эта карточка ОТКРЫВАЕТ шкалу» ------------------------------------------------
        // Токены — те же сокращения шкал, что и в Δ-колонке (см. CardLoader.Abbrevs).
        public const string OpenMoney = "Дн";
        public const string OpenRelations = "Отн";
        public const string OpenEnergy = "Эн";
        public const string OpenChild = "Реб";

        /// <summary>
        /// Карточка несёт ЛЮБОЙ флаг <c>OPEN:*</c> — она «открывающая». Читается сэмплером: среди карточек
        /// одного возраста открывающие идут ПЕРВЫМИ, иначе чужая карточка того же года перешагнёт
        /// возрастной порог за неё и туториал шкалы встанет ДО вопроса, который её вводит (r3, п.8).
        /// </summary>
        public bool OpensAnyScale
        {
            get
            {
                var flags = Flags;
                if (flags == null) return false;
                for (int i = 0; i < flags.Count; i++)
                    if (flags[i].StartsWith("OPEN:", System.StringComparison.Ordinal)) return true;
                return false;
            }
        }

        /// <summary>
        /// Карточка помечена флагом <c>OPEN:{scale}</c>, т.е. по канону колоды именно ОНА открывает эту
        /// шкалу («ПЕРВАЯ ЛЮБОВЬ! Начать встречаться?» → `OPEN:Отн`).
        ///
        /// Механически шкалы открываются ПО ВОЗРАСТУ (<see cref="Game"/>), а не по флагу, и до 2026-08-07
        /// это давало ровно ту нелогичность, которую поймал живой плейтест: возраст догоняет карточку
        /// СРАЗУ при её выдаче, поэтому туториал шкалы вставал ПОВЕРХ ещё не отвеченного вопроса — сначала
        /// «вот тебе шкала отношений», и только потом «начать встречаться?». Флаг читается <see cref="Game"/>
        /// ровно для того, чтобы придержать открытие до ответа на СВОЮ карточку (порядок «встречаться →
        /// OPEN:Отн»), не трогая канон-возрасты.
        /// </summary>
        public bool Opens(string scale)
        {
            var flags = Flags;
            if (flags == null) return false;
            for (int i = 0; i < flags.Count; i++)
                if (flags[i].Length == 5 + scale.Length
                    && flags[i].StartsWith("OPEN:", System.StringComparison.Ordinal)
                    && flags[i].EndsWith(scale, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// <c>BREAK:Отн</c> (отрезок 0) — карточка РВЁТ отношения на ДА: партнёр уходит немедленно, шкала
        /// гаснет, дальше живёшь один. До 2026-08-08 разрыв был описан только прозой, а движку доставалась
        /// Δ «Отн −3» — на `CR06` «БРОСИТЬ ПАРТНЁРА ПРЯМО СЕЙЧАС! Немедленный развод» это двигало шкалу
        /// 55 → 52 и оставляло в зелёной зоне. Механика ухода партнёра в игре уже была (накопленные ~10 с
        /// в красной зоне); флаг просто вызывает её напрямую (<see cref="Game"/>).
        /// </summary>
        public bool BreaksRelationships;

        /// <summary>
        /// <c>BREAK:Отн</c> вместе с <c>DELAY(n)</c> — разрыв ОТЛОЖЕН на n игровых лет («Роман на стороне?»
        /// `RND05`: «через 2 года развод», ровно как обещает проза карточки). 0 — рвёт сразу.
        /// </summary>
        public int BreakDelayYears;

        /// <summary>
        /// <c>EXCL:&lt;ключ&gt;</c> (отрезок 0) — ВЗАИМОИСКЛЮЧАЮЩАЯ ветка. Как только карточка этой группы
        /// разрешилась в ДА, остальные карточки с тем же ключом в этом забеге не появляются: две ипотеки
        /// (`FC02` ранняя 25–29 и `MD04` поздняя 30–40) — одно и то же событие в двух отрезках, а ветка
        /// «детей не будет» (`EXCL:реб`) — равноценный путь, а не пустота. null — карточка не в группе.
        /// </summary>
        public string ExclusiveGroup;

        /// <summary>
        /// <c>PRENUP</c> (отрезок 0) — брачный договор: гасит штраф <see cref="Game.DivorceCost"/> при
        /// разводе. Разовый, на всю жизнь, ставится ответом ДА.
        /// </summary>
        public bool IsPrenup;

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
