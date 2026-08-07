using System.Collections.Generic;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ЛИНГЕРИНГ-СИМ РЕАЛЬНОГО ПУТИ ЭНЕРГИИ НА КЛАВИАТУРЕ — регрессия на два живых плейтеста подряд:
    /// «жму — ничего не происходит» (2026-08-05) и «тяжело играется — не понимаю про две секунды»
    /// (2026-08-07, редизайн: «просто зажать датчик высоты, пока батарейка не заполнится»).
    ///
    /// Гоняются НАСТОЯЩИЕ звенья, а не их пересказ:
    ///   1. <see cref="HeightSimulator"/> пакета arcade-controls, заведённый ИЗ ПАКЕТНОГО КОНФИГА
    ///      (<see cref="KeyboardMapping.LoadDefault"/>) — т.е. тест падает, если кто-то поменяет скорости
    ///      эмуляции в JSON пакета, не сверившись с механикой;
    ///   2. <see cref="BreathSensor"/> — тот же детектор «датчик поднят», что стоит в ArcadeInputSource,
    ///      и та же модель «переиздавать held-сигнал каждый кадр»;
    ///   3. ПУРЕ <see cref="Game"/> — та же арифметика энергии (реген под удержанием, дренаж всегда), со
    ///      шкалой, открытой её собственным путём (возраст 25), а не выставленным руками флагом;
    ///   4. условие выхода §D-экрана энергии: батарея ЗАПОЛНИЛАСЬ
    ///      (≥<see cref="GameDriver.NewScaleEnergyFull"/> %) — ровно то, что проверяет TickNewScale.
    ///
    /// Что доказывается: игрок, который ЗАЖАЛ клавишу датчика, проходит окно за обещанные 4–6 с; игрок,
    /// который её не трогает, не растёт вовсе и в живой игре просаживается к выгоранию.
    /// </summary>
    public class BreathKeyboardPathTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Игрок как функция времени: держит ли он клавишу «вверх» / «вниз» в секунду t.</summary>
        private delegate void Player(float t, out bool up, out bool down);

        // ---------------------------------------------------------------- обвязка сима

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        /// <summary>
        /// Игра, доведённая до ОТКРЫТОЙ энергии её собственным путём (возраст 25). Колода — филлер 26…40:
        /// возраст событийный, поэтому он замирает на возрасте текущей карточки и не доезжает ни до кризиса
        /// (45), ни до конца колоды — сим меряет ровно энергию, и ничего кроме.
        /// </summary>
        private static Game GameWithEnergyOpen()
        {
            var deck = new List<Card> { Starter() };
            for (int a = 26; a <= 40; a++) deck.Add(Plain("F" + a, a));
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);      // I03 отвечен → пошёл возраст
            int guard = 0;
            while (!g.EnergyOpen && guard++ < 4000) g.Tick(0.05f);
            Assert.IsTrue(g.EnergyOpen, "сим обязан дойти до открытия энергии");
            Assert.AreEqual(Game.EnergyOpenValue, g.Scales.Energy,
                "шкала открывается ПРОСЕВШЕЙ — иначе «держи, пока не заполнится» нечего выполнять");
            return g;
        }

        private readonly struct Run
        {
            public readonly float FullAt;      // секунда, на которой батарея заполнилась (0 — не заполнилась)
            public readonly int MinEnergy, MaxEnergy;
            public Run(float fullAt, int minE, int maxE) { FullAt = fullAt; MinEnergy = minE; MaxEnergy = maxE; }
            public bool Passed => FullAt > 0f;
        }

        /// <summary>
        /// Прогон под §D-МОДАЛКОЙ энергии: время заморожено (<see cref="Game.Paused"/>), живы только
        /// контролы (<see cref="Game.PausedInputsLive"/>) — ровно то состояние, в котором игрок видит
        /// экран-туториал.
        /// </summary>
        private static Run SimulateTutorial(Player player, float seconds = 30f)
        {
            var g = GameWithEnergyOpen();
            g.Paused = true; g.PausedInputsLive = true;      // §D-модалка поднята
            return Simulate(g, player, seconds);
        }

        /// <summary>Тот же путь, но в ЖИВОЙ игре: дренаж идёт, время идёт.</summary>
        private static Run SimulateLive(Player player, float seconds = 30f)
            => Simulate(GameWithEnergyOpen(), player, seconds);

        private static Run Simulate(Game g, Player player, float seconds)
        {
            KeyboardMapping map = KeyboardMapping.LoadDefault();
            var height = new HeightSimulator(map.HeightRiseUnitsPerSecond, map.HeightFallUnitsPerSecond,
                                             0f, map.HeightReturnUnitsPerSecond);
            var sensor = new BreathSensor();

            float t = 0f, fullAt = 0f;
            int minE = g.Scales.Energy, maxE = g.Scales.Energy;

            while (t < seconds && fullAt <= 0f && g.State == GameState.Playing)
            {
                player(t, out bool up, out bool down);
                height.Update(up, down, Dt);

                // ArcadeInputSource: held-сигнал ПЕРЕИЗДАЁТСЯ каждый кадр, пока датчик поднят.
                if (sensor.Step(height.Value)) g.HandleInput(GameInput.EnergyHold);

                g.Tick(Dt);
                t += Dt;

                minE = System.Math.Min(minE, g.Scales.Energy);
                maxE = System.Math.Max(maxE, g.Scales.Energy);
                if (g.Scales.Energy >= GameDriver.NewScaleEnergyFull) fullAt = t;
            }
            return new Run(fullAt, minE, maxE);
        }

        /// <summary>«Зажми и держи»: клавиша датчика зажата всё время.</summary>
        private static Player HoldForever()
            => (float t, out bool up, out bool down) => { up = true; down = false; };

        /// <summary>Пассивный игрок: не трогает ничего.</summary>
        private static Player Idle()
            => (float t, out bool up, out bool down) => { up = false; down = false; };

        /// <summary>Прерывистое удержание: держит <paramref name="hold"/> с, отпускает <paramref name="rest"/> с.</summary>
        private static Player Pumping(float hold, float rest)
        {
            float period = hold + rest;
            return (float t, out bool up, out bool down) => { up = t % period < hold; down = false; };
        }

        // ---------------------------------------------------------------- §1: туториал = «зажми и держи»

        [Test]
        public void HoldingTheKey_FillsTheBattery_InFourToSixSeconds()
        {
            // ДОСЛОВНОЕ обещание инкремента: «в туториале с типового старта батарея до 100 % за ~4–6 с».
            var r = SimulateTutorial(HoldForever());
            Assert.IsTrue(r.Passed, "зажатая клавиша обязана заполнить батарею — это и есть задача экрана");
            Assert.GreaterOrEqual(r.FullAt, 3.5f,
                $"батарея заполнилась за {r.FullAt:0.00} с — быстрее, чем игрок успевает прочитать задачу");
            Assert.LessOrEqual(r.FullAt, 6.5f,
                $"батарея заполнялась {r.FullAt:0.00} с — дольше обещанных ~4–6 с");
        }

        [Test]
        public void ReleasingTheKey_StopsTheGrowth_TutorialNeverCloses()
        {
            // Отпустил → рост встал. Под модалкой дренажа нет, поэтому шкала просто СТОИТ — и окно не уходит
            // никогда, сколько ни жди. Это и есть «живая шкала требует активного ввода».
            var r = SimulateTutorial(Idle(), seconds: 30f);
            Assert.IsFalse(r.Passed, "без поднятого датчика батарея не наполняется — окно стоит");
            Assert.AreEqual(Game.EnergyOpenValue, r.MaxEnergy,
                "энергия не выросла НИ НА ПРОЦЕНТ, пока датчик не поднят");
        }

        [Test]
        public void PartialHolding_StillFills_ButSlower_TheMechanicCountsHeldTime()
        {
            // Прерывистое удержание («нажимай и отпускай» — прежний ритм-жест) ТОЖЕ наполняет батарею, просто
            // медленнее: механика больше никого не наказывает за жест, она считает ВРЕМЯ С ПОДНЯТЫМ ДАТЧИКОМ.
            // Гард на «случайно вернули гейт»: если импульсы снова начнут отвергаться, этот прогон встанет.
            var r = SimulateTutorial(Pumping(0.5f, 0.5f), seconds: 60f);
            Assert.IsTrue(r.Passed, "прерывистое удержание тоже наполняет — отвергать в игре больше нечего");
            Assert.Greater(r.FullAt, 6.5f,
                "…но медленнее непрерывного удержания: считается именно время с поднятым датчиком");
        }

        // ---------------------------------------------------------------- §2: живая игра

        [Test]
        public void InLivePlay_HoldingBeatsTheDrain_AndFillsInAboutSixSeconds()
        {
            var r = SimulateLive(HoldForever(), seconds: 30f);
            Assert.IsTrue(r.Passed, "в живой игре удержание обязано уверенно перекрывать дренаж");
            Assert.LessOrEqual(r.FullAt, 7.5f,
                $"с дренажом батарея набралась за {r.FullAt:0.00} с — ожидается ≈6 с (реген 16 − дренаж 1.7)");
        }

        [Test]
        public void InLivePlay_DoingNothing_OnlyDrains()
        {
            // Пассивный игрок по-прежнему просаживается — инвариант «живая шкала требует ввода».
            var r = SimulateLive(Idle(), seconds: 6f);
            Assert.IsFalse(r.Passed);
            Assert.Less(r.MinEnergy, Game.EnergyOpenValue, "без ввода энергия только падает");
            Assert.AreEqual(Game.EnergyOpenValue, r.MaxEnergy, "…и ни разу не растёт");
        }

        // ---------------------------------------------------------------- §3: согласованность чисел

        [Test]
        public void PackageEmulationSpeeds_AreConsistentWithTheHoldMechanic()
        {
            // Числа пакета и числа игры — ОДНА калибровка: если кто-то поменяет одну сторону, тест объяснит,
            // какая связь порвалась.
            //
            // ⚠ ЗАПИНЕННАЯ КАЛИБРОВКА (r1 2026-08-05, СОХРАНЕНА при редизайне r2 2026-08-07). Скорости
            // эмуляции живут в конфиге ПАКЕТА arcade-controls
            // (Runtime/Resources/ArcadeControls/default-keyboard-mapping.json, читается прямо здесь через
            // LoadDefault — т.е. это сверка с РЕАЛЬНЫМ файлом, а не с копией числа). Для «зажми и держи» они
            // идеальны: клавиша поднимает датчик за ~0.4 с и держит его наверху, пока её не отпустят, а
            // пружина роняет его сама, как живая рука.
            KeyboardMapping map = KeyboardMapping.LoadDefault();
            Assert.AreEqual(1.25f, map.HeightRiseUnitsPerSecond, 1e-4f,
                "пакетный heightRiseUnitsPerSecond обязан быть 1.25 ед/с (калибровка 2026-08-05)");
            Assert.AreEqual(1.25f, map.HeightFallUnitsPerSecond, 1e-4f,
                "пакетный heightFallUnitsPerSecond обязан быть 1.25 ед/с");
            Assert.AreEqual(2.0f, map.HeightReturnUnitsPerSecond, 1e-4f,
                "пакетный heightReturnUnitsPerSecond (пружина в покой) обязан быть 2.0 ед/с");

            float toThreshold = BreathSensor.High / map.HeightRiseUnitsPerSecond;   // с покоя до «поднят»
            Assert.Less(toThreshold, 1f,
                "датчик обязан вставать «поднятым» меньше чем за секунду после зажатия клавиши — иначе "
                + "игрок жмёт и не видит реакции (корень бага 2026-08-05)");
            Assert.Greater(map.HeightReturnUnitsPerSecond, 0f,
                "эмуляция обязана возвращаться в покой сама — иначе отпускание клавиши не гасит сигнал");

            // …и время наполнения ВЫВОДИТСЯ из этих чисел и скорости регена, а не задаётся отдельно.
            double fillSeconds = (100.0 - Game.EnergyOpenValue) / Game.EnergyRegenPerSec + toThreshold;
            Assert.GreaterOrEqual(fillSeconds, 4.0, "полное наполнение обязано ощущаться как действие, а не миг");
            Assert.LessOrEqual(fillSeconds, 6.0, "…и укладываться в обещанные основательнице ~4–6 с");
        }

        [Test]
        public void HoldingBeatsTheDrain_ByAWideMargin_EverywhereEnergyIsOpen()
        {
            // Баланс-инвариант инкремента: удержание обязано УВЕРЕННО перекрывать дренаж — иначе «держи,
            // пока не заполнится» превращается в «держи вечно». Дренаж у энергии один на все возрасты.
            Assert.Greater(Game.EnergyRegenPerSec, Game.EnergyDrainPerSec * 5,
                "реген обязан перекрывать дренаж кратно, а не впритык");
            double exitBurnoutSeconds =
                (Game.BurnoutExitEnergyAbove + 1 - Game.BurnoutEnterEnergyAtOrBelow)
                / (Game.EnergyRegenPerSec - Game.EnergyDrainPerSec);
            Assert.LessOrEqual(exitBurnoutSeconds, 5.0,
                "выгорание обязано выходиться удержанием за несколько секунд, а не за полминуты");
        }

        // ---------------------------------------------------------------- §4: ритм-механики БОЛЬШЕ НЕТ

        [Test]
        public void TheRhythmGate_IsGone_NoTypeNoInput()
        {
            // Гард отсутствия (done-contract §7): ритм-гейт удалён из сборки целиком, вместе с типом и
            // семантическим входом «импульс». Если кто-то вернёт их «на всякий случай», тест это скажет.
            var runtime = typeof(Game).Assembly;
            Assert.IsNull(runtime.GetType("ThanksNoThanks.BreathRhythm"),
                "BreathRhythm снят по слову основательницы 2026-08-07 — ритма в игре больше нет");
            Assert.IsNull(runtime.GetType("ThanksNoThanks.BreathPulse"),
                "…и исход импульса вместе с ним");
            Assert.IsNull(runtime.GetType("ThanksNoThanks.BreathLever"),
                "…и детектор ФРОНТА: датчик теперь читается уровнем (BreathSensor)");
            Assert.IsFalse(System.Enum.IsDefined(typeof(GameInput), "EnergyPulse"),
                "семантический вход ENERGY_PULSE заменён удерживаемым ENERGY_HOLD");
            Assert.IsTrue(System.Enum.IsDefined(typeof(GameInput), "EnergyHold"),
                "…и он на месте");
        }
    }
}
