namespace AiPlatform.Infrastructure.Auth;

// appsettings "Jwt" section থেকে bind হয়। SecretKey কখনো appsettings-এ নয় — user-secrets/env।
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 7;
    public string SecretKey { get; init; } = string.Empty;
}
