using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Auth.Refresh;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Auth;

public class RefreshCommandHandlerTests
{
    private readonly IRefreshTokenService _refresh = Substitute.For<IRefreshTokenService>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IJwtTokenGenerator _jwt = Substitute.For<IJwtTokenGenerator>();

    private RefreshCommandHandler CreateSut() => new(_refresh, _identity, _jwt);

    [Fact]
    public async Task RotationFails_ReturnsFailure_AndDoesNotGenerateToken()
    {
        _refresh.RotateAsync("bad", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RefreshRotation>(Error.Unauthorized("Auth.RefreshTokenReuse", "reuse")));

        var result = await CreateSut().Handle(new RefreshCommand("bad"), CancellationToken.None);

        Assert.True(result.IsFailure);
        _jwt.DidNotReceive().GenerateAccessToken(Arg.Any<AuthUser>());
    }

    [Fact]
    public async Task RotationSucceeds_UserExists_ReturnsNewAccessAndRotatedRefresh()
    {
        var newRefresh = new RefreshTokenResult("rotated-refresh", DateTime.UtcNow.AddDays(7), true);
        _refresh.RotateAsync("good", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RefreshRotation("user-1", newRefresh)));
        var user = new AuthUser("user-1", "user@test.com", "Test User", ["User"]);
        _identity.GetUserByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(Result.Success(user));
        _jwt.GenerateAccessToken(user).Returns(new AccessToken("new-access", DateTime.UtcNow.AddMinutes(15)));

        var result = await CreateSut().Handle(new RefreshCommand("good"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access", result.Value.AccessToken);
        Assert.Equal("rotated-refresh", result.Value.RefreshToken);
        Assert.True(result.Value.RememberMe);   // family-র rememberMe rotate-এও বহাল
    }

    [Fact]
    public async Task RotationSucceeds_ButUserGone_ReturnsFailure()
    {
        _refresh.RotateAsync("good", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RefreshRotation("ghost", new RefreshTokenResult("r", DateTime.UtcNow.AddDays(7), false))));
        _identity.GetUserByIdAsync("ghost", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AuthUser>(Error.NotFound("Auth.UserNotFound", "User not found.")));

        var result = await CreateSut().Handle(new RefreshCommand("good"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
