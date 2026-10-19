using Solvia.Services;

namespace Solvia.Models.ViewModels;

// College-side ViewModels (BUILD-PLAN T5): the roster ("who paid / who
// hasn't") and the public permit verification result. Like the student
// flow, every screen is BIG and SIMPLE — plain words, few numbers.

/// <summary>One student's row on a campus roster.</summary>
public sealed record RosterRow(
    string StudentName,
    RosterPermitState State,
    string StateLabel,
    string? PermitNumber,
    DateOnly? StartDate,
    DateOnly? EndDate);

/// <summary>The roster page for one campus: every seeded student, one state each.</summary>
public sealed class RosterViewModel
{
    public required string Campus { get; init; }

    public required IReadOnlyList<RosterRow> Rows { get; init; }

    /// <summary>All catalog campus names, for the campus picker.</summary>
    public required IReadOnlyList<string> Campuses { get; init; }

    /// <summary>Students holding a live-issued permit (Paid or Free) at this campus.</summary>
    public int WithPermitCount => Rows.Count(r =>
        r.State is RosterPermitState.Paid or RosterPermitState.FreeIssued);

    /// <summary>Students with no issued permit at this campus (Pending or none).</summary>
    public int WithoutPermitCount => Rows.Count - WithPermitCount;
}

/// <summary>
/// The /verify result page. Only ever describes the ONE permit the
/// token names — never any other student's data. For an unknown token
/// every permit field stays null and the reason is the neutral
/// "could not find" text.
/// </summary>
public sealed class VerifyViewModel
{
    public required bool IsValid { get; init; }

    /// <summary>The plain-words reason, shown on both the valid and the not-valid page.</summary>
    public required string Reason { get; init; }

    public string? PermitNumber { get; init; }

    public string? Campus { get; init; }

    public string? StudentName { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }
}
