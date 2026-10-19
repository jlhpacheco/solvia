namespace Solvia.Models.Enums;

/// <summary>
/// Where a permit application stands (REDO-PLAN item 50). Created
/// Pending; no admin approve/deny action exists in the demo — permit
/// validity is computed around Pending-counts-when-paid. Stored as TEXT.
/// </summary>
public enum ApplicationStatus
{
    Pending,
    Approved,
    Denied,
}
