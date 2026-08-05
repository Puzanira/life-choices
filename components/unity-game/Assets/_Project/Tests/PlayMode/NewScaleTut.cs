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
    /// Everything here goes through the ordinary input path (crank cap, breath rhythm gate, balancer axis,
    /// child press window) — nothing is force-flagged — and the modal's own clock is pumped through the
    /// same seam Update drives, so a synchronous test loop can pass through the screen deterministically.
    /// </summary>
    internal static class NewScaleTut
    {
        /// <summary>Satisfy the §D modal that is up (if any) and let it fade out. No-op when none is up.</summary>
        public static void Clear(GameDriver driver, PlayFakeInputSource fake)
        {
            int guard = 0;
            while (driver.NewScaleShowing && driver.Game.State == GameState.Playing && guard++ < 60)
            {
                switch (driver.NewScaleKind)
                {
                    case NewScale.Money:
                        driver.DebugAdvanceInputClocks(1f);          // let the ~5/s income cap re-arm
                        fake.Fire(GameInput.MoneyTick);              // …one ACCEPTED crank tick
                        break;
                    case NewScale.Energy:
                        driver.DebugAdvanceInputClocks(0.8f);        // a calm cadence between breaths
                        fake.Fire(GameInput.EnergyPulse);            // 1st seeds the rhythm, 2nd is valid
                        break;
                    case NewScale.Relations:
                        fake.Fire(GameInput.RelationUp);             // the balancer lever (starts in zone)
                        driver.Game.Tick(0.01f);                     // …consume the latched axis (no stale pull)
                        break;
                    case NewScale.Child:
                        fake.Fire(GameInput.ChildPress);             // «!» — pick the handset up
                        break;
                }
                driver.DebugAdvanceNewScale(0.5f);                   // hold clock + the 0.2 s fade
            }
        }

        /// <summary>Clear whichever modal/hint is up: the §D screen by its control, the S5 hint by GREEN.</summary>
        public static void ClearAny(GameDriver driver, PlayFakeInputSource fake)
        {
            if (driver.NewScaleShowing) Clear(driver, fake);
            if (driver.TutorialShowing) fake.Confirm();
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
            while (driver.NewScaleShowing && driver.Game.State == GameState.Playing && guard++ < 400)
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
                        backend.Next = new BackendSnapshot { Joystick = new Vector2(0f, 1f) };
                        yield return null;
                        break;
                    case NewScale.Child:
                        backend.Next = new BackendSnapshot { BangHeld = true };
                        yield return null; yield return null;
                        backend.Next = default;
                        yield return null;
                        break;
                    case NewScale.Energy:
                        backend.Next = new BackendSnapshot { HeightA = 0.85f };   // вдох — импульс на подъёме
                        yield return null;
                        backend.Next = new BackendSnapshot { HeightA = 0.15f };   // выдох — рычаг взводится
                        float t = 0f;
                        while (t < 0.5f) { t += Time.deltaTime; yield return null; }   // спокойная каденция
                        break;
                }
            }
            backend.Next = default;
        }
    }
}
