using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.Enums;
using Solvia.Models.ViewModels;

namespace Solvia.Services;

/// <summary>
/// Read-only review of local Sandbox demo records. No payment/provider/model
/// dependency and no change tracking. Access is gated by OperationsController.
/// </summary>
public sealed class PaymentReviewService(SolviaDbContext db)
{
    public async Task<PaymentReviewViewModel> ReadAsync(
        string? campus = null, PaymentStatus? status = null, UserCategory? category = null,
        bool attentionOnly = false, DateOnly? validityDate = null,
        CancellationToken cancellationToken = default)
    {
        // Never fetch Student/Vehicle: names, emails, plates and verification
        // tokens are unnecessary for this cross-person demo review surface.
        var applications = await db.PermitApplications.AsNoTracking()
            .OrderByDescending(application => application.SubmittedAtUtc)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var today = validityDate ?? DateOnly.FromDateTime(DateTime.Now);
        var campuses = applications.Select(application => application.Campus)
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

        // Unknown enum inputs must never broaden a requested restricted view.
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw new ArgumentOutOfRangeException(nameof(status));
        if (category.HasValue && !Enum.IsDefined(category.Value)) throw new ArgumentOutOfRangeException(nameof(category));

        var filtered = applications.Where(application =>
            (string.IsNullOrEmpty(campus) || string.Equals(application.Campus, campus, StringComparison.Ordinal)) &&
            (!status.HasValue || application.PaymentStatus == status.Value) &&
            (!category.HasValue || application.Category == category.Value));
        var rows = filtered.Select(application => ToRow(application, today))
            .Where(row => !attentionOnly || row.Issues.Count > 0).ToArray();
        return new PaymentReviewViewModel
        {
            Rows = rows, Campuses = campuses, Campus = campus, Status = status, Category = category,
            AttentionOnly = attentionOnly, ReadAtUtc = now, ValidityDate = today
        };
    }

    private static PaymentReviewRow ToRow(PermitApplication application, DateOnly today)
    {
        var validity = PermitValidity.Check(application, today);
        var issues = new List<string>();
        var hasConfirmedAmount = !(application.PaymentStatus == PaymentStatus.Pending &&
            application.PriceCents == 0 && string.IsNullOrWhiteSpace(application.ReceiptNumber));
        if (!hasConfirmedAmount)
            issues.Add("Price not confirmed");
        else if (application.PriceCents > 0 && application.PaymentStatus == PaymentStatus.Pending &&
            application.Status != ApplicationStatus.Denied)
            issues.Add("Awaiting payment");
        if (application.PaymentStatus == PaymentStatus.Paid && string.IsNullOrWhiteSpace(application.PayPalCaptureId))
            issues.Add("Paid · capture missing");
        if (application.PaymentStatus == PaymentStatus.Paid && !validity.IsActive)
            issues.Add("Paid · permit inactive");
        if (application.PaymentStatus == PaymentStatus.Failed)
            issues.Add("Payment failed");
        if (application.PaymentStatus == PaymentStatus.PartiallyRefunded)
            issues.Add("Partial refund · review provider");

        return new PaymentReviewRow(application.Id,
            string.IsNullOrWhiteSpace(application.ReceiptNumber) ? "Application " + application.Id.ToString("N")[..8] : application.ReceiptNumber,
            DemoAlias(application.StudentId), application.Campus, application.Category.ToString(),
            application.PaymentStatus.ToString(), application.PriceCents,
            application.PayPalOrderId, application.PayPalCaptureId, application.PaidAtUtc,
            application.StartDate, application.EndDate, validity.IsActive, validity.Reason, issues)
        {
            HasConfirmedAmount = hasConfirmedAmount
        };
    }

    // Fixed aliases only: a modified DisplayName or an unknown person must
    // not expose a real name through this intentionally unauthenticated demo.
    private static string DemoAlias(string id) => id switch
    {
        "maria" => "Maria · demo", "diego" => "Diego · demo", "priya" => "Priya · demo",
        "sam" => "Sam · demo", "elena" => "Elena · demo", "pat" => "Pat · demo",
        _ => "Demo person"
    };
}
