using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>How one webhook delivery ended: the HTTP answer for PayPal plus the outcome word the event log carries.</summary>
public sealed record PayPalWebhookOutcome(int StatusCode, string Outcome);

/// <summary>
/// The inbound PayPal webhook brain (REDO-PLAN item 33, PORT of the
/// reference app's rules with the redo's improvement kept). The
/// controller is a shell; every decision lives here:
///
/// - Empty body, missing signature headers, missing credentials, or
///   a signature PayPal will not verify → 401, and NOTHING is saved.
/// - A parseable delivery with no event id → 400 (there is nothing
///   to key idempotency on).
/// - Signature verification is delegated to the ONE PayPal talker
///   (<see cref="IPayPalService.VerifyWebhookSignatureAsync"/> —
///   item 32), webhook id from <c>PayPal:WebhookId</c> config.
/// - Idempotency is the <see cref="WebhookEventLog"/> table's unique
///   (Provider, EventId) pair: a redelivery of an event already on
///   file is a logged no-op — it never acts twice, never re-issues,
///   never re-fires the outbound webhook.
/// - Only <c>PAYMENT.CAPTURE.COMPLETED</c> acts. It finds the
///   application BY CAPTURE ID (falling back to the order id PayPal
///   embeds in the event, for the capture that landed at PayPal
///   before our own capture call recorded it). Then the money rule:
///   the event amount must equal the application's PriceCents
///   snapshot and the application must be a payable one — a
///   mismatch, or an Employee / $0 application, marks a
///   still-pending application Failed with NO issuance. Otherwise
///   the application becomes Paid (PaidAtUtc stamped if unset),
///   issuance runs through the ONE mint path
///   (<see cref="IIssuanceService"/> — which does NOT save; this
///   handler saves), and only after that save does the outbound
///   payment-complete webhook fire (item 35).
/// - Every other event type is logged and ignored. A delivery whose
///   capture names no application is logged as unmatched — PayPal
///   gets its 200 so it stops retrying, and nothing changes.
/// - An application that is already live (Paid, or NotRequired for
///   a free/Employee permit) is never dragged backwards by a stray
///   event: Paid is confirmed and topped up with issuance if it is
///   somehow missing; NotRequired is left exactly as it stands.
/// </summary>
public sealed class PayPalWebhookHandler
{
    /// <summary>Provider name stamped on every event-log row.</summary>
    public const string ProviderName = "PayPal";

    /// <summary>The only event type that acts.</summary>
    public const string CaptureCompletedEventType = "PAYMENT.CAPTURE.COMPLETED";

    // Outcome words (the event log's vocabulary).
    public const string OutcomeActed = "Acted";
    public const string OutcomeDuplicate = "Duplicate";
    public const string OutcomeIgnored = "Ignored";
    public const string OutcomeFailed = "Failed";
    public const string OutcomeUnmatched = "Unmatched";
    public const string OutcomeRejected = "Rejected";
    public const string OutcomeMalformed = "Malformed";

    private readonly SolviaDbContext _db;
    private readonly IPayPalService _payPal;
    private readonly IIssuanceService _issuance;
    private readonly PaymentWebhookService _paymentWebhook;
    private readonly ILogger<PayPalWebhookHandler> _logger;
    private readonly PrepaidPurchaseService? _prepaid;

    public PayPalWebhookHandler(
        SolviaDbContext db,
        IPayPalService payPal,
        IIssuanceService issuance,
        PaymentWebhookService paymentWebhook,
        ILogger<PayPalWebhookHandler> logger,
        PrepaidPurchaseService? prepaid = null)
    {
        _db = db;
        _payPal = payPal;
        _issuance = issuance;
        _paymentWebhook = paymentWebhook;
        _logger = logger;
        _prepaid = prepaid;
    }

    /// <summary>
    /// Handles one raw delivery. <paramref name="rawBody"/> is the
    /// request body exactly as received (signature verification needs
    /// the untouched text), <paramref name="headers"/> carries
    /// PayPal's <c>paypal-transmission-*</c> / <c>paypal-cert-url</c>
    /// / <c>paypal-auth-algo</c> headers, and
    /// <paramref name="publicBaseUrl"/> is the app's public origin,
    /// used only to build the outbound payload's verify URL.
    /// </summary>
    public async Task<PayPalWebhookOutcome> HandleAsync(
        string rawBody,
        IHeaderDictionary headers,
        string? publicBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        // Empty body / failed verify → 401 (item 33). Nothing is
        // saved on any rejection path.
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("PayPal webhook rejected: empty body.");
            return new PayPalWebhookOutcome(
                StatusCodes.Status401Unauthorized, OutcomeRejected);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            _logger.LogWarning("PayPal webhook rejected: body is not JSON.");
            return new PayPalWebhookOutcome(
                StatusCodes.Status400BadRequest, OutcomeMalformed);
        }

        using (document)
        {
            var root = document.RootElement;
            var eventId = GetString(root, "id");
            var eventType = GetString(root, "event_type") ?? "";

            if (string.IsNullOrWhiteSpace(eventId))
            {
                _logger.LogWarning(
                    "PayPal webhook rejected: no event id to key idempotency on.");
                return new PayPalWebhookOutcome(
                    StatusCodes.Status400BadRequest, OutcomeMalformed);
            }

            var transmissionId = Header(headers, "paypal-transmission-id");
            var transmissionTime = Header(headers, "paypal-transmission-time");
            var transmissionSig = Header(headers, "paypal-transmission-sig");
            var certUrl = Header(headers, "paypal-cert-url");
            var authAlgo = Header(headers, "paypal-auth-algo");
            if (transmissionId is null || transmissionTime is null ||
                transmissionSig is null || certUrl is null || authAlgo is null)
            {
                _logger.LogWarning(
                    "PayPal webhook {EventId} rejected: signature headers missing.",
                    eventId);
                return new PayPalWebhookOutcome(
                    StatusCodes.Status401Unauthorized, OutcomeRejected);
            }

            bool verified;
            try
            {
                verified = await _payPal.VerifyWebhookSignatureAsync(
                    transmissionId, transmissionTime, transmissionSig,
                    certUrl, authAlgo, rawBody, cancellationToken);
            }
            catch (PayPalNotConfiguredException)
            {
                // No credentials on this machine → nothing can be
                // verified → the delivery cannot be trusted.
                _logger.LogWarning(
                    "PayPal webhook {EventId} rejected: PayPal credentials " +
                    "are not configured, so the signature cannot be verified.",
                    eventId);
                return new PayPalWebhookOutcome(
                    StatusCodes.Status401Unauthorized, OutcomeRejected);
            }

            if (!verified)
            {
                _logger.LogWarning(
                    "PayPal webhook {EventId} rejected: signature verification failed.",
                    eventId);
                return new PayPalWebhookOutcome(
                    StatusCodes.Status401Unauthorized, OutcomeRejected);
            }

            // Idempotency (item 33): an event already on file has
            // already had its one say. Log the redelivery and stop.
            var alreadyLogged = await _db.WebhookEventLogs.AnyAsync(
                w => w.Provider == ProviderName && w.EventId == eventId,
                cancellationToken);
            if (alreadyLogged)
            {
                _logger.LogInformation(
                    "PayPal webhook {EventId} ({EventType}) is a duplicate " +
                    "delivery — logged no-op.", eventId, eventType);
                return new PayPalWebhookOutcome(
                    StatusCodes.Status200OK, OutcomeDuplicate);
            }

            var log = new WebhookEventLog
            {
                Id = Guid.NewGuid(),
                Provider = ProviderName,
                EventId = eventId,
                EventType = eventType,
                ReceivedAtUtc = DateTime.UtcNow,
            };
            try
            {
                // A shared purchase is checked and staged from a fresh
                // aggregate under the same SQLite write lock as its event log.
                // Signature verification has already completed outside the lock.
                await using var transaction = _prepaid is null ? null
                    : await _db.Database.BeginTransactionAsync(cancellationToken);
                if (_prepaid is not null) _db.ChangeTracker.Clear();
                _db.WebhookEventLogs.Add(log);
                var result = await ProcessAsync(
                    root, eventType, cancellationToken);
                if (result.Outcome == OutcomeUnmatched)
                {
                    _db.ChangeTracker.Clear();
                    return new PayPalWebhookOutcome(StatusCodes.Status503ServiceUnavailable, OutcomeUnmatched);
                }
                log.Outcome = result.Outcome;
                log.ProcessedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation(
                    "PayPal webhook {EventId} ({EventType}) handled: {Outcome}.",
                    eventId, eventType, result.Outcome);

                // Item 35: the outbound receipt event announces
                // SAVED truth only — it fires here, after the save.
                var notifications = new List<PermitApplication>();
                if (result.NotifyApplication is { } single) notifications.Add(single);
                if (result.NotifyApplications is { } many) notifications.AddRange(many);
                foreach (var application in notifications.DistinctBy(application => application.Id))
                {
                    await _paymentWebhook.NotifyPermitIssuedAsync(
                        application, publicBaseUrl, cancellationToken);
                }

                return new PayPalWebhookOutcome(
                    StatusCodes.Status200OK, result.Outcome);
            }
            catch (CheckoutPricingException)
            {
                // A changed/terminal aggregate cannot be partially issued or
                // logged as settled. The transaction has rolled back; retry
                // remains possible after the saved-record conflict is reviewed.
                _db.ChangeTracker.Clear();
                _logger.LogWarning("PayPal webhook {EventId} could not settle the saved purchase aggregate; retry required.", eventId);
                return new PayPalWebhookOutcome(StatusCodes.Status503ServiceUnavailable, OutcomeFailed);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "PayPal webhook {EventId} raced with another payment writer; retry required.", eventId);
                return new PayPalWebhookOutcome(StatusCodes.Status503ServiceUnavailable, OutcomeFailed);
            }
            catch (DbUpdateException ex)
            {
                // The unique (Provider, EventId) index fired: a
                // sibling delivery won the race. That IS the
                // duplicate case — unwind and answer as one.
                _db.ChangeTracker.Clear();
                if (!await _db.WebhookEventLogs.AnyAsync(w => w.Provider == ProviderName && w.EventId == eventId, cancellationToken))
                {
                    _logger.LogError(ex, "PayPal webhook {EventId} could not be saved; retry required.", eventId);
                    return new PayPalWebhookOutcome(StatusCodes.Status503ServiceUnavailable, OutcomeFailed);
                }
                _logger.LogInformation(
                    "PayPal webhook {EventId} collided with its own log row " +
                    "— treated as a duplicate delivery.", eventId);
                return new PayPalWebhookOutcome(
                    StatusCodes.Status200OK, OutcomeDuplicate);
            }
        }
    }

    /// <summary>What processing decided: the outcome word, plus the application whose live transition the outbound webhook must announce after the save (null = announce nothing).</summary>
    private sealed record ProcessResult(string Outcome, PermitApplication? NotifyApplication,
        IReadOnlyList<PermitApplication>? NotifyApplications = null);

    /// <summary>
    /// Acts on a verified, first-seen delivery. Mutates tracked
    /// entities only — the caller saves the application change and
    /// the event-log row in one unit of work, then fires any owed
    /// outbound notification.
    /// </summary>
    private async Task<ProcessResult> ProcessAsync(
        JsonElement root,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(eventType, CaptureCompletedEventType, StringComparison.Ordinal))
        {
            return new ProcessResult(OutcomeIgnored, null);
        }

        if (!root.TryGetProperty("resource", out var resource) ||
            resource.ValueKind != JsonValueKind.Object)
        {
            return new ProcessResult(OutcomeIgnored, null);
        }

        var captureId = GetString(resource, "id");
        if (string.IsNullOrWhiteSpace(captureId))
        {
            return new ProcessResult(OutcomeIgnored, null);
        }

        var prepaidResult = await ProcessPrepaidAsync(resource, captureId, cancellationToken);
        if (prepaidResult is not null) return prepaidResult;

        // Find the application BY CAPTURE ID (item 33); fall back
        // to the order id PayPal embeds in the event, which covers
        // the capture that completed at PayPal before Solvia's own
        // capture call recorded the capture id.
        var application = await _db.PermitApplications
            .SingleOrDefaultAsync(a => a.PayPalCaptureId == captureId, cancellationToken);
        if (application is null)
        {
            var orderId = GetRelatedOrderId(resource);
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                application = await _db.PermitApplications
                    .SingleOrDefaultAsync(a => a.PayPalOrderId == orderId, cancellationToken);
            }
        }

        if (application is null)
        {
            _logger.LogWarning(
                "PayPal webhook capture {CaptureId} names no application — ignored.",
                captureId);
            return new ProcessResult(OutcomeUnmatched, null);
        }

        if (application.PaymentStatus == PaymentStatus.Paid)
        {
            // Paid is standing truth (set by the capture path). The
            // webhook's one remaining job here is the reference app's
            // gap, closed: issue if issuance somehow never landed —
            // and announce it, since that mint IS a live transition.
            var minted = await _issuance.IssueIfAbsentAsync(application, cancellationToken);
            return new ProcessResult(OutcomeActed, minted ? application : null);
        }

        if (application.PaymentStatus == PaymentStatus.NotRequired)
        {
            // A live free / Employee permit. A capture event can
            // never apply to it — leave it exactly as it stands.
            return new ProcessResult(OutcomeIgnored, null);
        }

        // The money rule (item 33): the event's amount must equal
        // the snapshotted price, and the application must be a
        // payable one. Anything else is a Failed mark — and NO
        // issuance, ever, on this path.
        var eventCents = GetAmountCents(resource);
        if (application.Category == UserCategory.Employee ||
            application.PriceCents <= 0 ||
            GetString(resource, "status") != "COMPLETED" ||
            !resource.TryGetProperty("amount", out var eventAmount) ||
            !string.Equals(GetString(eventAmount, "currency_code"), "USD", StringComparison.OrdinalIgnoreCase) ||
            (application.PayPalCaptureId != null && application.PayPalCaptureId != captureId) ||
            eventCents is null ||
            eventCents.Value != application.PriceCents)
        {
            application.PaymentStatus = PaymentStatus.Failed;
            application.ConcurrencyToken = Guid.NewGuid();
            _logger.LogWarning(
                "PayPal webhook capture {CaptureId} does not match application " +
                "{ApplicationId} (event amount {EventCents}, snapshot {PriceCents}, " +
                "category {Category}) — marked Failed, NOT issued.",
                captureId, application.Id, eventCents, application.PriceCents,
                application.Category);
            return new ProcessResult(OutcomeFailed, null);
        }

        application.PaymentStatus = PaymentStatus.Paid;
        application.PaidAtUtc ??= DateTime.UtcNow;
        application.PayPalCaptureId ??= captureId;
        await _issuance.IssueIfAbsentAsync(application, cancellationToken);
        application.ConcurrencyToken = Guid.NewGuid();
        return new ProcessResult(OutcomeActed, application);
    }

    private async Task<ProcessResult?> ProcessPrepaidAsync(JsonElement resource, string captureId,
        CancellationToken cancellationToken)
    {
        var orderId = GetRelatedOrderId(resource);
        var byCapture = await _db.PrepaidPurchases.SingleOrDefaultAsync(
            purchase => purchase.PayPalCaptureId == captureId, cancellationToken);
        var byOrder = string.IsNullOrWhiteSpace(orderId) ? null : await _db.PrepaidPurchases.SingleOrDefaultAsync(
            purchase => purchase.PayPalOrderId == orderId, cancellationToken);
        if (byCapture is null && byOrder is null) return null;
        if (_prepaid is null) return new ProcessResult(OutcomeUnmatched, null);
        if (byCapture is not null && byOrder is not null && byCapture.Id != byOrder.Id)
            return new ProcessResult(OutcomeFailed, null);
        var matched = byCapture ?? byOrder!;
        // A known capture cannot authorize a different order, even when the
        // purchase is already Paid. An order match cannot replace a saved capture.
        if (string.IsNullOrWhiteSpace(matched.PayPalOrderId) ||
            (!string.IsNullOrWhiteSpace(orderId) && !string.Equals(orderId, matched.PayPalOrderId, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(matched.PayPalCaptureId) && !string.Equals(captureId, matched.PayPalCaptureId, StringComparison.Ordinal)))
            return new ProcessResult(OutcomeFailed, null);
        if (matched.PaymentStatus == PaymentStatus.NotRequired)
            return new ProcessResult(OutcomeIgnored, null);
        var amount = GetAmountCents(resource);
        if (captureId.Length > 64 || matched.TotalCents <= 0 || amount != matched.TotalCents ||
            GetString(resource, "status") != "COMPLETED" ||
            !resource.TryGetProperty("amount", out var eventAmount) ||
            !string.Equals(GetString(eventAmount, "currency_code"), "USD", StringComparison.OrdinalIgnoreCase))
            return new ProcessResult(OutcomeFailed, null);
        var purchase = await _prepaid.LoadAsync(matched.Id, cancellationToken);
        if (purchase is null) return new ProcessResult(OutcomeUnmatched, null);
        var unissued = purchase.Items.Where(item => string.IsNullOrWhiteSpace(item.Application.ReceiptNumber))
            .Select(item => item.ApplicationId).ToHashSet();
        var result = await _prepaid.StageVerifiedCaptureAsync(purchase,
            new PayPalCaptureResult("COMPLETED", captureId, amount, "USD"), cancellationToken);
        if (!result.Completed) return new ProcessResult(OutcomeFailed, null);
        return new ProcessResult(OutcomeActed, null, purchase.Items
            .Where(item => unissued.Contains(item.ApplicationId) && !string.IsNullOrWhiteSpace(item.Application.ReceiptNumber))
            .Select(item => item.Application).ToArray());
    }

    private static string? Header(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out var values) &&
        !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString()
            : null;

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetRelatedOrderId(JsonElement resource)
    {
        if (resource.TryGetProperty("supplementary_data", out var supplementary) &&
            supplementary.ValueKind == JsonValueKind.Object &&
            supplementary.TryGetProperty("related_ids", out var related) &&
            related.ValueKind == JsonValueKind.Object)
        {
            return GetString(related, "order_id");
        }

        return null;
    }

    /// <summary>The event amount in exact cents, or null when the event carries no parseable amount.</summary>
    private static int? GetAmountCents(JsonElement resource)
    {
        if (!resource.TryGetProperty("amount", out var amount) ||
            amount.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var value = GetString(amount, "value");
        if (value is null ||
            !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var major) ||
            major < 0 || major > int.MaxValue / 100m || major * 100m != decimal.Truncate(major * 100m))
        {
            return null;
        }

        return (int)(major * 100m);
    }
}
