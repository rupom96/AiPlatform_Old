using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Persistence;

public class DocumentRepository(AppDbContext dbContext) : IDocumentRepository
{
    public async Task AddAsync(Document document, CancellationToken cancellationToken) =>
        await dbContext.Documents.AddAsync(document, cancellationToken);

    public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Document> Items, int Total)> ListAsync(
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Documents.OrderByDescending(d => d.CreatedAtUtc);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
