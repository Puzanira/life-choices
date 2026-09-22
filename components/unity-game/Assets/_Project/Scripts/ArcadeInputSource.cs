using System;
using AiGameStudio.ArcadeControls;
using UnityEngine;

namespace ThanksNoThanks
{
    /// <summary>
    /// Arcade-cabinet implementation of <see cref="IInputSource"/>. It is the ONLY input source the game
    /// ships with, and it reads input EXCLUSIVELY through <see cref="ArcadeInput"/> — never a raw device
    /// (that lives behind the arcade-controls backend seam: KeyboardBackend for dev, SerialBackend on the
    /// cabinet). It translates the cabinet's logical controls into the game's semantic <see cref="GameInput"/>
    /// events; the driver + <see cref="Game"/> stay device-agnostic exactly as before.
    ///
    /// Physical → semantic mapping (founder Gate-2 decisions marked ✔; the rest are natural defaults awaiting
    /// Gate-2 sign-off — see the increment report):
    ///   GreenButton ✔ → ДА  (AnswerYes; on the opener/hint/FINALE the driver reads it as CONFIRM —
    ///                        i.e. start / dismiss / restart: the ONE confirm control, founder 99fab3c)
    ///   RedButton   ✔ → НЕТ (AnswerNo; answer-only. On the finale it is deliberately INERT so a masher
    ///                        can never skip the necrolog — this overrides the old «рестарт = красная»)
    ///   MenuButton  ✔ → выход (Exit — clean end-of-run, arcade contract §5)
    ///   Crank         → крутилка денег (accumulated degrees → discrete MoneyTick — a literal money crank)
    ///   BangButton    → «поднять трубку» звонящего ребёнка (ChildPress, revisions §5b)
    ///   HeightA / HeightB → энергия: HELD EnergyHold, re-emitted every frame ЛЮБОЙ из двух датчиков
    ///                       сидит выше середины хода (r7 п.2 — заряжает любая рука)
    ///   Joystick.x    → балансир отношений (held RelationRight/RelationLeft, spring-return = drift)
    ///
    /// A drop-in <c>SerialInputSource</c> is unnecessary: swapping keyboard→Arduino is a backend swap inside
    /// arcade-controls, invisible to this class and the game.
    /// </summary>
    /// <remarks>
    /// ⚠ ПОРЯДОК ИСПОЛНЕНИЯ (r5 п.1, «без задержки отклика»). Кадр обязан идти БУТЕРБРОДОМ из трёх слоёв:
    ///
    ///   1. <c>ArcadeInputRunner</c> (ПАКЕТ, порядок 0) — опрашивает бэкенд, обновляет <c>ArcadeInput</c>;
    ///   2. <c>ArcadeInputSource</c> (МЫ, <see cref="InputBeforeTick"/>) — читает свежее состояние и ЛАТЧИТ
    ///      семантический ввод (ось балансира, датчик высоты, фронты кнопок);
    ///   3. <see cref="GameDriver"/> (<see cref="GameDriver.TickAfterInput"/>) — сводит латч вызовом
    ///      <c>Game.Tick</c>.
    ///
    /// Без явных порядков Unity ставила все три как попало: ось, взведённая в кадре N, доезжала до
    /// интегратора кадром N+1, и порядок мог отличаться между сценой, тестом и билдом.
    ///
    /// ⚠ ПОЧЕМУ ЧИСЛА ПОЛОЖИТЕЛЬНЫЕ, А НЕ ОТРИЦАТЕЛЬНЫЕ. Первая редакция r5 пинила источник на −100 —
    /// «пораньше всех». Это ломало слой 1: раннер пакета остаётся на 0, и мы начинали читать состояние
    /// устройства ДО того, как он его обновил, то есть кадром СТАРШЕ (а дельта крутилки считается
    /// per-poll и так просто терялась). Поймал это не глаз, а живой прогон реальной цепочки кабинета —
    /// `ChildPhoneTests.BangButton_ThroughTheRealCabinetChain…` вставал на таймауте. Раннер пакета трогать
    /// нельзя, поэтому оба НАШИХ слоя уезжают ВПРАВО от него: 100 и 200.
    /// </remarks>
    [DefaultExecutionOrder(InputBeforeTick)]
    public sealed class ArcadeInputSource : MonoBehaviour, IInputSource
    {
        /// <summary>Приоритет исполнения: ПОЗЖЕ раннера пакета (он на 0 и обновляет ArcadeInput), но
        /// строго РАНЬШЕ <see cref="GameDriver.TickAfterInput"/>, чтобы латч сводился ТЕМ ЖЕ кадром.
        /// Гард — PlaytestFixesR5Tests.InputSource_Runs_AfterTheDevicePump_AndBeforeTheTick.</summary>
        public const int InputBeforeTick = 100;

        public event Action<GameInput> Received;

        [Tooltip("Crank rotation (degrees, either direction) that equals one MoneyTick.")]
        [SerializeField] private float degreesPerMoneyTick = 12f;

        // Датчики высоты: УДЕРЖИВАЕМЫЙ сигнал «датчик поднят», переиздаётся каждый кадр, пока рука
        // держит датчик выше середины хода (гистерезис — в PURE BreathSensor, чтобы весь клавиатурный путь
        // прошагивался детерминированно в EditMode: BreathKeyboardPathTests).
        //
        // ⚠ r7 п.2 («сделать оба датчика, чтобы работали»): детектор теперь СВОЙ У КАЖДОГО датчика, и
        // энергия заряжается, пока поднят ЛЮБОЙ из двух. Два состояния, а не одно общее, ровно потому,
        // что гистерезис — это ФИЗИКА КОНКРЕТНОЙ руки на КОНКРЕТНОМ датчике: один общий детектор,
        // накормленный Max(A,B), склеил бы два независимых жеста в один и терял бы отпускание («держу A,
        // мигаю B» читалось бы как непрерывное удержание).
        private readonly BreathSensor _breathSensorA = new();
        private readonly BreathSensor _breathSensorB = new();
        // Relationship balancer (Joystick.x): held axis, re-emitted every frame past the deadzone.
        private const float JoyDeadzone = 0.4f;

        // ⚠ ПОЛЯРНОСТЬ ОСИ НА СТОЙКЕ — ИЗ КОНФИГА, А НЕ ИЗ КОДА (см. GameConfig). Читается ОДИН раз на
        // Awake: это тюнинг сборки автомата, он не меняется посреди забега, а лезть в файл каждый кадр
        // — мусор на ровном месте.
        private float _relationAxisSign = GameConfig.DefaultRelationAxisSign;

        private float _crankAccum;
        private bool _prevGreen, _prevRed, _prevBang, _prevMenu;

        private void Awake()
        {
            // The game needs ArcadeInput pumped every frame. If no runner is present in the scene (standalone
            // game / editor screenshot), add one — it builds the packaged KeyboardBackend (dev simulation) and
            // pumps ArcadeInput.Update each frame. On the cabinet the launcher/hub provides the runner + serial
            // backend, so this is a no-op there.
            if (UnityEngine.Object.FindAnyObjectByType<ArcadeInputRunner>() == null)
                gameObject.AddComponent<ArcadeInputRunner>();

            _relationAxisSign = GameConfig.RelationAxisSign;
        }

        private void Update()
        {
            if (ArcadeInput.Backend == null) return; // runner hasn't initialized ArcadeInput yet this frame

            // ---- discrete buttons: own edge detection (independent of ArcadeInput's own Pressed events,
            //      which Initialize() clears — polling IsHeld here is reset-safe and order-independent) ----
            bool green = ArcadeInput.GreenButton.IsHeld;
            bool red = ArcadeInput.RedButton.IsHeld;
            bool bang = ArcadeInput.BangButton.IsHeld;
            bool menu = ArcadeInput.MenuButton.IsHeld;

            if (green && !_prevGreen) Emit(GameInput.AnswerYes);   // ДА  ✔
            if (red && !_prevRed) Emit(GameInput.AnswerNo);        // НЕТ ✔
            if (bang && !_prevBang) Emit(GameInput.ChildPress);    // «поднять трубку»
            if (menu && !_prevMenu) Emit(GameInput.Exit);          // выход ✔
            _prevGreen = green; _prevRed = red; _prevBang = bang; _prevMenu = menu;

            // ---- money crank → one MoneyTick per configured degrees, either direction ----
            _crankAccum += Mathf.Abs(ArcadeInput.Crank.DeltaDegrees);
            int guard = 0;
            while (_crankAccum >= degreesPerMoneyTick && guard++ < 64)
            {
                _crankAccum -= degreesPerMoneyTick;
                Emit(GameInput.MoneyTick);
            }

            // ---- датчики высоты (A ИЛИ B) → held EnergyHold, переиздаётся КАЖДЫЙ кадр, пока поднят ЛЮБОЙ --
            // ⚠ ЧТО ЗДЕСЬ НА САМОМ ДЕЛЕ НЕСУЩЕЕ: ДВА ОТДЕЛЬНЫХ ВЫЗОВА Step, А НЕ ЗНАК В УСЛОВИИ.
            // Оба детектора обязаны прошагать свой гистерезис КАЖДЫЙ кадр, иначе второй датчик застревает
            // в том состоянии, в котором его застали («отпустил A — B помнит удержание, которого уже нет»,
            // и наоборот: «держал A, B в это время не опрашивался и подняться уже не может»).
            //
            // Гарантирует это ВЫНОС вызовов в локальные переменные: обе строки выполняются безусловно, и
            // сокращать тут нечего. Поэтому «|» ниже — НЕ оберег: на уже вычисленных bool'ах «|» и «||»
            // делают ровно одно и то же, и подмена одного другим не меняет НИЧЕГО (проверено мутацией
            // 2026-09-22 — гард остался зелёным, потому что ловить было нечего). Сломать можно ДРУГИМ
            // движением — втянув вызовы обратно в условие: `if (_breathSensorA.Step(a) || _breathSensorB
            // .Step(b))`. Вот ЭТО теряет шаг второго детектора, и вот это ловит гард
            // ReleasingOneSensor_WhileTheOtherIsHeld_KeepsCharging (кадр B в полосе гистерезиса).
            // Прежняя редакция комментария приписывала защиту знаку «|» — это было неверно.
            //
            // ⚠ И РОВНО ОДИН Emit НА КАДР — не по одному с каждого датчика. Держать оба разом ДОЛЖНО быть
            // не быстрее, чем держать один (умолчание Maintainer'а: MAX, не сумма). Game._breathHeld —
            // булев латч, гасимый каждым Tick, так что два Emit и один дают один и тот же такт роста;
            // единственный Emit делает это свойством ИСТОЧНИКА, а не счастливым совпадением в Game.
            bool raisedA = _breathSensorA.Step(ArcadeInput.HeightA.Value);
            bool raisedB = _breathSensorB.Step(ArcadeInput.HeightB.Value);
            if (raisedA | raisedB) Emit(GameInput.EnergyHold);

            // ---- relationship balancer (Joystick HORIZONTAL) → held axis, re-emitted each frame ----
            // ⚠ r7 п.1: ось переехала с ВЕРТИКАЛИ на ГОРИЗОНТАЛЬ. Шкала отношений нарисована
            // ГОРИЗОНТАЛЬНЫМ балансиром (парень слева, девушка справа, сердце-маркер ездит между ними —
            // GameDriver.RelMarkerMinCx/MaxCx), а рычаг просил тянуть вверх-вниз. Вправо = маркер вправо
            // = рост шкалы: «тяни туда, куда хочешь сдвинуть маркер».
            //
            // ⚠ ЗЕРКАЛО X — ТОЛЬКО НА ЖЕЛЕЗЕ. Ориентация оси — свойство ФИЗИЧЕСКОЙ сборки (CONTROLS_BRIEF
            // §5: модуль джойстика уже дважды пере-подключали, и знак X переворачивался). Пакетный
            // SerialTuning.InvertJoystickX нам недоступен: раннер пакета строит SerialTuning.Default
            // жёстко, а пакет трогать нельзя. Поэтому множитель ±1 читается из game.json (GameConfig) и
            // применяется ЗДЕСЬ — правка одной цифры на стойке вместо пересборки билда.
            //
            // И применяется он СТРОГО на железном пути. Клавиатурная эмуляция однозначна по определению:
            // «←» — влево, «→» — вправо, там нечего зеркалить. Перевернуть заодно и её значило бы, что
            // человек, чинящий проводку автомата, ломает дев-клавиатуру всем остальным.
            float x = ArcadeInput.Joystick.Vector.x;
            if (!ArcadeInput.IsEmulated(ArcadeControlId.Joystick)) x *= _relationAxisSign;

            if (x >= JoyDeadzone) Emit(GameInput.RelationRight);
            else if (x <= -JoyDeadzone) Emit(GameInput.RelationLeft);
        }

        private void Emit(GameInput input) => Received?.Invoke(input);
    }
}
