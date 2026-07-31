using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ThanksNoThanks
{
    /// <summary>
    /// MonoBehaviour driver for «Спасибо, не надо». Owns the pure <see cref="Game"/>, wires an
    /// <see cref="IInputSource"/> (an <see cref="ArcadeInputSource"/> reading the shared arcade-controls
    /// layer by default; a fake can be injected for tests), and
    /// self-builds the TV-show HUD (16:9, 1920×1080) in Awake from the team's P0 sprite set so the
    /// scene needs no fragile hand-wired references. Visual-only layer: gameplay lives in <see cref="Game"/>.
    ///
    /// Assets are loaded from <c>Assets/_Project/Art/Resources</c> (a Resources root nested under Art):
    /// sprites at <c>Sprites/*</c>, fonts at <c>Fonts/*</c>. Fonts are legacy uGUI dynamic fonts —
    /// Arimo Bold for display headlines/questions/buttons/HUD digits (meeting-revisions §8: metric
    /// Helvetica lookalike, OFL, full Cyrillic + ₽), Rubik for body copy and small print (the BLOCK$
    /// price sub-line, the Ведущий bubble).
    /// </summary>
    public sealed class GameDriver : MonoBehaviour
    {
        // ---- palette tokens (#kit) — used for text only; sprites carry their own colour ----
        private static readonly Color Cobalt = new(0.184f, 0.329f, 0.784f);    // #2f54c8
        private static readonly Color CobaltDeep = new(0.122f, 0.227f, 0.588f);// #1f3a96
        // INK is the build-spec §2 token #0B0F1A, re-affirmed as canon by the founder (asset-map §12-4):
        // the dark text/outline colour comes from the TOKENS, never sampled off an explainer PNG.
        private static readonly Color Ink = new(11f / 255f, 15f / 255f, 26f / 255f);   // #0B0F1A
        /// <summary>The INK token, exposed so a test can bind the constant to the canon hex.</summary>
        public static Color InkToken => Ink;
        private static readonly Color TextLight = new(0.918f, 0.941f, 1f);     // #eaf0ff
        private static readonly Color Bulb = new(1f, 0.847f, 0.451f);          // #ffd873
        private static readonly Color Energy = new(0.973f, 0.824f, 0.271f);    // #f8d24c
        private static readonly Color Muted = new(0.62f, 0.69f, 0.91f);        // #9fb0e8
        // Красный «тревоги» — остался за плашкой разрыва и красной зоной балансира (купол на своих токенах).
        private static readonly Color TimerRed = new(0.910f, 0.267f, 0.227f);  // #e8443a
        // ---- купол-таймер (§5a): токены build-spec §1.1, не выборки с PNG ----
        private static readonly Color Cream = new(254f / 255f, 249f / 255f, 232f / 255f);   // #FEF9E8 CREAM
        private static readonly Color DomeYellow = new(1f, 212f / 255f, 0f);                // #FFD400 YELLOW
        private static readonly Color DomeAlarm = new(1f, 21f / 255f, 11f / 255f);          // #FF150B RED_BRIGHT
        // Кант купола — ЧИСТО ЧЁРНЫЙ, как штрихи арт-пака: купол теперь «наклейка среди наклеек»,
        // и токен INK (#0D0D1C) рядом с чёрными обводками баров читался как выцветший (дизайн-гейт).
        private static readonly Color DomeInk = Color.black;                                // #000000
        /// <summary>Токены купола, открытые тесту, чтобы связать константы с каноничными hex-ами.</summary>
        public static Color CreamToken => Cream;
        public static Color DomeYellowToken => DomeYellow;
        public static Color DomeAlarmToken => DomeAlarm;
        public static Color DomeInkToken => DomeInk;
        private static readonly Color PlateMute = new(0.62f, 0.62f, 0.64f);    // S10: muted answer plates while BLOCK$-blocked
        private static readonly Color CardBlockDim = new(0.52f, 0.54f, 0.60f); // S10: tint the card frame when unaffordable (dims to muted cobalt)
        // S1 opener, снято с эталона «Стартовый экран.png»: золото марки-рамки и тёплый крем её плашки —
        // это СВОИ значения экрана (крем опенера теплее токена CREAM карточек), поэтому отдельные токены.
        private static readonly Color MarqueeGold = new(248f / 255f, 180f / 255f, 50f / 255f); // #f8b432
        private static readonly Color OpenerCream = new(247f / 255f, 228f / 255f, 187f / 255f);// #f7e4bb
        private static readonly Color GoGreen = new(5f / 255f, 206f / 255f, 81f / 255f);       // #05CE51 — GREEN кабинета
        /// <summary>The GREEN token, exposed so a test can bind BOTH confirm CTAs to the canon hex.</summary>
        public static Color GoGreenToken => GoGreen;

        // ---- age gates (canon opening ages, S5 tutorial) : purely visual reveal ----
        public const int MoneyAge = 18;
        public const int RelationshipsAge = 20;
        public const int EnergyAge = 25;
        public const int HealthAge = 30;

        /// <summary>Optional input injection (tests). Defaults to an ArcadeInputSource in Start.</summary>
        public IInputSource Input;

        private Game _game;
        public Game Game => _game;

        private Font _display; // Arimo Bold (meeting-revisions §8 — «основные надписи»)
        private Font _body;    // Rubik (has ₽ + Cyrillic) — the VARIABLE file, i.e. its default wght 300
        private Font _bodyBold;// Rubik-Bold: static wght=700 instance of the same OFL file (host replies)

        // Panels
        private GameObject _openerPanel;
        private GameObject _gamePanel;
        private GameObject _finalePanel;

        // S1 opener parts (design-gate handles: logo cut, cream rules plate, canon rules copy)
        private Image _openerLogo;
        private Image _openerPlate;
        private Text _openerRules;

        // Shared background — `sunburst-bg-v3`, a SCREEN-SPACE synthesis of the art-pack rays.
        // Why a synthesis (design-gate round 2): the reference explainer was assembled with the rays at
        // ≈×1.22, i.e. the sprite's DARK outer half fills the frame. Any plain overscan big enough to
        // cover the rotation diagonal (×2.16) shows only the sprite's light inner part, and the whole
        // screen washed out — measured ring medians of R 138/116/81/60 against the reference's 54/30/24/23.
        // `scratchpad/synth_bg_v3.py` therefore resamples the (core-patched) sprite by ANGLE and RADIUS
        // into a 2210² screen-space square: inside the frame every pixel is what the reference sees, and
        // past the sprite's reach in a direction the colour of its maximum radius (the dark edge) is
        // carried on out. Rotating the square just turns the rays, so the 6°/сек spin is unchanged.
        private Image _bg;
        /// <summary>Background rect, reference px. Square, 1:1 with the synthesised texture (no rescale =
        /// no resampling blur), and every EDGE is 1105 px from the pivot — past the 1920×1080 half-diagonal
        /// (1101.6), so no spin angle can bare a corner.</summary>
        public const float BgOverscanW = 2210f;
        public const float BgOverscanH = 2210f;
        /// <summary>Ray hub = the geometric centre of `sunburst-bg-v3` (the synthesis puts the convergence at
        /// the texture centre), so a plain centre pivot keeps the hub pinned on the screen centre while the
        /// rays turn.</summary>
        public static readonly Vector2 BgSpinPivot = new Vector2(0.5f, 0.5f);
        /// <summary>Ray spin rate: 1 turn / 60 s = 6°/сек, clockwise (Unity z decreases).</summary>
        public const float BgSpinDegPerSecond = 6f;
        private float _bgSpin;   // accumulated clockwise degrees (monotonic, wrapped at 360)

        // ---- HUD widgets (art-pack row, gated by age) ------------------------------------------------
        private GameObject _hudRow;    // top-row container (батарея · отношения · здоровье · банка · бейдж)
        private GameObject _ageBadge;      // age_frame_blue badge (digits only, no «ВОЗРАСТ» caption)
        private GameObject _moneyGroup;    // money jar + sum label + coin
        private GameObject _healthGroup;   // health bar + its own marker
        private GameObject _energyGroup;   // battery + cavity fill
        private GameObject _balancerGroup; // relationships bar + its own marker

        private Text _ageText;
        private Text _moneyText;           // the sum INSIDE the jar's cream label (compact «₽12.5к», §11-6)
        private Image _moneyCoin;          // coin over the jar throat — drops in on an income tick
        private Image _jarImg, _batteryImg, _ageBadgeImg;
        // Battery level (asset-map §5.6/§8): the sprite carries a BAKED level at 75.1 % of its cavity, so the
        // live level is drawn as two flat rects over the cavity instead of repainting it — a cream «empty»
        // rect from the cavity top DOWN to the level line (its bottom edge IS the reading), plus a yellow
        // top-up rect between the baked line and a level above it. Everything the art draws inside the
        // cavity (the lightning bolt) survives untouched.
        private Image _energyEmpty;
        private Image _energyTopUp;
        private Image _energyBolt;         // the art's lightning, its own layer above the fill (§12-3)
        // Bar markers: the art's own health marker was a BAKED silhouette (patched out, asset-map §11-3), so
        // both bars carry OUR marker, drawn in the same flat-black figure style, positioned through the
        // non-linear value→track map (§11-2/4).
        private Image _relBarImg, _healthBarImg;
        private RectTransform _balancerMarker;
        private Image _balancerMarkerImg;  // tinted red in the >75 % «красная зона»
        private RectTransform _healthMarker;
        private Image _healthMarkerImg;

        // ================================================================ art-pack HUD geometry
        // Every art-pack PNG carries a transparent margin, and an Image/Simple stretches the WHOLE sprite
        // rect over its RectTransform — so a rect set to the measured DRAWN box would draw the artwork too
        // small. The rects below are the drawn boxes of asset-map §2 divided out by each sprite's own
        // alpha-tight fraction (measured on the imported PNGs), i.e. «put the rect here and the ARTWORK
        // lands exactly on the reference box». Format: cx, cyTop, w, h in 1920×1080 reference px.
        //   sprite (tight bbox / texture)          drawn box §2            → rect
        //   energy-battery-v2 (18,21,379,859/420,910)  127,58,114,218      → 184.75, 168.14, 126.33, 230.94
        //   rel-bar-v2        (18,11,1136,286/1172,309) 413,35,523,132     → 674.50, 101.23, 539.57, 142.62
        //   health-bar-v2     (18,11,1136,286/1172,309) 1042,38,504,127    → 1294.00, 101.72, 519.97, 137.21
        //   money-jar-v2      (126,103,772,835/1024²)   1664,65,173,185    → 1750.50, 155.62, 229.47, 226.87
        //   coin-v2           (1,0,858,868/860,869)     1714,6,68,68       → 1748.00, 40.04, 68.16, 68.08
        //   age-badge-v2      (35,63,947,923/1024²)     1639,392,212,207   → 1745.78, 492.70, 229.24, 229.65
        //   choice-plate-v2   (34,26,1468,968/1536,1024) 413,229,1093,721  → 959.50, 590.99, 1143.63, 762.71
        // The JAR box is NOT the asset-map §2 row (design gate round 2: the table is wrong for the jar and
        // the explainer wins) — the jar's own drawn glass, measured on the reference without the coin, is
        // 1664,65,173,185. The COIN is unchanged (it was already within 2 px), and moving the jar UP is what
        // seats the coin IN the lid slot the way the reference draws it.
        private static readonly Vector4 BatteryRect = new(184.75f, 168.14f, 126.33f, 230.94f);
        private static readonly Vector4 RelBarRect = new(674.50f, 101.23f, 539.57f, 142.62f);
        private static readonly Vector4 HealthBarRect = new(1294.00f, 101.72f, 519.97f, 137.21f);
        private static readonly Vector4 MoneyJarRect = new(1750.50f, 155.62f, 229.47f, 226.87f);
        private static readonly Vector4 CoinRect = new(1748.00f, 40.04f, 68.16f, 68.08f);
        private static readonly Vector4 AgeBadgeRect = new(1745.78f, 492.70f, 229.24f, 229.65f);
        private static readonly Vector4 CardPlateRect = new(959.50f, 590.99f, 1143.63f, 762.71f);

        // ---- КУПОЛ-ТАЙМЕР (revisions §5a / build-spec §2) --------------------------------------------
        // РЕШЕНИЕ ОСНОВАТЕЛЬНИЦЫ (2026-07-31): купол КРУПНЫЙ, по центру экрана, слоем ПОД барами — «как
        // наклейки на афише»: бары нарисованы ПОВЕРХ купола, а дуга читается в просветах (полоса над
        // барами y 0…35 и коридор между барами x 936…1042). Поэтому берётся бокс спека БУКВАЛЬНО:
        //   бокс = 760, 0, 400, 130 · центр «окружности» = (DomeCx, 0) на верхнем крае экрана
        // Форма — половина ЭЛЛИПСА (полуоси 200 × 130), а не окружности: спековый бокс 400×130 именно
        // приплюснутый, и тот же процедурный спрайт-полукруг растягивается в него ректом.
        //   видимая полоса дуги над барами (бары начинаются с y 35): 35 px ≥ 25 px гейта
        //   в коридоре 936…1042 купол виден от y 35 до y ≈ 119…129 (низ эллипса на этих x)
        // Z-порядок: Timer — ПЕРВЫЙ ребёнок _gamePanel, т.е. НИЖЕ HudRow (баров) и ВЫШЕ фона-лучей
        // (`Background` — сосед _gamePanel и создаётся раньше него).
        public const float DomeCx = 960f;        // центр экрана по X (бокс §2: 760…1160)
        public const float DomeW = 400f;         // ширина купола = 2 × горизонтальной полуоси (бокс §2)
        public const float DomeH = 130f;         // глубина купола = вертикальная полуось (бокс §2)
        public const float DomeOutlineWidth = 7f;// обводка 6–8 px (build-spec §1.4)
        /// <summary>Последняя секунда таймера — ВЕСЬ купол мигает RED_BRIGHT (§5a «тревожный/мигает»).</summary>
        public const float DomeAlarmSeconds = 1f;
        /// <summary>
        /// Период мигания тревоги, с. Фаза берётся от ОСТАВШЕГОСЯ времени, а не от `Time.time`:
        /// мигание получается детерминированным (кадр-харнесс и тест ловят пик точно), и на паузе
        /// купол честно замирает вместе со своим цветом.
        /// </summary>
        public const float DomeAlarmPulsePeriod = 0.35f;
        /// <summary>
        /// «Стрелка-кромка»: тонкая чёрная линия по радиусу границы заливки — она и подчёркивает ход
        /// времени, и закрывает лесенку `Image.Filled` (единственный несглаженный край HUD, дизайн-гейт).
        /// </summary>
        public const float DomeHandWidth = 3f;

        // Inner boxes, straight from asset-map §8 (screen px @1920×1080; x/y are LEFT/TOP edges).
        /// <summary>Battery cavity — the fill box: x, yTop, w, h.</summary>
        public const float CavityX = 141f, CavityTop = 88f, CavityW = 86f, CavityH = 175f;
        /// <summary>Level baked into `energy-battery-v2` (asset-map §5.6): cream above, yellow below.</summary>
        public const float BakedEnergyLevel = 0.751f;
        /// <summary>Relationships track (asset-map §8): left edge and width in screen px.</summary>
        public const float RelTrackX = 513f, RelTrackW = 316f, RelTrackTop = 85f, RelTrackH = 35f;
        /// <summary>Health track (asset-map §8).</summary>
        public const float HealthTrackX = 1097f, HealthTrackW = 397f, HealthTrackTop = 78f, HealthTrackH = 56f;
        /// <summary>Jar throat — where a dropped coin lands. Asset-map §8 gives it in SPRITE px (323,151,364,50);
        /// re-projected through the corrected jar box its centre sits at screen y ≈ 81.</summary>
        public const float ThroatCenterY = 81.2f;
        /// <summary>Jar sum label, re-projected from the sprite box (asset-map §8: 292,488,440,171) through the
        /// corrected jar box → screen 1701.2, 150.3, 98.6, 37.9.</summary>
        public const float JarLabelW = 98.6f, JarLabelH = 37.9f;

        // ---- markers ARE the art's own elements (founder canon, asset-map §12-1) ---------------------
        // The heart was patched OUT of the relationships track and the black figure cut from the UNPATCHED
        // health bar; both now RIDE their track through the §11-2/§11-4 map. Boxes are the elements' own
        // drawn sizes, re-projected from the sprite through each bar's screen box — i.e. the marker is
        // exactly as big, and sits exactly as high, as the art drew it.
        //   rel-marker-heart-v2 : cut at sprite (512,89,158,136) of rel-bar-v2   → 72.7 × 62.8, centre y 102.4
        //   health-marker-v2    : cut at sprite (526,52,163,236) of health-bar   → 72.3 × 104.8, centre y 108.6
        public const float RelMarkerW = 72.7f, RelMarkerH = 62.8f, RelMarkerCy = 102.4f;
        public const float HealthMarkerW = 72.3f, HealthMarkerH = 104.8f, HealthMarkerCy = 108.6f;

        // ---- the END DECORATIONS own the ends of the track (design gate, round 3) ---------------------
        // Each bar draws a fixed decoration at each end of its own track — the health bar a skull and a
        // heart INSIDE the track, the relationships bar the two faces sitting ON its ends. They are as black
        // (health) or as busy (rel) as the markers themselves, so a marker parked on one reads as a single
        // unreadable blob: at 100 health the figure covered the heart to its middle. The marker therefore
        // travels between them with a gap, and the value→track maps below carry that travel window in their
        // END knots (the INTERIOR knots — the drawn zone borders — are untouched canon).
        // Boxes are the DRAWN ink, measured on the imported PNGs and re-projected through each bar's screen
        // rect (health: sprite→screen ×0.4437 from x 1034.01; rel: ×0.4604 from x 404.71):
        //   health skull  sprite x 165…256   → screen 1107.2…1148.0
        //   health heart  sprite x 921…1015  → screen 1442.6…1484.8
        //   rel boy face  sprite x  95…245 skin + 7 px of its ink outline → screen 445.2…521.2
        //   rel girl face sprite x 922…1089 skin − 7 px of its ink outline → screen 825.9…909.7
        // The two FACES carry their edge from the RENDERED frame (523 / 822), not from that projection: the
        // rel bar draws at 0.46 of its source, and the resampled ink edge lands 2…4 px further out than the
        // sprite-space arithmetic predicts. The player sees the frame, so the frame wins; the skull and the
        // heart need no such correction (their edges agree to within a pixel).
        /// <summary>Right edge of the health bar's drawn skull (screen px).</summary>
        public const float HealthSkullRight = 1148.0f;
        /// <summary>Left edge of the health bar's drawn heart (screen px).</summary>
        public const float HealthHeartLeft = 1442.6f;
        /// <summary>Right edge of the relationships bar's drawn boy face, outline included (screen px).</summary>
        public const float RelBoyFaceRight = 523f;
        /// <summary>Left edge of the relationships bar's drawn girl face, outline included (screen px).</summary>
        public const float RelGirlFaceLeft = 822f;
        /// <summary>Clear air between a marker's drawn box and an end decoration. The design gate asks for
        /// ≥4 px; 8 keeps that true ON THE FRAME too — the bars draw at ≈0.44…0.46 of their source, and the
        /// resampled ink edge lands ≈2 px further out than the sprite-space projection predicts (measured on
        /// the regenerated poses).</summary>
        public const float MarkerEndIconGap = 8f;
        /// <summary>Travel window of the health marker's CENTRE (screen px) — its drawn box stays clear of
        /// both decorations.</summary>
        public const float HealthMarkerMinCx = HealthSkullRight + MarkerEndIconGap + HealthMarkerW / 2f;
        public const float HealthMarkerMaxCx = HealthHeartLeft - MarkerEndIconGap - HealthMarkerW / 2f;
        /// <summary>Travel window of the relationships marker's CENTRE (screen px).</summary>
        public const float RelMarkerMinCx = RelBoyFaceRight + MarkerEndIconGap + RelMarkerW / 2f;
        public const float RelMarkerMaxCx = RelGirlFaceLeft - MarkerEndIconGap - RelMarkerW / 2f;

        // ---- host bubble: the art-pack megaphone plate (`host-comment-v2`, 1445×506) -------------------
        // Measured on `explainers/Экран - комментарий ведущего.png` (1920×1080, pixel truth), by flood-filling
        // the plate's cream+gold body and growing through its ink outline:
        //   visible PLATE (cream + gold rim + ink)   257, 201, 453, 248   centre 483.5, 325.0
        //   visible WHOLE glyph (plate + megaphone)  198, 197, 512, 255
        // Fitting the sprite against BOTH boxes (scale × tilt × centre, minimising the box error) has one
        // solution: uniform scale 0.375, tilt +14.5°, rect centre (449, 333). The tilt is POSITIVE = counter-
        // clockwise: on the explainer the plate's RIGHT end rides higher and the megaphone hangs down-left.
        /// <summary>Sprite px → screen px for `host-comment-v2` (measured against the explainer).</summary>
        public const float BubbleScale = 0.375f;
        /// <summary>Plate tilt in degrees, CCW (+) — the right end sits higher, exactly as the explainer draws it.</summary>
        public const float BubbleTiltDeg = 14.5f;
        /// <summary>Bubble rect: centre x, centre y (from TOP), w, h — the sprite's full 1445×506 at <see cref="BubbleScale"/>.</summary>
        public static readonly Vector4 BubbleRect =
            new(449f, 333f, 1445f * BubbleScale, 506f * BubbleScale);
        // 9-slice borders live in SPRITE px (340,80,80,80 — asset-map §5): the LEFT one is wide because it has
        // to carry the whole megaphone (sprite x 19…330) plus the plate's left cap, so a resize never stretches
        // them. Drawn border = border / pixelsPerUnitMultiplier, so the multiplier below is what puts the
        // borders on the SAME 0.375 as the rest of the art (otherwise a 340 px border would eat 63 % of a
        // 542 px plate). With the rect at exactly 0.375×the texture, the stretched middle lands on 0.375 too —
        // the plate draws as a clean uniform scale of the source art, and 9-slice only absorbs later resizes.
        /// <summary>Border sprite-px, left (the megaphone side) and bottom/right/top.</summary>
        public const float BubbleBorderLeftPx = 340f, BubbleBorderPx = 80f;
        // Text insets, in SPRITE px, = the border + slop. The right one additionally clears the gold star baked
        // into the plate's bottom-right corner (sprite x 1297…1388): the largest rectangle that fits inside the
        // cream fill without touching the rim or that star is sprite 323…1286 × 77…429, so 159 from the right.
        // The extra slop exists because uGUI's best-fit only honours HEIGHT in Wrap mode: at the size it picks,
        // a wrapped line can still run ~2 px past the rect. Without the slop those 2 px land on the gold rim
        // (guarded RED by HostReactionTests — «Зачем терапевт, когда есть кот.» was the line that found it).
        /// <summary>Best-fit slop margin, sprite px (horizontal, vertical).</summary>
        public const float BubbleTextSlopXPx = 32f, BubbleTextSlopYPx = 16f;
        /// <summary>Reply kegl cap: the explainer's own cap-height (27.0 px de-tilted) ÷ Rubik's capHeight
        /// (700/1000 em). Best-fit shrinks a long line from here; a 1–3 word line draws at this size.</summary>
        public const int BubbleTextMaxSize = 39;
        /// <summary>Text insets from the plate's edges, in sprite px (left = the whole megaphone border).</summary>
        public const float BubbleTextLeftPx = BubbleBorderLeftPx + BubbleTextSlopXPx;
        public const float BubbleTextRightPx = 159f + BubbleTextSlopXPx;
        public const float BubbleTextVertPx = BubbleBorderPx + BubbleTextSlopYPx;

        // ---- battery lightning: its own layer (founder canon §12-3) ----------------------------------
        // Patched out of the cavity fill and cut as `battery-bolt-v2`; drawn OVER the mask and the top-up so
        // it always reads whole, at any level. Cut at sprite (119,408,182,316) of energy-battery-v2.
        public static readonly Vector4 BoltRect = new(184.75f, 196.30f, 54.74f, 80.20f);

        // ---- card: cream field, the question's safe box and the reserved bottom band -------------------
        /// <summary>Cream field of `choice-plate-v2` on screen (asset-map §8: 467,286,984,606).</summary>
        public const float FieldX = 467f, FieldTop = 286f, FieldW = 984f, FieldH = 606f;
        /// <summary>Question safe box (asset-map §8: 578,322,764,535) — x column and top edge.</summary>
        public const float CardTextX = 578f, CardTextW = 764f, CardTextTop = 322f;
        /// <summary>Bottom of the question box when the card carries NOTHING else (the full §8 safe box).</summary>
        public const float CardTextFullBottom = 857f;
        /// <summary>
        /// Top of the RESERVED bottom band of the cream field: the BLOCK$ banner (y 705…815) and the price
        /// sub-line (y 811…889) live here. While either is up the question box must end above this line —
        /// без этого длинный вопрос гарантированно печатается прямо по баннеру и цене (skeptic MAJOR-1).
        /// </summary>
        public const float CardBandTop = 705f;
        /// <summary>Clearance between the last line of the question and the reserved band.</summary>
        public const float CardTextBandGap = 12f;
        /// <summary>Bottom of the question box while the reserved band is occupied.</summary>
        public const float CardTextShortBottom = CardBandTop - CardTextBandGap;

        // ---- child «cabinet button» placeholder (инкремент «звонок» переделает) -----------------------
        // Free slot in the art row's right column: the gap BETWEEN the money jar (drawn box 1674,74,161,174
        // → bottom 248) and the age badge (1639,392,212,207 → top 392), right of the card plate (drawn right
        // edge 1506). The whole cluster — including the halo at its PULSE PEAK — has to fit that 144 px gap
        // and stay in frame, so the sizes below are derived from it, not picked by eye (skeptic MAJOR-2).
        /// <summary>Centre of the child button cluster, 1920×1080 reference px (x from LEFT, y from TOP).</summary>
        public const float ChildCx = 1840f, ChildCy = 320f;
        /// <summary>Halo (ChildGlow) rect size at rest; it pulse-scales while the flash window is open.</summary>
        public const float ChildGlowSize = 106f;
        /// <summary>Halo flash pulse: scale = mid ± amp. Peak size = ChildGlowSize × (mid + amp) ≈ 130 px.</summary>
        public const float ChildGlowPulseMid = 1.05f, ChildGlowPulseAmp = 0.18f;
        /// <summary>Button rect size at rest; sized so the halo stays bigger even at the pulse TROUGH.</summary>
        public const float ChildButtonSize = 66f;
        /// <summary>Button flash pulse: scale = mid ± amp.</summary>
        public const float ChildButtonPulseMid = 1.28f, ChildButtonPulseAmp = 0.10f;
        /// <summary>Halo size at the pulse PEAK — the worst case a layout guard has to clear.</summary>
        public const float ChildGlowPeakSize = ChildGlowSize * (ChildGlowPulseMid + ChildGlowPulseAmp);
        /// <summary>Button size at the pulse PEAK.</summary>
        public const float ChildButtonPeakSize = ChildButtonSize * (ChildButtonPulseMid + ChildButtonPulseAmp);

        // Baked cavity colours, sampled off `energy-battery-v2`: cream «empty», saturated yellow «full».
        private static readonly Color BatteryCream = new(253f / 255f, 249f / 255f, 230f / 255f);
        private static readonly Color BatteryYellow = new(254f / 255f, 210f / 255f, 1f / 255f);

        // ---- child button (opens on MD02=ДА, not age-gated; flashes on the signal-response window) ----
        private GameObject _childGroup;    // whole widget; shown while Game.ChildOpen, hidden after LT04
        private Image _childButtonImg;     // the «lit» bulb — bright while ChildFlashing, dim otherwise
        private Image _childGlow;          // bright pulsing halo behind the button — only while ChildFlashing (visibility fix)

        // Card
        private RectTransform _cardRoot;
        private Image _cardFrame;
        private Text _cardText;

        // Answer plates
        private Image _yesPlate;
        private Image _noPlate;
        private RectTransform _yesRect;
        private RectTransform _noRect;
        private Text _yesPlateText;   // crisis-only overlay («ДА»/«СПАСИБО, НЕ НАДО» are BAKED in the art)
        private Text _noPlateText;
        // The two answer-plate sprite sets. Ordinary play draws the ART-PACK plates with the lettering BAKED
        // IN (`btn-yes` / `btn-no`, Simple — the art is not a 9-slice); the crisis (blitz + impulse) keeps the
        // old blank code-plates + a dynamic Text, because it relabels them per thought («ВСЁ НОРМАЛЬНО» /
        // «О НЕТ») and gold-highlights the decline. Switched by UseBakedPlates / UseCodePlates.
        private Sprite _bakedYesSprite, _bakedNoSprite, _codeYesSprite, _codeNoSprite;
        // Plate tilts (meeting-revisions §9 / build-spec §B), re-measured on «Экран спокойный обычный.png» by
        // a min-area rotated-bbox fit over the plates' colour fill: red −9.05°, green +13.15° → the canon is
        // ASYMMETRIC. The GREEN «ДА» plate (screen-RIGHT) leans up-to-the-right → Unity z = +13; the RED
        // «СПАСИБО, НЕ НАДО» plate (screen-LEFT) leans down-to-the-right → Unity z = −9. (Reference check:
        // the red plate's HIGHEST corner is its top-LEFT one, the green plate's is its top-RIGHT one.)
        private const float YesTilt = 13f;
        private const float NoTilt = -9f;

        // ---- midlife crisis HUD (S6 blitz / S13 impulse); built hidden, shown only while Game.InCrisis ----
        private GameObject _crisisInfo;       // top readout: «МЫСЛЬ N/5 · ПРОВАЛОВ: K» / «ИМПУЛЬС N/3»
        private Text _crisisInfoText;
        private GameObject _impulseWarning;   // S13 INVERT plate: «МОЛЧАНИЕ = ДА! · ЖМИ СПАСИБО НЕ НАДО →»
        private bool _crisisUiActive;         // true while the plates/timer are in crisis mode (for restore)

        // Купол-таймер (§5a) — сменил круглое кольцо (252,590)
        private GameObject _timerGroup;   // whole dome widget; hidden during a rubric banner beat
        private Image _domeOutline;       // INK-обводка: тот же полукруг на весь внешний бокс DomeW×DomeH
        private Image _domeTrack;         // кремовый «истёкший» остаток под дугой
        private Image _domeFill;          // жёлтая дуга-остаток (Radial180), краснеет в последнюю секунду
        private Image _domeHand;          // «стрелка-кромка»: чёрная линия по границе заливки (AA, прячет лесенку)
        private Sprite _domeSprite;       // процедурный полукруг (плоской стороной вверх), общий на 3 слоя
        private Sprite _domeHandSprite;   // процедурная полоска с мягкими краями — тело стрелки

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
            "Крутите РУЧКУ — и деньги потекут. Но жизнь идёт своим чередом:\n" +
            "содержать себя стоит денег каждую секунду.\n\n" +
            "Рук всего две — крутить и отвечать придётся разом.";

        // NOTE: must NOT contain «УСТАЛОСТЬ» or «ТАЯТЬ» — the PlayMode hint-walker tests key off those
        // substrings to identify the energy/health hints; a collision would misidentify this one.
        private const string RelationshipsTutorialText =
            "ПЕРВАЯ ЛЮБОВЬ!\n\n" +
            "Появился БАЛАНСИР ОТНОШЕНИЙ — маркер всё время сползает ВНИЗ.\n" +
            "ДЕРЖИТЕ ДЖОЙСТИК ВВЕРХ, чтобы удержать его в зелёной зоне (ВНИЗ — опустить).\n\n" +
            "Упадёт в КРАСНУЮ надолго — расстанетесь. Задушите вверху — ссоры.";

        private const string EnergyTutorialText =
            "ПЕРВАЯ УСТАЛОСТЬ!\n\n" +
            "Появилась ЭНЕРГИЯ — и она тает сама собой.\n" +
            "Дышите РИТМИЧНО: ведите ДАТЧИК ВЫСОТЫ в спокойном темпе, не долбите.\n\n" +
            "Ровное дыхание возвращает силы.";

        private const string HealthTutorialText =
            "ЗДОРОВЬЕ НАЧАЛО ТАЯТЬ.\n\n" +
            "С этого возраста ЗДОРОВЬЕ убывает само по себе.\n" +
            "Лечиться можно за деньги — если накопили.\n\n" +
            "Запустите — организм не выдержит.";

        private const string BurnoutHintText =
            "ВЫГОРАНИЕ!\n\n" +
            "Всё даётся тяжелее — деньги идут вдвое медленнее.\n" +
            "Подышите ДАТЧИКОМ ВЫСОТЫ, чтобы прийти в себя.\n\n" +
            "Отпустит само, когда энергия восстановится.";

        // Child S5 hint. Must NOT contain «УСТАЛОСТЬ»/«ТАЯТЬ»/«ОТНОШЕНИЙ» — the PlayMode hint-walkers key
        // off those substrings to identify the energy/health/relationships hints; a collision misids this.
        private const string ChildTutorialText =
            "ПОПОЛНЕНИЕ!\n\n" +
            "Появился РЕБЁНОК — кнопка на пульте загорается время от времени.\n" +
            "Жмите кнопку «!», пока она горит — по вспышке.\n\n" +
            "Пропустите подряд — станете плохим родителем.";

        // ---- public inspection accessors (visual-assembly PlayMode tests) ----
        public RectTransform CanvasRect { get; private set; }
        public Image BackgroundImage => _bg;
        /// <summary>Accumulated CLOCKWISE spin of the background rays, in degrees (Layer-2 seam).</summary>
        public float BackgroundSpinDegrees => _bgSpin;
        public Image CardFrameImage => _cardFrame;
        public RectTransform CardRect => _cardRoot;
        public Image YesPlateImage => _yesPlate;
        public Image NoPlateImage => _noPlate;
        public GameObject AgeBadge => _ageBadge;
        public Text AgeText => _ageText;                 // the DIGITS on the age badge (no caption since the art pack)
        public Image AgeBadgeImage => _ageBadgeImg;
        public GameObject MoneyJar => _moneyGroup;
        public Image MoneyJarImage => _jarImg;
        public Text MoneyText => _moneyText;             // the sum drawn INSIDE the jar's cream label
        public Image MoneyCoin => _moneyCoin;
        public GameObject HudRow => _hudRow;
        public GameObject HealthGroup => _healthGroup;
        public Image HealthBarImage => _healthBarImg;
        public GameObject HealthMarker => _healthMarker != null ? _healthMarker.gameObject : null;
        public GameObject EnergyGroup => _energyGroup;
        public Image BatteryImage => _batteryImg;
        /// <summary>Cream «empty» rect over the battery cavity — its BOTTOM edge is the energy reading.</summary>
        public Image EnergyEmpty => _energyEmpty;
        public Image EnergyTopUp => _energyTopUp;
        /// <summary>The lightning layer — drawn whole above the mask and the top-up at every level.</summary>
        public Image EnergyBolt => _energyBolt;
        public GameObject BalancerGroup => _balancerGroup;
        public Image RelBarImage => _relBarImg;
        /// <summary>Жёлтая дуга-остаток купола (Radial180) — то, что реально убывает за таймер фазы.</summary>
        public Image TimerDomeFill => _domeFill;
        /// <summary>Кремовая «истёкшая» часть купола под дугой.</summary>
        public Image TimerDomeTrack => _domeTrack;
        /// <summary>Чёрная обводка купола — её rect и есть внешний бокс виджета (DomeW × DomeH).</summary>
        public Image TimerDomeOutline => _domeOutline;
        /// <summary>«Стрелка-кромка» купола — чёрная линия по радиусу границы заливки.</summary>
        public Image TimerDomeHand => _domeHand;
        /// <summary>Контейнер купола (скрывается на баннер-бите, отсутствует на опенере/финале).</summary>
        public GameObject TimerDome => _timerGroup;
        public GameObject OpenerPanel => _openerPanel;
        /// <summary>S1: the show logo cut from the explainer (`opener-logo-v2`).</summary>
        public Image OpenerLogo => _openerLogo;
        /// <summary>S1: the cream rules plate inside the marquee frame.</summary>
        public Image OpenerPlate => _openerPlate;
        /// <summary>S1: the canon rules copy drawn inside that plate.</summary>
        public Text OpenerRules => _openerRules;
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
        /// <summary>The pulsing halo BEHIND the child button — the widget's true visible extent.</summary>
        public Image ChildGlow => _childGlow;
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
        /// <summary>Layer-2 seam: advance the §7 ray spin by <paramref name="dt"/> seconds (same path Update drives).</summary>
        public void DebugSpinBackground(float dt) => SpinBackground(dt);
        /// <summary>Layer-2 seam: draw an arbitrary scale reading onto the art HUD (battery level + both bar
        /// markers) without driving a whole life — the same code path Update/UpdateHudValues use.</summary>
        public void DebugReflectScales(float energy, float health, float relations, bool relRedZone = false)
        {
            ReflectEnergyLevel(energy);
            ReflectHealthMarker(health);
            ReflectRelationsMarker(relations, relRedZone);
        }

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

        public void DebugPreviewDepression(bool lit = false)
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            ApplyAgeGates(60f);                         // reveal the minimal live HUD behind the wash
            _cardText.text = "Встать сегодня с кровати?";
            _depressionGroup.SetActive(true);
            _depressionVeil.color = new Color(GrayWash.r, GrayWash.g, GrayWash.b, 0.92f);   // match live ReflectDepression (S8 fix: heavier wash suppresses the sunburst)
            _depressionGrain.color = new Color(1f, 1f, 1f, 0.06f);
            _depressionPulse.gameObject.SetActive(true);
            // Freeze the big indicator in its lit («жми!») or resting (dim-but-visible) pose for a stable capture.
            if (lit)
            {
                _depressionPulse.color = Color.white;
                _depressionPulse.rectTransform.localScale = Vector3.one * 1.28f;
            }
            else
            {
                _depressionPulse.color = new Color(0.90f, 0.90f, 0.97f, 0.34f);
                _depressionPulse.rectTransform.localScale = Vector3.one * 0.86f;
            }
            enabled = false;
        }

        public void DebugPreviewChildFlash()
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            ApplyAgeGates(40f);
            _cardText.text = "Обычная жизнь идёт…";
            _childGroup.SetActive(true);
            _childButtonImg.color = Bulb;                                  // lit gold
            // Pose the flash at its PEAK — the same constants the layout guard clears.
            _childButtonImg.rectTransform.localScale =
                Vector3.one * (ChildButtonPulseMid + ChildButtonPulseAmp);
            _childGlow.color = new Color(Bulb.r, Bulb.g, Bulb.b, 0.85f);
            _childGlow.rectTransform.localScale = Vector3.one * (ChildGlowPulseMid + ChildGlowPulseAmp);
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

        // ---- the question box vs the card's reserved bottom band (skeptic MAJOR-1) --------------------
        // The BLOCK$ banner (screen y 705…815) and the price sub-line (811…889) sit INSIDE the cream field,
        // in the band below the question. The question's §8 safe box reaches y 857, so whenever either was
        // up a long question printed straight THROUGH them — best-fit only shrinks text to its RECT, and the
        // rect overlapped. Fix: the rect itself moves. Its bottom rises above the band while the band is
        // occupied, and returns to the full §8 safe box when the card carries nothing else.
        private void SetCardTextBottom(float bottomRef)
        {
            float h = bottomRef - CardTextTop;
            float cyRef = CardTextTop + h / 2f;
            float cardLeft = CardPlateRect.x - CardPlateRect.z / 2f;   // card rect edges in reference px
            float cardTop = CardPlateRect.y - CardPlateRect.w / 2f;
            Anchor(_cardText.rectTransform,
                new Vector2((CardTextX + CardTextW / 2f - cardLeft) / CardPlateRect.z,
                            1f - (cyRef - cardTop) / CardPlateRect.w),
                new Vector2(CardTextW, h));
        }

        /// <summary>
        /// Recompute the question box from what the reserved bottom band currently carries. Called from
        /// EVERY place that toggles the block banner or the price line, so «banner up + tall text box» is
        /// not a reachable state.
        /// </summary>
        private void ReflectCardTextBand()
        {
            bool band = (_blockBanner != null && _blockBanner.activeSelf)
                     || (_cardPricePlate != null && _cardPricePlate.gameObject.activeSelf);
            SetCardTextBottom(band ? CardTextShortBottom : CardTextFullBottom);
        }

        /// <summary>Screenshot pose: an ordinary frame with the Ведущий's comment plate up, so the
        /// reference-placed bubble (top-left, riding the card's corner) can be judged by eye.</summary>
        public void DebugPreviewHostComment()
        {
            DebugPreviewArcadeShot();          // ordinary frame, driver frozen
            _bubbleText.text = "Трещина на потолке — бесплатный ночник для мыслей.";
            _hostBubble.SetActive(true);
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

        // Screenshot hook (arcade-packaging increment): pose a clean, representative mid-life frame — a card
        // plus the revealed HUD scales (age, money, health/energy bars, relationship balancer, timer, answer
        // plates) — and FREEZE the driver so a batch capture is stable. Visual-only; the pure Game is untouched.
        public void DebugPreviewArcadeShot()
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            ApplyAgeGates(35f);                       // reveal money + health + energy + relationships widgets
            RestoreNormalPlates();
            _ageText.text = "35";
            _moneyText.text = FormatMoneyJar(1240);
            _cardText.text = "Взять ипотеку на 25 лет?";
            ReflectEnergyLevel(58f);
            ReflectHealthMarker(72f);
            ReflectRelationsMarker(58f, redZone: false);
            _yesPlate.color = Color.white; _noPlate.color = Color.white;
            _yesPlateText.text = "ДА"; _noPlateText.text = "СПАСИБО,\nНЕ НАДО";
            // Купол в спокойной позе: СВЕЖАЯ карточка, таймер ПОЛНЫЙ (t=0, дизайн-гейт просил именно
            // этот кадр), канонный YELLOW. Через ReflectDome, а не присвоением, — поза идёт тем же путём,
            // что и живой Update, и стрелка-кромка встаёт на своё место.
            ReflectDome(6f, 6f);
            enabled = false;
        }

        /// <summary>
        /// Screenshot pose (дизайн-гейт §5a): та же обычная сцена, но купол в ПОСЛЕДНЕЙ секунде — весь
        /// силуэт мигает RED_BRIGHT. Остаток 0.7 с от фазы C (6 с) — это РОВНО пик мигания
        /// (0.7 = 2 × DomeAlarmPulsePeriod), так что кадр ловит тревогу на максимуме и воспроизводим.
        /// Только визуал: чистый Game не трогается.
        /// </summary>
        public void DebugPreviewDomeLastSecond()
        {
            DebugPreviewArcadeShot();                 // обычный кадр, драйвер заморожен
            ReflectDome(2f * DomeAlarmPulsePeriod, 6f);
        }

        /// <summary>
        /// Screenshot pose: купол на ЧЕТВЕРТИ окна — граница заливки идёт ровно под 45°, т.е. это
        /// худший случай для ступенчатого края `Image.Filled` (на половине окна граница вертикальная и
        /// лесенки в принципе нет). Именно этот край и закрывает «стрелка-кромка», поэтому поза нужна
        /// дизайн-гейту для проверки сглаживания на зуме — в спокойной (жёлтой) и в тревожной фазе.
        /// Тревожный вариант ставится в ПРОВАЛ мигания, где рядом с красной дугой ещё виден крем.
        /// </summary>
        public void DebugPreviewDomeDiagonalEdge(bool alarm)
        {
            DebugPreviewArcadeShot();
            // 1.5 / 6 и 0.525 / 2.1 — обе четверти окна; вторая при этом лежит в последней секунде.
            if (alarm) ReflectDome(1.5f * DomeAlarmPulsePeriod, 6f * DomeAlarmPulsePeriod);
            else ReflectDome(1.5f, 6f);
        }

        /// <summary>Layer-2 seam: нарисовать купол по произвольной паре (осталось, полная длина) —
        /// ровно тем же путём, каким это делает Update.</summary>
        public void DebugReflectDome(float remaining, float full) => ReflectDome(remaining, full);

        /// <summary>
        /// Layer-2 seam: продвинуть чистый Game на dt и ТУТ ЖЕ перерисовать по нему купол, не дожидаясь
        /// кадра — так тест видит убывание дуги ровно за длину фазы, без вклада Time.deltaTime.
        /// </summary>
        public void DebugTick(float dt)
        {
            if (_game == null) return;
            _game.Tick(dt);
            if (_game.State != GameState.Playing) return;
            if (_game.InCrisis) ReflectDome(Mathf.Max(0f, _game.CrisisTimer), _game.CrisisTimerMax);
            else ReflectDome(Mathf.Max(0f, _game.CardTimer), _game.CardTimerMax);
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
            // «Основные надписи» = Arimo Bold (meeting-revisions §8): headlines, the card question, the
            // answer-plate labels and the HUD digits. Metric Helvetica lookalike, OFL, full Cyrillic + ₽.
            _display = Resources.Load<Font>("Fonts/Arimo-Bold");
            if (_display == null)
            {
                // Loud, not silent: without this the build would quietly fall through to Unity's
                // LegacyRuntime face and every «основная надпись» would render in the wrong typeface
                // (and Cyrillic/₽ coverage would be a lottery). Fall back to the shipped RussoOne.
                Debug.LogError("GameDriver: Resources/Fonts/Arimo-Bold missing — «основные надписи» (§8) "
                    + "fall back to RussoOne. Re-import Assets/_Project/Art/Resources/Fonts/Arimo-Bold.ttf.");
                _display = Resources.Load<Font>("Fonts/RussoOne");
            }
            _body = Resources.Load<Font>("Fonts/Rubik");
            // Rubik.ttf is a VARIABLE font whose wght axis is 300…900 with a DEFAULT of 300 — legacy uGUI
            // rasterises the default instance, so every Rubik line came out Light. The Ведущий's plate is
            // drawn HEAVY on the explainer (measured stroke/cap 0.219 against Rubik's 0.081 at 300 and 0.216
            // at 700), so the bubble runs on a static wght=700 instance cut from the same OFL file.
            _bodyBold = Resources.Load<Font>("Fonts/Rubik-Bold");
            if (_display == null) _display = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_body == null) _body = _display;
            if (_bodyBold == null)
            {
                Debug.LogError("GameDriver: Resources/Fonts/Rubik-Bold missing — реплики Ведущего рисуются "
                    + "лёгким Rubik (variable default 300). Re-import Art/Resources/Fonts/Rubik-Bold.ttf.");
                _bodyBold = _body;
            }
            _voice = new HostVoice(new System.Random().NextDouble);   // named-line priority + seeded pool
            BuildHud();
            LoadGame();
        }

        private void Start()
        {
            Input ??= gameObject.AddComponent<ArcadeInputSource>();
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
        /// Input funnel. The crank (MONEY_TICK / repeat) is the money crank and ONLY that: it cranks during
        /// Playing and is fully inert everywhere else — opener, finale AND tutorial (founder Gate-2:
        /// holding/mashing the crank must never confirm, start, restart, or skip a hint). GREEN
        /// (<see cref="GameInput.AnswerYes"/>) is the ONE physical confirm on every non-gameplay screen: it
        /// starts the game, dismisses a hint and restarts from the finale (founder 99fab3c) — RED is inert
        /// there. Enter / Numpad-Enter (CONFIRM) survives only as the HIDDEN dev emulation of that green
        /// button; no on-screen hint names it. The ~5/s income cap applies only on the gameplay-crank
        /// branch. While the overlay is up, all other input is swallowed. Pure <see cref="Game"/> gets a
        /// clean semantic event.
        /// </summary>
        private void OnInput(GameInput input)
        {
            // MenuButton (arcade §5): end the run cleanly from ANY screen/beat — highest priority so a quit
            // is never swallowed by a banner beat or a hint. No Application.Quit, no leftover state.
            if (input == GameInput.Exit) { QuitToFreshLife(); return; }

            // A hint just closed THIS frame (Confirm dismiss): swallow every later same-frame event so a
            // chorded Enter+Space/E can't leak a crank/pulse onto the frame the overlay closed. Only a
            // further Confirm passes (harmless during Playing). Cleared next frame in Update.
            if (_dismissedThisFrame && input != GameInput.Confirm) return;

            // A rubric banner beat is up (S4/S6 — the card is hidden): swallow ALL input so a masher can't
            // answer the hidden card or skip the announce unread. It auto-advances on its own ~1.5s clock.
            if (_bannerTimer.Visible && _game.State == GameState.Playing) return;

            // The arcade cabinet has no dedicated CONFIRM control (founder Gate-2 mapping). FOUNDER DECISION
            // 2026-07-29 (99fab3c): GREEN (ДА) is the ONE confirm across every non-gameplay screen — it starts
            // a life on the opener, dismisses a hint AND restarts from the finale. RED is answer-only: on the
            // finale it is deliberately INERT (Game ignores AnswerNo outside Playing), so a masher on the red
            // lever can never skip the necrolog. This overrides the earlier «рестарт = красная» row of the
            // input map. During Playing both stay ДА/НЕТ, so the card logic is untouched. Enter (CONFIRM)
            // survives as the hidden dev emulation only — no on-screen hint names it.
            if (input == GameInput.AnswerYes
                && (_game.State == GameState.Opener || _game.State == GameState.Finale || _tutorialShowing))
                input = GameInput.Confirm;
            else if (input == GameInput.AnswerYes && _game.State == GameState.Playing && _game.InDepression)
                input = GameInput.Confirm;   // depression pulse-catch: the cabinet has no Confirm control
                                             // during Playing, so GREEN (ДА) is the catch — otherwise the
                                             // depression mini-game would be unwinnable on the cabinet.

            if (_tutorialShowing)
            {
                // FOUNDER DECISION (Gate-2 playtest, re-mapped by 99fab3c): a hint dismisses on the
                // CONFIRM event ONLY — which on the cabinet means the GREEN button (mapped above), and
                // for dev the hidden Enter. She holds/mashes the crank — fresh crank ticks AND repeats
                // are both inert here, so a hint can never be skipped unread.
                if (input == GameInput.Confirm) { DismissTutorial(); _dismissedThisFrame = true; }
                return;
            }

            if (input == GameInput.MoneyTick || input == GameInput.MoneyTickRepeat)
            {
                // FOUNDER DECISION (Gate-2 round 2): the crank is the money crank and NOTHING else — it
                // must never confirm/start/restart. Outside Playing it is fully inert (she holds it through
                // the necrolog and it must not skip the payoff screen). This also matches the hardware
                // abstraction: the crank encoder and the confirm control are separate physical controls, so
                // the crank must never fire a confirm — the GREEN button is the confirm (99fab3c).
                if (_game.State != GameState.Playing) return;
                if (!_crankCap.TryAccept()) return;         // income cap (anti-mashgun) — gameplay only
                _game.HandleInput(GameInput.MoneyTick);     // Game sees only the semantic crank event
                if (_game.MoneyOpen && isActiveAndEnabled)  // coin drops into the jar on each PAYING tick
                {
                    if (_moneyPulse != null) StopCoroutine(_moneyPulse);
                    _moneyPulse = StartCoroutine(DropCoin());
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

        /// <summary>
        /// MenuButton clean-exit (arcade contract §5). Ends the current run: stop our own coroutines/timers,
        /// drop any open hint/banner pause, and return the pure <see cref="Game"/> to a fresh opener life.
        /// No <c>Application.Quit</c>, no static or DontDestroyOnLoad state — so the launcher reloading the
        /// entry scene is always a clean new life (SceneBootTests boots straight into the opener).
        /// </summary>
        private void QuitToFreshLife()
        {
            StopAllCoroutines();
            _moneyPulse = null;
            _cardAnim = null;
            if (_tutorialShowing)
            {
                _tutorialShowing = false;
                if (_tutorialOverlay != null) _tutorialOverlay.SetActive(false);
            }
            _bubbleTimer.Hide();
            _bannerTimer.Hide();
            _breakupTimer.Hide();
            if (_game != null)
            {
                _game.Paused = false;
                _game.AbortToOpener();   // full reset to a fresh opener life (fires StateChanged → Refresh)
            }
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
            SpinBackground(Time.deltaTime);      // ambient §7 ray spin — runs on every screen, pause included
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
                RenderCrisis();   // S6 blitz / S13 impulse — reuses the plates + dome timer, freezes normal HUD
            }
            else if (_game.State == GameState.Playing)
            {
                if (_crisisUiActive) RestoreNormalPlates();   // just resumed from a crisis → restore the plates
                _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
                _moneyText.text = FormatMoneyJar(_game.Money);   // live: ticks up on crank, drains down
                // Live battery/bars move on their own (decay/drain/breath), not just on cards.
                var s = _game.Scales;
                ReflectEnergyLevel(s.Energy);
                ReflectHealthMarker(s.Health);
                ReflectRelationsMarker(s.Relationships, _game.RelationshipRedZone);
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

                // Купол-таймер: дуга-остаток убывает ровно за длину ТЕКУЩЕЙ фазы (§3), не за фикс. 5 с.
                // На паузе (туториал/баннер-бит) Game.Tick не двигает CardTimer → купол сам заморожен.
                ReflectDome(Mathf.Max(0f, _game.CardTimer), _game.CardTimerMax);
            }
            ReflectHostReveals(_game.State == GameState.Playing);   // advances the banner-beat clock + pause
            ReflectBannerBeat();                                    // hide the card/plates while the beat is up
            ReflectBreakupPlate(_game.State == GameState.Playing);
            UpdateBrightness();
            ReflectDepression();   // B&W wash + grain + pulse while Game.InDepression (above the show veil)
        }

        // Midlife-crisis render (S6 blitz / S13 impulse). Reuses the card marquee (thought/impulse text),
        // the two answer plates (relabelled), and the dome timer (on the fast crisis clock). The normal HUD
        // (bars/money/balancer) is intentionally frozen — the 5 scales are paused in Game during the crisis.
        private void RenderCrisis()
        {
            _crisisUiActive = true;
            var c = _game.CurrentCard;
            _cardText.text = c != null ? c.Question : "";

            // S6 minimal HUD: only the age badge + the crisis counter show during the crisis — hide the
            // money jar / bars / balancer / child (re-revealed by ApplyAgeGates the moment play resumes).
            _moneyGroup.SetActive(false);
            _healthGroup.SetActive(false);
            _energyGroup.SetActive(false);
            _balancerGroup.SetActive(false);
            if (_childGroup != null) _childGroup.SetActive(false);

            // BLOCK$ visuals never apply during a crisis.
            SetCardBlockedDim(false);
            _blockBanner.SetActive(false);
            _cardPriceText.gameObject.SetActive(false);
            _cardPricePlate.gameObject.SetActive(false);
            ReflectCardTextBand();          // band free again → the thought/impulse text gets the full box

            bool blitz = _game.Phase == CrisisPhase.Blitz;
            if (blitz)
            {
                // «ВСЁ НОРМАЛЬНО» jumps between the two plates each thought (the randomisation lives in
                // Game.BlitzNormalOnYes, which names the ДА/yes LEVER — the plate that lever drives sits on
                // the RIGHT since §9, so painting the label here is what ties the lever to a screen side).
                // Colour-to-meaning is unaffected: both blitz plates render plain white.
                bool normalOnYes = _game.BlitzNormalOnYes;
                _yesPlateText.text = normalOnYes ? "ВСЁ\nНОРМАЛЬНО" : "О НЕТ";
                _noPlateText.text = normalOnYes ? "О НЕТ" : "ВСЁ\nНОРМАЛЬНО";
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
                // Impulse (S13): рычаг ДА (правая плашка) = поддаться, рычаг НЕТ (левая) = «СПАСИБО, НЕ НАДО». Highlight the decline
                // plate (обратный акцент) — the gold tint follows the MEANING, so it stays on the red plate.
                // Impulse keeps the code-plates at the normal S13 sizes (per the accepted 03 shot) — the
                // baked art can't be relabelled «поддаться/отказ» and must not show through here.
                UseImpulsePlates();
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

            // Купол на кризисном таймере (5с блиц / 3с импульс) — тот же виджет, та же дуга.
            ReflectDome(Mathf.Max(0f, _game.CrisisTimer), _game.CrisisTimerMax);
        }

        // Restore the plates + hide the crisis widgets when ordinary play resumes (called once on the
        // first normal frame after a crisis). OnCardChanged (fired by ResumeAfterCrisis) re-renders the card.
        private void RestoreNormalPlates()
        {
            _crisisUiActive = false;
            UseBakedPlates();    // back to the baked art (and the crisis label overlay goes away with it)
            _yesPlateText.text = "ДА";
            _noPlateText.text = "СПАСИБО,\nНЕ НАДО";
            _yesPlate.color = Color.white;
            _noPlate.color = Color.white;
            if (_crisisInfo != null) _crisisInfo.SetActive(false);
            if (_impulseWarning != null) _impulseWarning.SetActive(false);
        }

        // ---- Answer-plate geometry ------------------------------------------------------------------
        // ORDINARY PLAY — the art-pack plates with BAKED lettering (`btn-no` 1422×685, `btn-yes` 907×594).
        // Drawn Simple (the art is NOT a 9-slice), so the sprite spans the whole rect and the drawn artwork
        // fills the rect's alpha-tight fraction (no: 96.84%×95.91% · yes: 96.58%×97.14% — measured on the
        // PNGs). The rects below are the reference-screen sizes, derived instrumentally: on
        // «Экран спокойный обычный.png» the plates' colour fill measures 505.5×221.3 (red) and 335.7×205.6
        // (green) after undoing the tilt, and the SAME fill in the source art measures 1279×559 / 781×477 →
        // a UNIFORM scale of 0.3956 / 0.4304 for the whole texture. Hence 1422×685→562×271 and
        // 907×594→390×256: the drawn plate lands on the reference within ~0.5 px on both axes, and the rect
        // keeps the texture's aspect (±0.3%) so the baked lettering is never stretched.
        private static readonly Vector2 BakedYesPlateSize = new Vector2(390f, 256f);
        private static readonly Vector2 BakedNoPlateSize = new Vector2(562f, 271f);

        // CRISIS ONLY — the blank code-plates (9-slice, 460×270 with an 82px border) the blitz/impulse still
        // use because they get RELABELLED per thought. Their coloured pill sits ~55px in from every rect edge
        // REGARDLESS of the rect size (fixed 9-slice corners), so a rect of W×H shows a pill of only
        // ~(W−110)×(H−110): the blitz «ВСЁ НОРМАЛЬНО» needs a genuinely large pill or best-fit resolves a font
        // whose two lines spill off it. The blitz uses an enlarged, EQUAL pair; the impulse keeps the S13 pair.
        private static readonly Vector2 ImpulseYesPlateSize = new Vector2(385f, 278f);
        private static readonly Vector2 ImpulseNoPlateSize = new Vector2(615f, 275f);
        private static readonly Vector2 CrisisBlitzPlateSize = new Vector2(615f, 280f);

        // Ordinary play: baked art, NO dynamic label (the words are part of the picture — a live Text on top
        // would double them). Idempotent; called on build, on every restart and when a crisis ends.
        private void UseBakedPlates()
        {
            if (_yesPlate.sprite != _bakedYesSprite) _yesPlate.sprite = _bakedYesSprite;
            if (_noPlate.sprite != _bakedNoSprite) _noPlate.sprite = _bakedNoSprite;
            _yesPlate.type = Image.Type.Simple;
            _noPlate.type = Image.Type.Simple;
            _yesRect.sizeDelta = BakedYesPlateSize;
            _noRect.sizeDelta = BakedNoPlateSize;
            if (_yesPlateText.gameObject.activeSelf) _yesPlateText.gameObject.SetActive(false);
            if (_noPlateText.gameObject.activeSelf) _noPlateText.gameObject.SetActive(false);
        }

        // Crisis: blank code-plates + the dynamic label back on (the caller sets the text/colours).
        private void UseCodePlates(Vector2 yesSize, Vector2 noSize)
        {
            if (_yesPlate.sprite != _codeYesSprite) _yesPlate.sprite = _codeYesSprite;
            if (_noPlate.sprite != _codeNoSprite) _noPlate.sprite = _codeNoSprite;
            _yesPlate.type = Image.Type.Sliced;
            _noPlate.type = Image.Type.Sliced;
            _yesRect.sizeDelta = yesSize;
            _noRect.sizeDelta = noSize;
            if (!_yesPlateText.gameObject.activeSelf) _yesPlateText.gameObject.SetActive(true);
            if (!_noPlateText.gameObject.activeSelf) _noPlateText.gameObject.SetActive(true);
        }

        // Enlarge both blitz plates equally (S6) and push the text rect inside the (now taller) colored pill.
        private void UseCrisisBlitzPlates()
        {
            UseCodePlates(CrisisBlitzPlateSize, CrisisBlitzPlateSize);
            CrisisPlateTextRect(_yesPlateText.rectTransform);
            CrisisPlateTextRect(_noPlateText.rectTransform);
            _yesPlateText.resizeTextMinSize = 28; _yesPlateText.resizeTextMaxSize = 48;
            _noPlateText.resizeTextMinSize = 28; _noPlateText.resizeTextMaxSize = 48;
        }

        // S13 impulse plates: the code-plates at the sizes/insets the accepted 03 shot uses.
        private void UseImpulsePlates()
        {
            UseCodePlates(ImpulseYesPlateSize, ImpulseNoPlateSize);
            PlateTextRect(_yesPlateText.rectTransform);
            PlateTextRect(_noPlateText.rectTransform);
            _yesPlateText.resizeTextMinSize = 40; _yesPlateText.resizeTextMaxSize = 120;
            _noPlateText.resizeTextMinSize = 24; _noPlateText.resizeTextMaxSize = 60;
        }

        // The game is paused while EITHER a tutorial overlay OR a rubric banner beat is up. Both share the
        // single Game.Paused freeze (age, drains, cost-of-living, card timer, crisis timer). Kept in sync
        // from one place so a banner beat and a tutorial can never leave the pause flag stale.
        private void SyncPause()
        {
            if (_game == null) return;
            _game.Paused = _tutorialShowing || _bannerTimer.Visible;
        }

        // While a rubric banner beat is up (S4/S6), the card marquee, the two answer plates and the dome
        // timer are HIDDEN so the banner is its own beat and can never overlap a card (the founder bug).
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
            // Unmissable flash: the button flares gold and jumps ~1.3× (was a barely-there 1.1× tint pulse),
            // and the halo behind it flares bright and breathes. At rest the halo is fully transparent and the
            // button sits at its idle size — so the «жми Enter по вспышке» beat is impossible to miss.
            // Both pulses stay within their PEAK constants — the layout guard clears the peak, so the live
            // rects can never leave the jar↔badge gap.
            _childButtonImg.rectTransform.localScale = lit
                ? Vector3.one * (ChildButtonPulseMid + ChildButtonPulseAmp * Mathf.Sin(Time.time * 11f))
                : Vector3.one;
            if (_childGlow != null)
            {
                float a = lit ? 0.55f + 0.30f * Mathf.Sin(Time.time * 11f) : 0f;
                _childGlow.color = new Color(Bulb.r, Bulb.g, Bulb.b, a);
                _childGlow.rectTransform.localScale = lit
                    ? Vector3.one * (ChildGlowPulseMid + ChildGlowPulseAmp * Mathf.Sin(Time.time * 11f))
                    : Vector3.one;
            }
        }

        // Transient «РАССТАЛИСЬ» plate: advance its own ~2s clock and mirror visibility (only while
        // Playing). The timer keeps its state, so leaving play simply hides it.
        private void ReflectBreakupPlate(bool playing)
        {
            _breakupTimer.Advance(Time.deltaTime);
            bool show = playing && _breakupTimer.Visible;
            if (_breakupPlate.activeSelf != show) _breakupPlate.SetActive(show);
        }

        // §7 ambient background: the sunburst turns 1 revolution per 60 s (6°/сек) CLOCKWISE — in Unity's
        // CCW-positive z that is a NEGATIVE angle. Deliberately not frozen by Game.Paused: the spin is
        // ambience behind tutorials/banners, not a gameplay clock.
        private void SpinBackground(float dt)
        {
            if (_bg == null) return;
            _bgSpin = Mathf.Repeat(_bgSpin + BgSpinDegPerSecond * dt, 360f);
            _bg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_bgSpin);
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

            // Shared sunburst background — `sunburst-bg-v3`, the screen-space synthesis (see the field's
            // comment). Drawn 1:1 (2210² texture into a 2210² rect), centre pivot on the screen centre, and
            // it SPINS (meeting-revisions §7 / build-spec §6: 1 turn per 60 s = 6°/сек, clockwise, on every
            // screen). NO star accents — the reference background is clean.
            _bg = NewSprite("Background", canvasGo.transform, Sprite("sunburst-bg-v3"));
            var bgRt = _bg.rectTransform;
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.pivot = BgSpinPivot;
            bgRt.anchoredPosition = Vector2.zero;
            bgRt.sizeDelta = new Vector2(BgOverscanW, BgOverscanH);

            BuildOpener(canvasGo.transform);
            BuildGamePanel(canvasGo.transform);
            BuildFinale(canvasGo.transform);

            // Show-reaction veil: above the panels (dims the whole show), below the tutorial overlay.
            _brightness = NewSolid("BrightnessVeil", canvasGo.transform, new Color(0.02f, 0.03f, 0.10f, 0f));
            Stretch(_brightness.rectTransform);

            BuildDepressionOverlay(canvasGo.transform);  // B&W wash + grain + pulse (above the show veil)

            BuildTutorialOverlay(canvasGo.transform);   // top-most: dims every screen when up
        }

        // ---- S1 opener geometry, MEASURED off `explainers/Стартовый экран.png` -------------------------
        // The explainer is 2752×1536, so every box below is the measured source box × 0.6977 (→ 1920×1080):
        //   логотип      src x639..2096 y59..742    → 1920: x446..1462 y41..518  → centre (954,279) 1017×477
        //   марки-рамка  src x97..2674  y745..1493  → 1920: x68..1866  y520..1042 → centre (967,781) 1798×522
        //   плашка правил src x150..2624 y797..1442 → 1920: x105..1831 y556..1006 → centre (968,781) 1726×450
        //   ряд лампочек (осевая линия золотой полосы) → 1920: x87..1849 y538..1024, 48 поперёк / 13 вдоль,
        //   шаг 37.5 / 40.5, диаметр ≈24 — все три сняты инструментально (PIL), не на глаз.
        private const float OpenerLogoCx = 954f, OpenerLogoCy = 279f;
        private const float OpenerLogoW = 1017f, OpenerLogoH = 477f;
        private const float OpenerPlateCx = 966f, OpenerPlateCy = 781f;   // рамка и плашка соосны
        private const float OpenerFrameW = 1798f, OpenerFrameH = 522f;
        private const float OpenerCreamW = 1726f, OpenerCreamH = 450f;
        private const float OpenerBulbInset = 18.5f;   // от края рамки до осевой линии лампочек
        private const float OpenerBulbSize = 24f;
        private const int OpenerBulbsAcross = 48, OpenerBulbsDown = 13;
        private const float OpenerRim = 6f;            // тёмный кант вокруг рамки и вокруг кремовой плашки

        // `bar-track` — не белая заготовка: её заливка #E7E9F5, и uGUI УМНОЖАЕТ тинт на неё, так что
        // «покрасить в токен» напрямую даёт ~10 % грязи (замерено на кадре: золото выходило 224,164,47
        // вместо 248,180,50). Делим токен на заливку спрайта; канал ярче заливки недостижим и просто
        // упирается в неё (у крема/золота это красный: 231 вместо 247/248 — Δ6 %, глазом не читается).
        // ⚠ Гасить ПРОПОРЦИОНАЛЬНО, не клампом по каналу: у крема #F7E4BB красный (247) выше заливки (231),
        // а зелёный (228) — нет; кламп одного красного равняет R и G и уводит кремовую плашку в хаки
        // (проверено на кадре). Поэтому берём общий множитель по самому тесному каналу — тон сохраняется,
        // плашка садится на 6.5 % темнее токена, и это единственное, что физически достижимо на этом спрайте.
        private static readonly Color BarTrackFill = new(231f / 255f, 233f / 255f, 245f / 255f);
        /// <summary>
        /// `bar-track`'s own fill, exposed so a test can compute the RENDERED colour of a tinted plate
        /// (uGUI multiplies the tint by the sprite: rendered = <see cref="BarTrackFillToken"/> × Image.color)
        /// instead of trusting the raw tint value.
        /// </summary>
        public static Color BarTrackFillToken => BarTrackFill;
        private static Color OnBarTrack(Color target)
        {
            float s = 1f;
            if (target.r > 0.001f) s = Mathf.Min(s, BarTrackFill.r / target.r);
            if (target.g > 0.001f) s = Mathf.Min(s, BarTrackFill.g / target.g);
            if (target.b > 0.001f) s = Mathf.Min(s, BarTrackFill.b / target.b);
            return new Color(target.r * s / BarTrackFill.r,
                             target.g * s / BarTrackFill.g,
                             target.b * s / BarTrackFill.b, target.a);
        }

        /// <summary>
        /// Канон-текст правил опенера (build-spec §A, БЕЗ «5 секунд»). Переносы расставлены ВРУЧНУЮ ровно
        /// по строкам эталона (5 строк) — автоперенос по ширине ректа рвал второе предложение в другом
        /// месте и оставлял куцую строку в две трети пустоты.
        /// </summary>
        public const string OpenerRulesText =
            "Добро пожаловать в увлекательное шоу длинною в жизнь!\n" +
            "Пройди от 1 года до 100 лет, постарайся принять\n" +
            "правильные решения и за всем уследить.\n" +
            "Со временем жизнь будет становиться всё сложнее и быстрее.\n" +
            "Уследить за всем невозможно, но давай попробуем!";

        /// <summary>CTA опенера — называет ФИЗИЧЕСКИЙ контрол (founder 99fab3c), не dev-клавишу.</summary>
        public const string OpenerStartHintText = "НАЧАТЬ ЖИЗНЬ — ЖМИ ЗЕЛЁНУЮ";

        private void BuildOpener(Transform parent)
        {
            _openerPanel = NewGroup("Opener", parent);

            // (1) Логотип шоу — вырезка из эталона (`opener-logo-v2`, альфа снята заливкой фона по тёмному
            // контуру), кладётся 1:1 в свой измеренный бокс. Фон под ним — общие вращающиеся лучи HUD.
            _openerLogo = NewSprite("Logo", _openerPanel.transform, Sprite("opener-logo-v2"));
            AnchorPx(_openerLogo.rectTransform, OpenerLogoCx, OpenerLogoCy, OpenerLogoW, OpenerLogoH);

            // (2) Марки-рамка: тёмный кант → золотая полоса → тёмный кант → кремовая плашка. bar-track —
            // это залитый скруглённый прямоугольник (9-slice), так что рамка собирается слоями, а не
            // растягиванием готового marquee-frame-bulbs (у него нет 9-slice-бордера: лампочки поплыли бы).
            var frameEdge = NewSprite("FrameEdge", _openerPanel.transform, Sprite("bar-track"));
            frameEdge.type = Image.Type.Sliced;
            frameEdge.color = OnBarTrack(Ink);
            AnchorPx(frameEdge.rectTransform, OpenerPlateCx, OpenerPlateCy,
                OpenerFrameW + 2f * OpenerRim, OpenerFrameH + 2f * OpenerRim);

            var frame = NewSprite("MarqueeFrame", _openerPanel.transform, Sprite("bar-track"));
            frame.type = Image.Type.Sliced;
            frame.color = OnBarTrack(MarqueeGold);
            AnchorPx(frame.rectTransform, OpenerPlateCx, OpenerPlateCy, OpenerFrameW, OpenerFrameH);

            // (3) Лампочки по осевой линии золотой полосы — `marquee-bulb` (уже золотая с бликом), шагом
            // с эталона. Углы общие, поэтому боковые колонки идут без первой и последней позиции.
            float hx = OpenerFrameW * 0.5f - OpenerBulbInset;
            float hy = OpenerFrameH * 0.5f - OpenerBulbInset;
            int bulb = 0;
            for (int i = 0; i < OpenerBulbsAcross; i++)
            {
                float x = Mathf.Lerp(-hx, hx, i / (float)(OpenerBulbsAcross - 1));
                AddBulb(frame.transform, x, hy, bulb++);
                AddBulb(frame.transform, x, -hy, bulb++);
            }
            for (int i = 1; i < OpenerBulbsDown - 1; i++)
            {
                float y = Mathf.Lerp(hy, -hy, i / (float)(OpenerBulbsDown - 1));
                AddBulb(frame.transform, -hx, y, bulb++);
                AddBulb(frame.transform, hx, y, bulb++);
            }

            var plateEdge = NewSprite("PlateEdge", _openerPanel.transform, Sprite("bar-track"));
            plateEdge.type = Image.Type.Sliced;
            plateEdge.color = OnBarTrack(Ink);
            AnchorPx(plateEdge.rectTransform, OpenerPlateCx, OpenerPlateCy,
                OpenerCreamW + 2f * 5f, OpenerCreamH + 2f * 5f);

            _openerPlate = NewSprite("RulesPlate", _openerPanel.transform, Sprite("bar-track"));
            _openerPlate.type = Image.Type.Sliced;
            _openerPlate.color = OnBarTrack(OpenerCream);
            AnchorPx(_openerPlate.rectTransform, OpenerPlateCx, OpenerPlateCy, OpenerCreamW, OpenerCreamH);

            // (4) Канон-текст правил ЦЕЛИКОМ внутри кремовой плашки, Rubik, INK, по центру. Rubik.ttf —
            // вариативный с дефолтом wght=300 (Light), а на эталоне обводка/капитель = 0.135 (≈Regular),
            // так что лёгкое начертание догоняется однопиксельным Outline того же цвета: чисто «вес»,
            // без тени и без каймы другого цвета.
            _openerRules = NewText("RulesText", _openerPlate.transform, OpenerRulesText, 46,
                TextAnchor.MiddleCenter, Ink, _body);
            var rrt = _openerRules.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(1620f, 260f);
            rrt.anchoredPosition = new Vector2(0f, 68f);
            _openerRules.resizeTextForBestFit = true;
            _openerRules.resizeTextMinSize = 26; _openerRules.resizeTextMaxSize = 46;
            _openerRules.verticalOverflow = VerticalWrapMode.Truncate;   // best-fit честно держит и ВЫСОТУ
            var weight = _openerRules.gameObject.AddComponent<Outline>();
            weight.effectColor = Ink;
            weight.effectDistance = new Vector2(1f, 1f);

            // (5) CTA — зелёная плашка в низу кремовой: «НАЧАТЬ ЖИЗНЬ — ЖМИ ЗЕЛЁНУЮ», Arimo Bold. Кнопка
            // САМА зелёная, так что подсказка совпадает с физической кнопкой кабинета (founder 99fab3c).
            // plate-yes сюда не годится: его 9-slice-бордер 82 px выше самой плашки (104) и раздавил бы её.
            var startEdge = NewSprite("StartPlateEdge", _openerPanel.transform, Sprite("bar-track"));
            startEdge.type = Image.Type.Sliced;
            startEdge.color = OnBarTrack(Ink);
            AnchorPx(startEdge.rectTransform, OpenerPlateCx, 921f, 972f, 116f);

            var start = NewSprite("StartPlate", _openerPanel.transform, Sprite("bar-track"));
            start.type = Image.Type.Sliced;
            start.color = OnBarTrack(GoGreen);
            AnchorPx(start.rectTransform, OpenerPlateCx, 921f, 960f, 104f);
            var startText = NewText("StartText", start.transform,
                OpenerStartHintText, 46, TextAnchor.MiddleCenter, Ink, _display);
            Inset(startText.rectTransform, 26f);   // ≥ видимого скругления bar-track (16) → глифы всегда на плашке
            startText.resizeTextForBestFit = true; startText.resizeTextMinSize = 28; startText.resizeTextMaxSize = 46;
            startText.verticalOverflow = VerticalWrapMode.Truncate;
            // No DisplayFx: dark Ink text on the green pill needs no dark outline (it muddies it to a blob).
        }

        private void AddBulb(Transform frame, float x, float y, int index)
        {
            var b = NewSprite("Bulb" + index, frame, Sprite("marquee-bulb"));
            var rt = b.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(OpenerBulbSize, OpenerBulbSize);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private void BuildGamePanel(Transform parent)
        {
            _gamePanel = NewGroup("Game", parent);

            // ---- Купол-таймер (§5a) — ПЕРВЫМ ребёнком панели, т.е. ПОД всем HUD ------------------------
            // Сменил круглое кольцо (252,590) целиком: слоёв кольца, его рефлектов и цифры секунд больше
            // нет. Цифры в куполе НЕТ по спеку («купол = только дуга»): read-out — сама дуга + тревожный
            // цвет в последнюю секунду.
            // Решение основательницы: купол крупный (бокс §2 760,0,400,130), по центру, и лежит СЛОЕМ
            // НИЖЕ баров — бары дорисованы поверх него, дуга читается в просветах (полоса над барами и
            // коридор между ними). Отсюда порядок: Timer создаётся ДО HudRow (ниже по siblingIndex), но
            // после фона-лучей (тот — сосед _gamePanel).
            // Полукруг рисуется ПРОЦЕДУРНО (MakeDomeSprite), как остальной сгенерённый арт; один спрайт
            // на три слоя, соосных по центру «окружности» (DomeCx, 0):
            //   DomeOutline (внешний бокс, чёрный) · DomeTrack (вложен на толщину обводки, CREAM) ·
            //   DomeFill (тот же вложенный бокс, Radial180 от плоской верхней грани, YELLOW →
            //   RED_BRIGHT в последнюю секунду) · DomeHand («стрелка-кромка» по границе заливки).
            var domeGroup = NewGroup("Timer", _gamePanel.transform);
            _timerGroup = domeGroup;
            _domeSprite = MakeDomeSprite();
            _domeOutline = NewSprite("DomeOutline", domeGroup.transform, _domeSprite);
            AnchorPx(_domeOutline.rectTransform, DomeCx, DomeH / 2f, DomeW, DomeH);
            _domeOutline.color = DomeInk;
            // Внутренний бокс: обводка INK торчит из-под трека на DomeOutlineWidth со всех КРИВЫХ сторон
            // (сверху плоская грань лежит на крае экрана, там обводки нет — купол «врезан» в верхний край).
            float inW = DomeW - 2f * DomeOutlineWidth;
            float inH = DomeH - DomeOutlineWidth;
            _domeTrack = NewSprite("DomeTrack", domeGroup.transform, _domeSprite);
            AnchorPx(_domeTrack.rectTransform, DomeCx, inH / 2f, inW, inH);
            _domeTrack.color = Cream;                              // «истёкшая» часть купола
            _domeFill = NewSprite("DomeFill", domeGroup.transform, _domeSprite);
            AnchorPx(_domeFill.rectTransform, DomeCx, inH / 2f, inW, inH);
            // Radial180 с origin на ВЕРХНЕЙ (плоской) грани: ось развёртки проходит через центр
            // купола, поэтому дуга-остаток убывает вдоль самого купола, а не по хорде. Направление
            // (какой край тает первым) зафиксировано пиксельно в DomeTimerTests.
            _domeFill.type = Image.Type.Filled;
            _domeFill.fillMethod = Image.FillMethod.Radial180;
            _domeFill.fillOrigin = (int)Image.Origin180.Top;
            _domeFill.fillClockwise = true;
            _domeFill.fillAmount = 1f;
            _domeFill.color = DomeYellow;

            // «Стрелка-кромка» — ПОСЛЕДНИМ слоем купола, поверх заливки: тонкая чёрная линия из центра
            // купола вдоль радиуса, на котором стоит граница заливки. Её задача двойная — читаемый
            // «ход времени» и маскировка ступенчатого (без AA) края `Image.Filled`. Пивот — в ВЕРХНЕЙ
            // точке линии, т.е. в центре купола: поворот вокруг него и есть ход стрелки.
            _domeHandSprite = MakeDomeHandSprite();
            _domeHand = NewSprite("DomeHand", domeGroup.transform, _domeHandSprite);
            _domeHand.color = DomeInk;
            var handRt = _domeHand.rectTransform;
            handRt.anchorMin = handRt.anchorMax = new Vector2(DomeCx / 1920f, 1f);
            handRt.pivot = new Vector2(0.5f, 1f);
            handRt.anchoredPosition = Vector2.zero;
            handRt.sizeDelta = new Vector2(DomeHandWidth, inH);
            ReflectDomeHand(1f);

            // ---- HUD row — the art pack, pixel-placed off «Экран спокойный обычный.png» (asset-map §2) ----
            // A dedicated container so the row can be enumerated (no stray/placeholder Image) and so the
            // whole row hides as one on a rubric beat. Rects come from the geometry block above.
            _hudRow = NewGroup("HudRow", _gamePanel.transform);

            BuildBattery();       // энергия — v3_energy, заливка полости
            BuildRelBar();        // отношения — hf_relationship_cute_c + свой маркер
            BuildHealthBar();     // здоровье  — patched copy + свой маркер
            BuildMoneyJar();      // деньги    — банка + сумма в лейбле + монета
            BuildAgeBadge();      // возраст   — бейдж + цифра ~112 px, без подписи

            // Кнопка-ребёнок (заглушка, инкремент «звонок»): moved out of the art row's way — the jar now
            // owns the old 1850,88 corner. Lives in the free gap BETWEEN the jar and the age badge; its
            // size comes from that gap (geometry block, ChildCx/ChildCy/ChildGlowSize).
            BuildChildButton();

            // ---- Card — the art-pack cream plate (`choice-plate-v2`), INK question on the cream field ----
            // Image.Type.Simple, never 9-slice: the plate's tabs sit at the middle of each side and its stars
            // sit in the corners, so a sliced draw would stretch both (asset-map §1.1/§5).
            _cardRoot = NewGroup("Card", _gamePanel.transform).GetComponent<RectTransform>();
            AnchorPx(_cardRoot, CardPlateRect.x, CardPlateRect.y, CardPlateRect.z, CardPlateRect.w);
            _cardFrame = NewSprite("CardFrame", _cardRoot, Sprite("choice-plate-v2"));
            _cardFrame.type = Image.Type.Simple;
            Stretch(_cardFrame.rectTransform);
            // Question text on the plate's SAFE box (asset-map §8: screen 578,322,764,535 — inside the cream
            // field 467,286,984,606), expressed as a fraction of the card rect. Ink on cream: NO DisplayFx —
            // a dark outline on dark letters over a light plate just muddies them (the reference is flat black).
            _cardText = NewText("CardText", _cardRoot, "", 64, TextAnchor.MiddleCenter, Ink, _display);
            SetCardTextBottom(CardTextFullBottom);   // full §8 safe box until the bottom band is occupied
            _cardText.resizeTextForBestFit = true;   // auto-shrink long questions to fit the plate
            _cardText.resizeTextMinSize = 30;
            _cardText.resizeTextMaxSize = 64;
            // Truncate (not the NewText default Overflow) so best-fit honours HEIGHT too: a long question (the
            // deck's longest is 67 chars) otherwise rendered too big and spilled off the cream field.
            _cardText.verticalOverflow = VerticalWrapMode.Truncate;

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
            // Low on the taller art-pack plate but still INSIDE its cream field (screen y≈705..815 of the
            // field's 286..892) — the old «just below the card» anchor now lands on the answer plates.
            Anchor(blockBannerImg.rectTransform, new Vector2(0.5f, 0.2784f), new Vector2(900, 110));
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
            // Bottom of the cream field (screen y≈850), under the block banner — the art-pack plate reaches
            // y≈972, so the old below-the-card anchor would now sit on the answer plates.
            Anchor(_cardPricePlate.rectTransform, new Vector2(0.5f, 0.1604f), new Vector2(360, 78));
            _cardPriceText = NewText("CardPrice", _cardRoot, "", 40, TextAnchor.MiddleCenter, Bulb, _body);
            Anchor(_cardPriceText.rectTransform, new Vector2(0.5f, 0.1604f), new Vector2(820, 78));
            // ONE line by design («цена N ₽»). ApplyPriceLabel sizes the rect to preferredWidth, and font
            // metrics round differently at different canvas scales — with Wrap a half-pixel shortfall threw
            // the «₽» onto a second line that hung off the dark plate. Overflow makes that unreachable.
            _cardPriceText.horizontalOverflow = HorizontalWrapMode.Overflow;
            DisplayFx(_cardPriceText);
            _cardPricePlate.gameObject.SetActive(false);
            _cardPriceText.gameObject.SetActive(false);

            // ---- Answer plates (bottom) — RED «СПАСИБО, НЕ НАДО» LEFT, GREEN «ДА» RIGHT ----
            // Sides/centres/tilts are canon from meeting-revisions §9 + build-spec §B (boxes 165,735,615,275
            // and 1354,732,385,278), i.e. exactly the cabinet levers: left lever = НЕ НАДО (RedButton →
            // AnswerNo), right lever = ДА (GreenButton → AnswerYes). Only the SCREEN side moved — the input
            // mapping in ArcadeInputSource is unchanged.
            // Ordinary play draws the ART-PACK plates, lettering baked in; the blank code-plates are kept
            // loaded for the crisis relabel (UseBakedPlates / UseCodePlates swap the pair wholesale).
            _bakedYesSprite = Sprite("btn-yes");
            _bakedNoSprite = Sprite("btn-no");
            _codeYesSprite = Sprite("plate-yes");
            _codeNoSprite = Sprite("plate-no");

            _yesPlate = NewSprite("YesPlate", _gamePanel.transform, _bakedYesSprite);
            _yesPlate.type = Image.Type.Simple;
            _yesRect = _yesPlate.rectTransform;
            AnchorPx(_yesRect, 1547f, 871f, BakedYesPlateSize.x, BakedYesPlateSize.y);
            _yesRect.localRotation = Quaternion.Euler(0, 0, YesTilt);
            // Crisis-only overlay label — «ДА» is baked into the art, so this stays HIDDEN in ordinary play.
            // (White with the ink kant, matching the baked lettering, for the «ВСЁ НОРМАЛЬНО»/«О НЕТ» relabel.)
            var yesText = NewText("YesText", _yesPlate.transform, "ДА", 96, TextAnchor.MiddleCenter, Color.white, _display);
            PlateTextRect(yesText.rectTransform);   // inset onto the visible plate (clears the baked shadow)
            yesText.resizeTextForBestFit = true; yesText.resizeTextMinSize = 40; yesText.resizeTextMaxSize = 120;
            DisplayFx(yesText);
            _yesPlateText = yesText;

            _noPlate = NewSprite("NoPlate", _gamePanel.transform, _bakedNoSprite);
            _noPlate.type = Image.Type.Simple;
            _noRect = _noPlate.rectTransform;
            // Centre 432 (not the §B box centre 472.5): on the reference explainer the red art is FLUSH LEFT
            // in its build-spec box, so the drawn plate must start at x≈169 — canon call by the Maintainer,
            // «explainer-PNG = пиксель-истина, табличные боксы — ориентир». The green plate is box-centred.
            AnchorPx(_noRect, 432f, 872f, BakedNoPlateSize.x, BakedNoPlateSize.y);
            _noRect.localRotation = Quaternion.Euler(0, 0, NoTilt);
            var noText = NewText("NoText", _noPlate.transform, "СПАСИБО,\nНЕ НАДО", 56, TextAnchor.MiddleCenter, Color.white, _display);
            PlateTextRect(noText.rectTransform);   // inset onto the visible plate (clears the baked shadow)
            noText.resizeTextForBestFit = true; noText.resizeTextMinSize = 24; noText.resizeTextMaxSize = 60;
            DisplayFx(noText);
            _noPlateText = noText;
            UseBakedPlates();   // hides both overlay labels — ordinary play shows the baked art alone

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
            // Right under the relationships bar it reports on (the old 0.775/0.70 anchor now lands on the
            // age badge). Transient (~2s) — drawn above the card.
            AnchorPx(_breakupPlate.GetComponent<RectTransform>(), 674f, 215f, 360f, 96f);
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
        // two buttons and the fast timer REUSE the existing answer plates + dome timer (relabelled in-place).
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
            // Sits above the enlarged §9 answer plates (their tilted AABBs start at y≈703) so the pill never
            // clips a plate corner.
            AnchorPx(warnImg.rectTransform, 940f, 650f, 380f, 84f);
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

        // ---- КУПОЛ: процедурный полукруг --------------------------------------------------------------
        // Отдельного спрайта купола в assets_new нет (build-spec §2), поэтому форма генерится кодом, как
        // остальной рисованный-в-рантайме арт (mute-иконка, зерно депрессии). Плоская сторона — СВЕРХУ,
        // т.е. центр окружности лежит на верхней грани спрайта; ровно это и даёт «свисает с верхнего края».
        // Белый RGB + альфа-маска: цвет каждого слоя задаёт Image.color (INK / CREAM / YELLOW).
        // Текстура генерится с большим запасом (512×256) — тот же спрайт растягивается и на внешний бокс,
        // и на внутренний, а на 4K-канвасе не мылится. В приплюснутый бокс купола (DomeW×DomeH, спек §2)
        // ректы растягивают полукруг в ПОЛУЭЛЛИПС — это и есть канонная форма купола.
        private static UnityEngine.Sprite MakeDomeSprite()
        {
            const int w = 512, h = 256;            // h = радиус, w = диаметр
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            const float r = h;                     // радиус в пикселях текстуры
            for (int y = 0; y < h; y++)
            {
                // Текстурный y растёт ВВЕРХ, а центр окружности сидит на верхней грани (y = h),
                // поэтому расстояние вниз от центра = h − (y + 0.5).
                float dy = h - (y + 0.5f);
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) - w / 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Мягкий 1-пиксельный край: без него полукруг лесенкой на масштабе канваса.
                    byte a = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(r - d + 0.5f));
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            var s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f);
            s.name = "timer-dome";                 // имя видно в спрайт-переписи HUD-конформанса
            return s;
        }

        // Тело «стрелки-кромки»: вертикальная полоска с МЯГКИМИ боковыми краями. Именно мягкость и даёт
        // сглаживание — линия рисуется повёрнутой на произвольный угол, и без альфа-рампы её собственный
        // край был бы такой же лесенкой, какую она пришла прятать. По высоте текстура однородна, так что
        // растяжение по длине стрелки ничего не искажает.
        private static UnityEngine.Sprite MakeDomeHandSprite()
        {
            const int w = 16, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            const float edge = 2.5f;               // ширина мягкой каймы в текселях
            for (int x = 0; x < w; x++)
            {
                float d = Mathf.Min(x + 0.5f, w - (x + 0.5f));          // расстояние до ближайшего края
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(d / edge));
                for (int y = 0; y < h; y++) px[y * w + x] = new Color32(255, 255, 255, alpha);
            }
            tex.SetPixels32(px);
            tex.Apply();
            var s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), 100f);
            s.name = "timer-dome-hand";            // имя видно в спрайт-переписи HUD-конформанса
            return s;
        }

        // Отрисовать купол из (осталось, полная длина фазы). Единственная точка, где дуга и её цвет
        // получают значения — и живой Update, и кризис, и скриншот-позы идут через неё.
        private void ReflectDome(float remaining, float full)
        {
            if (_domeFill == null) return;
            float amount = Mathf.Clamp01(remaining / Mathf.Max(0.0001f, full));
            _domeFill.fillAmount = amount;
            ReflectDomeHand(amount);
            if (remaining <= DomeAlarmSeconds)
            {
                // ПОСЛЕДНЯЯ СЕКУНДА (§5a «тревожный / мигает»): краснеет и МИГАЕТ ВЕСЬ купол — не только
                // куцый остаток дуги. На 10 % остатка красное иначе живёт лишь узкой кромкой у верхнего
                // края (≈0.2 % экрана) и семантически слипается с красной зоной бара здоровья
                // (дизайн-гейт). Теперь тревогу несёт весь силуэт 400×130, включая коридор между барами:
                // остаток дуги = чистый RED_BRIGHT, а «истёкший» трек пульсирует CREAM ⇄ RED_BRIGHT.
                // Фаза считается от ОСТАВШЕГОСЯ времени → это честное мигание (≈3 вспышки за секунду),
                // детерминированное для кадра и теста; масштаб купола не трогаем.
                float p = 0.5f + 0.5f * Mathf.Cos(remaining / DomeAlarmPulsePeriod * 2f * Mathf.PI);
                _domeFill.color = DomeAlarm;
                _domeTrack.color = Color.Lerp(Cream, DomeAlarm, p);
            }
            else
            {
                _domeFill.color = DomeYellow;
                _domeTrack.color = Cream;
            }
        }

        // Поставить «стрелку-кромку» ровно на границу радиальной заливки.
        //
        // Граница `Radial180` (origin = Top) — ПРЯМОЙ отрезок из центра купола в точку внутреннего
        // эллипса: uGUI режет квадрант, лерпая x и y независимо, и конец реза при параметре θ = 90°·val
        // приходится ровно в (±a·cos θ, b·sin θ) — параметрическую точку эллипса с полуосями
        // a = ширина/2, b = глубина. Отсюда и длина стрелки, и её угол — без подгонки.
        //   fill ≤ ½ (гаснет правая половина): val = 2·fill, знак x = +
        //   fill > ½ (ещё тает левая):        val = 2 − 2·fill, знак x = −
        private void ReflectDomeHand(float fillAmount)
        {
            if (_domeHand == null) return;
            float a = (DomeW - 2f * DomeOutlineWidth) / 2f;    // полуоси ВНУТРЕННЕГО (залитого) эллипса
            float b = DomeH - DomeOutlineWidth;
            float sign = fillAmount <= 0.5f ? 1f : -1f;
            float val = fillAmount <= 0.5f ? 2f * fillAmount : 2f - 2f * fillAmount;
            float theta = Mathf.Clamp01(val) * 90f * Mathf.Deg2Rad;
            float dx = sign * a * Mathf.Cos(theta);            // вправо
            float dy = b * Mathf.Sin(theta);                   // ВНИЗ по экрану
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            // На самых краях окна границы заливки внутри купола НЕТ (она совпадает с его собственным
            // кантом), и стрелка выродилась бы в чёрточку вдоль верхнего края — артефакт на чистом
            // полном/пустом куполе. В этих двух точках она просто не рисуется.
            bool inside = fillAmount > 0.005f && fillAmount < 0.995f;
            _domeHand.color = inside ? DomeInk : new Color(0f, 0f, 0f, 0f);
            var rt = _domeHand.rectTransform;
            rt.sizeDelta = new Vector2(DomeHandWidth, len);
            // Спрайт нарисован «сверху вниз» (пивот в верхней точке), поэтому базовое направление —
            // (0,−1) в локальных осях канваса (y вверх); экранный «вниз» dy → локальный −dy.
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dy, dx) * Mathf.Rad2Deg + 90f);
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

        // Host speech bubble (S3, `host-comment-v2` 9-slice) + rubric banner (S4). Both start hidden.
        private void BuildHostReactions()
        {
            // ---- Speech bubble (S3): the art-pack megaphone plate, above-left, riding the card's corner ----
            // `host-comment-v2` IS the explainer's bubble (cream fill, gold rim, megaphone tail), so it is
            // drawn in its own colours at its own tilt — the old `bubble` sprite (a flat orange box with a
            // triangular tail, tinted gold) matched the measured RECT but never the picture.
            var bubbleImg = NewSprite("HostBubble", _gamePanel.transform, Sprite("host-comment-v2"));
            bubbleImg.type = Image.Type.Sliced;                       // borders 340/80/80/80 (import sets them)
            bubbleImg.pixelsPerUnitMultiplier = 1f / BubbleScale;     // borders drawn at the art's own 0.375
            _hostBubble = bubbleImg.gameObject;
            AnchorPx(bubbleImg.rectTransform, BubbleRect.x, BubbleRect.y, BubbleRect.z, BubbleRect.w);
            // The plate is TILTED on the explainer — right end higher, megaphone hanging down-left. Rotating
            // the Image rotates its text child with it, which is what the reference shows.
            bubbleImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, BubbleTiltDeg);
            // Host replies are «комментарии» → Rubik (meeting-revisions §8), not the display face: the
            // bubble carries the Ведущий's spoken line, the same role as the necrolog/tutorial body copy.
            _bubbleText = NewText("HostBubbleText", _hostBubble.transform, "", BubbleTextMaxSize,
                TextAnchor.MiddleCenter, Ink, _bodyBold);
            // The text rect insets PAST the 9-slice borders on every side, so glyphs can only ever land on the
            // stretched CREAM middle: the left inset is the whole megaphone border (no text over the horn),
            // the right one additionally clears the gold star baked into the plate's bottom-right corner.
            var brt = _bubbleText.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(BubbleTextLeftPx * BubbleScale, BubbleTextVertPx * BubbleScale);
            brt.offsetMax = new Vector2(-BubbleTextRightPx * BubbleScale, -BubbleTextVertPx * BubbleScale);
            // Best-fit honouring HEIGHT (Truncate, not the NewText default Overflow) so the loudest host
            // exclamations (deck lines up to ~50 chars) SHRINK to sit fully inside the cream fill.
            _bubbleText.resizeTextForBestFit = true;
            _bubbleText.resizeTextMinSize = 15;
            // Cap = what the EXPLAINER draws. Re-measured on «Экран - комментарий ведущего.png» by isolating
            // the GLYPH components inside the plate (the plate's own black outline has to be dropped, or the
            // reading doubles): «Ну ты и тип!» has a cap-height of 27.0 px and an x-height of 20.5 px once
            // de-tilted — i.e. 27 / 0.70 ≈ 39 px of Rubik (capHeight 700/1000 em). Best-fit only honours
            // HEIGHT when the mode is Wrap, so the cap also has to stay under the size at which the longest
            // single WORD would run past the rect (guarded, whole pool, by HostReactionTests).
            _bubbleText.resizeTextMaxSize = BubbleTextMaxSize;
            _bubbleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bubbleText.verticalOverflow = VerticalWrapMode.Truncate;
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

        // ---------------------------------------------------------------- art-pack HUD widgets
        // Each group is a FULL-CANVAS transparent container (NewGroup stretches), so every child can be
        // placed with AnchorPx in plain 1920×1080 reference coordinates straight out of the asset map.

        // Энергия — `energy-battery-v2`. The cavity level is drawn as flat rects OVER the sprite (see the
        // geometry block): a cream «empty» rect from the cavity top down to the level, plus a yellow top-up
        // between the sprite's baked 75.1 % line and a higher level. Sized every frame in ReflectEnergyLevel.
        private void BuildBattery()
        {
            _energyGroup = NewGroup("EnergyGroup", _hudRow.transform);
            _batteryImg = NewSprite("Battery", _energyGroup.transform, Sprite("energy-battery-v2"));
            AnchorPx(_batteryImg.rectTransform, BatteryRect.x, BatteryRect.y, BatteryRect.z, BatteryRect.w);
            // Sibling order IS the z-order and is asserted (HudConformanceTests): baked battery sprite →
            // cream «empty» mask → yellow top-up. Both overlays draw ABOVE the sprite (otherwise the baked
            // 75.1 % level would show through), and the top-up draws last so the live level always wins on
            // the level line itself.
            _energyEmpty = NewSolid("EnergyEmpty", _energyGroup.transform, BatteryCream);
            _energyTopUp = NewSolid("EnergyTopUp", _energyGroup.transform, BatteryYellow);
            // The lightning is its OWN layer (founder canon §12-3): patched out of the baked fill and drawn
            // last, so it reads WHOLE at every level instead of being half-swallowed by the cream mask. Its
            // own cream body + INK outline is what keeps it contrasty on both cream and yellow.
            _energyBolt = NewSprite("EnergyBolt", _energyGroup.transform, Sprite("battery-bolt-v2"));
            AnchorPx(_energyBolt.rectTransform, BoltRect.x, BoltRect.y, BoltRect.z, BoltRect.w);
            ReflectEnergyLevel(100f);
        }

        // Отношения — `rel-bar-v2` (boy…track…girl). «Сердце и есть маркер» (founder canon §12-1): the drawn
        // heart is patched OUT of the track and re-cut as `rel-marker-heart-v2`, and THAT sprite rides the
        // track through the non-linear map of §11-2. The end decorations (the two faces) stay put.
        private void BuildRelBar()
        {
            _balancerGroup = NewGroup("Balancer", _hudRow.transform);
            _relBarImg = NewSprite("RelBar", _balancerGroup.transform, Sprite("rel-bar-v2"));
            AnchorPx(_relBarImg.rectTransform, RelBarRect.x, RelBarRect.y, RelBarRect.z, RelBarRect.w);
            _balancerMarkerImg = NewSprite("Marker", _balancerGroup.transform, Sprite("rel-marker-heart-v2"));
            _balancerMarker = _balancerMarkerImg.rectTransform;
            ReflectRelationsMarker(50f, redZone: false);
        }

        // Здоровье — `health-bar-v2` (череп…сердце). The art's own marker was BAKED at 52 % and has been
        // patched out (asset-map §11-3); OUR marker is drawn in the same flat-black figure style and rides
        // the §11-4 map (mechanical 20 % lands exactly on the drawn red/green border at 52 %).
        private void BuildHealthBar()
        {
            _healthGroup = NewGroup("HealthGroup", _hudRow.transform);
            _healthBarImg = NewSprite("HealthBar", _healthGroup.transform, Sprite("health-bar-v2"));
            AnchorPx(_healthBarImg.rectTransform, HealthBarRect.x, HealthBarRect.y, HealthBarRect.z, HealthBarRect.w);
            _healthMarkerImg = NewSprite("Marker", _healthGroup.transform, Sprite("health-marker-v2"));
            _healthMarker = _healthMarkerImg.rectTransform;
            ReflectHealthMarker(100f);
        }

        // Деньги — `money-jar-v2` with the sum written INTO the jar's own cream label (asset-map §8:
        // 1709,154,92,36 — tiny, hence the compact «₽12.5к» format of §11-6 plus best-fit), and the coin
        // resting over the throat; an income tick drops it in (DropCoin).
        private void BuildMoneyJar()
        {
            _moneyGroup = NewGroup("MoneyGroup", _hudRow.transform);
            _jarImg = NewSprite("Jar", _moneyGroup.transform, Sprite("money-jar-v2"));
            AnchorPx(_jarImg.rectTransform, MoneyJarRect.x, MoneyJarRect.y, MoneyJarRect.z, MoneyJarRect.w);
            _moneyText = NewText("MoneyText", _jarImg.transform, "₽0", 30, TextAnchor.MiddleCenter, Ink, _display);
            // Label box as a fraction of the jar rect: the sprite box 292,488,440,171 of a 1024² texture.
            Anchor(_moneyText.rectTransform, new Vector2(0.5f, 0.4399f), new Vector2(JarLabelW, JarLabelH));
            _moneyText.resizeTextForBestFit = true;      // «автоужатие» (§11-6)
            _moneyText.resizeTextMinSize = 14;
            _moneyText.resizeTextMaxSize = 30;
            _moneyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _moneyText.verticalOverflow = VerticalWrapMode.Truncate;
            _moneyCoin = NewSprite("Coin", _moneyGroup.transform, Sprite("coin-v2"));
            AnchorPx(_moneyCoin.rectTransform, CoinRect.x, CoinRect.y, CoinRect.z, CoinRect.w);
        }

        // Возраст — `age-badge-v2` with the digits only. Kegl per asset-map §11-8: the reference's cap-height
        // is 80 px → ≈112 px Arimo Bold; best-fit shrinks a three-digit age instead of overflowing the frame
        // (this is the fix for the old «ВОЗРАСТ»-caption overlap debt — the caption is gone).
        private void BuildAgeBadge()
        {
            _ageBadgeImg = NewSprite("AgeBadge", _hudRow.transform, Sprite("age-badge-v2"));
            _ageBadge = _ageBadgeImg.gameObject;
            AnchorPx(_ageBadgeImg.rectTransform, AgeBadgeRect.x, AgeBadgeRect.y, AgeBadgeRect.z, AgeBadgeRect.w);
            // Digit box as a fraction of the badge rect (screen 1661,422,169,145 — the max inscribed box that
            // clears the frame's corner stars, asset-map §5.2).
            _ageText = NewText("AgeText", _ageBadge.transform, "0", 112, TextAnchor.MiddleCenter, Ink, _display);
            Anchor(_ageText.rectTransform, new Vector2(0.4988f, 0.4922f), new Vector2(169f, 145f));
            _ageText.resizeTextForBestFit = true;
            _ageText.resizeTextMinSize = 70;
            _ageText.resizeTextMaxSize = 112;
            _ageText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _ageText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ---------------------------------------------------------------- non-linear value → track maps
        // The ART draws its zone borders in different places than the MECHANIC's thresholds (canon, not to
        // be touched — asset-map §11-2/§11-4). So the marker rides a PIECEWISE-LINEAR map that pins each
        // mechanical threshold onto the DRAWN border: the marker is visually honest without moving a single
        // gameplay number. Pure functions, unit-tested knot by knot in EditMode/HudMappingTests.

        // The END knots are NOT 0 and 1: they are the ends of the marker's travel window (geometry block) —
        // the track's own ends belong to the drawn skull/heart and the two faces, and a marker parked on one
        // of them reads as a blob. Both end knots still sit in the same DRAWN zone as the value they carry
        // (health 0 → deep in the drawn red, health 100 → deep in the drawn green, rel 100 → past the drawn
        // green/red border, i.e. in the red zone), so the reading stays honest; only the last ≈15 % of the
        // rail, which the art already occupies, is off limits.

        /// <summary>Relationships value (0…100) → fraction of the drawn track. Knots: 0→travel start,
        /// 40→20.7 %, 75→81.5 % (the drawn red/green and green/red borders), 100→travel end.</summary>
        public static float RelationsTrackFraction(float relations)
            => PiecewiseFraction(relations, RelValueKnots, RelTrackKnots);

        /// <summary>Health value (0…100) → fraction of the drawn track. Knots: 0→travel start, 20→52 % (the
        /// drawn red/green border = the alarm threshold), 100→travel end.</summary>
        public static float HealthTrackFraction(float health)
            => PiecewiseFraction(health, HealthValueKnots, HealthTrackKnots);

        /// <summary>Fraction of a track at which a marker centre sits at <paramref name="cx"/>.</summary>
        private static float TrackFractionAt(float cx, float trackX, float trackW) => (cx - trackX) / trackW;

        private static readonly float[] RelValueKnots = { 0f, 40f, 75f, 100f };
        private static readonly float[] RelTrackKnots =
        {
            TrackFractionAt(RelMarkerMinCx, RelTrackX, RelTrackW),   // ≈0.160 — clear of the boy's face
            0.207f, 0.815f,                                          // drawn zone borders — canon, untouched
            TrackFractionAt(RelMarkerMaxCx, RelTrackX, RelTrackW),   // ≈0.856 — clear of the girl's face
        };
        private static readonly float[] HealthValueKnots = { 0f, 20f, 100f };
        private static readonly float[] HealthTrackKnots =
        {
            TrackFractionAt(HealthMarkerMinCx, HealthTrackX, HealthTrackW),   // ≈0.235 — clear of the skull
            0.52f,                                                            // drawn red/green border — canon
            TrackFractionAt(HealthMarkerMaxCx, HealthTrackX, HealthTrackW),   // ≈0.764 — clear of the heart
        };

        private static float PiecewiseFraction(float v, float[] xs, float[] ys)
        {
            if (v <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
            {
                if (v > xs[i]) continue;
                float t = (v - xs[i - 1]) / (xs[i] - xs[i - 1]);
                return Mathf.Lerp(ys[i - 1], ys[i], t);
            }
            return ys[ys.Length - 1];
        }

        // Battery cavity: cream «empty» rect from the cavity top down to the level line, plus a yellow top-up
        // between the sprite's baked level and a higher one. energy is 0…100.
        private void ReflectEnergyLevel(float energy)
        {
            float p = Mathf.Clamp01(energy / 100f);
            float levelTop = CavityTop + CavityH * (1f - p);     // screen y of the level line
            float emptyH = Mathf.Max(0f, levelTop - CavityTop);
            AnchorPx(_energyEmpty.rectTransform, CavityX + CavityW / 2f, CavityTop + emptyH / 2f, CavityW, emptyH);
            float bakedTop = CavityTop + CavityH * (1f - BakedEnergyLevel);
            float topUpH = Mathf.Max(0f, bakedTop - levelTop);   // only when the level is above the baked one
            AnchorPx(_energyTopUp.rectTransform, CavityX + CavityW / 2f, levelTop + topUpH / 2f, CavityW, topUpH);
        }

        // Both markers keep the art's own size and the art's own height on the bar (geometry block) — only
        // X moves, through the non-linear value→track map. That is the whole point of §12-1: what rides the
        // bar IS the picture the artist drew there.
        private void ReflectHealthMarker(float health)
        {
            float f = HealthTrackFraction(health);
            // Belt AND braces: the map's end knots already stop inside the travel window, and this clamp
            // keeps a future knot edit from pushing the figure back onto the skull or the heart.
            float x = Mathf.Clamp(HealthTrackX + f * HealthTrackW, HealthMarkerMinCx, HealthMarkerMaxCx);
            AnchorPx(_healthMarker, x, HealthMarkerCy, HealthMarkerW, HealthMarkerH);
        }

        private void ReflectRelationsMarker(float relations, bool redZone)
        {
            float f = RelationsTrackFraction(relations);
            float x = Mathf.Clamp(RelTrackX + f * RelTrackW, RelMarkerMinCx, RelMarkerMaxCx);
            AnchorPx(_balancerMarker, x, RelMarkerCy, RelMarkerW, RelMarkerH);
            // «Красная зона» (>75 %): the drawn zones keep their own colours (tinting the bar muddied it) —
            // the risk is flagged on the MARKER alone.
            var tint = redZone ? TimerRed : Color.white;
            if (_balancerMarkerImg.color != tint) _balancerMarkerImg.color = tint;
        }

        // Child «cabinet button» (S9): no dedicated sprite exists, so this is a placeholder built from
        // marquee-bulb.png (the round lit-bulb art) + a heart — dim/cool while idle, bright gold + pulsing
        // while the flash window is open (driven in Update off Game.ChildFlashing). Just a button, per the
        // mockup — no label, no scale stripe (a red bar there read as a foreign health/danger artifact).
        private void BuildChildButton()
        {
            _childGroup = NewGroup("Child", _hudRow.transform);
            AnchorPx(_childGroup.GetComponent<RectTransform>(), ChildCx, ChildCy,
                ChildGlowPeakSize, ChildGlowPeakSize);

            // Bright halo BEHIND the button (created first → lower sibling → drawn behind). Invisible at rest,
            // it flares gold and pulses while ChildFlashing so the «жми Enter по вспышке» window is unmissable
            // (founder playtest: the subtle gold-tint pulse read as «ничего не связано с ребёнком»).
            // Sized off the free jar↔badge gap (geometry block): at the pulse PEAK it still clears the jar,
            // the badge and the frame — the halo used to spill over the badge and off the right edge.
            _childGlow = NewSprite("ChildGlow", _childGroup.transform, Sprite("star-white"));
            _childGlow.color = new Color(Bulb.r, Bulb.g, Bulb.b, 0f);
            Anchor(_childGlow.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(ChildGlowSize, ChildGlowSize));

            _childButtonImg = NewSprite("Button", _childGroup.transform, Sprite("marquee-bulb"));
            _childButtonImg.color = CobaltDeep;   // idle (unlit)
            Anchor(_childButtonImg.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(ChildButtonSize, ChildButtonSize));
            var heart = NewSprite("Heart", _childButtonImg.transform, Sprite("icon-heart"));
            Anchor(heart.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(ChildButtonSize * 0.52f, ChildButtonSize * 0.52f));

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

            // Restart CTA. The plate IS the green cabinet button its label names, so its fill must be the
            // GREEN token #05CE51 — `plate-yes` carries its own paler art green (#5CBF5F), and a uGUI tint
            // only ever MULTIPLIES, so no tint on that sprite can reach the token (design gate 2026-07-31:
            // «зелёный финала бледнее опенера»). Built exactly like the opener CTA instead — dark Ink rim +
            // `bar-track` tinted through OnBarTrack(GoGreen) — so both confirm CTAs render the SAME token.
            var againEdge = NewSprite("AgainPlateEdge", _finalePanel.transform, Sprite("bar-track"));
            againEdge.type = Image.Type.Sliced;
            againEdge.color = OnBarTrack(Ink);
            AnchorPx(againEdge.rectTransform, 960f, 936f, 614f, 224f);

            var again = NewSprite("AgainPlate", _finalePanel.transform, Sprite("bar-track"));
            again.type = Image.Type.Sliced;
            again.color = OnBarTrack(GoGreen);
            AnchorPx(again.rectTransform, 960f, 936f, 600f, 210f);   // taller pill + comfortable bottom margin
            var againText = NewText("AgainText", again.transform,
                "НАЧАТЬ ЗАНОВО\nЖМИ ЗЕЛЁНУЮ", 40, TextAnchor.MiddleCenter, Ink, _display);
            Inset(againText.rectTransform, 34f);   // text rect well INSIDE the visible pill (bar-track cuts ~16px corners) → padding all sides
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

            // Blue «ПОНЯТНО — ЖМИ ЗЕЛЁНУЮ» button (S5) with a clear bottom margin inside the modal (NOT flush
            // to the edge). The dismiss control is named by its PHYSICAL colour (founder 99fab3c): the crank
            // must never dismiss, so the green lever stays discoverable on the button. bar-track tinted cobalt.
            var plate = NewSprite("GotItPlate", modal.transform, Sprite("bar-track"));
            plate.type = Image.Type.Sliced;
            plate.color = Cobalt;
            Anchor(plate.rectTransform, new Vector2(0.5f, 0.16f), new Vector2(470, 116));
            _tutorialButton = plate;
            var plateTxt = NewText("GotItText", plate.transform, "ПОНЯТНО — ЖМИ ЗЕЛЁНУЮ", 32, TextAnchor.MiddleCenter, Color.white, _display);
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

            // BIG breathing pulse indicator (S8 playtest rework): a large star that is ALWAYS visible during
            // depression and BLINKS on the steady beat — bright + scaled-up flash while the hit-window is open
            // («жми!»), dim-but-visible resting between (never fully gone), so the player sees the rhythm and taps
            // in time. Driven every frame in ReflectDepression. High contrast on the gray wash. (Was a faint 150px
            // dot shown ONLY on the ~0.6s window — «вообще не видно, как дышать».)
            _depressionPulse = NewSprite("DepressionPulse", _depressionGroup.transform, Sprite("star-white"));
            _depressionPulse.color = new Color(0.92f, 0.92f, 0.98f, 0.32f);
            Anchor(_depressionPulse.rectTransform, new Vector2(0.5f, 0.44f), new Vector2(280, 280));
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

            // The big star is ALWAYS visible while depressed (never fully gone) and BLINKS on the beat: a bright,
            // scaled-up FLASH while the hit-window is open («жми!»), and a dim-but-clearly-visible resting breath
            // between beats — so the steady tempo reads as an obvious «tap on each blink» affordance (S8 rework).
            bool pulsing = _game.DepressionPulsing;
            if (!_depressionPulse.gameObject.activeSelf) _depressionPulse.gameObject.SetActive(true);
            if (pulsing)
            {
                float a = 0.90f + 0.10f * Mathf.Sin(Time.time * 12f);           // near-full bright flash
                _depressionPulse.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                _depressionPulse.rectTransform.localScale = Vector3.one * 1.28f; // scaled UP on «жми!»
            }
            else
            {
                float a = 0.32f + 0.08f * Mathf.Sin(Time.time * 3f);            // dim but visible resting breath
                _depressionPulse.color = new Color(0.90f, 0.90f, 0.97f, a);
                _depressionPulse.rectTransform.localScale = Vector3.one * (0.86f + 0.03f * Mathf.Sin(Time.time * 3f));
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
            // Refresh it NOW so the just-opened widget (money jar / energy battery / health bar) is visible
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
            // MD02 «РЕБЁНОК! Завести?» is the one timeline card whose banner («ПОПОЛНЕНИЕ!» — a newborn
            // arrived) must NOT fire on DRAW: it would announce the baby BEFORE you answer «завести?»
            // (founder playtest: «пополнение вышло раньше, чем случилось»). Its announce comes AFTER ДА via
            // the OpenChild hint (OnChildOpened). Every other timeline card still announces on draw.
            if (c != null && c.IsTimeline && c.Id != "MD02") ShowBanner(c);
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
                ReflectCardTextBand();   // band may now be free → the question gets its full box back
                return;
            }
            int p = Mathf.RoundToInt((float)price);
            _cardPriceText.text = (blocked ? "цена " : "СТОИТ ") + p + " ₽";
            _cardPriceText.color = blocked ? TextLight : Bulb;

            // Size the text rect to its content, then wrap the dark plate around it (with padding) so the
            // plate always fully covers the text — never bare gold/light text on the yellow sunburst.
            float tw = _cardPriceText.preferredWidth;
            float th = _cardPriceText.preferredHeight;
            _cardPriceText.rectTransform.sizeDelta = new Vector2(tw + 4f, th);   // +4: metric rounding slack
            _cardPricePlate.rectTransform.sizeDelta = new Vector2(tw + 64f, th + 28f);

            _cardPricePlate.gameObject.SetActive(true);
            _cardPriceText.gameObject.SetActive(true);
            ReflectCardTextBand();   // band occupied → the question box ends above it
        }

        private void UpdateHudValues()
        {
            var s = _game.Scales;
            _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
            _moneyText.text = FormatMoneyJar(_game.Money);
            ReflectEnergyLevel(s.Energy);
            ReflectHealthMarker(s.Health);
            ReflectRelationsMarker(s.Relationships, _game.RelationshipRedZone);
        }

        /// <summary>Reveal HUD widgets by age (visual only — the scales themselves stay passive).</summary>
        private void ApplyAgeGates(float age)
        {
            int a = Mathf.FloorToInt(age);
            _ageBadge.SetActive(true);
            _moneyGroup.SetActive(a >= MoneyAge);
            // Balancer reveals at 20 and hides again after a breakup (partner gone — MD06 reopen deferred).
            _balancerGroup.SetActive(a >= RelationshipsAge && !_game.RelationshipsLost);
            _energyGroup.SetActive(a >= EnergyAge);
            _healthGroup.SetActive(a >= HealthAge);
        }

        private void OnInputFx(GameInput input)
        {
            // MoneyTick FX (the coin drop into the jar) is triggered from OnInput's ACCEPTED-crank branch
            // instead — raw (capped/no-op) presses must not flash feedback for income that didn't land.
            if (_game == null || _game.State != GameState.Playing || !isActiveAndEnabled) return;
            if (_tutorialShowing) return;
            if (input == GameInput.AnswerYes) StartCoroutine(PunchPlate(_yesRect, YesTilt));
            else if (input == GameInput.AnswerNo) StartCoroutine(PunchPlate(_noRect, NoTilt));
        }

        // Income-tick feedback: the coin resting over the jar's lid DROPS into the throat (asset-map §8:
        // throat centre 1753,89) and springs back to its perch — the art-pack replacement for the old pill
        // pulse. Purely visual; the money itself already landed in Game.
        private IEnumerator DropCoin()
        {
            var rt = _moneyCoin.rectTransform;
            float fall = ThroatCenterY - CoinRect.y;     // reference px from the perch down to the throat
            const float dur = 0.18f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float drop = Mathf.Sin(k * Mathf.PI) * fall;      // down and back in one arc
                rt.anchoredPosition = new Vector2(0f, -drop);
                yield return null;
            }
            rt.anchoredPosition = Vector2.zero;
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

        /// <summary>
        /// The sum written into the money jar's own cream label. That label measures just 92×36 screen px
        /// (asset-map §8), so five-digit-and-up sums switch to the COMPACT thousands form of §11-6
        /// («₽12.5к»); everything shorter stays literal. Floored; negative allowed («в минус», canon).
        /// </summary>
        public static string FormatMoneyJar(double value)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int v = Mathf.FloorToInt((float)value);
            int abs = Mathf.Abs(v);
            if (abs < 10_000) return "₽" + v.ToString(inv);                  // ≤4 digits fit literally
            string sign = v < 0 ? "-" : "";
            if (abs < 10_000_000) return sign + "₽" + (abs / 1000.0).ToString("0.#", inv) + "к";
            return sign + "₽" + (abs / 1_000_000.0).ToString("0.#", inv) + "м";
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
        // Every inset is ≥ the fixed ~55px 9-slice corner inset, so the text rect is a subset of the
        // visible coloured pill — best-fit can then only ever draw glyphs ON the pill (Layer-2 guard).
        private static void PlateTextRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(58f, 76f);    // left, bottom
            rt.offsetMax = new Vector2(-74f, -56f);  // right, top
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
