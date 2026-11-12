using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// এক agent request-এর doc retrieval স্কোর জমা (KB tool → handler → response)।
public interface IAgentRetrievalTrace
{
    void Clear();
    void SetDocumentHits(IReadOnlyList<ScoredHit> hits);
    IReadOnlyList<ScoredHit> DocumentHits { get; }
}
