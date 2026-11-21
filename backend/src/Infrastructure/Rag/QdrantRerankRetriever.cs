using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Rag;

// vector search থেকে অনেকগুলো candidate (Candidates, default ২০) → cross-encoder reranker প্রশ্ন+chunk একসাথে পড়ে
// নতুন করে সাজায় → সেরা topN। "কাছাকাছি-কিন্তু-ভুল" chunk ("নোটিশ" শব্দের কারণে Grounding Rules) নিচে নামে।
public class QdrantRerankRetriever(
    QdrantRetriever vector,
    IReranker reranker,
    IOptions<RerankerSettings> options) : IRetriever
{
    public string Name => RetrievalMethods.Rerank;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken)
    {
        var candidates = await vector.RetrieveAsync(query, Math.Max(topN, options.Value.Candidates), cancellationToken);
        return await reranker.RerankAsync(query, candidates, topN, cancellationToken);
    }
}

// hybrid (vector + BM25) candidate → reranker। সবচেয়ে বিস্তৃত পুল + সবচেয়ে নির্ভুল ক্রম।
public class QdrantHybridRerankRetriever(
    QdrantHybridRetriever hybrid,
    IReranker reranker,
    IOptions<RerankerSettings> options) : IRetriever
{
    public string Name => RetrievalMethods.HybridRerank;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken)
    {
        var candidates = await hybrid.RetrieveAsync(query, Math.Max(topN, options.Value.Candidates), cancellationToken);
        return await reranker.RerankAsync(query, candidates, topN, cancellationToken);
    }
}
