using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// CardLoader now reads the «Ведущий (ДА)/(НЕТ)» columns (8/9) into <see cref="Card.HostYes"/>/
    /// <see cref="Card.HostNo"/> and the TIMELINE flag into <see cref="Card.IsTimeline"/>. Verified
    /// against both a synthetic CSV and the real scenes.csv (CH01/YA01/CH02 named lines are canon).
    /// </summary>
    public class HostLoaderTests
    {
        private const string Header14 =
            "ID,Карточка,Когда,Тип,ДА-проза,ДА-Δ,НЕТ-проза,НЕТ-Δ,ВедущийДА,ВедущийНЕТ,НекроДА,НекроНЕТ,Флаги,Длит\n";

        [Test]
        public void HostLines_LoadFromCols8And9_BlankAndDash_BecomeNull()
        {
            string csv = Header14 +
                "YA01,Универ,18,Веха,,Дн +1,,—,Умница!,\"Бунтарь, обожаю!\",—,—,\"TIMELINE, OPEN:Дн\",\n" +
                "CH03,Кот,5,Детство,,Эн +1,,—,,,—,—,,\n" +          // both host cells blank → null
                "MD01,Свадьба,28,Веха,,Отн +2,,—,ГОРЬКО!,—,—,—,TIMELINE,\n"; // НЕТ cell is «—» → null
            var all = CardLoader.ParseAll(csv);

            var ya01 = all.First(c => c.Id == "YA01");
            Assert.AreEqual("Умница!", ya01.HostYes);
            Assert.AreEqual("Бунтарь, обожаю!", ya01.HostNo, "quoted comma in host line preserved");
            Assert.IsTrue(ya01.IsTimeline, "TIMELINE flag parsed");

            var ch03 = all.First(c => c.Id == "CH03");
            Assert.IsNull(ch03.HostYes, "blank host cell → null");
            Assert.IsNull(ch03.HostNo);
            Assert.IsFalse(ch03.IsTimeline, "no TIMELINE flag → not a milestone");

            var md01 = all.First(c => c.Id == "MD01");
            Assert.AreEqual("ГОРЬКО!", md01.HostYes);
            Assert.IsNull(md01.HostNo, "«—» host cell → null");
        }

        [Test]
        public void RealCsv_NamedHostLines_AreCanon()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "scenes.csv present in Resources");
            var all = CardLoader.ParseAll(asset.text);

            var ch01 = all.First(c => c.Id == "CH01");
            Assert.AreEqual("Крепкий орешек!", ch01.HostYes);

            var ya01 = all.First(c => c.Id == "YA01");
            Assert.AreEqual("Умница!", ya01.HostYes);
            Assert.AreEqual("Бунтарь, обожаю!", ya01.HostNo);
            Assert.IsTrue(ya01.IsTimeline, "YA01 is a TIMELINE milestone");

            var ch02 = all.First(c => c.Id == "CH02");
            Assert.AreEqual("Ну, спасибо за игру!", ch02.HostYes);
            Assert.AreEqual("Пронесло!", ch02.HostNo);
        }

        [Test]
        public void RealCsv_TimelineFlag_MarksMilestones_NotNormalCards()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            var all = CardLoader.ParseAll(asset.text);

            foreach (var id in new[] { "I03", "YA01", "YA03", "YA05", "MD01" })
                Assert.IsTrue(all.First(c => c.Id == id).IsTimeline, id + " is a milestone");

            foreach (var id in new[] { "I02", "CH01", "CH02", "YA06" })
                Assert.IsFalse(all.First(c => c.Id == id).IsTimeline, id + " is a normal card");
        }
    }
}
