using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// The money open (18) through the REAL driver. Since the increment «экран появления новой шкалы» this
    /// open raises the §D MODAL, not the old S5 text hint, so the contract changed with it:
    /// full freeze (age + drains + card timer) while it is up, NO dismissal by any answer button
    /// (meeting-revisions §2: «окно НЕ уходит, пока игрок не приведёт шкалу в нужный режим»), the CRANK is
    /// what closes it — and the money HUD + crank stay live IMMEDIATELY afterwards on the same card.
    ///
    /// ⚠ 2026-08-07: закрывает НЕ первый тик, а <see cref="GameDriver.MoneyTutorialTicks"/> принятых тиков
    /// (основательница: «крутить ручку денег надо дольше на туториале — слишком быстро пропадает»).
    ///
    /// The founder's Gate-2 rule «крутилка не снимает подсказку» is unchanged for the hints that are still
    /// hints (health 30, burnout — DriverLiveHudTests): there the crank is inert. On the §D screen the crank
    /// is not a dismiss control, it is the TASK — «Верти ручку — и монетки посыплются в копилку».
    ///
    /// Time is driven by explicit Game.Tick / DebugAdvanceNewScale calls.
    /// </summary>
    public class MoneyTutorialTests
    {
        private static GameDriver Boot(out GameObject go, out PlayFakeInputSource fake)
        {
            go = new GameObject("Driver");
            var driver = go.AddComponent<GameDriver>();   // Awake builds HUD + loads sampled deck
            fake = new PlayFakeInputSource();
            driver.Input = fake;
            return driver;
        }

        // Advance the life until the money modal appears. No cranking on the way (a crank attempt before 18
        // is a no-op, but after the modal is up it would satisfy the task before the test can look at it).
        private static void PlayUntilMoneyModal(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (!driver.NewScaleShowing && driver.Game.State == GameState.Playing && guard++ < 4000)
            {
                driver.Game.Tick(0.25f);
                if (!driver.NewScaleShowing && driver.Game.CurrentCard != null
                    && driver.Game.CardTimer < 3.5f)
                    fake.No();                            // answer so ages keep advancing (cards live ~1.5s)
            }
            Assert.IsTrue(driver.NewScaleShowing, "the money modal appeared during the run");
            Assert.AreEqual(NewScale.Money, driver.NewScaleKind, "…and it is the MONEY screen (open at 18)");
        }

        [UnityTest]
        public IEnumerator Modal_FreezesAll_AndNoAnswerButtonCloses_TheCrankDoes_TimerResumes()
        {
            var driver = Boot(out var go, out var fake);
            yield return null;                             // Start wires input + MoneyOpened

            fake.Confirm();                                // opener → playing
            PlayUntilMoneyModal(driver, fake);

            Assert.IsTrue(driver.Game.MoneyOpen, "money opened at 18");
            Assert.IsTrue(driver.NewScaleOverlay.activeSelf, "overlay visible");
            Assert.IsFalse(driver.TutorialShowing, "the old S5 money hint is gone");
            Assert.IsTrue(driver.Game.Paused, "game paused under the overlay");

            // Full freeze: age, money AND the current card's timer hold through a big tick.
            var card = driver.Game.CurrentCard;
            Assert.IsNotNull(card, "a card is up when the modal fires (overlay between answers)");
            float age0 = driver.Game.Age;
            float timer0 = driver.Game.CardTimer;
            double money0 = driver.Game.Money;
            driver.Game.Tick(5f);
            Assert.AreEqual(age0, driver.Game.Age, "age frozen");
            Assert.AreEqual(timer0, driver.Game.CardTimer, "card timer frozen (no timeout under pause)");
            Assert.AreEqual(money0, driver.Game.Money, "money frozen (cost + drains)");

            // NO answer button closes it — GREEN, RED and the dev CONFIRM are all inert here (§2).
            fake.Yes();
            fake.No();
            fake.Confirm();
            driver.DebugAdvanceNewScale(1f);
            Assert.IsTrue(driver.NewScaleShowing, "answers/CONFIRM do NOT close the §D screen");
            Assert.IsTrue(driver.Game.Paused, "still paused");
            Assert.AreSame(card, driver.Game.CurrentCard, "…and no answer was banked either");

            // The CRANK is the task: N accepted ticks pay income AND satisfy the screen. One tick is NOT
            // enough any more — the screen must survive a lone tick, so the founder gets «пару секунд
            // верчения» instead of a window that vanishes before she reads it.
            driver.DebugAdvanceInputClocks(1f);            // the ~5/s income cap is armed
            fake.Fire(GameInput.MoneyTick);
            Assert.Greater(driver.Game.Money, money0, "the crank is LIVE under the pause — income landed");
            driver.DebugAdvanceNewScale(0.05f);
            Assert.IsFalse(driver.NewScaleSatisfied, "ОДИН тик экран не закрывает (2026-08-07)");
            Assert.IsTrue(driver.NewScaleShowing, "…он всё ещё на экране");

            for (int i = 1; i < GameDriver.MoneyTutorialTicks; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);
                driver.DebugAdvanceNewScale(0.05f);
            }
            Assert.IsTrue(driver.NewScaleSatisfied,
                $"{GameDriver.MoneyTutorialTicks} принятых тиков — условие выполнено");
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "the crank closed the screen");
            Assert.IsFalse(driver.NewScaleOverlay.activeSelf, "overlay hidden");
            Assert.IsFalse(driver.Game.Paused, "game unpaused");

            // Mid-timer resume: the SAME card continues from its frozen timer — not skipped, not reset.
            Assert.AreSame(card, driver.Game.CurrentCard, "same card still up after the screen closed");
            driver.Game.Tick(0.25f);
            Assert.AreEqual(timer0 - 0.25f, driver.Game.CardTimer, 1e-3f,
                "card timer resumed from the frozen value (not zeroed, not restarted)");
            // Age is EVENT-based (holds at the current card's age — no creep past it, 2026-07-23 fix), so at
            // money-open (Age == the card's age 18) it does NOT climb further here.
            Assert.AreEqual(age0, driver.Game.Age, 1e-3f, "age holds at the current card's age (event-age)");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoneyHud_And_Crank_AreLive_ImmediatelyAfterTheScreen_SameCard()
        {
            // Founder Gate-2 bug: after «появилась работа» closed, the pill + crank only went live one
            // card later (the age-gated HUD reveal ran on card-advance only, but the 18-crossing
            // happens mid-card). Now closing the screen refreshes the gates: same card, instantly live.
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                                // opener → playing
            PlayUntilMoneyModal(driver, fake);

            var card = driver.Game.CurrentCard;
            Assert.IsNotNull(card, "a card is up under the modal");

            // While the screen is up the jar is ALREADY on screen — enlarged, as the §D reference draws it.
            Assert.AreSame(driver.MoneyJar, driver.NewScaleBigWidget, "the big scale IS the money jar");

            NewScaleTut.Clear(driver, fake);               // N принятых тиков → экран уходит
            Assert.IsFalse(driver.NewScaleShowing, "screen closed");
            Assert.IsTrue(driver.MoneyJar.activeSelf,
                "money jar visible the MOMENT the screen closes — not one card later");
            Assert.AreEqual(1f, ((RectTransform)driver.MoneyJar.transform).localScale.x, 1e-3f,
                "…and back at ordinary HUD size");

            yield return new WaitForSeconds(0.25f);        // let the ~5/s income-cap clock breathe
            Assert.AreSame(card, driver.Game.CurrentCard, "still the SAME card (timer was frozen ≥3s)");

            double before = driver.Game.Money;
            fake.Fire(GameInput.MoneyTick);
            Assert.Greater(driver.Game.Money, before,
                "the crank pays income immediately on this same card — gameplay is live");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Modal_IgnoresA_ConfirmPlusCrank_Chord_ExceptForTheCrankTask()
        {
            // The old chord hazard (Confirm dismissed a hint, then the same-frame crank leaked into
            // gameplay) cannot arise here — CONFIRM does not close the §D screen at all. What must hold
            // instead: the chord neither closes it via CONFIRM nor banks an answer, and the crank half is
            // scored exactly once, as the task.
            var driver = Boot(out var go, out var fake);
            yield return null;
            fake.Confirm();                                // opener → playing
            PlayUntilMoneyModal(driver, fake);

            yield return new WaitForSeconds(0.3f);         // arm the income cap while paused
            Assert.IsTrue(driver.NewScaleShowing, "still on the screen (waiting did not close it)");
            double money0 = driver.Game.Money;

            // The chord, in source order: Confirm FIRST, then MoneyTick + repeat the SAME frame.
            fake.Confirm();
            Assert.IsTrue(driver.NewScaleShowing, "CONFIRM alone does nothing here");
            fake.Fire(GameInput.MoneyTick);
            double afterFirst = driver.Game.Money;
            Assert.Greater(afterFirst, money0, "the crank half of the chord was accepted (task done)");
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.AreEqual(afterFirst, driver.Game.Money, 1e-6,
                "…and the second half is swallowed by the ~5/s income cap — one tick, not two");
            Assert.AreEqual(1, driver.NewScaleCrankTicks,
                "…и в счёт условия он тоже пошёл ровно ОДИН раз");

            // Докручиваем до N принятых тиков — только тогда задача выполнена.
            for (int i = 1; i < GameDriver.MoneyTutorialTicks; i++)
            {
                driver.DebugAdvanceInputClocks(1f);
                fake.Fire(GameInput.MoneyTick);
            }
            driver.DebugAdvanceNewScale(0.05f);                          // условие засчитано → фейд
            driver.DebugAdvanceNewScale(GameDriver.NewScaleFadeSeconds);
            Assert.IsFalse(driver.NewScaleShowing, "the accepted ticks satisfied the task");

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Space_NeverConfirms_EnterIs_TheSoleConfirmKey_AndTheScreenReArms()
        {
            // REVERSED (founder Gate-2 round 2): Space is the crank ONLY — it must never start, restart,
            // or advance a screen. She holds Space through the necrolog; it must not skip the payoff.
            var driver = Boot(out var go, out var fake);
            yield return null;

            // Opener: neither a fresh Space nor a held repeat may start the life — only Enter.
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "fresh Space does NOT start from the opener");
            fake.Fire(GameInput.MoneyTickRepeat);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "held Space does NOT start either");
            fake.Confirm();                                // Enter is the sole start key
            Assert.AreEqual(GameState.Playing, driver.Game.State, "Enter started the life");

            // Life 1: all-НЕТ straight to the finale, passing every §D screen by working its control.
            int guard = 0;
            while (driver.Game.State == GameState.Playing && guard++ < 600)
            {
                if (driver.NewScaleShowing) { NewScaleTut.Clear(driver, fake); continue; }
                if (driver.TutorialShowing) { fake.Confirm(); continue; }
                fake.No();
            }
            Assert.AreEqual(GameState.Finale, driver.Game.State, "reached the finale");

            // The core founder bug: holding/mashing Space through the necrolog must NOT restart —
            // the payoff screen stays up until she presses Enter.
            fake.Fire(GameInput.MoneyTickRepeat);
            fake.Fire(GameInput.MoneyTick);
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Finale, driver.Game.State,
                "no amount of Space (fresh or held) leaves the finale — the necrolog is not skipped");

            fake.Confirm();                                // Enter — the sole restart key
            Assert.AreEqual(GameState.Opener, driver.Game.State, "Enter restarted from the finale → opener");
            fake.Fire(GameInput.MoneyTick);
            Assert.AreEqual(GameState.Opener, driver.Game.State, "Space still inert on the opener");
            fake.Confirm();                                // Enter → new life
            Assert.AreEqual(GameState.Playing, driver.Game.State, "Enter started life 2");

            // Life 2: the money screen re-arms — it must show again at 18.
            PlayUntilMoneyModal(driver, fake);
            Assert.IsTrue(driver.Game.Paused, "the re-armed screen pauses again");
            NewScaleTut.Clear(driver, fake);
            Assert.IsFalse(driver.NewScaleShowing, "…and its task closes it again");
            Assert.IsFalse(driver.Game.Paused, "unpaused after the screen");

            Object.Destroy(go);
            yield return null;
        }
    }
}
