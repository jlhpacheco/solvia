using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Solvia.Data;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The JSON twin of the public permit check (REDO-PLAN item 48):
/// GET /api/verify?t={token}.
///
/// Same token, same <see cref="PermitValidity"/> verdict, same
/// field set as the /verify page — by construction, not by
/// parallel code: both doors build the one
/// <see cref="VerifyViewModel"/> through
/// <see cref="VerifyResultBuilder"/>, and this controller simply
/// serializes it. No extra data rides along: no student name, no
/// email, and never the plate — the plate state is the most an
/// anonymous check may see. An unknown or missing token gets the
/// same neutral not-found body the page renders, as a calm 200.
/// The shared per-IP rate limiter (60/minute) guards this door
/// exactly as it guards the page; a tripped limit answers 429.
/// </summary>
[ApiController]
[Route("api/verify")]
public sealed class VerifyApiController : ControllerBase
{
    private readonly SolviaDbContext _db;
    private readonly VerifyRateLimiter _rateLimiter;

    public VerifyApiController(SolviaDbContext db, VerifyRateLimiter rateLimiter)
    {
        _db = db;
        _rateLimiter = rateLimiter;
    }

    // GET /api/verify?t=<unguessable token issued at the live transition>
    [HttpGet]
    public async Task<IActionResult> Get(
        string? t, CancellationToken cancellationToken = default)
    {
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.TryAcquire(clientKey))
        {
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new
                {
                    error = "Too many permit checks from this connection — " +
                            "please wait a minute and try again.",
                });
        }

        if (string.IsNullOrWhiteSpace(t))
        {
            return Ok(VerifyResultBuilder.NotFound());
        }

        var application = await _db.PermitApplications
            .Include(a => a.PermitType)
            .Include(a => a.Vehicle)
            .SingleOrDefaultAsync(a => a.VerifyToken == t.Trim(), cancellationToken);

        if (application is null)
        {
            return Ok(VerifyResultBuilder.NotFound());
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Los_Angeles"));
        return Ok(VerifyResultBuilder.FromApplication(application, today));
    }
}
