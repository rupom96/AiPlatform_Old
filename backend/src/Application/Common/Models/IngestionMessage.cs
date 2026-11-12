namespace AiPlatform.Application.Common.Models;

// queue থেকে পড়া এক ingestion কাজ। MessageId = stream entry id (ack করতে লাগে)।
public record IngestionMessage(string MessageId, Guid DocumentId);
