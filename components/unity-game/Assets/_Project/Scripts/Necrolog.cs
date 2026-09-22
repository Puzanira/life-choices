using System.Collections.Generic;
using System.Linq;

namespace ThanksNoThanks
{
    /// <summary>One recorded life choice that contributes a story line to the necrolog.</summary>
    public sealed class NecrologEntry
    {
        public int Age;
        public int Order;
        public string Line;
        public bool IsRond;

        /// <summary>
        /// ВЕХА (карточка с флагом `TIMELINE`): любовь, свадьба, ребёнок, второй шанс, дети выросли,
        /// тёмная полоса. Отбор отрезка 0 (`cards-script-00…md` §6.2) НИКОГДА их не выкидывает — сначала
        /// в некролог попадают они, и только потом остаток добивается обычными строками.
        /// </summary>
        public bool IsMilestone;
    }

    public sealed class NecrologResult
    {
        public string Title;             // "СПАСИБО ЗА ИГРУ!"
        public string Cause;             // cause phrase, e.g. "весёлая старость"
        public List<string> StoryLines;  // ТОЛЬКО содержательные вехи, в порядке возраста

        /// <summary>
        /// Полный текст истории: отобранные вехи, КАЖДАЯ С НОВОЙ СТРОКИ, швы нормализованы
        /// (<see cref="Necrolog.Glue"/>).
        ///
        /// ⚠ 2026-08-08: разделитель был ПРОБЕЛ, и финал выходил сплошным абзацем из пятнадцати законченных
        /// предложений подряд (живой плейтест основательницы: «некролог большущей простынёй, никто читать не
        /// будет»). Отрезок 0 поменял лимит 15 → <see cref="Necrolog.MaxLines"/> и этот разделитель.
        ///
        /// ⚠ 2026-09-22, ЖИВОЙ ОТСМОТР КАДРОВ ОСНОВАТЕЛЬНИЦЕЙ — ПОДВОДКИ БОЛЬШЕ НЕТ. Её слова: «„Но не
        /// переживайте — ведь вы родились у прекрасных родителей“ как будто пишется везде и ни о чём игровом
        /// не сообщает — убрать, на последнем экране много текста и читается плохо». Обе строки были
        /// ЗАПЕЧЁННЫМИ, не игровыми: зачин `StoryIntro` печатался всегда, и строка родителей стояла первой
        /// в КАЖДОМ забеге независимо от того, что игрок делал. Удалены обе, и вместе с ними — поле
        /// `IntroLine`: некролог теперь равен ровно тому, что игрок прожил. Освободившиеся два ряда
        /// отданы содержанию (бюджет вех 6 → <see cref="Necrolog.MaxLines"/>) и воздуху блока.
        ///
        /// Склейка (схлопывание двойного многоточия на шве) ОСТАВЛЕНА: она защищает шов между любыми двумя
        /// строками, а не только тот, которого больше нет (дизайн-док §6.2(3)).
        /// </summary>
        public string ComposeStory()
        {
            var text = string.Empty;
            if (StoryLines != null)
                foreach (var l in StoryLines) text = Necrolog.Glue(text, l, Necrolog.LineSeparator);
            return text;
        }

        public string CauseLine => "Причина конца: " + Cause;

        /// <summary>
        /// Строка исхода для запечённой плашки финала (build-spec §4-H «Ты дожил до N лет / Причина: …»):
        /// возраст первой строкой, причина — второй. Причина берётся ИЗ <see cref="CauseLine"/>, т.е.
        /// формулировка «Причина конца: …» остаётся единственной в проекте (её же читают EditMode-тесты).
        /// </summary>
        public string OutcomeBlock(int age) => Necrolog.AgeLine(age) + "\n" + CauseLine;
    }

    /// <summary>
    /// Assembles the finale necrolog: title + cause + glued story. The story is the chosen cards'
    /// lines in AGE order and NOTHING else. When there are too many lines, ROND ("неважные")
    /// entries are dropped first.
    /// </summary>
    public static class Necrolog
    {
        public const string Title = "СПАСИБО ЗА ИГРУ!";

        /// <summary>
        /// Сколько СОДЕРЖАТЕЛЬНЫХ строк на финальной плашке. Фиксированных рядов больше нет — сколько
        /// здесь написано, столько вех игрок и увидит.
        ///
        /// ⚠ БЫЛО 15 (≈550 знаков сплошным абзацем). Решение основательницы 2026-08-07: «финал шоу — не
        /// биография, а эпитафия: чем короче, тем злее» → 7, но из них ДВА ряда съедали зачин и строка
        /// родителей, то есть содержания оставалось шесть.
        ///
        /// ⚠ 2026-09-22 (живой отсмотр кадров): зачин и строка родителей удалены как «ни о чём игровом не
        /// сообщающие». Число 7 не тронуто — тронут его СМЫСЛ: теперь это семь строк ПРО ИГРОКА, а не
        /// «две запечённых + пять прожитых». Высота плашки от этого не изменилась (было 8 рисуемых рядов,
        /// стало 7), поэтому освободившийся ряд ушёл в воздух блока.
        /// </summary>
        public const int MaxLines = 7;

        /// <summary>Разделитель строк истории: каждая с новой строки, а не сплошным абзацем (§6.2(3)).</summary>
        public const string LineSeparator = "\n";

        /// <summary>
        /// Единственная строка некролога для забега БЕЗ ЕДИНОЙ вехи (мгновенный FATAL в первые годы).
        /// Утверждена основательницей 2026-09-22 («для пустого ок»); совсем пустая плашка читалась бы
        /// как поломка. Это НЕ возврат запечённой подводки: строка появляется только когда сказать
        /// больше действительно нечего.
        /// </summary>
        public const string EmptyLifeLine = "И это, пожалуй, всё, что вы успели.";

        // --- Этапы жизни для отбора «по одной на этап» (§6.2(2)) --------------------------------------
        // Границы совпадают с фазами сэмплера (DeckSampler): детство ≤17 · юность 18–19 · молодость 20–29 ·
        // зрелость 30–54 · старость 55+. Держатся здесь константами, чтобы Necrolog остался чистым (без
        // ссылки на сэмплер) и его можно было тестировать одними синтетическими записями.
        public const int StageChildhoodMaxAge = 17;
        public const int StageYouthMaxAge = 19;
        public const int StageYoungMaxAge = 29;
        public const int StageMidMaxAge = 54;
        public const int LifeStages = 5;

        /// <summary>Номер этапа жизни (0 — детство … 4 — старость) для возраста записи.</summary>
        public static int StageOf(int age)
            => age <= StageChildhoodMaxAge ? 0
             : age <= StageYouthMaxAge ? 1
             : age <= StageYoungMaxAge ? 2
             : age <= StageMidMaxAge ? 3
             : 4;

        /// <summary>
        /// «Ты дожил до N лет» — первая строка исхода на плашке финала. Возраст ЗАЖИМАЕТСЯ снизу
        /// единицей: FATAL-карта может прилететь ещё до того, как счётчик возраста пошёл (он стартует
        /// на I03), и «дожил до 0 лет» — не текст, а баг на экране.
        /// </summary>
        public static string AgeLine(int age)
        {
            int a = age < 1 ? 1 : age;
            return "Ты дожил до " + a + " " + GenitiveYears(a);
        }

        /// <summary>
        /// Слово «год» в РОДИТЕЛЬНОМ падеже — том, которого требует предлог «до»: «до 1 года»,
        /// «до 41 года», «до 78 лет». ⚠ Это НЕ обычное счётное склонение (1 год · 2–4 года · 5+ лет):
        /// после «до» именительный давал «дожил до 41 год», а «2–4 года» здесь совпадает с «лет»
        /// («до 2 лет», не «до 2 года»). Правило родительного проще счётного: единица (кроме 11 и
        /// её сотенных повторов) → «года», всё остальное → «лет». Чистая функция, покрыта юнит-тестом.
        /// </summary>
        public static string GenitiveYears(int n)
        {
            int abs = n < 0 ? -n : n;
            return abs % 10 == 1 && abs % 100 != 11 ? "года" : "лет";
        }

        /// <summary>
        /// Glues one story fragment onto the running text and NORMALISES THE SEAM. Историческое
        /// происхождение: зачин кончался на «…», а строка родителей на «…» ОТКРЫВАЛАСЬ, и простая склейка
        /// печатала двойное многоточие, пойманное дизайн-гейтом на кадре финала (2026-07-31): «Ведь вы…
        /// …родились у прекрасных родителей». Обеих строк с 2026-09-22 нет, а шов ОСТАЛСЯ — он про любые
        /// две соседние строки, и ни одна строка колоды не обязана начинаться с буквы.
        /// Rule: when the previous fragment already ends in a terminator («…» or «.») and the
        /// next one opens with an ellipsis, the redundant LEADING ellipsis is dropped — exactly one mark
        /// survives the seam. This touches the JOIN only: no CSV line and no constant is edited, and a
        /// fragment that does not open with «…» is appended verbatim.
        /// </summary>
        public static string Glue(string prev, string next) => Glue(prev, next, " ");

        /// <summary>
        /// Тот же шов, но с ЯВНЫМ разделителем: истории финала нужен перенос строки
        /// (<see cref="LineSeparator"/>), а двухаргументная перегрузка остаётся пробельной — на неё
        /// опираются юнит-тесты шва и любой внешний вызов.
        /// </summary>
        public static string Glue(string prev, string next, string separator)
        {
            if (string.IsNullOrEmpty(next)) return prev ?? string.Empty;
            if (string.IsNullOrEmpty(prev)) return next.Trim();

            var tail = prev.TrimEnd();
            var head = next.TrimStart();

            if (EndsWithTerminator(tail))
                while (StartsWithEllipsis(head))
                {
                    head = (head[0] == '…' ? head.Substring(1) : head.Substring(3)).TrimStart();
                    if (head.Length == 0) return tail;
                }

            return head.Length == 0 ? tail : tail + (separator ?? " ") + head;
        }

        // «…» (U+2026) and the ASCII spelling «...» both count, so a hand-typed line collapses too.
        private static bool StartsWithEllipsis(string s)
            => s.Length > 0 && (s[0] == '…' || s.StartsWith("..."));

        private static bool EndsWithTerminator(string s)
            => s.Length > 0 && (s[s.Length - 1] == '…' || s[s.Length - 1] == '.');

        /// <summary>
        /// Собрать некролог: заголовок + причина + отобранные строки.
        ///
        /// ОТБОР ПО ВЕСУ, А НЕ ПО СЕРЕДИНЕ СПИСКА (`cards-script-00…md` §6.2(2), решение основательницы
        /// 2026-08-07). Старый порядок при переполнении резал СЕРЕДИНУ хронологии — то есть ровно ту часть
        /// жизни, где игрок больше всего наделал, оставляя детство со старостью. Новый порядок:
        ///
        ///  1. ВЕХИ (<see cref="NecrologEntry.IsMilestone"/>, флаг `TIMELINE`) — берутся первыми и не
        ///     выкидываются;
        ///  2. ОБЫЧНЫЕ — сначала ПО ОДНОЙ НА ЭТАП ЖИЗНИ (детство · юность · молодость · зрелость ·
        ///     старость), чтобы получилась биография, а не пять строк подряд про двадцать пять лет;
        ///  3. ОБЫЧНЫЕ остальные — добивают бюджет, если место ещё есть;
        ///  4. `ROND` (кек и флейвор) — только по остаточному принципу, как и раньше.
        ///
        /// Бюджет — весь <see cref="MaxLines"/>: фиксированных рядов больше нет (решение основательницы
        /// 2026-09-22, см. <see cref="NecrologResult.ComposeStory"/>), и освободившееся место от строки
        /// родителей досталось СОДЕРЖАНИЮ. Отобранное печатается в хронологическом порядке независимо от
        /// того, каким проходом попало.
        /// </summary>
        public static NecrologResult Build(string cause, IEnumerable<NecrologEntry> entries)
        {
            var list = entries
                .Where(e => e != null && !string.IsNullOrEmpty(e.Line))
                .OrderBy(e => e.Age).ThenBy(e => e.Order)
                .ToList();

            int budget = MaxLines;              // весь лимит — под вехи игрока
            var chosen = new List<NecrologEntry>(budget);

            // ⚠ ДЕДУП ПО ТЕКСТУ, А НЕ ПО ОБЪЕКТУ (находка ревью, MINOR). Проходов отбора четыре, и одна и
            // та же запись обязана попасть в некролог один раз — это `chosen.Contains(e)` и обеспечивал.
            // Но на плашке дублем читается ОДИНАКОВАЯ СТРОКА, а не одинаковая ссылка: у семи мест всего
            // (родители + шесть) две «…купили ненужную вещь на распродаже» подряд — это потерянная веха,
            // а не стиль. Кек-карточки (`KEK01`–`KEK03`, `KEK05`) и повторяющиеся филлеры делят текст
            // законно, поэтому режем на ОТБОРЕ: второй экземпляр не занимает слот, и он достаётся
            // следующему кандидату.
            void Take(NecrologEntry e)
            {
                if (chosen.Count >= budget) return;
                foreach (var c in chosen)
                    if (c == e || c.Line == e.Line) return;
                chosen.Add(e);
            }

            // 1) вехи — приоритет 1, никогда не выкидываются (пока влезают в плашку).
            foreach (var e in list.Where(e => e.IsMilestone)) Take(e);

            // 2) обычные — по одной на этап жизни, самая ранняя в этапе.
            var ordinary = list.Where(e => !e.IsMilestone && !e.IsRond).ToList();
            for (int stage = 0; stage < LifeStages && chosen.Count < budget; stage++)
            {
                var first = ordinary.FirstOrDefault(e => StageOf(e.Age) == stage && !chosen.Contains(e));
                if (first != null) Take(first);
            }

            // 3) остальные обычные добивают бюджет (иначе короткая жизнь печатала бы 2 строки при месте на 6).
            foreach (var e in ordinary) Take(e);

            // 4) ROND — по остаточному принципу.
            foreach (var e in list.Where(e => e.IsRond && !e.IsMilestone)) Take(e);

            chosen.Sort((a, b) => a.Age != b.Age ? a.Age.CompareTo(b.Age) : a.Order.CompareTo(b.Order));

            // ⚠ НИКАКОЙ ЗАПЕЧЁННОЙ ПЕРВОЙ СТРОКИ (2026-09-22). Здесь стояло
            // `new List<string> { ParentsLine }` — и это была ровно та строка, которую основательница
            // прочитала на кадре как «пишется везде и ни о чём игровом не сообщает».
            var story = chosen.Select(e => e.Line).ToList();

            // Забег ВООБЩЕ без вех (мгновенный FATAL в первые годы) — единственное исключение:
            // совсем пустая плашка читается как поломка, поэтому одна строка-эпитафия
            // (утверждена основательницей 2026-09-22, кандидат из макетов).
            if (story.Count == 0) story.Add(EmptyLifeLine);

            return new NecrologResult
            {
                Title = Title,
                Cause = cause,
                StoryLines = story,
            };
        }
    }
}
