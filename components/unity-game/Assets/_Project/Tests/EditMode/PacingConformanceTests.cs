using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// PACING GATE (founder playtest, 2026-07-23). The four young mechanics reveal back-to-back at their
    /// canon ages — деньги@18, отношения@20, энергия@25, здоровье@30 — each firing a tutorial.
    /// The fix keeps the ages but demands ≥8 ORDINARY (normal player-choice) cards between every
    /// consecutive reveal, so the player breathes between reveals.
    ///
    /// This test simulates a full young life from the REAL scenes.csv for several fixed seeds: it builds a
    /// <see cref="Game"/> from a seeded <see cref="DeckSampler"/> plan, answers deterministically (all НЕТ —
    /// the four opens are AGE-gated, not choice-gated, so they still fire, and НЕТ never triggers a ДА-only
    /// fatal), and drives injected time so the age crosses each reveal threshold. In draw order it records
    /// every card and the moment each mechanic first opens, then asserts ≥8 ordinary cards fell between each
    /// consecutive pair of opens (which also forbids two reveal cards drawn back-to-back).
    ///
    /// TEETH: this is RED on the OLD sampler targets (YoungTarget=3, MinDeck=25/MaxDeck=30, no young
    /// sub-windows) — only ~3 young normals were drawn for the WHOLE 18–29 stretch, so the gaps collapse to
    /// ~1. It goes GREEN only with the young sub-window buckets (youngEarly/youngMid/youngLate) filling each
    /// gap. Verified by reverting the DeckSampler targets → this test fails → restore.
    /// </summary>
    public class PacingConformanceTests
    {
        private const int RequiredGap = 8;   // ≥8 ordinary cards between consecutive mechanic reveals
        private static readonly int[] Seeds = { 1, 7, 13, 42, 101 };

        private static IReadOnlyList<Card> AllCards()
        {
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes.csv present");
            return CardLoader.ParseAll(asset.text);
        }

        // Ordinary = a normal player-choice card. Excludes TIMELINE / FORCED announcements (the reveal
        // banners YA03/YA05, вехи), the crisis blitz/impulse cards, and the LT08 system heal card. NOCONS
        // flavour cards (FA01/FA03/FA07/FC08…) ARE ordinary — they are still real choice cards on screen.
        private static bool IsOrdinary(Card c)
            => c != null
               && !c.IsTimeline && !c.IsForced && !c.IsBlitz && !c.IsInvert
               && c.Id != "LT08";

        // Records the four young-mechanic opens (in first-open order) with the ordinary-card count so far.
        private sealed class Pacing
        {
            public readonly List<(string Name, int OrdinaryDrawn, string RevealCardId)> Opens = new();
        }

        // Play a young life for one seed: age-sorted draws, all НЕТ, injected time. Stops the instant all
        // four opens have fired (the whole young window is measured — no need to enter the 45+ crisis).
        private static Pacing PlayYoungLife(int seed)
        {
            var all = AllCards();
            var g = new Game(() => DeckSampler.BuildPlan(all, new System.Random(seed)), coin: () => false);
            g.StartLife();

            var p = new Pacing();
            int ordinary = 0;
            bool prevMoney = false, prevRel = false, prevEnergy = false, prevHealth = false;

            void CheckOpens()
            {
                string id = g.CurrentCard?.Id;
                if (!prevMoney && g.MoneyOpen) { prevMoney = true; p.Opens.Add(("Money", ordinary, id)); }
                if (!prevRel && g.RelationshipsOpen) { prevRel = true; p.Opens.Add(("Relationships", ordinary, id)); }
                if (!prevEnergy && g.EnergyOpen) { prevEnergy = true; p.Opens.Add(("Energy", ordinary, id)); }
                if (!prevHealth && g.HealthDecaying) { prevHealth = true; p.Opens.Add(("Health", ordinary, id)); }
            }

            int guard = 0;
            while (g.State == GameState.Playing && g.CurrentCard != null && guard++ < 3000)
            {
                var card = g.CurrentCard;
                if (IsOrdinary(card)) ordinary++;

                // Advance injected time until age reaches this card's age so any threshold crossing (18/20/
                // 25/30) fires WHILE the card is up. Break early if age can't advance yet (AgeRunning off
                // until I03 resolves) so I02/I03 don't spin the guard.
                //
                // ⚠ ХОТЯ БЫ ОДИН ТИК НА КАЖДУЮ КАРТОЧКУ — обязательно (r3). В живой игре Update тикает
                // КАЖДЫЙ кадр, поэтому возрастной гейт проверяется и тогда, когда возраст уже догнан.
                // Прежний `while (g.Age < card.Age)` на такой карточке не тикал вовсе, и открытие,
                // ПРИДЕРЖАННОЕ до ответа на свою `OPEN:`-карточку (п.8), проваливалось до ближайшей
                // карточки СТАРШЕГО возраста — то есть тест мерил не игру, а собственный цикл.
                int safety = 0;
                float prevAge = g.Age;
                while (g.State == GameState.Playing && ReferenceEquals(g.CurrentCard, card) && safety++ < 400)
                {
                    g.Tick(0.1f);
                    CheckOpens();
                    if (g.Age >= card.Age) break;               // возраст догнан — карточка отработана
                    if (safety > 2 && g.Age == prevAge) break;  // age not advancing → stop ticking this card
                    prevAge = g.Age;
                }
                CheckOpens();

                if (prevMoney && prevRel && prevEnergy && prevHealth) break; // whole young window measured

                if (g.State == GameState.Playing && ReferenceEquals(g.CurrentCard, card))
                    g.HandleInput(GameInput.AnswerNo);
                CheckOpens();
            }

            return p;
        }

        [Test]
        public void MechanicReveals_AreSpacedBy_AtLeast8_OrdinaryCards()
        {
            foreach (int seed in Seeds)
            {
                var p = PlayYoungLife(seed);

                Assert.AreEqual(4, p.Opens.Count,
                    $"all four young mechanics open in a young life (seed {seed}); got: "
                    + string.Join(", ", p.Opens.Select(o => o.Name)));

                // Opens must arrive in canon age order: money → relationships → energy → health.
                CollectionAssert.AreEqual(
                    new[] { "Money", "Relationships", "Energy", "Health" },
                    p.Opens.Select(o => o.Name).ToArray(),
                    $"reveals open in canon order (seed {seed})");

                for (int i = 1; i < p.Opens.Count; i++)
                {
                    int gap = p.Opens[i].OrdinaryDrawn - p.Opens[i - 1].OrdinaryDrawn;
                    Assert.GreaterOrEqual(gap, RequiredGap,
                        $"seed {seed}: only {gap} ordinary cards between {p.Opens[i - 1].Name} "
                        + $"and {p.Opens[i].Name} reveals (need ≥{RequiredGap}) — mechanics stack");

                    // ≥8 ordinary between opens already forbids two reveal cards back-to-back; assert the
                    // reveal cards themselves differ as a direct guard on the founder's «nothing between» bug.
                    Assert.AreNotEqual(p.Opens[i - 1].RevealCardId, p.Opens[i].RevealCardId,
                        $"seed {seed}: {p.Opens[i - 1].Name}/{p.Opens[i].Name} revealed on the same card "
                        + "(back-to-back)");
                }
            }
        }

        [Test]
        public void EveryGap_MeetsThreshold_AcrossSeeds_WithHeadroomReport()
        {
            int worst = int.MaxValue;
            foreach (int seed in Seeds)
            {
                var p = PlayYoungLife(seed);
                Assert.AreEqual(4, p.Opens.Count, $"four opens (seed {seed})");
                for (int i = 1; i < p.Opens.Count; i++)
                    worst = System.Math.Min(worst, p.Opens[i].OrdinaryDrawn - p.Opens[i - 1].OrdinaryDrawn);
            }
            // The binding gap is энергия→здоровье (25–29 has exactly 8 tight FC cards). Documents the margin.
            Assert.GreaterOrEqual(worst, RequiredGap,
                $"the tightest inter-reveal gap across all seeds is {worst} ordinary cards (need ≥{RequiredGap})");
        }

        /// <summary>
        /// r3 (п.8) — ПОРЯДОК «КАРТОЧКА → ОТКРЫТИЕ ШКАЛЫ». Живой плейтест основательницы: «карточка
        /// „начать встречаться“ приходит ПОСЛЕ открытия шкалы отношений — нелогично».
        ///
        /// Причина была в МОМЕНТЕ, а не в колоде: возраст догоняет возраст текущей карточки сразу, как её
        /// выдали, поэтому гейт 20 щёлкал, пока `YA03` («ПЕРВАЯ ЛЮБОВЬ! Начать встречаться?») ещё висела
        /// НЕОТВЕЧЕННОЙ, и туториал шкалы вставал поверх собственного вопроса. Канон-возрасты не тронуты —
        /// открытие ПРИДЕРЖИВАЕТСЯ, пока текущая карточка сама несёт флаг `OPEN:{шкала}`.
        ///
        /// Проверяем на РЕАЛЬНОЙ колоде и всех тех же сидах: в момент открытия шкалы её `OPEN:`-карточка
        /// уже ОТВЕЧЕНА (её нет на экране). Зубы: снимите придержку в Game.Check*Open — краснеет.
        /// </summary>
        [Test]
        public void EveryOpenCard_IsAnswered_BeforeItsScaleOpens()
        {
            foreach (int seed in Seeds)
            {
                var all = AllCards();
                var g = new Game(() => DeckSampler.BuildPlan(all, new System.Random(seed)), coin: () => false);
                g.StartLife();

                bool money = false, rel = false, energy = false;
                var offenders = new List<string>();

                void Check()
                {
                    var c = g.CurrentCard;
                    if (c == null) return;
                    if (!money && g.MoneyOpen)
                    {
                        money = true;
                        if (c.Opens(Card.OpenMoney)) offenders.Add($"Дн открылись НА {c.Id}");
                    }
                    if (!rel && g.RelationshipsOpen)
                    {
                        rel = true;
                        if (c.Opens(Card.OpenRelations)) offenders.Add($"Отн открылись НА {c.Id}");
                    }
                    if (!energy && g.EnergyOpen)
                    {
                        energy = true;
                        if (c.Opens(Card.OpenEnergy)) offenders.Add($"Эн открылись НА {c.Id}");
                    }
                }

                int guard = 0;
                while (g.State == GameState.Playing && g.CurrentCard != null && guard++ < 3000)
                {
                    var card = g.CurrentCard;
                    int safety = 0;
                    float prevAge = g.Age;
                    while (g.State == GameState.Playing && ReferenceEquals(g.CurrentCard, card) && safety++ < 400)
                    {
                        g.Tick(0.1f);
                        Check();
                        if (g.Age >= card.Age) break;
                        if (safety > 2 && g.Age == prevAge) break;
                        prevAge = g.Age;
                    }
                    Check();
                    if (money && rel && energy) break;
                    if (g.State == GameState.Playing && ReferenceEquals(g.CurrentCard, card))
                        g.HandleInput(GameInput.AnswerNo);
                    Check();
                }

                Assert.IsTrue(money && rel && energy, $"seed {seed}: все три шкалы открылись за молодость");
                CollectionAssert.IsEmpty(offenders,
                    $"seed {seed}: шкала открылась ПОВЕРХ своей же неотвеченной карточки — "
                    + string.Join(", ", offenders));
            }
        }

        /// <summary>
        /// Прямая проверка канона колоды, без симуляции: карточка «начать встречаться» (`OPEN:Отн`) —
        /// САМАЯ РАННЯЯ среди карточек своего возраста, т.е. игрок встречает её первой из двадцатилетних.
        /// Это вторая половина порядка: придержка в Game спасает от «шкала поверх вопроса», а вот это —
        /// от «между вопросом и шкалой вклинилась чужая карточка того же возраста».
        /// </summary>
        [Test]
        public void TheDatingCard_IsTheFirstCardOfItsAge()
        {
            foreach (int seed in Seeds)
            {
                var deck = DeckSampler.BuildPlan(AllCards(), new System.Random(seed)).Deck;
                var open = deck.FirstOrDefault(c => c.Opens(Card.OpenRelations));
                Assert.IsNotNull(open, $"seed {seed}: карточка OPEN:Отн есть в колоде");

                int idx = deck.IndexOf(open);
                for (int i = 0; i < idx; i++)
                    Assert.Less(deck[i].Age, open.Age,
                        $"seed {seed}: {deck[i].Id} (возраст {deck[i].Age}) вклинилась ПЕРЕД "
                        + $"{open.Id} (возраст {open.Age}) — «встречаться» обязана быть первой в своём возрасте");
            }
        }
    }
}
