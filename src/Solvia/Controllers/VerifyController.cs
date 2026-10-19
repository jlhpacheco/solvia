using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// Public permit verification (BUILD-PLAN T5): GET /verify?t={token}.
///
/// Anonymous ON PURPOSE — a campus officer (or anyone a student shows
/// their permit to) has no Solvia login; the unguessable VerifyToken
/// issued at payment time is the credential. The page only ever
/// describes the one permit the token names. Every outcome is a calm
/// HTTP 200 page: VALID with the permit number, campus, and dates, or
/// NOT VALID with a plain-words reason. An unknown token gets the same
/// neutral "could not find" page as a missing one — never an error
/// stack, never a hint about other permits.
/// </summary>
public sealed class VerifyController : Controller
{
    private readonly SolviaDbContext _db;

    public VerifyController(SolviaDbContext db)
    {
        _db = db;
    }

    // GET /verify?t=<unguessable token issued at payment time>
    public async Task<IActionResult> Index(
        string? t, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(t))
        {
            return View("Invalid", NotFoundModel());
        }

        var application = await _db.PermitApplications
            .Include(a => a.Student)
            .SingleOrDefaultAsync(a => a.VerifyToken == t.Trim(), cancellationToken);

        if (application is null)
        {
            return View("Invalid", NotFoundModel());
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var check = PermitValidity.Check(application, today);

        if (!check.IsActive)
        {
            // The token is real, so the holder may be told their own
            // permit number and campus alongside the reason.
            return View("Invalid", new VerifyViewModel
            {
                IsValid = false,
                Reason = check.Reason,
                PermitNumber = application.ReceiptNumber,
                Campus = application.Campus,
            });
        }

        return View("Verify", new VerifyViewModel
        {
            IsValid = true,
            Reason = check.Reason,
            PermitNumber = application.ReceiptNumber,
            Campus = application.Campus,
            StudentName = application.Student?.DisplayName,
            StartDate = application.StartDate,
            EndDate = application.EndDate,
        });
    }

    /// <summary>The neutral answer for unknown or missing tokens — identical either way.</summary>
    private static VerifyViewModel NotFoundModel() => new()
    {
        IsValid = false,
        Reason = "We could not find a permit with that code. " +
                 "Check the code and try again.",
    };
}
