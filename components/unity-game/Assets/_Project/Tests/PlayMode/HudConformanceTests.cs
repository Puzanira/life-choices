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
    /// Layer-2 HUD-conformance guards (design gate: styleframe-03 / C2 / INDEX.md anchors). These assert
    /// the RENDERED result — each widget's real RectTransform position + size against the 1920×1080 INDEX
    /// anchors (not activeSelf) — plus the two founder-flagged regressions: the timer ring must NOT overlap
    /// the energy capsule, and the money pill must be the blue cobalt (not white). Also: scale labels sit
    /// BELOW their capsules, the HUD row contains exactly the expected Images (no stray/placeholder), and
    /// key elements stay fully inside the 16:9 frame.
    /// </summary>
    public class HudConformanceTests
    {
        // Cobalt token (#2f54c8) — the money pill fill colour required by the design gate.
        private static readonly Color Cobalt = new(0.184f, 0.329f, 0.784f);

        private static GameDriver BootToAdult(out GameObject go)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            driver.Input = new PlayFakeInputSource();
            return driver;
        }

        // The rect's OWN four corners in the canvas' local space (excludes children — so a label parented
        // below a pill can't pollute the pill's measured bounds). Canvas-local space is the 1920×1080-ish
        // reference space, so sizes read back in reference px regardless of the game-view resolution.
        private static Bounds OwnBounds(RectTransform canvas, RectTransform rt)
        {
            var wc = new Vector3[4];
            rt.GetWorldCorners(wc);
            var b = new Bounds(canvas.InverseTransformPoint(wc[0]), Vector3.zero);
            for (int i = 1; i < 4; i++) b.Encapsulate(canvas.InverseTransformPoint(wc[i]));
            return b;
        }

        // Expected element CENTRE in canvas-local coords from an INDEX anchor (x from left, y from top).
        private static Vector2 ExpectedCenter(RectTransform canvas, float cx, float cyTop)
        {
            float w = canvas.rect.width, h = canvas.rect.height;
            return new Vector2((cx / 1920f - 0.5f) * w, (0.5f - cyTop / 1080f) * h);
        }

        private static void AssertAt(RectTransform canvas, RectTransform rt,
            float cx, float cyTop, float w, float h, string what)
        {
            var b = OwnBounds(canvas, rt);
            var e = ExpectedCenter(canvas, cx, cyTop);
            float posTol = canvas.rect.width * 0.02f;      // ~38px at 1920
            Assert.AreEqual(e.x, b.center.x, posTol, what + " centre-x ≈ INDEX anchor");
            Assert.AreEqual(e.y, b.center.y, posTol, what + " centre-y ≈ INDEX anchor");
            // Size from the element's own local rect — rotation-independent (the answer plates are tilted).
            Assert.AreEqual(w, rt.rect.width, 3f, what + " width ≈ INDEX size");
            Assert.AreEqual(h, rt.rect.height, 3f, what + " height ≈ INDEX size");
        }

        private static bool Overlap(Bounds a, Bounds b)
            => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;

        private static IEnumerator ToAdult(GameDriver driver)
        {
            yield return null;                               // Start wires input + subscriptions
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;                               // enter Playing, first card drawn
            driver.DebugApplyAgeGates(34f);                  // reveal the full adult HUD
            yield return null;                               // let the canvas lay out
            yield return null;
        }

        [UnityTest]
        public IEnumerator HudRow_Widgets_Match_INDEX_Anchors()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            AssertAt(canvas, (RectTransform)driver.AgeBadge.transform,     158f, 142f, 200f, 224f, "age badge");
            AssertAt(canvas, (RectTransform)driver.MoneyPill.transform,    435f, 74f,  330f, 88f,  "money pill");
            AssertAt(canvas, (RectTransform)driver.HealthGroup.transform,  771f, 66f,  290f, 72f,  "health capsule");
            AssertAt(canvas, (RectTransform)driver.EnergyGroup.transform,  1085f, 66f, 290f, 72f,  "energy capsule");
            AssertAt(canvas, (RectTransform)driver.BalancerGroup.transform, 1444f, 66f, 380f, 72f, "relationships capsule");
            AssertAt(canvas, (RectTransform)driver.TimerRingFill.transform.parent, 960f, 250f, 180f, 180f, "timer ring");
            AssertAt(canvas, driver.YesPlateImage.rectTransform,           610f, 940f, 420f, 190f, "ДА plate");
            AssertAt(canvas, driver.NoPlateImage.rectTransform,            1310f, 940f, 470f, 190f, "СПАСИБО НЕ НАДО plate");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TimerRing_DoesNotOverlap_EnergyCapsule()
        {
            // The founder-flagged collision: the ring must live in the gap UNDER the HUD, never over energy.
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            var timer = OwnBounds(canvas, (RectTransform)driver.TimerRingFill.transform.parent);
            var energy = OwnBounds(canvas, (RectTransform)driver.EnergyGroup.transform);
            Assert.IsFalse(Overlap(timer, energy),
                "timer ring rect must be disjoint from the energy capsule rect (no overlap)");
            // And also clear of the whole HUD row (health/relationships), for good measure.
            Assert.IsFalse(Overlap(timer, OwnBounds(canvas, (RectTransform)driver.HealthGroup.transform)),
                "timer ring clear of the health capsule");
            Assert.IsFalse(Overlap(timer, OwnBounds(canvas, (RectTransform)driver.BalancerGroup.transform)),
                "timer ring clear of the relationships capsule");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoneyPill_Is_Cobalt_Not_White()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            var img = driver.MoneyPill.GetComponent<Image>();
            Assert.IsNotNull(img, "money pill has an Image");
            var c = img.color;
            Assert.Greater(c.b, c.r + 0.2f, "money pill is blue (blue channel dominates)");
            Assert.Less(c.r, 0.5f, "money pill is not white/light");
            Assert.AreEqual(Cobalt.r, c.r, 0.05f, "cobalt red channel");
            Assert.AreEqual(Cobalt.g, c.g, 0.05f, "cobalt green channel");
            Assert.AreEqual(Cobalt.b, c.b, 0.05f, "cobalt blue channel");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScaleLabels_Sit_Below_Their_Capsules()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            void Below(RectTransform label, RectTransform capsule, string what)
            {
                float lc = OwnBounds(canvas, label).center.y;
                float cc = OwnBounds(canvas, capsule).center.y;
                Assert.Less(lc, cc, what + " label sits below its element (smaller local-y)");
            }

            Below(driver.MoneyLabel.rectTransform, (RectTransform)driver.MoneyPill.transform, "деньги");
            Below(driver.HealthLabel.rectTransform, (RectTransform)driver.HealthGroup.transform, "здоровье");
            Below(driver.EnergyLabel.rectTransform, (RectTransform)driver.EnergyGroup.transform, "энергия");
            Below(driver.RelLabel.rectTransform, (RectTransform)driver.BalancerGroup.transform, "отношения");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GamePanel_Contains_Exactly_The_Expected_Active_Images()
        {
            // Exhaustive enumeration across the WHOLE game panel (HUD row + timer + card + plates), not just
            // the HUD row — a stray/placeholder/tofu Image anywhere in the game view would fail this.
            var driver = BootToAdult(out var go);
            yield return null;                               // Start wires input + subscriptions
            ((PlayFakeInputSource)driver.Input).Confirm();
            yield return null;                               // enter Playing, first card drawn
            // Reveal the full adult HUD and enumerate SYNCHRONOUSLY (the driver's per-frame ApplyAgeGates
            // re-gates off the live Age=0, so a yield here would re-hide the age-gated widgets).
            driver.DebugApplyAgeGates(34f);

            var actual = driver.GamePanel.GetComponentsInChildren<Image>(includeInactive: false)
                .Select(i => i.sprite != null ? i.sprite.name : "<null>")
                .OrderBy(s => s)
                .ToList();

            var expected = new List<string>
            {
                "age-badge",
                "money-pill", "icon-coin",
                "bar-track", "bar-health-fill", "icon-heart",        // health capsule
                "bar-track", "bar-energy-fill", "icon-lightning",    // energy capsule
                "bar-track", "balancer-track", "balancer-marker",    // relationships capsule
                "timer-ring-track", "timer-ring-track", "timer-ring", "marquee-bulb",  // timer ring layers
                "marquee-frame-bulbs",                               // card
                "plate-yes", "plate-no",                             // answer plates
            }.OrderBy(s => s).ToList();

            CollectionAssert.AreEqual(expected, actual,
                "the adult game panel renders exactly its expected sprites (no stray/placeholder Image)");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Key_Elements_Fully_Inside_The_16by9_Frame()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;
            float halfW = canvas.rect.width / 2f, halfH = canvas.rect.height / 2f;
            float tol = 1.5f;

            void Inside(RectTransform rt, string what)
            {
                var b = OwnBounds(canvas, rt);
                Assert.GreaterOrEqual(b.min.x, -halfW - tol, what + " not clipped by the left edge");
                Assert.LessOrEqual(b.max.x, halfW + tol, what + " not clipped by the right edge");
                Assert.GreaterOrEqual(b.min.y, -halfH - tol, what + " not clipped by the bottom edge");
                Assert.LessOrEqual(b.max.y, halfH + tol, what + " not clipped by the top edge");
            }

            Inside((RectTransform)driver.AgeBadge.transform, "age badge");
            Inside((RectTransform)driver.MoneyPill.transform, "money pill");
            Inside((RectTransform)driver.HealthGroup.transform, "health capsule");
            Inside((RectTransform)driver.EnergyGroup.transform, "energy capsule");
            Inside((RectTransform)driver.BalancerGroup.transform, "relationships capsule");
            Inside((RectTransform)driver.TimerRingFill.transform.parent, "timer ring");
            Inside(driver.CardRect, "card marquee");
            Inside(driver.YesPlateImage.rectTransform, "ДА plate");
            Inside(driver.NoPlateImage.rectTransform, "СПАСИБО НЕ НАДО plate");

            Object.Destroy(go);
            yield return null;
        }
    }
}
