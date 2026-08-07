using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// §D «Экран появления новой шкалы» (meeting-revisions §2 / build-spec §D, эталон
    /// `explainers/Экран - появление новой шкалы.png`) через РЕАЛЬНЫЙ драйвер:
    ///
    /// • модалка встаёт на каждом OPEN (деньги 18 / отношения 20 / энергия 25 / ребёнок «свадьба+2»)
    ///   с канон-текстами host-content §4 и КРУПНОЙ живой копией открываемой шкалы;
    /// • под ней СТОИТ время (возраст, дренажи, стоимость жизни, таймер карточки), но НЕ стоят контролы
    ///   шкал — иначе условие выхода недостижимо;
    /// • окно не закрывается ни ответами, ни таймаутом — только выполнением условия по реальному контролу;
    /// • выполнил → фейд 0.2 с → салют звёзд → пауза снята;
    /// • композиция трёх окон и крупной шкалы сверена с эталоном (±10 px).
    /// </summary>
    public class NewScaleTutorialTests
    {
        private const float RefTol = 10f;   // done-contract §6: боксы окон ±10 px против эталона

        // ---- замеры ЭТАЛОНА (PIL, 1920×1080) ---------------------------------------------------------
        // кремовое поле окна-задачи, кремовое поле облачка, обводка КРУПНОЙ батареи.
        private static readonly Vector4 RefTaskField = new(393f, 343f, 1625f, 819f);   // L, T, R, B
        private static readonly Vector4 RefStoryField = new(1014f, 107f, 1747f, 343f);
        // ⚠ КАЙМА ≠ alpha-tight поле. RefTaskField — прямоугольный bbox кремового пятна, но нарисованная
        // кайма плашки идёт ПО ЗВЁЗДАМ, и на строках задачи кремовое поле ýже: замер по кадрам r2
        // (1920×1080, самая узкая строка текстовой полосы) даёт x 449…1576. Против НЕГО и меряется
        // «дыхание» — против bbox зазор считался бы на ~50 px оптимистичнее, чем видит глаз.
        private static readonly Vector4 RefTaskFrameInner = new(449f, 343f, 1576f, 819f);
        /// <summary>Минимальный зазор глифов задачи до каймы (дизайн-скептик 2026-08-07: было 7 px слева
        /// на энергии — текст читался «втиснутым»). Цель раскладки ~30+ px, гард держит нижнюю границу.</summary>
        private const float TaskBreathMin = 20f;
        // ⚠ У батареи на эталоне ДВА разных бокса, и их нельзя путать (на этом и разъехалась раскладка):
        //   • ВНУТРЕННЯЯ КРОМКА ОБВОДКИ — x 207…412, y 145…546. Это то, что меряется с эталона «по рамке»;
        //     против него считает NewScaleBigWidgetLayoutTests (GameDriver.BatteryInnerStrokeRef).
        //   • ВЕСЬ РИСУНОК (alpha-tight bbox: клемма сверху + внешняя чёрная обводка) — x 180…435, y 81…575.
        // Здесь мы строим бокс из BatteryArtInSprite, то есть ВЕСЬ РИСУНОК, — значит и сверять надо со
        // вторым. Раньше тут стоял первый, и тест «подтверждал» раскладку, промахивавшуюся на ~104 px.
        private static readonly Vector4 RefBigBattery = new(180f, 81f, 435f, 575f);

        // alpha-tight bbox внутри текстур (см. HudConformanceTests): task-plate-v2 — кремовое ПОЛЕ,
        // host-comment-v2 — кремовое ПОЛЕ, energy-battery-v2 — вся картинка.
        private static readonly Vector4 TaskCreamInSprite = new(204f, 220f, 1239f, 482f);  // x, y, w, h
        private static readonly Vector2 TaskSpriteSize = new(1536f, 892f);
        private static readonly Vector4 StoryCreamInSprite = new(259f, 73f, 1120f, 362f);
        private static readonly Vector2 StorySpriteSize = new(1445f, 506f);
        private static readonly Vector4 BatteryArtInSprite = new(18f, 21f, 380f, 860f);
        private static readonly Vector2 BatterySpriteSize = new(420f, 910f);

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

        /// <summary>Колода: стартер + длинный хвост карточек РОВНО на возрасте открытия шкалы.</summary>
        private static Game AgeDeck(int openAge)
        {
            var deck = new List<Card> { Starter() };
            for (int i = 0; i < 80; i++)
            {
                var c = Plain("F" + i, openAge);
                c.Order = openAge + i;
                deck.Add(c);
            }
            return new Game(deck, coin: () => false);
        }

        /// <summary>Колода ребёнка: MD02 на 21, дальше держим возраст (как в ChildPhoneTests).</summary>
        private static Game ChildDeck()
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

        /// <summary>
        /// Колода «ПОЗДНИЙ ребёнок»: MD02 приходит на 26, то есть УЖЕ ПОСЛЕ открытия энергии (25). Только
        /// так и бывает выгорание в момент OPEN:Реб — а значит только на такой колоде проверяется развилка
        /// «выгорание × детская модалка» (обе половины: отложка старта и невозможность влететь в выгорание
        /// под уже открытой модалкой). Обычная <see cref="ChildDeck"/> открывает ребёнка на 21, когда шкалы
        /// энергии ещё нет и выгорание физически невозможно.
        /// </summary>
        /// <param name="timeoutSaysYes">Сторона, которую берёт ТАЙМАУТ (монетка). true — «не успел» на MD02
        /// открывает ребёнка, то есть §D-модалка приходит не с рычага, а из <see cref="Game.Tick"/>: ровно
        /// так она и может встретиться с выгоранием В ОДНОМ ТИКЕ.</param>
        private static Game LateChildDeck(bool timeoutSaysYes = false)
        {
            var deck = new List<Card> { Starter() };
            int order = 2;
            // Ровно три «разгонные» карты: возраст догоняет 26 за ~2 с (AgeCatchUpPerSecond = 12), значит
            // все три ранние шкалы успевают открыться и их экраны — сняться, а энергия ещё далеко не в нуле
            // (её дренаж с 25 — единственный способ УМЕРЕТЬ по дороге к MD02).
            for (int i = 0; i < 3; i++) { var c = Plain("F" + i, 26); c.Order = order++; deck.Add(c); }
            var md = Plain("MD02", 26); md.Order = order++; deck.Add(md);
            for (int i = 0; i < 80; i++) { var c = Plain("G" + i, 26); c.Order = order++; deck.Add(c); }
            return new Game(deck, coin: () => timeoutSaysYes) { ChildFlashInterval = () => 2f };
        }

        /// <summary>Довести «поздний» прогон до момента, когда MD02 — ТЕКУЩАЯ карта, энергия открыта, а все
        /// попутные модалки/подсказки сняты. Отвечать на MD02 (ДА) — уже дело теста.</summary>
        private static void DriveToLateMd02(GameDriver driver, PlayFakeInputSource fake,
                                            bool timeoutSaysYes = false)
        {
            driver.DebugReplaceGame(LateChildDeck(timeoutSaysYes));
            fake.Confirm();                       // опенер → игра (I03)
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 3000)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); continue; }
                if (driver.TutorialShowing) { fake.Confirm(); continue; }
                if (driver.Game.CurrentCard != null && driver.Game.CurrentCard.Id == "MD02") break;
                driver.DebugTick(0.1f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 1.0f) fake.No();
            }
            Assert.AreEqual("MD02", driver.Game.CurrentCard?.Id, "MD02 — текущая карта");
            Assert.IsTrue(driver.Game.EnergyOpen, "энергия уже открыта (26 > 25) — выгорание возможно");
            Assert.IsFalse(driver.Game.ChildOpen, "…а ребёнок ещё нет");
        }

        // Довести жизнь до модалки нужной шкалы: тикаем, попутно снимая ЧУЖИЕ модалки их же контролами.
        private static void DriveToModal(GameDriver driver, PlayFakeInputSource fake, NewScale target)
        {
            int guard = 0;
            while (driver.NewScaleKind != target && driver.Game.State == GameState.Playing && guard++ < 2000)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); continue; }
                if (driver.TutorialShowing) { fake.Confirm(); continue; }
                driver.DebugTick(0.1f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 1.0f) fake.No();
            }
            Assert.AreEqual(target, driver.NewScaleKind, "модалка нужной шкалы поднялась на её OPEN");
        }

        /// <summary>Довести УЖЕ ЗАПУЩЕННЫЙ драйвер (Start отработал, ввод подписан) до модалки шкалы.</summary>
        private static void SetupTo(GameDriver driver, PlayFakeInputSource fake, NewScale target)
        {
            driver.DebugReplaceGame(target switch
            {
                NewScale.Money => AgeDeck(18),
                NewScale.Relations => AgeDeck(20),
                NewScale.Energy => AgeDeck(25),
                _ => ChildDeck(),
            });
            fake.Confirm();                       // опенер → игра (I03)
            if (target == NewScale.Child)
            {
                fake.No();                        // I03 решена → MD02 текущая
                fake.Yes();                       // MD02=ДА → механика ребёнка открыта
            }
            DriveToModal(driver, fake, target);
        }

        // ---- геометрия в реф-px (та же конвенция, что в HudConformanceTests) --------------------------

        private static Vector2 ToReference(RectTransform canvas, Vector3 local)
            => new Vector2((local.x / canvas.rect.width + 0.5f) * 1920f,
                           (0.5f - local.y / canvas.rect.height) * 1080f);

        private static Vector2 RefCentre(RectTransform canvas, RectTransform rt)
            => ToReference(canvas, canvas.InverseTransformPoint(rt.position));

        /// <summary>Нарисованный кусок спрайта (alpha-tight/поле) в реф-px (L, T, R, B), с учётом того,
        /// что виджет может быть УВЕЛИЧЕН родителем (крупная копия шкалы едет на localScale группы).</summary>
        private static (float L, float T, float R, float B) DrawnBox(
            RectTransform canvas, RectTransform rt, Vector4 partInSprite, Vector2 spriteSize,
            bool mirrored = false)
        {
            // Модуль: у ЗЕРКАЛЬНОГО спрайта (облачко) localScale.x отрицателен — размер от этого не меняется,
            // а вот доля куска внутри ректа отражается (fx → 1-fx).
            float k = Mathf.Abs(rt.lossyScale.x / canvas.lossyScale.x);
            float rw = rt.rect.width * k, rh = rt.rect.height * k;
            float w = rw * partInSprite.z / spriteSize.x;
            float h = rh * partInSprite.w / spriteSize.y;
            float fx = (partInSprite.x + partInSprite.z / 2f) / spriteSize.x;
            if (mirrored) fx = 1f - fx;
            float fy = (partInSprite.y + partInSprite.w / 2f) / spriteSize.y;
            var c = RefCentre(canvas, rt);
            float cx = c.x + (fx - 0.5f) * rw;
            float cy = c.y + (fy - 0.5f) * rh;
            return (cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f);
        }

        /// <summary>
        /// Бокс, который текст РЕАЛЬНО закрашивает (сетка глифов с учётом best-fit), в реф-px — а не его
        /// рект. Именно глифы упираются в кайму, и именно их мерил дизайн-скептик.
        /// </summary>
        private static (float L, float T, float R, float B) GlyphBox(RectTransform canvas, Text t)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, "«" + t.name + "» рисует глифы (не пусто и не тофу)");

            float upp = 1f / t.pixelsPerUnit;
            float k = Mathf.Abs(t.rectTransform.lossyScale.x / canvas.lossyScale.x);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                float x = p.x * upp * k, y = p.y * upp * k;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            var c = RefCentre(canvas, t.rectTransform);
            // Вершины локальны ректу (пивот в центре); локальный +y — это реф-px ВВЕРХ, то есть −y.
            return (c.x + minX, c.y - maxY, c.x + maxX, c.y - minY);
        }

        private static (float L, float T, float R, float B) RefBox(RectTransform canvas, RectTransform rt)
        {
            var c = RefCentre(canvas, rt);
            float k = rt.lossyScale.x / canvas.lossyScale.x;
            float hw = rt.rect.width * k / 2f, hh = rt.rect.height * k / 2f;
            return (c.x - hw, c.y - hh, c.x + hw, c.y + hh);
        }

        private static void AssertBox(string what, (float L, float T, float R, float B) got, Vector4 want)
        {
            Assert.AreEqual(want.x, got.L, RefTol, what + ": левый край против эталона");
            Assert.AreEqual(want.y, got.T, RefTol, what + ": верхний край против эталона");
            Assert.AreEqual(want.z, got.R, RefTol, what + ": правый край против эталона");
            Assert.AreEqual(want.w, got.B, RefTol, what + ": нижний край против эталона");
        }

        // =====================================================================================
        // 1. Триггер + канон-тексты + крупная шкала (done-contract §2)
        // =====================================================================================

        [UnityTest]
        public IEnumerator Modal_RaisesOnEveryOpen_WithCanonTextsAndTheBigLiveWidget()
        {
            foreach (var kind in new[] { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child })
            {
                var driver = Boot(out var go, out var fake);
                yield return null;                               // Start подписал ввод
                SetupTo(driver, fake, kind);

                Assert.IsTrue(driver.NewScaleShowing, kind + ": модалка поднята");
                Assert.IsTrue(driver.NewScaleOverlay.activeSelf, kind + ": оверлей виден");
                Assert.IsFalse(driver.TutorialShowing, kind + ": СТАРАЯ S5-подсказка на этом OPEN снята");
                Assert.AreEqual(GameDriver.NewScaleStory(kind), driver.NewScaleStoryText.text,
                    kind + ": рассказ Ведущего — канон host-content §4, дословно");
                Assert.AreEqual(GameDriver.NewScaleTask(kind), driver.NewScaleTaskText.text,
                    kind + ": задача — канон host-content §4, дословно");
                Assert.IsTrue(driver.Game.Paused, kind + ": игра на паузе под модалкой");

                // КРУПНАЯ копия — это НАСТОЯЩИЙ виджет своей шкалы, увеличенный и поднятый над затемнением.
                var big = driver.NewScaleBigWidget;
                Assert.IsNotNull(big, kind + ": крупная шкала на экране");
                Assert.IsTrue(big.activeInHierarchy, kind + ": крупная шкала видима");
                // Порог у трубки ниже осознанно. Она и в HUD уже самый крупный виджет (411×444) и вдобавок
                // ЧАСТЬЮ ЗА КАДРОМ (её HUD-рект начинается на x=−62). На модалке она обязана быть ЦЕЛИКОМ в
                // кадре и не накрывать текст задачи, а текстовое поле начинается на x=452 — значит ширина
                // ≤436 px, то есть максимум ×1.06 от HUD. Требовать ×1.2 здесь — требовать либо вылет за
                // край, либо перекрытый текст. Точные боксы всех четырёх проверяет
                // NewScaleBigWidgetLayoutTests; здесь — что копия увеличена и ровно на объявленный k.
                float k = ((RectTransform)big.transform).localScale.x;
                Assert.Greater(k, kind == NewScale.Child ? 1.0f : 1.2f,
                    kind + ": шкала показана КРУПНО (увеличенная копия, не HUD-размер)");
                Assert.AreEqual(GameDriver.BigScaleFactor(kind), k, 1e-3f,
                    kind + ": масштаб ровно тот, что объявлен боксами раскладки");
                Assert.AreSame(driver.NewScaleOverlay.transform,
                    big.transform.parent.parent, kind + ": крупная шкала живёт НАД затемнением модалки");

                Object.Destroy(go);
                yield return null;
            }
        }

        // =====================================================================================
        // 2. Пауза: время стоит, ввод — нет (done-contract §4)
        // =====================================================================================

        [UnityTest]
        public IEnumerator UnderModal_Age_Drains_CostOfLiving_AndCardTimer_AllFrozen()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Energy);

            var card = driver.Game.CurrentCard;
            float age0 = driver.Game.Age, timer0 = driver.Game.CardTimer;
            double money0 = driver.Game.Money;
            int energy0 = driver.Game.Scales.Energy, health0 = driver.Game.Scales.Health;
            int rel0 = driver.Game.Scales.Relationships;

            for (int i = 0; i < 40; i++) driver.Game.Tick(0.25f);   // 10 с «в модалке»

            Assert.IsTrue(driver.NewScaleShowing, "модалка всё ещё стоит (таймаут её не закрывает)");
            Assert.AreEqual(age0, driver.Game.Age, 1e-3f, "возраст заморожен");
            Assert.AreEqual(timer0, driver.Game.CardTimer, 1e-3f, "таймер ответа заморожен");
            Assert.AreEqual(money0, driver.Game.Money, 1e-6, "стоимость жизни не списывается");
            Assert.AreEqual(energy0, driver.Game.Scales.Energy, "энергия не тикает под модалкой");
            Assert.AreEqual(health0, driver.Game.Scales.Health, "здоровье не тикает под модалкой");
            Assert.AreEqual(rel0, driver.Game.Scales.Relationships, "отношения не дрейфуют под модалкой");
            Assert.AreSame(card, driver.Game.CurrentCard, "карточка та же — таймаут не сработал");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Answers_And_Confirm_DoNotClose_TheModal()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Money);

            var card = driver.Game.CurrentCard;
            for (int i = 0; i < 5; i++)
            {
                fake.Yes();                      // зелёный рычаг — НЕ «понятно»
                fake.No();                       // красный рычаг
                fake.Confirm();                  // dev-CONFIRM
                driver.DebugAdvanceNewScale(0.5f);
            }

            Assert.IsTrue(driver.NewScaleShowing, "окно не закрывается кнопками-ответами (revisions §2)");
            Assert.IsTrue(driver.Game.Paused, "пауза держится");
            Assert.AreSame(card, driver.Game.CurrentCard, "и карточка не отвечена под модалкой");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 3. Условия по РЕАЛЬНЫМ контролам (done-contract §3)
        // =====================================================================================

        [UnityTest]
        public IEnumerator Money_ClosesOnlyOnAnAcceptedCrankTick_ThenStarsAndUnpause()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Money);

            int stars0 = driver.StarBurstCount;
            double money0 = driver.Game.Money;

            driver.DebugAdvanceNewScale(3f);
            Assert.IsTrue(driver.NewScaleShowing, "без крутилки окно стоит сколько угодно");

            // ⚠ 2026-08-07: условие — НЕ первый тик, а N (основательница: «крутить надо дольше, слишком
            // быстро пропадает»). Сначала N−1 тиков: деньги идут, монетки капают, а окно СТОИТ.
            for (int i = 0; i < GameDriver.MoneyTutorialTicks - 1; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);              // ПРИНЯТЫЙ тик — через кэп и Game.Crank
                driver.DebugAdvanceNewScale(0.05f);
            }
            Assert.Greater(driver.Game.Money, money0, "крутилка живая под модалкой: деньги пришли");
            Assert.IsTrue(driver.NewScaleArmed, "ввод по шкале был");
            Assert.IsFalse(driver.NewScaleSatisfied,
                $"{GameDriver.MoneyTutorialTicks - 1} тиков — окно ЕЩЁ стоит (условие {GameDriver.MoneyTutorialTicks})");
            Assert.IsTrue(driver.NewScaleShowing, "…и оно на экране");

            driver.DebugAdvanceInputClocks(1f);
            fake.Fire(GameInput.MoneyTick);                  // N-й тик — вот теперь условие
            driver.DebugAdvanceNewScale(0.05f);
            Assert.IsTrue(driver.NewScaleSatisfied, "условие засчитано → пошёл фейд");
            Assert.IsTrue(driver.NewScaleShowing, "во время фейда окно ещё на экране");
            Assert.IsTrue(driver.Game.Paused, "и пауза ещё держится");

            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "после фейда окно ушло");
            Assert.IsFalse(driver.NewScaleOverlay.activeSelf, "оверлей скрыт");
            Assert.AreEqual(stars0 + 1, driver.StarBurstCount, "салют звёзд на выходе (build-spec §D)");
            Assert.IsFalse(driver.Game.Paused, "пауза снята — игра продолжается");
            Assert.IsTrue(driver.MoneyJar.activeSelf, "банка осталась в HUD");
            Assert.AreEqual(1f, ((RectTransform)driver.MoneyJar.transform).localScale.x, 1e-3f,
                "…и вернулась в ОБЫЧНЫЙ размер (build-spec §D: «шкала в HUD обычного размера»)");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// РЕДИЗАЙН 2026-08-07 (основательница дословно: «просто зажать датчик высоты, пока батарейка не
        /// заполнится»). Условие экрана энергии — ПОЛНАЯ батарея, и наполнить её можно только УДЕРЖАНИЕМ:
        /// отпустил — рост встал (под модалкой время заморожено, поэтому шкала просто СТОИТ), зажал — растёт
        /// на глазах. Ритм-гейта, «не в ритм» и полутора секунд «в рабочей зоне» больше нет.
        /// </summary>
        [UnityTest]
        public IEnumerator Energy_ClosesOnlyWhenTheBatteryIsFull_AndOnlyHoldingFillsIt()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Energy);

            // Шкала открывается ПРОСЕВШЕЙ — иначе наполнять было бы нечего.
            Assert.AreEqual(Game.EnergyOpenValue, driver.Game.Scales.Energy,
                "энергия открывается на «первой усталости», а не полной");

            // Датчик НЕ поднят: время под модалкой заморожено, поэтому батарея не двигается вообще.
            int e0 = driver.Game.Scales.Energy;
            for (int i = 0; i < 40; i++) { driver.Game.Tick(0.25f); driver.DebugAdvanceNewScale(0.25f); }
            Assert.AreEqual(e0, driver.Game.Scales.Energy, "без поднятого датчика батарея не растёт");
            Assert.IsFalse(driver.NewScaleArmed, "…и условие не взводится");
            Assert.IsTrue(driver.NewScaleShowing, "…а окно стоит сколько угодно");

            // Зажали датчик: батарея наполняется НА ГЛАЗАХ, но пока не полна — окно держится.
            fake.Fire(GameInput.EnergyHold);
            driver.Game.Tick(0.5f);
            driver.DebugAdvanceNewScale(0.5f);
            Assert.Greater(driver.Game.Scales.Energy, e0, "поднятый датчик наполняет батарею под паузой");
            Assert.IsTrue(driver.NewScaleArmed, "…и это РЕАЛЬНЫЙ ввод по шкале");
            Assert.Less(driver.Game.Scales.Energy, GameDriver.NewScaleEnergyFull, "батарея ещё не полна…");
            Assert.IsFalse(driver.NewScaleSatisfied, "…значит условие не выполнено");

            // Отпустили на несколько тактов: рост встал ровно там, где отпустили.
            int paused = driver.Game.Scales.Energy;
            for (int i = 0; i < 8; i++) { driver.Game.Tick(0.25f); driver.DebugAdvanceNewScale(0.25f); }
            Assert.AreEqual(paused, driver.Game.Scales.Energy, "отпустил → рост прекратился");
            Assert.IsTrue(driver.NewScaleShowing, "…и окно не ушло");

            // Держим до конца — батарея заполняется, и ТОЛЬКО тогда экран уходит с салютом.
            int stars0 = driver.StarBurstCount;
            int guard = 0;
            while (driver.Game.Scales.Energy < GameDriver.NewScaleEnergyFull && guard++ < 60)
            {
                fake.Fire(GameInput.EnergyHold);
                driver.Game.Tick(0.25f);
                driver.DebugAdvanceNewScale(0.25f);
            }
            Assert.GreaterOrEqual(driver.Game.Scales.Energy, GameDriver.NewScaleEnergyFull, "батарея полна");
            driver.DebugAdvanceNewScale(0.05f);
            Assert.IsTrue(driver.NewScaleSatisfied, "полная батарея → условие выполнено");
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "окно ушло");
            Assert.AreEqual(stars0 + 1, driver.StarBurstCount, "салют");
            Assert.IsFalse(driver.Game.Paused, "пауза снята");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ПОЛОС ПРОГРЕССА БОЛЬШЕ НЕТ — гард отсутствия (основательница 2026-08-07: «убрать полосу
        /// прогресса в туториалах»). Проверяется не флагом драйвера, а самой ИЕРАРХИЕЙ оверлея: ни на одном
        /// из четырёх экранов внутри модалки не должно быть ни дорожки, ни заполнения — как бы далеко игрок
        /// ни продвинулся по условию.
        /// </summary>
        [UnityTest]
        public IEnumerator NoProgressBar_OnAnyModal_TheWidgetItselfIsTheProgress()
        {
            foreach (var kind in new[] { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child })
            {
                var driver = Boot(out var go, out var fake);
                yield return null;
                SetupTo(driver, fake, kind);

                AssertNoBar(driver, kind, "сразу на открытии");

                // …и после реального ввода по шкале (раньше полоска появлялась ровно тут — по «взводу»).
                switch (kind)
                {
                    case NewScale.Money:
                        driver.DebugAdvanceInputClocks(1f); fake.Fire(GameInput.MoneyTick); break;
                    case NewScale.Energy:
                        fake.Fire(GameInput.EnergyHold); driver.Game.Tick(0.5f); break;
                    case NewScale.Relations:
                        fake.Fire(GameInput.RelationUp); driver.Game.Tick(0.1f); break;
                    case NewScale.Child:
                        break;   // у ребёнка «взвод» = выполненное условие, окно уже уходит
                }
                driver.DebugAdvanceNewScale(0.5f);
                AssertNoBar(driver, kind, "после реального ввода по шкале");

                Object.Destroy(go);
                yield return null;
            }
        }

        // Обходим СОБСТВЕННУЮ обвязку модалки, но НЕ одолженный HUD-виджет (BigScaleSlot): там законно
        // живут «RelBar»/«HealthBar» самой шкалы — это её рисунок, а не полоса прогресса туториала.
        private static void AssertNoBar(GameDriver driver, NewScale kind, string when)
        {
            Walk(driver.NewScaleOverlay.transform, kind, when);
        }

        private static void Walk(Transform node, NewScale kind, string when)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                var c = node.GetChild(i);
                if (c.name == "BigScaleSlot") continue;             // одолженный виджет — не наша обвязка
                string n = c.name;
                Assert.IsFalse(n.Contains("Hold") || n.Contains("Progress") || n.Contains("Bar"),
                    $"{kind} ({when}): в модалке остался элемент прогресса «{n}» — полосы сняты 2026-08-07");
                Walk(c, kind, when);
            }
        }

        [UnityTest]
        public IEnumerator Relations_TheLeverMovesTheMarkerUnderThePause_AndTheZoneMustBeHeld()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Relations);

            Assert.IsTrue(driver.Game.RelationshipsOpen, "балансир открыт");

            // Рычаг ВНИЗ реально ведёт маркер под паузой (TickModalBalancer) — уводим его ИЗ зоны.
            // Модалку при этом НЕ тикаем: старт 55 % уже внутри 40–75, и полтора секунды удержания
            // истекли бы раньше, чем маркер успел уехать (тогда тест мерил бы не то).
            int guard = 0;
            while (driver.Game.Scales.Relationships >= Game.RelZoneMin && guard++ < 400)
            {
                fake.Fire(GameInput.RelationDown);
                driver.Game.Tick(0.25f);
            }
            Assert.Less(driver.Game.Scales.Relationships, Game.RelZoneMin,
                "рычаг живой под паузой: маркер уехал ниже зоны");
            Assert.IsTrue(driver.NewScaleArmed, "ввод по рычагу был");
            Assert.IsTrue(driver.NewScaleShowing, "окно держится");

            // Вне зоны удержание не копится и условие не выполняется, сколько окно ни держи.
            driver.DebugAdvanceNewScale(GameDriver.NewScaleHoldSeconds * 2f);
            Assert.IsFalse(driver.NewScaleSatisfied, "вне зоны условие не выполняется");
            Assert.AreEqual(0f, driver.NewScaleHoldFraction, 1e-3f, "и удержание обнулено");
            Assert.IsTrue(driver.NewScaleShowing, "окно всё ещё на экране");

            // …и обратно ВВЕРХ, в механическую зону 40–75.
            guard = 0;
            while (driver.Game.Scales.Relationships < Game.RelZoneMin && guard++ < 400)
            {
                fake.Fire(GameInput.RelationUp);
                driver.Game.Tick(0.25f);
            }
            Assert.GreaterOrEqual(driver.Game.Scales.Relationships, Game.RelZoneMin, "маркер вернулся в зону");
            Assert.LessOrEqual(driver.Game.Scales.Relationships, Game.RelZoneMax, "и не задушен вверху");

            int stars0 = driver.StarBurstCount;
            driver.DebugAdvanceNewScale(GameDriver.NewScaleHoldSeconds + 0.05f);
            Assert.IsTrue(driver.NewScaleSatisfied, "удержал 1.5 с в зоне → условие выполнено");
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "окно ушло");
            Assert.AreEqual(stars0 + 1, driver.StarBurstCount, "салют");
            Assert.IsFalse(driver.Game.Paused, "пауза снята");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ДОЛГ ГЕЙТА 2026-08-05: «культя» купола. Под §D-модалкой время заморожено, и замерший полукруг
        /// таймера торчал над затемнением обрубком — читался как недорисованный элемент. Купол обязан быть
        /// СКРЫТ, пока модалка поднята, и вернуться сам, как только она ушла. Проверяем на всех четырёх
        /// шкалах и по ЖИВОМУ пути (SetupTo доводит до модалки её собственным OPEN).
        /// </summary>
        [UnityTest]
        public IEnumerator TimerDome_IsHiddenWhileTheModalIsUp_AndBackAfter(
            [Values(NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child)] NewScale which)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод

            Assert.IsTrue(driver.TimerDome.activeInHierarchy || driver.Game.State != GameState.Playing,
                "до игры купол живёт вместе с игровой панелью");

            SetupTo(driver, fake, which);
            Assert.IsTrue(driver.NewScaleShowing, which + ": модалка поднята");
            Assert.IsFalse(driver.TimerDome.activeInHierarchy,
                which + ": под модалкой купола на экране НЕТ (иначе торчит замерший обрубок)");

            NewScaleTut.Clear(driver, fake);                     // выполнить условие — модалка уходит штатно
            Assert.IsFalse(driver.NewScaleShowing, which + ": модалка ушла");
            Assert.IsTrue(driver.TimerDome.activeInHierarchy,
                which + ": купол вернулся сам, как только модалка снялась");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Child_RingsUnderTheModal_AndOnlyPickingUpCloses()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Child);

            Assert.IsTrue(driver.Game.ChildOpen, "механика ребёнка открыта");
            Assert.IsTrue(driver.Game.ChildFlashing, "туториал ЗАВЁЛ звонок — есть что поднимать");

            // Окно звонка под модалкой не истекает: телефон звонит, пока не поднимут.
            for (int i = 0; i < 40; i++) driver.Game.Tick(0.5f);
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок не «просрочился» под замороженным временем");
            Assert.IsTrue(driver.NewScaleShowing, "и окно не ушло само");

            int stars0 = driver.StarBurstCount;
            fake.Fire(GameInput.ChildPress);                    // кнопка «!»
            Assert.IsFalse(driver.Game.ChildFlashing, "трубку подняли — звонок закрыт");
            Assert.IsTrue(driver.NewScaleArmed, "условие ребёнка выполнено поднятым звонком");

            driver.DebugAdvanceNewScale(0.05f);                       // условие засчитано → пошёл фейд
            Assert.IsTrue(driver.NewScaleSatisfied, "условие выполнено");
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "окно ушло");
            Assert.AreEqual(stars0 + 1, driver.StarBurstCount, "салют звёзд");
            Assert.IsFalse(driver.Game.Paused, "пауза снята");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 3b. Под модалкой жив РОВНО ОДИН контрол — её собственный (done-contract §4: «шкалы не умирают
        //     под туториалом» читается в обе стороны — они и не НАКРУЧИВАЮТСЯ под ним)
        // =====================================================================================

        /// <summary>
        /// ЭКОНОМИКА ПОД МОДАЛКОЙ. Под §D-экраном время стоит: возраст, стоимость жизни и все дренажи
        /// заморожены. Если под ним живы ВСЕ контролы шкал, а не только контрол объясняемой шкалы, то любой
        /// из четырёх экранов превращается в бесконечную крутилку: игрок получает, скажем, экран энергии и
        /// накручивает сколько угодно денег в мире, где за них ничего не списывается. Экономика игры
        /// проходит мимо, и никакой таймер это не ограничивает — окно ждёт условия сколько угодно.
        ///
        /// Поэтому инвариант: под модалкой шкалы X до <see cref="Game"/> доходит ТОЛЬКО контрол X; чужие
        /// scale-вводы инертны ровно как ДА/НЕТ. Проверяем на трёх экранах крест-накрест и тут же — что
        /// СВОЙ контрол при этом жив (иначе «фикс» мог бы просто убить весь ввод).
        /// </summary>
        [UnityTest]
        public IEnumerator UnderAModal_ForeignScaleControls_AreInert_NoCrankFarming()
        {
            // ---- экран ЭНЕРГИИ (25): открыты и деньги, и отношения — обе чужие крутилки под рукой ----
            var driver = Boot(out var go, out var fake);
            yield return null;
            SetupTo(driver, fake, NewScale.Energy);

            Assert.IsTrue(driver.Game.MoneyOpen, "деньги открыты — крутилка физически есть");
            Assert.IsTrue(driver.Game.RelationshipsOpen, "и балансир тоже");

            double money0 = driver.Game.Money;
            int rel0 = driver.Game.Scales.Relationships;

            // 40 тиков крутилки, каждый — с ПОЛНОСТЬЮ перезаряженным кэпом дохода, то есть каждый был бы
            // ПРИНЯТ на своём экране. На чужом не проходит ни один.
            for (int i = 0; i < 40; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);
                fake.Fire(GameInput.MoneyTickRepeat);
            }
            Assert.AreEqual(money0, driver.Game.Money, 1e-6,
                "крутилка на ЧУЖОМ экране не приносит ни копейки (иначе — бесконечная ферма денег)");

            // …и рычаг балансира — тоже чужой: ось не латчится, TickModalBalancer нечего сводить.
            for (int i = 0; i < 40; i++) { fake.Fire(GameInput.RelationUp); driver.Game.Tick(0.25f); }
            Assert.AreEqual(rel0, driver.Game.Scales.Relationships,
                "чужой рычаг не двигает маркер под модалкой");

            Assert.IsFalse(driver.NewScaleArmed, "чужие вводы условие энергии не взводят");
            Assert.IsTrue(driver.NewScaleShowing, "…и окно, разумеется, не закрывают");

            // А СВОЙ контрол жив: поднятый датчик наполняет батарею и взводит условие.
            driver.Game.Scales.Energy = 30;
            fake.Fire(GameInput.EnergyHold);
            driver.Game.Tick(0.5f);
            Assert.Greater(driver.Game.Scales.Energy, 30, "СВОЙ контрол (датчик высоты) под модалкой живой");
            Assert.IsTrue(driver.NewScaleArmed, "…и он же взводит условие");

            Object.Destroy(go);
            yield return null;

            // ---- экран ОТНОШЕНИЙ (20): деньги уже открыты ----
            driver = Boot(out go, out fake);
            yield return null;
            SetupTo(driver, fake, NewScale.Relations);

            Assert.IsTrue(driver.Game.MoneyOpen, "деньги открыты (18 < 20)");
            money0 = driver.Game.Money;
            for (int i = 0; i < 40; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);
            }
            Assert.AreEqual(money0, driver.Game.Money, 1e-6, "на экране отношений крутилка мертва");
            Assert.IsFalse(driver.NewScaleArmed, "и условие отношений ею не взводится");

            Object.Destroy(go);
            yield return null;

            // ---- экран РЕБЁНКА: и крутилка, и рычаг под рукой ----
            // Через «поздний» MD02 (26), а не через обычную ChildDeck: та открывает ребёнка на 21, когда
            // возраст ещё не догнал даже 18 — деньги закрыты, и проверка «крутилка не крутит» была бы
            // ПУСТОЙ (Game.Crank и так молчит при закрытой шкале).
            driver = Boot(out go, out fake);
            yield return null;
            DriveToLateMd02(driver, fake);
            yield return null;                        // кадр: одноразовый swallow-гейт подсказки сброшен
            fake.Yes();                               // MD02=ДА → детский экран
            Assert.AreEqual(NewScale.Child, driver.NewScaleKind, "детский экран поднят");
            Assert.IsTrue(driver.Game.MoneyOpen, "деньги ОТКРЫТЫ — крутилка реально под рукой");
            Assert.IsTrue(driver.Game.RelationshipsOpen, "и балансир тоже");

            money0 = driver.Game.Money;
            rel0 = driver.Game.Scales.Relationships;
            for (int i = 0; i < 40; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);
                fake.Fire(GameInput.RelationUp);
                driver.Game.Tick(0.25f);
            }
            Assert.AreEqual(money0, driver.Game.Money, 1e-6, "на экране ребёнка крутилка мертва");
            Assert.AreEqual(rel0, driver.Game.Scales.Relationships, "…и рычаг тоже");
            Assert.IsFalse(driver.NewScaleArmed, "условие ребёнка чужими вводами не взводится");
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок всё это время ждёт СВОЕГО ввода");

            // СВОЙ контрол «!» — жив и закрывает экран.
            fake.Fire(GameInput.ChildPress);
            Assert.IsTrue(driver.NewScaleArmed, "«!» взвело условие");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 3c. ВЫГОРАНИЕ × ЭКРАН РЕБЁНКА: модалка не имеет права встать НЕВЫПОЛНИМОЙ
        // =====================================================================================

        /// <summary>
        /// ⚠ КАНОН ПЕРЕПИСАН r3 (2026-08-07). Развилка была такой: условие детской модалки — ПОДНЯТЬ
        /// ЗВОНОК, а под выгоранием звонок поднять нельзя (плашка S7 — полноэкранный захват, драйвер
        /// прятал трубку, Game морозил окно). Модалка встала бы НЕВЫПОЛНИМОЙ.
        ///
        /// Плашка-захват снята: повторное выгорание показывает короткую плашку, трубка видна, звонок идёт,
        /// и <see cref="Game.ChildCallFrozen"/> на Burnout больше не смотрит. Но развилка не исчезла, а
        /// СМЕНИЛА причину: ПЕРВОЕ выгорание поднимает ВХОДНОЙ ЭКРАН спецрежима, и вот ПОД НИМ детская
        /// модалка встать не может — два модальных окна на одном оверлее. Контракт тот же: открытие не
        /// теряется и не выполняется вслепую, а ОТКЛАДЫВАЕТСЯ; экран выгорания снимается зелёной — и
        /// детский экран встаёт сам, звонок звонит, «!» его закрывает и даёт салют.
        /// </summary>
        [UnityTest]
        public IEnumerator ChildModal_UnderTheBurnoutScreen_IsDeferred_NotRaisedUnwinnable()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            DriveToLateMd02(driver, fake);

            // Загоняем в ВЫГОРАНИЕ до ответа на MD02 — поднимается его входной экран (первый раз за жизнь).
            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            driver.DebugTick(0.05f);
            Assert.IsTrue(driver.Game.Burnout, "выгорание активно ДО открытия ребёнка");
            Assert.IsTrue(driver.SpecialModeShowing, "…и объяснено входным экраном");
            Assert.AreEqual(SpecialMode.Burnout, driver.SpecialModeKind, "именно выгорания");
            Assert.IsTrue(driver.Game.Paused, "под ним стоит всё — в том числе дренаж, который его вызвал");

            int stars0 = driver.StarBurstCount;
            // Ответ на MD02 под входным экраном ИНЕРТЕН — экран глушит всё, кроме зелёной. Значит сперва
            // снимаем экран… но проверим и то, что ДА его именно ЗАКРЫЛ, а не ответил на карточку.
            var cardBefore = driver.Game.CurrentCard;
            fake.Yes();
            driver.DebugClearFrameGuards();
            Assert.IsFalse(driver.SpecialModeShowing, "зелёная сняла экран выгорания");
            Assert.AreSame(cardBefore, driver.Game.CurrentCard, "…и НЕ ответила за игрока на карточку");
            yield return null;

            fake.Yes();                               // теперь ДА уходит в MD02 → механика ребёнка открыта
            Assert.IsTrue(driver.Game.ChildOpen, "механика ребёнка открыта");
            Assert.IsTrue(driver.NewScaleShowing, "…и её экран встал сразу — помех больше нет");
            Assert.AreEqual(NewScale.Child, driver.NewScaleKind, "именно детский");
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок заведён — есть что поднимать");
            Assert.IsTrue(driver.ChildGroup.activeSelf,
                "…и трубка ВИДНА, хотя выгорание ещё горит: короткая плашка её не накрывает");

            // …и он проходится своим контролом: «!» → салют → пауза снята.
            fake.Fire(GameInput.ChildPress);
            Assert.IsFalse(driver.Game.ChildFlashing, "трубку подняли");
            Assert.IsTrue(driver.NewScaleArmed, "условие выполнено");
            driver.DebugAdvanceNewScale(0.05f);
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "окно ушло");
            Assert.AreEqual(stars0 + 1, driver.StarBurstCount, "салют звёзд на выходе");
            Assert.IsFalse(driver.Game.Paused, "пауза снята — игра продолжается");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ОЧЕРЕДЬ ДВУХ ЭКРАНОВ — честно, без единого Debug-закрытия (переписан по ревью r3, MINOR:
        /// прежняя версия сама звала <c>DebugCloseNewScale</c> и принимала «showing ИЛИ pending», то есть
        /// не проверяла ни очередь, ни момент подъёма).
        ///
        /// ⚠ НАПРАВЛЕНИЕ ОЧЕРЕДИ ЗАДАНО МЕХАНИКОЙ, а не выбором теста. «Выгорание приходит ПОД открытой
        /// §D-модалкой» НЕВОЗМОЖНО: под модалкой время стоит, дренаж энергии заморожен, а единственный
        /// живой путь пересчёта (поднятый датчик, <c>TickModalBreath</c>) энергию только ПОДНИМАЕТ —
        /// это отдельно доказано соседним тестом
        /// <see cref="BurnoutCannotStart_UnderAnOpenChildModal_SoTheScreenStaysWinnable"/>. Возможна ровно
        /// ОБРАТНАЯ очередь, и она приходит из ОДНОГО тика: дренаж роняет энергию в выгорание (входной
        /// экран встаёт), и тем же тиком истекает таймер карточки — таймаут MD02 открывает механику
        /// ребёнка, а её §D-модалка поверх чужого экрана встать не имеет права и ОТКЛАДЫВАЕТСЯ.
        ///
        /// Проверяется главное (связка с находкой ревью MAJOR о порядке тика): отложенный экран встаёт
        /// ДО ПЕРВОГО ЖИВОГО ТИКА после того, как место освободилось. Свидетель живого тика — таймер
        /// карточки: под паузой он стоит, в живом ходе убывает каждым кадром. Если очередь разбирается
        /// после <c>_game.Tick</c>, между зелёной кнопкой и модалкой проходит кадр НАСТОЯЩЕЙ игры — с
        /// дренажом горящего выгорания и без единого объяснения на экране.
        /// </summary>
        [UnityTest]
        public IEnumerator TwoScreensQueueUp_TheDeferredOneRisesBeforeAnyLiveTick()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            DriveToLateMd02(driver, fake, timeoutSaysYes: true);   // таймаут на MD02 = ДА (открытие ребёнка)
            var g = driver.Game;
            Assert.IsFalse(driver.SpecialModeShowing, "перед развилкой экранов нет");
            Assert.IsFalse(driver.NewScaleShowing, "…ни одного");

            // Подводим таймер карточки к самому краю ОБЫЧНЫМ ходом времени (энергия при этом здоровая).
            int guard = 0;
            while (g.CardTimer > 0.2f && guard++ < 3000 && g.State == GameState.Playing)
                driver.DebugTick(0.1f);
            Assert.AreEqual("MD02", g.CurrentCard?.Id, "на краю таймера всё ещё MD02");
            Assert.IsFalse(g.Burnout, "…и выгорания ещё нет");

            // ОДИН тик делает обе вещи сразу: роняет энергию в выгорание (экран) и добивает таймер
            // карточки (таймаут MD02 = ДА → OPEN:Реб → §D-модалка, которой некуда встать).
            g.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            driver.DebugTick(0.25f);

            Assert.IsTrue(g.Burnout, "выгорание защёлкнулось этим тиком");
            Assert.IsTrue(driver.SpecialModeShowing, "…и объяснено входным экраном (первый раз за жизнь)");
            Assert.AreEqual(SpecialMode.Burnout, driver.SpecialModeKind, "именно выгорания");
            Assert.IsTrue(g.ChildOpen, "тем же тиком таймаут открыл механику ребёнка");
            Assert.IsFalse(driver.NewScaleShowing, "…но её §D-модалка поверх чужого экрана НЕ встала");
            Assert.AreEqual(NewScale.Child, driver.NewScalePending, "она честно стоит в очереди");
            Assert.IsTrue(g.Paused, "под входным экраном стоит всё");

            // Игрок читает правила выгорания и закрывает экран ЗЕЛЁНОЙ — его собственным контролом.
            fake.Yes();
            driver.DebugClearFrameGuards();
            Assert.IsFalse(driver.SpecialModeShowing, "экран выгорания снят зелёной");

            float timer0 = g.CardTimer;
            float age0 = g.Age;
            yield return null;                       // ← ОДИН настоящий кадр Update

            Assert.IsTrue(driver.NewScaleShowing, "отложенная модалка встала САМА, без Debug-закрытий");
            Assert.AreEqual(NewScale.Child, driver.NewScaleKind, "именно детская");
            Assert.AreEqual(NewScale.None, driver.NewScalePending, "…и очередь пуста");
            Assert.AreEqual(timer0, g.CardTimer, 1e-4f,
                "…и подняли её ДО живого тика: таймер карточки не сдвинулся ни на кадр "
                + "(иначе выгорание успело бы капнуть дренажом при пустом экране)");
            Assert.AreEqual(age0, g.Age, 1e-4f, "…и возраст тоже стоял");
            Assert.IsTrue(g.Paused, "модалка держит паузу");

            // …и она ПРОХОДИМА своим контролом: трубка звонит, «!» её поднимает, экран уходит сам.
            Assert.IsTrue(g.ChildFlashing, "звонок заведён — есть что поднимать");
            fake.Fire(GameInput.ChildPress);
            Assert.IsTrue(driver.NewScaleArmed, "условие выполнено");
            guard = 0;
            while (driver.NewScaleShowing && guard++ < 600) yield return null;
            Assert.IsFalse(driver.NewScaleShowing, "модалка ушла своим ходом");
            Assert.IsFalse(g.Paused, "…и пауза снята — игра продолжается");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Вторая половина той же развилки: выгорание НЕ МОЖЕТ начаться под уже открытой модалкой, поэтому
        /// «отложки» достаточно и обрабатывать выгорание в середине экрана не нужно. Доказательство, а не
        /// рассуждение: держим энергию РОВНО на пороге латча (≤10 %) и лупим по всем контролам сколько
        /// угодно — латч не срабатывает, потому что под паузой <see cref="Game.Tick"/> не доходит до
        /// дренажа энергии, а единственный оставшийся путь пересчёта (поднятый датчик) энергию только
        /// ПОДНИМАЕТ и на чужом экране вдобавок отфильтрован. Экран остаётся проходимым.
        /// </summary>
        [UnityTest]
        public IEnumerator BurnoutCannotStart_UnderAnOpenChildModal_SoTheScreenStaysWinnable()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            DriveToLateMd02(driver, fake);

            Assert.IsFalse(driver.Game.Burnout, "в момент открытия выгорания нет");
            yield return null;                        // кадр: одноразовый swallow-гейт подсказки сброшен
            fake.Yes();                               // MD02=ДА → детский экран встаёт сразу
            Assert.IsTrue(driver.NewScaleShowing, "детский экран поднят");
            Assert.AreEqual(NewScale.Child, driver.NewScaleKind, "…именно он");

            // Энергия — на самом дне зоны латча. В живой игре ближайший же тик защёлкнул бы выгорание.
            driver.Game.Scales.Energy = Game.BurnoutEnterEnergyAtOrBelow;
            for (int i = 0; i < 60; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);       // чужие контролы — все, какие есть
                fake.Fire(GameInput.EnergyHold);
                fake.Fire(GameInput.RelationUp);
                fake.Yes(); fake.No();                // …и ответы, которые под экраном инертны
                driver.DebugTick(0.1f);
            }

            Assert.IsFalse(driver.Game.Burnout,
                "выгорание НЕ начинается под открытой модалкой: дренаж энергии заморожен паузой");
            Assert.AreEqual(Game.BurnoutEnterEnergyAtOrBelow, driver.Game.Scales.Energy,
                "…и сама энергия под экраном ребёнка не двигается ни вниз (дренаж), ни вверх (чужой датчик)");
            Assert.IsTrue(driver.NewScaleShowing, "экран всё ещё стоит и всё ещё ждёт СВОЕГО ввода");
            Assert.IsTrue(driver.Game.ChildFlashing, "звонок жив");
            Assert.IsTrue(driver.ChildGroup.activeSelf, "трубка на экране — накрывать её нечем");

            fake.Fire(GameInput.ChildPress);
            driver.DebugAdvanceNewScale(0.05f);
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "экран проходится штатно");
            Assert.IsFalse(driver.Game.Paused, "пауза снята");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 4. Рестарт из модалки чист (done-contract §4)
        // =====================================================================================

        [UnityTest]
        public IEnumerator ExitFromTheModal_IsClean_NoPause_NoOverlay_WidgetBackInTheHud()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                   // Start подписал ввод
            SetupTo(driver, fake, NewScale.Energy);

            var battery = driver.EnergyGroup;
            var hudRow = driver.HudRow.transform;

            fake.Fire(GameInput.Exit);                          // кнопка МЕНЮ кабинета
            Assert.AreEqual(GameState.Opener, driver.Game.State, "чистый выход в опенер");
            Assert.IsFalse(driver.NewScaleShowing, "модалка не пережила выход");
            Assert.IsFalse(driver.NewScaleOverlay.activeSelf, "оверлей скрыт");
            Assert.IsFalse(driver.Game.Paused, "пауза снята");
            Assert.AreSame(hudRow, battery.transform.parent, "батарея вернулась в HUD-ряд");
            Assert.AreEqual(1f, ((RectTransform)battery.transform).localScale.x, 1e-3f,
                "…и в обычном размере");

            // Новая жизнь — модалки взводятся заново.
            fake.Confirm();
            Assert.AreEqual(GameState.Playing, driver.Game.State, "жизнь 2 началась");
            Assert.IsFalse(driver.NewScaleShowing, "на старте жизни модалки нет");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // 5. Композиция против ЭТАЛОНА (done-contract §6)
        // =====================================================================================

        [UnityTest]
        public IEnumerator Composition_ThreeWindows_MatchTheReference_WithinTenPixels()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugPreviewNewScale(NewScale.Energy);        // поза эталона: открытие ЭНЕРГИИ
            yield return null;

            var canvas = driver.CanvasRect;

            // Затемнение — на весь экран, токен INK, ~40 %.
            var dim = driver.NewScaleDim;
            Assert.IsTrue(dim.gameObject.activeInHierarchy, "затемнение есть");
            Assert.AreEqual(GameDriver.NewScaleDimAlpha, dim.color.a, 0.02f, "затемнение ~40 %");
            Assert.AreEqual(GameDriver.InkToken.r, dim.color.r, 0.01f, "затемнение цвета INK");
            Assert.AreEqual(GameDriver.InkToken.g, dim.color.g, 0.01f, "затемнение цвета INK");
            Assert.AreEqual(GameDriver.InkToken.b, dim.color.b, 0.01f, "затемнение цвета INK");
            Assert.AreEqual(Vector2.zero, dim.rectTransform.anchorMin, "затемнение растянуто на весь экран");
            Assert.AreEqual(Vector2.one, dim.rectTransform.anchorMax, "…по обоим углам");
            Assert.AreEqual(Vector2.zero, dim.rectTransform.offsetMin, "…без полей");
            Assert.AreEqual(Vector2.zero, dim.rectTransform.offsetMax, "…с обеих сторон");

            // Окно-задача: кремовое поле плашки на эталонном месте.
            AssertBox("окно-задача (кремовое поле)",
                DrawnBox(canvas, driver.NewScaleTaskPlate.rectTransform, TaskCreamInSprite, TaskSpriteSize),
                RefTaskField);
            Assert.AreEqual(Image.Type.Simple, driver.NewScaleTaskPlate.type,
                "`task-plate-v2` рисуется Simple — 9-slice ей нельзя (asset-map §5: звёзды-лучи по периметру)");

            // Окно-рассказ: кремовое поле облачка на эталонном месте (спрайт отражён — рупор справа).
            AssertBox("окно-рассказ (кремовое поле)",
                DrawnBox(canvas, driver.NewScaleStoryBubble.rectTransform, StoryCreamInSprite, StorySpriteSize,
                    mirrored: true),
                RefStoryField);
            Assert.Less(driver.NewScaleStoryBubble.rectTransform.localScale.x, 0f,
                "облачко ОТРАЖЕНО: на эталоне рупор смотрит вправо");

            // Крупная батарея — на эталонном месте и в эталонном размере.
            var bigBattery = driver.BatteryImage.rectTransform;
            AssertBox("крупная батарея",
                DrawnBox(canvas, bigBattery, BatteryArtInSprite, BatterySpriteSize), RefBigBattery);

            // Тексты сидят В кремовых полях (глифы в пилюлях), а не на рамке/звёздах.
            var taskText = RefBox(canvas, driver.NewScaleTaskText.rectTransform);
            Assert.GreaterOrEqual(taskText.L, RefTaskField.x, "текст задачи не вылезает влево из поля");
            Assert.LessOrEqual(taskText.R, RefTaskField.z, "…и вправо");
            Assert.GreaterOrEqual(taskText.T, RefTaskField.y, "…и вверх");
            Assert.LessOrEqual(taskText.B, RefTaskField.w, "…и вниз");

            // …и НЕ ВПРИТЫК к кайме: меряем НАРИСОВАННЫЕ глифы, а не рект (рект может быть с полями, а
            // best-fit всё равно раздувает строку до его краёв — так и вышло 7 px слева на энергии).
            var taskGlyphs = GlyphBox(canvas, driver.NewScaleTaskText);
            Assert.GreaterOrEqual(taskGlyphs.L - RefTaskFrameInner.x, TaskBreathMin,
                $"глифы задачи дышат слева (зазор {taskGlyphs.L - RefTaskFrameInner.x:0.#} px)");
            Assert.GreaterOrEqual(RefTaskFrameInner.z - taskGlyphs.R, TaskBreathMin,
                $"…и справа (зазор {RefTaskFrameInner.z - taskGlyphs.R:0.#} px)");

            var storyText = RefBox(canvas, driver.NewScaleStoryText.rectTransform);
            Assert.GreaterOrEqual(storyText.L, RefStoryField.x, "текст рассказа не вылезает влево из поля");
            Assert.LessOrEqual(storyText.R, RefStoryField.z, "…и вправо");
            Assert.GreaterOrEqual(storyText.T, RefStoryField.y, "…и вверх");
            Assert.LessOrEqual(storyText.B, RefStoryField.w, "…и вниз");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Тот же зазор — на ВСЕХ ЧЕТЫРЁХ модалках. Дыра, которую это закрывает: текст задачи один на все
        /// четыре экрана, кегль выбирает best-fit, и КОРОТКАЯ формулировка набирается КРУПНЕЕ длинной —
        /// значит «поправили энергию» ничего не гарантирует деньгам, отношениям и ребёнку. Проверяется
        /// НАРИСОВАННЫЙ текст против каймы, а не рект против bbox.
        /// </summary>
        [UnityTest]
        public IEnumerator TaskGlyphs_KeepBreathingRoom_FromTheFrame_OnEveryModal(
            [Values(NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child)] NewScale which)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugPreviewNewScale(which);
            yield return null;

            var canvas = driver.CanvasRect;
            Assert.AreEqual(GameDriver.NewScaleTask(which), driver.NewScaleTaskText.text,
                which + ": на экране канон-задача");

            var g = GlyphBox(canvas, driver.NewScaleTaskText);
            float padL = g.L - RefTaskFrameInner.x, padR = RefTaskFrameInner.z - g.R;
            Assert.GreaterOrEqual(padL, TaskBreathMin, $"{which}: зазор слева {padL:0.#} px — текст втиснут");
            Assert.GreaterOrEqual(padR, TaskBreathMin, $"{which}: зазор справа {padR:0.#} px — текст втиснут");
            Assert.GreaterOrEqual(g.T, RefTaskField.y, which + ": глифы не вылезают вверх из поля");
            Assert.LessOrEqual(g.B, RefTaskField.w, which + ": …и вниз");

            Object.Destroy(go);
            yield return null;
        }
    }
}
