using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Computes the term / permit dates for a campus — the authority for
/// Term permit windows (REDO-PLAN §2a/§2c). A Term application's end
/// date always comes from here; it is computed, never typed. Daily
/// permits never consult this service: their end is their start.
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
