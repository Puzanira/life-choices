using System;
using System.Collections.Generic;
using System.Linq;

namespace ThanksNoThanks
{
    public enum GameState { Opener, Playing, Finale }

    /// <summary>
    /// Pure, engine-free spine of «Спасибо, не надо»: the 3-state machine
    /// (Opener → Playing → Finale → Opener), the event-time age model, the 5-second
    /// card timer (timeout = random answer), passive Δ application, FATAL / burnout /
    /// natural endings, and necrolog assembly. Time is injected via <see cref="Tick"/>
    /// so PlayMode tests drive it by API instead of racing the wall clock.
    /// </summary>
    public sealed class Game
    {
        public const float CardSeconds = 5f;             // 5 sec/card
        public const float AgeCatchUpPerSecond = 12f;    // age "catches up" to the card's age
        public const float AgeSlowTickPerSecond = 0.4f;  // ticks slowly once caught up

        private readonly List<Card> _deck;
        private readonly Func<bool> _coin;               // true => ДА / "+" side of a ±N delta
        private readonly List<NecrologEntry> _entries = new();
        private int _index;

        public GameState State { get; private set; } = GameState.Opener;
        public Card CurrentCard { get; private set; }
        public Scales Scales { get; } = new Scales();
        public float Age { get; private set; }

        /// <summary>
        /// The age timer starts only when the card flagged <see cref="Card.StartsAgeTimer"/>
        /// (I03 «Сделать первый шаг?») resolves — with either answer. Until then Age stays 0.
        /// </summary>
        public bool AgeRunning { get; private set; }
        public float CardTimer { get; private set; }
        public string Cause { get; private set; }
        public NecrologResult Necrolog { get; private set; }

        public event Action StateChanged;
        public event Action CardChanged;

        public Game(IEnumerable<Card> deck, Func<bool> coin = null)
        {
            _deck = deck?.ToList() ?? new List<Card>();
            if (coin != null)
            {
                _coin = coin;
            }
            else
            {
                var rng = new Random();              // engine-free RNG
                _coin = () => rng.Next(2) == 0;
            }
        }

        public int DeckCount => _deck.Count;
        public int CardIndex => _index;

        /// <summary>Route a semantic input into the current state. The ONLY entry point for input.</summary>
        public void HandleInput(GameInput input)
        {
            switch (State)
            {
                case GameState.Opener:
                    if (input == GameInput.Confirm) StartLife();
                    break;
                case GameState.Playing:
                    if (input == GameInput.AnswerYes) Answer(true);
                    else if (input == GameInput.AnswerNo) Answer(false);
                    break;
                case GameState.Finale:
                    if (input == GameInput.Confirm) ToOpener();
                    break;
            }
        }

        public void StartLife()
        {
            Scales.Reset();
            _entries.Clear();
            _index = -1;
            Age = 0f;
            AgeRunning = false;
            Cause = null;
            Necrolog = null;
            State = GameState.Playing;
            StateChanged?.Invoke();
            Advance();
        }

        /// <summary>Advance injected time: event-age progression + card countdown (timeout = random answer).</summary>
        public void Tick(float dt)
        {
            if (State != GameState.Playing || CurrentCard == null) return;

            if (AgeRunning) // age timer starts only once I03 has resolved
            {
                float target = CurrentCard.Age;
                if (Age < target)
                    Age = Math.Min(target, Age + AgeCatchUpPerSecond * dt);
                else
                    Age += AgeSlowTickPerSecond * dt;
            }

            CardTimer -= dt;
            if (CardTimer <= 0f)
                Answer(_coin());   // не успел — берём ДА или НЕТ случайно
        }

        private void Advance()
        {
            _index++;
            if (_index >= _deck.Count) { EndNatural(); return; }
            CurrentCard = _deck[_index];
            CardTimer = CardSeconds;
            CardChanged?.Invoke();
        }

        private void Answer(bool yes)
        {
            var card = CurrentCard;
            if (card == null) return;

            if (card.StartsAgeTimer)
                AgeRunning = true; // ДА и НЕТ равнозначны: «шаг всё равно происходит»

            if (!card.IsNoCons)
            {
                Scales.Apply(yes ? card.YesDeltas : card.NoDeltas, _coin);
                string line = yes ? card.YesNecrolog : card.NoNecrolog;
                if (!string.IsNullOrEmpty(line))
                    _entries.Add(new NecrologEntry
                    {
                        Age = card.Age,
                        Order = card.Order,
                        Line = line,
                        IsRond = card.IsRond,
                    });
            }

            if (yes && card.YesIsFatal) { End(card.FatalCause); return; }
            if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
            if (Scales.EnergyDepleted) { End("полное выгорание"); return; }

            Advance();
        }

        // Past the last spine card's age → "natural" end; cause by relationships (family/loneliness).
        private void EndNatural()
        {
            string cause =
                Scales.Relationships >= 60 ? "весёлая старость" :
                Scales.Relationships <= 50 ? "одинокая старость" :
                                             "спокойная старость";
            End(cause);
        }

        private void End(string cause)
        {
            Cause = cause;
            Necrolog = ThanksNoThanks.Necrolog.Build(cause, _entries);
            CurrentCard = null;
            State = GameState.Finale;
            StateChanged?.Invoke();
        }

        private void ToOpener()
        {
            Scales.Reset();
            _entries.Clear();
            _index = -1;
            Age = 0f;
            AgeRunning = false;
            Cause = null;
            Necrolog = null;
            CurrentCard = null;
            State = GameState.Opener;
            StateChanged?.Invoke();
        }
    }
}
