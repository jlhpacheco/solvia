using System.Globalization;
using Solvia.Models.Enums;

namespace Solvia.Models.ViewModels;

/// <summary>One saved application per row; never a provider balance or settlement report.</summary>
public sealed record PaymentReviewRow(
    Guid Id, string Reference, string DemoPerson, string Campus,
    string Category, string PaymentStatus, long PriceCents,
    string? OrderId, string? CaptureId, DateTime? PaidAtUtc,
    DateOnly StartDate, DateOnly EndDate, bool IsActive, string ValidityReason,
    IReadOnlyList<string> Issues)
{
    // Pending, unissued drafts can still hold the database's zero placeholder.
    // That is different from a confirmed free/exempt or settled $0 amount.
    public bool HasConfirmedAmount { get; init; } = true;
    public string Amount => HasConfirmedAmount ? PaymentReviewViewModel.Money(PriceCents) : "Not priced";
    public string IssueSummary => Issues.Count == 0 ? "No review flags" : string.Join(" · ", Issues);
    public string DetailsId => "record-" + Id.ToString("N");
}

public sealed class PaymentReviewViewModel
{
    public required IReadOnlyList<PaymentReviewRow> Rows { get; init; }
    public required IReadOnlyList<string> Campuses { get; init; }
    public string? Campus { get; init; }
    public PaymentStatus? Status { get; init; }
    public UserCategory? Category { get; init; }
    public bool AttentionOnly { get; init; }
    public required DateTime ReadAtUtc { get; init; }
    public required DateOnly ValidityDate { get; init; }
    public int ApplicationCount => Rows.Count;
    // A saved Paid status plus a nonblank capture reference is the inclusion
    // rule. Still no provider recheck, fee deduction, currency conversion, or
    // claim that references are unique external transactions.
    public int PaidWithCaptureCount => Rows.Count(IsRecordedPaid);
    public long PaidWithCaptureCents => Rows.Where(IsRecordedPaid).Sum(row => row.PriceCents);
    public int NoPaymentRequiredCount => Rows.Count(row => row.PaymentStatus == nameof(Enums.PaymentStatus.NotRequired));
    public int AttentionCount => Rows.Count(row => row.Issues.Count > 0);
    public IEnumerable<PaymentReviewRow> AttentionRows => Rows.Where(row => row.Issues.Count > 0);

    private static bool IsRecordedPaid(PaymentReviewRow row) =>
        row.PaymentStatus == nameof(Enums.PaymentStatus.Paid) && !string.IsNullOrWhiteSpace(row.CaptureId);

    public static string Money(long cents) => "$" + (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("en-US"));
}
