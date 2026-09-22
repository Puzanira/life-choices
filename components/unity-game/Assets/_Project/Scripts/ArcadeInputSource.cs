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
    ///   HeightA       → энергия: HELD EnergyHold, re-emitted every frame the sensor sits above mid-travel
    ///   Joystick.y    → балансир отношений (held RelationUp/RelationDown, spring-return = drift)
    ///
    /// A drop-in <c>SerialInputSource</c> is unnecessary: swapping keyboard→Arduino is a backend swap inside
    /// arcade-controls, invisible to this class and the game.
    /// </summary>
    /// <remarks>
    /// ⚠ ПОРЯДОК ИСПОЛНЕНИЯ (r5 п.1, «без задержки отклика»). Кадр обязан идти БУТЕРБРОДОМ из трёх слоёв:
    ///
    ///   1. <c>ArcadeInputRunner</c> (ПАКЕТ, порядок 0) — опрашивает бэкенд, обновляет <c>ArcadeInput</c>;
    ///   2. <c>ArcadeInputSource</c> (МЫ, <see cref="InputBeforeTick"/>) — читает свежее состояние и ЛАТЧИТ
    ///      семантический ввод (ось балансира, датчик высоты, фронты кнопок);
    ///   3. <see cref="GameDriver"/> (<see cref="GameDriver.TickAfterInput"/>) — сводит латч вызовом
    ///      <c>Game.Tick</c>.
    ///
    /// Без явных порядков Unity ставила все три как попало: ось, взведённая в кадре N, доезжала до
    /// интегратора кадром N+1, и порядок мог отличаться между сценой, тестом и билдом.
    ///
    /// ⚠ ПОЧЕМУ ЧИСЛА ПОЛОЖИТЕЛЬНЫЕ, А НЕ ОТРИЦАТЕЛЬНЫЕ. Первая редакция r5 пинила источник на −100 —
    /// «пораньше всех». Это ломало слой 1: раннер пакета остаётся на 0, и мы начинали читать состояние
    /// устройства ДО того, как он его обновил, то есть кадром СТАРШЕ (а дельта крутилки считается
    /// per-poll и так просто терялась). Поймал это не глаз, а живой прогон реальной цепочки кабинета —
    /// `ChildPhoneTests.BangButton_ThroughTheRealCabinetChain…` вставал на таймауте. Раннер пакета трогать
    /// нельзя, поэтому оба НАШИХ слоя уезжают ВПРАВО от него: 100 и 200.
    /// </remarks>
    [DefaultExecutionOrder(InputBeforeTick)]
    public sealed class ArcadeInputSource : MonoBehaviour, IInputSource
    {
        /// <summary>Приоритет исполнения: ПОЗЖЕ раннера пакета (он на 0 и обновляет ArcadeInput), но
        /// строго РАНЬШЕ <see cref="GameDriver.TickAfterInput"/>, чтобы латч сводился ТЕМ ЖЕ кадром.
        /// Гард — PlaytestFixesR5Tests.InputSource_Runs_AfterTheDevicePump_AndBeforeTheTick.</summary>
        public const int InputBeforeTick = 100;

        public event Action<GameInput> Received;

        [Tooltip("Crank rotation (degrees, either direction) that equals one MoneyTick.")]
        [SerializeField] private float degreesPerMoneyTick = 12f;

        // Датчик высоты (HeightA): УДЕРЖИВАЕМЫЙ сигнал «датчик поднят», переиздаётся каждый кадр, пока рука
        // держит датчик выше середины хода (гистерезис — в PURE BreathSensor, чтобы весь клавиатурный путь
        // прошагивался детерминированно в EditMode: BreathKeyboardPathTests).
        private readonly BreathSensor _breathSensor = new();
        // Relationship balancer (Joystick.y): held axis, re-emitted every frame past the deadzone.
        private const float JoyDeadzone = 0.4f;

        private float _crankAccum;
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

            // ---- датчик высоты (HeightA) → held EnergyHold, переиздаётся КАЖДЫЙ кадр, пока датчик поднят ----
            if (_breathSensor.Step(ArcadeInput.HeightA.Value)) Emit(GameInput.EnergyHold);

            // ---- relationship balancer (Joystick vertical) → held axis, re-emitted each frame ----
            float y = ArcadeInput.Joystick.Vector.y;
            if (y >= JoyDeadzone) Emit(GameInput.RelationUp);
            else if (y <= -JoyDeadzone) Emit(GameInput.RelationDown);
        }

        private void Emit(GameInput input) => Received?.Invoke(input);
    }
}
