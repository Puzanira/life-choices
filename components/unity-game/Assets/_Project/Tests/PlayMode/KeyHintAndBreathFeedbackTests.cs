using System.Collections;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// ПОДСКАЗКИ КЛАВИШ (плейтест основательницы 2026-08-05 §2). Пока ввод идёт от клавиатурной эмуляции,
    /// каждый экран-объяснение показывает ВТОРОЙ строкой клавишу своего контрола — прочитанную ИЗ КОНФИГА
    /// ПАКЕТА (<see cref="KeyboardHints"/> поверх <see cref="KeyboardMapping"/>), а не из зашитой в игре
    /// буквы. Стоит плате ответить — строка исчезает: на стойке дев-клавиш быть не должно.
    ///
    /// У ДАТЧИКА ВЫСОТЫ строка вдобавок называет ЖЕСТ: «эмуляция: зажми Q» (редизайн 2026-08-07 — механика
    /// «зажми и держи»). Отклик «не в ритм» и вздрагивание батареи из r1 сняты вместе с ритм-гейтом:
    /// отвергать больше нечего, обратная связь — сама наполняющаяся батарея.
    /// </summary>
    public class KeyHintAndBreathFeedbackTests
    {
        private sealed class FakeSerial : ISerialBackend, IButtonPresence
        {
            public bool ProvidesHeights { get; set; }
            public bool ProvidesJoystick { get; set; }
            public bool ProvidesButtons { get; set; }
            public BackendSnapshot Poll(float deltaTime) => default;
        }

        private GameObject _go;
        private GameDriver _driver;
        private PlayFakeInputSource _fake;
        private FakeSerial _serial;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Driver");
            _driver = _go.AddComponent<GameDriver>();
            _fake = new PlayFakeInputSource();
            _driver.Input = _fake;

            // Композит «клавиатура всегда жива + платы сверху» — ровно то, что строит ArcadeInputRunner.
            _serial = new FakeSerial();
            ArcadeInput.Initialize(new CompositeBackend(new KeyboardBackend(KeyboardMapping.LoadDefault()), _serial));
        }

        [TearDown]
        public void TearDown()
        {
            ArcadeInput.Initialize(null);
            if (_go != null) Object.Destroy(_go);
        }

        private static string ExpectedHint(ArcadeControlId control)
        {
            string key = KeyboardHints.PrimaryFor(KeyboardMapping.LoadDefault(), control);
            return (control == ArcadeControlId.HeightA ? GameDriver.BreathKeyHintPrefix
                                                       : GameDriver.KeyHintPrefix) + key;
        }

        // ---------------------------------------------------------------- §2

        [UnityTest]
        public IEnumerator EveryOpenScreen_NamesItsKey_FromThePackageConfig()
        {
            yield return null;   // Start собрал HUD
            _fake.Yes();         // opener → playing (экраны открытий живут внутри жизни)
            yield return null;

            var cases = new (NewScale Scale, ArcadeControlId Control)[]
            {
                (NewScale.Energy, ArcadeControlId.HeightA),
                (NewScale.Relations, ArcadeControlId.Joystick),
                (NewScale.Money, ArcadeControlId.Crank),
                (NewScale.Child, ArcadeControlId.BangButton),
            };

            foreach (var (scale, control) in cases)
            {
                _driver.DebugShowNewScale(scale);
                yield return null;

                Assert.AreEqual(ExpectedHint(control), _driver.NewScaleHintLine.text,
                    scale + ": вторая строка обязана называть клавишу своего контрола из конфига пакета");
                Assert.IsTrue(_driver.NewScaleHintLine.gameObject.activeInHierarchy,
                    scale + ": строка подсказки реально на экране, а не просто заполнена");
                // Мелко и в стиле: кегль подсказки заметно меньше кегля самой задачи.
                Assert.Less(_driver.NewScaleHintLine.fontSize, _driver.NewScaleTaskText.fontSize,
                    scale + ": подсказка обязана быть МЕЛКОЙ второй строкой, а не спорить с задачей");

                _driver.DebugCloseNewScale();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator LiveBoard_HidesTheHint_AndUnpluggingBringsItBack()
        {
            yield return null;
            _fake.Yes();
            yield return null;
            _driver.DebugShowNewScale(NewScale.Energy);
            yield return null;
            Assert.AreNotEqual("", _driver.NewScaleHintLine.text, "без плат подсказка есть");

            _serial.ProvidesHeights = true;              // воткнули плату датчиков
            yield return null;
            Assert.AreEqual("", _driver.NewScaleHintLine.text,
                "плата ведёт датчик — дев-клавиша на стойке не показывается");

            _serial.ProvidesHeights = false;             // выдернули
            yield return null;
            Assert.AreEqual(ExpectedHint(ArcadeControlId.HeightA), _driver.NewScaleHintLine.text,
                "выдернули плату — подсказка вернулась сама, без перезапуска");
        }

        [UnityTest]
        public IEnumerator BurnoutHint_NamesTheSensorKeyToo()
        {
            yield return null;
            _driver.DebugShowTutorial("ВЫГОРАНИЕ!", ArcadeControlId.HeightA);
            yield return null;

            Assert.IsTrue(_driver.TutorialShowing);
            Assert.AreEqual(ExpectedHint(ArcadeControlId.HeightA), _driver.TutorialHintLine.text,
                "S5-подсказка про датчик тоже называет клавишу при эмуляции");

            // …а подсказка БЕЗ контрола (здоровье) второй строки не получает — лишнего шума нет.
            _driver.DebugDismissTutorial();
            yield return null;
            _driver.DebugShowTutorial("ЗДОРОВЬЕ НАЧАЛО ТАЯТЬ.");
            yield return null;
            Assert.AreEqual("", _driver.TutorialHintLine.text,
                "у подсказки без своего контрола второй строки нет");
        }

        [UnityTest]
        public IEnumerator KeyHintLine_IsNotRebuiltEveryFrame()
        {
            // Строка подсказки живёт на экране десятками секунд и раньше СОБИРАЛАСЬ заново каждый кадр
            // (склейка + StringBuilder внутри пакета) — ровный поток мусора под открытым окном.
            // Устоявшийся кадр обязан возвращать ТУ ЖЕ ссылку: сравнение по тексту здесь ничего не
            // доказало бы — одинаковую строку в Text.text просто не переприсваивают.
            yield return null;
            _fake.Yes();
            yield return null;
            _driver.DebugShowNewScale(NewScale.Energy);
            yield return null;

            string first = _driver.NewScaleHintCachedLine;
            Assert.IsNotEmpty(first, "без плат подсказка есть — иначе тест ничего не сторожит");

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
                Assert.IsTrue(ReferenceEquals(first, _driver.NewScaleHintCachedLine),
                    "устоявшийся кадр обязан отдавать ТУ ЖЕ строку, а не пересобирать её");
            }

            // …но кэш обязан замечать смену того, от чего строка зависит: воткнули плату — строка
            // пересчиталась и погасла, выдернули — вернулась.
            _serial.ProvidesHeights = true;
            yield return null;
            Assert.AreEqual("", _driver.NewScaleHintLine.text, "плата ведёт датчик — подсказки нет");

            _serial.ProvidesHeights = false;
            yield return null;
            Assert.AreEqual(ExpectedHint(ArcadeControlId.HeightA), _driver.NewScaleHintLine.text,
                "выдернули плату — кэш пересобрался сам");
        }

        [UnityTest]
        public IEnumerator KeyHintLine_IsLegibleOnTheCreamField()
        {
            // §6-дизайн: подсказка обязана быть ТИШЕ задачи, но читаемой с дистанции автомата. Замер
            // дизайн-скептика 2026-08-05: прежние 55 % чернил давали на креме 2.11:1 — мало.
            // Считаем ровно то, что видит глаз: полупрозрачные чернила, смешанные с кремом В ЛИНЕЙНОМ
            // пространстве (URP), затем WCAG-контраст к тому же крему.
            yield return null;
            _fake.Yes();
            yield return null;
            _driver.DebugShowNewScale(NewScale.Energy);
            yield return null;

            Color cream = GameDriver.CreamToken;
            Color rendered = OverCream(_driver.NewScaleHintLine.color, cream);
            float ratio = Contrast(rendered, cream);

            Assert.GreaterOrEqual(ratio, 3f,
                $"подсказка выходит {ratio:0.00}:1 к кремовому полю — с дистанции автомата это не читается "
                + "(дизайн-скептик 2026-08-05: нужно ≥3:1)");
            Assert.LessOrEqual(ratio, 7f,
                "…но и спорить с текстом задачи она не должна — это служебная строка, а не вторая задача");
        }

        // Альфа-смешение цвета подсказки с кремом под ним — в линейном пространстве, как в URP.
        private static Color OverCream(Color ink, Color cream)
        {
            Color i = ink.linear, c = cream.linear;
            float a = ink.a;
            return new Color(i.r * a + c.r * (1f - a),
                             i.g * a + c.g * (1f - a),
                             i.b * a + c.b * (1f - a), 1f).gamma;
        }

        private static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static float Luminance(Color c)
        {
            Color l = c.linear;
            return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        }

        // ---------------------------------------------------------------- механика в текстах

        [UnityTest]
        public IEnumerator BreathHint_TellsYouToHoldTheKey_NotToPumpIt()
        {
            // ⚠ ГАРД НА ЖЕСТ. r1 просил «нажимай и отпускай, раз в ~2 секунды»; r2 (основательница дословно:
            // «просто зажать датчик высоты, пока батарейка не заполнится») перевернул это в «зажми».
            // Строка обязана называть ровно ТЕКУЩИЙ жест, иначе игрок снова делает не то, что игра ждёт.
            yield return null;
            _fake.Yes();
            yield return null;
            _driver.DebugShowNewScale(NewScale.Energy);
            yield return null;

            StringAssert.Contains("зажми", _driver.NewScaleHintLine.text,
                "подсказка датчика обязана просить ЗАЖАТЬ клавишу");
            StringAssert.DoesNotContain("отпускай", _driver.NewScaleHintLine.text,
                "…и не звать отпускать её — ритм-механика снята 2026-08-07");
            StringAssert.DoesNotContain("секунд", _driver.NewScaleHintLine.text,
                "…и не называть темп: темпа больше нет");
        }

        [UnityTest]
        public IEnumerator EnergyTaskText_NamesTheHoldAndTheGoal()
        {
            yield return null;
            StringAssert.Contains("датчик высоты", GameDriver.EnergyTaskText,
                "задача энергии называет физический контрол");
            StringAssert.Contains("держи", GameDriver.EnergyTaskText,
                "…и жест: держать (а не «дышать размеренно»)");
            StringAssert.Contains("батарейка", GameDriver.EnergyTaskText,
                "…и условие выхода словами основательницы: пока батарейка не заполнится");
            Assert.IsFalse(GameDriver.EnergyTaskText.Contains("секунд"),
                "…и НИ СЛОВА про темп: «не понимаю про две секунды» — плейтест 2026-08-07");
            yield break;
        }

        [UnityTest]
        public IEnumerator TheOffRhythmFeedback_IsGone_NoApiLeftToShowIt()
        {
            // Гард отсутствия: отклик «не в ритм» и вздрагивание батареи сняты ЦЕЛИКОМ — вместе с их
            // публичной поверхностью, чтобы «временно вернуть» их было нечем.
            yield return null;
            var t = typeof(GameDriver);
            Assert.IsNull(t.GetProperty("BreathRejectShowing"), "отклика «не в ритм» больше нет");
            Assert.IsNull(t.GetProperty("EnergyDimAmount"), "…и вздрагивания батареи тоже");
            Assert.IsNull(t.GetField("BreathOffRhythmText"), "…и его текста");
            Assert.IsNull(t.GetField("BreathKeyHintTail"), "…и ритмического хвоста подсказки");
            Assert.IsNull(t.GetProperty("NewScaleHoldTrack"), "…и полосы прогресса удержания");
            Assert.IsNull(t.GetProperty("NewScaleHoldFill"), "…и её заполнения");
        }
    }
}
