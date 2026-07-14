using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LifeChoices.Tests
{
    /// <summary>
    /// Scene-level contract: the LifeChoices scene boots, shows the HUD (scales,
    /// stage, timer) and a card; choices resolve and are observable; a full run
    /// reaches the obituary; restart resets. Console must stay clean (contract #6).
    /// Choices are driven through the controller's public API (deterministic);
    /// real keyboard input has its own fixture below.
    /// </summary>
    public class LifeChoicesSceneTests
    {
        private LifeGameController _c;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("LifeChoices");
            yield return null;
            yield return null;
            _c = Object.FindAnyObjectByType<LifeGameController>();
            Assert.IsNotNull(_c, "LifeGameController must be present in the scene");
        }

        [UnityTest]
        public IEnumerator Boots_ShowsScalesStageTimerAndCard()
        {
            yield return null;
            StringAssert.Contains("Настроение 50", _c.ScalesLine);
            StringAssert.Contains("Здоровье 50", _c.ScalesLine);
            StringAssert.Contains("Деньги 50", _c.ScalesLine);
            StringAssert.Contains("Люди 50", _c.ScalesLine);
            Assert.IsFalse(string.IsNullOrEmpty(_c.CardLine), "a card must be shown");
            Assert.Greater(_c.TimeRemaining, 0f, "timer must be counting a window");
            Assert.IsFalse(_c.IsShowingObituary);
        }

        [UnityTest]
        public IEnumerator ChoosingNo_AdvancesCardAndKeepsPlaying()
        {
            _c.StartRun(2024); // deterministic; first-card NO is never lethal
            string before = _c.CardLine;
            _c.ChooseNo();
            yield return null;
            Assert.IsFalse(_c.IsShowingObituary);
            Assert.AreNotEqual(before, _c.CardLine, "the next card should be shown");
        }

        [UnityTest]
        public IEnumerator FullRun_ReachesObituaryWithMemories()
        {
            _c.StartRun(2024);
            int guard = 0;
            // Balanced-safe play survives all four stages to a natural death.
            while (!_c.IsShowingObituary && guard++ < 200)
                ChooseBalancedSafe(_c);

            Assert.IsTrue(_c.IsShowingObituary, "the run must end in an obituary");
            StringAssert.Contains("НЕКРОЛОГ", _c.ObituaryText);
            StringAssert.Contains("А помнишь", _c.ObituaryText);
            StringAssert.Contains("Ярлык жизни", _c.ObituaryText);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Restart_ResetsScalesAndStage()
        {
            _c.StartRun(555);
            int guard = 0;
            while (!_c.IsShowingObituary && guard++ < 200)
                _c.ChooseNo();
            Assert.IsTrue(_c.IsShowingObituary);

            _c.RestartLife();
            yield return null;
            Assert.IsFalse(_c.IsShowingObituary);
            StringAssert.Contains("Настроение 50", _c.ScalesLine);
            Assert.AreEqual(Stage.Childhood, _c.Game.CurrentStage);
        }

        [UnityTest]
        public IEnumerator Timeout_ResolvesAndKeepsGameConsistent()
        {
            _c.StartRun(7);
            int before = _c.Game.History.Count;
            _c.ForceTimeout();
            yield return null;
            Assert.AreEqual(before + 1, _c.Game.History.Count, "timeout must resolve one card");
        }

        [UnityTest]
        public IEnumerator PlayingSeveralChoices_ProducesNoConsoleErrors()
        {
            _c.StartRun(2024);
            int guard = 0;
            while (!_c.IsShowingObituary && guard++ < 200)
                ChooseBalancedSafe(_c);
            _c.RestartLife();
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        private static void ChooseBalancedSafe(LifeGameController c)
        {
            Card card = c.Game.CurrentCard;
            Side death = card.DeathSide;
            int yes = death == Side.Yes ? int.MaxValue : Deviation(c.Game.Scales, card.Yes);
            int no = death == Side.No ? int.MaxValue : Deviation(c.Game.Scales, card.No);
            if (yes <= no) c.ChooseYes(); else c.ChooseNo();
        }

        private static int Deviation(ScaleState s, Effect e)
        {
            var probe = new ScaleState();
            probe.Apply(new Effect(
                s.Get(Scale.Mood) - 50, s.Get(Scale.Health) - 50,
                s.Get(Scale.Money) - 50, s.Get(Scale.People) - 50));
            probe.Apply(e);
            int max = 0;
            foreach (Scale sc in new[] { Scale.Mood, Scale.Health, Scale.Money, Scale.People })
                max = System.Math.Max(max, System.Math.Abs(probe.Get(sc) - 50));
            return max;
        }
    }
}
