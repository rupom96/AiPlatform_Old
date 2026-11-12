using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;

namespace AiPlatform.Application.Common.Interfaces;

// Google Sign-In-এর ID token (JWT) যাচাই — signature, expiry, audience (আমাদের ClientId)।
// Impl Infrastructure-এ (Google.Apis.Auth); Application শুধু ফলাফল চেনে।
public interface IGoogleIdTokenValidator
{
    Task<Result<GoogleUserInfo>> ValidateAsync(string idToken, CancellationToken cancellationToken);
}
