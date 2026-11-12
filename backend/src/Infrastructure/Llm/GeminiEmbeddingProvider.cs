using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Llm;

// Gemini embedding (gemini-embedding-001)। Phase 3 RAG এটা ব্যবহার করবে।
public class GeminiEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptions<LlmSettings> options)
    : IEmbeddingProvider
{
    private readonly EmbeddingConfig _config = options.Value.Embedding;

    public async Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.Model}:embedContent";
        var body = new
        {
            taskType = taskType == EmbeddingTaskType.RetrievalQuery ? "RETRIEVAL_QUERY" : "RETRIEVAL_DOCUMENT",
            content = new { parts = new[] { new { text } } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", _config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException("gemini-embedding", (int)response.StatusCode, raw);

        using var doc = LlmJson.Parse("gemini-embedding", raw, (int)response.StatusCode);
        var values = doc.RootElement.GetProperty("embedding").GetProperty("values");
        var vector = new float[values.GetArrayLength()];
        var i = 0;
        foreach (var v in values.EnumerateArray())
            vector[i++] = v.GetSingle();

        return vector;
    }
}
