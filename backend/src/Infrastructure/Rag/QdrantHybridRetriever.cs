using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Rag;

// vector (অর্থ) + BM25 (শব্দ) — দুটোই আপলোড করা ডকুমেন্টের Qdrant chunk-এর উপর, তারপর Reciprocal Rank Fusion।
// (পুরোনো "hybrid" শুধু কোডে লেখা seed নোট দেখে — আপলোড করা ফাইল দেখে না; এটা দেখে।)
public class QdrantHybridRetriever(
    QdrantRetriever vector,
    IVectorStore vectorStore,
    IOptions<RerankerSettings> options) : IRetriever
{
    // BM25 পুরো collection পড়ে (vector DB-তে শব্দ-সূচি নেই); বড় collection-এ খরচ ঠেকাতে সীমা।
    // ponytail: হাজার-হাজার chunk ছাড়ালে Qdrant-এর sparse vector / full-text index-এ যাওয়া উচিত।
    private const int MaxScrollChunks = 2000;

    public string Name => RetrievalMethods.Hybrid;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken)
    {
        // candidate সংখ্যা: reranker-এর সাথে একই (Candidates), যাতে hybrid আর hybrid+rerank সমান পুল থেকে খোঁজে।
        var pool = Math.Max(topN, options.Value.Candidates);

        // আগে vector (সে-ই seed index নিশ্চিত করে), তারপর শব্দ-খোঁজা।
        var semantic = await vector.RetrieveAsync(query, pool, cancellationToken);
        var corpus = await vectorStore.ScrollAsync(MaxScrollChunks, cancellationToken);
        var lexical = Bm25Ranker.Rank(query, corpus, pool);

        // Fuse: দুই তালিকায় থাকা chunk উপরে ওঠে; প্রথম-দেখা কপি (vector-এরটা) থাকে বলে VectorScore অক্ষত।
        return HybridRetriever.Fuse([semantic, lexical], topN);
    }
}
