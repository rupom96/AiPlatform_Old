using AiPlatform.Application.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Common;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    // Result-এর Error → সঠিক HTTP status + ProblemDetails
    protected IActionResult ToProblem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(statusCode: status, title: error.Description, type: $"https://httpstatuses.io/{status}");
    }
}
