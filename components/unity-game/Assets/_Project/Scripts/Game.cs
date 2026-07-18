using System;
using System.Collections.Generic;
using System.Linq;

namespace ThanksNoThanks
{
    public enum GameState { Opener, Playing, Finale }

    /// <summary>
    /// Which way a card resolved — the semantic input to the host's speech-bubble tone.
    /// <see cref="Timeout"/> means the 5-second timer ran out (the mechanical answer was still a coin
    /// flip, but the host reacts to the silence, not the random pick).
    /// </summary>
    public enum AnswerSide { Yes, No, Timeout }

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
            { "LT08", 100 },  // подлечиться (система, condition-triggered)
        };

        // ---- live health (tunable; canon §Здоровье + §Сводка констант) ----
        public const int HealthDecayFromAge = 30;         // до 30 не убывает; с 30 тает
        // Base decay per REAL second while decaying. Canon reference ≈1%/s; tuned to 0.7 so even the
        // worst-case slowest run (every card times out) survives «ничего плохого → доживаешь» — a real
        // player answering faster loses far less. #1 playtest tunable (see report §calibration/tension).
        public const double HealthDecayPerSec = 0.7;
        public const double Lt01NeglectDecayMult = 2.0;   // LT01=НЕТ «забросил» → декей ×2
        public const double Lt01CareDecayMult = 0.5;      // LT01=ДА «занялся» → декей ×0.5
        public const int Kek04HealthBonus = 5;            // KEK04=ДА ЗОЖ-секта → разовый небольшой плюс
        public const int Lt08TriggerAge = 30;             // LT08 «Пора подлечиться!» eligible age ≥30…
        public const int Lt08TriggerHealthBelow = 40;     // …AND when health < 40%
        public const int Lt02EligibleHealthBelow = 50;    // LT02 операция drawable only when health < 50%

        // ---- live energy (tunable; canon §Энергия) ----
        public const int EnergyOpenAge = 25;              // энергия открывается в 25 (YA05)
        // Drain is INTENTIONALLY steeper than health's: energy REQUIRES active breathing (founder
        // Gate-2 r3). Passive → burnout mid-adulthood → «полное выгорание» if ignored. A valid breath
        // cadence (≤1.5s apart, +3% each) recovers ≥2%/s, more than offsetting this — so it's doable,
        // it just costs hand-time vs cranking+answering. #1 energy tunable the founder will feel-tune.
        public const double EnergyDrainPerSec = 1.7;      // дренаж ≈1.7%/сек, пока энергия открыта
        public const int BreathEnergyGain = 3;            // корректный ритм-цикл дыхания → +3%

        // ---- burnout (temporary; canon §Энергия §Выгорание) ----
        public const int BurnoutEnterEnergyAtOrBelow = 10; // входит при энергии ≤10%
        public const int BurnoutExitEnergyAbove = 40;      // снимается сам при энергии >40%
        public const double BurnoutIncomeMult = 0.5;       // крутилка «тяжелеет» — доход ×0.5

        // ---- live relationships balancer (tunable; canon §Отношения) ----
        // The fourth live scale: a balancer to hold inside a zone while cranking/breathing/answering.
        // Opens at 20 (YA03 «первая любовь»), starts at 55% (Scales.Reset), target zone 40–75%.
        public const int RelationshipsOpenAge = 20;        // балансир открывается в 20 (YA03, OPEN:Отн)
        public const int RelZoneMin = 40;                  // ниже — риск разрыва
        public const int RelZoneMax = 75;                  // выше — «красная зона» (задушил вниманием)
        public const double RelDriftPerSec = 0.6;          // дрейф вниз ≈0.6%/сек, пока балансир открыт
        public const double RelDriftMarriedPerSec = 0.3;   // в браке (MD01=ДА) мягче — вдвое медленнее
        public const double RelBalancerPerSec = 1.5;       // RELATION_AXIS ↑/↓ тянет маркер ≈1.5%/сек
        public const double RelOverloadPenaltyPerSec = 0.3;// >75% — доп. штраф вниз (риск ссоры)
        public const float RelBreakupSeconds = 10f;        // суммарно ~10 сек ниже зоны → разрыв
        public const int RelBreakupValue = 20;             // после разрыва шкала падает сюда (одиноко)

        // ---- live child (signal-response tamagotchi button; tunable; canon §Ребёнок) ----
        // Opens on MD02=ДА (which the deck places at «свадьба +2», gated CHAIN→MD01=ДА, so it can only
        // resolve ≥2 game-years after the wedding). Signal-response: the button FLASHES on a random
        // interval; a CHILD_PRESS inside the open window is good parenting; missing 2+ flashes in a row is
        // «плохой родитель» (relationships −10% + child scale drop). NO death — it only bends relationships
        // (which fold into the show tone/brightness) and the child scale. Numbers are the #1 feel-tunables.
        public const float ChildFlashIntervalMin = 15f;   // вспышка каждые ~15–25с (нижняя граница)
        public const float ChildFlashIntervalMax = 25f;   // …верхняя граница (seeded/injectable roll)
        public const float ChildFlashWindow = 2f;         // окно нажатия ~2с, пока кнопка горит
        public const float ChildPressMinDelay = 1f;       // мин. задержка ~1с: пре-нажатие в этом окне
                                                           // перед вспышкой блокирует засчёт (анти-заспам)
        public const int ChildBadParentMisses = 2;        // пропуск 2+ вспышек подряд → «плохой родитель»
        public const int ChildBadParentRelPenalty = 10;   // …отношения −10% (разово за такой промах-лапс)
        public const int ChildBadParentScaleDrop = 12;    // …и шкала ребёнка проседает
        public const int ChildPressGain = 4;              // успел по вспышке → шкала ребёнка чуть вверх
        public const int ChildStartValue = 70;            // шкала ребёнка на открытии (MD02=ДА)

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

        // ---- live health / energy (dt-injected integration, like money; the int scale in Scales stays
        //      authoritative for card Δ, this layer decays/drains it in real time via a fractional
        //      accumulator so sub-1%-per-second steps integrate exactly and legacy Δ tests are untouched) ----
        private double _healthDecayFrac;    // accumulated fractional health decay pending a whole −1
        private double _energyDrainFrac;    // accumulated fractional energy drain pending a whole −1
        private double _healthDecayMult = 1.0; // set by LT01 (×2 забросил / ×0.5 занялся)
        private Card _lt08;                  // condition-triggered system heal card (from DeckPlan)
        private bool _lt08Triggered;         // single-shot per life

        /// <summary>True once the energy scale has opened (Age ≥ <see cref="EnergyOpenAge"/>). One-shot per life.</summary>
        public bool EnergyOpen { get; private set; }
        /// <summary>True once health has begun decaying (Age ≥ <see cref="HealthDecayFromAge"/>). One-shot per life.</summary>
        public bool HealthDecaying { get; private set; }
        /// <summary>Temporary «выгорание»: entered at energy ≤10%, exits above 40%. Halves crank income while on.</summary>
        public bool Burnout { get; private set; }

        // ---- live relationships balancer state ----
        private int _relAxis;                 // RELATION_AXIS for THIS tick: -1/0/+1, consumed each tick
        private double _relFrac;              // fractional accumulator (sub-1%/s drift/pull integrates exact)
        private double _relBelowZoneSeconds;  // CUMULATIVE time spent below the zone floor (canon «суммарно»)

        /// <summary>True while the relationships balancer is live (open at 20 via YA03; closed again on a
        /// breakup). Drives the balancer HUD reveal and the drift/axis/breakup integration.</summary>
        public bool RelationshipsOpen { get; private set; }
        /// <summary>True once <see cref="Answer"/> resolves MD01=ДА (свадьба) — softens the drift (canon:
        /// «реже балансировать»). Cleared by a breakup and on a fresh life.</summary>
        public bool Married { get; private set; }
        /// <summary>True once a breakup has fired this life (partner gone). Keeps the balancer closed (the
        /// MD06 second chance that would reopen it is deferred) and folds the loss into the show tone.</summary>
        public bool RelationshipsLost { get; private set; }
        /// <summary>«Красная зона»: relationships above <see cref="RelZoneMax"/> while open — задушил
        /// вниманием, penalty pulling back down. Read by the driver for the red-zone HUD tint.</summary>
        public bool RelationshipRedZone => RelationshipsOpen && Scales.Relationships > RelZoneMax;

        // ---- live child (signal-response) state ----
        private readonly Random _childRng = new();  // engine-free RNG for the flash interval roll
        private float _childFlashElapsed;            // time since the last window closed (counts toward next flash)
        private float _childNextInterval;            // rolled length of the current wait (~15–25s)
        private float _childWindowRemaining;         // time left in the OPEN press-window (>0 while lit)
        private float _childPressLockout;            // anti-pre-spam: while >0 an in-window press is ignored
        private int _childConsecutiveMiss;           // flashes missed in a row (2+ → «плохой родитель»)

        /// <summary>Test/tuning seam: supplies the NEXT flash interval in seconds. null → a uniform draw in
        /// [<see cref="ChildFlashIntervalMin"/>,<see cref="ChildFlashIntervalMax"/>] from the internal RNG.
        /// Survives <see cref="StartLife"/> so a deterministic test can pin every interval.</summary>
        public Func<float> ChildFlashInterval;

        /// <summary>True while the child scale/button is live: opened by MD02=ДА, closed by LT04 or a fresh
        /// life. Drives the child-button HUD reveal and the flash/press integration.</summary>
        public bool ChildOpen { get; private set; }
        /// <summary>True while the flash window is OPEN (button lit) — a CHILD_PRESS now is good parenting.
        /// Read by the driver for the lit/flashing button state (S9).</summary>
        public bool ChildFlashing { get; private set; }

        /// <summary>Fired the instant the child scale opens (MD02=ДА, «свадьба+2») — drives the S5
        /// «ПОПОЛНЕНИЕ! жмите Enter по вспышке» hint + pause, one-shot per life.</summary>
        public event Action ChildOpened;

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

        /// <summary>
        /// True when the CURRENT card is a BLOCK$ card with a known price. Drives the on-card price line
        /// (shown both affordable and blocked) — read-only view onto the single source <see cref="BlockPrices"/>.
        /// </summary>
        public bool CurrentCardHasPrice =>
            CurrentCard != null && CurrentCard.IsBlockCost && BlockPrices.ContainsKey(CurrentCard.Id);

        /// <summary>
        /// The current BLOCK$ card's price in ₽ — the ONE number used for the affordability gate, the ДА
        /// spend, and the on-card display. 0 when the current card carries no BLOCK$ price.
        /// </summary>
        public double CurrentCardPrice =>
            CurrentCardHasPrice ? BlockPrices[CurrentCard.Id] : 0;

        /// <summary>
        /// Current income multiplier (product of active FROM-gated multipliers). ≥ 1 unless wiped.
        /// While <see cref="Burnout"/> is active the crank «тяжелеет» — the product is halved
        /// (<see cref="BurnoutIncomeMult"/>), folded in here so every income path pays the same.
        /// </summary>
        public double IncomeMultiplier
        {
            get
            {
                double m = 1.0;
                foreach (var a in _mults)
                    if (a.FromAge == 0 || Age >= a.FromAge) m *= a.Value;
                if (Burnout) m *= BurnoutIncomeMult;
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
        /// <summary>
        /// Fired the instant a card resolves — with the card and the chosen <see cref="AnswerSide"/> —
        /// BEFORE the next card is drawn, so the driver's host bubble reacts to THIS choice. Semantic
        /// only: the bubble text and its RNG live in the driver's HostVoice, never here. NOT fired for a
        /// BLOCK$-blocked card (that is skipped, not answered).
        /// </summary>
        public event Action<Card, AnswerSide> AnswerResolved;
        /// <summary>Fired the first time money opens in a life (drives the S5 tutorial overlay + pause).</summary>
        public event Action MoneyOpened;
        /// <summary>Fired the first time energy opens (Age 25) — drives the S5 «дыхание» hint + pause.</summary>
        public event Action EnergyOpened;
        /// <summary>Fired the first time health starts decaying (Age 30) — drives the S5 health hint + pause.</summary>
        public event Action HealthOpened;
        /// <summary>Fired the first time the relationships balancer opens (Age 20, YA03) — drives the S5
        /// «держите отношения в зоне — ↑/↓» hint + pause.</summary>
        public event Action RelationshipsOpened;
        /// <summary>Fired the instant a breakup resolves (relationships spent ~10s cumulative below the
        /// zone floor): partner gone, balancer closed. NO death — drives the transient «РАССТАЛИСЬ» plate.</summary>
        public event Action RelationshipBrokeUp;
        /// <summary>
        /// Fired whenever burnout is entered (energy ≤10%). The driver shows the brief S5 hint only on
        /// the FIRST time per life (one-shot) and drives the S7 state plate off <see cref="Burnout"/>.
        /// </summary>
        public event Action BurnoutEntered;

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
            _lt08 = plan?.Lt08;   // condition-triggered system heal card for this life (may be null)
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
                    else if (input == GameInput.EnergyPulse) Breathe(); // already rhythm-validated by the driver
                    else if (input == GameInput.RelationUp) SetRelationAxis(+1);
                    else if (input == GameInput.RelationDown) SetRelationAxis(-1);
                    else if (input == GameInput.ChildPress) ChildPress();
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
            ResetHealthEnergy();
            ResetRelationships();
            ResetChild();
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
                    // NO open-checks here (deliberate): nothing NEW opens while coasting to the
                    // reckoning — an S5 hint pausing the death coast would be absurd. Scales already
                    // open keep draining below; an unopened scale has no drain to compete with anyway.
                    IntegrateMoney(dt);
                    IntegrateHealth(dt);               // live drains keep running while coasting —
                    IntegrateEnergy(dt);               // the reckoning doesn't freeze your body
                    // Deaths compete by first threshold crossed. TIE-BREAK (documented): when a scale
                    // hits zero within the SAME tick the scheduled age is reached, the scale death wins —
                    // it «happened» during the coast, before the knock on the door.
                    if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
                    if (EnergyOpen && Scales.EnergyDepleted) { End("полное выгорание"); return; }
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

                if (CheckMoneyOpen()) return;      // open money (18) → tutorial pause may freeze this frame
                if (CheckRelationshipsOpen()) return; // open relationships balancer (20) → hint + pause
                if (CheckEnergyOpen()) return;     // open energy (25) → «дыхание» hint + pause
                if (CheckHealthDecayOpen()) return;// health starts decaying (30) → hint + pause
                if (CheckScheduledFatal()) return; // «за вами пришли» once age crosses card.Age+n
            }

            IntegrateMoney(dt);                    // cost-of-living + installment drains (real-time)
            IntegrateHealth(dt);                   // decay from 30 (×LT01 modifier), real-time
            IntegrateEnergy(dt);                   // drain from 25 + burnout enter/exit, real-time
            IntegrateRelationships(dt);            // drift + RELATION_AXIS + breakup (no death), real-time
            IntegrateChild(dt);                    // flash scheduler + missed-flash bad-parent penalty (no death)

            if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
            if (EnergyOpen && Scales.EnergyDepleted) { End("полное выгорание"); return; }
            MaybeTriggerLt08();                    // «Пора подлечиться!» when health<40% & age≥30

            CardTimer -= dt;
            if (CardTimer <= 0f)
                Answer(_coin(), timeout: true);   // не успел — берём ДА или НЕТ случайно (тон — «пропуск»)
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

        // Opens the energy scale the first time Age reaches 25 (YA05 «первая усталость»). Fires the
        // «дыхание» hint; energy starts draining from here. Returns true if a listener paused the frame.
        private bool CheckEnergyOpen()
        {
            if (EnergyOpen || Age < EnergyOpenAge) return false;
            EnergyOpen = true;
            EnergyOpened?.Invoke();
            return Paused;
        }

        // Health starts decaying at 30 (canon: до 30 не убывает). Fires the health hint. Returns true
        // if a listener paused the frame.
        private bool CheckHealthDecayOpen()
        {
            if (HealthDecaying || Age < HealthDecayFromAge) return false;
            HealthDecaying = true;
            HealthOpened?.Invoke();
            return Paused;
        }

        // The relationships balancer opens the first time Age reaches 20 (YA03 «первая любовь», OPEN:Отн):
        // starts at 55% (already restored by Scales.Reset), drift + axis + breakup begin from here. Fires
        // the S5 «держите отношения в зоне» hint. Once a breakup has closed it (RelationshipsLost) the
        // age gate does NOT reopen it — the MD06 second chance that would is deferred. Returns true if a
        // listener paused the frame (parallels money/energy/health opens).
        private bool CheckRelationshipsOpen()
        {
            if (RelationshipsOpen || RelationshipsLost || Age < RelationshipsOpenAge) return false;
            RelationshipsOpen = true;
            RelationshipsOpened?.Invoke();
            return Paused;
        }

        // A CHAIN child is drawable only after its parent resolved ДА. Parents always precede their
        // children in age order, so by the time we reach a gated card its parent is already answered.
        private bool GatedOff(Card c)
            => c.RequiresParentYes != null
               && !(_answers.TryGetValue(c.RequiresParentYes, out var yes) && yes);

        // LT02 «Операция» carries its canonical condition «если Здр<50%»: drawable only when health is
        // below the eligibility threshold at draw time; otherwise skipped/substituted like a chain gate.
        private bool HealthGatedOff(Card c)
            => c.Id == "LT02" && Scales.Health >= Lt02EligibleHealthBelow;

        private void Advance()
        {
            if (CheckScheduledFatal()) return;
            while (true)
            {
                _index++;
                if (_index >= _deck.Count) { EndOfDeck(); return; }
                var c = _deck[_index];
                if (GatedOff(c) || HealthGatedOff(c)) // parent≠ДА, or LT02 while healthy → skip…
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

        private void Answer(bool yes) => Answer(yes, timeout: false);

        private void Answer(bool yes, bool timeout)
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
                // BLOCK$: the price (BlockPrices[id]) is the ONE authoritative money cost — the same number
                // that gates affordability and is shown on the card. So on a BLOCK$ card we DROP the CSV
                // money-Δ (e.g. MD03's tiny «Дн −2» on a different scale) to avoid a double/mis-scaled charge,
                // and instead spend exactly the price on ДА. Health/energy components of the Δ still apply
                // (MD03 keeps «Эн +2», LT02 «Здр +40», LT08 «Здр → 80%»). НЕТ spends nothing. (Reached only
                // when NOT blocked — a blocked BLOCK$ card returned above with no Δ and no spend.)
                bool isBlockCost = card.IsBlockCost;
                ApplyCardDeltas(yes ? card.YesDeltas : card.NoDeltas, skipMoney: isBlockCost); // rest → Scales/Money
                if (isBlockCost && yes && BlockPrices.TryGetValue(card.Id, out var price))
                    Money -= price;                                   // spend exactly the price on ДА
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

            // Card-id specials on the live layer: LT01 sets the health-decay modifier (both answers),
            // KEK04=ДА gives a small one-shot health plus. Runs regardless of NOCONS (KEK04 is NOCONS).
            ApplyCardSpecial(card, yes);

            // Host reaction hook: announce the resolution (card + side) BEFORE the fatal/advance fork,
            // so the bubble reflects THIS choice no matter what happens next. Timeout carries the «skip»
            // tone even though the mechanical pick above was a coin flip.
            AnswerResolved?.Invoke(card, timeout ? AnswerSide.Timeout : yes ? AnswerSide.Yes : AnswerSide.No);

            // Delayed fatal (RND01): ДА schedules «за вами пришли» for card.Age + n, life continues.
            if (yes && card.DelayedFatalYears > 0)
            {
                _scheduledFatalAge = card.Age + card.DelayedFatalYears;
                _scheduledFatalCause = card.FatalCause;
            }

            if (yes && card.YesIsFatal) { End(card.FatalCause); return; }
            if (Scales.HealthDepleted) { End("здоровье не выдержало"); return; }
            if (EnergyOpen && Scales.EnergyDepleted) { End("полное выгорание"); return; }

            MaybeTriggerLt08();   // a health-hit card may drop below 40% → «Пора подлечиться!»
            Advance();
        }

        // LT01 modifier + KEK04 bonus. LT02→80% / LT08→80% heals ride the normal Δ path (Set), so they
        // are NOT duplicated here. LT01 also carries its own ±2 Δ via the normal path; this only sets
        // the ongoing decay multiplier.
        private void ApplyCardSpecial(Card card, bool yes)
        {
            switch (card.Id)
            {
                case "LT01":
                    _healthDecayMult = yes ? Lt01CareDecayMult : Lt01NeglectDecayMult;
                    break;
                case "KEK04":
                    if (yes) HealHealth(Kek04HealthBonus);
                    break;
                case "MD01":
                    // Свадьба (MD01=ДА): relationships enter «реже балансировать» — the drift softens to
                    // RelDriftMarriedPerSec. НЕТ (свобода) leaves the full drift. Only meaningful while the
                    // balancer is open (canon MD01 «если Отн открыта») — never latch married after a
                    // breakup has closed it. Cleared by a breakup.
                    if (yes && RelationshipsOpen) Married = true;
                    break;
                case "MD02":
                    // Ребёнок (MD02=ДА «завести ребёнка», OPEN:Реб): opens the child scale + tamagotchi
                    // button and fires the S5 hint. The deck places MD02 at «свадьба +2» and gates it
                    // CHAIN→MD01=ДА, so this only fires ≥2 game-years after the wedding. НЕТ → nothing opens.
                    if (yes) OpenChild();
                    break;
                case "LT04":
                    // Дети выросли (LT04, «55–65, если MD02=ДА»): the button/scale go dark either way. The
                    // ДА «навязчивая опека» отношения −1 rides the normal CSV Δ (applied before this); НЕТ
                    // «отпустил» carries no Δ. Both just close the child mechanic — never a death.
                    CloseChild();
                    break;
            }
        }

        // Raise health by a whole amount, clamped to 100; card Δ owns the rest (this is only for the
        // KEK04 system bonus, which is otherwise a NOCONS card).
        private void HealHealth(int amount)
        {
            Scales.Health = Math.Min(100, Scales.Health + amount);
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
            ResetHealthEnergy();
            ResetRelationships();
            ResetChild();
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

        // ================================================================ live health / energy

        private void ResetHealthEnergy()
        {
            EnergyOpen = false;
            HealthDecaying = false;
            Burnout = false;
            _healthDecayFrac = 0;
            _energyDrainFrac = 0;
            _healthDecayMult = 1.0;
            _lt08Triggered = false;
            // Scales.Reset() (in StartLife/ToOpener) has already restored Health/Energy = 100.
        }

        // Health decay from 30, per REAL second, scaled by the LT01 modifier. Fractional accumulator so a
        // sub-1%/s rate decrements the int scale exactly on whole crossings (dt-injected — no wall clock).
        private void IntegrateHealth(float dt)
        {
            if (!HealthDecaying || Scales.Health <= 0) return;
            _healthDecayFrac += HealthDecayPerSec * _healthDecayMult * dt;
            int whole = (int)_healthDecayFrac;
            if (whole <= 0) return;
            _healthDecayFrac -= whole;
            Scales.Health = Math.Max(0, Scales.Health - whole);
        }

        // Energy drain from 25 (real-time, fractional accumulator) + burnout enter/exit tracking.
        private void IntegrateEnergy(float dt)
        {
            if (!EnergyOpen) return;
            if (Scales.Energy > 0)
            {
                _energyDrainFrac += EnergyDrainPerSec * dt;
                int whole = (int)_energyDrainFrac;
                if (whole > 0)
                {
                    _energyDrainFrac -= whole;
                    Scales.Energy = Math.Max(0, Scales.Energy - whole);
                }
            }
            UpdateBurnout();
        }

        // ENERGY_PULSE (a rhythm-VALID breath, already filtered by the driver): restore +3%, clamped to
        // 100, and re-evaluate burnout (a good breath can lift you back out). No-op before energy opens.
        private void Breathe()
        {
            if (!EnergyOpen || Paused) return;
            Scales.Energy = Math.Min(100, Scales.Energy + BreathEnergyGain);
            UpdateBurnout();
        }

        // Temporary «выгорание»: latch on at energy ≤10%, release above 40% (hysteresis, re-enterable).
        // Entering fires an event; the driver shows the one-shot hint and the S7 plate off Burnout.
        private void UpdateBurnout()
        {
            if (!Burnout && Scales.Energy <= BurnoutEnterEnergyAtOrBelow)
            {
                Burnout = true;
                BurnoutEntered?.Invoke();
            }
            else if (Burnout && Scales.Energy > BurnoutExitEnergyAbove)
            {
                Burnout = false;
            }
        }

        // ================================================================ live relationships balancer

        private void ResetRelationships()
        {
            RelationshipsOpen = false;
            Married = false;
            RelationshipsLost = false;
            _relAxis = 0;
            _relFrac = 0;
            _relBelowZoneSeconds = 0;
            // Scales.Reset() (in StartLife/ToOpener) has already restored Relationships = 55.
        }

        // RELATION_AXIS ↑/↓: latch the held direction for the NEXT integration tick, which consumes and
        // clears it. No-op unless relationships are open and the run is live/unpaused (inert in the
        // opener/finale/tutorial — HandleInput only routes it in Playing; this adds the open+pause guard).
        private void SetRelationAxis(int dir)
        {
            if (!RelationshipsOpen || Paused) return;
            _relAxis = dir;
        }

        // Relationships balancer integration (real-time, fractional accumulator like health/energy):
        // constant downward drift (softened while married), the held RELATION_AXIS pull (±1.5%/s), an
        // over-attention penalty above the zone, and the CUMULATIVE below-zone breakup timer. There is
        // NO death here — failure is a breakup (partner leaves), not a game over.
        private void IntegrateRelationships(float dt)
        {
            if (!RelationshipsOpen) return;

            double rate = -(Married ? RelDriftMarriedPerSec : RelDriftPerSec); // drift down
            rate += _relAxis * RelBalancerPerSec;                              // held axis (±)
            if (Scales.Relationships > RelZoneMax)                             // задушил вниманием →
                rate -= RelOverloadPenaltyPerSec;                             // extra pull back toward zone
            _relAxis = 0;                                                      // consume this tick's axis

            _relFrac += rate * dt;
            int whole = (int)_relFrac;   // truncates toward zero → symmetric for up and down
            if (whole != 0)
            {
                _relFrac -= whole;
                Scales.Relationships = Math.Max(0, Math.Min(100, Scales.Relationships + whole));
            }

            // Breakup: canon «ниже 40% суммарно ~10 сек» — CUMULATIVE below-zone time (not a continuous
            // streak), so brief repeated dips add up over the life. Never reset except on breakup/restart.
            if (Scales.Relationships < RelZoneMin)
            {
                _relBelowZoneSeconds += dt;
                if (_relBelowZoneSeconds >= RelBreakupSeconds)
                    BreakUp();
            }
        }

        // Partner leaves after too long below the zone. Resets the balancer (closed), clears marriage,
        // drops the scale to a lonely value (folds into the show tone + the natural-ending tone), and
        // records a necrolog line. NOT a death — the run continues, just without a partner.
        private void BreakUp()
        {
            Married = false;
            RelationshipsOpen = false;
            RelationshipsLost = true;
            _relAxis = 0;
            _relFrac = 0;
            _relBelowZoneSeconds = 0;
            Scales.Relationships = RelBreakupValue;

            _entries.Add(new NecrologEntry
            {
                Age = (int)Age,
                Order = int.MaxValue - 1,   // sorts after same-age card lines
                Line = "Отношения не удержали — расстались.",
                IsRond = false,
            });

            RelationshipBrokeUp?.Invoke();
        }

        // ================================================================ live child (signal-response)

        private void ResetChild()
        {
            ChildOpen = false;
            ChildFlashing = false;
            _childFlashElapsed = 0f;
            _childNextInterval = 0f;
            _childWindowRemaining = 0f;
            _childPressLockout = 0f;
            _childConsecutiveMiss = 0;
            // Scales.Reset() (in StartLife/ToOpener) has already restored Child = 0.
        }

        // MD02=ДА: open the child scale + button. Seeds the first flash interval, lifts the child scale to
        // its starting value (overriding the token «Реб +2» the CSV Δ just applied), and fires the S5 hint.
        private void OpenChild()
        {
            if (ChildOpen) return;
            ChildOpen = true;
            ChildFlashing = false;
            _childFlashElapsed = 0f;
            _childWindowRemaining = 0f;
            _childPressLockout = 0f;
            _childConsecutiveMiss = 0;
            _childNextInterval = PickChildInterval();
            if (Scales.Child < ChildStartValue) Scales.Child = ChildStartValue;
            ChildOpened?.Invoke();
        }

        // LT04 (either answer): the children have grown — the button/scale go dark for good this life.
        private void CloseChild()
        {
            ChildOpen = false;
            ChildFlashing = false;
            _childWindowRemaining = 0f;
            _childPressLockout = 0f;
            _childConsecutiveMiss = 0;
        }

        // The next wait between flashes: an injected value (test/tuning) or a uniform draw in [min,max].
        private float PickChildInterval()
        {
            if (ChildFlashInterval != null) return Math.Max(0.01f, ChildFlashInterval());
            return ChildFlashIntervalMin
                   + (float)(_childRng.NextDouble() * (ChildFlashIntervalMax - ChildFlashIntervalMin));
        }

        // Signal-response integration (real-time, dt-injected): count down to the next flash, hold the ~2s
        // open window, and register a MISS when the window closes unpressed. NO death path — a lapse only
        // bends relationships (which fold into the show tone) and the child scale.
        private void IntegrateChild(float dt)
        {
            if (!ChildOpen) return;

            // Anti-pre-spam lockout always winds down (so a press >1s before a flash is harmless again).
            if (_childPressLockout > 0f)
                _childPressLockout = Math.Max(0f, _childPressLockout - dt);

            if (ChildFlashing)
            {
                _childWindowRemaining -= dt;
                if (_childWindowRemaining <= 0f)   // window closed with no valid press → missed flash
                {
                    ChildFlashing = false;
                    RegisterChildMiss();
                    _childFlashElapsed = 0f;
                    _childNextInterval = PickChildInterval();
                }
            }
            else
            {
                _childFlashElapsed += dt;
                if (_childFlashElapsed >= _childNextInterval)   // time to flash → open the press window
                {
                    ChildFlashing = true;
                    _childWindowRemaining = ChildFlashWindow;
                }
            }
        }

        // CHILD_PRESS (Enter, routed by the driver only in gameplay-with-open-child). A press INSIDE the open
        // window (and not inside the anti-pre-spam lockout) is good parenting: child scale up, miss-streak
        // cleared, next flash rescheduled. Any other press — before/after the window, or while locked out —
        // is discarded AND (re)arms the ~1s lockout, so mashing ahead of the flash can't bank a success.
        private void ChildPress()
        {
            if (!ChildOpen || Paused) return;
            if (ChildFlashing && _childPressLockout <= 0f)
            {
                ChildFlashing = false;
                _childConsecutiveMiss = 0;
                Scales.Child = Math.Min(100, Scales.Child + ChildPressGain);
                _childFlashElapsed = 0f;
                _childNextInterval = PickChildInterval();
            }
            else
            {
                _childPressLockout = ChildPressMinDelay;   // premature / locked → punish the pre-spam
            }
        }

        // A missed flash. Two in a row = «плохой родитель»: relationships −10% + child scale drop, applied
        // ONCE per lapse (the streak resets, so it takes two fresh misses to be penalised again).
        private void RegisterChildMiss()
        {
            _childConsecutiveMiss++;
            if (_childConsecutiveMiss < ChildBadParentMisses) return;
            _childConsecutiveMiss = 0;
            Scales.Relationships = Math.Max(0, Scales.Relationships - ChildBadParentRelPenalty);
            Scales.Child = Math.Max(0, Scales.Child - ChildBadParentScaleDrop);
        }

        // «Пора подлечиться!» (LT08): a condition-triggered system card, single-shot per life. Eligible
        // only when health < 40% AND age ≥ 30; inserted into the remaining deck near the current age so
        // it comes up next. It is a BLOCK$ card (100₽) — if broke it shows blocked and skips (canon:
        // «ленился по здоровью — чинить нечем»); if paid, its ДА Δ heals health → 80% via the normal path.
        private void MaybeTriggerLt08()
        {
            if (_lt08Triggered || _lt08 == null) return;
            if (Age < Lt08TriggerAge || Scales.Health >= Lt08TriggerHealthBelow) return;
            _lt08Triggered = true;

            var card = _lt08;
            card.Age = Math.Max(Lt08TriggerAge, (int)Math.Ceiling(Age));
            int at = _deck.FindIndex(_index + 1, c => c.Age > card.Age);
            if (at < 0) at = _deck.Count;
            _deck.Insert(at, card);
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
        // <paramref name="skipMoney"/> = true drops the money component entirely (BLOCK$ cards: the price is
        // the authoritative money cost, so the CSV money-Δ must not also charge). Non-money deltas still apply.
        private void ApplyCardDeltas(IReadOnlyList<ScaleDelta> deltas, bool skipMoney = false)
        {
            if (deltas == null) return;
            List<ScaleDelta> nonMoney = null;
            foreach (var d in deltas)
            {
                if (d.Scale == Scale.Money)
                {
                    if (skipMoney) continue;   // BLOCK$: price owns the money cost — ignore CSV money-Δ
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
