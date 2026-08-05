using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ЛИНГЕРИНГ-СИМ РЕАЛЬНОГО ПУТИ ДЫХАНИЯ НА КЛАВИАТУРЕ — регрессия на «туториал энергии непроходим»
    /// (живой плейтест основательницы 2026-08-05: «жму — ничего не происходит», тест остановлен).
    ///
    /// Гоняются НАСТОЯЩИЕ звенья, а не их пересказ:
    ///   1. <see cref="HeightSimulator"/> пакета arcade-controls, заведённый ИЗ ПАКЕТНОГО КОНФИГА
    ///      (<see cref="KeyboardMapping.LoadDefault"/>) — т.е. тест падает, если кто-то поменяет
    ///      скорости эмуляции в JSON пакета, не сверившись с окном ритма;
    ///   2. <see cref="BreathLever"/> — тот же детектор фронта, что стоит в ArcadeInputSource;
    ///   3. <see cref="BreathRhythm"/> — тот же ритм-гейт, что стоит в GameDriver;
    ///   4. условие выхода §D-экрана энергии: «первый ПРИНЯТЫЙ вдох взводит окно, дальше энергия
    ///      &gt;<see cref="GameDriver.NewScaleEnergyAbove"/> % держится <see cref="GameDriver.NewScaleHoldSeconds"/> с».
    ///      Энергия на открытии = 100 %, а под модалкой время заморожено (Game.Paused), поэтому она НЕ
    ///      убывает — считаем удержание от взвода, ровно как TickNewScale.
    ///
    /// Что доказывается: живой человек на клавиатуре проходит окно ЗА СЕКУНДЫ во ВСЁМ комфортном
    /// диапазоне каденций, а заколачивание по-прежнему не лечит.
    /// </summary>
    public class BreathKeyboardPathTests
    {
        private const float Dt = 1f / 60f;

        /// <summary>Результат прогона: когда взвелось окно и когда закрылось (сек), сколько вдохов принято.</summary>
        private readonly struct Run
        {
            public readonly float ArmedAt, DoneAt;
            public readonly int Accepted;
            public Run(float armedAt, float doneAt, int accepted)
            {
                ArmedAt = armedAt; DoneAt = doneAt; Accepted = accepted;
            }
            public bool Passed => DoneAt > 0f;
        }

        /// <summary>Игрок как функция времени: держит ли он «вверх» / «вниз» в секунду t.</summary>
        private delegate void Player(float t, out bool up, out bool down);

        private static Run Simulate(Player player, float seconds = 60f)
        {
            KeyboardMapping map = KeyboardMapping.LoadDefault();
            var sensor = new HeightSimulator(map.HeightRiseUnitsPerSecond, map.HeightFallUnitsPerSecond,
                                             0f, map.HeightReturnUnitsPerSecond);
            var lever = new BreathLever();
            var rhythm = new BreathRhythm();

            float t = 0f, armedAt = 0f, doneAt = 0f, hold = 0f;
            int accepted = 0;
            bool armed = false;

            while (t < seconds && doneAt <= 0f)
            {
                player(t, out bool up, out bool down);
                sensor.Update(up, down, Dt);
                rhythm.Advance(Dt);

                if (lever.Step(sensor.Value) && rhythm.Pulse())
                {
                    accepted++;
                    if (!armed) { armed = true; armedAt = t; }
                }

                t += Dt;

                // Взведено → энергия 100 % (>40 %) и заморожена: копится непрерывное удержание.
                if (armed)
                {
                    hold += Dt;
                    if (hold >= GameDriver.NewScaleHoldSeconds) doneAt = t;
                }
            }
            return new Run(armedAt, doneAt, accepted);
        }

        // Один рычаг, человеческий вдох-выдох: держим `hold` секунд, отпускаем `rest` секунд.
        private static Player OneKey(float hold, float rest)
        {
            float period = hold + rest;
            return (float t, out bool up, out bool down) =>
            {
                up = t % period < hold;
                down = false;
            };
        }

        // Ручное чередование «вверх/вниз» (на стойке — рука вверх и вниз, на клавиатуре — две клавиши).
        private static Player TwoKeys(float up_, float down_)
        {
            float period = up_ + down_;
            return (float t, out bool up, out bool down) =>
            {
                float x = t % period;
                up = x < up_;
                down = !up;
            };
        }

        // ---------------------------------------------------------------- удобные каденции ПРОХОДЯТ

        [Test]
        public void ComfortableHumanCadences_PassTheEnergyTutorial_InSeconds()
        {
            // Основательница просила «удобный человеческий» цикл вдох-выдох ~1.5–2.5 с. Берём его с
            // запасом (1.0–3.0 с) и с разной долей удержания — люди не метрономы.
            foreach (float cycle in new[] { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f })
                foreach (float duty in new[] { 0.4f, 0.5f, 0.6f })
                {
                    var r = Simulate(OneKey(cycle * duty, cycle * (1f - duty)));
                    Assert.IsTrue(r.Passed,
                        $"цикл {cycle:0.0} с (удержание {duty:P0}) ОДНОЙ клавишей обязан проходить окно энергии, "
                        + $"а принято вдохов: {r.Accepted}");
                    Assert.Less(r.DoneAt, 20f,
                        $"цикл {cycle:0.0} с: окно закрылось за {r.DoneAt:0.0} с — контракт ≤~20 с");
                }
        }

        [Test]
        public void ManualUpDownAlternation_AlsoPasses()
        {
            // Тот же жест «рукой вверх-вниз» (на стойке — единственный возможный), цикл 2 с.
            var r = Simulate(TwoKeys(1.0f, 1.0f));
            Assert.IsTrue(r.Passed, "чередование вверх/вниз с циклом 2 с обязано проходить окно");
            Assert.Less(r.DoneAt, 20f);
        }

        [Test]
        public void FirstBreath_IsAcceptedWithinACoupleOfCycles()
        {
            // Взвод (первый ПРИНЯТЫЙ вдох) не должен требовать разминки: первый импульс только засевает
            // каденцию, значит принятым становится ВТОРОЙ — это ≤ двух циклов, а не десяти.
            var r = Simulate(OneKey(1.0f, 1.0f));
            Assert.IsTrue(r.Passed);
            Assert.LessOrEqual(r.ArmedAt, 2.5f * 2f,
                $"окно взвелось только на {r.ArmedAt:0.0} с — это дольше двух циклов дыхания");
        }

        // ---------------------------------------------------------------- механика СОХРАНЕНА

        [Test]
        public void Mashing_StillHealsNothing()
        {
            // Заколачивание (каденция < 0.3 с) не даёт НИ ОДНОГО принятого вдоха: на клавиатуре порог
            // хода датчика просто не успевает набраться, на живом рычаге его режет RhythmMin.
            foreach (float cycle in new[] { 0.1f, 0.2f, 0.3f })
            {
                var r = Simulate(OneKey(cycle * 0.5f, cycle * 0.5f), seconds: 20f);
                Assert.AreEqual(0, r.Accepted, $"заколачивание с циклом {cycle:0.0} с не должно лечить");
                Assert.IsFalse(r.Passed);
            }

            // …и то же самое двумя клавишами, чтобы «обход через две руки» тоже не работал.
            var two = Simulate(TwoKeys(0.15f, 0.15f), seconds: 20f);
            Assert.AreEqual(0, two.Accepted, "заколачивание вверх/вниз тоже не лечит");
        }

        [Test]
        public void HoldingTheLeverForever_IsNotBreathing()
        {
            // ДОКУМЕНТИРОВАННОЕ свойство механики, а не баг: поднятая и удерживаемая рука — не дыхание,
            // она даёт ровно один фронт. Именно этим кончился плейтест 2026-08-05, поэтому строка-подсказка
            // датчика НАЗЫВАЕТ движение словами (GameDriver.BreathKeyHintTail) — «нажимай и отпускай».
            var r = Simulate((float t, out bool up, out bool down) => { up = true; down = false; });
            Assert.IsFalse(r.Passed, "удержание не должно закрывать окно — это не ритм");
            StringAssert.Contains("отпускай", GameDriver.BreathKeyHintTail,
                "…и раз так, подсказка обязана прямо просить ОТПУСКАТЬ");
        }

        // ---------------------------------------------------------------- согласованность чисел

        [Test]
        public void PackageEmulationSpeeds_AreConsistentWithTheRhythmWindow()
        {
            // Числа пакета и числа игры — ОДНА калибровка: если кто-то поменяет одну сторону, тест
            // объяснит, какая связь порвалась (диагностика в docs/increments/2026-08-05-playtest-fixes-r1.md).
            KeyboardMapping map = KeyboardMapping.LoadDefault();
            var rhythm = new BreathRhythm();

            // ⚠ ЗАПИНЕННАЯ КАЛИБРОВКА 2026-08-05. Ниже — ОБЕ стороны связки, числом. МЕНЯЕШЬ ОДНО —
            // МЕНЯЙ ОБА И ЭТИ АССЕРТЫ: скорости эмуляции живут в конфиге ПАКЕТА arcade-controls
            // (Runtime/Resources/ArcadeControls/default-keyboard-mapping.json, читается прямо здесь через
            // LoadDefault — т.е. это сверка с РЕАЛЬНЫМ файлом, а не с копией числа), окно ритма — в игре
            // (BreathRhythm). Порознь они дают ровно тот баг, из-за которого встал плейтест: игрок дышит,
            // а игра молчит. Соотношения, которые эти числа обязаны держать, проверены ниже.
            Assert.AreEqual(1.25f, map.HeightRiseUnitsPerSecond, 1e-4f,
                "пакетный heightRiseUnitsPerSecond обязан быть 1.25 ед/с (калибровка 2026-08-05)");
            Assert.AreEqual(1.25f, map.HeightFallUnitsPerSecond, 1e-4f,
                "пакетный heightFallUnitsPerSecond обязан быть 1.25 ед/с");
            Assert.AreEqual(2.0f, map.HeightReturnUnitsPerSecond, 1e-4f,
                "пакетный heightReturnUnitsPerSecond (пружина в покой) обязан быть 2.0 ед/с");
            Assert.AreEqual(0.4, rhythm.RhythmMin, 1e-9,
                "низ окна ритма обязан быть 0.4 с — это и есть защита от заколачивания");
            Assert.AreEqual(3.0, rhythm.RhythmMax, 1e-9,
                "верх окна ритма обязан быть 3.0 с — под комфортный человеческий вдох-выдох 1.5–2.5 с");

            float toThreshold = BreathLever.High / map.HeightRiseUnitsPerSecond;   // с покоя до фронта
            Assert.Less(toThreshold, BreathRhythm.TargetSeconds / 2f,
                "подъём датчика обязан добивать до середины хода БЫСТРЕЕ, чем за полцикла дыхания, "
                + "иначе комфортный вдох не даёт импульса ВООБЩЕ (корень бага 2026-08-05)");

            Assert.Greater(map.HeightReturnUnitsPerSecond, 0f,
                "эмуляция обязана возвращаться в покой сама — иначе один вдох за жизнь");
            float fromTopToRearm = (1f - BreathLever.Low) / map.HeightReturnUnitsPerSecond;
            Assert.Less(fromTopToRearm, BreathRhythm.TargetSeconds / 2f,
                "с полного хода датчик обязан успевать перевзвестись за полцикла");

            Assert.GreaterOrEqual(rhythm.RhythmMax, 2.5,
                "верх окна обязан вмещать удобный человеческий цикл 1.5–2.5 с");
            Assert.LessOrEqual(rhythm.RhythmMin, 0.4,
                "низ окна — защита от заколачивания, его не задирают");
        }
    }
}
