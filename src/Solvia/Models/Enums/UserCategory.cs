namespace Solvia.Models.Enums;

/// <summary>
/// Who a person is, for pricing (REDO-PLAN §2b). Solvia has no login
/// stack, so a demo person's category is resolved by rule from the
/// seeded fictional person (email domain + a per-person override) —
/// never from a login claim — and snapshotted onto each application
/// at creation time. Stored in the database as TEXT.
/// </summary>
public enum UserCategory
{
    Student,
    Employee,
    Public,
    Faculty,
    Visitor,
}
