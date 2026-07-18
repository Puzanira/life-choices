using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ThanksNoThanks
{
    /// <summary>
    /// Tolerant loader for scenes.csv (the authoritative card source).
    /// Handles quoted commas, blank fields, unicode (−) AND ascii (-) minus, and
    /// non-contiguous IDs (I01/LT06/RND02 are absent — never assume a dense range).
    /// Unsupported flags (OPEN:*, CHAIN→, DELAY, BLOCK$, INVERT, BLITZ, ZONE) are parsed
    /// and ignored, never crash.
    ///
    /// Flag semantics honoured here (canon scenes.csv 2026-07-18):
    ///  • FORCED         → веха/объявление (no real choice); never contributes a necrolog line.
    ///  • RANDOM_TRIGGER → probabilistic INCLUSION marker (whether the card appears at all).
    ///  • RANDOM_OUTCOME → random OUTCOME marker (already carried by ±N deltas); mechanical no-op.
    ///  • legacy "RANDOM" → mapped to RANDOM_TRIGGER (backward compat with the old snapshot).
    ///
    /// The optional 14th column «Длительный эффект» (MULT/DRAIN/FROM/DUR grammar) is tolerated
    /// both when present and when absent — flags always live in column 12, so it is simply ignored.
    /// </summary>
    public static class CardLoader
    {
        // The spine subset for this increment, in no particular order (sorted by age on load):
        // втягивающие интро, всё детство, и коherentный ранний-взрослый срез (работа/любовь/
        // город + фатальный соблазн). NOT contiguous by design.
        public static readonly string[] DefaultSubset =
        {
            "I02", "I03",
            "CH01", "CH02", "CH03", "CH04", "CH05", "CH06", "CH07", "CH08",
            "YA01", "RND03", "YA03", "YA06"
        };

        // Fatal-cause phrasing for the finale (GAME_SPEC §финал / screens S12).
        private static readonly Dictionary<string, string> FatalCauses = new()
        {
            { "CH02", "вы сунули палец в розетку" },
            { "RND03", "белый порошок" },
            { "RND06", "селфи на краю крыши" },
            { "RND01", "за вами пришли" },
        };

        // --- Column indices in scenes.csv ---
        private const int ColId = 0;
        private const int ColQuestion = 1;
        private const int ColWhen = 2;
        // 3 = Тип, 4 = ДА-проза
        private const int ColYesDelta = 5;
        // 6 = НЕТ-проза
        private const int ColNoDelta = 7;
        // 8/9 = Ведущий
        private const int ColYesNecro = 10;
        private const int ColNoNecro = 11;
        private const int ColFlags = 12;

        public static List<Card> LoadSubset(string csv, IEnumerable<string> ids)
        {
            var wanted = new HashSet<string>(ids);
            var all = ParseAll(csv);
            var subset = new List<Card>();
            foreach (var c in all)
                if (wanted.Contains(c.Id))
                    subset.Add(c);

            // Draw in age order; stable on ties via source order.
            subset.Sort((a, b) => a.Age != b.Age ? a.Age.CompareTo(b.Age) : a.Order.CompareTo(b.Order));
            return subset;
        }

        public static List<Card> ParseAll(string csv)
        {
            var cards = new List<Card>();
            if (string.IsNullOrEmpty(csv)) return cards;

            var rows = Tokenize(csv);
            int order = 0;
            bool headerSkipped = false;

            foreach (var row in rows)
            {
                if (row.Count == 0) continue;
                string id = Field(row, ColId).Trim();
                if (string.IsNullOrEmpty(id)) continue;

                // Skip the header row (first data-bearing row whose ID isn't a real card id).
                if (!headerSkipped && string.Equals(id, "ID", StringComparison.OrdinalIgnoreCase))
                {
                    headerSkipped = true;
                    continue;
                }
                headerSkipped = true;

                var flags = ParseFlags(Field(row, ColFlags));
                var card = new Card
                {
                    Id = id,
                    Question = Field(row, ColQuestion).Trim(),
                    When = Field(row, ColWhen).Trim(),
                    Age = ParseAge(Field(row, ColWhen)),
                    Order = order++,
                    YesDeltas = ParseDeltas(Field(row, ColYesDelta)),
                    NoDeltas = ParseDeltas(Field(row, ColNoDelta)),
                    YesNecrolog = CleanNecrolog(Field(row, ColYesNecro)),
                    NoNecrolog = CleanNecrolog(Field(row, ColNoNecro)),
                    Flags = flags,
                };
                card.IsNoCons = flags.Contains("NOCONS");
                card.IsRond = flags.Contains("ROND");
                card.IsForced = flags.Contains("FORCED");
                // Probabilistic inclusion keys on RANDOM_TRIGGER; legacy "RANDOM" means the same
                // (old snapshot). RANDOM_OUTCOME is a separate, mechanically-inert marker.
                card.IsRandomTrigger = flags.Contains("RANDOM_TRIGGER") || flags.Contains("RANDOM");
                card.IsRandomOutcome = flags.Contains("RANDOM_OUTCOME");
                bool hasFatal = flags.Contains("FATAL");
                int delayYears = ParseDelayYears(flags);
                // DELAY(n)+FATAL (RND01) is a *delayed* fatal: ДА doesn't end the run now, the
                // finale is scheduled for card.Age + n. Plain FATAL (розетка/порошок/селфи) is
                // immediate. Other DELAY-only flags stay no-ops (prose in the necrolog only).
                if (hasFatal && delayYears > 0)
                {
                    card.DelayedFatalYears = delayYears;
                    card.YesIsFatal = false;
                }
                else
                {
                    card.YesIsFatal = hasFatal;
                }
                // Canon (scenes.csv I03 row / GAME_SPEC core loop): "Сделать первый шаг?"
                // starts the age timer regardless of the answer ("шаг всё равно происходит").
                // Prose-only in the CSV (no machine flag), so mapped here by id.
                card.StartsAgeTimer = id == "I03";
                if (hasFatal)
                    card.FatalCause = FatalCauses.TryGetValue(id, out var cause) ? cause : "неведомая дичь";

                cards.Add(card);
            }
            return cards;
        }

        // --- CSV tokenizer (RFC-4180-ish: quotes, "" escapes, CRLF/LF) ---
        internal static List<List<string>> Tokenize(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool rowHasContent = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') { inQuotes = true; rowHasContent = true; }
                    else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); rowHasContent = true; }
                    else if (c == '\n' || c == '\r')
                    {
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                        row.Add(sb.ToString()); sb.Clear();
                        if (rowHasContent) rows.Add(row);
                        row = new List<string>();
                        rowHasContent = false;
                    }
                    else { sb.Append(c); rowHasContent = true; }
                }
            }
            if (rowHasContent || sb.Length > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row);
            }
            return rows;
        }

        private static string Field(List<string> row, int index)
            => index >= 0 && index < row.Count ? row[index] : string.Empty;

        // First run of digits in the "Когда" text; cards with no number (e.g. "любой",
        // "если ...") sort to the end without breaking the load.
        private static readonly Regex AgeRx = new(@"\d+", RegexOptions.Compiled);

        internal static int ParseAge(string when)
        {
            if (string.IsNullOrEmpty(when)) return int.MaxValue;
            var m = AgeRx.Match(when);
            return m.Success && int.TryParse(m.Value, out var n) ? n : int.MaxValue;
        }

        private static string CleanNecrolog(string cell)
        {
            var t = (cell ?? string.Empty).Trim();
            if (t.Length == 0 || t == "—" || t == "-" || t == "−") return null;
            return t;
        }

        // "DELAY(3)" → 3; absent → 0. Used only in combination with FATAL (RND01).
        private static readonly Regex DelayRx = new(@"DELAY\s*\(\s*(\d+)\s*\)", RegexOptions.Compiled);

        internal static int ParseDelayYears(IEnumerable<string> flags)
        {
            foreach (var f in flags)
            {
                var m = DelayRx.Match(f);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;
            }
            return 0;
        }

        private static List<string> ParseFlags(string cell)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(cell)) return result;
            foreach (var raw in cell.Split(','))
            {
                var f = raw.Trim();
                if (f.Length > 0) result.Add(f);
            }
            return result;
        }

        // Δ column: "Здр +1, Эн −1" / "Дн ±3" / "Здр → 80%" / "—" (any unicode/ascii minus).
        private static readonly Dictionary<string, Scale> Abbrevs = new()
        {
            { "Здр", Scale.Health },
            { "Эн", Scale.Energy },
            { "Дн", Scale.Money },
            { "Отн", Scale.Relationships },
            { "Реб", Scale.Child },
        };

        // group 1 = abbrev, then either "→ N" (set) OR "<op> N" where op is + - − ±
        private static readonly Regex DeltaRx = new(
            @"(Здр|Эн|Дн|Отн|Реб)\s*(?:(→)\s*(\d+)|([+\-−±])\s*(\d+))",
            RegexOptions.Compiled);

        internal static List<ScaleDelta> ParseDeltas(string cell)
        {
            var deltas = new List<ScaleDelta>();
            if (string.IsNullOrEmpty(cell)) return deltas;
            var t = cell.Trim();
            if (t == "—" || t == "-" || t == "−") return deltas;

            foreach (Match m in DeltaRx.Matches(t))
            {
                if (!Abbrevs.TryGetValue(m.Groups[1].Value, out var scale)) continue;

                if (m.Groups[2].Success) // "→ N" set form
                {
                    int.TryParse(m.Groups[3].Value, out var v);
                    deltas.Add(new ScaleDelta(scale, DeltaKind.Set, v));
                }
                else
                {
                    string op = m.Groups[4].Value;
                    int.TryParse(m.Groups[5].Value, out var v);
                    if (op == "±")
                        deltas.Add(new ScaleDelta(scale, DeltaKind.RandomPlusMinus, v));
                    else if (op == "+")
                        deltas.Add(new ScaleDelta(scale, DeltaKind.Add, v));
                    else // "-" ascii OR "−" unicode
                        deltas.Add(new ScaleDelta(scale, DeltaKind.Add, -v));
                }
            }
            return deltas;
        }
    }
}
