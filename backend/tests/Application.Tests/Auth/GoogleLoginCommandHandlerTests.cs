using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Auth.GoogleLogin;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Auth;

public class GoogleLoginCommandHandlerTests
{
    private readonly IGoogleIdTokenValidator _google = Substitute.For<IGoogleIdTokenValidator>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IJwtTokenGenerator _jwt = Substitute.For<IJwtTokenGenerator>();
    private readonly IRefreshTokenService _refresh = Substitute.For<IRefreshTokenService>();

    private GoogleLoginCommandHandler CreateSut() => new(_google, _identity, _jwt, _refresh);

    [Fact]
    public async Task ValidGoogleToken_ReturnsTokens_WithPersistentSession()
    {
        _google.ValidateAsync("google-jwt", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new GoogleUserInfo("g@test.com", "G User")));
        var user = new AuthUser("user-1", "g@test.com", "G User", ["User"]);
        _identity.GetOrCreateExternalUserAsync("g@test.com", "G User", Arg.Any<CancellationToken>())
            .Returns(Result.Success(user));
        _jwt.GenerateAccessToken(user).Returns(new AccessToken("access-jwt", DateTime.UtcNow.AddMinutes(15)));
        _refresh.IssueAsync("user-1", true, Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenResult("refresh-token", DateTime.UtcNow.AddDays(7), true));

        var result = await CreateSut().Handle(new GoogleLoginCommand("google-jwt"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-jwt", result.Value.AccessToken);
        Assert.True(result.Value.RememberMe);   // Google login = সবসময় persistent
    }

    [Fact]
    public async Task InvalidGoogleToken_ReturnsFailure_AndCreatesNoUser()
    {
        _google.ValidateAsync("bad", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GoogleUserInfo>(Error.Unauthorized("Auth.InvalidGoogleToken", "Invalid Google token.")));

        var result = await CreateSut().Handle(new GoogleLoginCommand("bad"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        await _identity.DidNotReceive()
            .GetOrCreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UserCreationFails_ReturnsFailure_AndIssuesNoTokens()
    {
        _google.ValidateAsync("google-jwt", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new GoogleUserInfo("g@test.com", "G User")));
        _identity.GetOrCreateExternalUserAsync("g@test.com", "G User", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AuthUser>(Error.Validation("Auth.ExternalLogin", "boom")));

        var result = await CreateSut().Handle(new GoogleLoginCommand("google-jwt"), CancellationToken.None);

        Assert.True(result.IsFailure);
        await _refresh.DidNotReceive().IssueAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
