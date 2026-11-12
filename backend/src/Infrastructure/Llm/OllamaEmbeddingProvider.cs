using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Llm;

// Local embedding — Ollama native /api/embed (nomic-embed-text, 768-dim)। key লাগে না।
// taskType Ollama-তে নেই (query/document একই embed) — interface রক্ষা করতে param নেয়, ব্যবহার করে না।
public class OllamaEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptions<LlmSettings> options)
    : IEmbeddingProvider
{
    private readonly EmbeddingConfig _config = options.Value.Embedding;

    public async Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken cancellationToken)
    {
        var baseUrl = (_config.BaseUrl ?? "http://localhost:11434").TrimEnd('/');
        var body = new { model = _config.Model, input = text };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/embed")
        {
            Content = JsonContent.Create(body)
        };

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException("ollama-embedding", (int)response.StatusCode, raw);

        // /api/embed → { "embeddings": [[...]] } (batch shape; single input = প্রথম row)
        using var doc = LlmJson.Parse("ollama-embedding", raw, (int)response.StatusCode);
        var embeddings = doc.RootElement.GetProperty("embeddings");
        if (embeddings.GetArrayLength() == 0)
            throw new LlmProviderException("ollama-embedding", (int)response.StatusCode, "empty embeddings");

        var values = embeddings[0];
        var vector = new float[values.GetArrayLength()];
        var i = 0;
        foreach (var v in values.EnumerateArray())
            vector[i++] = v.GetSingle();

        return vector;
    }
}
