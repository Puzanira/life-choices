using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Verifies the concrete control scheme of the input abstraction:
    /// ← = ANSWER_YES (ДА), → = ANSWER_NO (СПАСИБО НЕ НАДО), Enter/Numpad-Enter/Space = CONFIRM.
    /// </summary>
    public class KeyboardMappingTests
    {
        private static List<GameInput> M(bool l, bool r, bool enter, bool numEnter, bool space)
            => KeyboardInputSource.Map(l, r, enter, numEnter, space).ToList();

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
        public void Enter_Space_NumpadEnter_AreConfirm()
        {
            CollectionAssert.AreEqual(new[] { GameInput.Confirm }, M(false, false, true, false, false));
            CollectionAssert.AreEqual(new[] { GameInput.Confirm }, M(false, false, false, true, false));
            CollectionAssert.AreEqual(new[] { GameInput.Confirm }, M(false, false, false, false, true));
        }

        [Test]
        public void NoKeys_YieldsNothing()
        {
            Assert.IsEmpty(M(false, false, false, false, false));
        }
    }
}
