using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Agent;

// Singleton-safe: AsyncLocal per request — KnowledgeBaseSearchTool এখানে লেখে।
public class AgentRetrievalTrace : IAgentRetrievalTrace
{
    private static readonly AsyncLocal<IReadOnlyList<ScoredHit>?> Current = new();

    public IReadOnlyList<ScoredHit> DocumentHits => Current.Value ?? [];

    public void SetDocumentHits(IReadOnlyList<ScoredHit> hits) =>
        Current.Value = hits;

    public void Clear() => Current.Value = null;
}
