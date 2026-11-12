using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Login;

public class LoginCommandHandler(
    IIdentityService identityService,
    IJwtTokenGenerator tokenGenerator,
    IRefreshTokenService refreshTokenService)
    : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var credentials = await identityService.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken);
        if (credentials.IsFailure)
            return Result.Failure<AuthResponse>(credentials.Error);

        var user = credentials.Value;
        var accessToken = tokenGenerator.GenerateAccessToken(user);
        var refreshToken = await refreshTokenService.IssueAsync(user.Id, request.RememberMe, cancellationToken);

        return new AuthResponse(
            accessToken.Token, accessToken.ExpiresAtUtc,
            refreshToken.Token, refreshToken.ExpiresAtUtc,
            refreshToken.RememberMe);
    }
}
