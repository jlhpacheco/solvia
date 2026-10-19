using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models;

namespace Solvia.Services;

/// <summary>
/// The payment-complete webhook (BUILD-PLAN T7 — the app side of the
/// Zapier receipt flow, docs/zapier-recipe.md).
///
/// When <see cref="CheckoutService"/> marks a permit Paid — or confirms
/// a $0 FREE permit — it calls <see cref="NotifyPermitIssuedAsync"/>
/// AFTER the payment truth is already saved. This service then POSTs
/// one small JSON event to the configured <c>Webhook:Url</c> (built for
/// a Zapier "Catch Hook" trigger):
///
///   { permitNumber, campus, amountCents, paypalCaptureId, paidAtUtc }
///
/// Deliberate properties:
/// - DISABLED when the URL is empty (the default): no call, no noise.
/// - NEVER throws and NEVER blocks a payment: every failure (endpoint
///   down, non-2xx, timeout past a few seconds) is caught and logged,
///   and the already-saved Paid state stands. A dead webhook can cost
///   a receipt email — it can never cost or roll back a payment.
/// - NO student PII in the event: no name, no email, no plate — permit
///   facts only. The Zap maps them into the receipt email and the
///   ledger row on Zapier's side.
/// </summary>
public sealed class PaymentWebhookService
{
    /// <summary>Hard cap on one send, so a hung endpoint can't stall checkout.</summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly WebhookOptions _options;
    private readonly ILogger<PaymentWebhookService> _logger;

    public PaymentWebhookService(
        HttpClient http,
        IOptions<WebhookOptions> options,
        ILogger<PaymentWebhookService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>True when a webhook URL is configured and events will be sent.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.Url);

    /// <summary>
    /// Announces that a permit just went live. Safe to call on every
    /// Paid / free-issued transition: it no-ops when disabled and
    /// swallows (with a log line) every send failure.
    /// </summary>
    public async Task NotifyPermitIssuedAsync(
        PermitApplication application,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogDebug(
                "Payment webhook disabled (Webhook:Url is empty) — " +
                "no event sent for permit {PermitNumber}.",
                application.ReceiptNumber);
            return;
        }

        var payload = new
        {
            permitNumber = application.ReceiptNumber,
            campus = application.Campus,
            amountCents = application.PriceCents,
            paypalCaptureId = application.PayPalCaptureId,
            paidAtUtc = application.PaidAtUtc,
        };

        try
        {
            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);

            using var response = await _http.PostAsJsonAsync(
                _options.Url, payload, timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Payment webhook sent for permit {PermitNumber} (HTTP {Status}).",
                    application.ReceiptNumber, (int)response.StatusCode);
            }
            else
            {
                _logger.LogWarning(
                    "Payment webhook for permit {PermitNumber} answered HTTP {Status} — " +
                    "the permit itself is unaffected.",
                    application.ReceiptNumber, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget by contract: log and move on. The payment
            // was saved before this call and is never revisited here.
            _logger.LogWarning(
                ex,
                "Payment webhook for permit {PermitNumber} failed to send — " +
                "the permit itself is unaffected.",
                application.ReceiptNumber);
        }
    }
}
