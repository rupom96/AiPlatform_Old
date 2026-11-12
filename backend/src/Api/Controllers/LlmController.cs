using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Application.Features.Llm.ListModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/llm")]
public class LlmController(IMediator mediator) : ApiControllerBase
{
    // ollama-তে installed model তালিকা — frontend model-picker dropdown (ollama select করলে)।
    // provider: "ollama" (এই মেশিন, default) | "ollama-server" (GPU সার্ভার) — UI-র toggle।
    [HttpGet("models")]
    public async Task<IActionResult> Models([FromQuery] string? provider, CancellationToken cancellationToken)
    {
        var models = await mediator.Send(new ListLocalModelsQuery(provider), cancellationToken);
        return Ok(models);
    }
}
