using AiPlatform.Application.Common.Models;
using AiPlatform.Domain.Entities;

namespace AiPlatform.Application.Common.Interfaces;

// audit trail write + read (tenant-scoped read query filter দিয়ে)।
public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AuditEntry> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}
