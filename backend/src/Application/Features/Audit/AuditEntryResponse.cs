namespace AiPlatform.Application.Features.Audit;

public record AuditEntryResponse(long Id, string? UserId, string Action, bool Succeeded, DateTime CreatedAtUtc);
