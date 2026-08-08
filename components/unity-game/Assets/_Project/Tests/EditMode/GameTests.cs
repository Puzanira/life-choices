using System;
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
            input.Received += i => g.HandleInput(i);    // exactly the wiring GameDriver uses
                                                        // (лямбда, а не группа методов: HandleInput отдаёт
                                                        // «принято ли шкалой» — см. §6-окно)

            input.Confirm();                            // start
            Assert.AreEqual("A?", g.CurrentCard.Question);
            input.Yes();                                // answer A = ДА (health -10)
            Assert.AreEqual(90, g.Scales.Health, "YES branch delta applied via semantic event");
            Assert.AreEqual("B?", g.CurrentCard.Question, "advanced to next card");
        }

        // ---- phase timeout -> random answer ----

        [Test]
        public void CardTimeout_PicksRandomAnswer_AndAdvances()
        {
            var g = new Game(
                new[] { Plain("A", 4, noNec: "n"), Plain("B", 6, noNec: "n") },
                coin: () => false); // timeout resolves to НЕТ
            g.StartLife();
            Assert.AreEqual("A?", g.CurrentCard.Question);

            g.Tick(g.CardTimerMax + 0.01f);             // let the phase timer expire (§3: 4 года → 10 с)
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

            g.Tick(g.CardTimerMax);                       // let I02 time out (random answer) -> I03
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
            // Отрезок 0: отбор по ВЕСУ и лимит 7. Первой реальной строкой идёт самая ранняя ВЕСОМАЯ
            // (CH02 «розетка»), а не самая ранняя вообще — детские ROND-строки (CH01 и компания) теперь
            // попадают только по остаточному принципу.
            Assert.AreEqual("В детстве чуть не тронули розетку — но вовремя одумались.", n.StoryLines[1],
                "первая реальная строка — самая ранняя ВЕСОМАЯ, а не первый попавшийся ROND");
            Assert.LessOrEqual(n.StoryLines.Count, Necrolog.MaxLines,
                "плашка финала держит не больше семи строк, считая родителей");
        }

        // ---- CHAIN honesty: a gated child only appears if the parent resolved ДА ----

        private static Card Gated(string id, int age, string parent)
        {
            var c = Plain(id, age, 0, null, "n");
            c.RequiresParentYes = parent;
            return c;
        }

        [Test]
        public void ChainedChild_Drawn_WhenParentYes()
        {
            var deck = new[] { Plain("P", 4, noNec: "n"), Gated("C", 10, "P") };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            Assert.AreEqual("P?", g.CurrentCard.Question);
            g.HandleInput(GameInput.AnswerYes);            // parent ДА unlocks the child
            Assert.AreEqual("C?", g.CurrentCard.Question, "gated child appears after parent ДА");
        }

        [Test]
        public void ChainedChild_Skipped_WhenParentNo()
        {
            var deck = new[] { Plain("P", 4, noNec: "n"), Gated("C", 10, "P") };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);             // parent НЕТ → child never drawn
            Assert.AreEqual(GameState.Finale, g.State, "no more cards after skipping the gated child");
        }

        [Test]
        public void ChainedChild_Skipped_WhenParentAbsent()
        {
            // Child gated on a parent that isn't in the deck at all → never drawn.
            var deck = new[] { Plain("A", 4, noNec: "n"), Gated("C", 10, "MISSING") };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(GameState.Finale, g.State);
        }

        // ---- reserve top-up: a skipped gated card is replaced so the run length holds ----

        [Test]
        public void SkippedGatedChild_IsReplaced_FromReserve()
        {
            var deck = new[] { Plain("P", 4, noNec: "n"), Gated("C", 10, "P") };
            var reserve = new[] { Plain("R", 12, noNec: "n") };
            var g = new Game(deck, coin: () => false, reserve: reserve);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);           // parent НЕТ → C skipped → R substituted
            Assert.AreEqual(GameState.Playing, g.State, "run continues on the substitute");
            Assert.AreEqual("R?", g.CurrentCard.Question, "reserve card replaced the gated child");
            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual(GameState.Finale, g.State);
        }

        // Contract row 2: the ACTUALLY DRAWN count stays inside the sampler corridor
        // [DeckSampler.MinDeck…MaxDeck] regardless of the answer path — chain-gated skips are topped up
        // from the reserve, so ДА-везде и НЕТ-везде дают забег одной длины.
        // (Коридор был 25–30 до пейсинг-фикса 2026-07-23; имена/сообщения врали — см. DeckSamplerTests.)

        private static int PlayCountingCards(Game g, Func<Card, bool> answerYes)
        {
            g.StartLife();
            int drawn = 0, guard = 0;
            while (g.State == GameState.Playing && g.CurrentCard != null && guard++ < 300)
            {
                drawn++;
                bool yes = answerYes(g.CurrentCard);
                g.HandleInput(yes ? GameInput.AnswerYes : GameInput.AnswerNo);
            }
            return drawn;
        }

        [Test]
        public void DrawnCount_AllNo_StaysInSamplerCorridor()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var all = CardLoader.ParseAll(asset.text);

            for (int seed = 1; seed <= 10; seed++)
            {
                var g = new Game(() => DeckSampler.BuildPlan(all, new System.Random(seed)),
                                 coin: () => false);
                int drawn = PlayCountingCards(g, _ => false); // все НЕТ → all chains closed
                Assert.AreEqual(GameState.Finale, g.State, $"all-НЕТ run ends (seed {seed})");
                Assert.That(drawn, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                    $"drawn count {drawn} within [{DeckSampler.MinDeck},{DeckSampler.MaxDeck}]"
                    + $" on all-НЕТ (seed {seed})");
            }
        }

        [Test]
        public void DrawnCount_AllYes_StaysInSamplerCorridor()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var all = CardLoader.ParseAll(asset.text);

            for (int seed = 1; seed <= 10; seed++)
            {
                var g = new Game(() => DeckSampler.BuildPlan(all, new System.Random(seed)),
                                 coin: () => false);
                // ДА везде (все цепочки открыты), кроме фаталов — иначе забег легитимно короче.
                int drawn = PlayCountingCards(g,
                    c => !c.YesIsFatal && c.DelayedFatalYears == 0);
                Assert.AreEqual(GameState.Finale, g.State, $"all-ДА run ends (seed {seed})");

                // ⚠ ЗАБЕГ, ОБОРВАННЫЙ СМЕРТЬЮ, КОРИДОР НЕ МЕРИТ. Тест проверяет СЭМПЛЕР («сколько карточек
                // он раздаёт»), а не выживание: если шкала кончилась на середине колоды, короткий забег —
                // это правильный ответ игры, а не просадка сэмплера. Ровно та же оговорка, что уже сделана
                // выше для фаталов; с отрезками 1–7 она понадобилась и здесь, потому что «ДА на всё»
                // означает в том числе ДА на «продолжать как раньше» и «не проверяться» (`LT19`, Здр −30).
                bool diedOfScales = g.CardIndex < g.DeckCount - 1;
                if (diedOfScales)
                {
                    Assert.Greater(drawn, 20, $"даже оборванный смертью забег не микроскопический (seed {seed})");
                    continue;
                }
                Assert.That(drawn, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                    $"drawn count {drawn} within [{DeckSampler.MinDeck},{DeckSampler.MaxDeck}]"
                    + $" on all-ДА (seed {seed})");
            }
        }

        // ---- delayed FATAL (RND01): ДА → «за вами пришли» ~3 event-years later, not immediately ----

        private static Card DelayedFatal(string id, int age, int years, string cause)
        {
            var c = Plain(id, age, 0, "взяли деньги", "отказались");
            c.DelayedFatalYears = years;
            c.FatalCause = cause;
            return c;
        }

        [Test]
        public void Rnd01_DelayedFatal_FiresLater_NotImmediately()
        {
            var starter = Plain("I03", 1); starter.StartsAgeTimer = true;
            var deck = new[]
            {
                starter,
                DelayedFatal("RND01", 22, 3, "за вами пришли"),
                Plain("F", 30, noNec: "n"),
            };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);             // resolve I03 → age running
            Assert.AreEqual("RND01?", g.CurrentCard.Question);

            g.HandleInput(GameInput.AnswerYes);            // take the money
            Assert.AreEqual(GameState.Playing, g.State, "life continues — fatal is delayed, not instant");
            Assert.AreEqual("F?", g.CurrentCard.Question);

            g.Tick(3f);                                    // age catches up past 25 (22 + 3)
            Assert.AreEqual(GameState.Finale, g.State, "delayed fatal fires once age crosses 25");
            Assert.AreEqual("за вами пришли", g.Cause);
        }

        [Test]
        public void Rnd01_AsLastCard_Yes_CoastsToDelayedFatal_NotInstantDeath()
        {
            var starter = Plain("I03", 1); starter.StartsAgeTimer = true;
            var deck = new[] { starter, DelayedFatal("RND01", 40, 3, "за вами пришли") };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);             // I03 → age running
            Assert.AreEqual("RND01?", g.CurrentCard.Question, "RND01 is the final card");

            g.HandleInput(GameInput.AnswerYes);            // deck exhausted with the fatal pending
            Assert.AreEqual(GameState.Playing, g.State,
                "no instant death and no «дожил» — the run coasts until the reckoning");
            Assert.IsNull(g.CurrentCard, "no card up while coasting");

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 1000)
                g.Tick(0.05f);                             // age fast-forwards at catch-up rate

            Assert.AreEqual(GameState.Finale, g.State, "delayed fatal fired during the coast");
            Assert.AreEqual("за вами пришли", g.Cause);
            Assert.That(g.Age, Is.EqualTo(43f).Within(0.01f),
                "death exactly at resolved age + 3 (40 + 3), not at answer time");
        }

        [Test]
        public void Rnd01_AsLastCard_No_EndsNaturally()
        {
            var starter = Plain("I03", 1); starter.StartsAgeTimer = true;
            var deck = new[] { starter, DelayedFatal("RND01", 40, 3, "за вами пришли") };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);             // I03
            g.HandleInput(GameInput.AnswerNo);             // decline RND01 → deck ends, nothing pending
            Assert.AreEqual(GameState.Finale, g.State, "natural finale immediately — no coast");
            Assert.AreEqual("спокойная старость", g.Cause, "tone ending, not «за вами пришли»");
        }

        [Test]
        public void Rnd01_No_Schedule_When_Declined()
        {
            var starter = Plain("I03", 1); starter.StartsAgeTimer = true;
            var deck = new[]
            {
                starter,
                DelayedFatal("RND01", 22, 3, "за вами пришли"),
                Plain("F", 30, noNec: "n"),
            };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);             // I03
            g.HandleInput(GameInput.AnswerNo);             // decline RND01 → no schedule
            g.Tick(5f);
            g.HandleInput(GameInput.AnswerNo);             // answer F → deck ends
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreNotEqual("за вами пришли", g.Cause, "declining RND01 never schedules the fatal");
        }

        // ---- natural old-age ending: tone by relationships ----

        private static Card RelCard(string id, int age, int rel)
        {
            var yes = new List<ScaleDelta> { new(Scale.Relationships, DeltaKind.Add, rel) };
            return new Card
            {
                Id = id, Question = id + "?", Age = age, Order = age,
                YesDeltas = yes, NoDeltas = new List<ScaleDelta>(),
                YesNecrolog = "y", NoNecrolog = "n",
                // Отрезок 0: Δ применяется ТОЛЬКО по открытым шкалам. Шкала отношений открывается по
                // возрасту (20) — а этот тест не тикает временем вовсе, поэтому карточка объявляет себя
                // открывающей: ровно тот случай, ради которого правило и знает про `OPEN:*`.
                Flags = new List<string> { "OPEN:" + Card.OpenRelations },
            };
        }

        [Test]
        public void NaturalEnding_JoyfulOldAge_WhenRelationshipsHigh()
        {
            var g = new Game(new[] { RelCard("R", 40, +10) }, coin: () => false); // 55 → 65 >= 60
            g.StartLife();
            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("весёлая старость", g.Cause);
        }

        [Test]
        public void NaturalEnding_LonelyOldAge_WhenRelationshipsLow()
        {
            var g = new Game(new[] { RelCard("R", 40, -10) }, coin: () => false); // 55 → 45 <= 50
            g.StartLife();
            g.HandleInput(GameInput.AnswerYes);
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual("одинокая старость", g.Cause);
        }

        // ---- necrolog cap exercised on a long run (ROND dropped first) ----

        [Test]
        public void LongRun_Necrolog_CapsAtLimit_DroppingRondFirst()
        {
            var deck = new List<Card>();
            for (int i = 0; i < 8; i++)                    // 8 weighty (non-ROND)
                deck.Add(Plain("W" + i, 20 + i, yesNec: "weighty" + i));
            for (int i = 0; i < 12; i++)                   // 12 ROND (droppable)
            {
                var c = Plain("K" + i, 5 + i, yesNec: "kek" + i);
                c.IsRond = true;
                deck.Add(c);
            }
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 100)
                g.HandleInput(GameInput.AnswerYes);

            var n = g.Necrolog;
            Assert.AreEqual(GameState.Finale, g.State);
            Assert.AreEqual(Necrolog.MaxLines, n.StoryLines.Count, "capped at Necrolog.MaxLines");
            foreach (var line in n.StoryLines)
                Assert.IsFalse(line.StartsWith("kek"),
                    "ROND-строки уходят первыми — пока есть весомые, кек в семь строк не попадает");
        }

        // ---- full sampled run to a known ending with a fixed seed ----

        [Test]
        public void SampledFullRun_FixedSeed_AllNo_ReachesEnding()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset);
            var all = CardLoader.ParseAll(asset.text);

            var g = new Game(() => DeckSampler.Build(all, new System.Random(4242)), coin: () => false);
            Assert.That(g.DeckCount, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                $"sampled deck sized [{DeckSampler.MinDeck},{DeckSampler.MaxDeck}] already in the opener");

            g.StartLife();
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 200)
                g.HandleInput(GameInput.AnswerNo);

            Assert.AreEqual(GameState.Finale, g.State, "seeded full run reaches an ending");
            Assert.IsNotNull(g.Necrolog);
            Assert.AreEqual(Necrolog.ParentsLine, g.Necrolog.StoryLines[0], "necrolog opens with parents");
        }

        // ---- FORCED cards never contribute a necrolog line (canon; enforced structurally) ----

        [Test]
        public void ForcedCard_NeverWritesNecrologLine_EvenWithProse()
        {
            // A FORCED card carrying (contrived) necrolog prose must still be excluded structurally.
            var forced = Plain("YA05", 25, yesNec: "НЕ ДОЛЖНО ПОПАСТЬ", noNec: "И ЭТО ТОЖЕ НЕТ");
            forced.IsForced = true;
            var normal = Plain("N", 30, yesNec: "обычная строка");

            var g = new Game(new[] { forced, normal }, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerYes);          // resolve FORCED (ДА)
            g.HandleInput(GameInput.AnswerYes);          // resolve normal → deck ends → finale

            Assert.AreEqual(GameState.Finale, g.State);
            var lines = g.Necrolog.StoryLines;
            CollectionAssert.DoesNotContain(lines, "НЕ ДОЛЖНО ПОПАСТЬ", "FORCED ДА line excluded");
            CollectionAssert.DoesNotContain(lines, "И ЭТО ТОЖЕ НЕТ", "FORCED НЕТ line excluded");
            CollectionAssert.Contains(lines, "обычная строка", "normal card's line still recorded");
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
