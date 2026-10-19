namespace Solvia.Models.Enums;

/// <summary>
/// An academic term, used when asking the fee catalog for a campus's
/// semester price (IFeeCatalog.SemesterPriceCents). Fall and Spring are
/// the regular semesters; Summer is the shorter summer session.
/// </summary>
public enum TermKind
{
    Fall,
    Spring,
    Summer,
}
