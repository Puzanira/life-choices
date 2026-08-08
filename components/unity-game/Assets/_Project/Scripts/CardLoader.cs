using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ThanksNoThanks
{
    /// <summary>
    /// Tolerant loader for scenes.csv (the authoritative card source).
    /// Handles quoted commas, blank fields, unicode (−) AND ascii (-) minus, and
    /// non-contiguous IDs (I01/LT06/RND02 are absent — never assume a dense range).
    /// Unsupported flags (OPEN:*, CHAIN→, DELAY, BLOCK$, INVERT, BLITZ, ZONE) are parsed
    /// and ignored, never crash.
    ///
    /// Flag semantics honoured here (canon scenes.csv 2026-07-18):
    ///  • FORCED         → веха/объявление (no real choice); never contributes a necrolog line.
    ///  • RANDOM_TRIGGER → probabilistic INCLUSION marker (whether the card appears at all).
    ///  • RANDOM_OUTCOME → random OUTCOME marker (already carried by ±N deltas); mechanical no-op.
    ///  • legacy "RANDOM" → mapped to RANDOM_TRIGGER (backward compat with the old snapshot).
    ///
    /// The optional 14th column «Длительный эффект» (MULT/DRAIN/FROM/DUR grammar) and 15th column
    /// «Тон» (explicit host-tone tag) are tolerated both when present and when absent — flags always
    /// live in column 12, so trailing columns are read positionally and a shorter row yields null.
    /// </summary>
    public static class CardLoader
    {
        // The spine subset for this increment, in no particular order (sorted by age on load):
        // втягивающие интро, всё детство, и коherentный ранний-взрослый срез (работа/любовь/
        // город + фатальный соблазн). NOT contiguous by design.
        public static readonly string[] DefaultSubset =
        {
            "I02", "I03",
            "CH01", "CH02", "CH03", "CH04", "CH05", "CH06", "CH07", "CH08",
            "YA01", "RND03", "YA03", "YA06"
        };

        // Fatal-cause phrasing for the finale (GAME_SPEC §финал / screens S12).
        private static readonly Dictionary<string, string> FatalCauses = new()
        {
            { "CH02", "вы сунули палец в розетку" },
            { "RND03", "белый порошок" },
            { "RND06", "селфи на краю крыши" },
            { "RND01", "за вами пришли" },
            // отрезки 1–7
            { "CH11", "прыжок с гаража" },
            { "FB33", "чужая фирма" },
        };

        // --- Column indices in scenes.csv ---
        private const int ColId = 0;
        private const int ColQuestion = 1;
        private const int ColWhen = 2;
        // 3 = Тип, 4 = ДА-проза
        private const int ColYesDelta = 5;
        // 6 = НЕТ-проза
        private const int ColNoDelta = 7;
        private const int ColHostYes = 8;   // «Ведущий (ДА)» — named host reaction line for ДА
        private const int ColHostNo = 9;    // «Ведущий (НЕТ)» — named host reaction line for НЕТ
        private const int ColYesNecro = 10;
        private const int ColNoNecro = 11;
        private const int ColFlags = 12;
        private const int ColLong = 13;   // «Длительный эффект» (MULT/DRAIN/FROM/DUR), tolerant if absent
        private const int ColTone = 14;   // «Тон» — explicit host-tone tag, tolerant if absent (blank = null)

        public static List<Card> LoadSubset(string csv, IEnumerable<string> ids)
        {
            var wanted = new HashSet<string>(ids);
            var all = ParseAll(csv);
            var subset = new List<Card>();
            foreach (var c in all)
                if (wanted.Contains(c.Id))
                    subset.Add(c);

            // Draw in age order; stable on ties via source order.
            subset.Sort((a, b) => a.Age != b.Age ? a.Age.CompareTo(b.Age) : a.Order.CompareTo(b.Order));
            return subset;
        }

        public static List<Card> ParseAll(string csv)
        {
            var cards = new List<Card>();
            if (string.IsNullOrEmpty(csv)) return cards;

            var rows = Tokenize(csv);
            int order = 0;
            bool headerSkipped = false;

            foreach (var row in rows)
            {
                if (row.Count == 0) continue;
                string id = Field(row, ColId).Trim();
                if (string.IsNullOrEmpty(id)) continue;

                // Skip the header row (first data-bearing row whose ID isn't a real card id).
                if (!headerSkipped && string.Equals(id, "ID", StringComparison.OrdinalIgnoreCase))
                {
                    headerSkipped = true;
                    continue;
                }
                headerSkipped = true;

                var flags = ParseFlags(Field(row, ColFlags));
                var card = new Card
                {
                    Id = id,
                    Question = Field(row, ColQuestion).Trim(),
                    When = Field(row, ColWhen).Trim(),
                    Age = ParseAge(Field(row, ColWhen)),
                    Order = order++,
                    YesDeltas = ParseDeltas(Field(row, ColYesDelta)),
                    NoDeltas = ParseDeltas(Field(row, ColNoDelta)),
                    YesNecrolog = CleanNecrolog(Field(row, ColYesNecro)),
                    NoNecrolog = CleanNecrolog(Field(row, ColNoNecro)),
                    // Named host lines (cols 8/9). Same «— / blank → null» cleaning as necrolog cells, so
                    // an empty or dash cell falls back to the tone pool downstream. NAMED beats the pool.
                    HostYes = CleanNecrolog(Field(row, ColHostYes)),
                    HostNo = CleanNecrolog(Field(row, ColHostNo)),
                    // «Тон» (col 14): explicit host-tone tag. Same «— / blank → null» cleaning; a row
                    // with fewer columns (old snapshot) yields null → HostVoice keeps its heuristic.
                    Tone = CleanNecrolog(Field(row, ColTone)),
                    Flags = flags,
                };
                card.IsNoCons = flags.Contains("NOCONS");
                card.IsTimeline = flags.Contains("TIMELINE");   // one-off milestone → rubric banner (S4)
                card.IsRond = flags.Contains("ROND");
                card.IsForced = flags.Contains("FORCED");
                card.IsBlockCost = flags.Contains("BLOCK$");
                card.IsBlitz = flags.Contains("BLITZ");     // кризис-мысль (CR00–CR05)
                card.IsInvert = flags.Contains("INVERT");   // импульс-карта (CR06–CR08): молчание=ДА
                card.LongEffects = ParseLongEffects(Field(row, ColLong));
                // --- метки отрезка 0 (2026-08-08) ---
                // Сторона разрыва: «BREAK:Отн» (ДА, как было) либо «НЕТ:BREAK:Отн» (`MD24` — развод на НЕТ).
                card.BreaksRelationships = flags.Contains(BreakRelationsFlag)
                                           || flags.Contains(BreakRelationsOnNoFlag);
                card.BreakOnNoSide = !flags.Contains(BreakRelationsFlag)
                                     && flags.Contains(BreakRelationsOnNoFlag);
                // BREAK вместе с DELAY(n) — отложенный разрыв (RND05 «через 2 года развод»). DELAY читается
                // ровно тем же парсером, что и у FATAL, поэтому у карточек с DELAY БЕЗ BREAK/FATAL
                // (YA01/MD01/MD04/YA04 — прозаические пометки) ничего не меняется.
                card.BreakDelayYears = card.BreaksRelationships ? ParseDelayYears(flags) : 0;
                card.ExclusiveGroup = ParseExclusiveGroup(flags);
                card.IsPrenup = flags.Contains("PRENUP");
                // --- условия из колонки «Когда» (отрезки 1–7) -----------------------------------------
                // До отрезка 1 условия были прозой для человека, а гейты — хардкодом в DeckSampler
                // (словарь ChainParent на пять строк). Сорок новых карточек носят условие «если MD02=ДА» /
                // «если MD01=ДА», и держать их списком в коде значит гарантированно разойтись с CSV.
                // Теперь условие ЧИТАЕТСЯ оттуда, где его пишет дизайнер.
                ApplyWhenConditions(card);
                // Probabilistic inclusion keys on RANDOM_TRIGGER; legacy "RANDOM" means the same
                // (old snapshot). RANDOM_OUTCOME is a separate, mechanically-inert marker.
                card.IsRandomTrigger = flags.Contains("RANDOM_TRIGGER") || flags.Contains("RANDOM");
                card.IsRandomOutcome = flags.Contains("RANDOM_OUTCOME");
                bool hasFatal = flags.Contains("FATAL");
                int delayYears = ParseDelayYears(flags);
                // DELAY(n)+FATAL (RND01) is a *delayed* fatal: ДА doesn't end the run now, the
                // finale is scheduled for card.Age + n. Plain FATAL (розетка/порошок/селфи) is
                // immediate. Other DELAY-only flags stay no-ops (prose in the necrolog only).
                if (hasFatal && delayYears > 0)
                {
                    card.DelayedFatalYears = delayYears;
                    card.YesIsFatal = false;
                }
                else
                {
                    card.YesIsFatal = hasFatal;
                }
                // Canon (scenes.csv I03 row / GAME_SPEC core loop): "Сделать первый шаг?"
                // starts the age timer regardless of the answer ("шаг всё равно происходит").
                // Prose-only in the CSV (no machine flag), so mapped here by id.
                card.StartsAgeTimer = id == "I03";
                if (hasFatal)
                    card.FatalCause = FatalCauses.TryGetValue(id, out var cause) ? cause : "неведомая дичь";

                cards.Add(card);
            }
            return cards;
        }

        // --- CSV tokenizer (RFC-4180-ish: quotes, "" escapes, CRLF/LF) ---
        internal static List<List<string>> Tokenize(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool rowHasContent = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') { inQuotes = true; rowHasContent = true; }
                    else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); rowHasContent = true; }
                    else if (c == '\n' || c == '\r')
                    {
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                        row.Add(sb.ToString()); sb.Clear();
                        if (rowHasContent) rows.Add(row);
                        row = new List<string>();
                        rowHasContent = false;
                    }
                    else { sb.Append(c); rowHasContent = true; }
                }
            }
            if (rowHasContent || sb.Length > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row);
            }
            return rows;
        }

        private static string Field(List<string> row, int index)
            => index >= 0 && index < row.Count ? row[index] : string.Empty;

        // First run of digits in the "Когда" text; cards with no number (e.g. "любой",
        // "если ...") sort to the end without breaking the load.
        private static readonly Regex AgeRx = new(@"\d+", RegexOptions.Compiled);

        internal static int ParseAge(string when)
        {
            if (string.IsNullOrEmpty(when)) return int.MaxValue;
            var m = AgeRx.Match(when);
            return m.Success && int.TryParse(m.Value, out var n) ? n : int.MaxValue;
        }

        private static string CleanNecrolog(string cell)
        {
            var t = (cell ?? string.Empty).Trim();
            if (t.Length == 0 || t == "—" || t == "-" || t == "−") return null;
            return t;
        }

        // "DELAY(3)" → 3; absent → 0. Used only in combination with FATAL (RND01).
        private static readonly Regex DelayRx = new(@"DELAY\s*\(\s*(\d+)\s*\)", RegexOptions.Compiled);

        internal static int ParseDelayYears(IEnumerable<string> flags)
        {
            foreach (var f in flags)
            {
                var m = DelayRx.Match(f);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;
            }
            return 0;
        }

        /// <summary>
        /// <c>BREAK:Отн</c> — единственная форма разрыва, которая сейчас имеет смысл (рвутся ОТНОШЕНИЯ).
        /// Токен шкалы тот же, что в Δ-колонке (<see cref="Card.OpenRelations"/>), чтобы CSV говорил на
        /// одном языке сокращений.
        /// </summary>
        private const string BreakRelationsFlag = "BREAK:" + Card.OpenRelations;
        /// <summary>`MD24` рвёт брак на НЕТ («Развод»). Тот же префикс стороны, что в «Длительном эффекте».</summary>
        private const string BreakRelationsOnNoFlag = "НЕТ:" + BreakRelationsFlag;

        // ---- условия колонки «Когда» (отрезки 1–7) -----------------------------------------------------
        // Дизайнер пишет их прозой в той же ячейке, что и возраст: «30–39, если MD02=ДА», «28–32, до MD01»,
        // «свадьба +2», «18–19, если на счету < 5 ₽», «~40, если Отн потеряна». Возрастную часть читает
        // DeckSampler.AgeWindow; здесь читается всё остальное.
        //
        // ⚠ РАЗБОР ПОКЛАУЗНЫЙ, А НЕ ПОИСКОМ ПО ВСЕЙ ЯЧЕЙКЕ (находка ревю, MAJOR). Раньше каждая форма
        // искалась регуляркой по всей строке, и то, чего ни одна регулярка не знала, ПРОСТО НЕ
        // СУЩЕСТВОВАЛО: «если Отн открыта» на `MD01` не читалось никем, и свадьба приходила игроку без
        // отношений. Теперь ячейка режется на клаузы и КАЖДАЯ обязана быть узнана — что не узнано,
        // ложится в <see cref="Card.UnparsedWhen"/> и краснит валидатор колоды. Класс «молча
        // проигнорировано» на этом закрыт: новую форму нельзя завести в CSV, не заведя её здесь.
        private static readonly Regex WhenParentYesRx =
            new(@"^\s*(?:если\s+)?([A-Z]+\d+)\s*=\s*ДА\s*$", RegexOptions.Compiled);
        private static readonly Regex WhenBeforeRx =
            new(@"^\s*(?:вместо/)?до\s+([A-Z]+\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex WhenMarriageRx =
            new(@"^\s*свадьба\s*\+\s*(\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex WhenMoneyBelowRx =
            new(@"^\s*(?:если\s+)?на\s+счету\s*<\s*(\d+)\s*₽?\s*$", RegexOptions.Compiled);
        private static readonly Regex WhenMoneyAtLeastRx =
            new(@"^\s*(?:если\s+)?на\s+счету\s*(?:≥|>=)\s*(\d+)\s*₽?\s*$", RegexOptions.Compiled);
        private static readonly Regex WhenMoneyMinusRx =
            new(@"^\s*(?:если\s+)?на\s+счету\s+минус\s*$", RegexOptions.Compiled);
        // «если Отн открыта» / «если шкала отношений открыта» / «если Дн открыта».
        private static readonly Regex WhenScaleOpenRx =
            new(@"^\s*(?:если\s+)?(?:шкала\s+)?([А-Яа-яЁё]+)\s+открыта\s*$", RegexOptions.Compiled);
        private static readonly Regex WhenScaleLostRx =
            new(@"^\s*(?:если\s+)?(?:шкала\s+)?([А-Яа-яЁё]+)\s+потеряна\s*$", RegexOptions.Compiled);
        // «если Здр<50%» (LT02) / «когда здоровье < 40%» (LT08).
        private static readonly Regex WhenHealthBelowRx =
            new(@"^\s*(?:если|когда)?\s*(?:Здр|здоровье)\s*<\s*(\d+)\s*%?\s*$", RegexOptions.Compiled);
        // «(возраст ≥ 30)» — вторая половина условия LT08.
        private static readonly Regex WhenMinAgeRx =
            new(@"^\s*возраст\s*(?:≥|>=)\s*(\d+)\s*$", RegexOptions.Compiled);
        // Возрастное окно: «18–19», «20+», «18», «~40». Его читает DeckSampler.AgeWindow, здесь — только
        // признание формы, чтобы клауза не считалась нераспознанной.
        private static readonly Regex WhenAgeWindowRx =
            new(@"^\s*~?\s*\d+\s*(?:[–—\-−]\s*\d+|\+)?\s*$", RegexOptions.Compiled);
        // «после I02» — порядок внутри интро; держится возрастными окнами (I02 0–3, I03 1–4) и
        // проверяется валидатором (названная карточка существует и стоит не позже).
        private static readonly Regex WhenAfterRx =
            new(@"^\s*после\s+([A-Z]+\d+)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// ПРОЗА БЕЗ МЕХАНИКИ — клаузы, которые условием НЕ являются: они называют место карточки в
        /// сценарии, а её выдачу держит отдельная система (интро, кризис-блок, импульс-раунд), не колонка
        /// «Когда». Список ЗАКРЫТЫЙ и покрыт валидатором с двух сторон: незнакомая проза = ошибка, а
        /// запись, которой в CSV больше нет, = мёртвая строка и тоже ошибка (иначе список сгниёт).
        /// </summary>
        public static readonly string[] WhenProseNoCondition =
        {
            "старт игры",              // I02 — первая карточка забега, ставит её опенер
            "любой",                   // RND06 — окно во всю взрослую жизнь (AgeWindow)
            "блиц",                    // CR01…CR05 — блиц-блок кризиса, собирается DeckSampler.Crisis
            "импульс-раунд",           // CR06…CR08, CR10…CR12 — раунд импульсов кризиса
            "если ≥2 промаха",         // …его условие входа; считает Game по промахам блица
            "после кризиса",           // CR09 — депрессия, ставится после кризис-блока
            "рандом",                  // …и не гарантированно (Game решает броском)
        };

        /// <summary>Токен шкалы из прозы «Когда» → канонический токен колонки Δ (<see cref="Card.OpenRelations"/>
        /// и соседи). Дизайнер пишет и «Отн», и «шкала отношений» — движку нужен один язык.</summary>
        public static string NormalizeScaleToken(string word)
        {
            var w = (word ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
            if (w.StartsWith("отн")) return Card.OpenRelations;
            if (w.StartsWith("дн") || w.StartsWith("деньг")) return Card.OpenMoney;
            if (w.StartsWith("эн")) return Card.OpenEnergy;
            if (w.StartsWith("реб")) return Card.OpenChild;
            if (w.StartsWith("здр") || w.StartsWith("здоров")) return "Здр";
            return null;
        }

        // Ячейка режется по запятым и скобкам («когда здоровье < 40% (возраст ≥ 30)» — ДВА условия), а
        // затем по союзу «и» («если FA11 = ДА и на счету минус» — тоже два).
        private static readonly Regex ClauseSplitRx = new(@"[,()]", RegexOptions.Compiled);
        private static readonly Regex AndSplitRx = new(@"\s+и\s+", RegexOptions.Compiled);

        public static IEnumerable<string> SplitWhenClauses(string when)
        {
            foreach (var part in ClauseSplitRx.Split(when ?? string.Empty))
                foreach (var clause in AndSplitRx.Split(part))
                {
                    var t = clause.Trim();
                    if (t.Length > 0) yield return t;
                }
        }

        /// <summary>
        /// Прочитать условия из «Когда» в поля карточки. Каждая клауза обязана быть узнана; нераспознанные
        /// складываются в <see cref="Card.UnparsedWhen"/> — загрузка не падает (колонка человеческая), но
        /// валидатор колоды на них краснеет.
        /// </summary>
        public static void ApplyWhenConditions(Card card)
        {
            var when = card.When ?? string.Empty;
            if (when.Length == 0) return;

            foreach (var clause in SplitWhenClauses(when))
            {
                if (WhenAgeWindowRx.IsMatch(clause)) continue;   // возраст — забота AgeWindow

                var marriage = WhenMarriageRx.Match(clause);
                if (marriage.Success && int.TryParse(marriage.Groups[1].Value, out var off))
                {
                    card.MarriageOffsetYears = off;
                    card.RequiresParentYes = MarriageCardId;   // без свадьбы годовщины не бывает
                    continue;
                }

                var parent = WhenParentYesRx.Match(clause);
                if (parent.Success) { card.RequiresParentYes = parent.Groups[1].Value; continue; }

                var before = WhenBeforeRx.Match(clause);
                if (before.Success) { card.BeforeCardId = before.Groups[1].Value; continue; }

                var below = WhenMoneyBelowRx.Match(clause);
                if (below.Success && int.TryParse(below.Groups[1].Value, out var lo))
                { card.RequiresMoneyBelow = lo; continue; }

                var atLeast = WhenMoneyAtLeastRx.Match(clause);
                if (atLeast.Success && int.TryParse(atLeast.Groups[1].Value, out var hi))
                { card.RequiresMoneyAtLeast = hi; continue; }

                if (WhenMoneyMinusRx.IsMatch(clause)) { card.RequiresMoneyBelow = 0; continue; }

                var health = WhenHealthBelowRx.Match(clause);
                if (health.Success && int.TryParse(health.Groups[1].Value, out var hp))
                { card.RequiresHealthBelow = hp; continue; }

                var minAge = WhenMinAgeRx.Match(clause);
                if (minAge.Success && int.TryParse(minAge.Groups[1].Value, out var age))
                { card.RequiresMinAge = age; continue; }

                var lost = WhenScaleLostRx.Match(clause);
                if (lost.Success && NormalizeScaleToken(lost.Groups[1].Value) == Card.OpenRelations)
                { card.RequiresRelationshipsLost = true; continue; }

                var open = WhenScaleOpenRx.Match(clause);
                if (open.Success)
                {
                    var token = NormalizeScaleToken(open.Groups[1].Value);
                    if (token != null) { card.RequiresScaleOpen = token; continue; }
                }

                if (WhenAfterRx.IsMatch(clause)) continue;   // порядок интро — держат возрастные окна

                if (System.Array.Exists(WhenProseNoCondition,
                        p => string.Equals(p, clause, System.StringComparison.OrdinalIgnoreCase)))
                    continue;

                card.UnparsedWhen.Add(clause);
            }
        }

        /// <summary>Веха свадьбы — якорь для окон «свадьба +N».</summary>
        public const string MarriageCardId = "MD01";

        // «EXCL:ипотека» / «EXCL:реб» → ключ группы («ипотека» / «реб»). Ключ произвольный: движку важно
        // только совпадение строк, поэтому новые ветки заводятся дизайнером без правки кода.
        private const string ExclusivePrefix = "EXCL:";

        internal static string ParseExclusiveGroup(IEnumerable<string> flags)
        {
            foreach (var f in flags)
                if (f.StartsWith(ExclusivePrefix, StringComparison.Ordinal))
                {
                    var key = f.Substring(ExclusivePrefix.Length).Trim();
                    if (key.Length > 0) return key;
                }
            return null;
        }

        // «Длительный эффект» grammar (entries split by ';'):
        //   MULT:Дн=x2 FROM:25   income multiplier ×2 from age 25
        //   MULT:Дн=x1.5         income multiplier ×1.5 (stacks)
        //   MULT:Дн=x5|0         random ×5 OR wipe money to 0 (with RANDOM_OUTCOME)
        //   DRAIN:Дн=-0.3/s DUR:10y   installment drain −0.3₽/сек for 10 game-years
        //   DRIFT:Отн=x2 [DUR:10y]    множитель пассивного дрейфа шкалы (отрезок 0); без DUR — бессрочно
        // Любую запись можно префиксовать стороной: «НЕТ:DRIFT:Отн=x2» (без префикса = ДА, как было).
        // Culture-invariant number parse so "1.5"/"0.3" never depend on the machine locale.
        private static readonly Regex MultRx = new(
            @"MULT:\s*(Здр|Эн|Дн|Отн|Реб)\s*=\s*x\s*([0-9]+(?:\.[0-9]+)?)\s*(\|\s*0)?\s*(?:FROM:\s*(\d+))?",
            RegexOptions.Compiled);
        private static readonly Regex DrainRx = new(
            @"DRAIN:\s*(Здр|Эн|Дн|Отн|Реб)\s*=\s*(-?[0-9]+(?:\.[0-9]+)?)\s*/s\s+DUR:\s*(\d+)\s*y",
            RegexOptions.Compiled);
        // РАЗОВАЯ выплата через N лет: «DRAIN:Дн=-25 ONCE:3y» / «DRAIN:Дн=+100 ONCE:10y». Отличается от
        // дренажа отсутствием «/s» — сумма, а не скорость.
        private static readonly Regex OnceRx = new(
            @"DRAIN:\s*(Здр|Эн|Дн|Отн|Реб)\s*=\s*([+-]?[0-9]+(?:\.[0-9]+)?)\s+ONCE:\s*(\d+)\s*y",
            RegexOptions.Compiled);
        private static readonly Regex DriftRx = new(
            @"DRIFT:\s*(Здр|Эн|Дн|Отн|Реб)\s*=\s*x\s*([0-9]+(?:\.[0-9]+)?)\s*(?:DUR:\s*(\d+)\s*y)?",
            RegexOptions.Compiled);
        // Префикс стороны: «ДА:…» / «НЕТ:…» (латиница DA/NO тоже принимается — CSV правят руками).
        private static readonly Regex SideRx = new(
            @"^\s*(ДА|НЕТ|DA|NO|YES)\s*:\s*", RegexOptions.Compiled);

        private static readonly System.Globalization.CultureInfo Inv =
            System.Globalization.CultureInfo.InvariantCulture;

        internal static List<LongEffect> ParseLongEffects(string cell)
        {
            var result = new List<LongEffect>();
            var t = (cell ?? string.Empty).Trim();
            if (t.Length == 0 || t == "—" || t == "-" || t == "−") return result;

            foreach (var raw in t.Split(';'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;

                // Сторона: «ДА:» / «НЕТ:» перед записью. Без префикса — ДА (все существующие строки).
                bool onNo = false;
                var sideMatch = SideRx.Match(part);
                if (sideMatch.Success)
                {
                    var tag = sideMatch.Groups[1].Value;
                    onNo = tag == "НЕТ" || tag == "NO";
                    part = part.Substring(sideMatch.Length).Trim();
                    if (part.Length == 0) continue;
                }

                var m = MultRx.Match(part);
                if (m.Success && Abbrevs.TryGetValue(m.Groups[1].Value, out var mscale))
                {
                    double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, Inv, out var val);
                    int.TryParse(m.Groups[4].Value, out var from);
                    result.Add(new LongEffect
                    {
                        Kind = LongEffectKind.Mult,
                        Scale = mscale,
                        MultValue = val,
                        RandomZero = m.Groups[3].Success,
                        FromAge = from,
                        OnNoSide = onNo,
                    });
                    continue;
                }

                // ONCE проверяется ДО дренажа: обе записи начинаются с «DRAIN:», и различает их только
                // «/s» против « ONCE:». Порядок здесь и есть различение.
                var o = OnceRx.Match(part);
                if (o.Success && Abbrevs.TryGetValue(o.Groups[1].Value, out var oscale))
                {
                    double.TryParse(o.Groups[2].Value, System.Globalization.NumberStyles.Float, Inv, out var amount);
                    int.TryParse(o.Groups[3].Value, out var years);
                    result.Add(new LongEffect
                    {
                        Kind = LongEffectKind.Once,
                        Scale = oscale,
                        OnceAmount = amount,
                        OnceYears = years,
                        OnNoSide = onNo,
                    });
                    continue;
                }

                var d = DrainRx.Match(part);
                if (d.Success && Abbrevs.TryGetValue(d.Groups[1].Value, out var dscale))
                {
                    double.TryParse(d.Groups[2].Value, System.Globalization.NumberStyles.Float, Inv, out var rate);
                    int.TryParse(d.Groups[3].Value, out var dur);
                    result.Add(new LongEffect
                    {
                        Kind = LongEffectKind.Drain,
                        Scale = dscale,
                        DrainPerSec = rate,
                        DurYears = dur,
                        OnNoSide = onNo,
                    });
                    continue;
                }

                var f = DriftRx.Match(part);
                if (f.Success && Abbrevs.TryGetValue(f.Groups[1].Value, out var fscale))
                {
                    double.TryParse(f.Groups[2].Value, System.Globalization.NumberStyles.Float, Inv, out var mult);
                    int.TryParse(f.Groups[3].Value, out var fdur);   // без DUR → 0 = бессрочно
                    result.Add(new LongEffect
                    {
                        Kind = LongEffectKind.Drift,
                        Scale = fscale,
                        MultValue = mult,
                        DurYears = fdur,
                        OnNoSide = onNo,
                    });
                }
                // Unknown grammar → tolerated (ignored), never throws (contract: all 50 rows parse).
            }
            return result;
        }

        private static List<string> ParseFlags(string cell)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(cell)) return result;
            foreach (var raw in cell.Split(','))
            {
                var f = raw.Trim();
                if (f.Length > 0) result.Add(f);
            }
            return result;
        }

        // Δ column: "Здр +1, Эн −1" / "Дн ±3" / "Здр → 80%" / "—" (any unicode/ascii minus).
        private static readonly Dictionary<string, Scale> Abbrevs = new()
        {
            { "Здр", Scale.Health },
            { "Эн", Scale.Energy },
            { "Дн", Scale.Money },
            { "Отн", Scale.Relationships },
            { "Реб", Scale.Child },
        };

        // group 1 = abbrev, then either "→ N" (set) OR "<op> N" where op is + - − ±
        private static readonly Regex DeltaRx = new(
            @"(Здр|Эн|Дн|Отн|Реб)\s*(?:(→)\s*(\d+)|([+\-−±])\s*(\d+))",
            RegexOptions.Compiled);

        internal static List<ScaleDelta> ParseDeltas(string cell)
        {
            var deltas = new List<ScaleDelta>();
            if (string.IsNullOrEmpty(cell)) return deltas;
            var t = cell.Trim();
            if (t == "—" || t == "-" || t == "−") return deltas;

            foreach (Match m in DeltaRx.Matches(t))
            {
                if (!Abbrevs.TryGetValue(m.Groups[1].Value, out var scale)) continue;

                if (m.Groups[2].Success) // "→ N" set form
                {
                    int.TryParse(m.Groups[3].Value, out var v);
                    deltas.Add(new ScaleDelta(scale, DeltaKind.Set, v));
                }
                else
                {
                    string op = m.Groups[4].Value;
                    int.TryParse(m.Groups[5].Value, out var v);
                    if (op == "±")
                        deltas.Add(new ScaleDelta(scale, DeltaKind.RandomPlusMinus, v));
                    else if (op == "+")
                        deltas.Add(new ScaleDelta(scale, DeltaKind.Add, v));
                    else // "-" ascii OR "−" unicode
                        deltas.Add(new ScaleDelta(scale, DeltaKind.Add, -v));
                }
            }
            return deltas;
        }
    }
}
