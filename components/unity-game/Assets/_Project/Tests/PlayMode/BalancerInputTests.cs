using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// DRIVER-LEVEL balancer input (founder playtest 2026-07-23: «держу ↑/↓ — полоса вообще не двигается»).
    /// Every existing relationships test calls Game.HandleInput directly — NONE exercise the real chain
    /// source.Received → GameDriver.OnInput → Game. This drives to relationships-open through the driver, then
    /// HOLDS ↑ (and ↓) via the fake source's Received event (the exact path a held arrow key takes) and asserts
    /// the marker actually moves. If the driver drops the axis, this is RED.
    /// </summary>
    public class BalancerInputTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };
        private static Card Starter() { var c = Plain("I03", 1); c.StartsAgeTimer = true; return c; }

        private static IEnumerator DriveToRelationshipsOpen(GameDriver driver, PlayFakeInputSource fake)
        {
            // Controlled deck: starter + a card at 22 so age climbs past 18/20 (money+rel open) then HOLDS at 22
            // (event-age) — no energy(25) tutorial interrupts the axis test.
            driver.DebugReplaceGame(new Game(new List<Card> { Starter(), Plain("A", 22) }, coin: () => false));
            fake.Confirm();                              // opener → playing (starter up)
            fake.No();                                   // resolve starter → «A»@22 drawn, age catches up
            int guard = 0;
            while (!driver.Game.RelationshipsOpen && driver.Game.State == GameState.Playing && guard++ < 300)
            {
                // §D: деньги/отношения теперь поднимают модальный экран новой шкалы — он снимается своим
                // контролом, а не кнопкой (NewScaleTut); прочие S5-подсказки — как раньше.
                if (driver.NewScaleShowing || driver.TutorialShowing) NewScaleTut.ClearAny(driver, fake);
                else driver.Game.Tick(0.2f);
            }
            NewScaleTut.ClearAny(driver, fake);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoldingUp_ThroughDriverInputPath_RaisesRelationships()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return DriveToRelationshipsOpen(driver, fake);

            Assert.IsTrue(driver.Game.RelationshipsOpen, "relationships opened");
            Assert.IsFalse(driver.Game.Paused, "not paused (hint dismissed)");
            int rel0 = driver.Game.Scales.Relationships;

            // HOLD → (вправо) for ~4s through the driver's REAL input path: source raises RelationRight EVERY frame the
            // axis is held, exactly as ArcadeInputSource re-emits for a held Joystick.x (RelationRight/Left).
            for (int i = 0; i < 40; i++) { fake.Fire(GameInput.RelationRight); driver.Game.Tick(0.1f); }

            Assert.Greater(driver.Game.Scales.Relationships, rel0,
                "holding ↑ through the driver's input path RAISES relationships (net +axis beats the drift)");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HoldingDown_ThroughDriverInputPath_LowersRelationships()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;
            yield return DriveToRelationshipsOpen(driver, fake);

            Assert.IsTrue(driver.Game.RelationshipsOpen, "relationships opened");
            int rel0 = driver.Game.Scales.Relationships;

            for (int i = 0; i < 20; i++) { fake.Fire(GameInput.RelationLeft); driver.Game.Tick(0.1f); }

            Assert.Less(driver.Game.Scales.Relationships, rel0,
                "holding ↓ through the driver's input path LOWERS relationships (axis + drift both down)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
