using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ThanksNoThanks
{
    /// <summary>
    /// MonoBehaviour driver for «Спасибо, не надо». Owns the pure <see cref="Game"/>, wires an
    /// <see cref="IInputSource"/> (keyboard by default; a fake can be injected for tests), and
    /// self-builds a rough TV-quiz HUD (16:9, 1920×1080) in Awake so the scene needs no fragile
    /// hand-wired references. Placeholder art — full sprites are deferred.
    /// </summary>
    public sealed class GameDriver : MonoBehaviour
    {
        // Palette from the styleframe / screens design tokens.
        private static readonly Color Page = new(0.063f, 0.090f, 0.200f);      // #101733
        private static readonly Color Cobalt = new(0.184f, 0.329f, 0.784f);    // #2f54c8
        private static readonly Color CobaltDeep = new(0.122f, 0.227f, 0.588f);// #1f3a96
        private static readonly Color Da = new(0.361f, 0.749f, 0.373f);        // #5cbf5f
        private static readonly Color No = new(0.910f, 0.267f, 0.227f);        // #e8443a
        private static readonly Color HealthRed = new(0.898f, 0.263f, 0.231f); // #e5433b
        private static readonly Color Bulb = new(1f, 0.847f, 0.451f);          // #ffd873
        private static readonly Color TextLight = new(0.918f, 0.941f, 1f);     // #eaf0ff
        private static readonly Color Ink = new(0.078f, 0.102f, 0.239f);       // #141a3d

        /// <summary>Optional input injection (tests). Defaults to a KeyboardInputSource in Start.</summary>
        public IInputSource Input;

        private Game _game;
        public Game Game => _game;

        private Font _font;

        // Panels
        private GameObject _openerPanel;
        private GameObject _gamePanel;
        private GameObject _finalePanel;

        // Gameplay widgets
        private Text _ageText;
        private Image _healthFill;
        private Text _healthText;
        private Text _statsText;
        private Text _timerText;
        private Text _cardText;

        // Finale widgets
        private Text _finaleTitle;
        private Text _finaleCause;
        private Text _finaleStory;

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildHud();
            LoadGame();
        }

        private void Start()
        {
            Input ??= gameObject.AddComponent<KeyboardInputSource>();
            Input.Received += _game.HandleInput;
            _game.StateChanged += Refresh;
            _game.CardChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (Input != null) Input.Received -= _game.HandleInput;
            if (_game != null)
            {
                _game.StateChanged -= Refresh;
                _game.CardChanged -= Refresh;
            }
        }

        private void LoadGame()
        {
            var csvAsset = Resources.Load<TextAsset>("scenes");
            List<Card> deck = csvAsset != null
                ? CardLoader.LoadSubset(csvAsset.text, CardLoader.DefaultSubset)
                : new List<Card>();
            if (csvAsset == null)
                Debug.LogError("[ThanksNoThanks] Resources/scenes.csv not found — deck is empty.");
            _game = new Game(deck);
        }

        private void Update()
        {
            if (_game == null) return;
            _game.Tick(Time.deltaTime);
            if (_game.State == GameState.Playing)
            {
                _ageText.text = "ВОЗРАСТ\n" + Mathf.FloorToInt(_game.Age);
                _timerText.text = Mathf.CeilToInt(Mathf.Max(0f, _game.CardTimer)).ToString();
            }
        }

        // ---------------------------------------------------------------- HUD build

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

            // Background
            var bg = NewImage("Background", canvasGo.transform, Page);
            Stretch(bg.rectTransform);

            BuildOpener(canvasGo.transform);
            BuildGamePanel(canvasGo.transform);
            BuildFinale(canvasGo.transform);
        }

        private void BuildOpener(Transform parent)
        {
            _openerPanel = NewPanel("Opener", parent, Cobalt);
            Stretch(_openerPanel.GetComponent<RectTransform>());

            var title = NewText("Title", _openerPanel.transform,
                "«СПАСИБО, НЕ НАДО»", 84, TextAnchor.MiddleCenter, TextLight);
            Anchor(title.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(1500, 140));

            var rules = NewText("Rules", _openerPanel.transform,
                "Проживите целую жизнь за пару минут — в прямом эфире!\n\n" +
                "▸  На каждый вопрос — рычаг: ДА или СПАСИБО, НЕ НАДО.\n" +
                "▸  На раздумья 5 секунд — дальше решаем за вас!\n" +
                "▸  Правильного ответа нет. Есть только ваша жизнь.",
                38, TextAnchor.MiddleCenter, TextLight);
            Anchor(rules.rectTransform, new Vector2(0.5f, 0.52f), new Vector2(1400, 320));

            var start = NewPanel("StartPlate", _openerPanel.transform, Da);
            Anchor(start.GetComponent<RectTransform>(), new Vector2(0.5f, 0.26f), new Vector2(560, 120));
            Outline(start.GetComponent<Image>());
            var startText = NewText("StartText", start.transform,
                "▸ НАЧАТЬ ЖИЗНЬ", 48, TextAnchor.MiddleCenter, Ink);
            Stretch(startText.rectTransform);

            var hint = NewText("Hint", _openerPanel.transform,
                "←  ДА        →  СПАСИБО, НЕ НАДО        Enter / Пробел — НАЧАТЬ",
                28, TextAnchor.MiddleCenter, new Color(0.62f, 0.69f, 0.91f));
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.10f), new Vector2(1500, 60));
        }

        private void BuildGamePanel(Transform parent)
        {
            _gamePanel = NewPanel("Game", parent, new Color(0, 0, 0, 0));
            Stretch(_gamePanel.GetComponent<RectTransform>());

            // Age badge (top-left)
            var badge = NewPanel("AgeBadge", _gamePanel.transform, Cobalt);
            Anchor(badge.GetComponent<RectTransform>(), new Vector2(0.075f, 0.86f), new Vector2(190, 190));
            Outline(badge.GetComponent<Image>(), Color.white, 4f);
            _ageText = NewText("AgeText", badge.transform, "ВОЗРАСТ\n0", 40, TextAnchor.MiddleCenter, Color.white);
            Stretch(_ageText.rectTransform);

            // Health bar (top, right of badge)
            var hpPanel = NewPanel("HealthPanel", _gamePanel.transform, Color.white);
            Anchor(hpPanel.GetComponent<RectTransform>(), new Vector2(0.30f, 0.90f), new Vector2(420, 120));
            _healthText = NewText("HealthLabel", hpPanel.transform, "♥ ЗДОРОВЬЕ", 26, TextAnchor.UpperLeft, CobaltDeep);
            Anchor(_healthText.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(380, 40));
            var barBg = NewImage("HealthBarBg", hpPanel.transform, new Color(0.906f, 0.914f, 0.961f));
            Anchor(barBg.rectTransform, new Vector2(0.5f, 0.30f), new Vector2(380, 44));
            _healthFill = NewImage("HealthFill", barBg.transform, HealthRed);
            var fillRt = _healthFill.rectTransform;
            fillRt.anchorMin = new Vector2(0, 0);
            fillRt.anchorMax = new Vector2(1, 1);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _healthFill.type = Image.Type.Filled;
            _healthFill.fillMethod = Image.FillMethod.Horizontal;
            _healthFill.fillAmount = 1f;

            // Passive scales readout (observability for the still-hidden scales)
            _statsText = NewText("Stats", _gamePanel.transform,
                "", 24, TextAnchor.UpperRight, TextLight);
            Anchor(_statsText.rectTransform, new Vector2(0.82f, 0.86f), new Vector2(460, 190));

            // Timer (top center)
            var timerPanel = NewPanel("Timer", _gamePanel.transform, No);
            Anchor(timerPanel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.90f), new Vector2(120, 120));
            Outline(timerPanel.GetComponent<Image>(), Color.white, 4f);
            _timerText = NewText("TimerText", timerPanel.transform, "5", 60, TextAnchor.MiddleCenter, Color.white);
            Stretch(_timerText.rectTransform);

            // Card marquee (center)
            var card = NewPanel("Card", _gamePanel.transform, Cobalt);
            Anchor(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.50f), new Vector2(1000, 420));
            Outline(card.GetComponent<Image>(), Color.white, 10f);
            var inner = NewImage("Bulbs", card.transform, new Color(1f, 0.847f, 0.451f, 0.12f));
            Inset(inner.rectTransform, 22f);
            _cardText = NewText("CardText", card.transform, "", 64, TextAnchor.MiddleCenter, Color.white);
            Inset(_cardText.rectTransform, 60f);

            // Answer plates (bottom)
            var yes = NewPanel("YesPlate", _gamePanel.transform, Da);
            Anchor(yes.GetComponent<RectTransform>(), new Vector2(0.30f, 0.14f), new Vector2(430, 150));
            Outline(yes.GetComponent<Image>());
            var yesText = NewText("YesText", yes.transform, "ДА", 60, TextAnchor.MiddleCenter, Ink);
            Stretch(yesText.rectTransform);

            var no = NewPanel("NoPlate", _gamePanel.transform, No);
            Anchor(no.GetComponent<RectTransform>(), new Vector2(0.70f, 0.14f), new Vector2(430, 150));
            Outline(no.GetComponent<Image>());
            var noText = NewText("NoText", no.transform, "СПАСИБО,\nНЕ НАДО", 44, TextAnchor.MiddleCenter, Color.white);
            Stretch(noText.rectTransform);
        }

        private void BuildFinale(Transform parent)
        {
            _finalePanel = NewPanel("Finale", parent, Cobalt);
            Stretch(_finalePanel.GetComponent<RectTransform>());

            _finaleTitle = NewText("FinaleTitle", _finalePanel.transform,
                "СПАСИБО ЗА ИГРУ!", 90, TextAnchor.MiddleCenter, Bulb);
            Anchor(_finaleTitle.rectTransform, new Vector2(0.5f, 0.80f), new Vector2(1600, 150));

            _finaleCause = NewText("FinaleCause", _finalePanel.transform,
                "", 44, TextAnchor.MiddleCenter, Color.white);
            Anchor(_finaleCause.rectTransform, new Vector2(0.5f, 0.63f), new Vector2(1500, 80));

            _finaleStory = NewText("FinaleStory", _finalePanel.transform,
                "", 34, TextAnchor.UpperCenter, TextLight);
            Anchor(_finaleStory.rectTransform, new Vector2(0.5f, 0.38f), new Vector2(1300, 380));

            var again = NewPanel("AgainPlate", _finalePanel.transform, Da);
            Anchor(again.GetComponent<RectTransform>(), new Vector2(0.5f, 0.10f), new Vector2(560, 110));
            Outline(again.GetComponent<Image>());
            var againText = NewText("AgainText", again.transform,
                "▸ НАЧАТЬ ЗАНОВО", 44, TextAnchor.MiddleCenter, Ink);
            Stretch(againText.rectTransform);
        }

        // ---------------------------------------------------------------- refresh

        private void Refresh()
        {
            if (_game == null) return;
            bool opener = _game.State == GameState.Opener;
            bool playing = _game.State == GameState.Playing;
            bool finale = _game.State == GameState.Finale;

            _openerPanel.SetActive(opener);
            _gamePanel.SetActive(playing);
            _finalePanel.SetActive(finale);

            if (playing)
            {
                var c = _game.CurrentCard;
                _cardText.text = c != null ? c.Question : "";
                _ageText.text = "ВОЗРАСТ\n" + Mathf.FloorToInt(_game.Age);
                _timerText.text = Mathf.CeilToInt(Mathf.Max(0f, _game.CardTimer)).ToString();
                _healthFill.fillAmount = Mathf.Clamp01(_game.Scales.Health / 100f);
                _healthText.text = "♥ ЗДОРОВЬЕ  " + _game.Scales.Health;
                _statsText.text =
                    "⚡ Энергия  " + _game.Scales.Energy + "\n" +
                    "₽ Деньги  " + _game.Scales.Money + "\n" +
                    "♾ Отношения  " + _game.Scales.Relationships;
            }
            else if (finale && _game.Necrolog != null)
            {
                var n = _game.Necrolog;
                _finaleTitle.text = n.Title;
                _finaleCause.text = n.CauseLine;
                _finaleStory.text = n.ComposeStory();
            }
        }

        // ---------------------------------------------------------------- UI helpers

        private Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private GameObject NewPanel(string name, Transform parent, Color color)
        {
            var img = NewImage(name, parent, color);
            return img.gameObject;
        }

        private Text NewText(string name, Transform parent, string content, int size,
            TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
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

        // Anchor a rect at a normalized screen point with a fixed pixel size.
        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        private static void Outline(Graphic g, Color? color = null, float dist = 6f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color ?? new Color(0.078f, 0.102f, 0.239f, 1f);
            o.effectDistance = new Vector2(dist, -dist);
        }
    }
}
