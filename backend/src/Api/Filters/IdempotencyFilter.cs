using System.Security.Claims;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AiPlatform.Api.Filters;

// opt-in: [TypeFilter(typeof(IdempotencyFilter))] দেওয়া action-এ `Idempotency-Key` header এলে
// একই key-এর duplicate request LLM/DB আবার হিট না করে আগের response ফেরত পায়। header না দিলে normal।
public class IdempotencyFilter(IIdempotencyStore store) : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!store.IsEnabled
            || !http.Request.Headers.TryGetValue(HeaderName, out var header)
            || string.IsNullOrWhiteSpace(header))
        {
            await next();
            return;
        }

        // key user-scoped → cross-user collision নেই।
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon";
        var key = $"idem:{userId}:{header}";

        var begin = await store.BeginAsync(key, http.RequestAborted);
        switch (begin.State)
        {
            case IdempotencyState.Completed:
                context.Result = new ContentResult
                {
                    StatusCode = begin.Stored!.StatusCode,
                    Content = begin.Stored.Body,
                    ContentType = "application/json",
                };
                return;

            case IdempotencyState.InProgress:
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "A request with this Idempotency-Key is already being processed.",
                })
                { StatusCode = StatusCodes.Status409Conflict };
                return;
        }

        // Started — action চালাও, তারপর ফল cache (শুধু সফল হলে)।
        var executed = await next();

        if (executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } ok)
        {
            var body = JsonSerializer.Serialize(ok.Value);
            await store.CompleteAsync(key, new StoredResponse(ok.StatusCode ?? 200, body), http.RequestAborted);
        }
        else
        {
            // ব্যর্থ/অপ্রত্যাশিত → slot ছাড়ো যাতে client retry করতে পারে।
            await store.AbandonAsync(key, http.RequestAborted);
        }
    }
}
