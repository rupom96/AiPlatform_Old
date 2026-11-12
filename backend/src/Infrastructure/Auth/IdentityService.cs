using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace AiPlatform.Infrastructure.Auth;

// Application-এর IIdentityService implement করে ASP.NET Core Identity দিয়ে।
public class IdentityService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager)
    : IIdentityService
{
    public const string DefaultRole = "User";

    public async Task<Result<string>> RegisterAsync(
        string email, string password, string fullName, CancellationToken cancellationToken)
    {
        var user = new AppUser { UserName = email, Email = email, FullName = fullName, TenantId = Guid.NewGuid() };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            // Identity error (duplicate email, weak password…) → validation failure
            var description = string.Join("; ", result.Errors.Select(e => e.Description));
            return Error.Validation("Auth.Registration", description);
        }

        await userManager.AddToRoleAsync(user, DefaultRole);
        return user.Id;
    }

    public async Task<Result<AuthUser>> ValidateCredentialsAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);

        // Generic message — "user নেই" আর "password ভুল" আলাদা করা যাবে না (enumeration রোধ)
        if (user is null)
            return Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password.");

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Error.Unauthorized("Auth.LockedOut", "Account locked due to failed attempts. Try again later.");
        if (!result.Succeeded)
            return Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password.");

        var roles = await userManager.GetRolesAsync(user);
        return new AuthUser(user.Id, user.Email!, user.FullName, roles.ToList(), user.TenantId);
    }

    public async Task<Result<AuthUser>> GetUserByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Error.NotFound("Auth.UserNotFound", "User not found.");

        var roles = await userManager.GetRolesAsync(user);
        return new AuthUser(user.Id, user.Email!, user.FullName, roles.ToList(), user.TenantId);
    }

    public async Task<string?> GeneratePasswordResetTokenAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : await userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<Result> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        // user নেই বা token খারাপ — একই generic message (enumeration রোধ)
        if (user is null)
            return Error.Validation("Auth.ResetPassword", "Invalid password reset request.");

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            var description = string.Join("; ", result.Errors.Select(e => e.Description));
            return Error.Validation("Auth.ResetPassword", description);
        }

        return Result.Success();
    }

    public async Task<Result<AuthUser>> GetOrCreateExternalUserAsync(
        string email, string fullName, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            var existingRoles = await userManager.GetRolesAsync(existing);
            return new AuthUser(existing.Id, existing.Email!, existing.FullName, existingRoles.ToList(), existing.TenantId);
        }

        // password-হীন account — Google-ই credential; email Google verify করেছে
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            TenantId = Guid.NewGuid(),
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            var description = string.Join("; ", result.Errors.Select(e => e.Description));
            return Error.Validation("Auth.ExternalLogin", description);
        }

        await userManager.AddToRoleAsync(user, DefaultRole);
        var roles = await userManager.GetRolesAsync(user);
        return new AuthUser(user.Id, user.Email!, user.FullName, roles.ToList(), user.TenantId);
    }
}
