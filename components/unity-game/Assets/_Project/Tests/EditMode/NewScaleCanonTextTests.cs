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
    }
}
