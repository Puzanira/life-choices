using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LifeChoices.Tests
{
    /// <summary>
    /// Player-path through REAL (simulated) keyboard input: → resolves «да»,
    /// ← resolves «нет» (contract #2). Follows the proven time-reverse fixture:
    /// input routed to the game view, focus ignored, device added in the test
    /// body (after the fixture reset), Input System's harmless injection noise
    /// ignored only around the press window.
    /// </summary>
    public class LifeChoicesInputTests : InputTestFixture
    {
        private LifeGameController _c;

        public override void Setup()
        {
            base.Setup();
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        public override void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            base.TearDown();
        }

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("LifeChoices");
            yield return null;
            yield return null;
            _c = Object.FindAnyObjectByType<LifeGameController>();
            Assert.IsNotNull(_c);
        }

        [UnityTest]
        public IEnumerator RightArrow_RegistersAsYes()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            _c.StartRun(2024);
            int before = _c.Game.History.Count;

            LogAssert.ignoreFailingMessages = true;
            Press(kb.rightArrowKey);
            yield return null;
            Release(kb.rightArrowKey);
            yield return null;
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(before + 1, _c.Game.History.Count, "→ must resolve a card");
        }

        [UnityTest]
        public IEnumerator LeftArrow_RegistersAsNoAndKeepsPlaying()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            _c.StartRun(2024);
            int before = _c.Game.History.Count;

            LogAssert.ignoreFailingMessages = true;
            Press(kb.leftArrowKey);
            yield return null;
            Release(kb.leftArrowKey);
            yield return null;
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(before + 1, _c.Game.History.Count, "← must resolve a card");
            Assert.IsFalse(_c.IsShowingObituary, "a safe НЕТ on the first card keeps the run alive");
        }
    }
}
