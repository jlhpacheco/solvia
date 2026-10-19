using Solvia.Services;

namespace Solvia.Models.ViewModels;

// College-side ViewModels (REDO-PLAN R8): the roster ("who paid /
// who hasn't"), its three admin tiles and "By student" section
// (items 53–54), and the find-your-college search result. Like the
// student flow, every screen is BIG and SIMPLE — plain words, few
// numbers. Roster truth comes from PermitValidity.Classify (payment
// state at a campus) — never re-derived here.

/// <summary>One student's row on a campus roster.</summary>
public sealed record RosterRow(
    string StudentName,
    RosterPermitState State,
    string StateLabel,
    string? PermitNumber,
    DateOnly? StartDate,
    DateOnly? EndDate);

/// <summary>
/// One student's line in the roster's "By student" section
/// (item 54): how many ISSUED permits they hold at this campus and
/// the receipt numbers of those permits. An application that never
/// issued is not a permit and does not count.
/// </summary>
public sealed record StudentPermitSummary(
    string StudentName,
    int PermitCount,
    IReadOnlyList<string> ReceiptNumbers);

/// <summary>The roster page for one campus.</summary>
public sealed class RosterViewModel
{
    public required string Campus { get; init; }

    /// <summary>Every seeded student with their state at this campus.</summary>
    public required IReadOnlyList<RosterRow> Rows { get; init; }

    /// <summary>All catalog campus names, for the campus picker.</summary>
    public required IReadOnlyList<string> Campuses { get; init; }

    // --- Duvara's three admin tiles (item 53), computed per campus.

    /// <summary>Total permits issued at this campus: Paid + NotRequired rows.</summary>
    public required int IssuedCount { get; init; }

    /// <summary>Permit revenue at this campus: the sum of Paid PriceCents snapshots.</summary>
    public required long RevenueCents { get; init; }

    /// <summary>Issued permits at this campus whose EndDate is already past.</summary>
    public required int ExpiredCount { get; init; }

    /// <summary>
    /// "By student" (item 54): one row per seeded student, sorted by
    /// issued-permit count descending.
    /// </summary>
    public required IReadOnlyList<StudentPermitSummary> ByStudent { get; init; }

    /// <summary>
    /// The price-repair banner lines (item 55), carried back to the
    /// roster after POST /College/RepairPrices. Null when no repair
    /// just ran.
    /// </summary>
    public IReadOnlyList<string>? RepairReport { get; init; }
}

/// <summary>One result row on the find-your-college search page.</summary>
public sealed record CollegeSearchRow(
    string Campus,
    string SemesterPriceLabel);

/// <summary>
/// The search page: the ranked campuses plus the provider that
/// ACTUALLY served them, labeled on-screen (the honesty rule).
/// </summary>
public sealed class CollegeSearchViewModel
{
    public required string? Query { get; init; }

    /// <summary>"builtin" or "elastic" — whoever really answered.</summary>
    public required string ServedBy { get; init; }

    public required IReadOnlyList<CollegeSearchRow> Rows { get; init; }
}
