using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// НОВЫЕ МЕХАНИКИ ОТРЕЗКА 0, поведенчески: правило долга (в минус уводят только кредитные), метки
    /// `BREAK:Отн` / `DRIFT:Отн=xN` / `EXCL:*` / `PRENUP` и пул реплик Ведущего `debt`.
    /// Каждая проверка написана так, чтобы РУХНУТЬ при снятии соответствующего куска реализации
    /// (mutation-proof прогон, done contract §7): проверяется наблюдаемое поведение, а не наличие поля.
    /// </summary>
    public class GameScoringTests
    {
        private const double Eps = 1e-6;

        private static Card Plain(string id, int age) => new Card
        {
            Id = id, Question = id + "?", Age = age, Order = age,
            YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(),
            Flags = new List<string>(),
        };

        private static Card Starter()
        {
            var c = Plain("I03", 1);
            c.StartsAgeTimer = true;
            return c;
        }

        private static Card Flags(Card c, params string[] flags)
        {
            var list = new List<string>(c.Flags);
            list.AddRange(flags);
            c.Flags = list;
            if (list.Contains("BLOCK$")) c.IsBlockCost = true;
            c.BreaksRelationships = list.Contains("BREAK:" + Card.OpenRelations);
            var excl = list.Find(f => f.StartsWith("EXCL:"));
            c.ExclusiveGroup = excl?.Substring("EXCL:".Length);
            c.IsPrenup = list.Contains("PRENUP");
            return c;
        }

        private static Game NewGame(params Card[] cards)
        {
            var deck = new List<Card> { Starter() };
            deck.AddRange(cards);
            return new Game(deck, coin: () => false);
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);

        // Открыть деньги и накрутить примерно столько ₽ (крутилка = +1 ₽ за тик).
        private static Game OpenMoney(Game g, int bank)
        {
            g.StartLife();
            No(g);                                   // I03 → возраст пошёл
            g.Tick(2f);                              // догнать 18 → деньги открылись
            Assert.IsTrue(g.MoneyOpen, "деньги открылись");
            for (int i = 0; i < bank; i++) g.HandleInput(GameInput.MoneyTick);
            return g;
        }

        // ================================================================ правило долга

        [Test]
        public void NonCreditCard_CanNeverPushTheAccountBelowZero()
        {
            // «А то, на что у нас не хватает денег, так и не должно быть доступно» (основательница,
            // 2026-08-07). Карточка НЕ из списка кредитных не может увести счёт в минус ни ценой, ни Δ.
            var spender = Plain("SPEND", 20);
            spender.YesDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, -3) };  // −100 ₽
            var g = OpenMoney(NewGame(Plain("OPEN", 18), spender, Plain("N", 40)), bank: 10);

            No(g);                                   // OPEN → SPEND
            Assert.AreEqual("SPEND", g.CurrentCard.Id);
            Yes(g);
            Assert.GreaterOrEqual(g.Money, 0.0, "не кредитная карточка в минус не уводит");
        }

        [Test]
        public void CreditCard_DoesGoNegative_ThatIsThePoint()
        {
            // …а кредитная — уводит, и это честная механика, а не покупка без денег.
            // (Берём FC14 «первая машина в кредит»: у YA04 своя точная сумма (+40 ₽), она перебила бы Δ.)
            Assert.Contains("FC14", Game.CreditCards.ToList(), "FC14 — кредитная по канону §3.5");
            var loan = Plain("FC14", 20);
            loan.YesDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, -3) };
            var g = OpenMoney(NewGame(Plain("OPEN", 18), loan, Plain("N", 40)), bank: 10);

            No(g);
            Yes(g);
            Assert.Less(g.Money, 0.0, "кредит — единственный законный путь в минус");
        }

        [Test]
        public void EveryPricedCard_IsEitherBlockCostOrCredit()
        {
            // Структурная проверка канона §3.1 прямо по живой колоде: у карточки с ценой стоит `BLOCK$`,
            // а если не стоит — она обязана быть кредитной. Иначе где-то завёлся тихий путь в минус.
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv);
            var byId = CardLoader.ParseAll(csv.text).ToDictionary(c => c.Id);
            foreach (var id in Game.BlockPrices.Keys)
            {
                if (!byId.TryGetValue(id, out var card)) continue;   // LT08 и прочие системные
                Assert.IsTrue(card.IsBlockCost || Game.CreditCards.Contains(id),
                    id + ": платная карточка обязана нести BLOCK$ (или быть кредитной)");
            }
        }

        [Test]
        public void PricedCard_IsNeverNocons()
        {
            // NOCONS означает «ничего не применяем» — и ровно поэтому айфон (FC08) списывал ноль при
            // любой цене. Платная карточка НЕ МОЖЕТ быть NOCONS, иначе цена снова станет декорацией.
            var csv = Resources.Load<TextAsset>("scenes");
            var byId = CardLoader.ParseAll(csv.text).ToDictionary(c => c.Id);
            foreach (var id in Game.BlockPrices.Keys.Concat(Game.CardYesIncome.Keys))
                if (byId.TryGetValue(id, out var card))
                    Assert.IsFalse(card.IsNoCons, id + ": у карточки с точной суммой NOCONS быть не может");
        }

        [Test]
        public void EarningCard_PaysItsExactAmount_NotTheCsvDelta()
        {
            // «Смена курьером в дождь» FA06: +20 ₽ по таблице §3.4, а не +50 ₽ (что дал бы общий маппинг
            // «Дн +2») и не +2 ₽ (что давала буквальная Δ). Юность лайтовая — это её правда.
            var earn = Plain("FA06", 19);
            earn.YesDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, 2) };
            var g = OpenMoney(NewGame(Plain("OPEN", 18), earn, Plain("N", 40)), bank: 5);

            No(g);
            double before = g.Money;
            Yes(g);
            Assert.AreEqual(before + Game.CardYesIncome["FA06"], g.Money, Eps,
                "заработок платит ровно свою сумму (+20 ₽)");
            Assert.AreEqual(20.0, Game.CardYesIncome["FA06"]);
        }

        [Test]
        public void DebtPool_SoundsOnce_WhenTheAccountCrossesZero()
        {
            // §3.2: «долг должен звучать, а не просто краснеть». Событие — на ПЕРЕСЕЧЕНИИ нуля, а не
            // каждый кадр в минусе, иначе Ведущий тараторил бы одно и то же всю старость.
            int fired = 0;
            var loan = Plain("FC14", 20);
            loan.YesDeltas = new[] { new ScaleDelta(Scale.Money, DeltaKind.Add, -3) };
            var g = NewGame(Plain("OPEN", 18), loan, Plain("N", 40));
            g.DebtEntered += () => fired++;
            OpenMoney(g, bank: 10);

            No(g);
            Assert.AreEqual(0, fired, "пока счёт в плюсе, про долг молчим");
            Yes(g);                                  // −100 ₽ → минус
            Assert.AreEqual(1, fired, "уход в минус объявлен ровно один раз");
            for (int i = 0; i < 20; i++) g.Tick(0.1f);
            Assert.AreEqual(1, fired, "…и не повторяется, пока сидим в долгу");
            Assert.AreEqual(6, HostContent.Pool[HostTone.Debt].Length, "пул `debt` — шесть реплик (§3.2)");
        }

        // ================================================================ BREAK:Отн

        // Колода, доводящая отношения до открытия (возраст 20), с испытуемой карточкой следом.
        private static Game WithOpenRelationships(Card probe, params Card[] tail)
        {
            var deck = new List<Card> { Starter(), Plain("OPEN18", 18), Plain("OPEN20", 20), probe };
            deck.AddRange(tail.Length > 0 ? tail : new[] { Plain("N", 70) });
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            No(g);                                   // I03
            g.Tick(2f); No(g);                       // 18
            g.Tick(2f);                              // 20 → отношения открылись
            Assert.IsTrue(g.RelationshipsOpen, "отношения открыты");
            No(g);                                   // OPEN20 → probe
            return g;
        }

        [Test]
        public void BreakFlag_EndsTheRelationshipImmediately_NoDeathNoAutoLine()
        {
            // CR06 «БРОСИТЬ ПАРТНЁРА ПРЯМО СЕЙЧАС! Немедленный развод»: раньше это была Δ «Отн −3»,
            // то есть 55 → 52 и всё ещё зелёная зона. Теперь партнёр уходит.
            var cr06 = Flags(Plain("CR06", 21), "BREAK:" + Card.OpenRelations);
            cr06.YesNecrolog = "Партнёра бросили одним вечером.";
            var g = WithOpenRelationships(cr06);

            bool broke = false;
            g.RelationshipBrokeUp += () => broke = true;
            Yes(g);

            Assert.IsTrue(broke, "событие разрыва прозвучало");
            Assert.IsTrue(g.RelationshipsLost);
            Assert.IsFalse(g.RelationshipsOpen, "шкала гаснет — джойстик больше ни на что не влияет");
            Assert.AreEqual(Game.RelBreakupValue, g.Scales.Relationships);
            Assert.AreEqual(GameState.Playing, g.State, "разрыв — не смерть");

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 60) No(g);
            CollectionAssert.Contains(g.Necrolog.StoryLines, "Партнёра бросили одним вечером.",
                "в некрологе строка САМОЙ карточки…");
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "Отношения не удержали — расстались.",
                "…и не служебная строка дрейфового разрыва вдобавок — иначе разрыв прозвучит дважды");
        }

        [Test]
        public void BreakFlag_OnНЕТ_DoesNothing()
        {
            var cr06 = Flags(Plain("CR06", 21), "BREAK:" + Card.OpenRelations);
            var g = WithOpenRelationships(cr06);
            No(g);
            Assert.IsFalse(g.RelationshipsLost, "отказ партнёра не уводит");
            Assert.IsTrue(g.RelationshipsOpen);
        }

        [Test]
        public void BreakFlag_WithDelay_RipsAfterTheStatedYears()
        {
            // RND05 «Роман на стороне?» — проза обещает развод ЧЕРЕЗ ДВА ГОДА, и теперь так и есть.
            var rnd05 = Flags(Plain("RND05", 21), "BREAK:" + Card.OpenRelations, "DELAY(2)");
            rnd05.BreakDelayYears = 2;
            var g = WithOpenRelationships(rnd05, Plain("LATER", 22), Plain("MUCH_LATER", 40));

            Yes(g);
            Assert.IsFalse(g.RelationshipsLost, "в момент ответа партнёр ещё здесь");
            Assert.IsTrue(g.RelationshipsOpen);
            g.Tick(0.2f);                            // возраст дошёл до 22 — срок ещё не вышел
            Assert.IsFalse(g.RelationshipsLost, "год спустя партнёр всё ещё здесь");

            No(g);                                   // следующая карточка стоит дальше по жизни
            g.Tick(2f);                              // …возраст перешагнул 23
            Assert.GreaterOrEqual(g.Age, 23f);
            Assert.IsTrue(g.RelationshipsLost, "через два года — развод, как обещала проза");
        }

        // ================================================================ DRIFT:Отн=xN

        // NB: id намеренно НЕ «MD01» — у той карточки есть свой спец-обработчик (свадьба ставит Married и
        // сама смягчает дрейф вдвое), и он замаскировал бы работу самого множителя.
        private static Card Drift(string id, int age, double mult, int durYears = 0, bool onNo = false)
        {
            var c = Plain(id, age);
            c.LongEffects = new[]
            {
                new LongEffect
                {
                    Kind = LongEffectKind.Drift, Scale = Scale.Relationships,
                    MultValue = mult, DurYears = durYears, OnNoSide = onNo,
                }
            };
            return c;
        }

        [Test]
        public void DriftMultiplier_SpeedsUpTheDrift_AndLiftsWhenTheEffectExpires()
        {
            var card = Drift("DRIFT_CARD", 21, mult: 2.0, durYears: 3);
            var g = WithOpenRelationships(card, Plain("LATER", 30), Plain("N", 70));
            double baseline = g.RelationshipDriftPerSec;

            Yes(g);
            Assert.AreEqual(baseline * 2.0, g.RelationshipDriftPerSec, Eps,
                "«DRIFT:Отн=x2» удваивает дрейф — отношения тают даже при поддержке");

            // …и СНИМАЕТСЯ вместе с эффектом: срок 3 года, следующая карточка стоит на 30.
            No(g);
            g.Tick(2f);
            Assert.Greater(g.Age, 24f, "срок эффекта вышел");
            Assert.AreEqual(baseline, g.RelationshipDriftPerSec, Eps, "множитель снялся вместе с эффектом");
        }

        [Test]
        public void DriftMultiplier_OnTheНЕТSide_AppliesOnlyOnНЕТ()
        {
            // `MD01`-НЕТ («отказ от свадьбы») — единственный случай, где длительный эффект висит на
            // стороне НЕТ. Формат — префикс стороны в колонке «Длительный эффект».
            var yesGame = WithOpenRelationships(Drift("DRIFT_CARD", 21, 2.0, onNo: true));
            double baseline = yesGame.RelationshipDriftPerSec;
            Yes(yesGame);
            Assert.AreEqual(baseline, yesGame.RelationshipDriftPerSec, Eps, "на ДА запись НЕТ не срабатывает");

            var noGame = WithOpenRelationships(Drift("DRIFT_CARD", 21, 2.0, onNo: true));
            No(noGame);
            Assert.AreEqual(baseline * 2.0, noGame.RelationshipDriftPerSec, Eps, "…а на НЕТ срабатывает");
        }

        [Test]
        public void RealCsv_Md01_CarriesTheНЕТDriftOnly()
        {
            // ДА-сторона ×0.5 живёт механикой брака (Game.RelDriftMarriedPerSec) ещё до отрезка 0 —
            // в CSV дублировать её нельзя, иначе свадьба дала бы ×0.25.
            var csv = Resources.Load<TextAsset>("scenes");
            var md01 = CardLoader.ParseAll(csv.text).Find(c => c.Id == "MD01");
            var drift = md01.LongEffects.Single(e => e.Kind == LongEffectKind.Drift);
            Assert.IsTrue(drift.OnNoSide, "запись стоит на стороне НЕТ");
            Assert.AreEqual(2.0, drift.MultValue, Eps);
            Assert.AreEqual(Game.RelDriftPerSec / 2.0, Game.RelDriftMarriedPerSec, Eps,
                "…а ДА-сторона (×0.5) остаётся механикой брака, не строкой CSV");
        }

        // ================================================================ EXCL:*

        [Test]
        public void ExclusiveBranch_HidesTheSecondCardOfTheGroup()
        {
            // Две ипотеки — одно и то же событие в двух отрезках. Взял раннюю — поздняя не приходит.
            var early = Flags(Plain("FC02", 26), "EXCL:ипотека");
            var late = Flags(Plain("MD04", 35), "EXCL:ипотека");
            var g = NewGame(Plain("OPEN", 18), early, late, Plain("N", 60));
            g.StartLife(); No(g); g.Tick(2f); No(g);

            Assert.AreEqual("FC02", g.CurrentCard.Id);
            Yes(g);                                  // ипотека взята
            Assert.AreEqual("N", g.CurrentCard.Id, "вторая ипотека в этом забеге не появляется");
        }

        [Test]
        public void ExclusiveBranch_StaysOpen_WhenTheFirstCardIsDeclined()
        {
            var early = Flags(Plain("FC02", 26), "EXCL:ипотека");
            var late = Flags(Plain("MD04", 35), "EXCL:ипотека");
            var g = NewGame(Plain("OPEN", 18), early, late, Plain("N", 60));
            g.StartLife(); No(g); g.Tick(2f); No(g);

            No(g);                                   // от ранней отказались
            Assert.AreEqual("MD04", g.CurrentCard.Id, "поздняя ипотека остаётся доступной");
        }

        [Test]
        public void RealCsv_BothMortgages_ShareOneExclusiveKey()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            var byId = CardLoader.ParseAll(csv.text).ToDictionary(c => c.Id);
            Assert.AreEqual("ипотека", byId["FC02"].ExclusiveGroup);
            Assert.AreEqual("ипотека", byId["MD04"].ExclusiveGroup);
            Assert.AreEqual("реб", byId["MD02"].ExclusiveGroup, "ветка «детей не будет» заведена");
            Assert.AreEqual(25, byId["FC02"].LongEffects.Single().DurYears,
                "ранняя ипотека платится 25 лет — влез рано, зато свои стены с молодости");
            Assert.AreEqual(20, byId["MD04"].LongEffects.Single().DurYears,
                "поздняя — 20 лет: накопил, влез поздно, отдашь быстрее");
        }

        // ================================================================ PRENUP

        // Довести до БРАКА (MD01=ДА при открытых отношениях), затем порвать карточкой.
        private static Game MarriedThenBreak(bool prenup)
        {
            var deck = new List<Card> { Starter(), Plain("OPEN18", 18), Plain("OPEN20", 20) };
            if (prenup) deck.Add(Flags(Plain("PRE", 21), "PRENUP"));
            deck.Add(Plain("MD01", 22));
            deck.Add(Flags(Plain("CR06", 23), "BREAK:" + Card.OpenRelations));
            deck.Add(Plain("N", 70));

            var g = new Game(deck, coin: () => false);
            g.StartLife();
            No(g);
            g.Tick(2f);
            for (int i = 0; i < 200; i++) g.HandleInput(GameInput.MoneyTick);   // накрутить на развод
            No(g);
            g.Tick(2f);
            Assert.IsTrue(g.RelationshipsOpen);
            No(g);                                   // OPEN20 → PRE или MD01
            if (prenup) { Yes(g); }                  // подписали брачный договор
            Assert.AreEqual("MD01", g.CurrentCard.Id);
            Yes(g);                                  // СВАДЬБА
            Assert.IsTrue(g.Married);
            Assert.AreEqual("CR06", g.CurrentCard.Id);
            return g;
        }

        [Test]
        public void Divorce_CostsSixtyRubles_UnlessPrenupWasSigned()
        {
            var plain = MarriedThenBreak(prenup: false);
            double before = plain.Money;
            Yes(plain);
            Assert.AreEqual(before - Game.DivorceCost, plain.Money, 0.001,
                "развод без договора стоит " + Game.DivorceCost + " ₽");

            var protectedRun = MarriedThenBreak(prenup: true);
            double beforePrenup = protectedRun.Money;
            Yes(protectedRun);
            Assert.AreEqual(beforePrenup, protectedRun.Money, 0.001,
                "брачный договор гасит штраф развода целиком");
            Assert.IsTrue(protectedRun.RelationshipsLost, "…но партнёр всё равно уходит — договор про деньги");
        }

        // ============================================ ревью: гарды «только по открытой шкале» + перепроверка цены

        [Test]
        public void DriftMultiplier_IsDropped_WhenTheScaleIsStillClosed()
        {
            // Находка ревью (MAJOR): `DRIFT:Отн=xN` — это Δ, растянутая во времени, и правило «Δ только по
            // ОТКРЫТЫМ шкалам» на неё распространяется. Карточка с дрейфом до двадцати не имеет права
            // «взвестись на потом»: балансира на экране не было, выбор про него ничего не говорил.
            var early = Drift("DRIFT_EARLY", 18, mult: 2.0);
            var deck = new List<Card>
            {
                Starter(), Plain("OPEN18", 18), early, Plain("LATER", 21), Plain("N", 70),
            };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            No(g);                                   // I03 → возраст пошёл
            g.Tick(2f);                              // догнать 18
            No(g);                                   // OPEN18 → DRIFT_EARLY
            Assert.AreEqual("DRIFT_EARLY", g.CurrentCard.Id);
            Assert.IsFalse(g.RelationshipsOpen, "балансир отношений открывается только в 20");

            Yes(g);                                  // дрейф ×2 по ЗАКРЫТОЙ шкале
            g.Tick(2f);                              // …догнать 20 → отношения открылись
            Assert.IsTrue(g.RelationshipsOpen, "балансир открылся штатно");
            Assert.AreEqual(Game.RelDriftPerSec, g.RelationshipDriftPerSec, Eps,
                "множитель по закрытой шкале НЕ принят — дрейф остался базовым");
        }

        [Test]
        public void DriftMultiplier_IsAccepted_WhenTheCardItselfOpensTheScale()
        {
            // Контроль к предыдущему: гард не должен съесть законный случай — карточка, которая САМА
            // открывает шкалу (`OPEN:Отн`), работает по уже открытой (та же поправка, что и у обычной Δ).
            var opener = Flags(Drift("YA03", 18, mult: 2.0), "OPEN:" + Card.OpenRelations);
            var deck = new List<Card> { Starter(), Plain("OPEN18", 18), opener, Plain("N", 70) };
            var g = new Game(deck, coin: () => false);
            g.StartLife();
            No(g);
            g.Tick(2f);
            No(g);
            Assert.AreEqual("YA03", g.CurrentCard.Id);

            Yes(g);
            g.Tick(2f);
            Assert.AreEqual(Game.RelDriftPerSec * 2.0, g.RelationshipDriftPerSec, Eps,
                "своя же шкала считается открытой — множитель принят");
        }

        [Test]
        public void BreakFlag_IsIgnored_WhileTheRelationshipScaleIsStillClosed()
        {
            // Находка ревью (MAJOR): отложенный `BREAK:Отн` этот гард нёс всегда, а немедленный — нет.
            // Карточка разрыва до двадцати переворачивала RelationshipsLost и тем самым УБИВАЛА механику
            // отношений на весь забег: балансир после этого не открывался уже никогда.
            var cr06 = Flags(Plain("CR06", 18), "BREAK:" + Card.OpenRelations);
            var deck = new List<Card> { Starter(), Plain("OPEN18", 18), cr06, Plain("LATER", 21), Plain("N", 70) };
            var g = new Game(deck, coin: () => false);
            bool broke = false;
            g.RelationshipBrokeUp += () => broke = true;
            g.StartLife();
            No(g);
            g.Tick(2f);
            No(g);                                   // OPEN18 → CR06
            Assert.AreEqual("CR06", g.CurrentCard.Id);
            Assert.IsFalse(g.RelationshipsOpen, "партнёра ещё нет — рвать нечего");
            int before = g.Scales.Relationships;

            Yes(g);
            Assert.IsFalse(broke, "события разрыва не было");
            Assert.IsFalse(g.RelationshipsLost, "шкала не переворачивается в «потеряно» до своего открытия");
            Assert.AreEqual(before, g.Scales.Relationships, "и не падает в RelBreakupValue");

            g.Tick(2f);
            Assert.IsTrue(g.RelationshipsOpen,
                "…а в 20 балансир открывается как ни в чём не бывало — механика не потеряна");
        }

        [Test]
        public void SecondBreakCard_IsIgnored_WhenThePartnerIsAlreadyGone()
        {
            // Тот же гард со второй стороны: разрыв по УЖЕ потерянной шкале объявлялся повторно —
            // Ведущий комментировал уход партнёра, которого давно нет.
            var first = Flags(Plain("CR06", 21), "BREAK:" + Card.OpenRelations);
            var second = Flags(Plain("RND05", 22), "BREAK:" + Card.OpenRelations);
            var g = WithOpenRelationships(first, second, Plain("N", 70));

            Yes(g);                                  // партнёр ушёл
            Assert.IsTrue(g.RelationshipsLost);
            int after = g.Scales.Relationships;

            int fired = 0;
            g.RelationshipBrokeUp += () => fired++;
            Assert.AreEqual("RND05", g.CurrentCard.Id);
            Yes(g);
            Assert.AreEqual(0, fired, "второй разрыв не объявляется — рвать больше нечего");
            Assert.AreEqual(after, g.Scales.Relationships, "и шкала не трогается второй раз");
        }

        // Деньги открыты, испытуемая платная карточка стоит следом за OPEN18.
        private static Game MoneyThenPriced(Card priced)
        {
            var g = NewGame(Plain("OPEN18", 18), priced, Plain("N", 19));
            g.StartLife();
            No(g);                                   // I03 → возраст пошёл
            g.Tick(2f);                              // догнать 18 → деньги открылись
            Assert.IsTrue(g.MoneyOpen, "деньги открылись");
            return g;
        }

        // Накрутить ЧУТЬ выше цены и уронить счёт стоимостью жизни ровно под неё, пока карточка на экране.
        private static void DrainJustBelowPrice(Game g, double price)
        {
            float dt = (float)((g.Money - price + 0.1) / Game.CostOfLivingPerSec);
            Assert.Less(dt, Game.AnswerSecondsYouth, "дренаж укладывается в таймер ответа — карточка не уйдёт сама");
            g.Tick(dt);
            Assert.Less(g.Money, price, "к моменту ответа денег меньше цены");
            Assert.AreEqual(GameState.Playing, g.State);
        }

        [Test]
        public void BlockCost_IsRecheckedOnAnswer_NotFixedAtDrawTimeOnly()
        {
            // Находка ревью (MAJOR): `BLOCK$` считался ОДИН раз, на показе. Стоимость жизни капает всё
            // время, пока игрок думает, — карточка, показанная по карману, к ответу становилась не по
            // карману, а ДА всё равно применял последствия покупки. Зажим в AddCardMoney списывал остаток,
            // то есть «автошкола за 24.9 ₽ вместо 25» — оплата неполная, покупка состоялась.
            double price = Game.BlockPrices["FA05"];              // автошкола за компанию, 25 ₽
            var card = Flags(Plain("FA05", 19), "BLOCK$");
            card.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -1) };   // −7 п.п.
            card.YesNecrolog = "Пошли в автошколу за компанию.";
            var g = MoneyThenPriced(card);

            while (g.Money < price + 0.5) g.HandleInput(GameInput.MoneyTick);
            No(g);                                   // OPEN18 → FA05
            Assert.AreEqual("FA05", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked, "на ПОКАЗЕ денег хватало — карточка пришла живой");

            int health = g.Scales.Health;
            DrainJustBelowPrice(g, price);
            Assert.AreEqual("FA05", g.CurrentCard.Id, "карточка всё ещё на экране");
            Assert.IsTrue(g.AnswerWouldSkipAsBlocked(true),
                "…но ДА теперь пропуск — драйвер узнаёт это ЗАРАНЕЕ (без панча и без §6-окна)");

            double moneyBefore = g.Money;
            Yes(g);
            Assert.AreEqual("N", g.CurrentCard.Id, "неоплаченная карточка пропущена, забег идёт дальше");
            Assert.AreEqual(moneyBefore, g.Money, 0.001, "ни рубля не списано — это пропуск, а не покупка");
            Assert.AreEqual(health, g.Scales.Health, "и последствий покупки нет");

            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 60) No(g);
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "Пошли в автошколу за компанию.",
                "…и в некрологе покупки, которой не было, тоже нет");
        }

        [Test]
        public void CreditCard_IsExemptFromTheAnswerTimeRecheck()
        {
            // Контроль: кредитные (§3.5) под правило перепроверки НЕ попадают — уход в минус и есть их
            // содержание. Взнос по ипотеке банк погейтил на показе, второй раз карточку не отбирают.
            double price = Game.BlockPrices["MD04"];              // поздняя ипотека — взнос 40 ₽
            Assert.Contains("MD04", Game.CreditCards.ToList(), "MD04 — кредитная по канону §3.5");
            var mortgage = Flags(Plain("MD04", 19), "BLOCK$");
            mortgage.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -1) };
            var g = MoneyThenPriced(mortgage);

            while (g.Money < price + 0.5) g.HandleInput(GameInput.MoneyTick);
            No(g);
            Assert.AreEqual("MD04", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked);

            int health = g.Scales.Health;
            DrainJustBelowPrice(g, price);
            Assert.IsFalse(g.AnswerWouldSkipAsBlocked(true), "кредитную поздний недобор не отбирает");

            double moneyBefore = g.Money;
            Yes(g);
            Assert.AreEqual(moneyBefore - price, g.Money, 0.001, "взнос списан ЦЕЛИКОМ — кредитной в минус можно");
            Assert.Less(g.Money, 0.0, "…и счёт ушёл в минус, как и задумано");
            Assert.AreEqual(health - 7, g.Scales.Health, "последствия применились полностью");
        }

        [Test]
        public void Breakup_WithoutMarriage_IsFree()
        {
            // Штраф — за РАЗВОД, а не за любое расставание: не был женат — нечего делить.
            var cr06 = Flags(Plain("CR06", 21), "BREAK:" + Card.OpenRelations);
            var deck = new List<Card> { Starter(), Plain("OPEN18", 18), Plain("OPEN20", 20), cr06, Plain("N", 70) };
            var g = new Game(deck, coin: () => false);
            g.StartLife(); No(g); g.Tick(2f);
            for (int i = 0; i < 200; i++) g.HandleInput(GameInput.MoneyTick);
            No(g); g.Tick(2f); No(g);

            Assert.IsFalse(g.Married);
            double before = g.Money;
            Yes(g);
            Assert.AreEqual(before, g.Money, 0.001, "расставание без брака денег не стоит");
        }
    }
}
