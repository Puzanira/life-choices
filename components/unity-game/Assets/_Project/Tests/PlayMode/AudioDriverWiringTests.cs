using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Разводка через НАСТОЯЩИЙ драйвер: игрок жмёт рычаг — звучит верный клип. Это mutation-proof
    /// половина покрытия (отвяжи событие — красное); полноту списка стережёт EditMode-скан исходника.
    /// </summary>
    public class AudioDriverWiringTests
    {
        private GameObject _go;

        private GameDriver Boot(out PlayFakeInputSource fake)
        {
            _go = new GameObject("Driver");
            var driver = _go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private static Card Plain(string id, int order) => new Card
        {
            Id = id, Question = "Вопрос?", When = "10", Age = 10, Order = order,
            YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
            NoNecrolog = "жил дальше", YesNecrolog = "жил дальше", Flags = new List<string>()
        };

        /// <summary>Колода без возрастных открытий: ни один туториал не влезет в проверку звука.</summary>
        private static Game SimpleGame(int n = 6)
        {
            var cards = new List<Card>();
            for (int i = 0; i < n; i++) cards.Add(Plain("SND" + i, i));
            return new Game(cards, coin: () => false);
        }

        private static readonly SoundEvent[] HostStingers =
        {
            SoundEvent.HostPositive, SoundEvent.HostRisky, SoundEvent.HostAbsurd,
            SoundEvent.HostCautious, SoundEvent.HostFatal, SoundEvent.HostSkip
        };

        [UnityTest]
        public IEnumerator Boot_StartsTheThemeLoop()
        {
            var driver = Boot(out _);
            yield return null;
            var music = driver.Audio.DebugLoopSource(LoopChannel.Music);
            Assert.IsNotNull(music, "канал темы построен");
            Assert.IsNotNull(music.clip, "тема-шарманка заведена на опенере");
            Assert.IsTrue(music.loop, "тема крутится лупом");
        }

        [UnityTest]
        public IEnumerator GreenOnOpener_PlaysTheFanfare_AndDealsTheFirstCard()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame());
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            fake.Yes();                       // ЗЕЛЁНАЯ = конфирм на опенере
            Assert.AreEqual(GameState.Playing, driver.Game.State, "жизнь началась");

            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.Opener),
                "опенер обязан звучать фанфарой «НАЧАТЬ ЖИЗНЬ»");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.CardDeal),
                "первая карточка въезжает со своим звуком раздачи");
        }

        /// <summary>
        /// Ответ ДА обязан звучать калимбой ВСЕГДА. Стингер Ведущего сюда НЕ приписан намеренно: он
        /// голос облачка, а облачко приходит ~30–40 % (дефект внедрения 2026-08-08 — см.
        /// <see cref="HostStinger_SoundsOnlyWhenTheBubbleActuallySpeaks"/>). Раньше этот тест требовал
        /// стингер на каждый ответ и тем самым КОДИРОВАЛ баг; требование снято вместе с багом.
        /// </summary>
        [UnityTest]
        public IEnumerator AnswerYes_SpeaksTheKalimbaUp()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame());
            fake.Yes();                                   // опенер → игра
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            fake.Yes();                                   // …а это уже ОТВЕТ ДА
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.AnswerYes),
                "ответ ДА — калимба вверх, голос игры");
        }

        [UnityTest]
        public IEnumerator AnswerNo_SpeaksTheKalimbaDown_NeverABuzzer()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame());
            fake.Yes();
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            fake.No();
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.AnswerNo),
                "«СПАСИБО, НЕ НАДО» — тот же звук, только ниже (правило отбора №1)");
            Assert.IsFalse(driver.Audio.DebugPlayedContains(SoundEvent.AnswerYes),
                "отказ не имеет права прозвучать согласием");
        }

        [UnityTest]
        public IEnumerator HealingCard_PlaysTheWarmKalimba()
        {
            var heal = Plain("HEAL", 0);
            heal.YesDeltas = new List<ScaleDelta>
            {
                new ScaleDelta { Scale = Scale.Health, Kind = DeltaKind.Add, Value = 5 }
            };
            var g = new Game(new List<Card> { heal, Plain("SND1", 1) }, coin: () => false);

            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(g);
            fake.Yes();                                   // опенер → игра, раздана HEAL
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            fake.Yes();                                   // ДА на карточке с плюсом к здоровью
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.Heal),
                "лечение выбором — тёплая калимба через октаву");
        }

        [UnityTest]
        public IEnumerator NonHealingCard_DoesNotPlayHeal()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame());
            fake.Yes();
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            fake.Yes();
            Assert.IsFalse(driver.Audio.DebugPlayedContains(SoundEvent.Heal),
                "обычная карточка не лечит и лечением звучать не должна");
        }

        [UnityTest]
        public IEnumerator StarBurst_IsTheSingleSourceOfTheConfettiSound()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame());
            fake.Yes();
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            driver.StarBurst();
            Assert.AreEqual(1, driver.Audio.DebugCount(SoundEvent.StarBurst),
                "салют звучит ровно один раз на один бёрст");
        }

        [UnityTest]
        public IEnumerator RunningOutOfDeck_EndsOnTheViolin_AndRestartRewindsTheTape()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame(4));
            fake.Yes();                                   // опенер → игра
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 50) fake.No();
            Assert.AreEqual(GameState.Finale, driver.Game.State, "колода кончилась → финал");
            StringAssert.Contains("старость", driver.Game.Cause, "исход колоды — естественная старость");

            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.FinaleOldAge),
                "финал по типу конца: старость = скрипка");
            Assert.IsFalse(driver.Audio.DebugPlayedContains(SoundEvent.FinaleBurnout));
            Assert.IsFalse(driver.Audio.DebugPlayedContains(SoundEvent.FinaleFatal));

            driver.Audio.DebugClear();
            fake.Yes();                                   // «НАЧАТЬ ЗАНОВО» с финала
            Assert.AreEqual(GameState.Opener, driver.Game.State);
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.Restart),
                "рестарт — перемотка плёнки");
        }

        [UnityTest]
        public IEnumerator Restart_LeavesNoStuckCotton()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame(3));
            fake.Yes();

            // Симулируем смерть В ДЕПРЕССИИ: слой сидит в вате, и жизнь заканчивается.
            driver.Audio.EnterDepression();
            Assert.AreEqual(500f, driver.Audio.CurrentCutoff, 0.01f, "вата действительно села");

            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 50) fake.No();
            Assert.AreEqual(GameState.Finale, driver.Game.State);

            Assert.IsFalse(driver.Audio.DepressionActive, "финал обязан звучать в чистом миксе");
            Assert.AreEqual(DepressionMix.CutoffOff, driver.Audio.CurrentCutoff, 0.01f,
                "«застрявшей ваты» после смерти в депрессии быть не может (done contract §4)");

            fake.Yes();                                   // и рестарт тоже чистый
            Assert.AreEqual(DepressionMix.CutoffOff, driver.Audio.CurrentCutoff, 0.01f);
            Assert.AreEqual(0, driver.Audio.DebugEnabledFilterCount);
        }

        /// <summary>
        /// ГАРД ДЕФЕКТА ВНЕДРЕНИЯ (плейтест основательницы 2026-08-08): стингер Ведущего играл на
        /// 100 % ответов, хотя облачко показывается ~30–40 % — пиццикато ложилось поверх калимбы на
        /// КАЖДОЙ карточке. Инвариант: стингер — это ГОЛОС ОБЛАЧКА, а не звук ответа.
        ///
        /// Проверяется ПОКАРТОЧНО, а не по итогу: первая же немая карточка со стингером валит тест.
        /// RNG пула не трогаем — инвариант «звучит ⇔ есть реплика» от вероятности не зависит.
        /// </summary>
        [UnityTest]
        public IEnumerator HostStinger_SoundsOnlyWhenTheBubbleActuallySpeaks()
        {
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame(40));   // без именных реплик → работает пул ~35 %
            fake.Yes();                                 // опенер → игра
            driver.Audio.DebugRecord = true;

            int answered = 0, spoke = 0, silent = 0;
            while (driver.Game.State == GameState.Playing && answered < 40)
            {
                driver.Audio.DebugClear();
                fake.No();
                // ⚠ ПОСЛЕДНЯЯ КАРТОЧКА НЕ НАБЛЮДАЕМА. Колода кончилась → финал → Refresh СТИРАЕТ
                // облачко (`_bubbleTimer.Hide()` на уходе из Playing) ещё до того, как тест успеет
                // его прочитать. Реплика при этом была, и стингер честно прозвучал — то есть проба
                // соврала бы «пиццикато без облачка». Это стоило одного красного прогона suite;
                // инвариант проверяем только на карточках, после которых игра ещё идёт.
                if (driver.Game.State != GameState.Playing) break;
                answered++;

                bool bubble = driver.HostBubbleVisible;
                bool stinger = driver.Audio.DebugPlayed.Any(e => HostStingers.Contains(e));
                Assert.AreEqual(bubble, stinger,
                    "карточка #" + answered + ": стингер обязан звучать РОВНО когда есть реплика "
                    + "(облачко=" + bubble + ", пиццикато=" + stinger + ")");

                if (bubble) spoke++; else silent++;
            }

            Assert.Greater(answered, 10, "прогнали достаточно карточек, чтобы вывод что-то значил");
            Assert.Greater(silent, 0,
                "в выборке обязаны быть НЕМЫЕ карточки — иначе тест не проверил ту самую ветку");
            Assert.Greater(spoke, 0, "…и говорящие тоже, иначе не проверена вторая ветка");
        }

        /// <summary>Контроль к тому же гарду: именная реплика показывается ВСЕГДА — и стингер с ней.</summary>
        [UnityTest]
        public IEnumerator NamedHostLine_AlwaysSpeaks_AndAlwaysStings()
        {
            var named = Plain("NAMED", 0);
            named.HostYes = "Красавчик!";              // именная реплика бьёт пул и показывается всегда
            var g = new Game(new List<Card> { named, Plain("SND1", 1) }, coin: () => false);

            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(g);
            fake.Yes();                                 // опенер → игра, раздана NAMED
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            fake.Yes();                                 // ДА на карточке с именной репликой
            Assert.IsTrue(driver.HostBubbleVisible, "именная реплика показывается всегда");
            Assert.IsTrue(driver.Audio.DebugPlayed.Any(e => HostStingers.Contains(e)),
                "есть реплика — обязан быть и её стингер");
        }

        [UnityTest]
        public IEnumerator AgeMilestones_StayIntentionallySilent()
        {
            // «Будет какофония, если на каждую цифру делать звук». Прогоняем несколько карточек и
            // убеждаемся, что ни одно НЕразведённое событие не всплыло: играет только то, что положено.
            var driver = Boot(out var fake);
            yield return null;
            driver.DebugReplaceGame(SimpleGame(5));
            fake.Yes();
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            for (int i = 0; i < 3; i++) fake.No();

            var allowed = new HashSet<SoundEvent>(HostStingers)
            {
                SoundEvent.AnswerNo, SoundEvent.CardDeal, SoundEvent.Timeout
            };
            var unexpected = driver.Audio.DebugPlayed.Where(e => !allowed.Contains(e)).Distinct().ToList();
            CollectionAssert.IsEmpty(unexpected,
                "на обычных карточках не должно звучать ничего лишнего (вехи возраста — без звука): "
                + string.Join(", ", unexpected));
        }
    }
}
