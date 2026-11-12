using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;

namespace AiPlatform.Application.Common.Interfaces;

// Identity (UserManager/SignInManager) Infrastructure-এ থাকে; Application শুধু এই interface চেনে।
public interface IIdentityService
{
    // নতুন account বানায়; সফল হলে userId ফেরত।
    Task<Result<string>> RegisterAsync(string email, string password, string fullName, CancellationToken cancellationToken);

    // credential যাচাই (lockout সহ); সফল হলে token বানানোর মতো user তথ্য।
    Task<Result<AuthUser>> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken);

    // refresh-এর সময় নতুন access token বানাতে current user তথ্য (roles সহ)।
    Task<Result<AuthUser>> GetUserByIdAsync(string userId, CancellationToken cancellationToken);

    // forgot-password: user থাকলে reset token, না থাকলে null (caller enumeration লুকায়)।
    Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken);

    // reset-password: token দিয়ে নতুন password সেট।
    Task<Result> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken);

    // external login (Google): email-এ account থাকলে সেটা, নইলে password-হীন নতুন account।
    Task<Result<AuthUser>> GetOrCreateExternalUserAsync(string email, string fullName, CancellationToken cancellationToken);
}
