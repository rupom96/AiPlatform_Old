using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Auth.Register;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Auth;

public class RegisterCommandHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    [Fact]
    public async Task Success_ReturnsUserIdFromIdentityService()
    {
        _identity.RegisterAsync("user@test.com", "Passw0rd!", "Test User", Arg.Any<CancellationToken>())
            .Returns(Result.Success("new-user-id"));

        var result = await new RegisterCommandHandler(_identity)
            .Handle(new RegisterCommand("user@test.com", "Passw0rd!", "Test User"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-user-id", result.Value);
    }

    [Fact]
    public async Task DuplicateOrWeak_PropagatesValidationFailure()
    {
        _identity.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(Error.Validation("Auth.Registration", "Email already taken.")));

        var result = await new RegisterCommandHandler(_identity)
            .Handle(new RegisterCommand("dupe@test.com", "Passw0rd!", "Dupe"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
