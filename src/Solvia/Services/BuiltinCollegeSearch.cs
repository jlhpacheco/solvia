using System.Text.RegularExpressions;

namespace Solvia.Services;

/// <summary>
/// The built-in college search (BUILD-PLAN T8): a deterministic
/// in-memory match over <see cref="IFeeCatalog"/>'s 25 rows. This is
/// the shipped default — 25 rows do not need a search cluster, and
/// this provider can never be down. It is also the fallback the
/// Elastic provider lands on whenever the local index is unreachable.
///
/// Ranking, strongest first: the full campus name appearing in the
/// query (or the query inside the name), then significant name
/// tokens ("Fresno", "Monterey" — not "College"/"State"/"District"),
/// then query words found anywhere in the campus's fee texts. Ties
/// keep catalog order, so results are stable and explainable.
/// </summary>
public sealed class BuiltinCollegeSearch : ICollegeSearch
{
    public const string ProviderName = "builtin";

    private readonly IFeeCatalog _catalog;

    public BuiltinCollegeSearch(IFeeCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<CollegeSearchResult> SearchAsync(
        string? query, CancellationToken cancellationToken = default)
    {
        var all = _catalog.All();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new CollegeSearchResult(all, ProviderName));
        }

        var normalized = query.Trim().ToLowerInvariant();
        var queryTokens = Tokenize(normalized);

        var scored = new List<(CampusFee Fee, int Score, int Index)>();
        for (var i = 0; i < all.Count; i++)
        {
            var score = Score(all[i], normalized, queryTokens);
            if (score > 0)
            {
                scored.Add((all[i], score, i));
            }
        }

        var ranked = scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Fee)
            .ToList();
        return Task.FromResult(new CollegeSearchResult(ranked, ProviderName));
    }

    private static int Score(
        CampusFee fee, string query, HashSet<string> queryTokens)
    {
        var score = 0;
        var name = fee.Campus.ToLowerInvariant();

        // Full-name phrase, either direction ("fresno city college"
        // typed in full, or a short query inside a long name).
        if (query.Length >= 3 && (name.Contains(query) || query.Contains(name)))
        {
            score += 100;
        }

        // Significant name tokens the student actually typed.
        score += 10 * SignificantTokens(fee.Campus).Count(queryTokens.Contains);

        // Any query word (3+ letters) appearing in the campus's fee
        // texts: term names, day-pass and motorcycle notes, source.
        var textTokens = Tokenize(string.Join(' ',
            fee.Campus, fee.StudentSemesterFeeText, fee.OtherTermFeesText,
            fee.DayPassText, fee.MotorcycleText, fee.SourceNote));
        score += 2 * queryTokens.Count(
            t => t.Length >= 3 && textTokens.Contains(t));

        return score;
    }

    /// <summary>Words of a name that identify it (mirrors FallbackExtractor's set).</summary>
    private static HashSet<string> SignificantTokens(string campusName)
    {
        var stopwords = new HashSet<string>(StringComparer.Ordinal)
        {
            "college", "community", "state", "csu", "university", "of",
            "the", "district", "office", "chancellor", "california",
        };
        return Tokenize(campusName).Where(t => !stopwords.Contains(t)).ToHashSet();
    }

    private static HashSet<string> Tokenize(string text) =>
        Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .ToHashSet();
}
