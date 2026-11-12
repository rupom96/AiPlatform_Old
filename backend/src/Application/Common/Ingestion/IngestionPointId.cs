using System.Security.Cryptography;
using System.Text;

namespace AiPlatform.Application.Common.Ingestion;

// documentId + chunk index → deterministic Qdrant point-id (UUID)।
// একই doc আবার process হলে (at-least-once redelivery) হুবহু একই id → upsert overwrite করে,
// duplicate vector জমে না। এটাই idempotent processing-এর চাবি।
// Application-এ থাকে কারণ delete handler-ও একই id বানায় — ingest ও delete এক নিয়ম মানে,
// নইলে দুই জায়গায় hash-এর নিয়ম আলাদা হয়ে গেলে delete কিছুই খুঁজে পেত না।
public static class IngestionPointId
{
    public static string For(Guid documentId, int chunkIndex)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{documentId}:{chunkIndex}"));
        return new Guid(bytes).ToString();
    }

    // ঐ document-এর সব chunk-এর id। chunkCount = Document.TotalChunks।
    public static IEnumerable<string> ForDocument(Guid documentId, int chunkCount)
        => Enumerable.Range(0, Math.Max(0, chunkCount)).Select(i => For(documentId, i));
}
