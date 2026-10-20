using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Solvia.Config;

namespace Solvia.Services;

/// <summary>
/// Server-side Together AI API adapter. Hosted inputs leave the app host.
/// Only the selected Qwen3.5 9B model is permitted; no tools or payments
/// are exposed to the model. Failures use the clearly labeled fallback.
/// Contract: https://docs.together.ai/docs/inference/chat/structured-outputs
/// </summary>
public sealed class TogetherExtractionService : IAiExtractor
{
    private static readonly Uri Endpoint = new("https://api.together.ai/v1/chat/completions");
    private readonly HttpClient _http;
    private readonly TogetherOptions _options;
    private readonly FallbackExtractor _fallback;
    private readonly ILogger<TogetherExtractionService> _logger;
    private readonly ModelExtractionContract _contract;
    private readonly object _schema;
    private readonly string _systemPrompt;
    private readonly TimeProvider _clock;

    public TogetherExtractionService(HttpClient http, IOptions<TogetherOptions> options,
        IFeeCatalog catalog, FallbackExtractor fallback, ILogger<TogetherExtractionService> logger,
        TimeProvider? timeProvider = null)
    {
        _http = http;
        _options = options.Value;
        _fallback = fallback;
        _logger = logger;
        _clock = timeProvider ?? TimeProvider.System;
        _contract = new ModelExtractionContract(catalog);
        _schema = _contract.JsonSchema();
        _systemPrompt = ModelExtractionContract.BuildSystemPrompt(catalog) +
            " Respond only in JSON following this schema: " + JsonSerializer.Serialize(_schema);
    }

    public async Task<ExtractionResult> ExtractAsync(string studentText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            _options.Model != "Qwen/Qwen3.5-9B")
        {
            return _fallback.Extract(studentText);
        }

        try
        {
            var started = _clock.GetTimestamp();
            var allowed = TimeSpan.FromSeconds(_options.TimeoutSeconds);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(allowed);
            var payload = new
            {
                model = _options.Model,
                stream = false,
                reasoning = new { enabled = false },
                temperature = 0,
                max_tokens = 512,
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
                        schema = _schema
                    }
                }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            cancellationToken.ThrowIfCancellationRequested();
            // A busy host can dispatch the cancellation timer late. The
            // monotonic elapsed budget still prevents trusting late output.
            if (_clock.GetElapsedTime(started) >= allowed) return _fallback.Extract(studentText);
            if (!response.IsSuccessStatusCode)
            {
                // Do not read or log error bodies: they can contain inputs or credentials.
                _logger.LogInformation("Together extraction returned HTTP {Status}; using fallback.", (int)response.StatusCode);
                return _fallback.Extract(studentText);
            }

            var body = await response.Content.ReadAsStringAsync(budget.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetElapsedTime(started) >= allowed) return _fallback.Extract(studentText);
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
                _contract.Parse(modelJson, studentText, ExtractionSource.Together, requireCompleteShape: true) is { } result)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_clock.GetElapsedTime(started) < allowed) return result;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            // Exception messages and raw provider data are deliberately excluded.
            _logger.LogInformation("Together extraction unavailable ({FailureKind}); using fallback.", exception.GetType().Name);
        }

        return _fallback.Extract(studentText);
    }
}
