using System.Collections.Generic;

namespace LifeChoices
{
    /// <summary>A resolved card in this run, with the magnitude of its shift.</summary>
    public readonly struct MemoryEntry
    {
        public readonly Card Card;
        public readonly Side Side;
        public readonly bool WasTimeout;
        public readonly int Shift;

        public MemoryEntry(Card card, Side side, bool wasTimeout, int shift)
        {
            Card = card; Side = side; WasTimeout = wasTimeout; Shift = shift;
        }

        /// <summary>"а помнишь, как ты…" line for the finale slideshow.</summary>
        public string MemoryLine()
        {
            string choice = WasTimeout ? "не успел решить" : (Side == Side.Yes ? "сказал да" : "сказал нет");
            return $"…«{Card.Title}» — {choice}.";
        }
    }

    /// <summary>The text obituary finale.</summary>
    public sealed class Obituary
    {
        public string Cause;
        public string Label;
        public string Funeral;
        public List<string> Memories = new List<string>();
    }

    public static class ObituaryBuilder
    {
        /// <summary>
        /// Assembles the finale: cause of death, life label (by highest scale),
        /// funeral turnout (by Люди), and the three biggest-shift memories of the run.
        /// </summary>
        public static Obituary Build(DeathInfo death, ScaleState scales, IReadOnlyList<MemoryEntry> history)
        {
            var o = new Obituary
            {
                Cause = death.Cause,
                Label = DeathTables.Label(scales),
                Funeral = DeathTables.Funeral(scales.Get(Scale.People))
            };

            // Top-3 memories by total absolute scale shift; ties break toward the
            // earlier card (stable) for deterministic output.
            var ordered = new List<int>();
            for (int i = 0; i < history.Count; i++) ordered.Add(i);
            ordered.Sort((a, b) =>
            {
                int cmp = history[b].Shift.CompareTo(history[a].Shift);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            int take = ordered.Count < 3 ? ordered.Count : 3;
            for (int k = 0; k < take; k++)
                o.Memories.Add(history[ordered[k]].MemoryLine());

            return o;
        }
    }
}
