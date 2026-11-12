namespace AiPlatform.Domain.Entities;

// append-only audit trail: কোন tenant-এর কোন user কী action করল, সফল হলো কি না, কখন।
public class AuditEntry
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
