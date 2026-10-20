using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Solvia.Config;

namespace Solvia.Services;

/// <summary>
/// Server-side Meta Model API adapter. Hosted inputs leave the app host.
/// Only Standard Muse Spark model IDs are permitted; no tools or payments
/// are exposed to the model. Failures use the clearly labeled fallback.
/// Contract: https://dev.meta.ai/docs/api-reference/chat-completions/schemas
/// </summary>
public sealed class MetaExtractionService : IAiExtractor
{
    private static readonly Uri Endpoint = new("https://api.meta.ai/v1/chat/completions");
    private readonly HttpClient _http;
    private readonly MetaOptions _options;
    private readonly FallbackExtractor _fallback;
    private readonly ILogger<MetaExtractionService> _logger;
    private readonly ModelExtractionContract _contract;
    private readonly string _systemPrompt;

    public MetaExtractionService(HttpClient http, IOptions<MetaOptions> options,
        IFeeCatalog catalog, FallbackExtractor fallback, ILogger<MetaExtractionService> logger)
    {
        _http = http;
        _options = options.Value;
        _fallback = fallback;
        _logger = logger;
        _contract = new ModelExtractionContract(catalog);
        _systemPrompt = ModelExtractionContract.BuildSystemPrompt(catalog);
    }

    public async Task<ExtractionResult> ExtractAsync(string studentText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            _options.Model is not ("muse-spark-1.3" or "muse-spark-1.2" or "muse-spark-1.1"))
        {
            return _fallback.Extract(studentText);
        }

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var payload = new
            {
                model = _options.Model,
                stream = false,
                store = false,
                reasoning_effort = "minimal",
                max_completion_tokens = 1024,
                messages = new[]
                {
                    new { role = "system", content = _systemPrompt },
                    new { role = "user", content = studentText }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "solvia_permit_draft",
                        strict = true,
                        schema = _contract.JsonSchema()
                    }
                }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Do not read or log error bodies: they can contain inputs or credentials.
                _logger.LogInformation("Meta extraction returned HTTP {Status}; using fallback.", (int)response.StatusCode);
                return _fallback.Extract(studentText);
            }

            var body = await response.Content.ReadAsStringAsync(budget.Token);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() == 1 &&
                choices[0].TryGetProperty("finish_reason", out var finish) && finish.GetString() == "stop" &&
                choices[0].TryGetProperty("message", out var message) &&
                (!message.TryGetProperty("refusal", out var refusal) || refusal.ValueKind == JsonValueKind.Null) &&
                message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String &&
                content.GetString() is { Length: > 0 } modelJson &&
                _contract.Parse(modelJson, studentText, ExtractionSource.Meta, requireCompleteShape: true) is { } result)
            {
                return result;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            // Exception messages and raw provider data are deliberately excluded.
            _logger.LogInformation("Meta extraction unavailable ({FailureKind}); using fallback.", exception.GetType().Name);
        }

        return _fallback.Extract(studentText);
    }
}
