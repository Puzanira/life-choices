using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    public class DriverSmokeTests
    {
        [UnityTest]
        public IEnumerator Driver_Boots_InOpener_ThenInjectedInput_PlaysToEnding()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Awake builds HUD + loads deck
            var fake = new PlayFakeInputSource();
            driver.Input = fake;                           // injected before Start wires it
            yield return null;                             // Start runs

            Assert.IsNotNull(driver.Game, "game constructed");
            Assert.AreEqual(GameState.Opener, driver.Game.State, "boots into opener");
            Assert.Greater(driver.Game.DeckCount, 0, "spine subset loaded from Resources");

            fake.Confirm();                                // start the life
            Assert.AreEqual(GameState.Playing, driver.Game.State);
            Assert.IsNotNull(driver.Game.CurrentCard);

            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 300)
                fake.No();                                 // answer СПАСИБО НЕ НАДО to every card

            Assert.AreEqual(GameState.Finale, driver.Game.State, "run reached an ending");
            Assert.IsNotNull(driver.Game.Necrolog);
            Assert.AreEqual(Necrolog.ParentsLine, driver.Game.Necrolog.StoryLines[0],
                "necrolog opens with the parents line");

            fake.Confirm();                                // finale -> opener, fresh state
            Assert.AreEqual(GameState.Opener, driver.Game.State);
            Assert.AreEqual(100, driver.Game.Scales.Health, "state reset on restart");

            Object.Destroy(go);
            yield return null;
        }
    }
}
