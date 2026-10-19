using Microsoft.AspNetCore.Mvc;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The inbound PayPal webhook endpoint (REDO-PLAN item 33):
///
///   POST /api/paypal/webhook
///
/// A deliberate shell. PayPal's servers call this — there is no
/// browser, no demo person, and no antiforgery token (the endpoint
/// is antiforgery-exempt BY DESIGN; the PayPal signature headers are
/// its credential, verified inside
/// <see cref="PayPalWebhookHandler"/> through the one PayPal talker
/// before anything is trusted or saved). The controller reads the
/// raw body untouched (verification needs the exact bytes' text),
/// hands body + headers to the handler, and answers with the
/// handler's status code: 401 for anything unverifiable, 400 for a
/// delivery with no event id, 200 for everything handled —
/// including duplicates and ignored event types, so PayPal stops
/// retrying what has already had its say.
/// </summary>
[ApiController]
[Route("api/paypal")]
public sealed class PayPalWebhookController : ControllerBase
{
    private readonly PayPalWebhookHandler _handler;

    public PayPalWebhookController(PayPalWebhookHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("webhook")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        var publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
        var outcome = await _handler.HandleAsync(
            rawBody, Request.Headers, publicBaseUrl, cancellationToken);
        return StatusCode(outcome.StatusCode);
    }
}
