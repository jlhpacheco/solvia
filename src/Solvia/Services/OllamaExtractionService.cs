using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// The Ollama adapter asks Microsoft Phi-4-mini to read a permit draft
/// out of the student's plain words (REDO-PLAN §3 R3).
///
/// Default inference is local to the application host. Configuring a remote
/// endpoint sends extraction inputs there; this service implements no training.
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
/// - ANY failure — Ollama down, timeout (10 s default; configurable
///   1–90 s budget), HTTP error, bad
///   JSON, schema violation — lands in <see cref="FallbackExtractor"/>
///   with Source=Fallback. No exception escapes this service.
/// </summary>
public sealed class OllamaExtractionService : IAiExtractor
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ModelExtractionContract _contract;
    private readonly FallbackExtractor _fallback;
    private readonly ILogger<OllamaExtractionService> _logger;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;
    private readonly string _systemPrompt;

    public OllamaExtractionService(
        HttpClient http,
        IOptions<OllamaOptions> options,
        IFeeCatalog catalog,
        FallbackExtractor fallback,
        ILogger<OllamaExtractionService> logger,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        _http = http;
        _options = options.Value;
        _contract = new ModelExtractionContract(catalog);
        _fallback = fallback;
        _logger = logger;
        // The optional constructor override remains available to offline tests.
        // Runtime configuration is bounded by OllamaOptions.TimeoutSeconds.
        _timeout = timeout ?? TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _clock = timeProvider ?? TimeProvider.System;

        // DI sets the base address from OllamaOptions when the client
        // is registered; when the client was built by hand (tests),
        // fall back to the options value.
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        _systemPrompt = ModelExtractionContract.BuildSystemPrompt(catalog);
    }

    public async Task<ExtractionResult> ExtractAsync(
        string studentText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ai = await TryModelAsync(studentText, cancellationToken);
            if (ai is not null)
            {
                return ai; // The shared model contract has already grounded the plate.
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
        var started = _clock.GetTimestamp();
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
        cancellationToken.ThrowIfCancellationRequested();
        // Timer cancellation can be dispatched late when the host is busy.
        // Independently reject output observed after the monotonic deadline.
        if (_clock.GetElapsedTime(started) >= _timeout) return null;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Ollama answered HTTP {Status}; using the deterministic fallback.",
                (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        cancellationToken.ThrowIfCancellationRequested();
        if (_clock.GetElapsedTime(started) >= _timeout) return null;
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentEl) ||
            contentEl.GetString() is not { Length: > 0 } content)
        {
            return null;
        }

        var result = _contract.Parse(content, studentText, ExtractionSource.Ollama);
        cancellationToken.ThrowIfCancellationRequested();
        return _clock.GetElapsedTime(started) < _timeout ? result : null;
    }

}
