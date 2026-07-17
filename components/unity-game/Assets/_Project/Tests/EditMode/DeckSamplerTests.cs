using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using W = ThanksNoThanks.DeckSampler.AgeWindow;

namespace ThanksNoThanks.Tests
{
    public class DeckSamplerTests
    {
        private static IReadOnlyList<Card> AllCards()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes.csv present");
            return CardLoader.ParseAll(asset.text);
        }

        private static List<Card> Sample(int seed) => DeckSampler.Build(AllCards(), new System.Random(seed));

        // ---- age-window parsing ----

        [Test]
        public void AgeWindow_Range_Open_Single_Any_Start_Marriage()
        {
            var range = W.Parse("4–8");                 // en-dash range
            Assert.AreEqual(4, range.Min); Assert.AreEqual(8, range.Max);

            var hyphen = W.Parse("55-70, если здоровье забивал"); // ascii hyphen + trailing condition
            Assert.AreEqual(55, hyphen.Min); Assert.AreEqual(70, hyphen.Max);

            var open = W.Parse("20+");
            Assert.AreEqual(20, open.Min); Assert.AreEqual(DeckSampler.LifeMax, open.Max);

            var single = W.Parse("18");
            Assert.AreEqual(18, single.Min); Assert.AreEqual(18, single.Max);

            var any = W.Parse("любой");
            Assert.AreEqual(DeckSampler.LifeAdultMin, any.Min); Assert.AreEqual(DeckSampler.LifeMax, any.Max);

            var start = W.Parse("0–3, старт игры");
            Assert.AreEqual(0, start.Min); Assert.AreEqual(3, start.Max);

            var marriage = W.Parse("свадьба +2");
            Assert.IsTrue(marriage.IsMarriageOffset, "«свадьба +2» flagged as a marriage offset");
        }

        // ---- excluded IDs never drawn ----

        [Test]
        public void ExcludedIds_NeverDrawn_AcrossSeeds()
        {
            Assert.AreEqual(13, DeckSampler.Excluded.Count, "exactly the 13 hard-excluded IDs");
            for (int seed = 0; seed < 40; seed++)
            {
                var ids = Sample(seed).Select(c => c.Id).ToHashSet();
                foreach (var bad in DeckSampler.Excluded)
                    Assert.IsFalse(ids.Contains(bad), $"excluded {bad} must never appear (seed {seed})");
            }
        }

        // ---- milestones always present at canonical ages ----

        [Test]
        public void Milestones_AlwaysPresent_AtCanonicalAges()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var deck = Sample(seed);
                var byId = deck.ToDictionary(c => c.Id);

                foreach (var id in new[] { "I02", "I03", "YA01", "YA03", "YA05", "MD01" })
                    Assert.IsTrue(byId.ContainsKey(id), $"milestone {id} always in the deck (seed {seed})");

                Assert.AreEqual(0, byId["I02"].Age, "I02 pinned to age 0 (intro first)");
                Assert.AreEqual(1, byId["I03"].Age, "I03 at age 1");
                Assert.AreEqual(18, byId["YA01"].Age);
                Assert.AreEqual(20, byId["YA03"].Age);
                Assert.AreEqual(25, byId["YA05"].Age);
                Assert.That(byId["MD01"].Age, Is.InRange(28, 32), "MD01 sampled inside its 28–32 window");

                Assert.AreEqual("I02", deck[0].Id, "I02 is always first");
                Assert.AreEqual("I03", deck[1].Id, "I03 is always second");
            }
        }

        // ---- variability: two seeds differ, same seed identical ----

        [Test]
        public void DifferentSeeds_ProduceDifferentDecks()
        {
            var a = Sample(101).Select(c => c.Id + "@" + c.Age).ToList();
            var b = Sample(202).Select(c => c.Id + "@" + c.Age).ToList();
            Assert.AreNotEqual(a, b, "two seeds yield different decks (set and/or order)");
        }

        [Test]
        public void SameSeed_ProducesIdenticalDeck()
        {
            var a = Sample(555).Select(c => c.Id + "@" + c.Age).ToList();
            var b = Sample(555).Select(c => c.Id + "@" + c.Age).ToList();
            CollectionAssert.AreEqual(a, b, "same seed is fully deterministic");
        }

        // ---- deck size cap ----

        [Test]
        public void DeckSize_Within_25_to_30_AcrossSeeds()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                int n = Sample(seed).Count;
                Assert.That(n, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                    $"deck size {n} within [25,30] (seed {seed})");
            }
        }

        // ---- ordering + chain placement ----

        [Test]
        public void Deck_Is_AgeOrdered()
        {
            var deck = Sample(7);
            for (int i = 1; i < deck.Count; i++)
                Assert.LessOrEqual(deck[i - 1].Age, deck[i].Age, "deck drawn in assigned-age order");
        }

        [Test]
        public void MD02_Age_Is_MD01_Plus_Two_And_Gated()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var byId = Sample(seed).ToDictionary(c => c.Id);
                Assert.IsTrue(byId.ContainsKey("MD02"), "MD02 planned (gated on MD01)");
                Assert.AreEqual(byId["MD01"].Age + 2, byId["MD02"].Age, "ребёнок = свадьба + 2");
                Assert.AreEqual("MD01", byId["MD02"].RequiresParentYes, "MD02 gated on MD01=ДА");
                Assert.AreEqual("MD02", byId["LT04"].RequiresParentYes, "LT04 gated on MD02=ДА");
                Assert.AreEqual("CH07", byId["LT07"].RequiresParentYes, "LT07 gated on CH07=ДА");
                Assert.AreEqual("CH08", byId["MD07"].RequiresParentYes, "MD07 gated on CH08=ДА");
            }
        }

        [Test]
        public void LifePhases_AreSpread_NotFrontLoaded()
        {
            // Every run must span childhood → young → midlife → old age, not cluster early.
            for (int seed = 1; seed <= 20; seed++)
            {
                var deck = Sample(seed);
                int childhood = deck.Count(c => c.Age <= DeckSampler.ChildhoodMaxAge);
                int young = deck.Count(c => c.Age > DeckSampler.ChildhoodMaxAge && c.Age <= DeckSampler.YoungMaxAge);
                int mid = deck.Count(c => c.Age > DeckSampler.YoungMaxAge && c.Age <= DeckSampler.MidMaxAge);
                int old = deck.Count(c => c.Age > DeckSampler.MidMaxAge);

                Assert.GreaterOrEqual(childhood, 6, $"childhood ~6-8 (seed {seed})");
                Assert.GreaterOrEqual(young, 1, $"young adult populated (seed {seed})");
                Assert.GreaterOrEqual(mid, 1, $"midlife populated (seed {seed})");
                Assert.GreaterOrEqual(old, 1, $"old age reached (seed {seed})");
            }
        }
    }
}
