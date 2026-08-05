using System.Collections;
using AiGameStudio.ArcadeControls;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Плейтест основательницы 2026-08-05, пункты 2 и 5.
    ///
    ///  • §2 ПОДСКАЗКИ КЛАВИШ. Пока ввод идёт от клавиатурной эмуляции, каждый экран-объяснение показывает
    ///    ВТОРОЙ строкой клавишу своего контрола — прочитанную ИЗ КОНФИГА ПАКЕТА
    ///    (<see cref="KeyboardHints"/> поверх <see cref="KeyboardMapping"/>), а не из зашитой в игре буквы.
    ///    Стоит плате ответить — строка исчезает: на стойке дев-клавиш быть не должно.
    ///  • §5 ОТКЛИК НА НЕВАЛИДНЫЙ ВДОХ. Вдох, отбитый ритм-гейтом, обязан быть ВИДЕН: подпись «не в ритм»
    ///    на окне энергии + короткое затемнение батареи. Молчание в ответ на нажатие — ровно то, из-за
    ///    чего плейтест и остановился.
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
            return GameDriver.KeyHintPrefix + key
                 + (control == ArcadeControlId.HeightA ? GameDriver.BreathKeyHintTail : "");
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
        public IEnumerator BurnoutHint_NamesTheBreathKeyToo()
        {
            yield return null;
            _driver.DebugShowTutorial("ВЫГОРАНИЕ!", ArcadeControlId.HeightA);
            yield return null;

            Assert.IsTrue(_driver.TutorialShowing);
            Assert.AreEqual(ExpectedHint(ArcadeControlId.HeightA), _driver.TutorialHintLine.text,
                "S5-подсказка про дыхание тоже называет клавишу при эмуляции");

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

        // ---------------------------------------------------------------- §5

        [UnityTest]
        public IEnumerator RejectedBreath_IsVisible_OnTheEnergyScreen()
        {
            yield return null;
            _fake.Yes();                       // opener → playing (иначе дыхание инертно)
            yield return null;
            _driver.DebugShowNewScale(NewScale.Energy);
            yield return null;

            Assert.IsFalse(_driver.BreathRejectShowing, "до вдоха откликов нет");
            float restingAlpha = _driver.EnergyDimAmount;
            Assert.AreEqual(1f, restingAlpha, 1e-3f, "батарея в покое не затемнена");

            // ПЕРВЫЙ импульс только засевает каденцию — за него не ругают (иначе игрок получал бы
            // «не в ритм» за самый первый вдох в жизни).
            _fake.Fire(GameInput.EnergyPulse);
            Assert.IsFalse(_driver.BreathRejectShowing, "первый вдох жизни — не «не в ритм»");

            // ВТОРОЙ приходит тем же кадром, интервал 0 — это ровно «заколачивание», и гейт его отбивает.
            _fake.Fire(GameInput.EnergyPulse);
            Assert.IsTrue(_driver.BreathRejectShowing, "отбитый вдох обязан дать видимый отклик");
            yield return null;
            Assert.AreEqual(GameDriver.BreathOffRhythmText, _driver.NewScaleHintLine.text,
                "на окне энергии подпись сменяется откликом «не в ритм»");
            Assert.Less(_driver.EnergyDimAmount, 1f, "…и батарея коротко темнеет");

            // Отклик короткий: он мигает, а не залипает.
            yield return new WaitForSeconds(GameDriver.BreathRejectSeconds + 0.3f);
            Assert.IsFalse(_driver.BreathRejectShowing, "отклик гаснет сам");
            Assert.AreEqual(ExpectedHint(ArcadeControlId.HeightA), _driver.NewScaleHintLine.text,
                "…и строка возвращается к подсказке клавиши");
            Assert.AreEqual(1f, _driver.EnergyDimAmount, 1e-3f, "…а батарея — к полной яркости");
        }

        [UnityTest]
        public IEnumerator EnergyTaskText_NamesTheRhythm_InWords()
        {
            yield return null;
            StringAssert.Contains("2 секунд", GameDriver.EnergyTaskText,
                "задача энергии обязана называть ТЕМП словами (плейтест §5)");
            StringAssert.Contains("датчик высоты", GameDriver.EnergyTaskText,
                "…не потеряв при этом имя физического контрола");
            yield break;
        }
    }
}
