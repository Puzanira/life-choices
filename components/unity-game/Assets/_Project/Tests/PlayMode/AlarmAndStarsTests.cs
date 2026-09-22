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
    /// §4 КРАСНЫЕ ТРЕВОГИ ШКАЛ и §6 САЛЮТ ЗВЁЗД через НАСТОЯЩИЙ драйвер (meeting-revisions §4/§6,
    /// build-spec §F/§6). Проверяется РЕЗУЛЬТАТ, а не флаг:
    ///  • пороги — параметризованно по всем четырём шкалам: включение ровно на спековой границе,
    ///    выключение только за гистерезисом (дребезг на границе физически недостижим);
    ///  • красное действительно НАРИСОВАНО — и в цвете живого элемента, и в ПИКСЕЛЯХ снятого кадра
    ///    (полость батареи в координатах эталона «Экран подсвечена красным шкала.png»);
    ///  • пульс детерминирован (фаза = время В ТРЕВОГЕ, не Time.time) и замирает на паузе;
    ///  • возврат в норму гаснет ФЕЙДОМ за ~0.2 с, а не скачком;
    ///  • салют стреляет ровно на «игрок вывел шкалу в норму» и НЕ стреляет на смене карточки,
    ///    без ввода и на рестарте; 4–5 звёзд спековых калибров; самоочистка после разлёта;
    ///  • на опенере и финале тревог нет, вейли состояний рисуются ПОВЕРХ подсветки.
    /// </summary>
    public class AlarmAndStarsTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        /// <summary>Опенер → живая карточка, весь взрослый HUD открыт, драйвер ЗАМОРОЖЕН: дальше время
        /// подаётся только явными DebugPumpAlarms, без вклада Time.deltaTime и без пере-гейтинга по
        /// живому возрасту (Update иначе прячет шкалы обратно — игре 1 год).</summary>
        private static IEnumerator ToFrozenAdult(GameDriver driver, PlayFakeInputSource fake)
        {
            yield return null;                       // Start wires input + subscriptions
            fake.Confirm();                          // opener → playing, first card dealt
            yield return null;
            Assert.IsFalse(driver.Game.Paused, "карточка идёт живьём");
            driver.enabled = false;                  // дальше время подаём вручную
            driver.DebugApplyAgeGates(40f);          // батарея + оба бара + банка на экране
        }

        /// <summary>
        /// То же, что <see cref="ToFrozenAdult"/>, но шкалы открыты НЕ ТОЛЬКО на HUD, а и в самой
        /// <see cref="Game"/>: жизнь реально доезжает до 25 лет (деньги 18, энергия 25), а §D-модалки
        /// снимаются своими контролами.
        /// ⚠ Нужна каждому тесту, который проверяет ПРИНЯТЫЙ ввод: <c>DebugApplyAgeGates</c> рисует
        /// виджеты, но НЕ открывает шкалы в Game, а ЗАКРЫТАЯ шкала свой ввод ОТВЕРГАЕТ. Пока драйвер
        /// отмечал §6-окно, не спрашивая Game, это расхождение было незаметно — и тест «принятый тик»
        /// на деле проверял «нажатый тик».
        /// ⚠ ОТНОШЕНИЙ здесь нет намеренно: балансир открывает не возраст, а СВАДЬБА — то есть ответы,
        /// а колода живая и монетка не сидированная. Ждать его в цикле = ждать до конца жизни.
        /// </summary>
        private static IEnumerator ToFrozenAdultWithLiveScales(GameDriver driver, PlayFakeInputSource fake)
        {
            yield return null;                       // Start wires input + subscriptions
            fake.Confirm();                          // opener → playing
            yield return null;
            var g = driver.Game;
            int guard = 0;
            while (guard++ < 4000 && g.State == GameState.Playing)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                if (g.MoneyOpen && g.EnergyOpen) break;
                if (g.EnergyOpen && g.Scales.Energy < 60) fake.Fire(GameInput.EnergyHold);  // не выгореть по пути
                g.Tick(0.2f);
                if (g.CurrentCard != null && g.CardTimer < 3f) fake.No();   // не отдавать ход таймауту
            }
            Assert.AreEqual(GameState.Playing, g.State, "жизнь дожила до открытых шкал");
            Assert.IsTrue(g.MoneyOpen && g.EnergyOpen, "деньги и энергия открыты В ИГРЕ, а не только на HUD");
            Assert.IsFalse(driver.NewScaleShowing, "…и ни одна §D-модалка не осталась на экране");
            driver.enabled = false;                  // дальше время подаём вручную
            driver.DebugApplyAgeGates(40f);
            FlushStarSky(driver);
        }

        /// <summary>Догасить салюты, которыми §D-туториалы наградили сам проход фикстуры: тесты ниже
        /// считают ТОЛЬКО новые звёзды, а закрытие каждой модалки — это законный бёрст (§6).</summary>
        private static void FlushStarSky(GameDriver driver)
        {
            driver.DebugAdvanceStars(2f);            // максимальный полёт звезды — 0.9 с
            Assert.AreEqual(0, driver.ActiveStarCount, "небо чистое: салюты фикстуры догорели");
        }

        /// <summary>Довести жизнь до РЕАЛЬНО открытых денег (возраст 18) и снять §D-модалку её же
        /// контролом. Без этого <see cref="Game"/> отвергает крутилку (<c>MoneyOpen == false</c>), и
        /// «принятый тик» ниже проверял бы только кэп дохода, а не приём механикой.</summary>
        private static IEnumerator OpenMoneyInGame(GameDriver driver, PlayFakeInputSource fake)
        {
            var g = driver.Game;
            int guard = 0;
            while (guard++ < 2000 && g.State == GameState.Playing)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                if (g.MoneyOpen) break;
                g.Tick(0.1f);
            }
            Assert.IsTrue(g.MoneyOpen, "деньги открыты В ИГРЕ (возраст 18)");
            Assert.IsFalse(driver.NewScaleShowing, "…и §D-модалка денег снята своим контролом");
            FlushStarSky(driver);
        }

        private static void SetScale(GameDriver d, AlarmScale s, int v)
        {
            switch (s)
            {
                case AlarmScale.Energy: d.Game.Scales.Energy = v; break;
                case AlarmScale.Health: d.Game.Scales.Health = v; break;
                case AlarmScale.Relations: d.Game.Scales.Relationships = v; break;
            }
        }

        // ================================================================ пороги + гистерезис

        // Энергия/здоровье: тревога ВКЛЮЧАЕТСЯ строго ниже 20 (спек §4) и снимается только на 22 —
        // на 21 (внутри гистерезиса) она обязана ещё гореть, иначе шкала на границе будет дребезжать.
        [UnityTest]
        public IEnumerator Alarm_EnergyAndHealth_TurnOnBelow20_AndOffOnlyPastTheHysteresis(
            [Values(AlarmScale.Energy, AlarmScale.Health)] AlarmScale scale)
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);

            SetScale(driver, scale, 21);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(scale), "21 % — ещё норма, тревоги нет");

            SetScale(driver, scale, 20);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(scale), "ровно 20 % — порог «ниже 20», ещё не тревога");

            SetScale(driver, scale, 19);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(scale), "19 % — тревога включилась на спековой границе");

            SetScale(driver, scale, 21);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(scale),
                "21 % — ВНУТРИ гистерезиса: тревога держится, значит на границе дребезга нет");

            SetScale(driver, scale, 22);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(scale), "22 % — вышли за гистерезис, тревога снята");

            Object.Destroy(go);
            yield return null;
        }

        // Отношения: тревога вне МЕХАНИЧЕСКОЙ зоны 40–75 (Game.RelZoneMin/Max — канон, не трогается),
        // с тем же гистерезисом внутрь зоны с обеих сторон.
        [UnityTest]
        public IEnumerator Alarm_Relations_TracksTheMechanicalZone_WithHysteresisOnBothSides()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);
            const AlarmScale rel = AlarmScale.Relations;

            Assert.AreEqual(40, Game.RelZoneMin, "зона отношений — механический канон, тревога только её отражает");
            Assert.AreEqual(75, Game.RelZoneMax);

            SetScale(driver, rel, 55);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(rel), "середина зоны — тревоги нет");

            SetScale(driver, rel, 39);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel), "ниже 40 — тревога");
            SetScale(driver, rel, 41);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel), "41 — внутри гистерезиса, тревога держится");
            SetScale(driver, rel, 42);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(rel), "42 — вышли за гистерезис снизу");

            SetScale(driver, rel, 76);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel), "выше 75 — тревога («задушил вниманием»)");
            SetScale(driver, rel, 74);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel), "74 — внутри гистерезиса сверху, тревога держится");
            SetScale(driver, rel, 73);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(rel), "73 — вышли за гистерезис сверху");

            Object.Destroy(go);
            yield return null;
        }

        // ---- деньги: тревога РОВНО по существующему сигналу блокировки BLOCK$-карточки --------------

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        /// <summary>Колода, гарантированно приводящая к настоящей BLOCK$-карточке MD03 (цена 60₽).</summary>
        private static Game BlockDeck()
        {
            // ⚠ «GRACE» — ЖЕРТВЕННАЯ КАРТОЧКА ПОД ЛЬГОТУ r4 п.1б. Первая карточка, выданная ПОСЛЕ
            // закрытия §D-окна денег, гарантированно по карману (иначе обучение заканчивалось запертой
            // дверью — жалоба основательницы). Без этого филлера льгота съедала бы ровно ту MD03,
            // которую тесты ниже и хотят увидеть заблокированной, и они проверяли бы пустоту.
            var deck = new List<Card>
            {
                Starter(), Plain("FILL", 18), Plain("GRACE", 20), Plain("MD03", 30), Plain("NORMAL", 40),
            };
            deck[3].IsBlockCost = true;
            return new Game(deck, coin: () => false);
        }

        [UnityTest]
        public IEnumerator Alarm_Money_RaisesOnlyWhenABlockCardIsUnaffordable()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(BlockDeck());
            fake.Confirm();                          // opener → starter
            fake.No();                               // starter → FILL
            driver.DebugApplyAgeGates(40f);          // банка на экране
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.Game.CurrentCardBlocked, "обычная карточка — денег хватает по определению");
            Assert.IsFalse(driver.AlarmActive(AlarmScale.Money), "…и тревоги денег нет");

            fake.No();                               // FILL → GRACE
            fake.No();                               // GRACE → MD03, денег 0 < 60 → БЛОКИРОВКА
            Assert.IsTrue(driver.Game.CurrentCardBlocked, "настоящая BLOCK$-карточка при пустом кармане");
            driver.DebugApplyAgeGates(40f);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(AlarmScale.Money), "деньги в тревоге ровно по сигналу блокировки");

            Object.Destroy(go);
            yield return null;
        }

        // ================================================================ красное РЕАЛЬНО нарисовано

        private static bool IsRed(Color c) => c.r > 0.7f && c.g < 0.35f && c.b < 0.35f;

        [UnityTest]
        public IEnumerator Alarm_PaintsRealRed_OnEveryWidget_NotJustAFlag()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);

            // (0) спокойный ход: тревожных слоёв на экране НЕТ вовсе, арт рисуется своими цветами.
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.BatteryAlarmImage.gameObject.activeSelf, "спокойно: красной батареи нет");
            Assert.IsFalse(driver.EnergyCharge.gameObject.activeSelf, "спокойно: тревожной полосы заряда нет");
            Assert.IsFalse(driver.HealthAlarmKant.gameObject.activeSelf, "спокойно: канта здоровья нет");
            Assert.IsFalse(driver.RelAlarmKant.gameObject.activeSelf, "спокойно: канта отношений нет");
            Assert.IsFalse(driver.HealthAlarmKantInk.gameObject.activeSelf, "спокойно: и его обводки нет");
            Assert.IsFalse(driver.RelAlarmKantInk.gameObject.activeSelf, "спокойно: и её обводки нет");
            Assert.AreEqual(Color.white, driver.MoneyJarImage.color, "спокойно: банка своим цветом");
            Assert.AreEqual(GameDriver.BatteryCreamToken, driver.EnergyEmpty.color,
                "спокойно: пустота полости — кремовая, как в спрайте");

            // (1) энергия: красная копия батареи включена, полость перекрашена в палитру ЭТАЛОНА.
            driver.Game.Scales.Energy = 12;
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.BatteryAlarmImage.gameObject.activeSelf, "тревога: красная батарея на экране");
            Assert.AreEqual("energy-battery-alarm-v2", driver.BatteryAlarmImage.sprite.name,
                "красная батарея — офлайн-перекрас ТОЙ ЖЕ картинки (геометрия не прыгает)");
            Assert.AreEqual(1f, driver.BatteryAlarmImage.color.a, 0.01f, "на полном весе она непрозрачна");
            AssertNear(GameDriver.AlarmCavityEmptyToken, driver.EnergyEmpty.color,
                "пустота полости = #FF3736 эталона");
            Assert.IsTrue(driver.EnergyCharge.gameObject.activeSelf, "остаток заряда докрашен");
            AssertNear(GameDriver.AlarmCavityChargeToken, driver.EnergyCharge.color,
                "остаток заряда = #FF0506 эталона");
            // Молния остаётся своим кремовым слоем ПОВЕРХ красного — читаемость (задача §1).
            Assert.AreEqual(Color.white, driver.EnergyBolt.color, "молния не тонируется — читается на красном");
            Assert.Greater(driver.EnergyBolt.transform.GetSiblingIndex(),
                driver.EnergyCharge.transform.GetSiblingIndex(), "…и рисуется ПОВЕРХ тревожной заливки");

            // (2) здоровье и отношения: КАНТ (состояние), а не тонировка плашки (это была бы «позиция»).
            driver.Game.Scales.Health = 12;
            driver.Game.Scales.Relationships = 20;
            driver.DebugPumpAlarms(0.02f);
            foreach (var (kant, ink, bar, what) in new[]
            {
                (driver.HealthAlarmKant, driver.HealthAlarmKantInk, driver.HealthBarImage, "здоровье"),
                (driver.RelAlarmKant, driver.RelAlarmKantInk, driver.RelBarImage, "отношения"),
            })
            {
                Assert.IsTrue(kant.gameObject.activeSelf, what + ": кант тревоги на экране");
                var drawn = new Color(kant.color.r * GameDriver.BarTrackFillToken.r,
                                      kant.color.g * GameDriver.BarTrackFillToken.g,
                                      kant.color.b * GameDriver.BarTrackFillToken.b, kant.color.a);
                Assert.IsTrue(IsRed(drawn), what + ": кант РИСУЕТСЯ красным (замер " + drawn + ")");
                Assert.AreEqual(1f, kant.color.a, 0.01f, what + ": на полном весе кант непрозрачен");
                Assert.AreEqual(Color.white, bar.color,
                    what + ": сама плашка бара НЕ тонируется — тревога читается как состояние, "
                         + "а не как «маркер заехал в нарисованную красную зону»");
                Assert.Less(kant.transform.GetSiblingIndex(), bar.transform.GetSiblingIndex(),
                    what + ": кант лежит ПОЗАДИ бара, т.е. торчит рамкой вокруг него");
                // Кант реально ШИРЕ бара — иначе он был бы невидим под плашкой.
                Assert.Greater(kant.rectTransform.rect.width, bar.rectTransform.rect.width * 0.5f,
                    what + ": кант охватывает бар");

                // Полиш дизайн-гейта: СНАРУЖИ красного — чёрное кольцо (keyline арт-пака), иначе кант
                // единственный на экране упирался бы голым красным в лучи фона.
                Assert.IsTrue(ink.gameObject.activeSelf, what + ": чёрная обводка канта на экране");
                var inkDrawn = new Color(ink.color.r * GameDriver.BarTrackFillToken.r,
                                         ink.color.g * GameDriver.BarTrackFillToken.g,
                                         ink.color.b * GameDriver.BarTrackFillToken.b, ink.color.a);
                AssertNear(GameDriver.InkToken, inkDrawn, what + ": обводка РИСУЕТСЯ токеном INK");
                Assert.AreEqual(1f, ink.color.a, 0.01f, what + ": на полном весе обводка непрозрачна");
                Assert.Less(ink.transform.GetSiblingIndex(), kant.transform.GetSiblingIndex(),
                    what + ": обводка лежит ПОЗАДИ красного — значит видна ровно кольцом снаружи");
                Assert.AreEqual(kant.rectTransform.rect.width + 2f * GameDriver.AlarmKantInk,
                    ink.rectTransform.rect.width, 0.01f, what + ": кольцо шире красного ровно на обводку");
                Assert.AreEqual(kant.rectTransform.rect.height + 2f * GameDriver.AlarmKantInk,
                    ink.rectTransform.rect.height, 0.01f, what + ": …и выше ровно на неё же");
                Assert.AreEqual(kant.rectTransform.anchoredPosition, ink.rectTransform.anchoredPosition,
                    what + ": кольцо СООСНО красному (углы концентричны)");
                Assert.AreEqual(kant.rectTransform.anchorMin, ink.rectTransform.anchorMin,
                    what + ": …и висит на том же якоре");
                Assert.AreEqual(kant.sprite, ink.sprite, what + ": та же плашка → тот же радиус угла");
                Assert.AreEqual(Image.Type.Sliced, ink.type, what + ": обводка тоже 9-slice (угол не тянется)");
                Assert.IsTrue(GameDriver.AlarmKantInk >= 3f && GameDriver.AlarmKantInk <= 4f,
                    "толщина обводки — 3–4 реф-px (дизайн-гейт)");
            }

            Object.Destroy(go);
            yield return null;
        }

        private static void AssertNear(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0.02f, what + " (r)");
            Assert.AreEqual(expected.g, actual.g, 0.02f, what + " (g)");
            Assert.AreEqual(expected.b, actual.b, 0.02f, what + " (b)");
        }

        // ---- то же самое, но СНЯТОЕ С КАДРА: цвет в Image ещё не «видно тревогу» ---------------------
        // Полость батареи берётся ровно в координатах эталона (asset-map §8: 141,88,86,175) — там, где
        // на «Экран подсвечена красным шкала.png» красное и нарисовано.
        [UnityTest]
        public IEnumerator Alarm_FloodsTheBatteryCavityWithRed_ReadOffTheRenderedFrame()
        {
            const int W = 1920, H = 1080;
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            yield return null;

            var canvas = driver.CanvasRect.GetComponent<Canvas>();
            var camGo = new GameObject("AlarmShotCam");
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
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreEqual(W, driver.CanvasRect.rect.width, 1f, "кадр снимается ровно в 1920 референс-px");

            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            Color32[] Shoot()
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex.GetPixels32();
            }

            float RedShare(Color32[] px, int x0, int y0, int x1, int y1)
            {
                int red = 0, total = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        var p = px[(H - 1 - y) * W + x];      // ReadPixels отдаёт строки снизу вверх
                        if (p.r > 180 && p.g < 110 && p.b < 110) red++;
                        total++;
                    }
                return (float)red / total;
            }

            // Полость батареи (asset-map §8: 141,88,86,175), чуть подрезанная от чёрной обводки. Верхняя
            // полоса берётся отдельно: она ВЫШЕ бокса молнии (BoltRect, y 156…236), поэтому там красное
            // обязано быть сплошным, а по всей полости кремовая молния законно съедает ~пятую часть.
            const int CavL = 145, CavT = 92, CavR = 223, CavB = 259;
            const int TopT = 95, TopB = 150;

            driver.DebugPreviewArcadeShot();                  // спокойный кадр
            yield return null;
            var calmPx = Shoot();
            Assert.Less(RedShare(calmPx, CavL, CavT, CavR, CavB), 0.02f,
                "вне тревоги в полости батареи красного нет");
            Assert.Less(RedShare(calmPx, CavL, TopT, CavR, TopB), 0.02f,
                "…в том числе в верхней (кремовой «пустой») полосе");

            driver.DebugPreviewAlarms();                      // все четыре шкалы в тревоге, пульс на пике
            yield return null;
            var alarmPx = Shoot();
            float cavity = RedShare(alarmPx, CavL, CavT, CavR, CavB);
            float top = RedShare(alarmPx, CavL, TopT, CavR, TopB);
            Assert.Greater(cavity, 0.7f,
                $"в тревоге полость батареи залита красным (замер {cavity:P0}; остальное — молния)");
            Assert.Greater(top, 0.95f,
                $"…а чистая от молнии полоса полости красная СПЛОШЬ, как на эталоне (замер {top:P0})");

            // Канты баров: полоса СНАРУЖИ нарисованного бокса бара — там, где спокойный кадр показывает
            // только фон-лучи. Берём тонкую полосу над каждым баром внутри канта.
            float healthKant = RedShare(alarmPx,
                (int)(GameDriver.HealthBarDrawnCx - GameDriver.HealthBarDrawnW / 2f) + 20,
                (int)(GameDriver.HealthBarDrawnCy - GameDriver.HealthBarDrawnH / 2f - GameDriver.AlarmKantPad + 2),
                (int)(GameDriver.HealthBarDrawnCx + GameDriver.HealthBarDrawnW / 2f) - 20,
                (int)(GameDriver.HealthBarDrawnCy - GameDriver.HealthBarDrawnH / 2f - 2));
            Assert.Greater(healthKant, 0.7f,
                $"кант здоровья реально нарисован красным вокруг бара (замер {healthKant:P0})");

            // Полиш дизайн-гейта: СНАРУЖИ красного канта — ЧЁРНОЕ КОЛЬЦО. Читаем сверху вниз по колонке
            // над центром бара (там фон — чистые синие лучи, #4F8CF6): фон → чёрный keyline → красное.
            // Проверяется именно КАДР: цвет в Image ещё не доказывает, что кольцо видно.
            bool Red(Color32 p) => p.r > 180 && p.g < 110 && p.b < 110;
            bool Dark(Color32 p) => p.r < 70 && p.g < 70 && p.b < 70;
            Color32 At(Color32[] px, int x, int y) => px[(H - 1 - y) * W + x];

            foreach (var (colX, barTopY, what) in new[]
            {
                ((int)GameDriver.HealthBarDrawnCx,
                 (int)(GameDriver.HealthBarDrawnCy - GameDriver.HealthBarDrawnH / 2f), "здоровье"),
                ((int)GameDriver.RelBarDrawnCx,
                 (int)(GameDriver.RelBarDrawnCy - GameDriver.RelBarDrawnH / 2f), "отношения"),
            })
            {
                int scanTop = barTopY - (int)(GameDriver.AlarmKantPad + GameDriver.AlarmKantInk) - 6;
                int firstRed = -1;
                for (int y = scanTop; y < barTopY; y++)
                    if (Red(At(alarmPx, colX, y))) { firstRed = y; break; }
                Assert.Greater(firstRed, 0, what + ": красный кант найден над баром");

                // Сплошных строк ждём на две меньше толщины: край `bar-track` мягкий, крайние строки
                // кольца — законный AA (снаружи в фон, внутри в красное).
                int dark = 0;
                for (int y = firstRed - (int)GameDriver.AlarmKantInk; y < firstRed; y++)
                    if (Dark(At(alarmPx, colX, y))) dark++;
                Assert.GreaterOrEqual(dark, (int)GameDriver.AlarmKantInk - 2,
                    what + ": СНАРУЖИ красного лежит чёрное кольцо keyline (замер " + dark + " сплошных "
                         + "строк из " + (int)GameDriver.AlarmKantInk + "; без него красное упиралось бы "
                         + "голым краем в лучи фона)");
                Assert.IsFalse(Dark(At(alarmPx, colX, firstRed - (int)GameDriver.AlarmKantInk - 3)),
                    what + ": и это именно ТОНКОЕ кольцо — на 3 px выше уже фон, а не чёрная плашка");
                // Спокойный кадр этой обводки не несёт вовсе: тревога уходит с экрана целиком.
                Assert.IsFalse(Dark(At(calmPx, colX, firstRed - 1)),
                    what + ": вне тревоги обводки канта на экране нет");
            }

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(tex);
            Object.Destroy(rt);
            Object.Destroy(camGo);
            Object.Destroy(go);
            yield return null;
        }

        // ================================================================ пульс и фейд

        [UnityTest]
        public IEnumerator AlarmPulse_PhaseIsDeterministic_AndFreezesOnPause()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);
            const AlarmScale e = AlarmScale.Energy;

            driver.Game.Scales.Energy = 10;
            driver.DebugPumpAlarms(0f);
            Assert.IsTrue(driver.AlarmActive(e));
            Assert.AreEqual(0f, driver.AlarmClock(e), 1e-4f, "вход в тревогу стартует фазу с нуля (ПИК)");
            Assert.AreEqual(1f, driver.AlarmPulseBrightness(e), 1e-3f, "…и на пике яркость полная");

            driver.DebugPumpAlarms(GameDriver.AlarmPulsePeriod / 2f);
            Assert.AreEqual(GameDriver.AlarmPulseMin, driver.AlarmPulseBrightness(e), 1e-3f,
                "полпериода — ровно ПРОВАЛ пульса (фаза считается от времени В ТРЕВОГЕ, не от Time.time)");

            driver.DebugPumpAlarms(GameDriver.AlarmPulsePeriod / 2f);
            Assert.AreEqual(1f, driver.AlarmPulseBrightness(e), 1e-3f, "полный период — снова пик");

            // Пауза (туториал) — часы тревоги стоят, значит и пульс замирает вместе с игрой.
            driver.DebugPumpAlarms(GameDriver.AlarmPulsePeriod / 4f);
            float frozenClock = driver.AlarmClock(e);
            float frozenBright = driver.AlarmPulseBrightness(e);
            driver.DebugShowTutorial("Тестовая подсказка");
            Assert.IsTrue(driver.Game.Paused, "туториал ставит игру на паузу");
            driver.DebugPumpAlarms(GameDriver.AlarmPulsePeriod);
            Assert.AreEqual(frozenClock, driver.AlarmClock(e), 1e-4f, "на паузе часы тревоги не идут");
            Assert.AreEqual(frozenBright, driver.AlarmPulseBrightness(e), 1e-4f, "…и пульс замер");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Alarm_FadesOut_OverTheSpecTwoTenths_NotInOneJump()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);
            const AlarmScale e = AlarmScale.Energy;

            driver.Game.Scales.Energy = 10;
            driver.DebugPumpAlarms(0.02f);
            Assert.AreEqual(1f, driver.AlarmWeight(e), 1e-3f, "в тревоге подсветка на полном весе");

            driver.Game.Scales.Energy = 60;                       // ушли в норму
            driver.DebugPumpAlarms(GameDriver.AlarmFadeSeconds / 2f);
            Assert.IsFalse(driver.AlarmActive(e), "тревога снята");
            Assert.AreEqual(0.5f, driver.AlarmWeight(e), 0.05f,
                "…но подсветка ГАСНЕТ, а не пропадает: на половине фейда она на половине веса");
            Assert.IsTrue(driver.BatteryAlarmImage.gameObject.activeSelf, "на фейде красная батарея ещё видна");

            driver.DebugPumpAlarms(GameDriver.AlarmFadeSeconds / 2f + 0.01f);
            Assert.AreEqual(0f, driver.AlarmWeight(e), 1e-3f, "за ~0.2 с погасла полностью");
            Assert.IsFalse(driver.BatteryAlarmImage.gameObject.activeSelf, "…и тревожный слой снят с экрана");
            Assert.AreEqual(GameDriver.BatteryCreamToken, driver.EnergyEmpty.color,
                "полость вернулась к своему кремовому — тревога не «залипла»");

            Object.Destroy(go);
            yield return null;
        }

        // ================================================================ опенер/финал/вейлы

        [UnityTest]
        public IEnumerator Alarms_AreAbsent_OnTheOpener_AndOnTheFinale()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            Assert.IsTrue(driver.OpenerPanel.activeSelf, "стартуем на опенере");
            driver.Game.Scales.Energy = 5;
            driver.Game.Scales.Health = 5;
            driver.DebugPumpAlarms(0.05f);
            foreach (AlarmScale s in System.Enum.GetValues(typeof(AlarmScale)))
                Assert.IsFalse(driver.AlarmActive(s), "на опенере тревог нет: " + s);
            Assert.IsFalse(driver.BatteryAlarmImage.gameObject.activeSelf, "…и красной батареи тоже");

            fake.Confirm();
            yield return null;
            driver.enabled = false;
            driver.DebugRenderFinale(Necrolog.Build("спокойная старость", new List<NecrologEntry>()));
            driver.Game.Scales.Energy = 5;
            driver.DebugPumpAlarms(0.05f);
            foreach (AlarmScale s in System.Enum.GetValues(typeof(AlarmScale)))
                Assert.IsFalse(driver.AlarmActive(s), "на финале тревог нет: " + s);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StateVeils_And_TheStarLayer_SitAboveTheAlarmPaint()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);

            int game = driver.GamePanel.transform.GetSiblingIndex();
            Assert.Greater(driver.BrightnessVeil.transform.GetSiblingIndex(), game,
                "вейль яркости шоу рисуется ПОВЕРХ игровой панели (а значит и поверх подсветки шкал)");
            Assert.Greater(driver.DepressionOverlay.transform.GetSiblingIndex(), game,
                "депрессия рисуется поверх подсветки");
            Assert.Greater(driver.TutorialOverlay.transform.GetSiblingIndex(), game,
                "модалка подсказки — поверх");
            Assert.Greater(driver.NewScaleOverlay.transform.GetSiblingIndex(),
                driver.TutorialOverlay.transform.GetSiblingIndex(),
                "§D-экран новой шкалы — над затемнением подсказки (build-spec §D, слой 6)");
            Assert.Greater(driver.StarLayer.transform.GetSiblingIndex(),
                driver.NewScaleOverlay.transform.GetSiblingIndex(),
                "…и под салютом звёзд (слой 7)");
            Assert.Greater(driver.StarLayer.transform.GetSiblingIndex(),
                driver.DepressionOverlay.transform.GetSiblingIndex(),
                "салют — слой 7, поверх всего (build-spec §1.3)");

            Object.Destroy(go);
            yield return null;
        }

        // ================================================================ §6 · салют

        [UnityTest]
        public IEnumerator StarBurst_Fires_WhenThePlayerLiftsAScaleOutOfAlarm()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);
            const AlarmScale rel = AlarmScale.Relations;

            driver.Game.Scales.Relationships = 25;                 // провалились ниже зоны
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel));
            int before = driver.StarBurstCount;

            driver.DebugNoteScaleInput(rel);                       // игрок тянет рычаг отношений
            driver.Game.Scales.Relationships = 55;                 // …и вытянул в зону
            driver.DebugPumpAlarms(0.02f);

            Assert.AreEqual(before + 1, driver.StarBurstCount, "калибровка игроком → разовый бёрст салюта");
            Assert.GreaterOrEqual(driver.ActiveStarCount, GameDriver.StarBurstMin, "звёзд не меньше 4");
            Assert.LessOrEqual(driver.ActiveStarCount, GameDriver.StarBurstMax, "…и не больше 5");

            // Калибры — ровно спековые 0.6/0.8/1.0/1.2× базового кегля.
            var allowed = new[] { 0.6f, 0.8f, 1.0f, 1.2f };
            foreach (var star in driver.StarLayer.GetComponentsInChildren<Image>())
            {
                Assert.AreEqual("star-burst-v2", star.sprite.name, "звезда рисуется спрайтом арт-пака");
                float k = star.rectTransform.rect.width / GameDriver.StarBaseSize;
                Assert.IsTrue(allowed.Any(a => Mathf.Abs(a - k) < 0.01f),
                    "калибр звезды из спекового набора 0.6/0.8/1.0/1.2× (замер " + k + ")");
            }

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StarBurst_DoesNotFire_WithoutRecentPlayerInputOnThatScale()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);
            const AlarmScale rel = AlarmScale.Relations;

            driver.Game.Scales.Relationships = 25;
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel));
            int before = driver.StarBurstCount;

            // Окно ввода протухло (шкалу вытащило что-то другое — например Δ карточки, не рычаг).
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);
            driver.Game.Scales.Relationships = 55;
            driver.DebugPumpAlarms(0.02f);

            Assert.IsFalse(driver.AlarmActive(rel), "тревога снята");
            Assert.AreEqual(before, driver.StarBurstCount, "без свежего ввода по шкале салюта НЕТ");
            Assert.AreEqual(0, driver.ActiveStarCount, "…и звёзд в воздухе нет");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StarBurst_DoesNotFire_WhenTheMoneyAlarmClearsWithANewCard(
            [Values(false, true)] bool cranked)
        {
            // Ключевой разделитель «игрок починил» ↔ «просто сменилась карточка»: тревога денег гаснет
            // на СЛЕДУЮЩЕЙ карточке сама по себе — салюта быть не должно. Второй прогон закрывает
            // ЛОЖНЫЙ САЛЮТ (скептик, MAJOR): игрок крутил ручку на ЗАБЛОКИРОВАННОЙ карточке (тик принят
            // кэпом, окно §6 свежее), карточка сменилась ответом — «починки» не было, тревога снялась
            // сменой карточки. Свежесть ввода тут ничего не решает.
            //
            // ⚠ ПОЧЕМУ КРУТИЛКА ТУТ НЕ ОТПИРАЕТ КАРТОЧКУ — ПРИЧИНА СМЕНИЛАСЬ В r6 п.2. Раньше это было
            // структурно невозможно: CurrentCardBlocked фиксировался на выдаче. Теперь доступность
            // живая и крутилка в принципе может отпереть карточку — но не ЭТА: один принятый тик даёт
            // +1 ₽ (MoneyTickIncome), а MD03 стоит 60 ₽, и до цены отсюда не дотянуться. Разделитель
            // «починил ↔ сменилась карточка» держится на этом, поэтому факт закреплён явной проверкой
            // ниже: если тюнинг дохода когда-нибудь сделает один тик достаточным, тест упадёт здесь,
            // а не молча перестанет проверять то, ради чего написан.
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(BlockDeck());
            fake.Confirm();
            fake.No();                                   // → FILL (18)
            yield return OpenMoneyInGame(driver, fake);  // …возраст доезжает до 18: деньги открыты В ИГРЕ
            fake.No();                                   // → GRACE (льгота r4 п.1б тратится здесь)
            fake.No();                                   // → MD03 (цена 60₽ ≫ туториальных монет: блокировка)
            driver.DebugApplyAgeGates(40f);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.Game.CurrentCardBlocked, "MD03 действительно заблокирована ценой");
            Assert.IsTrue(driver.AlarmActive(AlarmScale.Money));
            int before = driver.StarBurstCount;

            if (cranked)
            {
                driver.DebugAdvanceInputClocks(1f);      // кэп дохода перезаряжен после туториала
                fake.Fire(GameInput.MoneyTick);          // ПРИНЯТЫЙ тик: и кэп пропустил, и Game засчитал
                Assert.Less(driver.SinceScaleInput(AlarmScale.Money), GameDriver.AlarmRecentInputSeconds,
                    "принятый тик крутилки действительно открыл окно §6 по деньгам");
                // r6 п.2: живой пересчёт ВКЛЮЧЁН, и карточка обязана остаться запертой именно потому,
                // что одного тика до цены не хватает — а не потому, что состояние заморожено.
                Assert.Less(driver.Game.Money, driver.Game.CurrentCardPrice,
                    "одного тика (+1 ₽) до цены 60 ₽ не хватает");
                Assert.IsTrue(driver.Game.CurrentCardBlocked,
                    "…поэтому карточка всё ещё заперта — живой пересчёт согласен");
            }

            fake.No();                                   // MD03 → NORMAL: блокировки больше нет
            driver.DebugApplyAgeGates(40f);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(AlarmScale.Money), "на обычной карточке тревоги денег нет");
            Assert.AreEqual(before, driver.StarBurstCount, "смена карточки — не заслуга игрока, салюта нет");
            Assert.AreEqual(0, driver.ActiveStarCount, "…и звёзд в воздухе нет");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>Та же BLOCK$-колода, но с хвостом: после блокированной MD03 есть куда ходить дальше
        /// (контрольная половина теста отвечает ещё раз, и колода не имеет права кончиться финалом).</summary>
        private static Game BlockSkipDeck()
        {
            var deck = new List<Card>
            {
                Starter(), Plain("FILL", 18), Plain("GRACE", 20),   // GRACE — под льготу r4 п.1б, см. BlockDeck
                Plain("MD03", 30), Plain("NORMAL", 40), Plain("TAIL", 41),
            };
            deck[3].IsBlockCost = true;   // MD03 → BLOCK$ (Game.BlockPrices["MD03"] = 60)
            return new Game(deck, coin: () => false);
        }

        // BLOCK$-ПРОПУСК ≠ ВЫБОР (находка ревью r3, MAJOR). Заблокированная карточка пропускается любым
        // рычагом БЕЗ Δ, без некролога и без записи ответа — но Game.HandleInput при этом возвращает true
        // (ход состоялся). Драйвер отмечал по нему §6-окно ЗДОРОВЬЯ, хотя разводка вводов говорит прямо
        // обратное: здоровье чинят ВЫБОРЫ, а выбора не было. Получалось «недавно чинил здоровье» без
        // единой попытки лечения, и следующая же карточка, вытянувшая здоровье из тревоги, выдавала за
        // это САЛЮТ. Панч плашки на этом пути уже был отключён (п.4) — теперь отметка ходит с ним в паре.
        [UnityTest]
        public IEnumerator BlockedSkip_DoesNotOpenTheHealthWindow_AndGivesNoFalseStarBurst()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(BlockSkipDeck());
            fake.Confirm();                              // опенер → I03
            fake.No();                                   // I03 → FILL
            yield return OpenMoneyInGame(driver, fake);  // деньги открыты В ИГРЕ (но их всё равно < 60₽)
            fake.No();                                   // FILL → GRACE (льгота r4 п.1б тратится здесь)
            fake.No();                                   // GRACE → MD03: цена 60₽, карман пуст → БЛОКИРОВКА
            driver.DebugApplyAgeGates(40f);
            Assert.IsTrue(driver.Game.CurrentCardBlocked, "MD03 действительно заблокирована ценой");

            driver.Game.Scales.Health = 12;              // здоровье в тревоге → ложный салют ВОЗМОЖЕН
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);   // окна §6 протухли
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(AlarmScale.Health), "тревога здоровья живая");
            Assert.Greater(driver.SinceScaleInput(AlarmScale.Health), GameDriver.AlarmRecentInputSeconds,
                "…и окно §6 по здоровью закрыто — считать будем ровно то, что откроет пропуск");
            int before = driver.StarBurstCount;
            var blocked = driver.Game.CurrentCard;

            fake.No();                                   // рычаг на ЗАБЛОКИРОВАННОЙ карточке = ПРОПУСК
            Assert.AreNotSame(blocked, driver.Game.CurrentCard, "ход состоялся: карточка пропущена…");
            Assert.Greater(driver.SinceScaleInput(AlarmScale.Health), GameDriver.AlarmRecentInputSeconds,
                "…но окно §6 по здоровью он НЕ открыл: лечат ВЫБОРЫ, а выбора на блокировке не было");

            driver.Game.Scales.Health = 70;              // здоровье вытянула СЛЕДУЮЩАЯ карточка, не игрок
            driver.DebugApplyAgeGates(40f);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(AlarmScale.Health), "тревога снята");
            Assert.AreEqual(before, driver.StarBurstCount, "ложного салюта после BLOCK$-пропуска нет");
            Assert.AreEqual(0, driver.ActiveStarCount, "…и звёзд в воздухе нет");

            // Контроль тем же путём: на НЕ заблокированной карточке ТОТ ЖЕ рычаг окно §6 открывает —
            // фикс режет ровно пропуск, а не ответы вообще.
            Assert.IsFalse(driver.Game.CurrentCardBlocked, "текущая карточка обычная");
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);
            fake.No();
            Assert.AreEqual(0f, driver.SinceScaleInput(AlarmScale.Health), 1e-4f,
                "принятый ОТВЕТ (а не пропуск) окно §6 по здоровью открывает");

            Object.Destroy(go);
            yield return null;
        }

        // Окно §6 по деньгам открывает только ПРИНЯТЫЙ кэпом тик: «крутил» ≠ «механика засчитала».
        // Иначе мэшинг ручкой (кэп ~5/с режет всё лишнее) выпрашивал бы салют за чужую заслугу.
        [UnityTest]
        public IEnumerator MoneyWindow_OpensOnAnAcceptedCrankTick_NotOnOneTheIncomeCapRejected()
        {
            var driver = Boot(out var go, out var fake);
            // Шкалы открыты В ИГРЕ: «принятый тик» — это тик, который засчитал и кэп дохода, и Game.Crank.
            yield return ToFrozenAdultWithLiveScales(driver, fake);   // драйвер заморожен: часы кэпа вручную
            const AlarmScale money = AlarmScale.Money;

            driver.DebugAdvanceInputClocks(1f);          // окно кэпа открыто
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(0f, driver.SinceScaleInput(money), 1e-4f,
                "принятый тик крутилки открывает окно §6 по деньгам");

            // Окно §6 протухло, а часы кэпа НЕ шли — значит следующий тик кэп ОТВЕРГНЕТ (это мэшинг).
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);
            Assert.Greater(driver.SinceScaleInput(money), GameDriver.AlarmRecentInputSeconds,
                "окно §6 честно протухло");
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.Greater(driver.SinceScaleInput(money), GameDriver.AlarmRecentInputSeconds,
                "ОТВЕРГНУТЫЙ кэпом тик окно §6 не открывает — он и в Game.Crank не ушёл");

            Object.Destroy(go);
            yield return null;
        }

        // То же по энергии: окно §6 открывает ПОДНЯТЫЙ ДАТЧИК — то есть реальная работа шкалой. Шкалу,
        // вытянутую КАРТОЧКОЙ, салют не награждает: салют даётся за калибровку руками, а не за подарок.
        // (До 2026-08-07 роль «настоящего ввода» играл импульс, пропущенный ритм-гейтом; гейт снят.)
        [UnityTest]
        public IEnumerator StarBurst_DoesNotFire_WhenTheScaleWasLiftedByACard_NotByTheSensor()
        {
            var driver = Boot(out var go, out var fake);
            // Энергия открыта В ИГРЕ — иначе Game отвергает удержание, и «контроль» ниже ничего не проверяет.
            yield return ToFrozenAdultWithLiveScales(driver, fake);
            const AlarmScale e = AlarmScale.Energy;

            driver.Game.Scales.Energy = 10;
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(e), "энергия в тревоге");
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);   // окно §6 протухло
            int before = driver.StarBurstCount;
            Assert.Greater(driver.SinceScaleInput(e), GameDriver.AlarmRecentInputSeconds,
                "окно §6 честно протухло — датчик никто не трогал");

            driver.Game.Scales.Energy = 60;              // шкалу вытянула КАРТОЧКА, не игрок
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(e), "тревога снята");
            Assert.AreEqual(before, driver.StarBurstCount, "без работы датчиком салюта нет");
            Assert.AreEqual(0, driver.ActiveStarCount, "…и звёзд в воздухе нет");

            // Контроль тем же путём: ПОДНЯТЫЙ датчик — ввод засчитан, окно §6 открылось, выход в норму
            // награждается салютом.
            driver.Game.Scales.Energy = 10;
            driver.DebugPumpAlarms(0.02f);
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);
            Assert.IsTrue(driver.AlarmActive(e), "снова в тревоге, окно §6 снова протухло");
            fake.Fire(GameInput.EnergyHold);
            Assert.AreEqual(0f, driver.SinceScaleInput(e), 1e-4f,
                "поднятый датчик ЗАСЧИТАН — он и открывает окно §6");
            driver.Game.Scales.Energy = 60;
            driver.DebugPumpAlarms(0.02f);
            Assert.AreEqual(before + 1, driver.StarBurstCount, "работа датчиком → салют");

            Object.Destroy(go);
            yield return null;
        }

        // ================================================================ §6-окно ↔ ПРИНЯТЫЙ ввод
        // MAJOR (скептик 2026-08-07): датчик высоты переиздаётся КАЖДЫЙ кадр, и раньше драйвер отмечал окно
        // §6 по энергии на КАЖДОМ таком кадре — даже когда Game эти вводы ГЛУШИТ (кризис/депрессия: шкалы
        // на паузе). Тревога энергии в депрессии живая (AlarmLive депрессию не исключает), поэтому рост
        // энергии КАРТОЧКОЙ внутри двухсекундного окна выдавал бы ЛОЖНЫЙ салют за работу, которой не было.

        private static readonly string[] CrisisIds =
            { "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08" };

        private static string Csv()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            return asset.text;
        }

        /// <summary>Игра, доезжающая до кризиса за секунды и гарантированно сваливающаяся в депрессию
        /// (тот же боот, что в DepressionHudTests).</summary>
        private static Game CrisisGame(string csv, bool toDepression)
        {
            var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
            // r3: после кризиса нужен запас обычных карточек — депрессия входит только через зазор.
            var fillers = new List<Card>();
            for (int i = 0; i < Game.DepressionGapCards + 6; i++)
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
                DepressionTriggerRoll = () => toDepression,
                DepressionPulseInterval = () => 2.5f,
            };
        }

        /// <summary>Довести НАСТОЯЩИЙ драйвер до блица кризиса, разобрав по пути туториалы и облачко.</summary>
        private static IEnumerator ReachBlitz(GameDriver driver, Game g, PlayFakeInputSource fake)
        {
            driver.DebugReplaceGame(g);
            fake.Confirm();
            g.HandleInput(GameInput.AnswerNo);            // resolve I03 → age timer running
            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                // r3: по дороге к 45 годам встают и §D-модалки шкал, и ВХОДНЫЕ ЭКРАНЫ спецрежимов
                // (здоровье на 30). Каждый — своя пауза, поэтому снимаем их своим контролом, иначе
                // цикл просто крутится на замороженном Tick.
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); yield return null; continue; }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "доехали до блица кризиса");
            // …и сам ВХОДНОЙ ЭКРАН БЛИЦА: кризис теперь объявляется экраном, а не молча.
            Assert.AreEqual(SpecialMode.Blitz, driver.SpecialModeKind, "вход в блиц объявлен экраном");
            NewScaleTut.ClearSpecial(driver, fake);
            yield return null;
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);   // состарить объявление кризиса
        }

        // Депрессия ГЛУШИТ все шкальные контролы. Ни один из них не смеет открыть окно §6 — иначе шкала,
        // вытянутая карточкой в пределах окна, награждается салютом за чужую работу.
        [UnityTest]
        public IEnumerator ScaleWindow_StaysShut_WhenDepressionSwallowsTheControls_AndNoFalseStarBurst()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var g = CrisisGame(Csv(), toDepression: true);
            yield return ReachBlitz(driver, g, fake);
            for (int i = 0; i < 5; i++) fake.Yes();                   // чистый блиц → хвост арминг депрессии
            // r3: депрессия больше не влетает ВСТЫК за блицем — между ними зазор в обычных карточках.
            Assert.IsTrue(g.DepressionArmed, "хвост кризиса зарядил депрессию…");
            for (int i = 0; i < Game.DepressionGapCards; i++)
            {
                if (g.CurrentCard != null) g.HandleInput(GameInput.AnswerNo);
                NewScaleTut.ClearAny(driver, fake);
            }
            Assert.IsTrue(g.InDepression, "…и она началась, когда зазор был выбран");
            NewScaleTut.ClearSpecial(driver, fake);                   // входной экран депрессии — по зелёной
            yield return null;
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);    // состарить облачко «ТЁМНАЯ ПОЛОСА…»
            yield return null;

            driver.enabled = false;                                   // дальше время подаём вручную
            driver.DebugApplyAgeGates(40f);                           // весь взрослый HUD на экране
            const AlarmScale e = AlarmScale.Energy;

            g.Scales.Energy = 10;
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(e),
                "тревога энергии в депрессии ЖИВАЯ — значит ложный салют физически возможен");
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);   // окна §6 протухли
            int before = driver.StarBurstCount;

            // Игрок держит датчик (источник переиздаёт его каждый кадр) — но механика его глушит.
            for (int i = 0; i < 5; i++) fake.Fire(GameInput.EnergyHold);
            Assert.Greater(driver.SinceScaleInput(e), GameDriver.AlarmRecentInputSeconds,
                "заглушенное депрессией удержание окно §6 по энергии НЕ открывает");

            driver.DebugAdvanceInputClocks(1f);
            fake.Fire(GameInput.MoneyTick);
            Assert.Greater(driver.SinceScaleInput(AlarmScale.Money), GameDriver.AlarmRecentInputSeconds,
                "…крутилка в депрессии — тоже не работа по шкале");
            fake.Fire(GameInput.RelationRight);
            Assert.Greater(driver.SinceScaleInput(AlarmScale.Relations), GameDriver.AlarmRecentInputSeconds,
                "…и рычаг отношений");
            fake.No();
            Assert.Greater(driver.SinceScaleInput(AlarmScale.Health), GameDriver.AlarmRecentInputSeconds,
                "…и ответ на карточку (в депрессии карточек нет)");

            // Шкалу вытягивает КАРТОЧКА — внутри бывшего «окна». Салюта быть не должно.
            g.Scales.Energy = 60;
            driver.DebugPumpAlarms(0.02f);
            Assert.IsFalse(driver.AlarmActive(e), "тревога снята");
            Assert.AreEqual(before, driver.StarBurstCount, "ложного салюта в депрессии НЕТ");
            Assert.AreEqual(0, driver.ActiveStarCount, "…и звёзд в воздухе нет");

            Object.Destroy(go);
            yield return null;
        }

        // То же для кризиса: блиц ГЛУШИТ крутилку/датчик/ось и ПЕРЕНАЗНАЧАЕТ рычаги ДА/НЕТ под свои кнопки —
        // нажатие в блице не является калибровкой шкалы здоровья.
        [UnityTest]
        public IEnumerator ScaleWindow_StaysShut_WhenTheCrisisSwallowsOrRepurposesTheControls()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var g = CrisisGame(Csv(), toDepression: false);
            yield return ReachBlitz(driver, g, fake);
            driver.enabled = false;
            driver.DebugApplyAgeGates(40f);
            driver.DebugPumpAlarms(GameDriver.AlarmRecentInputSeconds + 0.5f);   // окна §6 протухли

            for (int i = 0; i < 5; i++) fake.Fire(GameInput.EnergyHold);
            driver.DebugAdvanceInputClocks(1f);
            fake.Fire(GameInput.MoneyTick);
            fake.Fire(GameInput.RelationLeft);
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "всё ещё в блице (шкальные контролы тут инертны)");
            fake.Yes();                                               // рычаг ДА = кнопка блица, НЕ ответ

            foreach (AlarmScale s in System.Enum.GetValues(typeof(AlarmScale)))
                Assert.Greater(driver.SinceScaleInput(s), GameDriver.AlarmRecentInputSeconds,
                    "кризис не открывает окно §6 ни по одной шкале: " + s);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StarBurst_DoesNotFire_OnRestart_AndTheRestartClearsTheSky()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);
            const AlarmScale rel = AlarmScale.Relations;

            driver.Game.Scales.Relationships = 25;
            driver.DebugNoteScaleInput(rel);
            driver.DebugPumpAlarms(0.02f);
            Assert.IsTrue(driver.AlarmActive(rel), "уходим в рестарт ИЗ тревоги, с свежим вводом в окне");

            driver.StarBurst();                                   // в небе уже висит салют
            Assert.Greater(driver.ActiveStarCount, 0);
            int before = driver.StarBurstCount;

            driver.enabled = true;
            fake.Fire(GameInput.Exit);                            // MenuButton → свежая жизнь
            Assert.AreEqual(GameState.Opener, driver.Game.State, "вернулись на опенер");
            Assert.AreEqual(before, driver.StarBurstCount, "рестарт салютом НЕ награждается");
            Assert.AreEqual(0, driver.ActiveStarCount, "…и небо очищено от старых звёзд");
            foreach (AlarmScale s in System.Enum.GetValues(typeof(AlarmScale)))
                Assert.IsFalse(driver.AlarmActive(s), "рестарт сбрасывает тревоги: " + s);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StarBurst_FliesOutward_FadesAndSelfCleans()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToFrozenAdult(driver, fake);

            driver.StarBurst();
            int n = driver.ActiveStarCount;
            Assert.GreaterOrEqual(n, GameDriver.StarBurstMin);
            Assert.LessOrEqual(n, GameDriver.StarBurstMax);
            var stars = driver.StarLayer.GetComponentsInChildren<Image>();
            foreach (var s in stars)
                Assert.AreEqual(Vector2.zero, s.rectTransform.anchoredPosition,
                    "бёрст стартует из ЦЕНТРА экрана");

            driver.DebugAdvanceStars(0.3f);
            foreach (var s in stars)
                Assert.Greater(s.rectTransform.anchoredPosition.magnitude, 100f,
                    "через 0.3 с звёзды заметно разлетелись наружу");

            driver.DebugAdvanceStars(0.3f);                       // 0.6 с — уже в зоне затухания
            Assert.IsTrue(stars.Where(s => s != null).All(s => s.color.a < 1f),
                "альфа затухает по ходу разлёта");

            // Самоочистка: спековый максимум разлёта 0.9 с — после него в небе не должно остаться ничего.
            driver.DebugAdvanceStars(GameDriver.StarFlightMax + 0.05f);
            Assert.AreEqual(0, driver.ActiveStarCount, "салют самоочистился (объекты сняты)");
            yield return null;                                     // Destroy отложен до конца кадра
            Assert.AreEqual(0, driver.StarLayer.transform.childCount, "…и слой FX реально пуст");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StarBurst_IsDeterministic_ForTheSameBurstNumber()
        {
            // Сид = StarBurstSeedBase + номер бёрста, поэтому два драйвера на одном и том же номере
            // дают ОДИН И ТОТ ЖЕ разлёт — на этом стоит воспроизводимость кадра дизайн-гейта.
            var a = Boot(out var goA, out var fakeA);
            yield return ToFrozenAdult(a, fakeA);
            var b = Boot(out var goB, out var fakeB);
            yield return ToFrozenAdult(b, fakeB);

            a.StarBurst(); a.DebugAdvanceStars(0.25f);
            b.StarBurst(); b.DebugAdvanceStars(0.25f);

            var pa = a.StarLayer.GetComponentsInChildren<Image>()
                .Select(i => i.rectTransform.anchoredPosition).ToList();
            var pb = b.StarLayer.GetComponentsInChildren<Image>()
                .Select(i => i.rectTransform.anchoredPosition).ToList();
            Assert.AreEqual(pa.Count, pb.Count, "одинаковое число звёзд");
            for (int i = 0; i < pa.Count; i++)
                Assert.Less((pa[i] - pb[i]).magnitude, 0.01f, "звезда " + i + " летит по той же траектории");

            Object.Destroy(goA);
            Object.Destroy(goB);
            yield return null;
        }
    }
}
