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
    /// ГАРДЫ ПЛЕЙТЕСТ-ФИКСОВ r6 — ЭКРАННАЯ ПОЛОВИНА (живой плейтест основательницы 2026-09-22,
    /// «игра практически готова, но есть несколько правок»). Чистая половина живого BLOCK$ —
    /// в <c>EditMode/GameMoneyTests</c> и <c>EditMode/PlaytestFixesR6Tests</c>.
    ///
    /// Здесь закрываются:
    ///   • п.1 — текст опенера ДОСЛОВНО + вёрстка плашки под две строки;
    ///   • п.2 — живая недоступность НА ЭКРАНЕ в обе стороны + неактивная зелёная / нетронутая красная;
    ///   • п.3 — «РАССТАЛИСЬ» на слоте отношений + уступка слота балансиру на втором шансе (MD06).
    /// </summary>
    public class PlaytestFixesR6Tests
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

        private static Card Starter() { var c = Plain("I03", 1); c.StartsAgeTimer = true; return c; }

        private static string Squash(string s)
            => System.Text.RegularExpressions.Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();

        // ============================================================ п.1 — ТЕКСТ ОПЕНЕРА

        /// <summary>
        /// ТЕКСТ ОПЕНЕРА = ФОРМУЛИРОВКА ОСНОВАТЕЛЬНИЦЫ, БУКВА В БУКВУ (r6 п.1).
        ///
        /// Строка продиктована ею в чате живого плейтеста 2026-09-22 и является каноном: менять её
        /// нельзя ни на букву, поэтому эталон здесь набран ЗАНОВО литералом, а не взят из
        /// <see cref="GameDriver.OpenerRulesCanonFlat"/> — иначе гард сверял бы константу саму с собой
        /// и молча принял бы любую правку кода.
        ///
        /// Отдельно прибита СТАРАЯ РЕДАКЦИЯ: в ней жила опечатка «длинною» и четыре строки про
        /// «правильные решения», и именно их основательница просила убрать. Если они вернутся любым
        /// путём (откат, мердж, копипаста из build-spec) — тест покраснеет на этом ассерте, а не на
        /// сравнении целиком, и в отчёте будет сразу видно ЧТО вернулось.
        ///
        /// Mutation-proof: поменяй в <c>GameDriver.OpenerRulesText</c> любую букву, знак или падеж —
        /// первый ассерт красный; верни старый пятистрочный текст — красные и первый, и оба ниже.
        /// </summary>
        [UnityTest]
        public IEnumerator Opener_RulesText_IsFounderCanon_Verbatim()
        {
            const string Canon = "Увлекательное шоу длиною в жизнь. Делай выборы, которые определят твою судьбу.";

            var driver = Boot(out var go, out _);
            yield return null;                       // Awake + Start → опенер собран

            Assert.AreEqual(Canon, Squash(GameDriver.OpenerRulesText),
                "константа правил = формулировка основательницы дословно");
            Assert.AreEqual(Canon, Squash(GameDriver.OpenerRulesCanonFlat),
                "плоская копия канона не разъехалась с версткой");
            Assert.AreEqual(Canon, Squash(driver.OpenerRules.text),
                "…и на СОБРАННОМ экране стоит ровно она");

            StringAssert.DoesNotContain("длинною", driver.OpenerRules.text,
                "опечатка «длинною» снята основательницей — обратно не возвращается");
            StringAssert.DoesNotContain("правильные решения", driver.OpenerRules.text,
                "строки про «правильные решения» сняты — текст теперь два предложения");

            // Перенос — ровно ОДИН и по границе предложений: это вёрстка, а не другой текст.
            Assert.AreEqual(2, GameDriver.OpenerRulesText.Split('\n').Length,
                "две строки: строка = предложение");

            // CTA не трогалась этим инкрементом — она называет ФИЗИЧЕСКУЮ зелёную кнопку стойки.
            Assert.AreEqual("НАЧАТЬ ЖИЗНЬ — ЖМИ ЗЕЛЁНУЮ КНОПКУ", GameDriver.OpenerStartHintText,
                "CTA осталась прежней");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ВЁРСТКА ПЛАШКИ ПОДТЯНУТА ПОД КОРОТКИЙ ТЕКСТ (r6 п.1): «две строки не должны болтаться в
        /// поле, рассчитанном на пять».
        ///
        /// Меряется не константа кегля, а РЕАЛЬНО НАРИСОВАННЫЕ ГЛИФЫ: сколько высоты свободного поля
        /// кремовой плашки занял текстовый блок. Поле — это верх плашки до верха зелёной CTA, которая
        /// стоит ПОВЕРХ её низа (556…863 в референс-пикселях, ~307 высоты). Пять строк кеглем 46
        /// занимали почти всё поле; две строки тем же кеглем — около трети, и это ровно то «болтается
        /// в пустоте», на которое пожаловалась основательница. Порог 0.42 стоит МЕЖДУ двумя случаями:
        /// две строки кеглем 46 дают ~0.35, кеглем 64 — ~0.49.
        ///
        /// Mutation-proof: верни <c>OpenerRulesMaxFont</c> к 46 (старый пятистрочный потолок) — доля
        /// падает ниже порога, тест краснеет. Верхняя граница ловит обратную ошибку: если кегль
        /// задрать так, что текст упрётся в края поля, гард тоже покраснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator Opener_RulesPlate_KegelFillsTheField_NotFiveLineLeftovers()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            var t = driver.OpenerRules;
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, t.GetGenerationSettings(t.rectTransform.rect.size));
            Assert.Greater(tg.characterCountVisible, 0, "текст правил реально нарисован");

            float upp = 1f / t.pixelsPerUnit;
            float minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                float y = verts[i].position.y * upp;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float drawn = maxY - minY;

            // Свободное поле кремовой плашки: от её верха до верха зелёной CTA (та стоит поверх низа).
            const float PlateTop = 781f - 450f / 2f;      // OpenerPlateCy − OpenerCreamH/2 = 556
            // CTA поднята дизайн-гейтом r6 на OpenerGroupLift — поле считается от её ЖИВОГО верха.
            float CtaTop = GameDriver.OpenerCtaCy - 116f / 2f;   // CTA cy − её высота/2 = 831
            float Field = CtaTop - PlateTop;                     // ≈ 275

            float fill = drawn / Field;
            Assert.Greater(fill, 0.42f,
                "две строки ЗАНИМАЮТ поле, а не болтаются в нём (кегль подтянут под короткий текст)");
            Assert.Less(fill, 0.85f, "…и всё же не распирают поле до краёв — воздух остался");

            // И ровно две нарисованные строки: перенос не разъехался в три.
            Assert.AreEqual(2, tg.lineCount, "нарисовано ровно две строки");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ п.3 — «РАССТАЛИСЬ»

        /// <summary>
        /// «РАССТАЛИСЬ» СТОИТ НА СЛОТЕ ПЛАШКИ ОТНОШЕНИЙ (r6 п.3).
        ///
        /// ПОЧЕМУ ОНА УЕЗЖАЛА. До 2026-07-31 плашка висела на нормированном якоре (0.775, 0.70).
        /// Коммит c824ddd1 («increment(hud-row): full HUD rebuilt on the art pack») перевёл ВЕСЬ HUD
        /// на пиксельные ректы арт-пака, и старая точка стала попадать на новый бейдж возраста. Её
        /// отодвинули руками в 674, 215: x взяли от шкалы отношений, а y назначили на глаз — и это
        /// вынесло плашку на ~114 px НИЖЕ ректа шкалы, в случайно свободный зазор. Ни r3, ни r5 сюда
        /// не возвращались. Основательница увидела это на живом плейтесте: «плашка уехала вниз».
        ///
        /// Гард держит не число, а СВЯЗЬ: центр плашки обязан совпасть с центром реальной шкалы
        /// отношений, у которой она отнимает слот. Поэтому эталон читается с ЖИВОГО виджета балансира,
        /// а не из константы — назначить плашке любой другой y теперь нельзя, не сдвинув саму шкалу.
        ///
        /// Mutation-proof: верни <c>BreakupPlateRect</c> к «674, 215» (или к любому собственному y) —
        /// центры разъезжаются, тест краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator Breakup_Plate_SitsOnRelationshipSlot()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                          // → playing, HUD собран
            yield return null;

            var plate = (RectTransform)driver.BreakupPlate.transform;
            var relBar = (RectTransform)driver.BalancerGroup.transform.Find("RelBar");
            Assert.IsNotNull(relBar, "шкала отношений собрана — есть с чем сверять слот");

            // AnchorPx кладёт позу в anchorMin/anchorMax; сверяем центры в референс-пикселях.
            Assert.AreEqual(relBar.anchorMin.x * 1920f, plate.anchorMin.x * 1920f, 0.5f,
                "«РАССТАЛИСЬ» стоит по центру слота отношений (x)");
            Assert.AreEqual((1f - relBar.anchorMin.y) * 1080f, (1f - plate.anchorMin.y) * 1080f, 0.5f,
                "…и по центру слота отношений (y) — та самая «уехавшая вниз» координата");

            // Плашка САДИТСЯ на слот, а не растягивается в него: собственный размер сохранён.
            // ⚠ ГАБАРИТ ПЕРЕСМОТРЕН ДИЗАЙН-ГЕЙТОМ r6: было 360×96 — на треть уже слота, и правый край
            // резал жёлтый купол посреди фигуры. Стало 480×103 (поле 30 px с боков, ~20 сверху/снизу).
            Assert.AreEqual(480f, plate.sizeDelta.x, 0.5f, "собственная ширина плашки");
            Assert.AreEqual(103f, plate.sizeDelta.y, 0.5f, "собственная высота плашки");
            Assert.GreaterOrEqual(plate.sizeDelta.x, 460f, "плашка занимает слот, а не жмётся в нём");

            // …и она обязана помещаться в рект шкалы — иначе «на слоте» было бы лукавством.
            Assert.LessOrEqual(plate.sizeDelta.x, relBar.sizeDelta.x + 0.5f, "плашка не шире слота");
            Assert.LessOrEqual(plate.sizeDelta.y, relBar.sizeDelta.y + 0.5f, "плашка не выше слота");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ПЛАШКА УСТУПАЕТ СЛОТ БАЛАНСИРУ НА ВТОРОМ ШАНСЕ (r6 п.3, MD06).
        ///
        /// С тех пор как «РАССТАЛИСЬ» села на рект шкалы отношений, два виджета делят одну точку
        /// экрана. В обычной жизни они разведены сами собой (расставание гасит балансир), но ВТОРОЙ
        /// ШАНС возвращает шкалу тем же кадром, а собственные ~2 с плашки могут ещё не истечь — и без
        /// правила они нарисовались бы друг поверх друга.
        ///
        /// Гард проверяет ИНВАРИАНТ, а не момент: на всём отрезке «расстались → сыграли MD06 → шкала
        /// вернулась» не должно быть НИ ОДНОГО кадра, где активны оба. Плюс две проверки на
        /// невырожденность: плашка реально была видна до MD06, и балансир реально вернулся после —
        /// иначе тест мог бы «пройти», ничего не проверив.
        ///
        /// Mutation-proof: убери из <c>ReflectBreakupPlate</c> слагаемое <c>!_balancerGroup.activeSelf</c> —
        /// сразу после MD06 появляется кадр с обоими активными, тест краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator Breakup_Plate_YieldsSlot_ToBalancer_OnSecondChance()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // Колода: карточка роняет отношения в красную зону (расставание через ~10 с), затем
            // филлеры того же возраста, и MD06 — ВТОРОЙ ШАНС (OPEN:Отн), который вернёт шкалу.
            var hit = Plain("HIT", 21);
            hit.NoDeltas = new[] { new ScaleDelta(Scale.Relationships, DeltaKind.Set, 12) };
            var deck = new List<Card> { Starter(), hit };
            for (int i = 0; i < 40; i++) deck.Add(Plain("F" + i, 22));
            var md06 = Plain("MD06", 22);
            md06.Flags = new List<string> { "OPEN:" + Card.OpenRelations };
            deck.Add(md06);
            deck.Add(Plain("TAIL", 22));

            var g = new Game(deck, coin: () => false);
            driver.DebugReplaceGame(g);
            fake.Confirm();

            int guard = 0;
            while (guard++ < 8000 && g.State == GameState.Playing && !g.RelationshipsLost)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.25f);
                if (g.CurrentCard != null && g.CardTimer < 3.5f) fake.No();
            }
            Assert.IsTrue(g.RelationshipsLost, "расставание состоялось");

            yield return null;                       // кадр, на котором драйвер отразил расставание
            Assert.IsTrue(driver.BreakupPlate.activeSelf, "плашка «РАССТАЛИСЬ» поднялась");
            Assert.IsFalse(driver.BalancerGroup.activeSelf, "…а балансир на это время погашен");

            // …и ДОБИРАЕМСЯ ДО MD06 СИНХРОННО, без единого кадра: собственные 2 с плашки идут по
            // Time.deltaTime, поэтому без yield они не тратятся и окно наложения остаётся настоящим.
            int hops = 0;
            while (hops++ < 200 && g.State == GameState.Playing
                   && (g.CurrentCard == null || g.CurrentCard.Id != "MD06"))
            {
                fake.No();
            }
            Assert.AreEqual("MD06", g.CurrentCard?.Id, "дошли до карточки второго шанса");
            Assert.IsTrue(driver.BreakupPlate.activeSelf, "плашка ВСЁ ЕЩЁ висит — окно наложения реально");

            fake.Yes();                              // второй шанс сыгран → шкала открывается заново
            Assert.IsFalse(g.RelationshipsLost, "MD06 вернул отношения");

            // Инвариант на всём хвосте: никогда оба сразу.
            bool balancerCameBack = false;
            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
                if (driver.NewScaleShowing) NewScaleTut.Clear(driver, fake);
                Assert.IsFalse(driver.BreakupPlate.activeSelf && driver.BalancerGroup.activeSelf,
                    "плашка и балансир НИКОГДА не рисуются в слоте одновременно");
                if (driver.BalancerGroup.activeSelf) balancerCameBack = true;
            }
            Assert.IsTrue(balancerCameBack, "балансир действительно вернулся — тест не выродился");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ п.2 — ЖИВОЙ BLOCK$ НА ЭКРАНЕ

        // Колода, которая кладёт под руку ДЕШЁВУЮ BLOCK$-карточку (FA02, 10 ₽).
        //
        // ДВА НАМЕРЕННЫХ ВЫБОРА, оба нужны, чтобы обратная сторона пересчёта вообще была наблюдаемой:
        //  • ЦЕНА 10 ₽ — «деньги просели ниже цены» надо успеть показать В ПРЕДЕЛАХ ЖИЗНИ КАРТОЧКИ, а
        //    стоимость жизни капает 0.5 ₽/с; на 60-рублёвой цене запас проедался бы минутами, и
        //    карточка ушла бы по таймауту раньше, чем вернулась бы блокировка.
        //  • ВОЗРАСТ 22 — возраст догоняет возраст ТЕКУЩЕЙ карточки, а открытие новой шкалы поднимает
        //    §D-окно и ставит игру на ПАУЗУ, под которой Tick не двигает деньги вовсе. 22 лежит между
        //    отношениями (20) и энергией (25), так что проедание идёт по живой, непаузированной игре.
        // GRACE поглощает одноразовую льготу r4 п.1б, которая иначе подменила бы саму BLOCK$-карточку.
        private static Game LiveBlockDeck()
        {
            var deck = new List<Card>
            {
                Starter(), Plain("FILL", 18), Plain("GRACE", 20),
                Plain("FA02", 22), Plain("NORMAL", 23),
            };
            deck[3].IsBlockCost = true;   // FA02 → BLOCK$ (Game.BlockPrices["FA02"] = 10)
            return new Game(deck, coin: () => false);
        }

        /// <summary>
        /// Довести живую игру до выданной BLOCK$-карточки FA02, будучи на мели.
        ///
        /// Дорога именно ЖИВАЯ (кадры + §D-окна), а не подстановка состояния: по пути открываются
        /// деньги (18) и отношения (20), каждое своим модальным окном, и без их закрытия игра стоит на
        /// паузе — под ней деньги не двигаются вовсе, и любой гард про живой пересчёт был бы ложью.
        /// Льгота r4 п.1б тратится на карточке GRACE и до BLOCK$-карточки не доживает.
        /// </summary>
        private static IEnumerator ReachBlockCard(GameDriver driver, PlayFakeInputSource fake, Game g)
        {
            int guard = 0;
            while (guard++ < 600 && g.State == GameState.Playing
                   && (g.CurrentCard == null || g.CurrentCard.Id != "FA02"))
            {
                if (driver.NewScaleShowing || driver.TutorialShowing)
                {
                    NewScaleTut.ClearAny(driver, fake);
                    yield return null;
                    continue;
                }
                g.Tick(0.25f);
                if (g.CurrentCard != null && g.CardTimer < 3.0f) fake.No();
                yield return null;
            }

            Assert.AreEqual("FA02", g.CurrentCard?.Id, "под рукой настоящая BLOCK$-карточка");
            Assert.IsFalse(g.Paused, "игра не на паузе — иначе деньги бы не двигались вовсе");
            Assert.IsTrue(g.CurrentCardBlocked, "выдана, когда денег меньше цены → заперта");
        }

        /// <summary>
        /// НЕДОСТУПНОСТЬ ЖИВЁТ НА ЭКРАНЕ И ХОДИТ В ОБЕ СТОРОНЫ (r6 п.2) — главный гард инкремента.
        ///
        /// Жалоба основательницы: «докрутил нужную сумму прямо под запертой карточкой, а она так и
        /// стоит запертой». До r6 драйвер рисовал недоступность ОДИН раз, на выдаче (OnCardChanged),
        /// и снять её мог только следующей карточкой. Здесь проверяется весь комплект визуала разом,
        /// дважды: сначала он УХОДИТ, когда денег стало хватать, потом ВОЗВРАЩАЕТСЯ, когда стоимость
        /// жизни съела разницу.
        ///
        /// Сюда же вшито решение основательницы про плашки: гаснет ТОЛЬКО зелёная (недоступна покупка),
        /// красная «СПАСИБО, НЕ НАДО» не трогается вовсе — отказ доступен всегда. Тон зелёной обязан
        /// совпасть с тоном приглушения карточки: одно сообщение одним токеном.
        ///
        /// Mutation-proof: убери вызов <c>ReflectBlockedVisuals</c> из <c>Update</c> (оставив старую
        /// отрисовку только в <c>OnCardChanged</c>) — первая половина краснеет: баннер и приглушение
        /// не снимаются. Верни тинт обеим плашкам — краснеет ассерт про нетронутую красную.
        /// </summary>
        [UnityTest]
        public IEnumerator BlockedCard_LiveVisuals_ClearWhenAfforded_AndReturnWhenDrained()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = LiveBlockDeck();
            driver.DebugReplaceGame(g);
            fake.Confirm();                              // → StartLife, стартер выдан

            // Доехать до FA02 живой дорогой: возраст догоняет карточку, по пути открываются деньги
            // (18) и отношения (20) — каждое своим §D-окном, которое надо закрыть, иначе игра стоит
            // на паузе. Льгота r4 п.1б тратится на GRACE и до BLOCK$-карточки не доживает.
            yield return ReachBlockCard(driver, fake, g);

            var dim = GameDriver.CardBlockDimTone;

            // --- СОСТОЯНИЕ «НЕДОСТУПНА»: баннер, приглушение карточки, неактивная зелёная -------------
            Assert.IsTrue(driver.BlockBanner.activeSelf, "баннер «Как жаль…» поднят");
            Assert.Less(driver.CardFrameImage.color.b, 0.9f, "карточка приглушена");
            Assert.AreEqual(dim.g, driver.YesPlateImage.color.g, 0.001f,
                "ЗЕЛЁНАЯ выглядит неактивной — тем же дим-тоном, что карточка");
            Assert.AreEqual(1f, driver.NoPlateImage.color.r, 0.001f,
                "КРАСНАЯ не тронута: отказ доступен всегда");

            // --- ИГРОК ДОКРУЧИВАЕТ ДО ЦЕНЫ: всё обязано СНЯТЬСЯ без смены карточки --------------------
            while (g.Money < 11.0) g.HandleInput(GameInput.MoneyTick);
            yield return null;                           // один кадр живого Update

            Assert.AreEqual("FA02", g.CurrentCard.Id, "карточка та же — её никто не подменял");
            Assert.IsFalse(g.CurrentCardBlocked, "денег хватает → доступна");
            Assert.IsFalse(driver.BlockBanner.activeSelf, "баннер СНЯТ живьём");
            Assert.Greater(driver.CardFrameImage.color.b, 0.9f, "приглушение карточки снято");
            Assert.AreEqual(1f, driver.YesPlateImage.color.g, 0.001f, "ЗЕЛЁНАЯ ОЖИЛА");
            Assert.AreEqual(1f, driver.NoPlateImage.color.r, 0.001f, "красная как была");

            // --- …И ОБРАТНО: стоимость жизни съедает разницу, карточка запирается снова ---------------
            double over = g.Money - 10.0;
            float eat = (float)(over / Game.CostOfLivingPerSec) + 0.6f;
            Assert.Less(eat, g.CardTimer, "проедание укладывается в остаток жизни карточки");
            g.Tick(eat);
            yield return null;

            Assert.AreEqual("FA02", g.CurrentCard.Id, "и снова та же карточка");
            Assert.IsTrue(g.CurrentCardBlocked, "деньги просели ниже цены → заперлась ОБРАТНО");
            Assert.IsTrue(driver.BlockBanner.activeSelf, "баннер ВЕРНУЛСЯ");
            Assert.Less(driver.CardFrameImage.color.b, 0.9f, "приглушение вернулось");
            Assert.AreEqual(dim.g, driver.YesPlateImage.color.g, 0.001f, "зелёная снова неактивна");
            Assert.AreEqual(1f, driver.NoPlateImage.color.r, 0.001f, "красная так и не тронута ни разу");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ ГЕЙТЫ r6 — ФИНАЛЬНЫЙ РАУНД
        //
        // Ниже — находки ДВУХ скептиков по уже собранному r6 (код + дизайн). Каждая держит СВОЙ
        // инвариант; общий helper один — перевод любого ректа в референс-пиксели кадра 1920×1080.

        /// <summary>
        /// Бокс ректа В РЕФЕРЕНС-ПИКСЕЛЯХ КАДРА: (L, T, R, B), y растёт ВНИЗ.
        ///
        /// ⚠ СЧИТАЕТСЯ ПО ЯКОРЯМ И sizeDelta, А НЕ ЧЕРЕЗ GetWorldCorners (грабли, на которые этот гард
        /// уже наступил). `AnchorPx` кладёт ПОЗУ в нормированные якоря, а РАЗМЕР — в sizeDelta, то есть
        /// в юнитах холста. В батч-прогоне вьюпорт не 16:9, CanvasScaler выдаёт холст СВОИХ пропорций —
        /// и мировые углы, приведённые к 1920×1080, врут по размеру в обе стороны (замерено: CTA 116
        /// юнитов читалась как 100 px). Кадр же всегда рендерится в 1920×1080, где юнит холста РАВЕН
        /// референс-пикселю, — поэтому «якорь + sizeDelta» и есть геометрия СНИМКА, а не вьюпорта.
        /// Годится для ректов, поставленных `AnchorPx` (точечный якорь), — только такие тут и мерим.
        /// </summary>
        private static Vector4 RefBoxPx(RectTransform rt)
        {
            float cx = rt.anchorMin.x * 1920f;
            float cy = (1f - rt.anchorMin.y) * 1080f;
            var s = rt.sizeDelta;
            return new Vector4(cx - s.x / 2f, cy - s.y / 2f, cx + s.x / 2f, cy + s.y / 2f);
        }

        /// <summary>
        /// БАННЕР «КАК ЖАЛЬ…» ОТПУСКАЕТ И ПЛАШКУ ОТВЕТА, И РАМУ КАРТОЧКИ (дизайн-гейт r6, MAJOR).
        ///
        /// Замер гейта на кадре r6-blocked: баннер 900×110 (y 705…815) НАЕЗЖАЛ на «СПАСИБО НЕ НАДО» —
        /// пересечение 6876 px², глубина 54 px, — и целовал раму карточки (зазоры 14 px с боков).
        /// Перевёрстан в 780×70 (y 675…745): боковые гаттеры ~70 px, как у бокса вопроса, низ отпускает
        /// плашку, до чипа цены остаётся ~66 px.
        ///
        /// Mutation-proof: верни <c>BlockBannerRect</c> к <c>new(959.5f, 760f, 900f, 110f)</c> — красный
        /// и зазор до плашки (станет отрицательным), и боковые зазоры.
        /// </summary>
        [UnityTest]
        public IEnumerator BlockBanner_ClearsTheAnswerPlate_AndTheCardFrame()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            driver.DebugPreviewBlocked();
            yield return null;

            var banner = RefBoxPx((RectTransform)driver.BlockBanner.transform);
            var chip = RefBoxPx(driver.CardPricePlate.rectTransform);
            var noPlate = RefBoxPx(driver.NoPlateImage.rectTransform);

            // (1) РАМА. «Рама» в коде — кремовое поле карточки (asset-map §8): всё, что карточка
            // рисует, живёт внутри него, и баннер обязан держать от его краёв воздух, а не касаться.
            Assert.GreaterOrEqual(banner.x - GameDriver.FieldX, 40f,
                "зазор баннер↔рама слева");
            Assert.GreaterOrEqual(GameDriver.FieldX + GameDriver.FieldW - banner.z, 40f,
                "зазор баннер↔рама справа");
            Assert.AreEqual(banner.x - GameDriver.FieldX, GameDriver.FieldX + GameDriver.FieldW - banner.z, 2f,
                "…и гаттеры симметричны — баннер стоит по оси карточки");

            // (2) ПЛАШКА ОТВЕТА. Кромка НАРИСОВАННОЙ «СПАСИБО НЕ НАДО» СКОШЕНА — наклон запечён в
            // спрайте, а рект стоит прямо, поэтому box-vs-box тут врёт в обе стороны: бокс ректа
            // объявляет верх 723 на всей ширине, тогда как краска в этой точке ещё не началась.
            // Кромка снята с кадра инструментально (PIL, r6-blocked.png): (230, 713) и (500, 755),
            // между ними она прямая — наклон 0.156 px/px. Мерим там, где плашка и баннер реально
            // сходятся: под ЛЕВЫМ краем баннера.
            const float EdgeX0 = 230f, EdgeY0 = 713f, EdgeSlope = 0.1556f;
            float plateInk = EdgeY0 + (banner.x - EdgeX0) * EdgeSlope;
            Assert.Greater(noPlate.z, banner.x, "плашка и баннер вообще пересекаются по x — мерить есть что");
            Assert.GreaterOrEqual(plateInk - banner.w, 12f,
                "зазор баннер↔краска плашки «СПАСИБО НЕ НАДО» (был минус 54 px — наезд)");

            // (3) ЧИП ЦЕНЫ — вторая фигура той же полосы, её баннер тоже не поджимает.
            Assert.GreaterOrEqual(chip.y - banner.w, 40f, "зазор баннер↔чип цены");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ЧИП ЦЕНЫ ЧИТАЕТСЯ ОДИНАКОВО В ОБОИХ СОСТОЯНИЯХ — РАЗНИЦА ТОЛЬКО В ЦВЕТЕ (дизайн-гейт r6).
        ///
        /// Было «цена 100 ₽» в блоке против «СТОИТ 100 ₽» в доступном: рассинхрон регистра плюс скачок
        /// ширины чипа на ±18 px ровно в тот момент, когда карточка оживает. Живой пересчёт r6 делает
        /// этот момент частым, так что чип дёргался на глазах у игрока. Теперь строка ОДНА, а состояние
        /// несёт цвет: бело-лавандовый «нужно столько» против золотого «стоит столько».
        ///
        /// Mutation-proof: верни развилку формулировок в <c>ApplyPriceLabel</c> — краснеют и равенство
        /// строк, и равенство ширин.
        /// </summary>
        [UnityTest]
        public IEnumerator PriceChip_SaysTheSameInBothStates_OnlyTheColourChanges()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            driver.DebugPreviewBlocked();
            yield return null;
            string blockedText = driver.CardPriceText.text;
            var blockedColor = driver.CardPriceText.color;
            var blockedSize = driver.CardPricePlate.rectTransform.sizeDelta;

            driver.DebugPreviewUnblocked();
            yield return null;
            string liveText = driver.CardPriceText.text;
            var liveColor = driver.CardPriceText.color;
            var liveSize = driver.CardPricePlate.rectTransform.sizeDelta;

            Assert.AreEqual("СТОИТ 100 ₽", blockedText, "запертая карточка говорит ту же фразу капсом");
            Assert.AreEqual(blockedText, liveText, "формулировка не меняется вместе с состоянием");
            Assert.AreEqual(blockedSize.x, liveSize.x, 0.01f,
                "и ширина чипа не прыгает на фронте — вёрстку больше не перекладывает");
            Assert.AreEqual(blockedSize.y, liveSize.y, 0.01f, "…и высота тоже");
            Assert.AreNotEqual(blockedColor, liveColor, "состояние несёт ЦВЕТ: лаванда против золота");
            Assert.Greater(liveColor.b, 0.0f);
            Assert.Less(liveColor.b, blockedColor.b, "доступная — золото (синего в нём меньше)");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// «РАССТАЛИСЬ» СОБРАНА ГЕНЕРАТОРОМ ПЛАШЕК, А НЕ ЗАЛИВКОЙ (дизайн-гейт r6, MAJOR).
        ///
        /// Была плоским <c>NewSolid</c> сплошного TimerRed: без канта, без скругления, без тени —
        /// единственная фигура HUD не на языке арт-пака, да ещё четвёртый красный на экране. Стала той
        /// же конструкцией, что плашки блица: кант INK 6 px → красная обойма цветом ПАКА → кремовое
        /// поле, радиус 24, жёсткая тень (+6, −6). Гард читает РАСТР спрайта, то есть проверяет
        /// нарисованное, а не намерение.
        ///
        /// Mutation-proof: верни <c>NewSolid</c> (или обнули радиус/кант/тень) — краснеет
        /// соответствующий ассерт: угол станет непрозрачным, кант — красным, тень исчезнет.
        /// </summary>
        [UnityTest]
        public IEnumerator Breakup_Plate_IsDrawnByThePlateGenerator_KantRadiusShadow()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();
            yield return null;

            var root = driver.BreakupPlate.transform;
            var face = root.Find("BreakupPlateFace")?.GetComponent<Image>();
            var shade = root.Find("BreakupPlateShadow")?.GetComponent<Image>();
            Assert.IsNotNull(face, "у плашки есть лицо — сгенерированный спрайт");
            Assert.IsNotNull(shade, "…и жёсткая тень отдельной копией спрайта");

            // ТЕНЬ: тот же спрайт (значит, повторяет скруглённый силуэт), чернильная, сдвинута вправо-вниз
            // и лежит ПОД лицом.
            Assert.AreSame(face.sprite, shade.sprite, "тень — копия того же силуэта, а не прямоугольник");
            Assert.AreEqual(new Vector2(6f, -6f), shade.rectTransform.anchoredPosition,
                "жёсткая тень сдвинута на (+6, +6) вниз-вправо");
            Assert.Less(shade.transform.GetSiblingIndex(), face.transform.GetSiblingIndex(),
                "тень рисуется ПОД плашкой");
            Assert.Less(shade.color.r + shade.color.g + shade.color.b, 0.3f, "тень чернильная");

            // РАСТР: кольца по глубине от контура — кант, обойма, поле.
            var tex = face.sprite.texture;
            Assert.AreEqual(480, tex.width, "растр нарисован под собственный габарит плашки");
            Assert.AreEqual(103, tex.height);
            int midY = tex.height / 2;
            Color kant = tex.GetPixel(2, midY);          // глубина 2 < 6 → чёрный кант
            Color bezel = tex.GetPixel(12, midY);        // 6 < 12 < 20 → красная обойма
            Color field = tex.GetPixel(60, midY);        // глубоко внутри → кремовое поле

            Assert.Less(kant.r + kant.g + kant.b, 0.3f, "кант 6 px — чернильный");
            Assert.Greater(bezel.r, 0.75f, "обойма красная…");
            Assert.Less(bezel.g, 0.25f, "…и это красный ПАКА (#E60E17), а не оранжево-алый TimerRed");
            Assert.Less(bezel.b, 0.25f);
            Assert.Greater(field.r, 0.9f, "поле кремовое…");
            Assert.Greater(field.g, 0.9f);
            Assert.Greater(field.b, 0.8f);

            // РАДИУС 24: по диагонали угла контур проходит через (7.0, 7.0) = 24 − 24/√2.
            Assert.Less(tex.GetPixel(4, 4).a, 0.1f, "угол скруглён — снаружи дуги пусто");
            Assert.Greater(tex.GetPixel(11, 11).a, 0.9f, "…а внутри дуги плашка уже есть");

            // ТЕКСТ — тёмно-синий INK на кремовом, а не белый по красному.
            var txt = root.Find("BreakupText").GetComponent<Text>();
            Assert.Less(txt.color.r + txt.color.g + txt.color.b, 0.3f, "«РАССТАЛИСЬ» набрано INK");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// «РАССТАЛИСЬ» НЕ ПРОСАЧИВАЕТСЯ НА ЭКРАН КРИЗИСА (находка код-скептика r6, MINOR).
        ///
        /// Плашка рисуется при <c>State == Playing</c> и уступает слот ЖИВОМУ балансиру. Кризис — тоже
        /// Playing, но RenderCrisis прячет ряд HUD вместе с балансиром, и условие «слот свободен»
        /// становилось ИСТИННЫМ: транзиентная плашка всплывала поверх экрана блица. Слот в кризисе не
        /// свободен — его нет вовсе.
        ///
        /// Mutation-proof: убери <c>!_game.InCrisis</c> — плашка появляется в кризисе, тест краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator Breakup_Plate_StaysHidden_WhileTheCrisisOwnsTheScreen()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();
            yield return null;

            // Колода: карточка-расставание в 44 (флаг BREAK) и сразу за ней возраст 45 — порог кризиса.
            // BREAK срабатывает на ДА (BreakOnNoSide не выставлен) — значит отвечать ей надо ДА.
            var br = Plain("BR", 44); br.BreaksRelationships = true;
            var deck = new List<Card> { Starter(), Plain("F18", 18), Plain("F20", 20), br };
            for (int i = 0; i < 8; i++) deck.Add(Plain("C" + i, 45));
            // Кризис не откроется без БЛОКА КРИЗИСА в плане (CheckCrisisTrigger молча возвращает false
            // на пустом списке мыслей) — поэтому игра собирается планом, а не голой колодой.
            var crisis = new List<Card>();
            for (int i = 1; i <= 8; i++) crisis.Add(Plain("CR0" + i, 45));   // CR01–05 мысли, CR06–08 импульс
            var plan = new DeckPlan { Deck = deck, Reserve = new List<Card>(), Crisis = crisis };
            var g = new Game(() => plan, coin: () => false) { BlitzNormalOnYesRoll = () => true };
            driver.DebugReplaceGame(g);
            fake.Confirm();

            int guard = 0;
            while (guard++ < 800 && g.State == GameState.Playing && !g.RelationshipsLost)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing || driver.SpecialModeShowing)
                { NewScaleTut.ClearAny(driver, fake); yield return null; continue; }
                driver.DebugTick(0.2f);
                if (g.CurrentCard != null && g.CardTimer < 3.5f)
                {
                    if (g.CurrentCard.Id == "BR") fake.Yes(); else fake.No();
                    driver.DebugClearFrameGuards();
                }
            }
            Assert.IsTrue(g.RelationshipsLost, "расставание случилось — плашке есть о чём сообщать");
            Assert.IsFalse(g.InCrisis, "…и кризис ещё не начался");

            yield return null;                          // кадр: Update поднял плашку на слот
            Assert.IsTrue(driver.BreakupPlate.activeSelf, "вне кризиса плашка видна — тест не выродился");

            // Возраст догоняет 45 БЕЗ настоящих кадров: собственные ~2 с плашки идут по Time.deltaTime,
            // и тугой цикл DebugTick их почти не тратит — плашка гарантированно ещё «горит».
            guard = 0;
            while (guard++ < 400 && !g.InCrisis && g.State == GameState.Playing) driver.DebugTick(0.05f);
            Assert.IsTrue(g.InCrisis, "кризис среднего возраста начался");

            yield return null;                          // первый живой кадр ПОД экраном блица
            Assert.IsFalse(driver.BreakupPlate.activeSelf,
                "в кризисе плашка скрыта — слота нет вовсе, ряд HUD убран");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ОПЕНЕР: ПОЛЯ ВНУТРИ КРЕМОВОЙ ПЛАШКИ СИММЕТРИЧНЫ (дизайн-гейт r6, MINOR).
        ///
        /// Было 92 px сверху (плашка → первая строка) против 27 px снизу (кант CTA → край плашки):
        /// зелёная кнопка лежала на нижней рейке. Группа «текст + CTA» поднята на OpenerGroupLift = 32,
        /// стало ≈60/59 — и ни кегль, ни интерлиньяж не тронуты.
        ///
        /// Mutation-proof: обнули <c>OpenerGroupLift</c> — нижнее поле падает к 27 px, тест краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator Opener_VerticalMargins_InsideTheCreamPlate_AreBalanced()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            var plate = RefBoxPx(driver.OpenerPlate.rectTransform);
            var cta = RefBoxPx((RectTransform)driver.OpenerPlate.transform.parent.Find("StartPlateEdge"));

            // Верхнее поле мерим по НАРИСОВАННЫМ глифам, а не по боксу текста: болталось именно то,
            // что видно глазом.
            var t = driver.OpenerRules;
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, t.GetGenerationSettings(t.rectTransform.rect.size));
            float upp = 1f / t.pixelsPerUnit;
            float maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++) maxY = Mathf.Max(maxY, verts[i].position.y * upp);
            // Бокс правил — ребёнок плашки на anchoredPosition (не AnchorPx), поэтому его центр
            // считаем от центра плашки: те же юниты холста = референс-пиксели кадра.
            float plateCy = (1f - driver.OpenerPlate.rectTransform.anchorMin.y) * 1080f;
            float glyphTop = plateCy - t.rectTransform.anchoredPosition.y - maxY;

            float top = glyphTop - plate.y;
            float bottom = plate.w - cta.w;

            // ⚠ ВЕРХНЕЕ ПОЛЕ МЕРИТСЯ ПО КВАДРАТАМ ГЛИФОВ, а не по краске: квадрат несёт над буквой
            // место под выносные и стоит примерно на 14 px выше самой краски (дизайн-гейт мерил
            // краску на кадре и видел 92 px до подъёма). Поэтому порог здесь 45 — это те же ≈59 px
            // чернил, что и снизу, то есть НЕ послабление, а тот же зазор в других единицах.
            Assert.GreaterOrEqual(top, 45f, "верхнее поле плашки правил (по квадратам глифов)");
            Assert.GreaterOrEqual(bottom, 50f, "нижнее поле: CTA больше не лежит на рейке плашки");
            Assert.AreEqual(top, bottom, 20f, "…и поля сошлись — композиция не съехала на один край");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ТРЕВОГА ДЕНЕГ НЕ ДРЕБЕЗЖИТ НА ГРАНИЦЕ ЦЕНЫ (находка код-скептика r6, MAJOR).
        ///
        /// Живой BLOCK$ (r6 п.2) сделал сигнал «денег не хватает» НЕПРЕРЫВНЫМ: крутилка даёт +1 ₽
        /// скачком, стоимость жизни съедает его за 2 с — и так по кругу, пока карточка висит. Тревога
        /// денег была единственной из четырёх без гистерезиса, поэтому каждый такой цикл стоил ЗВУКА
        /// тревоги на фронте вниз и САЛЮТА ЗВЁЗД на фронте вверх: награда за калибровку, которой не
        /// было, каждые две секунды. Теперь у неё тот же зазор, что у трёх соседок
        /// (<c>MoneyAlarmHysteresis</c>), и колебание в ±δ/2 вокруг цены не производит НИ ОДНОГО фронта.
        ///
        /// Mutation-proof: обнули <c>MoneyAlarmHysteresis</c> — каждый круг качелей поднимает фронт,
        /// краснеют и счётчик салюта, и счётчики звуков.
        /// </summary>
        [UnityTest]
        public IEnumerator MoneyAlarm_DoesNotChatter_WhenTheAccountWobblesAroundThePrice()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = LiveBlockDeck();
            driver.DebugReplaceGame(g);
            fake.Confirm();
            yield return ReachBlockCard(driver, fake, g);

            // Игрок довёл счёт до цены — карточка ожила, тревога законно погасла (один фронт, он же
            // салют за калибровку). ДАЛЬШЕ СЧИТАЕМ ТОЛЬКО ПОВТОРЫ.
            while (g.Money < 10.6) g.HandleInput(GameInput.MoneyTick);
            yield return null;
            Assert.IsFalse(g.CurrentCardBlocked, "карточка ожила — качели начинаются из доступного");

            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();
            int burstsBefore = driver.StarBurstCount;
            var chipBefore = driver.CardPricePlate.rectTransform.sizeDelta;

            // ДВА ПОЛНЫХ КРУГА КАЧЕЛЕЙ в коридоре ±1 ₽ вокруг цены: проели рубль (ушли под цену, но
            // НЕ на δ) — докрутили рубль обратно. Именно этот цикл и дребезжал.
            for (int i = 0; i < 2; i++)
            {
                g.Tick(2.0f);                         // −1 ₽: 9.6 … 9.9 — ниже цены, внутри зазора
                yield return null;
                Assert.IsTrue(g.CurrentCardBlocked, "карточка честно заперта (картинка обязана это показать)");
                g.HandleInput(GameInput.MoneyTick);   // +1 ₽ — снова выше цены
                yield return null;
                Assert.IsFalse(g.CurrentCardBlocked, "…и снова доступна");
            }

            Assert.AreEqual(0, driver.Audio.DebugCount(SoundEvent.AlarmMoney),
                "за качели тревога денег не издала НИ ОДНОГО звука");
            Assert.AreEqual(0, driver.Audio.DebugCount(SoundEvent.BlockMoney),
                "…и «вомп-вомп» живого запирания тоже молчит — фронт тревоги не поднимался");
            Assert.AreEqual(burstsBefore, driver.StarBurstCount,
                "…и салюта за несуществующую починку нет");
            Assert.AreEqual(chipBefore, driver.CardPricePlate.rectTransform.sizeDelta,
                "…и вёрстку чипа цены ни разу не переложило");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ЧЕСТНЫЙ ПРОВАЛ НИЖЕ ЦЕНЫ ПОДНИМАЕТ ФРОНТ РОВНО ОДИН РАЗ (пара к гарду выше).
        ///
        /// Зазор обязан ГАСИТЬ ДРЕБЕЗГ, а не сигнал: счёт, провалившийся на δ ниже цены, — это уже не
        /// качели, а «денег действительно нет», и тревога обязана вернуться. Заодно закрывается голос
        /// живого запирания (BlockMoney на фронте тревоги, находка код-скептика, MINOR): на выдаче он
        /// звучит из OnCardChanged, здесь — из фронта, и ни разу дважды.
        ///
        /// Mutation-proof: сделай гистерезис ОДНОСТОРОННИМ (никогда не зажигать) — оба счётчика
        /// остаются нулями. Сними условие <c>!cardChanged</c> у голоса — BlockMoney зазвучит дважды.
        /// </summary>
        [UnityTest]
        public IEnumerator MoneyAlarm_ReturnsOnce_WhenTheAccountHonestlyFallsBelowThePrice()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = LiveBlockDeck();
            driver.DebugReplaceGame(g);
            fake.Confirm();
            yield return ReachBlockCard(driver, fake, g);

            while (g.Money < 10.6) g.HandleInput(GameInput.MoneyTick);
            yield return null;
            Assert.IsFalse(g.CurrentCardBlocked, "стартуем из доступной — тревога погашена");

            driver.Audio.DebugRecord = true;
            driver.Audio.DebugClear();

            // Проедаем ЧЕСТНО: ниже цены на δ с запасом (10.6 → ~7.6 при цене 10 и δ = 2).
            for (int i = 0; i < 3; i++) { g.Tick(2.0f); yield return null; }

            Assert.AreEqual("FA02", g.CurrentCard?.Id, "карточка всё ещё на экране — мерим ЖИВОЙ фронт");
            Assert.Less(g.Money, 10.0 - GameDriver.MoneyAlarmHysteresis, "счёт провалился ниже зазора");
            Assert.AreEqual(1, driver.Audio.DebugCount(SoundEvent.AlarmMoney),
                "тревога денег вернулась РОВНО ОДИН раз");
            Assert.AreEqual(1, driver.Audio.DebugCount(SoundEvent.BlockMoney),
                "…и живое запирание озвучено ровно один раз, без дубля с выдачей");

            Object.Destroy(go);
            yield return null;
        }
    }
}
