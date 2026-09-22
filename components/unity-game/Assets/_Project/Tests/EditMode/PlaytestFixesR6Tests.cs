using System.Collections.Generic;
using NUnit.Framework;
using ThanksNoThanks;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// ГАРДЫ ПЛЕЙТЕСТ-ФИКСОВ r6 — ЧИСТАЯ ПОЛОВИНА (живой плейтест основательницы 2026-09-22).
    ///
    /// Здесь живёт то, что относится к ДВИЖКУ живого BLOCK$ (r6 п.2) и не требует экрана:
    /// фронт-событие для драйвера, исключение кредитных из живого пересчёта и поведение карточки,
    /// которая ожила и заперлась обратно. Сам пересчёт «в обе стороны» прибит в
    /// <c>GameMoneyTests</c> (две симметричные истории), экранная половина — в
    /// <c>PlayMode/PlaytestFixesR6Tests</c>.
    /// </summary>
    public class PlaytestFixesR6Tests
    {
        private const double Eps = 1e-6;

        private static Card Plain(string id, int age)
            => new Card { Id = id, Question = id + "?", Age = age, Order = age, Flags = new List<string>() };

        private static Card Block(string id, int age)
        {
            var c = Plain(id, age);
            c.IsBlockCost = true;
            return c;
        }

        private static Game NewGame(System.Func<bool> coin, params Card[] cards)
        {
            var starter = Plain("I03", 1);
            starter.StartsAgeTimer = true;
            var deck = new List<Card> { starter };
            deck.AddRange(cards);
            return new Game(deck, coin: coin);
        }

        private static void Yes(Game g) => g.HandleInput(GameInput.AnswerYes);
        private static void No(Game g) => g.HandleInput(GameInput.AnswerNo);
        private static void Crank(Game g) => g.HandleInput(GameInput.MoneyTick);

        /// <summary>Довести игру до выданной BLOCK$-карточки, будучи на мели.</summary>
        private static Game BrokeOnBlockCard(string id, out Card block)
        {
            var open = Plain("OPEN", 18);
            block = Block(id, 22);
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, block, after);
            g.StartLife(); No(g); g.Tick(2f);   // деньги открылись, на счету пусто
            No(g);                              // OPEN решён → BLOCK$-карточка выдана
            return g;
        }

        /// <summary>
        /// ФРОНТ ДЛЯ ДРАЙВЕРА ПОДНИМАЕТСЯ В ОБЕ СТОРОНЫ И РОВНО НА ПЕРЕКЛЮЧЕНИИ (r6 п.2).
        ///
        /// Живое значение драйвер мог бы и опрашивать покадрово — он так и делает подстраховкой. Но
        /// баннер «Как жаль…» и чип цены перекладывают РЕКТЫ (вплоть до бокса вопроса), и гонять это
        /// каждый кадр — лишняя работа и дрожь вёрстки. Поэтому Game обязана сообщать ПЕРЕКЛЮЧЕНИЕ,
        /// а не состояние: событие на фронте, и молчание, пока ничего не менялось.
        ///
        /// Mutation-proof: убери <c>NoteBlockedChanged()</c> из <c>Tick</c> — счётчик остаётся 0, тест
        /// краснеет на первом же ассерте. Подними событие безусловно (каждый тик) — краснеют ассерты
        /// «молчит, пока ничего не менялось».
        /// </summary>
        [Test]
        public void LiveBlocked_RaisesTheChangeEvent_OnBothEdges_AndOnlyOnEdges()
        {
            var g = BrokeOnBlockCard("FA02", out _);      // цена 10 ₽
            Assert.IsTrue(g.CurrentCardBlocked, "выдана запертой");

            int fired = 0;
            g.CardBlockedChanged += () => fired++;

            // (1) молчание, пока состояние не меняется
            g.Tick(0.2f);
            Assert.AreEqual(0, fired, "ничего не переключилось — событие молчит");

            // (2) ФРОНТ «ОЖИЛА»: накрутили выше цены
            while (g.Money < 11.0) Crank(g);
            g.Tick(0.05f);
            Assert.IsFalse(g.CurrentCardBlocked, "доступна");
            Assert.AreEqual(1, fired, "поднят ровно один фронт «ожила»");

            g.Tick(0.05f);
            Assert.AreEqual(1, fired, "и больше не повторяется, пока состояние держится");

            // (3) ФРОНТ «ЗАПЕРЛАСЬ ОБРАТНО»: стоимость жизни съела разницу
            double over = g.Money - 10.0;
            float eat = (float)(over / Game.CostOfLivingPerSec) + 0.4f;
            Assert.Less(eat, g.CardTimer, "проедание укладывается в жизнь карточки");
            g.Tick(eat);
            Assert.IsTrue(g.CurrentCardBlocked, "заперлась обратно");
            Assert.AreEqual(2, fired, "поднят второй фронт — обратная сторона тоже сообщается");
        }

        /// <summary>
        /// КРЕДИТНЫЕ НЕ ПЕРЕСЧИТЫВАЮТСЯ ЖИВЬЁМ ВОВСЕ (r6 п.2, сохранение правила r4).
        ///
        /// У кредитных уход в минус и есть содержание карточки, а взнос по ипотеке банк гейтит РОВНО
        /// ОДИН РАЗ — на показе. Живой пересчёт для них означал бы «банк передумал, пока ты думал»:
        /// одобренный кредит отбирают обратно. Поэтому у них читается вердикт выдачи и только он.
        ///
        /// Проверяется ИМЕННО РАЗНИЦА с обычной карточкой: одна и та же просадка денег обычную
        /// запирает, а кредитную — нет.
        ///
        /// Mutation-proof: убери развилку <c>IsCreditCard(CurrentCard) ? _blockedAtDeal : …</c> из
        /// <see cref="Game.CurrentCardBlocked"/> — кредитная начнёт запираться живьём, тест краснеет.
        /// </summary>
        [Test]
        public void CreditCard_IsExemptFromTheLiveRecompute_UnlikeAPlainBlockCard()
        {
            // FC02 — кредитная (ранняя ипотека, взнос 25 ₽) и одновременно BLOCK$.
            var open = Plain("OPEN", 18);
            var credit = Block("FC02", 22);
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, credit, after);

            g.StartLife(); No(g); g.Tick(2f);
            while (g.Money < 26.0) Crank(g);              // взнос по карману НА МОМЕНТ ПОКАЗА
            No(g);                                        // → FC02 выдана доступной
            Assert.AreEqual("FC02", g.CurrentCard.Id);
            Assert.IsFalse(g.CurrentCardBlocked, "кредитная выдана по карману");

            // …и проедаем счёт НИЖЕ взноса прямо под карточкой.
            double over = g.Money - 25.0;
            float eat = (float)(over / Game.CostOfLivingPerSec) + 0.4f;
            Assert.Less(eat, g.CardTimer, "проедание укладывается в жизнь карточки");
            g.Tick(eat);

            Assert.Less(g.Money, 25.0, "денег стало меньше взноса");
            Assert.IsFalse(g.CurrentCardBlocked,
                "КРЕДИТНАЯ НЕ ЗАПИРАЕТСЯ живьём — банк уже одобрил на показе");

            // Контроль: та же просадка на ОБЫЧНОЙ BLOCK$-карточке запирает её (см. GameMoneyTests) —
            // значит разница именно в кредитности, а не в том, что пересчёт вообще не сработал.
            var g2 = BrokeOnBlockCard("FA02", out _);
            Assert.IsTrue(g2.CurrentCardBlocked, "обычная BLOCK$ на мели заперта — пересчёт живой");
        }

        /// <summary>
        /// ТАЙМАУТ НА ЗАПЕРТОЙ = ПРОПУСК БЕЗ ЭФФЕКТОВ — И НА КАРТОЧКЕ, КОТОРАЯ ЗАПЕРЛАСЬ ОБРАТНО.
        ///
        /// Старый гард (<c>GameMoneyTests.BlockCard_Blocked_Timeout_SkipsCleanly…</c>) закрывает
        /// карточку, которая была заперта С ВЫДАЧИ. r6 открыл НОВЫЙ путь: карточка ожила, а потом
        /// заперлась обратно — и вот на ней таймаут обязан вести себя так же, а не «доиграть»
        /// доступность, которая была в середине её жизни.
        ///
        /// Mutation-proof, обе мутации бьют по РАЗНЫМ ассертам:
        ///   • сделай <see cref="Game.CurrentCardBlocked"/> снимком выдачи — карточка не оживёт вовсе,
        ///     и красным станет ассерт «ожила на докрученные» в середине истории;
        ///   • оставь пересчёт ОДНОСТОРОННИМ («ожить можно, запереться обратно нельзя») — карточка
        ///     доживёт до таймаута доступной, таймаут применит Δ и допишет строку некролога, и красными
        ///     станут ассерты «Δ не применена» и «строки некролога нет».
        /// </summary>
        [Test]
        public void ReBlockedCard_Timeout_SkipsWithNoDelta_NoNecrolog_NoAnswer()
        {
            var open = Plain("OPEN", 18);
            var block = Block("FA02", 22);               // 10 ₽
            block.YesDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -40) };
            block.NoDeltas = new[] { new ScaleDelta(Scale.Health, DeltaKind.Add, -40) };
            block.YesNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            block.NoNecrolog = "НЕ ДОЛЖНО ПОПАСТЬ";
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, block, after);

            g.StartLife(); No(g); g.Tick(2f);
            No(g);                                        // FA02 выдана на мели → заперта
            Assert.AreEqual("FA02", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked);

            while (g.Money < 11.0) Crank(g);              // ОЖИЛА
            g.Tick(0.05f);
            Assert.IsFalse(g.CurrentCardBlocked, "ожила на докрученные");

            double over = g.Money - 10.0;                 // …и ЗАПЕРЛАСЬ ОБРАТНО
            g.Tick((float)(over / Game.CostOfLivingPerSec) + 0.4f);
            Assert.IsTrue(g.CurrentCardBlocked, "заперлась обратно");

            int health = g.Scales.Health;
            double money = g.Money;

            // …и досиживаем остаток таймера до ТАЙМАУТА, не трогая органы.
            float left = g.CardTimer + 0.1f;
            g.Tick(left);

            Assert.AreNotEqual("FA02", g.CurrentCard?.Id, "карточка ушла по таймауту");
            Assert.AreEqual(health, g.Scales.Health, "Δ не применена");
            // Единственное, что имело право снять деньги за этот отрезок, — стоимость жизни.
            // Цена карточки (10 ₽) на фоне такого отрезка видна невооружённым глазом.
            Assert.AreEqual(money - Game.CostOfLivingPerSec * left, g.Money, 0.01,
                "цена НЕ списана — со счёта ушла только стоимость жизни");

            Yes(g);                                       // добить до финала
            int guard = 0;
            while (g.State == GameState.Playing && guard++ < 50) Yes(g);
            CollectionAssert.DoesNotContain(g.Necrolog.StoryLines, "НЕ ДОЛЖНО ПОПАСТЬ",
                "строки некролога нет — запертая ушла пропуском");
        }

        /// <summary>
        /// КРЕДИТНАЯ, ВЫДАННАЯ НА МЕЛИ, ОТПИРАЕТСЯ ЖИВЬЁМ (находка код-скептика r6, MAJOR).
        ///
        /// Первая редакция r6 читала для кредитных голый вердикт выдачи <c>_blockedAtDeal</c> — и
        /// защёлка держала В ОБЕ СТОРОНЫ. Для `FC02` (ранняя ипотека, взнос 25 ₽), показанной игроку с
        /// пустым счётом, это означало приговор: он докручивал 25 ₽ прямо под карточкой, а она
        /// оставалась запертой ДО САМОГО ТАЙМАУТА. То есть ровно та жалоба, с которой основательница
        /// пришла в r6, только спрятанная в исключении, — и прямое противоречие канону §3.5
        /// «кредитные не блокируются вовсе».
        ///
        /// Защёлка стала ОДНОСТОРОННЕЙ: она умеет удержать «не заперта» (одобренный кредит не отбирают
        /// — это сторожит <see cref="CreditCard_IsExemptFromTheLiveRecompute_UnlikeAPlainBlockCard"/>),
        /// но не умеет удержать «заперта».
        ///
        /// Mutation-proof: верни двустороннюю защёлку (<c>IsCreditCard(c) ? _blockedAtDeal : …</c>) —
        /// карточка не отопрётся ни на каких деньгах, и тест краснеет на первом же ассерте после
        /// докрутки. ПАРНЫЙ гард при этом останется зелёным — значит красное здесь ловит именно
        /// НАПРАВЛЕНИЕ защёлки, а не её наличие.
        /// </summary>
        [Test]
        public void CreditCard_DealtBroke_UNLOCKS_WhenThePlayerCranksThePrice()
        {
            var open = Plain("OPEN", 18);
            var credit = Block("FC02", 22);              // кредитная И BLOCK$ одновременно, взнос 25 ₽
            var after = Plain("AFTER", 40);
            var g = NewGame(() => false, open, credit, after);

            g.StartLife(); No(g); g.Tick(2f);            // деньги открылись, на счету пусто
            No(g);                                       // OPEN решён → FC02 выдана НА МЕЛИ
            Assert.AreEqual("FC02", g.CurrentCard.Id);
            Assert.IsTrue(g.CurrentCardBlocked, "на показе денег не было → пришла запертой");

            while (g.Money < 26.0) Crank(g);             // …игрок накрутил взнос ПРЯМО ПОД КАРТОЧКОЙ
            g.Tick(0.05f);

            Assert.AreEqual("FC02", g.CurrentCard.Id, "карточка та же — её никто не подменял");
            Assert.IsFalse(g.CurrentCardBlocked,
                "КРЕДИТНАЯ ОТПЕРЛАСЬ: защёлка выдачи односторонняя и «заперта» не держит");
            Assert.IsFalse(g.AnswerWouldSkipAsBlocked(true), "…и ДА теперь не пропуск, а настоящая покупка");

            double before = g.Money;
            Yes(g);
            Assert.AreEqual(before - 25.0, g.Money, 0.01, "взнос списан целиком — ипотека оформлена");
        }
    }
}
