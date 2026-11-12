using AiPlatform.Application.Common.Interfaces;

namespace AiPlatform.Infrastructure.Rag;

// registered retriever গুলো নাম দিয়ে map করে; default keyword।
public class RetrieverResolver(IEnumerable<IRetriever> retrievers) : IRetrieverResolver
{
    private readonly Dictionary<string, IRetriever> _retrievers =
        retrievers.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

    public IRetriever? Resolve(string? method)
    {
        var name = string.IsNullOrWhiteSpace(method) ? "keyword" : method;
        return _retrievers.TryGetValue(name, out var retriever) ? retriever : null;
    }
}
