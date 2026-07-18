using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The relationships balancer through the REAL driver: the S5 «первая любовь» hint pauses and the
    /// balancer goes live at 20, and a breakup hides the balancer + flashes the «РАССТАЛИСЬ» plate.
    /// Time is driven by explicit Game.Tick calls (no wall-clock).
    /// </summary>
    public class RelationshipsHudTests
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

        private static Card SetRelNo(string id, int age, int val)
        {
            var c = Plain(id, age);
            c.NoDeltas = new[] { new ScaleDelta(Scale.Relationships, DeltaKind.Set, val) };
            return c;
        }

        [UnityTest]
        public IEnumerator RelationshipsHint_ShowsAndPauses_At20_ThenBalancerGoesLive()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                             // Start wires input + events
            fake.Confirm();                                // opener → playing

            bool sawRelHint = false;
            int guard = 0;
            while (guard++ < 12000 && driver.Game.State == GameState.Playing && !sawRelHint)
            {
                if (driver.TutorialShowing)
                {
                    if (driver.TutorialText.text.Contains("ОТНОШЕНИЙ"))
                    {
                        Assert.IsTrue(driver.Game.Paused, "the relationships hint pauses the game");
                        Assert.IsTrue(driver.Game.RelationshipsOpen, "balancer opened");
                        Assert.GreaterOrEqual(driver.Game.Age, 19f, "relationships hint fires around age 20");
                        sawRelHint = true;
                        break;
                    }
                    fake.Confirm();                        // dismiss the earlier money hint and press on
                    yield return null;
                    continue;
                }
                fake.Fire(GameInput.MoneyTick);
                driver.Game.Tick(0.25f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3.5f)
                    fake.No();
            }
            Assert.IsTrue(sawRelHint, "the «первая любовь» balancer hint appeared at 20");

            fake.Confirm();                                // Enter dismisses
            Assert.IsFalse(driver.TutorialShowing, "hint closed on Enter");
            Assert.IsTrue(driver.BalancerGroup.activeSelf,
                "the balancer is visible the moment the hint closes — no one-card lag");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Breakup_HidesBalancer_AndFlashesPlate_NoDeath()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // A deterministic deck: a card that drops relationships below the zone at 20, then neutral
            // filler so the run keeps going while the below-zone timer accrues to a breakup.
            var deck = new List<Card> { Starter(), SetRelNo("HIT", 21, 35) };
            for (int a = 22; a <= 84; a += 3) deck.Add(Plain("F" + a, a));
            var g = new Game(deck, coin: () => false);
            driver.DebugReplaceGame(g);
            fake.Confirm();                                // opener → playing on the injected game

            int guard = 0;
            while (guard++ < 8000 && g.State == GameState.Playing && !g.RelationshipsLost)
            {
                if (driver.TutorialShowing) { fake.Confirm(); yield return null; continue; }
                driver.Game.Tick(0.25f);
                if (g.CurrentCard != null && g.CardTimer < 3.5f) fake.No();
            }

            Assert.IsTrue(g.RelationshipsLost, "the balancer broke up after ~10s below the zone");
            Assert.AreEqual(GameState.Playing, g.State, "a breakup NEVER ends the run (no death)");

            yield return null;                             // let the driver's Update reflect the breakup
            Assert.IsFalse(driver.BalancerGroup.activeSelf, "balancer hidden after the breakup");
            Assert.IsTrue(driver.BreakupPlate.activeSelf, "the «РАССТАЛИСЬ» plate flashed on the breakup");

            Object.Destroy(go);
            yield return null;
        }
    }
}
