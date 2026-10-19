using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models;

namespace Solvia.Services;

/// <summary>
/// The payment-complete outbound webhook (REDO-PLAN item 35 — the
/// app side of the Zapier receipt flow, docs/zapier-recipe.md).
///
/// When a permit goes live (paid, or free-issued) and that truth has
/// been SAVED, the transition's owner calls
/// <see cref="NotifyPermitIssuedAsync"/>: R7's inbound PayPal webhook
/// handler fires it directly after its own issuance + save, and the
/// checkout capture path fires it through a one-line optional-
/// dependency hook (filed as a change request to R6 — this service
/// never reaches into CheckoutService). The POST goes to the
/// configured <c>Webhook:Url</c> (built for a Zapier "Catch Hook"
/// trigger) with one small JSON event:
///
///   { permitNumber, campus, amountCents, paypalCaptureId,
///     paidAtUtc, receiptIssuedAtUtc, verifyUrl }
///
/// The <c>verifyUrl</c> and <c>receiptIssuedAtUtc</c> are the redo's
/// gains over the first build: the Zap's receipt email and ledger
/// row can carry the public permit link and the exact moment the
/// permit was minted. The link's origin comes from the caller when
/// it has one, else from the configured <c>Webhook:PublicBaseUrl</c>
/// (the capture path has no request to read an origin from).
///
/// Deliberate properties:
/// - DISABLED when the URL is empty (the default): no call, no noise.
/// - NEVER throws and NEVER blocks a payment: every failure (endpoint
///   down, non-2xx, timeout past a few seconds) is caught and logged,
///   and the already-saved live state stands. A dead webhook can
///   cost a receipt email — it can never cost or roll back a permit.
/// - NO student PII in the event: no name, no email, no plate —
///   permit facts only. The Zap maps them into the receipt email
///   and the ledger row on Zapier's side.
/// </summary>
public sealed class PaymentWebhookService
{
    /// <summary>Hard cap on one send, so a hung endpoint can't stall the caller.</summary>
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
    /// Announces that a permit just went live. Call ONLY after the
    /// live transition has been saved — this service never saves and
    /// never retries a state change. Safe to call on every Paid /
    /// free-issued transition: it no-ops when disabled and swallows
    /// (with a log line) every send failure.
    /// <paramref name="publicBaseUrl"/> is the app's public origin
    /// (scheme + host) when the caller has one; the verify URL is
    /// built from it and the permit's unguessable token. A caller
    /// with no request of its own (the checkout capture path) passes
    /// null, and the origin falls back to the configured
    /// <c>Webhook:PublicBaseUrl</c> — an explicit caller origin
    /// always wins over the configured one. With neither, the event
    /// simply carries no verify URL; an origin is never invented.
    /// </summary>
    public async Task NotifyPermitIssuedAsync(
        PermitApplication application,
        string? publicBaseUrl = null,
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

        var origin = !string.IsNullOrWhiteSpace(publicBaseUrl)
            ? publicBaseUrl
            : _options.PublicBaseUrl;

        var verifyUrl = !string.IsNullOrWhiteSpace(origin) &&
                        !string.IsNullOrWhiteSpace(application.VerifyToken)
            ? $"{origin.TrimEnd('/')}/verify?t={application.VerifyToken}"
            : null;

        var payload = new
        {
            permitNumber = application.ReceiptNumber,
            campus = application.Campus,
            amountCents = application.PriceCents,
            paypalCaptureId = application.PayPalCaptureId,
            paidAtUtc = application.PaidAtUtc,
            receiptIssuedAtUtc = application.ReceiptIssuedAtUtc,
            verifyUrl,
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
            // Fire-and-forget by contract: log and move on. The permit
            // was saved before this call and is never revisited here.
            _logger.LogWarning(
                ex,
                "Payment webhook for permit {PermitNumber} failed to send — " +
                "the permit itself is unaffected.",
                application.ReceiptNumber);
        }
    }
}
