using System;

namespace LifeChoices
{
    /// <summary>
    /// Mutable runtime state of the four scales. Starts at 50, clamps to [0,100].
    /// Death fires when any scale is driven to 0 (≤0) or 100 (≥100).
    /// </summary>
    public sealed class ScaleState
    {
        private readonly int[] _v = new int[4];

        public ScaleState(int start = Scales_.Start)
        {
            for (int i = 0; i < 4; i++) _v[i] = start;
        }

        public int Get(Scale s) => _v[(int)s];

        private static int Clamp(int x) => x < Scales_.Min ? Scales_.Min : (x > Scales_.Max ? Scales_.Max : x);

        /// <summary>Applies a card effect; every scale is clamped to [0,100].</summary>
        public void Apply(Effect e)
        {
            _v[0] = Clamp(_v[0] + e.Mood);
            _v[1] = Clamp(_v[1] + e.Health);
            _v[2] = Clamp(_v[2] + e.Money);
            _v[3] = Clamp(_v[3] + e.People);
        }

        /// <summary>The scale currently at a lethal boundary, if any. Iterates in
        /// Scale enum order so simultaneous breaks resolve deterministically.</summary>
        public bool TryGetBroken(out Scale broken, out bool high)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_v[i] <= Scales_.Min) { broken = (Scale)i; high = false; return true; }
                if (_v[i] >= Scales_.Max) { broken = (Scale)i; high = true; return true; }
            }
            broken = Scale.Mood; high = false; return false;
        }

        /// <summary>The highest scale (by value), tie-broken by enum order.</summary>
        public Scale Highest(out int value)
        {
            Scale best = Scale.Mood;
            int bestVal = _v[0];
            for (int i = 1; i < 4; i++)
            {
                if (_v[i] > bestVal) { bestVal = _v[i]; best = (Scale)i; }
            }
            value = bestVal;
            return best;
        }
    }
}
