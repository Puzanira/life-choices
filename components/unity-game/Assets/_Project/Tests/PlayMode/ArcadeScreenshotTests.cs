using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using ThanksNoThanks;
using AiGameStudio.ArcadeControls;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Batch screenshot harness for the arcade-packaging increment: poses a clean mid-life frame (card +
    /// revealed HUD scales), renders the self-built uGUI HUD through an offscreen camera into a 1920×1080
    /// RenderTexture, writes a PNG (path from the LIFECHOICES_SHOT_PATH env var, else the temp dir), and
    /// asserts the frame has ZERO magenta pixels (no missing shaders/sprites). Doubles as the done-contract
    /// screenshot artifact.
    /// </summary>
    public class ArcadeScreenshotTests
    {
        private const int W = 1920;
        private const int H = 1080;

        // ОБЫЧНАЯ прожитая жизнь для позы «finale» — ПОЛНЫЕ семь строк (родители + шесть выборов), как
        // их отбирает отрезок 0: вехи (свадьба, ребёнок) плюс по кусочку каждого возраста. До 2026-08-08
        // эта поза показывала всего четыре строки и потому ничего не говорила о вёрстке — а именно она
        // сломалась в живом плейтесте («некролог большущей простынёй»).
        private static NecrologResult SampleNecrolog()
        {
            var entries = new System.Collections.Generic.List<NecrologEntry>
            {
                new NecrologEntry { Age = 7,  Order = 0, Line = "В семь лет вы завели рыжего кота и назвали его Борщ." },
                new NecrologEntry { Age = 19, Order = 1, Line = "Первую зарплату спустили за один вечер." },
                new NecrologEntry { Age = 24, Order = 2, Line = "В двадцать четыре уехали в другой город и ни разу не пожалели." },
                new NecrologEntry { Age = 30, Order = 3, Line = "Свадьбу сыграли, и это было громко.", IsMilestone = true },
                new NecrologEntry { Age = 32, Order = 4, Line = "Ребёнка растили как умели.", IsMilestone = true },
                new NecrologEntry { Age = 68, Order = 5, Line = "В шестьдесят восемь внуки научили вас проигрывать в карты." },
            };
            return Necrolog.Build("спокойная старость", entries);
        }

        // The WORST case the finale can ever have to draw: the 14 longest necrolog lines of the live deck
        // + the fixed parents line (= the 15-line cap of scenes-table кол.11–12) under the longest cause.
        // The design gate reads this frame to judge the legibility floor of the best-fit shrink.
        private static NecrologResult LongestRealNecrolog()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            var lines = new System.Collections.Generic.List<string>();
            // РАЗНЫЕ строки: некролог дедуплицирует по тексту (ревью 2026-08-08), и повтор в этом списке
            // просто съел бы строку у худшего случая — кадр перестал бы показывать полную плашку.
            foreach (var c in CardLoader.ParseAll(csv.text))
            {
                if (!string.IsNullOrEmpty(c.YesNecrolog) && !lines.Contains(c.YesNecrolog))
                    lines.Add(c.YesNecrolog);
                if (!string.IsNullOrEmpty(c.NoNecrolog) && !lines.Contains(c.NoNecrolog))
                    lines.Add(c.NoNecrolog);
            }
            lines.Sort((a, b) => b.Length.CompareTo(a.Length));
            var entries = new System.Collections.Generic.List<NecrologEntry>();
            for (int i = 0; i < Necrolog.MaxLines - 1 && i < lines.Count; i++)
                entries.Add(new NecrologEntry { Age = i, Order = i, Line = lines[i], IsRond = false });
            return Necrolog.Build("вы сунули палец в розетку", entries);
        }

        /// <summary>Death age for the finale poses — LIFECHOICES_SHOT_AGE, else the pose's own default.</summary>
        private static int ShotAge(int fallback)
        {
            var raw = Environment.GetEnvironmentVariable("LIFECHOICES_SHOT_AGE");
            return !string.IsNullOrEmpty(raw) && int.TryParse(raw, out var a) ? a : fallback;
        }

        [UnityTest]
        public IEnumerator Capture_Arcade_Frame_NoMagenta()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            var fake = new PlayFakeInputSource();
            driver.Input = fake;

            // Снимаем ровно то, что видит РАЗРАБОТЧИК/ОСНОВАТЕЛЬНИЦА без плат: клавиатурная эмуляция
            // жива, значит служебные строки подсказки клавиш (плейтест 2026-08-05 §2) на кадре ЕСТЬ.
            // Иначе кадр врал бы — в тестах ArcadeInput никем не инициализирован, и подсказки пусты.
            ArcadeInput.Initialize(new KeyboardBackend(KeyboardMapping.LoadDefault()));
            yield return null;                       // Start builds/subscribes

            // Which frame to capture. Default = the representative card+scales pose; the others exist so the
            // design gate can look at the states the ordinary frame cannot show (LIFECHOICES_SHOT_POSE).
            switch ((Environment.GetEnvironmentVariable("LIFECHOICES_SHOT_POSE") ?? "arcade").ToLowerInvariant())
            {
                case "blocked":
                    driver.DebugPreviewBlocked();
                    driver.CardRect.Find("CardText").GetComponent<UnityEngine.UI.Text>().text =
                        "Ваш ребёнок вырос и больше не нуждается в помощи. Помочь всё равно?";
                    break;
                // §5b: трубка ребёнка — поза ЗВОНКА (выехала, дуги запечены) и поза ПОКОЯ (за левым краем).
                case "phonering": driver.DebugPreviewChildCall(); break;
                case "phonerest": driver.DebugPreviewChildPhoneRest(); break;
                // §5a: тот же обычный кадр, но купол-таймер в ПОСЛЕДНЕЙ секунде — дуга почти истекла
                // и горит RED_BRIGHT (состояние, которого спокойный кадр не показывает).
                case "domelast": driver.DebugPreviewDomeLastSecond(); break;
                // …и четверть окна — граница заливки под 45°, худший случай для лесенки: на этих двух
                // позах гейт смотрит сглаживание края (стрелка-кромка) на зуме, в жёлтой и в красной фазе.
                case "domediag": driver.DebugPreviewDomeDiagonalEdge(alarm: false); break;
                case "domediagalarm": driver.DebugPreviewDomeDiagonalEdge(alarm: true); break;
                // §4: все четыре шкалы в КРАСНОЙ ТРЕВОГЕ, пульс на пике — кадр сверяется с эталоном
                // «Экран подсвечена красным шкала.png» (батарея целиком красная).
                case "alarm": driver.DebugPreviewAlarms(); break;
                // §6: салют звёзд, пойманный на середине разлёта.
                case "stars": driver.DebugPreviewStarBurst(); break;
                // §D: модальный экран появления новой шкалы — по одному кадру на каждую из четырёх шкал
                // (сверяется с эталоном «Экран - появление новой шкалы.png»; эталон нарисован для энергии).
                case "tutmoney": driver.DebugPreviewNewScale(NewScale.Money); break;
                case "tutrel": driver.DebugPreviewNewScale(NewScale.Relations); break;
                case "tutenergy": driver.DebugPreviewNewScale(NewScale.Energy); break;
                case "tutchild": driver.DebugPreviewNewScale(NewScale.Child); break;
                case "host": driver.DebugPreviewHostComment(); break;
                // r3: ВХОДНЫЕ ЭКРАНЫ СПЕЦРЕЖИМОВ — по кадру на каждый (здоровье / блиц / депрессия /
                // первое выгорание). Собраны из тех же блоков §D + зелёная CTA.
                case "tuthealth": driver.DebugPreviewSpecialMode(SpecialMode.Health); break;
                case "tutblitz": driver.DebugPreviewSpecialMode(SpecialMode.Blitz); break;
                case "tutdepression": driver.DebugPreviewSpecialMode(SpecialMode.Depression); break;
                case "tutburnout": driver.DebugPreviewSpecialMode(SpecialMode.Burnout); break;
                // r3 §5б: повторное выгорание — КОРОТКАЯ плашка, доска под ней видна целиком.
                case "burnout": driver.DebugPreviewBurnout(); break;
                // r3 §9: пропущенный звонок — трубка уезжает ПОНИКШЕЙ + реплика Ведущего.
                case "phonemissed": driver.DebugPreviewChildPhoneMissed(); break;
                // Веха-TIMELINE в обычном ходу: жёлтая рубрика-баннер снята (плейтест 2026-08-05 §3),
                // поэтому веха выглядит РОВНО как любая другая карточка — этот кадр и показывает.
                case "milestone":
                    driver.DebugPreviewArcadeShot();
                    driver.CardRect.Find("CardText").GetComponent<UnityEngine.UI.Text>().text =
                        "Первая любовь. Признаться ей?";
                    break;
                case "opener":
                    // S1 as the player meets it: the driver already boots into the opener, so the pose is
                    // «touch nothing». Freeze Update so the spinning rays land on a deterministic angle.
                    driver.enabled = false;
                    break;
                case "finale":
                    // S11: the payoff screen (end.png + text in the baked plate) with an ordinary death —
                    // the design gate reads the seating against `explainers/Экран концовка.png`.
                    // LIFECHOICES_SHOT_AGE overrides the death age, so the gate can also LOOK at the other
                    // branch of the genitive-after-«до» rule («до 41 года» vs the default «до 78 лет»).
                    driver.DebugRenderFinale(SampleNecrolog(), ShotAge(78));
                    driver.enabled = false;
                    break;
                case "finalelong":
                    // …and the same screen with the LONGEST necrolog the deck can produce (15-line cap):
                    // the frame the legibility floor of the best-fit shrink is judged on.
                    driver.DebugRenderFinale(LongestRealNecrolog(), 100);
                    driver.enabled = false;
                    break;
                default: driver.DebugPreviewArcadeShot(); break;
            }
            // Optional energy override, so the design gate can look at the lightning layer against a cream,
            // a half and a full cavity (founder canon §12-3 asks for 10/50/90).
            var energy = Environment.GetEnvironmentVariable("LIFECHOICES_SHOT_ENERGY");
            if (!string.IsNullOrEmpty(energy) && float.TryParse(energy, out var e))
                driver.DebugReflectScales(e, 72f, 58f);
            // …and the whole triple «энергия,здоровье,отношения», so the design gate can look at the bar
            // markers at the ENDS of their scales (0 and 100) — the states an ordinary frame never shows and
            // the ones where a marker used to collide with the bar's own end decorations.
            var scales = Environment.GetEnvironmentVariable("LIFECHOICES_SHOT_SCALES");
            if (!string.IsNullOrEmpty(scales))
            {
                var p = scales.Split(',');
                if (p.Length == 3 && float.TryParse(p[0], out var se) && float.TryParse(p[1], out var sh)
                    && float.TryParse(p[2], out var sr))
                    driver.DebugReflectScales(se, sh, sr, relRedZone: sr > 75f);
            }
            yield return null;                       // let the Canvas rebuild its meshes for the posed state

            // Render the ScreenSpaceOverlay HUD deterministically via an offscreen camera → RenderTexture,
            // independent of the batch Screen size.
            var canvas = driver.CanvasRect.GetComponent<Canvas>();
            var camGo = new GameObject("ShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.14f, 1f);
            cam.cullingMask = ~0;

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 100f;

            // The capture MUST run the canvas at exactly 1920×1080 reference px. Otherwise the CanvasScaler
            // keeps deriving its scale from the batch game view (not 16:9 — the canvas comes out ≈1664×1248,
            // scaleFactor ≈0.62), and uGUI rasterises every DYNAMIC-FONT glyph at that 0.62× size before the
            // frame is blown back up to the 1920×1080 render texture: the art stays crisp (it is a texture)
            // while all generated text goes soft — the «замылен» the design gate measured (stroke edges 6–8 px
            // against 1–2 px for the art). The game itself is fine at 1920×1080; it was the HARNESS lying.
            // ConstantPixelSize + scaleFactor 1 pins canvas.rect to the camera's 1920×1080 pixel rect.
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            yield return null;                       // let the canvas adopt camera-space layout
            // Changing the canvas scale does NOT by itself invalidate a Text's glyph request: the dynamic
            // font atlas still holds the entries rasterised at the old (small) size, and the mesh would be
            // drawn from those upscaled. Dirty every Text so each re-requests its glyphs at the new 1:1 size.
            foreach (var t in driver.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                t.FontTextureChanged();
                t.SetAllDirty();
            }
            Canvas.ForceUpdateCanvases();
            yield return null;                       // …and let the dynamic font atlas re-raster at 1:1
            Canvas.ForceUpdateCanvases();
            yield return null;

            Assert.AreEqual(W, driver.CanvasRect.rect.width, 1f,
                "the capture canvas really is 1920 reference px wide (else the glyphs rasterise scaled)");
            Assert.AreEqual(H, driver.CanvasRect.rect.height, 1f,
                "the capture canvas really is 1080 reference px tall");

            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var pixels = tex.GetPixels32();
            int magenta = 0;
            int nonBackground = 0;
            var bg = (Color32)cam.backgroundColor;
            foreach (var p in pixels)
            {
                if (p.r > 230 && p.g < 40 && p.b > 230) magenta++;
                if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) > 40) nonBackground++;
            }

            string path = Environment.GetEnvironmentVariable("LIFECHOICES_SHOT_PATH");
            if (string.IsNullOrEmpty(path)) path = Path.Combine(Path.GetTempPath(), "lifechoices-arcade.png");
            var png = tex.EncodeToPNG();
            Assert.IsNotNull(png, "encoded a PNG");
            File.WriteAllBytes(path, png);
            Debug.Log($"[arcade-screenshot] wrote {png.Length} bytes to {path}; " +
                      $"magenta={magenta}px, nonBackground={nonBackground}px of {pixels.Length}");

            Assert.AreEqual(0, magenta, "the arcade frame must contain ZERO magenta (no missing shaders/sprites)");
            Assert.Greater(nonBackground, pixels.Length / 100,
                "the frame is not blank (HUD content rendered)");

            // cleanup
            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(go);
            yield return null;
        }
    }
}
