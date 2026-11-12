using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Rag;

// keyword (lexical) + embedding (semantic) — দুই signal মিলিয়ে খোঁজে, তারপর Reciprocal Rank
// Fusion (RRF) দিয়ে merge + rerank। RRF র্যাঙ্ক-ভিত্তিক তাই দুই retriever-এর আলাদা score-scale
// (count vs cosine) normalize করতে হয় না — একটাতে উপরে থাকা doc fused list-এও উপরে ওঠে।
// ponytail: RRF র্যাঙ্ক-fusion যথেষ্ট rerank; দরকার হলে পরে cross-encoder reranker যোগ করা যাবে।
public class HybridRetriever(KeywordRetriever keyword, EmbeddingRetriever embedding) : IRetriever
{
    // RRF ধ্রুবক — বেশি rank-কে খুব বেশি শাস্তি না দিতে; 60 হলো paper/industry default।
    private const int RrfK = 60;

    public string Name => "hybrid";

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken)
    {
        // প্রতি source থেকে topN candidate; দুই list-এ থাকা doc RRF-এ boost পায়।
        var lists = await Task.WhenAll(
            keyword.RetrieveAsync(query, topN, cancellationToken),
            embedding.RetrieveAsync(query, topN, cancellationToken));

        return Fuse(lists, topN);
    }

    // Reciprocal Rank Fusion: Text-কে identity ধরে score = Σ 1/(k + rank); rank 1-based।
    // দুই list-এ থাকা doc দুইবার contribution পায় → উপরে ওঠে। pure → আলাদা testable।
    public static IReadOnlyList<RetrievedChunk> Fuse(
        IEnumerable<IReadOnlyList<RetrievedChunk>> lists, int topN, int k = RrfK)
    {
        var fused = new Dictionary<string, (RetrievedChunk Chunk, double Score)>();
        foreach (var list in lists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var chunk = list[rank];
                var contribution = 1.0 / (k + rank + 1);
                if (fused.TryGetValue(chunk.Text, out var existing))
                    fused[chunk.Text] = (existing.Chunk, existing.Score + contribution);
                else
                    fused[chunk.Text] = (chunk, contribution);
            }
        }

        return fused.Values
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .Select(x => x.Chunk with { Score = x.Score })
            .ToList();
    }
}
