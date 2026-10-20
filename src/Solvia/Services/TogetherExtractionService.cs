using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Solvia.Config;

namespace Solvia.Services;

/// <summary>
/// Server-side Together AI API adapter. Hosted inputs leave the app host.
/// Only the selected OpenAI GPT-OSS 120B model is permitted; no tools or payments
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
            _options.Model != TogetherOptions.SupportedModel)
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
                reasoning_effort = "low",
                temperature = 1,
                // GPT-OSS counts internal reasoning and final JSON against
                // the same bounded completion budget. Never expose reasoning.
                max_tokens = 2048,
                messages = new[]
                {
                    new { role = "developer", content = _systemPrompt },
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

            var body = await ReadBoundedAsync(response.Content, budget.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (body is null || _clock.GetElapsedTime(started) >= allowed) return _fallback.Extract(studentText);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("model", out var responseModel) && responseModel.ValueKind == JsonValueKind.String &&
                responseModel.GetString() == TogetherOptions.SupportedModel &&
                root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() == 1 &&
                choices[0].TryGetProperty("finish_reason", out var finish) && finish.GetString() == "stop" &&
                choices[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
                (!message.TryGetProperty("refusal", out var refusal) || refusal.ValueKind == JsonValueKind.Null) &&
                (!message.TryGetProperty("tool_calls", out var tools) || tools.ValueKind == JsonValueKind.Null ||
                    (tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() == 0)) &&
                (!message.TryGetProperty("function_call", out var function) || function.ValueKind == JsonValueKind.Null) &&
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

        cancellationToken.ThrowIfCancellationRequested();
        return _fallback.Extract(studentText);
    }

    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        const int maximum = 64 * 1024;
        if (content.Headers.ContentLength > maximum) return null;
        using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(bytes, token)) > 0)
        {
            if (buffer.Length + count > maximum) return null;
            buffer.Write(bytes, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
