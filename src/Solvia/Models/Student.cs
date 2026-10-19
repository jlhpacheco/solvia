namespace Solvia.Models;

/// <summary>
/// A student. Solvia has no login stack (BUILD-PLAN §1): the students in
/// the database are SEEDED, FICTIONAL demo people — Maria and a few
/// classmates. <see cref="Id"/> is a short slug assigned on the server;
/// it is the owner key stamped onto vehicles and permit applications
/// (never trusted from a form).
/// </summary>
public sealed class Student
{
    /// <summary>Server-set owner id, e.g. "maria". Primary key.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name, e.g. "Maria".</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Fictional contact address (always an .example domain).</summary>
    public string Email { get; set; } = "";
}
