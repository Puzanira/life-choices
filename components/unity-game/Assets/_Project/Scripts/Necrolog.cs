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
    }

    public sealed class NecrologResult
    {
        public string Title;             // "СПАСИБО ЗА ИГРУ!"
        public string Cause;             // cause phrase, e.g. "весёлая старость"
        public string IntroLine;         // "Но не переживайте! Ведь вы…"
        public List<string> StoryLines;  // [0] is always the parents line, then choices in age order

        /// <summary>Full glued story: intro + all story lines, seams normalised (see <see cref="Necrolog.Glue"/>).</summary>
        public string ComposeStory()
        {
            var text = IntroLine ?? string.Empty;
            if (StoryLines != null)
                foreach (var l in StoryLines) text = Necrolog.Glue(text, l);
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
    /// Assembles the finale necrolog: title + cause + glued story. The story always opens
    /// with the parents line, then the chosen cards' lines in AGE order. When there are too
    /// many lines, ROND ("неважные") entries are dropped first.
    /// </summary>
    public static class Necrolog
    {
        public const string Title = "СПАСИБО ЗА ИГРУ!";
        public const string StoryIntro = "Но не переживайте! Ведь вы…";
        public const string ParentsLine = "…родились у прекрасных родителей.";
        public const int MaxLines = 15;

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
        /// Glues one story fragment onto the running text and NORMALISES THE SEAM. The intro ends with
        /// «…» and the parents line opens with «…», so plain concatenation printed the double ellipsis
        /// the design gate caught on the finale frame (2026-07-31): «Ведь вы… …родились у прекрасных
        /// родителей». Rule: when the previous fragment already ends in a terminator («…» or «.») and the
        /// next one opens with an ellipsis, the redundant LEADING ellipsis is dropped — exactly one mark
        /// survives the seam. This touches the JOIN only: no CSV line and no constant is edited, and a
        /// fragment that does not open with «…» is appended verbatim.
        /// </summary>
        public static string Glue(string prev, string next)
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

            return head.Length == 0 ? tail : tail + " " + head;
        }

        // «…» (U+2026) and the ASCII spelling «...» both count, so a hand-typed line collapses too.
        private static bool StartsWithEllipsis(string s)
            => s.Length > 0 && (s[0] == '…' || s.StartsWith("..."));

        private static bool EndsWithTerminator(string s)
            => s.Length > 0 && (s[s.Length - 1] == '…' || s[s.Length - 1] == '.');

        public static NecrologResult Build(string cause, IEnumerable<NecrologEntry> entries)
        {
            var list = entries
                .Where(e => e != null && !string.IsNullOrEmpty(e.Line))
                .OrderBy(e => e.Age).ThenBy(e => e.Order)
                .ToList();

            // Over the limit → drop ROND-flagged lines first (lowest priority), earliest first.
            // The fixed parents line counts toward the total.
            while (1 + list.Count > MaxLines && list.Any(e => e.IsRond))
            {
                int idx = list.FindIndex(e => e.IsRond);
                list.RemoveAt(idx);
            }

            // HARD cap: still over with no ROND left → drop from the middle of the remaining
            // chronology. Deterministic; keeps the childhood opening and the late-life ending
            // (the story's bookends read best) until real weights exist (GAME_SPEC «приоритет
            // весомым выборам»).
            while (1 + list.Count > MaxLines)
                list.RemoveAt(list.Count / 2);

            var story = new List<string> { ParentsLine };
            story.AddRange(list.Select(e => e.Line));

            return new NecrologResult
            {
                Title = Title,
                Cause = cause,
                IntroLine = StoryIntro,
                StoryLines = story,
            };
        }
    }
}
