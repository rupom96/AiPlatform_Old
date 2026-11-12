using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// Tool descriptions → আলাদা Qdrant collection। অনেক tool হলে prompt-এ শুধু relevant subset যায়।
// Source of truth এখনও ITool কোড; vector DB শুধু retrieval index।
public interface IToolCatalog
{
    Task SyncAsync(IReadOnlyCollection<ITool> tools, CancellationToken cancellationToken);

    // প্রশ্নের সাথে মিলিয়ে top-k tool বাছে। retrieval বন্ধ/ফেল → সব tool ফেরায় (safe fallback)।
    Task<IReadOnlyList<ITool>> SelectForPromptAsync(
        IReadOnlyList<ITool> tools,
        string question,
        CancellationToken cancellationToken);

    // UI debug: tools collection থেকে top-N score (filter skip হলেও)।
    Task<IReadOnlyList<ScoredHit>> SearchScoresAsync(
        string question,
        int topN,
        CancellationToken cancellationToken);
}
