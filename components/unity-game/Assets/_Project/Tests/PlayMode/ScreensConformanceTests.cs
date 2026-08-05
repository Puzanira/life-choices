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
    ///  • opener: the logo cut, the marquee frame and the cream rules plate sit in their MEASURED explainer
    ///    boxes (×0.6977, ±10 px), the bulb ring rides the gold band, the CANON rules copy renders whole
    ///    inside the plate, the «НАЧАТЬ ЖИЗНЬ — ЖМИ ЗЕЛЁНУЮ» CTA lands on its green pill, and the panel
    ///    renders EXACTLY its expected sprite census;
    ///  • tutorial: the hint body sits fully inside the yellow modal's visible pill, the «ПОНЯТНО» dismiss
    ///    button label is inside its pill AND the button is not flush to the modal bottom (a clear margin),
    ///    the gameplay behind is dimmed, and the modal renders exactly modal+button;
    ///  • finales: the cause line is on-screen (never clipped by the 16:9 edge), a worst-case LONG story
    ///    still fits inside the story plate's visible pill, the «НАЧАТЬ ЗАНОВО» label lands on its pill,
    ///    that pill renders the GREEN token (same assertion as the opener CTA — both name the same physical
    ///    button), and the finale panel renders exactly cause-pill + story-plate + restart rim+plate.
    /// The visible pill = the plate rect shrunk by the sprite's 9-slice corner inset (NOT the raw text rect):
    /// best-fit only shrinks glyphs to WITHIN the text rect, so text-rect ⊆ pill guarantees the drawn label
    /// lands on the coloured pill for any best-fit result. Geometric + deterministic (stable headless).
    /// </summary>
    public class ScreensConformanceTests
    {
        // Per-sprite visible-pill corner insets (UI units), conservative vs the raw 9-slice border.
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

        // Both confirm CTAs (opener «НАЧАТЬ ЖИЗНЬ», finale «НАЧАТЬ ЗАНОВО») promise the GREEN cabinet
        // button, so their plate must render the GREEN token #05CE51 — not merely a green. uGUI MULTIPLIES
        // the Image tint by the sprite's own fill, so the rendered colour (what the design gate measures off
        // the frame) is fill × tint; asserting the raw tint would pass on a plate that renders pale.
        private static void AssertTokenGreen(Image plate, string what)
        {
            Assert.AreEqual("bar-track", plate.sprite.name,
                what + " is built on bar-track — the only plate sprite a tint can drive to the token");
            var rendered = new Color(GameDriver.BarTrackFillToken.r * plate.color.r,
                                     GameDriver.BarTrackFillToken.g * plate.color.g,
                                     GameDriver.BarTrackFillToken.b * plate.color.b);
            var token = GameDriver.GoGreenToken;
            Assert.AreEqual(token.r, rendered.r, 1f / 255f, what + " renders the GREEN token R (#05CE51)");
            Assert.AreEqual(token.g, rendered.g, 1f / 255f, what + " renders the GREEN token G (#05CE51)");
            Assert.AreEqual(token.b, rendered.b, 1f / 255f, what + " renders the GREEN token B (#05CE51)");
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

        /// <summary>Канон-текст правил опенера (build-spec §A) — независимая копия, чтобы тест реально
        /// СВЕРЯЛ строку драйвера с каноном, а не сравнивал константу саму с собой.</summary>
        private const string CanonRules =
            "Добро пожаловать в увлекательное шоу длинною в жизнь! Пройди от 1 года до 100 лет, "
            + "постарайся принять правильные решения и за всем уследить. Со временем жизнь будет "
            + "становиться всё сложнее и быстрее. Уследить за всем невозможно, но давай попробуем!";

        private static string Squash(string s)
            => System.Text.RegularExpressions.Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>
        /// Assert an element sits in its MEASURED mockup box — (cx, cy-from-top, w, h) at 1920×1080, i.e.
        /// the explainer box × 0.6977. Read back out of the rect's own anchor + sizeDelta (the exact inverse
        /// of GameDriver.AnchorPx): the batch canvas is not 16:9, so canvas-pixel arithmetic would lie about
        /// sizes, while the authored anchor/size pair IS the geometry that ships on the 1920×1080 cabinet.
        /// </summary>
        private static void AssertMockBox(RectTransform rt, float cx, float cyTop, float w, float h,
            float tol, string what)
        {
            Assert.AreEqual(rt.anchorMin, rt.anchorMax, what + ": point-anchored (AnchorPx)");
            Assert.AreEqual(0f, rt.anchoredPosition.x, 0.01f, what + ": sits on its anchor (x)");
            Assert.AreEqual(0f, rt.anchoredPosition.y, 0.01f, what + ": sits on its anchor (y)");
            Assert.AreEqual(cx, rt.anchorMin.x * 1920f, tol, what + ": centre X (эталон ×0.6977)");
            Assert.AreEqual(cyTop, (1f - rt.anchorMin.y) * 1080f, tol, what + ": centre Y from the TOP");
            Assert.AreEqual(w, rt.sizeDelta.x, tol, what + ": width");
            Assert.AreEqual(h, rt.sizeDelta.y, tol, what + ": height");
        }

        [UnityTest]
        public IEnumerator Opener_MatchesExplainer_CanonRules_GreenCta_ExactSprites()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                                  // Awake built the HUD; boots in the opener

            Assert.AreEqual(GameState.Opener, driver.Game.State, "boots into the opener");
            Assert.IsTrue(driver.OpenerPanel.activeSelf, "opener panel is up");
            Assert.IsFalse(driver.GamePanel.activeSelf, "HUD/таймер на опенере не идут (build-spec §A)");
            Assert.IsFalse(driver.FinalePanel.activeSelf, "no finale panel on the opener");

            var opener = driver.OpenerPanel;

            // (1) The three measured boxes off `explainers/Стартовый экран.png` (2752×1536 × 0.6977), ±10 px.
            Assert.IsNotNull(driver.OpenerLogo, "the opener has a logo Image");
            Assert.IsNotNull(driver.OpenerLogo.sprite, "…with a real sprite (not a missing asset)");
            Assert.AreEqual("opener-logo-v2", driver.OpenerLogo.sprite.name,
                "the logo is the cut taken from the explainer");
            AssertMockBox(driver.OpenerLogo.rectTransform, 954f, 279f, 1017f, 477f, 10f, "логотип");

            var frame = opener.transform.Find("MarqueeFrame").GetComponent<Image>();
            AssertMockBox(frame.rectTransform, 966f, 781f, 1798f, 522f, 10f, "марки-рамка");
            AssertMockBox(driver.OpenerPlate.rectTransform, 966f, 781f, 1726f, 450f, 10f, "плашка правил");

            // The cream plate really is INSIDE the marquee frame (a gold band on every side), and the logo
            // clears the frame — i.e. the composition, not just four independent numbers.
            Assert.Greater(frame.rectTransform.sizeDelta.x, driver.OpenerPlate.rectTransform.sizeDelta.x,
                "the cream plate sits inside the marquee frame (width)");
            Assert.Greater(frame.rectTransform.sizeDelta.y, driver.OpenerPlate.rectTransform.sizeDelta.y,
                "the cream plate sits inside the marquee frame (height)");
            float logoBottom = 279f + 477f * 0.5f;
            float frameTop = 781f - 522f * 0.5f;
            Assert.LessOrEqual(logoBottom, frameTop + 1f, "the logo clears the rules frame (no overlap)");

            // (2) Marquee bulbs: a real ring around the frame, every bulb ON the gold band's midline.
            var bulbs = frame.GetComponentsInChildren<Image>(true)
                .Where(i => i.gameObject.name.StartsWith("Bulb")).ToList();
            Assert.AreEqual(118, bulbs.Count, "48 bulbs across × 2 + 11 down × 2 — the explainer's cadence");
            float hx = frame.rectTransform.sizeDelta.x * 0.5f;
            float hy = frame.rectTransform.sizeDelta.y * 0.5f;
            foreach (var b in bulbs)
            {
                var p = b.rectTransform.anchoredPosition;
                bool onSide = Mathf.Abs(Mathf.Abs(p.x) - (hx - 18.5f)) < 1f;
                bool onCap = Mathf.Abs(Mathf.Abs(p.y) - (hy - 18.5f)) < 1f;
                Assert.IsTrue(onSide || onCap,
                    "bulb «" + b.gameObject.name + "» sits on the gold band, not adrift on the plate");
                Assert.LessOrEqual(Mathf.Abs(p.x), hx, "bulb inside the frame (x)");
                Assert.LessOrEqual(Mathf.Abs(p.y), hy, "bulb inside the frame (y)");
            }

            // (3) The CANON rules copy, whole and unedited, with its drawn glyphs inside the cream plate.
            Assert.AreEqual(CanonRules, Squash(driver.OpenerRules.text),
                "правила опенера = канон build-spec §A (без «5 секунд»), целиком");
            AssertGeneratedInPill(driver.OpenerRules, driver.OpenerPlate, BarTrackPill, "канон-текст правил");
            AssertNoTofu(driver.OpenerRules, "канон-текст правил");

            // (4) The start CTA names the PHYSICAL green button (founder 99fab3c) and lands on its own pill.
            var startPlate = opener.transform.Find("StartPlate").GetComponent<Image>();
            var startText = startPlate.transform.Find("StartText").GetComponent<Text>();
            StringAssert.Contains("НАЧАТЬ ЖИЗНЬ", startText.text, "start button reads «НАЧАТЬ ЖИЗНЬ»");
            StringAssert.Contains("ЗЕЛЁНУЮ", startText.text, "…and names the GREEN cabinet button");
            AssertGeneratedInPill(startText, startPlate, BarTrackPill, "«НАЧАТЬ ЖИЗНЬ» CTA");
            AssertTokenGreen(startPlate, "opener start CTA");

            // (5) Exhaustive census: logo + 4 layered plates (frame rim/gold, plate rim/cream) + the CTA
            //     (rim + green) + 118 bulbs — no stray/placeholder Image anywhere on the screen.
            var census = SpriteNames(opener).GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(1, census["opener-logo-v2"], "exactly one logo");
            Assert.AreEqual(118, census["marquee-bulb"], "exactly the measured bulb ring");
            Assert.AreEqual(6, census["bar-track"], "frame rim+gold, plate rim+cream, CTA rim+green");
            Assert.AreEqual(3, census.Count, "opener renders NOTHING else — no stray/placeholder Image");

            // (6) Exhaustive Text enumeration: the rules copy + the start CTA; no stray/tofu text.
            AssertExactTexts(opener, 2, "opener");

            Object.Destroy(go);
            yield return null;
        }

        // ============================================================ S5 tutorial

        // Raise the S5 hint over live gameplay. It used to be reached by walking a life to the money open
        // (18); since that open now leads the §D modal instead (increment «экран появления новой шкалы»),
        // the S5 hint survives only for health (30) and burnout — both far enough into a CSV-SAMPLED deck
        // that walking there is a flake. The composition guarded below is the modal's own, so the hint is
        // raised through the driver's own show path over a live board.
        private static void PlayUntilTutorial(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (driver.Game.Age < 3f && driver.Game.State == GameState.Playing && guard++ < 400)
            {
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f) fake.No();
            }
            driver.DebugShowTutorial("ЗДОРОВЬЕ НАЧАЛО ТАЯТЬ.\n\nС этого возраста ЗДОРОВЬЕ убывает само по себе.");
            Assert.IsTrue(driver.TutorialShowing, "a tutorial modal is up over live gameplay");
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

            // (3) «ПОНЯТНО — ЖМИ ЗЕЛЁНУЮ» button label inside its own pill.
            StringAssert.Contains("ПОНЯТНО", driver.TutorialButtonText.text, "button reads «ПОНЯТНО»");
            StringAssert.Contains("ЗЕЛЁНУЮ", driver.TutorialButtonText.text,
                "the dismiss button names the PHYSICAL green control, never a dev key (founder 99fab3c)");
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

            // (3) «НАЧАТЬ ЗАНОВО / ЖМИ ЗЕЛЁНУЮ» two-line label — drawn glyphs land on the green restart pill.
            var again = driver.FinalePanel.transform.Find("AgainPlate").GetComponent<Image>();
            var againText = again.transform.Find("AgainText").GetComponent<Text>();
            StringAssert.Contains("НАЧАТЬ ЗАНОВО", againText.text, "restart button reads «НАЧАТЬ ЗАНОВО»");
            AssertGeneratedInPill(againText, again, BarTrackPill, "«НАЧАТЬ ЗАНОВО» two-line label");

            // (3b) …and the pill is the GREEN TOKEN itself, not «some green»: the label names the green
            // cabinet button, so a paler plate (plate-yes' own #5CBF5F) makes the hint point at a colour the
            // cabinet does not have (design gate 2026-07-31). Asserted on the RENDERED colour — uGUI
            // multiplies the tint by the sprite's own fill — exactly as the opener CTA is asserted below.
            AssertTokenGreen(again, "finale restart CTA");

            // (4) Exhaustive enumeration: exactly the cause pill + the story plate + the restart plate
            //     (rim + green, built like the opener CTA so both confirm plates carry the same token).
            var expected = new List<string> { "bar-track", "bar-track", "bar-track", "bar-track" };
            CollectionAssert.AreEqual(expected, SpriteNames(driver.FinalePanel),
                "the finale renders exactly the cause pill + story plate + restart rim+plate — no stray Image");

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
