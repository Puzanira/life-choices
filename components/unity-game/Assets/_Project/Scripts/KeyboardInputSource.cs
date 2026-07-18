using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ThanksNoThanks
{
    /// <summary>
    /// Keyboard implementation of <see cref="IInputSource"/>.
    /// ← = ДА (ANSWER_YES), → = СПАСИБО НЕ НАДО (ANSWER_NO), Enter/Numpad-Enter = CONFIRM,
    /// Пробел = MONEY_TICK, E = ENERGY_PULSE, ↑/↓ = RELATION_AXIS (held). This source is a PURE
    /// key→semantic mapping: it always emits MONEY_TICK for
    /// Space (fresh) / MONEY_TICK_REPEAT (held). The driver treats Space as the money crank ONLY — inert
    /// outside Playing — so Space never confirms/starts/restarts (founder Gate-2; also matches the
    /// hardware: crank encoder and CONFIRM button are separate controls). Held Space auto-repeats ~4/с
    /// via <see cref="MoneyCrankAutoRepeat"/>; the ~5/с anti-mash INCOME cap lives in the driver's
    /// gameplay-crank branch (<see cref="MoneyTickThrottle"/>), not here.
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
                         crank == CrankEmit.Repeat,
                         kb.eKey.wasPressedThisFrame,
                         // RELATION_AXIS is a HELD axis, so it keys off isPressed (re-emitted every
                         // frame the key is down), not wasPressedThisFrame. ↑ up / ↓ down.
                         kb.upArrowKey.isPressed,
                         kb.downArrowKey.isPressed))
                Received?.Invoke(input);
        }

        /// <summary>
        /// Pure key→semantic mapping. Engine-agnostic so the control scheme is deterministically testable.
        /// ← = ДА, → = СПАСИБО НЕ НАДО, Enter/Numpad-Enter = CONFIRM; a FRESH Space keydown = MONEY_TICK,
        /// a held-Space autorepeat = MONEY_TICK_REPEAT (income-only — never confirms screens); a fresh
        /// E keydown = ENERGY_PULSE (raw breath — the driver rhythm-validates before it reaches Game).
        /// </summary>
        public static IEnumerable<GameInput> Map(bool left, bool right, bool enter, bool numpadEnter,
            bool moneyTick, bool moneyTickRepeat, bool energyPulse = false,
            bool relationUp = false, bool relationDown = false)
        {
            if (left) yield return GameInput.AnswerYes;
            if (right) yield return GameInput.AnswerNo;
            if (enter || numpadEnter) yield return GameInput.Confirm;
            if (moneyTick) yield return GameInput.MoneyTick;
            if (moneyTickRepeat) yield return GameInput.MoneyTickRepeat;
            if (energyPulse) yield return GameInput.EnergyPulse;
            // RELATION_AXIS ↑/↓ — a held axis. Mutually exclusive with ↑ PRIORITY: if a fumbling player
            // holds both keys, the marker pulls UP (the safe direction), never a surprise dive toward a
            // breakup. Mirrors a real single-axis lever/joystick, which can't report both ways at once.
            if (relationUp) yield return GameInput.RelationUp;
            else if (relationDown) yield return GameInput.RelationDown;
        }
    }
}
