using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Guards the TV-visual assembly: the HUD is not merely "active" — its widgets occupy real,
    /// non-zero rects on the canvas and carry the intended P0 sprites. Also pins the age-gated
    /// reveal (studio lesson: a green test on activeSelf alone can hide an invisible feature).
    /// </summary>
    public class HudVisualTests
    {
        private static GameDriver BootToPlaying(out GameObject go)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>(); // Awake builds HUD + loads deck
            var fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        // Bounds of a widget expressed in the canvas' local space (resolution-independent).
        private static Bounds RelBounds(RectTransform canvas, RectTransform child)
            => RectTransformUtility.CalculateRelativeRectTransformBounds(canvas, child);

        private static void AssertOnCanvas(GameDriver d, RectTransform rt, string what)
        {
            var b = RelBounds(d.CanvasRect, rt);
            Assert.Greater(b.size.x, 1f, what + " has a non-zero width on canvas");
            Assert.Greater(b.size.y, 1f, what + " has a non-zero height on canvas");
            var canvasRect = d.CanvasRect.rect;
            Assert.IsTrue(canvasRect.Contains(new Vector2(b.center.x, b.center.y)),
                what + " centre sits inside the canvas rect " + canvasRect);
        }

        [UnityTest]
        public IEnumerator Playing_HUD_Assembles_With_Sprites_On_Canvas()
        {
            var driver = BootToPlaying(out var go);
            yield return null;                       // Start wires input + subscriptions
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;                       // enter Playing, first card drawn
            yield return null;                       // let CanvasScaler + layout settle

            Assert.AreEqual(GameState.Playing, driver.Game.State);
            Assert.IsTrue(driver.GamePanel.activeSelf, "game panel shown while playing");

            // Background is the sunburst sprite, full-screen.
            Assert.IsNotNull(driver.BackgroundImage.sprite, "background has a sprite");
            Assert.AreEqual("sunburst-bg", driver.BackgroundImage.sprite.name);

            // Card marquee: bulbs-frame sprite, real rect on canvas.
            Assert.IsNotNull(driver.CardFrameImage.sprite, "card frame has a sprite");
            Assert.AreEqual("marquee-frame-bulbs", driver.CardFrameImage.sprite.name);
            AssertOnCanvas(driver, driver.CardRect, "card marquee");

            // Answer plates: correct sprites + tilt, both on canvas.
            Assert.AreEqual("plate-yes", driver.YesPlateImage.sprite.name);
            Assert.AreEqual("plate-no", driver.NoPlateImage.sprite.name);
            AssertOnCanvas(driver, driver.YesPlateImage.rectTransform, "ДА plate");
            AssertOnCanvas(driver, driver.NoPlateImage.rectTransform, "СПАСИБО НЕ НАДО plate");

            // Timer ring is a radial-filled image.
            Assert.AreEqual(Image.Type.Filled, driver.TimerRingFill.type, "timer ring is a filled image");
            Assert.AreEqual(Image.FillMethod.Radial360, driver.TimerRingFill.fillMethod);

            // Age badge always present during play, with a real rect.
            Assert.IsTrue(driver.AgeBadge.activeSelf, "age badge visible during play");
            AssertOnCanvas(driver, (RectTransform)driver.AgeBadge.transform, "age badge");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HUD_Widgets_Reveal_With_Age()
        {
            var driver = BootToPlaying(out var go);
            yield return null;
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;                       // Playing, game panel active
            yield return null;

            // Childhood: only the age badge — money/energy/health/balancer hidden.
            driver.DebugApplyAgeGates(7f);
            Assert.IsTrue(driver.AgeBadge.activeSelf, "childhood keeps the age badge");
            Assert.IsFalse(driver.MoneyPill.activeSelf, "money pill hidden at age 7");
            Assert.IsFalse(driver.BalancerGroup.activeSelf, "balancer hidden at age 7");
            Assert.IsFalse(driver.EnergyGroup.activeSelf, "energy bar hidden at age 7");
            Assert.IsFalse(driver.HealthGroup.activeSelf, "health bar hidden at age 7");

            // Money reveals at 18.
            driver.DebugApplyAgeGates(17f);
            Assert.IsFalse(driver.MoneyPill.activeSelf, "money pill still hidden at 17");
            driver.DebugApplyAgeGates(18f);
            Assert.IsTrue(driver.MoneyPill.activeSelf, "money pill revealed at 18");
            AssertOnCanvas(driver, (RectTransform)driver.MoneyPill.transform, "money pill (revealed)");

            // Balancer at 20, energy at 25, health at 30.
            driver.DebugApplyAgeGates(20f);
            Assert.IsTrue(driver.BalancerGroup.activeSelf, "balancer revealed at 20");
            Assert.IsFalse(driver.EnergyGroup.activeSelf, "energy still hidden at 20");

            driver.DebugApplyAgeGates(25f);
            Assert.IsTrue(driver.EnergyGroup.activeSelf, "energy revealed at 25");
            Assert.IsFalse(driver.HealthGroup.activeSelf, "health still hidden at 25");

            driver.DebugApplyAgeGates(30f);
            Assert.IsTrue(driver.HealthGroup.activeSelf, "health revealed at 30 (full HUD)");
            AssertOnCanvas(driver, (RectTransform)driver.HealthGroup.transform, "health bar (revealed)");

            Object.Destroy(go);
            yield return null;
        }

        private static Image ChildImage(GameObject root, string path)
        {
            var t = path.Length == 0 ? root.transform : root.transform.Find(path);
            Assert.IsNotNull(t, root.name + "/" + path + " exists");
            var img = t.GetComponent<Image>();
            Assert.IsNotNull(img, root.name + "/" + path + " has an Image");
            return img;
        }

        private static void AssertSprite(Image img, string expected, string what)
        {
            Assert.IsNotNull(img.sprite, what + " has a sprite assigned");
            Assert.AreEqual(expected, img.sprite.name, what + " carries the intended P0 sprite");
        }

        [UnityTest]
        public IEnumerator Every_Hud_Widget_Carries_Its_P0_Sprite()
        {
            var driver = BootToPlaying(out var go);
            yield return null;
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;
            yield return null;

            // Full adult HUD so every gated widget is revealed and inspectable.
            driver.DebugApplyAgeGates(34f);

            // Table-driven: every named Resources sprite load in GameDriver's HUD widgets is
            // asserted on its actual Image (a typo in a Resources name or a wrong assignment
            // on any of these would otherwise render blank/wrong while rect checks stay green).
            (Image img, string sprite, string what)[] table =
            {
                (ChildImage(driver.AgeBadge, ""),                "age-badge",       "age badge"),
                (ChildImage(driver.MoneyPill, ""),               "money-pill",      "money pill"),
                (ChildImage(driver.MoneyPill, "Coin"),           "icon-coin",       "money coin icon"),
                (ChildImage(driver.HealthGroup, "Icon"),         "icon-heart",      "health icon"),
                (ChildImage(driver.HealthGroup, "Track"),        "bar-track",       "health track"),
                (ChildImage(driver.HealthGroup, "Track/Fill"),   "bar-health-fill", "health fill"),
                (ChildImage(driver.EnergyGroup, "Icon"),         "icon-lightning",  "energy icon"),
                (ChildImage(driver.EnergyGroup, "Track"),        "bar-track",       "energy track"),
                (ChildImage(driver.EnergyGroup, "Track/Fill"),   "bar-energy-fill", "energy fill"),
                (ChildImage(driver.BalancerGroup, "Track"),      "balancer-track",  "balancer track"),
                (ChildImage(driver.BalancerGroup, "Track/Marker"), "balancer-marker", "balancer marker"),
                (driver.TimerRingFill,                           "timer-ring",      "timer ring fill"),
                (ChildImage(driver.TimerRingFill.transform.parent.gameObject, "RingTrack"),
                                                                 "timer-ring-track", "timer ring track"),
            };
            foreach (var row in table)
                AssertSprite(row.img, row.sprite, row.what);

            Object.Destroy(go);
            yield return null;
        }
    }
}
