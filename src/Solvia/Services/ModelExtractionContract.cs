using System.Text.Json;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Shared trust boundary for every model provider: validate catalog claims,
/// known state/color values and ground plates in the person's original words.
/// This draft contract has no fee, payment or issuance authority.
/// </summary>
internal sealed class ModelExtractionContract(IFeeCatalog catalog)
{
    private static readonly string[] FieldNames =
        ["college", "permitType", "term", "carMake", "carModel", "carColor", "plate", "carState"];

    public object JsonSchema()
    {
        static object NullableText() => new { type = new[] { "string", "null" } };
        static object NullableEnum(IEnumerable<string> values) => new Dictionary<string, object?>
        {
            ["type"] = new[] { "string", "null" },
            ["enum"] = values.Cast<object?>().Append(null).ToArray()
        };
        var properties = new Dictionary<string, object>
        {
            ["college"] = NullableEnum(catalog.All().Select(c => c.Campus)),
            ["permitType"] = NullableEnum(["Daily", "Term"]),
            ["term"] = NullableEnum(["Fall", "Spring", "Summer"]),
            ["carMake"] = NullableText(),
            ["carModel"] = NullableText(),
            ["carColor"] = NullableEnum(Enum.GetNames<VehicleColor>()),
            ["plate"] = NullableText(),
            ["carState"] = NullableEnum(Enum.GetNames<UsState>())
        };
        return new
        {
            type = "object",
            properties,
            required = properties.Keys.ToArray(),
            additionalProperties = false
        };
    }

    /// <summary>
    /// Parses and validates the model's JSON content. Returns null —
    /// reject the whole answer — when the JSON is unparseable, the
    /// college cannot be resolved against the fee catalog, or a
    /// vocabulary field (term / permit type) is out of vocabulary.
    /// Unknown extra fields — including any price the model might
    /// invent — are ignored: nothing here reads them.
    /// </summary>
    public ExtractionResult? Parse(string content, string studentText, ExtractionSource source,
        bool requireCompleteShape = false)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(StripCodeFences(content));
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (requireCompleteShape &&
                (root.EnumerateObject().Count() != FieldNames.Length ||
                 FieldNames.Any(name => !root.TryGetProperty(name, out var value) ||
                     value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))))
            {
                return null;
            }

            string? Field(string name) =>
                root.TryGetProperty(name, out var el) &&
                el.ValueKind == JsonValueKind.String &&
                el.GetString() is { Length: > 0 } value
                    ? value.Trim()
                    : null;

            // College: must resolve against the catalog — exact name
            // or the deterministic matcher. Unresolvable ⇒ reject all.
            string? college = null;
            if (Field("college") is { } collegeClaim)
            {
                var resolved = catalog.Find(collegeClaim) ??
                               FallbackExtractor.FindCampusMention(catalog, collegeClaim);
                if (resolved is null)
                {
                    return null;
                }

                college = resolved.Campus;
            }

            // Term: null stays null; a stated value must be in vocabulary.
            TermKind? term = null;
            if (Field("term") is { } termClaim)
            {
                // Exact named vocabulary, with Autumn as a case-insensitive
                // synonym. Substrings such as "fallacy" are not valid terms.
                term = termClaim.ToLowerInvariant() switch
                {
                    "fall" or "autumn" => TermKind.Fall,
                    "spring" => TermKind.Spring,
                    "summer" => TermKind.Summer,
                    _ => null
                };
                if (term is null)
                {
                    return null; // out-of-vocabulary term = schema violation
                }
            }

            // Permit type: same rule.
            PermitTypeCode? permitType = null;
            if (Field("permitType") is { } typeClaim)
            {
                // Accept complete names and common UI synonyms, never partial
                // matches such as "weekday" or "longterm".
                permitType = typeClaim.ToLowerInvariant() switch
                {
                    "day" or "daily" or "day pass" or "day permit" or "daily permit" => PermitTypeCode.Daily,
                    "term" or "semester" or "term permit" or "semester permit" or "term / semester permit" => PermitTypeCode.Term,
                    _ => null
                };
                if (permitType is null)
                {
                    return null;
                }
            }

            // Plate state: two letters, must be a real US state code.
            string? carState = null;
            if (Field("carState") is { } stateClaim)
            {
                var code = stateClaim.Trim().ToUpperInvariant();
                if (code.Length == 2 && Enum.IsDefined(typeof(UsState), code))
                {
                    carState = code;
                }
            }

            return new ExtractionResult
            {
                College = college,
                PermitType = permitType,
                Term = term,
                CarMake = Field("carMake"),
                CarModel = Field("carModel"),
                CarColor = CanonicalColor(Field("carColor")),
                Plate = FallbackExtractor.ResolvePlate(studentText, Field("plate")),
                CarState = carState,
                Source = source,
            };
        }
    }

    /// <summary>Canonical known color name; numeric or unknown claims stay missing.</summary>
    private static string? CanonicalColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var trimmed = color.Trim();
        if (string.Equals(trimmed, "grey", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(VehicleColor.Gray);
        }

        return Enum.GetNames<VehicleColor>().FirstOrDefault(
            name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Models sometimes wrap JSON in ``` fences despite format:"json".</summary>
    private static string StripCodeFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstNewline + 1)..];
        var fence = body.LastIndexOf("```", StringComparison.Ordinal);
        return fence >= 0 ? body[..fence].Trim() : body.Trim();
    }

    // ------------------------------------------------------------------
    // The prompt: strict schema, catalog colleges only, no prices ever
    // ------------------------------------------------------------------

    public static string BuildSystemPrompt(IFeeCatalog catalog)
    {
        var campusList = string.Join("; ", catalog.All().Select(c => c.Campus));
        return
            "You extract parking-permit facts from a student's message. " +
            "Respond with ONLY a JSON object — no prose, no explanation, no markdown. " +
            "Use exactly these keys: \"college\", \"permitType\", \"term\", \"carMake\", " +
            "\"carModel\", \"carColor\", \"plate\", \"carState\". " +
            "Every value is a string or null; use null for anything the student did not say. " +
            "\"college\" must be copied exactly from this list, or null if none fits: " +
            campusList + ". " +
            "\"permitType\" is \"Daily\" or \"Term\". " +
            "\"term\" is \"Fall\", \"Spring\", or \"Summer\". " +
            "\"carState\" is a two-letter US state code. " +
            "Never output a price, fee, amount, or cost — prices are not your job.";
    }
}
