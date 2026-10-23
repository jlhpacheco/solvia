using Microsoft.AspNetCore.Mvc;
using Solvia.Services;

namespace Solvia.Controllers;

[ApiController]
[Route("api/ai/status")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiStatusController(AiOperationStatus status) : ControllerBase
{
    // Memory only. No model health probe, database, user input or external request.
    [HttpGet]
    public ActionResult<AiOperationSnapshot> Get() => Ok(status.GetSnapshot());
}
