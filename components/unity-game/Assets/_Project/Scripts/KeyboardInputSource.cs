using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ThanksNoThanks
{
    /// <summary>
    /// Keyboard implementation of <see cref="IInputSource"/>.
    /// ← = ДА (ANSWER_YES), → = СПАСИБО НЕ НАДО (ANSWER_NO), Enter/Space = CONFIRM.
    /// A future SerialInputSource (Arduino/COM) is a drop-in replacement of this one
    /// class; the game logic subscribes to the semantic events, not to the keys.
    /// </summary>
    public sealed class KeyboardInputSource : MonoBehaviour, IInputSource
    {
        public event Action<GameInput> Received;

        private void Update() => Poll();

        /// <summary>Reads the current keyboard once and raises semantic events (called every frame).</summary>
        public void Poll()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            foreach (var input in Map(
                         kb.leftArrowKey.wasPressedThisFrame,
                         kb.rightArrowKey.wasPressedThisFrame,
                         kb.enterKey.wasPressedThisFrame,
                         kb.numpadEnterKey.wasPressedThisFrame,
                         kb.spaceKey.wasPressedThisFrame))
                Received?.Invoke(input);
        }

        /// <summary>
        /// Pure key→semantic mapping. Kept engine-agnostic so the control scheme
        /// (← = ДА, → = СПАСИБО НЕ НАДО, Enter/Space = CONFIRM) is deterministically testable.
        /// </summary>
        public static IEnumerable<GameInput> Map(bool left, bool right, bool enter, bool numpadEnter, bool space)
        {
            if (left) yield return GameInput.AnswerYes;
            if (right) yield return GameInput.AnswerNo;
            if (enter || numpadEnter || space) yield return GameInput.Confirm;
        }
    }
}
