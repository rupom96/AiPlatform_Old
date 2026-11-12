using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Rag;

// শব্দ-মিল দিয়ে খোঁজে (demo migrate)। প্রশ্নের শব্দ কতগুলো doc-এ আছে সেই count = score।
public class KeywordRetriever : IRetriever
{
    public string Name => "keyword";

    private static readonly char[] Separators = [' ', ',', '?', '.', '!', '।', '\n'];

    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken)
    {
        var queryWords = query
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .ToHashSet();

        IReadOnlyList<RetrievedChunk> ranked = KnowledgeBaseSeed.Documents
            .Select(doc => new RetrievedChunk(doc, queryWords.Count(w => doc.Contains(w)), "knowledge-base"))
            .Where(chunk => chunk.Score > 0)
            .OrderByDescending(chunk => chunk.Score)
            .Take(topN)
            .ToList();

        return Task.FromResult(ranked);
    }
}
