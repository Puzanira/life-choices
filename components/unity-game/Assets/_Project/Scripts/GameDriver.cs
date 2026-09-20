using System.Collections;
using System.Collections.Generic;
using AiGameStudio.ArcadeControls;
using UnityEngine;
using UnityEngine.UI;

namespace ThanksNoThanks
{
    /// <summary>
    /// Шкалы, у которых есть КРАСНАЯ ТРЕВОГА (meeting-revisions §4 / build-spec §F). Чисто визуальный
    /// перечень — механика этих шкал живёт в <see cref="Game"/> и тревогой не меняется.
    /// </summary>
    public enum AlarmScale
    {
        /// <summary>Энергия &lt; 20 % — батарея краснеет целиком (эталон «Экран подсвечена красным шкала.png»).</summary>
        Energy,
        /// <summary>Здоровье &lt; 20 % — красный кант вокруг бара.</summary>
        Health,
        /// <summary>Отношения вне механической зоны 40–75 % — красный кант вокруг бара.</summary>
        Relations,
        /// <summary>Денег не хватает на BLOCK$-карточку — банка и её лейбл краснеют.</summary>
        Money,
    }

    /// <summary>
    /// Шкала, чей ТУТОРИАЛ-ЭКРАН (build-spec §D, «появление новой шкалы») поднят сейчас. Ровно четыре
    /// открытия (`OPEN:*`) ведут этот модальный экран; здоровье (30) и выгорание остались на прежней
    /// текстовой S5-подсказке (meeting-revisions §2 их не перечисляет).
    /// </summary>
    public enum NewScale
    {
        /// <summary>Никакой — экран не поднят.</summary>
        None,
        /// <summary>Деньги (18, `OPEN:Дн`) — условие выхода: ≥1 ПРИНЯТЫЙ тик крутилки.</summary>
        Money,
        /// <summary>Отношения (20, `OPEN:Отн`) — маркер в механической зоне 40–75 % и удержать 1.5 с.</summary>
        Relations,
        /// <summary>Энергия (25, `OPEN:Эн`) — держать датчик высоты, пока батарейка не заполнится.</summary>
        Energy,
        /// <summary>Ребёнок (свадьба+2, `OPEN:Реб`) — поднять один звонок кнопкой «!».</summary>
        Child,
    }

    /// <summary>
    /// СПЕЦРЕЖИМ, чей ВХОДНОЙ ЭКРАН поднят сейчас (плейтест-фиксы r3, 2026-08-07). Обобщение §D-модалки на
    /// состояния, которые до сих пор начинались без объяснений: игрок влетал в блиц/депрессию/выгорание и
    /// «умирал, не понимая, что происходит», а здоровье объяснялось старой жёлтой S5-подсказкой, выпадавшей
    /// из арт-пака.
    ///
    /// Экран собран ИЗ ТЕХ ЖЕ БЛОКОВ, что и §D (затемнение · крупный виджет · облачко-рассказ ·
    /// окно-задача), но закрывается не работой контролом — у этих режимов либо нет своего контрола
    /// (здоровье), либо он и есть содержание режима, — а ЗЕЛЁНОЙ кнопкой, тем же CTA-блоком, каким
    /// начинается опенер и перезапускается финал. Пока экран висит, стоит ВСЁ: возраст, дренажи, таймер
    /// карточки, кризисный таймер и планировщик пульса депрессии — умереть, читая правила, нельзя.
    /// </summary>
    public enum SpecialMode
    {
        /// <summary>Никакой — входного экрана нет.</summary>
        None,
        /// <summary>Здоровье начало таять (30). Крупный виджет — бар здоровья; своего контрола нет.</summary>
        Health,
        /// <summary>Кризис среднего возраста → БЛИЦ (45). Крупный виджет — купол-таймер.</summary>
        Blitz,
        /// <summary>«Тёмная полоса» (CR09). Крупный виджет — та самая звезда-пульс, которую ловят.</summary>
        Depression,
        /// <summary>ПЕРВОЕ выгорание за жизнь. Крупный виджет — красная батарея (повторные — короткая плашка).</summary>
        Burnout,
    }

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
        private static readonly Color CobaltDeep = new(0.122f, 0.227f, 0.588f);// #1F3A96
        /// <summary>
        /// The DEEP COBALT token, exposed so a test can pin the rendered colour of the price chip to the
        /// exact hex instead of only asserting «dark and not INK».
        /// </summary>
        public static Color CobaltDeepToken => CobaltDeep;
        // INK is the build-spec §2 token #0B0F1A, re-affirmed as canon by the founder (asset-map §12-4):
        // the dark text/outline colour comes from the TOKENS, never sampled off an explainer PNG.
        private static readonly Color Ink = new(11f / 255f, 15f / 255f, 26f / 255f);   // #0B0F1A
        /// <summary>The INK token, exposed so a test can bind the constant to the canon hex.</summary>
        public static Color InkToken => Ink;
        private static readonly Color TextLight = new(0.918f, 0.941f, 1f);     // #eaf0ff
        private static readonly Color Bulb = new(1f, 0.847f, 0.451f);          // #ffd873
        private static readonly Color Energy = new(0.973f, 0.824f, 0.271f);    // #f8d24c
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
        // Короткая плашка ПОВТОРНОГО выгорания (r3, п.5б): тот же насыщенный красный, что у BLOCK$-баннера,
        // — это «плохое состояние», и оно обязано читаться тем же языком, что остальные тревоги арт-пака.
        private static readonly Color BurnoutRed = new(0.90f, 0.18f, 0.14f);
        /// <summary>
        /// Короткая плашка выгорания: центр x, центр y от ВЕРХА, w, h. ПОД БАТАРЕЕЙ, в левой колонке.
        ///
        /// ⚠ ПЕРЕВЕШЕНА 2026-08-08 (дизайн-скептик, раунд 2). Стояла на (1294, 226) — то есть под баром
        /// ЗДОРОВЬЯ, хотя говорит про БАТАРЕЮ, и низом (271) подходила к обводке карточки на 2 px. Правило
        /// «сообщение о состоянии живёт под своим виджетом» никуда не делось — просто применено к нужному
        /// виджету: плашка ушла под батарею (её низ 283.6), в левую колонку.
        ///
        /// Коридор посчитан по НАРИСОВАННЫМ соседям, не на глаз: батарея сверху (низ 275.5), рисунок
        /// карточки справа (левый край 412.3), трубка в покое снизу (<see cref="PhoneRestDrawnBox"/>,
        /// верх 396.2). Кант (<see cref="BlockKeylineInk"/> = 4 px с каждой стороны) даёт внешний бокс
        /// 12…375 × 288…384 — зазоры 12.5 / 37 / 12.2 при требуемых ≥12.
        ///
        /// ⚠ ВЫЕХАВШАЯ трубка (звонок, <see cref="PhoneRingDrawnBox"/>) в этот коридор не помещается ни
        /// при какой ширине: между низом батареи и её верхом всего 55 px. Разведены ВРЕМЕНЕМ — плашка
        /// гаснет, пока трубка на экране (см. ветку плашки в Update).
        /// </summary>
        public static readonly Vector4 BurnoutPlateRect = new(193.5f, 336f, 355f, 88f);
        /// <summary>
        /// НАРИСОВАННЫЙ бокс трубки в позе покоя (центр x, центр y от ВЕРХА, w, h). Рект
        /// <see cref="PhoneRestRect"/> уезжает за левый край экрана и вдобавок несёт прозрачные поля
        /// спрайта (`phone-rest-v2` 662×715, alpha-bbox 113…546 × 47…681), поэтому «не накрывать трубку»
        /// считается по РИСУНКУ, а не по ректу: x −80.5…158.4, y 396.2…745.6.
        /// </summary>
        public static readonly Vector4 PhoneRestDrawnBox = new(38.95f, 570.9f, 238.9f, 349.4f);
        /// <summary>То же для позы ЗВОНКА (`phone-ring-v2` 662×715, alpha-bbox 0…661 × 20…682): выехавшая
        /// трубка занимает x −61.7…349.7, y 330.3…742.2 — весь низ левой колонки. Именно она, а не покой,
        /// и есть настоящий сосед короткой плашки выгорания.</summary>
        public static readonly Vector4 PhoneRingDrawnBox = new(144f, 536.25f, 411.3f, 411.9f);
        /// <summary>НАРИСОВАННЫЙ бокс карточки (`choice-plate-v2` 1536×1024, alpha-bbox 33…1501 × 26…994):
        /// x 412.3…1506.0, y 229.0…950.7. Рект карточки заметно шире рисунка (по 25 px прозрачных полей).</summary>
        public static readonly Vector4 CardPlateDrawnBox = new(959.13f, 589.87f, 1093.75f, 721.74f);
        /// <summary>BLOCK$-баннер «нет денег» на экране: центр x, центр y от ВЕРХА, w, h. Те же пиксели,
        /// что давала прежняя доля карточки (y 705…815 внутри её кремового поля 286…892).</summary>
        public static readonly Vector4 BlockBannerRect = new(959.5f, 760f, 900f, 110f);
        /// <summary>Чип цены («СТОИТ N ₽» / «цена N ₽») — так же абсолютным боксом (y 811…889).</summary>
        public static readonly Vector4 CardPriceRect = new(959.5f, 850f, 360f, 78f);
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
        // §4-тревога: «заряд» — полоса от живого уровня до ДНА полости. В спокойном ходе она не нужна
        // (там ниже уровня видна запечённая жёлтая заливка спрайта) и держится прозрачной; в тревоге
        // именно она красит остаток заряда в #FF0506, а `_energyEmpty` — пустоту в #FF3736.
        private Image _energyCharge;
        private Image _batteryAlarm;       // §4: красная копия батареи, кроссфейдится поверх спокойной
        private Image _energyBolt;         // the art's lightning, its own layer above the fill (§12-3)
        private float _energyShown = 100f; // последнее НАРИСОВАННОЕ значение энергии (позы = живой ход)
        // Bar markers: the art's own health marker was a BAKED silhouette (patched out, asset-map §11-3), so
        // both bars carry OUR marker, drawn in the same flat-black figure style, positioned through the
        // non-linear value→track map (§11-2/4).
        private Image _relBarImg, _healthBarImg;
        // §4-тревога баров: НЕ тонировка плашки, а КРАСНЫЙ КАНТ вокруг всего виджета (см. ReflectAlarms).
        private Image _relKant, _healthKant;
        // …и чёрное кольцо СНАРУЖИ красного — тот же keyline, что несёт весь арт-пак (полиш дизайн-гейта).
        private Image _relKantInk, _healthKantInk;
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
        //   coin-v2           (1,0,858,868/860,869)     1716.5,9.1,68,68   → 1750.54, 43.12, 68.16, 68.08
        //   age-badge-v2      (35,63,947,923/1024²)     1639,392,212,207   → 1745.78, 492.70, 229.24, 229.65
        //   choice-plate-v2   (34,26,1468,968/1536,1024) 413,229,1093,721  → 959.50, 590.99, 1143.63, 762.71
        // The JAR box is NOT the asset-map §2 row (design gate round 2: the table is wrong for the jar and
        // the explainer wins) — the jar's own drawn glass, measured on the reference without the coin, is
        // 1664,65,173,185.
        // ⚠ КОИН — ДОЛГ ГЕЙТА (2026-08-05). Раньше монета стояла по своему боксу с эталона (1714,6),
        // но НАШ бокс банки садится на ~2 px ниже эталонного, и в кадре монета висела НАД крышкой:
        // прорезь (синяя щель в крышке) оставалась голой во всю ширину, а между низом монеты и крышкой
        // читался зазор ~4 px. Монета пересчитана ПО СВОЕЙ БАНКЕ, а не по эталонному кадру:
        //   прорезь `money-jar-v2` в кадре = ink 1721.4…1779.4 × 73.2…79.8, СИНЯЯ полость 1728.1…1772.9
        //   × 75.6…77.6, центр по X 1750.50 (замер PIL по спрайту через бокс банки);
        //   монета сдвинута так, что её центр по X = центру прорези (было 1748.0 — «левее» на 2.5 px),
        //   а нижняя кромка её обводки = 77.0, т.е. она СИДИТ в полости прорези: зазор к крышке 0
        //   (перекрытие), середина прорези закрыта монетой, а её концы торчат по бокам — ровно так, как
        //   рисует эталон. Тест сверяет эти отношения, а не только бокс (см. HudConformanceTests).
        /// <summary>Прорезь-полость в крышке банки (синяя щель) в кадре: x0, yTop, x1, yBottom.</summary>
        public static readonly Vector4 JarSlotCavity = new(1728.09f, 75.64f, 1772.91f, 77.63f);
        /// <summary>Та же прорезь ВМЕСТЕ с её чёрной обводкой — в неё монета и «вставляется».</summary>
        public static readonly Vector4 JarSlotInk = new(1721.37f, 73.19f, 1779.41f, 79.83f);
        private static readonly Vector4 BatteryRect = new(184.75f, 168.14f, 126.33f, 230.94f);
        private static readonly Vector4 RelBarRect = new(674.50f, 101.23f, 539.57f, 142.62f);
        private static readonly Vector4 HealthBarRect = new(1294.00f, 101.72f, 519.97f, 137.21f);
        private static readonly Vector4 MoneyJarRect = new(1750.50f, 155.62f, 229.47f, 226.87f);
        private static readonly Vector4 CoinRect = new(1750.54f, 43.12f, 68.16f, 68.08f);
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

        // ---- КРАСНЫЕ ТРЕВОГИ ШКАЛ (revisions §4 / build-spec §F) ------------------------------------
        // Пороги — ДЕФОЛТ ИЗ СПЕКА, тюнятся. Механика не трогается: Game ничего про тревогу не знает,
        // это чистое отображение поверх тех же чисел.
        //
        // ГИСТЕРЕЗИС. Порог «сырьём» дребезжит: энергия/здоровье тают долями процента в секунду и на
        // границе 20 шкала за секунду успевает перейти её несколько раз (а отношения на 40 ещё и
        // тянутся рычагом вверх-вниз). Поэтому тревога ВКЛЮЧАЕТСЯ на спековом пороге и ВЫКЛЮЧАЕТСЯ
        // на пороге, отодвинутом внутрь нормы на <see cref="AlarmHysteresis"/> — числа тюнимые.
        /// <summary>Энергия/здоровье: тревога ВКЛЮЧАЕТСЯ ниже этого (спек §4, тюнится).</summary>
        public const float AlarmScaleOnBelow = 20f;
        /// <summary>Ширина гистерезиса, п.п.: выключение — на «порог ± столько» внутрь нормы (тюнится).</summary>
        public const float AlarmHysteresis = 2f;
        /// <summary>Период пульса яркости, с (спек §4/§6: синус ~0.7 с).</summary>
        public const float AlarmPulsePeriod = 0.7f;
        /// <summary>Яркость в ПРОВАЛЕ пульса (1.0 = полная) — тревога «дышит», а не мигает выключателем.</summary>
        public const float AlarmPulseMin = 0.72f;
        /// <summary>Возврат в норму: подсветка гаснет плавным фейдом за столько секунд (спек §4: ~0.2 с).</summary>
        public const float AlarmFadeSeconds = 0.2f;
        /// <summary>
        /// Окно «живого ввода по шкале», с (тюнится). Салют (§6) даётся только за КАЛИБРОВКУ игроком:
        /// шкала вышла из тревоги, и по ЭТОЙ шкале был ввод игрока за последние столько секунд. Отсюда
        /// же и различение «не на рестарте / не на смене карточки» — там ввода по шкале не было.
        /// </summary>
        public const float AlarmRecentInputSeconds = 2f;
        // Палитра тревожной батареи снята ИНСТРУМЕНТАЛЬНО с пары эталонов (спокойный ↔ тревожный,
        // одна и та же батарея): крем полости → #FF3736, жёлтая заливка → #FF0506, синяя рамка →
        // #CE183B. Рамку тинтом не получить (uGUI умножает, синее под красным множителем чернеет),
        // поэтому тревожная батарея — отдельный ОФЛАЙН-перекрашенный спрайт `energy-battery-alarm-v2`
        // той же геометрии (scratchpad/make_battery_alarm.py), а полость дорисовывается этими двумя.
        private static readonly Color AlarmCavityEmpty = new(255f / 255f, 55f / 255f, 54f / 255f);  // #FF3736
        private static readonly Color AlarmCavityCharge = new(255f / 255f, 5f / 255f, 6f / 255f);   // #FF0506
        /// <summary>Токены тревожной полости, открытые тесту (эталон «Экран подсвечена красным шкала.png»).</summary>
        public static Color AlarmCavityEmptyToken => AlarmCavityEmpty;
        public static Color AlarmCavityChargeToken => AlarmCavityCharge;
        /// <summary>Толщина красного канта вокруг бара, реф-px (кант выходит за нарисованный бокс бара).</summary>
        public const float AlarmKantPad = 13f;
        /// <summary>Толщина ЧЁРНОЙ обводки СНАРУЖИ красного канта, реф-px (полиш дизайн-гейта). Весь
        /// арт-пак несёт чёрный keyline, и кант был единственной фигурой на экране без него — голый
        /// красный упирался прямо в лучи фона. 4 px — верх запрошенного дизайном коридора 3–4: у спрайта
        /// `bar-track` край мягкий (~1 px AA с каждой стороны), и на 3 px в кадре оставалась ОДНА
        /// сплошная чёрная строка — вдвое тоньше собственного keyline баров; на 4 их две, ровно как у
        /// арта. Углы соосны (тот же спрайт, тот же радиус); на диагонали кольцо шире в √2 — это
        /// геометрия раздутия скруглённого прямоугольника с постоянным радиусом, а не рассинхрон.</summary>
        public const float AlarmKantInk = 4f;
        /// <summary>Толщина чёрного keyline вокруг BLOCK$-баннера и чипа цены, реф-px (долг гейта
        /// 2026-08-05: это были единственные фигуры экрана без канта арт-пака). Та же цифра и та же
        /// причина, что у <see cref="AlarmKantInk"/> — 3 px на мягком крае `bar-track` дают одну
        /// сплошную чёрную строку, 4 px дают две, как у собственного keyline арта.</summary>
        public const float BlockKeylineInk = 4f;
        // НАРИСОВАННЫЕ боксы баров (asset-map §2) — кант строится от них, а не от рект-боксов: у спрайтов
        // прозрачные поля, и кант от ректа висел бы в воздухе, не касаясь плашки.
        public const float RelBarDrawnCx = 674.5f, RelBarDrawnCy = 101f, RelBarDrawnW = 523f, RelBarDrawnH = 132f;
        public const float HealthBarDrawnCx = 1294f, HealthBarDrawnCy = 101.5f, HealthBarDrawnW = 504f, HealthBarDrawnH = 127f;

        // ---- САЛЮТ ЗВЁЗД (revisions §6 / build-spec §6, слой 7 — поверх всего) -----------------------
        /// <summary>База сида: сид бёрста = база + номер бёрста, т.е. разлёт ДЕТЕРМИНИРОВАН и повторим в тесте.</summary>
        public const int StarBurstSeedBase = 20260731;
        /// <summary>Сколько звёзд в бёрсте (спек §6: 4–5).</summary>
        public const int StarBurstMin = 4, StarBurstMax = 5;
        /// <summary>Базовый кегль звезды, реф-px; множители — спековые 0.6/0.8/1.0/1.2×.</summary>
        public const float StarBaseSize = 130f;
        private static readonly float[] StarSizeScales = { 1.0f, 0.6f, 1.2f, 0.8f, 1.0f };
        /// <summary>Разлёт из центра экрана, реф-px. Верх подобран так, чтобы звезда доходила до края
        /// кадра (полукадр 540 по высоте) уже ПОЧТИ прозрачной — обрезанных краем звёзд в кадре нет.</summary>
        public const float StarTravelMin = 380f, StarTravelMax = 560f;
        /// <summary>Длительность разлёта, с (спек §6: 0.6–0.9 с).</summary>
        public const float StarFlightMin = 0.6f, StarFlightMax = 0.9f;
        /// <summary>Лёгкое вращение за полёт, град.</summary>
        public const float StarSpinMin = 40f, StarSpinMax = 150f;
        /// <summary>До этой доли полёта звезда держит ПОЛНУЮ альфу, дальше — затухание в ноль. Держим
        /// больше половины: на 0.35 салют уже в середине разлёта выцветал в бледные пятна (свой кадр).</summary>
        public const float StarHoldFraction = 0.55f;
        /// <summary>Поза для кадра дизайн-гейта: середина разлёта (все звёзды ещё в воздухе и в полную силу).</summary>
        public const float StarPreviewSeconds = 0.3f;

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

        // ---- ТРУБКА РЕБЁНКА (revisions §5b / build-spec §E) ------------------------------------------
        // ЗАМЕНИЛА старую «вспышку кнопки-ребёнка» (жёлтая лампочка + halo в правой колонке ряда): механика
        // счёта в Game та же (ChildOpen/ChildFlashing/ChildPress/пропуски), сменился ВИД и окно (5 с).
        // Две ПОЗЫ ОДНОГО спрайта (asset-map §1/§5.7): покой — `phone-rest-v2` (`hf_phone copy.png`, БЕЗ
        // дуг), звонок — `phone-ring-v2` (`hf_phone.png`, красные дуги-вибрация ЗАПЕЧЕНЫ вместе с корпусом).
        // Поэтому «пульс альфы дуг» (build-spec §6) физически невозможен и заменён КАЧАНИЕМ всей трубки
        // (asset-map §12-7).
        // Оба бокса СНЯТЫ С ЭТАЛОНОВ ИНСТРУМЕНТАЛЬНО (IoU-подгонка залитой маски корпуса трубки):
        //   покой  — «Экран спокойный обычный.png», IoU 0.65: scale 0.550, rot −13.5°  (asset-map §5.7
        //            независимо дала ровно 0.550 / +14° по часовой → сходится);
        //   звонок — «Экран звонит телефон.png»,   IoU 0.92: scale 0.621, rot −46.5°.
        // Спековые «rot ≈ −14°» для звонка (build-spec §4-E) эталону НЕ соответствуют — asset-map §4.2 сама
        // пометила угол как ненадёжный («домерить»), а нарисованный корпус на эталоне лежит под −46.5°.
        // Формат — как у остального арт-пака: cx, cyTop, w, h реф-px на ВЕСЬ спрайт (662×715); поворот —
        // вокруг центра ректа (центр текстуры совпадает с центром рисунка, так что бокс не уезжает).
        /// <summary>Покой: трубка торчит из-за левого края (нарисованный AABB = asset-map §2 −62,394,191,349).</summary>
        public static readonly Vector4 PhoneRestRect = new(39.5f, 567.0f, 364.1f, 393.3f);
        /// <summary>Звонок: трубка выехала внутрь (корпус на эталоне = asset-map §4.2 14,351,258,384).
        /// Центр сдвинут на 4 px влево и 2 px вверх от чистой IoU-подгонки (152,544): в той позе кончик
        /// нижней дуги наезжал на плашку «СПАСИБО НЕ НАДО» (замер: 47 px чернил дуги на чернилах плашки).
        /// 4/2 — максимум, который гасит наезд в 0 px, оставаясь в допуске IoU корпуса ≥0.9 (0.902).</summary>
        public static readonly Vector4 PhoneRingRect = new(144.0f, 540.0f, 411.3f, 444.2f);
        /// <summary>Наклон позы покоя, град (Unity z; минус = по часовой).</summary>
        public const float PhoneRestTilt = -13.5f;
        /// <summary>Наклон позы звонка, град (Unity z).</summary>
        public const float PhoneRingTilt = -46.5f;
        /// <summary>Выезд из-за края и уезд обратно, с (build-spec §6).</summary>
        public const float PhoneSlideSeconds = 0.3f;
        /// <summary>Качание звонящей трубки: rot ±6° с периодом ~0.12 с (build-spec §6).</summary>
        public const float PhoneWobbleDegrees = 6f, PhoneWobblePeriod = 0.12f;
        /// <summary>
        /// Тинт-множитель ПОЗЫ ПОКОЯ: эталон «Экран спокойный обычный.png» намеренно ГАСИТ спящую трубку,
        /// чтобы она не тянула глаз, а наш спрайт горит той же полной яркостью, что и на звонке. Множитель
        /// подобран ИНСТРУМЕНТАЛЬНО по кадру (зоны корпуса earpiece x[5,40] y[420,470] и shaft x[2,22]
        /// y[500,620], среднее по не-фоновым пикселям): G/B сводятся к эталонным (Δ≤8 на канал), R зажат
        /// в 1.0 — эталонная спящая трубка не просто темнее, она ОБЕСЦВЕЧЕНА (её R ВЫШЕ нашего), а умножение
        /// канал поднять не может, и любой R&lt;1 только уводит дальше. На звонке тинт снимается (Color.white)
        /// и лерпается вместе с позой за <see cref="PhoneSlideSeconds"/> — выезд «разгорается», уезд гаснет.
        /// </summary>
        public static readonly Color PhoneRestTint = new(1f, 0.830f, 0.745f, 1f);
        /// <summary>
        /// Тинт ПОНИКШЕЙ позы (r3, п.9): трубка, которую проспали, уезжает ЗАМЕТНО темнее обычного покоя —
        /// контраст с уездом после успеха, где ещё догорает салют звёзд. Живой плейтест основательницы:
        /// «пропуск звонка вообще никак не отзывается». Множитель — 0.55 от тинта покоя по всем каналам:
        /// это ровно «та же трубка, но погасшая», а не другой цвет (uGUI тинт умножает, поэтому одна
        /// доля по всем каналам сохраняет оттенок и роняет только светлоту).
        /// </summary>
        public const float PhoneMissedDim = 0.55f;
        /// <summary>Готовый тинт поникшей позы — из <see cref="PhoneRestTint"/> × <see cref="PhoneMissedDim"/>.</summary>
        public static readonly Color PhoneMissedTint = new(
            PhoneRestTint.r * PhoneMissedDim, PhoneRestTint.g * PhoneMissedDim, PhoneRestTint.b * PhoneMissedDim, 1f);

        // Baked cavity colours, sampled off `energy-battery-v2`: cream «empty», saturated yellow «full».
        private static readonly Color BatteryCream = new(253f / 255f, 249f / 255f, 230f / 255f);
        private static readonly Color BatteryYellow = new(254f / 255f, 210f / 255f, 1f / 255f);
        /// <summary>Спокойные цвета полости, открытые тесту: тревога обязана ВЕРНУТЬ ровно их.</summary>
        public static Color BatteryCreamToken => BatteryCream;
        public static Color BatteryYellowToken => BatteryYellow;

        // ---- трубка ребёнка (открывается на MD02=ДА, не по возрасту; звонит по окну Game.ChildFlashing) ----
        private GameObject _childGroup;    // весь виджет; показан пока Game.ChildOpen, гаснет после LT04
        private Image _phoneImg;           // ОДНА картинка, две позы: покой (без дуг) / звонок (с дугами)
        private Sprite _phoneRestSprite, _phoneRingSprite;
        private float _phoneOut;           // 0 = за левым краем (покой), 1 = выехала внутрь (звонок)
        private float _phoneRingClock;     // часы ТЕКУЩЕГО звонка — детерминированная фаза качания
        private bool _phoneRinging;        // прошлый Game.ChildFlashing (ловим ФРОНТ звонка)
        // r3 (п.9): ПОСЛЕДНИЙ звонок был ПРОСПАН — трубка уезжает «поникшей» (тот же спрайт покоя, но
        // притушенный сильнее обычного). Снимается следующим звонком: подняли или нет — это уже про новый.
        private bool _phoneMissed;

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
        // ЖЁСТКАЯ ТЕНЬ ПОДПИСИ — ОТДЕЛЬНЫЙ МЕШ (копия текста цветом Ink со сдвигом). Компонентом Shadow
        // её не сделать: Outline+Shadow на одном меше дают «призрак» (r3 §4(s)), а полупрозрачная тень
        // (альфа 0.32) — это ровно та «размытая», которую завернул дизайн-гейт. См. ApplyBlitzLabel.
        private Text _yesPlateShade, _noPlateShade;
        private Outline _yesLabelKant, _noLabelKant;   // кант буквы (жёсткий, Ink в блице)
        private Shadow _yesLabelSoft, _noLabelSoft;    // мягкая тень DisplayFx — живёт только в импульсе
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

        // Finale (S11) — `end.png` целиком: заголовок и плашка ЗАПЕЧЕНЫ в фоне, драйвер рисует только
        // текст В запечённой плашке + зелёную CTA под ней (asset-map §11-12).
        private Image _finaleBg;
        private Text _finaleOutcome;   // «Ты дожил до N лет» + «Причина конца: …», Arimo Bold
        private Text _finaleStory;     // склеенный некролог, Rubik

        // Tutorial modal widgets (S5) — captured for Layer-2 conformance.
        private Image _tutorialModal;
        private Image _tutorialButton;
        private Text _tutorialButtonText;

        // BLOCK$ (S10): the card is dimmed by tinting its OWN frame sprite (exact rounded silhouette — a
        // separate veil rect showed straight edges cutting across the sunburst), plus a red block-tag banner.
        private GameObject _blockOverlay;     // r3: контейнер баннера+чипа, созданный ПОСЛЕ плашек ответа
        private GameObject _blockBanner;
        private GameObject _blockBannerInk;   // чёрный keyline вокруг баннера (сосед НИЖЕ него)
        private Image _cardPriceInk;          // тот же keyline вокруг чипа цены
        // BLOCK$ price sub-line on the card: «СТОИТ N ₽» when affordable, «НУЖНО N ₽» when blocked.
        // Above the veil (drawn after it), so it stays legible in the dimmed/blocked state too.
        // Sits on a dark rounded plate (_cardPricePlate) so the gold/light text never reads as
        // «gold on yellow» against the sunburst — the S10 dark block-tag treatment.
        private Text _cardPriceText;
        private Image _cardPricePlate;

        // ---- D: «экран появления новой шкалы» (meeting-revisions §2 / build-spec §D) ------------------
        // Модальный туториал на каждом OPEN четырёх шкал: затемнение поверх геймплея, КРУПНАЯ копия самой
        // шкалы у её HUD-места, облачко-рассказ Ведущего и окно-задача. Кнопками НЕ закрывается — только
        // выполнением условия по РЕАЛЬНОМУ контролу (крутилка / датчик высоты / рычаг / «!»).
        private GameObject _nsOverlay;     // корень модалки; поднимается в конец списка на показе
        private CanvasGroup _nsFade;       // фейд 0.2 с на выходе
        private Image _nsDim;              // затемнение ~40 % INK на весь экран
        private GameObject _nsSlot;        // полноэкранный слот, куда «одалживается» настоящий HUD-виджет
        private Image _nsPlate;            // окно-задача — `task-plate-v2` (Simple, НЕ 9-slice: звёзды-лучи)
        private Image _nsBubble;           // окно-рассказ — `host-comment-v2`, ЗЕРКАЛЬНО (рупор справа)
        // ⚠ ПОЛОСА ПРОГРЕССА УДЕРЖАНИЯ СНЯТА 2026-08-07 (основательница, живой плейтест: «убрать полосу
        // прогресса в туториалах»). Прогресс показывают САМИ виджеты: батарея наполняется, монеты капают в
        // банку, маркер стоит в зоне. Гард отсутствия — NewScaleTutorialTests.NoProgressBar_OnAnyModal.
        private Text _nsTaskText;          // задача (Arimo Bold, в кремовом поле плашки)
        private Text _nsStoryText;         // рассказ (Rubik-Bold, в кремовом поле облачка)
        private NewScale _nsWhich;         // какая шкала сейчас объясняется
        private bool _nsShowing;
        private bool _nsArmed;             // РЕАЛЬНЫЙ ввод по этой шкале уже был (см. NewScaleArmed)
        private bool _nsDone;              // условие выполнено → идёт фейд
        private float _nsHold;             // секунд НЕПРЕРЫВНОГО удержания режима (осталось у ОТНОШЕНИЙ)
        private int _nsTicks;              // ПРИНЯТЫХ тиков крутилки на экране денег (условие — MoneyTutorialTicks)
        private float _nsFadeT;            // прожито фейда, с
        private readonly bool[] _nsSeen = new bool[5];   // один раз за жизнь на шкалу (индекс = NewScale)
        // ОТЛОЖЕННЫЙ показ: OPEN пришёл, но поднимать модалку сейчас нельзя (сейчас — только ВЫГОРАНИЕ под
        // ребёнком, см. ShowNewScale). OPEN не теряется: пампится в TickNewScale и стартует, как только
        // помеха снята. См. NewScalePending.
        private NewScale _nsPending = NewScale.None;

        // Одолженный виджет: настоящий HUD-виджет уезжает на модалку (увеличенным), а не клонируется, —
        // так КРУПНАЯ копия ЖИВАЯ (батарея наполняется под датчиком, сердце едет от рычага, монетка падает)
        // и рисуется поверх затемнения. Возвращается на своё место в HUD при закрытии.
        private GameObject _nsBorrowed;
        private Transform _nsBorrowedParent;
        private int _nsBorrowedIndex;
        // Раскладка виджета в HUD целиком (якоря + отступы + масштаб): модалка сдвигает его ЯКОРЯМИ, поэтому
        // и возвращать надо якоря, а не одну anchoredPosition.
        private Vector2 _nsBorrowedAnchorMin, _nsBorrowedAnchorMax, _nsBorrowedOffsetMin, _nsBorrowedOffsetMax;
        private Vector3 _nsBorrowedScale;

        /// <summary>Сколько секунд НЕПРЕРЫВНО держать режим, чтобы окно ушло (revisions §2: «~1.5 сек»).
        /// Осталось только у ОТНОШЕНИЙ: деньги закрываются счётом тиков, энергия — полной батареей,
        /// ребёнок — поднятой трубкой.</summary>
        public const float NewScaleHoldSeconds = 1.5f;
        /// <summary>Фейд ухода окна (build-spec §D: «окно уходит (фейд 0.2с)»).</summary>
        public const float NewScaleFadeSeconds = 0.2f;
        /// <summary>Затемнение геймплея под модалкой — ~40 % INK (build-spec §D).</summary>
        public const float NewScaleDimAlpha = 0.40f;
        /// <summary>
        /// Условие экрана ЭНЕРГИИ: «держи, пока батарейка НЕ ЗАПОЛНИТСЯ» (основательница, 2026-08-07).
        /// 99, а не 100, — запас ровно в один процент на дискретность шкалы: реген интегрируется дробным
        /// аккумулятором, и требовать точного попадания в 100 значило бы ловить последний целый шаг.
        /// Прежнее условие «энергия &gt;40 % удержана 1.5 с» снято вместе с ритм-механикой.
        /// </summary>
        public const int NewScaleEnergyFull = 99;
        /// <summary>
        /// Сколько ПРИНЯТЫХ тиков крутилки закрывают экран ДЕНЕГ (основательница, 2026-08-07: «крутить
        /// ручку денег надо дольше на туториале — слишком быстро пропадает»). Было 1 тик — окно исчезало
        /// раньше, чем игрок успевал понять, что произошло. 7 тиков при кэпе дохода ~5/с — это пара секунд
        /// живого верчения; монеты капают на КАЖДЫЙ тик, так что прогресс виден без всякой полоски.
        /// </summary>
        public const int MoneyTutorialTicks = 7;

        // ---- ВХОДНОЙ ЭКРАН СПЕЦРЕЖИМА (r3): те же блоки §D + зелёная CTA -----------------------------
        private GameObject _smCtaEdge;     // тёмный кант CTA (тот же приём, что у опенера/финала)
        private Image _smCta;              // сама зелёная плашка (bar-track × OnBarTrack(GoGreen))
        private Text _smCtaText;
        private SpecialMode _smWhich;
        private bool _smShowing;
        private bool _smHealthSeen;        // здоровье объясняется один раз за жизнь
        private bool _smBurnoutSeen;       // …и выгорание тоже: повторные идут короткой плашкой
        private SpecialMode _smPending;    // экран пришёл, пока сверху висело другое окно — не теряем

        /// <summary>Резерв нижней полосы окна-задачи на входном экране спецрежима: там, кроме служебной
        /// строки, стоит ещё и CTA-плашка, поэтому текст задачи ужимается сильнее, чем на §D-модалке.
        /// ⚠ 160 → 132 (дизайн-скептик, раунд 2): CTA поднялась внутрь поля, и старый резерв оставлял между
        /// текстом и ней ДЫРУ 110–135 px — особенно заметную на здоровье и блице, где служебной строки нет
        /// вовсе. Теперь полоса раздана честно: текст 391…639, служебная строка 651…685, кант CTA 699…795,
        /// низ поля 819 — зазоры 12 / 14 / 24.</summary>
        private const float SpecialTaskReserve = 132f;
        /// <summary>Центр служебной строки (клавиша эмуляции) на входном экране спецрежима: между текстом
        /// задачи и кантом CTA (см. <see cref="SpecialTaskReserve"/>).</summary>
        private const float SpecialHintLineCy = 668.5f;
        /// <summary>Зазор от нижней кромки кремового поля окна-задачи до КАНТА зелёной CTA. До 2026-08-08
        /// CTA стояла на 770 и её кант упирался в кромку поля 0…3 px — «приклеена ко дну» (дизайн-скептик,
        /// раунд 2). Коридор ≥16, взято 24 — и дыхание есть, и CTA не лезет в текстовую полосу.</summary>
        public const float SpecialCtaFieldGap = 24f;
        /// <summary>Зелёная CTA входного экрана: центр x, центр y от ВЕРХА, w, h — в кремовом поле плашки
        /// (поле 343…819), поэтому плашка целиком лежит на креме, а не на кайме со звёздами. Вертикаль
        /// ВЫВЕДЕНА из поля: низ канта = низ поля − <see cref="SpecialCtaFieldGap"/>.</summary>
        public static readonly Vector4 SpecialCtaRect = new(1009f, SpecialCtaCy, 620f, 84f);
        /// <summary>Тёмный кант этой CTA — внешний контур, по которому меряется «дыхание» до кромки поля.</summary>
        public static readonly Vector4 SpecialCtaEdgeRect = new(1009f, SpecialCtaCy, 632f, 96f);
        private const float SpecialCtaEdgeH = 96f;
        /// <summary>Центр CTA по вертикали: 819.5 (низ кремового поля) − 24 (зазор) − 48 (полвысоты канта).</summary>
        private const float SpecialCtaCy = 581f + 477f / 2f - SpecialCtaFieldGap - SpecialCtaEdgeH / 2f;

        // КРУПНЫЕ ВИДЖЕТЫ входных экранов: ИСТОЧНИК (нарисованный бокс в HUD) → ЦЕЛЬ (бокс на экране),
        // по тем же правилам, что и BigScaleSrc/Dst: у своего HUD-места, крупнее HUD, целиком в кадре с
        // полем ≥16 px, мимо текстовых полей обоих окон, мимо бейджа возраста и мимо CTA.
        //   ЗДОРОВЬЕ — У СВОЕГО HUD-МЕСТА (правый верх). ⚠ ПЕРЕСТАВЛЕНО 2026-08-08 (дизайн-скептик,
        //     раунд 2): бар стоял на 610 — то есть в слоте ОТНОШЕНИЙ — сросся кромкой с батареей и
        //     оставлял торчащий розовый обрезок плашки отношений. Канон r2-tut-rel: крупная копия стоит
        //     У СВОЕГО места и перекрывает соседей ЦЕЛИКОМ, а не наполовину. Правый верх занят облачком —
        //     поэтому на ЭТОМ экране облачко зеркалится ВЛЕВО (см. StoryBubbleLeftRect), а бар садится в
        //     коридор между ним и банкой: 640 px ширины (k≈1.27) с зазорами ≥16 до обоих.
        private static readonly Vector4 BigHealthSrc = new(HealthBarDrawnCx, HealthBarDrawnCy, HealthBarDrawnW, HealthBarDrawnH);
        private static readonly Vector4 BigHealthDst = new(1307f, 120f, 640f, 161.27f);   // k≈1.27
        //   ВЫГОРАНИЕ — та же батарея и тот же бокс, что у экрана энергии (проверенное чистое место).
        //     Боксы берутся ЧЕРЕЗ BigScaleSrc/Dst (вызовом, не полем): BigEnergySrc/Dst объявлены НИЖЕ по
        //     файлу, и статический инициализатор поля прочитал бы их нулями.

        /// <summary>Целевой бокс крупного виджета входного экрана спецрежима (нулевой — виджета нет).</summary>
        public static Vector4 BigSpecialDst(SpecialMode m) => m switch
        {
            SpecialMode.Health => BigHealthDst,
            SpecialMode.Burnout => BigScaleDst(NewScale.Energy),
            _ => Vector4.zero,      // блиц и депрессия идут БЕЗ крупного виджета (см. ShowSpecialMode)
        };

        /// <summary>Исходный (HUD) бокс того же виджета. Нулевой — у режима крупного виджета нет.</summary>
        public static Vector4 BigSpecialSrc(SpecialMode m) => m switch
        {
            SpecialMode.Health => BigHealthSrc,
            SpecialMode.Burnout => BigScaleSrc(NewScale.Energy),
            _ => Vector4.zero,
        };

        // ---- подсказки клавиш при эмуляции (плейтест 2026-08-05, перекалибровано 2026-08-07) ----------
        // ⚠ ОТКЛИК «НЕ В РИТМ» И ВЗДРАГИВАНИЕ БАТАРЕИ СНЯТЫ 2026-08-07 вместе с ритм-гейтом: отклоняться
        // больше нечему — удержание либо идёт (батарея растёт на глазах), либо нет.
        /// <summary>Приставка второй строки: она честно говорит, что это НЕ контрол автомата, а эмуляция.</summary>
        public const string KeyHintPrefix = "эмуляция: ";
        /// <summary>Приставка второй строки для ДАТЧИКА ВЫСОТЫ. Клавиша сама по себе игрока не спасает —
        /// строка обязана назвать ЖЕСТ. С 2026-08-07 жест ровно один и он же тот, который основательница
        /// делала интуитивно: «зажми Q» (и держи, пока батарейка не заполнится — это уже текст задачи).</summary>
        public const string BreathKeyHintPrefix = "эмуляция: зажми ";

        // Вторая строка окна-задачи §D: подсказка клавиши при клавиатурной эмуляции. Мелкая служебная
        // строка под текстом задачи.
        private Text _nsHintLine;
        // То же под текстом S5-подсказки (выгорание объясняет датчик высоты — там клавиша тоже нужна).
        private Text _tutHintLine;
        private ArcadeControlId? _tutHintControl;    // какой контрол объясняет ОТКРЫТАЯ сейчас S5-подсказка

        // Кэш готовой строки подсказки — по одному на каждое место (окно-задача / S5-подсказка), чтобы
        // два разных контрола на экране не выбивали друг друга из кэша. См. KeyHintLine.
        private HintCache _nsHintCache;
        private HintCache _tutHintCache;
        private HintCache _depHintCache;   // …и третье место — подсказка контрола НА экране депрессии

        /// <summary>
        /// Запомненная строка подсказки вместе с ВСЕМ, от чего она зависит: контрол, ответ «этот контрол
        /// сейчас эмулируется?» и САМА таблица клавиш (по ссылке). Пока всё три совпадают — строку
        /// пересобирать не из чего, и кадр не создаёт ни одной. Меняется только среднее (воткнули/выдернули
        /// плату) — ради этого проверка и живёт в каждом кадре.
        /// </summary>
        private struct HintCache
        {
            public bool Valid;
            public ArcadeControlId Control;
            public bool Emulated;
            public KeyboardMapping Mapping;
            public string Line;
        }

        // ---- геометрия D, СНЯТА С ЭТАЛОНА «Экран - появление новой шкалы.png» (1920×1080, PIL) ---------
        // Табличные боксы build-spec §D — ориентир; пиксель-истина — эталон (asset-map §11-а). Замеры:
        //   кремовое поле окна-задачи   x 393…1625, y 343…819  (1233×477), центр (1009, 581)
        //   кремовое поле облачка       x 1014…1747, y 107…343 (734×237),  центр (1380.5, 225)
        //   крупная батарея (обводка)   x 207…412,  y 144…546  (206×403),  центр (309.5, 345)
        // Спрайт `task-plate-v2` 1536×892, его запечённое кремовое поле — sprite (204…1442, 220…701):
        // 1233/1239 = ×0.995, т.е. плашка на эталоне нарисована практически 1:1 к исходнику.
        /// <summary>Окно-задача: спрайт целиком (центр x, центр y от ВЕРХА, w, h).</summary>
        public static readonly Vector4 TaskPlateRect = new(954.3f, 566.6f, 1528.3f, 887.5f);
        /// <summary>Кремовое поле окна-задачи на экране — сюда садится текст задачи.</summary>
        public static readonly Vector4 TaskFieldRect = new(1009f, 581f, 1233f, 477f);
        /// <summary>Окно-рассказ: спрайт `host-comment-v2` целиком, ×0.655 (рупор ЗЕРКАЛЬНО, справа).
        /// Центр по X учитывает ОТРАЖЕНИЕ: кремовое поле лежит правее центра спрайта (+96 sprite-px), а
        /// после зеркала уходит левее, поэтому рект сдвинут на +62.9 экранных px относительно поля.</summary>
        public static readonly Vector4 StoryBubbleRect = new(1443.4f, 224.7f, 946.5f, 331.4f);
        /// <summary>Кремовое поле облачка на экране — сюда садится рассказ.</summary>
        public static readonly Vector4 StoryFieldRect = new(1380.5f, 225f, 734f, 237f);
        /// <summary>
        /// ЗЕРКАЛЬНАЯ РОКИРОВКА (дизайн-скептик, раунд 2): облачко уезжает ВЛЕВО, освобождая правый верх
        /// крупному бару здоровья (его собственное HUD-место). Спрайт при этом идёт в РОДНОЙ ориентации
        /// (localScale +1, рупор слева), а кремовое поле лежит правее центра спрайта — поэтому смещение
        /// поля меняет знак: рект = поле − 62.9 (справа было поле + 62.9).
        ///
        /// Вертикаль ВЫШЕ канонической (165 против 224.7) — и это не вкусовщина, а то же правило «перекрывай
        /// соседей ЦЕЛИКОМ»: НАРИСОВАННОЕ облачко (alpha-bbox спрайта 1445×506 → поля 11.8/9.2/17.0/16.4 px
        /// на экране) обязано накрыть и плашку отношений (413…936, 35…167), и батарею (126…241, 57…276)
        /// без единого торчащего обрезка. На 165 нарисованное облачко занимает x 38…964, y 16…314 —
        /// обе фигуры внутри с полем ≥17 px.
        /// </summary>
        public static readonly Vector4 StoryBubbleLeftRect = new(500f, 165f, 946.5f, 331.4f);
        /// <summary>Кремовое поле зеркального (левого) облачка — сюда садится тот же рассказ.</summary>
        public static readonly Vector4 StoryFieldLeftRect = new(562.9f, 165.3f, 734f, 237f);
        /// <summary>Прозрачные поля РИСУНКА внутри спрайта `host-comment-v2` — в долях спрайта (L, T, R, B).
        /// Замер по самому ассету: 1445×506, alpha-bbox x 18…1430, y 26…480. Нужен всем, кто считает, что
        /// облачко реально накрывает (гард «никаких торчащих обрезков соседей»): рект облачка заметно
        /// больше нарисованной фигуры.</summary>
        public static readonly Vector4 StoryBubbleArtInset01 =
            new(18f / 1445f, 26f / 506f, 14f / 1445f, 25f / 506f);
        // Кегли сняты с эталона: задача — cap-height ≈75 px ⇒ Arimo Bold ≈104; рассказ — cap-height ≈26 px
        // и межстрочный 48 ⇒ Rubik ≈39 (тот же кегль, что у живой реплики Ведущего, BubbleTextMaxSize).
        // ⚠ ПОТОЛОК задачи снят со 104 до 96 (дизайн-скептик 2026-08-07): 104 был КРАЙНИМ кеглем эталона,
        // и КОРОТКАЯ задача (а новая задача энергии короче прежней) набиралась им впритык к кайме. Потолок
        // — это страховка «не крупнее эталона», а не цель: реальный кегль всё равно выбирает best-fit.
        private const int TaskTextMaxSize = 96, TaskTextMinSize = 40;
        // Служебная вторая строка (клавиша эмуляции / отклик «не в ритм»): МЕЛКО — втрое ниже задачи.
        private const int HintTextSize = 30;
        // Полоса под неё ВЫРЕЗАЕТСЯ из текстового поля задачи (а не кладётся поверх): иначе длинная
        // задача доезжает низом ровно туда, где стоит подсказка. Резерв постоянный, чтобы композиция окна
        // не прыгала от наличия плат. (Это резерв ПОДСКАЗКИ; снятая полоса прогресса своего резерва в
        // текстовом поле не имела — она лежала ниже, в 776…796, уже за нижним краем текста.)
        private const float HintLineReserve = 62f;
        // Центр служебной строки: под ужатым текстом задачи, у нижнего края кремового поля.
        private const float HintLineCy = 740f;
        /// <summary>
        /// Цвет служебной строки: чернила ПОЛУПРОЗРАЧНО — она обязана быть тише задачи, но остаться
        /// читаемой с дистанции автомата. Альфа 0.55 давала на кремовом поле ≈#B1AE96 — контраст 2.11:1,
        /// мало для 23-px строки (дизайн-скептик, 2026-08-05); 0.74 даёт ≈#8B8878 ≈3.3:1. Кегль, вес и
        /// сама «полупрозрачная» подача не менялись — только глубина чернил.
        /// Гард: KeyHintLine_IsLegibleOnTheCreamField.
        /// </summary>
        private static Color HintInk => new(Ink.r, Ink.g, Ink.b, 0.74f);
        private const int StoryTextMaxSize = 39, StoryTextMinSize = 22;
        // Поля текста внутри кремовых полей (чтобы best-fit не садился на рамку/звёзды).
        // ⚠ ГОРИЗОНТАЛЬНОЕ поле задачи 60 → 90 (дизайн-скептик 2026-08-07). <see cref="TaskFieldRect"/> —
        // это alpha-tight bbox кремового поля, но НАРИСОВАННАЯ кайма плашки идёт по звёздам и в верхних/
        // нижних строках съедает ещё ~46 px с каждой стороны. При поле 60 самая широкая строка задачи
        // (энергия) упиралась в кайму с зазором 7 px слева и 13 px справа — «дыхания» не оставалось.
        // 90 даёт от каймы ≥30 px на всех четырёх модалках (гард: TaskGlyphs_KeepBreathingRoom...).
        private const float TaskTextPadX = 90f, TaskTextPadY = 48f;
        private const float StoryTextPadX = 30f, StoryTextPadY = 20f;

        // ---- КРУПНАЯ копия шкалы: ИСТОЧНИК → ЦЕЛЬ, оба БОКСАМИ (cx, cy-от-верха, w, h) в 1920×1080 --------
        // Задаётся именно ЦЕЛЕВОЙ бокс, а не «точка + масштаб»: масштаб ВЫВОДИТСЯ (k = dst.w / src.w), и
        // промах константы-источника больше не умножается на масштаб. Прошлая раскладка ошибалась вдвойне:
        // (1) источником брался рект СПРАЙТА, а целью — бокс НАРИСОВАННОЙ фигуры с эталона (у батареи это
        // разные боксы: рект 126×231, рисунок 114×218, внутренняя кромка обводки 92×179), и
        // (2) позиция замораживалась в пикселях канваса на момент «одалживания» — см. PlaceBigWidget.
        //
        // ИСТОЧНИК — бокс НАРИСОВАННОГО виджета в HUD (замер по кадру hud-phonering, канвас ровно 1920×1080).
        private static readonly Vector4 BigEnergySrc = new(183.5f, 166.5f, 114f, 218f);
        private static readonly Vector4 BigRelSrc = new(674f, 100.5f, 523f, 130f);
        // Банка + монета над ней. Пересчитан после посадки монеты в горловину (монета опустилась на 3.1 px,
        // поэтому верх объединённого бокса ушёл с 6.0 на 9.1): 1663.8…1837.0 × 9.08…250.0.
        private static readonly Vector4 BigMoneySrc = new(1750.4f, 129.54f, 173.2f, 240.92f);
        private static readonly Vector4 BigChildSrc = new(144f, 540f, 411.3f, 444.2f);  // = PhoneRingRect, рисунок его заполняет
        // ЦЕЛЬ. Энергия — С ЭТАЛОНА: подобрана так, чтобы ВНУТРЕННЯЯ КРОМКА ОБВОДКИ батареи легла в
        // x 207…412, y 145…546 (см. BatteryInnerStrokeSrc и NewScaleBigWidgetLayoutTests). Остальные три
        // эталона не имеют и расставлены по правилам: у своего HUD-места, крупнее HUD, ЦЕЛИКОМ в кадре с
        // полем ≥16 px, мимо текстовых полей обоих окон и мимо бейджа возраста.
        private static readonly Vector4 BigEnergyDst = new(309.5f, 326.442f, 255.60f, 488.78f);   // k=2.2421
        private static readonly Vector4 BigRelDst = new(515f, 178f, 993.70f, 247.00f);            // k=1.90
        // ДЕНЬГИ — долг гейта 2026-08-05: «ось банка↔бейдж». Крупная банка стояла на 1764.5, бейдж возраста
        // прямо над ней — на 1745 (его НАРИСОВАННЫЙ центр, asset-map §2), и правая колонка модалки читалась
        // как две несоосные наклейки. Бейдж не трогаем (он — живой HUD на своём эталонном месте), двигаем
        // банку: dst.x = 1745, ровно ось бейджа.
        // ⚠ Сдвиг влево упирается в кремовое поле окна-задачи (оно кончается на x=1625.5, и тест
        // BigWidget_ClearsTheCreamPlates_WhereItCan требует, чтобы деньги были с ним разведены). Поэтому
        // вместе с осью пересчитан и масштаб: ширина 227 даёт левый край 1631.5, т.е. 6 px чистого поля до
        // плашки. k упал с 1.58 до 1.31 — копия по-прежнему крупнее HUD-виджета (173→227) и вдобавок села
        // по ширине бейджа (212), отчего колонка «бейдж над банкой» читается как один блок.
        private static readonly Vector4 BigMoneyDst = new(1745f, 869.85f, 227f, 315.71f);         // k=1.3106
        private static readonly Vector4 BigChildDst = new(234f, 540f, 431.87f, 466.41f);          // k=1.05

        /// <summary>Бокс ВНУТРЕННЕЙ КРОМКИ обводки батареи в HUD (замер по кадру, 1920×1080). Именно этот
        /// бокс эталон «Экран - появление новой шкалы.png» задаёт как x 207…412, y 145…546.</summary>
        public static readonly Vector4 BatteryInnerStrokeSrc = new(183.5f, 175f, 92f, 179f);
        /// <summary>Эталонный бокс той же кромки на модалке (замер с эталона, допуск ±10).</summary>
        public static readonly Vector4 BatteryInnerStrokeRef = new(309.5f, 345.5f, 206f, 402f);

        /// <summary>Бокс виджета в HUD (нарисованная фигура) — вход раскладки крупной шкалы.</summary>
        public static Vector4 BigScaleSrc(NewScale s) => s switch
        {
            NewScale.Energy => BigEnergySrc,
            NewScale.Relations => BigRelSrc,
            NewScale.Money => BigMoneySrc,
            NewScale.Child => BigChildSrc,
            _ => Vector4.zero,
        };

        /// <summary>Целевой бокс той же фигуры на модалке.</summary>
        public static Vector4 BigScaleDst(NewScale s) => s switch
        {
            NewScale.Energy => BigEnergyDst,
            NewScale.Relations => BigRelDst,
            NewScale.Money => BigMoneyDst,
            NewScale.Child => BigChildDst,
            _ => Vector4.zero,
        };

        /// <summary>Во сколько раз крупная копия больше своего HUD-виджета (выводится из боксов).</summary>
        public static float BigScaleFactor(NewScale s)
        {
            var src = BigScaleSrc(s);
            return src.z <= 0f ? 1f : BigScaleDst(s).z / src.z;
        }

        /// <summary>Поле от краёв кадра, которое крупная шкала обязана оставлять (done-contract §6).</summary>
        public const float BigScaleFrameMargin = 16f;

        /// <summary>Текстовое поле окна-задачи (кремовое поле минус поля набора) — его крупная шкала не
        /// имеет права накрывать НИ НА ОДНОЙ шкале.</summary>
        public static Vector4 TaskTextBox => new(TaskFieldRect.x, TaskFieldRect.y,
            TaskFieldRect.z - 2f * TaskTextPadX, TaskFieldRect.w - 2f * TaskTextPadY);
        /// <summary>То же для окна-рассказа.</summary>
        public static Vector4 StoryTextBox => new(StoryFieldRect.x, StoryFieldRect.y,
            StoryFieldRect.z - 2f * StoryTextPadX, StoryFieldRect.w - 2f * StoryTextPadY);
        /// <summary>Бокс текста задачи на ВХОДНОМ ЭКРАНЕ спецрежима — то, что ставит LayoutTaskWindow(true).
        /// Вынесен наружу ради гарда «вертикаль роздана»: текст ↔ служебная строка ↔ CTA ↔ кромка поля.</summary>
        public static Vector4 SpecialTaskTextRect => new(TaskFieldRect.x, TaskFieldRect.y - SpecialTaskReserve / 2f,
            TaskFieldRect.z - 2f * TaskTextPadX, TaskFieldRect.w - 2f * TaskTextPadY - SpecialTaskReserve);
        /// <summary>Бокс служебной строки (клавиша эмуляции) там же.</summary>
        public static Vector4 SpecialHintLineRect => new(TaskFieldRect.x, SpecialHintLineCy,
            TaskFieldRect.z - 2f * TaskTextPadX, 34f);
        /// <summary>Бейдж возраста — крупная шкала на него не налезает.</summary>
        public static Vector4 AgeBadgeBox => AgeBadgeRect;
        /// <summary>НАРИСОВАННЫЙ бокс бейджа возраста (asset-map §2: 1639,392,212,207) — ось правой
        /// колонки модалки, по которой выравнивается крупная банка.</summary>
        public static readonly Vector4 AgeBadgeDrawnBox = new(1745f, 495.5f, 212f, 207f);

        // ---- КАНОН-ТЕКСТЫ D (docs/new_concept/host-content.md §4, выбор основательницы 2026-07-29) ----
        // Дословно. Проверяются против самого документа (NewScaleTutorialTests), чтобы копия не разъехалась
        // с каноном. Энергия — единственное число («датчик высоты»): §Q-controls закрыт основательницей.
        /// <summary>Рассказ Ведущего на открытии ЭНЕРГИИ (host-content §4).</summary>
        public const string EnergyStoryText =
            "Ого! Что это? Первая усталость? Ты же не думал, что энергия бесконечна?";
        /// <summary>Задача на открытии ЭНЕРГИИ (host-content §4). Формулировка основательницы дословно,
        /// живой плейтест 2026-08-07: ритм-механика («дыши раз в ~2 секунды») снята, задача называет ровно
        /// один жест — зажать и держать — и ровно одно условие выхода: полная батарея.</summary>
        public const string EnergyTaskText =
            "Зажми датчик высоты — держи, пока батарейка не заполнится";
        /// <summary>Рассказ Ведущего на открытии ОТНОШЕНИЙ (host-content §4, вар.1).</summary>
        public const string RelationsStoryText =
            "Ого-го! У кого-то, кажется, появились ЧУВСТВА! Только не задуши и не забрось — любовь любит золотую середину!";
        /// <summary>Задача на открытии ОТНОШЕНИЙ (host-content §4). Формулировка основательницы,
        /// плейтест 2026-08-05: контрол назван прямо («джойстиком»), вар.1 заменён.</summary>
        public const string RelationsTaskText =
            "Двигай джойстиком — сохраняй маркер отношений в зелёной зоне";
        /// <summary>Рассказ Ведущего на открытии ДЕНЕГ (host-content §4, вар.2).</summary>
        public const string MoneyStoryText =
            "Добро пожаловать во взрослую жизнь! Денежки любят тех, кто их крутит. Так покрути же!";
        /// <summary>Задача на открытии ДЕНЕГ (host-content §4, вар.2).</summary>
        public const string MoneyTaskText =
            "Верти ручку — и монетки посыплются в копилку";
        /// <summary>Рассказ Ведущего на открытии РЕБЁНКА (host-content §4, вар.1).</summary>
        public const string ChildStoryText =
            "Пополнение в семействе! Теперь вас трое! Малыш будет звонить — не игнорируй, а то запишем в плохие родители!";
        /// <summary>Задача на открытии РЕБЁНКА (host-content §4, вар.1).</summary>
        public const string ChildTaskText =
            "Когда телефон слева зазвонит — жми «!», чтобы поднять трубку";

        // ---- КАНОН-ЧЕРНОВИКИ ВХОДНЫХ ЭКРАНОВ СПЕЦРЕЖИМОВ (host-content §4, помечены «✍ черновик») -----
        // Все четыре пары «рассказ + задача» лежат в docs/new_concept/host-content.md §4 с пометкой
        // «✍ черновик — основательница правит свободно» и сверяются с документом дословно
        // (NewScaleCanonTextTests), чтобы копия не разъехалась с каноном. Тон — Цезарь Фликерман.

        /// <summary>
        /// КОНТРОЛ ЛОВЛИ ДЕПРЕССИИ — одна константа на все тексты (п.3г контракта). ⚠ ВОПРОС ЗАКРЫТ
        /// ОСНОВАТЕЛЬНИЦЕЙ 2026-08-08: ловля идёт по КНОПКЕ «!» (<see cref="GameInput.ChildPress"/>,
        /// BangButton кабинета), как и стояло в спеке встречи, а не по зелёной. Заготовка сработала как
        /// задумано — смена контрола вышла правкой одной строки текста и одной ветки в Game.
        /// </summary>
        public const string DepressionCatchControlName = "«!»";

        /// <summary>Рассказ Ведущего на открытии ЗДОРОВЬЯ (30). ✍ черновик.</summary>
        public const string HealthStoryText =
            "А годы-то берут своё! С этого дня здоровье тает само — просто потому, что ты живёшь.";
        /// <summary>Задача на открытии ЗДОРОВЬЯ (30). Своего контрола у шкалы нет — лечат ВЫБОРЫ. ✍ черновик.</summary>
        public const string HealthTaskText =
            "Контрола у здоровья нет — лечись выборами за деньги, если накопил";

        /// <summary>Рассказ Ведущего на входе в БЛИЦ — то самое объявление кризиса (HostContent.CrisisAnnounce).</summary>
        public static string BlitzStoryText => HostContent.CrisisAnnounce;
        /// <summary>Задача на входе в БЛИЦ — правила блица из crisis-content §2. ✍ черновик.</summary>
        public const string BlitzTaskText =
            "Пять мыслей по пять секунд — жми «ВСЁ НОРМАЛЬНО». Кнопки прыгают местами!";

        /// <summary>Рассказ Ведущего на входе в ДЕПРЕССИЮ — глухое объявление (HostContent.DepressionAnnounce).</summary>
        public static string DepressionStoryText => HostContent.DepressionAnnounce;
        /// <summary>Задача на входе в ДЕПРЕССИЮ (crisis-content §1). Контрол — через константу. ✍ черновик.</summary>
        public static string DepressionTaskText =>
            "Лови пульс: жми " + DepressionCatchControlName + " в момент вспышки — пять попаданий вернут краски";
        /// <summary>Подсказка контрола НА САМОМ экране депрессии (п.3в) — из той же константы.</summary>
        public static string DepressionBoardHint =>
            "лови пульс — жми " + DepressionCatchControlName.ToLowerInvariant();

        /// <summary>Рассказ Ведущего на ПЕРВОМ выгорании. ✍ черновик.</summary>
        public const string BurnoutStoryText =
            "Перегорел! Бывает с лучшими из нас. Всё теперь даётся туго — и деньги идут вдвое медленнее.";
        /// <summary>Задача на ПЕРВОМ выгорании — формулировка основательницы дословно (п.5г). ✍ черновик.</summary>
        public const string BurnoutTaskText =
            "Зажми датчик высоты и держи, пока не придёшь в себя";
        /// <summary>Короткая плашка ПОВТОРНОГО выгорания (без блокировки) — заголовок.</summary>
        public const string BurnoutPlateTitle = "ВЫГОРАНИЕ";
        /// <summary>…и её вторая строка: что делать, одной фразой.</summary>
        public const string BurnoutPlateSubtitle = "зажми датчик высоты";

        /// <summary>Реплика Ведущего на ПРОПУЩЕННЫЙ звонок ребёнка (п.9). ✍ черновик.</summary>
        public const string ChildMissedLine = "Малыш ждал…";

        /// <summary>CTA входного экрана спецрежима — тот же блок и та же грамматика, что у опенера/финала.</summary>
        public const string SpecialModeCtaText = "ПОНЯЛ — ЖМИ ЗЕЛЁНУЮ";

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
        // Тот же приём для входного экрана спецрежима, но строже — см. OnInput. Сбрасывается в Update.
        private bool _smClosedThisFrame;
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
        private Text _depHint;             // «лови пульс — жми ЗЕЛЁНУЮ» (называет КОНТРОЛ, п.3в)
        private Text _depKeyHint;          // …и клавиша под ней при клавиатурной эмуляции
        private int _depMutterCount;       // muttering index (one muted host line per catch)
        // Depression colour tokens.
        private static readonly Color GrayWash = new(0.50f, 0.50f, 0.53f);   // the B&W wash tint

        // ---- §4 тревоги: состояние на 4 шкалы (индекс = (int)AlarmScale) -----------------------------
        private const int AlarmCount = 4;
        private readonly bool[] _alarmOn = new bool[AlarmCount];
        /// <summary>Время В ТРЕВОГЕ, с — от него берётся фаза пульса. НЕ Time.time: на паузе часы стоят,
        /// поэтому пульс детерминирован для кадра/теста и честно замирает вместе с игрой (как купол §5a).</summary>
        private readonly float[] _alarmClock = new float[AlarmCount];
        /// <summary>Вес подсветки: 1 в тревоге, гаснет до 0 за <see cref="AlarmFadeSeconds"/> (плавный фейд).</summary>
        private readonly float[] _alarmWeight = new float[AlarmCount];
        /// <summary>Секунд с последнего ввода игрока ПО ЭТОЙ шкале — окно §6-триггера салюта.</summary>
        private readonly float[] _sinceScaleInput = new float[AlarmCount];
        /// <summary>Карточка, которую видел ПРЕДЫДУЩИЙ такт <see cref="ReflectAlarms"/>. Смена карточки —
        /// это «тревога денег погасла сама», а не починка игроком (см. ReflectAlarms).</summary>
        private Card _lastReflectCard;

        // ---- §6 салют звёзд ---------------------------------------------------------------------------
        private GameObject _fxLayer;      // слой 7 — поверх всего (build-spec §1.3)
        private Sprite _starSprite;
        private int _burstCount;          // счётчик бёрстов = сид (детерминированный разлёт)
        private readonly List<Star> _stars = new();

        /// <summary>Одна летящая звезда салюта. Продвигается ШАГОМ ВРЕМЕНИ (AdvanceStars), не корутиной:
        /// так бёрст детерминирован, позируется для кадра и переживает заморозку драйвера.</summary>
        private struct Star
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 Dir;     // единичное направление разлёта
            public float Dist;      // сколько реф-px пролетит
            public float Dur;       // за сколько секунд
            public float Spin;      // поворот за полёт, град (знак — сторона)
            public float T;         // прожито, с
        }

        // ---- Host (Ведущий): speech bubble (S3) ----
        // Tunable (default; noted in the report). The clock is injected real-time via Update.
        // Рубрика-баннер вехи (S4) и её «баннер-бит» СНЯТЫ (плейтест 2026-08-05) — см. BuildHost.
        public const float BubbleSeconds = 2f;   // speech bubble auto-hide (~2s)

        private HostVoice _voice;
        private readonly TimedReveal _bubbleTimer = new(BubbleSeconds);

        private GameObject _hostBubble;   // yellow bubble.png (9-slice), S3 corner
        private Text _bubbleText;

        private Coroutine _cardAnim;
        private Coroutine _moneyPulse;

        // Income cap (~5/s): applied ONLY on the gameplay-crank branch in OnInput. Space does nothing
        // outside Playing (crank-only), so the cap never interacts with any confirm.
        private readonly MoneyTickThrottle _crankCap = new();

        // ⚠ ЧЕТЫРЕ прежних S5-хинта открытий (деньги 18, отношения 20, энергия 25, ребёнок «свадьба+2»)
        // СНЯТЫ этим инкрементом: их заменил модальный экран §D (канон-тексты — константы *StoryText /
        // *TaskText выше, host-content §4). Дублирующие тексты убраны из кода целиком, чтобы вторая
        // формулировка той же задачи не осталась в пуле подсказок. На S5-подсказке остались только
        // ЗДОРОВЬЕ (30) и ВЫГОРАНИЕ — их meeting-revisions §2 не перечисляет.
        // ⚠ И ПОСЛЕДНИЕ ДВА S5-ТЕКСТА СНЯТЫ 2026-08-07 (r3): здоровье (30) и выгорание переехали на
        // ВХОДНОЙ ЭКРАН СПЕЦРЕЖИМА (HealthStoryText/HealthTaskText, BurnoutStoryText/BurnoutTaskText —
        // канон-черновики host-content §4). Ни одна ПРОДАКШН-ветка больше не поднимает жёлтую модалку S5:
        // её механика (ShowTutorial/DismissTutorial + полная заморозка ввода) остаётся общим примитивом
        // «блокирующая подсказка» и Слой-2-швом (DebugShowTutorial), но своих текстов у неё больше нет.

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
        /// <summary>§4: полоса «остаток заряда» от живого уровня до дна полости — красная только в тревоге.</summary>
        public Image EnergyCharge => _energyCharge;
        /// <summary>§4: тревожная (красная) копия батареи — кроссфейдится поверх спокойной по весу тревоги.</summary>
        public Image BatteryAlarmImage => _batteryAlarm;
        /// <summary>The lightning layer — drawn whole above the mask and the top-up at every level.</summary>
        public Image EnergyBolt => _energyBolt;
        public GameObject BalancerGroup => _balancerGroup;
        public Image RelBarImage => _relBarImg;
        /// <summary>§4: красный кант вокруг бара отношений (альфа = вес тревоги).</summary>
        public Image RelAlarmKant => _relKant;
        /// <summary>§4: красный кант вокруг бара здоровья.</summary>
        public Image HealthAlarmKant => _healthKant;
        /// <summary>§4: чёрное кольцо СНАРУЖИ красного канта отношений (keyline арт-пака).</summary>
        public Image RelAlarmKantInk => _relKantInk;
        /// <summary>§4: чёрное кольцо СНАРУЖИ красного канта здоровья.</summary>
        public Image HealthAlarmKantInk => _healthKantInk;
        /// <summary>§4: шкала СЕЙЧАС в тревоге (после гистерезиса).</summary>
        public bool AlarmActive(AlarmScale s) => _alarmOn[(int)s];
        /// <summary>§4: вес подсветки 0…1 — 1 в тревоге, гаснет за <see cref="AlarmFadeSeconds"/>.</summary>
        public float AlarmWeight(AlarmScale s) => _alarmWeight[(int)s];
        /// <summary>§4: часы «сколько шкала в тревоге» — источник ДЕТЕРМИНИРОВАННОЙ фазы пульса.</summary>
        public float AlarmClock(AlarmScale s) => _alarmClock[(int)s];
        /// <summary>§4: текущая яркость пульса 0.72…1.0 (функция от <see cref="AlarmClock"/>, не от Time.time).</summary>
        public float AlarmPulseBrightness(AlarmScale s) => AlarmBrightness((int)s);
        /// <summary>§6: сколько бёрстов салюта уже отстреляно (он же сид следующего).</summary>
        public int StarBurstCount => _burstCount;
        /// <summary>§6: сколько звёзд ЛЕТИТ прямо сейчас (0 = салют самоочистился).</summary>
        public int ActiveStarCount => _stars.Count;
        /// <summary>§6: слой FX (build-spec §1.3 — слой 7, поверх всего).</summary>
        public GameObject StarLayer => _fxLayer;
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
        // ---- D-модалка «появление новой шкалы» (Слой-2) ----
        /// <summary>§D: корень модального экрана новой шкалы (затемнение + окна + крупная шкала).</summary>
        public GameObject NewScaleOverlay => _nsOverlay;
        /// <summary>§D: модалка поднята прямо сейчас (включая 0.2 с фейда на выходе).</summary>
        public bool NewScaleShowing => _nsShowing;
        /// <summary>§D: какая шкала объясняется (None, если модалки нет).</summary>
        public NewScale NewScaleKind => _nsShowing ? _nsWhich : NewScale.None;
        /// <summary>§D: полноэкранное затемнение под модалкой (~40 % INK).</summary>
        public Image NewScaleDim => _nsDim;
        /// <summary>§D: окно-задача — спрайт `task-plate-v2` (Simple, не 9-slice).</summary>
        public Image NewScaleTaskPlate => _nsPlate;
        /// <summary>§D: окно-рассказ — `host-comment-v2`, отражённое (рупор справа, как на эталоне).</summary>
        public Image NewScaleStoryBubble => _nsBubble;
        /// <summary>§D: текст задачи (Arimo Bold в кремовом поле плашки).</summary>
        public Text NewScaleTaskText => _nsTaskText;
        /// <summary>§D: текст рассказа (Rubik-Bold в кремовом поле облачка).</summary>
        public Text NewScaleStoryText => _nsStoryText;
        /// <summary>§D: КРУПНАЯ копия шкалы — это НАСТОЯЩИЙ HUD-виджет, одолженный модалке.</summary>
        public GameObject NewScaleBigWidget => _nsBorrowed;
        /// <summary>§D: доля выдержанного режима 0…1 (осталась у ОТНОШЕНИЙ; ВИДИМОЙ полоски больше нет —
        /// основательница сняла её 2026-08-07, прогресс читается по самому виджету).</summary>
        public float NewScaleHoldFraction => Mathf.Clamp01(_nsHold / NewScaleHoldSeconds);
        /// <summary>§D: сколько ПРИНЯТЫХ тиков крутилки уже набрано на экране денег.</summary>
        public int NewScaleCrankTicks => _nsTicks;
        /// <summary>Вторая строка окна-задачи §D (подсказка клавиши эмуляции).</summary>
        public Text NewScaleHintLine => _nsHintLine;
        /// <summary>Вторая строка S5-подсказки (подсказка клавиши).</summary>
        public Text TutorialHintLine => _tutHintLine;
        /// <summary>Test seam: САМА ссылка на закэшированную строку подсказки §D. Тест сравнивает её
        /// ReferenceEquals два кадра подряд — если строка пересобирается, ссылка меняется, даже когда
        /// текст совпадает (и Text.text этого уже не покажет: одинаковую строку туда не переприсваивают).</summary>
        public string NewScaleHintCachedLine => _nsHintCache.Line;
        /// <summary>§D: по этой шкале уже был РЕАЛЬНЫЙ принятый ввод (крутилка/датчик/рычаг/«!»).</summary>
        public bool NewScaleArmed => _nsArmed;
        /// <summary>§D: условие выполнено, идёт фейд ухода.</summary>
        public bool NewScaleSatisfied => _nsDone;
        /// <summary>§D: OPEN пришёл, но экран ОТЛОЖЕН до снятия помехи (выгорание под ребёнком).
        /// None — ничего не отложено. Открытие не теряется: поднимется само (PumpPendingNewScale).</summary>
        public NewScale NewScalePending => _nsPending;
        // ---- r3: входной экран СПЕЦРЕЖИМА (Слой-2) ----
        /// <summary>r3: входной экран спецрежима поднят прямо сейчас.</summary>
        public bool SpecialModeShowing => _smShowing;
        /// <summary>r3: какой именно спецрежим объясняется (None, если экрана нет).</summary>
        public SpecialMode SpecialModeKind => _smShowing ? _smWhich : SpecialMode.None;
        /// <summary>r3: экран пришёл, но ОТЛОЖЕН — сверху висит другое окно. None — очередь пуста.</summary>
        public SpecialMode SpecialModePending => _smPending;
        /// <summary>r3: зелёная CTA входного экрана — та же сборка, что у опенера/финала.</summary>
        public Image SpecialModeCta => _smCta;
        /// <summary>r3: тёмный кант этой CTA — внешний контур, по которому и меряется «дыхание» до кромки
        /// кремового поля окна-задачи (дизайн-скептик, раунд 2).</summary>
        public GameObject SpecialModeCtaEdge => _smCtaEdge;
        /// <summary>r3: подпись на этой CTA.</summary>
        public Text SpecialModeCtaLabel => _smCtaText;
        /// <summary>r3: короткая плашка ПОВТОРНОГО выгорания (полноэкранный захват S7 снят).</summary>
        public GameObject BurnoutPlateGroup => _burnoutPlate;
        /// <summary>r3: контейнер BLOCK$-баннера и чипа цены — создан ПОСЛЕ плашек ответа (z-порядок).</summary>
        public GameObject BlockOverlay => _blockOverlay;
        /// <summary>r3: подсказка контрола НА экране депрессии («лови пульс — жми зелёную»).</summary>
        public Text DepressionHintText => _depHint;
        /// <summary>r3: клавиша под ней при клавиатурной эмуляции (на стойке пусто).</summary>
        public Text DepressionKeyHint => _depKeyHint;
        /// <summary>r3: трубка уехала ПОНИКШЕЙ — последний звонок был проспан.</summary>
        public bool ChildPhoneMissed => _phoneMissed;
        public Image TutorialModal => _tutorialModal;
        public Image TutorialButton => _tutorialButton;
        public Text TutorialButtonText => _tutorialButtonText;
        /// <summary>S11: фон финала — `end.png` целиком (кулисы + запечённые «ИТОГИ ШОУ» и плашка).</summary>
        public Image FinaleBackground => _finaleBg;
        /// <summary>S11: строка исхода («Ты дожил до N лет» + причина) в запечённой кремовой плашке.</summary>
        public Text FinaleOutcomeText => _finaleOutcome;
        /// <summary>S11: склеенный некролог в той же запечённой плашке, под строкой исхода.</summary>
        public Text FinaleStoryText => _finaleStory;
        public GameObject BlockBanner => _blockBanner;
        /// <summary>S10: чёрный keyline вокруг BLOCK$-баннера (сосед НИЖЕ него).</summary>
        public GameObject BlockBannerInk => _blockBannerInk;
        public Text CardPriceText => _cardPriceText;
        public Image CardPricePlate => _cardPricePlate;
        /// <summary>S10: чёрный keyline вокруг чипа цены (сосед НИЖЕ него).</summary>
        public Image CardPriceInk => _cardPriceInk;
        public GameObject BurnoutPlate => _burnoutPlate;
        public GameObject BreakupPlate => _breakupPlate;
        public GameObject BalancerMarker => _balancerMarker != null ? _balancerMarker.gameObject : null;
        /// <summary>§5b: контейнер трубки — активен ровно пока механика ребёнка открыта.</summary>
        public GameObject ChildGroup => _childGroup;
        /// <summary>§5b: сама трубка (один Image, две позы — покой без дуг / звонок с дугами).</summary>
        public Image ChildPhoneImage => _phoneImg;
        /// <summary>§5b: 0 = трубка за левым краем (покой), 1 = выехала внутрь (звонок).</summary>
        public float ChildPhoneOut => _phoneOut;
        /// <summary>§5b: часы ТЕКУЩЕГО звонка — ДЕТЕРМИНИРОВАННАЯ фаза качания (не Time.time).</summary>
        public float ChildPhoneRingClock => _phoneRingClock;
        public Image BrightnessVeil => _brightness;
        public Text TutorialText => _tutorialText;
        public GameObject HostBubble => _hostBubble;
        public Text HostBubbleText => _bubbleText;
        public GameObject CrisisInfo => _crisisInfo;
        public Text CrisisInfoText => _crisisInfoText;
        public GameObject ImpulseWarning => _impulseWarning;
        public Text YesPlateText => _yesPlateText;
        public Text NoPlateText => _noPlateText;
        /// <summary>Теневые КОПИИ подписей плашек (жёсткая тень блица — отдельный меш, см. ApplyBlitzLabel).</summary>
        public Text YesPlateShade => _yesPlateShade;
        public Text NoPlateShade => _noPlateShade;
        public bool HostBubbleVisible => _bubbleTimer.Visible;
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

        /// <summary>Test/screenshot hook: raise a tutorial modal with the given body over live gameplay.</summary>
        public void DebugShowTutorial(string text, ArcadeControlId? control = null)
        {
            bool dummy = false;
            ShowTutorial(text, ref dummy, control);
        }

        /// <summary>Layer-2 seam (§D): поднять модальный экран новой шкалы поверх живой игры — тем же
        /// путём, каким его поднимает открытие шкалы (одалживание виджета, канон-тексты, пауза).</summary>
        public void DebugShowNewScale(NewScale which)
        {
            _nsSeen[(int)which] = false;
            ShowNewScale(which);
        }

        /// <summary>Test seam: снять §D-модалку тихо (без салюта), как при уходе из Playing.</summary>
        public void DebugCloseNewScale() => CloseNewScale(reward: false);

        /// <summary>Слой-2 (r3): поднять входной экран спецрежима тем же путём, каким его поднимает вход
        /// в сам режим (крупный виджет, канон-черновики, пауза, зелёная CTA).</summary>
        public void DebugShowSpecialMode(SpecialMode which) => ShowSpecialMode(which);

        /// <summary>Test seam (r3): снять входной экран тем же путём, что и зелёная кнопка.</summary>
        public void DebugCloseSpecialMode() => CloseSpecialMode();

        /// <summary>
        /// Test seam: сбросить одноразовые «съесть остаток кадра» гейты — ровно то, что делает начало
        /// <see cref="Update"/>. Синхронный тест-цикл, который гонит вводы без реальных кадров, иначе
        /// упёрся бы в гейт, поставленный закрытием подсказки/входного экрана, и остался бы без ввода.
        /// </summary>
        public void DebugClearFrameGuards() { _dismissedThisFrame = false; _smClosedThisFrame = false; }

        /// <summary>Test seam: снять S5-подсказку тем же путём, что и зелёная кнопка.</summary>
        public void DebugDismissTutorial() => DismissTutorial();

        /// <summary>Layer-2 seam (§D): продвинуть модалку на dt (удержание + фейд) без ожидания кадров —
        /// ровно тем же вызовом, что и Update.</summary>
        public void DebugAdvanceNewScale(float dt) => TickNewScale(dt);

        /// <summary>
        /// Screenshot pose (§D): модальный экран новой шкалы поверх обычного кадра — с КРУПНОЙ живой
        /// шкалой у её HUD-места, окном-рассказом и окном-задачей. Драйвер замораживается, чтобы кадр
        /// был стабилен; для «ребёнка» трубка ставится в позу звонка тем же путём, что в живой игре.
        /// </summary>
        public void DebugPreviewNewScale(NewScale which)
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            RestoreNormalPlates();
            // Возраст показываем «свой» для каждой шкалы, а HUD раскрываем ровно до неё — на эталоне
            // за модалкой видны только уже открытые виджеты.
            float age = which switch
            {
                NewScale.Money => 18f,
                NewScale.Relations => 20f,
                NewScale.Energy => 25f,
                _ => 33f,
            };
            ApplyAgeGates(age);
            _ageText.text = Mathf.FloorToInt(age).ToString();
            _moneyText.text = FormatMoneyJar(120);
            _cardText.text = "Взять ипотеку на 25 лет?";
            // На экране ЭНЕРГИИ батарея показывается В ПРОЦЕССЕ НАПОЛНЕНИЯ — это и есть кадр механики
            // «зажми и держи»: полная батарея означала бы «задача уже выполнена».
            ReflectEnergyLevel(which == NewScale.Energy ? 55f : 100f);
            ReflectHealthMarker(100f);
            ReflectRelationsMarker(55f, redZone: false);
            _yesPlate.color = Color.white; _noPlate.color = Color.white;
            SetYesLabel("ДА"); SetNoLabel("СПАСИБО,\nНЕ НАДО");
            ReflectDome(6f, 6f);
            if (which == NewScale.Child)
            {
                _childGroup.SetActive(true);
                _phoneRinging = true;
                _phoneOut = 1f;
                _phoneRingClock = 0f;
                _phoneImg.sprite = _phoneRingSprite;
                ApplyPhonePose(1f, 0f);
            }
            DebugShowNewScale(which);
            ReflectKeyHints(0f);   // поза замораживает Update — вторую строку заполняем явно
            enabled = false;
        }

        /// <summary>
        /// Test hook: force the finale panel visible and render an arbitrary necrolog into the baked plate,
        /// without driving a whole life — so a Layer-2 test can stress a worst-case LONG story against the
        /// measured cream field. Mirrors the finale branch of <see cref="Refresh"/>.
        /// </summary>
        public void DebugRenderFinale(NecrologResult n, int age = 100)
        {
            _openerPanel.SetActive(false);
            _gamePanel.SetActive(false);
            _finalePanel.SetActive(true);
            RenderFinaleTexts(n, age);
        }

        // ---- Design-gate Layer-3 screenshot hooks: render a state overlay in a representative pose over the
        // live game panel and FREEZE the driver (disable Update) so the capture is stable. Visual-only — the
        // pure Game is never touched; these only flip the driver's own overlay Images on for a screenshot. ----
        public void DebugPreviewBurnout()
        {
            DebugPreviewArcadeShot();          // обычный кадр (доска видна) — плашка её НЕ накрывает
            ApplyAgeGates(33f);
            ReflectEnergyLevel(8f);            // выгорание = энергия на дне…
            DebugPaintAlarm(AlarmScale.Energy);// …и, значит, батарея горит §4-тревогой
            _burnoutPlate.SetActive(true);
        }

        /// <summary>Screenshot seam: зажечь §4-тревогу шкалы на пике пульса (в позе Update не крутится).</summary>
        public void DebugPaintAlarm(AlarmScale s)
        {
            _alarmOn[(int)s] = true;
            _alarmWeight[(int)s] = 1f;
            _alarmClock[(int)s] = 0f;
            PaintAlarm(s, 1f, AlarmBrightness((int)s));
        }

        /// <summary>
        /// Screenshot pose (r3): ВХОДНОЙ ЭКРАН СПЕЦРЕЖИМА поверх обычного кадра — затемнение, крупный
        /// виджет, облачко-рассказ, окно-задача и зелёная CTA. Драйвер замораживается ради стабильного
        /// кадра; состояние шкал ставится «как в жизни» для этого режима (здоровье уже тает, батарея на
        /// дне у выгорания, серая мойка у депрессии).
        /// </summary>
        public void DebugPreviewSpecialMode(SpecialMode which)
        {
            _openerPanel.SetActive(false); _finalePanel.SetActive(false); _gamePanel.SetActive(true);
            RestoreNormalPlates();
            float age = which switch
            {
                SpecialMode.Health => 30f,
                SpecialMode.Burnout => 27f,
                SpecialMode.Blitz => 45f,
                _ => 47f,
            };
            ApplyAgeGates(age);
            _ageText.text = Mathf.FloorToInt(age).ToString();
            _moneyText.text = FormatMoneyJar(140);
            _cardText.text = "Взять ипотеку на 25 лет?";
            ReflectEnergyLevel(which == SpecialMode.Burnout ? 8f : 62f);
            // На экране ВЫГОРАНИЯ батарея обязана быть КРАСНОЙ — этого и просила основательница
            // («крупная красная батарея вместо программного ВЫГОРАНИЕ!»). В живой игре её красит §4-тревога
            // (энергия <20 %), а в замороженной позе Update не крутится — зажигаем явно.
            if (which == SpecialMode.Burnout) DebugPaintAlarm(AlarmScale.Energy);
            ReflectHealthMarker(which == SpecialMode.Health ? 100f : 64f);
            ReflectRelationsMarker(55f, redZone: false);
            _yesPlate.color = Color.white; _noPlate.color = Color.white;
            ReflectDome(6f, 6f);
            if (which == SpecialMode.Depression)
            {
                // Серая мойка стоит ПОД экраном: игрок уже в «тёмной полосе», ему объясняют, как выйти.
                _depressionGroup.SetActive(true);
                _depressionVeil.color = new Color(GrayWash.r, GrayWash.g, GrayWash.b, 0.92f);
                _depressionGrain.color = new Color(1f, 1f, 1f, 0.06f);
                _depressionPulse.gameObject.SetActive(true);
                _depressionPulse.color = new Color(0.90f, 0.90f, 0.97f, 0.42f);
                _depressionPulse.rectTransform.localScale = Vector3.one * 0.86f;
                _depHint.text = DepressionBoardHint;
            }
            ShowSpecialMode(which);
            ReflectKeyHints(0f);   // поза замораживает Update — служебные строки заполняем явно
            enabled = false;
        }

        /// <summary>
        /// Screenshot pose (r3, п.9): обычный кадр + трубка ПОНИКШАЯ — звонок проспан, спрайт покоя,
        /// тинт <see cref="PhoneMissedTint"/>, реплика Ведущего в облачке. Сверяется по глазам с позой
        /// обычного покоя (`phonerest`): пропуск обязан читаться иначе.
        /// </summary>
        public void DebugPreviewChildPhoneMissed()
        {
            DebugPreviewArcadeShot();
            _childGroup.SetActive(true);
            _phoneRinging = false;
            _phoneMissed = true;
            _phoneOut = 0.55f;            // «на полпути за край» — момент уезда, а не пустое место
            _phoneRingClock = 0f;
            _phoneImg.sprite = _phoneRestSprite;
            ApplyPhonePose(_phoneOut, 0f);
            _bubbleTimer.Show(ChildMissedLine);
            _hostBubble.SetActive(true);
            _bubbleText.text = ChildMissedLine;
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

        /// <summary>
        /// Screenshot pose (§5b): обычный кадр + трубка в позе ЗВОНКА — выехала целиком (out = 1), спрайт
        /// с запечёнными дугами, качание в фазе 0 (детерминированно и воспроизводимо). Сверяется с
        /// эталоном «Экран звонит телефон.png».
        /// </summary>
        public void DebugPreviewChildCall()
        {
            DebugPreviewArcadeShot();                 // обычный кадр, драйвер заморожен
            _childGroup.SetActive(true);
            _phoneRinging = true;
            _phoneOut = 1f;
            _phoneRingClock = 0f;
            _phoneImg.sprite = _phoneRingSprite;
            ApplyPhonePose(1f, 0f);
        }

        /// <summary>
        /// Screenshot pose (§5b): обычный кадр + трубка в ПОКОЕ — за левым краем, спрайт БЕЗ дуг.
        /// Сверяется с эталоном «Экран спокойный обычный.png».
        /// </summary>
        public void DebugPreviewChildPhoneRest()
        {
            DebugPreviewArcadeShot();
            _childGroup.SetActive(true);
            _phoneRinging = false;
            _phoneOut = 0f;
            _phoneRingClock = 0f;
            _phoneImg.sprite = _phoneRestSprite;
            ApplyPhonePose(0f, 0f);
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
            SetBlockBannerVisible(true);
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
            SetYesLabel("ДА"); SetNoLabel("СПАСИБО,\nНЕ НАДО");
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

        /// <summary>
        /// Screenshot pose (§4): обычный кадр, но ВСЕ ЧЕТЫРЕ шкалы в тревоге и пульс стоит на ПИКЕ
        /// (фаза 0 — детерминированно и воспроизводимо). Энергия 12 % — та самая поза эталона
        /// «Экран подсвечена красным шкала.png»: красная батарея с тонкой полосой остатка внизу.
        /// Деньги в тревоге бывают только на BLOCK$-карточке, поэтому пришлось поднять и её баннер.
        /// Только визуал: чистый Game не трогается.
        /// </summary>
        public void DebugPreviewAlarms()
        {
            DebugPreviewArcadeShot();                  // обычный кадр, драйвер заморожен
            ReflectEnergyLevel(12f);
            ReflectHealthMarker(14f);
            ReflectRelationsMarker(28f, redZone: false);
            _moneyText.text = FormatMoneyJar(0);
            _cardText.text = "Пора подлечиться!";
            SetCardBlockedDim(true);
            SetBlockBannerVisible(true);
            ApplyPriceLabel(true, 100, blocked: true);
            _yesPlate.color = PlateMute; _noPlate.color = PlateMute;
            for (int i = 0; i < AlarmCount; i++)
            {
                _alarmOn[i] = true;
                _alarmWeight[i] = 1f;
                _alarmClock[i] = 0f;                   // пик пульса
                PaintAlarm((AlarmScale)i, 1f, AlarmBrightness(i));
            }
            enabled = false;
        }

        /// <summary>
        /// Screenshot pose (§6): обычный кадр + бёрст салюта, отмотанный на СЕРЕДИНУ разлёта
        /// (<see cref="StarPreviewSeconds"/>) — все 4–5 звёзд ещё в воздухе, видно калибры и разлёт.
        /// Разлёт детерминирован сидом, поэтому кадр воспроизводим.
        /// </summary>
        /// <summary>
        /// Кадр §D-окна ДЕНЕГ в РОВНО той ситуации, на которую пожаловалась основательница (r4 п.1а):
        /// счёт 0 ₽, тревога банки насильно зажжена — и §4 отрабатывает поверх. Если подавление на
        /// месте, кадр выходит БЕЗ единого красного пикселя; если его убрать, банка загорится и это
        /// будет видно на снимке. То есть поза не «показывает результат», а ПРОВЕРЯЕТ его глазом.
        /// </summary>
        public void DebugPreviewMoneyTutorialQuiet()
        {
            DebugPreviewNewScale(NewScale.Money);
            _moneyText.text = FormatMoneyJar(0);
            DebugPaintAlarm(AlarmScale.Money);   // зажечь насильно…
            ReflectAlarms(0.1f);                 // …и дать §4 решить: своё обучение её гасит
        }

        /// <summary>
        /// Кадр БЛИЦА с плашками, переодетыми в арт-пак (r4 п.4). <paramref name="punch"/> — то же
        /// состояние «нажатие засчитано», что играет корутина PunchPlate: плашка просажена в нижней
        /// точке дуги (та же формула, что в <see cref="PunchPlate"/>, взятая на пике k = 0.5).
        /// </summary>
        public void DebugPreviewBlitzPlates(bool punch)
        {
            DebugPreviewArcadeShot();
            _crisisUiActive = true;
            ApplyAgeGates(45f);
            _ageText.text = "45";
            _cardText.text = "А я вообще туда иду?";
            SetYesLabel("ВСЁ\nНОРМАЛЬНО");
            SetNoLabel("О НЕТ");
            UseCrisisBlitzPlates();
            _crisisInfoText.text = "МЫСЛЬ 3/5\n<color=#e8686a>ПРОВАЛОВ: 1</color>";
            if (!_crisisInfo.activeSelf) _crisisInfo.SetActive(true);
            if (_impulseWarning.activeSelf) _impulseWarning.SetActive(false);
            if (punch) _yesRect.localScale = PunchScaleAt(0.5f);   // нижняя точка дуги (sin(π/2) = 1)
        }

        public void DebugPreviewStarBurst()
        {
            DebugPreviewArcadeShot();
            StarBurst();
            AdvanceStars(StarPreviewSeconds);
            enabled = false;
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
            PumpPendingScreens();   // тот же ПОРЯДОК, что в Update: отложенный экран — до живого тика
            _game.Tick(dt);
            TickAudioLatches(dt);   // …и те же ЛАТЧИ звука (купол/пульс/выгорание/провал блица):
                                    // без них события «без события в коде» были бы проверяемы только
                                    // настоящими кадрами по 1/60 с — то есть на практике никак.
            TickNewScale(dt);   // §D-модалка живёт тем же тактом, что и в Update
            if (_game.State != GameState.Playing) return;
            if (_game.InCrisis) ReflectDome(Mathf.Max(0f, _game.CrisisTimer), _game.CrisisTimerMax);
            else ReflectDome(Mathf.Max(0f, _game.CardTimer), _game.CardTimerMax);
            UpdateHudValues();      // живые шкалы на HUD — тот же путь, что у Update
            ReflectChildPhone(dt);  // …и §5b-трубка: выезд/уезд/качание на ТОМ ЖЕ dt, без Time.deltaTime
            ReflectAlarms(dt);      // …и §4/§6 поверх них
            SyncPhoneRingLoop();    // …и рингтон сводится с видимостью трубки, как в конце Update
        }

        /// <summary>Layer-2 seam: продвинуть §4-тревоги и §6-салют на dt для замороженного драйвера
        /// (позы/кадры), где Update не крутится. Живые шкалы перерисовываются тем же вызовом, что в
        /// Update, — иначе тревожная заливка строилась бы от УСТАРЕВШЕГО уровня.</summary>
        public void DebugPumpAlarms(float dt)
        {
            if (_game != null && _game.State == GameState.Playing) UpdateHudValues();
            ReflectAlarms(dt);
        }

        /// <summary>Layer-2 seam: отметить «ввод игрока по этой шкале был прямо сейчас» (окно §6).</summary>
        public void DebugNoteScaleInput(AlarmScale s) => NoteScaleInput(s);

        /// <summary>Layer-2 seam: сколько секунд прошло с последнего ЗАСЧИТАННОГО ввода по шкале (окно §6).
        /// Тест смотрит именно на него, чтобы отличить «нажал» от «механика приняла».</summary>
        public float SinceScaleInput(AlarmScale s) => _sinceScaleInput[(int)s];

        /// <summary>Layer-2 seam: продвинуть ДЕТЕРМИНИРОВАННЫЕ часы гейтов ввода (остался один — кэп
        /// дохода крутилки) — ровно тем же вызовом, что и Update, для замороженного драйвера.</summary>
        public void DebugAdvanceInputClocks(float dt) => _crankCap.Advance(dt);

        /// <summary>Layer-2 seam: продвинуть летящие звёзды на dt (проверка самоочистки).</summary>
        public void DebugAdvanceStars(float dt) => AdvanceStars(dt);

        // Set the two finale labels that go INTO the baked plate. The plate itself is part of `end.png`, so
        // nothing here resizes a plate any more — подбор кегля и есть то, что сажает некролог в поле.
        private void RenderFinaleTexts(NecrologResult n, int age)
        {
            _finaleOutcome.text = n.OutcomeBlock(age);
            _finaleStory.text = FitStoryPerLine(n.ComposeStory());
        }

        /// <summary>
        /// ОДНА ВЕХА — ОДНА СТРОКА НА ЭКРАНЕ. Подбирает кегль некролога и возвращает разметку для
        /// <see cref="_finaleStory"/>.
        ///
        /// ⚠ ЗАЧЕМ ЭТО ВМЕСТО ОБЫЧНОГО best-fit (дизайн-гейт 2026-08-08, MAJOR). uGUI-best-fit меряет
        /// БЛОК: он ужимает текст, только когда не сходится ВЫСОТА. Строка, которая не влезла в ширину
        /// поля, при этом спокойно переносится — и худший случай колоды давал сироту («жизнь.», 101 px)
        /// ПОСЕРЕДИНЕ блока. Это ломает и инвариант «одна веха = одна строка», и вертикальный ритм: между
        /// соседними вехами вдруг полторы межстрочных.
        ///
        /// Поэтому кегль подбирается ПОСТРОЧНО: блок берёт самый крупный кегль, при котором сходится
        /// высота, а КАЖДАЯ строка, которой этого кегля мало по ширине, ужимается персонально тегом
        /// `&lt;size&gt;` — ровно настолько, чтобы лечь в одну строку. Соседи своего размера не теряют
        /// (в этом вся разница с общим ужатием: одна длинная строка не мельчит весь некролог).
        /// Контейнер и поля не трогаются — сажаем ТЕКСТ, а не бокс.
        ///
        /// Пол ужатия общий с блоком (<see cref="FinaleStoryMinSize"/>): ниже порога читаемости строку не
        /// давим — если и там не влезло, перенос честнее нечитаемой строки.
        /// </summary>
        private string FitStoryPerLine(string story)
        {
            var t = _finaleStory;
            if (t == null || string.IsNullOrEmpty(story)) return story;

            float boxW = t.rectTransform.rect.width;
            float boxH = t.rectTransform.rect.height;
            // Рект ещё не разложен (или шрифт не поднялся) — молча отдаём текст как есть, без гадания.
            if (boxW <= 1f || boxH <= 1f || t.font == null) return story;

            var rows = story.Split('\n');
            string markup = story;
            for (int size = FinaleStoryMaxSize; size >= FinaleStoryMinSize; size--)
            {
                markup = ComposeFittedRows(t, rows, size, boxW);
                if (PreferredBlockHeight(t, markup, size, boxW) <= boxH)
                {
                    t.fontSize = size;
                    return markup;
                }
            }
            // Не сошлось даже на полу читаемости: берём пол (verticalOverflow=Truncate дорисует остальное).
            t.fontSize = FinaleStoryMinSize;
            return markup;
        }

        // Собрать блок при базовом кегле `size`: строки, влезающие по ширине, идут как есть; остальные —
        // в персональном `<size=k>`, где k — САМЫЙ КРУПНЫЙ кегль, при котором строка ложится в одну.
        private static string ComposeFittedRows(Text t, string[] rows, int size, float boxW)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                int k = LargestSizeFittingOneLine(t, rows[i], size, boxW);
                if (k >= size) sb.Append(rows[i]);
                else sb.Append("<size=").Append(k).Append('>').Append(rows[i]).Append("</size>");
            }
            return sb.ToString();
        }

        /// <summary>
        /// ПОЛЕ ПО БОКАМ, которое подборщик оставляет строке внутри ректа, в ЭКРАННЫХ ПИКСЕЛЯХ КАБИНЕТА.
        ///
        /// ⚠ ЗДЕСЬ БЫЛ КОЭФФИЦИЕНТ 0.93, И ОН ЛЕЧИЛ НЕ ТУ БОЛЕЗНЬ (дизайн-гейт 2026-08-08, MINOR).
        /// Ширина строки мерилась в масштабе ТЕКУЩЕГО канваса (делением на `Text.pixelsPerUnit`), а в
        /// батч-прогоне это 0.3849 — замер выходил примерно на 7 % УЖЕ правды, и живой кадр кабинета
        /// строку всё равно переносил. Семь процентов «запаса» ровно эту ошибку и компенсировали: не
        /// поле, а поправка на кривую линейку. Та же болезнь, что была у высоты блока
        /// (<see cref="PreferredBlockHeight"/>), и лечится тем же — мерить в сетке, в которой кадр
        /// РИСУЕТСЯ (`scaleFactor` = <see cref="CabinetCanvasScale"/>).
        ///
        /// Цена кривой линейки была видна на кадре: строки, которым замер обещал 31-й кегль, в кабинете
        /// не влезали и переносились, блок мерился в 12–14 рядов вместо восьми, и подборщик уезжал вниз
        /// до кегля 30 (интерлиньяж 36 против 39 у эталона), а длиннейшую строку давил до 28.
        ///
        /// С честным замером поле стало настоящим полем: 8 px с каждой стороны — только чтобы глиф не
        /// касался кромки ректа. Проверять обязательно КАРТИНКОЙ: ни один headless-замер сироту не видит.
        /// </summary>
        public const float FinaleStorySideMargin = 8f;

        /// <summary>Сколько ширины даётся ОДНОЙ строке некролога в ректе шириной <paramref name="boxW"/>.</summary>
        public static float FinaleStoryLineLimit(float boxW) => boxW - 2f * FinaleStorySideMargin;

        private static int LargestSizeFittingOneLine(Text t, string row, int from, float boxW)
        {
            if (string.IsNullOrEmpty(row)) return from;
            float limit = FinaleStoryLineLimit(boxW);
            for (int k = from; k > FinaleStoryMinSize; k--)
                if (PreferredRowWidth(t, row, k) <= limit) return k;
            return FinaleStoryMinSize;
        }

        /// <summary>
        /// Ширина ОДНОЙ строки без переноса (generationExtents = 0 + Overflow → натуральная ширина),
        /// В ЭКРАННЫХ ПИКСЕЛЯХ КАБИНЕТА — той же меркой, что и высота блока. Делить на `pixelsPerUnit`
        /// после фиксации `scaleFactor` НЕЛЬЗЯ: результат уже в экранных пикселях (см.
        /// <see cref="PreferredBlockHeight"/>, там эта же оговорка).
        /// </summary>
        public static float PreferredRowWidth(Text t, string row, int size)
        {
            var s = t.GetGenerationSettings(Vector2.zero);
            s.fontSize = size;
            s.resizeTextForBestFit = false;
            s.richText = true;
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            s.scaleFactor = CabinetCanvasScale;
            return t.cachedTextGeneratorForLayout.GetPreferredWidth(row, s);
        }

        /// <summary>
        /// МАСШТАБ КАНВАСА КАБИНЕТА. Экран автомата — ровно 1920×1080, т.е. `referenceResolution` скалера
        /// один в один, и `Canvas.scaleFactor` там равен 1. В нём — и только в нём — блок некролога и
        /// рисуется по-настоящему.
        /// </summary>
        public const float CabinetCanvasScale = 1f;

        /// <summary>
        /// Высота ВСЕГО блока (с уже проставленными `&lt;size&gt;`) в ширину поля, В ЭКРАННЫХ ПИКСЕЛЯХ
        /// КАБИНЕТА. Генератор честно учитывает персональные кегли строк.
        ///
        /// ⚠ МЕРИТЬ ОБЯЗАТЕЛЬНО В МАСШТАБЕ КАБИНЕТА, А НЕ В ТЕКУЩЕМ (регрессия 2026-08-08, поймана глазами
        /// по кадру: у полного некролога пропала СЕДЬМАЯ веха). `TextGenerator` растеризует шрифт в сетке
        /// `settings.scaleFactor` = <see cref="Text.pixelsPerUnit"/> = `Canvas.scaleFactor`, и межстрочье
        /// КВАНТУЕТСЯ в этой сетке. В батч-прогоне игровое окно не 16:9, скалер выдаёт scaleFactor 0.3849 —
        /// и те же восемь рядов кеглем 34 меряются как 311.8 px (влезают в поле 314!), тогда как в кабинете
        /// (scaleFactor 1) им нужно 320 px. Подборщик брал 34, а `verticalOverflow = Truncate` на живом
        /// кадре молча срезал последний ряд. Замер делением на `pixelsPerUnit` эту разницу НЕ ловит: делится
        /// уже проквантованное число.
        ///
        /// Поэтому здесь фиксируется `scaleFactor` = <see cref="CabinetCanvasScale"/>: замер перестаёт
        /// зависеть от того, в каком окне сейчас крутится сцена, и всегда отвечает про ту сетку, в которой
        /// кадр рисуется. Делить на `pixelsPerUnit` после этого НЕЛЬЗЯ — результат уже в экранных пикселях.
        /// (Ширина строк — <see cref="PreferredRowWidth"/> — с 2026-08-08 мерится ТАК ЖЕ, в сетке кабинета:
        /// раньше она оставалась в текущем масштабе, и «запас» 7 % компенсировал не поле, а ошибку замера.)
        /// </summary>
        private static float PreferredBlockHeight(Text t, string markup, int size, float boxW)
        {
            var s = t.GetGenerationSettings(new Vector2(boxW, 0f));
            s.fontSize = size;
            s.resizeTextForBestFit = false;
            s.richText = true;
            s.scaleFactor = CabinetCanvasScale;
            return t.cachedTextGeneratorForLayout.GetPreferredHeight(markup, s);
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
            BuildAudio();
            BuildHud();
            LoadGame();
        }

        /// <summary>
        /// Звуковой слой живёт на СОБСТВЕННОМ дочернем объекте: пул источников и луп-каналы строятся
        /// кодом (как и весь остальной проект), а драйвер держит одну ссылку и дёргает её из тех же
        /// мест, что рисуют картинку. Своих источников звука драйвер не держит — см. <see cref="AudioLayer"/>.
        /// </summary>
        private void BuildAudio()
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(transform, false);
            Audio = go.AddComponent<AudioLayer>();
        }

        private void Start()
        {
            Input ??= gameObject.AddComponent<ArcadeInputSource>();
            Input.Received += OnInput;
            SubscribeGame();
            // Тема-шарманка заводится на опенере и живёт всю сессию одним лупом (манифест §1: «опенер
            // и/или геймплей»). Фильтр депрессии накрывает и её — на то она и на фильтруемом канале.
            if (Audio != null) Audio.PlayLoop(SoundEvent.MusicTheme);
            Refresh();
        }

        private void OnDestroy()
        {
            if (Input != null)
            {
                Input.Received -= OnInput;
            }
            if (_game != null) UnsubscribeGame();
            DestroyBlitzPlateSprites();
        }

        /// <summary>
        /// Убрать за собой СГЕНЕРИРОВАННЫЕ плашки блица (находка код-скептика r4).
        /// <see cref="BuildBlitzPlateSprite"/> создаёт на драйвер ДВА <c>Texture2D</c> и ДВА <c>Sprite</c>
        /// — это нативные объекты, и сборщик мусора C# их не забирает: без явного Destroy они живут до
        /// выгрузки домена. В PlayMode-прогоне драйвер поднимается и рушится десятки раз за сессию, и
        /// каждый оставлял бы по паре текстур 615×280 RGBA32 (≈0.7 МБ на драйвер).
        ///
        /// Спрайт и его текстура уничтожаются ОТДЕЛЬНО: <c>Sprite.Create</c> текстуру не присваивает
        /// себе, уничтожение спрайта её не тронет. Текстуру берём ДО уничтожения спрайта — после него
        /// поле <c>sprite.texture</c> уже не спросить.
        ///
        /// ⚠ Пересоздания в рантайме НЕТ и быть не должно: обе плашки строятся ОДИН раз в Build, а панч
        /// (squash) двигает только <c>localScale</c> — растр он не перерисовывает. Иначе каждый кадр
        /// панча плодил бы по текстуре, и утечка была бы не «пара на драйвер», а «пара на кадр».
        /// </summary>
        private void DestroyBlitzPlateSprites()
        {
            foreach (var sp in new[] { _blitzYesSprite, _blitzNoSprite })
            {
                if (sp == null) continue;
                var tex = sp.texture;
                Destroy(sp);
                if (tex != null) Destroy(tex);
            }
            _blitzYesSprite = _blitzNoSprite = null;
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
            _game.DebtEntered += OnDebtEntered;
            _game.ChildOpened += OnChildOpened;
            _game.ChildCallMissed += OnChildCallMissed;
            _game.CrisisStarted += OnCrisisStarted;
            _game.CrisisBlitzAdvanced += OnCrisisBlitzAdvanced;
            _game.CrisisImpulseStarted += OnCrisisImpulseStarted;
            _game.DepressionStarted += OnDepressionStarted;
            _game.DepressionProgressed += OnDepressionProgressed;
            // ЗВУК: три семантических события существовали в Game, но драйверу до сих пор были не нужны —
            // картинке хватало покадрового чтения флагов. Звуку нужен ФРОНТ, поэтому подписываемся.
            _game.DepressionEnded += OnDepressionEnded;
            _game.CrisisEnded += OnCrisisEnded;
            _game.ChildBadParent += OnChildBadParent;
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
            _game.DebtEntered -= OnDebtEntered;
            _game.ChildOpened -= OnChildOpened;
            _game.ChildCallMissed -= OnChildCallMissed;
            _game.CrisisStarted -= OnCrisisStarted;
            _game.CrisisBlitzAdvanced -= OnCrisisBlitzAdvanced;
            _game.CrisisImpulseStarted -= OnCrisisImpulseStarted;
            _game.DepressionStarted -= OnDepressionStarted;
            _game.DepressionProgressed -= OnDepressionProgressed;
            _game.DepressionEnded -= OnDepressionEnded;
            _game.CrisisEnded -= OnCrisisEnded;
            _game.ChildBadParent -= OnChildBadParent;
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

            // …и ЖЁСТЧЕ — после зелёной, снявшей ВХОДНОЙ ЭКРАН спецрежима: там глушится и Confirm тоже.
            // Причина конкретная: экран депрессии закрывается зелёной, а сразу под ним CONFIRM — это ЛОВЛЯ
            // ПУЛЬСА. Аккорд «Enter + зелёная» в одном опросе иначе закрыл бы экран и тем же кадром
            // засчитал/испортил первую ловлю, которую игрок ещё не видел.
            if (_smClosedThisFrame) return;

            // (Раньше здесь глушился ввод на баннер-бите вехи — бит снят вместе с баннером 2026-08-05.)

            // §D — модальный экран новой шкалы. Стоит ДО ремапа ДА→CONFIRM: зелёный рычаг здесь обязан
            // остаться инертным (окно не закрывается кнопками-ответами, meeting-revisions §2). Живыми
            // проходят только контролы шкал — их разбирает NewScaleInput.
            // r3 — ВХОДНОЙ ЭКРАН СПЕЦРЕЖИМА. Стоит ПЕРВЫМ (даже раньше §D-модалки и раньше ремапа ДА→CONFIRM):
            // это самый верхний слой, и он закрывается ровно зелёной кнопкой. Все прочие вводы под ним
            // инертны — включая крутилку, которая на паузе иначе печатала бы деньги в замороженном мире.
            if (_smShowing) { SpecialModeInput(input); return; }

            if (_nsShowing) { NewScaleInput(input); return; }

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
                // §6-окно — ровно по ПРИНЯТОМУ тику: кэп дохода пропустил И Game.Crank его засчитал
                // (деньги открыты, не «глухая» пауза, не кризис/депрессия — там крутилка глушится).
                if (_game.HandleInput(GameInput.MoneyTick)) // Game sees only the semantic crank event
                {
                    NoteScaleInput(AlarmScale.Money);
                    // ЗВУК: щелчок динамо на ПРИНЯТЫЙ тик. До 5/с — поэтому тихий и с гулянием питча
                    // ±6 % (манифест), иначе очередь одинаковых щелчков превращается в дребезг.
                    if (Audio != null) Audio.Play(SoundEvent.CrankTick);
                    // ⚠ МОНЕТА — ТОЖЕ ТОЛЬКО НА ПРИНЯТЫЙ ТИК (находка Codex 2026-08-08, MAJOR). Она
                    // жила ниже, на одном лишь `MoneyOpen`, и потому звенела на ОТВЕРГНУТУЮ крутилку:
                    // в депрессии и в кризисе Game.HandleInput возвращает false (шкалы стоят, ввод
                    // глушится), а банка всё равно набирала ~5 монет в секунду — ложный доход на слух
                    // плюс забитый SFX-пул под самой тихой сценой игры. Та же accepted-семантика, что
                    // у панча плашек (r3) и у §6-окна: звучит РОВНО то, что механика засчитала.
                    if (Audio != null) Audio.Play(SoundEvent.CoinJar);
                }
                // ВИЗУАЛ монеты остаётся на прежнем условии (не звук — не эта находка): падающая монета
                // существует с r2 и её поведение под спецрежимами — отдельный вопрос к дизайну.
                if (_game.MoneyOpen && isActiveAndEnabled)  // coin drops into the jar on each PAYING tick
                {
                    if (_moneyPulse != null) StopCoroutine(_moneyPulse);
                    _moneyPulse = StartCoroutine(DropCoin());
                }
                return;
            }

            if (input == GameInput.EnergyHold)
            {
                // УДЕРЖИВАЕМЫЙ сигнал: источник переиздаёт его каждый кадр, пока датчик поднят, и драйвер
                // просто пропускает его в Game — там он латчится и превращается в один такт роста батареи.
                // Никакого гейта: с 2026-08-07 удержание и есть механика, отвергать нечего. Инертен вне
                // живого геймплея (на опенере/финале датчик ничего не делает).
                if (_game.State != GameState.Playing) return;
                // §6-окно открывает только ПРИНЯТОЕ удержание: датчик поднят И механика его засчитала.
                // Кризис/депрессия глушат датчик (шкалы там на паузе) — «держал во время депрессии» работой
                // по шкале не является, иначе рост энергии карточкой сразу после дал бы ложный салют.
                if (_game.HandleInput(GameInput.EnergyHold))
                {
                    NoteScaleInput(AlarmScale.Energy);      // игрок работает шкалой ПРЯМО СЕЙЧАС
                    NoteEnergyGesture();
                }
                return;
            }

            // Кнопка «!» (BangButton → CHILD_PRESS): поднять трубку. Идёт через ChildPress-хелпер, чтобы
            // УДАЧНОЕ поднятие отстрелило салют звёзд (revisions §5b/§6).
            if (input == GameInput.ChildPress)
            {
                // ⚠ В ДЕПРЕССИИ «!» — ЭТО ЛОВЛЯ ПУЛЬСА, а не трубка (решение основательницы 2026-08-08,
                // п.3г). Уходит в Game напрямую: PressChildPhone здесь врал бы фидбеком (он салютует за
                // ПОДНЯТЫЙ звонок, а окно звонка под депрессией стоит).
                if (_game.State == GameState.Playing && _game.InDepression)
                {
                    int grayBefore = _game.DepressionGray;
                    _game.HandleInput(GameInput.ChildPress);
                    // ПОПАДАНИЕ звучит не клипом, а САМИМ ФИЛЬТРОМ: ступень ваты снимается в
                    // OnDepressionProgressed. У ПРОМАХА/ЗАМКА свой ватный «пшик». Отличаем по серости:
                    // чистая ловля её уменьшает, промах и долбёж по локауту — нет.
                    if (Audio != null && _game.DepressionGray >= grayBefore)
                        Audio.Play(SoundEvent.DepressionMiss);
                    return;
                }
                PressChildPhone();
                return;
            }

            // Enter double-duty: during gameplay with the child scale open (and no tutorial up — that case
            // returned above), Enter/CONFIRM means «поднять трубку» → CHILD_PRESS. Everywhere else it stays
            // CONFIRM (start the game / restart from the finale / dismiss a hint), so the child mechanic
            // never steals those. Game itself only honours the press inside the open call window. NOT during
            // depression: там ловлю ведёт «!» (кнопка кабинета), а dev-Enter не ловит и трубку не поднимает —
            // окно звонка под депрессией всё равно заморожено.
            if (input == GameInput.Confirm
                && _game.State == GameState.Playing
                && _game.ChildOpen
                && !_game.InDepression)
            {
                PressChildPhone();
                return;
            }

            // §6-окно: отметить шкалу ТОЛЬКО если Game РЕАЛЬНО ПРИНЯЛ ввод (сюда доходят рычаг отношений и
            // ответ на карточку — остальные контролы разобраны своими ветками выше). Кризис и депрессия эти
            // же рычаги ГЛУШАТ или ПЕРЕНАЗНАЧАЮТ (блиц/импульс/ловля), и «нажал, но механика отвергла»
            // калибровкой не является: иначе окно §6 открывалось бы без работы игрока и рост шкалы
            // КАРТОЧКОЙ внутри окна выдавал бы ложный салют.
            // ПАНЧ ПЛАШКИ — ТОЛЬКО НА ПРИНЯТЫЙ ОТВЕТ (r3, п.4). До 2026-08-07 анимация жила своей веткой
            // (OnInputFx) и играла на КАЖДОЕ нажатие рычага: в депрессии, на входных экранах, под
            // подсказкой и на BLOCK$-блокировке плашка бодро дёргалась, хотя игра ввод глушила или
            // пропускала карточку без последствий. Игрок читал это как «нажалось», а ничего не
            // происходило. Теперь панч — ФУНКЦИЯ ПРИНЯТИЯ: та же accepted-семантика (r2), по которой
            // открывается §6-окно. Все не-геймплейные состояния сюда просто не доходят (вернулись выше),
            // а BLOCK$-блокировка доходит, но ответом не является — карточка пропускается без Δ.
            bool answer = input == GameInput.AnswerYes || input == GameInput.AnswerNo;
            // Спрашиваем ИГРУ, будет ли этот ответ пропуском: гашёная карточка ЛИБО цена, ставшая
            // неподъёмной, пока игрок думал (перепроверка платёжеспособности на ДА). Один источник истины
            // с Game.Answer — иначе поздний пропуск панчил бы плашку за покупку, которая не состоялась.
            bool blockedSkip = answer && _game.AnswerWouldSkipAsBlocked(input == GameInput.AnswerYes);
            // ЗВУК: в БЛИЦЕ рычаг значит не «да/нет», а «попал/не попал», и голос ему нужен по
            // РЕЗУЛЬТАТУ. Считаем ДО хода — HandleInput тут же уводит блиц на следующую мысль.
            bool inBlitz = answer && _game.InCrisis && _game.BlitzThoughtNumber > 0;
            bool blitzHit = inBlitz && (input == GameInput.AnswerYes) == _game.BlitzNormalOnYes;
            bool accepted = _game.HandleInput(input);
            // ⚠ БЛИЦ НЕ ВОЗВРАЩАЕТ «ПРИНЯТО» (найдено при разборе находок Codex 2026-08-08). Game
            // разбирает рычаг в собственной ветке кризиса (`BlitzPress`) и падает в общий `return
            // false` — то есть accepted-блок ниже в блице НЕ ВЫПОЛНЯЕТСЯ НИКОГДА, и голос попадания,
            // написанный внутри него, был мёртвым кодом: провал звучал (латч `Game.BlitzFails`), а
            // попадание молчало — ровно наоборот замыслу. Голос блица живёт СНАРУЖИ accepted-блока,
            // потому что «принято» здесь означает ход обычной карточки, а не удачное нажатие в блице.
            // §6-окно и панч плашки остаются внутри: это по-прежнему НЕ работа по шкале и НЕ ответ.
            if (inBlitz)
            {
                // ПАНЧ В БЛИЦЕ (r4 п.4): «кнопки блица не анимированы» — им нужен тот же отклик
                // «нажатие засчитано», что у ДА/СПАСИБО НЕ НАДО. Семантика r3 не ломается: панч
                // по-прежнему играет ТОЛЬКО на ПРИНЯТЫЙ ввод — просто «принято» в блице означает не
                // «ответ на карточку», а «рычаг засчитан как нажатие по мысли». `inBlitz` посчитан ДО
                // хода и уже требует живой мысли (BlitzThoughtNumber > 0), а BlitzPress принимает ОБА
                // рычага всегда — значит здесь принятие гарантировано.
                // Панчим ПРЕССОВАННУЮ плашку, а не «правильную»: отклик — про палец игрока, не про
                // попадание (попадание озвучивает голос строкой ниже, промах остаётся тихим).
                PunchAnswerPlate(input);
                if (blitzHit && Audio != null) Audio.Play(SoundEvent.AnswerYes);
                return;
            }
            if (accepted)
            {
                // ⚠ BLOCK$-ПРОПУСК — НЕ РАБОТА ПО ШКАЛЕ (находка ревью r3, MAJOR). Game.HandleInput
                // возвращает true и на ЗАБЛОКИРОВАННОЙ карточке — ход состоялся, карточка пропущена, — но
                // ВЫБОРА не было: ни Δ, ни некролога, ни записи ответа. А §6-окно здоровья открывает
                // именно выбор («лечиться можно только выбором», см. NoteScaleInput). Отметить его здесь
                // значило бы «недавно чинил здоровье» без единой попытки лечения: следующая карточка,
                // вытянувшая здоровье из тревоги, выдала бы САЛЮТ за чужую работу. Поэтому пропуск не
                // отмечает ввод и не панчит плашку — он не ответ. Accepted-семантика r2 доведена до конца.
                if (blockedSkip) return;
                NoteScaleInput(input);
                if (answer) PunchAnswerPlate(input);
                if (Audio != null && answer)
                {
                    // Калимба — ГОЛОС ИГРЫ: ДА = две ноты вверх, НЕТ = те же вниз. «НЕТ» не наказывает
                    // (правило отбора №1: отказ — полноценный выбор, баззеров тут не будет никогда).
                    // Блиц сюда не доходит (см. ранний возврат выше): там свой голос — попадание.
                    Audio.Play(input == GameInput.AnswerYes ? SoundEvent.AnswerYes : SoundEvent.AnswerNo);
                }
            }
        }

        /// <summary>Единственная точка, откуда играется панч плашки ответа (см. комментарий в OnInput).</summary>
        private void PunchAnswerPlate(GameInput input)
        {
            if (!isActiveAndEnabled) return;
            if (input == GameInput.AnswerYes) StartCoroutine(PunchPlate(_yesRect, YesTilt));
            else if (input == GameInput.AnswerNo) StartCoroutine(PunchPlate(_noRect, NoTilt));
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
            CloseNewScale(reward: false);   // §D: выход из игры прямо с модалки — тихо, без салюта
            _nsPending = NewScale.None;     // …и отложенный OPEN выход из жизни тоже снимает
            CloseSpecialMode();             // …ровно так же — входной экран спецрежима
            _smPending = SpecialMode.None;
            _bubbleTimer.Hide();
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
            _smClosedThisFrame = false;          // …и его строгий брат с входного экрана спецрежима
            SpinBackground(Time.deltaTime);      // ambient §7 ray spin — runs on every screen, pause included
            if (_game == null) return;
            _crankCap.Advance(Time.deltaTime);   // deterministic clock for the income cap
            PumpPendingScreens();                // ⚠ ДО тика — см. комментарий у самого метода
            _game.Tick(Time.deltaTime);
            TickAudioLatches(Time.deltaTime);    // звук: фронты, которых нет событиями (купол/пульс/выгорание)
            TickNewScale(Time.deltaTime);        // §D: условие выхода модалки → фейд → салют → снятие паузы
            if (_game.State == GameState.Playing && _game.InCrisis)
            {
                RenderCrisis();   // S6 blitz / S13 impulse — reuses the plates + dome timer, freezes normal HUD
            }
            else if (_game.State == GameState.Playing)
            {
                if (_crisisUiActive) RestoreNormalPlates();   // just resumed from a crisis → restore the plates
                _ageText.text = Mathf.FloorToInt(_game.Age).ToString();
                _moneyText.text = FormatMoneyJar(_game.Money);   // live: ticks up on crank, drains down
                // Live battery/bars move on their own (decay/drain/held sensor), not just on cards.
                var s = _game.Scales;
                ReflectEnergyLevel(s.Energy);
                ReflectHealthMarker(s.Health);
                ReflectRelationsMarker(s.Relationships, _game.RelationshipRedZone);
                // ⚠ ПЛАШКА И ТУТОРИАЛ ВЫГОРАНИЯ — ВЗАИМОИСКЛЮЧАЮЩИ (находка ревью r3, MAJOR). Плашка —
                // подача ПОВТОРНОГО выгорания (п.5б), а ПЕРВОЕ за жизнь объясняет входной экран с паузой.
                // Без гейта первый раз показывал ОБА разом: под затемнением экрана в полосе HUD висела ещё
                // и короткая плашка — второе, лишнее сообщение о том же самом. Ждущий очереди экран
                // (`_smPending`) считается так же: он поднимется этим же/следующим кадром.
                // …и ВТОРОЕ условие — ЗВОНОК (дизайн-скептик, раунд 2). Плашка переехала под батарею, про
                // которую она и говорит, а левая колонка ниже батареи — это дорожка выезжающей трубки
                // (`PhoneRingRect`: её рисунок идёт с y 330 и до x 350). Свободного коридора там ровно
                // 55 px — плашке с двумя строками в нём не встать. Разводим их ВРЕМЕНЕМ, а не пикселями:
                // пока трубка на экране, колонка принадлежит ЕЙ (звонок транзиентен и требует ответа),
                // плашка возвращается, как только трубка уехала. Инвариант «плашка никогда не заслоняет
                // трубку» держится буквально, а состояние всё это время читается красной §4-тревогой
                // батареи (в выгорании энергия ≤10 %, тревога горит по определению).
                bool burnPlate = _game.Burnout
                                 && !(_smShowing && _smWhich == SpecialMode.Burnout)
                                 && _smPending != SpecialMode.Burnout
                                 && _phoneOut <= 0.001f;
                if (_burnoutPlate.activeSelf != burnPlate) _burnoutPlate.SetActive(burnPlate);
                // S10: while the current card is BLOCK$-blocked, mute the two answer plates (the card veil
                // dims the marquee, this dims the plates) so the whole board reads «недоступно».
                var plateTint = _game.CurrentCardBlocked ? PlateMute : Color.white;
                if (_yesPlate.color != plateTint) _yesPlate.color = plateTint;
                if (_noPlate.color != plateTint) _noPlate.color = plateTint;
                ReflectChildPhone(Time.deltaTime);  // reveal on MD02=ДА; slide/wobble the handset on a call
                // Age-gated reveals run every frame (SetActive is a no-op on same value): a widget
                // opening MID-CARD (18/25/30 crossings) appears the moment its age is crossed instead
                // of waiting for the next card resolution (founder Gate-2 bug, uniform fix).
                ApplyAgeGates(_game.Age);

                // Купол-таймер: дуга-остаток убывает ровно за длину ТЕКУЩЕЙ фазы (§3), не за фикс. 5 с.
                // На паузе (туториал/баннер-бит) Game.Tick не двигает CardTimer → купол сам заморожен.
                ReflectDome(Mathf.Max(0f, _game.CardTimer), _game.CardTimerMax);
            }
            ReflectHostReveals(_game.State == GameState.Playing);   // advances the speech-bubble clock
            ReflectDomeUnderModal();                                // купол прячется под §D-модалкой
            ReflectKeyHints(Time.deltaTime);                        // клавиши эмуляции + отклик «не в ритм»
            ReflectBreakupPlate(_game.State == GameState.Playing);
            // §4/§6 — до вейлей: депрессия/выгорание/яркость рисуются выше и накрывают подсветку, как и просит
            // спек («вейлы поверх»).
            ReflectAlarms(Time.deltaTime);
            UpdateBrightness();
            ReflectDepression();   // B&W wash + grain + pulse while Game.InDepression (above the show veil)
            // ПОСЛЕДНИМ — когда видимость трубки этим кадром уже решена любой из веток выше.
            SyncPhoneRingLoop();
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
            // …и трубка ребёнка: кризис её убирает с экрана (ReflectChildPhone вернёт её сразу, как
            // только игра выйдет из кризиса и Game.ChildOpen снова будет живым).
            if (_childGroup != null) _childGroup.SetActive(false);

            // BLOCK$ visuals never apply during a crisis.
            SetCardBlockedDim(false);
            SetBlockBannerVisible(false);
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
                SetYesLabel(normalOnYes ? "ВСЁ\nНОРМАЛЬНО" : "О НЕТ");
                SetNoLabel(normalOnYes ? "О НЕТ" : "ВСЁ\nНОРМАЛЬНО");
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
                SetYesLabel("ДА");
                SetNoLabel("СПАСИБО,\nНЕ НАДО");
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
            SetYesLabel("ДА");
            SetNoLabel("СПАСИБО,\nНЕ НАДО");
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

        // ---- БЛИЦ В СТИЛЕ АРТ-ПАКА (r4 п.4) --------------------------------------------------------
        // Жалоба основательницы: «кнопки блица — в старом стиле и не анимированы». Обычная игра рисует
        // плашки АРТ-ПАКА (`btn-yes`/`btn-no`), а блиц переключался на плоские код-плашки
        // (`plate-yes`/`plate-no`) — тёмно-синий кант и ровная заливка, ни канта-кеглей, ни жёлтой
        // полосы. Рядом с арт-паком это читается как экран из другой игры.
        // ПЕРЕИСПОЛЬЗОВАТЬ `btn-yes`/`btn-no` НЕЛЬЗЯ: в них ВПИСАНЫ слова «ДА» и «СПАСИБО НЕ НАДО»
        // (буквы — часть битмапа), а блицу нужны свои «ВСЁ НОРМАЛЬНО» / «О НЕТ». Чистой заготовки в паке
        // нет — единственные «пустые» плашки и есть те самые старые `plate-yes`/`plate-no`.
        //
        // ⚠ ПЕРВАЯ РЕДАКЦИЯ (стопка 9-slice `bar-track`) ЗАВЁРНУТА ДИЗАЙН-ГЕЙТОМ 2026-08-08, и по делу:
        // 9-slice несёт радиус УГЛОВ ИСХОДНОГО СПРАЙТА в ИСХОДНЫХ ПИКСЕЛЯХ. У `bar-track` (360×56) это
        // ~16 px — на плашке высотой 280 получается радиус 5.7 % H против канонных 16–19 % у `btn-yes`,
        // то есть почти прямоугольник. По той же причине кант и жёлтая полоса выходили вдвое тяжелее
        // канона: их толщина задавалась в пикселях, а не долей высоты.
        // ЛЕЧИТСЯ НЕ ПОДБОРОМ ОТСТУПОВ, А ИСТОЧНИКОМ ФОРМЫ: плашка блица рисуется ОДНИМ спрайтом,
        // СГЕНЕРИРОВАННЫМ ПОД ЕЁ СОБСТВЕННЫЙ РАЗМЕР (<see cref="BuildBlitzPlateSprite"/>), где ВСЯ
        // геометрия — доли высоты. Тогда любой размер плашки даёт канон-пропорции сам собой, а не
        // «повезло с числом». Доли сняты с `btn-yes.png` (внутр. 875×577):
        //   • радиус угла      ≈ 0.17·H  (замер канона 16–19 % H);
        //   • чёрный кант      ≈ 0.024·H (замер 14 px / 577);
        //   • жёлтая полоса    от 0.059·H (замер: центр полосы на 7.2 % H от края) толщиной 0.025·H —
        //     ровно вдвое легче прежних 9-slice-бордюров, как и потребовал гейт (MINOR-3).
        // Тёмный ОДИН на всю плашку — <see cref="Ink"/>, им же красится кант букв (MINOR-4): раньше
        // контур подписи был #141A3D, а кант плашки #0B0F1A, и рядом это читалось как два разных чёрных.
        public const float BlitzPlateRadiusFrac = 0.17f;    // радиус угла / высота плашки
        public const float BlitzPlateKeylineFrac = 0.024f;  // толщина чёрного канта / высота
        public const float BlitzPlateStripeOutFrac = 0.059f;// внешний край жёлтой полосы / высота
        public const float BlitzPlateStripeFrac = 0.025f;   // толщина жёлтой полосы / высота
        private Sprite _blitzYesSprite, _blitzNoSprite;

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
            SetLabelShades(false);
        }

        /// <summary>Тень подписи — ОТДЕЛЬНЫЙ меш (см. <see cref="ApplyBlitzLabel"/>), поэтому её
        /// видимостью управляем вместе с самой подписью, а не через компонент-эффект.</summary>
        private void SetLabelShades(bool on)
        {
            if (_yesPlateShade != null && _yesPlateShade.gameObject.activeSelf != on)
                _yesPlateShade.gameObject.SetActive(on);
            if (_noPlateShade != null && _noPlateShade.gameObject.activeSelf != on)
                _noPlateShade.gameObject.SetActive(on);
        }

        /// <summary>Теневая копия подписи: тот же текст, тот же кант, цвет Ink — но ОТДЕЛЬНЫЙ меш,
        /// сдвинутый в <see cref="ApplyBlitzLabel"/>. Строится выключенной и НИЖЕ подписи по иерархии.</summary>
        private Text NewPlateShade(string name, Image plate, string content)
        {
            var t = NewText(name, plate.transform, content, 96, TextAnchor.MiddleCenter, Ink, _display);
            PlateTextRect(t.rectTransform);
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 40; t.resizeTextMaxSize = 180;
            var kant = t.gameObject.AddComponent<Outline>();   // тот же силуэт, что у канта подписи
            kant.effectColor = Ink;
            kant.effectDistance = new Vector2(BlitzLabelKantPx, -BlitzLabelKantPx);
            t.gameObject.SetActive(false);
            return t;
        }

        // Подпись плашки и её теневая копия ОБЯЗАНЫ нести один текст — иначе тень отстанет на строку.
        private void SetYesLabel(string s) { _yesPlateText.text = s; if (_yesPlateShade != null) _yesPlateShade.text = s; }
        private void SetNoLabel(string s) { _noPlateText.text = s; if (_noPlateShade != null) _noPlateShade.text = s; }

        /// <summary>
        /// Нарисовать плашку блица ЦЕЛИКОМ в текстуру ровно того размера, которым она выйдет на экран:
        /// кант → цветное поле → жёлтая полоса → цветное поле, углы скруглены по канону (см. доли
        /// <see cref="BlitzPlateRadiusFrac"/> и соседей). Спрайт рисуется Simple и 1:1 по пикселям
        /// (pixelsPerUnit 100 = referencePixelsPerUnit холста), поэтому доли высоты доезжают до кадра
        /// НЕИСКАЖЁННЫМИ — в отличие от 9-slice, который тащит радиус исходника в исходных пикселях.
        /// Края сглажены: и внешний контур, и швы колец размываются ровно на один пиксель, так что
        /// диагональ угла не лесенкой (тот же приём, что у купола-таймера).
        /// </summary>
        private static UnityEngine.Sprite BuildBlitzPlateSprite(string name, int w, int h, Color body)
        {
            float r = BlitzPlateRadiusFrac * h;
            float kant = BlitzPlateKeylineFrac * h;
            float stripeOut = BlitzPlateStripeOutFrac * h;
            float stripeIn = stripeOut + BlitzPlateStripeFrac * h;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name + "Tex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[w * h];
            float hw = w * 0.5f, hh = h * 0.5f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Signed distance до скруглённого прямоугольника: внутри отрицательна, значит
                    // `depth` = «на сколько пикселей точка ЗАШЛА внутрь от внешнего контура».
                    float qx = Mathf.Abs(x + 0.5f - hw) - (hw - r);
                    float qy = Mathf.Abs(y + 0.5f - hh) - (hh - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)
                                             + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float depth = r - (Mathf.Min(Mathf.Max(qx, qy), 0f) + outside);

                    // Кольца: последовательные лерпы с окном в один пиксель — кант, поле, полоса, поле.
                    var c = Ink;
                    c = Color.Lerp(c, body, Mathf.Clamp01(depth - kant + 0.5f));
                    c = Color.Lerp(c, DomeYellow, Mathf.Clamp01(depth - stripeOut + 0.5f));
                    c = Color.Lerp(c, body, Mathf.Clamp01(depth - stripeIn + 0.5f));
                    c.a = Mathf.Clamp01(depth + 0.5f);
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            // ⚠ ПОЛНОЕ ИМЯ ТИПА: у драйвера есть СВОЙ метод Sprite(string) (загрузка из Resources), и
            // короткое `Sprite.Create` разрешается в него — компилятор берёт метод, а не тип.
            var sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            return sprite;
        }

        // Crisis: blank code-plates + the dynamic label back on (the caller sets the text/colours).
        private void UseCodePlates(Vector2 yesSize, Vector2 noSize)
        {
            if (_yesPlate.sprite != _codeYesSprite) _yesPlate.sprite = _codeYesSprite;
            if (_noPlate.sprite != _codeNoSprite) _noPlate.sprite = _codeNoSprite;
            _yesPlate.color = Color.white;
            _noPlate.color = Color.white;
            _yesPlate.type = Image.Type.Sliced;
            _noPlate.type = Image.Type.Sliced;
            _yesRect.sizeDelta = yesSize;
            _noRect.sizeDelta = noSize;
            if (!_yesPlateText.gameObject.activeSelf) _yesPlateText.gameObject.SetActive(true);
            if (!_noPlateText.gameObject.activeSelf) _noPlateText.gameObject.SetActive(true);
        }

        // Enlarge both blitz plates equally (S6) and push the text rect inside the (now taller) colored pill.
        // Плашка блица — ОДИН сгенерированный спрайт в канон-пропорциях (см. BuildBlitzPlateSprite).
        // Размер прямоугольника прежний (615×280), поэтому композиция S6 и гард ширины плашек не трогаются.
        private void UseCrisisBlitzPlates()
        {
            UseCodePlates(CrisisBlitzPlateSize, CrisisBlitzPlateSize);
            _yesPlate.sprite = _blitzYesSprite;
            _noPlate.sprite = _blitzNoSprite;
            _yesPlate.type = Image.Type.Simple;   // спрайт нарисован ПОД этот размер — растягивать нечего
            _noPlate.type = Image.Type.Simple;
            _yesPlate.color = Color.white;        // цвет уже в текстуре; тинт только исказил бы токены
            _noPlate.color = Color.white;
            ApplyBlitzLabel(_yesPlateText, _yesPlateShade, _yesLabelKant, _yesLabelSoft);
            ApplyBlitzLabel(_noPlateText, _noPlateShade, _noLabelKant, _noLabelSoft);
            SetLabelShades(true);
        }

        // S13 impulse plates: the code-plates at the sizes/insets the accepted 03 shot uses.
        // ⚠ ИМПУЛЬС НАМЕРЕННО НЕ ПЕРЕОДЕВАЕТСЯ: его вид зафиксирован принятым кадром «03», поэтому здесь
        // восстанавливается ИСХОДНАЯ типографика плашки (Arimo Bold, мягкая тень DisplayFx, кегль 1:1) —
        // всё, что блиц у себя поменял, откатывается явно, иначе режимы утекали бы друг в друга.
        private void UseImpulsePlates()
        {
            UseCodePlates(ImpulseYesPlateSize, ImpulseNoPlateSize);
            RestoreDisplayLabel(_yesPlateText, _yesPlateShade, _yesLabelKant, _yesLabelSoft);
            RestoreDisplayLabel(_noPlateText, _noPlateShade, _noLabelKant, _noLabelSoft);
            PlateTextRect(_yesPlateText.rectTransform);
            PlateTextRect(_noPlateText.rectTransform);
            _yesPlateText.resizeTextMinSize = 40; _yesPlateText.resizeTextMaxSize = 120;
            _noPlateText.resizeTextMinSize = 24; _noPlateText.resizeTextMaxSize = 60;
            SetLabelShades(false);
        }

        // ---- ТИПОГРАФИКА ПОДПИСИ БЛИЦА (r4 п.4, переделка по дизайн-гейту MAJOR-2) ------------------
        // Замер гейта: кэп-хайт подписи был 18 % высоты плашки против 63 % у канонного «ДА», лицо —
        // тонкий гротеск, тень — полупрозрачная (альфа 0.32), то есть РАЗМЫТАЯ рядом с жёсткой тенью пака.
        // Три отдельные причины, три отдельные правки:
        //
        // (1) РАЗМЕР. Прежний best-fit был зажат в 28…48 pt внутри прямоугольника, отодвинутого на 74 px
        //     от края (он обходил фиксированный 9-slice-угол код-плашки). Угла больше нет — видимое поле
        //     начинается сразу за кантом, — поэтому прямоугольник подписи отодвинут всего на 0.107·H,
        //     а потолок best-fit поднят до 180 pt.
        // (2) ШИРИНА ЛИЦА. Канон — УЗКИЕ дудл-капсы: у `btn-no` самая длинная строка занимает 1145 px при
        //     кэп-хайте 197, то есть ≈0.73 ширины на знак-кэп; у Arimo Bold и Rubik-Bold это ≈1.03.
        //     Без сжатия «НОРМАЛЬНО» упирается в ширину плашки на кэп-хайте ≈60 px и выше не растёт —
        //     ограничение не высотой, а ШИРИНОЙ. Поэтому прямоугольник подписи делается шире плашки и
        //     сжимается по X до <see cref="BlitzLabelCondense"/>: ровно та же узость, что у канона, а
        //     best-fit получает право взять кегль вдвое крупнее.
        // (3) ЛИЦО И ТЕНЬ. Rubik-Bold вместо Arimo Bold: замер по растру — толщина штриха 0.257 кэп-хайта
        //     против 0.207 у Arimo Bold (+24 %), заливка знака 0.545 против 0.465. Это самое жирное лицо
        //     из четырёх в проекте, у которого есть кириллица (RussoOne 0.250 — легче и он служебный
        //     фолбэк). Тень — ЖЁСТКАЯ: отдельная КОПИЯ текста цветом Ink со сдвигом, а не полупрозрачный
        //     компонент Shadow. Отдельным мешом — потому что Outline+Shadow на ОДНОМ меше дают «призрак»
        //     (r3 §4(s)); здесь на каждом меше висит только Outline, и оба канта одного тёмного (Ink).
        // ⚠ ПУНКТ (2) ВЫШЕ ЗАВЁРНУТ ВТОРЫМ ДИЗАЙН-ГЕЙТОМ — см. блок ниже. Он оставлен как есть, потому
        // что объясняет, ОТКУДА взялось сжатие: рассуждение про узость канона верное, ошибочен способ.
        // ---- ПЕРЕДЕЛКА ПО ВТОРОМУ ДИЗАЙН-ГЕЙТУ (2026-09-20): КОНДЕНС СНЯТ, ВОЗДУХ ВЕРНУЛИ ---------
        // Гейт завернул РЕДАКЦИЮ ВЫШЕ двумя замерами, и оба — следствие одной ошибки: кегль гнали вверх
        // за счёт всего остального.
        //   BLOCKER: «НОРМАЛЬНО» въезжала в жёлтый кант — минимум ink→рант 2.0 px (0.72 % H) при
        //            канонном поле 6.8–9.2 % H. Отступ 0.10·H давал рамку 28 px, а внутренняя кромка
        //            ранта стоит на (0.059+0.025)·H = 23.5 px: «воздуха» оставалось 4.5 px, и его
        //            дочиста съедали кант буквы (5 px) и сдвиг тени (4, −9).
        //   MAJOR:   сжатие 0.66 делало ГЛИФЫ ЧУЖИМИ — w/h 0.56 против канонных 0.82–0.89, и штрих
        //            утончался (17.2 % против 18.3–26.5 %): сжатие по X режет ВЕРТИКАЛЬНЫЕ штоки, то
        //            есть ровно ту толщину, ради которой брался Rubik-Bold. Дудл-капсы пака узкие ПО
        //            РИСУНКУ, а не по масштабу, и подделать это аффинным сжатием нельзя.
        //
        // ЧТО ВЫБРАНО И ЧЕМ ЗАПЛАЧЕНО (развилка честная, поэтому записана целиком):
        //   • `BlitzLabelCondense` 0.66 → 1.0 — глифы своей формы, w/h возвращается к ≈0.85 (0.56/0.66),
        //     то есть в канонный коридор 0.82–0.89, штрих снова 0.257 кэп-хайта.
        //   • отступ считается НЕ «долей от края», а ОТ ВНУТРЕННЕЙ КРОМКИ ЖЁЛТОЙ + канонный воздух +
        //     ВЫЛЕТ ЭФФЕКТОВ. Последнее слагаемое и было пропущено: прямоугольник best-fit ограничивает
        //     АДВАНСЫ глифов, а на экран выходит ink = адвансы + кант с каждой стороны + сдвиг тени.
        //     Отсюда 0.084·H (рант) + 0.075·H (воздух) + 9 px (кант 5 + тень 4) = 53.5 px = 0.191·H.
        //   • ПЛАТА — КЭГЛЬ: «НОРМАЛЬНО» это ДЕВЯТЬ знаков широкого лица (6.565 кегля), и при канонном
        //     воздухе ширина плашки оставляет им кегль ≈78 ⇒ кэп-хайт ≈20 % H вместо прежних 31 %.
        //     Прежние 31 % были ЗАНЯТЫ у ранта и у формы глифа — то есть их не было. Белое ядро штриха
        //     при этом остаётся ниже канонных 9.7–11.6 % H (≈5 % H), и это ЧЕСТНЫЙ ПРЕДЕЛ ГЕОМЕТРИИ:
        //     канонные числа сняты с восьми знаков УЗКОГО рисованного лица, наше слово длиннее и шире.
        //     Уйти от него можно только сменой слова или рисованным лицом — не моя развилка.
        //   • ВЕРТИКАЛЬНЫЙ отступ меньше горизонтального и считается от КАНТА: связывать подпись обязана
        //     ШИРИНА (иначе «О НЕТ» в одну строку упёрлась бы в высоту и разъехалась с зелёной по
        //     заполнению — это и был MINOR «плашки оптически несогласованы»).
        //
        // АРИФМЕТИКА (метрики Rubik-Bold сняты с растра: кэп-хайт 0.720 кегля, «НОРМАЛЬНО» 6.565 кегля,
        // «О НЕТ» 3.24 кегля, lineHeight 1.24; плашка 615×280, рант изнутри на 23.5 px):
        //   отступ X = 23.5 + 0.075·280 + 9 = 53.5 px ⇒ прямоугольник 508 px ⇒ ink = 508 + 2·5 + 4 = 522;
        //   воздух ink→рант = (568 − 522)/2 ≈ 21–25 px = 7.5–8.9 % H — В КАНОНЕ (6.8–9.2 %);
        //   заполнение интерьера (внутри чёрного канта, 601.6 px) = 522/601.6 ≈ 87 % — в коридоре 78–90 %,
        //     и ОДНО НА ОБЕ ПЛАШКИ, потому что обе связаны одной и той же шириной прямоугольника;
        //   зелёная: кегль 508/6.565 ≈ 77 ⇒ кэп-хайт ≈ 56 px ≈ 20 % H, две строки занимают
        //     2·77·1.24·0.76 ≈ 145 px ≤ 225 (высота прямоугольника) — высота НЕ связывает;
        //   красная: кегль 508/3.24 ≈ 157 ⇒ кэп-хайт ≈ 113 px ≈ 40 % H, строка 157·1.24 ≈ 195 ≤ 225 —
        //     тоже связана ШИРИНОЙ, поэтому и садится в то же заполнение. Красная крупнее зелёной по
        //     кэп-хайту (слово короче), но оптически они согласованы: одинаково заполняют интерьер.
        private const float BlitzLabelCondense = 1.0f;    // 1.0 = глиф своей формы; 0.66 давало w/h 0.56
        private const float BlitzLabelAirFrac = 0.075f;   // воздух ink→рант / высота (канон 6.8–9.2 %)
        private const float BlitzLabelLineSpacing = 0.76f;// канон `btn-no`: шаг строк 0.39·H при кэпе 0.30·H
        private const float BlitzLabelKantPx = 5f;        // жёсткий кант буквы
        private static readonly Vector2 BlitzLabelShadeOffset = new Vector2(4f, -9f);

        /// <summary>Вылет ЭФФЕКТОВ за прямоугольник best-fit: кант буквы наружу плюс сдвиг теневой копии.
        /// Прямоугольник держит адвансы глифов, а рант видит ink — разницу обязан оплатить отступ.</summary>
        private static float BlitzLabelInkBleed => BlitzLabelKantPx + Mathf.Abs(BlitzLabelShadeOffset.x);

        /// <summary>Горизонтальный отступ подписи: внутренняя кромка жёлтой полосы + канонный воздух +
        /// вылет эффектов. Именно он СВЯЗЫВАЕТ best-fit — и потому задаёт обеим плашкам одно заполнение.</summary>
        private static float BlitzLabelInsetX =>
            (BlitzPlateStripeOutFrac + BlitzPlateStripeFrac + BlitzLabelAirFrac) * CrisisBlitzPlateSize.y
            + BlitzLabelInkBleed;

        /// <summary>Вертикальный отступ: кант + тот же канонный воздух + вылет эффектов по вертикали
        /// (кант буквы вверх, кант + сдвиг тени вниз). МЕНЬШЕ горизонтального намеренно: высота обязана
        /// оставлять место обеим подписям, но НЕ давать третьей строке — см. <see cref="ApplyBlitzLabel"/>.</summary>
        private static float BlitzLabelInsetY =>
            (BlitzPlateKeylineFrac + BlitzLabelAirFrac) * CrisisBlitzPlateSize.y
            + BlitzLabelKantPx + Mathf.Abs(BlitzLabelShadeOffset.y);

        /// <summary>
        /// ⚠ ГЛАВНАЯ НАХОДКА ВТОРОГО ГЕЙТА, БЕЗ КОТОРОЙ ЧИСЛА НЕ СХОДИЛИСЬ: <c>best-fit</c> у uGUI
        /// подбирает кегль ТОЛЬКО ПО ШИРИНЕ, пока <c>verticalOverflow == Overflow</c> — переполнение по
        /// высоте разрешено, значит «влезает» истинно всегда, и подбор упирается в потолок кегля.
        ///
        /// Это и есть корень MINOR «плашки оптически несогласованы». «ВСЁ НОРМАЛЬНО» — длинное слово,
        /// его связывала ширина (кегль 129, заполнение 95 %); «О НЕТ» — короткое, ширина его не связывала
        /// НИКОГДА, и он просто брал максимум 180 pt (заполнение 62 %). Две подписи жили по РАЗНЫМ
        /// законам, поэтому и выглядели из разных наборов.
        ///
        /// Со снятым сжатием прямоугольник стал уже, и та же дыра дала уже не косметику, а поломку:
        /// «О НЕТ» ПЕРЕНОСИЛСЯ по пробелу на две строки и на 180 pt вылезал за плашку (замер пробой:
        /// кегль 180, строк 2, чернила 349×292 при прямоугольнике 508×225).
        ///
        /// Поэтому в блице <c>verticalOverflow = Truncate</c>: подбор обязан считаться и с высотой.
        /// Тогда обе подписи связаны ОДНИМ ограничением — прямоугольником — и садятся в одно заполнение.
        /// Высота прямоугольника подобрана так, чтобы у длинного слова не появилась ТРЕТЬЯ строка:
        /// 2 строки кегля 77 занимают ≈145 px, три строки кегля 78 — уже ≈220 px, и 197 px их не пускает.
        /// </summary>
        private void ApplyBlitzLabel(Text t, Text shade, Outline kant, Shadow soft)
        {
            var size = new Vector2((CrisisBlitzPlateSize.x - 2f * BlitzLabelInsetX) / BlitzLabelCondense,
                                    CrisisBlitzPlateSize.y - 2f * BlitzLabelInsetY);
            foreach (var g in new[] { shade, t })
            {
                var rt = g.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
                rt.localScale = new Vector3(BlitzLabelCondense, 1f, 1f);
                rt.anchoredPosition = g == shade ? BlitzLabelShadeOffset : Vector2.zero;
                g.font = _bodyBold;
                g.lineSpacing = BlitzLabelLineSpacing;
                g.resizeTextForBestFit = true;
                g.resizeTextMinSize = 40;
                g.resizeTextMaxSize = 180;
                g.verticalOverflow = VerticalWrapMode.Truncate;   // …иначе подбор идёт ТОЛЬКО по ширине
            }
            t.color = Color.white;
            shade.color = Ink;
            kant.effectColor = Ink;                        // MINOR-4: кант буквы и кант плашки — ОДИН тёмный
            kant.effectDistance = new Vector2(BlitzLabelKantPx, -BlitzLabelKantPx);
            soft.enabled = false;                          // мягкой полупрозрачной тени в блице нет
        }

        private void RestoreDisplayLabel(Text t, Text shade, Outline kant, Shadow soft)
        {
            var rt = t.rectTransform;
            rt.localScale = Vector3.one;
            t.font = _display;
            t.lineSpacing = 1f;
            t.color = Color.white;
            kant.effectColor = DisplayKantInk;
            kant.effectDistance = new Vector2(3f, -3f);
            soft.enabled = true;
            shade.rectTransform.localScale = Vector3.one;
            // …и режим подбора тоже откатывается: блиц ставит Truncate (см. ApplyBlitzLabel), а импульс
            // живёт на принятом кадре «03», где подпись подбиралась по ширине. Режимы не должны утекать.
            t.verticalOverflow = VerticalWrapMode.Overflow;
            shade.verticalOverflow = VerticalWrapMode.Overflow;
        }

        // The game is paused while a tutorial overlay OR the §D modal is up. Both share the single
        // Game.Paused freeze (age, drains, cost-of-living, card timer, crisis timer). Kept in sync from
        // one place so no overlay can leave the pause flag stale. (Баннер-бит вехи снят 2026-08-05.)
        private void SyncPause()
        {
            if (_game == null) return;
            // Входной экран спецрежима (r3) добавлен в ту же ОДНУ заморозку: под ним стоит всё, включая
            // кризисный таймер блица, планировщик пульса депрессии и дренаж энергии выгорания.
            _game.Paused = _tutorialShowing || _nsShowing || _smShowing;
            // §D: под модальным экраном новой шкалы ВРЕМЯ стоит так же, как под подсказкой (дренажи, возраст,
            // таймер карточки), но КОНТРОЛЫ ШКАЛ живые — иначе условие выхода недостижимо. Если поверх
            // модалки оказалась S5-подсказка (она глушит ввод целиком), приоритет у неё.
            _game.PausedInputsLive = _nsShowing && !_tutorialShowing && !_smShowing;
        }

        /// <summary>
        /// Купол-таймер прячется под §D-модалкой (долг гейта 2026-08-05): время под ней заморожено,
        /// поэтому замерший полукруг торчал над затемнением «культёй» — читался как недорисованный
        /// элемент. Скрываем целиком; возвращается сам, как только модалка ушла. Идемпотентно.
        ///
        /// Это ВСЁ, что осталось от прежнего <c>ReflectBannerBeat</c>: карточка, плашки ответов и ряд HUD
        /// пряталась только ради баннер-бита вехи, а он снят целиком (плейтест основательницы 2026-08-05),
        /// поэтому теперь их никто не скрывает — веха идёт обычной карточкой.
        /// </summary>
        private void ReflectDomeUnderModal()
        {
            if (_cardRoot == null) return;
            // …и то же самое под входным экраном спецрежима: под ним время тоже стоит.
            bool dome = !_nsShowing && !_smShowing;
            if (_timerGroup != null && _timerGroup.activeSelf != dome) _timerGroup.SetActive(dome);
        }

        /// <summary>
        /// Вторая строка окон-подсказок. Строка КЛАВИШИ показывается ТОЛЬКО пока контрол реально
        /// эмулируется клавиатурой (<see cref="ArcadeInput.KeyHint"/> — маппинг читается ИЗ КОНФИГА
        /// ПАКЕТА, в игре нет ни одного зашитого имени клавиши); стоит воткнуть плату — и та же проверка
        /// вернёт пустую строку, подсказка исчезнет сама, без перезапуска. Считается каждый кадр именно
        /// ради этого «воткнул/выдернул».
        ///
        /// (Отклик «не в ритм» и вздрагивание батареи, жившие здесь до 2026-08-07, снялись вместе с
        /// ритм-гейтом: отвергать нечего — либо датчик держат и батарея растёт, либо нет.)
        /// </summary>
        private void ReflectKeyHints(float dt)
        {
            if (_nsHintLine != null)
            {
                // ЗАКРЫТАЯ модалка не считает вообще ничего: её строка пуста по определению, и подмешивать
                // сюда последнюю шкалу (а тем более собирать строку) — работа в пустоту каждый кадр.
                // Одна строка на два окна одного оверлея: §D-модалка называет контрол СВОЕЙ шкалы,
                // входной экран спецрежима — контрол СВОЕГО режима (у здоровья и блица его нет — там
                // строка пуста по определению).
                string line =
                    _nsShowing ? KeyHintLine(ControlOf(_nsWhich), ref _nsHintCache)
                    : _smShowing ? KeyHintLine(ControlOf(_smWhich), ref _nsHintCache)
                    : "";
                if (_nsHintLine.text != line) _nsHintLine.text = line;
                if (_nsHintLine.color != HintInk) _nsHintLine.color = HintInk;
            }

            if (_tutHintLine != null)
            {
                string line = _tutorialShowing ? KeyHintLine(_tutHintControl, ref _tutHintCache) : "";
                if (_tutHintLine.text != line) _tutHintLine.text = line;
            }

            // r3 (п.3в): НА САМОМ экране депрессии тоже написано, ЧЕМ играть — и клавиша при эмуляции.
            // До сих пор там висело безадресное «нажми в такт пульсу»: игрок видел ритм, но не знал, чем
            // по нему бить. Контрол — из той же константы DepressionCatchControlName, что и текст входного
            // экрана (п.3г: смена контрола = правка одной строки).
            if (_depKeyHint != null)
            {
                bool dep = _game != null && _game.State == GameState.Playing && _game.InDepression;
                string line = dep ? KeyHintLine(ArcadeControlId.BangButton, ref _depHintCache) : "";
                if (_depKeyHint.text != line) _depKeyHint.text = line;
                if (_depKeyHint.gameObject.activeSelf != (dep && line.Length > 0))
                    _depKeyHint.gameObject.SetActive(dep && line.Length > 0);
            }
        }

        /// <summary>Какой контрол автомата объясняет §D-экран этой шкалы (для подсказки клавиши).</summary>
        private static ArcadeControlId? ControlOf(NewScale s) => s switch
        {
            NewScale.Energy => ArcadeControlId.HeightA,     // датчик высоты — «зажми и держи»
            NewScale.Relations => ArcadeControlId.Joystick, // балансир отношений
            NewScale.Money => ArcadeControlId.Crank,        // крутилка денег
            NewScale.Child => ArcadeControlId.BangButton,   // «поднять трубку»
            _ => null,
        };

        /// <summary>
        /// Готовая вторая строка, или пусто — когда контрол ведёт настоящая плата (стойка).
        ///
        /// Строка ЖИВЁТ В КЭШЕ, а не собирается заново каждый кадр: сама сборка (склейка приставки,
        /// клавиши и хвоста + StringBuilder внутри KeyboardHints) — это мусор на каждом кадре открытого
        /// окна, а окна §D висят десятками секунд. Пересборка происходит РОВНО тогда, когда меняется
        /// что-то, от чего строка зависит: контрол окна, ответ «эмулируется ли он сейчас» (воткнули или
        /// выдернули плату — ради этого и проверяем каждый кадр) или сама таблица клавиш. В устоявшемся
        /// кадре возвращается ТА ЖЕ ссылка (гард: KeyHintLine_IsNotRebuiltEveryFrame).
        /// </summary>
        private static string KeyHintLine(ArcadeControlId? control, ref HintCache cache)
        {
            if (control == null) return "";
            ArcadeControlId id = control.Value;

            // Дёшево и без аллокаций: два вызова-предиката по уже готовому бэкенду.
            IKeyboardEmulation emu = ArcadeInput.KeyboardEmulation;
            bool emulated = emu != null && emu.IsEmulated(id);
            KeyboardMapping map = emulated ? emu.Mapping : null;

            if (cache.Valid && cache.Control == id && cache.Emulated == emulated
                && ReferenceEquals(cache.Mapping, map))
                return cache.Line;

            string line = "";
            if (emulated)
            {
                string key = KeyboardHints.PrimaryFor(map, id);
                if (!string.IsNullOrEmpty(key))
                    line = (id == ArcadeControlId.HeightA ? BreathKeyHintPrefix : KeyHintPrefix) + key;
            }

            cache.Valid = true;
            cache.Control = id;
            cache.Emulated = emulated;
            cache.Mapping = map;
            cache.Line = line;
            return line;
        }

        /// <summary>
        /// Test seam: advance the driver-side host speech-bubble clock by an injected
        /// <paramref name="dt"/> and reconcile visibility exactly as Update would, so a synchronous
        /// Game.Tick-driven test can age a bubble out deterministically without pumping real frames.
        /// Production drives it off Time.deltaTime in Update.
        /// </summary>
        public void DebugPumpHost(float dt)
        {
            if (_game == null) return;
            _bubbleTimer.Advance(dt);
            SyncPause();
            bool playing = _game.State == GameState.Playing;
            bool bub = playing && _bubbleTimer.Visible;
            if (_hostBubble != null && _hostBubble.activeSelf != bub) _hostBubble.SetActive(bub);
            ReflectDomeUnderModal();
        }

        // Crisis entered (CR00): the Ведущий ANNOUNCES the blitz in his speech bubble. It used to be a
        // blocking gold rubric band; the band (and its beat) went with the milestone banners on 2026-08-05,
        // and the bubble is the voice that stayed.
        // r3: кризис больше не стартует «без объяснений» — объявление уходит на ВХОДНОЙ ЭКРАН блица
        // (рассказ = то же самое объявление, задача = правила блица), и кризисный таймер стоит, пока экран
        // висит (Game.Paused обрывает Tick до TickCrisis). Облачко при этом тоже показываем: экран уйдёт по
        // зелёной, и голос Ведущего останется на первой мысли.
        private void OnCrisisStarted()
        {
            if (Audio != null)
            {
                Audio.Play(SoundEvent.BlitzStart);   // драматическая перебивка «КРИЗИС! БЛИЦ!»
                _audioBlitzFails = 0;                // счётчик провалов начинается заново
            }
            _bubbleTimer.Show(HostContent.CrisisAnnounce);
            ShowSpecialMode(SpecialMode.Blitz);
        }

        // Each new blitz thought: shout a hurrying host-nag line in the speech bubble (S3).
        // ПЕРВАЯ мысль — исключение: на ней в облачке ещё висит объявление входа в кризис
        // (OnCrisisStarted, тем же кадром), и нахлобучить сверху «Быстрее!» значило бы съесть
        // объявление целиком — ровно это и случилось, когда рубрика-плашка со своим битом ушла
        // (2026-08-05). Объявление громче и играет ту же роль подгонялки, поэтому нагоняй №1 пропускаем.
        private void OnCrisisBlitzAdvanced()
        {
            // ЗВУК — ДО раннего возврата: «вжик» новой мысли положен КАЖДОЙ из пяти, включая первую.
            // Молчит здесь только НАГОНЯЙ Ведущего (на первой мысли ещё висит объявление кризиса).
            if (Audio != null) Audio.Play(SoundEvent.BlitzThought);
            if (_game.BlitzThoughtNumber <= 1) return;
            _bubbleTimer.Show(HostContent.BlitzNagFor(_game.BlitzThoughtNumber));
        }

        // Impulse round opened: the S13 warning plate reveals via RenderCrisis. ЗВУК: струнная
        // «сирена-предупреждение» заводится ЛУПОМ на весь раунд и снимается в OnCrisisEnded —
        // тикающая бомба обязана звучать, пока бомба тикает, а не 3 секунды из неизвестно скольких.
        private void OnCrisisImpulseStarted()
        {
            if (Audio != null) Audio.PlayLoop(SoundEvent.Impulse);
        }

        // Поза трубки по «выезду» p ∈ 0…1 (0 = покой за краем, 1 = звонок): лерпятся И бокс, И наклон —
        // на эталонах звонящая трубка не просто сдвинута, она крупнее и повёрнута сильнее (см. геоблок).
        // `wobble` — качание звонка, добавка к углу.
        private void ApplyPhonePose(float p, float wobble = 0f)
        {
            if (_phoneImg == null) return;
            var r = Vector4.Lerp(PhoneRestRect, PhoneRingRect, p);
            AnchorPx(_phoneImg.rectTransform, r.x, r.y, r.z, r.w);
            _phoneImg.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Lerp(PhoneRestTilt, PhoneRingTilt, p) + wobble);
            // …и ЯРКОСТЬ по тому же p: спящая трубка притушена (PhoneRestTint), звонящая горит в полную
            // (Color.white). Один и тот же лерп, поэтому выезд/уезд плавно разгорается и гаснет за 0.3 с.
            // r3 (п.9): если звонок ПРОСПАЛИ, покой берётся ПОНИКШИЙ — трубка уезжает заметно темнее.
            _phoneImg.color = Color.Lerp(_phoneMissed ? PhoneMissedTint : PhoneRestTint, Color.white, p);
        }

        // Трубка ребёнка (§5b): виджет показан ровно пока Game.ChildOpen (не по возрасту — открывается на
        // MD02=ДА, гаснет после LT04). Звонок = Game.ChildFlashing: трубка выезжает из-за левого края за
        // PhoneSlideSeconds, встаёт в позу звонка (спрайт с запечёнными дугами) и КАЧАЕТСЯ ±6° (замена
        // невозможного «пульса альфы дуг», asset-map §12-7). Окно закрылось — поднял или проспал — трубка
        // сразу «успокаивается» (спрайт покоя, без дуг) и тем же 0.3 с уезжает за край. Фаза качания идёт
        // от СОБСТВЕННЫХ часов звонка, а не от Time.time: поза детерминирована и воспроизводима в тесте.
        private void ReflectChildPhone(float dt)
        {
            if (_childGroup == null) return;
            bool open = _game.ChildOpen;
            // ⚠ ВЫГОРАНИЕ БОЛЬШЕ НЕ ПРЯЧЕТ ТРУБКУ (r3, 2026-08-07). Прятали её ровно потому, что плашка
            // S7 была полноэкранным захватом поверх доски; захвата больше нет — повторное выгорание
            // показывает КОРОТКУЮ плашку у батареи, накрывать трубку нечем, и Game.ChildCallFrozen
            // соответственно тоже перестал смотреть на Burnout. Кризис по-прежнему убирает трубку сам
            // (RenderCrisis), а входной экран спецрежима ставит обычную паузу — там трубка честно замирает.
            // ⚠ КРИЗИС ПРЯЧЕТ ТРУБКУ, И ЗДЕСЬ ЭТО СКАЗАНО ЯВНО. В живом кадре ветка кризиса сюда не
            // заходит вовсе (её обслуживает RenderCrisis, он же гасит `_childGroup`), но seam DebugTick
            // зовёт нас НАПРЯМУЮ — и без этого условия трубка в тесте была бы видна там, где на экране
            // её нет. Одно условие видимости на оба пути — иначе звук синхронизируется с фикцией.
            bool visible = open && !_game.InCrisis;
            if (_childGroup.activeSelf != visible) _childGroup.SetActive(visible);
            if (!open)
            {
                _phoneOut = 0f; _phoneRingClock = 0f; _phoneRinging = false;
                _phoneMissed = false;
                return;
            }
            // Спрятана кризисом, но шкала жива: позу и часы качания НЕ трогаем — звонок вернётся на
            // экран тем же кадром, каким кончится кризис (состояние звонка держит Game, не мы).
            if (!visible) return;

            bool ringing = _game.ChildFlashing;
            if (ringing && !_phoneRinging)
            {
                _phoneRingClock = 0f;   // ФРОНТ звонка → фаза качания с нуля…
                _phoneMissed = false;   // …и новый звонок стирает «поникшесть» прошлого
            }
            _phoneRinging = ringing;

            var want = ringing ? _phoneRingSprite : _phoneRestSprite;
            if (_phoneImg.sprite != want) _phoneImg.sprite = want;

            // На паузе (S5-подсказка / баннер-бит) трубка замирает. Исключение — §D-модалка ребёнка:
            // там окно звонка ЖИВОЕ (Game.PausedInputsLive), трубка обязана выехать и качаться, иначе
            // «поднять звонок» показывали бы на неподвижной трубке за краем экрана.
            float step = (_game.Paused && !_game.PausedInputsLive) ? 0f : dt;
            _phoneOut = Mathf.MoveTowards(_phoneOut, ringing ? 1f : 0f, step / PhoneSlideSeconds);
            float wobble = 0f;
            if (ringing)
            {
                _phoneRingClock += step;
                wobble = PhoneWobbleDegrees
                         * Mathf.Sin(_phoneRingClock * 2f * Mathf.PI / PhoneWobblePeriod);
            }
            ApplyPhonePose(_phoneOut, wobble);
        }

        /// <summary>
        /// «Поднять трубку» (кнопка «!» / dev-Enter). Механика счёта живёт в Game (ChildPress закрывает
        /// окно и обнуляет серию пропусков); драйвер добавляет ФИДБЕК: окно было открыто и закрылось этим
        /// нажатием → звонок ПОДНЯТ → салют звёзд (revisions §5b + §6). Пре-нажатие/локаут окно не
        /// закрывают, поэтому салют за них не выдаётся.
        /// </summary>
        private void PressChildPhone()
        {
            bool wasRinging = _game.ChildFlashing;
            _game.HandleInput(GameInput.ChildPress);
            SyncPhoneRingLoop();   // трубку сняли — рингтон замолкает ТУТ ЖЕ, а не следующим кадром
            if (wasRinging && !_game.ChildFlashing)
            {
                _phoneMissed = false;
                // «Алло» калимбой ИДЁТ ПОД САЛЮТОМ (манифест: держать тише салюта) — за это отвечает
                // VolPhonePickup < VolReward в каталоге, тест инвариант стережёт.
                if (Audio != null) Audio.Play(SoundEvent.PhonePickup);
                StarBurst();
            }
        }

        /// <summary>
        /// r3 (п.9): окно звонка закрылось НЕПОДНЯТЫМ. Успех уже салютует звёздами — у пропуска до сих пор
        /// не было НИКАКОГО отклика, и игрок не понимал, что вообще что-то потерял. Теперь пропуск
        /// сообщается двумя средствами того же языка: трубка уезжает ПОНИКШЕЙ (спрайт покоя + тинт
        /// <see cref="PhoneMissedTint"/>, темнее обычного покоя) и Ведущий это озвучивает.
        /// </summary>
        private void OnChildCallMissed()
        {
            _phoneMissed = true;
            // Оборванный гудок. Рингтон снимет SyncPhoneRingLoop тем же кадром (окно закрылось).
            if (Audio != null) Audio.Play(SoundEvent.PhoneMissed);
            _bubbleTimer.Show(ChildMissedLine);
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

            BuildTutorialOverlay(canvasGo.transform);   // dims every screen when up

            // §D — модальный экран новой шкалы. СТРОГО выше S5-подсказки (задание: «затемнение ~40 % INK
            // поверх геймплея, ниже модальных окон») и ниже слоя салюта, который строится следующим.
            BuildNewScaleOverlay(canvasGo.transform);

            // §6 салют — build-spec §1.3 слой 7, ПОВЕРХ ВСЕГО (включая модалку туториала: та поднимает
            // себя в конец на показе, поэтому бёрст тоже поднимает свой слой на каждом выстреле).
            BuildStarLayer(canvasGo.transform);
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
            // Плашка блица — один сгенерированный спрайт в канон-пропорциях (BuildBlitzPlateSprite).
            // Строится ЗДЕСЬ, чтобы размер текстуры совпал с экранным размером плашки пиксель-в-пиксель.
            _blitzYesSprite = BuildBlitzPlateSprite("BlitzPlateYes",
                Mathf.RoundToInt(CrisisBlitzPlateSize.x), Mathf.RoundToInt(CrisisBlitzPlateSize.y), GoGreen);
            _blitzNoSprite = BuildBlitzPlateSprite("BlitzPlateNo",
                Mathf.RoundToInt(CrisisBlitzPlateSize.x), Mathf.RoundToInt(CrisisBlitzPlateSize.y), TimerRed);

            // Crisis-only overlay label — «ДА» is baked into the art, so this stays HIDDEN in ordinary play.
            // (White with the ink kant, matching the baked lettering, for the «ВСЁ НОРМАЛЬНО»/«О НЕТ» relabel.)
            // ТЕНЬ идёт ПЕРВОЙ (ниже по иерархии = под буквами), сама подпись — последней.
            _yesPlateShade = NewPlateShade("YesTextShade", _yesPlate, "ДА");
            var yesText = NewText("YesText", _yesPlate.transform, "ДА", 96, TextAnchor.MiddleCenter, Color.white, _display);
            PlateTextRect(yesText.rectTransform);   // inset onto the visible plate (clears the baked shadow)
            yesText.resizeTextForBestFit = true; yesText.resizeTextMinSize = 40; yesText.resizeTextMaxSize = 120;
            DisplayFx(yesText, out _yesLabelKant, out _yesLabelSoft);
            _yesPlateText = yesText;

            _noPlate = NewSprite("NoPlate", _gamePanel.transform, _bakedNoSprite);
            _noPlate.type = Image.Type.Simple;
            _noRect = _noPlate.rectTransform;
            // Centre 432 (not the §B box centre 472.5): on the reference explainer the red art is FLUSH LEFT
            // in its build-spec box, so the drawn plate must start at x≈169 — canon call by the Maintainer,
            // «explainer-PNG = пиксель-истина, табличные боксы — ориентир». The green plate is box-centred.
            AnchorPx(_noRect, 432f, 872f, BakedNoPlateSize.x, BakedNoPlateSize.y);
            _noRect.localRotation = Quaternion.Euler(0, 0, NoTilt);
            _noPlateShade = NewPlateShade("NoTextShade", _noPlate, "СПАСИБО,\nНЕ НАДО");
            var noText = NewText("NoText", _noPlate.transform, "СПАСИБО,\nНЕ НАДО", 56, TextAnchor.MiddleCenter, Color.white, _display);
            PlateTextRect(noText.rectTransform);   // inset onto the visible plate (clears the baked shadow)
            noText.resizeTextForBestFit = true; noText.resizeTextMinSize = 24; noText.resizeTextMaxSize = 60;
            DisplayFx(noText, out _noLabelKant, out _noLabelSoft);
            _noPlateText = noText;
            UseBakedPlates();   // hides both overlay labels — ordinary play shows the baked art alone

            // ---- BLOCK$ (S10 + r3 п.6): БАННЕР И ЧИП ЦЕНЫ — ПОВЕРХ ПЛАШЕК ОТВЕТА -------------------
            // ⚠ Z-ПОРЯДОК ИСПРАВЛЕН 2026-08-07 (живой плейтест: «баннер и цена уезжают ПОД кнопки»).
            // Обе фигуры были детьми `_cardRoot`, а плашки ответа — соседями ПОЗЖЕ него, поэтому кнопки
            // рисовались поверх: баннер «нет денег» и чип цены оказывались частично срезаны ровно в тот
            // момент, когда их и надо читать. Лечится не сдвигом (карточка и плашки перекрываются по
            // геометрии — это канон композиции), а СЛОЕМ: обе фигуры переехали в собственный контейнер,
            // созданный ПОСЛЕ обеих плашек, и садятся теперь абсолютными боксами (BlockBannerRect /
            // CardPriceRect — те же экранные координаты, что давали прежние доли карточки: 705…815 и
            // 811…889 внутри кремового поля).
            // Плата за это одна и осознанная: баннер больше не едет вместе с анимацией въезда карточки
            // (CardEntry масштабирует `_cardRoot`) — он просто появляется на своём месте. Это состояние
            // доски, а не часть картинки карточки.
            _blockOverlay = NewGroup("BlockOverlay", _gamePanel.transform);
            // No separate dim veil: the card is dimmed by tinting _cardFrame directly (SetCardBlockedDim) so
            // the darkening follows the marquee's exact rounded silhouette — a rounded-rect overlay still showed
            // straight edges cutting across the sunburst rays (design-gate S10 fix).
            // Rounded red banner (bar-track 9-slice tinted red, navy-outlined white text) low on the card so
            // the dimmed «Пора подлечиться!» question still reads above it (S10). One sentence-case line.
            // ЧЁРНЫЙ KEYLINE (долг гейта 2026-08-05): весь арт-пак несёт чёрный кант, и красный баннер с
            // чипом цены были ЕДИНСТВЕННЫМИ фигурами экрана без него — голая заливка упиралась прямо в
            // кремовое поле карточки. Кант строится тем же приёмом, что кант тревоги шкал: отдельный
            // `bar-track`, покрашенный в INK, СОСЕДОМ и НИЖЕ (меньший siblingIndex) — он торчит кольцом
            // из-под плашки на BlockKeylineInk со всех сторон. Толщина 4 px = верх коридора 3–4 из
            // задания: у `bar-track` край мягкий (~1 px AA с каждой стороны), и на 3 px в кадре остаётся
            // ОДНА сплошная чёрная строка — вдвое тоньше собственного keyline арта; на 4 их две.
            var blockBannerInkImg = NewSprite("BlockBannerInk", _blockOverlay.transform, Sprite("bar-track"));
            blockBannerInkImg.type = Image.Type.Sliced;
            blockBannerInkImg.color = OnBarTrack(Ink);
            _blockBannerInk = blockBannerInkImg.gameObject;
            AnchorPx(blockBannerInkImg.rectTransform, BlockBannerRect.x, BlockBannerRect.y,
                BlockBannerRect.z + 2f * BlockKeylineInk, BlockBannerRect.w + 2f * BlockKeylineInk);

            var blockBannerImg = NewSprite("BlockBanner", _blockOverlay.transform, Sprite("bar-track"));
            blockBannerImg.type = Image.Type.Sliced;
            blockBannerImg.color = new Color(0.90f, 0.18f, 0.14f);   // punchy saturated red (S10 banner)
            _blockBanner = blockBannerImg.gameObject;
            // Low on the taller art-pack plate but still INSIDE its cream field (screen y≈705..815 of the
            // field's 286..892) — the old «just below the card» anchor now lands on the answer plates.
            AnchorPx(blockBannerImg.rectTransform, BlockBannerRect.x, BlockBannerRect.y,
                BlockBannerRect.z, BlockBannerRect.w);
            var blockTxt = NewText("BlockText", _blockBanner.transform,
                "Как жаль, у вас нет денег на это!", 40, TextAnchor.MiddleCenter, Color.white, _display);
            blockTxt.horizontalOverflow = HorizontalWrapMode.Overflow;   // single line, best-fit shrinks to width
            Inset(blockTxt.rectTransform, 40f);
            blockTxt.resizeTextForBestFit = true; blockTxt.resizeTextMinSize = 20; blockTxt.resizeTextMaxSize = 44;
            DisplayFx(blockTxt);
            SetBlockBannerVisible(false);

            // ---- BLOCK$ price sub-line (S10): the required amount, on any BLOCK$-priced card ----
            // A dark rounded plate (bar-track 9-slice, tinted Ink) BEHIND the text, sat just BELOW the
            // card so it clears the bottom bulb ring — the S10 dark block-tag: light text on dark, never
            // the forbidden «gold on yellow». Added AFTER the veil so it reads in the blocked state too.
            // Plate is created first (lower sibling index → drawn behind the text). Both are sized to the
            // text and shown/hidden together in ApplyPriceLabel.
            // «СТОИТ N ₽» (gold) when affordable · «НУЖНО N ₽» (light) when blocked.
            // Тот же чёрный keyline, что у баннера (см. блок выше): сосед НИЖЕ чипа, размер = чип + 2×кант,
            // пересчитывается вместе с чипом в ApplyPriceLabel (чип растёт по тексту).
            _cardPriceInk = NewSprite("CardPriceInk", _blockOverlay.transform, Sprite("bar-track"));
            _cardPriceInk.type = Image.Type.Sliced;
            _cardPriceInk.color = OnBarTrack(Ink);
            AnchorPx(_cardPriceInk.rectTransform, CardPriceRect.x, CardPriceRect.y,
                CardPriceRect.z + 2f * BlockKeylineInk, CardPriceRect.w + 2f * BlockKeylineInk);

            _cardPricePlate = NewSprite("CardPricePlate", _blockOverlay.transform, Sprite("bar-track"));
            _cardPricePlate.type = Image.Type.Sliced;
            // ⚠ Заливка чипа — DEEP COBALT, а не INK. Чип был закрашен ровно тем же INK, что и его
            // чёрный keyline, и кант получался НЕВИДИМЫМ (замер по кадру: и заливка, и кант рисовались
            // как [10,10,27] — «обводка есть, а глазом её нет»). Тёмная плашка S10 при этом сохраняется:
            // тёмный синий тег со светлым текстом, ровно тот же тон, каким в проекте уже нарисованы
            // тёмные подложки (CobaltDeep), только теперь с настоящим чёрным кантом.
            // ЦВЕТ ЧИПА, три числа — чтобы их больше не путали (Codex MINOR 2026-08-05):
            //   • сырой тинт Image.color  = #22409C — токен, ПОДЕЛЁННЫЙ на заливку спрайта (OnBarTrack);
            //   • рендер по модели sRGB   = #1F3A96 — тинт × заливка `bar-track`, т.е. РОВНО токен
            //     CobaltDeep: OnBarTrack делит на заливку, uGUI умножает обратно (это и пинит тест);
            //   • пиксель на кадре        = #1D3996 (замер по `inc7-blocked.png`, сплошная середина чипа).
            // Расхождение ≤2/255 — не ошибка тинта: проект рендерит в ЛИНЕЙНОМ цветовом пространстве
            // (ProjectSettings m_ActiveColorSpace: 1), а модель теста перемножает в sRGB. Тот же сдвиг
            // ≤2/255 виден на красном баннере (модель #D02A22 → кадр #D02920) и на всех прочих
            // OnBarTrack-плашках, поэтому тест пинит МОДЕЛЬ, а фактический пиксель задокументирован тут.
            _cardPricePlate.color = OnBarTrack(CobaltDeep);
            // Bottom of the cream field (screen y≈850), under the block banner — the art-pack plate reaches
            // y≈972, so the old below-the-card anchor would now sit on the answer plates.
            AnchorPx(_cardPricePlate.rectTransform, CardPriceRect.x, CardPriceRect.y,
                CardPriceRect.z, CardPriceRect.w);
            _cardPriceText = NewText("CardPrice", _blockOverlay.transform, "", 40, TextAnchor.MiddleCenter, Bulb, _body);
            AnchorPx(_cardPriceText.rectTransform, CardPriceRect.x, CardPriceRect.y, 820f, CardPriceRect.w);
            // ONE line by design («цена N ₽»). ApplyPriceLabel sizes the rect to preferredWidth, and font
            // metrics round differently at different canvas scales — with Wrap a half-pixel shortfall threw
            // the «₽» onto a second line that hung off the dark plate. Overflow makes that unreachable.
            _cardPriceText.horizontalOverflow = HorizontalWrapMode.Overflow;
            // ⚠ БЕЗ DisplayFx (дизайн-скептик, раунд 2: «под чипом проступает вторая серая подпись цены»).
            // Причина двоения — не второй объект, а САМ эффект: Unity применяет Outline и Shadow ПОСЛЕДОВАТЕЛЬНО
            // к одному мешу, поэтому Shadow дублирует УЖЕ раздутую обводкой копию, и на кегле 40 она читается
            // отдельной размытой строкой, торчащей из-под чипа. Здесь эффект и не нужен: чип — ТЁМНАЯ плашка
            // (CobaltDeep) со светлым текстом, контраст даёт сама подложка. Ровно та же причина, по которой
            // без DisplayFx живут вопрос карточки (чернила по крему) и обе строки короткой плашки выгорания.
            _cardPriceInk.gameObject.SetActive(false);
            _cardPricePlate.gameObject.SetActive(false);
            _cardPriceText.gameObject.SetActive(false);


            // ---- Трубка ребёнка (§5b): слой 5 «оверлеи» — создаётся ПОСЛЕ карточки и плашек ответа ----
            BuildChildPhone();

            // ---- Выгорание, ПОВТОРНОЕ: КОРОТКАЯ ПЛАШКА БЕЗ БЛОКИРОВКИ (r3, п.5б) --------------------
            // ⚠ Полноэкранный захват S7 (непрозрачная кобальтовая подложка + лучи + «ВЫГОРАНИЕ!» на пол-
            // экрана) СНЯТ 2026-08-07. Он был двумя проблемами сразу: программной подачей вместо арт-пака
            // («крупная красная батарея вместо программного ВЫГОРАНИЕ!» — основательница) и глухой
            // шторой поверх доски, из-за которой приходилось прятать трубку ребёнка и морозить звонок
            // (Game.ChildCallFrozen), иначе набегали НЕВИДИМЫЕ пропуски.
            // Теперь: ПЕРВОЕ выгорание объясняет входной экран спецрежима (с паузой дренажа), а каждое
            // следующее — вот эта короткая плашка ПОД батареей, на своём месте, ничего не накрывающая.
            // Собрана тем же блоком, что и плашка «РАССТАЛИСЬ»: `bar-track` + кант, чтобы жить в арт-паке.
            // Контейнер, чтобы кант и плашка гасли одним SetActive и кант лежал СОСЕДОМ НИЖЕ (ребёнок
            // рисуется поверх родителя, поэтому кант не может быть ребёнком плашки).
            _burnoutPlate = NewGroup("BurnoutPlate", _gamePanel.transform);
            var burnEdge = NewSprite("BurnoutPlateEdge", _burnoutPlate.transform, Sprite("bar-track"));
            burnEdge.type = Image.Type.Sliced;
            burnEdge.color = OnBarTrack(Ink);
            AnchorPx(burnEdge.rectTransform, BurnoutPlateRect.x, BurnoutPlateRect.y,
                BurnoutPlateRect.z + 2f * BlockKeylineInk, BurnoutPlateRect.w + 2f * BlockKeylineInk);

            var burnPlate = NewSprite("BurnoutPlateFill", _burnoutPlate.transform, Sprite("bar-track"));
            burnPlate.type = Image.Type.Sliced;
            burnPlate.color = OnBarTrack(BurnoutRed);
            AnchorPx(burnPlate.rectTransform, BurnoutPlateRect.x, BurnoutPlateRect.y,
                BurnoutPlateRect.z, BurnoutPlateRect.w);
            // Заголовок БЕЛЫЙ, как на плашке «РАССТАЛИСЬ»: жёлтый по красному на кегле ~28 читался вяло,
            // а «фирменный» жёлтый S7 был рассчитан на 150 px во весь экран.
            var burnoutTxt = NewText("BurnoutText", burnPlate.transform,
                BurnoutPlateTitle, 46, TextAnchor.MiddleCenter, Color.white, _display);
            // ⚠ Обе строки обязаны лечь ВНУТРЬ видимой пилюли `bar-track` (её скругление съедает ~16 px с
            // каждой стороны), поэтому боксы посажены по замеру пилюли, а не «на глаз по плашке».
            // Кегли и высоты строк подобраны под ВИДИМУЮ пилюлю плашки (её скругление съедает по 16 px):
            // нарисованный глиф выше своего рект-бокса на ~15 %, поэтому боксы взяты с запасом.
            Anchor(burnoutTxt.rectTransform, new Vector2(0.5f, 0.6477f), new Vector2(BurnoutPlateRect.z - 56f, 28f));
            burnoutTxt.resizeTextForBestFit = true; burnoutTxt.resizeTextMinSize = 16; burnoutTxt.resizeTextMaxSize = 22;
            burnoutTxt.verticalOverflow = VerticalWrapMode.Truncate;
            // ⚠ НИ BurnoutTitleFx (его КРАСНЫЙ кант со сдвигом 5 px рассчитан на заголовок в 150 px во
            // весь экран и на кегле ~28 превращает буквы в кашу — да ещё красным по красному), НИ
            // DisplayFx (его тень −6 px на этом кегле читается вторым, размазанным словом). Тонкий
            // чернильный кант — ровно то, что нужно короткой плашке.
            var burnoutKant = burnoutTxt.gameObject.AddComponent<Outline>();
            burnoutKant.effectColor = Ink;
            burnoutKant.effectDistance = new Vector2(2f, -2f);
            var burnoutSub = NewText("BurnoutSubtitle", burnPlate.transform,
                BurnoutPlateSubtitle, 28, TextAnchor.MiddleCenter, Color.white, _display);
            Anchor(burnoutSub.rectTransform, new Vector2(0.5f, 0.3295f), new Vector2(BurnoutPlateRect.z - 56f, 24f));
            burnoutSub.resizeTextForBestFit = true; burnoutSub.resizeTextMinSize = 13; burnoutSub.resizeTextMaxSize = 18;
            burnoutSub.verticalOverflow = VerticalWrapMode.Truncate;
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

            // ⚠ ЖЁЛТЫЙ БАННЕР-РУБРИКА ВЕХИ (S4) СНЯТ ЦЕЛИКОМ — плейтест основательницы 2026-08-05:
            // «их нет в присланных макетах, убрать вместе с паузой баннер-бита». Вехи (TIMELINE) идут
            // обычными карточками; облачко Ведущего (выше) остаётся единственным голосом на поле.
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
            // (Группа альфы для «вздрагивания» батареи на невалидный вдох снята 2026-08-07 вместе с
            // ритм-гейтом: отвергнутых вдохов больше не бывает, отклику нечего показывать.)
            _batteryImg = NewSprite("Battery", _energyGroup.transform, Sprite("energy-battery-v2"));
            AnchorPx(_batteryImg.rectTransform, BatteryRect.x, BatteryRect.y, BatteryRect.z, BatteryRect.w);
            // §4-ТРЕВОГА: та же батарея, ОФЛАЙН перекрашенная в палитру эталона (`energy-battery-alarm-v2`,
            // scratchpad/make_battery_alarm.py). Тот же спрайтовый размер и тот же рект → кроссфейд по альфе
            // не двигает ни пикселя, а синяя рамка честно становится красной (тинтом это недостижимо:
            // uGUI умножает, и синее под красным множителем уходит в чёрный). Прозрачна вне тревоги.
            _batteryAlarm = NewSprite("BatteryAlarm", _energyGroup.transform, Sprite("energy-battery-alarm-v2"));
            AnchorPx(_batteryAlarm.rectTransform, BatteryRect.x, BatteryRect.y, BatteryRect.z, BatteryRect.w);
            _batteryAlarm.color = new Color(1f, 1f, 1f, 0f);
            _batteryAlarm.gameObject.SetActive(false);
            // Sibling order IS the z-order and is asserted (HudConformanceTests): baked battery sprite →
            // alarm repaint → cream «empty» mask → yellow top-up → alarm charge band. Every overlay draws
            // ABOVE the sprite (otherwise the baked 75.1 % level would show through), and the top-up draws
            // after the mask so the live level always wins on the level line itself.
            _energyEmpty = NewSolid("EnergyEmpty", _energyGroup.transform, BatteryCream);
            _energyTopUp = NewSolid("EnergyTopUp", _energyGroup.transform, BatteryYellow);
            // «Остаток заряда» — от живого уровня до ДНА полости. В спокойном ходе прозрачна (там всё уже
            // нарисовано запечённой заливкой спрайта); в тревоге это она красит заряд в #FF0506, иначе под
            // красной батареей осталась бы жёлтая полоска запечённой заливки (эталон её не знает).
            _energyCharge = NewSolid("EnergyCharge", _energyGroup.transform, BatteryYellow);
            _energyCharge.gameObject.SetActive(false);
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
            _relKant = NewAlarmKant("RelAlarmKant", _balancerGroup.transform,
                RelBarDrawnCx, RelBarDrawnCy, RelBarDrawnW, RelBarDrawnH, out _relKantInk);
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
            _healthKant = NewAlarmKant("HealthAlarmKant", _healthGroup.transform,
                HealthBarDrawnCx, HealthBarDrawnCy, HealthBarDrawnW, HealthBarDrawnH, out _healthKantInk);
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
            _energyShown = energy;               // §4: тревожная полоса заряда строится от ТОГО ЖЕ уровня
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

        // ================================================================ §4 · КРАСНЫЕ ТРЕВОГИ ШКАЛ
        //
        // ВЫБРАННЫЕ СПОСОБЫ ПОДСВЕТКИ (решение владельца инкремента, задача §1):
        //   • ЭНЕРГИЯ — ПЕРЕКРАС. Батарея краснеет ЦЕЛИКОМ, ровно как на эталоне «Экран подсвечена
        //     красным шкала.png»: офлайн-перекрашенная копия спрайта кроссфейдится поверх спокойной
        //     (рамка #CE183B), а полость дорисовывается кодом — пустота #FF3736, остаток заряда #FF0506.
        //     Молния остаётся СВОИМ кремовым слоем поверх всего: на красном она читается лучше, чем
        //     красная-на-красной эталона, и задача просит именно читаемости.
        //   • ЗДОРОВЬЕ и ОТНОШЕНИЯ — КРАСНЫЙ КАНТ вокруг виджета, НЕ тонировка плашки. Причина —
        //     различимость: у обоих баров есть СВОИ нарисованные красные зоны (#FF150B у здоровья,
        //     #F02120 у отношений), и тонировка плашки читалась бы как «маркер заехал в красное», то
        //     есть как ПОЗИЦИЯ. Кант обводит ВЕСЬ виджет снаружи чёрной обводки — это структурно другая
        //     фигура, её нельзя спутать с зоной внутри бара, и она читается как СОСТОЯНИЕ.
        //   • ДЕНЬГИ — ПЕРЕКРАС банки и её лейбла (спек §4 «банка/лейбл краснеет»): банка и монета
        //     тонируются RED_BRIGHT. Кремовый лейбл — часть спрайта банки, поэтому краснеет вместе с
        //     ней; сумма на нём остаётся INK и читается.
        //
        // ПУЛЬС: яркость = синус периода 0.7 с, фаза берётся от `_alarmClock` — времени В ТРЕВОГЕ, а не
        // от Time.time. Часы стоят на паузе (туториал/баннер-бит), поэтому пульс замирает вместе с игрой
        // и полностью детерминирован для кадра и теста — тот же приём, что у купола §5a.
        //
        // ГИСТЕРЕЗИС: включение на спековом пороге, выключение — на пороге, отодвинутом внутрь нормы на
        // AlarmHysteresis (2 п.п.). Без него шкала, ползущая по границе, мигала бы каждые пол-секунды.

        /// <summary>Красный кант тревоги: скруглённая плашка `bar-track` ПОЗАДИ бара, раздутая на
        /// <see cref="AlarmKantPad"/> вокруг НАРИСОВАННОГО бокса бара. Невидима вне тревоги (альфа 0).
        /// Собирается ПАРОЙ: сначала чёрное кольцо (та же плашка, ещё на <see cref="AlarmKantInk"/> шире,
        /// тонированная в INK), поверх — красное. Так у канта появляется тот же чёрный keyline, что несёт
        /// весь арт-пак, и красное не упирается голым краем в лучи фона (полиш дизайн-гейта).</summary>
        private Image NewAlarmKant(string name, Transform parent, float cx, float cy, float w, float h,
                                   out Image ink)
        {
            ink = NewSprite(name + "Ink", parent, Sprite("bar-track"));
            ink.type = Image.Type.Sliced;
            AnchorPx(ink.rectTransform, cx, cy,
                w + 2f * (AlarmKantPad + AlarmKantInk), h + 2f * (AlarmKantPad + AlarmKantInk));
            ink.color = new Color(1f, 1f, 1f, 0f);
            ink.gameObject.SetActive(false);

            var img = NewSprite(name, parent, Sprite("bar-track"));
            img.type = Image.Type.Sliced;
            AnchorPx(img.rectTransform, cx, cy, w + 2f * AlarmKantPad, h + 2f * AlarmKantPad);
            img.color = new Color(1f, 1f, 1f, 0f);
            img.gameObject.SetActive(false);
            return img;
        }

        /// <summary>Множитель яркости, гасящий цвет и НЕ трогающий альфу.</summary>
        private static Color Dim(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>Пульс яркости шкалы: 0.72…1.0, фаза от времени в тревоге (детерминированно, стоит на паузе).</summary>
        private float AlarmBrightness(int i)
            => Mathf.Lerp(AlarmPulseMin, 1f,
                0.5f + 0.5f * Mathf.Cos(_alarmClock[i] / AlarmPulsePeriod * 2f * Mathf.PI));

        /// <summary>Виджет шкалы СЕЙЧАС на экране и может нести тревогу: живая игра, не кризис (там ряд
        /// HUD спрятан), виджет открыт по возрасту и не скрыт баннер-битом. На опенере/финале — false.</summary>
        private bool AlarmLive(AlarmScale s)
        {
            if (_game == null || _game.State != GameState.Playing || _game.InCrisis) return false;
            // ⚠ ШКАЛА НЕ ТРЕВОЖИТ, ПОКА ИДЁТ ЕЁ СОБСТВЕННОЕ ОБУЧЕНИЕ (r4 п.1а, живой плейтест:
            // «во время туториала деньги были красными — смотрелось очень плохо»).
            // Деньги открываются с 0 ₽, а стоимость жизни уводит счёт в минус сразу же, поэтому первая
            // же BLOCK$-карточка поднимает тревогу банки — и §D-модалка показывает игроку КРАСНЫЙ
            // виджет ровно в тот момент, когда учит им пользоваться. Виджет при этом физически тот же:
            // BorrowBigWidget переносит `_moneyGroup` в слот модалки и оставляет активным, так что
            // проверка `activeInHierarchy` ниже его не отсекает — гасить надо явно.
            // Гасим ТОЛЬКО объясняемую шкалу: чужие тревоги под модалкой — законная информация HUD.
            // Выключение проходит по ветке «не живая» в ReflectAlarms, то есть тихо и БЕЗ салюта звёзд:
            // обучение не должно выглядеть как заслуженная починка.
            if (_nsShowing && AlarmScaleOf(_nsWhich) == s) return false;
            var group = s switch
            {
                AlarmScale.Energy => _energyGroup,
                AlarmScale.Health => _healthGroup,
                AlarmScale.Relations => _balancerGroup,
                _ => _moneyGroup,
            };
            return group != null && group.activeInHierarchy;
        }

        /// <summary>Какой тревоге соответствует §D-окно новой шкалы. `Child` своей тревоги не имеет
        /// (звонок ребёнка — не тревога шкалы), поэтому отображается в «никакую».</summary>
        private static AlarmScale? AlarmScaleOf(NewScale ns) => ns switch
        {
            NewScale.Money => AlarmScale.Money,
            NewScale.Relations => AlarmScale.Relations,
            NewScale.Energy => AlarmScale.Energy,
            _ => null,
        };

        /// <summary>Порог тревоги с гистерезисом: <paramref name="on"/> — состояние ПРЕДЫДУЩЕГО кадра.</summary>
        private bool AlarmRaised(AlarmScale s, bool on)
        {
            var sc = _game.Scales;
            switch (s)
            {
                case AlarmScale.Energy:
                    return on ? sc.Energy < AlarmScaleOnBelow + AlarmHysteresis : sc.Energy < AlarmScaleOnBelow;
                case AlarmScale.Health:
                    return on ? sc.Health < AlarmScaleOnBelow + AlarmHysteresis : sc.Health < AlarmScaleOnBelow;
                case AlarmScale.Relations:
                    // Зона 40–75 — МЕХАНИЧЕСКАЯ (Game.RelZoneMin/Max), тревога только отображает её.
                    float lo = ThanksNoThanks.Game.RelZoneMin + (on ? AlarmHysteresis : 0f);
                    float hi = ThanksNoThanks.Game.RelZoneMax - (on ? AlarmHysteresis : 0f);
                    return sc.Relationships < lo || sc.Relationships > hi;
                default:
                    // Деньги: тревога РОВНО по существующему сигналу блокировки — карточка BLOCK$ и денег
                    // не хватает. Сигнал дискретный (меняется только со сменой карточки/платежом), так что
                    // дребезжать нечему и гистерезис ему не нужен.
                    return _game.CurrentCardBlocked;
            }
        }

        /// <summary>Продвинуть тревоги на dt и перерисовать подсветку. Единственная точка, где §4 живёт:
        /// её зовут и Update, и синхронный DebugTick, и скриншот-позы.</summary>
        private void ReflectAlarms(float dt)
        {
            if (_game == null) return;
            bool frozen = _game.Paused;
            // Сменилась ли карточка В ЭТОМ такте. Тревога ДЕНЕГ — это CurrentCardBlocked, а он фиксируется
            // на ВЫДАЧЕ карточки, поэтому гаснуть он умеет ровно двумя способами: игрок довёл сумму до
            // цены (починка) ИЛИ пришла другая карточка (не заслуга игрока). Второе не должно давать
            // салют НИКОГДА — даже если крутилку крутили секунду назад на заблокированной карточке.
            bool cardChanged = !ReferenceEquals(_game.CurrentCard, _lastReflectCard);
            _lastReflectCard = _game.CurrentCard;
            for (int i = 0; i < AlarmCount; i++)
            {
                _sinceScaleInput[i] = Mathf.Min(_sinceScaleInput[i] + dt, 999f);
                var scale = (AlarmScale)i;
                if (!AlarmLive(scale))
                {
                    // Шкала ушла с экрана (опенер/финал/кризис/баннер-бит/ещё не открыта по возрасту) —
                    // тревога снимается МОЛЧА: это не «игрок починил», салют тут не положен.
                    _alarmOn[i] = false;
                    _alarmWeight[i] = 0f;
                    _alarmClock[i] = 0f;
                }
                else
                {
                    bool on = AlarmRaised(scale, _alarmOn[i]);
                    if (on && !_alarmOn[i])
                    {
                        _alarmClock[i] = 0f;      // вход в тревогу — фаза с пика
                        // ЗВУК — НА ФРОНТЕ, один раз на заход: пульс 0.7 с озвучивать нельзя, это
                        // прямое нарушение правила отбора №2 («ничего резкого — человек слушает это
                        // всё время»). Деньги/энергия/здоровье = ОДИН файл на трёх высотах. Отношения
                        // в семейство тревог не входят: у них свои блипы зоны (манифест §5).
                        if (Audio != null && !frozen)
                            Audio.Play(scale == AlarmScale.Relations
                                ? SoundEvent.ZoneOut
                                : AudioCatalog.ForAlarm(scale));
                    }
                    // …и симметрично: маркер отношений ВЕРНУЛСЯ в зелёную зону — мягкий позитивный блип.
                    if (!on && _alarmOn[i] && scale == AlarmScale.Relations && Audio != null && !frozen)
                        Audio.Play(SoundEvent.ZoneIn);
                    // §6: салют — только за КАЛИБРОВКУ. Свежий ввод по шкале + выход из тревоги, И (для
                    // денег) причина выхода не «пришла другая карточка».
                    bool byCardSwap = scale == AlarmScale.Money && cardChanged;
                    if (!on && _alarmOn[i] && _sinceScaleInput[i] <= AlarmRecentInputSeconds && !byCardSwap)
                        StarBurst();
                    _alarmOn[i] = on;
                    if (on)
                    {
                        if (!frozen) _alarmClock[i] += dt;            // на паузе пульс замирает
                        _alarmWeight[i] = 1f;
                    }
                    else _alarmWeight[i] = Mathf.MoveTowards(_alarmWeight[i], 0f, dt / AlarmFadeSeconds);
                }
                PaintAlarm(scale, _alarmWeight[i], AlarmBrightness(i));
            }
            AdvanceStars(dt);
        }

        /// <summary>Нарисовать подсветку одной шкалы: <paramref name="w"/> — вес 0…1, <paramref name="b"/> —
        /// яркость пульса. w = 0 обязано вернуть РОВНО спокойный вид (иначе тревога «залипает»).</summary>
        private void PaintAlarm(AlarmScale s, float w, float b)
        {
            switch (s)
            {
                case AlarmScale.Energy:
                {
                    if (_batteryAlarm == null) return;
                    // Кроссфейд красной копии + пульс её ЯРКОСТИ (тинт по серому: 0.72…1.0).
                    _batteryAlarm.color = new Color(b, b, b, w);
                    _energyEmpty.color = Color.Lerp(BatteryCream, Dim(AlarmCavityEmpty, b), w);
                    _energyTopUp.color = Color.Lerp(BatteryYellow, Dim(AlarmCavityCharge, b), w);
                    // Полоса остатка заряда нужна ТОЛЬКО в тревоге (в спокойном ходе её роль играет
                    // запечённая заливка спрайта) — вне тревоги оба тревожных слоя выключены целиком,
                    // так что спокойная перепись спрайтов игровой панели остаётся прежней.
                    ShowAlarmPart(_batteryAlarm, w);
                    ShowAlarmPart(_energyCharge, w);
                    if (w > 0f)
                    {
                        _energyCharge.color = Color.Lerp(BatteryYellow, Dim(AlarmCavityCharge, b), w);
                        float p = Mathf.Clamp01(_energyShown / 100f);
                        float levelTop = CavityTop + CavityH * (1f - p);
                        float hgt = Mathf.Max(0f, CavityTop + CavityH - levelTop);
                        AnchorPx(_energyCharge.rectTransform,
                            CavityX + CavityW / 2f, levelTop + hgt / 2f, CavityW, hgt);
                    }
                    return;
                }
                case AlarmScale.Health:
                    PaintKant(_healthKantInk, _healthKant, w, b);
                    return;
                case AlarmScale.Relations:
                    PaintKant(_relKantInk, _relKant, w, b);
                    return;
                default:
                {
                    if (_jarImg == null) return;
                    var tint = Color.Lerp(Color.white, Dim(DomeAlarm, b), w);
                    if (_jarImg.color != tint) _jarImg.color = tint;
                    if (_moneyCoin != null && _moneyCoin.color != tint) _moneyCoin.color = tint;
                    return;
                }
            }
        }

        /// <summary>Тревожный слой существует только пока горит тревога: вне её он ВЫКЛЮЧЕН, а не просто
        /// прозрачен — спокойный кадр остаётся ровно тем же набором картинок, что и до инкремента.</summary>
        private static void ShowAlarmPart(Image img, float w)
        {
            bool show = w > 0f;
            if (img.gameObject.activeSelf != show) img.gameObject.SetActive(show);
        }

        // Кант рисуется на `bar-track`, а у неё СВОЯ заливка #E7E9F5 и uGUI на неё УМНОЖАЕТ, поэтому
        // токен пропускается через OnBarTrack — иначе «красный» вышел бы на ~10 % грязнее.
        private static Color KantColor(float w, float b)
        {
            var c = OnBarTrack(Dim(DomeAlarm, b));
            return new Color(c.r, c.g, c.b, w);
        }

        // Чёрное кольцо канта: тот же путь через OnBarTrack, но токеном INK — и БЕЗ пульса. Keyline в
        // арт-паке всегда одинаково чёрный, «дышит» только красное; за фейдом кольцо идёт альфой, чтобы
        // тревога уходила с экрана одной фигурой, а не оставляла висеть чёрную рамку.
        private static Color KantInkColor(float w)
        {
            var c = OnBarTrack(Ink);
            return new Color(c.r, c.g, c.b, w);
        }

        /// <summary>Нарисовать пару «чёрное кольцо + красный кант» одной шкалы (вес <paramref name="w"/>,
        /// яркость пульса <paramref name="b"/>). w = 0 снимает обе фигуры с экрана.</summary>
        private void PaintKant(Image ink, Image kant, float w, float b)
        {
            if (kant == null) return;
            if (ink != null) { ink.color = KantInkColor(w); ShowAlarmPart(ink, w); }
            kant.color = KantColor(w, b);
            ShowAlarmPart(kant, w);
        }

        /// <summary>Снять все тревоги и подсветку без салюта (рестарт / уход из игры).</summary>
        private void ResetAlarms()
        {
            for (int i = 0; i < AlarmCount; i++)
            {
                _alarmOn[i] = false;
                _alarmWeight[i] = 0f;
                _alarmClock[i] = 0f;
                _sinceScaleInput[i] = 999f;
                PaintAlarm((AlarmScale)i, 0f, 1f);
            }
            _lastReflectCard = _game != null ? _game.CurrentCard : null;
        }

        /// <summary>Отметить ввод игрока ПО ШКАЛЕ — окно §6-триггера салюта. Разводка вводов по шкалам:
        /// датчик высоты → энергия, рычаг отношений → отношения, крутилка → деньги, ответ на карточку →
        /// здоровье (лечиться можно только выбором). Именно эта разводка и отличает «игрок починил» от
        /// «просто сменилась карточка».
        /// ВЫЗЫВАЕТСЯ ТОЛЬКО НА ПРИНЯТОМ ВВОДЕ — <see cref="Game.HandleInput"/> вернул true. «Нажал, но
        /// механика отвергла» (кэп дохода срезал мэшинг; кризис/депрессия заглушили или переназначили
        /// контрол; шкала ещё не открыта) калибровкой не является и окно §6 открывать не должно.
        /// Крутилка и датчик высоты идут своими ветками <see cref="OnInput"/> через перегрузку по шкале —
        /// у них есть ещё и собственный гейт (кэп дохода) перед Game.</summary>
        private void NoteScaleInput(GameInput input)
        {
            switch (input)
            {
                case GameInput.RelationUp:
                case GameInput.RelationDown: NoteScaleInput(AlarmScale.Relations); break;
                case GameInput.AnswerYes:
                case GameInput.AnswerNo: NoteScaleInput(AlarmScale.Health); break;
            }
        }

        /// <summary>Открыть окно §6 по КОНКРЕТНОЙ шкале — точка, куда отмечаются вводы со своей веткой
        /// (принятый кэпом тик крутилки, поднятый датчик высоты).</summary>
        private void NoteScaleInput(AlarmScale s) => _sinceScaleInput[(int)s] = 0f;

        // ================================================================ §6 · САЛЮТ ЗВЁЗД
        // Слой 7 (build-spec §1.3): создаётся ПОСЛЕДНИМ ребёнком канваса и на каждом бёрсте поднимается
        // в конец — салют рисуется поверх всего, включая модалку туториала (которая тоже поднимает себя).

        private void BuildStarLayer(Transform parent)
        {
            _fxLayer = NewGroup("StarFx", parent);
            _starSprite = Sprite("star-burst-v2");
        }

        /// <summary>
        /// §6 «всё сделано верно»: разовый бёрст 4–5 звёзд разного калибра из ЦЕНТРА экрана наружу.
        /// ПУБЛИЧНЫЙ и без аргументов — инкременты 5 (удачный звонок) и 6 (закрытие туториала шкалы)
        /// подключаются к этой же точке. Разлёт детерминирован: сид = <see cref="StarBurstSeedBase"/> +
        /// номер бёрста, поэтому тест и кадр видят один и тот же салют.
        /// </summary>
        public void StarBurst()
        {
            // Праздничный свисто-хлопок конфетти — ЗДЕСЬ, в единственной точке салюта, поэтому все три
            // его повода (поднятый звонок / починенная шкала / выполненный туториал) звучат одинаково.
            if (Audio != null) Audio.Play(SoundEvent.StarBurst);
            if (_fxLayer == null || _starSprite == null) return;
            var rnd = new System.Random(StarBurstSeedBase + _burstCount);
            _burstCount++;
            _fxLayer.transform.SetAsLastSibling();
            int n = StarBurstMin + rnd.Next(StarBurstMax - StarBurstMin + 1);
            float baseAngle = (float)rnd.NextDouble() * 360f;
            for (int k = 0; k < n; k++)
            {
                // Углы «вразнобой, но веером»: равномерный сектор на звезду + джиттер в половину сектора —
                // случайно, но без слипшегося комка в одну сторону.
                float sector = 360f / n;
                float ang = baseAngle + sector * k + ((float)rnd.NextDouble() - 0.5f) * sector * 0.6f;
                float rad = ang * Mathf.Deg2Rad;
                var img = NewSprite("Star", _fxLayer.transform, _starSprite);
                float size = StarBaseSize * StarSizeScales[k % StarSizeScales.Length];
                AnchorPx(img.rectTransform, 960f, 540f, size, size);
                _stars.Add(new Star
                {
                    Rt = img.rectTransform,
                    Img = img,
                    Dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)),
                    Dist = StarTravelMin + (float)rnd.NextDouble() * (StarTravelMax - StarTravelMin),
                    Dur = StarFlightMin + (float)rnd.NextDouble() * (StarFlightMax - StarFlightMin),
                    Spin = (rnd.Next(2) == 0 ? -1f : 1f)
                           * (StarSpinMin + (float)rnd.NextDouble() * (StarSpinMax - StarSpinMin)),
                    T = 0f,
                });
            }
        }

        /// <summary>Продвинуть летящие звёзды на dt: разлёт с замедлением, затухание альфы, лёгкое
        /// вращение — и САМООЧИСТКА (объект уничтожается, как только долетел).</summary>
        private void AdvanceStars(float dt)
        {
            for (int i = _stars.Count - 1; i >= 0; i--)
            {
                var s = _stars[i];
                s.T += dt;
                float u = Mathf.Clamp01(s.T / s.Dur);
                float ease = 1f - (1f - u) * (1f - u);          // out-quad: резкий выброс, мягкий доезд
                s.Rt.anchoredPosition = s.Dir * (s.Dist * ease);
                s.Rt.localRotation = Quaternion.Euler(0f, 0f, s.Spin * ease);
                float a = u <= StarHoldFraction ? 1f : 1f - (u - StarHoldFraction) / (1f - StarHoldFraction);
                s.Img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                if (u >= 1f)
                {
                    if (s.Rt != null) Destroy(s.Rt.gameObject);
                    _stars.RemoveAt(i);
                    continue;
                }
                _stars[i] = s;
            }
        }

        /// <summary>Убрать салют без анимации (рестарт / уход из игры).</summary>
        private void ClearStars()
        {
            for (int i = 0; i < _stars.Count; i++)
                if (_stars[i].Rt != null) Destroy(_stars[i].Rt.gameObject);
            _stars.Clear();
        }

        // Трубка ребёнка (§5b, экран E). ОДИН Image с двумя позами одного рисунка: покой — `phone-rest-v2`
        // (без дуг, торчит из-за левого края), звонок — `phone-ring-v2` (дуги-вибрация запечены), выехавшая
        // внутрь и качающаяся. Группа — ребёнок _gamePanel, созданный ПОСЛЕ карточки и плашек ответа: по
        // build-spec §1.3 звонящая трубка живёт на слое 5 (оверлеи), выше карточки.
        private void BuildChildPhone()
        {
            _childGroup = NewGroup("ChildPhone", _gamePanel.transform);
            _phoneRestSprite = Sprite("phone-rest-v2");
            _phoneRingSprite = Sprite("phone-ring-v2");
            _phoneImg = NewSprite("Phone", _childGroup.transform, _phoneRestSprite);
            _phoneImg.type = Image.Type.Simple;   // never 9-slice: это цельный рисунок, а не плашка
            ApplyPhonePose(0f);
            _childGroup.SetActive(false);
        }

        // ---- S11 ФИНАЛ «ИТОГИ ШОУ»: посадка снята ИНСТРУМЕНТАЛЬНО с `end.png` (PIL) -------------------
        // Решение asset-map §11-12: фон = `end.png` ЦЕЛИКОМ, вместе с запечёнными «ИТОГИ ШОУ» и кремовой
        // плашкой; второй слой плашки НЕ рисуется, заголовок НЕ рисуется. Драйвер кладёт в плашку только
        // текст (исход + некролог) и вешает зелёную CTA под ней.
        //
        // ⚠ ПОСАДКА ФОНА. Эталон/ассет — 2752×1536, это аспект 1.7917, а кадр 16:9 = 1.7778. Значит один
        // общий множитель «эталон ×0.6977» даёт 1920×1071.6 — сверху и снизу остались бы полосы по 4.2 px,
        // сквозь которые светили бы вращающиеся лучи HUD. Поэтому фон садится ПО ЗАПОЛНЕНИЮ (cover):
        //   k = max(1920/2752, 1080/1536) = 0.703125 → спрайт 1935×1080 по центру кадра,
        //   за левый и правый край уходит по 7.75 px (там только кулисы, ни одного смыслового элемента).
        // Расхождение с табличной сверкой ×0.6977 при этом ≤4.7 px по X и ≤2.6 px по Y — внутри допуска
        // ±10 гейта, а полос в кадре нет.
        /// <summary>Множитель посадки эталона финала в кадр (cover, см. блок выше).</summary>
        public const float FinaleBgScale = 0.703125f;
        /// <summary>Размер спрайта `finale-bg-v2` в кадре: 2752×1536 × <see cref="FinaleBgScale"/>.</summary>
        public const float FinaleBgW = 1935f, FinaleBgH = 1080f;

        // ЗАПЕЧЁННОЕ КРЕМОВОЕ ПОЛЕ плашки — залив-заполнением по `end.png`: заливка занимает 498…2251 по X
        // и 405…1259 по Y в файле; через cover-посадку это (cx, cy-от-верха, w, h) ниже. Именно это поле, а
        // не внешний бокс плашки `343,70,1233,816` из asset-map §5.8 (тот снят с загрязнённого bbox: его
        // верх 70 попал на глифы запечённого заголовка, реальный верх кремового поля — 285).
        /// <summary>Кремовое поле запечённой плашки на экране (cx, cy-от-верха, w, h).</summary>
        public static readonly Vector4 FinaleCreamField = new(958.94f, 585.00f, 1232.57f, 600.46f);
        // БЕЗОПАСНЫЙ ТЕКСТОВЫЙ БОКС внутри поля. Поле не прямоугольник: у него скруглённые «срезанные»
        // углы, слева в него вгрызается запечённая звезда (до x≈403 на y 474…556), справа-внизу — ещё две
        // (x 1411…1439 на y 833…865). Бокс ниже — крупнейший прямоугольник по оси поля, который целиком
        // лежит на креме, с запасом от всех трёх выкусов (проверено попиксельно по `end.png`).
        /// <summary>Текстовый бокс внутри кремового поля (cx, cy-от-верха, w, h).</summary>
        public static readonly Vector4 FinaleTextBox = new(959f, 576f, 1058f, 492f);
        // Раскладка внутри бокса повторяет эталон: строка исхода вверху, некролог — ниже.
        //   эталон: исход  y 334…442 (2 строки, шаг 60, кегль ≈56) · некролог y 476…820 (шаг 40, кегль ≈33)
        private static readonly Vector4 FinaleOutcomeRect = new(959f, 391f, 1058f, 122f);   // y 330…452
        // ⚠ БЛОК НЕКРОЛОГА СДВИНУТ НА +20 px ВНИЗ (дизайн-гейт 2026-08-08, MINOR). Было (959, 644, 1058,
        // 356) — y 466…822, центр 644. Блок сидел ВЫШЕ оптического центра доступной области: зазор от
        // строки исхода 41 px против нижнего поля 83 px, и плашка читалась «съехавшей вверх». Доступная
        // область — от низа ректа исхода (452) до низа кремового поля (885.2), её центр 668.6. Новый центр
        // 664 стоит фактически в нём. Двигаем ВЕРХОМ, а не целиком: низ ректа обязан остаться внутри
        // безопасного бокса (<see cref="FinaleTextBox"/>, y 330…822), иначе текст поехал бы на скруглённый
        // угол плашки. Текст в ректе центрирован (MiddleCenter), поэтому сдвиг центра = сдвиг блока.
        //
        // ⚠ ШИРИНА 1058 → 1104 (дизайн-гейт 2026-08-08, MINOR: «финал на длинной колоде мельчит без
        // нужды»). Общий безопасный бокс <see cref="FinaleTextBox"/> ужат до 1058 ради ВСЕХ трёх
        // запечённых звёзд-выкусов сразу, но полосе некролога (y 507…821) мешает только ЛЕВАЯ: правые две
        // (x 1411…1439) лежат на y 833…865, то есть НИЖЕ полосы целиком. Значит для этой полосы ограничение
        // одно — левая звезда, x ≤ 403 на y 474…556. Ректу нельзя левее 403; берём 407 (запас 4 px), то
        // есть полуширину 552 → 1104. См. <see cref="FinaleStoryBox"/>.
        //
        // ⚠ БОЛЬШЕ НЕЛЬЗЯ, хотя крема по бокам ещё много. Разбор гейта предлагал уйти в боковые поля до
        // 33 px от кромки крема (ширина ≈1166) — на этой плашке так не выходит: полоса некролога
        // НАЧИНАЕТСЯ на y 507, а левая звезда кончается на y 556, они перекрываются на первых 49 px, и
        // ПЕРВАЯ строка некролога легла бы прямо на звезду. Потолок ставит звезда, а не кромка крема.
        //
        // ЗАЧЕМ ВООБЩЕ: длиннейшая строка колоды упиралась в ширину и ужималась персонально до кегля 28, а
        // следом тянула вниз и весь блок (кегль 30, интерлиньяж 36 против 39 у эталона; под последней
        // строкой 81 px пустоты). Платить кеглем за ширину, имея 87 px незанятого крема с каждой стороны,
        // незачем. Вместе с честным замером ширины (<see cref="FinaleStorySideMargin"/>) худший случай
        // набирается базовым кеглем 33 — интерлиньяж 39, ровно эталон, — и блок заполняет поле.
        private static readonly Vector4 FinaleStoryRect = new(959f, 664f, 1104f, 314f);     // y 507…821

        /// <summary>
        /// Безопасный бокс ПОЛОСЫ НЕКРОЛОГА — шире общего <see cref="FinaleTextBox"/>, потому что из трёх
        /// звёзд-выкусов в эту полосу вгрызается только левая (x ≤ 403), а правые две лежат ниже неё.
        /// Левый край 407 — те самые 403 плюс 4 px запаса. Приёмка та же, что была, только звеньев два:
        /// рект некролога ⊆ этот бокс ⊆ кремовое поле, и бокс не заходит на выкусы.
        /// </summary>
        public static readonly Vector4 FinaleStoryBox = new(959f, 664f, 1104f, 314f);
        /// <summary>Кегли строки исхода (Arimo Bold): верх — с эталона, низ — предел ужатия.</summary>
        public const int FinaleOutcomeMaxSize = 56, FinaleOutcomeMinSize = 34;
        /// <summary>
        /// Кегли некролога (Rubik). Верх 34 — кегль эталона. НИЗ 24 — задокументированный порог
        /// читаемости: длиннейший реальный некролог колоды (лимит <see cref="Necrolog.MaxLines"/> строк,
        /// самые длинные строки CSV) садится в бокс с запасом над полом; ниже 24 px на кабинетном экране
        /// текст перестаёт читаться, и упор в пол здесь означал бы не «ужали», а «контент перерос плашку» —
        /// тогда режется лимит строк, а не кегль. Пол общий: и для блока, и для ОТДЕЛЬНОЙ строки
        /// (<see cref="FitStoryPerLine"/>).
        /// </summary>
        public const int FinaleStoryMaxSize = 34, FinaleStoryMinSize = 24;

        // CTA рестарта. На эталоне кнопки НЕТ (как и на опенере) — место выбрано по композиции: плашка
        // кончается на y≈929.5 (внешний синий кант, замер по `end.png`), ниже до края кадра 150 px чистой
        // сцены. CTA встаёт по центру этой полосы (центр y 1005), соосно плашке, с полем 17.5 px сверху
        // до плашки и 17 px снизу до края. Пропорции — ровно как у CTA опенера (кант 116 / плашка 104).
        private static readonly Vector4 FinaleCtaRect = new(959f, 1005f, 860f, 104f);
        private static readonly Vector4 FinaleCtaEdgeRect = new(959f, 1005f, 872f, 116f);
        /// <summary>Низ запечённой плашки (внешний синий кант) на экране — CTA обязана быть НИЖЕ.</summary>
        public const float FinalePlateBottom = 929.5f;

        /// <summary>CTA финала — называет ФИЗИЧЕСКИЙ контрол, ОДНОЙ строкой через «—», ровно как опенер
        /// (<see cref="OpenerStartHintText"/>): раньше здесь стоял перенос строки вместо тире, и одна и та
        /// же формула управления печаталась в игре двумя разными способами.</summary>
        public const string FinaleRestartHintText = "НАЧАТЬ ЗАНОВО — ЖМИ ЗЕЛЁНУЮ";

        private void BuildFinale(Transform parent)
        {
            _finalePanel = NewGroup("Finale", parent);

            // (1) Фон — `end.png` целиком (кулисы, софит, запечённые «ИТОГИ ШОУ» и кремовая плашка).
            // ПЕРВЫМ ребёнком панели: он же перекрывает общий вращающийся санбёрст HUD, поэтому на финале
            // фон статичный, как и требует спек §4-H.
            _finaleBg = NewSprite("FinaleBg", _finalePanel.transform, Sprite("finale-bg-v2"));
            _finaleBg.type = Image.Type.Simple;
            var brt = _finaleBg.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(FinaleBgW, FinaleBgH);

            // (2) Строка исхода — В запечённую плашку, вверх её кремового поля. Arimo Bold, INK
            // (канон §12-4), БЕЗ DisplayFx: тёмная обводка по тёмным глифам на светлом креме только
            // мажет их в кляксу (та же причина, что у вопроса карточки).
            _finaleOutcome = NewText("FinaleOutcome", _finalePanel.transform,
                "", FinaleOutcomeMaxSize, TextAnchor.UpperCenter, Ink, _display);
            AnchorPx(_finaleOutcome.rectTransform, FinaleOutcomeRect.x, FinaleOutcomeRect.y,
                FinaleOutcomeRect.z, FinaleOutcomeRect.w);
            _finaleOutcome.resizeTextForBestFit = true;
            _finaleOutcome.resizeTextMinSize = FinaleOutcomeMinSize;
            _finaleOutcome.resizeTextMaxSize = FinaleOutcomeMaxSize;
            _finaleOutcome.verticalOverflow = VerticalWrapMode.Truncate;   // best-fit честно держит и ВЫСОТУ

            // (3) Некролог — туда же, под строкой исхода. Rubik, INK, по центру своего бокса (короткая
            // FATAL-история встаёт в середину поля, а не жмётся к строке исхода).
            _finaleStory = NewText("FinaleStory", _finalePanel.transform,
                "", FinaleStoryMaxSize, TextAnchor.MiddleCenter, Ink, _body);
            AnchorPx(_finaleStory.rectTransform, FinaleStoryRect.x, FinaleStoryRect.y,
                FinaleStoryRect.z, FinaleStoryRect.w);
            // Кегль подбирает НЕ uGUI-best-fit, а FitStoryPerLine: блочный best-fit меряет только высоту и
            // потому спокойно переносит слишком широкую строку (сирота в середине блока, дизайн-гейт
            // 2026-08-08). Границы кеглей те же — они и есть вход подборщика.
            _finaleStory.resizeTextForBestFit = false;
            _finaleStory.resizeTextMinSize = FinaleStoryMinSize;
            _finaleStory.resizeTextMaxSize = FinaleStoryMaxSize;
            _finaleStory.supportRichText = true;    // персональный `<size=k>` на слишком широкой строке
            _finaleStory.verticalOverflow = VerticalWrapMode.Truncate;

            // (4) Restart CTA. The plate IS the green cabinet button its label names, so its fill must be the
            // GREEN token #05CE51 — `plate-yes` carries its own paler art green (#5CBF5F), and a uGUI tint
            // only ever MULTIPLIES, so no tint on that sprite can reach the token (design gate 2026-07-31:
            // «зелёный финала бледнее опенера»). Built exactly like the opener CTA instead — dark Ink rim +
            // `bar-track` tinted through OnBarTrack(GoGreen) — so both confirm CTAs render the SAME token.
            var againEdge = NewSprite("AgainPlateEdge", _finalePanel.transform, Sprite("bar-track"));
            againEdge.type = Image.Type.Sliced;
            againEdge.color = OnBarTrack(Ink);
            AnchorPx(againEdge.rectTransform, FinaleCtaEdgeRect.x, FinaleCtaEdgeRect.y,
                FinaleCtaEdgeRect.z, FinaleCtaEdgeRect.w);

            var again = NewSprite("AgainPlate", _finalePanel.transform, Sprite("bar-track"));
            again.type = Image.Type.Sliced;
            again.color = OnBarTrack(GoGreen);
            AnchorPx(again.rectTransform, FinaleCtaRect.x, FinaleCtaRect.y, FinaleCtaRect.z, FinaleCtaRect.w);
            var againText = NewText("AgainText", again.transform,
                FinaleRestartHintText, 46, TextAnchor.MiddleCenter, Ink, _display);
            Inset(againText.rectTransform, 26f);   // ≥ видимого скругления bar-track (16) → глифы всегда на плашке
            againText.resizeTextForBestFit = true; againText.resizeTextMinSize = 28; againText.resizeTextMaxSize = 46;
            againText.verticalOverflow = VerticalWrapMode.Truncate;
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
            _tutorialText = NewText("TutBody", modal.transform, "", 40, TextAnchor.MiddleCenter, Ink, _body);
            var trt = _tutorialText.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.34f); trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(120f, 20f); trt.offsetMax = new Vector2(-120f, -100f);
            _tutorialText.resizeTextForBestFit = true; _tutorialText.resizeTextMinSize = 26; _tutorialText.resizeTextMaxSize = 44;

            // Вторая строка подсказки — клавиша эмуляции, мелко и приглушённо, между телом и кнопкой.
            _tutHintLine = NewText("TutKeyHint", modal.transform, "", HintTextSize,
                TextAnchor.MiddleCenter, HintInk, _body);
            var thrt = _tutHintLine.rectTransform;
            thrt.anchorMin = new Vector2(0f, 0.255f); thrt.anchorMax = new Vector2(1f, 0.33f);
            thrt.offsetMin = new Vector2(120f, 0f); thrt.offsetMax = new Vector2(-120f, 0f);
            _tutHintLine.resizeTextForBestFit = true;
            _tutHintLine.resizeTextMinSize = 16; _tutHintLine.resizeTextMaxSize = HintTextSize;
            _tutHintLine.verticalOverflow = VerticalWrapMode.Truncate;

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

        /// <summary>
        /// §D «Экран появления новой шкалы»: затемнение ~40 % INK поверх геймплея, слот под КРУПНУЮ копию
        /// открываемой шкалы, окно-рассказ (облачко Ведущего, зеркальное) и окно-задача (`task-plate-v2`).
        /// Порядок детей = z-порядок: затемнение → плашка-задача → её текст → прогресс → облачко → его
        /// текст → слот виджета. Крупная шкала рисуется ПОВЕРХ окон — ровно как на эталоне, где батарея
        /// перекрывает левые лучи плашки. Строится скрытым.
        /// </summary>
        private void BuildNewScaleOverlay(Transform parent)
        {
            _nsOverlay = NewGroup("NewScaleOverlay", parent);
            _nsFade = _nsOverlay.AddComponent<CanvasGroup>();
            _nsFade.interactable = false; _nsFade.blocksRaycasts = false;

            _nsDim = NewSolid("NewScaleDim", _nsOverlay.transform, new Color(Ink.r, Ink.g, Ink.b, NewScaleDimAlpha));
            Stretch(_nsDim.rectTransform);

            // Окно-задача. `ui_warning_v2` НЕЛЬЗЯ резать 9-slice (asset-map §5/§1.1: звёзды-лучи по периметру),
            // поэтому Simple и один фиксированный размер = исходник ×0.995 (замер с эталона).
            _nsPlate = NewSprite("TaskPlate", _nsOverlay.transform, Sprite("task-plate-v2"));
            _nsPlate.type = Image.Type.Simple;
            AnchorPx(_nsPlate.rectTransform, TaskPlateRect.x, TaskPlateRect.y, TaskPlateRect.z, TaskPlateRect.w);

            // Текст задачи — отдельный ребёнок ОВЕРЛЕЯ (не плашки): плашка стоит ровно, а поле кремовое
            // запечено со смещением, поэтому текст сажаем по ЗАМЕРУ поля, а не по долям спрайта.
            _nsTaskText = NewText("TaskText", _nsOverlay.transform, EnergyTaskText, TaskTextMaxSize,
                TextAnchor.MiddleCenter, Ink, _display);
            AnchorPx(_nsTaskText.rectTransform, TaskFieldRect.x, TaskFieldRect.y - HintLineReserve / 2f,
                TaskFieldRect.z - 2f * TaskTextPadX, TaskFieldRect.w - 2f * TaskTextPadY - HintLineReserve);
            _nsTaskText.resizeTextForBestFit = true;
            _nsTaskText.resizeTextMinSize = TaskTextMinSize;
            _nsTaskText.resizeTextMaxSize = TaskTextMaxSize;
            _nsTaskText.verticalOverflow = VerticalWrapMode.Truncate;

            // Вторая строка: мелко, Rubik, приглушённым — она служебная и не должна спорить с задачей.
            // Полоса между низом ужатого текста задачи (≈710) и дорожкой удержания (776…796).
            // Приглушённый ЧЕРНИЛЬНЫЙ, а не холодный голубой: строка лежит на КРЕМОВОМ поле, и
            // #9fb0e8 (прежний токен Muted, ушёл вместе с тусклым баннером) на нём почти не читается —
            // проверено по кадру. Полупрозрачный Ink держит и «служебность», и контраст.
            _nsHintLine = NewText("TaskKeyHint", _nsOverlay.transform, "", HintTextSize,
                TextAnchor.MiddleCenter, HintInk, _body);
            AnchorPx(_nsHintLine.rectTransform, TaskFieldRect.x, HintLineCy,
                TaskFieldRect.z - 2f * TaskTextPadX, 34f);
            _nsHintLine.resizeTextForBestFit = true;
            _nsHintLine.resizeTextMinSize = 16;
            _nsHintLine.resizeTextMaxSize = HintTextSize;
            _nsHintLine.verticalOverflow = VerticalWrapMode.Truncate;

            // ⚠ ЗДЕСЬ БЫЛА ПОЛОСКА ПРОГРЕССА УДЕРЖАНИЯ. Снята 2026-08-07 по слову основательницы («убрать
            // полосу прогресса в туториалах»): прогресс на этих экранах показывает САМА шкала — батарея
            // наполняется, монетки капают в банку, маркер стоит в зоне. Ничего вместо неё не строится.

            // Окно-рассказ: тот же спрайт облачка, но ОТРАЖЁННЫЙ — на эталоне рупор смотрит ВПРАВО-ВНИЗ
            // (на экране C он слева). Текст — отдельный ребёнок оверлея, поэтому отражение его не касается.
            _nsBubble = NewSprite("StoryBubble", _nsOverlay.transform, Sprite("host-comment-v2"));
            _nsBubble.type = Image.Type.Simple;
            AnchorPx(_nsBubble.rectTransform, StoryBubbleRect.x, StoryBubbleRect.y, StoryBubbleRect.z, StoryBubbleRect.w);
            _nsBubble.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            _nsStoryText = NewText("StoryText", _nsOverlay.transform, EnergyStoryText, StoryTextMaxSize,
                TextAnchor.MiddleCenter, Ink, _bodyBold);
            AnchorPx(_nsStoryText.rectTransform, StoryFieldRect.x, StoryFieldRect.y,
                StoryFieldRect.z - 2f * StoryTextPadX, StoryFieldRect.w - 2f * StoryTextPadY);
            _nsStoryText.resizeTextForBestFit = true;
            _nsStoryText.resizeTextMinSize = StoryTextMinSize;
            _nsStoryText.resizeTextMaxSize = StoryTextMaxSize;
            _nsStoryText.verticalOverflow = VerticalWrapMode.Truncate;

            // ---- ЗЕЛЁНАЯ CTA входного экрана спецрежима (r3) ----------------------------------------
            // Собрана ТЕМ ЖЕ блоком, что CTA опенера и финала: тёмный кант `bar-track` + сама плашка
            // `bar-track`, покрашенная токеном GoGreen через OnBarTrack, + Arimo Bold чернилами БЕЗ
            // DisplayFx. Значит все три «жми зелёную» кабинета рендерятся одним и тем же зелёным #05CE51,
            // а не тремя похожими. Скрыта: §D-модалка кнопками не закрывается и CTA не показывает.
            _smCtaEdge = NewSprite("SpecialCtaEdge", _nsOverlay.transform, Sprite("bar-track")).gameObject;
            var smEdgeImg = _smCtaEdge.GetComponent<Image>();
            smEdgeImg.type = Image.Type.Sliced;
            smEdgeImg.color = OnBarTrack(Ink);
            AnchorPx(smEdgeImg.rectTransform, SpecialCtaEdgeRect.x, SpecialCtaEdgeRect.y,
                SpecialCtaEdgeRect.z, SpecialCtaEdgeRect.w);

            _smCta = NewSprite("SpecialCta", _nsOverlay.transform, Sprite("bar-track"));
            _smCta.type = Image.Type.Sliced;
            _smCta.color = OnBarTrack(GoGreen);
            AnchorPx(_smCta.rectTransform, SpecialCtaRect.x, SpecialCtaRect.y, SpecialCtaRect.z, SpecialCtaRect.w);
            _smCtaText = NewText("SpecialCtaText", _smCta.transform,
                SpecialModeCtaText, 46, TextAnchor.MiddleCenter, Ink, _display);
            Inset(_smCtaText.rectTransform, 26f);   // ≥ видимого скругления bar-track (16)
            _smCtaText.resizeTextForBestFit = true;
            _smCtaText.resizeTextMinSize = 28; _smCtaText.resizeTextMaxSize = 46;
            _smCtaText.verticalOverflow = VerticalWrapMode.Truncate;
            _smCtaEdge.SetActive(false);
            _smCta.gameObject.SetActive(false);

            // Слот КРУПНОЙ шкалы — последний ребёнок: одолженный виджет рисуется поверх обоих окон.
            _nsSlot = NewGroup("BigScaleSlot", _nsOverlay.transform);

            _nsOverlay.SetActive(false);
        }

        /// <summary>
        /// Раскладка окна-задачи. На §D-модалке в нижней полосе кремового поля стоит одна служебная строка
        /// (клавиша эмуляции); на входном экране спецрежима под ней стоит ещё и зелёная CTA, поэтому текст
        /// задачи ужимается сильнее, а служебная строка поднимается. Резерв ПОСТОЯННЫЙ для своего режима,
        /// чтобы композиция не прыгала от наличия плат.
        /// </summary>
        /// <summary>
        /// Сторона облачка-рассказа. Справа — канон §D (и все экраны, кроме здоровья); слева — рокировка
        /// под КРУПНЫЙ БАР ЗДОРОВЬЯ, которому правый верх нужен как своё HUD-место (см.
        /// <see cref="StoryBubbleLeftRect"/>). Зеркалится и сам спрайт (localScale), и посадка текста.
        /// </summary>
        private void LayoutStoryBubble(bool left)
        {
            var b = left ? StoryBubbleLeftRect : StoryBubbleRect;
            var f = left ? StoryFieldLeftRect : StoryFieldRect;
            AnchorPx(_nsBubble.rectTransform, b.x, b.y, b.z, b.w);
            _nsBubble.rectTransform.localScale = new Vector3(left ? 1f : -1f, 1f, 1f);
            AnchorPx(_nsStoryText.rectTransform, f.x, f.y,
                f.z - 2f * StoryTextPadX, f.w - 2f * StoryTextPadY);
        }

        private void LayoutTaskWindow(bool special)
        {
            float reserve = special ? SpecialTaskReserve : HintLineReserve;
            AnchorPx(_nsTaskText.rectTransform, TaskFieldRect.x, TaskFieldRect.y - reserve / 2f,
                TaskFieldRect.z - 2f * TaskTextPadX, TaskFieldRect.w - 2f * TaskTextPadY - reserve);
            AnchorPx(_nsHintLine.rectTransform, TaskFieldRect.x, special ? SpecialHintLineCy : HintLineCy,
                TaskFieldRect.z - 2f * TaskTextPadX, 34f);
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

            // ПОДСКАЗКА КОНТРОЛА (S8 + r3 п.3в): на тёмной плашке НАД кнопкой, чтобы читалась (жалоба
            // основательницы — серое по серому было не видно). Текст называет КОНТРОЛ, а не только ритм:
            // «лови пульс — жми зелёную» вместо безадресного «нажми в такт пульсу». Имя контрола берётся
            // из DepressionCatchControlName — той же константы, что и в задаче входного экрана (п.3г).
            var hintPlate = NewSprite("DepHintPlate", _depressionGroup.transform, Sprite("bar-track"));
            hintPlate.type = Image.Type.Sliced;
            hintPlate.color = new Color(0.08f, 0.08f, 0.10f, 0.96f);
            AnchorPx(hintPlate.rectTransform, 960f, 866f, 640f, 100f);
            _depHint = NewText("DepHint", hintPlate.transform,
                DepressionBoardHint, 34, TextAnchor.MiddleCenter, new Color(0.96f, 0.96f, 0.98f), _display);
            Inset(_depHint.rectTransform, 24f);
            _depHint.resizeTextForBestFit = true; _depHint.resizeTextMinSize = 20; _depHint.resizeTextMaxSize = 34;

            // …и вторая строка — КЛАВИША при эмуляции, ровно тем же путём (ArcadeInput.KeyHint), что и на
            // окнах-подсказках. На стойке её нет: там контрол ведёт настоящая плата.
            _depKeyHint = NewText("DepKeyHint", _depressionGroup.transform, "", HintTextSize,
                TextAnchor.MiddleCenter, new Color(0.86f, 0.86f, 0.92f), _body);
            AnchorPx(_depKeyHint.rectTransform, 960f, 806f, 640f, 40f);
            _depKeyHint.resizeTextForBestFit = true;
            _depKeyHint.resizeTextMinSize = 16; _depKeyHint.resizeTextMaxSize = HintTextSize;
            _depKeyHint.verticalOverflow = VerticalWrapMode.Truncate;
            _depKeyHint.gameObject.SetActive(false);

            // BIG breathing pulse indicator (S8 playtest rework): a large star that is ALWAYS visible during
            // depression and BLINKS on the steady beat — bright + scaled-up flash while the hit-window is open
            // («жми!»), dim-but-visible resting between (never fully gone), so the player sees the rhythm and taps
            // in time. Driven every frame in ReflectDepression. High contrast on the gray wash. (Was a faint 150px
            // dot shown ONLY on the ~0.6s window — «вообще не видно, как дышать».)
            // r3 (п.3в): индикатор ЗАМЕТНЕЕ — 280 → 360 px и выше пол яркости в покое (0.32 → 0.42).
            // Он и был «дышащим», но на 4K-стойке 280 px по центру читались как невнятное пятно, а покой
            // на 0.32 сливался с серой мойкой.
            _depressionPulse = NewSprite("DepressionPulse", _depressionGroup.transform, Sprite("star-white"));
            _depressionPulse.color = new Color(0.92f, 0.92f, 0.98f, 0.42f);
            Anchor(_depressionPulse.rectTransform, new Vector2(0.5f, 0.46f), new Vector2(360, 360));
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

        // Depression started (CR09): the muted «ТЁМНАЯ ПОЛОСА…» announce (now in the host bubble — the
        // rubric band it used to ride on was removed 2026-08-05) + reset the mutter cycle.
        private void OnDepressionStarted()
        {
            if (Audio != null)
            {
                // ★ ГЛАВНЫЙ ПРИЁМ. Обратная тарелка играет вход, и ЭТИМ ЖЕ мгновением весь микс
                // (включая музыку) разом уходит в вату — 500 Гц / −9 дБ, ступень 0.
                Audio.Play(SoundEvent.DepressionEnter);
                Audio.EnterDepression();
                _audioDepPulsing = false;
            }
            _depMutterCount = 0;
            _bubbleTimer.Show(HostContent.DepressionAnnounce);
            ShowSpecialMode(SpecialMode.Depression);   // r3: правила ловли — ДО того, как начнёт капать серость
        }

        // Each successful catch: a muted host mutter as a step of colour returns.
        private void OnDepressionProgressed()
        {
            // У ПОПАДАНИЯ нет своего клипа — и не должно быть: его звук в том, что мир возвращается
            // на ступень. Game считает ОСТАВШУЮСЯ серость, спека — НАБРАННЫЕ попадания; переводит
            // DepressionMix.HitsFromGray, чтобы направление не путалось.
            if (Audio != null) Audio.SetDepressionHits(DepressionMix.HitsFromGray(_game.DepressionGray));
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
                float a = 0.42f + 0.08f * Mathf.Sin(Time.time * 3f);            // dim but visible resting breath
                _depressionPulse.color = new Color(0.90f, 0.90f, 0.97f, a);
                _depressionPulse.rectTransform.localScale = Vector3.one * (0.86f + 0.03f * Mathf.Sin(Time.time * 3f));
            }
        }

        // Четыре OPEN-открытия ведут МОДАЛЬНЫЙ экран §D (не S5-подсказку): он закрывается только
        // выполнением условия по реальному контролу. Здоровье (30) осталось на S5.
        private void OnMoneyOpened()  => ShowNewScale(NewScale.Money);
        /// <summary>
        /// Открытие отношений приходит ДВАЖДЫ за жизнь, если сыгран ВТОРОЙ ШАНС (MD06): Game снимает
        /// RelationshipsLost и заново зовёт CheckRelationshipsOpen. Первый раз — туториал (его «туш»
        /// поднимает ShowNewScale), второй — «шкала открылась заново», и это ровно строка манифеста
        /// «Второй шанс»: четыре ноты калимбы вверх. Различаем по уже взведённому `_nsSeen`.
        /// </summary>
        private void OnRelationshipsOpened()
        {
            if (Audio != null && _nsSeen[(int)NewScale.Relations]) Audio.Play(SoundEvent.SecondChance);
            ShowNewScale(NewScale.Relations);
        }
        private void OnEnergyOpened() => ShowNewScale(NewScale.Energy);
        // ЗДОРОВЬЕ (30) и ВЫГОРАНИЕ переехали со старой жёлтой S5-подсказки на ВХОДНОЙ ЭКРАН спецрежима
        // (r3, живой плейтест 2026-08-07): «жёлтая плашка с ПОНЯТНО выпадает из арт-пака». Одноразовость
        // за жизнь сохранена — только флаг теперь свой (_smHealthSeen / _smBurnoutSeen).
        private void OnHealthOpened()
        {
            // «Здоровье тает» — глухой колокол, один раз за жизнь. Общего «туша» спецрежимам не даём:
            // у каждого входа свой голос из манифеста, иначе четыре разных экрана звучали бы одинаково.
            if (Audio != null) Audio.Play(SoundEvent.HealthOpen);
            if (!_smHealthSeen) { _smHealthSeen = true; ShowSpecialMode(SpecialMode.Health); }
        }
        // ВЫГОРАНИЕ: первый раз за жизнь — входной экран с ПАУЗОЙ дренажа (умереть, читая правила, нельзя);
        // повторные — короткая плашка `_burnoutPlate` без блокировки, она живёт off Game.Burnout в Update.
        private void OnBurnoutEntered()
        {
            // Резкий провал/глушение — на КАЖДОЕ выгорание, а не только на первое: экран одноразовый,
            // а состояние возвращается, и игрок обязан слышать, что оно вернулось.
            if (Audio != null) Audio.Play(SoundEvent.BurnoutIn);
            if (!_smBurnoutSeen) { _smBurnoutSeen = true; ShowSpecialMode(SpecialMode.Burnout); }
        }
        // MD02=ДА opened the child scale: the «ПОПОЛНЕНИЕ!» rubric banner already fired when MD02 was drawn;
        // this sequences the §D modal after the answer (one-shot per life, pauses like every other open).
        private void OnChildOpened()  => ShowNewScale(NewScale.Child);

        // Breakup: flash the transient «РАССТАЛИСЬ» plate (auto-hides on its own ~2s clock, reflected in
        // Update). The balancer HUD hides itself off Game.RelationshipsLost on the next ApplyAgeGates.
        private void OnRelationshipBrokeUp()
        {
            // Битое стекло + калимба вниз. «Ооох» зала тут НЕТ и не будет: зал снят целиком, а в CC0
            // такого звука всё равно не нашлось ни на одном источнике (манифест, «Известная дыра»).
            if (Audio != null) Audio.Play(SoundEvent.Breakup);
            _breakupTimer.Show("РАССТАЛИСЬ");
        }

        // Счёт ушёл в минус (отрезок 0, §3.2): Ведущий это КОММЕНТИРУЕТ, а не только пилюля краснеет.
        // Реплики идут по кругу пула, а не случайно, — за жизнь их слышно один-два раза, и повтор подряд
        // читался бы как заедание.
        private int _debtLineCount;
        private void OnDebtEntered()
        {
            _debtLineCount++;
            _bubbleTimer.Show(HostContent.DebtLineFor(_debtLineCount));
            // Долг — тоже реплика Ведущего, значит и он звучит пиццикато. Своего файла у пула `debt`
            // нет: манифест назвал шесть стингеров по шести тонам, а `debt` появился позже — берёт
            // голос «осторожность» (см. AudioCatalog.ForHostTone).
            if (Audio != null) Audio.Play(AudioCatalog.ForHostTone(HostTone.Debt));
        }

        // Shared S5 hint: pauses the game (freezes age, drains, cost-of-living, decay and the card timer)
        // and shows the modal. The one-shot «seen» flag is set at show time (the hint always resolves via
        // dismiss). Opens don't collide — each pauses until dismissed — so a stacked show is simply skipped.
        private void ShowTutorial(string text, ref bool seen, ArcadeControlId? control = null)
        {
            if (_tutorialShowing) return;
            seen = true;
            _tutorialShowing = true;
            _tutHintControl = control;
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

        // ================================================================ §D — экран появления новой шкалы

        /// <summary>Канон-рассказ Ведущего для шкалы (host-content §4).</summary>
        public static string NewScaleStory(NewScale s) => s switch
        {
            NewScale.Money => MoneyStoryText,
            NewScale.Relations => RelationsStoryText,
            NewScale.Energy => EnergyStoryText,
            NewScale.Child => ChildStoryText,
            _ => "",
        };

        /// <summary>Канон-задача для шкалы (host-content §4).</summary>
        public static string NewScaleTask(NewScale s) => s switch
        {
            NewScale.Money => MoneyTaskText,
            NewScale.Relations => RelationsTaskText,
            NewScale.Energy => EnergyTaskText,
            NewScale.Child => ChildTaskText,
            _ => "",
        };

        // Поднять модальный экран новой шкалы. Один раз за жизнь на шкалу. Не встаёт поверх S5-подсказки
        // (здоровье/выгорание) и поверх самой себя — иначе два открытия в одном кадре подрались бы за паузу.
        private void ShowNewScale(NewScale which)
        {
            if (which == NewScale.None) return;
            if (_nsSeen[(int)which]) return;

            // ⚠ ЭКРАН ПОВЕРХ ЭКРАНА — НЕЛЬЗЯ. Открытие не теряется, а ОТКЛАДЫВАЕТСЯ (`_nsSeen` не
            // помечаем, ставим `_nsPending`), и TickNewScale поднимет его, как только место освободится.
            //
            // Историческая причина этой развилки была уже: детская модалка требует ПОДНЯТЬ ЗВОНОК, а под
            // полноэкранной плашкой выгорания S7 трубка была спрятана и окно заморожено — модалка встала
            // бы НЕВЫПОЛНИМОЙ. Плашка S7 снята r3 (2026-08-07): выгорание объясняет входной экран, а
            // повторные показывают короткую плашку без блокировки, трубку никто не накрывает, и
            // Game.ChildCallFrozen больше не смотрит на Burnout. Так что теперь конфликт ровно один и
            // общий — ЧУЖОЕ ОКНО СВЕРХУ (входной экран спецрежима или S5-подсказка), и обрабатывается он
            // тем же откладыванием.
            if (_nsShowing || _tutorialShowing || _smShowing)
            {
                _nsPending = which;
                return;
            }

            _nsSeen[(int)which] = true;
            _nsPending = NewScale.None;
            // ТУТОРИАЛ: «внимание, новая рубрика!» — туш на ОТКРЫТИИ экрана. Тот же туш звучит и на
            // ВЫПОЛНЕНИИ условия (CloseNewScale) — манифест даёт им одну строку и один файл.
            if (Audio != null) Audio.Play(SoundEvent.Tutorial);

            _nsWhich = which;
            _nsShowing = true;
            _nsDone = false;
            _nsArmed = false;
            _nsHold = 0f;
            _nsTicks = 0;
            _nsFadeT = 0f;
            _nsStoryText.text = NewScaleStory(which);
            _nsTaskText.text = NewScaleTask(which);
            LayoutStoryBubble(left: false);   // §D всегда канонический: облачко справа
            BorrowBigWidget(which);
            _nsFade.alpha = 1f;
            _nsOverlay.transform.SetAsLastSibling();
            _nsOverlay.SetActive(true);
            SyncPause();
            ReflectDomeUnderModal();   // купол уходит ЭТИМ же кадром (иначе «культя» мигала бы один кадр)

            // Ребёнок: условие — ПОДНЯТЬ ЗВОНОК, поэтому звонок заводится принудительно (обычный планировщик
            // 15–25 с под замороженным временем не сработал бы никогда). Трубка выезжает и звонит, пока не
            // поднимут (окно не истекает: IntegrateChild под паузой не крутится).
            if (which == NewScale.Child) _game.RingChildNow();
        }

        /// <summary>
        /// Такт модалки: условие выхода → фейд 0.2 с → салют → снятие паузы. Условие проверяется ТОЛЬКО по
        /// живому состоянию механики — фальшивых «нажал ок» здесь нет. Четыре разных условия:
        /// <list type="bullet">
        /// <item>ДЕНЬГИ — <see cref="MoneyTutorialTicks"/> ПРИНЯТЫХ тиков крутилки (2026-08-07: одного тика
        /// было мало, окно исчезало быстрее, чем игрок понимал, что случилось);</item>
        /// <item>ЭНЕРГИЯ — датчик реально поднимали И батарея ЗАПОЛНИЛАСЬ (≥<see cref="NewScaleEnergyFull"/> %).
        /// Шкала открывается на <see cref="Game.EnergyOpenValue"/> %, а наполнить её можно ТОЛЬКО поднятым
        /// датчиком, так что в живой игре «взвод» выполняется сам собой — он страхует поднятие экрана
        /// снаружи (поза-снимок / Layer-2-сид) при уже полной батарее;</item>
        /// <item>ОТНОШЕНИЯ — маркер в зоне 40–75 непрерывно <see cref="NewScaleHoldSeconds"/> с. Единственная
        /// шкала, которой «взвод» ещё нужен: она открывается на 55 %, то есть УЖЕ в зоне, и без флага
        /// «реальный ввод был» окно ушло бы само через 1.5 с, не потребовав ничего;</item>
        /// <item>РЕБЁНОК — один ПОДНЯТЫЙ звонок (взвод и есть условие).</item>
        /// </list>
        /// </summary>
        private void TickNewScale(float dt)
        {
            PumpPendingScreens();   // ретрай тем же тактом (см. PumpPendingScreens — порядок задаёт Update)
            if (!_nsShowing) return;

            if (_nsDone)
            {
                _nsFadeT += dt;
                _nsFade.alpha = Mathf.Clamp01(1f - _nsFadeT / NewScaleFadeSeconds);
                if (_nsFadeT >= NewScaleFadeSeconds) CloseNewScale(reward: true);
                return;
            }

            bool inMode;
            switch (_nsWhich)
            {
                case NewScale.Money:                    // N ПРИНЯТЫХ тиков крутилки (кэп + Game.Crank)
                    inMode = _nsTicks >= MoneyTutorialTicks;
                    _nsHold = inMode ? NewScaleHoldSeconds : 0f;   // мгновенное условие — без удержания
                    break;
                case NewScale.Child:                    // один ПОДНЯТЫЙ звонок
                    inMode = _nsArmed;
                    _nsHold = inMode ? NewScaleHoldSeconds : 0f;
                    break;
                case NewScale.Energy:                   // «держи, пока батарейка не заполнится»
                    // «Взвод» здесь не про механику, а про ЧЕСТНОСТЬ экрана: условие обязано быть
                    // выполнено РУКАМИ игрока. В живой игре шкала открывается на 20 % и наполнить её
                    // нечем, кроме поднятого датчика, — но экран умеют поднимать и поза-снимок, и
                    // Layer-2-сид (DebugShowNewScale) при полной батарее, и без флага он ушёл бы сам,
                    // не дождавшись ни одного касания.
                    inMode = _nsArmed && _game.Scales.Energy >= NewScaleEnergyFull;
                    _nsHold = inMode ? NewScaleHoldSeconds : 0f;
                    break;
                case NewScale.Relations:
                    float r = _game.Scales.Relationships;
                    inMode = _nsArmed && r >= Game.RelZoneMin && r <= Game.RelZoneMax;
                    _nsHold = inMode ? _nsHold + dt : 0f;
                    break;
                default:
                    inMode = false;
                    break;
            }

            if (inMode && _nsHold >= NewScaleHoldSeconds) _nsDone = true;   // → фейд со следующего такта
        }

        /// <summary>
        /// ⚠ ПОРЯДОК ТИКА КАДРА (находка ревью r3, MAJOR). Отложенные экраны — §D-модалка шкалы
        /// (<see cref="_nsPending"/>) и входной экран спецрежима (<see cref="_smPending"/>) — поднимаются
        /// ПЕРЕД <see cref="Game.Tick"/>, а не после него.
        ///
        /// Причина. Окно, из-за которого экран отложился, снимается в КОНЦЕ кадра: §D-модалка — фейдом в
        /// <see cref="TickNewScale"/>, S5-подсказка — зелёной в <see cref="DismissTutorial"/>. Оба снимают
        /// паузу и НЕ поднимают очередь сами. Если пампить очередь только следующим <c>TickNewScale</c>
        /// (то есть ПОСЛЕ <c>_game.Tick</c>), между снятием помехи и подъёмом отложенного экрана проходит
        /// РОВНО ОДИН ЖИВОЙ ТИК ИГРЫ. Для выгорания это прямо запрещённый случай (экран обязан вставать
        /// С ПАУЗОЙ — «умереть, читая правила, нельзя»), для депрессии — тик серости до объяснения ловли.
        /// Пампим до тика — и живых тиков без уже поднятого экрана не остаётся ни одного.
        ///
        /// Идемпотентен: обе половины выходят сразу, если очередь пуста, поэтому его же зовёт
        /// <see cref="TickNewScale"/> (ретрай каждый такт: помеха может уйти не в свой кадр, а очередь из
        /// двух окон должна разбираться по одному).
        /// </summary>
        private void PumpPendingScreens()
        {
            PumpPendingNewScale();
            PumpPendingSpecialMode();
        }

        // Отложенный OPEN (см. ShowNewScale): держим его, пока помеха не снята, и поднимаем экран ТЕМ ЖЕ
        // путём — с теми же гейтами `_nsSeen`/подсказки и с тем же RingChildNow. Ретраим каждый такт, а не
        // «один раз по фронту»: на выходе из выгорания сверху может стоять S5-подсказка, и тогда попытка
        // просто повторится следующим тактом, вместо того чтобы потерять открытие насовсем.
        // Жизнь кончилась (смерть/финал/опенер) — отложенное открытие снимается вместе с ней.
        private void PumpPendingNewScale()
        {
            if (_nsPending == NewScale.None) return;
            if (_game == null || _game.State != GameState.Playing) { _nsPending = NewScale.None; return; }
            if (_nsShowing || _tutorialShowing || _smShowing) return;

            var pending = _nsPending;
            _nsPending = NewScale.None;
            ShowNewScale(pending);
        }

        // Закрыть модалку. reward=true — штатный выход по выполненному условию (салют звёзд, build-spec §D);
        // reward=false — аварийный (выход из игры / рестарт / уход из Playing): тихо, без награды.
        private void CloseNewScale(bool reward)
        {
            if (!_nsShowing) return;
            _nsShowing = false;
            _nsDone = false;
            _nsArmed = false;
            _nsHold = 0f;
            _nsTicks = 0;
            // ⚠ ЛЬГОТА «СЛЕДУЮЩАЯ КАРТОЧКА — ПО КАРМАНУ» (r4 п.1б): взводится ровно здесь, на закрытии
            // окна ДЕНЕГ, потому что жалоба основательницы дословно про «сразу ПОСЛЕ ТУРИАЛА». Взводим
            // и на аварийном закрытии тоже: обучение показали — обещание игре уже дано.
            // Снимается флаг сам, на первой же выданной карточке (Game.Advance).
            bool armGrace = _nsWhich == NewScale.Money;
            _nsWhich = NewScale.None;
            ReturnBigWidget();
            _nsOverlay.SetActive(false);
            _nsFade.alpha = 1f;
            // «Условие выполнено» — тот же туш-фанфара, теперь как разрешение. Только на ШТАТНОМ
            // выходе: аварийное закрытие (рестарт/выход из игры) обязано быть тихим, как и салют.
            if (reward && Audio != null) Audio.Play(SoundEvent.Tutorial);
            if (reward) StarBurst();      // §6-салют «всё сделано верно» — ПОСЛЕ фейда, ДО снятия паузы
            SyncPause();
            ReflectDomeUnderModal();      // …и купол возвращается тем же кадром, что снялась модалка
            // ⚠ ЛЬГОТА ЗОВЁТСЯ ЗДЕСЬ, А НЕ В НАЧАЛЕ МЕТОДА: она может ПЕРЕОФОРМИТЬ уже стоящую на экране
            // заблокированную карточку (Game.ReplaceBlockedCurrentCard), а значит поднять CardChanged —
            // и обработчик обязан увидеть мир БЕЗ модалки: окно снято, пауза уже пересчитана, купол на
            // месте. Иначе новая карточка въехала бы под ещё живую §D-модалку.
            if (armGrace) _game?.ArmAffordableNextCard();
            // Та же причина, что у DismissTutorial: открытие происходит СЕРЕДИНОЙ карточки, поэтому
            // возрастные гейты и значения HUD пересчитываются прямо здесь — виджет живой сразу.
            if (_game != null && _game.State == GameState.Playing)
            {
                UpdateHudValues();
                ApplyAgeGates(_game.Age);
            }
        }

        // Ввод под модалкой. Ответы (ДА/НЕТ) и таймаут окно НЕ закрывают — они здесь просто инертны;
        // живым остаётся ровно ОДИН контрол — контрол ОБЪЯСНЯЕМОЙ шкалы, тем же путём, что и в обычной
        // игре (кэп дохода, поднятый датчик, ось балансира, окно звонка), поэтому «выполнил условие» =
        // «реально поработал контролом».
        //
        // ⚠ ФИЛЬТР ПО ШКАЛЕ — не косметика, а защита механики (находка ревью). Под модалкой время стоит:
        // возраст, стоимость жизни и ВСЕ дренажи заморожены. Если под ней живы ВСЕ контролы, игрок,
        // получив, скажем, экран энергии, может бесконечно крутить крутилку в замороженном мире и
        // накрутить сколько угодно денег — вся экономика игры проходит мимо. Поэтому чужие scale-вводы
        // сюда доходят, но до Game НЕ доходят: они инертны ровно как ДА/НЕТ.
        private void NewScaleInput(GameInput input)
        {
            if (_nsDone) return;   // условие уже выполнено, идёт фейд — доигрываем без новых событий

            switch (input)
            {
                case GameInput.MoneyTick:
                case GameInput.MoneyTickRepeat:
                    if (_nsWhich != NewScale.Money) return;           // чужой экран — крутилка мертва
                    if (_game.State != GameState.Playing) return;
                    if (!_crankCap.TryAccept()) return;               // тот же кэп дохода ~5/с
                    // Та же accepted-семантика, что в игровой ветке OnInput: звучит только ЗАСЧИТАННЫЙ
                    // тик. Под §D-модалкой пауза «живая» (PausedInputsLive), поэтому Crank её принимает —
                    // но условие спрашивается у механики, а не подразумевается.
                    if (_game.HandleInput(GameInput.MoneyTick) && Audio != null)
                    {
                        Audio.Play(SoundEvent.CrankTick);   // тот же щелчок, что и в игре
                        Audio.Play(SoundEvent.CoinJar);     // …и та же монета
                    }
                    if (_game.MoneyOpen && isActiveAndEnabled)
                    {
                        if (_moneyPulse != null) StopCoroutine(_moneyPulse);
                        _moneyPulse = StartCoroutine(DropCoin());
                    }
                    _nsArmed = true;                                  // ПРИНЯТЫЙ тик…
                    if (_nsTicks < MoneyTutorialTicks) _nsTicks++;    // …и он же — шаг к N тикам условия
                    return;

                case GameInput.EnergyHold:
                    if (_nsWhich != NewScale.Energy) return;          // чужой экран — датчик мёртв
                    if (_game.State != GameState.Playing) return;
                    _game.HandleInput(GameInput.EnergyHold);          // латчится, TickModalBreath наполнит
                    NoteEnergyGesture();                              // …и та же сцена зарядки, что в игре
                    _nsArmed = true;
                    return;

                case GameInput.RelationUp:
                case GameInput.RelationDown:
                    if (_nsWhich != NewScale.Relations) return;       // чужой экран — ось не латчится
                    if (_game.State != GameState.Playing) return;
                    _game.HandleInput(input);                         // ось латчится, TickModalBalancer её сведёт
                    _nsArmed = true;
                    return;

                case GameInput.ChildPress:
                case GameInput.Confirm:   // dev-Enter = «поднять трубку», как и в обычной игре
                    if (_nsWhich != NewScale.Child) return;           // на остальных экранах CONFIRM инертен
                    bool wasRinging = _game.ChildFlashing;
                    _game.HandleInput(GameInput.ChildPress);
                    SyncPhoneRingLoop();   // тот же съём рингтона, что и в PressChildPhone
                    if (wasRinging && !_game.ChildFlashing)
                    {
                        if (Audio != null) Audio.Play(SoundEvent.PhonePickup);   // то же «алло» калимбой
                        _nsArmed = true;
                    }
                    return;
            }
        }

        // ================================================================ r3 — входной экран СПЕЦРЕЖИМА

        /// <summary>Канон-рассказ Ведущего для спецрежима (host-content §4, ✍ черновики).</summary>
        public static string SpecialStory(SpecialMode m) => m switch
        {
            SpecialMode.Health => HealthStoryText,
            SpecialMode.Blitz => BlitzStoryText,
            SpecialMode.Depression => DepressionStoryText,
            SpecialMode.Burnout => BurnoutStoryText,
            _ => "",
        };

        /// <summary>Канон-задача для спецрежима (host-content §4, ✍ черновики).</summary>
        public static string SpecialTask(SpecialMode m) => m switch
        {
            SpecialMode.Health => HealthTaskText,
            SpecialMode.Blitz => BlitzTaskText,
            SpecialMode.Depression => DepressionTaskText,
            SpecialMode.Burnout => BurnoutTaskText,
            _ => "",
        };

        /// <summary>Какой контрол автомата называет служебная строка входного экрана (клавиша эмуляции).
        /// Здоровье лечат ВЫБОРЫ (своего контрола нет), блиц играется двумя рычагами ответа — их называет
        /// сам текст задачи, поэтому там строка пуста.</summary>
        private static ArcadeControlId? ControlOf(SpecialMode m) => m switch
        {
            SpecialMode.Burnout => ArcadeControlId.HeightA,      // «зажми датчик высоты и держи»
            SpecialMode.Depression => ArcadeControlId.BangButton,  // ловля пульса «!» (DepressionCatchControlName)
            _ => null,
        };

        /// <summary>
        /// Поднять входной экран спецрежима. Один раз на вход в режим (здоровье и выгорание — один раз за
        /// ЖИЗНЬ, блиц и депрессия входят по одному разу за жизнь сами). Экран НЕ встаёт поверх другого
        /// окна: конкурент откладывается в <see cref="_smPending"/> и поднимется, как только освободится
        /// место, — ровно тот же приём, что и у отложенной §D-модалки, и по той же причине (пауза, которую
        /// нечем снять, — мёртвый прогон).
        /// </summary>
        private void ShowSpecialMode(SpecialMode which)
        {
            if (which == SpecialMode.None || _game == null) return;
            if (_smShowing || _nsShowing || _tutorialShowing) { _smPending = which; return; }

            _smPending = SpecialMode.None;
            _smWhich = which;
            _smShowing = true;
            _nsFadeT = 0f;

            _nsStoryText.text = SpecialStory(which);
            _nsTaskText.text = SpecialTask(which);
            LayoutTaskWindow(special: true);
            // Рокировка окон: ЗДОРОВЬЮ нужен правый верх (его HUD-место) — облачко уходит влево.
            LayoutStoryBubble(left: which == SpecialMode.Health);

            // Крупный виджет: у здоровья/выгорания/блица — НАСТОЯЩИЙ HUD-виджет (бар, батарея, купол),
            // одолженный тем же блоком, что и на §D; у депрессии — эмблема-звезда в том же слоте.
            // ⚠ КРУПНЫЙ ВИДЖЕТ ЕСТЬ НЕ У ВСЕХ — и это осознанно. Основательница назвала ровно два:
            // «КРУПНЫЙ бар здоровья» (п.1) и «крупная красная батарея» (п.5а). Блицу и депрессии
            // одалживать нечего: у блица «виджет» — это купол-таймер, но раздутый полуэллипс читается
            // как жёлтое пятно, а не как таймер (проверено кадром), а у депрессии её звезда-пульс
            // ложится ровно на плашку-задачу и читается как случайная наклейка. Пустой слот на этих
            // двух экранах честнее: рассказ + задача + CTA и так несут всю подачу.
            GameObject borrow = which switch
            {
                SpecialMode.Health => _healthGroup,
                SpecialMode.Burnout => _energyGroup,
                _ => null,
            };
            if (borrow != null) BorrowWidget(borrow, BigSpecialSrc(which), BigSpecialDst(which));

            _smCtaEdge.SetActive(true);
            _smCta.gameObject.SetActive(true);
            if (_smCtaText.text != SpecialModeCtaText) _smCtaText.text = SpecialModeCtaText;

            _nsFade.alpha = 1f;
            _nsOverlay.transform.SetAsLastSibling();
            _nsOverlay.SetActive(true);
            SyncPause();
            ReflectDomeUnderModal();   // …и купол уходит/остаётся ЭТИМ же кадром (блиц его как раз одолжил)
        }

        /// <summary>
        /// Снять входной экран (зелёная кнопка / уход из Playing). Салюта здесь НЕТ: игрок ничего не
        /// выполнил, он прочитал правила — салют §6 остаётся наградой за КАЛИБРОВКУ шкалы.
        /// </summary>
        private void CloseSpecialMode()
        {
            if (!_smShowing) return;
            _smShowing = false;
            _smWhich = SpecialMode.None;
            ReturnBigWidget();
            _smCtaEdge.SetActive(false);
            _smCta.gameObject.SetActive(false);
            LayoutTaskWindow(special: false);   // окно возвращается к §D-раскладке…
            LayoutStoryBubble(left: false);     // …и облачко — на свою каноническую правую сторону
            if (!_nsShowing) _nsOverlay.SetActive(false);
            _nsFade.alpha = 1f;
            SyncPause();
            ReflectDomeUnderModal();
            // Та же причина, что у DismissTutorial/CloseNewScale: вход в режим случается СЕРЕДИНОЙ карточки.
            if (_game != null && _game.State == GameState.Playing)
            {
                UpdateHudValues();
                ApplyAgeGates(_game.Age);
            }
            PumpPendingSpecialMode();
        }

        // Отложенный входной экран: поднимаем, как только место освободилось. Ретраим каждый такт (а не
        // «один раз по фронту»), чтобы очередь из двух окон не теряла второе.
        private void PumpPendingSpecialMode()
        {
            if (_smPending == SpecialMode.None) return;
            if (_game == null || _game.State != GameState.Playing) { _smPending = SpecialMode.None; return; }
            if (_smShowing || _nsShowing || _tutorialShowing) return;
            var pending = _smPending;
            _smPending = SpecialMode.None;
            ShowSpecialMode(pending);
        }

        /// <summary>
        /// Ввод под входным экраном спецрежима. Живой контрол РОВНО ОДИН — ЗЕЛЁНАЯ кнопка (и её скрытая
        /// dev-эмуляция Enter/CONFIRM), ровно как на опенере и финале. Всё остальное — крутилка, датчик,
        /// джойстик, «!», красный рычаг — инертно: под экраном время стоит, и «нафармить» на паузе нечего.
        /// </summary>
        private void SpecialModeInput(GameInput input)
        {
            if (input != GameInput.AnswerYes && input != GameInput.Confirm) return;
            CloseSpecialMode();
            _dismissedThisFrame = true;   // тот же swallow-гейт, что у подсказки: аккорд не течёт в геймплей
            _smClosedThisFrame = true;    // …плюс собственный, который глушит и Confirm (см. OnInput)
        }

        // «Одолжить» настоящий HUD-виджет модалке: перевесить его в слот оверлея (оба родителя —
        // полноэкранные stretched-рект, поэтому раскладка детей не меняется), увеличить и поставить на
        // указанную точку. Масштаб идёт ВОКРУГ точки-источника: localScale масштабирует детей относительно
        // пивота группы (центр канваса), а anchoredPosition доводит центр виджета до места назначения.
        private void BorrowBigWidget(NewScale which)
        {
            GameObject w = which switch
            {
                NewScale.Money => _moneyGroup,
                NewScale.Relations => _balancerGroup,
                NewScale.Energy => _energyGroup,
                NewScale.Child => _childGroup,
                _ => null,
            };
            BorrowWidget(w, BigScaleSrc(which), BigScaleDst(which));
        }

        /// <summary>
        /// Общий блок «одолжить настоящий HUD-виджет крупной копии» — им пользуются И §D-модалка (четыре
        /// шкалы), И входной экран спецрежима (бар здоровья, батарея выгорания, купол блица). Виджет
        /// ПЕРЕВЕШИВАЕТСЯ, а не клонируется, поэтому крупная копия живая: батарея краснеет тревогой,
        /// маркер здоровья стоит там же, где в HUD, дуга купола полная.
        /// </summary>
        private void BorrowWidget(GameObject w, Vector4 src, Vector4 dst)
        {
            if (w == null || _nsBorrowed != null) return;

            var rt = (RectTransform)w.transform;
            _nsBorrowed = w;
            _nsBorrowedParent = rt.parent;
            _nsBorrowedIndex = rt.GetSiblingIndex();
            _nsBorrowedAnchorMin = rt.anchorMin;
            _nsBorrowedAnchorMax = rt.anchorMax;
            _nsBorrowedOffsetMin = rt.offsetMin;
            _nsBorrowedOffsetMax = rt.offsetMax;
            _nsBorrowedScale = rt.localScale;

            rt.SetParent(_nsSlot.transform, worldPositionStays: false);
            rt.SetAsLastSibling();
            w.SetActive(true);
            PlaceBigWidget(rt, src, dst);
        }

        /// <summary>
        /// Поставить одолженный виджет так, чтобы его HUD-бокс <see cref="BigScaleSrc"/> лёг ровно в целевой
        /// <see cref="BigScaleDst"/>.
        ///
        /// ⚠ ПОЧЕМУ ЧЕРЕЗ ЯКОРЯ, А НЕ ЧЕРЕЗ anchoredPosition. Виджет-группа растянута на весь канвас, а её
        /// дети посажены <see cref="AnchorPx"/> — то есть ДОЛЯМИ канваса, которые пересчитываются на каждом
        /// layout-проходе. Старый код замораживал сдвиг группы в ПИКСЕЛЯХ канваса на момент «одалживания»:
        /// стоило канвасу потом поменять размер (харнесс скриншотов пиннит его с ≈1664×1248 на 1920×1080 уже
        /// ПОСЛЕ показа модалки), как дети уезжали по долям, а замороженный сдвиг — нет, и крупная шкала
        /// промахивалась мимо цели на ~100 px. Сдвиг в ДОЛЯХ (якорями) переживает любой ресайз канваса.
        /// </summary>
        private static void PlaceBigWidget(RectTransform rt, NewScale which)
            => PlaceBigWidget(rt, BigScaleSrc(which), BigScaleDst(which));

        /// <summary>Тот же расчёт по ПРОИЗВОЛЬНОЙ паре боксов — им пользуются и входные экраны спецрежимов.</summary>
        private static void PlaceBigWidget(RectTransform rt, Vector4 src, Vector4 dst)
        {
            if (src.z <= 0f) return;
            float k = dst.z / src.z;

            // Доли канваса: где фигура лежит сейчас и куда должна лечь (y считается от ВЕРХА, как в AnchorPx).
            Vector2 f = new(src.x / 1920f, 1f - src.y / 1080f);
            Vector2 g = new(dst.x / 1920f, 1f - dst.y / 1080f);
            // Масштаб идёт вокруг пивота группы (центр канваса), поэтому доля источника тоже масштабируется.
            Vector2 d = new((g.x - 0.5f) - k * (f.x - 0.5f), (g.y - 0.5f) - k * (f.y - 0.5f));

            rt.localScale = new Vector3(k, k, 1f);
            rt.anchorMin = new Vector2(d.x, d.y);
            rt.anchorMax = new Vector2(1f + d.x, 1f + d.y);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // …и вернуть его в HUD ровно туда, откуда взяли (родитель, порядок, позиция, масштаб).
        private void ReturnBigWidget()
        {
            if (_nsBorrowed == null) return;
            var rt = (RectTransform)_nsBorrowed.transform;
            if (_nsBorrowedParent != null)
            {
                rt.SetParent(_nsBorrowedParent, worldPositionStays: false);
                rt.SetSiblingIndex(_nsBorrowedIndex);
            }
            rt.anchorMin = _nsBorrowedAnchorMin;
            rt.anchorMax = _nsBorrowedAnchorMax;
            rt.offsetMin = _nsBorrowedOffsetMin;
            rt.offsetMax = _nsBorrowedOffsetMax;
            rt.localScale = _nsBorrowedScale;
            _nsBorrowed = null;
            _nsBorrowedParent = null;
        }

        // ================================================================ звук (манифест дизайн-сессии)

        /// <summary>
        /// Звуковой слой. Публичный — тесты слушают <see cref="AudioLayer.DebugPlayed"/>, а
        /// проигрывание идёт ТОЛЬКО через него (см. класс: единая точка + фильтр депрессии).
        /// </summary>
        public AudioLayer Audio { get; private set; }

        // --- Латчи: события, которых в коде НЕТ, приходится ловить фронтом самим ------------------
        private GameState _audioPrevState = GameState.Opener;
        private bool _audioDomeAlarmed;      // купол уже пропищал последнюю секунду ЭТОЙ карточки
        private bool _audioBurnout;          // прошлый кадр: выгорание (выхода из него события нет)
        private bool _audioDepPulsing;       // прошлый кадр: окно пульса депрессии открыто
        private int _audioBlitzFails;        // прошлый счётчик провалов блица (провал события не имеет)
        private float _audioSinceEnergyHold; // с последнего принятого удержания датчика

        /// <summary>Пауза дольше этой — следующее удержание датчика считается НОВЫМ жестом зарядки.</summary>
        private const float EnergyGestureGap = 0.25f;

        /// <summary>
        /// ЕДИНСТВЕННОЕ УСЛОВИЕ РИНГТОНА (находка Codex 2026-08-08, MAJOR). Луп звонка обязан жить
        /// РОВНО столько, сколько трубка ВИДНА игроку и звонок активен. Раньше он включался и
        /// выключался внутри <see cref="ReflectChildPhone"/> — а её под кризисом никто не зовёт
        /// (Game.Tick входит в кризис ДО интеграции ребёнка, звонок остаётся ChildFlashing,
        /// <see cref="RenderCrisis"/> прячет трубку и уходит), и рингтон продолжал звенеть из-под
        /// блица над спрятанной трубкой. Латать это вторым StopLoop в RenderCrisis значило бы
        /// заводить третью, четвёртую ветку на каждый новый способ спрятать виджет.
        ///
        /// Поэтому спрашиваем не «какой ветке рендера сейчас ход», а ФАКТ: объект трубки жив в
        /// иерархии (кризис, финал, рестарт, выход из игры — все гасят именно его) И окно звонка
        /// открыто И оно РЕАЛЬНО КРУТИТСЯ (глухая пауза его стопорит — <see cref="Game.ChildCallFrozen"/>;
        /// «живая» пауза §D-модалки ребёнка не стопорит, там звонок и есть содержание экрана).
        /// </summary>
        private bool PhoneRingAudible
            => _game != null
               && _game.State == GameState.Playing
               && _game.ChildFlashing
               && !_game.ChildCallFrozen
               && _childGroup != null && _childGroup.activeInHierarchy;

        /// <summary>
        /// Свести луп рингтона с <see cref="PhoneRingAudible"/>. Идемпотентно: <c>PlayLoop</c> на уже
        /// играющем канале фразу не рвёт, <c>StopLoop</c> на молчащем — no-op, поэтому вызывать можно
        /// хоть каждый кадр. Зовётся из конца кадра (Update / seam DebugTick) и сразу после поднятия
        /// трубки — чтобы «алло» не ложилось поверх ещё звенящего рингтона.
        /// </summary>
        private void SyncPhoneRingLoop()
        {
            if (Audio == null) return;
            if (PhoneRingAudible) Audio.PlayLoop(SoundEvent.PhoneRing);
            else Audio.StopLoop(LoopChannel.PhoneRing);
        }

        /// <summary>
        /// Покадровые ЛАТЧИ звука. Здесь живут ровно те строки манифеста, под которые в коде нет
        /// события: выход из выгорания, последняя секунда купола, пульс депрессии и провал блица.
        ///
        /// ⚠ НА ПАУЗЕ ЛАТЧИ СТОЯТ. Очереди у слоя нет по устройству (<see cref="AudioLayer"/>), но
        /// если бы латчи крутились под туториалом, снятие паузы дало бы пачку фронтов разом —
        /// done contract §4 «пауза туториалов не копит очередь звуков» держится именно этим ранним
        /// возвратом, а не фильтрацией на стороне слоя.
        /// </summary>
        private void TickAudioLatches(float dt)
        {
            if (_game == null || Audio == null) return;
            _audioSinceEnergyHold = Mathf.Min(_audioSinceEnergyHold + dt, 999f);
            if (_game.Paused && !_game.PausedInputsLive) return;
            if (_game.State != GameState.Playing) return;

            // ВЫГОРАНИЕ: вход озвучен событием BurnoutEntered, а выход Game гасит МОЛЧА (флаг
            // Game.Burnout просто становится false на энергии >40 %). Ловим спад сами.
            bool burn = _game.Burnout;
            if (!burn && _audioBurnout) Audio.Play(SoundEvent.BurnoutOut);
            _audioBurnout = burn;

            // ПУЛЬС ДЕПРЕССИИ: одинокий удар сердца на КАЖДОЕ открытие окна. Идёт ВНЕ фильтра
            // (SoundBus.Unfiltered) — «остаётся наверху нетронутым», пока весь микс в вате.
            bool pulsing = _game.DepressionPulsing;
            if (pulsing && !_audioDepPulsing) Audio.Play(SoundEvent.DepressionPulse);
            _audioDepPulsing = pulsing;

            // ПРОВАЛ БЛИЦА: и промах рычагом, и истёкшее окно поднимают ОДИН счётчик Game.BlitzFails
            // и оба обязаны звучать «ответом НЕТ» (манифест: в блице игрок отвечает тем же голосом).
            // Один латч на счётчике покрывает оба, поэтому в OnInput провал намеренно не озвучивается.
            int fails = _game.BlitzFails;
            if (fails > _audioBlitzFails) Audio.Play(SoundEvent.AnswerNo);
            _audioBlitzFails = fails;

            // КУПОЛ, ПОСЛЕДНЯЯ СЕКУНДА: состояние, а не фронт (ReflectDome красит его каждый кадр) —
            // латчим сами и звучим ровно один раз на карточку. В кризисе/депрессии купол чужой.
            if (_game.InCrisis || _game.InDepression) return;
            bool last = _game.CardTimer > 0f && _game.CardTimer <= DomeAlarmSeconds;
            if (last && !_audioDomeAlarmed) { Audio.Play(SoundEvent.DomeLastSecond); _audioDomeAlarmed = true; }
            else if (!last && _game.CardTimer > DomeAlarmSeconds) _audioDomeAlarmed = false;
        }

        /// <summary>
        /// ЗАРЯДКА — ЦЕЛЫЙ ЖЕСТ, А НЕ ТРИ СОБЫТИЯ. Основательница отвергла разбиение «начало / луп /
        /// отпустил»: «по кусочкам оно не читалось». Файл содержит всю сцену наливания вместе с
        /// сигналом «полная», поэтому мы его просто ЗАПУСКАЕМ — и больше не трогаем. Датчик
        /// переиздаёт EnergyHold каждый кадр, значит «начало жеста» = первое удержание после паузы
        /// длиннее <see cref="EnergyGestureGap"/>.
        /// </summary>
        private void NoteEnergyGesture()
        {
            if (Audio == null) return;
            if (_audioSinceEnergyHold > EnergyGestureGap) Audio.Play(SoundEvent.EnergyCharge);
            _audioSinceEnergyHold = 0f;
        }

        /// <summary>Депрессия закончилась: «мир снова включили» — риза и полное снятие фильтра.</summary>
        private void OnDepressionEnded()
        {
            if (Audio == null) return;
            Audio.Play(SoundEvent.DepressionExit);
            Audio.ExitDepression();
        }

        /// <summary>Кризис закончился — снять струнную «сирену» импульса, если она крутилась.</summary>
        private void OnCrisisEnded()
        {
            if (Audio != null) Audio.StopLoop(LoopChannel.Impulse);
        }

        /// <summary>Второй пропуск подряд: редкий низкий акцент «плохой родитель».</summary>
        private void OnChildBadParent()
        {
            if (Audio != null) Audio.Play(SoundEvent.BadParent);
        }

        /// <summary>
        /// «Лечение выбором» отдельного события в игре не имеет — лечит обычная Δ карточки. Читаем
        /// выбранную сторону: есть ли в ней ПЛЮС к здоровью. Системный бонус KEK04 (Game.HealHealth)
        /// сюда не попадает — он не выбор игрока.
        /// </summary>
        private static bool HealsHealth(Card card, AnswerSide side)
        {
            if (card == null || side == AnswerSide.Timeout) return false;
            var deltas = side == AnswerSide.Yes ? card.YesDeltas : card.NoDeltas;
            if (deltas == null) return false;
            foreach (var d in deltas)
                if (d.Scale == Scale.Health && d.Kind == DeltaKind.Add && d.Value > 0) return true;
            return false;
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

            // Fresh life → every hint is armed again and leftover state is cleared.
            if (playing && !_wasPlaying)
            {
                _smHealthSeen = false;
                _smBurnoutSeen = false;
                // §D: свежая жизнь — все четыре модальных экрана взводятся заново и ни один не висит.
                for (int i = 0; i < _nsSeen.Length; i++) _nsSeen[i] = false;
                _nsPending = NewScale.None;   // …и отложенный OPEN прошлой жизни с собой не тащим
                CloseNewScale(reward: false);
                // r3: входные экраны спецрежимов тоже взводятся заново и ни один не висит.
                _smPending = SpecialMode.None;
                CloseSpecialMode();
                // §5b: свежая жизнь начинается БЕЗ трубки на экране, и любой звонок оборван — поза
                // сбрасывается в покой (иначе выехавшая трубка пережила бы рестарт).
                _childGroup.SetActive(false);
                _phoneOut = 0f;
                _phoneRingClock = 0f;
                _phoneRinging = false;
                _phoneMissed = false;
                if (_phoneImg != null) _phoneImg.sprite = _phoneRestSprite;
                ApplyPhonePose(0f);
                _tutorialShowing = false;
                _tutorialOverlay.SetActive(false);
                _game.Paused = false;
                _crankCap.Reset();
                _burnoutPlate.SetActive(false);
                _breakupTimer.Hide();
                _breakupPlate.SetActive(false);
                RestoreNormalPlates();   // clear any crisis relabel/highlight carried across a restart
                _depressionGroup.SetActive(false);   // no B&W wash carried across a restart
                _depMutterCount = 0;
                _brightnessAlpha = 0f;
                // Host reveals reset each life: no stale bubble carried across a restart.
                _bubbleTimer.Hide();
                _hostBubble.SetActive(false);
                // §4/§6: свежая жизнь начинается без единой тревоги и без звёзд в воздухе. Сброс МОЛЧАЛИВЫЙ
                // (ResetAlarms не стреляет салютом) — иначе рестарт из тревожного состояния давал бы салют.
                ResetAlarms();
                ClearStars();
                // ЗВУК: свежая жизнь начинается в ЧИСТОМ миксе. Без этого смерть В ДЕПРЕССИИ оставляла
                // бы следующую жизнь играть под лоу-пассом — «застрявшая вата» (done contract §4), —
                // а рингтон/сирена прошлой жизни продолжали бы крутиться поверх новой.
                if (Audio != null) { Audio.ResetAll(); Audio.PlayLoop(SoundEvent.MusicTheme); }
                _audioDomeAlarmed = false;
                _audioBurnout = false;
                _audioDepPulsing = false;
                _audioBlitzFails = 0;
                _audioSinceEnergyHold = 999f;   // первое удержание новой жизни — заведомо НОВЫЙ жест
            }
            // Опенер/финал: тревог там нет по спеку, и подсветка не должна пережить уход из игры.
            if (!playing) { ResetAlarms(); ClearStars(); }
            if (!playing && _tutorialShowing) DismissTutorial();
            if (!playing && _nsShowing) CloseNewScale(reward: false);   // §D: смерть/финал с модалки — тихо
            if (!playing && _smShowing) { CloseSpecialMode(); _smPending = SpecialMode.None; }
            if (!playing) { _bubbleTimer.Hide(); _hostBubble.SetActive(false); SyncPause(); }
            _wasPlaying = playing;

            // ЗВУК РАМКИ ЖИЗНИ — три перехода состояния, три строки манифеста. Идёт ПОСЛЕ блока
            // «свежая жизнь» нарочно: тот сбрасывает слой, и фанфару опенера нельзя играть до сброса.
            // ВЕХИ ВОЗРАСТА здесь намеренно НЕ звучат — решение основательницы («будет какофония»).
            if (Audio != null && _game.State != _audioPrevState)
            {
                var from = _audioPrevState;
                _audioPrevState = _game.State;
                if (finale)
                {
                    // Умереть можно ПРЯМО В ДЕПРЕССИИ — финал обязан звучать в чистом миксе, и ни
                    // рингтон, ни сирена импульса не имеют права пережить конец жизни.
                    Audio.ResetAll();
                    Audio.PlayLoop(SoundEvent.MusicTheme);
                    Audio.Play(FinaleSound.EventFor(_game.Cause));   // старость / выгорание / FATAL
                }
                else if (opener)
                {
                    // Уход в опенер — это либо «НАЧАТЬ ЗАНОВО» с финала, либо чистый выход по
                    // MenuButton. Слой чистим в обоих случаях; плёнку-перемотку играем только рестарту.
                    Audio.ResetAll();
                    Audio.PlayLoop(SoundEvent.MusicTheme);
                    if (from == GameState.Finale) Audio.Play(SoundEvent.Restart);
                }
                else if (playing && from == GameState.Opener)
                {
                    Audio.Play(SoundEvent.Opener);   // фанфара «НАЧАТЬ ЖИЗНЬ» — БЕЗ аплодисментов: зал снят
                }
            }

            if (playing)
            {
                UpdateHudValues();
                ApplyAgeGates(_game.Age);
            }
            else if (finale && _game.Necrolog != null)
            {
                // Возраст исхода — тот же счётчик, что рисует бейдж HUD (FloorToInt), т.е. «дожил до N»
                // совпадает с последним числом, которое игрок видел на экране.
                RenderFinaleTexts(_game.Necrolog, Mathf.FloorToInt(_game.Age));
            }
        }

        private void OnCardChanged()
        {
            if (_game == null || _game.State != GameState.Playing) return;
            var c = _game.CurrentCard;
            _cardText.text = c != null ? c.Question : "";
            bool blocked = _game.CurrentCardBlocked;
            SetCardBlockedDim(blocked);           // S10: dim the card (frame tint) + red banner when unaffordable
            SetBlockBannerVisible(blocked);
            RefreshPriceLabel();                  // S10: show the required amount on any BLOCK$ card
            // TIMELINE-веха больше НИЧЕГО не объявляет: жёлтая рубрика-баннер и её блокирующий бит сняты
            // (плейтест основательницы 2026-08-05 — «их нет в макетах»). Веха приходит обычной карточкой;
            // голос Ведущего живёт в облачке и остаётся ответным (OnAnswerResolved), поэтому здесь его
            // намеренно не трогаем — облачко доживает свои ~2 с поверх новой карточки.
            SyncPause();
            UpdateHudValues();
            ApplyAgeGates(_game.Age);
            // ЗВУК: раздача карточки — на КАЖДУЮ новую, включая заблокированную (она тоже въезжает).
            // BLOCK$ добавляет сверху свой «вомп-вомп»: карточка пришла И она недоступна — два разных
            // сообщения. Купольная тревога взводится заново — она одна на карточку.
            if (Audio != null && c != null)
            {
                _audioDomeAlarmed = false;
                Audio.Play(SoundEvent.CardDeal);
                if (blocked) Audio.Play(SoundEvent.BlockMoney);
            }
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

            if (Audio == null) return;
            // ТАЙМАУТ («молчание»): ответ дала монетка — музыкальный провал, БЕЗ смешка зала (зал снят
            // целиком, sound-direction §3). Сама калимба ДА/НЕТ звучит в OnInput на ПРИНЯТОМ рычаге —
            // здесь её нет намеренно: таймаут рычага не касался.
            if (side == AnswerSide.Timeout) Audio.Play(SoundEvent.Timeout);
            // ЛЕЧЕНИЕ ВЫБОРОМ: карточка с плюсом к здоровью применена — тёплая калимба через октаву.
            else if (HealsHealth(card, side)) Audio.Play(SoundEvent.Heal);
            // ВЕДУЩИЙ: стингер — это ГОЛОС ОБЛАЧКА, а не звук ответа: звучит ТОЛЬКО когда реплика
            // реально показана (~30–40% выборов). Безусловный Play клал пиццикато поверх калимбы на
            // каждой карточке без всякого облачка (плейтест 2026-08-08, sound-direction «Дефект
            // внедрения»). Тон — у того же HostVoice, что выбрал текст.
            if (!string.IsNullOrEmpty(line))
                Audio.Play(AudioCatalog.ForHostTone(_voice.Classify(card, side)));
        }

        // Advance the host speech-bubble clock (real-time) and mirror its visibility onto the widget.
        // Only visible while Playing; the timer keeps its own state so a restart/leaving-play simply hides it.
        private void ReflectHostReveals(bool playing)
        {
            _bubbleTimer.Advance(Time.deltaTime);

            bool bub = playing && _bubbleTimer.Visible;
            if (_hostBubble.activeSelf != bub) _hostBubble.SetActive(bub);
            if (bub) _bubbleText.text = _bubbleTimer.Text;
        }

        // BLOCK$ price sub-line: reads the single source (Game.CurrentCardPrice / CurrentCardBlocked) and
        // shows the required amount on the card in both the affordable and the blocked (dimmed) states.
        private void RefreshPriceLabel()
        {
            bool has = _game.CurrentCardHasPrice;
            ApplyPriceLabel(has, has ? _game.CurrentCardPrice : 0, _game.CurrentCardBlocked);
        }

        // BLOCK$ banner + its black keyline go up and down together (the keyline is a separate sibling
        // BELOW the banner, so it can never be toggled independently and leave a bare red plate).
        private void SetBlockBannerVisible(bool show)
        {
            if (_blockBannerInk != null && _blockBannerInk.activeSelf != show) _blockBannerInk.SetActive(show);
            if (_blockBanner != null && _blockBanner.activeSelf != show) _blockBanner.SetActive(show);
        }

        private void ApplyPriceLabel(bool hasPrice, double price, bool blocked)
        {
            if (!hasPrice)
            {
                _cardPriceText.gameObject.SetActive(false);
                _cardPricePlate.gameObject.SetActive(false);
                _cardPriceInk.gameObject.SetActive(false);
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
            // …и keyline вокруг чипа — ровно на BlockKeylineInk шире с каждой стороны (чип растёт по тексту,
            // поэтому кант пересчитывается здесь же, а не остаётся на размере из BuildGamePanel).
            _cardPriceInk.rectTransform.sizeDelta =
                _cardPricePlate.rectTransform.sizeDelta + new Vector2(2f * BlockKeylineInk, 2f * BlockKeylineInk);

            _cardPriceInk.gameObject.SetActive(true);
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

        // ⚠ OnInputFx СНЯТ 2026-08-07 (r3, п.4). Он был отдельной веткой обратной связи, которая играла
        // панч плашки на СЫРОЕ нажатие рычага, ничего не зная о том, приняла ли механика ввод. Панч
        // переехал в OnInput, на accepted-семантику — см. PunchAnswerPlate. Монета в банку (MoneyTick FX)
        // и так жила в OnInput'е, на ПРИНЯТОМ тике, ровно по той же причине.

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

        // ПАНЧ = SQUASH, А НЕ РАВНОМЕРНАЯ ПРОСАДКА (дизайн-гейт r4, «по желанию»): дудл-канон пака жмёт
        // нажатую плашку по ВЫСОТЕ и распирает по ШИРИНЕ — резина, а не удаление от камеры. Амплитуда
        // прежняя по объёму (пик 1.06 W / 0.88 H против прежних 0.86/0.86), длительность не тронута,
        // accepted-семантика r3 не тронута — меняется ТОЛЬКО форма дуги.
        private const float PunchSquashW = 0.06f;   // +6 % по ширине в нижней точке
        private const float PunchSquashH = 0.12f;   // −12 % по высоте в нижней точке

        /// <summary>Форма панча в момент <paramref name="k"/> ∈ [0,1] — одна формула на корутину и на
        /// отладочную позу, чтобы кадр дизайн-гейта не разъезжался с тем, что видит игрок.</summary>
        private static Vector3 PunchScaleAt(float k)
        {
            float s = Mathf.Sin(k * Mathf.PI);      // 0 → 1 → 0
            return new Vector3(1f + PunchSquashW * s, 1f - PunchSquashH * s, 1f);
        }

        private static IEnumerator PunchPlate(RectTransform rt, float tilt)
        {
            const float dur = 0.16f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                rt.localScale = PunchScaleAt(Mathf.Clamp01(t / dur));
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
        private static void DisplayFx(Graphic g) => DisplayFx(g, out _, out _);

        private static readonly Color DisplayKantInk = new(0.078f, 0.102f, 0.239f, 1f);

        /// <summary>…и то же самое, но с отдачей обоих эффектов вызывающему: плашкам ответа нужно
        /// ПЕРЕКЛЮЧАТЬ их между режимами (блиц — жёсткий кант Ink без мягкой тени, импульс — как было),
        /// а <c>GetComponent&lt;Shadow&gt;()</c> для этого не годится: <see cref="Outline"/> НАСЛЕДУЕТ
        /// <see cref="Shadow"/> и вернулся бы вместо неё.</summary>
        private static void DisplayFx(Graphic g, out Outline kant, out Shadow soft)
        {
            kant = g.gameObject.AddComponent<Outline>();
            kant.effectColor = DisplayKantInk;
            kant.effectDistance = new Vector2(3f, -3f);
            soft = g.gameObject.AddComponent<Shadow>();
            soft.effectColor = new Color(0f, 0f, 0f, 0.32f);
            soft.effectDistance = new Vector2(0f, -6f);
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
