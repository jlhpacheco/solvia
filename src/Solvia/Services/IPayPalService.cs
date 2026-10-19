using System.Net;

namespace Solvia.Services;

/// <summary>
/// PayPal sandbox client contract (REDO-PLAN R6, items 30–32).
/// <see cref="PayPalService"/> is the ONLY PayPal talker in Solvia:
/// OAuth, order create/capture, and webhook-signature verification
/// all live behind this interface, against the PayPal SANDBOX only
/// (https://api-m.sandbox.paypal.com) — fictitious accounts, no real
/// money.
///
/// The amount sent to PayPal is always the permit's snapshotted
/// <c>PriceCents</c> (resolved from the college's real published fee
/// by R1's PricingService) — never a PermitType placeholder and never
/// a number typed into a form.
/// </summary>
public interface IPayPalService
{
    /// <summary>
    /// Exchanges the configured client credentials (PayPalOptions —
    /// env vars / user-secrets ONLY) for an OAuth access token.
    /// Throws <see cref="PayPalNotConfiguredException"/> when the
    /// client id or secret is missing — before any HTTP call.
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a sandbox order: intent CAPTURE, a single purchase
    /// unit for exactly <paramref name="amountCents"/> in the
    /// configured currency, tagged with <paramref name="reference"/>
    /// (the permit application id). When
    /// <paramref name="returnUrl"/> / <paramref name="cancelUrl"/>
    /// are given they point back at Solvia's own return/cancel
    /// endpoints, so PayPal sends the buyer home after approval.
    /// Returns the new order id and the URL the buyer is redirected
    /// to in order to approve the payment on sandbox.paypal.com.
    /// </summary>
    Task<PayPalOrderResult> CreateOrderAsync(
        int amountCents,
        string reference,
        string? returnUrl = null,
        string? cancelUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures an approved order. The returned status is the ONLY
    /// truth about the money: a permit may be marked Paid solely
    /// when this returns COMPLETED (see CheckoutService). A
    /// non-COMPLETED status — or a PayPal API error — never means
    /// Paid.
    /// </summary>
    Task<PayPalCaptureResult> CaptureOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an inbound webhook delivery's signature with PayPal
    /// (the <c>verify-webhook-signature</c> call; REDO-PLAN item 32,
    /// consumed by R7's webhook endpoint). The webhook id comes from
    /// <c>PayPal:WebhookId</c> config; when no webhook id is
    /// configured there is nothing to verify against and this
    /// returns false. Returns true only when PayPal answers
    /// <c>verification_status = SUCCESS</c>.
    /// </summary>
    Task<bool> VerifyWebhookSignatureAsync(
        string transmissionId,
        string transmissionTime,
        string transmissionSig,
        string certUrl,
        string authAlgo,
        string webhookEventJson,
        CancellationToken cancellationToken = default);
}

/// <summary>A created PayPal order: its id, PayPal's status, and the buyer approval URL.</summary>
public sealed record PayPalOrderResult(string OrderId, string Status, string ApproveUrl);

/// <summary>
/// A capture attempt's outcome: PayPal's order status (Paid requires
/// exactly "COMPLETED") and the capture id when PayPal issued one.
/// </summary>
public sealed record PayPalCaptureResult(string Status, string? CaptureId,
    int? AmountCents = null, string? Currency = null);

/// <summary>
/// The PayPal sandbox credentials are not configured on this machine.
/// The message names the missing settings (PAYPAL_CLIENT_ID /
/// PAYPAL_CLIENT_SECRET) — it NEVER contains a credential value.
/// Callers turn this into the honest 503 / not-configured state.
/// </summary>
public sealed class PayPalNotConfiguredException : InvalidOperationException
{
    public PayPalNotConfiguredException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// PayPal's API answered with an error. Carries PayPal's own error
/// name and HTTP status for logging/display; never carries
/// credentials.
/// </summary>
public sealed class PayPalApiException : Exception
{
    public PayPalApiException(
        string operation,
        HttpStatusCode statusCode,
        string? payPalErrorName,
        string? payPalMessage)
        : base(BuildMessage(operation, statusCode, payPalErrorName, payPalMessage))
    {
        Operation = operation;
        StatusCode = statusCode;
        PayPalErrorName = payPalErrorName;
    }

    public string Operation { get; }

    public HttpStatusCode StatusCode { get; }

    public string? PayPalErrorName { get; }

    private static string BuildMessage(
        string operation,
        HttpStatusCode statusCode,
        string? payPalErrorName,
        string? payPalMessage)
    {
        var text = $"PayPal sandbox {operation} failed with HTTP {(int)statusCode} ({statusCode})";
        if (!string.IsNullOrWhiteSpace(payPalErrorName))
        {
            text += $": {payPalErrorName}";
        }

        if (!string.IsNullOrWhiteSpace(payPalMessage))
        {
            text += $" — {payPalMessage}";
        }

        return text;
    }
}
