namespace Solvia.Models.ViewModels;

// ======================================================================
// R5 view models — My Permits + the Receipt page (REDO-PLAN §3 R5,
// checklist items 36–44). Display shapes only: every fact here was
// resolved on the server from the saved application row (price
// snapshot, category snapshot, minted number) — nothing is computed
// from anything the browser sent.
// ======================================================================

/// <summary>One row of My Permits — one application, Duvara's column set (item 38).</summary>
public sealed class MyPermitRow
{
    public Guid Id { get; set; }

    /// <summary>Submitted stamp, already rendered in Pacific time.</summary>
    public string SubmittedDisplay { get; set; } = "";

    /// <summary>The campus this permit is for.</summary>
    public string Campus { get; set; } = "";

    /// <summary>Permit type Name, e.g. "Term / semester permit".</summary>
    public string TypeName { get; set; } = "";

    /// <summary>e.g. "8XYZ123 (CA)".</summary>
    public string VehicleDisplay { get; set; } = "";

    /// <summary>e.g. "Aug 24, 2026 → Dec 22, 2026".</summary>
    public string DatesDisplay { get; set; } = "";

    /// <summary>Category snapshot word, e.g. "Student", "Employee".</summary>
    public string CategoryLabel { get; set; } = "";

    /// <summary>
    /// Payment word with "Not Required" spelled out (item 38);
    /// the amount rides in parentheses when it is above $0.
    /// </summary>
    public string PaymentDisplay { get; set; } = "";

    /// <summary>True when the permit is live (Paid or NotRequired).</summary>
    public bool IsLive { get; set; }

    /// <summary>Item 39: "Pay Now" shows only when money is still owed.</summary>
    public bool ShowPayNow { get; set; }

    /// <summary>Item 40: the PDF action shows only on live rows.</summary>
    public bool ShowPdf => IsLive;

    /// <summary>The minted permit number, once issued.</summary>
    public string? ReceiptNumber { get; set; }
}

/// <summary>
/// My Permits (items 36–42): the current person's own applications,
/// newest first, with the three summary tiles. There is deliberately
/// NO search, filter, sort, or pagination on this page (item 42).
/// </summary>
public sealed class MyPermitsViewModel
{
    /// <summary>The demo person's display name, for the heading.</summary>
    public string StudentName { get; set; } = "";

    /// <summary>Tile 1: total permits (all of the person's own rows).</summary>
    public int TotalCount { get; set; }

    /// <summary>Tile 2: how many are Paid.</summary>
    public int PaidCount { get; set; }

    /// <summary>Tile 3: how many still owe money (price > 0 and not Paid).</summary>
    public int PendingPaymentCount { get; set; }

    /// <summary>The rows, newest first by SubmittedAtUtc.</summary>
    public IReadOnlyList<MyPermitRow> Rows { get; set; } = [];
}

/// <summary>
/// The Receipt page for one application. A live permit shows the
/// receipt card (number, facts, PayPal ids, verify + PDF actions);
/// a not-yet-live one shows the friendly "not paid yet" state with
/// the way to finish paying — the page never 400s (that hard gate
/// belongs to the PDF action, item 43).
/// </summary>
public sealed class ReceiptViewModel
{
    public Guid Id { get; set; }

    /// <summary>True when Paid or NotRequired — the permit is live.</summary>
    public bool IsLive { get; set; }

    /// <summary>True for a free permit (no payment was needed).</summary>
    public bool IsFree { get; set; }

    public string? ReceiptNumber { get; set; }

    public string College { get; set; } = "";

    /// <summary>Category snapshot word, e.g. "Student".</summary>
    public string CategoryLabel { get; set; } = "";

    /// <summary>e.g. "Blue Honda Civic".</summary>
    public string CarLine { get; set; } = "";

    /// <summary>e.g. "8XYZ123 (CA)".</summary>
    public string PlateLine { get; set; } = "";

    public string TypeName { get; set; } = "";

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>"$30.00", or the free/exempt wording for a $0 permit.</summary>
    public string TotalDisplay { get; set; } = "";

    public string? PayPalOrderId { get; set; }

    public string? PayPalCaptureId { get; set; }

    /// <summary>The public check-anyone link, once a token is minted.</summary>
    public string? VerifyUrl { get; set; }
}
