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
        Confirm,         // start / restart (keyboard: Enter; fresh Space in Opener/Finale/tutorial — driver-mapped)
        MoneyTick,       // крутилка денег, FRESH physical keydown (may double as CONFIRM outside gameplay)

        /// <summary>
        /// Autorepeat crank from HELD Space (~4/с) — input-layer kind, income-only. The driver lets it
        /// crank during gameplay (translated to <see cref="MoneyTick"/> after the income cap) but keeps
        /// it INERT outside Playing and on the tutorial: held Space must never confirm screens or
        /// dismiss hints. <see cref="Game"/> never receives this value.
        /// </summary>
        MoneyTickRepeat
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
