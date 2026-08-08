using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// НОВЫЕ МЕХАНИКИ ОТРЕЗКОВ 1–7, поведенчески: `MD06` «второй шанс» (шкала отношений открывается
    /// ЗАНОВО), носитель `PRENUP` в живой колоде, взаимоисключение `EXCL:реб` в обе стороны, бесплатные
    /// импульсы `CR10`–`CR12`, разовые выплаты `ONCE:Ny` и разрыв на стороне НЕТ (`MD24`).
    ///
    /// Каждая проверка написана так, чтобы РУХНУТЬ при снятии своего куска реализации: проверяется
    /// наблюдаемое поведение, а не наличие поля.
    /// </summary>
    public class FullDeckMechanicsTests
    {
        private static List<Card> Csv()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            return CardLoader.ParseAll(csv.text);
        }

        private static Dictionary<string, Card> ById() => Csv().ToDictionary(c => c.Id);

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

        private static Game NewGame(params Card[] cards)
        {
            var deck = new List<Card> { Starter() };
            deck.AddRange(cards);
            return new Game(deck, coin: () => false);
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);

        // Догнать возраст МЕЛКИМИ тиками, не трогая текущую карточку: один большой Tick проматывает
        // её таймер, карточка уходит по таймауту, и тест меряет собственный цикл, а не игру.
        private static void AgeTo(Game g, float age)
        {
            var card = g.CurrentCard;
            for (int i = 0; i < 4000 && g.Age < age; i++)
            {
                g.Tick(0.05f);
                if (!ReferenceEquals(g.CurrentCard, card)) break;
            }
        }

        // ==================================================== MD06 «второй шанс»

        [Test]
        public void SecondChance_Card_IsNoLongerHardExcluded_AndCarriesOpenRelations()
        {
            var md06 = ById()["MD06"];
            Assert.IsFalse(DeckSampler.Excluded.Contains("MD06"), "MD06 больше не hard-excluded");
            Assert.IsTrue(md06.Opens(Card.OpenRelations), "MD06 несёт OPEN:Отн — она и открывает шкалу заново");
            Assert.IsTrue(md06.RequiresRelationshipsLost,
                "MD06 приходит только тому, у кого отношения потеряны («если Отн потеряна»)");
        }

        [Test]
        public void SecondChance_ReopensTheRelationshipsScale_AfterABreakup()
        {
            // Разрыв карточкой в 20+ (BREAK:Отн) гасит шкалу и ставит RelationshipsLost; до отрезка 6
            // возрастной путь открытия этот флаг уже НИКОГДА не переступал.
            var breakCard = Plain("BRK", 21);
            breakCard.BreaksRelationships = true;
            var second = Plain("MD06", 40);
            second.Flags = new List<string> { "OPEN:" + Card.OpenRelations };

            var g = NewGame(breakCard, second, Plain("AFTER", 50));
            g.StartLife();
            No(g);                      // I03 → таймер пошёл
            AgeTo(g, 21);               // догнать 21 → шкала отношений открылась
            Assert.IsTrue(g.RelationshipsOpen, "балансир открылся по возрасту");

            Yes(g);                     // BRK → разрыв
            Assert.IsFalse(g.RelationshipsOpen, "после разрыва шкала погасла");
            Assert.IsTrue(g.RelationshipsLost, "разрыв записан");

            AgeTo(g, 40);               // дожить до MD06
            Assert.IsFalse(g.RelationshipsOpen, "сам по себе возраст шкалу НЕ переоткрывает");
            Yes(g);                     // MD06 → второй шанс
            Assert.IsFalse(g.RelationshipsLost, "второй шанс снял метку потери");
            g.Tick(1f);
            Assert.IsTrue(g.RelationshipsOpen, "балансир отношений открылся ЗАНОВО");
            Assert.That(g.Scales.Relationships, Is.InRange(Scales.RelationshipsStart - 3, Scales.RelationshipsStart + 3),
                "новые отношения начинаются с того же, с чего начинались первые (± тик дрейфа)");
            Assert.Greater(g.Scales.Relationships, Game.RelBreakupValue + 10,
                "…а НЕ с «одиноко»-уровня прошлого разрыва");
        }

        [Test]
        public void SecondChance_Declined_LeavesTheScaleClosed()
        {
            var breakCard = Plain("BRK", 21);
            breakCard.BreaksRelationships = true;
            var second = Plain("MD06", 40);
            second.Flags = new List<string> { "OPEN:" + Card.OpenRelations };

            var g = NewGame(breakCard, second, Plain("AFTER", 50));
            g.StartLife();
            No(g);
            AgeTo(g, 21);
            Yes(g);                     // разрыв
            AgeTo(g, 40);
            No(g);                      // «нет, больше никого не подпущу»
            g.Tick(0.5f);
            Assert.IsFalse(g.RelationshipsOpen, "отказ от второго шанса шкалу не открывает");
            Assert.IsTrue(g.RelationshipsLost, "метка потери на месте");
        }

        // ==================================================== PRENUP получил носителя

        [Test]
        public void Prenup_HasACarrierInTheLiveDeck_AndItComesBeforeTheWedding()
        {
            var carriers = Csv().Where(c => c.IsPrenup).Select(c => c.Id).ToList();
            CollectionAssert.Contains(carriers, "MD09", "брачный договор носит MD09");
            var md09 = ById()["MD09"];
            Assert.AreEqual("MD01", md09.BeforeCardId, "MD09 обязана выпасть ДО свадьбы — иначе она бессмысленна");
        }

        [Test]
        public void Prenup_IsDealtBeforeTheWedding_OnTheLiveDeck()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var deck = DeckSampler.Build(Csv(), new System.Random(seed));
                int prenup = deck.FindIndex(c => c.Id == "MD09");
                int wedding = deck.FindIndex(c => c.Id == "MD01");
                Assert.Greater(prenup, -1, $"seed {seed}: MD09 в колоде");
                Assert.Greater(wedding, -1, $"seed {seed}: MD01 в колоде");
                Assert.Less(prenup, wedding, $"seed {seed}: брачный договор предлагают ДО свадьбы");
                Assert.Less(deck[prenup].Age, deck[wedding].Age, $"seed {seed}: и по возрасту тоже");
            }
        }

        // ==================================================== EXCL:реб в обе стороны

        [Test]
        public void ChildBranch_IsMutuallyExclusive_OnTheLiveDeck()
        {
            var byId = ById();
            Assert.AreEqual("реб", byId["MD02"].ExclusiveGroup, "MD02 в ветке «реб»");
            Assert.AreEqual("реб", byId["MD13"].ExclusiveGroup, "MD13 «детей не будет» — встречная карточка");
            Assert.IsTrue(byId["MD13"].LongEffects.Any(e => e.Kind == LongEffectKind.Mult
                                                            && e.Scale == Scale.Money),
                "у ветки «детей не будет» свой множитель дохода (денег остаётся больше)");
        }

        [Test]
        public void ChildlessBranch_IsOfferedBeforeTheChild_SoEitherAnswerDecides()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var deck = DeckSampler.Build(Csv(), new System.Random(seed));
                int no = deck.FindIndex(c => c.Id == "MD13");
                int child = deck.FindIndex(c => c.Id == "MD02");
                Assert.Greater(no, -1, $"seed {seed}: MD13 в колоде");
                Assert.Greater(child, -1, $"seed {seed}: MD02 в колоде");
                Assert.Less(deck[no].Age, deck[child].Age,
                    $"seed {seed}: «детей не будет» спрашивают раньше «завести ребёнка»");
            }
        }

        [Test]
        public void SayingYesToChildless_HidesTheChildCard_AndViceVersa()
        {
            var childless = Plain("MD13", 30);
            childless.ExclusiveGroup = "реб";
            var child = Plain("MD02", 32);
            child.ExclusiveGroup = "реб";
            var after = Plain("AFTER", 40);

            var g = NewGame(childless, child, after);
            g.StartLife();
            No(g);
            AgeTo(g, 30);
            Yes(g);                          // MD13 = ДА → ветка занята
            Assert.AreNotEqual("MD02", g.CurrentCard?.Id, "встречная карточка про ребёнка не приходит");

            // …и наоборот: отказ ветку не закрывает.
            var childless2 = Plain("MD13", 30);
            childless2.ExclusiveGroup = "реб";
            var child2 = Plain("MD02", 32);
            child2.ExclusiveGroup = "реб";
            var g2 = NewGame(childless2, child2);
            g2.StartLife();
            No(g2);
            AgeTo(g2, 30);
            No(g2);                          // MD13 = НЕТ → вопрос остаётся открытым
            Assert.AreEqual("MD02", g2.CurrentCard?.Id, "после отказа карточка про ребёнка приходит");
        }

        // ==================================================== CR10–CR12 бесплатные импульсы

        [Test]
        public void FreeImpulses_AreInTheCrisisBlock_AndCostNothing()
        {
            var byId = ById();
            foreach (var id in new[] { "CR10", "CR11", "CR12" })
            {
                Assert.IsTrue(byId.ContainsKey(id), $"{id} посажена в колоду");
                Assert.IsTrue(byId[id].IsInvert, $"{id} — импульс (INVERT: молчание = ДА)");
                Assert.IsFalse(byId[id].IsBlockCost, $"{id} бесплатна — в этом весь смысл");
                Assert.IsFalse(Game.BlockPrices.ContainsKey(id), $"{id} не имеет цены");
            }
            for (int seed = 0; seed < 10; seed++)
            {
                var plan = DeckSampler.BuildPlan(Csv(), new System.Random(seed));
                var crisis = plan.Crisis.Select(c => c.Id).ToList();
                foreach (var id in new[] { "CR10", "CR11", "CR12" })
                {
                    CollectionAssert.Contains(crisis, id, $"seed {seed}: {id} несётся кризис-блоком");
                    Assert.IsFalse(plan.Deck.Any(c => c.Id == id), $"seed {seed}: {id} не сэмплируется в колоду");
                    Assert.IsFalse(plan.Reserve.Any(c => c.Id == id), $"seed {seed}: {id} не попадает в резерв");
                }
            }
        }

        [Test]
        public void ImpulsePool_LeavesTheBrokePlayerSomethingToDo()
        {
            // Дыра, которую CR10–CR12 закрывают: до них платными были ВСЕ импульсы, кроме CR06, и у
            // безденежного кризис сводился к одному «БРОСИТЬ ПАРТНЁРА».
            var impulses = Csv().Where(c => c.IsInvert).ToList();
            Assert.GreaterOrEqual(impulses.Count, 6, "импульс-пул вырос до шести");
            int free = impulses.Count(c => !c.IsBlockCost);
            Assert.GreaterOrEqual(free, Game.ImpulseRoundSize,
                "бесплатных импульсов хватает, чтобы набрать целый раунд без единого рубля");
        }

        // ==================================================== ONCE:Ny — разовая расплата

        [Test]
        public void OnceGrammar_Parses_AsALumpSum_NotADrain()
        {
            var byId = ById();
            var fa27 = byId["FA27"].LongEffects.Single();
            Assert.AreEqual(LongEffectKind.Once, fa27.Kind, "поручительство — разовая выплата");
            Assert.AreEqual(-25, fa27.OnceAmount, 0.001);
            Assert.AreEqual(3, fa27.OnceYears);

            var fc32 = byId["FC32"].LongEffects.Single();
            Assert.AreEqual(LongEffectKind.Once, fc32.Kind);
            Assert.AreEqual(100, fc32.OnceAmount, 0.001, "подушка безопасности — единственная ПОЛОЖИТЕЛЬНАЯ");
            Assert.AreEqual(10, fc32.OnceYears);

            // …и это НЕ дренаж: у дренажа есть /s, у разовой — нет.
            Assert.AreEqual(LongEffectKind.Drain, byId["FA31"].LongEffects.Single().Kind,
                "микрозайм остался дренажом");
        }

        [Test]
        public void OnceEffect_LandsOnceAtItsYear_ThenStops()
        {
            var card = Plain("FA27", 19);
            card.LongEffects = new List<LongEffect>
            {
                new LongEffect { Kind = LongEffectKind.Once, Scale = Scale.Money, OnceAmount = -25, OnceYears = 3 },
            };
            var g = NewGame(card, Plain("LATER", 60));
            g.StartLife();
            No(g);
            g.Tick(2f);                                  // догнать 18–19, деньги открылись
            for (int i = 0; i < 200; i++) g.HandleInput(GameInput.MoneyTick);
            Yes(g);                                      // поручился
            double atAnswer = g.Money;
            float ageAtAnswer = g.Age;

            g.Tick(0.05f);   // одна РЕАЛЬНАЯ секунда — это ~8 игровых лет, поэтому шаг мелкий
            Assert.Greater(g.Money, atAnswer - 10, "до срока чужой долг не трогают (капает только стоимость жизни)");

            while (g.Age < ageAtAnswer + 4 && g.Age < 70) g.Tick(0.05f);
            Assert.Less(g.Money, atAnswer - 20, "через три года долг закрывать самому — удар, а не дренаж");
            double afterHit = g.Money;
            g.Tick(2f);
            Assert.Greater(g.Money, afterHit - 25 + 1, "удар одноразовый — второй раз не прилетает");
        }

        // ==================================================== BREAK на стороне НЕТ

        [Test]
        public void BreakOnTheNoSide_Parses_AndOnlyFiresOnNo()
        {
            var md24 = ById()["MD24"];
            Assert.IsTrue(md24.BreaksRelationships, "MD24 умеет рвать");
            Assert.IsTrue(md24.BreakOnNoSide, "…но на НЕТ: «Развод» — это отказ терпеть ради ребёнка");
            Assert.IsFalse(ById()["FB31"].BreakOnNoSide, "FB31 по-прежнему рвёт на ДА");

            // ДА по MD24 отношения НЕ рвёт (терпим дальше)…
            var stay = Plain("MD24", 45);
            stay.BreaksRelationships = true;
            stay.BreakOnNoSide = true;
            var g = NewGame(stay);
            g.StartLife();
            No(g);
            AgeTo(g, 45);
            Assert.IsTrue(g.RelationshipsOpen, "балансир открыт");
            Yes(g);
            Assert.IsTrue(g.RelationshipsOpen, "«потерпеть» брак не рвёт");

            // …а НЕТ рвёт.
            var leave = Plain("MD24", 45);
            leave.BreaksRelationships = true;
            leave.BreakOnNoSide = true;
            var g2 = NewGame(leave);
            g2.StartLife();
            No(g2);
            AgeTo(g2, 45);
            No(g2);
            Assert.IsFalse(g2.RelationshipsOpen, "«свобода» — это развод");
            Assert.IsTrue(g2.RelationshipsLost);
        }

        // ==================================================== условия колонки «Когда»

        [Test]
        public void MoneyCondition_KeepsTheCardAwayFromWhoeverHasMoney()
        {
            var byId = ById();
            Assert.AreEqual(5, byId["FA35"].RequiresMoneyBelow, 0.001, "«если на счету < 5 ₽»");
            Assert.AreEqual(0, byId["FC16"].RequiresMoneyBelow, 0.001, "«и на счету минус»");
            Assert.AreEqual("FA11", byId["FC16"].RequiresParentYes, "…и только тому, кто съехал от родителей");

            // Накрутил — сцены не увидел.
            var poor = Plain("FA35", 18);
            poor.RequiresMoneyBelow = 5;
            var next = Plain("NEXT", 19);
            var g = NewGame(poor, next);
            g.StartLife();
            No(g);
            g.Tick(2f);
            for (int i = 0; i < 50; i++) g.HandleInput(GameInput.MoneyTick);
            g.Tick(0.1f);
            Assert.AreNotEqual("FA35", g.CurrentCard?.Id, "у кого деньги есть — карточка про первый ноль не приходит");
        }

        [Test]
        public void MarriageOffsetCards_LandAroundTheWedding_NotInChildhood()
        {
            // «свадьба +N» окно-парсер отдаёт как (0,0). Оставь такую карточку в общем пуле — и «взять его
            // фамилию?» уедет в детство.
            for (int seed = 0; seed < 20; seed++)
            {
                var deck = DeckSampler.Build(Csv(), new System.Random(seed));
                var byId = deck.ToDictionary(c => c.Id, c => c);
                Assert.IsTrue(byId.ContainsKey("MD01"), $"seed {seed}: свадьба в колоде");
                int wedding = byId["MD01"].Age;
                foreach (var (id, offset) in new[] { ("MD11", 0), ("MD12", 1), ("MD02", 2) })
                {
                    Assert.IsTrue(byId.ContainsKey(id), $"seed {seed}: {id} в колоде");
                    Assert.AreEqual(wedding + offset, byId[id].Age, $"seed {seed}: {id} = свадьба + {offset}");
                    Assert.AreEqual("MD01", byId[id].RequiresParentYes, $"{id} гейтится свадьбой");
                }
            }
        }

        // ==================================================== «если {шкала} открыта» — гейт живого состояния
        //
        // ⚠ НАХОДКА РЕВЮ (MAJOR). Форма молча игнорировалась парсером, и `MD01` «СВАДЬБА! Сказать „да“?»
        // приходила игроку БЕЗ ОТКРЫТОЙ ШКАЛЫ ОТНОШЕНИЙ — а следом `MD02` предлагала ребёнка. Достижимый
        // сценарий ровно один и совершенно обычный: разрыв до тридцати (`FB31` «Отпустить?» = ДА в 20–24)
        // гасит шкалу насовсем, и в 28–32 игроку, которого бросили в двадцать два, предлагали жениться.

        // Карточка `MD01` как она есть в CSV: веха + условие «если Отн открыта» из колонки «Когда».
        private static Card Wedding(int age)
        {
            var c = Plain("MD01", age);
            c.When = "28–32, если Отн открыта";
            CardLoader.ApplyWhenConditions(c);
            Assert.AreEqual(Card.OpenRelations, c.RequiresScaleOpen,
                "условие свадьбы прочитано ИЗ КОЛОНКИ, а не проставлено тестом руками");
            return c;
        }

        [Test]
        public void Wedding_IsDealt_WhileTheRelationshipsScaleIsOpen()
        {
            var g = NewGame(Plain("FILL", 21), Wedding(30), Plain("AFTER", 50));
            g.StartLife();
            No(g);                       // I03
            AgeTo(g, 21);
            Assert.IsTrue(g.RelationshipsOpen, "балансир открыт возрастным путём");
            No(g);                       // FILL
            AgeTo(g, 30);
            Assert.AreEqual("MD01", g.CurrentCard?.Id, "с открытой шкалой свадьбу предлагают");
        }

        [Test]
        public void Wedding_IsSkipped_AfterABreakup_WhenTheRelationshipsScaleIsClosed()
        {
            // FB31-подобный разрыв в 21 → шкала погасла и возрастом больше не открывается.
            var split = Plain("FB31", 21);
            split.BreaksRelationships = true;

            var g = NewGame(split, Wedding(30), Plain("AFTER", 50));
            g.StartLife();
            No(g);
            AgeTo(g, 21);
            Assert.IsTrue(g.RelationshipsOpen, "до разрыва балансир открыт");
            Yes(g);                      // «отпустить» → партнёр ушёл
            Assert.IsFalse(g.RelationshipsOpen, "шкала погасла");

            AgeTo(g, 30);
            Assert.AreNotEqual("MD01", g.CurrentCard?.Id,
                "жениться НЕ на ком: без открытой шкалы отношений свадьба не приходит");
            Assert.AreEqual("AFTER", g.CurrentCard?.Id, "…и забег идёт дальше — карточка пропущена, а не зависла");
        }

        [Test]
        public void ChildCard_NeverFollowsAWeddingThatWasNeverOffered()
        {
            // Вторая половина репро: `MD02` («РЕБЁНОК! Завести?») гейтится «MD01=ДА». Раз свадьбы не было,
            // не должно быть и ребёнка — иначе шкала ребёнка открывалась бы «от партнёра», которого нет.
            var split = Plain("FB31", 21);
            split.BreaksRelationships = true;
            var child = Plain("MD02", 32);
            child.RequiresParentYes = "MD01";

            var g = NewGame(split, Wedding(30), child, Plain("AFTER", 50));
            g.StartLife();
            No(g);
            AgeTo(g, 21);
            Yes(g);                      // разрыв
            AgeTo(g, 30);
            Assert.AreNotEqual("MD01", g.CurrentCard?.Id, "свадьбы нет");
            Assert.AreNotEqual("MD02", g.CurrentCard?.Id, "и ребёнка от несуществующего брака — тоже");
            Assert.IsFalse(g.ChildOpen, "шкала ребёнка закрыта");
        }

        [Test]
        public void MoneyAtLeastCondition_GatesTheCardOnAFullWallet()
        {
            // Зеркало «на счету < N ₽», тоже молча игнорировавшееся: `MD03` «купить машину за 60 ₽».
            Card Car()
            {
                var c = Plain("MD03", 31);
                c.When = "30+, если на счету ≥ 60₽";
                CardLoader.ApplyWhenConditions(c);
                Assert.AreEqual(60d, c.RequiresMoneyAtLeast, "порог прочитан из колонки");
                return c;
            }

            // Крутилка работает только при ОТКРЫТЫХ деньгах (18+), поэтому в обоих забегах есть филлер в
            // 25, на котором богатый и крутит; бедный просто стоит рядом.
            var broke = NewGame(Plain("FILL", 25), Car(), Plain("AFTER", 40));
            broke.StartLife(); No(broke);
            AgeTo(broke, 25);
            No(broke);
            AgeTo(broke, 31);
            Assert.Less(broke.Money, 60d, "у бедного на счету меньше порога");
            Assert.AreNotEqual("MD03", broke.CurrentCard?.Id, "пустому кошельку машину не предлагают");

            var rich = NewGame(Plain("FILL", 25), Car(), Plain("AFTER", 40));
            rich.StartLife(); No(rich);
            AgeTo(rich, 25);
            for (int i = 0; i < 200; i++) rich.HandleInput(GameInput.MoneyTick);
            No(rich);
            AgeTo(rich, 31);
            Assert.GreaterOrEqual(rich.Money, 60d, "у состоятельного на счету действительно есть 60 ₽");
            Assert.AreEqual("MD03", rich.CurrentCard?.Id, "…и ему машину предлагают");
        }

        // ==================================================== ONCE:Ny — расплата через тот же зажим
        //
        // ⚠ НАХОДКА РЕВЮ (MAJOR). Разовая выплата шла голым `Money += …` мимо `AddCardMoney`, и НЕкредитная
        // карточка (`FA27` поручительство −25 ₽, `FB32` бумаги не читая −60 ₽) уводила счёт В МИНУС —
        // прямое нарушение правила отрезка 0 «единственный минус — кредитные».

        // Карточка с разовой расплатой −25 ₽ через 3 года. Id решает, кредитная она или нет.
        private static Card OnceDebtCard(string id, int age) => new Card
        {
            Id = id, Question = id + "?", Age = age, Order = age,
            YesDeltas = new List<ScaleDelta>(), NoDeltas = new List<ScaleDelta>(), Flags = new List<string>(),
            LongEffects = new List<LongEffect>
            {
                new LongEffect { Kind = LongEffectKind.Once, Scale = Scale.Money, OnceAmount = -25, OnceYears = 3 },
            },
        };

        // Довести забег до расплаты со счётом ≈10 ₽ и вернуть счёт сразу после удара.
        private static double MoneyAfterTheLumpSum(string cardId)
        {
            var card = OnceDebtCard(cardId, 19);
            var g = NewGame(card, Plain("LATER", 60));
            g.StartLife();
            No(g);
            g.Tick(2f);                                        // 18+ → деньги открылись
            for (int i = 0; i < 10; i++) g.HandleInput(GameInput.MoneyTick);   // ≈10 ₽ на счету
            Assert.That(g.Money, Is.InRange(8d, 11d), $"{cardId}: на момент ответа на счету около десятки");

            Yes(g);                                            // поручился
            float due = g.Age + 3;
            for (int i = 0; i < 4000 && g.Age < due; i++) g.Tick(0.01f);
            g.Tick(0.01f);                                     // …и тик, в котором расплата созрела
            return g.Money;
        }

        [Test]
        public void LumpSumPayment_OnAnOrdinaryCard_StopsAtZero_NotInTheRed()
        {
            double money = MoneyAfterTheLumpSum("FA27");
            Assert.Greater(money, -1.0,
                "расплата по НЕкредитной карточке зажимается остатком: со счёта в 10 ₽ снимается 10, а не 25 "
                    + "(до находки ревю здесь было −15 ₽ мимо AddCardMoney)");
            Assert.Greater(money, Game.DebtAnnounceBelow,
                "…и долг по ней не объявляется — долг бывает только по кредитным");
        }

        [Test]
        public void LumpSumPayment_OnACreditCard_DoesGoIntoTheRed()
        {
            // Контроль: зажим не «выключен везде», он различает кредитную карточку. `FC14` — кредитная и
            // при этом БЕЗ своей цены и без записи в таблице заработков, поэтому в замер не подмешивается
            // ничего, кроме самой разовой расплаты (`YA04` для контроля не годится: она несёт +40 ₽).
            CollectionAssert.Contains(Game.CreditCards, "FC14", "контрольная карточка действительно кредитная");
            Assert.IsFalse(Game.CardYesIncome.ContainsKey("FC14"), "…и своего заработка у неё нет");
            Assert.IsFalse(Game.BlockPrices.ContainsKey("FC14"), "…и своей цены тоже");
            double money = MoneyAfterTheLumpSum("FC14");
            Assert.Less(money, -13.0,
                "по кредитной уход в минус — содержание карточки: 10 − 25 ≈ −15 ₽");
        }

        // ==================================================== разрыв уносит свои DRIFT-эффекты
        //
        // ⚠ НАХОДКА РЕВЮ (MAJOR). `MD06` возвращал шкалу и метку, но НЕ чистил `DRIFT:Отн` от умершей
        // ветки: «НЕТ на MD01» (×2 «таять даже при поддержке») → разрыв → второй шанс, и новые отношения
        // начинались с унаследованным ускорением от связи, которой больше нет.

        [Test]
        public void Breakup_ClearsRelationshipDrifts_SoTheSecondChanceStartsClean()
        {
            // Карточка «отказ от свадьбы»: на НЕТ вешает DRIFT:Отн=x2 (как MD01 в живом CSV).
            var refuse = Plain("MD01", 21);
            refuse.LongEffects = new List<LongEffect>
            {
                new LongEffect
                {
                    Kind = LongEffectKind.Drift, Scale = Scale.Relationships, MultValue = 2.0, OnNoSide = true,
                },
            };
            var split = Plain("BRK", 25);
            split.BreaksRelationships = true;
            var second = Plain("MD06", 40);
            second.Flags = new List<string> { "OPEN:" + Card.OpenRelations };

            var g = NewGame(refuse, split, second, Plain("AFTER", 50));
            g.StartLife();
            No(g);
            AgeTo(g, 21);
            Assert.IsTrue(g.RelationshipsOpen, "балансир открыт");
            No(g);                                   // отказ от свадьбы → ×2
            Assert.AreEqual(Game.RelDriftPerSec * 2.0, g.RelationshipDriftPerSec, 0.001,
                "отказ от свадьбы действительно ускорил дрейф вдвое — иначе тест ничего не стережёт");

            AgeTo(g, 25);
            Yes(g);                                  // разрыв: ветка умерла
            AgeTo(g, 40);
            Yes(g);                                  // MD06 — второй шанс
            g.Tick(0.05f);
            Assert.IsTrue(g.RelationshipsOpen, "шкала открылась заново");
            Assert.AreEqual(Game.RelDriftPerSec, g.RelationshipDriftPerSec, 0.001,
                "новые отношения тают С БАЗОВОЙ скоростью: ×2 умер вместе с прошлой связью");
        }

        [Test]
        public void Breakup_LeavesNonRelationshipDrifts_Alone()
        {
            // Чистка адресная: разрыв уносит эффекты ПО ОТНОШЕНИЯМ, а не весь список длительных эффектов.
            var card = Plain("MULTI", 21);
            card.LongEffects = new List<LongEffect>
            {
                new LongEffect { Kind = LongEffectKind.Drift, Scale = Scale.Relationships, MultValue = 2.0 },
                new LongEffect { Kind = LongEffectKind.Mult, Scale = Scale.Money, MultValue = 3.0 },
            };
            var split = Plain("BRK", 25);
            split.BreaksRelationships = true;

            var g = NewGame(card, split, Plain("AFTER", 50));
            g.StartLife();
            No(g);
            AgeTo(g, 21);
            Yes(g);                                  // ×2 по отношениям + ×3 по доходу
            Assert.AreEqual(3.0, g.IncomeMultiplier, 0.001, "множитель дохода взведён");
            AgeTo(g, 25);
            Yes(g);                                  // разрыв
            Assert.AreEqual(3.0, g.IncomeMultiplier, 0.001,
                "разрыв уносит только дрейф отношений — доход к партнёру отношения не имеет");
        }
    }
}
