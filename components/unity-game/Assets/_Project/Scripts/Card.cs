using System.Collections.Generic;

namespace LifeChoices
{
    /// <summary>
    /// One decision card. Effects are the scale deltas for each side.
    /// A card may have a lethal side (DeathSide): ☠ cards are instant deaths,
    /// the old-age "ещё одну серию" card is a peaceful natural death.
    /// </summary>
    public sealed class Card
    {
        public string Id;
        public Stage Stage;
        public string Title;
        public Effect Yes;
        public Effect No;
        public Window Window;

        /// <summary>Side that ends the life, or Side.None.</summary>
        public Side DeathSide = Side.None;
        /// <summary>True → the death is peaceful/natural (not a ☠ instant death).</summary>
        public bool PeacefulDeath;
        /// <summary>Cause text shown when the death side is chosen.</summary>
        public string DeathCause;
        /// <summary>🔗 conditional-unlock card (unlock logic deferred this increment).</summary>
        public bool Linked;

        public bool HasDeathSide => DeathSide != Side.None;
        public bool IsInstantDeath => HasDeathSide && !PeacefulDeath;
    }

    /// <summary>
    /// The static ~40-card content set, faithfully encoded from
    /// docs/mvp-content.md. Letters map: Н→Mood, З→Health, Д→Money, Л→People.
    /// </summary>
    public static class CardDatabase
    {
        // Effect helper: E(Настроение, Здоровье, Деньги, Люди).
        private static Effect E(int mood, int health, int money, int people) =>
            new Effect(mood, health, money, people);

        public static List<Card> ForStage(Stage stage)
        {
            var list = new List<Card>();
            foreach (var c in All())
                if (c.Stage == stage) list.Add(c);
            return list;
        }

        public static List<Card> All()
        {
            var cards = new List<Card>();

            // ---------- Этап 1 — Детство ----------
            cards.Add(new Card { Id = "1.1", Stage = Stage.Childhood, Title = "Съесть жука на спор", Yes = E(10, -8, 0, 8), No = E(-4, 0, 0, -6), Window = Window.S });
            cards.Add(new Card { Id = "1.2", Stage = Stage.Childhood, Title = "Поделиться завтраком с новеньким", Yes = E(6, 0, -4, 12), No = E(0, 0, 4, -8), Window = Window.S });
            cards.Add(new Card { Id = "1.3", Stage = Stage.Childhood, Title = "Признаться, что разбил мамину вазу", Yes = E(8, 0, -6, 6), No = E(-10, 0, 0, -4), Window = Window.M });
            cards.Add(new Card { Id = "1.4", Stage = Stage.Childhood, Title = "Пойти в музыкалку (мама настаивает)", Yes = E(-8, 0, 0, 8), No = E(8, 0, 0, -8), Window = Window.M });
            cards.Add(new Card { Id = "1.5", Stage = Stage.Childhood, Title = "Дёрнуть кота за хвост", Yes = E(6, -10, 0, -4), No = E(-2, 0, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "1.6", Stage = Stage.Childhood, Title = "Съесть все конфеты разом", Yes = E(14, -12, 0, 0), No = E(-6, 4, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "1.7", Stage = Stage.Childhood, Title = "Подраться за место на горке", Yes = E(6, -8, 0, 8), No = E(-4, 0, 0, -4), Window = Window.S });
            cards.Add(new Card { Id = "1.8", Stage = Stage.Childhood, Title = "Пообещать жениться на девочке из песочницы", Yes = E(8, 0, 0, 12), No = E(-4, 0, 0, 0), Window = Window.S, Linked = true });
            cards.Add(new Card { Id = "1.9", Stage = Stage.Childhood, Title = "Обменять молочный зуб бабушке на деньги (вырвать пораньше)", Yes = E(8, -6, 10, 0), No = E(-2, 0, 0, 0), Window = Window.M });
            cards.Add(new Card { Id = "1.10", Stage = Stage.Childhood, Title = "Сунуть вилку в розетку «на слабо»", Yes = Effect.Zero, No = E(4, 0, 0, 6), Window = Window.S, DeathSide = Side.Yes, DeathCause = "Не успел вырасти: сунул вилку в розетку «на слабо»." });

            // ---------- Этап 2 — Юность ----------
            cards.Add(new Card { Id = "2.1", Stage = Stage.Youth, Title = "Поступить на юрфак (мама хочет)", Yes = E(-10, 0, 8, 6), No = E(12, 0, 0, -8), Window = Window.M });
            cards.Add(new Card { Id = "2.2", Stage = Stage.Youth, Title = "Взять кредит на новый айфон (мелкий шрифт)", Yes = E(14, 0, -20, 0), No = E(-6, 0, 2, 0), Window = Window.L });
            cards.Add(new Card { Id = "2.3", Stage = Stage.Youth, Title = "Уйти на вписку вместо экзамена", Yes = E(14, -6, 0, 10), No = E(-8, 0, 0, -6), Window = Window.S });
            cards.Add(new Card { Id = "2.4", Stage = Stage.Youth, Title = "Записаться в ЗОЖ-секту к инфлюенсеру", Yes = E(-6, 16, -8, 0), No = E(4, -4, 0, 0), Window = Window.M });
            cards.Add(new Card { Id = "2.5", Stage = Stage.Youth, Title = "Серьёзные отношения на 3 года", Yes = E(6, 0, -8, 16), No = E(8, 0, 0, -10), Window = Window.M });
            cards.Add(new Card { Id = "2.6", Stage = Stage.Youth, Title = "Продать почку на топовую видеокарту", Yes = E(10, -30, 25, 0), No = E(-4, 0, 0, 0), Window = Window.L });
            cards.Add(new Card { Id = "2.7", Stage = Stage.Youth, Title = "Пойти курьером ради денег", Yes = E(-6, -8, 14, 0), No = E(4, 0, -6, 0), Window = Window.S });
            cards.Add(new Card { Id = "2.8", Stage = Stage.Youth, Title = "Всё бросить и уйти в стартап «убийца всего»", Yes = E(10, 0, -15, -4), No = E(-6, 0, 6, 0), Window = Window.M });
            cards.Add(new Card { Id = "2.9", Stage = Stage.Youth, Title = "Снять танец для рилсов", Yes = E(8, -2, 0, 10), No = E(-2, 0, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "2.10", Stage = Stage.Youth, Title = "Сесть за руль после вечеринки («тут ехать 5 минут»)", Yes = Effect.Zero, No = E(-4, 0, 0, 8), Window = Window.S, DeathSide = Side.Yes, DeathCause = "Не доехал: сел за руль после вечеринки." });

            // ---------- Этап 3 — Зрелость ----------
            cards.Add(new Card { Id = "3.1", Stage = Stage.Adulthood, Title = "Взять ипотеку на 30 лет (мелкий шрифт)", Yes = E(-12, 0, -22, 12), No = E(6, 0, 4, -10), Window = Window.L });
            cards.Add(new Card { Id = "3.2", Stage = Stage.Adulthood, Title = "Уйти в стабильный найм (вместо своего дела)", Yes = E(-12, 0, 12, 0), No = E(12, -4, -12, 0), Window = Window.M });
            cards.Add(new Card { Id = "3.3", Stage = Stage.Adulthood, Title = "Завести ребёнка", Yes = E(8, -8, -16, 18), No = E(4, 0, 0, -12), Window = Window.M });
            cards.Add(new Card { Id = "3.4", Stage = Stage.Adulthood, Title = "Купить абонемент в зал и не ходить", Yes = E(6, 0, -8, 0), No = E(-2, 0, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "3.5", Stage = Stage.Adulthood, Title = "Пойти к начальнику просить повышение", Yes = E(8, 0, 16, 0), No = E(-8, 0, -4, 0), Window = Window.M });
            cards.Add(new Card { Id = "3.6", Stage = Stage.Adulthood, Title = "Кредит на «свадьбу мечты» (мелкий шрифт)", Yes = E(12, 0, -24, 16), No = E(-8, 0, 4, 0), Window = Window.L });
            cards.Add(new Card { Id = "3.7", Stage = Stage.Adulthood, Title = "Переехать в другой город за работой", Yes = E(6, 0, 14, -16), No = E(4, 0, -8, 8), Window = Window.M });
            cards.Add(new Card { Id = "3.8", Stage = Stage.Adulthood, Title = "Марафон саморазвития за 90к", Yes = E(14, 0, -18, 0), No = E(-4, 0, 0, 0), Window = Window.L, Linked = true });
            cards.Add(new Card { Id = "3.9", Stage = Stage.Adulthood, Title = "Завести собаку", Yes = E(10, 0, -8, 12), No = E(-4, 0, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "3.10", Stage = Stage.Adulthood, Title = "Вложить всё в «крипту от друга детства»", Yes = E(-10, 0, -40, 0), No = E(-2, 0, 2, 0), Window = Window.L });

            // ---------- Этап 4 — Старость ----------
            cards.Add(new Card { Id = "4.1", Stage = Stage.OldAge, Title = "Переписать завещание на кота", Yes = E(16, 0, 0, -14), No = E(-4, 0, 0, 8), Window = Window.L });
            cards.Add(new Card { Id = "4.2", Stage = Stage.OldAge, Title = "«Омолаживающие» уколы из телемагазина", Yes = E(8, -16, -12, 0), No = E(-2, 0, 0, 0), Window = Window.L });
            cards.Add(new Card { Id = "4.3", Stage = Stage.OldAge, Title = "Переехать к детям", Yes = E(-8, 0, 0, 16), No = E(8, 0, 0, -12), Window = Window.M });
            cards.Add(new Card { Id = "4.4", Stage = Stage.OldAge, Title = "Завести блог «дед разбирается в мемах»", Yes = E(14, 0, 0, 10), No = E(-4, 0, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "4.5", Stage = Stage.OldAge, Title = "Раздать сбережения внукам сейчас", Yes = E(10, 0, -20, 18), No = E(-4, 0, 6, -10), Window = Window.M });
            cards.Add(new Card { Id = "4.6", Stage = Stage.OldAge, Title = "Записаться в клуб моржей", Yes = E(12, -14, 0, 8), No = E(-2, 0, 0, 0), Window = Window.S });
            cards.Add(new Card { Id = "4.7", Stage = Stage.OldAge, Title = "Круиз мечты на все деньги", Yes = E(18, 0, -22, 0), No = E(-8, 0, 0, 0), Window = Window.M });
            cards.Add(new Card { Id = "4.8", Stage = Stage.OldAge, Title = "Помириться с сыном после 20 лет молчания", Yes = E(14, 0, 0, 20), No = E(-12, 0, 0, -8), Window = Window.M, Linked = true });
            cards.Add(new Card { Id = "4.9", Stage = Stage.OldAge, Title = "Операция «на всякий случай»", Yes = E(-6, 16, -12, 0), No = E(4, -10, 0, 0), Window = Window.M });
            cards.Add(new Card { Id = "4.10", Stage = Stage.OldAge, Title = "Уснуть под «ещё одну серию» сериала", Yes = Effect.Zero, No = E(-4, 0, 0, 0), Window = Window.S, DeathSide = Side.Yes, PeacefulDeath = true, DeathCause = "Уснул под «ещё одну серию» и умер счастливым." });

            return cards;
        }
    }
}
