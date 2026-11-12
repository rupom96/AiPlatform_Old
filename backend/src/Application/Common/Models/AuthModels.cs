namespace AiPlatform.Application.Common.Models;

// Token generation-এর জন্য দরকারি user তথ্য (Identity type Application-এ আসে না)।
public record AuthUser(string Id, string Email, string FullName, IReadOnlyList<string> Roles, Guid TenantId = default);

// Access token + কখন expire করবে।
public record AccessToken(string Token, DateTime ExpiresAtUtc);

// Refresh token + expiry (opaque string, JWT নয়)। RememberMe family-তে টিকে থাকে —
// rotate করলেও cookie persistent-না-session সিদ্ধান্তটা login-এর পছন্দই থাকে।
public record RefreshTokenResult(string Token, DateTime ExpiresAtUtc, bool RememberMe);

// Rotate-এর ফল: কার token + নতুন refresh token।
public record RefreshRotation(string UserId, RefreshTokenResult NewToken);

// Google ID token যাচাইয়ের ফল — token বৈধ হলে এই user তথ্য।
public record GoogleUserInfo(string Email, string FullName);

// Login-এর পূর্ণ ফল — controller access token body-তে দেয়, refresh cookie-তে বসায়।
public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    bool RememberMe);
