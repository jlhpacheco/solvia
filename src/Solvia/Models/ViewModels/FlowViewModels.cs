using Solvia.Models.Enums;

namespace Solvia.Models.ViewModels;

// Flow ViewModels for the Maria student journey (BUILD-PLAN T2).
// Every screen is BIG and SIMPLE: one action per screen, few words.

/// <summary>Home screen: the demo-student switcher + the two big doors.</summary>
public sealed class HomeIndexViewModel
{
    public required IReadOnlyList<Student> Students { get; init; }

    public required string CurrentStudentId { get; init; }

    public required string CurrentStudentName { get; init; }
}

/// <summary>One tappable campus row on the pick-your-college screen.</summary>
public sealed record CampusPickItem(
    string Name,
    string PriceLabel,
    bool HasPublishedPrice);

/// <summary>Pick-your-college screen: the searchable list of 10 campuses.</summary>
public sealed class CollegePickViewModel
{
    public required IReadOnlyList<CampusPickItem> Campuses { get; init; }

    /// <summary>The search text the student submitted, if any (T8).</summary>
    public string? Query { get; init; }

    /// <summary>
    /// Which search provider actually served a query — "builtin" or
    /// "elastic" (T8) — shown on the screen so the demo never claims
    /// Elastic answered when the built-in match did. Null when no
    /// query was run (the plain full list).
    /// </summary>
    public string? SearchServedBy { get; init; }
}

/// <summary>The 3-questions screen (college prefilled, car in her own words, term taps).</summary>
public sealed class QuestionsViewModel
{
    /// <summary>
    /// The persisted draft application's id, when the student arrived
    /// here via "Fix it" — carried so fixing answers UPDATES the same
    /// draft instead of starting a second one.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    public required string College { get; set; }

    public string? CarText { get; set; }

    public string? Notice { get; set; }

    public PermitTypeCode PermitType { get; set; } = PermitTypeCode.Term;

    public TermKind Term { get; set; } = TermKind.Fall;
}

/// <summary>
/// The permit draft as the flow carries it between screens (hidden form
/// fields). It NEVER carries a price: the price is recomputed on the
/// server from the fee catalog at Review and again at Confirm, so
/// nothing the browser sends can change what she pays.
/// </summary>
public sealed class PermitDraftViewModel
{
    /// <summary>
    /// The persisted draft application's id, once the Understand step
    /// has saved it. From Review on, ONLY this id travels between
    /// screens — the server reloads the draft; the browser no longer
    /// relays the car facts.
    /// </summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>
    /// True when the Understand gate stopped the flow because the
    /// plate is still blank — the Understood screen shows its big
    /// "I still need your plate" prompt.
    /// </summary>
    public bool PlateMissing { get; set; }

    public string College { get; set; } = "";

    public string? CarText { get; set; }

    public string? Plate { get; set; }

    public string? CarMake { get; set; }

    public string? CarModel { get; set; }

    /// <summary>Canonical color name from the extractor (e.g. "Blue").</summary>
    public string? CarColor { get; set; }

    /// <summary>Two-letter plate state (e.g. "CA").</summary>
    public string? CarState { get; set; }

    public PermitTypeCode PermitType { get; set; } = PermitTypeCode.Term;

    public TermKind Term { get; set; } = TermKind.Fall;

    /// <summary>"ollama" or "fallback" — which reader answered (honest label).</summary>
    public string Source { get; set; } = "fallback";

    /// <summary>The car in plain words, e.g. "Blue 2019 Honda Civic".</summary>
    public string CarSummary
    {
        get
        {
            var parts = new[] { CarColor, CarMake, CarModel }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var text = string.Join(" ", parts);
            return text.Length > 0 ? text : "I did not catch your car yet";
        }
    }

    /// <summary>The permit in plain words, e.g. "Fall term permit" or "Day pass".</summary>
    public string PermitSummary =>
        PermitType == PermitTypeCode.Daily ? "Day pass" : $"{Term} term permit";
}

/// <summary>
/// The Review screen: the exact total from her college's fee row,
/// shown BEFORE any payment. A null <see cref="PriceCents"/> means the
/// college does not publish that price — the screen blocks with the
/// honest message and offers no way to pay an invented number.
/// </summary>
public sealed class ReviewViewModel
{
    /// <summary>The persisted draft application's id — the only thing
    /// the Confirm form posts back.</summary>
    public required Guid ApplicationId { get; init; }

    public required PermitDraftViewModel Draft { get; init; }

    /// <summary>Exact price in cents from the fee catalog, or null = not published.</summary>
    public int? PriceCents { get; init; }

    public required string TotalDisplay { get; init; }

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    /// <summary>
    /// A big error banner for the screen (e.g. Confirm found the plate
    /// still blank on a residual path) — shown INSTEAD of bouncing the
    /// student backwards to an earlier screen.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// When a DAY PASS is blocked because its price could not be read,
    /// the same college's term-permit price in cents — the one-tap
    /// alternative offered next to "Pick another college". Null when
    /// no term price is published either.
    /// </summary>
    public int? TermAlternativePriceCents { get; init; }

    /// <summary>The term-alternative price, formatted ("$30.00").</summary>
    public string? TermAlternativeDisplay { get; init; }

    public bool Blocked => PriceCents is null;

    public bool IsFree => PriceCents == 0;

    /// <summary>The block is a day-pass price that could not be read.</summary>
    public bool IsDayPassBlock =>
        Blocked && Draft.PermitType == PermitTypeCode.Daily;

    /// <summary>A term permit at a known price can be offered instead.</summary>
    public bool HasTermAlternative => TermAlternativePriceCents is not null;
}

/// <summary>One row on the My Permits screen.</summary>
public sealed record MyPermitItem(
    Guid Id,
    string Campus,
    string TypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    string PriceDisplay,
    PaymentStatus PaymentStatus,
    string? ReceiptNumber)
{
    public bool IsLive =>
        PaymentStatus is PaymentStatus.Paid or PaymentStatus.NotRequired;
}

/// <summary>My Permits screen: everything the signed-in demo student holds.</summary>
public sealed class MyPermitsViewModel
{
    public required string StudentName { get; init; }

    public required IReadOnlyList<MyPermitItem> Permits { get; init; }
}

/// <summary>The big receipt screen — the permit itself, shown after payment.</summary>
public sealed class ReceiptViewModel
{
    public required Guid Id { get; init; }

    public string? ReceiptNumber { get; init; }

    public required string College { get; init; }

    public required string CarLine { get; init; }

    public required string TypeName { get; init; }

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    public required string TotalDisplay { get; init; }

    public bool IsLive { get; init; }

    public bool IsFree { get; init; }

    public string? PayPalOrderId { get; init; }

    public string? PayPalCaptureId { get; init; }

    /// <summary>Public check link for this permit: /verify?t=&lt;token&gt; (T5's page).</summary>
    public string? VerifyUrl { get; init; }
}

/// <summary>The fix-a-car screen (Vehicles/Edit).</summary>
public sealed class VehicleEditViewModel
{
    public Guid Id { get; set; }

    public string Plate { get; set; } = "";

    public string? Make { get; set; }

    public string? Model { get; set; }

    public VehicleColor Color { get; set; } = VehicleColor.Other;

    public UsState State { get; set; } = UsState.CA;
}
