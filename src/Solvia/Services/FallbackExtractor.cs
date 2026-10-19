using System.Text.RegularExpressions;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// The deterministic extractor — plain rules, no model, no network.
/// It answers whenever Ollama is unreachable, times out, or returns
/// an answer that fails validation, so the demo never dies because
/// the AI is down. It also owns campus-name resolution, which the
/// Ollama path reuses to validate the model's claimed college.
///
/// Honesty rules, shared with the AI path:
/// - A campus must resolve against <see cref="IFeeCatalog"/>'s 10
///   campuses. An ambiguous mention ("Valley" is shared by Antelope
///   Valley College and Diablo Valley College) resolves to null —
///   never a guess.
/// - Nothing about money is ever produced here; the result type
///   carries no price at all.
/// </summary>
public sealed class FallbackExtractor
{
    private readonly IFeeCatalog _catalog;

    public FallbackExtractor(IFeeCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Extracts the draft from the student's words, by rules.</summary>
    public ExtractionResult Extract(string studentText)
    {
        var text = studentText ?? string.Empty;

        var campus = FindCampusMention(_catalog, text);
        var term = FindTerm(text);
        var make = FindMake(text);

        return new ExtractionResult
        {
            College = campus?.Campus,
            PermitType = FindPermitType(text, term),
            Term = term,
            CarMake = make,
            CarModel = make is null ? null : FindModel(text, make),
            CarColor = FindColor(text),
            Plate = FindPlate(text),
            CarState = FindState(text),
            Source = ExtractionSource.Fallback,
        };
    }

    // ------------------------------------------------------------------
    // Campus resolution (also used to validate the model's answer)
    // ------------------------------------------------------------------

    /// <summary>
    /// Finds the one catalog campus a piece of text is talking about —
    /// a model's college string or a full student sentence alike.
    /// Passes, strongest first:
    /// 1. the exact catalog name appears verbatim (case-insensitive);
    /// 2. a campus whose significant name tokens ALL appear in the
    ///    text (most tokens wins; a tie is ambiguity → null);
    /// 3. a distinctive token that belongs to exactly ONE campus.
    /// Returns null when nothing matches or the match is ambiguous.
    /// </summary>
    public static CampusFee? FindCampusMention(IFeeCatalog catalog, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var campuses = catalog.All();

        // Pass 1: exact name, verbatim in the text.
        foreach (var campus in campuses)
        {
            if (text.Contains(campus.Campus, StringComparison.OrdinalIgnoreCase))
            {
                return campus;
            }
        }

        var textTokens = Tokenize(text);

        // Pass 2: every significant token of the campus name present.
        var qualified = campuses
            .Select(c => (Campus: c, Tokens: SignificantTokens(c.Campus)))
            .Where(x => x.Tokens.Count > 0 && x.Tokens.IsSubsetOf(textTokens))
            .ToList();
        if (qualified.Count > 0)
        {
            var best = qualified.Max(x => x.Tokens.Count);
            var winners = qualified.Where(x => x.Tokens.Count == best).ToList();
            return winners.Count == 1 ? winners[0].Campus : null;
        }

        // Pass 3: a token that names exactly one campus in the catalog
        // (e.g. "fresno", "monterey", "antelope") mentioned in the text.
        var owners = new Dictionary<string, CampusFee>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var campus in campuses)
        {
            foreach (var token in SignificantTokens(campus.Campus))
            {
                if (owners.TryGetValue(token, out var existing) &&
                    !ReferenceEquals(existing, campus))
                {
                    ambiguous.Add(token);
                }
                else
                {
                    owners[token] = campus;
                }
            }
        }

        var hits = textTokens
            .Where(t => owners.ContainsKey(t) && !ambiguous.Contains(t))
            .Select(t => owners[t])
            .Distinct()
            .ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>Words of a campus name that identify it (not "College", "State", …).</summary>
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

    // ------------------------------------------------------------------
    // Term + permit type
    // ------------------------------------------------------------------

    private static TermKind? FindTerm(string text)
    {
        if (HasWord(text, "fall") || HasWord(text, "autumn"))
        {
            return TermKind.Fall;
        }

        if (HasWord(text, "spring"))
        {
            return TermKind.Spring;
        }

        if (HasWord(text, "summer"))
        {
            return TermKind.Summer;
        }

        return null;
    }

    private static PermitTypeCode? FindPermitType(string text, TermKind? term)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("day pass") || HasWord(text, "daily") ||
            lower.Contains("one day") || lower.Contains("single day") ||
            lower.Contains("per day"))
        {
            return PermitTypeCode.Daily;
        }

        if (HasWord(text, "semester") || HasWord(text, "term"))
        {
            return PermitTypeCode.Term;
        }

        // A named term with no other signal means a term-length permit.
        return term is null ? null : PermitTypeCode.Term;
    }

    // ------------------------------------------------------------------
    // Vehicle: plate, make, model, color, state
    // ------------------------------------------------------------------
    //
    // Plate rules (proven in v1; the plate is the flow's gate field,
    // so the reader is deliberately narrow but forgiving of spacing):
    // 1. the word "plate"/"plates"/"license" followed within a few
    //    characters by 4–12 alphanumerics — SPACES INSIDE THE PLATE
    //    ARE ALLOWED AND THEN STRIPPED ("plate 8XYZ 123" → "8XYZ123");
    // 2. failing that, the strict California pattern digit + three
    //    letters + three digits ("8XYZ123") anywhere in the text.
    // Whatever wins is normalized: upper case, separators removed,
    // and it must end up 4–10 alphanumerics or it is not a plate.

    private static readonly Regex PlateAfterKeyword = new(
        @"\b(?:plate|plates|license)\b[^A-Za-z0-9]{0,12}([A-Za-z0-9][A-Za-z0-9\- ]{2,10}[A-Za-z0-9])",
        RegexOptions.Compiled);

    // California-style plate: digit, three letters, three digits (8XYZ123).
    private static readonly Regex StrictPlate = new(
        @"\b\d[A-Za-z]{3}\d{3}\b", RegexOptions.Compiled);

    private static string? FindPlate(string text)
    {
        var keyword = PlateAfterKeyword.Match(text);
        if (keyword.Success)
        {
            var plate = NormalizePlate(keyword.Groups[1].Value);
            if (plate is not null)
            {
                return plate;
            }
        }

        var strict = StrictPlate.Match(text);
        return strict.Success ? NormalizePlate(strict.Value) : null;
    }

    /// <summary>Upper-cases and strips separators; sane plates are 4–10 alphanumerics.</summary>
    public static string? NormalizePlate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var plate = Regex.Replace(raw.ToUpperInvariant(), @"[^A-Z0-9]", "");
        return plate.Length is >= 4 and <= 10 ? plate : null;
    }

    private static readonly string[] KnownMakes =
    [
        "Acura", "Audi", "BMW", "Buick", "Cadillac", "Chevrolet", "Chevy",
        "Chrysler", "Dodge", "Ford", "GMC", "Honda", "Hyundai", "Jeep",
        "Kia", "Lexus", "Mazda", "Mercedes-Benz", "Mercedes", "Nissan",
        "Porsche", "Ram", "Subaru", "Tesla", "Toyota", "Volkswagen",
        "Volvo", "VW",
    ];

    private static string? FindMake(string text)
    {
        foreach (var make in KnownMakes.OrderByDescending(m => m.Length))
        {
            if (Regex.IsMatch(
                    text, $@"\b{Regex.Escape(make)}\b", RegexOptions.IgnoreCase))
            {
                return make switch
                {
                    "Chevy" => "Chevrolet",
                    "VW" => "Volkswagen",
                    "Mercedes" => "Mercedes-Benz",
                    _ => make,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The model is the token right after the make ("Honda Civic",
    /// "Toyota RAV4", "Ford F-150"), skipping a 4-digit year when the
    /// student put it between make and model. Words that clearly are
    /// not a model (colors, terms, "plate", …) stop the search → null.
    /// </summary>
    private static string? FindModel(string text, string make)
    {
        var makeMatch = Regex.Match(
            text, $@"\b(?:{MakeSearchForm(make)})\b", RegexOptions.IgnoreCase);
        if (!makeMatch.Success)
        {
            return null;
        }

        var rest = text[(makeMatch.Index + makeMatch.Length)..];
        foreach (Match token in Regex.Matches(rest, @"[A-Za-z0-9][A-Za-z0-9\-]*"))
        {
            var word = token.Value;
            if (Regex.IsMatch(word, @"^(19|20)\d{2}$"))
            {
                continue; // a year, not the model
            }

            if (IsNotAModelWord(word))
            {
                return null;
            }

            return word;
        }

        return null;
    }

    private static string MakeSearchForm(string canonicalMake) => canonicalMake switch
    {
        "Chevrolet" => "Chevrolet|Chevy",
        "Volkswagen" => "Volkswagen|VW",
        "Mercedes-Benz" => "Mercedes-Benz|Mercedes",
        _ => canonicalMake,
    };

    private static bool IsNotAModelWord(string word)
    {
        if (word.Length == 1)
        {
            return true; // "I", "a" — never a model name
        }

        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "plate", "plates", "license", "fall", "spring", "summer",
            "semester", "term", "daily", "day", "pass", "permit", "for",
            "at", "in", "on", "the", "a", "an", "my", "is", "with",
        };
        return stopwords.Contains(word) ||
               FindColor(word) is not null ||
               KnownMakes.Any(m => string.Equals(m, word, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindColor(string text)
    {
        // Canonical names mirror the VehicleColor enum.
        (string Word, string Canonical)[] colors =
        [
            ("black", "Black"), ("white", "White"), ("silver", "Silver"),
            ("gray", "Gray"), ("grey", "Gray"), ("red", "Red"),
            ("blue", "Blue"), ("green", "Green"), ("brown", "Brown"),
            ("orange", "Orange"), ("yellow", "Yellow"), ("gold", "Gold"),
            ("purple", "Purple"),
        ];
        foreach (var (word, canonical) in colors)
        {
            if (HasWord(text, word))
            {
                return canonical;
            }
        }

        return null;
    }

    private static string? FindState(string text)
    {
        if (HasWord(text, "california"))
        {
            return "CA";
        }

        // A two-letter code right after the plate ("8XYZ123, CA").
        var afterPlate = Regex.Match(
            text, @"\b\d[A-Za-z]{3}\d{3}\b[^A-Za-z0-9]{0,4}([A-Za-z]{2})\b");
        if (afterPlate.Success)
        {
            var code = afterPlate.Groups[1].Value.ToUpperInvariant();
            if (Enum.TryParse<UsState>(code, ignoreCase: false, out _))
            {
                return code;
            }
        }

        return null;
    }

    private static bool HasWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
}
