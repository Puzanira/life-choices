using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The child tamagotchi button through the REAL driver: the Enter double-duty routing (CONFIRM starts
    /// the game / dismisses the child hint / restarts from the finale, but becomes CHILD_PRESS ONLY while
    /// Playing with the child open and no tutorial up), the S5 «ПОПОЛНЕНИЕ!» hint on MD02=ДА, and the
    /// button HUD reveal + lit state. Time is driven by explicit Game.Tick calls.
    /// </summary>
    public class ChildButtonTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Awake builds HUD + loads sampled deck
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        // Deck that opens the child on MD02=ДА, with a short deterministic flash interval and neutral filler.
        private static Game ChildDeck()
        {
            var deck = new List<Card> { Starter(), Plain("MD02", 21) };
            for (int a = 22; a <= 120; a++) deck.Add(Plain("F" + a, a));
            return new Game(deck, coin: () => false) { ChildFlashInterval = () => 2f };
        }

        [UnityTest]
        public IEnumerator Enter_RoutesToChildPress_OnlyInGameplayWithOpenChild_ElseConfirm()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                           // Awake + Start
            driver.DebugReplaceGame(ChildDeck());

            // (1) Opener: CONFIRM starts the game (never a child press — child isn't open).
            fake.Confirm();
            Assert.AreEqual(GameState.Playing, driver.Game.State, "Enter/CONFIRM in the opener starts the game");
            Assert.IsFalse(driver.Game.ChildOpen);

            fake.No();                                   // resolve I03 → MD02 becomes current
            Assert.AreEqual("MD02", driver.Game.CurrentCard.Id);

            // (2) MD02=ДА opens the child → the S5 «ПОПОЛНЕНИЕ!» hint shows and pauses.
            fake.Yes();
            Assert.IsTrue(driver.Game.ChildOpen, "MD02=ДА opened the child");
            Assert.IsTrue(driver.TutorialShowing, "the child S5 hint is up");
            StringAssert.Contains("ПОПОЛНЕНИЕ", driver.TutorialText.text, "it is the child hint");

            // (3) Under the tutorial, Enter/CONFIRM DISMISSES the hint — it is NOT a child press.
            fake.Confirm();
            Assert.IsFalse(driver.TutorialShowing, "Enter dismissed the child hint (still CONFIRM under a hint)");
            Assert.IsFalse(driver.Game.Paused, "game resumed");

            // The button is assembled + revealed while the child is open; capture its DIM/idle rendered tint
            // (driven through ReflectChildButton on a real Update, not the state flag).
            yield return null;                           // let Update run ReflectChildButton (idle state)
            Assert.IsNotNull(driver.ChildButtonImage, "child button assembled");
            Assert.IsTrue(driver.ChildGroup.activeSelf, "child button revealed while the child is open");
            Assert.IsFalse(driver.Game.ChildFlashing, "not flashing yet");
            Color idleTint = driver.ChildButtonImage.color;
            Assert.Less(idleTint.r, 0.3f, "idle button renders a dim/cool (unlit) tint");
            Assert.Greater(idleTint.b, idleTint.r, "idle tint is cool (blue-ish), not warm");

            // (4) Drive to a flash window, dismissing the age-gate hints (money/rel/energy…) that pop as the
            // child ages up — each also on Enter, proving CONFIRM still dismisses hints during gameplay.
            int guard = 0;
            while (!driver.Game.ChildFlashing && driver.Game.State == GameState.Playing && guard++ < 600)
            {
                if (driver.TutorialShowing) fake.Confirm();   // dismiss an age-gate hint (Enter = CONFIRM)
                else driver.Game.Tick(0.1f);
            }
            Assert.IsTrue(driver.Game.ChildFlashing, "the flash window is open (game flag)");

            // While flashing, the REAL Image LIGHTS UP: ReflectChildButton renders a bright-gold LIT tint,
            // distinct from the idle tint — proving the flash is actually VISIBLE, not just a state flag.
            yield return null;                           // let Update run ReflectChildButton (lit state)
            Assert.IsTrue(driver.Game.ChildFlashing, "still within the ~2s window after one frame");
            Color litTint = driver.ChildButtonImage.color;
            Assert.AreNotEqual(idleTint, litTint, "the lit tint is visibly different from the idle tint");
            Assert.Greater(litTint.r, 0.8f, "lit button renders a bright tint");
            Assert.Greater(litTint.r, litTint.b, "lit tint is warm gold (not the cool idle blue)");

            // (5) Enter/CONFIRM now = CHILD_PRESS (success) — NOT a state change / confirm.
            int child0 = driver.Game.Scales.Child;
            fake.Confirm();                              // routed to CHILD_PRESS
            Assert.AreEqual(GameState.Playing, driver.Game.State, "the press did not confirm/restart anything");
            Assert.IsFalse(driver.Game.ChildFlashing, "a successful press closed the window");
            Assert.AreEqual(child0 + Game.ChildPressGain, driver.Game.Scales.Child,
                "Enter in gameplay-with-open-child was a CHILD_PRESS (good parenting)");

            // …and the button returns to its DIM/idle tint once the window is over (unlit again).
            yield return null;                           // Update reflects the now-unlit button
            Assert.Less(driver.ChildButtonImage.color.r, 0.3f, "button renders dim/unlit again after the press");
            Assert.AreEqual(idleTint, driver.ChildButtonImage.color, "same idle tint as before the flash");

            // (6) Finale: CONFIRM restarts — the Playing guard means it is NOT a child press.
            guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 8000)
            {
                if (driver.TutorialShowing) { fake.Confirm(); continue; }
                driver.Game.Tick(0.5f);
                if (driver.Game.EnergyOpen)
                    for (int i = 0; i < 3 && driver.Game.State == GameState.Playing; i++)
                        fake.Fire(GameInput.EnergyPulse);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3f)
                    fake.No();
            }
            Assert.AreEqual(GameState.Finale, driver.Game.State, "run reached the finale");
            fake.Confirm();
            Assert.AreEqual(GameState.Opener, driver.Game.State,
                "Enter/CONFIRM at the finale restarts (never a child press outside Playing)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
