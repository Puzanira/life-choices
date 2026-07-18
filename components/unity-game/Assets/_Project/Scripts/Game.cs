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

        // Natural old-age ending tone by relationships (GAME_SPEC: весёлая · спокойная · одинокая).
        // Serialized as tunable thresholds so the bigger deck can be re-balanced by playtest.
        public const int JoyfulOldAgeRelationships = 60;   // >= → весёлая старость
        public const int LonelyOldAgeRelationships = 50;   // <= → одинокая старость

        // ---- money crank (tunable; canon §Деньги + §Сводка констант) ----
        public const int MoneyOpenAge = 18;                  // деньги открываются в 18 («работа»)
        public const double MoneyTickIncome = 1.0;           // +1₽ × множитель за тик
        public const double CostOfLivingPerSec = 0.5;        // −0.5₽/сек, пока деньги открыты
        public const int UniversityMultFromAge = 25;         // универ-множитель включается с 25 (FROM:25)

        // BLOCK$ prices — из прозы канона (не в CSV), тюнинг-константы. Карта недоступна при деньгах < цены.
        // LT08 (100₽) — вне scope (системная карта здоровья, hard-excluded из колоды), но цена учтена
        //   на случай появления, чтобы BLOCK$ работал единообразно.
        public static readonly IReadOnlyDictionary<string, double> BlockPrices = new Dictionary<string, double>
        {
            { "MD03", 60 },   // отпуск
            { "LT02", 120 },  // операция
            { "LT08", 100 },  // подлечиться (вне scope)
        };

        private List<Card> _deck;
        private List<Card> _reserve = new();             // top-up pool for skipped chain-gated cards
        private readonly Func<IReadOnlyList<Card>> _deckFactory; // re-samples a fresh deck per life
        private readonly Func<DeckPlan> _planFactory;    // re-samples deck + reserve per life
        private readonly Func<bool> _coin;               // true => ДА / "+" side of a ±N delta
        private readonly List<NecrologEntry> _entries = new();
        private readonly Dictionary<string, bool> _answers = new(); // card id → ДА(true)/НЕТ(false)
        private int _index;

        // Delayed FATAL (RND01): scheduled event-age at which «за вами пришли» fires. NaN = none.
        private float _scheduledFatalAge = float.NaN;
        private string _scheduledFatalCause;
        // Deck exhausted while a delayed fatal is pending: no card up, age fast-forwards to the
        // scheduled age (a brief beat, not «дожил») and then the fatal fires.
        private bool _coasting;

        public GameState State { get; private set; } = GameState.Opener;
        public Card CurrentCard { get; private set; }
        public Scales Scales { get; } = new Scales();
        public float Age { get; private set; }

        // ---- live money economy (float ₽; may go negative — «в минус», canon; no death from money) ----
        private struct ActiveMult { public double Value; public int FromAge; }
        private struct MoneyDrain { public double PerSec; public float StartAge; public float EndAge; }
        private readonly List<ActiveMult> _mults = new();
        private readonly List<MoneyDrain> _drains = new();

        /// <summary>Live money in ₽ (fractional; negative allowed). Authoritative for the HUD pill.</summary>
        public double Money { get; private set; }

        /// <summary>True once the money scale has opened (Age ≥ <see cref="MoneyOpenAge"/>). One-shot per life.</summary>
        public bool MoneyOpen { get; private set; }

        /// <summary>
        /// Tutorial-pause flag (set by the driver while the S5 overlay is up). While true, <see cref="Tick"/>
        /// freezes EVERYTHING — age, cost-of-living, installment drains and the card timer (canon §Подсказки).
        /// Pure Game exposes it; the visual overlay lives in the driver.
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>
        /// BLOCK$ state of the CURRENT card, fixed at draw time: true when the card is a BLOCK$ card and
        /// money was below its price when it came up. Timer still runs; any answer/timeout skips it with
        /// no Δ, no necrolog line, no reschedule (design-agent variant «а», мокап S10).
        /// </summary>
        public bool CurrentCardBlocked { get; private set; }

        /// <summary>Current income multiplier (product of active FROM-gated multipliers). ≥ 1 unless wiped.</summary>
        public double IncomeMultiplier
        {
            get
            {
                double m = 1.0;
                foreach (var a in _mults)
                    if (a.FromAge == 0 || Age >= a.FromAge) m *= a.Value;
                return m;
            }
        }

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
        /// <summary>Fired the first time money opens in a life (drives the S5 tutorial overlay + pause).</summary>
        public event Action MoneyOpened;

        public Game(IEnumerable<Card> deck, Func<bool> coin = null, IEnumerable<Card> reserve = null)
        {
            _deck = deck?.ToList() ?? new List<Card>();
            _reserve = reserve?.ToList() ?? new List<Card>();
            _coin = coin ?? DefaultCoin();
        }

        /// <summary>
        /// Per-run sampling constructor: <paramref name="deckFactory"/> is invoked once now (so the
        /// opener already reports a deck) and again on every <see cref="StartLife"/>, giving each
        /// life a freshly sampled deck. Used by <see cref="GameDriver"/> with <see cref="DeckSampler"/>.
        /// </summary>
        public Game(Func<IReadOnlyList<Card>> deckFactory, Func<bool> coin = null)
        {
            _deckFactory = deckFactory;
            _coin = coin ?? DefaultCoin();
            _deck = deckFactory?.Invoke()?.ToList() ?? new List<Card>();
        }

        /// <summary>
        /// Plan constructor (deck + reserve): the reserve tops up the run when chain-gated cards are
        /// skipped, keeping the actually drawn count inside the 25–30 contract for any answer path.
        /// </summary>
        public Game(Func<DeckPlan> planFactory, Func<bool> coin = null)
        {
            _planFactory = planFactory;
            _coin = coin ?? DefaultCoin();
            AdoptPlan(planFactory?.Invoke());
        }

        private void AdoptPlan(DeckPlan plan)
        {
            _deck = plan?.Deck?.ToList() ?? new List<Card>();
            _reserve = plan?.Reserve?.ToList() ?? new List<Card>();
        }

        private static Func<bool> DefaultCoin()
        {
            var rng = new Random();                  // engine-free RNG
            return () => rng.Next(2) == 0;
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
                    else if (input == GameInput.MoneyTick) Crank();
                    break;
                case GameState.Finale:
                    if (input == GameInput.Confirm) ToOpener();
                    break;
            }
        }

        public void StartLife()
        {
            if (_planFactory != null)
                AdoptPlan(_planFactory());                            // fresh deck + reserve each life
            else if (_deckFactory != null)
                _deck = _deckFactory()?.ToList() ?? new List<Card>(); // fresh deck each life
            Scales.Reset();
            _entries.Clear();
            _answers.Clear();
            _index = -1;
            Age = 0f;
            AgeRunning = false;
            Cause = null;
            Necrolog = null;
            _scheduledFatalAge = float.NaN;
            _scheduledFatalCause = null;
            _coasting = false;
            ResetMoney();
            State = GameState.Playing;
            StateChanged?.Invoke();
            Advance();
        }

        /// <summary>Advance injected time: event-age progression + card countdown (timeout = random answer).</summary>
        public void Tick(float dt)
        {
            if (State != GameState.Playing) return;
            if (Paused) return; // tutorial overlay up — freeze age, drains, cost-of-living and the card timer

            if (CurrentCard == null)
            {
                // Deck exhausted with a delayed fatal pending: fast-forward age (brief beat at the
                // catch-up rate) until «за вами пришли» — never a «дожил» finale while it's pending.
                if (_coasting)
                {
                    Age += AgeCatchUpPerSecond * dt;
                    if (CheckMoneyOpen()) return; // tutorial pause can fire even during the coast
                    IntegrateMoney(dt);
                    CheckScheduledFatal();
                }
                return;
            }

            if (AgeRunning) // age timer starts only once I03 has resolved
            {
                float target = CurrentCard.Age;
                if (Age < target)
                    Age = Math.Min(target, Age + AgeCatchUpPerSecond * dt);
                else
                    Age += AgeSlowTickPerSecond * dt;

                if (CheckMoneyOpen()) return;      // open money → tutorial pause may freeze this frame
                if (CheckScheduledFatal()) return; // «за вами пришли» once age crosses card.Age+n
            }

            IntegrateMoney(dt);                    // cost-of-living + installment drains (real-time)

            CardTimer -= dt;
            if (CardTimer <= 0f)
                Answer(_coin());   // не успел — берём ДА или НЕТ случайно
        }

        // Opens the money scale the first time Age reaches 18 and announces it (tutorial + pause hook).
        // Returns true if the frame should stop here (a listener paused the game on open).
        private bool CheckMoneyOpen()
        {
            if (MoneyOpen || Age < MoneyOpenAge) return false;
            MoneyOpen = true;
            MoneyOpened?.Invoke();   // driver shows the S5 overlay and sets Paused
            return Paused;
        }

        // A CHAIN child is drawable only after its parent resolved ДА. Parents always precede their
        // children in age order, so by the time we reach a gated card its parent is already answered.
        private bool GatedOff(Card c)
            => c.RequiresParentYes != null
               && !(_answers.TryGetValue(c.RequiresParentYes, out var yes) && yes);

        private void Advance()
        {
            if (CheckScheduledFatal()) return;
            while (true)
            {
                _index++;
                if (_index >= _deck.Count) { EndOfDeck(); return; }
                var c = _deck[_index];
                if (GatedOff(c))                  // parent said НЕТ / never appeared → skip child…
                {
                    Substitute(c);                // …and top up from the reserve (drawn count 25–30)
                    continue;
                }
                CurrentCard = c;
                // BLOCK$ affordability fixed at draw time («на момент показа денег меньше цены»).
                CurrentCardBlocked = c.IsBlockCost
                    && BlockPrices.TryGetValue(c.Id, out var price)
                    && Money < price;
                CardTimer = CardSeconds;
                CardChanged?.Invoke();
                return;
            }
        }

        /// <summary>
        /// Replace a skipped chain-gated card with a reserve card so the run length holds.
        /// Deterministic (no RNG): (a) the first reserve card (age-sorted) already at an age ≥ the
        /// skipped card's age; (b) else the earliest reserve card whose «Когда» window still reaches
        /// that age, re-aged to it; (c) else no substitute (reserve exhausted). The substitute is
        /// inserted into the remaining deck at its age-sorted slot.
        /// </summary>
        private void Substitute(Card skipped)
        {
            if (_reserve.Count == 0) return;

            int ri = _reserve.FindIndex(r => r.Age >= skipped.Age);
            Card sub = null;
            if (ri >= 0)
            {
                sub = _reserve[ri];
                _reserve.RemoveAt(ri);
            }
            else
            {
                ri = _reserve.FindIndex(r => DeckSampler.AgeWindow.Parse(r.When).Max >= skipped.Age);
                if (ri < 0) return;
                sub = _reserve[ri];
                _reserve.RemoveAt(ri);
                sub.Age = skipped.Age;
            }

            int at = _deck.FindIndex(_index + 1, c => c.Age > sub.Age);
            if (at < 0) at = _deck.Count;
            _deck.Insert(at, sub);
        }

        // Deck exhausted: with a delayed fatal pending we coast (age runs on) instead of «дожил».
        private void EndOfDeck()
        {
            if (!float.IsNaN(_scheduledFatalAge))
            {
                _coasting = true;
                CurrentCard = null;
                CardChanged?.Invoke();
                return;
            }
            EndNatural();
        }

        // Delayed FATAL (RND01): fires the moment event-age reaches the scheduled age.
        private bool CheckScheduledFatal()
        {
            if (float.IsNaN(_scheduledFatalAge) || Age < _scheduledFatalAge)
                return false;
            Age = _scheduledFatalAge;             // death age snaps to «resolved age + n» exactly
            var cause = _scheduledFatalCause;
            _scheduledFatalAge = float.NaN;
            _scheduledFatalCause = null;
            _coasting = false;
            End(cause);
            return true;
        }

        private void Answer(bool yes)
        {
            var card = CurrentCard;
            if (card == null) return;

            // BLOCK$ при нехватке денег: любой ответ/таймаут = пропуск без Δ, без некролога, без
            // записи ответа (CHAIN-гейт не считает это ДА) и без повторного выпадения — просто дальше.
            if (CurrentCardBlocked)
            {
                Advance();
                return;
            }

            _answers[card.Id] = yes;   // recorded before consequences so CHAIN gates can read it

            if (card.StartsAgeTimer)
                AgeRunning = true; // ДА и НЕТ равнозначны: «шаг всё равно происходит»

            if (!card.IsNoCons)
            {
                ApplyCardDeltas(yes ? card.YesDeltas : card.NoDeltas); // money Δ → live float; rest → Scales
                if (yes) ApplyLongEffects(card);                       // multipliers / installment drains (on ДА)
                // FORCED cards (вехи/объявления, no real choice) NEVER write a necrolog line — canon.
                // Enforced structurally here, independent of what the CSV cell happens to hold.
                if (!card.IsForced)
                {
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
            }

            // Delayed fatal (RND01): ДА schedules «за вами пришли» for card.Age + n, life continues.
            if (yes && card.DelayedFatalYears > 0)
            {
                _scheduledFatalAge = card.Age + card.DelayedFatalYears;
                _scheduledFatalCause = card.FatalCause;
            }

            if (yes && card.YesIsFatal) { End(card.FatalCause); return; }
            if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
            if (Scales.EnergyDepleted) { End("полное выгорание"); return; }

            Advance();
        }

        // Survived the whole deck with nothing pending → reached old age; tone by relationships.
        // (A pending delayed fatal never gets here — EndOfDeck coasts to «за вами пришли» instead.)
        private void EndNatural()
        {
            string tone =
                Scales.Relationships >= JoyfulOldAgeRelationships ? "весёлая старость" :
                Scales.Relationships <= LonelyOldAgeRelationships ? "одинокая старость" :
                                                                    "спокойная старость";
            End(tone);
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
            _answers.Clear();
            _index = -1;
            Age = 0f;
            AgeRunning = false;
            Cause = null;
            Necrolog = null;
            _scheduledFatalAge = float.NaN;
            _scheduledFatalCause = null;
            _coasting = false;
            ResetMoney();
            CurrentCard = null;
            State = GameState.Opener;
            StateChanged?.Invoke();
        }

        // ================================================================ money crank

        private void ResetMoney()
        {
            Money = 0;
            MoneyOpen = false;
            Paused = false;
            CurrentCardBlocked = false;
            _mults.Clear();
            _drains.Clear();
        }

        /// <summary>MONEY_TICK: +1₽ × multiplier. No-op unless money is open and the run is live/unpaused.</summary>
        private void Crank()
        {
            if (!MoneyOpen || Paused) return;
            Money += MoneyTickIncome * IncomeMultiplier;
        }

        // Real-time money integration for one Tick step (cost-of-living + active installment drains).
        // Rate is per REAL second; drain windows are measured in GAME-years (canon reading, see report).
        private void IntegrateMoney(float dt)
        {
            if (!MoneyOpen) return;
            Money -= CostOfLivingPerSec * dt;
            foreach (var d in _drains)
                if (Age >= d.StartAge && Age < d.EndAge)
                    Money += d.PerSec * dt;   // PerSec is signed (e.g. −0.3)
        }

        // Apply a card's «Длительный эффект» money entries on ДА (multipliers + installment drains).
        private void ApplyLongEffects(Card card)
        {
            foreach (var e in card.LongEffects)
            {
                if (e.Scale != Scale.Money) continue;   // non-money (e.g. health MULT) inert this increment
                if (e.Kind == LongEffectKind.Mult)
                {
                    if (e.RandomZero)
                    {
                        // «×5 или обнуление денег» (YA02): coin win → ongoing ×MultValue income multiplier;
                        // coin loss → wipe current money to 0 (one-shot). Reading noted in the report.
                        if (_coin()) _mults.Add(new ActiveMult { Value = e.MultValue, FromAge = e.FromAge });
                        else Money = 0;
                    }
                    else
                    {
                        _mults.Add(new ActiveMult { Value = e.MultValue, FromAge = e.FromAge });
                    }
                }
                else // Drain: starts NOW (on ДА), lasts DurYears game-years
                {
                    _drains.Add(new MoneyDrain
                    {
                        PerSec = e.DrainPerSec,
                        StartAge = Age,
                        EndAge = Age + e.DurYears,
                    });
                }
            }
        }

        // Route a card's money Δ onto the live float (the authoritative money); non-money deltas go to Scales.
        private void ApplyCardDeltas(IReadOnlyList<ScaleDelta> deltas)
        {
            if (deltas == null) return;
            List<ScaleDelta> nonMoney = null;
            foreach (var d in deltas)
            {
                if (d.Scale == Scale.Money)
                {
                    switch (d.Kind)
                    {
                        case DeltaKind.Add: Money += d.Value; break;
                        case DeltaKind.RandomPlusMinus: Money += _coin() ? d.Value : -d.Value; break;
                        case DeltaKind.Set: Money = d.Value; break;
                    }
                }
                else
                {
                    (nonMoney ??= new List<ScaleDelta>()).Add(d);
                }
            }
            if (nonMoney != null) Scales.Apply(nonMoney, _coin);
        }
    }
}
