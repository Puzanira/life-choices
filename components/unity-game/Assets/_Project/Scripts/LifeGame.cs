using System.Collections.Generic;

namespace LifeChoices
{
    /// <summary>
    /// The pure (Unity-free) game model: one mini-life. Owns the scales, the
    /// per-stage deck, the run history and death resolution. The MonoBehaviour
    /// driver wraps this with a real-time timer, input and UI.
    ///
    /// Death priority (docs): ☠ instant → broken scale → natural (survived all).
    /// </summary>
    public sealed class LifeGame
    {
        public ScaleState Scales { get; private set; }
        public Card CurrentCard { get; private set; }
        public Stage CurrentStage => _deck.Current;
        public bool IsDead { get; private set; }
        public DeathInfo Death { get; private set; }
        public Obituary Obituary { get; private set; }
        public IReadOnlyList<MemoryEntry> History => _history;

        private StageDeck _deck;
        private readonly List<MemoryEntry> _history = new List<MemoryEntry>();

        public LifeGame(int? seed = null) => StartNewLife(seed);

        /// <summary>Resets to a fresh childhood run, scales back to 50.</summary>
        public void StartNewLife(int? seed = null)
        {
            Scales = new ScaleState();
            _deck = new StageDeck(seed);
            _history.Clear();
            IsDead = false;
            Obituary = null;
            DrawNext();
        }

        public float CurrentWindowSeconds =>
            CurrentCard != null ? Windows.Seconds(CurrentCard.Window) : 0f;

        public void ChooseYes() => Resolve(Side.Yes, timeout: false);
        public void ChooseNo() => Resolve(Side.No, timeout: false);

        /// <summary>Timer expired: resolves as НЕТ with an extra Настроение−4.</summary>
        public void Timeout() => Resolve(Side.No, timeout: true);

        private void Resolve(Side side, bool timeout)
        {
            if (IsDead || CurrentCard == null) return;
            Card card = CurrentCard;

            // Priority 1: a lethal card side (☠ instant death, or the peaceful finale).
            if (card.DeathSide == side)
            {
                _history.Add(new MemoryEntry(card, side, timeout, 0));
                Die(card.PeacefulDeath
                    ? DeathInfo.Natural(card.DeathCause)
                    : DeathInfo.Instant(card.DeathCause));
                return;
            }

            Effect eff = side == Side.Yes ? card.Yes : card.No;
            if (timeout) eff = eff.WithMoodDelta(-4); // «жизнь прошла мимо»

            Scales.Apply(eff);
            _history.Add(new MemoryEntry(card, side, timeout, eff.AbsSum()));

            // Priority 2: a scale driven to a lethal boundary.
            if (Scales.TryGetBroken(out Scale broken, out bool high))
            {
                Die(DeathInfo.Broken(DeathTables.BrokenCause(broken, high)));
                return;
            }

            DrawNext();
        }

        private void DrawNext()
        {
            if (_deck.TryDraw(out Card next))
            {
                CurrentCard = next;
            }
            else
            {
                // Priority 3: survived all four stages.
                CurrentCard = null;
                Die(DeathInfo.Natural("Дожил до титров — жизнь прошла целиком."));
            }
        }

        private void Die(DeathInfo death)
        {
            Death = death;
            IsDead = true;
            Obituary = ObituaryBuilder.Build(death, Scales, _history);
        }
    }
}
