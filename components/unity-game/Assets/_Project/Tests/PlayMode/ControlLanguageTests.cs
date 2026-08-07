using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AiGameStudio.ArcadeControls;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Layer-2 guard for the founder's control language (decision 2026-07-29, commit 99fab3c):
    ///  • GREEN (<see cref="GameInput.AnswerYes"/>) is the ONE confirm on every non-gameplay screen —
    ///    it starts a life on the opener, dismisses a hint AND restarts from the finale;
    ///  • RED (<see cref="GameInput.AnswerNo"/>) is answer-only: on the finale it is deliberately INERT
    ///    (this overrides the earlier «рестарт = красная» row of the input map, build-spec §3);
    ///  • the dev CONFIRM key (Enter) survives as a HIDDEN emulation — it still restarts;
    ///  • no string the player can read names a dev key («Enter», «ПРОБЕЛ», a bare «E», «стрелки», ↑/↓ …)
    ///    — every on-screen hint names a PHYSICAL cabinet control instead. The sweep covers the static
    ///    copy of GameDriver + HostContent (announces, nags, mutterings AND the fallback bubble pool) +
    ///    Necrolog, every Text built into the HUD, and the whole authored deck in scenes.csv.
    ///
    /// ⚠ ТОЧЕЧНОЕ ОСЛАБЛЕНИЕ (плейтест основательницы 2026-08-05, пункт 2). Ровно ДВЕ строки —
    /// служебные подсказки клавиш (<c>TaskKeyHint</c> на §D-экране и <c>TutKeyHint</c> на S5-подсказке) —
    /// выведены из свипа рендеренных Text. Причина: их содержимое ЗАКОННО называет клавишу эмуляции
    /// («эмуляция: Q», «эмуляция: ↑ / ↓»), потому что основательница играет без плат и без имени клавиши
    /// играть не может. Ослабление узкое и обвешано условиями, которые проверяются ЗДЕСЬ же
    /// (<see cref="KeyHintLines_AreTheOnlyDevKeyStrings_AndOnlyUnderEmulation"/>):
    ///   • это ДИНАМИЧЕСКИЕ строки — они собираются из конфига пакета в рантайме, ни одной статической
    ///     константы с именем клавиши в игре нет (свип (1) по константам НЕ ослаблен и ловит такое);
    ///   • при живых платах строка ПУСТА, т.е. на стойке игрок дев-клавиш по-прежнему не видит;
    ///   • никакой ДРУГОЙ Text освобождения не получает — исключение по точному имени объекта.
    /// </summary>
    public class ControlLanguageTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        // Live a whole run out to the payoff screen on the semantic input funnel (same shape as the other
        // driver tests): decline late cards, hold the height sensor when energy sags, dismiss hints on the
        // confirm.
        private static void DriveToFinale(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 30000)
            {
                driver.DebugAdvanceNewScale(0.5f);   // поднять ОТЛОЖЕННЫЕ окна (в живой игре это Update)
                // §D: открытия четырёх шкал поднимают МОДАЛКУ, которая кнопкой не снимается — её
                // проходят реальным контролом шкалы (NewScaleTut), остальные подсказки — как раньше.
                if (driver.SpecialModeShowing) { NewScaleTut.ClearSpecial(driver, fake); continue; }
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); continue; }
                if (driver.TutorialShowing) { fake.Confirm(); driver.DebugClearFrameGuards(); continue; }
                // ДЕПРЕССИЯ — тупик для «просто откажись»: там рычаг НЕТ инертен, а выход только через
                // ловлю пульса зелёной. Ловим по вспышке (мэшинг по контракту не выигрывает), иначе жизнь
                // не кончается никогда и тест ждёт финала до конца бюджета.
                if (driver.Game.InDepression)
                {
                    if (driver.Game.DepressionPulsing) fake.Fire(GameInput.ChildPress);
                    else driver.Game.Tick(0.2f);
                    driver.DebugClearFrameGuards();
                    continue;
                }
                if (driver.Game.EnergyOpen && driver.Game.Scales.Energy < 60)
                    fake.Fire(GameInput.EnergyHold);     // датчик зажат, пока батарея ниже половины
                driver.Game.Tick(0.5f);
                if (driver.Game.CurrentCard != null && driver.Game.CardTimer < 3f) fake.No();
            }
        }

        [UnityTest]
        public IEnumerator Finale_Green_Restarts_And_Red_Is_Inert()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                       // Start built the HUD; boots into the opener

            Assert.AreEqual(GameState.Opener, driver.Game.State, "boots into the opener");

            // RED on the opener is inert too — only GREEN starts a life (there is no confirm control).
            fake.No();
            Assert.AreEqual(GameState.Opener, driver.Game.State, "RED on the opener changes nothing");
            fake.Yes();
            Assert.AreEqual(GameState.Playing, driver.Game.State, "GREEN started the life");

            DriveToFinale(driver, fake);
            Assert.AreEqual(GameState.Finale, driver.Game.State, "the run reached the payoff screen");
            // The drive loop dismissed hints synchronously, so the driver's same-frame dismiss-swallow guard
            // is still armed (it clears in Update). Let one real frame pass — as on the cabinet, where the
            // player never presses twice inside a single frame.
            yield return null;

            // RED must NOT restart (canon flip): a masher on the red lever can never skip the necrolog.
            var story = driver.FinaleStoryText.text;
            fake.No();
            Assert.AreEqual(GameState.Finale, driver.Game.State,
                "RED on the finale is INERT — the state does not change (founder 99fab3c)");
            Assert.AreEqual(story, driver.FinaleStoryText.text, "…and the necrolog on screen is untouched");
            fake.No(); fake.No();
            Assert.AreEqual(GameState.Finale, driver.Game.State, "…still inert when mashed");

            // GREEN is the restart.
            fake.Yes();
            Assert.AreEqual(GameState.Opener, driver.Game.State,
                "GREEN restarted from the finale to the opener");
            Assert.AreEqual(100, driver.Game.Scales.Health, "state fully reset for the next player");

            fake.Yes();
            Assert.AreEqual(GameState.Playing, driver.Game.State, "and GREEN starts the next life");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Finale_DevConfirmKey_Still_Restarts_Hidden()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            fake.Confirm();                          // dev Enter — the hidden emulation of GREEN
            Assert.AreEqual(GameState.Playing, driver.Game.State, "dev CONFIRM still starts a life");

            DriveToFinale(driver, fake);
            Assert.AreEqual(GameState.Finale, driver.Game.State, "the run reached the payoff screen");
            yield return null;                       // clear the same-frame dismiss-swallow guard

            fake.Confirm();
            Assert.AreEqual(GameState.Opener, driver.Game.State,
                "dev CONFIRM (Enter) still restarts from the finale — hidden, but unbroken");

            Object.Destroy(go);
            yield return null;
        }

        // Every dev-keyboard name that must never reach the player's eyes. «Enter» is the founder's
        // headline case; the rest are the other emulation keys the old hint copy used to spell out —
        // INCLUDING the shapes a re-written hint would most plausibly slip back in: the height-sensor key
        // «E» as a bare key name («жми E», «(E)»), and the balancer's keyboard arrows, named either as
        // glyphs (↑/↓) or in words («стрелки вверх/вниз»). The cabinet words are «ДАТЧИК ВЫСОТЫ» and
        // «ДЖОЙСТИК ВВЕРХ/ВНИЗ» (host-content §4), so none of these may appear in player-facing copy.
        private static readonly (string Name, Regex Pattern)[] DevKeyPatterns =
        {
            ("Enter",  new Regex("enter",  RegexOptions.IgnoreCase)),
            ("ПРОБЕЛ", new Regex("пробел", RegexOptions.IgnoreCase)),
            ("Space",  new Regex("space",  RegexOptions.IgnoreCase)),
            ("Numpad", new Regex("numpad", RegexOptions.IgnoreCase)),
            ("↑",      new Regex("↑")),
            ("↓",      new Regex("↓")),
            ("стрелк", new Regex("стрелк", RegexOptions.IgnoreCase)),
            // Latin «E» standing ALONE as a key name — «жми E», « E », «(E)», «E.» — but never inside a
            // word, so Latin-spelled copy and ids (Energy, BLOCK$, CH0E…) do not false-positive. Cyrillic
            // «Е»/«е» is a different codepoint and is deliberately NOT matched.
            ("клавиша E", new Regex(@"(?<![0-9A-Za-z\p{IsCyrillic}_])[Ee](?![0-9A-Za-z\p{IsCyrillic}_])")),
        };

        // Имена объектов ДВУХ служебных строк подсказки клавиши — единственное исключение свипа (2).
        private static readonly string[] KeyHintObjectNames = { "TaskKeyHint", "TutKeyHint" };

        private static void AssertNoDevKey(string text, string where)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var (name, pattern) in DevKeyPatterns)
                Assert.IsFalse(pattern.IsMatch(text),
                    where + " must name a PHYSICAL control, not the dev key «" + name + "» (founder 99fab3c). "
                        + "Got: «" + text + "»");
        }

        // Flatten a reflected static member into the player-facing strings it holds: a bare string, a
        // string[]/IEnumerable&lt;string&gt;, or a dictionary whose VALUES are strings or string arrays
        // (HostContent keeps its banners and its per-tone bubble pool in exactly those shapes).
        private static IEnumerable<string> Flatten(object value)
        {
            switch (value)
            {
                case null: yield break;
                case string s: yield return s; break;
                case IDictionary dict:
                    foreach (var v in dict.Values)
                        foreach (var s in Flatten(v)) yield return s;
                    break;
                case IEnumerable seq:
                    foreach (var v in seq)
                        foreach (var s in Flatten(v)) yield return s;
                    break;
            }
        }

        // Every static string-bearing member of a content type, whatever its shape.
        private static IEnumerable<(string Where, string Text)> StaticStrings(System.Type type)
        {
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                object value;
                try { value = f.GetValue(null); } catch { continue; }
                foreach (var s in Flatten(value))
                    yield return (type.Name + "." + f.Name, s);
            }
        }

        [UnityTest]
        public IEnumerator No_PlayerFacing_String_Names_A_DevKey()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // (1) Every hint/label CONSTANT the three content types can put on screen, whatever the shape
            // (string, string[], dictionary of either). Reflection, so a NEW hint added later is covered
            // automatically — this cannot rot into a whitelist.
            //   • GameDriver  — the HUD's own hint/label copy;
            //   • HostContent — the Ведущий's banners, blitz nags, depression mutterings AND the per-tone
            //     bubble POOL. The pool needed covering here explicitly: sweep (2) below only sees the ONE
            //     line that happens to be in the bubble at this instant, so all the other pool entries are
            //     invisible to a rendered-Text sweep;
            //   • Necrolog    — the finale's fixed intro/parents lines.
            var constants = StaticStrings(typeof(GameDriver))
                .Concat(StaticStrings(typeof(HostContent)))
                .Concat(StaticStrings(typeof(Necrolog)))
                .ToList();
            Assert.Greater(constants.Count, 40, "the content types really do own their copy as static fields");
            Assert.IsTrue(constants.Any(c => c.Where.StartsWith("HostContent.Pool")),
                "the Ведущий's fallback bubble POOL is really part of the sweep");
            foreach (var (where, text) in constants)
                AssertNoDevKey(text, "constant «" + where + "»");

            // (2) …every Text actually built into the HUD, inactive panels included (opener CTA, finale
            // restart CTA, the tutorial's dismiss button) — the rendered truth, not just the constants.
            var texts = driver.GetComponentsInChildren<Text>(includeInactive: true);
            Assert.Greater(texts.Length, 3, "the HUD really built its labels");
            Assert.AreEqual(KeyHintObjectNames.Length,
                texts.Count(t => KeyHintObjectNames.Contains(t.name)),
                "обе служебные строки подсказки клавиш на месте — исключение свипа не протухло");
            foreach (var t in texts)
            {
                if (KeyHintObjectNames.Contains(t.name)) continue;   // ⚠ точечное ослабление, см. шапку
                AssertNoDevKey(t.text, "on-screen label «" + t.name + "»");
            }

            // (2b) …and the whole authored DECK, not just the cards this run happens to draw: every card
            // question, host line and necrolog line in scenes.csv is player-facing copy too.
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "the deck really is loadable from Resources");
            var deck = CardLoader.ParseAll(csv.text);
            Assert.Greater(deck.Count, 50, "the whole authored deck, not a subset");
            foreach (var c in deck)
            {
                AssertNoDevKey(c.Question, "card «" + c.Id + "» question");
                AssertNoDevKey(c.HostYes, "card «" + c.Id + "» host ДА");
                AssertNoDevKey(c.HostNo, "card «" + c.Id + "» host НЕТ");
                AssertNoDevKey(c.YesNecrolog, "card «" + c.Id + "» necrolog ДА");
                AssertNoDevKey(c.NoNecrolog, "card «" + c.Id + "» necrolog НЕТ");
            }

            // (3) The two confirm CTAs must POSITIVELY name the green button, not merely avoid «Enter».
            var startText = driver.OpenerPanel.transform.Find("StartPlate/StartText").GetComponent<Text>();
            StringAssert.Contains("ЗЕЛЁНУЮ", startText.text, "the opener CTA names the GREEN button");
            var againText = driver.FinalePanel.transform.Find("AgainPlate/AgainText").GetComponent<Text>();
            StringAssert.Contains("ЗЕЛЁНУЮ", againText.text, "the finale CTA names the GREEN button");
            StringAssert.Contains("ЗЕЛЁНУЮ", driver.TutorialButtonText.text, "the hint dismiss names the GREEN button");

            // (4) …and the child tutorial names the «!» button (the cabinet control), per the same decision.
            // Since the §D screen replaced the old text hint, the canon copy now lives in the TASK window.
            StringAssert.Contains("«!»", GameDriver.ChildTaskText,
                "the child task window names the physical «!» button");

            Object.Destroy(go);
            yield return null;
        }

        /// <summary>
        /// Условия точечного ослабления. (а) Ни одна СТАТИЧЕСКАЯ строка игры не называет клавишу — имена
        /// клавиш существуют только как рантайм-значения из конфига пакета. (б) При живых платах строка
        /// подсказки ПУСТА: на стойке дев-клавиш не видно. (в) При эмуляции строка есть и говорит правду.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyHintLines_AreTheOnlyDevKeyStrings_AndOnlyUnderEmulation()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            // (а) статический свип по константам — БЕЗ послаблений (это и есть «гард на статические тексты»).
            foreach (var (where, text) in StaticStrings(typeof(GameDriver)))
                AssertNoDevKey(text, "constant «" + where + "»");

            // (б) стойка: платa ведёт датчики → подсказка пуста.
            var serial = new HintFakeSerial { ProvidesHeights = true, ProvidesJoystick = true, ProvidesButtons = true };
            ArcadeInput.Initialize(new CompositeBackend(new KeyboardBackend(KeyboardMapping.LoadDefault()), serial));
            driver.DebugShowNewScale(NewScale.Energy);
            yield return null;
            Assert.AreEqual("", driver.NewScaleHintLine.text,
                "с живой платой строка клавиши пуста — на стойке дев-клавиш не видно");

            // (в) без плат: подсказка появляется и называет клавишу ИЗ КОНФИГА ПАКЕТА.
            serial.ProvidesHeights = false;
            yield return null;
            string expected = GameDriver.BreathKeyHintPrefix
                + KeyboardHints.PrimaryFor(KeyboardMapping.LoadDefault(), ArcadeControlId.HeightA);
            Assert.AreEqual(expected, driver.NewScaleHintLine.text,
                "без плат строка есть и собрана из маппинга пакета, а не из зашитой в игре буквы");

            ArcadeInput.Initialize(null);
            Object.Destroy(go);
            yield return null;
        }

        private sealed class HintFakeSerial : ISerialBackend, IButtonPresence
        {
            public bool ProvidesHeights { get; set; }
            public bool ProvidesJoystick { get; set; }
            public bool ProvidesButtons { get; set; }
            public BackendSnapshot Poll(float deltaTime) => default;
        }

        /// <summary>
        /// ЗЕЛЁНАЯ — единственный подтверждающий контрол кабинета, и живой блокирующий экран обязан
        /// сниматься ИМЕННО ей (подпись на его CTA это и обещает).
        ///
        /// ⚠ r3 2026-08-07: блокирующий экран, до которого доезжает обычная жизнь, — это уже не жёлтая
        /// S5-подсказка (её последние два текста, здоровье и выгорание, переехали), а ВХОДНОЙ ЭКРАН
        /// СПЕЦРЕЖИМА. Проверяем на нём — и на самой S5-механике отдельно, чтобы примитив не сгнил.
        /// </summary>
        [UnityTest]
        public IEnumerator BlockingScreen_Dismisses_On_Green()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            fake.Yes();                                        // opener → playing
            int guard = 0;
            while (!driver.SpecialModeShowing && driver.Game.State == GameState.Playing && guard++ < 8000)
            {
                // Первые открытия (18/20/25) ведут §D-модалку — она НЕ снимается зелёной и проходится
                // своим контролом; блокирующий экран, который снимает ЗЕЛЁНАЯ, — это здоровье (30).
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); continue; }
                driver.Game.Tick(0.25f);
                if (!driver.SpecialModeShowing && !driver.NewScaleShowing && driver.Game.CurrentCard != null
                    && driver.Game.CardTimer < 3.5f) fake.No();
            }
            Assert.IsTrue(driver.SpecialModeShowing, "блокирующий экран поднялся");
            Assert.IsTrue(driver.Game.Paused, "…и он держит паузу");

            fake.Yes();                                        // GREEN
            Assert.IsFalse(driver.SpecialModeShowing, "ЗЕЛЁНАЯ сняла экран (аркадный confirm)");
            Assert.IsFalse(driver.Game.Paused, "…и пауза снята");

            // …и тот же контракт у S5-примитива: он остался общей «блокирующей подсказкой».
            driver.DebugClearFrameGuards();
            driver.DebugShowTutorial("ТЕСТ");
            Assert.IsTrue(driver.TutorialShowing, "S5-подсказка поднята");
            fake.Yes();
            Assert.IsFalse(driver.TutorialShowing, "…и тоже снимается ЗЕЛЁНОЙ");

            Object.Destroy(go);
            yield return null;
        }
    }
}
