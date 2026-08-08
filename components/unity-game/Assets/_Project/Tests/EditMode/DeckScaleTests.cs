using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// КОЛОДА РАСТЁТ ВТРОЕ, ЗАБЕГ — НЕТ. Сценарная сессия дописала отрезки 1–7: 85 → ~243 карточки
    /// (детство 36 · юность 35 · молодость 40 + 48 · 30–39 30 · 40–55 27 · 56–100 27). Показываться при
    /// этом должно столько же — «вехи обязательны, филлеры по окнам».
    ///
    /// Здесь сажается ПРАВИЛО и его тест: длина забега — свойство сэмплера (цели по фазам и окнам), а не
    /// функция размера пула. Сама колода в этом инкременте остаётся на 85 строках, поэтому пул до 243
    /// добивается СИНТЕТИЧЕСКИМИ филлерами с настоящими окнами «Когда».
    ///
    /// ⚠ ЧИСЛО «25–30». Дизайн-док и хендофф говорят «за забег по-прежнему ~25–30 карточек». Это число
    /// ПРОТУХЛО: пейсинг-фикс основательницы (2026-07-23) требует ≥8 обычных карточек между соседними
    /// открытиями механик (деньги@18 → отношения@20 → энергия@25 → здоровье@30), а три таких зазора плюс
    /// детство и старость арифметически не помещаются в тридцать карточек. Тогда же коридор и был поднят
    /// до <see cref="DeckSampler.MinDeck"/>…<see cref="DeckSampler.MaxDeck"/>. Инвариант, который
    /// действительно имели в виду, — «столько же, сколько сейчас», и проверяется именно он.
    /// </summary>
    public class DeckScaleTests
    {
        // Профиль дописанной колоды по отрезкам 1–7: окно «Когда» → сколько филлеров дописать.
        private static readonly (string When, int Count)[] GrowthPlan =
        {
            ("4–17", 24),    // отрезок 1, детство: 10 → 36
            ("18–19", 22),   // отрезок 2, юность: 13 → 35
            ("20–24", 26),   // отрезок 3: 14 → 40
            ("25–29", 27),   // отрезок 4: 19 → 48
            ("30–39", 22),   // отрезок 5: 7 → 30
            ("40–55", 20),   // отрезок 6: 7 → 27
            ("56–100", 21),  // отрезок 7: 6 → 27
        };

        /// <summary>Живые 85 строк + синтетические филлеры до ~243 — пул, каким он станет после посадки.</summary>
        private static List<Card> GrownPool()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            var pool = CardLoader.ParseAll(csv.text);
            int order = pool.Count;
            foreach (var (when, count) in GrowthPlan)
                for (int i = 0; i < count; i++)
                {
                    string id = "GROW_" + when.Replace("–", "_") + "_" + i;
                    pool.Add(new Card
                    {
                        Id = id, Question = id + "?", When = when,
                        Age = CardLoaderAge(when), Order = order++,
                        YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                        YesNecrolog = "y" + id, NoNecrolog = "n" + id,
                        Flags = new List<string>(),
                    });
                }
            return pool;
        }

        private static int CardLoaderAge(string when) => DeckSampler.AgeWindow.Parse(when).Min;

        [Test]
        public void GrownPool_IsAboutTwoHundredForty()
        {
            Assert.That(GrownPool().Count, Is.InRange(235, 250),
                "модель дописанной колоды — те самые ~243 карточки");
        }

        [Test]
        public void RunLength_DoesNotGrowWithThePool()
        {
            // ГЛАВНЫЙ ИНВАРИАНТ: тройной пул не удлиняет забег. Сравниваем не с литералом, а с самим
            // сэмплером на ЖИВОЙ колоде — если однажды коридор пересмотрят, тест не соврёт.
            var csv = Resources.Load<TextAsset>("scenes");
            var live = CardLoader.ParseAll(csv.text);
            var grown = GrownPool();

            for (int seed = 0; seed < 20; seed++)
            {
                int liveSize = DeckSampler.BuildPlan(live, new System.Random(seed)).Deck.Count;
                int grownSize = DeckSampler.BuildPlan(GrownPool(), new System.Random(seed)).Deck.Count;
                Assert.That(grownSize, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                    $"seed {seed}: колода из ~243 остаётся в коридоре забега ({grownSize})");
                Assert.LessOrEqual(grownSize, liveSize + 8,
                    $"seed {seed}: втрое больший пул не удлиняет забег (было {liveSize}, стало {grownSize})");
            }
            Assert.Greater(grown.Count, live.Count * 2, "пул действительно вырос втрое");
        }

        [Test]
        public void Milestones_SurviveTheBiggerPool()
        {
            // «Вехи обязательны»: сколько бы филлеров ни дописали, канон-карточки жизни в забеге есть.
            string[] must = { "I02", "I03", "YA01", "YA03", "YA05", "MD01" };
            for (int seed = 0; seed < 10; seed++)
            {
                var ids = DeckSampler.BuildPlan(GrownPool(), new System.Random(seed))
                                     .Deck.Select(c => c.Id).ToHashSet();
                foreach (var id in must)
                    Assert.IsTrue(ids.Contains(id), $"seed {seed}: веха {id} на месте даже в колоде из 243");
            }
        }

        [Test]
        public void FillersSpreadAcrossWindows_NoPhaseIsStarved()
        {
            // «Филлеры по окнам»: разросшийся пул не должен утянуть весь забег в один возраст.
            for (int seed = 0; seed < 10; seed++)
            {
                var deck = DeckSampler.BuildPlan(GrownPool(), new System.Random(seed)).Deck;
                Assert.Greater(deck.Count(c => c.Age <= DeckSampler.ChildhoodMaxAge), 3, $"seed {seed}: детство");
                Assert.Greater(deck.Count(c => c.Age > DeckSampler.ChildhoodMaxAge
                                            && c.Age <= DeckSampler.YoungMaxAge), 8, $"seed {seed}: молодость");
                Assert.Greater(deck.Count(c => c.Age > DeckSampler.YoungMaxAge
                                            && c.Age <= DeckSampler.MidMaxAge), 2, $"seed {seed}: зрелость");
                Assert.Greater(deck.Count(c => c.Age > DeckSampler.MidMaxAge), 1, $"seed {seed}: старость");
            }
        }
    }
}
