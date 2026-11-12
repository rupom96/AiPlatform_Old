using System.Security.Cryptography;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiPlatform.Infrastructure.Auth;

// Token-family rotation + reuse-detection, Redis-এ।
//   refresh:{token}   → { userId, familyId }   (token চিনতে)
//   family:{familyId} → { userId, currentToken } (family-র বর্তমান বৈধ token)
// rotate করলে currentToken বদলায়; পুরনো token আবার এলে (currentToken != presented) = reuse → family revoke।
public class RefreshTokenService(IConnectionMultiplexer redis, IOptions<JwtSettings> options)
    : IRefreshTokenService
{
    private readonly JwtSettings _settings = options.Value;
    private TimeSpan Ttl => TimeSpan.FromDays(_settings.RefreshTokenDays);

    private static string TokenKey(string token) => $"refresh:{token}";
    private static string FamilyKey(string familyId) => $"family:{familyId}";

    private record TokenEntry(string UserId, string FamilyId);
    private record FamilyEntry(string UserId, string CurrentToken, bool RememberMe = false);

    public async Task<RefreshTokenResult> IssueAsync(string userId, bool rememberMe, CancellationToken cancellationToken)
    {
        var familyId = Guid.NewGuid().ToString("N");
        return await CreateTokenInFamilyAsync(userId, familyId, rememberMe);
    }

    public async Task<Result<RefreshRotation>> RotateAsync(string presentedToken, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();

        var tokenJson = await db.StringGetAsync(TokenKey(presentedToken));
        if (tokenJson.IsNullOrEmpty)
            return Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid or expired refresh token.");

        var entry = JsonSerializer.Deserialize<TokenEntry>(tokenJson.ToString())!;

        var familyJson = await db.StringGetAsync(FamilyKey(entry.FamilyId));
        if (familyJson.IsNullOrEmpty)
            return Error.Unauthorized("Auth.InvalidRefreshToken", "Invalid or expired refresh token.");

        var family = JsonSerializer.Deserialize<FamilyEntry>(familyJson.ToString())!;

        // presented token family-র current নয় → superseded token replay → reuse attack
        if (family.CurrentToken != presentedToken)
        {
            await RevokeFamilyAsync(db, entry.FamilyId, family.CurrentToken);
            return Error.Unauthorized("Auth.RefreshTokenReuse", "Refresh token reuse detected. Session revoked.");
        }

        // বৈধ rotation — family-র rememberMe নতুন token-এও বয়ে যায়
        var rotated = await CreateTokenInFamilyAsync(entry.UserId, entry.FamilyId, family.RememberMe);
        return new RefreshRotation(entry.UserId, rotated);
    }

    public async Task RevokeAsync(string presentedToken, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var tokenJson = await db.StringGetAsync(TokenKey(presentedToken));
        if (tokenJson.IsNullOrEmpty)
            return;   // idempotent

        var entry = JsonSerializer.Deserialize<TokenEntry>(tokenJson.ToString())!;
        var familyJson = await db.StringGetAsync(FamilyKey(entry.FamilyId));
        var currentToken = familyJson.IsNullOrEmpty
            ? presentedToken
            : JsonSerializer.Deserialize<FamilyEntry>(familyJson.ToString())!.CurrentToken;

        await RevokeFamilyAsync(db, entry.FamilyId, currentToken);
    }

    // family-র current token সেট করে নতুন token বানায় (issue + rotate দুটোই ব্যবহার করে)
    private async Task<RefreshTokenResult> CreateTokenInFamilyAsync(string userId, string familyId, bool rememberMe)
    {
        var db = redis.GetDatabase();
        var token = GenerateSecureToken();
        var expiresAtUtc = DateTime.UtcNow.Add(Ttl);

        await db.StringSetAsync(TokenKey(token), JsonSerializer.Serialize(new TokenEntry(userId, familyId)), Ttl);
        await db.StringSetAsync(FamilyKey(familyId), JsonSerializer.Serialize(new FamilyEntry(userId, token, rememberMe)), Ttl);

        return new RefreshTokenResult(token, expiresAtUtc, rememberMe);
    }

    private static async Task RevokeFamilyAsync(IDatabase db, string familyId, string currentToken)
    {
        await db.KeyDeleteAsync(FamilyKey(familyId));
        await db.KeyDeleteAsync(TokenKey(currentToken));
        // পুরনো superseded token গুলো নিজ TTL-এ মরবে; family নেই বলে সেগুলো দিয়ে rotate করলেও fail
    }

    // 256-bit cryptographically-secure token, URL-safe
    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
