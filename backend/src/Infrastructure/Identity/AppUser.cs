using Microsoft.AspNetCore.Identity;

namespace AiPlatform.Infrastructure.Identity;

// Identity user. Persistence-tied বলে Infrastructure-এ (Domain pure থাকে)।
public class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // প্রতি signup = নিজস্ব tenant (org)। row-level isolation-এর ভিত্তি।
    public Guid TenantId { get; set; }
}
