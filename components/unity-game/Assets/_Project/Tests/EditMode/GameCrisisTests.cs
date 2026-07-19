using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Midlife crisis (CR00–CR08): the one-shot 45–50 trigger, the 5×2s blitz with a seeded
    /// «ВСЁ НОРМАЛЬНО» side, the fail counter, the ≥2-fail impulse gate, INVERT semantics
    /// (silence = ДА, → = НЕТ), impulse-card consequences, the scales-pause, and the restart reset.
    /// Time and the blitz side are injected (dt / a roll seam) so nothing races the wall clock.
    /// </summary>
    public class GameCrisisTests
    {
        private static readonly string[] CrisisIds =
            { "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08" };

        private static string Csv()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            return asset.text;
        }

        // A minimal plan re-parsed fresh each life: I03 (starts the age timer) + a filler card at 60 so the
        // age climbs through the 45–50 window, plus the whole crisis block carried on the plan.
        private static System.Func<DeckPlan> CrisisPlan(string csv)
        {
            return () =>
            {
                var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
                var i03 = byId["I03"];                          // StartsAgeTimer set by the loader
                var filler = new Card
                {
                    Id = "FILL", Question = "FILL?", When = "60", Age = 60, Order = 999,
                    YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                    NoNecrolog = "жил дальше", Flags = new List<string>(),
                };
                return new DeckPlan
                {
                    Deck = new List<Card> { i03, filler },
                    Reserve = new List<Card>(),
                    Crisis = CrisisIds.Select(id => byId[id]).ToList(),
                };
            };
        }

        // Start a life and run the age timer up to (but not into) the crisis, returning the live game.
        private static Game StartAndReach(string csv, System.Func<bool> blitzLeft = null)
        {
            var g = new Game(CrisisPlan(csv), coin: () => false) { BlitzNormalOnLeftRoll = blitzLeft };
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);   // resolve I03 → age timer running, FILL drawn
            return g;
        }

        private static void TickToCrisis(Game g)
        {
            int guard = 0;
            while (g.Phase == CrisisPhase.None && g.State == GameState.Playing && guard++ < 400)
                g.Tick(0.1f);
        }

        // ---- trigger: once, at 45–50, not before ----

        [Test]
        public void Crisis_Triggers_At45_NotBefore()
        {
            var g = StartAndReach(Csv());

            // Below the window: no crisis while age is still climbing under 44.
            int guard = 0;
            while (g.Age < 44f && g.State == GameState.Playing && guard++ < 400)
            {
                Assert.AreEqual(CrisisPhase.None, g.Phase, "no crisis before 45");
                g.Tick(0.1f);
            }
            Assert.AreEqual(CrisisPhase.None, g.Phase, "still no crisis just under the window");

            TickToCrisis(g);
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "crisis opened once age reached the 45–50 window");
            Assert.GreaterOrEqual(g.Age, Game.CrisisTriggerAge, "fired at/after 45");
            Assert.AreEqual("CR01", g.CurrentCard.Id, "blitz opens on the first thought");
            Assert.AreEqual(1, g.BlitzThoughtNumber);
        }

        [Test]
        public void Crisis_IsOneShot_DoesNotFireTwice()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase);

            // Clear the blitz cleanly (all correct → no impulse), then age on well past 50.
            for (int i = 0; i < 5; i++) g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(CrisisPhase.None, g.Phase, "crisis ended, ordinary play resumed");

            int guard = 0;
            while (g.State == GameState.Playing && g.Age < 58f && guard++ < 400)
            {
                g.Tick(0.1f);
                Assert.AreEqual(CrisisPhase.None, g.Phase, "the crisis never re-fires this life");
            }
        }

        // ---- blitz: 5 thoughts, 2s each ----

        [Test]
        public void Blitz_FiveThoughts_TwoSecondsEach()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);

            for (int n = 1; n <= 5; n++)
            {
                Assert.AreEqual(CrisisPhase.Blitz, g.Phase, $"still in blitz at thought {n}");
                Assert.AreEqual(n, g.BlitzThoughtNumber, "thought number advances 1..5");
                Assert.AreEqual("CR0" + n, g.CurrentCard.Id, "the CR0N thought is up");
                Assert.That(g.CrisisTimer, Is.EqualTo(Game.BlitzSeconds).Within(0.001f), "2s per thought");
                g.HandleInput(GameInput.AnswerYes);   // correct (normal on left)
            }
            Assert.AreEqual(CrisisPhase.None, g.Phase, "blitz over after 5 thoughts");
            Assert.AreEqual("FILL", g.CurrentCard.Id, "the suspended normal card resumed");
        }

        [Test]
        public void Blitz_Timeout_CountsAsFail_AndAdvances()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);
            Assert.AreEqual(1, g.BlitzThoughtNumber);
            Assert.AreEqual(0, g.BlitzFails);

            g.Tick(Game.BlitzSeconds + 0.01f);   // let thought 1's 2s timer expire
            Assert.AreEqual(1, g.BlitzFails, "timeout = +1 fail");
            Assert.AreEqual(2, g.BlitzThoughtNumber, "advanced to the next thought");
        }

        [Test]
        public void Blitz_NormalSide_Randomizes_BothSidesOccur()
        {
            // Injected alternating side → verify Game reports the seam's side and that both sides occur.
            bool left = true;
            var g = StartAndReach(Csv(), blitzLeft: () => { left = !left; return left; });
            TickToCrisis(g);

            var sides = new List<bool>();
            for (int n = 0; n < 5; n++)
            {
                sides.Add(g.BlitzNormalOnLeft);
                // press the CURRENT normal side so it's always correct regardless of the roll
                g.HandleInput(g.BlitzNormalOnLeft ? GameInput.AnswerYes : GameInput.AnswerNo);
            }
            Assert.Contains(true, sides, "«ВСЁ НОРМАЛЬНО» lands on the LEFT for some thoughts");
            Assert.Contains(false, sides, "…and on the RIGHT for others");
            Assert.AreEqual(0, g.BlitzFails, "pressing the reported normal side is always correct");
        }

        [Test]
        public void Blitz_CorrectPress_NoFail_WrongPress_Fail()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);  // «ВСЁ НОРМАЛЬНО» always on the LEFT (←)
            TickToCrisis(g);

            g.HandleInput(GameInput.AnswerYes);   // ← = normal side → correct
            Assert.AreEqual(0, g.BlitzFails, "correct press: no fail");

            g.HandleInput(GameInput.AnswerNo);    // → = «О НЕТ» side → fail
            Assert.AreEqual(1, g.BlitzFails, "wrong press («О НЕТ»): +1 fail");
        }

        // ---- impulse gate: only at ≥2 fails ----

        [Test]
        public void Impulse_Skipped_When_FailsUnderTwo()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);

            g.HandleInput(GameInput.AnswerNo);    // 1 fail
            for (int i = 0; i < 4; i++) g.HandleInput(GameInput.AnswerYes);  // 4 correct

            Assert.AreEqual(1, g.BlitzFails);
            Assert.AreEqual(CrisisPhase.None, g.Phase, "0–1 fails → impulse skipped, crisis ends");
            Assert.AreEqual("FILL", g.CurrentCard.Id, "resumed ordinary play, never touched an impulse card");
        }

        [Test]
        public void Impulse_Entered_When_FailsAtLeastTwo()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);

            g.HandleInput(GameInput.AnswerNo);    // fail 1
            g.HandleInput(GameInput.AnswerNo);    // fail 2
            for (int i = 0; i < 3; i++) g.HandleInput(GameInput.AnswerYes);  // 3 correct

            Assert.AreEqual(2, g.BlitzFails);
            Assert.AreEqual(CrisisPhase.Impulse, g.Phase, "≥2 fails opens the impulse round");
            Assert.AreEqual("CR06", g.CurrentCard.Id, "impulse opens on CR06");
        }

        // Reach the impulse round on CR06 (2 fails), for the INVERT / consequence tests.
        private static Game ReachImpulse(string csv)
        {
            var g = StartAndReach(csv, blitzLeft: () => true);
            TickToCrisis(g);
            g.HandleInput(GameInput.AnswerNo);
            g.HandleInput(GameInput.AnswerNo);
            for (int i = 0; i < 3; i++) g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(CrisisPhase.Impulse, g.Phase);
            Assert.AreEqual("CR06", g.CurrentCard.Id);
            return g;
        }

        // ---- INVERT: silence = ДА, → = НЕТ ----

        [Test]
        public void Impulse_Invert_Timeout_IsYes_AppliesConsequence()
        {
            var g = ReachImpulse(Csv());
            int rel0 = g.Scales.Relationships;    // CR06 ДА = Отн −3

            g.Tick(Game.ImpulseSeconds + 0.01f);  // silence → INVERT → ДА
            Assert.AreEqual(rel0 - 3, g.Scales.Relationships, "timeout accepted CR06 → Отн −3 applied");
            Assert.AreEqual("CR07", g.CurrentCard.Id, "advanced to the next impulse card");
        }

        [Test]
        public void Impulse_Invert_RightArrow_IsNo_DeclinesWithoutConsequence()
        {
            var g = ReachImpulse(Csv());
            int rel0 = g.Scales.Relationships;

            g.HandleInput(GameInput.AnswerNo);    // → «СПАСИБО, НЕ НАДО» = decline
            Assert.AreEqual(rel0, g.Scales.Relationships, "declining CR06 applies no ДА-Δ (Отн unchanged)");
            Assert.AreEqual("CR07", g.CurrentCard.Id, "advanced to the next impulse card");
        }

        [Test]
        public void Impulse_CardConsequences_Apply_OnAccept()
        {
            var g = ReachImpulse(Csv());
            g.HandleInput(GameInput.AnswerNo);    // decline CR06 → CR07 «мотоцикл» (Эн +2, Дн −2)
            Assert.AreEqual("CR07", g.CurrentCard.Id);

            int energy0 = g.Scales.Energy;
            double money0 = g.Money;
            g.HandleInput(GameInput.AnswerYes);   // accept CR07 (← = поддаться)
            Assert.AreEqual(energy0 + 2, g.Scales.Energy, "CR07 accept applies Эн +2");
            Assert.AreEqual(money0 - 2, g.Money, 0.001, "CR07 accept applies Дн −2");
        }

        [Test]
        public void Impulse_AllThree_ThenResumes()
        {
            var g = ReachImpulse(Csv());
            g.HandleInput(GameInput.AnswerNo);    // CR06 decline → CR07
            Assert.AreEqual("CR07", g.CurrentCard.Id);
            g.HandleInput(GameInput.AnswerNo);    // CR07 decline → CR08
            Assert.AreEqual("CR08", g.CurrentCard.Id);
            g.HandleInput(GameInput.AnswerNo);    // CR08 decline → crisis over
            Assert.AreEqual(CrisisPhase.None, g.Phase, "impulse round complete → ordinary play");
            Assert.AreEqual("FILL", g.CurrentCard.Id, "the suspended card resumed");
        }

        // ---- scales paused during the crisis ----

        [Test]
        public void LiveScales_ArePaused_DuringBlitz()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);
            Assert.IsTrue(g.EnergyOpen, "energy is live by 45 (drains in ordinary play)");

            int health0 = g.Scales.Health, energy0 = g.Scales.Energy, rel0 = g.Scales.Relationships;
            double money0 = g.Money;
            g.Tick(0.5f);   // half of thought 1's window — no advance, and no passive drain
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "still on thought 1");
            Assert.AreEqual(health0, g.Scales.Health, "health frozen during the blitz");
            Assert.AreEqual(energy0, g.Scales.Energy, "energy frozen during the blitz");
            Assert.AreEqual(rel0, g.Scales.Relationships, "relationships frozen during the blitz");
            Assert.AreEqual(money0, g.Money, "money (cost-of-living) frozen during the blitz");
        }

        [Test]
        public void Crisis_DoesNotKill_ByItself()
        {
            // Even the worst blitz (every thought times out → 5 fails → impulse) plus declining every
            // impulse card leaves the run alive: the crisis itself never ends the life.
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);
            for (int i = 0; i < 5; i++) g.Tick(Game.BlitzSeconds + 0.01f);  // 5 timeouts → 5 fails
            Assert.AreEqual(CrisisPhase.Impulse, g.Phase, "5 fails opened the impulse round");
            g.HandleInput(GameInput.AnswerNo);
            g.HandleInput(GameInput.AnswerNo);
            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(GameState.Playing, g.State, "crisis alone never kills — run continues");
            Assert.AreEqual(CrisisPhase.None, g.Phase);
        }

        // ---- restart resets the crisis ----

        [Test]
        public void Restart_ResetsCrisis_SoItCanFireAgain()
        {
            var g = StartAndReach(Csv(), blitzLeft: () => true);
            TickToCrisis(g);
            for (int i = 0; i < 5; i++) g.HandleInput(GameInput.AnswerYes);   // clear blitz
            // Finish this life.
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 500) g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(GameState.Finale, g.State);

            g.HandleInput(GameInput.Confirm);   // finale → opener (resets crisis state)
            g.StartLife();                      // fresh life
            g.HandleInput(GameInput.AnswerNo);  // I03
            TickToCrisis(g);
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "restart cleared _crisisDone — the crisis fires again");
        }
    }
}
