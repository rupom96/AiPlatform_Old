using System.Security.Claims;
using AiPlatform.Application.Common.Interfaces;

namespace AiPlatform.Api.Common;

// HttpContext-এর claim থেকে user/tenant পড়ে। HTTP scope-এর বাইরে (background) HttpContext null → Empty tenant।
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public Guid TenantId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("tenantId"), out var tenant)
            ? tenant
            : Guid.Empty;
}
