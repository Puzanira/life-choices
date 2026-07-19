using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    public class LoaderTests
    {
        private const string HeaderLine =
            "ID,Карточка,Когда,Тип,ДА-проза,ДА-Δ,НЕТ-проза,НЕТ-Δ,ВедущийДА,ВедущийНЕТ,НекроДА,НекроНЕТ,Флаги\n";

        // Canon 2026-07-18 header with the 14th column «Длительный эффект».
        private const string HeaderLine14 =
            "ID,Карточка,Когда,Тип,ДА-проза,ДА-Δ,НЕТ-проза,НЕТ-Δ,ВедущийДА,ВедущийНЕТ,НекроДА,НекроНЕТ,Флаги,Длительный эффект\n";

        // Canon 2026-07-19 header with the 15th column «Тон».
        private const string HeaderLine15 =
            "ID,Карточка,Когда,Тип,ДА-проза,ДА-Δ,НЕТ-проза,НЕТ-Δ,ВедущийДА,ВедущийНЕТ,НекроДА,НекроНЕТ,Флаги,Длительный эффект,Тон\n";

        [Test]
        public void ToneColumn_Parses_WhenSet_NullWhenBlank_TolerantWhenAbsent()
        {
            string csv = HeaderLine15 +
                // 15 cols: explicit «absurd» tag in the last column.
                "KEK01,Мем,4–8,Кек,,Дн −1,,—,,,—,—,\"NOCONS, ROND\",,absurd\n" +
                // 15 cols but a blank «Тон» cell → null.
                "YA01,Работа,18,Развилка,,Дн +2,,—,,,ДА,—,,,\n" +
                // 14 cols (old snapshot, no «Тон» column at all) → tolerated, Tone stays null.
                "CH04,Дерево,6–12,Детство,,Эн −1,,—,,,ЛазилиДА,—,ROND,\n";

            var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
            Assert.AreEqual("absurd", byId["KEK01"].Tone, "explicit «Тон» tag read from col 14");
            Assert.IsNull(byId["YA01"].Tone, "blank «Тон» cell → null");
            Assert.IsNull(byId["CH04"].Tone, "a shorter row (no «Тон» column) → null, no crash");
            // Trailing «Тон» column must not shift the flags/long-effect columns.
            Assert.IsTrue(byId["KEK01"].IsRond, "flags (col 12) still parse with a trailing «Тон» column");
            Assert.IsTrue(byId["KEK01"].IsNoCons, "NOCONS flag still parses");
        }

        [Test]
        public void Tolerant_Parses_QuotedCommas_BlankFields_BothMinusSigns_NonContiguousIds()
        {
            // Non-contiguous ids (RND02 absent), a quoted field with commas, blank cells,
            // ascii '-' in one delta and unicode '−' in another, and an unsupported flag.
            string csv = HeaderLine +
                "RND01,\"Соблазн, с запятой\",20+,Соблазн,,Дн +2,,Дн -1,,,ЛинияДА,ЛинияНЕТ,\"DELAY(3), FATAL\"\n" +
                "RND03,Порошок,18+,Соблазн,,—,,—,,,—,ЛинияНЕТ3,FATAL\n" +
                "CH04,Дерево,6–12,Детство,,Эн −1,,—,,,ЛазилиДА,—,ROND\n";

            var all = CardLoader.ParseAll(csv);
            Assert.AreEqual(3, all.Count, "three data rows parsed");

            var rnd01 = all.First(c => c.Id == "RND01");
            Assert.AreEqual("Соблазн, с запятой", rnd01.Question, "quoted comma preserved");
            Assert.AreEqual(20, rnd01.Age);
            // ascii '-' minus parsed on the НЕТ side
            Assert.AreEqual(1, rnd01.NoDeltas.Count);
            Assert.AreEqual(-1, rnd01.NoDeltas[0].Value);
            Assert.AreEqual(2, rnd01.YesDeltas[0].Value);
            // DELAY(n)+FATAL is a *delayed* fatal (RND01): не мгновенно, а через n игровых лет.
            Assert.IsFalse(rnd01.YesIsFatal, "DELAY+FATAL is delayed, not an immediate fatal");
            Assert.AreEqual(3, rnd01.DelayedFatalYears, "DELAY(3) parsed into a 3-year delayed fatal");
            Assert.AreEqual("за вами пришли", rnd01.FatalCause, "RND01 fatal cause still mapped");

            var ch04 = all.First(c => c.Id == "CH04");
            // unicode '−' minus parsed
            Assert.AreEqual(Scale.Energy, ch04.YesDeltas[0].Scale);
            Assert.AreEqual(-1, ch04.YesDeltas[0].Value, "unicode minus parsed as negative");
            Assert.IsTrue(ch04.IsRond);
            Assert.IsNull(ch04.NoNecrolog, "'—' necrolog cell becomes null");
        }

        [Test]
        public void SetForm_And_RandomPlusMinus_Parse()
        {
            string csv = HeaderLine +
                "LT08,Подлечиться,30,Система,,Здр → 80%,,—,,,—,—,BLOCK$\n" +
                "YA02,Стартап,19,Развилка,,Дн ±3,,—,,,ДА,—,RANDOM\n";
            var all = CardLoader.ParseAll(csv);

            var lt08 = all.First(c => c.Id == "LT08");
            Assert.AreEqual(DeltaKind.Set, lt08.YesDeltas[0].Kind);
            Assert.AreEqual(80, lt08.YesDeltas[0].Value);

            var ya02 = all.First(c => c.Id == "YA02");
            Assert.AreEqual(DeltaKind.RandomPlusMinus, ya02.YesDeltas[0].Kind);
            Assert.AreEqual(3, ya02.YesDeltas[0].Value);
        }

        [Test]
        public void NonNumericWhen_SortsToEnd_WithoutBreaking()
        {
            string csv = HeaderLine +
                "RND06,Селфи,любой,Фатальная,,—,,—,,,—,СтрокаНЕТ,\"FATAL, RANDOM\"\n" +
                "CH01,Жук,4–8,Детство,,Здр +1,,—,,,ДА,НЕТ,ROND\n";
            var subset = CardLoader.LoadSubset(csv, new[] { "RND06", "CH01" });
            Assert.AreEqual(2, subset.Count);
            Assert.AreEqual("CH01", subset[0].Id, "numeric age sorts first");
            Assert.AreEqual("RND06", subset[1].Id, "non-numeric 'любой' sorts last");
        }

        [Test]
        public void ImmediateFatals_StayImmediate_OnlyRnd01IsDelayed()
        {
            // Regression guard for the DELAY+FATAL→delayed change: the three plain-FATAL cards
            // must remain INSTANT deaths; only RND01 (DELAY(3)+FATAL) is deferred.
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var all = CardLoader.ParseAll(asset.text);

            var rnd06 = all.First(c => c.Id == "RND06");
            Assert.IsTrue(rnd06.YesIsFatal, "RND06 (селфи) is an immediate fatal");
            Assert.AreEqual(0, rnd06.DelayedFatalYears, "RND06 has no delay");
            Assert.AreEqual("селфи на краю крыши", rnd06.FatalCause);

            var ch02 = all.First(c => c.Id == "CH02");
            Assert.IsTrue(ch02.YesIsFatal, "CH02 (розетка) still immediate");
            Assert.AreEqual(0, ch02.DelayedFatalYears);

            var rnd03 = all.First(c => c.Id == "RND03");
            Assert.IsTrue(rnd03.YesIsFatal, "RND03 (порошок) still immediate");
            Assert.AreEqual(0, rnd03.DelayedFatalYears);

            var rnd01 = all.First(c => c.Id == "RND01");
            Assert.IsFalse(rnd01.YesIsFatal, "RND01 is NOT immediate");
            Assert.AreEqual(3, rnd01.DelayedFatalYears, "RND01 fatal deferred by 3 event-years");
        }

        // ---- canon 2026-07-18: FORCED + RANDOM_TRIGGER/OUTCOME split + 14th column ----

        [Test]
        public void ForcedFlag_And_RandomSplit_Parse_FromFlagsColumn()
        {
            string csv = HeaderLine14 +
                // FORCED веха; RANDOM_TRIGGER (вероятностное выпадение); RANDOM_OUTCOME (случаен исход)
                "YA05,Усталость,25,Таймлайн,,—,,Эн −1,,,—,—,\"OPEN:Эн, TIMELINE, FORCED\",—\n" +
                "RND06,Селфи,любой,Фатальная,,—,,—,,,—,СтрокаНЕТ,\"FATAL, RANDOM_TRIGGER\",—\n" +
                "YA02,Стартап,\"19–21, если YA01=ДА\",Развилка,,Дн ±3,,—,,,ДА,—,RANDOM_OUTCOME,MULT:Дн=x5|0\n";
            var all = CardLoader.ParseAll(csv);

            var ya05 = all.First(c => c.Id == "YA05");
            Assert.IsTrue(ya05.IsForced, "FORCED parsed on YA05");
            Assert.IsFalse(ya05.IsRandomTrigger); Assert.IsFalse(ya05.IsRandomOutcome);

            var rnd06 = all.First(c => c.Id == "RND06");
            Assert.IsTrue(rnd06.IsRandomTrigger, "RANDOM_TRIGGER = probabilistic inclusion marker");
            Assert.IsFalse(rnd06.IsRandomOutcome, "not an outcome-random card");
            Assert.IsTrue(rnd06.YesIsFatal, "FATAL still parsed alongside RANDOM_TRIGGER");

            var ya02 = all.First(c => c.Id == "YA02");
            Assert.IsTrue(ya02.IsRandomOutcome, "RANDOM_OUTCOME parsed on YA02");
            Assert.IsFalse(ya02.IsRandomTrigger, "RANDOM_OUTCOME is NOT a probabilistic-inclusion marker");
            Assert.AreEqual(DeltaKind.RandomPlusMinus, ya02.YesDeltas[0].Kind, "outcome randomness lives in ±Δ");
        }

        [Test]
        public void LegacyRandomLiteral_MapsTo_RandomTrigger()
        {
            // The OLD snapshot used a bare "RANDOM" flag; it must still load and behave as TRIGGER
            // (backward compat so both snapshots parse).
            string csv = HeaderLine +
                "RND06,Селфи,любой,Фатальная,,—,,—,,,—,СтрокаНЕТ,\"FATAL, RANDOM\"\n";
            var rnd06 = CardLoader.ParseAll(csv).First(c => c.Id == "RND06");
            Assert.IsTrue(rnd06.IsRandomTrigger, "legacy RANDOM → probabilistic-inclusion (TRIGGER)");
            Assert.IsFalse(rnd06.IsRandomOutcome, "legacy RANDOM is not OUTCOME");
        }

        [Test]
        public void FourteenthColumn_Tolerated_PresentAndAbsent_InSameParse()
        {
            // A 14-column row (canon) and a 13-column row (old) parse side by side. The «Длительный
            // эффект» cell (index 13) must NEVER be read as a flag — proven with a red-herring "FORCED"
            // placed in the 14th column of the first row: flags live only in column 12.
            string csv = HeaderLine14 +
                // 14 cols: real flag TIMELINE in col12; a decoy "FORCED" in the 14th «Длительный эффект».
                "YA01,Универ,18,Развилка,,Дн −1,,\"Дн +1, Эн −1\",Умница!,,ОкупилосьДА,СвободноНЕТ,TIMELINE,FORCED\n" +
                // 13 cols (old snapshot shape): genuine FORCED in col12, no 14th cell at all.
                "YA05,Усталость,25,Таймлайн,,—,,Эн −1,,,—,—,\"TIMELINE, FORCED\"\n";
            var all = CardLoader.ParseAll(csv);
            Assert.AreEqual(2, all.Count, "both the 14-col and the 13-col row parsed");

            var ya01 = all.First(c => c.Id == "YA01");
            Assert.IsFalse(ya01.IsForced, "the 14th «Длительный эффект» cell is NOT read as a flag");
            Assert.IsTrue(ya01.NoDeltas.Any(d => d.Scale == Scale.Money && d.Value == 1), "YA01 НЕТ Дн +1 intact");

            var ya05 = all.First(c => c.Id == "YA05");
            Assert.IsTrue(ya05.IsForced, "13-col row still parses its flags column (genuine FORCED)");
        }

        [Test]
        public void Lt02_When_YieldsWindow_55_to_70_FromCanon()
        {
            // Canon «Когда» is now «55–70, если Здр<50%» — prose after the comma must be stripped,
            // leaving the age window 55–70.
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var lt02 = CardLoader.ParseAll(asset.text).First(c => c.Id == "LT02");
            Assert.AreEqual(55, lt02.Age, "leading age parsed from «55–70, если Здр<50%»");
            var w = DeckSampler.AgeWindow.Parse(lt02.When);
            Assert.AreEqual(55, w.Min); Assert.AreEqual(70, w.Max);
        }

        [Test]
        public void Canon_Snapshot_Flags_Parse_Forced_And_RandomSplit()
        {
            // Spot-check the real (refreshed) Resources snapshot carries the canon flags.
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var byId = CardLoader.ParseAll(asset.text).ToDictionary(c => c.Id);

            Assert.IsTrue(byId["YA05"].IsForced, "YA05 FORCED");
            Assert.IsTrue(byId["CR00"].IsForced, "CR00 FORCED");
            Assert.IsTrue(byId["CR09"].IsForced, "CR09 FORCED");
            Assert.IsTrue(byId["RND06"].IsRandomTrigger, "RND06 RANDOM_TRIGGER");
            Assert.IsTrue(byId["LT08"].IsRandomTrigger, "LT08 RANDOM_TRIGGER");
            Assert.IsTrue(byId["YA02"].IsRandomOutcome && !byId["YA02"].IsRandomTrigger, "YA02 OUTCOME only");
            Assert.IsTrue(byId["RND04"].IsRandomOutcome && !byId["RND04"].IsRandomTrigger, "RND04 OUTCOME only");
            Assert.IsFalse(byId["RND01"].IsRandomTrigger, "RND01 is a normal card (never RANDOM in canon)");
        }

        // ---- «Длительный эффект» (col 13): money multipliers + installment drains ----

        [Test]
        public void LongEffects_Parse_Mult_Drain_From_And_RandomZero_FromCanon()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var byId = CardLoader.ParseAll(asset.text).ToDictionary(c => c.Id);

            // YA01 → MULT:Дн=x2 FROM:25
            var ya01 = byId["YA01"].LongEffects.Single();
            Assert.AreEqual(LongEffectKind.Mult, ya01.Kind);
            Assert.AreEqual(Scale.Money, ya01.Scale);
            Assert.AreEqual(2.0, ya01.MultValue, 1e-9);
            Assert.AreEqual(25, ya01.FromAge);
            Assert.IsFalse(ya01.RandomZero);

            // YA06 → MULT:Дн=x1.5 (stacks, no FROM)
            var ya06 = byId["YA06"].LongEffects.Single();
            Assert.AreEqual(1.5, ya06.MultValue, 1e-9);
            Assert.AreEqual(0, ya06.FromAge, "no FROM → active immediately");

            // YA02 → MULT:Дн=x5|0 (random ×5 or wipe), paired with RANDOM_OUTCOME
            var ya02 = byId["YA02"].LongEffects.Single();
            Assert.AreEqual(5.0, ya02.MultValue, 1e-9);
            Assert.IsTrue(ya02.RandomZero, "x5|0 → random-zero branch parsed");

            // YA04 → DRAIN:Дн=-0.3/s DUR:10y ; MD04 → DUR:20y
            var ya04 = byId["YA04"].LongEffects.Single();
            Assert.AreEqual(LongEffectKind.Drain, ya04.Kind);
            Assert.AreEqual(-0.3, ya04.DrainPerSec, 1e-9);
            Assert.AreEqual(10, ya04.DurYears);
            Assert.AreEqual(20, byId["MD04"].LongEffects.Single().DurYears, "ипотека DUR:20y");
        }

        [Test]
        public void BlockCost_Flag_Parsed_On_Price_Cards()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var byId = CardLoader.ParseAll(asset.text).ToDictionary(c => c.Id);
            Assert.IsTrue(byId["MD03"].IsBlockCost, "MD03 (отпуск) BLOCK$");
            Assert.IsTrue(byId["LT02"].IsBlockCost, "LT02 (операция) BLOCK$");
        }

        [Test]
        public void EveryCanonRow_ParsesLongEffectColumn_WithoutError()
        {
            // Contract row 6: «Длительный эффект» of all 50 canon rows parses tolerantly — no throw,
            // and the money-effect cards carry a parsed entry.
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var all = CardLoader.ParseAll(asset.text);
            Assert.Greater(all.Count, 40, "full canon deck parsed");
            foreach (var c in all)
                Assert.IsNotNull(c.LongEffects, $"{c.Id} has a (possibly empty) long-effects list");

            int withLong = all.Count(c => c.LongEffects.Count > 0);
            Assert.GreaterOrEqual(withLong, 5, "the money-effect cards (YA01/YA02/YA04/YA06/MD04) parsed entries");
        }

        [Test]
        public void RealSubset_Loads_FromResources_InAgeOrder_WithFlags()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes.csv present");

            var subset = CardLoader.LoadSubset(asset.text, CardLoader.DefaultSubset);
            Assert.AreEqual(CardLoader.DefaultSubset.Length, subset.Count,
                "every requested spine card loaded despite non-contiguous ids");

            // Ordered by age.
            for (int i = 1; i < subset.Count; i++)
                Assert.LessOrEqual(subset[i - 1].Age, subset[i].Age, "cards drawn in age order");

            Assert.AreEqual("I02", subset[0].Id, "intro card first");

            var ch02 = subset.First(c => c.Id == "CH02");
            Assert.IsTrue(ch02.YesIsFatal, "CH02 (розетка) is FATAL");
            Assert.AreEqual("вы сунули палец в розетку", ch02.FatalCause);

            var i02 = subset.First(c => c.Id == "I02");
            Assert.IsTrue(i02.IsNoCons, "I02 is NOCONS");
            Assert.IsNull(i02.YesNecrolog, "intro excluded from necrolog");

            var ya01 = subset.First(c => c.Id == "YA01");
            Assert.IsTrue(ya01.NoDeltas.Any(d => d.Scale == Scale.Money && d.Value == 1),
                "YA01 НЕТ gives Дн +1");
            Assert.IsTrue(ya01.NoDeltas.Any(d => d.Scale == Scale.Energy && d.Value == -1),
                "YA01 НЕТ gives Эн −1");
        }
    }
}
