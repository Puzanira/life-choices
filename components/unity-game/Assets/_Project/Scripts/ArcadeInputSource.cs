using System;
using AiGameStudio.ArcadeControls;
using UnityEngine;

namespace ThanksNoThanks
{
    /// <summary>
    /// Arcade-cabinet implementation of <see cref="IInputSource"/>. It is the ONLY input source the game
    /// ships with, and it reads input EXCLUSIVELY through <see cref="ArcadeInput"/> — never a raw device
    /// (that lives behind the arcade-controls backend seam: KeyboardBackend for dev, SerialBackend on the
    /// cabinet). It translates the cabinet's logical controls into the game's semantic <see cref="GameInput"/>
    /// events; the driver + <see cref="Game"/> stay device-agnostic exactly as before.
    ///
    /// Physical → semantic mapping (founder Gate-2 decisions marked ✔; the rest are natural defaults awaiting
    /// Gate-2 sign-off — see the increment report):
    ///   GreenButton ✔ → ДА  (AnswerYes; on the opener/hint/FINALE the driver reads it as CONFIRM —
    ///                        i.e. start / dismiss / restart: the ONE confirm control, founder 99fab3c)
    ///   RedButton   ✔ → НЕТ (AnswerNo; answer-only. On the finale it is deliberately INERT so a masher
    ///                        can never skip the necrolog — this overrides the old «рестарт = красная»)
    ///   MenuButton  ✔ → выход (Exit — clean end-of-run, arcade contract §5)
    ///   Crank         → крутилка денег (accumulated degrees → discrete MoneyTick — a literal money crank)
    ///   BangButton    → «поднять трубку» звонящего ребёнка (ChildPress, revisions §5b)
    ///   HeightA       → дыхание: one EnergyPulse per up-stroke through mid-travel (breathing lever)
    ///   Joystick.y    → балансир отношений (held RelationUp/RelationDown, spring-return = drift)
    ///
    /// A drop-in <c>SerialInputSource</c> is unnecessary: swapping keyboard→Arduino is a backend swap inside
    /// arcade-controls, invisible to this class and the game.
    /// </summary>
    public sealed class ArcadeInputSource : MonoBehaviour, IInputSource
    {
        public event Action<GameInput> Received;

        [Tooltip("Crank rotation (degrees, either direction) that equals one MoneyTick.")]
        [SerializeField] private float degreesPerMoneyTick = 12f;

        // Breathing lever (HeightA): one pulse per rising crossing of mid-travel, re-armed once it drops back.
        private const float BreathHigh = 0.5f;
        private const float BreathLow = 0.35f;
        // Relationship balancer (Joystick.y): held axis, re-emitted every frame past the deadzone.
        private const float JoyDeadzone = 0.4f;

        private float _crankAccum;
        private float _prevHeightA;
        private bool _breathArmed = true;
        private bool _prevGreen, _prevRed, _prevBang, _prevMenu;

        private void Awake()
        {
            // The game needs ArcadeInput pumped every frame. If no runner is present in the scene (standalone
            // game / editor screenshot), add one — it builds the packaged KeyboardBackend (dev simulation) and
            // pumps ArcadeInput.Update each frame. On the cabinet the launcher/hub provides the runner + serial
            // backend, so this is a no-op there.
            if (UnityEngine.Object.FindAnyObjectByType<ArcadeInputRunner>() == null)
                gameObject.AddComponent<ArcadeInputRunner>();
        }

        private void Update()
        {
            if (ArcadeInput.Backend == null) return; // runner hasn't initialized ArcadeInput yet this frame

            // ---- discrete buttons: own edge detection (independent of ArcadeInput's own Pressed events,
            //      which Initialize() clears — polling IsHeld here is reset-safe and order-independent) ----
            bool green = ArcadeInput.GreenButton.IsHeld;
            bool red = ArcadeInput.RedButton.IsHeld;
            bool bang = ArcadeInput.BangButton.IsHeld;
            bool menu = ArcadeInput.MenuButton.IsHeld;

            if (green && !_prevGreen) Emit(GameInput.AnswerYes);   // ДА  ✔
            if (red && !_prevRed) Emit(GameInput.AnswerNo);        // НЕТ ✔
            if (bang && !_prevBang) Emit(GameInput.ChildPress);    // «поднять трубку»
            if (menu && !_prevMenu) Emit(GameInput.Exit);          // выход ✔
            _prevGreen = green; _prevRed = red; _prevBang = bang; _prevMenu = menu;

            // ---- money crank → one MoneyTick per configured degrees, either direction ----
            _crankAccum += Mathf.Abs(ArcadeInput.Crank.DeltaDegrees);
            int guard = 0;
            while (_crankAccum >= degreesPerMoneyTick && guard++ < 64)
            {
                _crankAccum -= degreesPerMoneyTick;
                Emit(GameInput.MoneyTick);
            }

            // ---- breathing lever (HeightA) → one EnergyPulse per up-stroke through mid-travel ----
            float h = ArcadeInput.HeightA.Value;
            if (_breathArmed && _prevHeightA < BreathHigh && h >= BreathHigh)
            {
                Emit(GameInput.EnergyPulse);
                _breathArmed = false;
            }
            else if (!_breathArmed && h <= BreathLow)
            {
                _breathArmed = true; // lever came back down → ready for the next breath
            }
            _prevHeightA = h;

            // ---- relationship balancer (Joystick vertical) → held axis, re-emitted each frame ----
            float y = ArcadeInput.Joystick.Vector.y;
            if (y >= JoyDeadzone) Emit(GameInput.RelationUp);
            else if (y <= -JoyDeadzone) Emit(GameInput.RelationDown);
        }

        private void Emit(GameInput input) => Received?.Invoke(input);
    }
}
