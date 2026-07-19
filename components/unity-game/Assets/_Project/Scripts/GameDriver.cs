using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ThanksNoThanks
{
    /// <summary>
    /// MonoBehaviour driver for «Спасибо, не надо». Owns the pure <see cref="Game"/>, wires an
    /// <see cref="IInputSource"/> (keyboard by default; a fake can be injected for tests), and
    /// self-builds the TV-show HUD (16:9, 1920×1080) in Awake from the team's P0 sprite set so the
    /// scene needs no fragile hand-wired references. Visual-only layer: gameplay lives in <see cref="Game"/>.
    ///
    /// Assets are loaded from <c>Assets/_Project/Art/Resources</c> (a Resources root nested under Art):
    /// sprites at <c>Sprites/*</c>, fonts at <c>Fonts/*</c>. Fonts are legacy uGUI dynamic fonts —
    /// Russo One for display headlines, Rubik for the money pill (₽) and body copy.
    /// </summary>
    public sealed class GameDriver : MonoBehaviour
    {
        // ---- palette tokens (#kit) — used for text only; sprites carry their own colour ----
        private static readonly Color Cobalt = new(0.184f, 0.329f, 0.784f);    // #2f54c8
        private static readonly Color CobaltDeep = new(0.122f, 0.227f, 0.588f);// #1f3a96
        private static readonly Color Ink = new(0.078f, 0.102f, 0.239f);       // #141a3d
        private static readonly Color TextLight = new(0.918f, 0.941f, 1f);     // #eaf0ff
        private static readonly Color Bulb = new(1f, 0.847f, 0.451f);          // #ffd873
        private static readonly Color Energy = new(0.973f, 0.824f, 0.271f);    // #f8d24c
        private static readonly Color Muted = new(0.62f, 0.69f, 0.91f);        // #9fb0e8
        private static readonly Color TimerRed = new(0.910f, 0.267f, 0.227f);  // #e8443a
        private static readonly Color TimerHot = new(1f, 0.32f, 0.18f);        // low-time shift

        // ---- age gates (canon opening ages, S5 tutorial) : purely visual reveal ----
        public const int MoneyAge = 18;
        public const int RelationshipsAge = 20;
        public const int EnergyAge = 25;
        public const int HealthAge = 30;

        /// <summary>Optional input injection (tests). Defaults to a KeyboardInputSource in Start.</summary>
        public IInputSource Input;

        private Game _game;
        public Game Game => _game;

        private Font _display; // Russo One
        private Font _body;    // Rubik (has ₽ + Cyrillic)

        // Panels
        private GameObject _openerPanel;
        private GameObject _gamePanel;
        private GameObject _finalePanel;

        // Shared background
        private Image _bg;

        // HUD widgets (gated by age)
        private GameObject _ageBadge;
        private GameObject _moneyPill;
        private GameObject _healthGroup;
        private GameObject _energyGroup;
        private GameObject _balancerGroup;

        private Text _ageText;
        private Text _moneyText;
        private Image _healthFill;
        private Image _energyFill;
        private RectTransform _balancerMarker;
        private Image _balancerTrackImg;   // tinted red in the >75% «красная зона»
        private Image _balancerMarkerImg;  // tinted red in the red zone too
        private float _balancerTrackWidth;

        // ---- child button (opens on MD02=ДА, not age-gated; flashes on the signal-response window) ----
        private GameObject _childGroup;    // whole widget; shown while Game.ChildOpen, hidden after LT04
        private Image _childButtonImg;     // the «lit» bulb — bright while ChildFlashing, dim otherwise
        private Image _childScaleFill;     // compact child-scale readout (шкала ребёнка)

        // Card
        private RectTransform _cardRoot;
        private Image _cardFrame;
        private Text _cardText;

        // Answer plates
        private Image _yesPlate;
        private Image _noPlate;
        private RectTransform _yesRect;
        private RectTransform _noRect;
        private Text _yesPlateText;   // relabelled during the crisis blitz («ВСЁ НОРМАЛЬНО» / «О НЕТ»)
        private Text _noPlateText;
        private const float YesTilt = -2f;
        private const float NoTilt = 2f;

        // ---- midlife crisis HUD (S6 blitz / S13 impulse); built hidden, shown only while Game.InCrisis ----
        private GameObject _crisisInfo;       // top readout: «МЫСЛЬ N/5 · ПРОВАЛОВ: K» / «ИМПУЛЬС N/3»
        private Text _crisisInfoText;
        private GameObject _impulseWarning;   // S13 INVERT plate: «МОЛЧАНИЕ = ДА! · ЖМИ СПАСИБО НЕ НАДО →»
        private bool _crisisUiActive;         // true while the plates/timer are in crisis mode (for restore)

        // Timer ring
        private Image _timerFill;
        private Text _timerText;

        // Finale
        private Text _finaleTitle;
        private Text _finaleCause;
        private Text _finaleStory;

        // BLOCK$ (S10): dim veil over the card + red block-tag banner.
        private GameObject _blockVeil;
        private GameObject _blockBanner;
        // BLOCK$ price sub-line on the card: «СТОИТ N ₽» when affordable, «НУЖНО N ₽» when blocked.
        // Above the veil (drawn after it), so it stays legible in the dimmed/blocked state too.
        private Text _cardPriceText;

        // Tutorial overlay (S5): dimmed bg + yellow modal + «ПОНЯТНО»; freezes the game while up.
        // Reused for every hint: money (18), energy (25), health (30) and the first burnout.
        private GameObject _tutorialOverlay;
        private Text _tutorialText;
        private bool _tutorialShowing;
        // Same-frame swallow guard: in one input poll the source yields Confirm BEFORE MoneyTick/E, so an
        // Enter+Space chord could dismiss a hint then leak the later same-frame crank into gameplay. When
        // a Confirm dismisses a hint we arm this; the rest of THIS frame's non-Confirm input is swallowed.
        // Reset at the top of Update so the next frame behaves normally.
        private bool _dismissedThisFrame;
        private bool _moneyTutorialSeen;   // one-shot per life; reset on a fresh life
        private bool _relTutorialSeen;
        private bool _energyTutorialSeen;
        private bool _healthTutorialSeen;
        private bool _burnoutHintSeen;
        private bool _childTutorialSeen;
        private bool _wasPlaying;

        // Burnout state plate (S7): dim-cobalt «ВЫГОРАНИЕ» banner, shown while Game.Burnout is on.
        private GameObject _burnoutPlate;

        // Breakup notice: a transient red «РАССТАЛИСЬ» plate, shown ~2s when Game fires RelationshipBrokeUp.
        private GameObject _breakupPlate;
        private readonly TimedReveal _breakupTimer = new(2f);

        // Show-reaction brightness veil: full-screen dark Image whose alpha lerps with overall state
        // (ShowMood), «шоу тускнеет» as health+energy sag. Above the panels, below the tutorial overlay.
        private Image _brightness;
        private float _brightnessAlpha;

        // ---- depression «тёмная полоса» (S8): full-screen B&W wash + grain, dim centre pulse, progress pips.
        // Built hidden; shown only while Game.InDepression. The veil alpha steps with Game.DepressionGray so
        // colour returns a step per catch; the grain is a static seeded-noise overlay; the pulse indicator
        // reveals only on the ~0.6s hit-window. Above the brightness veil, below the tutorial overlay.
        private GameObject _depressionGroup;
        private Image _depressionVeil;     // near-opaque gray wash — alpha = DepressionGray/5 · max
        private Image _depressionGrain;    // faint static noise (runtime-seeded texture)
        private Image _depressionPulse;    // faint centre dot, visible only while DepressionPulsing
        private Image[] _depPips;          // 5-step colour-progress readout
        private int _depMutterCount;       // muttering index (one muted host line per catch)
        // Depression colour tokens.
        private static readonly Color GrayWash = new(0.50f, 0.50f, 0.53f);   // the B&W wash tint

        // Breath rhythm validator (E in a calm cadence → valid pulse → +energy). Clock advanced in Update.
        private readonly BreathRhythm _breath = new();

        // ---- Host (Ведущий): speech bubble (S3) + rubric banner (S4) ----
        // Tunables (defaults; noted in the report). Both clocks are injected real-time via Update.
        public const float BubbleSeconds = 2f;   // speech bubble auto-hide (~2s)
        public const float BannerSeconds = 1.5f; // rubric banner brief announce (~1.5s), then it clears

        // The banner is a NON-blocking announcement band: it does NOT pause Game and does NOT swallow
        // input (so it can never soft-lock, and the direct-Tick test loops keep flowing). It auto-hides
        // on its own ~1.5s clock AND is replaced/cleared the moment the next card is drawn.
        private HostVoice _voice;
        private readonly TimedReveal _bubbleTimer = new(BubbleSeconds);
        private readonly TimedReveal _bannerTimer = new(BannerSeconds);

        private GameObject _hostBubble;   // yellow bubble.png (9-slice), S3 corner
        private Text _bubbleText;
        private GameObject _bannerRoot;   // rubric band (S4), over the card's upper area
        private Image _bannerBand;
        private Text _bannerText;

        private Coroutine _cardAnim;
        private Coroutine _moneyPulse;

        // Income cap (~5/s): applied ONLY on the gameplay-crank branch in OnInput. Space does nothing
        // outside Playing (crank-only), so the cap never interacts with any confirm.
        private readonly MoneyTickThrottle _crankCap = new();

        private const string MoneyTutorialText =
            "ТЕПЕРЬ У ВАС ЕСТЬ РАБОТА!\n\n" +
            "Крутите ПРОБЕЛ — и деньги потекут. Но жизнь идёт своим чередом:\n" +
            "содержать себя стоит денег каждую секунду.\n\n" +
            "Рук всего две — крутить и отвечать придётся разом.";

        // NOTE: must NOT contain «УСТАЛОСТЬ» or «ТАЯТЬ» — the PlayMode hint-walker tests key off those
        // substrings to identify the energy/health hints; a collision would misidentify this one.
        private const string RelationshipsTutorialText =
            "ПЕРВАЯ ЛЮБОВЬ!\n\n" +
            "Появился БАЛАНСИР ОТНОШЕНИЙ — и он всё время сползает вниз.\n" +
            "Держите маркер в зоне: ↑ тянет вверх, ↓ вниз.\n\n" +
            "Упустите надолго — расстанетесь. Переусердствуете — ссоры.";

        private const string EnergyTutorialText =
            "ПЕРВАЯ УСТАЛОСТЬ!\n\n" +
            "Появилась ЭНЕРГИЯ — и она тает сама собой.\n" +
            "Дышите РИТМИЧНО: жмите E в спокойном темпе, не долбите.\n\n" +
            "Ровное дыхание возвращает силы.";

        private const string HealthTutorialText =
            "ЗДОРОВЬЕ НАЧАЛО ТАЯТЬ.\n\n" +
            "С этого возраста ЗДОРОВЬЕ убывает само по себе.\n" +
            "Лечиться можно за деньги — если накопили.\n\n" +
            "Запустите — организм не выдержит.";

        private const string BurnoutHintText =
            "ВЫГОРАНИЕ!\n\n" +
            "Всё даётся тяжелее — деньги идут вдвое медленнее.\n" +
            "Подышите (E), чтобы прийти в себя.\n\n" +
            "Отпустит само, когда энергия восстановится.";

        // Child S5 hint. Must NOT contain «УСТАЛОСТЬ»/«ТАЯТЬ»/«ОТНОШЕНИЙ» — the PlayMode hint-walkers key
        // off those substrings to identify the energy/health/relationships hints; a collision misids this.
        private const string ChildTutorialText =
            "ПОПОЛНЕНИЕ!\n\n" +
            "Появился РЕБЁНОК — кнопка на пульте загорается время от времени.\n" +
            "Жмите Enter, пока она горит — по вспышке.\n\n" +
            "Пропустите подряд — станете плохим родителем.";

        // ---- public inspection accessors (visual-assembly PlayMode tests) ----
        public RectTransform CanvasRect { get; private set; }
        public Image BackgroundImage => _bg;
        public Image CardFrameImage => _cardFrame;
        public RectTransform CardRect => _cardRoot;
        public Image YesPlateImage => _yesPlate;
        public Image NoPlateImage => _noPlate;
        public GameObject AgeBadge => _ageBadge;
        public GameObject MoneyPill => _moneyPill;
        public GameObject HealthGroup => _healthGroup;
        public GameObject EnergyGroup => _energyGroup;
        public GameObject BalancerGroup => _balancerGroup;
        public Image TimerRingFill => _timerFill;
        public GameObject OpenerPanel => _openerPanel;
        public GameObject GamePanel => _gamePanel;
        public GameObject FinalePanel => _finalePanel;
        public GameObject TutorialOverlay => _tutorialOverlay;
        public bool TutorialShowing => _tutorialShowing;
        public GameObject BlockBanner => _blockBanner;
        public Text CardPriceText => _cardPriceText;
        public GameObject BurnoutPlate => _burnoutPlate;
        public GameObject BreakupPlate => _breakupPlate;
        public GameObject BalancerMarker => _balancerMarker != null ? _balancerMarker.gameObject : null;
        public GameObject ChildGroup => _childGroup;
        public Image ChildButtonImage => _childButtonImg;
        public Image BrightnessVeil => _brightness;
        public Text TutorialText => _tutorialText;
        public GameObject HostBubble => _hostBubble;
        public Text HostBubbleText => _bubbleText;
        public GameObject CrisisInfo => _crisisInfo;
        public Text CrisisInfoText => _crisisInfoText;
        public GameObject ImpulseWarning => _impulseWarning;
        public Text YesPlateText => _yesPlateText;
        public Text NoPlateText => _noPlateText;
        public GameObject HostBanner => _bannerRoot;
        public Text HostBannerText => _bannerText;
        public bool HostBubbleVisible => _bubbleTimer.Visible;
        public bool HostBannerVisible => _bannerTimer.Visible;
        public GameObject DepressionOverlay => _depressionGroup;
        public Image DepressionVeil => _depressionVeil;
        public Image DepressionPulseIndicator => _depressionPulse;

        /// <summary>Test hook: run the age-gated HUD visibility for an arbitrary age.</summary>
        public void DebugApplyAgeGates(float age) => ApplyAgeGates(age);

        private void Awake()
        {
            _display = Resources.Load<Font>("Fonts/RussoOne");
            _body = Resources.Load<Font>("Fonts/Rubik");
            if (_display == null) _display = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_body == null) _body = _display;
            _voice = new HostVoice(new System.Random().NextDouble);   // named-line priority + seeded pool
            BuildHud();
            LoadGame();
        }

        private void Start()
        {
            Input ??= gameObject.AddComponent<KeyboardInputSource>();
            Input.Received += OnInput;
            Input.Received += OnInputFx;
            SubscribeGame();
            Refresh();
        }

        private void OnDestroy()
        {
            if (Input != null)
            {
                Input.Received -= OnInput;
                Input.Received -= OnInputFx;
            }
            if (_game != null) UnsubscribeGame();
        }

        private void SubscribeGame()
        {
            _game.StateChanged += Refresh;
            _game.CardChanged += OnCardChanged;
            _game.AnswerResolved += OnAnswerResolved;
            _game.MoneyOpened += OnMoneyOpened;
            _game.RelationshipsOpened += OnRelationshipsOpened;
            _game.EnergyOpened += OnEnergyOpened;
            _game.HealthOpened += OnHealthOpened;
            _game.BurnoutEntered += OnBurnoutEntered;
            _game.RelationshipBrokeUp += OnRelationshipBrokeUp;
            _game.ChildOpened += OnChildOpened;
            _game.CrisisStarted += OnCrisisStarted;
            _game.CrisisBlitzAdvanced += OnCrisisBlitzAdvanced;
            _game.CrisisImpulseStarted += OnCrisisImpulseStarted;
            _game.DepressionStarted += OnDepressionStarted;
            _game.DepressionProgressed += OnDepressionProgressed;
        }

        private void UnsubscribeGame()
        {
            _game.StateChanged -= Refresh;
            _game.CardChanged -= OnCardChanged;
            _game.AnswerResolved -= OnAnswerResolved;
            _game.MoneyOpened -= OnMoneyOpened;
            _game.RelationshipsOpened -= OnRelationshipsOpened;
            _game.EnergyOpened -= OnEnergyOpened;
            _game.HealthOpened -= OnHealthOpened;
            _game.BurnoutEntered -= OnBurnoutEntered;
            _game.RelationshipBrokeUp -= OnRelationshipBrokeUp;
            _game.ChildOpened -= OnChildOpened;
            _game.CrisisStarted -= OnCrisisStarted;
            _game.CrisisBlitzAdvanced -= OnCrisisBlitzAdvanced;
            _game.CrisisImpulseStarted -= OnCrisisImpulseStarted;
            _game.DepressionStarted -= OnDepressionStarted;
            _game.DepressionProgressed -= OnDepressionProgressed;
        }

        /// <summary>
        /// Test seam: swap in a purpose-built <see cref="Game"/> (e.g. a deterministic deck with a
        /// BLOCK$ card) and rewire the HUD to it, so a test can drive a REAL priced card through the
        /// live card flow (Advance → CardChanged → OnCardChanged) instead of poking the label directly.
        /// </summary>
        public void DebugReplaceGame(Game game)
        {
            if (_game != null) UnsubscribeGame();
            _game = game;
            SubscribeGame();
            Refresh();
        }

        /// <summary>
        /// Input funnel. Space (MONEY_TICK / repeat) is the money crank and ONLY that: it cranks during
        /// Playing and is fully inert everywhere else — opener, finale AND tutorial (founder Gate-2:
        /// holding/mashing the crank must never confirm, start, restart, or skip a hint). Enter /
        /// Numpad-Enter (CONFIRM) is the sole key that starts the game, dismisses a hint, and restarts
        /// from the finale. The ~5/s income cap applies only on the gameplay-crank branch. While the
        /// overlay is up, all other input is swallowed. Pure <see cref="Game"/> gets a clean semantic event.
        /// </summary>
        private void OnInput(GameInput input)
        {
            // A hint just closed THIS frame (Confirm dismiss): swallow every later same-frame event so a
            // chorded Enter+Space/E can't leak a crank/pulse onto the frame the overlay closed. Only a
            // further Confirm passes (harmless during Playing). Cleared next frame in Update.
            if (_dismissedThisFrame && input != GameInput.Confirm) return;

            if (_tutorialShowing)
            {
                // FOUNDER DECISION (Gate-2 playtest): hints dismiss on Enter ONLY. She holds/mashes
                // Space for the crank — fresh Space presses AND repeats are both inert here, so a
                // hint can never be skipped unread. (Overrides the earlier «fresh Space dismisses».)
                if (input == GameInput.Confirm) { DismissTutorial(); _dismissedThisFrame = true; }
                return;
            }

            if (input == GameInput.MoneyTick || input == GameInput.MoneyTickRepeat)
            {
                // FOUNDER DECISION (Gate-2 round 2): Space is the money crank and NOTHING else — it must
                // never confirm/start/restart. Outside Playing it is fully inert (she holds Space through
                // the necrolog and it must not skip the payoff screen). This also matches the hardware
                // abstraction: the crank encoder and the CONFIRM button are separate physical controls,
                // so the crank must never fire a confirm. Enter (CONFIRM) is the sole confirm key.
                if (_game.State != GameState.Playing) return;
                if (!_crankCap.TryAccept()) return;         // income cap (anti-mashgun) — gameplay only
                _game.HandleInput(GameInput.MoneyTick);     // Game sees only the semantic crank event
                if (_game.MoneyOpen && isActiveAndEnabled)  // pill pulse on each PAYING tick
                {
                    if (_moneyPulse != null) StopCoroutine(_moneyPulse);
                    _moneyPulse = StartCoroutine(PulseMoney());
                }
                return;
            }

            if (input == GameInput.EnergyPulse)
            {
                // Rhythm gate lives HERE (pure BreathRhythm): Game receives the pulse only on a valid
                // cadence, so mashing / sparse taps never restore energy. Inert outside live gameplay.
                if (_game.State != GameState.Playing) return;
                if (_breath.Pulse()) _game.HandleInput(GameInput.EnergyPulse);
                return;
            }

            // Enter double-duty: during gameplay with the child scale open (and no tutorial up — that case
            // returned above), Enter/CONFIRM means «жать по вспышке» → CHILD_PRESS. Everywhere else it stays
            // CONFIRM (start the game / restart from the finale / dismiss a hint), so the child mechanic
            // never steals those. Game itself only honours the press inside the open flash window. NOT during
            // depression: there CONFIRM is the pulse catch (Game routes it), so the child press must defer.
            if (input == GameInput.Confirm
                && _game.State == GameState.Playing
                && _game.ChildOpen
                && !_game.InDepression)
            {
                _game.HandleInput(GameInput.ChildPress);
                return;
            }

            _game.HandleInput(input);
        }

        private void LoadGame()
        {
            var csvAsset = Resources.Load<TextAsset>("scenes");
            if (csvAsset == null)
            {
                Debug.LogError("[ThanksNoThanks] Resources/scenes.csv not found — deck is empty.");
                _game = new Game(new List<Card>());
                return;
            }

            string csv = csvAsset.text;
            _game = new Game(() => DeckSampler.PlanFromCsv(csv, new System.Random()));
        }

        private void Update()
        {
            _dismissedThisFrame = false;         // fresh frame → the same-frame dismiss-swallow guard clears
            if (_game == null) return;
            _crankCap.Advance(Time.deltaTime);   // deterministic clock for the income cap
            _breath.Advance(Time.deltaTime);     // deterministic clock for the breathing rhythm
            _game.Tick(Time.deltaTime);
            if (_game.State == GameState.Playing && _game.InCrisis)
            {
                RenderCrisis();   // S6 blitz / S13 impulse — reuses the plates + timer ring, freezes normal HUD
            }
            else if (_game.State == GameState.Playing)
            {
                if (_crisisUiActive) RestoreNormalPlates();   // just resumed from a crisis → restore the plates
                _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
                _moneyText.text = FormatMoney(_game.Money);   // live: ticks up on crank, drains down
                // Live health/energy bars + balancer move on their own (decay/drain/breath), not just on cards.
                var s = _game.Scales;
                _healthFill.fillAmount = Mathf.Clamp01(s.Health / 100f);
                _energyFill.fillAmount = Mathf.Clamp01(s.Energy / 100f);
                float rel = Mathf.Clamp01(s.Relationships / 100f);
                _balancerMarker.anchoredPosition = new Vector2((rel - 0.5f) * _balancerTrackWidth, 0f);
                // «Красная зона» (>75%): tint the track + marker red so the over-attention risk reads.
                var relTint = _game.RelationshipRedZone ? TimerRed : Color.white;
                if (_balancerTrackImg.color != relTint) _balancerTrackImg.color = relTint;
                if (_balancerMarkerImg.color != relTint) _balancerMarkerImg.color = relTint;
                if (_burnoutPlate.activeSelf != _game.Burnout) _burnoutPlate.SetActive(_game.Burnout);
                ReflectChildButton();               // reveal on MD02=ДА, light the bulb while flashing
                // Age-gated reveals run every frame (SetActive is a no-op on same value): a widget
                // opening MID-CARD (18/25/30 crossings) appears the moment its age is crossed instead
                // of waiting for the next card resolution (founder Gate-2 bug, uniform fix).
                ApplyAgeGates(_game.Age);

                float remaining = Mathf.Max(0f, _game.CardTimer);
                _timerText.text = Mathf.CeilToInt(remaining).ToString();
                float t = Mathf.Clamp01(remaining / Game.CardSeconds);
                _timerFill.fillAmount = t;
                bool low = remaining <= 1.5f;
                _timerFill.color = low ? TimerHot : TimerRed;
                _timerText.transform.localScale = low
                    ? Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 12f))
                    : Vector3.one;
            }
            ReflectHostReveals(_game.State == GameState.Playing);
            ReflectBreakupPlate(_game.State == GameState.Playing);
            UpdateBrightness();
            ReflectDepression();   // B&W wash + grain + pulse while Game.InDepression (above the show veil)
        }

        // Midlife-crisis render (S6 blitz / S13 impulse). Reuses the card marquee (thought/impulse text),
        // the two answer plates (relabelled), and the timer ring (on the fast crisis clock). The normal HUD
        // (bars/money/balancer) is intentionally frozen — the 5 scales are paused in Game during the crisis.
        private void RenderCrisis()
        {
            _crisisUiActive = true;
            var c = _game.CurrentCard;
            _cardText.text = c != null ? c.Question : "";

            // BLOCK$ visuals never apply during a crisis.
            _blockVeil.SetActive(false);
            _blockBanner.SetActive(false);
            _cardPriceText.gameObject.SetActive(false);

            bool blitz = _game.Phase == CrisisPhase.Blitz;
            if (blitz)
            {
                // «ВСЁ НОРМАЛЬНО» jumps sides each thought; ← = left plate (yes), → = right plate (no).
                bool normalLeft = _game.BlitzNormalOnLeft;
                _yesPlateText.text = normalLeft ? "ВСЁ\nНОРМАЛЬНО" : "О НЕТ";
                _noPlateText.text = normalLeft ? "О НЕТ" : "ВСЁ\nНОРМАЛЬНО";
                _yesPlate.color = Color.white;
                _noPlate.color = Color.white;
                _crisisInfoText.text = $"МЫСЛЬ {_game.BlitzThoughtNumber}/5     ПРОВАЛОВ: {_game.BlitzFails}";
            }
            else
            {
                // Impulse: normal levers work (← = поддаться/ДА, → = «СПАСИБО, НЕ НАДО»). Highlight the → decline.
                _yesPlateText.text = "ПОДДАТЬСЯ";
                _noPlateText.text = "СПАСИБО,\nНЕ НАДО";
                _yesPlate.color = Color.white;
                _noPlate.color = Bulb;   // gold-highlight the safe active decline
                _crisisInfoText.text = $"ИМПУЛЬС {_game.ImpulseCardNumber}/3";
            }
            if (!_crisisInfo.activeSelf) _crisisInfo.SetActive(true);
            if (_impulseWarning.activeSelf == blitz) _impulseWarning.SetActive(!blitz);

            // Fast crisis timer on the ring (2s blitz / 3s impulse).
            float remaining = Mathf.Max(0f, _game.CrisisTimer);
            _timerText.text = Mathf.CeilToInt(remaining).ToString();
            _timerFill.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(0.0001f, _game.CrisisTimerMax));
            _timerFill.color = TimerHot;
            _timerText.transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 14f));
        }

        // Restore the plates + hide the crisis widgets when ordinary play resumes (called once on the
        // first normal frame after a crisis). OnCardChanged (fired by ResumeAfterCrisis) re-renders the card.
        private void RestoreNormalPlates()
        {
            _crisisUiActive = false;
            _yesPlateText.text = "ДА";
            _noPlateText.text = "СПАСИБО,\nНЕ НАДО";
            _yesPlate.color = Color.white;
            _noPlate.color = Color.white;
            if (_crisisInfo != null) _crisisInfo.SetActive(false);
            if (_impulseWarning != null) _impulseWarning.SetActive(false);
        }

        // Crisis entered (CR00): announce «КРИЗИС СРЕДНЕГО ВОЗРАСТА! БЛИЦ!» on the rubric banner.
        private void OnCrisisStarted() => ShowBannerText(HostContent.BannerFor("CR00"), muted: false);

        // Each new blitz thought: shout a hurrying host-nag line in the speech bubble (S3).
        private void OnCrisisBlitzAdvanced() => _bubbleTimer.Show(HostContent.BlitzNagFor(_game.BlitzThoughtNumber));

        // Impulse round opened: the S13 warning plate reveals via RenderCrisis; nothing else needed here.
        private void OnCrisisImpulseStarted() { }

        // Child button: reveal off Game.ChildOpen (not age-gated — opens on MD02=ДА, hides after LT04), and
        // light the bulb bright gold + pulse while the flash window is open (Game.ChildFlashing), dim else.
        // The scale bar tracks Scales.Child so a bad-parent drop reads. NO brightness coupling (canon —
        // child flows into the show tone only via the relationships penalty, already folded in elsewhere).
        private void ReflectChildButton()
        {
            if (_childGroup == null) return;
            if (_childGroup.activeSelf != _game.ChildOpen) _childGroup.SetActive(_game.ChildOpen);
            if (!_game.ChildOpen) return;

            bool lit = _game.ChildFlashing;
            var tint = lit ? Bulb : CobaltDeep;
            if (_childButtonImg.color != tint) _childButtonImg.color = tint;
            _childButtonImg.rectTransform.localScale = lit
                ? Vector3.one * (1f + 0.10f * Mathf.Sin(Time.time * 11f))
                : Vector3.one;
            _childScaleFill.fillAmount = Mathf.Clamp01(_game.Scales.Child / 100f);
        }

        // Transient «РАССТАЛИСЬ» plate: advance its own ~2s clock and mirror visibility (only while
        // Playing). The timer keeps its state, so leaving play simply hides it.
        private void ReflectBreakupPlate(bool playing)
        {
            _breakupTimer.Advance(Time.deltaTime);
            bool show = playing && _breakupTimer.Visible;
            if (_breakupPlate.activeSelf != show) _breakupPlate.SetActive(show);
        }

        // Show-reaction veil: dark alpha follows (health+energy+relationships)/3 via ShowMood, smoothed so
        // it never flickers. Only dims live gameplay; the opener/finale read at full brightness. Until
        // relationships have gone live (open OR lost) they pass as «full» (100) so they never dim early.
        private void UpdateBrightness()
        {
            if (_brightness == null) return;
            bool relCounts = _game.RelationshipsOpen || _game.RelationshipsLost;
            int rel = relCounts ? _game.Scales.Relationships : 100;
            float target = _game.State == GameState.Playing
                ? (float)ShowMood.DarkAlphaFor(_game.Scales.Health, _game.Scales.Energy, rel)
                : 0f;
            _brightnessAlpha = Mathf.MoveTowards(_brightnessAlpha, target, 0.6f * Time.deltaTime);
            var c = _brightness.color;
            _brightness.color = new Color(c.r, c.g, c.b, _brightnessAlpha);
        }

        // ================================================================ HUD build

        private void BuildHud()
        {
            var canvasGo = new GameObject("HUD",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasRect = canvasGo.GetComponent<RectTransform>();

            // Shared sunburst background + static star accents (on all screens, per increment §1).
            _bg = NewSprite("Background", canvasGo.transform, Sprite("sunburst-bg"));
            Stretch(_bg.rectTransform);
            BuildStars(canvasGo.transform);

            BuildOpener(canvasGo.transform);
            BuildGamePanel(canvasGo.transform);
            BuildFinale(canvasGo.transform);

            // Show-reaction veil: above the panels (dims the whole show), below the tutorial overlay.
            _brightness = NewSolid("BrightnessVeil", canvasGo.transform, new Color(0.02f, 0.03f, 0.10f, 0f));
            Stretch(_brightness.rectTransform);

            BuildDepressionOverlay(canvasGo.transform);  // B&W wash + grain + pulse (above the show veil)

            BuildTutorialOverlay(canvasGo.transform);   // top-most: dims every screen when up
        }

        private void BuildStars(Transform parent)
        {
            (Vector2 pos, string sprite, float size)[] stars =
            {
                (new Vector2(0.09f, 0.56f), "star-white",   84f),
                (new Vector2(0.155f, 0.30f), "star-outline", 60f),
                (new Vector2(0.87f, 0.66f), "star-white",   88f),
                (new Vector2(0.905f, 0.38f), "star-outline", 64f),
                (new Vector2(0.80f, 0.84f), "star-white",   54f),
            };
            foreach (var s in stars)
            {
                var star = NewSprite("Star", parent, Sprite(s.sprite));
                Anchor(star.rectTransform, s.pos, new Vector2(s.size, s.size));
            }
        }

        private void BuildOpener(Transform parent)
        {
            _openerPanel = NewGroup("Opener", parent);

            var title = NewText("Title", _openerPanel.transform,
                "«СПАСИБО, НЕ НАДО»", 96, TextAnchor.MiddleCenter, TextLight, _display);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.83f), new Vector2(1600, 160));
            DisplayFx(title);

            // Rules inside a marquee-frame content box (9-slice cobalt panel) for legibility.
            var box = NewSprite("RulesBox", _openerPanel.transform, Sprite("marquee-frame"));
            box.type = Image.Type.Sliced;
            Anchor(box.rectTransform, new Vector2(0.5f, 0.50f), new Vector2(1360, 430));
            var rules = NewText("Rules", box.transform,
                "Проживите целую жизнь за пару минут — в прямом эфире!\n\n" +
                "▸  На каждый вопрос — рычаг: ДА или СПАСИБО, НЕ НАДО.\n" +
                "▸  На раздумья 5 секунд — дальше решаем за вас!\n" +
                "▸  С возрастом откроются ручки жизни. Рук две — всё удержать нельзя.",
                38, TextAnchor.MiddleCenter, TextLight, _body);
            Inset(rules.rectTransform, 90f);

            var start = NewSprite("StartPlate", _openerPanel.transform, Sprite("plate-yes"));
            start.type = Image.Type.Sliced;
            Anchor(start.rectTransform, new Vector2(0.5f, 0.17f), new Vector2(520, 150));
            var startText = NewText("StartText", start.transform,
                "▸ НАЧАТЬ ЖИЗНЬ — Enter", 40, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(startText.rectTransform);

            var hint = NewText("Hint", _openerPanel.transform,
                "←  ДА          →  СПАСИБО, НЕ НАДО          Enter — НАЧАТЬ",
                28, TextAnchor.MiddleCenter, Muted, _body);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.06f), new Vector2(1500, 60));
        }

        private void BuildGamePanel(Transform parent)
        {
            _gamePanel = NewGroup("Game", parent);

            // ---- Age badge (always visible during play) ----
            _ageBadge = NewSprite("AgeBadge", _gamePanel.transform, Sprite("age-badge")).gameObject;
            Anchor(_ageBadge.GetComponent<RectTransform>(), new Vector2(0.06f, 0.85f), new Vector2(160, 175));
            var ageLbl = NewText("AgeLbl", _ageBadge.transform, "ВОЗРАСТ", 18, TextAnchor.MiddleCenter, TextLight, _display);
            Anchor(ageLbl.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(150, 28));
            _ageText = NewText("AgeText", _ageBadge.transform, "0", 60, TextAnchor.MiddleCenter, Color.white, _display);
            Anchor(_ageText.rectTransform, new Vector2(0.5f, 0.38f), new Vector2(150, 92));
            DisplayFx(_ageText);

            // ---- Money pill (age 18+) ----
            _moneyPill = NewSprite("MoneyPill", _gamePanel.transform, Sprite("money-pill")).gameObject;
            var moneyImg = _moneyPill.GetComponent<Image>();
            moneyImg.type = Image.Type.Sliced;
            Anchor(_moneyPill.GetComponent<RectTransform>(), new Vector2(0.215f, 0.875f), new Vector2(360, 150));
            var coin = NewSprite("Coin", _moneyPill.transform, Sprite("icon-coin"));
            Anchor(coin.rectTransform, new Vector2(0.16f, 0.5f), new Vector2(58, 58));
            _moneyText = NewText("MoneyText", _moneyPill.transform, "₽ 0", 34, TextAnchor.MiddleLeft, CobaltDeep, _body);
            Anchor(_moneyText.rectTransform, new Vector2(0.60f, 0.5f), new Vector2(220, 70));

            // ---- Health bar (age 30+) ----
            _healthGroup = BuildBar("HealthGroup", new Vector2(0.375f, 0.885f),
                "ЗДОРОВЬЕ", "icon-heart", "bar-health-fill", out _healthFill);

            // ---- Energy bar (age 25+) ----
            _energyGroup = BuildBar("EnergyGroup", new Vector2(0.545f, 0.885f),
                "ЭНЕРГИЯ", "icon-lightning", "bar-energy-fill", out _energyFill);

            // ---- Relationships balancer (age 20+) ----
            BuildBalancer(new Vector2(0.775f, 0.875f));

            // ---- Child tamagotchi button (opens on MD02=ДА, not age-gated) ----
            BuildChildButton(new Vector2(0.5f, 0.315f));

            // ---- Timer ring (top centre) ----
            var ringGroup = NewGroup("Timer", _gamePanel.transform);
            Anchor(ringGroup.GetComponent<RectTransform>(), new Vector2(0.5f, 0.875f), new Vector2(150, 150));
            var ringTrack = NewSprite("RingTrack", ringGroup.transform, Sprite("timer-ring-track"));
            Stretch(ringTrack.rectTransform);
            _timerFill = NewSprite("RingFill", ringGroup.transform, Sprite("timer-ring"));
            Stretch(_timerFill.rectTransform);
            _timerFill.type = Image.Type.Filled;
            _timerFill.fillMethod = Image.FillMethod.Radial360;
            _timerFill.fillOrigin = (int)Image.Origin360.Top;
            _timerFill.fillClockwise = false;
            _timerFill.fillAmount = 1f;
            _timerFill.color = TimerRed;
            _timerText = NewText("TimerText", ringGroup.transform, "5", 56, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(_timerText.rectTransform);
            DisplayFx(_timerText);

            // ---- Card marquee (centre) ----
            _cardRoot = NewGroup("Card", _gamePanel.transform).GetComponent<RectTransform>();
            Anchor(_cardRoot, new Vector2(0.5f, 0.53f), new Vector2(940, 600));
            _cardFrame = NewSprite("CardFrame", _cardRoot, Sprite("marquee-frame-bulbs"));
            Stretch(_cardFrame.rectTransform);
            _cardText = NewText("CardText", _cardRoot, "", 64, TextAnchor.MiddleCenter, Color.white, _display);
            Inset(_cardText.rectTransform, 130f);
            DisplayFx(_cardText);

            // ---- BLOCK$ (S10): dim veil over the card + red block-tag banner (hidden by default) ----
            _blockVeil = NewSolid("BlockVeil", _cardRoot, new Color(0.02f, 0.03f, 0.10f, 0.62f)).gameObject;
            Stretch(_blockVeil.GetComponent<RectTransform>());
            _blockBanner = NewSolid("BlockBanner", _cardRoot, TimerRed).gameObject;
            Anchor(_blockBanner.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820, 150));
            var blockTxt = NewText("BlockText", _blockBanner.transform,
                "КАК ЖАЛЬ, У ВАС НЕТ\nДЕНЕГ НА ЭТО!", 48, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(blockTxt.rectTransform);
            DisplayFx(blockTxt);
            _blockVeil.SetActive(false);
            _blockBanner.SetActive(false);

            // ---- BLOCK$ price sub-line (S10): the required amount, on any BLOCK$-priced card ----
            // Sits at the bottom of the card, added AFTER the veil so it reads in the blocked state too.
            // «СТОИТ N ₽» (gold) when affordable · «НУЖНО N ₽» (light, next to the banner) when blocked.
            _cardPriceText = NewText("CardPrice", _cardRoot, "", 44, TextAnchor.MiddleCenter, Bulb, _display);
            Anchor(_cardPriceText.rectTransform, new Vector2(0.5f, 0.12f), new Vector2(820, 80));
            DisplayFx(_cardPriceText);
            _cardPriceText.gameObject.SetActive(false);

            // ---- Answer plates (bottom) ----
            _yesPlate = NewSprite("YesPlate", _gamePanel.transform, Sprite("plate-yes"));
            _yesPlate.type = Image.Type.Sliced;
            _yesRect = _yesPlate.rectTransform;
            Anchor(_yesRect, new Vector2(0.31f, 0.145f), new Vector2(380, 220));
            _yesRect.localRotation = Quaternion.Euler(0, 0, YesTilt);
            var yesText = NewText("YesText", _yesPlate.transform, "ДА", 64, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(yesText.rectTransform);
            DisplayFx(yesText);
            _yesPlateText = yesText;

            _noPlate = NewSprite("NoPlate", _gamePanel.transform, Sprite("plate-no"));
            _noPlate.type = Image.Type.Sliced;
            _noRect = _noPlate.rectTransform;
            Anchor(_noRect, new Vector2(0.69f, 0.145f), new Vector2(380, 220));
            _noRect.localRotation = Quaternion.Euler(0, 0, NoTilt);
            var noText = NewText("NoText", _noPlate.transform, "СПАСИБО,\nНЕ НАДО", 46, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(noText.rectTransform);
            DisplayFx(noText);
            _noPlateText = noText;

            // ---- Burnout state plate (S7): dim-cobalt banner, shown only while Game.Burnout is on ----
            _burnoutPlate = NewSolid("BurnoutPlate", _gamePanel.transform, CobaltDeep).gameObject;
            Anchor(_burnoutPlate.GetComponent<RectTransform>(), new Vector2(0.5f, 0.70f), new Vector2(560, 96));
            var burnoutTxt = NewText("BurnoutText", _burnoutPlate.transform,
                "ВЫГОРАНИЕ", 44, TextAnchor.MiddleCenter, Muted, _display);
            Stretch(burnoutTxt.rectTransform);
            DisplayFx(burnoutTxt);
            _burnoutPlate.SetActive(false);

            // ---- Breakup notice: transient red «РАССТАЛИСЬ» plate (shown ~2s on a breakup) ----
            _breakupPlate = NewSolid("BreakupPlate", _gamePanel.transform, TimerRed).gameObject;
            Anchor(_breakupPlate.GetComponent<RectTransform>(), new Vector2(0.775f, 0.70f), new Vector2(360, 96));
            var breakupTxt = NewText("BreakupText", _breakupPlate.transform,
                "РАССТАЛИСЬ", 40, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(breakupTxt.rectTransform);
            DisplayFx(breakupTxt);
            _breakupPlate.SetActive(false);

            BuildCrisisHud();
            BuildHostReactions();
        }

        // Midlife-crisis HUD: a top readout (blitz progress + fail count / impulse index) and the S13 INVERT
        // warning plate. Both start hidden and are shown by RenderCrisis only while Game.InCrisis. The blitz's
        // two buttons and the fast timer REUSE the existing answer plates + timer ring (relabelled in-place).
        private void BuildCrisisHud()
        {
            _crisisInfo = NewSolid("CrisisInfo", _gamePanel.transform, CobaltDeep).gameObject;
            Anchor(_crisisInfo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.965f), new Vector2(720, 64));
            _crisisInfoText = NewText("CrisisInfoText", _crisisInfo.transform,
                "", 34, TextAnchor.MiddleCenter, TextLight, _display);
            Stretch(_crisisInfoText.rectTransform);
            DisplayFx(_crisisInfoText);
            _crisisInfo.SetActive(false);

            _impulseWarning = NewSolid("ImpulseWarning", _gamePanel.transform, TimerRed).gameObject;
            Anchor(_impulseWarning.GetComponent<RectTransform>(), new Vector2(0.5f, 0.32f), new Vector2(900, 160));
            var warn = NewText("ImpulseWarnText", _impulseWarning.transform,
                HostContent.ImpulseInvertWarning + "\n" + HostContent.ImpulseDeclinePrompt,
                46, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(warn.rectTransform);
            DisplayFx(warn);
            _impulseWarning.SetActive(false);
        }

        // Host speech bubble (S3, yellow bubble.png 9-slice) + rubric banner (S4). Both start hidden.
        private void BuildHostReactions()
        {
            // ---- Speech bubble (S3): a corner bubble, right of the card so it never covers it ----
            var bubbleImg = NewSprite("HostBubble", _gamePanel.transform, Sprite("bubble"));
            bubbleImg.type = Image.Type.Sliced;   // 9-slice border 70/120/70/70 (import already set)
            _hostBubble = bubbleImg.gameObject;
            Anchor(bubbleImg.rectTransform, new Vector2(0.85f, 0.42f), new Vector2(360, 220));
            _bubbleText = NewText("HostBubbleText", _hostBubble.transform, "", 34,
                TextAnchor.MiddleCenter, Ink, _display);
            // Inset asymmetrically: leave the bottom «tail» (120px border) clear of text.
            var brt = _bubbleText.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(34, 66);   // left, bottom (above the tail)
            brt.offsetMax = new Vector2(-34, -28);  // right, top
            _hostBubble.SetActive(false);

            // ---- Rubric banner (S4): a bold gold band over the card's upper area, static flourish ----
            _bannerRoot = NewGroup("HostBanner", _gamePanel.transform);
            _bannerBand = NewSolid("BannerBand", _bannerRoot.transform, Bulb);
            Anchor(_bannerBand.rectTransform, new Vector2(0.5f, 0.60f), new Vector2(1600, 240));
            // Static star flourishes at the band ends (no particles, no sound — canon).
            var starL = NewSprite("BannerStarL", _bannerBand.transform, Sprite("star-white"));
            Anchor(starL.rectTransform, new Vector2(0.05f, 0.5f), new Vector2(80, 80));
            var starR = NewSprite("BannerStarR", _bannerBand.transform, Sprite("star-white"));
            Anchor(starR.rectTransform, new Vector2(0.95f, 0.5f), new Vector2(80, 80));
            _bannerText = NewText("BannerText", _bannerBand.transform, "", 82,
                TextAnchor.MiddleCenter, Ink, _display);
            Inset(_bannerText.rectTransform, 150f);   // clear the flourishes at both ends
            DisplayFx(_bannerText);
            _bannerRoot.SetActive(false);
        }

        private GameObject BuildBar(string name, Vector2 anchor, string label, string icon,
            string fillSprite, out Image fill)
        {
            var group = NewGroup(name, _gamePanel.transform);
            Anchor(group.GetComponent<RectTransform>(), anchor, new Vector2(300, 110));

            var ico = NewSprite("Icon", group.transform, Sprite(icon));
            Anchor(ico.rectTransform, new Vector2(0.07f, 0.70f), new Vector2(46, 46));
            var lbl = NewText("Label", group.transform, label, 22, TextAnchor.MiddleLeft, TextLight, _display);
            Anchor(lbl.rectTransform, new Vector2(0.58f, 0.78f), new Vector2(230, 34));

            var track = NewSprite("Track", group.transform, Sprite("bar-track"));
            track.type = Image.Type.Sliced;
            Anchor(track.rectTransform, new Vector2(0.5f, 0.25f), new Vector2(300, 46));
            fill = NewSprite("Fill", track.transform, Sprite(fillSprite));
            fill.type = Image.Type.Sliced;
            var fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            return group;
        }

        private void BuildBalancer(Vector2 anchor)
        {
            _balancerGroup = NewGroup("Balancer", _gamePanel.transform);
            Anchor(_balancerGroup.GetComponent<RectTransform>(), anchor, new Vector2(340, 120));

            var lbl = NewText("Label", _balancerGroup.transform, "ОТНОШЕНИЯ", 22, TextAnchor.MiddleCenter, TextLight, _display);
            Anchor(lbl.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(320, 34));

            _balancerTrackWidth = 320f;
            _balancerTrackImg = NewSprite("Track", _balancerGroup.transform, Sprite("balancer-track"));
            Anchor(_balancerTrackImg.rectTransform, new Vector2(0.5f, 0.34f), new Vector2(_balancerTrackWidth, 46));
            _balancerMarkerImg = NewSprite("Marker", _balancerTrackImg.transform, Sprite("balancer-marker"));
            _balancerMarker = _balancerMarkerImg.rectTransform;
            Anchor(_balancerMarker, new Vector2(0.5f, 0.5f), new Vector2(58, 78));
        }

        // Child «cabinet button»: no dedicated sprite exists, so this is a code placeholder built from
        // marquee-bulb.png (the lit-bulb art) with a heart glyph — dim while idle, bright gold + pulsing
        // while the flash window is open (driven in Update off Game.ChildFlashing). A compact bar under it
        // shows the child scale so a лапс (bad-parent drop) reads. Hidden until MD02=ДА, and after LT04.
        private void BuildChildButton(Vector2 anchor)
        {
            _childGroup = NewGroup("Child", _gamePanel.transform);
            Anchor(_childGroup.GetComponent<RectTransform>(), anchor, new Vector2(220, 210));

            var lbl = NewText("Label", _childGroup.transform, "РЕБЁНОК", 22, TextAnchor.MiddleCenter, TextLight, _display);
            Anchor(lbl.rectTransform, new Vector2(0.5f, 0.92f), new Vector2(220, 30));

            _childButtonImg = NewSprite("Button", _childGroup.transform, Sprite("marquee-bulb"));
            _childButtonImg.color = CobaltDeep;   // idle (unlit)
            Anchor(_childButtonImg.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(128, 128));
            var heart = NewSprite("Heart", _childButtonImg.transform, Sprite("icon-heart"));
            Anchor(heart.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(56, 56));

            var hint = NewText("Hint", _childGroup.transform, "Enter", 20, TextAnchor.MiddleCenter, Muted, _display);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.14f), new Vector2(220, 26));

            var track = NewSprite("ScaleTrack", _childGroup.transform, Sprite("bar-track"));
            track.type = Image.Type.Sliced;
            Anchor(track.rectTransform, new Vector2(0.5f, 0.02f), new Vector2(180, 22));
            _childScaleFill = NewSprite("ScaleFill", track.transform, Sprite("bar-health-fill"));
            _childScaleFill.type = Image.Type.Filled;
            _childScaleFill.fillMethod = Image.FillMethod.Horizontal;
            _childScaleFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            var cfr = _childScaleFill.rectTransform;
            cfr.anchorMin = Vector2.zero; cfr.anchorMax = Vector2.one;
            cfr.offsetMin = Vector2.zero; cfr.offsetMax = Vector2.zero;
            _childScaleFill.fillAmount = 1f;

            _childGroup.SetActive(false);
        }

        private void BuildFinale(Transform parent)
        {
            _finalePanel = NewGroup("Finale", parent);

            _finaleTitle = NewText("FinaleTitle", _finalePanel.transform,
                "СПАСИБО ЗА ИГРУ!", 92, TextAnchor.MiddleCenter, Energy, _display);
            Anchor(_finaleTitle.rectTransform, new Vector2(0.5f, 0.85f), new Vector2(1700, 160));
            DisplayFx(_finaleTitle);

            var box = NewSprite("StoryBox", _finalePanel.transform, Sprite("marquee-frame"));
            box.type = Image.Type.Sliced;
            Anchor(box.rectTransform, new Vector2(0.5f, 0.46f), new Vector2(1400, 540));

            _finaleCause = NewText("FinaleCause", box.transform,
                "", 40, TextAnchor.UpperCenter, Bulb, _body);
            Anchor(_finaleCause.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(1160, 90));

            _finaleStory = NewText("FinaleStory", box.transform,
                "", 32, TextAnchor.UpperCenter, TextLight, _body);
            Anchor(_finaleStory.rectTransform, new Vector2(0.5f, 0.36f), new Vector2(1160, 300));

            var again = NewSprite("AgainPlate", _finalePanel.transform, Sprite("plate-yes"));
            again.type = Image.Type.Sliced;
            Anchor(again.rectTransform, new Vector2(0.5f, 0.10f), new Vector2(560, 140));
            var againText = NewText("AgainText", again.transform,
                "▸ ПРОЖИТЬ ЗАНОВО — Enter", 38, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(againText.rectTransform);
        }

        // S5 tutorial: full-screen dim + a yellow modal in the Host's tone + «ПОНЯТНО» plate.
        private void BuildTutorialOverlay(Transform parent)
        {
            _tutorialOverlay = NewSolid("TutorialOverlay", parent, new Color(0.02f, 0.03f, 0.10f, 0.78f)).gameObject;
            Stretch(_tutorialOverlay.GetComponent<RectTransform>());

            var modal = NewSprite("Modal", _tutorialOverlay.transform, Sprite("marquee-frame"));
            modal.type = Image.Type.Sliced;
            modal.color = Bulb;   // жёлтая карточка (тон Ведущего)
            Anchor(modal.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(1280, 560));

            var head = NewText("TutHead", modal.transform, "ПОДСКАЗКА", 34, TextAnchor.UpperCenter, Ink, _display);
            Anchor(head.rectTransform, new Vector2(0.5f, 0.86f), new Vector2(1100, 60));

            _tutorialText = NewText("TutBody", modal.transform, MoneyTutorialText, 40, TextAnchor.MiddleCenter, Ink, _body);
            Anchor(_tutorialText.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(1100, 320));

            // «Enter» is spelled out on the plate (founder Gate-2): Space is the crank and must never
            // dismiss a hint, so the dismiss key has to be discoverable right on the button.
            var plate = NewSprite("GotItPlate", modal.transform, Sprite("plate-yes"));
            plate.type = Image.Type.Sliced;
            Anchor(plate.rectTransform, new Vector2(0.5f, 0.14f), new Vector2(480, 120));
            var plateTxt = NewText("GotItText", plate.transform, "ПОНЯТНО — Enter ▸", 32, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(plateTxt.rectTransform);

            _tutorialOverlay.SetActive(false);
        }

        // Depression «тёмная полоса» (S8): a HARD black-&-white wash + static grain over the whole show, a
        // faint centre pulse the player must catch, and a 5-step colour-progress readout. Built hidden; the
        // whole group is toggled by Game.InDepression and driven in ReflectDepression.
        private void BuildDepressionOverlay(Transform parent)
        {
            _depressionGroup = NewGroup("Depression", parent);

            // B&W wash: a near-opaque gray Image whose alpha steps DOWN as colour returns (5 catches → clear).
            _depressionVeil = NewSolid("DepressionVeil", _depressionGroup.transform, new Color(GrayWash.r, GrayWash.g, GrayWash.b, 0.85f));
            Stretch(_depressionVeil.rectTransform);

            // Static grain: a faint seeded-noise texture stretched over the screen (self-contained, no asset).
            _depressionGrain = NewSprite("DepressionGrain", _depressionGroup.transform, MakeGrainSprite());
            Stretch(_depressionGrain.rectTransform);
            _depressionGrain.color = new Color(1f, 1f, 1f, 0.06f);

            // «Собраться» label + hint (muted).
            var label = NewText("DepLabel", _depressionGroup.transform,
                "СОБРАТЬСЯ", 64, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.88f), _display);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(1200, 120));
            var hint = NewText("DepHint", _depressionGroup.transform,
                "жмите Enter точно по тусклому пульсу — медленно и метко", 30, TextAnchor.MiddleCenter,
                new Color(0.7f, 0.7f, 0.74f), _body);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.64f), new Vector2(1200, 60));

            // Dim centre pulse — a faint soft dot, revealed only while the hit-window is open.
            _depressionPulse = NewSprite("DepressionPulse", _depressionGroup.transform, Sprite("star-white"));
            _depressionPulse.color = new Color(0.9f, 0.9f, 0.95f, 0.28f);
            Anchor(_depressionPulse.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(150, 150));
            _depressionPulse.gameObject.SetActive(false);

            // 5-step colour-progress pips (a step fills gold per catch).
            _depPips = new Image[Game.DepressionGraySteps];
            const float pipW = 70f, gap = 26f;
            float total = _depPips.Length * pipW + (_depPips.Length - 1) * gap;
            for (int i = 0; i < _depPips.Length; i++)
            {
                var pip = NewSolid("DepPip" + i, _depressionGroup.transform, new Color(1f, 1f, 1f, 0.15f));
                float x = -total / 2f + pipW / 2f + i * (pipW + gap);
                var rt = pip.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.34f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(pipW, 22f);
                _depPips[i] = pip;
            }

            _depressionGroup.SetActive(false);
        }

        // A small seeded noise texture used as static «зерно». Deterministic (fixed seed) so it never
        // flickers between builds; stretched full-screen and drawn very faint.
        private static UnityEngine.Sprite MakeGrainSprite()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(20260719);
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)rng.Next(256);
                px[i] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return UnityEngine.Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        // Depression started (CR09): muted «ТЁМНАЯ ПОЛОСА…» banner + reset the mutter cycle.
        private void OnDepressionStarted()
        {
            _depMutterCount = 0;
            ShowBannerText(HostContent.BannerFor("CR09"), muted: true);
        }

        // Each successful catch: a muted host mutter as a step of colour returns.
        private void OnDepressionProgressed()
        {
            _depMutterCount++;
            _bubbleTimer.Show(HostContent.DepressionMutterFor(_depMutterCount));
        }

        // Toggle + drive the depression overlay (called every frame from Update). The veil alpha steps with
        // the gray level, the pulse dot reveals only on the hit-window, and the pips fill as colour returns.
        private void ReflectDepression()
        {
            bool dep = _game != null && _game.State == GameState.Playing && _game.InDepression;
            if (_depressionGroup.activeSelf != dep) _depressionGroup.SetActive(dep);
            if (!dep) return;

            float grayT = Mathf.Clamp01(_game.DepressionGray / (float)Game.DepressionGraySteps);
            var vc = _depressionVeil.color;
            _depressionVeil.color = new Color(vc.r, vc.g, vc.b, grayT * 0.85f);   // 5 gray → 0.85, 0 → clear
            _depressionGrain.color = new Color(1f, 1f, 1f, 0.06f * grayT);

            bool pulsing = _game.DepressionPulsing;
            if (_depressionPulse.gameObject.activeSelf != pulsing) _depressionPulse.gameObject.SetActive(pulsing);
            if (pulsing)
            {
                float a = 0.22f + 0.14f * Mathf.Sin(Time.time * 4f);   // slow faint throb
                _depressionPulse.color = new Color(0.9f, 0.9f, 0.95f, a);
                _depressionPulse.rectTransform.localScale = Vector3.one * (1f + 0.10f * Mathf.Sin(Time.time * 4f));
            }

            int restored = Game.DepressionGraySteps - _game.DepressionGray;   // filled steps of colour
            for (int i = 0; i < _depPips.Length; i++)
                _depPips[i].color = i < restored ? Bulb : new Color(1f, 1f, 1f, 0.15f);
        }

        private void OnMoneyOpened()  { if (!_moneyTutorialSeen)  ShowTutorial(MoneyTutorialText,  ref _moneyTutorialSeen); }
        private void OnRelationshipsOpened() { if (!_relTutorialSeen) ShowTutorial(RelationshipsTutorialText, ref _relTutorialSeen); }
        private void OnEnergyOpened() { if (!_energyTutorialSeen) ShowTutorial(EnergyTutorialText, ref _energyTutorialSeen); }
        private void OnHealthOpened() { if (!_healthTutorialSeen) ShowTutorial(HealthTutorialText, ref _healthTutorialSeen); }
        private void OnBurnoutEntered(){ if (!_burnoutHintSeen)  ShowTutorial(BurnoutHintText,   ref _burnoutHintSeen); }
        // MD02=ДА opened the child scale: the «ПОПОЛНЕНИЕ!» rubric banner already fired when MD02 was drawn;
        // this sequences the S5 hint after the answer (one-shot per life, pauses like every other open).
        private void OnChildOpened()  { if (!_childTutorialSeen)  ShowTutorial(ChildTutorialText,  ref _childTutorialSeen); }

        // Breakup: flash the transient «РАССТАЛИСЬ» plate (auto-hides on its own ~2s clock, reflected in
        // Update). The balancer HUD hides itself off Game.RelationshipsLost on the next ApplyAgeGates.
        private void OnRelationshipBrokeUp() => _breakupTimer.Show("РАССТАЛИСЬ");

        // Shared S5 hint: pauses the game (freezes age, drains, cost-of-living, decay and the card timer)
        // and shows the modal. The one-shot «seen» flag is set at show time (the hint always resolves via
        // dismiss). Opens don't collide — each pauses until dismissed — so a stacked show is simply skipped.
        private void ShowTutorial(string text, ref bool seen)
        {
            if (_tutorialShowing) return;
            seen = true;
            _tutorialShowing = true;
            _tutorialText.text = text;
            _tutorialOverlay.transform.SetAsLastSibling();
            _tutorialOverlay.SetActive(true);
            _game.Paused = true;
        }

        private void DismissTutorial()
        {
            if (!_tutorialShowing) return;
            _tutorialShowing = false;
            _tutorialOverlay.SetActive(false);
            _game.Paused = false;
            // Root cause of the «one card late» founder bug: the 18/25/30 crossings happen MID-CARD
            // (inside Game.Tick), but the age-gated HUD reveal only ran on card-advance/state-change.
            // Refresh it NOW so the just-opened widget (money pill / energy / health bar) is visible
            // and live on THIS card the moment the hint closes.
            if (_game.State == GameState.Playing)
            {
                UpdateHudValues();
                ApplyAgeGates(_game.Age);
            }
        }

        // ================================================================ refresh / events

        private void Refresh()
        {
            if (_game == null) return;
            bool opener = _game.State == GameState.Opener;
            bool playing = _game.State == GameState.Playing;
            bool finale = _game.State == GameState.Finale;

            _openerPanel.SetActive(opener);
            _gamePanel.SetActive(playing);
            _finalePanel.SetActive(finale);

            // Fresh life → every hint is armed again, breathing re-seeds, and leftover state is cleared.
            if (playing && !_wasPlaying)
            {
                _moneyTutorialSeen = false;
                _relTutorialSeen = false;
                _energyTutorialSeen = false;
                _healthTutorialSeen = false;
                _burnoutHintSeen = false;
                _childTutorialSeen = false;
                _childGroup.SetActive(false);
                _tutorialShowing = false;
                _tutorialOverlay.SetActive(false);
                _game.Paused = false;
                _crankCap.Reset();
                _breath.Reset();
                _burnoutPlate.SetActive(false);
                _breakupTimer.Hide();
                _breakupPlate.SetActive(false);
                RestoreNormalPlates();   // clear any crisis relabel/highlight carried across a restart
                _depressionGroup.SetActive(false);   // no B&W wash carried across a restart
                _depMutterCount = 0;
                _brightnessAlpha = 0f;
                // Host reveals reset each life: no stale bubble/banner carried across a restart.
                _bubbleTimer.Hide();
                _bannerTimer.Hide();
                _hostBubble.SetActive(false);
                _bannerRoot.SetActive(false);
            }
            if (!playing && _tutorialShowing) DismissTutorial();
            _wasPlaying = playing;

            if (playing)
            {
                UpdateHudValues();
                ApplyAgeGates(_game.Age);
            }
            else if (finale && _game.Necrolog != null)
            {
                var n = _game.Necrolog;
                _finaleTitle.text = n.Title;
                _finaleCause.text = n.CauseLine;
                _finaleStory.text = n.ComposeStory();
            }
        }

        private void OnCardChanged()
        {
            if (_game == null || _game.State != GameState.Playing) return;
            var c = _game.CurrentCard;
            _cardText.text = c != null ? c.Question : "";
            bool blocked = _game.CurrentCardBlocked;
            _blockVeil.SetActive(blocked);        // S10: dim the card + red banner when unaffordable
            _blockBanner.SetActive(blocked);
            RefreshPriceLabel();                  // S10: show the required amount on any BLOCK$ card
            // Rubric banner (S4): announce on a TIMELINE milestone; clear it on any non-milestone card
            // (so it never lingers onto the card after the milestone). The bubble is answer-driven and
            // deliberately NOT touched here — it survives this same-frame advance to live out its ~2s.
            if (c != null && c.IsTimeline) ShowBanner(c);
            else _bannerTimer.Hide();
            UpdateHudValues();
            ApplyAgeGates(_game.Age);
            if (c != null && isActiveAndEnabled)
            {
                if (_cardAnim != null) StopCoroutine(_cardAnim);
                _cardAnim = StartCoroutine(CardEntry());
            }
        }

        // Host bubble: on every resolved answer pick a line (named for the chosen side beats the tone
        // pool; overall ~30–40%). Non-null → show ~2s; null → clear (so a new card with no line hides the
        // old bubble). Fires BEFORE the next card is drawn, so it reflects the choice just made.
        private void OnAnswerResolved(Card card, AnswerSide side)
        {
            var line = _voice.Pick(card, side);
            if (!string.IsNullOrEmpty(line)) _bubbleTimer.Show(line);
            else _bubbleTimer.Hide();
        }

        // Show the rubric band for a TIMELINE card. CR09 is styled muted (out-of-scope crisis; text baked).
        private void ShowBanner(Card c)
            => ShowBannerText(HostContent.BannerFor(c.Id), muted: c.Id == HostContent.MutedBannerId);

        // Show an arbitrary rubric-band caption (used by TIMELINE cards and the crisis CR00 banner).
        private void ShowBannerText(string text, bool muted)
        {
            _bannerBand.color = muted ? CobaltDeep : Bulb;
            _bannerText.color = muted ? Muted : Ink;
            _bannerText.text = text;
            _bannerTimer.Show(text);
            _bannerRoot.transform.SetAsLastSibling();   // draw over the card
        }

        // Advance both host clocks (real-time) and mirror their visibility onto the widgets. Only visible
        // while Playing; the timers keep their own state so a restart/leaving-play simply hides them.
        private void ReflectHostReveals(bool playing)
        {
            _bubbleTimer.Advance(Time.deltaTime);
            _bannerTimer.Advance(Time.deltaTime);

            bool bub = playing && _bubbleTimer.Visible;
            if (_hostBubble.activeSelf != bub) _hostBubble.SetActive(bub);
            if (bub) _bubbleText.text = _bubbleTimer.Text;

            bool ban = playing && _bannerTimer.Visible;
            if (_bannerRoot.activeSelf != ban) _bannerRoot.SetActive(ban);
            if (ban) _bannerText.text = _bannerTimer.Text;
        }

        // BLOCK$ price sub-line: reads the single source (Game.CurrentCardPrice / CurrentCardBlocked) and
        // shows the required amount on the card in both the affordable and the blocked (dimmed) states.
        private void RefreshPriceLabel()
        {
            bool has = _game.CurrentCardHasPrice;
            ApplyPriceLabel(has, has ? _game.CurrentCardPrice : 0, _game.CurrentCardBlocked);
        }

        private void ApplyPriceLabel(bool hasPrice, double price, bool blocked)
        {
            if (!hasPrice) { _cardPriceText.gameObject.SetActive(false); return; }
            int p = Mathf.RoundToInt((float)price);
            _cardPriceText.text = (blocked ? "НУЖНО " : "СТОИТ ") + p + " ₽";
            _cardPriceText.color = blocked ? TextLight : Bulb;
            _cardPriceText.gameObject.SetActive(true);
        }

        private void UpdateHudValues()
        {
            var s = _game.Scales;
            _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
            _moneyText.text = FormatMoney(_game.Money);
            _healthFill.fillAmount = Mathf.Clamp01(s.Health / 100f);
            _energyFill.fillAmount = Mathf.Clamp01(s.Energy / 100f);
            float rel = Mathf.Clamp01(s.Relationships / 100f);
            _balancerMarker.anchoredPosition = new Vector2((rel - 0.5f) * _balancerTrackWidth, 0f);
        }

        /// <summary>Reveal HUD widgets by age (visual only — the scales themselves stay passive).</summary>
        private void ApplyAgeGates(float age)
        {
            int a = Mathf.FloorToInt(age);
            _ageBadge.SetActive(true);
            _moneyPill.SetActive(a >= MoneyAge);
            // Balancer reveals at 20 and hides again after a breakup (partner gone — MD06 reopen deferred).
            _balancerGroup.SetActive(a >= RelationshipsAge && !_game.RelationshipsLost);
            _energyGroup.SetActive(a >= EnergyAge);
            _healthGroup.SetActive(a >= HealthAge);
        }

        private void OnInputFx(GameInput input)
        {
            // MoneyTick FX (pill pulse) is triggered from OnInput's ACCEPTED-crank branch instead —
            // raw (capped/no-op) presses must not flash feedback for income that didn't land.
            if (_game == null || _game.State != GameState.Playing || !isActiveAndEnabled) return;
            if (_tutorialShowing) return;
            if (input == GameInput.AnswerYes) StartCoroutine(PunchPlate(_yesRect, YesTilt));
            else if (input == GameInput.AnswerNo) StartCoroutine(PunchPlate(_noRect, NoTilt));
        }

        // Small pill pulse on each accepted crank tick (feedback that the tick landed).
        private IEnumerator PulseMoney()
        {
            var rt = (RectTransform)_moneyPill.transform;
            const float dur = 0.12f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float s = 1f + 0.06f * Mathf.Sin(k * Mathf.PI);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
            _moneyPulse = null;
        }

        // ================================================================ transitions

        private IEnumerator CardEntry()
        {
            const float dur = 0.24f;
            float t = 0f;
            var rt = _cardRoot;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                // ease-out-back overshoot
                float s = 1f + 2.2f * Mathf.Pow(k - 1f, 3) + 1.2f * Mathf.Pow(k - 1f, 2);
                float scale = Mathf.Lerp(0.82f, 1f, s);
                rt.localScale = new Vector3(scale, scale, 1f);
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(-60f, 0f, k));
                yield return null;
            }
            rt.localScale = Vector3.one;
            rt.anchoredPosition = Vector2.zero;
            _cardAnim = null;
        }

        private static IEnumerator PunchPlate(RectTransform rt, float tilt)
        {
            const float dur = 0.16f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float scale = 1f - 0.14f * Mathf.Sin(k * Mathf.PI); // dip and return
                rt.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        // ================================================================ UI helpers

        private static Sprite Sprite(string name)
        {
            var s = Resources.Load<Sprite>("Sprites/" + name);
            if (s == null) Debug.LogError("[ThanksNoThanks] sprite not found: Sprites/" + name);
            return s;
        }

        private static string FormatThousands(int value)
        {
            return value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
        }

        // Live money → «₽ N» (floored; negative allowed — «в минус», canon).
        private static string FormatMoney(double value)
        {
            return "₽ " + FormatThousands(Mathf.FloorToInt((float)value));
        }

        private Image NewSprite(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A flat solid-colour Image (no sprite) — veils, banners, dimmers.</summary>
        private Image NewSolid(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Transparent full-rect container (a panel that groups children without drawing).</summary>
        private GameObject NewGroup(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            return go;
        }

        private Text NewText(string name, Transform parent, string content, int size,
            TextAnchor anchor, Color color, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        /// <summary>Mockup letter treatment: thick ink outline + downward drop shadow.</summary>
        private static void DisplayFx(Graphic g)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.078f, 0.102f, 0.239f, 1f);
            o.effectDistance = new Vector2(3f, -3f);
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.32f);
            sh.effectDistance = new Vector2(0f, -6f);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Inset(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        // Anchor a rect at a normalized point of its parent with a fixed pixel size.
        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }
    }
}
