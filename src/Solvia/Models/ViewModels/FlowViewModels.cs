using Solvia.Models.Enums;

namespace Solvia.Models.ViewModels;

// ======================================================================
// R4 view models — the two doors (REDO-PLAN §3). The vehicle view
// models live in VehicleViewModels.cs (R2); My Permits / Receipt
// view models are R5's. Everything here is display + form state:
// no price is ever trusted from these shapes — the server resolves
// money from the fee catalog (ApplicationService / PricingService).
// ======================================================================

// ----------------------------------------------------------------------
// The FORM door (Duvara's Apply)
// ----------------------------------------------------------------------

/// <summary>One vehicle choice on the Apply form: id + "Plate · State · Make Model".</summary>
public sealed class ApplyVehicleOption
{
    public Guid Id { get; set; }

    public string Label { get; set; } = "";
}

/// <summary>One permit-type choice on the Apply form: id + catalog Name.</summary>
public sealed class ApplyTypeOption
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public PermitTypeCode Code { get; set; }
}

/// <summary>
/// The Duvara-shaped Apply form (items 1–8): Campus first (Solvia's
/// multi-college addition), then the person's own Vehicle, Permit
/// Type, Term, and Start Date. Every POST failure re-renders THIS
/// shape with the dropdowns repopulated and the errors on-screen —
/// the applicant never leaves the form to fix a mistake.
/// </summary>
public sealed class ApplyViewModel
{
    public string StudentName { get; set; } = "";

    /// <summary>Plain-word category label, e.g. "Employee" (shown honestly — §2b).</summary>
    public string CategoryLabel { get; set; } = "";

    public IReadOnlyList<string> Campuses { get; set; } = [];

    public IReadOnlyList<ApplyVehicleOption> Vehicles { get; set; } = [];

    public IReadOnlyList<ApplyTypeOption> Types { get; set; } = [];

    /// <summary>Item 6: no vehicles → the form is replaced by the add-a-vehicle card.</summary>
    public bool NoVehicles { get; set; }

    /// <summary>Item 7: no active types → the form is replaced by the no-types card.</summary>
    public bool NoTypes { get; set; }

    // The applicant's picks (posted back on failure, intact).

    public string? Campus { get; set; }

    public Guid VehicleId { get; set; }

    public int PermitTypeId { get; set; }

    public TermKind Term { get; set; } = TermKind.Fall;

    /// <summary>Default today (set by the controller on GET).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Plain-language problems, shown at the top of the form.</summary>
    public List<string> Errors { get; set; } = [];
}

// ----------------------------------------------------------------------
// The AI door — pick a college
// ----------------------------------------------------------------------

/// <summary>One campus card on the AI door's pick page.</summary>
public sealed class CampusPickItem
{
    public string Name { get; set; } = "";

    /// <summary>e.g. "$30.00 per term", "$0 — FREE", or the honest not-published line.</summary>
    public string PriceLabel { get; set; } = "";

    public bool HasPrice { get; set; }
}

/// <summary>The AI door's front page: pick your college (tonight's pick page, moved here).</summary>
public sealed class AskViewModel
{
    public string StudentName { get; set; } = "";

    public IReadOnlyList<CampusPickItem> Campuses { get; set; } = [];
}

// ----------------------------------------------------------------------
// The AI door — 3 questions, what was understood, the trust moment
// ----------------------------------------------------------------------

/// <summary>
/// The 3-questions screen. College is question 1 (already answered
/// by the pick); the form asks the car (one sentence) and carries
/// the type/term tap choices — nothing else is ever asked.
/// </summary>
public sealed class QuestionsViewModel
{
    public string College { get; set; } = "";

    /// <summary>The student's car, in their own words (kept across the Fix-it loop).</summary>
    public string? CarText { get; set; }

    public PermitTypeCode PermitType { get; set; } = PermitTypeCode.Term;

    public TermKind Term { get; set; } = TermKind.Fall;

    /// <summary>Set when the Fix-it path returns here for an existing draft.</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>A plain-language note to show above the form, if any.</summary>
    public string? Notice { get; set; }
}

/// <summary>
/// The Understood screen's draft — and the shape that travels the
/// Fix-it loop. The extraction PRE-FILLS the car fields; the fields
/// themselves are editable inputs, and what is finally submitted
/// here is what gets saved. Plate is the gate field.
/// </summary>
public sealed class PermitDraftViewModel
{
    /// <summary>The saved draft this screen is correcting, once one exists.</summary>
    public Guid? ApplicationId { get; set; }

    public string College { get; set; } = "";

    /// <summary>The student's own words, kept so Fix-it never loses them.</summary>
    public string? CarText { get; set; }

    public string? Plate { get; set; }

    public string? CarMake { get; set; }

    public string? CarModel { get; set; }

    /// <summary>Canonical color name (a VehicleColor name) or null.</summary>
    public string? CarColor { get; set; }

    /// <summary>Two-letter plate state or null (defaults to CA when saved).</summary>
    public string? CarState { get; set; }

    public PermitTypeCode PermitType { get; set; } = PermitTypeCode.Term;

    public TermKind Term { get; set; } = TermKind.Fall;

    /// <summary>Honest origin label: "ollama" or "fallback". ("form" never appears here.)</summary>
    public string Source { get; set; } = "fallback";

    /// <summary>Set by the gate: the plate was blank on submit — show the big prompt.</summary>
    public bool PlateMissing { get; set; }

    /// <summary>Who read the words, in plain language (set by the controller).</summary>
    public string SourceNote { get; set; } = "";
}

/// <summary>
/// The Review screen — the AI door's trust moment. Rendered from the
/// SAVED draft plus a fresh catalog price (never the posted one):
/// college, car, dates, category, and the exact total, before any
/// money moves. A price the catalog does not publish BLOCKS here:
/// no invented total, no pay button — an honest explanation and the
/// one-tap alternative instead.
/// </summary>
public sealed class ReviewViewModel
{
    public Guid ApplicationId { get; set; }

    /// <summary>The draft itself, carried so the "check my answers" path loses nothing.</summary>
    public PermitDraftViewModel Draft { get; set; } = new();

    public string College { get; set; } = "";

    /// <summary>Plain-word category snapshot, e.g. "Student".</summary>
    public string CategoryLabel { get; set; } = "";

    public string TypeName { get; set; } = "";

    public TermKind Term { get; set; }

    /// <summary>e.g. "Blue Honda Civic".</summary>
    public string CarLine { get; set; } = "";

    /// <summary>e.g. "8XYZ123 (CA)".</summary>
    public string PlateLine { get; set; } = "";

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>The server's price, or null when the catalog does not publish it.</summary>
    public int? PriceCents { get; set; }

    public bool Blocked => PriceCents is null;

    /// <summary>True when the block is an unreadable DAY-PASS price (the term alternative applies).</summary>
    public bool IsDayPassBlock { get; set; }

    /// <summary>Display total: "$30.00", "$0 — FREE", or "Price not published".</summary>
    public string TotalDisplay { get; set; } = "";

    /// <summary>The term-permit price offered next to a day-pass block, when it is published.</summary>
    public int? TermAlternativePriceCents { get; set; }

    public string? TermAlternativeDisplay { get; set; }

    /// <summary>A big-banner problem (e.g. the residual blank-plate path). Null when fine.</summary>
    public string? Error { get; set; }
}
