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
    /// Layer-2 conformance for the four TEXT screens (design gate: S1 opener, S5 tutorial, S11/S12 finales).
    /// Asserts the RENDERED result, not activeSelf flags:
    ///  • opener: the four rules each render inside their OWN cobalt plate's visible pill, the «НАЧАТЬ ЖИЗНЬ»
    ///    label lands on its green plate's pill, and the panel renders EXACTLY its expected sprite set;
    ///  • tutorial: the hint body sits fully inside the yellow modal's visible pill, the «ПОНЯТНО — Enter»
    ///    button label is inside its pill AND the button is not flush to the modal bottom (a clear margin),
    ///    the gameplay behind is dimmed, and the modal renders exactly modal+button;
    ///  • finales: the cause line is on-screen (never clipped by the 16:9 edge), a worst-case LONG story
    ///    still fits inside the story plate's visible pill, the «НАЧАТЬ ЗАНОВО» label lands on its pill, and
    ///    the finale panel renders exactly story-plate + restart-plate.
    /// The visible pill = the plate rect shrunk by the sprite's 9-slice corner inset (NOT the raw text rect):
    /// best-fit only shrinks glyphs to WITHIN the text rect, so text-rect ⊆ pill guarantees the drawn label
    /// lands on the coloured pill for any best-fit result. Geometric + deterministic (stable headless).
    /// </summary>
    public class ScreensConformanceTests
    {
        // Per-sprite visible-pill corner insets (UI units), conservative vs the raw 9-slice border.
        private const float PlatePill = 55f;     // plate-yes / plate-no (82px border → ~55 visible)
        private const float BarTrackPill = 16f;  // bar-track rounded rect — only the corners are cut

        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        // Real rendered glyph count (best-fit honoured) — a screen renders text, not tofu/empty.
        private static int VisibleGlyphs(Text t)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            return tg.characterCountVisible;
        }

        // Real no-tofu teeth: a missing-font replacement box (□) still counts toward characterCountVisible,
        // so a count>0 check alone would pass on tofu. Assert the label's OWN Font actually carries a glyph
        // for every non-whitespace character it is asked to draw. HasCharacter(c) is the coverage query; for
        // dynamic fonts warm the atlas first so it is answered against real glyph data. A null/empty/wrong
        // font (or a genuinely missing glyph) fails here even though the label still "renders" a box.
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

        // Assert a label's TEXT RECT sits within the plate's visible coloured pill (plate rect − corner inset)
        // on all four sides, and that it renders glyphs. Text is a child of the plate, so the mapping is exact.
        private static void AssertTextInPill(Text t, Graphic plate, float pill, string what)
        {
            Assert.Greater(VisibleGlyphs(t), 0, what + " renders glyphs (not empty/tofu-collapsed)");
            var plateRt = plate.rectTransform;
            var tr = t.rectTransform;
            var tc = new Vector3[4];
            tr.GetLocalCorners(tc);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var pl = plateRt.InverseTransformPoint(tr.TransformPoint(tc[i]));
                if (pl.x < minX) minX = pl.x; if (pl.x > maxX) maxX = pl.x;
                if (pl.y < minY) minY = pl.y; if (pl.y > maxY) maxY = pl.y;
            }
            var pr = plateRt.rect;
            const float tol = 1f;
            Assert.GreaterOrEqual(minX, pr.xMin + pill - tol, what + " text within the visible pill (left)");
            Assert.LessOrEqual(maxX, pr.xMax - pill + tol, what + " text within the visible pill (right)");
            Assert.GreaterOrEqual(minY, pr.yMin + pill - tol, what + " text within the visible pill (bottom)");
            Assert.LessOrEqual(maxY, pr.yMax - pill + tol, what + " text within the visible pill (top)");
        }

        // Assert a text's rendered rect is fully on-screen (never clipped by the 16:9 canvas edge).
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

        private static List<string> SpriteNames(GameObject root)
            => root.GetComponentsInChildren<Image>(includeInactive: false)
                .Select(i => i.sprite != null ? (string.IsNullOrEmpty(i.sprite.name) ? "<unnamed>" : i.sprite.name) : "<null>")
                .OrderBy(s => s).ToList();

        // Enumerate the ACTIVE Text elements under a screen root: a stray/extra label changes the count and a
        // tofu-collapsed label renders zero glyphs — either fails. (Image-only enumeration would miss both.)
        private static void AssertExactTexts(GameObject root, int expectedCount, string what)
        {
            var texts = root.GetComponentsInChildren<Text>(includeInactive: false)
                .Where(t => !string.IsNullOrEmpty(t.text)).ToList();
            Assert.AreEqual(expectedCount, texts.Count,
                what + " renders exactly its expected Text labels — no stray/placeholder text (got: "
                    + string.Join(", ", texts.Select(t => t.name)) + ")");
            foreach (var t in texts)
            {
                Assert.Greater(VisibleGlyphs(t), 0, what + " label «" + t.name + "» renders glyphs (not tofu)");
                AssertNoTofu(t, what + " label «" + t.name + "»");
            }
        }

        // Teeth for vertical/horizontal overflow: assert the actual GENERATED glyph mesh (best-fit honoured)
        // sits inside the plate's visible pill on all four sides — NOT merely the Text RectTransform. With
        // verticalOverflow=Overflow best-fit only fits WIDTH, so a two-line label can spill past its rect and
        // past the pill while a rect-corner check stays green; this reads the real drawn bounds and fails.
        private static void AssertGeneratedInPill(Text t, Graphic plate, float pill, string what)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, what + " renders glyphs (not empty/tofu-collapsed)");

            // Vertex positions map to the RectTransform's local space exactly as Text.OnPopulateMesh draws
            // them: local = vertex.position * (1 / pixelsPerUnit). Bound the drawn mesh, then map to the plate.
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

        // ============================================================ S1 opener

        [UnityTest]
        public IEnumerator Opener_RulesInPlates_StartLabelOnPill_ExactSprites()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                  // Awake built the HUD; boots in the opener

            Assert.AreEqual(GameState.Opener, driver.Game.State, "boots into the opener");
            Assert.IsTrue(driver.OpenerPanel.activeSelf, "opener panel is up");

            var opener = driver.OpenerPanel;

            // (1) Exactly four rules, each inside its OWN cobalt plate's visible pill (never bare on the
            //     sunburst). Teeth for "exactly four": the four Find()s below prove >=4, and asserting there
            //     is NO fifth plate proves <=4 (a stray 5th plate would fail here rather than pass silently).
            Assert.IsNull(opener.transform.Find("RulePlate4"), "no fifth rule plate — exactly four (S1)");
            for (int i = 0; i < 4; i++)
            {
                var plateT = opener.transform.Find("RulePlate" + i);
                Assert.IsNotNull(plateT, "rule plate " + i + " exists");
                var plate = plateT.GetComponent<Image>();
                var text = plateT.Find("RuleText" + i).GetComponent<Text>();
                AssertTextInPill(text, plate, BarTrackPill, "rule " + i);
            }

            // (2) «НАЧАТЬ ЖИЗНЬ (Enter)» label lands on the green start plate's visible pill.
            var startPlate = opener.transform.Find("StartPlate").GetComponent<Image>();
            var startText = startPlate.transform.Find("StartText").GetComponent<Text>();
            StringAssert.Contains("НАЧАТЬ ЖИЗНЬ", startText.text, "start button reads «НАЧАТЬ ЖИЗНЬ»");
            // Two-line CTA: assert the DRAWN glyph mesh (not just the rect) fits the green pill on all sides.
            AssertGeneratedInPill(startText, startPlate, PlatePill, "«НАЧАТЬ ЖИЗНЬ» two-line label");

            // (3) Exhaustive enumeration: exactly the four rule plates + the start plate (no stray Image).
            var expected = new List<string> { "bar-track", "bar-track", "bar-track", "bar-track", "plate-yes" };
            CollectionAssert.AreEqual(expected.OrderBy(s => s).ToList(), SpriteNames(opener),
                "opener renders exactly four rule plates + the start plate — no stray/placeholder Image");

            // (4) Exhaustive Text enumeration: title + four rules + the start label; no stray/tofu text.
            AssertExactTexts(opener, 6, "opener");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ S5 tutorial

        // Drive a real life until the first (money) tutorial modal appears.
        private static void PlayUntilTutorial(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (!driver.TutorialShowing && driver.Game.State == GameState.Playing && guard++ < 6000)
            {
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (!driver.TutorialShowing && driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }
            Assert.IsTrue(driver.TutorialShowing, "a tutorial modal appeared during the run");
        }

        [UnityTest]
        public IEnumerator Tutorial_BodyAndButtonInModal_ButtonNotFlush_GameplayDimmed()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                                     // opener → playing
            PlayUntilTutorial(driver, fake);
            yield return null;

            Assert.IsTrue(driver.TutorialOverlay.activeSelf, "the modal overlay is up");

            // (1) Gameplay behind is dimmed: the overlay is a near-opaque dark veil above the panels.
            var veil = driver.TutorialOverlay.GetComponent<Image>();
            Assert.IsNotNull(veil, "the overlay carries a dimming veil Image");
            Assert.Greater(veil.color.a, 0.5f, "the veil dims the gameplay behind the modal");

            var modal = driver.TutorialModal;

            // (2) Hint body fully inside the yellow modal's visible pill (solid bar-track card). The body is a
            // multi-line hint under verticalOverflow, so assert the DRAWN glyph mesh (not just the rect) fits.
            AssertGeneratedInPill(driver.TutorialText, modal, BarTrackPill, "hint body");

            // (3) «ПОНЯТНО — Enter» button label inside its own pill.
            StringAssert.Contains("ПОНЯТНО", driver.TutorialButtonText.text, "button reads «ПОНЯТНО»");
            StringAssert.Contains("Enter", driver.TutorialButtonText.text, "button spells out «Enter»");
            AssertTextInPill(driver.TutorialButtonText, driver.TutorialButton, BarTrackPill, "«ПОНЯТНО»");

            // (4) The button is NOT flush to the modal bottom — a clear margin below it (S5 mockup).
            var mr = modal.rectTransform.rect;
            var br = driver.TutorialButton.rectTransform;
            var bc = new Vector3[4];
            br.GetWorldCorners(bc);
            float btnBottomLocal = float.MaxValue;
            for (int i = 0; i < 4; i++)
                btnBottomLocal = Mathf.Min(btnBottomLocal, modal.rectTransform.InverseTransformPoint(bc[i]).y);
            float margin = btnBottomLocal - mr.yMin;
            Assert.Greater(margin, 20f, "button has a clear bottom margin inside the modal (not flush)");

            // (5) Exhaustive enumeration of the modal: exactly the yellow card + the blue button (both bar-track).
            var expected = new List<string> { "bar-track", "bar-track" }.OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(expected, SpriteNames(modal.gameObject),
                "the modal renders exactly the yellow card + the button — no stray Image");

            // (6) Exhaustive Text enumeration of the modal: exactly the hint body + the button label; no stray/tofu.
            AssertExactTexts(modal.gameObject, 2, "tutorial modal");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ S11/S12 finales

        // Worst-case necrolog: the parents line + the maximum number of long weighty lines (no ROND to drop),
        // «весёлая старость» tone — the longest glued story the finale can ever have to render.
        private static NecrologResult LongStory()
        {
            const string longLine = "Рыжий кот из детства, его котята и их котята прожили с вами всю жизнь.";
            var entries = new List<NecrologEntry>();
            for (int i = 0; i < Necrolog.MaxLines + 4; i++)   // over the cap → still clamps to MaxLines
                entries.Add(new NecrologEntry { Age = i, Order = i, Line = longLine, IsRond = false });
            return Necrolog.Build("весёлая старость", entries);
        }

        [UnityTest]
        public IEnumerator Finale_CauseOnScreen_LongStoryInPlate_RestartLabel_ExactSprites()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugRenderFinale(LongStory());
            yield return null;

            Assert.IsTrue(driver.FinalePanel.activeSelf, "the finale panel is up");
            var canvas = driver.CanvasRect;

            // (1) Cause line (yellow) is on-screen AND its drawn glyphs fit its dark pill (solid, high-contrast).
            StringAssert.Contains("Причина конца", driver.FinaleCauseText.text, "cause line reads «Причина конца»");
            AssertOnScreen(driver.FinaleCauseText, canvas, "finale cause");
            var causePlate = driver.FinaleCauseText.transform.parent.GetComponent<Image>();
            AssertGeneratedInPill(driver.FinaleCauseText, causePlate, BarTrackPill, "finale cause on its dark pill");

            // (2) The worst-case LONG glued story fits INSIDE the dark story plate's visible pill — assert the
            // DRAWN glyph mesh (best-fit honoured), not just the rect, so a vertical spill past the pill fails.
            AssertGeneratedInPill(driver.FinaleStoryText, driver.FinaleStoryPlate, BarTrackPill, "long finale story");

            // (3) «НАЧАТЬ ЗАНОВО (Enter)» two-line label — drawn glyphs land on the green restart plate's pill.
            var again = driver.FinalePanel.transform.Find("AgainPlate").GetComponent<Image>();
            var againText = again.transform.Find("AgainText").GetComponent<Text>();
            StringAssert.Contains("НАЧАТЬ ЗАНОВО", againText.text, "restart button reads «НАЧАТЬ ЗАНОВО»");
            AssertGeneratedInPill(againText, again, PlatePill, "«НАЧАТЬ ЗАНОВО» two-line label");

            // (4) Exhaustive enumeration: exactly the cause pill + the story plate + the restart plate.
            var expected = new List<string> { "bar-track", "bar-track", "plate-yes" }.OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(expected, SpriteNames(driver.FinalePanel),
                "the finale renders exactly the cause pill + story plate + restart plate — no stray Image");

            // (5) Exhaustive Text enumeration: title + cause + story + restart label; no stray/tofu text.
            AssertExactTexts(driver.FinalePanel, 4, "finale");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Finale_ShortFatalStory_FitsPlate()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var fatal = Necrolog.Build("вы сунули палец в розетку", new List<NecrologEntry>());
            driver.DebugRenderFinale(fatal);
            yield return null;

            AssertOnScreen(driver.FinaleCauseText, driver.CanvasRect, "fatal cause");
            var causePlate = driver.FinaleCauseText.transform.parent.GetComponent<Image>();
            AssertGeneratedInPill(driver.FinaleCauseText, causePlate, BarTrackPill, "fatal cause on its dark pill");
            AssertGeneratedInPill(driver.FinaleStoryText, driver.FinaleStoryPlate, BarTrackPill, "short fatal story");
            // No-tofu teeth for both finale labels (this test does not run the exhaustive Text enumeration).
            AssertNoTofu(driver.FinaleCauseText, "fatal cause");
            AssertNoTofu(driver.FinaleStoryText, "short fatal story");

            Object.Destroy(go);
            yield return null;
        }
    }
}
