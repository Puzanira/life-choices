using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Depression «тёмная полоса» through the REAL driver (S8 observability): entering the crisis tail
    /// shows the full-screen B&W wash, the dim centre pulse reveals only on the hit-window, catches step
    /// the desaturation down, and lifting depression clears the overlay. Time is driven by explicit
    /// Game.Tick calls (no wall-clock).
    /// </summary>
    public class DepressionHudTests
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

        private static Game DepressionGame(string csv)
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
                BlitzNormalOnLeftRoll = () => true,
                DepressionTriggerRoll = () => true,
                DepressionPulseInterval = () => 2.5f,
            };
        }

        [UnityTest]
        public IEnumerator Depression_ShowsBwOverlay_PulseToggles_AndClearsOnExit()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                              // Start wires input + events

            var g = DepressionGame(Csv());
            driver.DebugReplaceGame(g);
            fake.Confirm();                                 // opener → playing
            g.HandleInput(GameInput.AnswerNo);              // resolve I03 → age timer running

            // Age up to the crisis, dismissing any S5 hints along the way.
            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "reached the crisis blitz");

            // The «КРИЗИС… БЛИЦ!» rubric is a blocking beat — clear it before pressing the blitz buttons.
            driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f);
            for (int i = 0; i < 5; i++) fake.Yes();         // clear the blitz cleanly → crisis tail rolls
            Assert.IsTrue(g.InDepression, "the crisis tail entered depression");

            // Depression opens with its own muted «ТЁМНАЯ ПОЛОСА…» beat — clear it before driving pulses.
            driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f);
            yield return null;                              // let the driver's Update reflect it
            Assert.IsTrue(driver.DepressionOverlay.activeSelf, "the B&W overlay is shown while depressed");
            float fullGrayAlpha = driver.DepressionVeil.color.a;
            Assert.Greater(fullGrayAlpha, 0.5f, "the desaturation wash is near-opaque at full gray");

            // Open a pulse and verify the dim indicator reveals on the window.
            g.Tick(2.5f);
            yield return null;
            Assert.IsTrue(g.DepressionPulsing, "the pulse is lit");
            Assert.IsTrue(driver.DepressionPulseIndicator.gameObject.activeSelf, "the dim centre pulse is visible");
            fake.Confirm();                                 // catch → one step of colour returns
            Assert.AreEqual(Game.DepressionGraySteps - 1, g.DepressionGray, "a catch stepped the gray down");

            yield return null;
            Assert.Less(driver.DepressionVeil.color.a, fullGrayAlpha, "the wash lightened as colour returned");

            // Catch the remaining pulses → depression lifts → overlay clears.
            for (int i = 0; i < Game.DepressionGraySteps - 1; i++)
            {
                g.Tick(2.5f);
                fake.Confirm();
            }
            Assert.IsFalse(g.InDepression, "5 catches lifted depression");

            yield return null;
            Assert.IsFalse(driver.DepressionOverlay.activeSelf, "the overlay clears when depression lifts");

            Object.Destroy(go);
            yield return null;
        }
    }
}
