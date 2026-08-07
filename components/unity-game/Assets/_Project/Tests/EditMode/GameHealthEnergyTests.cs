using System;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The live health + energy layer on the pure <see cref="Game"/> spine (dt-injected, never wall-clock):
    /// health decay from 30 (×LT01 modifier, KEK04 bonus), the LT08 system trigger and LT02 eligibility,
    /// energy drain from 25 + HELD-sensor regen («зажми и держи», редизайн 2026-08-07), burnout
    /// enter/income/exit/re-enter, the two burnout endings, clean-run survival calibration, the open events
    /// at 25/30 + hint-pause freeze, and restart.
    /// </summary>
    public class GameHealthEnergyTests
    {
        private const double Eps = 1e-9;

        // ---- card builders ----
        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        private static Card WithNoDelta(Card c, Scale s, DeltaKind k, int v)
        {
            c.NoDeltas = new[] { new ScaleDelta(s, k, v) };
            return c;
        }

        private static Card WithYesDelta(Card c, Scale s, DeltaKind k, int v)
        {
            c.YesDeltas = new[] { new ScaleDelta(s, k, v) };
            return c;
        }

        // Deck = I03 starter + the given cards (ascending age). Raw-deck constructor.
        private static Game NewGame(Func<bool> coin, params Card[] cards)
        {
            var deck = new List<Card> { Starter() };
            deck.AddRange(cards);
            return new Game(deck, coin: coin);
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);
        private static void Crank(Game g) => g.HandleInput(GameInput.MoneyTick);
        /// <summary>Датчик высоты поднят В ЭТОМ такте (сигнал непрерывный — латч живёт ровно один Tick).</summary>
        private static void Hold(Game g) => g.HandleInput(GameInput.EnergyHold);

        /// <summary>
        /// Колода, на которой ВОЗРАСТ ОСТАЁТСЯ НИЗКИМ (26…40): возраст событийный, он замирает на возрасте
        /// текущей карточки, поэтому за секунды теста нельзя случайно доехать до кризиса (45) и заморозить
        /// им энергию. Нужна каждому тесту, который РЕАЛЬНО тикает время с открытой энергией.
        /// </summary>
        private static Card[] EnergyFiller()
        {
            var cards = new List<Card>();
            for (int a = 26; a <= 40; a++) cards.Add(Plain("F" + a, a));
            return cards.ToArray();
        }

        /// <summary>
        /// Довести жизнь до ОТКРЫТОЙ энергии МЕЛКИМИ тактами. Один жирный Tick(2.2f) сюда не годится:
        /// открытие и дренаж случаются в ОДНОМ такте, и шкала стартовала бы уже просевшей на пару процентов.
        /// </summary>
        private static void OpenEnergy(Game g)
        {
            int guard = 0;
            while (!g.EnergyOpen && guard++ < 4000) g.Tick(0.05f);
            Assert.IsTrue(g.EnergyOpen, "сим обязан дойти до открытия энергии");
        }

        /// <summary>Держать датчик <paramref name="seconds"/> секунд подряд, тактами по 0.1 с.</summary>
        private static void HoldFor(Game g, float seconds)
        {
            for (float t = 0; t < seconds && g.State == GameState.Playing; t += 0.1f)
            {
                Hold(g);
                g.Tick(0.1f);
            }
        }

        // ================================================================ health decay

        [Test]
        public void HealthDecay_DoesNotStartBefore30()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.StartLife(); No(g);
            g.Tick(2.4f);                       // age ≈ 28.8 (< 30)
            Assert.IsFalse(g.HealthDecaying, "health not decaying before 30");
            Assert.AreEqual(100, g.Scales.Health, "full health up to 30");
        }

        [Test]
        public void HealthDecay_StartsAt30_AndDropsOverTime()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.StartLife(); No(g);
            g.Tick(2.5f);                       // age → 30.0 → decay begins this frame
            Assert.IsTrue(g.HealthDecaying, "health decays from exactly 30");
            int h = g.Scales.Health;
            g.Tick(2.0f);                       // ~1.4% more (0.7/s), still inside the card's phase window (age 90 → 6 s)
            Assert.Less(g.Scales.Health, h, "health keeps dropping past 30");
            Assert.That(g.Scales.Health, Is.InRange(95, 99), "≈0.7%/s baseline decay (sane bound)");
        }

        // Isolate the decay MODIFIER: a bare LT01 (no Δ) sets ×0.5 (ДА) / ×2 (НЕТ); a plain card = ×1.
        private static int MeasureDropOverWindow(bool answerYes, bool useLt01)
        {
            var mid = useLt01 ? Plain("LT01", 31) : Plain("P", 31);
            var g = NewGame(() => false, mid, Plain("LONG", 90));
            g.StartLife(); No(g);
            g.Tick(3f);                         // age → 31 (decay running, mult still 1 here)
            if (answerYes) Yes(g); else No(g);  // resolve mid → LT01 sets the modifier
            int h0 = g.Scales.Health;
            g.Tick(4f);                         // measured window under the card's phase timer (LONG в 90 → 6 s)
            return h0 - g.Scales.Health;
        }

        [Test]
        public void Lt01_Neglect_DoublesDecay_Care_HalvesIt()
        {
            int care = MeasureDropOverWindow(answerYes: true, useLt01: true);   // ×0.5
            int baseline = MeasureDropOverWindow(answerYes: false, useLt01: false); // ×1
            int neglect = MeasureDropOverWindow(answerYes: false, useLt01: true);  // ×2

            Assert.Less(care, baseline, "LT01=ДА «занялся» halves the decay");
            Assert.Greater(neglect, baseline, "LT01=НЕТ «забросил» doubles the decay");
            Assert.GreaterOrEqual(neglect, care * 2, "neglect ≈2× vs care ≈0.5× around the baseline");
        }

        [Test]
        public void Kek04_Yes_GivesOneShotHealthBonus_EvenThoughNoCons()
        {
            // KEK04 is a NOCONS card carrying a (contrived) Δ Здр+1 that must be SKIPPED; the +5 system
            // bonus rides the card-id special path instead, so only +5 lands (not +1).
            var kek = Plain("KEK04", 32);
            kek.IsNoCons = true;
            kek.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, 1) };

            var hit = WithNoDelta(Plain("HIT", 31), Scale.Health, DeltaKind.Set, 50);
            var g = NewGame(() => false, hit, kek, Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(3f);                          // age 31
            No(g);                               // HIT → health set to 50
            int before = g.Scales.Health;
            Assert.AreEqual(50, before, "health set to 50 by HIT");
            Yes(g);                              // KEK04 ДА → +5 (NOT +1)
            Assert.AreEqual(55, g.Scales.Health, "KEK04=ДА is a +5 bonus; the NOCONS Δ is not applied");
        }

        [Test]
        public void Kek04_No_GivesNothing()
        {
            var kek = Plain("KEK04", 32);
            kek.IsNoCons = true;
            var hit = WithNoDelta(Plain("HIT", 31), Scale.Health, DeltaKind.Set, 50);
            var g = NewGame(() => false, hit, kek, Plain("L", 90));
            g.StartLife(); No(g); g.Tick(3f); No(g);
            int before = g.Scales.Health;
            No(g);                               // KEK04 НЕТ → no bonus
            Assert.AreEqual(before, g.Scales.Health, "KEK04=НЕТ leaves health untouched");
        }

        // ================================================================ LT08 system trigger

        private static Card MakeLt08()
        {
            var c = new Card
            {
                Id = "LT08", Question = "LT08?", Age = 35, Order = 100,
                Flags = new List<string> { "BLOCK$" }, IsBlockCost = true,
                YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Set, 80) },
                NoDeltas = Array.Empty<ScaleDelta>(),
            };
            return c;
        }

        private static Game Lt08Game(Func<bool> coin, out Card lt08)
        {
            lt08 = MakeLt08();
            var hit = WithNoDelta(Plain("HIT", 31), Scale.Health, DeltaKind.Set, 30);
            var plan = new DeckPlan
            {
                Deck = new List<Card> { Starter(), hit, Plain("END", 90) },
                Reserve = new List<Card>(),
                Lt08 = lt08,
            };
            var captured = lt08;
            return new Game(() => new DeckPlan { Deck = new List<Card>(plan.Deck), Reserve = new List<Card>(), Lt08 = captured }, coin);
        }

        [Test]
        public void Lt08_Triggers_WhenHealthBelow40AndAge30_BlockedWhenPoor_SingleShot()
        {
            var g = Lt08Game(() => false, out _);
            g.StartLife(); No(g);
            g.Tick(3f);                          // age → 31, HIT up
            Assert.AreEqual("HIT", g.CurrentCard.Id);
            No(g);                               // HIT → health 30 (<40) at age≥30 → LT08 inserted next
            Assert.AreEqual("LT08", g.CurrentCard.Id, "«Пора подлечиться!» triggered by the condition");
            Assert.IsTrue(g.CurrentCardBlocked, "BLOCK$ 100₽ blocks it while broke (money 0)");

            No(g);                               // blocked → skip, no heal
            Assert.AreEqual("END", g.CurrentCard.Id, "skipped straight on after the blocked heal");
            Assert.AreEqual(30, g.Scales.Health, "no heal on the blocked skip");

            No(g);                               // END → deck ends; LT08 не всплывает второй раз
            Assert.AreEqual(GameState.Finale, g.State, "single-shot: LT08 не переспавнится");
        }

        [Test]
        public void Lt08_Paid_HealsToEighty()
        {
            var g = Lt08Game(() => false, out _);
            g.StartLife(); No(g);
            g.Tick(2f);                          // age ≈ 24, money open (≥18)
            for (int i = 0; i < 150; i++) Crank(g); // bank ≥ 100₽
            Assert.GreaterOrEqual(g.Money, 100.0);
            g.Tick(1f);                          // age → 31, HIT up
            No(g);                               // HIT → health 30 → LT08 inserted
            Assert.AreEqual("LT08", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked, "affordable now (≥100₽) → not blocked");
            Yes(g);                              // pay → heal
            Assert.AreEqual(80, g.Scales.Health, "LT08=ДА sets health to 80%");
        }

        [Test]
        public void Lt08_NotTriggered_WhenHealthy()
        {
            // Health stays ≥40 → LT08 never inserted; the run ends on the normal deck.
            var lt08 = MakeLt08();
            var plan = new DeckPlan
            {
                Deck = new List<Card> { Starter(), Plain("A", 31), Plain("END", 90) },
                Reserve = new List<Card>(),
                Lt08 = lt08,
            };
            var g = new Game(() => new DeckPlan { Deck = new List<Card>(plan.Deck), Lt08 = lt08, Reserve = new List<Card>() }, () => false);
            g.StartLife(); No(g);
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 50)
            {
                Assert.AreNotEqual("LT08", g.CurrentCard?.Id, "LT08 never appears while healthy");
                No(g);
            }
            Assert.AreEqual(GameState.Finale, g.State);
        }

        // ================================================================ LT02 eligibility (<50%)

        private static Card MakeLt02(int age)
        {
            var c = new Card
            {
                Id = "LT02", Question = "LT02?", Age = age, Order = 200,
                Flags = new List<string> { "BLOCK$" }, IsBlockCost = true,
                YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, 2) },
                NoDeltas = Array.Empty<ScaleDelta>(),
            };
            return c;
        }

        [Test]
        public void Lt02_Skipped_WhenHealthy_SubstitutedFromReserve()
        {
            var deck = new List<Card> { Starter(), MakeLt02(56) };
            var g = new Game(deck, coin: () => false, reserve: new[] { Plain("SUB", 57) });
            g.StartLife(); No(g);                // health 100 ≥ 50 → LT02 gated off → substitute
            Assert.AreEqual("SUB", g.CurrentCard.Id, "LT02 skipped while health ≥ 50%");
        }

        [Test]
        public void Lt02_Drawn_WhenHealthBelow50()
        {
            var hit = WithNoDelta(Plain("HIT", 40), Scale.Health, DeltaKind.Set, 30);
            var deck = new List<Card> { Starter(), hit, MakeLt02(56) };
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            No(g);                               // HIT → health 30 (<50)
            Assert.AreEqual("LT02", g.CurrentCard.Id, "LT02 drawn once health < 50%");
        }

        // ================================================================ energy drain + breathing

        [Test]
        public void Energy_DoesNotDrainBefore25()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.StartLife(); No(g);
            g.Tick(1.5f);                        // age ≈ 18 (< 25)
            Assert.IsFalse(g.EnergyOpen, "energy shut before 25");
            Assert.AreEqual(100, g.Scales.Energy, "no drain before 25");
        }

        [Test]
        public void Energy_Opens25_AtTheTiredValue_AndDrainsWhileTheSensorIsDown()
        {
            var g = NewGame(() => false, EnergyFiller());
            g.StartLife(); No(g);
            OpenEnergy(g);
            Assert.IsTrue(g.EnergyOpen, "energy opens at 25");
            // ⚠ РЕДИЗАЙН 2026-08-07: шкала открывается ПРОСЕВШЕЙ («первая усталость»), а не полной —
            // иначе задача экрана «держи, пока батарейка не заполнится» была бы выполнена ещё до касания.
            Assert.AreEqual(Game.EnergyOpenValue, g.Scales.Energy, "энергия открывается на 20 %");

            int e = g.Scales.Energy;
            g.Tick(2f);                          // датчик НЕ поднят → только дренаж
            Assert.Less(g.Scales.Energy, e, "с опущенным датчиком энергия только убывает");
        }

        [Test]
        public void HeldSensor_FillsTheBattery_ReleasingStopsTheGrowth()
        {
            var g = NewGame(() => false, EnergyFiller());
            g.StartLife(); No(g);
            OpenEnergy(g);

            int e0 = g.Scales.Energy;
            HoldFor(g, 1f);                      // секунда удержания
            int afterHold = g.Scales.Energy;
            Assert.Greater(afterHold, e0 + 10,
                "секунда удержания обязана дать ДВУЗНАЧНЫЙ прирост — рост видно на глазах");

            g.Tick(1f);                          // отпустили — рост встал, дренаж пошёл
            Assert.Less(g.Scales.Energy, afterHold, "отпустил → рост прекращается, дренаж продолжается");
        }

        [Test]
        public void HeldSensor_FillsToFull_InAboutFiveSeconds_AndClampsAtHundred()
        {
            var g = NewGame(() => false, EnergyFiller());
            g.StartLife(); No(g);
            OpenEnergy(g);
            Assert.AreEqual(Game.EnergyOpenValue, g.Scales.Energy);

            HoldFor(g, 6f);
            Assert.AreEqual(100, g.Scales.Energy,
                "6 с удержания с типового старта заполняют батарею ДО КОНЦА (контракт: ~4–6 с)");
            HoldFor(g, 2f);
            Assert.AreEqual(100, g.Scales.Energy, "…и выше 100 % не уходит");
        }

        // Латч удержания потребляется ТОЛЬКО тиком, который реально интегрирует время. Tick(0) — кадр, за
        // который не прошло ни секунды: он не вправе съесть поднятый датчик. Иначе кадр с нулевым dt молча
        // крал бы удержание, которое игрок честно держал (скептик, MINOR 2026-08-07).
        [Test]
        public void HeldSensor_SurvivesAZeroDtTick_AndIsSpentExactlyOnceOnTheNextRealTick()
        {
            var g = NewGame(() => false, EnergyFiller());
            g.StartLife(); No(g);
            OpenEnergy(g);

            // Опорная величина: то же удержание за 0.1 с БЕЗ вклинившегося Tick(0).
            var reference = NewGame(() => false, EnergyFiller());
            reference.StartLife(); No(reference);
            OpenEnergy(reference);
            int refBefore = reference.Scales.Energy;
            Hold(reference);
            reference.Tick(0.1f);
            int refGain = reference.Scales.Energy - refBefore;
            Assert.Greater(refGain, 0, "0.1 с удержания даёт измеримый прирост (иначе тест ничего не ловит)");

            int before = g.Scales.Energy;
            Hold(g);
            g.Tick(0f);                          // кадр без прошедшего времени
            Assert.AreEqual(before, g.Scales.Energy, "Tick(0) ничего не интегрирует");
            g.Tick(0.1f);                        // датчик так и не отпускали — рост ровно за 0.1 с
            Assert.AreEqual(refGain, g.Scales.Energy - before,
                "удержание пережило Tick(0) и выросло ровно за 0.1 с — ни больше, ни меньше");

            // …и потрачено оно РОВНО ОДИН раз: дальше датчик считается отпущенным. Мерим СЕКУНДОЙ, а не
            // тактом: за 0.1 с дренаж (1.7 %/с) не набирает целого процента, и целочисленная шкала не
            // сдвинулась бы даже при честно отпущенном датчике.
            int afterSpent = g.Scales.Energy;
            g.Tick(1f);
            Assert.Less(g.Scales.Energy, afterSpent, "латч не залип — без нового Hold идёт только дренаж");
        }

        // ================================================================ ввод ПРИНЯТ / ОТВЕРГНУТ
        // HandleInput отдаёт «поработал ли игрок ЭТОЙ шкалой» — по этому признаку драйвер открывает
        // §6-окно недавнего ввода (салют за калибровку). Кризис и депрессия шкальные контролы ГЛУШАТ.

        [Test]
        public void HandleInput_ReportsAccepted_OnlyWhenTheScaleMechanicReallyTookIt()
        {
            var g = NewGame(() => false, EnergyFiller());
            g.StartLife(); No(g);

            Assert.IsFalse(g.EnergyOpen, "энергия ещё закрыта");
            Assert.IsFalse(g.HandleInput(GameInput.EnergyHold),
                "до открытия шкалы удержание ОТВЕРГНУТО — работой по шкале оно не является");

            OpenEnergy(g);
            Assert.IsTrue(g.HandleInput(GameInput.EnergyHold), "открытая шкала принимает удержание");

            g.Paused = true;                     // «глухая» S5-пауза: контролы мертвы
            Assert.IsFalse(g.HandleInput(GameInput.EnergyHold), "под глухой паузой удержание отвергнуто");
            g.Paused = false;

            Assert.AreEqual(g.MoneyOpen, g.HandleInput(GameInput.MoneyTick),
                "крутилка принимается ровно тогда, когда деньги открыты");
            Assert.AreEqual(g.RelationshipsOpen, g.HandleInput(GameInput.RelationUp),
                "ось отношений принимается ровно тогда, когда балансир открыт");
            Assert.IsFalse(g.HandleInput(GameInput.Confirm),
                "подтверждение — не шкальный ввод, окно §6 оно открывать не должно");
        }

        // ================================================================ burnout

        [Test]
        public void Burnout_Enters_HalvesIncome_ExitsAbove40_ReEnterable()
        {
            // ⚠ Δ карточки задаются SET, а не ADD: с 2026-08-07 энергия открывается на 20 %, и прежний
            // «−95» утащил бы шкалу в минус — Answer убил бы забег ещё до того, как выгорание защёлкнется.
            // D2 сидит на 28, а не на 45: тест реально ТИКАЕТ время (удержание), а возраст 45 завёл бы
            // кризис и заморозил им энергию.
            var d1 = WithNoDelta(Plain("D1", 26), Scale.Energy, DeltaKind.Set, 4);
            var d2 = WithNoDelta(Plain("D2", 28), Scale.Energy, DeltaKind.Set, 3);
            var g = NewGame(() => true, d1, d2, Plain("C", 90));
            g.StartLife(); No(g);
            OpenEnergy(g);                       // age ≈ 26, energy open, money open
            No(g);                               // D1 → energy = 4 (card Δ doesn't latch burnout yet)
            Assert.IsFalse(g.Burnout, "card Δ alone hasn't latched burnout");
            g.Tick(0.1f);                        // integrate → burnout latches at ≤10%
            Assert.IsTrue(g.Burnout, "burnout entered at ≤10% energy");
            Assert.AreEqual(0.5, g.IncomeMultiplier, Eps, "crank income halved while burnt out");
            double m0 = g.Money;
            Crank(g);
            Assert.AreEqual(m0 + 0.5, g.Money, Eps, "a tick pays only +0.5 during burnout");

            HoldFor(g, 3f);                      // зажали датчик — выгорание выходится за несколько секунд
            Assert.IsFalse(g.Burnout, "burnout releases above 40% (удержанием, за ~2-3 с)");
            Assert.AreEqual(1.0, g.IncomeMultiplier, Eps, "income back to full after recovery");

            No(g);                               // D2 → energy back down to 3
            g.Tick(0.1f);
            // r3 (п.5в): ВСТЫК больше нельзя — после выхода идёт грейс, и латч в нём ЗАПРЕЩЁН.
            Assert.IsFalse(g.Burnout, "повторное выгорание ВСТЫК запрещено — идёт грейс");
            g.Tick(Game.BurnoutGraceSeconds);     // …грейс истёк
            Assert.IsTrue(g.Burnout, "burnout is re-enterable — но только ПОСЛЕ грейса");
        }

        /// <summary>
        /// r3 (п.5в) — ГРЕЙС ПОСЛЕ ВЫГОРАНИЯ. Живой плейтест: игрок выкарабкивался из выгорания и тут же
        /// падал в него обратно — «выдохнуть не дают». Контракт: несколько секунд после выхода дренаж
        /// энергии НЕ идёт и выгорание не может защёлкнуться заново.
        ///
        /// Зубы: без грейса энергия после выхода стоит на 41 %, дренаж 1.7 %/с съедает её за ~18 с и — что
        /// важнее — карточка, роняющая энергию, защёлкивает выгорание ТЕМ ЖЕ тактом. Оба случая красные.
        /// </summary>
        [Test]
        public void BurnoutGrace_FreezesTheDrain_AndForbidsAnImmediateRelapse()
        {
            var d1 = WithNoDelta(Plain("D1", 26), Scale.Energy, DeltaKind.Set, 4);
            var d2 = WithNoDelta(Plain("D2", 28), Scale.Energy, DeltaKind.Set, 3);
            var g = NewGame(() => true, d1, d2, Plain("C", 90));
            g.StartLife(); No(g);
            OpenEnergy(g);
            No(g);                                // D1 → energy 4
            g.Tick(0.1f);
            Assert.IsTrue(g.Burnout, "вошли в выгорание");
            Assert.AreEqual(0f, g.BurnoutGrace, "внутри выгорания грейса нет");

            int hold = 0;                         // держим ровно ДО выхода — ни такта лишнего
            while (g.Burnout && hold++ < 400) { Hold(g); g.Tick(0.05f); }
            Assert.IsFalse(g.Burnout, "вышли из выгорания");
            Assert.AreEqual(Game.BurnoutGraceSeconds, g.BurnoutGrace, 1e-3f,
                "на выходе взводится грейс на всю длину");
            Assert.That(Game.BurnoutGraceSeconds, Is.InRange(5f, 8f),
                "коридор основательницы — 5–8 с");

            // (1) В ГРЕЙСЕ ДРЕНАЖА НЕТ: энергия стоит на месте, хотя датчик отпущен.
            int e0 = g.Scales.Energy;
            g.Tick(Game.BurnoutGraceSeconds - 1f);
            Assert.AreEqual(e0, g.Scales.Energy, "в грейсе энергия не убывает — это и есть «выдохнуть»");
            Assert.Greater(g.BurnoutGrace, 0f, "грейс ещё идёт");

            // (2) …и даже провал энергии КАРТОЧКОЙ внутри грейса выгорание не защёлкивает.
            No(g);                                // D2 → energy = 3
            g.Tick(0.2f);
            Assert.AreEqual(3, g.Scales.Energy, "карточка уронила шкалу…");
            Assert.IsFalse(g.Burnout, "…но выгорание в грейсе не защёлкивается");

            // (3) Грейс истёк — всё работает как раньше: дренаж идёт, выгорание входит.
            g.Tick(Game.BurnoutGraceSeconds);
            Assert.AreEqual(0f, g.BurnoutGrace, "грейс кончился");
            Assert.IsTrue(g.Burnout, "…и выгорание снова возможно");
        }

        /// <summary>Грейс живёт ОДНУ жизнь: рестарт снимает его вместе с выгоранием.</summary>
        [Test]
        public void BurnoutGrace_IsClearedByARestart()
        {
            var d1 = WithNoDelta(Plain("D1", 26), Scale.Energy, DeltaKind.Set, 4);
            var g = NewGame(() => true, d1, Plain("C", 90));
            g.StartLife(); No(g);
            OpenEnergy(g); No(g); g.Tick(0.1f);
            Assert.IsTrue(g.Burnout);
            int hold = 0;
            while (g.Burnout && hold++ < 400) { Hold(g); g.Tick(0.05f); }
            Assert.Greater(g.BurnoutGrace, 0f, "грейс взведён");

            g.StartLife();
            Assert.AreEqual(0f, g.BurnoutGrace, "свежая жизнь начинается без грейса");
            Assert.IsFalse(g.Burnout, "…и без выгорания");
        }

        // ================================================================ the two burnout endings

        [Test]
        public void HealthZero_FromDecay_EndsWithHealthCause()
        {
            var sick = WithNoDelta(Plain("SICK", 31), Scale.Health, DeltaKind.Set, 3);
            var g = NewGame(() => false, sick, Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(3f);                          // age 31
            No(g);                               // health set to 3
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 200) g.Tick(0.2f);
            Assert.AreEqual(GameState.Finale, g.State, "decay drove health to 0");
            Assert.AreEqual("здоровье не выдержало", g.Cause);
        }

        [Test]
        public void EnergyZero_FromDrain_EndsWithBurnoutCause()
        {
            var drained = WithNoDelta(Plain("TIRED", 26), Scale.Energy, DeltaKind.Set, 3);
            var g = NewGame(() => false, drained, Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2.2f);                        // energy open at 25
            No(g);                               // energy set to 3
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 200) g.Tick(0.2f);
            Assert.AreEqual(GameState.Finale, g.State, "drain drove energy to 0");
            Assert.AreEqual("полное выгорание", g.Cause);
        }

        // ================================================================ coasting: live drains keep running

        private static Card DelayedFatal(string id, int age, int years, string cause)
        {
            var c = Plain(id, age);
            c.DelayedFatalYears = years;
            c.FatalCause = cause;
            return c;
        }

        [Test]
        public void Coasting_NearZeroHealth_DecayDeathWins_NotScheduledFatal()
        {
            // Deck exhausts with «за вами пришли» pending 30 years out, but health is at 1%: the decay
            // keeps integrating during the coast and crosses zero first → health death, correct cause.
            var hit = WithNoDelta(Plain("HIT", 31), Scale.Health, DeltaKind.Set, 1);
            var g = NewGame(() => false, hit, DelayedFatal("RND01", 32, 30, "за вами пришли"));
            g.StartLife(); No(g);
            g.Tick(3f);                          // age → 31 (health decay live)
            No(g);                               // HIT → health 1
            Yes(g);                              // RND01 ДА → deck exhausted → coasting
            Assert.AreEqual(GameState.Playing, g.State, "coasting with the fatal pending");
            Assert.IsNull(g.CurrentCard);

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 1000) g.Tick(0.05f);

            Assert.AreEqual(GameState.Finale, g.State, "the coast ended in a finale");
            Assert.AreEqual("здоровье не выдержало", g.Cause,
                "health hit zero during the coast BEFORE the scheduled age — decay death wins");
            Assert.Less(g.Age, 62f, "died before the scheduled «за вами пришли» age (32+30)");
        }

        [Test]
        public void Coasting_NearZeroEnergy_DrainDeathWins_NotScheduledFatal()
        {
            var hit = WithNoDelta(Plain("HIT", 26), Scale.Energy, DeltaKind.Set, 1);
            var g = NewGame(() => false, hit, DelayedFatal("RND01", 27, 30, "за вами пришли"));
            g.StartLife(); No(g);
            g.Tick(2.2f);                        // age → ~26.4 (energy open + draining)
            No(g);                               // HIT → energy 1
            Yes(g);                              // RND01 ДА → coasting
            Assert.AreEqual(GameState.Playing, g.State);

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 1000) g.Tick(0.05f);

            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("полное выгорание", g.Cause,
                "energy hit zero during the coast BEFORE the scheduled age — drain death wins");
            Assert.Less(g.Age, 57f, "died before the scheduled «за вами пришли» age (27+30)");
        }

        [Test]
        public void Coasting_Crossing25_NeverOpensEnergy_NoPause_DeathLandsExactly()
        {
            // RND01 answered ДА at 22 as the LAST card → coast 22→27 crosses age 25. Nothing may open:
            // no EnergyOpened event (the driver would show the S5 hint and pause the death coast).
            var g = NewGame(() => false, DelayedFatal("RND01", 22, 5, "за вами пришли"));
            bool fired = false;
            g.EnergyOpened += () => { fired = true; g.Paused = true; };  // what the driver would do
            g.StartLife(); No(g);
            g.Tick(2f);                          // age → 22 (catch-up capped at the card's age)
            Yes(g);                              // deck exhausted → coasting toward 27

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 1000) g.Tick(0.05f);

            Assert.IsFalse(fired, "EnergyOpened never fires during the coast (no hint, no pause)");
            Assert.IsFalse(g.EnergyOpen, "energy stays unopened while coasting across 25");
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("за вами пришли", g.Cause, "the reckoning lands undisturbed");
            Assert.That(g.Age, Is.EqualTo(27f).Within(0.01f), "death exactly at 22 + 5 — no pause delay");
        }

        [Test]
        public void Coasting_Crossing30_NeverOpensHealthDecay_NoPause_DeathLandsExactly()
        {
            // Same for the health hint: ДА at 28 as the last card → coast 28→33 crosses age 30.
            var g = NewGame(() => false, DelayedFatal("RND01", 28, 5, "за вами пришли"));
            bool fired = false;
            g.HealthOpened += () => { fired = true; g.Paused = true; };
            g.StartLife(); No(g);
            g.Tick(2.5f);                        // age → 28 (energy opened normally in play at 25)
            Assert.IsTrue(g.EnergyOpen, "energy opened during normal play before the coast");
            Yes(g);                              // → coasting toward 33

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 1000) g.Tick(0.05f);

            Assert.IsFalse(fired, "HealthOpened never fires during the coast");
            Assert.IsFalse(g.HealthDecaying, "health decay stays unopened while coasting across 30");
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("за вами пришли", g.Cause);
            Assert.That(g.Age, Is.EqualTo(33f).Within(0.01f), "death exactly at 28 + 5 — no pause delay");
        }

        // ================================================================ LT02 heals +40 (REAL CSV row)

        [Test]
        public void Lt02_RealCsv_YesHealsPlus40_SickPlayerThirtyToSeventy()
        {
            // Guard against the Δ desync the skeptic caught (prose «+40%» vs machine «Здр +2»): assert
            // the ACTUAL loaded LT02 row heals +40, end-to-end through the game.
            var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes.csv present");
            var lt02 = CardLoader.LoadSubset(asset.text, new[] { "LT02" })[0];

            bool hasPlus40 = false;
            foreach (var d in lt02.YesDeltas)
                if (d.Scale == Scale.Health && d.Kind == DeltaKind.Add && d.Value == 40) hasPlus40 = true;
            Assert.IsTrue(hasPlus40, "real LT02 row carries ДА-Δ «Здр +40» (canon fae05ea)");

            var hit = WithNoDelta(Plain("HIT", 50), Scale.Health, DeltaKind.Set, 30);
            var deck = new List<Card> { Starter(), hit, lt02 };   // LT02 parses at its window start (55)
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f);                          // money open (18)
            for (int i = 0; i < 140; i++) Crank(g);   // bank ≥ 120₽ so BLOCK$ passes
            g.Tick(2.2f);                        // age → 50, HIT up
            No(g);                               // health → 30 (<50 → LT02 eligible)
            Assert.AreEqual("LT02", g.CurrentCard.Id, "operation drawn while sick");
            Assert.IsFalse(g.CurrentCardBlocked, "affordable at 120₽");
            Yes(g);                              // операция
            Assert.AreEqual(70, g.Scales.Health, "30% + 40% = 70% — the real row heals, not +2");
        }

        // ================================================================ energy REQUIRES breathing (r3)

        private static List<Card> AdultFiller()
        {
            var cards = new List<Card>();
            for (int a = 18; a <= 84; a += 3) cards.Add(Plain("F" + a, a));
            return cards;
        }

        [Test]
        public void NeverTouchingTheSensor_SlidesIntoBurnout_ThenEnergyDeath()
        {
            // ИНВАРИАНТ «живая шкала требует ввода» (memory 2026-07), несмотря на смену механики: игрок,
            // который НИ РАЗУ не поднял датчик, обязан выгореть и, проигнорировав это, умереть от энергии.
            // Удержание — реальная цена (рука уходит с крутилки), а не декорация.
            var g = NewGame(() => false, AdultFiller().ToArray());
            g.StartLife(); No(g);
            bool sawBurnout = false;
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 100000)
            {
                g.Tick(0.25f);                       // датчик не поднят НИ РАЗУ
                if (g.Burnout) sawBurnout = true;
            }
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.IsTrue(sawBurnout, "passive player hits burnout (income ×0.5) partway through adult life");
            Assert.AreEqual("полное выгорание", g.Cause,
                "ignoring the height sensor is fatal — energy death is reachable by neglect");
        }

        [Test]
        public void ModestHolding_StaysAboveBurnout_ToANonEnergyEnding()
        {
            // Компетентный игрок: замечает просевшую батарею и ДЕРЖИТ датчик, пока она не наберётся снова.
            // Такой игрок держится комфортно выше выгорания и доходит до не-энергетического финала. Доступно
            // — просто стоит рабочего времени рук. (Здоровье выживает на своей калибровке 0.7 %/с → забег
            // кончается сам собой, значит ни одна шкала не убивает грамотного игрока без плохих выборов.)
            var g = NewGame(() => false, AdultFiller().ToArray());
            g.StartLife(); No(g);
            int guard = 0;
            int minEnergyWhileOpen = 100;
            while (g.State == GameState.Playing && guard++ < 100000)
            {
                // Держим, пока батарея ниже 60 %: это «поднял руку и подождал», а не метроном.
                if (g.EnergyOpen && g.Scales.Energy < 60) Hold(g);
                g.Tick(0.25f);
                if (g.EnergyOpen) minEnergyWhileOpen = System.Math.Min(minEnergyWhileOpen, g.Scales.Energy);
            }
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreNotEqual("полное выгорание", g.Cause, "работа датчиком снимает энергетическую смерть");
            Assert.IsFalse(g.Burnout, "never left in burnout at the end");
            Assert.Greater(minEnergyWhileOpen, BurnoutEnterEnergyReadable(),
                "энергия всю взрослую жизнь держалась выше порога выгорания");
            CollectionAssert.Contains(new[] { "спокойная старость", "весёлая старость", "одинокая старость" },
                g.Cause, $"reaches a natural old-age ending (got «{g.Cause}»)");
        }

        private static int BurnoutEnterEnergyReadable() => Game.BurnoutEnterEnergyAtOrBelow;

        // ================================================================ opens + hint pause + restart

        [Test]
        public void EnergyOpen_And_HealthDecay_FireEventsAt25And30()
        {
            bool energyFired = false, healthFired = false;
            var g = NewGame(() => false, Plain("A", 90));
            g.EnergyOpened += () => energyFired = true;
            g.HealthOpened += () => healthFired = true;
            g.StartLife(); No(g);

            g.Tick(2.2f);                        // age ≈ 26.4
            Assert.IsTrue(energyFired && g.EnergyOpen, "energy opened at 25");
            Assert.IsFalse(healthFired, "health not decaying yet at 26");

            g.Tick(0.5f);                        // age ≈ 32.4
            Assert.IsTrue(healthFired && g.HealthDecaying, "health decay opened at 30");
        }

        [Test]
        public void OpenHint_Pauses_FreezesEverything_LikeMoney()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.EnergyOpened += () => g.Paused = true;   // the driver does this to freeze under the S5 hint
            g.StartLife(); No(g);
            g.Tick(2.2f);                        // crosses 25 → energy opens → pause
            Assert.IsTrue(g.Paused && g.EnergyOpen, "energy-open hint paused the game");

            float age0 = g.Age; int e0 = g.Scales.Energy; float t0 = g.CardTimer;
            g.Tick(5f);                          // frozen
            Assert.AreEqual(age0, g.Age, "age frozen under the hint");
            Assert.AreEqual(e0, g.Scales.Energy, "energy drain frozen under the hint");
            Assert.AreEqual(t0, g.CardTimer, "card timer frozen under the hint");
        }

        [Test]
        public void Restart_ResetsHealthEnergyBurnout_AndReArmsOpens()
        {
            var d1 = WithNoDelta(Plain("D1", 26), Scale.Energy, DeltaKind.Set, 4);
            var g = NewGame(() => false, d1, Plain("C", 90));
            g.StartLife(); No(g);
            OpenEnergy(g); No(g); g.Tick(0.1f);  // burnout on
            Assert.IsTrue(g.Burnout);
            No(g);                               // finish the deck → finale (burnout still on)
            Assert.AreEqual(GameState.Finale, g.State);

            g.HandleInput(GameInput.Confirm);    // → opener
            g.HandleInput(GameInput.Confirm);    // → fresh life
            Assert.AreEqual(100, g.Scales.Health, "health reset");
            Assert.AreEqual(100, g.Scales.Energy, "energy reset");
            Assert.IsFalse(g.Burnout, "burnout cleared");
            Assert.IsFalse(g.EnergyOpen, "energy closed again");
            Assert.IsFalse(g.HealthDecaying, "health decay re-armed (off)");
            Assert.AreEqual(1.0, g.IncomeMultiplier, Eps, "burnout income penalty cleared");

            No(g);                               // start age timer
            g.Tick(2.2f);                        // and the opens re-fire on the new life
            Assert.IsTrue(g.EnergyOpen, "energy re-opens on the new life");
        }
    }
}
