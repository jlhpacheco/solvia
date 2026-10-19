using Solvia.Models.Enums;

namespace Solvia.Models;

/// <summary>
/// A demo person. Solvia has no login stack (REDO-PLAN §2b): the
/// people in the database are SEEDED, FICTIONAL — Maria and her
/// classmates, plus Elena (Employee) and Pat (Public) so every
/// category branch is demoable honestly. <see cref="Id"/> is a short
/// slug assigned on the server; it is the owner key stamped onto
/// vehicles and permit applications (never trusted from a form).
/// </summary>
public sealed class Student
{
    /// <summary>Server-set owner id, e.g. "maria". Primary key.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name, e.g. "Maria".</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Fictional contact address (always an .example domain).</summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// Per-person category override (REDO-PLAN §2b): when set, it wins
    /// over the email-domain rule in UserCategoryResolver — the demo
    /// equivalent of an admin-set category flag. Seeded null for every
    /// fictional person; no screen edits it. Stored as TEXT.
    /// </summary>
    public UserCategory? CategoryOverride { get; set; }
}
