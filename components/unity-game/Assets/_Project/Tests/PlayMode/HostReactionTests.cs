using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The host reactions through the REAL driver: the rubric banner (S4) fires on a TIMELINE milestone
    /// but not on a normal card, auto-advances on its own ~1.5s clock without soft-locking or pausing the
    /// game (so the following tutorial pathway stays free), the speech bubble (S3) shows a named line on
    /// a resolved answer and survives the same-frame card advance, and a restart clears both.
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
        public IEnumerator Banner_FiresOnTimeline_NotNormal_AndDoesNotPause()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            driver.DebugReplaceGame(DeckGame());
            fake.Confirm();                       // → playing, first card N0 (normal)
            yield return null;

            Assert.AreEqual("N0", driver.Game.CurrentCard.Id);
            Assert.IsFalse(driver.HostBannerVisible, "no banner on a normal card");
            Assert.IsFalse(driver.HostBanner.activeSelf, "banner GO hidden on a normal card");

            fake.No();                            // resolve N0 → milestone YA03 becomes current
            yield return null;

            Assert.AreEqual("YA03", driver.Game.CurrentCard.Id);
            Assert.IsTrue(driver.HostBannerVisible, "banner fires on the TIMELINE milestone");
            Assert.IsTrue(driver.HostBanner.activeSelf, "banner GO visible");
            Assert.AreEqual("ПЕРВАЯ ЛЮБОВЬ!", driver.HostBannerText.text, "correct rubric caption");
            Assert.IsFalse(driver.Game.Paused, "banner does NOT pause the game (tutorial pathway stays free)");

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

            // Exhaust the deck to the finale, then restart to a fresh life.
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 50) fake.No();
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
    }
}
