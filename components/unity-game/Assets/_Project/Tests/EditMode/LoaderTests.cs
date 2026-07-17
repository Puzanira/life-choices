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
            Assert.IsTrue(rnd01.YesIsFatal, "FATAL flag recognised alongside unsupported DELAY(3)");

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
