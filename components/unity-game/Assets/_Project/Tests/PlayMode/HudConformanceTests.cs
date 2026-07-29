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
            // §9 canon: RED «СПАСИБО, НЕ НАДО» LEFT (432,872), GREEN «ДА» RIGHT (1547,871) — the cabinet-lever
            // layout, replacing the old green-left/red-right anchors. Since the baked art-pack plates landed,
            // the rect is the art's own box (562×271 / 390×256 — the reference scale of `btn-no`/`btn-yes`,
            // see BakedNo/YesPlateSize), not the old code-plate boxes. The red centre is 432, NOT the §B box
            // centre 472.5: on the reference explainer the art sits flush with the box's LEFT edge (drawn
            // plate x≈169..693) — Maintainer canon call, the explainer PNG outranks the spec table.
            AssertAt(canvas, driver.NoPlateImage.rectTransform,             432f, 872f, 562f, 271f, "СПАСИБО НЕ НАДО plate (left)");
            AssertAt(canvas, driver.YesPlateImage.rectTransform,           1547f, 871f, 390f, 256f, "ДА plate (right)");

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
                "btn-yes", "btn-no",                                 // answer plates (baked art, §9)
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

        // Assert the actual GENERATED glyph mesh (best-fit honoured) of `t` sits inside `plate`'s rect shrunk
        // by `pill` on all four sides — reads the drawn verts, so a vertical spill past the marquee fails even
        // while a rect-corner check stays green. (Local copy of the ScreensConformanceTests helper.)
        private static void AssertGeneratedInPill(Text t, Graphic plate, float pill, string what)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, what + " renders glyphs (not empty/tofu-collapsed)");

            float upp = 1f / t.pixelsPerUnit;
            float lMinX = float.MaxValue, lMaxX = float.MinValue, lMinY = float.MaxValue, lMaxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                float x = p.x * upp, y = p.y * upp;
                if (x < lMinX) lMinX = x; if (x > lMaxX) lMaxX = x;
                if (y < lMinY) lMinY = y; if (y > lMaxY) lMaxY = y;
            }

            var tr = t.rectTransform;
            var plateRt = plate.rectTransform;
            var corners = new[]
            {
                new Vector3(lMinX, lMinY, 0f), new Vector3(lMinX, lMaxY, 0f),
                new Vector3(lMaxX, lMinY, 0f), new Vector3(lMaxX, lMaxY, 0f),
            };
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                var pl = plateRt.InverseTransformPoint(tr.TransformPoint(c));
                if (pl.x < minX) minX = pl.x; if (pl.x > maxX) maxX = pl.x;
                if (pl.y < minY) minY = pl.y; if (pl.y > maxY) maxY = pl.y;
            }
            var pr = plateRt.rect;
            const float tol = 1f;
            Assert.GreaterOrEqual(minX, pr.xMin + pill - tol, what + " drawn glyphs within the marquee (left)");
            Assert.LessOrEqual(maxX, pr.xMax - pill + tol, what + " drawn glyphs within the marquee (right)");
            Assert.GreaterOrEqual(minY, pr.yMin + pill - tol, what + " drawn glyphs within the marquee (bottom)");
            Assert.LessOrEqual(maxY, pr.yMax - pill + tol, what + " drawn glyphs within the marquee (top)");
        }

        // The deck's LONGEST question (67 chars) must best-fit ENTIRELY inside the card marquee — before the
        // S15 fix it rendered too big and spilled above the top bulbs / below the bottom edge (best-fit only
        // honoured width until _cardText.verticalOverflow was set to Truncate).
        [UnityTest]
        public IEnumerator CardMarquee_LongestQuestion_FitsInsideTheCard()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            var cardText = driver.CardRect.Find("CardText").GetComponent<Text>();
            // The two longest real deck questions are 67/66 chars; use the longest verbatim.
            cardText.text = "Ваш ребёнок вырос и больше не нуждается в помощи. Помочь всё равно?";
            yield return null;                               // best-fit + layout settle
            yield return null;

            // The text rect is inset 120 from the card root; assert the drawn glyphs stay inside that interior
            // (well within the bulb border) — pill = 120 against the card frame.
            AssertGeneratedInPill(cardText, driver.CardFrameImage, 120f, "longest card question");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ §9 · buttons swapped (visual-foundation)

        // The plate sprites expose a coloured pill ~55px inside the rect on every side (fixed 9-slice corner).

        /// Signed tilt of a rect in degrees, CCW-positive (Unity z), normalised to (−180,180].
        private static float TiltZ(RectTransform rt) => Mathf.DeltaAngle(0f, rt.localEulerAngles.z);

        // The art-pack plates carry the words BAKED INTO the picture, and the PNGs have a transparent margin:
        // the drawn (alpha-tight) art covers this fraction of the Image rect — measured on the source files
        // btn-no.png 1422×685 → 1377×657 and btn-yes.png 907×594 → 876×577.
        private const float NoArtFracX = 0.9684f, NoArtFracY = 0.9591f;
        private const float YesArtFracX = 0.9658f, YesArtFracY = 0.9714f;
        // The same plates measured on the reference screen «Экран спокойный обычный.png» (colour fill fitted
        // with a min-area rotated bbox, then scaled out to the full art): the DRAWN plate is 544.7×259.9 (red)
        // and 377.1×248.4 (green). This is the «видимая заливка как на эталоне» check — a rect that no longer
        // matches the art's aspect (stretched lettering) or a plate a quarter too small fails it.
        private const float NoArtRefW = 544.7f, NoArtRefH = 259.9f;
        private const float YesArtRefW = 377.1f, YesArtRefH = 248.4f;
        private const float ArtSizeTol = 6f;

        // §9 canon: on EVERY ordinary choice screen the RED «СПАСИБО, НЕ НАДО» is the LEFT plate and the GREEN
        // «ДА» is the RIGHT one — matching the cabinet levers (RedButton→AnswerNo left, GreenButton→AnswerYes
        // right; ArcadeInputSource is untouched, only the screen side moved). Both are the ART-PACK sprites
        // with the lettering baked in, so the dynamic label overlay must be HIDDEN (a live Text on top would
        // double the words). Tilts are the reference's asymmetric pair (red −9°, green +13°, measured by a
        // min-area rotated-bbox fit), and the drawn art matches the reference size.
        [UnityTest]
        public IEnumerator AnswerPlates_RedLeft_GreenRight_BakedArt_ReferenceTiltsAndSizes()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            var no = OwnBounds(canvas, driver.NoPlateImage.rectTransform);
            var yes = OwnBounds(canvas, driver.YesPlateImage.rectTransform);

            // Sides: canvas-local x = 0 is the screen centre (the 960 reference column).
            Assert.Less(no.center.x, 0f, "the RED «СПАСИБО, НЕ НАДО» plate is LEFT of screen centre");
            Assert.Greater(yes.center.x, 0f, "the GREEN «ДА» plate is RIGHT of screen centre");
            Assert.Less(no.center.x, yes.center.x, "red is left of green (never the old green-left layout)");
            Assert.IsFalse(Overlap(no, yes), "the two plates never overlap each other");

            // The art-pack sprites, each on its own side — колор-к-смыслу plus the baked wording.
            Assert.AreEqual("btn-no", driver.NoPlateImage.sprite.name,
                "the left plate is the baked RED «СПАСИБО НЕ НАДО» art (btn-no), not a code plate");
            Assert.AreEqual("btn-yes", driver.YesPlateImage.sprite.name,
                "the right plate is the baked GREEN «ДА» art (btn-yes), not a code plate");
            Assert.AreEqual(Image.Type.Simple, driver.NoPlateImage.type, "baked art draws Simple (not 9-sliced)");
            Assert.AreEqual(Image.Type.Simple, driver.YesPlateImage.type, "baked art draws Simple (not 9-sliced)");

            // The words come from the PICTURE: the dynamic overlay label is hidden in ordinary play.
            Assert.IsFalse(driver.YesPlateText.gameObject.activeInHierarchy,
                "«ДА» is baked into the art — the dynamic label must be hidden (no doubled text)");
            Assert.IsFalse(driver.NoPlateText.gameObject.activeInHierarchy,
                "«СПАСИБО, НЕ НАДО» is baked into the art — the dynamic label must be hidden");
            Assert.IsTrue(driver.YesPlateImage.color.r > 0.9f && driver.YesPlateImage.color.g > 0.9f
                && driver.YesPlateImage.color.b > 0.9f, "the baked art is drawn untinted (white) in normal play");

            // Tilts per the reference — ASYMMETRIC and signed (a mirrored sign is a visible defect).
            Assert.AreEqual(-9f, TiltZ(driver.NoPlateImage.rectTransform), 0.5f,
                "the red LEFT plate leans down-to-the-right (Unity z = −9, reference −9.05)");
            Assert.AreEqual(13f, TiltZ(driver.YesPlateImage.rectTransform), 0.5f,
                "the green RIGHT plate leans up-to-the-right (Unity z = +13, reference +13.15)");

            // Visible (drawn) size == the reference plate, ±6 px — rotation-independent (own rect × art frac).
            var noRect = driver.NoPlateImage.rectTransform.rect;
            var yesRect = driver.YesPlateImage.rectTransform.rect;
            Assert.AreEqual(NoArtRefW, noRect.width * NoArtFracX, ArtSizeTol, "drawn width of the red plate ≈ reference");
            Assert.AreEqual(NoArtRefH, noRect.height * NoArtFracY, ArtSizeTol, "drawn height of the red plate ≈ reference");
            Assert.AreEqual(YesArtRefW, yesRect.width * YesArtFracX, ArtSizeTol, "drawn width of the green plate ≈ reference");
            Assert.AreEqual(YesArtRefH, yesRect.height * YesArtFracY, ArtSizeTol, "drawn height of the green plate ≈ reference");

            Object.Destroy(go);
            yield return null;
        }

        // §8 typography: «основные надписи» (headlines, the card question, the plate labels, the HUD digits)
        // render in Arimo Bold — and that font really carries the Cyrillic + ₽ glyphs they draw (anti-tofu:
        // HasCharacter, not a glyph count, which a replacement box would satisfy).
        [UnityTest]
        public IEnumerator DisplayFont_IsArimoBold_AndCarriesCyrillicAndRouble()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);

            var display = driver.YesPlateText.font;
            Assert.IsNotNull(display, "the display font is loaded (never a null font → all tofu)");
            StringAssert.Contains("Arimo", display.name,
                "«основные надписи» use Arimo Bold (meeting-revisions §8), not Russo One");

            // The HUD digits + card question + headline share that same display font.
            Assert.AreSame(display, driver.NoPlateText.font, "the decline label uses the display font");
            Assert.AreSame(display, driver.CardRect.Find("CardText").GetComponent<Text>().font,
                "the card question uses the display font");
            Assert.AreSame(display, driver.AgeText.font,
                "the age DIGITS use the display font");
            foreach (var t in driver.AgeBadge.GetComponentsInChildren<Text>(includeInactive: true))
                Assert.AreSame(display, t.font,
                    "every age-badge label («" + t.text + "») uses the display font");

            const string needed = "ДЯ₽СПАБОЕНХ0123456789";
            if (display.dynamic) display.RequestCharactersInTexture(needed, 64, FontStyle.Normal);
            foreach (char c in needed)
                Assert.IsTrue(display.HasCharacter(c),
                    "display font «" + display.name + "» has a real glyph for '" + c
                        + "' (U+" + ((int)c).ToString("X4") + ") — not a tofu box");

            // Body copy stays Rubik (a different font asset) — §8 keeps the two roles apart. The Ведущий's
            // bubble is the canonical «комментарий», so assert the bubble's OWN Text, not a stand-in.
            var body = driver.HostBubbleText.font;
            Assert.IsNotNull(body, "the body font is loaded");
            StringAssert.Contains("Rubik", body.name, "облачко Ведущего stays Rubik (§8 «комментарии»)");
            Assert.AreNotSame(display, body, "the body face is a different asset from the display face");
            Assert.AreSame(body, driver.CardPriceText.font, "small copy (BLOCK$ price) shares the body font");

            Object.Destroy(go);
            yield return null;
        }

        // §7 background: the rays are a CENTRED SQUARE big enough to cover the 1920×1080 diagonal at ANY
        // rotation angle (никогда не оголяет углы), and they spin CLOCKWISE at 6°/сек (1 turn / 60 s).
        [UnityTest]
        public IEnumerator Background_CoversTheRotationDiagonal_AndSpinsClockwise()
        {
            var driver = BootToAdult(out var go);
            yield return ToAdult(driver);
            var canvas = driver.CanvasRect;

            var bg = driver.BackgroundImage.rectTransform;
            float diagonal = Mathf.Sqrt(1920f * 1920f + 1080f * 1080f);   // ≈2202.9 reference px
            Assert.GreaterOrEqual(Mathf.Min(bg.rect.width, bg.rect.height), diagonal,
                "the rays square covers the frame diagonal — corners stay filled at every spin angle");
            Assert.AreEqual(bg.rect.width, bg.rect.height, 1f, "the rays backdrop is square");

            // The spin centre = the screen centre: the rect's PIVOT (its rotation centre) is pinned there.
            Assert.AreEqual(0.5f, bg.anchorMin.x, 0.001f, "the rays are anchored to the screen CENTRE (x)");
            Assert.AreEqual(0.5f, bg.anchorMin.y, 0.001f, "the rays are anchored to the screen CENTRE (y)");
            Assert.AreEqual(Vector2.zero, bg.anchoredPosition, "the pivot sits exactly on the screen centre");
            var pivotOnCanvas = canvas.InverseTransformPoint(bg.position);
            Assert.AreEqual(0f, pivotOnCanvas.x, 1f, "the rotation centre is the screen centre (x)");
            Assert.AreEqual(0f, pivotOnCanvas.y, 1f, "the rotation centre is the screen centre (y)");

            // Real coverage guard: EVERY edge is at least the frame's half-diagonal away from that pivot, so
            // no rotation angle can bare a corner (a centre-pivot square alone would not prove this once the
            // pivot moved onto the sprite's ray hub).
            float halfDiag = 0.5f * Mathf.Sqrt(canvas.rect.width * canvas.rect.width
                                             + canvas.rect.height * canvas.rect.height);
            var p = bg.pivot;
            float w2 = bg.rect.width, h2 = bg.rect.height;
            foreach (var (dist, edge) in new[]
            {
                (p.x * w2, "left"), ((1f - p.x) * w2, "right"),
                (p.y * h2, "bottom"), ((1f - p.y) * h2, "top"),
            })
                Assert.GreaterOrEqual(dist, halfDiag,
                    "the rays reach past the frame corner in every direction (" + edge + " edge)");

            // (a) live wiring: consecutive Update frames must actually MOVE the rays, clockwise
            //     (Unity z decreasing). Frame-rate independent — only the sign/motion is asserted here.
            float spin0 = driver.BackgroundSpinDegrees;
            float z0 = bg.localEulerAngles.z;
            yield return null;
            yield return null;
            yield return null;
            float z1 = bg.localEulerAngles.z;
            Assert.Greater(driver.BackgroundSpinDegrees, spin0, "the rays keep spinning frame to frame (Update-driven)");
            Assert.Less(Mathf.DeltaAngle(z0, z1), 0f,
                "the rendered rays turn CLOCKWISE (Unity z decreases) — не против часовой");

            // (b) exact rate through the very same code path, on a deterministic dt: 6°/сек = 1 turn / 60 s.
            float zBefore = bg.localEulerAngles.z;
            driver.DebugSpinBackground(10f);
            Assert.AreEqual(-60f, Mathf.DeltaAngle(zBefore, bg.localEulerAngles.z), 0.05f,
                "10 s of spin = 60° clockwise → 6°/сек, one revolution per 60 s (meeting-revisions §7)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
