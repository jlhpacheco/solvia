using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// The AI extractor: asks the local model — Microsoft Phi-4-mini,
/// served by Ollama on the SAME computer — to read a permit draft
/// out of the student's plain words (REDO-PLAN §3 R3).
///
/// Privacy by design: the only thing that leaves this process is an
/// HTTP call to Ollama on localhost. Student data never reaches a
/// vendor cloud and can never be used to train any model.
///
/// Trust rules:
/// - The prompt demands ONLY a JSON object in our schema, lists the
///   10 real campus names from the fee catalog, and forbids prices —
///   the model may not invent a college or a fee.
/// - The answer is validated before it is believed: the college must
///   resolve against <see cref="IFeeCatalog"/> (a name the catalog
///   does not know rejects the WHOLE answer), term and permit type
///   must be in vocabulary, and no code path here reads a price —
///   even if the model emits one it is ignored, because
///   <see cref="ExtractionResult"/> has no price member to land on.
/// - ANY failure — Ollama down, timeout (10 s cap), HTTP error, bad
///   JSON, schema violation — lands in <see cref="FallbackExtractor"/>
///   with Source=Fallback. No exception escapes this service.
/// </summary>
public sealed class OllamaExtractionService : IAiExtractor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly IFeeCatalog _catalog;
    private readonly FallbackExtractor _fallback;
    private readonly ILogger<OllamaExtractionService> _logger;
    private readonly TimeSpan _timeout;
    private readonly string _systemPrompt;

    public OllamaExtractionService(
        HttpClient http,
        IOptions<OllamaOptions> options,
        IFeeCatalog catalog,
        FallbackExtractor fallback,
        ILogger<OllamaExtractionService> logger,
        TimeSpan? timeout = null)
    {
        _http = http;
        _options = options.Value;
        _catalog = catalog;
        _fallback = fallback;
        _logger = logger;
        _timeout = timeout ?? DefaultTimeout;

        // DI sets the base address from OllamaOptions when the client
        // is registered; when the client was built by hand (tests),
        // fall back to the options value.
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        _systemPrompt = BuildSystemPrompt(catalog);
    }

    public async Task<ExtractionResult> ExtractAsync(
        string studentText,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ai = await TryModelAsync(studentText, cancellationToken);
            if (ai is not null)
            {
                return ai;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException ||
                                   !cancellationToken.IsCancellationRequested)
        {
            // Ollama down / refused / timed out / unreadable answer —
            // all expected, all land in the fallback. The student's own
            // words are never logged; only the failure kind.
            _logger.LogInformation(
                "AI extraction unavailable ({Reason}); using the deterministic fallback.",
                ex.GetType().Name);
        }

        return _fallback.Extract(studentText);
    }

    /// <summary>
    /// One Ollama round trip. Returns the validated result, or null
    /// when the model's answer failed validation (the caller then
    /// falls back). Throws only for transport-level failures, which
    /// the caller also turns into the fallback.
    /// </summary>
    private async Task<ExtractionResult?> TryModelAsync(
        string studentText,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var payload = new
        {
            model = _options.Model,
            stream = false,
            format = "json",
            options = new { temperature = 0 },
            messages = new[]
            {
                new { role = "system", content = _systemPrompt },
                new { role = "user", content = studentText },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Ollama answered HTTP {Status}; using the deterministic fallback.",
                (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentEl) ||
            contentEl.GetString() is not { Length: > 0 } content)
        {
            return null;
        }

        return ValidateModelJson(content);
    }

    // ------------------------------------------------------------------
    // Validation: the model's JSON is a claim, not the truth
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses and validates the model's JSON content. Returns null —
    /// reject the whole answer — when the JSON is unparseable, the
    /// college cannot be resolved against the fee catalog, or a
    /// vocabulary field (term / permit type) is out of vocabulary.
    /// Unknown extra fields — including any price the model might
    /// invent — are ignored: nothing here reads them.
    /// </summary>
    private ExtractionResult? ValidateModelJson(string content)
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
                var resolved = _catalog.Find(collegeClaim) ??
                               FallbackExtractor.FindCampusMention(_catalog, collegeClaim);
                if (resolved is null)
                {
                    _logger.LogInformation(
                        "AI named a college the catalog does not know; " +
                        "rejecting the AI answer and using the fallback.");
                    return null;
                }

                college = resolved.Campus;
            }

            // Term: null stays null; a stated value must be in vocabulary.
            TermKind? term = null;
            if (Field("term") is { } termClaim)
            {
                var lowerTerm = termClaim.ToLowerInvariant();
                term =
                    lowerTerm.Contains("fall") || lowerTerm.Contains("autumn")
                        ? TermKind.Fall
                        : lowerTerm.Contains("spring")
                            ? TermKind.Spring
                            : lowerTerm.Contains("summer")
                                ? TermKind.Summer
                                : null;
                if (term is null)
                {
                    return null; // out-of-vocabulary term = schema violation
                }
            }

            // Permit type: same rule.
            PermitTypeCode? permitType = null;
            if (Field("permitType") is { } typeClaim)
            {
                var lower = typeClaim.ToLowerInvariant();
                permitType =
                    lower.Contains("day") || lower.Contains("daily")
                        ? PermitTypeCode.Daily
                        : lower.Contains("term") || lower.Contains("semester")
                            ? PermitTypeCode.Term
                            : null;
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
                if (code.Length == 2 && Enum.TryParse<UsState>(code, out _))
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
                Plate = FallbackExtractor.NormalizePlate(Field("plate")),
                CarState = carState,
                Source = ExtractionSource.Ollama,
            };
        }
    }

    /// <summary>Canonical color name when it is a known VehicleColor; else the raw word.</summary>
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

        return Enum.TryParse<VehicleColor>(trimmed, ignoreCase: true, out var parsed)
            ? parsed.ToString()
            : trimmed;
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

    private static string BuildSystemPrompt(IFeeCatalog catalog)
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
