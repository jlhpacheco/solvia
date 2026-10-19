namespace Solvia.Services;

/// <summary>
/// The term-dates shape. One campus + one term in, four dates out:
///
/// - <see cref="TermStart"/> / <see cref="TermEnd"/> — the academic
///   term window the permit is bought for.
/// - <see cref="PermitEndDate"/> — TermStart + the Term permit type's
///   DurationDays (a Term permit lasts 120 days).
/// - <see cref="RenewalDate"/> — the renew-by date shown to the buyer;
///   the day the permit ends, so renewing on or before it never
///   leaves a gap.
///
/// The record is deliberately flat with <see cref="DateOnly"/> values
/// so it serializes straight to JSON (ISO "yyyy-MM-dd").
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
