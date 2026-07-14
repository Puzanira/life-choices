using NUnit.Framework;

namespace LifeChoices.Tests
{
    /// <summary>Scale clamping and lethal-boundary detection (contract #3, #4).</summary>
    public class ScaleStateTests
    {
        [Test]
        public void StartsAtFifty()
        {
            var s = new ScaleState();
            Assert.AreEqual(50, s.Get(Scale.Mood));
            Assert.AreEqual(50, s.Get(Scale.Health));
            Assert.AreEqual(50, s.Get(Scale.Money));
            Assert.AreEqual(50, s.Get(Scale.People));
        }

        [Test]
        public void ApplyMovesValues()
        {
            var s = new ScaleState();
            s.Apply(new Effect(10, -8, 0, 8));
            Assert.AreEqual(60, s.Get(Scale.Mood));
            Assert.AreEqual(42, s.Get(Scale.Health));
            Assert.AreEqual(50, s.Get(Scale.Money));
            Assert.AreEqual(58, s.Get(Scale.People));
        }

        [Test]
        public void ClampsAtZero_AndReportsBrokenLow()
        {
            var s = new ScaleState();
            s.Apply(new Effect(-999, 0, 0, 0));
            Assert.AreEqual(0, s.Get(Scale.Mood));
            Assert.IsTrue(s.TryGetBroken(out var scale, out var high));
            Assert.AreEqual(Scale.Mood, scale);
            Assert.IsFalse(high);
        }

        [Test]
        public void ClampsAtHundred_AndReportsBrokenHigh()
        {
            var s = new ScaleState();
            s.Apply(new Effect(0, 0, 0, 999));
            Assert.AreEqual(100, s.Get(Scale.People));
            Assert.IsTrue(s.TryGetBroken(out var scale, out var high));
            Assert.AreEqual(Scale.People, scale);
            Assert.IsTrue(high);
        }

        [Test]
        public void NoBreakInMidRange()
        {
            var s = new ScaleState();
            s.Apply(new Effect(20, -20, 10, -10));
            Assert.IsFalse(s.TryGetBroken(out _, out _));
        }

        [Test]
        public void HighestTieBreaksByEnumOrder()
        {
            var s = new ScaleState();
            s.Apply(new Effect(20, 20, 0, 0)); // Mood and Health both 70
            var top = s.Highest(out int val);
            Assert.AreEqual(70, val);
            Assert.AreEqual(Scale.Mood, top);
        }
    }

    public class EffectTests
    {
        [Test]
        public void AbsSumAddsMagnitudes()
        {
            Assert.AreEqual(26, new Effect(10, -8, 0, 8).AbsSum());
        }

        [Test]
        public void WithMoodDeltaOnlyTouchesMood()
        {
            var e = new Effect(4, 0, 0, 6).WithMoodDelta(-4);
            Assert.AreEqual(0, e.Mood);
            Assert.AreEqual(6, e.People);
        }
    }

    /// <summary>All ~40 cards load and are shaped correctly (contract #6).</summary>
    public class CardContentTests
    {
        [Test]
        public void FortyCardsTotal()
        {
            Assert.AreEqual(40, CardDatabase.All().Count);
        }

        [Test]
        public void TenCardsPerStage()
        {
            foreach (Stage st in System.Enum.GetValues(typeof(Stage)))
                Assert.AreEqual(10, CardDatabase.ForStage(st).Count, $"stage {st}");
        }

        [Test]
        public void EveryCardHasTitle()
        {
            foreach (var c in CardDatabase.All())
                Assert.IsFalse(string.IsNullOrWhiteSpace(c.Title), c.Id);
        }

        [Test]
        public void InstantDeathCardsAreMarked()
        {
            var socket = Find("1.10");
            Assert.AreEqual(Side.Yes, socket.DeathSide);
            Assert.IsTrue(socket.IsInstantDeath);

            var drive = Find("2.10");
            Assert.AreEqual(Side.Yes, drive.DeathSide);
            Assert.IsTrue(drive.IsInstantDeath);
        }

        [Test]
        public void PeacefulFinaleCardIsNaturalNotInstant()
        {
            var series = Find("4.10");
            Assert.AreEqual(Side.Yes, series.DeathSide);
            Assert.IsTrue(series.PeacefulDeath);
            Assert.IsFalse(series.IsInstantDeath);
        }

        [Test]
        public void CryptoCardIsNotADeathCard()
        {
            var crypto = Find("3.10");
            Assert.IsFalse(crypto.HasDeathSide);
            Assert.AreEqual(-40, crypto.Yes.Money);
        }

        [Test]
        public void LinkedCardsAreFlagged()
        {
            Assert.IsTrue(Find("1.8").Linked);
            Assert.IsTrue(Find("3.8").Linked);
            Assert.IsTrue(Find("4.8").Linked);
        }

        private static Card Find(string id) => CardDatabase.All().Find(c => c.Id == id);
    }

    public class DeathTablesTests
    {
        [Test]
        public void EightScaleDeathsAllDistinct()
        {
            var causes = new System.Collections.Generic.HashSet<string>();
            foreach (Scale s in new[] { Scale.Mood, Scale.Health, Scale.Money, Scale.People })
            {
                causes.Add(DeathTables.BrokenCause(s, false));
                causes.Add(DeathTables.BrokenCause(s, true));
            }
            Assert.AreEqual(8, causes.Count, "each scale must yield two distinct deaths");
        }

        [Test]
        public void LabelByDominantScale()
        {
            var s = new ScaleState();
            s.Apply(new Effect(0, 0, 40, 0)); // Money 90
            Assert.AreEqual("Успешный успех™", DeathTables.Label(s));
        }

        [Test]
        public void LabelNearFiftyIsLivedLikeEveryone()
        {
            var s = new ScaleState();
            s.Apply(new Effect(3, -2, 1, 0)); // nothing dominates
            Assert.AreEqual("Прожил как все", DeathTables.Label(s));
        }

        [Test]
        public void FuneralBands()
        {
            Assert.AreEqual("Пришли все, даже бывшие и налоговая.", DeathTables.Funeral(80));
            Assert.AreEqual("Пришли родственники и пара коллег.", DeathTables.Funeral(50));
            Assert.AreEqual("Пришли двое: нотариус и кот.", DeathTables.Funeral(10));
        }
    }
}
