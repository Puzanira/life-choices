using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// КОЛОДА ВЫРОСЛА ВТРОЕ, ЗАБЕГ — НЕТ. Сценарная сессия дописала отрезки 1–7: 85 → ~253 строки
    /// (детство 36 · юность 35 · молодость 40 + 48 · 30–39 30 · 40–55 27 · 56–100 27, плюс кризис-блок
    /// и интро, которые дизайн-док в свои «~243» не считает). Показываться при этом должно столько же —
    /// «вехи обязательны, филлеры по окнам».
    ///
    /// С отрезком 0 этот файл МОДЕЛИРОВАЛ рост синтетическими филлерами, потому что живая колода ещё
    /// стояла на 85 строках. Теперь рост НАСТОЯЩИЙ, и проверяется он на живом CSV. Синтетика осталась,
    /// но переехала в другую роль: она добивает пул ЕЩЁ вдвое (~253 → ~415) и доказывает, что инвариант
    /// структурный — свойство сэмплера, а не удачное совпадение на текущем размере.
    ///
    /// ⚠ ЧИСЛО «25–30». Дизайн-док и хендофф говорят «за забег по-прежнему ~25–30 карточек». Это число
    /// ПРОТУХЛО: пейсинг-фикс основательницы (2026-07-23) требует ≥8 обычных карточек между соседними
    /// открытиями механик (деньги@18 → отношения@20 → энергия@25 → здоровье@30), а три таких зазора плюс
    /// детство и старость арифметически не помещаются в тридцать карточек. Тогда же коридор и был поднят
    /// до <see cref="DeckSampler.MinDeck"/>…<see cref="DeckSampler.MaxDeck"/>. Инвариант, который
    /// действительно имели в виду, — «длина забега не растёт вместе с пулом», и проверяется именно он.
    /// Возврат к 25–30 ценой пейсинг-правила — решение основательницы, здесь оно не принимается.
    /// </summary>
    public class DeckScaleTests
    {
        /// <summary>Живая колода — теперь это и есть дописанные отрезки 1–7.</summary>
        private static List<Card> LivePool()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            return CardLoader.ParseAll(csv.text);
        }

        // Ещё вдвое сверх живой колоды — запас, на котором видно, что инвариант структурный.
        private static readonly (string When, int Count)[] HeadroomPlan =
        {
            ("4–17", 25), ("18–19", 25), ("20–24", 25), ("25–29", 25),
            ("30–39", 25), ("40–55", 20), ("56–100", 20),
        };

        private static List<Card> HeadroomPool()
        {
            var pool = LivePool();
            int order = pool.Count;
            foreach (var (when, count) in HeadroomPlan)
                for (int i = 0; i < count; i++)
                {
                    string id = "GROW_" + when.Replace("–", "_") + "_" + i;
                    pool.Add(new Card
                    {
                        Id = id, Question = id + "?", When = when,
                        Age = DeckSampler.AgeWindow.Parse(when).Min, Order = order++,
                        YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
                        YesNecrolog = "y" + id, NoNecrolog = "n" + id,
                        Flags = new List<string>(),
                    });
                }
            return pool;
        }

        [Test]
        public void LivePool_IsTheFullAuthoredDeck_AboutTwoHundredFifty()
        {
            // Отрезки 1–7 ПОСАЖЕНЫ: это уже не модель, а живой файл. Границы широкие намеренно —
            // тест сторожит «колода целиком на месте», а не точное число строк.
            Assert.That(LivePool().Count, Is.InRange(240, 265),
                "живая колода — дописанные отрезки 1–7 целиком");
        }

        [Test]
        public void EveryLifePhase_HasARealPool_NotJustMilestones()
        {
            // Перекос, ради которого сессия и затевалась: раньше на 12 лет молодости приходилось 46
            // карточек, а на 70 лет остальной жизни — 20. Теперь у каждой фазы свой живой пул.
            // Считаем по НАЧАЛУ окна: «70+» и «65–90» — обе карточки поздней жизни, но по верхней границе
            // они попадают в разные корзины, а по нижней — в одну, ту самую, про которую говорит дизайн-док.
            var pool = LivePool();
            int StartsIn(int lo, int hi) => pool.Count(c =>
            {
                var w = DeckSampler.AgeWindow.Parse(c.When);
                return !w.IsMarriageOffset && w.Min >= lo && w.Min <= hi;
            });
            Assert.Greater(StartsIn(0, 17), 30, "детство");
            Assert.Greater(StartsIn(18, 19), 30, "юность 18–19");
            Assert.Greater(StartsIn(20, 24), 30, "молодость 20–24");
            Assert.Greater(StartsIn(25, 29), 35, "молодость 25–29");
            Assert.Greater(StartsIn(30, 39), 20, "взрослость 30–39");
            Assert.Greater(StartsIn(40, 55), 20, "кризис и зрелость 40–55");
            Assert.Greater(StartsIn(56, 100), 20, "поздняя жизнь 56–100 — была самая большая дыра в игре");
        }

        [Test]
        public void RunLength_DoesNotGrowWithThePool()
        {
            // ГЛАВНЫЙ ИНВАРИАНТ: пул вдвое больше живого не удлиняет забег. Сравниваем не с литералом, а
            // с самим сэмплером на живой колоде — если однажды коридор пересмотрят, тест не соврёт.
            var live = LivePool();
            var grown = HeadroomPool();

            for (int seed = 0; seed < 20; seed++)
            {
                int liveSize = DeckSampler.BuildPlan(LivePool(), new System.Random(seed)).Deck.Count;
                int grownSize = DeckSampler.BuildPlan(HeadroomPool(), new System.Random(seed)).Deck.Count;
                Assert.That(liveSize, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                    $"seed {seed}: живая колода из ~253 держит коридор забега ({liveSize})");
                Assert.That(grownSize, Is.InRange(DeckSampler.MinDeck, DeckSampler.MaxDeck),
                    $"seed {seed}: и вдвое больший пул тоже ({grownSize})");
                Assert.LessOrEqual(grownSize, liveSize + 8,
                    $"seed {seed}: больший пул не удлиняет забег (было {liveSize}, стало {grownSize})");
            }
            Assert.Greater(grown.Count, live.Count + 150, "пул для проверки запаса действительно больше");
        }

        [Test]
        public void Milestones_SurviveTheBiggerPool()
        {
            // «Вехи обязательны»: сколько бы филлеров ни дописали, канон-карточки жизни в забеге есть.
            string[] must = { "I02", "I03", "YA01", "YA03", "YA05", "MD01" };
            for (int seed = 0; seed < 10; seed++)
            {
                var ids = DeckSampler.BuildPlan(HeadroomPool(), new System.Random(seed))
                                     .Deck.Select(c => c.Id).ToHashSet();
                foreach (var id in must)
                    Assert.IsTrue(ids.Contains(id), $"seed {seed}: веха {id} на месте даже в колоде из 400+");
            }
        }

        [Test]
        public void FillersSpreadAcrossWindows_NoPhaseIsStarved()
        {
            // «Филлеры по окнам»: разросшийся пул не должен утянуть весь забег в один возраст.
            foreach (var pool in new[] { LivePool(), HeadroomPool() })
                for (int seed = 0; seed < 10; seed++)
                {
                    var deck = DeckSampler.BuildPlan(pool, new System.Random(seed)).Deck;
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
