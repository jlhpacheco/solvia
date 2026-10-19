using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>
/// The permit itself (docs/SCHEMA.md). There is no separate "permit"
/// table: a PermitApplication whose payment lands Paid IS the permit,
/// and the <see cref="ReceiptNumber"/> issued at payment time doubles
/// as the permit number the student shows on campus.
/// </summary>
public sealed class PermitApplication
{
    public Guid Id { get; set; }

    /// <summary>Owner of the application; set on the server, never trusted from the form.</summary>
    public string StudentId { get; set; } = "";

    public Student? Student { get; set; }

    /// <summary>
    /// The campus this permit is for — the canonical name from the fee
    /// catalog, snapshotted at purchase time next to the price, so the
    /// receipt and My Permits can show her college. (Added for T2: the
    /// fee catalog is keyed by campus, and no other field records it.)
    /// </summary>
    public string Campus { get; set; } = "";

    /// <summary>The vehicle this permit covers.</summary>
    public Guid VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    /// <summary>Which term/type was bought.</summary>
    public int PermitTypeId { get; set; }

    public PermitType? PermitType { get; set; }

    /// <summary>First day the permit is valid.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last day the permit is valid.</summary>
    public DateOnly EndDate { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    /// <summary>
    /// Exact price snapshot, in cents, taken from the college's real fee
    /// row (data/fees.csv) at purchase time. This — never a catalog
    /// placeholder — is the amount sent to PayPal.
    /// </summary>
    public int PriceCents { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    /// <summary>PayPal order created at checkout (sandbox).</summary>
    public string? PayPalOrderId { get; set; }

    /// <summary>PayPal capture returned when the money lands.</summary>
    public string? PayPalCaptureId { get; set; }

    /// <summary>Set once, only when the payment is truly Paid.</summary>
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Issued at payment; this IS the permit number (unique).</summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>Unguessable token for public permit verification.</summary>
    public string? VerifyToken { get; set; }

    /// <summary>When the student applied.</summary>
    public DateTime SubmittedAtUtc { get; set; }
}
