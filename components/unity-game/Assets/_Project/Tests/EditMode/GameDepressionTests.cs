using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Depression «тёмная полоса» (CR09): the RANDOM_TRIGGER tail after the crisis, the steady-pulse
    /// «собраться» mini-game (~1.5s metronome interval, ~0.75s window) that is DELIBERATELY the OPPOSITE of the
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

        private static Card Filler(int n = 0)
            => new Card
            {
                Id = "FILL" + n, Question = "FILL?", When = "60", Age = 60, Order = 999 + n,
                YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                NoNecrolog = "жил дальше", Flags = new List<string>(),
            };

        // Обычных карточек после кризиса нужно ХВАТИТЬ на зазор (r3: депрессия не встык за блицем), плюс
        // запас — иначе колода кончится раньше, чем зазор будет выбран, и жизнь уйдёт в финал.
        private static List<Card> Fillers()
        {
            var list = new List<Card>();
            for (int i = 0; i < Game.DepressionGapCards + 6; i++) list.Add(Filler(i));
            return list;
        }

        // A minimal plan re-parsed fresh each life: I03 (starts the age timer) + a filler at 60 so age climbs
        // through the 45–50 crisis window, the whole crisis block, and CR09 carried on the depression slot.
        private static System.Func<DeckPlan> DepressionPlan(string csv, bool withCrisis = true)
        {
            return () =>
            {
                var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
                return new DeckPlan
                {
                    Deck = new List<Card> { byId["I03"] }.Concat(Fillers()).ToList(),
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
                BlitzNormalOnYesRoll = () => true,       // «ВСЁ НОРМАЛЬНО» always on the ДА lever → AnswerYes is always correct
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

        /// <summary>
        /// r3: депрессия ЗАРЯЖАЕТСЯ хвостом кризиса, но ВХОДИТ только после зазора в обычных карточках.
        /// Хелпер проходит зазор обычными ответами и возвращает, сколько карточек на это ушло.
        /// </summary>
        private static int WalkDepressionGap(Game g)
        {
            int cards = 0;
            int guard = 0;
            while (!g.InDepression && g.State == GameState.Playing && guard++ < 50)
            {
                if (g.CurrentCard == null) break;
                g.HandleInput(GameInput.AnswerNo);
                cards++;
            }
            return cards;
        }

        private static Game ReachDepression(string csv)
        {
            var g = StartCrisisAndTail(csv, depressionRoll: true);
            Assert.IsTrue(g.DepressionArmed, "the crisis tail armed depression (gap pending)");
            WalkDepressionGap(g);
            Assert.IsTrue(g.InDepression, "the crisis tail rolled into depression");
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "enters fully desaturated");
            return g;
        }

        // Advance to the next lit pulse and catch it (одно чистое нажатие «!» внутри окна).
        // ⚠ КОНТРОЛ ЛОВЛИ — «!» (CHILD_PRESS), решение основательницы 2026-08-08 по п.3г.
        private static void CatchOnePulse(Game g)
        {
            g.Tick(2.5f);                                 // interval elapses → pulse opens (window full)
            Assert.IsTrue(g.DepressionPulsing, "the dim pulse is lit");
            g.HandleInput(GameInput.ChildPress);          // catch on the pulse — кнопка «!»
        }

        // ---- entry: RANDOM_TRIGGER tail after the crisis ----

        [Test]
        public void Depression_EntersAfterCrisis_WhenRollHits()
        {
            var g = ReachDepression(Csv());
            Assert.AreEqual(GameState.Playing, g.State, "depression is a sub-state of Playing (no state change)");
            Assert.IsFalse(g.InCrisis, "the crisis already resolved before depression");
        }

        /// <summary>
        /// r3 (п.3а): ЗАЗОР «кризис → депрессия». Живой плейтест: депрессия влетала ВСТЫК за блицем, и два
        /// спецрежима подряд читались как один сплошной. Контракт: ролл делается хвостом кризиса (как и
        /// был), но ВХОД откладывается минимум на <see cref="Game.DepressionGapCards"/> ОБЫЧНЫХ карточек.
        /// Зубы: на встык-поведении (вход прямо в ResumeAfterCrisis) первый же Assert краснеет.
        /// </summary>
        [Test]
        public void Depression_WaitsOutAGapOfOrdinaryCards_NotBackToBackWithTheBlitz()
        {
            var g = StartCrisisAndTail(Csv(), depressionRoll: true);

            Assert.IsFalse(g.InDepression, "депрессия НЕ начинается тем же тактом, что кончился блиц");
            Assert.IsTrue(g.DepressionArmed, "…но она заряжена и ждёт зазора");
            Assert.AreEqual(Game.DepressionGapCards, g.DepressionGapLeft, "зазор взведён на полную длину");
            Assert.AreEqual(CrisisPhase.None, g.Phase, "кризис при этом закончен — идёт обычная игра");

            // Каждая обычная карточка выбирает по одному шагу зазора — и НИ ОДНА раньше срока не пускает.
            for (int i = 1; i < Game.DepressionGapCards; i++)
            {
                g.HandleInput(GameInput.AnswerNo);
                Assert.IsFalse(g.InDepression,
                    $"после {i} обычной карточки депрессии всё ещё нет (нужно {Game.DepressionGapCards})");
                Assert.AreEqual(Game.DepressionGapCards - i, g.DepressionGapLeft, "зазор убывает по карточке");
            }

            g.HandleInput(GameInput.AnswerNo);            // …и ровно на N-й она входит
            Assert.IsTrue(g.InDepression, "зазор выбран — депрессия началась");
            Assert.IsFalse(g.DepressionArmed, "…и очередь пуста");
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "входит полностью обесцвеченной");

            // Зазор МЕРЯЕТСЯ КАРТОЧКАМИ, а не временем: время под депрессией не при чём, но и до неё
            // просто «постоять» нельзя — без ответов депрессия не наступит никогда.
            Assert.GreaterOrEqual(Game.DepressionGapCards, 3, "коридор основательницы 3–4 карточки");
        }

        /// <summary>Ожидание зазора НЕ прожигается временем: постой сколько угодно — пока карточки не
        /// отвечены, депрессия не входит (иначе «зазор» стал бы таймером и мог совпасть со встыком).</summary>
        [Test]
        public void DepressionGap_IsCountedInCards_NotInSeconds()
        {
            var g = StartCrisisAndTail(Csv(), depressionRoll: true);
            Assert.IsTrue(g.DepressionArmed);

            g.HandleInput(GameInput.AnswerNo);        // свежая карточка → полный таймер фазы под ногами
            var card = g.CurrentCard;
            int gap0 = g.DepressionGapLeft;
            Assert.Greater(gap0, 0, "зазор ещё не выбран");
            // Стоим на ОДНОЙ карточке, не доводя её таймер до конца (фаза 30+ = 6 с).
            for (int i = 0; i < 20; i++)
            {
                g.Tick(0.1f);
                Assert.IsFalse(g.InDepression, "время само по себе зазор не выбирает");
            }
            Assert.AreSame(card, g.CurrentCard, "карточка та же — таймаута не было");
            Assert.AreEqual(gap0, g.DepressionGapLeft, "зазор считается КАРТОЧКАМИ, а не секундами");
        }

        [Test]
        public void Depression_DoesNotEnter_WhenRollMisses()
        {
            var g = StartCrisisAndTail(Csv(), depressionRoll: false);
            Assert.IsFalse(g.InDepression, "roll missed → no depression");
            Assert.IsFalse(g.DepressionArmed, "…и ничего не заряжено на потом");
            Assert.AreEqual("FILL0", g.CurrentCard.Id, "ordinary play resumed on the suspended card");
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

            g.Tick(0.7f);
            Assert.IsTrue(g.DepressionPulsing, "the ~0.75s window is still open at 0.7s");
            g.Tick(0.1f);
            Assert.IsFalse(g.DepressionPulsing, "the window closes after ~0.75s");
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
            g.HandleInput(GameInput.ChildPress);          // a press OUTSIDE the window = miss
            Assert.AreEqual(Game.DepressionGraySteps, g.DepressionGray, "colour slips one step back toward gray");

            g.HandleInput(GameInput.ChildPress);          // another errant press
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
            g.Tick(0.8f);                                  // let it close UNPRESSED (canon «не нажал в окне»)
            Assert.IsFalse(g.DepressionPulsing);
            Assert.AreEqual(gray + 1, g.DepressionGray, "a pulse missed by inaction slips colour back a step");
        }

        // ---- mashing never wins ----

        [Test]
        public void Depression_Mashing_NeverWins()
        {
            var g = ReachDepression(Csv());
            // Mash «!» every frame across many pulse cycles: the anti-mash lockout means an unlocked
            // press can never coincide with the window, so no catch ever lands and depression never lifts.
            for (int i = 0; i < 600; i++)
            {
                g.HandleInput(GameInput.ChildPress);
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
            StringAssert.StartsWith("FILL", g.CurrentCard.Id, "resumed on an ordinary card, ходом игры");
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

        // ---- «!» is the catch; ЗЕЛЁНАЯ в депрессии инертна ----

        /// <summary>
        /// РЕШЕНИЕ ОСНОВАТЕЛЬНИЦЫ 2026-08-08 (п.3г): ловля пульса — кнопка «!» (CHILD_PRESS), как и стояло
        /// в спеке встречи. Зелёная (ДА) и dev-Enter (CONFIRM) в депрессии НЕ ловят и вообще ничего не
        /// делают: иначе экран звал бы жать «!», а работала бы другая кнопка.
        /// </summary>
        [Test]
        public void Depression_BangButton_IsTheCatch_AndGreenIsInert()
        {
            var g = ReachDepression(Csv());
            g.HandleInput(GameInput.ChildPress);          // outside a window — a miss, NOT a restart
            Assert.AreEqual(GameState.Playing, g.State, "«!» never restarts/leaves play during depression");
            Assert.IsTrue(g.InDepression);

            CatchOnePulse(g);                             // inside the window — a catch (progress)
            Assert.AreEqual(Game.DepressionGraySteps - 1, g.DepressionGray, "«!» in the window catches (progress)");
            Assert.AreEqual(GameState.Playing, g.State, "still Playing (mid-life), not the opener");

            // …а ЗЕЛЁНАЯ и dev-Enter на открытом окне не ловят — прогресса от них нет.
            int gray = g.DepressionGray;
            g.Tick(2.5f);
            Assert.IsTrue(g.DepressionPulsing, "окно ловли открыто");
            Assert.IsFalse(g.HandleInput(GameInput.AnswerYes), "зелёная в депрессии ОТВЕРГНУТА");
            Assert.IsFalse(g.HandleInput(GameInput.Confirm), "…и dev-Enter тоже");
            Assert.AreEqual(gray, g.DepressionGray, "ни одна из них не поймала пульс");
            Assert.IsTrue(g.DepressionPulsing, "…окно так и осталось открытым");
            Assert.IsTrue(g.InDepression, "…и депрессия не кончилась");

            g.HandleInput(GameInput.ChildPress);          // а «!» — ловит
            Assert.AreEqual(gray - 1, g.DepressionGray, "поймала именно «!»");
        }

        // ---- the height sensor is inert in depression (distinct from the CONFIRM catch) ----

        [Test]
        public void Depression_EnergyHold_IsInert()
        {
            var g = ReachDepression(Csv());
            int energy0 = g.Scales.Energy, gray0 = g.DepressionGray;
            // Держим датчик НЕСКОЛЬКО тактов: сигнал непрерывный, поэтому «инертен» обязано означать
            // «не растит ничего СО ВРЕМЕНЕМ», а не «один вызов ничего не сделал».
            for (int i = 0; i < 20; i++) { g.HandleInput(GameInput.EnergyHold); g.Tick(0.1f); }
            Assert.AreEqual(energy0, g.Scales.Energy,
                "поднятый датчик не наполняет батарею в депрессии (шкалы на паузе)");
            Assert.AreEqual(gray0, g.DepressionGray, "…и он не «поймал пульс» — это делает CONFIRM");
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
