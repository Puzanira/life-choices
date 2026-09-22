using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Physical→semantic mapping guards for <see cref="ArcadeInputSource"/>, driven end-to-end through the
    /// arcade-controls package's own <see cref="FakeBackend"/> (snapshot → ArcadeInput → source → GameInput).
    /// One test per control, and every test is ONE-HOT: injecting a single control must emit ONLY its own
    /// <see cref="GameInput"/> value(s) — any cross-talk (a button leaking a foreign event) fails the exact
    /// received-list assertion. Replaces the retired keyboard-mapping tests at the new integration boundary.
    /// </summary>
    public class ArcadeInputSourceMappingTests
    {
        private GameObject _rig;       // ArcadeInputRunner + FakeBackend (pumps ArcadeInput each frame)
        private GameObject _sourceGo;  // the ArcadeInputSource under test
        private FakeBackend _fake;
        private List<GameInput> _received;

        /// <summary>
        /// Поднять связку «раннер с ЭТИМ бэкендом + источник» и начать с чистого листа.
        ///
        /// ⚠ Бэкенд — параметр, а не константа, ровно из-за r7-гарда полярности оси: <see cref="FakeBackend"/>
        /// НЕ реализует <c>IKeyboardEmulation</c>, поэтому <c>ArcadeInput.IsEmulated</c> на нём отвечает
        /// «нет» — это ЖЕЛЕЗНЫЙ путь. Клавиатурный путь надо изображать бэкендом, который отвечает «да»
        /// (<see cref="EmulatedBackend"/>): настоящую клавиатуру в headless-прогоне не понажимать.
        /// </summary>
        private IEnumerator BootWith(IArcadeBackend backend)
        {
            _rig = new GameObject("Rig");
            _rig.SetActive(false);
            _rig.AddComponent<ArcadeInputRunner>().BackendOverride = backend;  // injected before Awake
            _rig.SetActive(true);                                              // Initialize(backend)

            _sourceGo = new GameObject("Source");
            var src = _sourceGo.AddComponent<ArcadeInputSource>();             // finds the rig's runner
            _received = new List<GameInput>();
            src.Received += i => _received.Add(i);

            // Settle two zero-snapshot frames so every control starts released/at-rest, then start clean.
            yield return null;
            yield return null;
            _received.Clear();
        }

        private IEnumerator Boot()
        {
            _fake = new FakeBackend();
            yield return BootWith(_fake);
        }

        /// <summary>Бэкенд, который ОТВЕЧАЕТ как клавиатурная эмуляция, но снимок отдаёт тестовый.</summary>
        private sealed class EmulatedBackend : IArcadeBackend, IKeyboardEmulation
        {
            public BackendSnapshot Next;
            public BackendSnapshot Poll(float deltaTime) => Next;
            public KeyboardMapping Mapping { get; } = KeyboardMapping.LoadDefault();
            public bool IsEmulated(ArcadeControlId control) => true;
        }

        private EmulatedBackend _emu;

        private IEnumerator BootEmulated()
        {
            _emu = new EmulatedBackend();
            yield return BootWith(_emu);
        }

        private IEnumerator HoldEmulated(BackendSnapshot snap, int frames = 4)
        {
            _emu.Next = snap;
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>Знак оси — глобальное состояние конфига; ни один тест не смеет утечь им в соседний.</summary>
        [TearDown]
        public void ResetGameConfig() => GameConfig.DebugReload();

        private IEnumerator Cleanup()
        {
            Object.Destroy(_sourceGo);
            Object.Destroy(_rig);
            yield return null;   // let Destroy complete so the next test's FindAnyObjectByType is clean
        }

        private IEnumerator Hold(BackendSnapshot snap, int frames = 4)
        {
            _fake.Next = snap;
            for (int i = 0; i < frames; i++) yield return null;
        }

        private IEnumerator Release(int frames = 3)
        {
            _fake.Next = default;
            for (int i = 0; i < frames; i++) yield return null;
        }

        // ---- buttons: one edge per press, one-hot ----------------------------------------------------

        [UnityTest]
        public IEnumerator GreenButton_Emits_AnswerYes_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { GreenHeld = true });   // held over several frames
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.AnswerYes }, _received,
                "GREEN = ДА: exactly one AnswerYes per press (edge, not autorepeat) and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator RedButton_Emits_AnswerNo_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { RedHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.AnswerNo }, _received,
                "RED = СПАСИБО НЕ НАДО: exactly one AnswerNo per press and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator MenuButton_Emits_Exit_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { MenuHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.Exit }, _received,
                "MENU = выход: exactly one Exit per press and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator BangButton_Emits_ChildPress_Once_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { BangHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.ChildPress }, _received,
                "BANG = кнопка ребёнка: exactly one ChildPress per press and no foreign events");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Button_Repress_Emits_A_Second_Edge()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { GreenHeld = true });
            yield return Release();
            yield return Hold(new BackendSnapshot { GreenHeld = true });
            yield return Release();
            CollectionAssert.AreEqual(new[] { GameInput.AnswerYes, GameInput.AnswerYes }, _received,
                "release + repress = a second clean edge");
            yield return Cleanup();
        }

        // ---- crank → MoneyTick ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Crank_Rotation_Emits_Only_MoneyTicks()
        {
            yield return Boot();
            // One full degreesPerMoneyTick (12°) per poll while held → at least one tick, and NOTHING else.
            yield return Hold(new BackendSnapshot { CrankDeltaDegrees = 12f }, frames: 4);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 1, "rotation produced at least one MoneyTick");
            Assert.IsTrue(_received.All(i => i == GameInput.MoneyTick),
                $"crank emits ONLY MoneyTick (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Crank_Small_Deltas_Accumulate_Into_Ticks()
        {
            yield return Boot();
            // 4° per poll — under the 12° threshold each frame, but 10 polls accumulate ≥ 40° → ticks.
            yield return Hold(new BackendSnapshot { CrankDeltaDegrees = 4f }, frames: 10);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 1, "sub-threshold deltas accumulate into MoneyTicks");
            Assert.IsTrue(_received.All(i => i == GameInput.MoneyTick),
                $"accumulated crank emits ONLY MoneyTick (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        // ---- height sensor (HeightA) → HELD EnergyHold -------------------------------------------------

        [UnityTest]
        public IEnumerator HeightA_Held_Reemits_EnergyHold_Every_Frame_And_Nothing_Else()
        {
            // ⚠ РЕДИЗАЙН 2026-08-07: датчик — УДЕРЖИВАЕМЫЙ контрол, как и джойстик отношений. Пока он выше
            // середины хода, источник переиздаёт EnergyHold КАЖДЫЙ кадр (раньше выдавался один импульс на
            // подъём — под механику ритма, которой больше нет).
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 6);
            Assert.GreaterOrEqual(_received.Count, 5,
                "поднятый датчик — HELD-сигнал: он переиздаётся каждый кадр, а не один раз на подъём");
            Assert.IsTrue(_received.All(i => i == GameInput.EnergyHold),
                $"…и ничего, кроме EnergyHold (получено: {string.Join(",", _received)})");

            // Опустили ниже порога отпускания — сигнал ГАСНЕТ, новых событий нет.
            yield return Release();
            int afterRelease = _received.Count;
            yield return Hold(new BackendSnapshot { HeightA = 0.2f }, frames: 4);   // ниже High и ниже Low
            Assert.AreEqual(afterRelease, _received.Count,
                "опущенный датчик не шлёт ничего — рост энергии обязан прекращаться");

            // Подняли снова — сигнал вернулся сам, без всякого «перевзвода».
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 4);
            Assert.Greater(_received.Count, afterRelease, "подняли снова — сигнал снова идёт");
            Assert.IsTrue(_received.All(i => i == GameInput.EnergyHold), "…и по-прежнему только EnergyHold");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator HeightA_BetweenTheHysteresisThresholds_DoesNotChatter()
        {
            // Гистерезис: значение между Low и High не переключает состояние. Поднялись выше High — держим;
            // просели до 0.4 (ниже High, но выше Low) — сигнал ДЕРЖИТСЯ (дрожание руки не рвёт удержание).
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 3);
            int held = _received.Count;
            Assert.Greater(held, 0, "датчик поднят");
            yield return Hold(new BackendSnapshot { HeightA = 0.4f }, frames: 4);   // Low < 0.4 < High
            Assert.Greater(_received.Count, held,
                "между порогами сигнал НЕ рвётся — иначе дрожь руки мигала бы батареей");
            yield return Cleanup();
        }

        // ---- ОБА датчика заряжают энергию (r7 п.2) ----------------------------------------------------
        // Жалоба основательницы: «сделать оба датчика, чтобы работали». До r7 батарею поднимал только A —
        // второй датчик на стойке был мёртвым органом, и игрок, взявшийся за него, ничего не добивался.

        /// <summary>
        /// ⚠ ГАРД «ЛЮБОЙ ИЗ ДВУХ ЗАРЯЖАЕТ», сторона B. Mutation-proof по обоим направлениям: он красный и
        /// когда B забыли подключить (получим 0 событий), и когда A случайно отвязали (тогда красным
        /// станет парный HeightA-тест выше). Один тест на оба датчика не годился бы: он бы прошёл, пока
        /// работает ХОТЯ БЫ один, то есть ровно на той поломке, которую чинит инкремент.
        /// </summary>
        [UnityTest]
        public IEnumerator HeightB_Held_Reemits_EnergyHold_JustLikeHeightA()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightB = 0.8f }, frames: 6);
            Assert.GreaterOrEqual(_received.Count, 5,
                "ВТОРОЙ датчик держит батарею ровно так же, как первый — это HELD-сигнал каждый кадр");
            Assert.IsTrue(_received.All(i => i == GameInput.EnergyHold),
                $"…и ничего, кроме EnergyHold (получено: {string.Join(",", _received)})");

            yield return Release();
            int afterRelease = _received.Count;
            yield return Hold(new BackendSnapshot { HeightB = 0.2f }, frames: 4);
            Assert.AreEqual(afterRelease, _received.Count, "опущенный B не шлёт ничего — как и опущенный A");
            yield return Cleanup();
        }

        /// <summary>
        /// ⚠ ДВА РАЗОМ НЕ БЫСТРЕЕ ОДНОГО (умолчание Maintainer'а: MAX, не сумма). Жест — «одной рукой»,
        /// и вторая рука не обязана удваивать скорость: иначе у стойки появился бы скрытый «турбо-режим»,
        /// который никто не объяснял и который ломает калибровку «батарея за 4–6 с».
        ///
        /// Проверяем на УРОВНЕ ИСТОЧНИКА (ровно один Emit за кадр), а не только на латче в Game: латч —
        /// свойство Game, и мутация «шлём по событию с каждого датчика» его бы не потревожила, зато
        /// удвоила бы работу на каждом кадре и звук жеста.
        /// </summary>
        [UnityTest]
        public IEnumerator BothSensorsHeld_ChargeNoFasterThanOne_MaxNotSum()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f }, frames: 6);
            int oneHand = _received.Count;
            Assert.Greater(oneHand, 0, "одна рука заряжает");

            yield return Release();
            _received.Clear();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f, HeightB = 0.8f }, frames: 6);
            int twoHands = _received.Count;

            Assert.AreEqual(oneHand, twoHands,
                $"оба датчика разом дают СТОЛЬКО ЖЕ EnergyHold, сколько один ({twoHands} против {oneHand}) — "
                + "это MAX, а не сумма: вторая рука не ускоряет зарядку");
            yield return Cleanup();
        }

        /// <summary>
        /// ⚠ ДЕТЕКТОРЫ НЕЗАВИСИМЫ. Отпустили A, продолжая держать B — сигнал обязан ИДТИ. Классическая
        /// поломка, которую это ловит: второй датчик не опрашивается, пока поднят первый, и застревает в
        /// том состоянии, в котором его застали («перехват руки посреди зарядки»).
        ///
        /// ⚠ ЧТО ИМЕННО ЗДЕСЬ МУТАЦИЯ. Не знак «|»/«||» в условии источника: вызовы <c>Step</c> вынесены
        /// в локальные переменные и выполняются безусловно, так что на готовых bool'ах «|» и «||» — одно
        /// и то же (мутация 2026-09-22: гард остался зелёным, потому что поломки не было). Ловится
        /// ДРУГАЯ форма — втягивание вызовов обратно в условие,
        /// <c>if (_breathSensorA.Step(a) || _breathSensorB.Step(b))</c>: вот она теряет шаг второго
        /// детектора. Проверено: инлайн с «||» — красный, инлайн с «|» — зелёный.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleasingOneSensor_WhileTheOtherIsHeld_KeepsCharging()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f, HeightB = 0.8f }, frames: 4);

            // ⚠ КЛЮЧЕВОЙ КАДР ГАРДА — БЕЗ НЕГО ТЕСТ ВАКУУМЕН. B проседает В ПОЛОСУ ГИСТЕРЕЗИСА
            // (Low 0.35 < 0.40 < High 0.5), пока A ещё поднят:
            //   • на «|»  детектор B шагает и УДЕРЖИВАЕТ «поднят» (0.40 выше порога отпускания);
            //   • на «||» детектор B в эти кадры не опрашивается вовсе и остаётся «опущен» — а потом
            //     уже не поднимется, потому что 0.40 НИЖЕ порога подъёма.
            // Пока тест ходил 0.8 / 0.0, то есть ВНЕ полосы, обе версии давали один и тот же результат:
            // 0.8 переваливает порог подъёма сам по себе, и «||» успевало догнать на первом же кадре
            // после отпускания A. Расходимость живёт ровно внутри полосы.
            yield return Hold(new BackendSnapshot { HeightA = 0.8f, HeightB = 0.40f }, frames: 2);
            _received.Clear();

            // Рука ушла с A, B остался в полосе удержания — перехват посреди жеста.
            yield return Hold(new BackendSnapshot { HeightA = 0.0f, HeightB = 0.40f }, frames: 4);
            Assert.Greater(_received.Count, 0, "отпустил A, держу B — зарядка продолжается");
            Assert.IsTrue(_received.All(i => i == GameInput.EnergyHold), "…и это по-прежнему EnergyHold");

            // И симметрично: ушли с B, вернулись на A.
            _received.Clear();
            yield return Hold(new BackendSnapshot { HeightA = 0.8f, HeightB = 0.0f }, frames: 4);
            Assert.Greater(_received.Count, 0, "отпустил B, держу A — зарядка продолжается");

            // Отпустили ОБА — тишина.
            _received.Clear();
            yield return Hold(new BackendSnapshot { HeightA = 0.0f, HeightB = 0.0f }, frames: 4);
            CollectionAssert.IsEmpty(_received, "оба опущены — рост обязан прекратиться");
            yield return Cleanup();
        }

        // ---- joystick HORIZONTAL → relationship balancer (r7 п.1) --------------------------------------
        // ⚠ Ось переехала с ВЕРТИКАЛИ на ГОРИЗОНТАЛЬ: шкала отношений нарисована горизонтальным
        // балансиром (лицо парня слева, лицо девушки справа, сердце-маркер ездит между ними), и рычаг
        // обязан совпадать с картинкой. Вправо = рост шкалы = маркер вправо.

        [UnityTest]
        public IEnumerator Joystick_Right_Reemits_RelationRight_Every_Frame_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(1f, 0f) }, frames: 4);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 2, "a HELD axis re-emits every frame (not a single edge)");
            Assert.IsTrue(_received.All(i => i == GameInput.RelationRight),
                $"joystick RIGHT emits ONLY RelationRight (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Joystick_Left_Reemits_RelationLeft_And_Nothing_Else()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(-1f, 0f) }, frames: 4);
            yield return Release();
            Assert.GreaterOrEqual(_received.Count, 2, "a HELD axis re-emits every frame");
            Assert.IsTrue(_received.All(i => i == GameInput.RelationLeft),
                $"joystick LEFT emits ONLY RelationLeft (got: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        /// <summary>
        /// ⚠ ГАРД ПЕРЕЕЗДА ОСИ (r7 п.1): ВЕРТИКАЛЬ джойстика на отношения НЕ влияет — не шлёт ВООБЩЕ
        /// ничего. Без него переезд прошёл бы и при ошибочном «x ИЛИ y»: горизонталь бы заработала,
        /// жалоба основательницы закрылась бы, а рычаг остался бы двусмысленным (вверх всё ещё тянет,
        /// и на стойке игрок снова тянет не туда). Проверяем ОБА знака, чтобы ни один не проскочил.
        /// </summary>
        [UnityTest]
        public IEnumerator Joystick_Vertical_DoesNotTouchTheBalancer_AtAll()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(0f, 1f) }, frames: 4);
            yield return Release();
            CollectionAssert.IsEmpty(_received,
                "ВВЕРХ больше не тянет отношения — балансир горизонтальный, вертикаль обязана молчать");

            yield return Hold(new BackendSnapshot { Joystick = new Vector2(0f, -1f) }, frames: 4);
            yield return Release();
            CollectionAssert.IsEmpty(_received, "…и ВНИЗ тоже молчит");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Joystick_Inside_Deadzone_Emits_Nothing()
        {
            yield return Boot();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(0.3f, 0f) }, frames: 4);
            yield return Release();
            CollectionAssert.IsEmpty(_received, "a wobble inside the deadzone must not pull the balancer");
            yield return Cleanup();
        }

        // ---- ПОЛЯРНОСТЬ ОСИ НА СТОЙКЕ (r7, находка код-скептика) ---------------------------------------
        //
        // Ориентация X — свойство физической сборки, а не кода: модуль джойстика уже дважды пере-подключали,
        // и знак переворачивался (CONTROLS_BRIEF §5). Пакетный SerialTuning.InvertJoystickX нам недоступен
        // (раннер пакета строит SerialTuning.Default жёстко, пакет трогать нельзя), поэтому множитель ±1
        // живёт в game.json и применяется игрой. Проверить полярность может только живой человек у автомата
        // — задача этих гардов в том, чтобы к моменту проверки у него БЫЛА ручка, и чтобы она крутила
        // ровно то, что должна.

        /// <summary>
        /// ⚠ ЗНАК −1 ЗЕРКАЛИТ ЖЕЛЕЗНУЮ ОСЬ. Бэкенд теста не реализует <c>IKeyboardEmulation</c>, значит для
        /// <c>ArcadeInput.IsEmulated</c> это НЕ эмуляция — железный путь. Джойстик уехал ВПРАВО, а игра
        /// обязана прочитать это как ВЛЕВО.
        ///
        /// Mutation-proof: убрать умножение на знак — тест краснеет (придёт RelationRight). Парный тест
        /// ниже краснеет на обратной мутации «применять знак всегда».
        /// </summary>
        [UnityTest]
        public IEnumerator RelationAxisSign_MinusOne_Mirrors_TheCabinetJoystick()
        {
            GameConfig.DebugUseJson("{\"RelationAxisSign\": -1}");   // до Boot: источник читает знак на Awake
            yield return Boot();

            yield return Hold(new BackendSnapshot { Joystick = new Vector2(1f, 0f) }, frames: 4);
            yield return Release();
            Assert.Greater(_received.Count, 0, "ось всё ещё тянет — зеркало переворачивает знак, а не глушит");
            Assert.IsTrue(_received.All(i => i == GameInput.RelationLeft),
                "при RelationAxisSign = −1 отклонение ВПРАВО на стойке читается как ВЛЕВО "
                + $"(получено: {string.Join(",", _received)})");

            // …и симметрично: влево читается как вправо, а не «влево не работает».
            _received.Clear();
            yield return Hold(new BackendSnapshot { Joystick = new Vector2(-1f, 0f) }, frames: 4);
            yield return Release();
            Assert.IsTrue(_received.Count > 0 && _received.All(i => i == GameInput.RelationRight),
                $"…и ВЛЕВО читается как ВПРАВО (получено: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        /// <summary>
        /// ⚠ И ТОТ ЖЕ ЗНАК НЕ ТРОГАЕТ КЛАВИАТУРУ. «←» — это влево, «→» — вправо; зеркалится ПРОВОДКА
        /// СТОЙКИ, а не смысл стрелок. Иначе человек, чинящий полярность автомата, ломает дев-клавиатуру
        /// всем остальным — и §D-подсказка «эмуляция: ← / →» начинает врать.
        ///
        /// Mutation-proof: снять проверку <c>IsEmulated</c> (применять знак всегда) — тест краснеет.
        /// </summary>
        [UnityTest]
        public IEnumerator RelationAxisSign_MinusOne_DoesNotTouch_TheKeyboardEmulation()
        {
            GameConfig.DebugUseJson("{\"RelationAxisSign\": -1}");
            yield return BootEmulated();

            yield return HoldEmulated(new BackendSnapshot { Joystick = new Vector2(1f, 0f) }, frames: 4);
            Assert.Greater(_received.Count, 0, "клавиатурная ось работает");
            Assert.IsTrue(_received.All(i => i == GameInput.RelationRight),
                "на ЭМУЛЯЦИИ «→» остаётся «вправо» при любом знаке проводки стойки "
                + $"(получено: {string.Join(",", _received)})");

            _received.Clear();
            yield return HoldEmulated(new BackendSnapshot { Joystick = new Vector2(-1f, 0f) }, frames: 4);
            Assert.IsTrue(_received.Count > 0 && _received.All(i => i == GameInput.RelationLeft),
                $"…и «←» остаётся «влево» (получено: {string.Join(",", _received)})");
            yield return Cleanup();
        }

        /// <summary>Умолчание +1: без конфига и без правок ось работает ровно как до r7-полярности.</summary>
        [UnityTest]
        public IEnumerator RelationAxisSign_DefaultsToPlusOne_WhenTheConfigSaysNothing()
        {
            Assert.AreEqual(1f, GameConfig.SignFromJson("{\"id\": \"нет такого поля\"}"),
                "нет ключа — знак +1");
            GameConfig.DebugUseJson("{\"id\": \"нет такого поля\"}");
            yield return Boot();

            yield return Hold(new BackendSnapshot { Joystick = new Vector2(1f, 0f) }, frames: 4);
            yield return Release();
            Assert.IsTrue(_received.Count > 0 && _received.All(i => i == GameInput.RelationRight),
                "без конфига вправо остаётся вправо");
            yield return Cleanup();
        }

        [UnityTest]
        public IEnumerator Zero_Snapshot_Emits_Nothing()
        {
            yield return Boot();
            yield return Hold(default, frames: 5);
            CollectionAssert.IsEmpty(_received, "an idle cabinet emits no GameInput at all");
            yield return Cleanup();
        }
    }
}
