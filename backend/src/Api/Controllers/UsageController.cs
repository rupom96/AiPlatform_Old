using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Application.Features.Usage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/usage")]
public class UsageController(IMediator mediator) : ApiControllerBase
{
    // LLM cost/token/usage summary (per provider+model)
    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetUsageSummaryQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }
}
