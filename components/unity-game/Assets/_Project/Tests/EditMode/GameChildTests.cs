using System;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The live child (fifth scale) on the pure <see cref="Game"/> spine (dt-injected, never wall-clock):
    /// the open on MD02=ДА (+not before, +not on НЕТ) with the hint-pause freeze, the signal-response flash
    /// scheduler (interval / ~2s window / ~1s anti-pre-spam min-delay), the in-window CHILD_PRESS success,
    /// the 2-consecutive-miss «плохой родитель» penalty (relationships −10% + child drop, one-shot per
    /// lapse), the LT04 close (ДА −1 rel / НЕТ ok), the no-death guarantee, and the restart reset.
    /// Numbers are the tunable canon constants on <see cref="Game"/>. The flash interval is injected via
    /// <see cref="Game.ChildFlashInterval"/> so timing is deterministic.
    /// </summary>
    public class GameChildTests
    {
        // ---- card builders (mirroring GameRelationshipsTests) ----
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

        // A modest tail of neutral cards so timeouts during multi-second ticks just advance harmlessly and
        // the run never ends mid-test. Low, tightly-packed ages keep the age (and thus the other scales)
        // from racing ahead while we exercise the child flash timing.
        private static List<Card> Filler(int from = 22, int to = 120)
        {
            var cards = new List<Card>();
            for (int a = from; a <= to; a++) cards.Add(Plain("F" + a, a));
            return cards;
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);
        private static void Press(Game g) => g.HandleInput(GameInput.ChildPress);

        // Build a game whose second card is MD02 (opens the child), followed by neutral filler.
        private static Game WithMd02(Func<bool> coin = null, float? interval = null)
        {
            var deck = new List<Card> { Starter(), Plain("MD02", 21) };
            deck.AddRange(Filler());
            var g = new Game(deck, coin: coin ?? (() => false));
            if (interval.HasValue) g.ChildFlashInterval = () => interval.Value;
            return g;
        }

        // Advance MD02 to be the current card (I03 resolved → age timer running), then open the child on ДА.
        private static void OpenChild(Game g)
        {
            g.StartLife();
            No(g);      // I03 (starter) → age timer starts, MD02 becomes current
            Yes(g);     // MD02=ДА → child opens
        }

        // Tick until the flash window opens (or a guard trips). Returns the elapsed time.
        private static float TickUntilFlashing(Game g, float dt = 0.1f, int guard = 5000)
        {
            float t = 0f;
            int i = 0;
            while (!g.ChildFlashing && g.State == GameState.Playing && i++ < guard)
            {
                g.Tick(dt);
                t += dt;
            }
            return t;
        }

        // ================================================================ open on MD02=ДА

        [Test]
        public void Child_DoesNotOpenBeforeMd02()
        {
            var g = WithMd02();
            g.StartLife(); No(g);                 // I03 resolved, MD02 current but unanswered
            Assert.IsFalse(g.ChildOpen, "child shut until MD02 resolves");
            Assert.AreEqual(0, g.Scales.Child, "child scale untouched before open");
        }

        [Test]
        public void Child_OpensOnMd02Yes_FiresEvent_LiftsScale()
        {
            bool fired = false;
            var g = WithMd02();
            g.ChildOpened += () => fired = true;
            g.StartLife(); No(g);
            Assert.IsFalse(g.ChildOpen);
            Yes(g);                               // MD02=ДА
            Assert.IsTrue(g.ChildOpen, "child opened on MD02=ДА");
            Assert.IsTrue(fired, "the open event fired (drives the S5 hint)");
            Assert.AreEqual(Game.ChildStartValue, g.Scales.Child, "child scale lifted to its start value");
        }

        [Test]
        public void Child_DoesNotOpenOnMd02No()
        {
            var g = WithMd02();
            g.StartLife(); No(g);
            No(g);                                // MD02=НЕТ «не завели»
            Assert.IsFalse(g.ChildOpen, "НЕТ opens nothing");
            Assert.AreEqual(0, g.Scales.Child);
        }

        [Test]
        public void OpenHint_Pauses_FreezesChild_LikeTheOtherScales()
        {
            var g = WithMd02(interval: 5f);
            g.ChildOpened += () => g.Paused = true;   // what the driver does under the S5 hint
            OpenChild(g);
            Assert.IsTrue(g.Paused && g.ChildOpen, "child-open hint paused the game");

            bool flashing0 = g.ChildFlashing; int child0 = g.Scales.Child; float age0 = g.Age;
            g.Tick(10f);                          // frozen — no flash scheduling, no age
            Assert.AreEqual(flashing0, g.ChildFlashing, "flash scheduler frozen under the hint");
            Assert.AreEqual(child0, g.Scales.Child, "child scale frozen under the hint");
            Assert.AreEqual(age0, g.Age, "age frozen under the hint");
        }

        // ================================================================ flash scheduler

        [Test]
        public void Flash_DefaultInterval_IsFifteenToTwentyFive()
        {
            var g = WithMd02();                   // no injected interval → uniform [15,25] draw
            OpenChild(g);
            Assert.IsFalse(g.ChildFlashing, "no flash immediately on open");
            float t = TickUntilFlashing(g, 0.25f);
            Assert.IsTrue(g.ChildFlashing, "a flash eventually fires");
            Assert.That(t, Is.InRange(14.5f, 25.5f), "first flash lands in the ~15–25s canon window");
        }

        [Test]
        public void Flash_InjectedInterval_OpensWindow_AndPressInWindowSucceeds()
        {
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            float t = TickUntilFlashing(g);
            Assert.IsTrue(g.ChildFlashing, "window opened");
            Assert.That(t, Is.InRange(4.9f, 5.2f), "flash fired at the injected ~5s interval");

            int child0 = g.Scales.Child;
            Press(g);                             // CHILD_PRESS inside the open window → success
            Assert.IsFalse(g.ChildFlashing, "a successful press closes the window");
            Assert.AreEqual(child0 + Game.ChildPressGain, g.Scales.Child, "good parenting nudges the scale up");
        }

        [Test]
        public void PressOutsideWindow_BanksNothing_AndTheFlashStillMissesIfIgnored()
        {
            // Mashing Enter BEFORE the flash cannot pre-satisfy the window (нельзя заспамить заранее).
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            int child0 = g.Scales.Child;
            for (int i = 0; i < 20; i++) { Press(g); g.Tick(0.1f); }   // 2s of pre-spam, no window yet
            Assert.IsFalse(g.ChildFlashing, "still before the flash");
            Assert.AreEqual(child0, g.Scales.Child, "pre-spam banked no success");
        }

        [Test]
        public void PreSpam_WithinMinDelayBeforeFlash_LocksOutTheWindow()
        {
            // A press within ~1s BEFORE the flash arms the anti-pre-spam lockout, which carries INTO the
            // window — so an in-window press while locked out does NOT count (min-delay guard).
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            g.Tick(4.5f);                         // 0.5s before the 5s flash
            Assert.IsFalse(g.ChildFlashing);
            Press(g);                             // premature press → arms the ~1s lockout
            g.Tick(0.6f);                         // window opens (~5.1s); lockout still ~0.9s left
            Assert.IsTrue(g.ChildFlashing, "window is open");
            int child0 = g.Scales.Child;
            Press(g);                             // in-window but locked out → ignored
            Assert.AreEqual(child0, g.Scales.Child, "pre-pressed within 1s → the window press is void");
        }

        [Test]
        public void CleanWait_AfterEarlyPressExpires_SucceedsAgain()
        {
            // A press MORE than ~1s before the flash is harmless: the lockout expires before the window, so
            // a clean in-window press still counts (confirms only the last ~1s pre-press blocks).
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            g.Tick(2f);                           // 3s before the flash
            Press(g);                             // early press → lockout armed but will expire
            float t = TickUntilFlashing(g);
            Assert.IsTrue(g.ChildFlashing, "window opened");
            int child0 = g.Scales.Child;
            Press(g);                             // clean, not locked out → success
            Assert.AreEqual(child0 + Game.ChildPressGain, g.Scales.Child, "clean in-window press succeeds");
        }

        // ================================================================ missed flashes → «плохой родитель»

        [Test]
        public void SingleMissedFlash_NoPenaltyYet()
        {
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            int child0 = g.Scales.Child;
            int missed = 0;
            g.ChildCallMissed += () => missed++;
            TickUntilFlashing(g);
            // Мерим ОТ МОМЕНТА, когда трубка зазвонила: тогда единственное, что может тронуть отношения за
            // окно, — это дрейф, и его верхняя граница считается точно. (Раньше сравнение шло с началом
            // ожидания и молча предполагало, что дрейф «мал»; r3 сделал его ощутимо быстрее.)
            int rel1 = g.Scales.Relationships;
            const float window = Game.ChildFlashWindow + 0.2f;
            g.Tick(window);                       // окно истекает НЕПОДНЯТЫМ → пропуск №1
            Assert.IsFalse(g.ChildFlashing, "window closed (missed)");
            Assert.AreEqual(1, missed, "ровно один ПРОПУСК объявлен (r3 п.9: событие на КАЖДЫЙ пропуск)");
            Assert.AreEqual(child0, g.Scales.Child, "one miss alone does not drop the child scale");

            int drop = rel1 - g.Scales.Relationships;
            int driftMax = (int)(Game.RelDriftPerSec * window) + 2;   // +2 — округление до целого
            Assert.LessOrEqual(drop, driftMax,
                $"падение отношений {drop} объясняется ОДНИМ дрейфом (≤{driftMax}) — штрафа "
                + $"−{Game.ChildBadParentRelPenalty}% за ОДИН пропуск нет");
        }

        [Test]
        public void TwoConsecutiveMisses_BadParent_ChildDrops_RelationshipsHit_OneShotPerLapse()
        {
            var g = WithMd02(interval: 5f);
            OpenChild(g);

            // Miss #1: let the first window expire unpressed.
            TickUntilFlashing(g);
            g.Tick(Game.ChildFlashWindow + 0.2f);
            Assert.IsFalse(g.ChildFlashing);

            // Reach the SECOND flash, then snapshot right at its window open (isolates the penalty step).
            TickUntilFlashing(g);
            Assert.IsTrue(g.ChildFlashing, "second flash opened");
            int childBefore = g.Scales.Child; int relBefore = g.Scales.Relationships;

            // Miss #2 → «плохой родитель»: child scale drop is a clean fixed step; relationships take the
            // −10% penalty (plus any tiny drift over the 2s window, so assert «at least»).
            g.Tick(Game.ChildFlashWindow + 0.2f);
            Assert.AreEqual(childBefore - Game.ChildBadParentScaleDrop, g.Scales.Child,
                "the child scale drops by the bad-parent step on the 2nd consecutive miss");
            Assert.LessOrEqual(g.Scales.Relationships, relBefore - Game.ChildBadParentRelPenalty,
                "relationships take at least the −10% bad-parent penalty");

            // One-shot per lapse: the streak reset, so the very NEXT single miss does not re-penalise.
            int childAfter = g.Scales.Child;
            TickUntilFlashing(g);
            g.Tick(Game.ChildFlashWindow + 0.2f);     // miss #3 (a fresh streak of 1)
            Assert.AreEqual(childAfter, g.Scales.Child, "a single fresh miss after a lapse does not drop again");
        }

        [Test]
        public void PressingEveryFlash_NeverBecomesBadParent()
        {
            var g = WithMd02(interval: 3f);
            OpenChild(g);
            for (int flash = 0; flash < 4; flash++)
            {
                TickUntilFlashing(g);
                Assert.IsTrue(g.ChildFlashing);
                Press(g);                          // succeed every flash
            }
            // The child scale only moves on a press (+gain) or a bad-parent lapse (−drop). Answering every
            // flash means it can only ever go UP — a lapse would have pulled it below the start value.
            Assert.GreaterOrEqual(g.Scales.Child, Game.ChildStartValue,
                "attentive parent keeps the scale up — never a bad-parent drop");
        }

        // ================================================================ LT04 (children grown)

        [Test]
        public void Lt04_Yes_ClosesChild_AndCostsOneRelationship()
        {
            var deck = new List<Card>
            {
                Starter(), Plain("MD02", 21),
                WithYesDelta(Plain("LT04", 22), Scale.Relationships, DeltaKind.Add, -1)  // canon «Отн −1»
            };
            deck.AddRange(Filler(23));
            var g = new Game(deck, coin: () => false) { ChildFlashInterval = () => 5f };
            g.StartLife(); No(g); Yes(g);         // open child via MD02
            Assert.IsTrue(g.ChildOpen);
            int rel0 = g.Scales.Relationships;
            Yes(g);                               // LT04=ДА «навязчивая опека»
            Assert.IsFalse(g.ChildOpen, "children grown → child scale/button off");
            Assert.IsFalse(g.ChildFlashing, "no more flashing after LT04");
            Assert.AreEqual(rel0 - 1, g.Scales.Relationships, "ДА (опека) costs one relationship point");
        }

        [Test]
        public void Lt04_No_ClosesChild_NoRelationshipCost()
        {
            var deck = new List<Card> { Starter(), Plain("MD02", 21), Plain("LT04", 22) };
            deck.AddRange(Filler(23));
            var g = new Game(deck, coin: () => false) { ChildFlashInterval = () => 5f };
            g.StartLife(); No(g); Yes(g);
            int rel0 = g.Scales.Relationships;
            No(g);                                // LT04=НЕТ «отпустил с миром»
            Assert.IsFalse(g.ChildOpen, "children grown → off, either answer");
            Assert.AreEqual(rel0, g.Scales.Relationships, "НЕТ carries no relationships cost");
        }

        // ================================================================ no death

        [Test]
        public void Child_NeverKills_EvenWhenEveryFlashIsMissed()
        {
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            // Pile up bad-parent lapses until the child scale bottoms out. It reaching 0 WHILE the run is
            // still Playing proves the child path never calls End() — no death from the child (canon).
            bool bottomedWhilePlaying = false;
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 2000)
            {
                g.Tick(0.5f);                     // never press
                if (g.EnergyOpen && g.State == GameState.Playing)
                    g.HandleInput(GameInput.EnergyHold);    // датчик зажат — ЭНЕРГИЯ не кончит забег первой
                if (g.State == GameState.Playing && g.Scales.Child == 0) { bottomedWhilePlaying = true; break; }
            }
            Assert.IsTrue(bottomedWhilePlaying,
                "the child scale bottomed out from repeated lapses — and the run was still live (no child death)");
            Assert.AreEqual(GameState.Playing, g.State, "the child bottoming out did NOT end the run");
        }

        // ================================================================ input routing / inertness

        [Test]
        public void ChildPress_IsInert_BeforeOpen_AndOutsideGameplay()
        {
            var g = WithMd02(interval: 5f);
            // Before open (opener): a stray CHILD_PRESS does nothing.
            Press(g);
            Assert.AreEqual(GameState.Opener, g.State);
            g.StartLife(); No(g);
            Press(g);                             // Playing, but child not open yet
            Assert.IsFalse(g.ChildOpen);
            Assert.AreEqual(0, g.Scales.Child, "CHILD_PRESS inert before the child opens");
            Yes(g);                               // open child
            Press(g);                             // not flashing → no-op (no success banked)
            Assert.AreEqual(Game.ChildStartValue, g.Scales.Child, "press with no window banks nothing");
        }

        [Test]
        public void ChildPress_IsInert_WhilePaused()
        {
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            TickUntilFlashing(g);
            Assert.IsTrue(g.ChildFlashing);
            g.Paused = true;                      // tutorial/overlay up
            int child0 = g.Scales.Child;
            Press(g);
            Assert.AreEqual(child0, g.Scales.Child, "a press while paused is ignored");
        }

        // ================================================================ restart reset

        [Test]
        public void Restart_ResetsChild_FlashLapses_AndReArmsOpen()
        {
            var g = WithMd02(interval: 5f);
            OpenChild(g);
            TickUntilFlashing(g);
            Assert.IsTrue(g.ChildOpen && g.ChildFlashing);

            // Drive to the finale, then a fresh life.
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 5000)
            {
                g.Tick(0.5f);
                if (g.EnergyOpen && g.State == GameState.Playing)
                    g.HandleInput(GameInput.EnergyHold);
            }
            Assert.AreEqual(GameState.Finale, g.State);

            g.HandleInput(GameInput.Confirm);     // → opener
            Assert.IsFalse(g.ChildOpen, "child closed on leaving play");
            Assert.IsFalse(g.ChildFlashing);
            Assert.AreEqual(0, g.Scales.Child, "child scale reset to 0");

            g.HandleInput(GameInput.Confirm);     // → fresh life
            No(g);
            Assert.IsFalse(g.ChildOpen, "child still shut on the new life until MD02 again");
            Yes(g);                               // MD02=ДА again
            Assert.IsTrue(g.ChildOpen, "the child re-opens on the new life");
            Assert.AreEqual(Game.ChildStartValue, g.Scales.Child);
        }
    }
}
