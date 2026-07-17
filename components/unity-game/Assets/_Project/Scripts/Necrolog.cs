using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        /// <summary>Full glued story: intro + all story lines.</summary>
        public string ComposeStory()
        {
            var sb = new StringBuilder(IntroLine);
            foreach (var l in StoryLines) sb.Append(' ').Append(l);
            return sb.ToString();
        }

        public string CauseLine => "Причина конца: " + Cause;
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
