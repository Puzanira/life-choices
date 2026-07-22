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
    /// Layer-2 conformance for the four STATE-overlay screens (design gate: S7 burnout, S8 depression,
    /// S9 child flash, S10 BLOCK$). Asserts the RENDERED result, not activeSelf flags:
    ///  • S7: the big «ВЫГОРАНИЕ!» title + the white subtitle both draw fully on-screen (never clipped by the
    ///    16:9 edge) and their glyph meshes sit inside the full-screen cobalt backing; no tofu on «—•ё»;
    ///  • S8: «нажми в такт пульсу» sits on a DARK plate with LIGHT text (the founder-reported readability
    ///    fix) and its drawn glyphs fit the plate pill; «СОБРАТЬСЯ» fits its light pill; the wash is really
    ///    desaturated (saturation≈0); the colour-progress pips are gone (mockup has none);
    ///  • S9: the LIT child button's world-rect is DISJOINT from the «СПАСИБО, НЕ НАДО» plate (the founder-
    ///    reported overlap fix) — same GetWorldCorners no-overlap pattern as the HUD timer↔energy guard;
    ///  • S10: the «цена N ₽» line (Rubik → real ₽ glyph) fits its dark pill, the red banner «Как жаль…»
    ///    fits its pill, and both answer plates are muted while the card is blocked.
    /// The visible pill = the plate rect shrunk by the sprite's 9-slice corner inset (NOT the raw text rect).
    /// Geometric + deterministic (stable headless).
    /// </summary>
    public class StateOverlaysConformanceTests
    {
        private const float BarTrackPill = 16f;  // bar-track rounded rect — only the corners are cut

        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        // ---- shared render helpers (mirror ScreensConformanceTests; kept local so the two files stay
        // independent) ----------------------------------------------------------------------------------

        private static int VisibleGlyphs(Text t)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            return tg.characterCountVisible;
        }

        private static void AssertNoTofu(Text t, string what)
        {
            var font = t.font;
            Assert.IsNotNull(font, what + " has a Font assigned (else every glyph is tofu)");
            var s = t.text;
            if (font.dynamic)
                font.RequestCharactersInTexture(s, t.fontSize, t.fontStyle);
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                Assert.IsTrue(font.HasCharacter(c),
                    what + " font «" + font.name + "» has a real glyph for '" + c
                        + "' (U+" + ((int)c).ToString("X4") + ") — not a tofu box");
            }
        }

        // Assert the actual GENERATED glyph mesh (best-fit honoured) sits inside the plate's visible pill on
        // all four sides — NOT merely the Text RectTransform. Reads the drawn verts so a vertical/horizontal
        // spill past the pill fails even while a rect-corner check stays green.
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
            Assert.GreaterOrEqual(minX, pr.xMin + pill - tol, what + " drawn glyphs within the visible pill (left)");
            Assert.LessOrEqual(maxX, pr.xMax - pill + tol, what + " drawn glyphs within the visible pill (right)");
            Assert.GreaterOrEqual(minY, pr.yMin + pill - tol, what + " drawn glyphs within the visible pill (bottom)");
            Assert.LessOrEqual(maxY, pr.yMax - pill + tol, what + " drawn glyphs within the visible pill (top)");
        }

        private static void AssertOnScreen(Text t, RectTransform canvas, string what)
        {
            Assert.Greater(VisibleGlyphs(t), 0, what + " renders glyphs");
            var tr = t.rectTransform;
            var tc = new Vector3[4];
            tr.GetWorldCorners(tc);
            var cr = canvas.rect;
            const float tol = 1f;
            for (int i = 0; i < 4; i++)
            {
                var p = canvas.InverseTransformPoint(tc[i]);
                Assert.GreaterOrEqual(p.x, cr.xMin - tol, what + " within the screen (left)");
                Assert.LessOrEqual(p.x, cr.xMax + tol, what + " within the screen (right)");
                Assert.GreaterOrEqual(p.y, cr.yMin - tol, what + " within the screen (bottom)");
                Assert.LessOrEqual(p.y, cr.yMax + tol, what + " within the screen (top)");
            }
        }

        // Exhaustive active-Text enumeration: the set of active, non-empty Text object NAMES under `root` must
        // equal `expected` EXACTLY (a stray/placeholder label fails), and every one renders real glyphs (no tofu).
        private static void AssertExactTexts(GameObject root, string what, params string[] expected)
        {
            var texts = root.GetComponentsInChildren<Text>(includeInactive: false)
                .Where(t => !string.IsNullOrEmpty(t.text)).ToList();
            CollectionAssert.AreEquivalent(expected, texts.Select(t => t.name).ToList(),
                what + " renders exactly its expected Text labels — no stray/placeholder text (got: "
                    + string.Join(", ", texts.Select(t => t.name)) + ")");
            foreach (var t in texts)
            {
                Assert.Greater(VisibleGlyphs(t), 0, what + " label «" + t.name + "» renders glyphs (not tofu)");
                AssertNoTofu(t, what + " label «" + t.name + "»");
            }
        }

        // Exhaustive active-Image enumeration: the set of active Image object NAMES under `root` must equal
        // `expected` EXACTLY — a stray/placeholder Image (leaked overlay, orphan sprite) fails the state.
        private static void AssertExactImages(GameObject root, string what, params string[] expected)
        {
            var imgs = root.GetComponentsInChildren<Image>(includeInactive: false).Select(i => i.name).ToList();
            CollectionAssert.AreEquivalent(expected, imgs,
                what + " renders exactly its expected Images — no stray/placeholder sprite (got: "
                    + string.Join(", ", imgs) + ")");
        }

        // World-space AABB of a RectTransform, and an overlap test (screen-overlay canvas → pixel space).
        private static Rect WorldAabb(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                if (c[i].x < minX) minX = c[i].x; if (c[i].x > maxX) maxX = c[i].x;
                if (c[i].y < minY) minY = c[i].y; if (c[i].y > maxY) maxY = c[i].y;
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static bool Overlap(Rect a, Rect b)
            => a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;

        // ============================================================ S7 burnout

        [UnityTest]
        public IEnumerator Burnout_TitleAndSubtitle_OnScreen_InBacking_NoTofu()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            driver.GamePanel.SetActive(true);
            driver.BurnoutPlate.SetActive(true);
            var plate = driver.BurnoutPlate;
            var backing = plate.GetComponent<Image>();

            var title = plate.transform.Find("BurnoutText").GetComponent<Text>();
            var sub = plate.transform.Find("BurnoutSubtitle").GetComponent<Text>();
            StringAssert.Contains("ВЫГОРАНИЕ", title.text, "the S7 title reads «ВЫГОРАНИЕ!»");
            StringAssert.Contains("подышите", sub.text, "the S7 subtitle reads the mockup line");

            // Both draw inside the full-screen cobalt backing AND fully on-screen (never clipped by the edge).
            AssertGeneratedInPill(title, backing, 0f, "burnout title on the full-screen backing");
            AssertGeneratedInPill(sub, backing, 0f, "burnout subtitle on the full-screen backing");
            AssertOnScreen(title, driver.CanvasRect, "burnout title");
            AssertOnScreen(sub, driver.CanvasRect, "burnout subtitle");

            // Real glyphs for «—», «•», «ё» — the subtitle would tofu on a font lacking them.
            AssertNoTofu(title, "burnout title");
            AssertNoTofu(sub, "burnout subtitle");

            // Exhaustive: exactly the backing + cobalt rays, and exactly the title + subtitle — no stray element.
            AssertExactImages(plate, "burnout", "BurnoutPlate", "BurnoutRays");
            AssertExactTexts(plate, "burnout", "BurnoutText", "BurnoutSubtitle");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ S8 depression

        [UnityTest]
        public IEnumerator Depression_HintReadableOnDarkPlate_GatherInPill_WashGray_NoPips()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            var group = driver.DepressionOverlay;
            group.SetActive(true);

            var labelPlate = group.transform.Find("DepGatherPlate").GetComponent<Image>();
            var label = group.transform.Find("DepGatherPlate/DepLabel").GetComponent<Text>();
            var hintPlate = group.transform.Find("DepHintPlate").GetComponent<Image>();
            var hint = group.transform.Find("DepHintPlate/DepHint").GetComponent<Text>();

            Assert.AreEqual("СОБРАТЬСЯ", label.text, "the S8 button reads «СОБРАТЬСЯ»");
            Assert.AreEqual("нажми в такт пульсу", hint.text, "the S8 hint reads «нажми в такт пульсу»");

            // (founder complaint) the hint must be READABLE: a DARK plate behind LIGHT text — not gray-on-gray.
            var hc = hintPlate.color;
            Assert.Less(Mathf.Max(hc.r, Mathf.Max(hc.g, hc.b)), 0.2f, "the hint plate is a dark ink plate");
            var htc = hint.color;
            Assert.Greater(Mathf.Min(htc.r, Mathf.Min(htc.g, htc.b)), 0.8f, "the hint text is light (high contrast on the dark plate)");

            // …and both labels' drawn glyphs sit inside their pills (nothing spills off the plate).
            AssertGeneratedInPill(hint, hintPlate, BarTrackPill, "«нажми в такт пульсу» on its dark plate");
            AssertGeneratedInPill(label, labelPlate, BarTrackPill, "«СОБРАТЬСЯ» on its light pill");
            AssertNoTofu(hint, "depression hint");
            AssertNoTofu(label, "depression gather");

            // «СОБРАТЬСЯ» pill is near-white (monochrome, dark text reads on it).
            var gc = labelPlate.color;
            Assert.Greater(Mathf.Min(gc.r, Mathf.Min(gc.g, gc.b)), 0.8f, "the «СОБРАТЬСЯ» pill is near-white");

            // The B&W wash is really DESATURATED (saturation ≈ 0), not a tinted veil.
            Color.RGBToHSV(driver.DepressionVeil.color, out _, out float sat, out _);
            Assert.Less(sat, 0.1f, "the depression wash is desaturated (grayscale)");

            // The 5-step colour-progress pips are gone (S8 mockup has none) — a regression re-adding them fails.
            Assert.IsNull(group.transform.Find("DepPip0"), "no colour-progress pips (S8 mockup has none)");

            // Exhaustive: exactly the wash + grain + the two plates (pulse is inactive here), and exactly the
            // two labels — no stray element / re-added pip / placeholder.
            AssertExactImages(group, "depression",
                "DepressionVeil", "DepressionGrain", "DepGatherPlate", "DepHintPlate");
            AssertExactTexts(group, "depression", "DepLabel", "DepHint");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ S9 child flash — overlap guard

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        private static Game ChildDeck()
        {
            var deck = new List<Card> { Starter(), Plain("MD02", 21) };
            for (int a = 22; a <= 120; a++) deck.Add(Plain("F" + a, a));
            return new Game(deck, coin: () => false) { ChildFlashInterval = () => 2f };
        }

        [UnityTest]
        public IEnumerator ChildFlash_LitButton_DoesNotOverlap_SpasiboNeNado_Plate()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(ChildDeck());

            fake.Confirm();                              // opener → playing (starter drawn)
            fake.No();                                   // resolve I03 → MD02 current
            fake.Yes();                                  // MD02=ДА opens the child (+ S5 hint)
            if (driver.TutorialShowing) fake.Confirm();  // dismiss the child hint
            yield return null;

            // Drive to an open flash window (dismissing age-gate hints along the way).
            int guard = 0;
            while (!driver.Game.ChildFlashing && driver.Game.State == GameState.Playing && guard++ < 600)
            {
                if (driver.TutorialShowing) fake.Confirm();
                else driver.Game.Tick(0.1f);
            }
            Assert.IsTrue(driver.Game.ChildFlashing, "the flash window is open");

            yield return null;                           // ReflectChildButton lights + pulse-scales the button
            Color lit = driver.ChildButtonImage.color;
            Assert.Greater(lit.r, 0.8f, "the child button is actually LIT (bright) for the guard");

            // (founder complaint) the LIT/scaled child button must NOT reach the bottom answer plate.
            var childAabb = WorldAabb(driver.ChildButtonImage.rectTransform);
            var noAabb = WorldAabb(driver.NoPlateImage.rectTransform);
            Assert.IsFalse(Overlap(childAabb, noAabb),
                "the lit child button (" + childAabb + ") is DISJOINT from the «СПАСИБО, НЕ НАДО» plate (" + noAabb + ")");

            // It stays strictly in its top-right HUD zone: fully above the plate's top edge.
            Assert.Greater(childAabb.yMin, noAabb.yMax,
                "the child button sits entirely above the answer plate (its HUD zone)");

            // No OTHER state overlay may leak into the child-flash state (the whole-HUD stray-Image guard lives
            // in HudConformanceTests; here we bound the state-specific strays: a stuck burnout/depression/
            // finale/opener panel over live gameplay would be a stray and must fail).
            Assert.IsFalse(driver.BurnoutPlate.activeInHierarchy, "no burnout overlay during child flash");
            Assert.IsFalse(driver.DepressionOverlay.activeInHierarchy, "no depression overlay during child flash");
            Assert.IsFalse(driver.FinalePanel.activeInHierarchy, "no finale panel during child flash");
            Assert.IsFalse(driver.OpenerPanel.activeInHierarchy, "no opener panel during child flash");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ S10 BLOCK$

        private static Game BlockDeck()
        {
            var deck = new List<Card>
            {
                Starter(), Plain("FILL", 18), Plain("MD03", 30), Plain("NORMAL", 40),
            };
            deck[2].IsBlockCost = true;   // MD03 → BLOCK$ (Game.BlockPrices["MD03"] = 60)
            return new Game(deck, coin: () => false);
        }

        [UnityTest]
        public IEnumerator Blocked_PriceInPill_BannerInPill_PlatesMuted()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(BlockDeck());
            fake.Confirm();                              // opener → StartLife (starter drawn)
            fake.No();                                   // resolve starter → FILL drawn
            fake.No();                                   // resolve FILL → MD03 drawn (money 0 < 60 → blocked)

            Assert.AreEqual("MD03", driver.Game.CurrentCard.Id, "landed on the real BLOCK$ card");
            Assert.IsTrue(driver.Game.CurrentCardBlocked, "drawn broke → blocked");
            yield return null;                           // one Update → the answer-plate mute applies

            // «цена N ₽» on its dark plate — real ₽ glyph (Rubik), drawn glyphs inside the pill.
            Assert.IsTrue(driver.CardPriceText.gameObject.activeSelf, "the price line is shown on the blocked card");
            StringAssert.Contains("цена", driver.CardPriceText.text, "S10 wording «цена N ₽»");
            StringAssert.Contains("60", driver.CardPriceText.text, "shows the required amount");
            AssertGeneratedInPill(driver.CardPriceText, driver.CardPricePlate, BarTrackPill, "«цена N ₽» on its dark plate");
            AssertNoTofu(driver.CardPriceText, "«цена N ₽» (has ₽)");

            // Red banner «Как жаль…» fully inside its pill.
            Assert.IsTrue(driver.BlockBanner.activeSelf, "the S10 block banner is up");
            var bannerImg = driver.BlockBanner.GetComponent<Image>();
            var bannerText = driver.BlockBanner.transform.Find("BlockText").GetComponent<Text>();
            StringAssert.Contains("Как жаль", bannerText.text, "the banner reads «Как жаль, у вас нет денег на это!»");
            AssertGeneratedInPill(bannerText, bannerImg, BarTrackPill, "S10 red banner");
            AssertNoTofu(bannerText, "S10 red banner");

            // Both answer plates are MUTED while blocked (not the full-bright white of a normal card).
            Assert.Less(driver.YesPlateImage.color.g, 0.9f, "the ДА plate is muted while blocked");
            Assert.Less(driver.NoPlateImage.color.r, 0.9f, "the «СПАСИБО, НЕ НАДО» plate is muted while blocked");
            Assert.Greater(driver.YesPlateImage.color.g, 0.3f, "muted, not black (still legible)");

            // Exhaustive: the blocked card carries EXACTLY the frame (dimmed via its own tint — no overlay veil)
            // + red banner + price plate, and exactly the question + banner + price texts — a stray sprite/label
            // on the block card fails.
            var card = driver.CardRect.gameObject;
            AssertExactImages(card, "blocked card",
                "CardFrame", "BlockBanner", "CardPricePlate");
            AssertExactTexts(card, "blocked card", "CardText", "BlockText", "CardPrice");
            // …and the dim is a real frame tint (the S10 fix), not the full-bright white of a normal card.
            Assert.Less(driver.CardFrameImage.color.b, 0.9f, "the blocked card frame is dimmed (tinted, not full-bright)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
