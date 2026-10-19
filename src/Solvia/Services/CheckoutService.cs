using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>What a checkout start decided the caller should do next.</summary>
public enum CheckoutStartKind
{
    /// <summary>Send the buyer to <c>ApproveUrl</c> on sandbox.paypal.com.</summary>
    RedirectToPayPal,

    /// <summary>The permit is free ($0): confirmed with NO PayPal call.</summary>
    FreePermitConfirmed,

    /// <summary>The permit was already Paid — nothing to do.</summary>
    AlreadyPaid,
}

/// <summary>The outcome of <see cref="CheckoutService.StartAsync"/>.</summary>
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
/// The checkout brain (BUILD-PLAN T4). Both entry points — the redirect
/// flow (CheckoutController) and the JS SDK buttons (PayPalController) —
/// run through here, so the money rules live in exactly one place:
///
/// - The amount sent to PayPal is the application's snapshotted
///   PriceCents (the college's real fees.csv fee), never anything else.
/// - A $0 (FREE) permit is confirmed as PaymentStatus NotRequired with
///   NO PayPal call at all.
/// - Paid is set ONLY in <see cref="ApplyCaptureAsync"/>, from a capture
///   response whose status is COMPLETED — together with PaidAtUtc, the
///   capture id, the receipt/permit number, and the verify token.
///   Landing on a return/success page never marks anything paid.
/// - Any non-COMPLETED capture leaves the application Failed with the
///   PayPal status logged — never Paid.
/// - After a permit goes live (Paid or free), the payment-complete
///   webhook (T7) is notified — after the save, and it can never block
///   or roll back the payment (see PaymentWebhookService).
/// </summary>
public sealed class CheckoutService
{
    private readonly SolviaDbContext _db;
    private readonly IPayPalService _payPal;
    private readonly PermitNumberService _numbers;
    private readonly ILogger<CheckoutService> _logger;
    private readonly PaymentWebhookService? _webhook;

    public CheckoutService(
        SolviaDbContext db,
        IPayPalService payPal,
        PermitNumberService numbers,
        ILogger<CheckoutService> logger,
        PaymentWebhookService? webhook = null)
    {
        _db = db;
        _payPal = payPal;
        _numbers = numbers;
        _logger = logger;
        _webhook = webhook;
    }

    /// <summary>
    /// Starts checkout for one application: free permits confirm in
    /// place; paid permits get a PayPal sandbox order and an approval
    /// URL for the buyer.
    /// </summary>
    public async Task<CheckoutStartResult> StartAsync(
        Guid applicationId,
        string? returnUrl,
        string? cancelUrl,
        CancellationToken cancellationToken = default)
    {
        var application = await LoadAsync(applicationId, cancellationToken);

        if (application.PaymentStatus == PaymentStatus.Paid)
        {
            return new CheckoutStartResult(
                CheckoutStartKind.AlreadyPaid, application.Id,
                application.PayPalOrderId, null);
        }

        if (application.PriceCents <= 0)
        {
            // FREE permit (e.g. YCCD's $0 semester fee): the permit goes
            // live right here. PayPal is never called for $0.
            application.PaymentStatus = PaymentStatus.NotRequired;
            await IssuePermitArtifactsAsync(application, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyWebhookAsync(application, cancellationToken);
            _logger.LogInformation(
                "Free permit confirmed for application {ApplicationId} — " +
                "PaymentStatus NotRequired, no PayPal call.",
                application.Id);
            return new CheckoutStartResult(
                CheckoutStartKind.FreePermitConfirmed, application.Id, null, null);
        }

        var order = await _payPal.CreateOrderAsync(
            application.PriceCents,
            application.Id.ToString("N"),
            returnUrl,
            cancelUrl,
            cancellationToken);

        application.PayPalOrderId = order.OrderId;
        application.PaymentStatus = PaymentStatus.Pending;
        await _db.SaveChangesAsync(cancellationToken);

        return new CheckoutStartResult(
            CheckoutStartKind.RedirectToPayPal, application.Id,
            order.OrderId, order.ApproveUrl);
    }

    /// <summary>Captures the application's PayPal order and applies the result.</summary>
    public async Task<CheckoutCaptureResult> CaptureAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var application = await LoadAsync(applicationId, cancellationToken);

        if (application.PaymentStatus == PaymentStatus.Paid)
        {
            // Paid is terminal and stamped once — a duplicate capture
            // call just reports the standing truth.
            return new CheckoutCaptureResult(
                true, application.Id, "COMPLETED",
                application.PayPalCaptureId, application.ReceiptNumber);
        }

        if (string.IsNullOrWhiteSpace(application.PayPalOrderId))
        {
            throw new InvalidOperationException(
                "This application has no PayPal order to capture. Start checkout first.");
        }

        var capture = await _payPal.CaptureOrderAsync(
            application.PayPalOrderId, cancellationToken);
        return await ApplyCaptureAsync(application, capture, cancellationToken);
    }

    /// <summary>
    /// Captures by PayPal order id — the shape the return endpoint and
    /// the JS SDK capture call both have in hand.
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
    /// THE one place Paid is decided. Only a capture response with
    /// status COMPLETED marks the application Paid; anything else marks
    /// it Failed (with the status logged) and the permit stays dead.
    /// </summary>
    private async Task<CheckoutCaptureResult> ApplyCaptureAsync(
        PermitApplication application,
        PayPalCaptureResult capture,
        CancellationToken cancellationToken)
    {
        if (string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            application.PaymentStatus = PaymentStatus.Paid;
            application.PaidAtUtc = DateTime.UtcNow;
            application.PayPalCaptureId = capture.CaptureId;
            await IssuePermitArtifactsAsync(application, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyWebhookAsync(application, cancellationToken);

            _logger.LogInformation(
                "Application {ApplicationId} marked Paid from COMPLETED capture {CaptureId}; " +
                "permit number {ReceiptNumber} issued.",
                application.Id, capture.CaptureId, application.ReceiptNumber);
            return new CheckoutCaptureResult(
                true, application.Id, capture.Status,
                capture.CaptureId, application.ReceiptNumber);
        }

        application.PaymentStatus = PaymentStatus.Failed;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Capture for application {ApplicationId} did NOT complete " +
            "(PayPal status {Status}) — permit is NOT paid.",
            application.Id, capture.Status);
        return new CheckoutCaptureResult(
            false, application.Id, capture.Status, capture.CaptureId, null);
    }

    /// <summary>
    /// Announces a now-live permit to the payment-complete webhook
    /// (T7, the Zapier seam) — strictly AFTER the payment truth has
    /// been saved. The webhook service never throws, so this call can
    /// never block or roll back the payment it announces.
    /// </summary>
    private async Task NotifyWebhookAsync(
        PermitApplication application,
        CancellationToken cancellationToken)
    {
        if (_webhook is not null)
        {
            await _webhook.NotifyPermitIssuedAsync(application, cancellationToken);
        }
    }

    /// <summary>
    /// Stamps the receipt/permit number and verify token the first time
    /// a permit goes live (paid or free) — never re-issued afterwards.
    /// </summary>
    private async Task IssuePermitArtifactsAsync(
        PermitApplication application,
        CancellationToken cancellationToken)
    {
        application.ReceiptNumber ??=
            await _numbers.IssueReceiptNumberAsync(cancellationToken);
        application.VerifyToken ??= _numbers.NewVerifyToken();
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
