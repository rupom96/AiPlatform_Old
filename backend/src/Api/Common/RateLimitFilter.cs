using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StackExchange.Redis;

namespace AiPlatform.Api.Common;

// Redis fixed-window rate limiter — distributed (সব replica এক Redis counter দেখে)।
// key = ratelimit:{path}:{ip}; INCR + first hit-এ EXPIRE; limit ছাড়ালে 429।
public class RateLimitFilter(IConnectionMultiplexer redis, int limit, int windowSeconds) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // ponytail: nginx-এর পেছনে (Phase 5) RemoteIpAddress = proxy IP; তখন ForwardedHeaders লাগবে
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"ratelimit:{context.HttpContext.Request.Path}:{ip}";

        var db = redis.GetDatabase();
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(windowSeconds));

        if (count > limit)
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            var retryAfter = (int)(ttl?.TotalSeconds ?? windowSeconds);
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests. Please try again later.",
                Type = "https://httpstatuses.io/429"
            })
            { StatusCode = StatusCodes.Status429TooManyRequests };
            return;
        }

        await next();
    }
}
