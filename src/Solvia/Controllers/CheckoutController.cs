using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The checkout pages (REDO-PLAN R6, items 19–21, 60):
///
///   GET  /Checkout/{id}        the checkout page (Views/Permits/Checkout).
///                              Guards (item 19): unknown id OR another
///                              demo person's application → 404; Paid or
///                              priced ≤ $0 → redirect to My Permits —
///                              the page NEVER renders in those cases.
///   POST /Checkout/Start/{id}  the plain no-JS path: creates (or
///                              re-returns) the PayPal sandbox order via
///                              CheckoutService, then redirects the buyer
///                              to PayPal's own approval page.
///   GET  /Checkout/Return      PayPal sends the buyer back here with
///                              ?token=&lt;orderId&gt;. This is plumbing,
///                              not a landing page (item 60): it CAPTURES
///                              the order — only a COMPLETED capture marks
///                              the permit Paid, inside CheckoutService —
///                              then redirects with a banner. Merely
///                              landing here changes nothing, and no
///                              success state ever renders from here.
///   GET  /Checkout/Cancel      buyer bailed at PayPal: same plumbing —
///                              a redirect with a banner; nothing charged,
///                              nothing paid.
///   GET  /Checkout/Done/{id}   the receipt page (Views/Checkout/Done).
/// </summary>
public sealed class CheckoutController : DemoControllerBase
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
        var application = await LoadOwnedAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        // Item 19: Paid and $0 permits never see a checkout page —
        // their home is My Permits.
        if (application.Status == ApplicationStatus.Denied || application.Category == UserCategory.Employee ||
            application.PaymentStatus is not (PaymentStatus.Pending or PaymentStatus.Failed) ||
            application.PriceCents <= 0)
        {
            return Redirect("/Permits/My");
        }

        ViewData["Title"] = "Checkout";
        // The JS SDK's client id + currency come from config —
        // never hardcoded.
        ViewData["PayPalClientId"] = _payPalOptions.ClientId;
        ViewData["PayPalCurrency"] = string.IsNullOrWhiteSpace(_payPalOptions.Currency)
            ? "USD"
            : _payPalOptions.Currency;
        ViewData["CheckoutError"] = TempData["CheckoutError"] as string;
        ViewData["CheckoutInfo"] = TempData["CheckoutInfo"] as string;
        return View("~/Views/Permits/Checkout.cshtml", application);
    }

    [HttpPost("Checkout/Start/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
    {
        var application = await LoadOwnedAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        // Return/cancel point at OUR endpoints, on this host — PayPal
        // sends the buyer back to us after approval.
        var returnUrl = $"{Request.Scheme}://{Request.Host}/Checkout/Return";
        var cancelUrl = $"{Request.Scheme}://{Request.Host}/Checkout/Cancel";

        try
        {
            var result = await _checkout.StartAsync(
                id, returnUrl, cancelUrl, cancellationToken);
            return result.Kind switch
            {
                // A fresh order: off to PayPal's approval page. A
                // re-returned (idempotent) order has no stored
                // approve URL — the checkout page's buttons reuse
                // the same order id, so the buyer goes back there.
                CheckoutStartKind.OrderReady when result.ApproveUrl is not null =>
                    Redirect(result.ApproveUrl),
                CheckoutStartKind.OrderReady =>
                    RedirectToAction(nameof(Index), new { id }),
                CheckoutStartKind.NotRequired =>
                    RedirectToAction(nameof(Done), new { id }),
                CheckoutStartKind.AlreadyPaid =>
                    RedirectToAction(nameof(Done), new { id }),
                _ => RedirectToAction(nameof(Index), new { id }),
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "This permit changed during checkout. Check its current status before retrying.";
            return Redirect("/Permits/My");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "PayPal sandbox connection failed.");
            TempData["CheckoutError"] = "PayPal sandbox is temporarily unavailable. Check your permit status before retrying.";
            return RedirectToAction(nameof(Index), new { id });
        }
        catch (PayPalNotConfiguredException ex)
        {
            // Clear error state on the page — not a crash, not a loop.
            TempData["CheckoutError"] = ex.Message;
            return RedirectToAction(nameof(Index), new { id });
        }
        catch (CheckoutPricingException ex)
        {
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
        var application = await FindOwnedByOrderAsync(token, cancellationToken);
        if (application is null)
        {
            // A bogus (or someone else's) token renders NO success
            // state — it goes home with an honest banner (item 60).
            TempData["ErrorMessage"] =
                "We could not find that PayPal order — nothing was charged " +
                "and no permit was marked paid.";
            return Redirect("/");
        }

        try
        {
            // The capture decides — this endpoint's arrival alone
            // proves nothing.
            var result = await _checkout.CaptureAsync(application.Id, cancellationToken);
            if (result.Completed)
            {
                TempData["StatusMessage"] =
                    "Payment complete — your permit is live.";
                return RedirectToAction(nameof(Done), new { id = application.Id });
            }

            TempData["CheckoutError"] =
                $"PayPal did not complete this payment (status: {result.Status}). " +
                "Your permit is not paid — you can try again.";
            return RedirectToAction(nameof(Index), new { id = application.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] = "This permit changed during payment. Check its current status before retrying.";
            return Redirect("/Permits/My");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "PayPal sandbox connection failed.");
            TempData["ErrorMessage"] = "PayPal sandbox is temporarily unavailable. Check your permit status before retrying.";
            return Redirect("/Permits/My");
        }
        catch (InvalidOperationException ex) when (ex is not PayPalNotConfiguredException)
        {
            TempData["ErrorMessage"] = "This permit cannot accept a payment. Check its current status.";
            return Redirect("/Permits/My");
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
        var application = await FindOwnedByOrderAsync(token, cancellationToken);
        if (application is null)
        {
            return Redirect("/");
        }

        TempData["CheckoutInfo"] =
            "Payment cancelled at PayPal — nothing was charged and your permit is not paid.";
        return RedirectToAction(nameof(Index), new { id = application.Id });
    }

    [HttpGet("Checkout/Done/{id:guid}")]
    public async Task<IActionResult> Done(Guid id, CancellationToken cancellationToken)
    {
        var application = await LoadOwnedAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Permit status";
        ViewData["AmountDisplay"] = FormatAmount(application.PriceCents);
        return View(application);
    }

    /// <summary>
    /// Loads an application WITH its type + vehicle, enforcing the
    /// demo-person owner rule: unknown id and someone else's
    /// application are the same answer — 404.
    /// </summary>
    private async Task<Models.PermitApplication?> LoadOwnedAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .Include(a => a.PermitType)
            .Include(a => a.Vehicle)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return application is not null && application.StudentId == CurrentStudentId
            ? application
            : null;
    }

    /// <summary>
    /// Resolves a PayPal return/cancel token (an order id) to the
    /// current demo person's application — null when the token is
    /// empty, unknown, or belongs to another demo person.
    /// </summary>
    private async Task<Models.PermitApplication?> FindOwnedByOrderAsync(
        string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var application = await _checkout.FindByOrderIdAsync(token, cancellationToken);
        return application is not null && application.StudentId == CurrentStudentId
            ? application
            : null;
    }

    private static string FormatAmount(int cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
