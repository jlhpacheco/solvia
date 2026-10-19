using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Calendar seam (T6): computes the term / permit dates for a campus.
/// The dates page renders this shape through the native calendar list —
/// Bryntum Calendar is NOT embedded (no free edition; a 45-day trial
/// would expire during judging). See docs/SPONSORS.md for the decision
/// and the drop-in point for a licensed Bryntum build.
/// </summary>
public interface ITermDatesService
{
    /// <summary>
    /// Dates for one campus + term, or null when the campus is not in
    /// the fee catalog at all. <paramref name="year"/> is the calendar
    /// year the term starts in (Fall 2026, Spring 2026, ...).
    /// </summary>
    TermDates? GetTermDates(string campusName, TermKind term, int year);
}
