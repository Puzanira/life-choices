using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ВАЛИДАТОР ПОЛНОЙ КОЛОДЫ (отрезки 1–7, 2026-08-08) — структурные инварианты живого `scenes.csv`
    /// после того, как в него сели 168 новых карточек: ID уникальны, флаги известны парсеру, чейны
    /// замкнуты (родитель существует и стоит раньше ребёнка), окна возрастов осмысленны, у каждой платной
    /// карточки есть цена, а у каждой новой — реплика Ведущего на обе стороны.
    ///
    /// Смысл файла — поймать ОПЕЧАТКУ дизайнера, а не проверить движок: колода теперь такого размера, что
    /// «BLOCK$ без цены» или «CHAIN→ в никуда» глазами не видно. Поведение новых механик живёт рядом,
    /// в <see cref="FullDeckMechanicsTests"/>.
    /// </summary>
    public class FullDeckConformanceTests
    {
        private static List<Card> Deck()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            return CardLoader.ParseAll(csv.text);
        }

        // Всё, что парсер и движок действительно понимают. Незнакомый флаг не роняет загрузку (терпимость
        // — часть контракта загрузчика), поэтому опечатку способен поймать только такой список.
        private static readonly HashSet<string> KnownBareFlags = new()
        {
            "NOCONS", "TIMELINE", "ROND", "FORCED", "BLOCK$", "BLITZ", "INVERT", "ZONE",
            "FATAL", "RANDOM", "RANDOM_TRIGGER", "RANDOM_OUTCOME", "PRENUP",
        };
        private static readonly string[] KnownFlagPrefixes =
        {
            "OPEN:", "CHAIN→", "DELAY(", "EXCL:", "BREAK:", "НЕТ:BREAK:", "MULT:", "DRIFT:",
        };

        [Test]
        public void EveryRowParses_AndIdsAreUnique()
        {
            var deck = Deck();
            Assert.Greater(deck.Count, 240, "полная колода отрезков 1–7 разобрана");
            var dupes = deck.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            CollectionAssert.IsEmpty(dupes, "ID уникальны");
            foreach (var c in deck)
            {
                Assert.IsNotEmpty(c.Question, $"{c.Id}: текст карточки не пуст");
                Assert.IsNotEmpty(c.When, $"{c.Id}: колонка «Когда» не пуста");
            }
        }

        [Test]
        public void EveryFlag_IsKnownToTheParser()
        {
            var unknown = new List<string>();
            foreach (var c in Deck())
                foreach (var f in c.Flags)
                {
                    if (KnownBareFlags.Contains(f)) continue;
                    if (KnownFlagPrefixes.Any(p => f.StartsWith(p, System.StringComparison.Ordinal))) continue;
                    unknown.Add(c.Id + ": «" + f + "»");
                }
            CollectionAssert.IsEmpty(unknown, "незнакомых флагов в колоде нет (опечатка молча игнорируется парсером)");
        }

        [Test]
        public void EveryChain_IsClosed_ParentExists_AndComesFirst()
        {
            var deck = Deck();
            var byId = deck.ToDictionary(c => c.Id);

            // (а) CHAIN→X на родителе: ребёнок X существует.
            foreach (var c in deck)
                foreach (var f in c.Flags.Where(f => f.StartsWith("CHAIN→", System.StringComparison.Ordinal)))
                {
                    var child = f.Substring("CHAIN→".Length).Trim();
                    Assert.IsTrue(byId.ContainsKey(child), $"{c.Id}: CHAIN→{child} ведёт в несуществующую карточку");
                }

            // (б) условие «если X=ДА» на ребёнке: родитель X существует И его окно начинается раньше.
            foreach (var c in deck.Where(c => c.RequiresParentYes != null))
            {
                Assert.IsTrue(byId.ContainsKey(c.RequiresParentYes),
                    $"{c.Id}: гейт на несуществующую карточку {c.RequiresParentYes}");
                var parent = byId[c.RequiresParentYes];
                var pw = DeckSampler.AgeWindow.Parse(parent.When);
                var cw = DeckSampler.AgeWindow.Parse(c.When);
                if (cw.IsMarriageOffset || pw.IsMarriageOffset) continue;   // возраст задаёт веха, не окно
                Assert.LessOrEqual(pw.Min, cw.Min,
                    $"{c.Id}: родитель {parent.Id} должен открываться не позже ребёнка");
            }

            // (в) «до X» существует.
            foreach (var c in deck.Where(c => c.BeforeCardId != null))
                Assert.IsTrue(byId.ContainsKey(c.BeforeCardId),
                    $"{c.Id}: «до {c.BeforeCardId}» ведёт в несуществующую карточку");
        }

        [Test]
        public void EveryAgeWindow_IsSane()
        {
            foreach (var c in Deck())
            {
                var w = DeckSampler.AgeWindow.Parse(c.When);
                if (w.IsMarriageOffset) continue;
                Assert.LessOrEqual(w.Min, w.Max, $"{c.Id}: окно «{c.When}» вывернуто");
                Assert.GreaterOrEqual(w.Min, 0, $"{c.Id}: окно «{c.When}» начинается до рождения");
                Assert.LessOrEqual(w.Max, 100, $"{c.Id}: окно «{c.When}» уходит за сто лет");
            }
        }

        [Test]
        public void EveryBlockCostCard_HasAPrice()
        {
            var missing = Deck().Where(c => c.IsBlockCost && !Game.BlockPrices.ContainsKey(c.Id))
                                .Select(c => c.Id).ToList();
            CollectionAssert.IsEmpty(missing,
                "BLOCK$ без цены = карточка, которая гейтится нулём и списывает ноль (то есть бесплатна)");
        }

        [Test]
        public void PriceTable_AgreesWithTheCsvMoneyDelta()
        {
            // Доки отрезков 1–7 пишут суммы прямо в ₽ («−30 ₽»), поэтому у платных карточек одно и то же
            // число живёт в двух местах: в колонке Δ (для человека) и в BlockPrices (для движка). Разойтись
            // они не должны — и это единственная проверка, которая это ловит.
            foreach (var c in Deck())
            {
                if (!Game.BlockPrices.TryGetValue(c.Id, out var price)) continue;
                var money = c.YesDeltas.FirstOrDefault(d => d.Scale == Scale.Money
                                                            && d.Kind == DeltaKind.Add
                                                            && !DeltaScale.IsQualitative(d.Kind, d.Value));
                if (money.Scale != Scale.Money || money.Value == 0) continue;   // Δ качественная либо её нет
                Assert.AreEqual(price, System.Math.Abs(money.Value), 0.001,
                    $"{c.Id}: цена в таблице ({price}) разошлась с Δ в CSV ({money.Value})");
            }
        }

        [Test]
        public void PricedCard_IsNeverNocons()
        {
            // Правило отрезка 0: NOCONS = «ничего не применяем», поэтому платная NOCONS-карточка списывает
            // ноль при любой цене. Здесь оно перепроверяется уже на полной колоде.
            foreach (var c in Deck())
                Assert.IsFalse(c.IsNoCons && Game.BlockPrices.ContainsKey(c.Id),
                    $"{c.Id}: NOCONS несовместим с ценой");
        }

        [Test]
        public void EveryCard_HasSomethingForTheHostToSay()
        {
            // Реплики Ведущего: НАЗВАННАЯ строка бьёт пул всегда, поэтому карточка без обеих строк молчит
            // ровно настолько, насколько позволит PoolChance. Дизайн-сессия закрыла все немые карточки —
            // требуем, чтобы у каждой ОБЫЧНОЙ карточки была хотя бы одна названная реплика.
            //
            // ⚠ Пять ЛЕГАСИ-карточек молчат до сих пор: `LT02` операция, `LT05` старый друг и кек
            // `KEK03`/`KEK04`/`KEK05`. Дизайн-сессия закрыла все немые карточки ДЕТСТВА (отрезок 1 §1.1) и
            // выдала реплики каждой новой, но этих пяти не касалась — а сочинять голос Ведущего за неё
            // owner не станет. Список зафиксирован, чтобы он не РОС: любая новая немая карточка красит тест.
            var knownMute = new HashSet<string> { "LT02", "LT05", "KEK03", "KEK04", "KEK05" };
            var mute = Deck()
                .Where(c => !c.IsForced && !c.IsBlitz && !c.IsNoCons)
                .Where(c => string.IsNullOrEmpty(c.HostYes) && string.IsNullOrEmpty(c.HostNo))
                .Select(c => c.Id).ToList();
            CollectionAssert.IsSubsetOf(mute, knownMute,
                "новых немых карточек не появилось (старые пять — открытый вопрос основательнице)");
        }

        [Test]
        public void EveryToneTag_IsEitherAPool_OrTheDesignVocabulary()
        {
            // Пулов у Ведущего четыре (positive/risky/absurd/cautious); дизайн-сессия писала ещё три тона
            // «настроения» — warm/sweet/bitter. Они НЕ пулы и намеренно проваливаются в Δ-эвристику
            // (HostVoice.TryParseTone), но должны быть написаны без опечаток, иначе тон молча теряется.
            var vocabulary = new HashSet<string> { "positive", "risky", "absurd", "cautious", "warm", "sweet", "bitter" };
            var bad = Deck().Where(c => !string.IsNullOrEmpty(c.Tone))
                            .Where(c => !vocabulary.Contains(c.Tone.Trim().ToLowerInvariant()))
                            .Select(c => c.Id + ": «" + c.Tone + "»").ToList();
            CollectionAssert.IsEmpty(bad, "тон написан из известного словаря");
        }

        [Test]
        public void EveryFatalCard_HasANamedCause()
        {
            // Причина финала печатается на экране смерти. Безымянная даёт «неведомая дичь» — приемлемо как
            // запасной вариант, но не для карточек, которые дизайнер назвал.
            foreach (var c in Deck().Where(c => c.YesIsFatal || c.DelayedFatalYears > 0))
                Assert.AreNotEqual("неведомая дичь", c.FatalCause,
                    $"{c.Id}: фатальная карточка без названной причины финала");
        }

        [Test]
        public void NecrologLines_EndAsSentences()
        {
            // Некролог собирается построчно; строка без точки на конце склеивается с соседкой в кашу.
            var bad = new List<string>();
            foreach (var c in Deck())
                foreach (var (line, side) in new[] { (c.YesNecrolog, "ДА"), (c.NoNecrolog, "НЕТ") })
                    if (!string.IsNullOrEmpty(line) && !line.EndsWith(".") && !line.EndsWith("!")
                        && !line.EndsWith("?") && !line.EndsWith("…"))
                        bad.Add($"{c.Id} {side}: «{line}»");
            CollectionAssert.IsEmpty(bad, "строки некролога — законченные предложения");
        }

        // ==================================================== колонка «Когда»: узнано ВСЁ
        //
        // ⚠ НАХОДКА РЕВЮ (MAJOR). Парсер условий молча игнорировал незнакомую формулировку — «колонка
        // остаётся человеческой прозой». Цена этой терпимости: «28–32, если Отн открыта» на свадьбе не
        // работало ВООБЩЕ, и на CSV в 253 строки это было невидимо. Проверки ниже закрывают класс: любая
        // форма, которую парсер не разобрал, роняет валидатор — новую формулировку нельзя завести в CSV,
        // не заведя её в грамматике.

        [Test]
        public void EveryWhenCondition_IsUnderstoodByTheParser()
        {
            var unparsed = new List<string>();
            foreach (var c in Deck())
                foreach (var clause in c.UnparsedWhen)
                    unparsed.Add($"{c.Id} «{c.When}» → не разобрано: «{clause}»");

            CollectionAssert.IsEmpty(unparsed,
                "каждая клауза колонки «Когда» разобрана грамматикой CardLoader.ApplyWhenConditions — "
                    + "иначе условие написано, но НЕ РАБОТАЕТ (так было со «свадьбой без отношений»)");
        }

        [Test]
        public void UnknownWhenForm_IsReportedAsAnError_NotSilentlyIgnored()
        {
            // Тот самый класс ошибок, в лицо: форма, похожая на условие, но грамматике неизвестная.
            foreach (var junk in new[]
                     {
                         "30–39, если луна в скорпионе",
                         "25–29, до того как всё станет ясно",
                         "40–55, если Отн закрыта",     // «закрыта» грамматике не известна — только «открыта»
                         "18–19, на счету ровно 5 ₽",
                     })
            {
                var card = new Card { Id = "JUNK", When = junk };
                CardLoader.ApplyWhenConditions(card);
                CollectionAssert.IsNotEmpty(card.UnparsedWhen,
                    $"«{junk}» — незнакомая форма, и парсер обязан о ней ОТЧИТАТЬСЯ, а не пропустить");
            }
        }

        [Test]
        public void ProseWhitelist_HasNoDeadEntries()
        {
            // Список «проза без механики» — единственная законная дыра в грамматике, поэтому он обязан
            // быть ЖИВЫМ: запись, которой в CSV больше нет, завтра прикроет собой настоящую опечатку.
            var clauses = new HashSet<string>(
                Deck().SelectMany(c => CardLoader.SplitWhenClauses(c.When))
                      .Select(s => s.ToLowerInvariant()));

            var dead = CardLoader.WhenProseNoCondition
                .Where(p => !clauses.Contains(p.ToLowerInvariant())).ToList();
            CollectionAssert.IsEmpty(dead,
                "в белом списке прозы нет мёртвых записей — каждая реально стоит в колоде");
        }

        [Test]
        public void ScaleStateConditions_AreReadFromTheColumn_IntoTheCard()
        {
            var byId = Deck().ToDictionary(c => c.Id);

            // Обе формы записи одной и той же вещи — короткий токен и человеческая проза.
            Assert.AreEqual(Card.OpenRelations, byId["MD01"].RequiresScaleOpen,
                "«если Отн открыта» — свадьба требует ОТКРЫТОЙ шкалы отношений");
            Assert.AreEqual(Card.OpenRelations, byId["FB31"].RequiresScaleOpen,
                "«если шкала отношений открыта» — та же вещь прозой, читается так же");
            Assert.AreEqual(Card.OpenMoney, byId["YA04"].RequiresScaleOpen,
                "«если Дн открыта» — первый кредит требует открытых денег");

            // Зеркало денежного порога: до находки ревю «≥ N ₽» тоже молча игнорировалось.
            Assert.AreEqual(60d, byId["MD03"].RequiresMoneyAtLeast, "«на счету ≥ 60₽» прочитано");
            Assert.AreEqual(40d, byId["MD04"].RequiresMoneyAtLeast, "«на счету ≥ 40₽» прочитано");

            // …и здоровье: раньше условие LT02 было зашито в Game по id карточки.
            Assert.AreEqual(50d, byId["LT02"].RequiresHealthBelow, "«если Здр<50%» прочитано из CSV");
        }

        [Test]
        public void Lt08TriggerConstants_AgreeWithItsOwnWhenColumn()
        {
            // Взвод LT08 живёт в коде (карточку надо ВСТАВИТЬ в колоду), условие показа — в CSV.
            // Разъехаться молча они больше не могут.
            var lt08 = Deck().Single(c => c.Id == "LT08");
            Assert.AreEqual((double)Game.Lt08TriggerHealthBelow, lt08.RequiresHealthBelow,
                "порог здоровья взвода LT08 = порог из её колонки «Когда»");
            Assert.AreEqual(Game.Lt08TriggerAge, lt08.RequiresMinAge,
                "…и возрастной порог тоже");
        }
    }
}
