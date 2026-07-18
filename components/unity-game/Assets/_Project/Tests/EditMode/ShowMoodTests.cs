using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The «реакция шоу на состояние» brightness curve (pure <see cref="ShowMood"/>): the dark veil is a
    /// monotonically non-increasing function of the mean live state — more life is never darker.
    /// </summary>
    public class ShowMoodTests
    {
        private const double Eps = 1e-9;

        [Test]
        public void FullState_NoVeil_EmptyState_MaxVeil()
        {
            Assert.AreEqual(0.0, ShowMood.DarkAlpha(100), Eps, "full state → бright, no veil");
            Assert.AreEqual(ShowMood.MaxDarkAlpha, ShowMood.DarkAlpha(0), Eps, "empty state → darkest veil");
        }

        [Test]
        public void Alpha_IsMonotonicNonIncreasing_InState()
        {
            double prev = ShowMood.DarkAlpha(0);
            for (int s = 1; s <= 100; s++)
            {
                double a = ShowMood.DarkAlpha(s);
                Assert.LessOrEqual(a, prev + Eps, $"veil never darkens as state rises (at {s})");
                prev = a;
            }
        }

        [Test]
        public void Alpha_IsClamped_OutsideRange()
        {
            Assert.AreEqual(ShowMood.MaxDarkAlpha, ShowMood.DarkAlpha(-50), Eps, "below 0 clamps to darkest");
            Assert.AreEqual(0.0, ShowMood.DarkAlpha(150), Eps, "above 100 clamps to no veil");
        }

        [Test]
        public void Midpoint_IsHalfDark()
        {
            Assert.AreEqual(ShowMood.MaxDarkAlpha * 0.5, ShowMood.DarkAlpha(50), Eps, "50% state → half veil");
        }

        [Test]
        public void DarkAlphaFor_AveragesTheTwoScales()
        {
            // health 100 + energy 0 → mean 50 → half veil (same as DarkAlpha(50)).
            Assert.AreEqual(ShowMood.DarkAlpha(50), ShowMood.DarkAlphaFor(100, 0), Eps);
            Assert.AreEqual(0.0, ShowMood.DarkAlphaFor(100, 100), Eps, "both full → no veil");
            // Negative (dead) scales are floored at 0 before averaging.
            Assert.AreEqual(ShowMood.DarkAlpha(50), ShowMood.DarkAlphaFor(100, -20), Eps);
        }
    }
}
