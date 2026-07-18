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
        private float _balancerTrackWidth;

        // Card
        private RectTransform _cardRoot;
        private Image _cardFrame;
        private Text _cardText;

        // Answer plates
        private Image _yesPlate;
        private Image _noPlate;
        private RectTransform _yesRect;
        private RectTransform _noRect;
        private const float YesTilt = -2f;
        private const float NoTilt = 2f;

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

        // Tutorial overlay (S5): dimmed bg + yellow modal + «ПОНЯТНО»; freezes the game while up.
        private GameObject _tutorialOverlay;
        private Text _tutorialText;
        private bool _tutorialShowing;
        private bool _moneyTutorialSeen;   // one-shot per life; reset on a fresh life
        private bool _wasPlaying;

        private Coroutine _cardAnim;
        private Coroutine _moneyPulse;

        // Income cap (~5/s): applied ONLY on the gameplay-crank branch in OnInput — Space-as-CONFIRM
        // (opener/finale/tutorial) bypasses it entirely, so a recent crank can never eat a confirm.
        private readonly MoneyTickThrottle _crankCap = new();

        private const string MoneyTutorialText =
            "ТЕПЕРЬ У ВАС ЕСТЬ РАБОТА!\n\n" +
            "Крутите ПРОБЕЛ — и деньги потекут. Но жизнь идёт своим чередом:\n" +
            "содержать себя стоит денег каждую секунду.\n\n" +
            "Рук всего две — крутить и отвечать придётся разом.";

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

        /// <summary>Test hook: run the age-gated HUD visibility for an arbitrary age.</summary>
        public void DebugApplyAgeGates(float age) => ApplyAgeGates(age);

        private void Awake()
        {
            _display = Resources.Load<Font>("Fonts/RussoOne");
            _body = Resources.Load<Font>("Fonts/Rubik");
            if (_display == null) _display = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_body == null) _body = _display;
            BuildHud();
            LoadGame();
        }

        private void Start()
        {
            Input ??= gameObject.AddComponent<KeyboardInputSource>();
            Input.Received += OnInput;
            Input.Received += OnInputFx;
            _game.StateChanged += Refresh;
            _game.CardChanged += OnCardChanged;
            _game.MoneyOpened += OnMoneyOpened;
            Refresh();
        }

        private void OnDestroy()
        {
            if (Input != null)
            {
                Input.Received -= OnInput;
                Input.Received -= OnInputFx;
            }
            if (_game != null)
            {
                _game.StateChanged -= Refresh;
                _game.CardChanged -= OnCardChanged;
                _game.MoneyOpened -= OnMoneyOpened;
            }
        }

        /// <summary>
        /// Input funnel. The ONLY place the state-dependent Space re-map lives (contract): a Space crank
        /// (MONEY_TICK) doubles as CONFIRM in the Opener/Finale and while the tutorial overlay is up —
        /// those paths are INSTANT and unthrottled (the source never swallows a discrete keydown).
        /// The ~5/s income cap applies only when the tick actually cranks money during gameplay.
        /// While the overlay is up, all other input is swallowed. Pure <see cref="Game"/> never sees
        /// any of this — it gets a clean semantic event.
        /// </summary>
        private void OnInput(GameInput input)
        {
            if (_tutorialShowing)
            {
                // Only a FRESH press (or Enter) dismisses; held-Space repeats are inert here —
                // holding through money-open must not insta-dismiss the hint.
                if (input == GameInput.Confirm || input == GameInput.MoneyTick) DismissTutorial();
                return;
            }

            if (input == GameInput.MoneyTick || input == GameInput.MoneyTickRepeat)
            {
                if (_game.State != GameState.Playing)
                {
                    // Fresh Space = CONFIRM on opener/finale — instant. Autorepeat is inert: holding
                    // Space through a life ending must never auto-confirm screens into a new life.
                    if (input == GameInput.MoneyTick)
                        _game.HandleInput(GameInput.Confirm);
                    return;
                }
                if (!_crankCap.TryAccept()) return;         // income cap (anti-mashgun) — gameplay only
                _game.HandleInput(GameInput.MoneyTick);     // Game sees only the semantic crank event
                if (_game.MoneyOpen && isActiveAndEnabled)  // pill pulse on each PAYING tick
                {
                    if (_moneyPulse != null) StopCoroutine(_moneyPulse);
                    _moneyPulse = StartCoroutine(PulseMoney());
                }
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
            if (_game == null) return;
            _crankCap.Advance(Time.deltaTime);   // deterministic clock for the income cap
            _game.Tick(Time.deltaTime);
            if (_game.State == GameState.Playing)
            {
                _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
                _moneyText.text = FormatMoney(_game.Money);   // live: ticks up on crank, drains down
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
                "▸ НАЧАТЬ ЖИЗНЬ", 46, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(startText.rectTransform);

            var hint = NewText("Hint", _openerPanel.transform,
                "←  ДА          →  СПАСИБО, НЕ НАДО          Enter / Пробел — НАЧАТЬ",
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

            // ---- Answer plates (bottom) ----
            _yesPlate = NewSprite("YesPlate", _gamePanel.transform, Sprite("plate-yes"));
            _yesPlate.type = Image.Type.Sliced;
            _yesRect = _yesPlate.rectTransform;
            Anchor(_yesRect, new Vector2(0.31f, 0.145f), new Vector2(380, 220));
            _yesRect.localRotation = Quaternion.Euler(0, 0, YesTilt);
            var yesText = NewText("YesText", _yesPlate.transform, "ДА", 64, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(yesText.rectTransform);
            DisplayFx(yesText);

            _noPlate = NewSprite("NoPlate", _gamePanel.transform, Sprite("plate-no"));
            _noPlate.type = Image.Type.Sliced;
            _noRect = _noPlate.rectTransform;
            Anchor(_noRect, new Vector2(0.69f, 0.145f), new Vector2(380, 220));
            _noRect.localRotation = Quaternion.Euler(0, 0, NoTilt);
            var noText = NewText("NoText", _noPlate.transform, "СПАСИБО,\nНЕ НАДО", 46, TextAnchor.MiddleCenter, Color.white, _display);
            Stretch(noText.rectTransform);
            DisplayFx(noText);
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
            var track = NewSprite("Track", _balancerGroup.transform, Sprite("balancer-track"));
            Anchor(track.rectTransform, new Vector2(0.5f, 0.34f), new Vector2(_balancerTrackWidth, 46));
            _balancerMarker = NewSprite("Marker", track.transform, Sprite("balancer-marker")).rectTransform;
            Anchor(_balancerMarker, new Vector2(0.5f, 0.5f), new Vector2(58, 78));
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
                "▸ НАЧАТЬ ЗАНОВО", 44, TextAnchor.MiddleCenter, Ink, _display);
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

            var plate = NewSprite("GotItPlate", modal.transform, Sprite("plate-yes"));
            plate.type = Image.Type.Sliced;
            Anchor(plate.rectTransform, new Vector2(0.5f, 0.14f), new Vector2(420, 120));
            var plateTxt = NewText("GotItText", plate.transform, "ПОНЯТНО ▸", 40, TextAnchor.MiddleCenter, Ink, _display);
            Stretch(plateTxt.rectTransform);

            _tutorialOverlay.SetActive(false);
        }

        private void OnMoneyOpened()
        {
            if (_moneyTutorialSeen || _tutorialShowing) return;
            _tutorialShowing = true;
            _tutorialText.text = MoneyTutorialText;
            _tutorialOverlay.transform.SetAsLastSibling();
            _tutorialOverlay.SetActive(true);
            _game.Paused = true;   // freeze age, drains, cost-of-living and the card timer while the hint is up
        }

        private void DismissTutorial()
        {
            if (!_tutorialShowing) return;
            _tutorialShowing = false;
            _moneyTutorialSeen = true;
            _tutorialOverlay.SetActive(false);
            _game.Paused = false;
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

            // Fresh life → the money tutorial is armed again and any leftover overlay is cleared.
            if (playing && !_wasPlaying)
            {
                _moneyTutorialSeen = false;
                _tutorialShowing = false;
                _tutorialOverlay.SetActive(false);
                _game.Paused = false;
                _crankCap.Reset();
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
            UpdateHudValues();
            ApplyAgeGates(_game.Age);
            if (c != null && isActiveAndEnabled)
            {
                if (_cardAnim != null) StopCoroutine(_cardAnim);
                _cardAnim = StartCoroutine(CardEntry());
            }
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
            _balancerGroup.SetActive(a >= RelationshipsAge);
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
