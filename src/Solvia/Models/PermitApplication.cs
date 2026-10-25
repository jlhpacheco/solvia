using System.ComponentModel.DataAnnotations.Schema;
using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>
/// The permit itself. There is no separate "permit" table: a
/// PermitApplication whose payment lands Paid IS the permit, and the
/// <see cref="ReceiptNumber"/> issued at the live transition doubles
/// as the permit number shown on campus.
///
/// Redo deltas over the first build (REDO-PLAN items 14, 34, 51): a
/// <see cref="Category"/> snapshot copied at creation (like the price,
/// it is never re-resolved later), <see cref="ReceiptIssuedAtUtc"/>
/// stamped when the number is minted, and an optimistic-concurrency
/// token every writer rotates before saving.
/// </summary>
public sealed class PermitApplication
{
    public Guid Id { get; set; }

    /// <summary>Owner of the application; set on the server, never trusted from the form.</summary>
    public string StudentId { get; set; } = "";

    public Student? Student { get; set; }

    /// <summary>
    /// The campus this permit is for — the canonical name from the fee
    /// catalog, snapshotted at creation next to the price, so the
    /// receipt, My Permits, and the roster can show the college.
    /// </summary>
    public string Campus { get; set; } = "";

    /// <summary>
    /// The buyer's category, resolved by UserCategoryResolver and
    /// snapshotted at creation (REDO-PLAN §2b). Pricing, the roster,
    /// and the PDF read this snapshot — never a re-resolution.
    /// </summary>
    public UserCategory Category { get; set; } = UserCategory.Public;

    /// <summary>The vehicle this permit covers.</summary>
    public Guid VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    /// <summary>Which catalog type was bought (Daily / Term).</summary>
    public int PermitTypeId { get; set; }

    public PermitType? PermitType { get; set; }

    /// <summary>
    /// The term the buyer picked (Fall / Spring / Summer) — stored on
    /// the row so the price can be recomputed from the fee catalog
    /// without trusting anything the browser posts.
    /// </summary>
    public TermKind Term { get; set; } = TermKind.Fall;

    /// <summary>
    /// Which door/reader produced the application ("form", "ollama",
    /// or "fallback") — the honest source label, kept with the row so
    /// it survives redirects.
    /// </summary>
    public string Source { get; set; } = "form";

    /// <summary>First day the permit is valid.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last day the permit is valid (always computed, never typed).</summary>
    public DateOnly EndDate { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    /// <summary>
    /// Exact price snapshot, in cents, resolved from the college's
    /// real fee row at creation time. This — never the PermitType
    /// row's placeholder — is the amount sent to PayPal.
    /// </summary>
    public int PriceCents { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    /// <summary>PayPal order created at checkout (sandbox).</summary>
    public string? PayPalOrderId { get; set; }

    /// <summary>PayPal capture returned when the money lands.</summary>
    public string? PayPalCaptureId { get; set; }

    /// <summary>Optional membership in one prepaid multi-date purchase.</summary>
    public PrepaidPurchaseItem? PrepaidItem { get; set; }

    /// <summary>Read-only shared reference; never copied into a daily child's persisted payment fields.</summary>
    [NotMapped]
    public string? PaymentOrderReference => PrepaidItem?.Purchase?.PayPalOrderId ?? PayPalOrderId;

    [NotMapped]
    public string? PaymentCaptureReference => PrepaidItem?.Purchase?.PayPalCaptureId ?? PayPalCaptureId;

    /// <summary>The whole purchase total; PriceCents remains this permit's own fee.</summary>
    [NotMapped]
    public int? PurchaseTotalCents => PrepaidItem?.Purchase?.TotalCents;

    /// <summary>Set once, only when the payment is truly Paid.</summary>
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Issued at the live transition; this IS the permit number (unique).</summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>
    /// When the receipt/permit number was minted (REDO-PLAN item 34).
    /// Null until issuance; stamped by the one issuance path only.
    /// </summary>
    public DateTime? ReceiptIssuedAtUtc { get; set; }

    /// <summary>Unguessable token for public permit verification.</summary>
    public string? VerifyToken { get; set; }

    /// <summary>When the person applied.</summary>
    public DateTime SubmittedAtUtc { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (REDO-PLAN item 51). Every service
    /// that saves an application sets a fresh value first; a writer
    /// holding a stale token gets a DbUpdateConcurrencyException
    /// instead of silently overwriting a sibling's save. (A Guid
    /// stands in for a rowversion column — SQLite has none.)
    /// </summary>
    public Guid ConcurrencyToken { get; set; }
}
