namespace AiPlatform.Application.Common.Interfaces;

// বর্তমান request-এর user + tenant (JWT claim থেকে)।
// HTTP context নেই (background worker) হলে TenantId = Guid.Empty = system/unscoped (সব tenant দেখে)।
public interface ICurrentUser
{
    string? UserId { get; }
    Guid TenantId { get; }
}
