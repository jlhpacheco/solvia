using Solvia.Models.Enums;

namespace Solvia.Services;

/// <summary>
/// Read-only catalog of the real college permit fees in data/fees.csv
/// (BUILD-PLAN §2 contract, owned by T1). This is the ONLY source of
/// prices in Solvia: the AI quotes from here, and the amount sent to
/// PayPal is snapshotted from here — never a generic or invented price.
/// </summary>
public interface IFeeCatalog
{
    /// <summary>
    /// Finds a campus by name (case-insensitive, surrounding whitespace
    /// ignored). Returns null only when the campus is not in the CSV at
    /// all; a campus that IS listed but publishes no price comes back
    /// with null price fields.
    /// </summary>
    CampusFee? Find(string campusName);

    /// <summary>All campuses, in CSV order.</summary>
    IReadOnlyList<CampusFee> All();

    /// <summary>
    /// The campus's published price, in cents, for one term — or null
    /// when the campus does not publish that term's price. A FREE campus
    /// returns 0 (a real price), never null.
    /// </summary>
    int? SemesterPriceCents(CampusFee fee, TermKind term);
}
