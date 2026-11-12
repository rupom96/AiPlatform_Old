using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Api.Contracts;
using AiPlatform.Application.Features.Agent;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/agent")]
public class AgentController(IMediator mediator) : ApiControllerBase
{
    // tool-agent — LLM tools ব্যবহার করে multi-step উত্তর দেয়। provider tool-capable হতে হবে (groq/mistral)।
    [HttpPost]
    public async Task<IActionResult> Run(AgentRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RunAgentCommand(request.Message, request.Provider, request.Model), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }
}
