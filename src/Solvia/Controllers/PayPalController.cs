using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Solvia.Data;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// PayPal sandbox API behind the Checkout page's PayPal JS SDK
/// buttons (REDO-PLAN R6, items 30/31):
///
///   POST /api/paypal/orders   create (idempotent — CheckoutService)
///   POST /api/paypal/capture  capture by order id
///
/// All money truth stays server-side in <see cref="CheckoutService"/>:
/// Paid is set only from a COMPLETED capture response, never from
/// anything the browser reports. Both endpoints are
/// antiforgery-protected (the page's script sends the token header)
/// and owner-checked against the current demo person: an unknown
/// application/order is a 404, another demo person's is a 403.
///
/// Errors are clear, small JSON bodies — and when the sandbox
/// credentials are simply not configured on this machine yet, the
/// answer is a 503 saying exactly that (no crash loop, no secret).
/// </summary>
[ApiController]
[Route("api/paypal")]
public sealed class PayPalController : DemoControllerBase
{
    private readonly SolviaDbContext _db;
    private readonly CheckoutService _checkout;
    private readonly ILogger<PayPalController> _logger;

    public PayPalController(
        SolviaDbContext db,
        CheckoutService checkout,
        ILogger<PayPalController> logger)
    {
        _db = db;
        _checkout = checkout;
        _logger = logger;
    }

    /// <summary>Request body for create-order: which permit is being paid for.</summary>
    public sealed record CreateOrderRequest(Guid PermitApplicationId);

    /// <summary>Request body for capture: the approved PayPal order id.</summary>
    public sealed record CaptureRequest(string OrderId);

    /// <summary>
    /// Creates the sandbox order for a permit application — or
    /// re-returns the stored one (item 30 idempotency). The JS SDK
    /// expects the order id back as <c>{ "id": ... }</c>; an
    /// application that needs no payment answers
    /// <c>{ "status": "not-required" }</c> instead (Employee
    /// exemption, a genuinely free permit, or one already Paid),
    /// and the page sends the buyer to the receipt.
    /// </summary>
    [HttpPost("orders")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var application = await _db.PermitApplications
            .SingleOrDefaultAsync(a => a.Id == request.PermitApplicationId, cancellationToken);
        if (application is null)
        {
            return NotFound(new { error = "That permit application was not found." });
        }

        if (application.StudentId != CurrentStudentId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "That permit belongs to a different demo person." });
        }

        try
        {
            var result = await _checkout.StartAsync(
                request.PermitApplicationId,
                returnUrl: null,
                cancelUrl: null,
                cancellationToken);
            return result.Kind switch
            {
                CheckoutStartKind.OrderReady => Ok(new
                {
                    id = result.OrderId,
                    status = "created",
                }),
                CheckoutStartKind.NotRequired => Ok(new
                {
                    id = (string?)null,
                    status = "not-required",
                }),
                _ => Ok(new
                {
                    id = result.OrderId,
                    status = "already-paid",
                }),
            };
        }
        catch (PayPalNotConfiguredException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (CheckoutPricingException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(ex, "PayPal create-order failed.");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "PayPal sandbox could not create the order. The permit is not paid." });
        }
    }

    /// <summary>
    /// Captures an approved order (owner-checked first, item 31) and
    /// reports the standing truth: <c>completed</c> is true only
    /// when PayPal's capture answered COMPLETED and the permit was
    /// marked Paid.
    /// </summary>
    [HttpPost("capture")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Capture(
        [FromBody] CaptureRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId))
        {
            return BadRequest(new { error = "An order id is required." });
        }

        var application = await _checkout.FindByOrderIdAsync(
            request.OrderId, cancellationToken);
        if (application is null)
        {
            return NotFound(new { error = "No permit application owns that PayPal order." });
        }

        if (application.StudentId != CurrentStudentId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "That permit belongs to a different demo person." });
        }

        try
        {
            var result = await _checkout.CaptureAsync(application.Id, cancellationToken);
            return Ok(new
            {
                status = result.Status,
                completed = result.Completed,
                captureId = result.CaptureId,
                receiptNumber = result.ReceiptNumber,
            });
        }
        catch (PayPalNotConfiguredException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(ex, "PayPal capture failed.");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "PayPal sandbox could not capture the order. The permit is not paid." });
        }
    }
}
