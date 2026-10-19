using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// The ONE price-resolution function (REDO-PLAN §2a). Every door and
/// every repair path asks here; nobody else computes a price.
///
/// - Term → the campus's published semester fee for the term
///   (<see cref="IFeeCatalog.SemesterPriceCents"/>). FREE campus = 0,
///   a real price. Unpublished = null, and null means BLOCKED — never
///   a fallback number, and never the PermitType row's placeholder.
/// - Daily → the campus's parsed day-pass price
///   (<see cref="CampusFee.DayPassCents"/>); null likewise blocks.
/// - The Employee exemption applies AFTER resolution (§2b): the
///   catalog is consulted first, so an unpublished fee blocks even an
///   employee application instead of silently issuing it. Faculty and
///   Visitor price exactly like Public.
/// </summary>
public sealed class PricingService
{
    private readonly IFeeCatalog _catalog;

    public PricingService(IFeeCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Resolves the price for a campus row, or null when the campus
    /// does not publish that price (the application must be blocked).
    /// <paramref name="term"/> is read only for Term permits — a day
    /// pass has no term.
    /// </summary>
    public int? ResolvePriceCents(CampusFee fee, PermitTypeCode typeCode, TermKind term)
    {
        ArgumentNullException.ThrowIfNull(fee);
        return typeCode switch
        {
            PermitTypeCode.Term => _catalog.SemesterPriceCents(fee, term),
            PermitTypeCode.Daily => fee.DayPassCents,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves the price for a buyer of the given category: the
    /// fee is resolved first (null still blocks), then an Employee
    /// buyer is exempted to 0. Everyone else pays the published fee.
    /// </summary>
    public int? ResolvePriceCents(
        CampusFee fee, PermitTypeCode typeCode, TermKind term, UserCategory category)
    {
        var resolved = ResolvePriceCents(fee, typeCode, term);
        if (resolved is null)
        {
            return null;
        }

        return category == UserCategory.Employee ? 0 : resolved;
    }

    /// <summary>
    /// Campus-name convenience for the doors: an unknown campus
    /// resolves to null (blocked), exactly like an unpublished fee.
    /// </summary>
    public int? ResolvePriceCents(
        string campusName, PermitTypeCode typeCode, TermKind term, UserCategory category)
    {
        var fee = _catalog.Find(campusName);
        return fee is null ? null : ResolvePriceCents(fee, typeCode, term, category);
    }

    /// <summary>True only for the exempt category (REDO-PLAN §2b).</summary>
    public static bool IsExempt(UserCategory category) =>
        category == UserCategory.Employee;
}
