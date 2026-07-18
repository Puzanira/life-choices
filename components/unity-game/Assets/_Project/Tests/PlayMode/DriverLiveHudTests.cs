using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Driver-level wiring of the live layer: the burnout state plate and the show-brightness veil are
    /// assembled (veil transparent at full state), and the S5 energy hint fires + pauses when energy
    /// opens at 25. Time is driven by explicit Game.Tick calls (no wall-clock).
    /// </summary>
    public class DriverLiveHudTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        [UnityTest]
        public IEnumerator Assembles_BurnoutPlate_And_BrightnessVeil()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                              // → playing
            yield return null;
            yield return null;

            Assert.IsNotNull(driver.BurnoutPlate, "burnout plate assembled");
            Assert.IsFalse(driver.BurnoutPlate.activeSelf, "burnout plate hidden at full energy");
            Assert.IsNotNull(driver.BrightnessVeil, "brightness veil assembled");
            Assert.Less(driver.BrightnessVeil.color.a, 0.05f, "veil transparent while state is full");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnergyHint_Shows_AndPauses_When_EnergyOpensAt25()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                              // → playing

            bool sawEnergyHint = false;
            int guard = 0;
            while (guard++ < 12000 && driver.Game.State == GameState.Playing && !sawEnergyHint)
            {
                if (driver.TutorialShowing)
                {
                    if (driver.TutorialText.text.Contains("УСТАЛОСТЬ"))
                    {
                        Assert.IsTrue(driver.Game.Paused, "the energy hint pauses the game (age/drains frozen)");
                        Assert.GreaterOrEqual(driver.Game.Age, 24f, "energy hint fires around age 25");
                        Assert.IsTrue(driver.Game.EnergyOpen, "energy scale opened");
                        sawEnergyHint = true;
                        break;
                    }
                    fake.Confirm();                      // dismiss the earlier money hint and press on
                    yield return null;                   // let Update clear the same-frame dismiss guard
                    continue;
                }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }

            Assert.IsTrue(sawEnergyHint, "the energy tutorial appeared when energy opened at 25");

            // Same-card liveness (founder Gate-2): the energy bar had the same one-card reveal lag as
            // the money pill — dismissing the hint must reveal the bar IMMEDIATELY, not one card later.
            fake.Confirm();                              // Enter dismisses (the only dismiss key)
            Assert.IsFalse(driver.TutorialShowing, "energy hint closed on Enter");
            Assert.IsTrue(driver.EnergyGroup.activeSelf,
                "energy bar visible the moment the hint closes — no one-card lag");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HealthHint_Dismiss_SameFrameChord_DoesNotLeak_Crank_Or_EnergyPulse()
        {
            // The EnergyPulse variant of the same-frame chord (skeptic HIGH), at the HEALTH hint (age 30)
            // where energy is open and money is bankable — so a leaked crank/pulse would be observable.
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                              // → playing

            // Drive to the HEALTH hint (age 30), dismissing the money(18) + energy(25) hints on the way.
            bool atHealth = false;
            int guard = 0;
            while (guard++ < 20000 && driver.Game.State == GameState.Playing && !atHealth)
            {
                if (driver.TutorialShowing)
                {
                    if (driver.TutorialText.text.Contains("ТАЯТЬ")) { atHealth = true; break; }
                    fake.Confirm();                      // dismiss money/energy hint
                    yield return null;                   // let Update clear the same-frame dismiss guard
                    continue;
                }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }
            Assert.IsTrue(atHealth, "reached the health hint at 30");
            Assert.IsTrue(driver.Game.Paused, "health hint paused the game");
            Assert.IsTrue(driver.Game.EnergyOpen, "energy is open (drained below full) by 30");

            // Cap breathes while paused so a leaked crank WOULD land — proving the guard, not the cap.
            yield return new WaitForSeconds(0.3f);
            double money0 = driver.Game.Money;
            int energy0 = driver.Game.Scales.Energy;

            // The chord in source order: Confirm FIRST, then MoneyTick + EnergyPulse the SAME frame.
            fake.Confirm();
            Assert.IsFalse(driver.TutorialShowing, "Enter dismissed the health hint");
            fake.Fire(GameInput.MoneyTick);
            fake.Fire(GameInput.EnergyPulse);
            Assert.AreEqual(money0, driver.Game.Money,
                "no crank leaked onto the dismiss frame (strong observable — cap was armed)");
            Assert.AreEqual(energy0, driver.Game.Scales.Energy,
                "no energy pulse leaked onto the dismiss frame (same guarded path)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
