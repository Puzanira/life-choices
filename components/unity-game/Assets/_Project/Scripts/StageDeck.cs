using System;
using System.Collections.Generic;

namespace LifeChoices
{
    /// <summary>
    /// Per-stage "bag" a-la Reigns: each stage draws its cards shuffled with no
    /// repeat within a run; when a stage's bag empties, the next stage begins.
    /// After Старость empties, TryDraw returns false → natural death.
    /// 🔗 conditional-unlock logic is deferred: those cards are dealt normally.
    /// </summary>
    public sealed class StageDeck
    {
        private readonly Queue<Card>[] _bags = new Queue<Card>[4];
        public Stage Current { get; private set; } = Stage.Childhood;

        public StageDeck(int? seed = null)
        {
            var rng = new Random(seed ?? Environment.TickCount);
            for (int s = 0; s < 4; s++)
            {
                var cards = CardDatabase.ForStage((Stage)s);
                Shuffle(cards, rng);
                _bags[s] = new Queue<Card>(cards);
            }
        }

        private static void Shuffle(List<Card> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Draws the next card, advancing stages as bags empty.</summary>
        public bool TryDraw(out Card card)
        {
            while (_bags[(int)Current].Count == 0)
            {
                if (Current == Stage.OldAge) { card = null; return false; }
                Current = (Stage)((int)Current + 1);
            }
            card = _bags[(int)Current].Dequeue();
            return true;
        }

        /// <summary>Remaining undrawn cards across all stages (for tests).</summary>
        public int Remaining()
        {
            int n = 0;
            for (int s = 0; s < 4; s++) n += _bags[s].Count;
            return n;
        }
    }
}
