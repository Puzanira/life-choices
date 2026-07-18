using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Live money economy on the pure <see cref="Game"/> spine: crank income × multiplier stack,
    /// cost-of-living + installment drains (real-time, dt-injected), the tutorial pause freeze,
    /// BLOCK$ affordability, restart reset, and a balance smoke. Time is injected — never wall-clock.
    /// </summary>
    public class GameMoneyTests
    {
        private const double Eps = 1e-6;

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Mult(string id, int age, double val, int fromAge, bool randomZero = false)
        {
            var c = Plain(id, age);
            c.LongEffects = new[]
            {
                new LongEffect { Kind = LongEffectKind.Mult, Scale = Scale.Money, MultValue = val, FromAge = fromAge, RandomZero = randomZero }
            };
            return c;
        }

        private static Card Drain(string id, int age, double perSec, int durYears)
        {
            var c = Plain(id, age);
            c.LongEffects = new[]
            {
                new LongEffect { Kind = LongEffectKind.Drain, Scale = Scale.Money, DrainPerSec = perSec, DurYears = durYears }
            };
            return c;
        }

        private static Card Block(string id, int age)
        {
            var c = Plain(id, age);
            c.IsBlockCost = true;
            return c;
        }

        // Deck = an age-timer starter (I03) + the given cards (must be handed in ascending age).
        private static Game NewGame(System.Func<bool> coin, params Card[] cards)
        {
            var starter = Plain("I03", 1);
            starter.StartsAgeTimer = true;
            var deck = new List<Card> { starter };
            deck.AddRange(cards);
            return new Game(deck, coin: coin);
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);
        private static void Crank(Game g) => g.HandleInput(GameInput.MoneyTick);

        // ---- opening ----

        [Test]
        public void Money_Opens_AtAge18_NotBefore()
        {
            var g = NewGame(() => false, Plain("A", 40));
            g.StartLife();
            No(g);                       // resolve starter → age running
            Assert.IsFalse(g.MoneyOpen, "money shut before 18");
            g.Tick(1f);                  // age 0 → 12 (still < 18)
            Assert.IsFalse(g.MoneyOpen, "still shut at 12");
            g.Tick(1f);                  // age → 18 (capped at the card's age 40 catch-up)
            Assert.IsTrue(g.MoneyOpen, "money opens once age reaches 18");
        }

        [Test]
        public void Crank_NoOp_BeforeMoneyOpens()
        {
            var g = NewGame(() => false, Plain("A", 40));
            g.StartLife();
            No(g);
            double before = g.Money;
            Crank(g);
            Assert.AreEqual(before, g.Money, Eps, "crank does nothing before money opens");
        }

        // ---- income × multiplier stack ----

        [Test]
        public void CrankIncome_Is_OnePerTick_TimesMultiplier()
        {
            var g = NewGame(() => true, Plain("A", 40));
            g.StartLife(); No(g); g.Tick(2f);       // open money at 18
            Assert.IsTrue(g.MoneyOpen);
            double before = g.Money;
            Crank(g);
            Assert.AreEqual(before + 1.0, g.Money, Eps, "base tick = +1₽ ×1");
        }

        [Test]
        public void MultiplierStack_University_FromAge25_And_CityStacks()
        {
            var uni = Mult("YA01", 18, 2.0, 25);     // ×2 from 25
            var mid = Plain("MID", 24);
            var city = Mult("YA06", 26, 1.5, 0);     // ×1.5 immediately (stacks)
            var late = Plain("LATE", 40);
            var g = NewGame(() => true, uni, mid, city, late);

            g.StartLife(); No(g); g.Tick(2f);        // → age 18, open
            Assert.IsTrue(g.MoneyOpen);
            Yes(g);                                  // YA01 ДА → ×2 FROM:25 armed
            g.Tick(1f);                              // → age 24 (< 25)
            Assert.AreEqual(1.0, g.IncomeMultiplier, Eps, "university mult inert before 25");

            No(g);                                   // resolve MID → current = YA06 (26)
            g.Tick(1f);                              // → age 26 (>= 25)
            Assert.AreEqual(2.0, g.IncomeMultiplier, Eps, "×2 active from 25");

            Yes(g);                                  // YA06 ДА → ×1.5 stacks
            Assert.AreEqual(3.0, g.IncomeMultiplier, Eps, "×2 × ×1.5 = ×3 (stacks multiplicatively)");

            double before = g.Money;
            Crank(g);
            Assert.AreEqual(before + 3.0, g.Money, Eps, "tick pays +1 × 3");
        }

        [Test]
        public void Startup_RandomOutcome_Win_GivesFiveX()
        {
            var g = NewGame(() => true, Mult("YA02", 18, 5.0, 0, randomZero: true), Plain("N", 40));
            g.StartLife(); No(g); g.Tick(2f);
            Yes(g);                                  // coin=true → win → ×5 income multiplier
            Assert.AreEqual(5.0, g.IncomeMultiplier, Eps, "startup win → ×5");
        }

        [Test]
        public void Startup_RandomOutcome_Loss_WipesMoneyToZero()
        {
            var g = NewGame(() => false, Mult("YA02", 18, 5.0, 0, randomZero: true), Plain("N", 40));
            g.StartLife(); No(g); g.Tick(2f);
            for (int i = 0; i < 30; i++) Crank(g);   // bank some money first
            Assert.Greater(g.Money, 0.0);
            Yes(g);                                  // coin=false → loss → обнуление денег
            Assert.AreEqual(0.0, g.Money, Eps, "startup loss wipes money to 0");
            Assert.AreEqual(1.0, g.IncomeMultiplier, Eps, "no multiplier gained on the wipe branch");
        }

        // ---- cost of living + installment drains (real-time) ----

        [Test]
        public void CostOfLiving_Drains_HalfRublePerSecond()
        {
            var g = NewGame(() => false, Plain("A", 40));
            g.StartLife(); No(g); g.Tick(2f);        // open money
            double before = g.Money;
            g.Tick(1f);                              // one real second
            Assert.AreEqual(before - Game.CostOfLivingPerSec, g.Money, 1e-4, "−0.5₽/сек cost of living");
        }

        [Test]
        public void InstallmentDrain_ActiveOnlyInsideItsGameYearWindow()
        {
            var drain = Drain("YA04", 20, -0.3, 5);  // window [20, 25) game-years, −0.3₽/сек
            var next = Plain("N", 26);
            var g = NewGame(() => false, drain, next);

            g.StartLife(); No(g); g.Tick(2f);        // → age ~20, open money
            Assert.That((double)g.Age, Is.EqualTo(20).Within(0.01), "positioned at 20");
            Yes(g);                                  // YA04 ДА → drain armed for [20,25)

            double b1 = g.Money;
            g.Tick(0.1f);                            // age 20→21.2, inside window: cost 0.5 + drain 0.3
            Assert.AreEqual(0.08, b1 - g.Money, 1e-3, "in-window drop = (0.5+0.3)/s");

            int guard = 0;
            while (g.Age < 25.5f && guard++ < 200) g.Tick(0.1f);   // push age past the window end

            double b2 = g.Money;
            g.Tick(0.1f);                            // past 25: cost only, drain expired
            Assert.AreEqual(0.05, b2 - g.Money, 1e-3, "after window: cost 0.5/s only, no drain");
        }

        // ---- tutorial pause freezes age + drains + timer ----

        [Test]
        public void Paused_Freezes_Age_Drains_And_Timer()
        {
            var g = NewGame(() => false, Drain("YA04", 20, -0.3, 30), Plain("N", 60));
            g.StartLife(); No(g); g.Tick(2f);        // age ~20, open
            Yes(g);                                  // drain armed (long window)
            g.Tick(0.2f);                            // let a beat pass so timer/age moved

            float age0 = g.Age; double money0 = g.Money; float timer0 = g.CardTimer;
            g.Paused = true;
            g.Tick(5f);                              // a big frozen step
            Assert.AreEqual(age0, g.Age, "age frozen while paused");
            Assert.AreEqual(money0, g.Money, Eps, "money (cost+drain) frozen while paused");
            Assert.AreEqual(timer0, g.CardTimer, "card timer frozen while paused");
            Assert.AreEqual(GameState.Playing, g.State, "no timeout fired under the pause");

            g.Paused = false;
            g.Tick(0.1f);
            Assert.Greater(g.Age, age0, "age resumes after unpause");
            Assert.Less(g.Money, money0, "drains resume after unpause");
        }

        // ---- BLOCK$ (S10) ----

        [Test]
        public void BlockCard_Unaffordable_SkipsWithNoDeltaNoNecrologNoReroll()
        {
            var open = Plain("OPEN", 18);
            var block = Block("MD03", 30);           // price 60₽
            block.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -50) };
            block.YesNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, block, after);

            g.StartLife(); No(g); g.Tick(2f);        // open money (broke: money < 60)
            No(g);                                   // resolve OPEN → MD03 drawn
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "unaffordable BLOCK$ card is blocked");

            int health = g.Scales.Health; double money = g.Money;
            Yes(g);                                  // any answer on a blocked card = skip
            Assert.AreEqual("AFTER", g.CurrentCard.Id, "skipped straight to the next card");
            Assert.AreEqual(health, g.Scales.Health, "no Δ applied on the blocked skip");
            Assert.AreEqual(money, g.Money, Eps, "no money Δ on the blocked skip");

            Yes(g);                                  // finish AFTER → deck ends
            Assert.AreEqual(GameState.Finale, g.State);
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "НЕ ДОЛЖНО ПОПАСТЬ",
                "blocked card writes no necrolog line and is not re-rolled");
        }

        [Test]
        public void BlockCard_Affordable_IsNormal_AndAppliesDelta()
        {
            var open = Plain("OPEN", 18);
            var block = Block("MD03", 30);
            block.YesDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, -2) };
            block.YesNecrolog = "отпуск на море";
            var g = NewGame(() => false, open, block, Plain("N", 40));

            g.StartLife(); No(g); g.Tick(2f);        // open money
            for (int i = 0; i < 100; i++) Crank(g);  // bank ≥ 60₽
            Assert.GreaterOrEqual(g.Money, 60.0);

            No(g);                                    // OPEN → MD03 drawn while affordable
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked, "affordable BLOCK$ card is a normal card");

            double m = g.Money;
            Yes(g);                                   // normal ДА applies its Δ (includes the cost)
            Assert.AreEqual(m - 2.0, g.Money, Eps, "ДА applied the card's money Δ");

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 50) No(g);
            CollectionAssert.Contains(g.Necrolog.StoryLines, "отпуск на море", "affordable card writes its line");
        }

        [Test]
        public void BlockedCard_AffordabilitySnapshot_CrankDuringTimer_DoesNotUnblock()
        {
            // BLOCK$ is fixed «на момент показа»: cranking past the price while the blocked card is
            // up must NOT unblock it — any answer still skips with no Δ and no necrolog line.
            var open = Plain("OPEN", 18);
            var block = Block("MD03", 30);           // price 60₽
            block.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -50) };
            block.YesNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, block, after);

            g.StartLife(); No(g); g.Tick(2f);        // open money (broke)
            No(g);                                   // OPEN resolved → MD03 drawn blocked
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "drawn while broke → blocked");

            for (int i = 0; i < 200; i++) Crank(g);  // crank during the card's 5s window
            Assert.GreaterOrEqual(g.Money, 60.0, "now affordable in raw money terms");
            Assert.IsTrue(g.CurrentCardBlocked, "still blocked — affordability snapshot is at draw time");

            int health = g.Scales.Health;
            Yes(g);                                  // any answer = skip regardless of the new balance
            Assert.AreEqual("AFTER", g.CurrentCard.Id, "skipped to the next card");
            Assert.AreEqual(health, g.Scales.Health, "no Δ applied on the late-crank skip");

            Yes(g);                                  // finish → finale
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "НЕ ДОЛЖНО ПОПАСТЬ",
                "no necrolog line for the still-blocked card");
        }

        [Test]
        public void ConcurrentDrains_CreditAndMortgage_IntegrateTogether()
        {
            // Кредит + ипотека active at once: both installments and cost-of-living integrate in the
            // same real-second while their game-year windows overlap.
            var credit = Drain("YA04", 20, -0.3, 30);
            var mortgage = Drain("MD04", 21, -0.3, 30);
            var next = Plain("N", 60);
            var g = NewGame(() => false, credit, mortgage, next);

            g.StartLife(); No(g); g.Tick(2f);        // → age 20, money open
            Yes(g);                                  // credit drain armed [20, 50)
            g.Tick(0.1f);                            // catch-up → age 21 (mortgage card up)
            Yes(g);                                  // mortgage drain armed [21, 51)

            double before = g.Money;
            g.Tick(0.1f);                            // both windows active: 0.5 + 0.3 + 0.3 = 1.1/s
            Assert.AreEqual(0.11, before - g.Money, 1e-3, "cost + both drains integrate together");
        }

        [Test]
        public void Finale_FreezesMoney_DrainsStopAtLifeEnd()
        {
            var credit = Drain("YA04", 20, -0.3, 30);
            var g = NewGame(() => false, credit, Plain("N", 25));

            g.StartLife(); No(g); g.Tick(2f);        // open money
            Yes(g);                                  // drain armed and running
            No(g);                                   // resolve N → deck ends → finale
            Assert.AreEqual(GameState.Finale, g.State);

            double frozen = g.Money;
            g.Tick(10f);                             // a long dead beat
            Assert.AreEqual(frozen, g.Money, Eps, "no cost-of-living or drains after life end");
        }

        // ---- restart reset ----

        [Test]
        public void Restart_Resets_Money_Multipliers_Drains_And_Open()
        {
            var g = NewGame(() => true, Mult("YA06", 18, 1.5, 0), Plain("N", 40));
            g.StartLife(); No(g); g.Tick(2f);
            Yes(g);                                   // multiplier armed
            for (int i = 0; i < 10; i++) Crank(g);    // money banked
            Assert.Greater(g.Money, 0.0);

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 50) No(g);   // to finale
            g.HandleInput(GameInput.Confirm);         // → opener
            g.HandleInput(GameInput.Confirm);         // → fresh life

            Assert.AreEqual(0.0, g.Money, Eps, "money reset");
            Assert.IsFalse(g.MoneyOpen, "money closed again");
            Assert.AreEqual(1.0, g.IncomeMultiplier, Eps, "multipliers cleared");
            double before = g.Money;
            Crank(g);
            Assert.AreEqual(before, g.Money, Eps, "no crank income before re-opening (drains/mults cleared)");
        }

        // ---- balance smoke: active cranker ends positive, lazy run ends negative (loose bounds) ----

        private static Game FillerLife()
        {
            var deck = new List<Card>();
            var starter = Plain("I03", 1); starter.StartsAgeTimer = true;
            deck.Add(starter);
            for (int a = 18; a <= 84; a += 3) deck.Add(Plain("F" + a, a));
            return new Game(deck, coin: () => false);
        }

        private static double RunLifeMoney(bool active)
        {
            var g = FillerLife();
            g.StartLife(); No(g);                     // start the age timer
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 100000)
            {
                g.Tick(0.25f);                        // 0.25s steps
                if (active && g.MoneyOpen) g.HandleInput(GameInput.MoneyTick);  // ≈4 ticks/s focused
            }
            return g.Money;
        }

        [Test]
        public void BalanceSmoke_ActiveCrankerPositive_LazyNegative()
        {
            double active = RunLifeMoney(active: true);
            double lazy = RunLifeMoney(active: false);
            Assert.Greater(active, 50.0, $"active cranker ends comfortably positive (got {active:0})");
            Assert.Less(lazy, 0.0, $"lazy run ends in the red (got {lazy:0})");
            Assert.Greater(active, lazy, "cranking beats coasting");
        }
    }
}
