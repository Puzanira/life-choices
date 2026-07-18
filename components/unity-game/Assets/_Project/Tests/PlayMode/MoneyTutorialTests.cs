using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The S5 money tutorial and the Space re-map through the REAL driver: overlay on first open,
    /// full freeze (age + drains + card timer) while up, INSTANT Space dismiss right after cranking
    /// (the skeptic's swallow scenario), instant Space-confirm in Opener/Finale, mid-timer card
    /// resume, and tutorial re-arm on restart. Time is driven by explicit Game.Tick calls.
    /// </summary>
    public class MoneyTutorialTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Awake builds HUD + loads sampled deck
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        // Advance the life until the money tutorial appears, cranking (raw MoneyTick) every step so
        // the overlay always arrives «right after a crank» — the exact swallow window.
        private static void PlayUntilTutorial(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (!driver.TutorialShowing && driver.Game.State == GameState.Playing && guard++ < 4000)
            {
                fake.Fire(GameInput.MoneyTick);           // crank attempt (no-op before 18; capped after)
                driver.Game.Tick(0.25f);
                if (!driver.TutorialShowing && driver.Game.CurrentCard != null
                    && driver.Game.CardTimer < 3.5f)
                    fake.No();                            // answer so ages keep advancing (cards live ~1.5s)
            }
            Assert.IsTrue(driver.TutorialShowing, "money tutorial appeared during the run");
        }

        [UnityTest]
        public IEnumerator Tutorial_Freezes_All_And_SpaceDismisses_RightAfterCranking_TimerResumes()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                             // Start wires input + MoneyOpened

            fake.Confirm();                                // opener → playing
            PlayUntilTutorial(driver, fake);

            Assert.IsTrue(driver.Game.MoneyOpen, "money opened at 18");
            Assert.IsTrue(driver.TutorialOverlay.activeSelf, "overlay visible");
            Assert.IsTrue(driver.Game.Paused, "game paused under the overlay");

            // Full freeze: age, money AND the current card's timer hold through a big tick.
            var card = driver.Game.CurrentCard;
            Assert.IsNotNull(card, "a card is up when the tutorial fires (overlay between answers)");
            float age0 = driver.Game.Age;
            float timer0 = driver.Game.CardTimer;
            double money0 = driver.Game.Money;
            driver.Game.Tick(5f);
            Assert.AreEqual(age0, driver.Game.Age, "age frozen");
            Assert.AreEqual(timer0, driver.Game.CardTimer, "card timer frozen (no timeout under pause)");
            Assert.AreEqual(money0, driver.Game.Money, "money frozen (cost + drains)");

            // Held-Space autorepeat is INERT on the tutorial: holding through money-open must not
            // insta-dismiss the hint — only a fresh keydown (or Enter) may.
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.IsTrue(driver.TutorialShowing, "autorepeat does NOT dismiss the tutorial");
            Assert.IsTrue(driver.Game.Paused, "still paused after an inert repeat");

            // The swallow scenario: the last crank was ≤0.25s ago (cap window!) — a FRESH Space press
            // must STILL dismiss instantly, because the income cap only guards the gameplay-crank branch.
            fake.Fire(GameInput.MoneyTick);
            Assert.IsFalse(driver.TutorialShowing, "Space dismissed the tutorial instantly after a crank");
            Assert.IsFalse(driver.TutorialOverlay.activeSelf, "overlay hidden");
            Assert.IsFalse(driver.Game.Paused, "game unpaused");

            // Mid-timer resume: the SAME card continues from its frozen timer — not skipped, not reset.
            Assert.AreSame(card, driver.Game.CurrentCard, "same card still up after dismiss");
            driver.Game.Tick(0.25f);
            Assert.AreEqual(timer0 - 0.25f, driver.Game.CardTimer, 1e-3f,
                "card timer resumed from the frozen value (not zeroed, not restarted)");
            Assert.Greater(driver.Game.Age, age0, "age resumed");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Space_Confirms_Instantly_InFinaleAndOpener_And_TutorialReArms_OnRestart()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            fake.Fire(GameInput.MoneyTick);                // Space on the OPENER = CONFIRM → playing
            Assert.AreEqual(GameState.Playing, driver.Game.State, "Space started the life");

            // Life 1: all-НЕТ without ticking (age stays put → no tutorial) straight to the finale.
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 300)
                fake.No();
            Assert.AreEqual(GameState.Finale, driver.Game.State, "reached the finale");

            // Held-Space autorepeat is INERT outside gameplay: a player who cranked into the finale
            // with Space held must NOT auto-confirm through the screens into a new life.
            fake.Fire(GameInput.MoneyTickRepeat);
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.AreEqual(GameState.Finale, driver.Game.State,
                "autorepeat events do not confirm the finale (no auto-restart while holding Space)");

            // Two FRESH Space presses back-to-back — zero cap-clock advance between them. Both must
            // land: finale → opener → new life. (The old source-side throttle swallowed the second.)
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "fresh Space confirmed the finale instantly");
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "autorepeat is inert on the opener too");
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Playing, driver.Game.State,
                "an immediate second fresh Space also landed (no throttle outside gameplay)");

            // Life 2: the tutorial re-arms — it must show again at 18 and CONFIRM dismisses it.
            PlayUntilTutorial(driver, fake);
            Assert.IsTrue(driver.Game.Paused, "re-armed tutorial pauses again");
            fake.Confirm();                                // Enter (CONFIRM) dismisses too
            Assert.IsFalse(driver.TutorialShowing, "CONFIRM dismissed the re-armed tutorial");
            Assert.IsFalse(driver.Game.Paused, "unpaused after dismissal");

            Object.Destroy(go);
            yield return null;
        }
    }
}
