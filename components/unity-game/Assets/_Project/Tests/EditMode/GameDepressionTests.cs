using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Depression «тёмная полоса» (CR09): the RANDOM_TRIGGER tail after the crisis, the slow-pulse
    /// «собраться» mini-game (~2.5–3s interval, ~0.6s window) that is DELIBERATELY the OPPOSITE of the
    /// energy breathing, the ±color-step progress, the anti-mash rule (mashing never wins), the 5-catch
    /// exit, the scales-pause, the NO-death contract, that CONFIRM is the catch (not a restart), and the
    /// restart reset. Time / the pulse interval / the entry roll are injected — nothing races the wall clock.
    /// </summary>
    public class GameDepressionTests
    {
        private static readonly string[] CrisisIds =
            { "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08" };

        private static string Csv()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            return asset.text;
        }

        private static Card Filler()
            => new Card
            {
                Id = "FILL", Question = "FILL?", When = "60", Age = 60, Order = 999,
                YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                NoNecrolog = "жил дальше", Flags = new List<string>(),
            };

        // A minimal plan re-parsed fresh each life: I03 (starts the age timer) + a filler at 60 so age climbs
        // through the 45–50 crisis window, the whole crisis block, and CR09 carried on the depression slot.
        private static System.Func<DeckPlan> DepressionPlan(string csv, bool withCrisis = true)
        {
            return () =>
            {
                var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
                return new DeckPlan
                {
                    Deck = new List<Card> { byId["I03"], Filler() },
                    Reserve = new List<Card>(),
                    Crisis = withCrisis ? CrisisIds.Select(id => byId[id]).ToList() : new List<Card>(),
                    Depression = byId["CR09"],
                };
            };
        }

        private static void TickToCrisis(Game g)
        {
            int guard = 0;
            while (g.Phase == CrisisPhase.None && g.State == GameState.Playing && guard++ < 400)
                g.Tick(0.1f);
        }

        // Start a life, clear the crisis blitz cleanly (0 fails → no impulse), and let the crisis tail roll.
        // depressionRoll controls whether the tail enters depression; pulse interval is pinned for determinism.
        private static Game StartCrisisAndTail(string csv, bool depressionRoll)
        {
            var g = new Game(DepressionPlan(csv), coin: () => false)
            {
                BlitzNormalOnLeftRoll = () => true,       // «ВСЁ НОРМАЛЬНО» always LEFT → ← is always correct
                DepressionTriggerRoll = () => depressionRoll,
                DepressionPulseInterval = () => 2.5f,     // pin every pulse to the low bound
            };
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);            // resolve I03 → age timer running, FILL drawn
            TickToCrisis(g);
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "reached the crisis blitz");
            for (int i = 0; i < 5; i++) g.HandleInput(GameInput.AnswerYes);   // 5 correct → 0 fails → tail
            return g;
        }

        private static Game ReachDepression(string csv)
        {
            var g = StartCrisisAndTail(csv, depressionRoll: true);
            Assert.IsTrue(g.InDepression, "the crisis tail rolled into depression");
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "enters fully desaturated");
            return g;
        }

        // Advance to the next lit pulse and catch it (one clean CONFIRM inside the window).
        private static void CatchOnePulse(Game g)
        {
            g.Tick(2.5f);                                 // interval elapses → pulse opens (window full)
            Assert.IsTrue(g.DepressionPulsing, "the dim pulse is lit");
            g.HandleInput(GameInput.Confirm);             // catch on the pulse
        }

        // ---- entry: RANDOM_TRIGGER tail after the crisis ----

        [Test]
        public void Depression_EntersAfterCrisis_WhenRollHits()
        {
            var g = ReachDepression(Csv());
            Assert.AreEqual(GameState.Playing, g.State, "depression is a sub-state of Playing (no state change)");
            Assert.IsFalse(g.InCrisis, "the crisis already resolved before depression");
        }

        [Test]
        public void Depression_DoesNotEnter_WhenRollMisses()
        {
            var g = StartCrisisAndTail(Csv(), depressionRoll: false);
            Assert.IsFalse(g.InDepression, "roll missed → no depression");
            Assert.AreEqual("FILL", g.CurrentCard.Id, "ordinary play resumed on the suspended card");
            Assert.AreEqual(GameState.Playing, g.State);
        }

        [Test]
        public void Depression_NeverEnters_WithoutACrisis()
        {
            // Even with the roll forced true, depression can only follow the crisis tail — a life with no
            // crisis block never touches it.
            var g = new Game(DepressionPlan(Csv(), withCrisis: false), coin: () => false)
            {
                DepressionTriggerRoll = () => true,
            };
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);            // I03
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 3000)
            {
                Assert.IsFalse(g.InDepression, "no depression without a crisis");
                g.Tick(0.2f);
                if (g.CurrentCard != null && g.CardTimer < 1f) g.HandleInput(GameInput.AnswerNo);
            }
            Assert.AreEqual(GameState.Finale, g.State, "the life ended normally");
            Assert.IsFalse(g.InDepression);
        }

        [Test]
        public void Depression_IsOneShot_DoesNotReEnter_ThisLife()
        {
            var g = ReachDepression(Csv());
            for (int i = 0; i < Game.DepressionGraySteps; i++) CatchOnePulse(g);   // exit
            Assert.IsFalse(g.InDepression, "depression lifted after 5 catches");

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 400)
            {
                g.Tick(0.2f);
                Assert.IsFalse(g.InDepression, "depression never re-enters this life (one-shot after the crisis)");
            }
        }

        // ---- scales paused throughout ----

        [Test]
        public void Depression_ScalesArePaused()
        {
            var g = ReachDepression(Csv());
            Assert.IsTrue(g.EnergyOpen, "energy is live by ~45 (would drain in ordinary play)");

            int health0 = g.Scales.Health, energy0 = g.Scales.Energy, rel0 = g.Scales.Relationships;
            double money0 = g.Money;
            g.Tick(0.5f);   // less than one pulse interval — no catch, no passive drain
            Assert.AreEqual(health0, g.Scales.Health, "health frozen in depression");
            Assert.AreEqual(energy0, g.Scales.Energy, "energy frozen in depression");
            Assert.AreEqual(rel0, g.Scales.Relationships, "relationships frozen in depression");
            Assert.AreEqual(money0, g.Money, "money (cost-of-living) frozen in depression");
        }

        // ---- slow pulse: ~2.5–3s interval, ~0.6s window ----

        [Test]
        public void Depression_Pulse_IntervalThenWindow()
        {
            var g = ReachDepression(Csv());
            Assert.IsFalse(g.DepressionPulsing, "no pulse the instant depression begins");

            g.Tick(2.4f);
            Assert.IsFalse(g.DepressionPulsing, "still no pulse before the ~2.5s interval");
            g.Tick(0.11f);
            Assert.IsTrue(g.DepressionPulsing, "the dim pulse opens after ~2.5s");

            g.Tick(0.5f);
            Assert.IsTrue(g.DepressionPulsing, "the ~0.6s window is still open at 0.5s");
            g.Tick(0.11f);
            Assert.IsFalse(g.DepressionPulsing, "the window closes after ~0.6s");
        }

        // ---- hit in window → +1 color step ----

        [Test]
        public void Depression_HitInWindow_RestoresOneColorStep()
        {
            var g = ReachDepression(Csv());
            int gray0 = g.DepressionGray;
            CatchOnePulse(g);
            Assert.AreEqual(gray0 - 1, g.DepressionGray, "a clean catch returns one step of colour");
            Assert.IsFalse(g.DepressionPulsing, "the pulse is consumed by the catch");
        }

        // ---- miss / outside window → −1 color step, floored ----

        [Test]
        public void Depression_MissOutsideWindow_SlipsBackOneStep_Floored()
        {
            var g = ReachDepression(Csv());
            CatchOnePulse(g);                              // gray 5 → 4 (make progress first)
            Assert.AreEqual(Game.DepressionGraySteps - 1, g.DepressionGray);

            Assert.IsFalse(g.DepressionPulsing, "no pulse lit right now");
            g.HandleInput(GameInput.Confirm);             // a press OUTSIDE the window = miss
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "colour slips one step back toward gray");

            g.HandleInput(GameInput.Confirm);             // another errant press
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "floored at full gray — never below the start");
            Assert.IsTrue(g.InDepression, "a miss never ends depression");
        }

        [Test]
        public void Depression_MissedPulse_ClosingUnpressed_SlipsBack()
        {
            var g = ReachDepression(Csv());
            CatchOnePulse(g);                              // gray 4
            int gray = g.DepressionGray;

            g.Tick(2.5f);                                  // next pulse opens
            Assert.IsTrue(g.DepressionPulsing);
            g.Tick(0.7f);                                  // let it close UNPRESSED (canon «не нажал в окне»)
            Assert.IsFalse(g.DepressionPulsing);
            Assert.AreEqual(gray + 1, g.DepressionGray, "a pulse missed by inaction slips colour back a step");
        }

        // ---- mashing never wins ----

        [Test]
        public void Depression_Mashing_NeverWins()
        {
            var g = ReachDepression(Csv());
            // Mash CONFIRM every frame across many pulse cycles: the anti-mash lockout means an unlocked
            // press can never coincide with the window, so no catch ever lands and depression never lifts.
            for (int i = 0; i < 600; i++)
            {
                g.HandleInput(GameInput.Confirm);
                g.Tick(0.05f);
                Assert.IsTrue(g.InDepression, "mashing never exits depression");
            }
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "colour stays pinned at full gray while mashing");
            Assert.AreEqual(GameState.Playing, g.State, "still alive, still Playing");
        }

        // ---- 5 catches → exit + resume ----

        [Test]
        public void Depression_FiveCatches_LiftAndResume()
        {
            var g = ReachDepression(Csv());
            for (int i = 0; i < Game.DepressionGraySteps; i++)
            {
                Assert.IsTrue(g.InDepression, $"still depressed before catch {i + 1}");
                CatchOnePulse(g);
            }
            Assert.AreEqual(0, g.DepressionGray, "5 catches → full colour");
            Assert.IsFalse(g.InDepression, "depression lifted");
            Assert.AreEqual(GameState.Playing, g.State, "ordinary play resumes");
            Assert.AreEqual("FILL", g.CurrentCard.Id, "resumed on the suspended normal card");
        }

        // ---- no death in depression ----

        [Test]
        public void Depression_NoDeath_EvenIfNeverCaught()
        {
            var g = ReachDepression(Csv());
            for (int i = 0; i < 600; i++)   // ~60s of pulses opening and closing unpressed
            {
                g.Tick(0.1f);
                Assert.AreEqual(GameState.Playing, g.State, "depression has no death path");
                Assert.IsTrue(g.InDepression, "…and the only way out is the mini-game");
            }
        }

        // ---- CONFIRM is the catch, not a restart ----

        [Test]
        public void Depression_Confirm_IsTheCatch_NotARestart()
        {
            var g = ReachDepression(Csv());
            g.HandleInput(GameInput.Confirm);             // outside a window — a miss, NOT a restart
            Assert.AreEqual(GameState.Playing, g.State, "CONFIRM never restarts/leaves play during depression");
            Assert.IsTrue(g.InDepression);

            CatchOnePulse(g);                             // inside the window — a catch (progress)
            Assert.AreEqual(Game.DepressionGraySteps - 1, g.DepressionGray, "CONFIRM in the window catches (progress)");
            Assert.AreEqual(GameState.Playing, g.State, "still Playing (mid-life), not the opener");
        }

        // ---- E (breathing) is inert in depression (distinct from the CONFIRM catch) ----

        [Test]
        public void Depression_EnergyPulse_IsInert()
        {
            var g = ReachDepression(Csv());
            int energy0 = g.Scales.Energy, gray0 = g.DepressionGray;
            g.HandleInput(GameInput.EnergyPulse);         // the breathing lever does nothing in depression
            Assert.AreEqual(energy0, g.Scales.Energy, "E does not restore energy in depression (scales paused)");
            Assert.AreEqual(gray0, g.DepressionGray, "…and E is not the pulse catch (that's CONFIRM)");
            Assert.IsTrue(g.InDepression);
        }

        // ---- restart resets depression state ----

        [Test]
        public void Depression_Restart_ResetsState()
        {
            var g = ReachDepression(Csv());
            Assert.IsTrue(g.InDepression);
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray);

            g.StartLife();                                // fresh life
            Assert.IsFalse(g.InDepression, "restart cleared depression");
            Assert.AreEqual(0, g.DepressionGray, "grayscale reset");
            Assert.IsFalse(g.DepressionPulsing, "no lingering pulse");
        }
    }
}
