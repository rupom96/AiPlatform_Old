using AiPlatform.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Common;

// Unexpected exception এখানে ধরা পড়ে → RFC 7807 ProblemDetails।
// Expected/business failure Result দিয়ে যায় (controller map করে), এখানে আসে না।
public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, errors) = Map(exception);

        // 5xx-ই কেবল error-level log (internal bug); 4xx warning যথেষ্ট
        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception");
        else
            logger.LogWarning(exception, "Request failed: {Title}", title);

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}"
        };
        if (errors is not null)
            problemDetails.Extensions["errors"] = errors;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static (int status, string title, IDictionary<string, string[]>? errors) Map(Exception exception) =>
        exception switch
        {
            ValidationException v => (
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                v.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray())),

            // upstream LLM fail → 502, raw body leak না (log-এ থাকে)
            LlmProviderException llm => (
                StatusCodes.Status502BadGateway,
                $"The '{llm.Provider}' LLM provider is unavailable (upstream status {llm.StatusCode}).",
                null),

            // internal detail leak না — generic message
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null)
        };
}
