namespace AiPlatform.Domain.Entities;

public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

// আপলোড করা document — chunk→embed→Qdrant ingestion pipeline এর status track করে।
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }   // কোন tenant-এর (row-level isolation)
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public int TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // soft-delete — সারি মুছি না, flag করি (query filter auto-hide করে; audit/undo সম্ভব)।
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
