namespace Solvia.Models.Enums;

/// <summary>
/// Permit catalog code. Stored in the database as TEXT (never as an
/// integer), per docs/SCHEMA.md — the EF Core conversion lives in
/// SolviaDbContext.
/// </summary>
public enum PermitTypeCode
{
    Daily,
    Term,
}
