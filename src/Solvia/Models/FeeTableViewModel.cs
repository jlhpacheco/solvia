namespace Solvia.Models;

/// <summary>
/// One row of the college-side fee table (T6 seam). Display strings are
/// computed on the server from IFeeCatalog so BOTH renderers (AG Grid
/// Community and the native table) show byte-identical values:
/// a price the campus does not publish is "Not published" — never blank,
/// never invented — and a FREE campus shows "$0 FREE".
/// </summary>
public sealed record FeeTableRow(
    string Campus,
    string SemesterPrice,
    int? SemesterPriceCents,
    string DayPass,
    string SourceNote);

/// <summary>What Views/Shared/_FeeTable.cshtml needs: rows + which renderer.</summary>
public sealed class FeeTableViewModel
{
    /// <summary>"aggrid" (AG Grid Community) or "native" (server-rendered table).</summary>
    public required string Renderer { get; init; }

    public required IReadOnlyList<FeeTableRow> Rows { get; init; }
}
