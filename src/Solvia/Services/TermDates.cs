namespace Solvia.Services;

/// <summary>
/// The term-dates shape (T6 calendar seam). One campus + one term in,
/// four dates out:
///
/// - <see cref="TermStart"/> / <see cref="TermEnd"/> — the academic term
///   window the permit is bought for.
/// - <see cref="PermitEndDate"/> — TermStart + the permit type's
///   DurationDays (SCHEMA.md: a Term permit lasts 120 days).
/// - <see cref="RenewalDate"/> — the renew-by date shown to the student;
///   the day the permit ends, so renewing on or before it never leaves
///   a gap.
///
/// The record is deliberately flat with <see cref="DateOnly"/> values so
/// it serializes straight to JSON (ISO "yyyy-MM-dd") — this is the exact
/// payload the native calendar list renders today (and the payload a
/// licensed Bryntum Calendar would consume at the drop-in point
/// described in docs/SPONSORS.md), and the same payload the
/// /College/DatesJson endpoint returns.
/// </summary>
public sealed record TermDates(
    string Campus,
    string Term,
    int Year,
    DateOnly TermStart,
    DateOnly TermEnd,
    DateOnly PermitEndDate,
    DateOnly RenewalDate,
    int DurationDays);
