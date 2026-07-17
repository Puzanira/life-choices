using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    public class ScalesTests
    {
        [Test]
        public void Starts_From_GameSpec_Defaults()
        {
            var s = new Scales();
            Assert.AreEqual(100, s.Health);
            Assert.AreEqual(100, s.Energy);
            Assert.AreEqual(0, s.Money);
            Assert.AreEqual(55, s.Relationships);
            Assert.AreEqual(0, s.Child);
        }

        [Test]
        public void Apply_Add_Set_Random()
        {
            var s = new Scales();
            s.Apply(new[]
            {
                new ScaleDelta(Scale.Health, DeltaKind.Add, -15),
                new ScaleDelta(Scale.Money, DeltaKind.Add, 2),
                new ScaleDelta(Scale.Energy, DeltaKind.Set, 80),
                new ScaleDelta(Scale.Relationships, DeltaKind.RandomPlusMinus, 3),
            }, coin: () => true); // "+" side of ±

            Assert.AreEqual(85, s.Health);
            Assert.AreEqual(2, s.Money);
            Assert.AreEqual(80, s.Energy);
            Assert.AreEqual(58, s.Relationships, "±3 with coin=+ adds 3 to 55");
        }

        [Test]
        public void Random_MinusSide()
        {
            var s = new Scales();
            s.Apply(new[] { new ScaleDelta(Scale.Money, DeltaKind.RandomPlusMinus, 3) },
                coin: () => false);
            Assert.AreEqual(-3, s.Money);
        }

        [Test]
        public void Health_Depletion_Detected()
        {
            var s = new Scales();
            Assert.IsFalse(s.HealthDepleted);
            s.Apply(new[] { new ScaleDelta(Scale.Health, DeltaKind.Set, 0) }, () => true);
            Assert.IsTrue(s.HealthDepleted);
        }

        [Test]
        public void Reset_Restores_Defaults()
        {
            var s = new Scales();
            s.Apply(new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -50) }, () => true);
            s.Reset();
            Assert.AreEqual(100, s.Health);
        }
    }
}
