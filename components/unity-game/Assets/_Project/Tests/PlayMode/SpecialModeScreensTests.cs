using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// ПЛЕЙТЕСТ-ФИКСЫ r3 (живой плейтест основательницы 2026-08-07) — входные экраны спецрежимов и подача.
    ///
    /// Здесь проверяется ровно то, чего до сих пор не было: спецрежимы (здоровье 30, блиц 45, депрессия,
    /// первое выгорание) начинались БЕЗ ОБЪЯСНЕНИЙ либо объяснялись жёлтой S5-плашкой, выпадающей из
    /// арт-пака. Теперь у каждого — ВХОДНОЙ ЭКРАН, собранный из тех же блоков, что §D-модалка шкал:
    /// затемнение · крупный виджет · облачко-рассказ · окно-задача · зелёная CTA. Плюс подача:
    /// панч плашек только на принятый ответ (п.4), BLOCK$-баннер поверх кнопок (п.6) и отклик на
    /// пропущенный звонок (п.9).
    /// </summary>
    public class SpecialModeScreensTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        /// <summary>Колода, держащая возраст: ничего лишнего не открывается посреди измерения.</summary>
        private static Game HoldDeck(int holdAge)
        {
            var deck = new List<Card> { Starter() };
            for (int i = 0; i < 60; i++)
            {
                var c = Plain("F" + i, holdAge);
                c.Order = holdAge + i;
                deck.Add(c);
            }
            return new Game(deck, coin: () => false);
        }

        private static readonly SpecialMode[] AllModes =
            { SpecialMode.Health, SpecialMode.Blitz, SpecialMode.Depression, SpecialMode.Burnout };

        /// <summary>Бокс ректа в реф-px (x от ЛЕВОГО края, y от ВЕРХНЕГО): L, T, R, B.</summary>
        private static Vector4 RefBox(RectTransform canvas, RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var l = canvas.InverseTransformPoint(c[i]);
                minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x);
                minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
            }
            float X(float v) => (v / canvas.rect.width + 0.5f) * 1920f;
            float Y(float v) => (0.5f - v / canvas.rect.height) * 1080f;
            return new Vector4(X(minX), Y(maxY), X(maxX), Y(minY));
        }

        // =====================================================================================
        // 1. КАРКАС: каждый спецрежим входит через экран, собранный из блоков §D + зелёная CTA
        // =====================================================================================

        /// <summary>
        /// done-contract §2/§6: все четыре входа поднимают ОДИН И ТОТ ЖЕ каркас — затемнение §D, окно-
        /// рассказ, окно-задача с непустым канон-черновиком и ЗЕЛЁНАЯ CTA в токене кабинета. Не «похожая
        /// вёрстка на каждый экран», а буквально те же объекты оверлея.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryMode_RaisesTheSameDStyleFrame_WithAGreenCta(
            [ValueSource(nameof(AllModes))] SpecialMode mode)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Yes();                                  // opener → playing
            yield return null;

            driver.DebugShowSpecialMode(mode);
            yield return null;

            Assert.IsTrue(driver.SpecialModeShowing, mode + ": экран поднят");
            Assert.AreEqual(mode, driver.SpecialModeKind, mode + ": именно он");

            // …и это ТЕ ЖЕ блоки, что у §D-модалки шкал (переиспользование, а не вторая вёрстка).
            Assert.IsTrue(driver.NewScaleOverlay.activeInHierarchy, mode + ": общий оверлей §D поднят");
            Assert.IsTrue(driver.NewScaleDim.gameObject.activeInHierarchy, mode + ": затемнение §D");
            Assert.AreEqual(GameDriver.NewScaleDimAlpha, driver.NewScaleDim.color.a, 0.02f,
                mode + ": то же ~40 % INK, что у экранов шкал");
            Assert.IsTrue(driver.NewScaleTaskPlate.gameObject.activeInHierarchy, mode + ": окно-задача §D");
            Assert.IsTrue(driver.NewScaleStoryBubble.gameObject.activeInHierarchy, mode + ": облачко §D");

            // Тексты — канон-черновики host-content §4b, непустые и разные по ролям.
            Assert.AreEqual(GameDriver.SpecialStory(mode), driver.NewScaleStoryText.text, mode + ": рассказ");
            Assert.AreEqual(GameDriver.SpecialTask(mode), driver.NewScaleTaskText.text, mode + ": задача");
            Assert.IsNotEmpty(driver.NewScaleStoryText.text, mode + ": рассказ не пустой");
            Assert.IsNotEmpty(driver.NewScaleTaskText.text, mode + ": задача не пустая");
            Assert.AreNotEqual(driver.NewScaleStoryText.text, driver.NewScaleTaskText.text,
                mode + ": рассказ и задача — разные тексты, а не одно и то же дважды");

            // CTA — ЗЕЛЁНАЯ, тем же токеном и тем же блоком, что опенер и финал.
            Assert.IsTrue(driver.SpecialModeCta.gameObject.activeInHierarchy, mode + ": зелёная CTA на экране");
            Assert.AreEqual(GameDriver.SpecialModeCtaText, driver.SpecialModeCtaLabel.text, mode + ": подпись CTA");
            StringAssert.Contains("ЗЕЛЁНУЮ", driver.SpecialModeCtaLabel.text,
                mode + ": CTA называет ФИЗИЧЕСКУЮ кнопку кабинета");
            // …и красится ТЕМ ЖЕ ТИНТОМ, что CTA опенера: оба — `bar-track`, пропущенный через один и тот
            // же токен GoGreen (OnBarTrack делит на заливку спрайта, uGUI умножает обратно). Сравнение с
            // живым опенером, а не с числом, — это и есть доказательство «собрано из готового блока».
            var openerCta = driver.OpenerPanel.transform.Find("StartPlate").GetComponent<Image>();
            Assert.AreSame(openerCta.sprite, driver.SpecialModeCta.sprite, mode + ": тот же спрайт плашки");
            Assert.AreEqual(openerCta.color, driver.SpecialModeCta.color, mode + ": тот же зелёный токен");
            Assert.AreEqual(Image.Type.Sliced, driver.SpecialModeCta.type, mode + ": та же 9-slice сборка");
            Assert.Greater(driver.SpecialModeCta.color.g, driver.SpecialModeCta.color.r + 0.2f,
                mode + ": …и он действительно зелёный");

            // §D-модалка шкал при этом НЕ поднята — это разные экраны на одном оверлее.
            Assert.IsFalse(driver.NewScaleShowing, mode + ": экран шкалы не поднят одновременно");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// done-contract §4: ПОКА ЭКРАН ВИСИТ, СТОИТ ВСЁ. Умереть, читая правила, нельзя — это была
        /// прямая претензия основательницы к выгоранию («первый раз читаешь и умираешь»).
        /// Проверяем на самом злом входе: выгорание при энергии на дне и здоровье, которое уже тает.
        /// </summary>
        [UnityTest]
        public IEnumerator UnderTheScreen_NothingTicks_NoScaleCanKillYou(
            [ValueSource(nameof(AllModes))] SpecialMode mode)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldDeck(35));
            fake.Yes();                                   // opener → playing
            int guard = 0;
            while (guard++ < 400 && driver.Game.Age < 34f)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.1f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;

            driver.Game.Scales.Energy = 5;                // на дне: живой дренаж убил бы за пару секунд
            driver.Game.Scales.Health = 4;
            driver.DebugShowSpecialMode(mode);
            yield return null;
            Assert.IsTrue(driver.SpecialModeShowing, mode + ": экран поднят");
            Assert.IsTrue(driver.Game.Paused, mode + ": экран ставит паузу");

            float age0 = driver.Game.Age;
            double money0 = driver.Game.Money;
            float timer0 = driver.Game.CardTimer;
            int e0 = driver.Game.Scales.Energy, h0 = driver.Game.Scales.Health;

            for (int i = 0; i < 200; i++) driver.DebugTick(0.1f);   // 20 секунд под экраном

            Assert.AreEqual(GameState.Playing, driver.Game.State, mode + ": под экраном НЕЛЬЗЯ умереть");
            Assert.AreEqual(age0, driver.Game.Age, 1e-3f, mode + ": возраст заморожен");
            Assert.AreEqual(money0, driver.Game.Money, 1e-6, mode + ": стоимость жизни заморожена");
            Assert.AreEqual(timer0, driver.Game.CardTimer, 1e-3f, mode + ": таймер карточки заморожен");
            Assert.AreEqual(e0, driver.Game.Scales.Energy, mode + ": дренаж энергии заморожен");
            Assert.AreEqual(h0, driver.Game.Scales.Health, mode + ": таяние здоровья заморожено");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Экран закрывается ЗЕЛЁНОЙ и ТОЛЬКО ей. Красный рычаг, крутилка, датчик, джойстик и «!» под ним
        /// инертны — под замороженным временем «нафармить» иначе можно было бы что угодно.
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyGreenClosesTheScreen_EveryOtherControlIsInert()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldDeck(30));
            fake.Yes();
            int guard = 0;
            while (guard++ < 400 && !driver.Game.MoneyOpen) { NewScaleTut.ClearAny(driver, fake); driver.DebugTick(0.1f); }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;

            driver.DebugShowSpecialMode(SpecialMode.Health);
            yield return null;
            double money0 = driver.Game.Money;
            int energy0 = driver.Game.Scales.Energy;
            int rel0 = driver.Game.Scales.Relationships;
            var card0 = driver.Game.CurrentCard;

            fake.No();                                    // красный
            fake.Fire(GameInput.MoneyTick);               // крутилка
            fake.Fire(GameInput.EnergyHold);              // датчик
            fake.Fire(GameInput.RelationUp);              // джойстик
            fake.Fire(GameInput.ChildPress);              // «!»
            driver.DebugTick(0.2f);

            Assert.IsTrue(driver.SpecialModeShowing, "ни один чужой контрол экран не закрыл");
            Assert.AreEqual(money0, driver.Game.Money, 1e-6, "…и крутилка на паузе НЕ печатает деньги");
            Assert.AreEqual(energy0, driver.Game.Scales.Energy, "…датчик не наполняет батарею");
            Assert.AreEqual(rel0, driver.Game.Scales.Relationships, "…джойстик не двигает маркер");
            Assert.AreSame(card0, driver.Game.CurrentCard, "…и красный не отвечает за игрока");

            fake.Yes();                                   // ЗЕЛЁНАЯ
            Assert.IsFalse(driver.SpecialModeShowing, "зелёная — и только она — снимает экран");
            Assert.IsFalse(driver.Game.Paused, "пауза снята");
            Assert.AreSame(card0, driver.Game.CurrentCard, "…и зелёная НЕ ответила за игрока на карточку");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 2. Каждый вход — СВОИМ событием (п.1, 2, 3б, 5а)
        // =====================================================================================

        /// <summary>п.1: здоровье (30) объясняется входным экраном, а не старой жёлтой S5-плашкой.</summary>
        [UnityTest]
        public IEnumerator HealthOpen_RaisesTheScreen_NotTheOldYellowHint()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldDeck(31));
            fake.Yes();

            int guard = 0;
            while (guard++ < 600 && !driver.SpecialModeShowing && driver.Game.State == GameState.Playing)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); continue; }
                driver.DebugTick(0.1f);
            }
            Assert.IsTrue(driver.SpecialModeShowing, "экран поднялся");
            Assert.AreEqual(SpecialMode.Health, driver.SpecialModeKind, "…именно здоровья");
            Assert.IsTrue(driver.Game.HealthDecaying, "…ровно в момент, когда здоровье начало таять");
            Assert.IsFalse(driver.TutorialShowing, "жёлтой S5-плашки «ПОНЯТНО» больше нет");
            Assert.IsTrue(driver.NewScaleBigWidget != null && driver.NewScaleBigWidget == driver.HealthGroup,
                "крупный виджет — НАСТОЯЩИЙ бар здоровья, одолженный из HUD");

            var canvas = driver.CanvasRect;
            // Мерим НАРИСОВАННЫЙ бар (сама группа растянута на весь канвас — её бокс ни о чём не говорит).
            var big = RefBox(canvas, driver.HealthBarImage.rectTransform);
            Assert.Greater(((RectTransform)driver.HealthGroup.transform).localScale.x, 1f,
                "…и он КРУПНЕЕ своего HUD-размера");
            Assert.GreaterOrEqual(big.x, -1f, "крупный бар целиком в кадре слева");
            Assert.LessOrEqual(big.z, 1921f, "…и справа");
            Assert.GreaterOrEqual(big.y, -1f, "…и сверху");
            Assert.LessOrEqual(big.w, 1081f, "…и снизу");

            fake.Yes();
            Assert.IsFalse(driver.SpecialModeShowing, "закрывается зелёной");
            Assert.AreSame(driver.HudRow.transform, driver.HealthGroup.transform.parent,
                "…и бар вернулся в HUD-ряд");
            Assert.AreEqual(1f, ((RectTransform)driver.HealthGroup.transform).localScale.x, 1e-3f,
                "…в обычном размере");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>п.2: кризис объявляет БЛИЦ входным экраном, и кризисный таймер под ним СТОИТ.</summary>
        [UnityTest]
        public IEnumerator CrisisStart_RaisesTheBlitzScreen_AndTheBlitzClockIsFrozenUnderIt()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame();
            driver.DebugReplaceGame(g);
            fake.Yes();                                   // opener → playing (I03)
            g.HandleInput(GameInput.AnswerNo);            // I03 → возраст пошёл

            int guard = 0;
            while (guard++ < 4000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "доехали до блица");
            Assert.IsTrue(driver.SpecialModeShowing, "…и он объявлен ВХОДНЫМ ЭКРАНОМ, а не молча");
            Assert.AreEqual(SpecialMode.Blitz, driver.SpecialModeKind, "именно блица");
            Assert.AreEqual(HostContent.CrisisAnnounce, driver.NewScaleStoryText.text,
                "рассказ = каноничное объявление кризиса");
            StringAssert.Contains("ВСЁ НОРМАЛЬНО", driver.NewScaleTaskText.text,
                "задача = правила блица: по какой кнопке бить");
            StringAssert.Contains("пять", driver.NewScaleTaskText.text.ToLowerInvariant(),
                "…и сколько их (пять мыслей)");

            // Кризисный таймер СТОИТ: за 20 «секунд» чтения ни одна мысль не проваливается.
            float t0 = g.CrisisTimer;
            int thought0 = g.BlitzThoughtNumber, fails0 = g.BlitzFails;
            for (int i = 0; i < 200; i++) driver.DebugTick(0.1f);
            Assert.AreEqual(t0, g.CrisisTimer, 1e-3f, "таймер блица под экраном заморожен");
            Assert.AreEqual(thought0, g.BlitzThoughtNumber, "мысль не сменилась");
            Assert.AreEqual(fails0, g.BlitzFails, "…и провалов не набежало");

            // Зелёная стартует блиц: с этого момента таймер идёт.
            fake.Yes();
            driver.DebugClearFrameGuards();
            Assert.IsFalse(driver.SpecialModeShowing, "экран снят зелёной");
            driver.DebugTick(0.5f);
            Assert.Less(g.CrisisTimer, t0, "…и кризисный таймер пошёл");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>п.3б: депрессия объясняет ловлю пульса ДО того, как начнёт капать серость.</summary>
        [UnityTest]
        public IEnumerator DepressionStart_RaisesTheScreen_AndThePulseSchedulerIsFrozenUnderIt()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame();
            driver.DebugReplaceGame(g);
            fake.Yes();
            g.HandleInput(GameInput.AnswerNo);

            int guard = 0;
            while (guard++ < 4000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.2f);
            }
            NewScaleTut.ClearSpecial(driver, fake);       // снять экран блица
            for (int i = 0; i < 5; i++) { fake.Yes(); driver.DebugClearFrameGuards(); }   // чистый блиц
            Assert.IsTrue(g.DepressionArmed, "хвост кризиса зарядил депрессию (вход — через зазор)");

            guard = 0;
            while (guard++ < 40 && !g.InDepression && g.State == GameState.Playing)
            {
                fake.No();
                driver.DebugClearFrameGuards();
                // ⚠ ТОЛЬКО §D/S5: экран депрессии, ради которого мы тут, снимать нельзя.
                if (driver.NewScaleShowing) NewScaleTut.Clear(driver, fake);
                if (driver.TutorialShowing) { fake.Confirm(); driver.DebugClearFrameGuards(); }
            }
            Assert.IsTrue(g.InDepression, "депрессия началась после зазора");
            Assert.IsTrue(driver.SpecialModeShowing, "…и объяснена входным экраном");
            Assert.AreEqual(SpecialMode.Depression, driver.SpecialModeKind, "именно депрессии");
            Assert.AreEqual(HostContent.DepressionAnnounce, driver.NewScaleStoryText.text,
                "рассказ = глухое объявление «ТЁМНАЯ ПОЛОСА…»");
            StringAssert.Contains(GameDriver.DepressionCatchControlName, driver.NewScaleTaskText.text,
                "задача НАЗЫВАЕТ контрол ловли (п.3г — через единственную константу)");
            Assert.IsNull(driver.NewScaleBigWidget,
                "у депрессии крупного виджета НЕТ: её звезда-пульс на плашке-задаче читалась бы наклейкой");

            int gray0 = g.DepressionGray;
            for (int i = 0; i < 200; i++) driver.DebugTick(0.1f);
            Assert.AreEqual(gray0, g.DepressionGray, "под экраном цвет не уползает — промахов не начисляют");
            Assert.IsFalse(g.DepressionPulsing, "…и пульс не мигает: планировщик стоит");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>п.5а/б: первое выгорание — экран с крупной КРАСНОЙ батареей; повторное — короткая плашка.</summary>
        [UnityTest]
        public IEnumerator FirstBurnout_IsAScreen_RepeatIsAShortPlate()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldDeck(27));
            fake.Yes();
            int guard = 0;
            while (guard++ < 900 && !driver.Game.EnergyOpen && driver.Game.State == GameState.Playing)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.1f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;

            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            driver.DebugTick(0.05f);
            Assert.IsTrue(driver.Game.Burnout, "выгорание защёлкнулось");
            Assert.IsTrue(driver.SpecialModeShowing, "ПЕРВОЕ — входной экран");
            Assert.AreEqual(SpecialMode.Burnout, driver.SpecialModeKind, "именно выгорания");
            StringAssert.Contains("датчик высоты", driver.NewScaleTaskText.text,
                "задача — формулировка основательницы: зажми датчик высоты и держи");
            Assert.IsTrue(driver.NewScaleBigWidget == driver.EnergyGroup,
                "крупный виджет — НАСТОЯЩАЯ батарея (она же и красная: энергия на дне)");
            Assert.Greater(((RectTransform)driver.EnergyGroup.transform).localScale.x, 1f, "…и крупная");
            Assert.IsFalse(driver.BurnoutPlateGroup.activeSelf,
                "короткая плашка при этом НЕ дублирует экран");

            fake.Yes();                                    // прочитал
            driver.DebugClearFrameGuards();
            yield return null;
            Assert.IsFalse(driver.SpecialModeShowing, "экран снят");
            Assert.IsTrue(driver.Game.Burnout, "…выгорание продолжается");
            Assert.IsTrue(driver.BurnoutPlateGroup.activeSelf, "…и теперь висит КОРОТКАЯ плашка");

            // Выйти из выгорания и войти снова — второго экрана НЕ будет, только плашка.
            guard = 0;
            while (guard++ < 400 && driver.Game.Burnout)
            {
                fake.Fire(GameInput.EnergyHold);
                driver.DebugTick(0.05f);
            }
            Assert.IsFalse(driver.Game.Burnout, "вышли");
            driver.DebugTick(Game.BurnoutGraceSeconds + 0.5f);   // переждать грейс
            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            driver.DebugTick(0.05f);
            Assert.IsTrue(driver.Game.Burnout, "выгорание ПОВТОРНОЕ");
            Assert.IsFalse(driver.SpecialModeShowing, "…и оно НЕ блокирует игру вторым экраном");
            Assert.IsFalse(driver.Game.Paused, "…паузы нет");
            Assert.IsTrue(driver.BurnoutPlateGroup.activeSelf, "…только короткая плашка");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// п.5б, находка ревью r3 (MAJOR): ПЕРВОЕ выгорание и короткая плашка ВЗАИМОИСКЛЮЧАЮЩИ.
        ///
        /// Плашка живёт «off <see cref="Game.Burnout"/>» в <c>Update</c>, и без гейта первый раз показывал
        /// ОБА сообщения разом: под затемнением входного экрана в полосе HUD зажигалась ещё и короткая
        /// плашка. Одно и то же сказано дважды, причём вторым — тем языком, который основательница
        /// назначила ПОВТОРНЫМ выгораниям.
        ///
        /// ⚠ Тест идёт РЕАЛЬНЫМИ КАДРАМИ (<c>yield return null</c> → настоящий <c>Update</c>), а не
        /// <see cref="GameDriver.DebugTick"/>: ветка плашки живёт ТОЛЬКО в <c>Update</c>, синхронный тик её
        /// не касается вовсе — потому соседний тест <see cref="FirstBurnout_IsAScreen_RepeatIsAShortPlate"/>
        /// баг и не ловил (он мерил плашку там, где её никто не включал).
        /// </summary>
        [UnityTest]
        public IEnumerator FirstBurnout_ShowsTheScreenAlone_NoShortPlateUnderIt_RealUpdatePath()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldDeck(27));
            fake.Yes();
            int guard = 0;
            while (guard++ < 900 && !driver.Game.EnergyOpen && driver.Game.State == GameState.Playing)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.1f);
            }
            NewScaleTut.ClearAny(driver, fake);
            Assert.IsTrue(driver.Game.EnergyOpen, "энергия открыта — выгорание физически возможно");
            yield return null;
            Assert.IsFalse(driver.BurnoutPlateGroup.activeSelf, "до выгорания плашки нет");

            // ПЕРВОЕ выгорание защёлкивается в ЖИВОМ КАДРЕ — ровно так, как оно случается у игрока.
            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            yield return null;                       // ← настоящий Update: Tick → латч → экран → ветка плашки
            Assert.IsTrue(driver.Game.Burnout, "выгорание защёлкнулось живым кадром");
            Assert.IsTrue(driver.SpecialModeShowing, "первый раз — входной экран");
            Assert.AreEqual(SpecialMode.Burnout, driver.SpecialModeKind, "именно выгорания");
            Assert.IsTrue(driver.Game.Paused, "…и он держит паузу (умереть, читая правила, нельзя)");
            Assert.IsFalse(driver.BurnoutPlateGroup.activeSelf,
                "…а короткой плашки под затемнением НЕТ — иначе одно и то же сказано дважды");
            for (int i = 0; i < 3; i++)
            {
                yield return null;                   // …и она не всплывает следующими кадрами
                Assert.IsFalse(driver.BurnoutPlateGroup.activeSelf,
                    "плашка не всплывает и на последующих кадрах под экраном");
            }

            // Экран прочитан и снят — вот теперь плашка и есть подача «выгорание всё ещё горит».
            fake.Yes();
            driver.DebugClearFrameGuards();
            yield return null;
            Assert.IsFalse(driver.SpecialModeShowing, "экран снят зелёной");
            Assert.IsTrue(driver.Game.Burnout, "…выгорание продолжается");
            Assert.IsTrue(driver.BurnoutPlateGroup.activeSelf, "…и его несёт короткая плашка");

            // ПОВТОРНОЕ выгорание — только плашка, и это тоже проверяется через Update.
            guard = 0;
            while (guard++ < 400 && driver.Game.Burnout)
            {
                fake.Fire(GameInput.EnergyHold);
                driver.DebugTick(0.05f);
            }
            Assert.IsFalse(driver.Game.Burnout, "вышли из выгорания");
            yield return null;
            Assert.IsFalse(driver.BurnoutPlateGroup.activeSelf, "вне выгорания плашки нет");

            driver.DebugTick(Game.BurnoutGraceSeconds + 0.5f);   // переждать грейс (встык нельзя)
            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            yield return null;                                   // живой кадр → повторный латч
            Assert.IsTrue(driver.Game.Burnout, "выгорание ПОВТОРНОЕ");
            Assert.IsFalse(driver.SpecialModeShowing, "второй раз экрана НЕТ");
            Assert.IsFalse(driver.Game.Paused, "…и паузы нет");
            Assert.IsTrue(driver.BurnoutPlateGroup.activeSelf, "…только короткая плашка");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 3. ПАНЧ ПЛАШЕК — только на ПРИНЯТЫЙ ответ (п.4)
        // =====================================================================================

        /// <summary>
        /// п.4. Панч (просадка масштаба плашки) — обратная связь «ответ засчитан». Он играл на СЫРОЕ
        /// нажатие рычага и потому бодро дёргался там, где игра ввод ГЛУШИТ: в депрессии, под входными
        /// экранами и подсказками, и на BLOCK$-блокировке, где карточка просто пропускается.
        ///
        /// Гард по СОСТОЯНИЯМ: в каждом «глухом» состоянии оба рычага не имеют права тронуть масштаб
        /// плашек, а в обычной игре — обязаны.
        /// </summary>
        [UnityTest]
        public IEnumerator PlatePunch_OnlyOnAnAcceptedAnswer_NotInAnySwallowedState()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(HoldDeck(30));
            fake.Yes();
            int guard = 0;
            while (guard++ < 400 && driver.Game.Age < 29f)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.1f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;

            var yes = (RectTransform)driver.YesPlateImage.transform;
            var no = (RectTransform)driver.NoPlateImage.transform;

            // (a) ОБЫЧНАЯ ИГРА: ответ принят — панч ЕСТЬ (корутина уже на первом кадре роняет масштаб).
            Assert.AreEqual(1f, yes.localScale.x, 1e-3f, "исходно плашка в норме");
            fake.Yes();
            yield return null;
            Assert.Less(yes.localScale.x, 1f, "принятый ответ → плашка панчит");
            yield return new WaitForSeconds(0.25f);        // дать корутине доиграть
            Assert.AreEqual(1f, yes.localScale.x, 1e-2f, "…и возвращается");

            // (b) ПОД ВХОДНЫМ ЭКРАНОМ спецрежима: зелёная закрывает экран, но плашку НЕ панчит.
            driver.DebugShowSpecialMode(SpecialMode.Health);
            yield return null;
            fake.Yes();
            yield return null;
            Assert.AreEqual(1f, yes.localScale.x, 1e-3f, "закрытие экрана — не ответ, панча нет");
            Assert.AreEqual(1f, no.localScale.x, 1e-3f, "…и красная тоже спокойна");
            driver.DebugClearFrameGuards();
            yield return null;

            // (c) ПОД S5-ПОДСКАЗКОЙ: ввод глушится целиком.
            driver.DebugShowTutorial("тест");
            yield return null;
            fake.No();
            yield return null;
            Assert.AreEqual(1f, no.localScale.x, 1e-3f, "под подсказкой панча нет");
            driver.DebugDismissTutorial();
            driver.DebugClearFrameGuards();
            yield return null;

            // (d) ПОД §D-МОДАЛКОЙ ШКАЛЫ: ответы там инертны по контракту §D.
            driver.DebugShowNewScale(NewScale.Money);
            yield return null;
            fake.Yes(); fake.No();
            yield return null;
            Assert.AreEqual(1f, yes.localScale.x, 1e-3f, "под §D-модалкой панча нет (ДА)");
            Assert.AreEqual(1f, no.localScale.x, 1e-3f, "…и (НЕТ)");
            driver.DebugCloseNewScale();
            yield return null;

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Та же семантика в ДЕПРЕССИИ и в БЛИЦЕ: там рычаги ПЕРЕНАЗНАЧЕНЫ (ловля пульса / кнопки блица),
        /// и «ответом на карточку» они не являются — значит и плашки дёргаться не должны.
        /// </summary>
        [UnityTest]
        public IEnumerator PlatePunch_IsSilent_InDepressionAndInTheBlitz()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame();
            driver.DebugReplaceGame(g);
            fake.Yes();
            g.HandleInput(GameInput.AnswerNo);

            int guard = 0;
            while (guard++ < 4000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                NewScaleTut.ClearAny(driver, fake);
                driver.DebugTick(0.2f);
            }
            NewScaleTut.ClearSpecial(driver, fake);
            yield return null;

            var yes = (RectTransform)driver.YesPlateImage.transform;
            var no = (RectTransform)driver.NoPlateImage.transform;

            // БЛИЦ: ДА/НЕТ — это кнопки блица, не ответ на карточку.
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "в блице");
            fake.Yes();
            yield return null;
            Assert.AreEqual(1f, yes.localScale.x, 1e-3f, "в блице плашка не панчит — это не ответ");
            fake.No();
            yield return null;
            Assert.AreEqual(1f, no.localScale.x, 1e-3f, "…и красная тоже");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 4. BLOCK$-баннер и чип цены — ПОВЕРХ плашек ответа (п.6)
        // =====================================================================================

        /// <summary>
        /// п.6. Баннер «нет денег» и чип цены жили ДЕТЬМИ карточки, а плашки ответа — СОСЕДЯМИ ПОЗЖЕ неё,
        /// поэтому кнопки рисовались ПОВЕРХ и срезали ровно то, что игрок должен прочитать.
        /// Sibling-ассерт: контейнер обеих фигур стоит в списке ПОЗЖЕ обеих плашек (в uGUI это и есть «выше»).
        /// </summary>
        [UnityTest]
        public IEnumerator BlockBannerAndPriceChip_DrawAboveTheAnswerPlates()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            var overlay = driver.BlockOverlay.transform;
            var yes = driver.YesPlateImage.transform;
            var no = driver.NoPlateImage.transform;

            Assert.AreSame(yes.parent, overlay.parent, "баннер живёт в том же контейнере, что и плашки…");
            Assert.AreSame(no.parent, overlay.parent, "…и красная тоже");
            Assert.Greater(overlay.GetSiblingIndex(), yes.GetSiblingIndex(),
                "контейнер баннера/цены — ПОЗЖЕ зелёной плашки, т.е. рисуется ПОВЕРХ неё");
            Assert.Greater(overlay.GetSiblingIndex(), no.GetSiblingIndex(),
                "…и поверх красной");

            // И сами фигуры — внутри этого контейнера (а не остались детьми карточки).
            Assert.AreSame(overlay, driver.BlockBanner.transform.parent, "баннер — в контейнере оверлея");
            Assert.AreSame(overlay, driver.BlockBannerInk.transform.parent, "…его кант тоже");
            Assert.AreSame(overlay, driver.CardPricePlate.transform.parent, "…чип цены тоже");
            Assert.AreSame(overlay, driver.CardPriceText.transform.parent, "…и его текст");
            Assert.AreNotSame(driver.CardRect, driver.BlockBanner.transform.parent,
                "…и НИ ОДНА из них больше не ребёнок карточки (это и был баг z-порядка)");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// …и геометрически: там, где баннер РЕАЛЬНО перекрывается с плашкой, поверх оказывается баннер.
        /// Проверяем на позе блокировки — той самой, где игрок читает «нет денег».
        /// </summary>
        [UnityTest]
        public IEnumerator OnABlockedCard_TheBannerOverlapsAPlate_AndWinsTheZOrder()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            driver.DebugPreviewBlocked();
            yield return null;

            var canvas = driver.CanvasRect;
            var banner = RefBox(canvas, (RectTransform)driver.BlockBanner.transform);
            var noPlate = RefBox(canvas, (RectTransform)driver.NoPlateImage.transform);

            Assert.IsTrue(driver.BlockBanner.activeInHierarchy, "баннер на экране");
            bool overlaps = banner.x < noPlate.z && noPlate.x < banner.z
                            && banner.y < noPlate.w && noPlate.y < banner.w;
            if (overlaps)
                Assert.Greater(driver.BlockOverlay.transform.GetSiblingIndex(),
                    driver.NoPlateImage.transform.GetSiblingIndex(),
                    "перекрытие есть — и наверху обязан быть баннер, а не кнопка");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 5. ОТКЛИК НА ПРОПУЩЕННЫЙ ЗВОНОК (п.9)
        // =====================================================================================

        /// <summary>
        /// п.9. Успешный звонок уже салютовал звёздами, а ПРОПУСК не отзывался ничем — игрок не понимал,
        /// что вообще что-то потерял. Теперь пропуск читается двумя средствами того же языка:
        /// трубка уезжает ПОНИКШЕЙ (спрайт покоя + тинт ЗАМЕТНО темнее обычного покоя) и Ведущий это
        /// озвучивает. Контраст с успехом проверяется прямо здесь: подняли — тинт обычный, проспали — темнее.
        /// </summary>
        [UnityTest]
        public IEnumerator MissedCall_LeavesTheHandsetDimmed_AndTheHostSaysSo()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(CallDeck());
            fake.Yes();                                    // opener → playing (I03)
            fake.No();                                     // I03 → MD02
            fake.Yes();                                    // MD02=ДА → механика ребёнка
            NewScaleTut.ClearAny(driver, fake);
            yield return null;

            // (1) УСПЕХ: звонок поднят — трубка уезжает обычным покоем, без «поникшести».
            TickUntilRinging(driver, fake);
            fake.Fire(GameInput.ChildPress);
            Assert.IsFalse(driver.Game.ChildFlashing, "трубку подняли");
            driver.DebugTick(0.5f);
            yield return null;
            Assert.IsFalse(driver.ChildPhoneMissed, "успех — трубка НЕ поникшая");

            // (2) ПРОПУСК: следующий звонок просыпаем целиком. Карточку отвечаем ЗАРАНЕЕ — иначе её
            // таймаут внутри окна перебил бы облачко своей репликой, и тест мерил бы не то.
            TickUntilRinging(driver, fake);
            fake.No();
            driver.DebugClearFrameGuards();
            driver.DebugTick(Game.ChildFlashWindow + 0.3f);
            Assert.IsFalse(driver.Game.ChildFlashing, "окно закрылось неподнятым");
            yield return null;                             // Update: облачко Ведущего и поза трубки

            Assert.IsTrue(driver.ChildPhoneMissed, "трубка помечена ПОНИКШЕЙ");
            Assert.AreEqual(GameDriver.ChildMissedLine, driver.HostBubbleText.text,
                "…и Ведущий это озвучил (канон-черновик host-content §4b)");
            Assert.IsTrue(driver.HostBubbleVisible, "реплика реально на экране");

            // Поза: спрайт ПОКОЯ (без дуг) и тинт ЗАМЕТНО темнее обычного покоя — это и есть «поникшая».
            driver.DebugTick(GameDriver.PhoneSlideSeconds);   // трубка доехала за край в поникшей позе
            yield return null;
            Assert.AreEqual("phone-rest-v2", driver.ChildPhoneImage.sprite.name, "спрайт покоя, без дуг");
            float lit = driver.ChildPhoneImage.color.g;
            float restG = GameDriver.PhoneRestTint.g;
            Assert.Less(lit, restG * 0.95f,
                $"поникшая трубка ({lit:0.00}) ТЕМНЕЕ обычного покоя ({restG:0.00}) — пропуск виден глазом");
            Assert.Less(GameDriver.PhoneMissedTint.g, GameDriver.PhoneRestTint.g,
                "…и это заложено в самих токенах, а не в случайной фазе уезда");

            // Следующий звонок стирает «поникшесть» — она про КОНКРЕТНЫЙ пропуск, а не про жизнь вообще.
            TickUntilRinging(driver, fake);
            yield return null;
            Assert.IsFalse(driver.ChildPhoneMissed, "новый звонок снимает поникшесть");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>Дотикать до ОТКРЫТОГО окна звонка, снимая по дороге любые окна открытий шкал.</summary>
        private static void TickUntilRinging(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (guard++ < 900 && !driver.Game.ChildFlashing && driver.Game.State == GameState.Playing)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing || driver.SpecialModeShowing)
                    NewScaleTut.ClearAny(driver, fake);
                else driver.DebugTick(0.1f);
            }
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок пошёл");
        }

        // =====================================================================================
        // 6. КОМПОЗИЦИЯ ВХОДНЫХ ЭКРАНОВ — находки ДИЗАЙН-СКЕПТИКА (раунд 2, 2026-08-08)
        // =====================================================================================

        /// <summary>Бокс из (центр x, центр y от ВЕРХА, w, h) в формат L, T, R, B.</summary>
        private static Vector4 Box(Vector4 cxcywh)
            => new(cxcywh.x - cxcywh.z / 2f, cxcywh.y - cxcywh.w / 2f,
                   cxcywh.x + cxcywh.z / 2f, cxcywh.y + cxcywh.w / 2f);

        private static bool Overlaps(Vector4 a, Vector4 b)
            => a.x < b.z && b.x < a.z && a.y < b.w && b.y < a.w;

        /// <summary>Зазор между боксами: &gt;0 — разведены (по той оси, где разведены), ≤0 — пересекаются.</summary>
        private static float Gap(Vector4 a, Vector4 b)
            => Mathf.Max(Mathf.Max(b.x - a.z, a.x - b.z), Mathf.Max(b.y - a.w, a.y - b.w));

        /// <summary><paramref name="inner"/> целиком внутри <paramref name="outer"/>.</summary>
        private static bool Contains(Vector4 outer, Vector4 inner)
            => outer.x <= inner.x && outer.y <= inner.y && outer.z >= inner.z && outer.w >= inner.w;

        /// <summary>Нарисованная фигура облачка внутри его ректа (рект заметно больше рисунка).</summary>
        private static Vector4 BubbleArt(Vector4 rect)
        {
            var i = GameDriver.StoryBubbleArtInset01;
            float w = rect.z - rect.x, h = rect.w - rect.y;
            return new Vector4(rect.x + w * i.x, rect.y + h * i.y, rect.z - w * i.z, rect.w - h * i.w);
        }

        /// <summary>
        /// ⚠ ПОЧЕМУ ЗДЕСЬ МЕРЯЮТСЯ ЦЕНТРЫ, А НЕ БОКСЫ. Канвас в headless-прогоне НЕ 1920×1080 (CanvasScaler
        /// тянет его под вид батча), и ПОЛОЖЕНИЕ виджета — это доля канваса (переводится в реф-пиксели
        /// точно), а РАЗМЕР — sizeDelta в единицах канваса (в реф-пиксели переводится с масштабом вида).
        /// Поэтому живьём проверяется ЦЕНТР (канвас-независимо) + факт «объект на экране», а размеры и
        /// зазоры считаются по объявленным боксам — они все в одной реф-системе.
        /// </summary>
        private static Vector2 RefCentre(RectTransform canvas, RectTransform rt)
        {
            var b = RefBox(canvas, rt);
            return new Vector2((b.x + b.z) / 2f, (b.y + b.w) / 2f);
        }

        /// <summary>Где сейчас лежит точка дизайн-бокса виджета — в дизайн-пикселях (тот же приём, что в
        /// <see cref="NewScaleBigWidgetPlacementTests"/>: настоящие матрицы Unity, а не формула раскладки).</summary>
        private static Vector2 DesignPositionOf(GameDriver driver, RectTransform widget, Vector2 designPoint)
        {
            var canvas = driver.CanvasRect;
            float cw = canvas.rect.width, ch = canvas.rect.height;
            var local = new Vector3((designPoint.x / 1920f - 0.5f) * cw,
                                    (0.5f - designPoint.y / 1080f) * ch, 0f);
            var back = canvas.InverseTransformPoint(widget.TransformPoint(local));
            return new Vector2(back.x / cw * 1920f + 960f, 540f - back.y / ch * 1080f);
        }

        private static IEnumerator Pose(GameDriver driver, System.Action pose)
        {
            pose();
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        /// <summary>
        /// п.5 скептика. КРУПНЫЙ БАР ЗДОРОВЬЯ СТОИТ У СВОЕГО HUD-МЕСТА. Стоял в слоте ОТНОШЕНИЙ (x 229…990):
        /// срастался кромкой с батареей и оставлял торчащий розовый обрезок плашки отношений — то самое
        /// «наполовину накрытый сосед», за которое канон r2-tut-rel и требует накрывать ЦЕЛИКОМ.
        ///
        /// Правый верх при этом занят облачком-рассказом, поэтому экран здоровья (и только он) делает
        /// РОКИРОВКУ: облачко зеркалится влево и накрывает левую пару виджетов целиком, а бар садится в свой
        /// слот. Проверяется и то, и другое — вместе они и означают «ни одного обрезка на экране».
        /// </summary>
        [UnityTest]
        public IEnumerator HealthScreen_BigBarStandsOnItsOwnHudSlot_AndTheBubbleSwapsSides()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            yield return Pose(driver, () => driver.DebugPreviewSpecialMode(SpecialMode.Health));

            var canvas = driver.CanvasRect;
            var slot = Box(new Vector4(GameDriver.HealthBarDrawnCx, GameDriver.HealthBarDrawnCy,
                                       GameDriver.HealthBarDrawnW, GameDriver.HealthBarDrawnH));
            var dst = GameDriver.BigSpecialDst(SpecialMode.Health);
            var dstBox = Box(dst);
            const float Need = GameDriver.BigScaleFrameMargin;   // 16 px — то же поле, что у §D-модалок

            // (1) целевой бокс — У СВОЕГО HUD-места, крупнее HUD-размера и целиком в кадре.
            Assert.IsTrue(Overlaps(dstBox, slot), "крупный бар стоит У СВОЕГО HUD-места (слот здоровья)");
            Assert.Greater(dst.x, 960f, "…то есть в ПРАВОЙ половине кадра, а не в слоте отношений");
            Assert.Greater(dst.z, GameDriver.HealthBarDrawnW, "…и он крупнее своего HUD-размера");
            Assert.GreaterOrEqual(dstBox.x, Need, "…целиком в кадре слева");
            Assert.LessOrEqual(dstBox.z, 1920f - Need, "…и справа");
            Assert.GreaterOrEqual(dstBox.y, Need, "…и сверху");

            // (2) виджет РЕАЛЬНО туда приехал (матрицы Unity, а не формула раскладки).
            Assert.AreSame(driver.HealthGroup, driver.NewScaleBigWidget, "крупный виджет — сам бар здоровья");
            var src = GameDriver.BigSpecialSrc(SpecialMode.Health);
            var got = DesignPositionOf(driver, (RectTransform)driver.NewScaleBigWidget.transform,
                                       new Vector2(src.x, src.y));
            Assert.AreEqual(dst.x, got.x, 1.5f, "центр крупного бара по X");
            Assert.AreEqual(dst.y, got.y, 1.5f, "…и по Y");

            // (3) …и разведён с соседями правой колонки: НАРИСОВАННОЙ банкой и бейджем возраста.
            Assert.GreaterOrEqual(Gap(dstBox, Box(GameDriver.BigScaleSrc(NewScale.Money))), Need,
                "зазор до банки ≥16 px");
            Assert.GreaterOrEqual(Gap(dstBox, Box(GameDriver.AgeBadgeDrawnBox)), Need,
                "…и до бейджа возраста");

            // (4) РОКИРОВКА: облачко ушло ВЛЕВО (живой замер центра) и накрывает левую пару ЦЕЛИКОМ.
            var bubbleRt = driver.NewScaleStoryBubble.rectTransform;
            Assert.Less(RefCentre(canvas, bubbleRt).x, 960f, "на экране здоровья облачко зеркалится ВЛЕВО");
            Assert.Greater(bubbleRt.localScale.x, 0f, "…в РОДНОЙ ориентации спрайта (рупор к левому краю)");
            Assert.AreEqual(GameDriver.StoryBubbleLeftRect.x, RefCentre(canvas, bubbleRt).x, 2f,
                "…и ровно на объявленном месте");
            var art = BubbleArt(Box(GameDriver.StoryBubbleLeftRect));
            Assert.IsTrue(Contains(art, Box(new Vector4(GameDriver.RelBarDrawnCx, GameDriver.RelBarDrawnCy,
                                                        GameDriver.RelBarDrawnW, GameDriver.RelBarDrawnH))),
                "…и накрывает плашку отношений ЦЕЛИКОМ (иначе её верх торчит розовым обрезком)");
            Assert.IsTrue(Contains(art, Box(GameDriver.BigScaleSrc(NewScale.Energy))),
                "…и батарею тоже — целиком, а не по кромку");
            Assert.GreaterOrEqual(Gap(art, dstBox), Need, "…и само облачко разведено с крупным баром ≥16 px");

            // (5) на ОСТАЛЬНЫХ экранах облачко остаётся каноническим — справа.
            driver.enabled = true;
            driver.DebugCloseSpecialMode();          // …иначе следующий экран просто встанет в очередь
            yield return Pose(driver, () => driver.DebugPreviewSpecialMode(SpecialMode.Burnout));
            Assert.Greater(RefCentre(canvas, driver.NewScaleStoryBubble.rectTransform).x, 960f,
                "у выгорания облачко на своей канонической стороне");
            Assert.Less(driver.NewScaleStoryBubble.rectTransform.localScale.x, 0f, "…и отражено, как на эталоне");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// п.6 скептика. КОРОТКАЯ ПЛАШКА ВЫГОРАНИЯ ВИСИТ ПОД БАТАРЕЕЙ — тем виджетом, про который она и
        /// говорит. Стояла под баром ЗДОРОВЬЯ (x 1294) и подходила низом к обводке карточки на 2 px.
        ///
        /// Соседи считаются по НАРИСОВАННЫМ боксам: у карточки и у трубки прозрачные поля спрайтов
        /// достигают 25…60 px, и «зазор по ректу» врал бы в обе стороны.
        /// </summary>
        [UnityTest]
        public IEnumerator BurnoutPlate_HangsUnderTheBattery_ClearOfTheCardBarsAndPhone()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            yield return Pose(driver, () => driver.DebugPreviewBurnout());

            var canvas = driver.CanvasRect;
            var edgeRt = (RectTransform)driver.BurnoutPlateGroup.transform.Find("BurnoutPlateEdge");
            Assert.IsTrue(driver.BurnoutPlateGroup.activeSelf, "плашка на экране");
            Assert.AreEqual(GameDriver.BurnoutPlateRect.x, RefCentre(canvas, edgeRt).x, 2f,
                "плашка стоит на объявленном месте по X");
            Assert.AreEqual(GameDriver.BurnoutPlateRect.y, RefCentre(canvas, edgeRt).y, 2f, "…и по Y");

            // Внешний контур = плашка + кант (BlockKeylineInk с каждой стороны).
            var edge = Box(new Vector4(GameDriver.BurnoutPlateRect.x, GameDriver.BurnoutPlateRect.y,
                                       GameDriver.BurnoutPlateRect.z + 2f * GameDriver.BlockKeylineInk,
                                       GameDriver.BurnoutPlateRect.w + 2f * GameDriver.BlockKeylineInk));
            var battery = Box(GameDriver.BigScaleSrc(NewScale.Energy));
            const float Need = 12f;

            Assert.Greater(edge.y, battery.w, "плашка висит ПОД батареей…");
            Assert.GreaterOrEqual(edge.y - battery.w, Need, "…с зазором ≥12 px");
            Assert.Less(GameDriver.BurnoutPlateRect.x, 640f,
                "…и в левой трети кадра — у той самой батареи, про которую говорит");

            Assert.GreaterOrEqual(Gap(edge, Box(GameDriver.CardPlateDrawnBox)), Need,
                "…не подходит к карточке ближе 12 px (было 2)");
            Assert.GreaterOrEqual(Gap(edge, Box(GameDriver.PhoneRestDrawnBox)), Need,
                "…и не наезжает на трубку в покое");
            Assert.GreaterOrEqual(Gap(edge, Box(new Vector4(GameDriver.HealthBarDrawnCx, GameDriver.HealthBarDrawnCy,
                                                            GameDriver.HealthBarDrawnW, GameDriver.HealthBarDrawnH))), Need,
                "…и на бар здоровья");
            Assert.GreaterOrEqual(Gap(edge, Box(new Vector4(GameDriver.RelBarDrawnCx, GameDriver.RelBarDrawnCy,
                                                            GameDriver.RelBarDrawnW, GameDriver.RelBarDrawnH))), Need,
                "…и на плашку отношений");
            Assert.GreaterOrEqual(edge.x, Need, "…и не липнет к левому краю кадра");

            // ⚠ ВЫЕХАВШАЯ трубка (звонок) по геометрии с плашкой НЕ разводится — свободного коридора под
            // батареей всего 55 px. Это осознанный размен, и он закрыт ВРЕМЕНЕМ: плашка гаснет, пока трубка
            // на экране (гард живой — ChildPhoneTests.Burnout_FirstTimePausesTheCall_…).
            Assert.Less(Gap(edge, Box(GameDriver.PhoneRingDrawnBox)), Need,
                "напоминание: развод с ЗВОНЯЩЕЙ трубкой держится не пикселями, а гашением плашки");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// п.7 скептика. СТРОКА ЦЕНЫ РИСУЕТСЯ РОВНО ОДИН РАЗ. Под чипом проступала вторая, серая и
        /// размытая копия «цена 100 ₽»: uGUI применяет Outline и Shadow ПОСЛЕДОВАТЕЛЬНО к одному мешу,
        /// поэтому тень дублировала уже раздутую обводкой копию — на кегле 40 это читалось второй подписью,
        /// торчащей из-под чипа. Тёмному чипу со светлым текстом эффект и не нужен.
        /// </summary>
        [UnityTest]
        public IEnumerator PriceChip_DrawsItsLineExactlyOnce_NoGhostUnderIt()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            yield return Pose(driver, () => driver.DebugPreviewBlocked());

            Assert.IsTrue(driver.CardPriceText.gameObject.activeSelf, "чип цены на экране");
            var fx = driver.CardPriceText.GetComponents<BaseMeshEffect>();
            Assert.AreEqual(0, fx.Length,
                "строка цены рисуется ОДНИМ мешем: ни Outline, ни Shadow — они и давали призрак-дубль"
                + (fx.Length > 0 ? " (нашлось: " + fx[0].GetType().Name + ")" : ""));

            // …и она по-прежнему целиком лежит на своём тёмном чипе (иначе «светлое по жёлтому»).
            var canvas = driver.CanvasRect;
            Assert.IsTrue(Contains(RefBox(canvas, driver.CardPricePlate.rectTransform),
                                   RefBox(canvas, driver.CardPriceText.rectTransform)),
                "подпись цены целиком внутри своего чипа");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// п.8 скептика. ЗЕЛЁНАЯ CTA ДЫШИТ. Её кант упирался в нижнюю кромку кремового поля (0…3 px), а
        /// над ней при этом зияла дыра 110–135 px — особенно на здоровье и блице, где служебной строки
        /// клавиши нет вовсе. Гард: и зазор до кромки, и то, что вертикаль РОЗДАНА, а не собрана в дыру.
        /// </summary>
        [UnityTest]
        public IEnumerator SpecialCta_BreathesInsideTheCreamField_OnEveryMode(
            [ValueSource(nameof(AllModes))] SpecialMode mode)
        {
            var driver = Boot(out var go, out _);
            yield return null;
            yield return Pose(driver, () => driver.DebugPreviewSpecialMode(mode));

            var canvas = driver.CanvasRect;
            var edge = Box(GameDriver.SpecialCtaEdgeRect);
            var field = Box(GameDriver.TaskFieldRect);
            var task = Box(GameDriver.SpecialTaskTextRect);
            var hint = Box(GameDriver.SpecialHintLineRect);
            const float Need = 16f;

            // Живьём: CTA поднята и стоит там, где объявлено (центр — канвас-независимый замер).
            Assert.IsTrue(driver.SpecialModeCtaEdge.activeInHierarchy, mode + ": кант CTA на экране");
            var ctaCentre = RefCentre(canvas, (RectTransform)driver.SpecialModeCtaEdge.transform);
            Assert.AreEqual(GameDriver.SpecialCtaEdgeRect.x, ctaCentre.x, 2f, mode + ": CTA на месте по X");
            Assert.AreEqual(GameDriver.SpecialCtaEdgeRect.y, ctaCentre.y, 2f, mode + ": …и по Y");
            // …и раскладка окна-задачи — та самая, «спецрежимная» (текст поднят под CTA).
            Assert.AreEqual(GameDriver.SpecialTaskTextRect.y,
                RefCentre(canvas, driver.NewScaleTaskText.rectTransform).y, 2f,
                mode + ": текст задачи ужат под CTA (LayoutTaskWindow(special))");

            Assert.GreaterOrEqual(field.w - edge.w, Need,
                mode + ": между кантом CTA и нижней кромкой кремового поля ≥16 px");
            Assert.GreaterOrEqual(edge.x - field.x, Need, mode + ": …и слева поле не задето");
            Assert.GreaterOrEqual(field.z - edge.z, Need, mode + ": …и справа");

            Assert.GreaterOrEqual(edge.y - task.w, 12f, mode + ": текст задачи не упирается в CTA");
            Assert.LessOrEqual(edge.y - task.w, 80f,
                mode + ": …но и дыры в 100+ px между текстом и CTA больше нет — вертикаль роздана");
            Assert.GreaterOrEqual(hint.y - task.w, 8f, mode + ": служебная строка стоит ПОД текстом задачи");
            Assert.GreaterOrEqual(edge.y - hint.w, 12f, mode + ": …и НАД кантом CTA, а не под ним");

            Object.Destroy(go);
            yield return null;
        }

        // ---- вспомогательные колоды --------------------------------------------------------

        private static Game CallDeck()
        {
            var deck = new List<Card> { Starter(), Plain("MD02", 21) };
            for (int i = 0; i < 80; i++)
            {
                var c = Plain("F" + i, 22);
                c.Order = 22 + i;
                deck.Add(c);
            }
            return new Game(deck, coin: () => false) { ChildFlashInterval = () => 2f };
        }

        private static readonly string[] CrisisIds =
            { "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08" };

        private static Game CrisisGame()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            var byId = CardLoader.ParseAll(asset.text).ToDictionary(c => c.Id);
            var fillers = new List<Card>();
            for (int i = 0; i < Game.DepressionGapCards + 8; i++)
                fillers.Add(new Card
                {
                    Id = "FILL" + i, Question = "FILL?", When = "60", Age = 60, Order = 999 + i,
                    YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                    NoNecrolog = "жил дальше", Flags = new List<string>(),
                });
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
    }
}
