namespace Solvia.Services;

/// <summary>
/// One campus row from data/fees.csv — real published parking-permit
/// fees (researched from the campuses' public pages, Oct 2026).
///
/// Price honesty rules:
/// - A fee the campus does not publish is <c>null</c> — "not listed" —
///   never 0 and never an invented number.
/// - A campus that publishes FREE permits has price 0 with
///   <see cref="IsFree"/> = true — a real $0, distinct from null.
/// </summary>
public sealed record CampusFee(
    string Campus,
    string StudentSemesterFeeText,
    string OtherTermFeesText,
    string DayPassText,
    string MotorcycleText,
    int? FallSemesterCents,
    int? SpringSemesterCents,
    int? SummerSemesterCents,
    bool IsFree,
    string SourceNote)
{
    /// <summary>
    /// The campus's published per-day price, in cents, parsed from
    /// <see cref="DayPassText"/> by the catalog loader (REDO-PLAN
    /// §2a). Null when the campus publishes no readable day-pass
    /// price — a Daily permit for such a campus is blocked, never
    /// priced by invention.
    /// </summary>
    public int? DayPassCents { get; init; }

    /// <summary>True when the campus publishes at least one semester price (FREE counts).</summary>
    public bool HasPublishedSemesterPrice =>
        FallSemesterCents.HasValue || SpringSemesterCents.HasValue;
}
