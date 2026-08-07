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
    ///  • the dome timer is disjoint from the counter badge and the card;
    ///  • the blitz view renders EXACTLY its expected sprite set (no stray empty band / tofu);
    ///  • the S13 impulse warning carries a DRAWN mute icon (a real sprite, not a font glyph) and the
    ///    «СПАСИБО, НЕ НАДО» decline is highlighted.
    /// Time is driven by explicit Game.Tick calls; the host bubble is aged out via DebugPumpHost.
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
        // climbs straight to the 45 crisis with «ВСЁ НОРМАЛЬНО» pinned to one LEVER (normalOnYes = the ДА
        // lever; which screen side that paints is the driver's business). No depression.
        private static Game CrisisGame(string csv, bool normalOnYes = true)
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
                BlitzNormalOnYesRoll = () => normalOnYes,   // pin «ВСЁ НОРМАЛЬНО» to one lever
                DepressionTriggerRoll = () => false,  // stay on the crisis (no «тёмная полоса» tail)
            };
        }

        // Drive to the crisis blitz; on return the CR00 announce bubble is UP (nothing is blocked).
        private static IEnumerator DriveToCrisis(GameDriver driver, PlayFakeInputSource fake, Game g)
        {
            driver.DebugReplaceGame(g);
            fake.Confirm();                               // opener → playing, I03 (обычная карточка-веха)
            g.HandleInput(GameInput.AnswerNo);            // resolve I03 directly → age timer on, beat clears

            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); yield return null; continue; }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "reached the crisis blitz");
            // r3: кризис объявляется ВХОДНЫМ ЭКРАНОМ блица; снимаем его зелёной, как это делает игрок.
            Assert.AreEqual(SpecialMode.Blitz, driver.SpecialModeKind, "вход в блиц объявлен экраном");
            NewScaleTut.ClearSpecial(driver, fake);
            yield return null;
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
        public IEnumerator CrisisAnnounce_RidesTheHostBubble_ThenBlitzConforms()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame(Csv());
            yield return DriveToCrisis(driver, fake, g);

            // (1) Вход в кризис объявляет ВЕДУЩИЙ В ОБЛАЧКЕ: жёлтая рубрика-плашка и её блокирующий бит
            // сняты (плейтест 2026-08-05), поэтому блиц читаем СРАЗУ — игра не встаёт и карточку не прячет.
            yield return null;
            Assert.IsTrue(driver.HostBubbleVisible, "вход в кризис объявлен репликой Ведущего");
            StringAssert.Contains("БЛИЦ", driver.HostBubbleText.text, "…и это именно объявление блица");
            AssertLabelFits(driver.HostBubbleText, "crisis announce");
            Assert.IsFalse(driver.Game.Paused, "объявление НЕ блокирующее — паузы больше нет");
            Assert.IsTrue(driver.CardRect.gameObject.activeSelf, "мысль блица видна сразу, её не прячут");

            // Age the bubble out (thought-1 nag included) so the screenshots below read the resting blitz.
            driver.DebugPumpHost(3f);
            yield return null;
            Assert.IsTrue(driver.CardRect.gameObject.activeSelf, "the blitz thought is shown");

            var canvas = driver.CanvasRect;

            // (2) Counter «МЫСЛЬ 1/5 · ПРОВАЛОВ 0» on the dark badge.
            Assert.IsTrue(driver.CrisisInfo.activeSelf, "the crisis counter badge is shown");
            StringAssert.Contains("МЫСЛЬ 1/5", driver.CrisisInfoText.text, "counter reads the thought number");
            StringAssert.Contains("ПРОВАЛОВ", driver.CrisisInfoText.text, "counter reads the fail count");

            // (3) «ВСЁ НОРМАЛЬНО» is on the ДА/yes plate (since meeting-revisions §9 that is the RIGHT-hand
            // plate on screen — Game.BlitzNormalOnYes names the LEVER, not the screen side; the screen-side ⇄
            // scoring coupling itself is asserted in Blitz_NormalLabelSide_ScoresForThatSidesLever) and its
            // rendered glyphs land fully on the plate's visible colored pill — no spill on any side.
            Assert.AreEqual("ВСЁ\nНОРМАЛЬНО", driver.YesPlateText.text, "the ДА plate reads «ВСЁ НОРМАЛЬНО»");
            AssertLabelFits(driver.YesPlateText, driver.YesPlateImage, "«ВСЁ НОРМАЛЬНО»");

            // (4) Купол-таймер disjoint from the counter badge and the card.
            var timer = OwnBounds(canvas, driver.TimerDomeOutline.rectTransform);
            var counter = OwnBounds(canvas, (RectTransform)driver.CrisisInfo.transform);
            var card = OwnBounds(canvas, driver.CardRect);
            Assert.IsFalse(Overlap(timer, counter), "купол не перекрывает счётчик мыслей");
            Assert.IsFalse(Overlap(timer, card), "купол не перекрывает карточку-мысль");

            // (5) Exhaustive enumeration: the blitz view renders EXACTLY its expected sprites (no stray band).
            var actual = driver.GamePanel.GetComponentsInChildren<Image>(includeInactive: false)
                .Select(i => i.sprite != null ? (string.IsNullOrEmpty(i.sprite.name) ? "<unnamed>" : i.sprite.name) : "<null>")
                .OrderBy(s => s)
                .ToList();
            var expected = new List<string>
            {
                "age-badge-v2",                                     // minimal HUD (age only)
                "timer-dome", "timer-dome", "timer-dome",           // купол: обводка + трек + дуга
                "timer-dome-hand",                                  // …и стрелка-кромка
                "choice-plate-v2",                                  // blitz thought card (art-pack plate)
                "plate-yes", "plate-no",                            // the two blitz buttons
                "bar-track",                                        // the dark counter badge
            }.OrderBy(s => s).ToList();
            CollectionAssert.AreEqual(expected, actual,
                "the blitz view renders exactly its expected sprites — no stray Image");

            Object.Destroy(go);
            yield return null;
        }

        // The screen-side ⇄ scoring coupling of the blitz, asserted through the RENDERED layout only — never
        // through Game's flag name. Since meeting-revisions §9 swapped the plates, Game's «yes/no» names the
        // LEVER while the driver decides which SCREEN side that lever paints; a future re-swap that moved the
        // plates without moving the scoring (or vice versa) would leave every existing assert green while the
        // player pressed the label they saw and got a провал. So: read which plate DRAWS «ВСЁ НОРМАЛЬНО» and
        // where that plate physically sits on the canvas, press the lever belonging to THAT side, and require
        // a hit; the other side's lever must cost a fail. Run for both pinned rolls → both screen sides.
        private IEnumerator AssertNormalLabelSideScores(bool normalOnYesLever)
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            var g = CrisisGame(Csv(), normalOnYesLever);
            yield return DriveToCrisis(driver, fake, g);
            driver.DebugPumpHost(3f);                     // age out the CR00 announce bubble
            yield return null;

            var canvas = driver.CanvasRect;
            float yesX = OwnBounds(canvas, driver.YesPlateImage.rectTransform).center.x;
            float noX = OwnBounds(canvas, driver.NoPlateImage.rectTransform).center.x;
            Assert.AreNotEqual(yesX, noX, "the two plates occupy different screen sides");
            float midX = 0.5f * (yesX + noX);

            // Geometry → lever: the lever whose plate is drawn on the requested screen side.
            GameInput LeverOnSide(bool right) => right == (yesX > noX) ? GameInput.AnswerYes : GameInput.AnswerNo;

            // Which side DRAWS «ВСЁ НОРМАЛЬНО» right now (from the rendered labels + their rect positions).
            bool NormalIsOnScreenRight()
            {
                bool onYesPlate = driver.YesPlateText.text.Contains("НОРМАЛЬНО");
                Assert.AreNotEqual(onYesPlate, driver.NoPlateText.text.Contains("НОРМАЛЬНО"),
                    "«ВСЁ НОРМАЛЬНО» is drawn on exactly one of the two plates");
                return (onYesPlate ? yesX : noX) > midX;
            }

            // (a) pressing the lever of the side that SHOWS «ВСЁ НОРМАЛЬНО» scores — no fail, blitz advances.
            bool normalRight = NormalIsOnScreenRight();
            int failsBefore = g.BlitzFails;
            int thoughtBefore = g.BlitzThoughtNumber;
            fake.Fire(LeverOnSide(normalRight));
            Assert.AreEqual(failsBefore, g.BlitzFails,
                "pressing the lever on the side that RENDERS «ВСЁ НОРМАЛЬНО» ("
                    + (normalRight ? "right" : "left") + ") is a hit, not a провал");
            Assert.AreNotEqual(thoughtBefore, g.BlitzThoughtNumber, "…and the blitz advanced to the next thought");
            yield return null;                            // driver re-renders the next thought's plates

            // (b) …and the OTHER side's lever is a miss on the next thought.
            bool normalRightNow = NormalIsOnScreenRight();
            failsBefore = g.BlitzFails;
            fake.Fire(LeverOnSide(!normalRightNow));
            Assert.AreEqual(failsBefore + 1, g.BlitzFails,
                "pressing the lever on the side that shows «О НЕТ» costs a провал");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Blitz_NormalLabelSide_ScoresForThatSidesLever_NormalOnYesLever()
            => AssertNormalLabelSideScores(normalOnYesLever: true);

        [UnityTest]
        public IEnumerator Blitz_NormalLabelSide_ScoresForThatSidesLever_NormalOnNoLever()
            => AssertNormalLabelSideScores(normalOnYesLever: false);

        // §9 baked art ⇄ crisis code-plates, THERE AND BACK. Ordinary play draws the art-pack plates with the
        // words baked in (dynamic label hidden); the blitz/impulse must swap to the blank code-plates, because
        // they relabel the pair per thought («ВСЁ НОРМАЛЬНО»/«О НЕТ») and gold-highlight the decline — and the
        // moment the crisis ends the baked art must come back (a stuck code-plate = «ДА» silently missing).
        [UnityTest]
        public IEnumerator Plates_SwapToCodePlates_ForTheCrisis_AndBackToBakedArt()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // (1) ordinary play: baked art, no live label.
            Assert.AreEqual("btn-yes", driver.YesPlateImage.sprite.name, "ordinary play draws the baked ДА art");
            Assert.AreEqual("btn-no", driver.NoPlateImage.sprite.name, "ordinary play draws the baked НЕ НАДО art");
            Assert.IsFalse(driver.YesPlateText.gameObject.activeSelf, "the label overlay is hidden (baked words)");
            Assert.IsFalse(driver.NoPlateText.gameObject.activeSelf, "the label overlay is hidden (baked words)");

            var g = CrisisGame(Csv());
            yield return DriveToCrisis(driver, fake, g);
            driver.DebugPumpHost(3f);                     // age out the CR00 announce bubble
            yield return null;

            // (2) blitz: blank code-plates + the live relabel.
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "in the blitz");
            Assert.AreEqual("plate-yes", driver.YesPlateImage.sprite.name, "the blitz uses the blank code-plate");
            Assert.AreEqual("plate-no", driver.NoPlateImage.sprite.name, "the blitz uses the blank code-plate");
            Assert.IsTrue(driver.YesPlateText.gameObject.activeInHierarchy, "the blitz label overlay is VISIBLE");
            Assert.IsTrue(driver.NoPlateText.gameObject.activeInHierarchy, "the blitz label overlay is VISIBLE");
            StringAssert.Contains("НОРМАЛЬНО", driver.YesPlateText.text + driver.NoPlateText.text,
                "one of the blitz plates is relabelled «ВСЁ НОРМАЛЬНО»");

            // (3) clear the blitz cleanly (roll pinned to the ДА lever) → ordinary play resumes…
            for (int i = 0; i < 5; i++) fake.Yes();
            Assert.AreEqual(CrisisPhase.None, g.Phase, "a clean blitz ends the crisis (no impulse)");
            yield return null;                            // RestoreNormalPlates runs on the first normal frame

            // …and the baked art is back, label overlay hidden again.
            Assert.AreEqual("btn-yes", driver.YesPlateImage.sprite.name, "the baked ДА art returns after the crisis");
            Assert.AreEqual("btn-no", driver.NoPlateImage.sprite.name, "the baked НЕ НАДО art returns after the crisis");
            Assert.IsFalse(driver.YesPlateText.gameObject.activeSelf, "the crisis label overlay is hidden again");
            Assert.IsFalse(driver.NoPlateText.gameObject.activeSelf, "the crisis label overlay is hidden again");

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

            driver.DebugPumpHost(3f);                     // age out the CR00 announce bubble
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

            // The impulse keeps the blank code-plates + live labels (the baked art can't carry «поддаться»).
            Assert.AreEqual("plate-yes", driver.YesPlateImage.sprite.name, "the impulse uses the code-plate");
            Assert.AreEqual("plate-no", driver.NoPlateImage.sprite.name, "the impulse uses the code-plate");
            Assert.IsTrue(driver.NoPlateText.gameObject.activeInHierarchy, "the impulse label overlay is VISIBLE");

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
