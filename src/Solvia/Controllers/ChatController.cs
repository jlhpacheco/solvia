using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Controllers;

/// <summary>
/// The extraction endpoint (REDO-PLAN §3 R3): one student message in,
/// one validated permit draft out, as JSON.
///
/// The draft carries NO price — the AI never sets the price; the
/// flow takes the price from the fee catalog for the resolved
/// campus. The <c>source</c> field is honest about which path
/// answered: "ollama" or "together" when the selected model answered and
/// passed validation, "fallback" when the deterministic extractor
/// answered because the model was down or its answer failed
/// validation.
/// </summary>
[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IAiExtractor _extractor;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IAiExtractor extractor, ILogger<ChatController> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    /// <summary>Request body: the student's message, in their own words.</summary>
    public sealed record ExtractRequest(string? Text);

    [HttpPost("extract")]
    public async Task<IActionResult> Extract(
        [FromBody] ExtractRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Text))
        {
            return BadRequest(new { error = "A student message is required." });
        }

        if (request.Text.Length > PermitDraftViewModel.MaxCarTextLength)
        {
            return BadRequest(new { error = PermitDraftViewModel.CarTextLengthMessage });
        }

        var result = await _extractor.ExtractAsync(request.Text, cancellationToken);
        _logger.LogInformation(
            "Extraction answered by {Source}; college={College}",
            result.Source, result.College ?? "(none)");

        return Ok(new
        {
            college = result.College,
            permitType = result.PermitType?.ToString(),
            term = result.Term?.ToString(),
            carMake = result.CarMake,
            carModel = result.CarModel,
            carColor = result.CarColor,
            plate = result.Plate,
            carState = result.CarState,
            source = result.Source.ToWireName(),
        });
    }
}
