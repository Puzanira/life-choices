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

        /// <summary>
        /// Пометить синтетическую карточку как ОТКРЫВАЮЩУЮ шкалу. Отрезок 0 ввёл правило «Δ только по
        /// открытым шкалам», а колоды этих тестов не доводят возраст до 20/25 — без пометки Δ по энергии
        /// или отношениям молча не применилась бы, и тест проверял бы пустоту. `OPEN:*` — ровно тот
        /// законный случай, который правило и знает (карточка применяет свою Δ, шкала открывается следом).
        /// </summary>
        private static Card Opening(Card c, string scale)
        {
            c.Flags = new List<string>(c.Flags) { "OPEN:" + scale };
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
        public void BlockCard_Affordable_ДА_SpendsExactlyThePrice_AndSkipsCsvMoneyDelta()
        {
            // NEW (block-cost-spend-and-show): ДА on an affordable BLOCK$ card spends EXACTLY the price
            // (MD03 = 60₽), and the card's CSV money-Δ («Дн −2») is NOT applied — the price is the single
            // authoritative money cost. Any non-money Δ (here «Эн +2») still applies.
            var open = Plain("OPEN", 18);
            var block = Opening(Block("MD03", 30), Card.OpenEnergy);
            block.YesDeltas = new[]
            {
                new ScaleDelta(Scale.Energy, DeltaKind.Add, 2),   // applies
                new ScaleDelta(Scale.Money, DeltaKind.Add, -2),   // DROPPED on BLOCK$ (price owns money)
            };
            block.YesNecrolog = "отпуск на море";
            var g = NewGame(() => false, open, block, Plain("N", 40));

            g.StartLife(); No(g); g.Tick(2f);        // open money
            for (int i = 0; i < 100; i++) Crank(g);  // bank ≥ 60₽
            Assert.GreaterOrEqual(g.Money, 60.0);

            No(g);                                    // OPEN → MD03 drawn while affordable
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked, "affordable BLOCK$ card is a normal card");
            Assert.AreEqual(60.0, g.CurrentCardPrice, Eps, "single source: shown/gate/spend price = 60");

            double m = g.Money;
            // Энергия просаживается заранее: Δ теперь разворачивается по канону отрезка 0 («Эн +2» = +18
            // п.п.), и с полной шкалы прибавка была бы съедена потолком 100 — тест перестал бы что-либо
            // проверять. Шкала при этом ОТКРЫТА (иначе Δ по ней не применяется — второе правило отрезка).
            g.Scales.Energy = 50;
            int e = g.Scales.Energy;
            int wantEnergy = DeltaScale.Resolve(Scale.Energy, DeltaKind.Add, 2);
            Yes(g);                                   // ДА → spend exactly 60, skip the −2, apply Эн +2
            Assert.AreEqual(m - 60.0, g.Money, Eps, "ДА spent exactly the price (−60), not −2 or −62");
            Assert.AreEqual(e + wantEnergy, g.Scales.Energy,
                "non-money Δ («Эн +2» → +18 п.п.) still applies on a BLOCK$ card");

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 50) No(g);
            CollectionAssert.Contains(g.Necrolog.StoryLines, "отпуск на море", "affordable card writes its line");
        }

        [Test]
        public void BlockCard_Affordable_ДА_Spend_MatchesEachPrice_AndAppliesHealth()
        {
            // Per-id spend + health/energy Δ for all three BLOCK$ ids, exactly per the CSV:
            //   MD03 −60 & Эн +2 · LT02 −120 & Здр +40 · LT08 −100 & Здр → 80%.
            // (Health is pre-dropped to 40 so LT02's «Здр<50» draw gate lets it through.)
            // MD03 «Эн +2» = +18 п.п. по канону отрезка 0; шкала стоит на 100 и упирается в потолок,
            // поэтому проверяем именно потолок (Δ применилась и не выкинула маркер за край).
            AssertSpend("MD03", 60, new[] { new ScaleDelta(Scale.Energy, DeltaKind.Add, 2) },
                g => Assert.AreEqual(100, g.Scales.Energy, "MD03 «Эн +2» упирается в потолок 100"),
                opens: Card.OpenEnergy);
            AssertSpend("LT02", 120, new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, 40) },
                g => Assert.AreEqual(80, g.Scales.Health, "LT02 heals +40 (40 → 80)"));
            AssertSpend("LT08", 100, new[] { new ScaleDelta(Scale.Health, DeltaKind.Set, 80) },
                g => Assert.AreEqual(80, g.Scales.Health, "LT08 sets health → 80%"));
        }

        // Health-drop card (Set): resolving ДА lowers health to a known value so LT02's «Здр<50» draw
        // gate is satisfied. Harmless for the money-only-gated ids (MD03/LT08).
        private static Card Drop(int age, int toHealth)
        {
            var c = Plain("DROP" + age, age);
            c.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Set, toHealth) };
            return c;
        }

        // Bank enough, drop health under LT02's gate, draw the BLOCK$ card affordable, answer ДА, assert
        // money dropped by EXACTLY the registered price and the health/energy side-effect landed.
        private static void AssertSpend(string id, double price, ScaleDelta[] yesDeltas,
                                        System.Action<Game> effectCheck, string opens = null)
        {
            var block = Block(id, 40);
            if (opens != null) Opening(block, opens);   // Δ по не-открытой шкале не применяется (отрезок 0)
            block.YesDeltas = yesDeltas;
            var g = NewGame(() => false, Plain("OPEN", 18), Drop(30, 40), block, Plain("N", 60));

            g.StartLife(); No(g); g.Tick(2f);        // open money
            for (int i = 0; i < 300; i++) Crank(g);  // bank well past 120₽
            No(g);                                    // OPEN → DROP
            Yes(g);                                   // DROP → health = 40 (LT02 gate needs < 50)
            Assert.AreEqual(id, g.CurrentCard.Id, id + " drawn (not gated off)");
            Assert.IsFalse(g.CurrentCardBlocked, id + " affordable");
            Assert.AreEqual(price, g.CurrentCardPrice, Eps, id + " price is the single source");

            double m = g.Money;
            Yes(g);
            Assert.AreEqual(m - price, g.Money, Eps, id + " spent exactly its price on ДА");
            effectCheck(g);
        }

        [Test]
        public void BlockCard_НЕТ_Affordable_SpendsNothing()
        {
            // НЕТ on an affordable BLOCK$ card declines the purchase → no spend (money-Δ also skipped).
            var open = Plain("OPEN", 18);
            var block = Block("MD03", 40);
            block.YesDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, -2) };
            block.NoDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, -5) }; // also dropped
            var g = NewGame(() => false, open, block, Plain("N", 60));

            g.StartLife(); No(g); g.Tick(2f);
            for (int i = 0; i < 200; i++) Crank(g);
            No(g);
            Assert.AreEqual("MD03", g.CurrentCard.Id);

            double m = g.Money;
            No(g);                                    // declined → nothing spent, no money-Δ
            Assert.AreEqual(m, g.Money, Eps, "НЕТ on a BLOCK$ card spends nothing");
        }

        [Test]
        public void BlockCard_Blocked_ДА_SpendsNothing_MoneyUnchanged()
        {
            // Unaffordable (money < price): ДА skips with no spend, no Δ (regression companion to the
            // existing no-necrolog/no-reroll test, focused on the money column).
            var open = Plain("OPEN", 18);
            var block = Block("MD03", 30);            // price 60₽
            var g = NewGame(() => false, open, block, Plain("N", 40));

            g.StartLife(); No(g); g.Tick(2f);         // open money, broke (< 60)
            No(g);
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "broke → blocked");

            double m = g.Money;
            Yes(g);
            Assert.AreEqual(m, g.Money, Eps, "blocked ДА spends nothing");
        }

        [Test]
        public void BlockCard_Blocked_НЕТ_SkipsCleanly_NoDelta_NoNecrolog()
        {
            // НЕТ on a blocked BLOCK$ card: skip with no spend, no Δ, no necrolog line, no re-roll.
            // MD03 at age 5 → money never opens (no cost-of-living), so «unchanged» is exact.
            var block = Block("MD03", 5);
            block.NoDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -40) };
            block.NoNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            var after = Plain("AFTER", 10);
            var g = NewGame(() => false, block, after);

            g.StartLife(); No(g);                     // start age timer → MD03 drawn (Money 0 < 60 → blocked)
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "broke → blocked");
            Assert.IsFalse(g.MoneyOpen, "money shut at this age → no drain, exact unchanged assert");

            int health = g.Scales.Health; double money = g.Money;
            No(g);                                     // НЕТ = skip
            Assert.AreEqual("AFTER", g.CurrentCard.Id, "skipped to the next card, not re-rolled");
            Assert.AreEqual(money, g.Money, Eps, "blocked НЕТ spends nothing");
            Assert.AreEqual(health, g.Scales.Health, "no Δ on the blocked НЕТ skip");

            Yes(g);                                    // finish → finale
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "НЕ ДОЛЖНО ПОПАСТЬ",
                "blocked НЕТ writes no necrolog line");
        }

        [Test]
        public void BlockCard_Blocked_Timeout_SkipsCleanly_NoDelta_NoNecrolog()
        {
            // Timeout (the card's PHASE window expires → random auto-answer; §3: 10/8/6 s by age, здесь
            // возраст 5 → 10 s) on a blocked BLOCK$ card behaves like any answer:
            // skip with no spend, no Δ, no necrolog line, no re-roll. MD03 at age 5 keeps money shut so
            // the timer's Tick can't drain cost-of-living — the «unchanged» assert stays exact.
            var block = Block("MD03", 5);
            block.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -40) };
            block.NoDeltas  = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -40) };
            block.YesNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            block.NoNecrolog  = "НЕ ДОЛЖНО ПОПАСТЬ";
            var after = Plain("AFTER", 10);
            var g = NewGame(() => false, block, after);

            g.StartLife(); No(g);                     // MD03 drawn, blocked (Money 0 < 60)
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "broke → blocked");

            int health = g.Scales.Health; double money = g.Money;
            g.Tick(g.CardTimerMax + 0.1f);            // let the phase timer expire → timeout auto-answer
            Assert.AreEqual("AFTER", g.CurrentCard.Id, "timeout on a blocked card just skips");
            Assert.AreEqual(money, g.Money, Eps, "blocked timeout spends nothing");
            Assert.AreEqual(health, g.Scales.Health, "no Δ on the blocked timeout skip");

            Yes(g);
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "НЕ ДОЛЖНО ПОПАСТЬ",
                "timed-out blocked card writes no necrolog line");
        }

        [Test]
        public void BlockPrice_SingleSource_Gate_Spend_Display_AllAgree()
        {
            // The gate threshold (blocked iff money < price), the ДА spend, and the displayed number
            // are the SAME value for every BLOCK$ id — sourced from Game.BlockPrices.
            foreach (var kv in Game.BlockPrices)
            {
                string id = kv.Key; double price = kv.Value;

                // Just-below the price → blocked (gate reads the same number). Health pre-dropped to 40 so
                // LT02's «Здр<50» draw gate lets it through (money-only ids are unaffected).
                var block = Block(id, 40);
                var g = NewGame(() => false, Plain("OPEN", 18), Drop(30, 40), block, Plain("N", 60));
                g.StartLife(); No(g); g.Tick(2f);
                int ticks = (int)price - 1;           // bank ~price−1 (money < price)
                for (int i = 0; i < ticks; i++) Crank(g);
                No(g);                                 // OPEN → DROP
                Yes(g);                                // DROP → health 40, then draw the BLOCK$ card
                Assert.AreEqual(id, g.CurrentCard.Id, id + " drawn");
                Assert.Less(g.Money, price, id + " under price");
                Assert.IsTrue(g.CurrentCardBlocked, id + " gate uses the price");
                Assert.AreEqual(price, g.CurrentCardPrice, Eps, id + " displayed price == gate price == spend");
            }
        }

        [Test]
        public void BlockedCard_LiveAffordability_CrankDuringTimer_UNBLOCKS_AndTheYesGoesThrough()
        {
            // ⚠ ПРАВИЛО ПЕРЕВЁРНУТО В r6 п.2 (решение основательницы, живой плейтест 2026-09-22).
            // До r6 доступность была СНИМКОМ «на момент показа»: игрок докручивал нужную сумму прямо
            // под запертой карточкой, видел её на счету — и карточка всё равно оставалась запертой до
            // таймаута. Прежний гард ровно это и закреплял («still blocked — affordability snapshot is
            // at draw time»), то есть охранял саму жалобу. Теперь доступность ЖИВАЯ: накрутил до цены —
            // карточка ожила, ДА проходит, списывает цену и применяет Δ с некрологом, как у обычной.
            var open = Plain("OPEN", 18);
            var block = Block("MD03", 30);           // price 60₽
            block.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -50) };
            block.YesNecrolog = "КУПИЛ НА ДОКРУЧЕННЫЕ";
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, block, after);

            g.StartLife(); No(g); g.Tick(2f);        // open money (broke)
            No(g);                                   // OPEN resolved → MD03 drawn blocked
            Assert.AreEqual("MD03", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "drawn while broke → blocked");

            for (int i = 0; i < 200; i++) Crank(g);  // crank during the card's phase window (age 5 → 10 s)
            Assert.GreaterOrEqual(g.Money, 60.0, "now affordable in raw money terms");
            Assert.IsFalse(g.CurrentCardBlocked,
                "ОЖИЛА: доступность считается от ТЕКУЩИХ денег, а не от снимка выдачи");

            int health = g.Scales.Health;
            double money = g.Money;
            Yes(g);                                  // …и ДА теперь проходит как у обычной карточки
            Assert.AreEqual("AFTER", g.CurrentCard.Id, "карточка разрешилась и уступила следующей");
            Assert.AreEqual(health - 50, g.Scales.Health, "Δ применена — это НЕ пропуск");
            Assert.AreEqual(money - 60.0, g.Money, Eps, "списана ровно цена BLOCK$");

            Yes(g);                                  // finish → finale
            CollectionAssert.Contains(g.Necrolog.StoryLines, "КУПИЛ НА ДОКРУЧЕННЫЕ",
                "строка некролога есть — карточка была сыграна, а не пропущена");
        }

        [Test]
        public void AffordableCard_LiveAffordability_CostOfLivingDuringTimer_RE_BLOCKS_It()
        {
            // …и ТА ЖЕ ЖИВАЯ ДОСТУПНОСТЬ В ОБРАТНУЮ СТОРОНУ (r6 п.2): карточка пришла по карману, но
            // стоимость жизни капает, пока игрок думает. Просела ниже цены — карточка запирается
            // обратно, и ДА снова уходит пропуском без Δ и без строки некролога.
            // ДЁШЕВАЯ карточка взята намеренно: запас над ценой надо проесть стоимостью жизни
            // (0.5 ₽/с) В ПРЕДЕЛАХ ЖИЗНИ КАРТОЧКИ — у возраста 30 это 6 с (AnswerSecondsMature),
            // то есть окно всего на 3 ₽. На 60-рублёвой цене запас проедался бы пять минут, и
            // карточка ушла бы по таймауту раньше, чем вернулась бы блокировка.
            var open = Plain("OPEN", 18);
            var block = Block("FA02", 30);           // price 10₽
            block.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -50) };
            block.YesNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, block, after);

            g.StartLife(); No(g); g.Tick(2f);        // open money (broke)
            while (g.Money < 11.0) Crank(g);         // ЧУТЬ выше цены — запас на один-два рубля
            No(g);                                   // OPEN resolved → FA02 drawn AFFORDABLE
            Assert.AreEqual("FA02", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked, "пришла по карману");

            // …и проедаем запас стоимостью жизни, не трогая карточку.
            double over = g.Money - 10.0;
            Assert.Greater(over, 0.0, "запас над ценой действительно есть");
            float eat = (float)(over / Game.CostOfLivingPerSec) + 0.4f;
            Assert.Less(eat, Game.AnswerSecondsMature, "проедание укладывается в жизнь карточки");
            g.Tick(eat);

            Assert.Less(g.Money, 10.0, "стоимость жизни съела разницу");
            Assert.IsTrue(g.CurrentCardBlocked, "ЗАПЕРЛАСЬ ОБРАТНО — пересчёт живой в обе стороны");

            int health = g.Scales.Health;
            double money = g.Money;
            Yes(g);
            Assert.AreEqual(health, g.Scales.Health, "Δ не применена — это пропуск");
            Assert.AreEqual(money, g.Money, Eps, "ничего не списано с запертой");

            Yes(g);
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "НЕ ДОЛЖНО ПОПАСТЬ",
                "строки некролога нет — карточка ушла пропуском");
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

        // «active» = a COMPETENT player working all their hands: cranks money AND breathes to hold
        // energy (post-Gate-2-r3 energy needs breathing or you burn out and die). «lazy» does neither —
        // it coasts, so it slides into burnout/energy death and ends in the red. This keeps the smoke a
        // clean money-balance contrast (engaged banks money over a full life; disengaged does not).
        private static double RunLifeMoney(bool active)
        {
            var g = FillerLife();
            g.StartLife(); No(g);                     // start the age timer
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 100000)
            {
                g.Tick(0.25f);                        // 0.25s steps
                if (active && g.MoneyOpen) g.HandleInput(GameInput.MoneyTick);  // ≈4 ticks/s focused
                // Датчик высоты держим, когда батарея просела ниже половины: рука уходит с крутилки, и
                // это и есть цена энергии в новой механике (2026-08-07 — прежде это был ритм-вдох).
                if (active && g.EnergyOpen && g.Scales.Energy < 50) g.HandleInput(GameInput.EnergyHold);
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
