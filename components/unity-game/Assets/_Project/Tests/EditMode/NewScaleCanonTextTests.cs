using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Drift guard for the §D screen's copy: the eight canon strings shipped in <see cref="GameDriver"/>
    /// (рассказ + задача × 4 шкалы) must appear WORD FOR WORD in `docs/new_concept/host-content.md` §4 —
    /// the founder picked those exact variants on 2026-07-29. If the doc is re-edited and the code isn't
    /// (or vice-versa) this goes red instead of the two silently diverging (same contract as
    /// <c>SnapshotSyncTests</c> for the deck).
    ///
    /// The doc wraps its lines, so both sides are whitespace-normalised before comparison; nothing else
    /// is touched (no case folding, no punctuation stripping) — «дословно» stays literal.
    /// </summary>
    public class NewScaleCanonTextTests
    {
        private static string CanonPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..",
                "docs", "new_concept", "host-content.md"));

        private static string Squash(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

        private static string CanonSection4()
        {
            Assert.IsTrue(File.Exists(CanonPath), $"канон host-content.md на месте: {CanonPath}");
            string all = File.ReadAllText(CanonPath).Replace("\r\n", "\n");
            int start = all.IndexOf("## 4.", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "в host-content.md есть §4 (тексты туториалов открытия шкал)");
            return Squash(all.Substring(start));
        }

        [Test]
        public void EveryStoryAndTaskString_IsVerbatimInHostContentSection4()
        {
            string canon = CanonSection4();
            foreach (var s in new[] { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child })
            {
                StringAssert.Contains(Squash(GameDriver.NewScaleStory(s)), canon,
                    s + ": рассказ Ведущего дословно из host-content §4");
                StringAssert.Contains(Squash(GameDriver.NewScaleTask(s)), canon,
                    s + ": задача дословно из host-content §4");
            }
        }

        [Test]
        public void EnergyTask_IsSingular_TheFounderClosedQControls()
        {
            // Основательница 2026-07-29: энергия = ОДИН рычаг-дыхание. Эталонный PNG с «датчики… выровнять»
            // остался только референсом раскладки, текст канона — единственное число.
            StringAssert.Contains("датчик высоты", GameDriver.EnergyTaskText, "«датчик высоты» — ед. ч.");
            Assert.IsFalse(GameDriver.EnergyTaskText.Contains("датчики"),
                "во множественном числе («датчики») задача энергии больше не звучит");
        }

        [Test]
        public void TheFourOpens_HaveBothWindowsFilled()
        {
            foreach (var s in new[] { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child })
            {
                Assert.IsNotEmpty(GameDriver.NewScaleStory(s), s + ": окно-рассказ не пустое");
                Assert.IsNotEmpty(GameDriver.NewScaleTask(s), s + ": окно-задача не пустое");
            }
            Assert.IsEmpty(GameDriver.NewScaleStory(NewScale.None), "None — не экран");
            Assert.IsEmpty(GameDriver.NewScaleTask(NewScale.None), "None — не экран");
        }

        // ============================================================ r3 — §4b, входные экраны спецрежимов

        private static readonly SpecialMode[] Modes =
            { SpecialMode.Health, SpecialMode.Blitz, SpecialMode.Depression, SpecialMode.Burnout };

        private static string CanonSection4b()
        {
            Assert.IsTrue(File.Exists(CanonPath), $"канон host-content.md на месте: {CanonPath}");
            string all = File.ReadAllText(CanonPath).Replace("\r\n", "\n");
            int start = all.IndexOf("## 4b.", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "в host-content.md есть §4b (входные экраны спецрежимов)");
            return Squash(all.Substring(start));
        }

        /// <summary>
        /// Тот же дрейф-гард, что у §4, но для восьми строк входных экранов спецрежимов (r3). Они —
        /// ЧЕРНОВИКИ: основательница правит их свободно ПРЯМО В ДОКУМЕНТЕ, и этот тест краснеет ровно
        /// тогда, когда правка не доехала до кода (или наоборот).
        /// </summary>
        [Test]
        public void EverySpecialModeString_IsVerbatimInHostContentSection4b()
        {
            string canon = CanonSection4b();
            foreach (var m in Modes)
            {
                StringAssert.Contains(Squash(GameDriver.SpecialStory(m)), canon,
                    m + ": рассказ Ведущего дословно из host-content §4b");
                StringAssert.Contains(Squash(GameDriver.SpecialTask(m)), canon,
                    m + ": задача дословно из host-content §4b");
            }
            StringAssert.Contains(Squash(GameDriver.ChildMissedLine), canon,
                "реплика на пропущенный звонок — тоже канон-черновик §4b");
            StringAssert.Contains(Squash(GameDriver.BurnoutPlateTitle), canon,
                "заголовок короткой плашки повторного выгорания — оттуда же");
            StringAssert.Contains(Squash(GameDriver.BurnoutPlateSubtitle), canon,
                "…и её вторая строка");
        }

        /// <summary>
        /// §4b помечен как ЧЕРНОВИК основательницы — иначе правки уедут «в код навсегда», и она перестанет
        /// понимать, что здесь можно трогать. Гард на саму пометку.
        /// </summary>
        [Test]
        public void SpecialModeTexts_AreMarkedAsFounderEditableDrafts()
        {
            string canon = CanonSection4b();
            StringAssert.Contains("ЧЕРНОВИК", canon.ToUpperInvariant(),
                "§4b помечен как черновик, который основательница правит свободно");
            StringAssert.Contains("ОСНОВАТЕЛЬНИЦА ПРАВИТ СВОБОДНО", canon.ToUpperInvariant(),
                "…прямым текстом");
        }

        [Test]
        public void TheFourSpecialModes_HaveBothWindowsFilled()
        {
            foreach (var m in Modes)
            {
                Assert.IsNotEmpty(GameDriver.SpecialStory(m), m + ": окно-рассказ не пустое");
                Assert.IsNotEmpty(GameDriver.SpecialTask(m), m + ": окно-задача не пустое");
            }
            Assert.IsEmpty(GameDriver.SpecialStory(SpecialMode.None), "None — не экран");
            Assert.IsEmpty(GameDriver.SpecialTask(SpecialMode.None), "None — не экран");
        }

        /// <summary>
        /// п.3г — ВОПРОС ЗАКРЫТ ОСНОВАТЕЛЬНИЦЕЙ 2026-08-08: ловля идёт по КНОПКЕ «!». Гард держит две вещи
        /// сразу: (а) все тексты по-прежнему собираются из ОДНОЙ константы (заготовка и сделала смену
        /// правкой одной строки), (б) ни в одном из них не осталось ЗЕЛЁНОЙ — иначе экран звал бы игрока
        /// жать не тот контрол, а механика молчала бы.
        /// </summary>
        [Test]
        public void DepressionCatchControl_IsNamedFromASingleConstant_AndItIsTheBangButton()
        {
            Assert.IsNotEmpty(GameDriver.DepressionCatchControlName, "константа контрола задана");
            StringAssert.Contains("!", GameDriver.DepressionCatchControlName,
                "решение основательницы 2026-08-08: контрол ловли — кнопка «!»");
            StringAssert.Contains(GameDriver.DepressionCatchControlName, GameDriver.DepressionTaskText,
                "задача входного экрана называет контрол ИЗ константы");
            StringAssert.Contains(GameDriver.DepressionCatchControlName.ToLowerInvariant(),
                GameDriver.DepressionBoardHint,
                "…и подсказка на самой доске депрессии — оттуда же");
            foreach (var text in new[] { GameDriver.DepressionTaskText, GameDriver.DepressionBoardHint })
                Assert.IsFalse(text.ToUpperInvariant().Contains("ЗЕЛЁН"),
                    "в текстах депрессии не осталось ЗЕЛЁНОЙ: «" + text + "»");
        }
    }
}
