using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// One shared payment, separate dated permits. Every write operation owns a
/// SQLite transaction; the webhook staging method deliberately owns neither a
/// transaction nor SaveChanges so its caller can commit the event log too.
/// </summary>
public sealed class PrepaidPurchaseService(
    SolviaDbContext db,
    ApplicationService applications,
    IPayPalService payPal,
    IIssuanceService issuance,
    PricingService pricing,
    IFeeCatalog catalog,
    TimeProvider? clock = null,
    ILogger<PrepaidPurchaseService>? logger = null,
    PaymentWebhookService? paymentWebhook = null)
{
    private static readonly TimeZoneInfo PacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger<PrepaidPurchaseService> _logger = logger ?? NullLogger<PrepaidPurchaseService>.Instance;

    public async Task<PrepaidPurchase?> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.PrepaidPurchases
            .Include(p => p.Vehicle)
            .Include(p => p.Items).ThenInclude(i => i.Application).ThenInclude(a => a.Vehicle)
            .Include(p => p.Items).ThenInclude(i => i.Application).ThenInclude(a => a.PermitType)
            .Include(p => p.Items).ThenInclude(i => i.Application).ThenInclude(a => a.Student)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<PrepaidPurchase> CreateAsync(string studentId, Guid vehicleId, string campus,
        TermKind term, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var orderedDates = dates.Order().ToArray();
        if (!Enum.IsDefined(term) || orderedDates.Length is < 1 or > 182 ||
            orderedDates.Distinct().Count() != orderedDates.Length ||
            orderedDates.Any(date => date < PacificToday()) ||
            orderedDates[^1].DayNumber - orderedDates[0].DayNumber >= 182)
            throw new CheckoutPricingException("Choose 1 to 182 distinct dates, today or later, within a 182-day period.");
        var fee = catalog.Find(campus) ?? throw new CheckoutPricingException("Choose a college from the catalog.");
        if (pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, term) is not { } published || published < 0)
            throw new CheckoutPricingException("This college does not publish a valid day-pass price. No purchase was created.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.ChangeTracker.Clear();
            var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Id == vehicleId && v.StudentId == studentId, cancellationToken)
                ?? throw new KeyNotFoundException("That vehicle does not belong to the current person.");
            if (!await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken))
                throw new KeyNotFoundException("The current person was not found.");
            var key = DraftKey(studentId, vehicleId, fee.Campus, term, orderedDates);
            var existingId = await db.PrepaidPurchases.Where(p => p.DraftKey == key)
                .Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);
            if (existingId is { } id)
            {
                var existing = (await LoadAsync(id, cancellationToken))!;
                ValidateAggregate(existing);
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var existingPermits = await db.PermitApplications.IgnoreAutoIncludes()
                .Where(a => a.StudentId == studentId && a.VehicleId == vehicleId && a.Campus == fee.Campus)
                .Select(a => new { a.StartDate, a.EndDate, Code = a.PermitType!.Code, a.PaymentStatus, a.Status })
                .ToListAsync(cancellationToken);
            if (orderedDates.Any(date => existingPermits.Any(a =>
                    a.Code == PermitTypeCode.Daily && a.StartDate == date ||
                    a.Code == PermitTypeCode.Term && a.Status != ApplicationStatus.Denied &&
                    (a.PaymentStatus == PaymentStatus.Paid || a.PaymentStatus == PaymentStatus.NotRequired) &&
                    a.StartDate <= date && a.EndDate >= date)))
                throw new CheckoutPricingException("A selected date already has a day-pass application or is covered by a paid/free term permit. No existing application was added to this purchase.");

            var purchase = new PrepaidPurchase
            {
                Id = Guid.NewGuid(), StudentId = studentId, VehicleId = vehicleId, Vehicle = vehicle,
                Campus = fee.Campus, Term = term, DraftKey = key,
                SubmittedAtUtc = _clock.GetUtcNow().UtcDateTime, ConcurrencyToken = Guid.NewGuid()
            };
            foreach (var date in orderedDates)
            {
                var result = await applications.CreateAsync(new ApplicationInput(studentId, vehicleId,
                    fee.Campus, PermitTypeCode.Daily, term, date, "form"), cancellationToken);
                if (!result.IsCreated || result.Application is not { } application)
                    throw new CheckoutPricingException(result.ErrorMessage ?? "A dated permit could not be created. The entire purchase was rolled back.");
                if (purchase.Items.Count == 0)
                {
                    // Category comes from the existing server category authority,
                    // never from a new browser field or a duplicate application.
                    purchase.Category = application.Category;
                    purchase.UnitPriceCents = application.PriceCents;
                }
                if (application.Category != purchase.Category || application.PriceCents != purchase.UnitPriceCents || application.PriceCents < 0)
                    throw new CheckoutPricingException("The catalog changed while preparing these permits. Review the dates and price again.");
                purchase.Items.Add(new PrepaidPurchaseItem { Purchase = purchase, PurchaseId = purchase.Id,
                    Application = application, ApplicationId = application.Id });
            }
            purchase.TotalCents = CheckedTotal(purchase.UnitPriceCents, purchase.Items.Count);
            purchase.PaymentStatus = purchase.TotalCents == 0 ? PaymentStatus.NotRequired : PaymentStatus.Pending;
            db.PrepaidPurchases.Add(purchase);
            await db.SaveChangesAsync(cancellationToken);
            ValidateAggregate(purchase);
            await transaction.CommitAsync(cancellationToken);
            return purchase;
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    /// <summary>Lifecycle fields are excluded so unchanged order/capture retries keep the same reviewed snapshot.</summary>
    public static string ReviewSnapshot(PrepaidPurchase purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        var vehicle = purchase.Vehicle ?? throw new InvalidOperationException("Load the purchase vehicle before review.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1, purchase.Id, purchase.StudentId, purchase.Campus, purchase.Category,
            purchase.VehicleId, purchase.Term, purchase.UnitPriceCents, purchase.TotalCents,
            VehicleOwner = vehicle.StudentId, vehicle.Plate, vehicle.State, vehicle.Make, vehicle.Model, vehicle.Color,
            Items = purchase.Items.OrderBy(i => i.Application.StartDate).ThenBy(i => i.ApplicationId)
                .Select(i => new { i.ApplicationId, i.Application.StudentId, i.Application.VehicleId,
                    i.Application.Campus, i.Application.Category, i.Application.PermitTypeId,
                    TypeCode = i.Application.PermitType?.Code, i.Application.Term,
                    i.Application.StartDate, i.Application.EndDate, i.Application.PriceCents }).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public async Task<CheckoutStartResult> StartAsync(Guid id, string reviewedSnapshot,
        string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var purchase = await FreshAsync(id, cancellationToken);
            ValidateAggregate(purchase);
            if (reviewedSnapshot is null || reviewedSnapshot.Length != 64 ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ReviewSnapshot(purchase)), Encoding.ASCII.GetBytes(reviewedSnapshot)))
                throw new CheckoutReviewChangedException();
            RejectTerminal(purchase);
            if (purchase.PaymentStatus == PaymentStatus.Paid)
                return new CheckoutStartResult(CheckoutStartKind.AlreadyPaid, id, purchase.PayPalOrderId, null);
            if (purchase.PaymentStatus == PaymentStatus.NotRequired)
                return new CheckoutStartResult(CheckoutStartKind.NotRequired, id, null, null);
            var hasPastDates = purchase.Items.Any(i => i.Application.StartDate < PacificToday());
            if (!string.IsNullOrWhiteSpace(purchase.PayPalOrderId))
            {
                // Recovery uses the existing order, never a newly priced one.
                var orderId = purchase.PayPalOrderId;
                await transaction.CommitAsync(cancellationToken);
                var existing = await payPal.GetOrderAsync(orderId, cancellationToken);
                if (existing.OrderId != orderId) throw new CheckoutPricingException("The stored payment order could not be verified.");
                if (existing.Status is "APPROVED" or "COMPLETED")
                {
                    var result = await CaptureAsync(id, cancellationToken);
                    if (result.Completed) return new CheckoutStartResult(CheckoutStartKind.AlreadyPaid, id, orderId, null);
                    throw new CheckoutPricingException("This payment is not confirmed completed. Check its status before retrying.");
                }
                if (hasPastDates)
                    throw new CheckoutPricingException("A selected visit date has passed. This unpaid order cannot begin or resume buyer approval. Review a new plan.");
                if (existing.Status is not ("CREATED" or "PAYER_ACTION_REQUIRED") || string.IsNullOrWhiteSpace(existing.ApproveUrl))
                    throw new CheckoutPricingException("This existing order cannot resume approval. No new order was created.");
                return new CheckoutStartResult(CheckoutStartKind.OrderReady, id, orderId, existing.ApproveUrl);
            }
            if (hasPastDates)
                throw new CheckoutPricingException("A selected visit date has passed. Review a new plan before paying. No order was created.");
            var currentPrice = CurrentUnitPrice(purchase);
            if (currentPrice != purchase.UnitPriceCents)
                throw new CheckoutPricingException("The published daily fee changed. Review the updated purchase total before paying. No order was created.");
            if (purchase.TotalCents == 0)
            {
                await SettleFreeAsync(purchase, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new CheckoutStartResult(CheckoutStartKind.NotRequired, id, null, null);
            }
            purchase.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
            var order = await payPal.CreateOrderAsync(purchase.TotalCents, $"prepaid-{id:N}", returnUrl, cancelUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(order.OrderId) || order.OrderId.Length > 64 ||
                await db.PrepaidPurchases.AnyAsync(p => p.Id != id && p.PayPalOrderId == order.OrderId, cancellationToken) ||
                await db.PermitApplications.IgnoreAutoIncludes().AnyAsync(a => a.PayPalOrderId == order.OrderId, cancellationToken))
                throw new CheckoutPricingException("The provider did not return an unused payment order. This purchase was not marked paid.");
            purchase.PayPalOrderId = order.OrderId;
            purchase.PaymentStatus = PaymentStatus.Pending;
            foreach (var item in purchase.Items)
            {
                item.Application.PaymentStatus = PaymentStatus.Pending;
                item.Application.ConcurrencyToken = Guid.NewGuid();
            }
            purchase.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CheckoutStartResult(CheckoutStartKind.OrderReady, id, order.OrderId, order.ApproveUrl);
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    public async Task<CheckoutCaptureResult> CaptureAsync(Guid id, CancellationToken cancellationToken = default)
    {
        CheckoutCaptureResult result;
        List<PermitApplication> newlyIssued;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var purchase = await FreshAsync(id, cancellationToken);
                ValidateAggregate(purchase);
                RejectTerminal(purchase);
                if (purchase.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired) return SettledResult(purchase);
                if (string.IsNullOrWhiteSpace(purchase.PayPalOrderId) || purchase.TotalCents <= 0)
                    throw new CheckoutPricingException("This purchase has no payable order to capture.");
                purchase.ConcurrencyToken = Guid.NewGuid();
                await db.SaveChangesAsync(cancellationToken);
                var capture = await payPal.CaptureOrderAsync(purchase.PayPalOrderId, cancellationToken);
                result = await StageVerifiedCaptureAsync(purchase, capture, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                newlyIssued = result.Completed ? purchase.Items.Select(i => i.Application).ToList() : [];
            }
            catch { db.ChangeTracker.Clear(); throw; }
        }
        // The payment and all permits are durable before outbound delivery.
        // StageVerifiedCapture never sends, and settled retries return above.
        if (paymentWebhook is not null)
            foreach (var application in newlyIssued)
                await paymentWebhook.NotifyPermitIssuedAsync(application, null, cancellationToken);
        return result;
    }

    /// <summary>Releases only an unbound, wholly unissued draft; never payment or permit history.</summary>
    public async Task CancelDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var purchase = await FreshAsync(id, cancellationToken);
            ValidateAggregate(purchase);
            if (purchase.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.Failed) ||
                !string.IsNullOrWhiteSpace(purchase.PayPalOrderId) || !string.IsNullOrWhiteSpace(purchase.PayPalCaptureId) ||
                purchase.PaidAtUtc is not null || purchase.Items.Any(i =>
                    i.Application.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.Failed) ||
                    i.Application.ReceiptNumber is not null || i.Application.VerifyToken is not null ||
                    i.Application.ReceiptIssuedAtUtc is not null || i.Application.PaidAtUtc is not null))
                throw new CheckoutPricingException("This purchase has a payment or issued permit and cannot be released. Resume its existing payment or open its receipt.");
            db.PrepaidPurchaseItems.RemoveRange(purchase.Items);
            db.PermitApplications.RemoveRange(purchase.Items.Select(i => i.Application));
            db.PrepaidPurchases.Remove(purchase);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    public async Task<PrepaidPurchase> RefreshPriceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var purchase = await FreshAsync(id, cancellationToken);
            ValidateAggregate(purchase);
            RejectTerminal(purchase);
            if (!string.IsNullOrWhiteSpace(purchase.PayPalOrderId) || purchase.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired)
                return purchase;
            purchase.UnitPriceCents = CurrentUnitPrice(purchase);
            purchase.TotalCents = CheckedTotal(purchase.UnitPriceCents, purchase.Items.Count);
            foreach (var item in purchase.Items)
            {
                item.Application.PriceCents = purchase.UnitPriceCents;
                item.Application.ConcurrencyToken = Guid.NewGuid();
            }
            purchase.ConcurrencyToken = Guid.NewGuid();
            if (purchase.TotalCents == 0) await SettleFreeAsync(purchase, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return purchase;
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    /// <summary>
    /// Requires the full tracked aggregate and a provider-verified result. The
    /// webhook caller must hold its write transaction and save once afterward.
    /// No child stores a copy of the shared order/capture identifiers.
    /// </summary>
    public async Task<CheckoutCaptureResult> StageVerifiedCaptureAsync(PrepaidPurchase purchase,
        PayPalCaptureResult capture, CancellationToken cancellationToken = default)
    {
        ValidateAggregate(purchase);
        RejectTerminal(purchase);
        var persistedCapture = await db.PrepaidPurchases.AsNoTracking().Where(p => p.Id == purchase.Id)
            .Select(p => p.PayPalCaptureId).SingleOrDefaultAsync(cancellationToken);
        if ((!string.IsNullOrWhiteSpace(persistedCapture) && persistedCapture != capture.CaptureId) ||
            (!string.IsNullOrWhiteSpace(purchase.PayPalCaptureId) && purchase.PayPalCaptureId != capture.CaptureId))
            return new CheckoutCaptureResult(false, purchase.Id, "CAPTURE_CONFLICT", persistedCapture ?? purchase.PayPalCaptureId, null);
        if (purchase.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired) return SettledResult(purchase);
        if (string.IsNullOrWhiteSpace(purchase.PayPalOrderId) || purchase.TotalCents <= 0)
            throw new CheckoutPricingException("This purchase has no payable order to settle.");
        var completed = string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
        var verified = completed && !string.IsNullOrWhiteSpace(capture.CaptureId) && capture.CaptureId.Length <= 64 &&
            capture.AmountCents == purchase.TotalCents && string.Equals(capture.Currency, "USD", StringComparison.OrdinalIgnoreCase);
        if (verified)
        {
            verified = !await db.PrepaidPurchases.AnyAsync(p => p.Id != purchase.Id && p.PayPalCaptureId == capture.CaptureId, cancellationToken) &&
                !await db.PermitApplications.IgnoreAutoIncludes().AnyAsync(a => a.PayPalCaptureId == capture.CaptureId, cancellationToken);
        }
        if (verified)
        {
            var paidAt = _clock.GetUtcNow().UtcDateTime;
            purchase.PaymentStatus = PaymentStatus.Paid;
            purchase.PaidAtUtc ??= paidAt;
            purchase.PayPalCaptureId = capture.CaptureId;
            foreach (var item in purchase.Items)
            {
                item.Application.PaymentStatus = PaymentStatus.Paid;
                item.Application.PaidAtUtc ??= purchase.PaidAtUtc;
                await issuance.IssueIfAbsentAsync(item.Application, cancellationToken);
                item.Application.ConcurrencyToken = Guid.NewGuid();
            }
            purchase.ConcurrencyToken = Guid.NewGuid();
            _logger.LogInformation("Prepaid purchase {PurchaseId}: verified settlement staged for {Count} dated permits.", purchase.Id, purchase.Items.Count);
            return SettledResult(purchase);
        }
        purchase.PaymentStatus = string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase) ? PaymentStatus.Pending : PaymentStatus.Failed;
        foreach (var item in purchase.Items)
        {
            item.Application.PaymentStatus = purchase.PaymentStatus;
            item.Application.ConcurrencyToken = Guid.NewGuid();
        }
        purchase.ConcurrencyToken = Guid.NewGuid();
        return new CheckoutCaptureResult(false, purchase.Id, completed ? "VERIFICATION_FAILED" : capture.Status, capture.CaptureId, null);
    }

    private async Task<PrepaidPurchase> FreshAsync(Guid id, CancellationToken cancellationToken)
    {
        // These operations accept only an id; no caller's pending EF changes
        // are part of their contract. Fresh roots and relations share the lock.
        db.ChangeTracker.Clear();
        return await LoadAsync(id, cancellationToken) ?? throw new KeyNotFoundException("That prepaid purchase was not found.");
    }

    private int CurrentUnitPrice(PrepaidPurchase purchase)
    {
        var fee = catalog.Find(purchase.Campus);
        var price = fee is null ? null : pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, purchase.Term, purchase.Category);
        return price is >= 0 ? price.Value : throw new CheckoutPricingException("This college no longer publishes a valid daily fee. No new order was created.");
    }

    private DateOnly PacificToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), PacificZone).DateTime);

    private static int CheckedTotal(int unitPrice, int count)
    {
        try { return checked(unitPrice * count); }
        catch (OverflowException) { throw new CheckoutPricingException("The purchase total is too large to charge safely."); }
    }

    private static string DraftKey(string owner, Guid vehicleId, string campus, TermKind term, IEnumerable<DateOnly> dates) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { Version = 1, Owner = owner, VehicleId = vehicleId, Campus = campus, Term = term, Dates = dates.Order().ToArray() }))).ToLowerInvariant();

    private static void ValidateAggregate(PrepaidPurchase purchase)
    {
        if (purchase.Vehicle is null || purchase.Vehicle.StudentId != purchase.StudentId ||
            !Enum.IsDefined(purchase.Category) || !Enum.IsDefined(purchase.Term) ||
            purchase.Items.Count is < 1 or > 182 || purchase.UnitPriceCents < 0 ||
            purchase.TotalCents != CheckedTotal(purchase.UnitPriceCents, purchase.Items.Count) ||
            purchase.Items.Any(i => i.PurchaseId != purchase.Id || i.Application is null ||
                i.ApplicationId != i.Application.Id || i.Application.StudentId != purchase.StudentId ||
                i.Application.VehicleId != purchase.VehicleId || i.Application.Campus != purchase.Campus ||
                i.Application.Category != purchase.Category || i.Application.Term != purchase.Term ||
                i.Application.PermitType?.Code != PermitTypeCode.Daily ||
                i.Application.StartDate != i.Application.EndDate || i.Application.PriceCents != purchase.UnitPriceCents ||
                !string.IsNullOrWhiteSpace(i.Application.PayPalOrderId) || !string.IsNullOrWhiteSpace(i.Application.PayPalCaptureId)))
            throw new CheckoutPricingException("The dated permits no longer match this purchase. No payment action was taken.");
        var dates = purchase.Items.Select(i => i.Application.StartDate).Order().ToArray();
        if (dates.Distinct().Count() != dates.Length || dates[^1].DayNumber - dates[0].DayNumber >= 182 ||
            DraftKey(purchase.StudentId, purchase.VehicleId, purchase.Campus, purchase.Term, dates) != purchase.DraftKey)
            throw new CheckoutPricingException("The purchase dates changed. Review a new plan before paying.");
        var paid = purchase.PaymentStatus == PaymentStatus.Paid;
        var free = purchase.PaymentStatus == PaymentStatus.NotRequired;
        if (paid && (purchase.TotalCents <= 0 || string.IsNullOrWhiteSpace(purchase.PayPalOrderId) ||
                string.IsNullOrWhiteSpace(purchase.PayPalCaptureId) || purchase.PaidAtUtc is null) ||
            free && (purchase.TotalCents != 0 || purchase.PayPalOrderId is not null || purchase.PayPalCaptureId is not null || purchase.PaidAtUtc is not null) ||
            purchase.Items.Any(i => i.Application.PaymentStatus != purchase.PaymentStatus ||
                (paid || free) && (i.Application.ReceiptNumber is null || i.Application.VerifyToken is null) ||
                paid && i.Application.PaidAtUtc != purchase.PaidAtUtc ||
                !(paid || free) && (i.Application.ReceiptNumber is not null || i.Application.VerifyToken is not null || i.Application.PaidAtUtc is not null)))
            throw new CheckoutPricingException("The payment and dated permits have conflicting states. No payment action was taken.");
    }

    private static void RejectTerminal(PrepaidPurchase purchase)
    {
        if (purchase.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded or PaymentStatus.Expired ||
            purchase.Items.Any(i => i.Application.Status == ApplicationStatus.Denied || i.Application.PaymentStatus is
                PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded or PaymentStatus.Expired))
            throw new CheckoutPricingException("This purchase cannot accept a payment.");
    }

    private async Task SettleFreeAsync(PrepaidPurchase purchase, CancellationToken cancellationToken)
    {
        purchase.PaymentStatus = PaymentStatus.NotRequired;
        foreach (var item in purchase.Items)
        {
            item.Application.PaymentStatus = PaymentStatus.NotRequired;
            await issuance.IssueIfAbsentAsync(item.Application, cancellationToken);
            item.Application.ConcurrencyToken = Guid.NewGuid();
        }
        purchase.ConcurrencyToken = Guid.NewGuid();
    }

    private static CheckoutCaptureResult SettledResult(PrepaidPurchase purchase) =>
        new(true, purchase.Id, purchase.PaymentStatus == PaymentStatus.Paid ? "COMPLETED" : "NOT_REQUIRED", purchase.PayPalCaptureId, null);
}
