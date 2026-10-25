using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// Public permit verification (REDO-PLAN items 46/47):
/// GET /verify?t={token}.
///
/// Anonymous ON PURPOSE — a campus officer (or anyone a student
/// shows their permit to) has no Solvia login; the unguessable
/// VerifyToken issued at the live transition is the credential. The
/// page only ever describes the one permit the token names, in the
/// item-46 field set: type, plate STATE (never the plate), the
/// snapshotted category, the privilege line, the payment status
/// word, the last-updated stamp, and the
/// <see cref="PermitValidity"/> verdict with its plain-words
/// reason. Every outcome is a calm HTTP 200 page — VALID, NOT VALID
/// with the reason, "(not issued)" for the defensive token-without-
/// a-number state, or the same neutral "could not find" page for an
/// unknown token as for a missing one.
///
/// A lightweight per-IP rate limiter (60/minute, shared with the
/// JSON twin) guards the door; a tripped limit answers 429.
/// </summary>
[Route("verify")]
public sealed class VerifyController : Controller
{
    private readonly SolviaDbContext _db;
    private readonly VerifyRateLimiter _rateLimiter;

    public VerifyController(SolviaDbContext db, VerifyRateLimiter rateLimiter)
    {
        _db = db;
        _rateLimiter = rateLimiter;
    }

    // GET /verify?t=<unguessable token issued at the live transition>
    [HttpGet]
    public async Task<IActionResult> Index(
        string? t, CancellationToken cancellationToken = default)
    {
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.TryAcquire(clientKey))
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                "Too many permit checks from this connection — " +
                "please wait a minute and try again.");
        }

        if (string.IsNullOrWhiteSpace(t))
        {
            return View("Invalid", VerifyResultBuilder.NotFound());
        }

        var application = await _db.PermitApplications
            .Include(a => a.PermitType)
            .Include(a => a.Vehicle)
            .SingleOrDefaultAsync(a => a.VerifyToken == t.Trim(), cancellationToken);

        if (application is null)
        {
            return View("Invalid", VerifyResultBuilder.NotFound());
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Los_Angeles"));
        var model = VerifyResultBuilder.FromApplication(application, today);
        return View(model.IsValid ? "Verify" : "Invalid", model);
    }
}
