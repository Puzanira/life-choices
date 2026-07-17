using System;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>A test IInputSource that fires semantic events on demand (no keyboard hardware).</summary>
    public sealed class FakeInputSource : IInputSource
    {
        public event Action<GameInput> Received;
        public void Fire(GameInput input) => Received?.Invoke(input);

        public void Yes() => Fire(GameInput.AnswerYes);
        public void No() => Fire(GameInput.AnswerNo);
        public void Confirm() => Fire(GameInput.Confirm);
    }
}
