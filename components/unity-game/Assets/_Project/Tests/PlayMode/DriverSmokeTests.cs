using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    public class DriverSmokeTests
    {
        [UnityTest]
        public IEnumerator Driver_Boots_InOpener_ThenInjectedInput_PlaysToEnding()
        {
            var go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Awake builds HUD + loads deck
            var fake = new PlayFakeInputSource();
            driver.Input = fake;                           // injected before Start wires it
            yield return null;                             // Start runs

            Assert.IsNotNull(driver.Game, "game constructed");
            Assert.AreEqual(GameState.Opener, driver.Game.State, "boots into opener");
            Assert.Greater(driver.Game.DeckCount, 0, "spine subset loaded from Resources");

            fake.Confirm();                                // start the life
            Assert.AreEqual(GameState.Playing, driver.Game.State);
            Assert.IsNotNull(driver.Game.CurrentCard);

            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 300)
            {
                fake.No();                                 // answer СПАСИБО НЕ НАДО to every card
            }

            Assert.AreEqual(GameState.Finale, driver.Game.State, "run reached an ending");
            Assert.IsNotNull(driver.Game.Necrolog);

            // ⚠ НА ЖИВОМ ЗАБЕГЕ: НИ ОДНОЙ ЗАПЕЧЁННОЙ СТРОКИ (решение основательницы 2026-09-22 — «пишется
            // везде и ни о чём игровом не сообщает»). Здесь стояло `StoryLines[0] == Necrolog.ParentsLine`,
            // то есть гард ТРЕБОВАЛ ту самую строку. Теперь он требует обратного, и требует на РЕАЛЬНОМ
            // прогоне колоды, а не на синтетике: подводка вернулась бы именно сюда.
            var story = driver.Game.Necrolog.ComposeStory();
            StringAssert.DoesNotContain("переживайте", story, "зачина в некрологе нет");
            StringAssert.DoesNotContain("прекрасных родителей", story, "…и всегда-первой строки родителей");
            Assert.Greater(driver.Game.Necrolog.StoryLines.Count, 0,
                "забег по всей колоде прожит — вехи в некрологе есть");
            // …и каждая строка некролога пришла ИЗ КАРТОЧКИ, а не из кода: сверяем с колодой.
            var deck = new System.Collections.Generic.HashSet<string>();
            foreach (var c in CardLoader.ParseAll(Resources.Load<TextAsset>("scenes").text))
            { deck.Add(c.YesNecrolog); deck.Add(c.NoNecrolog); }
            foreach (var line in driver.Game.Necrolog.StoryLines)
                Assert.IsTrue(deck.Contains(line) || line == "Отношения не удержали — расстались.",
                    $"строка «{line}» пришла из колоды/механики, а не запечена в некрологе");

            fake.Confirm();                                // finale -> opener, fresh state
            Assert.AreEqual(GameState.Opener, driver.Game.State);
            Assert.AreEqual(100, driver.Game.Scales.Health, "state reset on restart");

            Object.Destroy(go);
            yield return null;
        }
    }
}
