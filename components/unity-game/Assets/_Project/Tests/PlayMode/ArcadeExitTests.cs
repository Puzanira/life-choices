using System.Collections;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Arcade contract §5 lifecycle, driven END-TO-END through the REAL arcade path: the package's
    /// <see cref="FakeBackend"/> feeds ArcadeInput, the driver's own <see cref="ArcadeInputSource"/>
    /// (added by GameDriver.Start — no semantic fake injected) translates buttons to GameInput. So these
    /// tests exercise the actual cabinet flow: GREEN is the ONE confirm — it starts a life, dismisses hints
    /// AND restarts from the finale (founder 99fab3c); RED only answers cards and is inert on the finale;
    /// MENU exits a live run cleanly to a fresh opener.
    /// </summary>
    public class ArcadeExitTests
    {
        private GameObject _rig;
        private GameObject _driverGo;
        private FakeBackend _fake;
        private GameDriver _driver;

        private static BackendSnapshot Green => new BackendSnapshot { GreenHeld = true };
        private static BackendSnapshot Red => new BackendSnapshot { RedHeld = true };
        private static BackendSnapshot Menu => new BackendSnapshot { MenuHeld = true };

        private IEnumerator BootArcadeGame()
        {
            _fake = new FakeBackend();
            _rig = new GameObject("Rig");
            _rig.SetActive(false);
            _rig.AddComponent<ArcadeInputRunner>().BackendOverride = _fake;   // injected before Awake
            _rig.SetActive(true);

            _driverGo = new GameObject("Driver");
            _driver = _driverGo.AddComponent<GameDriver>();
            // NO injected Input: GameDriver.Start adds its own ArcadeInputSource, which finds the rig's
            // runner — exactly what happens in the shipped scene / on the cabinet.
            yield return null;   // Start runs
            yield return null;   // one settled zero-snapshot frame
        }

        private IEnumerator Cleanup()
        {
            Object.Destroy(_driverGo);
            Object.Destroy(_rig);
            yield return null;
        }

        /// <summary>A physical button press: held for two frames, released for two.</summary>
        private IEnumerator Press(BackendSnapshot snap)
        {
            _fake.Next = snap;
            yield return null;
            yield return null;
            _fake.Next = default;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator MenuButton_Aborts_A_Live_Run_To_A_Fresh_Opener_And_Green_Reenters()
        {
            yield return BootArcadeGame();
            Assert.AreEqual(GameState.Opener, _driver.Game.State, "boots into the opener");

            yield return Press(Green);   // GREEN on the opener = start the life (arcade confirm)
            Assert.AreEqual(GameState.Playing, _driver.Game.State, "GREEN started a life from the opener");
            Assert.IsNotNull(_driver.Game.CurrentCard, "a live card is up");

            // Let the run genuinely breathe (real driver Update ticks the game between frames).
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(GameState.Playing, _driver.Game.State, "still mid-life just before exit");

            yield return Press(Menu);    // MENU = clean exit (must bypass any banner/hint swallow)
            Assert.AreEqual(GameState.Opener, _driver.Game.State,
                "MenuButton ended the run and returned to the opener");
            Assert.AreEqual(0f, _driver.Game.Age, 0.0001f, "a fresh life — age reset to 0");
            Assert.AreEqual(100, _driver.Game.Scales.Health, "scales reset on a clean exit");
            Assert.IsFalse(_driver.TutorialShowing, "no hint left hanging after exit");
            Assert.IsFalse(_driver.NewScaleShowing, "…and no §D modal left hanging either");
            Assert.IsFalse(_driver.Game.Paused, "game unpaused after exit");

            // Re-entry through the REAL arcade path: GREEN starts the next life (no Confirm control exists).
            yield return Press(Green);
            Assert.AreEqual(GameState.Playing, _driver.Game.State,
                "GREEN starts a new life again after a MenuButton exit — the machine is fully re-armed");
            Assert.IsNotNull(_driver.Game.CurrentCard, "the new life drew a card");

            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Red_Declines_A_Whole_Life_To_The_Finale_Then_Green_Restarts()
        {
            yield return BootArcadeGame();
            yield return Press(Green);   // opener → playing
            Assert.AreEqual(GameState.Playing, _driver.Game.State);

            // Live the whole life on the cabinet buttons: RED declines every card; GREEN dismisses hints
            // (arcade confirm while a tutorial is up), exactly as DriverSmokeTests does.
            int guard = 0;
            while (_driver.Game.State == GameState.Playing && guard++ < 600)
            {
                // §D: открытие шкалы поднимает модалку, которую зелёная НЕ снимает — её проходят
                // настоящим контролом кабинета (крутилка / джойстик / «!» / рычаг дыхания).
                if (_driver.NewScaleShowing) { yield return NewScaleTut.ClearArcade(_driver, _fake); continue; }
                if (_driver.TutorialShowing) { yield return Press(Green); continue; }
                yield return Press(Red);
            }

            Assert.AreEqual(GameState.Finale, _driver.Game.State, "an all-RED (СПАСИБО НЕ НАДО) run reached an ending");
            Assert.IsNotNull(_driver.Game.Necrolog, "the necrolog rendered");

            // Founder mapping 2026-07-29 (99fab3c), OVERRIDING the earlier «рестарт = красная» row:
            // GREEN is the one confirm everywhere, and RED on the finale is INERT.
            yield return Press(Red);
            Assert.AreEqual(GameState.Finale, _driver.Game.State,
                "RED on the finale does nothing — the payoff screen cannot be mashed away");

            yield return Press(Green);
            Assert.AreEqual(GameState.Opener, _driver.Game.State, "GREEN restarted from the finale to the opener");
            Assert.AreEqual(100, _driver.Game.Scales.Health, "state fully reset for the next player");

            yield return Press(Green);
            Assert.AreEqual(GameState.Playing, _driver.Game.State, "and GREEN starts the next life");

            yield return Cleanup();
        }
    }
}
