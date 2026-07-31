using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using ThanksNoThanks;
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

        // A representative finished life for the «finale» pose — a handful of real necrolog lines + a cause.
        private static NecrologResult SampleNecrolog()
        {
            var entries = new System.Collections.Generic.List<NecrologEntry>
            {
                new NecrologEntry { Age = 7,  Order = 0, Line = "В семь лет вы завели рыжего кота и назвали его Борщ.", IsRond = false },
                new NecrologEntry { Age = 24, Order = 1, Line = "В двадцать четыре вы уехали в другой город и ни разу не пожалели.", IsRond = false },
                new NecrologEntry { Age = 41, Order = 2, Line = "К сорока одному у вас была работа, которую вы почти любили.", IsRond = false },
                new NecrologEntry { Age = 68, Order = 3, Line = "В шестьдесят восемь внуки научили вас проигрывать в карты.", IsRond = false },
            };
            return Necrolog.Build("спокойная старость", entries);
        }

        [UnityTest]
        public IEnumerator Capture_Arcade_Frame_NoMagenta()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            var fake = new PlayFakeInputSource();
            driver.Input = fake;
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
                case "childflash": driver.DebugPreviewChildFlash(); break;
                // §5a: тот же обычный кадр, но купол-таймер в ПОСЛЕДНЕЙ секунде — дуга почти истекла
                // и горит RED_BRIGHT (состояние, которого спокойный кадр не показывает).
                case "domelast": driver.DebugPreviewDomeLastSecond(); break;
                // …и четверть окна — граница заливки под 45°, худший случай для лесенки: на этих двух
                // позах гейт смотрит сглаживание края (стрелка-кромка) на зуме, в жёлтой и в красной фазе.
                case "domediag": driver.DebugPreviewDomeDiagonalEdge(alarm: false); break;
                case "domediagalarm": driver.DebugPreviewDomeDiagonalEdge(alarm: true); break;
                case "host": driver.DebugPreviewHostComment(); break;
                case "opener":
                    // S1 as the player meets it: the driver already boots into the opener, so the pose is
                    // «touch nothing». Freeze Update so the spinning rays land on a deterministic angle.
                    driver.enabled = false;
                    break;
                case "finale":
                    // S11: the payoff screen with a real necrolog, so the design gate can read the
                    // «НАЧАТЬ ЗАНОВО — ЖМИ ЗЕЛЁНУЮ» restart CTA against the founder's control language.
                    driver.DebugRenderFinale(SampleNecrolog());
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
