using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>Confirmed planning choices. Category and prices are deliberately not browser inputs.</summary>
public sealed record PlanRequest(
    string Campus,
    DateOnly StartDate,
    int Weeks,
    IReadOnlyList<DayOfWeek> Weekdays,
    TermKind Term,
    DateOnly? EndDate = null);

/// <summary>Read-only estimate. All monetary values are nullable integer USD cents.</summary>
public sealed record PlanResult
{
    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string Campus { get; init; } = "";
    public UserCategory Category { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int Weeks { get; init; }
    public IReadOnlyList<DayOfWeek> Weekdays { get; init; } = [];
    public TermKind Term { get; init; }
    public IReadOnlyList<DateOnly> VisitDates { get; init; } = [];
    public int DayCount => VisitDates.Count;
    public DateOnly? FirstVisit => VisitDates.Count == 0 ? null : VisitDates[0];
    public DateOnly? LastVisit => VisitDates.Count == 0 ? null : VisitDates[^1];
    public int? DayCents { get; init; }
    public int? DailyTotal { get; init; }
    public int? TermCents { get; init; }
    public bool TermAvailable { get; init; }
    /// <summary>First positive visit count for which the term is no dearer; null for unavailable/zero daily prices.</summary>
    public int? BreakEvenDays { get; init; }
    public string Recommendation { get; init; } = "Unavailable";
    public int? SavingsCents { get; init; }
    public string Reason { get; init; } = "";
    public string FeeSource { get; init; } = "";
    public string FeeAsOf { get; init; } = "October 2026 catalog snapshot";
    public TermDates? Window { get; init; }
    public string Disclosure => "Planning estimate only: published catalog snapshot and demo permit dates, not live prices or verified campus eligibility. " +
        "This does not buy several day passes. Continuing with a day pass covers only the first planned day.";
}

/// <summary>
/// Compares confirmed visits using the existing price/date authorities. No AI,
/// payment, application creation, or mutation occurs here. The controller must
/// resolve category from the current server-loaded demo person, never a form.
/// </summary>
public sealed class VisitPlannerService
{
    private readonly IFeeCatalog _catalog;
    private readonly PricingService _pricing;
    private readonly ITermDatesService _dates;
    private readonly TimeProvider _clock;

    public VisitPlannerService(IFeeCatalog catalog, PricingService pricing,
        ITermDatesService dates, TimeProvider? clock = null)
    {
        _catalog = catalog;
        _pricing = pricing;
        _dates = dates;
        _clock = clock ?? TimeProvider.System;
    }

    public PlanResult Compare(PlanRequest input, UserCategory category)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new List<string>();
        var weeks = input.Weeks;
        if (input.EndDate is { } end)
        {
            var inclusiveDays = end.DayNumber - input.StartDate.DayNumber + 1;
            if (inclusiveDays < 1) errors.Add("Choose an end date on or after the planning start date.");
            else if (inclusiveDays > 26 * 7) errors.Add("Choose a date range of at most 26 weeks (182 days).");
            else weeks = (inclusiveDays + 6) / 7;
        }
        else if (weeks is < 1 or > 26) errors.Add("Choose between 1 and 26 weeks.");
        if (input.Weekdays is null || input.Weekdays.Count == 0)
            errors.Add("Choose at least one visit weekday.");
        else if (input.Weekdays.Count > 7 || input.Weekdays.Any(day => !Enum.IsDefined(day)) ||
                 input.Weekdays.Distinct().Count() != input.Weekdays.Count)
            errors.Add("Choose each weekday at most once from the seven listed days.");
        if (!Enum.IsDefined(input.Term)) errors.Add("Choose a listed term.");
        if (!Enum.IsDefined(category)) errors.Add("The current demo person's category is not available.");
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (input.StartDate < today) errors.Add("Choose today or a future planning start date.");

        var result = new PlanResult
        {
            Campus = input.Campus?.Trim() ?? "",
            Category = category,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Weeks = weeks,
            Weekdays = input.Weekdays is null ? [] : Array.AsReadOnly(input.Weekdays.ToArray()),
            Term = input.Term,
        };
        if (errors.Count > 0) return Invalid(result, errors);

        var fee = string.IsNullOrWhiteSpace(input.Campus) ? null : _catalog.Find(input.Campus);
        if (fee is null) return Invalid(result, ["Choose a college from Solvia's catalog."]);
        result = result with { Campus = fee.Campus, FeeSource = fee.SourceNote };

        var visits = new List<DateOnly>();
        try
        {
            var length = input.EndDate is { } rangeEnd
                ? rangeEnd.DayNumber - input.StartDate.DayNumber + 1
                : checked(weeks * 7);
            // Evaluate the final date first so malformed extreme dates do not
            // return a partial count or overflow while rendering a result.
            _ = input.StartDate.AddDays(length - 1);
            for (var index = 0; index < length; index++)
            {
                var date = input.StartDate.AddDays(index);
                if (result.Weekdays.Contains(date.DayOfWeek)) visits.Add(date);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return Invalid(result, ["The schedule goes beyond supported calendar dates. Choose an earlier start date."]);
        }
        if (visits.Count == 0)
            return Invalid(result, ["No selected visit weekdays fall inside this date range. Choose another weekday or adjust the dates."]);

        TermDates? window;
        try { window = _dates.GetTermDates(fee.Campus, input.Term, input.StartDate.Year); }
        catch (ArgumentOutOfRangeException) { window = null; }
        if (window is null)
            return Invalid(result, ["The selected college's term dates are unavailable; no term comparison can be made."]);
        if (window.TermEnd < window.TermStart || window.PermitEndDate < window.TermStart)
            return Invalid(result, ["The selected term's date window is invalid; no comparison can be made."]);
        // Match actual issuance: the term permit ends at the authority's
        // anchored PermitEndDate, never first-visit + a fresh 120 days.
        if (visits[0] < window.TermStart || visits[0] > window.TermEnd)
            return Invalid(result with { Window = window }, ["The first visit must be inside the selected term's academic dates."]);
        if (visits[^1] > window.PermitEndDate)
            return Invalid(result with { Window = window }, ["This schedule crosses the selected term permit's end date. Shorten it or choose another start date/term."]);
        result = result with { VisitDates = Array.AsReadOnly(visits.ToArray()), Window = window };

        // Consult the catalog before exemption. Unpublished is still null for
        // Employee, and corrupt negative amounts must not become a free offer.
        var publishedDay = _pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, input.Term);
        var publishedTerm = _pricing.ResolvePriceCents(fee, PermitTypeCode.Term, input.Term);
        if (publishedDay is < 0 || publishedTerm is < 0)
            return Invalid(result, ["The catalog contains an invalid price; no comparison can be made."]);
        var day = _pricing.ResolvePriceCents(fee, PermitTypeCode.Daily, input.Term, category);
        var term = _pricing.ResolvePriceCents(fee, PermitTypeCode.Term, input.Term, category);
        int? total;
        try { total = day is { } cents ? checked(cents * visits.Count) : null; }
        catch (OverflowException)
        {
            return Invalid(result, ["The schedule total is too large to compare safely."]);
        }
        result = result with { DayCents = day, DailyTotal = total, TermCents = term, TermAvailable = term.HasValue };
        if (day is null || term is null)
        {
            var missing = day is null && term is null ? "day-pass and selected term prices" : day is null ? "day-pass price" : "selected term price";
            return result with { Reason = $"The catalog does not publish the {missing}. Missing prices are not zero, so no cheapest-option claim is made." };
        }

        var breakEven = day is > 0 ? checked((int)Math.Max(1, ((long)term.Value + day.Value - 1) / day.Value)) : (int?)null;
        var totalCents = total!.Value;
        var recommendation = totalCents < term.Value ? "Daily passes" : totalCents > term.Value ? "Term permit" : "Tie";
        return result with
        {
            BreakEvenDays = breakEven,
            Recommendation = recommendation,
            SavingsCents = Math.Abs(checked(totalCents - term.Value)),
            Reason = recommendation switch
            {
                "Daily passes" => "For these confirmed dates, the estimated day-pass total is lower than one term permit.",
                "Term permit" => "For these confirmed dates, one term permit costs less than the estimated day-pass total.",
                _ when category == UserCategory.Employee => "Both published options resolve to $0 under the demo Employee exemption; no PayPal payment is needed.",
                _ => "The two catalog-based estimates are equal for these confirmed dates.",
            }
        };
    }

    private static PlanResult Invalid(PlanResult result, IReadOnlyList<string> errors) => result with
    {
        Errors = Array.AsReadOnly(errors.ToArray()),
        VisitDates = [],
        DayCents = null,
        DailyTotal = null,
        TermCents = null,
        TermAvailable = false,
        Reason = errors[0],
    };
}
