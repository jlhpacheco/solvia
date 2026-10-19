using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>
/// The catalog of what can be bought (REDO-PLAN item 9 — the reference
/// app's row shape, field for field). Solvia seeds two rows — Daily
/// and Term. <see cref="PriceCents"/> is a seeded-0, NON-AUTHORITATIVE
/// placeholder: the amount anyone actually pays is resolved from the
/// college's published fee row (data/fees.csv) by PricingService and
/// snapshotted onto the PermitApplication. No code path may charge
/// from this column — a guard test pins that (REDO-PLAN §2a).
/// </summary>
public sealed class PermitType
{
    public int Id { get; set; }

    /// <summary>Catalog code — stored in the database as TEXT.</summary>
    public PermitTypeCode Code { get; set; }

    /// <summary>Display name, e.g. "Term / semester permit" (required, ≤ 200).</summary>
    public string Name { get; set; } = "";

    /// <summary>How long a permit of this type lasts, in days.</summary>
    public int DurationDays { get; set; }

    /// <summary>Seeded 0. Placeholder only — never a price source (see class note).</summary>
    public int PriceCents { get; set; }

    /// <summary>Only active types are offered on the Apply form.</summary>
    public bool Active { get; set; }
}
