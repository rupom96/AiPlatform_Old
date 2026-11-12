using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AiPlatform.Infrastructure.Persistence;

// Startup-এ base role গুলো নিশ্চিত করে (idempotent — বারবার চললেও নিরাপদ)।
public static class IdentitySeeder
{
    private static readonly string[] Roles = ["User", "Admin"];

    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}
