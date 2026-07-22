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
        private static readonly Color PlateMute = new(0.62f, 0.62f, 0.64f);    // S10: muted answer plates while BLOCK$-blocked
        private static readonly Color CardBlockDim = new(0.52f, 0.54f, 0.60f); // S10: tint the card frame when unaffordable (dims to muted cobalt)

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
        private GameObject _hudRow;    // top-row container (age·деньги·здоровье·энергия·отношения·ребёнок)
        private GameObject _ageBadge;
        private GameObject _moneyPill;
        private GameObject _healthGroup;
        private GameObject _energyGroup;
        private GameObject _balancerGroup;

        private Text _ageText;
        private Text _moneyText;
        private Text _moneyLabel;      // «ДЕНЬГИ ×N» under the pill (dark-blue)
        private Text _healthLabel;     // «ЗДОРОВЬЕ» under the capsule
        private Text _energyLabel;     // «ЭНЕРГИЯ»
        private Text _relLabel;        // «ОТНОШЕНИЯ»
        private Image _healthFill;
        private Image _energyFill;
        private RectTransform _balancerMarker;
        private Image _balancerTrackImg;   // tinted red in the >75% «красная зона»
        private Image _balancerMarkerImg;  // tinted red in the red zone too
        private float _balancerTrackWidth;

        // ---- child button (opens on MD02=ДА, not age-gated; flashes on the signal-response window) ----
        private GameObject _childGroup;    // whole widget; shown while Game.ChildOpen, hidden after LT04
        private Image _childButtonImg;     // the «lit» bulb — bright while ChildFlashing, dim otherwise

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
        private GameObject _timerGroup;   // whole ring widget; hidden during a rubric banner beat
        private Image _timerFill;
        private Text _timerText;

        // Finale
        private Text _finaleTitle;
        private Text _finaleCause;
        private Text _finaleStory;
        private Image _finaleStoryPlate;

        // Tutorial modal widgets (S5) — captured for Layer-2 conformance.
        private Image _tutorialModal;
        private Image _tutorialButton;
        private Text _tutorialButtonText;

        // BLOCK$ (S10): the card is dimmed by tinting its OWN frame sprite (exact rounded silhouette — a
        // separate veil rect showed straight edges cutting across the sunburst), plus a red block-tag banner.
        private GameObject _blockBanner;
        // BLOCK$ price sub-line on the card: «СТОИТ N ₽» when affordable, «НУЖНО N ₽» when blocked.
        // Above the veil (drawn after it), so it stays legible in the dimmed/blocked state too.
        // Sits on a dark rounded plate (_cardPricePlate) so the gold/light text never reads as
        // «gold on yellow» against the sunburst — the S10 dark block-tag treatment.
        private Text _cardPriceText;
        private Image _cardPricePlate;

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
        public GameObject HudRow => _hudRow;
        public GameObject HealthGroup => _healthGroup;
        public GameObject EnergyGroup => _energyGroup;
        public GameObject BalancerGroup => _balancerGroup;
        public Text MoneyLabel => _moneyLabel;
        public Text HealthLabel => _healthLabel;
        public Text EnergyLabel => _energyLabel;
        public Text RelLabel => _relLabel;
        public Image TimerRingFill => _timerFill;
        public GameObject OpenerPanel => _openerPanel;
        public GameObject GamePanel => _gamePanel;
        public GameObject FinalePanel => _finalePanel;
        public GameObject TutorialOverlay => _tutorialOverlay;
        public bool TutorialShowing => _tutorialShowing;
        public Image TutorialModal => _tutorialModal;
        public Image TutorialButton => _tutorialButton;
        public Text TutorialButtonText => _tutorialButtonText;
        public Text FinaleTitleText => _finaleTitle;
        public Text FinaleCauseText => _finaleCause;
        public Text FinaleStoryText => _finaleStory;
        public Image FinaleStoryPlate => _finaleStoryPlate;
        public GameObject BlockBanner => _blockBanner;
        public Text CardPriceText => _cardPriceText;
        public Image CardPricePlate => _cardPricePlate;
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

        /// <summary>
        /// Test hook: force the finale panel visible and render an arbitrary necrolog (title/cause/story)
        /// into it, without driving a whole life — so a Layer-2 test can stress a worst-case LONG story
        /// against the story plate. Mirrors the finale branch of <see cref="Refresh"/>.
        /// </summary>
        /// <summary>Test/screenshot hook: raise a tutorial modal with the given body over live gameplay.</summary>
        public void DebugShowTutorial(string text)
        {
            bool dummy = false;
            ShowTutorial(text, ref dummy);
        }

        public void DebugRenderFinale(NecrologResult n)
        {
            _openerPanel.SetActive(false);
            _gamePanel.SetActive(false);
            _finalePanel.SetActive(true);
            RenderFinaleTexts(n);
        }

        // ---- Design-gate Layer-3 screenshot hooks: render a state overlay in a representative pose over the
        // live game panel and FREEZE the driver (disable Update) so the capture is stable. Visual-only — the
        // pure Game is never touched; these only flip the driver's own overlay Images on for a screenshot. ----
        public void DebugPreviewBurnout()
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            _burnoutPlate.SetActive(true);
            enabled = false;
        }

        public void DebugPreviewDepression()
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            ApplyAgeGates(60f);                         // reveal the minimal live HUD behind the wash
            _cardText.text = "Встать сегодня с кровати?";
            _depressionGroup.SetActive(true);
            _depressionVeil.color = new Color(GrayWash.r, GrayWash.g, GrayWash.b, 0.92f);   // match live ReflectDepression (S8 fix: heavier wash suppresses the sunburst)
            _depressionGrain.color = new Color(1f, 1f, 1f, 0.06f);
            _depressionPulse.gameObject.SetActive(true);
            _depressionPulse.color = new Color(0.9f, 0.9f, 0.95f, 0.30f);
            enabled = false;
        }

        public void DebugPreviewChildFlash()
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            ApplyAgeGates(40f);
            _cardText.text = "Обычная жизнь идёт…";
            _childGroup.SetActive(true);
            _childButtonImg.color = Bulb;                                  // lit gold
            _childButtonImg.rectTransform.localScale = Vector3.one * 1.10f; // max flash pulse
            _bubbleText.text = "Скорее!"; _hostBubble.SetActive(true);
            enabled = false;
        }

        // S10 dim: tint the card's own marquee sprite (fill + bulbs + border) toward muted cobalt so the
        // darkening follows the card's exact rounded silhouette — no overlay rectangle spilling onto the rays.
        // The red banner + price plate are separate _cardRoot children and stay bright above the dimmed card.
        private void SetCardBlockedDim(bool blocked)
        {
            var tint = blocked ? CardBlockDim : Color.white;
            if (_cardFrame.color != tint) _cardFrame.color = tint;
        }

        public void DebugPreviewBlocked()
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            ApplyAgeGates(58f);
            _cardText.text = "Пора подлечиться!";
            SetCardBlockedDim(true);
            _blockBanner.SetActive(true);
            ApplyPriceLabel(true, 100, blocked: true);
            _yesPlate.color = PlateMute; _noPlate.color = PlateMute;
            enabled = false;
        }

        // Set the three finale texts and size the story plate to its content (short story → compact plate).
        private void RenderFinaleTexts(NecrologResult n)
        {
            _finaleTitle.text = n.Title;
            _finaleCause.text = n.CauseLine;
            _finaleStory.text = n.ComposeStory();
            FitStoryPlate();
        }

        // Size the story plate HEIGHT to its content: a short story gets a compact plate (no stranded text in
        // a huge navy box), a long story keeps the full clamped height and best-fit wraps inside the pill.
        private void FitStoryPlate()
        {
            const float inset = 60f, minH = 170f, maxH = 360f;
            var rt = _finaleStoryPlate.rectTransform;
            var t = _finaleStory;
            float textW = rt.rect.width - 2f * inset;
            bool bf = t.resizeTextForBestFit;
            int fs = t.fontSize;
            t.resizeTextForBestFit = false;
            t.fontSize = t.resizeTextMaxSize;   // measure at the largest size the plate would ever use
            var settings = t.GetGenerationSettings(new Vector2(textW, 0f));
            float ph = t.cachedTextGeneratorForLayout.GetPreferredHeight(t.text, settings) / t.pixelsPerUnit;
            t.fontSize = fs;
            t.resizeTextForBestFit = bf;
            float h = Mathf.Clamp(ph + 2f * inset, minH, maxH);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
        }

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

            // A rubric banner beat is up (S4/S6 — the card is hidden): swallow ALL input so a masher can't
            // answer the hidden card or skip the announce unread. It auto-advances on its own ~1.5s clock.
            if (_bannerTimer.Visible && _game.State == GameState.Playing) return;

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
            if (_game.State == GameState.Playing && _bannerTimer.Visible)
            {
                // Rubric banner beat (S4/S6): the card, plates and timer are hidden (ReflectBannerBeat) and
                // the game is paused — render nothing gameplay here so the banner never overlaps a card.
            }
            else if (_game.State == GameState.Playing && _game.InCrisis)
            {
                RenderCrisis();   // S6 blitz / S13 impulse — reuses the plates + timer ring, freezes normal HUD
            }
            else if (_game.State == GameState.Playing)
            {
                if (_crisisUiActive) RestoreNormalPlates();   // just resumed from a crisis → restore the plates
                _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
                _moneyText.text = FormatMoney(_game.Money);   // live: ticks up on crank, drains down
                _moneyLabel.text = FormatMoneyLabel(_game.IncomeMultiplier);   // ×N (burnout halves live)
                // Live health/energy bars + balancer move on their own (decay/drain/breath), not just on cards.
                var s = _game.Scales;
                _healthFill.fillAmount = Mathf.Clamp01(s.Health / 100f);
                _energyFill.fillAmount = Mathf.Clamp01(s.Energy / 100f);
                float rel = Mathf.Clamp01(s.Relationships / 100f);
                _balancerMarker.anchoredPosition = new Vector2((rel - 0.5f) * _balancerTrackWidth, 0f);
                // «Красная зона» (>75%): keep the zone bar's own colours (tinting the green HOLD-ZONE red
                // muddied it to brown) and flag the risk on the MARKER alone.
                var markerTint = _game.RelationshipRedZone ? TimerRed : Color.white;
                if (_balancerTrackImg.color != Color.white) _balancerTrackImg.color = Color.white;
                if (_balancerMarkerImg.color != markerTint) _balancerMarkerImg.color = markerTint;
                if (_burnoutPlate.activeSelf != _game.Burnout) _burnoutPlate.SetActive(_game.Burnout);
                // S10: while the current card is BLOCK$-blocked, mute the two answer plates (the card veil
                // dims the marquee, this dims the plates) so the whole board reads «недоступно».
                var plateTint = _game.CurrentCardBlocked ? PlateMute : Color.white;
                if (_yesPlate.color != plateTint) _yesPlate.color = plateTint;
                if (_noPlate.color != plateTint) _noPlate.color = plateTint;
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
            ReflectHostReveals(_game.State == GameState.Playing);   // advances the banner-beat clock + pause
            ReflectBannerBeat();                                    // hide the card/plates while the beat is up
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

            // S6 minimal HUD: only the age badge + the crisis counter show during the crisis — hide the
            // money pill / bars / balancer / child (re-revealed by ApplyAgeGates the moment play resumes).
            _moneyPill.SetActive(false);
            _moneyLabel.gameObject.SetActive(false);
            _healthGroup.SetActive(false);
            _energyGroup.SetActive(false);
            _balancerGroup.SetActive(false);
            if (_childGroup != null) _childGroup.SetActive(false);

            // BLOCK$ visuals never apply during a crisis.
            SetCardBlockedDim(false);
            _blockBanner.SetActive(false);
            _cardPriceText.gameObject.SetActive(false);
            _cardPricePlate.gameObject.SetActive(false);

            bool blitz = _game.Phase == CrisisPhase.Blitz;
            if (blitz)
            {
                // «ВСЁ НОРМАЛЬНО» jumps sides each thought; ← = left plate (yes), → = right plate (no).
                bool normalLeft = _game.BlitzNormalOnLeft;
                _yesPlateText.text = normalLeft ? "ВСЁ\nНОРМАЛЬНО" : "О НЕТ";
                _noPlateText.text = normalLeft ? "О НЕТ" : "ВСЁ\nНОРМАЛЬНО";
                _yesPlate.color = Color.white;
                _noPlate.color = Color.white;
                // S6: both blitz plates are LARGE and EQUAL so the 2-line «ВСЁ НОРМАЛЬНО» sits fully inside
                // the colored pill with margin. The 9-slice plate sprite keeps a fixed ~55px corner inset,
                // so the normal 420×190 plate only exposes a ~79px-tall pill — far too short for two lines at
                // a readable size (the label spilled off the pill top/sides). Enlarge the rect AND the text
                // rect (which must clear the corner inset) so best-fit resolves a big font that still fits.
                UseCrisisBlitzPlates();
                // S6 counter: two lines on a dark badge — «МЫСЛЬ N/5» light, «ПРОВАЛОВ: K» in muted red.
                _crisisInfoText.text = $"МЫСЛЬ {_game.BlitzThoughtNumber}/5\n<color=#e8686a>ПРОВАЛОВ: {_game.BlitzFails}</color>";
            }
            else
            {
                // Impulse (S13): ← = поддаться (ДА), → = «СПАСИБО, НЕ НАДО». Highlight the → decline (обратный акцент).
                // Impulse keeps the normal-sized plates (per S13 / accepted 03 shot).
                UseNormalPlates();
                _yesPlateText.text = "ДА";
                _noPlateText.text = "СПАСИБО,\nНЕ НАДО";
                _yesPlateText.resizeTextMaxSize = 60;   // single-line «ДА» reads big
                _noPlateText.resizeTextMaxSize = 40;
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
            UseNormalPlates();   // restore rect size, text rect + best-fit ceilings the crisis enlarged
            _yesPlateText.text = "ДА";
            _noPlateText.text = "СПАСИБО,\nНЕ НАДО";
            _yesPlate.color = Color.white;
            _noPlate.color = Color.white;
            if (_crisisInfo != null) _crisisInfo.SetActive(false);
            if (_impulseWarning != null) _impulseWarning.SetActive(false);
        }

        // ---- Answer-plate geometry ------------------------------------------------------------------
        // The plate sprite is a 460×270 9-slice with an 82px border; its colored pill (the flat green/red
        // fill) sits ~55px in from every rect edge REGARDLESS of the rect size (9-slice corners are fixed).
        // So a plate rect of W×H shows a pill of only ~(W-110)×(H-110). The normal 190-tall plate therefore
        // exposes a pill barely ~80px tall — fine for «ДА» / a small 2-line decline, but the blitz
        // «ВСЁ НОРМАЛЬНО» needs a genuinely large pill or best-fit resolves a font whose two lines spill off
        // the pill top/sides. The blitz uses an enlarged, equal pair; everything else uses the normal sizes.
        private static readonly Vector2 NormalYesPlateSize = new Vector2(420f, 190f);
        private static readonly Vector2 NormalNoPlateSize = new Vector2(470f, 190f);
        private static readonly Vector2 CrisisBlitzPlateSize = new Vector2(520f, 250f);

        // Enlarge both blitz plates equally (S6) and push the text rect inside the (now taller) colored pill.
        private void UseCrisisBlitzPlates()
        {
            _yesRect.sizeDelta = CrisisBlitzPlateSize;
            _noRect.sizeDelta = CrisisBlitzPlateSize;
            CrisisPlateTextRect(_yesPlateText.rectTransform);
            CrisisPlateTextRect(_noPlateText.rectTransform);
            _yesPlateText.resizeTextMinSize = 28; _yesPlateText.resizeTextMaxSize = 48;
            _noPlateText.resizeTextMinSize = 28; _noPlateText.resizeTextMaxSize = 48;
        }

        // Normal answer plates (childhood/adult play + the S13 impulse): the sizes/insets built in BuildUi.
        private void UseNormalPlates()
        {
            _yesRect.sizeDelta = NormalYesPlateSize;
            _noRect.sizeDelta = NormalNoPlateSize;
            PlateTextRect(_yesPlateText.rectTransform);
            PlateTextRect(_noPlateText.rectTransform);
            _yesPlateText.resizeTextMinSize = 26; _yesPlateText.resizeTextMaxSize = 60;
            _noPlateText.resizeTextMinSize = 22; _noPlateText.resizeTextMaxSize = 40;
        }

        // The game is paused while EITHER a tutorial overlay OR a rubric banner beat is up. Both share the
        // single Game.Paused freeze (age, drains, cost-of-living, card timer, crisis timer). Kept in sync
        // from one place so a banner beat and a tutorial can never leave the pause flag stale.
        private void SyncPause()
        {
            if (_game == null) return;
            _game.Paused = _tutorialShowing || _bannerTimer.Visible;
        }

        // While a rubric banner beat is up (S4/S6), the card marquee, the two answer plates and the timer
        // ring are HIDDEN so the banner is its own beat and can never overlap a card (the founder bug).
        // Restored the instant the beat clears. Idempotent — safe to call every frame.
        private void ReflectBannerBeat()
        {
            if (_cardRoot == null) return;
            bool beat = _game != null && _game.State == GameState.Playing && _bannerTimer.Visible;
            bool show = !beat;
            if (_cardRoot.gameObject.activeSelf != show) _cardRoot.gameObject.SetActive(show);
            if (_yesPlate.gameObject.activeSelf != show) _yesPlate.gameObject.SetActive(show);
            if (_noPlate.gameObject.activeSelf != show) _noPlate.gameObject.SetActive(show);
            if (_timerGroup != null && _timerGroup.activeSelf != show) _timerGroup.SetActive(show);
            // S4: the banner is a clean beat — the whole HUD row is hidden too (re-shown, age-gated, after).
            if (_hudRow != null && _hudRow.activeSelf != show) _hudRow.SetActive(show);
            if (beat)
            {
                if (_crisisInfo != null && _crisisInfo.activeSelf) _crisisInfo.SetActive(false);
                if (_impulseWarning != null && _impulseWarning.activeSelf) _impulseWarning.SetActive(false);
            }
        }

        /// <summary>
        /// Test seam: advance the driver-side host reveal clocks (speech bubble + the blocking rubric banner
        /// beat) by an injected <paramref name="dt"/> and reconcile pause + visibility exactly as Update
        /// would — so a synchronous Game.Tick-driven test can pass THROUGH a banner beat deterministically
        /// without pumping real frames. Production drives these off Time.deltaTime in Update.
        /// </summary>
        public void DebugPumpHost(float dt)
        {
            if (_game == null) return;
            _bubbleTimer.Advance(dt);
            _bannerTimer.Advance(dt);
            SyncPause();
            bool playing = _game.State == GameState.Playing;
            bool bub = playing && _bubbleTimer.Visible;
            if (_hostBubble != null && _hostBubble.activeSelf != bub) _hostBubble.SetActive(bub);
            bool ban = playing && _bannerTimer.Visible;
            if (_bannerRoot.activeSelf != ban) _bannerRoot.SetActive(ban);
            ReflectBannerBeat();
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

        // The four opener rules (S1) — each on its own cobalt plate, verbatim from the mockup.
        private static readonly string[] OpenerRules =
        {
            "Проживите ЦЕЛУЮ ЖИЗНЬ за пару минут — в прямом эфире!",
            "На каждый вопрос — рычаг: ДА или СПАСИБО, НЕ НАДО. 5 секунд — дальше решаем за вас!",
            "С возрастом откроются ручки жизни. Рук две — всё удержать нельзя, и это нормально!",
            "Правильного ответа нет. Есть только ВАША жизнь.",
        };

        private void BuildOpener(Transform parent)
        {
            _openerPanel = NewGroup("Opener", parent);

            var title = NewText("Title", _openerPanel.transform,
                "«СПАСИБО, НЕ НАДО»", 96, TextAnchor.MiddleCenter, TextLight, _display);
            AnchorPx(title.rectTransform, 960f, 150f, 1650f, 175f);
            DisplayFx(title);

            // S1: each rule on its OWN cobalt rounded plate (bar-track 9-slice tinted deep cobalt) —
            // never bare text on the sunburst. White best-fit text inside each plate's visible pill.
            float[] cy = { 360f, 484f, 608f, 726f };   // column nudged up → room for the taller CTA below
            float[] hh = { 116f, 116f, 116f, 92f };
            for (int i = 0; i < OpenerRules.Length; i++)
            {
                var plate = NewSprite("RulePlate" + i, _openerPanel.transform, Sprite("bar-track"));
                plate.type = Image.Type.Sliced;
                plate.color = CobaltDeep;
                AnchorPx(plate.rectTransform, 960f, cy[i], 1360f, hh[i]);
                var rt = NewText("RuleText" + i, plate.transform, OpenerRules[i], 38,
                    TextAnchor.MiddleCenter, TextLight, _body);
                RulePlateTextRect(rt.rectTransform);
                rt.resizeTextForBestFit = true; rt.resizeTextMinSize = 24; rt.resizeTextMaxSize = 40;
            }

            // Green «НАЧАТЬ ЖИЗНЬ (Enter)» button (S1) — two lines, text inside the plate's visible pill.
            var start = NewSprite("StartPlate", _openerPanel.transform, Sprite("plate-yes"));
            start.type = Image.Type.Sliced;
            AnchorPx(start.rectTransform, 960f, 900f, 600f, 210f);   // taller pill + comfortable bottom margin
            var startText = NewText("StartText", start.transform,
                "НАЧАТЬ ЖИЗНЬ\n(Enter)", 40, TextAnchor.MiddleCenter, Ink, _display);
            Inset(startText.rectTransform, 68f);   // text rect well INSIDE the visible pill (55px 9-slice) → padding all sides
            startText.resizeTextForBestFit = true; startText.resizeTextMinSize = 26; startText.resizeTextMaxSize = 40;
            startText.verticalOverflow = VerticalWrapMode.Truncate;  // best-fit now honours HEIGHT → both rows fit the pill
            // No DisplayFx: dark Ink text on the green pill needs no dark outline (it muddies it to a blob).
        }

        private void BuildGamePanel(Transform parent)
        {
            _gamePanel = NewGroup("Game", parent);

            // ---- HUD row (styleframe-03 / C2 / INDEX anchors, 1920×1080, y from top) ----
            // A dedicated container so the row can be enumerated (no stray/placeholder Image) and so the
            // whole row skins/moves as one. All row widgets are pixel-anchored via AnchorPx.
            _hudRow = NewGroup("HudRow", _gamePanel.transform);

            // Возраст — синий квадрат-стикер ~x58 y30 w200 h224, «ВОЗРАСТ» сверху + крупная цифра.
            // Nudged right of x46 so neither the sticker nor its caption clip the left screen edge.
            _ageBadge = NewSprite("AgeBadge", _hudRow.transform, Sprite("age-badge")).gameObject;
            AnchorPx(_ageBadge.GetComponent<RectTransform>(), 158f, 142f, 200f, 224f);
            var ageLbl = NewText("AgeLbl", _ageBadge.transform, "ВОЗРАСТ", 18, TextAnchor.MiddleCenter, Color.white, _display);
            Anchor(ageLbl.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(172, 30));
            ageLbl.resizeTextForBestFit = true; ageLbl.resizeTextMinSize = 10; ageLbl.resizeTextMaxSize = 18;
            _ageText = NewText("AgeText", _ageBadge.transform, "0", 78, TextAnchor.MiddleCenter, Color.white, _display);
            Anchor(_ageText.rectTransform, new Vector2(0.5f, 0.36f), new Vector2(190, 120));
            DisplayFx(_ageText);

            // Деньги — СИНЯЯ пилюля (кобальт) x270 y30 w330 h88; «₽ N» белым; подпись «ДЕНЬГИ ×N» ПОД пилюлей.
            _moneyPill = NewSprite("MoneyPill", _hudRow.transform, Sprite("money-pill")).gameObject;
            var moneyImg = _moneyPill.GetComponent<Image>();
            moneyImg.type = Image.Type.Sliced;
            moneyImg.color = Cobalt;                          // white sprite → cobalt pill (design gate)
            AnchorPx(_moneyPill.GetComponent<RectTransform>(), 435f, 74f, 330f, 88f);
            // «₽ N» crisp white on the cobalt pill (no dark outline/shadow — it dulls the value to gray).
            _moneyText = NewText("MoneyText", _moneyPill.transform, "₽ 0", 40, TextAnchor.MiddleCenter, Color.white, _body);
            Inset(_moneyText.rectTransform, 18f);
            // Caption UNDER the pill: «ДЕНЬГИ ×N» dark-blue, with the coin ◎ following it (INDEX order).
            // The coin stays a direct child of the pill (P0-sprite test path) but sits in the caption row.
            _moneyLabel = NewText("MoneyLabel", _hudRow.transform, "ДЕНЬГИ", 24, TextAnchor.MiddleLeft, CobaltDeep, _display);
            AnchorPx(_moneyLabel.rectTransform, 388f, 150f, 232f, 32f);
            var coin = NewSprite("Coin", _moneyPill.transform, Sprite("icon-coin"));
            AnchorPx(coin.rectTransform, 478f, 150f, 26f, 26f);

            // Здоровье / Энергия — белые капсулы w290 h72 с иконкой-спрайтом + бар, подпись СНИЗУ.
            _healthGroup = BuildBar("HealthGroup", 771f, 66f, "ЗДОРОВЬЕ", "icon-heart",
                "bar-health-fill", out _healthFill, out _healthLabel);
            _energyGroup = BuildBar("EnergyGroup", 1085f, 66f, "ЭНЕРГИЯ", "icon-lightning",
                "bar-energy-fill", out _energyFill, out _energyLabel);

            // Отношения — белая капсула w380 h72 с зона-баром + маркер, подпись снизу.
            BuildBalancer(1444f, 66f);

            // Кнопка-ребёнок — своя зона верх-право (1850,88), НЕ поверх плашек.
            BuildChildButton(1850f, 88f);

            // ---- Timer ring — в ЗАЗОРЕ под HUD (центр x960 y250), НЕ поверх энергии/шкал ----
            // Layered to match the mockup: white outline (back) · cobalt base ring · red arc (=remaining) ·
            // cobalt centre disc · white number on top.
            var ringGroup = NewGroup("Timer", _gamePanel.transform);
            _timerGroup = ringGroup;
            AnchorPx(ringGroup.GetComponent<RectTransform>(), 960f, 250f, 180f, 180f);
            var ringOutline = NewSprite("RingOutline", ringGroup.transform, Sprite("timer-ring-track"));
            Stretch(ringOutline.rectTransform);
            ringOutline.color = Color.white;                       // white outer outline
            var ringTrack = NewSprite("RingTrack", ringGroup.transform, Sprite("timer-ring-track"));
            Anchor(ringTrack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(162, 162));
            ringTrack.color = Cobalt;                              // blue base ring inside the outline
            _timerFill = NewSprite("RingFill", ringGroup.transform, Sprite("timer-ring"));
            Anchor(_timerFill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(162, 162));
            _timerFill.type = Image.Type.Filled;
            _timerFill.fillMethod = Image.FillMethod.Radial360;
            _timerFill.fillOrigin = (int)Image.Origin360.Top;
            _timerFill.fillClockwise = false;
            _timerFill.fillAmount = 1f;
            _timerFill.color = TimerRed;                           // red arc = remaining time
            var ringCenter = NewSprite("RingCenter", ringGroup.transform, Sprite("marquee-bulb"));
            Anchor(ringCenter.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(112, 112));
            ringCenter.color = Cobalt;                             // cobalt centre disc behind the number
            _timerText = NewText("TimerText", ringGroup.transform, "5", 56, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(_timerText.rectTransform);
            DisplayFx(_timerText);

            // ---- Card marquee (centre x960, in the gap below the timer ring) ----
            _cardRoot = NewGroup("Card", _gamePanel.transform).GetComponent<RectTransform>();
            AnchorPx(_cardRoot, 960f, 560f, 940f, 430f);
            _cardFrame = NewSprite("CardFrame", _cardRoot, Sprite("marquee-frame-bulbs"));
            Stretch(_cardFrame.rectTransform);
            _cardText = NewText("CardText", _cardRoot, "", 64, TextAnchor.MiddleCenter, Color.white, _display);
            Inset(_cardText.rectTransform, 120f);
            _cardText.resizeTextForBestFit = true;   // auto-shrink long questions to fit the marquee
            _cardText.resizeTextMinSize = 30;
            _cardText.resizeTextMaxSize = 64;
            // Truncate (not the NewText default Overflow) so best-fit honours HEIGHT too: a long question (the
            // deck's longest is 67 chars) otherwise rendered too big and spilled above the top bulbs / below the
            // bottom edge of the marquee. With Truncate it shrinks to fit inside the card (design-gate S15 fix).
            _cardText.verticalOverflow = VerticalWrapMode.Truncate;
            DisplayFx(_cardText);

            // ---- BLOCK$ (S10): dim veil over the card + red block-tag banner (hidden by default) ----
            // No separate dim veil: the card is dimmed by tinting _cardFrame directly (SetCardBlockedDim) so
            // the darkening follows the marquee's exact rounded silhouette — a rounded-rect overlay still showed
            // straight edges cutting across the sunburst rays (design-gate S10 fix).
            // Rounded red banner (bar-track 9-slice tinted red, navy-outlined white text) low on the card so
            // the dimmed «Пора подлечиться!» question still reads above it (S10). One sentence-case line.
            var blockBannerImg = NewSprite("BlockBanner", _cardRoot, Sprite("bar-track"));
            blockBannerImg.type = Image.Type.Sliced;
            blockBannerImg.color = new Color(0.90f, 0.18f, 0.14f);   // punchy saturated red (S10 banner)
            _blockBanner = blockBannerImg.gameObject;
            Anchor(blockBannerImg.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(900, 132));
            var blockTxt = NewText("BlockText", _blockBanner.transform,
                "Как жаль, у вас нет денег на это!", 40, TextAnchor.MiddleCenter, Color.white, _display);
            blockTxt.horizontalOverflow = HorizontalWrapMode.Overflow;   // single line, best-fit shrinks to width
            Inset(blockTxt.rectTransform, 40f);
            blockTxt.resizeTextForBestFit = true; blockTxt.resizeTextMinSize = 20; blockTxt.resizeTextMaxSize = 44;
            DisplayFx(blockTxt);
            _blockBanner.SetActive(false);

            // ---- BLOCK$ price sub-line (S10): the required amount, on any BLOCK$-priced card ----
            // A dark rounded plate (bar-track 9-slice, tinted Ink) BEHIND the text, sat just BELOW the
            // card so it clears the bottom bulb ring — the S10 dark block-tag: light text on dark, never
            // the forbidden «gold on yellow». Added AFTER the veil so it reads in the blocked state too.
            // Plate is created first (lower sibling index → drawn behind the text). Both are sized to the
            // text and shown/hidden together in ApplyPriceLabel.
            // «СТОИТ N ₽» (gold) when affordable · «НУЖНО N ₽» (light) when blocked.
            _cardPricePlate = NewSprite("CardPricePlate", _cardRoot, Sprite("bar-track"));
            _cardPricePlate.type = Image.Type.Sliced;
            _cardPricePlate.color = Ink;                     // dark navy plate (S10 block-tag)
            Anchor(_cardPricePlate.rectTransform, new Vector2(0.5f, -0.10f), new Vector2(360, 88));
            _cardPriceText = NewText("CardPrice", _cardRoot, "", 40, TextAnchor.MiddleCenter, Bulb, _body);
            Anchor(_cardPriceText.rectTransform, new Vector2(0.5f, -0.10f), new Vector2(820, 88));
            DisplayFx(_cardPriceText);
            _cardPricePlate.gameObject.SetActive(false);
            _cardPriceText.gameObject.SetActive(false);

            // ---- Answer plates (bottom) ----
            _yesPlate = NewSprite("YesPlate", _gamePanel.transform, Sprite("plate-yes"));
            _yesPlate.type = Image.Type.Sliced;
            _yesRect = _yesPlate.rectTransform;
            AnchorPx(_yesRect, 610f, 940f, 420f, 190f);
            _yesRect.localRotation = Quaternion.Euler(0, 0, YesTilt);
            var yesText = NewText("YesText", _yesPlate.transform, "ДА", 64, TextAnchor.MiddleCenter, Ink, _display);
            PlateTextRect(yesText.rectTransform);   // inset onto the visible plate (clears the baked shadow)
            yesText.resizeTextForBestFit = true; yesText.resizeTextMinSize = 26; yesText.resizeTextMaxSize = 60;
            DisplayFx(yesText);
            _yesPlateText = yesText;

            _noPlate = NewSprite("NoPlate", _gamePanel.transform, Sprite("plate-no"));
            _noPlate.type = Image.Type.Sliced;
            _noRect = _noPlate.rectTransform;
            AnchorPx(_noRect, 1310f, 940f, 470f, 190f);
            _noRect.localRotation = Quaternion.Euler(0, 0, NoTilt);
            var noText = NewText("NoText", _noPlate.transform, "СПАСИБО,\nНЕ НАДО", 46, TextAnchor.MiddleCenter, Color.white, _display);
            PlateTextRect(noText.rectTransform);   // inset onto the visible plate (clears the baked shadow)
            noText.resizeTextForBestFit = true; noText.resizeTextMinSize = 22; noText.resizeTextMaxSize = 40;
            DisplayFx(noText);
            _noPlateText = noText;

            // ---- Burnout state (S7): full-screen dim-cobalt sunburst takeover, shown while Game.Burnout ----
            // An OPAQUE deep-cobalt backing hides the show; a cobalt-tinted sunburst sprite over it paints the
            // muted two-tone cobalt rays of the S7 mockup. Big yellow «ВЫГОРАНИЕ!» (red kant) centred + a
            // white subtitle below, both fully on-screen.
            var burnBack = NewSolid("BurnoutPlate", _gamePanel.transform, CobaltDeep);
            Stretch(burnBack.rectTransform);
            _burnoutPlate = burnBack.gameObject;
            var burnRays = NewSprite("BurnoutRays", _burnoutPlate.transform, Sprite("sunburst-bg"));
            Stretch(burnRays.rectTransform);
            burnRays.color = new Color(Cobalt.r, Cobalt.g, Cobalt.b, 0.45f);   // lighter-cobalt rays over deep cobalt
            var burnoutTxt = NewText("BurnoutText", _burnoutPlate.transform,
                "ВЫГОРАНИЕ!", 150, TextAnchor.MiddleCenter, Energy, _display);
            Anchor(burnoutTxt.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(1560, 260));
            burnoutTxt.resizeTextForBestFit = true; burnoutTxt.resizeTextMinSize = 60; burnoutTxt.resizeTextMaxSize = 130;
            BurnoutTitleFx(burnoutTxt);
            var burnoutSub = NewText("BurnoutSubtitle", _burnoutPlate.transform,
                "крутите деньги — идёт туго • подышите рычагом", 40, TextAnchor.MiddleCenter, Color.white, _display);
            Anchor(burnoutSub.rectTransform, new Vector2(0.5f, 0.34f), new Vector2(1500, 96));
            burnoutSub.resizeTextForBestFit = true; burnoutSub.resizeTextMinSize = 24; burnoutSub.resizeTextMaxSize = 44;
            DisplayFx(burnoutSub);
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
            // ---- Counter badge (S6): top-right DARK rounded badge — «МЫСЛЬ N/5» + «ПРОВАЛОВ: K» ----
            // bar-track 9-slice tinted Ink gives the rounded dark plate; text stays fully inside via Inset.
            var infoImg = NewSprite("CrisisInfo", _gamePanel.transform, Sprite("bar-track"));
            infoImg.type = Image.Type.Sliced;
            infoImg.color = Ink;                    // dark navy badge — readable over the sunburst
            _crisisInfo = infoImg.gameObject;
            AnchorPx(infoImg.rectTransform, 1690f, 100f, 380f, 112f);
            _crisisInfoText = NewText("CrisisInfoText", _crisisInfo.transform,
                "", 32, TextAnchor.MiddleCenter, TextLight, _display);
            Inset(_crisisInfoText.rectTransform, 18f);
            DisplayFx(_crisisInfoText);
            _crisisInfo.SetActive(false);

            // ---- Impulse warning (S13): dark pill «молчание = ДА» with a DRAWN mute icon (never a glyph) ----
            var warnImg = NewSprite("ImpulseWarning", _gamePanel.transform, Sprite("bar-track"));
            warnImg.type = Image.Type.Sliced;
            warnImg.color = Ink;
            _impulseWarning = warnImg.gameObject;
            AnchorPx(warnImg.rectTransform, 940f, 700f, 380f, 84f);
            var mute = NewSprite("MuteIcon", _impulseWarning.transform, MakeMuteSprite());
            Anchor(mute.rectTransform, new Vector2(0.14f, 0.5f), new Vector2(52, 52));
            var warn = NewText("ImpulseWarnText", _impulseWarning.transform,
                "молчание = ДА", 34, TextAnchor.MiddleCenter, Color.white, _display);
            var wrt = warn.rectTransform;
            wrt.anchorMin = Vector2.zero; wrt.anchorMax = Vector2.one;
            wrt.offsetMin = new Vector2(78f, 8f);   // clear the mute icon on the left
            wrt.offsetMax = new Vector2(-18f, -8f);
            DisplayFx(warn);
            _impulseWarning.SetActive(false);
        }

        // A small DRAWN «mute» icon (crossed speaker) baked to a runtime texture — a real sprite, never a
        // font glyph/tofu (S13 «молчание = ДА»). Deterministic; a white speaker body+cone with a red slash.
        private static UnityEngine.Sprite MakeMuteSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            var clear = new Color32(0, 0, 0, 0);
            var white = new Color32(245, 248, 255, 255);
            var red = new Color32(232, 68, 58, 255);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            void Set(int x, int y, Color32 c) { if (x >= 0 && x < n && y >= 0 && y < n) px[y * n + x] = c; }
            // speaker body box
            for (int x = 12; x <= 26; x++)
                for (int y = 24; y <= 40; y++) Set(x, y, white);
            // speaker cone (widens toward the mouth)
            for (int x = 26; x <= 42; x++)
            {
                float hh = 6f + (x - 26) / 16f * 16f;
                int y0 = Mathf.RoundToInt(32 - hh), y1 = Mathf.RoundToInt(32 + hh);
                for (int y = y0; y <= y1; y++) Set(x, y, white);
            }
            // mute slash (red) — a thick 45° diagonal across the whole icon
            for (int x = 8; x <= 54; x++)
                for (int t = -3; t <= 3; t++) { Set(x, x + t, red); Set(x + 1, x + t, red); }
            tex.SetPixels32(px);
            tex.Apply();
            return UnityEngine.Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        // Host speech bubble (S3, yellow bubble.png 9-slice) + rubric banner (S4). Both start hidden.
        private void BuildHostReactions()
        {
            // ---- Speech bubble (S3): a corner bubble, right of the card so it never covers it ----
            var bubbleImg = NewSprite("HostBubble", _gamePanel.transform, Sprite("bubble"));
            bubbleImg.type = Image.Type.Sliced;   // 9-slice border 70/120/70/70 (import already set)
            bubbleImg.color = Energy;             // saturated bulb-gold (#f8d24c) per C1/S3 — not pale cream
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
            // Asymmetric inset: clear the end flourishes horizontally (150) but only a slim top/bottom pad
            // (24) — a uniform 150 inset on the 240-tall band collapses the text rect to a negative height,
            // which is why the band rendered EMPTY (the founder «пустой баннер» bug). Best-fit so a long
            // rubric («КРИЗИС СРЕДНЕГО ВОЗРАСТА! БЛИЦ!») wraps/shrinks fully inside the band.
            var banRt = _bannerText.rectTransform;
            banRt.anchorMin = Vector2.zero; banRt.anchorMax = Vector2.one;
            banRt.offsetMin = new Vector2(150f, 24f);
            banRt.offsetMax = new Vector2(-150f, -24f);
            _bannerText.resizeTextForBestFit = true;
            _bannerText.resizeTextMinSize = 28;
            _bannerText.resizeTextMaxSize = 82;
            DisplayFx(_bannerText);
            _bannerRoot.SetActive(false);
        }

        // White capsule (w290 h72) at INDEX anchor: bar-track = the white capsule; a coloured fill sits
        // inside (left-inset to clear the icon-sprite badge); the caps label sits BELOW, dark-blue.
        private GameObject BuildBar(string name, float cx, float cyTop, string label, string icon,
            string fillSprite, out Image fill, out Text labelText)
        {
            var group = NewGroup(name, _hudRow.transform);
            var grt = group.GetComponent<RectTransform>();
            AnchorPx(grt, cx, cyTop, 290f, 72f);

            var track = NewSprite("Track", group.transform, Sprite("bar-track"));
            track.type = Image.Type.Sliced;
            track.color = Color.white;
            Stretch(track.rectTransform);                    // the white capsule = the whole group
            fill = NewSprite("Fill", track.transform, Sprite(fillSprite));
            var fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(64f, 13f);            // leave room for the icon badge on the left
            fr.offsetMax = new Vector2(-16f, -13f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            var ico = NewSprite("Icon", group.transform, Sprite(icon));
            Anchor(ico.rectTransform, new Vector2(0.10f, 0.5f), new Vector2(56, 56));

            labelText = NewText("Label", group.transform, label, 24, TextAnchor.MiddleCenter, CobaltDeep, _display);
            Anchor(labelText.rectTransform, new Vector2(0.5f, -0.36f), new Vector2(290f, 34f));  // BELOW the capsule
            return group;
        }

        // Relationships (w380 h72): white capsule bg + the red/yellow/green/yellow/red zone bar + up/down
        // marker at the value; label «ОТНОШЕНИЯ» below.
        private void BuildBalancer(float cx, float cyTop)
        {
            _balancerGroup = NewGroup("Balancer", _hudRow.transform);
            AnchorPx(_balancerGroup.GetComponent<RectTransform>(), cx, cyTop, 380f, 72f);

            var capsule = NewSprite("Capsule", _balancerGroup.transform, Sprite("bar-track"));
            capsule.type = Image.Type.Sliced;
            capsule.color = Color.white;
            Stretch(capsule.rectTransform);

            _balancerTrackWidth = 320f;
            _balancerTrackImg = NewSprite("Track", _balancerGroup.transform, Sprite("balancer-track"));
            Anchor(_balancerTrackImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(_balancerTrackWidth, 40f));
            _balancerMarkerImg = NewSprite("Marker", _balancerTrackImg.transform, Sprite("balancer-marker"));
            _balancerMarker = _balancerMarkerImg.rectTransform;
            Anchor(_balancerMarker, new Vector2(0.5f, 0.5f), new Vector2(52, 68));

            _relLabel = NewText("Label", _balancerGroup.transform, "ОТНОШЕНИЯ", 24, TextAnchor.MiddleCenter, CobaltDeep, _display);
            Anchor(_relLabel.rectTransform, new Vector2(0.5f, -0.36f), new Vector2(380f, 34f));
        }

        // Child «cabinet button» (S9): no dedicated sprite exists, so this is a placeholder built from
        // marquee-bulb.png (the round lit-bulb art) + a heart — dim/cool while idle, bright gold + pulsing
        // while the flash window is open (driven in Update off Game.ChildFlashing). Just a button, per the
        // mockup — no label, no scale stripe (a red bar there read as a foreign health/danger artifact).
        private void BuildChildButton(float cx, float cyTop)
        {
            _childGroup = NewGroup("Child", _hudRow.transform);
            AnchorPx(_childGroup.GetComponent<RectTransform>(), cx, cyTop, 96f, 96f);

            _childButtonImg = NewSprite("Button", _childGroup.transform, Sprite("marquee-bulb"));
            _childButtonImg.color = CobaltDeep;   // idle (unlit)
            Anchor(_childButtonImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(88, 88));
            var heart = NewSprite("Heart", _childButtonImg.transform, Sprite("icon-heart"));
            Anchor(heart.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(48, 48));

            _childGroup.SetActive(false);
        }

        private void BuildFinale(Transform parent)
        {
            _finalePanel = NewGroup("Finale", parent);

            _finaleTitle = NewText("FinaleTitle", _finalePanel.transform,
                "СПАСИБО ЗА ИГРУ!", 92, TextAnchor.MiddleCenter, Energy, _display);
            AnchorPx(_finaleTitle.rectTransform, 960f, 185f, 1700f, 175f);
            DisplayFx(_finaleTitle);

            // Cause line (S11/S12): yellow, seated on a DARK navy pill. Bare on the multicolour sunburst the
            // thin yellow fill washed out and read as hollow/outline-only; on a solid dark plate the fill is
            // high-contrast and solid. bar-track 9-slice tinted Ink; best-fit so a long phrase never clips.
            var causePlate = NewSprite("CausePlate", _finalePanel.transform, Sprite("bar-track"));
            causePlate.type = Image.Type.Sliced;
            causePlate.color = Ink;
            AnchorPx(causePlate.rectTransform, 960f, 340f, 1300f, 108f);
            _finaleCause = NewText("FinaleCause", causePlate.transform,
                "", 44, TextAnchor.MiddleCenter, Bulb, _body);
            var crt = _finaleCause.rectTransform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(56f, 20f); crt.offsetMax = new Vector2(-56f, -20f);   // inside the pill, padded
            _finaleCause.resizeTextForBestFit = true; _finaleCause.resizeTextMinSize = 26; _finaleCause.resizeTextMaxSize = 46;
            _finaleCause.verticalOverflow = VerticalWrapMode.Truncate;
            DisplayFx(_finaleCause);

            // Glued necrolog story INSIDE a dark navy plate (S11/S12) — never bare on the background.
            // bar-track 9-slice tinted deep cobalt; best-fit shrinks a long (up to 15-line) story to fit the
            // plate's visible pill. FitStoryPlate() then sizes the plate HEIGHT to the content so a short
            // story doesn't float in a huge empty plate (a long story keeps the full clamped height).
            _finaleStoryPlate = NewSprite("StoryBox", _finalePanel.transform, Sprite("bar-track"));
            _finaleStoryPlate.type = Image.Type.Sliced;
            _finaleStoryPlate.color = CobaltDeep;
            AnchorPx(_finaleStoryPlate.rectTransform, 960f, 620f, 1400f, 360f);
            _finaleStory = NewText("FinaleStory", _finaleStoryPlate.transform,
                "", 32, TextAnchor.MiddleCenter, TextLight, _body);
            Inset(_finaleStory.rectTransform, 60f);
            _finaleStory.resizeTextForBestFit = true; _finaleStory.resizeTextMinSize = 18; _finaleStory.resizeTextMaxSize = 34;
            _finaleStory.verticalOverflow = VerticalWrapMode.Truncate;  // best-fit now honours HEIGHT → the worst-case long story shrinks to fit inside the pill instead of spilling past the plate
            DisplayFx(_finaleStory);

            var again = NewSprite("AgainPlate", _finalePanel.transform, Sprite("plate-yes"));
            again.type = Image.Type.Sliced;
            AnchorPx(again.rectTransform, 960f, 936f, 600f, 210f);   // taller pill + comfortable bottom margin
            var againText = NewText("AgainText", again.transform,
                "НАЧАТЬ ЗАНОВО\n(Enter)", 40, TextAnchor.MiddleCenter, Ink, _display);
            Inset(againText.rectTransform, 68f);   // text rect well INSIDE the visible pill (55px 9-slice) → padding all sides
            againText.resizeTextForBestFit = true; againText.resizeTextMinSize = 26; againText.resizeTextMaxSize = 40;
            againText.verticalOverflow = VerticalWrapMode.Truncate;  // best-fit now honours HEIGHT → both rows fit the pill
            // No DisplayFx: dark Ink text on the green pill needs no dark outline (it muddies it to a blob).
        }

        // S5 tutorial: full-screen dim + a yellow modal in the Host's tone + «ПОНЯТНО» plate.
        private void BuildTutorialOverlay(Transform parent)
        {
            _tutorialOverlay = NewSolid("TutorialOverlay", parent, new Color(0.02f, 0.03f, 0.10f, 0.78f)).gameObject;
            Stretch(_tutorialOverlay.GetComponent<RectTransform>());

            // Solid yellow card (bar-track 9-slice is a filled rounded rect — marquee-frame is a HOLLOW frame
            // whose transparent centre let the dark veil bleed through and killed the dark text's contrast).
            var modal = NewSprite("Modal", _tutorialOverlay.transform, Sprite("bar-track"));
            modal.type = Image.Type.Sliced;
            modal.color = Bulb;   // жёлтая карточка (тон Ведущего)
            Anchor(modal.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(1280, 600));
            _tutorialModal = modal;

            // Hint body (S5): the title rides as the first line of each hint constant. Fully inside the
            // modal's visible pill with margins; best-fit shrinks a long hint to fit above the button.
            _tutorialText = NewText("TutBody", modal.transform, MoneyTutorialText, 40, TextAnchor.MiddleCenter, Ink, _body);
            var trt = _tutorialText.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.34f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(120f, 20f); trt.offsetMax = new Vector2(-120f, -100f);
            _tutorialText.resizeTextForBestFit = true; _tutorialText.resizeTextMinSize = 26; _tutorialText.resizeTextMaxSize = 44;

            // Blue «ПОНЯТНО — Enter» button (S5) with a clear bottom margin inside the modal (NOT flush to
            // the edge). «Enter» is spelled out (founder Gate-2): Space is the crank and must never dismiss,
            // so the dismiss key stays discoverable on the button. bar-track 9-slice tinted cobalt.
            var plate = NewSprite("GotItPlate", modal.transform, Sprite("bar-track"));
            plate.type = Image.Type.Sliced;
            plate.color = Cobalt;
            Anchor(plate.rectTransform, new Vector2(0.5f, 0.16f), new Vector2(470, 116));
            _tutorialButton = plate;
            var plateTxt = NewText("GotItText", plate.transform, "ПОНЯТНО — Enter", 32, TextAnchor.MiddleCenter, Color.white, _display);
            Inset(plateTxt.rectTransform, 30f);
            plateTxt.resizeTextForBestFit = true; plateTxt.resizeTextMinSize = 22; plateTxt.resizeTextMaxSize = 34;
            DisplayFx(plateTxt);
            _tutorialButtonText = plateTxt;

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

            // «СОБРАТЬСЯ» button (S8): a near-white rounded pill low-centre with dark text (monochrome, so it
            // reads on the gray wash). bar-track 9-slice = the filled rounded plate.
            var gatherPlate = NewSprite("DepGatherPlate", _depressionGroup.transform, Sprite("bar-track"));
            gatherPlate.type = Image.Type.Sliced;
            gatherPlate.color = new Color(0.93f, 0.93f, 0.95f);
            AnchorPx(gatherPlate.rectTransform, 960f, 968f, 420f, 116f);
            var label = NewText("DepLabel", gatherPlate.transform,
                "СОБРАТЬСЯ", 60, TextAnchor.MiddleCenter, new Color(0.10f, 0.10f, 0.12f), _display);
            Inset(label.rectTransform, 40f);
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 30; label.resizeTextMaxSize = 60;

            // «нажми в такт пульсу» hint (S8): on a DARK ink plate ABOVE the button so it is clearly READABLE
            // (founder complaint — gray-on-gray was invisible). Light text on a near-opaque dark plate.
            var hintPlate = NewSprite("DepHintPlate", _depressionGroup.transform, Sprite("bar-track"));
            hintPlate.type = Image.Type.Sliced;
            hintPlate.color = new Color(0.08f, 0.08f, 0.10f, 0.96f);
            AnchorPx(hintPlate.rectTransform, 960f, 874f, 500f, 92f);
            var hint = NewText("DepHint", hintPlate.transform,
                "нажми в такт пульсу", 30, TextAnchor.MiddleCenter, new Color(0.96f, 0.96f, 0.98f), _display);
            Inset(hint.rectTransform, 28f);
            hint.resizeTextForBestFit = true; hint.resizeTextMinSize = 20; hint.resizeTextMaxSize = 30;

            // Dim centre pulse — a faint soft dot below the card, revealed only while the hit-window is open.
            _depressionPulse = NewSprite("DepressionPulse", _depressionGroup.transform, Sprite("star-white"));
            _depressionPulse.color = new Color(0.9f, 0.9f, 0.95f, 0.28f);
            Anchor(_depressionPulse.rectTransform, new Vector2(0.5f, 0.40f), new Vector2(150, 150));
            _depressionPulse.gameObject.SetActive(false);

            // No colour-progress pips (S8 mockup has none) — the wash lightening alone reads the recovery.

            _depressionGroup.SetActive(false);
        }

        // A small seeded noise texture used as static «зерно». Deterministic (fixed seed) so it never
        // flickers between builds; stretched full-screen and drawn very faint.
        private static UnityEngine.Sprite MakeGrainSprite()
        {
            const int n = 256;
            // Bilinear (not Point) + a finer tile so the full-screen stretch reads as soft film-grain, NOT the
            // big blocky squares a 128px point-sampled noise produced at 4K (design-gate S8 fix).
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
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
            // Heavier wash (0.92) so the colourful sunburst rays behind are strongly suppressed and the board
            // reads as a monochrome «тёмная полоса», not muted-but-still-coloured bands (design-gate S8 fix).
            _depressionVeil.color = new Color(vc.r, vc.g, vc.b, grayT * 0.92f);   // 5 gray → 0.92, 0 → clear
            _depressionGrain.color = new Color(1f, 1f, 1f, 0.06f * grayT);

            bool pulsing = _game.DepressionPulsing;
            if (_depressionPulse.gameObject.activeSelf != pulsing) _depressionPulse.gameObject.SetActive(pulsing);
            if (pulsing)
            {
                float a = 0.22f + 0.14f * Mathf.Sin(Time.time * 4f);   // slow faint throb
                _depressionPulse.color = new Color(0.9f, 0.9f, 0.95f, a);
                _depressionPulse.rectTransform.localScale = Vector3.one * (1f + 0.10f * Mathf.Sin(Time.time * 4f));
            }
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
            SyncPause();
        }

        private void DismissTutorial()
        {
            if (!_tutorialShowing) return;
            _tutorialShowing = false;
            _tutorialOverlay.SetActive(false);
            SyncPause();
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
            if (!playing) { _bannerTimer.Hide(); _bannerRoot.SetActive(false); SyncPause(); }
            _wasPlaying = playing;

            if (playing)
            {
                UpdateHudValues();
                ApplyAgeGates(_game.Age);
            }
            else if (finale && _game.Necrolog != null)
            {
                RenderFinaleTexts(_game.Necrolog);
            }
        }

        private void OnCardChanged()
        {
            if (_game == null || _game.State != GameState.Playing) return;
            var c = _game.CurrentCard;
            _cardText.text = c != null ? c.Question : "";
            bool blocked = _game.CurrentCardBlocked;
            SetCardBlockedDim(blocked);           // S10: dim the card (frame tint) + red banner when unaffordable
            _blockBanner.SetActive(blocked);
            RefreshPriceLabel();                  // S10: show the required amount on any BLOCK$ card
            // Rubric banner (S4): announce on a TIMELINE milestone; clear it on any non-milestone card
            // (so it never lingers onto the card after the milestone). The bubble is answer-driven and
            // deliberately NOT touched here — it survives this same-frame advance to live out its ~2s.
            if (c != null && c.IsTimeline) ShowBanner(c);
            else _bannerTimer.Hide();
            SyncPause();   // reconcile the beat pause NOW (a non-timeline card ends any prior beat)
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

        // Show an arbitrary rubric-band caption as a brief BLOCKING beat (used by TIMELINE milestone cards
        // and the crisis CR00 banner). The text is set BEFORE the reveal (never an empty band), the game is
        // paused and the card/plates/timer are hidden this same frame, and it auto-advances on the ~1.5s
        // banner clock (ReflectHostReveals) — card appears only once the beat clears (S4/S6). Never both up.
        private void ShowBannerText(string text, bool muted)
        {
            _bannerBand.color = muted ? CobaltDeep : Bulb;
            _bannerText.color = muted ? Muted : Ink;
            _bannerText.text = text;
            _bannerTimer.Show(text);
            _bannerRoot.transform.SetAsLastSibling();   // draw above the (now hidden) card
            SyncPause();                                 // freeze the game for the beat
            ReflectBannerBeat();                         // hide the card/plates/timer immediately
        }

        // Advance both host clocks (real-time) and mirror their visibility onto the widgets. Only visible
        // while Playing; the timers keep their own state so a restart/leaving-play simply hides them.
        private void ReflectHostReveals(bool playing)
        {
            _bubbleTimer.Advance(Time.deltaTime);
            _bannerTimer.Advance(Time.deltaTime);   // when this auto-hides, the banner beat ends
            SyncPause();                            // Game.Paused tracks the beat (+ any tutorial)

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
            if (!hasPrice)
            {
                _cardPriceText.gameObject.SetActive(false);
                _cardPricePlate.gameObject.SetActive(false);
                return;
            }
            int p = Mathf.RoundToInt((float)price);
            _cardPriceText.text = (blocked ? "цена " : "СТОИТ ") + p + " ₽";
            _cardPriceText.color = blocked ? TextLight : Bulb;

            // Size the text rect to its content, then wrap the dark plate around it (with padding) so the
            // plate always fully covers the text — never bare gold/light text on the yellow sunburst.
            float tw = _cardPriceText.preferredWidth;
            float th = _cardPriceText.preferredHeight;
            _cardPriceText.rectTransform.sizeDelta = new Vector2(tw, th);
            _cardPricePlate.rectTransform.sizeDelta = new Vector2(tw + 64f, th + 28f);

            _cardPricePlate.gameObject.SetActive(true);
            _cardPriceText.gameObject.SetActive(true);
        }

        private void UpdateHudValues()
        {
            var s = _game.Scales;
            _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
            _moneyText.text = FormatMoney(_game.Money);
            _moneyLabel.text = FormatMoneyLabel(_game.IncomeMultiplier);
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
            _moneyLabel.gameObject.SetActive(a >= MoneyAge);   // caption tracks the pill (no orphan «ДЕНЬГИ»)
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

        // Money caption under the pill: «ДЕНЬГИ» plus a «×N» factor when the income multiplier is not 1
        // (startup fork / burnout). N is trimmed (×2, ×1.5, ×0.5) so it stays short under the pill.
        private static string FormatMoneyLabel(double multiplier)
        {
            if (System.Math.Abs(multiplier - 1.0) < 0.01) return "ДЕНЬГИ";
            string n = multiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return "ДЕНЬГИ ×" + n;
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

        // Burnout title (S7): yellow letters with a RED kant (outline) + a soft drop shadow (mockup).
        private static void BurnoutTitleFx(Graphic g)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.847f, 0.157f, 0.078f, 1f);   // red outline
            o.effectDistance = new Vector2(5f, -5f);
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.35f);
            sh.effectDistance = new Vector2(0f, -8f);
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

        // Text rect for an answer plate: the plate sprites carry a baked drop-shadow toward the
        // bottom-right, so the visible red/green sits toward the top-left. Inset asymmetrically (more
        // bottom+right) so best-fit text lands fully ON the plate, never spilling onto the background.
        private static void PlateTextRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(30f, 48f);    // left, bottom
            rt.offsetMax = new Vector2(-46f, -26f);  // right, top
        }

        // Text rect for an opener rule plate (bar-track 9-slice): inset well past the rounded corners so the
        // white rule text always lands inside the visible cobalt pill on all four sides.
        private static void RulePlateTextRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(64f, 22f);
            rt.offsetMax = new Vector2(-64f, -22f);
        }

        // Text rect for the ENLARGED blitz plate: inset past the ~55px 9-slice corner so best-fit text lands
        // inside the colored pill (with margin) rather than on the navy frame / off the pill entirely.
        private static void CrisisPlateTextRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(74f, 74f);    // left, bottom (> 55px pill inset + margin)
            rt.offsetMax = new Vector2(-74f, -66f);  // right, top
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

        // Pixel-anchor a rect at a point given in mockup/INDEX coordinates (1920×1080, x from LEFT, y from
        // TOP, at the element CENTRE). Only valid for a child of a full-canvas (stretched) parent —
        // _hudRow / _gamePanel — where an anchor fraction maps straight to a canvas position.
        private static void AnchorPx(RectTransform rt, float cx, float cyTop, float w, float h)
        {
            var a = new Vector2(cx / 1920f, 1f - cyTop / 1080f);
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
