using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Auth;

// Google.Apis.Auth দিয়ে ID token যাচাই — signature (Google JWKS), expiry, audience।
public class GoogleIdTokenValidator(
    IOptions<GoogleAuthSettings> options,
    ILogger<GoogleIdTokenValidator> logger)
    : IGoogleIdTokenValidator
{
    private readonly GoogleAuthSettings _settings = options.Value;

    public async Task<Result<GoogleUserInfo>> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.ClientId))
            return Error.Validation("Auth.GoogleNotConfigured", "Google login is not configured on this server.");

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [_settings.ClientId] });

            // email_verified=false মানে Google-ও email-এর মালিকানা নিশ্চিত না — account বানানো যাবে না
            if (!payload.EmailVerified)
                return Error.Unauthorized("Auth.GoogleEmailUnverified", "Google account email is not verified.");

            return new GoogleUserInfo(payload.Email, payload.Name ?? payload.Email);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning("Google ID token validation failed: {Reason}", ex.Message);
            return Error.Unauthorized("Auth.InvalidGoogleToken", "Invalid Google token.");
        }
    }
}
