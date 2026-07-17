using System;
using System.Collections.Generic;

namespace ThanksNoThanks
{
    /// <summary>
    /// Passive life scales. This increment changes them ONLY via card Δ — no real-time
    /// drain, no hand-balancing (deferred). Starting values follow GAME_SPEC.
    /// Health &lt;= 0 (and Energy &lt;= 0) are supported burnout endings.
    /// </summary>
    public sealed class Scales
    {
        public int Health;
        public int Energy;
        public int Money;
        public int Relationships;
        public int Child;

        public Scales() => Reset();

        public void Reset()
        {
            Health = 100;        // starts at 100 (GAME_SPEC)
            Energy = 100;
            Money = 0;
            Relationships = 55;  // GAME_SPEC start for the relationship scale
            Child = 0;
        }

        public bool HealthDepleted => Health <= 0;
        public bool EnergyDepleted => Energy <= 0;

        public int Get(Scale s) => s switch
        {
            Scale.Health => Health,
            Scale.Energy => Energy,
            Scale.Money => Money,
            Scale.Relationships => Relationships,
            Scale.Child => Child,
            _ => 0
        };

        public void Set(Scale s, int v)
        {
            switch (s)
            {
                case Scale.Health: Health = v; break;
                case Scale.Energy: Energy = v; break;
                case Scale.Money: Money = v; break;
                case Scale.Relationships: Relationships = v; break;
                case Scale.Child: Child = v; break;
            }
        }

        /// <param name="coin">true → the "+" side of a ±N random delta.</param>
        public void Apply(IEnumerable<ScaleDelta> deltas, Func<bool> coin)
        {
            if (deltas == null) return;
            foreach (var d in deltas)
            {
                switch (d.Kind)
                {
                    case DeltaKind.Add:
                        Set(d.Scale, Get(d.Scale) + d.Value);
                        break;
                    case DeltaKind.RandomPlusMinus:
                        Set(d.Scale, Get(d.Scale) + ((coin != null && coin()) ? d.Value : -d.Value));
                        break;
                    case DeltaKind.Set:
                        Set(d.Scale, d.Value);
                        break;
                }
            }
        }
    }
}
