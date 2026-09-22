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

        // ============================================================ r7 — ОБРАТНАЯ СТОРОНА ДРЕЙФ-ГАРДА
        //
        // ⚠ НАХОДКА КОД-СКЕПТИКА 2026-09-22. Оба дрейф-гарда выше проверяют вхождение В ОДНУ СТОРОНУ:
        // «текст из кода лежит в документе». Пока документ хранил СНЯТУЮ формулировку ДОСЛОВНО (под
        // зачёркиванием, как историю), возврат кода к ней оставался ЗЕЛЁНЫМ — строка-то в документе есть.
        // То есть гард, ради которого всё затевалось («основательница переписала текст — код обязан
        // поехать за ней»), не ловил ровно ту поломку, от которой защищает.
        //
        // Лечится ДВУМЯ движениями, и оба нужны:
        //   1. в документе история пересказана, а не процитирована (дословных копий снятых строк там
        //      больше нет) — это чинит одностороннее вхождение;
        //   2. здесь — ЯВНЫЙ ЗАПРЕТ: список снятых формулировок живёт В ТЕСТЕ, и ни одна не смеет
        //      вернуться ни в код, ни дословно в документ.
        // Список намеренно НЕ в документе: он и есть то, чего в документе быть не должно.

        private static readonly (string Where, string Text)[] RetiredTexts =
        {
            ("здоровье, задача до 2026-09-22 (заменена основательницей)",
                "Контрола у здоровья нет — лечись выборами за деньги, если накопил"),
            ("отношения, задача до 2026-09-22 (укорочена дизайн-гейтом r5)",
                "Двигай джойстиком — сохраняй маркер отношений в зелёной зоне"),
            ("депрессия, задача до 2026-09-22 (укорочена дизайн-гейтом r5)",
                "Лови пульс: жми жёлтую кнопку в момент вспышки — пять попаданий вернут краски"),
        };

        /// <summary>
        /// Снятая формулировка не возвращается НИ В КОД, ни дословно в канон-документ.
        /// Mutation-proof: вернуть старую константу в <c>GameDriver</c> — красный (а раньше был зелёный).
        /// </summary>
        [Test]
        public void RetiredTaskTexts_NeverComeBackIntoTheCode()
        {
            var live = new System.Collections.Generic.List<(string Where, string Text)>();
            foreach (var m in Modes)
            {
                live.Add(("SpecialStory(" + m + ")", GameDriver.SpecialStory(m)));
                live.Add(("SpecialTask(" + m + ")", GameDriver.SpecialTask(m)));
            }
            foreach (var s in new[] { NewScale.Money, NewScale.Relations, NewScale.Energy, NewScale.Child })
            {
                live.Add(("NewScaleStory(" + s + ")", GameDriver.NewScaleStory(s)));
                live.Add(("NewScaleTask(" + s + ")", GameDriver.NewScaleTask(s)));
            }
            Assert.Greater(live.Count, 8, "живые тексты действительно собраны, а не пустой список");

            Assert.IsTrue(File.Exists(CanonPath), $"канон host-content.md на месте: {CanonPath}");
            string wholeDoc = Squash(File.ReadAllText(CanonPath).Replace("\r\n", "\n"));

            foreach (var (where, text) in RetiredTexts)
            {
                string retired = Squash(text);
                foreach (var (liveWhere, liveText) in live)
                    StringAssert.DoesNotContain(retired, Squash(liveText),
                        liveWhere + ": в игру вернулась СНЯТАЯ формулировка (" + where + ")");

                StringAssert.DoesNotContain(retired, wholeDoc,
                    where + ": снятая формулировка снова лежит в документе ДОСЛОВНО — от этого гард "
                          + "вхождения становится вакуумным. История пересказывается, а не цитируется.");
            }
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
        /// п.3г — ВОПРОС ЗАКРЫТ ОСНОВАТЕЛЬНИЦЕЙ 2026-08-08: ловля идёт по BangButton кабинета. Гард
        /// держит две вещи сразу: (а) все тексты по-прежнему собираются из ОДНОЙ константы (заготовка и
        /// сделала смену правкой одной строки), (б) ни в одном из них не осталось ЗЕЛЁНОЙ — иначе экран
        /// звал бы игрока жать не тот контрол, а механика молчала бы.
        ///
        /// ⚠ r5 п.2 — ИМЯ КОНТРОЛА СМЕНИЛОСЬ: «!» → «жёлтая кнопка» (панч-лист автомата 2026-09-22, на
        /// стойке органы подписаны физически и значка «!» там нет). КОНТРОЛ ТОТ ЖЕ, поменялась подпись.
        /// Гард тоже развёрнут: теперь он требует имя из словаря стойки и ЗАПРЕЩАЕТ откат к голому «!».
        /// </summary>
        [Test]
        public void DepressionCatchControl_IsNamedFromASingleConstant_AndItIsTheBangButton()
        {
            Assert.IsNotEmpty(GameDriver.DepressionCatchControlName, "константа контрола задана");
            StringAssert.Contains("жёлтую кнопку", GameDriver.DepressionCatchControlName,
                "r5 п.2: контрол ловли зовётся словарём стойки — «жёлтая кнопка»");
            StringAssert.DoesNotContain("!", GameDriver.DepressionCatchControlName,
                "…и откат к значку «!» как имени органа запрещён: на стойке такой подписи нет");
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
