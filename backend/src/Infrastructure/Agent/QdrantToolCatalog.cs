using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Features.Agent;
using AiPlatform.Infrastructure.Llm;
using AiPlatform.Infrastructure.Rag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Agent;

// Tool cards → Qdrant collection (docs RAG collection থেকে আলাদা)।
public class QdrantToolCatalog(
    IHttpClientFactory httpClientFactory,
    IEmbeddingProvider embeddingProvider,
    AgentSettings agentSettings,
    IOptions<QdrantSettings> qdrantOptions,
    IOptions<LlmSettings> llmOptions,
    ILogger<QdrantToolCatalog> logger) : IToolCatalog
{
    private readonly QdrantSettings _qdrant = qdrantOptions.Value;
    private readonly string _collection = CollectionNaming.ForEmbedding(
        agentSettings.ToolsCollection,
        llmOptions.Value.Embedding.Provider,
        llmOptions.Value.Embedding.Model);

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private string? _syncedFingerprint;

    private HttpClient Http() => httpClientFactory.CreateClient("llm");
    private string CollectionUrl => $"{_qdrant.Url}/collections/{_collection}";

    public async Task SyncAsync(IReadOnlyCollection<ITool> tools, CancellationToken cancellationToken)
    {
        if (!agentSettings.ToolRetrievalEnabled || tools.Count == 0)
            return;

        var fingerprint = string.Join('|', tools
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => $"{t.Name}\u001f{t.Description}"));
        if (fingerprint == _syncedFingerprint)
            return;

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            if (fingerprint == _syncedFingerprint)
                return;

            var points = new List<object>();
            int? dimension = null;
            foreach (var tool in tools)
            {
                var text = $"{tool.Name}: {tool.Description}";
                var vector = await embeddingProvider.EmbedAsync(
                    text, EmbeddingTaskType.RetrievalDocument, cancellationToken);
                dimension ??= vector.Length;
                points.Add(new
                {
                    id = ToolPointId.For(tool.Name),
                    vector,
                    payload = new { toolName = tool.Name, text },
                });
            }

            await EnsureCollectionAsync(dimension!.Value, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{CollectionUrl}/points?wait=true")
            {
                Content = JsonContent.Create(new { points }),
            };
            using var response = await Http().SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            _syncedFingerprint = fingerprint;
            logger.LogInformation("Tool catalog synced {Count} tools → Qdrant collection {Collection}",
                tools.Count, _collection);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool catalog sync failed for collection {Collection}", _collection);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<IReadOnlyList<ITool>> SelectForPromptAsync(
        IReadOnlyList<ITool> tools,
        string question,
        CancellationToken cancellationToken)
    {
        if (!agentSettings.ToolRetrievalEnabled || tools.Count == 0)
            return tools;

        var topK = Math.Max(1, agentSettings.ToolTopK);
        if (tools.Count <= topK)
            return tools;

        var hits = await SearchScoresAsync(question, topK, cancellationToken);
        if (hits.Count == 0)
            return tools;

        var byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new List<ITool>();
        foreach (var hit in hits)
        {
            if (byName.TryGetValue(hit.Label, out var tool))
                selected.Add(tool);
        }

        return selected.Count > 0 ? selected : tools;
    }

    public async Task<IReadOnlyList<ScoredHit>> SearchScoresAsync(
        string question,
        int topN,
        CancellationToken cancellationToken)
    {
        if (!agentSettings.ToolRetrievalEnabled || string.IsNullOrWhiteSpace(question) || topN <= 0)
            return [];

        try
        {
            var query = await embeddingProvider.EmbedAsync(
                question, EmbeddingTaskType.RetrievalQuery, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{CollectionUrl}/points/query")
            {
                Content = JsonContent.Create(new { query, limit = topN, with_payload = true }),
            };
            using var response = await Http().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
                return [];

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            var points = doc.RootElement.GetProperty("result").GetProperty("points");

            var hits = new List<ScoredHit>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in points.EnumerateArray())
            {
                if (!point.TryGetProperty("payload", out var payload))
                    continue;
                var name = payload.TryGetProperty("toolName", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;
                var score = point.TryGetProperty("score", out var s) ? s.GetDouble() : 0;
                hits.Add(new ScoredHit(name, score, _collection));
            }

            return hits;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool score search failed for {Collection}", _collection);
            return [];
        }
    }

    private async Task EnsureCollectionAsync(int dimension, CancellationToken cancellationToken)
    {
        var http = Http();
        using var check = await http.GetAsync(CollectionUrl, cancellationToken);
        if (check.IsSuccessStatusCode)
            return;

        using var create = new HttpRequestMessage(HttpMethod.Put, CollectionUrl)
        {
            Content = JsonContent.Create(new { vectors = new { size = dimension, distance = "Cosine" } }),
        };
        using var response = await http.SendAsync(create, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
