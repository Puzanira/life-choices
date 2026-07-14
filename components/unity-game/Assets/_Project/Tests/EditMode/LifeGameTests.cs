using System.Collections.Generic;
using NUnit.Framework;

namespace LifeChoices.Tests
{
    /// <summary>
    /// End-to-end model behaviour: timeout rule, death-priority resolution,
    /// stage advancement, natural survival and obituary assembly (contracts #2-#5, #7).
    /// Scripted "players" avoid RNG fragility by driving choices by rule, not by luck.
    /// </summary>
    public class LifeGameTests
    {
        [Test]
        public void FreshRunStartsInChildhoodAtFifty()
        {
            var g = new LifeGame(123);
            Assert.IsFalse(g.IsDead);
            Assert.IsNotNull(g.CurrentCard);
            Assert.AreEqual(Stage.Childhood, g.CurrentStage);
            Assert.AreEqual(50, g.Scales.Get(Scale.Mood));
        }

        [Test]
        public void TimeoutResolvesAsNoPlusMoodMinusFour()
        {
            var g = new LifeGame(7);
            Card card = g.CurrentCard;
            // First card is always a childhood card; its NO side is never lethal.
            var expected = new ScaleState();
            expected.Apply(card.No.WithMoodDelta(-4));

            g.Timeout();

            Assert.AreEqual(expected.Get(Scale.Mood), g.Scales.Get(Scale.Mood));
            Assert.AreEqual(expected.Get(Scale.Health), g.Scales.Get(Scale.Health));
            Assert.AreEqual(expected.Get(Scale.Money), g.Scales.Get(Scale.Money));
            Assert.AreEqual(expected.Get(Scale.People), g.Scales.Get(Scale.People));
        }

        [Test]
        public void ChoosingInstantDeathSideKillsImmediatelyWithThatCause()
        {
            // Play a scale-neutral, death-avoiding line until the first ☠ card
            // (childhood always contains 1.10), then take the lethal side.
            var g = new LifeGame(42);
            Card lethal = DriveUntil(g, c => c.IsInstantDeath, takeDeathSideWhenFound: true);

            Assert.IsNotNull(lethal, "should reach a ☠ card in childhood");
            Assert.IsTrue(g.IsDead);
            Assert.AreEqual(DeathKind.Instant, g.Death.Kind);
            Assert.AreEqual(lethal.DeathCause, g.Death.Cause);
        }

        [Test]
        public void PeacefulFinaleCardDiesNatural()
        {
            var g = new LifeGame(99);
            Card series = DriveUntil(g, c => c.Id == "4.10", takeDeathSideWhenFound: true);

            Assert.IsNotNull(series, "balanced play should survive to the old-age finale card");
            Assert.IsTrue(g.IsDead);
            Assert.AreEqual(DeathKind.Natural, g.Death.Kind);
            StringAssert.Contains("серию", g.Death.Cause);
        }

        [Test]
        public void BalancedPlayerSurvivesAllFourStagesToNaturalDeath()
        {
            var g = new LifeGame(2024);
            int guard = 0;
            while (!g.IsDead && guard++ < 100)
                ChooseBalancedSafe(g);

            Assert.IsTrue(g.IsDead);
            Assert.AreEqual(DeathKind.Natural, g.Death.Kind);
            StringAssert.Contains("Дожил", g.Death.Cause);
            // Reached the last stage on the way to the titles.
            Assert.AreEqual(Stage.OldAge, g.CurrentStage);
        }

        [Test]
        public void DrivingMoodToZeroDiesByBrokenScale()
        {
            // Always pick the non-death side with the most negative mood delta.
            var g = new LifeGame(555);
            int guard = 0;
            while (!g.IsDead && guard++ < 100)
            {
                Side pick = ChooseWorseMood(g.CurrentCard);
                Apply(g, pick);
            }

            Assert.IsTrue(g.IsDead);
            Assert.AreEqual(DeathKind.BrokenScale, g.Death.Kind);
        }

        [Test]
        public void SurvivalRunAdvancesThroughEveryStageInOrder()
        {
            var g = new LifeGame(2024);
            var seen = new List<Stage>();
            int guard = 0;
            while (!g.IsDead && guard++ < 100)
            {
                if (seen.Count == 0 || seen[seen.Count - 1] != g.CurrentStage)
                    seen.Add(g.CurrentStage);
                ChooseBalancedSafe(g);
            }
            CollectionAssert.AreEqual(
                new[] { Stage.Childhood, Stage.Youth, Stage.Adulthood, Stage.OldAge }, seen);
        }

        [Test]
        public void ObituaryReportsThreeMemoriesAndFields()
        {
            var g = new LifeGame(2024);
            int guard = 0;
            while (!g.IsDead && guard++ < 100)
                ChooseBalancedSafe(g);

            Obituary o = g.Obituary;
            Assert.IsNotNull(o);
            Assert.IsFalse(string.IsNullOrEmpty(o.Cause));
            Assert.IsFalse(string.IsNullOrEmpty(o.Label));
            Assert.IsFalse(string.IsNullOrEmpty(o.Funeral));
            Assert.AreEqual(3, o.Memories.Count);
        }

        [Test]
        public void ObituaryFundamentalsMatchScalesAtDeath()
        {
            // Direct assembly check: label by max scale, funeral by Люди.
            var scales = new ScaleState();
            scales.Apply(new Effect(0, 0, 0, 40)); // People 90 dominates
            var history = new List<MemoryEntry>
            {
                new MemoryEntry(CardDatabase.All()[0], Side.Yes, false, 30),
                new MemoryEntry(CardDatabase.All()[1], Side.No, false, 5),
                new MemoryEntry(CardDatabase.All()[2], Side.Yes, false, 18),
                new MemoryEntry(CardDatabase.All()[3], Side.No, false, 25),
            };
            var o = ObituaryBuilder.Build(DeathInfo.Natural("тест"), scales, history);

            Assert.AreEqual("Душа компании", o.Label);
            Assert.AreEqual("Пришли все, даже бывшие и налоговая.", o.Funeral);
            Assert.AreEqual(3, o.Memories.Count);
            // Top-3 by shift are the 30, 25 and 18 entries (the 5 is dropped).
            StringAssert.Contains(CardDatabase.All()[0].Title, o.Memories[0]);
            StringAssert.Contains(CardDatabase.All()[3].Title, o.Memories[1]);
            StringAssert.Contains(CardDatabase.All()[2].Title, o.Memories[2]);
        }

        [Test]
        public void StartNewLifeResetsScalesAndStage()
        {
            var g = new LifeGame(2024);
            int guard = 0;
            while (!g.IsDead && guard++ < 100) ChooseBalancedSafe(g);
            Assert.IsTrue(g.IsDead);

            g.StartNewLife(2024);
            Assert.IsFalse(g.IsDead);
            Assert.AreEqual(Stage.Childhood, g.CurrentStage);
            Assert.AreEqual(50, g.Scales.Get(Scale.Mood));
            Assert.AreEqual(50, g.Scales.Get(Scale.People));
        }

        // ---------- scripted players ----------

        private static void Apply(LifeGame g, Side s)
        {
            if (s == Side.Yes) g.ChooseYes(); else g.ChooseNo();
        }

        /// <summary>Drives balanced-safe play until a card matches; optionally takes
        /// that card's death side. Returns the matched card, or null if the life
        /// ended first.</summary>
        private static Card DriveUntil(LifeGame g, System.Func<Card, bool> match, bool takeDeathSideWhenFound)
        {
            int guard = 0;
            while (!g.IsDead && guard++ < 200)
            {
                Card c = g.CurrentCard;
                if (match(c))
                {
                    if (takeDeathSideWhenFound)
                        Apply(g, c.DeathSide);
                    return c;
                }
                ChooseBalancedSafe(g);
            }
            return null;
        }

        /// <summary>Picks the non-death side that keeps scales closest to 50.</summary>
        private static void ChooseBalancedSafe(LifeGame g)
        {
            Card c = g.CurrentCard;
            Side death = c.DeathSide;
            int yesScore = death == Side.Yes ? int.MaxValue : Deviation(g.Scales, c.Yes);
            int noScore = death == Side.No ? int.MaxValue : Deviation(g.Scales, c.No);
            Apply(g, yesScore <= noScore ? Side.Yes : Side.No);
        }

        private static Side ChooseWorseMood(Card c)
        {
            // Called when one side is forced non-death; if both allowed pick worse mood.
            if (c.DeathSide == Side.Yes) return Side.No;
            if (c.DeathSide == Side.No) return Side.Yes;
            return c.Yes.Mood <= c.No.Mood ? Side.Yes : Side.No;
        }

        private static int Deviation(ScaleState s, Effect e)
        {
            var probe = new ScaleState();
            probe.Apply(new Effect(
                s.Get(Scale.Mood) - 50, s.Get(Scale.Health) - 50,
                s.Get(Scale.Money) - 50, s.Get(Scale.People) - 50));
            probe.Apply(e);
            int max = 0;
            foreach (Scale sc in new[] { Scale.Mood, Scale.Health, Scale.Money, Scale.People })
                max = System.Math.Max(max, System.Math.Abs(probe.Get(sc) - 50));
            return max;
        }
    }
}
