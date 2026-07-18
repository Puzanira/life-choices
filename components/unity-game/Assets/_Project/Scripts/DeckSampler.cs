using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ThanksNoThanks
{
    /// <summary>
    /// A sampled run: the planned deck plus an age-assigned reserve of ungated normal cards.
    /// When <see cref="Game"/> skips a chain-gated card (parent ≠ ДА) it substitutes a reserve
    /// card so the ACTUALLY DRAWN count stays inside the 25–30 contract regardless of answers.
    /// </summary>
    public sealed class DeckPlan
    {
        public List<Card> Deck = new();
        public List<Card> Reserve = new();   // age-sorted, ungated, never-excluded normals

        /// <summary>
        /// LT08 «Пора подлечиться!» — a conditional SYSTEM card. Un-excluded from the hard list but
        /// NEVER randomly sampled into <see cref="Deck"/>: it is carried here and inserted by
        /// <see cref="Game"/> the moment health &lt; 40% AND age ≥ 30 first hold (single-shot per life).
        /// Null when the card isn't present in the parsed set.
        /// </summary>
        public Card Lt08;
    }

    /// <summary>
    /// Per-run deck builder for «Спасибо, не надо». Replaces the fixed 14-card spine subset with a
    /// seeded random sample that spans the whole life (детство → старость):
    ///  • hard-excluded IDs (crisis block, system-only cards) never appear;
    ///  • TIMELINE milestones are always included at their canonical age;
    ///  • non-milestone cards are sampled per life-phase so the run stays ~25–30 cards;
    ///  • RANDOM_TRIGGER cards roll for probabilistic inclusion (RND06 in the pool; CR09/LT08 excluded);
    ///    RANDOM_OUTCOME (YA02/RND04) and RND01 draw by the normal rules (only their outcome is random);
    ///  • CHAIN children are placed in the age-sorted plan but tagged with a parent-ДА gate that
    ///    <see cref="Game"/> checks at draw time (so the child is skipped unless the parent said ДА);
    ///  • RND01 is turned into a delayed fatal by <see cref="CardLoader"/> (age + n → «за вами пришли»).
    /// Pure C# — no engine references. The RNG is injected so tests are deterministic (no statistics).
    /// </summary>
    public static class DeckSampler
    {
        // ---- tunable constants (starting values for playtest tuning) ----
        public const int LifeAdultMin = 18;   // «любой» / open windows begin adult life here
        public const int LifeMax = 85;        // upper cap for open-ended («N+», «любой») windows
        public const int ChildhoodMaxAge = 17;
        public const int YoungMaxAge = 29;
        public const int MidMaxAge = 54;

        public const int MinDeck = 25;
        public const int MaxDeck = 30;
        public const double RandomInclusionChance = 0.5; // per RANDOM_TRIGGER card, each run

        // Per-phase non-milestone sample sizes (childhood is a range; the rest are fixed targets).
        public const int ChildhoodMin = 6;
        public const int ChildhoodMax = 8;
        public const int YoungTarget = 3;
        public const int MidTarget = 3;
        public const int OldTarget = 4;

        // Hard-excluded — these IDs must never be drawn (enforced by a test). LT08 is NO LONGER here:
        // it is un-excluded (canon 2026-07-18) but pulled out of sampling into DeckPlan.Lt08 and
        // condition-inserted by Game (health<40% & age≥30), so it still never appears randomly.
        public static readonly HashSet<string> Excluded = new()
        {
            "CR00", "CR01", "CR02", "CR03", "CR04", "CR05", "CR06", "CR07", "CR08", "CR09",
            "MD06", "RND05",
        };

        // System card extracted from the pool before sampling (never a random draw); see DeckPlan.Lt08.
        private const string SystemHealthCardId = "LT08";

        // Milestones always present at their canonical age (I02 intro is pinned first).
        private static readonly string[] UnconditionalMilestones =
            { "I02", "I03", "YA01", "YA03", "YA05", "MD01" };

        // CHAIN children → the parent whose ДА unlocks them (child id → parent id).
        private static readonly Dictionary<string, string> ChainParent = new()
        {
            { "LT07", "CH07" },   // мемуары ← дневник
            { "MD07", "CH08" },   // блог    ← кружок
            { "YA02", "YA01" },   // стартап ← универ (also RANDOM/probabilistic)
            { "MD02", "MD01" },   // ребёнок ← свадьба (milestone; age = MD01 + 2)
            { "LT04", "MD02" },   // дети выросли ← ребёнок (milestone)
        };

        // Conditional milestones — always placed in the plan, gated at runtime.
        private static readonly string[] ConditionalMilestones = { "MD02", "LT04" };
        // Chained non-milestone cards — same treatment. YA02 (стартап ← универ) is RANDOM_OUTCOME,
        // not RANDOM_TRIGGER, so it is a NORMAL chained card now: always placed, gated on YA01=ДА;
        // only its ±Δ outcome is random.
        private static readonly string[] ChainedNormals = { "LT07", "MD07", "YA02" };

        /// <summary>Convenience: parse the CSV and sample one deck.</summary>
        public static List<Card> SampleFromCsv(string csv, Random rng)
            => Build(CardLoader.ParseAll(csv), rng);

        /// <summary>Convenience: parse the CSV and sample one plan (deck + reserve).</summary>
        public static DeckPlan PlanFromCsv(string csv, Random rng)
            => BuildPlan(CardLoader.ParseAll(csv), rng);

        /// <summary>Sample one age-ordered deck from the full parsed card set.</summary>
        public static List<Card> Build(IReadOnlyList<Card> allCards, Random rng)
            => BuildPlan(allCards, rng).Deck;

        /// <summary>Sample one plan: the age-ordered deck plus the top-up reserve.</summary>
        public static DeckPlan BuildPlan(IReadOnlyList<Card> allCards, Random rng)
        {
            rng ??= new Random();
            var byId = new Dictionary<string, Card>();
            foreach (var c in allCards)
                if (!Excluded.Contains(c.Id))
                    byId[c.Id] = c;

            // Pull LT08 out of the sampling pool: it is a condition-triggered system card, inserted by
            // Game — never a random draw (would otherwise fire via its RANDOM_TRIGGER flag). Carried on
            // the plan; its final age is set by Game at insertion time.
            Card lt08 = null;
            if (byId.TryGetValue(SystemHealthCardId, out lt08))
                byId.Remove(SystemHealthCardId);

            var deck = new List<Card>();
            var handled = new HashSet<string>();

            // 1) Unconditional milestones at canonical ages.
            foreach (var id in UnconditionalMilestones)
                if (byId.TryGetValue(id, out var c))
                {
                    c.Age = id == "I02" ? 0 : id == "I03" ? 1 : AssignAge(c, rng);
                    deck.Add(c);
                    handled.Add(id);
                }

            // 2) Conditional milestone MD02 (ребёнок): age = свадьба + 2, gated on MD01=ДА.
            int md01Age = byId.TryGetValue("MD01", out var md01) ? md01.Age : 30;
            if (byId.TryGetValue("MD02", out var md02))
            {
                md02.Age = md01Age + 2;
                md02.RequiresParentYes = "MD01";
                deck.Add(md02);
                handled.Add("MD02");
            }

            // 3) Remaining conditional milestone (LT04) + chained normals (LT07, MD07): plan + gate.
            foreach (var id in ConditionalMilestones.Concat(ChainedNormals))
            {
                if (id == "MD02" || handled.Contains(id)) continue;
                if (!byId.TryGetValue(id, out var c)) continue;
                c.Age = AssignAge(c, rng);
                c.RequiresParentYes = ChainParent[id];
                deck.Add(c);
                handled.Add(id);
            }

            // 4) Probabilistic inclusion — cards flagged RANDOM_TRIGGER (canon) or legacy RANDOM,
            //    each an independent coin per run. In the playable pool this is RND06 (селфи);
            //    CR09/LT08 (the other RANDOM_TRIGGER cards) are hard-excluded. RANDOM_OUTCOME cards
            //    (YA02/RND04) and RND01 are NOT here — they draw by the normal rules below.
            //    Iterated in source order for deterministic seeded output.
            foreach (var c in byId.Values.Where(c => c.IsRandomTrigger).OrderBy(c => c.Order).ToList())
            {
                if (handled.Contains(c.Id)) continue;
                if (rng.NextDouble() >= RandomInclusionChance) { handled.Add(c.Id); continue; }
                c.Age = AssignAge(c, rng);
                if (ChainParent.TryGetValue(c.Id, out var parent)) c.RequiresParentYes = parent;
                deck.Add(c);
                handled.Add(c.Id);
            }

            // 5) Non-milestone pool → phase buckets → sampled to fill the run.
            var pool = byId.Values.Where(c => !handled.Contains(c.Id)).ToList();
            var childhood = pool.Where(c => Window(c).Max <= ChildhoodMaxAge).ToList();
            var young = pool.Where(c => { var w = Window(c); return w.Max > ChildhoodMaxAge && w.Min <= YoungMaxAge; }).ToList();
            var mid = pool.Where(c => { var w = Window(c); return w.Min > YoungMaxAge && w.Min <= MidMaxAge; }).ToList();
            var old = pool.Where(c => Window(c).Min > MidMaxAge).ToList();

            var normals = new List<Card>();
            normals.AddRange(TakeRandom(childhood, rng.Next(ChildhoodMin, ChildhoodMax + 1), rng));
            normals.AddRange(TakeRandom(young, YoungTarget, rng));
            normals.AddRange(TakeRandom(mid, MidTarget, rng));
            normals.AddRange(TakeRandom(old, OldTarget, rng));
            foreach (var c in normals) c.Age = AssignAge(c, rng);

            // Normals not drawn this run — a padding reserve if the clamp needs to reach MinDeck.
            var leftover = pool.Where(c => !normals.Contains(c)).ToList();

            deck.AddRange(normals);

            // 6) Clamp to [MinDeck, MaxDeck]. Trim lowest-priority normals (ROND first); never a
            //    milestone or a gated card. Pad from leftover normals if somehow short.
            ClampSize(deck, normals, leftover, rng);

            // 7) Age order; stable on ties by source order (I02@0 and I03@1 stay first).
            deck.Sort((a, b) => a.Age != b.Age ? a.Age.CompareTo(b.Age) : a.Order.CompareTo(b.Order));

            // 8) Reserve: every ungated normal that didn't make the deck (incl. clamp-trimmed ones),
            //    age-assigned inside its window and age-sorted. Game.Substitute draws from it when a
            //    chain-gated card is skipped, so the drawn count stays 25–30 for any answer path.
            var reserve = pool.Where(c => !deck.Contains(c)).ToList();
            foreach (var c in reserve) c.Age = AssignAge(c, rng);
            reserve.Sort((a, b) => a.Age != b.Age ? a.Age.CompareTo(b.Age) : a.Order.CompareTo(b.Order));

            return new DeckPlan { Deck = deck, Reserve = reserve, Lt08 = lt08 };
        }

        private static void ClampSize(List<Card> deck, List<Card> normals, List<Card> leftover, Random rng)
        {
            while (deck.Count > MaxDeck && normals.Count > 0)
            {
                // Drop adult flavour/kek (ROND) first, then any ROND, then any normal — this keeps
                // the childhood spread and the weighty adult choices intact.
                var victim = normals.FirstOrDefault(c => c.IsRond && c.Age > ChildhoodMaxAge)
                             ?? normals.FirstOrDefault(c => c.IsRond)
                             ?? normals[0];
                normals.Remove(victim);
                deck.Remove(victim);
            }
            var pad = TakeRandom(leftover, MinDeck, rng); // shuffled leftovers
            int p = 0;
            while (deck.Count < MinDeck && p < pad.Count)
            {
                var add = pad[p++];
                add.Age = AssignAge(add, rng);
                deck.Add(add);
                normals.Add(add);
            }
        }

        // Assign a concrete age uniformly inside the card's «Когда» window.
        private static int AssignAge(Card c, Random rng)
        {
            var w = Window(c);
            return w.Min >= w.Max ? w.Min : rng.Next(w.Min, w.Max + 1);
        }

        private static AgeWindow Window(Card c) => AgeWindow.Parse(c.When);

        private static List<Card> TakeRandom(List<Card> src, int count, Random rng)
        {
            var copy = new List<Card>(src);
            // Fisher–Yates
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            if (count > copy.Count) count = copy.Count;
            return copy.GetRange(0, Math.Max(0, count));
        }

        /// <summary>An age window parsed from the «Когда» column.</summary>
        public readonly struct AgeWindow
        {
            public readonly int Min;
            public readonly int Max;
            public readonly bool IsMarriageOffset; // «свадьба +2» → resolved by the sampler (MD02)

            public AgeWindow(int min, int max, bool marriage = false)
            {
                Min = min; Max = max; IsMarriageOffset = marriage;
            }

            private static readonly Regex RangeRx =
                new(@"^\s*(\d+)\s*[–—\-−]\s*(\d+)", RegexOptions.Compiled);
            private static readonly Regex OpenRx = new(@"^\s*(\d+)\s*\+", RegexOptions.Compiled);
            private static readonly Regex SingleRx = new(@"^\s*(\d+)", RegexOptions.Compiled);

            /// <summary>
            /// Parse the age part of a «Когда» cell tolerantly. Handles ranges «4–8» (any dash),
            /// open «20+», single «18», «любой» (whole adult life), «свадьба +2» (marriage offset),
            /// and trailing free-text conditions after a comma («0–3, старт игры», «…, если YA01=ДА»).
            /// </summary>
            public static AgeWindow Parse(string when)
            {
                if (string.IsNullOrWhiteSpace(when))
                    return new AgeWindow(LifeAdultMin, LifeMax);

                // The age token is whatever precedes the first comma (conditions follow it).
                int comma = when.IndexOf(',');
                string token = (comma >= 0 ? when.Substring(0, comma) : when).Trim();

                if (token.StartsWith("свадьба", StringComparison.OrdinalIgnoreCase))
                    return new AgeWindow(0, 0, marriage: true);
                if (token.StartsWith("любой", StringComparison.OrdinalIgnoreCase))
                    return new AgeWindow(LifeAdultMin, LifeMax);

                var range = RangeRx.Match(token);
                if (range.Success)
                    return new AgeWindow(int.Parse(range.Groups[1].Value), int.Parse(range.Groups[2].Value));

                var open = OpenRx.Match(token);
                if (open.Success)
                    return new AgeWindow(int.Parse(open.Groups[1].Value), LifeMax);

                var single = SingleRx.Match(token);
                if (single.Success)
                {
                    int n = int.Parse(single.Groups[1].Value);
                    return new AgeWindow(n, n);
                }

                // «если Отн открыта» etc. with no leading number → treat as adult-life-wide.
                return new AgeWindow(LifeAdultMin, LifeMax);
            }
        }
    }
}
