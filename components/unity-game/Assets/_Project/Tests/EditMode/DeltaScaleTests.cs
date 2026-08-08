using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// КАНОН ОТРЕЗКА 0 — конверсия Δ (`cards-script-00-deltas-and-prices.md` §2) и два правила, которые
    /// живут на том же шве: Δ только по ОТКРЫТЫМ шкалам и зажим процентных шкал в 0…100.
    ///
    /// Смысл всего файла одной строкой: до 2026-08-08 «Дн −2» на айфоне снимало ДВА РУБЛЯ при доходе
    /// ~4 ₽/сек — полсекунды кручения. Эти тесты не дают вернуться в то состояние.
    /// </summary>
    public class DeltaScaleTests
    {
        // ---- 1. таблица §2, все шесть значений на каждую шкалу, точно -------------------------------

        [TestCase(Scale.Money, 1, 20)]
        [TestCase(Scale.Money, 2, 50)]
        [TestCase(Scale.Money, 3, 100)]
        [TestCase(Scale.Health, 1, 7)]
        [TestCase(Scale.Health, 2, 15)]
        [TestCase(Scale.Health, 3, 30)]
        [TestCase(Scale.Energy, 1, 8)]
        [TestCase(Scale.Energy, 2, 18)]
        [TestCase(Scale.Energy, 3, 35)]
        [TestCase(Scale.Relationships, 1, 5)]
        [TestCase(Scale.Relationships, 2, 12)]
        [TestCase(Scale.Relationships, 3, 30)]
        public void Qualitative_Steps_MapToCanonValues_BothSigns(Scale scale, int step, int want)
        {
            Assert.AreEqual(want, DeltaScale.Resolve(scale, DeltaKind.Add, step),
                $"«{scale} +{step}» = +{want}");
            Assert.AreEqual(-want, DeltaScale.Resolve(scale, DeltaKind.Add, -step),
                $"«{scale} −{step}» = −{want}");
            // ±N несёт МОДУЛЬ шага (знак разыгрывает монетка вызывающего) — модуль и на выходе.
            Assert.AreEqual(want, DeltaScale.Resolve(scale, DeltaKind.RandomPlusMinus, step),
                $"«{scale} ±{step}» = ±{want}");
        }

        [Test]
        public void TheAnchor_IphoneIsFiftyRubles()
        {
            // Якорь, от которого основательница задала всю таблицу: FC08 «новый флагман» = 50 ₽,
            // «после этого на отпуск (60 ₽) уже не хватит, придётся выбирать одно из двух».
            Assert.AreEqual(-50, DeltaScale.Resolve(Scale.Money, DeltaKind.Add, -2));
            Assert.AreEqual(50.0, Game.BlockPrices["FC08"], "…и цена самой карточки-якоря совпадает");
        }

        // ---- 2. абсолютные значения не конвертируются ------------------------------------------------

        [Test]
        public void AbsoluteValues_PassThroughUntouched()
        {
            // Та же колонка смешивает две системы счёта: «Здр +40» (LT02, операция) и «Здр → 80%» (LT08)
            // — настоящие проценты, а не качественные шаги. Признак чисто арифметический: |шаг| > 3.
            Assert.AreEqual(40, DeltaScale.Resolve(Scale.Health, DeltaKind.Add, 40));
            Assert.AreEqual(-40, DeltaScale.Resolve(Scale.Health, DeltaKind.Add, -40));
            Assert.AreEqual(80, DeltaScale.Resolve(Scale.Health, DeltaKind.Set, 80));
            Assert.AreEqual(4, DeltaScale.Resolve(Scale.Money, DeltaKind.Add, 4), "граница: 4 уже абсолютно");
            Assert.AreEqual(0, DeltaScale.Resolve(Scale.Money, DeltaKind.Add, 0));
            Assert.IsFalse(DeltaScale.IsQualitative(DeltaKind.Set, 3), "форма «→ N» абсолютна ВСЕГДА");
        }

        [Test]
        public void RealCsv_Lt02_StaysAnAbsoluteFortyPercent()
        {
            // Живая строка колоды: если конверсия однажды съест абсолютные значения, операция за 120 ₽
            // начнёт лечить на 30 п.п. вместо обещанных 40.
            var csv = UnityEngine.Resources.Load<UnityEngine.TextAsset>("scenes");
            Assert.IsNotNull(csv);
            var lt02 = CardLoader.ParseAll(csv.text).Find(c => c.Id == "LT02");
            var d = lt02.YesDeltas[0];
            Assert.AreEqual(Scale.Health, d.Scale);
            Assert.AreEqual(40, DeltaScale.Resolve(d).Value, "LT02 «Здр +40» остаётся сорока процентами");
        }

        // ---- 3. Δ только по ОТКРЫТЫМ шкалам ----------------------------------------------------------

        private static Card Card(string id, int age, params ScaleDelta[] yes) => new Card
        {
            Id = id, Question = id + "?", Age = age, Order = age,
            YesDeltas = yes, NoDeltas = new List<ScaleDelta>(), Flags = new List<string>(),
        };

        private static Card Starter()
        {
            var c = Card("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        [Test]
        public void ClosedScales_SwallowTheirDelta()
        {
            // Вторая находка сценарной сессии: Δ раздавались по шкалам, которых на экране ещё нет —
            // в детстве по энергии и отношениям. Теперь такая Δ просто не применяется.
            var child = Card("CH", 6,
                new ScaleDelta(Scale.Energy, DeltaKind.Add, -2),
                new ScaleDelta(Scale.Relationships, DeltaKind.Add, -3),
                new ScaleDelta(Scale.Health, DeltaKind.Add, -1));
            var g = new Game(new[] { Starter(), child, Card("N", 40) }, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);          // I03 → CH

            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(100, g.Scales.Energy, "энергия закрыта до 25 — Δ по ней не падает");
            Assert.AreEqual(55, g.Scales.Relationships, "отношения закрыты до 20 — Δ по ним не падает");
            Assert.AreEqual(93, g.Scales.Health, "…а здоровье открыто с первого кадра: −1 = −7 п.п.");
        }

        [Test]
        public void TheOpeningCard_KeepsItsOwnDelta()
        {
            // Единственное исключение и ровно то, ради которого правило знает про `OPEN:*`: карточка,
            // которая САМА открывает шкалу («ПЕРВАЯ ЛЮБОВЬ! Начать встречаться?» → `OPEN:Отн`, Отн +2),
            // свою Δ применяет — шкала откроется тиком позже, после ответа на её же вопрос.
            var ya03 = Card("YA03", 20, new ScaleDelta(Scale.Relationships, DeltaKind.Add, 2));
            ya03.Flags = new List<string> { "OPEN:" + ThanksNoThanks.Card.OpenRelations };
            var g = new Game(new[] { Starter(), ya03, Card("N", 40) }, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);

            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(55 + 12, g.Scales.Relationships, "«Отн +2» открывающей карточки = +12 п.п.");
        }

        // ---- 4. потолок и пол процентных шкал --------------------------------------------------------

        [Test]
        public void PercentScales_AreClampedToZeroHundred()
        {
            // При старом масштабе (±1…±3) переполнение было теоретическим, при новом (±35) — обычным.
            var heal = Card("H", 6, new ScaleDelta(Scale.Health, DeltaKind.Add, 3));
            var g = new Game(new[] { Starter(), heal, Card("N", 40) }, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);
            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(100, g.Scales.Health, "«Здр +3» со ста не даёт 130");

            var hit = Card("D", 6, new ScaleDelta(Scale.Health, DeltaKind.Set, 5));
            var hit2 = Card("D2", 7, new ScaleDelta(Scale.Health, DeltaKind.Add, -3));
            var g2 = new Game(new[] { Starter(), hit, hit2, Card("N", 40) }, coin: () => false);
            g2.StartLife();
            g2.HandleInput(GameInput.AnswerNo);
            g2.HandleInput(GameInput.AnswerYes);        // здоровье = 5
            g2.HandleInput(GameInput.AnswerYes);        // −30 → пол 0 → смерть, а не −25
            Assert.AreEqual(GameState.Finale, g2.State);
            Assert.AreEqual(0, g2.Scales.Health, "пол — ноль, а не отрицательное здоровье");
        }
    }
}
