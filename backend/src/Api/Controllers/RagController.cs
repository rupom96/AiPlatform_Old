using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Api.Contracts;
using AiPlatform.Application.Features.Rag;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/rag")]
public class RagController(IMediator mediator) : ApiControllerBase
{
    // context থেকে grounded উত্তর + কোন chunk ব্যবহার হলো (sources)
    [HttpPost]
    public async Task<IActionResult> Query(RagRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RagQueryCommand(request.Question, request.Method, request.Provider, request.Model), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }
}
