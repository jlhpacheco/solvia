using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Models.ViewModels;

/// <summary>
/// The public permit-check answer (REDO-PLAN items 46/48). ONE
/// shape serves both doors — the anonymous /verify page renders it,
/// and /api/verify serializes the very same object — so the JSON
/// twin can never drift from the page: same token, same
/// <see cref="PermitValidity"/> verdict, same field set, nothing
/// extra.
///
/// The fields are permit facts only: NO student name, NO email, and
/// NEVER the plate itself — the plate STATE is the most an anonymous
/// check may see. An unknown or missing token produces the neutral
/// not-found shape (<see cref="Found"/> = false, every permit field
/// null), identical either way, with no hint about other permits.
/// </summary>
public sealed class VerifyViewModel
{
    /// <summary>True when the token named a real application.</summary>
    public bool Found { get; init; }

    /// <summary>The PermitValidity verdict for today.</summary>
    public bool IsValid { get; init; }

    /// <summary>The plain-words reason, shown on both the valid and the not-valid answer.</summary>
    public string Reason { get; init; } = "";

    /// <summary>The permit (= receipt) number. Null on a found permit means it was never issued — the page says "(not issued)".</summary>
    public string? PermitNumber { get; init; }

    public string? Campus { get; init; }

    /// <summary>Permit type display name (e.g. "Term parking permit").</summary>
    public string? PermitType { get; init; }

    /// <summary>License-plate STATE only — the plate itself never appears on a public check.</summary>
    public string? PlateState { get; init; }

    /// <summary>The application's snapshotted category word (Student / Employee / Public / …).</summary>
    public string? Category { get; init; }

    /// <summary>"Staff Parking Eligible" for Employee permits, "Student/Public Parking Eligible" otherwise — the same line the permit PDF carries.</summary>
    public string? Privilege { get; init; }

    /// <summary>Payment status in words, with "Not Required" spelled out.</summary>
    public string? PaymentStatus { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// The freshest stamp the row carries: when the permit was
    /// issued, else when it was paid, else when it was submitted.
    /// </summary>
    public DateTime? LastUpdatedUtc { get; init; }
}

/// <summary>Builds the one verify answer from an application row (or from no row at all).</summary>
public static class VerifyResultBuilder
{
    /// <summary>The neutral answer for unknown or missing tokens — identical either way.</summary>
    public static VerifyViewModel NotFound() => new()
    {
        Found = false,
        IsValid = false,
        Reason = "We could not find a permit with that code. " +
                 "Check the code and try again.",
    };

    /// <summary>
    /// The answer for a token that named a real application. The
    /// caller loads the PermitType and Vehicle navigations; the
    /// verdict is <see cref="PermitValidity.Check"/> for
    /// <paramref name="today"/>, never re-derived here.
    /// </summary>
    public static VerifyViewModel FromApplication(PermitApplication application, DateOnly today)
    {
        var check = PermitValidity.Check(application, today);
        return new VerifyViewModel
        {
            Found = true,
            IsValid = check.IsActive,
            Reason = check.Reason,
            PermitNumber = application.ReceiptNumber,
            Campus = application.Campus,
            PermitType = application.PermitType?.Name,
            PlateState = application.Vehicle?.State.ToString(),
            Category = application.Category.ToString(),
            Privilege = application.Category == UserCategory.Employee
                ? "Staff Parking Eligible"
                : "Student/Public Parking Eligible",
            PaymentStatus = PaymentWord(application.PaymentStatus),
            StartDate = application.StartDate,
            EndDate = application.EndDate,
            LastUpdatedUtc = application.ReceiptIssuedAtUtc
                             ?? application.PaidAtUtc
                             ?? application.SubmittedAtUtc,
        };
    }

    private static string PaymentWord(Enums.PaymentStatus status) =>
        status == Enums.PaymentStatus.NotRequired ? "Not Required" : status.ToString();
}
