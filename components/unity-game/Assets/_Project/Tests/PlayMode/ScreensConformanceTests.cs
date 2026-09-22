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

        // ============================================================ S11/S12 finale (end.png канон)

        // Worst-case necrolog СИНТЕТИЧЕСКИЙ: 15 строк лимита, каждая — длиннейшая строка колоды.
        // (Реальный худший случай, собранный из scenes.csv, живёт в LongestRealStory ниже.)
        private static NecrologResult LongStory()
        {
            // ⚠ СТРОКИ ОБЯЗАНЫ БЫТЬ РАЗНЫМИ. Раньше здесь одна и та же строка повторялась 11 раз, и после
            // дедупа некролога по ТЕКСТУ (ревью 2026-08-08) «худший случай» схлопнулся бы в две строки —
            // приёмка вёрстки перестала бы что-либо проверять. Меняем первое слово, длина сохраняется.
            var coats = new[]
            {
                "Рыжий", "Серый", "Белый", "Чёрный", "Полосатый", "Трёхцветный",
                "Пятнистый", "Дымчатый", "Рябой", "Огненный", "Синеглазый",
            };
            var entries = new List<NecrologEntry>();
            for (int i = 0; i < Necrolog.MaxLines + 4; i++)   // over the cap → still clamps to MaxLines
                entries.Add(new NecrologEntry
                {
                    Age = i, Order = i, IsRond = false,
                    Line = coats[i % coats.Length]
                           + " кот из детства, его котята и их котята прожили с вами всю жизнь.",
                });
            return Necrolog.Build("весёлая старость", entries);
        }

        /// <summary>
        /// РЕАЛЬНЫЙ худший случай: <see cref="Necrolog.MaxLines"/> самых длинных строк некролога из живой
        /// колоды (`Resources/scenes.csv`, кол. «Некролог ДА»/«Некролог НЕТ»). Синтетика с одной
        /// повторённой строкой не годится как приёмка: она короче реальной склейки и не ловит, например,
        /// длинную причину поверх длинной истории.
        ///
        /// ⚠ 2026-09-22: было «MaxLines−1 строк + фиксированная строка родителей». Родителей больше нет,
        /// поэтому худший случай набирается ЦЕЛИКОМ из колоды — и он стал ДЛИННЕЕ на одну настоящую
        /// строку, а не короче: лимит держит семь строк, и все семь теперь самые длинные в колоде.
        /// </summary>
        private static NecrologResult LongestRealStory()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "колода читается из Resources (нужна для реального худшего случая)");
            var lines = new List<string>();
            foreach (var c in CardLoader.ParseAll(csv.text))
            {
                if (!string.IsNullOrEmpty(c.YesNecrolog) && c.YesNecrolog != "—") lines.Add(c.YesNecrolog);
                if (!string.IsNullOrEmpty(c.NoNecrolog) && c.NoNecrolog != "—") lines.Add(c.NoNecrolog);
            }
            // РАЗНЫЕ строки: некролог дедуплицирует по тексту, а худший случай обязан набрать полный лимит.
            lines = lines.Distinct().ToList();
            lines.Sort((a, b) => b.Length.CompareTo(a.Length));
            var entries = new List<NecrologEntry>();
            for (int i = 0; i < Necrolog.MaxLines && i < lines.Count; i++)
                entries.Add(new NecrologEntry { Age = i, Order = i, Line = lines[i], IsRond = false });
            Assert.AreEqual(Necrolog.MaxLines, entries.Count,
                "в колоде хватает строк, чтобы набрать лимит некролога целиком");
            // Длиннейшая причина колоды тоже участвует: строка исхода делит с некрологом одну плашку.
            return Necrolog.Build("вы сунули палец в розетку", entries);
        }

        /// <summary>
        /// ОБЫЧНАЯ прожитая жизнь на полный лимит вех — ровно та поза, которую снимает кадр `finale`
        /// (<c>ArcadeScreenshotTests.SampleNecrolog</c>). Для ВЫСОТЫ блока это худший случай из всех:
        /// строки короткие, ни одна не получает персонального <c>&lt;size&gt;</c>, поэтому все ряды
        /// стоят полным кеглем блока. Худшие «длинные» случаи, наоборот, часть рядов ужимают и по высоте
        /// оказываются мягче.
        ///
        /// ⚠ 2026-09-22: строк СЕМЬ, а не шесть. Раньше седьмой ряд давала строка родителей, и поза
        /// набирала полный лимит только вместе с ней; теперь лимит набирается прожитыми строками, и
        /// добавленная сюда седьмая — не «для красоты», а условие того, что гард полного лимита вообще
        /// что-то стережёт (он сам это и проверяет ассертом на `MaxLines`).
        /// </summary>
        private static NecrologResult OrdinaryFullStory()
        {
            var entries = new List<NecrologEntry>
            {
                new NecrologEntry { Age = 7,  Order = 0, Line = "В семь лет вы завели рыжего кота и назвали его Борщ." },
                new NecrologEntry { Age = 19, Order = 1, Line = "Первую зарплату спустили за один вечер." },
                new NecrologEntry { Age = 24, Order = 2, Line = "В двадцать четыре уехали в другой город и ни разу не пожалели." },
                new NecrologEntry { Age = 30, Order = 3, Line = "Свадьбу сыграли, и это было громко.", IsMilestone = true },
                new NecrologEntry { Age = 32, Order = 4, Line = "Ребёнка растили как умели.", IsMilestone = true },
                new NecrologEntry { Age = 49, Order = 5, Line = "Ипотеку закрыли на четыре года позже, чем обещали себе." },
                new NecrologEntry { Age = 68, Order = 6, Line = "В шестьдесят восемь внуки научили вас проигрывать в карты." },
            };
            return Necrolog.Build("спокойная старость", entries);
        }

        /// <summary>
        /// Поставить канвас в МАСШТАБ КАБИНЕТА (`Canvas.scaleFactor` = 1) — ровно то, что делает капчер
        /// перед съёмкой кадра, и ровно то, что стоит на экране автомата 1920×1080. В батч-прогоне игровое
        /// окно не 16:9, и скалер по умолчанию выдаёт 0.3849: шрифт растеризуется в ДРУГОЙ сетке, межстрочье
        /// квантуется иначе, и всякий замер высоты в этом масштабе врёт про живой кадр.
        /// </summary>
        private static IEnumerator PinCabinetCanvas(GameDriver driver)
        {
            var scaler = driver.CanvasRect.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = GameDriver.CabinetCanvasScale;
            scaler.referencePixelsPerUnit = 100f;
            yield return null;
            foreach (var t in driver.GetComponentsInChildren<Text>(true)) { t.FontTextureChanged(); t.SetAllDirty(); }
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.AreEqual(GameDriver.CabinetCanvasScale,
                driver.CanvasRect.GetComponent<Canvas>().scaleFactor, 0.001f,
                "канвас действительно встал в масштаб кабинета (иначе проверка ничего не значит)");
        }

        /// <summary>
        /// Кремовое поле ЗАПЕЧЁННОЙ плашки `end.png` (замер PIL, cover-посадка) — независимая копия
        /// драйверных констант, чтобы тест реально СВЕРЯЛ посадку, а не сравнивал константу с собой.
        /// (cx, cy-от-верха, w, h).
        /// </summary>
        private static readonly Vector4 BakedCreamField = new(958.94f, 585.00f, 1232.57f, 600.46f);

        /// <summary>An AnchorPx-placed rect read back as (cx, cy-from-top, w, h) reference px.</summary>
        private static Vector4 RectOf(RectTransform rt) => new(
            rt.anchorMin.x * 1920f, (1f - rt.anchorMin.y) * 1080f, rt.sizeDelta.x, rt.sizeDelta.y);

        /// <summary>
        /// Inner box ⊆ outer box, both (cx, cy-from-top, w, h) in reference px.
        ///
        /// <paramref name="tolPx"/> — ДОПУСК НА ОКРУГЛЕНИЕ, не на вёрстку. Рект, снятый с живого
        /// RectTransform (<see cref="RectOf"/>), проходит через нормализованный anchor
        /// (<c>x / 1920 → × 1920</c>), и кромка, заданная ровно 415, возвращается как 414.99994. Пока у
        /// ректа есть хоть какой-то запас до бокса, это незаметно; у блока, чей рект РАВЕН своему боксу
        /// (строка исхода), запаса нет — и гард краснел бы на 6·10⁻⁵ px. Допуск здесь субпиксельный: он
        /// гасит round-trip и НЕ прощает ни одного видимого пикселя.
        /// </summary>
        private static void AssertRefBoxInside(Vector4 inner, Vector4 outer, string what, float tolPx = 0f)
        {
            Assert.GreaterOrEqual(inner.x - inner.z / 2f, outer.x - outer.z / 2f - tolPx, what + " (слева)");
            Assert.LessOrEqual(inner.x + inner.z / 2f, outer.x + outer.z / 2f + tolPx, what + " (справа)");
            Assert.GreaterOrEqual(inner.y - inner.w / 2f, outer.y - outer.w / 2f - tolPx, what + " (сверху)");
            Assert.LessOrEqual(inner.y + inner.w / 2f, outer.y + outer.w / 2f + tolPx, what + " (снизу)");
        }

        /// <summary>
        /// НАРИСОВАННЫЕ глифы подписи как бокс (cx, cy-от-верха, w, h) в 1920×1080 reference px — та же
        /// математика, что у <see cref="AssertGlyphsInRefBox"/>, но результатом, а не проверкой: композиция
        /// (зазоры между блоками) судится по чернилам, а не по ректам.
        /// </summary>
        private static Vector4 GlyphBoxOf(Text t)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, t.name + " renders glyphs");

            float upp = 1f / t.pixelsPerUnit;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                float x = verts[i].position.x * upp, y = verts[i].position.y * upp;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }

            var rt = t.rectTransform;
            float cx = rt.anchorMin.x * 1920f, cyTop = (1f - rt.anchorMin.y) * 1080f;
            float gLeft = cx + minX, gRight = cx + maxX;
            float gTop = cyTop - maxY, gBottom = cyTop - minY;
            return new Vector4((gLeft + gRight) / 2f, (gTop + gBottom) / 2f, gRight - gLeft, gBottom - gTop);
        }

        /// <summary>
        /// Assert the DRAWN glyph mesh of a label sits inside a box given in 1920×1080 reference px
        /// (cx, cy-from-top, w, h). Read through the label's own rect (authored in the same reference px by
        /// AnchorPx), so a non-16:9 batch game view cannot make it pass/fail for the wrong reason.
        /// </summary>
        private static void AssertGlyphsInRefBox(Text t, Vector4 box, float tol, string what)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, what + " renders glyphs (not empty/tofu-collapsed)");

            float upp = 1f / t.pixelsPerUnit;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                float x = verts[i].position.x * upp, y = verts[i].position.y * upp;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }

            // The rect's own centre in reference px (the exact inverse of AnchorPx).
            var rt = t.rectTransform;
            float cx = rt.anchorMin.x * 1920f, cyTop = (1f - rt.anchorMin.y) * 1080f;
            // Glyph bounds are local to the rect (y up) → reference px (y down from the top).
            float gLeft = cx + minX, gRight = cx + maxX;
            float gTop = cyTop - maxY, gBottom = cyTop - minY;

            float bLeft = box.x - box.z / 2f, bRight = box.x + box.z / 2f;
            float bTop = box.y - box.w / 2f, bBottom = box.y + box.w / 2f;
            Assert.GreaterOrEqual(gLeft, bLeft - tol, what + ": глифы внутри поля слева");
            Assert.LessOrEqual(gRight, bRight + tol, what + ": глифы внутри поля справа");
            Assert.GreaterOrEqual(gTop, bTop - tol, what + ": глифы внутри поля сверху");
            Assert.LessOrEqual(gBottom, bBottom + tol, what + ": глифы внутри поля снизу");
        }

        [UnityTest]
        public IEnumerator Finale_IsEndPng_TextInsideTheBakedPlate_GreenCta_ExactSprites()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugRenderFinale(LongStory(), 100);
            yield return null;

            Assert.IsTrue(driver.FinalePanel.activeSelf, "the finale panel is up");

            // (1) Фон — `end.png` целиком, на весь кадр по ЗАПОЛНЕНИЮ (никаких полос сверху/снизу).
            Assert.IsNotNull(driver.FinaleBackground, "у финала есть фон-Image");
            Assert.AreEqual("finale-bg-v2", driver.FinaleBackground.sprite.name,
                "фон финала = импорт `end.png` (asset-map §11-12), а не общий санбёрст");
            var bgRt = driver.FinaleBackground.rectTransform;
            Assert.AreEqual(GameDriver.FinaleBgW, bgRt.sizeDelta.x, 1f, "фон покрывает кадр по ширине");
            Assert.AreEqual(GameDriver.FinaleBgH, bgRt.sizeDelta.y, 1f, "…и по высоте (cover, без полос)");
            Assert.GreaterOrEqual(bgRt.sizeDelta.x, 1920f, "фон не уже кадра");
            Assert.GreaterOrEqual(bgRt.sizeDelta.y, 1080f, "фон не ниже кадра");
            Assert.AreEqual(0, driver.FinaleBackground.transform.GetSiblingIndex(),
                "фон — ПЕРВЫЙ слой панели: он перекрывает вращающийся санбёрст HUD");

            // (2) Строка исхода: «Ты дожил до N лет» + причина, глифы ВНУТРИ запечённого кремового поля.
            StringAssert.Contains("Ты дожил до 100 лет", driver.FinaleOutcomeText.text,
                "строка исхода называет возраст (build-spec §4-H)");
            StringAssert.Contains("Причина конца", driver.FinaleOutcomeText.text, "…и причину");
            AssertGlyphsInRefBox(driver.FinaleOutcomeText, BakedCreamField, 0f, "строка исхода");

            // (3) Худший СИНТЕТИЧЕСКИЙ некролог — тоже целиком в запечённом поле (best-fit честно ужимает).
            AssertGlyphsInRefBox(driver.FinaleStoryText, BakedCreamField, 0f, "длинный некролог");

            // (4) «НАЧАТЬ ЗАНОВО — ЖМИ ЗЕЛЁНУЮ» — одной строкой, через ТИРЕ, ровно как CTA опенера.
            var again = driver.FinalePanel.transform.Find("AgainPlate").GetComponent<Image>();
            var againText = again.transform.Find("AgainText").GetComponent<Text>();
            Assert.AreEqual(GameDriver.FinaleRestartHintText, againText.text,
                "CTA финала — та же формула управления, что у опенера, и через «—» (U+2014)");
            // r5 п.2: строка называет ОРГАН целиком («зелёную кнопку»), а не цвет-прилагательное.
            StringAssert.Contains("ЗЕЛЁНУЮ КНОПКУ", againText.text,
                "CTA финала называет орган словарём стойки, а не голое «ЗЕЛЁНУЮ»");
            AssertGeneratedInPill(againText, again, BarTrackPill, "«" + GameDriver.FinaleRestartHintText + "»");
            AssertTokenGreen(again, "finale restart CTA");

            // (4b) …и CTA стоит НИЖЕ запечённой плашки, не накрывая её (место выбрано по композиции).
            var ctaRt = again.rectTransform;
            float ctaTop = (1f - ctaRt.anchorMin.y) * 1080f - ctaRt.sizeDelta.y / 2f;
            float ctaBottom = (1f - ctaRt.anchorMin.y) * 1080f + ctaRt.sizeDelta.y / 2f;
            Assert.GreaterOrEqual(ctaTop, GameDriver.FinalePlateBottom,
                "CTA не перекрывает запечённую плашку");
            Assert.LessOrEqual(ctaBottom, 1080f, "…и не свисает за нижний край кадра");

            // (4c) Цепочка боксов: оба текстовых ректа ⊆ безопасный бокс ⊆ ЗАМЕРЕННОЕ кремовое поле.
            // Кремового поля мало как приёмки: оно не прямоугольник (скруглённые углы + три запечённые
            // звезды-выкуса), и текст, легший на выкус, читался бы «на звезде», а не на креме.
            // Строка исхода живёт в СВОЁМ боксе с 2026-09-22 (вторая итерация фикса финала): она уехала на
            // общую с некрологом колонку x 415…1511, то есть левее общего бокса (его левый край 430).
            // Полосе исхода (y 330…452) не мешает ни одна из трёх звёзд — левая начинается на y 474.
            // Допуск 0.05 px — round-trip нормализованного anchor (см. AssertRefBoxInside): рект исхода
            // РАВЕН своему боксу, запаса на округление у него нет.
            AssertRefBoxInside(RectOf(driver.FinaleOutcomeText.rectTransform), GameDriver.FinaleHeadBox,
                "рект строки исхода — в безопасном боксе своей полосы", 0.05f);
            AssertRefBoxInside(GameDriver.FinaleHeadBox, BakedCreamField,
                "…и бокс полосы исхода — внутри кремового поля");
            // Некролог живёт в СВОЁМ боксе: он шире общего (1104 против 1058), потому что из трёх
            // звёзд-выкусов в его полосу вгрызается только левая. Цепочка приёмки та же, что была,
            // просто звеньев теперь два (дизайн-гейт 2026-08-08, MINOR).
            AssertRefBoxInside(RectOf(driver.FinaleStoryText.rectTransform), GameDriver.FinaleStoryBox,
                "рект некролога — в безопасном боксе своей полосы");
            AssertRefBoxInside(GameDriver.FinaleTextBox, BakedCreamField,
                "безопасный бокс — внутри замеренного кремового поля end.png");
            AssertRefBoxInside(GameDriver.FinaleStoryBox, BakedCreamField,
                "…и бокс полосы некролога — тоже внутри кремового поля");

            // …и расширяли его НЕ «до упора по крему», а ДО ЗВЕЗДЫ. Полоса некролога перекрывается с левой
            // запечённой звездой (x ≤ 403 на y 474…556) на своих первых 49 px, поэтому потолок ширины
            // ставит звезда, а не кромка крема. Проверяем ОБА выкуса явно и по факту перекрытия — иначе
            // следующее расширение «там же ещё много крема» посадит первую строку прямо на звезду.
            var storyBox = GameDriver.FinaleStoryBox;
            float storyLeft = storyBox.x - storyBox.z / 2f, storyRight = storyBox.x + storyBox.z / 2f;
            float storyTop = storyBox.y - storyBox.w / 2f, storyBottom = storyBox.y + storyBox.w / 2f;

            if (storyTop < 556f && storyBottom > 474f)
                Assert.GreaterOrEqual(storyLeft, 403f,
                    $"левый край полосы некролога ({storyLeft:0.#}) не заходит на левую звезду-выкус (x ≤ 403)");
            if (storyTop < 865f && storyBottom > 833f)
                Assert.LessOrEqual(storyRight, 1411f,
                    $"правый край полосы некролога ({storyRight:0.#}) не заходит на правые звёзды (x 1411…1439)");

            // ТОТ ЖЕ разбор для полосы ИСХОДА. Сегодня она звёзд не касается (y 330…452 кончается за 22 px
            // до левой звезды), и именно поэтому её можно было пустить на колонку x 415. Сдвиньте полосу
            // вниз — и проверка перестанет быть холостой ровно в тот момент, когда это станет важно.
            var headBox = GameDriver.FinaleHeadBox;
            float headLeft = headBox.x - headBox.z / 2f, headRight = headBox.x + headBox.z / 2f;
            float headTop = headBox.y - headBox.w / 2f, headBottom = headBox.y + headBox.w / 2f;

            if (headTop < 556f && headBottom > 474f)
                Assert.GreaterOrEqual(headLeft, 403f,
                    $"левый край полосы исхода ({headLeft:0.#}) не заходит на левую звезду-выкус (x ≤ 403)");
            if (headTop < 865f && headBottom > 833f)
                Assert.LessOrEqual(headRight, 1411f,
                    $"правый край полосы исхода ({headRight:0.#}) не заходит на правые звёзды (x 1411…1439)");

            // …и бокс симметричен оси плашки: текст в нём центрирован, перекос увёл бы блок с оси.
            Assert.AreEqual(BakedCreamField.x, storyBox.x, 1f, "бокс некролога стоит на оси кремового поля");

            // (5) Перепись спрайтов: фон + кант CTA + сама CTA. Второй плашки НЕ рисуется (она запечена).
            var expected = new List<string> { "bar-track", "bar-track", "finale-bg-v2" };
            CollectionAssert.AreEqual(expected, SpriteNames(driver.FinalePanel),
                "финал рисует ровно фон + кант и плашку CTA — никакого второго слоя плашки/заголовка");

            // (6) Перепись текстов: исход + некролог + подпись CTA. Заголовка НЕТ — он запечён в фоне.
            AssertExactTexts(driver.FinalePanel, 3, "finale");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Лимит строк против ВЫСОТЫ запечённого поля: длиннейший РЕАЛЬНЫЙ некролог колоды (15 строк
        /// лимита scenes-table кол.11–12) обязан влезть в плашку целиком и при этом не упереться в пол
        /// ужатия — иначе «влез» означало бы «нечитаемо».
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_LongestRealNecrolog_FitsTheBakedPlate_AboveTheLegibilityFloor()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var worst = LongestRealStory();
            Assert.AreEqual(Necrolog.MaxLines, worst.StoryLines.Count,
                "худший случай действительно упирается в лимит строк");
            driver.DebugRenderFinale(worst, 100);
            yield return null;

            AssertGlyphsInRefBox(driver.FinaleOutcomeText, BakedCreamField, 0f, "исход (худший случай)");
            AssertGlyphsInRefBox(driver.FinaleStoryText, BakedCreamField, 0f, "длиннейший реальный некролог");
            AssertNoTofu(driver.FinaleStoryText, "длиннейший реальный некролог");

            // …и подбор кегля НЕ УПЁРСЯ в пол читаемости. Проверяем прямо: набираем ту же строку кеглем
            // ПОЛА в тот же бокс и меряем нужную высоту. Влезло → подборщик (он идёт сверху вниз и берёт
            // САМЫЙ КРУПНЫЙ влезающий кегль) заведомо выбрал ≥ пола. Не влезло бы — «влез» означало бы
            // «нечитаемо», и резать надо лимит строк, а не кегль.
            var t = driver.FinaleStoryText;
            float boxW = t.rectTransform.rect.width, boxH = t.rectTransform.rect.height;
            bool bf = t.resizeTextForBestFit;
            int fs = t.fontSize;
            t.resizeTextForBestFit = false;
            t.fontSize = GameDriver.FinaleStoryMinSize;
            var floorSettings = t.GetGenerationSettings(new Vector2(boxW, 0f));
            float needed = t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, floorSettings)
                           / t.pixelsPerUnit;
            t.fontSize = fs;
            t.resizeTextForBestFit = bf;
            Assert.LessOrEqual(needed, boxH,
                $"на пороге читаемости {GameDriver.FinaleStoryMinSize} px худший некролог всё ещё влезает "
                    + $"в поле плашки (нужно {needed:0.#} px из {boxH:0.#}) — значит best-fit взял кегль КРУПНЕЕ порога");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ОДНА ВЕХА — ОДНА СТРОКА НА ЭКРАНЕ (дизайн-гейт 2026-08-08, MAJOR). Блочный uGUI-best-fit ужимал
        /// текст только по ВЫСОТЕ, поэтому слишком широкая строка худшего случая спокойно переносилась и
        /// давала сироту («жизнь.», ≈101 px) ПОСЕРЕДИНЕ некролога: ломался и смысл строки, и вертикальный
        /// ритм блока. Кегль подбирается построчно — визуальных строк ровно столько, сколько строк истории.
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_EveryNecrologLine_RendersAsExactlyOneVisualLine(
            [Values("worst-real", "worst-synthetic")] string which)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugRenderFinale(which == "worst-real" ? LongestRealStory() : LongStory(), 100);
            yield return null;

            var t = driver.FinaleStoryText;
            int rows = t.text.Split('\n').Length;
            // ⚠ 2026-09-22 ПЕРЕСПЕК: было `MaxLines + 1` — «зачин + лимит». Зачина нет, и ряд под него
            // тоже: рядов ровно столько, сколько вех. Ослаблением это не является — наоборот, равенство
            // стало точным (лишний ряд теперь краснит, а раньше был обязателен).
            Assert.AreEqual(Necrolog.MaxLines, rows,
                "худший случай набран целиком: лимит строк истории, и ни одного ряда сверх них");

            // Меряем ТЕМ ЖЕ генератором и теми же настройками, какими подпись рисуется, — включая кегль,
            // который проставил подборщик, и персональные `<size>` на ужатых строках.
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.AreEqual(rows, tg.lineCount,
                $"[{which}] визуальных строк ровно столько, сколько строк истории — ни одного переноса "
                    + "и ни одной сироты в середине блока");

            // ⚠ ОДНОГО lineCount МАЛО. Генератор отвечает «переноса нет» и тогда, когда живой кадр строку
            // ВСЁ РАВНО переносит: рисуется подпись при другом масштабе канваса, шрифт растеризуется в
            // другой сетке (поймано глазами на `inc00-finalelong`, headless был зелёный). Поэтому проверяем
            // ЗАПАС: каждая строка обязана лечь в поле, оставив боковое поле — ровно тот критерий, по
            // которому подборщик и работает. Без запаса тест снова стал бы слепым к сироте.
            //
            // ⚠ И МЕРИТЬ ЕГО НАДО В СЕТКЕ КАБИНЕТА (2026-08-08). Раньше здесь стояло деление на
            // `pixelsPerUnit`, то есть замер в масштабе батч-окна — он занижает ширину примерно на 7 %,
            // и «запас» 0.93 компенсировал именно эту ошибку, а не поле. Гард мерил ту же кривую линейку,
            // что и подборщик, поэтому и не видел, что строки в кабинете переносятся.
            float boxW = t.rectTransform.rect.width;
            float limit = GameDriver.FinaleStoryLineLimit(boxW);
            foreach (var row in t.text.Split('\n'))
            {
                var plain = System.Text.RegularExpressions.Regex.Replace(row, "</?size(=\\d+)?>", "");
                int size = t.fontSize;
                var m = System.Text.RegularExpressions.Regex.Match(row, "<size=(\\d+)>");
                if (m.Success) size = int.Parse(m.Groups[1].Value);

                float w = GameDriver.PreferredRowWidth(t, plain, size);

                Assert.LessOrEqual(w, limit,
                    $"[{which}] строка «{plain.Substring(0, System.Math.Min(30, plain.Length))}…» ложится "
                        + $"в поле с запасом ({w:0.#} из {limit:0.#} px при кегле {size})");
            }

            // …и ужимали именно строку, а не всю плашку: кегль блока остался в своём коридоре.
            Assert.That(t.fontSize, Is.InRange(GameDriver.FinaleStoryMinSize, GameDriver.FinaleStoryMaxSize),
                "кегль блока — внутри задокументированного коридора читаемости");
            AssertGlyphsInRefBox(t, BakedCreamField, 0f, "некролог без переносов (" + which + ")");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ПОЛНЫЙ НЕКРОЛОГ ДОРИСОВЫВАЕТСЯ ДО ПОСЛЕДНЕЙ ВЕХИ — на кадре КАБИНЕТА ровно
        /// <see cref="Necrolog.MaxLines"/> визуальных рядов (семь вех; до 2026-09-22 было «+1» на зачин).
        ///
        /// ⚠ РЕГРЕССИЯ 2026-08-08, ПОЙМАНА ГЛАЗАМИ ПО КАДРУ, А НЕ ТЕСТАМИ. Подбор кегля мерил высоту блока
        /// делением на <see cref="Text.pixelsPerUnit"/>, т.е. в сетке ТЕКУЩЕГО канваса. В батч-прогоне это
        /// 0.3849, и восемь рядов кеглем 34 меряются как 311.8 px — влезают в поле 314. На кадре кабинета
        /// (scaleFactor 1) тем же восьми рядам нужно 320 px, и `verticalOverflow = Truncate` МОЛЧА срезал
        /// седьмую веху. Suite при этом был зелёный: все проверки финала мерили в том же лгущем масштабе.
        ///
        /// Поэтому тест: (1) собирает кадр в обычном для батча масштабе — как это делает капчер и как это
        /// делает игра до первого кадра; (2) ТОЛЬКО ПОТОМ ставит канвас в масштаб кабинета; (3) считает
        /// ряды у ТОГО ЖЕ генератора и с тем же `Truncate`, каким подпись рисуется. Подбор кегля обязан
        /// пережить смену масштаба — именно этого он и не делал.
        ///
        /// Три случая: обычная жизнь (кадр `finale` — самый жёсткий по ВЫСОТЕ: ни один ряд не ужат
        /// персонально), реальный худший случай колоды и синтетический (кадр `finalelong`).
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_FullNecrolog_DrawsEveryMilestone_OnTheCabinetCanvas(
            [Values("ordinary", "worst-real", "worst-synthetic")] string which)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var story = which == "ordinary" ? OrdinaryFullStory()
                      : which == "worst-real" ? LongestRealStory()
                      : LongStory();
            Assert.AreEqual(Necrolog.MaxLines, story.StoryLines.Count,
                $"[{which}] случай действительно набирает ПОЛНЫЙ лимит вех — иначе гард ничего не стережёт");

            driver.DebugRenderFinale(story, 78);
            yield return null;
            yield return PinCabinetCanvas(driver);

            var t = driver.FinaleStoryText;
            int rows = t.text.Split('\n').Length;
            // ⚠ 2026-09-22 ПЕРЕСПЕК «интро + 7 вех = 8 рядов» → «7 вех = 7 рядов». Суть гарда не тронута:
            // он стережёт, что `verticalOverflow = Truncate` не срезал ПОСЛЕДНЮЮ веху молча. Изменилось
            // только число рядов, потому что два из них были запечённой подводкой, а не содержанием.
            Assert.AreEqual(Necrolog.MaxLines, rows,
                $"[{which}] в подпись отдан весь некролог: {Necrolog.MaxLines} вех и ничего сверх них");

            // (1) РЯДОВ НА ЭКРАНЕ — ровно столько же. Настройки берём у самой подписи, т.е. вместе с её
            // `verticalOverflow = Truncate`: если блок не влез в поле, генератор отдаст меньше рядов —
            // ровно то, что произошло с седьмой вехой.
            var drawn = t.GetGenerationSettings(t.rectTransform.rect.size);
            Assert.AreEqual(VerticalWrapMode.Truncate, drawn.verticalOverflow,
                "подпись всё ещё режет по высоте — значит недобор рядов возможен и его надо стеречь");
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, drawn);
            Assert.AreEqual(rows, tg.lineCount,
                $"[{which}] на кадре кабинета нарисованы ВСЕ {rows} рядов ({Necrolog.MaxLines} вех), "
                    + "ни один не срезан по высоте");

            // (2) …и срезано не «полряда»: знаков нарисовано столько же, сколько без ограничения по высоте.
            var unclipped = t.GetGenerationSettings(t.rectTransform.rect.size);
            unclipped.verticalOverflow = VerticalWrapMode.Overflow;
            int drawnGlyphs = tg.characterCountVisible;
            tg.Populate(t.text, unclipped);
            Assert.AreEqual(tg.characterCountVisible, drawnGlyphs,
                $"[{which}] ни один знак некролога не потерян при отрисовке в поле плашки");

            // (3) …и это не совпадение: подобранный кегль честно влезает по МЕРКЕ КАБИНЕТА. Считаем ту же
            // высоту, что и подборщик (scaleFactor = масштаб кабинета) — при возврате замера в текущий
            // масштаб канваса это число расходится с полем, и тест краснеет.
            var fit = t.GetGenerationSettings(new Vector2(t.rectTransform.rect.width, 0f));
            fit.fontSize = t.fontSize;
            fit.resizeTextForBestFit = false;
            fit.richText = true;
            fit.scaleFactor = GameDriver.CabinetCanvasScale;
            float needed = t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, fit);
            Assert.LessOrEqual(needed, t.rectTransform.rect.height,
                $"[{which}] блок кеглем {t.fontSize} влезает в поле по мерке кабинета "
                    + $"({needed:0.#} px из {t.rectTransform.rect.height:0.#})");
            // (4) …и влез НЕ «схлопыванием в пол». Пол — это аварийный выход подборщика («не сошлось ни на
            // одном кегле»), а не рабочий результат: упор в него означает «контент перерос плашку», и
            // резать тогда надо ЛИМИТ СТРОК, а не кегль (см. FinaleStoryMinSize). Без этой проверки гард
            // зелёный и на сломанном подборщике: восемь рядов кеглем 24 влезают в поле с огромным запасом.
            Assert.Greater(t.fontSize, GameDriver.FinaleStoryMinSize,
                $"[{which}] кегль подобран, а не сорвался в аварийный пол {GameDriver.FinaleStoryMinSize}");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ЛЕВАЯ КРОМКА КАЖДОГО ВИЗУАЛЬНОГО РЯДА в 1920×1080 reference px — по ПЕРУ, а не по чернилам.
        ///
        /// ⚠ Мерить выравнивание чернилами нельзя: левый боковой вынос у «Г» и у «Я» разный на несколько
        /// пикселей, и идеально выровненный блок показал бы разброс, которого в вёрстке нет. uGUI
        /// выравнивает именно ПЕРО (<c>UICharInfo.cursorPos</c>), поэтому гард смотрит туда же —
        /// и допуск тогда честно жёсткий, а не подогнанный под шрифт.
        /// </summary>
        /// <summary>
        /// КЕГЛЬ, КОТОРЫМ ТЕКСТ РЕАЛЬНО НАРИСОВАН. При <c>resizeTextForBestFit</c> подобранный кегль живёт
        /// ТОЛЬКО в генераторе — в <c>Text.fontSize</c> остаётся авторский потолок, и наивная проверка
        /// «заголовок крупнее» мерила бы потолок вместо картинки.
        /// </summary>
        private static int EffectiveFontSizePx(Text t)
        {
            if (!t.resizeTextForBestFit) return t.fontSize;
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, t.GetGenerationSettings(t.rectTransform.rect.size));
            return tg.fontSizeUsedForBestFit;
        }

        private static float[] RowLeftKerbsPx(Text t)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.lineCount, 0, t.name + ": рядов нет — мерить нечего");

            float upp = 1f / t.pixelsPerUnit;
            float cx = t.rectTransform.anchorMin.x * 1920f;
            var kerbs = new float[tg.lineCount];
            var chars = tg.characters;
            for (int i = 0; i < tg.lineCount; i++)
            {
                int idx = tg.lines[i].startCharIdx;
                Assert.Less(idx, chars.Count, $"ряд {i}: генератор отдал индекс за пределами набора");
                kerbs[i] = cx + chars[idx].cursorPos.x * upp;
            }
            return kerbs;
        }

        /// <summary>
        /// НЕКРОЛОГ ВЫРОВНЕН ПО ЛЕВОМУ КРАЮ — решение основательницы 2026-09-22 по живым кадрам:
        /// «выравнивание надо по левому краю, а не по центру».
        ///
        /// Центровка давала каждой строке свой отступ слева (строки-то разной длины), и семь законченных
        /// предложений читались рваным столбиком. Гард держит ТРИ вещи, и третья — не косметика:
        ///  (1) все ряды начинаются на одной кромке (±<see cref="LeftKerbTolerancePx"/> px);
        ///  (2) эта кромка — левая кромка РЕКТА, то есть блок прижат, а не «почти прижат»;
        ///  (3) кромка держит зазор до ЗАПЕЧЁННОЙ ЗВЕЗДЫ слева (x ≤ 403 на y 474…556). Левое
        ///      выравнивание — единственный режим, в котором глифы вообще доходят до левой кромки ректа,
        ///      поэтому «не на звезде» впервые стало вопросом про ЧЕРНИЛА, а не про рект.
        ///
        /// И строка ИСХОДА при этом осталась ПО ЦЕНТРУ: её основательница не трогала, а общий «заодно
        /// выровняем весь финал» сломал бы отдельный блок, про который речи не было.
        /// </summary>
        private const float LeftKerbTolerancePx = 2f;

        /// <summary>
        /// Сколько пикселей чернила некролога обязаны держать до запечённой звезды-выкуса (её правая
        /// граница — x 403).
        ///
        /// ⚠ ЭТО ЛИТЕРАЛ, И ЭТО ВАЖНО. Первая редакция гарда требовала
        /// <c>inkLeft ≥ 403 + GameDriver.FinaleStoryLeftInset</c> — то есть мерила ТЕМ ЖЕ ЧИСЛОМ, которое
        /// и двигает текст. Прогон мутаций показал это прямо: снятие левого поля (`FinaleStoryLeftInset`
        /// → 0) уезжало ЗЕЛЁНЫМ, потому что вместе с текстом на те же 8 px уезжал и порог. Требование к
        /// зазору обязано быть внешним по отношению к вёрстке, иначе гард стережёт тавтологию.
        /// Замер на отдаваемом дереве: чернила на 417, звезда до 403 → 14 px, порог 10 оставляет 4 px
        /// запаса и краснеет на 6 px, которые даёт снятое поле.
        /// </summary>
        private const float StarClearancePx = 10f;

        [UnityTest]
        public IEnumerator Finale_Necrolog_IsLeftAligned_EveryRowOnTheSameKerb_ClearOfTheBakedStar(
            [Values("ordinary", "worst-real", "worst-synthetic")] string which)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugRenderFinale(
                which == "ordinary" ? OrdinaryFullStory()
              : which == "worst-real" ? LongestRealStory()
              : LongStory(), 78);
            yield return null;
            yield return PinCabinetCanvas(driver);

            var t = driver.FinaleStoryText;

            // (0) Заголовок — НА ТОЙ ЖЕ КРОМКЕ И ТЕМ ЖЕ ЛИЦОМ (founder 2026-09-22, вторая итерация:
            // «причина конца и ты дожил тоже надо по левому краю. плюс шрифт текста я другой выбрала же»).
            // Это ДВА разных Text — значит «одинаково» приходится проверять, а не наследовать.
            Assert.AreEqual(TextAnchor.UpperLeft, driver.FinaleOutcomeText.alignment,
                "строка исхода выровнена по ЛЕВОМУ краю, как и некролог");
            Assert.AreNotSame(driver.FinaleOutcomeText, t, "исход и некролог — РАЗНЫЕ Text, а не один блок");
            Assert.AreSame(t.font, driver.FinaleOutcomeText.font,
                $"исход и некролог набраны ОДНИМ лицом (некролог «{t.font?.name}», "
                    + $"исход «{driver.FinaleOutcomeText.font?.name}»)");

            // (1) Режим выравнивания — по левому краю, по вертикали по-прежнему по центру бокса.
            Assert.AreEqual(TextAnchor.MiddleLeft, t.alignment,
                "некролог выровнен по ЛЕВОМУ краю (founder 2026-09-22), вертикаль осталась по центру");

            // (2) Ряды стоят на ОДНОЙ кромке. Это и есть проверка «выровнено», независимая от (1):
            // возврат центровки разносит кромки на сотни пикселей (строки разной длины).
            var kerbs = RowLeftKerbsPx(t);
            Assert.GreaterOrEqual(kerbs.Length, 5, $"[{which}] блок многострочный — иначе гард пустой");
            float min = float.MaxValue, max = float.MinValue;
            foreach (var k in kerbs) { if (k < min) min = k; if (k > max) max = k; }
            Assert.LessOrEqual(max - min, LeftKerbTolerancePx,
                $"[{which}] левые кромки всех {kerbs.Length} рядов совпадают "
                    + $"(разброс {max - min:0.##} px при допуске {LeftKerbTolerancePx})");

            // (3) …и это именно ЛЕВАЯ КРОМКА РЕКТА, а не случайное общее место.
            float rectLeft = t.rectTransform.anchorMin.x * 1920f - t.rectTransform.sizeDelta.x / 2f;
            Assert.AreEqual(rectLeft, min, LeftKerbTolerancePx,
                $"[{which}] блок прижат к левой кромке своего ректа ({rectLeft:0.#})");

            // (4) ЗВЕЗДА. Рект и так не левее 407 (это стережёт бокс-гард выше), но глифы ДОХОДЯТ до
            // кромки только теперь — поэтому зазор проверяется по ЧЕРНИЛАМ, самым левым во всём блоке.
            var ink = GlyphBoxOf(t);
            float inkLeft = ink.x - ink.z / 2f;
            Assert.GreaterOrEqual(inkLeft, 403f + StarClearancePx,
                $"[{which}] чернила некролога ({inkLeft:0.#}) держат не меньше {StarClearancePx:0.#} px "
                    + "до запечённой звезды (её правая граница x = 403)");
            AssertGlyphsInRefBox(t, BakedCreamField, 0f, $"некролог по левому краю ({which})");

            // (5) …и на экране НЕТ ПОДВОДКИ — ни зачина, ни всегда-первой строки родителей.
            StringAssert.DoesNotContain("переживайте", t.text, $"[{which}] зачина на экране нет");
            StringAssert.DoesNotContain("прекрасных родителей", t.text,
                $"[{which}] и всегда-первой строки родителей тоже");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ВСЕ ПРИЧИНЫ, КОТОРЫМИ ИГРА УМЕЕТ КОНЧАТЬСЯ — перепись с КОДА, а не выдумка теста:
        /// шесть именованных фаталов (<c>CardLoader.FatalCauses</c>), безымянный фатал («неведомая дичь»,
        /// там же), два шкальных конца (<c>Game.End</c>: здоровье/энергия) и три тона старости
        /// (<c>Game.EndNatural</c>). Длиннейшая — «вы сунули палец в розетку» (25 знаков), она и делает
        /// худшую строку заголовка «Причина конца: вы сунули палец в розетку».
        /// </summary>
        private static readonly string[] EveryCauseTheGameCanEndWith =
        {
            "вы сунули палец в розетку", "белый порошок", "селфи на краю крыши", "за вами пришли",
            "прыжок с гаража", "чужая фирма", "неведомая дичь",
            "здоровье не выдержало", "полное выгорание",
            "весёлая старость", "одинокая старость", "спокойная старость",
        };

        /// <summary>
        /// ЗАГОЛОВОК ФИНАЛА СТОИТ НА КРОМКЕ НЕКРОЛОГА, НАБРАН ЕГО ЛИЦОМ, И ПРИЧИНА НЕ ПЕРЕНОСИТСЯ.
        ///
        /// ⚠ РЕШЕНИЕ ОСНОВАТЕЛЬНИЦЫ 2026-09-22 (вторая итерация фикса финала), дословно: «причина конца и
        /// ты дожил тоже надо по левому краю. плюс шрифт текста я другой выбрала же». Первая итерация
        /// выровняла только список вех, и заголовок остался центрованным Arimo Bold над левым Rubik-Bold —
        /// два разных лица и две разных вертикали на одном экране.
        ///
        /// Гард держит ЧЕТЫРЕ вещи, и ни одна не выводится из другой:
        ///  (1) ЛИЦО заголовка = лицо некролога (тот же ассет, а не «тоже жирный»);
        ///  (2) обе строки заголовка начинаются на ОДНОЙ кромке — то есть он действительно выровнен;
        ///  (3) эта кромка совпадает с кромкой некролога (± <see cref="LeftKerbTolerancePx"/> px) — общая
        ///      колонка <c>GameDriver.FinaleTextColumnLeft</c>, а не два случайно похожих числа;
        ///  (4) строка ПРИЧИНЫ набрана В ОДИН РЯД. Это не косметика: заголовок стоит на best-fit, а
        ///      best-fit с переносом слов «влезает» и в три ряда, просто мельче — и «Причина конца: вы
        ///      сунули палец в» / «розетку» прошло бы все остальные проверки насквозь.
        ///
        /// Иерархия (заголовок крупнее списка) проверяется тут же: одно лицо на два блока снимает
        /// единственный сигнал «это разные уровни», кроме кегля.
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_OutcomeHead_OnTheNecrologKerb_SameFace_CauseNeverWraps(
            [ValueSource(nameof(EveryCauseTheGameCanEndWith))] string cause)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // Возраст 100 — длиннейшая строка возраста («Ты дожил до 100 лет»); история — худшая реальная,
            // чтобы заголовок мерился в той же сцене, где блок под ним работает на пределе.
            var worst = LongestRealStory();
            worst.Cause = cause;
            driver.DebugRenderFinale(worst, 100);
            yield return null;
            yield return PinCabinetCanvas(driver);

            var head = driver.FinaleOutcomeText;
            var body = driver.FinaleStoryText;

            // (1) ЛИЦО — тот же ассет шрифта, что у некролога.
            Assert.AreSame(body.font, head.font,
                $"[{cause}] заголовок набран лицом некролога (у некролога «{body.font?.name}», "
                    + $"у заголовка «{head.font?.name}»)");
            Assert.AreEqual(TextAnchor.UpperLeft, head.alignment, $"[{cause}] заголовок по левому краю");
            AssertNoTofu(head, $"заголовок «{cause}»");

            // (2) Обе строки заголовка — на одной кромке. При центровке «Ты дожил до 100 лет» и
            // «Причина конца: …» расходятся на сотни пикселей: они сильно разной длины.
            var headKerbs = RowLeftKerbsPx(head);
            Assert.AreEqual(2, headKerbs.Length,
                $"[{cause}] заголовок — РОВНО два ряда: возраст и причина. Больше двух означает перенос "
                    + "внутри строки причины, меньше — что одна из строк пропала");
            Assert.AreEqual(headKerbs[0], headKerbs[1], LeftKerbTolerancePx,
                $"[{cause}] обе строки заголовка начинаются на одной кромке "
                    + $"({headKerbs[0]:0.##} и {headKerbs[1]:0.##})");

            // (3) …и это кромка НЕКРОЛОГА. Мерим по перу обоих блоков — одна общая колонка.
            var bodyKerbs = RowLeftKerbsPx(body);
            Assert.AreEqual(bodyKerbs[0], headKerbs[0], LeftKerbTolerancePx,
                $"[{cause}] заголовок ({headKerbs[0]:0.##}) стоит на кромке некролога ({bodyKerbs[0]:0.##})");
            Assert.AreEqual(GameDriver.FinaleTextColumnLeft, headKerbs[0], LeftKerbTolerancePx,
                $"[{cause}] и это ОБЪЯВЛЕННАЯ колонка {GameDriver.FinaleTextColumnLeft:0.#}, "
                    + "а не совпадение");

            // (4) Причина В ОДИН РЯД — проверено ещё и по тексту, независимо от счёта рядов: генератор
            // отдал ровно два ряда, и второй обязан содержать причину ЦЕЛИКОМ.
            StringAssert.Contains("Причина конца: " + cause, head.text,
                $"[{cause}] строка причины на экране целиком");

            // …и заголовок не свесился за крем ни вправо (длинная причина), ни вниз (best-fit).
            AssertGlyphsInRefBox(head, BakedCreamField, 0f, $"заголовок «{cause}»");
            var headInk = GlyphBoxOf(head);
            Assert.LessOrEqual(headInk.x + headInk.z / 2f, GameDriver.FinaleTextColumnRight,
                $"[{cause}] правая кромка заголовка внутри колонки");

            // (5) ИЕРАРХИЯ: одно лицо на два блока — значит уровень различает ТОЛЬКО кегль.
            //
            // ⚠ Мерить `head.fontSize` НЕЛЬЗЯ: заголовок стоит на best-fit, а best-fit НЕ ПИШЕТ подобранный
            // кегль обратно в компонент — там так и лежит авторский потолок 56. Спрашиваем генератор,
            // который и рисует (`fontSizeUsedForBestFit`), иначе гард всегда сравнивал бы 56 с 30 и был бы
            // зелёным даже при заголовке, ужатом в ничто.
            int headSize = EffectiveFontSizePx(head);
            Assert.Greater(headSize, body.fontSize,
                $"[{cause}] заголовок крупнее списка (заголовок {headSize}, список {body.fontSize}) — "
                    + "на общем лице это единственный признак иерархии");
            Assert.GreaterOrEqual(headSize, GameDriver.FinaleOutcomeMinSize,
                $"[{cause}] кегль заголовка ({headSize}) не сорвался в аварийный пол "
                    + $"{GameDriver.FinaleOutcomeMinSize}");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Блок некролога стоит в ОПТИЧЕСКОМ ЦЕНТРЕ доступной области плашки (дизайн-гейт 2026-08-08,
        /// MINOR): зазор «строка исхода → некролог» и зазор «некролог → низ кремового поля» равны. Было
        /// 41 против 83 px — блок читался съехавшим вверх; контейнер сдвинут на +20 px вниз.
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_NecrologBlock_SitsAtTheOpticalCentreOfThePlate()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugRenderFinale(LongestRealStory(), 100);
            yield return null;

            var head = GlyphBoxOf(driver.FinaleOutcomeText);
            var body = GlyphBoxOf(driver.FinaleStoryText);

            float above = (body.y - body.w / 2f) - (head.y + head.w / 2f);      // исход → некролог
            float below = (BakedCreamField.y + BakedCreamField.w / 2f) - (body.y + body.w / 2f);

            Assert.Greater(above, 0f, "некролог не наезжает на строку исхода");
            Assert.Greater(below, 0f, "…и не свисает за низ кремового поля");
            // Допуск 20 px при сдвиге 20: возврат контейнера на старое место разводит зазоры на 40 —
            // тест краснеет. Идеальная симметрия недостижима, глифы у шрифтов разной высоты.
            Assert.AreEqual(above, below, 20f,
                $"зазоры над и под некрологом уравнены (сверху {above:0.#}, снизу {below:0.#} px)");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Каждый тип конца доводится до финала и печатает СВОЮ причину в запечённой плашке.
        ///
        /// ⚠ 2026-09-22: раньше сюда подавался ПУСТОЙ список записей, и блок истории всё равно рисовал
        /// глифы — потому что их давали зачин и строка родителей. То есть половина этого гарда на самом
        /// деле проверяла подводку, а не «короткую историю». Теперь подаётся ОДНА настоящая строка: это и
        /// есть кратчайшая история, которую финал реально показывает.
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_EveryCauseKind_LandsInTheBakedPlate(
            [Values("весёлая старость", "спокойная старость", "одинокая старость",
                    "вы выгорели", "вы сунули палец в розетку", "вас бросили")] string cause)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            driver.DebugRenderFinale(Necrolog.Build(cause, new List<NecrologEntry>
            {
                new NecrologEntry { Age = 8, Order = 0, Line = "Ели жуков и ничего не боялись." },
            }), 41);
            yield return null;

            StringAssert.Contains(cause, driver.FinaleOutcomeText.text, "причина этого конца на экране");
            StringAssert.Contains("Ты дожил до 41 года", driver.FinaleOutcomeText.text,
                "…и возраст исхода стоит в родительном падеже, которого требует «до»");
            AssertGlyphsInRefBox(driver.FinaleOutcomeText, BakedCreamField, 0f, "исход «" + cause + "»");
            AssertGlyphsInRefBox(driver.FinaleStoryText, BakedCreamField, 0f, "короткая история «" + cause + "»");
            AssertNoTofu(driver.FinaleOutcomeText, "исход «" + cause + "»");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// ЗАБЕГ БЕЗ ЕДИНОЙ ВЕХИ НЕ ЛОМАЕТ ЭКРАН. До 2026-09-22 такого случая не существовало: строка
        /// родителей стояла в КАЖДОМ некрологе, поэтому блок истории никогда не был пустым. Сняв её, мы
        /// завели новое состояние — мгновенный FATAL до первого значимого выбора, — и оно обязано быть
        /// ЗАКОННЫМ, а не «пустой Text, который никто не проверял»: о забеге честно говорит одна строка
        /// исхода, а блок истории просто ничего не рисует и никуда не вылезает.
        /// </summary>
        [UnityTest]
        public IEnumerator Finale_WithNoMilestonesAtAll_ShowsTheOutcomeAndAnEmptyStory()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var n = Necrolog.Build("вы сунули палец в розетку", new List<NecrologEntry>());
            Assert.AreEqual(0, n.StoryLines.Count, "без вех история пуста — запечённых строк больше нет");
            driver.DebugRenderFinale(n, 1);
            yield return null;

            // Исход на месте и читается — именно он и несёт всю информацию о таком забеге.
            StringAssert.Contains("Ты дожил до 1 года", driver.FinaleOutcomeText.text);
            StringAssert.Contains("вы сунули палец в розетку", driver.FinaleOutcomeText.text);
            AssertGlyphsInRefBox(driver.FinaleOutcomeText, BakedCreamField, 0f, "исход без вех");

            // Блок истории пуст — и пуст ЧИСТО: ни одного видимого знака, никакого мусора от подводки.
            var t = driver.FinaleStoryText;
            Assert.IsTrue(string.IsNullOrEmpty(t.text), "блок истории пуст, а не «почти пуст»");
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            t.cachedTextGenerator.Populate(t.text, settings);
            Assert.AreEqual(0, t.cachedTextGenerator.characterCountVisible,
                "…и ничего не рисует");

            Object.Destroy(go);
            yield return null;
        }
    }
}
