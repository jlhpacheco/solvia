using Microsoft.AspNetCore.Mvc;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>Opt-in local fictional data only. This is not a production admin/auth surface.</summary>
[Route("Operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationsController(
    PaymentReviewService review, IWebHostEnvironment environment, IConfiguration configuration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? campus, string? status, string? category,
        bool attentionOnly = false, CancellationToken cancellationToken = default)
    {
        // Never allow an environment or feature flag alone to open this page.
        if (!(environment.IsDevelopment() || environment.IsEnvironment("Testing")) ||
            !configuration.GetValue<bool>("Demo:PaymentReviewEnabled"))
            return NotFound();

        if (!ModelState.IsValid || !TryFilter(status, out PaymentStatus? paymentStatus) ||
            !TryFilter(category, out UserCategory? userCategory) || campus?.Length > 200)
            return BadRequest("Choose a listed payment status, category, and campus.");

        var model = await review.ReadAsync(campus, paymentStatus, userCategory,
            attentionOnly, cancellationToken: cancellationToken);
        if (!string.IsNullOrEmpty(campus) && !model.Campuses.Contains(campus, StringComparer.Ordinal))
            return BadRequest("Choose a listed campus.");
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return View(model);
    }

    private static bool TryFilter<T>(string? value, out T? parsed) where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrEmpty(value)) return true;
        // Only textual names from the actual choices; Enum.TryParse alone
        // would also accept numeric values and comma combinations.
        if (!Enum.GetNames<T>().Contains(value, StringComparer.Ordinal)) return false;
        parsed = Enum.Parse<T>(value);
        return true;
    }
}
