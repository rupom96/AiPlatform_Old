using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;

namespace AiPlatform.Application.Common.Interfaces;

// Refresh token Redis-এ token-family হিসেবে থাকে (rotation + reuse-detection)।
public interface IRefreshTokenService
{
    // login: নতুন family + token। rememberMe family-তে জমা থাকে (cookie persistence rotate-এও টেকে)।
    Task<RefreshTokenResult> IssueAsync(string userId, bool rememberMe, CancellationToken cancellationToken);

    // refresh: token যাচাই + rotate। পুরনো (superseded) token এলে reuse → family revoke → failure।
    Task<Result<RefreshRotation>> RotateAsync(string presentedToken, CancellationToken cancellationToken);

    // logout: token-এর পুরো family revoke (idempotent)।
    Task RevokeAsync(string presentedToken, CancellationToken cancellationToken);
}
