using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Verifies the concrete control scheme of the input abstraction:
    /// ← = ANSWER_YES (ДА), → = ANSWER_NO (СПАСИБО НЕ НАДО), Enter/Numpad-Enter = CONFIRM,
    /// a fresh Space keydown = MONEY_TICK, a held-Space autorepeat = MONEY_TICK_REPEAT.
    /// (Space→CONFIRM on opener/finale screens is a driver re-map for FRESH presses only,
    /// deliberately NOT in this pure key→semantic mapping.)
    /// </summary>
    public class KeyboardMappingTests
    {
        private static List<GameInput> M(bool l, bool r, bool enter, bool numEnter,
            bool moneyTick, bool moneyTickRepeat = false, bool energyPulse = false)
            => KeyboardInputSource.Map(l, r, enter, numEnter, moneyTick, moneyTickRepeat, energyPulse).ToList();

        [Test]
        public void LeftArrow_IsAnswerYes()
        {
            CollectionAssert.AreEqual(new[] { GameInput.AnswerYes }, M(true, false, false, false, false));
        }

        [Test]
        public void RightArrow_IsAnswerNo()
        {
            CollectionAssert.AreEqual(new[] { GameInput.AnswerNo }, M(false, true, false, false, false));
        }

        [Test]
        public void Enter_And_NumpadEnter_AreConfirm()
        {
            CollectionAssert.AreEqual(new[] { GameInput.Confirm }, M(false, false, true, false, false));
            CollectionAssert.AreEqual(new[] { GameInput.Confirm }, M(false, false, false, true, false));
        }

        [Test]
        public void SpaceTick_IsMoneyTick_NotConfirm()
        {
            CollectionAssert.AreEqual(new[] { GameInput.MoneyTick }, M(false, false, false, false, true));
        }

        [Test]
        public void SpaceAutoRepeat_IsMoneyTickRepeat_DistinctFromFreshPress()
        {
            CollectionAssert.AreEqual(new[] { GameInput.MoneyTickRepeat },
                M(false, false, false, false, false, true));
        }

        [Test]
        public void EKey_IsEnergyPulse()
        {
            CollectionAssert.AreEqual(new[] { GameInput.EnergyPulse },
                M(false, false, false, false, false, false, true));
        }

        [Test]
        public void NoKeys_YieldsNothing()
        {
            Assert.IsEmpty(M(false, false, false, false, false));
        }
    }
}
