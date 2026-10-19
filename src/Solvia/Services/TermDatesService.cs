using Solvia.Data;
using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Default <see cref="ITermDatesService"/>.
///
/// Term windows below are the DEMO windows for this seam. The fee
/// catalog's contract (IFeeCatalog / CampusFee) does not carry
/// per-campus term dates — the CSV's free-text "Terms/dates listed"
/// column was research notes, not structured data — so the windows are
/// defined here in one place, ready to be replaced by real per-campus
/// dates when T5/integration lands. (Fall ends Dec 11, matching the
/// SJECCD Fall 2026 calendar.)
///
/// PermitEndDate = TermStart + DurationDays of the Term permit type
/// (read from the seeded PermitType catalog; 120 days).
/// </summary>
public sealed class TermDatesService : ITermDatesService
{
    /// <summary>Matches the seeded Term PermitType when the DB is unavailable.</summary>
    private const int FallbackTermDurationDays = 120;

    private readonly IFeeCatalog _catalog;
    private readonly SolviaDbContext _db;

    public TermDatesService(IFeeCatalog catalog, SolviaDbContext db)
    {
        _catalog = catalog;
        _db = db;
    }

    public TermDates? GetTermDates(string campusName, TermKind term, int year)
    {
        var fee = _catalog.Find(campusName);
        if (fee is null)
        {
            return null;
        }

        var (start, end) = TermWindow(term, year);
        var durationDays = TermPermitDurationDays();
        var permitEnd = start.AddDays(durationDays);

        return new TermDates(
            Campus: fee.Campus,
            Term: term.ToString(),
            Year: year,
            TermStart: start,
            TermEnd: end,
            PermitEndDate: permitEnd,
            RenewalDate: permitEnd,
            DurationDays: durationDays);
    }

    private static (DateOnly Start, DateOnly End) TermWindow(TermKind term, int year) =>
        term switch
        {
            TermKind.Fall => (new DateOnly(year, 8, 24), new DateOnly(year, 12, 11)),
            TermKind.Spring => (new DateOnly(year, 1, 20), new DateOnly(year, 5, 15)),
            TermKind.Summer => (new DateOnly(year, 6, 8), new DateOnly(year, 7, 31)),
            _ => throw new ArgumentOutOfRangeException(nameof(term), term, "Unknown term."),
        };

    private int TermPermitDurationDays()
    {
        try
        {
            var days = _db.PermitTypes
                .Where(p => p.Code == PermitTypeCode.Term && p.Active)
                .Select(p => (int?)p.DurationDays)
                .FirstOrDefault();
            return days ?? FallbackTermDurationDays;
        }
        catch (Exception)
        {
            // The seam page must still render before the demo database
            // is created/migrated; the seeded Term duration is 120.
            return FallbackTermDurationDays;
        }
    }
}
