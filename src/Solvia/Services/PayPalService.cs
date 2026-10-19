using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;

namespace Solvia.Services;

/// <summary>
/// The PayPal sandbox REST client (REDO-PLAN R6) — the ONLY code in
/// Solvia that talks to PayPal: OAuth token, order create/capture,
/// and webhook-signature verification (for R7). Talks ONLY to the
/// configured sandbox base URL (https://api-m.sandbox.paypal.com)
/// with OAuth client credentials.
///
/// Secrecy rules, enforced here:
/// - The client secret comes from <see cref="PayPalOptions"/> (env
///   vars / user-secrets) and is used in exactly one place — the
///   token call's Basic authorization header.
/// - Nothing about a credential is ever logged: log lines carry
///   order ids, statuses, and PayPal's own error names only.
/// </summary>
public sealed class PayPalService : IPayPalService
{
    private readonly HttpClient _http;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalService> _logger;

    public PayPalService(
        HttpClient http,
        IOptions<PayPalOptions> options,
        ILogger<PayPalService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        // DI sets the base address from PayPalOptions (Program.cs);
        // when the client was built by hand (tests), fall back to
        // the options.
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");

        // The ONLY use of the secret anywhere in Solvia: this Basic
        // header, on this one call. It is never logged or stored.
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
        });

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await ReadBodyOrThrowAsync(response, "token request", cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.TryGetProperty("access_token", out var tokenEl)
            ? tokenEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new PayPalApiException(
                "token request", response.StatusCode, null,
                "PayPal's token response did not include an access_token.");
        }

        return token;
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(
        int amountCents,
        string reference,
        string? returnUrl = null,
        string? cancelUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (amountCents <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountCents), amountCents,
                "A PayPal order needs at least 1 cent. Free ($0) permits are " +
                "confirmed without PayPal — see CheckoutService.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        // Exact money: cents -> a fixed two-decimal string (3000 -> "30.00").
        var value = (amountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        var currency = string.IsNullOrWhiteSpace(_options.Currency)
            ? "USD"
            : _options.Currency;

        var payload = new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = reference,
                    ["custom_id"] = reference,
                    ["description"] = "Solvia parking permit",
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = currency,
                        ["value"] = value,
                    },
                },
            },
        };

        if (returnUrl is not null || cancelUrl is not null)
        {
            var context = new JsonObject
            {
                ["brand_name"] = "Solvia",
                ["shipping_preference"] = "NO_SHIPPING",
                ["user_action"] = "PAY_NOW",
            };
            if (returnUrl is not null)
            {
                context["return_url"] = returnUrl;
            }

            if (cancelUrl is not null)
            {
                context["cancel_url"] = cancelUrl;
            }

            payload["application_context"] = context;
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // One create call per checkout start is a promise the money
        // brain keeps (item 30 idempotency); the reference doubles
        // as PayPal's own deduplication key.
        request.Headers.Add("PayPal-Request-Id", $"solvia-{reference}");
        request.Content = new StringContent(
            payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await ReadBodyOrThrowAsync(response, "create order", cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var orderId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        var approveUrl = FindLink(root, "approve") ?? FindLink(root, "payer-action");
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(approveUrl))
        {
            throw new PayPalApiException(
                "create order", response.StatusCode, null,
                "PayPal's create-order response did not include an order id and approval link.");
        }

        _logger.LogInformation(
            "Created PayPal sandbox order {OrderId} for {AmountCents} cents (reference {Reference}).",
            orderId, amountCents, reference);
        return new PayPalOrderResult(orderId, status ?? "", approveUrl);
    }

    public async Task<PayPalCaptureResult> CaptureOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Prefer", "return=representation");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await ReadBodyOrThrowAsync(response, "capture order", cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var status = root.TryGetProperty("status", out var statusEl)
            ? statusEl.GetString() ?? ""
            : "";

        // The capture id lives at purchase_units[0].payments.captures[0].id.
        string? captureId = null;
        if (root.TryGetProperty("purchase_units", out var units) &&
            units.ValueKind == JsonValueKind.Array && units.GetArrayLength() > 0 &&
            units[0].TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("captures", out var captures) &&
            captures.ValueKind == JsonValueKind.Array && captures.GetArrayLength() > 0 &&
            captures[0].TryGetProperty("id", out var captureEl))
        {
            captureId = captureEl.GetString();
        }

        _logger.LogInformation(
            "Captured PayPal sandbox order {OrderId}: status {Status}, capture {CaptureId}.",
            orderId, status, captureId ?? "(none)");
        return new PayPalCaptureResult(status, captureId);
    }

    public async Task<bool> VerifyWebhookSignatureAsync(
        string transmissionId,
        string transmissionTime,
        string transmissionSig,
        string certUrl,
        string authAlgo,
        string webhookEventJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transmissionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookEventJson);

        // No webhook id configured -> nothing to verify against.
        // R7 treats false as a failed verification (401), which is
        // the honest answer until a webhook is registered.
        if (string.IsNullOrWhiteSpace(_options.WebhookId))
        {
            _logger.LogWarning(
                "Webhook signature check skipped: PayPal:WebhookId is not configured.");
            return false;
        }

        JsonNode? eventNode;
        try
        {
            eventNode = JsonNode.Parse(webhookEventJson);
        }
        catch (JsonException)
        {
            // A body that is not JSON cannot be a verified event.
            return false;
        }

        var payload = new JsonObject
        {
            ["auth_algo"] = authAlgo,
            ["cert_url"] = certUrl,
            ["transmission_id"] = transmissionId,
            ["transmission_sig"] = transmissionSig,
            ["transmission_time"] = transmissionTime,
            ["webhook_id"] = _options.WebhookId,
            ["webhook_event"] = eventNode,
        };

        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "v1/notifications/verify-webhook-signature");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await ReadBodyOrThrowAsync(
            response, "verify webhook signature", cancellationToken);

        using var doc = JsonDocument.Parse(body);
        var verified = doc.RootElement.TryGetProperty("verification_status", out var statusEl) &&
            string.Equals(statusEl.GetString(), "SUCCESS", StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation(
            "PayPal webhook signature verification for transmission {TransmissionId}: {Verified}.",
            transmissionId, verified ? "SUCCESS" : "FAILURE");
        return verified;
    }

    /// <summary>
    /// Fails fast — before any HTTP call — when the sandbox
    /// credentials are missing, naming the settings to supply. The
    /// message never contains a credential value.
    /// </summary>
    private void EnsureConfigured()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            missing.Add("PAYPAL_CLIENT_ID (PayPal:ClientId)");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            missing.Add("PAYPAL_CLIENT_SECRET (PayPal:ClientSecret)");
        }

        if (missing.Count > 0)
        {
            throw new PayPalNotConfiguredException(
                "PayPal sandbox credentials are not configured — missing: " +
                string.Join(", ", missing) +
                ". Create a free sandbox app at developer.paypal.com, then set " +
                "those environment variables (or the matching user-secrets) and " +
                "restart Solvia. Until then no payment can be started.");
        }
    }

    /// <summary>
    /// Returns the success body; on an error status, throws a
    /// <see cref="PayPalApiException"/> built from PayPal's own error
    /// name/message (which never contain our credentials).
    /// </summary>
    private async Task<string> ReadBodyOrThrowAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        string? name = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("name", out var nameEl))
            {
                name = nameEl.GetString();
            }

            if (doc.RootElement.TryGetProperty("message", out var messageEl))
            {
                message = messageEl.GetString();
            }
        }
        catch (JsonException)
        {
            // PayPal answered with a non-JSON error body — the status
            // code alone is the honest report.
        }

        _logger.LogWarning(
            "PayPal sandbox {Operation} failed: HTTP {StatusCode} {ErrorName}.",
            operation, (int)response.StatusCode, name ?? "(no error name)");
        throw new PayPalApiException(operation, response.StatusCode, name, message);
    }

    private static string? FindLink(JsonElement root, string rel)
    {
        if (!root.TryGetProperty("links", out var links) ||
            links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            if (link.TryGetProperty("rel", out var relEl) &&
                string.Equals(relEl.GetString(), rel, StringComparison.Ordinal) &&
                link.TryGetProperty("href", out var hrefEl))
            {
                return hrefEl.GetString();
            }
        }

        return null;
    }
}
