using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// meeting-revisions §3 — таймер ответа по фазам возраста вместо единых 5 сек.
    /// Канон (финальные числа встречи 2026-07-29): 1–19 → 10 с · 20–29 → 8 с · 30–100 → 6 с ·
    /// блиц → 5 с (перебивает фазу). Здесь — точные числа на границах, приёмка §3 (15/25/40/блиц),
    /// длительность, которую реально получает карточка при раздаче, и «лингеринг-сим»: карточка
    /// живёт ровно свои секунды до таймаута, прогоняется настоящим <see cref="Game.Tick"/>, не моком.
    /// Семантика таймаута («молчание = случайный выбор») этим инкрементом не менялась.
    /// </summary>
    public class GameAnswerTimerTests
    {
        private static Card Plain(string id, int age) => new Card
        {
            Id = id, Question = id + "?", Age = age, Order = age,
            YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
            NoNecrolog = "жил дальше", Flags = new List<string>(),
        };

        // ---- чистая функция возраста: точные числа на границах фаз ----

        [TestCase(0, 10f)]     // до старта возрастного таймера — фаза A
        [TestCase(1, 10f)]     // A: нижняя граница детства
        [TestCase(18, 10f)]
        [TestCase(19, 10f)]    // A: верхняя граница
        [TestCase(20, 8f)]     // B: нижняя граница молодости
        [TestCase(25, 8f)]
        [TestCase(29, 8f)]     // B: верхняя граница
        [TestCase(30, 6f)]     // C: нижняя граница зрелости
        [TestCase(60, 6f)]
        [TestCase(100, 6f)]    // C: верхняя граница жизни
        public void AnswerSecondsFor_IsExact_AtEveryPhaseBoundary(int age, float expected)
        {
            Assert.AreEqual(expected, Game.AnswerSecondsFor(age),
                $"§3: возраст {age} → {expected} сек");
        }

        [Test]
        public void Acceptance_Section3_FifteenTenTwentyfiveEightFortySix_AndBlitzFive()
        {
            // Дословная приёмка §3: «на карточке в 15 лет таймер = 10 сек; в 25 = 8; в 40 = 6; в блице = 5».
            Assert.AreEqual(10f, Game.AnswerSecondsFor(15), "15 лет → 10 сек");
            Assert.AreEqual(8f, Game.AnswerSecondsFor(25), "25 лет → 8 сек");
            Assert.AreEqual(6f, Game.AnswerSecondsFor(40), "40 лет → 6 сек");
            Assert.AreEqual(5f, Game.AnswerSecondsFor(40, blitz: true), "блиц → 5 сек");
            Assert.AreEqual(5f, Game.BlitzSeconds, "константа блица — 5 сек (было 2)");
        }

        [TestCase(1)]
        [TestCase(19)]
        [TestCase(25)]
        [TestCase(47)]
        [TestCase(100)]
        public void Blitz_Overrides_ThePhase_AtEveryAge(int age)
        {
            Assert.AreEqual(Game.BlitzSeconds, Game.AnswerSecondsFor(age, blitz: true),
                "приоритет: блиц > фаза по возрасту");
        }

        [Test]
        public void PhaseConstants_AreTheFoundersFinalNumbers()
        {
            Assert.AreEqual(10f, Game.AnswerSecondsYouth);
            Assert.AreEqual(8f, Game.AnswerSecondsYoung);
            Assert.AreEqual(6f, Game.AnswerSecondsMature);
            Assert.AreEqual(20, Game.AnswerPhaseYoungFromAge);
            Assert.AreEqual(30, Game.AnswerPhaseMatureFromAge);
        }

        // ---- раздача карточки берёт длительность своей фазы ----

        [Test]
        public void DealtCard_GetsItsOwnPhaseTimer_FullOnDraw()
        {
            var g = new Game(new[] { Plain("A", 15), Plain("B", 25), Plain("C", 40) }, coin: () => false);
            g.StartLife();

            Assert.AreEqual("A?", g.CurrentCard.Question);
            Assert.AreEqual(10f, g.CardTimerMax, 1e-4f, "карточка 15 лет раздана с 10-секундным окном");
            Assert.AreEqual(10f, g.CardTimer, 1e-4f, "…и стартует с полной дугой");

            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(8f, g.CardTimerMax, 1e-4f, "карточка 25 лет → 8 сек");
            Assert.AreEqual(8f, g.CardTimer, 1e-4f);

            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(6f, g.CardTimerMax, 1e-4f, "карточка 40 лет → 6 сек");
            Assert.AreEqual(6f, g.CardTimer, 1e-4f);
        }

        [Test]
        public void Restart_ResetsTheTimerToTheFirstPhase()
        {
            var g = new Game(new[] { Plain("A", 40), Plain("B", 41) }, coin: () => false);
            g.StartLife();
            Assert.AreEqual(6f, g.CardTimerMax, 1e-4f);

            g.AbortToOpener();
            Assert.AreEqual(GameState.Opener, g.State);
            Assert.AreEqual(Game.AnswerSecondsYouth, g.CardTimerMax, 1e-4f,
                "рестарт сбрасывает купол на полную дугу первой фазы");
            Assert.AreEqual(0f, g.CardTimer, 1e-4f);
        }

        // ---- лингеринг-сим: карточка живёт РОВНО свои секунды (настоящий Tick, без мока) ----

        [TestCase(15, 10f)]
        [TestCase(25, 8f)]
        [TestCase(40, 6f)]
        public void LingerSim_CardTimesOut_AfterExactlyItsPhaseSeconds(int age, float seconds)
        {
            var g = new Game(new[] { Plain("A", age), Plain("B", age + 1) }, coin: () => false);
            g.StartLife();
            Assert.AreEqual("A?", g.CurrentCard.Question);

            // 0.1-секундными шагами почти до конца окна — карточка обязана стоять.
            float t = 0f;
            while (t < seconds - 0.15f)
            {
                g.Tick(0.1f);
                t += 0.1f;
                Assert.AreEqual("A?", g.CurrentCard.Question,
                    $"на {t:0.0} с из {seconds} карточка ещё жива");
            }
            Assert.AreEqual(seconds - t, g.CardTimer, 0.02f, "остаток совпадает с прошедшим временем");

            g.Tick(0.25f);   // переходим за границу окна
            Assert.AreEqual("B?", g.CurrentCard.Question,
                $"таймаут ровно на {seconds} с — молчание всё так же берёт случайный ответ");
        }

        [Test]
        public void LingerSim_ShorterPhase_DoesNotTimeOut_OnTheLongerPhasesClock()
        {
            // Регрессия на старую единую пятёрку: карточка в 15 лет ОБЯЗАНА пережить 5-секундную отметку.
            var g = new Game(new[] { Plain("A", 15), Plain("B", 16) }, coin: () => false);
            g.StartLife();
            for (int i = 0; i < 55; i++) g.Tick(0.1f);      // 5.5 с — старый таймер тут бы уже сработал
            Assert.AreEqual("A?", g.CurrentCard.Question, "10-секундная фаза не обрывается на пятой секунде");
            Assert.AreEqual(4.5f, g.CardTimer, 0.05f);
        }

        [Test]
        public void LingerSim_MaturePhase_DoesNotOutlive_ItsSixSeconds()
        {
            // …и обратная регрессия: зрелость НЕ получает старые 5 с и не растягивается до 10.
            var g = new Game(new[] { Plain("A", 40), Plain("B", 41) }, coin: () => false);
            g.StartLife();
            for (int i = 0; i < 65; i++) g.Tick(0.1f);      // 6.5 с
            Assert.AreEqual("B?", g.CurrentCard.Question, "6-секундная фаза истекла и перевела карту");
        }

        [Test]
        public void PhaseFollowsTheCard_AcrossAPhaseBoundary_InOneRun()
        {
            // Живой прогон: возраст реально едет (I03-подобный стартер), а окно каждой карточки —
            // её собственная фаза. Никакого мока: только Tick и настоящие ответы.
            var starter = Plain("S", 1);
            starter.StartsAgeTimer = true;
            var g = new Game(new[] { starter, Plain("Y", 19), Plain("Z", 20), Plain("W", 30) },
                coin: () => false);
            g.StartLife();
            Assert.AreEqual(10f, g.CardTimerMax, 1e-4f, "стартовая карточка — фаза A");

            g.HandleInput(GameInput.AnswerNo);   // стартер решён → возрастной таймер пошёл
            Assert.IsTrue(g.AgeRunning);
            Assert.AreEqual(10f, g.CardTimerMax, 1e-4f, "19 лет — ещё фаза A");
            for (int i = 0; i < 20; i++) g.Tick(0.1f);   // возраст догоняет карточку (12 лет/с)
            Assert.AreEqual(19f, g.Age, 0.01f, "возраст догнал карточку");

            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(8f, g.CardTimerMax, 1e-4f, "20 лет — фаза B");

            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(6f, g.CardTimerMax, 1e-4f, "30 лет — фаза C");
        }
    }
}
