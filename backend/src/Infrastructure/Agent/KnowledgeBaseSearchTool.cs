using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Agent;

// ⚠️ APP-SPECIFIC — uploaded documents (Qdrant) + company KB seed.
// Unified chat: agent একসাথে get_sales ইত্যাদি + এই tool ডেকে এক উত্তর দিতে পারে.
public class KnowledgeBaseSearchTool(
    IRetrieverResolver retrieverResolver,
    IAgentRetrievalTrace retrievalTrace) : ITool
{
    public string Name => "search_knowledge_base";
    public string Description =>
        "Searches uploaded documents and the company knowledge base (policies: return, delivery, " +
        "warranty process, payment, support). Returns relevant text passages with sources. " +
        "Use for document, policy, or attachment questions — NOT for live sales/stock numbers. " +
        "For sales totals or top products use get_sales; for warehouse quantities use get_stock; " +
        "for a product/serial warranty end-date use get_warranty. " +
        "You may call this tool together with ERP tools in the same turn, then combine results " +
        "into one Bengali answer. " +
        "Prefer the user's wording in the query; Bengali queries match Bengali documents best.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "Search query — reuse the user's wording when possible."
            }
        },
        required = new[] { "query" }
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(query))
            return "No query provided.";

        const int TraceTopN = 8;
        const int LlmTopN = 3;

        IReadOnlyList<RetrievedChunk> chunks = [];
        string methodUsed = "";

        foreach (var method in new[] { "qdrant", "hybrid", "keyword" })
        {
            chunks = await TryRetrieveAsync(method, query, TraceTopN, cancellationToken);
            if (chunks.Count > 0)
            {
                methodUsed = method;
                break;
            }
        }

        if (chunks.Count == 0)
        {
            retrievalTrace.SetDocumentHits([]);
            return "No relevant information found in documents or knowledge base.";
        }

        retrievalTrace.SetDocumentHits(
            chunks.Select(c =>
            {
                var src = string.IsNullOrWhiteSpace(c.Source) ? "knowledge-base" : c.Source!;
                var label = c.Text.Length <= 80 ? c.Text : c.Text[..80] + "…";
                return new ScoredHit(label, c.Score ?? 0, $"{methodUsed}:{src}");
            }).ToList());

        var forLlm = chunks.Take(LlmTopN).ToList();
        return string.Join("\n\n", forLlm.Select((c, i) =>
        {
            var src = string.IsNullOrWhiteSpace(c.Source) ? "knowledge-base" : c.Source;
            var score = c.Score is null ? "" : $" score={c.Score:F3}";
            return $"[{i + 1}] ({src}{score})\n{c.Text}";
        }));
    }

    private async Task<IReadOnlyList<RetrievedChunk>> TryRetrieveAsync(
        string method, string query, int topN, CancellationToken cancellationToken)
    {
        var retriever = retrieverResolver.Resolve(method);
        if (retriever is null)
            return [];

        try
        {
            return await retriever.RetrieveAsync(query, topN, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }
}
