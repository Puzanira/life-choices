using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    public class GameTests
    {
        private static Card Plain(string id, int age, int yesHealth = 0, string yesNec = null, string noNec = null)
        {
            var yes = new List<ScaleDelta>();
            if (yesHealth != 0) yes.Add(new ScaleDelta(Scale.Health, DeltaKind.Add, yesHealth));
            return new Card
            {
                Id = id, Question = id + "?", Age = age, Order = age,
                YesDeltas = yes, NoDeltas = new List<ScaleDelta>(),
                YesNecrolog = yesNec, NoNecrolog = noNec,
                Flags = new List<string>(),
            };
        }

        private static Card Fatal(string id, int age, string cause)
        {
            var c = Plain(id, age, 0, null, "спаслись");
            c.YesIsFatal = true;
            c.FatalCause = cause;
            return c;
        }

        // ---- state machine ----

        [Test]
        public void StateMachine_Opener_Playing_Finale_Opener()
        {
            var g = new Game(new[] { Fatal("F", 4, "розетка") });
            Assert.AreEqual(GameState.Opener, g.State);

            g.HandleInput(GameInput.Confirm);           // opener -> playing
            Assert.AreEqual(GameState.Playing, g.State);
            Assert.IsNotNull(g.CurrentCard);

            g.HandleInput(GameInput.AnswerYes);         // fatal -> finale
            Assert.AreEqual(GameState.Finale, g.State);

            g.HandleInput(GameInput.Confirm);           // finale -> opener
            Assert.AreEqual(GameState.Opener, g.State);
        }

        [Test]
        public void Opener_Ignores_Answers_Playing_Ignores_Confirm()
        {
            var g = new Game(new[] { Plain("A", 4, noNec: "n") });
            g.HandleInput(GameInput.AnswerYes);         // ignored in opener
            Assert.AreEqual(GameState.Opener, g.State);

            g.HandleInput(GameInput.Confirm);           // start
            var first = g.CurrentCard;
            g.HandleInput(GameInput.Confirm);           // ignored in playing
            Assert.AreSame(first, g.CurrentCard);
        }

        // ---- input abstraction drives choices ----

        [Test]
        public void SemanticInputEvents_DriveChoices()
        {
            var g = new Game(new[] { Plain("A", 4, yesHealth: -10, yesNec: "y"), Plain("B", 6, noNec: "n") });
            var input = new FakeInputSource();
            input.Received += g.HandleInput;            // exactly the wiring GameDriver uses

            input.Confirm();                            // start
            Assert.AreEqual("A?", g.CurrentCard.Question);
            input.Yes();                                // answer A = ДА (health -10)
            Assert.AreEqual(90, g.Scales.Health, "YES branch delta applied via semantic event");
            Assert.AreEqual("B?", g.CurrentCard.Question, "advanced to next card");
        }

        // ---- 5s timeout -> random answer ----

        [Test]
        public void CardTimeout_PicksRandomAnswer_AndAdvances()
        {
            var g = new Game(
                new[] { Plain("A", 4, noNec: "n"), Plain("B", 6, noNec: "n") },
                coin: () => false); // timeout resolves to НЕТ
            g.StartLife();
            Assert.AreEqual("A?", g.CurrentCard.Question);

            g.Tick(Game.CardSeconds + 0.01f);           // let the 5s timer expire
            Assert.AreEqual("B?", g.CurrentCard.Question, "timeout auto-answered and advanced");
        }

        // ---- FATAL immediate end + cause ----

        [Test]
        public void FatalYes_EndsImmediately_WithCause()
        {
            var g = new Game(new[] { Fatal("CH02", 4, "вы сунули палец в розетку"), Plain("B", 6) });
            g.StartLife();
            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("вы сунули палец в розетку", g.Cause);
            Assert.AreEqual("Причина конца: вы сунули палец в розетку", g.Necrolog.CauseLine);
        }

        [Test]
        public void FatalNo_Survives_AndContinues()
        {
            var g = new Game(new[] { Fatal("CH02", 4, "розетка"), Plain("B", 6, noNec: "n") });
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(GameState.Playing, g.State);
            Assert.AreEqual("B?", g.CurrentCard.Question);
        }

        // ---- passive Δ + event-age ----

        [Test]
        public void PassiveDelta_Applied_OnChoice()
        {
            var g = new Game(new[] { Plain("A", 4, yesHealth: -15, yesNec: "y") });
            g.StartLife();
            Assert.AreEqual(100, g.Scales.Health);
            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(85, g.Scales.Health);
        }

        [Test]
        public void EventAge_CatchesUp_And_CardsDrawnInAgeOrder()
        {
            var starter = Plain("I03", 1, noNec: null);
            starter.StartsAgeTimer = true;
            var deck = new[] { starter, Plain("A", 4, noNec: "n"), Plain("B", 10, noNec: "n") };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);           // resolve I03 -> age timer running

            Assert.AreEqual("A?", g.CurrentCard.Question);
            g.Tick(1f);                                  // catch-up 12/s reaches age 4
            Assert.That(g.Age, Is.EqualTo(4f).Within(0.001f), "age caught up to current card");

            int prevAge = g.CurrentCard.Age;
            g.HandleInput(GameInput.AnswerNo);
            Assert.Greater(g.CurrentCard.Age, prevAge, "next card is older (age order)");
            Assert.AreEqual("B?", g.CurrentCard.Question);
        }

        [Test]
        public void AgeTimer_NotRunning_Until_I03_Resolves_EitherAnswer()
        {
            // Real spine subset: I02 first (no timer), then I03 starts it — canon scenes.csv.
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var deck = CardLoader.LoadSubset(asset.text, CardLoader.DefaultSubset);
            var g = new Game(deck, coin: () => false);
            g.StartLife();

            Assert.AreEqual("I02", g.CurrentCard.Id);
            Assert.IsFalse(g.AgeRunning, "age timer stopped on I02");
            g.Tick(3f);                                   // idle on I02 well past any slow tick
            Assert.AreEqual(0f, g.Age, "age stays 0 while I02 is up");

            g.Tick(Game.CardSeconds);                     // let I02 time out (random answer) -> I03
            Assert.AreEqual("I03", g.CurrentCard.Id);
            Assert.IsFalse(g.AgeRunning, "still stopped while I03 is up (starts on resolve)");
            Assert.AreEqual(0f, g.Age);

            g.HandleInput(GameInput.AnswerNo);            // НЕТ also starts it: «шаг всё равно происходит»
            Assert.IsTrue(g.AgeRunning, "resolving I03 starts the age timer (either answer)");
            g.Tick(1f);
            Assert.Greater(g.Age, 0f, "age advances after I03");
        }

        [Test]
        public void Rnd03_WhitePowder_FatalOnYes_SurvivesOnNo()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var rnd03 = CardLoader.LoadSubset(asset.text, new[] { "RND03" })[0];

            var g1 = new Game(new[] { rnd03, Plain("B", 99, noNec: "n") });
            g1.StartLife();
            g1.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(GameState.Finale, g1.State, "ДА on RND03 ends the run immediately");
            Assert.AreEqual("белый порошок", g1.Cause);
            Assert.AreEqual("Причина конца: белый порошок", g1.Necrolog.CauseLine);

            var g2 = new Game(new[] { rnd03, Plain("B", 99, noNec: "n") });
            g2.StartLife();
            g2.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(GameState.Playing, g2.State, "НЕТ on RND03 continues the life");
            Assert.AreEqual("B?", g2.CurrentCard.Question);
        }

        [Test]
        public void HealthDepletion_IsBurnoutEnding()
        {
            var g = new Game(new[] { Plain("A", 4, yesHealth: -100, yesNec: "y") });
            g.StartLife();
            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("здоровье не выдержало", g.Cause);
        }

        // ---- full run smoke on the real spine subset ----

        [Test]
        public void FullRun_AllNo_ReachesNaturalEnding_WithAssembledNecrolog()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var deck = CardLoader.LoadSubset(asset.text, CardLoader.DefaultSubset);
            var g = new Game(deck, coin: () => false);

            g.StartLife();
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 200)
                g.HandleInput(GameInput.AnswerNo);

            Assert.AreEqual(GameState.Finale, g.State, "reached an ending");
            Assert.AreEqual("спокойная старость", g.Cause,
                "all-NO leaves relationships at 56 -> спокойная");

            var n = g.Necrolog;
            Assert.AreEqual(Necrolog.ParentsLine, n.StoryLines[0], "parents line first");
            Assert.AreEqual("Росли аккуратным, брезгливым ребёнком.", n.StoryLines[1],
                "CH01 НЕТ line is the first real story line (age order, intro excluded)");
            Assert.AreEqual(13, n.StoryLines.Count, "parents + 12 choice lines");
        }

        [Test]
        public void Restart_FromFinale_ResetsState()
        {
            var g = new Game(new[] { Plain("A", 4, yesHealth: -30, yesNec: "y") }, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerYes);          // A answered, runs out -> natural finale
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual(70, g.Scales.Health);

            g.HandleInput(GameInput.Confirm);            // back to opener, fresh
            Assert.AreEqual(GameState.Opener, g.State);
            Assert.AreEqual(100, g.Scales.Health, "scales reset on return to opener");

            g.HandleInput(GameInput.Confirm);            // new life
            Assert.AreEqual(GameState.Playing, g.State);
            Assert.AreEqual(100, g.Scales.Health);
        }
    }
}
