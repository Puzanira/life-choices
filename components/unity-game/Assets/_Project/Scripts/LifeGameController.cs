using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LifeChoices
{
    /// <summary>
    /// The playable driver. Wraps the pure <see cref="LifeGame"/> model with a
    /// real-time decision timer, keyboard input (→ да / ← нет) and a self-built
    /// legacy-uGUI HUD + obituary panel. Builds its own UI in Awake, so the
    /// scene needs only this component on a GameObject (no fragile references).
    /// </summary>
    public sealed class LifeGameController : MonoBehaviour
    {
        [Tooltip("Fixed RNG seed for deterministic runs. Leave -1 for random.")]
        [SerializeField] private int seed = -1;

        public LifeGame Game { get; private set; }
        public float TimeRemaining { get; private set; }
        public bool IsShowingObituary => Game != null && Game.IsDead;

        // --- UI ---
        private Text _scalesLabel;
        private Text _stageTimerLabel;
        private Text _cardLabel;
        private Text _hintLabel;
        private GameObject _obituaryPanel;
        private Text _obituaryLabel;

        private Font _font;
        private bool _enteredObituaryThisFrame;

        private void Awake()
        {
            BuildUi();
            StartRun(seed < 0 ? (int?)null : seed);
        }

        /// <summary>Starts a fresh run (optionally seeded) — also used by tests.</summary>
        public void StartRun(int? runSeed)
        {
            Game = new LifeGame(runSeed);
            ArmTimer();
            RefreshUi();
        }

        /// <summary>Restarts a new life from the obituary screen.</summary>
        public void RestartLife()
        {
            Game.StartNewLife(seed < 0 ? (int?)null : seed);
            ArmTimer();
            RefreshUi();
        }

        public void ChooseYes()
        {
            if (Game.IsDead) return;
            Game.ChooseYes();
            OnResolved();
        }

        public void ChooseNo()
        {
            if (Game.IsDead) return;
            Game.ChooseNo();
            OnResolved();
        }

        /// <summary>Forces the timeout resolution (timer hit zero). Test-visible.</summary>
        public void ForceTimeout()
        {
            if (Game.IsDead) return;
            Game.Timeout();
            OnResolved();
        }

        private void OnResolved()
        {
            if (Game.IsDead) _enteredObituaryThisFrame = true;
            else ArmTimer();
            RefreshUi();
        }

        private void ArmTimer() => TimeRemaining = Game.CurrentWindowSeconds;

        private void Update()
        {
            var kb = Keyboard.current;

            if (Game.IsDead)
            {
                // Ignore the very frame we entered the obituary so the deciding
                // key press doesn't instantly restart.
                if (_enteredObituaryThisFrame) { _enteredObituaryThisFrame = false; return; }
                if (kb != null && (kb.rightArrowKey.wasPressedThisFrame ||
                                   kb.leftArrowKey.wasPressedThisFrame ||
                                   kb.spaceKey.wasPressedThisFrame ||
                                   kb.enterKey.wasPressedThisFrame))
                {
                    RestartLife();
                }
                return;
            }

            // Real-time decision window.
            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining <= 0f)
            {
                ForceTimeout();
                return;
            }

            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) { ChooseYes(); return; }
                if (kb.leftArrowKey.wasPressedThisFrame) { ChooseNo(); return; }
            }

            UpdateTimerLabel();
        }

        // ---------- UI rendering ----------

        public string ScalesLine => _scalesLabel != null ? _scalesLabel.text : "";
        public string CardLine => _cardLabel != null ? _cardLabel.text : "";
        public string ObituaryText => _obituaryLabel != null ? _obituaryLabel.text : "";

        private void RefreshUi()
        {
            var s = Game.Scales;
            _scalesLabel.text =
                $"Настроение {s.Get(Scale.Mood)}   Здоровье {s.Get(Scale.Health)}   " +
                $"Деньги {s.Get(Scale.Money)}   Люди {s.Get(Scale.People)}";

            if (Game.IsDead)
            {
                _cardLabel.text = "";
                _hintLabel.text = "";
                _stageTimerLabel.text = "Финал";
                _obituaryPanel.SetActive(true);
                _obituaryLabel.text = BuildObituaryText(Game.Obituary);
                return;
            }

            _obituaryPanel.SetActive(false);
            _cardLabel.text = Game.CurrentCard != null ? Game.CurrentCard.Title : "";
            _hintLabel.text = "←  нет                    да  →";
            UpdateTimerLabel();
        }

        private void UpdateTimerLabel()
        {
            if (Game.IsDead) return;
            float t = TimeRemaining < 0f ? 0f : TimeRemaining;
            _stageTimerLabel.text = $"{StageNames.Ru(Game.CurrentStage)}          ⏱ {t:0.0}";
        }

        private static string BuildObituaryText(Obituary o)
        {
            var sb = new StringBuilder();
            sb.AppendLine("НЕКРОЛОГ");
            sb.AppendLine();
            sb.AppendLine(o.Cause);
            sb.AppendLine();
            sb.AppendLine($"Ярлык жизни: {o.Label}");
            sb.AppendLine($"На похоронах: {o.Funeral}");
            sb.AppendLine();
            sb.AppendLine("А помнишь, как ты…");
            foreach (var m in o.Memories) sb.AppendLine(m);
            sb.AppendLine();
            sb.AppendLine("— нажми →, ←, пробел или Enter, чтобы прожить заново —");
            return sb.ToString();
        }

        // ---------- UI construction ----------

        private void BuildUi()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            var canvasGo = new GameObject("LifeChoicesCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            // Top HUD: scales.
            _scalesLabel = MakeText("Scales", canvasGo.transform,
                new Vector2(0.02f, 0.9f), new Vector2(0.98f, 0.99f), 30, TextAnchor.MiddleCenter);
            // Stage + timer.
            _stageTimerLabel = MakeText("StageTimer", canvasGo.transform,
                new Vector2(0.02f, 0.82f), new Vector2(0.98f, 0.9f), 26, TextAnchor.MiddleCenter);
            // Card title.
            _cardLabel = MakeText("Card", canvasGo.transform,
                new Vector2(0.08f, 0.4f), new Vector2(0.92f, 0.75f), 42, TextAnchor.MiddleCenter);
            // Hint.
            _hintLabel = MakeText("Hint", canvasGo.transform,
                new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.38f), 30, TextAnchor.MiddleCenter);

            // Obituary panel (hidden until death).
            _obituaryPanel = new GameObject("Obituary", typeof(RectTransform), typeof(Image));
            _obituaryPanel.transform.SetParent(canvasGo.transform, false);
            var panelRt = _obituaryPanel.GetComponent<RectTransform>();
            Stretch(panelRt, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
            _obituaryPanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.96f);
            _obituaryLabel = MakeText("ObituaryText", _obituaryPanel.transform,
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), 30, TextAnchor.UpperCenter);
            _obituaryPanel.SetActive(false);
        }

        private Text MakeText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            int fontSize, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>(), anchorMin, anchorMax);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
