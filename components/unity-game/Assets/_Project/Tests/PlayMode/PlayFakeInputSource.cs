using System;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>Injectable test input source for PlayMode driver tests.</summary>
    public sealed class PlayFakeInputSource : IInputSource
    {
        public event Action<GameInput> Received;
        public void Fire(GameInput input) => Received?.Invoke(input);
        public void Yes() => Fire(GameInput.AnswerYes);
        public void No() => Fire(GameInput.AnswerNo);
        public void Confirm() => Fire(GameInput.Confirm);
    }
}
