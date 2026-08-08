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
    /// ПОВЕДЕНЧЕСКОЕ ПОКРЫТИЕ ЗВУКА и честная граница этого покрытия.
    ///
    /// Гард полноты разводки (<c>AudioWiringTests</c>, EditMode) читает ИСХОДНИК драйвера. Такой скан
    /// отвечает на вопрос «есть ли вообще точка вызова», но НЕ на вопрос «звучит ли верное в верный
    /// момент» — а раньше он ещё и засчитывал имя события, встреченное В КОММЕНТАРИИ (находка Codex
    /// 2026-08-08). Комментарии из скана вырезаны, но подмена вопроса осталась бы, если бы список
    /// «проверено только сканом» нигде не был написан явно.
    ///
    /// Здесь он написан. <see cref="BehaviourCovered"/> — события, которые ПРОГОНЯЮТСЯ ЧЕРЕЗ ЖИВОЙ
    /// ДРАЙВЕР и спрашиваются у слоя журналом <c>AudioLayer.DebugPlayed</c>; у каждого назван тест.
    /// <see cref="ScanOnly"/> — события, поведенчески дорогие для headless, с ПРИЧИНОЙ на каждое.
    /// Вместе они обязаны давать РОВНО перечисление манифеста: новое событие нельзя завести молча.
    /// </summary>
    public class AudioBehaviourCoverageTests
    {
        private static readonly string[] CrisisIds =
            { "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08" };

        /// <summary>Событие → тест, который слышит его через живой драйвер.</summary>
        private static readonly Dictionary<SoundEvent, string> BehaviourCovered = new()
        {
            // AudioDriverWiringTests (соседний файл): рамка жизни и обычная карточка.
            [SoundEvent.MusicTheme] = "AudioDriverWiringTests.Boot_StartsTheThemeLoop",
            [SoundEvent.Opener] = "AudioDriverWiringTests.GreenOnOpener_PlaysTheFanfare…",
            [SoundEvent.CardDeal] = "AudioDriverWiringTests.GreenOnOpener_PlaysTheFanfare…",
            [SoundEvent.AnswerYes] = "AudioDriverWiringTests.AnswerYes_SpeaksTheKalimbaUp",
            [SoundEvent.AnswerNo] = "AudioDriverWiringTests.AnswerNo_SpeaksTheKalimbaDown…",
            [SoundEvent.Heal] = "AudioDriverWiringTests.HealingCard_PlaysTheWarmKalimba",
            [SoundEvent.StarBurst] = "AudioDriverWiringTests.StarBurst_IsTheSingleSource…",
            [SoundEvent.FinaleOldAge] = "AudioDriverWiringTests.RunningOutOfDeck_EndsOnTheViolin…",
            [SoundEvent.Restart] = "AudioDriverWiringTests.RunningOutOfDeck…RestartRewindsTheTape",
            [SoundEvent.HostPositive] = "AudioDriverWiringTests.NamedHostLine_AlwaysStings (семейство)",
            [SoundEvent.HostSkip] = "AudioDriverWiringTests.HostStinger_SoundsOnlyWhenTheBubbleSpeaks",

            // Этот файл.
            [SoundEvent.CrankTick] = "Crank_RingsOnAcceptedTick_AndIsSilentInDepression",
            [SoundEvent.CoinJar] = "Crank_RingsOnAcceptedTick_AndIsSilentInDepression",
            [SoundEvent.Timeout] = "CardTimer_SoundsTheDome_ThenTheTimeout",
            [SoundEvent.DomeLastSecond] = "CardTimer_SoundsTheDome_ThenTheTimeout",
            [SoundEvent.PhoneRing] = "PhoneRing_LivesExactlyWhileTheHandsetIsVisible",
            [SoundEvent.PhonePickup] = "PhoneRing_LivesExactlyWhileTheHandsetIsVisible",
            [SoundEvent.BlitzStart] = "Blitz_SoundsTheSting_TheThought_AndTheHit",
            [SoundEvent.BlitzThought] = "Blitz_SoundsTheSting_TheThought_AndTheHit",
            [SoundEvent.DepressionEnter] = "Depression_SoundsEnter_Pulse_Miss_AndExit",
            [SoundEvent.DepressionPulse] = "Depression_SoundsEnter_Pulse_Miss_AndExit",
            [SoundEvent.DepressionMiss] = "Depression_SoundsEnter_Pulse_Miss_AndExit",
            [SoundEvent.DepressionExit] = "Depression_SoundsEnter_Pulse_Miss_AndExit",
        };

        /// <summary>Событие → ПОЧЕМУ оно осталось на скане исходника, а не на поведении.</summary>
        private static readonly Dictionary<SoundEvent, string> ScanOnly = new()
        {
            [SoundEvent.BlockMoney] = "нужна карточка с ценой И счёт ниже неё в нужный момент",
            [SoundEvent.AlarmMoney] = "тревога — удержанное СОСТОЯНИЕ шкалы (§4), не фронт ввода",
            [SoundEvent.AlarmEnergy] = "то же семейство, тот же файл на другой высоте",
            [SoundEvent.AlarmHealth] = "то же семейство, тот же файл на другой высоте",
            [SoundEvent.EnergyCharge] = "жест зарядки = удержанный датчик кадр за кадром",
            [SoundEvent.BurnoutIn] = "выгорание требует довести энергию до ≤10 % живыми дренажами",
            [SoundEvent.BurnoutOut] = "выход из выгорания — обратная дорога по той же шкале",
            [SoundEvent.ZoneIn] = "красная зона отношений — дрейф оси на длинной дистанции",
            [SoundEvent.ZoneOut] = "то же, но обратный переход",
            [SoundEvent.Breakup] = "расставание — сюжетная цепочка карточек (MD01/RND05)",
            [SoundEvent.SecondChance] = "второй шанс отношений — та же цепочка, дальше по ней",
            [SoundEvent.HealthOpen] = "открытие шкалы здоровья — возрастной гейт 30 + своя модалка",
            [SoundEvent.BadParent] = "два ПРОСПАННЫХ звонка подряд — два полных окна по 5 с",
            [SoundEvent.PhoneMissed] = "проспанное окно: 5 с реального ожидания сверх набора возраста",
            [SoundEvent.Tutorial] = "туш подсказки завязан на конкретные S5/§D-экраны",
            [SoundEvent.Impulse] = "импульс-раунд открывается только с ≥2 провалами блица",
            [SoundEvent.HostRisky] = "тон реплики выбирает классификатор — пул не пиннится вводом",
            [SoundEvent.HostAbsurd] = "то же семейство (маппер ForHostTone проверен в каталоге)",
            [SoundEvent.HostCautious] = "то же семейство",
            [SoundEvent.HostFatal] = "то же семейство",
            [SoundEvent.FinaleBurnout] = "нужен конец ИМЕННО по выгоранию (классификатор — EditMode)",
            [SoundEvent.FinaleFatal] = "нужен FATAL-конец (классификатор — EditMode)",
        };

        [Test]
        public void CoverageSplit_IsExhaustive_AndDisjoint()
        {
            var all = System.Enum.GetValues(typeof(SoundEvent)).Cast<SoundEvent>().ToList();
            var both = BehaviourCovered.Keys.Where(ScanOnly.ContainsKey).ToList();
            CollectionAssert.IsEmpty(both, "событие не может быть одновременно и поведением, и сканом: "
                + string.Join(", ", both));

            var uncovered = all.Where(e => !BehaviourCovered.ContainsKey(e) && !ScanOnly.ContainsKey(e))
                               .OrderBy(e => e.ToString()).ToList();
            CollectionAssert.IsEmpty(uncovered,
                "новое событие обязано попасть ЛИБО в поведенческое покрытие, ЛИБО в список «проверено "
                + "сканом» с причиной — молча заводить звук нельзя: " + string.Join(", ", uncovered));

            var stale = BehaviourCovered.Keys.Concat(ScanOnly.Keys).Where(e => !all.Contains(e)).ToList();
            CollectionAssert.IsEmpty(stale, "в списках остались события, которых уже нет в манифесте");

            foreach (var pair in ScanOnly)
                Assert.IsNotEmpty(pair.Value, "у скан-только события обязана быть причина: " + pair.Key);
        }

        // ---------------------------------------------------------------- общая оснастка

        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static string Csv()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes на месте");
            return asset.text;
        }

        private static Card Plain(string id, int age, int order) => new Card
        {
            Id = id, Question = id + "?", When = age.ToString(), Age = age, Order = order,
            YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
            NoNecrolog = "жил дальше", YesNecrolog = "жил дальше", Flags = new List<string>()
        };

        private static Card Starter()
        {
            var c = Plain("I03", 1, 0);
            c.StartsAgeTimer = true;
            return c;
        }

        /// <summary>Снять любой висящий экран (S5 / §D / входной спецрежима), если он есть.</summary>
        private static void ClearScreens(GameDriver driver, PlayFakeInputSource fake)
        {
            if (driver.SpecialModeShowing || driver.NewScaleShowing || driver.TutorialShowing)
                NewScaleTut.ClearAny(driver, fake);
        }

        // ---------------------------------------------------------------- деньги: находка #1

        /// <summary>Колода, доводящая возраст до <paramref name="holdAge"/> и там его держащая.</summary>
        private static Game HoldGame(int holdAge, int count = 40)
        {
            var deck = new List<Card> { Starter() };
            for (int i = 0; i < count; i++) deck.Add(Plain("H" + i, holdAge, 100 + i));
            return new Game(deck, coin: () => false);
        }

        /// <summary>
        /// НАХОДКА CODEX #1 (MAJOR). Монета в банку играла на ОТВЕРГНУТУЮ крутилку: условие смотрело
        /// на «шкала денег открыта», а не на возврат <c>Game.HandleInput</c>. В депрессии (и в кризисе)
        /// механика ввод глушит — а банка всё равно звенела до пяти раз в секунду поверх самой тихой
        /// сцены игры и забивала SFX-пул. Контроль в том же тесте: в обычной игре тот же тик звенит.
        /// </summary>
        [UnityTest]
        public IEnumerator Crank_RingsOnAcceptedTick_AndIsSilentInDepression()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // --- КОНТРОЛЬ: обычная игра, деньги открыты (18) → принятый тик звенит.
            driver.DebugReplaceGame(HoldGame(20));
            fake.Confirm();                                  // опенер → игра (I03)
            fake.No();                                       // I03 решена → возрастной таймер пошёл
            int guard = 0;
            while (guard++ < 400 && !driver.Game.MoneyOpen && driver.Game.State == GameState.Playing)
            {
                if (driver.SpecialModeShowing || driver.NewScaleShowing || driver.TutorialShowing)
                { ClearScreens(driver, fake); yield return null; continue; }
                driver.DebugTick(0.1f);
            }
            Assert.IsTrue(driver.Game.MoneyOpen, "шкала денег открылась (18 лет)");
            ClearScreens(driver, fake);                      // §D-экран денег снят его же контролом
            yield return null;

            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();
            driver.DebugAdvanceInputClocks(1f);              // кэп дохода перевзведён
            fake.Fire(GameInput.MoneyTick);
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.CrankTick),
                "в обычной игре принятый тик обязан щёлкнуть динамо");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.CoinJar),
                "…и уронить монету в банку");

            Object.Destroy(go);
            yield return null;

            // --- ПРОБА: та же крутилка в депрессии — механика ввод отвергает, значит и звука нет.
            var driver2 = Boot(out var go2, out var fake2);
            yield return null;
            var g = DepressionGame(Csv());
            yield return ReachDepressionAtRest(driver2, g, fake2);
            Assert.IsTrue(g.InDepression, "игра действительно в депрессии");
            Assert.IsTrue(g.MoneyOpen, "…и деньги давно открыты — молчание не от закрытой шкалы");

            driver2.Audio.DebugRecord = true;
            driver2.Audio.DebugClear();
            for (int i = 0; i < 5; i++)
            {
                driver2.DebugAdvanceInputClocks(1f);         // кэп не при чём: каждый тик проходит кэп
                fake2.Fire(GameInput.MoneyTick);
            }
            Assert.IsFalse(driver2.Audio.DebugPlayedContains(SoundEvent.CoinJar),
                "в депрессии крутилка ОТВЕРГНУТА — монета звенеть не имеет права");
            Assert.IsFalse(driver2.Audio.DebugPlayedContains(SoundEvent.CrankTick),
                "…и щелчка динамо тоже нет: звучит только то, что механика засчитала");

            Object.Destroy(go2);
            yield return null;
        }

        /// <summary>Латчи купола и таймаута — оба без события в коде, оба ловятся покадрово.</summary>
        [UnityTest]
        public IEnumerator CardTimer_SoundsTheDome_ThenTheTimeout()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldGame(10));
            fake.Confirm();                                  // опенер → игра
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            int guard = 0;
            while (guard++ < 300 && !driver.Audio.DebugPlayedContains(SoundEvent.Timeout)
                                 && driver.Game.State == GameState.Playing)
            {
                if (driver.SpecialModeShowing || driver.NewScaleShowing || driver.TutorialShowing)
                { ClearScreens(driver, fake); yield return null; continue; }
                driver.DebugTick(0.1f);
            }

            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.DomeLastSecond),
                "последняя секунда купола обязана пропищать ДО таймаута");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.Timeout),
                "истёкшая карточка уезжает со своим звуком");

            Object.Destroy(go);
            yield return null;
        }

        // ---------------------------------------------------------------- телефон: находка #2

        /// <summary>
        /// Колода «ребёнок открыт прямо перед кризисом»: возраст доводится до 44 (все возрастные
        /// экраны снимаются по дороге), там MD02=ДА открывает трубку, а карточки на 45 ждут своей
        /// очереди — кризис срабатывает ровно тогда, когда мы этого захотим.
        /// </summary>
        private static Game CallThenCrisisGame(string csv)
        {
            var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
            var deck = new List<Card> { Starter() };
            for (int i = 0; i < 4; i++) deck.Add(Plain("H" + i, 44, 100 + i));   // набор возраста до 44
            deck.Add(Plain("MD02", 44, 200));                                    // ДА → механика ребёнка
            for (int i = 0; i < 8; i++) deck.Add(Plain("G" + i, 44, 300 + i));   // держим 44, ждём звонка
            for (int i = 0; i < 12; i++) deck.Add(Plain("C" + i, 45, 400 + i));  // 45 → кризис по требованию

            var plan = new DeckPlan
            {
                Deck = deck,
                Reserve = new List<Card>(),
                Crisis = CrisisIds.Select(id => byId[id]).ToList(),
                Depression = byId["CR09"],
            };
            return new Game(() => plan, coin: () => false)
            {
                BlitzNormalOnYesRoll = () => true,       // «ВСЁ НОРМАЛЬНО» приколото к рычагу ДА
                DepressionTriggerRoll = () => false,     // без хвоста-депрессии: нас интересует выход
                ChildFlashInterval = () => 2f,           // звонок раз в 2 с — ждать нечего
            };
        }

        /// <summary>
        /// НАХОДКА CODEX #2 (MAJOR). Луп рингтона снимался ТОЛЬКО в <c>ReflectChildPhone</c>, а под
        /// кризисом эта ветка не выполняется вовсе: <c>Game.Tick</c> уходит в кризис раньше, чем
        /// доходит до ребёнка, звонок остаётся активным, <c>RenderCrisis</c> прячет трубку — и
        /// рингтон продолжал звенеть из-под блица над пустым местом. Теперь условие одно на всех:
        /// звучит РОВНО пока трубка видна и звонок активен.
        /// </summary>
        [UnityTest]
        public IEnumerator PhoneRing_LivesExactlyWhileTheHandsetIsVisible()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CallThenCrisisGame(Csv());
            driver.DebugReplaceGame(g);
            fake.Confirm();                                  // опенер → игра (I03)
            fake.No();                                       // I03 решена → возрастной таймер пошёл

            // Набрать 44 года, снимая по дороге экраны шкал (18/20/25/30).
            int guard = 0;
            while (guard++ < 900 && g.State == GameState.Playing && g.Age < 43.5f)
            {
                if (driver.SpecialModeShowing || driver.NewScaleShowing || driver.TutorialShowing)
                { ClearScreens(driver, fake); yield return null; continue; }
                driver.DebugTick(0.1f);
            }
            Assert.GreaterOrEqual(g.Age, 43.5f, "возраст добрался до 44");

            // Дойти ответами до MD02 и открыть ребёнка (ответы времени не тратят — окно не сгорает).
            guard = 0;
            while (guard++ < 60 && g.State == GameState.Playing && !g.ChildOpen)
            {
                if (driver.SpecialModeShowing || driver.NewScaleShowing || driver.TutorialShowing)
                { ClearScreens(driver, fake); yield return null; continue; }
                if (g.CurrentCard != null && g.CurrentCard.Id == "MD02") fake.Yes();
                else fake.No();
                driver.DebugClearFrameGuards();
            }
            Assert.IsTrue(g.ChildOpen, "MD02=ДА открыл механику ребёнка");
            ClearScreens(driver, fake);                      // §D-экран трубки снят её же контролом
            yield return null;

            // Дождаться живого окна звонка.
            guard = 0;
            while (guard++ < 400 && !g.ChildFlashing && g.State == GameState.Playing && !g.InCrisis)
            {
                if (driver.SpecialModeShowing || driver.NewScaleShowing || driver.TutorialShowing)
                { ClearScreens(driver, fake); yield return null; continue; }
                driver.DebugTick(0.1f);
            }
            Assert.IsTrue(g.ChildFlashing, "окно звонка открыто");
            yield return null;                               // живой кадр: Update свёл луп с видимостью
            Assert.IsTrue(driver.Audio.LoopWanted(LoopChannel.PhoneRing),
                "трубка на экране и звонит — рингтон обязан крутиться лупом");
            Assert.IsTrue(driver.ChildGroup.activeInHierarchy, "…и трубка действительно видна");

            // Ответами (мгновенно, без хода времени) доводим текущую карточку до 45 — и в кризис.
            guard = 0;
            while (guard++ < 30 && g.CurrentCard != null && g.CurrentCard.Age < 45 && !g.InCrisis)
            {
                fake.No();
                driver.DebugClearFrameGuards();
            }
            guard = 0;
            while (guard++ < 100 && !g.InCrisis && g.State == GameState.Playing) driver.DebugTick(0.05f);
            Assert.IsTrue(g.InCrisis, "кризис среднего возраста начался");
            Assert.IsTrue(g.ChildFlashing, "…и звонок всё ещё активен — иначе проба ничего не значит");

            yield return null;                               // кадр под ВХОДНЫМ экраном блица
            Assert.IsFalse(driver.Audio.LoopWanted(LoopChannel.PhoneRing),
                "трубки на экране нет — рингтон обязан молчать (звенел из-под кризиса до фикса)");

            NewScaleTut.ClearSpecial(driver, fake);          // объявление блица снято зелёной
            yield return null;
            Assert.IsFalse(driver.ChildGroup.activeInHierarchy, "блиц убрал трубку с доски");
            Assert.IsFalse(driver.Audio.LoopWanted(LoopChannel.PhoneRing),
                "…и под самим блицем рингтон тоже молчит");

            // Пройти блиц чисто (ДА = «ВСЁ НОРМАЛЬНО») → кризис кончается без импульс-раунда.
            guard = 0;
            while (guard++ < 12 && g.InCrisis) { fake.Yes(); driver.DebugClearFrameGuards(); }
            Assert.IsFalse(g.InCrisis, "блиц пройден без провалов — кризис закрылся");

            yield return null;
            Assert.IsTrue(g.ChildFlashing, "окно звонка пережило кризис (в кризис его часы не идут)");
            Assert.IsTrue(driver.Audio.LoopWanted(LoopChannel.PhoneRing),
                "трубка вернулась на экран — вернулся и рингтон");

            // …и «алло»: поднятая трубка глушит луп тем же вызовом, каким звучит калимба.
            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();
            fake.Fire(GameInput.ChildPress);
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.PhonePickup),
                "поднятая трубка отвечает калимбой «алло»");
            Assert.IsFalse(driver.Audio.LoopWanted(LoopChannel.PhoneRing),
                "…и рингтон замолкает тем же мгновением, а не следующим кадром");

            Object.Destroy(go);
            yield return null;
        }

        // ---------------------------------------------------------------- блиц и депрессия

        /// <summary>
        /// Блиц: перебивка на входе, whoosh на каждой мысли и ГОЛОС ПОПАДАНИЯ. Последний до правки
        /// был мёртвым кодом — он стоял внутри accepted-блока, а <c>Game.HandleInput</c> в блице
        /// всегда возвращает false (рычаг разбирает ветка кризиса). Провал звучал, попадание — нет.
        /// </summary>
        [UnityTest]
        public IEnumerator Blitz_SoundsTheSting_TheThought_AndTheHit()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame(Csv());
            driver.DebugReplaceGame(g);
            driver.Audio.DebugRecord = true;
            fake.Confirm();
            g.HandleInput(GameInput.AnswerNo);               // I03 решена → возрастной таймер пошёл

            driver.Audio.DebugClear();
            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); yield return null; continue; }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "дошли до блица");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.BlitzStart),
                "вход в кризис — драматическая перебивка");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.BlitzThought),
                "первая мысль въезжает со своим whoosh");

            NewScaleTut.ClearSpecial(driver, fake);
            yield return null;

            driver.Audio.DebugClear();
            fake.Yes();                                      // ДА = «ВСЁ НОРМАЛЬНО» (приколото роллом)
            driver.DebugClearFrameGuards();
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.AnswerYes),
                "ПОПАДАНИЕ в блице обязано звучать голосом игры (до фикса молчало)");
            Assert.AreEqual(0, g.BlitzFails, "…и это действительно было попадание, а не промах");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>Депрессия целиком: вход «в вату», удар сердца, ватный «пшик» промаха и выход.</summary>
        [UnityTest]
        public IEnumerator Depression_SoundsEnter_Pulse_Miss_AndExit()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = DepressionGame(Csv());
            driver.Audio.DebugRecord = true;
            yield return ReachDepressionAtRest(driver, g, fake);
            Assert.IsTrue(g.InDepression, "игра в депрессии");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.DepressionEnter),
                "вход в депрессию — обратная тарелка, и микс садится в вату");
            Assert.IsTrue(driver.Audio.DepressionActive, "…фильтр действительно включён");

            // ПРОМАХ: окно пульса закрыто, «!» по пустому месту → ватный «пшик», серость не сдвинулась.
            driver.Audio.DebugClear();
            int grayBefore = g.DepressionGray;
            fake.Fire(GameInput.ChildPress);
            Assert.AreEqual(grayBefore, g.DepressionGray, "промах цвет не возвращает");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.DepressionMiss),
                "промах ловли — глухой ватный удар");

            // ПУЛЬС: окно открывается латчем покадрово (события в коде нет).
            driver.Audio.DebugClear();
            int guard = 0;
            while (guard++ < 200 && !g.DepressionPulsing) driver.DebugTick(0.1f);
            Assert.IsTrue(g.DepressionPulsing, "окно пульса открылось");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.DepressionPulse),
                "одинокий удар сердца — на КАЖДОЕ открытие окна");

            // ВЫХОД: ловим все ступени → «мир снова включили».
            driver.Audio.DebugClear();
            guard = 0;
            while (guard++ < 400 && g.InDepression)
            {
                if (g.DepressionPulsing) fake.Fire(GameInput.ChildPress);
                else driver.DebugTick(0.1f);
            }
            Assert.IsFalse(g.InDepression, "пять попаданий подняли депрессию");
            Assert.IsTrue(driver.Audio.DebugPlayedContains(SoundEvent.DepressionExit),
                "выход из депрессии — риза «мир снова включили»");
            Assert.IsFalse(driver.Audio.DepressionActive, "…и вата снята со всего микса");

            Object.Destroy(go);
            yield return null;
        }

        // ---------------------------------------------------------------- драйв до кризиса/депрессии
        // (та же дорога, что в CrisisConformanceTests / DepressionHudTests — колода-эталон из CSV)

        private static Game CrisisGame(string csv)
        {
            var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
            var plan = new DeckPlan
            {
                Deck = new List<Card> { byId["I03"], Plain("FILL", 60, 999) },
                Reserve = new List<Card>(),
                Crisis = CrisisIds.Select(id => byId[id]).ToList(),
                Depression = byId["CR09"],
            };
            return new Game(() => plan, coin: () => false)
            {
                BlitzNormalOnYesRoll = () => true,
                DepressionTriggerRoll = () => false,
            };
        }

        private static Game DepressionGame(string csv)
        {
            var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
            var fillers = new List<Card>();
            for (int i = 0; i < Game.DepressionGapCards + 6; i++) fillers.Add(Plain("FILL" + i, 60, 999 + i));
            var plan = new DeckPlan
            {
                Deck = new List<Card> { byId["I03"] }.Concat(fillers).ToList(),
                Reserve = new List<Card>(),
                Crisis = CrisisIds.Select(id => byId[id]).ToList(),
                Depression = byId["CR09"],
            };
            return new Game(() => plan, coin: () => false)
            {
                BlitzNormalOnYesRoll = () => true,
                DepressionTriggerRoll = () => true,
                DepressionPulseInterval = () => 2.5f,
            };
        }

        private static IEnumerator ReachDepressionAtRest(GameDriver driver, Game g, PlayFakeInputSource fake)
        {
            driver.DebugReplaceGame(g);
            fake.Confirm();
            g.HandleInput(GameInput.AnswerNo);
            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); yield return null; continue; }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "дошли до блица");
            NewScaleTut.ClearSpecial(driver, fake);
            yield return null;
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);
            for (int i = 0; i < 5; i++) { fake.Yes(); driver.DebugClearFrameGuards(); }

            Assert.IsTrue(g.DepressionArmed, "хвост кризиса зарядил депрессию");
            guard = 0;
            while (guard++ < 400 && !g.InDepression && g.State == GameState.Playing)
            {
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); yield return null; continue; }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                fake.No();
                driver.DebugClearFrameGuards();
                g.Tick(0.05f);
            }
            Assert.IsTrue(g.InDepression, "зазор пройден — депрессия началась");
            if (driver.SpecialModeShowing) NewScaleTut.ClearSpecial(driver, fake);
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);
            yield return null;
        }
    }
}
