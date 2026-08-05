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
                BlitzNormalOnYesRoll = () => true,
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
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "reached the crisis blitz");

            // Объявление кризиса теперь живёт в облачке и ничего не блокирует — состарим его, чтобы
            // экран читался отдохнувшим (плашка-рубрика и её бит сняты 2026-08-05).
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);
            for (int i = 0; i < 5; i++) fake.Yes();         // clear the blitz cleanly → crisis tail rolls
            Assert.IsTrue(g.InDepression, "the crisis tail entered depression");

            // Depression announces itself with a muted «ТЁМНАЯ ПОЛОСА…» bubble — age it out before pulses.
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);
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

        // Reach depression through the real driver (age → blitz → clean tail → depression), clearing the
        // announce bubbles. Leaves the game AT REST (no pulse lit yet). Mirrors the boot in the test above.
        private IEnumerator ReachDepressionAtRest(GameDriver driver, Game g, PlayFakeInputSource fake)
        {
            driver.DebugReplaceGame(g);
            fake.Confirm();
            g.HandleInput(GameInput.AnswerNo);
            int guard = 0;
            while (guard++ < 12000 && g.Phase == CrisisPhase.None && g.State == GameState.Playing)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); yield return null; continue; }
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                g.Tick(0.2f);
            }
            Assert.AreEqual(CrisisPhase.Blitz, g.Phase, "reached the crisis blitz");
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);
            for (int i = 0; i < 5; i++) fake.Yes();
            Assert.IsTrue(g.InDepression, "the crisis tail entered depression");
            driver.DebugPumpHost(GameDriver.BubbleSeconds + 0.1f);   // age out the «ТЁМНАЯ ПОЛОСА…» bubble
            yield return null;                                       // one Update → ReflectDepression, resting
        }

        // FIX 2 (S8 rework): the depression indicator is a BIG star that is ALWAYS visible during depression and
        // BLINKS on the steady beat — dim-but-visible at rest, bright + scaled-up while the hit-window is open.
        [UnityTest]
        public IEnumerator DepressionIndicator_AlwaysVisibleAndLarge_FlashesBiggerOnBeat()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            var g = DepressionGame(Csv());
            yield return ReachDepressionAtRest(driver, g, fake);

            Assert.IsTrue(g.InDepression, "in depression");
            Assert.IsFalse(g.DepressionPulsing, "resting between beats (no pulse lit yet)");

            var ind = driver.DepressionPulseIndicator;
            // ALWAYS visible — even at rest, the indicator is on (was hidden between the rare old flashes).
            Assert.IsTrue(ind.gameObject.activeInHierarchy, "indicator is visible at rest (always on while depressed)");
            // BIG: drawn size (rect × scale) is well above a floor.
            float restDrawn = ind.rectTransform.rect.width * ind.rectTransform.localScale.x;
            Assert.Greater(restDrawn, 220f, "indicator is BIG at rest (≥220px drawn)");
            // VISIBLE: alpha above a floor (never fully gone).
            float restAlpha = ind.color.a;
            Assert.Greater(restAlpha, 0.18f, "indicator is dim-but-visible at rest (alpha floor)");

            // Open the beat → the indicator FLASHES brighter and scales UP («жми!»).
            g.Tick(2.5f);
            yield return null;
            Assert.IsTrue(g.DepressionPulsing, "the beat is lit");
            float litDrawn = ind.rectTransform.rect.width * ind.rectTransform.localScale.x;
            float litAlpha = ind.color.a;
            Assert.Greater(litAlpha, restAlpha, "the indicator flashes BRIGHTER on the beat");
            Assert.Greater(litDrawn, restDrawn, "the indicator scales UP on the beat");
            Assert.Greater(litAlpha, 0.7f, "the lit flash is high-contrast/bright");
            Assert.Greater(litDrawn, 300f, "the lit flash is genuinely large (≥300px drawn)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
