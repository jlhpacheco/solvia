using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models;
using Solvia.Models.ViewModels;

namespace Solvia.Controllers;

/// <summary>
/// The home screen (R0 shell): one big door — "Get my parking
/// permit" — plus My Permits, and the demo-person switcher. Maria
/// is the default demo person; the switcher just picks which SEEDED
/// fictional person the demo acts as. There is no login stack and
/// no real personal data anywhere.
/// </summary>
public class HomeController : DemoControllerBase
{
    private readonly SolviaDbContext _db;

    public HomeController(SolviaDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var students = await _db.Students
            .OrderBy(s => s.DisplayName)
            .ToListAsync(cancellationToken);
        var current = await CurrentStudentAsync(_db, cancellationToken);

        return View(new HomeIndexViewModel
        {
            Students = students,
            CurrentStudentId = current.Id,
            CurrentStudentName = current.DisplayName,
        });
    }

    /// <summary>Demo switcher: act as another seeded fictional person.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchStudent(
        string id, CancellationToken cancellationToken)
    {
        var student = await _db.Students
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (student is not null)
        {
            Response.Cookies.Append(StudentCookie, student.Id, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                IsEssential = true,
            });
        }

        return RedirectToAction(nameof(Index));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
