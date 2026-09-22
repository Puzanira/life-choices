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
    /// ГАРДЫ ПАНЧ-ЛИСТА АВТОМАТА r5 — ЭКРАННАЯ ПОЛОВИНА (живой плейтест основательницы НА СТОЙКЕ
    /// 2026-09-22). Чистая половина (отклик балансира) живёт в EditMode-файле того же имени; словарь
    /// органов — в <c>ControlLanguageTests</c>, рядом со свипом дев-клавиш.
    ///
    /// Здесь закрывается п.4: «финальный текст большой, плохо читаемый, и шрифт не нравится».
    /// </summary>
    public class PlaytestFixesR5Tests
    {
        private static GameDriver Boot(out GameObject go)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            return driver;
        }

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

        /// <summary>Довести драйвер до открытого балансира (та же дорога, что в BalancerInputTests):
        /// колода держит возраст на 22, чтобы туториал энергии не влез в измерение оси.</summary>
        private static IEnumerator DriveToRelationshipsOpen(GameDriver driver, PlayFakeInputSource fake)
        {
            driver.DebugReplaceGame(new Game(new List<Card> { Starter(), Plain("A", 22) }, coin: () => false));
            fake.Confirm();
            fake.No();
            int guard = 0;
            while (!driver.Game.RelationshipsOpen && driver.Game.State == GameState.Playing && guard++ < 300)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.Game.Tick(0.2f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;
        }

        /// <summary>Центр маркера балансира в референс-пикселях (AnchorPx кладёт позу в anchorMin).</summary>
        private static float MarkerRefX(GameDriver driver)
            => ((RectTransform)driver.BalancerMarker.transform).anchorMin.x * 1920f;

        /// <summary>
        /// ОТКЛИК НА ЭКРАНЕ, А НЕ В МОДЕЛИ — гард, который стал возможен только после починки
        /// <c>UpdateHudValues</c> (r5-ревю, находка код-скептика MINOR).
        ///
        /// EditMode-близнец (`HeldAxis_MovesTheDrawnMarker_OnTheVeryFirstFrame`) проверяет
        /// <see cref="Game.RelationshipsPrecise"/> — то есть ЧИСЛО. Здесь проверяется ПИКСЕЛЬ: реально ли
        /// сдвинулся прямоугольник маркера за один кадр синхронного пути (`DebugTick`). До правки этот
        /// путь рисовал маркер по ЦЕЛОЙ шкале, поэтому написать такой гард было нельзя — он был бы красным
        /// на исправной игре.
        ///
        /// Измерение поставлено так, чтобы отличать ДРОБНЫЙ сдвиг от целого: сначала докручиваем кадрами
        /// до момента сразу ПОСЛЕ целого шага (остаток аккумулятора близок к нулю), и только потом меряем
        /// один кадр. Целая шкала за него заведомо не меняется — это проверяется ассертом, — значит любой
        /// сдвиг маркера имеет ровно один источник: дробная часть.
        ///
        /// Mutation-proof: верни в `UpdateHudValues` отрисовку по `Scales.Relationships` — целое за
        /// измеряемый кадр не меняется, маркер стоит, тест краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator HeldAxis_MovesTheDrawnMarkerRect_OnTheFirstSyncTick()
        {
            const float frame = 1f / 60f;

            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return DriveToRelationshipsOpen(driver, fake);

            Assume.That(driver.Game.RelationshipsOpen, Is.True, "предусловие: балансир открыт");
            Assume.That(driver.Game.Paused, Is.False, "предусловие: подсказки сняты, игра живая");

            // Встать сразу ПОСЛЕ целого шага — так остаток аккумулятора заведомо мал и следующий кадр
            // его точно не переполнит.
            int at = driver.Game.Scales.Relationships;
            int spin = 0;
            while (driver.Game.Scales.Relationships == at && spin++ < 30)
            {
                fake.Fire(GameInput.RelationRight);
                driver.DebugTick(frame);
            }
            Assume.That(spin, Is.LessThan(30), "предусловие: целый шаг набежал за разумное число кадров");

            int whole0 = driver.Game.Scales.Relationships;
            float x0 = MarkerRefX(driver);

            fake.Fire(GameInput.RelationRight);
            driver.DebugTick(frame);

            Assert.AreEqual(whole0, driver.Game.Scales.Relationships,
                "измеряемый кадр НЕ двигает целую шкалу — иначе сдвиг маркера объяснялся бы ею");
            Assert.Greater(MarkerRefX(driver), x0,
                $"за один кадр с зажатой осью прямоугольник маркера обязан сдвинуться вправо "
                + $"({x0:F2} → {MarkerRefX(driver):F2} px) — это и есть «джойстик слушается без задержки»");

            Object.Destroy(go);
            yield return null;
        }

        // ================================================================ ДИЗАЙН-ГЕЙТ r5 — геометрия

        /// <summary>
        /// Рект в референс-пикселях: (xMin, yMin, xMax, yMax), y — от ВЕРХА кадра.
        ///
        /// ⚠ Считается через МИРОВЫЕ УГЛЫ и канвас, а не через anchorMin×1920. Короткая формула верна
        /// только для прямых детей канваса; текст вопроса — внук (лежит внутри ректа карточки), и его
        /// якоря заданы долями РОДИТЕЛЯ. На нём короткая формула давала 392.9 вместо 444.5 — то есть
        /// «проверяла» геометрию, которой нет.
        /// </summary>
        private static (float xMin, float yMin, float xMax, float yMax) RefBox(GameDriver driver, RectTransform rt)
        {
            var canvas = driver.CanvasRect;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);                        // 0 = низ-лево, 2 = верх-право
            var lo = canvas.InverseTransformPoint(corners[0]);
            var hi = canvas.InverseTransformPoint(corners[2]);
            return (lo.x + 960f, 540f - hi.y, hi.x + 960f, 540f - lo.y);
        }

        /// <summary>Сколько символов генератор реально НАРИСОВАЛ бы без обрезки по высоте — и сколько
        /// рисует сейчас. Равенство = «ничего не срезано» (verticalOverflow у окон-задач = Truncate).</summary>
        private static (int shown, int wanted) VisibleVsWanted(Text t)
        {
            var was = t.verticalOverflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.cachedTextGenerator.Invalidate();
            Canvas.ForceUpdateCanvases();
            int shown = t.cachedTextGenerator.characterCountVisible;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.cachedTextGenerator.Invalidate();
            Canvas.ForceUpdateCanvases();
            int wanted = t.cachedTextGenerator.characterCountVisible;
            t.verticalOverflow = was;
            t.cachedTextGenerator.Invalidate();
            Canvas.ForceUpdateCanvases();
            return (shown, wanted);
        }

        /// <summary>
        /// ОДИН КЕГЛЬ НА ТРИ ТУТОРИАЛЬНЫЕ МОДАЛКИ (дизайн-гейт r5, MAJOR).
        ///
        /// Замер гейта: x-height денег 49 px → отношений 41 → депрессии 36. Кегль был ПОБОЧНЫМ ЭФФЕКТОМ
        /// длины строки — авторазмер 40…96 ужимал тот экран, где текста больше, то есть самый плотный
        /// экран игра показывала самым мелким шрифтом. Лечение: тексты укорочены, кегль прибит к
        /// <see cref="GameDriver.TaskTextFixedSize"/>.
        ///
        /// Гард держит ОБА конца: (1) все три окна набраны ОДНИМ кеглем — никакого авторазмера между
        /// ними; (2) при этом кегле ни на одном из них текст НЕ ОБРЕЗАН (окна стоят на Truncate, то есть
        /// не влезший текст исчезает МОЛЧА — без этой половины «одинаковый кегль» достигался бы обрезкой).
        /// Mutation-proof: подними токен на пару пунктов — депрессия (самый длинный текст на самом тесном
        /// боксе) начнёт резаться; верни авторазмер 40…96 — кегли разъедутся и первая половина краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator TaskWindows_ShareOneTypeSize_AndEveryLongestLineFits()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var task = driver.NewScaleTaskText;

            var sizes = new System.Collections.Generic.List<(string Where, int Size, int Shown, int Wanted)>();

            // Гейт назвал три модалки, но виджет окна-задачи ОДИН на все восемь экранов — значит и
            // проверять надо все восемь. Иначе поднятый кем-то токен молча срезал бы хвост чужого экрана
            // (ровно это и случилось на первой моей попытке: 76 резало «ребёнка» и блиц).
            foreach (var which in new[] { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child })
            {
                driver.DebugPreviewNewScale(which);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var (shown, wanted) = VisibleVsWanted(task);
                sizes.Add((which.ToString(), task.cachedTextGenerator.fontSizeUsedForBestFit, shown, wanted));
                driver.DebugCloseNewScale();
                yield return null;
            }

            foreach (var mode in new[] { SpecialMode.Depression, SpecialMode.Blitz,
                                         SpecialMode.Health, SpecialMode.Burnout })
            {
                driver.DebugPreviewSpecialMode(mode);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var (shown, wanted) = VisibleVsWanted(task);
                sizes.Add((mode.ToString(), task.cachedTextGenerator.fontSizeUsedForBestFit, shown, wanted));
                driver.DebugCloseSpecialMode();
                yield return null;
            }

            string report = string.Join(" · ", sizes.Select(s => $"{s.Where}: кегль {s.Size}, "
                                                                 + $"видно {s.Shown}/{s.Wanted}"));

            // (1) ОДИН КЕГЛЬ. Сравниваются сами замеры между собой: `fontSizeUsedForBestFit` отдаётся в
            // ЭКРАННЫХ пикселях (канвас-скейлер ужимает референс под окно раннера), поэтому сверять его с
            // референсной константой было бы сверкой разных единиц. Само число прибито конфигурацией —
            // её проверяет ассерт ниже.
            foreach (var s in sizes)
                Assert.AreEqual(sizes[0].Size, s.Size,
                    $"окно «{s.Where}» набрано не тем же кеглем, что «{sizes[0].Where}». Замер: {report}");

            Assert.AreEqual(GameDriver.TaskTextFixedSize, task.resizeTextMinSize,
                "окно-задача сидит на общем токене кегля снизу");
            Assert.AreEqual(GameDriver.TaskTextFixedSize, task.resizeTextMaxSize,
                "…и сверху — то есть авторазмер между экранами схлопнут, а не просто сужен");

            foreach (var s in sizes)
                Assert.AreEqual(s.Wanted, s.Shown,
                    $"окно «{s.Where}»: текст ОБРЕЗАН по высоте на общем кегле — опусти "
                    + $"GameDriver.TaskTextFixedSize. Замер: {report}");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ИМПУЛЬСНАЯ ПИЛЮЛЯ НЕ НАКРЫВАЕТ ВОПРОС (дизайн-гейт r5, BLOCKER).
        ///
        /// На кадре `r5-impulse` пилюля «молчание = ДА» закрывала 67 % второй строки вопроса. Чинилось
        /// ПАРОЙ правок, и гард держит именно пару: пилюля ушла в верхнюю внутреннюю полосу карточки, а
        /// бокс вопроса получил потолок высоты вокруг НЕПОДВИЖНОГО центра — то есть текст не съехал ни на
        /// пиксель, но и вырасти в полосу пилюли больше не может.
        ///
        /// Проверяется на ДЛИННЕЙШЕМ вопросе живой колоды и на синтетическом четырёхстрочном — потому что
        /// «зазор есть» на короткой карточке ничего не доказывает.
        /// Mutation-proof: верни пилюле y 650 — она сядет в середину бокса вопроса; сними потолок высоты
        /// (CardTextFullH → 535) — четырёхстрочный вопрос дорастёт вверх под пилюлю. Красное в обоих.
        /// </summary>
        [UnityTest]
        public IEnumerator ImpulsePill_ClearsTheQuestionText_OnTheLongestQuestion()
        {
            const float minGap = 16f;

            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugPreviewImpulsePlates();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var pill = RefBox(driver, (RectTransform)driver.ImpulseWarning.transform);
            var box = RefBox(driver, driver.CardQuestionText.rectTransform);

            // (1) ЗАЗОР — по БОКСУ, а не по сегодняшним буквам. Бокс вопроса не зависит от строки, поэтому
            // это утверждение накрывает ЛЮБОЙ вопрос разом, включая те, которых в колоде ещё нет.
            Assert.LessOrEqual(pill.yMax + minGap, box.yMin,
                $"пилюля «молчание = ДА» (низ {pill.yMax:F1}) не оставляет {minGap} px до бокса вопроса "
                + $"(верх {box.yMin:F1})");

            // (2) …и вторая половина: потолок высоты бокса не начал РЕЗАТЬ текст. Проверяется на
            // длиннейшем вопросе живой колоды и на синтетическом четырёхстрочном.
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "колода читается из Resources");
            string longest = CardLoader.ParseAll(csv.text)
                .Select(c => c.Question ?? "").OrderByDescending(q => q.Length).First();
            Assert.Greater(longest.Length, 30, "длиннейший вопрос колоды действительно длинный");

            foreach (var q in new[] { longest, "Съехаться, пожениться, взять ипотеку и завести кота — всё это прямо сейчас, одним махом?" })
            {
                driver.DebugSetCardQuestion(q);
                yield return null;
                Canvas.ForceUpdateCanvases();

                var (shown, wanted) = VisibleVsWanted(driver.CardQuestionText);
                Assert.AreEqual(wanted, shown,
                    $"вопрос обрезан потолком высоты бокса (видно {shown} из {wanted}): «{q}»");
            }

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ПИЛЮЛЯ ДЕПРЕССИИ НЕ СЪЕДАЕТ КАНТ КНОПКИ «СОБРАТЬСЯ» (дизайн-гейт r5, MAJOR).
        ///
        /// Замер гейта: пилюля y 820…913, кнопка y 914…1020 — зазор НОЛЬ, и чёрный кант кнопки был съеден
        /// пилюлей. Считается по ВИДИМЫМ границам: у кнопки кант вынесен отдельным спрайтом-соседом и
        /// торчит на <see cref="GameDriver.BlockKeylineInk"/> за её рект, поэтому «зазор до ректа» соврал
        /// бы ровно на толщину канта — то есть на то самое, что и было съедено.
        /// Mutation-proof: верни пилюле y 866 — зазор уходит в минус, тест краснеет.
        /// </summary>
        [Test]
        public void DepressionHintPill_LeavesTheGatherButtonItsKeyline()
        {
            const float minGap = 16f;

            var pill = GameDriver.DepHintPlateRect;
            var button = GameDriver.DepGatherPlateRect;

            float pillBottom = pill.y + pill.w / 2f;
            float buttonTop = button.y - button.w / 2f - GameDriver.BlockKeylineInk;   // кант — соседний спрайт

            Assert.GreaterOrEqual(buttonTop - pillBottom, minGap,
                $"между низом пилюли подсказки ({pillBottom:F1}) и ВЕРХОМ КАНТА кнопки «СОБРАТЬСЯ» "
                + $"({buttonTop:F1}) должно оставаться ≥{minGap} px, замерено {buttonTop - pillBottom:F1}");
        }

        /// <summary>
        /// ПОДПИСЬ ПЛАШКИ ВЫГОРАНИЯ БОЛЬШЕ НЕ САМЫЙ МЕЛКИЙ ТИП ИГРЫ (дизайн-гейт r5, MINOR).
        ///
        /// Замер гейта: капитель подписи ~13 px (кегль 18) — мельче всего остального в игре, — и при этом
        /// с полями в 44 % ширины плашки. Место было по ГОРИЗОНТАЛИ, а упор оказался по вертикали.
        ///
        /// ⚠ ЗАПРОС ГЕЙТА ВЫПОЛНЕН ЧАСТИЧНО, И ГАРД ЭТО ЧЕСТНО ОТРАЖАЕТ. Просили ×1.5–1.6; помещается
        /// кегль 22 (капитель ≈16, ×1.22) — выше не пускает видимая полоса пилюли в 56 px на две строки,
        /// см. разбор у самого набора в GameDriver. Поэтому гард держит не «×1.5», а то, что достижимо и
        /// действительно важно: ПОЛ коридора поднят не меньше чем в полтора раза (13 → 20), текст на нём
        /// НЕ РЕЖЕТСЯ, и подпись лежит внутри плашки с полями ≥12 px. Требовать здесь ×1.5 значило бы
        /// прибить гардом геометрически невозможное.
        /// Mutation-proof (обе проверены прогоном): верни коридор 13…18 — краснеет пол здесь; верни бокс
        /// на прежнюю высоту 0.3295 — глифы вылезают из пилюли (−30.6 против −29) и краснеет соседний гард
        /// соответствия `Burnout_TitleAndSubtitle_OnScreen_InBacking_NoTofu`.
        /// ⚠ А вот поднятие ПОТОЛКА коридора (22 → 26) НЕ ломает ничего и ничего не меняет: набор упирается
        /// в высоту бокса раньше, чем в потолок, — проверено мутацией, вышла зелёной. Несущий параметр
        /// здесь позиция и высота бокса, а не потолок; знать это важно, чтобы не «чинить» не ту ручку.
        /// </summary>
        [UnityTest]
        public IEnumerator BurnoutPlateCaption_IsNoLongerTheSmallestTypeInTheGame()
        {
            const int wasFloor = 13;          // коридор до правки — 13…18
            const float minMargin = 12f;

            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugPreviewBurnout();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var sub = driver.BurnoutPlate.GetComponentsInChildren<Text>(true)
                .First(t => t.name == "BurnoutSubtitle");

            Assert.GreaterOrEqual(sub.resizeTextMinSize, Mathf.CeilToInt(wasFloor * 1.5f),
                $"пол кегля подписи выгорания поднят минимум в полтора раза от прежних {wasFloor} "
                + $"(сейчас {sub.resizeTextMinSize})");

            var (shown, wanted) = VisibleVsWanted(sub);
            Assert.AreEqual(wanted, shown,
                $"на поднятом кегле подпись «{sub.text}» обрезана ({shown} из {wanted}) — "
                + "кегль вырос за счёт хвоста, а не за счёт бокса");

            var plate = RefBox(driver, (RectTransform)driver.BurnoutPlate.transform
                .GetComponentsInChildren<Image>(true).First(i => i.name == "BurnoutPlateFill").transform);
            var box = RefBox(driver, sub.rectTransform);
            Assert.GreaterOrEqual(box.xMin - plate.xMin, minMargin, "подпись не выходит на левый край плашки");
            Assert.GreaterOrEqual(plate.xMax - box.xMax, minMargin, "…и на правый");
            Assert.GreaterOrEqual(plate.yMax - box.yMax, minMargin, "…и не свисает с низа плашки");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// КОРЕНЬ «ПЛОХО ЧИТАЕМОГО» — некролог набирался ВАРИАТИВНЫМ `Fonts/Rubik`, у которого ось wght
        /// 300…900 с дефолтом 300: legacy-uGUI растеризует дефолтную инстанцию, то есть Rubik LIGHT.
        /// Семь светлых строк на кремовом поле, которые читают с расстояния стойки, — это и есть жалоба.
        /// Ту же ловушку уже ловили на плашке Ведущего (там завели статический `Rubik-Bold`).
        ///
        /// Гард держит ИНВАРИАНТ, а не сегодняшний набор: финальный блок не имеет права стоять на лёгком
        /// начертании, каким бы ни был выбор основательницы между A/B/C.
        /// Mutation-proof: верни `_body` (то есть `Fonts/Rubik`) — тест краснеет на имени шрифта.
        /// </summary>
        [UnityTest]
        public IEnumerator FinaleNecrolog_IsNeverSetInTheLightVariableRubik()
        {
            var driver = Boot(out var go);
            yield return null;

            var font = driver.FinaleStoryText.font;
            Assert.IsNotNull(font, "блок некролога поднял шрифт");
            Assert.AreNotEqual("Rubik", font.name,
                "некролог не имеет права стоять на вариативном Rubik — его дефолт wght 300 (Light), "
                + "именно он и читался с дистанции как «плохо читаемый» (r5 п.4)");
            Assert.That(font.name, Is.EqualTo("Rubik-Bold").Or.EqualTo("Arimo-Bold"),
                $"лицо некролога — одно из жирных начертаний проекта, а не «{font.name}»");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Набор, который реально стоит в игре, совпадает с объявленным <see
        /// cref="GameDriver.ShippedFinaleStoryStyle"/> — лицо, коридор кеглей и межстрочье. Без этого
        /// «выбор основательницы по кадрам» мог бы разъехаться с тем, что собирает драйвер: кадры
        /// показывали бы один набор, а автомат печатал другой.
        /// </summary>
        [UnityTest]
        public IEnumerator FinaleNecrolog_RendersExactly_TheShippedStyle()
        {
            var driver = Boot(out var go);
            yield return null;

            var style = GameDriver.ShippedFinaleStoryStyle;
            var t = driver.FinaleStoryText;

            Assert.AreEqual(style.UseDisplayFace ? "Arimo-Bold" : "Rubik-Bold", t.font.name,
                "лицо блока — из отгружаемого набора «" + style.Name + "»");
            Assert.AreEqual(style.MaxSize, t.resizeTextMaxSize, "потолок кегля — из набора");
            Assert.AreEqual(style.MinSize, t.resizeTextMinSize, "пол кегля — из набора");
            Assert.AreEqual(style.LineSpacing, t.lineSpacing, 1e-4f, "межстрочье — из набора");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ВСЕ ТРИ НАБОРА ЛЕЖАТ ВНУТРИ КАНОН-КОРИДОРА ЧИТАЕМОСТИ. Пол 24 px — задокументированный порог
        /// на кабинетном экране (<see cref="GameDriver.FinaleStoryMinSize"/>), и ни один «красивый» набор
        /// не имеет права его продавить ради того, чтобы влезло побольше текста: если не влезает, режут
        /// лимит вех, а не кегль. Потолок 34 — кегль эталона, выше блок не растёт.
        /// Mutation-proof: опусти пол любого набора до 22 — тест краснеет именно на нём.
        /// </summary>
        [Test]
        public void FinaleStoryStyles_StayInsideTheCanonBounds()
        {
            var styles = GameDriver.FinaleStoryStyles;
            Assert.AreEqual(3, styles.Length, "наборов ровно три — A/B/C, по кадрам основательнице");

            foreach (var s in styles)
            {
                Assert.GreaterOrEqual(s.MinSize, GameDriver.FinaleStoryMinSize,
                    $"набор «{s.Name}»: пол {s.MinSize} ниже порога читаемости {GameDriver.FinaleStoryMinSize}");
                Assert.LessOrEqual(s.MaxSize, GameDriver.FinaleStoryMaxSize,
                    $"набор «{s.Name}»: потолок {s.MaxSize} выше канон-кегля {GameDriver.FinaleStoryMaxSize}");
                Assert.Greater(s.MaxSize, s.MinSize, $"набор «{s.Name}»: коридор непустой");
                Assert.GreaterOrEqual(s.LineSpacing, 1.0f, $"набор «{s.Name}»: строки не наезжают друг на друга");
            }

            Assert.IsTrue(styles.Any(s => s.Name == GameDriver.ShippedFinaleStoryStyle.Name),
                "отгружаемый набор — один из трёх, показанных основательнице, а не четвёртый тайком");
        }
    }
}
