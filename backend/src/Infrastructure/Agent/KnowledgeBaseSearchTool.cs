using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace AiPlatform.Infrastructure.Agent;

// ⚠️ APP-SPECIFIC — uploaded documents (Qdrant) + company KB seed.
// Unified chat: agent একসাথে get_sales ইত্যাদি + এই tool ডেকে এক উত্তর দিতে পারে.
public class KnowledgeBaseSearchTool(
    IRetrieverResolver retrieverResolver,
    IAgentRetrievalTrace retrievalTrace,
    ILogger<KnowledgeBaseSearchTool>? logger = null) : ITool
{
    private const string PassagesHeader =
        "Passages below are ordered best match first and may be in English or Bengali. " +
        "If any passage answers the question, answer from it in Bengali (translate if needed); " +
        "say there is no information only if none of them is relevant.\n\n";

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
        // ৩ থেকে ৫: dense search-এ সঠিক chunk কখনো ৪র্থ হয় ("নোটিশ পিরিয়ড" প্রশ্নে "Grounding Rules" উপরে উঠত)।
        // প্রতিটা chunk ~৫০০ অক্ষর, তাই prompt-এর খরচ সামান্য।
        const int LlmTopN = 5;

        IReadOnlyList<RetrievedChunk> chunks = [];
        string methodUsed = "";

        foreach (var method in MethodsToTry())
        {
            chunks = await TryRetrieveAsync(method, query, TraceTopN, cancellationToken);
            if (chunks.Count > 0)
            {
                methodUsed = method;
                break;
            }
        }

        // একই ফাইল দুবার আপলোড হলে হুবহু একই chunk একাধিকবার আসে এবং top-N দখল করে বাকি তথ্য (যেমন বেতনের section)
        // বের করে দেয়। তাই লেখা-ভিত্তিক dedupe; সবচেয়ে উপরের (সেরা score) কপিটা থাকে।
        chunks = chunks.DistinctBy(c => c.Text).ToList();

        retrievalTrace.SetMethodUsed(chunks.Count > 0 ? methodUsed : null);

        if (chunks.Count == 0)
        {
            retrievalTrace.SetDocumentHits([]);
            retrievalTrace.SetBestVectorScore(null);
            retrievalTrace.SetBestRerankScore(null);
            return "No relevant information found in documents or knowledge base.";
        }

        retrievalTrace.SetDocumentHits(
            chunks.Select(c =>
            {
                var src = string.IsNullOrWhiteSpace(c.Source) ? "knowledge-base" : c.Source!;
                var label = c.Text.Length <= 80 ? c.Text : c.Text[..80] + "…";
                return new ScoredHit(label, c.Score ?? 0, $"{methodUsed}:{src}");
            }).ToList());
        // hybrid/rerank-এ Score বদলে যায়; "প্রশ্ন ডকুমেন্টের কিনা" gate-এর জন্য মূল vector cosine আলাদা রাখি।
        retrievalTrace.SetBestVectorScore(chunks.Where(c => c.VectorScore is not null).Max(c => c.VectorScore));
        // rerank পদ্ধতিতে Score = reranker relevance — gate-এর সেরা সংকেত। অন্য পদ্ধতিতে Score অন্য মাপের, তাই null।
        retrievalTrace.SetBestRerankScore(IsRerankMethod(methodUsed) ? chunks.Max(c => c.Score) : null);

        var forLlm = chunks.Take(LlmTopN).ToList();
        // ছোট model ইংরেজি passage দেখে বা একাধিক passage-এ দ্বিধায় পড়ে "তথ্য নেই" বলত, যদিও প্রথম passage-এই উত্তর ছিল।
        return PassagesHeader + string.Join("\n\n", forLlm.Select((c, i) =>
        {
            var src = string.IsNullOrWhiteSpace(c.Source) ? "knowledge-base" : c.Source;
            var score = c.Score is null ? "" : $" score={c.Score:F3}";
            return $"[{i + 1}] ({src}{score})\n{c.Text}";
        }));
    }

    private static bool IsRerankMethod(string method) =>
        method.Equals(RetrievalMethods.Rerank, StringComparison.OrdinalIgnoreCase)
        || method.Equals(RetrievalMethods.HybridRerank, StringComparison.OrdinalIgnoreCase);

    // user কোনো পদ্ধতি বেছে থাকলে সেটা আগে; ব্যর্থ/ফাঁকা হলে আগের ক্রম (qdrant → hybrid → keyword) — তাই বেছে-নেওয়া
    // পদ্ধতি (যেমন rerank, reranker বন্ধ) ব্যর্থ হলে উত্তর থামে না, আর UI-তে hit-এর লেবেলে আসল পদ্ধতির নাম দেখা যায়।
    private IEnumerable<string> MethodsToTry()
    {
        var requested = retrievalTrace.RequestedMethod;
        if (!RetrievalMethods.IsDefault(requested))
            yield return requested!;

        foreach (var method in new[] { RetrievalMethods.Vector, "hybrid", "keyword" })
            if (!method.Equals(requested, StringComparison.OrdinalIgnoreCase))
                yield return method;
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
            // পরের পদ্ধতিতে ফেরা স্বাভাবিক (যেমন reranker বন্ধ), কিন্তু কারণটা লগে থাকা চাই — নইলে "rerank চলছে" ভেবে ভুল হয়।
            logger?.LogWarning(ex, "Retrieval method {Method} failed; falling back to the next method", method);
            return [];
        }
    }
}
