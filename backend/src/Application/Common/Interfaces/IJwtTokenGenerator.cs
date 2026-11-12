using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    // short-lived signed access token (claims: sub, email, roles)।
    AccessToken GenerateAccessToken(AuthUser user);
}
