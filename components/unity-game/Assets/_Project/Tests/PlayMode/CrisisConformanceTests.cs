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
    /// Layer-2 conformance for the midlife-crisis screen (design gate: S6 blitz / S13 impulse). Asserts the
    /// RENDERED result, not activeSelf flags:
    ///  • the «КРИЗИС… БЛИЦ!» rubric plays as a blocking BEAT — banner up + card HIDDEN — and the blitz
    ///    appears only once the beat clears (banner and card are NEVER both visible in one frame);
    ///  • the «ВСЁ НОРМАЛЬНО» plate label is FULLY inside its plate (rendered glyphs ≤ the text rect ⊆ plate);
    ///  • the timer ring is disjoint from the counter badge and the card;
    ///  • the blitz view renders EXACTLY its expected sprite set (no stray empty band / tofu);
    ///  • the S13 impulse warning carries a DRAWN mute icon (a real sprite, not a font glyph) and the
    ///    «СПАСИБО, НЕ НАДО» decline is highlighted.
    /// Time is driven by explicit Game.Tick calls; the banner beat is pumped via DebugPumpHost.
    /// </summary>
    public class CrisisConformanceTests
    {
        private static readonly string[] CrisisIds =
            { "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08" };

        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static string Csv()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            return asset.text;
        }

        // A deck of just the intro milestone I03 + neutral filler, plus the real crisis block — so the run
        // climbs straight to the 45 crisis with «ВСЁ НОРМАЛЬНО» pinned to the LEFT (yes) plate. No depression.
        private static Game CrisisGame(string csv)
        {
            var byId = CardLoader.ParseAll(csv).ToDictionary(c => c.Id);
            var filler = new Card
            {
                Id = "FILL", Question = "FILL?", When = "60", Age = 60, Order = 999,
                YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                NoNecrolog = "жил дальше", Flags = new List<string>(),
            };
            var plan = new DeckPlan
            {
                Deck = new List<Card> { byId["I03"], filler },
                Reserve = new List<Card>(),
                Crisis = CrisisIds.Select(id => byId[id]).ToList(),
                Depression = byId["CR09"],
            };
            return new Game(() => plan, coin: () => false)
            {
                BlitzNormalOnLeftRoll = () => true,   // «ВСЁ НОРМАЛЬНО» → left (yes) plate
                DepressionTriggerRoll = () => false,  // stay on the crisis (no «тёмная полоса» tail)
            };
        }

        // Drive to the crisis blitz; on return the CR00 banner beat is UP (card hidden, game paused).
        private static IEnumerator DriveToCrisis(GameDriver driver, PlayFakeInputSource fake, Game g)
        {
            driver.DebugReplaceGame(g);
            fake.Confirm();                               // opener → playing, I03 (banner beat)
            g.HandleInput(GameInput.AnswerNo);            // resolve I03 directly → age timer on, beat clears

            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "reached the crisis blitz");
        }

        // Own rect corners in canvas-local space (excludes children).
        private static Bounds OwnBounds(RectTransform canvas, RectTransform rt)
        {
            var wc = new Vector3[4];
            rt.GetWorldCorners(wc);
            var b = new Bounds(canvas.InverseTransformPoint(wc[0]), Vector3.zero);
            for (int i = 1; i < 4; i++) b.Encapsulate(canvas.InverseTransformPoint(wc[i]));
            return b;
        }

        private static bool Overlap(Bounds a, Bounds b)
            => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;

        // The plate sprite is a 460×270 9-slice with an 82px border; its colored pill (the visible flat
        // green/red fill the label must sit on) is inset ~55px from EVERY rect edge, independent of the rect
        // size — a fixed property of 9-slice corners. So the pill is the plate rect shrunk by this inset, NOT
        // the Text's own rect (best-fit keeps glyphs inside the text rect even when that rect is far taller
        // than the short pill — which is exactly how the old check passed a label spilling off the pill).
        private const float PillInset = 55f;

        // Real rendered glyph extents (best-fit honoured, via the Text's own generator) in local (rect) units.
        private static Rect GlyphLocalBounds(Text t, out int visibleChars)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            visibleChars = tg.characterCountVisible;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = tg.verts;
            float ppu = Mathf.Max(1f, t.pixelsPerUnit);
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position / ppu;   // -> text-rect-local units
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        // Banner caption: the band is a plain rect, so "fits" = rendered glyphs within the text rect.
        private static void AssertLabelFits(Text t, string what)
        {
            var g = GlyphLocalBounds(t, out int vis);
            var rect = t.rectTransform.rect;
            Assert.Greater(vis, 0, what + " renders glyphs (not empty/tofu-collapsed)");
            Assert.LessOrEqual(g.width, rect.width + 1f, what + " rendered width ≤ the text rect (no overflow)");
            Assert.LessOrEqual(g.height, rect.height + 1f, what + " rendered height ≤ the text rect (no overflow)");
        }

        // Answer plate: assert the label's TEXT RECT sits within the plate's VISIBLE COLORED PILL (the plate
        // rect minus the fixed ~55px 9-slice corner inset) on all four sides — the real "the label is inside
        // the plate" check. Best-fit only ever shrinks glyphs to WITHIN the text rect, so text-rect ⊆ pill
        // guarantees the drawn label lands on the colored pill for ANY best-fit result. The old check compared
        // glyphs to the TEXT RECT itself — which is trivially satisfied and stays green even when that rect is
        // far taller/wider than the short pill, so the label spills «ВСЁ» above the pill and «НОРМАЛЬНО» past
        // its sides (the founder bug). Geometric + deterministic: unlike rasterised glyph metrics it is stable
        // headless. The text is a child of the plate and shares its tilt, so the mapping is rotation-exact.
        private static void AssertLabelFits(Text t, Graphic plate, string what)
        {
            GlyphLocalBounds(t, out int vis);
            Assert.Greater(vis, 0, what + " renders glyphs (not empty/tofu-collapsed)");

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
            float fillL = pr.xMin + PillInset, fillR = pr.xMax - PillInset;
            float fillB = pr.yMin + PillInset, fillT = pr.yMax - PillInset;
            const float tol = 1f;
            Assert.GreaterOrEqual(minX, fillL - tol, what + " text rect within the visible pill (left)");
            Assert.LessOrEqual(maxX, fillR + tol, what + " text rect within the visible pill (right)");
            Assert.GreaterOrEqual(minY, fillB - tol, what + " text rect within the visible pill (bottom)");
            Assert.LessOrEqual(maxY, fillT + tol, what + " text rect within the visible pill (top)");
        }

        [UnityTest]
        public IEnumerator BlitzBeat_BannerAndCard_NeverBothVisible_ThenBlitzConforms()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame(Csv());
            yield return DriveToCrisis(driver, fake, g);

            // (1) Banner beat up: the «КРИЗИС… БЛИЦ!» band is visible and the card is HIDDEN — never both.
            yield return null;
            Assert.IsTrue(driver.HostBannerVisible, "the crisis rubric plays as a banner beat");
            Assert.IsTrue(driver.Game.Paused, "the beat pauses the game");
            Assert.IsFalse(driver.CardRect.gameObject.activeSelf, "the card/blitz thought is hidden on the beat");
            // The band must actually RENDER its caption (the founder «пустой баннер» bug: text set but the
            // rect collapsed so nothing drew). Assert visible glyphs that fit the band.
            Assert.IsFalse(string.IsNullOrEmpty(driver.HostBannerText.text), "banner caption text is set");
            AssertLabelFits(driver.HostBannerText, "banner caption");

            // Clear the beat (and the thought-1 nag bubble) → the blitz appears.
            driver.DebugPumpHost(3f);
            yield return null;
            Assert.IsFalse(driver.HostBannerVisible, "banner gone once the blitz shows");
            Assert.IsFalse(driver.HostBanner.activeSelf, "banner GO hidden in the blitz view");
            Assert.IsTrue(driver.CardRect.gameObject.activeSelf, "the blitz thought is shown once the beat clears");

            var canvas = driver.CanvasRect;

            // (2) Counter «МЫСЛЬ 1/5 · ПРОВАЛОВ 0» on the dark badge.
            Assert.IsTrue(driver.CrisisInfo.activeSelf, "the crisis counter badge is shown");
            StringAssert.Contains("МЫСЛЬ 1/5", driver.CrisisInfoText.text, "counter reads the thought number");
            StringAssert.Contains("ПРОВАЛОВ", driver.CrisisInfoText.text, "counter reads the fail count");

            // (3) «ВСЁ НОРМАЛЬНО» is on the LEFT (yes) plate and its rendered glyphs land fully on the plate's
            // visible colored pill — no spill onto the frame/background on any side.
            Assert.AreEqual("ВСЁ\nНОРМАЛЬНО", driver.YesPlateText.text, "left plate is «ВСЁ НОРМАЛЬНО»");
            AssertLabelFits(driver.YesPlateText, driver.YesPlateImage, "«ВСЁ НОРМАЛЬНО»");

            // (4) Timer ring disjoint from the counter badge and the card.
            var timer = OwnBounds(canvas, (RectTransform)driver.TimerRingFill.transform.parent);
            var counter = OwnBounds(canvas, (RectTransform)driver.CrisisInfo.transform);
            var card = OwnBounds(canvas, driver.CardRect);
            Assert.IsFalse(Overlap(timer, counter), "timer ring must not overlap the counter badge");
            Assert.IsFalse(Overlap(timer, card), "timer ring must not overlap the card");

            // (5) Exhaustive enumeration: the blitz view renders EXACTLY its expected sprites (no stray band).
            var actual = driver.GamePanel.GetComponentsInChildren<Image>(includeInactive: false)
                .Select(i => i.sprite != null ? (string.IsNullOrEmpty(i.sprite.name) ? "<unnamed>" : i.sprite.name) : "<null>")
                .OrderBy(s => s)
                .ToList();
            var expected = new List<string>
            {
                "age-badge",                                        // minimal HUD (age only)
                "timer-ring-track", "timer-ring-track", "timer-ring", "marquee-bulb",  // timer ring
                "marquee-frame-bulbs",                              // blitz thought card
                "plate-yes", "plate-no",                            // the two blitz buttons
                "bar-track",                                        // the dark counter badge
            }.OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(expected, actual,
                "the blitz view renders exactly its expected sprites — no empty rubric band, no stray Image");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Impulse_ShowsDrawnMuteWarning_AndHighlightsDecline()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame(Csv());
            yield return DriveToCrisis(driver, fake, g);

            driver.DebugPumpHost(3f);                     // clear the CR00 beat → blitz answerable
            yield return null;

            for (int i = 0; i < 5; i++) fake.No();        // fail all 5 thoughts (≥2) → impulse round opens
            Assert.AreEqual(CrisisPhase.Impulse, g.Phase, "≥2 fails opened the impulse round");
            yield return null;                            // RenderCrisis lays out the S13 impulse view

            Assert.IsTrue(driver.ImpulseWarning.activeSelf, "the S13 «молчание = ДА» warning is shown");

            // DRAWN mute icon: a real sprite Image, never a font glyph/tofu.
            var mute = driver.ImpulseWarning.transform.Find("MuteIcon");
            Assert.IsNotNull(mute, "the impulse warning carries a mute icon child");
            var muteImg = mute.GetComponent<Image>();
            Assert.IsNotNull(muteImg, "the mute icon is a drawn Image (not a Text glyph)");
            Assert.IsNotNull(muteImg.sprite, "the mute icon has a real sprite (not tofu)");

            // «СПАСИБО, НЕ НАДО» is the highlighted decline (gold), «ДА» sits on the yes plate.
            var noC = driver.NoPlateImage.color;
            Assert.Greater(noC.r, 0.9f, "decline plate highlighted warm (red channel high)");
            Assert.Greater(noC.g, 0.7f, "decline plate highlighted warm (green channel high)");
            Assert.Less(noC.b, 0.6f, "decline plate highlighted gold (blue channel low)");
            StringAssert.Contains("СПАСИБО", driver.NoPlateText.text, "the decline label");
            Assert.AreEqual("ДА", driver.YesPlateText.text, "the yes plate reads «ДА» (поддаться)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
