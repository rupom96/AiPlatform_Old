using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Refresh;

public class RefreshCommandHandler(
    IRefreshTokenService refreshTokenService,
    IIdentityService identityService,
    IJwtTokenGenerator tokenGenerator)
    : IRequestHandler<RefreshCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        var rotation = await refreshTokenService.RotateAsync(request.RefreshToken, cancellationToken);
        if (rotation.IsFailure)
            return Result.Failure<AuthResponse>(rotation.Error);

        // rotate সফল হলেও user এখনো আছে/roles current কিনা যাচাই করে নতুন access token
        var userResult = await identityService.GetUserByIdAsync(rotation.Value.UserId, cancellationToken);
        if (userResult.IsFailure)
            return Result.Failure<AuthResponse>(userResult.Error);

        var accessToken = tokenGenerator.GenerateAccessToken(userResult.Value);
        var newRefresh = rotation.Value.NewToken;

        return new AuthResponse(
            accessToken.Token, accessToken.ExpiresAtUtc,
            newRefresh.Token, newRefresh.ExpiresAtUtc,
            newRefresh.RememberMe);
    }
}
