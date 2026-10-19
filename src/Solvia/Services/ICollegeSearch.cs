namespace Solvia.Services;

/// <summary>
/// The outcome of one college search: the ranked campuses plus WHO
/// actually served them — "builtin" or "elastic". The search page
/// prints the provider on-screen, so a demo never silently pretends
/// Elastic answered when the built-in match did (docs/SPONSORS.md
/// honesty rule: claim only what actually ran).
/// </summary>
public sealed record CollegeSearchResult(
    IReadOnlyList<CampusFee> Campuses,
    string ServedBy);

/// <summary>
/// College search behind the find-your-college screen (REDO-PLAN
/// §2e, R8). Two implementations serve the SAME contract:
/// - <see cref="BuiltinCollegeSearch"/> — deterministic in-memory
///   match over <see cref="IFeeCatalog"/>'s 10 rows; the shipped
///   default, needs no server, never fails.
/// - <see cref="ElasticCollegeSearch"/> — a local Elasticsearch
///   <c>colleges</c> index (hybrid keyword multi_match), selected by
///   config <c>Search:Provider=elastic</c>; falls back to the
///   built-in match on ANY failure.
/// Either way the returned rows are the catalog's own
/// <see cref="CampusFee"/> records — search only ranks and finds;
/// prices always come from the catalog, never from the index.
/// </summary>
public interface ICollegeSearch
{
    /// <summary>
    /// Ranked campuses matching <paramref name="query"/> (a college
    /// name, a town, or words from the fee notes — e.g. "Fresno",
    /// "monterey", "motorcycle"). A null/empty query returns all 10
    /// campuses in catalog order.
    /// </summary>
    Task<CollegeSearchResult> SearchAsync(
        string? query, CancellationToken cancellationToken = default);
}
