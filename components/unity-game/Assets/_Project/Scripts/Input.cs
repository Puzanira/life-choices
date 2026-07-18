using System;

namespace ThanksNoThanks
{
    /// <summary>
    /// Semantic input events the game logic reacts to. Game logic subscribes to these
    /// and NEVER reads a keyboard/serial device directly — swapping the input hardware
    /// (keyboard now, Arduino/COM later) is a drop-in replacement of a single
    /// <see cref="IInputSource"/> implementation, the logic untouched.
    /// </summary>
    public enum GameInput
    {
        AnswerYes,       // ДА          (keyboard: ← )
        AnswerNo,        // СПАСИБО, НЕ НАДО (keyboard: → )
        Confirm,         // start / dismiss-hint / restart — keyboard Enter ONLY (the sole confirm key)
        MoneyTick,       // крутилка денег, FRESH physical keydown — crank-only, inert outside gameplay

        /// <summary>
        /// Autorepeat crank from HELD Space (~4/с) — input-layer kind, income-only. The driver lets it
        /// crank during gameplay (translated to <see cref="MoneyTick"/> after the income cap) but keeps
        /// it INERT outside Playing and on the tutorial: held Space must never confirm screens or
        /// dismiss hints. <see cref="Game"/> never receives this value.
        /// </summary>
        MoneyTickRepeat,

        /// <summary>
        /// ENERGY_PULSE — «дыхание» (keyboard: E; later a physical breathing lever). The RAW key press
        /// goes to the driver, which validates the RHYTHM (a pure <see cref="BreathRhythm"/> with an
        /// injectable clock) and forwards this value to <see cref="Game"/> ONLY on a valid breath cycle.
        /// So the value <see cref="Game"/> receives always means «a well-timed breath happened» → +energy;
        /// mashing / sparse presses never reach the logic. Keeps <see cref="Game"/> semantic-only.
        /// </summary>
        EnergyPulse
    }

    /// <summary>
    /// A source of semantic game inputs. The only contract between the physical world
    /// (keyboard, serial controller, test harness) and the game logic.
    /// </summary>
    public interface IInputSource
    {
        event Action<GameInput> Received;
    }
}
