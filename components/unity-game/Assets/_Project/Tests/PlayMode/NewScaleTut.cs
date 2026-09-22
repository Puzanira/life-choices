using System.Collections;
using AiGameStudio.ArcadeControls;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// Shared helper for every life-walking PlayMode test: the four `OPEN:*` scales now raise the §D
    /// MODAL («экран появления новой шкалы») instead of the old text hint, and that modal does NOT close
    /// on a button — only on the scale's REAL control. So a walker that used to say
    /// «<c>if (driver.TutorialShowing) fake.Confirm();</c>» has to work the control instead.
    ///
    /// Everything here goes through the ordinary input path (crank cap, held height sensor, balancer axis,
    /// child press window) — nothing is force-flagged — and the modal's own clock is pumped through the
    /// same seam Update drives, so a synchronous test loop can pass through the screen deterministically.
    /// </summary>
    internal static class NewScaleTut
    {
        /// <summary>Satisfy the §D modal that is up (if any) and let it fade out. No-op when none is up.</summary>
        public static void Clear(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (driver.NewScaleShowing && driver.Game.State == GameState.Playing && guard++ < 200)
            {
                switch (driver.NewScaleKind)
                {
                    case NewScale.Money:
                        driver.DebugAdvanceInputClocks(1f);          // let the ~5/s income cap re-arm
                        fake.Fire(GameInput.MoneyTick);              // …one ACCEPTED crank tick (N нужно N)
                        break;
                    case NewScale.Energy:
                        fake.Fire(GameInput.EnergyHold);             // датчик зажат ЭТОТ такт…
                        driver.Game.Tick(0.5f);                      // …и такт наполняет батарею (TickModalBreath)
                        break;
                    case NewScale.Relations:
                        fake.Fire(GameInput.RelationRight);             // the balancer lever (starts in zone)
                        driver.Game.Tick(0.01f);                     // …consume the latched axis (no stale pull)
                        break;
                    case NewScale.Child:
                        fake.Fire(GameInput.ChildPress);             // «!» — pick the handset up
                        break;
                }
                driver.DebugAdvanceNewScale(0.5f);                   // hold clock + the 0.2 s fade
            }
        }

        /// <summary>
        /// r3: снять ВХОДНОЙ ЭКРАН СПЕЦРЕЖИМА (здоровье / блиц / депрессия / первое выгорание) тем же
        /// путём, каким его снимает игрок, — ЗЕЛЁНОЙ кнопкой. Экран глушит остаток кадра (аккорд
        /// «Enter + зелёная» не должен утечь в ловлю пульса), поэтому синхронный цикл сразу сбрасывает
        /// одноразовые гейты — в живой игре их сбрасывает следующий кадр Update.
        /// </summary>
        public static void ClearSpecial(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (driver.SpecialModeShowing && guard++ < 20)
            {
                fake.Yes();                       // ЗЕЛЁНАЯ — единственный живой контрол под этим экраном
                driver.DebugClearFrameGuards();
            }
        }

        /// <summary>Clear whichever modal/hint/screen is up: §D by its control, S5 by GREEN, спецрежим по GREEN.</summary>
        public static void ClearAny(GameDriver driver, PlayFakeInputSource fake)
        {
            if (driver.SpecialModeShowing) ClearSpecial(driver, fake);
            if (driver.NewScaleShowing) Clear(driver, fake);
            if (driver.TutorialShowing) { fake.Confirm(); driver.DebugClearFrameGuards(); }
            if (driver.SpecialModeShowing) ClearSpecial(driver, fake);   // очередь из двух окон
        }

        /// <summary>
        /// The same, but on the PHYSICAL cabinet chain (FakeBackend → ArcadeInput → ArcadeInputSource):
        /// the crank really turns, the joystick really holds, «!» is really pressed and the breathing lever
        /// really rides up and down in a calm cadence. Real frames pass, so the modal's own hold/fade clock
        /// runs in Update exactly as it does for a player.
        /// </summary>
        public static IEnumerator ClearArcade(GameDriver driver, FakeBackend backend)
        {
            int guard = 0;
            while (driver.NewScaleShowing && driver.Game.State == GameState.Playing && guard++ < 1500)
            {
                switch (driver.NewScaleKind)
                {
                    case NewScale.Money:
                        backend.Next = new BackendSnapshot { CrankDeltaDegrees = 24f };   // > degreesPerMoneyTick
                        yield return null;
                        backend.Next = default;                                           // delta is per-poll
                        yield return null;
                        break;
                    case NewScale.Relations:
                        // ⚠ r7 п.1: балансир ведёт ГОРИЗОНТАЛЬ джойстика (X), не вертикаль. Вправо = рост.
                        backend.Next = new BackendSnapshot { Joystick = new Vector2(1f, 0f) };
                        yield return null;
                        break;
                    case NewScale.Child:
                        backend.Next = new BackendSnapshot { BangHeld = true };
                        yield return null; yield return null;
                        backend.Next = default;
                        yield return null;
                        break;
                    case NewScale.Energy:
                        // «Зажми и держи»: датчик просто СТОИТ поднятым кадр за кадром, пока батарея не
                        // наполнится и экран не уйдёт сам (значение per-poll — поэтому ставим каждый кадр).
                        backend.Next = new BackendSnapshot { HeightA = 0.85f };
                        yield return null;
                        break;
                }
            }
            backend.Next = default;
        }
    }
}
