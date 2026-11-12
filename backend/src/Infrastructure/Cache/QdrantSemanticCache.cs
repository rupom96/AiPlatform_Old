using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure.Rag;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Cache;

// Qdrant-এ আলাদা collection দিয়ে semantic cache। cache fail = silently miss (request ভাঙে না)।
public class QdrantSemanticCache(
    IHttpClientFactory httpClientFactory,
    IOptions<QdrantSettings> qdrantOptions,
    IOptions<SemanticCacheSettings> cacheOptions,
    IOptions<Llm.LlmSettings> llmOptions) : ISemanticCache
{
    private readonly QdrantSettings _qdrant = qdrantOptions.Value;
    private readonly SemanticCacheSettings _settings = cacheOptions.Value;
    // embedding model-ভেদে cache collection-ও isolate (query embedding একই vector space-এ)
    private readonly string _collection =
        Rag.CollectionNaming.ForEmbedding(
            cacheOptions.Value.Collection, llmOptions.Value.Embedding.Provider, llmOptions.Value.Embedding.Model);

    public bool IsEnabled => _settings.Enabled;

    private HttpClient Http() => httpClientFactory.CreateClient("llm");
    private string CollectionUrl => $"{_qdrant.Url}/collections/{_collection}";

    public async Task<string?> GetAsync(float[] queryEmbedding, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled) return null;

        try
        {
            var body = new { query = queryEmbedding, limit = 1, with_payload = true };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{CollectionUrl}/points/query")
            {
                Content = JsonContent.Create(body),
            };
            using var response = await Http().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
                return null; // collection নেই বা cache down → miss

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            var points = doc.RootElement.GetProperty("result").GetProperty("points");
            if (points.GetArrayLength() == 0) return null;

            var top = points[0];
            if (top.GetProperty("score").GetDouble() < _settings.SimilarityThreshold)
                return null; // যথেষ্ট কাছাকাছি নয়

            return top.GetProperty("payload").GetProperty("answer").GetString();
        }
        catch
        {
            return null; // cache-ব্যর্থতা কখনো request ভাঙবে না
        }
    }

    public async Task SetAsync(float[] queryEmbedding, string answer, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled) return;

        try
        {
            await EnsureCollectionAsync(queryEmbedding.Length, cancellationToken);
            var body = new
            {
                points = new[]
                {
                    new { id = Guid.NewGuid().ToString(), vector = queryEmbedding, payload = new { answer } },
                },
            };
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{CollectionUrl}/points?wait=true")
            {
                Content = JsonContent.Create(body),
            };
            using var response = await Http().SendAsync(request, cancellationToken);
            _ = response; // best-effort
        }
        catch
        {
            /* cache set fail হলেও উত্তর user পেয়ে গেছে */
        }
    }

    private async Task EnsureCollectionAsync(int dimension, CancellationToken cancellationToken)
    {
        var http = Http();
        using var check = await http.GetAsync(CollectionUrl, cancellationToken);
        if (check.IsSuccessStatusCode) return;

        using var create = new HttpRequestMessage(HttpMethod.Put, CollectionUrl)
        {
            Content = JsonContent.Create(new { vectors = new { size = dimension, distance = "Cosine" } }),
        };
        using var response = await http.SendAsync(create, cancellationToken);
        _ = response;
    }
}
