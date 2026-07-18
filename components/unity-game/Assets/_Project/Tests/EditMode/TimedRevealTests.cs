using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// The shared auto-hiding reveal (injected clock) behind the host bubble (~2s) and banner (~1.5s):
    /// visible after Show, auto-hides once Duration elapses (reporting the hand-off step), Hide is
    /// immediate, and Show re-arms the countdown.
    /// </summary>
    public class TimedRevealTests
    {
        [Test]
        public void Show_MakesVisible_WithText()
        {
            var r = new TimedReveal(2.0);
            Assert.IsFalse(r.Visible, "hidden before Show");
            r.Show("Браво!");
            Assert.IsTrue(r.Visible);
            Assert.AreEqual("Браво!", r.Text);
        }

        [Test]
        public void Advance_AutoHides_AfterDuration_AndReportsTheStep()
        {
            var r = new TimedReveal(1.5);
            r.Show("ПОРА ЗАРАБАТЫВАТЬ!");
            Assert.IsFalse(r.Advance(1.0), "still visible partway through");
            Assert.IsTrue(r.Visible);
            Assert.IsTrue(r.Advance(0.6), "the step that crosses 1.5s reports the auto-hide");
            Assert.IsFalse(r.Visible, "hidden after the full duration");
            Assert.IsFalse(r.Advance(1.0), "no-op once hidden (no repeated hand-off)");
        }

        [Test]
        public void Hide_IsImmediate()
        {
            var r = new TimedReveal(2.0);
            r.Show("Уважаю!");
            r.Hide();
            Assert.IsFalse(r.Visible);
            Assert.IsFalse(r.Advance(0.1), "advancing a hidden reveal never re-shows it");
        }

        [Test]
        public void Show_ReArmsCountdown()
        {
            var r = new TimedReveal(2.0);
            r.Show("a");
            r.Advance(1.9);
            r.Show("b");                       // re-arm
            Assert.IsFalse(r.Advance(1.0), "re-armed: not hidden 1.0s after the second Show");
            Assert.IsTrue(r.Visible);
            Assert.AreEqual("b", r.Text);
        }
    }
}
