using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The plain server-side checkout flow (BUILD-PLAN T4; T2 builds the
/// full Maria UI later and links here):
///
///   GET  /Checkout/{id}         the checkout page (Views/Permits/Checkout)
///   POST /Checkout/Start/{id}   creates the PayPal sandbox order, then
///                               redirects the buyer to PayPal's own
///                               approval page on sandbox.paypal.com
///   GET  /Checkout/Return       PayPal sends the buyer back here with
///                               ?token=&lt;orderId&gt;; this endpoint
///                               CAPTURES the order — and only a
///                               COMPLETED capture marks the permit
///                               Paid (inside CheckoutService). Merely
///                               landing here never marks anything.
///   GET  /Checkout/Cancel       buyer bailed at PayPal: nothing charged,
///                               nothing paid, back to the checkout page.
///   GET  /Checkout/Done/{id}    plain status page (permit number + the
///                               PayPal ids when Paid). T2's Receipt
///                               page replaces this in the full UI.
/// </summary>
public sealed class CheckoutController : Controller
{
    private readonly SolviaDbContext _db;
    private readonly CheckoutService _checkout;
    private readonly PayPalOptions _payPalOptions;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        SolviaDbContext db,
        CheckoutService checkout,
        IOptions<PayPalOptions> payPalOptions,
        ILogger<CheckoutController> logger)
    {
        _db = db;
        _checkout = checkout;
        _payPalOptions = payPalOptions.Value;
        _logger = logger;
    }

    [HttpGet("Checkout/{id:guid}")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Checkout";
        // The JS SDK's client id comes from config — never hardcoded.
        ViewData["PayPalClientId"] = _payPalOptions.ClientId;
        ViewData["AmountDisplay"] = FormatAmount(application.PriceCents);
        ViewData["CheckoutError"] = TempData["CheckoutError"] as string;
        ViewData["CheckoutInfo"] = TempData["CheckoutInfo"] as string;
        return View("~/Views/Permits/Checkout.cshtml", application);
    }

    [HttpPost("Checkout/Start/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        // Return/cancel point at OUR endpoints, on this host — PayPal
        // sends the buyer back to us after approval.
        var returnUrl = Url.Action(nameof(Return), "Checkout", null, Request.Scheme);
        var cancelUrl = Url.Action(nameof(Cancel), "Checkout", null, Request.Scheme);

        try
        {
            var result = await _checkout.StartAsync(
                id, returnUrl, cancelUrl, cancellationToken);
            return result.Kind switch
            {
                CheckoutStartKind.RedirectToPayPal => Redirect(result.ApproveUrl!),
                CheckoutStartKind.FreePermitConfirmed =>
                    RedirectToAction(nameof(Done), new { id }),
                CheckoutStartKind.AlreadyPaid =>
                    RedirectToAction(nameof(Done), new { id }),
                _ => RedirectToAction(nameof(Index), new { id }),
            };
        }
        catch (PayPalNotConfiguredException ex)
        {
            // Clear error state on the page — not a crash, not a loop.
            TempData["CheckoutError"] = ex.Message;
            return RedirectToAction(nameof(Index), new { id });
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(
                ex, "PayPal create-order failed for application {ApplicationId}.", id);
            TempData["CheckoutError"] =
                "PayPal sandbox could not create the order. Nothing was charged " +
                "and the permit is not paid.";
            return RedirectToAction(nameof(Index), new { id });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("Checkout/Return")]
    public async Task<IActionResult> Return(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction("Index", "Home");
        }

        var application = await _checkout.FindByOrderIdAsync(token, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        try
        {
            // The capture decides — this page's arrival alone proves nothing.
            var result = await _checkout.CaptureAsync(application.Id, cancellationToken);
            if (result.Completed)
            {
                return RedirectToAction(nameof(Done), new { id = application.Id });
            }

            TempData["CheckoutError"] =
                $"PayPal did not complete this payment (status: {result.Status}). " +
                "Your permit is not paid — you can try again.";
            return RedirectToAction(nameof(Index), new { id = application.Id });
        }
        catch (PayPalNotConfiguredException ex)
        {
            TempData["CheckoutError"] = ex.Message;
            return RedirectToAction(nameof(Index), new { id = application.Id });
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(
                ex, "PayPal capture failed for application {ApplicationId}.", application.Id);
            TempData["CheckoutError"] =
                "PayPal sandbox could not capture the payment. Your permit is not paid.";
            return RedirectToAction(nameof(Index), new { id = application.Id });
        }
    }

    [HttpGet("Checkout/Cancel")]
    public async Task<IActionResult> Cancel(string? token, CancellationToken cancellationToken)
    {
        var application = string.IsNullOrWhiteSpace(token)
            ? null
            : await _checkout.FindByOrderIdAsync(token, cancellationToken);
        if (application is null)
        {
            return RedirectToAction("Index", "Home");
        }

        TempData["CheckoutInfo"] =
            "Payment cancelled at PayPal — nothing was charged and your permit is not paid.";
        return RedirectToAction(nameof(Index), new { id = application.Id });
    }

    [HttpGet("Checkout/Done/{id:guid}")]
    public async Task<IActionResult> Done(Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.PermitType)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Permit status";
        ViewData["AmountDisplay"] = FormatAmount(application.PriceCents);
        return View(application);
    }

    private static string FormatAmount(int cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
