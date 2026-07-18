using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The «дыхание» rhythm gate (pure <see cref="BreathRhythm"/>, injected clock): a valid breath is one
    /// landing in the calm cadence window; mashing (too fast) and sparse taps (too slow) yield nothing.
    /// Deterministic — the clock is advanced explicitly, never wall-time.
    /// </summary>
    public class BreathRhythmTests
    {
        private static BreathRhythm New() => new() { RhythmMin = 0.4, RhythmMax = 1.5 };

        [Test]
        public void FirstPulse_Seeds_NotValid()
        {
            var r = New();
            r.Advance(0.8);
            Assert.IsFalse(r.Pulse(), "the very first breath has no predecessor — it only seeds the cadence");
        }

        [Test]
        public void SteadyCadence_InsideWindow_IsValid_EveryCycle()
        {
            var r = New();
            r.Advance(0.8); r.Pulse();          // seed
            int valid = 0;
            for (int i = 0; i < 5; i++)
            {
                r.Advance(0.8);                 // 0.8s apart — inside [0.4,1.5]
                if (r.Pulse()) valid++;
            }
            Assert.AreEqual(5, valid, "a steady ~0.8s cadence is a valid breath every cycle");
        }

        [Test]
        public void Mashing_TooFast_YieldsNothing()
        {
            var r = New();
            r.Advance(0.8); r.Pulse();          // seed
            int valid = 0;
            for (int i = 0; i < 20; i++)
            {
                r.Advance(0.1);                 // 0.1s apart — below the 0.4 floor (долбёж)
                if (r.Pulse()) valid++;
            }
            Assert.AreEqual(0, valid, "mashing (intervals below the floor) never restores energy");
        }

        [Test]
        public void SparseTaps_TooSlow_YieldNothing()
        {
            var r = New();
            r.Advance(0.8); r.Pulse();          // seed
            int valid = 0;
            for (int i = 0; i < 5; i++)
            {
                r.Advance(3.0);                 // 3s apart — above the 1.5 ceiling (слишком редко)
                if (r.Pulse()) valid++;
            }
            Assert.AreEqual(0, valid, "too-sparse taps (above the ceiling) never restore energy");
        }

        [Test]
        public void SparseTap_ReSeeds_SoNextWellTimedBreathCanCount()
        {
            var r = New();
            r.Advance(0.8); r.Pulse();          // seed
            r.Advance(3.0); Assert.IsFalse(r.Pulse(), "sparse tap invalid but re-anchors the cadence");
            r.Advance(0.8); Assert.IsTrue(r.Pulse(), "a well-timed breath after a sparse tap counts");
        }

        [Test]
        public void Boundaries_AreInclusive()
        {
            var lo = New();
            lo.Advance(1.0); lo.Pulse();
            lo.Advance(0.4); Assert.IsTrue(lo.Pulse(), "exactly the min interval is valid (inclusive)");

            var hi = New();
            hi.Advance(1.0); hi.Pulse();
            hi.Advance(1.5); Assert.IsTrue(hi.Pulse(), "exactly the max interval is valid (inclusive)");

            var under = New();
            under.Advance(1.0); under.Pulse();
            under.Advance(0.39); Assert.IsFalse(under.Pulse(), "just under the floor is invalid");

            var over = New();
            over.Advance(1.0); over.Pulse();
            over.Advance(1.51); Assert.IsFalse(over.Pulse(), "just over the ceiling is invalid");
        }

        [Test]
        public void Reset_ClearsCadence()
        {
            var r = New();
            r.Advance(0.8); r.Pulse();
            r.Reset();
            r.Advance(0.8);
            Assert.IsFalse(r.Pulse(), "after reset the next breath only re-seeds the cadence");
        }
    }
}
