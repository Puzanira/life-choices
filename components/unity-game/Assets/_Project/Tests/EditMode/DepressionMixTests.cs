using NUnit.Framework;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Спека «микса в вате» — таблица, утверждённая основательницей НА СЛУХ по демо. Здесь она
    /// заморожена числами: если кто-то однажды «подправит» частоты или уронит ступень, красным
    /// станет тест, а не главный художественный приём игры.
    /// </summary>
    public class DepressionMixTests
    {
        [Test]
        public void FiveSteps_MatchGameSteps()
        {
            Assert.AreEqual(5, DepressionMix.Steps, "ступеней возврата ровно пять");
            Assert.AreEqual(Game.DepressionGraySteps, DepressionMix.Steps,
                "ступени звука и ступени серости — одно и то же: разъедутся, и вата переживёт выход");
        }

        [Test]
        public void CutoffTable_IsExactlyTheManifestSpec()
        {
            Assert.AreEqual(500f, DepressionMix.CutoffFor(0), 0.01f);    // вход
            Assert.AreEqual(900f, DepressionMix.CutoffFor(1), 0.01f);
            Assert.AreEqual(1700f, DepressionMix.CutoffFor(2), 0.01f);
            Assert.AreEqual(3200f, DepressionMix.CutoffFor(3), 0.01f);
            Assert.AreEqual(6000f, DepressionMix.CutoffFor(4), 0.01f);
            Assert.AreEqual(DepressionMix.CutoffOff, DepressionMix.CutoffFor(5), 0.01f);   // выход
        }

        [Test]
        public void DecibelTable_IsExactlyTheManifestSpec()
        {
            Assert.AreEqual(-9f, DepressionMix.DecibelsFor(0), 0.01f);
            Assert.AreEqual(-7f, DepressionMix.DecibelsFor(1), 0.01f);
            Assert.AreEqual(-5f, DepressionMix.DecibelsFor(2), 0.01f);
            Assert.AreEqual(-3f, DepressionMix.DecibelsFor(3), 0.01f);
            Assert.AreEqual(-1f, DepressionMix.DecibelsFor(4), 0.01f);
            Assert.AreEqual(0f, DepressionMix.DecibelsFor(5), 0.01f);
        }

        [Test]
        public void EachHit_OpensTheMixFurther()
        {
            for (int h = 0; h < DepressionMix.Steps; h++)
            {
                Assert.Less(DepressionMix.CutoffFor(h), DepressionMix.CutoffFor(h + 1),
                    "срез обязан расти с каждым попаданием, ступень " + h);
                Assert.Less(DepressionMix.GainFor(h), DepressionMix.GainFor(h + 1),
                    "громкость обязана расти с каждым попаданием, ступень " + h);
            }
        }

        [Test]
        public void ExitIsFullyTransparent()
        {
            Assert.IsFalse(DepressionMix.Transparent(0), "вход — это самая густая вата, а не прозрачность");
            Assert.IsFalse(DepressionMix.Transparent(4));
            Assert.IsTrue(DepressionMix.Transparent(5), "пятое попадание снимает фильтр целиком");
            Assert.AreEqual(1f, DepressionMix.GainFor(5), 0.0001f, "на выходе громкость возвращается в ноль дБ");
        }

        [Test]
        public void GainMatchesDecibels()
        {
            // −9 дБ ≈ 0.355; −1 дБ ≈ 0.891. Проверяем, что перевод дБ→множитель не «на глаз».
            Assert.AreEqual(0.3548f, DepressionMix.GainFor(0), 0.001f);
            Assert.AreEqual(0.8913f, DepressionMix.GainFor(4), 0.001f);
        }

        [Test]
        public void GrayToHits_TranslatesDirection()
        {
            // Game считает ОСТАВШУЮСЯ серость 5→0; спека считает НАБРАННЫЕ попадания 0→5.
            Assert.AreEqual(0, DepressionMix.HitsFromGray(5), "только вошли: попаданий нет");
            Assert.AreEqual(1, DepressionMix.HitsFromGray(4));
            Assert.AreEqual(4, DepressionMix.HitsFromGray(1));
            Assert.AreEqual(5, DepressionMix.HitsFromGray(0), "серость снята — это пятое попадание");
        }

        [Test]
        public void OutOfRangeHits_AreClamped()
        {
            Assert.AreEqual(DepressionMix.CutoffFor(0), DepressionMix.CutoffFor(-3), 0.01f);
            Assert.AreEqual(DepressionMix.CutoffFor(5), DepressionMix.CutoffFor(99), 0.01f);
        }

        [Test]
        public void TwoCascades_AsTheSpecAsks()
        {
            Assert.AreEqual(2, DepressionMix.Cascades, "спека: 2 каскада по 2 полюса");
        }
    }
}
