using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Application.Features.Evals;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/evals")]
public class EvalsController(IMediator mediator) : ApiControllerBase
{
    // RAG grounding eval dataset চালিয়ে pass rate + per-case report ফেরায়।
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromQuery] string? provider, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RunEvalsCommand(provider), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }
}
