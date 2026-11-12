namespace AiPlatform.Application.Features.Documents;

public record DocumentStatusResponse(
    Guid Id,
    string FileName,
    string Status,
    int TotalChunks,
    int ProcessedChunks,
    string? Error);
