using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

[Route("Prepaid")]
public sealed class PrepaidController(SolviaDbContext db, PrepaidPlanProtection plans,
    PrepaidPurchaseService purchases, IFeeCatalog catalog, PricingService pricing,
    UserCategoryResolver categories, VisitPlannerService planner, PermitPdfService pdf,
    ILogger<PrepaidController> logger) : DemoControllerBase
{
    [HttpGet("Choose")]
    public async Task<IActionResult> Choose(string? plan, CancellationToken cancellationToken, Guid? vehicleId = null)
    {
        var person = await CurrentStudentAsync(db, cancellationToken);
        var schedule = plans.Read(plan, person.Id);
        if (schedule is null) return ExpiredPlan();
        return View(await ChoiceModel(plan!, schedule, vehicleId, schedule.Dates.ToHashSet(), null, cancellationToken));
    }

    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? plan, Guid vehicleId, string[]? selectedDates,
        CancellationToken cancellationToken)
    {
        var person = await CurrentStudentAsync(db, cancellationToken);
        var schedule = plans.Read(plan, person.Id);
        if (schedule is null) return ExpiredPlan();
        var dates = new List<DateOnly>();
        var valid = selectedDates is { Length: >= 1 and <= 182 };
        foreach (var text in selectedDates ?? [])
        {
            if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) || !schedule.Dates.Contains(date)) valid = false;
            else dates.Add(date);
        }
        if (dates.Distinct().Count() != dates.Count) valid = false;
        if (!valid)
        {
            Response.StatusCode = 400;
            return View("Choose", await ChoiceModel(plan!, schedule, vehicleId, dates.ToHashSet(),
                "Choose at least one of the listed dates, with each date selected once.", cancellationToken));
        }
        try
        {
            var purchase = await purchases.CreateAsync(person.Id, vehicleId, schedule.Campus,
                schedule.Term, dates, cancellationToken);
            return RedirectToAction(purchase.PaymentStatus == PaymentStatus.NotRequired ? nameof(Receipt) : nameof(Review),
                new { id = purchase.Id });
        }
        catch (Exception ex) when (ex is CheckoutPricingException or ArgumentException or KeyNotFoundException)
        {
            Response.StatusCode = 400;
            return View("Choose", await ChoiceModel(plan!, schedule, vehicleId, dates.ToHashSet(),
                ex is KeyNotFoundException ? "Choose one of your saved vehicles." : ex.Message, cancellationToken));
        }
    }

    [HttpGet("Review/{id:guid}")]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        var purchase = await Own(id, cancellationToken);
        if (purchase is null) return NotFound();
        if (Settled(purchase)) return RedirectToAction(nameof(Receipt), new { id });
        return View(ReviewModel(purchase, TempData["PrepaidError"] as string));
    }

    [HttpPost("Refresh/{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(Guid id, CancellationToken cancellationToken)
    {
        if (await Own(id, cancellationToken) is null) return NotFound();
        try { await purchases.RefreshPriceAsync(id, cancellationToken); }
        catch (CheckoutPricingException ex) { TempData["PrepaidError"] = ex.Message; }
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost("Revise/{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Revise(Guid id, CancellationToken cancellationToken)
    {
        var purchase = await Own(id, cancellationToken);
        if (purchase is null) return NotFound();
        var plan = plans.Protect(purchase.StudentId, purchase.Campus, purchase.Term,
            purchase.Items.Select(i => i.Application.StartDate).Order().ToList());
        try
        {
            await purchases.CancelDraftAsync(id, cancellationToken);
            return RedirectToAction(nameof(Choose), new { plan, vehicleId = purchase.VehicleId });
        }
        catch (CheckoutPricingException ex) { TempData["PrepaidError"] = ex.Message; }
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost("Start/{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, string? reviewedSnapshot, CancellationToken cancellationToken)
    {
        if (await Own(id, cancellationToken) is null) return NotFound();
        try
        {
            if (string.IsNullOrWhiteSpace(reviewedSnapshot)) throw new CheckoutReviewChangedException();
            var result = await purchases.StartAsync(id, reviewedSnapshot, Callback("Return"), Callback("Cancel"), cancellationToken);
            if (result.Kind != CheckoutStartKind.OrderReady) return RedirectToAction(nameof(Receipt), new { id });
            if (result.ApproveUrl is not null) return Redirect(result.ApproveUrl);
        }
        catch (Exception ex) when (PaymentError(ex, cancellationToken)) { TempData["PrepaidError"] = PublicError(ex); }
        return RedirectToAction(nameof(Review), new { id });
    }

    [HttpGet("Return")]
    public async Task<IActionResult> Return(string? token, CancellationToken cancellationToken)
    {
        var purchase = await OwnOrder(token, cancellationToken);
        if (purchase is null) return BadRequest("This PayPal order could not be matched to your purchase. Check My permits and PayPal activity before retrying.");
        try
        {
            var result = await purchases.CaptureAsync(purchase.Id, cancellationToken);
            if (result.Completed) return RedirectToAction(nameof(Receipt), new { id = purchase.Id });
            TempData["PrepaidError"] = "PayPal has not completed this payment. No paid permits are issued until capture is confirmed.";
        }
        catch (Exception ex) when (PaymentError(ex, cancellationToken)) { TempData["PrepaidError"] = PublicError(ex); }
        return RedirectToAction(nameof(Review), new { id = purchase.Id });
    }

    [HttpGet("Cancel")]
    public async Task<IActionResult> Cancel(string? token, CancellationToken cancellationToken)
    {
        var purchase = await OwnOrder(token, cancellationToken);
        if (purchase is null) return NotFound();
        if (Settled(purchase)) return RedirectToAction(nameof(Receipt), new { id = purchase.Id });
        TempData["PrepaidError"] = "Checkout was closed. Check the saved payment status before retrying; closing PayPal is not proof of a refund or a failed capture.";
        return RedirectToAction(nameof(Review), new { id = purchase.Id });
    }

    public sealed record OrderRequest(Guid PurchaseId, string? ReviewedSnapshot);
    public sealed record CaptureRequest(string? OrderId);

    [HttpPost("/api/prepaid/orders"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder([FromBody] OrderRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null) return BadRequest(new { error = "A valid purchase request is required." });
        var purchase = await purchases.LoadAsync(request.PurchaseId, cancellationToken);
        if (purchase is null) return NotFound();
        if (purchase.StudentId != (await CurrentStudentAsync(db, cancellationToken)).Id) return StatusCode(403);
        try
        {
            if (string.IsNullOrWhiteSpace(request.ReviewedSnapshot)) throw new CheckoutReviewChangedException();
            var result = await purchases.StartAsync(purchase.Id, request.ReviewedSnapshot, Callback("Return"), Callback("Cancel"), cancellationToken);
            return Json(new { id = result.OrderId, orderId = result.OrderId, purchaseId = purchase.Id,
                status = result.Kind == CheckoutStartKind.OrderReady ? "created" : result.Kind == CheckoutStartKind.AlreadyPaid ? "already-paid" : "not-required",
                completed = result.Kind != CheckoutStartKind.OrderReady });
        }
        catch (Exception ex) when (PaymentError(ex, cancellationToken)) { return ApiError(ex); }
    }

    [HttpPost("/api/prepaid/capture"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Capture([FromBody] CaptureRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null) return BadRequest(new { error = "A valid capture request is required." });
        if (string.IsNullOrWhiteSpace(request.OrderId) || request.OrderId.Length > 128) return BadRequest();
        var purchase = await db.PrepaidPurchases.SingleOrDefaultAsync(p => p.PayPalOrderId == request.OrderId, cancellationToken);
        if (purchase is null) return NotFound();
        if (purchase.StudentId != (await CurrentStudentAsync(db, cancellationToken)).Id) return StatusCode(403);
        try
        {
            var result = await purchases.CaptureAsync(purchase.Id, cancellationToken);
            return Json(new { completed = result.Completed, status = result.Status, orderId = request.OrderId,
                captureId = result.CaptureId, receiptNumber = result.ReceiptNumber, purchaseId = purchase.Id });
        }
        catch (Exception ex) when (PaymentError(ex, cancellationToken)) { return ApiError(ex); }
    }

    [HttpGet("Receipt/{id:guid}")]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken cancellationToken)
    {
        var purchase = await Own(id, cancellationToken);
        if (purchase is null) return NotFound();
        if (!Settled(purchase))
        {
            TempData["PrepaidError"] = "This purchase is not paid yet. Review its status before retrying.";
            return RedirectToAction(nameof(Review), new { id });
        }
        return View(purchase);
    }

    [HttpGet("Pdf/{id:guid}")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var purchase = await Own(id, cancellationToken);
        if (purchase is null) return NotFound();
        if (!Settled(purchase) || purchase.Items.Count == 0 || purchase.Items.Any(i =>
            i.Application.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.NotRequired) ||
            string.IsNullOrWhiteSpace(i.Application.ReceiptNumber)))
            return BadRequest("PDF permits are available only after verified payment or a published exemption.");
        return File(pdf.BuildPermitPacket(purchase.Items.OrderBy(i => i.Application.StartDate)
            .Select(i => i.Application).ToList(), $"{Request.Scheme}://{Request.Host}"),
            "application/pdf", $"Solvia-prepaid-{id:N}.pdf");
    }

    private async Task<PrepaidPurchase?> Own(Guid id, CancellationToken ct)
    {
        var person = await CurrentStudentAsync(db, ct);
        var purchase = await purchases.LoadAsync(id, ct);
        return purchase?.StudentId == person.Id ? purchase : null;
    }
    private async Task<PrepaidPurchase?> OwnOrder(string? order, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(order) || order.Length > 128) return null;
        var id = await db.PrepaidPurchases.Where(p => p.PayPalOrderId == order).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        return id is null ? null : await Own(id.Value, ct);
    }
    private async Task<PrepaidChooseViewModel> ChoiceModel(string token, PrepaidPlan plan, Guid? vehicle,
        IReadOnlySet<DateOnly> dates, string? error, CancellationToken ct)
    {
        var person = await CurrentStudentAsync(db, ct);
        var cars = await db.Vehicles.Where(v => v.StudentId == person.Id).OrderBy(v => v.Plate).ToListAsync(ct);
        var fee = catalog.Find(plan.Campus);
        var unit = fee is null ? null : pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, plan.Term, categories.Resolve(person));
        return new(token, plan, cars.Select(v => new PrepaidVehicleOption(v.Id, $"{v.Plate} · {v.State} · {v.Make} {v.Model}")).ToList(),
            vehicle, dates, unit, error);
    }
    private PrepaidReviewViewModel ReviewModel(PrepaidPurchase p, string? error)
    {
        var fee = catalog.Find(p.Campus);
        var current = fee is null ? null : pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, p.Term, p.Category);
        var dates = p.Items.Select(i => i.Application.StartDate).Order().ToList();
        var comparison = planner.Compare(new PlanRequest(p.Campus, dates[0], 1,
            dates.Select(d => d.DayOfWeek).Distinct().ToList(), p.Term, dates[^1]), p.Category);
        // A subset may skip individual dates. Compare its exact count and total,
        // not all occurrences of the same weekday inside the range.
        comparison = comparison with { VisitDates = dates, DailyTotal = p.TotalCents };
        return new(p, PrepaidPurchaseService.ReviewSnapshot(p), current, comparison, error);
    }
    private IActionResult ExpiredPlan()
    {
        TempData["ErrorMessage"] = "This date selection expired or belongs to another demo person. Your saved planner is still available; compare it again.";
        return Redirect("/Planner?showComparison=true");
    }
    private string Callback(string action) => $"{Request.Scheme}://{Request.Host}/Prepaid/{action}";
    private static bool Settled(PrepaidPurchase p) => p.PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired;
    private static bool PaymentError(Exception ex, CancellationToken ct) => ex is CheckoutPricingException or
        CheckoutReviewChangedException or PayPalNotConfiguredException or PayPalApiException or HttpRequestException or
        DbUpdateConcurrencyException or KeyNotFoundException || ex is OperationCanceledException && !ct.IsCancellationRequested;
    private static string PublicError(Exception ex) => ex switch
    {
        CheckoutReviewChangedException => CheckoutReviewChangedException.ReviewMessage,
        CheckoutPricingException => ex.Message,
        PayPalApiException { PayPalIssue: "ORDER_NOT_APPROVED" } => "Approve this order with a PayPal Sandbox Personal buyer account before completing payment. The Business account is the seller.",
        _ => "Payment could not be confirmed. Check this purchase and PayPal activity before retrying; the saved order will be reused."
    };
    private IActionResult ApiError(Exception ex)
    {
        logger.LogWarning("Prepaid payment request failed: {ErrorType}", ex.GetType().Name);
        var status = ex is CheckoutReviewChangedException or CheckoutPricingException or DbUpdateConcurrencyException ? 409 :
            ex is PayPalNotConfiguredException or HttpRequestException or OperationCanceledException ? 503 : 502;
        var code = ex is CheckoutReviewChangedException ? "REVIEW_CHANGED" : ex is PayPalApiException api ? api.PayPalIssue : "PAYMENT_UNCONFIRMED";
        return StatusCode(status, new { error = PublicError(ex), code });
    }
}
