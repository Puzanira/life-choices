using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    ///    copy of GameDriver + HostContent (banners, nags, mutterings AND the fallback bubble pool) +
    ///    Necrolog, every Text built into the HUD, and the whole authored deck in scenes.csv.
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
        // driver tests): decline late cards, breathe when energy is open, dismiss hints on the confirm.
        private static void DriveToFinale(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 8000)
            {
                // A TIMELINE milestone plays a blocking banner beat that swallows input — pump its clock
                // synchronously, exactly as the other driver tests do, so the run never stalls on it.
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                if (driver.TutorialShowing) { fake.Confirm(); continue; }
                driver.Game.Tick(0.5f);
                if (driver.Game.EnergyOpen)
                    for (int i = 0; i < 3 && driver.Game.State == GameState.Playing; i++)
                        fake.Fire(GameInput.EnergyPulse);
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
            foreach (var t in texts)
                AssertNoDevKey(t.text, "on-screen label «" + t.name + "»");

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
            var child = (string)typeof(GameDriver)
                .GetField("ChildTutorialText", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            StringAssert.Contains("«!»", child, "the child hint names the physical «!» button");

            Object.Destroy(go);
            yield return null;
        }

        // A live tutorial really is dismissed by GREEN (the button label promises exactly that).
        [UnityTest]
        public IEnumerator Hint_Dismisses_On_Green()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;

            fake.Yes();                                        // opener → playing
            int guard = 0;
            while (!driver.TutorialShowing && driver.Game.State == GameState.Playing && guard++ < 8000)
            {
                if (driver.HostBannerVisible) { driver.DebugPumpHost(GameDriver.BannerSeconds + 0.1f); continue; }
                driver.Game.Tick(0.25f);
                if (!driver.TutorialShowing && driver.Game.CurrentCard != null
                    && driver.Game.CardTimer < 3.5f) fake.No();
            }
            Assert.IsTrue(driver.TutorialShowing, "a hint modal came up");

            fake.Yes();                                        // GREEN = «ПОНЯТНО»
            Assert.IsFalse(driver.TutorialShowing, "GREEN dismissed the hint (the arcade confirm)");

            Object.Destroy(go);
            yield return null;
        }
    }
}
