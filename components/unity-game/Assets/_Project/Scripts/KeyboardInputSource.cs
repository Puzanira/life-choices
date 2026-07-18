using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ThanksNoThanks
{
    /// <summary>
    /// Keyboard implementation of <see cref="IInputSource"/>.
    /// ← = ДА (ANSWER_YES), → = СПАСИБО НЕ НАДО (ANSWER_NO), Enter/Numpad-Enter = CONFIRM,
    /// Пробел = MONEY_TICK. Every discrete Space keydown emits IMMEDIATELY (never swallowed — outside
    /// gameplay Space doubles as CONFIRM via the driver re-map and must be instant); held Space
    /// auto-repeats ~4/с via <see cref="MoneyCrankAutoRepeat"/>. The ~5/с anti-mash INCOME cap lives
    /// in the driver's gameplay-crank branch (<see cref="MoneyTickThrottle"/>), not here. The
    /// state-dependent Space→CONFIRM re-map lives in the driver wiring (<see cref="GameDriver"/>),
    /// NOT here and NOT in <see cref="Game"/> — this source stays a pure key→semantic mapping.
    /// A future SerialInputSource (Arduino/COM) is a drop-in replacement of this one class.
    /// </summary>
    public sealed class KeyboardInputSource : MonoBehaviour, IInputSource
    {
        public event Action<GameInput> Received;

        private readonly MoneyCrankAutoRepeat _crank = new();

        private void Update() => Poll(Time.deltaTime);

        /// <summary>Reads the current keyboard once and raises semantic events (called every frame).</summary>
        public void Poll(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            var crank = _crank.Step(dt, kb.spaceKey.wasPressedThisFrame, kb.spaceKey.isPressed);
            foreach (var input in Map(
                         kb.leftArrowKey.wasPressedThisFrame,
                         kb.rightArrowKey.wasPressedThisFrame,
                         kb.enterKey.wasPressedThisFrame,
                         kb.numpadEnterKey.wasPressedThisFrame,
                         crank == CrankEmit.Press,
                         crank == CrankEmit.Repeat))
                Received?.Invoke(input);
        }

        /// <summary>
        /// Pure key→semantic mapping. Engine-agnostic so the control scheme is deterministically testable.
        /// ← = ДА, → = СПАСИБО НЕ НАДО, Enter/Numpad-Enter = CONFIRM; a FRESH Space keydown = MONEY_TICK,
        /// a held-Space autorepeat = MONEY_TICK_REPEAT (income-only — never confirms screens).
        /// </summary>
        public static IEnumerable<GameInput> Map(bool left, bool right, bool enter, bool numpadEnter,
            bool moneyTick, bool moneyTickRepeat)
        {
            if (left) yield return GameInput.AnswerYes;
            if (right) yield return GameInput.AnswerNo;
            if (enter || numpadEnter) yield return GameInput.Confirm;
            if (moneyTick) yield return GameInput.MoneyTick;
            if (moneyTickRepeat) yield return GameInput.MoneyTickRepeat;
        }
    }
}
