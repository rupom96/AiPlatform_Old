using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Persistence;

// audit entry EF দিয়ে persist + read। read tenant-scoped (DbContext query filter)।
public class AuditLog(AppDbContext dbContext) : IAuditLog
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        dbContext.AuditEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<AuditEntry> Items, int Total)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.AuditEntries.OrderByDescending(a => a.Id);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
