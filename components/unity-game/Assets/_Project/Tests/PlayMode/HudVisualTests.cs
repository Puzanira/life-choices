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

            // Background is the art-pack sunburst (core patched out), oversized behind everything.
            Assert.IsNotNull(driver.BackgroundImage.sprite, "background has a sprite");
            Assert.AreEqual("sunburst-bg-v3", driver.BackgroundImage.sprite.name);

            // Card: the art-pack cream plate, real rect on canvas.
            Assert.IsNotNull(driver.CardFrameImage.sprite, "card frame has a sprite");
            Assert.AreEqual("choice-plate-v2", driver.CardFrameImage.sprite.name);
            AssertOnCanvas(driver, driver.CardRect, "card plate");

            // Answer plates: correct sprites + tilt, both on canvas.
            Assert.AreEqual("btn-yes", driver.YesPlateImage.sprite.name);   // §9 baked art
            Assert.AreEqual("btn-no", driver.NoPlateImage.sprite.name);
            AssertOnCanvas(driver, driver.YesPlateImage.rectTransform, "ДА plate");
            AssertOnCanvas(driver, driver.NoPlateImage.rectTransform, "СПАСИБО НЕ НАДО plate");

            // Купол-таймер: дуга — Radial180-заливка от плоской (верхней) грани полукруга.
            Assert.AreEqual(Image.Type.Filled, driver.TimerDomeFill.type, "дуга купола — filled image");
            Assert.AreEqual(Image.FillMethod.Radial180, driver.TimerDomeFill.fillMethod);
            Assert.AreEqual((int)Image.Origin180.Top, driver.TimerDomeFill.fillOrigin,
                "развёртка идёт от верхней грани — ось проходит через центр окружности купола");

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
            Assert.IsFalse(driver.MoneyJar.activeSelf, "money jar hidden at age 7");
            Assert.IsFalse(driver.BalancerGroup.activeSelf, "balancer hidden at age 7");
            Assert.IsFalse(driver.EnergyGroup.activeSelf, "battery hidden at age 7");
            Assert.IsFalse(driver.HealthGroup.activeSelf, "health bar hidden at age 7");

            // Money reveals at 18.
            driver.DebugApplyAgeGates(17f);
            Assert.IsFalse(driver.MoneyJar.activeSelf, "money jar still hidden at 17");
            driver.DebugApplyAgeGates(18f);
            Assert.IsTrue(driver.MoneyJar.activeSelf, "money jar revealed at 18");
            AssertOnCanvas(driver, driver.MoneyJarImage.rectTransform, "money jar (revealed)");

            // Balancer at 20, energy at 25, health at 30.
            driver.DebugApplyAgeGates(20f);
            Assert.IsTrue(driver.BalancerGroup.activeSelf, "balancer revealed at 20");
            Assert.IsFalse(driver.EnergyGroup.activeSelf, "battery still hidden at 20");

            driver.DebugApplyAgeGates(25f);
            Assert.IsTrue(driver.EnergyGroup.activeSelf, "battery revealed at 25");
            Assert.IsFalse(driver.HealthGroup.activeSelf, "health still hidden at 25");

            driver.DebugApplyAgeGates(30f);
            Assert.IsTrue(driver.HealthGroup.activeSelf, "health revealed at 30 (full HUD)");
            AssertOnCanvas(driver, driver.HealthBarImage.rectTransform, "health bar (revealed)");

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
                (ChildImage(driver.AgeBadge, ""),                    "age-badge-v2",      "age badge"),
                (ChildImage(driver.MoneyJar, "Jar"),                 "money-jar-v2",      "money jar"),
                (ChildImage(driver.MoneyJar, "Coin"),                "coin-v2",           "money coin"),
                (ChildImage(driver.HealthGroup, "HealthBar"),        "health-bar-v2",     "health bar"),
                (ChildImage(driver.HealthGroup, "Marker"),           "health-marker-v2",  "health marker"),
                (ChildImage(driver.EnergyGroup, "Battery"),          "energy-battery-v2", "battery"),
                (ChildImage(driver.BalancerGroup, "RelBar"),         "rel-bar-v2",        "relationships bar"),
                (ChildImage(driver.BalancerGroup, "Marker"),         "rel-marker-heart-v2", "relationships marker"),
                (driver.TimerDomeFill,                               "timer-dome",        "дуга купола"),
                (driver.TimerDomeTrack,                              "timer-dome",        "трек купола"),
                (driver.TimerDomeOutline,                            "timer-dome",        "обводка купола"),
                (driver.TimerDomeHand,                               "timer-dome-hand",   "стрелка-кромка купола"),
            };
            foreach (var row in table)
                AssertSprite(row.img, row.sprite, row.what);

            // The battery's live level is drawn as two flat rects over the cavity (no sprite by design):
            // assert they exist and carry NO sprite, so a future «give it a sprite» change is a conscious one.
            Assert.IsNull(driver.EnergyEmpty.sprite, "the battery's cream «empty» rect is a flat fill");
            Assert.IsNull(driver.EnergyTopUp.sprite, "the battery's yellow top-up rect is a flat fill");

            Object.Destroy(go);
            yield return null;
        }
    }
}
