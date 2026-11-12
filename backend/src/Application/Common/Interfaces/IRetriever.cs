using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// এক retrieval কৌশল (keyword; পরে embedding, chunking, Qdrant)। নতুন কৌশল = নতুন impl।
public interface IRetriever
{
    string Name { get; }
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken);
}
