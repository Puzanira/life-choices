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
    /// ГАРДЫ ПЛЕЙТЕСТ-ФИКСОВ r4 — экранная половина (то, что доказывается только живым драйвером):
    /// п.1а тревога молчит под собственным обучением, п.1б первая карточка после обучения по карману,
    /// п.3 ЖИВОЙ путь ввода «зажата стрелка → ось → маркер» на настоящих кадрах.
    /// Колодная половина — в EditMode-файле того же имени.
    /// </summary>
    public class PlaytestFixesR4Tests
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

        /// <summary>Шаг времени для зонда «мгновенная чистка против штатного затухания»: заметно
        /// МЕНЬШЕ AlarmFadeSeconds, иначе затухание успеет дойти до нуля само и зонд ослепнет.</summary>
        private const float FadeProbeDt = 0.05f;

        // =====================================================================================
        // п.1а — ТРЕВОГА ШКАЛЫ МОЛЧИТ, ПОКА ИДЁТ ЕЁ СОБСТВЕННОЕ ОБУЧЕНИЕ
        // =====================================================================================

        /// <summary>
        /// ЖАЛОБА ОСНОВАТЕЛЬНИЦЫ: «во время туториала деньги были красными — смотрелось очень плохо».
        /// Деньги открываются с 0 ₽, стоимость жизни сразу уводит в минус, и §D-окно показывает КРАСНУЮ
        /// банку ровно в тот момент, когда учит ею пользоваться.
        ///
        /// Виджет под модалкой — ТОТ ЖЕ объект (его одалживает BorrowBigWidget) и он остаётся активным,
        /// поэтому обычная проверка «виджет на экране» его не отсекает: гасить надо явно.
        /// Mutation-proof: убери подавление в AlarmLive — тревога поднимется и тест покраснеет.
        /// </summary>
        /// <summary>Довести живой драйвер до возраста, где банка уже на экране (деньги открыты в 18),
        /// и снять все всплывшие по дороге окна — дальше тесты сами поднимают нужное.</summary>
        private static IEnumerator DriveToMoneyOnScreen(GameDriver driver, PlayFakeInputSource fake)
        {
            driver.DebugReplaceGame(new Game(new List<Card> { Starter(), Plain("A", 22) },
                                             coin: () => false));
            fake.Confirm();
            fake.No();
            int guard = 0;
            while (!driver.Game.MoneyOpen && driver.Game.State == GameState.Playing && guard++ < 400)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.Game.Tick(0.2f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoneyAlarm_IsSilent_WhileItsOwnTutorialIsUp()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return DriveToMoneyOnScreen(driver, fake);
            Assume.That(driver.Game.MoneyOpen, Is.True, "предусловие: банка на экране");

            driver.DebugShowNewScale(NewScale.Money);
            yield return null;
            Assume.That(driver.NewScaleKind, Is.EqualTo(NewScale.Money), "поднято §D-окно ДЕНЕГ");

            // Зажечь тревогу банки НАСИЛЬНО, затем дать драйверу один такт §4: если шкала «не живая»
            // (подавлена своим обучением), ReflectAlarms гасит её тихо и без салюта.
            driver.DebugPaintAlarm(AlarmScale.Money);
            Assume.That(driver.AlarmActive(AlarmScale.Money), Is.True, "предусловие: тревога зажжена");
            driver.DebugPumpAlarms(FadeProbeDt);
            yield return null;

            Assert.IsFalse(driver.AlarmActive(AlarmScale.Money),
                "под собственным обучением тревога денег МОЛЧИТ (r4 п.1а)");
            Assert.AreEqual(0f, driver.AlarmWeight(AlarmScale.Money), 1e-3f,
                "…и не подкрашена даже частично: подавление гасит ВМИГ, а не плавным затуханием — "
                + "на экране обучения не должно мелькнуть даже полкадра красного");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ОБРАТНАЯ СТОРОНА: подавление АДРЕСНОЕ. Та же банка, та же насильно зажжённая тревога — но
        /// открыто ЧУЖОЕ окно (энергии). Отличие от теста выше — РОВНО ОДИН аргумент, вид поднятого
        /// окна, поэтому тест бьёт точно в адресность. Mutation-proof к ленивой правке «гасить все
        /// тревоги под любой модалкой».
        ///
        /// ⚠ МЕРИМ ВЕС, А НЕ ФЛАГ, И ВОТ ПОЧЕМУ. Тревога денег — это `CurrentCardBlocked`, и на обычной
        /// карточке она НЕ ВЗВЕДЕНА; значит флаг `AlarmActive` погаснет в обоих случаях, и тест на него
        /// ничего бы не доказал. Но гаснет он ПО-РАЗНОМУ: подавлённая шкала чистится МГНОВЕННО (ветка
        /// «не живая» в ReflectAlarms), а живая-но-неподнятая ЗАТУХАЕТ за AlarmFadeSeconds. Поэтому
        /// на маленьком шаге времени вес подавлённой уже ноль, а вес чужой — ещё нет. Разделяет чисто.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSuppression_IsScopedToTheExplainedScale_ForeignAlarmsAreNotCleared()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return DriveToMoneyOnScreen(driver, fake);
            Assume.That(driver.Game.MoneyOpen, Is.True, "предусловие: банка на экране");

            driver.DebugShowNewScale(NewScale.Energy);
            yield return null;
            Assume.That(driver.NewScaleKind, Is.EqualTo(NewScale.Energy), "поднято ЧУЖОЕ окно (энергии)");

            driver.DebugPaintAlarm(AlarmScale.Money);
            Assume.That(driver.AlarmWeight(AlarmScale.Money), Is.EqualTo(1f).Within(1e-3f),
                "предусловие: тревога зажжена на полный вес");
            driver.DebugPumpAlarms(FadeProbeDt);
            yield return null;

            Assert.Greater(driver.AlarmWeight(AlarmScale.Money), 0f,
                "под ЧУЖИМ окном тревога денег НЕ вычищается — она живая и лишь затухает штатно; "
                + "подавляется только объясняемая шкала (r4 п.1а)");

            Object.Destroy(go);
            yield return null;
        }

        // =====================================================================================
        // п.3 — ЖИВОЙ ПУТЬ ВВОДА НА НАСТОЯЩИХ КАДРАХ (гипотеза «б» из дока инкремента)
        // =====================================================================================

        /// <summary>
        /// ГИПОТЕЗА «б» ЖАЛОБЫ «джойстиком двигаю — шкала не растёт»: путь «зажатая стрелка →
        /// RelationRight КАЖДЫЙ КАДР → латч _relAxis → интеграция» где-то рвётся.
        ///
        /// Проверяется ДВА свойства латча, которых не было ни в одном прежнем тесте:
        ///  (1) переиздание оси КАЖДЫЙ КАДР на живых кадрах драйвера двигает маркер вверх;
        ///  (2) ось — ЛАТЧ, а не счётчик: несколько RelationRight в ОДНОМ кадре дают ровно один шаг, а не
        ///      кратный (иначе частота опроса железа превращалась бы в скорость балансира).
        ///
        /// ⚠ ВРЕМЯ ДВИГАЕТСЯ ЯВНО (`DebugTick`), А НЕ `Time.deltaTime`, И ЭТО НЕ ПОДЛОГ. В batchmode
        /// кадры крутятся без вертикальной синхронизации, deltaTime там доли миллисекунды: сто «живых»
        /// кадров дают околонулевое игровое время, и замер выродился бы в ноль независимо от кода —
        /// тест врал бы в обе стороны. Под тестом здесь ЦЕПОЧКА ВВОДА (source.Received → GameDriver →
        /// Game), и она настоящая: события идут через реальный источник, а не через Game.HandleInput.
        /// Кадры при этом всё равно прокручиваются (`yield return null`), так что порядок Update
        /// драйвера и потеря оси между кадрами были бы видны.
        /// </summary>
        [UnityTest]
        public IEnumerator HeldArrow_MovesTheMarker_AndTheAxisIsALatch_NotACounter()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(new Game(new List<Card> { Starter(), Plain("A", 22) },
                                             coin: () => false));
            fake.Confirm();
            fake.No();

            int guard = 0;
            while (!driver.Game.RelationshipsOpen && driver.Game.State == GameState.Playing && guard++ < 400)
            {
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.Game.Tick(0.2f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;
            Assume.That(driver.Game.RelationshipsOpen, Is.True, "предусловие: балансир открыт");
            Assume.That(driver.Game.Paused, Is.False, "предусловие: пауза снята");

            // Сначала увести маркер вниз, чтобы у тяги вверх был ход (и потолок 100 не мешал замеру).
            for (int i = 0; i < 40; i++)
            {
                fake.Fire(GameInput.RelationLeft);
                driver.DebugTick(0.05f);
                yield return null;
            }
            int low = driver.Game.Scales.Relationships;
            Assume.That(low, Is.LessThan(55), "предусловие: маркер опущен, есть куда тянуть");

            // (1) ЗАЖАТАЯ СТРЕЛКА ВВЕРХ — ось переиздаётся каждый кадр, ровно как у ArcadeInputSource.
            for (int i = 0; i < 40; i++)
            {
                fake.Fire(GameInput.RelationRight);
                driver.DebugTick(0.05f);
                yield return null;
            }
            int held = driver.Game.Scales.Relationships;
            Assert.Greater(held, low,
                "зажатая стрелка ↑ через РЕАЛЬНЫЙ источник ввода поднимает маркер (путь ввода цел)");

            // (2) ЛАТЧ, А НЕ СЧЁТЧИК: та же длительность, но ось переиздаётся ПЯТЬ раз за кадр.
            // Если бы ось накапливалась, шаг вышел бы кратным — и скорость балансира зависела бы от
            // частоты опроса железа (на кабинете с serial-бэкендом это был бы плавающий баланс).
            int before = driver.Game.Scales.Relationships;
            for (int i = 0; i < 20; i++)
            {
                for (int k = 0; k < 5; k++) fake.Fire(GameInput.RelationRight);
                driver.DebugTick(0.05f);
                yield return null;
            }
            int multi = driver.Game.Scales.Relationships - before;

            int singleBefore = driver.Game.Scales.Relationships;
            for (int i = 0; i < 20; i++)
            {
                fake.Fire(GameInput.RelationRight);
                driver.DebugTick(0.05f);
                yield return null;
            }
            int single = driver.Game.Scales.Relationships - singleBefore;

            Assert.AreEqual(single, multi, 1,
                $"пять RelationRight в кадре дают тот же шаг, что один ({multi} против {single}) — "
                + "ось это ЛАТЧ направления, а не счётчик нажатий");

            Object.Destroy(go);
            yield return null;
        }
    
        // =====================================================================================
        // п.4 — ПИКСЕЛЬНЫЕ ГАРДЫ ПЛАШКИ БЛИЦА (переделка по дизайн-гейту 2026-08-08)
        //
        // Гейт завернул ПЕРВУЮ редакцию плашки с тремя замерами, и каждый из них лечится своей
        // правкой — значит и стеречь их надо порознь, числами, а не «выглядит как пак»:
        //   MAJOR-1 радиус углов 3.8–8.3 % высоты против канонных 16–19 % (9-slice тащил радиус
        //           ПОЛОСЫ `bar-track` в её исходных пикселях);
        //   MAJOR-2 кэп-хайт подписи 18 % высоты против 63 % у канонного «ДА», тонкий гротеск и
        //           РАЗМЫТАЯ (полупрозрачная) тень вместо жёсткой;
        //   MINOR-3 кант и жёлтая полоса пропорционально вдвое тяжелее канона;
        //   MINOR-4 два разных тёмных: контур букв #141A3D против канта плашки #0B0F1A.
        // =====================================================================================

        /// <summary>
        /// MAJOR-1 + MINOR-3: ФОРМА плашки читается с её СОБСТВЕННОГО растра, а не с описания кода.
        /// Радиус угла и толщины колец меряются по пикселям сгенерированного спрайта и сверяются с
        /// канон-долями `btn-yes.png`. Отдельно проверяется, что спрайт доезжает до экрана 1:1
        /// (Simple + размер текстуры = размер прямоугольника): 9-slice растянул бы поле, оставив углы
        /// в исходных пикселях, — ровно та ошибка, которую завернул гейт.
        ///
        /// MUTATION-PROOF: верните блицу 9-slice `bar-track` (или сдвиньте BlitzPlateRadiusFrac к 0.05,
        /// что и даёт прежние ~5.7 % H) — тест краснеет на первом же ассерте.
        /// </summary>
        [UnityTest]
        public IEnumerator BlitzPlate_CarriesTheArtPacksCornerRadius_AndProportionalBorders()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            driver.DebugPreviewBlitzPlates(punch: false);
            yield return null;

            foreach (var plate in new[] { driver.YesPlateImage, driver.NoPlateImage })
            {
                string who = plate.name;
                var rect = plate.rectTransform.rect;
                Assert.AreEqual(Image.Type.Simple, plate.type,
                    who + ": плашка блица рисуется ОДНИМ спрайтом под свой размер. 9-slice здесь "
                    + "запрещён — он растягивает поле, но оставляет углы в пикселях ИСХОДНИКА");
                var sp = plate.sprite;
                Assert.AreEqual(rect.height, sp.rect.height, 1f,
                    who + ": высота растра = высоте плашки (иначе доли высоты едут на экран искажёнными)");
                Assert.AreEqual(rect.width, sp.rect.width, 1f, who + ": ширина растра = ширине плашки");

                int tw = (int)sp.rect.width, th = (int)sp.rect.height;
                var px = sp.texture.GetPixels32();
                Color32 At(int x, int y) => px[y * sp.texture.width + x];

                // (1) РАДИУС УГЛА — метод наименьших квадратов по левому краю верхних строк растра.
                var inset = new float[Mathf.RoundToInt(0.32f * th)];
                for (int dy = 0; dy < inset.Length; dy++)
                {
                    int y = th - 1 - dy, first = tw;
                    for (int x = 0; x < tw; x++) if (At(x, y).a > 128) { first = x; break; }
                    inset[dy] = first;
                }
                float bestR = 0f, bestErr = float.MaxValue;
                for (float r = 2f; r < 0.45f * th; r += 0.25f)
                {
                    float err = 0f;
                    for (int dy = 0; dy < inset.Length; dy++)
                    {
                        float pred = dy < r ? r - Mathf.Sqrt(Mathf.Max(r * r - (r - dy) * (r - dy), 0f)) : 0f;
                        err += (pred - inset[dy]) * (pred - inset[dy]);
                    }
                    if (err < bestErr) { bestErr = err; bestR = r; }
                }
                float radiusFrac = bestR / th;
                Assert.That(radiusFrac, Is.InRange(0.14f, 0.20f),
                    who + $": радиус угла {bestR:F1} px = {radiusFrac:P1} высоты. Канон `btn-yes` — "
                    + "16–19 % H; 9-slice `bar-track` давал 5.7 % (почти прямоугольник), и именно это "
                    + "дизайн-гейт назвал MAJOR-1");

                // (2) КОЛЬЦА — вертикальный разрез по середине растра, сверху вниз.
                int xm = tw / 2;
                bool IsInk(Color32 c) => c.a > 200 && c.r < 60 && c.g < 60 && c.b < 60;
                bool IsYellow(Color32 c) => c.a > 200 && c.r > 200 && c.g > 150 && c.b < 90;
                int kant = 0;
                while (kant < th && IsInk(At(xm, th - 1 - kant))) kant++;
                int yStart = -1, yLen = 0;
                for (int dy = 0; dy < th / 2; dy++)
                {
                    if (!IsYellow(At(xm, th - 1 - dy))) { if (yStart >= 0) break; continue; }
                    if (yStart < 0) yStart = dy;
                    yLen++;
                }
                Assert.That(kant / (float)th, Is.InRange(0.016f, 0.031f),
                    who + $": чёрный кант {kant} px = {kant / (float)th:P1} высоты (канон 2.4 %). "
                    + "9-slice оставлял бордюр в пикселях исходника — 3.3–3.6 %, MINOR-3");
                Assert.Greater(yStart, 0, who + ": жёлтая полоса пака на плашке ЕСТЬ");
                Assert.That(yStart / (float)th, Is.InRange(0.042f, 0.075f),
                    who + $": жёлтая полоса начинается на {yStart / (float)th:P1} высоты (канон 5.9 %; "
                    + "у завёрнутой редакции было 9–10 %)");
                Assert.That(yLen / (float)th, Is.InRange(0.015f, 0.033f),
                    who + $": толщина жёлтой полосы {yLen / (float)th:P1} высоты (канон ≈2.5 %)");
            }

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// MAJOR-2 + MINOR-4: ПОДПИСЬ читается С КАДРА — кэп-хайт, жёсткость тени и единый тёмный.
        /// Меряем не «свойства компонентов», а РАСТР: сколько высоты плашки занимает белая буква, есть
        /// ли на поле полупрозрачная тень и не завелось ли рядом с кантом плашки второе тёмное.
        ///
        /// ⚠ ЗАЧЕМ ПРОЕКЦИЯ, А НЕ ЭКРАННЫЕ ПРЯМОУГОЛЬНИКИ: плашки НАКЛОНЕНЫ (канон §9, +13.15°/−9.05°),
        /// и «высота буквы в пикселях экрана» у наклонённой плашки — не её кэп-хайт. Каждый пиксель
        /// переводится в СОБСТВЕННЫЕ координаты плашки её же трансформом, поэтому замер честный при
        /// любом наклоне и не зависит от того, как кадр лёг на экран.
        ///
        /// MUTATION-PROOF (проверено прогоном, см. чекпоинт-лог):
        ///   • вернуть best-fit 28…48 pt / отменить сжатие ⇒ падает кэп-хайт;
        ///   • включить мягкую тень DisplayFx вместо копии-меша ⇒ падает жёсткость тени;
        ///   • вернуть канту букв #141A3D ⇒ падает единый тёмный.
        /// </summary>
        [UnityTest]
        public IEnumerator BlitzLabel_IsBigDoodleCaps_WithAHardShadow_AndOneDarkToken()
        {
            const int W = 1920, H = 1080;
            var driver = Boot(out var go, out _);
            yield return null;

            var canvas = driver.CanvasRect.GetComponent<Canvas>();
            var camGo = new GameObject("BlitzShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.14f, 1f);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 100f;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            yield return null;

            driver.DebugPreviewBlitzPlates(punch: false);
            yield return null;
            // Кегль подписи резолвится best-fit'ом ПОД РАЗМЕР ХОЛСТА: без этого глифы остались бы
            // растеризованы под батч-вьюпорт и замер кэп-хайта врал бы (урок харнесса кадров).
            foreach (var t in driver.GetComponentsInChildren<Text>(true)) { t.FontTextureChanged(); t.SetAllDirty(); }
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreEqual(W, driver.CanvasRect.rect.width, 1f, "кадр снимается ровно в 1920 референс-px");

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var frame = tex.GetPixels32();

            var canvasRt = driver.CanvasRect;
            foreach (var plate in new[] { driver.YesPlateImage, driver.NoPlateImage })
            {
                var prt = plate.rectTransform;
                string label = (plate == driver.YesPlateImage ? driver.YesPlateText : driver.NoPlateText).text;
                int lines = label.Split('\n').Length;
                float pw = prt.rect.width, ph = prt.rect.height;

                // Карта «координаты плашки → пиксель кадра». Идём ОТ ПЛАШКИ: у наклонённой плашки
                // обратный обход оставил бы в её растре дыры от поворота, а связные компоненты глифов
                // на дырявом растре рассыпались бы — и замер w/h глифа стал бы шумом.
                Vector2 ToScreen(Vector2 local)
                {
                    var c = canvasRt.InverseTransformPoint(prt.TransformPoint(new Vector3(local.x, local.y, 0f)));
                    return new Vector2(c.x + W * 0.5f, c.y + H * 0.5f);
                }

                // Эталон поля берётся С КАДРА, из заведомо пустого места внутри жёлтой полосы:
                // цвет плашки запечён в растр, спрашивать его у Image бессмысленно.
                var probe = ToScreen(new Vector2(0.40f * pw, 0.40f * ph));
                // ⚠ ReadPixels отдаёт строки СНИЗУ ВВЕРХ, и координата холста тоже растёт вверх —
                // значит индекс строки берётся БЕЗ переворота (иначе замер уходит в зеркальное пустое
                // место кадра и молча «не находит» подпись).
                var body = frame[Mathf.Clamp((int)probe.y, 0, H - 1) * W + Mathf.Clamp((int)probe.x, 0, W - 1)];
                Vector3 bodyV = new Vector3(body.r, body.g, body.b);
                float bodySq = Vector3.Dot(bodyV, bodyV);
                Assert.Greater(bodySq, 2000f, plate.name + ": проба поля плашки попала в цвет, а не в чёрное");

                // Растр плашки в ЕЁ координатах: белое (буква), тёмное (кант буквы + жёсткая тень).
                int LW = Mathf.CeilToInt(pw), LH = Mathf.CeilToInt(ph);
                var white = new bool[LW * LH];
                var dark = new bool[LW * LH];
                int inkPx = 0, halfTonePx = 0, darkPx = 0;
                var darkBlue = new int[256];
                // Интерьер = всё, что ВНУТРИ жёлтой полосы: её внутренняя кромка на (0.059+0.025)·H.
                float stripeIn = (GameDriver.BlitzPlateStripeOutFrac + GameDriver.BlitzPlateStripeFrac) * ph;
                float kantPx = GameDriver.BlitzPlateKeylineFrac * ph;
                for (int ly = 0; ly < LH; ly++)
                {
                    for (int lx = 0; lx < LW; lx++)
                    {
                        float localX = lx + 0.5f - pw / 2f, localY = ly + 0.5f - ph / 2f;
                        // Кромка ранта отрезана вместе с её сглаживанием (+1 px), иначе в «чернила»
                        // попала бы сама жёлтая полоса и зазор мерился бы до самого себя.
                        if (Mathf.Abs(localX) > pw / 2f - stripeIn - 1f) continue;
                        if (Mathf.Abs(localY) > ph / 2f - stripeIn - 1f) continue;
                        var s = ToScreen(new Vector2(localX, localY));
                        int sx = Mathf.RoundToInt(s.x), sy = Mathf.RoundToInt(s.y);
                        if (sx < 0 || sx >= W || sy < 0 || sy >= H) continue;
                        var p = frame[sy * W + sx];
                        int i = ly * LW + lx;
                        if (p.r > 240 && p.g > 240 && p.b > 240) { white[i] = true; continue; }
                        // «Затемнённое поле»: цвет лежит на луче body·k. Жёсткая тень даёт только
                        // однопиксельную кромку, полупрозрачная (альфа 0.32) — СПЛОШНОЕ пятно k≈0.68.
                        var v = new Vector3(p.r, p.g, p.b);
                        float k = Vector3.Dot(v, bodyV) / bodySq;
                        float residual = (v - bodyV * k).magnitude;
                        if (residual < 28f)
                        {
                            if (k < 0.22f) { inkPx++; dark[i] = true; }
                            else if (k < 0.85f) halfTonePx++;
                        }
                        if (p.r < 80 && p.g < 80 && p.b < 110) { darkBlue[p.b]++; darkPx++; }
                    }
                }

                // (1) КЭП-ХАЙТ: полосы белых строк в координатах плашки.
                var whiteRows = new int[LH];
                for (int ly = 0; ly < LH; ly++)
                    for (int lx = 0; lx < LW; lx++)
                        if (white[ly * LW + lx]) whiteRows[ly]++;
                var bands = new List<int>();
                int run = 0;
                for (int i = 0; i < LH; i++)
                {
                    if (whiteRows[i] > 4) run++;
                    else { if (run > 20) bands.Add(run); run = 0; }
                }
                if (run > 20) bands.Add(run);
                Assert.AreEqual(lines, bands.Count,
                    plate.name + $": «{label.Replace("\n", " ")}» рисуется {lines} строкой/строками. "
                    + "Лишняя полоса = best-fit сам перенёс слово (так «О НЕТ» уезжал на две строки)");
                // ⚠ ПОЛ КЭП-ХАЙТА ПЕРЕСПЕЧЕН ВНИЗ ВТОРЫМ ДИЗАЙН-ГЕЙТОМ — и это не послабление, а
                // исправленный ОБМЕН. Прежние 40 % / 30 % H были ВЗЯТЫ В ДОЛГ: кегль гнали вверх сжатием
                // глифа по X (w/h 0.56 вместо канонных 0.82–0.89) и съеданием воздуха до ранта (0.72 % H
                // вместо канонных 6.8–9.2 %). Гейт назвал это BLOCKER'ом и MAJOR'ом: буква въезжала в кант
                // и была чужой формы. Долг возвращён, и кэп-хайт честно осел: «НОРМАЛЬНО» — ДЕВЯТЬ знаков
                // широкого лица, при канонном воздухе шире они не встают.
                // Числа сняты с ЭТОГО кадра: 1 строка («О НЕТ») 42.5 % H, 2 строки («ВСЁ НОРМАЛЬНО»)
                // 19.3–20.0 % H на строку. Полы поставлены на ≈15 % ниже факта — стеречь падение, а не
                // дрожание растеризатора.
                float floor = lines == 1 ? 0.35f : 0.17f;
                foreach (var b in bands)
                    Assert.GreaterOrEqual(b / ph, floor,
                        plate.name + $": кэп-хайт строки {b} px = {b / ph:P1} высоты плашки, а надо ≥{floor:P0}. "
                        + "Завёрнутая ПЕРВАЯ редакция давала 12 % — подпись не читалась с дистанции (MAJOR-2)");

                // (2) ЖЁСТКАЯ ТЕНЬ: полупрозрачного пятна на поле нет.
                Assert.Greater(inkPx, 500, plate.name + ": тёмная подпись на поле вообще есть");
                // Порог 0.30 выбран ЗАМЕРОМ, а не на глаз: жёсткая тень даёт 0.107 (только кромка
                // сглаживания), прежняя мягкая (альфа 0.32 вместо копии-меша) — 0.585. Запас в обе
                // стороны ≈3×. Честная граница гарда: если мягкую тень ДОБАВИТЬ ПОВЕРХ жёсткой, она
                // почти целиком скрыта копией (0.117) и тест этого не заметит — он стережёт, что
                // ОСНОВНАЯ тень жёсткая, а не отсутствие компонента Shadow как такового.
                Assert.Less(halfTonePx / (float)inkPx, 0.30f,
                    plate.name + $": полутоновых пикселей {halfTonePx} на {inkPx} тёмных "
                    + $"({halfTonePx / (float)inkPx:P1}) — это размытая "
                    + "полупрозрачная тень. Тень пака ЖЁСТКАЯ: отдельная копия текста цветом Ink, и "
                    + "полутон остаётся только однопиксельной кромкой сглаживания (MAJOR-2)");

                // (3) ЕДИНЫЙ ТЁМНЫЙ (MINOR-4). Кант плашки отрезан отступом, значит ВСЁ тёмное на поле —
                //     это кант и тень подписи. Меряем МЕДИАННУЮ синеву тёмных пикселей, а не «есть ли
                //     хоть один тёмно-синий»: кромка сглаживания между чёрным кантом и белой буквой
                //     сама по себе проходит через синеватые значения, и счётчик-порог ловил бы её.
                //     Медиана же показывает, каким тёмным ЗАЛИТА масса: Ink #0B0F1A даёт B≈26,
                //     прежний контур #141A3D — B≈61.
                Assert.Greater(darkPx, 500, plate.name + ": тёмных пикселей подписи набралось на замер");
                int half = darkPx / 2, acc = 0, medianBlue = 0;
                for (int b = 0; b < 256; b++) { acc += darkBlue[b]; if (acc >= half) { medianBlue = b; break; } }
                Assert.LessOrEqual(medianBlue, 40,
                    plate.name + $": медианная синева тёмного на поле B={medianBlue}. Кант буквы обязан "
                    + "быть ТЕМ ЖЕ тёмным, что кант плашки (Ink #0B0F1A, B=26); прежний #141A3D давал "
                    + "B=61 и читался рядом как второй чёрный (MINOR-4)");

                // (4) ВОЗДУХ ink→РАНТ (BLOCKER второго гейта). Меряем ЧЕРНИЛА — букву ВМЕСТЕ с её кантом
                //     и тенью, потому что въезжает в жёлтую полосу именно кант, а не «геометрия текста».
                int bx0 = LW, bx1 = -1, by0 = LH, by1 = -1;
                for (int ly = 0; ly < LH; ly++)
                    for (int lx = 0; lx < LW; lx++)
                    {
                        if (!white[ly * LW + lx] && !dark[ly * LW + lx]) continue;
                        if (lx < bx0) bx0 = lx; if (lx > bx1) bx1 = lx;
                        if (ly < by0) by0 = ly; if (ly > by1) by1 = ly;
                    }
                Assert.Greater(bx1, bx0, plate.name + ": чернила подписи найдены");
                float interiorHalfX = pw / 2f - stripeIn, interiorHalfY = ph / 2f - stripeIn;
                float gapL = (bx0 + 0.5f - pw / 2f) + interiorHalfX;
                float gapR = interiorHalfX - (bx1 + 0.5f - pw / 2f);
                float gapB = (by0 + 0.5f - ph / 2f) + interiorHalfY;
                float gapT = interiorHalfY - (by1 + 0.5f - ph / 2f);
                float gapMin = Mathf.Min(Mathf.Min(gapL, gapR), Mathf.Min(gapB, gapT));
                Assert.GreaterOrEqual(gapMin / ph, 0.05f,
                    plate.name + $": минимальный зазор ink→рант {gapMin:F1} px = {gapMin / ph:P1} высоты "
                    + $"(L{gapL:F0} R{gapR:F0} B{gapB:F0} T{gapT:F0}). Канонное поле пака 6.8–9.2 % H; "
                    + "завёрнутая редакция давала 2.0 px = 0.72 % — «НОРМАЛЬНО» въезжала в жёлтый кант, "
                    + "и это был BLOCKER дизайн-гейта. Пол 5 % — канон минус запас на растеризатор");

                // (5) ЗАПОЛНЕНИЕ ИНТЕРЬЕРА — ОДИН КОРИДОР НА ОБЕ ПЛАШКИ (MINOR «оптически несогласованы»).
                //     Интерьер = цветное поле внутри ЧЁРНОГО КАНТА. Было 62 % у красной против 95 % у
                //     зелёной: красную не связывала ширина (короткое слово брало потолок кегля), зелёную
                //     связывала. Теперь обе связаны одним прямоугольником и садятся в 79.8 / 83.2 %.
                float fill = (bx1 - bx0 + 1) / (pw - 2f * kantPx);
                Assert.That(fill, Is.InRange(0.78f, 0.90f),
                    plate.name + $": подпись занимает {fill:P1} ширины интерьера. Канон пака 78–90 %; "
                    + "разъезд 62 % против 95 % и есть та самая оптическая несогласованность плашек");

                // (6) ФОРМА ГЛИФА (MAJOR второго гейта). Сжатие по X 0.66 делало буквы ЧУЖИМИ: w/h 0.56
                //     против канонных 0.82–0.89, и заодно утончало вертикальные штоки — то есть резало
                //     ровно ту жирность, ради которой брался Rubik-Bold. Меряем связные компоненты белого.
                var seen = new bool[LW * LH];
                var stack = new Stack<int>();
                var ratios = new List<float>();
                float capMax = 0f;
                foreach (var b in bands) capMax = Mathf.Max(capMax, b);
                for (int i0 = 0; i0 < white.Length; i0++)
                {
                    if (!white[i0] || seen[i0]) continue;
                    stack.Push(i0); seen[i0] = true;
                    int gx0 = LW, gx1 = -1, gy0 = LH, gy1 = -1, n = 0;
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        int x = i % LW, y = i / LW;
                        n++;
                        if (x < gx0) gx0 = x; if (x > gx1) gx1 = x;
                        if (y < gy0) gy0 = y; if (y > gy1) gy1 = y;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || nx >= LW || ny < 0 || ny >= LH) continue;
                                int j = ny * LW + nx;
                                if (white[j] && !seen[j]) { seen[j] = true; stack.Push(j); }
                            }
                    }
                    int gw = gx1 - gx0 + 1, gh = gy1 - gy0 + 1;
                    // Только ПОЛНОРОСЛЫЕ знаки: точки «Ё» и обрывки сглаживания формой глифа не являются.
                    if (n < 200 || gh < 0.6f * capMax) continue;
                    ratios.Add(gw / (float)gh);
                }
                Assert.GreaterOrEqual(ratios.Count, 3, plate.name + ": глифов набралось на замер формы");
                ratios.Sort();
                float wh = ratios[ratios.Count / 2];
                Assert.GreaterOrEqual(wh, 0.75f,
                    plate.name + $": медианное w/h глифа {wh:F3} (n={ratios.Count}). Канон дудл-капсов "
                    + "пака 0.82–0.89; сжатие 0.66 давало 0.56 — буквы читались как ЧУЖОЙ шрифт, и это "
                    + "MAJOR дизайн-гейта. Пол 0.75 — канон минус запас на растеризатор");
            }

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(tex);
            Object.Destroy(rt);
            Object.Destroy(camGo);
            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// УТЕЧКА СГЕНЕРЁННЫХ ПЛАШЕК (находка код-скептика r4). <c>BuildBlitzPlateSprite</c> создаёт на
        /// каждый драйвер ДВА <c>Texture2D</c> 615×280 RGBA32 и ДВА <c>Sprite</c>. Это НАТИВНЫЕ объекты:
        /// сборщик мусора C# их не забирает, и без явного Destroy они доживают до выгрузки домена. В
        /// PlayMode-прогоне драйвер поднимается и рушится десятки раз — каждый оставлял бы ≈0.7 МБ.
        ///
        /// Меряем не «есть ли вызов Destroy», а ФАКТ: после уничтожения драйвера и спрайт, и его текстура
        /// отвечают на <c>== null</c> перегрузкой Unity, то есть действительно уничтожены.
        /// MUTATION-PROOF: убрать <c>DestroyBlitzPlateSprites()</c> из <c>OnDestroy</c> — тест краснеет.
        ///
        /// Заодно закрыт вопрос «не плодятся ли они по одному на кадр панча»: панч двигает только
        /// <c>localScale</c>, поэтому после серии поз спрайт остаётся ТЕМ ЖЕ объектом.
        /// </summary>
        [UnityTest]
        public IEnumerator BlitzPlateSprites_AreDestroyedWithTheDriver_AndNotRegeneratedPerFrame()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            driver.DebugPreviewBlitzPlates(punch: false);
            yield return null;

            var sprite = driver.YesPlateImage.sprite;
            var texture = sprite.texture;
            Assert.IsNotNull(sprite, "плашка блица нарисована сгенерированным спрайтом");
            Assert.IsNotNull(texture, "…у него есть собственная текстура");

            // Перегенерации в рантайме быть не должно: панч — это масштаб, а не новый растр.
            for (int i = 0; i < 3; i++)
            {
                driver.DebugPreviewBlitzPlates(punch: i % 2 == 0);
                yield return null;
                Assert.AreSame(sprite, driver.YesPlateImage.sprite,
                    "панч не перерисовывает плашку — иначе каждый кадр дуги плодил бы текстуру");
            }

            Object.Destroy(go);
            yield return null;
            yield return null;

            Assert.IsTrue(sprite == null,
                "СГЕНЕРЁННЫЙ спрайт плашки обязан уничтожаться вместе с драйвером — он нативный, и "
                + "сборщик мусора C# его не заберёт (r4, находка код-скептика)");
            Assert.IsTrue(texture == null,
                "…и его ТЕКСТУРА отдельно: Sprite.Create текстуру себе не присваивает, уничтожение "
                + "спрайта её не трогает — 615×280 RGBA32 остались бы висеть до выгрузки домена");
        }

        /// <summary>
        /// «ПО ЖЕЛАНИЮ» из вердикта гейта: панч — SQUASH, а не равномерная просадка. Плашка в нижней
        /// точке дуги РАСПИРАЕТСЯ по ширине и жмётся по высоте (дудл-резина), а не просто уезжает от
        /// камеры. Замер берётся с той же формулы, которой живёт корутина, — через отладочную позу.
        /// MUTATION-PROOF: верните равномерное 0.86/0.86 — оба ассерта краснеют.
        /// </summary>
        [UnityTest]
        public IEnumerator PlatePunch_SquashesThePlate_RatherThanShrinkingIt()
        {
            var driver = Boot(out var go, out _);
            yield return null;
            driver.DebugPreviewBlitzPlates(punch: true);
            yield return null;

            var s = driver.YesPlateImage.rectTransform.localScale;
            Assert.Greater(s.x, 1.02f, $"в нижней точке панча плашка ШИРЕ обычного (замер {s.x:F3})");
            Assert.Less(s.y, 0.92f, $"…и НИЖЕ обычного (замер {s.y:F3})");
            Assert.Greater(s.x / s.y, 1.10f,
                $"squash — это разница осей, а не масштаб: {s.x:F3}/{s.y:F3}");

            Object.Destroy(go);
            yield return null;
        }
}
}