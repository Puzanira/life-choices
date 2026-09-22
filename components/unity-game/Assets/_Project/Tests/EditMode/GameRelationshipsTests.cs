using System;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The live relationships balancer on the pure <see cref="Game"/> spine (dt-injected, never wall-clock):
    /// the open at 20 (YA03) + hint-pause freeze, the downward drift (softened while married via MD01=ДА),
    /// the RELATION_AXIS ↑/↓ pull, the cumulative below-zone breakup (reset, no death), the over-attention
    /// penalty + red-zone flag above 75%, axis gameplay-only inertness, and the restart reset.
    /// Numbers are the tunable canon constants on <see cref="Game"/>.
    /// </summary>
    public class GameRelationshipsTests
    {
        // ---- card builders (mirroring GameHealthEnergyTests) ----
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

        // A card whose НЕТ answer SETS relationships to a fixed value (to place the marker precisely).
        private static Card SetRelNo(string id, int age, int val)
            => WithNoDelta(Plain(id, age), Scale.Relationships, DeltaKind.Set, val);

        private static Game NewGame(Func<bool> coin, params Card[] cards)
        {
            var deck = new List<Card> { Starter() };
            deck.AddRange(cards);
            return new Game(deck, coin: coin);
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);
        private static void Up(Game g) => g.HandleInput(GameInput.RelationUp);
        private static void Down(Game g) => g.HandleInput(GameInput.RelationDown);

        private static List<Card> AdultFiller(int from = 22, int to = 84, int step = 3)
        {
            var cards = new List<Card>();
            for (int a = from; a <= to; a += step) cards.Add(Plain("F" + a, a));
            return cards;
        }

        // ================================================================ open at 20

        [Test]
        public void Relationships_DoesNotOpenBefore20()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.StartLife(); No(g);
            g.Tick(1.5f);                       // age → 18 (money opens; relationships need 20)
            Assert.IsFalse(g.RelationshipsOpen, "balancer shut before 20");
            Assert.AreEqual(55, g.Scales.Relationships, "no drift before the balancer opens");
        }

        [Test]
        public void Relationships_OpensAt20_FiresEvent()
        {
            bool fired = false;
            var g = NewGame(() => false, Plain("A", 90));
            g.RelationshipsOpened += () => fired = true;
            g.StartLife(); No(g);
            g.Tick(1.5f);                       // age → 18, still shut
            Assert.IsFalse(g.RelationshipsOpen);
            g.Tick(0.3f);                       // age ≈ 21.6 → balancer opens
            Assert.IsTrue(fired && g.RelationshipsOpen, "balancer opened at 20");
        }

        [Test]
        public void OpenHint_Pauses_FreezesRelationships_LikeTheOtherScales()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.RelationshipsOpened += () => g.Paused = true;   // what the driver does under the S5 hint
            g.StartLife(); No(g);
            g.Tick(2f);                          // crosses 20 → opens → pause
            Assert.IsTrue(g.Paused && g.RelationshipsOpen, "relationships-open hint paused the game");

            float age0 = g.Age; int r0 = g.Scales.Relationships; float t0 = g.CardTimer;
            g.Tick(5f);                          // frozen
            Assert.AreEqual(age0, g.Age, "age frozen under the hint");
            Assert.AreEqual(r0, g.Scales.Relationships, "relationships drift frozen under the hint");
            Assert.AreEqual(t0, g.CardTimer, "card timer frozen under the hint");
        }

        // ================================================================ drift (r3: −1.6, married −0.8)
        // ⚠ ПЕРЕКАЛИБРОВКА 2026-08-07 (живой плейтест: «до расставания при бездействии больше минуты —
        // бездействие не наказывается»). Числа тюнимые, механика и пороги зон НЕ менялись; тесты ниже
        // связаны с КОНСТАНТАМИ, а не с зашитыми «0.6» — балансная правка меняет одну строку в Game.

        [Test]
        public void Drift_PullsDownAtTheCanonRate()
        {
            var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2f);                          // age → 21, balancer open
            Assert.IsTrue(g.RelationshipsOpen);
            No(g);                               // A → L up (свежее окно фазы: L в 90 лет → 6 s)
            int r0 = g.Scales.Relationships;
            for (int i = 0; i < 8; i++) g.Tick(0.5f);   // 4s under the timer, no axis
            int drop = r0 - g.Scales.Relationships;
            double want = Game.RelDriftPerSec * 4.0;
            Assert.That(drop, Is.InRange(want - 1.0, want + 1.0),
                $"дрейф ≈{Game.RelDriftPerSec}%/с (4 с ≈ {want}%) — округление до целого даёт ±1");
            Assert.Greater(Game.RelDriftPerSec, 1.0,
                "бездействие ДОЛЖНО наказываться: дрейф быстрее одного процента в секунду");
        }

        [Test]
        public void Marriage_MD01Yes_HalvesTheDrift()
        {
            var g = NewGame(() => false, Plain("MD01", 21), Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2f);                          // age → 21, balancer open
            Yes(g);                              // MD01=ДА → married; L up
            Assert.IsTrue(g.Married, "MD01=ДА marks married");
            int r0 = g.Scales.Relationships;
            for (int i = 0; i < 8; i++) g.Tick(0.5f);   // 4s
            int drop = r0 - g.Scales.Relationships;
            double want = Game.RelDriftMarriedPerSec * 4.0;
            Assert.That(drop, Is.InRange(want - 1.0, want + 1.0),
                $"дрейф в браке ≈{Game.RelDriftMarriedPerSec}%/с (4 с ≈ {want}%) — мягче холостого");
            Assert.AreEqual(Game.RelDriftPerSec / 2.0, Game.RelDriftMarriedPerSec, 1e-6,
                "брак — ровно вдвое мягче, каким бы ни был базовый дрейф");
        }

        /// <summary>
        /// r3 (п.7) — ГЛАВНЫЙ гард балансной правки: ИЗ ЗЕЛЁНОЙ СЕРЕДИНЫ (стартовые 55 %) ПОЛНОЕ
        /// БЕЗДЕЙСТВИЕ приводит к РАЗРЫВУ за 30–45 секунд. Это ровно то, что просила основательница:
        /// «сейчас до расставания при бездействии больше минуты — не наказывается».
        ///
        /// Зубы двусторонние: на старом дрейфе 0.6 %/с получалось ≈76 с (краснеет по верхней границе), а
        /// на «слишком злом» дрейфе разрыв успел бы до 30 с (краснеет по нижней). Порог зон и длительность
        /// красного таймера при этом не участвуют в правке — они те же константы.
        /// </summary>
        [Test]
        public void Idle_FromTheGreenMiddle_BreaksUpWithinThirtyToFortyFiveSeconds()
        {
            // Возраст ДЕРЖИМ на 22: энергия (25) не открывается, здоровье (30) не тает — измеряется РОВНО
            // дрейф отношений, а не гонка «кто убьёт раньше». Карточек с запасом на весь коридор.
            var deck = new List<Card> { Starter() };
            for (int i = 0; i < 30; i++) deck.Add(Plain("F" + i, 22));
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f);                          // открылось на 20; шкала на стартовых 55 (зелёная середина)
            Assert.IsTrue(g.RelationshipsOpen);
            Assert.That(g.Scales.Relationships, Is.InRange(Game.RelZoneMin, Game.RelZoneMax),
                "стартуем именно из зелёной зоны");

            float t = 0f;
            int guard = 0;
            while (!g.RelationshipsLost && g.State == GameState.Playing && guard++ < 3000)
            {
                g.Tick(0.1f);                    // НИ ОДНОГО ввода по оси — полное бездействие
                t += 0.1f;
            }

            Assert.IsTrue(g.RelationshipsLost, "полное бездействие приводит к разрыву");
            Assert.That(t, Is.InRange(30f, 45f),
                $"из зелёной середины до разрыва при бездействии {t:0.0} с — коридор основательницы 30–45 с");
        }

        /// <summary>
        /// Вторая половина той же правки: УДЕРЖАНИЕ по-прежнему уверенно вытягивает. Нетто «держу ↑»
        /// обязано остаться ЯВНО положительным — иначе ускоренный дрейф превратил бы живую шкалу в
        /// неуправляемую (memory: «живую шкалу нельзя калибровать так, чтобы игрок не мог её удержать»).
        /// </summary>
        [Test]
        public void Holding_StillPullsUpConfidently_AgainstTheFasterDrift()
        {
            Assert.Greater(Game.RelBalancerPerSec - Game.RelDriftPerSec, 2.0,
                "нетто удержания ↑ — ЯВНО положительное (>2 %/с), а не «еле-еле»");

            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 20) };
            for (int i = 0; i < 30; i++) deck.Add(Plain("F" + i, 22));   // возраст держим ниже 25
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f); No(g);                   // шкала посажена на 20 — жёлтый буфер, до разрыва рукой подать
            int r0 = g.Scales.Relationships;

            float t = 0f;
            for (int i = 0; i < 100; i++) { Up(g); g.Tick(0.1f); t += 0.1f; }   // 10 с удержания ↑
            Assert.IsFalse(g.RelationshipsLost, "удержание спасает от разрыва");
            Assert.That(g.Scales.Relationships - r0, Is.GreaterThanOrEqualTo(20),
                $"за {t:0.0} с удержания шкала выросла минимум на 20 п.п. (нетто "
                + $"{Game.RelBalancerPerSec - Game.RelDriftPerSec:0.0} %/с)");
            Assert.That(g.Scales.Relationships, Is.GreaterThanOrEqualTo(Game.RelZoneMin),
                "…и вернулась в зелёную зону");
        }

        // ================================================================ RELATION_AXIS ↑/↓

        [Test]
        public void AxisUp_RaisesMarker_OvercomingDrift()
        {
            var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2f); No(g);                   // balancer open, L up
            int r0 = g.Scales.Relationships;
            for (int i = 0; i < 8; i++) { Up(g); g.Tick(0.5f); }   // 4s holding ↑
            Assert.Greater(g.Scales.Relationships, r0,
                "held ↑ pulls the marker UP despite the drift (net ≈ +0.9%/s)");
        }

        [Test]
        public void AxisDown_LowersMarker_FasterThanDrift()
        {
            var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2f); No(g);
            int r0 = g.Scales.Relationships;
            for (int i = 0; i < 8; i++) { Down(g); g.Tick(0.5f); }  // 4s holding ↓
            int drop = r0 - g.Scales.Relationships;
            Assert.That(drop, Is.GreaterThanOrEqualTo(7),
                "held ↓ плюс дрейф тянут вниз заметно быстрее одного дрейфа (4 с ≳ 8 %)");
        }

        /// <summary>
        /// Величина САМОЙ оси, очищенная от дрейфа: (Δс ↑) − (Δбез ввода) на одном и том же окне.
        ///
        /// ⚠ ОКНО СОКРАЩЕНО ДО 1 с (r5 п.1). Тяга выросла 4.0 → 22.0 %/с (панч-лист автомата: «отношения
        /// всё время выходят, удержать нельзя»), и прежнее окно в 4 с упирало маркер в потолок 100 —
        /// замер мерил бы КЛАМП, а не скорость. Число берётся из КОНСТАНТЫ, а не зашито: балансная правка
        /// снова меняет одну строку в Game, а не этот тест.
        /// </summary>
        [Test]
        public void AxisMagnitude_MatchesTheBalancerConstant()
        {
            const float window = 1f;          // ровно столько, чтобы тяга 22 %/с не достала потолка со старта 55
            const int steps = 2;              // 2 × 0.5 с

            int WithoutAxis()
            {
                var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
                g.StartLife(); No(g); g.Tick(2f); No(g);
                int r0 = g.Scales.Relationships;
                for (int i = 0; i < steps; i++) g.Tick(window / steps);
                return g.Scales.Relationships - r0;   // ≈ −дрейф
            }
            int WithUp()
            {
                var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
                g.StartLife(); No(g); g.Tick(2f); No(g);
                int r0 = g.Scales.Relationships;
                for (int i = 0; i < steps; i++) { Up(g); g.Tick(window / steps); }
                Assert.Less(g.Scales.Relationships, 100, "замер не упёрся в потолок — иначе мерили бы кламп");
                return g.Scales.Relationships - r0;
            }

            int axisOnly = WithUp() - WithoutAxis();
            double want = Game.RelBalancerPerSec * window;
            Assert.That(axisOnly, Is.InRange(want - 3.0, want + 3.0),
                $"тяга оси ≈{Game.RelBalancerPerSec} %/с (за {window} с ≈ {want}) — замерено {axisOnly}; "
                + "округление до целого на двух замерах даёт ±3");
        }

        [Test]
        public void Axis_IsInert_BeforeOpenAndOutsideGameplay()
        {
            var g = NewGame(() => false, Plain("A", 90));
            g.StartLife(); No(g);
            g.Tick(1f);                          // age ≈ 12 — balancer not open
            Up(g);                               // axis before open
            g.Tick(0.5f);
            Assert.IsFalse(g.RelationshipsOpen);
            Assert.AreEqual(55, g.Scales.Relationships, "↑ inert before the balancer opens");

            // Also inert while paused (open, but under a hint).
            g.Tick(2f);                          // opens now (age past 20)
            Assert.IsTrue(g.RelationshipsOpen);
            g.Paused = true;
            int r0 = g.Scales.Relationships;
            Up(g); g.Tick(1f);                   // paused → nothing moves
            Assert.AreEqual(r0, g.Scales.Relationships, "↑ inert while paused");
        }

        // ================================================================ breakup (RED zone <15 cumulative → reset)

        [Test]
        public void Breakup_AfterCumulativeBelowZone_ResetsPartner_NoDeath()
        {
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 12) };
            deck.AddRange(AdultFiller());        // plenty of neutral cards so the run doesn't end first
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f);                          // age → 21, balancer open
            Assert.IsTrue(g.RelationshipsOpen);
            No(g);                               // HIT → relationships set to 12 (in the red zone, below 15)
            Assert.AreEqual(12, g.Scales.Relationships);

            bool broke = false;
            g.RelationshipBrokeUp += () => broke = true;
            int guard = 0;
            while (!g.RelationshipsLost && g.State == GameState.Playing && guard++ < 3000)
                g.Tick(0.1f);

            Assert.IsTrue(g.RelationshipsLost, "≈10s cumulative below the zone → breakup");
            Assert.IsTrue(broke, "the breakup event fired");
            Assert.IsFalse(g.RelationshipsOpen, "balancer closed — partner gone");
            Assert.IsFalse(g.Married, "marriage cleared on breakup");
            Assert.AreEqual(Game.RelBreakupValue, g.Scales.Relationships, "dropped to the lonely value");
            Assert.AreEqual(GameState.Playing, g.State, "relationships NEVER kill — the run continues");
        }

        [Test]
        public void Breakup_Timing_IsAboutTenSeconds()
        {
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 14) };
            deck.AddRange(AdultFiller());
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f); No(g);                   // open + relationships 14 (just inside the red zone, below 15)
            float below = 0f;
            int guard = 0;
            while (!g.RelationshipsLost && g.State == GameState.Playing && guard++ < 3000)
            {
                g.Tick(0.1f);
                below += 0.1f;
            }
            Assert.IsTrue(g.RelationshipsLost, "broke up");
            Assert.That(below, Is.InRange(9f, 12f), "breakup around the ~10s cumulative threshold");
        }

        [Test]
        public void YellowBuffer_BelowZoneFloorButAboveRedFloor_DoesNotBreakUp()
        {
            // Founder fix (2026-07-23): the yellow band [RelBreakupFloor..RelZoneMin] is a WARNING buffer —
            // sitting there must NOT arm the breakup timer. Breakup counts only in the RED zone (<15).
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 30) };
            deck.AddRange(AdultFiller());
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f); No(g);                   // open + relationships 30 (yellow: below 40, above 15)
            for (int i = 0; i < 60 && g.Scales.Relationships >= Game.RelBreakupFloor; i++)
            {
                g.Tick(0.1f);
                Assert.IsFalse(g.RelationshipsLost, "no breakup while the marker is in the yellow buffer");
            }
        }

        [Test]
        public void InZone_NeverBreaksUp_EvenOverALongTime()
        {
            // Held ↑ keeps the marker in-zone for a long adult life: the below-zone timer never runs, so
            // there is no breakup (confirms only time BELOW the floor counts toward it).
            var deck = new List<Card> { Starter() };
            deck.AddRange(AdultFiller());
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f);                          // open at 20 (relationships 55, in zone)
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 400)
            {
                Up(g);                           // hold the marker up, inside 40–75
                g.Tick(0.1f);
                Assert.IsFalse(g.RelationshipsLost, "no breakup while held inside the zone");
            }
        }

        // ================================================================ over-attention penalty (>75)

        [Test]
        public void AboveSeventyFive_FlagsRedZone_AndPenaltyPullsBack()
        {
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 82) };
            deck.AddRange(AdultFiller());
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f);                          // open
            No(g);                               // HIT → relationships 82 (>75)
            Assert.AreEqual(82, g.Scales.Relationships);
            Assert.IsTrue(g.RelationshipRedZone, "above 75 flags the red zone");

            int guard = 0;                       // penalty (0.3) + drift (0.6) pull it back under 75
            while (g.RelationshipRedZone && g.State == GameState.Playing && guard++ < 400)
                g.Tick(0.1f);
            Assert.IsFalse(g.RelationshipRedZone, "penalty pulled the marker back below 75");
            Assert.LessOrEqual(g.Scales.Relationships, Game.RelZoneMax, "back inside the zone ceiling");
        }

        // ================================================================ restart reset

        [Test]
        public void Restart_ResetsRelationshipsOpenMarriedLost_AndReArmsOpen()
        {
            var g = NewGame(() => false, Plain("MD01", 21), Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2f);                          // open
            Yes(g);                              // married; L up
            Assert.IsTrue(g.Married && g.RelationshipsOpen);
            No(g);                               // finish deck → finale
            Assert.AreEqual(GameState.Finale, g.State);

            g.HandleInput(GameInput.Confirm);    // → opener
            g.HandleInput(GameInput.Confirm);    // → fresh life
            Assert.IsFalse(g.RelationshipsOpen, "balancer closed on the new life");
            Assert.IsFalse(g.Married, "marriage cleared");
            Assert.IsFalse(g.RelationshipsLost, "loss flag cleared");
            Assert.AreEqual(55, g.Scales.Relationships, "relationships reset to 55");

            No(g);                               // start age timer
            g.Tick(2f);                          // cross 20 again
            Assert.IsTrue(g.RelationshipsOpen, "the balancer re-opens on the new life");
        }

        // ================================================================ ending tone reflects LIVE value

        [Test]
        public void BreakupDrivenLowRelationships_LandsLonelyOldAge()
        {
            // Drift the marker into a breakup, then survive to old age: the natural-ending tone reads the
            // LIVE (post-breakup, lonely) relationships value — «одинокая старость».
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 35) };
            deck.AddRange(AdultFiller());
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f); No(g);                   // open + relationships 35
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 100000)
            {
                g.Tick(0.25f);
                if (g.EnergyOpen && g.State == GameState.Playing)
                    g.HandleInput(GameInput.EnergyHold);    // датчик зажат — энергия не убьёт раньше
            }
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.IsTrue(g.RelationshipsLost, "the marker broke up along the way");
            Assert.AreEqual("одинокая старость", g.Cause,
                "the old-age tone reflects the live, lonely relationships value at death");
        }
    }
}
