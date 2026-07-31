using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    public class NecrologTests
    {
        [Test]
        public void Assembles_ParentsFirst_ThenAgeOrder()
        {
            var entries = new List<NecrologEntry>
            {
                new() { Age = 22, Order = 5, Line = "поздняя строка" },
                new() { Age = 4,  Order = 1, Line = "ранняя строка" },
                new() { Age = 8,  Order = 3, Line = "средняя строка" },
            };
            var r = Necrolog.Build("спокойная старость", entries);

            Assert.AreEqual("СПАСИБО ЗА ИГРУ!", r.Title);
            Assert.AreEqual("Причина конца: спокойная старость", r.CauseLine);
            Assert.AreEqual(Necrolog.ParentsLine, r.StoryLines[0], "parents line always first");
            Assert.AreEqual("ранняя строка", r.StoryLines[1]);
            Assert.AreEqual("средняя строка", r.StoryLines[2]);
            Assert.AreEqual("поздняя строка", r.StoryLines[3]);
            StringAssert.StartsWith("Но не переживайте! Ведь вы…", r.ComposeStory());
        }

        // The intro ends with «…» and the parents line OPENS with «…»: plain concatenation printed
        // «Ведь вы… …родились у прекрасных родителей» on the finale frame (design gate 2026-07-31).
        // The SEAM is normalised — the CSV/const content itself is never edited.
        [Test]
        public void Glue_CollapsesDoubleEllipsisAtTheSeam()
        {
            Assert.AreEqual("Ведь вы… родились у прекрасных родителей.",
                Necrolog.Glue("Ведь вы…", "…родились у прекрасных родителей."),
                "«…» + «…» collapse into a single ellipsis");
            Assert.AreEqual("Ведь вы… родились.", Necrolog.Glue("Ведь вы…", "...родились."),
                "the ASCII spelling «...» collapses too");
            Assert.AreEqual("Жили ярко. Родились.", Necrolog.Glue("Жили ярко.", "…Родились."),
                "a full stop already terminates the seam — the leading ellipsis is redundant there too");
            Assert.AreEqual("Ведь вы… Жили ярко.", Necrolog.Glue("Ведь вы…", "Жили ярко."),
                "a fragment that does NOT open with an ellipsis is appended verbatim");
            Assert.AreEqual("Ведь вы…", Necrolog.Glue("Ведь вы…", "…"),
                "a fragment that is nothing but an ellipsis leaves no dangling seam");
        }

        [Test]
        public void ComposedStory_HasNoDoubleEllipsis()
        {
            var r = Necrolog.Build("весёлая старость", new List<NecrologEntry>
            {
                new() { Age = 5, Order = 0, Line = "Ели жуков и ничего не боялись." },
            });
            var story = r.ComposeStory();

            StringAssert.StartsWith("Но не переживайте! Ведь вы… родились у прекрасных родителей.", story);
            StringAssert.DoesNotContain("… …", story, "no double ellipsis anywhere in the glued story");
            StringAssert.DoesNotContain("……", story, "…nor a glued-together one");
            StringAssert.Contains("Ели жуков и ничего не боялись.", story, "the CSV line itself is untouched");
        }

        [Test]
        public void EmptyLines_And_Nulls_Excluded()
        {
            var entries = new List<NecrologEntry>
            {
                new() { Age = 1, Order = 0, Line = null },
                new() { Age = 2, Order = 1, Line = "" },
                new() { Age = 3, Order = 2, Line = "реальная" },
            };
            var r = Necrolog.Build("одинокая старость", entries);
            Assert.AreEqual(2, r.StoryLines.Count, "parents + one real line");
            Assert.AreEqual("реальная", r.StoryLines[1]);
        }

        [Test]
        public void OverLimit_DropsRondFirst()
        {
            var entries = new List<NecrologEntry>();
            // 10 ROND + 10 weighty, all with distinct ages
            for (int i = 0; i < 10; i++)
                entries.Add(new NecrologEntry { Age = i, Order = i, Line = "rond" + i, IsRond = true });
            for (int i = 0; i < 10; i++)
                entries.Add(new NecrologEntry { Age = 100 + i, Order = 100 + i, Line = "weighty" + i, IsRond = false });

            var r = Necrolog.Build("весёлая старость", entries);

            // parents (1) + kept lines must be <= 15
            Assert.LessOrEqual(r.StoryLines.Count, Necrolog.MaxLines);
            // all weighty lines survive; only ROND were dropped
            foreach (var e in entries)
                if (!e.IsRond)
                    CollectionAssert.Contains(r.StoryLines, e.Line);

            int rondKept = 0;
            foreach (var line in r.StoryLines)
                if (line.StartsWith("rond")) rondKept++;
            Assert.AreEqual(4, rondKept, "1 parents + 10 weighty + 4 rond = 15");
        }

        [Test]
        public void HardCap_HoldsEvenWithoutRond_TruncatingFromTheMiddle()
        {
            // 20 non-ROND lines + 3 ROND: ROND go first, then the middle of the chronology is
            // truncated deterministically until the hard 15-line cap holds.
            var entries = new List<NecrologEntry>();
            for (int i = 1; i <= 20; i++)
                entries.Add(new NecrologEntry { Age = i, Order = i, Line = "weighty" + i });
            for (int i = 0; i < 3; i++)
                entries.Add(new NecrologEntry { Age = 50 + i, Order = 50 + i, Line = "rond" + i, IsRond = true });

            var r = Necrolog.Build("весёлая старость", entries);

            Assert.AreEqual(Necrolog.MaxLines, r.StoryLines.Count, "hard cap: exactly 15 lines");
            foreach (var line in r.StoryLines)
                StringAssert.DoesNotStartWith("rond", line, "every ROND line dropped first");
            Assert.AreEqual(Necrolog.ParentsLine, r.StoryLines[0], "parents line always survives");
            Assert.AreEqual("weighty1", r.StoryLines[1], "the childhood opening survives");
            Assert.AreEqual("weighty20", r.StoryLines[^1], "the late-life ending survives");
        }
    }
}
