using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The S5 money tutorial and the Space re-map through the REAL driver: overlay on first open,
    /// full freeze (age + drains + card timer) while up, Enter-ONLY dismissal (founder Gate-2:
    /// Space presses AND repeats are inert on the hint — mashing the crank must never skip it),
    /// instant Space-confirm in Opener/Finale, mid-timer card resume, tutorial re-arm on restart,
    /// and the money HUD + crank going live IMMEDIATELY on dismissal (same card, no one-card lag).
    /// Time is driven by explicit Game.Tick calls.
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
                // A TIMELINE milestone (I03, YA01…) plays a blocking banner beat that swallows input — pump
                // its ~1.5s clock synchronously so the run passes through it (banner beat → card → tutorial).
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                fake.Fire(GameInput.MoneyTick);           // crank attempt (no-op before 18; capped after)
                driver.Game.Tick(0.25f);
                if (!driver.TutorialShowing && driver.Game.CurrentCard != null
                    && driver.Game.CardTimer < 3.5f)
                    fake.No();                            // answer so ages keep advancing (cards live ~1.5s)
            }
            Assert.IsTrue(driver.TutorialShowing, "money tutorial appeared during the run");
        }

        [UnityTest]
        public IEnumerator Tutorial_Freezes_All_And_SpaceDoesNotDismiss_OnlyEnterDoes_TimerResumes()
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

            // FOUNDER DECISION (Gate-2), REVERSED from the original design: Space must NOT dismiss the
            // hint — she holds/mashes Space for the crank and was skipping every hint unread. Both the
            // held-Space autorepeat AND a fresh Space press are inert here; ONLY Enter dismisses.
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.IsTrue(driver.TutorialShowing, "autorepeat does NOT dismiss the tutorial");
            Assert.IsTrue(driver.Game.Paused, "still paused after an inert repeat");

            fake.Fire(GameInput.MoneyTick);
            Assert.IsTrue(driver.TutorialShowing, "a FRESH Space press does NOT dismiss either (founder)");
            Assert.IsTrue(driver.Game.Paused, "still paused after the inert fresh press");

            fake.Confirm();                                // Enter — the ONLY dismiss key
            Assert.IsFalse(driver.TutorialShowing, "Enter dismissed the tutorial");
            Assert.IsFalse(driver.TutorialOverlay.activeSelf, "overlay hidden");
            Assert.IsFalse(driver.Game.Paused, "game unpaused");

            // Mid-timer resume: the SAME card continues from its frozen timer — not skipped, not reset.
            Assert.AreSame(card, driver.Game.CurrentCard, "same card still up after dismiss");
            driver.Game.Tick(0.25f);
            Assert.AreEqual(timer0 - 0.25f, driver.Game.CardTimer, 1e-3f,
                "card timer resumed from the frozen value (not zeroed, not restarted)");
            // Age is EVENT-based (holds at the current card's age — no creep past it, 2026-07-23 fix), so at
            // money-open (Age == the card's age 18) it does NOT climb further here. The freeze-LIFT is already
            // proven above by the unpaused state + the resumed card timer. (The old Assert.Greater(Age, age0)
            // encoded the age-creep bug — age racing past the card ages — which broke real-play pacing.)
            Assert.AreEqual(age0, driver.Game.Age, 1e-3f, "age holds at the current card's age (event-age, no creep)");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoneyHud_And_Crank_AreLive_ImmediatelyAfterDismiss_SameCard()
        {
            // Founder Gate-2 bug: after «появилась работа» closed, the pill + crank only went live one
            // card later (the age-gated HUD reveal ran on card-advance only, but the 18-crossing
            // happens mid-card). Now dismissal refreshes the gates: same card, instantly live.
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                                // opener → playing
            PlayUntilTutorial(driver, fake);

            var card = driver.Game.CurrentCard;
            Assert.IsNotNull(card, "a card is up under the tutorial");

            fake.Confirm();                                // Enter dismisses
            Assert.IsFalse(driver.TutorialShowing, "hint closed");
            Assert.IsTrue(driver.MoneyPill.activeSelf,
                "money pill visible the MOMENT the hint closes — not one card later");

            yield return new WaitForSeconds(0.25f);        // let the ~5/s income-cap clock breathe
            Assert.AreSame(card, driver.Game.CurrentCard, "still the SAME card (timer was frozen ≥3s)");

            double before = driver.Game.Money;
            fake.Fire(GameInput.MoneyTick);
            Assert.Greater(driver.Game.Money, before,
                "Space pays income immediately on this same card — the crank is live");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Tutorial_Dismiss_SameFrameChord_DoesNotLeakCrank()
        {
            // Skeptic HIGH: in one input poll the source yields Confirm BEFORE MoneyTick, so an Enter+Space
            // chord could dismiss the hint then leak the later same-frame crank into gameplay. The
            // same-frame swallow guard must eat everything but the dismiss on the frame the hint closes.
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                                // opener → playing
            PlayUntilTutorial(driver, fake);

            // Let the ~5/s income cap breathe WHILE PAUSED so a leaked crank WOULD land (proves the guard,
            // not the cap, is what swallows it). Update advances the cap clock even under the pause.
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(driver.TutorialShowing, "still on the hint (WaitForSeconds didn't dismiss)");
            double money0 = driver.Game.Money;

            // The chord, in source order: Confirm FIRST (dismisses), then MoneyTick + repeat SAME frame.
            fake.Confirm();
            Assert.IsFalse(driver.TutorialShowing, "Enter dismissed the hint");
            fake.Fire(GameInput.MoneyTick);
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.AreEqual(money0, driver.Game.Money,
                "no crank leaked onto the dismiss frame (fresh Space AND repeat both swallowed)");

            // Next frame everything is normal again: Space cranks (cap re-armed by the wait).
            yield return new WaitForSeconds(0.3f);
            double money1 = driver.Game.Money;
            fake.Fire(GameInput.MoneyTick);
            Assert.Greater(driver.Game.Money, money1, "Space cranks normally on the following frame");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Space_NeverConfirms_EnterIs_TheSoleConfirmKey_AndTutorialReArms()
        {
            // REVERSED (founder Gate-2 round 2): Space is the crank ONLY — it must never start, restart,
            // or advance a screen. She holds Space through the necrolog; it must not skip the payoff.
            var driver = Boot(out var go, out var fake);
            yield return null;

            // Opener: neither a fresh Space nor a held repeat may start the life — only Enter.
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "fresh Space does NOT start from the opener");
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "held Space does NOT start either");
            fake.Confirm();                                // Enter is the sole start key
            Assert.AreEqual(GameState.Playing, driver.Game.State, "Enter started the life");

            // Life 1: all-НЕТ without ticking (age stays put → no tutorial) straight to the finale. Milestone
            // banners (I03…) are blocking beats now — pump the ~1.5s clock so the all-НЕТ sweep passes through.
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 300)
            {
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                fake.No();
            }
            Assert.AreEqual(GameState.Finale, driver.Game.State, "reached the finale");

            // The core founder bug: holding/mashing Space through the necrolog must NOT restart —
            // the payoff screen stays up until she presses Enter.
            fake.Fire(GameInput.MoneyTickRepeat);
            fake.Fire(GameInput.MoneyTick);
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Finale, driver.Game.State,
                "no amount of Space (fresh or held) leaves the finale — the necrolog is not skipped");

            fake.Confirm();                                // Enter — the sole restart key
            Assert.AreEqual(GameState.Opener, driver.Game.State, "Enter restarted from the finale → opener");
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "Space still inert on the opener");
            fake.Confirm();                                // Enter → new life
            Assert.AreEqual(GameState.Playing, driver.Game.State, "Enter started life 2");

            // Life 2: the tutorial re-arms — it must show again at 18 and Enter (CONFIRM) dismisses it.
            PlayUntilTutorial(driver, fake);
            Assert.IsTrue(driver.Game.Paused, "re-armed tutorial pauses again");
            fake.Confirm();                                // Enter (CONFIRM) dismisses
            Assert.IsFalse(driver.TutorialShowing, "CONFIRM dismissed the re-armed tutorial");
            Assert.IsFalse(driver.Game.Paused, "unpaused after dismissal");

            Object.Destroy(go);
            yield return null;
        }
    }
}
