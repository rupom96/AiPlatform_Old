using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.GoogleLogin;

public class GoogleLoginCommandHandler(
    IGoogleIdTokenValidator googleValidator,
    IIdentityService identityService,
    IJwtTokenGenerator tokenGenerator,
    IRefreshTokenService refreshTokenService)
    : IRequestHandler<GoogleLoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await googleValidator.ValidateAsync(request.IdToken, cancellationToken);
        if (googleUser.IsFailure)
            return Result.Failure<AuthResponse>(googleUser.Error);

        // email Google যাচাই করেছে — আগের account থাকলে সেটাই, নইলে নতুন (password-হীন)
        var user = await identityService.GetOrCreateExternalUserAsync(
            googleUser.Value.Email, googleUser.Value.FullName, cancellationToken);
        if (user.IsFailure)
            return Result.Failure<AuthResponse>(user.Error);

        var accessToken = tokenGenerator.GenerateAccessToken(user.Value);
        var refreshToken = await refreshTokenService.IssueAsync(user.Value.Id, rememberMe: true, cancellationToken);

        return new AuthResponse(
            accessToken.Token, accessToken.ExpiresAtUtc,
            refreshToken.Token, refreshToken.ExpiresAtUtc,
            refreshToken.RememberMe);
    }
}
