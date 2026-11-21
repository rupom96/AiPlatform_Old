using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// Vector DB abstraction (Qdrant now; interface-এর পিছনে বলে পরে অন্য store swap করা যায়)।
public interface IVectorStore
{
    Task EnsureCollectionAsync(int dimension, CancellationToken cancellationToken);
    Task UpsertAsync(IEnumerable<VectorPoint> points, CancellationToken cancellationToken);
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(float[] query, int topN, CancellationToken cancellationToken);

    // collection-এর chunk-গুলো (vector ছাড়া, সর্বোচ্চ maxChunks) — শব্দ-ভিত্তিক (BM25) খোঁজার জন্য।
    // collection নেই = ফাঁকা তালিকা।
    Task<IReadOnlyList<RetrievedChunk>> ScrollAsync(int maxChunks, CancellationToken cancellationToken);

    // id ধরে মোছে। না-থাকা id উপেক্ষা করে (delete idempotent — retry নিরাপদ)।
    Task DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken);
}
