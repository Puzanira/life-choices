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

        [UnityTest]
        public IEnumerator Capture_Arcade_Frame_NoMagenta()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            var fake = new PlayFakeInputSource();
            driver.Input = fake;
            yield return null;                       // Start builds/subscribes

            driver.DebugPreviewArcadeShot();         // pose a representative card+scales frame, freeze driver
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
            yield return null;                       // let the canvas adopt camera-space layout

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
