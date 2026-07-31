using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The host reactions through the REAL driver: the rubric banner (S4/S6) fires on a TIMELINE milestone
    /// but not on a normal card, plays as a brief BLOCKING beat (game paused + card hidden — never both up),
    /// auto-advances on its own ~1.5s clock without soft-locking, the speech bubble (S3) shows a named line
    /// on a resolved answer and survives the same-frame card advance, and a restart clears both.
    /// </summary>
    public class HostReactionTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static Card Normal(string id, string hostYes = null) =>
            new Card { Id = id, Question = id + "?", Age = 4, HostYes = hostYes };

        private static Card Milestone(string id) =>
            new Card { Id = id, Question = id + "?", Age = 4, IsTimeline = true };

        // Deck: normal → milestone(YA03) → normal, so we can watch the banner appear on the milestone only.
        private static Game DeckGame() => new Game(new List<Card>
        {
            Normal("N0", hostYes: "Красавчик!"),
            Milestone("YA03"),
            Normal("N2"),
        });

        [UnityTest]
        public IEnumerator Banner_FiresOnTimeline_NotNormal_AndPausesAsBeat_CardHidden()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(DeckGame());
            fake.Confirm();                       // → playing, first card N0 (normal)
            yield return null;

            Assert.AreEqual("N0", driver.Game.CurrentCard.Id);
            Assert.IsFalse(driver.HostBannerVisible, "no banner on a normal card");
            Assert.IsFalse(driver.HostBanner.activeSelf, "banner GO hidden on a normal card");
            Assert.IsTrue(driver.CardRect.gameObject.activeSelf, "card is shown on a normal (non-banner) card");

            fake.No();                            // resolve N0 → milestone YA03 becomes current
            yield return null;

            Assert.AreEqual("YA03", driver.Game.CurrentCard.Id);
            Assert.IsTrue(driver.HostBannerVisible, "banner fires on the TIMELINE milestone");
            Assert.IsTrue(driver.HostBanner.activeSelf, "banner GO visible");
            Assert.AreEqual("ПЕРВАЯ ЛЮБОВЬ!", driver.HostBannerText.text, "correct rubric caption");
            // Blocking beat (S4/S6): the banner pauses the game and the card is HIDDEN — NEVER both up.
            Assert.IsTrue(driver.Game.Paused, "the rubric banner is a blocking beat — it pauses the game");
            Assert.IsFalse(driver.CardRect.gameObject.activeSelf,
                "the card is hidden while the banner beat is up (banner never overlaps the card)");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Banner_AutoAdvances_WithoutSoftLock_CardStaysAnswerable()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(DeckGame());
            fake.Confirm();
            yield return null;
            fake.No();                            // → milestone YA03 + banner
            yield return null;
            Assert.IsTrue(driver.HostBannerVisible, "banner up");

            var card = driver.Game.CurrentCard;
            yield return new WaitForSeconds(GameDriver.BannerSeconds + 0.3f);   // let Update run its clock

            Assert.IsFalse(driver.HostBannerVisible, "banner auto-hid after ~1.5s");
            Assert.IsFalse(driver.HostBanner.activeSelf, "banner GO hidden after auto-advance");
            Assert.AreSame(card, driver.Game.CurrentCard, "milestone card still up (not skipped/soft-locked)");

            fake.No();                            // still answerable → advances
            Assert.AreEqual("N2", driver.Game.CurrentCard.Id, "answer advances past the milestone");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Bubble_ShowsNamedLine_OnAnswer_AndSurvivesCardAdvance()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(DeckGame());
            fake.Confirm();
            yield return null;
            Assert.IsFalse(driver.HostBubbleVisible, "no bubble before any answer");

            fake.Yes();                           // N0 ДА → named bubble «Красавчик!»
            yield return null;

            Assert.IsTrue(driver.HostBubbleVisible, "bubble shows on the resolved answer");
            Assert.IsTrue(driver.HostBubble.activeSelf, "bubble GO visible");
            Assert.AreEqual("Красавчик!", driver.HostBubbleText.text, "named ДА line");
            Assert.AreEqual("YA03", driver.Game.CurrentCard.Id,
                "bubble persists even though the next card was drawn the same frame");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Restart_ClearsBubbleAndBanner()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(DeckGame());
            fake.Confirm();
            yield return null;
            fake.Yes();                           // bubble «Красавчик!» + advance to milestone (banner)
            yield return null;
            Assert.IsTrue(driver.HostBubbleVisible, "bubble up before restart");
            Assert.IsTrue(driver.HostBannerVisible, "banner up before restart");

            // Exhaust the deck to the finale, then restart to a fresh life. A milestone banner is a blocking
            // beat now (swallows input) — pump its ~1.5s clock synchronously so the loop passes through it.
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 50)
            {
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                fake.No();
            }
            Assert.AreEqual(GameState.Finale, driver.Game.State, "reached the finale");
            fake.Confirm();                       // finale → opener
            fake.Confirm();                       // opener → fresh life (fresh-life reset runs)
            yield return null;

            Assert.IsFalse(driver.HostBubbleVisible, "bubble cleared on restart");
            Assert.IsFalse(driver.HostBannerVisible, "banner cleared on restart");
            Assert.IsFalse(driver.HostBubble.activeSelf, "bubble GO hidden on restart");
            Assert.IsFalse(driver.HostBanner.activeSelf, "banner GO hidden on restart");

            Object.Destroy(go);
            yield return null;
        }

        // ---- S3 bubble overflow (playtest fix): the LONGEST host exclamation must sit fully INSIDE the plate's
        // cream fill, never on the rim / the megaphone / the baked star. Best-fit + Truncate shrink the line to
        // the text rect; the text rect is inset past the 9-slice borders. Reads the REAL drawn glyph mesh
        // (best-fit honoured), same generated-glyph pattern as AssertGeneratedInPill — so a fixed-size Overflow
        // regression (glyphs spilling past the text rect) fails RED.
        private static void AssertGlyphsInsideBox(Text t, Rect box, string what, string where)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, what + " renders glyphs (not empty/tofu-collapsed)");

            float upp = 1f / t.pixelsPerUnit;
            // The glyph mesh is in the TEXT rect's local space; the box is given in the BUBBLE's local space,
            // and the text rect is a plain (unrotated, unscaled) child of it — so the offset between them is
            // just the text rect's position inside the bubble.
            var off = (Vector2)t.rectTransform.localPosition;
            const float tol = 1f;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                float x = p.x * upp + off.x, y = p.y * upp + off.y;
                Assert.GreaterOrEqual(x, box.xMin - tol, what + " drawn glyphs stay " + where + " (left)");
                Assert.LessOrEqual(x, box.xMax + tol, what + " drawn glyphs stay " + where + " (right)");
                Assert.GreaterOrEqual(y, box.yMin - tol, what + " drawn glyphs stay " + where + " (bottom)");
                Assert.LessOrEqual(y, box.yMax + tol, what + " drawn glyphs stay " + where + " (top)");
            }
        }

        /// <summary>Every line the bubble can ever carry: the deck's named lines + the whole host-content pool.</summary>
        private static IEnumerable<string> HostBubbleLines()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "Resources/scenes present");
            return CardLoader.ParseAll(csv.text)
                .SelectMany(c => new[] { c.HostYes, c.HostNo })
                .Concat(HostContent.Pool.Values.SelectMany(v => v))
                .Concat(HostContent.BlitzNags)
                .Concat(HostContent.DepressionMutterings)
                .Where(s => !string.IsNullOrEmpty(s));
        }

        /// <summary>The bubble's CREAM fill in bubble-local px: the rect minus the drawn 9-slice borders.</summary>
        private static Rect CreamFill(Image img)
        {
            var b = img.sprite.border;                       // sprite px: x=left, y=bottom, z=right, w=top
            float m = img.pixelsPerUnit * img.pixelsPerUnitMultiplier;   // sprite px → rect px divisor
            var r = img.rectTransform.rect;
            return Rect.MinMaxRect(r.xMin + b.x / m, r.yMin + b.y / m, r.xMax - b.z / m, r.yMax - b.w / m);
        }

        [UnityTest]
        public IEnumerator Bubble_LongestHostLine_StaysOnTheCreamFill_HornAndStarClear()
        {
            var driver = Boot(out var go, out _);
            yield return null;                                  // Start builds the HUD (bubble + text)

            // The actual longest line the bubble can carry (≤50 chars, e.g. «Уже умеет заказать кофе…»).
            string longest = HostBubbleLines().OrderByDescending(s => s.Length).First();
            Assert.GreaterOrEqual(longest.Length, 46, "picked a genuinely long host exclamation");

            var bubbleText = driver.HostBubbleText;
            var bubbleImg = driver.HostBubble.GetComponent<Image>();
            driver.HostBubble.SetActive(true);
            bubbleText.text = longest;
            yield return null;                                  // let the layout settle before reading the mesh

            // The 9-slice contract this all rests on: borders are authored in SPRITE px (340 on the megaphone
            // side, 80 elsewhere) and drawn at the art's own scale, so the stretched middle IS the cream fill.
            var b = bubbleImg.sprite.border;
            Assert.AreEqual(340f, b.x, 0.5f, "left 9-slice border covers the megaphone (asset-map §5)");
            Assert.AreEqual(80f, b.y, 0.5f, "bottom 9-slice border");
            Assert.AreEqual(80f, b.z, 0.5f, "right 9-slice border");
            Assert.AreEqual(80f, b.w, 0.5f, "top 9-slice border");
            Assert.AreEqual(Image.Type.Sliced, bubbleImg.type, "the plate is 9-sliced, not stretched whole");

            // (b) The DRAWN glyphs of the longest line sit inside the cream fill — the plate minus its borders.
            // Glyphs ⊆ cream fill ⇒ nothing lands on the gold rim, on the megaphone, or on the baked star (the
            // earlier fix passed a rect check yet the em-dash floated outside, because the RECT stuck out).
            AssertGlyphsInsideBox(bubbleText, CreamFill(bubbleImg),
                "longest host line «" + longest + "»", "on the plate's cream fill");

            // …and the text RECT itself is inset past every border, so the guard above cannot be satisfied by a
            // lucky short line: the whole text AREA is on the fill, horn zone included.
            var fill = CreamFill(bubbleImg);
            var trt = bubbleText.rectTransform;
            var local = new Rect((Vector2)trt.localPosition - trt.rect.size * 0.5f, trt.rect.size);
            Assert.GreaterOrEqual(local.xMin, fill.xMin - 0.01f, "text left past the megaphone border — no text over the horn");
            Assert.LessOrEqual(local.xMax, fill.xMax + 0.01f, "text right past the right border (and clear of the baked star)");
            Assert.LessOrEqual(local.yMax, fill.yMax + 0.01f, "text top past the top border (clears the rounded corners)");
            Assert.GreaterOrEqual(local.yMin, fill.yMin - 0.01f, "text bottom past the bottom border");

            // Every OTHER line the bubble can show fits too (the longest one is not a lucky special case).
            foreach (var line in HostBubbleLines())
            {
                bubbleText.text = line;
                AssertGlyphsInsideBox(bubbleText, fill, "host line «" + line + "»", "on the plate's cream fill");
            }

            Object.Destroy(go);
            yield return null;
        }

        // ---- S3 reply TYPE (design gate, round 3): weight and kegl, measured on the explainer -------------
        // The bubble's copy came out half the explainer's weight because `Rubik.ttf` is a VARIABLE font whose
        // wght axis DEFAULTS to 300 (Light) — legacy uGUI rasterises that default, silently. Re-measured on
        // `explainers/Экран - комментарий ведущего.png` by isolating the GLYPH components inside the plate
        // (dropping the plate's own black outline, which doubles a naive reading): «Ну ты и тип!» draws at a
        // cap-height of 27.0 px de-tilted with a mean stroke of 5.9 px, i.e. stroke/cap 0.219 — Rubik hits
        // 0.216 at wght 700 and 0.081 at 300. Hence a static wght=700 instance cut from the same OFL file.

        /// <summary>usWeightClass out of a TTF's OS/2 table, read straight off disk (big-endian sfnt).</summary>
        private static int WeightClassOf(string ttf)
        {
            var b = File.ReadAllBytes(ttf);
            int U16(int o) => (b[o] << 8) | b[o + 1];
            int U32(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            int tables = U16(4);
            for (int i = 0; i < tables; i++)
            {
                int rec = 12 + i * 16;
                string tag = System.Text.Encoding.ASCII.GetString(b, rec, 4);
                if (tag != "OS/2") continue;
                return U16(U32(rec + 8) + 4);                 // usWeightClass, 4 bytes into the table
            }
            Assert.Fail("no OS/2 table in " + ttf);
            return 0;
        }

        [UnityTest]
        public IEnumerator BubbleReply_IsDrawnBold_AtTheExplainersKegl()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            var t = driver.HostBubbleText;
            Assert.AreEqual("Rubik-Bold", t.font.name,
                "реплика Ведущего рисуется ЖИРНЫМ начертанием Rubik (вариативный Rubik.ttf по умолчанию — Light 300)");

            // …and that asset really is a 700 cut, not a renamed copy of the Light file.
            string path = Path.Combine(Application.dataPath, "_Project/Art/Resources/Fonts/Rubik-Bold.ttf");
            Assert.IsTrue(File.Exists(path), "статический инстанс на диске: " + path);
            Assert.AreEqual(700, WeightClassOf(path), "Rubik-Bold.ttf — это инстанс wght=700");
            Assert.AreEqual(300, WeightClassOf(Path.Combine(Application.dataPath,
                    "_Project/Art/Resources/Fonts/Rubik.ttf")),
                "…и он ОТЛИЧАЕТСЯ от вариативного Rubik.ttf, который грузится как Light 300");

            // Kegl: a short reply draws at the explainer's cap (27 px ÷ Rubik's capHeight 0.70 em ≈ 39).
            Assert.AreEqual(GameDriver.BubbleTextMaxSize, t.resizeTextMaxSize, "кегль-потолок реплики");
            Assert.AreEqual(39, GameDriver.BubbleTextMaxSize,
                "39 px Rubik = cap-height 27 px эталона (замер по глифам внутри плашки)");

            driver.HostBubble.SetActive(true);
            t.text = "Скорее!";
            yield return null;

            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            // best-fit reports the size in SCREEN px, i.e. through the canvas scale (a batch game view is not
            // 1920×1080, so scaleFactor ≠ 1 here — the verts below are converted back to reference px).
            float canvasScale = driver.CanvasRect.GetComponent<Canvas>().scaleFactor;
            Assert.GreaterOrEqual(tg.fontSizeUsedForBestFit,
                Mathf.FloorToInt(GameDriver.BubbleTextMaxSize * canvasScale),
                "короткая реплика рисуется в ПОЛНЫЙ кегль (best-fit её не ужимает)");

            // The DRAWN band of a short reply: cap-height class, not the old ~24 px.
            float upp = 1f / t.pixelsPerUnit, minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                float y = verts[i].position.y * upp;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            Assert.GreaterOrEqual(maxY - minY, 27f,
                "нарисованная строка «Скорее!» по высоте — как на эталоне (≥27 px cap-height), а не "
                + (maxY - minY).ToString("F1"));

            Object.Destroy(go);
            yield return null;
        }

        // ---- S3 bubble ART (design gate): the plate the player SEES, not the rect it lives in ----------------
        // Reads the imported PNG's own alpha, maps every opaque texel through the REAL 9-slice layout and the
        // REAL tilt, and compares the resulting silhouette with the box measured on the explainer. Rect-only
        // asserts stayed green while the bubble drew as the old flat-orange `bubble` sprite — this cannot.

        /// <summary>Sprite px → rect px through the 9-slice layout (borders fixed, middle stretched).</summary>
        private static float Slice(float v, float texSize, float lo, float hi, float rectSize, float m)
        {
            float dLo = lo / m, dHi = hi / m;
            if (v <= lo) return v / m;
            if (v >= texSize - hi) return rectSize - (texSize - v) / m;
            return dLo + (v - lo) / (texSize - lo - hi) * (rectSize - dLo - dHi);
        }

        [UnityTest]
        public IEnumerator Bubble_DrawnPlate_LandsOnTheExplainerBox()
        {
            var driver = Boot(out var go, out _);
            yield return null;

            var bubbleImg = driver.HostBubble.GetComponent<Image>();
            var rt = bubbleImg.rectTransform;
            driver.HostBubble.SetActive(true);
            yield return null;

            Assert.AreEqual("host-comment-v2", bubbleImg.sprite.name,
                "the bubble draws the art-pack megaphone plate, not the old programmer `bubble`");
            Assert.AreEqual(Color.white, bubbleImg.color,
                "the plate keeps its own cream+gold (no tint — asset-map §12-2)");

            // Tilt: POSITIVE = counter-clockwise = the plate's right end higher, as the explainer draws it.
            float tilt = Mathf.DeltaAngle(0f, rt.localEulerAngles.z);
            Assert.AreEqual(GameDriver.BubbleTiltDeg, tilt, 1f,
                "plate tilt +14.5° CCW (right end higher) — measured on the explainer");

            // The imported PNG, read straight off disk: the sprite atlas texture is not Read/Write enabled
            // (project default, asset-map §6.2), and the point here is to measure the SHIPPED pixels.
            string path = Path.Combine(Application.dataPath,
                "_Project/Art/Resources/Sprites/host-comment-v2.png");
            Assert.IsTrue(File.Exists(path), "imported bubble PNG on disk: " + path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(path)), "bubble PNG decodes");
            int tw = tex.width, th = tex.height;
            Assert.AreEqual(1445, tw, "source art width");
            Assert.AreEqual(506, th, "source art height");

            var border = bubbleImg.sprite.border;
            float mult = bubbleImg.pixelsPerUnit * bubbleImg.pixelsPerUnitMultiplier;
            var rect = rt.rect;
            float rad = tilt * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

            // Rect centre in 1920×1080 reference px (x from LEFT, y from TOP). A batch game view is not 16:9,
            // so POSITIONS have to be mapped through the canvas while local extents already are reference px.
            var canvas = driver.CanvasRect;
            var c = canvas.InverseTransformPoint(rt.position);
            var centre = new Vector2((c.x / canvas.rect.width + 0.5f) * 1920f,
                                     (0.5f - c.y / canvas.rect.height) * 1080f);

            var px = tex.GetPixels32();                       // row 0 = BOTTOM of the texture
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            int opaque = 0;
            for (int y = 0; y < th; y++)
            {
                float ly = Slice(y, th, border.y, border.w, rect.height, mult) - rect.height * 0.5f;
                int row = y * tw;
                for (int x = 0; x < tw; x++)
                {
                    if (px[row + x].a <= 200) continue;
                    opaque++;
                    float lx = Slice(x, tw, border.x, border.z, rect.width, mult) - rect.width * 0.5f;
                    // local (x right, y up) → reference px (x right, y DOWN), rotated by the plate's tilt
                    float rx = centre.x + lx * cos - ly * sin;
                    float ry = centre.y - (lx * sin + ly * cos);
                    if (rx < minX) minX = rx;
                    if (rx > maxX) maxX = rx;
                    if (ry < minY) minY = ry;
                    if (ry > maxY) maxY = ry;
                }
            }
            Object.Destroy(tex);
            Assert.Greater(opaque, 300000, "the plate really is drawn (opaque art, not an empty texture)");

            // Measured on `explainers/Экран - комментарий ведущего.png`: the WHOLE visible glyph (cream plate +
            // gold rim + ink outline + megaphone) occupies 198,197,512,255 — its plate alone is 257,201,453,248.
            const float Tol = 10f;
            Assert.AreEqual(198f, minX, Tol, "drawn bubble LEFT edge (megaphone tip) on the explainer's 198");
            Assert.AreEqual(197f, minY, Tol, "drawn bubble TOP edge on the explainer's 197");
            Assert.AreEqual(512f, maxX - minX + 1f, Tol, "drawn bubble WIDTH = the explainer's 512");
            Assert.AreEqual(255f, maxY - minY + 1f, Tol, "drawn bubble HEIGHT = the explainer's 255");

            Object.Destroy(go);
            yield return null;
        }
    }
}
