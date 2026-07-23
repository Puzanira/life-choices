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

        // ---- S3 bubble overflow (playtest fix): the LONGEST host exclamation must sit fully INSIDE the yellow
        // bubble, with the bottom «tail» kept clear. Best-fit + Truncate shrink the line to the text rect; the
        // text rect is inset inside the bubble body above the tail. Reads the REAL drawn glyph mesh (best-fit
        // honoured), same generated-glyph pattern as AssertGeneratedInPill — so a fixed-size Overflow regression
        // (glyphs spilling past the text rect) fails RED.
        private static void AssertGlyphsFitTextRect(Text t, string what)
        {
            var settings = t.GetGenerationSettings(t.rectTransform.rect.size);
            var tg = t.cachedTextGenerator;
            tg.Populate(t.text, settings);
            Assert.Greater(tg.characterCountVisible, 0, what + " renders glyphs (not empty/tofu-collapsed)");

            float upp = 1f / t.pixelsPerUnit;
            var rect = t.rectTransform.rect;
            const float tol = 1f;
            var verts = tg.verts;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                float x = p.x * upp, y = p.y * upp;
                Assert.GreaterOrEqual(x, rect.xMin - tol, what + " drawn glyphs inside the bubble text area (left)");
                Assert.LessOrEqual(x, rect.xMax + tol, what + " drawn glyphs inside the bubble text area (right)");
                Assert.GreaterOrEqual(y, rect.yMin - tol, what + " drawn glyphs inside the bubble text area (bottom, above the tail)");
                Assert.LessOrEqual(y, rect.yMax + tol, what + " drawn glyphs inside the bubble text area (top)");
            }
        }

        [UnityTest]
        public IEnumerator Bubble_LongestHostLine_FitsInsideBubble_TailClear()
        {
            var driver = Boot(out var go, out _);
            yield return null;                                  // Start builds the HUD (bubble + text)

            // The actual longest host line in the shipping deck (≤50 chars, e.g. «Уже умеет заказать кофе…»).
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "Resources/scenes present");
            string longest = CardLoader.ParseAll(csv.text)
                .SelectMany(c => new[] { c.HostYes, c.HostNo })
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderByDescending(s => s.Length)
                .First();
            Assert.GreaterOrEqual(longest.Length, 46, "picked a genuinely long host exclamation");

            var bubbleText = driver.HostBubbleText;
            var bubbleRt = driver.HostBubble.GetComponent<RectTransform>();
            driver.HostBubble.SetActive(true);
            bubbleText.text = longest;
            yield return null;                                  // let the layout settle before reading the mesh

            // The drawn glyphs of the longest line fit the bubble's text area (best-fit shrinks it to fit HEIGHT
            // too via Truncate) — nothing spills outside the yellow bubble.
            AssertGlyphsFitTextRect(bubbleText, "longest host line «" + longest + "»");

            // …and the text RECT itself sits inside the bubble's FLAT gold fill. The «bubble» sprite is 9-slice
            // border 70/120/70/70, so the fill starts 70px in on left/right/top and 120px up from the bottom
            // (the tail + rounded corners live in that border). Glyphs ⊆ text rect ⊆ fill ⇒ nothing spills onto
            // the outline/corners/tail (the earlier fix passed the rect check yet the em-dash floated outside,
            // because the rect stuck out past the fill — this guards that).
            var tc = new Vector3[4];
            bubbleText.rectTransform.GetWorldCorners(tc);   // 0=BL,1=TL,2=TR,3=BR
            var br = bubbleRt.rect;
            float L = bubbleRt.InverseTransformPoint(tc[0]).x, R = bubbleRt.InverseTransformPoint(tc[3]).x;
            float B = bubbleRt.InverseTransformPoint(tc[0]).y, T = bubbleRt.InverseTransformPoint(tc[1]).y;
            Assert.GreaterOrEqual(L, br.xMin + 70f, "text left inside the bubble fill (past the 70px border/corner)");
            Assert.LessOrEqual(R, br.xMax - 70f, "text right inside the bubble fill (em-dash can't float outside)");
            Assert.LessOrEqual(T, br.yMax - 70f, "text top inside the fill (clears the rounded top corners)");
            Assert.GreaterOrEqual(B, br.yMin + 120f, "text bottom clears the tail (120px border)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
