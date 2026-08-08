using System.Collections.Generic;
using System.Linq;
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

            // Схлопывание двойного многоточия на шве сохранено (дизайн-док §6.2(3): «механика склейки
            // остаётся как есть — меняется только разделитель»). Перенос строки — ЛИТЕРАЛОМ, не через
            // Necrolog.LineSeparator: сверять константу с самой собой значит не проверять ничего.
            StringAssert.StartsWith("Но не переживайте! Ведь вы…\nродились у прекрасных родителей.", story);
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

            // Плашка держит ровно MaxLines строк, считая фиксированных родителей.
            Assert.AreEqual(Necrolog.MaxLines, r.StoryLines.Count);
            Assert.AreEqual(Necrolog.ParentsLine, r.StoryLines[0]);

            // ROND — низший приоритет: пока есть весомые строки, ни одна кековая не попадает.
            int rondKept = 0;
            foreach (var line in r.StoryLines)
                if (line.StartsWith("rond")) rondKept++;
            Assert.AreEqual(0, rondKept, "бюджет целиком выбран весомыми строками — ROND не попал ни одной");

            // …и добраны они с начала хронологии, а не из середины списка.
            for (int i = 0; i < Necrolog.MaxLines - 1; i++)
                Assert.AreEqual("weighty" + i, r.StoryLines[i + 1]);
        }

        [Test]
        public void EveryLine_StartsOnItsOwnLine_NotOneLongParagraph()
        {
            // Главная жалоба живого плейтеста: «некролог большущей простынёй, сплошным текстом, никто
            // читать не будет». Проверяем ровно её: сколько строк отобрано — столько РЯДОВ и на экране,
            // плюс отдельный ряд под зачин. Литеральный '\n' — тест не должен зависеть от константы.
            var r = Necrolog.Build("спокойная старость", new List<NecrologEntry>
            {
                new() { Age = 7,  Order = 0, Line = "Рыжий кот из детства прожил с вами всю жизнь." },
                new() { Age = 19, Order = 1, Line = "Первую зарплату спустили за один вечер." },
                new() { Age = 30, Order = 2, Line = "Свадьбу сыграли, и это было громко.", IsMilestone = true },
            });

            var rows = r.ComposeStory().Split('\n');
            Assert.AreEqual(1 + r.StoryLines.Count, rows.Length,
                "зачин + КАЖДАЯ строка истории отдельным рядом (а не всё одним абзацем через пробел)");
            Assert.AreEqual(Necrolog.StoryIntro, rows[0]);
            Assert.AreEqual("родились у прекрасных родителей.", rows[1], "многоточие схлопнулось на шве");
            Assert.AreEqual("Свадьбу сыграли, и это было громко.", rows[rows.Length - 1]);
            foreach (var row in rows)
                Assert.IsFalse(row.Contains("  "), "внутри ряда не остаётся склеек через двойной пробел");
        }

        [Test]
        public void Milestones_AreNeverDropped_EvenWhenOrdinaryLinesAreOlder()
        {
            // Отрезок 0 §6.2(2): отбор ПО ВЕСУ. Раньше при переполнении резалась СЕРЕДИНА хронологии —
            // то есть ровно та часть жизни, где игрок больше всего наделал. Теперь вехи (TIMELINE)
            // забираются первыми, а обычные строки добивают остаток.
            var entries = new List<NecrologEntry>();
            for (int i = 0; i < 12; i++)   // 12 обычных строк детства — с запасом больше бюджета
                entries.Add(new NecrologEntry { Age = 5, Order = i, Line = "ordinary" + i });
            entries.Add(new NecrologEntry { Age = 30, Order = 100, Line = "свадьба", IsMilestone = true });
            entries.Add(new NecrologEntry { Age = 62, Order = 101, Line = "дети выросли", IsMilestone = true });

            var r = Necrolog.Build("спокойная старость", entries);

            Assert.AreEqual(Necrolog.MaxLines, r.StoryLines.Count);
            CollectionAssert.Contains(r.StoryLines, "свадьба", "веха не выкидывается…");
            CollectionAssert.Contains(r.StoryLines, "дети выросли", "…ни одна");
            // …и печатается в хронологическом порядке, а не в порядке отбора.
            Assert.Less(r.StoryLines.IndexOf("ordinary0"), r.StoryLines.IndexOf("свадьба"));
            Assert.Less(r.StoryLines.IndexOf("свадьба"), r.StoryLines.IndexOf("дети выросли"));
        }

        [Test]
        public void OrdinaryLines_SpreadOverLifeStages_BeforeFillingUp()
        {
            // «по одной на этап жизни (детство · юность · молодость · зрелость · старость), чтобы
            // получилась биография, а не список». Проверяем на перекошенном входе: детство завалено
            // строками, у остальных этапов — по одной. Все пять этапов обязаны прозвучать.
            var entries = new List<NecrologEntry>();
            for (int i = 0; i < 10; i++)
                entries.Add(new NecrologEntry { Age = 6, Order = i, Line = "детство" + i });
            entries.Add(new NecrologEntry { Age = 18, Order = 50, Line = "юность" });
            entries.Add(new NecrologEntry { Age = 25, Order = 51, Line = "молодость" });
            entries.Add(new NecrologEntry { Age = 40, Order = 52, Line = "зрелость" });
            entries.Add(new NecrologEntry { Age = 70, Order = 53, Line = "старость" });

            var r = Necrolog.Build("спокойная старость", entries);

            foreach (var stage in new[] { "юность", "молодость", "зрелость", "старость" })
                CollectionAssert.Contains(r.StoryLines, stage, "этап " + stage + " обязан прозвучать");
            CollectionAssert.Contains(r.StoryLines, "детство0", "…и детство тоже");
            // Перекошенный этап не съедает плашку: сначала каждый этап получает по строке, и только
            // ОСТАТОК бюджета (6 − 5 = 1) достаётся второй детской. Пяти подряд про шесть лет не бывает.
            Assert.AreEqual(2, r.StoryLines.Count(l => l.StartsWith("детство")),
                "детство берёт свою строку + единственный оставшийся слот, не больше");
        }

        [Test]
        public void ShortLife_FillsTheBudget_InsteadOfPrintingOnePerStage()
        {
            // Обратная сторона правила: «по одной на этап» — это ПОРЯДОК отбора, а не потолок. Если
            // строк мало и место осталось, оставшиеся добираются, иначе короткая жизнь печатала бы две
            // строки при месте на шесть.
            var entries = new List<NecrologEntry>();
            for (int i = 0; i < 4; i++)
                entries.Add(new NecrologEntry { Age = 7, Order = i, Line = "детство" + i });

            var r = Necrolog.Build("спокойная старость", entries);

            Assert.AreEqual(5, r.StoryLines.Count, "родители + все четыре строки — место есть");
        }

        [Test]
        public void IdenticalLines_TakeOneSlot_AndTheFreedSlotGoesToTheNextEntry()
        {
            // Находка ревью (MINOR): защита от повтора стояла на ОБЪЕКТЕ записи (`chosen.Contains(e)`), а
            // на плашке дублем читается ОДИНАКОВЫЙ ТЕКСТ. Кек-карточки и филлеры делят строки законно, так
            // что при семи местах всего два «…купили ненужную вещь» подряд съедали слот у настоящей вехи.
            var entries = new List<NecrologEntry>
            {
                new() { Age = 19, Order = 0, Line = "Купили ненужную вещь на распродаже." },
                new() { Age = 21, Order = 1, Line = "Купили ненужную вещь на распродаже." },
                new() { Age = 30, Order = 2, Line = "Свадьбу сыграли, и это было громко.", IsMilestone = true },
                new() { Age = 33, Order = 3, Line = "Родился ребёнок.", IsMilestone = true },
                new() { Age = 40, Order = 4, Line = "Ушли с офисной работы в никуда." },
                new() { Age = 52, Order = 5, Line = "Второй язык так и остался на уровне A1." },
                new() { Age = 70, Order = 6, Line = "Внуки приезжали каждое лето." },
            };

            var r = Necrolog.Build("весёлая старость", entries);

            Assert.AreEqual(1, r.StoryLines.Count(l => l == "Купили ненужную вещь на распродаже."),
                "одинаковый текст занимает РОВНО ОДИН слот");
            Assert.AreEqual(r.StoryLines.Count, r.StoryLines.Distinct().Count(),
                "…и вообще ни одна строка плашки не повторяется");
            Assert.AreEqual(Necrolog.MaxLines, r.StoryLines.Count,
                "освободившийся слот не пропал — плашка заполнена до конца");
            CollectionAssert.Contains(r.StoryLines, "Внуки приезжали каждое лето.",
                "…и достался следующему кандидату, который раньше не влезал");
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

        // ---- S11: строка исхода в запечённой плашке финала (build-spec §4-H) -------------------------

        // Родительный падеж после «до». Таблица покрывает обе ветки правила и обе ловушки:
        // 11 (единица, но «лет») и её сотенный повтор 111, против 101 (единица → «года»).
        [TestCase(1, "года")]      // до 1 года
        [TestCase(2, "лет")]       // до 2 лет — НЕ «до 2 года»: счётное «2 года» здесь не действует
        [TestCase(5, "лет")]
        [TestCase(11, "лет")]      // 11 — исключение из правила единиц
        [TestCase(21, "года")]     // до 21 года
        [TestCase(41, "года")]
        [TestCase(78, "лет")]
        [TestCase(100, "лет")]
        [TestCase(101, "года")]    // 101 — единица, «до 101 года»
        [TestCase(111, "лет")]     // 111 — сотенный повтор 11, «до 111 лет»
        public void GenitiveYears_IsTheCaseThePrepositionДоDemands(int n, string expected)
        {
            Assert.AreEqual(expected, Necrolog.GenitiveYears(n));
        }

        [Test]
        public void GenitiveYears_NeverLeavesAnAgeWithoutAWord()
        {
            for (int a = 0; a <= 120; a++) Assert.IsNotEmpty(Necrolog.GenitiveYears(a), "возраст " + a);
        }

        [Test]
        public void AgeLine_ReadsAsARealSentence_AndNeverPrintsZero()
        {
            // ⚠ «до» требует РОДИТЕЛЬНОГО падежа: «до 41 года», не «до 41 год» (Codex MAJOR 2026-08-05).
            Assert.AreEqual("Ты дожил до 41 года", Necrolog.AgeLine(41));
            Assert.AreEqual("Ты дожил до 100 лет", Necrolog.AgeLine(100));
            Assert.AreEqual("Ты дожил до 1 года", Necrolog.AgeLine(0),
                "FATAL до старта счётчика возраста не печатает «до 0 лет»");
        }

        [Test]
        public void OutcomeBlock_IsAgeLine_ThenTheCauseLine()
        {
            var r = Necrolog.Build("вы сунули палец в розетку", new List<NecrologEntry>());
            Assert.AreEqual("Ты дожил до 7 лет\nПричина конца: вы сунули палец в розетку",
                r.OutcomeBlock(7), "две строки исхода: возраст, затем причина");
        }
    }
}
