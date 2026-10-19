using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>
/// The catalog of what can be bought (docs/SCHEMA.md). Solvia seeds two
/// rows — Daily and Term. <see cref="PriceCents"/> is only a catalog
/// placeholder: the amount a student actually pays is the exact fee
/// snapshotted from the college's real row in data/fees.csv onto the
/// PermitApplication at purchase time.
/// </summary>
public sealed class PermitType
{
    public int Id { get; set; }

    /// <summary>Catalog code — stored in the database as TEXT.</summary>
    public PermitTypeCode Code { get; set; }

    /// <summary>Display name, e.g. "Term / semester permit".</summary>
    public string Name { get; set; } = "";

    /// <summary>How long a permit of this type lasts, in days.</summary>
    public int DurationDays { get; set; }

    /// <summary>Catalog price in cents (placeholder; see class note).</summary>
    public int PriceCents { get; set; }

    /// <summary>Only active types are offered to students.</summary>
    public bool Active { get; set; }
}
