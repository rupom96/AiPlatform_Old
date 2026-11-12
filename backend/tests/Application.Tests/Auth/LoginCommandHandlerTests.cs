using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Auth.Login;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Auth;

public class LoginCommandHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IJwtTokenGenerator _jwt = Substitute.For<IJwtTokenGenerator>();
    private readonly IRefreshTokenService _refresh = Substitute.For<IRefreshTokenService>();

    private LoginCommandHandler CreateSut() => new(_identity, _jwt, _refresh);

    [Fact]
    public async Task ValidCredentials_ReturnsAccessAndRefreshTokens()
    {
        var user = new AuthUser("user-1", "user@test.com", "Test User", ["User"]);
        _identity.ValidateCredentialsAsync("user@test.com", "pw", Arg.Any<CancellationToken>())
            .Returns(Result.Success(user));
        _jwt.GenerateAccessToken(user).Returns(new AccessToken("access-jwt", DateTime.UtcNow.AddMinutes(15)));
        _refresh.IssueAsync("user-1", false, Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenResult("refresh-token", DateTime.UtcNow.AddDays(7), false));

        var result = await CreateSut().Handle(new LoginCommand("user@test.com", "pw"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-jwt", result.Value.AccessToken);
        Assert.Equal("refresh-token", result.Value.RefreshToken);
        Assert.False(result.Value.RememberMe);
    }

    [Fact]
    public async Task RememberMe_FlowsThroughToTokenIssueAndResponse()
    {
        var user = new AuthUser("user-1", "user@test.com", "Test User", ["User"]);
        _identity.ValidateCredentialsAsync("user@test.com", "pw", Arg.Any<CancellationToken>())
            .Returns(Result.Success(user));
        _jwt.GenerateAccessToken(user).Returns(new AccessToken("access-jwt", DateTime.UtcNow.AddMinutes(15)));
        _refresh.IssueAsync("user-1", true, Arg.Any<CancellationToken>())
            .Returns(new RefreshTokenResult("refresh-token", DateTime.UtcNow.AddDays(7), true));

        var result = await CreateSut().Handle(
            new LoginCommand("user@test.com", "pw", RememberMe: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RememberMe);
        await _refresh.Received(1).IssueAsync("user-1", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidCredentials_ReturnsFailure_AndIssuesNoTokens()
    {
        _identity.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AuthUser>(Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password.")));

        var result = await CreateSut().Handle(new LoginCommand("user@test.com", "bad"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        _jwt.DidNotReceive().GenerateAccessToken(Arg.Any<AuthUser>());
        await _refresh.DidNotReceive().IssueAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
