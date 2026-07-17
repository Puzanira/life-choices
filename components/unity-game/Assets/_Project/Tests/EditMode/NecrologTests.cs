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
