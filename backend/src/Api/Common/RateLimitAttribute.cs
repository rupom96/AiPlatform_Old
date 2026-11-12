using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Common;

// [RateLimit(limit, windowSeconds)] — action-এ বসিয়ে per-IP rate limit।
// TypeFilter IConnectionMultiplexer DI থেকে দেয়, limit/window Arguments থেকে।
public class RateLimitAttribute : TypeFilterAttribute
{
    public RateLimitAttribute(int limit, int windowSeconds) : base(typeof(RateLimitFilter))
    {
        Arguments = [limit, windowSeconds];
    }
}
