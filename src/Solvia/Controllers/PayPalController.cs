using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// PayPal sandbox API behind the Checkout page's PayPal JS SDK buttons
/// (BUILD-PLAN T4). The buttons call create-order, PayPal collects the
/// buyer's approval, then the page calls capture — and all money truth
/// stays server-side in <see cref="CheckoutService"/>: Paid is set only
/// from a COMPLETED capture response, never from anything the browser
/// reports.
///
/// Errors are clear, small JSON bodies — and when the sandbox
/// credentials are simply not configured on this machine yet, the
/// answer is a 503 saying exactly that (no crash loop, no secret).
/// </summary>
[ApiController]
[Route("api/paypal")]
public sealed class PayPalController : ControllerBase
{
    private readonly CheckoutService _checkout;
    private readonly ILogger<PayPalController> _logger;

    public PayPalController(CheckoutService checkout, ILogger<PayPalController> logger)
    {
        _checkout = checkout;
        _logger = logger;
    }

    /// <summary>Request body for create-order: which permit is being paid for.</summary>
    public sealed record CreateOrderRequest(Guid PermitApplicationId);

    /// <summary>Request body for capture: the approved PayPal order id.</summary>
    public sealed record CaptureRequest(string OrderId);

    /// <summary>
    /// Creates the sandbox order for a permit application. The JS SDK
    /// expects the new order id back as <c>{ "id": ... }</c>.
    /// </summary>
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _checkout.StartAsync(
                request.PermitApplicationId,
                returnUrl: null,
                cancelUrl: null,
                cancellationToken);
            return result.Kind switch
            {
                CheckoutStartKind.RedirectToPayPal => Ok(new { id = result.OrderId }),
                CheckoutStartKind.FreePermitConfirmed => Ok(new { id = (string?)null, free = true }),
                _ => Ok(new { id = result.OrderId, alreadyPaid = true }),
            };
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
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(ex, "PayPal create-order failed.");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "PayPal sandbox could not create the order. The permit is not paid." });
        }
    }

    /// <summary>
    /// Captures an approved order and reports the standing truth:
    /// <c>completed</c> is true only when PayPal's capture answered
    /// COMPLETED and the permit was marked Paid.
    /// </summary>
    [HttpPost("capture")]
    public async Task<IActionResult> Capture(
        [FromBody] CaptureRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _checkout.CaptureByOrderAsync(
                request.OrderId, cancellationToken);
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
        catch (PayPalApiException ex)
        {
            _logger.LogWarning(ex, "PayPal capture failed.");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "PayPal sandbox could not capture the order. The permit is not paid." });
        }
    }
}
