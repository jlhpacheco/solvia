using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;

namespace Solvia.Controllers;

/// <summary>
/// Base for the demo flow controllers. Solvia has NO login stack
/// (REDO-PLAN §2b): the "signed-in" person is one of the SEEDED
/// fictional people, picked with the demo switcher on the home
/// screen and remembered in a cookie. Maria is the default. The id
/// is always resolved against the Students table on the server — a
/// cookie value naming no seeded person falls back to Maria — and
/// it is the server-set owner stamped onto vehicles and
/// applications, never a value trusted from a form.
/// </summary>
public abstract class DemoControllerBase : Controller
{
    public const string StudentCookie = "solvia_student";
    public const string DefaultStudentId = "maria";

    /// <summary>The current demo person's id (slug), "maria" unless switched.</summary>
    protected string CurrentStudentId =>
        Request.Cookies.TryGetValue(StudentCookie, out var id) &&
        !string.IsNullOrWhiteSpace(id)
            ? id.Trim().ToLowerInvariant()
            : DefaultStudentId;

    /// <summary>The current demo person row (falls back to Maria).</summary>
    protected async Task<Student> CurrentStudentAsync(
        SolviaDbContext db, CancellationToken cancellationToken)
    {
        var wanted = CurrentStudentId;
        var student = await db.Students
            .SingleOrDefaultAsync(s => s.Id == wanted, cancellationToken);
        return student ?? await db.Students
            .SingleAsync(s => s.Id == DefaultStudentId, cancellationToken);
    }
}
