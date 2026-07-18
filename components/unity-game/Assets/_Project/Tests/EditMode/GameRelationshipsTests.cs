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

        // ================================================================ drift (−0.6, married −0.3)

        [Test]
        public void Drift_PullsDownAboutPointSixPerSecond()
        {
            var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
            g.StartLife(); No(g);
            g.Tick(2f);                          // age → 21, balancer open
            Assert.IsTrue(g.RelationshipsOpen);
            No(g);                               // A → L up (fresh 5s timer)
            int r0 = g.Scales.Relationships;
            for (int i = 0; i < 8; i++) g.Tick(0.5f);   // 4s under the timer, no axis
            int drop = r0 - g.Scales.Relationships;
            Assert.That(drop, Is.InRange(2, 3), "≈0.6%/s downward drift (4s ≈ 2–3%)");
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
            Assert.That(drop, Is.InRange(1, 2), "married drift ≈0.3%/s (4s ≈ 1%) — softer than single");
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
                "held ↓ (≈1.5) plus drift (≈0.6) ≈ 2.1%/s down (4s ≳ 8%)");
        }

        [Test]
        public void AxisMagnitude_IsAboutOnePointFivePerSecond()
        {
            // Isolate the axis: (Δwith ↑) − (Δwithout) over the same window ≈ +1.5%/s × window.
            int WithoutAxis()
            {
                var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
                g.StartLife(); No(g); g.Tick(2f); No(g);
                int r0 = g.Scales.Relationships;
                for (int i = 0; i < 8; i++) g.Tick(0.5f);
                return g.Scales.Relationships - r0;   // ≈ −2.4
            }
            int WithUp()
            {
                var g = NewGame(() => false, Plain("A", 21), Plain("L", 90));
                g.StartLife(); No(g); g.Tick(2f); No(g);
                int r0 = g.Scales.Relationships;
                for (int i = 0; i < 8; i++) { Up(g); g.Tick(0.5f); }
                return g.Scales.Relationships - r0;   // ≈ +3.6
            }
            int axisOnly = WithUp() - WithoutAxis();   // ≈ 6 over 4s
            Assert.That(axisOnly, Is.InRange(5, 7), "the ↑ pull alone is ≈1.5%/s (≈6% over 4s)");
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

        // ================================================================ breakup (<40 cumulative → reset)

        [Test]
        public void Breakup_AfterCumulativeBelowZone_ResetsPartner_NoDeath()
        {
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 35) };
            deck.AddRange(AdultFiller());        // plenty of neutral cards so the run doesn't end first
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f);                          // age → 21, balancer open
            Assert.IsTrue(g.RelationshipsOpen);
            No(g);                               // HIT → relationships set to 35 (below the 40 floor)
            Assert.AreEqual(35, g.Scales.Relationships);

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
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 39) };
            deck.AddRange(AdultFiller());
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g);
            g.Tick(2f); No(g);                   // open + relationships 39 (just below 40)
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
                for (int i = 0; i < 6 && g.EnergyOpen && g.State == GameState.Playing; i++)
                    g.HandleInput(GameInput.EnergyPulse);   // breathe so energy never kills first
            }
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.IsTrue(g.RelationshipsLost, "the marker broke up along the way");
            Assert.AreEqual("одинокая старость", g.Cause,
                "the old-age tone reflects the live, lonely relationships value at death");
        }
    }
}
