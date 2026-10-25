using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// My Permits, the Receipt page, and the Permit PDF (REDO-PLAN §3
/// R5, checklist items 36–45) — a second controller on the
/// /Permits routes so R4's PermitsController keeps one owner.
/// Attribute routes pin these three actions under /Permits exactly
/// where the doors and the layout already link to them.
///
/// The owner is the server-resolved demo person on every action —
/// never a value from the URL or a form. The gates differ by
/// surface, on purpose (item 43):
/// - My Permits lists ONLY the current person's rows.
/// - The Receipt page is friendly: an owned application always
///   renders (live card, or "not paid yet" with the way to pay);
///   someone else's (or an unknown id) is a 404.
/// - The PDF is the hard gate: unknown id → 404; not the owner's
///   → 403; owned but not yet live → 400 in plain words; live →
///   the PDF, regenerated on every call (item 45).
/// </summary>
[Route("Permits")]
public sealed class MyPermitsController : DemoControllerBase
{
    private static readonly TimeZoneInfo PacificZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    private readonly SolviaDbContext _db;
    private readonly PermitPdfService _pdf;
    private readonly TimeProvider _clock;

    // PermitPdfService is stateless; the optional parameter keeps
    // this controller constructible before R0 wires the DI
    // registration (filed as a change request with this track) —
    // DI supplies the registered instance when one exists.
    public MyPermitsController(
        SolviaDbContext db, PermitPdfService? pdfService = null, TimeProvider? clock = null)
    {
        _db = db;
        _pdf = pdfService ?? new PermitPdfService();
        _clock = clock ?? TimeProvider.System;
    }

    // ==================================================================
    // My Permits (items 36–42)
    // ==================================================================

    [HttpGet("My")]
    public async Task<IActionResult> My(CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        var applications = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .Include(a => a.PrepaidItem).ThenInclude(item => item!.Purchase).ThenInclude(purchase => purchase!.Items)
            .Where(a => a.StudentId == student.Id)
            .OrderByDescending(a => a.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        var today = TodayPacific();
        var rows = applications.Select(application => ToRow(application, today)).ToList();
        var shownPurchases = new HashSet<Guid>();
        foreach (var row in rows.Where(row => row.PurchaseId.HasValue))
        {
            row.ShowPurchaseActions = shownPurchases.Add(row.PurchaseId!.Value);
            row.ShowPayNow &= row.ShowPurchaseActions;
        }
        var model = new MyPermitsViewModel
        {
            StudentName = student.DisplayName,
            TotalCount = applications.Count,
            PaidCount = applications.Count(
                a => a.PaymentStatus == PaymentStatus.Paid),
            PendingPaymentCount = applications.Where(CanPay)
                .Select(a => a.PrepaidItem is { } item ? "purchase:" + item.PurchaseId : "permit:" + a.Id)
                .Distinct(StringComparer.Ordinal).Count(),
            Rows = rows,
        };

        return View("/Views/Permits/My.cshtml", model);
    }

    private static MyPermitRow ToRow(PermitApplication a, DateOnly today)
    {
        var isLive = a.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired;
        var validity = PermitValidity.Check(a, today);
        var payment = PaymentWord(a.PaymentStatus);
        if (a.PriceCents > 0)
        {
            payment += a.PrepaidItem is null ? $" ({Money(a.PriceCents)})" : $" ({Money(a.PriceCents)} for this date)";
        }

        return new MyPermitRow
        {
            Id = a.Id,
            SubmittedDisplay = Pacific(a.SubmittedAtUtc) + " PT",
            Campus = a.Campus,
            TypeName = a.PermitType?.Name ?? "Parking permit",
            VehicleDisplay = a.Vehicle is null
                ? "—"
                : $"{a.Vehicle.Plate} ({a.Vehicle.State})",
            DatesDisplay = $"{Date(a.StartDate)} → {Date(a.EndDate)}",
            CategoryLabel = a.Category.ToString(),
            PaymentDisplay = payment,
            IsLive = isLive,
            IsActive = validity.IsActive,
            ValidityReason = validity.Reason,
            StatusDisplay = StatusDisplay(a, today, validity.IsActive),
            PurchaseId = a.PrepaidItem?.PurchaseId,
            PurchaseTotalDisplay = a.PurchaseTotalCents is { } total ? Money(total) : null,
            PurchaseDayCount = a.PrepaidItem?.Purchase?.Items.Count,
            ShowPayNow = CanPay(a),
            ReceiptNumber = a.ReceiptNumber,
        };
    }

    // ==================================================================
    // Receipt page (friendly — see the class summary)
    // ==================================================================

    [HttpGet("Receipt/{id:guid}")]
    public async Task<IActionResult> Receipt(
        Guid id, CancellationToken cancellationToken)
    {
        var application = await LoadOwnApplicationAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        var vehicle = application.Vehicle;
        var carLine = string.Join(" ", new[]
        {
            vehicle?.Color.ToString(), vehicle?.Make, vehicle?.Model,
        }.Where(p => !string.IsNullOrWhiteSpace(p)));

        var isLive = application.PaymentStatus is
            PaymentStatus.Paid or PaymentStatus.NotRequired;
        var isFree = application.PaymentStatus == PaymentStatus.NotRequired;
        var today = TodayPacific();
        var validity = PermitValidity.Check(application, today);

        var model = new ReceiptViewModel
        {
            Id = application.Id,
            IsLive = isLive,
            IsActive = validity.IsActive,
            ValidityReason = validity.Reason,
            StatusDisplay = StatusDisplay(application, today, validity.IsActive),
            PurchaseId = application.PrepaidItem?.PurchaseId,
            PurchaseTotalDisplay = application.PurchaseTotalCents is { } total ? Money(total) : null,
            PurchaseDayCount = application.PrepaidItem?.Purchase?.Items.Count,
            CanPay = CanPay(application),
            IsFree = isFree,
            ReceiptNumber = application.ReceiptNumber,
            College = application.Campus,
            CategoryLabel = application.Category.ToString(),
            CarLine = carLine.Length > 0 ? carLine : "Your car",
            PlateLine = vehicle is null ? "" : $"{vehicle.Plate} ({vehicle.State})",
            TypeName = application.PermitType?.Name ?? "Parking permit",
            StartDate = application.StartDate,
            EndDate = application.EndDate,
            TotalDisplay = isFree
                ? application.Category == UserCategory.Employee
                    ? "EXEMPT (Employee)"
                    : "$0 — FREE"
                : Money(application.PriceCents),
            PayPalOrderId = application.PaymentOrderReference,
            PayPalCaptureId = application.PaymentCaptureReference,
            VerifyUrl = application.VerifyToken is null
                ? null
                : $"{Request.Scheme}://{Request.Host}/verify?t=" +
                  Uri.EscapeDataString(application.VerifyToken),
        };

        return View("/Views/Permits/Receipt.cshtml", model);
    }

    // ==================================================================
    // Permit PDF (the hard gate — item 43)
    // ==================================================================

    [HttpGet("PermitPdf/{id:guid}")]
    public async Task<IActionResult> PermitPdf(
        Guid id, CancellationToken cancellationToken)
    {
        // Existence first (404), then ownership (403), then the
        // live gate (400) — the checklist's order, and the only
        // order that never leaks whether someone else's permit
        // exists beyond the denial itself.
        var application = await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .Include(a => a.Student)
            .Include(a => a.PrepaidItem).ThenInclude(item => item!.Purchase).ThenInclude(purchase => purchase!.Items)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        var student = await CurrentStudentAsync(_db, cancellationToken);
        if (application.StudentId != student.Id)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (application.PaymentStatus is not
            (PaymentStatus.Paid or PaymentStatus.NotRequired))
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentType = "text/plain; charset=utf-8",
                Content =
                    "This permit is not issued yet, so there is no permit " +
                    "PDF to download. The PDF is available only after " +
                    "payment is completed (or a free permit is issued). " +
                    "Finish paying first, then download your permit.",
            };
        }

        var origin = $"{Request.Scheme}://{Request.Host}";
        var bytes = _pdf.BuildPermitPdf(application, origin);
        var fileName = $"{application.ReceiptNumber ?? application.Id.ToString()}.pdf";
        return File(bytes, "application/pdf", fileName);
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    /// <summary>One of the current person's applications (vehicle + type joined), or null.</summary>
    private async Task<PermitApplication?> LoadOwnApplicationAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var student = await CurrentStudentAsync(_db, cancellationToken);
        return await _db.PermitApplications
            .Include(a => a.Vehicle)
            .Include(a => a.PermitType)
            .Include(a => a.PrepaidItem).ThenInclude(item => item!.Purchase).ThenInclude(purchase => purchase!.Items)
            .SingleOrDefaultAsync(
                a => a.Id == id && a.StudentId == student.Id, cancellationToken);
    }

    /// <summary>"Not Required" is spelled out; the other words ride as-is (item 38).</summary>
    private static string PaymentWord(PaymentStatus status) => status switch
    {
        PaymentStatus.NotRequired => "Not Required",
        PaymentStatus.PartiallyRefunded => "Partially Refunded",
        _ => status.ToString(),
    };

    private static bool CanPay(PermitApplication app) => app.Status != ApplicationStatus.Denied &&
        app.Category != UserCategory.Employee && app.PriceCents > 0 &&
        app.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Failed &&
        (app.PrepaidItem?.Purchase is not { } purchase ||
            purchase.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Failed);

    private DateOnly TodayPacific() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), PacificZone).DateTime);

    private static string StatusDisplay(PermitApplication app, DateOnly today, bool active)
    {
        if (active) return "Valid today";
        var issued = app.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired;
        if (issued && app.Status != ApplicationStatus.Denied)
        {
            var payment = app.PaymentStatus == PaymentStatus.Paid ? "Paid" : "Issued";
            if (today < app.StartDate) return "Upcoming · " + payment;
            if (today > app.EndDate) return "Expired · " + payment;
        }
        return issued ? "Issued · inactive" : CanPay(app) ? "Payment due" : PaymentWord(app.PaymentStatus);
    }

    private static string Pacific(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), PacificZone)
            .ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) =>
        date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    private static string Money(int cents) =>
        "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
