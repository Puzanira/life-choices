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
                    continue;
                }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }

            Assert.IsTrue(sawEnergyHint, "the energy tutorial appeared when energy opened at 25");

            Object.Destroy(go);
            yield return null;
        }
    }
}
