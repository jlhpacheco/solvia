using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>What a checkout start decided the caller should do next.</summary>
public enum CheckoutStartKind
{
    /// <summary>
    /// A PayPal order exists for this application — send the buyer
    /// to approve it. On an idempotent repeat start this is the
    /// STORED order coming back, never a second order.
    /// </summary>
    OrderReady,

    /// <summary>
    /// No payment is required: the application was flipped to
    /// NotRequired and issued in this call (Employee exemption, or a
    /// genuinely $0 published fee on a still-Pending row).
    /// </summary>
    NotRequired,

    /// <summary>The permit was already Paid — nothing to do.</summary>
    AlreadyPaid,
}

/// <summary>
/// The outcome of <see cref="CheckoutService.StartAsync"/>.
/// <see cref="ApproveUrl"/> is null when the order is the stored one
/// from an earlier start — the order id is the durable handle (the
/// JS SDK path needs only the id).
/// </summary>
public sealed record CheckoutStartResult(
    CheckoutStartKind Kind,
    Guid ApplicationId,
    string? OrderId,
    string? ApproveUrl);

/// <summary>The outcome of a capture attempt against an application.</summary>
public sealed record CheckoutCaptureResult(
    bool Completed,
    Guid ApplicationId,
    string Status,
    string? CaptureId,
    string? ReceiptNumber);

/// <summary>
/// The application claims to be payable but its price cannot be
/// trusted: the snapshot is ≤ 0 while the campus publishes a real
/// price (or no price at all). Charging on it — or waving it through
/// as "free" — would both be lies, so checkout refuses loudly and
/// names the campus (REDO-PLAN item 30 iv, item 11's rule).
/// </summary>
public sealed class CheckoutPricingException : InvalidOperationException
{
    public CheckoutPricingException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The checkout brain (REDO-PLAN R6, items 30/31). Both buyer paths —
/// the redirect flow (CheckoutController) and the in-page JS SDK
/// buttons (PayPalController) — run through here, so the money
/// rules live in exactly one place:
///
/// - The amount sent to PayPal is the application's snapshotted
///   PriceCents (the college's published fee), never anything else.
/// - Create is IDEMPOTENT: a Pending application that already holds
///   an order id returns that id — a second order is never created.
/// - An Employee-category application is exempt: create flips it to
///   NotRequired and issues it, with NO PayPal call. An already-Paid
///   application answers "not required" the same way.
/// - A payable application whose snapshot is ≤ 0 is a pricing
///   error, decided by re-resolving against the fee catalog: an
///   unpublished fee (or a snapshot that disagrees with the
///   catalog) throws <see cref="CheckoutPricingException"/>; only a
///   fee the catalog truly publishes as $0 confirms free.
/// - Paid is set ONLY in <see cref="ApplyCaptureAsync"/>, from a
///   capture response whose status is COMPLETED — together with
///   PaidAtUtc, the capture id, and issuance through
///   <see cref="IIssuanceService"/> (which does NOT save; this
///   service saves). Landing on a return page never marks anything.
/// - A PENDING capture remains Pending; other non-COMPLETED captures
///   fail. Amount, currency and capture id must also match before issuance.
/// - Missing PayPal credentials surface as
///   <see cref="PayPalNotConfiguredException"/> from the PayPal
///   client — the honest 503 the controllers render.
/// - A live transition is announced on the outbound payment
///   webhook (item 35) only AFTER it has been saved, through the
///   optional <see cref="PaymentWebhookService"/> hook — the same
///   post-save call the inbound webhook handler makes.
/// </summary>
public sealed class CheckoutService
{
    private readonly SolviaDbContext _db;
    private readonly IPayPalService _payPal;
    private readonly IIssuanceService _issuance;
    private readonly IFeeCatalog _catalog;
    private readonly PricingService _pricing;
    private readonly ILogger<CheckoutService> _logger;
    private readonly PaymentWebhookService? _paymentWebhook;

    public CheckoutService(
        SolviaDbContext db,
        IPayPalService payPal,
        IIssuanceService issuance,
        IFeeCatalog catalog,
        PricingService pricing,
        ILogger<CheckoutService> logger,
        PaymentWebhookService? paymentWebhook = null)
    {
        _db = db;
        _payPal = payPal;
        _issuance = issuance;
        _catalog = catalog;
        _pricing = pricing;
        _logger = logger;
        _paymentWebhook = paymentWebhook;
    }

    /// <summary>
    /// Starts checkout for one application: Employee and genuinely
    /// free permits settle in place; an already-Paid permit reports
    /// itself; a payable permit gets (or re-returns) its PayPal
    /// sandbox order.
    /// </summary>
    public async Task<CheckoutStartResult> StartAsync(
        Guid applicationId,
        string? returnUrl,
        string? cancelUrl,
        CancellationToken cancellationToken = default)
    {
        var application = await LoadAsync(applicationId, cancellationToken);

        if (application.Status == ApplicationStatus.Denied || application.PaymentStatus is
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded or PaymentStatus.Expired)
            throw new CheckoutPricingException("This application cannot start a new payment.");

        if (application.PaymentStatus == PaymentStatus.Paid)
        {
            return new CheckoutStartResult(
                CheckoutStartKind.AlreadyPaid, application.Id,
                application.PayPalOrderId, null);
        }

        // The Employee exemption (item 30 ii): employees never pay,
        // whatever the catalog price — flip + issue, no PayPal call.
        if (application.Category == UserCategory.Employee)
        {
            await SettleAsNotRequiredAsync(application, cancellationToken);
            _logger.LogInformation(
                "Employee application {ApplicationId} settled as NotRequired " +
                "at checkout start — issued, no PayPal order.",
                application.Id);
            return new CheckoutStartResult(
                CheckoutStartKind.NotRequired, application.Id, null, null);
        }

        if (application.PriceCents <= 0)
        {
            // A ≤ 0 snapshot on a Pending row is either a genuinely
            // free permit that creation did not settle, or a price
            // that was never honestly resolved. The catalog decides
            // which — never the snapshot alone (item 30 iv).
            var resolved = ResolveSnapshotPrice(application);
            if (resolved is null)
            {
                throw new CheckoutPricingException(
                    $"{application.Campus} does not publish the price this " +
                    "permit needs, so there is no honest amount to charge. " +
                    "The permit is not paid and PayPal was not called.");
            }

            if (resolved.Value != application.PriceCents)
            {
                throw new CheckoutPricingException(
                    $"The stored price for this {application.Campus} permit " +
                    $"({application.PriceCents} cents) does not match the " +
                    $"college's published fee ({resolved.Value} cents), so " +
                    "checkout refuses to charge either number. The permit " +
                    "is not paid and PayPal was not called.");
            }

            // resolved == snapshot == 0: a real published $0 fee.
            await SettleAsNotRequiredAsync(application, cancellationToken);
            _logger.LogInformation(
                "Free permit {ApplicationId} settled as NotRequired at " +
                "checkout start — published $0 fee, no PayPal call.",
                application.Id);
            return new CheckoutStartResult(
                CheckoutStartKind.NotRequired, application.Id, null, null);
        }

        // Idempotency (item 30 i): the stored order comes back; a
        // second order is never created for the same application.
        if (!string.IsNullOrWhiteSpace(application.PayPalOrderId))
        {
            return new CheckoutStartResult(
                CheckoutStartKind.OrderReady, application.Id,
                application.PayPalOrderId, null);
        }

        // SQLite's write transaction and token check prevent maintenance from
        // changing the amount between order creation and saving its durable id.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        application.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);
        var order = await _payPal.CreateOrderAsync(
            application.PriceCents,
            application.Id.ToString("N"),
            returnUrl,
            cancelUrl,
            cancellationToken);

        application.PayPalOrderId = order.OrderId;
        application.PaymentStatus = PaymentStatus.Pending;
        application.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CheckoutStartResult(
            CheckoutStartKind.OrderReady, application.Id,
            order.OrderId, order.ApproveUrl);
    }

    /// <summary>Captures the application's PayPal order and applies the result.</summary>
    public async Task<CheckoutCaptureResult> CaptureAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var application = await LoadAsync(applicationId, cancellationToken);

        if (application.Status == ApplicationStatus.Denied || application.PaymentStatus is
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded or PaymentStatus.Expired)
            throw new InvalidOperationException("This application cannot accept a payment.");

        if (application.PaymentStatus == PaymentStatus.Paid)
        {
            // Paid is terminal and stamped once — a duplicate capture
            // call just reports the standing truth, with no second
            // call to PayPal.
            return new CheckoutCaptureResult(
                true, application.Id, "COMPLETED",
                application.PayPalCaptureId, application.ReceiptNumber);
        }

        if (string.IsNullOrWhiteSpace(application.PayPalOrderId))
        {
            throw new InvalidOperationException(
                "This application has no PayPal order to capture. Start checkout first.");
        }

        if (application.PriceCents <= 0 || application.Category == UserCategory.Employee ||
            application.PaymentStatus == PaymentStatus.NotRequired)
            throw new InvalidOperationException("This permit does not require a payment.");

        var capture = await _payPal.CaptureOrderAsync(
            application.PayPalOrderId, cancellationToken);
        return await ApplyCaptureAsync(application, capture, cancellationToken);
    }

    /// <summary>
    /// Captures by PayPal order id — the shape the return endpoint
    /// and the JS SDK capture call both have in hand.
    /// </summary>
    public async Task<CheckoutCaptureResult> CaptureByOrderAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var application = await FindByOrderIdAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"No permit application found for PayPal order '{orderId}'.");
        return await CaptureAsync(application.Id, cancellationToken);
    }

    /// <summary>Finds the application that owns a PayPal order id, if any.</summary>
    public async Task<PermitApplication?> FindByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        return await _db.PermitApplications
            .SingleOrDefaultAsync(a => a.PayPalOrderId == orderId, cancellationToken);
    }

    /// <summary>
    /// Flips an application to NotRequired and issues it (number +
    /// token through the ONE mint path), then saves — issuance does
    /// not save, so the flip and the mint commit together.
    /// </summary>
    private async Task SettleAsNotRequiredAsync(
        PermitApplication application,
        CancellationToken cancellationToken)
    {
        application.PaymentStatus = PaymentStatus.NotRequired;
        await _issuance.IssueIfAbsentAsync(application, cancellationToken);
        application.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyIssuedAsync(application, cancellationToken);
    }

    /// <summary>
    /// Announces a just-SAVED live transition on the outbound
    /// payment webhook (item 35) — the same post-save call the
    /// inbound PayPal webhook handler makes after its own save.
    /// The dependency is optional (R7's hook, filed as a change
    /// request to R6): absent, nothing fires. The webhook service
    /// never throws and no-ops when no URL is configured, so a
    /// webhook failure can never change a payment outcome. This
    /// service has no request in hand, so — matching the
    /// handler's optional parameter — no public base URL is
    /// passed and the payload's verify URL is simply absent.
    /// </summary>
    private async Task NotifyIssuedAsync(
        PermitApplication application,
        CancellationToken cancellationToken)
    {
        if (_paymentWebhook is not null)
        {
            await _paymentWebhook.NotifyPermitIssuedAsync(
                application, null, cancellationToken);
        }
    }

    /// <summary>
    /// Re-resolves the application's price from the fee catalog —
    /// the same rule creation used (PricingService over the campus
    /// row, the type code, the stored term, and the SNAPSHOT
    /// category). Null when the catalog publishes no such price.
    /// </summary>
    private int? ResolveSnapshotPrice(PermitApplication application)
    {
        var fee = _catalog.Find(application.Campus);
        var type = _db.PermitTypes.Find(application.PermitTypeId);
        if (fee is null || type is null)
        {
            return null;
        }

        return _pricing.ResolvePriceCents(
            fee, type.Code, application.Term, application.Category);
    }

    /// <summary>
    /// THE one place Paid is decided. Only a capture response with
    /// status COMPLETED plus verified amount/currency marks it Paid.
    /// A pending capture can settle later through the verified webhook.
    /// </summary>
    private async Task<CheckoutCaptureResult> ApplyCaptureAsync(
        PermitApplication application,
        PayPalCaptureResult capture,
        CancellationToken cancellationToken)
    {
        if (string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(capture.CaptureId) && capture.AmountCents == application.PriceCents &&
            string.Equals(capture.Currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            application.PaymentStatus = PaymentStatus.Paid;
            application.PaidAtUtc ??= DateTime.UtcNow;
            application.PayPalCaptureId = capture.CaptureId;
            await _issuance.IssueIfAbsentAsync(application, cancellationToken);
            application.ConcurrencyToken = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await _db.Entry(application).ReloadAsync(cancellationToken);
                if (application.PaymentStatus == PaymentStatus.Paid)
                    return new CheckoutCaptureResult(true, application.Id, "COMPLETED",
                        application.PayPalCaptureId, application.ReceiptNumber);
                throw;
            }

            _logger.LogInformation(
                "Application {ApplicationId} marked Paid from COMPLETED capture {CaptureId}; " +
                "permit number {ReceiptNumber} issued.",
                application.Id, capture.CaptureId, application.ReceiptNumber);
            await NotifyIssuedAsync(application, cancellationToken);
            return new CheckoutCaptureResult(
                true, application.Id, capture.Status,
                capture.CaptureId, application.ReceiptNumber);
        }

        application.PaymentStatus = string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase)
            ? PaymentStatus.Pending : PaymentStatus.Failed;
        application.PayPalCaptureId ??= capture.CaptureId;
        application.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Capture for application {ApplicationId} did NOT complete " +
            "(PayPal status {Status}) — permit is NOT paid and was not issued.",
            application.Id, capture.Status);
        return new CheckoutCaptureResult(
            false, application.Id, capture.Status == "COMPLETED" ? "VERIFICATION_FAILED" : capture.Status, capture.CaptureId, null);
    }

    private async Task<PermitApplication> LoadAsync(
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        return await _db.PermitApplications.FindAsync([applicationId], cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Permit application '{applicationId}' was not found.");
    }
}
