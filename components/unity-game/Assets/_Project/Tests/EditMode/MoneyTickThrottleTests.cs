using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The crank input pipeline, deterministic (injected clocks, never wall-time):
    ///  • <see cref="MoneyCrankAutoRepeat"/> (source): held Space ≈4/s accumulator-scheduled (no
    ///    under-run drift), every discrete keydown emits IMMEDIATELY as a distinct Press — never
    ///    swallowed (Space-confirm must be instant outside gameplay) and never confused with a Repeat
    ///    (held Space must not confirm screens);
    ///  • <see cref="MoneyTickThrottle"/> (driver, gameplay-crank branch only): income capped ≈5/s.
    /// </summary>
    public class MoneyTickThrottleTests
    {
        // Composed source→driver pipeline: pressAt(frame) = discrete keydown on that frame.
        // Presses and repeats both count as income (that is the gameplay branch behaviour).
        private static int RunPipeline(double seconds, int fps, System.Func<int, bool> pressAt, bool held)
        {
            var rep = new MoneyCrankAutoRepeat();
            var cap = new MoneyTickThrottle();
            double dt = 1.0 / fps;
            int frames = (int)System.Math.Round(seconds * fps);
            int accepted = 0;
            for (int i = 0; i < frames; i++)
            {
                cap.Advance(dt);                     // the driver Update advances the cap clock
                if (rep.Step(dt, pressAt(i), held) != CrankEmit.None && cap.TryAccept())
                    accepted++;
            }
            return accepted;
        }

        // ---- the skeptic's swallow scenario (source layer) ----

        [Test]
        public void DiscretePress_AlwaysEmits_EvenRightAfterAnotherTick()
        {
            // Two presses 0.05s apart: the OLD source-side cap swallowed the second (inside the 0.2s
            // window) — which ate Space-confirm right after cranking. The repeater must emit BOTH as
            // fresh Presses; rate-limiting is the driver's business and only for income.
            var rep = new MoneyCrankAutoRepeat();
            Assert.AreEqual(CrankEmit.Press, rep.Step(1.0 / 60, true, true), "first keydown emits a Press");
            int presses = 0;
            for (int i = 0; i < 3; i++)
                if (rep.Step(1.0 / 60, i == 2, true) == CrankEmit.Press) presses++;   // +0.05s → press
            Assert.AreEqual(1, presses, "a discrete keydown 0.05s later STILL emits a Press (never swallowed)");
        }

        [Test]
        public void Repeats_AreDistinguishable_FromFreshPresses()
        {
            // Held Space: the first frame's keydown is a Press; every subsequent emission while held
            // must be a Repeat — the driver keeps Repeats inert outside gameplay (no screen-confirm).
            var rep = new MoneyCrankAutoRepeat();
            double dt = 1.0 / 60;
            Assert.AreEqual(CrankEmit.Press, rep.Step(dt, true, true));
            int presses = 0, repeats = 0;
            for (int i = 0; i < 120; i++)            // 2s of holding, no new keydown
            {
                var e = rep.Step(dt, false, true);
                if (e == CrankEmit.Press) presses++;
                else if (e == CrankEmit.Repeat) repeats++;
            }
            Assert.AreEqual(0, presses, "holding never fabricates a fresh Press");
            Assert.That(repeats, Is.InRange(7, 9), $"2s hold ≈4/s of Repeats (got {repeats})");
        }

        // ---- auto-repeat rate + long-hold drift ----

        [Test]
        public void HeldSpace_AutoRepeats_AboutFourPerSecond()
        {
            int ticks = RunPipeline(1.0, 60, i => i == 0, held: true);
            Assert.That(ticks, Is.InRange(4, 6), $"held Space ≈4/s (got {ticks})");
        }

        [Test]
        public void LongHold_TenSeconds_NoRateDrift()
        {
            // Accumulator scheduling: a 10s hold at fixed 60fps dt lands 40±1 ticks — the old
            // re-anchor-on-emit design drifted to 16-frame intervals (~3.75/s, 38 ticks).
            int ticks = RunPipeline(10.0, 60, i => i == 0, held: true);
            Assert.That(ticks, Is.InRange(39, 41), $"10s hold is a true ≈4/s, no under-run drift (got {ticks})");
        }

        [Test]
        public void Stall_DoesNotBurst_CatchUpTicks()
        {
            // One 2s frame mid-hold (editor hiccup): at most ONE repeat for the stall, and the
            // schedule re-arms cleanly instead of machine-gunning the backlog.
            var rep = new MoneyCrankAutoRepeat();
            rep.Step(1.0 / 60, true, true);                       // press, schedule armed
            Assert.AreEqual(CrankEmit.Repeat, rep.Step(2.0, false, true), "stall emits a single repeat");
            int immediate = 0;
            for (int i = 0; i < 6; i++)                           // next 0.1s — nothing due yet
                if (rep.Step(1.0 / 60, false, true) != CrankEmit.None) immediate++;
            Assert.AreEqual(0, immediate, "no catch-up burst after the stall");
        }

        // ---- cap: mash-gun and press+hold combinations ----

        [Test]
        public void MashGun_120PressesPerSecond_ClampedToCap()
        {
            int ticks = RunPipeline(1.0, 120, _ => true, held: true);
            Assert.That(ticks, Is.InRange(4, 6), $"120 presses/s clamped to ~5/s (got {ticks})");
        }

        [Test]
        public void AlternatingPressAndHold_CannotExceedCap()
        {
            // A press every 5th frame ON TOP of holding (press + hold interleave) — the combined
            // stream must still respect the ~5/s income cap over 2 seconds.
            int ticks = RunPipeline(2.0, 60, i => i % 5 == 0, held: true);
            Assert.LessOrEqual(ticks, 11, $"2s of press+hold interleave stays ≤ cap (got {ticks})");
            Assert.GreaterOrEqual(ticks, 8, "still cranks near the cap, not starved");
        }

        [Test]
        public void SingleDiscretePress_EmitsExactlyOne()
        {
            int ticks = RunPipeline(0.5, 60, i => i == 0, held: false);
            Assert.AreEqual(1, ticks, "one keydown → one accepted tick, then silence");
        }
    }
}
