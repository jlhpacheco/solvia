using System.Text.RegularExpressions;

namespace Solvia.Services;

/// <summary>
/// Bounded spelling repair for the reader's private interpretation copy.
/// Callers keep the original sentence for display and still ground every fact.
/// This helper supplies no dates, durations, prices or authorization.
/// </summary>
internal static class ScheduleSpelling
{
    private static readonly string[] Weekdays =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
    private static readonly string[] Months =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];
    private static readonly string[] MonthPlurals =
        ["januaries", "februaries", "marches", "aprils", "mays", "junes", "julys", "augusts", "septembers", "octobers", "novembers", "decembers"];
    private static readonly string[] CalendarWords = Weekdays.Concat(Weekdays.Select(day => day + "s"))
        .Concat(Months).Concat(MonthPlurals).ToArray();
    // May is too short and too common to be a fuzzy target, including its plural.
    private static readonly string[] RepairWords = CalendarWords.Where(word => word.Length >= 4 && word != "mays")
        .Concat(["week", "weeks"]).ToArray();
    private static readonly HashSet<string> KnownWords = new(CalendarWords.Concat(
        ["week", "weeks", "wk", "wks", "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat", "sun",
         "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec"]), StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wensday"] = "wednesday", ["wensdays"] = "wednesdays",
        ["wendsday"] = "wednesday", ["wendsdays"] = "wednesdays"
    };
    // Real words close to a calendar token are not evidence of a calendar claim.
    private static readonly HashSet<string> OrdinaryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "money", "many", "maybe", "never", "none", "only", "once", "nine",
        "weak", "weaks", "seek", "seeks", "peek", "peeks", "meek", "meeks", "leek", "leeks",
        "weed", "weeds", "weep", "weeps", "geek", "geeks", "reek", "reeks", "weens",
        "sundae", "sundaes", "sundry", "tune", "tunes", "dune", "dunes", "jute", "jutes", "jury", "juries", "duly",
        "marsh", "marshes", "match", "matches", "marched", "marcher", "marchers", "market", "markets",
        "today", "todays", "yesterday", "yesterdays", "tomorrow", "tomorrows", "someday", "somedays",
        "birthday", "birthdays", "holiday", "holidays", "payday", "paydays", "anyday", "anydays",
        "weekday", "weekdays", "weekend", "weekends", "everyday", "weekly", "daily", "day", "days", "month", "months"
    };
    // These are deliberately not near-spelling aliases for real calendar names.
    private static readonly HashSet<string> UnknownCalendarNames = new(StringComparer.OrdinalIgnoreCase)
        { "smarch", "smarchs", "smarches", "funday", "fundays" };
    private static readonly Regex Words = new(@"(?<![\p{L}\p{N}_])\p{L}{4,}(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant);

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Words.Replace(text, match =>
        {
            var word = match.Value;
            if (KnownWords.Contains(word) || OrdinaryWords.Contains(word) || UnknownCalendarNames.Contains(word)) return word;
            if (Aliases.TryGetValue(word, out var alias)) return MatchCase(word, alias);
            var candidates = RepairWords.Where(candidate => DistanceAtMost(word, candidate, 1)).Take(2).ToArray();
            return candidates.Length == 1 ? MatchCase(word, candidates[0]) : word;
        });
    }

    /// <summary>
    /// A remaining calendar-shaped unknown must force review instead of allowing
    /// a reader to silently drop it from an otherwise valid weekday list.
    /// Pass the normalized copy; ordinary words and supported abbreviations stay safe.
    /// </summary>
    public static bool HasUnrecognizedScheduleWord(string normalizedText)
    {
        ArgumentNullException.ThrowIfNull(normalizedText);
        foreach (Match match in Words.Matches(normalizedText))
        {
            var word = match.Value;
            if (KnownWords.Contains(word) || OrdinaryWords.Contains(word)) continue;
            if (UnknownCalendarNames.Contains(word) || word.EndsWith("day", StringComparison.OrdinalIgnoreCase) ||
                word.EndsWith("days", StringComparison.OrdinalIgnoreCase)) return true;
            // A remaining distance-one tie (Jule -> June/July) is ambiguous.
            // Distance two is useful only for longer calendar names: applying
            // it to week/March/June/July falsely treats well/work/need/each as
            // schedule claims. Ordinary prose must not invalidate a good draft.
            if (RepairWords.Count(candidate => DistanceAtMost(word, candidate, 1)) > 1 ||
                (word.Length >= 5 && CalendarWords.Any(candidate => candidate.Length >= 7 &&
                    DistanceAtMost(word, candidate, 2)))) return true;
        }
        return false;
    }

    private static string MatchCase(string original, string correction)
    {
        if (original.All(char.IsUpper)) return correction.ToUpperInvariant();
        return char.IsUpper(original[0]) ? char.ToUpperInvariant(correction[0]) + correction[1..] : correction;
    }

    // Optimal-string-alignment distance supports a single adjacent transposition.
    // Length rejection bounds both work and allocation for arbitrary input words.
    private static bool DistanceAtMost(string source, string target, int limit)
    {
        if (Math.Abs(source.Length - target.Length) > limit) return false;
        var first = source.ToLowerInvariant();
        var distance = new int[first.Length + 1, target.Length + 1];
        for (var i = 0; i <= first.Length; i++) distance[i, 0] = i;
        for (var j = 0; j <= target.Length; j++) distance[0, j] = j;
        for (var i = 1; i <= first.Length; i++)
        for (var j = 1; j <= target.Length; j++)
        {
            var cost = first[i - 1] == target[j - 1] ? 0 : 1;
            distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && first[i - 1] == target[j - 2] && first[i - 2] == target[j - 1])
                distance[i, j] = Math.Min(distance[i, j], distance[i - 2, j - 2] + 1);
        }
        return distance[first.Length, target.Length] <= limit;
    }
}
