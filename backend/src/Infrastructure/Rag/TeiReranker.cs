using System.Net;
using System.Net.Http.Json;
using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Rag;

// HuggingFace TEI-র POST /rerank {query, texts[]} → [{index, score}] (score ০–১)। model = bge-reranker-v2-m3।
public class TeiReranker(IHttpClientFactory httpClientFactory, IOptions<RerankerSettings> options) : IReranker
{
    private const string Provider = "reranker";
    private readonly RerankerSettings _settings = options.Value;

    public async Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query, IReadOnlyList<RetrievedChunk> candidates, int topN, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
            throw new InvalidOperationException("Reranker is disabled (Reranker:Enabled=false).");
        if (candidates.Count == 0)
            return [];

        // নিজস্ব timeout — ধীর/ঝুলে-থাকা reranker যেন chat আটকে না রাখে।
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/rerank")
        {
            // truncate = লম্বা chunk model-এর সীমায় কাটে (error নয়)
            Content = JsonContent.Create(new { query, texts = candidates.Select(c => c.Text).ToArray(), truncate = true })
        };

        string raw;
        HttpStatusCode status;
        try
        {
            using var response = await httpClientFactory.CreateClient("llm").SendAsync(request, timeout.Token);
            status = response.StatusCode;
            raw = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmProviderException(Provider, 504, $"reranker did not answer within {_settings.TimeoutSeconds}s");
        }

        if ((int)status is < 200 or >= 300)
            throw new LlmProviderException(Provider, (int)status, raw);

        using var doc = LlmJson.Parse(Provider, raw, (int)status);
        var ranked = new List<(int Index, double Score)>();
        foreach (var item in doc.RootElement.EnumerateArray())
            ranked.Add((item.GetProperty("index").GetInt32(), item.GetProperty("score").GetDouble()));

        return ranked
            .Where(r => r.Index >= 0 && r.Index < candidates.Count)
            .OrderByDescending(r => r.Score)
            .Take(Math.Max(1, topN))
            .Select(r => candidates[r.Index] with { Score = r.Score })
            .ToList();
    }
}
