using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ГАРДЫ ПЛЕЙТЕСТ-ФИКСОВ r4 (живой прогон основательницы 2026-08-08), чистая часть — та, что
    /// доказывается на `Game`/колоде без сцены. Экранная половина (тревога под §D-окном, льгота первой
    /// карточки, кадры блица) живёт в PlayMode-файле того же имени.
    ///
    /// Закрываемые пункты: п.2 (YA03 выброшена), п.3 (пол удержания отношений), п.5 (живой гейт брака).
    /// </summary>
    public class PlaytestFixesR4Tests
    {
        // ------------------------------------------------------------------ helpers

        private static List<Card> LiveDeck()
        {
            var csv = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(csv, "живая колода читается из Resources");
            return CardLoader.ParseAll(csv.text);
        }

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Starter() { var c = Plain("I03", 1); c.StartsAgeTimer = true; return c; }

        /// <summary>Карточка с множителем ПАССИВНОГО дрейфа отношений на стороне НЕТ — ровно форма
        /// `MD01` («отказались от свадьбы → DRIFT:Отн=x2», scenes.csv). durYears = 0 ⇒ бессрочно.</summary>
        private static Card DriftOnNo(string id, int age, double mult)
        {
            var c = Plain(id, age);
            c.LongEffects = new[]
            {
                new LongEffect
                {
                    Kind = LongEffectKind.Drift, Scale = Scale.Relationships,
                    MultValue = mult, DurYears = 0, OnNoSide = true,
                }
            };
            return c;
        }

        // =====================================================================================
        // п.2 — карточка YA03 «ПЕРВАЯ ЛЮБОВЬ! Начать встречаться?» выброшена СОВСЕМ
        // =====================================================================================

        /// <summary>
        /// Решение основательницы: шкала отношений появляется ПО ВОЗРАСТУ независимо от карточки, а
        /// карточка, на которую можно ответить НЕТ без последствий, — обман. Гард держит её отсутствие
        /// в ЖИВОЙ колоде (обе копии CSV читаются одним загрузчиком), чтобы её не вернули «на всякий».
        /// </summary>
        [Test]
        public void Ya03_IsGone_FromTheLiveDeck()
        {
            var all = LiveDeck();
            Assert.IsFalse(all.Any(c => c.Id == "YA03"),
                "YA03 выброшена из колоды решением основательницы (r4 п.2)");
            Assert.Greater(all.Count, 240, "остальная колода на месте — выброшена ровно одна карточка");
        }

        /// <summary>
        /// Обратная сторона того же решения: раз карточки-открывашки больше нет, шкала обязана
        /// открыться САМА в канон-возраст 20. Mutation-proof к «убрали карточку и забыли про открытие»:
        /// если кто-то вернёт удержание `HeldByItsOwnCard` для Отн, шкала не откроется и тест покраснеет.
        /// </summary>
        [Test]
        public void Relationships_OpenByAgeAlone_WithNoCardToAnswer()
        {
            // Колода без единого OPEN:Отн: стартер + обычная карточка на 22.
            var g = new Game(new List<Card> { Starter(), Plain("A", 22) }, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);          // разрешить стартер → возраст побежал

            int guard = 0;
            while (!g.RelationshipsOpen && g.State == GameState.Playing && guard++ < 4000)
                g.Tick(0.05f);

            Assert.IsTrue(g.RelationshipsOpen, "шкала отношений открылась по возрасту, без карточки");
            Assert.GreaterOrEqual(g.Age, Game.RelationshipsOpenAge, "…и не раньше канон-возраста 20");
        }

        // =====================================================================================
        // п.3 — удержание ↑ вытягивает ≥2 %/с нетто ПРИ ЛЮБОМ легальном множителе дрейфа
        // =====================================================================================

        /// <summary>
        /// Прогнать N секунд с зажатой осью и вернуть НЕТТО-скорость, %/с.
        /// Время считается ТОЛЬКО пока забег жив: иначе доехавшая до финала колода растворила бы
        /// измерение в нулях и тест врал бы в безопасную сторону.
        /// </summary>
        private static double HoldRate(Game g, int dir, float seconds, float dt = 0.05f)
        {
            int before = g.Scales.Relationships;
            int steps = Mathf.RoundToInt(seconds / dt);
            int lived = 0;
            for (int i = 0; i < steps; i++)
            {
                if (g.State != GameState.Playing || !g.RelationshipsOpen) break;
                if (dir != 0) g.HandleInput(dir > 0 ? GameInput.RelationUp : GameInput.RelationDown);
                g.Tick(dt);
                lived++;
            }
            Assert.Greater(lived, steps / 2, "забег прожил измерение — иначе замер недостоверен");
            return (g.Scales.Relationships - before) / (double)(lived * dt);
        }

        /// <summary>
        /// Игра с ВЗВЕДЁННЫМ множителем дрейфа отношений. Карточка-носитель стоит ПОСЛЕ открытия шкалы
        /// (20) — иначе `Game` законно ОТБРАСЫВАЕТ эффект по закрытой шкале (правило «Δ только по
        /// открытым шкалам», см. ApplyLongEffects). По дороге к ней ось держим ВВЕРХ, иначе дрейф
        /// доводит до разрыва и мерить становится нечего.
        /// </summary>
        private static Game GameWithRelDrift(double mult)
        {
            var deck = new List<Card> { Starter() };
            for (int age = 5; age < 30; age++) deck.Add(Plain("F" + age, age));
            deck.Add(DriftOnNo("DR", 30, mult));
            for (int age = 31; age < 80; age++) deck.Add(Plain("T" + age, age));

            var g = new Game(deck, coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);

            DriveTo(g, "DR");
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("DR"), "предусловие: дошли до карточки-носителя");
            Assume.That(g.RelationshipsOpen, Is.True, "предусловие: шкала открыта (иначе эффект отбросят)");

            g.HandleInput(GameInput.AnswerNo);       // НЕТ → множитель взведён
            Assert.AreEqual(Game.RelDriftPerSec * mult, g.RelationshipDriftPerSec, 1e-6,
                "множитель дрейфа действительно взведён — иначе тест ничего не проверяет");
            return g;
        }

        /// <summary>
        /// Доехать до карточки с заданным id, НЕ отвечая: возраст «догоняет» возраст текущей карточки
        /// (<see cref="Game.AgeCatchUpPerSecond"/>), поэтому мгновенные ответы обгоняют его, и карточка
        /// на 30 лет оказывается отрезана возрастным гейтом. Пусть колоду двигают ТАЙМАУТЫ — тогда
        /// возраст успевает. По дороге держим рычаг и датчик: без них забег умирает по дороге.
        /// </summary>
        private static void DriveTo(Game g, string id)
        {
            int guard = 0;
            while (g.CurrentCard != null && g.CurrentCard.Id != id
                   && g.State == GameState.Playing && guard++ < 200000)
            {
                if (g.RelationshipsOpen) g.HandleInput(GameInput.RelationUp);   // не дать разорваться
                g.HandleInput(GameInput.EnergyHold);                            // …и не умереть по пути
                g.Tick(0.05f);
            }
        }

        /// <summary>Опустить маркер к заданному уровню, чтобы у тяги вверх был ход (и потолок 100 не
        /// срезал измерение). Останавливаемся заметно выше порога разрыва.</summary>
        private static void SettleDownTo(Game g, int target)
        {
            int guard = 0;
            while (g.Scales.Relationships > target && g.State == GameState.Playing && guard++ < 20000)
            {
                g.HandleInput(GameInput.RelationDown);
                g.Tick(0.05f);
            }
        }

        /// <summary>
        /// ЖАЛОБА ОСНОВАТЕЛЬНИЦЫ: «джойстиком двигаю — шкала отношений не растёт». Корень —
        /// `DRIFT:Отн=x2` от `MD01`-НЕТ, который в CSV идёт БЕЗ `DUR`, то есть навсегда: дрейф 1.6 → 3.2,
        /// а тяга вверх всего 4.0, нетто +0.8 %/с ≈ «стоит на месте».
        ///
        /// Гард держит ИНВАРИАНТ МЕХАНИКИ, а не сегодняшнее число: при ЛЮБОМ легальном множителе
        /// (включая стак ×2·×2, где без пола нетто было бы ОТРИЦАТЕЛЬНЫМ) активное удержание ↑ тянет
        /// вверх не слабее <see cref="Game.RelHoldNetFloorPerSec"/>. Mutation-proof: убери пол — при
        /// ×2 выйдет 0.8, при ×4 отрицательное, оба ниже порога.
        /// </summary>
        [TestCase(1.0, TestName = "без множителя")]
        [TestCase(2.0, TestName = "MD01=НЕТ (x2, бессрочный)")]
        [TestCase(4.0, TestName = "стак множителей (x4)")]
        public void HoldingUp_PullsAtLeastTheFloor_UnderAnyDriftMultiplier(double mult)
        {
            var g = GameWithRelDrift(mult);
            SettleDownTo(g, 50);                         // запас хода вверх, вдали от порога разрыва

            double rate = HoldRate(g, +1, 5f);
            Assert.GreaterOrEqual(rate, Game.RelHoldNetFloorPerSec - 0.25,
                $"удержание ↑ при множителе ×{mult} обязано тянуть ≥{Game.RelHoldNetFloorPerSec} %/с "
                + $"нетто (замерено {rate:F2})");
        }

        /// <summary>
        /// ОБРАТНАЯ СТОРОНА ПОЛА — наказание БЕЗДЕЙСТВИЯ не тронуто. Пол работает только пока игрок
        /// ТЯНЕТ ВВЕРХ; отпустил рычаг — множитель карточки действует целиком, и отношения тают ровно
        /// так быстро, как обещает её проза («тает даже при поддержке»).
        /// Mutation-proof к ленивой правке «просто не давать шкале падать»: если пол наложить
        /// безусловно, бездействие перестанет наказывать и тест покраснеет.
        /// </summary>
        [Test]
        public void Idling_StillFallsAtTheFullMultiplier_TheFloorIsForActiveHoldingOnly()
        {
            var g = GameWithRelDrift(2.0);
            SettleDownTo(g, 70);                         // подальше от потолка, чтобы падение было видно

            double rate = HoldRate(g, 0, 5f);
            Assert.Less(rate, -Game.RelDriftPerSec,
                $"без рычага шкала падает по ПОЛНОМУ множителю (замерено {rate:F2} %/с), пол её не держит");
        }

        // =====================================================================================
        // п.1б — ПЕРВАЯ КАРТОЧКА ПОСЛЕ ОБУЧЕНИЯ ДЕНЬГАМ ГАРАНТИРОВАННО ПО КАРМАНУ
        // =====================================================================================

        /// <summary>BLOCK$-карточка с настоящей ценой из <see cref="Game.BlockPrices"/>.</summary>
        private static Card Block(string id, int age)
        {
            var c = Plain(id, age);
            c.IsBlockCost = true;
            return c;
        }

        /// <summary>
        /// …И ТО ЖЕ САМОЕ В КОНТЕНТ-ДОКАХ (находка код-скептика r4). Рантайм-CSV почистили, а
        /// `docs/new_concept/scenes.html` — СОДЕРЖАТЕЛЬНЫЙ КОНТРАКТ по `docs/DESIGN_CONTRACT.md`: это
        /// просмотрщик колоды, которым живут дизайнер и контент-агент. Пока удалённая карточка лежит там
        /// (плюс живые ссылки в `host-content.md` и `scenes-table.md`), в игре её нет, а в доках она есть —
        /// и следующая контентная сессия «восстановит» её как потерянную.
        ///
        /// Прецедент чтения репозиторных доков из теста — `NewScaleCanonTextTests`.
        /// MUTATION-PROOF: вернуть строку `YA03` в любой из трёх файлов — тест краснеет с именем файла.
        /// </summary>
        [Test]
        public void Ya03_IsGone_FromTheContentDocsToo()
        {
            string docs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs"));
            Assume.That(Directory.Exists(docs), Is.True, "доки репозитория на месте: " + docs);

            foreach (var rel in new[] { "new_concept/scenes.html", "new_concept/host-content.md",
                                        "new_concept/scenes-table.md", "new_concept/game-design.md" })
            {
                string path = Path.Combine(docs, rel);
                Assert.IsTrue(File.Exists(path), rel + " на месте");
                foreach (var line in File.ReadAllLines(path))
                {
                    if (!line.Contains("YA03")) continue;
                    // Единственная законная форма — ПАМЯТКА о том, что карточка снята (строка про снятие).
                    Assert.IsTrue(line.Contains("снят"),
                        $"{rel}: строка «{line.Trim()}» всё ещё подаёт YA03 как существующую карточку. "
                        + "Карточка выброшена решением основательницы 2026-08-08 — контент-доки обязаны "
                        + "совпадать с CSV, иначе дизайнер видит удалённую карточку как живую");
                }
            }
        }

        /// <summary>Карточка-открывашка шкалы: несёт флаг <c>OPEN:{scale}</c>, как `YA01` для денег.</summary>
        private static Card Opener(string id, int age, string scale)
        {
            var c = Plain(id, age);
            c.Flags = new List<string> { "OPEN:" + scale };
            return c;
        }

        /// <summary>
        /// ЖАЛОБА ОСНОВАТЕЛЬНИЦЫ: «сразу после туториала выпала карточка, на которую нет денег».
        ///
        /// ⚠ ЭТОТ ТЕСТ ПЕРЕПИСАН НА РЕАЛЬНЫЙ ПОРЯДОК СОБЫТИЙ (находка код-скептика r4). Первая редакция
        /// взводила льготу ДО <c>Advance</c> — то есть проверяла порядок, которого в игре не бывает, и
        /// была ЗЕЛЁНОЙ при живой жалобе. Настоящая последовательность такая:
        ///   1. `I03` (стартер) → игрок отвечает, возраст побежал;
        ///   2. `YA01` — открывашка денег (`OPEN:Дн`): пока она на экране, <c>CheckMoneyOpen</c> держит
        ///      шкалу закрытой («сначала ответь на СВОЮ карточку»);
        ///   3. игрок отвечает на `YA01` ⇒ <c>Advance</c> УЖЕ выдаёт `FA05` (BLOCK$, цена 25 ₽) и
        ///      фиксирует <c>CurrentCardBlocked = true</c> — денег на счету 0;
        ///   4. и только СЛЕДУЮЩИМ тиком <c>CheckMoneyOpen</c> открывает шкалу и поднимает §D-окно
        ///      ПОВЕРХ уже выданной запертой карточки;
        ///   5. игрок крутит крутилку, окно закрывается — драйвер зовёт <c>ArmAffordableNextCard</c>.
        /// К шагу 5 запертая дверь уже стоит на экране, и льготы «на следующую выдачу» мало.
        ///
        /// Гард держит ОБА свойства фикса: текущая заблокированная карточка ПЕРЕОФОРМЛЯЕТСЯ, и таймер
        /// у новой честно полный (иначе она доигрывала бы чужие секунды).
        ///
        /// MUTATION-PROOF: убрать переоформление текущей (оставить только взвод флага) — тест краснеет
        /// на первом же ассерте: `FA05` остаётся на экране заблокированной.
        /// </summary>
        [Test]
        public void AfterTheMoneyTutorial_TheAlreadyDealtBlockedCard_IsReplaced_NotJustTheNextOne()
        {
            Assume.That(Game.BlockPrices.TryGetValue("FA05", out var price), Is.True, "FA05 — BLOCK$ с ценой");

            var g = new Game(new List<Card>
            {
                Starter(),
                Opener("YA01", 18, Card.OpenMoney),   // …открывашка денег: держит шкалу до ответа
                Block("FA05", 18),                    // …и следом BLOCK$ того же возраста — она и выпадала
                Plain("FREE", 18),
                Plain("TAIL", 19),
            }, coin: () => false);

            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);        // I03 → следом сразу открывашка денег
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("YA01"), "предусловие: открывашка денег на экране");
            // Возраст «догоняет» карточку (AgeCatchUpPerSecond), поэтому 18 наступает ПОКА YA01 висит:
            // ровно так, как в живой колоде. Шкалу при этом держит HeldByItsOwnCard.
            int guard = 0;
            while (g.Age < Game.MoneyOpenAge && g.CurrentCard?.Id == "YA01"
                   && g.State == GameState.Playing && guard++ < 4000) g.Tick(0.02f);
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("YA01"), "предусловие: YA01 ещё не истекла по таймеру");
            Assume.That(g.Age, Is.GreaterThanOrEqualTo(Game.MoneyOpenAge), "предусловие: возраст догнал 18");
            Assume.That(g.MoneyOpen, Is.False, "…и шкала ЕЩЁ закрыта — её держит собственная карточка");

            g.HandleInput(GameInput.AnswerNo);        // ответ на YA01 ⇒ Advance выдаёт FA05
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("FA05"), "предусловие: BLOCK$ выдана СРАЗУ");
            Assume.That(g.CurrentCardBlocked, Is.True,
                $"предусловие: на счету {g.Money:F1} ₽ против цены {price} ₽ — карточка пришла запертой");

            g.Tick(0.02f);                            // …и только теперь открывается шкала (§D-окно)
            Assume.That(g.MoneyOpen, Is.True, "предусловие: обучение деньгам поднялось ПОВЕРХ FA05");
            float timerBefore = g.CardTimer;
            // Условие выхода из §D-окна — MoneyTutorialTicks принятых тиков крутилки. Берём ровно его:
            // за семь тиков на цену FA05 (25 ₽) не набирается, то есть дверь остаётся запертой.
            for (int i = 0; i < GameDriver.MoneyTutorialTicks; i++) g.HandleInput(GameInput.MoneyTick);
            Assume.That(g.Money, Is.LessThan(price), "предусловие: семь тиков цену не покрыли");

            g.ArmAffordableNextCard();                // ровно то, что делает драйвер на закрытии §D-окна

            Assert.AreNotEqual("FA05", g.CurrentCard?.Id,
                "ЗАКРЫЛИ ОБУЧЕНИЕ — а на экране всё та же запертая карточка. Льгота обязана переоформить "
                + "и ТЕКУЩУЮ выдачу, иначе жалоба основательницы («сразу после туториала выпала карточка, "
                + "на которую нет денег») остаётся живой (r4 п.1б, находка код-скептика)");
            Assert.AreEqual("FREE", g.CurrentCard?.Id,
                "…и подмена идёт ТЕМ ЖЕ правилом отбора — следующая доступная по колоде");
            Assert.IsFalse(g.CurrentCardBlocked, "…новая карточка не заперта");
            Assert.AreEqual(g.CardTimerMax, g.CardTimer, 1e-3f,
                $"…и таймер у неё ЧЕСТНО полный ({g.CardTimer:F2} из {g.CardTimerMax:F2}), а не доигранный "
                + $"остаток от FA05 ({timerBefore:F2} с)");
        }

        /// <summary>
        /// ОБРАТНАЯ СТОРОНА ТОГО ЖЕ: переоформление — это ПОДМЕНА ДО ВЗАИМОДЕЙСТВИЯ, а не ответ и не
        /// пропуск ответа. У пропущенной карточки не должно остаться НИ ОДНОГО следа: ни записи ответа
        /// (по ней строятся чейны), ни строки некролога. Иначе игрок «ответил» на то, чего не видел.
        /// </summary>
        [Test]
        public void TheReplacedCard_LeavesNoTrace_NoAnswer_NoNecrolog()
        {
            var block = Block("FA05", 18);
            block.YesNecrolog = "Автошколу закончили с первого раза.";
            block.NoNecrolog = "За руль так и не сели.";
            var g = new Game(new List<Card>
            {
                Starter(), Opener("YA01", 18, Card.OpenMoney), block, Plain("FREE", 18), Plain("TAIL", 19),
            }, coin: () => false);

            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);
            int guard = 0;
            while (g.Age < Game.MoneyOpenAge && g.CurrentCard?.Id == "YA01"
                   && g.State == GameState.Playing && guard++ < 4000) g.Tick(0.02f);
            g.HandleInput(GameInput.AnswerNo);
            g.Tick(0.02f);
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("FA05"), "предусловие: FA05 на экране");
            Assume.That(g.CurrentCardBlocked, Is.True, "предусловие: …и заперта");

            double moneyBefore = g.Money;
            g.ArmAffordableNextCard();

            Assert.AreNotEqual("FA05", g.CurrentCard?.Id, "предпосылка теста: карточка переоформлена");
            Assert.AreEqual(moneyBefore, g.Money, 1e-6,
                "…и со счёта НЕ списана цена: подмена случилась ДО взаимодействия, это не ответ «ДА»");

            int guard2 = 0;
            while (g.State == GameState.Playing && guard2++ < 40000)
            {
                if (g.CurrentCard != null) g.HandleInput(GameInput.AnswerNo); else g.Tick(0.05f);
            }
            var lines = string.Join(" | ", g.Necrolog.StoryLines);
            StringAssert.DoesNotContain("Автошколу", lines, "…и НЕТ строки некролога от её ДА");
            StringAssert.DoesNotContain("За руль", lines,
                "…и НЕТ строки некролога от её НЕТ: игрок этой карточки не видел, значит и выбора не делал");
        }

        /// <summary>
        /// ВТОРАЯ ПОЛОВИНА ЛЬГОТЫ — «СЛЕДУЮЩАЯ ВЫДАЧА». Она нужна ровно тогда, когда переоформлять
        /// нечего: обучение закрылось на ОБЫЧНОЙ карточке, а BLOCK$ стоит сразу за ней. Этот гард и
        /// стережёт тот случай, плюс ОДНОРАЗОВОСТЬ: дальше BLOCK$ работает как работал, иначе «нет денег»
        /// исчезло бы из игры вообще.
        ///
        /// ⚠ ЧЕСТНО: именно эта постановка (взвод ДО <c>Advance</c>) и оказалась ВАКУУМНОЙ как репро
        /// жалобы — реальный порядок другой, и его держит тест выше. Здесь она законна, потому что
        /// проверяет ДРУГОЕ свойство: флаг, доживающий до следующей выдачи.
        /// </summary>
        [Test]
        public void TheArmedGrace_SkipsAnUnaffordableCard_ThenIsSpent()
        {
            Assume.That(Game.BlockPrices.ContainsKey("MD03"), Is.True, "MD03 — BLOCK$ с ценой");

            var g = new Game(new List<Card>
            {
                Starter(), Block("MD03", 5), Plain("FREE", 6), Block("MD04", 7), Plain("TAIL", 8),
            }, coin: () => false);

            g.StartLife();
            Assume.That(g.CurrentCardBlocked, Is.False, "предусловие: текущая карточка НЕ заперта — "
                + "переоформлять нечего, работает только взвод на следующую выдачу");
            g.ArmAffordableNextCard();
            g.HandleInput(GameInput.AnswerNo);  // разрешить стартер → выдаётся СЛЕДУЮЩАЯ карточка

            Assert.AreEqual("FREE", g.CurrentCard.Id,
                "льгота пропустила неподъёмную BLOCK$-карточку и выдала доступную (r4 п.1б)");
            Assert.IsFalse(g.CurrentCardBlocked, "…и она действительно не заблокирована");

            // Льгота ОДНОРАЗОВАЯ: следующая неподъёмная BLOCK$ приходит как обычно.
            g.HandleInput(GameInput.AnswerNo);
            Assert.AreEqual("MD04", g.CurrentCard.Id, "льгота потрачена — BLOCK$ снова выдаётся");
            Assert.IsTrue(g.CurrentCardBlocked, "…и снова блокирует, как и было задумано");
        }

        /// <summary>
        /// Льгота — про ДЕНЬГИ, а не про «пропусти что-нибудь». Если игрок богат, она не имеет права
        /// ничего менять: BLOCK$-карточка по карману выдаётся первой же, как и без льготы.
        /// </summary>
        [Test]
        public void TheGrace_ChangesNothing_WhenThePlayerCanAfford()
        {
            // Деньги открываются в 18, поэтому копим на карточке-предшественнице, а BLOCK$ ставим позже.
            var g = new Game(new List<Card> { Starter(), Plain("OPEN", 18), Block("MD03", 30), Plain("TAIL", 40) },
                             coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);           // стартер → возраст побежал
            int guard = 0;
            while (!g.MoneyOpen && g.State == GameState.Playing && guard++ < 20000) g.Tick(0.05f);
            Assume.That(g.MoneyOpen, Is.True, "предусловие: деньги открыты");

            for (int i = 0; i < 400; i++) g.HandleInput(GameInput.MoneyTick);   // накрутить с запасом
            Assume.That(g.Money, Is.GreaterThan(Game.BlockPrices["MD03"]), "предусловие: денег хватает");

            g.ArmAffordableNextCard();
            g.HandleInput(GameInput.AnswerNo);

            Assert.AreEqual("MD03", g.CurrentCard.Id,
                "по карману — значит выдаётся, льгота не пропускает платные карточки просто так");
            Assert.IsFalse(g.CurrentCardBlocked, "и не заблокирована");
        }

        // =====================================================================================
        // п.5 — свадебная ветка уходит целиком после развода (живой гейт поверх чейна ответов)
        // =====================================================================================

        /// <summary>Карточки, которым живой гейт брака ОБЯЗАТЕЛЕН: чистая свадебная атрибутика.</summary>
        private static readonly string[] MarriageGated =
            { "MD11", "MD12", "MD22", "MD23", "MD24", "MD25", "MD26" };

        /// <summary>
        /// Карточки ветки РЕБЁНКА: у них гейт ТОЛЬКО по истории (`MD02=ДА`) и живого гейта брака быть
        /// НЕ ДОЛЖНО. Прямое требование основательницы «ребёнок не исчезает при разводе — НЕ перегибать».
        /// Внуки (LT10) здесь же и по той же причине: дети развод пережили ⇒ внуки легальны.
        /// </summary>
        private static readonly string[] ChildGated =
            { "MD02", "FC37", "FC38", "FC39", "FC40", "FC41", "LT04", "LT10", "LT11", "LT12", "LT13", "LT14" };

        [Test]
        public void WeddingBranch_CarriesTheLiveMarriageGate()
        {
            var byId = LiveDeck().ToDictionary(c => c.Id);
            foreach (var id in MarriageGated)
            {
                Assert.IsTrue(byId.ContainsKey(id), $"{id} есть в колоде");
                Assert.IsTrue(byId[id].RequiresMarried,
                    $"{id} — свадебная ветка, ей нужен ЖИВОЙ гейт «если в браке» поверх чейна ответов "
                    + "(иначе приходит через годы после развода — жалоба основательницы r4 п.5)");
            }
        }

        [Test]
        public void ChildBranch_KeepsOnlyItsAnswerHistoryGate_DivorceDoesNotEraseTheChild()
        {
            var byId = LiveDeck().ToDictionary(c => c.Id);
            foreach (var id in ChildGated)
            {
                Assert.IsTrue(byId.ContainsKey(id), $"{id} есть в колоде");
                Assert.IsFalse(byId[id].RequiresMarried,
                    $"{id} — ветка РЕБЁНКА, а не брака: развод её не отменяет («НЕ перегибать», r4 п.5)");
            }
        }

        /// <summary>
        /// ЖИВОЙ ПРОГОН ЖАЛОБЫ. Игрок женился (`MD01`=ДА), потом расстался — и «Брак на износе» обязан
        /// перестать приходить, хотя в истории ответов `MD01`=ДА остался навсегда.
        /// Mutation-proof: без `MarriedGatedOff` в фильтре выдачи карточка выпадет и тест покраснеет.
        /// </summary>
        [Test]
        public void WhileNotMarried_AMarriageGatedCard_IsNeverDealt()
        {
            // Синтетическая пара «гейтованная / контрольная» на одном возрасте: отличие между ними —
            // ровно один флаг, поэтому тест бьёт точно в гейт и не спотыкается о пейсинг живой колоды.
            var gated = Plain("WED", 5); gated.RequiresMarried = true;
            var control = Plain("FREE", 5);

            var g = new Game(new List<Card> { Starter(), gated, control, Plain("TAIL", 8) },
                             coin: () => false);
            g.StartLife();
            Assume.That(g.Married, Is.False, "предусловие: игрок не в браке");
            g.HandleInput(GameInput.AnswerNo);          // стартер разрешён → выдаётся следующая

            Assert.AreEqual("FREE", g.CurrentCard.Id,
                "карточка свадебной ветки НЕ выдаётся вне живого брака — она пропущена (r4 п.5)");

            // …и не всплывает до конца забега ни разу.
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 5000)
            {
                Assert.AreNotEqual("WED", g.CurrentCard?.Id,
                    "свадебная ветка не приходит вне брака НИ РАЗУ");
                g.HandleInput(GameInput.AnswerNo);
                g.Tick(0.05f);
            }
        }

        /// <summary>
        /// СЕРДЦЕ ЖАЛОБЫ: история ответов помнит `MD01`=ДА ВЕЧНО, а живой гейт смотрит на «сейчас».
        /// Здесь игрок реально женится на живой карточке `MD01` из колоды, потом теряет партнёра —
        /// и пак «Брак на износе» обязан замолчать, хотя чейн-гейт по истории по-прежнему открыт.
        /// Mutation-proof: убери `MarriedGatedOff` из фильтра выдачи — карточка выпадет и тест покраснеет.
        /// </summary>
        [Test]
        public void AfterTheBreakup_TheHistoryGateAlone_NoLongerLetsTheBranchThrough()
        {
            // Гейты берём с ЖИВОЙ карточки «Брак на износе», а возраст — синтетический: настоящая MD22
            // стоит на 40–55, и короткий забег до неё просто не доезжает, отчего тест молчал бы даже с
            // выключённым гейтом (проверено мутацией — он был ЗЕЛЁНЫМ без MarriedGatedOff).
            // То, что у живых карточек эти флаги действительно проставлены, держит
            // <see cref="WeddingBranch_CarriesTheLiveMarriageGate"/>; здесь проверяется ПОВЕДЕНИЕ.
            var live = LiveDeck().ToDictionary(c => c.Id)["MD22"];
            Assume.That(live.RequiresMarried, Is.True, "предусловие: у живой карточки есть живой гейт");
            Assume.That(live.RequiresParentYes, Is.EqualTo("MD01"),
                "предусловие: чейн-гейт по истории на месте — проверяем, что ЖИВОЙ добавлен ПОВЕРХ");

            var wornOut = Plain("MD22", 22);
            wornOut.RequiresMarried = live.RequiresMarried;
            wornOut.RequiresParentYes = live.RequiresParentYes;

            // Синтетическая свадьба: та же механика (`ApplyCardSpecial` по id `MD01`), но на возрасте,
            // до которого забег доезжает без борьбы с пейсингом живой колоды.
            var wedding = Plain("MD01", 21);

            // РАЗРЫВ КАРТОЧКОЙ (`BREAK:Отн`), а не дрейфом. Дрейфовый разрыв копится ~10 секунд красной
            // зоны, и всё это время колода едет по таймаутам — слот ветки успевает проехать ДО развода,
            // и тест перестаёт проверять то, ради чего написан. Карточный разрыв мгновенный.
            var breaker = Plain("BREAK", 22);
            breaker.BreaksRelationships = true;

            // Ветка и «AFTER» стоят на ТОМ ЖЕ возрасте, что и разрыв: иначе пропуск ветки приводит нас
            // к следующей карточке СЛИШКОМ РАНО и её срезает возрастной гейт — маркер «место пройдено»
            // не сработал бы по причине, не имеющей к брачному гейту никакого отношения.
            var g = new Game(new List<Card> { Starter(), wedding, breaker, wornOut, Plain("AFTER", 22), Plain("TAIL", 26) },
                             coin: () => false);
            g.StartLife();
            g.HandleInput(GameInput.AnswerNo);

            DriveTo(g, "MD01");
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("MD01"), "предусловие: дошли до свадьбы");

            // Возраст «догоняет» карточку не мгновенно (12 лет/с), а шкала отношений открывается по
            // ВОЗРАСТУ — дать ей открыться, пока свадьба ещё на экране.
            int settle = 0;
            while (!g.RelationshipsOpen && g.CurrentCard?.Id == "MD01"
                   && g.State == GameState.Playing && settle++ < 2000)
                g.Tick(0.02f);
            Assume.That(g.RelationshipsOpen, Is.True, "предусловие: шкала открыта (брак её требует)");

            g.HandleInput(GameInput.AnswerYes);         // СВАДЬБА — ДА
            Assert.IsTrue(g.Married, "поженились — и история ответов это запомнила навсегда");

            // …и расстались — карточкой `BREAK:Отн`, мгновенно и не двигая колоду.
            DriveTo(g, "BREAK");
            Assume.That(g.CurrentCard?.Id, Is.EqualTo("BREAK"), "предусловие: дошли до карточки разрыва");
            g.HandleInput(GameInput.AnswerYes);
            Assert.IsFalse(g.Married, "развод/разрыв состоялся");
            Assert.IsTrue(g.RelationshipsLost, "…шкала отношений потеряна");
            int guard;

            // Докрутить забег таймаутами: карточка ветки не имеет права выпасть НИ РАЗУ…
            // Игрока по дороге ДЕРЖИМ ЖИВЫМ (датчик + крутилка): если он умрёт до места, где стояла
            // ветка, «не выпала» перестанет что-либо значить — ровно этим прежняя редакция и молчала
            // под мутацией.
            bool sawAfter = false;
            guard = 0;   // переиспользуем счётчик
            while (g.State == GameState.Playing && !sawAfter && guard++ < 200000)
            {
                Assert.AreNotEqual("MD22", g.CurrentCard?.Id,
                    "после развода пак «Брак на износе» приходить НЕ должен (r4 п.5)");
                if (g.CurrentCard?.Id == "AFTER") sawAfter = true;
                g.HandleInput(GameInput.EnergyHold);
                g.HandleInput(GameInput.MoneyTick);
                g.Tick(0.05f);
            }

            // …и тест обязан ДОЕХАТЬ до карточки ЗА пропущенной.
            Assert.IsTrue(sawAfter,
                $"забег дошёл до карточки ЗА пропущенной (возраст {g.Age:F0}, состояние {g.State}) — "
                + "значит место ветки действительно пройдено");
        }
    }
}
