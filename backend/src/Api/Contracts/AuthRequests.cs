namespace AiPlatform.Api.Contracts;

// API-এর ইনপুট shape (Application command থেকে আলাদা রাখা — decoupled contract)।
public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password, bool RememberMe = false);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
public record GoogleLoginRequest(string IdToken);
