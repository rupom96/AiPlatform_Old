using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Application.Features.Audit;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audit")]
public class AuditController(IMediator mediator) : ApiControllerBase
{
    // current tenant-এর mutating action trail (newest-first, paged)
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new ListAuditQuery(page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }
}
