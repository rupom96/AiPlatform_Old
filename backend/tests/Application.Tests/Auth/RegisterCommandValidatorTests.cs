using AiPlatform.Application.Features.Auth.Register;
using Xunit;

namespace AiPlatform.Application.Tests.Auth;

public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Theory]
    [InlineData("", "Passw0rd!", "Name")]              // empty email
    [InlineData("not-an-email", "Passw0rd!", "Name")]  // bad email
    [InlineData("user@test.com", "short", "Name")]     // password < 8
    [InlineData("user@test.com", "Passw0rd!", "")]     // empty name
    public void InvalidInput_FailsValidation(string email, string password, string fullName)
    {
        var result = _validator.Validate(new RegisterCommand(email, password, fullName));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidInput_PassesValidation()
    {
        var result = _validator.Validate(new RegisterCommand("user@test.com", "Passw0rd!", "Test User"));
        Assert.True(result.IsValid);
    }
}
