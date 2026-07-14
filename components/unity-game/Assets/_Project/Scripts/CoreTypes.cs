using System;

namespace LifeChoices
{
    /// <summary>The four affect scales. Order is the tie-break order for
    /// "highest scale" (life label) and simultaneous scale-break death.</summary>
    public enum Scale
    {
        Mood = 0,    // Настроение (Н)
        Health = 1,  // Здоровье   (З)
        Money = 2,   // Деньги     (Д)
        People = 3   // Люди       (Л)
    }

    /// <summary>Life stages, in play order.</summary>
    public enum Stage
    {
        Childhood = 0, // Детство
        Youth = 1,     // Юность
        Adulthood = 2, // Зрелость
        OldAge = 3     // Старость
    }

    /// <summary>Which side of a card. None = the card has no death side.</summary>
    public enum Side { None = 0, Yes = 1, No = 2 }

    /// <summary>Decision-window length. Seconds tuned by playtest.</summary>
    public enum Window { S = 0, M = 1, L = 2 }

    public enum DeathKind { Instant, BrokenScale, Natural }

    public static class Windows
    {
        // S ≈ 2.0s · M ≈ 3.5s · L ≈ 5.0s (docs/mvp-content.md).
        public static float Seconds(Window w) => w switch
        {
            Window.S => 2.0f,
            Window.M => 3.5f,
            Window.L => 5.0f,
            _ => 3.5f
        };
    }

    public static class Scales_
    {
        public const int Start = 50;
        public const int Min = 0;
        public const int Max = 100;
    }

    public static class ScaleNames
    {
        public static string Short(Scale s) => s switch
        {
            Scale.Mood => "Настроение",
            Scale.Health => "Здоровье",
            Scale.Money => "Деньги",
            Scale.People => "Люди",
            _ => s.ToString()
        };
    }

    public static class StageNames
    {
        public static string Ru(Stage s) => s switch
        {
            Stage.Childhood => "Детство",
            Stage.Youth => "Юность",
            Stage.Adulthood => "Зрелость",
            Stage.OldAge => "Старость",
            _ => s.ToString()
        };
    }

    /// <summary>Immutable scale delta of one card side. Indexed by Scale order:
    /// Настроение / Здоровье / Деньги / Люди.</summary>
    public readonly struct Effect
    {
        public readonly int Mood;
        public readonly int Health;
        public readonly int Money;
        public readonly int People;

        public Effect(int mood, int health, int money, int people)
        {
            Mood = mood; Health = health; Money = money; People = people;
        }

        public static readonly Effect Zero = new Effect(0, 0, 0, 0);

        public int this[Scale s] => s switch
        {
            Scale.Mood => Mood,
            Scale.Health => Health,
            Scale.Money => Money,
            Scale.People => People,
            _ => 0
        };

        /// <summary>Total magnitude of the shift — used to rank "memories".</summary>
        public int AbsSum() => Math.Abs(Mood) + Math.Abs(Health) + Math.Abs(Money) + Math.Abs(People);

        /// <summary>Adds a mood delta (used for timeout's extra Настроение−4).</summary>
        public Effect WithMoodDelta(int delta) => new Effect(Mood + delta, Health, Money, People);
    }

    public readonly struct DeathInfo
    {
        public readonly DeathKind Kind;
        public readonly string Cause;

        private DeathInfo(DeathKind kind, string cause) { Kind = kind; Cause = cause; }

        public static DeathInfo Instant(string cause) => new DeathInfo(DeathKind.Instant, cause);
        public static DeathInfo Broken(string cause) => new DeathInfo(DeathKind.BrokenScale, cause);
        public static DeathInfo Natural(string cause) => new DeathInfo(DeathKind.Natural, cause);
    }
}
