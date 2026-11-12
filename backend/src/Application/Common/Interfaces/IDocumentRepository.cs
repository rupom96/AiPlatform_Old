using AiPlatform.Domain.Entities;

namespace AiPlatform.Application.Common.Interfaces;

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken);
    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken);

    // newest-first page + মোট count (soft-deleted বাদ — query filter)।
    Task<(IReadOnlyList<Document> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
