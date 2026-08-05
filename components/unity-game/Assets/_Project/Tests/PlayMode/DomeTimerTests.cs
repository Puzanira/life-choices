using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Купол-таймер (revisions §5a / build-spec §2) через НАСТОЯЩИЙ драйвер: круглое кольцо снято
    /// целиком, дуга-остаток убывает ровно за таймер текущей фазы (§3), в последнюю секунду уходит
    /// в `RED_BRIGHT`, на паузе (туториал/§D-модалка) заморожена, а на опенере и финале купола нет.
    /// Геометрия (крупный бокс §2 по центру), z-порядок «под барами» и просветы живут в
    /// HudConformanceTests (DomeTimer_IsTheBigCentredDome_DrawnUnderTheBars).
    /// </summary>
    public class DomeTimerTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static IEnumerator ToPlaying(GameDriver driver, PlayFakeInputSource fake)
        {
            yield return null;          // Start wires input + subscriptions
            fake.Confirm();             // opener → playing, first card dealt
            yield return null;
            // Веха идёт обычной карточкой (баннер-бит снят 2026-08-05) — игра живая с первого же кадра.
            Assert.IsFalse(driver.Game.Paused, "карточка идёт живьём");
        }

        // ---- the ring is gone -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheOldTimerRing_IsGone_TheDomeIsTheOnlyTimerWidget()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToPlaying(driver, fake);

            var names = driver.GamePanel.GetComponentsInChildren<Image>(true)
                .Select(i => i.gameObject.name).ToList();
            foreach (var gone in new[] { "RingOutline", "RingTrack", "RingFill", "RingCenter" })
                CollectionAssert.DoesNotContain(names, gone, $"слой кольца «{gone}» снят");
            Assert.IsNull(driver.TimerDome.GetComponentInChildren<Text>(true),
                "цифры секунд в куполе нет — по спеку купол = только дуга");

            // …and the three dome layers ARE there, all on the same procedurally drawn semicircle.
            foreach (var img in new[] { driver.TimerDomeOutline, driver.TimerDomeTrack, driver.TimerDomeFill })
            {
                Assert.IsNotNull(img, "слой купола построен");
                Assert.IsNotNull(img.sprite, "слой купола несёт спрайт");
                Assert.AreEqual("timer-dome", img.sprite.name, "полукруг нарисован процедурно");
            }
            // …плюс «стрелка-кромка» — свой процедурный спрайт с мягкими краями, поверх заливки.
            Assert.IsNotNull(driver.TimerDomeHand, "стрелка-кромка построена");
            Assert.AreEqual("timer-dome-hand", driver.TimerDomeHand.sprite.name, "стрелка нарисована процедурно");
            Assert.AreSame(driver.TimerDomeFill.transform.parent, driver.TimerDomeHand.transform.parent,
                "стрелка живёт в той же группе купола");
            Assert.Greater(driver.TimerDomeHand.transform.GetSiblingIndex(),
                driver.TimerDomeFill.transform.GetSiblingIndex(),
                "стрелка рисуется ПОВЕРХ заливки — иначе она не спрячет её ступенчатый край");
            // Кант купола — ЧИСТО ЧЁРНЫЙ (как штрихи арт-пака), а не INK: купол стоит среди наклеек.
            Assert.AreEqual(GameDriver.DomeInkToken, driver.TimerDomeOutline.color, "кант — чистый чёрный");
            Assert.AreEqual(Color.black, GameDriver.DomeInkToken, "…и это именно #000000");
            Assert.AreNotEqual(GameDriver.InkToken, GameDriver.DomeInkToken, "кант больше не токен INK");
            Assert.AreEqual(GameDriver.CreamToken, driver.TimerDomeTrack.color, "трек — токен CREAM");
            Assert.AreEqual(GameDriver.DomeYellowToken, driver.TimerDomeFill.color, "дуга — токен YELLOW");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>Цвет с допуском: пульс считается через Mathf.Cos, так что бит-в-бит равенства нет.</summary>
        private static void AssertColorNear(Color expected, Color actual, string what)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f, what + " (R)");
            Assert.AreEqual(expected.g, actual.g, 0.01f, what + " (G)");
            Assert.AreEqual(expected.b, actual.b, 0.01f, what + " (B)");
        }

        // ---- the arc empties over EXACTLY the phase's seconds ------------------------------------

        [UnityTest]
        public IEnumerator Dome_IsFullOnANewCard_AndEmptiesOverExactlyThePhaseTimer()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToPlaying(driver, fake);

            var card = driver.Game.CurrentCard;
            float full = driver.Game.CardTimerMax;
            Assert.AreEqual(Game.AnswerSecondsFor(card.Age), full, 1e-4f,
                "длина дуги = таймер фазы этой карточки (§3), а не фиксированные 5 с");
            Assert.Greater(driver.TimerDomeFill.fillAmount, 0.97f, "в начале карточки дуга полная");

            // Драйвер продвигаем ТОЛЬКО через DebugTick, чтобы кадры не подмешивали свой Time.deltaTime.
            float rem0 = driver.Game.CardTimer;
            driver.DebugTick(rem0 / 2f);
            Assert.AreSame(card, driver.Game.CurrentCard, "на половине окна карточка ещё та же");
            Assert.AreEqual(rem0 / 2f / full, driver.TimerDomeFill.fillAmount, 0.02f,
                "дуга убывает пропорционально ОСТАВШЕМУСЯ времени фазы");

            // …почти до нуля к концу окна — и карточка всё ещё та же.
            driver.DebugTick(rem0 / 2f - 0.08f);
            Assert.AreSame(card, driver.Game.CurrentCard, "в последние миллисекунды окна карточка жива");
            Assert.Less(driver.TimerDomeFill.fillAmount, 0.03f, "дуга практически пуста к концу окна");

            // …и ровно на конце окна — таймаут, новая карточка, снова полная дуга (рестарт купола).
            driver.DebugTick(0.12f);
            Assert.AreNotSame(card, driver.Game.CurrentCard, "таймаут ровно в конце таймера фазы");
            Assert.Greater(driver.TimerDomeFill.fillAmount, 0.97f, "новая карточка — снова полный купол");

            Object.Destroy(go);
            yield return null;
        }

        // ---- WHICH half of the dome is yellow — the arc's DIRECTION, read off the real fill mesh ---------
        // `fillAmount` alone says «половина» but not «какая половина»: Radial180 c origin=Top даёт разное
        // направление при clockwise true/false, и обе версии дают ровно тот же fillAmount. Поэтому канон
        // направления пинится по ФАКТИЧЕСКОЙ геометрии заливки — по вершинам меша, который Image рисует.
        //
        // КАНОН (Radial180 · origin = Top · clockwise): дуга тает СЛЕВА НАПРАВО. Первой гаснет ЛЕВАЯ
        // половина купола (к середине окна её уже нет), последней догорает ПРАВАЯ — жёлтый остаток
        // всегда прижат к правому краю. Симметричное «таяние с двух краёв к центру» средствами
        // Radial180 недостижимо, так что канон — этот, и он зафиксирован здесь визуально.

        /// <summary>Треугольники, которые Image реально отдаёт на отрисовку (локальные px рект-а дуги).</summary>
        private static List<UIVertex> FillMesh(Image img)
        {
            var m = typeof(Image).GetMethod("OnPopulateMesh",
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(VertexHelper) }, null);
            Assert.IsNotNull(m, "Image.OnPopulateMesh доступен — иначе меш дуги не прочитать");
            var vh = new VertexHelper();
            m.Invoke(img, new object[] { vh });
            var verts = new List<UIVertex>();
            vh.GetUIVertexStream(verts);       // треугольниками, по 3 вершины
            vh.Dispose();
            return verts;
        }

        /// <summary>Крайние X закрашенной части (локальные px: 0 = ось купола, + = вправо).</summary>
        private static (float min, float max) FillSpanX(Image img)
        {
            var v = FillMesh(img);
            Assert.Greater(v.Count, 0, "дуга что-то рисует");
            float min = float.MaxValue, max = float.MinValue;
            foreach (var uv in v) { min = Mathf.Min(min, uv.position.x); max = Mathf.Max(max, uv.position.x); }
            return (min, max);
        }

        /// <summary>Площадь закрашенных треугольников — «сколько краски» на куполе.</summary>
        private static float FillArea(Image img)
        {
            var v = FillMesh(img);
            float a = 0f;
            for (int i = 0; i + 2 < v.Count; i += 3)
            {
                Vector3 p0 = v[i].position, p1 = v[i + 1].position, p2 = v[i + 2].position;
                a += Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y)) * 0.5f;
            }
            return a;
        }

        [UnityTest]
        public IEnumerator Dome_ArcMeltsLeftToRight_TheLastYellowHugsTheRightEdge()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToPlaying(driver, fake);

            var fill = driver.TimerDomeFill;
            float halfW = fill.rectTransform.rect.width / 2f;
            Assert.Greater(halfW, 100f, "дуга крупная — купол по центру экрана, а не в коридоре");

            // Полный купол: краска на ОБЕИХ половинах, от края до края.
            driver.DebugReflectDome(10f, 10f);
            var whole = FillSpanX(fill);
            float wholeArea = FillArea(fill);
            Assert.AreEqual(-halfW, whole.min, 1.5f, "на полном таймере закрашен и левый край");
            Assert.AreEqual(halfW, whole.max, 1.5f, "…и правый");

            // 3/4 окна: левая половина уже НАДКУСАНА, но ещё достаёт до левого края — таяние идёт слева.
            driver.DebugReflectDome(7.5f, 10f);
            var threeQ = FillSpanX(fill);
            Assert.Less(threeQ.min, -halfW * 0.9f, "на 3/4 левая половина ещё частично жёлтая");
            Assert.Less(FillArea(fill), wholeArea - 1f, "…но краски уже меньше, чем на полном куполе");

            // РОВНО половина окна: жёлтая ровно ПРАВАЯ половина купола, левой нет вовсе.
            driver.DebugReflectDome(5f, 10f);
            var half = FillSpanX(fill);
            Assert.AreEqual(0f, half.min, 1.5f,
                "на середине окна левая половина купола ПОГАСЛА — краска начинается от оси");
            Assert.AreEqual(halfW, half.max, 1.5f, "…и доходит до правого края: жёлтая именно ПРАВАЯ половина");
            Assert.AreEqual(0.5f, FillArea(fill) / wholeArea, 0.02f,
                "половина времени = ровно половина закрашенного купола");

            // Последняя четверть: остаток целиком в правой половине и его меньше, чем на середине.
            float halfArea = FillArea(fill);
            driver.DebugReflectDome(2.5f, 10f);
            var quarter = FillSpanX(fill);
            Assert.GreaterOrEqual(quarter.min, -1.5f, "в конце окна краска не возвращается в левую половину");
            Assert.AreEqual(halfW, quarter.max, 1.5f, "последний жёлтый прижат к ПРАВОМУ краю купола");
            Assert.Less(FillArea(fill), halfArea - 1f, "…и его строго меньше, чем на середине окна");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Dome_TurnsAlarmRed_InTheLastSecond()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToPlaying(driver, fake);

            var alarm = GameDriver.DomeAlarmToken;

            driver.DebugReflectDome(2.5f, 10f);
            Assert.AreEqual(GameDriver.DomeYellowToken, driver.TimerDomeFill.color,
                "пока времени больше секунды — канонный YELLOW");
            Assert.AreEqual(GameDriver.CreamToken, driver.TimerDomeTrack.color,
                "…и «истёкший» трек вне тревоги — чистый CREAM, красного в куполе нет вовсе");

            driver.DebugReflectDome(0.6f, 10f);
            var c = driver.TimerDomeFill.color;
            Assert.Greater(c.r, 0.85f, "последняя секунда — тревожный красный");
            Assert.Less(c.g, 0.35f, "…не жёлтый (зелёная составляющая упала)");
            Assert.Less(Mathf.Abs(c.r - alarm.r), 0.2f, "…и это именно RED_BRIGHT, а не произвольный красный");
            Assert.Less(c.b, 0.4f);

            // Дизайн-гейт: на 10 % остатка красная кромка дуги — это ~0.2 % экрана, тревогу не видно.
            // Поэтому в последнюю секунду мигает ВЕСЬ купол: «истёкший» трек ходит CREAM ⇄ RED_BRIGHT,
            // и на ПИКЕ весь силуэт 400×130 (включая коридор между барами) — красный.
            driver.DebugReflectDome(2f * GameDriver.DomeAlarmPulsePeriod, 6f);   // ровно пик мигания
            AssertColorNear(alarm, driver.TimerDomeTrack.color,
                "на пике тревоги трек залит RED_BRIGHT — красным занят весь купол, а не кромка");
            AssertColorNear(alarm, driver.TimerDomeFill.color, "…и остаток дуги тоже RED_BRIGHT");

            // …а между вспышками трек возвращается к крему — это МИГАНИЕ, а не разовый перекрас.
            driver.DebugReflectDome(1.5f * GameDriver.DomeAlarmPulsePeriod, 6f); // ровно провал мигания
            AssertColorNear(GameDriver.CreamToken, driver.TimerDomeTrack.color,
                "в провале мигания трек снова CREAM — купол честно мигает");
            AssertColorNear(alarm, driver.TimerDomeFill.color, "…дуга при этом остаётся тревожной");

            Object.Destroy(go);
            yield return null;
        }

        // ---- the alarm READ OFF THE RENDERED FRAME -----------------------------------------------
        // Цвет в Image — ещё не «видно тревогу»: дизайн-гейт мерил именно ПИКСЕЛИ и показал, что на
        // 10 % остатка красное жило только узкой полосой у верхнего края. Поэтому здесь купол реально
        // рендерится в 1920×1080 RenderTexture и красное считается в двух ПРОСВЕТАХ — в коридоре между
        // барами и в полосе над ними.
        [UnityTest]
        public IEnumerator Dome_InTheLastSecond_FloodsBothGapsWithRed_ReadOffTheRenderedFrame()
        {
            const int W = 1920, H = 1080;
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            yield return null;
            driver.DebugPreviewArcadeShot();          // весь взрослый HUD, драйвер заморожен

            // Тот же способ съёмки, что у ArcadeScreenshotTests: канвас в камеру, ConstantPixelSize 1:1.
            var canvas = driver.CanvasRect.GetComponent<Canvas>();
            var camGo = new GameObject("DomeShotCam");
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

            // Доля «тревожно-красных» пикселей в боксе, заданном в референс-px (y — от ВЕРХА экрана).
            float RedShare(Color32[] px, int x0, int y0, int x1, int y1)
            {
                int red = 0, total = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        var p = px[(H - 1 - y) * W + x];       // ReadPixels отдаёт строки снизу вверх
                        if (p.r > 190 && p.g < 90 && p.b < 90) red++;
                        total++;
                    }
                return (float)red / total;
            }

            // Коридор между барами (x 936…1042) и полоса над ними (бары начинаются с y 35) — ровно те
            // два просвета, в которых купол вообще виден.
            const int CorL = 937, CorT = 60, CorR = 1041, CorB = 110;
            const int BandL = 800, BandT = 5, BandR = 1120, BandB = 30;

            // (1) ПИК мигания в последнюю секунду: красным залиты ОБА просвета.
            driver.DebugReflectDome(2f * GameDriver.DomeAlarmPulsePeriod, 6f);
            var alarmPx = Shoot();
            float corridor = RedShare(alarmPx, CorL, CorT, CorR, CorB);
            float band = RedShare(alarmPx, BandL, BandT, BandR, BandB);
            Assert.Greater(corridor, 0.5f,
                $"на пике тревоги коридор между барами красный (замер {corridor:P0})");
            Assert.Greater(band, 0.5f,
                $"на пике тревоги полоса купола над барами красная (замер {band:P0})");

            // (2) …и вне последней секунды красного в куполе НЕТ ни в одном просвете.
            driver.DebugReflectDome(3f, 6f);
            var calmPx = Shoot();
            float calmCorridor = RedShare(calmPx, CorL, CorT, CorR, CorB);
            float calmBand = RedShare(calmPx, BandL, BandT, BandR, BandB);
            Assert.Less(calmCorridor, 0.01f,
                $"вне тревоги в коридоре красного нет (замер {calmCorridor:P1})");
            Assert.Less(calmBand, 0.01f, $"вне тревоги в полосе красного нет (замер {calmBand:P1})");

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(tex);
            Object.Destroy(rt);
            Object.Destroy(camGo);
            Object.Destroy(go);
            yield return null;
        }

        // ---- pauses freeze the dome --------------------------------------------------------------

        [UnityTest]
        public IEnumerator Dome_IsFrozen_WhileATutorialIsUp()
        {
            var driver = Boot(out var go, out var fake);
            yield return ToPlaying(driver, fake);

            driver.DebugTick(1f);                       // немного стравим дугу, чтобы заморозка была видна
            float frozen = driver.TimerDomeFill.fillAmount;
            Assert.Less(frozen, 1f, "дуга уже убывала до паузы");

            driver.DebugShowTutorial("Тестовая подсказка");
            Assert.IsTrue(driver.TutorialShowing);
            Assert.IsTrue(driver.Game.Paused, "туториал ставит игру на паузу");

            driver.DebugTick(3f);                       // на паузе Game.Tick не двигает таймер карточки
            Assert.AreEqual(frozen, driver.TimerDomeFill.fillAmount, 1e-4f,
                "купол заморожен под подсказкой — дуга не сдвинулась");

            fake.Confirm();                             // Enter снимает подсказку
            Assert.IsFalse(driver.TutorialShowing);
            driver.DebugTick(0.5f);
            Assert.Less(driver.TimerDomeFill.fillAmount, frozen - 1e-3f,
                "после снятия паузы дуга пошла дальше с замороженного места");

            Object.Destroy(go);
            yield return null;
        }

        // ---- opener / finale carry no dome -------------------------------------------------------

        [UnityTest]
        public IEnumerator Dome_IsAbsent_OnTheOpener_AndOnTheFinale()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            Assert.IsTrue(driver.OpenerPanel.activeSelf, "стартуем на опенере");
            Assert.IsFalse(driver.TimerDome.activeInHierarchy, "на опенере купола нет");

            fake.Confirm();
            yield return null;
            Assert.IsTrue(driver.TimerDome.activeInHierarchy, "в игре купол виден");

            driver.DebugRenderFinale(Necrolog.Build("спокойная старость",
                new System.Collections.Generic.List<NecrologEntry>()));
            Assert.IsFalse(driver.TimerDome.activeInHierarchy, "на финале купола нет");

            Object.Destroy(go);
            yield return null;
        }
    }
}
