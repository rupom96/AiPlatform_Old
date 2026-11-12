using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Llm;

// Google Gemini — নিজস্ব request format (contents + systemInstruction)।
public class GeminiChatProvider(IHttpClientFactory httpClientFactory, string name, ProviderConfig config)
    : IChatProvider
{
    public string Name => name;

    public async Task<ChatResult> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent";

        var contents = messages
            .Where(m => m.Role != "system")
            .Select(m => new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            })
            .ToArray();

        var systemText = messages.FirstOrDefault(m => m.Role == "system")?.Content;
        object body = systemText is null
            ? new { contents }
            : new { contents, systemInstruction = new { parts = new[] { new { text = systemText } } } };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, raw);

        using var doc = LlmJson.Parse(Name, raw, (int)response.StatusCode);
        var text = doc.RootElement
            .GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;

        var (inputTokens, outputTokens) = ReadUsage(doc.RootElement);
        return new ChatResult(text, Name, config.Model, inputTokens, outputTokens);
    }

    private static (int input, int output) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
            return (0, 0);
        var input = usage.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
        var output = usage.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
        return (input, output);
    }

    public async Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent";
        var contents = messages
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role == "assistant" ? "model" : "user", parts = new[] { new { text = m.Content } } })
            .ToArray();
        var systemText = messages.FirstOrDefault(m => m.Role == "system")?.Content;

        // responseMimeType = application/json → JSON বাধ্যতামূলক
        object body = systemText is null
            ? new { contents, generationConfig = new { responseMimeType = "application/json" } }
            : new { contents, systemInstruction = new { parts = new[] { new { text = systemText } } }, generationConfig = new { responseMimeType = "application/json" } };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, raw);

        using var doc = LlmJson.Parse(Name, raw, (int)response.StatusCode);
        return doc.RootElement
            .GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "{}";
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:streamGenerateContent?alt=sse";
        var contents = messages
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role == "assistant" ? "model" : "user", parts = new[] { new { text = m.Content } } })
            .ToArray();
        var systemText = messages.FirstOrDefault(m => m.Role == "system")?.Content;
        object body = systemText is null
            ? new { contents }
            : new { contents, systemInstruction = new { parts = new[] { new { text = systemText } } } };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", config.ApiKey);
        request.Content = JsonContent.Create(body);

        var http = httpClientFactory.CreateClient("llm");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new LlmProviderException(Name, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var json = line["data:".Length..].Trim();
            if (json.Length == 0) continue;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }
}
