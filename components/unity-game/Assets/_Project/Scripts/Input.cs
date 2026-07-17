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
        AnswerYes,  // ДА          (keyboard: ← )
        AnswerNo,   // СПАСИБО, НЕ НАДО (keyboard: → )
        Confirm     // start / restart (keyboard: Enter / Space)
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
